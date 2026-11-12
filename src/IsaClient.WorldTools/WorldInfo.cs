using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.Enums;
using Terraria.GameContent.Drawing;
using Terraria.GameContent.Events;
using Terraria.ID;
using Terraria.Localization;
using IsaClient.Core;
using IsaClient.Movement;
using IsaClient.Music;
using IsaClient.Rendering;

namespace IsaClient.WorldTools;

/// <summary>World information values and actions: bosses, invasions, orbs, moon phase, purity, size.</summary>
public static class WorldInfo
{
	public static bool SunDialEnabled
	{
		get
		{
			if (Main.netMode == 1)
			{
				return Main.sundialCooldown == 0;
			}
			return true;
		}
	}

	public static bool MoonDialEnabled
	{
		get
		{
			if (Main.netMode == 1)
			{
				return Main.moondialCooldown == 0;
			}
			return true;
		}
	}

	public static bool DoWeHaveDungeonPosition
	{
		get
		{
			if (Main.dungeonX > 0)
			{
				return Main.dungeonY > 0;
			}
			return false;
		}
	}

	/// <summary>Golem's summon packet needs a Lihzahrd Altar next to you on servers.</summary>
	public static bool GolemSpawnEnabled
	{
		get
		{
			if (Main.netMode != 1)
			{
				return true;
			}
			int x = (int)(Main.LocalPlayer.Center.X / 16f);
			int y = (int)(Main.LocalPlayer.Center.Y / 16f);
			for (int i = x - 20; i < x + 20; i++)
			{
				for (int j = y - 20; j < y + 20; j++)
				{
					if (WorldGen.InWorld(i, j) && Main.tile[i, j] != null && Main.tile[i, j].active() && Main.tile[i, j].type == 237 && Main.tile[i, j].frameX == 18 && Main.tile[i, j].frameY == 0)
					{
						return true;
					}
				}
			}
			return false;
		}
	}

	/// <summary>Mechdusa only exists in the For the Worthy + Don't Dig Up (zenith) worlds on servers.</summary>
	public static bool MechdusaEnabled
	{
		get
		{
			if (Main.netMode == 1)
			{
				if (!Main.getGoodWorld)
				{
					return Main.remixWorld;
				}
				return true;
			}
			return true;
		}
	}

	public static string WorldInfoValue(string name)
	{
		if (Main.gameMenu || Main.ActiveWorldFileData == null)
		{
			return null;
		}
		switch (name)
		{
		case "World Name":
			return Main.worldName;
		case "World Size":
			return WorldSizeName();
		case "World Difficulty":
			return WorldDifficultyName();
		case "World Evil":
			return WorldGen.crimson ? "Crimson" : "Corruption";
		case "World ID":
			return Main.worldID.ToString();
		case "World GUID":
			return Main.ActiveWorldFileData.UniqueId.ToString();
		case "World Generator Version":
			return Main.ActiveWorldFileData.WorldGeneratorVersion.ToString();
		case "Seed":
		{
			string seed = Main.ActiveWorldFileData.GetFullSeedText();
			if (!string.IsNullOrEmpty(seed))
			{
				return seed;
			}
			return (Main.netMode == 1) ? "Unknown (server)" : "No Seed (World too old?)";
		}
		case "World Infection":
			return WorldPurityStatus();
		case "Moon Phase":
			return MoonPhaseName();
		case "Moon Type":
			return Main.moonType.ToString();
		case "Spawn Position":
			return "X: " + Main.spawnTileX + " Y: " + Main.spawnTileY;
		case "Dungeon Position":
			return DoWeHaveDungeonPosition ? ("X: " + Main.dungeonX + " Y: " + Main.dungeonY) : "Unknown";
		case "Ice Background Style":
			return Main.iceBackStyle.ToString();
		case "Jungle Background Style":
			return Main.jungleBackStyle.ToString();
		case "Hell Background Style":
			return Main.hellBackStyle.ToString();
		case "World Surface":
			return "Y: " + (int)Main.worldSurface;
		case "Rock Layer":
			return "Y: " + (int)Main.rockLayer;
		case "Max Raining":
			return Main.maxRaining.ToString("0.000");
		case "Sandstorm Severity":
			return Sandstorm.IntendedSeverity.ToString("0.000");
		case "Invasion Type":
			return InvasionName(Main.invasionType);
		case "Enchanted Sundial Cooldown":
			return Main.sundialCooldown + " day(s)";
		case "Enchanted Moondial Cooldown":
			return Main.moondialCooldown + " day(s)";
		case "Copper Ore":
			return OreName(WorldGen.SavedOreTiers.Copper, 7, "Copper", 166, "Tin");
		case "Iron Ore":
			return OreName(WorldGen.SavedOreTiers.Iron, 6, "Iron", 167, "Lead");
		case "Silver Ore":
			return OreName(WorldGen.SavedOreTiers.Silver, 9, "Silver", 168, "Tungsten");
		case "Gold Ore":
			return OreName(WorldGen.SavedOreTiers.Gold, 8, "Gold", 169, "Platinum");
		case "Cobalt Ore":
			return OreName(WorldGen.SavedOreTiers.Cobalt, 107, "Cobalt", 221, "Palladium");
		case "Mythril Ore":
			return OreName(WorldGen.SavedOreTiers.Mythril, 108, "Mythril", 222, "Orichalcum");
		case "Adamantite Ore":
			return OreName(WorldGen.SavedOreTiers.Adamantite, 111, "Adamantite", 223, "Titanium");
		case "Smashed Orb Count":
			return (Main.netMode == 1) ? "Unknown (server)" : WorldGen.shadowOrbCount.ToString();
		case "Smashed Altar Count":
			return (Main.netMode == 1) ? "Unknown (server)" : WorldGen.altarCount.ToString();
		case "Generating Hardmode":
			return WorldGen.TransformingWorld ? "True" : "False";
		default:
			return null;
		}
	}

