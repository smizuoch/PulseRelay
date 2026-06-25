using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRelay.App;
using PulseRelay.App.Settings;
using PulseRelay.Core.HeartRate;
using PulseRelay.Core.Sources;
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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_host_blocks_save(string host)
    {
        var (viewModel, settings, store) = CreateViewModel();

        viewModel.OscHost = host;
        viewModel.SaveCommand.Execute(null);

        Assert.NotNull(viewModel.ErrorText);
        Assert.False(File.Exists(store.FilePath));
        Assert.Equal("127.0.0.1", settings.OscHost);
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
    public void Mock_source_and_empty_filter_are_saved()
    {
        var (viewModel, settings, store) = CreateViewModel();

        viewModel.IsBleSource = false;
        viewModel.DeviceNameFilter = "   ";
        viewModel.SaveCommand.Execute(null);

        Assert.Equal(HeartRateSourceKind.Mock, settings.SourceKind);
        Assert.Null(settings.DeviceNameFilter);
        var reloaded = store.Load();
        Assert.Equal(HeartRateSourceKind.Mock, reloaded.SourceKind);
        Assert.Null(reloaded.DeviceNameFilter);
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

    [Fact]
    public void Cancel_without_subscribers_is_noop()
    {
        var (viewModel, settings, store) = CreateViewModel();

        viewModel.CancelCommand.Execute(null);

        Assert.Equal(9000, settings.OscPort);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Hide_to_tray_choice_is_saved()
    {
        var (viewModel, settings, store) = CreateViewModel();

        viewModel.HideToTrayOnClose = false;
        viewModel.SaveCommand.Execute(null);

        Assert.False(settings.HideToTrayOnClose);
        Assert.False(store.Load().HideToTrayOnClose);
    }

    [Fact]
    public async Task Save_failure_rolls_back_shared_settings_and_keeps_dialog_open()
    {
        string blockedDirectory = Path.Combine(
            Path.GetTempPath(),
            "PulseRelayTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(blockedDirectory)!);
        File.WriteAllText(blockedDirectory, "not a directory");
        try
        {
            var session = new BridgeSession(new FakeSourceFactory(), NullLoggerFactory.Instance);
            var supervisor = new BridgeSupervisor(session);
            var settings = new AppSettings
            {
                OscHost = "127.0.0.1",
                OscPort = 9000,
                OscEnabled = true,
                OscAddress = "/avatar/parameters/VRCOSC/Heartrate/Value",
                SourceKind = HeartRateSourceKind.Ble,
                DeviceNameFilter = "Charge",
                HideToTrayOnClose = true,
            };
            var viewModel = new SettingsViewModel(supervisor, settings, new SettingsStore(blockedDirectory));
            bool closed = false;
            viewModel.CloseRequested += (_, _) => closed = true;

            viewModel.OscHost = "192.168.0.10";
            viewModel.OscPortText = "9100";
            viewModel.OscEnabled = false;
            viewModel.IsBleSource = false;
            viewModel.DeviceNameFilter = "Other";
            viewModel.HideToTrayOnClose = false;
            viewModel.SaveCommand.Execute(null);

            Assert.False(closed);
            Assert.NotNull(viewModel.ErrorText);
            Assert.True(settings.OscEnabled);
            Assert.Equal("127.0.0.1", settings.OscHost);
            Assert.Equal(9000, settings.OscPort);
            Assert.Equal(HeartRateSourceKind.Ble, settings.SourceKind);
            Assert.Equal("Charge", settings.DeviceNameFilter);
            Assert.True(settings.HideToTrayOnClose);
            await supervisor.DisposeAsync();
        }
        finally
        {
            File.Delete(blockedDirectory);
        }
    }

    [Fact]
    public async Task Osc_apply_failure_rolls_back_and_keeps_dialog_open()
    {
        BridgeSession? session = null;
        var source = new ReentrantAttachSource(() =>
            session!.DisconnectAsync().GetAwaiter().GetResult());
        var factory = new SingleSourceFactory(source);
        await using var createdSession = new BridgeSession(factory, NullLoggerFactory.Instance);
        session = createdSession;
        var supervisor = new BridgeSupervisor(createdSession);
        var settings = new AppSettings
        {
            SourceKind = HeartRateSourceKind.Mock,
            OscEnabled = false,
        };
        Assert.True(await createdSession.ConnectAsync(settings, CancellationToken.None));
        var viewModel = new SettingsViewModel(supervisor, settings, new SettingsStore(_directory));
        bool closed = false;
        viewModel.CloseRequested += (_, _) => closed = true;

        viewModel.OscEnabled = true;
        viewModel.SaveCommand.Execute(null);

        Assert.False(closed);
        Assert.NotNull(viewModel.ErrorText);
        Assert.False(settings.OscEnabled);
        Assert.Equal(OscOutputStatus.Error, createdSession.Snapshot.OscStatus);

        await supervisor.DisposeAsync();
    }

    private sealed class SingleSourceFactory(IHeartRateSource source) : IHeartRateSourceFactory
    {
        public bool SupportsBle => true;

        public IHeartRateSource Create(AppSettings settings) => source;
    }

    private sealed class ReentrantAttachSource(Action onPublisherAttach) : IHeartRateSource
    {
        private EventHandler<HeartRateSample>? _sampleReceived;
        private int _sampleSubscriberCount;

        public string Description => "reentrant source";

        public HeartRateSourceState State { get; private set; } = HeartRateSourceState.Idle;

        public event EventHandler<HeartRateSample>? SampleReceived
        {
            add
            {
                _sampleReceived += value;
                _sampleSubscriberCount++;
                if (_sampleSubscriberCount == 2)
                {
                    onPublisherAttach();
                }
            }
            remove => _sampleReceived -= value;
        }

        public event EventHandler<HeartRateSourceState>? StateChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
