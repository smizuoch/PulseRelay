using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using PulseRelay.App;
using PulseRelay.App.Localization;
using PulseRelay.App.Logging;
using PulseRelay.App.Settings;
using PulseRelay.Desktop.ViewModels;
using PulseRelay.Desktop.Views;

namespace PulseRelay.Desktop.Services;

/// <summary>Owns the desktop app lifetime and keeps App.axaml.cs as thin framework glue.</summary>
public sealed class DesktopAppRuntime
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly Func<RingBufferLogSink> _createLogSink;
    private readonly Func<RingBufferLogSink, ILoggerFactory> _createLoggerFactory;
    private readonly Func<ILoggerFactory, SettingsStore> _createSettingsStore;
    private readonly Func<ILoggerFactory, IHeartRateSourceFactory> _createSourceFactory;
    private readonly Func<MainWindow> _createMainWindow;
    private readonly Func<
        IClassicDesktopStyleApplicationLifetime,
        BridgeSupervisor,
        AppSettings,
        SettingsStore,
        Func<Task>,
        ILoggerFactory,
        IDisposable> _createTray;
    private readonly Action<IClassicDesktopStyleApplicationLifetime> _shutdownDesktop;
    private readonly object _shutdownGate = new();

    private BridgeSupervisor? _supervisor;
    private BridgeSession? _session;
    private ILoggerFactory? _loggerFactory;
    private IDisposable? _tray;
    private MainWindow? _mainWindow;
    private MainWindowViewModel? _mainViewModel;
    private Task? _shutdownTask;

    public DesktopAppRuntime(
        IClassicDesktopStyleApplicationLifetime desktop,
        Func<RingBufferLogSink>? createLogSink = null,
        Func<RingBufferLogSink, ILoggerFactory>? createLoggerFactory = null,
        Func<ILoggerFactory, SettingsStore>? createSettingsStore = null,
        Func<ILoggerFactory, IHeartRateSourceFactory>? createSourceFactory = null,
        Func<MainWindow>? createMainWindow = null,
        Func<
            IClassicDesktopStyleApplicationLifetime,
            BridgeSupervisor,
            AppSettings,
            SettingsStore,
            Func<Task>,
            ILoggerFactory,
            IDisposable>? createTray = null,
        Action<IClassicDesktopStyleApplicationLifetime>? shutdownDesktop = null)
    {
        _desktop = desktop;
        _createLogSink = createLogSink ?? (() => new RingBufferLogSink());
        _createLoggerFactory = createLoggerFactory ?? (logSink => LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Debug)
            .AddProvider(logSink)));
        _createSettingsStore = createSettingsStore
            ?? (loggerFactory => new SettingsStore(logger: loggerFactory.CreateLogger<SettingsStore>()));
        _createSourceFactory = createSourceFactory ?? SourceFactoryProvider.Create;
        _createMainWindow = createMainWindow ?? (() => new MainWindow());
        _createTray = createTray ?? ((desktopLifetime, supervisor, settings, settingsStore, requestShutdown, loggerFactory) =>
            new TrayIconController(
                desktopLifetime,
                supervisor,
                settings,
                settingsStore,
                requestShutdown,
                loggerFactory.CreateLogger<TrayIconController>()));
        _shutdownDesktop = shutdownDesktop ?? (desktopLifetime => desktopLifetime.Shutdown());
    }

    public BridgeSupervisor? Supervisor => _supervisor;

    public AppSettings? Settings { get; private set; }

    public MainWindow? MainWindow => _mainWindow;

    public void Start()
    {
        _desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var logSink = _createLogSink();
        _loggerFactory = _createLoggerFactory(logSink);

        var settingsStore = _createSettingsStore(_loggerFactory);
        var settings = settingsStore.Load();
        Settings = settings;
        Avalonia.Application.Current!.RequestedThemeVariant = settings.Theme == AppTheme.Light
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
        LocalizationManager.Apply(settings.Language);

        var sourceFactory = _createSourceFactory(_loggerFactory);
        if (!sourceFactory.SupportsBle && settings.SourceKind == HeartRateSourceKind.Ble)
        {
            // Session-only fallback so macOS development runs out of the box; not persisted.
            settings.SourceKind = HeartRateSourceKind.Mock;
        }

        _session = new BridgeSession(sourceFactory, _loggerFactory);
        _supervisor = new BridgeSupervisor(
            _session,
            logger: _loggerFactory.CreateLogger<BridgeSupervisor>());

        _mainViewModel = new MainWindowViewModel(_supervisor, settings, settingsStore, logSink);
        _mainWindow = _createMainWindow();
        _mainWindow.DataContext = _mainViewModel;
        _mainWindow.ConfigureCloseBehavior(
            () => settings.HideToTrayOnClose,
            RequestShutdownAsync);
        _desktop.MainWindow = _mainWindow;
        _tray = _createTray(
            _desktop,
            _supervisor,
            settings,
            settingsStore,
            RequestShutdownAsync,
            _loggerFactory);
        _desktop.ShutdownRequested += OnShutdownRequested;

        if (settings.AutoConnectOnLaunch)
        {
            _supervisor.Start(settings);
        }
    }

    public Task RequestShutdownAsync()
    {
        lock (_shutdownGate)
        {
            return _shutdownTask ??= ShutdownCoreAsync();
        }
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        e.Cancel = true;
        _ = RequestShutdownAsync();
    }

    private async Task ShutdownCoreAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _mainWindow?.Hide();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(() => _mainWindow?.Hide());
        }

        _mainViewModel?.Dispose();
        _tray?.Dispose();

        bool supervisorStopped = true;
        if (_supervisor is not null)
        {
            try
            {
                await _supervisor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (Exception ex)
            {
                supervisorStopped = false;
                _loggerFactory?.CreateLogger<DesktopAppRuntime>().LogError(ex, "Supervisor shutdown failed");
            }
        }

        if (supervisorStopped && _session is not null)
        {
            try
            {
                await _session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _loggerFactory?.CreateLogger<DesktopAppRuntime>().LogError(ex, "Session shutdown failed");
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _desktop.ShutdownRequested -= OnShutdownRequested;
            _mainWindow?.AllowShutdownClose();
            _shutdownDesktop(_desktop);
        });
        _loggerFactory?.Dispose();
    }
}
