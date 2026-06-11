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
}
