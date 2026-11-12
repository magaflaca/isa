using System;
using System.Collections.Generic;
using IsaClient.Movement;
using IsaClient.WorldTools;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.Events;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;

namespace IsaClient.Menu;

/// <summary>World Information tab: world data, bosses, invasions, special seeds, utilities and flags.</summary>
public static class WorldInfoMenu
{
	/// <summary>World Information categories (CreateCheatGroup index 1): 20 world, 21 pre-hardmode bosses, ... 26 world data.</summary>
	internal const int WorldInfoFirstCategory = 20;

	internal static readonly List<string>[] SectionEntries = new List<string>[7]
	{
		new List<string> { "World Name", "World Size", "World Difficulty", "World Evil", "World ID", "World GUID", "World Generator Version", "Seed", "Hardmode", "World Infection" },
		new List<string>
		{
			"Defeated King Slime", "Spawn King Slime", "Defeated Eye of Cthulhu", "Spawn Eye of Cthulhu", "Defeated Boss 2", "Spawn Boss 2", "Spawn Alternate Evil Boss", "Defeated Skeletron", "Spawn Skeletron", "Defeated Queen Bee",
			"Spawn Queen Bee", "Defeated Deerclops", "Spawn Deerclops", "Spawn Wall of Flesh"
		},
		new List<string>
		{
			"Defeated Queen Slime", "Spawn Queen Slime", "Defeated The Destroyer", "Spawn The Destroyer", "Defeated The Twins", "Spawn The Twins", "Defeated Skeletron Prime", "Spawn Skeletron Prime", "Spawn Mechdusa", "Defeated Plantera",
			"Spawn Plantera", "Defeated Golem", "Spawn Golem", "Defeated Duke Fishron", "Spawn Duke Fishron", "Defeated Empress of Light", "Spawn Empress of Light", "Defeated Lunatic Cultist", "Spawn Lunatic Cultist", "Defeated Moon Lord",
			"Spawn Moon Lord"
		},
		new List<string>
		{
			"Defeated Goblin Army", "Summon Goblin Army", "Defeated Frost Legion", "Summon Frost Legion", "Defeated Pirate Invasion", "Summon Pirate Invasion", "Defeated Martian Madness", "Summon Martian Madness", "Start Blood Moon", "Start Eclipse",
			"Start Pumpkin Moon", "Start Frost Moon", "Summon Old One's Army", "Trigger Lunar Apocalypse"
		},
		new List<string>
		{
			"Drunk", "For the Worthy", "Not the Bees", "Celebration", "The Constant", "No Traps", "Remix (Don't Dig Up)", "Zenith (GetFixedBoi)", "Skyblock", "Vampire",
			"Infected", "Team Based Spawns", "Dual Dungeons", "More Lightning", "No Lightning"
		},
		new List<string>
		{
			"Use Enchanted Sundial", "Use Enchanted Moondial", "Use Advanced Combat Techniques", "Use Advanced Combat Techniques V2", "Use Peddler's Satchel", "Use Cat License", "Use Dog License", "Use Bunny License", "Smash Shadow Orb", "Smash Crimson Heart",
			"Spawn Meteor", "Smash Demon Altar", "Generate Hardmode Biomes", "Spawn Plantera Bulb", "Purify World", "Corrupt World", "Hallow World", "Crimson World", "Jungle to Mushroom", "Desert World",
			"Snow World", "Forest World"
		},
		new List<string>
		{
			"Spawn Position", "Dungeon Position", "Moon Phase", "Moon Type", "Ice Background Style", "Jungle Background Style", "Hell Background Style", "World Surface", "Rock Layer", "Clouds",
			"Raining", "Max Raining", "Sandstorm Ongoing", "Sandstorm Severity", "Slime Rain", "Blood Moon", "Eclipse", "Pumpkin Moon", "Frost Moon", "Killed a Clown",
			"Killed any Mechanical Boss", "Killed a Mourning Wood", "Killed a Pumpking", "Killed an Everscream", "Killed a Santa-NK1", "Killed an Ice Queen", "Ongoing Natural Party", "Ongoing Manually Started Party", "Ongoing Old One's Army", "Defeated Old One's Army T1",
			"Defeated Old One's Army T2", "Defeated Old One's Army T3", "Used Advanced Combat Techniques", "Used Advanced Combat Techniques V2", "Used Peddler's Satchel", "Natural Lantern Night", "Manually Started Lantern Night", "Defeated Solar Pillar", "Defeated Vortex Pillar", "Defeated Nebula Pillar",
			"Defeated Stardust Pillar", "Force Christmas for Today", "Force Halloween for Today", "Purchased a Town Cat", "Purchased a Town Dog", "Purchased a Town Bunny", "Free Cake from Party Girl", "Rescued Nerdy Slime", "Unlocked Cool Slime", "Rescued Elder Slime",
			"Rescued Clumsy Slime", "Unlocked Diva Slime", "Rescued Surly Slime", "Rescued Mystic Slime", "Unlocked Squire Slime", "Unlocked Truffle", "Enchanted Sundial Cooldown", "Enchanted Moondial Cooldown", "Copper Ore", "Iron Ore",
			"Silver Ore", "Gold Ore", "Cobalt Ore", "Mythril Ore", "Adamantite Ore", "Smashed Orb", "Smashed Orb Count", "Smashed Altar Count", "Saved Goblin Tinkerer", "Saved Wizard",
			"Saved Mechanic", "Saved Stylist", "Saved Angler", "Saved Tax Collector", "Saved Tavernkeep", "Saved Golfer", "Invasion Type", "Generating Hardmode"
		}
	};

