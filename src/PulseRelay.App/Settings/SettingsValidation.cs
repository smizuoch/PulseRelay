using System.Globalization;
using PulseRelay.Osc;

namespace PulseRelay.App.Settings;

/// <summary>
/// Input validation for the settings dialog. Lives here (not in the UI project) so the
/// rules are unit-testable and shared with any future entry point.
/// </summary>
public static class SettingsValidation
{
    public const int MinPort = 1;
    public const int MaxPort = 65535;

    /// <summary>Strict digits-only parse: rejects signs, whitespace, and non-ASCII digits.</summary>
    public static bool TryParsePort(string? text, out int port) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port)
        && port is >= MinPort and <= MaxPort;

    public static bool IsValidOscAddress(string? address) =>
        OscWriter.IsValidAddress(address);

    public static bool IsValidHost(string? host) => !string.IsNullOrWhiteSpace(host);

    public static AppSettings Normalize(AppSettings settings)
    {
        var defaults = new AppSettings();
        if (!Enum.IsDefined(settings.SourceKind))
        {
            settings.SourceKind = defaults.SourceKind;
        }

        if (settings.ScanTimeoutSeconds is <= 0 or > 3600)
        {
            settings.ScanTimeoutSeconds = defaults.ScanTimeoutSeconds;
        }

        settings.OscHost = IsValidHost(settings.OscHost)
            ? settings.OscHost.Trim()
            : defaults.OscHost;
        if (settings.OscPort is < MinPort or > MaxPort)
        {
            settings.OscPort = defaults.OscPort;
        }

        string? oscAddress = settings.OscAddress?.Trim();
        settings.OscAddress = IsValidOscAddress(oscAddress)
            ? oscAddress!
            : defaults.OscAddress;
        if (!Enum.IsDefined(settings.Theme))
        {
            settings.Theme = defaults.Theme;
        }

        if (!Enum.IsDefined(settings.Language))
        {
            settings.Language = defaults.Language;
        }

        return settings;
    }
}
