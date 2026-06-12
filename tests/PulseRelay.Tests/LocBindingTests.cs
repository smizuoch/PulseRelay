using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRelay.App;
using PulseRelay.App.Localization;
using PulseRelay.App.Settings;
using PulseRelay.Desktop.Localization;
using PulseRelay.Desktop.ViewModels;
using Xunit;

namespace PulseRelay.Tests;

/// <summary>
/// Live language switching, tested at the binding level: a <c>{loc:Loc Key}</c> binding is
/// a reflection binding to the <see cref="Loc"/> indexer, so these tests reproduce exactly
/// what the XAML does. Regression guard for the bug where buttons stayed Japanese in
/// English mode because the indexer change notification used WPF's name.
/// </summary>
public class LocBindingTests
{
    /// <summary>Restores the process-wide culture state LocalizationManager.Apply mutates.</summary>
    private sealed class LanguageScope : IDisposable
    {
        private readonly CultureInfo? _default = CultureInfo.DefaultThreadCurrentUICulture;
        private readonly CultureInfo _current = CultureInfo.CurrentUICulture;

        public void Dispose()
        {
            CultureInfo.DefaultThreadCurrentUICulture = _default;
            CultureInfo.CurrentUICulture = _current;
        }
    }

    // The strings reported stuck in Japanese after switching to English.
    [AvaloniaTheory]
    [InlineData("Action_Start")]
    [InlineData("Output_TurnOn")]
    [InlineData("Output_TurnOff")]
    [InlineData("Device_ConnectHint")]
    public void Loc_binding_refreshes_when_language_changes(string key)
    {
        using var scope = new LanguageScope();
        var text = new TextBlock();
        text.Bind(TextBlock.TextProperty, new Binding($"[{key}]") { Source = Loc.Instance });

        LocalizationManager.Apply(AppLanguage.English);
        string english = LocalizationManager.GetString(key, CultureInfo.GetCultureInfo("en"));
        Assert.Equal(english, text.Text);

        LocalizationManager.Apply(AppLanguage.Japanese);
        string japanese = LocalizationManager.GetString(key, CultureInfo.GetCultureInfo("ja"));
        Assert.Equal(japanese, text.Text);
        Assert.NotEqual(english, japanese);
    }

    [AvaloniaFact]
    public void Dashboard_derived_strings_refresh_when_language_changes()
    {
        using var scope = new LanguageScope();
        string directory = Path.Combine(Path.GetTempPath(), "PulseRelayTests", Guid.NewGuid().ToString("N"));
        var session = new BridgeSession(new FakeSourceFactory(), NullLoggerFactory.Instance);
        var supervisor = new BridgeSupervisor(session);
        var settings = new AppSettings();
        using var viewModel = new DashboardViewModel(supervisor, settings, new SettingsStore(directory));

        LocalizationManager.Apply(AppLanguage.English);
        Assert.Equal("OSC on", viewModel.OscStateText);
        Assert.Equal("Not connected", viewModel.StatusText);

        LocalizationManager.Apply(AppLanguage.Japanese);
        Assert.Equal(
            LocalizationManager.GetString("Output_OscOn", CultureInfo.GetCultureInfo("ja")),
            viewModel.OscStateText);
        Assert.Equal(
            LocalizationManager.GetString("Status_NotConnected", CultureInfo.GetCultureInfo("ja")),
            viewModel.StatusText);
    }
}
