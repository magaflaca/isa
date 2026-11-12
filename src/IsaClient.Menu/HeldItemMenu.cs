using System;
using System.Collections.Generic;
using IsaClient.Core;
using IsaClient.Gameplay;
using IsaClient.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.Initializers;
using Terraria.UI;

namespace IsaClient.Menu;

/// <summary>Cheats → Held Item and Item Mod Presets: every stat of the item in your hand, and presets of item mods.</summary>
public static class HeldItemMenu
{
	internal static readonly List<string> Entries = new List<string>
	{
		"Item Name", "Prefix", "Damage", "Crit Chance", "Knockback", "Scale", "Mana Cost", "Use Time", "Animation Time", "Use Style",
		"Auto Reuse", "Use Turn", "Channel", "Projectile", "Use Ammo", "Shoot Speed", "Shoot Style", "Times To Shoot", "Create Tile", "Create Wall",
		"Place Style", "Heal Life", "Heal Mana", "Pickaxe Power", "Axe Power", "Hammer Power", "Melee", "Magic", "Ranged", "Summon",
		"Sentry", "No Melee", "Hits Invincible NPCs", "Save Item Mod", "Reset All"
	};

	internal static readonly List<string> PresetEntries = new List<string> { "Current Preset", "Preset Author", "Apply Preset", "Preset Tied To Character Name", "Load Item From Current Preset", "Create New Preset", "Delete Current Preset" };

	private static readonly int[] _usableAmmo = new int[17]
	{
		0, 23, 40, 71, 75, 97, 169, 283, 771, 780,
		931, 949, 1261, 1783, 1785, 1836, 3108
	};

	private static Item Held => Main.LocalPlayer.HeldItem;

	/// <summary>Editing the item on the cursor: keep the cursor copy in step.</summary>
	private static void SyncHeld()
	{
		if (Main.LocalPlayer.selectedItem == 58)
		{
			Main.mouseItem = Main.LocalPlayer.HeldItem.Clone();
		}
	}

	private static float SliderStep(float value, int decimals)
	{
		if (Main.keyState.IsKeyDown(Keys.LeftShift))
		{
			return (float)Math.Round(value, 1);
		}
		return (decimals >= 0) ? ((float)Math.Round(value, decimals)) : value;
	}

	private static UIElement HeldSlider(string cheat, Color color, int id, float max, Func<Item, float> get, Action<Item, float> set, Func<Item, string> show, int decimals = -1)
	{
		MenuSlider slider = new MenuSlider(() => cheat + ": " + show(Held), () => MathHelper.Clamp(get(Held) / max, 0f, 1f), delegate(float f)
		{
			set(Held, SliderStep(f, decimals) * max);
			SyncHeld();
		}, delegate
		{
			float f = UILinksInitializer.HandleSliderHorizontalInput(MathHelper.Clamp(get(Held) / max, 0f, 1f), 0f, 1f, 1f, 0.35f);
			set(Held, f * max);
			SyncHeld();
		}, id, color);
		slider.OnMouseOver += delegate
		{
			SoundEngine.PlaySound(12);
		};
		slider.OnUpdate += delegate(UIElement element)
		{
			MenuControls.ShowTooltip(element, cheat);
		};
		return slider;
	}

	private static UIElement HeldToggle(string cheat, Color color, Func<Item, bool> get, Action<Item, bool> set)
	{
		return MenuControls.MakeToggle(cheat, color, () => get(Held), delegate
		{
			bool value = !get(Held);
			set(Held, value);
			SyncHeld();
			SoundEngine.PlaySound(value ? 10 : 11);
		});
	}

	private static UIElement HeldInfo(string cheat, Color color, Action onLeft = null, Action onRight = null)
	{
		MenuListItem element = new MenuListItem(cheat, color);
		if (onLeft != null)
		{
			element.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			element.OnLeftClick += delegate
			{
				onLeft();
				SyncHeld();
			};
		}
		if (onRight != null)
		{
			element.OnRightClick += delegate
			{
				onRight();
				SyncHeld();
			};
		}
		element.OnUpdate += delegate(UIElement el)
		{
			MenuControls.ShowTooltip(el, cheat);
		};
		return element;
	}

