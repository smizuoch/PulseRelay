using System.Text;
using System.Text.RegularExpressions;
using PulseRelay.App.Logging;

namespace PulseRelay.App.Diagnostics;

/// <summary>
/// Builds the copyable diagnostics report. The report is raw English with ASCII punctuation
/// (log policy: diagnostics are not localized) and always redacts MAC-shaped addresses —
/// BLE peripheral addresses are session-scoped RPAs and must never leave the machine.
/// </summary>
public static partial class DiagnosticsFormatter
{
    // Lookarounds keep longer hex runs (UUIDs) intact while catching aa:bb:cc:dd:ee:ff
    // and aa-bb-cc-dd-ee-ff in any case.
    [GeneratedRegex("(?<![0-9A-Fa-f])(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}(?![0-9A-Fa-f])")]
    private static partial Regex MacPattern();

    public static string RedactMacAddresses(string text) =>
        MacPattern().Replace(text, "[mac-redacted]");

    /// <summary>One log line, identical in the Diagnostics window and the copied report.</summary>
    public static string FormatLine(LogEntry entry) =>
        $"{entry.Timestamp:HH:mm:ss.fff} [{entry.Level}] {entry.Category}: {entry.Message}";

    public static string BuildReport(
        SupervisorSnapshot snapshot,
        IReadOnlyList<LogEntry> entries,
        DateTimeOffset nowUtc)
    {
        var session = snapshot.Session;
        var sb = new StringBuilder();
        sb.AppendLine("PulseRelay diagnostics");
        sb.AppendLine($"Captured: {nowUtc:O}");
        sb.AppendLine($"RunState: {snapshot.RunState} (retry attempt {snapshot.RetryAttempt})");
        sb.AppendLine($"Status: {session.Status}");
        sb.AppendLine($"Source: {session.SourceDescription ?? "(none)"}");
        sb.AppendLine($"Bpm: {session.Bpm?.ToString() ?? "-"}  LastSampleAt: {session.LastSampleAt?.ToString("O") ?? "-"}");
        sb.AppendLine($"Osc: {session.OscStatus} sent={session.OscSentCount} errors={session.OscErrorCount}");
        if (session.LastError is not null)
        {
            sb.AppendLine($"LastError: {session.LastError}");
        }

        sb.AppendLine("--- log ---");
        foreach (var entry in entries)
        {
            sb.AppendLine(FormatLine(entry));
        }

        return RedactMacAddresses(sb.ToString());
    }
}
