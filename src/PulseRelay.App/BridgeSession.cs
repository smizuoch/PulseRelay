using Microsoft.Extensions.Logging;
using PulseRelay.App.Settings;
using PulseRelay.Core.HeartRate;
using PulseRelay.Core.Sources;
using PulseRelay.Osc;

namespace PulseRelay.App;

/// <summary>
/// Owns one heart-rate source and its optional OSC publisher, and condenses their events
/// into an immutable <see cref="BridgeSnapshot"/> for the UI.
/// </summary>
/// <remarks>
/// <see cref="SnapshotChanged"/> fires on whatever thread delivered the underlying event
/// (BLE callbacks, mock timer). UI consumers must marshal to their dispatcher themselves;
/// this type stays UI-framework-free.
/// </remarks>
public sealed class BridgeSession : IAsyncDisposable
{
    public static readonly TimeSpan DefaultStaleThreshold = TimeSpan.FromSeconds(10);

    private readonly IHeartRateSourceFactory _sourceFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private IHeartRateSource? _source;
    private HeartRateOscPublisher? _publisher;
    private BridgeSnapshot _snapshot = BridgeSnapshot.Initial;

    public BridgeSession(IHeartRateSourceFactory sourceFactory, ILoggerFactory loggerFactory)
    {
        _sourceFactory = sourceFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<BridgeSession>();
    }

    public BridgeSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public event EventHandler<BridgeSnapshot>? SnapshotChanged;

    public bool SupportsBle => _sourceFactory.SupportsBle;

    public TimeSpan StaleThreshold { get; init; } = DefaultStaleThreshold;

    /// <summary>Maps a raw source state to the coarser UI status. Staleness is applied separately.</summary>
    public static BridgeStatus Map(HeartRateSourceState state) => state switch
    {
        HeartRateSourceState.Idle => BridgeStatus.NotConnected,
        HeartRateSourceState.Scanning => BridgeStatus.Searching,
        HeartRateSourceState.Connecting => BridgeStatus.Connecting,
        HeartRateSourceState.Subscribing => BridgeStatus.Connecting,
        HeartRateSourceState.Subscribed => BridgeStatus.WaitingForData,
        HeartRateSourceState.Streaming => BridgeStatus.Streaming,
        HeartRateSourceState.Disconnected => BridgeStatus.Disconnected,
        HeartRateSourceState.Failed => BridgeStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown source state."),
    };

    /// <summary>
    /// Creates the source per <paramref name="settings"/>, attaches OSC if enabled, and starts
    /// streaming. Returns false (with the failure recorded in the snapshot) instead of throwing,
    /// so callers can drive the UI from the snapshot alone.
    /// </summary>
    public async Task<bool> ConnectAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        IHeartRateSource source;
        try
        {
            source = _sourceFactory.Create(settings);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or ArgumentException)
        {
            _logger.LogError(ex, "Cannot create heart-rate source");
            Update(_ => BridgeSnapshot.Initial with
            {
                Status = BridgeStatus.Failed,
                LastError = ex.Message,
                FailureKind = ex is PlatformNotSupportedException
                    ? BridgeFailureKind.PlatformUnsupported
                    : BridgeFailureKind.Unknown,
            });
            return false;
        }

        lock (_gate)
        {
            if (_source is not null)
            {
                throw new InvalidOperationException("Already connected; disconnect first.");
            }

            _source = source;
        }

        source.StateChanged += OnStateChanged;
        source.SampleReceived += OnSampleReceived;

        if (settings.OscEnabled && !TryAttachPublisher(source, settings))
        {
            string? oscError = Snapshot.OscError;
            await CleanupAsync().ConfigureAwait(false);
            Update(s => s with
            {
                Status = BridgeStatus.Failed,
                LastError = oscError,
                FailureKind = BridgeFailureKind.OscConfig,
                OscStatus = OscOutputStatus.Error,
                OscError = oscError,
            });
            return false;
        }

        Update(s => BridgeSnapshot.Initial with
        {
            Status = BridgeStatus.Connecting,
            SourceDescription = source.Description,
            OscStatus = settings.OscEnabled ? OscOutputStatus.On : OscOutputStatus.Off,
        });

