using System;
using System.IO;
using Terraria.Localization;
using Terraria;

namespace IsaClient.Networking;

/// <summary>
/// Hand-crafted packets used by the world editor. Packet layouts follow the 1.4.5.8 protocol
/// (packet 5 carries stack, prefix, type and a flags byte; packet 147 carries the loadout index + accessory visibility).
/// </summary>
public class RawPackets
{
	/// <summary>
	/// Sends a forged inventory slot packet (5) announcing that <paramref name="number2"/> slot holds item <paramref name="number4"/>
	/// (stack 1, prefix <paramref name="number3"/>), or a forged loadout packet (147) with an invalid loadout index (TShock detection probe).
	/// </summary>
	public static void SendModifiedData(int msgType, int remoteClient = -1, int ignoreClient = -1, NetworkText text = null, int number = 0, float number2 = 0f, float number3 = 0f, float number4 = 0f, int number5 = 0, int number6 = 0, int number7 = 0)
	{
		if (Main.netMode != 1)
		{
			return;
		}
		int num = 256;
		lock (NetMessage.buffer[num])
		{
			BinaryWriter writer = NetMessage.buffer[num].writer;
			if (writer == null)
			{
				NetMessage.buffer[num].ResetWriter();
				writer = NetMessage.buffer[num].writer;
			}
			writer.BaseStream.Position = 0L;
			long position = writer.BaseStream.Position;
			writer.BaseStream.Position += 2L;
			writer.Write((byte)msgType);
			if (msgType == 5)
			{
				writer.Write((byte)number);
				writer.Write((short)number2);
				writer.Write((short)1);
				writer.Write((byte)number3);
				writer.Write((short)number4);
				writer.Write((byte)0);
			}
			if (msgType == 147)
			{
				writer.Write((byte)Main.myPlayer);
				writer.Write((byte)69);
				NetMessage.WriteAccessoryVisibility(writer, Main.player[Main.myPlayer].hideVisibleAccessory);
			}
			int num4 = (int)writer.BaseStream.Position;
			if (num4 > 65535)
			{
				throw new Exception("Maximum packet length exceeded. id: " + msgType + " length: " + num4);
			}
			writer.BaseStream.Position = position;
			writer.Write((ushort)num4);
			writer.BaseStream.Position = num4;
			if (IsaClient.Building.PasteJob.Route(NetMessage.buffer[num].writeBuffer, 0, num4))
			{
			}
			else if (Netplay.Connection.Socket.IsConnected())
			{
				try
				{
					NetMessage.buffer[num].spamCount++;
					PacketPacing.QueuedPackets++;
					Main.ActiveNetDiagnosticsUI.CountSentMessage(msgType, num4);
					Netplay.Connection.Socket.AsyncSend(NetMessage.buffer[num].writeBuffer, 0, num4, Netplay.Connection.ClientWriteCallBack);
				}
				catch
				{
				}
			}
			NetMessage.buffer[num].writeLocked = false;
		}
	}
}
