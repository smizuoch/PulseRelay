using Avalonia;
using Avalonia.Controls;
using PulseRelay.Desktop.Services;

namespace PulseRelay.Desktop.Views;

public partial class MainWindow : Window
{
    private Func<bool>? _hideToTray;
    private Func<Task>? _requestShutdown;
    private bool _shutdownRequested;

    public MainWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }

    public void ConfigureCloseBehavior(Func<bool> hideToTray, Func<Task> requestShutdown)
    {
        _hideToTray = hideToTray;
        _requestShutdown = requestShutdown;
    }

    public void AllowShutdownClose() => _shutdownRequested = true;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        var action = MainWindowClosePolicy.Decide(
            _hideToTray?.Invoke() ?? false,
            _shutdownRequested);
        switch (action)
        {
            case MainWindowCloseAction.Hide:
                e.Cancel = true;
                Hide();
                return;
            case MainWindowCloseAction.Shutdown:
                e.Cancel = true;
                Hide();
                if (_requestShutdown is not null)
                {
                    _ = _requestShutdown();
                }

                return;
            default:
                return;
        }
    }
}