	private static string OreName(int tier, int first, string firstName, int second, string secondName)
	{
		if (tier == first)
		{
			return firstName;
		}
		if (tier == second)
		{
			return secondName;
		}
		return (tier == -1) ? "Not decided yet" : ("Unknown (" + tier + ")");
	}

	public static string WorldSizeName()
	{
		return Main.maxTilesX switch
		{
			4200 => Language.GetTextValue("Terraria.UI.WorldSizeSmall"),
			6400 => Language.GetTextValue("Terraria.UI.WorldSizeMedium"),
			8400 => Language.GetTextValue("Terraria.UI.WorldSizeLarge"),
			5000 => "Pre-Alpha",
			1750 => "Legacy Tiny",
			_ => $"Custom: {Main.maxTilesX}x{Main.maxTilesY}",
		};
	}

	public static string WorldDifficultyName()
	{
		if (Main.GameMode == 3)
		{
			return Language.GetTextValue("Terraria.UI.Creative");
		}
		int level = Main.GameMode switch
		{
			1 => 2,
			2 => 3,
			_ => 1,
		};
		if (Main.getGoodWorld)
		{
			level++;
		}
		return level switch
		{
			2 => Language.GetTextValue("Terraria.UI.Expert"),
			3 => Language.GetTextValue("Terraria.UI.Master"),
			4 => Language.GetTextValue("Terraria.UI.Legendary"),
			_ => Language.GetTextValue("Terraria.UI.Normal"),
		};
	}

	public static string WorldPurityStatus()
	{
		int evil = WorldGen.tEvil;
		int blood = WorldGen.tBlood;
		int good = WorldGen.tGood;
		if (evil <= 0 && blood <= 0 && good <= 0)
		{
			return "100% Pure";
		}
		string text = "";
		if (evil > 0)
		{
			text = evil + "% Corrupted";
		}
		if (blood > 0)
		{
			text += ((text.Length > 0) ? " & " : "") + blood + "% Crimson";
		}
		if (good > 0)
		{
			text += ((text.Length > 0) ? " & " : "") + good + "% Hallowed";
		}
		return text;
	}

	public static string MoonPhaseName()
	{
		return Main.moonPhase switch
		{
			0 => Language.GetTextValue("GameUI.FullMoon"),
			1 => Language.GetTextValue("GameUI.WaningGibbous"),
			2 => Language.GetTextValue("GameUI.ThirdQuarter"),
			3 => Language.GetTextValue("GameUI.WaningCrescent"),
			4 => Language.GetTextValue("GameUI.NewMoon"),
			5 => Language.GetTextValue("GameUI.WaxingCrescent"),
			6 => Language.GetTextValue("GameUI.FirstQuarter"),
			7 => Language.GetTextValue("GameUI.WaxingGibbous"),
			_ => Main.moonPhase.ToString(),
		};
	}

