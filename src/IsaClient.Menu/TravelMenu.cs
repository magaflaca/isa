using System.Collections.Generic;
using IsaClient.Gameplay;
using IsaClient.Movement;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.UI;

namespace IsaClient.Menu;

/// <summary>Cheats → Travel: noclip, teleports, surfaces, flight and position updates.</summary>
public static class TravelMenu
{
	internal static UIElement Create(string cheat, Color color)
	{
		bool fakeBoolean = false;
		switch (cheat)
		{
		case "Asphalt Surfaces":
			return MenuControls.MakeToggle(cheat, color, () => Travel.AsphaltSurfaces, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Travel.AsphaltSurfaces);
			});
		case "Bouncy Surfaces":
			return MenuControls.MakeToggle(cheat, color, () => Travel.BouncySurfaces, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Travel.BouncySurfaces);
			});
		case "Surface Friction":
			return MenuControls.MakeCycle(cheat, color, delegate(bool back)
			{
				Travel.SurfaceFrictionMode = MenuControls.Cycle(Travel.SurfaceFrictionMode, 0, 3, back);
			});
		case "Conveyer Belt Handling":
			return MenuControls.MakeCycle(cheat, color, delegate(bool back)
			{
				Travel.ConveyerBeltMode = MenuControls.Cycle(Travel.ConveyerBeltMode, 0, 3, back);
			});
		case "Infinite Flight":
			return MenuControls.MakeToggle(cheat, color, () => Travel.InfiniteFlight, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Travel.InfiniteFlight);
			});
		case "Infinite Carpet Flight":
			return MenuControls.MakeToggle(cheat, color, () => Travel.InfiniteCarpet, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Travel.InfiniteCarpet);
			});
		case "Can Always Hover & Boost Upwards":
			return MenuControls.MakeToggle(cheat, color, () => Travel.AlwaysHoverable, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Travel.AlwaysHoverable);
			});
		case "Max Wing Stats":
			return MenuControls.MakeToggle(cheat, color, () => Travel.MaxWings, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Travel.MaxWings);
			});
		case "Always Portal Physics":
			return MenuControls.MakeToggle(cheat, color, () => Travel.PortalPhysics, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Travel.PortalPhysics);
			});
		case "Bypass Teleport Requests":
			return MenuControls.MakeToggle(cheat, color, () => TeleportTools.BypassTeleportRequests, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref TeleportTools.BypassTeleportRequests);
			});
		case "Rapid Position Updates":
			return MenuControls.MakeToggle(cheat, color, () => Noclip.RapidPosUpdate, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Noclip.RapidPosUpdate);
			});
		case "Noclip":
		{
			MenuToggle modsNewUIToggleListItem11 = new MenuToggle(() => cheat, () => Noclip.Enabled, color);
			modsNewUIToggleListItem11.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			modsNewUIToggleListItem11.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			modsNewUIToggleListItem11.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Noclip.Enabled);
			};
			return modsNewUIToggleListItem11;
		}
		case "Mouse Teleportation":
		{
			MenuToggle modsNewUIToggleListItem8 = new MenuToggle(() => cheat, () => TeleportTools.GotoMouse, color);
			modsNewUIToggleListItem8.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			modsNewUIToggleListItem8.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			modsNewUIToggleListItem8.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref TeleportTools.GotoMouse);
			};
			return modsNewUIToggleListItem8;
		}
		case "Map Teleportation":
		{
			MenuToggle modsNewUIToggleListItem = new MenuToggle(() => cheat, () => TeleportTools.MapTeleportation, color);
			modsNewUIToggleListItem.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			modsNewUIToggleListItem.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			modsNewUIToggleListItem.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref TeleportTools.MapTeleportation);
			};
			return modsNewUIToggleListItem;
		}
		case "Godmode":
		{
			UIElement uIElement = new MenuToggle(() => cheat, () => PlayerCheats.GodMode, color);
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
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.GodMode);
			};
			return uIElement;
		}
		case "Journey Menu":
		{
			UIElement uIElement = new MenuToggle(() => cheat, () => PlayerCheats.JourneyMenu, color);
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
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerCheats.JourneyMenu);
				Main.LocalPlayerCreativeTracker.ItemSacrifices.MarkDirty();
				if (!PlayerCheats.JourneyMenu && Main.LocalPlayer.difficulty != 3)
				{
					Main.CreativeMenu.CloseMenu();
				}
			};
			return uIElement;
		}
		case "Noclip Speed":
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
				if (evt.Target is MenuListItem)
				{
					Noclip.CycleSpeed();
					SoundEngine.PlaySound(11);
				}
			};
			return uIElement;
		}
		case "Disable All":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				SoundEngine.PlaySound(11);
				DisableAll();
			};
			return uIElement;
		}
		default:
			return new MenuToggle(() => cheat, () => fakeBoolean, color);
		}
	}

	/// <summary>The category's "Disable All" button.</summary>
	internal static void DisableAll()
	{
		Travel.DisableAll();
		Noclip.Enabled = false;
		TeleportTools.GotoMouse = false;
		TeleportTools.MapTeleportation = false;
		if (Main.netMode != 0)
		{
			NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
		}
	}

	internal static readonly List<string> Entries = new List<string>
	{
		"Asphalt Surfaces", "Bouncy Surfaces", "Surface Friction", "Conveyer Belt Handling", "Infinite Flight", "Infinite Carpet Flight", "Can Always Hover & Boost Upwards", "Max Wing Stats", "Always Portal Physics", "Noclip", "Noclip Speed",
		"Mouse Teleportation", "Map Teleportation", "Bypass Teleport Requests", "Rapid Position Updates", "Disable All"
	};

	/// <summary>Hover text of a row of this category (null: none).</summary>
	internal static string Tooltip(string name)
	{
		return name switch
		{
			"Noclip" => "Disables all collision and gives you free movement.",
			"Mouse Teleportation" => "Enables teleporting to the mouse position in-world with a key (Settings > Controls > IsaClient: Movement > Mouse TP).",
			"Map Teleportation" => "Enables teleporting to the mouse position on the map with a key (Settings > Controls > IsaClient: Movement > Map TP).",
			"Noclip Speed" => "How fast noclip moves you (slow, default or fast).\nCan also be cycled with the Noclip Speed Modifier key while holding Hold for Activator.",
			"Asphalt Surfaces" => "Every surface allows you to run immensely fast.",
			"Bouncy Surfaces" => "Every surface will bounce you upwards when you land on it.",
			"Surface Friction" => "Modify how much friction all surfaces have.\nNormal - Unchanged.\nNone - Disables icy surfaces.\nIcy - All surfaces act like ice.\nNo Friction - All surfaces act like Frozen Slime Blocks.",
			"Conveyer Belt Handling" => "Allows you to ignore or force conveyer belt behavior.\nNormal - Unchanged.\nIgnored - You will ignore conveyer belts.\nForce Clockwise/Counter-Clockwise - All tiles act like the specified conveyer belt.",
			"Infinite Flight" => "You can fly forever.",
			"Infinite Carpet Flight" => "The Flying Carpet never runs out while you hold jump.",
			"Can Always Hover & Boost Upwards" => "All wings can use hover & upwards boost.",
			"Max Wing Stats" => "Your wings will have max stats.",
			"Always Portal Physics" => "You will always have the physics from holding a portal gun or recently exiting a portal.",
			"Bypass Teleport Requests" => "Don't request the server for teleports.\nThis allows for usage of telportation items even when they're disallowed.\nMost effective when the map is revealed.",
			"Rapid Position Updates" => "Updates your position rapidly. This is not vanilla behavior, so it can be seen as suspicious on servers.\nDoes nothing in single player.",
			_ => null,
		};
	}
}
