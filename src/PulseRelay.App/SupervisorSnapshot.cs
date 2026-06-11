namespace PulseRelay.App;

/// <summary>
/// Immutable view of the supervised bridge: the underlying session snapshot plus user intent
/// and retry progress. Like <see cref="BridgeSnapshot"/>, replaced wholesale on every change.
/// </summary>
public sealed record SupervisorSnapshot(
    BridgeSnapshot Session,
    BridgeRunState RunState,
    int RetryAttempt,
    DateTimeOffset? NextRetryAt,
    bool HasStreamedThisRun)
{
    public static readonly SupervisorSnapshot Initial = new(
        BridgeSnapshot.Initial,
        BridgeRunState.Stopped,
        RetryAttempt: 0,
        NextRetryAt: null,
        HasStreamedThisRun: false);

    /// <summary>
    /// Session status with staleness applied, overlaid with <see cref="BridgeStatus.Reconnecting"/>
    /// while a retry is pending and the session has nothing better to report. An in-flight retry
    /// attempt still surfaces Searching/Connecting so the user sees progress.
    /// </summary>
    public BridgeStatus EffectiveStatus(DateTimeOffset nowUtc, TimeSpan staleThreshold)
    {
        var status = Session.EffectiveStatus(nowUtc, staleThreshold);
        return RunState == BridgeRunState.Reconnecting
            && status is BridgeStatus.NotConnected or BridgeStatus.Disconnected or BridgeStatus.Failed
                ? BridgeStatus.Reconnecting
                : status;
    }
}
