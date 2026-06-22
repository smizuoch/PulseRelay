using PulseRelay.App.Settings;
using PulseRelay.Core.Sources;
using PulseRelay.Osc;
using PulseRelay.Probe;
using Xunit;

namespace PulseRelay.Tests;

/// <summary>
/// Regression tests for bugs found during the cross-layer audit: OSC address
/// validation, settings/CLI validation parity, command-scoped probe options,
/// empty OSC host rejection, and mock-source timer interval validation.
/// </summary>
public class BugRegressionTests
{
    // ---------------------------------------------------------------------------------
    // Regression 1 — OscWriter must not emit illegal OSC addresses verbatim onto the wire.
    // OSC 1.0 addresses are printable-ASCII strings; a space, '#', or any control
    // character is not a legal address character.
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("/avatar param")]   // space
    [InlineData("/avatar#1")]       // '#'
    [InlineData("/avatar\tx")]      // TAB control char
    [InlineData("/avatar\nx")]      // newline control char
    public void OscAddress_OscWriter_should_reject_illegal_address_characters(string address)
    {
        Assert.Throws<ArgumentException>(() => OscWriter.WriteMessage(address, 90));
    }

    // ---------------------------------------------------------------------------------
    // Regression 2 — The shared validator must reject the same illegal addresses so the
    // settings dialog and probe CLI cannot persist malformed OSC packet destinations.
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("/avatar param")]
    [InlineData("/avatar#1")]
    [InlineData("/avatar\tx")]
    public void SettingsValidation_SettingsValidation_should_reject_illegal_address_characters(string address)
    {
        Assert.False(SettingsValidation.IsValidOscAddress(address));
    }

    // ---------------------------------------------------------------------------------
    // Regression 3 — Options must be scoped to their command. The Usage text documents
    // --service/--all as 'scan' options and --name as a 'connect' option, but the parser
    // previously accepted them on the wrong command and silently ignored them.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ProbeOptions_mock_should_reject_scan_only_service_option()
    {
        bool ok = ProbeOptions.TryParse(["mock", "--service", "180D"], out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void ProbeOptions_connect_should_reject_scan_only_all_option()
    {
        bool ok = ProbeOptions.TryParse(["connect", "--all"], out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void ProbeOptions_connect_should_reject_scan_only_service_option()
    {
        bool ok = ProbeOptions.TryParse(["connect", "--service", "180D"], out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void ProbeOptions_scan_should_reject_connect_only_name_option()
    {
        bool ok = ProbeOptions.TryParse(["scan", "--service", "180D", "--name", "Charge 6"], out _, out _);
        Assert.False(ok);
    }

    // ---------------------------------------------------------------------------------
    // Regression 4 — Empty --osc-host must be rejected at parse time instead of being
    // deferred to runtime where OscUdpSender throws ArgumentException.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ProbeOptions_probe_should_reject_empty_osc_host_at_parse_time()
    {
        bool ok = ProbeOptions.TryParse(
            ["mock", "--osc", "--osc-host", "", "--osc-port", "9000"], out _, out _);
        Assert.False(ok);
    }

    // ---------------------------------------------------------------------------------
    // Regression 5 — MockHeartRateSource validates the BPM band and must also validate
    // the timer interval. A zero/negative interval is invalid for PeriodicTimer and must
    // fail before StartAsync can report Subscribed.
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MockHeartRateSource_mock_source_should_reject_non_positive_interval_at_construction(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MockHeartRateSource(interval: TimeSpan.FromMilliseconds(milliseconds)));
    }
}
