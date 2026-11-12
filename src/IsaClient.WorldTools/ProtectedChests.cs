using Terraria.Audio;
using Terraria.UI;
using IsaClient.Core;
using Terraria;

namespace IsaClient.WorldTools;

/// <summary>
/// View Protected Chests (Render, .rochest), ported from WldFlow: when you ask the server to open a chest, the copy of
/// it this client already has (Main.chest) opens right away, read-only. If the server then really opens it for you
/// (packet 33), the same window goes back to normal; a protected chest simply stays in its read-only view.
/// While read-only nothing can change it: slot clicks, Loot All, Deposit All, Quick Stack, Restock, rename and sort
/// are disabled, and the chest packets 32/33 are never sent. Nothing is requested that the server did not already
/// send: what you see is the last version this client received.
/// </summary>
public static class ProtectedChests
{
	[Setting("ViewProtectedChests")]
	public static bool Enabled = true;

	private static int _readOnlyChest = -1;

	public static bool ReadOnlyActive => _readOnlyChest >= 0 && Main.netMode == 1 && Main.LocalPlayer != null && Main.LocalPlayer.chest == _readOnlyChest;

	/// <summary>Outgoing packet 31 (open chest at x, y).</summary>
	public static void OnOpenRequest(int x, int y)
	{
		if (!Enabled || Main.netMode != 1 || Main.LocalPlayer == null)
		{
			return;
		}
		int chestIndex = Chest.FindChest(x, y);
		if (chestIndex < 0 || chestIndex >= Main.chest.Length || Main.chest[chestIndex] == null)
		{
			return;
		}
		Player player = Main.LocalPlayer;
		int previous = player.chest;
		_readOnlyChest = chestIndex;
		player.chest = chestIndex;
		player.chestX = x;
		player.chestY = y;
		Main.editChest = false;
		Main.playerInventory = true;
		Main.PipsUseGrid = false;
		Main.stackSplit = 600;
		ItemSlot.SetGlowForChest(Main.chest[chestIndex]);
		SoundEngine.PlaySound((previous == -1) ? 10 : 12);
	}

	/// <summary>Incoming packet 33: the server opened this chest for you, so it is no longer read-only.</summary>
	public static void OnServerOpened(int chestIndex)
	{
		if (chestIndex >= 0 && chestIndex == _readOnlyChest)
		{
			_readOnlyChest = -1;
		}
	}

	public static void Update()
	{
		if (_readOnlyChest >= 0 && (Main.LocalPlayer == null || Main.LocalPlayer.chest != _readOnlyChest || Main.netMode != 1))
		{
			_readOnlyChest = -1;
		}
	}

	public static bool SuppressPacket(int msgType)
	{
		return (msgType == 32 || msgType == 33) && ReadOnlyActive;
	}

	public static void Toggle()
	{
		Enabled = !Enabled;
		if (!Enabled)
		{
			_readOnlyChest = -1;
		}
		ClientSettings.Save();
		ClientChat.Info("View Protected Chests " + (Enabled ? "enabled" : "disabled") + ".");
	}

	#region Chat commands

	/// <summary>.rochest / .readonlychests / .protectedchests</summary>
	internal static void ProtectedChestsCommand(CommandContext command)
	{
		Toggle();
	}

	#endregion
}
