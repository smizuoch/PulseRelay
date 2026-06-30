using PulseRelay.LinuxBle;
using Xunit;

namespace PulseRelay.Tests;

public sealed class BluezNoInputNoOutputAgentTests
{
    [Theory]
    [InlineData("Release")]
    [InlineData("Cancel")]
    [InlineData("RequestAuthorization")]
    [InlineData("AuthorizeService")]
    public void Classify_accepts_no_input_no_output_compatible_methods(string member)
    {
        var reply = BluezNoInputNoOutputAgent.Classify(BluezDbus.AgentInterface, member);

        Assert.Equal(BluezAgentReply.Empty, reply);
    }

    [Theory]
    [InlineData("RequestPinCode")]
    [InlineData("RequestPasskey")]
    [InlineData("DisplayPinCode")]
    [InlineData("DisplayPasskey")]
    [InlineData("RequestConfirmation")]
    public void Classify_rejects_pin_and_passkey_methods(string member)
    {
        var reply = BluezNoInputNoOutputAgent.Classify(BluezDbus.AgentInterface, member);

        Assert.Equal(BluezAgentReply.RejectPinOrPasskey, reply);
    }

    [Fact]
    public void Classify_rejects_unknown_interface_or_method()
    {
        Assert.Equal(
            BluezAgentReply.RejectUnsupported,
            BluezNoInputNoOutputAgent.Classify("org.example.Other", "Release"));
        Assert.Equal(
            BluezAgentReply.RejectUnsupported,
            BluezNoInputNoOutputAgent.Classify(BluezDbus.AgentInterface, "SomethingElse"));
    }
}
