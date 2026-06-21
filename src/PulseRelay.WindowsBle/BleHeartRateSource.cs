using Microsoft.Extensions.Logging;
using PulseRelay.Core.HeartRate;
using PulseRelay.Core.Sources;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace PulseRelay.WindowsBle;

/// <summary>
/// Heart-rate source backed by the Windows BLE GATT client:
/// scan (0x180D) -> connect -> subscribe to Heart Rate Measurement (0x2A37) notifications.
/// Pairing (SMP Security Request) is handled when GATT access requires encryption, and all
/// GATT calls use the *WithResult / *Result APIs so status and protocol-error bytes are logged.
/// </summary>
public sealed class BleHeartRateSource : IHeartRateSource
{
    // ATT protocol errors that indicate the link must be paired/encrypted first.
    private const byte AttInsufficientAuthentication = 0x05;
    private const byte AttInsufficientAuthorization = 0x08;
    private const byte AttInsufficientEncryption = 0x0F;
    private static readonly TimeSpan GattOperationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StopOperationTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger _logger;
    private readonly string? _nameFilter;
    private readonly TimeSpan _scanTimeout;

    private BluetoothLEDevice? _device;
    private GattDeviceService? _service;
    private GattCharacteristic? _characteristic;
    private string _deviceName = "<unknown>";
    private CancellationTokenSource? _lifetimeCts;

    public BleHeartRateSource(ILogger<BleHeartRateSource> logger, string? nameFilter, TimeSpan scanTimeout)
    {
        _logger = logger;
        _nameFilter = nameFilter;
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
            throw new InvalidOperationException("BLE source is already started.");
        }

        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var lifetimeToken = _lifetimeCts.Token;
        try
        {
            SetState(HeartRateSourceState.Scanning);
            var report = await ScanForDeviceAsync(lifetimeToken).ConfigureAwait(false);

            SetState(HeartRateSourceState.Connecting);
            _device = await ConnectAsync(report.Address, lifetimeToken).ConfigureAwait(false);

            await ReadPeripheralDeviceNameAsync(_device, lifetimeToken).ConfigureAwait(false);

            SetState(HeartRateSourceState.Subscribing);
            var outcome = await TrySubscribeCoreAsync(_device, lifetimeToken).ConfigureAwait(false);
            bool subscribed = outcome.Success
                || await PairAndRetryAsync(outcome.NeedsPairing, lifetimeToken).ConfigureAwait(false);

            if (!subscribed)
            {
                throw new InvalidOperationException(
                    "Could not subscribe to Heart Rate Measurement notifications. See log for GATT statuses.");
            }

            SetState(HeartRateSourceState.Subscribed);
            _logger.LogInformation("Subscribed, waiting for first Heart Rate Measurement notification...");
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
        var characteristic = _characteristic;
        if (characteristic is not null)
        {
            try
            {
                var operation = characteristic.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None);
                var result = await AwaitAsync(
                    operation,
                    CancellationToken.None,
                    StopOperationTimeout).ConfigureAwait(false);
                _logger.LogInformation(
                    "Unsubscribe CCCD write: status={Status} protocolError={ProtocolError}",
                    result.Status,
                    FormatProtocolError(result.ProtocolError));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear CCCD on stop (device may already be gone)");
            }
        }

        await CleanupAsync().ConfigureAwait(false);
        SetState(HeartRateSourceState.Disconnected);
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task<BleAdvertisementReport> ScanForDeviceAsync(CancellationToken cancellationToken)
    {
        using var scanner = new BleAdvertisementScanner(_logger, filterHeartRateService: true, logRepeats: true);

        bool Matches(BleAdvertisementReport report) =>
            _nameFilter is null
            || report.LocalName.Contains(_nameFilter, StringComparison.OrdinalIgnoreCase);

        var report = await scanner.WaitForFirstAsync(Matches, _scanTimeout, cancellationToken);
        if (report is null)
        {
            throw new TimeoutException(
                $"No device advertising Heart Rate Service (0x180D)"
                + (_nameFilter is null ? string.Empty : $" with name containing \"{_nameFilter}\"")
                + $" found within {_scanTimeout.TotalSeconds:0} s. "
                + "Check that the tracker shows 'HR on equipment' and is not connected to another app, "
                + "then try 'scan --all' to see whether it advertises at all.");
        }

        _deviceName = string.IsNullOrEmpty(report.LocalName) ? "<unnamed>" : report.LocalName;
        _logger.LogInformation(
            "Selected device: address={Address} (RPA - session-scoped, do not persist) name={Name} rssi={Rssi} dBm",
            BleAdvertisementScanner.FormatAddress(report.Address),
            _deviceName,
            report.RssiDbm);
        return report;
    }

