namespace PulseRelay.Core.HeartRate;

/// <summary>Sensor contact bits (flags bits 1-2) of a Heart Rate Measurement.</summary>
public enum SensorContactStatus
{
    /// <summary>The device does not report sensor contact (feature bit clear).</summary>
    NotSupported,

    /// <summary>Sensor contact is supported but no skin contact is detected.</summary>
    NoContact,

    /// <summary>Sensor contact is supported and skin contact is detected.</summary>
    Contact,
}
