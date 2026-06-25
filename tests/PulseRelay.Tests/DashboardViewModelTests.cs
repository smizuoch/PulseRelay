using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRelay.App;
using PulseRelay.App.Localization;
using PulseRelay.App.Logging;
using PulseRelay.App.Settings;
using PulseRelay.Core.HeartRate;
using PulseRelay.Desktop.ViewModels;
using System.Reflection;
using Xunit;

namespace PulseRelay.Tests;

public class DashboardViewModelTests
{
    private sealed class Harness : IDisposable
    {
        private readonly string _directory;

        public Harness(string? directory = null)
        {
            _directory = directory ?? Path.Combine(
                Path.GetTempPath(),
                "PulseRelayTests",
                Guid.NewGuid().ToString("N"));
            Store = new SettingsStore(_directory);
            Session = new BridgeSession(Factory, NullLoggerFactory.Instance);
            Supervisor = new BridgeSupervisor(Session);
            Settings = new AppSettings { SourceKind = HeartRateSourceKind.Mock };
            ViewModel = new DashboardViewModel(Supervisor, Settings, Store, new RingBufferLogSink());
        }

        public FakeSourceFactory Factory { get; } = new();

        public SettingsStore Store { get; }

        public BridgeSession Session { get; }

        public BridgeSupervisor Supervisor { get; }

        public AppSettings Settings { get; }

        public DashboardViewModel ViewModel { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            Supervisor.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }

    [AvaloniaFact]
    public void Language_option_uses_system_label_for_unknown_value()
    {
        using var culture = new CultureScope("en");
        var option = new LanguageOption((AppLanguage)999);

        Assert.Equal("System", option.Label);
    }

    [AvaloniaFact]
    public void Language_options_use_localized_known_labels()
    {
        using var culture = new CultureScope("en");

        Assert.Equal("English", new LanguageOption(AppLanguage.English).Label);
        Assert.Equal("日本語", new LanguageOption(AppLanguage.Japanese).Label);
    }

    [AvaloniaFact]
    public async Task Start_command_streams_and_updates_dashboard_copy()
    {
        using var culture = new CultureScope("en");
        using var h = new Harness();

        h.ViewModel.StartCommand.Execute(null);
        await WaitForAsync(() => h.Factory.Created.Count == 1, "source creation");
        h.Factory.Latest.EmitSample(72, DateTimeOffset.UtcNow);

        await WaitForAsync(() => h.ViewModel.BpmText == "72", "streaming dashboard refresh");

        Assert.Equal("72", h.ViewModel.BpmText);
        Assert.True(h.ViewModel.IsStreaming);
        Assert.False(h.ViewModel.ShowStart);
        Assert.Equal("Fake device", h.ViewModel.DeviceLine);
        Assert.True(h.ViewModel.HasContactInfo);
        Assert.Equal(LocalizationManager.GetString("Device_ContactDetected"), h.ViewModel.ContactText);
    }

    [AvaloniaFact]
    public async Task No_contact_sample_updates_contact_copy()
    {
        using var culture = new CultureScope("en");
        using var h = new Harness();

        h.ViewModel.StartCommand.Execute(null);
        await WaitForAsync(() => h.Factory.Created.Count == 1, "source creation");
        h.Factory.Latest.EmitSample(72, DateTimeOffset.UtcNow, SensorContactStatus.NoContact);

        await WaitForAsync(() => h.ViewModel.BpmText == "72", "sample refresh");

        Assert.True(h.ViewModel.HasContactInfo);
        Assert.Equal(LocalizationManager.GetString("Device_ContactNotDetected"), h.ViewModel.ContactText);
    }

    [AvaloniaFact]
    public async Task Contact_not_supported_hides_contact_copy()
    {
        using var h = new Harness();

        h.ViewModel.StartCommand.Execute(null);
        await WaitForAsync(() => h.Factory.Created.Count == 1, "source creation");
        h.Factory.Latest.EmitSample(72, DateTimeOffset.UtcNow, SensorContactStatus.NotSupported);

        await WaitForAsync(() => h.ViewModel.BpmText == "72", "sample refresh");

        Assert.False(h.ViewModel.HasContactInfo);
        Assert.Equal("", h.ViewModel.ContactText);
    }

    [AvaloniaFact]
    public async Task Disconnected_state_uses_failure_brush()
    {
        using var culture = new CultureScope("en");
        using var h = new Harness();

        Assert.True(await h.Session.ConnectAsync(h.Settings, CancellationToken.None));
        h.Factory.Latest.RaiseDisconnected();

        await WaitForAsync(() => h.ViewModel.DeviceStateText.Contains("Disconnected"), "disconnected refresh");

        Assert.False(h.ViewModel.IsStreaming);
        Assert.True(h.ViewModel.ShowStart);
    }

    [AvaloniaFact]
    public async Task Stop_command_is_ignored_while_stop_is_already_in_progress()
    {
        using var h = new Harness();
        h.ViewModel.StartCommand.Execute(null);
        await WaitForAsync(() => h.Factory.Created.Count == 1, "source creation");
        var stopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Factory.Latest.StopGate = stopGate;

        h.ViewModel.StopCommand.Execute(null);
        await WaitForAsync(() => h.ViewModel.IsBusy, "stop busy flag");
        h.ViewModel.StopCommand.Execute(null);
        await Task.Delay(20);

        Assert.Equal(1, h.Factory.Latest.StopCalls);

        stopGate.SetResult();
        await WaitForAsync(() => !h.ViewModel.IsBusy && h.ViewModel.ShowStart, "stop completion");
    }

