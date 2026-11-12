using System;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using IsaClient.Community;
using IsaClient.Core;

namespace IsaClient.Networking;

/// <summary>Finds players hidden by vanish on the server.</summary>
public static class VanishDetection
{
	/// <summary>"Track Players": other players are shown on the map/overlay regardless of PvP or teams.</summary>
	public static bool CanSeePlayer(Player other)
	{
		return PlayerListPanel.PlayerTracker;
	}

	/// <summary>
	/// Hooked at the start of MessageBuffer.GetData on the client, before the packet is applied.
	/// Detects packets from inactive (vanished) players and reports server-side changes to the local player.
	/// </summary>
	public static void OnClientPacket(byte type, byte[] buffer, int start, int length)
	{
		if (Main.netMode != 1 || (!ServerDetection.NotifyPacket4 && !ServerDetection.NotifyServerModifiedPlayer && AwaitingVanishResponseTime <= 0) || Main.gameMenu)
		{
			return;
		}
		try
		{
			using MemoryStream stream = new MemoryStream(buffer, start + 1, Math.Max(0, length - 1), writable: false);
			using BinaryReader reader = new BinaryReader(stream);
			int playerIndex;
			if (type == 65)
			{
				BitsByte flags = reader.ReadByte();
				int teleportType = (flags[0] ? 1 : 0) + (flags[1] ? 2 : 0);
				playerIndex = reader.ReadInt16();
				if (teleportType != 1 && playerIndex >= 0 && playerIndex < 256)
				{
					Vector2 target = reader.ReadVector2();
					DetectInactivePlayer(playerIndex, "TeleportEntity");
					if (playerIndex == Main.myPlayer && ServerDetection.NotifyServerModifiedPlayer)
					{
						ClientChat.Info("Server teleported you to X: " + target.X / 16f + ", Y: " + target.Y / 16f);
					}
				}
				return;
			}
			if (!NetDebug.PlayerPacketNames.TryGetValue(type, out var packetName) || stream.Length < 1)
			{
				return;
			}
			playerIndex = reader.ReadByte();
			DetectInactivePlayer(playerIndex, packetName);
			if (playerIndex != Main.myPlayer || !ServerDetection.NotifyServerModifiedPlayer)
			{
				return;
			}
			switch (type)
			{
			case 12:
				ClientChat.Info("Server spawned your player.");
				break;
			case 16:
				if (Main.ServerSideCharacter)
				{
					int life = reader.ReadInt16();
					int lifeMax = reader.ReadInt16();
					ClientChat.Info("Server modified your HP. (" + life + "/" + lifeMax + ")");
				}
				break;
			case 35:
				ClientChat.Info("Server healed your player for " + reader.ReadInt16() + " HP.");
				break;
			case 42:
				if (Main.ServerSideCharacter)
				{
					int mana = reader.ReadInt16();
					int manaMax = reader.ReadInt16();
					ClientChat.Info("Server modified your mana. (" + mana + "/" + manaMax + ")");
				}
				break;
			case 45:
				ClientChat.Info("Server changed your team.");
				break;
			case 66:
				ClientChat.Info("Server healed your HP for " + reader.ReadInt16() + " HP.");
				break;
			case 117:
				ServerDetection.ReceivedServerDamage = true;
				break;
			case 118:
				ClientChat.Info("Server killed you.");
				break;
			}
		}
		catch
		{
		}
	}

	private static void DetectInactivePlayer(int playerIndex, string packetName)
	{
		if ((!ServerDetection.NotifyPacket4 && AwaitingVanishResponseTime <= 0) || playerIndex < 0 || playerIndex > 255 || playerIndex == Main.myPlayer)
		{
			return;
		}
		Player player = Main.player[playerIndex];
		if (player.active)
		{
			return;
		}
		if (playerIndex != 255 && _joining[playerIndex])
		{
			// Someone connecting: what they send while they do (not a vanished player).
			Syncing(playerIndex, packetName);
			return;
		}
		if (playerIndex == 255)
		{
			ClientChat.Notice("Recieved " + packetName + " packet from the server's player (slot 255)! The server is messing with you!");
			return;
		}
		if (!player.wasDetectedInVanish || AwaitingVanishResponseTime > 0)
		{
			ClientChat.Notice("Recieved " + packetName + " packet from inactive player in slot " + playerIndex + "!");
		}
		player.active = true;
		player.wasDetectedInVanish = true;
		if (AwaitingVanishResponseTime > 0)
		{
			_detectedVanishedPlayers++;
		}
	}

