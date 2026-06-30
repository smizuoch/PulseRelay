#if !WINDOWS_BLE
using Microsoft.Extensions.Logging;
using PulseRelay.App;
using PulseRelay.App.Settings;
using PulseRelay.Core.Sources;
using PulseRelay.LinuxBle;

namespace PulseRelay.Desktop.Services;

/// <summary>Linux factory: BLE via BlueZ/D-Bus, with the simulated source still available for testing.</summary>
public sealed class LinuxBleSourceFactory : IHeartRateSourceFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public LinuxBleSourceFactory(ILoggerFactory loggerFactory) => _loggerFactory = loggerFactory;

    public bool SupportsBle => true;

    public IHeartRateSource Create(AppSettings settings) => settings.SourceKind switch
    {
        HeartRateSourceKind.Ble => new BluezHeartRateSource(
            new BluezDbusHeartRateClient(),
            _loggerFactory.CreateLogger<BluezHeartRateSource>(),
            settings.DeviceNameFilter,
            TimeSpan.FromSeconds(settings.ScanTimeoutSeconds)),
        HeartRateSourceKind.Mock => new MockHeartRateSource(
            logger: _loggerFactory.CreateLogger<MockHeartRateSource>()),
        _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.SourceKind, "Unknown source kind."),
    };
}
#endif
