using PulseRelay.App;
using PulseRelay.Osc;
using Xunit;

namespace PulseRelay.Tests;

public class OscTestSenderTests
{
    [Fact]
    public void Sends_default_test_value_to_loopback()
    {
        bool ok = OscTestSender.TrySend(
            "127.0.0.1",
            9000,
            HeartRateOscPublisher.DefaultAddress,
            OscTestSender.DefaultTestBpm,
            out string? error);

        Assert.True(ok);
        Assert.Null(error);
    }

    [Fact]
    public void Invalid_address_returns_error()
    {
        bool ok = OscTestSender.TrySend(
            "127.0.0.1",
            9000,
            "not-an-address",
            100,
            out string? error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}
