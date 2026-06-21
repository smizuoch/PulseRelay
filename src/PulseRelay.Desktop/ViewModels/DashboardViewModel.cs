using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulseRelay.App;
using PulseRelay.App.Localization;
using PulseRelay.App.Logging;
using PulseRelay.App.Settings;
using PulseRelay.Core.HeartRate;

namespace PulseRelay.Desktop.ViewModels;

/// <summary>One entry of the language picker; the label re-localizes itself.</summary>
public sealed class LanguageOption : ObservableObject
{
    public LanguageOption(AppLanguage value) => Value = value;

    public AppLanguage Value { get; }

    public string Label => Value switch
    {
        AppLanguage.English => LocalizationManager.GetString("Lang_English"),
        AppLanguage.Japanese => LocalizationManager.GetString("Lang_Japanese"),
        _ => LocalizationManager.GetString("Lang_System"),
    };

    public void RefreshLabel() => OnPropertyChanged(nameof(Label));
}

/// <summary>
/// Main-screen view model. Mirrors the latest <see cref="SupervisorSnapshot"/> onto bindable
/// properties; a 1-second ticker refreshes the freshness line and stale detection. All
/// user-visible strings come from the localization resources and recompute on language change.
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
    private readonly SettingsStore _settingsStore;
    private readonly RingBufferLogSink _logSink;
    private readonly DispatcherTimer _ticker;
    private bool _disposed;

    [ObservableProperty]
    private string _bpmText = "—";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _lastUpdateText = "";

    [ObservableProperty]
    private string _deviceLine = "";

    [ObservableProperty]
    private string _deviceStateText = "";

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
    private bool _showConnectHint = true;

    [ObservableProperty]
    private bool _oscOn;

    [ObservableProperty]
    private string _oscStateText = "";

    [ObservableProperty]
    private string _oscTargetText = "";

    [ObservableProperty]
    private string _oscAddressText = "";

    [ObservableProperty]
    private IBrush _oscStatusBrush = IdleBrush;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    public DashboardViewModel(
        BridgeSupervisor supervisor,
        AppSettings settings,
        SettingsStore settingsStore,
        RingBufferLogSink logSink)
    {
        _supervisor = supervisor;
        _settings = settings;
        _settingsStore = settingsStore;
        _logSink = logSink;
        _supervisor.SnapshotChanged += OnSnapshotChanged;
        LocalizationManager.LanguageChanged += OnLanguageChanged;

        LanguageOptions =
        [
            new LanguageOption(AppLanguage.System),
            new LanguageOption(AppLanguage.English),
            new LanguageOption(AppLanguage.Japanese),
        ];
        _selectedLanguage = LanguageOptions.First(o => o.Value == settings.Language);

        _ticker = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh());
        _ticker.Start();

        Refresh();
    }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    public SettingsViewModel CreateSettingsViewModel() => new(_supervisor, _settings, _settingsStore);

    public DiagnosticsViewModel CreateDiagnosticsViewModel() => new(_supervisor, _settings, _logSink);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ticker.Stop();
        _supervisor.SnapshotChanged -= OnSnapshotChanged;
        LocalizationManager.LanguageChanged -= OnLanguageChanged;
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
        var snapshot = _supervisor.Snapshot;
        bool enable = snapshot.RunState == BridgeRunState.Stopped
            ? !_settings.OscEnabled
            : snapshot.Session.OscStatus == OscOutputStatus.Off;
        if (_supervisor.TrySetOscEnabled(enable, _settings, out _))
        {
            bool previous = _settings.OscEnabled;
            _settings.OscEnabled = enable;
            if (!_settingsStore.TrySave(_settings, out _))
            {
                _settings.OscEnabled = previous;
                _supervisor.TrySetOscEnabled(previous, _settings, out _);
            }
        }

        Refresh();
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (_settings.Language == value.Value)
        {
            return;
        }

        var previous = _settings.Language;
        _settings.Language = value.Value;
        if (!_settingsStore.TrySave(_settings, out _))
        {
            _settings.Language = previous;
            SelectedLanguage = LanguageOptions.First(o => o.Value == previous);
            return;
        }

        LocalizationManager.Apply(value.Value);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var option in LanguageOptions)
        {
            option.RefreshLabel();
        }

        // The settings dialog can change the language too; keep the footer picker in
        // sync. Loop-safe: OnSelectedLanguageChanged early-returns on an equal value.
        SelectedLanguage = LanguageOptions.First(o => o.Value == _settings.Language);
        Refresh();
    }

    private void OnSnapshotChanged(object? sender, SupervisorSnapshot snapshot) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                Refresh();
            }
        });

    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

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
            ? LocalizationManager.Format("Dashboard_UpdatedAgo", Math.Max(0, (int)(now - last).TotalSeconds))
            : "";

        DeviceLine = BridgeStatusCopy.DeviceLine(session.SourceDescription, _settings.DeviceNameFilter);
        DeviceStateText = BridgeStatusCopy.DeviceState(status);
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
            SensorContactStatus.Contact => LocalizationManager.GetString("Device_ContactDetected"),
            SensorContactStatus.NoContact => LocalizationManager.GetString("Device_ContactNotDetected"),
            _ => "",
        };

        ShowStart = snapshot.RunState == BridgeRunState.Stopped;
        ShowReconnect = status == BridgeStatus.Reconnecting;
        ShowConnectHint = ShowStart && _supervisor.SupportsBle;

        // While stopped the session snapshot always reports Off; show the persisted
        // setting instead so a fresh install reads "OSC on" before the first Start.
        var oscStatus = ShowStart
            ? (_settings.OscEnabled ? OscOutputStatus.On : OscOutputStatus.Off)
            : session.OscStatus;
        OscOn = oscStatus != OscOutputStatus.Off;
        OscStateText = oscStatus switch
        {
            OscOutputStatus.On => LocalizationManager.GetString("Output_OscOn"),
            OscOutputStatus.Error => LocalizationManager.GetString("Output_OscError"),
            _ => LocalizationManager.GetString("Output_OscOff"),
        };
        OscStatusBrush = oscStatus switch
        {
            OscOutputStatus.On => AccentBrush,
            OscOutputStatus.Error => WarnBrush,
            _ => IdleBrush,
        };
        OscTargetText = $"{_settings.OscHost}:{_settings.OscPort}";
        OscAddressText = _settings.OscAddress;
    }
}
