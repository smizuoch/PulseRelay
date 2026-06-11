using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRelay.App.Settings;

namespace PulseRelay.App;

/// <summary>
/// Keeps a <see cref="BridgeSession"/> alive according to user intent. While started, any
/// drop (initial connect failure, mid-session disconnect, or prolonged sample silence) leads
/// to a fresh connect attempt through the source factory with backoff; Stop cancels
/// everything and nothing retries.
/// </summary>
/// <remarks>
/// Like the session, <see cref="SnapshotChanged"/> fires on arbitrary threads; UI consumers
/// marshal to their dispatcher. <see cref="TimeProvider"/> is injected so tests can drive
/// backoff and the stale watchdog deterministically.
/// </remarks>
public sealed class BridgeSupervisor : IAsyncDisposable
{
    private readonly BridgeSession _session;
    private readonly TimeProvider _timeProvider;
    private readonly BridgeSupervisorOptions _options;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private SupervisorSnapshot _snapshot = SupervisorSnapshot.Initial;
    private Task? _loop;
    private CancellationTokenSource? _loopCts;
    private CancellationTokenSource? _delayCts;
    private TaskCompletionSource? _dropSignal;
    private bool _streamedThisConnection;

    public BridgeSupervisor(
        BridgeSession session,
        TimeProvider? timeProvider = null,
        BridgeSupervisorOptions? options = null,
        ILogger<BridgeSupervisor>? logger = null)
    {
        _session = session;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options ?? new BridgeSupervisorOptions();
        _logger = logger ?? NullLogger<BridgeSupervisor>.Instance;
        _session.SnapshotChanged += OnSessionSnapshotChanged;
    }

    public SupervisorSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public event EventHandler<SupervisorSnapshot>? SnapshotChanged;

    public bool SupportsBle => _session.SupportsBle;

