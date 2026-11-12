using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ObjectData;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Building;

/// <summary>
/// Works out how to place a pasted tile the way a player would: which item creates it and, for multi-tile objects
/// (doors, banners, furniture...), the placement style, alternate, random variant, direction and origin that rebuild
/// exactly the frames stored in the schematic. The frames are matched against what TileObject.Place would write for
/// every style an item can place, so no per-tile tables are needed.
/// </summary>
public static class ObjectPaste
{
	public struct Plan
	{
		public int Type;

		public int Style;

		public int Alternate;

		public int Random;

		public int Direction;

		public int Item;

		/// <summary>Offset from the object's top-left tile to the tile the placement packet targets.</summary>
		public Point Origin;
	}

	private static Dictionary<int, List<KeyValuePair<int, int>>> _stylesByTile;

	private static readonly Dictionary<long, Plan?> _planCache = new Dictionary<long, Plan?>();

	private static readonly object _sync = new object();

	/// <summary>(style, item type) pairs of every item that places this tile, lowest style first.</summary>
	private static List<KeyValuePair<int, int>> StylesOf(int type)
	{
		lock (_sync)
		{
			if (_stylesByTile == null)
			{
				Dictionary<int, List<KeyValuePair<int, int>>> map = new Dictionary<int, List<KeyValuePair<int, int>>>();
				for (int i = 1; i < ItemID.Count; i++)
				{
					if (!ContentSamples.ItemsByType.TryGetValue(i, out var item) || item == null || item.createTile < 0)
					{
						continue;
					}
					if (!map.TryGetValue(item.createTile, out var list))
					{
						list = (map[item.createTile] = new List<KeyValuePair<int, int>>());
					}
					int index = list.FindIndex((KeyValuePair<int, int> p) => p.Key == item.placeStyle);
					if (index < 0)
					{
						list.Add(new KeyValuePair<int, int>(item.placeStyle, i));
					}
					else if (ContentSamples.ItemsByType[list[index].Value].tileWand > 0 && item.tileWand <= 0)
					{
						// Prefer a plain placeable item over a tile wand (wands need ammo).
						list[index] = new KeyValuePair<int, int>(item.placeStyle, i);
					}
				}
				foreach (List<KeyValuePair<int, int>> list2 in map.Values)
				{
					list2.Sort((KeyValuePair<int, int> a, KeyValuePair<int, int> b) => a.Key.CompareTo(b.Key));
				}
				_stylesByTile = map;
			}
			List<KeyValuePair<int, int>> value;
			return _stylesByTile.TryGetValue(type, out value) ? value : null;
		}
	}

	/// <summary>An item whose createTile is this tile (with this style when one exists), or 0.</summary>
	public static int FindPlaceItem(int type, int style = 0)
	{
		List<KeyValuePair<int, int>> styles = StylesOf(type);
		if (styles == null || styles.Count == 0)
		{
			return 0;
		}
		foreach (KeyValuePair<int, int> pair in styles)
		{
			if (pair.Key == style)
			{
				return pair.Value;
			}
		}
		return styles[0].Value;
	}

