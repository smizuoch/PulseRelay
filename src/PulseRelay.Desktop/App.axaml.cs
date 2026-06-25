using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PulseRelay.Desktop.Services;

namespace PulseRelay.Desktop;

public class App : Application
{
    private DesktopAppRuntime? _runtime;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _runtime = new DesktopAppRuntime(desktop);
            _runtime.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public Task RequestShutdownAsync() => _runtime?.RequestShutdownAsync() ?? Task.CompletedTask;
}
