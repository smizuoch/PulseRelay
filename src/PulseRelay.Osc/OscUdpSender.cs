using System.Net.Sockets;

namespace PulseRelay.Osc;

/// <summary>Sends pre-encoded OSC datagrams over UDP to a fixed endpoint.</summary>
public sealed class OscUdpSender : IDisposable
{
    private readonly UdpClient _client;

    public OscUdpSender(string host, int port)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, ushort.MaxValue);

        Host = host;
        Port = port;
        _client = new UdpClient();
        _client.Connect(host, port);
    }

    public string Host { get; }

    public int Port { get; }

    public void Send(ReadOnlySpan<byte> datagram) => _client.Client.Send(datagram);

    public void Dispose() => _client.Dispose();
}
