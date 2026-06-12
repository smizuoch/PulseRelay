using System.ComponentModel;
using PulseRelay.App.Localization;

namespace PulseRelay.Desktop.Localization;

/// <summary>
/// Bindable indexer over the localization resources. When the language changes it raises
/// a change for the indexer, so every <c>{loc:Loc Key}</c> binding refreshes live without
/// a restart.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private Loc()
    {
        // Avalonia listens for "Item" (its CommonPropertyNames.IndexerName), not WPF's
        // "Item[]" - raising the wrong name silently never refreshes any binding.
        LocalizationManager.LanguageChanged += (_, _) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => LocalizationManager.GetString(key);
}
