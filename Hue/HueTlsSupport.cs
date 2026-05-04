using System.Net;
using System.Net.Sockets;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace hui.Hue;

internal sealed class UdpTransport : DatagramTransport
{
    private readonly UdpClient _udpClient;

    public UdpTransport(UdpClient udpClient)
    {
        _udpClient = udpClient;
        _udpClient.Client.ReceiveTimeout = 30000;
        _udpClient.Client.SendTimeout = 30000;
    }

    public int GetReceiveLimit() => 65535;

    public int GetSendLimit() => 65535;

    public int Receive(byte[] buf, int off, int len, int waitMillis)
    {
        try
        {
            _udpClient.Client.ReceiveTimeout = waitMillis;
            IPEndPoint? endpoint = null;
            var payload = _udpClient.Receive(ref endpoint);
            var copyLength = Math.Min(len, payload.Length);
            Array.Copy(payload, 0, buf, off, copyLength);
            return copyLength;
        }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.TimedOut)
        {
            return -1;
        }
    }

    public int Receive(Span<byte> buffer, int waitMillis)
    {
        try
        {
            _udpClient.Client.ReceiveTimeout = waitMillis;
            IPEndPoint? endpoint = null;
            var payload = _udpClient.Receive(ref endpoint);
            var copyLength = Math.Min(buffer.Length, payload.Length);
            payload.AsSpan(0, copyLength).CopyTo(buffer);
            return copyLength;
        }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.TimedOut)
        {
            return -1;
        }
    }

    public void Send(byte[] buf, int off, int len)
    {
        if (off == 0)
        {
            _udpClient.Send(buf, len);
            return;
        }

        _udpClient.Send(buf.AsSpan(off, len));
    }

    public void Send(ReadOnlySpan<byte> buffer)
    {
        _udpClient.Send(buffer);
    }

    public void Close()
    {
        _udpClient.Dispose();
    }
}

internal sealed class HuePskTlsClient : PskTlsClient
{
    public HuePskTlsClient(string identity, byte[] psk)
        : base(new HueTlsCrypto(), new BasicTlsPskIdentity(identity, psk))
    {
    }

    public override ProtocolVersion[] GetProtocolVersions()
    {
        return [ProtocolVersion.DTLSv12];
    }

    public override int[] GetCipherSuites()
    {
        return [CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256];
    }
}

internal sealed class HueTlsCrypto : BcTlsCrypto
{
    public HueTlsCrypto()
        : base(new SecureRandom(new CryptoApiRandomGenerator()))
    {
    }
}

