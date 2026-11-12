using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using IsaClient.Building;
using IsaClient.Core;
using IsaClient.Rendering;

namespace IsaClient.Gameplay;

/// <summary>Player cheats of the panel: god mode, infinite mana/ammo/breath, ranges, drops, buffs on death and more.</summary>
public static class PlayerCheats
{
	[Setting("AntiDeath", Remembered = true)]
	public static bool AntiDeathGod;

	[Setting("DodgeEverything", Remembered = true)]
	public static bool DodgeEverything;

	[Setting("InstantRegen", Remembered = true)]
	public static bool InstantRegen;

	[Setting("InfiniteMana", Remembered = true)]
	public static bool InfiniteMana;

	[Setting("AntiDebuff", Remembered = true)]
	public static bool AntiDebuff;

	[Setting("InstantRespawn", Remembered = true)]
	public static bool InstantRespawn;

	[Setting("ExtendItemReach", Remembered = true)]
	public static bool ExtendedPickUpRange;

	[Setting("ExtendPlaceRange", Remembered = true)]
	public static bool ExtendedPlacementRange;

	[Setting("InfiniteMinions", Remembered = true)]
	public static bool InfiniteMinions;

	[Setting("InfiniteAmmo", Remembered = true)]
	public static bool InfiniteAmmo;

	[Setting("InfiniteBreath", Remembered = true)]
	public static bool InfiniteBreath;

	[Setting("NoFallDamage", Remembered = true)]
	public static bool NoFallDamage;

	[Setting("IgnoreLiquids", Remembered = true)]
	public static bool IgnoreLiquids;

	[Setting("IgnoreDamageBlocks", Remembered = true)]
	public static bool IgnoreDamageBlocks;

	/// <summary>0 = drop inventory (vanilla), 1 = drop a copy and keep everything, 2 = drop nothing.</summary>
	[Setting("ItemLossType", Remembered = true, Min = 0, Max = 2)]
	public static int DeathItemDropMode;

	/// <summary>0 = lose all (vanilla), 1 keep buffs, 2 keep long buffs, 3 keep debuffs, 4 keep long debuffs, 5 keep all.</summary>
	[Setting("BuffPersistenceType", Remembered = true, Min = 0, Max = 5)]
	public static int PersistBuffsMode;

	[Setting("UnlimitTeleportRod", Remembered = true)]
	public static bool UnrestrictTeleportRod;

	[Setting("SupressHorrified", Remembered = true)]
	public static bool SuppressHorrified;

	[Setting("SuppressNoBuilding", Remembered = true)]
	public static bool SuppressCreativeShock;

	[Setting("IgnoreCrowdControl", Remembered = true)]
	public static bool IgnoreCC;

	[Setting("ForceExtraAccessories", Remembered = true)]
	public static bool ExtraAccSlots;

	[Setting("EquipDuplicateAccessories", Remembered = true)]
	public static bool EquipDupeAccessory;

	[Setting("GrappleWithMounts", Remembered = true)]
	public static bool GrappleMounted;

	/// <summary>Use weapons and items while you are the Rat, the Bat or the Pixie (1.4.5 mounts that don't let you hold items).</summary>
	[Setting("ItemsOnTransformMounts", Remembered = true)]
	public static bool ItemsOnTransformMounts;

	/// <summary>
	/// The local player is on a mount that takes the items away (MountID.Sets.DontHoldItems: Rat, Bat, Pixie) and
	/// <see cref="ItemsOnTransformMounts"/> is on: the mount neither blocks the items nor gets off when one is used.
	/// </summary>
	public static bool KeepsItemsOnMount(Player player)
	{
		return ItemsOnTransformMounts && player != null && player.whoAmI == Main.myPlayer && !Main.gameMenu && player.mount.Active && player.mount.Type >= 0 && MountID.Sets.DontHoldItems[player.mount.Type];
	}

	[Setting("SwapItemsAnytime", Remembered = true)]
	public static bool NoSwapRestriction;

	[Setting("HammerAnything", Remembered = true)]
	public static bool HammerAnything;

	[Setting("FullHpRespawn", Remembered = true)]
	public static bool AlwaysPvpDeath;

	[Setting("FreeCrafting", Remembered = true)]
	public static bool FreeCraft;

	[Setting("CraftAnywhere", Remembered = true)]
	public static bool CraftAnywhere;

	[Setting("ExplodeOnHit", Remembered = true)]
	public static bool AlwaysSolarExplosion;

	[Setting("AlwaysGuardian", Remembered = true)]
	public static bool AlwaysStardustGuardian;

