using System;
using System.Collections.Generic;
using IsaClient.Community;
using IsaClient.Gameplay;
using IsaClient.Movement;
using IsaClient.Rendering;
using IsaClient.WorldTools;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.Initializers;
using Terraria.UI;

namespace IsaClient.Menu;

/// <summary>Cheats → Miscellaneous: map, ground items, aura and butcher, free camera, chests, anti-lag.</summary>
public static class MiscMenu
{
	/// <summary>The rows of this category, in display order.</summary>
	internal static readonly List<string> Entries = new List<string>
	{
		"Reveal Map", "Wipe Ground Items", "Auto-Wipe Ground Items", "Clean Gores", "Track Players",
		"Kill All Owned Projectiles", "Free Shops", "Kill Aura", "Butcher Hostile NPCs", "Auto-Butcher Hostile NPCs",
		"Butcher Friendly NPCs", "Lock-On Can Target Hostile Players", "Smart Aim", "Auto Aim", "Shimmer Town NPCs", "Shimmer Hostile NPCs",
		"Shimmer Critters", "Free Camera", "Free Camera Speed", "Open Locked Chests", "Fix Fake_NewChest",
		"Anything Fits on Plates/Weapon Racks", "Super Actuation Rod", "Anti-Lag - Projectile Spam Blocking",
		"Anti-Lag - Sound Spam Blocking", "Anti-Lag - Particle Spam Blocking", "Anti-Lag - Low FPS Measures",
		"Invisible Enemies", "Anti-Chat Spam", "Particle Limiter", "Client Menu Button", "My Character Button", "New Interface", "Disable & Reset All"
	};

	private static MenuSlider _killAuraSize;