	internal static readonly string[] SectionTitles = new string[7] { "World Information", "Pre-Hardmode Bosses", "Hardmode Bosses", "Invasions", "Special Seeds", "Utilities", "World Data" };

	internal static UIElement Create(string cheat, Color color, int category)
	{
		UIElement element = category switch
		{
			0 => WorldInfo_General(cheat, color),
			1 => WorldInfo_Bosses(cheat, color),
			2 => WorldInfo_Bosses(cheat, color),
			3 => WorldInfo_Invasions(cheat, color),
			4 => WorldInfo_Seeds(cheat, color),
			5 => WorldInfo_Utilities(cheat, color),
			6 => WorldInfo_Data(cheat, color),
			_ => null,
		};
		return element ?? new MenuListItem(cheat, color);
	}

	private static UIElement WorldInfo_General(string cheat, Color color)
	{
		switch (cheat)
		{
		case "World Difficulty":
			return MenuControls.MakeCycle(cheat, color, delegate(bool back)
			{
				Main.GameMode = MenuControls.Cycle(Main.GameMode, 0, 3, back);
			});
		case "World Evil":
			return MenuControls.MakeInfo(cheat, color, delegate
			{
				WorldGen.crimson = !WorldGen.crimson;
				SoundEngine.PlaySound(11);
			});
		case "Hardmode":
			return MenuControls.MakeFlag(cheat, color, () => Main.hardMode, delegate(bool v)
			{
				Main.hardMode = v;
			});
		default:
			return MenuControls.MakeInfo(cheat, color);
		}
	}

	private static readonly Dictionary<string, (Func<bool> Get, Action<bool> Set)> _bossFlags = new Dictionary<string, (Func<bool>, Action<bool>)>
	{
		{ "Defeated King Slime", (() => NPC.downedSlimeKing, v => NPC.downedSlimeKing = v) },
		{ "Defeated Eye of Cthulhu", (() => NPC.downedBoss1, v => NPC.downedBoss1 = v) },
		{ "Defeated Boss 2", (() => NPC.downedBoss2, v => NPC.downedBoss2 = v) },
		{ "Defeated Skeletron", (() => NPC.downedBoss3, v => NPC.downedBoss3 = v) },
		{ "Defeated Queen Bee", (() => NPC.downedQueenBee, v => NPC.downedQueenBee = v) },
		{ "Defeated Deerclops", (() => NPC.downedDeerclops, v => NPC.downedDeerclops = v) },
		{ "Defeated Queen Slime", (() => NPC.downedQueenSlime, v => NPC.downedQueenSlime = v) },
		{ "Defeated The Destroyer", (() => NPC.downedMechBoss1, v => NPC.downedMechBoss1 = v) },
		{ "Defeated The Twins", (() => NPC.downedMechBoss2, v => NPC.downedMechBoss2 = v) },
		{ "Defeated Skeletron Prime", (() => NPC.downedMechBoss3, v => NPC.downedMechBoss3 = v) },
		{ "Defeated Plantera", (() => NPC.downedPlantBoss, v => NPC.downedPlantBoss = v) },
		{ "Defeated Golem", (() => NPC.downedGolemBoss, v => NPC.downedGolemBoss = v) },
		{ "Defeated Duke Fishron", (() => NPC.downedFishron, v => NPC.downedFishron = v) },
		{ "Defeated Empress of Light", (() => NPC.downedEmpressOfLight, v => NPC.downedEmpressOfLight = v) },
		{ "Defeated Lunatic Cultist", (() => NPC.downedAncientCultist, v => NPC.downedAncientCultist = v) },
		{ "Defeated Moon Lord", (() => NPC.downedMoonlord, v => NPC.downedMoonlord = v) },
		{ "Defeated Goblin Army", (() => NPC.downedGoblins, v => NPC.downedGoblins = v) },
		{ "Defeated Frost Legion", (() => NPC.downedFrost, v => NPC.downedFrost = v) },
		{ "Defeated Pirate Invasion", (() => NPC.downedPirates, v => NPC.downedPirates = v) },
		{ "Defeated Martian Madness", (() => NPC.downedMartians, v => NPC.downedMartians = v) }
	};

