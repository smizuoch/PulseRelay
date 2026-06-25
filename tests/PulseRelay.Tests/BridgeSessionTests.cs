using Microsoft.Extensions.Logging.Abstractions;
using PulseRelay.App;
using PulseRelay.App.Settings;
using PulseRelay.Core.HeartRate;
using PulseRelay.Core.Sources;
using PulseRelay.Osc;
using System.Reflection;
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
    public void Unknown_source_state_is_rejected()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            BridgeSession.Map((HeartRateSourceState)999));

        Assert.Equal("state", ex.ParamName);
    }

    [Fact]
    public async Task Enabling_osc_without_source_updates_snapshot_only()
    {
        await using var session = CreateSession();

        Assert.True(session.TrySetOscEnabled(true, MockSettings(), out string? error));

        Assert.Null(error);
        Assert.Equal(OscOutputStatus.On, session.Snapshot.OscStatus);
    }

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
    public async Task Source_factory_argument_error_fails_with_unknown_kind()
    {
        var factory = new ThrowingSourceFactory(new ArgumentException("bad source"));
        await using var session = new BridgeSession(factory, NullLoggerFactory.Instance);

        bool connected = await session.ConnectAsync(MockSettings(), CancellationToken.None);

        Assert.False(connected);
        Assert.Equal(BridgeStatus.Failed, session.Snapshot.Status);
        Assert.Equal("bad source", session.Snapshot.LastError);
        Assert.Equal(BridgeFailureKind.Unknown, session.Snapshot.FailureKind);
    }

    [Fact]
    public async Task Timeout_start_failure_sets_device_not_found()
    {
        var factory = new FakeSourceFactory
        {
            Configure = source => source.StartFailure = new TimeoutException("no device"),
        };
        await using var session = new BridgeSession(factory, NullLoggerFactory.Instance);

        bool connected = await session.ConnectAsync(MockSettings(), CancellationToken.None);

        Assert.False(connected);
        Assert.Equal(BridgeStatus.Failed, session.Snapshot.Status);
        Assert.Equal("no device", session.Snapshot.LastError);
        Assert.Equal(BridgeFailureKind.DeviceNotFound, session.Snapshot.FailureKind);
        Assert.Equal(1, factory.Latest.DisposeCalls);
    }

    [Fact]
    public async Task Non_timeout_start_failure_sets_unknown_kind()
    {
        var factory = new FakeSourceFactory
        {
            Configure = source => source.StartFailure = new InvalidOperationException("boom"),
        };
        await using var session = new BridgeSession(factory, NullLoggerFactory.Instance);

        bool connected = await session.ConnectAsync(MockSettings(), CancellationToken.None);

        Assert.False(connected);
        Assert.Equal(BridgeStatus.Failed, session.Snapshot.Status);
        Assert.Equal("boom", session.Snapshot.LastError);
        Assert.Equal(BridgeFailureKind.Unknown, session.Snapshot.FailureKind);
    }

    [Fact]
    public async Task Canceled_connect_cleans_up_source_and_returns_false()
    {
        var factory = new FakeSourceFactory();
        await using var session = new BridgeSession(factory, NullLoggerFactory.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        bool connected = await session.ConnectAsync(MockSettings(), cts.Token);

        Assert.False(connected);
        Assert.Equal(BridgeSnapshot.Initial, session.Snapshot);
        Assert.Equal(1, factory.Latest.DisposeCalls);
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
    public async Task Enabling_osc_fails_if_source_changes_during_publisher_attach()
    {
        BridgeSession? session = null;
        var source = new ReentrantAttachSource(() =>
            session!.DisconnectAsync().GetAwaiter().GetResult());
        var factory = new SingleSourceFactory(source);
        await using var createdSession = new BridgeSession(factory, NullLoggerFactory.Instance);
        session = createdSession;

        bool connected = await createdSession.ConnectAsync(MockSettings(oscEnabled: true), CancellationToken.None);

        Assert.False(connected);
        Assert.Equal(BridgeStatus.Failed, createdSession.Snapshot.Status);
        Assert.Equal(BridgeFailureKind.OscConfig, createdSession.Snapshot.FailureKind);
        Assert.Equal(OscOutputStatus.Error, createdSession.Snapshot.OscStatus);
        Assert.Contains("source changed", createdSession.Snapshot.OscError);
        Assert.Equal(1, source.DisposeCalls);
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
    public async Task Enabling_osc_again_replaces_previous_publisher()
    {
        await using var session = CreateSession();
        var settings = MockSettings(oscEnabled: true, oscPort: 9206);

        Assert.True(await session.ConnectAsync(settings, CancellationToken.None));
        object first = GetPublisher(session);

        Assert.True(session.TrySetOscEnabled(true, settings, out string? error));

        Assert.Null(error);
        Assert.NotSame(first, GetPublisher(session));
        Assert.Equal(OscOutputStatus.On, session.Snapshot.OscStatus);
    }

    [Fact]
    public async Task Stale_osc_send_completion_is_ignored_and_current_error_is_recorded()
    {
        var factory = new FakeSourceFactory();
        await using var session = new BridgeSession(factory, NullLoggerFactory.Instance);

        Assert.True(await session.ConnectAsync(MockSettings(oscEnabled: true), CancellationToken.None));

        InvokeOscSendCompleted(session, new object(), new OscSendResult(80, "stale"));
        Assert.Equal(OscOutputStatus.On, session.Snapshot.OscStatus);
        Assert.Null(session.Snapshot.OscError);

        object publisher = GetPublisher(session);
        InvokeOscSendCompleted(session, publisher, new OscSendResult(81, "send failed"));

        Assert.Equal(OscOutputStatus.Error, session.Snapshot.OscStatus);
        Assert.Equal("send failed", session.Snapshot.OscError);
        Assert.Equal(1, session.Snapshot.OscErrorCount);
    }

    [Fact]
    public async Task Direct_stale_source_callbacks_are_ignored()
    {
        await using var session = CreateSession();
        Assert.True(await session.ConnectAsync(MockSettings(), CancellationToken.None));
        var before = session.Snapshot;
        var staleSender = new object();

        InvokeStateChanged(session, staleSender, HeartRateSourceState.Failed);
        InvokeSampleReceived(session, staleSender, new HeartRateSample(
            Bpm: 199,
            SensorContact: SensorContactStatus.NoContact,
            EnergyExpendedKilojoules: null,
            RrIntervalsMs: [301.5],
            Timestamp: DateTimeOffset.UtcNow));

        Assert.Equal(before, session.Snapshot);
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
    public async Task Disconnect_resets_snapshot_even_when_dispose_throws()
    {
        var factory = new FakeSourceFactory
        {
            Configure = source => source.DisposeFailure = new IOException("dispose failed"),
        };
        await using var session = new BridgeSession(factory, NullLoggerFactory.Instance);

        Assert.True(await session.ConnectAsync(MockSettings(), CancellationToken.None));
        await session.DisconnectAsync();

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
    public async Task Late_state_from_disconnected_source_does_not_change_new_session()
    {
        var factory = new FakeSourceFactory();
        await using var session = new BridgeSession(factory, NullLoggerFactory.Instance);

        Assert.True(await session.ConnectAsync(MockSettings(), CancellationToken.None));
        var oldSource = factory.Latest;
        await session.DisconnectAsync();
        Assert.True(await session.ConnectAsync(MockSettings(), CancellationToken.None));

        oldSource.RaiseDisconnected();

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

    private sealed class ThrowingSourceFactory : IHeartRateSourceFactory
    {
        private readonly Exception _exception;

        public ThrowingSourceFactory(Exception exception) => _exception = exception;

        public bool SupportsBle => true;

        public IHeartRateSource Create(AppSettings settings) => throw _exception;
    }

    private static object GetPublisher(BridgeSession session)
    {
        var field = typeof(BridgeSession).GetField(
            "_publisher",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        object? publisher = field.GetValue(session);
        Assert.NotNull(publisher);
        return publisher;
    }

    private static void InvokeOscSendCompleted(
        BridgeSession session,
        object sender,
        OscSendResult result)
    {
        var method = typeof(BridgeSession).GetMethod(
            "OnOscSendCompleted",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(session, [sender, result]);
    }

    private static void InvokeStateChanged(
        BridgeSession session,
        object sender,
        HeartRateSourceState state)
    {
        var method = typeof(BridgeSession).GetMethod(
            "OnStateChanged",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(session, [sender, state]);
    }

    private static void InvokeSampleReceived(
        BridgeSession session,
        object sender,
        HeartRateSample sample)
    {
        var method = typeof(BridgeSession).GetMethod(
            "OnSampleReceived",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(session, [sender, sample]);
    }

    private sealed class SingleSourceFactory(IHeartRateSource source) : IHeartRateSourceFactory
    {
        public bool SupportsBle => true;

        public IHeartRateSource Create(AppSettings settings) => source;
    }

    private sealed class ReentrantAttachSource(Action onPublisherAttach) : IHeartRateSource
    {
        private EventHandler<HeartRateSample>? _sampleReceived;
        private int _sampleSubscriberCount;

        public int DisposeCalls { get; private set; }

        public string Description => "reentrant source";

        public HeartRateSourceState State { get; private set; } = HeartRateSourceState.Idle;

        public event EventHandler<HeartRateSample>? SampleReceived
        {
            add
            {
                _sampleReceived += value;
                _sampleSubscriberCount++;
                if (_sampleSubscriberCount == 2)
                {
                    onPublisherAttach();
                }
            }
            remove => _sampleReceived -= value;
        }

        public event EventHandler<HeartRateSourceState>? StateChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }
}