	/// <summary>Frames left to wait for the server's answers to .findvanish.</summary>
	public static int AwaitingVanishResponseTime;

	private static int _detectedVanishedPlayers;

	/// <summary>
	/// .findvanish: hits every empty slot for 1 PvP damage. A server that hides a vanished player still answers
	/// with that player's life, which reveals them (they show up in the Player List as "(Vanished)").
	/// </summary>
	internal static void FindVanishedPlayers()
	{
		if (Main.netMode != 1)
		{
			ClientChat.Error("You can only do this in an online game.");
			return;
		}
		ClientChat.Info("Detecting vanished players...");
		PlayerDeathReason reason = PlayerDeathReason.ByPlayer(255);
		for (int i = 0; i < 255; i++)
		{
			if (!Main.player[i].active)
			{
				NetMessage.SendPlayerHurt(i, reason, 1, -1, critical: true, pvp: true, -1);
			}
		}
		_detectedVanishedPlayers = 0;
		AwaitingVanishResponseTime = 300;
	}

	#region Joining

	/// <summary>The vanilla join line's color.</summary>
	private static readonly Color JoinColor = new Color(255, 240, 20);

	/// <summary>When each slot started connecting (Stopwatch ticks).</summary>
	private static readonly long[] _joinStart = new long[256];

	/// <summary>
	/// Who is connecting right now, by slot. Kept here, not on the player: the game puts a new player object in the
	/// slot when the player becomes active, which would forget it.
	/// </summary>
	private static readonly bool[] _joining = new bool[256];

	/// <summary>Also say what they sync and how long it took (off: only "is joining").</summary>
	[Setting("JoinNotifyDetails")]
	public static bool Detailed = true;

	/// <summary>What each connecting player was already said to be syncing (bits).</summary>
	private static readonly int[] _synced = new int[256];

	/// <summary>Ticks since you entered the world: the players already there when you join aren't announced.</summary>
	private static int _inWorld;

	private static bool Announce => ServerDetection.NotifyPacket4 && _inWorld > 180;

	private static void Line(Player player, string what)
	{
		Main.NewText("Player " + player.name + " " + what + " (ID:" + player.whoAmI + ")", JoinColor);
	}

	/// <summary>A connecting player's data: "is syncing life", once each.</summary>
	private static void Syncing(int index, string packetName)
	{
		int bit;
		string what;
		switch (packetName)
		{
		case "PlayerLifeMana":
			bit = 1;
			what = "life";
			break;
		case "PlayerBuffs":
			bit = 2;
			what = "buffs";
			break;
		case "SyncEquipment":
			bit = 4;
			what = "items";
			break;
		case "PlayerMana":
			bit = 8;
			what = "mana";
			break;
		default:
			return;
		}
		if ((_synced[index] & bit) != 0)
		{
			return;
		}
		_synced[index] |= bit;
		if (Announce && Detailed)
		{
			Line(Main.player[index], "is syncing " + what);
		}
	}

	#endregion

	/// <summary>Entering a world: nobody is connecting yet, and the ones already here aren't announced.</summary>
	public static void ResetJoins()
	{
		_inWorld = 0;
		for (int i = 0; i < 256; i++)
		{
			Main.player[i].joining = false;
			_joining[i] = false;
			_synced[i] = 0;
		}
	}

	internal static void UpdateVanishSearch()
	{
		_inWorld = Main.gameMenu ? 0 : Math.Min(_inWorld + 1, 100000);
		if (AwaitingVanishResponseTime > 0 && Netplay.Connection.TimeOutTimer < 40)
		{
			AwaitingVanishResponseTime--;
			if (AwaitingVanishResponseTime == 0)
			{
				ClientChat.Info((_detectedVanishedPlayers > 0) ? ("Detected " + _detectedVanishedPlayers + " players in vanish!") : "Did not detect any vanished players.");
				_detectedVanishedPlayers = 0;
			}
		}
	}