        try
        {
            await source.StartAsync(cancellationToken).ConfigureAwait(false);
            // A BLE source only learns the device name during StartAsync (scan + GATT read),
            // so the description captured above may still hold a placeholder. Re-publish it.
            Update(s => s with { SourceDescription = source.Description });
            _logger.LogInformation("Bridge connected: {Description}", source.Description);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Connect canceled");
            await CleanupAsync().ConfigureAwait(false);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connect failed");
            string message = ex.Message;
            // BleHeartRateSource throws TimeoutException when no device advertises within
            // the scan window; a radio that is off typically surfaces the same way.
            var kind = ex is TimeoutException ? BridgeFailureKind.DeviceNotFound : BridgeFailureKind.Unknown;
            await CleanupAsync().ConfigureAwait(false);
            Update(s => s with { Status = BridgeStatus.Failed, LastError = message, FailureKind = kind });
            return false;
        }
    }

    /// <summary>Stops streaming and resets the snapshot to NotConnected. Safe to call when idle.</summary>
    public async Task DisconnectAsync()
    {
        await CleanupAsync().ConfigureAwait(false);
        _logger.LogInformation("Bridge disconnected");
    }

    /// <summary>
    /// Turns OSC output on or off mid-session. When no source is connected this only updates
    /// the snapshot; the publisher is created on the next connect. Returns false when enabling
    /// fails (for example an invalid OSC address).
    /// </summary>
    public bool TrySetOscEnabled(bool enabled, AppSettings settings, out string? error)
    {
        error = null;

        if (!enabled)
        {
            HeartRateOscPublisher? publisher;
            lock (_gate)
            {
                publisher = _publisher;
                _publisher = null;
            }

            publisher?.Dispose();
            Update(s => s with { OscStatus = OscOutputStatus.Off, OscError = null });
            return true;
        }

        IHeartRateSource? source;
        lock (_gate)
        {
            source = _source;
        }

        if (source is not null && !TryAttachPublisher(source, settings))
        {
            error = Snapshot.OscError;
            return false;
        }

        Update(s => s with { OscStatus = OscOutputStatus.On, OscError = null });
        return true;
    }

    public async ValueTask DisposeAsync() => await CleanupAsync().ConfigureAwait(false);

    private bool TryAttachPublisher(IHeartRateSource source, AppSettings settings)
    {
        HeartRateOscPublisher? publisher = null;
        try
        {
            publisher = new HeartRateOscPublisher(
                settings.OscHost,
                settings.OscPort,
                settings.OscAddress,
                _loggerFactory.CreateLogger<HeartRateOscPublisher>());
            publisher.SendCompleted += OnOscSendCompleted;
            publisher.Attach(source);
            HeartRateOscPublisher? previous;
            bool sourceChanged;
            lock (_gate)
            {
                sourceChanged = !ReferenceEquals(_source, source);
                if (sourceChanged)
                {
                    previous = null;
                }
                else
                {
                    previous = _publisher;
                    _publisher = publisher;
                }
            }

            if (sourceChanged)
            {
                publisher.Dispose();
                Update(s => s with
                {
                    OscStatus = OscOutputStatus.Error,
                    OscError = "The heart-rate source changed while OSC output was being enabled.",
                });
                return false;
            }

            previous?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or System.Net.Sockets.SocketException)
        {
            publisher?.Dispose();
            _logger.LogError(ex, "Cannot enable OSC output");
            Update(s => s with { OscStatus = OscOutputStatus.Error, OscError = ex.Message });
            return false;
        }
    }

    private async Task CleanupAsync()
    {
        IHeartRateSource? source;
        HeartRateOscPublisher? publisher;
        lock (_gate)
        {
            source = _source;
            publisher = _publisher;
            _source = null;
            _publisher = null;
        }

        if (source is not null)
        {
            // Unhook first so the source's own Disconnected transition doesn't overwrite
            // the NotConnected snapshot below.
            source.StateChanged -= OnStateChanged;
            source.SampleReceived -= OnSampleReceived;
        }

        publisher?.Dispose();

        if (source is not null)
        {
            try
            {
                await source.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while stopping source");
            }

            try
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while disposing source");
            }
        }

        Update(_ => BridgeSnapshot.Initial);
    }

    private void OnStateChanged(object? sender, HeartRateSourceState state)
    {
        if (!IsCurrentSource(sender))
        {
            return;
        }

        Update(s => s with
        {
            Status = Map(state),
            // Keep the name current while StartAsync is still running (scan -> connect).
            SourceDescription = (sender as IHeartRateSource)?.Description ?? s.SourceDescription,
        });
    }

    private void OnSampleReceived(object? sender, HeartRateSample sample)
    {
        if (!IsCurrentSource(sender))
        {
            return;
        }

        Update(s => s with
        {
            Bpm = sample.Bpm,
            SensorContact = sample.SensorContact,
            LastSampleAt = sample.Timestamp,
            SampleCount = s.SampleCount + 1,
        });
    }

    private void OnOscSendCompleted(object? sender, OscSendResult result)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _publisher))
            {
                return;
            }
        }

        Update(s => result.Success
            ? s with { OscStatus = OscOutputStatus.On, OscError = null, OscSentCount = s.OscSentCount + 1 }
            : s with { OscStatus = OscOutputStatus.Error, OscError = result.Error, OscErrorCount = s.OscErrorCount + 1 });
    }

    private bool IsCurrentSource(object? sender)
    {
        lock (_gate)
        {
            return ReferenceEquals(sender, _source);
        }
    }

    private void Update(Func<BridgeSnapshot, BridgeSnapshot> transform)
    {
        BridgeSnapshot updated;
        lock (_gate)
        {
            updated = transform(_snapshot);
            _snapshot = updated;
        }

        SnapshotChanged?.Invoke(this, updated);
    }
}