    private async Task<BluetoothLEDevice> ConnectAsync(ulong address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operation = BluetoothLEDevice.FromBluetoothAddressAsync(address);
        var device = await AwaitAsync(operation, cancellationToken, GattOperationTimeout).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"BluetoothLEDevice.FromBluetoothAddressAsync returned null for "
                + $"{BleAdvertisementScanner.FormatAddress(address)}. The device may have rotated its RPA "
                + "or gone out of range; re-scan instead of reusing old addresses.");

        device.ConnectionStatusChanged += OnConnectionStatusChanged;
        _logger.LogInformation(
            "Device object acquired: name={Name} addressType={AddressType} connectionStatus={Status}",
            device.Name,
            device.BluetoothAddressType,
            device.ConnectionStatus);
        return device;
    }

    /// <summary>
    /// Best-effort read of the peripheral's Device Name (0x2A00). Never fatal — this is
    /// diagnostic evidence for the verification log, not part of the streaming path.
    /// </summary>
    private async Task ReadPeripheralDeviceNameAsync(
        BluetoothLEDevice device,
        CancellationToken cancellationToken)
    {
        try
        {
            var services = await AwaitAsync(
                device.GetGattServicesForUuidAsync(
                    GattUuids.GenericAccessService, BluetoothCacheMode.Uncached),
                cancellationToken,
                GattOperationTimeout).ConfigureAwait(false);
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
            {
                _logger.LogDebug(
                    "Generic Access service (0x1800) not readable: status={Status} protocolError={ProtocolError}",
                    services.Status,
                    FormatProtocolError(services.ProtocolError));
                foreach (var service in services.Services)
                {
                    service.Dispose();
                }

                return;
            }

            try
            {
                var characteristics = await AwaitAsync(
                    services.Services[0].GetCharacteristicsForUuidAsync(
                        GattUuids.DeviceName, BluetoothCacheMode.Uncached),
                    cancellationToken,
                    GattOperationTimeout).ConfigureAwait(false);
                if (characteristics.Status != GattCommunicationStatus.Success
                    || characteristics.Characteristics.Count == 0)
                {
                    _logger.LogDebug(
                        "Device Name characteristic (0x2A00) not found: status={Status}", characteristics.Status);
                    return;
                }

                var read = await AwaitAsync(
                    characteristics.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached),
                    cancellationToken,
                    GattOperationTimeout).ConfigureAwait(false);
                if (read.Status == GattCommunicationStatus.Success)
                {
                    string name = CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, read.Value);
                    _logger.LogInformation("Peripheral Device Name (0x2A00): \"{Name}\"", name);
                    if (!string.IsNullOrEmpty(name))
                    {
                        _deviceName = name;
                    }
                }
                else
                {
                    _logger.LogDebug(
                        "Device Name (0x2A00) read failed: status={Status} protocolError={ProtocolError}",
                        read.Status,
                        FormatProtocolError(read.ProtocolError));
                }
            }
            finally
            {
                foreach (var service in services.Services)
                {
                    service.Dispose();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Peripheral Device Name read failed (non-fatal)");
        }
    }

    private async Task<bool> PairAndRetryAsync(bool needsPairing, CancellationToken cancellationToken)
    {
        if (!needsPairing || _device is null)
        {
            return false;
        }

        _logger.LogInformation(
            "GATT access denied or device unreachable - attempting pairing "
            + "(responding to the SMP Security Request; confirm any prompt on the tracker)");

        var status = await PairingHandler.PairAsync(
            _device.DeviceInformation,
            _logger,
            cancellationToken).ConfigureAwait(false);
        if (status is not (DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired))
        {
            return false;
        }

        // GATT handles acquired before pairing are stale once the link is re-encrypted.
        // Drop everything and reacquire device -> services -> characteristics from scratch.
        ulong address = _device.BluetoothAddress;
        _logger.LogInformation("Pairing succeeded; reacquiring device and GATT services from scratch");
        await CleanupDeviceAsync().ConfigureAwait(false);
        _device = await ConnectAsync(address, cancellationToken).ConfigureAwait(false);

        return (await TrySubscribeCoreAsync(_device, cancellationToken).ConfigureAwait(false)).Success;
    }

    private async Task<(bool Success, bool NeedsPairing)> TrySubscribeCoreAsync(
        BluetoothLEDevice device,
        CancellationToken cancellationToken)
    {
        var services = await AwaitAsync(
            device.GetGattServicesForUuidAsync(
                GattUuids.HeartRateService, BluetoothCacheMode.Uncached),
            cancellationToken,
            GattOperationTimeout).ConfigureAwait(false);
        _logger.LogInformation(
            "Heart Rate Service (0x180D) discovery: status={Status} protocolError={ProtocolError} count={Count}",
            services.Status,
            FormatProtocolError(services.ProtocolError),
            services.Services.Count);
        if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
        {
            foreach (var discoveredService in services.Services)
            {
                discoveredService.Dispose();
            }

            return (false, IsAuthRelated(services.Status, services.ProtocolError));
        }

        var service = services.Services[0];
        bool ownershipTransferred = false;
        try
        {
            var characteristics = await AwaitAsync(
                service.GetCharacteristicsForUuidAsync(
                    GattUuids.HeartRateMeasurement, BluetoothCacheMode.Uncached),
                cancellationToken,
                GattOperationTimeout).ConfigureAwait(false);
            _logger.LogInformation(
                "Heart Rate Measurement (0x2A37) discovery: status={Status} protocolError={ProtocolError} count={Count}",
                characteristics.Status,
                FormatProtocolError(characteristics.ProtocolError),
                characteristics.Characteristics.Count);
            if (characteristics.Status != GattCommunicationStatus.Success
                || characteristics.Characteristics.Count == 0)
            {
                return (false, IsAuthRelated(characteristics.Status, characteristics.ProtocolError));
            }

            var characteristic = characteristics.Characteristics[0];
            _logger.LogInformation(
                "Characteristic properties: {Properties} (expect Notify)", characteristic.CharacteristicProperties);

            var write = await AwaitAsync(
                characteristic.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify),
                cancellationToken,
                GattOperationTimeout).ConfigureAwait(false);
            _logger.LogInformation(
                "CCCD write (Notify): status={Status} protocolError={ProtocolError}",
                write.Status,
                FormatProtocolError(write.ProtocolError));
            if (write.Status != GattCommunicationStatus.Success)
            {
                return (false, IsAuthRelated(write.Status, write.ProtocolError));
            }

            characteristic.ValueChanged += OnValueChanged;
            _service = service;
            _characteristic = characteristic;
            ownershipTransferred = true;
            return (true, false);
        }
        finally
        {
            foreach (var extraService in services.Services.Skip(1))
            {
                extraService.Dispose();
            }

            if (!ownershipTransferred)
            {
                service.Dispose();
            }
        }
    }

    private static bool IsAuthRelated(GattCommunicationStatus status, byte? protocolError) =>
        status is GattCommunicationStatus.AccessDenied or GattCommunicationStatus.Unreachable
        || protocolError is AttInsufficientAuthentication
            or AttInsufficientAuthorization
            or AttInsufficientEncryption;

    private static string FormatProtocolError(byte? protocolError) =>
        protocolError is byte b ? $"0x{b:X2}" : "<none>";

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        byte[] payload = new byte[args.CharacteristicValue.Length];
        using (var reader = DataReader.FromBuffer(args.CharacteristicValue))
        {
            reader.ReadBytes(payload);
        }

        _logger.LogDebug("0x2A37 notification: {Hex}", Convert.ToHexString(payload));

        HeartRateSample sample;
        try
        {
            sample = HeartRateMeasurementParser.Parse(payload, args.Timestamp);
        }
        catch (FormatException ex)
        {
            // Keep the subscription alive; one malformed packet must not kill the stream.
            _logger.LogWarning("Failed to parse Heart Rate Measurement: {Message}", ex.Message);
            return;
        }

        if (State != HeartRateSourceState.Streaming)
        {
            SetState(HeartRateSourceState.Streaming);
        }

        SampleReceived?.Invoke(this, sample);
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        _logger.LogInformation("Connection status changed: {Status}", sender.ConnectionStatus);
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected
            && State is not (HeartRateSourceState.Idle
                or HeartRateSourceState.Disconnected
                or HeartRateSourceState.Failed))
        {
            SetState(HeartRateSourceState.Disconnected);
        }
    }

    private Task CleanupAsync()
    {
        _lifetimeCts?.Dispose();
        _lifetimeCts = null;
        return CleanupDeviceAsync();
    }

    private Task CleanupDeviceAsync()
    {
        if (_characteristic is not null)
        {
            _characteristic.ValueChanged -= OnValueChanged;
            _characteristic = null;
        }

        _service?.Dispose();
        _service = null;

        if (_device is not null)
        {
            _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            _device.Dispose();
            _device = null;
        }

        return Task.CompletedTask;
    }

    private static async Task<T> AwaitAsync<T>(
        IAsyncOperation<T> operation,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            return await operation.AsTask(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Bluetooth operation did not complete within {timeout.TotalSeconds:0} seconds.");
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