	[Setting("AlwaysSpiritHeal", Remembered = true)]
	public static bool AlwaysGhostHeal;

	[Setting("AlwaysSpiritHurt", Remembered = true)]
	public static bool AlwaysGhostHurt;

	[Setting("AlwaysNebulaBonus", Remembered = true)]
	public static bool AlwaysNebulaSet;

	/// <summary>0 = no override, 1 Shield of Cthulhu, 2 Master Ninja Gear, 3 Solar armor, 5 Crystal Assassin.</summary>
	[Setting("DashOverrideType", Remembered = true, Min = 0, Max = 5)]
	public static int ForceDashType;

	/// <summary>0 = no override, 1 = max vanilla luck, 2 = "dream" luck.</summary>
	[Setting("LuckOverrideType", Remembered = true, Min = 0, Max = 2)]
	public static int DreamLuckMode;

	/// <summary>Set while something must drop the inventory for real (Item Drop Mode is ignored then).</summary>
	public static bool ForcedDropInventory;

	private static bool _droppingDuplicate;

	/// <summary>Cheats → Player → Disable All.</summary>
	public static void DisablePlayerCheats()
	{
		AntiDeathGod = false;
		DodgeEverything = false;
		InstantRegen = false;
		InfiniteMana = false;
		AntiDebuff = false;
		InstantRespawn = false;
		ExtendedPickUpRange = false;
		ExtendedPlacementRange = false;
		InfiniteMinions = false;
		InfiniteAmmo = false;
		InfiniteBreath = false;
		NoFallDamage = false;
		IgnoreLiquids = false;
		IgnoreDamageBlocks = false;
		DeathItemDropMode = 0;
		PersistBuffsMode = 0;
		UnrestrictTeleportRod = false;
		LockOnHelper.ForceUsability = false;
		SuppressHorrified = false;
		SuppressCreativeShock = false;
		IgnoreCC = false;
		ExtraAccSlots = false;
		EquipDupeAccessory = false;
		GrappleMounted = false;
		ItemsOnTransformMounts = false;
		NoSwapRestriction = false;
		HammerAnything = false;
		Placement.PlaceAnywhere = false;
		AlwaysPvpDeath = false;
		FreeCraft = false;
		CraftAnywhere = false;
		AlwaysSolarExplosion = false;
		AlwaysStardustGuardian = false;
		AlwaysGhostHeal = false;
		AlwaysGhostHurt = false;
		AlwaysNebulaSet = false;
		ForceDashType = 0;
		DreamLuckMode = 0;
		BuffTools.SetRemoveDebuffs(on: false);
	}

	private static bool LocalCheats(Player player)
	{
		if (player.whoAmI == Main.myPlayer)
		{
			return !Main.gameMenu;
		}
		return false;
	}

	/// <summary>Every frame (ClientLifecycle.OnUpdate): mana/life refills and Instant Respawn.</summary>
	internal static void UpdatePlayerCheats()
	{
		if (Main.gameMenu)
		{
			return;
		}
		Player player = Main.LocalPlayer;
		if (InfiniteMana)
		{
			player.statMana = player.statManaMax2;
		}
		if (InstantRespawn && player.dead && player.respawnTimer > 0)
		{
			if (Main.mouseItem.type > 0)
			{
				Main.playerInventory = true;
			}
			player.respawnTimer = 0;
			player.Spawn(PlayerSpawnContext.ReviveFromDeath);
		}
	}

	/// <summary>Player.Update, local player, right after the buffs were updated.</summary>
	public static void PlayerCheatsAfterBuffs(Player player)
	{
		if (!LocalCheats(player))
		{
			return;
		}
		if (AntiDebuff)
		{
			bool removed = false;
			for (int i = 0; i < Player.maxBuffs; i++)
			{
				if (IsBlockedDebuff(player.buffType[i]))
				{
					player.DelBuff(i);
					i--;
					removed = true;
				}
			}
			if (removed && Main.netMode == 1)
			{
				NetMessage.SendData(50, -1, -1, null, Main.myPlayer);
			}
		}
		if (DreamLuckMode > 0)
		{
			player.luckPotion = (byte)((DreamLuckMode < 2) ? 3 : 120);
		}
	}

	/// <summary>Anti Debuff: debuffs that are not area effects (and not Featherfall/Ironskin style exceptions).</summary>
	public static bool IsBlockedDebuff(int type)
	{
		if (type > 0 && type < BuffID.Count && Main.debuff[type] && !BuffID.Sets.IsBuffAppliedInArea[type] && type != 28)
		{
			return type != 34;
		}
		return false;
	}

