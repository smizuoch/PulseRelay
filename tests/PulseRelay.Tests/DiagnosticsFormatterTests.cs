using Microsoft.Extensions.Logging;
using PulseRelay.App;
using PulseRelay.App.Diagnostics;
using PulseRelay.App.Logging;
using Xunit;

namespace PulseRelay.Tests;

public class DiagnosticsFormatterTests
{
    [Theory]
    [InlineData("found AA:BB:CC:DD:EE:FF nearby")]
    [InlineData("found aa-bb-cc-dd-ee-ff nearby")]
    [InlineData("found Aa:bB:0c:1d:2E:3f nearby")]
    public void Redacts_mac_shaped_addresses(string text)
    {
        string redacted = DiagnosticsFormatter.RedactMacAddresses(text);

        Assert.Equal("found [mac-redacted] nearby", redacted);
    }

    [Fact]
    public void Redacts_multiple_occurrences()
    {
        string redacted = DiagnosticsFormatter.RedactMacAddresses(
            "from 11:22:33:44:55:66 to 77:88:99:aa:bb:cc.");

        Assert.Equal("from [mac-redacted] to [mac-redacted].", redacted);
    }

    [Theory]
    [InlineData("uuid 0000180d-0000-1000-8000-00805f9b34fb stays")]
    [InlineData("short 00:11 fragment stays")]
    [InlineData("hex run 00112233445566778899aabbccddeeff stays")]
    public void Leaves_non_mac_hex_alone(string text) =>
        Assert.Equal(text, DiagnosticsFormatter.RedactMacAddresses(text));

    [Fact]
    public void Report_contains_state_and_log_lines_with_redaction()
    {
        var snapshot = SupervisorSnapshot.Initial with
        {
            RunState = BridgeRunState.Running,
            Session = BridgeSnapshot.Initial with
            {
                Status = BridgeStatus.Streaming,
                SourceDescription = "BLE Charge 6",
                Bpm = 72,
                OscStatus = OscOutputStatus.On,
                OscSentCount = 41,
                OscErrorCount = 2,
            },
        };
        var entries = new List<LogEntry>
        {
            new(DateTimeOffset.UtcNow, LogLevel.Information, "Test", "peer AA:BB:CC:DD:EE:FF connected"),
        };

        string report;
        using (new CultureScope("ja"))
        {
            // Diagnostics are raw English regardless of the UI culture (log policy).
            report = DiagnosticsFormatter.BuildReport(snapshot, entries, DateTimeOffset.UtcNow);
        }

        Assert.Contains("RunState: Running", report);
        Assert.Contains("Status: Streaming", report);
        Assert.Contains("Source: BLE Charge 6", report);
        Assert.Contains("Bpm: 72", report);
        Assert.Contains("Osc: On sent=41 errors=2", report);
        Assert.Contains("peer [mac-redacted] connected", report);
        Assert.DoesNotContain("AA:BB:CC:DD:EE:FF", report);
    }
}
