using System.Threading.Channels;
using Tmds.DBus.Protocol;

namespace PulseRelay.LinuxBle;

public sealed class BluezDbusHeartRateClient : IBluezHeartRateClient
{
    private static readonly TimeSpan ServicesResolvedTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ServicesResolvedPollInterval = TimeSpan.FromMilliseconds(200);
    private readonly IBluezBus _bus;
    private readonly bool _ownsBus;
    private readonly Channel<BluezDeviceAdvertisement> _advertisements =
        Channel.CreateUnbounded<BluezDeviceAdvertisement>();
    private readonly Dictionary<string, Dictionary<string, IReadOnlyDictionary<string, VariantValue>>> _knownInterfaces = [];
    private readonly Dictionary<string, Action<byte[]>> _notificationHandlers = [];
    private readonly List<IDisposable> _subscriptions = [];
    private string? _adapterPath;
    private bool _watchingAdvertisements;
    private bool _watchingProperties;

    public BluezDbusHeartRateClient()
        : this(new TmdsBluezBus(Connection.System), ownsBus: true)
    {
    }

    public BluezDbusHeartRateClient(IBluezBus bus)
        : this(bus, ownsBus: false)
    {
    }

    private BluezDbusHeartRateClient(IBluezBus bus, bool ownsBus)
    {
        _bus = bus;
        _ownsBus = ownsBus;
    }

