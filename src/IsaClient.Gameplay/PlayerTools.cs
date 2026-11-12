using Microsoft.Xna.Framework;
using Terraria;
using IsaClient.Core;

namespace IsaClient.Gameplay;

/// <summary>Player tools: ghost mode and any item in any slot.</summary>
public static class PlayerTools
{
	/// <summary>Any item can go in any equipment, coin or ammo slot.</summary>
	[Setting("AnyItemAnySlot")]
	public static bool AnyItemAnySlot;

	/// <summary>True while the player is a ghost through .ghost (alive), so ghost keeps normal inventory/item input.</summary>
	public static bool GhostCheat;

	/// <summary>.ghost: turns the player into a ghost (and back, respawning if needed).</summary>
	public static void ToggleGhost()
	{
		Player player = Main.LocalPlayer;
		if (player.ghost && player.dead)
		{
			ClientChat.Info("Respawned and returned to normal!");
			ClientChat.Notice("You were dead when unghosting, so you have been respawned.");
			ClientChat.Notice("This may cause issues or desyncs on servers, and could be picked up on. Use with caution.");
			player.dead = false;
			player.ghost = false;
			GhostCheat = false;
			Player.deadForGood = false;
			player.Spawn(PlayerSpawnContext.ReviveFromDeath);
			return;
		}
		if (player.dead)
		{
			player.dead = false;
			player.Spawn(PlayerSpawnContext.ReviveFromDeath);
		}
		SetGhost(!player.ghost, functional: true);
		ClientChat.Info(player.ghost ? "Turned into ghost." : "Reverted to normal");
	}

	/// <summary>
	/// Ghost on or off without chat lines (plugins' ghost switches use it). A functional ghost keeps its inventory,
	/// hotbar and items working, like .ghost; a plain one is the game's ghost.
	/// </summary>
	public static void SetGhost(bool ghost, bool functional)
	{
		Player player = Main.LocalPlayer;
		bool changed = player.ghost != ghost;
		player.ghost = ghost;
		GhostCheat = ghost && functional;
		if (!changed)
		{
			return;
		}
		if (ghost)
		{
			// The ghost update never runs the mount code, so a mount kept from before could never be dismounted.
			if (player.mount.Active)
			{
				player.mount.Dismount(player);
			}
			player.RemoveAllGrapplingHooks();
			player.velocity = Vector2.Zero;
		}
		// Tell the server right away: its NPCs stop targeting ghosts (and start again when you come back).
		if (Main.netMode == 1)
		{
			NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
		}
	}

	/// <summary>Hooked at the start of ItemSlot.PickItemMovementAction: -1 = use the vanilla rules.</summary>
	public static int AnySlotMovementAction(int context, Item checkItem)
	{
		if (!AnyItemAnySlot || checkItem == null)
		{
			return -1;
		}
		switch (context)
		{
		case 1:
		case 2:
			return 0;
		case 8:
		case 9:
		case 10:
		case 11:
		case 16:
		case 17:
		case 18:
		case 19:
		case 20:
		case 23:
		case 24:
		case 26:
		case 38:
		case 39:
			return 1;
		default:
			return -1;
		}
	}

	#region Chat commands

	/// <summary>.ghost / .ghostmode / .gh / .g</summary>
	internal static void GhostModeCommand(CommandContext command)
	{
		ToggleGhost();
	}

	/// <summary>.anyslot</summary>
	internal static void AnySlotCommand(CommandContext command)
	{
		AnyItemAnySlot = !AnyItemAnySlot;
		ClientChat.Info("Any Item in Any Slot " + (AnyItemAnySlot ? "En" : "Dis") + "abled");
		ClientSettings.Save();
	}

	#endregion
}
