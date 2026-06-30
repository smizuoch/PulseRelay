using Microsoft.Extensions.Logging.Abstractions;
using PulseRelay.Core.Sources;
using PulseRelay.LinuxBle;
using Xunit;

namespace PulseRelay.Tests;

public sealed class LinuxBleHeartRateSourceTests
{
    [Fact]
    public async Task StartAsync_scans_connects_subscribes_and_emits_first_sample()
    {
        var client = new FakeBluezClient();
        client.Devices.Enqueue(new BluezDeviceAdvertisement(
            "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF",
            "AA:BB:CC:DD:EE:FF",
            "random",
            "BLE Charge 6",
            [BluezUuids.HeartRateService],
            -49));
        client.CharacteristicPath = "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF/service0012/char0014";
        using var sampleReceived = new ManualResetEventSlim();
        var source = new BluezHeartRateSource(
            client,
            NullLogger<BluezHeartRateSource>.Instance,
            nameFilter: "Charge",
            scanTimeout: TimeSpan.FromSeconds(5));
        int? bpm = null;
        source.SampleReceived += (_, sample) =>
        {
            bpm = sample.Bpm;
            sampleReceived.Set();
        };

        await source.StartAsync(CancellationToken.None);
        client.EmitNotification([0x00, 72]);

        Assert.True(sampleReceived.Wait(TimeSpan.FromSeconds(1)));
        Assert.Equal(72, bpm);
        Assert.Equal(HeartRateSourceState.Streaming, source.State);
        Assert.Equal("BLE BLE Charge 6", source.Description);
        Assert.Equal([BluezUuids.HeartRateService], client.StartDiscoveryServiceUuids);
        Assert.Equal("le", client.StartDiscoveryTransport);
        Assert.Equal(client.DevicesSeen[0].ObjectPath, client.ConnectedDevicePath);
        Assert.Equal(client.CharacteristicPath, client.StartedNotifyPath);
    }

    [Fact]
    public async Task StartAsync_ignores_non_matching_names_until_timeout_match()
    {
        var client = new FakeBluezClient();
        client.Devices.Enqueue(new BluezDeviceAdvertisement(
            "/org/bluez/hci0/dev_11_22_33_44_55_66",
            "11:22:33:44:55:66",
            "random",
            "Other Tracker",
            [BluezUuids.HeartRateService],
            -60));
        client.Devices.Enqueue(new BluezDeviceAdvertisement(
            "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF",
            "AA:BB:CC:DD:EE:FF",
            "random",
            "BLE Charge 6",
            [BluezUuids.HeartRateService],
            -52));
        client.CharacteristicPath = "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF/service0012/char0014";
        var source = new BluezHeartRateSource(
            client,
            NullLogger<BluezHeartRateSource>.Instance,
            nameFilter: "Charge 6",
            scanTimeout: TimeSpan.FromSeconds(5));

        await source.StartAsync(CancellationToken.None);

        Assert.Equal("/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF", client.ConnectedDevicePath);
    }

    [Fact]
    public async Task StopAsync_stops_notify_disconnects_and_resets_discovery()
    {
        var client = new FakeBluezClient();
        client.Devices.Enqueue(new BluezDeviceAdvertisement(
            "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF",
            "AA:BB:CC:DD:EE:FF",
            "random",
            "BLE Charge 6",
            [BluezUuids.HeartRateService],
            -49));
        client.CharacteristicPath = "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF/service0012/char0014";
        var source = new BluezHeartRateSource(
            client,
            NullLogger<BluezHeartRateSource>.Instance,
            nameFilter: null,
            scanTimeout: TimeSpan.FromSeconds(5));

        await source.StartAsync(CancellationToken.None);
        await source.StopAsync();

        Assert.Equal(client.CharacteristicPath, client.StoppedNotifyPath);
        Assert.Equal("/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF", client.DisconnectedDevicePath);
        Assert.True(client.StopDiscoveryCalled);
        Assert.Equal(HeartRateSourceState.Disconnected, source.State);
    }

    [Fact]
    public async Task StartAsync_sets_failed_when_no_matching_device_is_seen()
    {
        var client = new FakeBluezClient();
        var source = new BluezHeartRateSource(
            client,
            NullLogger<BluezHeartRateSource>.Instance,
            nameFilter: "Charge",
            scanTimeout: TimeSpan.FromMilliseconds(10));

        await Assert.ThrowsAsync<TimeoutException>(() => source.StartAsync(CancellationToken.None));

        Assert.True(client.StopDiscoveryCalled);
        Assert.Equal(HeartRateSourceState.Failed, source.State);
    }

