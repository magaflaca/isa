using System;
using System.Collections.Generic;
using IsaClient.Community;
using IsaClient.Core;
using IsaClient.Music;
using IsaClient.Networking;
using IsaClient.Rendering;
using IsaClient.WorldTools;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.Initializers;
using Terraria.UI;

namespace IsaClient.Menu;

/// <summary>Cheats → Render: hitboxes, forced sights, shaders, zoom, brightness, chat and SSC options.</summary>
public static class RenderMenu
{
	/// <summary>The rows of this category, in display order.</summary>
	internal static readonly List<string> Entries = new List<string>
	{
		"Player Hitboxes", "NPC Hitboxes", "Projectile Hitboxes", "Item Hitboxes", "Hitbox Style", "Force Spelunker",
		"Force Hunter", "Force Dangersense", "Force Biome Sight", "Always Render Invisible Tiles/Walls",
		"Render Other Player's Wire Kites", "Disable Invisibility on Players", "Use CRT Shader", "Use GameBoy Shader",
		"Disable Blood Moon Shader", "Disable Moon Lord Shader", "Use Windows Cursor", "Enhanced Chat Box",
		"Keep Chat Between Worlds", "Keep SSC Character (One-Way)", "Two-Way SSC Sync", "Mute Game Sound",
		"View Protected Chests", "Extended Zoom Out", "Fullbright", "Minimum Zoom", "Global Brightness", "Dust Level",
		"Disable All"
	};

