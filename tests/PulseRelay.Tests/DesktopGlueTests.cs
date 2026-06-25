using Avalonia.Headless.XUnit;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRelay.App;
using PulseRelay.Desktop;
using PulseRelay.Desktop.Services;
using Xunit;

namespace PulseRelay.Tests;

public class DesktopGlueTests
{
    [Fact]
    public void Build_avalonia_app_returns_configured_builder()
    {
        var builder = Program.BuildAvaloniaApp();

        Assert.NotNull(builder);
    }

    [Fact]
    public void Main_returns_when_single_instance_lock_is_already_owned()
    {
        using var guard = SingleInstanceGuard.Acquire();
        Assert.True(guard.HasOwnership);

        Program.Main([]);
    }

    [AvaloniaFact]
    public async Task App_shutdown_request_without_runtime_is_completed()
    {
        var app = new PulseRelay.Desktop.App();

        await app.RequestShutdownAsync();
    }

    [AvaloniaFact]
    public void App_framework_initialization_without_desktop_lifetime_does_not_start_runtime()
    {
        var app = new PulseRelay.Desktop.App();

        app.OnFrameworkInitializationCompleted();
    }

    [Fact]
    public void Source_factory_provider_returns_platform_factory()
    {
        var factory = SourceFactoryProvider.Create(NullLoggerFactory.Instance);

        Assert.IsAssignableFrom<IHeartRateSourceFactory>(factory);
    }
}
