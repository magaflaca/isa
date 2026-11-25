using System.IO;
using Terraria;
using Terraria.GameContent.UI;

namespace IsaClient.Networking;

/// <summary>
/// Servers older than 1.4.6 (network version 1.4.5.8, and 1.4.5.6 on top of <see cref="Compat1456"/>). What 1.4.6 changed:
/// - Handshake "Terraria326" (<see cref="NetworkVersion.Release"/>).
/// - Item animation (41): 1.4.6 added the animation length.
/// - Item tweaks (88): 1.4.6 dropped the item size.
/// - Emote bubbles (91): 1.4.6 writes the anchor by kind (NPC slot, player slot, projectile key); before, kind and a slot.
/// - Player hurt (117): 1.4.6 added the dodgeable bit; before, every hit could be dodged.
/// - Server info, platform and custom ack (163-165) do not exist, and neither does crossplay (<see cref="Crossplay"/>).
/// - The trap bomb projectile (1136) does not exist: it is never sent.
/// </summary>
public static class Compat1458
{
	public const int MessageCount = 163;

	public const int ProjectileCount = 1136;

	public static bool Active
	{
		get
		{
			if (!NetworkVersion.IsLatest)
			{
				return Main.netMode == 1;
			}
			return false;
		}
	}

	public static bool AllowSend(int msgType, int number)
	{
		if (!Active)
		{
			return true;
		}
		if (msgType == 27)
		{
			return Main.projectile[number].type < ProjectileCount;
		}
		return msgType < MessageCount;
	}

	public static bool Dodgeable(bool flag)
	{
		return flag || Active;
	}

	public static void SkipItemSize(BinaryReader reader, BitsByte flags)
	{
		if (Active)
		{
			if (flags[0])
			{
				reader.ReadInt16();
			}
			if (flags[1])
			{
				reader.ReadInt16();
			}
		}
	}

	public static WorldUIAnchor ReadEmoteAnchor(BinaryReader reader, int kind)
	{
		int slot = reader.ReadUInt16();
		switch (kind)
		{
		case 0:
			if (slot < Main.maxNPCs)
			{
				return new WorldUIAnchor(Main.npc[slot]);
			}
			break;
		case 1:
			if (slot < Main.maxPlayers)
			{
				Main.player[slot].emoteTime = 360;
				return new WorldUIAnchor(Main.player[slot]);
			}
			break;
		case 2:
			if (slot < Main.maxProjectiles)
			{
				return new WorldUIAnchor(Main.projectile[slot]);
			}
			break;
		}
		return new WorldUIAnchor();
	}
}
