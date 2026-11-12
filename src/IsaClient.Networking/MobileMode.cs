using System;
using System.IO;
using Terraria;
using IsaClient.Core;
using Terraria.Audio;

namespace IsaClient.Networking;

/// <summary>
/// Mobile simulation (main menu, under the network version): the client also sends packet 164 exactly as Terraria for
/// Android does (captured from a phone): right after the loadout packet (147) of the join, before the inventory, holding
/// the player's slot and three zero bytes. Servers that tell phones apart by it (TShock's PlayerPlatformInfo: IsaHacks,
/// MobileMannequinFix) treat this client as a phone. Vanilla PC servers ignore the packet (id over their message count).
/// </summary>
public static class MobileMode
{
	public const byte PlatformPacket = 164;

	[Setting("SimulateMobile")]
	public static bool Enabled;

	public static bool CanToggle => Main.netMode == 0;

	public static void Toggle()
	{
		if (!CanToggle)
		{
			return;
		}
		Enabled = !Enabled;
		SoundEngine.PlaySound(12);
		ClientSettings.Save();
	}

	public static string Tooltip => Enabled
		? "Playing as a phone: servers that tell phones apart see you as Terraria for Android.\nClick to play as a PC again."
		: "Playing as a PC.\nClick to look like a phone (Android) to the servers that tell them apart.";

	/// <summary>Right after packet 147 while joining (the server just gave the slot): the platform packet.</summary>
	public static void AfterSlot(int slot)
	{
		if (!Enabled)
		{
			return;
		}
		try
		{
			byte[] packet = new byte[7];
			BitConverter.GetBytes((ushort)packet.Length).CopyTo(packet, 0);
			packet[2] = PlatformPacket;
			packet[3] = (byte)slot;
			if (Netplay.Connection.Socket != null && Netplay.Connection.Socket.IsConnected())
			{
				Netplay.Connection.Socket.AsyncSend(packet, 0, packet.Length, Netplay.Connection.ClientWriteCallBack);
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}
}
