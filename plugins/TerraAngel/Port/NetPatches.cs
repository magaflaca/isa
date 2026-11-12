using HarmonyLib;
using Terraria.Net.Sockets;

namespace TerraAngel;

/// <summary>
/// The Net Debugger's feed: the bytes the client's socket sends and the bytes it receives, cut into packets.
/// It only patches small methods (the socket's send, NetMessage.ReceiveBytes): Harmony copies of the game's huge
/// SendData and GetData crashed in native code. Each patch only reads a flag unless the window is logging, and never
/// changes what the game does.
/// </summary>
public static partial class Patches
{
    private static void ApplyNet(Harmony harmony)
    {
        Prefix(harmony, AccessTools.Method(typeof(NetMessage), nameof(NetMessage.ReceiveBytes)), nameof(ReceiveBytesPrefix));
        foreach (Type socket in new[] { typeof(TcpSocket), typeof(SocialSocket) })
        {
            var send = AccessTools.Method(socket, "Terraria.Net.Sockets.ISocket.AsyncSend");
            if (send != null)
            {
                Prefix(harmony, send, nameof(AsyncSendPrefix));
            }
        }
    }

    private static void ReceiveBytesPrefix(byte[] bytes, int streamLength, int i)
    {
        NetMessageWindow window = NetMessageWindow.Instance;
        if (!window.Logging || i != 256 || Main.netMode != 1)
        {
            return;
        }
        Guard("ReceiveBytes", () => window.Received(bytes, streamLength));
    }

    private static void AsyncSendPrefix(object __instance, byte[] data, int offset, int size)
    {
        NetMessageWindow window = NetMessageWindow.Instance;
        if (!window.Logging || Main.netMode != 1 || !ReferenceEquals(__instance, Netplay.Connection?.Socket))
        {
            return;
        }
        Guard("AsyncSend", () => window.Sent(data, offset, size));
    }
}
