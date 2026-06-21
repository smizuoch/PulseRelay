using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PulseRelay.App;
using PulseRelay.App.Settings;
using Xunit;

namespace PulseRelay.Tests;

public class BridgeSupervisorTests
{
    private sealed class Harness : IAsyncDisposable
    {
        public Harness(BridgeSupervisorOptions? options = null)
        {
            Session = new BridgeSession(Factory, NullLoggerFactory.Instance);
            Supervisor = new BridgeSupervisor(Session, Time, options);
        }

        public FakeTimeProvider Time { get; } = new();

        public FakeSourceFactory Factory { get; } = new();

        public BridgeSession Session { get; }

        public BridgeSupervisor Supervisor { get; }

        public AppSettings Settings { get; } = new() { SourceKind = HeartRateSourceKind.Mock, OscEnabled = false };

        public DateTimeOffset Now => Time.GetUtcNow();

        public BridgeStatus EffectiveStatus() =>
            Supervisor.Snapshot.EffectiveStatus(Now, Supervisor.StaleThreshold);

        /// <summary>Pumps real continuations (no fake-time advance) until the condition holds.</summary>
        public async Task WaitForAsync(Func<SupervisorSnapshot, bool> condition, string description)
        {
            for (int i = 0; i < 500; i++)
            {
                if (condition(Supervisor.Snapshot))
                {
                    return;
                }

                await Task.Delay(10);
            }

            Assert.Fail($"Timed out waiting for: {description}. Snapshot: {Supervisor.Snapshot}");
        }

        /// <summary>Advances fake time in steps, pumping continuations, until the condition holds.</summary>
        public async Task AdvanceUntilAsync(
            Func<SupervisorSnapshot, bool> condition, TimeSpan step, int maxSteps, string description)
        {
            for (int i = 0; i < maxSteps; i++)
            {
                if (condition(Supervisor.Snapshot))
                {
                    return;
                }

                Time.Advance(step);
                await Task.Delay(10);
            }

            Assert.Fail($"Timed out advancing for: {description}. Snapshot: {Supervisor.Snapshot}");
        }

        public async Task StartAndStreamAsync()
        {
            Supervisor.Start(Settings);
            await WaitForAsync(
                s => s.RunState == BridgeRunState.Running && s.Session.Status == BridgeStatus.WaitingForData,
                "initial connect");
            Factory.Latest.EmitSample(80, Now);
            await WaitForAsync(s => s.Session.Status == BridgeStatus.Streaming, "first sample");
        }

        public ValueTask DisposeAsync() => Supervisor.DisposeAsync();
    }

    [Fact]
    public async Task Reconnects_with_fresh_source_after_unexpected_disconnect()
    {
        await using var h = new Harness();
        await h.StartAndStreamAsync();
        Assert.Single(h.Factory.Created);

        h.Factory.Latest.RaiseDisconnected();

        await h.WaitForAsync(s => s.RunState == BridgeRunState.Reconnecting, "retry scheduled");
        Assert.Equal(BridgeStatus.Reconnecting, h.EffectiveStatus());
        Assert.True(h.Supervisor.Snapshot.HasStreamedThisRun);

        await h.AdvanceUntilAsync(
            s => h.Factory.Created.Count == 2, TimeSpan.FromSeconds(1), 10, "fresh source created");
        await h.WaitForAsync(
            s => s.RunState == BridgeRunState.Running && s.Session.Status == BridgeStatus.WaitingForData,
            "reconnected");
    }

    [Fact]
    public async Task Does_not_reconnect_after_manual_stop()
    {
        await using var h = new Harness();
        await h.StartAndStreamAsync();

        await h.Supervisor.StopAsync();

        Assert.Equal(BridgeRunState.Stopped, h.Supervisor.Snapshot.RunState);
        Assert.Equal(BridgeStatus.NotConnected, h.EffectiveStatus());

        for (int i = 0; i < 5; i++)
        {
            h.Time.Advance(TimeSpan.FromSeconds(30));
            await Task.Delay(10);
        }

        Assert.Single(h.Factory.Created);
        Assert.Equal(BridgeRunState.Stopped, h.Supervisor.Snapshot.RunState);
    }

    [Fact]
    public async Task Streaming_shows_stale_after_display_threshold_without_reconnecting()
    {
        using var culture = new CultureScope("en");
        await using var h = new Harness();
        await h.StartAndStreamAsync();

        for (int i = 0; i < 12; i++)
        {
            h.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5);
        }

