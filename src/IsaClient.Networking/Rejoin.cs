using System;
using System.Threading.Tasks;
using Terraria;
using Terraria.IO;
using IsaClient.Core;

namespace IsaClient.Networking;

/// <summary>Rejoining the current server (.rj) and auto-reconnect after a kick or lost connection.</summary>
public static class Rejoin
{
	/// <summary>.rj: reconnect to the current server with the same character.</summary>
	public static async void RejoinServer(string message = "Rejoining!")
	{
		if (Main.netMode != 1)
		{
			ClientChat.Error("You can only do this in an online game.");
			return;
		}
		string serverIp = Netplay.ServerIPText;
		int port = Netplay.ListenPort;
		PlayerFileData playerFile = Main.ActivePlayerFileData;
		WorldGen.SaveAndQuit();
		await Task.Delay(500);
		for (int i = 0; i < 100 && !Main.gameMenu; i++)
		{
			await Task.Delay(100);
		}
		Netplay.ListenPort = port;
		Main.instance.AutoJoin(serverIp);
		Main.menuMultiplayer = true;
		Main.menuServer = false;
		Main.SelectPlayer(playerFile);
		Main.statusText = message;
	}

	[Setting("AutoRejoin", Remembered = true)]
	public static bool AutoRejoin;

	private static string _rejoinAddress;

	private static int _rejoinPort;

	private static PlayerFileData _rejoinPlayer;

	internal static int RejoinTimer = -1;

	internal static int RejoinAttempts;

	/// <summary>Netplay: the connection ended while playing (kick, lost connection, time out). Rejoins a few seconds later.</summary>
	public static void QueueAutoRejoin()
	{
		if (!AutoRejoin || Main.netMode != 1 || Main.ActivePlayerFileData == null)
		{
			return;
		}
		if (RejoinAttempts >= 5)
		{
			return;
		}
		_rejoinAddress = Netplay.ServerIPText;
		_rejoinPort = Netplay.ListenPort;
		_rejoinPlayer = Main.ActivePlayerFileData;
		RejoinTimer = 180;
		RejoinAttempts++;
	}

	internal static void UpdateAutoRejoin()
	{
		if (!Main.gameMenu)
		{
			if (RejoinTimer < 0 && Main.netMode == 1 && Netplay.Connection != null && Netplay.Connection.State >= 10)
			{
				// Back in a world: allow the next disconnect to rejoin again.
				RejoinAttempts = 0;
			}
			return;
		}
		if (RejoinTimer < 0)
		{
			return;
		}
		// Leaving the "connection lost" screen (Back) cancels it.
		if (!AutoRejoin || string.IsNullOrEmpty(_rejoinAddress) || _rejoinPlayer == null || (Main.menuMode != 14 && Main.menuMode != 15))
		{
			RejoinTimer = -1;
			return;
		}
		if (RejoinTimer > 0)
		{
			RejoinTimer--;
			Main.statusText = "Reconnecting in " + (RejoinTimer / 60 + 1) + "... (" + RejoinAttempts + "/5, .autorejoin to stop)";
			return;
		}
		RejoinTimer = -1;
		try
		{
			if (_rejoinPlayer.Player == null || _rejoinPlayer.Player.loadStatus != 0)
			{
				return;
			}
			Netplay.ListenPort = _rejoinPort;
			Main.instance.AutoJoin(_rejoinAddress);
			Main.menuMultiplayer = true;
			Main.menuServer = false;
			Main.SelectPlayer(_rejoinPlayer);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	#region Chat commands

	/// <summary>.arc / .autorejoin</summary>
	internal static void AutoRejoinCommand(CommandContext command)
	{
		AutoRejoin = !AutoRejoin;
		RejoinTimer = -1;
		RejoinAttempts = 0;
		ClientChat.Info("Auto-reconnect on kick/connection lost " + (AutoRejoin ? "en" : "dis") + "abled");
	}

	/// <summary>.rj</summary>
	internal static void RejoinCommand(CommandContext command)
	{
		RejoinServer();
	}

	#endregion
}