	/// <summary>Hooked in MessageBuffer case 4 (SyncPlayer) on the client, after the name was read.</summary>
	public static void OnSyncPlayerReceived(Player player)
	{
		if (Main.netMode != 1 || player.whoAmI == Main.myPlayer)
		{
			return;
		}
		if (player.whoAmI == 255)
		{
			if (!player.active && ServerDetection.NotifyPacket4)
			{
				ClientChat.Notice("Recieved SyncPlayer packet from the server's player (slot 255)! The server is messing with you!");
			}
			return;
		}
		// Only someone connecting after you're in (the players already here are sent the same way while you join).
		if (!player.active && !_joining[player.whoAmI] && _inWorld > 180)
		{
			_joining[player.whoAmI] = true;
			player.joining = true;
			player.spawned = false;
			_joinStart[player.whoAmI] = System.Diagnostics.Stopwatch.GetTimestamp();
			_synced[player.whoAmI] = 0;
			if (Announce)
			{
				Line(player, "is joining");
			}
		}
		else if (_joining[player.whoAmI] && !player.active)
		{
			player.playerSyncsWhileNotActive++;
		}
		// Still hidden long after a normal connection would have finished: likely vanished.
		long since = (System.Diagnostics.Stopwatch.GetTimestamp() - _joinStart[player.whoAmI]) / System.Diagnostics.Stopwatch.Frequency;
		if (ServerDetection.NotifyPacket4 && player.playerSyncsWhileNotActive > 1 && since > 15 && !player.wasDetectedInVanish && _joining[player.whoAmI] && !player.active && !player.spawned)
		{
			ClientChat.Notice(player.name + " sent multiple SyncPlayer packets, but hasn't spawned yet!");
			player.wasDetectedInVanish = true;
		}
	}

	/// <summary>Hooked in MessageBuffer case 12 (PlayerSpawn).</summary>
	public static void OnPlayerSpawnReceived(Player player)
	{
		if (player.whoAmI >= 0 && player.whoAmI < 255 && _joining[player.whoAmI] && Announce && Detailed)
		{
			long ms = (System.Diagnostics.Stopwatch.GetTimestamp() - _joinStart[player.whoAmI]) * 1000 / System.Diagnostics.Stopwatch.Frequency;
			Line(player, "completed connection in " + ms + " ms");
		}
		player.spawned = true;
		player.joining = false;
		if (player.whoAmI >= 0 && player.whoAmI < 256)
		{
			_joining[player.whoAmI] = false;
		}
	}

	/// <summary>Hooked in MessageBuffer case 14 when a player is set inactive.</summary>
	public static void OnPlayerDeactivated(int playerIndex)
	{
		Player player = Main.player[playerIndex];
		if (_joining[playerIndex] && Announce)
		{
			Line(player, "stopped connecting");
		}
		player.wasDetectedInVanish = false;
		player.playerSyncsWhileNotActive = 0;
		player.isProjectileSpammer = false;
		player.spammedProjectileCount = 0;
		player.blockProjectileRecieveTime = 0;
		player.isSoundSpammer = false;
		player.spammedSoundCount = 0;
		player.blockSoundRecieveTime = 0;
		player.isParticleSpammer = false;
		player.spammedParticleCount = 0;
		player.blockParticleRecieveTime = 0;
		player.isNebulaSpammer = false;
		player.spammedNebulaCount = 0;
		player.blockNebulaRecieveTime = 0;
		player.joining = false;
		_joining[playerIndex] = false;
		if (Spectate.SpectatePlayerTarget == playerIndex)
		{
			Spectate.SpectatePlayerTarget = -1;
			Spectate.ShowSpectatedInventory = false;
		}
	}

	#region Chat commands

	/// <summary>.findvanish / .detvanish</summary>
	internal static void FindVanishCommand(CommandContext command)
	{
		FindVanishedPlayers();
	}

	#endregion
}
