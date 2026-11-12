using System;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using IsaClient.Core;
using IsaClient.Networking;

namespace IsaClient.Gameplay;

/// <summary>Commands about yourself: forced death, difficulty, invisibility, forced buff immunity, achievements.</summary>
public static class SelfCommands
{
	/// <summary>.unlockachievements: completes the achievements that are only luck or hard to trigger.</summary>
	internal static void UnlockHiddenAchievements()
	{
		string[,] conditions = new string[30, 2]
		{
			{ "STAR_POWER", "Use" }, { "TOPPED_OFF", "Use" }, { "GET_A_LIFE", "Use" }, { "NOT_THE_BEES", "Use" }, { "WATCH_YOUR_STEP", "Hit" },
			{ "RAINBOWS_AND_UNICORNS", "Use" }, { "YOU_AND_WHAT_ARMY", "Spawn" }, { "THROWING_LINES", "Use" }, { "LUCKY_BREAK", "Hit" }, { "VEHICULAR_MANSLAUGHTER", "Hit" },
			{ "ROCK_BOTTOM", "Reach" }, { "INTO_ORBIT", "Reach" }, { "WHERES_MY_HONEY", "Reach" }, { "JEEPERS_CREEPERS", "Reach" }, { "ITS_GETTING_HOT_IN_HERE", "Reach" },
			{ "FUNKYTOWN", "Reach" }, { "I_AM_LOOT", "Peek" }, { "FLY_A_KITE_ON_A_WINDY_DAY", "Use" }, { "FOUND_GRAVEYARD", "Reach" }, { "GO_LAVA_FISHING", "Do" },
			{ "TALK_TO_NPC_AT_MAX_HAPPINESS", "Do" }, { "PET_THE_PET", "Do" }, { "FIND_A_FAIRY", "Do" }, { "DIE_TO_DEAD_MANS_CHEST", "Do" }, { "GAIN_TORCH_GODS_FAVOR", "Use" },
			{ "DRINK_BOTTLED_WATER_WHILE_DROWNING", "Use" }, { "PLAY_ON_A_SPECIAL_SEED", "Do" }, { "TROUT_MONKEY", "Finish" }, { "PURIFY_ENTIRE_WORLD", "Do" }, { "TOPPED_OFF", "Use" }
		};
		int done = 0;
		for (int i = 0; i < conditions.GetLength(0); i++)
		{
			try
			{
				Main.Achievements.GetCondition(conditions[i, 0], conditions[i, 1])?.Complete();
				done++;
			}
			catch
			{
			}
		}
		ClientChat.Info("Completed " + done + " achievement conditions.");
	}

	/// <summary>.tinvis: your HP is sent as 0 so other clients think you are dead and stop drawing you.</summary>
	public static bool Invisibility;

	public static bool[] BuffForceImmune = new bool[BuffID.Count];

	/// <summary>.suicide / .killself / .killme / .kms / .die</summary>
	internal static void KillSelfCommand(CommandContext command)
	{
		string[] array = command.Parts;
		Player player = Main.LocalPlayer;
		if (player.dead)
		{
			ClientChat.Error("You are already dead.");
			return;
		}
		string kind = (array.Length > 1) ? array[1].ToLower() : "";
		int param = (array.Length > 2 && int.TryParse(array[2], out var p1)) ? p1 : 0;
		int param2 = (array.Length > 3 && int.TryParse(array[3], out var p2)) ? p2 : (-1);
		if (param2 >= Main.maxPlayers)
		{
			param2 = Main.maxPlayers - 1;
		}
		PlayerDeathReason reason;
		switch (kind)
		{
		case "p":
		case "plr":
		case "player":
			reason = PlayerDeathReason.ByPlayer(Utils.Clamp(param, 0, Main.maxPlayers - 1));
			break;
		case "n":
		case "npc":
			reason = PlayerDeathReason.ByNPC(param);
			break;
		case "pr":
		case "proj":
		case "projectile":
			reason = PlayerDeathReason.ByProjectileType(param2, param);
			break;
		case "o":
		case "other":
			reason = PlayerDeathReason.ByOther(param);
			break;
		case "e":
		case "empty":
			reason = new PlayerDeathReason();
			break;
		default:
			reason = PlayerDeathReason.LegacyDefault();
			break;
		}
		// A forced death: Anti-Death and Journey godmode don't stop it.
		bool oldAntiDeath = PlayerCheats.AntiDeathGod;
		bool oldCreativeGod = player.creativeGodMode;
		PlayerCheats.AntiDeathGod = false;
		player.creativeGodMode = false;
		try
		{
			player.KillMe(reason, 10.0, 0, pvp: true);
		}
		finally
		{
			PlayerCheats.AntiDeathGod = oldAntiDeath;
			player.creativeGodMode = oldCreativeGod;
		}
		ClientChat.Info("Committed suicide.");
	}

