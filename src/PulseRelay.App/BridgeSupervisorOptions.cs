namespace PulseRelay.App;

/// <summary>Retry and stale-watchdog policy for <see cref="BridgeSupervisor"/>.</summary>
public sealed class BridgeSupervisorOptions
{
    /// <summary>
    /// Maximum time a BLE run may remain without ever establishing a subscribed connection.
    /// Once a connection is established this limit is permanently disabled for that run,
    /// including later reconnects. Null disables the limit.
    /// </summary>
    public TimeSpan? InitialConnectionTimeout { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Maximum time to wait for the first sample after a source reports that subscription
    /// setup completed. A timeout forces a fresh connection attempt.
    /// </summary>
    public TimeSpan FirstSampleTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Delays between reconnect attempts. The last entry repeats indefinitely while the user
    /// intends the bridge to run; Stop always cancels.
    /// </summary>
    public IReadOnlyList<TimeSpan> Backoff { get; init; } =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
    ];

    /// <summary>
    /// Silence after the last sample (while Streaming) that triggers a full reconnect.
    /// Display-only staleness uses the shorter <see cref="BridgeSession.StaleThreshold"/>.
    /// Never applies before the first sample: the Charge 6 has been observed to take ~19 s
    /// from subscription to first notification.
    /// </summary>
    public TimeSpan StaleReconnectThreshold { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How often the stale watchdog checks the last-sample age.</summary>
    public TimeSpan WatchdogInterval { get; init; } = TimeSpan.FromSeconds(1);
}
