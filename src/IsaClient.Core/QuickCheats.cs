using IsaClient.Community;
using IsaClient.Gameplay;
using IsaClient.Movement;
using IsaClient.Networking;
using IsaClient.Rendering;

namespace IsaClient.Core;

/// <summary>.qc / .quickcheat: turns most cheats on at once.</summary>
public static class QuickCheats
{
	/// <summary>.qc / .quickcheat: turns most cheats on at once.</summary>
	public static void EnableMost()
	{
		PlayerCheats.GodMode = true;
		PlayerCheats.AntiDeathGod = true;
		PlayerCheats.InfiniteMana = true;
		PlayerCheats.JourneyMenu = true;
		PlayerCheats.ExtendedPlacementRange = true;
		PlayerCheats.InfiniteMinions = true;
		PlayerCheats.InfiniteAmmo = true;
		PlayerCheats.IgnoreLiquids = true;
		PlayerCheats.IgnoreDamageBlocks = true;
		PlayerCheats.GrappleMounted = true;
		PlayerCheats.DeathItemDropMode = 2;
		PlayerCheats.PersistBuffsMode = 1;
		PlayerCheats.UnrestrictTeleportRod = true;
		PlayerCheats.IgnoreCC = true;
		ServerDetection.NotifyPacket4 = true;
		PlayerCheats.NoSwapRestriction = true;
		TeleportTools.MapTeleportation = true;
		Cosmetics.RgbPlayerOutline = true;
		PlayerListPanel.PlayerTracker = true;
		PacketPacing.TilePacketMaxMode = false;
		WorldView.NoScreenClamp = true;
		WorldView.KeepThinVanillaBorder = true;
		PlayerCheats.SuppressCreativeShock = true;
		PlayerCheats.SuppressHorrified = true;
		PlayerCheats.ExtraAccSlots = true;
		FullbrightLighting.Set(0.5f);
	}

	#region Chat commands

	/// <summary>.quickcheat / .qc</summary>
	internal static void QuickCheatCommand(CommandContext command)
	{
		EnableMost();
		ClientChat.Info("Enabled most cheats.");
	}

	#endregion
}