	internal static UIElement Create(string cheat, Color color)
	{
		bool fakeBoolean = false;
		float fakeFloat = 0f;
		switch (cheat)
		{
		case "Free Camera Speed":
		{
			float camFloat = FreeCamera.FreeCamSpeed / 200f;
			UIElement camSlider = new MenuSlider(() => "Free Camera Speed: (" + (int)FreeCamera.FreeCamSpeed + ")", () => FreeCamera.FreeCamSpeed / 200f, delegate(float f)
			{
				camFloat = f;
				FreeCamera.FreeCamSpeed = Math.Max(1f, (float)Math.Round(200f * f));
			}, delegate
			{
				camFloat = UILinksInitializer.HandleSliderHorizontalInput(camFloat, 0f, 1f, 1f, 0.35f);
				FreeCamera.FreeCamSpeed = Math.Max(1f, (float)Math.Round(200f * camFloat));
			}, 6911, color);
			camSlider.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			camSlider.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			return camSlider;
		}
		case "Reveal Map":
			return MenuControls.MakeButton(cheat, color, delegate
			{
				if (Main.netMode == 0)
				{
					return cheat;
				}
				if (MapReveal.IsRevealing)
				{
					return "Revealing Map (Chunks Left: " + MapReveal.CountUnloadedSections() + ")";
				}
				return (MapReveal.CountUnloadedSections() != 0) ? cheat : "Refresh Map";
			}, delegate
			{
				bool revealing = MapReveal.IsRevealing;
				MapTools.RevealTheMap();
				SoundEngine.PlaySound((!revealing && Main.netMode != 0) ? 10 : 11);
			});
		case "Wipe Ground Items":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				WorldCommands.DeleteGroundItemsCall(0);
			});
		case "Auto-Wipe Ground Items":
			return MenuControls.MakeToggle(cheat, color, () => WorldCommands.AutoDeleteItems, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldCommands.AutoDeleteItems);
			});
		case "Clean Gores":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				AntiLag.CleanupGores();
			});
		case "Track Players":
			return MenuControls.MakeToggle(cheat, color, () => PlayerListPanel.PlayerTracker, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerListPanel.PlayerTracker);
			});
		case "Kill All Owned Projectiles":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				Combat.KillAllProjectiles(selfOnly: true);
			});
		case "Free Shops":
			return MenuControls.MakeToggle(cheat, color, () => PlayerCheats.FreeShops, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.FreeShops);
			});
		case "Kill Aura":
		{
			fakeFloat = Math.Min((float)Combat.KillAuraSize / 100f, 1f);
			UIElement uIElement = new MenuSlider(() => "Kill Aura: (" + ((Combat.KillAuraSize > 0) ? (Combat.KillAuraSize + " Blocks)") : "Disabled)"), () => fakeFloat, delegate(float f)
			{
				fakeFloat = (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift) ? ((float)Math.Round(f, 1)) : f);
				Combat.KillAuraSize = (int)(100f * fakeFloat);
				Combat.KillAura = Combat.KillAuraSize > 0;
			}, delegate
			{
				fakeFloat = UILinksInitializer.HandleSliderHorizontalInput(fakeFloat, 0f, 1f, 1f, 0.35f);
			}, 6902, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			_killAuraSize = (MenuSlider)uIElement;
			return uIElement;
		}
		case "Butcher Hostile NPCs":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				Combat.ButcherMobsAndNpcs(0, 1);
			});
		case "Butcher Friendly NPCs":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				Combat.ButcherMobsAndNpcs(0, 2);
			});
		case "Auto-Butcher Hostile NPCs":
			return MenuControls.MakeToggle(cheat, color, () => Combat.AutoButcher, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Combat.AutoButcher);
			});
		case "Lock-On Can Target Hostile Players":
			return MenuControls.MakeToggle(cheat, color, () => LockOnHelper.targetPlayers, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref LockOnHelper.targetPlayers);
			});
		case "Smart Aim":
			return MenuControls.MakeToggle(cheat, color, () => IsaClient.Gameplay.SmartAim.Enabled, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref IsaClient.Gameplay.SmartAim.Enabled);
			});
		case "Auto Aim":
			return MenuControls.MakeToggle(cheat, color, () => IsaClient.Gameplay.SmartAim.AutoAim, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref IsaClient.Gameplay.SmartAim.AutoAim);
			});
		case "Shimmer Town NPCs":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				Combat.ShimmerNPCs(0);
			}, delegate
			{
				SoundEngine.PlaySound(11);
				Combat.ShimmerNPCs(0, toNormal: true);
			});
		case "Shimmer Hostile NPCs":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				Combat.ShimmerNPCs(1);
			});
		case "Shimmer Critters":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				Combat.ShimmerNPCs(2);
			});
		case "Free Camera":
			return MenuControls.MakeToggle(cheat, color, () => FreeCamera.FreeCam, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref FreeCamera.FreeCam);
			});
		case "Open Locked Chests":
			return MenuControls.MakeToggle(cheat, color, () => Chests.OpenLockedChests, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Chests.OpenLockedChests);
			});
		case "Fix Fake_NewChest":
			return MenuControls.MakeToggle(cheat, color, () => Chests.BugFixFakeNewChest, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Chests.BugFixFakeNewChest);
			});
		case "Anything Fits on Plates/Weapon Racks":
			return MenuControls.MakeToggle(cheat, color, () => WorldCommands.AnythingOnPlates, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldCommands.AnythingOnPlates);
			});
		case "Super Actuation Rod":
			return MenuControls.MakeToggle(cheat, color, () => WorldCommands.ActuatorRodPlus, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldCommands.ActuatorRodPlus);
			});
		case "Anti-Lag - Projectile Spam Blocking":
			return MenuControls.MakeToggle(cheat, color, () => AntiLag.AntiLagProjectiles, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AntiLag.AntiLagProjectiles);
			});
		case "Anti-Lag - Sound Spam Blocking":
			return MenuControls.MakeToggle(cheat, color, () => AntiLag.AntiLagSounds, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AntiLag.AntiLagSounds);
			});
		case "Anti-Lag - Particle Spam Blocking":
			return MenuControls.MakeToggle(cheat, color, () => AntiLag.AntiLagParticles, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AntiLag.AntiLagParticles);
			});
		case "Anti-Lag - Low FPS Measures":
			return MenuControls.MakeToggle(cheat, color, () => AntiLag.AntiLagFps, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AntiLag.AntiLagFps);
			});
		case "Invisible Enemies":
			return MenuControls.MakeToggle(cheat, color, () => WorldView.InvisibleEnemies, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldView.InvisibleEnemies);
			});
		case "Client Menu Button":
			return MenuControls.MakeToggle(cheat, color, () => HudButtons.ClientMenu, delegate
			{
				HudButtons.SetEnabled("client", !HudButtons.ClientMenu);
				SoundEngine.PlaySound(HudButtons.ClientMenu ? 10 : 11);
			});
		case "New Interface":
			return MenuControls.MakeToggle(cheat, color, () => false, delegate
			{
				SoundEngine.PlaySound(10);
				IsaClient.Hub.ClientHub.SetClassic(false);
			});
		case "My Character Button":
			return MenuControls.MakeToggle(cheat, color, () => HudButtons.MyCharacter, delegate
			{
				HudButtons.SetEnabled("character", !HudButtons.MyCharacter);
				SoundEngine.PlaySound(HudButtons.MyCharacter ? 10 : 11);
			});
		case "Anti-Chat Spam":
			return MenuControls.MakeToggle(cheat, color, () => ChatTools.AntiChatSpam, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref ChatTools.AntiChatSpam);
			});
		case "Particle Limiter":
			return MenuControls.MakeToggle(cheat, color, () => AntiLag.ParticleLimiter, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AntiLag.ParticleLimiter);
			});
		case "Disable & Reset All":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				DisableAll();
			});
		default:
			return new MenuToggle(() => cheat, () => fakeBoolean, color);
		}
	}

	/// <summary>The category's "Disable All" button.</summary>
	internal static void DisableAll()
	{
		WorldCommands.AutoDeleteItems = false;
		PlayerListPanel.PlayerTracker = false;
		PlayerCheats.FreeShops = false;
		Combat.KillAuraSize = 0;
		Combat.KillAura = false;
		Combat.AutoButcher = false;
		FreeCamera.FreeCam = false;
		FreeCamera.FreeCam2 = false;
		LockOnHelper.targetPlayers = false;
		IsaClient.Gameplay.SmartAim.Enabled = false;
		IsaClient.Gameplay.SmartAim.AutoAim = false;
		Chests.OpenLockedChests = false;
		Chests.BugFixFakeNewChest = false;
		WorldCommands.AnythingOnPlates = false;
		WorldCommands.ActuatorRodPlus = false;
		AntiLag.AntiLagProjectiles = false;
		AntiLag.AntiLagSounds = false;
		AntiLag.AntiLagParticles = false;
		AntiLag.AntiLagFps = false;
		ChatTools.AntiChatSpam = false;
		WorldView.InvisibleEnemies = false;
		AntiLag.ParticleLimiter = false;
		if (MapReveal.IsRevealing)
		{
			MapTools.RevealTheMap();
		}
	}

	/// <summary>Hover text of a row of this category (null: none).</summary>
	internal static string Tooltip(string name)
	{
		return name switch
		{
			"Free Camera Speed" => "Free camera movement speed (.freecam <speed>).\nHold LeftShift to go faster and LeftControl to go slower.",
			"Reveal Map" => (Main.netMode == 0) ? "Reveal the entire world map." : ((MapReveal.CountUnloadedSections() > 0) ? "Retrieve all tiles in the world and fully reveal the map.\nTries section requests first and falls back to the legacy method." : "Refresh the currently loaded world."),
			"Wipe Ground Items" => "Delete all items on the ground.",
			"Auto-Wipe Ground Items" => "Automatically delete all items on the ground.",
			"Clean Gores" => "Removes all gore on the client-side.",
			"Track Players" => "See everyone's location regardless of PvP or Teams.",
			"Kill All Owned Projectiles" => "Destroys all projectiles owned by you.",
			"Free Shops" => "Everything in shops is free.",
			"Kill Aura" => "Kill all hostile NPCs within a radius around you.\nHold LeftShift to adjust this by 10. The slider goes up to 100 blocks; .killaura <size> sets any size.",
			"Butcher Hostile NPCs" => "Kill all hostile NPCs.",
			"Butcher Friendly NPCs" => "Kill all of your friends.",
			"Auto-Butcher Hostile NPCs" => "Kill all hostile NPCs automatically.",
			"Lock-On Can Target Hostile Players" => "Allows Lock-On to target hostile players in PvP.",
			"Smart Aim" => "Lock-On leads moving targets: it follows how they really move and aims where your shot meets them.\nIt learns how fast and how much each weapon's shots fall from the ones you fire.",
			"Auto Aim" => "No key needed: while you use a weapon, it locks on the target nearest the cursor.\nWorks with Smart Aim.",
			"Shimmer Town NPCs" => "Shimmers every town NPC.\nLeft Click to shimmer into shimmered variant.\nRight click to shimmer into normal variant.",
			"Shimmer Hostile NPCs" => "Transform every possible hostile NPC.",
			"Shimmer Critters" => "Shimmer all critters, typically into a Faeling.",
			"Free Camera" => "Detach the camera from your player and control it freely with WASD.\nHold LeftShift to move faster and LeftControl to move slower.",
			"Open Locked Chests" => "Allows you to open a chest even if it is locked.",
			"Fix Fake_NewChest" => "Prevents you from freezing in place when mousing over Fake_NewChest1 or 2.\nThis applies to all invalid chests.",
			"Anything Fits on Plates/Weapon Racks" => "Removes restriction on what can be placed on Plates and Weapon Racks.",
			"Super Actuation Rod" => "Makes Actuation Rod able to actuate tiles that don't have an actuator on them.\nIn online games, it will automatically place actuators on tiles you actuate.",
			"Anti-Lag - Projectile Spam Blocking" => "Blocks the projectiles of players that spam them for a few seconds.",
			"Anti-Lag - Sound Spam Blocking" => "Blocks the instrument sounds of players that spam them for a few seconds.",
			"Anti-Lag - Particle Spam Blocking" => "Blocks the network particles of players that spam them for a few seconds.",
			"Anti-Lag - Low FPS Measures" => "When the frame rate drops, removes projectiles, dust and gores and pauses network projectiles.",
			"Invisible Enemies" => "Hostile NPCs are not drawn (bosses and town NPCs are still drawn).",
			"Anti-Chat Spam" => "Ignore annoying spam in the chat.",
			"New Interface" => "Switch to the new IsaClient panel: sections with icons, search, favorites, your own buttons and setups (.menu new).",
			"Client Menu Button" => "The IsaClient tree next to your life hearts: click it to open or close this panel.\nHold it and drag to put it anywhere (.hud menu, .hud reset).",
			"My Character Button" => "Your head next to your life hearts: click it to open My Character.\nHold it and drag to put it anywhere (.hud character, .hud reset).",
			"Particle Limiter" => "Limits how many dust particles can exist at once (.particlelimit <n>, default 1500).",
			_ => null,
		};
	}

	/// <summary>Points the Kill Aura slider at the current value.</summary>
	internal static void SyncSliders()
	{
		if (_killAuraSize != null)
		{
			_killAuraSize.GetStatusFunction = () => Math.Min((float)Combat.KillAuraSize / 100f, 1f);
		}
	}
}
