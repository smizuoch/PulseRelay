using PulseRelay.App.Localization;

namespace PulseRelay.App;

/// <summary>
/// User-facing one-line status copy, backed by the localization resources. Lives here (not
/// in the UI project) so the dashboard, future views, and tray tooltip share identical
/// wording and it stays unit-testable. Plain language only — GATT/CCCD/RPA terms belong in
/// Diagnostics, and log text never comes from here.
/// </summary>
public static class BridgeStatusCopy
{
    /// <summary>Headline for the supervised bridge, including reconnect states.</summary>
    public static string Headline(SupervisorSnapshot snapshot, DateTimeOffset nowUtc, TimeSpan staleThreshold)
    {
        if (snapshot.EffectiveStatus(nowUtc, staleThreshold) == BridgeStatus.Reconnecting)
        {
            return LocalizationManager.GetString(
                snapshot.HasStreamedThisRun ? "Status_Reconnecting" : "Status_ReconnectingInitial");
        }

        return Headline(snapshot.Session, nowUtc, staleThreshold);
    }

    public static string Headline(BridgeSnapshot snapshot, DateTimeOffset nowUtc, TimeSpan staleThreshold)
    {
        var status = snapshot.EffectiveStatus(nowUtc, staleThreshold);
        return status switch
        {
            BridgeStatus.NotConnected => LocalizationManager.GetString("Status_NotConnected"),
            BridgeStatus.Searching => LocalizationManager.GetString("Status_Searching"),
            BridgeStatus.Connecting => LocalizationManager.GetString("Status_Connecting"),
            BridgeStatus.WaitingForData => LocalizationManager.GetString("Status_WaitingForData"),
            BridgeStatus.Streaming => LocalizationManager.GetString("Status_Streaming"),
            BridgeStatus.Stale => StaleHeadline(snapshot, nowUtc),
            BridgeStatus.Disconnected => LocalizationManager.GetString("Status_Disconnected"),
            BridgeStatus.Reconnecting => LocalizationManager.GetString("Status_Reconnecting"),
            BridgeStatus.Failed => FailedHeadline(snapshot),
            _ => status.ToString(),
        };
    }

    /// <summary>Short state label for the device card.</summary>
    public static string DeviceState(BridgeStatus status) => status switch
    {
        BridgeStatus.NotConnected => LocalizationManager.GetString("Device_State_NotConnected"),
        BridgeStatus.Searching => LocalizationManager.GetString("Device_State_Searching"),
        BridgeStatus.Connecting => LocalizationManager.GetString("Device_State_Connecting"),
        BridgeStatus.WaitingForData => LocalizationManager.GetString("Device_State_WaitingForData"),
        BridgeStatus.Streaming => LocalizationManager.GetString("Device_State_Streaming"),
        BridgeStatus.Stale => LocalizationManager.GetString("Device_State_Stale"),
        BridgeStatus.Disconnected => LocalizationManager.GetString("Device_State_Disconnected"),
        BridgeStatus.Reconnecting => LocalizationManager.GetString("Device_State_Reconnecting"),
        BridgeStatus.Failed => LocalizationManager.GetString("Device_State_Failed"),
        _ => status.ToString(),
    };

    /// <summary>
    /// Device line for the device card. A BLE source reports "BLE &lt;unknown&gt;" before the
    /// scan (and "BLE &lt;unnamed&gt;" when the peripheral advertises no name); those
    /// placeholders are never shown — the user's device name filter or a generic label
    /// substitutes. The literals mirror BleHeartRateSource, which cross-platform code
    /// cannot reference (Windows-only project).
    /// </summary>
    public static string DeviceLine(string? sourceDescription, string? deviceNameFilter)
    {
        if (sourceDescription is null)
        {
            return LocalizationManager.GetString("Device_NoDevice");
        }

        if (sourceDescription.Contains("<unknown>", StringComparison.Ordinal)
            || sourceDescription.Contains("<unnamed>", StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(deviceNameFilter)
                ? LocalizationManager.GetString("Device_BleFallback")
                : deviceNameFilter.Trim();
        }

        return sourceDescription;
    }

    private static string FailedHeadline(BridgeSnapshot snapshot) => snapshot.FailureKind switch
    {
        BridgeFailureKind.DeviceNotFound => LocalizationManager.GetString("Error_DeviceNotFound"),
        BridgeFailureKind.BluetoothUnavailable => LocalizationManager.GetString("Error_BluetoothUnavailable"),
        BridgeFailureKind.PlatformUnsupported => LocalizationManager.GetString("Error_PlatformUnsupported"),
        BridgeFailureKind.OscConfig => LocalizationManager.GetString("Error_OscConfig"),
        BridgeFailureKind.ConnectionTimeout => LocalizationManager.GetString("Error_ConnectionTimeout"),
        _ => snapshot.LastError ?? LocalizationManager.GetString("Status_Failed"),
    };

    private static string StaleHeadline(BridgeSnapshot snapshot, DateTimeOffset nowUtc)
    {
        int seconds = snapshot.LastSampleAt is { } last
            ? (int)(nowUtc - last).TotalSeconds
            : 0;
        return LocalizationManager.Format("Status_Stale", seconds);
    }
}
