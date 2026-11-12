using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using IsaClient.Core;

namespace IsaClient.Rendering;

/// <summary>Extra information: IDs, item/NPC/tile data and the owner of dropped items.</summary>
public static class InfoOverlays
{
	[Setting("ShowDataIds", Remembered = true)]
	public static bool ShowIDs;

	[Setting("SeeTileData", Remembered = true)]
	public static bool SeeTileData;

	public static string TileDataString(Tile tile)
	{
		List<string> list = new List<string>();
		if (tile.HasTile)
		{
			list.Add($"Tile Name: {TileID.Search.GetName((int)tile.TileType)} (ID: {tile.TileType})");
		}
		if (tile.WallType > 0)
		{
			list.Add($"Wall Name: {WallID.Search.GetName((int)tile.WallType)} (ID: {tile.WallType})");
		}
		if (tile.HasTile)
		{
			list.Add($"Tile FrameX: {tile.TileFrameX}, Tile FrameY: {tile.TileFrameY}");
			list.Add($"Tile Paint: {PaintID.Search.GetName((int)tile.TileColor)} (ID: {tile.TileColor})");
		}
		if (tile.WallType > 0)
		{
			list.Add($"Wall Paint: {PaintID.Search.GetName((int)tile.WallColor)} (ID: {tile.WallColor})");
		}
		if (tile.HasTile)
		{
			list.Add($"Invisble Tile: {tile.IsTileInvisible}");
		}
		if (tile.WallType > 0)
		{
			list.Add($"Invisible Wall: {tile.IsWallInvisible}");
		}
		if (tile.HasTile)
		{
			list.Add($"Fullbright Tile: {tile.IsTileFullbright}");
		}
		if (tile.WallType > 0)
		{
			list.Add($"Fullbright Wall: {tile.IsWallFullbright}");
		}
		if (tile.LiquidAmount > 0)
		{
			list.Add($"Liquid: {LiquidID.Search.GetName(tile.LiquidType)}, Amount: {tile.LiquidAmount}/255");
		}
		List<string> list2 = new List<string>();
		string text = "";
		if (tile.RedWire)
		{
			list2.Add("Red");
		}
		if (tile.BlueWire)
		{
			list2.Add("Blue");
		}
		if (tile.GreenWire)
		{
			list2.Add("Green");
		}
		if (tile.YellowWire)
		{
			list2.Add("Yellow");
		}
		if (list2.Count > 0)
		{
			text = string.Join(", ", list2);
		}
		if (text != "")
		{
			list.Add("Wires: " + text);
		}
		if (tile.HasActuator)
		{
			list.Add($"Has Actuator: {tile.HasActuator}");
		}
		if (tile.HasTile)
		{
			list.Add($"Is Tile Actuated: {!tile.HasUnactuatedTile}");
		}
		return string.Join("\n", list);
	}

	[Setting("ShowDroppedItemOwner", Remembered = true)]
	public static bool ShowItemOwner;

	[Setting("ShowExtraItemData", Remembered = true)]
	public static bool ExtraItemInfo;

	[Setting("ShowExtraNpcInfo", Remembered = true)]
	public static bool SeeNpcData;

	private static readonly IdNameSearch _useStyleSearch = IdNameSearch.Create(typeof(ItemUseStyleID), typeof(int));

	public static string UseStyleName(int useStyle)
	{
		return (useStyle > 0 && _useStyleSearch.ContainsId(useStyle)) ? _useStyleSearch.GetName(useStyle) : "None";
	}

	public static string NpcDataString(NPC npc)
	{
		List<string> list = new List<string>
		{
			"Internal Name: " + NPCID.Search.GetName(npc.netID),
			"Index: " + npc.whoAmI,
			"Defense: " + npc.defense,
			"AI Style: " + npc.aiStyle
		};
		if (npc.value > 0f)
		{
			list.Add("Value (Money): " + npc.value);
		}
		if ((float)npc.extraValue > 0f)
		{
			list.Add("Extra Value (Money): " + npc.extraValue);
		}
		list.Add("AI[0]: " + npc.ai[0]);
		list.Add("AI[1]: " + npc.ai[1]);
		list.Add("AI[2]: " + npc.ai[2]);
		list.Add("Friendly: " + npc.friendly);
		if (npc.HasPlayerTarget && npc.target != 255)
		{
			list.Add("Target Player: " + Main.player[npc.target].name + " (" + npc.target + ")");
		}
		if (npc.HasNPCTarget && npc.target != 255)
		{
			list.Add("Target NPC: " + Main.npc[npc.TranslatedTargetIndex].FullName + " (" + npc.TranslatedTargetIndex + ")");
		}
		if (npc.dontTakeDamage)
		{
			list.Add("Doesn't Take Damage: " + npc.dontTakeDamage);
		}
		return "\n" + string.Join("\n", list);
	}