	/// <summary>Player.AddBuff: Anti Debuff refuses debuffs for the local player.</summary>
	public static bool BlocksBuff(Player player, int type)
	{
		if (AntiDebuff && LocalCheats(player))
		{
			return IsBlockedDebuff(type);
		}
		return false;
	}

	/// <summary>Player.Update, local player, after UpdateEquips.</summary>
	public static void PlayerCheatsAfterEquips(Player player)
	{
		if (!LocalCheats(player))
		{
			return;
		}
		if (DreamLuckMode > 0)
		{
			player.equipmentBasedLuckBonus = (DreamLuckMode < 2) ? 0.1f : 1000000f;
		}
		if (NoFallDamage)
		{
			player.noFallDmg = true;
		}
		if (IgnoreLiquids)
		{
			player.ignoreWater = true;
		}
		Cosmetics.ApplyAfterEquips(player);
	}

	/// <summary>Player.Update, local player, after the armor set bonuses.</summary>
	public static void PlayerCheatsAfterArmorSets(Player player)
	{
		if (!LocalCheats(player))
		{
			return;
		}
		if (AlwaysStardustGuardian)
		{
			if (player.FindBuffIndex(187) == -1)
			{
				player.AddBuff(187, 3600);
			}
			if (player.ownedProjectileCounts[623] < 1)
			{
				int damage = 30;
				int index = Projectile.NewProjectile(player.GetProjectileSource_SetBonus(7), player.Center.X, player.Center.Y, 0f, -1f, 623, damage, 10f, Main.myPlayer);
				Main.projectile[index].originalDamage = damage;
			}
		}
		if (AlwaysNebulaSet && !player.setNebula && player.nebulaCD > 0)
		{
			player.nebulaCD--;
		}
		if (AlwaysGhostHeal)
		{
			player.ghostHeal = true;
		}
		if (AlwaysGhostHurt)
		{
			player.ghostHurt = true;
		}
		if (ForceDashType > 0)
		{
			player.dashType = ForceDashType;
		}
		if (InfiniteMinions)
		{
			player.maxMinions = 10000;
			player.maxTurrets = 10000;
		}
		if (InstantRegen && !player.dead)
		{
			player.statLife = player.statLifeMax2;
		}
		if (InfiniteMana)
		{
			player.statMana = player.statManaMax2;
		}
	}

	/// <summary>Player.UpdateLuckFactors: Luck Override.</summary>
	public static void ApplyLuckOverride(Player player)
	{
		if (DreamLuckMode <= 0 || !LocalCheats(player))
		{
			return;
		}
		player.ladyBugLuckTimeLeft = (DreamLuckMode < 2) ? 86400 : 864000000;
		player.torchLuck = (DreamLuckMode < 2) ? 1f : 1000000f;
		player.coinLuck = (DreamLuckMode < 2) ? 1000000f : float.MaxValue;
		if (!player.HasGardenGnomeNearby)
		{
			player.HasGardenGnomeNearby = true;
		}
		player.luckNeedsSync = true;
	}

	/// <summary>Player.UpdateDead: Buff Loss Mode decides which buffs survive your death.</summary>
	public static bool KeepsBuffOnDeath(Player player, int slot)
	{
		int type = player.buffType[slot];
		if (type > 0 && Main.persistentBuff[type])
		{
			return true;
		}
		if (type <= 0 || player.whoAmI != Main.myPlayer)
		{
			return false;
		}
		bool debuff = Main.debuff[type];
		bool longBuff = player.buffTime[slot] >= 5184000;
		return PersistBuffsMode switch
		{
			1 => !debuff,
			2 => !debuff && longBuff,
			3 => debuff,
			4 => debuff && longBuff,
			5 => true,
			_ => false,
		};
	}

