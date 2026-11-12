using Terraria.DataStructures;

namespace Terraria;

public partial class Projectile
{
	/// <summary>IsaClient Held Item: shot by an item that hits invincible NPCs.</summary>
	public bool hitsInvincibleNpc;

	/// <summary>Set while an item shoots: every projectile it spawns inherits its "Hits Invincible NPCs".</summary>
	public static bool IsaSpawnHitsInvincible;

	private static readonly ushort[,] legacyKeyGenerations = new ushort[256, 1001];

	/// <summary>
	/// 1.4.5.6 network compatibility (<see cref="IsaClient.Networking.ProtocolCompat" />): the key of the projectile a 1.4.5.6 server
	/// calls (identity, owner). An active projectile with that spawner and slot keeps its key; otherwise <paramref name="create" />
	/// makes a new generation, like 1.4.5.6 taking a fresh slot. Without it, a missing projectile gives the default key.
	/// </summary>
	internal static ProjectileKey LegacyNetKey(int owner, int identity, bool create)
	{
		if (owner < 0 || owner > 255 || identity < 0 || identity > 1000)
		{
			return default(ProjectileKey);
		}
		Projectile projectile = Main.projectile[keyToIndex[owner, identity]];
		if (projectile.active && (int)projectile.key != 0 && projectile.key.Spawner == owner && projectile.key.Index == identity)
		{
			return projectile.key;
		}
		if (!create)
		{
			return default(ProjectileKey);
		}
		int generation;
		if (owner == Main.myPlayer && identity < 1000)
		{
			generation = ++slotGenerations[identity] & 0x3FFF;
		}
		else
		{
			generation = ++legacyKeyGenerations[owner, identity] & 0x3FFF;
		}
		if (generation == 0)
		{
			generation = 1;
		}
		return new ProjectileKey(owner, identity, generation);
	}
}
