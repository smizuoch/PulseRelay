namespace PulseRelay.Desktop.Services;

public enum MainWindowCloseAction
{
    Hide,
    Shutdown,
    Close,
}

public static class MainWindowClosePolicy
{
    public static MainWindowCloseAction Decide(bool hideToTray, bool shutdownRequested) =>
        shutdownRequested
            ? MainWindowCloseAction.Close
            : hideToTray
                ? MainWindowCloseAction.Hide
                : MainWindowCloseAction.Shutdown;
}