    [AvaloniaFact]
    public async Task Reconnect_command_skips_pending_backoff()
    {
        using var h = new Harness();
        h.Factory.StartFailureForAttempt = _ => new TimeoutException("no device");
        h.ViewModel.StartCommand.Execute(null);
        await WaitForAsync(() => h.Supervisor.Snapshot.RunState == BridgeRunState.Reconnecting, "reconnecting");
        int createdBefore = h.Factory.Created.Count;

        h.ViewModel.ReconnectCommand.Execute(null);

        await WaitForAsync(() => h.Factory.Created.Count > createdBefore, "manual reconnect");
        Assert.True(h.ViewModel.ShowReconnect);
    }

    [AvaloniaFact]
    public void Toggle_osc_while_stopped_persists_choice_and_updates_copy()
    {
        using var culture = new CultureScope("en");
        using var h = new Harness();

        Assert.True(h.ViewModel.OscOn);
        Assert.Equal("OSC on", h.ViewModel.OscStateText);

        h.ViewModel.ToggleOscCommand.Execute(null);

        Assert.False(h.Settings.OscEnabled);
        Assert.False(h.ViewModel.OscOn);
        Assert.Equal("OSC off", h.ViewModel.OscStateText);
        Assert.False(h.Store.Load().OscEnabled);
    }

    [AvaloniaFact]
    public async Task Toggle_osc_while_running_surfaces_osc_error_without_persisting()
    {
        using var culture = new CultureScope("en");
        using var h = new Harness();
        h.Settings.OscEnabled = false;
        h.Settings.OscAddress = "not-an-osc-address";
        h.ViewModel.StartCommand.Execute(null);
        await WaitForAsync(
            () => h.Supervisor.Snapshot.RunState == BridgeRunState.Running
                && h.Supervisor.Snapshot.Session.Status == BridgeStatus.WaitingForData,
            "source connection");

        h.ViewModel.ToggleOscCommand.Execute(null);
        string errorCopy = LocalizationManager.GetString("Output_OscError");
        await WaitForAsync(() => h.ViewModel.OscStateText == errorCopy, "OSC error copy");

        Assert.False(h.Settings.OscEnabled);
        Assert.False(File.Exists(h.Store.FilePath));
        Assert.True(h.ViewModel.OscOn);
        Assert.Equal(errorCopy, h.ViewModel.OscStateText);
    }

    [AvaloniaFact]
    public void Toggle_osc_reverts_when_settings_save_fails()
    {
        string blockedDirectory = Path.Combine(
            Path.GetTempPath(),
            "PulseRelayTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(blockedDirectory)!);
        File.WriteAllText(blockedDirectory, "not a directory");
        try
        {
            using var h = new Harness(blockedDirectory);

            h.ViewModel.ToggleOscCommand.Execute(null);

            Assert.True(h.Settings.OscEnabled);
            Assert.True(h.ViewModel.OscOn);
        }
        finally
        {
            File.Delete(blockedDirectory);
        }
    }

    [AvaloniaFact]
    public void Selected_language_reverts_when_settings_save_fails()
    {
        using var culture = new CultureScope("en");
        string blockedDirectory = Path.Combine(
            Path.GetTempPath(),
            "PulseRelayTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(blockedDirectory)!);
        File.WriteAllText(blockedDirectory, "not a directory");
        try
        {
            using var h = new Harness(blockedDirectory);
            var japanese = h.ViewModel.LanguageOptions.First(o => o.Value == AppLanguage.Japanese);

            h.ViewModel.SelectedLanguage = japanese;

            Assert.Equal(AppLanguage.System, h.Settings.Language);
            Assert.Equal(AppLanguage.System, h.ViewModel.SelectedLanguage.Value);
        }
        finally
        {
            File.Delete(blockedDirectory);
        }
    }

    [AvaloniaFact]
    public void Selected_language_persists_and_applies_language()
    {
        using var culture = new CultureScope("en");
        using var h = new Harness();
        var japanese = h.ViewModel.LanguageOptions.First(o => o.Value == AppLanguage.Japanese);

        h.ViewModel.SelectedLanguage = japanese;

        Assert.Equal(AppLanguage.Japanese, h.Settings.Language);
        Assert.Equal(AppLanguage.Japanese, h.Store.Load().Language);
        Assert.Equal("ja", Thread.CurrentThread.CurrentUICulture.Name);
    }

    [AvaloniaFact]
    public void Creates_child_view_models()
    {
        using var h = new Harness();

        Assert.NotNull(h.ViewModel.CreateSettingsViewModel());
        Assert.NotNull(h.ViewModel.CreateDiagnosticsViewModel());
    }

    [AvaloniaFact]
    public void Dispose_is_idempotent()
    {
        using var h = new Harness();

        h.ViewModel.Dispose();
        h.ViewModel.Dispose();
    }

    [AvaloniaFact]
    public void Refresh_after_dispose_is_noop()
    {
        using var h = new Harness();
        h.ViewModel.Dispose();
        string before = h.ViewModel.StatusText;

        InvokeRefresh(h.ViewModel);

        Assert.Equal(before, h.ViewModel.StatusText);
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

    private static void InvokeRefresh(DashboardViewModel viewModel)
    {
        var method = typeof(DashboardViewModel).GetMethod(
            "Refresh",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(viewModel, null);
    }
}
