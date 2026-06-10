using Windows.Devices.Bluetooth;

namespace PulseRelay.WindowsBle;

/// <summary>Standard Bluetooth SIG UUIDs used by the heart-rate bridge.</summary>
public static class GattUuids
{
    /// <summary>Heart Rate Service (0x180D).</summary>
    public static readonly Guid HeartRateService = BluetoothUuidHelper.FromShortId(0x180D);

    /// <summary>Heart Rate Measurement characteristic (0x2A37).</summary>
    public static readonly Guid HeartRateMeasurement = BluetoothUuidHelper.FromShortId(0x2A37);

    /// <summary>Generic Access service (0x1800).</summary>
    public static readonly Guid GenericAccessService = BluetoothUuidHelper.FromShortId(0x1800);

    /// <summary>Device Name characteristic (0x2A00).</summary>
    public static readonly Guid DeviceName = BluetoothUuidHelper.FromShortId(0x2A00);
}
