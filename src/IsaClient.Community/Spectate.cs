using Microsoft.Xna.Framework;
using Terraria;
using IsaClient.Movement;

namespace IsaClient.Community;

/// <summary>Spectating players from the Player List, optionally with their inventory in your HUD.</summary>
public static class Spectate
{
	public static bool SpectatingPlayer;

	public static int SpectatePlayerTarget = -1;

	/// <summary>Right click on Spectate: your HUD (hotbar, inventory, buffs, info accessories) shows the spectated player's.</summary>
	public static bool ShowSpectatedInventory;

	public static bool IsSpectating
	{
		get
		{
			int t = SpectatePlayerTarget;
			if (Main.gameMenu || t < 0 || t >= 255 || t == Main.myPlayer)
			{
				return false;
			}
			return Main.player[t].active;
		}
	}

	public static bool IsInsideWorld(Player p)
	{
		return !(p.position.X < Main.leftWorld) && !(p.position.Y < Main.topWorld) && !(p.BottomRight.X > Main.rightWorld) && !(p.BottomRight.Y > Main.bottomWorld);
	}

	/// <summary>Per tick: stop spectating someone who left or whose position is not known.</summary>
	internal static void UpdateSpectate()
	{
		if (SpectatePlayerTarget == -1)
		{
			ShowSpectatedInventory = false;
			return;
		}
		if (FreeCamera.FreeCam || SpectatePlayerTarget < 0 || SpectatePlayerTarget >= 255 || SpectatePlayerTarget == Main.myPlayer || !Main.player[SpectatePlayerTarget].active || !IsInsideWorld(Main.player[SpectatePlayerTarget]))
		{
			SpectatePlayerTarget = -1;
			ShowSpectatedInventory = false;
		}
	}

	/// <summary>Spectate button (left = camera only, right = camera + their inventory in your HUD). Clicking the spectated player again stops.</summary>
	public static void ToggleSpectate(int index, bool withInventory)
	{
		if (SpectatePlayerTarget == index || index == Main.myPlayer || index < 0 || index >= 255)
		{
			SpectatePlayerTarget = -1;
			ShowSpectatedInventory = false;
			return;
		}
		Player p = Main.player[index];
		if (!p.active || !IsInsideWorld(p))
		{
			return;
		}
		SpectatePlayerTarget = index;
		ShowSpectatedInventory = withInventory;
		FreeCamera.FreeCam = false;
		FreeCamera.FreeCam2 = false;
	}

	/// <summary>Teleports to a player (position + no fall damage + immediate sync).</summary>
	public static void TeleportToPlayer(int index)
	{
		if (index < 0 || index >= 255 || index == Main.myPlayer)
		{
			return;
		}
		Player target = Main.player[index];
		if (!target.active || !IsInsideWorld(target))
		{
			return;
		}
		Player local = Main.LocalPlayer;
		local.position = target.position;
		local.oldPosition = local.position;
		local.velocity = Vector2.Zero;
		local.fallStart = (int)local.Bottom.Y / 16;
		TeleportTools.LastCenter = local.Center;
		local.noFallDmg = true;
		NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
	}

	/// <summary>The player whose HUD is shown instead of yours, or null.</summary>
	public static Player SpectatedHudOwner
	{
		get
		{
			if (!ShowSpectatedInventory || !IsSpectating)
			{
				return null;
			}
			return Main.player[SpectatePlayerTarget];
		}
	}

	public struct HudSwap
	{
		public Player Local;

		public Player Target;

		public bool TargetMouseInterface;

		public int TargetChest;

		public bool MouseLeft;

		public bool MouseLeftRelease;

		public bool MouseRight;

		public bool MouseRightRelease;
	}

	/// <summary>
	/// Draws the vanilla HUD pieces with the spectated player in your slot, read-only: the mouse buttons are hidden from the
	/// game while they draw, so hovering shows tooltips but nothing can be clicked, moved, toggled or synced. Must be paired
	/// with <see cref="EndSpectatedHud"/> in a finally block.
	/// </summary>
	public static bool BeginSpectatedHud(out HudSwap swap)
	{
		swap = default(HudSwap);
		Player target = SpectatedHudOwner;
		if (target == null)
		{
			return false;
		}
		swap.Local = Main.player[Main.myPlayer];
		swap.Target = target;
		swap.TargetMouseInterface = target.mouseInterface;
		swap.TargetChest = target.chest;
		// Their open chest is not loaded on this client; the inventory is drawn without it.
		target.chest = -1;
		swap.MouseLeft = Main.mouseLeft;
		swap.MouseLeftRelease = Main.mouseLeftRelease;
		swap.MouseRight = Main.mouseRight;
		swap.MouseRightRelease = Main.mouseRightRelease;
		target.mouseInterface = false;
		Main.mouseLeft = false;
		Main.mouseLeftRelease = false;
		Main.mouseRight = false;
		Main.mouseRightRelease = false;
		Main.player[Main.myPlayer] = target;
		return true;
	}

	public static void EndSpectatedHud(ref HudSwap swap)
	{
		if (swap.Local == null)
		{
			return;
		}
		Main.player[Main.myPlayer] = swap.Local;
		if (swap.Target.mouseInterface)
		{
			swap.Local.mouseInterface = true;
		}
		swap.Target.mouseInterface = swap.TargetMouseInterface;
		swap.Target.chest = swap.TargetChest;
		Main.mouseLeft = swap.MouseLeft;
		Main.mouseLeftRelease = swap.MouseLeftRelease;
		Main.mouseRight = swap.MouseRight;
		Main.mouseRightRelease = swap.MouseRightRelease;
		if (Main.cursorOverride == 2 && Main.HoverItem != null && !Main.HoverItem.IsAir)
		{
			// The favorite key with chat open still shares the hovered item in chat.
			Terraria.UI.ItemSlot.LeftClickShareInChat(Main.HoverItem);
		}
		swap.Local = null;
	}
}
