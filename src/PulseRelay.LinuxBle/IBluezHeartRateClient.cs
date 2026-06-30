namespace PulseRelay.LinuxBle;

public interface IBluezHeartRateClient : IAsyncDisposable
{
    Task StartDiscoveryAsync(
        IReadOnlyList<string> serviceUuids,
        string transport,
        CancellationToken cancellationToken);

    Task StopDiscoveryAsync();

    IAsyncEnumerable<BluezDeviceAdvertisement> WatchAdvertisementsAsync(CancellationToken cancellationToken);

    Task ConnectAsync(string devicePath, CancellationToken cancellationToken);

    Task PairAsync(string devicePath, CancellationToken cancellationToken);

    Task<string> FindHeartRateMeasurementCharacteristicAsync(
        string devicePath,
        CancellationToken cancellationToken);

    Task StartNotifyAsync(
        string characteristicPath,
        Action<byte[]> notificationHandler,
        CancellationToken cancellationToken);

    Task StopNotifyAsync(string characteristicPath);

    Task DisconnectAsync(string devicePath);
}
