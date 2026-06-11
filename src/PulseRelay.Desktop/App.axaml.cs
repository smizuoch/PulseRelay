using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;
using PulseRelay.App;
using PulseRelay.App.Logging;
using PulseRelay.App.Settings;
using PulseRelay.Desktop.Services;
using PulseRelay.Desktop.ViewModels;
using PulseRelay.Desktop.Views;

namespace PulseRelay.Desktop;

public class App : Application
{
    private BridgeSession? _session;
    private ILoggerFactory? _loggerFactory;

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

            var sourceFactory = SourceFactoryProvider.Create(_loggerFactory);
            if (!sourceFactory.SupportsBle && settings.SourceKind == HeartRateSourceKind.Ble)
            {
                // Session-only fallback so macOS development runs out of the box; not persisted.
                settings.SourceKind = HeartRateSourceKind.Mock;
            }

            _session = new BridgeSession(sourceFactory, _loggerFactory);

            var mainViewModel = new MainWindowViewModel(_session, settings);
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };
            desktop.Exit += OnExit;

            if (settings.AutoConnectOnLaunch)
            {
                _ = mainViewModel.Dashboard.ConnectCommand.ExecuteAsync(null);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _session?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _loggerFactory?.Dispose();
    }
}
