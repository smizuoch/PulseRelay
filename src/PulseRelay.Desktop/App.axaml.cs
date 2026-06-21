using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using PulseRelay.App;
using PulseRelay.App.Localization;
using PulseRelay.App.Logging;
using PulseRelay.App.Settings;
using PulseRelay.Desktop.Services;
using PulseRelay.Desktop.ViewModels;
using PulseRelay.Desktop.Views;

namespace PulseRelay.Desktop;

public class App : Application
{
    private BridgeSupervisor? _supervisor;
    private BridgeSession? _session;
    private ILoggerFactory? _loggerFactory;
    private TrayIconController? _tray;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _mainWindow;
    private MainWindowViewModel? _mainViewModel;
    private readonly object _shutdownGate = new();
    private Task? _shutdownTask;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            var logSink = new RingBufferLogSink();
            _loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Debug)
                .AddProvider(logSink));

            var settingsStore = new SettingsStore(logger: _loggerFactory.CreateLogger<SettingsStore>());
            var settings = settingsStore.Load();
            RequestedThemeVariant = settings.Theme == AppTheme.Light
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
            LocalizationManager.Apply(settings.Language);

            var sourceFactory = SourceFactoryProvider.Create(_loggerFactory);
            if (!sourceFactory.SupportsBle && settings.SourceKind == HeartRateSourceKind.Ble)
            {
                // Session-only fallback so macOS development runs out of the box; not persisted.
                settings.SourceKind = HeartRateSourceKind.Mock;
            }

            _session = new BridgeSession(sourceFactory, _loggerFactory);
            _supervisor = new BridgeSupervisor(
                _session, logger: _loggerFactory.CreateLogger<BridgeSupervisor>());

            _mainViewModel = new MainWindowViewModel(_supervisor, settings, settingsStore, logSink);
            _mainWindow = new MainWindow
            {
                DataContext = _mainViewModel,
            };
            _mainWindow.ConfigureCloseBehavior(
                () => settings.HideToTrayOnClose,
                RequestShutdownAsync);
            desktop.MainWindow = _mainWindow;
            _tray = new TrayIconController(
                desktop,
                _supervisor,
                settings,
                settingsStore,
                RequestShutdownAsync,
                _loggerFactory.CreateLogger<TrayIconController>());
            desktop.ShutdownRequested += OnShutdownRequested;

            if (settings.AutoConnectOnLaunch)
            {
                _supervisor.Start(settings);
            }
        }

        base.OnFrameworkInitializationCompleted();
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
                _loggerFactory?.CreateLogger<App>().LogError(ex, "Supervisor shutdown failed");
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
                _loggerFactory?.CreateLogger<App>().LogError(ex, "Session shutdown failed");
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_desktop is null)
            {
                return;
            }

            _desktop.ShutdownRequested -= OnShutdownRequested;
            _mainWindow?.AllowShutdownClose();
            _desktop.Shutdown();
        });
        _loggerFactory?.Dispose();
    }
}
