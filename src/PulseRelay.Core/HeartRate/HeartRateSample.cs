namespace PulseRelay.Core.HeartRate;

/// <summary>
/// A single parsed Heart Rate Measurement (Bluetooth characteristic 0x2A37).
/// </summary>
/// <param name="Bpm">Heart rate in beats per minute (UINT8 or UINT16 form).</param>
/// <param name="SensorContact">Sensor contact status from the flags byte.</param>
/// <param name="EnergyExpendedKilojoules">Energy Expended field in kilojoules, when present.</param>
/// <param name="RrIntervalsMs">RR intervals in milliseconds (raw 1/1024 s units converted), empty when absent.</param>
/// <param name="Timestamp">Local receive time; the characteristic carries no timestamp.</param>
public sealed record HeartRateSample(
    int Bpm,
    SensorContactStatus SensorContact,
    int? EnergyExpendedKilojoules,
    IReadOnlyList<double> RrIntervalsMs,
    DateTimeOffset Timestamp);
