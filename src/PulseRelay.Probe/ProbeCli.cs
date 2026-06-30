using Microsoft.Extensions.Logging;
using PulseRelay.Core.HeartRate;
using PulseRelay.Core.Sources;
#if !WINDOWS_BLE
using PulseRelay.LinuxBle;
#endif
using PulseRelay.Osc;
#if WINDOWS_BLE
using PulseRelay.WindowsBle;
#endif

namespace PulseRelay.Probe;

public static class ProbeCli
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (!ProbeOptions.TryParse(args, out var options, out string parseError))
        {
            error.WriteLine(parseError);
            error.WriteLine();
            error.WriteLine(ProbeOptions.Usage);
            return 2;
        }

        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(options.Verbose ? LogLevel.Debug : LogLevel.Information)
            .AddSimpleConsole(console =>
            {
                console.SingleLine = true;
                console.TimestampFormat = "HH:mm:ss.fff ";
            }));
        var logger = loggerFactory.CreateLogger("probe");

        try
        {
            return options.Command switch
            {
                ProbeCommand.Mock => await RunSourceAsync(
                    NewMockSource(options, loggerFactory),
                    options,
                    logger,
                    cancellationToken).ConfigureAwait(false),
#if WINDOWS_BLE
                ProbeCommand.Scan => await RunScanAsync(options, loggerFactory, cancellationToken)
                    .ConfigureAwait(false),
                ProbeCommand.Connect => await RunConnectAsync(
                    options,
                    loggerFactory,
                    logger,
                    cancellationToken).ConfigureAwait(false),
#else
                ProbeCommand.Scan => OperatingSystem.IsLinux()
                    ? await RunLinuxScanAsync(options, loggerFactory, cancellationToken).ConfigureAwait(false)
                    : BleUnavailable(error),
                ProbeCommand.Connect => OperatingSystem.IsLinux()
                    ? await RunLinuxConnectAsync(options, loggerFactory, logger, cancellationToken)
                        .ConfigureAwait(false)
                    : BleUnavailable(error),
#endif
                _ => 2,
            };
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Probe failed: {Message}", ex.Message);
            return 1;
        }
    }

    public static async Task<int> RunWithConsoleCancellationAsync(string[] args, TextWriter error)
    {
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            return await RunAsync(args, error, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private static MockHeartRateSource NewMockSource(
        ProbeOptions options,
        ILoggerFactory loggerFactory) => new(
            interval: TimeSpan.FromMilliseconds(options.IntervalMs),
            logger: loggerFactory.CreateLogger<MockHeartRateSource>());

#if !WINDOWS_BLE
    private static int BleUnavailable(TextWriter error)
    {
        error.WriteLine(
            "BLE commands require Windows 11 with the Windows build or Linux with BlueZ on the system bus. "
            + "On this platform only the 'mock' command is available.");
        return 1;
    }
#endif

    internal static async Task<int> RunSourceAsync(
        IHeartRateSource source,
        ProbeOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var _ = source;
        using var oscPublisher = options.OscEnabled
            ? new HeartRateOscPublisher(options.OscHost, options.OscPort, options.OscAddress)
            : null;
        using var sampleLimitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        bool firstSample = true;
        int samplesSeen = 0;
        source.SampleReceived += (_, sample) =>
        {
            samplesSeen++;
            if (firstSample)
            {
                firstSample = false;
                logger.LogInformation(
                    "SUCCESS: first valid Heart Rate Measurement parsed from {Description}", source.Description);
            }

            logger.LogInformation(
                "BPM={Bpm} contact={Contact}{Energy} rr=[{Rr}]",
                sample.Bpm,
                sample.SensorContact,
                sample.EnergyExpendedKilojoules is int kj ? $" energy={kj}kJ" : string.Empty,
                string.Join(", ", sample.RrIntervalsMs.Select(ms => ms.ToString("0.0"))));
            if (options.SampleCount is int sampleCount && samplesSeen >= sampleCount)
            {
                sampleLimitCts.Cancel();
            }
        };
        source.StateChanged += (_, state) => logger.LogInformation("Source state: {State}", state);

        if (options.OscEnabled)
        {
            oscPublisher!.Attach(source);
        }

        logger.LogInformation("Starting source: {Description}", source.Description);
        await source.StartAsync(sampleLimitCts.Token).ConfigureAwait(false);
        logger.LogInformation(
            options.SampleCount is int sampleCount
                ? "Running until {SampleCount} sample(s) are received..."
                : "Running until Ctrl+C...",
            options.SampleCount);

        try
        {
            await Task.Delay(Timeout.Infinite, sampleLimitCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await source.StopAsync().ConfigureAwait(false);
        return 0;
    }

#if WINDOWS_BLE
    private static async Task<int> RunScanAsync(
        ProbeOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger<BleAdvertisementScanner>();
        using var scanner = new BleAdvertisementScanner(
            logger,
            filterHeartRateService: !options.ScanAll,
            logRepeats: options.Verbose);

        int count = 0;
        var heartRateAdvertisers = new HashSet<ulong>();
        scanner.AdvertisementReceived += (_, report) =>
        {
            count++;
            if (report.ServiceUuids.Contains(GattUuids.HeartRateService))
            {
                heartRateAdvertisers.Add(report.Address);
            }
        };

        scanner.Start();
        logger.LogInformation("Scanning for {Timeout} s (Ctrl+C to stop early)...", options.TimeoutSec);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(options.TimeoutSec), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        scanner.Stop();
        logger.LogInformation(
            "Scan finished: {Count} advertisement(s) logged, {HrCount} device(s) advertising Heart Rate Service 0x180D",
            count,
            heartRateAdvertisers.Count);

        if (count == 0)
        {
            logger.LogWarning(
                "No advertisements at all - check the Bluetooth radio and Windows Settings > Privacy & security > "
                + "Bluetooth. If scan --all shows devices but scan --service 180D shows none, the tracker is not "
                + "advertising the Heart Rate Service (is 'HR on equipment' open and not connected elsewhere?).");
        }

        return 0;
    }

    private static async Task<int> RunConnectAsync(
        ProbeOptions options,
        ILoggerFactory loggerFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("=== Heart-rate sharing checklist (cannot be automated) ===");
        logger.LogInformation("1. On the tracker, open the 'HR on equipment' tile and keep the screen awake.");
        logger.LogInformation("2. When the tracker asks to share heart rate, tap Share, then tap Start.");
        logger.LogInformation("3. The tracker connects to ONE equipment/app at a time - disconnect others first.");
        logger.LogInformation("===========================================================");

        var source = new BleHeartRateSource(
            loggerFactory.CreateLogger<BleHeartRateSource>(),
            options.NameFilter,
            TimeSpan.FromSeconds(options.TimeoutSec));

        return await RunSourceAsync(source, options, logger, cancellationToken).ConfigureAwait(false);
    }
#endif

#if !WINDOWS_BLE
    private static async Task<int> RunLinuxScanAsync(
        ProbeOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger<BluezDbusHeartRateClient>();
        await using var client = new BluezDbusHeartRateClient();
        using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scanCts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSec));

        int count = 0;
        var heartRateAdvertisers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await client.StartDiscoveryAsync(
                options.ScanAll ? [] : [BluezUuids.HeartRateService],
                "le",
                scanCts.Token).ConfigureAwait(false);
            logger.LogInformation("Scanning with BlueZ for {Timeout} s (Ctrl+C to stop early)...", options.TimeoutSec);
            await foreach (var report in client.WatchAdvertisementsAsync(scanCts.Token).ConfigureAwait(false))
            {
                count++;
                if (report.ServiceUuids.Any(uuid =>
                        string.Equals(uuid, BluezUuids.HeartRateService, StringComparison.OrdinalIgnoreCase)))
                {
                    heartRateAdvertisers.Add(report.Address);
                }

                logger.LogInformation(
                    "Advertisement: path={Path} address={Address} addressType={AddressType} name={Name} "
                    + "services=[{Services}] rssi={Rssi} dBm",
                    report.ObjectPath,
                    report.Address,
                    report.AddressType,
                    string.IsNullOrEmpty(report.Name) ? "<none>" : report.Name,
                    report.ServiceUuids.Count == 0 ? "<none>" : string.Join(", ", report.ServiceUuids),
                    report.RssiDbm);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await client.StopDiscoveryAsync().ConfigureAwait(false);
        }

        logger.LogInformation(
            "Scan finished: {Count} advertisement(s) logged, {HrCount} device(s) advertising Heart Rate Service 0x180D",
            count,
            heartRateAdvertisers.Count);
        return 0;
    }

    private static async Task<int> RunLinuxConnectAsync(
        ProbeOptions options,
        ILoggerFactory loggerFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("=== Heart-rate sharing checklist (cannot be automated) ===");
        logger.LogInformation("1. On the tracker, open the heart-rate sharing/equipment mode and keep it awake.");
        logger.LogInformation("2. Start sharing on the tracker before connecting from PulseRelay.");
        logger.LogInformation("3. The tracker connects to ONE equipment/app at a time - disconnect others first.");
        logger.LogInformation("===========================================================");

        var source = new BluezHeartRateSource(
            new BluezDbusHeartRateClient(),
            loggerFactory.CreateLogger<BluezHeartRateSource>(),
            options.NameFilter,
            TimeSpan.FromSeconds(options.TimeoutSec));

        return await RunSourceAsync(source, options, logger, cancellationToken).ConfigureAwait(false);
    }
#endif
}
