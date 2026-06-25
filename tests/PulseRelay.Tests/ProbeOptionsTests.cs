using PulseRelay.Probe;
using Xunit;

namespace PulseRelay.Tests;

public class ProbeOptionsTests
{
    [Fact]
    public void Parses_mock_defaults()
    {
        Assert.True(ProbeOptions.TryParse(["mock"], out var options, out string error));

        Assert.Empty(error);
        Assert.Equal(ProbeCommand.Mock, options.Command);
        Assert.False(options.OscEnabled);
        Assert.Equal("127.0.0.1", options.OscHost);
        Assert.Equal(9000, options.OscPort);
        Assert.Equal("/avatar/parameters/VRCOSC/Heartrate/Value", options.OscAddress);
        Assert.Equal(1000, options.IntervalMs);
        Assert.Equal(30, options.TimeoutSec);
    }

    [Fact]
    public void Parses_connect_options()
    {
        Assert.True(ProbeOptions.TryParse(
            [
                "connect",
                "--name",
                "Charge",
                "--osc",
                "--osc-host",
                "192.168.0.10",
                "--osc-port",
                "9100",
                "--osc-address",
                "/avatar/parameters/HeartRate",
                "--timeout-sec",
                "45",
                "--verbose",
            ],
            out var options,
            out string error));

        Assert.Empty(error);
        Assert.Equal(ProbeCommand.Connect, options.Command);
        Assert.Equal("Charge", options.NameFilter);
        Assert.True(options.OscEnabled);
        Assert.Equal("192.168.0.10", options.OscHost);
        Assert.Equal(9100, options.OscPort);
        Assert.Equal("/avatar/parameters/HeartRate", options.OscAddress);
        Assert.Equal(45, options.TimeoutSec);
        Assert.True(options.Verbose);
    }

    [Fact]
    public void Parses_connect_without_name_filter()
    {
        Assert.True(ProbeOptions.TryParse(["connect"], out var options, out string error));

        Assert.Empty(error);
        Assert.Equal(ProbeCommand.Connect, options.Command);
        Assert.Null(options.NameFilter);
    }

    [Fact]
    public void Connect_name_filter_is_trimmed()
    {
        Assert.True(ProbeOptions.TryParse(
            ["connect", "--name", "  Charge 6  "],
            out var options,
            out string error));

        Assert.Empty(error);
        Assert.Equal("Charge 6", options.NameFilter);
    }

    [Theory]
    [InlineData("180D")]
    [InlineData("0x180D")]
    [InlineData("0x180d")]
    public void Parses_scan_service_filter(string service)
    {
        Assert.True(ProbeOptions.TryParse(["scan", "--service", service], out var options, out string error));

        Assert.Empty(error);
        Assert.Equal(ProbeCommand.Scan, options.Command);
        Assert.False(options.ScanAll);
    }

    [Fact]
    public void Parses_scan_all()
    {
        Assert.True(ProbeOptions.TryParse(["scan", "--all"], out var options, out string error));

        Assert.Empty(error);
        Assert.Equal(ProbeCommand.Scan, options.Command);
        Assert.True(options.ScanAll);
    }

    [Fact]
    public void Rejects_scan_all_with_service_filter()
    {
        Assert.False(ProbeOptions.TryParse(
            ["scan", "--all", "--service", "180D"],
            out _,
            out string error));

        Assert.Contains("--all", error);
        Assert.Contains("--service", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_empty_name_filter(string name)
    {
        Assert.False(ProbeOptions.TryParse(
            ["connect", "--name", name],
            out _,
            out string error));

        Assert.Contains("--name", error);
    }

    [Fact]
    public void Rejects_option_when_next_token_is_another_option()
    {
        Assert.False(ProbeOptions.TryParse(
            ["mock", "--osc-host", "--osc"],
            out _,
            out string error));

        Assert.Contains("--osc-host", error);
    }

    [Fact]
    public void Rejects_missing_command()
    {
        Assert.False(ProbeOptions.TryParse([], out _, out string error));

        Assert.Contains("No command", error);
    }

    [Fact]
    public void Rejects_unknown_command()
    {
        Assert.False(ProbeOptions.TryParse(["status"], out _, out string error));

        Assert.Contains("Unknown command", error);
    }

    [Fact]
    public void Rejects_unknown_option_for_known_command()
    {
        Assert.False(ProbeOptions.TryParse(["mock", "--bogus"], out _, out string error));

        Assert.Contains("Unknown option", error);
    }

    [Theory]
    [InlineData("connect", "--all")]
    [InlineData("mock", "--service", "180D")]
    [InlineData("scan", "--name", "Charge")]
    [InlineData("connect", "--interval-ms", "500")]
    public void Rejects_options_that_do_not_apply_to_command(params string[] args)
    {
        Assert.False(ProbeOptions.TryParse(args, out _, out string error));

        Assert.Contains("only valid", error);
    }

    [Fact]
    public void Rejects_scan_without_filter_mode()
    {
        Assert.False(ProbeOptions.TryParse(["scan"], out _, out string error));

        Assert.Contains("requires either", error);
    }

    [Fact]
    public void Rejects_unsupported_scan_service()
    {
        Assert.False(ProbeOptions.TryParse(["scan", "--service", "180F"], out _, out string error));

        Assert.Contains("Heart Rate Service", error);
    }

    [Theory]
    [InlineData("--osc-port")]
    [InlineData("--timeout-sec")]
    [InlineData("--interval-ms")]
    public void Rejects_missing_integer_value(string option)
    {
        Assert.False(ProbeOptions.TryParse(["mock", option], out _, out string error));

        Assert.Contains(option, error);
    }

    [Theory]
    [InlineData("scan", "--service")]
    [InlineData("connect", "--name")]
    [InlineData("mock", "--osc-address")]
    public void Rejects_missing_string_value(params string[] args)
    {
        Assert.False(ProbeOptions.TryParse(args, out _, out string error));

        Assert.Contains(args[^1], error);
    }

    [Theory]
    [InlineData("--osc-port", "0")]
    [InlineData("--timeout-sec", "-1")]
    [InlineData("--interval-ms", "abc")]
    public void Rejects_non_positive_or_non_numeric_integer_values(string option, string value)
    {
        Assert.False(ProbeOptions.TryParse(["mock", option, value], out _, out string error));

        Assert.Contains("positive integer", error);
    }

    [Fact]
    public void Accepts_max_udp_port()
    {
        Assert.True(ProbeOptions.TryParse(
            ["mock", "--osc-port", "65535"],
            out var options,
            out string error));

        Assert.Empty(error);
        Assert.Equal(65535, options.OscPort);
    }

    [Fact]
    public void Rejects_empty_osc_host()
    {
        Assert.False(ProbeOptions.TryParse(
            ["mock", "--osc-host", "   "],
            out _,
            out string error));

        Assert.Contains("--osc-host", error);
    }

    [Fact]
    public void Rejects_udp_port_above_65535()
    {
        Assert.False(ProbeOptions.TryParse(
            ["mock", "--osc", "--osc-port", "70000"],
            out _,
            out string error));
        Assert.Contains("65535", error);
    }

    [Fact]
    public void Rejects_non_ascii_osc_address()
    {
        Assert.False(ProbeOptions.TryParse(
            ["mock", "--osc", "--osc-address", "/心拍"],
            out _,
            out string error));
        Assert.Contains("ASCII", error);
    }
}