	private static UIElement WorldInfo_Bosses(string cheat, Color color)
	{
		if (_bossFlags.TryGetValue(cheat, out var flag))
		{
			return MenuControls.MakeFlag(cheat, color, flag.Get, flag.Set);
		}
		int type;
		Func<bool> disabled = null;
		switch (cheat)
		{
		case "Spawn King Slime":
			type = 50;
			break;
		case "Spawn Eye of Cthulhu":
			type = 4;
			break;
		case "Spawn Boss 2":
			return MenuControls.MakeAction(cheat, color, () => "Spawn " + (WorldGen.crimson ? "Brain of Cthulhu" : "Eater of Worlds"), delegate
			{
				WorldInfo.SpawnBoss(WorldGen.crimson ? 266 : 13);
			});
		case "Spawn Alternate Evil Boss":
			return MenuControls.MakeAction(cheat, color, () => "Spawn " + (WorldGen.crimson ? "Eater of Worlds" : "Brain of Cthulhu") + " (other evil)", delegate
			{
				WorldInfo.SpawnBoss(WorldGen.crimson ? 13 : 266);
			});
		case "Spawn Skeletron":
			type = 35;
			break;
		case "Spawn Queen Bee":
			type = 222;
			break;
		case "Spawn Deerclops":
			type = 668;
			break;
		case "Spawn Wall of Flesh":
			type = 113;
			disabled = () => Main.netMode == 1;
			break;
		case "Spawn Queen Slime":
			type = 657;
			break;
		case "Spawn The Destroyer":
			type = 134;
			break;
		case "Spawn The Twins":
			type = 125;
			break;
		case "Spawn Skeletron Prime":
			type = 127;
			break;
		case "Spawn Mechdusa":
			type = -2;
			disabled = () => !WorldInfo.MechdusaEnabled;
			break;
		case "Spawn Plantera":
			type = 262;
			disabled = () => Main.netMode == 1;
			break;
		case "Spawn Golem":
			type = 245;
			disabled = () => !WorldInfo.GolemSpawnEnabled;
			break;
		case "Spawn Duke Fishron":
			type = 370;
			break;
		case "Spawn Empress of Light":
			type = 636;
			disabled = () => Main.netMode == 1;
			break;
		case "Spawn Lunatic Cultist":
			type = 439;
			disabled = () => Main.netMode == 1;
			break;
		case "Spawn Moon Lord":
			type = 398;
			break;
		default:
			return null;
		}
		return MenuControls.MakeAction(cheat, color, () => cheat, delegate
		{
			WorldInfo.SpawnBoss(type);
		}, disabled);
	}

	private static UIElement WorldInfo_Invasions(string cheat, Color color)
	{
		if (_bossFlags.TryGetValue(cheat, out var flag))
		{
			return MenuControls.MakeFlag(cheat, color, flag.Get, flag.Set);
		}
		int type;
		Func<bool> disabled = null;
		switch (cheat)
		{
		case "Summon Goblin Army":
			type = -1;
			break;
		case "Summon Frost Legion":
			type = -2;
			break;
		case "Summon Pirate Invasion":
			type = -3;
			break;
		case "Summon Martian Madness":
			type = -7;
			break;
		case "Start Blood Moon":
			type = -10;
			break;
		case "Start Eclipse":
			type = -6;
			break;
		case "Start Pumpkin Moon":
			type = -4;
			break;
		case "Start Frost Moon":
			type = -5;
			break;
		case "Summon Old One's Army":
			type = -98;
			break;
		case "Trigger Lunar Apocalypse":
			type = -97;
			disabled = () => Main.netMode == 1;
			break;
		default:
			return null;
		}
		return MenuControls.MakeAction(cheat, color, () => cheat, delegate
		{
			WorldInfo.SpawnInvasion(type);
		}, disabled);
	}

