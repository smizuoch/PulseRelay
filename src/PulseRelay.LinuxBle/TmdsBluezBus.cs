using System.Diagnostics.CodeAnalysis;
using Tmds.DBus.Protocol;

namespace PulseRelay.LinuxBle;

[ExcludeFromCodeCoverage(Justification = "Thin system D-Bus adapter; BlueZ behavior is covered through IBluezBus contract tests.")]
public sealed class TmdsBluezBus : IBluezBus
{
    private readonly Connection _connection;
    private readonly HashSet<string> _registeredAgentPaths = [];

    public TmdsBluezBus(Connection connection) => _connection = connection;

    public async Task<IReadOnlyDictionary<string, BluezObject>> GetManagedObjectsAsync(
        CancellationToken cancellationToken)
    {
        await _connection.ConnectAsync().ConfigureAwait(false);
        var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            BluezDbus.Service,
            BluezDbus.ObjectManagerPath,
            BluezDbus.ObjectManagerInterface,
            "GetManagedObjects",
            string.Empty,
            MessageFlags.None);
        return await _connection.CallMethodAsync(
            writer.CreateMessage(),
            static (message, _) => ReadManagedObjects(message),
            null).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CallMethodAsync(
        string path,
        string @interface,
        string member,
        string signature,
        IReadOnlyDictionary<string, VariantValue>? dictionaryBody,
        CancellationToken cancellationToken)
    {
        await _connection.ConnectAsync().ConfigureAwait(false);
        var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(BluezDbus.Service, path, @interface, member, signature, MessageFlags.None);
        if (dictionaryBody is not null)
        {
            writer.WriteDictionary(dictionaryBody);
        }

        try
        {
            await _connection.CallMethodAsync(writer.CreateMessage())
                .WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DBusException ex)
        {
            throw new BluezDbusCallException(ex.ErrorName, ex.ErrorMessage);
        }
    }

    public async Task<VariantValue> GetPropertyAsync(
        string path,
        string @interface,
        string propertyName,
        CancellationToken cancellationToken)
    {
        await _connection.ConnectAsync().ConfigureAwait(false);
        var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            BluezDbus.Service,
            path,
            BluezDbus.PropertiesInterface,
            "Get",
            "ss",
            MessageFlags.None);
        writer.WriteString(@interface);
        writer.WriteString(propertyName);
        return await _connection.CallMethodAsync(
            writer.CreateMessage(),
            static (message, _) => message.GetBodyReader().ReadVariantValue(),
            null).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IDisposable> WatchInterfacesAddedAsync(
        Action<BluezInterfacesAdded> handler,
        CancellationToken cancellationToken)
    {
        await _connection.ConnectAsync().ConfigureAwait(false);
        return await _connection.AddMatchAsync(
            new MatchRule
            {
                Type = MessageType.Signal,
                Sender = BluezDbus.Service,
                Interface = BluezDbus.ObjectManagerInterface,
                Member = "InterfacesAdded",
                PathNamespace = "/org/bluez",
            },
            static (message, _) => TmdsBluezBus.ReadInterfacesAdded(message),
            static (exception, added, _, state) =>
            {
                if (exception is null)
                {
                    ((Action<BluezInterfacesAdded>)state!)(added);
                }
            },
            null,
            handler,
            false,
            ObserverFlags.None).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IDisposable> WatchPropertiesChangedAsync(
        Action<BluezPropertiesChanged> handler,
        CancellationToken cancellationToken)
    {
        await _connection.ConnectAsync().ConfigureAwait(false);
        return await _connection.AddMatchAsync(
            new MatchRule
            {
                Type = MessageType.Signal,
                Sender = BluezDbus.Service,
                Interface = BluezDbus.PropertiesInterface,
                Member = "PropertiesChanged",
                PathNamespace = "/org/bluez",
            },
            static (message, _) => TmdsBluezBus.ReadPropertiesChanged(message),
            static (exception, changed, _, state) =>
            {
                if (exception is null)
                {
                    ((Action<BluezPropertiesChanged>)state!)(changed);
                }
            },
            null,
            handler,
            false,
            ObserverFlags.None).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RegisterNoInputNoOutputAgentAsync(
        string agentPath,
        string capability,
        CancellationToken cancellationToken)
    {
        await _connection.ConnectAsync().ConfigureAwait(false);
        if (_registeredAgentPaths.Add(agentPath))
        {
            _connection.AddMethodHandler(new BluezNoInputNoOutputAgent(agentPath));
        }

        try
        {
            await CallAgentManagerAsync(
                "RegisterAgent",
                "os",
                writer =>
                {
                    writer.WriteObjectPath(agentPath);
                    writer.WriteString(capability);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _connection.RemoveMethodHandler(agentPath);
            _registeredAgentPaths.Remove(agentPath);
            throw;
        }
    }

    public async Task UnregisterAgentAsync(string agentPath)
    {
        try
        {
            await CallAgentManagerAsync(
                "UnregisterAgent",
                "o",
                writer => writer.WriteObjectPath(agentPath),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (BluezDbusCallException)
        {
        }
        finally
        {
            _connection.RemoveMethodHandler(agentPath);
            _registeredAgentPaths.Remove(agentPath);
        }
    }

    public ValueTask DisposeAsync()
    {
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task CallAgentManagerAsync(
        string member,
        string signature,
        Action<MessageWriter> writeBody,
        CancellationToken cancellationToken)
    {
        var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            BluezDbus.Service,
            BluezDbus.ObjectManagerPath,
            BluezDbus.AgentManagerInterface,
            member,
            signature,
            MessageFlags.None);
        writeBody(writer);

        try
        {
            await _connection.CallMethodAsync(writer.CreateMessage())
                .WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DBusException ex)
        {
            throw new BluezDbusCallException(ex.ErrorName, ex.ErrorMessage);
        }
    }

    private static IReadOnlyDictionary<string, BluezObject> ReadManagedObjects(Message message)
    {
        var reader = message.GetBodyReader();
        var objects = new Dictionary<string, BluezObject>();
        var objectsEnd = reader.ReadDictionaryStart();
        while (reader.HasNext(objectsEnd))
        {
            string path = reader.ReadObjectPathAsString();
            objects[path] = new BluezObject(ReadInterfaceDictionary(ref reader));
        }

        return objects;
    }

    private static BluezInterfacesAdded ReadInterfacesAdded(Message message)
    {
        var reader = message.GetBodyReader();
        string path = reader.ReadObjectPathAsString();
        return new BluezInterfacesAdded(path, ReadInterfaceDictionary(ref reader));
    }

    private static BluezPropertiesChanged ReadPropertiesChanged(Message message)
    {
        var reader = message.GetBodyReader();
        string @interface = reader.ReadString();
        var changed = reader.ReadDictionaryOfStringToVariantValue();
        _ = reader.ReadArrayOfString();
        return new BluezPropertiesChanged(message.PathAsString ?? string.Empty, @interface, changed);
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, VariantValue>> ReadInterfaceDictionary(
        ref Reader reader)
    {
        var interfaces = new Dictionary<string, IReadOnlyDictionary<string, VariantValue>>();
        var interfacesEnd = reader.ReadDictionaryStart();
        while (reader.HasNext(interfacesEnd))
        {
            string interfaceName = reader.ReadString();
            interfaces[interfaceName] = reader.ReadDictionaryOfStringToVariantValue();
        }

        return interfaces;
    }
}
