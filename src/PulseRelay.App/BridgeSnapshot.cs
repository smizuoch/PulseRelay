using PulseRelay.Core.HeartRate;

namespace PulseRelay.App;

/// <summary>State of the OSC output side of the bridge.</summary>
public enum OscOutputStatus
{
    Off,
    On,

    /// <summary>OSC is enabled but the most recent send attempt failed.</summary>
    Error,
}

/// <summary>
/// Immutable view of the bridge for UI consumption. Replaced wholesale on every change so
/// readers on any thread always see a consistent state; never mutated in place.
/// </summary>
public sealed record BridgeSnapshot(
    BridgeStatus Status,
    string? SourceDescription,
    int? Bpm,
    SensorContactStatus? SensorContact,
    DateTimeOffset? LastSampleAt,
    long SampleCount,
    string? LastError,
    BridgeFailureKind? FailureKind,
    OscOutputStatus OscStatus,
    string? OscError,
    long OscSentCount,
    long OscErrorCount)
{
    public static readonly BridgeSnapshot Initial = new(
        BridgeStatus.NotConnected,
        SourceDescription: null,
        Bpm: null,
        SensorContact: null,
        LastSampleAt: null,
        SampleCount: 0,
        LastError: null,
        FailureKind: null,
        OscStatus: OscOutputStatus.Off,
        OscError: null,
        OscSentCount: 0,
        OscErrorCount: 0);

    /// <summary>
    /// Status with staleness applied: Streaming becomes Stale when the last sample is older
    /// than <paramref name="staleThreshold"/>. Pure so the UI can re-evaluate it on a timer
    /// without mutating the snapshot.
    /// </summary>
    public BridgeStatus EffectiveStatus(DateTimeOffset nowUtc, TimeSpan staleThreshold) =>
        Status == BridgeStatus.Streaming && LastSampleAt is { } last && nowUtc - last > staleThreshold
            ? BridgeStatus.Stale
            : Status;
}