    [Fact]
    public async Task Malformed_notification_is_ignored_without_leaving_streaming_state()
    {
        var client = new FakeBluezClient();
        client.Devices.Enqueue(new BluezDeviceAdvertisement(
            "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF",
            "AA:BB:CC:DD:EE:FF",
            "random",
            "BLE Charge 6",
            [BluezUuids.HeartRateService],
            -49));
        client.CharacteristicPath = "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF/service0012/char0014";
        using var sampleReceived = new ManualResetEventSlim();
        var source = new BluezHeartRateSource(
            client,
            NullLogger<BluezHeartRateSource>.Instance,
            nameFilter: null,
            scanTimeout: TimeSpan.FromSeconds(5));
        source.SampleReceived += (_, _) => sampleReceived.Set();

        await source.StartAsync(CancellationToken.None);
        client.EmitNotification([0x00]);

        Assert.False(sampleReceived.Wait(TimeSpan.FromMilliseconds(100)));
        Assert.Equal(HeartRateSourceState.Subscribed, source.State);
    }

    [Fact]
    public async Task StartAsync_pairs_and_retries_once_when_notify_requires_authentication()
    {
        var client = new FakeBluezClient
        {
            ThrowPairingRequiredOnFirstNotify = true,
        };
        client.Devices.Enqueue(new BluezDeviceAdvertisement(
            "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF",
            "AA:BB:CC:DD:EE:FF",
            "random",
            "BLE Charge 6",
            [BluezUuids.HeartRateService],
            -49));
        client.CharacteristicPath = "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF/service0012/char0014";
        var source = new BluezHeartRateSource(
            client,
            NullLogger<BluezHeartRateSource>.Instance,
            nameFilter: null,
            scanTimeout: TimeSpan.FromSeconds(5));

        await source.StartAsync(CancellationToken.None);

        Assert.Equal("/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF", client.PairedDevicePath);
        Assert.Equal(2, client.FindCharacteristicCalls);
        Assert.Equal(2, client.StartNotifyCalls);
        Assert.Equal(HeartRateSourceState.Subscribed, source.State);
    }

    private sealed class FakeBluezClient : IBluezHeartRateClient
    {
        private Action<byte[]>? _notificationHandler;

        public Queue<BluezDeviceAdvertisement> Devices { get; } = new();

        public List<BluezDeviceAdvertisement> DevicesSeen { get; } = [];

        public string? CharacteristicPath { get; set; }

        public IReadOnlyList<string>? StartDiscoveryServiceUuids { get; private set; }

        public string? StartDiscoveryTransport { get; private set; }

        public string? ConnectedDevicePath { get; private set; }

        public string? StartedNotifyPath { get; private set; }

        public string? StoppedNotifyPath { get; private set; }

        public string? DisconnectedDevicePath { get; private set; }

        public bool StopDiscoveryCalled { get; private set; }

        public bool ThrowPairingRequiredOnFirstNotify { get; init; }

        public string? PairedDevicePath { get; private set; }

        public int FindCharacteristicCalls { get; private set; }

        public int StartNotifyCalls { get; private set; }

        public Task StartDiscoveryAsync(
            IReadOnlyList<string> serviceUuids,
            string transport,
            CancellationToken cancellationToken)
        {
            StartDiscoveryServiceUuids = serviceUuids;
            StartDiscoveryTransport = transport;
            return Task.CompletedTask;
        }

        public Task StopDiscoveryAsync()
        {
            StopDiscoveryCalled = true;
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<BluezDeviceAdvertisement> WatchAdvertisementsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (Devices.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                var next = Devices.Dequeue();
                DevicesSeen.Add(next);
                yield return next;
            }
        }

        public Task ConnectAsync(string devicePath, CancellationToken cancellationToken)
        {
            ConnectedDevicePath = devicePath;
            return Task.CompletedTask;
        }

        public Task<string> FindHeartRateMeasurementCharacteristicAsync(
            string devicePath,
            CancellationToken cancellationToken)
        {
            FindCharacteristicCalls++;
            return Task.FromResult(CharacteristicPath ?? throw new InvalidOperationException("No characteristic."));
        }

        public Task StartNotifyAsync(
            string characteristicPath,
            Action<byte[]> notificationHandler,
            CancellationToken cancellationToken)
        {
            StartNotifyCalls++;
            if (ThrowPairingRequiredOnFirstNotify && StartNotifyCalls == 1)
            {
                throw new BluezPairingRequiredException("Notify requires pairing.");
            }

            StartedNotifyPath = characteristicPath;
            _notificationHandler = notificationHandler;
            return Task.CompletedTask;
        }

        public Task PairAsync(string devicePath, CancellationToken cancellationToken)
        {
            PairedDevicePath = devicePath;
            return Task.CompletedTask;
        }

        public Task StopNotifyAsync(string characteristicPath)
        {
            StoppedNotifyPath = characteristicPath;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(string devicePath)
        {
            DisconnectedDevicePath = devicePath;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void EmitNotification(byte[] payload) => _notificationHandler?.Invoke(payload);
    }
}
