using Terraria;
using IsaClient.Core;

namespace IsaClient.Networking;

/// <summary>How fast world edit sends tile/erase/liquid/paint packets, and when it waits for the server.</summary>
public static class PacketPacing
{
	[Setting("Packets500msMode", Remembered = true)]
	public static bool TilePacketMaxMode = false;

	[Setting("TilePacketRate", Remembered = true)]
	public static int TilePacketRate = 75;

	[Setting("ErasePacketRate", Remembered = true, Min = 1, Max = 1000)]
	public static int ErasePacketRate = 75;

	[Setting("LiquidPacketRate", Remembered = true, Min = 1, Max = 1000)]
	public static int LiquidPacketRate = 20;

	[Setting("PaintPacketRate", Remembered = true, Min = 1, Max = 1000)]
	public static int PaintPacketRate = 50;

	public static bool HandlingPackets;

	public static int QueuedPackets;

	/// <summary>World Edit → "Bypass FPS & Timeout Protections": paste / brush / fill pacing ignores low FPS and a silent server.</summary>
	[Setting("BypassPacketProtections", Remembered = true)]
	public static bool BypassPacketProtections;

	/// <summary>Packet pacing guard: the server has not sent anything for over a second (unless bypassed).</summary>
	public static bool ServerSilentGuard => !BypassPacketProtections && Netplay.Connection.TimeOutTimer > 60;

	#region Chat commands

	/// <summary>.packetmax / .packetlimit / .packm / .pl / .tilepacketmax / .tilepacketlimit / .tpackm / .tpl</summary>
	internal static void TilePacketLimitCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out var result32))
			{
				TilePacketRate = result32;
				ClientChat.Info("Tile & Wall Packet Rate set to " + result32);
			}
			else
			{
				ClientChat.Error("Invalid packet rate.");
			}
		}
		else
		{
			ClientChat.Error("No packet rate specified.");
		}
	}

	/// <summary>.erasepacketmax / .erasepacketlimit / .epackm / .epl</summary>
	internal static void ErasePacketLimitCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out var result21))
			{
				ErasePacketRate = result21;
				ClientChat.Info($"Erase Packet Rate set to {result21}");
			}
			else
			{
				ClientChat.Error("Invalid packet rate.");
			}
		}
		else
		{
			ClientChat.Error("No packet rate specified.");
		}
	}

	/// <summary>.liquidpacketmax / .liquidpacketlimit / .lpackm / .lpl</summary>
	internal static void LiquidPacketLimitCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out var result24))
			{
				LiquidPacketRate = result24;
				ClientChat.Info($"Liquid Packet Rate set to {result24}");
			}
			else
			{
				ClientChat.Error("Invalid packet rate.");
			}
		}
		else
		{
			ClientChat.Error("No packet rate specified.");
		}
	}

	/// <summary>.paintpacketmax / .paintpacketlimit / .ppackm / .ppl</summary>
	internal static void PaintPacketLimitCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out var result14))
			{
				PaintPacketRate = result14;
				ClientChat.Info($"Paint Packet Rate set to {result14}");
			}
			else
			{
				ClientChat.Error("Invalid packet rate.");
			}
		}
		else
		{
			ClientChat.Error("No packet rate specified.");
		}
	}

	#endregion
}
