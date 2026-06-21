using Microsoft.Extensions.Logging.Abstractions;
using PulseRelay.App;
using PulseRelay.App.Settings;
using PulseRelay.Core.Sources;
using Xunit;

namespace PulseRelay.Tests;

public class BridgeSessionTests
{
    private static AppSettings MockSettings(bool oscEnabled = false, int oscPort = 9201) => new()
    {
        SourceKind = HeartRateSourceKind.Mock,
        OscEnabled = oscEnabled,
        OscPort = oscPort,
    };

    private static BridgeSession CreateSession() =>
        new(new MockOnlySourceFactory(NullLoggerFactory.Instance), NullLoggerFactory.Instance);

    [Theory]
    [InlineData(HeartRateSourceState.Idle, BridgeStatus.NotConnected)]
    [InlineData(HeartRateSourceState.Scanning, BridgeStatus.Searching)]
    [InlineData(HeartRateSourceState.Connecting, BridgeStatus.Connecting)]
    [InlineData(HeartRateSourceState.Subscribing, BridgeStatus.Connecting)]
    [InlineData(HeartRateSourceState.Subscribed, BridgeStatus.WaitingForData)]
    [InlineData(HeartRateSourceState.Streaming, BridgeStatus.Streaming)]
    [InlineData(HeartRateSourceState.Disconnected, BridgeStatus.Disconnected)]
    [InlineData(HeartRateSourceState.Failed, BridgeStatus.Failed)]
    public void Maps_every_source_state(HeartRateSourceState state, BridgeStatus expected) =>
        Assert.Equal(expected, BridgeSession.Map(state));