	/// <summary>.plrdif / .playerdifficulty / .chrdif / .characterdifficulty</summary>
	internal static void CharacterDifficultyCommand(CommandContext command)
	{
		string[] array = command.Parts;
		Player player = Main.LocalPlayer;
		if (array.Length <= 1 || string.IsNullOrWhiteSpace(array[1]))
		{
			ClientChat.Info("Current player difficulty: " + PlayerDifficultyName(player.difficulty) + " (ID: " + player.difficulty + ")");
			return;
		}
		int difficulty;
		switch (array[1].ToLower())
		{
		case "0":
		case "sc":
		case "classic":
		case "softcore":
			difficulty = 0;
			break;
		case "1":
		case "mc":
		case "medium":
		case "mediumcore":
			difficulty = 1;
			break;
		case "2":
		case "hc":
		case "hard":
		case "hardcore":
			difficulty = 2;
			break;
		case "3":
		case "cr":
		case "journey":
		case "creative":
			difficulty = 3;
			break;
		default:
			ClientChat.Error("Invalid player difficulty.");
			return;
		}
		player.difficulty = (byte)difficulty;
		if (Main.netMode == 1)
		{
			NetMessage.TrySendData(4, -1, -1, null, Main.myPlayer);
		}
		ClientChat.Info("Player difficulty changed to: " + PlayerDifficultyName(player.difficulty) + " (ID: " + player.difficulty + ")");
	}

	private static string PlayerDifficultyName(byte difficulty)
	{
		return difficulty switch
		{
			0 => Language.GetTextValue("Terraria.UI.Softcore"),
			1 => Language.GetTextValue("Terraria.UI.Mediumcore"),
			2 => Language.GetTextValue("Terraria.UI.Hardcore"),
			3 => Language.GetTextValue("Terraria.UI.Creative"),
			_ => "Unknown",
		};
	}

	/// <summary>Player.UpdateEquips: buffs from .blockbuff can never be applied to you.</summary>
	public static void ApplyForcedBuffImmunities(Player player)
	{
		if (player.whoAmI != Main.myPlayer)
		{
			return;
		}
		int count = Math.Min(player.buffImmune.Length, BuffForceImmune.Length);
		for (int i = 0; i < count; i++)
		{
			if (BuffForceImmune[i])
			{
				player.buffImmune[i] = true;
			}
		}
	}

	#region Chat commands

	/// <summary>.unlockachievements</summary>
	internal static void UnlockAchievementsCommand(CommandContext command)
	{
		UnlockHiddenAchievements();
	}

	/// <summary>.tinvis</summary>
	internal static void TrueInvisibilityCommand(CommandContext command)
	{
		Invisibility = !Invisibility;
		ClientChat.Info(Invisibility ? "You are now invisible." : "You are no longer invisible.");
		if (Invisibility && ServerDetection.OnTshockServer)
		{
			ClientChat.Notice("Doesn't work on TShock servers.");
		}
		NetMessage.SendData(16, -1, -1, null, Main.myPlayer);
	}

	/// <summary>.blockbuff / .bbuff</summary>
	internal static void BlockBuffCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out var buffId) && buffId > 0 && buffId < BuffID.Count)
			{
				BuffForceImmune[buffId] = !BuffForceImmune[buffId];
				ClientChat.Info(BuffForceImmune[buffId] ? IsaClient.Languages.L10n.F("You are now immune to {0} (ID {1}).", Lang.GetBuffName(buffId), buffId) : IsaClient.Languages.L10n.F("You are no longer immune to {0} (ID {1}).", Lang.GetBuffName(buffId), buffId));
			}
			else
			{
				ClientChat.Error("Invalid Buff ID specified.");
			}
			return;
		}
		ClientChat.Info("Current buffs you are forced immune to:");
		for (int i = 1; i < BuffID.Count; i++)
		{
			if (BuffForceImmune[i])
			{
				ClientChat.Info(Lang.GetBuffName(i) + " (ID " + i + ")");
			}
		}
	}

	#endregion
}
