using PulseRelay.Core.Sources;

namespace PulseRelay.App;

/// <summary>
/// UI-facing status of the bridge, derived from <see cref="HeartRateSourceState"/> plus
/// app-level facts (staleness). Deliberately coarser than the source state: Connecting
/// covers both Connecting and Subscribing because the distinction is diagnostics-only.
/// </summary>
public enum BridgeStatus
{
    NotConnected,

    /// <summary>Scanning for a device broadcasting heart rate.</summary>
    Searching,

    /// <summary>Connecting and setting up the heart-rate stream.</summary>
    Connecting,

    /// <summary>Connected; no measurement received yet.</summary>
    WaitingForData,

    /// <summary>Receiving heart-rate samples.</summary>
    Streaming,

    /// <summary>Still nominally streaming but no sample within the stale threshold.</summary>
    Stale,

    Disconnected,

    Failed,

    /// <summary>
    /// The bridge dropped (or never connected) while the user intends it to run; a retry is
    /// pending or in flight. Supervisor-level: never produced by <see cref="BridgeSession"/>.
    /// </summary>
    Reconnecting,
}

/// <summary>User intent plus retry activity, owned by <see cref="BridgeSupervisor"/>.</summary>
public enum BridgeRunState
{
    Stopped,

    /// <summary>The user started the bridge and a session is connecting or connected.</summary>
    Running,

    /// <summary>The user started the bridge but it dropped; retrying with backoff.</summary>
    Reconnecting,
}

/// <summary>Coarse failure classification so UI copy can be selected (and localized) by kind.</summary>
public enum BridgeFailureKind
{
    /// <summary>No device advertising heart rate was found within the scan timeout.</summary>
    DeviceNotFound,

    /// <summary>The Bluetooth radio is off or absent. Mapping is best-effort; see docs.</summary>
    BluetoothUnavailable,

    /// <summary>BLE is not supported on this platform.</summary>
    PlatformUnsupported,

    /// <summary>OSC output could not be configured (bad address or socket).</summary>
    OscConfig,

    /// <summary>No BLE connection was established within the run's initial time limit.</summary>
    ConnectionTimeout,

    Unknown,
}
