using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var logSink = new RingBufferLogSink();
            _loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(LogLevel.Debug)
                .AddProvider(logSink));

            var settingsStore = new SettingsStore(logger: _loggerFactory.CreateLogger<SettingsStore>());
            var settings = settingsStore.Load();
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

            var mainViewModel = new MainWindowViewModel(_supervisor, settings, settingsStore, logSink);
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };
            _tray = new TrayIconController(desktop, _supervisor, settings, settingsStore);
            desktop.Exit += OnExit;

            if (settings.AutoConnectOnLaunch)
            {
                _supervisor.Start(settings);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _tray?.Dispose();
        _supervisor?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _session?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _loggerFactory?.Dispose();
    }
}
