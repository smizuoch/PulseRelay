using System.Collections;
using System.Globalization;
using PulseRelay.App;
using PulseRelay.App.Localization;
using PulseRelay.App.Settings;
using Xunit;

namespace PulseRelay.Tests;

public class LocalizationTests
{
    private static readonly string[] RequiredKeys =
    [
        "Status_NotConnected",
        "Status_Searching",
        "Status_Connecting",
        "Status_WaitingForData",
        "Status_Streaming",
        "Status_Stale",
        "Status_Disconnected",
        "Status_Reconnecting",
        "Status_ReconnectingInitial",
        "Status_Failed",
        "Error_DeviceNotFound",
        "Error_BluetoothUnavailable",
        "Error_PlatformUnsupported",
        "Error_OscConfig",
        "Error_DeviceDisconnected",
        "Device_CardTitle",
        "Device_State_Streaming",
        "Device_ContactDetected",
        "Device_ContactNotDetected",
        "Device_ConnectHint",
        "Output_CardTitle",
        "Output_OscOn",
        "Output_OscOff",
        "Output_OscError",
        "Output_TurnOn",
        "Output_TurnOff",
        "Action_Start",
        "Action_Stop",
        "Action_Reconnect",
        "Action_Settings",
        "Action_Diagnostics",
        "Osc_TestSent",
        "Osc_TestFailed",
        "Dashboard_UpdatedAgo",
        "Lang_Label",
        "Lang_System",
        "Lang_English",
        "Lang_Japanese",
        "Guide_Share_Title",
        "Guide_Share_Intro",
        "Guide_Share_Charge6_Title",
        "Guide_Share_Charge6_Steps",
        "Guide_Share_Charge6_Note",
    ];

    private static Dictionary<string, string> Entries(CultureInfo culture)
    {
        var set = LocalizationManager.GetResourceSet(culture);
        Assert.NotNull(set);
        return set.Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    [Fact]
    public void English_and_japanese_have_identical_key_sets()
    {
        var english = Entries(CultureInfo.InvariantCulture);
        var japanese = Entries(CultureInfo.GetCultureInfo("ja"));

        Assert.Equal(
            english.Keys.Order().ToArray(),
            japanese.Keys.Order().ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("ja")]
    public void Required_keys_exist_with_non_empty_values(string cultureName)
    {
        var entries = Entries(CultureInfo.GetCultureInfo(cultureName));

        foreach (string key in RequiredKeys)
        {
            Assert.True(entries.TryGetValue(key, out string? value), $"Missing key '{key}' in '{cultureName}'");
            Assert.False(string.IsNullOrWhiteSpace(value), $"Empty value for '{key}' in '{cultureName}'");
        }
    }

    [Fact]
    public void Format_placeholders_match_between_cultures()
    {
        var english = Entries(CultureInfo.InvariantCulture);
        var japanese = Entries(CultureInfo.GetCultureInfo("ja"));

        foreach ((string key, string value) in english)
        {
            bool enHasPlaceholder = value.Contains("{0}");
            bool jaHasPlaceholder = japanese[key].Contains("{0}");
            Assert.True(enHasPlaceholder == jaHasPlaceholder, $"Placeholder mismatch for '{key}'");
        }
    }

    [Fact]
    public void Status_copy_honors_current_culture()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = BridgeSnapshot.Initial with { Status = BridgeStatus.Streaming, LastSampleAt = now };

        string english;
        string japanese;
        using (new CultureScope("en"))
        {
            english = BridgeStatusCopy.Headline(snapshot, now, TimeSpan.FromSeconds(10));
        }

        using (new CultureScope("ja"))
        {
            japanese = BridgeStatusCopy.Headline(snapshot, now, TimeSpan.FromSeconds(10));
        }

        Assert.Equal("Receiving heart rate", english);
        Assert.Equal("心拍数を受信中", japanese);
    }

    [Fact]
    public void Stale_copy_formats_seconds_in_both_cultures()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = BridgeSnapshot.Initial with
        {
            Status = BridgeStatus.Streaming,
            LastSampleAt = now - TimeSpan.FromSeconds(15),
        };

        using (new CultureScope("en"))
        {
            Assert.Contains("15s", BridgeStatusCopy.Headline(snapshot, now, TimeSpan.FromSeconds(10)));
        }

        using (new CultureScope("ja"))
        {
            Assert.Contains("15秒", BridgeStatusCopy.Headline(snapshot, now, TimeSpan.FromSeconds(10)));
        }
    }

    [Fact]
    public void Missing_key_returns_key_instead_of_throwing()
    {
        Assert.Equal("No_Such_Key", LocalizationManager.GetString("No_Such_Key"));
    }

    [Theory]
    [InlineData(AppLanguage.System, null)]
    [InlineData(AppLanguage.English, "en")]
    [InlineData(AppLanguage.Japanese, "ja")]
    public void Resolves_language_setting_to_culture(AppLanguage language, string? expected)
    {
        var culture = LocalizationManager.ResolveCulture(language);
        Assert.Equal(expected, culture?.Name);
    }
}
