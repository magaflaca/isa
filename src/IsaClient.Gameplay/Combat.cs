using System;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using IsaClient.Core;
using IsaClient.Movement;
using IsaClient.Networking;

namespace IsaClient.Gameplay;

/// <summary>Combat helpers: kill aura, butcher, crystal kill aura, projectile killing, NPC spawning and shimmering.</summary>
public static class Combat
{
	internal static void ShimmerAll(string target)
	{
		switch (target)
		{
		case "town":
		case "townnpc":
		case "tn":
		case "townalt":
		case "townnpcalt":
		case "tnalt":
		{
			int variation = target.EndsWith("alt") ? 1 : 0;
			for (int i = 0; i < Main.maxNPCs; i++)
			{
				NPC npc = Main.npc[i];
				if (npc.active && NPCID.Sets.ShimmerTownTransform[npc.type] && npc.townNpcVariationIndex == variation)
				{
					npc.AddBuff(353, 100);
				}
			}
			ClientChat.Info((variation == 0) ? "Shimmered all Town NPCs into their shimmer variants!" : "Shimmered all Town NPCs to normal!");
			break;
		}
		case "enemy":
		case "unfriendly":
		case "en":
		case "uf":
			for (int j = 0; j < Main.maxNPCs; j++)
			{
				NPC npc = Main.npc[j];
				if (npc.active && !npc.friendly && !npc.CountsAsACritter)
				{
					npc.AddBuff(353, 100);
				}
			}
			ClientChat.Info("Shimmered all enemies!");
			break;
		case "":
			ClientChat.Error("No NPC type specified (town, townalt or enemy).");
			break;
		default:
			ClientChat.Error("Invalid NPC type specified (town, townalt or enemy).");
			break;
		}
	}

	/// <summary>.spawnmob / .summon / .summonmob / .npc</summary>
	internal static void SummonMobCommand(CommandContext command)
	{
		string[] array = command.Parts;
		if (ServerDetection.OnTshockServer)
		{
			ClientChat.Error("Spawnmob doesn't work on TShock Servers.");
			return;
		}
		if (array.Length <= 1)
		{
			ClientChat.Error("Usage: .spawnmob <npc id or name> [amount]");
			return;
		}
		if (!TryParseNpc(array[1], out var npcType, out var npcName) || npcType <= -66 || npcType >= NPCID.Count || npcType == 0)
		{
			ClientChat.Error("Invalid NPC.");
			return;
		}
		int amount = 1;
		if (array.Length > 2 && (!int.TryParse(array[2], out amount) || amount < 1))
		{
			ClientChat.Error("Invalid amount.");
			return;
		}
		amount = Utils.Clamp(amount, 1, 200);
		var mouse = Main.MouseWorld.ToTileCoordinates();
		int columns = Math.Min((int)Math.Ceiling(Math.Sqrt(amount)), 51);
		int rows = Math.Min((int)Math.Ceiling((double)amount / columns), 51);
		double stepX = (columns > 1) ? (50.0 / (columns - 1)) : 0.0;
		double stepY = (rows > 1) ? (50.0 / (rows - 1)) : 0.0;
		for (int i = 0; i < amount; i++)
		{
			int x = mouse.X;
			int y = mouse.Y;
			if (amount > 1)
			{
				x += (int)Math.Round(-25.0 + stepX * (i % columns));
				y += (int)Math.Round(-25.0 + stepY * (i / columns));
			}
			if (Main.netMode != 1)
			{
				NPC.NewNPC(new EntitySource_FishedOut(Main.LocalPlayer), x * 16, y * 16, npcType);
			}
			else
			{
				NetMessage.SendData(130, -1, -1, null, x, y, npcType);
			}
		}
		ClientChat.Info("Spawned " + amount + " " + npcName);
	}

	internal static bool TryParseNpc(string value, out int type, out string name)
	{
		FieldInfo field = typeof(NPCID).GetFields(BindingFlags.Public | BindingFlags.Static).FirstOrDefault((FieldInfo f) => f.IsLiteral && f.Name.Equals(value, StringComparison.OrdinalIgnoreCase));
		if (field != null)
		{
			type = Convert.ToInt32(field.GetRawConstantValue());
			name = field.Name;
			return true;
		}
		if (int.TryParse(value, out type))
		{
			name = (type > -66 && type < NPCID.Count) ? NPCID.Search.GetName(type) : type.ToString();
			return true;
		}
		name = "";
		return false;
	}

	public static bool ShimmerGodMode;

	public static bool CrystalKillAuraEnabled;

	public static bool AutoKillProjectiles;

