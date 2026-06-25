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

    /// <summary>
    /// When set, <see cref="StartAsync"/> swaps <see cref="Description"/> to this value —
    /// mimics a BLE source that only learns the device name during the scan.
    /// </summary>
    public string? DescriptionAfterStart { get; set; }

    public Exception? StopFailure { get; set; }

    public Exception? DisposeFailure { get; set; }

    public TaskCompletionSource? StopGate { get; set; }

    public int StopCalls { get; private set; }

    public int DisposeCalls { get; private set; }

    public string Description { get; set; } = "Fake device";

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

        if (DescriptionAfterStart is not null)
        {
            Description = DescriptionAfterStart;
        }

        SetState(HeartRateSourceState.Subscribing);
        SetState(HeartRateSourceState.Subscribed);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        StopCalls++;
        if (StopGate is not null)
        {
            await StopGate.Task;
        }

        if (StopFailure is not null)
        {
            throw StopFailure;
        }

        SetState(HeartRateSourceState.Disconnected);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        if (DisposeFailure is not null)
        {
            throw DisposeFailure;
        }

        return ValueTask.CompletedTask;
    }

    public void EmitSample(
        int bpm,
        DateTimeOffset timestamp,
        SensorContactStatus sensorContact = SensorContactStatus.Contact)
    {
        if (State != HeartRateSourceState.Streaming)
        {
            SetState(HeartRateSourceState.Streaming);
        }

        SampleReceived?.Invoke(this, new HeartRateSample(
            bpm,
            sensorContact,
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

    /// <summary>Applied to every new source right after creation.</summary>
    public Action<FakeHeartRateSource>? Configure { get; set; }

    public FakeHeartRateSource Latest => Created[^1];

    public bool SupportsBle => true;

    public IHeartRateSource Create(AppSettings settings)
    {
        var source = new FakeHeartRateSource
        {
            StartFailure = StartFailureForAttempt?.Invoke(Created.Count),
        };
        Configure?.Invoke(source);
        Created.Add(source);
        return source;
    }
}
