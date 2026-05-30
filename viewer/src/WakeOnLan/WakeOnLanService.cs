using System.Net;
using System.Net.Sockets;

namespace SelfDesk.Viewer.WakeOnLan;

public static class WakeOnLanService
{
    public static async Task SendMagicPacketAsync(string macAddress)
    {
        var macBytes = ParseMac(macAddress);
        var packet   = BuildMagicPacket(macBytes);

        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        await udp.SendAsync(packet, new IPEndPoint(IPAddress.Broadcast, 9));
    }

    private static byte[] ParseMac(string mac)
    {
        var clean = mac.Replace(":", "").Replace("-", "");
        if (clean.Length != 12)
            throw new ArgumentException($"Endereço MAC inválido: {mac}");
        var bytes = new byte[6];
        for (int i = 0; i < 6; i++)
            bytes[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
        return bytes;
    }

    private static byte[] BuildMagicPacket(byte[] mac)
    {
        var packet = new byte[6 + 16 * 6];
        for (int i = 0; i < 6; i++)  packet[i] = 0xFF;
        for (int i = 0; i < 16; i++) mac.CopyTo(packet, 6 + i * 6);
        return packet;
    }
}
