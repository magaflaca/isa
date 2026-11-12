using System.IO;
using Terraria.Audio;
using Terraria.DataStructures;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Networking;

/// <summary>
/// Network version switch ([F5] / hammer in the main menu): lets this 1.4.5.8 client join 1.4.5.6 servers (release 319).
/// 1.4.5.7 reworked several packets, so with <see cref="Legacy"/> on every packet that changed is written and read in the
/// 1.4.5.6 layout:
/// - Handshake "Terraria319".
/// - Projectiles (27/29): 1.4.5.8 identifies them by a packed key (spawner, slot, generation); 1.4.5.6 by (identity, owner)
///   plus a UUID for the Stardust Dragon. Keys are rebuilt from (owner, identity), and ai fields that point at another
///   projectile (Last Prism, Lunar Portal, Stardust Dragon, ...) are converted between keys and identities.
/// - NPCs (23/28): 1.4.5.6 has no slot generation, short slot ids and no sync anchor. The server never acknowledges strikes
///   (packet 162), so the client stops predicting pending damage instead of piling it up forever.
/// - Items (21/22/39/145/148): the old drop flags (no grab delay), the old shimmer/enemy-grab packets and the old ownership
///   packet (no reservation timers).
/// - World info (7) without the dungeon position, catch NPC (70) with its player byte.
/// - NetModules: 1.4.5.6 has no CreativeUnlocks module and has TagEffectState, so ids 5-12 are shifted; leashed critter
///   types are shifted too (Aquatic Walker and Turtle were added).
/// - Content added after 1.4.5.6 (items, projectiles, buffs, tiles, mounts, particles) is never sent: a 1.4.5.6 server
///   would index out of its arrays and drop or kick.
/// </summary>
public static class ProtocolCompat
{
	public const int ModernRelease = 326;

	public const int LegacyRelease = 319;

	public const int LegacyItemCount = 6148;

	public const int LegacyProjectileCount = 1111;

	public const int LegacyTileCount = 753;

	public const int LegacyWallCount = 367;

	public const int LegacyBuffCount = 389;

	public const int LegacyMountCount = 64;

	public const int LegacyMessageCount = 162;

	public const int LegacyParticleCount = 69;

	/// <summary>1.4.5.6 protocol (release 319) instead of 1.4.5.8 (326). Saved; only changed from the main menu.</summary>
	public static bool Legacy;

	/// <summary>Legacy protocol on a multiplayer connection: the only case where packets are rewritten.</summary>
	public static bool Active
	{
		get
		{
			if (Legacy)
			{
				return Main.netMode == 1;
			}
			return false;
		}
	}

	public static int Release
	{
		get
		{
			if (!Legacy)
			{
				return ModernRelease;
			}
			return LegacyRelease;
		}
	}

	public static string VersionName
	{
		get
		{
			if (!Legacy)
			{
				return "1.4.5.8";
			}
			return "1.4.5.6";
		}
	}

	/// <summary>The protocol can only change while disconnected: a live connection keeps the one it joined with.</summary>
	public static bool CanToggle => Main.netMode == 0;

	public static void Toggle()
	{
		if (!CanToggle)
		{
			return;
		}
		Legacy = !Legacy;
		SoundEngine.PlaySound(12);
		ClientSettings.Save();
	}

	public static string Tooltip
	{
		get
		{
			if (!Legacy)
			{
				return "Network version used to join servers: 1.4.5.8 (current).\nClick (or [F5]) to join 1.4.5.6 servers instead.";
			}
			return "Network version used to join servers: 1.4.5.6 (older servers).\nPackets that changed in 1.4.5.7 are translated; content added after 1.4.5.6 is not sent.\nClick (or [F5]) to go back to 1.4.5.8.";
		}
	}

	#region Content added after 1.4.5.6

	/// <summary>Item id written for the server: items a 1.4.5.6 server does not know are sent as air.</summary>
	public static int ItemNetId(int type)
	{
		if (Active && type >= LegacyItemCount)
		{
			return 0;
		}
		return type;
	}

