namespace Terraria.DataStructures;

public partial class PlayerDeathReason
{
	/// <summary>The player that caused the death, or -1 (Lucy death messages).</summary>
	public int SourcePlayerIndexForMods => _sourcePlayerIndex;

	/// <summary>The NPC behind it (-1 for none): TerraAngel's old presence mark is a 1-damage hurt "by" NPC 203.</summary>
	internal int SourceNPCIndex => _sourceNPCIndex;

	/// <summary>.kms pr: killed by a projectile type, without a real projectile.</summary>
	public static PlayerDeathReason ByProjectileType(int playerIndex, int projectileType)
	{
		PlayerDeathReason playerDeathReason = new PlayerDeathReason
		{
			_sourcePlayerIndex = playerIndex,
			_sourceProjectileLocalIndex = 9999,
			_sourceProjectileType = projectileType
		};
		if (playerIndex >= 0 && playerIndex < 255)
		{
			playerDeathReason._sourceItemType = Main.player[playerIndex].inventory[Main.player[playerIndex].selectedItem].type;
			playerDeathReason._sourceItemPrefix = Main.player[playerIndex].inventory[Main.player[playerIndex].selectedItem].prefix;
		}
		return playerDeathReason;
	}
}
