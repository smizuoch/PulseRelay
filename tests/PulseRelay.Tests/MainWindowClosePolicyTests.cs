using PulseRelay.Desktop.Services;
using Xunit;

namespace PulseRelay.Tests;

public class MainWindowClosePolicyTests
{
    [Fact]
    public void User_close_hides_when_hide_to_tray_is_enabled() =>
        Assert.Equal(
            MainWindowCloseAction.Hide,
            MainWindowClosePolicy.Decide(hideToTray: true, shutdownRequested: false));

    [Fact]
    public void User_close_requests_shutdown_when_hide_to_tray_is_disabled() =>
        Assert.Equal(
            MainWindowCloseAction.Shutdown,
            MainWindowClosePolicy.Decide(hideToTray: false, shutdownRequested: false));

    [Fact]
    public void Explicit_shutdown_allows_window_to_close() =>
        Assert.Equal(
            MainWindowCloseAction.Close,
            MainWindowClosePolicy.Decide(hideToTray: true, shutdownRequested: true));
}
