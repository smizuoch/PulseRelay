using Avalonia.Controls;
using Avalonia.Interactivity;
using PulseRelay.Desktop.ViewModels;

namespace PulseRelay.Desktop.Views;

public partial class DashboardView : UserControl
{
    private DiagnosticsWindow? _diagnostics;

    public DashboardView()
    {
        InitializeComponent();
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DashboardViewModel viewModel
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var window = new SettingsWindow { DataContext = viewModel.CreateSettingsViewModel() };
        window.ShowDialog(owner);
    }

    private void OnDiagnosticsClick(object? sender, RoutedEventArgs e)
    {
        if (_diagnostics is not null)
        {
            _diagnostics.Activate();
            return;
        }

        if (DataContext is not DashboardViewModel viewModel
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        // Modeless and single-instance: logs can be watched while using the dashboard.
        var window = new DiagnosticsWindow { DataContext = viewModel.CreateDiagnosticsViewModel() };
        window.Closed += (_, _) => _diagnostics = null;
        _diagnostics = window;
        window.Show(owner);
    }
}