	private static string InvasionName(int type)
	{
		return type switch
		{
			0 => "None",
			1 => "Goblin Army",
			2 => "Frost Legion",
			3 => "Pirates",
			4 => "Martian Madness",
			_ => type.ToString(),
		};
	}

	private static Vector2 BossSpawnPoint()
	{
		Player player = Main.LocalPlayer;
		float x = Main.screenPosition.X + (float)(Main.screenWidth / 2) + (float)((player.direction == 1) ? 300 : (-300));
		float y = Main.screenPosition.Y + (float)(Main.screenHeight / 2) - 100f;
		return new Vector2(x, y);
	}

	/// <summary>Spawns a boss: locally in single player, through the boss summon packet on servers. -2 is Mechdusa.</summary>
	public static void SpawnBoss(int type)
	{
		SoundEngine.PlaySound(11);
		Player player = Main.LocalPlayer;
		if (type != 113 && type != 222 && type != 262 && type != 370 && type != 398 && type != 439 && type != 636)
		{
			SoundEngine.PlaySound(15, (int)player.position.X, (int)player.position.Y, 0);
		}
		else if (type == 222)
		{
			SoundEngine.PlaySound(SoundID.Item173, (int)player.position.X, (int)player.position.Y);
		}
		Vector2 spot = BossSpawnPoint();
		if (Main.netMode != 1)
		{
			switch (type)
			{
			case 113:
				NPC.SpawnWOF(player.position);
				break;
			case 125:
				NPC.SpawnOnPlayer(Main.myPlayer, 125);
				NPC.SpawnOnPlayer(Main.myPlayer, 126);
				break;
			case 370:
			{
				int index = NPC.NewNPC(NPC.GetBossSpawnSource(Main.myPlayer), (int)spot.X, (int)spot.Y + 100, 370);
				Main.NewText(IsaClient.Languages.L10n.T(Language.GetTextValue("Announcement.HasAwoken", Main.npc[index].TypeName)), 175, 75);
				break;
			}
			case 439:
			{
				int index = NPC.NewNPC(NPC.GetBossSpawnSource(Main.myPlayer), (int)spot.X, (int)spot.Y, 439);
				Main.npc[index].direction = (Main.npc[index].spriteDirection = ((player.direction <= 0) ? 1 : (-1)));
				Main.NewText(IsaClient.Languages.L10n.T(Language.GetTextValue("Announcement.HasAwoken", Main.npc[index].TypeName)), 175, 75);
				break;
			}
			case 636:
				NPC.SpawnBoss((int)spot.X, (int)spot.Y, type, Main.myPlayer);
				break;
			case -2:
			{
				NPC.mechQueen = -2;
				NPC.SpawnOnPlayer(Main.myPlayer, 127);
				NPC.mechQueen = NPC.FindFirstNPC(127);
				if (NPC.mechQueen < 0)
				{
					break;
				}
				Vector2 center = Main.npc[NPC.mechQueen].Center;
				NPC.NewNPC(NPC.GetBossSpawnSource(Main.myPlayer), (int)center.X, (int)center.Y, 125, 1);
				NPC.NewNPC(NPC.GetBossSpawnSource(Main.myPlayer), (int)center.X, (int)center.Y, 126, 1);
				int destroyer = NPC.NewNPC(NPC.GetBossSpawnSource(Main.myPlayer), (int)center.X, (int)center.Y, 134, 1);
				NPC.NewNPC(NPC.GetBossSpawnSource(Main.myPlayer), (int)center.X, (int)center.Y, 139, 1, 0f, 0f, destroyer, -1f);
				NPC.NewNPC(NPC.GetBossSpawnSource(Main.myPlayer), (int)center.X, (int)center.Y, 139, 1, 0f, 0f, destroyer, 1f);
				break;
			}
			default:
				NPC.SpawnOnPlayer(Main.myPlayer, type);
				break;
			}
			return;
		}
		switch (type)
		{
		case 35:
			NetMessage.SendData(61, -1, -1, null, Main.myPlayer, type);
			NetMessage.SendData(51, -1, -1, null, Main.myPlayer, 1f);
			break;
		case 125:
			NetMessage.SendData(61, -1, -1, null, Main.myPlayer, 125f);
			NetMessage.SendData(61, -1, -1, null, Main.myPlayer, 126f);
			break;
		case 398:
			NetMessage.SendData(61, -1, -1, null, Main.myPlayer, -8f);
			break;
		case -2:
			NetMessage.SendData(61, -1, -1, null, Main.myPlayer, -16f);
			break;
		default:
			NetMessage.SendData(61, -1, -1, null, Main.myPlayer, type);
			break;
		}
	}

