using Microsoft.Extensions.Logging;
using PulseRelay.Core.HeartRate;
using PulseRelay.Core.Sources;
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
                ProbeCommand.Scan or ProbeCommand.Connect => BleUnavailable(error),
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
            "BLE commands require the Windows build (net10.0-windows10.0.19041.0) running on Windows 11. "
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

        bool firstSample = true;
        source.SampleReceived += (_, sample) =>
        {
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
        };
        source.StateChanged += (_, state) => logger.LogInformation("Source state: {State}", state);

        if (options.OscEnabled)
        {
            oscPublisher!.Attach(source);
        }

        logger.LogInformation("Starting source: {Description}", source.Description);
        await source.StartAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Running until Ctrl+C...");

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
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
}
