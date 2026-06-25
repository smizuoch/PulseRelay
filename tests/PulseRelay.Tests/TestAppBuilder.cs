using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(PulseRelay.Tests.TestAppBuilder))]

namespace PulseRelay.Tests;

/// <summary>
/// Headless Avalonia host. The real App is used so view smoke tests load the same resource
/// dictionary as production; without a desktop lifetime it does not spin up the bridge.
/// </summary>
public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<global::PulseRelay.Desktop.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