	public static void SpawnAllBosses(bool hardmode)
	{
		if (!hardmode)
		{
			SpawnBoss(50);
			SpawnBoss(4);
			SpawnBoss(13);
			SpawnBoss(266);
			SpawnBoss(35);
			SpawnBoss(222);
			SpawnBoss(668);
			if (Main.netMode != 1)
			{
				SpawnBoss(113);
			}
			return;
		}
		SpawnBoss(657);
		if (Main.remixWorld && Main.getGoodWorld)
		{
			SpawnBoss(-2);
		}
		else
		{
			SpawnBoss(134);
			SpawnBoss(125);
			SpawnBoss(127);
		}
		if (Main.netMode != 1)
		{
			SpawnBoss(262);
			SpawnBoss(636);
			SpawnBoss(439);
		}
		if (GolemSpawnEnabled)
		{
			SpawnBoss(245);
		}
		SpawnBoss(370);
		SpawnBoss(398);
	}

	/// <summary>-1 goblins, -2 frost legion, -3 pirates, -4 pumpkin moon, -5 frost moon, -6 eclipse, -7 martians,
	/// -10 blood moon, -98 Old One's Army crystal, -97 lunar apocalypse.</summary>
	public static void SpawnInvasion(int type)
	{
		SoundEngine.PlaySound(11);
		if (type != -98)
		{
			SoundEngine.PlaySound(15, (int)Main.LocalPlayer.position.X, (int)Main.LocalPlayer.position.Y, 0);
		}
		if (Main.netMode == 1)
		{
			if (type == -98)
			{
				NetMessage.SendData(113, -1, -1, null, (int)Main.LocalPlayer.Center.X / 16, (int)Main.LocalPlayer.Center.Y / 16);
			}
			else if (type >= -10)
			{
				NetMessage.SendData(61, -1, -1, null, Main.myPlayer, type);
			}
			return;
		}
		switch (type)
		{
		case -1:
		case -2:
		case -3:
		case -7:
			Main.invasionType = 0;
			Main.invasionSize = 0;
			Main.invasionDelay = 0;
			Main.StartInvasion(type switch
			{
				-2 => 2,
				-3 => 3,
				-7 => 4,
				_ => 1,
			});
			if (type != -7)
			{
				Main.invasionX = Main.spawnTileX - 1;
				Main.invasionWarn = 2;
			}
			break;
		case -4:
			Main.NewText(IsaClient.Languages.L10n.T(Lang.misc[31].Value), 50, byte.MaxValue, 130);
			Main.startPumpkinMoon();
			break;
		case -5:
			Main.NewText(IsaClient.Languages.L10n.T(Lang.misc[34].Value), 50, byte.MaxValue, 130);
			Main.startSnowMoon();
			break;
		case -6:
			Main.eclipse = true;
			Main.NewText(IsaClient.Languages.L10n.T(Main.remixWorld ? Lang.misc[106].Value : Lang.misc[20].Value), 50, byte.MaxValue, 130);
			break;
		case -10:
			Main.bloodMoon = true;
			if (Main.GetMoonPhase() == MoonPhase.Empty)
			{
				Main.moonPhase = 5;
			}
			Main.NewText(IsaClient.Languages.L10n.T(Lang.misc[8].Value), 50, byte.MaxValue, 130);
			break;
		case -98:
			DD2Event.SummonCrystalDirect((int)Main.LocalPlayer.Top.X / 16 - 2, (int)Main.LocalPlayer.Top.Y / 16, Main.myPlayer);
			break;
		case -97:
			WorldGen.TriggerLunarApocalypse();
			break;
		}
	}