	public static bool KnownBuff(int type)
	{
		if (Active)
		{
			return type < LegacyBuffCount;
		}
		return true;
	}

	public static bool KnownMount(int type)
	{
		if (Active)
		{
			return type < LegacyMountCount;
		}
		return true;
	}

	/// <summary>
	/// SendData pre-filter: false drops the packet. Only packets carrying content a 1.4.5.6 server does not have, or packets
	/// that do not exist there.
	/// </summary>
	public static bool AllowSend(int msgType, int number, float number2, float number3, float number4)
	{
		if (!Active)
		{
			return true;
		}
		if (msgType >= LegacyMessageCount)
		{
			return false;
		}
		switch (msgType)
		{
		case 17:
		{
			// Tile edits that place a tile: 1 PlaceTile, 21 ReplaceTile (the tile type travels in number4).
			int action = number;
			if ((action == 1 || action == 21) && (int)number4 >= LegacyTileCount)
			{
				return false;
			}
			if ((action == 3 || action == 22) && (int)number4 >= LegacyWallCount)
			{
				return false;
			}
			break;
		}
		case 79:
			return (int)number3 < LegacyTileCount;
		case 27:
		{
			Projectile projectile = Main.projectile[number];
			return projectile.type < LegacyProjectileCount;
		}
		case 53:
		case 55:
			return (int)number2 < LegacyBuffCount;
		}
		return true;
	}

	#endregion

	#region Projectiles

	/// <summary>Projectiles whose ai field holds another projectile: a key in 1.4.5.8, the owner's identity in 1.4.5.6.</summary>
	public static int ProjectileRefAi(int type)
	{
		switch (type)
		{
		case 536:
		case 625:
		case 626:
		case 627:
		case 628:
			return 0;
		case 460:
		case 461:
		case 618:
		case 632:
		case 642:
		case 644:
			return 1;
		default:
			return -1;
		}
	}

	/// <summary>1.4.5.6 sends the Stardust Dragon's UUID (bit 7 of the projectile flags).</summary>
	public static bool NeedsUUID(int type)
	{
		if (type >= 625)
		{
			return type <= 628;
		}
		return false;
	}

	/// <summary>Our ai value (a projectile key) as 1.4.5.6 expects it: the identity, which is the key's slot.</summary>
	public static float AiToLegacy(float value)
	{
		ProjectileKey key = (ProjectileKey)value;
		if ((int)key == 0)
		{
			return 0f;
		}
		return key.Index;
	}

	/// <summary>
	/// Packet 27 in the 1.4.5.6 layout: identity, position, velocity, owner, type, flags, the optional fields, the Stardust
	/// Dragon UUID (bit 7) and ai[2]. Our identity is the key's slot, the same number 1.4.5.6 gave its projectiles.
	/// </summary>
	public static void WriteProjectile(BinaryWriter writer, Projectile projectile)
	{
		ProjectileKey key = projectile.key;
		float ai0 = projectile.ai[0];
		float ai1 = projectile.ai[1];
		float ai2 = projectile.ai[2];
		switch (ProjectileRefAi(projectile.type))
		{
		case 0:
			ai0 = AiToLegacy(ai0);
			break;
		case 1:
			ai1 = AiToLegacy(ai1);
			break;
		}
		BitsByte flags = (byte)0;
		BitsByte flags2 = (byte)0;
		flags[0] = ai0 != 0f;
		flags[1] = ai1 != 0f;
		flags2[0] = ai2 != 0f;
		flags[3] = projectile.bannerIdToRespondTo != 0;
		flags[4] = projectile.damage != 0;
		flags[5] = projectile.knockBack != 0f;
		flags[6] = projectile.originalDamage != 0;
		flags[7] = NeedsUUID(projectile.type);
		flags[2] = (byte)flags2 != 0;
		writer.Write((short)key.Index);
		writer.WriteVector2(projectile.position);
		writer.WriteVector2(projectile.velocity);
		writer.Write((byte)projectile.owner);
		writer.Write((short)projectile.type);
		writer.Write(flags);
		if (flags[2])
		{
			writer.Write(flags2);
		}
		if (flags[0])
		{
			writer.Write(ai0);
		}
		if (flags[1])
		{
			writer.Write(ai1);
		}
		if (flags[3])
		{
			writer.Write((ushort)projectile.bannerIdToRespondTo);
		}
		if (flags[4])
		{
			writer.Write((short)projectile.damage);
		}
		if (flags[5])
		{
			writer.Write(projectile.knockBack);
		}
		if (flags[6])
		{
			writer.Write((short)projectile.originalDamage);
		}
		if (flags[7])
		{
			writer.Write((short)key.Index);
		}
		if (flags2[0])
		{
			writer.Write(ai2);
		}
	}

