using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria.ID;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Gameplay;

/// <summary>
/// Item mods: saved stats of an item (one JSON per item inside a preset folder in "Terraria/Item Mods"),
/// applied to every copy of that item you carry. The folder layout and JSON keys match the older presets format, so those presets load as-is.
/// </summary>
public class ItemModPresets
{
	public struct ItemModData
	{
		public short NetID;

		public int Damage;

		public int CritChance;

		public float Knockback;

		public float Scale;

		public int ManaCost;

		public int UseTime;

		public int AnimationTime;

		public int UseStyle;

		public int HoldStyle;

		public bool AutoReusable;

		public bool TurnWhileUsing;

		public bool ChannelUse;

		public int ShotProjectile;

		public float ProjectileSpeed;

		public int AmmoUsed;

		public ushort TimesToShoot;

		public int ShootStyle;

		public bool ShootsEveryUse;

		public int HealedLife;

		public int HealedMana;

		public int BuffAdded;

		public int BuffDuration;

		public int PickaxePower;

		public int AxePower;

		public int HammerPower;

		public int FishingPower;

		public bool ConsumableStack;

		public int TileRangeModifier;

		public bool IsMelee;

		public bool IsRanged;

		public bool IsMagic;

		public bool IsSummoner;

		public bool IsSentry;

		public bool IsDD2Sentry;

		public int ArmorHeadSlot;

		public int ArmorBodySlot;

		public int ArmorLegSlot;

		public bool IsAccessory;

		public bool IsVanityItem;

		public sbyte WingType;

		public int Defense;

		public int LifeRegenBonus;

		public int ManaBonus;

		public bool DoesNoMeleeDamage;

		public bool NoUseGraphic;

		public bool CanHitInvincibleNPCs;

		public string CustomName;

		public Color Color;

		public ItemModData(short netId)
		{
			NetID = netId;
			Damage = -1;
			CritChance = 0;
			Knockback = 0f;
			Scale = 1f;
			ManaCost = 0;
			UseTime = 20;
			AnimationTime = 20;
			UseStyle = 0;
			HoldStyle = 0;
			AutoReusable = false;
			TurnWhileUsing = false;
			ChannelUse = false;
			ShotProjectile = 0;
			ProjectileSpeed = 0f;
			AmmoUsed = AmmoID.None;
			TimesToShoot = 1;
			ShootStyle = 0;
			ShootsEveryUse = false;
			HealedLife = 0;
			HealedMana = 0;
			BuffAdded = 0;
			BuffDuration = 0;
			PickaxePower = 0;
			AxePower = 0;
			HammerPower = 0;
			FishingPower = 0;
			ConsumableStack = false;
			TileRangeModifier = 0;
			IsMelee = false;
			IsRanged = false;
			IsMagic = false;
			IsSummoner = false;
			IsSentry = false;
			IsDD2Sentry = false;
			ArmorHeadSlot = -1;
			ArmorBodySlot = -1;
			ArmorLegSlot = -1;
			IsAccessory = false;
			IsVanityItem = false;
			WingType = -1;
			Defense = 0;
			LifeRegenBonus = 0;
			ManaBonus = 0;
			DoesNoMeleeDamage = false;
			NoUseGraphic = false;
			CanHitInvincibleNPCs = false;
			CustomName = null;
			Color = Color.White;
		}

		public void ApplyToItem(Item item, string presetName = null)
		{
			if (item.modifiedByServer)
			{
				ClientChat.Notice("Warning: " + item.Name + " was modified by the server. Applying item mods to it will override those modifications. If you're sure about this, apply the item mod again.\nThe \"Modified By Server\" flag has been removed.");
				item.modifiedByServer = false;
			}
			item.damage = Damage;
			item.crit = CritChance;
			item.knockBack = Knockback;
			item.scale = Scale;
			item.mana = ManaCost;
			item.useTime = UseTime;
			item.useAnimation = AnimationTime;
			item.useStyle = UseStyle;
			item.holdStyle = HoldStyle;
			item.autoReuse = AutoReusable;
			item.useTurn = TurnWhileUsing;
			item.channel = ChannelUse;
			item.shoot = ShotProjectile;
			item.shootSpeed = ProjectileSpeed;
			item.useAmmo = AmmoUsed;
			item.timesToShoot = Math.Max((ushort)1, TimesToShoot);
			item.itemShootBehavior = ShootStyle;
			item.shootsEveryUse = ShootsEveryUse;
			item.healLife = HealedLife;
			item.healMana = HealedMana;
			item.buffType = BuffAdded;
			item.buffTime = BuffDuration;
			item.pick = PickaxePower;
			item.axe = AxePower;
			item.hammer = HammerPower;
			item.fishingPole = FishingPower;
			item.consumable = ConsumableStack;
			item.tileBoost = TileRangeModifier;
			item.melee = IsMelee;
			item.ranged = IsRanged;
			item.magic = IsMagic;
			item.summon = IsSummoner;
			item.sentry = IsSentry;
			item.DD2Summon = IsDD2Sentry;
			item.headSlot = ArmorHeadSlot;
			item.bodySlot = ArmorBodySlot;
			item.legSlot = ArmorLegSlot;
			item.accessory = IsAccessory;
			item.vanity = IsVanityItem;
			item.wingSlot = WingType;
			item.defense = Defense;
			item.lifeRegen = LifeRegenBonus;
			item.manaIncrease = ManaBonus;
			item.noMelee = DoesNoMeleeDamage;
			item.noUseGraphic = NoUseGraphic;
			item.hitsInvincibleNpc = CanHitInvincibleNPCs;
			if (!string.IsNullOrWhiteSpace(CustomName))
			{
				item.SetNameOverride(CustomName);
			}
			item.color = Color;
			item.modifiedByItemMods = true;
			item.itemModPresetName = presetName;
		}
	}

