using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRelay.App;
using PulseRelay.App.Settings;
using PulseRelay.Desktop.ViewModels;
using Xunit;

namespace PulseRelay.Tests;

public class SettingsViewModelTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PulseRelayTests", Guid.NewGuid().ToString("N"));

    private readonly CultureInfo? _defaultCulture = CultureInfo.DefaultThreadCurrentUICulture;
    private readonly CultureInfo _currentCulture = CultureInfo.CurrentUICulture;

    public void Dispose()
    {
        CultureInfo.DefaultThreadCurrentUICulture = _defaultCulture;
        CultureInfo.CurrentUICulture = _currentCulture;
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private (SettingsViewModel ViewModel, AppSettings Settings, SettingsStore Store) CreateViewModel()
    {
        var session = new BridgeSession(new FakeSourceFactory(), NullLoggerFactory.Instance);
        var supervisor = new BridgeSupervisor(session);
        var settings = new AppSettings();
        var store = new SettingsStore(_directory);
        return (new SettingsViewModel(supervisor, settings, store), settings, store);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData("abc")]
    public void Invalid_port_blocks_save(string port)
    {
        var (viewModel, _, store) = CreateViewModel();
        bool closed = false;
        viewModel.CloseRequested += (_, _) => closed = true;

        viewModel.OscPortText = port;
        viewModel.SaveCommand.Execute(null);

        Assert.NotNull(viewModel.ErrorText);
        Assert.False(closed);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Address_without_slash_blocks_save()
    {
        var (viewModel, settings, store) = CreateViewModel();

        viewModel.OscAddress = "avatar/heartrate";
        viewModel.SaveCommand.Execute(null);

        Assert.NotNull(viewModel.ErrorText);
        Assert.False(File.Exists(store.FilePath));
        Assert.Equal("/avatar/parameters/VRCOSC/Heartrate/Value", settings.OscAddress);
    }

    [Fact]
    public void Valid_save_persists_and_mutates_shared_settings()
    {
        var (viewModel, settings, store) = CreateViewModel();
        bool? savedArg = null;
        viewModel.CloseRequested += (_, saved) => savedArg = saved;

        viewModel.OscHost = "192.168.0.10";
        viewModel.OscPortText = "9100";
        viewModel.DeviceNameFilter = "  Charge 6  ";
        viewModel.SaveCommand.Execute(null);

        Assert.True(savedArg);
        Assert.Null(viewModel.ErrorText);
        Assert.Equal("192.168.0.10", settings.OscHost);
        Assert.Equal(9100, settings.OscPort);
        Assert.Equal("Charge 6", settings.DeviceNameFilter);

        var reloaded = store.Load();
        Assert.Equal("192.168.0.10", reloaded.OscHost);
        Assert.Equal(9100, reloaded.OscPort);
        Assert.Equal("Charge 6", reloaded.DeviceNameFilter);
    }

    [Fact]
    public void Explicit_osc_off_is_saved()
    {
        var (viewModel, settings, store) = CreateViewModel();

        viewModel.OscEnabled = false;
        viewModel.SaveCommand.Execute(null);

        Assert.False(settings.OscEnabled);
        Assert.False(store.Load().OscEnabled);
    }

    [Fact]
    public void Language_save_applies_culture_live()
    {
        var (viewModel, settings, _) = CreateViewModel();

        viewModel.SelectedLanguage = viewModel.LanguageOptions.First(o => o.Value == AppLanguage.Japanese);
        viewModel.SaveCommand.Execute(null);

        Assert.Equal(AppLanguage.Japanese, settings.Language);
        Assert.Equal("ja", CultureInfo.CurrentUICulture.Name);
    }

    [Fact]
    public void Cancel_changes_nothing()
    {
        var (viewModel, settings, store) = CreateViewModel();
        bool? savedArg = null;
        viewModel.CloseRequested += (_, saved) => savedArg = saved;

        viewModel.OscPortText = "9100";
        viewModel.CancelCommand.Execute(null);

        Assert.False(savedArg);
        Assert.Equal(9000, settings.OscPort);
        Assert.False(File.Exists(store.FilePath));
    }
}
