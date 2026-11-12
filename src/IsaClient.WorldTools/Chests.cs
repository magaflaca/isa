using System.Threading.Tasks;
using Terraria;
using Terraria.ID;
using IsaClient.Core;

namespace IsaClient.WorldTools;

/// <summary>Chest tools: local chests, long names, locking/unlocking, filling, locked chests.</summary>
public static class Chests
{
	public static bool FuckChestName;

	public static bool LocalChests;

	/// <summary>.fillchest / .fillc</summary>
	internal static void FillChestCommand(CommandContext command)
	{
		string[] array = command.Parts;
		Player player = Main.LocalPlayer;
		if (player.chest < 0)
		{
			ClientChat.Error("You need to be in a chest for this.");
			return;
		}
		if (array.Length <= 1)
		{
			ClientChat.Error("No Item ID provided (-1 = random items).");
			return;
		}
		if (!int.TryParse(array[1], out var itemId) || itemId >= ItemID.Count || itemId < -1 || itemId == 0)
		{
			ClientChat.Error("Invalid Item ID.");
			return;
		}
		Chest chest = Main.chest[player.chest];
		for (int i = 0; i < chest.item.Length; i++)
		{
			chest.item[i].SetDefaults((itemId == -1) ? Main.rand.Next(1, ItemID.Count) : itemId);
			chest.item[i].stack = chest.item[i].maxStack;
			if (Main.netMode == 1)
			{
				NetMessage.SendData(32, -1, -1, null, player.chest, i);
			}
		}
		ClientChat.Info("Filled chest with " + ((itemId == -1) ? "random items" : chest.item[0].Name) + ".");
	}

	internal static void LockOrUnlockAllChests(bool unlock)
	{
		Task.Run(delegate
		{
			int count = 0;
			for (int i = 0; i < Main.maxTilesX; i++)
			{
				for (int j = 0; j < Main.maxTilesY; j++)
				{
					Tile tile = Main.tile[i, j];
					if (tile == null || !tile.active() || (tile.type != 21 && tile.type != 467))
					{
						continue;
					}
					if (!unlock && Chest.Lock(i, j))
					{
						NetMessage.SendData(52, -1, -1, null, 0, 3f, i, j);
						count++;
					}
					else if (unlock && Chest.Unlock(i, j))
					{
						NetMessage.SendData(52, -1, -1, null, 0, 1f, i, j);
						count++;
					}
				}
			}
			ClientChat.Info((unlock ? "Unlocked " : "Locked ") + count + " chests in the world." + ((Main.netMode == 1) ? " Reveal the map first to reach every chest." : ""));
		});
	}

	[Setting("OpenLockedChests", Remembered = true)]
	public static bool OpenLockedChests;

	[Setting("FixFakeNewChest", Remembered = true)]
	public static bool BugFixFakeNewChest;

	/// <summary>"Fix Fake_NewChest": invalid dresser styles fall back to style 0 instead of crashing the hover.</summary>
	public static int SafeDresserStyle(int style)
	{
		if (style >= 0 && style < Lang.dresserType.Length && style < Chest.dresserTypeToIcon.Length)
		{
			return style;
		}
		return BugFixFakeNewChest ? 0 : style;
	}

	#region Chat commands

	/// <summary>.openlocked / .unlockedchests</summary>
	internal static void UnlockedChestsCommand(CommandContext command)
	{
		OpenLockedChests = !OpenLockedChests;
		ClientChat.Info("Open Locked Chests " + (OpenLockedChests ? "En" : "Dis") + "abled");
	}

	/// <summary>.relockchests</summary>
	internal static void RelockChestsCommand(CommandContext command)
	{
		LockOrUnlockAllChests(unlock: false);
	}

	/// <summary>.unlockchests</summary>
	internal static void UnlockChestsCommand(CommandContext command)
	{
		LockOrUnlockAllChests(unlock: true);
	}

	/// <summary>.fuckchest / .extendchestname</summary>
	internal static void ExtendChestNameCommand(CommandContext command)
	{
		FuckChestName = !FuckChestName;
		ClientChat.Info("Extended chest names " + (FuckChestName ? "en" : "dis") + "abled.");
	}

	/// <summary>.lchest</summary>
	internal static void LocalChestsCommand(CommandContext command)
	{
		LocalChests = !LocalChests;
		ClientChat.Info((!LocalChests) ? "Chest contents are now requested from the server." : "Chest contents are no longer requested from the server.");
	}

	#endregion
}
