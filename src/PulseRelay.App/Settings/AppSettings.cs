using PulseRelay.Osc;

namespace PulseRelay.App.Settings;

public enum HeartRateSourceKind
{
    /// <summary>A Bluetooth LE device exposing the standard Heart Rate Service.</summary>
    Ble,

    /// <summary>The built-in simulated source (sine wave); works on every platform.</summary>
    Mock,
}

public enum AppTheme
{
    Dark,
    Light,
}

public enum AppLanguage
{
    /// <summary>Follow the operating system's UI language.</summary>
    System,
    English,
    Japanese,
}

/// <summary>
/// Persisted user settings. Deliberately contains no BLE address: peripheral addresses are
/// resolvable private addresses (RPA) and must never be stored as stable identities — only
/// the user-typed device name filter persists.
/// </summary>
public sealed class AppSettings
{
    public HeartRateSourceKind SourceKind { get; set; } = HeartRateSourceKind.Ble;

    /// <summary>Optional case-insensitive substring match on the advertised device name.</summary>
    public string? DeviceNameFilter { get; set; }

    public int ScanTimeoutSeconds { get; set; } = 30;

    public bool OscEnabled { get; set; } = true;

    public string OscHost { get; set; } = HeartRateOscPublisher.DefaultHost;

    public int OscPort { get; set; } = HeartRateOscPublisher.DefaultPort;

    public string OscAddress { get; set; } = HeartRateOscPublisher.DefaultAddress;

    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public AppLanguage Language { get; set; } = AppLanguage.System;

    public bool AutoConnectOnLaunch { get; set; }

    public bool HideToTrayOnClose { get; set; } = true;

    /// <summary>False until the first-run wizard finishes (or is skipped).</summary>
    public bool FirstRunCompleted { get; set; }
}
