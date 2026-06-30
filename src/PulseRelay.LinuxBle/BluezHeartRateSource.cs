using Microsoft.Extensions.Logging;
using PulseRelay.Core.HeartRate;
using PulseRelay.Core.Sources;

namespace PulseRelay.LinuxBle;

public sealed class BluezHeartRateSource : IHeartRateSource
{
    private readonly IBluezHeartRateClient _client;
    private readonly ILogger<BluezHeartRateSource> _logger;
    private readonly string? _nameFilter;
    private readonly TimeSpan _scanTimeout;
    private CancellationTokenSource? _lifetimeCts;
    private string? _devicePath;
    private string? _characteristicPath;
    private string _deviceName = "<unknown>";

    public BluezHeartRateSource(
        IBluezHeartRateClient client,
        ILogger<BluezHeartRateSource> logger,
        string? nameFilter,
        TimeSpan scanTimeout)
    {
        _client = client;
        _logger = logger;
        _nameFilter = string.IsNullOrWhiteSpace(nameFilter) ? null : nameFilter;
        _scanTimeout = scanTimeout;
    }

    public string Description => $"BLE {_deviceName}";

    public HeartRateSourceState State { get; private set; } = HeartRateSourceState.Idle;

    public event EventHandler<HeartRateSample>? SampleReceived;

    public event EventHandler<HeartRateSourceState>? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_lifetimeCts is not null)
        {
            throw new InvalidOperationException("BlueZ source is already started.");
        }

        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var lifetimeToken = _lifetimeCts.Token;
        try
        {
            SetState(HeartRateSourceState.Scanning);
            await _client.StartDiscoveryAsync(
                [BluezUuids.HeartRateService],
                "le",
                lifetimeToken).ConfigureAwait(false);

            var device = await ScanForDeviceAsync(lifetimeToken).ConfigureAwait(false);
            _devicePath = device.ObjectPath;
            _deviceName = string.IsNullOrWhiteSpace(device.Name) ? "<unnamed>" : device.Name;
            _logger.LogInformation(
                "Selected BlueZ device: path={Path} address={Address} addressType={AddressType} name={Name} rssi={Rssi} dBm",
                device.ObjectPath,
                device.Address,
                device.AddressType,
                _deviceName,
                device.RssiDbm);

            SetState(HeartRateSourceState.Connecting);
            await _client.ConnectAsync(device.ObjectPath, lifetimeToken).ConfigureAwait(false);

            SetState(HeartRateSourceState.Subscribing);
            await SubscribeWithPairingRetryAsync(device.ObjectPath, lifetimeToken).ConfigureAwait(false);

            SetState(HeartRateSourceState.Subscribed);
            _logger.LogInformation("Subscribed via BlueZ, waiting for first Heart Rate Measurement notification...");
        }
        catch
        {
            SetState(HeartRateSourceState.Failed);
            await CleanupAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync()
    {
        _lifetimeCts?.Cancel();
        await CleanupAsync().ConfigureAwait(false);
        SetState(HeartRateSourceState.Disconnected);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<BluezDeviceAdvertisement> ScanForDeviceAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_scanTimeout);
        try
        {
            await foreach (var device in _client.WatchAdvertisementsAsync(timeoutCts.Token).ConfigureAwait(false))
            {
                if (!AdvertisesHeartRate(device) || !MatchesNameFilter(device))
                {
                    continue;
                }

                return device;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        throw new TimeoutException(
            $"No BlueZ device advertising Heart Rate Service (0x180D)"
            + (_nameFilter is null ? string.Empty : $" with name containing \"{_nameFilter}\"")
            + $" found within {_scanTimeout.TotalSeconds:0} s.");
    }

    private bool AdvertisesHeartRate(BluezDeviceAdvertisement device) =>
        device.ServiceUuids.Any(uuid => string.Equals(uuid, BluezUuids.HeartRateService, StringComparison.OrdinalIgnoreCase));

    private bool MatchesNameFilter(BluezDeviceAdvertisement device) =>
        _nameFilter is null || device.Name.Contains(_nameFilter, StringComparison.OrdinalIgnoreCase);

    private async Task SubscribeWithPairingRetryAsync(
        string devicePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await SubscribeCoreAsync(devicePath, cancellationToken).ConfigureAwait(false);
        }
        catch (BluezPairingRequiredException ex)
        {
            _logger.LogInformation(ex, "BlueZ GATT access requires pairing; pairing and retrying once.");
            await _client.PairAsync(devicePath, cancellationToken).ConfigureAwait(false);
            _characteristicPath = null;
            await SubscribeCoreAsync(devicePath, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SubscribeCoreAsync(
        string devicePath,
        CancellationToken cancellationToken)
    {
        _characteristicPath = await _client
            .FindHeartRateMeasurementCharacteristicAsync(devicePath, cancellationToken)
            .ConfigureAwait(false);
        await _client
            .StartNotifyAsync(_characteristicPath, OnNotification, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task CleanupAsync()
    {
        if (_characteristicPath is not null)
        {
            try
            {
                await _client.StopNotifyAsync(_characteristicPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop BlueZ notifications.");
            }

            _characteristicPath = null;
        }

        if (_devicePath is not null)
        {
            try
            {
                await _client.DisconnectAsync(_devicePath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to disconnect BlueZ device.");
            }

            _devicePath = null;
        }

        try
        {
            await _client.StopDiscoveryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to stop BlueZ discovery.");
        }

        _lifetimeCts?.Dispose();
        _lifetimeCts = null;
    }

    private void OnNotification(byte[] payload)
    {
        try
        {
            var sample = HeartRateMeasurementParser.Parse(payload, DateTimeOffset.UtcNow);
            if (State != HeartRateSourceState.Streaming)
            {
                SetState(HeartRateSourceState.Streaming);
            }

            SampleReceived?.Invoke(this, sample);
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Malformed Heart Rate Measurement notification from BlueZ.");
        }
    }

    private void SetState(HeartRateSourceState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, state);
    }
}
