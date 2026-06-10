using PulseRelay.Core.HeartRate;
using PulseRelay.Core.Sources;
using Xunit;

namespace PulseRelay.Tests;

public class MockHeartRateSourceTests
{
    [Fact]
    public async Task Emits_samples_within_band_after_start()
    {
        await using var source = new MockHeartRateSource(
            interval: TimeSpan.FromMilliseconds(10), minBpm: 60, maxBpm: 100);

        var received = new TaskCompletionSource<HeartRateSample>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SampleReceived += (_, sample) => received.TrySetResult(sample);

        await source.StartAsync(CancellationToken.None);
        var first = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.InRange(first.Bpm, 60, 100);
        Assert.Equal(SensorContactStatus.Contact, first.SensorContact);
        Assert.Single(first.RrIntervalsMs);
    }

    [Fact]
    public async Task Transitions_subscribed_before_streaming()
    {
        await using var source = new MockHeartRateSource(interval: TimeSpan.FromMilliseconds(10));

        var states = new List<HeartRateSourceState>();
        var streaming = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.StateChanged += (_, state) =>
        {
            lock (states)
            {
                states.Add(state);
            }

            if (state == HeartRateSourceState.Streaming)
            {
                streaming.TrySetResult();
            }
        };

        await source.StartAsync(CancellationToken.None);
        Assert.Equal(HeartRateSourceState.Subscribed, source.State);

        await streaming.Task.WaitAsync(TimeSpan.FromSeconds(5));

        lock (states)
        {
            int subscribedIndex = states.IndexOf(HeartRateSourceState.Subscribed);
            int streamingIndex = states.IndexOf(HeartRateSourceState.Streaming);
            Assert.True(subscribedIndex >= 0 && subscribedIndex < streamingIndex,
                $"Expected Subscribed before Streaming, got: {string.Join(", ", states)}");
        }
    }

    [Fact]
    public async Task Stops_emitting_after_stop()
    {
        var source = new MockHeartRateSource(interval: TimeSpan.FromMilliseconds(10));

        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SampleReceived += (_, _) => received.TrySetResult();

        await source.StartAsync(CancellationToken.None);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await source.StopAsync();
        Assert.Equal(HeartRateSourceState.Disconnected, source.State);

        int countAfterStop = 0;
        source.SampleReceived += (_, _) => Interlocked.Increment(ref countAfterStop);
        await Task.Delay(100, CancellationToken.None);

        Assert.Equal(0, countAfterStop);
    }

    [Fact]
    public async Task Double_start_throws()
    {
        await using var source = new MockHeartRateSource(interval: TimeSpan.FromMilliseconds(10));

        await source.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.StartAsync(CancellationToken.None));
    }
}
