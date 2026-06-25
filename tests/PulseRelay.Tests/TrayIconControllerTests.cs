using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRelay.App;
using PulseRelay.App.Localization;
using PulseRelay.App.Settings;
using PulseRelay.Desktop.Services;
using Xunit;

namespace PulseRelay.Tests;

public class TrayIconControllerTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PulseRelayTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Builds_stopped_menu_with_show_start_osc_off_and_quit()
    {
        using var culture = new CultureScope("en");
        using var h = new Harness(_directory);
        using var controller = h.CreateController();

        string[] headers = MenuItemHeaders(controller);

        Assert.Equal(["Show", "Start", "Turn OSC off", "Quit"], headers);
    }

    [AvaloniaFact]
    public void Dispose_is_idempotent()
    {
        using var h = new Harness(_directory);
        using var controller = h.CreateController();

        controller.Dispose();
        controller.Dispose();
    }

    [AvaloniaFact]
    public void Show_menu_item_restores_main_window_when_present()
    {
        using var h = new Harness(_directory);
        using var controller = h.CreateController();
        h.Desktop.MainWindow!.WindowState = WindowState.Minimized;
        h.Desktop.MainWindow.Hide();

        Click(MenuItems(controller)[0]);

        Assert.True(h.Desktop.MainWindow.IsVisible);
        Assert.Equal(WindowState.Normal, h.Desktop.MainWindow.WindowState);
    }

    [AvaloniaFact]
    public void Show_menu_item_is_noop_without_main_window()
    {
        using var h = new Harness(_directory);
        h.Desktop.MainWindow = null;
        using var controller = h.CreateController();

        Click(MenuItems(controller)[0]);
    }

    [AvaloniaFact]
    public async Task Start_stop_menu_item_controls_supervisor_and_rebuilds_label()
    {
        using var culture = new CultureScope("en");
        using var h = new Harness(_directory);
        using var controller = h.CreateController();

        Click(MenuItems(controller)[1]);
        await WaitForAsync(() => h.Supervisor.IsRunning, "supervisor start");
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("Stop", MenuItemHeaders(controller));

        Click(MenuItems(controller)[1]);
        await WaitForAsync(() => !h.Supervisor.IsRunning, "supervisor stop");
        await WaitForAsync(
            () => MenuItemHeaders(controller).Contains("Start"),
            "start menu rebuild");

        Assert.Contains("Start", MenuItemHeaders(controller));
    }

    [AvaloniaFact]
    public async Task Menu_rebuild_keeps_the_same_native_menu_instance()
    {
        using var culture = new CultureScope("en");
        using var h = new Harness(_directory);
        using var controller = h.CreateController();

        var menuBefore = Menu(controller);

        // Starting flips the run-state label (Start -> Stop), forcing a menu rebuild.
        Click(MenuItems(controller)[1]);
        await WaitForAsync(() => h.Supervisor.IsRunning, "supervisor start");
        await WaitForAsync(
            () => MenuItemHeaders(controller).Contains("Stop"),
            "stop label rebuild");

        // macOS's native menu exporter aborts the whole process if TrayIcon.Menu is
        // reassigned to a different NativeMenu after export ("The menu being updated does
        // not match"). A rebuild must therefore mutate Items on the same instance.
        Assert.Same(menuBefore, Menu(controller));
    }

    [AvaloniaFact]
    public async Task Snapshot_that_does_not_change_menu_state_does_not_rebuild_menu()
    {
        using var h = new Harness(_directory);
        using var controller = h.CreateController();

        Click(MenuItems(controller)[1]);
        await WaitForAsync(() => h.Factory.Created.Count == 1, "source creation");
        h.Factory.Latest.EmitSample(72, DateTimeOffset.UtcNow);
        await WaitForAsync(() => h.Supervisor.Snapshot.Session.Bpm == 72, "first sample");
        var menu = Menu(controller);

        h.Factory.Latest.EmitSample(73, DateTimeOffset.UtcNow);
        await WaitForAsync(() => h.Supervisor.Snapshot.Session.Bpm == 73, "second sample");
        Dispatcher.UIThread.RunJobs();

        Assert.Same(menu, Menu(controller));
    }

    [AvaloniaFact]
    public void Snapshot_callback_returns_after_dispose()
    {
        using var h = new Harness(_directory);
        using var controller = h.CreateController();
        controller.Dispose();

        InvokeSnapshotChanged(controller, h.Supervisor.Snapshot);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Osc_menu_item_persists_stopped_choice()
    {
        using var culture = new CultureScope("en");
        using var h = new Harness(_directory);
        using var controller = h.CreateController();

        Click(MenuItems(controller)[2]);

        Assert.False(h.Settings.OscEnabled);
        Assert.False(h.Store.Load().OscEnabled);
        await WaitForAsync(
            () => MenuItemHeaders(controller).Contains("Turn OSC on"),
            "OSC menu rebuild");
        Assert.Contains("Turn OSC on", MenuItemHeaders(controller));
    }

    [AvaloniaFact]
    public void Osc_menu_item_reverts_stopped_choice_when_save_fails()
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
            using var controller = h.CreateController();

            Click(MenuItems(controller)[2]);

            Assert.True(h.Settings.OscEnabled);
            Assert.Equal(OscOutputStatus.On, h.Supervisor.Snapshot.Session.OscStatus);
        }
        finally
        {
            File.Delete(blockedDirectory);
        }
    }

    [AvaloniaFact]
    public async Task Osc_menu_item_enables_osc_while_running()
    {
        using var culture = new CultureScope("en");
        using var h = new Harness(_directory);
        h.Settings.OscEnabled = false;
        using var controller = h.CreateController();

        Click(MenuItems(controller)[1]);
        await WaitForAsync(() => h.Supervisor.Snapshot.RunState == BridgeRunState.Running, "running");

        Click(MenuItems(controller)[2]);

        await WaitForAsync(() => h.Supervisor.Snapshot.Session.OscStatus == OscOutputStatus.On, "OSC on");
        Assert.True(h.Settings.OscEnabled);
        Assert.Contains("Turn OSC off", MenuItemHeaders(controller));
    }

    [AvaloniaFact]
    public void Quit_menu_item_requests_shutdown()
    {
        using var h = new Harness(_directory);
        using var controller = h.CreateController();

        Click(MenuItems(controller)[3]);

        Assert.Equal(1, h.ShutdownRequests);
    }

    [AvaloniaFact]
    public void Language_change_rebuilds_menu_copy()
    {
        using var culture = new CultureScope("en");
        using var h = new Harness(_directory);
        using var controller = h.CreateController();

        LocalizationManager.Apply(AppLanguage.Japanese);

        Assert.Contains(LocalizationManager.GetString("Tray_Quit"), MenuItemHeaders(controller));
    }

    private static NativeMenu Menu(TrayIconController controller)
    {
        var field = typeof(TrayIconController).GetField(
            "_trayIcon",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var tray = Assert.IsType<TrayIcon>(field.GetValue(controller));
        Assert.NotNull(tray.Menu);
        return tray.Menu;
    }

    private static NativeMenuItem[] MenuItems(TrayIconController controller)
    {
        return Menu(controller).Items
            .OfType<NativeMenuItem>()
            .Where(item => item is not NativeMenuItemSeparator)
            .ToArray();
    }

    private static string[] MenuItemHeaders(TrayIconController controller) =>
        MenuItems(controller).Select(item =>
        {
            Assert.NotNull(item.Header);
            return item.Header;
        }).ToArray();

    private static void Click(NativeMenuItem item)
    {
        var method = typeof(NativeMenuItem).GetMethod(
            "Avalonia.Controls.INativeMenuItemExporterEventsImplBridge.RaiseClicked",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(item, null);
    }

    private static void InvokeSnapshotChanged(
        TrayIconController controller,
        SupervisorSnapshot snapshot)
    {
        var method = typeof(TrayIconController).GetMethod(
            "OnSnapshotChanged",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(controller, [null, snapshot]);
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

    private sealed class Harness : IDisposable
    {
        public Harness(string directory)
        {
            Store = new SettingsStore(directory);
            Settings = new AppSettings { SourceKind = HeartRateSourceKind.Mock, OscEnabled = true };
            Session = new BridgeSession(Factory, NullLoggerFactory.Instance);
            Supervisor = new BridgeSupervisor(Session);
            Desktop.MainWindow = new Window();
        }

        public ClassicDesktopStyleApplicationLifetime Desktop { get; } = new();

        public FakeSourceFactory Factory { get; } = new();

        public BridgeSession Session { get; }

        public BridgeSupervisor Supervisor { get; }

        public AppSettings Settings { get; }

        public SettingsStore Store { get; }

        public int ShutdownRequests { get; private set; }

        public TrayIconController CreateController() => new(
            Desktop,
            Supervisor,
            Settings,
            Store,
            () =>
            {
                ShutdownRequests++;
                return Task.CompletedTask;
            });

        public void Dispose()
        {
            Supervisor.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Desktop.MainWindow?.Close();
        }
    }

}
