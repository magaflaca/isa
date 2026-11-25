using System;
using IsaClient.Core;
using Terraria;
using Terraria.Audio;

namespace IsaClient.Networking;

/// <summary>
/// Network version switch ([F5] / paper airplane in the main menu): the Terraria protocol used to join servers.
/// 1.4.6.1 is the game's own. Older servers are joined by translating, packet by packet, what changed since:
/// <see cref="Compat1458"/> for 1.4.6 (used by 1.4.5.8 and 1.4.5.6) and <see cref="Compat1456"/> for 1.4.5.7 (1.4.5.6 only).
/// Saved; only changed while disconnected, a live connection keeps the version it joined with.
/// </summary>
public static class NetworkVersion
{
	public enum Protocol
	{
		V1461,
		V1458,
		V1456
	}

	private static readonly string[] Names = { "1.4.6.1", "1.4.5.8", "1.4.5.6" };

	private static readonly int[] Releases = { 329, 326, 319 };

	public static Protocol Current;

	public static string Name => NameOf(Current);

	public static int Release => ReleaseOf(Current);

	public static bool IsLatest => Current == Protocol.V1461;

	public static bool CanChange => Main.netMode == 0;

	public static string NameOf(Protocol protocol)
	{
		return Names[(int)protocol];
	}

	public static int ReleaseOf(Protocol protocol)
	{
		return Releases[(int)protocol];
	}

	public static Protocol Parse(string name)
	{
		int index = Array.IndexOf(Names, name);
		return (index >= 0) ? ((Protocol)index) : Protocol.V1461;
	}

	public static void Cycle()
	{
		if (!CanChange)
		{
			return;
		}
		Current = (Protocol)(((int)Current + 1) % Names.Length);
		SoundEngine.PlaySound(12);
		ClientSettings.Save();
	}

	public static bool AllowSend(int msgType, int number, float number2, float number3, float number4)
	{
		if (Compat1458.AllowSend(msgType, number))
		{
			return Compat1456.AllowSend(msgType, number, number2, number3, number4);
		}
		return false;
	}

	public static string Tooltip
	{
		get
		{
			switch (Current)
			{
			case Protocol.V1458:
				return "Network version used to join servers: 1.4.5.8 (older servers).\nPackets that changed in 1.4.6 are translated; crossplay is off.\nClick (or [F5]) for 1.4.5.6 servers.";
			case Protocol.V1456:
				return "Network version used to join servers: 1.4.5.6 (older servers).\nPackets that changed in 1.4.5.7 and 1.4.6 are translated; content added after 1.4.5.6 is not sent.\nClick (or [F5]) to go back to 1.4.6.1.";
			default:
				return "Network version used to join servers: 1.4.6.1 (current).\nClick (or [F5]) for 1.4.5.8 servers.";
			}
		}
	}
}
