using Terraria;
using Terraria.ID;

namespace IsaClient.Menu;

/// <summary>
/// Which personal storages of other players the server has sent (Piggy Bank, Safe, Defender's Forge, Void Vault). A
/// vanilla server only relays the Void Vault; others (SSC, plugins) send all of them. Marked from the inventory slot
/// packet, per player and per character name, so a new player in the same slot starts unmarked.
/// </summary>
public static class StorageSync
{
	private static readonly bool[,] _received = new bool[256, 5];

	private static readonly string[] _names = new string[256];

	/// <summary>Called for every inventory slot the server sends about a player.</summary>
	public static void OnSlot(int player, int slotId)
	{
		if (player < 0 || player >= 255 || Main.netMode != 1 || player == Main.myPlayer)
		{
			return;
		}
		int storage = StorageOf(slotId);
		if (storage <= 0)
		{
			return;
		}
		string name = Main.player[player]?.name ?? "";
		if (_names[player] != name)
		{
			_names[player] = name;
			for (int i = 0; i < 5; i++)
			{
				_received[player, i] = false;
			}
		}
		_received[player, storage] = true;
	}

	/// <summary>True when the server sent at least one slot of this storage (1 Piggy Bank ... 4 Void Vault) for this player.</summary>
	public static bool Received(int player, int storage)
	{
		if (player == Main.myPlayer || storage == 0)
		{
			return true;
		}
		if (player < 0 || player >= 255 || storage < 0 || storage > 4)
		{
			return false;
		}
		return _received[player, storage] && _names[player] == (Main.player[player]?.name ?? "");
	}

	private static int StorageOf(int slotId)
	{
		if (slotId >= PlayerItemSlotID.Bank1_0 && slotId < PlayerItemSlotID.Bank1_0 + 200)
		{
			return 1;
		}
		if (slotId >= PlayerItemSlotID.Bank2_0 && slotId < PlayerItemSlotID.Bank2_0 + 200)
		{
			return 2;
		}
		if (slotId >= PlayerItemSlotID.Bank3_0 && slotId < PlayerItemSlotID.Bank3_0 + 200)
		{
			return 3;
		}
		if (slotId >= PlayerItemSlotID.Bank4_0 && slotId < PlayerItemSlotID.Bank4_0 + 200)
		{
			return 4;
		}
		return 0;
	}
}
