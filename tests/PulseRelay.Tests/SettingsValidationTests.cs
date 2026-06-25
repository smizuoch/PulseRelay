using PulseRelay.App.Settings;
using Xunit;

namespace PulseRelay.Tests;

public class SettingsValidationTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("9000", 9000)]
    [InlineData("65535", 65535)]
    public void Accepts_valid_ports(string text, int expected)
    {
        Assert.True(SettingsValidation.TryParsePort(text, out int port));
        Assert.Equal(expected, port);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("+80")]
    [InlineData(" 80")]
    [InlineData("8 0")]
    [InlineData("8,000")]
    [InlineData("９０００")]
    public void Rejects_invalid_ports(string? text) =>
        Assert.False(SettingsValidation.TryParsePort(text, out _));

    [Theory]
    [InlineData("/")]
    [InlineData("/x")]
    [InlineData("/avatar/parameters/VRCOSC/Heartrate/Value")]
    public void Accepts_valid_osc_addresses(string address) =>
        Assert.True(SettingsValidation.IsValidOscAddress(address));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("x")]
    [InlineData("avatar/parameters")]
    public void Rejects_invalid_osc_addresses(string? address) =>
        Assert.False(SettingsValidation.IsValidOscAddress(address));

    [Fact]
    public void Rejects_non_ascii_osc_address() =>
        Assert.False(SettingsValidation.IsValidOscAddress("/心拍"));

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("localhost", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void Validates_host(string? host, bool expected) =>
        Assert.Equal(expected, SettingsValidation.IsValidHost(host));

    [Fact]
    public void Normalize_repairs_invalid_values_and_trims_valid_strings()
    {
        var settings = new AppSettings
        {
            SourceKind = (HeartRateSourceKind)999,
            ScanTimeoutSeconds = 0,
            OscHost = "  192.168.0.10  ",
            OscPort = 70000,
            OscAddress = " /avatar/parameters/HeartRate ",
            Theme = (AppTheme)999,
            Language = (AppLanguage)999,
        };

        var normalized = SettingsValidation.Normalize(settings);

        Assert.Same(settings, normalized);
        Assert.Equal(HeartRateSourceKind.Ble, normalized.SourceKind);
        Assert.Equal(30, normalized.ScanTimeoutSeconds);
        Assert.Equal("192.168.0.10", normalized.OscHost);
        Assert.Equal(9000, normalized.OscPort);
        Assert.Equal("/avatar/parameters/HeartRate", normalized.OscAddress);
        Assert.Equal(AppTheme.Dark, normalized.Theme);
        Assert.Equal(AppLanguage.System, normalized.Language);
    }

    [Fact]
    public void Normalize_replaces_invalid_host_and_address()
    {
        var settings = new AppSettings
        {
            ScanTimeoutSeconds = 3601,
            OscHost = "   ",
            OscAddress = "not/an/address",
        };

        var normalized = SettingsValidation.Normalize(settings);

        Assert.Equal(30, normalized.ScanTimeoutSeconds);
        Assert.Equal("127.0.0.1", normalized.OscHost);
        Assert.Equal("/avatar/parameters/VRCOSC/Heartrate/Value", normalized.OscAddress);
    }
}
