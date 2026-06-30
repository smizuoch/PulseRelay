using Tmds.DBus.Protocol;

namespace PulseRelay.LinuxBle;

public static class BluezDbus
{
    public const string Service = "org.bluez";
    public const string ObjectManagerPath = "/";
    public const string AdapterInterface = "org.bluez.Adapter1";
    public const string DeviceInterface = "org.bluez.Device1";
    public const string GattServiceInterface = "org.bluez.GattService1";
    public const string GattCharacteristicInterface = "org.bluez.GattCharacteristic1";
    public const string AgentManagerInterface = "org.bluez.AgentManager1";
    public const string AgentInterface = "org.bluez.Agent1";
    public const string AgentPath = "/io/github/pulserelay/agent";
    public const string ObjectManagerInterface = "org.freedesktop.DBus.ObjectManager";
    public const string PropertiesInterface = "org.freedesktop.DBus.Properties";
}

public sealed record BluezObject(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, VariantValue>> Interfaces)
{
    public static BluezObject WithInterface(
        string name,
        IReadOnlyDictionary<string, VariantValue> properties) =>
        new(new Dictionary<string, IReadOnlyDictionary<string, VariantValue>>
        {
            [name] = properties,
        });
}

public sealed record BluezInterfacesAdded(
    string ObjectPath,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, VariantValue>> Interfaces);

public sealed record BluezPropertiesChanged(
    string ObjectPath,
    string Interface,
    IReadOnlyDictionary<string, VariantValue> Changed);

public interface IBluezBus : IAsyncDisposable
{
    Task<IReadOnlyDictionary<string, BluezObject>> GetManagedObjectsAsync(CancellationToken cancellationToken);

    Task CallMethodAsync(
        string path,
        string @interface,
        string member,
        string signature,
        IReadOnlyDictionary<string, VariantValue>? dictionaryBody,
        CancellationToken cancellationToken);

    Task<VariantValue> GetPropertyAsync(
        string path,
        string @interface,
        string propertyName,
        CancellationToken cancellationToken);

    ValueTask<IDisposable> WatchInterfacesAddedAsync(
        Action<BluezInterfacesAdded> handler,
        CancellationToken cancellationToken);

    ValueTask<IDisposable> WatchPropertiesChangedAsync(
        Action<BluezPropertiesChanged> handler,
        CancellationToken cancellationToken);

    Task RegisterNoInputNoOutputAgentAsync(
        string agentPath,
        string capability,
        CancellationToken cancellationToken);

    Task UnregisterAgentAsync(string agentPath);
}
