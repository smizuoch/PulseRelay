using PulseRelay.App;
using PulseRelay.App.Settings;
using PulseRelay.Core.HeartRate;
using PulseRelay.Core.Sources;

namespace PulseRelay.Tests;

/// <summary>
/// Hand-controlled heart-rate source for supervisor tests: the test decides when it
/// streams, drops, or fails to start.
/// </summary>
public sealed class FakeHeartRateSource : IHeartRateSource
{
    /// <summary>When set, <see cref="StartAsync"/> throws this (e.g. a scan timeout).</summary>
    public Exception? StartFailure { get; set; }

    public string Description => "Fake device";

    public HeartRateSourceState State { get; private set; } = HeartRateSourceState.Idle;

    public event EventHandler<HeartRateSample>? SampleReceived;

    public event EventHandler<HeartRateSourceState>? StateChanged;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (StartFailure is not null)
        {
            SetState(HeartRateSourceState.Failed);
            throw StartFailure;
        }

        SetState(HeartRateSourceState.Subscribing);
        SetState(HeartRateSourceState.Subscribed);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        SetState(HeartRateSourceState.Disconnected);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void EmitSample(int bpm, DateTimeOffset timestamp)
    {
        if (State != HeartRateSourceState.Streaming)
        {
            SetState(HeartRateSourceState.Streaming);
        }

        SampleReceived?.Invoke(this, new HeartRateSample(
            bpm,
            SensorContactStatus.Contact,
            EnergyExpendedKilojoules: null,
            RrIntervalsMs: [60_000.0 / bpm],
            Timestamp: timestamp));
    }

    public void RaiseDisconnected() => SetState(HeartRateSourceState.Disconnected);

    private void SetState(HeartRateSourceState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, state);
    }
}

/// <summary>Factory handing out fresh fakes; tests assert on <see cref="Created"/> count.</summary>
public sealed class FakeSourceFactory : IHeartRateSourceFactory
{
    public List<FakeHeartRateSource> Created { get; } = [];

    /// <summary>Applied to every new source; lets tests fail the first N attempts.</summary>
    public Func<int, Exception?>? StartFailureForAttempt { get; set; }

    public FakeHeartRateSource Latest => Created[^1];

    public bool SupportsBle => true;

    public IHeartRateSource Create(AppSettings settings)
    {
        var source = new FakeHeartRateSource
        {
            StartFailure = StartFailureForAttempt?.Invoke(Created.Count),
        };
        Created.Add(source);
        return source;
    }
}
