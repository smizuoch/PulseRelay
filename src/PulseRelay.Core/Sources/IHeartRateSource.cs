using PulseRelay.Core.HeartRate;

namespace PulseRelay.Core.Sources;

/// <summary>
/// A source of real-time heart-rate samples (BLE device, mock, etc.).
/// </summary>
/// <remarks>
/// Contract: <see cref="StartAsync"/> completes once the source is in
/// <see cref="HeartRateSourceState.Subscribed"/> (ready, no data yet) and throws on failure.
/// The source transitions to <see cref="HeartRateSourceState.Streaming"/> only when the
/// first valid sample is raised via <see cref="SampleReceived"/> — callers must not treat
/// a completed start as proof of data flow.
/// </remarks>
public interface IHeartRateSource : IAsyncDisposable
{
    /// <summary>Human-readable description for logs, e.g. "Mock (sine 60-100)" or a BLE device name.</summary>
    string Description { get; }

    HeartRateSourceState State { get; }

    event EventHandler<HeartRateSample>? SampleReceived;

    event EventHandler<HeartRateSourceState>? StateChanged;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();
}
