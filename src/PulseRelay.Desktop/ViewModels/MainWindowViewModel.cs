using CommunityToolkit.Mvvm.ComponentModel;
using PulseRelay.App;
using PulseRelay.App.Settings;

namespace PulseRelay.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(BridgeSupervisor supervisor, AppSettings settings)
    {
        Dashboard = new DashboardViewModel(supervisor, settings);
    }

    public DashboardViewModel Dashboard { get; }
}
