using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using PulseRelay.App;
using PulseRelay.App.Localization;
using PulseRelay.App.Settings;

namespace PulseRelay.Desktop.Services;

/// <summary>
/// Tray icon with a minimal menu: Show, Start/Stop, OSC on/off, Quit. Native menus don't
/// re-localize in place, so the whole menu is rebuilt on language changes and on
/// run-state/OSC flips. Start is idempotent in the supervisor, so the tray can never
/// create a duplicate active source; Quit goes through the lifetime shutdown, which runs
/// the existing App.OnExit cleanup (stop bridge, cancel retries, dispose source).
/// </summary>
public sealed class TrayIconController : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly BridgeSupervisor _supervisor;
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly TrayIcon _trayIcon;
    private (bool IsRunning, bool OscOn) _menuState;

    public TrayIconController(
        IClassicDesktopStyleApplicationLifetime desktop,
        BridgeSupervisor supervisor,
        AppSettings settings,
        SettingsStore settingsStore)
    {
        _desktop = desktop;
        _supervisor = supervisor;
        _settings = settings;
        _settingsStore = settingsStore;

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(
                new Uri("avares://PulseRelay.Desktop/Assets/tray-icon.png"))),
            ToolTipText = "PulseRelay",
        };
        TrayIcon.SetIcons(Application.Current!, [_trayIcon]);

        LocalizationManager.LanguageChanged += OnLanguageChanged;
        _supervisor.SnapshotChanged += OnSnapshotChanged;
        RebuildMenu();
    }

    public void Dispose()
    {
        LocalizationManager.LanguageChanged -= OnLanguageChanged;
        _supervisor.SnapshotChanged -= OnSnapshotChanged;
        // Explicit disposal: Windows otherwise leaves a ghost icon behind.
        _trayIcon.Dispose();
    }

    private bool EffectiveOscOn()
    {
        var snapshot = _supervisor.Snapshot;
        return snapshot.RunState == BridgeRunState.Stopped
            ? _settings.OscEnabled
            : snapshot.Session.OscStatus != OscOutputStatus.Off;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RebuildMenu();

    // Snapshots arrive on BLE/timer threads; rebuild only when the menu would change so
    // BPM samples don't churn native menus.
    private void OnSnapshotChanged(object? sender, SupervisorSnapshot snapshot) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_menuState != (_supervisor.IsRunning, EffectiveOscOn()))
            {
                RebuildMenu();
            }
        });

    private void RebuildMenu()
    {
        bool isRunning = _supervisor.IsRunning;
        bool oscOn = EffectiveOscOn();
        _menuState = (isRunning, oscOn);

        var show = new NativeMenuItem(LocalizationManager.GetString("Tray_Show"));
        show.Click += (_, _) => ShowMainWindow();

        var startStop = new NativeMenuItem(
            LocalizationManager.GetString(isRunning ? "Action_Stop" : "Action_Start"));
        startStop.Click += async (_, _) =>
        {
            if (_supervisor.IsRunning)
            {
                await _supervisor.StopAsync();
            }
            else
            {
                _supervisor.Start(_settings);
            }
        };

        var osc = new NativeMenuItem(
            LocalizationManager.GetString(oscOn ? "Tray_OscOff" : "Tray_OscOn"));
        osc.Click += (_, _) => ToggleOsc();

        var quit = new NativeMenuItem(LocalizationManager.GetString("Tray_Quit"));
        quit.Click += (_, _) => _desktop.Shutdown();

        _trayIcon.Menu = new NativeMenu
        {
            Items =
            {
                show,
                new NativeMenuItemSeparator(),
                startStop,
                osc,
                new NativeMenuItemSeparator(),
                quit,
            },
        };
    }

    private void ShowMainWindow()
    {
        if (_desktop.MainWindow is { } window)
        {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        }
    }

    // Mirrors DashboardViewModel.ToggleOsc, including persisting the explicit choice.
    private void ToggleOsc()
    {
        var snapshot = _supervisor.Snapshot;
        bool enable = snapshot.RunState == BridgeRunState.Stopped
            ? !_settings.OscEnabled
            : snapshot.Session.OscStatus == OscOutputStatus.Off;
        if (_supervisor.TrySetOscEnabled(enable, _settings, out _))
        {
            _settings.OscEnabled = enable;
            _settingsStore.Save(_settings);
        }
    }
}
