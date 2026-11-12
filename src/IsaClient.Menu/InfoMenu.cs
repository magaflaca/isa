using System.Collections.Generic;
using IsaClient.Building;
using IsaClient.Community;
using IsaClient.Networking;
using IsaClient.Rendering;
using Microsoft.Xna.Framework;
using Terraria.Audio;
using Terraria.UI;

namespace IsaClient.Menu;

/// <summary>Cheats → Informational: overlays and notifications.</summary>
public static class InfoMenu
{
	/// <summary>The rows of this category, in display order.</summary>
	internal static readonly List<string> Entries = new List<string>
	{
		"Show Netplay Text", "Player Vitals", "Show Owner of Dropped Items", "Notify When Server Modifies You",
		"Notify When Players Connect", "Show IDs", "Extra Item Information", "Extra NPC Information",
		"Paste/Selection Mode Information", "See Tile Data", "Shift+Click Wiki Lookup", "Player List",
		"Inform Other Clients", "Disable All"
	};

	internal static UIElement Create(string cheat, Color color)
	{
		bool fakeBoolean = false;
		switch (cheat)
		{
		case "Show Netplay Text":
			return MenuControls.MakeToggle(cheat, color, () => NetDebug.ShowNetplayText, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref NetDebug.ShowNetplayText);
			});
		case "Player Vitals":
			return MenuControls.MakeToggle(cheat, color, () => PlayerListPanel.PlayerVitalStatsUI, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerListPanel.PlayerVitalStatsUI);
			});
		case "Show Owner of Dropped Items":
			return MenuControls.MakeToggle(cheat, color, () => InfoOverlays.ShowItemOwner, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref InfoOverlays.ShowItemOwner);
			});
		case "Notify When Server Modifies You":
			return MenuControls.MakeToggle(cheat, color, () => ServerDetection.NotifyServerModifiedPlayer, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref ServerDetection.NotifyServerModifiedPlayer);
			});
		case "Notify When Players Connect":
			return MenuControls.MakeToggle(cheat, color, () => ServerDetection.NotifyPacket4, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref ServerDetection.NotifyPacket4);
			});
		case "Show IDs":
			return MenuControls.MakeToggle(cheat, color, () => InfoOverlays.ShowIDs, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref InfoOverlays.ShowIDs);
			});
		case "Extra Item Information":
			return MenuControls.MakeToggle(cheat, color, () => InfoOverlays.ExtraItemInfo, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref InfoOverlays.ExtraItemInfo);
			});
		case "Shift+Click Wiki Lookup":
			return MenuControls.MakeToggle(cheat, color, () => Wiki.ShiftWikiLookup, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Wiki.ShiftWikiLookup);
			});
		case "Extra NPC Information":
			return MenuControls.MakeToggle(cheat, color, () => InfoOverlays.SeeNpcData, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref InfoOverlays.SeeNpcData);
			});
		case "Paste/Selection Mode Information":
		{
			MenuToggle modsNewUIToggleListItem10 = new MenuToggle(() => cheat, () => Selection.SelectionModeInfo, color);
			modsNewUIToggleListItem10.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			modsNewUIToggleListItem10.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			modsNewUIToggleListItem10.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Selection.SelectionModeInfo);
			};
			return modsNewUIToggleListItem10;
		}
		case "Player List":
			return MenuControls.MakeToggle(cheat, color, () => PlayerListPanel.ShowPlayerList, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerListPanel.ShowPlayerList);
			});
		case "Inform Other Clients":
			return MenuControls.MakeToggle(cheat, color, () => PlayerListPanel.ShareMyInfo, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PlayerListPanel.ShareMyInfo);
			});
		case "See Tile Data":
		{
			MenuToggle modsNewUIToggleListItem5 = new MenuToggle(() => cheat, () => InfoOverlays.SeeTileData, color);
			modsNewUIToggleListItem5.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			modsNewUIToggleListItem5.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			modsNewUIToggleListItem5.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref InfoOverlays.SeeTileData);
			};
			return modsNewUIToggleListItem5;
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
		NetDebug.ShowNetplayText = false;
		PlayerListPanel.PlayerVitalStatsUI = false;
		InfoOverlays.ShowItemOwner = false;
		ServerDetection.NotifyServerModifiedPlayer = false;
		ServerDetection.NotifyPacket4 = false;
		InfoOverlays.ShowIDs = false;
		InfoOverlays.ExtraItemInfo = false;
		InfoOverlays.SeeNpcData = false;
		Selection.SelectionModeInfo = false;
		InfoOverlays.SeeTileData = false;
		PlayerListPanel.ShowPlayerList = false;
	}

	/// <summary>Hover text of a row of this category (null: none).</summary>
	internal static string Tooltip(string name)
	{
		return name switch
		{
			"Shift+Click Wiki Lookup" => "With the inventory open, hold Shift over an item: the cursor turns into a W.\nClick to open that item on the Terraria wiki.",
			"Show Netplay Text" => "Display informational text at the top of the screen in an online game.",
			"Player Vitals" => "Display the HP, Mana, and Defense of a player below them.",
			"Show Owner of Dropped Items" => "Display the owner of a dropped item when it is moused over.",
			"Notify When Server Modifies You" => "Prints a chat message when the server modifies your player.",
			"Notify When Players Connect" => "Prints a chat message when a player starts connecting to the server.\nAlso reveals players that are hidden (vanished) but still sending packets.",
			"Show IDs" => "Adds the ID of Items, Item Prefixes, NPCs, and Buffs to their names.",
			"Extra Item Information" => "Adds extra item information to the item tooltip.",
			"Extra NPC Information" => "Adds extra NPC data when mousing over an NPC.",
			"Paste/Selection Mode Information" => "Adds extra information when in Paste/Selection Mode.",
			"Player List" => "Show a list of connected players with an I badge for IsaClient users and Z for zMod (.playerlist).",
			"Inform Other Clients" => "Let other IsaClient users see you in their Player List (.shareinfo). Off = you stay hidden.\nzMod users show up with a Z badge (.clients lists who uses what).",
			"See Tile Data" => "Displays tile data at your cursor.",
			_ => null,
		};
	}
}
