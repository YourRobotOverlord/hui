using System.Net.Sockets;
using System.Text;

namespace hui.Hue;

internal sealed class HueEntertainmentStreamer : IDisposable
{
    private readonly string _bridge;
    private readonly string _appKey;
    private readonly string _clientKey;
    private readonly byte[] _areaIdBytes;

    private UdpTransport? _udpTransport;
    private Org.BouncyCastle.Tls.DtlsTransport? _dtlsTransport;
    private byte _sequenceNumber;

    public HueEntertainmentStreamer(string bridge, string appKey, string clientKey, string areaId)
    {
        _bridge = bridge;
        _appKey = appKey;
        _clientKey = clientKey;
        _areaIdBytes = Encoding.ASCII.GetBytes(areaId.ToLowerInvariant());
        if (_areaIdBytes.Length != 36)
        {
            throw new InvalidOperationException("Entertainment area id must be UUID string.");
        }
    }

    public void Connect()
    {
        var udpClient = new UdpClient();
        udpClient.Connect(_bridge, 2100);

        _udpTransport = new UdpTransport(udpClient);
        var dtlsProtocol = new Org.BouncyCastle.Tls.DtlsClientProtocol();
        _dtlsTransport = dtlsProtocol.Connect(new HuePskTlsClient(_appKey, Convert.FromHexString(_clientKey)), _udpTransport);
    }

    public void SendFrame(IReadOnlyList<ChannelColor> colors)
    {
        if (_dtlsTransport is null)
        {
            throw new InvalidOperationException("Entertainment stream not connected.");
        }

        foreach (var chunk in colors.Chunk(20))
        {
            var packet = BuildPacket(chunk);
            _dtlsTransport.Send(packet, 0, packet.Length);
        }
    }

    public void SendBlackout(IReadOnlyList<EntertainmentChannel> channels)
    {
        var blackout = channels
            .Select(channel => new ChannelColor((byte)channel.ChannelId, RgbColor.Black))
            .ToArray();
        SendFrame(blackout);
    }

    private byte[] BuildPacket(IReadOnlyList<ChannelColor> chunk)
    {
        var buffer = new byte[52 + (chunk.Count * 7)];
        "HueStream"u8.CopyTo(buffer.AsSpan(0, 9));
        buffer[9] = 0x02;
        buffer[10] = 0x00;
        buffer[11] = _sequenceNumber++;
        buffer[14] = 0x00;
        _areaIdBytes.CopyTo(buffer, 16);

        var offset = 52;
        foreach (var color in chunk)
        {
            buffer[offset++] = color.ChannelId;
            offset = WriteColor(buffer, offset, color.Color.R);
            offset = WriteColor(buffer, offset, color.Color.G);
            offset = WriteColor(buffer, offset, color.Color.B);
        }

        return buffer;
    }

    private static int WriteColor(byte[] buffer, int offset, double value)
    {
        var channel = (byte)Math.Round(Math.Clamp(value, 0, 1) * 255d);
        buffer[offset++] = channel;
        buffer[offset++] = channel;
        return offset;
    }

    public void Dispose()
    {
        _dtlsTransport?.Close();
        _udpTransport?.Close();
    }
}

internal readonly record struct ChannelColor(byte ChannelId, RgbColor Color);

internal readonly record struct RgbColor(double R, double G, double B)
{
    public static RgbColor Black => new(0, 0, 0);
}