	private static UIElement WorldInfo_Seeds(string cheat, Color color)
	{
		switch (cheat)
		{
		case "Drunk":
			return MenuControls.MakeFlag(cheat, color, () => Main.drunkWorld, v => Main.drunkWorld = v);
		case "For the Worthy":
			return MenuControls.MakeFlag(cheat, color, () => Main.getGoodWorld, v => Main.getGoodWorld = v);
		case "Not the Bees":
			return MenuControls.MakeFlag(cheat, color, () => Main.notTheBeesWorld, v => Main.notTheBeesWorld = v);
		case "Celebration":
			return MenuControls.MakeFlag(cheat, color, () => Main.tenthAnniversaryWorld, v => Main.tenthAnniversaryWorld = v);
		case "The Constant":
			return MenuControls.MakeFlag(cheat, color, () => Main.dontStarveWorld, v => Main.dontStarveWorld = v);
		case "No Traps":
			return MenuControls.MakeFlag(cheat, color, () => Main.noTrapsWorld, v => Main.noTrapsWorld = v);
		case "Remix (Don't Dig Up)":
			return MenuControls.MakeFlag(cheat, color, () => Main.remixWorld, v => Main.remixWorld = v);
		case "Zenith (GetFixedBoi)":
			return MenuControls.MakeFlag(cheat, color, () => Main.zenithWorld, v => Main.zenithWorld = v);
		case "Skyblock":
			return MenuControls.MakeFlag(cheat, color, () => Main.skyblockWorld, v => Main.skyblockWorld = v);
		case "Vampire":
			return MenuControls.MakeFlag(cheat, color, () => Main.vampireSeed, v => Main.vampireSeed = v);
		case "Infected":
			return MenuControls.MakeFlag(cheat, color, () => Main.infectedSeed, v => Main.infectedSeed = v);
		case "Team Based Spawns":
			return MenuControls.MakeFlag(cheat, color, () => Main.teamBasedSpawnsSeed, v => Main.teamBasedSpawnsSeed = v);
		case "Dual Dungeons":
			return MenuControls.MakeFlag(cheat, color, () => Main.dualDungeonsSeed, v => Main.dualDungeonsSeed = v);
		case "More Lightning":
			return MenuControls.MakeFlag(cheat, color, () => Main.moreLightningSeed, v => Main.moreLightningSeed = v);
		case "No Lightning":
			return MenuControls.MakeFlag(cheat, color, () => Main.noLightningSeed, v => Main.noLightningSeed = v);
		default:
			return null;
		}
	}

	private static bool SinglePlayerOnly()
	{
		return Main.netMode != 0;
	}

	private static int ShiftCount(int withShift)
	{
		if (!Main.keyState.IsKeyDown(Keys.LeftShift))
		{
			return 1;
		}
		return withShift;
	}

	private static void UseBook(ref bool used, string textKey, int packet)
	{
		SoundEngine.PlaySound(SoundID.Item92);
		if (Main.netMode == 1)
		{
			NetMessage.SendData(61, -1, -1, null, Main.myPlayer, packet);
			return;
		}
		used = true;
		Main.NewText(IsaClient.Languages.L10n.T(Language.GetTextValue(textKey)), 50, byte.MaxValue, 130);
	}

