namespace PulseRelay.App;

/// <summary>
/// User-facing one-line status copy. Lives here (not in the UI project) so the dashboard,
/// wizard, and tray tooltip share identical wording and it stays unit-testable.
/// Plain language only — GATT/CCCD/RPA terms belong in Diagnostics.
/// </summary>
public static class BridgeStatusCopy
{
    public static string Headline(BridgeSnapshot snapshot, DateTimeOffset nowUtc, TimeSpan staleThreshold)
    {
        var status = snapshot.EffectiveStatus(nowUtc, staleThreshold);
        return status switch
        {
            BridgeStatus.NotConnected => "Not connected",
            BridgeStatus.Searching => "Looking for your device…",
            BridgeStatus.Connecting => "Connecting…",
            BridgeStatus.WaitingForData => "Connected — waiting for the first reading…",
            BridgeStatus.Streaming => "Receiving heart rate",
            BridgeStatus.Stale => StaleHeadline(snapshot, nowUtc),
            BridgeStatus.Disconnected => "Device disconnected",
            BridgeStatus.Failed => snapshot.LastError ?? "Connection failed",
            _ => status.ToString(),
        };
    }

    private static string StaleHeadline(BridgeSnapshot snapshot, DateTimeOffset nowUtc)
    {
        int seconds = snapshot.LastSampleAt is { } last
            ? (int)(nowUtc - last).TotalSeconds
            : 0;
        return $"No data for {seconds}s — is the device still sharing?";
    }
}
