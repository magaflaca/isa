using System;
using System.Threading;
using System.Threading.Tasks;
using Terraria;
using Terraria.ID;
using IsaClient.Core;

namespace IsaClient.Networking;

/// <summary>What the server is (TShock, server-side characters) and blocking server changes to your player.</summary>
public static class ServerDetection
{
	/// <summary>MessageBuffer: .blockssc. Answers the server's change with your own data and drops it.</summary>
	public static bool BlockServerChange(int playerIndex, string what, Action resend)
	{
		if (!BlockServerControl || !Main.ServerSideCharacter || Main.netMode != 1 || playerIndex != Main.myPlayer)
		{
			return false;
		}
		if (NotifyServerModifiedPlayer)
		{
			ClientChat.Notice("Blocked server from modifying your " + what + ".");
		}
		resend?.Invoke();
		return true;
	}

	[Setting("NotifyServerUpdatedPlayer", Remembered = true)]
	public static bool NotifyServerModifiedPlayer;

	[Setting("NotifyConnectingPlayer", Remembered = true)]
	public static bool NotifyPacket4;

	public static bool BlockServerControl;

	public static bool SendingBlockedSSC;

	public static bool DidTShockDisableYou;

	public static int TShockDisableTime = 0;

	public static int AwaitTshockResponseTime = 0;

	public static bool OnTshockServer;

	public static int AwaitingTshockPvpResponseTime = 0;

	public static async void SendInventoryManually(bool instant = false)
	{
		await Task.Run(delegate
		{
			if (!SendingBlockedSSC)
			{
				SendingBlockedSSC = true;
				if (instant)
				{
					NetMessage.SendData(16, -1, -1, null, Main.myPlayer);
					NetMessage.SendData(42, -1, -1, null, Main.myPlayer);
				}
				NetMessage.SendData(147, -1, -1, null, Main.myPlayer, Main.player[Main.myPlayer].CurrentLoadoutIndex);
				for (int i = 0; i < 59; i++)
				{
					NetMessage.SendData(5, -1, -1, null, Main.myPlayer, PlayerItemSlotID.Inventory0 + i, (int)Main.player[Main.myPlayer].inventory[i].prefix);
					if (!instant)
					{
						Thread.Sleep(40);
					}
				}
				for (int j = 0; j < Main.player[Main.myPlayer].armor.Length; j++)
				{
					NetMessage.SendData(5, -1, -1, null, Main.myPlayer, PlayerItemSlotID.Armor0 + j, (int)Main.player[Main.myPlayer].armor[j].prefix);
					if (!instant)
					{
						Thread.Sleep(40);
					}
				}
				for (int k = 0; k < Main.player[Main.myPlayer].dye.Length; k++)
				{
					NetMessage.SendData(5, -1, -1, null, Main.myPlayer, PlayerItemSlotID.Dye0 + k, (int)Main.player[Main.myPlayer].dye[k].prefix);
					if (!instant)
					{
						Thread.Sleep(40);
					}
				}
				NetMessage.SyncOnePlayer_ItemArray(Main.myPlayer, -1, -1, Main.player[Main.myPlayer].miscEquips, PlayerItemSlotID.Misc0);
				if (!instant)
				{
					Thread.Sleep(40);
				}
				NetMessage.SyncOnePlayer_ItemArray(Main.myPlayer, -1, -1, Main.player[Main.myPlayer].miscDyes, PlayerItemSlotID.MiscDye0);
				if (!instant)
				{
					Thread.Sleep(40);
				}
				NetMessage.SyncOnePlayer_ItemArray(Main.myPlayer, -1, -1, Main.player[Main.myPlayer].Loadouts[0].Armor, PlayerItemSlotID.Loadout1_Armor_0);
				if (!instant)
				{
					Thread.Sleep(40);
				}
				NetMessage.SyncOnePlayer_ItemArray(Main.myPlayer, -1, -1, Main.player[Main.myPlayer].Loadouts[0].Dye, PlayerItemSlotID.Loadout1_Dye_0);
				if (!instant)
				{
					Thread.Sleep(40);
				}
				NetMessage.SyncOnePlayer_ItemArray(Main.myPlayer, -1, -1, Main.player[Main.myPlayer].Loadouts[1].Armor, PlayerItemSlotID.Loadout2_Armor_0);
				if (!instant)
				{
					Thread.Sleep(40);
				}
				NetMessage.SyncOnePlayer_ItemArray(Main.myPlayer, -1, -1, Main.player[Main.myPlayer].Loadouts[1].Dye, PlayerItemSlotID.Loadout2_Dye_0);
				if (!instant)
				{
					Thread.Sleep(40);
				}
				NetMessage.SyncOnePlayer_ItemArray(Main.myPlayer, -1, -1, Main.player[Main.myPlayer].Loadouts[2].Armor, PlayerItemSlotID.Loadout3_Armor_0);
				if (!instant)
				{
					Thread.Sleep(40);
				}
				NetMessage.SyncOnePlayer_ItemArray(Main.myPlayer, -1, -1, Main.player[Main.myPlayer].Loadouts[2].Dye, PlayerItemSlotID.Loadout3_Dye_0);
				SendingBlockedSSC = false;
				if (NotifyServerModifiedPlayer)
				{
					ClientChat.Info("Finished resending inventory.");
				}
			}
		});
	}

	public static bool ReceivedServerDamage;

	#region Chat commands

	/// <summary>.notify</summary>
	internal static void NotifyCommand(CommandContext command)
	{
		NotifyServerModifiedPlayer = !NotifyServerModifiedPlayer;
		ClientChat.Info("Messages notifying when server modifies your player " + (NotifyServerModifiedPlayer ? "en" : "dis") + "abled.");
	}

	/// <summary>.detsp</summary>
	internal static void DetectSpyingCommand(CommandContext command)
	{
		NotifyPacket4 = !NotifyPacket4;
		ClientChat.Info("Detecting spying players " + (NotifyPacket4 ? "en" : "dis") + "abled.");
	}

	/// <summary>.blockssc</summary>
	internal static void BlockSscCommand(CommandContext command)
	{
		BlockServerControl = !BlockServerControl;
		ClientChat.Info(BlockServerControl ? "The server can no longer change your character." : "The server can change your character again.");
	}

	/// <summary>.tshock</summary>
	internal static void TshockCommand(CommandContext command)
	{
		ClientChat.Info((OnTshockServer ? IsaClient.Languages.L10n.T("You are on a TShock server.") : IsaClient.Languages.L10n.T("You are not on a TShock server.")) + (DidTShockDisableYou ? IsaClient.Languages.L10n.F(" (disabled for {0} more ticks)", TShockDisableTime) : ""));
	}

	#endregion
}
