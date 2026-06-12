using System.Globalization;

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
        !string.IsNullOrWhiteSpace(address) && address.StartsWith('/');

    public static bool IsValidHost(string? host) => !string.IsNullOrWhiteSpace(host);
}
