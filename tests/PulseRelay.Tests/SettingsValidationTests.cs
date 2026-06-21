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
}