	private static UIElement WorldInfo_Utilities(string cheat, Color color)
	{
		switch (cheat)
		{
		case "Use Enchanted Sundial":
			return MenuControls.MakeAction(cheat, color, () => WorldInfo.SunDialEnabled ? cheat : ("Sundial on Cooldown: " + Main.sundialCooldown + " Days"), delegate
			{
				SoundEngine.PlaySound(SoundID.Item4);
				if (Main.netMode == 1)
				{
					NetMessage.SendData(51, -1, -1, null, Main.myPlayer, 3f);
					return;
				}
				Main.Sundialing();
				Main.sundialCooldown = 0;
			}, () => !WorldInfo.SunDialEnabled);
		case "Use Enchanted Moondial":
			return MenuControls.MakeAction(cheat, color, () => WorldInfo.MoonDialEnabled ? cheat : ("Moondial on Cooldown: " + Main.moondialCooldown + " Days"), delegate
			{
				SoundEngine.PlaySound(SoundID.Item4);
				if (Main.netMode == 1)
				{
					NetMessage.SendData(51, -1, -1, null, Main.myPlayer, 6f);
					return;
				}
				Main.Moondialing();
				Main.moondialCooldown = 0;
			}, () => !WorldInfo.MoonDialEnabled);
		case "Use Advanced Combat Techniques":
			return MenuControls.MakeAction(cheat, color, () => (NPC.combatBookWasUsed ? "Re-" : "") + cheat, delegate
			{
				UseBook(ref NPC.combatBookWasUsed, "Misc.CombatBookUsed", -11);
			});
		case "Use Advanced Combat Techniques V2":
			return MenuControls.MakeAction(cheat, color, () => (NPC.combatBookVolumeTwoWasUsed ? "Re-" : "") + cheat, delegate
			{
				UseBook(ref NPC.combatBookVolumeTwoWasUsed, "Misc.CombatBookVolumeTwoUsed", -17);
			});
		case "Use Peddler's Satchel":
			return MenuControls.MakeAction(cheat, color, () => (NPC.peddlersSatchelWasUsed ? "Re-" : "") + cheat, delegate
			{
				UseBook(ref NPC.peddlersSatchelWasUsed, "Misc.PeddlersSatchelUsed", -18);
			});
		case "Use Cat License":
			return MenuControls.MakeAction(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(SoundID.Item92);
				NPC.UnlockOrExchangePet(ref NPC.boughtCat, 637, "Misc.LicenseCatUsed", -12);
			});
		case "Use Dog License":
			return MenuControls.MakeAction(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(SoundID.Item92);
				NPC.UnlockOrExchangePet(ref NPC.boughtDog, 638, "Misc.LicenseDogUsed", -13);
			});
		case "Use Bunny License":
			return MenuControls.MakeAction(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(SoundID.Item92);
				NPC.UnlockOrExchangePet(ref NPC.boughtBunny, 656, "Misc.LicenseBunnyUsed", -14);
			});
		case "Smash Shadow Orb":
			return MenuControls.MakeAction(cheat, color, () => cheat, delegate
			{
				WorldInfo.SmashOrb(crimson: false);
			}, SinglePlayerOnly);
		case "Smash Crimson Heart":
			return MenuControls.MakeAction(cheat, color, () => cheat, delegate
			{
				WorldInfo.SmashOrb(crimson: true);
			}, SinglePlayerOnly);
		case "Spawn Meteor":
			return MenuControls.MakeAction(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				int count = ShiftCount(10);
				for (int i = 0; i < count; i++)
				{
					WorldGen.dropMeteor();
				}
			}, SinglePlayerOnly);
		case "Smash Demon Altar":
			return MenuControls.MakeAction(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				int count = ShiftCount(25);
				for (int i = 0; i < count; i++)
				{
					WorldGen.SmashAltar((int)Main.LocalPlayer.position.X / 16, (int)Main.LocalPlayer.position.Y / 16);
				}
			}, SinglePlayerOnly);
		case "Generate Hardmode Biomes":
			return MenuControls.MakeAction(cheat, color, () => WorldGen.TransformingWorld ? "Generating Hardmode..." : cheat, delegate
			{
				if (!WorldGen.TransformingWorld)
				{
					SoundEngine.PlaySound(11);
					WorldGen.StartHardmode(force: true);
				}
			}, SinglePlayerOnly);
		case "Spawn Plantera Bulb":
			return MenuControls.MakeAction(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				bool grew = false;
				int count = ShiftCount(10);
				for (int i = 0; i < count; i++)
				{
					grew |= WorldGen.GeneratePlanteraBulbOnAllMechsDefeated();
				}
				if (grew)
				{
					Main.NewText(IsaClient.Languages.L10n.T(Lang.misc[32].Value), 50, byte.MaxValue, 130);
				}
			}, SinglePlayerOnly);
		case "Purify World":
			return ConvertWorldButton(cheat, color, 0);
		case "Corrupt World":
			return ConvertWorldButton(cheat, color, 1);
		case "Hallow World":
			return ConvertWorldButton(cheat, color, 2);
		case "Crimson World":
			return ConvertWorldButton(cheat, color, 4);
		case "Jungle to Mushroom":
			return ConvertWorldButton(cheat, color, 3);
		case "Desert World":
			return ConvertWorldButton(cheat, color, 5);
		case "Snow World":
			return ConvertWorldButton(cheat, color, 6);
		case "Forest World":
			return ConvertWorldButton(cheat, color, 7);
		default:
			return null;
		}
	}

	private static UIElement ConvertWorldButton(string cheat, Color color, int conversion)
	{
		return MenuControls.MakeAction(cheat, color, () => cheat, delegate
		{
			if (!WorldGen.TransformingWorld && !WorldCommands.PurifyingWorld)
			{
				SoundEngine.PlaySound(11);
				WorldCommands.ConvertWorld(conversion);
			}
		}, SinglePlayerOnly);
	}

	private static readonly Dictionary<string, (Func<bool> Get, Action<bool> Set)> _worldFlags = new Dictionary<string, (Func<bool>, Action<bool>)>
	{
		{ "Slime Rain", (() => Main.slimeRain, v => Main.slimeRain = v) },
		{ "Blood Moon", (() => Main.bloodMoon, v => Main.bloodMoon = v) },
		{ "Eclipse", (() => Main.eclipse, v => Main.eclipse = v) },
		{ "Pumpkin Moon", (() => Main.pumpkinMoon, v => Main.pumpkinMoon = v) },
		{ "Frost Moon", (() => Main.snowMoon, v => Main.snowMoon = v) },
		{ "Killed a Clown", (() => NPC.downedClown, v => NPC.downedClown = v) },
		{ "Killed any Mechanical Boss", (() => NPC.downedMechBossAny, v => NPC.downedMechBossAny = v) },
		{ "Killed a Mourning Wood", (() => NPC.downedHalloweenTree, v => NPC.downedHalloweenTree = v) },
		{ "Killed a Pumpking", (() => NPC.downedHalloweenKing, v => NPC.downedHalloweenKing = v) },
		{ "Killed an Everscream", (() => NPC.downedChristmasTree, v => NPC.downedChristmasTree = v) },
		{ "Killed a Santa-NK1", (() => NPC.downedChristmasSantank, v => NPC.downedChristmasSantank = v) },
		{ "Killed an Ice Queen", (() => NPC.downedChristmasIceQueen, v => NPC.downedChristmasIceQueen = v) },
		{ "Ongoing Natural Party", (() => BirthdayParty.GenuineParty, v => BirthdayParty.GenuineParty = v) },
		{ "Ongoing Manually Started Party", (() => BirthdayParty.ManualParty, v => BirthdayParty.ManualParty = v) },
		{ "Ongoing Old One's Army", (() => DD2Event.Ongoing, v => DD2Event.Ongoing = v) },
		{ "Defeated Old One's Army T1", (() => DD2Event.DownedInvasionT1, v => DD2Event.DownedInvasionT1 = v) },
		{ "Defeated Old One's Army T2", (() => DD2Event.DownedInvasionT2, v => DD2Event.DownedInvasionT2 = v) },
		{ "Defeated Old One's Army T3", (() => DD2Event.DownedInvasionT3, v => DD2Event.DownedInvasionT3 = v) },
		{ "Used Advanced Combat Techniques", (() => NPC.combatBookWasUsed, v => NPC.combatBookWasUsed = v) },
		{ "Used Advanced Combat Techniques V2", (() => NPC.combatBookVolumeTwoWasUsed, v => NPC.combatBookVolumeTwoWasUsed = v) },
		{ "Used Peddler's Satchel", (() => NPC.peddlersSatchelWasUsed, v => NPC.peddlersSatchelWasUsed = v) },
		{ "Natural Lantern Night", (() => LanternNight.GenuineLanterns, v => LanternNight.GenuineLanterns = v) },
		{ "Manually Started Lantern Night", (() => LanternNight.ManualLanterns, v => LanternNight.ManualLanterns = v) },
		{ "Defeated Solar Pillar", (() => NPC.downedTowerSolar, v => NPC.downedTowerSolar = v) },
		{ "Defeated Vortex Pillar", (() => NPC.downedTowerVortex, v => NPC.downedTowerVortex = v) },
		{ "Defeated Nebula Pillar", (() => NPC.downedTowerNebula, v => NPC.downedTowerNebula = v) },
		{ "Defeated Stardust Pillar", (() => NPC.downedTowerStardust, v => NPC.downedTowerStardust = v) },
		{ "Force Christmas for Today", (() => Main.forceXMasForToday, v => Main.forceXMasForToday = v) },
		{ "Force Halloween for Today", (() => Main.forceHalloweenForToday, v => Main.forceHalloweenForToday = v) },
		{ "Purchased a Town Cat", (() => NPC.boughtCat, v => NPC.boughtCat = v) },
		{ "Purchased a Town Dog", (() => NPC.boughtDog, v => NPC.boughtDog = v) },
		{ "Purchased a Town Bunny", (() => NPC.boughtBunny, v => NPC.boughtBunny = v) },
		{ "Free Cake from Party Girl", (() => NPC.freeCake, v => NPC.freeCake = v) },
		{ "Rescued Nerdy Slime", (() => NPC.unlockedSlimeBlueSpawn, v => NPC.unlockedSlimeBlueSpawn = v) },
		{ "Unlocked Cool Slime", (() => NPC.unlockedSlimeGreenSpawn, v => NPC.unlockedSlimeGreenSpawn = v) },
		{ "Rescued Elder Slime", (() => NPC.unlockedSlimeOldSpawn, v => NPC.unlockedSlimeOldSpawn = v) },
		{ "Rescued Clumsy Slime", (() => NPC.unlockedSlimePurpleSpawn, v => NPC.unlockedSlimePurpleSpawn = v) },
		{ "Unlocked Diva Slime", (() => NPC.unlockedSlimeRainbowSpawn, v => NPC.unlockedSlimeRainbowSpawn = v) },
		{ "Rescued Surly Slime", (() => NPC.unlockedSlimeRedSpawn, v => NPC.unlockedSlimeRedSpawn = v) },
		{ "Rescued Mystic Slime", (() => NPC.unlockedSlimeYellowSpawn, v => NPC.unlockedSlimeYellowSpawn = v) },
		{ "Unlocked Squire Slime", (() => NPC.unlockedSlimeCopperSpawn, v => NPC.unlockedSlimeCopperSpawn = v) },
		{ "Unlocked Truffle", (() => NPC.unlockedTruffleSpawn, v => NPC.unlockedTruffleSpawn = v) },
		{ "Smashed Orb", (() => WorldGen.shadowOrbSmashed, v => WorldGen.shadowOrbSmashed = v) }
	};

	/// <summary>Rescues only matter to the single player world (the server owns them).</summary>
	private static readonly Dictionary<string, (Func<bool> Get, Action<bool> Set)> _rescueFlags = new Dictionary<string, (Func<bool>, Action<bool>)>
	{
		{ "Saved Goblin Tinkerer", (() => NPC.savedGoblin, v => NPC.savedGoblin = v) },
		{ "Saved Wizard", (() => NPC.savedWizard, v => NPC.savedWizard = v) },
		{ "Saved Mechanic", (() => NPC.savedMech, v => NPC.savedMech = v) },
		{ "Saved Stylist", (() => NPC.savedStylist, v => NPC.savedStylist = v) },
		{ "Saved Angler", (() => NPC.savedAngler, v => NPC.savedAngler = v) },
		{ "Saved Tax Collector", (() => NPC.savedTaxCollector, v => NPC.savedTaxCollector = v) },
		{ "Saved Tavernkeep", (() => NPC.savedBartender, v => NPC.savedBartender = v) },
		{ "Saved Golfer", (() => NPC.savedGolfer, v => NPC.savedGolfer = v) }
	};

	private static UIElement WorldInfo_Data(string cheat, Color color)
	{
		if (_worldFlags.TryGetValue(cheat, out var flag))
		{
			return MenuControls.MakeFlag(cheat, color, flag.Get, flag.Set);
		}
		if (_rescueFlags.TryGetValue(cheat, out var rescue))
		{
			return MenuControls.MakeFlag(cheat, color, rescue.Get, rescue.Set, () => Main.netMode == 0, () => cheat + ((Main.netMode == 0) ? "" : " (Unavailable)"));
		}
		switch (cheat)
		{
		case "Spawn Position":
			return MenuControls.MakeInfo(cheat, color, delegate
			{
				TeleportTools.TeleportToTile(Main.spawnTileX, Main.spawnTileY);
				SoundEngine.PlaySound(11);
			}, delegate
			{
				if (Main.netMode == 0 && Main.keyState.IsKeyDown(Keys.LeftShift))
				{
					Main.spawnTileX = (int)Main.LocalPlayer.Bottom.X / 16;
					Main.spawnTileY = (int)Main.LocalPlayer.Bottom.Y / 16;
					SoundEngine.PlaySound(10);
				}
			});
		case "Dungeon Position":
			return MenuControls.MakeInfo(cheat, color, delegate
			{
				if (WorldInfo.DoWeHaveDungeonPosition)
				{
					TeleportTools.TeleportToTile(Main.dungeonX, Main.dungeonY);
					SoundEngine.PlaySound(11);
				}
			}, delegate
			{
				if (Main.netMode == 0 && Main.keyState.IsKeyDown(Keys.LeftShift))
				{
					Main.dungeonX = (int)Main.LocalPlayer.Bottom.X / 16;
					Main.dungeonY = (int)Main.LocalPlayer.Bottom.Y / 16;
					SoundEngine.PlaySound(10);
				}
			}, () => !WorldInfo.DoWeHaveDungeonPosition);
		case "Moon Phase":
			return MenuControls.MakeCycle(cheat, color, delegate(bool back)
			{
				Main.moonPhase = MenuControls.Cycle(Main.moonPhase, 0, 7, back);
			});
		case "Moon Type":
			return MenuControls.MakeCycle(cheat, color, delegate(bool back)
			{
				Main.moonType = MenuControls.Cycle(Main.moonType, 0, 8, back);
			});
		case "Ice Background Style":
			return MenuControls.MakeCycle(cheat, color, delegate(bool back)
			{
				Main.iceBackStyle = MenuControls.Cycle(Main.iceBackStyle, 0, 3, back);
			});
		case "Jungle Background Style":
			return MenuControls.MakeCycle(cheat, color, delegate(bool back)
			{
				Main.jungleBackStyle = MenuControls.Cycle(Main.jungleBackStyle, 0, 1, back);
			});
		case "Hell Background Style":
			return MenuControls.MakeCycle(cheat, color, delegate(bool back)
			{
				Main.hellBackStyle = MenuControls.Cycle(Main.hellBackStyle, 0, 2, back);
			});
		case "Clouds":
			return MenuControls.MakeSlider(cheat, color, () => "Clouds: " + Main.numClouds, () => (float)Main.numClouds / 200f, delegate(float f)
			{
				float value = Main.keyState.IsKeyDown(Keys.LeftShift) ? ((float)Math.Round(f, 1)) : f;
				Main.numClouds = Utils.Clamp((int)(value * 200f), 0, 200);
			}, 6941);
		case "Raining":
			return MenuControls.MakeState(cheat, color, () => Main.raining);
		case "Sandstorm Ongoing":
			return MenuControls.MakeState(cheat, color, () => Sandstorm.Happening);
		default:
			return MenuControls.MakeInfo(cheat, color);
		}
	}

	/// <summary>These only explain why the row is greyed out, so they show only on servers.</summary>
	private static readonly HashSet<string> _serverOnlyTooltips = new HashSet<string>
	{
		"Smash Shadow Orb", "Smash Crimson Heart", "Spawn Meteor", "Smash Demon Altar", "Generate Hardmode Biomes", "Spawn Plantera Bulb", "Purify World", "Corrupt World", "Hallow World", "Crimson World",
		"Jungle to Mushroom", "Desert World", "Snow World", "Forest World", "Spawn Wall of Flesh", "Spawn Plantera", "Spawn Golem", "Spawn Mechdusa", "Spawn Empress of Light", "Spawn Lunatic Cultist",
		"Trigger Lunar Apocalypse"
	};

	/// <summary>Boss section titles: Shift+click spawns every boss of the section.</summary>
	internal static void DecorateHeader(UITextPanel<LocalizedText> header, int section)
	{
		if (section != 1 && section != 2)
		{
			return;
		}
		bool hardmode = section == 2;
		header.OnLeftClick += delegate
		{
			if (Main.keyState.IsKeyDown(Keys.LeftShift))
			{
				WorldInfo.SpawnAllBosses(hardmode);
			}
		};
		header.OnUpdate += delegate(UIElement element)
		{
			if (element.IsMouseHovering)
			{
				Main.instance.MouseText(IsaClient.Languages.L10n.T("Hold Left Shift and click to spawn every " + (hardmode ? "Hardmode" : "Pre-Hardmode") + " boss."), 0, 0);
			}
		};
	}

	/// <summary>Hover text of a row of this category (null: none).</summary>
	internal static string Tooltip(string name)
	{
		string tooltip = name switch
		{
			"Smash Shadow Orb" => "This utility is only available in single player.",
			"Smash Crimson Heart" => "This utility is only available in single player.",
			"Spawn Meteor" => "This utility is only available in single player.",
			"Smash Demon Altar" => "This utility is only available in single player.",
			"Generate Hardmode Biomes" => "This utility is only available in single player.",
			"Spawn Plantera Bulb" => "This utility is only available in single player.",
			"Purify World" => "The world can only be transformed in single player.",
			"Corrupt World" => "The world can only be transformed in single player.",
			"Hallow World" => "The world can only be transformed in single player.",
			"Crimson World" => "The world can only be transformed in single player.",
			"Jungle to Mushroom" => "The world can only be transformed in single player.",
			"Desert World" => "The world can only be transformed in single player.",
			"Snow World" => "The world can only be transformed in single player.",
			"Forest World" => "The world can only be transformed in single player.",
			"Spawn Wall of Flesh" => "This boss can only be spawned with the UI in single player.",
			"Spawn Plantera" => "This boss can only be spawned with the UI in single player.",
			"Spawn Golem" => "You need to be within 20 blocks of a Lihzahrd Altar to spawn Golem.",
			"Spawn Mechdusa" => "Mechdusa can only be spawned in single player, or if the server's world is a For The Worthy or Don't Dig Up seed.",
			"Spawn Empress of Light" => "This boss can only be spawned with the UI in single player.",
			"Spawn Lunatic Cultist" => "This boss can only be spawned with the UI in single player.",
			"Trigger Lunar Apocalypse" => "You can only start the apocalypse in single player.",
			"Spawn Position" => "Click to teleport to the world spawn.\nSingle player: Shift + right click moves the spawn to where you stand.",
			"Dungeon Position" => "Click to teleport to the dungeon.\nSingle player: Shift + right click moves it to where you stand.",
			"Clouds" => "How many clouds the sky has (client-side).\nHold Left Shift for 10% steps.",
			"World Difficulty" => "Cycles this client's copy of the world difficulty.",
			"World Evil" => "Click to switch between Corruption and Crimson (this client only).",
			"Use Enchanted Sundial" => "Fast forwards to dawn like the Enchanted Sundial (the server's cooldown applies).",
			"Use Enchanted Moondial" => "Fast forwards to dusk like the Enchanted Moondial (the server's cooldown applies).",
			"Hardmode" => "This client's copy of the Hardmode flag.",
			_ => null,
		};
		return (tooltip != null && _serverOnlyTooltips.Contains(name) && !ServerOnlyActionUnavailable(name)) ? null : tooltip;
	}

	/// <summary>Server-only actions only explain themselves when they cannot run.</summary>
	private static bool ServerOnlyActionUnavailable(string name)
	{
		return name switch
		{
			"Spawn Golem" => !WorldInfo.GolemSpawnEnabled,
			"Spawn Mechdusa" => !WorldInfo.MechdusaEnabled,
			_ => Main.netMode == 1,
		};
	}
}
