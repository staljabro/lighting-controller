using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LightingController.Output;

public enum DmxNetworkProtocol { ArtNet, Sacn }

public sealed class DmxNetworkOutput : IDisposable
{
    private readonly UdpClient _udp = new();
    private readonly Guid _cid = Guid.NewGuid();
    private byte _sequence;

    public async Task SendAsync(DmxNetworkProtocol protocol, int universe, ReadOnlyMemory<byte> values, string? destination = null, CancellationToken cancellationToken = default)
    {
        if (values.Length is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(values));
        byte[] packet;
        IPEndPoint endpoint;
        if (protocol == DmxNetworkProtocol.ArtNet)
        {
            if (universe is < 0 or > 32767) throw new ArgumentOutOfRangeException(nameof(universe));
            if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("Art-Net requires a node IP address.", nameof(destination));
            packet = DmxPacketBuilder.CreateArtDmx(universe, values.Span, NextSequence());
            endpoint = new IPEndPoint(IPAddress.Parse(destination), 6454);
        }
        else
        {
            if (universe is < 1 or > 63999) throw new ArgumentOutOfRangeException(nameof(universe));
            packet = DmxPacketBuilder.CreateSacn(universe, values.Span, NextSequence(), _cid, "Lighting Controller");
            var address = string.IsNullOrWhiteSpace(destination)
                ? IPAddress.Parse($"239.255.{universe >> 8}.{universe & 0xff}")
                : IPAddress.Parse(destination);
            endpoint = new IPEndPoint(address, 5568);
        }
        await _udp.SendAsync(packet, endpoint, cancellationToken);
    }

    private byte NextSequence() { _sequence++; if (_sequence == 0) _sequence = 1; return _sequence; }
    public void Dispose() => _udp.Dispose();
}

public static class DmxPacketBuilder
{
    public static byte[] CreateArtDmx(int universe, ReadOnlySpan<byte> values, byte sequence)
    {
        var length = Math.Max(2, values.Length + (values.Length & 1));
        var packet = new byte[18 + length];
        Encoding.ASCII.GetBytes("Art-Net\0").CopyTo(packet, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(8), 0x5000);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(10), 14);
        packet[12] = sequence;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(14), (ushort)universe);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(16), (ushort)length);
        values.CopyTo(packet.AsSpan(18));
        return packet;
    }

    public static byte[] CreateSacn(int universe, ReadOnlySpan<byte> values, byte sequence, Guid cid, string sourceName)
    {
        var propertyCount = values.Length + 1;
        var packet = new byte[126 + values.Length];
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(0), 0x0010);
        Encoding.ASCII.GetBytes("ASC-E1.17\0\0\0").CopyTo(packet, 4);
        WriteFlagsLength(packet, 16, packet.Length - 16);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(18), 4);
        cid.ToByteArray().CopyTo(packet, 22);
        WriteFlagsLength(packet, 38, packet.Length - 38);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(40), 2);
        Encoding.UTF8.GetBytes(sourceName[..Math.Min(63, sourceName.Length)]).CopyTo(packet, 44);
        packet[108] = 100;
        packet[111] = sequence;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(113), (ushort)universe);
        WriteFlagsLength(packet, 115, packet.Length - 115);
        packet[117] = 2;
        packet[118] = 0xa1;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(119), 0);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(121), 1);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(123), (ushort)propertyCount);
        values.CopyTo(packet.AsSpan(126));
        return packet;
    }

    private static void WriteFlagsLength(byte[] packet, int offset, int length) =>
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(offset), (ushort)(0x7000 | length));
}
