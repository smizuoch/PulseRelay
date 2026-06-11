using CommunityToolkit.Mvvm.ComponentModel;
using PulseRelay.App;
using PulseRelay.App.Settings;

namespace PulseRelay.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(BridgeSession session, AppSettings settings)
    {
        Dashboard = new DashboardViewModel(session, settings);
    }

    public DashboardViewModel Dashboard { get; }
}