	/// <summary>
	/// The placement that recreates the object whose top-left tile is <paramref name="tile" />. False when the tile is
	/// not the top-left tile of an object, or when no item can place it.
	/// </summary>
	public static bool TryPlan(TileCopy tile, out Plan plan)
	{
		plan = default(Plan);
		if (tile == null || !tile.active() || tile.type >= TileID.Count || !Main.tileFrameImportant[tile.type])
		{
			return false;
		}
		long key = ((long)tile.type << 32) | ((long)(ushort)tile.frameX << 16) | (ushort)tile.frameY;
		lock (_planCache)
		{
			if (_planCache.TryGetValue(key, out var cached))
			{
				if (cached.HasValue)
				{
					plan = cached.Value;
					return true;
				}
				return false;
			}
		}
		Plan? found = null;
		try
		{
			found = Solve(tile.type, tile.frameX, tile.frameY);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		lock (_planCache)
		{
			_planCache[key] = found;
		}
		if (found.HasValue)
		{
			plan = found.Value;
			return true;
		}
		return false;
	}

	private static Plan? Solve(int type, int frameX, int frameY)
	{
		if (TileObjectData.GetTileData(type, 0) == null)
		{
			return null;
		}
		List<KeyValuePair<int, int>> styles = StylesOf(type);
		if (styles == null || styles.Count == 0)
		{
			return null;
		}
		// 0: exact (state lines a style skips allowed). 1: any other line is a state (gem lock filled: line 1 exists
		// in the sheet but no item places it). 2: a single-item object with a random look (books, gnomes): its style.
		for (int pass = 0; pass < 3; pass++)
		{
			if (pass == 2 && styles.Count != 1)
			{
				break;
			}
			foreach (KeyValuePair<int, int> pair in styles)
			{
				int style = pair.Key;
				if (style < 0)
				{
					continue;
				}
				TileObjectData styleData = TileObjectData.GetTileData(type, style);
				if (styleData == null)
				{
					continue;
				}
				int alternates = styleData.AlternatesCount;
				for (int alternate = 0; alternate <= alternates; alternate++)
				{
					TileObjectData data = TileObjectData.GetTileData(type, style, alternate);
					if (data == null)
					{
						continue;
					}
					int randoms = Math.Max(data.RandomStyleRange, 0);
					for (int random = ((randoms > 0) ? 0 : (-1)); random < Math.Max(randoms, 0); random++)
					{
						if (Matches(data, style, alternate, random, frameX, frameY, pass))
						{
							return new Plan
							{
								Type = type,
								Style = style,
								Alternate = alternate,
								Random = random,
								Direction = ((data.Direction == TileObjectDirection.PlaceLeft) ? (-1) : 1),
								Item = pair.Value,
								Origin = new Point(data.Origin.X, data.Origin.Y)
							};
						}
						if (random < 0)
						{
							break;
						}
					}
				}
			}
		}
		return null;
	}

	/// <summary>Whether TileObject.Place with these values writes this frame into the object's top-left tile.</summary>
	private static bool Matches(TileObjectData data, int style, int alternate, int random, int frameX, int frameY, int pass)
	{
		int placement = data.CalculatePlacementStyle(style, alternate, random);
		int line = 0;
		if (data.StyleWrapLimit > 0)
		{
			line = placement / data.StyleWrapLimit * data.StyleLineSkip;
			placement %= data.StyleWrapLimit;
		}
		int fx;
		int fy;
		if (data.StyleHorizontal)
		{
			fx = data.CoordinateFullWidth * placement;
			fy = data.CoordinateFullHeight * line;
		}
		else
		{
			fx = data.CoordinateFullWidth * line;
			fy = data.CoordinateFullHeight * placement;
		}
		int dx = frameX - fx;
		int dy = frameY - fy;
		if (pass == 2)
		{
			return frameX >= 0 && frameY >= 0 && frameX % data.CoordinateFullWidth == 0 && frameY % data.CoordinateFullHeight == 0;
		}
		// The lines a style skips hold its states (open/closed, on/off, the door's visual variants): accept them. Without
		// a wrap limit every style sits on the first line, so any other line is a state (music box playing...).
		int skip = ((data.StyleWrapLimit > 0 && pass == 0) ? Math.Max(data.StyleLineSkip, 1) : 64);
		if (data.StyleHorizontal)
		{
			return dx == 0 && dy >= 0 && dy < data.CoordinateFullHeight * skip && dy % data.CoordinateFullHeight == 0;
		}
		return dy == 0 && dx >= 0 && dx < data.CoordinateFullWidth * skip && dx % data.CoordinateFullWidth == 0;
	}

	/// <summary>
	/// Whether this tile belongs to an object TryPlan can rebuild (any of its tiles, not only the top-left one). Those
	/// keep their frames as they are: the old per-type frame fixups were written for 1.4.4 and break several of them.
	/// </summary>
	public static bool Recognizes(TileCopy tile)
	{
		if (tile == null || !tile.active() || tile.type >= TileID.Count || !Main.tileFrameImportant[tile.type] || tile.frameX < 0 || tile.frameY < 0)
		{
			return false;
		}
		try
		{
			Tile probe = new Tile();
			probe.LoadFrom(tile);
			TileObjectData data = TileObjectData.GetTileData(probe);
			if (data == null || data.CoordinateFullWidth <= 0 || data.CoordinateFullHeight <= 0)
			{
				return false;
			}
			int inX = tile.frameX % data.CoordinateFullWidth;
			int inY = tile.frameY % data.CoordinateFullHeight;
			int column = inX / (data.CoordinateWidth + data.CoordinatePadding);
			int row = 0;
			int offsetY = 0;
			while (row < data.CoordinateHeights.Length && offsetY + data.CoordinateHeights[row] + data.CoordinatePadding <= inY)
			{
				offsetY += data.CoordinateHeights[row] + data.CoordinatePadding;
				row++;
			}
			TileCopy topLeft = new TileCopy();
			topLeft.CopyFrom(tile);
			topLeft.frameX = (short)(tile.frameX - column * (data.CoordinateWidth + data.CoordinatePadding));
			topLeft.frameY = (short)(tile.frameY - offsetY);
			return TryPlan(topLeft, out _);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>Closed door left by LazyTileFixup for an open door: the direction to open it again, or 0.</summary>
	public static int OpenDoorDirection(TileCopy original)
	{
		if (original == null || !original.active() || original.type != 11)
		{
			return 0;
		}
		int x = original.frameX % 72;
		if (x == 0)
		{
			return 1;
		}
		if (x == 54)
		{
			return -1;
		}
		return 0;
	}
}
