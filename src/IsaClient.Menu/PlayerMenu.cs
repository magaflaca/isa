using System;
using System.Collections.Generic;
using IsaClient.Building;
using IsaClient.Gameplay;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.UI;

namespace IsaClient.Menu;

/// <summary>Cheats → Player: player cheats and the auto clicker.</summary>
public static class PlayerMenu
{
	/// <summary>The rows of this category, in display order.</summary>
	internal static readonly List<string> Entries = new List<string>
	{
		"Godmode", "Journey Menu", "Anti Death", "Dodge All Damage", "Instant Regen", "Infinite Mana", "Anti Debuff",
		"Instant Respawn", "Extended Pick-Up Range", "Extended Placement Range", "Infinite Minions", "Infinite Ammo",
		"Infinite Breath", "No Fall Damage", "Ignore Liquids", "Ignore Damaging Blocks", "Item Drop Mode",
		"Buff Loss Mode", "Force Dash Type", "Luck Override", "Unrestrict Rod Of Discord", "Enable Lock-On",
		"Suppress Horrified Debuff", "Ignore Crowd Control", "Enable Extra Accessory Slots", "Equip Duplicate Accessories",
		"Grapple While Mounted", "Items On Transform Mounts", "Always PvP Deaths", "Free Crafting", "Craft Anywhere", "No Swap Restriction",
		"Hammer Anything", "Place Anywhere", "Always Explode on Hit", "Always Have Stardust Guardian",
		"Always Spirit Heal", "Always Spectre Wrath", "Always Nebula Boosters", "Suppress Creative Shock Debuff",
		"Ghost Mode", "Stack Buffs", "Remove Debuffs (Right Click)", "Any Item in Any Slot", "Auto Clicker",
		"Auto Clicker Mode", "Auto Clicker Button", "Auto Clicker Pauses in Inventory", "Auto Clicker Speed",
		"Auto Clicker Hold/Release", "Auto Clicker Timer", "Disable All"
	};

