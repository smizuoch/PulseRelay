using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulseRelay.App;
using PulseRelay.App.Settings;
using PulseRelay.Core.HeartRate;

namespace PulseRelay.Desktop.ViewModels;

/// <summary>
/// Main-screen view model. Mirrors the latest <see cref="BridgeSnapshot"/> onto bindable
/// properties; a 1-second ticker refreshes the freshness line and stale detection without
/// touching the session.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#E8637E"));
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.Parse("#6B7077"));
    private static readonly IBrush WorkingBrush = new SolidColorBrush(Color.Parse("#A8AEB8"));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.Parse("#C9A227"));
    private static readonly IBrush FailBrush = new SolidColorBrush(Color.Parse("#B3565E"));

    private readonly BridgeSession _session;
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
    private bool _showConnect = true;

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

    public DashboardViewModel(BridgeSession session, AppSettings settings)
    {
        _session = session;
        _settings = settings;
        _session.SnapshotChanged += OnSnapshotChanged;

        _ticker = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh());
        _ticker.Start();

        Refresh();
    }

    public void Dispose()
    {
        _ticker.Stop();
        _session.SnapshotChanged -= OnSnapshotChanged;
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            // Disconnect first so retry after a mid-session drop or failure always works.
            await _session.DisconnectAsync();
            await _session.ConnectAsync(_settings, CancellationToken.None);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _session.DisconnectAsync();
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    [RelayCommand]
    private void ToggleOsc()
    {
        bool enable = _session.Snapshot.OscStatus == OscOutputStatus.Off;
        if (_session.TrySetOscEnabled(enable, _settings, out _))
        {
            _settings.OscEnabled = enable;
        }

        Refresh();
    }

    private void OnSnapshotChanged(object? sender, BridgeSnapshot snapshot) =>
        Dispatcher.UIThread.Post(Refresh);

    private void Refresh()
    {
        var snapshot = _session.Snapshot;
        var now = DateTimeOffset.UtcNow;
        var status = snapshot.EffectiveStatus(now, _session.StaleThreshold);

        StatusText = BridgeStatusCopy.Headline(snapshot, now, _session.StaleThreshold);
        BpmText = status is BridgeStatus.Streaming or BridgeStatus.Stale && snapshot.Bpm is { } bpm
            ? bpm.ToString()
            : "—";
        IsStreaming = status == BridgeStatus.Streaming;

        LastUpdateText = snapshot.LastSampleAt is { } last
            ? $"updated {Math.Max(0, (int)(now - last).TotalSeconds)}s ago"
            : "";

        DeviceLine = snapshot.SourceDescription ?? "No device";
        DeviceStateText = status switch
        {
            BridgeStatus.NotConnected => "Not connected",
            BridgeStatus.Searching => "Searching",
            BridgeStatus.Connecting => "Connecting",
            BridgeStatus.WaitingForData => "Waiting for data",
            BridgeStatus.Streaming => "Streaming",
            BridgeStatus.Stale => "No recent data",
            BridgeStatus.Disconnected => "Disconnected",
            BridgeStatus.Failed => "Failed",
            _ => status.ToString(),
        };
        DeviceStatusBrush = status switch
        {
            BridgeStatus.Streaming => AccentBrush,
            BridgeStatus.Searching or BridgeStatus.Connecting or BridgeStatus.WaitingForData => WorkingBrush,
            BridgeStatus.Stale => WarnBrush,
            BridgeStatus.Failed or BridgeStatus.Disconnected => FailBrush,
            _ => IdleBrush,
        };

        HasContactInfo = snapshot.SensorContact is SensorContactStatus.Contact or SensorContactStatus.NoContact;
        ContactText = snapshot.SensorContact switch
        {
            SensorContactStatus.Contact => "Skin contact: detected",
            SensorContactStatus.NoContact => "Skin contact: not detected",
            _ => "",
        };

        ShowConnect = status is BridgeStatus.NotConnected or BridgeStatus.Failed or BridgeStatus.Disconnected;
        ShowConnectHint = ShowConnect && _session.SupportsBle;

        OscOn = snapshot.OscStatus != OscOutputStatus.Off;
        OscStateText = snapshot.OscStatus switch
        {
            OscOutputStatus.On => "OSC on",
            OscOutputStatus.Error => "Sending failed — check the host and port.",
            _ => "OSC off",
        };
        OscStatusBrush = snapshot.OscStatus switch
        {
            OscOutputStatus.On => AccentBrush,
            OscOutputStatus.Error => WarnBrush,
            _ => IdleBrush,
        };
        OscTargetText = $"{_settings.OscHost}:{_settings.OscPort}";
        OscAddressText = _settings.OscAddress;
    }
}
