using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace PulseRelay.WindowsBle;

/// <summary>One observed BLE advertisement, flattened for logging and matching.</summary>
public sealed record BleAdvertisementReport(
    ulong Address,
    BluetoothAddressType AddressType,
    string LocalName,
    IReadOnlyList<Guid> ServiceUuids,
    short RssiDbm,
    BluetoothLEAdvertisementType AdvertisementType,
    DateTimeOffset Timestamp);

/// <summary>
/// Advertisement watcher with two modes: filtered on the Heart Rate Service (0x180D),
/// or unfiltered scan-all for diagnostics. Every advertisement and every watcher state
/// change is logged so a failing first run can be triaged from the log alone:
/// device absent in scan-all = not advertising; present in scan-all but absent in
/// filtered = no visible 0x180D; watcher stopped with error = Windows-side failure.
/// </summary>
public sealed class BleAdvertisementScanner : IDisposable
{
    private readonly BluetoothLEAdvertisementWatcher _watcher;
    private readonly ILogger _logger;
    private readonly bool _logRepeats;
    private readonly HashSet<ulong> _seenAddresses = [];
    private readonly Lock _lock = new();

    public BleAdvertisementScanner(ILogger logger, bool filterHeartRateService, bool logRepeats = false)
    {
        _logger = logger;
        _logRepeats = logRepeats;
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
        };

        if (filterHeartRateService)
        {
            _watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(GattUuids.HeartRateService);
        }

        _watcher.Received += OnReceived;
        _watcher.Stopped += OnStopped;
    }

    /// <summary>Raised for each advertisement (deduplicated per address unless logRepeats is set).</summary>
    public event EventHandler<BleAdvertisementReport>? AdvertisementReceived;

    private event EventHandler<BluetoothError>? WatcherStopped;

    public void Start()
    {
        _logger.LogInformation(
            "Starting BLE advertisement watcher (mode={Mode}, scanning=Active)",
            _watcher.AdvertisementFilter.Advertisement.ServiceUuids.Count > 0 ? "filtered:0x180D" : "scan-all");
        _watcher.Start();
        _logger.LogDebug("Watcher status after Start: {Status}", _watcher.Status);
    }

    public void Stop()
    {
        if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
        {
            _watcher.Stop();
        }
    }

    /// <summary>
    /// Runs the watcher until an advertisement matches <paramref name="predicate"/> or the
    /// timeout elapses. Returns null on timeout. The watcher is stopped before returning.
    /// </summary>
    public async Task<BleAdvertisementReport?> WaitForFirstAsync(
        Func<BleAdvertisementReport, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var found = new TaskCompletionSource<BleAdvertisementReport>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource<BluetoothError>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, BleAdvertisementReport report)
        {
            if (predicate(report))
            {
                found.TrySetResult(report);
            }
        }

        void StoppedHandler(object? sender, BluetoothError error) => stopped.TrySetResult(error);

        AdvertisementReceived += Handler;
        WatcherStopped += StoppedHandler;
        try
        {
            Start();
            var timeoutTask = Task.Delay(timeout, cancellationToken);
            var completed = await Task.WhenAny(found.Task, stopped.Task, timeoutTask).ConfigureAwait(false);
            if (completed == found.Task)
            {
                return await found.Task.ConfigureAwait(false);
            }

            if (completed == stopped.Task)
            {
                var error = await stopped.Task.ConfigureAwait(false);
                throw new InvalidOperationException($"BLE advertisement watcher stopped unexpectedly: {error}.");
            }

            await timeoutTask.ConfigureAwait(false);
            return null;
        }
        finally
        {
            AdvertisementReceived -= Handler;
            WatcherStopped -= StoppedHandler;
            Stop();
        }
    }

    public void Dispose()
    {
        Stop();
        _watcher.Received -= OnReceived;
        _watcher.Stopped -= OnStopped;
    }

    /// <summary>Formats a 48-bit Bluetooth address as AA:BB:CC:DD:EE:FF.</summary>
    public static string FormatAddress(ulong address) =>
        string.Join(":", BitConverter.GetBytes(address).Take(6).Reverse().Select(b => b.ToString("X2")));

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        if (!_logRepeats)
        {
            lock (_lock)
            {
                if (!_seenAddresses.Add(args.BluetoothAddress))
                {
                    return;
                }
            }
        }

        var report = new BleAdvertisementReport(
            args.BluetoothAddress,
            args.BluetoothAddressType,
            args.Advertisement.LocalName,
            [.. args.Advertisement.ServiceUuids],
            args.RawSignalStrengthInDBm,
            args.AdvertisementType,
            args.Timestamp);

        _logger.LogInformation(
            "Advertisement: address={Address} (RPA - session-scoped, do not persist) addressType={AddressType} "
            + "name={Name} services=[{Services}] rssi={Rssi} dBm type={Type}",
            FormatAddress(report.Address),
            report.AddressType,
            string.IsNullOrEmpty(report.LocalName) ? "<none>" : report.LocalName,
            report.ServiceUuids.Count == 0 ? "<none>" : string.Join(", ", report.ServiceUuids),
            report.RssiDbm,
            report.AdvertisementType);

        AdvertisementReceived?.Invoke(this, report);
    }

    private void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        WatcherStopped?.Invoke(this, args.Error);
        if (args.Error == BluetoothError.Success)
        {
            _logger.LogInformation("BLE advertisement watcher stopped (no error)");
        }
        else
        {
            _logger.LogError(
                "BLE advertisement watcher stopped with error: {Error}. "
                + "Check Bluetooth radio state and Windows Settings > Privacy & security > Bluetooth.",
                args.Error);
        }
    }
}
