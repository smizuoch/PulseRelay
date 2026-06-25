using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRelay.App;
using PulseRelay.App.Logging;
using PulseRelay.App.Settings;
using PulseRelay.Desktop.ViewModels;
using PulseRelay.Desktop.Views;
using Xunit;

namespace PulseRelay.Tests;

public class DesktopViewSmokeTests : IDisposable
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
    public void Constructs_all_primary_views()
    {
        _ = new MainWindow();
        _ = new DashboardView();
        _ = new SettingsWindow();
        _ = new DiagnosticsWindow();
        _ = new ThirdPartyNoticesWindow();
    }

    [AvaloniaFact]
    public void Main_window_view_model_owns_dashboard_view_model()
    {
        var session = new BridgeSession(new FakeSourceFactory(), NullLoggerFactory.Instance);
        var supervisor = new BridgeSupervisor(session);
        var settings = new AppSettings { SourceKind = HeartRateSourceKind.Mock };
        var store = new SettingsStore(_directory);
        var sink = new RingBufferLogSink();
        var viewModel = new MainWindowViewModel(supervisor, settings, store, sink);

        Assert.NotNull(viewModel.Dashboard);

        viewModel.Dispose();
        supervisor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        sink.Dispose();
    }

    [AvaloniaFact]
    public void Main_window_user_close_hides_when_configured_for_tray()
    {
        var window = new MainWindow();
        bool shutdownRequested = false;
        window.ConfigureCloseBehavior(() => true, () =>
        {
            shutdownRequested = true;
            return Task.CompletedTask;
        });

        window.Show();
        window.Close();

        Assert.False(window.IsVisible);
        Assert.False(shutdownRequested);
    }

    [AvaloniaFact]
    public void Main_window_user_close_requests_shutdown_when_tray_hide_is_disabled()
    {
        var window = new MainWindow();
        bool shutdownRequested = false;
        window.ConfigureCloseBehavior(() => false, () =>
        {
            shutdownRequested = true;
            return Task.CompletedTask;
        });

        window.Show();
        window.Close();

        Assert.False(window.IsVisible);
        Assert.True(shutdownRequested);
    }

    [AvaloniaFact]
    public void Main_window_allow_shutdown_close_does_not_request_shutdown_again()
    {
        var window = new MainWindow();
        bool shutdownRequested = false;
        window.ConfigureCloseBehavior(() => false, () =>
        {
            shutdownRequested = true;
            return Task.CompletedTask;
        });

        window.AllowShutdownClose();
        window.Show();
        window.Close();

        Assert.False(window.IsVisible);
        Assert.False(shutdownRequested);
    }

    [AvaloniaFact]
    public void Dashboard_click_handlers_return_without_owner_or_view_model()
    {
        var view = new DashboardView();
        InvokePrivateHandler(view, "OnSettingsClick");
        InvokePrivateHandler(view, "OnDiagnosticsClick");
        InvokePrivateHandler(view, "OnLicensesClick");
    }

    [AvaloniaFact]
    public void Dashboard_diagnostics_click_opens_single_modeless_window()
    {
        var owner = new Window();
        var view = new DashboardView();
        owner.Content = view;
        var session = new BridgeSession(new FakeSourceFactory(), NullLoggerFactory.Instance);
        var supervisor = new BridgeSupervisor(session);
        var settings = new AppSettings { SourceKind = HeartRateSourceKind.Mock };
        var sink = new RingBufferLogSink();
        var viewModel = new DashboardViewModel(
            supervisor,
            settings,
            new SettingsStore(_directory),
            sink);
        view.DataContext = viewModel;

        owner.Show();
        InvokePrivateHandler(view, "OnDiagnosticsClick");
        var first = GetDiagnosticsWindow(view);
        Assert.NotNull(first);
        InvokePrivateHandler(view, "OnDiagnosticsClick");

        Assert.Same(first, GetDiagnosticsWindow(view));

        first!.Close();
        owner.Close();
        viewModel.Dispose();
        supervisor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        sink.Dispose();
    }

    [AvaloniaFact]
    public void Dashboard_settings_click_opens_settings_dialog()
    {
        var owner = new Window();
        var view = new DashboardView();
        owner.Content = view;
        var session = new BridgeSession(new FakeSourceFactory(), NullLoggerFactory.Instance);
        var supervisor = new BridgeSupervisor(session);
        var settings = new AppSettings { SourceKind = HeartRateSourceKind.Mock };
        var sink = new RingBufferLogSink();
        var viewModel = new DashboardViewModel(
            supervisor,
            settings,
            new SettingsStore(_directory),
            sink);
        view.DataContext = viewModel;

        owner.Show();
        InvokePrivateHandler(view, "OnSettingsClick");
        var dialog = Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>());

        dialog.Close();
        owner.Close();
        viewModel.Dispose();
        supervisor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        sink.Dispose();
    }

    [AvaloniaFact]
    public void Dashboard_licenses_click_opens_notices_dialog()
    {
        var owner = new Window();
        var view = new DashboardView();
        owner.Content = view;

        owner.Show();
        InvokePrivateHandler(view, "OnLicensesClick");
        var dialog = Assert.Single(owner.OwnedWindows.OfType<ThirdPartyNoticesWindow>());

        dialog.Close();
        owner.Close();
    }

    [AvaloniaFact]
    public void Diagnostics_copy_click_returns_without_view_model_or_clipboard()
    {
        var window = new DiagnosticsWindow();
        InvokePrivateHandler(window, "OnCopyClick");

        var session = new BridgeSession(new FakeSourceFactory(), NullLoggerFactory.Instance);
        var supervisor = new BridgeSupervisor(session);
        var settings = new AppSettings { SourceKind = HeartRateSourceKind.Mock };
        var sink = new RingBufferLogSink();
        window.DataContext = new DiagnosticsViewModel(supervisor, settings, sink);

        InvokePrivateHandler(window, "OnCopyClick");

        ((IDisposable)window.DataContext).Dispose();
        supervisor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        sink.Dispose();
    }

    [AvaloniaFact]
    public void Settings_window_subscribes_to_view_model_close_request()
    {
        var window = new SettingsWindow();
        var session = new BridgeSession(new FakeSourceFactory(), NullLoggerFactory.Instance);
        var supervisor = new BridgeSupervisor(session);
        var settings = new AppSettings { SourceKind = HeartRateSourceKind.Mock };
        var viewModel = new SettingsViewModel(supervisor, settings, new SettingsStore(_directory));

        window.DataContext = viewModel;

        window.Show();
        viewModel.CancelCommand.Execute(null);

        Assert.False(window.IsVisible);
        supervisor.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static void InvokePrivateHandler(object target, string methodName)
    {
        var method = target.GetType().GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(target, [null, new RoutedEventArgs()]);
    }

    private static DiagnosticsWindow? GetDiagnosticsWindow(DashboardView view)
    {
        var field = typeof(DashboardView).GetField(
            "_diagnostics",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field.GetValue(view) as DiagnosticsWindow;
    }
}
