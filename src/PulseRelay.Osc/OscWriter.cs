using System.Buffers.Binary;
using System.Text;

namespace PulseRelay.Osc;

/// <summary>
/// Minimal OSC 1.0 message encoder. Only what the heart-rate bridge needs:
/// a single int32 argument message.
/// </summary>
public static class OscWriter
{
    /// <summary>
    /// Encodes an OSC message with one int32 argument:
    /// null-terminated address padded to a 4-byte boundary, ",i" type tag string
    /// padded likewise, then the value as big-endian int32.
    /// </summary>
    public static byte[] WriteMessage(string address, int value)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        if (address[0] != '/')
        {
            throw new ArgumentException($"OSC address must start with '/': \"{address}\"", nameof(address));
        }

        if (!Ascii.IsValid(address))
        {
            throw new ArgumentException($"OSC address must be ASCII: \"{address}\"", nameof(address));
        }

        int addressLength = PaddedLength(address.Length + 1);
        const string typeTag = ",i";
        int typeTagLength = PaddedLength(typeTag.Length + 1);

        var buffer = new byte[addressLength + typeTagLength + sizeof(int)];
        Encoding.ASCII.GetBytes(address, buffer);
        Encoding.ASCII.GetBytes(typeTag, buffer.AsSpan(addressLength));
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(addressLength + typeTagLength), value);
        return buffer;
    }

    /// <summary>Rounds a string length (including its null terminator) up to a 4-byte boundary.</summary>
    private static int PaddedLength(int lengthIncludingNull) => (lengthIncludingNull + 3) & ~3;
}
