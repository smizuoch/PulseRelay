using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRelay.Core.HeartRate;

namespace PulseRelay.Core.Sources;

/// <summary>
/// Timer-driven mock source emitting a smooth sine wave between a configurable BPM band,
/// with RR intervals derived from the current BPM. Lets the full pipeline (including OSC)
/// run on platforms without BLE hardware support.
/// </summary>
public sealed class MockHeartRateSource : IHeartRateSource
{
    private static readonly TimeSpan SinePeriod = TimeSpan.FromSeconds(60);

    private readonly TimeSpan _interval;
    private readonly int _minBpm;
    private readonly int _maxBpm;
    private readonly ILogger _logger;

    private CancellationTokenSource? _cts;
    private Task? _pump;

    public MockHeartRateSource(
        TimeSpan? interval = null,
        int minBpm = 60,
        int maxBpm = 100,
        ILogger<MockHeartRateSource>? logger = null)
    {
        if (minBpm <= 0 || maxBpm < minBpm)
        {
            throw new ArgumentOutOfRangeException(nameof(minBpm), $"Invalid BPM band [{minBpm}, {maxBpm}].");
        }

        _interval = interval ?? TimeSpan.FromSeconds(1);
        if (_interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), _interval, "Sample interval must be positive.");
        }

        _minBpm = minBpm;
        _maxBpm = maxBpm;
        _logger = logger ?? NullLogger<MockHeartRateSource>.Instance;
    }

    public string Description => $"Mock (sine {_minBpm}-{_maxBpm} BPM every {_interval.TotalMilliseconds:0} ms)";

    public HeartRateSourceState State { get; private set; } = HeartRateSourceState.Idle;

    public event EventHandler<HeartRateSample>? SampleReceived;

    public event EventHandler<HeartRateSourceState>? StateChanged;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
        {
            throw new InvalidOperationException("Mock source is already started.");
        }

        SetState(HeartRateSourceState.Subscribing);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pump = PumpAsync(_cts.Token);
        SetState(HeartRateSourceState.Subscribed);
        _logger.LogInformation("Mock source subscribed, waiting for first tick");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync();
        try
        {
            await _pump!;
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
        _cts = null;
        _pump = null;
        SetState(HeartRateSourceState.Disconnected);
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        var start = DateTimeOffset.UtcNow;

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            double phase = (now - start).TotalSeconds / SinePeriod.TotalSeconds * 2 * Math.PI;
            double mid = (_minBpm + _maxBpm) / 2.0;
            double amplitude = (_maxBpm - _minBpm) / 2.0;
            int bpm = (int)Math.Round(mid + amplitude * Math.Sin(phase));

            var sample = new HeartRateSample(
                bpm,
                SensorContactStatus.Contact,
                EnergyExpendedKilojoules: null,
                RrIntervalsMs: [60_000.0 / bpm],
                Timestamp: now);

            if (State != HeartRateSourceState.Streaming)
            {
                SetState(HeartRateSourceState.Streaming);
            }

            SampleReceived?.Invoke(this, sample);
        }
    }

    private void SetState(HeartRateSourceState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        _logger.LogDebug("Mock source state -> {State}", state);
        StateChanged?.Invoke(this, state);
    }
}