        Assert.Equal(BridgeStatus.Stale, h.EffectiveStatus());
        string headline = BridgeStatusCopy.Headline(h.Supervisor.Snapshot, h.Now, h.Supervisor.StaleThreshold);
        Assert.Contains("12s", headline);
        Assert.Single(h.Factory.Created);
        Assert.Equal(BridgeRunState.Running, h.Supervisor.Snapshot.RunState);
    }

    [Fact]
    public async Task No_stale_or_reconnect_before_first_sample()
    {
        await using var h = new Harness();
        h.Supervisor.Start(h.Settings);
        await h.WaitForAsync(s => s.Session.Status == BridgeStatus.WaitingForData, "subscribed");

        // Charge 6 first notification has been observed ~19s after subscription; 25s of
        // silence before the first sample must neither look stale nor trigger a reconnect.
        for (int i = 0; i < 25; i++)
        {
            h.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5);
        }

        Assert.Equal(BridgeStatus.WaitingForData, h.EffectiveStatus());
        Assert.Single(h.Factory.Created);
        Assert.Equal(BridgeRunState.Running, h.Supervisor.Snapshot.RunState);
    }

    [Fact]
    public async Task Prolonged_stale_triggers_reconnect_with_fresh_source()
    {
        await using var h = new Harness();
        await h.StartAndStreamAsync();

        await h.AdvanceUntilAsync(
            s => h.Factory.Created.Count == 2, TimeSpan.FromSeconds(1), 60, "stale-triggered reconnect");

        await h.WaitForAsync(
            s => s.RunState == BridgeRunState.Running && s.Session.Status == BridgeStatus.WaitingForData,
            "recovered after stale reconnect");
    }

    [Fact]
    public async Task Backoff_follows_sequence_and_stop_cancels_pending_retry()
    {
        await using var h = new Harness();
        h.Factory.StartFailureForAttempt = _ => new TimeoutException("no device");
        h.Supervisor.Start(h.Settings);

        TimeSpan[] expected =
        [
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30),
        ];
        for (int attempt = 1; attempt <= expected.Length; attempt++)
        {
            await h.WaitForAsync(
                s => s.RetryAttempt == attempt && s.NextRetryAt is not null,
                $"retry {attempt} scheduled");
            Assert.Equal(expected[attempt - 1], h.Supervisor.Snapshot.NextRetryAt - h.Now);
            Assert.Equal(BridgeStatus.Reconnecting, h.EffectiveStatus());
            h.Time.Advance(expected[attempt - 1]);
        }

        await h.WaitForAsync(s => s.RetryAttempt == 6 && s.NextRetryAt is not null, "retry 6 scheduled");
        int createdBeforeStop = h.Factory.Created.Count;

        await h.Supervisor.StopAsync();

        for (int i = 0; i < 5; i++)
        {
            h.Time.Advance(TimeSpan.FromSeconds(30));
            await Task.Delay(10);
        }

        Assert.Equal(createdBeforeStop, h.Factory.Created.Count);
        Assert.Equal(BridgeRunState.Stopped, h.Supervisor.Snapshot.RunState);
    }

    [Fact]
    public async Task Initial_connect_failure_retries_with_not_found_copy()
    {
        using var culture = new CultureScope("en");
        await using var h = new Harness();
        h.Factory.StartFailureForAttempt = attempt => attempt == 0 ? new TimeoutException("scan timeout") : null;
        h.Supervisor.Start(h.Settings);

        await h.WaitForAsync(s => s.RunState == BridgeRunState.Reconnecting, "retry after initial failure");
        Assert.False(h.Supervisor.Snapshot.HasStreamedThisRun);
        Assert.Equal(BridgeFailureKind.DeviceNotFound, h.Supervisor.Snapshot.Session.FailureKind);

        string headline = BridgeStatusCopy.Headline(h.Supervisor.Snapshot, h.Now, h.Supervisor.StaleThreshold);
        Assert.Contains("trying again", headline);
        Assert.Contains("find", headline);

        await h.AdvanceUntilAsync(
            s => h.Factory.Created.Count == 2, TimeSpan.FromSeconds(1), 10, "second attempt");
        await h.WaitForAsync(s => s.RunState == BridgeRunState.Running, "second attempt connected");
    }

    [Fact]
    public async Task Manual_reconnect_skips_backoff_and_resets_sequence()
    {
        await using var h = new Harness();
        h.Factory.StartFailureForAttempt = _ => new TimeoutException("no device");
        h.Supervisor.Start(h.Settings);

        // Walk to the 30s tier: attempts 1..4 with delays 1s/3s/10s consumed.
        await h.AdvanceUntilAsync(
            s => s.RetryAttempt == 4 && s.NextRetryAt is not null, TimeSpan.FromSeconds(1), 60, "30s tier");
        int createdBefore = h.Factory.Created.Count;

        h.Supervisor.RequestReconnectNow();

        // Immediate attempt without advancing fake time.
        await h.WaitForAsync(
            s => h.Factory.Created.Count == createdBefore + 1, "immediate attempt after manual reconnect");

        // Sequence restarted: the failed manual attempt schedules a 1s wait again.
        await h.WaitForAsync(s => s is { RetryAttempt: 1, NextRetryAt: not null }, "backoff reset");
        Assert.Equal(TimeSpan.FromSeconds(1), h.Supervisor.Snapshot.NextRetryAt - h.Now);
    }

    [Fact]
    public async Task Start_is_ignored_until_an_in_progress_stop_finishes()
    {
        await using var h = new Harness();
        await h.StartAndStreamAsync();
        var stopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Factory.Latest.StopGate = stopGate;

        Task stopping = h.Supervisor.StopAsync();
        await Task.Delay(20);
        h.Supervisor.Start(h.Settings);

        Assert.Single(h.Factory.Created);
        stopGate.SetResult();
        await stopping;

        h.Supervisor.Start(h.Settings);
        await h.WaitForAsync(s => h.Factory.Created.Count == 2, "start after stop completed");
    }

    [Fact]
    public async Task Initial_connection_timeout_stops_and_notifies_after_thirty_minutes()
    {
        var options = new BridgeSupervisorOptions
        {
            InitialConnectionTimeout = TimeSpan.FromMinutes(30),
        };
        await using var h = new Harness(options);
        h.Settings.SourceKind = HeartRateSourceKind.Ble;
        h.Factory.StartFailureForAttempt = _ => new TimeoutException("no device");
        var timedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Supervisor.InitialConnectionTimedOut += (_, _) => timedOut.TrySetResult();

        h.Supervisor.Start(h.Settings);
        await h.WaitForAsync(s => s.RunState == BridgeRunState.Reconnecting, "initial retry");
        h.Time.Advance(TimeSpan.FromMinutes(30));

        await timedOut.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(h.Supervisor.IsRunning);
        Assert.Equal(BridgeRunState.Stopped, h.Supervisor.Snapshot.RunState);
        Assert.Equal(BridgeFailureKind.ConnectionTimeout, h.Supervisor.Snapshot.Session.FailureKind);
    }

    [Fact]
    public async Task Initial_connection_timeout_is_disabled_permanently_after_first_sample()
    {
        var options = new BridgeSupervisorOptions
        {
            InitialConnectionTimeout = TimeSpan.FromMinutes(30),
            StaleReconnectThreshold = TimeSpan.FromHours(2),
        };
        await using var h = new Harness(options);
        h.Settings.SourceKind = HeartRateSourceKind.Ble;
        bool timedOut = false;
        h.Supervisor.InitialConnectionTimedOut += (_, _) => timedOut = true;
        await h.StartAndStreamAsync();

        h.Time.Advance(TimeSpan.FromHours(1));
        await Task.Delay(20);

        Assert.False(timedOut);
        Assert.True(h.Supervisor.IsRunning);
        Assert.Equal(BridgeRunState.Running, h.Supervisor.Snapshot.RunState);
    }

    [Fact]
    public async Task Initial_connection_timeout_is_disabled_after_subscription_connects()
    {
        var options = new BridgeSupervisorOptions
        {
            InitialConnectionTimeout = TimeSpan.FromMinutes(30),
            FirstSampleTimeout = TimeSpan.FromHours(2),
        };
        await using var h = new Harness(options);
        h.Settings.SourceKind = HeartRateSourceKind.Ble;
        bool timedOut = false;
        h.Supervisor.InitialConnectionTimedOut += (_, _) => timedOut = true;

        h.Supervisor.Start(h.Settings);
        await h.WaitForAsync(s => s.Session.Status == BridgeStatus.WaitingForData, "subscribed");
        h.Time.Advance(TimeSpan.FromHours(1));
        await Task.Delay(20);

        Assert.False(timedOut);
        Assert.True(h.Supervisor.IsRunning);
    }

    [Fact]
    public async Task Missing_first_sample_forces_a_fresh_connection_attempt()
    {
        var options = new BridgeSupervisorOptions
        {
            FirstSampleTimeout = TimeSpan.FromSeconds(60),
        };
        await using var h = new Harness(options);
        h.Supervisor.Start(h.Settings);
        await h.WaitForAsync(s => s.Session.Status == BridgeStatus.WaitingForData, "subscribed");

        await h.AdvanceUntilAsync(
            _ => h.Factory.Created.Count == 2,
            TimeSpan.FromSeconds(1),
            70,
            "reconnect after missing first sample");
    }

    [Fact]
    public async Task Retry_attempt_resets_after_successful_reconnect()
    {
        await using var h = new Harness();
        h.Factory.StartFailureForAttempt = attempt => attempt == 0 ? new TimeoutException("first fails") : null;
        h.Supervisor.Start(h.Settings);
        await h.WaitForAsync(s => s.RetryAttempt == 1 && s.NextRetryAt is not null, "first retry");
        h.Time.Advance(TimeSpan.FromSeconds(1));

        await h.WaitForAsync(
            s => s.RunState == BridgeRunState.Running && s.Session.Status == BridgeStatus.WaitingForData,
            "successful retry");

        Assert.Equal(0, h.Supervisor.Snapshot.RetryAttempt);
    }

    [Fact]
    public async Task Unexpected_loop_failure_does_not_leave_supervisor_stuck_running()
    {
        var options = new BridgeSupervisorOptions { Backoff = [] };
        await using var h = new Harness(options);
        h.Factory.StartFailureForAttempt = _ => new TimeoutException("failure");

        h.Supervisor.Start(h.Settings);
        await h.WaitForAsync(
            s => s.RunState == BridgeRunState.Stopped && s.Session.Status == BridgeStatus.Failed,
            "loop failure");

        Assert.False(h.Supervisor.IsRunning);
        int beforeRestart = h.Factory.Created.Count;
        h.Supervisor.Start(h.Settings);
        await h.WaitForAsync(s => h.Factory.Created.Count > beforeRestart, "restart after loop failure");
    }
}
