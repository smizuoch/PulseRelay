using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRelay.App;
using PulseRelay.App.Localization;
using PulseRelay.App.Logging;
using PulseRelay.App.Settings;
using PulseRelay.Osc;
using PulseRelay.Desktop.ViewModels;
using System.Reflection;
using Xunit;

namespace PulseRelay.Tests;

public class DiagnosticsViewModelTests
{
    private sealed class Harness : IDisposable
    {
        public Harness(RingBufferLogSink? logSink = null)
        {
            LogSink = logSink ?? new RingBufferLogSink();
            Session = new BridgeSession(Factory, NullLoggerFactory.Instance);
            Supervisor = new BridgeSupervisor(Session);
            Settings = new AppSettings
            {
                SourceKind = HeartRateSourceKind.Mock,
                DeviceNameFilter = "Charge 6",
            };
            ViewModel = new DiagnosticsViewModel(Supervisor, Settings, LogSink);
        }

        public FakeSourceFactory Factory { get; } = new();

        public RingBufferLogSink LogSink { get; }

        public BridgeSession Session { get; }

        public BridgeSupervisor Supervisor { get; }

        public AppSettings Settings { get; }

        public DiagnosticsViewModel ViewModel { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            Supervisor.DisposeAsync().AsTask().GetAwaiter().GetResult();
            LogSink.Dispose();
        }
    }

    [AvaloniaFact]
    public void Constructor_loads_existing_log_snapshot_and_caps_visible_lines()
    {
        var sink = new RingBufferLogSink(capacity: 600);
        var logger = sink.CreateLogger("DiagnosticsTest");
        for (int i = 0; i < 505; i++)
        {
            logger.LogInformation("line {Line}", i);
        }

        using var h = new Harness(sink);

        Assert.Equal(500, h.ViewModel.LogLines.Count);
        Assert.DoesNotContain(h.ViewModel.LogLines, line => line.Contains("line 0", StringComparison.Ordinal));
        Assert.Contains(h.ViewModel.LogLines, line => line.Contains("line 504", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task New_log_entries_are_appended_on_ui_dispatcher()
    {
        using var h = new Harness();
        var logger = h.LogSink.CreateLogger("DiagnosticsTest");

        logger.LogWarning(new InvalidOperationException("boom"), "failed");

        await WaitForAsync(
            () => h.ViewModel.LogLines.Any(line => line.Contains("failed - InvalidOperationException: boom")),
            "log append");
    }

    [AvaloniaFact]
    public async Task Snapshot_changes_refresh_state_source_bpm_and_osc_text()
    {
        using var culture = new CultureScope("en");
        using var h = new Harness();

        h.Supervisor.Start(h.Settings);
        await WaitForAsync(() => h.Factory.Created.Count == 1, "source creation");
        h.Factory.Latest.Description = "BLE Charge 6";
        h.Factory.Latest.EmitSample(88, DateTimeOffset.UtcNow);

        await WaitForAsync(() => h.ViewModel.BpmText == "88", "diagnostics snapshot refresh");

        Assert.Contains("heart rate", h.ViewModel.StateText);
        Assert.Equal("BLE Charge 6", h.ViewModel.SourceText);
        Assert.Equal("88", h.ViewModel.BpmText);
        Assert.Contains("OSC on", h.ViewModel.OscText);
    }

    [AvaloniaFact]
    public async Task Osc_error_status_uses_error_copy()
    {
        using var culture = new CultureScope("en");
        using var h = new Harness();
        h.Settings.OscEnabled = false;
        h.Supervisor.Start(h.Settings);
        await WaitForAsync(
            () => h.Supervisor.Snapshot.Session.Status == BridgeStatus.WaitingForData,
            "source connection");

        Assert.True(h.Supervisor.TrySetOscEnabled(true, h.Settings, out _));
        InvokeOscSendCompleted(h.Session, GetPublisher(h.Session), new OscSendResult(80, "send failed"));
        InvokeRefresh(h.ViewModel);

        Assert.Contains(LocalizationManager.GetString("Output_OscError"), h.ViewModel.OscText);
    }

    [AvaloniaFact]
    public async Task Clear_log_command_clears_sink_and_visible_lines()
    {
        using var h = new Harness();
        h.LogSink.CreateLogger("DiagnosticsTest").LogInformation("before clear");
        await WaitForAsync(() => h.ViewModel.LogLines.Count == 1, "log append");

        h.ViewModel.ClearLogCommand.Execute(null);

        Assert.Empty(h.ViewModel.LogLines);
        Assert.Empty(h.LogSink.GetSnapshot());
    }

    [AvaloniaFact]
    public async Task Disposed_view_model_ignores_later_events()
    {
        using var culture = new CultureScope("en");
        using var h = new Harness();

        h.ViewModel.Dispose();
        h.LogSink.CreateLogger("DiagnosticsTest").LogInformation("after dispose");
        h.Supervisor.Start(h.Settings);
        await Task.Delay(20);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(h.ViewModel.LogLines);
        Assert.Equal("Not connected", h.ViewModel.StateText);
    }

    private static async Task WaitForAsync(Func<bool> condition, string description)
    {
        for (int i = 0; i < 500; i++)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for: {description}");
    }

    private static object GetPublisher(BridgeSession session)
    {
        var field = typeof(BridgeSession).GetField(
            "_publisher",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        object? publisher = field.GetValue(session);
        Assert.NotNull(publisher);
        return publisher;
    }

    private static void InvokeOscSendCompleted(
        BridgeSession session,
        object sender,
        OscSendResult result)
    {
        var method = typeof(BridgeSession).GetMethod(
            "OnOscSendCompleted",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(session, [sender, result]);
    }

    private static void InvokeRefresh(DiagnosticsViewModel viewModel)
    {
        var method = typeof(DiagnosticsViewModel).GetMethod(
            "Refresh",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(viewModel, null);
    }
}
