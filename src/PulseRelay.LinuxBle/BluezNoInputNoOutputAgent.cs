using Tmds.DBus.Protocol;

namespace PulseRelay.LinuxBle;

internal sealed class BluezNoInputNoOutputAgent : IMethodHandler
{
    public BluezNoInputNoOutputAgent(string path) => Path = path;

    public string Path { get; }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        switch (Classify(request.InterfaceAsString, request.MemberAsString))
        {
            case BluezAgentReply.Empty:
                ReplyEmpty(context);
                break;
            case BluezAgentReply.RejectPinOrPasskey:
                context.ReplyError(
                    "org.bluez.Error.Rejected",
                    "PulseRelay registers a NoInputNoOutput pairing agent and cannot confirm or provide PIN/passkey values.");
                break;
            case BluezAgentReply.RejectUnsupported:
                context.ReplyError("org.bluez.Error.Rejected", $"Unsupported agent method: {request.MemberAsString}.");
                break;
        }

        return ValueTask.CompletedTask;
    }

    public bool RunMethodHandlerSynchronously(Message message) => true;

    internal static BluezAgentReply Classify(string? interfaceName, string? memberName)
    {
        if (interfaceName != BluezDbus.AgentInterface)
        {
            return BluezAgentReply.RejectUnsupported;
        }

        return memberName switch
        {
            "Release" or
            "Cancel" or
            "RequestAuthorization" or
            "AuthorizeService" => BluezAgentReply.Empty,
            "RequestPinCode" or
            "RequestPasskey" or
            "DisplayPinCode" or
            "DisplayPasskey" or
            "RequestConfirmation" => BluezAgentReply.RejectPinOrPasskey,
            _ => BluezAgentReply.RejectUnsupported,
        };
    }

    private static void ReplyEmpty(MethodContext context)
    {
        var writer = context.CreateReplyWriter(string.Empty);
        context.Reply(writer.CreateMessage());
    }
}

internal enum BluezAgentReply
{
    Empty,
    RejectPinOrPasskey,
    RejectUnsupported,
}
