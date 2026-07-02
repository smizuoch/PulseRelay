using PulseRelay.LinuxBle;
using Tmds.DBus.Protocol;
using Xunit;

namespace PulseRelay.Tests;

public sealed class BluezDbusHeartRateClientTests
{
    [Fact]
    public async Task StartDiscovery_sets_heart_rate_le_filter_and_starts_adapter()
    {
        var bus = new FakeBluezBus();
        bus.Objects["/org/bluez/hci0"] = BluezObject.WithInterface(
            BluezDbus.AdapterInterface,
            new Dictionary<string, VariantValue>
            {
                ["Powered"] = true,
            });
        await using var client = new BluezDbusHeartRateClient(bus);

        await client.StartDiscoveryAsync([BluezUuids.HeartRateService], "le", CancellationToken.None);

        Assert.Equal("/org/bluez/hci0", bus.Calls[0].Path);
        Assert.Equal(BluezDbus.AdapterInterface, bus.Calls[0].Interface);
        Assert.Equal("SetDiscoveryFilter", bus.Calls[0].Member);
        Assert.Equal("a{sv}", bus.Calls[0].Signature);
        Assert.NotNull(bus.LastDiscoveryFilterUuids);
        Assert.Equal([BluezUuids.HeartRateService], bus.LastDiscoveryFilterUuids);
        Assert.Equal("le", bus.LastDiscoveryFilterTransport);
        Assert.Equal("StartDiscovery", bus.Calls[1].Member);
    }

    [Fact]
    public async Task WatchAdvertisements_yields_existing_and_new_matching_devices()
    {
        var bus = new FakeBluezBus();
        bus.Objects["/org/bluez/hci0"] = BluezObject.WithInterface(
            BluezDbus.AdapterInterface,
            new Dictionary<string, VariantValue>());
        bus.Objects["/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF"] = Device(
            "AA:BB:CC:DD:EE:FF",
            "random",
            "BLE Charge 6",
            [BluezUuids.HeartRateService],
            -48);
        await using var client = new BluezDbusHeartRateClient(bus);

        await client.StartDiscoveryAsync([BluezUuids.HeartRateService], "le", CancellationToken.None);
        await using var enumerator = client.WatchAdvertisementsAsync(CancellationToken.None).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("BLE Charge 6", enumerator.Current.Name);

        bus.EmitInterfacesAdded(
            "/org/bluez/hci0/dev_11_22_33_44_55_66",
            Device("11:22:33:44:55:66", "random", "Other HR", [BluezUuids.HeartRateService], -55));

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("11:22:33:44:55:66", enumerator.Current.Address);
    }

    [Fact]
    public async Task WatchAdvertisements_yields_device_when_existing_properties_change_to_heart_rate()
    {
        var bus = new FakeBluezBus();
        const string path = "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF";
        bus.Objects["/org/bluez/hci0"] = BluezObject.WithInterface(
            BluezDbus.AdapterInterface,
            new Dictionary<string, VariantValue>());
        bus.Objects[path] = Device(
            "AA:BB:CC:DD:EE:FF",
            "random",
            string.Empty,
            [],
            0);
        await using var client = new BluezDbusHeartRateClient(bus);

        await client.StartDiscoveryAsync([BluezUuids.HeartRateService], "le", CancellationToken.None);
        await using var enumerator = client.WatchAdvertisementsAsync(CancellationToken.None).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.DoesNotContain(BluezUuids.HeartRateService, enumerator.Current.ServiceUuids);

        bus.EmitPropertiesChanged(
            path,
            BluezDbus.DeviceInterface,
            new Dictionary<string, VariantValue>
            {
                ["Name"] = "BLE Charge 6",
                ["UUIDs"] = VariantValue.Array(new[] { BluezUuids.HeartRateService }),
                ["RSSI"] = (short)-51,
            });

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("BLE Charge 6", enumerator.Current.Name);
        Assert.Equal([BluezUuids.HeartRateService], enumerator.Current.ServiceUuids);
        Assert.Equal(-51, enumerator.Current.RssiDbm);
    }