	/// <summary>Extra tooltip lines for "Extra Item Information" (Main.MouseText_DrawItemTooltip_GetLinesInfo).</summary>
	public static void AddExtraItemInfoLines(Item item, string[] toolTipLine, ref int numLines)
	{
		if (!ExtraItemInfo || Main.gameMenu || toolTipLine == null)
		{
			return;
		}
		List<string> lines = new List<string> { "Internal Name: " + ItemID.Search.GetName(item.type) };
		if (item.useStyle > 0 && _useStyleSearch.ContainsId(item.useStyle))
		{
			lines.Add("Use Time: " + item.useTime);
			lines.Add("Animation Time: " + item.useAnimation);
			lines.Add("Use Style: " + _useStyleSearch.GetName(item.useStyle) + (ShowIDs ? (" (ID: " + item.useStyle + ")") : ""));
		}
		if (item.createTile > -1 && item.createTile < TileID.Count)
		{
			lines.Add("Creates Tile: " + TileID.Search.GetName(item.createTile) + (ShowIDs ? (" (ID: " + item.createTile + ")") : ""));
		}
		if (item.createWall > -1 && item.createWall < WallID.Count)
		{
			lines.Add("Creates Wall: " + WallID.Search.GetName(item.createWall) + (ShowIDs ? (" (ID: " + item.createWall + ")") : ""));
		}
		if (item.shoot > 1 && item.shoot < ProjectileID.Count)
		{
			lines.Add("Shoots Projectile: " + Lang.GetProjectileName(item.shoot).Value + " (" + ProjectileID.Search.GetName(item.shoot) + ")" + (ShowIDs ? (" (ID: " + item.shoot + ")") : ""));
		}
		if (item.buffType > 0 && item.buffType < BuffID.Count)
		{
			lines.Add("Adds Buff: " + Lang.GetBuffName(item.buffType) + " (" + BuffID.Search.GetName(item.buffType) + ")" + (ShowIDs ? (" (ID: " + item.buffType + ")") : ""));
		}
		foreach (string line in lines)
		{
			if (numLines >= toolTipLine.Length)
			{
				break;
			}
			toolTipLine[numLines] = line;
			numLines++;
		}
	}

	public static string IdSuffix(int id)
	{
		if (!ShowIDs)
		{
			return "";
		}
		return " (ID: " + id + ")";
	}

	#region Chat commands

	/// <summary>.shio / .showitemowner</summary>
	internal static void ShowItemOwnerCommand(CommandContext command)
	{
		ShowItemOwner = !ShowItemOwner;
		ClientChat.Info(ShowItemOwner ? "Items now show who they are reserved for when you point at them." : "Items no longer show who they are reserved for.");
	}

	/// <summary>.ids / .showids</summary>
	internal static void ShowIdsCommand(CommandContext command)
	{
		ShowIDs = !ShowIDs;
		ClientChat.Info("Show IDs " + (ShowIDs ? "en" : "dis") + "abled.");
	}

	/// <summary>.iteminfo / .extraiteminfo</summary>
	internal static void ExtraItemInfoCommand(CommandContext command)
	{
		ExtraItemInfo = !ExtraItemInfo;
		ClientChat.Info("Extra item information " + (ExtraItemInfo ? "en" : "dis") + "abled.");
	}

	/// <summary>.seenpcdata / .nd</summary>
	internal static void SeeNpcDataCommand(CommandContext command)
	{
		SeeNpcData = !SeeNpcData;
		ClientChat.Info("Extra NPC information on the moused over NPC " + (SeeNpcData ? "en" : "dis") + "abled.");
	}

	/// <summary>.seetiledata / .td</summary>
	internal static void SeeTileDataCommand(CommandContext command)
	{
		SeeTileData = !SeeTileData;
		ClientChat.Info("Tile information on the mouse cursor " + (SeeTileData ? "en" : "dis") + "abled.");
	}

	#endregion
}