	public static string PresetsPath => Path.Combine(Main.SavePath, "Item Mods");

	public static int ActivePresetIndex = 0;

	private static List<ItemModPreset> _presets;

	public static List<ItemModPreset> Presets
	{
		get
		{
			if (_presets == null)
			{
				_presets = new List<ItemModPreset>
				{
					new ItemModPreset("None", isLocked: true, "Default Item Mods Preset")
				};
			}
			return _presets;
		}
	}

	public static ItemModPreset ActivePreset
	{
		get
		{
			if (ActivePresetIndex < 0 || ActivePresetIndex >= Presets.Count)
			{
				return Presets[0];
			}
			return Presets[ActivePresetIndex];
		}
	}

	public static string ActivePresetName => ActivePreset.Name;

	public static void RefreshAllPresets()
	{
		try
		{
			Directory.CreateDirectory(PresetsPath);
			Presets.RemoveRange(1, Presets.Count - 1);
			Presets[0].LoadPreset();
			foreach (string directory in Directory.GetDirectories(PresetsPath))
			{
				string name = Path.GetFileName(directory);
				if (name == Presets[0].Name || !File.Exists(Path.Combine(directory, ItemModPreset.PresetDataFileName)))
				{
					continue;
				}
				ItemModPreset preset = new ItemModPreset();
				preset.Name = name;
				preset.DataLocked = false;
				preset.LoadPresetFromPath(directory);
				Presets.Add(preset);
			}
			if (ActivePresetIndex >= Presets.Count)
			{
				ActivePresetIndex = 0;
			}
		}
		catch (Exception ex)
		{
			ClientChat.Error($"Refresh all presets error!\n{ex}");
			ClientLog.LogException(ex);
		}
	}

	public static bool AddNewPreset(string name, out int newIndex)
	{
		newIndex = 0;
		if (PresetExists(name))
		{
			return false;
		}
		try
		{
			ItemModPreset preset = new ItemModPreset(name);
			if (preset.DataLocked)
			{
				return false;
			}
			Presets.Add(preset);
			newIndex = Presets.IndexOf(preset);
		}
		catch
		{
			return false;
		}
		return true;
	}

	public static bool PresetExists(string name)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return true;
			}
			string clean = ItemModPreset.CleanupName(name);
			foreach (ItemModPreset preset in Presets)
			{
				if (preset.Name == name || preset.Name == clean)
				{
					return true;
				}
			}
		}
		catch
		{
			return false;
		}
		return false;
	}

	public static bool DeleteCurrentPreset()
	{
		string name = ActivePreset.Name;
		try
		{
			if (ActivePreset.DataLocked)
			{
				if (Directory.Exists(ActivePreset.FilePath))
				{
					ActivePreset.Delete();
				}
				ActivePreset.Items.Clear();
				Directory.CreateDirectory(ActivePreset.FilePath);
				return true;
			}
			if (Directory.Exists(ActivePreset.FilePath))
			{
				ActivePreset.Delete();
			}
			Presets.Remove(ActivePreset);
			if (ActivePresetIndex >= Presets.Count)
			{
				ActivePresetIndex = 0;
			}
			return true;
		}
		catch (Exception ex)
		{
			ClientChat.Error($"Deleting Item Mod Preset \"{name}\" error!\n{ex}");
			return false;
		}
	}

	/// <summary>On entering a world: refresh the presets and apply the one tied to this character's name.</summary>
	public static void OnEnterWorld()
	{
		RefreshAllPresets();
		try
		{
			string clean = ItemModPreset.CleanupName(Main.LocalPlayer.name);
			for (int i = 0; i < Presets.Count; i++)
			{
				ItemModPreset preset = Presets[i];
				if ((Main.LocalPlayer.name == preset.Name || clean == preset.Name) && preset.PresetTiedToCharacterName && !preset.DataLocked)
				{
					preset.Apply();
					ActivePresetIndex = i;
					ClientChat.Info($"Auto-applied preset \"{preset.Name}\" ({preset.Items.Count} item mods).");
					break;
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}
}