	/// <summary>.crystalaura: Old One's Army enemies close to the Eternia Crystal (or its lanes) die instantly.</summary>
	internal static void CrystalKillAura()
	{
		int crystal = NPC.FindFirstNPC(548);
		if (crystal < 0)
		{
			return;
		}
		Vector2 crystalCenter = Main.npc[crystal].Center;
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			NPC npc = Main.npc[i];
			if (!npc.active || npc.friendly || npc.townNPC || npc.dontTakeDamage || NPCID.Sets.CountsAsCritter[npc.type] || npc.type == 548)
			{
				continue;
			}
			float distance = npc.Distance(crystalCenter);
			bool near = (npc.type == 636 && distance <= 2048f) || (npc.type == 370 && distance <= 512f) || (!NPCID.Sets.BelongsToInvasionOldOnesArmy[npc.type] && npc.type != 396 && npc.type != 397 && npc.type != 398 && distance <= 256f) || (NPCID.Sets.BelongsToInvasionOldOnesArmy[npc.type] && distance <= 256f);
			if (!near)
			{
				continue;
			}
			npc.StrikeNPCNoInteraction(32767, 10f, -npc.direction);
			if (Main.netMode != 0)
			{
				NetMessage.SendData(28, -1, -1, null, i, 32767f, 10f, -npc.direction, 1);
			}
		}
	}

	public static bool KillAura;

	public static int KillAuraSize;

	public static bool AutoButcher;

	public static int AutoButcherDamage;

	public static void KillAllProjectiles(bool selfOnly = false)
	{
		for (int i = 0; i < Main.maxProjectiles; i++)
		{
			Projectile projectile = Main.projectile[i];
			bool owned = projectile.owner == Main.myPlayer;
			if (!projectile.active)
			{
				continue;
			}
			if (selfOnly && owned)
			{
				projectile.Kill();
			}
			else if (!selfOnly)
			{
				projectile.active = false;
			}
			if (owned && Main.netMode == 1)
			{
				NetMessage.SendData(29, -1, -1, null, projectile.key, float.NaN, float.NaN);
			}
		}
		if (selfOnly)
		{
			ClientChat.Info("Removed all projectiles owned by you.");
		}
	}

	public static void ButcherMobsAndNpcs(int damage, int mode)
	{
		int num = 0;
		bool moved = false;
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			NPC nPC = Main.npc[i];
			if (damage <= 0)
			{
				damage = ((!AutoButcher) ? 5000 : 9999);
			}
			if (nPC.active && nPC.type != 488 && ((mode == 1) ? (!nPC.townNPC && !nPC.friendly) : nPC.townNPC))
			{
				if (Main.netMode == 0)
				{
					// Just enough to kill it, a few times if armor or damage reduction eats some. Never int.MaxValue: the
					// game's hit effects make dust in loops of damage / max life * 100 (billions of times: the game froze).
					for (int strike = 0; strike < 6 && nPC.active && nPC.life > 0; strike++)
					{
						int lethal = (int)Math.Min(int.MaxValue / 4, (long)nPC.life * 2 + nPC.defense + 10);
						nPC.StrikeNPCNoInteraction(lethal, 10f, -nPC.direction);
					}
					num++;
					continue;
				}
				nPC.StrikeNPCNoInteraction(damage, 10f, -nPC.direction);
				if (Main.netMode != 0)
				{
					TeleportTools.FakeNetMessage13(nPC.position, Main.LocalPlayer.selectedItem);
					NetMessage.SendData(28, -1, -1, null, i, damage, 10f, -nPC.direction, 1);
					moved = true;
				}
				num++;
			}
		}
		if (moved)
		{
			NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
		}
		if (!AutoButcher)
		{
			ClientChat.Info("You've butchered " + num + " npc's");
		}
	}

	public static void KillBubble(int radius)
	{
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			NPC nPC = Main.npc[i];
			if (nPC.active && !nPC.friendly && nPC.type != 488 && !nPC.dontTakeDamage && WithinPlayerRadius(radius, nPC.position.ToTileCoordinates()))
			{
				int damage = 1500;
				nPC.StrikeNPCNoInteraction(damage, 0.5f, -nPC.direction);
				if (Main.netMode != 0)
				{
					NetMessage.SendData(28, -1, -1, null, i, damage, 0.5f, -nPC.direction, 1);
				}
			}
		}
	}

	/// <summary>True when the entity is inside the square of <paramref name="radius"/> tiles around the player (any size, no overflow).</summary>
	public static bool WithinPlayerRadius(int radius, Point entity)
	{
		Point point = Main.LocalPlayer.position.ToTileCoordinates();
		return Math.Abs((long)entity.X - point.X) <= radius && Math.Abs((long)entity.Y - point.Y) <= radius;
	}

	public static void ShimmerNPCs(int kind, bool toNormal = false)
	{
		bool flag = false;
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			NPC nPC = Main.npc[i];
			if (!nPC.active)
			{
				continue;
			}
			int type = nPC.type;
			bool shouldShimmer = kind switch
			{
				0 => NPCID.Sets.ShimmerTownTransform[type] && nPC.townNpcVariationIndex == (toNormal ? 1 : 0),
				1 => !nPC.friendly && !nPC.CountsAsACritter && (NPCID.Sets.ShimmerTransformToNPC[type] >= 0 || NPCID.Sets.ShimmerTransformToItem[type] >= 0),
				_ => nPC.CountsAsACritter && !NPCID.Sets.ShimmerImmunity[type],
			};
			if (shouldShimmer)
			{
				nPC.AddBuff(353, 100);
				flag = true;
			}
		}
		switch (kind)
		{
		case 0:
			ClientChat.Info(flag ? (toNormal ? "Shimmered all Town NPCs to normal!" : "Shimmered all Town NPCs into their shimmer variants!") : "No Town NPCs to shimmer...");
			break;
		case 1:
			ClientChat.Info(flag ? "Shimmered all enemies!" : "No hostile NPCs to shimmer...");
			break;
		default:
			ClientChat.Info(flag ? "Shimmered all critters!" : "No critters to shimmer...");
			break;
		}
	}

	#region Chat commands

	/// <summary>.killprojectiles / .killproj / .kpj</summary>
	internal static void KillProjectilesCommand(CommandContext command)
	{
		KillAllProjectiles(selfOnly: true);
	}

	/// <summary>.killaura / .killbubble</summary>
	internal static void KillBubbleCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out int size))
			{
				KillAuraSize = Math.Max(size, 0);
				KillAura = KillAuraSize > 0;
				ClientChat.Info("Kill Aura " + (KillAura ? "En" : "Dis") + "abled  Size: " + KillAuraSize);
			}
			else
			{
				ClientChat.Error("Invalid Value ");
			}
		}
		else
		{
			if (KillAuraSize <= 0)
			{
				KillAuraSize = 1;
			}
			KillAura = !KillAura;
			ClientChat.Info("Kill Aura " + (KillAura ? "En" : "Dis") + "abled" + (KillAura ? ("  Size: " + KillAuraSize) : ""));
		}
	}

	/// <summary>.butcher / .killall</summary>
	internal static void ButcherCommand(CommandContext command)
	{
		int result = 0;
		if (command.Parts.Length > 1 && !int.TryParse(command.Parts[1], out result))
		{
			ClientChat.Error("Invalid Ammount ");
			return;
		}
		ButcherMobsAndNpcs(result, 1);
	}

	/// <summary>.butcherfriendly / .killtownnpcs</summary>
	internal static void ButcherFriendlyCommand(CommandContext command)
	{
		ButcherMobsAndNpcs(0, 2);
	}

	/// <summary>.autobutcher</summary>
	internal static void AutoButcherCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out AutoButcherDamage))
			{
				AutoButcher = true;
			}
			else
			{
				ClientChat.Error("Invalid Value ");
			}
		}
		else
		{
			AutoButcherDamage = 0;
			AutoButcher = !AutoButcher;
		}
		ClientChat.Info("Auto Butcher " + (AutoButcher ? "En" : "Dis") + "abled");
	}

	/// <summary>.shimmertown</summary>
	internal static void ShimmerTownCommand(CommandContext command)
	{
		ShimmerNPCs(0, command.Parts.Length > 1 && command.Parts[1] == "normal");
	}

	/// <summary>.shimmerenemies</summary>
	internal static void ShimmerEnemiesCommand(CommandContext command)
	{
		ShimmerNPCs(1);
	}

	/// <summary>.shimmercritters</summary>
	internal static void ShimmerCrittersCommand(CommandContext command)
	{
		ShimmerNPCs(2);
	}

	/// <summary>.crystalkillaura / .crystalaura / .cka / .ca</summary>
	internal static void CrystalKillAuraCommand(CommandContext command)
	{
		CrystalKillAuraEnabled = !CrystalKillAuraEnabled;
		ClientChat.Info(CrystalKillAuraEnabled ? "Enemies near the Eternia Crystal will now be destroyed automatically." : "Enemies near the Eternia Crystal will no longer be destroyed automatically.");
	}

	/// <summary>.shimmerall / .shma</summary>
	internal static void ShimmerAllCommand(CommandContext command)
	{
		ShimmerAll((command.Parts.Length > 1) ? command.Parts[1].ToLower() : "");
	}

	/// <summary>.shgod / .shimmergodmode</summary>
	internal static void ShimmerGodmodeCommand(CommandContext command)
	{
		ShimmerGodMode = !ShimmerGodMode;
		ClientChat.Info("Shimmer Godmode " + (ShimmerGodMode ? "En" : "Dis") + "abled.");
		if (!ShimmerGodMode)
		{
			command.Player.ClearBuff(353);
		}
	}

	/// <summary>.autokillprojectiles / .autokillproj / .akpj</summary>
	internal static void AutoKillProjectilesCommand(CommandContext command)
	{
		AutoKillProjectiles = !AutoKillProjectiles;
		ClientChat.Info("AutoKill Projectiles " + (AutoKillProjectiles ? "En" : "Dis") + "abled");
	}

	#endregion
}
