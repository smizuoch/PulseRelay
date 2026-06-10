using System.Buffers.Binary;

namespace PulseRelay.Core.HeartRate;

/// <summary>
/// Parser for the Bluetooth Heart Rate Measurement characteristic value (0x2A37),
/// per the Heart Rate Service specification.
/// </summary>
public static class HeartRateMeasurementParser
{
    private const byte HeartRateValueFormat16Bit = 0b0000_0001;
    private const byte SensorContactDetectedBit = 0b0000_0010;
    private const byte SensorContactSupportedBit = 0b0000_0100;
    private const byte EnergyExpendedPresentBit = 0b0000_1000;
    private const byte RrIntervalPresentBit = 0b0001_0000;

    /// <summary>
    /// Parses a raw 0x2A37 notification payload.
    /// Field order per spec: flags, heart rate value, energy expended, RR intervals.
    /// </summary>
    /// <exception cref="FormatException">The payload is empty or truncated; the message includes the payload hex.</exception>
    public static HeartRateSample Parse(ReadOnlySpan<byte> value, DateTimeOffset timestamp)
    {
        if (value.IsEmpty)
        {
            throw new FormatException("Heart Rate Measurement payload is empty.");
        }

        byte flags = value[0];
        int offset = 1;

        int bpm;
        if ((flags & HeartRateValueFormat16Bit) != 0)
        {
            if (value.Length < offset + 2)
            {
                throw Truncated("16-bit heart rate value", value);
            }

            bpm = BinaryPrimitives.ReadUInt16LittleEndian(value[offset..]);
            offset += 2;
        }
        else
        {
            if (value.Length < offset + 1)
            {
                throw Truncated("8-bit heart rate value", value);
            }

            bpm = value[offset];
            offset += 1;
        }

        SensorContactStatus contact = (flags & SensorContactSupportedBit) == 0
            ? SensorContactStatus.NotSupported
            : (flags & SensorContactDetectedBit) != 0
                ? SensorContactStatus.Contact
                : SensorContactStatus.NoContact;

        int? energyExpended = null;
        if ((flags & EnergyExpendedPresentBit) != 0)
        {
            if (value.Length < offset + 2)
            {
                throw Truncated("Energy Expended field", value);
            }

            energyExpended = BinaryPrimitives.ReadUInt16LittleEndian(value[offset..]);
            offset += 2;
        }

        IReadOnlyList<double> rrIntervals = [];
        if ((flags & RrIntervalPresentBit) != 0)
        {
            int remaining = value.Length - offset;
            if (remaining < 2 || remaining % 2 != 0)
            {
                throw Truncated("RR-Interval field", value);
            }

            var intervals = new double[remaining / 2];
            for (int i = 0; i < intervals.Length; i++)
            {
                ushort raw = BinaryPrimitives.ReadUInt16LittleEndian(value[offset..]);
                intervals[i] = raw * 1000.0 / 1024.0;
                offset += 2;
            }

            rrIntervals = intervals;
        }

        return new HeartRateSample(bpm, contact, energyExpended, rrIntervals, timestamp);
    }

    private static FormatException Truncated(string field, ReadOnlySpan<byte> value) =>
        new($"Heart Rate Measurement payload truncated in {field} (payload: {Convert.ToHexString(value)}).");
}
