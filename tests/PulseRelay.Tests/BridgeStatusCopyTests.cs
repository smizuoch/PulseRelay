using PulseRelay.App;
using PulseRelay.App.Localization;
using Xunit;

namespace PulseRelay.Tests;

public class BridgeStatusCopyTests
{
    [Theory]
    [InlineData(BridgeStatus.NotConnected, "Device_State_NotConnected")]
    [InlineData(BridgeStatus.Searching, "Device_State_Searching")]
    [InlineData(BridgeStatus.Connecting, "Device_State_Connecting")]
    [InlineData(BridgeStatus.WaitingForData, "Device_State_WaitingForData")]
    [InlineData(BridgeStatus.Streaming, "Device_State_Streaming")]
    [InlineData(BridgeStatus.Stale, "Device_State_Stale")]
    [InlineData(BridgeStatus.Disconnected, "Device_State_Disconnected")]
    [InlineData(BridgeStatus.Reconnecting, "Device_State_Reconnecting")]
    [InlineData(BridgeStatus.Failed, "Device_State_Failed")]
    public void Device_state_returns_localized_label_for_known_statuses(BridgeStatus status, string key)
    {
        using var culture = new CultureScope("en");

        Assert.Equal(LocalizationManager.GetString(key), BridgeStatusCopy.DeviceState(status));
    }

    [Fact]
    public void Device_state_falls_back_to_enum_name_for_unknown_status()
    {
        Assert.Equal("999", BridgeStatusCopy.DeviceState((BridgeStatus)999));
    }

    [Theory]
    [InlineData(BridgeStatus.NotConnected, "Status_NotConnected")]
    [InlineData(BridgeStatus.Searching, "Status_Searching")]
    [InlineData(BridgeStatus.Connecting, "Status_Connecting")]
    [InlineData(BridgeStatus.WaitingForData, "Status_WaitingForData")]
    [InlineData(BridgeStatus.Streaming, "Status_Streaming")]
    [InlineData(BridgeStatus.Disconnected, "Status_Disconnected")]
    [InlineData(BridgeStatus.Reconnecting, "Status_Reconnecting")]
    public void Headline_returns_localized_label_for_known_statuses(BridgeStatus status, string key)
    {
        using var culture = new CultureScope("en");
        var snapshot = BridgeSnapshot.Initial with { Status = status };

        string headline = BridgeStatusCopy.Headline(
            snapshot,
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(10));

        Assert.Equal(LocalizationManager.GetString(key), headline);
    }

    [Fact]
    public void Headline_falls_back_to_enum_value_for_unknown_status()
    {
        var snapshot = BridgeSnapshot.Initial with { Status = (BridgeStatus)999 };

        string headline = BridgeStatusCopy.Headline(
            snapshot,
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(10));

        Assert.Equal("999", headline);
    }

    [Theory]
    [InlineData(BridgeFailureKind.DeviceNotFound, "Error_DeviceNotFound")]
    [InlineData(BridgeFailureKind.BluetoothUnavailable, "Error_BluetoothUnavailable")]
    [InlineData(BridgeFailureKind.PlatformUnsupported, "Error_PlatformUnsupported")]
    [InlineData(BridgeFailureKind.OscConfig, "Error_OscConfig")]
    [InlineData(BridgeFailureKind.ConnectionTimeout, "Error_ConnectionTimeout")]
    public void Failed_headline_uses_failure_kind_copy(BridgeFailureKind kind, string key)
    {
        using var culture = new CultureScope("en");
        var snapshot = BridgeSnapshot.Initial with
        {
            Status = BridgeStatus.Failed,
            FailureKind = kind,
            LastError = "raw error",
        };

        string headline = BridgeStatusCopy.Headline(
            snapshot,
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(10));

        Assert.Equal(LocalizationManager.GetString(key), headline);
    }

    [Fact]
    public void Failed_headline_falls_back_to_last_error()
    {
        var snapshot = BridgeSnapshot.Initial with
        {
            Status = BridgeStatus.Failed,
            LastError = "raw error",
        };

        string headline = BridgeStatusCopy.Headline(
            snapshot,
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(10));

        Assert.Equal("raw error", headline);
    }

    [Fact]
    public void Failed_headline_without_error_uses_generic_copy()
    {
        using var culture = new CultureScope("en");
        var snapshot = BridgeSnapshot.Initial with { Status = BridgeStatus.Failed };

        string headline = BridgeStatusCopy.Headline(
            snapshot,
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(10));

        Assert.Equal(LocalizationManager.GetString("Status_Failed"), headline);
    }

    [Fact]
    public void Failed_headline_with_unknown_failure_kind_without_error_uses_generic_copy()
    {
        using var culture = new CultureScope("en");
        var snapshot = BridgeSnapshot.Initial with
        {
            Status = BridgeStatus.Failed,
            FailureKind = (BridgeFailureKind)999,
        };

        string headline = BridgeStatusCopy.Headline(
            snapshot,
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(10));

        Assert.Equal(LocalizationManager.GetString("Status_Failed"), headline);
    }

    [Fact]
    public void Stale_headline_includes_elapsed_seconds()
    {
        using var culture = new CultureScope("en");
        var now = DateTimeOffset.UtcNow;
        var snapshot = BridgeSnapshot.Initial with
        {
            Status = BridgeStatus.Streaming,
            LastSampleAt = now - TimeSpan.FromSeconds(12),
        };

        string headline = BridgeStatusCopy.Headline(snapshot, now, TimeSpan.FromSeconds(10));

        Assert.Contains("12s", headline);
    }

    [Fact]
    public void Stale_headline_without_sample_time_reports_zero_seconds()
    {
        using var culture = new CultureScope("en");
        var snapshot = BridgeSnapshot.Initial with { Status = BridgeStatus.Stale };

        string headline = BridgeStatusCopy.Headline(
            snapshot,
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(10));

        Assert.Contains("0s", headline);
    }

    [Theory]
    [InlineData(true, "Status_Reconnecting")]
    [InlineData(false, "Status_ReconnectingInitial")]
    public void Supervisor_headline_distinguishes_initial_and_streamed_reconnects(
        bool hasStreamed,
        string key)
    {
        using var culture = new CultureScope("en");
        var snapshot = SupervisorSnapshot.Initial with
        {
            RunState = BridgeRunState.Reconnecting,
            HasStreamedThisRun = hasStreamed,
        };

        string headline = BridgeStatusCopy.Headline(
            snapshot,
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(10));

        Assert.Equal(LocalizationManager.GetString(key), headline);
    }

    [Fact]
    public void Placeholder_device_line_trims_filter()
    {
        Assert.Equal("Charge 6", BridgeStatusCopy.DeviceLine("BLE <unknown>", "  Charge 6  "));
    }
}