	/// <summary>A 1.4.5.6 ai value (the owner's identity of the other projectile) as the key of our copy of it.</summary>
	public static float AiFromLegacy(int owner, float value)
	{
		int identity = (int)value;
		if (identity < 0 || identity > 1000 || owner < 0 || owner > 255)
		{
			return 0f;
		}
		// Not synced yet (or gone): the default key, which never matches an active projectile. The old number must not be
		// kept: read as key bits it points at random slots.
		return Projectile.LegacyNetKey(owner, identity, create: false);
	}

	/// <summary>
	/// NPCs whose ai holds a server projectile: the Moon Leech Clot (401, ai[1]: its Moon Lord tongue 456) and the Lunatic
	/// Cultist (439, ai[2]: its ritual 490). 1.4.5.6 sends the projectile's slot on the server; we keep keys.
	/// </summary>
	public static void NpcAiFromLegacy(int type, float[] ai)
	{
		switch (type)
		{
		case 401:
			ai[1] = AiFromLegacy(255, ai[1]);
			break;
		case 439:
			ai[2] = AiFromLegacy(255, ai[2]);
			break;
		}
	}

	#endregion

	#region NetModules

	/// <summary>Module id 1.4.5.6 uses for one of ours, or -1 when it has no such module (CreativeUnlocks).</summary>
	public static int ModuleToLegacy(int id)
	{
		if (id < 5 || id >= 13)
		{
			return id;
		}
		if (id == 5)
		{
			return -1;
		}
		return id - 1;
	}

	/// <summary>Our module id for one 1.4.5.6 sent, or -1 when we have no such module (TagEffectState).</summary>
	public static int ModuleFromLegacy(int id)
	{
		if (id < 5 || id >= 13)
		{
			return id;
		}
		if (id == 12)
		{
			return -1;
		}
		return id + 1;
	}

	/// <summary>NetParticles module id (after the mapping above it is 8 on 1.4.5.6, 9 here).</summary>
	public const int ParticlesModuleId = 9;

	/// <summary>Leashed critter type: 1.4.5.7 inserted Aquatic Walker (2) and Turtle (5).</summary>
	public static int LeashedTypeFromLegacy(int type)
	{
		if (!Active || type < 2)
		{
			return type;
		}
		if (type < 4)
		{
			return type + 1;
		}
		return type + 2;
	}

	/// <summary>
	/// Rewrites an outgoing NetModule packet for 1.4.5.6 (in place). False means the server has no such module or content.
	/// </summary>
	public static bool PrepareModulePacket(byte[] data)
	{
		if (!Active || data == null || data.Length < 6)
		{
			return true;
		}
		int id = data[3] | (data[4] << 8);
		if (id == ParticlesModuleId && data[5] >= LegacyParticleCount)
		{
			return false;
		}
		int legacy = ModuleToLegacy(id);
		if (legacy < 0)
		{
			return false;
		}
		data[3] = (byte)legacy;
		data[4] = (byte)(legacy >> 8);
		return true;
	}

	#endregion
}