	/// <summary>
	/// Player.DropItems: Item Drop Mode. Returns true when it handled the drop (nothing dropped, or a copy was dropped and
	/// the inventory kept).
	/// </summary>
	public static bool OverrideDropItems(Player player, bool gemsOnly)
	{
		if (_droppingDuplicate || ForcedDropInventory || DeathItemDropMode == 0 || player.whoAmI != Main.myPlayer)
		{
			return false;
		}
		if (DeathItemDropMode == 2)
		{
			return true;
		}
		Item[] inventory = CloneItems(player.inventory);
		Item[] armor = CloneItems(player.armor);
		Item[] dye = CloneItems(player.dye);
		Item[] miscEquips = CloneItems(player.miscEquips);
		Item[] miscDyes = CloneItems(player.miscDyes);
		Item[][] loadoutArmor = new Item[player.Loadouts.Length][];
		Item[][] loadoutDye = new Item[player.Loadouts.Length][];
		for (int i = 0; i < player.Loadouts.Length; i++)
		{
			loadoutArmor[i] = CloneItems(player.Loadouts[i].Armor);
			loadoutDye[i] = CloneItems(player.Loadouts[i].Dye);
		}
		_droppingDuplicate = true;
		try
		{
			player.DropItems(gemsOnly);
		}
		finally
		{
			_droppingDuplicate = false;
		}
		RestoreItems(player.inventory, inventory);
		RestoreItems(player.armor, armor);
		RestoreItems(player.dye, dye);
		RestoreItems(player.miscEquips, miscEquips);
		RestoreItems(player.miscDyes, miscDyes);
		for (int j = 0; j < player.Loadouts.Length; j++)
		{
			RestoreItems(player.Loadouts[j].Armor, loadoutArmor[j]);
			RestoreItems(player.Loadouts[j].Dye, loadoutDye[j]);
		}
		return true;
	}

	private static Item[] CloneItems(Item[] items)
	{
		Item[] copy = new Item[items.Length];
		for (int i = 0; i < items.Length; i++)
		{
			copy[i] = items[i].Clone();
		}
		return copy;
	}

	private static void RestoreItems(Item[] target, Item[] source)
	{
		for (int i = 0; i < target.Length && i < source.Length; i++)
		{
			target[i] = source[i];
		}
	}

	/// <summary>Names shown on the right of the cycling buttons.</summary>
	public static string PlayerCheatValue(string name)
	{
		switch (name)
		{
		case "Item Drop Mode":
			return DeathItemDropMode switch
			{
				1 => "Duplicate Inventory",
				2 => "Don't Drop Anything",
				_ => "Drop Inventory (Vanilla)",
			};
		case "Buff Loss Mode":
			return PersistBuffsMode switch
			{
				1 => "Keep Buffs",
				2 => "Keep Buffs (Long)",
				3 => "Keep Debuffs",
				4 => "Keep Debuffs (Long)",
				5 => "Keep All",
				_ => "Lose All (Vanilla)",
			};
		case "Force Dash Type":
			return ForceDashType switch
			{
				1 => Lang.GetItemNameValue(977),
				2 => Lang.GetItemNameValue(3097),
				3 => Lang.GetBuffName(172),
				4 => "Unknown Dash Type (4)",
				5 => "Crystal Assassin",
				_ => "No Override",
			};
		case "Luck Override":
			return DreamLuckMode switch
			{
				1 => "Max Vanilla Luck",
				2 => "Dream Luck",
				_ => "No Override",
			};
		default:
			return null;
		}
	}

	[Setting("JourneyMenu", Remembered = true)]
	public static bool JourneyMenu;

	[Setting("Godmode", Remembered = true)]
	public static bool GodMode;

	[Setting("FreeShops", Remembered = true)]
	public static bool FreeShops;

	#region Chat commands

	/// <summary>.fc / .freecraft</summary>
	internal static void FreeCraftCommand(CommandContext command)
	{
		FreeCraft = !FreeCraft;
		ClientChat.Info("Free Crafting " + (FreeCraft ? "en" : "dis") + "abled.");
		Recipe.UpdateRecipeList();
		ClientSettings.Save();
	}

	/// <summary>.craftanywhere</summary>
	internal static void CraftAnywhereCommand(CommandContext command)
	{
		CraftAnywhere = !CraftAnywhere;
		ClientChat.Info("Craft Anywhere " + (CraftAnywhere ? "en" : "dis") + "abled.");
		Recipe.UpdateRecipeList();
		ClientSettings.Save();
	}

	/// <summary>.nohorror</summary>
	internal static void NoHorrorCommand(CommandContext command)
	{
		SuppressHorrified = !SuppressHorrified;
		ClientChat.Info((!SuppressHorrified) ? "The Horrified debuff affects you again." : "The Horrified debuff no longer affects you.");
		ClientSettings.Save();
	}

	/// <summary>.infammo / .infiniteammo / .infam</summary>
	internal static void InfiniteAmmoCommand(CommandContext command)
	{
		InfiniteAmmo = !InfiniteAmmo;
		ClientChat.Info(InfiniteAmmo ? "Infinite ammo enabled." : "Infinite ammo disabled.");
		ClientSettings.Save();
	}

	/// <summary>.ignoreliquids / .igl</summary>
	internal static void IgnoreLiquidsCommand(CommandContext command)
	{
		IgnoreLiquids = !IgnoreLiquids;
		ClientChat.Info("Ignore Liquids " + (IgnoreLiquids ? "En" : "Dis") + "abled.");
		ClientSettings.Save();
	}

