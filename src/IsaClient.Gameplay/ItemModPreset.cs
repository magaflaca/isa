using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria.ID;
using Terraria.IO;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Gameplay;

/// <summary>A folder of item mods (one "&lt;InternalItemName&gt;.json" per item) plus "_PresetData.json" (author, tied to character).</summary>
public class ItemModPreset
{
	public const string PresetDataFileName = "_PresetData.json";

	public const string DefaultPresetName = "Default Item Mods Preset";

	public const string DefaultAuthor = "Terrarian";

	public List<ItemModPresets.ItemModData> Items = new List<ItemModPresets.ItemModData>();

	public string Name = "Default Item Mods Preset";

	public bool DataLocked = true;

	public string Author = "Terrarian";

	public bool PresetTiedToCharacterName;

	public string FilePath => Path.Combine(ItemModPresets.PresetsPath, Name);

	public string PresetFilePath => Path.Combine(ItemModPresets.PresetsPath, Name, "_PresetData.json");

	public ItemModPreset()
	{
	}

	public ItemModPreset(string author = "Terrarian", bool isLocked = false, string name = null)
	{
		Author = author;
		DataLocked = isLocked;
		string clean = CleanupName(author);
		Name = name ?? clean;
		try
		{
			if (!Directory.Exists(FilePath))
			{
				Directory.CreateDirectory(FilePath);
			}
			else if (!DataLocked)
			{
				ClientChat.Error("A folder with the name " + Name + " already exists. Please make a preset using a different character name.");
				DataLocked = true;
				return;
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		if (!DataLocked)
		{
			SavePresetData();
		}
		LoadPreset();
	}

	public static string CleanupName(string name)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		if (name.Length > 20)
		{
			name = name.Substring(0, 20);
		}
		name = name.Replace(".", "_").Replace("*", "_");
		string text = "";
		foreach (char c in name)
		{
			text += (!invalid.Contains(c)) ? ((c != ' ') ? c : '_') : '-';
		}
		return text;
	}

	public void LoadPreset()
	{
		LoadPresetFromPath(FilePath);
	}

	public void LoadPresetFromPath(string presetPath)
	{
		if (string.IsNullOrEmpty(presetPath) || !Directory.Exists(presetPath))
		{
			return;
		}
		try
		{
			Items.Clear();
			foreach (string file in Directory.GetFiles(presetPath, "*.json"))
			{
				string internalName = Path.GetFileNameWithoutExtension(file);
				if (internalName != null && internalName != "_PresetData" && ItemID.Search.TryGetId(internalName, out int id))
				{
					Items.Add(GetSingleItemMod(file, id));
				}
			}
			if (!DataLocked)
			{
				LoadPresetData();
			}
		}
		catch (Exception ex)
		{
			ClientChat.Error($"Loading Item Mod Preset \"{Name}\" error!\n{ex}");
			ClientLog.LogException(ex);
		}
	}

	public void Delete()
	{
		Directory.Delete(FilePath, recursive: true);
	}

	public void LoadPresetData()
	{
		if (!DataLocked)
		{
			Preferences preferences = new Preferences(PresetFilePath);
			if (preferences.Load())
			{
				preferences.Get("Author", ref Author);
				preferences.Get("TiedToCharacter", ref PresetTiedToCharacterName);
			}
		}
	}

	public void SavePresetData()
	{
		if (!DataLocked)
		{
			Preferences preferences = new Preferences(PresetFilePath);
			preferences.Put("Author", Author);
			preferences.Put("TiedToCharacter", PresetTiedToCharacterName);
			preferences.Save();
		}
	}

	public static ItemModPresets.ItemModData GetSingleItemMod(string path, int id)
	{
		ItemModPresets.ItemModData result = new ItemModPresets.ItemModData((short)id);
		Preferences preferences = new Preferences(path);
		if (!preferences.Load())
		{
			return result;
		}
		preferences.Get("Damage", ref result.Damage);
		preferences.Get("CritChance", ref result.CritChance);
		preferences.Get("Knockback", ref result.Knockback);
		preferences.Get("Scale", ref result.Scale);
		preferences.Get("ManaCost", ref result.ManaCost);
		preferences.Get("UseTime", ref result.UseTime);
		preferences.Get("AnimationTime", ref result.AnimationTime);
		preferences.Get("UseStyle", ref result.UseStyle);
		preferences.Get("HoldStyle", ref result.HoldStyle);
		preferences.Get("AutoReusable", ref result.AutoReusable);
		preferences.Get("TurnWhileUsing", ref result.TurnWhileUsing);
		preferences.Get("ChannelUse", ref result.ChannelUse);
		preferences.Get("ShotProjectile", ref result.ShotProjectile);
		preferences.Get("ProjectileSpeed", ref result.ProjectileSpeed);
		preferences.Get("AmmoUsed", ref result.AmmoUsed);
		preferences.Get("TimesToShoot", ref result.TimesToShoot);
		preferences.Get("ShootStyle", ref result.ShootStyle);
		preferences.Get("ShootsEveryUse", ref result.ShootsEveryUse);
		preferences.Get("HealedLife", ref result.HealedLife);
		preferences.Get("HealedMana", ref result.HealedMana);
		preferences.Get("BuffAdded", ref result.BuffAdded);
		preferences.Get("BuffDuration", ref result.BuffDuration);
		preferences.Get("PickaxePower", ref result.PickaxePower);
		preferences.Get("AxePower", ref result.AxePower);
		preferences.Get("HammerPower", ref result.HammerPower);
		preferences.Get("FishingPower", ref result.FishingPower);
		preferences.Get("ConsumableStack", ref result.ConsumableStack);
		preferences.Get("TileRangeModifier", ref result.TileRangeModifier);
		preferences.Get("IsMelee", ref result.IsMelee);
		preferences.Get("IsRanged", ref result.IsRanged);
		preferences.Get("IsMagic", ref result.IsMagic);
		preferences.Get("IsSummoner", ref result.IsSummoner);
		preferences.Get("IsSentry", ref result.IsSentry);
		preferences.Get("IsDD2Sentry", ref result.IsDD2Sentry);
		preferences.Get("ArmorHeadSlot", ref result.ArmorHeadSlot);
		preferences.Get("ArmorBodySlot", ref result.ArmorBodySlot);
		preferences.Get("ArmorLegSlot", ref result.ArmorLegSlot);
		preferences.Get("IsAccessory", ref result.IsAccessory);
		preferences.Get("IsVanityItem", ref result.IsVanityItem);
		preferences.Get("WingType", ref result.WingType);
		preferences.Get("Defense", ref result.Defense);
		preferences.Get("LifeRegenBonus", ref result.LifeRegenBonus);
		preferences.Get("ManaBonus", ref result.ManaBonus);
		preferences.Get("DoesNoMeleeDamage", ref result.DoesNoMeleeDamage);
		preferences.Get("NoUseGraphic", ref result.NoUseGraphic);
		preferences.Get("CanHitInvincibleNPCs", ref result.CanHitInvincibleNPCs);
		Dictionary<string, byte> color = preferences.Get("Color", new Dictionary<string, byte>());
		if (color != null)
		{
			if (color.TryGetValue("R", out var value))
			{
				result.Color.R = value;
			}
			if (color.TryGetValue("G", out value))
			{
				result.Color.G = value;
			}
			if (color.TryGetValue("B", out value))
			{
				result.Color.B = value;
			}
			if (color.TryGetValue("A", out value))
			{
				result.Color.A = value;
			}
		}
		preferences.Get("CustomName", ref result.CustomName);
		return result;
	}

	public void SaveItemMod(Item item)
	{
		Directory.CreateDirectory(FilePath);
		string path = Path.Combine(FilePath, ItemID.Search.GetName(item.type)) + ".json";
		Preferences preferences = new Preferences(path);
		preferences.Put("Damage", item.damage);
		preferences.Put("CritChance", item.crit);
		preferences.Put("Knockback", item.knockBack);
		preferences.Put("Scale", item.scale);
		preferences.Put("ManaCost", item.mana);
		preferences.Put("UseTime", item.useTime);
		preferences.Put("AnimationTime", item.useAnimation);
		preferences.Put("UseStyle", item.useStyle);
		preferences.Put("HoldStyle", item.holdStyle);
		preferences.Put("AutoReusable", item.autoReuse);
		preferences.Put("TurnWhileUsing", item.useTurn);
		preferences.Put("ChannelUse", item.channel);
		preferences.Put("ShotProjectile", item.shoot);
		preferences.Put("ProjectileSpeed", item.shootSpeed);
		preferences.Put("AmmoUsed", item.useAmmo);
		preferences.Put("TimesToShoot", item.timesToShoot);
		preferences.Put("ShootStyle", item.itemShootBehavior);
		preferences.Put("ShootsEveryUse", item.shootsEveryUse);
		preferences.Put("HealedLife", item.healLife);
		preferences.Put("HealedMana", item.healMana);
		preferences.Put("BuffAdded", item.buffType);
		preferences.Put("BuffDuration", item.buffTime);
		preferences.Put("PickaxePower", item.pick);
		preferences.Put("AxePower", item.axe);
		preferences.Put("HammerPower", item.hammer);
		preferences.Put("FishingPower", item.fishingPole);
		preferences.Put("ConsumableStack", item.consumable);
		preferences.Put("TileRangeModifier", item.tileBoost);
		preferences.Put("IsMelee", item.melee);
		preferences.Put("IsRanged", item.ranged);
		preferences.Put("IsMagic", item.magic);
		preferences.Put("IsSummoner", item.summon);
		preferences.Put("IsSentry", item.sentry);
		preferences.Put("IsDD2Sentry", item.DD2Summon);
		preferences.Put("ArmorHeadSlot", item.headSlot);
		preferences.Put("ArmorBodySlot", item.bodySlot);
		preferences.Put("ArmorLegSlot", item.legSlot);
		preferences.Put("IsAccessory", item.accessory);
		preferences.Put("IsVanityItem", item.vanity);
		preferences.Put("WingType", item.wingSlot);
		preferences.Put("Defense", item.defense);
		preferences.Put("LifeRegenBonus", item.lifeRegen);
		preferences.Put("ManaBonus", item.manaIncrease);
		preferences.Put("DoesNoMeleeDamage", item.noMelee);
		preferences.Put("NoUseGraphic", item.noUseGraphic);
		preferences.Put("CanHitInvincibleNPCs", item.hitsInvincibleNpc);
		preferences.Put("Color", new Dictionary<string, byte>
		{
			{ "R", item.color.R },
			{ "G", item.color.G },
			{ "B", item.color.B },
			{ "A", item.color.A }
		});
		preferences.Put("CustomName", string.IsNullOrEmpty(item.NameOverride) ? "" : item.NameOverride);
		preferences.Save();
		ItemModPresets.ItemModData data = GetSingleItemMod(path, item.type);
		if (!HasItem(item.type))
		{
			Items.Add(data);
		}
		else
		{
			UpdateItemInlist(data);
		}
	}

	public bool HasItem(int netId)
	{
		if (netId <= 0 || netId >= ItemID.Count)
		{
			return false;
		}
		for (int i = 0; i < Items.Count; i++)
		{
			if (Items[i].NetID == netId)
			{
				return true;
			}
		}
		return false;
	}

	public void UpdateItemInlist(ItemModPresets.ItemModData data)
	{
		for (int i = 0; i < Items.Count; i++)
		{
			if (Items[i].NetID == data.NetID)
			{
				Items[i] = data;
				break;
			}
		}
	}

	public void Apply()
	{
		Player player = Main.LocalPlayer;
		foreach (Item item in player.inventory)
		{
			RefreshAndApply(item);
		}
		foreach (Item item2 in player.armor)
		{
			RefreshAndApply(item2);
		}
		foreach (Item item3 in player.dye)
		{
			RefreshAndApply(item3);
		}
	}

	public void RefreshAndApply(Item item)
	{
		if (item != null && HasItem(item.type))
		{
			item.Refresh(onlyIfVariantChanged: false);
			ApplyToItem(item);
		}
	}

	public void ApplyToItem(Item item)
	{
		for (int i = 0; i < Items.Count; i++)
		{
			if (Items[i].NetID == item.type)
			{
				Items[i].ApplyToItem(item, Name);
				break;
			}
		}
	}
}