	internal static UIElement Create(string cheat, Color color)
	{
		switch (cheat)
		{
		case "Item Name":
			return HeldInfo(cheat, color);
		case "Prefix":
			return HeldInfo(cheat, color, delegate
			{
				CyclePrefix(Held, back: false);
			}, delegate
			{
				CyclePrefix(Held, back: true);
			});
		case "Damage":
			return HeldSlider(cheat, color, 6950, 1000f, (Item i) => i.damage, delegate(Item i, float v)
			{
				i.damage = (int)v;
			}, (Item i) => i.damage.ToString());
		case "Crit Chance":
			return HeldSlider(cheat, color, 6951, 100f, (Item i) => i.crit, delegate(Item i, float v)
			{
				i.crit = (int)v;
			}, (Item i) => i.crit.ToString());
		case "Knockback":
			return HeldSlider(cheat, color, 6952, 200f, (Item i) => i.knockBack, delegate(Item i, float v)
			{
				i.knockBack = v;
			}, (Item i) => i.knockBack.ToString("0.00"), 3);
		case "Scale":
			return HeldSlider(cheat, color, 6953, 100f, (Item i) => i.scale, delegate(Item i, float v)
			{
				i.scale = v;
			}, (Item i) => i.scale.ToString("0.00"), 3);
		case "Mana Cost":
			return HeldSlider(cheat, color, 6954, 200f, (Item i) => i.mana, delegate(Item i, float v)
			{
				i.mana = (int)v;
			}, (Item i) => i.mana.ToString());
		case "Use Time":
			return HeldSlider(cheat, color, 6955, 100f, (Item i) => i.useTime, delegate(Item i, float v)
			{
				i.useTime = Math.Max(0, (int)v);
			}, (Item i) => i.useTime.ToString());
		case "Animation Time":
			return HeldSlider(cheat, color, 6956, 100f, (Item i) => i.useAnimation, delegate(Item i, float v)
			{
				i.useAnimation = Math.Max(1, (int)v);
			}, (Item i) => i.useAnimation.ToString());
		case "Use Style":
			return HeldInfo(cheat, color, delegate
			{
				CycleInt(ref Held.useStyle, 0, 16, back: false);
			}, delegate
			{
				CycleInt(ref Held.useStyle, 0, 16, back: true);
			});
		case "Auto Reuse":
			return HeldToggle(cheat, color, (Item i) => i.autoReuse, delegate(Item i, bool v)
			{
				i.autoReuse = v;
			});
		case "Use Turn":
			return HeldToggle(cheat, color, (Item i) => i.useTurn, delegate(Item i, bool v)
			{
				i.useTurn = v;
			});
		case "Channel":
			return HeldToggle(cheat, color, (Item i) => i.channel, delegate(Item i, bool v)
			{
				i.channel = v;
			});
		case "Projectile":
			return HeldInfo(cheat, color);
		case "Use Ammo":
			return HeldInfo(cheat, color, delegate
			{
				CycleAmmo(back: false);
			}, delegate
			{
				CycleAmmo(back: true);
			});
		case "Shoot Speed":
			return HeldSlider(cheat, color, 6957, 200f, (Item i) => i.shootSpeed, delegate(Item i, float v)
			{
				i.shootSpeed = v;
			}, (Item i) => i.shootSpeed.ToString("0.##"), 2);
		case "Shoot Style":
			return HeldInfo(cheat, color, delegate
			{
				CycleInt(ref Held.itemShootBehavior, 0, ShootBehaviors.Count - 1, back: false);
			}, delegate
			{
				CycleInt(ref Held.itemShootBehavior, 0, ShootBehaviors.Count - 1, back: true);
			});
		case "Times To Shoot":
			return HeldSlider(cheat, color, 6958, 500f, (Item i) => (int)i.timesToShoot, delegate(Item i, float v)
			{
				i.timesToShoot = (ushort)Math.Max(1, (int)v);
			}, (Item i) => i.timesToShoot.ToString(), 2);
		case "Create Tile":
			return HeldInfo(cheat, color);
		case "Create Wall":
			return HeldInfo(cheat, color);
		case "Place Style":
			return HeldInfo(cheat, color, delegate
			{
				CycleInt(ref Held.placeStyle, 0, 120, back: false);
			}, delegate
			{
				CycleInt(ref Held.placeStyle, 0, 120, back: true);
			});
		case "Heal Life":
			return HeldSlider(cheat, color, 6959, 500f, (Item i) => i.healLife, delegate(Item i, float v)
			{
				i.healLife = (int)v;
			}, (Item i) => i.healLife.ToString());
		case "Heal Mana":
			return HeldSlider(cheat, color, 6960, 400f, (Item i) => i.healMana, delegate(Item i, float v)
			{
				i.healMana = (int)v;
			}, (Item i) => i.healMana.ToString());
		case "Pickaxe Power":
			return HeldSlider(cheat, color, 6961, 500f, (Item i) => i.pick, delegate(Item i, float v)
			{
				i.pick = (int)v;
			}, (Item i) => i.pick.ToString());
		case "Axe Power":
			return HeldSlider(cheat, color, 6962, 500f, (Item i) => i.axe, delegate(Item i, float v)
			{
				i.axe = (int)v;
			}, (Item i) => i.axe.ToString());
		case "Hammer Power":
			return HeldSlider(cheat, color, 6963, 500f, (Item i) => i.hammer, delegate(Item i, float v)
			{
				i.hammer = (int)v;
			}, (Item i) => i.hammer.ToString());
		case "Melee":
			return HeldToggle(cheat, color, (Item i) => i.melee, delegate(Item i, bool v)
			{
				i.melee = v;
			});
		case "Magic":
			return HeldToggle(cheat, color, (Item i) => i.magic, delegate(Item i, bool v)
			{
				i.magic = v;
			});
		case "Ranged":
			return HeldToggle(cheat, color, (Item i) => i.ranged, delegate(Item i, bool v)
			{
				i.ranged = v;
			});
		case "Summon":
			return HeldToggle(cheat, color, (Item i) => i.summon, delegate(Item i, bool v)
			{
				i.summon = v;
			});
		case "Sentry":
			return HeldToggle(cheat, color, (Item i) => i.sentry, delegate(Item i, bool v)
			{
				i.sentry = v;
			});
		case "No Melee":
			return HeldToggle(cheat, color, (Item i) => i.noMelee, delegate(Item i, bool v)
			{
				i.noMelee = v;
			});
		case "Hits Invincible NPCs":
			return HeldToggle(cheat, color, (Item i) => i.hitsInvincibleNpc, delegate(Item i, bool v)
			{
				i.hitsInvincibleNpc = v;
			});
		case "Save Item Mod":
			return MenuControls.MakeButton(cheat, color, () => cheat + " to '" + ItemModPresets.ActivePreset.Name + "' Preset", delegate
			{
				if (Held.IsAir)
				{
					ClientChat.Error("Hold an item first.");
					return;
				}
				SoundEngine.PlaySound(11);
				ItemModPresets.ActivePreset.SaveItemMod(Held);
				ClientChat.Info("Saved " + ItemID.Search.GetName(Held.type) + ".json to the " + ItemModPresets.ActivePreset.Name + " preset.");
			});
		case "Reset All":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(SoundID.Item37);
				Held.Refresh(onlyIfVariantChanged: false);
				SyncHeld();
			});
		default:
			return new MenuToggle(() => cheat, () => false, color);
		}
	}

	internal static UIElement CreatePreset(string cheat, Color color)
	{
		switch (cheat)
		{
		case "Current Preset":
			return HeldInfo(cheat, color, delegate
			{
				CyclePreset(back: false);
			}, delegate
			{
				CyclePreset(back: true);
			});
		case "Preset Author":
			return HeldInfo(cheat, color);
		case "Apply Preset":
		{
			MenuSimpleItem element = new MenuSimpleItem(() => cheat + " (Double Click)", color, () => ItemModPresets.ActivePreset.Items.Count == 0);
			element.OnUpdate += delegate(UIElement el)
			{
				MenuControls.ShowTooltip(el, cheat);
			};
			element.OnLeftDoubleClick += delegate
			{
				if (ItemModPresets.ActivePreset.Items.Count > 0)
				{
					ItemModPresets.ActivePreset.Apply();
					SoundEngine.PlaySound(10);
					ClientChat.Info($"Successfully applied preset! ({ItemModPresets.ActivePreset.Items.Count} item mods)");
				}
			};
			return element;
		}
		case "Preset Tied To Character Name":
		{
			MenuToggle element = new MenuToggle(() => cheat, () => ItemModPresets.ActivePreset.PresetTiedToCharacterName, color, () => ItemModPresets.ActivePreset.DataLocked);
			element.OnMouseOver += delegate
			{
				if (!ItemModPresets.ActivePreset.DataLocked)
				{
					SoundEngine.PlaySound(12);
				}
			};
			element.OnUpdate += delegate(UIElement el)
			{
				MenuControls.ShowTooltip(el, cheat);
			};
			element.OnLeftClick += delegate
			{
				if (!ItemModPresets.ActivePreset.DataLocked)
				{
					ItemModPresets.ActivePreset.PresetTiedToCharacterName = !ItemModPresets.ActivePreset.PresetTiedToCharacterName;
					ItemModPresets.ActivePreset.SavePresetData();
					SoundEngine.PlaySound(ItemModPresets.ActivePreset.PresetTiedToCharacterName ? 10 : 11);
				}
			};
			return element;
		}
		case "Load Item From Current Preset":
		{
			MenuSimpleItem element = new MenuSimpleItem(() => cheat, color, () => !ItemModPresets.ActivePreset.HasItem(Held.type));
			element.OnUpdate += delegate(UIElement el)
			{
				if (ItemModPresets.ActivePreset.HasItem(Held.type))
				{
					MenuControls.ShowTooltip(el, cheat);
				}
			};
			element.OnLeftClick += delegate
			{
				if (ItemModPresets.ActivePreset.HasItem(Held.type))
				{
					ItemModPresets.ActivePreset.ApplyToItem(Held);
					SyncHeld();
					SoundEngine.PlaySound(SoundID.Item37);
					ClientChat.Info("Successfully applied item mod!");
				}
			};
			return element;
		}
		case "Create New Preset":
		{
			MenuSimpleItem element = new MenuSimpleItem(() => cheat + " (Double Click)", color, () => Main.gameMenu || ItemModPresets.PresetExists(Main.LocalPlayer.name));
			element.OnUpdate += delegate(UIElement el)
			{
				MenuControls.ShowTooltip(el, cheat);
			};
			element.OnLeftDoubleClick += delegate
			{
				if (!ItemModPresets.PresetExists(Main.LocalPlayer.name) && ItemModPresets.AddNewPreset(Main.LocalPlayer.name, out var newIndex))
				{
					SoundEngine.PlaySound(11);
					ItemModPresets.ActivePresetIndex = newIndex;
					ClientChat.Info("Successfully created preset!");
				}
			};
			return element;
		}
		case "Delete Current Preset":
		{
			MenuSimpleItem element = new MenuSimpleItem(() => (ItemModPresets.ActivePreset.DataLocked ? "Clear Default Preset" : cheat) + " (Double Click)", color);
			element.OnUpdate += delegate(UIElement el)
			{
				MenuControls.ShowTooltip(el, cheat);
			};
			element.OnLeftDoubleClick += delegate
			{
				bool locked = ItemModPresets.ActivePreset.DataLocked;
				if (ItemModPresets.DeleteCurrentPreset())
				{
					SoundEngine.PlaySound(11);
					ClientChat.Info("Successfully " + (locked ? "cleared" : "deleted") + " preset!");
				}
			};
			return element;
		}
		default:
			return new MenuToggle(() => cheat, () => false, color);
		}
	}

	private static void CycleInt(ref int value, int min, int max, bool back)
	{
		value += (back ? (-1) : 1);
		if (value > max)
		{
			value = min;
		}
		if (value < min)
		{
			value = max;
		}
		SoundEngine.PlaySound(SoundID.Item37);
	}

	private static void CycleAmmo(bool back)
	{
		Item item = Held;
		int index = Array.IndexOf(_usableAmmo, item.useAmmo);
		index = ((index < 0) ? 0 : ((index + (back ? (-1) : 1) + _usableAmmo.Length) % _usableAmmo.Length));
		item.useAmmo = _usableAmmo[index];
		SoundEngine.PlaySound(SoundID.Item37);
	}

	/// <summary>Next / previous prefix this item can roll (none in between). This resets the item's stats.</summary>
	private static void CyclePrefix(Item item, bool back)
	{
		int[] prefixes = item.GetRollablePrefixes();
		if (item.IsAir || prefixes == null || prefixes.Length == 0)
		{
			return;
		}
		int index = Array.IndexOf(prefixes, (int)item.prefix);
		int next;
		if (index < 0)
		{
			next = (back ? prefixes[prefixes.Length - 1] : prefixes[0]);
		}
		else
		{
			int to = index + (back ? (-1) : 1);
			next = ((to < 0 || to >= prefixes.Length) ? 0 : prefixes[to]);
		}
		int stack = item.stack;
		bool favorited = item.favorited;
		item.SetDefaults(item.type);
		if (next > 0)
		{
			item.Prefix(next);
		}
		item.stack = stack;
		item.favorited = favorited;
		SoundEngine.PlaySound(SoundID.Item37);
	}

	private static void CyclePreset(bool back)
	{
		if (ItemModPresets.Presets.Count <= 1)
		{
			ItemModPresets.RefreshAllPresets();
			if (ItemModPresets.Presets.Count <= 1)
			{
				return;
			}
		}
		int index = ItemModPresets.ActivePresetIndex + (back ? (-1) : 1);
		if (index >= ItemModPresets.Presets.Count)
		{
			index = 0;
		}
		if (index < 0)
		{
			index = ItemModPresets.Presets.Count - 1;
		}
		ItemModPresets.ActivePresetIndex = index;
		SoundEngine.PlaySound(SoundID.Item37);
	}

	/// <summary>Value shown next to a Held Item / preset entry (MenuListItem); null for other entries.</summary>
	public static string HeldItemValue(string name)
	{
		Item item = Main.LocalPlayer?.HeldItem;
		if (item == null)
		{
			return null;
		}
		switch (name)
		{
		case "Item Name":
			return ((item.Name != "") ? item.Name : "None") + (InfoOverlays.ShowIDs ? "" : (" (ID: " + item.type + ")"));
		case "Prefix":
		{
			string prefixName = ((item.prefix > 0 && item.prefix < Lang.prefix.Length) ? Lang.prefix[item.prefix].Value : "");
			return ((prefixName.Length > 0) ? prefixName : "None") + " (ID: " + item.prefix + ")";
		}
		case "Use Style":
			return InfoOverlays.UseStyleName(item.useStyle) + " (ID: " + item.useStyle + ")";
		case "Projectile":
		{
			string projectileName = ((item.shoot > 0 && item.shoot < ProjectileID.Count) ? Lang.GetProjectileName(item.shoot).Value : "");
			return ((projectileName != "") ? projectileName : "None") + " (ID: " + item.shoot + ")";
		}
		case "Use Ammo":
		{
			string ammoName = ((item.useAmmo > 0 && item.useAmmo < ItemID.Count) ? Lang.GetItemNameValue(item.useAmmo) : "");
			return ((ammoName != "") ? ammoName : "None") + " (ID: " + item.useAmmo + ")";
		}
		case "Shoot Style":
			return ShootBehaviors.Describe(item) + " (ID: " + item.itemShootBehavior + ")";
		case "Create Tile":
			return ((item.createTile >= 0 && item.createTile < TileID.Count) ? TileID.Search.GetName(item.createTile) : "None") + " (ID: " + item.createTile + ")";
		case "Create Wall":
			return ((item.createWall > 0 && item.createWall < WallID.Count) ? WallID.Search.GetName(item.createWall) : "None") + " (ID: " + item.createWall + ")";
		case "Place Style":
			return item.placeStyle.ToString();
		case "Current Preset":
			return ItemModPresets.ActivePresetName + " (#" + ItemModPresets.ActivePresetIndex + ")";
		case "Preset Author":
			return ItemModPresets.ActivePreset.Author;
		default:
			return null;
		}
	}

	/// <summary>Tooltips of the Held Item / preset entries; null for other entries.</summary>
	/// <summary>Hover text of a Held Item / Item Mod Presets row (null: none).</summary>
	internal static string Tooltip(string name)
	{
		switch (name)
		{
		case "Item Name":
			return "The name of your currently held item.";
		case "Prefix":
			return "The prefix of your currently held item.\nLeft click to change the prefix in order of available prefixes.\nRight click to go backwards.\nNote that changing the prefix resets all item stats.";
		case "Damage":
			return "The damage your currently held item deals.\nTo change this beyond 1000, use the .dmg <value> command.\nHold LeftShift to adjust this by 100.";
		case "Crit Chance":
			return "The chance your currently held item has to critically hit.\nHold LeftShift to change this by 10.";
		case "Knockback":
			return "The knockback your currently held item deals.\nHold LeftShift to change this by 20.";
		case "Scale":
			return "The scale of your currently held item.\nTo change this beyond 100, use the .scale <value> command.\nHold LeftShift to adjust this by 10.";
		case "Mana Cost":
			return "The mana your currently held item costs to use.\nHold LeftShift to adjust this by 20.";
		case "Use Time":
			return "The time it takes for your currently held item to complete a full use.\nIf the animation time is greater than this, it will restart the use without restarting the animation.\nHold LeftShift to adjust this by 10.";
		case "Animation Time":
			return "The duration of your currently held item's use animation.\nHold LeftShift to adjust this by 10.";
		case "Use Style":
			return "The way in which your currently held item is used.\nLeft click: next style. Right click: previous.";
		case "Auto Reuse":
			return "Specifies if your currently held item should automatically be reused once the use is finished.";
		case "Use Turn":
			return "Specifies if you can turn around while using your currently held item.";
		case "Channel":
			return "Specifies if your currently held item will use for as long as you hold the Use Item key.";
		case "Projectile":
			return "The projectile fired by your currently held item.\nCan be changed with the .proj <value> command.";
		case "Use Ammo":
			return "The ammo your currently held item uses.\nLeft click to advance to the next ammo type, or right click to go back.";
		case "Shoot Speed":
			return "The velocity at which your held item will shoot projectiles at.\nHold LeftShift to adjust this by 20.";
		case "Shoot Style":
			return "The special behavior used when shooting your currently held item: it shoots like the weapon the style comes from\n(Star Wrath, Zenith, Daedalus Stormbow...), or a random style (.rngamount sets how many).\nLeft click to advance to the next style, or right click to go back. Also .shootstyle <id>.\nThis is a custom feature.";
		case "Times To Shoot":
			return "The amount of times this item will shoot its projectile on every use.\nThis is a custom feature.";
		case "Create Tile":
			return "The tile created by your currently held item.\nCan be changed with the .crtl <value> command.";
		case "Create Wall":
			return "The wall created by your currently held item.\nCan be changed with the .crwl <value> command.";
		case "Place Style":
			return "The style in which your currently held item will place or use certain consumable items.\nLeft click to advance by 1, right click to go back by 1.\nTo increase this past 120, use the .plst <value> command.";
		case "Heal Life":
			return "The life your currently held item restores when used.\nHold LeftShift to adjust this by 50.";
		case "Heal Mana":
			return "The mana your currently held item restores when used.\nHold LeftShift to adjust this by 40.";
		case "Pickaxe Power":
			return "The pickaxe power of your currently held item.\nHold LeftShift to adjust this by 50.";
		case "Axe Power":
			return "The axe power of your currently held item.\nHold LeftShift to adjust this by 50.";
		case "Hammer Power":
			return "The hammer power of your currently held item.\nHold LeftShift to adjust this by 50.";
		case "Melee":
			return "Specifies if your currently held item is a melee weapon.\nHighest priority over the other weapon type flags.";
		case "Magic":
			return "Specifies if your currently held item is a magic weapon.\nThird highest priority over the other weapon type flags.";
		case "Ranged":
			return "Specifies if your currently held item is a ranged weapon.\nSecond highest priority over the other weapon type flags.";
		case "Summon":
			return "Specifies if your currently held item is a summon weapon.\nLowest priority over the other weapon type flags.";
		case "Sentry":
			return "Specifies if your currently held item is a sentry weapon.\nDoes not have its own damage type.";
		case "No Melee":
			return "Specifies if your currently held item does not deal melee damage when swung.";
		case "Hits Invincible NPCs":
			return "Specifies if your currently held item (and what it shoots) can damage NPCs regardless of their invincibility status.\nThis is a custom feature.";
		case "Save Item Mod":
			return "Save the current item data as an item mod in the current preset (Terraria/Item Mods).";
		case "Reset All":
			return "Reset all item stats to vanilla.";
		case "Current Preset":
			return "The currently active preset. Left click to cycle forwards, and right click to cycle back.";
		case "Preset Author":
			return "Who made the current preset.";
		case "Apply Preset":
			return "Double-click to apply this preset to every item you carry that it has an item mod for.";
		case "Preset Tied To Character Name":
			return "If the preset should automatically load for a character with its name when joining a world.";
		case "Load Item From Current Preset":
			return "Load an item mod for this item from the current preset.";
		case "Create New Preset":
			return "Double-click to make a new preset with your character name.";
		case "Delete Current Preset":
			return "Permanently deletes the current preset. Double-click to confirm.";
		default:
			return null;
		}
	}
}
