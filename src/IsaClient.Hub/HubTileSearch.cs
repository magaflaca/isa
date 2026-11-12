using System;
using System.Collections.Generic;
using IsaClient.WorldTools;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// World > Find Tiles: pick a tile from a grid (type to narrow it: name or id), search the whole world, then the places
/// it is in, nearest first, each with its coordinates, a button to see it on the map and one to go there. The work is
/// <see cref="TileSearch"/> (also .searchtile).
/// </summary>
internal static class HubTileSearch
{
	private static HubTheme Theme => HubTheme.Current;

	private static readonly Color Green = new Color(60, 140, 80);

	private static string _query = "";

	private static string _filteredFor;

	private static List<TileSearch.Kind> _shown = new List<TileSearch.Kind>();

	private static TileSearch.Kind _picked;

	private static float _scroll;

	private static string _scrollDrag;

	private static float _scrollDragFrom;

	private const int Cell = 46;

	private const int GridRows = 4;

	/// <summary>How many places are listed (the rest are on the map).</summary>
	private const int MaxPlaces = 40;

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y = DrawPicker(sb, x, y, width) + 12;
		y = DrawPicked(sb, x, y, width) + 12;
		y = DrawResults(sb, x, y, width);
		return y - start;
	}

	/// <summary>An icon for a kind: the item that places it, else the tile's own picture.</summary>
	private static HubIcon Icon(TileSearch.Kind kind)
	{
		if (kind.Item > 0)
		{
			return HubIcon.Item(kind.Item);
		}
		int type = kind.Type;
		Rectangle frame = Main.tileFrameImportant[type] ? new Rectangle(0, 0, 16, 16) : new Rectangle(162, 54, 16, 16);
		return HubIcon.Sprite(delegate
		{
			Main.instance.LoadTiles(type);
			return TextureAssets.Tile[type].Value;
		}, frame);
	}

	/// <summary>The search field and the grid of tiles (it scrolls inside its box).</summary>
	private static int DrawPicker(SpriteBatch sb, int x, int y, int width)
	{
		_query = HubKit.Field(sb, "tilesearch:q", new Rectangle(x, y, width, 34), _query, "Find a tile or liquid: name or id (ore, chest, life crystal, lava, 21...)", 40, HubIcon.Item(I.MetalDetector));
		y += 42;
		if (_filteredFor != _query)
		{
			_filteredFor = _query;
			_shown = TileSearch.Find(_query);
			_scroll = 0f;
		}
		int columns = Math.Max(1, (width - 16) / (Cell + 4));
		int rows = (_shown.Count + columns - 1) / columns;
		int viewRows = Math.Max(1, Math.Min(GridRows, rows));
		Rectangle box = new Rectangle(x, y, width, viewRows * (Cell + 4) + 8);
		HubKit.Box(sb, box, Theme.Card * HubPrefs.Opacity);
		if (_shown.Count == 0)
		{
			HubKit.Text(sb, "No tile has that name.", box.X + 14, box.Y + 18, Theme.Muted, 0.72f);
			return box.Bottom;
		}
		float content = rows * (Cell + 4) + 8;
		Rectangle view = new Rectangle(box.X + 4, box.Y + 4, box.Width - 20, box.Height - 8);
		if (content > box.Height)
		{
			HubInput.InnerScroll = box;
			if (HubInput.Wheel != 0 && HubInput.In(box))
			{
				_scroll -= HubInput.Wheel / 2f;
				HubInput.Wheel = 0;
			}
			_scroll = MathHelper.Clamp(_scroll, 0f, content - box.Height);
			_scroll = HubKit.Scrollbar(sb, "tilesearch:bar", new Rectangle(box.Right - 12, box.Y + 4, 8, box.Height - 8), _scroll, content, box.Height, ref _scrollDrag, ref _scrollDragFrom);
		}
		else
		{
			_scroll = 0f;
		}
		bool outer = HubInput.Clipping;
		Rectangle outerClip = HubInput.Clip;
		HubInput.BeginClip(sb, outer ? Rectangle.Intersect(view, outerClip) : view);
		int firstRow = Math.Max(0, (int)(_scroll / (Cell + 4)));
		for (int i = firstRow * columns; i < _shown.Count; i++)
		{
			int row = i / columns;
			Rectangle r = new Rectangle(view.X + (i % columns) * (Cell + 4), view.Y + row * (Cell + 4) - (int)_scroll, Cell, Cell);
			if (r.Y > view.Bottom)
			{
				break;
			}
			TileSearch.Kind kind = _shown[i];
			bool picked = _picked == kind;
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover("tilesearch:k:" + kind.Id, kind.Name + "  " + kind.Id))
			{
				_picked = kind;
			}
			float glow = HubInput.Anim("tilesearch:k:" + kind.Id, hover || picked);
			HubKit.Box(sb, r, picked ? Color.Lerp(Theme.Slot, Theme.Accent, 0.55f) : Color.Lerp(Theme.Slot, Theme.CardHover, glow));
			Rectangle icon = new Rectangle(r.X + 8, r.Y + 8, r.Width - 16, r.Height - 16);
			icon.Inflate((int)(glow * 2f), (int)(glow * 2f));
			Icon(kind).Draw(sb, icon, Color.White, 2f);
		}
		HubInput.EndClip(sb);
		if (outer)
		{
			HubInput.BeginClip(sb, outerClip);
		}
		HubKit.RawText(sb, IsaClient.Languages.L10n.F("{0} tiles", _shown.Count), box.Right - 90, box.Bottom + 4, Theme.Muted * 0.8f, 0.6f);
		return box.Bottom + 14;
	}

	/// <summary>The picked tile: its name and id, the search button and how far it went.</summary>
	private static int DrawPicked(SpriteBatch sb, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 76);
		HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.Accent, 0.1f) * HubPrefs.Opacity);
		TileSearch.Kind kind = _picked ?? (TileSearch.Searching ? TileSearch.SearchingFor : TileSearch.Last?.Kind);
		if (kind == null)
		{
			HubIcon.Item(I.MetalDetector).Draw(sb, new Rectangle(r.X + 14, r.Y + 16, 44, 44), Color.White * 0.5f);
			HubKit.Text(sb, "Pick a tile above, then search the whole world for it.", r.X + 72, r.Y + 18, Color.White, 0.8f, width - 90);
			HubKit.Text(sb, "The places show on the full map the next time you open it.", r.X + 72, r.Y + 42, Theme.Muted, 0.66f, width - 90);
			return r.Bottom;
		}
		Rectangle slot = new Rectangle(r.X + 12, r.Y + 12, 52, 52);
		HubKit.Box(sb, slot, Theme.Slot);
		Icon(kind).Draw(sb, new Rectangle(slot.X + 8, slot.Y + 8, 36, 36), Color.White, 2.5f);
		HubKit.RawText(sb, kind.Name, r.X + 76, r.Y + 12, Color.White, 0.92f, width - 300);
		HubKit.RawText(sb, kind.Id + "  ·  " + kind.Internal, r.X + 76, r.Y + 38, Theme.Muted, 0.64f, width - 300);
		Rectangle button = new Rectangle(r.Right - 200, r.Y + 20, 186, 36);
		if (TileSearch.Searching)
		{
			HubKit.Box(sb, button, Theme.Slot);
			Rectangle bar = new Rectangle(button.X + 4, button.Bottom - 8, (int)((button.Width - 8) * TileSearch.Progress), 4);
			HubKit.Fill(sb, bar, TileSearch.TileColor);
			HubKit.TextCentered(sb, IsaClient.Languages.L10n.F("Searching... {0}%", (int)(TileSearch.Progress * 100f)), new Rectangle(button.X, button.Y - 2, button.Width, button.Height), Color.White, 0.74f);
		}
		else if (HubKit.Button(sb, "tilesearch:go", button, "Search the world", Green, "Reads every tile of the world for this one.", !Main.gameMenu, HubIcon.Item(I.MetalDetector)))
		{
			TileSearch.Start(kind);
		}
		return r.Bottom;
	}

	/// <summary>How many were found, the map button, the warning when the world isn't all here, and the places.</summary>
	private static int DrawResults(SpriteBatch sb, int x, int y, int width)
	{
		TileSearch.Result result = TileSearch.Last;
		if (result == null)
		{
			return y;
		}
		if (result.Loaded < 0.99f && Main.netMode == 1)
		{
			Rectangle warn = new Rectangle(x, y, width, 44);
			HubKit.Box(sb, warn, Color.Lerp(Theme.Card, new Color(255, 190, 80), 0.18f) * HubPrefs.Opacity);
			HubKit.Text(sb, IsaClient.Languages.L10n.F("Only {0}% of the world is loaded here, so some may be missing. Reveal the map to load the rest, then search again.", (int)(result.Loaded * 100f)), warn.X + 14, warn.Y + 6, Color.White, 0.66f, width - 190);
			if (HubKit.Button(sb, "tilesearch:reveal", new Rectangle(warn.Right - 160, warn.Y + 7, 148, 30), "Reveal map", Theme.Slot, "Asks the server for the whole map (.revmap).", true, HubIcon.Item(I.TrifoldMap)))
			{
				MapReveal.RevealMapFromUI();
			}
			y = warn.Bottom + 10;
		}
		Point you = Main.gameMenu ? Point.Zero : Main.LocalPlayer.Center.ToTileCoordinates();
		Rectangle head = new Rectangle(x, y, width, 48);
		HubKit.Box(sb, head, Theme.Card * HubPrefs.Opacity);
		if (result.Total == 0)
		{
			HubKit.Text(sb, IsaClient.Languages.L10n.F("No {0} in the world.", result.Kind.Name), head.X + 14, head.Y + 15, Color.White, 0.8f, width - 28);
			return head.Bottom + 8;
		}
		HubKit.RawText(sb, IsaClient.Languages.L10n.F("{0} {1} in {2} places", result.Total.ToString("N0"), result.Kind.Name, result.Places.Count), head.X + 14, head.Y + 6, Color.White, 0.84f, width - 350);
		HubKit.RawText(sb, IsaClient.Languages.L10n.F("Searched in {0:0.0} s · on the full map: tiles in pink, places ringed.", result.Seconds), head.X + 14, head.Y + 28, Theme.Muted, 0.6f, width - 350);
		if (HubKit.Button(sb, "tilesearch:map", new Rectangle(head.Right - 312, head.Y + 9, 150, 30), "Show on map", Theme.Slot, "Opens the full map on the nearest place, with the marks. They go away when you close it.", !Main.gameMenu, HubIcon.Item(I.TrifoldMap)))
		{
			TileSearch.ShowOnMap(result.Places[0].Spot);
		}
		if (HubKit.Button(sb, "tilesearch:clear", new Rectangle(head.Right - 156, head.Y + 9, 144, 30), "Clear", Theme.Slot, "Forgets this search and its marks.", true, HubIcon.Item(I.TrashCan)))
		{
			TileSearch.Clear();
			return head.Bottom + 8;
		}
		y = head.Bottom + 10;
		HubKit.Text(sb, "Places, nearest first", x, y, Theme.Muted, 0.72f);
		y += 22;
		int count = Math.Min(MaxPlaces, result.Places.Count);
		for (int i = 0; i < count; i++)
		{
			TileSearch.Place place = result.Places[i];
			Rectangle r = new Rectangle(x, y, width, 42);
			if (!HubInput.Visible(r))
			{
				y = r.Bottom + 5;
				continue;
			}
			HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
			string number = (i + 1).ToString();
			HubKit.RawText(sb, number, r.X + 12, r.Y + 11, TileSearch.RingColor, 0.8f);
			Icon(result.Kind).Draw(sb, new Rectangle(r.X + 40, r.Y + 9, 24, 24), Color.White);
			HubKit.RawText(sb, "×" + place.Count.ToString("N0"), r.X + 70, r.Y + 5, Color.White, 0.78f);
			HubKit.RawText(sb, IsaClient.Languages.L10n.F("X {0}, Y {1}", place.Spot.X, place.Spot.Y), r.X + 70, r.Y + 23, Theme.Muted, 0.6f);
			HubKit.RawText(sb, Where(place.Spot, you), r.X + 200, r.Y + 13, Color.White * 0.9f, 0.66f, width - 420);
			if (HubKit.Button(sb, "tilesearch:pm:" + i, new Rectangle(r.Right - 196, r.Y + 6, 90, 30), "Map", Theme.Slot, "Opens the full map here, with the marks.", !Main.gameMenu, HubIcon.Item(I.TrifoldMap)))
			{
				TileSearch.ShowOnMap(place.Spot);
			}
			if (HubKit.Button(sb, "tilesearch:tp:" + i, new Rectangle(r.Right - 100, r.Y + 6, 90, 30), "TP", Green, "Teleports you on top of it.", !Main.gameMenu, HubIcon.Item(I.MagicMirror)))
			{
				TileSearch.TeleportTo(place.Spot);
			}
			y = r.Bottom + 5;
		}
		if (result.Places.Count > count)
		{
			HubKit.Text(sb, IsaClient.Languages.L10n.F("And {0} more places: all of them are on the map.", result.Places.Count - count), x + 4, y + 2, Theme.Muted, 0.66f);
			y += 24;
		}
		return y;
	}

	/// <summary>"350 tiles left, 120 down" (from you), as far as it is.</summary>
	private static string Where(Point spot, Point you)
	{
		int dx = spot.X - you.X;
		int dy = spot.Y - you.Y;
		string h = (dx == 0) ? "" : IsaClient.Languages.L10n.F((dx > 0) ? "{0} right" : "{0} left", Math.Abs(dx));
		string v = (dy == 0) ? "" : IsaClient.Languages.L10n.F((dy > 0) ? "{0} down" : "{0} up", Math.Abs(dy));
		string both = (h.Length > 0 && v.Length > 0) ? (h + ", " + v) : (h + v);
		if (both.Length == 0)
		{
			return IsaClient.Languages.L10n.T("Right here");
		}
		return IsaClient.Languages.L10n.F("{0}  ·  {1} tiles away", both, (int)TileSearch.Distance(spot, you));
	}
}
