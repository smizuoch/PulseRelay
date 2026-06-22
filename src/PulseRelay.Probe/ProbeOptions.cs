using PulseRelay.Osc;

namespace PulseRelay.Probe;

public enum ProbeCommand
{
    Scan,
    Connect,
    Mock,
}

/// <summary>Parsed CLI options. Hand-rolled parsing — no CLI framework dependency for the probe.</summary>
public sealed class ProbeOptions
{
    public ProbeCommand Command { get; private init; }

    /// <summary>scan: true = unfiltered diagnostic scan; false = filter on Heart Rate Service.</summary>
    public bool ScanAll { get; private init; }

    /// <summary>connect: optional case-insensitive substring filter on the advertised local name.</summary>
    public string? NameFilter { get; private init; }

    public bool OscEnabled { get; private init; }

    public string OscHost { get; private init; } = HeartRateOscPublisher.DefaultHost;

    public int OscPort { get; private init; } = HeartRateOscPublisher.DefaultPort;

    public string OscAddress { get; private init; } = HeartRateOscPublisher.DefaultAddress;

    public bool Verbose { get; private init; }

    public int TimeoutSec { get; private init; } = 30;

    public int IntervalMs { get; private init; } = 1000;

    public static string Usage => """
        PulseRelay.Probe - heart-rate BLE probe and OSC bridge

        Commands:
          scan --service 180D        Scan for devices advertising the Heart Rate Service (Windows only)
          scan --all                 Diagnostic scan logging all nearby BLE advertisements (Windows only)
          connect [--name <substr>]  Scan, connect, and stream heart-rate notifications (Windows only)
          mock                       Stream synthetic heart-rate samples (any platform)

        Options:
          --name <substr>            connect: only match devices whose advertised name contains <substr>
          --osc                      Forward BPM to an OSC endpoint
          --osc-host <host>          OSC host (default 127.0.0.1)
          --osc-port <port>          OSC UDP port (default 9000)
          --osc-address <address>    OSC address (default /avatar/parameters/VRCOSC/Heartrate/Value)
          --timeout-sec <n>          Scan duration / device-search timeout (default 30)
          --interval-ms <n>          mock: sample interval (default 1000)
          --verbose                  Debug-level logging (raw payload hex, GATT details)

        Examples:
          PulseRelay.Probe scan --all --verbose
          PulseRelay.Probe scan --service 180D --verbose
          PulseRelay.Probe connect --name "Charge 6" --verbose
          PulseRelay.Probe connect --name "Charge 6" --osc
          PulseRelay.Probe mock --osc
        """;