    public TimeSpan StaleThreshold => _session.StaleThreshold;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _loopCts is not null;
            }
        }
    }

    /// <summary>Declares the intent to run and starts the supervision loop. Idempotent.</summary>
    public void Start(AppSettings settings)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_loopCts is not null)
            {
                return;
            }

            cts = _loopCts = new CancellationTokenSource();
        }

        _logger.LogInformation("Bridge started by user");
        Update(s => SupervisorSnapshot.Initial with { Session = s.Session, RunState = BridgeRunState.Running });

        var loop = RunAsync(settings, cts.Token);
        lock (_gate)
        {
            _loop = loop;
        }
    }

    /// <summary>
    /// Declares the intent to stop: cancels any pending backoff, stops the source cleanly,
    /// and guarantees no further retries. Safe to call when not running.
    /// </summary>
    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        CancellationTokenSource? delay;
        Task? loop;
        lock (_gate)
        {
            cts = _loopCts;
            delay = _delayCts;
            loop = _loop;
            _loopCts = null;
            _delayCts = null;
            _loop = null;
        }

        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        delay?.Cancel();
        if (loop is not null)
        {
            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _session.DisconnectAsync();
        Update(_ => SupervisorSnapshot.Initial);
        cts.Dispose();
        _logger.LogInformation("Bridge stopped by user");
    }

    /// <summary>
    /// Skips the pending backoff wait so the next attempt starts immediately, and resets the
    /// backoff sequence. No-op when no retry is pending.
    /// </summary>
    public void RequestReconnectNow()
    {
        CancellationTokenSource? delay;
        lock (_gate)
        {
            delay = _delayCts;
        }

        if (delay is not null)
        {
            _logger.LogInformation("Manual reconnect requested - skipping backoff");
            delay.Cancel();
        }
    }

    public bool TrySetOscEnabled(bool enabled, AppSettings settings, out string? error) =>
        _session.TrySetOscEnabled(enabled, settings, out error);

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _session.SnapshotChanged -= OnSessionSnapshotChanged;
    }

    private async Task RunAsync(AppSettings settings, CancellationToken ct)
    {
        int failedAttempts = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Clean slate: tears down any previous source so every attempt gets a fresh
                // one from the factory (BLE addresses are session-scoped RPAs, never reused).
                await _session.DisconnectAsync();
                ct.ThrowIfCancellationRequested();

                var drop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_gate)
                {
                    _dropSignal = drop;
                    _streamedThisConnection = false;
                }

                bool connected = await _session.ConnectAsync(settings, ct);
                ct.ThrowIfCancellationRequested();

                if (connected)
                {
                    Update(s => s with { RunState = BridgeRunState.Running, NextRetryAt = null });
                    await WaitForDropAsync(drop, ct);

                    bool streamed;
                    lock (_gate)
                    {
                        streamed = _streamedThisConnection;
                        _dropSignal = null;
                    }

                    if (streamed)
                    {
                        failedAttempts = 0;
                    }
                }

                failedAttempts++;
                var delay = _options.Backoff[Math.Min(failedAttempts - 1, _options.Backoff.Count - 1)];
                var nextAt = _timeProvider.GetUtcNow() + delay;
                _logger.LogInformation(
                    "Reconnect attempt {Attempt} in {Delay}s", failedAttempts, delay.TotalSeconds);

                // Register the delay timer before announcing it: once NextRetryAt is visible,
                // the wait must already be skippable and (in tests) advanceable.
                var (delayTask, delayCts) = BeginDelay(delay, ct);
                Update(s => s with
                {
                    RunState = BridgeRunState.Reconnecting,
                    RetryAttempt = failedAttempts,
                    NextRetryAt = nextAt,
                });

                bool skipped = await AwaitDelayAsync(delayTask, delayCts, ct);
                if (skipped)
                {
                    failedAttempts = 0;
                }

                Update(s => s with { NextRetryAt = null });
            }
        }
        catch (OperationCanceledException)
        {
            // Stop requested; StopAsync owns the final cleanup and snapshot reset.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Supervision loop crashed; bridge halted");
            Update(s => s with
            {
                RunState = BridgeRunState.Stopped,
                Session = s.Session with
                {
                    Status = BridgeStatus.Failed,
                    LastError = ex.Message,
                    FailureKind = BridgeFailureKind.Unknown,
                },
            });
        }
    }

    private async Task WaitForDropAsync(TaskCompletionSource drop, CancellationToken ct)
    {
        using var watchdog = _timeProvider.CreateTimer(
            _ =>
            {
                var session = _session.Snapshot;
                if (session.Status == BridgeStatus.Streaming
                    && session.LastSampleAt is { } last
                    && _timeProvider.GetUtcNow() - last > _options.StaleReconnectThreshold)
                {
                    _logger.LogWarning(
                        "No samples for over {Seconds}s while streaming - forcing reconnect",
                        _options.StaleReconnectThreshold.TotalSeconds);
                    drop.TrySetResult();
                }
            },
            state: null,
            _options.WatchdogInterval,
            _options.WatchdogInterval);

        await drop.Task.WaitAsync(ct);
    }

    private (Task Delay, CancellationTokenSource Cts) BeginDelay(TimeSpan delay, CancellationToken ct)
    {
        var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_gate)
        {
            _delayCts = delayCts;
        }

        return (Task.Delay(delay, _timeProvider, delayCts.Token), delayCts);
    }

    /// <summary>Waits out a backoff delay. Returns true when skipped by a manual reconnect.</summary>
    private async Task<bool> AwaitDelayAsync(Task delayTask, CancellationTokenSource delayCts, CancellationToken ct)
    {
        try
        {
            await delayTask;
            return false;
        }
        catch (OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            return true;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_delayCts, delayCts))
                {
                    _delayCts = null;
                }
            }

            delayCts.Dispose();
        }
    }

    private void OnSessionSnapshotChanged(object? sender, BridgeSnapshot session)
    {
        TaskCompletionSource? drop = null;
        SupervisorSnapshot updated;
        lock (_gate)
        {
            bool streaming = session.Status == BridgeStatus.Streaming;
            if (streaming)
            {
                _streamedThisConnection = true;
            }

            _snapshot = _snapshot with
            {
                Session = session,
                HasStreamedThisRun = _snapshot.HasStreamedThisRun || streaming,
            };
            updated = _snapshot;

            if (session.Status is BridgeStatus.Disconnected or BridgeStatus.Failed)
            {
                drop = _dropSignal;
            }
        }

        SnapshotChanged?.Invoke(this, updated);
        drop?.TrySetResult();
    }

    private void Update(Func<SupervisorSnapshot, SupervisorSnapshot> transform)
    {
        SupervisorSnapshot updated;
        lock (_gate)
        {
            updated = transform(_snapshot);
            _snapshot = updated;
        }

        SnapshotChanged?.Invoke(this, updated);
    }
}
