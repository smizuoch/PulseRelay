using PulseRelay.Probe;
using Xunit;

namespace PulseRelay.Tests;

public class ProbeOptionsTests
{
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
