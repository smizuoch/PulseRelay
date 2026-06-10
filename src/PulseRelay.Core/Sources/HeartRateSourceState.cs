namespace PulseRelay.Core.Sources;

/// <summary>Lifecycle states of a heart-rate source.</summary>
public enum HeartRateSourceState
{
    Idle,

    /// <summary>Scanning for a device advertising the Heart Rate Service.</summary>
    Scanning,

    /// <summary>Establishing a connection to the device.</summary>
    Connecting,

    /// <summary>Discovering services/characteristics and writing the CCCD.</summary>
    Subscribing,

    /// <summary>Subscription established; no measurement received yet.</summary>
    Subscribed,

    /// <summary>At least one valid Heart Rate Measurement has been parsed.</summary>
    Streaming,

    Disconnected,

    Failed,
}
