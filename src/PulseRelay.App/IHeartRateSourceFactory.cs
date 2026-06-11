using PulseRelay.App.Settings;
using PulseRelay.Core.Sources;

namespace PulseRelay.App;

/// <summary>
/// Creates heart-rate sources from settings. The desktop head supplies a platform-specific
/// implementation so BLE construction stays out of this assembly and out of the UI views.
/// </summary>
public interface IHeartRateSourceFactory
{
    /// <summary>Whether <see cref="HeartRateSourceKind.Ble"/> is available on this platform.</summary>
    bool SupportsBle { get; }

    /// <summary>
    /// Creates a fresh, unstarted source. Throws <see cref="PlatformNotSupportedException"/>
    /// for <see cref="HeartRateSourceKind.Ble"/> when <see cref="SupportsBle"/> is false.
    /// </summary>
    IHeartRateSource Create(AppSettings settings);
}
