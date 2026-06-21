using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulseRelay.App;
using PulseRelay.App.Diagnostics;
using PulseRelay.App.Localization;
using PulseRelay.App.Logging;
using PulseRelay.App.Settings;

namespace PulseRelay.Desktop.ViewModels;

/// <summary>
/// Backing model for the Diagnostics window: live bridge state plus the recent log lines
/// from <see cref="RingBufferLogSink"/>. Labels are localized; log lines stay raw English
/// (log policy). Owns event subscriptions — the window disposes it on close.
/// </summary>
public sealed partial class DiagnosticsViewModel : ObservableObject, IDisposable
{
    private const int MaxVisibleLines = 500;

    private readonly BridgeSupervisor _supervisor;
    private readonly AppSettings _settings;
    private readonly RingBufferLogSink _logSink;
    private bool _disposed;

    [ObservableProperty]
    private string _stateText = "";

    [ObservableProperty]
    private string _sourceText = "";

    [ObservableProperty]
    private string _bpmText = "—";

    [ObservableProperty]
    private string _oscText = "";

    [ObservableProperty]
    private bool _copiedVisible;

    public DiagnosticsViewModel(BridgeSupervisor supervisor, AppSettings settings, RingBufferLogSink logSink)
    {
        _supervisor = supervisor;
        _settings = settings;
        _logSink = logSink;

        foreach (var entry in logSink.GetSnapshot())
        {
            Append(entry);
        }

        _supervisor.SnapshotChanged += OnSnapshotChanged;
        _logSink.EntryAdded += OnEntryAdded;
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        Refresh();
    }

    public ObservableCollection<string> LogLines { get; } = [];

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _supervisor.SnapshotChanged -= OnSnapshotChanged;
        _logSink.EntryAdded -= OnEntryAdded;
        LocalizationManager.LanguageChanged -= OnLanguageChanged;
    }

    /// <summary>Full report for the clipboard; MAC-shaped addresses are always redacted.</summary>
    public string BuildReport() =>
        DiagnosticsFormatter.BuildReport(_supervisor.Snapshot, _logSink.GetSnapshot(), DateTimeOffset.UtcNow);

    public void ShowCopiedFeedback()
    {
        CopiedVisible = true;
        DispatcherTimer.RunOnce(() => CopiedVisible = false, TimeSpan.FromSeconds(2));
    }

    [RelayCommand]
    private void ClearLog()
    {
        _logSink.Clear();
        LogLines.Clear();
    }

    // Both events fire on logging/BLE/timer threads; marshal before touching properties
    // or the ObservableCollection.
    private void OnSnapshotChanged(object? sender, SupervisorSnapshot snapshot) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                Refresh();
            }
        });

    private void OnEntryAdded(object? sender, LogEntry entry) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                Append(entry);
            }
        });

    private void OnLanguageChanged(object? sender, EventArgs e) => Refresh();

    private void Append(LogEntry entry)
    {
        LogLines.Add(DiagnosticsFormatter.FormatLine(entry));
        while (LogLines.Count > MaxVisibleLines)
        {
            LogLines.RemoveAt(0);
        }
    }

    private void Refresh()
    {
        var snapshot = _supervisor.Snapshot;
        var session = snapshot.Session;

        StateText = BridgeStatusCopy.Headline(snapshot, DateTimeOffset.UtcNow, _supervisor.StaleThreshold);
        SourceText = BridgeStatusCopy.DeviceLine(session.SourceDescription, _settings.DeviceNameFilter);
        BpmText = session.Bpm?.ToString() ?? "—";

        string oscState = session.OscStatus switch
        {
            OscOutputStatus.On => LocalizationManager.GetString("Output_OscOn"),
            OscOutputStatus.Error => LocalizationManager.GetString("Output_OscError"),
            _ => LocalizationManager.GetString("Output_OscOff"),
        };
        OscText = $"{oscState} — "
            + LocalizationManager.Format("Diag_OscCounts", session.OscSentCount, session.OscErrorCount);
    }
}