	internal static UIElement Create(string cheat, Color color)
	{
		switch (cheat)
		{
		case "Godmode":
		case "Journey Menu":
			return TravelMenu.Create(cheat, color);
		case "Anti Death":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.AntiDeathGod, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.AntiDeathGod);
			});
		case "Dodge All Damage":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.DodgeEverything, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.DodgeEverything);
			});
		case "Instant Regen":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.InstantRegen, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.InstantRegen);
			});
		case "Infinite Mana":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.InfiniteMana, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.InfiniteMana);
			});
		case "Anti Debuff":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.AntiDebuff, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.AntiDebuff);
			});
		case "Instant Respawn":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.InstantRespawn, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.InstantRespawn);
			});
		case "Extended Pick-Up Range":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.ExtendedPickUpRange, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.ExtendedPickUpRange);
			});
		case "Extended Placement Range":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.ExtendedPlacementRange, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.ExtendedPlacementRange);
			});
		case "Infinite Minions":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.InfiniteMinions, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.InfiniteMinions);
			});
		case "Infinite Ammo":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.InfiniteAmmo, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.InfiniteAmmo);
			});
		case "Infinite Breath":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.InfiniteBreath, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.InfiniteBreath);
			});
		case "No Fall Damage":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.NoFallDamage, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.NoFallDamage);
			});
		case "Ignore Liquids":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.IgnoreLiquids, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.IgnoreLiquids);
			});
		case "Ignore Damaging Blocks":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.IgnoreDamageBlocks, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.IgnoreDamageBlocks);
			});
		case "Unrestrict Rod Of Discord":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.UnrestrictTeleportRod, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.UnrestrictTeleportRod);
			});
		case "Enable Lock-On":
			return MenuControls.MakeToggle(cheat, color, () => LockOnHelper.ForceUsability, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref LockOnHelper.ForceUsability);
			});
		case "Suppress Horrified Debuff":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.SuppressHorrified, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.SuppressHorrified);
			});
		case "Suppress Creative Shock Debuff":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.SuppressCreativeShock, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.SuppressCreativeShock);
			});
		case "Ignore Crowd Control":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.IgnoreCC, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.IgnoreCC);
			});
		case "Enable Extra Accessory Slots":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.ExtraAccSlots, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.ExtraAccSlots);
			});
		case "Equip Duplicate Accessories":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.EquipDupeAccessory, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.EquipDupeAccessory);
			});
		case "Grapple While Mounted":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.GrappleMounted, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.GrappleMounted);
			});
		case "Items On Transform Mounts":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.ItemsOnTransformMounts, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.ItemsOnTransformMounts);
			});
		case "Always PvP Deaths":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.AlwaysPvpDeath, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.AlwaysPvpDeath);
			});
		case "Free Crafting":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.FreeCraft, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.FreeCraft);
				Recipe.UpdateRecipeList();
			});
		case "Craft Anywhere":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.CraftAnywhere, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.CraftAnywhere);
				Recipe.UpdateRecipeList();
			});
		case "No Swap Restriction":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.NoSwapRestriction, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.NoSwapRestriction);
			});
		case "Hammer Anything":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.HammerAnything, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.HammerAnything);
			});
		case "Place Anywhere":
			return MenuControls.MakeToggle(cheat, color, () => Placement.PlaceAnywhere, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Placement.PlaceAnywhere);
			});
		case "Always Explode on Hit":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.AlwaysSolarExplosion, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.AlwaysSolarExplosion);
			});
		case "Always Have Stardust Guardian":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.AlwaysStardustGuardian, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.AlwaysStardustGuardian);
			});
		case "Always Spirit Heal":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.AlwaysGhostHeal, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.AlwaysGhostHeal);
			});
		case "Always Spectre Wrath":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.AlwaysGhostHurt, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.AlwaysGhostHurt);
			});
		case "Always Nebula Boosters":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.AlwaysNebulaSet, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.AlwaysNebulaSet);
			});
		case "Item Drop Mode":
		{
			UIElement uIElement = new MenuListItem(cheat, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnCycleClicked(evt, cheat, 0, 2, ref PlayerCheats.DeathItemDropMode);
			};
			uIElement.OnRightClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnCycleClicked(evt, cheat, 0, 2, ref PlayerCheats.DeathItemDropMode, goBack: true);
			};
			return uIElement;
		}
		case "Buff Loss Mode":
		{
			UIElement uIElement = new MenuListItem(cheat, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnCycleClicked(evt, cheat, 0, 5, ref PlayerCheats.PersistBuffsMode);
			};
			uIElement.OnRightClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnCycleClicked(evt, cheat, 0, 5, ref PlayerCheats.PersistBuffsMode, goBack: true);
			};
			return uIElement;
		}
		case "Force Dash Type":
		{
			UIElement uIElement = new MenuListItem(cheat, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnCycleClicked(evt, cheat, 0, 5, ref PlayerCheats.ForceDashType);
			};
			uIElement.OnRightClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnCycleClicked(evt, cheat, 0, 5, ref PlayerCheats.ForceDashType, goBack: true);
			};
			return uIElement;
		}
		case "Luck Override":
		{
			UIElement uIElement = new MenuListItem(cheat, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnCycleClicked(evt, cheat, 0, 2, ref PlayerCheats.DreamLuckMode);
			};
			uIElement.OnRightClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnCycleClicked(evt, cheat, 0, 2, ref PlayerCheats.DreamLuckMode, goBack: true);
			};
			return uIElement;
		}
		case "Remove Debuffs (Right Click)":
			return MenuControls.MakeToggle(cheat, color, () => BuffTools.RemoveDebuffs, delegate
			{
				BuffTools.SetRemoveDebuffs(!BuffTools.RemoveDebuffs);
				SoundEngine.PlaySound(BuffTools.RemoveDebuffs ? 10 : 11);
			});
		case "Stack Buffs":
			return MenuControls.MakeToggle(cheat, color, () => BuffTools.StackBuffs, delegate
			{
				BuffTools.SetStack(!BuffTools.StackBuffs);
				SoundEngine.PlaySound(BuffTools.StackBuffs ? 10 : 11);
			});
		case "Ghost Mode":
			return MenuControls.MakeToggle(cheat, color, () => Main.LocalPlayer != null && Main.LocalPlayer.ghost, delegate
			{
				PlayerTools.ToggleGhost();
				SoundEngine.PlaySound(Main.LocalPlayer.ghost ? 10 : 11);
			});
		case "Any Item in Any Slot":
			return MenuControls.MakeToggle(cheat, color, () => PlayerTools.AnyItemAnySlot, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerTools.AnyItemAnySlot);
			});
		case "Auto Clicker":
			return MenuControls.MakeToggle(cheat, color, () => AutoClicker.Enabled, delegate
			{
				AutoClicker.SetEnabled(!AutoClicker.Enabled);
				SoundEngine.PlaySound(AutoClicker.Enabled ? 10 : 11);
			});
		case "Auto Clicker Mode":
			return MenuControls.MakeButton(cheat, color, () => "Mode: " + AutoClicker.Mode switch
			{
				AutoClicker.AutoClickMode.Hold => IsaClient.Languages.L10n.T("autoclick::Hold"),
				AutoClicker.AutoClickMode.Click => "Clicks",
				_ => "Hold & Release",
			}, delegate
			{
				SoundEngine.PlaySound(12);
				AutoClicker.Mode = (AutoClicker.AutoClickMode)((int)(AutoClicker.Mode + 1) % 3);
			}, delegate
			{
				SoundEngine.PlaySound(12);
				AutoClicker.Mode = (AutoClicker.AutoClickMode)((int)(AutoClicker.Mode + 2) % 3);
			});
		case "Auto Clicker Button":
			return MenuControls.MakeButton(cheat, color, () => "Button: " + AutoClicker.Button, delegate
			{
				SoundEngine.PlaySound(12);
				AutoClicker.Button = (AutoClicker.AutoClickButton)((int)(AutoClicker.Button + 1) % 3);
			}, delegate
			{
				SoundEngine.PlaySound(12);
				AutoClicker.Button = (AutoClicker.AutoClickButton)((int)(AutoClicker.Button + 2) % 3);
			});
		case "Auto Clicker Pauses in Inventory":
			return MenuControls.MakeToggle(cheat, color, () => AutoClicker.PauseInInventory, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AutoClicker.PauseInInventory);
			});
		case "Auto Clicker Speed":
			return MenuControls.MakeSlider(cheat, color, () => "Auto Clicker Speed: " + AutoClicker.ClicksPerSecond.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " clicks/s", () => (float)Math.Sqrt((AutoClicker.ClicksPerSecond - 0.5f) / 29.5f), delegate(float f)
			{
				AutoClicker.ClicksPerSecond = (float)Math.Round(0.5f + 29.5f * f * f, 1);
			}, 6913);
		case "Auto Clicker Hold/Release":
			return MenuControls.MakeButton(cheat, color, () => "Hold & Release: hold " + AutoClicker.HoldSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " s / release " + AutoClicker.ReleaseSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " s", delegate
			{
				SoundEngine.PlaySound(12);
				AutoClicker.HoldSeconds = MenuControls.NextStep(AutoClicker.HoldSeconds, new float[9] { 0.25f, 0.5f, 1f, 2f, 3f, 5f, 10f, 30f, 60f });
			}, delegate
			{
				SoundEngine.PlaySound(12);
				AutoClicker.ReleaseSeconds = MenuControls.NextStep(AutoClicker.ReleaseSeconds, new float[7] { 0.1f, 0.25f, 0.5f, 1f, 2f, 5f, 10f });
			});
		case "Auto Clicker Timer":
			return MenuControls.MakeSlider(cheat, color, () => "Auto Clicker Stops After: " + ((AutoClicker.TimeLimitMinutes == 0) ? "never" : (AutoClicker.TimeLimitMinutes + " min")), () => AutoClicker.TimeLimitMinutes / 240f, delegate(float f)
			{
				AutoClicker.TimeLimitMinutes = (int)Math.Round(240f * f / 5f) * 5;
			}, 6914);
		case "Disable All":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				DisableAll();
			});
		default:
			return new UIElement();
		}
	}

	/// <summary>The category's "Disable All" button.</summary>
	internal static void DisableAll()
	{
		PlayerCheats.DisablePlayerCheats();
		PlayerCheats.GodMode = false;
		PlayerCheats.JourneyMenu = false;
		PlayerTools.AnyItemAnySlot = false;
		AutoClicker.Enabled = false;
		if (Main.LocalPlayer != null && Main.LocalPlayer.ghost)
		{
			PlayerTools.ToggleGhost();
		}
	}

	/// <summary>Hover text of a row of this category (null: none).</summary>
	internal static string Tooltip(string name)
	{
		return name switch
		{
			"Anti Death" => "You cannot die, but can still receive damage.",
			"Dodge All Damage" => "You cannot be hit.",
			"Instant Regen" => "You will immediately regenerate health.",
			"Infinite Mana" => "You will never expend your mana.",
			"Anti Debuff" => "You can't be debuffed (no potion sickness either).",
			"Instant Respawn" => "Respawn instantly upon death.",
			"Extended Pick-Up Range" => "Pick up items from afar.",
			"Extended Placement Range" => "You can build and place much further away.",
			"Infinite Minions" => "You can summon as many minions as you want.",
			"Infinite Ammo" => "Never run out of ammo for your ranged weapons.",
			"Infinite Breath" => "You will never drown.",
			"No Fall Damage" => "You will never receive fall damage.",
			"Ignore Liquids" => "Pass through liquids as if they don't exist.",
			"Ignore Damaging Blocks" => "Stop various blocks such as Meteorite/Hellstone,\nSpikes, and Honey blocks from affecting you.",
			"Unrestrict Rod Of Discord" => "The Rod of Discord will have no restrictions on its use.",
			"Enable Lock-On" => "The controller Lock-On, for mouse and keyboard.\nBind a key in Settings > Controls > IsaClient: Combat > Lock On.",
			"Suppress Horrified Debuff" => "Makes the Horrified debuff present when fighting the Wall of Flesh ignore you.",
			"Suppress Creative Shock Debuff" => "Makes the Creative Shock debuff not disallow building and mining.",
			"Ignore Crowd Control" => "Allows you to move around regardless of crowd control debuffs.",
			"Enable Extra Accessory Slots" => "Enable the two extra accessory slots from Expert & Master mode.",
			"Equip Duplicate Accessories" => "Allows for equipping the same accessory multiple times.\nNot all accessory effects will stack.",
			"Grapple While Mounted" => "Allows you to use a grappling hook without being dismounted.",
			"Items On Transform Mounts" => "Use weapons and items as the Rat, the Bat or the Pixie, without being dismounted.",
			"Always PvP Deaths" => "All of your deaths will count as PvP deaths, so you respawn at full HP and with longer invulnerability time.",
			"Free Crafting" => "You can craft items freely as if you have every resource.",
			"Craft Anywhere" => "You can craft items as if you're next to every crafting station.",
			"No Swap Restriction" => "Removes restrictions on being able to swap to different items while using another item.",
			"Hammer Anything" => "Allows hammering almost any tile into a half block or slope. Most tiles will not save these states.",
			"Place Anywhere" => "Allows you to place any tile, anywhere. May not always be shown on the server-side.",
			"Always Explode on Hit" => "You will always explode when taking damage as if you are wearing Solar armor.",
			"Always Have Stardust Guardian" => "You will always have the Stardust Guardian protecting you as if you were wearing Stardust armor.",
			"Always Spirit Heal" => "You always have the set bonus of Spectre Armor with the Spectre Hood.\nDamaging a hostile NPC or Player will spawn healing orbs that fly towards the lowest-health player in range.",
			"Always Spectre Wrath" => "You always have the set bonus of Spectre Armor with the Spectre Mask.\nDamaging a hostile NPC or Player will spawn attacking orbs that target nearby hostile NPCs.",
			"Always Nebula Boosters" => "You always have the set bonus of Nebula Armor.\nDamaging a hostile NPC or Player will spawn a random Nebula Booster.",
			"Item Drop Mode" => "Change item drop behavior for Mediumcore/Hardcore.",
			"Buff Loss Mode" => "Change the behavior for buff persistence when you die.\nKeep Buffs/Debuffs (Long) will only keep buffs/debuffs with a duration greater than 1 day.",
			"Force Dash Type" => "Overrides your dash type.",
			"Luck Override" => "Overrides your luck values.\nMax Luck will set your luck values to the maximum possible in vanilla.\nDream Luck will set your luck values to absurd values beyond legitimate ones.\nNotes: Overriding your luck values will actually change them.\nDream Luck does not work in multiplayer.",
			"Remove Debuffs (Right Click)" => "Right click a debuff in your buff bar to remove it, the same way you cancel a buff\n(also works on the few buffs Terraria never lets you cancel).",
			"Stack Buffs" => "Buffs that normally replace each other stay together: every Well Fed tier at once, several flasks...\nPick buffs in My Character > Buffs (Player List > your row) or with .buff.",
			"Ghost Mode" => "Turns you into a ghost (.ghost). Turning it off respawns you if you were dead.",
			"Any Item in Any Slot" => "Lets you put any item in armor, accessory, vanity, equipment, coin and ammo slots (.anyslot).",
			"Auto Clicker" => "Uses your held item by itself (.ac). Works with the game in the background or the chat open.\nIt never clicks on menus. Bind a key in Settings > Controls > IsaClient: Combat > Auto Clicker.",
			"Auto Clicker Mode" => "Hold: keeps the button pressed.  Clicks: clicks at the chosen speed.\nHold & Release: holds for a while, releases, and repeats (charged weapons, fishing...).",
			"Auto Clicker Button" => "Left = use item, Right = use/interact, or both.",
			"Auto Clicker Pauses in Inventory" => "Pause the auto clicker while the inventory is open.",
			"Auto Clicker Speed" => "Clicks per second in Clicks mode (.ac <cps>, up to 30).",
			"Auto Clicker Hold/Release" => "Hold & Release timing. Left click: change the hold time. Right click: change the release time.\nExact values: .ac cycle hold=3 release=0.5",
			"Auto Clicker Timer" => "Stop the auto clicker automatically after this many minutes (.ac min=60).",
			"Godmode" => "You cannot die or take damage.",
			"Journey Menu" => "Enables the Journey Mode menu (duplication, powers) for any character, also on servers. Open it with the creative menu key (C) while the inventory is open.",
			_ => null,
		};
	}
}
