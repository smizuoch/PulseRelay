using PulseRelay.App;
using Xunit;

namespace PulseRelay.Tests;

public class DeviceLineTests
{
    [Fact]
    public void No_source_yields_no_device_copy()
    {
        using var culture = new CultureScope("en");
        Assert.Equal("No device", BridgeStatusCopy.DeviceLine(null, deviceNameFilter: "Charge 6"));
    }

    [Theory]
    [InlineData("BLE <unknown>")]
    [InlineData("BLE <unnamed>")]
    public void Placeholder_description_falls_back_to_filter(string description)
    {
        Assert.Equal("Charge 6", BridgeStatusCopy.DeviceLine(description, "Charge 6"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Placeholder_without_filter_falls_back_to_generic_label(string? filter)
    {
        using var culture = new CultureScope("en");
        Assert.Equal("Bluetooth LE device", BridgeStatusCopy.DeviceLine("BLE <unknown>", filter));
    }

    [Fact]
    public void Generic_label_is_localized()
    {
        using var culture = new CultureScope("ja");
        Assert.Equal("Bluetooth LE デバイス", BridgeStatusCopy.DeviceLine("BLE <unknown>", null));
    }

    [Fact]
    public void Real_device_name_passes_through()
    {
        Assert.Equal("BLE Charge 6", BridgeStatusCopy.DeviceLine("BLE Charge 6", "Charge 6"));
    }

    [Theory]
    [InlineData("BLE <unknown>", null)]
    [InlineData("BLE <unknown>", "Charge 6")]
    [InlineData("BLE <unnamed>", null)]
    [InlineData("BLE Charge 6", null)]
    [InlineData(null, null)]
    public void Never_shows_a_placeholder(string? description, string? filter)
    {
        string line = BridgeStatusCopy.DeviceLine(description, filter);
        Assert.DoesNotContain("<unknown>", line);
        Assert.DoesNotContain("<unnamed>", line);
    }
}
