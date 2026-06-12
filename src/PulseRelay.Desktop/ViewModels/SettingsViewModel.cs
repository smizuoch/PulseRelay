using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulseRelay.App;
using PulseRelay.App.Localization;
using PulseRelay.App.Settings;

namespace PulseRelay.Desktop.ViewModels;

/// <summary>
/// Edit model for the settings dialog. Works on copies and writes back into the shared
/// <see cref="AppSettings"/> instance only on a successful Save — the supervisor reads
/// that same instance on every connect attempt, so it must never be replaced.
/// Dispatcher-free by design so it stays unit-testable.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly BridgeSupervisor _supervisor;
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private bool _oscEnabled;

    [ObservableProperty]
    private string _oscHost;

    [ObservableProperty]
    private string _oscPortText;

    [ObservableProperty]
    private string _oscAddress;

    [ObservableProperty]
    private bool _isBleSource;

    [ObservableProperty]
    private string _deviceNameFilter;

    [ObservableProperty]
    private string? _errorText;

    public SettingsViewModel(BridgeSupervisor supervisor, AppSettings settings, SettingsStore store)
    {
        _supervisor = supervisor;
        _settings = settings;
        _store = store;

        LanguageOptions =
        [
            new LanguageOption(AppLanguage.System),
            new LanguageOption(AppLanguage.English),
            new LanguageOption(AppLanguage.Japanese),
        ];
        _selectedLanguage = LanguageOptions.First(o => o.Value == settings.Language);
        _oscEnabled = settings.OscEnabled;
        _oscHost = settings.OscHost;
        _oscPortText = settings.OscPort.ToString();
        _oscAddress = settings.OscAddress;
        _isBleSource = settings.SourceKind == HeartRateSourceKind.Ble;
        _deviceNameFilter = settings.DeviceNameFilter ?? "";
    }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    /// <summary>Raised when the dialog should close; the argument is true when saved.</summary>
    public event EventHandler<bool>? CloseRequested;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    [RelayCommand]
    private void Save()
    {
        if (!SettingsValidation.IsValidHost(OscHost))
        {
            ErrorText = LocalizationManager.GetString("Settings_Error_Host");
            return;
        }

        if (!SettingsValidation.TryParsePort(OscPortText, out int port))
        {
            ErrorText = LocalizationManager.GetString("Settings_Error_Port");
            return;
        }

        string address = OscAddress.Trim();
        if (!SettingsValidation.IsValidOscAddress(address))
        {
            ErrorText = LocalizationManager.GetString("Settings_Error_Address");
            return;
        }

        string host = OscHost.Trim();
        bool oscChanged = _settings.OscEnabled != OscEnabled
            || _settings.OscHost != host
            || _settings.OscPort != port
            || _settings.OscAddress != address;
        bool languageChanged = _settings.Language != SelectedLanguage.Value;

        var previous = (_settings.OscEnabled, _settings.OscHost, _settings.OscPort, _settings.OscAddress);
        _settings.OscEnabled = OscEnabled;
        _settings.OscHost = host;
        _settings.OscPort = port;
        _settings.OscAddress = address;

        // Re-attach (or detach) the publisher so a running session sends to the new
        // endpoint immediately; on failure roll the shared instance back and stay open.
        if (oscChanged && !_supervisor.TrySetOscEnabled(OscEnabled, _settings, out string? error))
        {
            (_settings.OscEnabled, _settings.OscHost, _settings.OscPort, _settings.OscAddress) = previous;
            ErrorText = LocalizationManager.Format("Settings_Error_OscApply", error ?? "");
            return;
        }

        _settings.SourceKind = IsBleSource ? HeartRateSourceKind.Ble : HeartRateSourceKind.Mock;
        _settings.DeviceNameFilter =
            string.IsNullOrWhiteSpace(DeviceNameFilter) ? null : DeviceNameFilter.Trim();
        _settings.Language = SelectedLanguage.Value;

        _store.Save(_settings);
        if (languageChanged)
        {
            LocalizationManager.Apply(_settings.Language);
        }

        CloseRequested?.Invoke(this, true);
    }
}
