using PulseRelay.Osc;

namespace PulseRelay.App;

/// <summary>
/// Fires a single OSC message outside any bridge session, for the "Send test value" button
/// in the wizard and settings.
/// </summary>
public static class OscTestSender
{
    public const int DefaultTestBpm = 100;

    public static bool TrySend(string host, int port, string address, int value, out string? error)
    {
        try
        {
            using var sender = new OscUdpSender(host, port);
            sender.Send(OscWriter.WriteMessage(address, value));
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
