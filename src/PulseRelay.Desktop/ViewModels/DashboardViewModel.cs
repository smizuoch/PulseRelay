using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulseRelay.App;
using PulseRelay.App.Settings;
using PulseRelay.Core.HeartRate;

namespace PulseRelay.Desktop.ViewModels;

/// <summary>
/// Main-screen view model. Mirrors the latest <see cref="SupervisorSnapshot"/> onto bindable
/// properties; a 1-second ticker refreshes the freshness line and stale detection without
/// touching the supervisor.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#E8637E"));
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.Parse("#6B7077"));
    private static readonly IBrush WorkingBrush = new SolidColorBrush(Color.Parse("#A8AEB8"));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.Parse("#C9A227"));
    private static readonly IBrush FailBrush = new SolidColorBrush(Color.Parse("#B3565E"));

    private readonly BridgeSupervisor _supervisor;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _ticker;

    [ObservableProperty]
    private string _bpmText = "—";

    [ObservableProperty]
    private string _statusText = "Not connected";

    [ObservableProperty]
    private string _lastUpdateText = "";

    [ObservableProperty]
    private string _deviceLine = "No device";

    [ObservableProperty]
    private string _deviceStateText = "Not connected";

    [ObservableProperty]
    private string _contactText = "";

    [ObservableProperty]
    private bool _hasContactInfo;

    [ObservableProperty]
    private IBrush _deviceStatusBrush = IdleBrush;

    [ObservableProperty]
    private bool _showStart = true;

    [ObservableProperty]
    private bool _showReconnect;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private string _connectHint = "Make sure your device is sharing its heart rate first.";

    [ObservableProperty]
    private bool _showConnectHint = true;

    [ObservableProperty]
    private bool _oscOn;

    [ObservableProperty]
    private string _oscStateText = "OSC off";

    [ObservableProperty]
    private string _oscTargetText = "";

    [ObservableProperty]
    private string _oscAddressText = "";

    [ObservableProperty]
    private IBrush _oscStatusBrush = IdleBrush;

    public DashboardViewModel(BridgeSupervisor supervisor, AppSettings settings)
    {
        _supervisor = supervisor;
        _settings = settings;
        _supervisor.SnapshotChanged += OnSnapshotChanged;

        _ticker = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh());
        _ticker.Start();

        Refresh();
    }

    public void Dispose()
    {
        _ticker.Stop();
        _supervisor.SnapshotChanged -= OnSnapshotChanged;
    }

    [RelayCommand]
    private void Start()
    {
        _supervisor.Start(_settings);
        Refresh();
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _supervisor.StopAsync();
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    [RelayCommand]
    private void Reconnect()
    {
        _supervisor.RequestReconnectNow();
        Refresh();
    }

    [RelayCommand]
    private void ToggleOsc()
    {
        bool enable = _supervisor.Snapshot.Session.OscStatus == OscOutputStatus.Off;
        if (_supervisor.TrySetOscEnabled(enable, _settings, out _))
        {
            _settings.OscEnabled = enable;
        }

        Refresh();
    }

    private void OnSnapshotChanged(object? sender, SupervisorSnapshot snapshot) =>
        Dispatcher.UIThread.Post(Refresh);

    private void Refresh()
    {
        var snapshot = _supervisor.Snapshot;
        var session = snapshot.Session;
        var now = DateTimeOffset.UtcNow;
        var status = snapshot.EffectiveStatus(now, _supervisor.StaleThreshold);

        StatusText = BridgeStatusCopy.Headline(snapshot, now, _supervisor.StaleThreshold);
        BpmText = status is BridgeStatus.Streaming or BridgeStatus.Stale && session.Bpm is { } bpm
            ? bpm.ToString()
            : "—";
        IsStreaming = status == BridgeStatus.Streaming;

        LastUpdateText = session.LastSampleAt is { } last
            ? $"updated {Math.Max(0, (int)(now - last).TotalSeconds)}s ago"
            : "";

        DeviceLine = session.SourceDescription ?? "No device";
        DeviceStateText = status switch
        {
            BridgeStatus.NotConnected => "Not connected",
            BridgeStatus.Searching => "Searching",
            BridgeStatus.Connecting => "Connecting",
            BridgeStatus.WaitingForData => "Waiting for data",
            BridgeStatus.Streaming => "Streaming",
            BridgeStatus.Stale => "No recent data",
            BridgeStatus.Disconnected => "Disconnected",
            BridgeStatus.Reconnecting => "Reconnecting",
            BridgeStatus.Failed => "Failed",
            _ => status.ToString(),
        };
        DeviceStatusBrush = status switch
        {
            BridgeStatus.Streaming => AccentBrush,
            BridgeStatus.Searching or BridgeStatus.Connecting or BridgeStatus.WaitingForData => WorkingBrush,
            BridgeStatus.Stale or BridgeStatus.Reconnecting => WarnBrush,
            BridgeStatus.Failed or BridgeStatus.Disconnected => FailBrush,
            _ => IdleBrush,
        };

        HasContactInfo = session.SensorContact is SensorContactStatus.Contact or SensorContactStatus.NoContact;
        ContactText = session.SensorContact switch
        {
            SensorContactStatus.Contact => "Skin contact: detected",
            SensorContactStatus.NoContact => "Skin contact: not detected",
            _ => "",
        };

        ShowStart = snapshot.RunState == BridgeRunState.Stopped;
        ShowReconnect = status == BridgeStatus.Reconnecting;
        ShowConnectHint = ShowStart && _supervisor.SupportsBle;

        OscOn = session.OscStatus != OscOutputStatus.Off;
        OscStateText = session.OscStatus switch
        {
            OscOutputStatus.On => "OSC on",
            OscOutputStatus.Error => "Sending failed — check the host and port.",
            _ => "OSC off",
        };
        OscStatusBrush = session.OscStatus switch
        {
            OscOutputStatus.On => AccentBrush,
            OscOutputStatus.Error => WarnBrush,
            _ => IdleBrush,
        };
        OscTargetText = $"{_settings.OscHost}:{_settings.OscPort}";
        OscAddressText = _settings.OscAddress;
    }
}
