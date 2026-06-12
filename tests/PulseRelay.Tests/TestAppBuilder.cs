using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(PulseRelay.Tests.TestAppBuilder))]

namespace PulseRelay.Tests;

/// <summary>
/// Headless Avalonia host for binding-level tests. A bare <see cref="Application"/> is
/// enough: the tests exercise bindings, not the real App (which would spin up the bridge).
/// </summary>
public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
