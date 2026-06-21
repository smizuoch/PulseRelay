using CommunityToolkit.Mvvm.ComponentModel;
using PulseRelay.App;
using PulseRelay.App.Logging;
using PulseRelay.App.Settings;

namespace PulseRelay.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    public MainWindowViewModel(
        BridgeSupervisor supervisor,
        AppSettings settings,
        SettingsStore settingsStore,
        RingBufferLogSink logSink)
    {
        Dashboard = new DashboardViewModel(supervisor, settings, settingsStore, logSink);
    }

    public DashboardViewModel Dashboard { get; }

    public void Dispose() => Dashboard.Dispose();
}
