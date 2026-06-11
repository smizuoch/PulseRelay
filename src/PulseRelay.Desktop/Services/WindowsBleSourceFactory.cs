#if WINDOWS_BLE
using Microsoft.Extensions.Logging;
using PulseRelay.App;
using PulseRelay.App.Settings;
using PulseRelay.Core.Sources;
using PulseRelay.WindowsBle;

namespace PulseRelay.Desktop.Services;

/// <summary>
/// Windows factory: BLE via <see cref="BleHeartRateSource"/>, with the simulated source still
/// available for testing. The only place in the desktop head that touches BLE types.
/// </summary>
public sealed class WindowsBleSourceFactory : IHeartRateSourceFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public WindowsBleSourceFactory(ILoggerFactory loggerFactory) => _loggerFactory = loggerFactory;

    public bool SupportsBle => true;

    public IHeartRateSource Create(AppSettings settings) => settings.SourceKind switch
    {
        HeartRateSourceKind.Ble => new BleHeartRateSource(
            _loggerFactory.CreateLogger<BleHeartRateSource>(),
            settings.DeviceNameFilter,
            TimeSpan.FromSeconds(settings.ScanTimeoutSeconds)),
        HeartRateSourceKind.Mock => new MockHeartRateSource(
            logger: _loggerFactory.CreateLogger<MockHeartRateSource>()),
        _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.SourceKind, "Unknown source kind."),
    };
}
#endif
