using Microsoft.Extensions.Logging;

namespace PulseRelay.App.Logging;

/// <summary>One captured log line for the Diagnostics view. Exception text is folded into Message.</summary>
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message);