	/// <summary>.ignoredmgblocks / .igd</summary>
	internal static void IgnoreDamageBlocksCommand(CommandContext command)
	{
		IgnoreDamageBlocks = !IgnoreDamageBlocks;
		ClientChat.Info("Ignore Damage Blocks " + (IgnoreDamageBlocks ? "En" : "Dis") + "abled.");
		ClientSettings.Save();
	}

	/// <summary>.infinitemana / .infmana</summary>
	internal static void InfiniteManaCommand(CommandContext command)
	{
		InfiniteMana = !InfiniteMana;
		ClientChat.Info("Infinite Mana " + (InfiniteMana ? "En" : "Dis") + "abled");
		ClientSettings.Save();
	}

	/// <summary>.instarespawn / .instantrespawn</summary>
	internal static void InstantRespawnCommand(CommandContext command)
	{
		InstantRespawn = !InstantRespawn;
		ClientChat.Info("Instant Respawn " + (InstantRespawn ? "En" : "Dis") + "abled");
		ClientSettings.Save();
	}

	/// <summary>.extendedpickup</summary>
	internal static void ExtendedPickupCommand(CommandContext command)
	{
		ExtendedPickUpRange = !ExtendedPickUpRange;
		ClientChat.Info("Extended Pick-Up Range " + (ExtendedPickUpRange ? "En" : "Dis") + "abled");
		ClientSettings.Save();
	}

	/// <summary>.extendedplacement</summary>
	internal static void ExtendedPlacementCommand(CommandContext command)
	{
		ExtendedPlacementRange = !ExtendedPlacementRange;
		ClientChat.Info("Extended Placement Range " + (ExtendedPlacementRange ? "En" : "Dis") + "abled");
		ClientSettings.Save();
	}

	/// <summary>.infminions / .infiniteminions</summary>
	internal static void InfiniteMinionsCommand(CommandContext command)
	{
		InfiniteMinions = !InfiniteMinions;
		ClientChat.Info("Infinite Minions " + (InfiniteMinions ? "En" : "Dis") + "abled");
		ClientSettings.Save();
	}

	/// <summary>.antidebuff</summary>
	internal static void AntiDebuffCommand(CommandContext command)
	{
		AntiDebuff = !AntiDebuff;
		ClientChat.Info("Anti Debuff " + (AntiDebuff ? "En" : "Dis") + "abled");
		ClientSettings.Save();
	}

	/// <summary>.antideath</summary>
	internal static void AntiDeathCommand(CommandContext command)
	{
		AntiDeathGod = !AntiDeathGod;
		ClientChat.Info("Anti Death " + (AntiDeathGod ? "En" : "Dis") + "abled");
		ClientSettings.Save();
	}

	/// <summary>.dodge / .dodgeall</summary>
	internal static void DodgeAllCommand(CommandContext command)
	{
		DodgeEverything = !DodgeEverything;
		ClientChat.Info("Dodge All Damage " + (DodgeEverything ? "En" : "Dis") + "abled");
		ClientSettings.Save();
	}

	/// <summary>.instantregen / .instaregen</summary>
	internal static void InstantRegenCommand(CommandContext command)
	{
		InstantRegen = !InstantRegen;
		ClientChat.Info("Instant Regen " + (InstantRegen ? "En" : "Dis") + "abled");
		ClientSettings.Save();
	}

	/// <summary>.freeshops / .freeshop</summary>
	internal static void FreeShopsCommand(CommandContext command)
	{
		FreeShops = !FreeShops;
		ClientChat.Info("Free Shops " + (FreeShops ? "En" : "Dis") + "abled");
	}

	/// <summary>.journeymenu / .journey / .jm</summary>
	internal static void JourneyMenuCommand(CommandContext command)
	{
		JourneyMenu = !JourneyMenu;
		Main.LocalPlayerCreativeTracker.ItemSacrifices.MarkDirty();
		if (!JourneyMenu && Main.LocalPlayer.difficulty != 3)
		{
			Main.CreativeMenu.CloseMenu();
		}
		ClientChat.Info("Journey Menu " + (JourneyMenu ? "En" : "Dis") + "abled" + (JourneyMenu ? " (press the creative menu key, default C, with the inventory open)" : ""));
	}

	/// <summary>.god / .godmode</summary>
	internal static void GodmodeCommand(CommandContext command)
	{
		GodMode = !GodMode;
		ClientChat.Info("Godmode " + (GodMode ? "En" : "Dis") + "abled");
	}

	#endregion
}