    [Fact]
    public async Task Connect_reaches_waiting_then_streaming()
    {
        await using var session = CreateSession();

        var streaming = new TaskCompletionSource<BridgeSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot is { Status: BridgeStatus.Streaming, Bpm: not null })
            {
                streaming.TrySetResult(snapshot);
            }
        };

        bool connected = await session.ConnectAsync(MockSettings(), CancellationToken.None);

        Assert.True(connected);
        Assert.Equal(BridgeStatus.WaitingForData, session.Snapshot.Status);
        Assert.NotNull(session.Snapshot.SourceDescription);

        var live = await streaming.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.InRange(live.Bpm!.Value, 60, 100);
        Assert.True(live.SampleCount >= 1);
        Assert.NotNull(live.LastSampleAt);
    }

    [Fact]
    public async Task Connect_refreshes_source_description_after_start()
    {
        // Mimics a BLE source: the name placeholder is only replaced during StartAsync.
        var factory = new FakeSourceFactory
        {
            Configure = s =>
            {
                s.Description = "BLE <unknown>";
                s.DescriptionAfterStart = "BLE Charge 6";
            },
        };
        await using var session = new BridgeSession(factory, NullLoggerFactory.Instance);

        bool connected = await session.ConnectAsync(MockSettings(), CancellationToken.None);

        Assert.True(connected);
        Assert.Equal("BLE Charge 6", session.Snapshot.SourceDescription);
    }

    [Fact]
    public async Task Disconnect_resets_snapshot()
    {
        await using var session = CreateSession();
        await session.ConnectAsync(MockSettings(), CancellationToken.None);

        await session.DisconnectAsync();

        Assert.Equal(BridgeSnapshot.Initial, session.Snapshot);
    }

    [Fact]
    public async Task Double_connect_throws()
    {
        await using var session = CreateSession();
        await session.ConnectAsync(MockSettings(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.ConnectAsync(MockSettings(), CancellationToken.None));
    }

    [Fact]
    public async Task Ble_on_unsupported_platform_fails_with_message()
    {
        await using var session = CreateSession();

        bool connected = await session.ConnectAsync(
            new AppSettings { SourceKind = HeartRateSourceKind.Ble }, CancellationToken.None);

        Assert.False(connected);
        Assert.Equal(BridgeStatus.Failed, session.Snapshot.Status);
        Assert.Contains("platform", session.Snapshot.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(BridgeFailureKind.PlatformUnsupported, session.Snapshot.FailureKind);
    }

    [Fact]
    public async Task Osc_counts_sends_on_loopback()
    {
        await using var session = CreateSession();

        var sent = new TaskCompletionSource<BridgeSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.OscSentCount > 0)
            {
                sent.TrySetResult(snapshot);
            }
        };

        bool connected = await session.ConnectAsync(MockSettings(oscEnabled: true), CancellationToken.None);
        Assert.True(connected);
        Assert.Equal(OscOutputStatus.On, session.Snapshot.OscStatus);

        var snapshot = await sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(OscOutputStatus.On, snapshot.OscStatus);
        Assert.Null(snapshot.OscError);
    }

    [Fact]
    public async Task Invalid_osc_address_fails_connect_with_error()
    {
        await using var session = CreateSession();
        var settings = MockSettings(oscEnabled: true);
        settings.OscAddress = "not-an-osc-address";

        bool connected = await session.ConnectAsync(settings, CancellationToken.None);

        Assert.False(connected);
        Assert.Equal(BridgeStatus.Failed, session.Snapshot.Status);
        Assert.Equal(BridgeFailureKind.OscConfig, session.Snapshot.FailureKind);
        Assert.Equal(OscOutputStatus.Error, session.Snapshot.OscStatus);
        Assert.NotNull(session.Snapshot.OscError);
    }

    [Fact]
    public async Task Osc_can_be_toggled_mid_session()
    {
        await using var session = CreateSession();
        var settings = MockSettings(oscEnabled: false);
        await session.ConnectAsync(settings, CancellationToken.None);
        Assert.Equal(OscOutputStatus.Off, session.Snapshot.OscStatus);

        Assert.True(session.TrySetOscEnabled(true, settings, out string? error));
        Assert.Null(error);
        Assert.Equal(OscOutputStatus.On, session.Snapshot.OscStatus);

        Assert.True(session.TrySetOscEnabled(false, settings, out _));
        Assert.Equal(OscOutputStatus.Off, session.Snapshot.OscStatus);
    }

    [Fact]
    public async Task Disconnect_disposes_source_even_when_stop_throws()
    {
        var factory = new FakeSourceFactory
        {
            Configure = source => source.StopFailure = new IOException("stop failed"),
        };
        await using var session = new BridgeSession(factory, NullLoggerFactory.Instance);

        Assert.True(await session.ConnectAsync(MockSettings(), CancellationToken.None));
        await session.DisconnectAsync();

        Assert.Equal(1, factory.Latest.StopCalls);
        Assert.Equal(1, factory.Latest.DisposeCalls);
        Assert.Equal(BridgeSnapshot.Initial, session.Snapshot);
    }

    [Fact]
    public async Task Late_events_from_disconnected_source_do_not_change_new_session()
    {
        var factory = new FakeSourceFactory();
        await using var session = new BridgeSession(factory, NullLoggerFactory.Instance);

        Assert.True(await session.ConnectAsync(MockSettings(), CancellationToken.None));
        var oldSource = factory.Latest;
        await session.DisconnectAsync();
        Assert.True(await session.ConnectAsync(MockSettings(), CancellationToken.None));

        oldSource.EmitSample(199, DateTimeOffset.UtcNow);

        Assert.Null(session.Snapshot.Bpm);
        Assert.Equal(BridgeStatus.WaitingForData, session.Snapshot.Status);
    }

    [Fact]
    public async Task Osc_observer_exception_does_not_escape_sample_delivery()
    {
        var source = new FakeHeartRateSource();
        using var publisher = new PulseRelay.Osc.HeartRateOscPublisher(port: 9202);
        publisher.SendCompleted += (_, _) => throw new InvalidOperationException("observer failed");
        publisher.Attach(source);
        await source.StartAsync(CancellationToken.None);

        var exception = Record.Exception(
            () => source.EmitSample(80, DateTimeOffset.UtcNow));

        Assert.Null(exception);
    }

    [Fact]
    public void Streaming_goes_stale_after_threshold()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = BridgeSnapshot.Initial with
        {
            Status = BridgeStatus.Streaming,
            LastSampleAt = now - TimeSpan.FromSeconds(20),
        };

        Assert.Equal(BridgeStatus.Stale, snapshot.EffectiveStatus(now, TimeSpan.FromSeconds(10)));
        Assert.Equal(
            BridgeStatus.Streaming,
            (snapshot with { LastSampleAt = now - TimeSpan.FromSeconds(5) })
                .EffectiveStatus(now, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Stale_headline_reports_seconds()
    {
        using var culture = new CultureScope("en");
        var now = DateTimeOffset.UtcNow;
        var snapshot = BridgeSnapshot.Initial with
        {
            Status = BridgeStatus.Streaming,
            LastSampleAt = now - TimeSpan.FromSeconds(12),
        };

        string headline = BridgeStatusCopy.Headline(snapshot, now, TimeSpan.FromSeconds(10));

        Assert.Contains("12s", headline);
    }
}
