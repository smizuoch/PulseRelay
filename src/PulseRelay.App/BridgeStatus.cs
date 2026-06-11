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
}
