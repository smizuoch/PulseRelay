using PulseRelay.App.Settings;
using Xunit;

namespace PulseRelay.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PulseRelayTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_yields_defaults()
    {
        var store = new SettingsStore(_directory);

        var settings = store.Load();

        Assert.Equal(HeartRateSourceKind.Ble, settings.SourceKind);
        Assert.True(settings.OscEnabled);
        Assert.False(settings.FirstRunCompleted);
    }

    [Fact]
    public void Round_trips_all_fields()
    {
        var store = new SettingsStore(_directory);
        var original = new AppSettings
        {
            SourceKind = HeartRateSourceKind.Mock,
            DeviceNameFilter = "Charge 6",
            ScanTimeoutSeconds = 45,
            OscEnabled = false,
            OscHost = "192.168.1.50",
            OscPort = 9100,
            OscAddress = "/custom/path",
            Theme = AppTheme.Light,
            AutoConnectOnLaunch = true,
            HideToTrayOnClose = false,
            FirstRunCompleted = true,
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(HeartRateSourceKind.Mock, loaded.SourceKind);
        Assert.Equal("Charge 6", loaded.DeviceNameFilter);
        Assert.Equal(45, loaded.ScanTimeoutSeconds);
        Assert.False(loaded.OscEnabled);
        Assert.Equal("192.168.1.50", loaded.OscHost);
        Assert.Equal(9100, loaded.OscPort);
        Assert.Equal("/custom/path", loaded.OscAddress);
        Assert.Equal(AppTheme.Light, loaded.Theme);
        Assert.True(loaded.AutoConnectOnLaunch);
        Assert.False(loaded.HideToTrayOnClose);
        Assert.True(loaded.FirstRunCompleted);
    }

    [Fact]
    public void Missing_osc_enabled_field_defaults_to_enabled()
    {
        var store = new SettingsStore(_directory);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.FilePath, """{ "SourceKind": "Ble", "OscPort": 9000 }""");

        Assert.True(store.Load().OscEnabled);
    }

    [Fact]
    public void Explicit_osc_disabled_survives_load()
    {
        var store = new SettingsStore(_directory);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.FilePath, """{ "OscEnabled": false }""");

        Assert.False(store.Load().OscEnabled);
    }

    [Fact]
    public void Corrupt_file_yields_defaults()
    {
        var store = new SettingsStore(_directory);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.FilePath, "{ not valid json !!");

        var settings = store.Load();

        Assert.Equal(HeartRateSourceKind.Ble, settings.SourceKind);
    }

    [Fact]
    public void Save_after_corrupt_load_recovers()
    {
        var store = new SettingsStore(_directory);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.FilePath, "garbage");

        var settings = store.Load();
        settings.FirstRunCompleted = true;
        store.Save(settings);

        Assert.True(store.Load().FirstRunCompleted);
    }

    [Fact]
    public void Settings_file_never_contains_a_ble_address_field()
    {
        var store = new SettingsStore(_directory);
        store.Save(new AppSettings { DeviceNameFilter = "Charge 6" });

        string json = File.ReadAllText(store.FilePath);

        // Guard against accidentally persisting RPA-derived identity later.
        Assert.DoesNotContain("Address\":", json.Replace("OscAddress", ""), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mac", json, StringComparison.Ordinal);
    }
}