    public static bool TryParse(string[] args, out ProbeOptions options, out string error)
    {
        options = new ProbeOptions();
        error = string.Empty;

        if (args.Length == 0)
        {
            error = "No command given.";
            return false;
        }

        ProbeCommand command;
        switch (args[0].ToLowerInvariant())
        {
            case "scan":
                command = ProbeCommand.Scan;
                break;
            case "connect":
                command = ProbeCommand.Connect;
                break;
            case "mock":
                command = ProbeCommand.Mock;
                break;
            default:
                error = $"Unknown command \"{args[0]}\".";
                return false;
        }

        bool scanAll = false;
        string? serviceFilter = null;
        string? nameFilter = null;
        bool osc = false;
        string oscHost = HeartRateOscPublisher.DefaultHost;
        int oscPort = HeartRateOscPublisher.DefaultPort;
        string oscAddress = HeartRateOscPublisher.DefaultAddress;
        bool verbose = false;
        int timeoutSec = 30;
        int intervalMs = 1000;
        bool sawAll = false;
        bool sawService = false;
        bool sawName = false;
        bool sawInterval = false;

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--all":
                    sawAll = true;
                    scanAll = true;
                    break;
                case "--service":
                    sawService = true;
                    if (!TryTakeValue(args, ref i, arg, ref error, out serviceFilter))
                    {
                        return false;
                    }

                    break;
                case "--name":
                    sawName = true;
                    if (!TryTakeValue(args, ref i, arg, ref error, out nameFilter))
                    {
                        return false;
                    }

                    break;
                case "--osc":
                    osc = true;
                    break;
                case "--osc-host":
                    if (!TryTakeValue(args, ref i, arg, ref error, out oscHost!))
                    {
                        return false;
                    }

                    break;
                case "--osc-port":
                    if (!TryTakeInt(args, ref i, arg, ref error, out oscPort))
                    {
                        return false;
                    }

                    break;
                case "--osc-address":
                    if (!TryTakeValue(args, ref i, arg, ref error, out oscAddress!))
                    {
                        return false;
                    }

                    break;
                case "--timeout-sec":
                    if (!TryTakeInt(args, ref i, arg, ref error, out timeoutSec))
                    {
                        return false;
                    }

                    break;
                case "--interval-ms":
                    sawInterval = true;
                    if (!TryTakeInt(args, ref i, arg, ref error, out intervalMs))
                    {
                        return false;
                    }

                    break;
                case "--verbose":
                    verbose = true;
                    break;
                default:
                    error = $"Unknown option \"{arg}\" for command \"{args[0]}\".";
                    return false;
            }
        }

        if (command != ProbeCommand.Scan && sawAll)
        {
            error = "Option --all is only valid for command \"scan\".";
            return false;
        }

        if (command != ProbeCommand.Scan && sawService)
        {
            error = "Option --service is only valid for command \"scan\".";
            return false;
        }

        if (command != ProbeCommand.Connect && sawName)
        {
            error = "Option --name is only valid for command \"connect\".";
            return false;
        }

        if (command != ProbeCommand.Mock && sawInterval)
        {
            error = "Option --interval-ms is only valid for command \"mock\".";
            return false;
        }

        if (command == ProbeCommand.Scan)
        {
            if (!scanAll && serviceFilter is null)
            {
                error = "scan requires either --service 180D or --all.";
                return false;
            }

            if (serviceFilter is not null
                && !serviceFilter.Equals("180D", StringComparison.OrdinalIgnoreCase)
                && !serviceFilter.Equals("0x180D", StringComparison.OrdinalIgnoreCase))
            {
                error = $"Only the Heart Rate Service filter (180D) is supported, got \"{serviceFilter}\".";
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(oscHost))
        {
            error = "Option --osc-host requires a non-empty host.";
            return false;
        }

        if (oscPort > ushort.MaxValue)
        {
            error = $"Option --osc-port must be between 1 and {ushort.MaxValue}, got \"{oscPort}\".";
            return false;
        }

        if (!OscWriterAddressIsValid(oscAddress))
        {
            error = $"Option --osc-address must be an ASCII OSC address beginning with '/', got \"{oscAddress}\".";
            return false;
        }

        options = new ProbeOptions
        {
            Command = command,
            ScanAll = scanAll,
            NameFilter = nameFilter,
            OscEnabled = osc,
            OscHost = oscHost.Trim(),
            OscPort = oscPort,
            OscAddress = oscAddress,
            Verbose = verbose,
            TimeoutSec = timeoutSec,
            IntervalMs = intervalMs,
        };
        return true;
    }

    private static bool OscWriterAddressIsValid(string address)
    {
        try
        {
            _ = OscWriter.WriteMessage(address, 0);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryTakeValue(string[] args, ref int i, string name, ref string error, out string? value)
    {
        if (i + 1 >= args.Length)
        {
            error = $"Option {name} requires a value.";
            value = null;
            return false;
        }

        value = args[++i];
        return true;
    }

    private static bool TryTakeInt(string[] args, ref int i, string name, ref string error, out int value)
    {
        value = 0;
        if (!TryTakeValue(args, ref i, name, ref error, out string? raw))
        {
            return false;
        }

        if (!int.TryParse(raw, out value) || value <= 0)
        {
            error = $"Option {name} requires a positive integer, got \"{raw}\".";
            return false;
        }

        return true;
    }
}