    [Fact]
    public async Task Connect_and_find_characteristic_uses_Device1_and_ObjectManager()
    {
        var bus = new FakeBluezBus();
        const string devicePath = "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF";
        const string characteristicPath = devicePath + "/service0012/char0014";
        bus.Objects["/org/bluez/hci0"] = BluezObject.WithInterface(
            BluezDbus.AdapterInterface,
            new Dictionary<string, VariantValue>());
        bus.Objects[devicePath] = Device(
            "AA:BB:CC:DD:EE:FF",
            "random",
            "BLE Charge 6",
            [BluezUuids.HeartRateService],
            -48,
            servicesResolved: true);
        bus.Objects[devicePath + "/service0012"] = BluezObject.WithInterface(
            BluezDbus.GattServiceInterface,
            new Dictionary<string, VariantValue>
            {
                ["UUID"] = BluezUuids.HeartRateService,
                ["Device"] = VariantValue.ObjectPath(devicePath),
            });
        bus.Objects[characteristicPath] = BluezObject.WithInterface(
            BluezDbus.GattCharacteristicInterface,
            new Dictionary<string, VariantValue>
            {
                ["UUID"] = BluezUuids.HeartRateMeasurement,
                ["Service"] = VariantValue.ObjectPath(devicePath + "/service0012"),
            });
        await using var client = new BluezDbusHeartRateClient(bus);

        await client.ConnectAsync(devicePath, CancellationToken.None);
        string found = await client.FindHeartRateMeasurementCharacteristicAsync(devicePath, CancellationToken.None);

        Assert.Contains(bus.Calls, call => call.Path == devicePath && call.Member == "Connect");
        Assert.Equal(characteristicPath, found);
    }

    [Fact]
    public async Task StartNotify_dispatches_value_property_notifications()
    {
        var bus = new FakeBluezBus();
        const string characteristicPath = "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF/service0012/char0014";
        using var received = new ManualResetEventSlim();
        byte[]? payload = null;
        await using var client = new BluezDbusHeartRateClient(bus);

        await client.StartNotifyAsync(
            characteristicPath,
            value =>
            {
                payload = value;
                received.Set();
            },
            CancellationToken.None);
        bus.EmitPropertiesChanged(
            characteristicPath,
            BluezDbus.GattCharacteristicInterface,
            new Dictionary<string, VariantValue>
            {
                ["Value"] = VariantValue.Array(new byte[] { 0x00, 64 }),
            });

        Assert.True(received.Wait(TimeSpan.FromSeconds(1)));
        Assert.Equal([0x00, 64], payload);
        Assert.Contains(bus.Calls, call => call.Path == characteristicPath && call.Member == "StartNotify");
    }

    [Fact]
    public async Task Notification_is_delivered_once_after_advertisement_watch_is_active()
    {
        // Regression: the scan flow (WatchAdvertisementsAsync) already subscribes
        // PropertiesChanged; StartNotify must not add a second subscription, or every
        // Heart Rate Measurement is dispatched twice (double samples, double OSC sends).
        var bus = new FakeBluezBus();
        const string devicePath = "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF";
        const string characteristicPath = devicePath + "/service0012/char0014";
        bus.Objects["/org/bluez/hci0"] = BluezObject.WithInterface(
            BluezDbus.AdapterInterface,
            new Dictionary<string, VariantValue>());
        bus.Objects[devicePath] = Device(
            "AA:BB:CC:DD:EE:FF",
            "random",
            "BLE Charge 6",
            [BluezUuids.HeartRateService],
            -48);
        await using var client = new BluezDbusHeartRateClient(bus);

        await client.StartDiscoveryAsync([BluezUuids.HeartRateService], "le", CancellationToken.None);
        await using var enumerator = client.WatchAdvertisementsAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());

        int deliveries = 0;
        await client.StartNotifyAsync(
            characteristicPath,
            _ => Interlocked.Increment(ref deliveries),
            CancellationToken.None);
        bus.EmitPropertiesChanged(
            characteristicPath,
            BluezDbus.GattCharacteristicInterface,
            new Dictionary<string, VariantValue>
            {
                ["Value"] = VariantValue.Array(new byte[] { 0x00, 72 }),
            });