	/// <summary>Single player: smashes a Shadow Orb / Crimson Heart (the third one summons the evil boss).</summary>
	public static void SmashOrb(bool crimson)
	{
		SoundEngine.PlaySound(11);
		WorldGen.shadowOrbSmashed = true;
		WorldGen.shadowOrbCount++;
		if (WorldGen.shadowOrbCount >= 3)
		{
			if ((crimson && NPC.AnyNPCs(266)) || (!crimson && NPC.AnyNPCs(13)))
			{
				return;
			}
			WorldGen.shadowOrbCount = 0;
			NPC.SpawnOnPlayer(Main.myPlayer, crimson ? 266 : 13);
			return;
		}
		LocalizedText text = (WorldGen.shadowOrbCount == 2) ? Lang.misc[11] : Lang.misc[10];
		Main.NewText(IsaClient.Languages.L10n.T(text.Value), 50, byte.MaxValue, 130);
	}

	/// <summary>.wlddif / .worlddifficulty</summary>
	internal static void WorldDifficultyCommand(CommandContext command)
	{
		string[] array = command.Parts;
		string[] names = new string[4] { "Classic", "Expert", "Master", "Journey" };
		if (array.Length <= 1 || string.IsNullOrWhiteSpace(array[1]))
		{
			ClientChat.Info("Current world difficulty: " + ((Main.GameMode >= 0 && Main.GameMode < 4) ? names[Main.GameMode] : "Unknown") + " (ID: " + Main.GameMode + ")");
			return;
		}
		int mode;
		switch (array[1].ToLowerInvariant())
		{
		case "0":
		case "c":
		case "classic":
		case "normal":
			mode = 0;
			break;
		case "1":
		case "e":
		case "expert":
			mode = 1;
			break;
		case "2":
		case "m":
		case "master":
		case "legendary":
		case "legend":
			mode = 2;
			break;
		case "3":
		case "j":
		case "cr":
		case "journey":
		case "creative":
			mode = 3;
			break;
		default:
			ClientChat.Error("Invalid world difficulty.");
			return;
		}
		Main.GameMode = mode;
		ClientChat.Info("World difficulty changed to: " + names[mode] + " (ID: " + mode + ")");
	}

	/// <summary>Right-hand value of the list items of the ported categories (and World Information); null when not ours.</summary>
	public static string PanelValue(string name)
	{
		switch (name)
		{
		case "Surface Friction":
			return Travel.SurfaceFrictionMode switch
			{
				1 => "None",
				2 => "Icy",
				3 => "No Friction",
				_ => "Normal",
			};
		case "Conveyer Belt Handling":
			return Travel.ConveyerBeltMode switch
			{
				1 => "Ignored",
				2 => "Force Clockwise",
				3 => "Force Counter-Clockwise",
				_ => "Normal",
			};
		case "Hitbox Style":
			return Hitboxes.HitBoxesTexture switch
			{
				1 => "Border w/ Overlay",
				2 => "Flat Overlay",
				_ => "Border",
			};
		case "Walking Particles":
			return Cosmetics.WalkParticleType switch
			{
				1 => "Aetherium Block",
				2 => "Flames",
				3 => "Flames (Dyed)",
				_ => "None",
			};
		case "Death Particle":
			return Cosmetics.ParticleName((ParticleOrchestraType)Cosmetics.DeathParticleCosmeticId);
		case "Death Effect":
			return Cosmetics.DeathEffectName(Cosmetics.DeathGoreCosmeticId);
		case "Death Sound":
			return Cosmetics.ParticleName((ParticleOrchestraType)Cosmetics.DeathSoundCosmeticId);
		case "Lucy Messages":
			return Cosmetics.LucyMessagesMode switch
			{
				1 => "Forced Off",
				2 => "Forced On",
				3 => "Forced On (Everyone)",
				_ => "No Override",
			};
		case "Lucy Message on Death":
			return Cosmetics.LucyDeathMessageMode switch
			{
				1 => "Self",
				2 => "Others",
				3 => "Everyone",
				_ => "Disabled",
			};
		case "Force Soundtrack":
			return AudioTweaks.ForceSoundtrackId switch
			{
				1 => "Terraria",
				2 => "Terraria: Otherworld",
				_ => "No Override",
			};
		default:
			return WorldInfoValue(name);
		}
	}

	#region Chat commands

	/// <summary>.dungeon</summary>
	internal static void DungeonCommand(CommandContext command)
	{
		if (Main.netMode != 1 || DoWeHaveDungeonPosition)
		{
			TeleportTools.TeleportToTile(Main.dungeonX, Main.dungeonY);
			ClientChat.Info("Teleported to the dungeon.");
		}
		else
		{
			ClientChat.Error("The dungeon position is unavailable.");
		}
	}

	#endregion
}
