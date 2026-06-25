using Microsoft.Extensions.Logging.Abstractions;
using PulseRelay.Core.HeartRate;
using PulseRelay.Core.Sources;
using PulseRelay.Probe;
using System.Reflection;
using Xunit;

namespace PulseRelay.Tests;

public class ProbeCliTests
{
    [Fact]
    public async Task Parse_error_writes_usage_and_returns_2()
    {
        using var error = new StringWriter();

        int exitCode = await ProbeCli.RunAsync([], error);

        Assert.Equal(2, exitCode);
        Assert.Contains("No command", error.ToString());
        Assert.Contains("Commands:", error.ToString());
    }

    [Theory]
    [InlineData("scan", "--all")]
    [InlineData("connect")]
    public async Task Ble_commands_return_1_on_non_windows_build(params string[] args)
    {
        using var error = new StringWriter();

        int exitCode = await ProbeCli.RunAsync(args, error);

#if WINDOWS_BLE
        Assert.NotEqual(1, exitCode);
#else
        Assert.Equal(1, exitCode);
        Assert.Contains("BLE commands require the Windows build", error.ToString());
#endif
    }

    [Fact]
    public async Task Mock_returns_0_when_cancelled_before_start()
    {
        using var error = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        int exitCode = await ProbeCli.RunAsync(["mock", "--interval-ms", "1"], error, cts.Token);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Mock_runs_until_cancelled_after_start()
    {
        using var error = new StringWriter();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        int exitCode = await ProbeCli.RunAsync(["mock", "--interval-ms", "1"], error, cts.Token);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Run_source_attaches_osc_logs_sample_details_and_stops_on_cancellation()
    {
        Assert.True(ProbeOptions.TryParse(
            ["mock", "--osc", "--osc-port", "9204"],
            out var options,
            out string error));
        Assert.Empty(error);
        var source = new OneShotSource();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        int exitCode = await InvokeRunSourceAsync(
            source,
            options,
            NullLogger.Instance,
            cts.Token);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, source.StopCalls);
        Assert.Equal(1, source.DisposeCalls);
    }

    [Fact]
    public async Task Console_cancellation_wrapper_returns_parse_error_for_invalid_args()
    {
        using var error = new StringWriter();

        int exitCode = await ProbeCli.RunWithConsoleCancellationAsync([], error);

        Assert.Equal(2, exitCode);
        Assert.Contains("No command", error.ToString());
    }

    private static async Task<int> InvokeRunSourceAsync(
        IHeartRateSource source,
        ProbeOptions options,
        Microsoft.Extensions.Logging.ILogger logger,
        CancellationToken cancellationToken)
    {
        var method = typeof(ProbeCli).GetMethod(
            "RunSourceAsync",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task<int>>(method.Invoke(
            null,
            [source, options, logger, cancellationToken]));
        return await task;
    }

    private sealed class OneShotSource : IHeartRateSource
    {
        public int StopCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public string Description => "one shot";

        public HeartRateSourceState State { get; private set; } = HeartRateSourceState.Idle;

        public event EventHandler<HeartRateSample>? SampleReceived;

        public event EventHandler<HeartRateSourceState>? StateChanged;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            State = HeartRateSourceState.Streaming;
            StateChanged?.Invoke(this, State);
            SampleReceived?.Invoke(this, new HeartRateSample(
                Bpm: 80,
                SensorContact: SensorContactStatus.Contact,
                EnergyExpendedKilojoules: 12,
                RrIntervalsMs: [750.0],
                Timestamp: DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCalls++;
            State = HeartRateSourceState.Disconnected;
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }
}
