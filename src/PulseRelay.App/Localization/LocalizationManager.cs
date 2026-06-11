using System.Globalization;
using System.Resources;
using PulseRelay.App.Settings;

namespace PulseRelay.App.Localization;

/// <summary>
/// String-keyed access to the UI copy resources (neutral English fallback + Japanese), and
/// the runtime language switch. Log text never comes from here: <see cref="ILogger"/>
/// messages stay English with ASCII punctuation by policy.
/// </summary>
public static class LocalizationManager
{
    private static readonly ResourceManager Resources =
        new("PulseRelay.App.Localization.Strings", typeof(LocalizationManager).Assembly);

    /// <summary>Raised after <see cref="Apply"/> changes the UI culture, on the calling thread.</summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>
    /// Applies the chosen language to the current and all future threads and notifies
    /// listeners. <see cref="AppLanguage.System"/> restores the OS UI culture.
    /// </summary>
    public static void Apply(AppLanguage language)
    {
        var culture = ResolveCulture(language);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture ?? CultureInfo.InstalledUICulture;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Null means "follow the OS" (System).</summary>
    public static CultureInfo? ResolveCulture(AppLanguage language) => language switch
    {
        AppLanguage.English => CultureInfo.GetCultureInfo("en"),
        AppLanguage.Japanese => CultureInfo.GetCultureInfo("ja"),
        _ => null,
    };

    /// <summary>Returns the key itself when missing so the UI never crashes on a copy gap.</summary>
    public static string GetString(string key) => GetString(key, CultureInfo.CurrentUICulture);

    public static string GetString(string key, CultureInfo culture) =>
        Resources.GetString(key, culture) ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentUICulture, GetString(key), args);

    /// <summary>For the key-parity tests: the raw set for one culture, without parent fallback.</summary>
    public static ResourceSet? GetResourceSet(CultureInfo culture) =>
        Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
}
