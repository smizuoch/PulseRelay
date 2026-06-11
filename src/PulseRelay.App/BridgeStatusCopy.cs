namespace PulseRelay.App;

/// <summary>
/// User-facing one-line status copy. Lives here (not in the UI project) so the dashboard,
/// wizard, and tray tooltip share identical wording and it stays unit-testable.
/// Plain language only — GATT/CCCD/RPA terms belong in Diagnostics.
/// </summary>
public static class BridgeStatusCopy
{
    /// <summary>Headline for the supervised bridge, including reconnect states.</summary>
    public static string Headline(SupervisorSnapshot snapshot, DateTimeOffset nowUtc, TimeSpan staleThreshold)
    {
        if (snapshot.EffectiveStatus(nowUtc, staleThreshold) == BridgeStatus.Reconnecting)
        {
            return snapshot.HasStreamedThisRun
                ? "Device disconnected — trying again…"
                : "Couldn't find your device — trying again…";
        }

        return Headline(snapshot.Session, nowUtc, staleThreshold);
    }

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
            BridgeStatus.Reconnecting => "Device disconnected — trying again…",
            BridgeStatus.Failed => FailedHeadline(snapshot),
            _ => status.ToString(),
        };
    }

    private static string FailedHeadline(BridgeSnapshot snapshot) => snapshot.FailureKind switch
    {
        BridgeFailureKind.DeviceNotFound =>
            "Couldn't find a heart-rate device. Check that it's sharing its heart rate and is near this computer.",
        BridgeFailureKind.BluetoothUnavailable =>
            "Bluetooth isn't available. Turn it on in system settings, then try again.",
        BridgeFailureKind.PlatformUnsupported =>
            "Bluetooth LE devices aren't supported on this platform yet.",
        BridgeFailureKind.OscConfig =>
            "OSC output couldn't start — check the host, port, and address in settings.",
        _ => snapshot.LastError ?? "Connection failed",
    };

    private static string StaleHeadline(BridgeSnapshot snapshot, DateTimeOffset nowUtc)
    {
        int seconds = snapshot.LastSampleAt is { } last
            ? (int)(nowUtc - last).TotalSeconds
            : 0;
        return $"No data for {seconds}s — is the device still sharing?";
    }
}