    public async Task StartDiscoveryAsync(
        IReadOnlyList<string> serviceUuids,
        string transport,
        CancellationToken cancellationToken)
    {
        _adapterPath = await FindAdapterPathAsync(cancellationToken).ConfigureAwait(false);
        var filter = new Dictionary<string, VariantValue>
        {
            ["UUIDs"] = VariantValue.Array(serviceUuids.ToArray()),
            ["Transport"] = transport,
            ["DuplicateData"] = false,
        };
        await _bus.CallMethodAsync(
            _adapterPath,
            BluezDbus.AdapterInterface,
            "SetDiscoveryFilter",
            "a{sv}",
            filter,
            cancellationToken).ConfigureAwait(false);
        await _bus.CallMethodAsync(
            _adapterPath,
            BluezDbus.AdapterInterface,
            "StartDiscovery",
            string.Empty,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task StopDiscoveryAsync()
    {
        if (_adapterPath is null)
        {
            return;
        }

        await _bus.CallMethodAsync(
            _adapterPath,
            BluezDbus.AdapterInterface,
            "StopDiscovery",
            string.Empty,
            null,
            CancellationToken.None).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<BluezDeviceAdvertisement> WatchAdvertisementsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await EnsureAdvertisementWatchAsync(cancellationToken).ConfigureAwait(false);
        var objects = await _bus.GetManagedObjectsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (path, obj) in objects)
        {
            RememberInterfaces(path, obj.Interfaces);
            if (TryCreateAdvertisement(path, obj.Interfaces, out var advertisement))
            {
                _advertisements.Writer.TryWrite(advertisement);
            }
        }

        await foreach (var advertisement in _advertisements.Reader.ReadAllAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return advertisement;
        }
    }

    public async Task ConnectAsync(string devicePath, CancellationToken cancellationToken)
    {
        await _bus.CallMethodAsync(
            devicePath,
            BluezDbus.DeviceInterface,
            "Connect",
            string.Empty,
            null,
            cancellationToken).ConfigureAwait(false);
        await WaitForServicesResolvedAsync(devicePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task PairAsync(string devicePath, CancellationToken cancellationToken)
    {
        await _bus.RegisterNoInputNoOutputAgentAsync(
            BluezDbus.AgentPath,
            "NoInputNoOutput",
            cancellationToken).ConfigureAwait(false);
        try
        {
            await _bus.CallMethodAsync(
                devicePath,
                BluezDbus.DeviceInterface,
                "Pair",
                string.Empty,
                null,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _bus.UnregisterAgentAsync(BluezDbus.AgentPath).ConfigureAwait(false);
        }
    }

    public async Task<string> FindHeartRateMeasurementCharacteristicAsync(
        string devicePath,
        CancellationToken cancellationToken)
    {
        var objects = await _bus.GetManagedObjectsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (path, obj) in objects)
        {
            if (!path.StartsWith(devicePath + "/", StringComparison.Ordinal)
                || !obj.Interfaces.TryGetValue(BluezDbus.GattCharacteristicInterface, out var properties)
                || !TryGetString(properties, "UUID", out string? uuid)
                || !string.Equals(uuid, BluezUuids.HeartRateMeasurement, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return path;
        }

        throw new InvalidOperationException(
            $"Heart Rate Measurement characteristic ({BluezUuids.HeartRateMeasurement}) was not found under {devicePath}.");
    }

    public async Task StartNotifyAsync(
        string characteristicPath,
        Action<byte[]> notificationHandler,
        CancellationToken cancellationToken)
    {
        _notificationHandlers[characteristicPath] = notificationHandler;
        await EnsurePropertiesWatchAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _bus.CallMethodAsync(
                characteristicPath,
                BluezDbus.GattCharacteristicInterface,
                "StartNotify",
                string.Empty,
                null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPairingRequired(ex))
        {
            _notificationHandlers.Remove(characteristicPath);
            throw new BluezPairingRequiredException("BlueZ requires pairing before notifications can start.", ex);
        }
    }

    public async Task StopNotifyAsync(string characteristicPath)
    {
        _notificationHandlers.Remove(characteristicPath);
        await _bus.CallMethodAsync(
            characteristicPath,
            BluezDbus.GattCharacteristicInterface,
            "StopNotify",
            string.Empty,
            null,
            CancellationToken.None).ConfigureAwait(false);
    }

    public Task DisconnectAsync(string devicePath) =>
        _bus.CallMethodAsync(
            devicePath,
            BluezDbus.DeviceInterface,
            "Disconnect",
            string.Empty,
            null,
            CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        if (_ownsBus)
        {
            await _bus.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<string> FindAdapterPathAsync(CancellationToken cancellationToken)
    {
        var objects = await _bus.GetManagedObjectsAsync(cancellationToken).ConfigureAwait(false);
        var firstAdapterPath = objects.FirstOrDefault(static pair =>
            pair.Value.Interfaces.ContainsKey(BluezDbus.AdapterInterface)).Key;
        foreach (var (path, obj) in objects)
        {
            if (obj.Interfaces.TryGetValue(BluezDbus.AdapterInterface, out var properties)
                && (!properties.TryGetValue("Powered", out var powered) || powered.GetBool()))
            {
                return path;
            }
        }

        return firstAdapterPath
            ?? throw new InvalidOperationException("No BlueZ adapter object was found on the system bus.");
    }

    private async Task EnsureAdvertisementWatchAsync(CancellationToken cancellationToken)
    {
        if (_watchingAdvertisements)
        {
            return;
        }

        _watchingAdvertisements = true;
        _subscriptions.Add(await _bus.WatchInterfacesAddedAsync(OnInterfacesAdded, cancellationToken)
            .ConfigureAwait(false));
        _subscriptions.Add(await _bus.WatchPropertiesChangedAsync(OnPropertiesChanged, cancellationToken)
            .ConfigureAwait(false));
    }

    private async Task EnsurePropertiesWatchAsync(CancellationToken cancellationToken)
    {
        if (_watchingProperties)
        {
            return;
        }

        _watchingProperties = true;
        _subscriptions.Add(await _bus.WatchPropertiesChangedAsync(OnPropertiesChanged, cancellationToken)
            .ConfigureAwait(false));
    }

    private async Task WaitForServicesResolvedAsync(
        string devicePath,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ServicesResolvedTimeout);
        while (true)
        {
            var resolved = await _bus.GetPropertyAsync(
                devicePath,
                BluezDbus.DeviceInterface,
                "ServicesResolved",
                timeoutCts.Token).ConfigureAwait(false);
            if (resolved.GetBool())
            {
                return;
            }

            await Task.Delay(ServicesResolvedPollInterval, timeoutCts.Token).ConfigureAwait(false);
        }
    }

    private void OnInterfacesAdded(BluezInterfacesAdded added)
    {
        RememberInterfaces(added.ObjectPath, added.Interfaces);
        if (TryCreateAdvertisement(added.ObjectPath, added.Interfaces, out var advertisement))
        {
            _advertisements.Writer.TryWrite(advertisement);
        }
    }

    private void OnPropertiesChanged(BluezPropertiesChanged changed)
    {
        if (changed.Interface == BluezDbus.DeviceInterface
            && changed.Changed.Count > 0)
        {
            MergeProperties(changed.ObjectPath, changed.Interface, changed.Changed);
            if (_knownInterfaces.TryGetValue(changed.ObjectPath, out var interfaces)
                && TryCreateAdvertisement(changed.ObjectPath, interfaces, out var advertisement))
            {
                _advertisements.Writer.TryWrite(advertisement);
            }
        }

        if (changed.Interface != BluezDbus.GattCharacteristicInterface
            || !changed.Changed.TryGetValue("Value", out var value)
            || !_notificationHandlers.TryGetValue(changed.ObjectPath, out var handler))
        {
            return;
        }

        handler(value.GetArray<byte>());
    }

    private static bool TryCreateAdvertisement(
        string objectPath,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, VariantValue>> interfaces,
        out BluezDeviceAdvertisement advertisement)
    {
        advertisement = default!;
        if (!interfaces.TryGetValue(BluezDbus.DeviceInterface, out var properties)
            || !TryGetString(properties, "Address", out string? address))
        {
            return false;
        }

        string addressType = TryGetString(properties, "AddressType", out string? type) ? type : "unknown";
        string name = TryGetString(properties, "Name", out string? deviceName)
            ? deviceName
            : TryGetString(properties, "Alias", out string? alias) ? alias : string.Empty;
        string[] serviceUuids = properties.TryGetValue("UUIDs", out var uuids)
            ? uuids.GetArray<string>()
            : [];
        short rssi = properties.TryGetValue("RSSI", out var rssiValue)
            ? rssiValue.GetInt16()
            : (short)0;
        advertisement = new BluezDeviceAdvertisement(
            objectPath,
            address,
            addressType,
            name,
            serviceUuids,
            rssi);
        return true;
    }

    private void RememberInterfaces(
        string objectPath,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, VariantValue>> interfaces)
    {
        if (!_knownInterfaces.TryGetValue(objectPath, out var existing))
        {
            existing = [];
            _knownInterfaces[objectPath] = existing;
        }

        foreach (var (name, properties) in interfaces)
        {
            existing[name] = properties;
        }
    }

    private void MergeProperties(
        string objectPath,
        string interfaceName,
        IReadOnlyDictionary<string, VariantValue> changed)
    {
        if (!_knownInterfaces.TryGetValue(objectPath, out var interfaces))
        {
            interfaces = [];
            _knownInterfaces[objectPath] = interfaces;
        }

        var merged = interfaces.TryGetValue(interfaceName, out var existing)
            ? new Dictionary<string, VariantValue>(existing)
            : [];
        foreach (var (name, value) in changed)
        {
            merged[name] = value;
        }

        interfaces[interfaceName] = merged;
    }

    private static bool IsPairingRequired(Exception exception)
    {
        string? errorName = exception switch
        {
            BluezDbusCallException bluez => bluez.ErrorName,
            DBusException dbus => dbus.ErrorName,
            _ => null,
        };

        return errorName is
            "org.bluez.Error.NotAuthorized" or
            "org.bluez.Error.AuthenticationFailed" or
            "org.bluez.Error.AuthenticationRejected" or
            "org.bluez.Error.AuthenticationTimeout";
    }

    private static bool TryGetString(
        IReadOnlyDictionary<string, VariantValue> properties,
        string name,
        out string value)
    {
        if (properties.TryGetValue(name, out var variant))
        {
            value = variant.GetString();
            return true;
        }

        value = string.Empty;
        return false;
    }
}
