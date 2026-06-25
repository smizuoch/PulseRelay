using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using PulseRelay.App;
using PulseRelay.App.Localization;
using PulseRelay.App.Logging;
using PulseRelay.App.Settings;
using PulseRelay.Core.Sources;
using PulseRelay.Desktop.Services;
using Xunit;

namespace PulseRelay.Tests;

public class DesktopAppRuntimeTests : IDisposable
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
    public async Task Start_loads_settings_applies_theme_and_falls_back_to_mock_when_ble_is_unavailable()
    {
        using var culture = new CultureScope("en");
        var store = new SettingsStore(_directory);
        store.Save(new AppSettings
        {
            SourceKind = HeartRateSourceKind.Ble,
            Theme = AppTheme.Light,
            Language = AppLanguage.Japanese,
        });
        var runtime = CreateRuntime(
            store,
            new SourceFactory(supportsBle: false),
            out var desktop,
            out _,
            out _);

        runtime.Start();

        Assert.Equal(ShutdownMode.OnExplicitShutdown, desktop.ShutdownMode);
        Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
        Assert.Equal("ja", Thread.CurrentThread.CurrentUICulture.Name);
        Assert.Equal(HeartRateSourceKind.Mock, runtime.Settings!.SourceKind);
        Assert.Equal(HeartRateSourceKind.Ble, store.Load().SourceKind);

        await runtime.RequestShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Start_auto_connects_when_enabled()
    {
        var store = new SettingsStore(_directory);
        store.Save(new AppSettings
        {
            SourceKind = HeartRateSourceKind.Mock,
            AutoConnectOnLaunch = true,
        });
        var sourceFactory = new SourceFactory(supportsBle: true);
        var runtime = CreateRuntime(store, sourceFactory, out _, out _, out _);

        runtime.Start();
        await WaitForAsync(() => runtime.Supervisor?.IsRunning == true, "auto connect");

        Assert.Single(sourceFactory.Inner.Created);

        await runtime.RequestShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Request_shutdown_is_idempotent_and_disposes_tray_once()
    {
        var store = new SettingsStore(_directory);
        store.Save(new AppSettings { SourceKind = HeartRateSourceKind.Mock });
        var runtime = CreateRuntime(
            store,
            new SourceFactory(supportsBle: true),
            out _,
            out var tray,
            out var shutdown);
        runtime.Start();

        Task first = runtime.RequestShutdownAsync();
        Task second = runtime.RequestShutdownAsync();

        Assert.Same(first, second);
        await first;

        Assert.Equal(1, tray.DisposeCalls);
        Assert.Equal(1, shutdown.Calls);
    }

    [AvaloniaFact]
    public async Task Request_shutdown_before_start_runs_lifetime_shutdown_without_components()
    {
        var desktop = new ClassicDesktopStyleApplicationLifetime();
        var shutdown = new ShutdownRecorder();
        var runtime = new DesktopAppRuntime(
            desktop,
            shutdownDesktop: _ => shutdown.Calls++);

        await runtime.RequestShutdownAsync();

        Assert.Equal(1, shutdown.Calls);
        Assert.Null(runtime.Supervisor);
        Assert.Null(runtime.MainWindow);
    }

    [AvaloniaFact]
    public async Task Request_shutdown_marshal_hides_window_from_background_thread()
    {
        var store = new SettingsStore(_directory);
        store.Save(new AppSettings { SourceKind = HeartRateSourceKind.Mock });
        var runtime = CreateRuntime(
            store,
            new SourceFactory(supportsBle: true),
            out _,
            out _,
            out var shutdown);
        runtime.Start();
        runtime.MainWindow!.Show();
        Assert.True(runtime.MainWindow.IsVisible);

        await Task.Run(() => runtime.RequestShutdownAsync());
        Dispatcher.UIThread.RunJobs();

        Assert.False(runtime.MainWindow.IsVisible);
        Assert.Equal(1, shutdown.Calls);
    }

    [AvaloniaFact]
    public async Task Shutdown_requested_event_is_canceled_and_runs_async_shutdown()
    {
        var store = new SettingsStore(_directory);
        store.Save(new AppSettings { SourceKind = HeartRateSourceKind.Mock });
        var runtime = CreateRuntime(
            store,
            new SourceFactory(supportsBle: true),
            out _,
            out _,
            out var shutdown);
        runtime.Start();
        var args = new ShutdownRequestedEventArgs();

        InvokeShutdownRequested(runtime, args);
        await WaitForAsync(() => shutdown.Calls == 1, "shutdown request");

        Assert.True(args.Cancel);
    }

    [AvaloniaFact]
    public async Task Shutdown_still_completes_when_supervisor_stop_fails()
    {
        var store = new SettingsStore(_directory);
        store.Save(new AppSettings
        {
            SourceKind = HeartRateSourceKind.Mock,
            AutoConnectOnLaunch = true,
        });
        var sourceFactory = new SourceFactory(supportsBle: true)
        {
            Configure = source => source.StopFailure = new IOException("stop failed"),
        };
        var runtime = CreateRuntime(
            store,
            sourceFactory,
            out _,
            out var tray,
            out var shutdown);
        runtime.Start();
        await WaitForAsync(() => sourceFactory.Inner.Created.Count == 1, "source creation");

        await runtime.RequestShutdownAsync();

        Assert.Equal(1, tray.DisposeCalls);
        Assert.Equal(1, shutdown.Calls);
    }

    [AvaloniaFact]
    public async Task Start_without_auto_connect_leaves_supervisor_stopped()
    {
        var store = new SettingsStore(_directory);
        store.Save(new AppSettings
        {
            SourceKind = HeartRateSourceKind.Mock,
            AutoConnectOnLaunch = false,
            Theme = AppTheme.Dark,
        });
        var sourceFactory = new SourceFactory(supportsBle: true);
        var runtime = CreateRuntime(store, sourceFactory, out _, out _, out _);

        runtime.Start();

        Assert.NotNull(runtime.Supervisor);
        Assert.False(runtime.Supervisor.IsRunning);
        Assert.Empty(sourceFactory.Inner.Created);
        Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);

        await runtime.RequestShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Start_can_use_default_factories_for_logger_log_sink_and_main_window()
    {
        var store = new SettingsStore(_directory);
        store.Save(new AppSettings { SourceKind = HeartRateSourceKind.Mock });
        var sourceFactory = new SourceFactory(supportsBle: true);
        var tray = new FakeTray();
        var shutdown = new ShutdownRecorder();
        var desktop = new ClassicDesktopStyleApplicationLifetime();
        var runtime = new DesktopAppRuntime(
            desktop,
            createSettingsStore: _ => store,
            createSourceFactory: _ => sourceFactory,
            createTray: (_, _, _, _, _, _) => tray,
            shutdownDesktop: _ => shutdown.Calls++);

        runtime.Start();

        Assert.NotNull(runtime.MainWindow);
        Assert.Same(runtime.MainWindow, desktop.MainWindow);

        await runtime.RequestShutdownAsync();
        Assert.Equal(1, tray.DisposeCalls);
        Assert.Equal(1, shutdown.Calls);
    }

    [AvaloniaFact]
    public void Constructor_accepts_all_default_factories()
    {
        var runtime = new DesktopAppRuntime(new ClassicDesktopStyleApplicationLifetime());

        Assert.Null(runtime.Supervisor);
        Assert.Null(runtime.Settings);
        Assert.Null(runtime.MainWindow);
    }

    private DesktopAppRuntime CreateRuntime(
        SettingsStore store,
        SourceFactory sourceFactory,
        out ClassicDesktopStyleApplicationLifetime desktop,
        out FakeTray tray,
        out ShutdownRecorder shutdown)
    {
        desktop = new ClassicDesktopStyleApplicationLifetime();
        var createdTray = new FakeTray();
        var shutdownRecorder = new ShutdownRecorder();
        tray = createdTray;
        shutdown = shutdownRecorder;
        return new DesktopAppRuntime(
            desktop,
            createLoggerFactory: logSink => LoggerFactory.Create(builder => builder.AddProvider(logSink)),
            createSettingsStore: _ => store,
            createSourceFactory: _ => sourceFactory,
            createTray: (_, _, _, _, _, _) => createdTray,
            shutdownDesktop: _ => shutdownRecorder.Calls++);
    }

    private static async Task WaitForAsync(Func<bool> condition, string description)
    {
        for (int i = 0; i < 500; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for: {description}");
    }

    private static void InvokeShutdownRequested(
        DesktopAppRuntime runtime,
        ShutdownRequestedEventArgs args)
    {
        var method = typeof(DesktopAppRuntime).GetMethod(
            "OnShutdownRequested",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(runtime, [null, args]);
    }

    private sealed class SourceFactory : IHeartRateSourceFactory
    {
        public SourceFactory(bool supportsBle) => SupportsBle = supportsBle;

        public FakeSourceFactory Inner { get; } = new();

        public Action<FakeHeartRateSource>? Configure
        {
            get => Inner.Configure;
            set => Inner.Configure = value;
        }

        public bool SupportsBle { get; }

        public IHeartRateSource Create(AppSettings settings) => Inner.Create(settings);
    }

    private sealed class FakeTray : IDisposable
    {
        public int DisposeCalls { get; private set; }

        public void Dispose() => DisposeCalls++;
    }

    private sealed class ShutdownRecorder
    {
        public int Calls { get; set; }
    }
}