        Assert.Equal(1, deliveries);
    }

    [Fact]
    public async Task StartDiscovery_fails_when_no_adapter_exists()
    {
        var bus = new FakeBluezBus();
        await using var client = new BluezDbusHeartRateClient(bus);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.StartDiscoveryAsync([BluezUuids.HeartRateService], "le", CancellationToken.None));

        Assert.Contains("No BlueZ adapter", ex.Message);
    }

    [Fact]
    public async Task FindHeartRateMeasurementCharacteristicAsync_fails_when_missing()
    {
        var bus = new FakeBluezBus();
        const string devicePath = "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF";
        bus.Objects[devicePath] = Device(
            "AA:BB:CC:DD:EE:FF",
            "random",
            "BLE Charge 6",
            [BluezUuids.HeartRateService],
            -48,
            servicesResolved: true);
        await using var client = new BluezDbusHeartRateClient(bus);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.FindHeartRateMeasurementCharacteristicAsync(devicePath, CancellationToken.None));

        Assert.Contains(BluezUuids.HeartRateMeasurement, ex.Message);
    }

    [Fact]
    public async Task PairAsync_calls_Device1_Pair()
    {
        var bus = new FakeBluezBus();
        const string devicePath = "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF";
        await using var client = new BluezDbusHeartRateClient(bus);

        await client.PairAsync(devicePath, CancellationToken.None);

        Assert.Equal(BluezDbus.AgentPath, bus.RegisteredAgentPath);
        Assert.Equal("NoInputNoOutput", bus.RegisteredAgentCapability);
        Assert.Contains(bus.Calls, call =>
            call.Path == devicePath
            && call.Interface == BluezDbus.DeviceInterface
            && call.Member == "Pair");
        Assert.Equal(BluezDbus.AgentPath, bus.UnregisteredAgentPath);
    }

    [Fact]
    public async Task PairAsync_does_not_pair_when_agent_registration_fails()
    {
        var error = new BluezDbusCallException("org.bluez.Error.Failed", "Agent registration failed.");
        var bus = new FakeBluezBus
        {
            ErrorOnRegisterAgent = error,
        };
        const string devicePath = "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF";
        await using var client = new BluezDbusHeartRateClient(bus);

        var ex = await Assert.ThrowsAsync<BluezDbusCallException>(() =>
            client.PairAsync(devicePath, CancellationToken.None));

        Assert.Same(error, ex);
        Assert.DoesNotContain(bus.Calls, call =>
            call.Path == devicePath
            && call.Interface == BluezDbus.DeviceInterface
            && call.Member == "Pair");
        Assert.Null(bus.UnregisteredAgentPath);
    }

    [Fact]
    public async Task StartNotify_wraps_bluez_authorization_errors_as_pairing_required()
    {
        var bus = new FakeBluezBus
        {
            ErrorOnNextCall = new BluezDbusCallException("org.bluez.Error.NotAuthorized", "Authentication required."),
        };
        await using var client = new BluezDbusHeartRateClient(bus);

        await Assert.ThrowsAsync<BluezPairingRequiredException>(() =>
            client.StartNotifyAsync("/char", _ => { }, CancellationToken.None));
    }

    private static BluezObject Device(
        string address,
        string addressType,
        string name,
        string[] serviceUuids,
        short rssi,
        bool servicesResolved = false) =>
        BluezObject.WithInterface(
            BluezDbus.DeviceInterface,
            new Dictionary<string, VariantValue>
            {
                ["Address"] = address,
                ["AddressType"] = addressType,
                ["Name"] = name,
                ["UUIDs"] = VariantValue.Array(serviceUuids),
                ["RSSI"] = rssi,
                ["ServicesResolved"] = servicesResolved,
            });

    private sealed class FakeBluezBus : IBluezBus
    {
        private readonly List<Action<BluezInterfacesAdded>> _interfacesAddedHandlers = [];
        private readonly List<Action<BluezPropertiesChanged>> _propertiesChangedHandlers = [];

        public Dictionary<string, BluezObject> Objects { get; } = [];

        public List<CallRecord> Calls { get; } = [];

        public string[]? LastDiscoveryFilterUuids { get; private set; }

        public string? LastDiscoveryFilterTransport { get; private set; }

        public Exception? ErrorOnNextCall { get; init; }

        public Exception? ErrorOnRegisterAgent { get; init; }

        public string? RegisteredAgentPath { get; private set; }

        public string? RegisteredAgentCapability { get; private set; }

        public string? UnregisteredAgentPath { get; private set; }

        public Task<IReadOnlyDictionary<string, BluezObject>> GetManagedObjectsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, BluezObject>>(Objects);

        public Task CallMethodAsync(
            string path,
            string @interface,
            string member,
            string signature,
            IReadOnlyDictionary<string, VariantValue>? dictionaryBody,
            CancellationToken cancellationToken)
        {
            Calls.Add(new CallRecord(path, @interface, member, signature));
            if (ErrorOnNextCall is not null)
            {
                throw ErrorOnNextCall;
            }

            if (member == "SetDiscoveryFilter" && dictionaryBody is not null)
            {
                LastDiscoveryFilterUuids = dictionaryBody["UUIDs"].GetArray<string>();
                LastDiscoveryFilterTransport = dictionaryBody["Transport"].GetString();
            }

            return Task.CompletedTask;
        }

        public Task<VariantValue> GetPropertyAsync(
            string path,
            string @interface,
            string propertyName,
            CancellationToken cancellationToken) =>
            Task.FromResult(Objects[path].Interfaces[@interface][propertyName]);

        public ValueTask<IDisposable> WatchInterfacesAddedAsync(
            Action<BluezInterfacesAdded> handler,
            CancellationToken cancellationToken)
        {
            _interfacesAddedHandlers.Add(handler);
            return ValueTask.FromResult<IDisposable>(new Subscription(() => _interfacesAddedHandlers.Remove(handler)));
        }

        public ValueTask<IDisposable> WatchPropertiesChangedAsync(
            Action<BluezPropertiesChanged> handler,
            CancellationToken cancellationToken)
        {
            _propertiesChangedHandlers.Add(handler);
            return ValueTask.FromResult<IDisposable>(new Subscription(() => _propertiesChangedHandlers.Remove(handler)));
        }

        public Task RegisterNoInputNoOutputAgentAsync(
            string agentPath,
            string capability,
            CancellationToken cancellationToken)
        {
            RegisteredAgentPath = agentPath;
            RegisteredAgentCapability = capability;
            if (ErrorOnRegisterAgent is not null)
            {
                throw ErrorOnRegisterAgent;
            }

            return Task.CompletedTask;
        }

        public Task UnregisterAgentAsync(string agentPath)
        {
            UnregisteredAgentPath = agentPath;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void EmitInterfacesAdded(string path, BluezObject obj)
        {
            Objects[path] = obj;
            foreach (var handler in _interfacesAddedHandlers.ToArray())
            {
                handler(new BluezInterfacesAdded(path, obj.Interfaces));
            }
        }

        public void EmitPropertiesChanged(
            string path,
            string @interface,
            IReadOnlyDictionary<string, VariantValue> changed)
        {
            if (Objects.TryGetValue(path, out var obj)
                && obj.Interfaces.TryGetValue(@interface, out var existing))
            {
                var merged = new Dictionary<string, VariantValue>(existing);
                foreach (var (name, value) in changed)
                {
                    merged[name] = value;
                }

                var interfaces = new Dictionary<string, IReadOnlyDictionary<string, VariantValue>>(obj.Interfaces)
                {
                    [@interface] = merged,
                };
                Objects[path] = new BluezObject(interfaces);
            }

            foreach (var handler in _propertiesChangedHandlers.ToArray())
            {
                handler(new BluezPropertiesChanged(path, @interface, changed));
            }
        }
    }

    private sealed record CallRecord(string Path, string Interface, string Member, string Signature);

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