	internal static UIElement Create(string cheat, Color color)
	{
		switch (cheat)
		{
		case "Player Hitboxes":
			return MenuControls.MakeToggle(cheat, color, () => Hitboxes.PlayerHitboxes, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Hitboxes.PlayerHitboxes);
			});
		case "NPC Hitboxes":
			return MenuControls.MakeToggle(cheat, color, () => Hitboxes.NpcHitboxes, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Hitboxes.NpcHitboxes);
			});
		case "Projectile Hitboxes":
			return MenuControls.MakeToggle(cheat, color, () => Hitboxes.ProjectileHitboxes, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Hitboxes.ProjectileHitboxes);
			});
		case "Item Hitboxes":
			return MenuControls.MakeToggle(cheat, color, () => Hitboxes.ItemHitboxes, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Hitboxes.ItemHitboxes);
			});
		case "Hitbox Style":
			return MenuControls.MakeCycle(cheat, color, delegate(bool back)
			{
				Hitboxes.HitBoxesTexture = MenuControls.Cycle(Hitboxes.HitBoxesTexture, 0, 2, back);
			});
		case "Force Spelunker":
			return MenuControls.MakeToggle(cheat, color, () => WorldView.ForceSpelunker, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldView.ForceSpelunker);
			});
		case "Force Hunter":
			return MenuControls.MakeToggle(cheat, color, () => WorldView.ForceHunter, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldView.ForceHunter);
			});
		case "Force Dangersense":
			return MenuControls.MakeToggle(cheat, color, () => WorldView.ForceDangersense, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldView.ForceDangersense);
			});
		case "Force Biome Sight":
			return MenuControls.MakeToggle(cheat, color, () => WorldView.ForceBiomeSight, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldView.ForceBiomeSight);
			});
		case "Always Render Invisible Tiles/Walls":
			return MenuControls.MakeToggle(cheat, color, () => WorldView.RenderInvisibleTiles, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldView.RenderInvisibleTiles);
			});
		case "Render Other Player's Wire Kites":
			return MenuControls.MakeToggle(cheat, color, () => WorldView.RenderOtherWireKites, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldView.RenderOtherWireKites);
			});
		case "Disable Invisibility on Players":
			return MenuControls.MakeToggle(cheat, color, () => WorldView.SeeInvisPlayers, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldView.SeeInvisPlayers);
			});
		case "Use CRT Shader":
			return MenuControls.MakeToggle(cheat, color, () => WorldView.UseCrt, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldView.UseCrt);
			});
		case "Use GameBoy Shader":
			return MenuControls.MakeToggle(cheat, color, () => WorldView.UseGameBoy, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldView.UseGameBoy);
			});
		case "Disable Blood Moon Shader":
			return MenuControls.MakeToggle(cheat, color, () => WorldView.NoBloodMoonShader, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldView.NoBloodMoonShader);
			});
		case "Disable Moon Lord Shader":
			return MenuControls.MakeToggle(cheat, color, () => WorldView.NoMoonLordShader, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldView.NoMoonLordShader);
			});
		case "Dust Level":
			return MenuControls.MakeSlider(cheat, color, () => "Dust Level: " + AntiLag.DustLevel + "%", () => (float)AntiLag.DustLevel / 100f, delegate(float f)
			{
				float step = Main.keyState.IsKeyDown(Keys.LeftShift) ? 10f : 1f;
				AntiLag.DustLevel = Utils.Clamp((int)(Math.Round(f * 100f / step) * step), 0, 100);
			}, 6940);
		case "Use Windows Cursor":
			return MenuControls.MakeToggle(cheat, color, () => InputState.NativeCursor, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref InputState.NativeCursor);
			});
		case "Enhanced Chat Box":
			return MenuControls.MakeToggle(cheat, color, () => ChatTools.EnhancedChat, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref ChatTools.EnhancedChat);
			});
		case "Keep SSC Character (One-Way)":
			return MenuControls.MakeToggle(cheat, color, () => SscSync.Enabled, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SscSync.Enabled);
				ClientSettings.Save();
			});
		case "Two-Way SSC Sync":
			return MenuControls.MakeToggle(cheat, color, () => SscSync.TwoWay, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SscSync.TwoWay);
				ClientSettings.Save();
			});
		case "View Protected Chests":
			return MenuControls.MakeToggle(cheat, color, () => ProtectedChests.Enabled, delegate
			{
				ProtectedChests.Toggle();
			});
		case "Extended Zoom Out":
			return MenuControls.MakeToggle(cheat, color, () => Zoom.Enabled, delegate
			{
				Zoom.SetEnabled(!Zoom.Enabled);
				SoundEngine.PlaySound(Zoom.Enabled ? 10 : 11);
			});
		case "Fullbright":
			return MenuControls.MakeToggle(cheat, color, () => FullbrightLighting.Percent > 0f, delegate
			{
				FullbrightLighting.HandleCommand("fb", new string[1] { "fb" });
				SoundEngine.PlaySound((FullbrightLighting.Percent > 0f) ? 10 : 11);
			});
		case "Global Brightness":
		{
			UIElement uIElement = new MenuSlider(() => "Global Brightness: " + FullbrightLighting.Label, () => FullbrightLighting.Percent, delegate(float f)
			{
				FullbrightLighting.Set(Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift) ? ((float)Math.Round(f, 1)) : ((float)Math.Round(f, 2)), save: false);
			}, delegate
			{
				FullbrightLighting.Set(UILinksInitializer.HandleSliderHorizontalInput(FullbrightLighting.Percent, 0f, 1f, 5f, 1f), save: false);
			}, 6904, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			return uIElement;
		}
		case "Minimum Zoom":
		{
			float zoomSlider = (Zoom.MinZoom - Zoom.LowestZoom) / (1f - Zoom.LowestZoom);
			UIElement uIElement = new MenuSlider(() => cheat + ": " + Math.Round(Zoom.MinZoom * 100f) + "%", () => zoomSlider, delegate(float f)
			{
				zoomSlider = MathHelper.Clamp((float)Math.Round(f, 2), 0f, 1f);
				Zoom.MinZoom = Zoom.LowestZoom + zoomSlider * (1f - Zoom.LowestZoom);
				if (Zoom.Enabled)
				{
					Main.GameZoomTarget = Zoom.MinZoom;
				}
			}, delegate
			{
				zoomSlider = UILinksInitializer.HandleSliderHorizontalInput(zoomSlider, 0f, 1f, 1f, 0.35f);
			}, 6903, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			return uIElement;
		}
		case "Mute Game Sound":
			return MenuControls.MakeToggle(cheat, color, () => MuteAll.Muted, delegate
			{
				MuteAll.Toggle();
			});
		case "Keep Chat Between Worlds":
			return MenuControls.MakeToggle(cheat, color, () => ChatTools.KeepChat, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref ChatTools.KeepChat);
			});
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
		FullbrightLighting.Set(0f);
		InputState.NativeCursor = false;
		ChatTools.KeepChat = false;
		WorldView.DisableAll();
	}

	/// <summary>Hover text of a row of this category (null: none).</summary>
	internal static string Tooltip(string name)
	{
		return name switch
		{
			"Use Windows Cursor" => "Uses the Windows mouse cursor instead of the Terraria cursor.",
			"Enhanced Chat Box" => "Multi-line chat: Shift+Enter for a new line, the box grows up to 3 lines and then scrolls.\nClick to move the cursor, drag or Shift+arrows to select, Ctrl+A/C/X/V/Z, Ctrl+arrows jump words.",
			"Two-Way SSC Sync" => "Like Keep SSC Character, in both directions: whichever side changed last wins.\nIf your local .plr changed since it last followed this server (single player, another server), it is uploaded\nto the server after you log in (the server's character is saved to Players/SSC Backups first).\nOtherwise your local .plr follows the server's character. Set it before joining. (.ssc twoway, .ssc push)",
			"Keep SSC Character (One-Way)" => "On servers with server-side characters (TShock SSC), your local .plr follows the server's character:\nit is written whenever the game saves (autosave, leaving, disconnect), once the server confirms your login.\nThe first write of each visit keeps a copy of your old file in Players/SSC Backups. (.ssc, .ssc save, .ssc status)",
			"View Protected Chests" => "Opening a chest shows this client's copy of it right away, read-only (.rochest).\nIf the server lets you open it, it becomes a normal chest; a protected one stays read-only.\nShows what the server already sent: it may be empty or out of date.",
			"Mute Game Sound" => "Turns music, sound and ambient volume off, and back to the volumes you had (.mute).",
			"Extended Zoom Out" => "Lets the game zoom go below 100% to see much more of the world (zoom out keys, or .zoom 25).\nBelow 100% the world is drawn directly to the screen: very low zooms cost FPS,\nand areas the server has not sent you yet stay dark.",
			"Fullbright" => "The world will be brighter, allowing for better vision (.fb).\nTurns Global Brightness on/off; .fb full = no lighting at all.",
			"Global Brightness" => "Minimum brightness of every tile. 100% skips lighting entirely (everything fully lit).\nHold Shift for 10% steps. Commands: .fb, .fb full, .fb off, .fb 75",
			"Minimum Zoom" => "How far Extended Zoom Out can go (" + Math.Round(Zoom.LowestZoom * 100f) + "% to 100%).",
			"Keep Chat Between Worlds" => "Keeps the chat history when you leave a world or server (.keepchat).",
			"Player Hitboxes" => "Draws the hitbox of each player. Color coded as follows:\nRGB: Your Player\nTurquoise: Other Players\nDark Gray: Dead Player",
			"NPC Hitboxes" => "Draws the hitbox of each NPC. Color coded as follows:\nRed: Hostile NPC\nDark Red: Hostile NPC, deals damage, does not recieve damage\nOrange: Hostile NPC, deals no damage\nLime Green: Friendly NPC",
			"Projectile Hitboxes" => "Draws the hitbox of each projectile. Color coded as follows:\nGold: Projectile owned by someone else\nHot Pink: Hostile Projectile\nGreen: Projectile owned by you",
			"Item Hitboxes" => "Draws the hitbox of each dropped item. Always purple.",
			"Hitbox Style" => "The style to use for hitboxes.",
			"Force Spelunker" => "Renders tiles as if you have a Spelunker potion active.",
			"Force Hunter" => "Renders NPCs as if you have a Hunter potion active.",
			"Force Dangersense" => "Renders tiles as if you have a Dangersense potion active.",
			"Force Biome Sight" => "Renders tiles as if you have a Biome Sight potion active.",
			"Always Render Invisible Tiles/Walls" => "Renders Echo Blocks/Walls or Echo coated Blocks/Walls as if you're able to see them.",
			"Render Other Player's Wire Kites" => "Forces other player's wire kites (Grand Design) to render.",
			"Disable Invisibility on Players" => "Other players can never appear invisible unless dead.",
			"Use CRT Shader" => "Use the unused CRT Shader.",
			"Use GameBoy Shader" => "Use the unused GameBoy Shader.\nMay not be visually pleasing - please disable if your eyes hurt.",
			"Disable Blood Moon Shader" => "Disables the shader during a Blood Moon.",
			"Disable Moon Lord Shader" => "Disables the annoying shader when Moon Lord is alive.",
			"Dust Level" => "How much dust should be created overall.\n100% is normal, 0% disables dust.\nHold LeftShift to adjust this by 10%.",
			_ => null,
		};
	}
}
