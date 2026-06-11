using Microsoft.Extensions.Logging;
using PulseRelay.App.Settings;
using PulseRelay.Core.Sources;

namespace PulseRelay.App;

/// <summary>
/// Factory for platforms without BLE support (macOS development): only the simulated source
/// is available. The Windows desktop head replaces this with a BLE-capable factory.
/// </summary>
public sealed class MockOnlySourceFactory : IHeartRateSourceFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public MockOnlySourceFactory(ILoggerFactory loggerFactory) => _loggerFactory = loggerFactory;

    public bool SupportsBle => false;

    public IHeartRateSource Create(AppSettings settings) => settings.SourceKind switch
    {
        HeartRateSourceKind.Mock => new MockHeartRateSource(
            logger: _loggerFactory.CreateLogger<MockHeartRateSource>()),
        HeartRateSourceKind.Ble => throw new PlatformNotSupportedException(
            "Bluetooth LE devices aren't supported on this platform yet."),
        _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.SourceKind, "Unknown source kind."),
    };
}
