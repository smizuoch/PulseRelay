using Microsoft.Extensions.Logging;
using PulseRelay.App;

namespace PulseRelay.Desktop.Services;

/// <summary>Picks the heart-rate source factory for the current platform at compile time.</summary>
public static class SourceFactoryProvider
{
    public static IHeartRateSourceFactory Create(ILoggerFactory loggerFactory) =>
#if WINDOWS_BLE
        new WindowsBleSourceFactory(loggerFactory);
#else
        OperatingSystem.IsLinux()
            ? new LinuxBleSourceFactory(loggerFactory)
            : new MockOnlySourceFactory(loggerFactory);
#endif
}
