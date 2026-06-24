using Avalonia;
using PulseRelay.Desktop.Services;

namespace PulseRelay.Desktop;

public static class Program
{
    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    [STAThread]
    public static void Main(string[] args)
    {
        using var singleInstance = SingleInstanceGuard.Acquire();
        if (!singleInstance.HasOwnership)
        {
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
}
