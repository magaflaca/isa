using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Building;
using IsaClient.Core;
using IsaClient.Gameplay;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// Building > Sprite Art: a picker like Journey's item browser, but for the game's sprites (items, NPCs, bosses,
/// projectiles, buffs), with search and categories. Pick one, choose the frame, size and options, and it becomes a
/// pixel-art paste made of real blocks, the same way a player's character is (<see cref="CharacterArt"/>).
/// </summary>
internal static class HubSpriteArt
{
	private enum Kind
	{
		Items,
		Npcs,
		Bosses,
		Projectiles,
		Buffs
	}

	private readonly struct Sprite
	{
		public readonly Kind Kind;

		public readonly int Type;

		public readonly string Name;

		public Sprite(Kind kind, int type, string name)
		{
			Kind = kind;
			Type = type;
			Name = name;
		}
	}

	private static HubTheme Theme => HubTheme.Current;

	private static readonly string[] KindNames = { "Items", "NPCs", "Bosses", "Projectiles", "Buffs" };

	private static readonly int[] KindIcons = { I.IronPickaxe, I.GuideVoodooDoll, I.SuspiciousLookingEye, I.WoodenArrow, I.RegenerationPotion };

	private static readonly Dictionary<Kind, List<Sprite>> _lists = new Dictionary<Kind, List<Sprite>>();

	private static Kind _kind = Kind.Items;

	private static string _search = "";

	private static List<Sprite> _results = new List<Sprite>();

	private static string _resultsKey;

	private static float _scroll;

	private static string _scrollDrag;

	private static float _scrollDragFrom;

	private static Sprite? _picked;

	private static int _frame;

	/// <summary>The picked frame's art pixels (made again only when something changes).</summary>
	private static Color[] _pixels;

	private static int _pixelsW;

	private static int _pixelsH;

	private static string _pixelsKey;

	/// <summary>Terraria draws most sprites with 2x2 pixels: halved, each art pixel becomes one block.</summary>
	[Setting("SpriteArtHalf")]
	public static bool Half = true;

	[Setting("SpriteArtFlip")]
	public static bool Flip;

	#region Lists

	private static List<Sprite> List(Kind kind)
	{
		if (_lists.TryGetValue(kind, out List<Sprite> list))
		{
			return list;
		}
		list = new List<Sprite>();
		try
		{
			switch (kind)
			{
			case Kind.Items:
				for (int t = 1; t < I.Count; t++)
				{
					string name = Lang.GetItemNameValue(t);
					if (!string.IsNullOrWhiteSpace(name) && !I.Sets.Deprecated[t])
					{
						list.Add(new Sprite(kind, t, name));
					}
				}
				break;
			case Kind.Npcs:
			case Kind.Bosses:
				for (int t = 1; t < NPCID.Count; t++)
				{
					string name = Lang.GetNPCNameValue(t);
					if (string.IsNullOrWhiteSpace(name))
					{
						continue;
					}
					bool boss = (ContentSamples.NpcsByNetId.TryGetValue(t, out NPC sample) && sample.boss) || NPCID.Sets.ShouldBeCountedAsBossForBestiary[t];
					if (kind == Kind.Npcs || boss)
					{
						list.Add(new Sprite(kind, t, name));
					}
				}
				break;
			case Kind.Projectiles:
				for (int t = 1; t < ProjectileID.Count; t++)
				{
					string name = Lang.GetProjectileName(t).Value;
					if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("ProjectileName."))
					{
						list.Add(new Sprite(kind, t, name));
					}
				}
				break;
			case Kind.Buffs:
				for (int t = 1; t < BuffID.Count; t++)
				{
					string name = Lang.GetBuffName(t);
					if (!string.IsNullOrWhiteSpace(name))
					{
						list.Add(new Sprite(kind, t, name));
					}
				}
				break;
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		_lists[kind] = list;
		return list;
	}

	/// <summary>
	/// The sprite's texture and how many frames it has (stacked top to bottom). Thumbnails ask for it in the background
	/// (null until it's there); the picked one waits for it.
	/// </summary>
	private static Texture2D Texture(Sprite s, out int frames, bool wait = false)
	{
		frames = 1;
		try
		{
			Asset<Texture2D> asset;
			switch (s.Kind)
			{
			case Kind.Items:
				frames = (Main.itemAnimations[s.Type] != null) ? Math.Max(1, Main.itemAnimations[s.Type].FrameCount) : 1;
				asset = TextureAssets.Item[s.Type];
				break;
			case Kind.Npcs:
			case Kind.Bosses:
				frames = Math.Max(1, Main.npcFrameCount[s.Type]);
				asset = TextureAssets.Npc[s.Type];
				break;
			case Kind.Projectiles:
				frames = Math.Max(1, Main.projFrames[s.Type]);
				asset = TextureAssets.Projectile[s.Type];
				break;
			default:
				asset = TextureAssets.Buff[s.Type];
				break;
			}
			if (asset.IsLoaded)
			{
				return asset.Value;
			}
			Asset<Texture2D> requested = Main.Assets.Request<Texture2D>(asset.Name, wait ? AssetRequestMode.ImmediateLoad : AssetRequestMode.AsyncLoad);
			return requested.IsLoaded ? requested.Value : null;
		}
		catch
		{
		}
		return null;
	}

	private static Rectangle Frame(Texture2D texture, int frames, int frame)
	{
		int h = texture.Height / Math.Max(1, frames);
		return new Rectangle(0, Utils.Clamp(frame, 0, frames - 1) * h, texture.Width, h);
	}

	private static List<Sprite> Results()
	{
		string query = (_search ?? "").Trim();
		string key = _kind + "|" + query;
		if (key == _resultsKey)
		{
			return _results;
		}
		_resultsKey = key;
		_scroll = 0f;
		List<Sprite> all = List(_kind);
		if (query.Length == 0)
		{
			_results = all;
		}
		else
		{
			bool number = int.TryParse(query, out int id);
			_results = all.Where(s => (number && s.Type == id) || s.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
		}
		return _results;
	}

	#endregion

	#region Making the art

	/// <summary>The frame's pixels: straight colors, see-through edges dropped, halved and cropped, flipped if asked.</summary>
	private static Color[] Pixels(Sprite s, int frame, out int width, out int height)
	{
		width = height = 0;
		Texture2D texture = Texture(s, out int frames, wait: true);
		if (texture == null)
		{
			return null;
		}
		Rectangle src = Frame(texture, frames, frame);
		Color[] data = new Color[src.Width * src.Height];
		texture.GetData(0, src, data, 0, data.Length);
		for (int i = 0; i < data.Length; i++)
		{
			Color c = data[i];
			if (c.A < 110)
			{
				data[i] = Color.Transparent;
			}
			else if (c.A < 255)
			{
				// Game textures hold premultiplied colors.
				float a = c.A / 255f;
				data[i] = new Color((int)Math.Min(255f, c.R / a), (int)Math.Min(255f, c.G / a), (int)Math.Min(255f, c.B / a), 255);
			}
		}
		int w = src.Width;
		int h = src.Height;
		if (Half && w >= 4 && h >= 4)
		{
			data = CharacterArt.Downsample(data, w, h, out w, out h);
		}
		data = CharacterArt.Crop(data, w, h, out width, out height);
		if (data != null && Flip)
		{
			for (int y = 0; y < height; y++)
			{
				Array.Reverse(data, y * width, width);
			}
		}
		return data;
	}

	private static void Paste()
	{
		if (!_picked.HasValue || Main.gameMenu)
		{
			return;
		}
		Sprite s = _picked.Value;
		try
		{
			Color[] pixels = Pixels(s, _frame, out int width, out int height);
			if (pixels == null || width == 0 || height == 0)
			{
				ClientChat.Error(IsaClient.Languages.L10n.F("{0} has nothing to draw.", s.Name));
				return;
			}
			CharacterArt.LoadIntoPaste(pixels, width, height, CharacterPresets.ArtScale, CharacterPresets.ArtWalkThrough, CharacterPresets.ArtGlow);
			ClientChat.Info(IsaClient.Languages.L10n.F("Pixel art of {0} ready to paste ({1}x{2} blocks). Left click in the world to place it; Ctrl+Z undoes it.", s.Name, Selection.SchematicSizeX, Selection.SchematicSizeY));
			SoundEngine.PlaySound(12);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			ClientChat.Error("Could not make the pixel art: " + ex.Message);
		}
	}

	#endregion

	#region Drawing

	/// <summary>Pixel art from here on drawn sharp in the panel's batch; <see cref="HubInput.Restart"/> after.</summary>
	private static void Sharp(SpriteBatch sb)
	{
		sb.End();
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, HubInput.Clipping ? new RasterizerState
		{
			CullMode = CullMode.None,
			ScissorTestEnable = true
		} : RasterizerState.CullCounterClockwise, null, HubInput.Matrix);
	}

	/// <summary>A frame of a sprite fitted in a box (never blurry: whole-pixel steps when it's enlarged).</summary>
	private static void DrawSprite(SpriteBatch sb, Texture2D texture, Rectangle src, Rectangle box, bool flip, float maxScale)
	{
		float scale = Math.Min((float)box.Width / src.Width, (float)box.Height / src.Height);
		if (scale >= 1f)
		{
			scale = Math.Min((float)Math.Floor(scale), maxScale);
		}
		Vector2 size = new Vector2(src.Width, src.Height) * scale;
		sb.Draw(texture, new Vector2((float)Math.Round(box.Center.X - size.X / 2f), (float)Math.Round(box.Center.Y - size.Y / 2f)), src, Color.White, 0f, Vector2.Zero, scale, flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
	}

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		// Intro.
		Rectangle intro = new Rectangle(x, y, width, 50);
		HubKit.Box(sb, intro, Color.Lerp(Theme.Card, new Color(255, 150, 110), 0.12f) * HubPrefs.Opacity);
		HubIcon.Item(I.PaintingTheSeason).Draw(sb, new Rectangle(intro.X + 12, intro.Y + 9, 32, 32), Color.White, 3f);
		HubKit.Text(sb, "Sprite Art", intro.X + 54, intro.Y + 6, Color.White, 0.88f);
		HubKit.Text(sb, "Any sprite of the game, built out of real blocks: pick one, then paste it in the world.", intro.X + 54, intro.Y + 27, Theme.Muted, 0.64f, width - 70);
		y = intro.Bottom + 10;
		// Search and categories.
		_search = HubKit.Field(sb, "spriteart:search", new Rectangle(x, y, width, 30), _search, IsaClient.Languages.L10n.T("Search sprites by name or id..."), 40, HubIcon.Texture("Images/UI/Cursor_2"));
		y += 38;
		int cx = x;
		for (int k = 0; k < KindNames.Length; k++)
		{
			string label = IsaClient.Languages.L10n.T(KindNames[k]);
			int w = (int)HubKit.Measure(label, 0.78f) + 50;
			bool on = (int)_kind == k;
			if (HubKit.Button(sb, "spriteart:kind:" + k, new Rectangle(cx, y, w, 30), label, on ? Color.Lerp(Theme.Slot, Theme.Accent, 0.45f) : Theme.Slot, null, true, HubIcon.Item(KindIcons[k])))
			{
				_kind = (Kind)k;
				_picked = null;
			}
			cx += w + 5;
		}
		List<Sprite> results = Results();
		string count = IsaClient.Languages.L10n.F("{0} sprites", results.Count);
		HubKit.Text(sb, count, x + width - HubKit.Measure(count, 0.64f), y + 8, Theme.Muted, 0.64f);
		y += 40;
		// The grid on the left, the picked sprite on the right.
		const int sideW = 250;
		// As tall as the page shows, so the page itself never scrolls.
		int gridH = Math.Max(300, ClientHub.PageViewHeight - (y - start) - 20);
		Rectangle grid = new Rectangle(x, y, width - sideW - 22, gridH);
		Rectangle side = new Rectangle(grid.Right + 22, y, sideW, gridH);
		DrawGrid(sb, grid, results);
		DrawPicked(sb, side);
		return grid.Bottom + 8 - start;
	}

	private static void DrawGrid(SpriteBatch sb, Rectangle area, List<Sprite> results)
	{
		HubKit.Box(sb, area, Color.Black * 0.25f);
		Rectangle inner = new Rectangle(area.X + 6, area.Y + 6, area.Width - 22, area.Height - 12);
		const int cell = 46;
		const int gap = 4;
		int columns = Math.Max(1, (inner.Width + gap) / (cell + gap));
		int rows = (results.Count + columns - 1) / columns;
		float content = rows * (cell + gap);
		HubInput.InnerScroll = area;
		if (HubInput.Wheel != 0 && HubInput.In(area))
		{
			_scroll -= HubInput.Wheel / 2f;
			HubInput.Wheel = 0;
		}
		_scroll = MathHelper.Clamp(_scroll, 0f, Math.Max(0f, content - inner.Height));
		_scroll = HubKit.Scrollbar(sb, "spriteart:bar", new Rectangle(area.Right - 12, area.Y + 6, 10, area.Height - 12), _scroll, content, inner.Height, ref _scrollDrag, ref _scrollDragFrom);
		if (results.Count == 0)
		{
			HubKit.Text(sb, "Nothing found.", inner.X + 8, inner.Y + 8, Theme.Muted, 0.72f);
			return;
		}
		bool outer = HubInput.Clipping;
		Rectangle outerClip = HubInput.Clip;
		HubInput.BeginClip(sb, outer ? Rectangle.Intersect(inner, outerClip) : inner);
		int first = (int)(_scroll / (cell + gap));
		int last = Math.Min(rows - 1, first + inner.Height / (cell + gap) + 1);
		// Slots first (normal batch), then every sprite in one sharp batch.
		List<(Rectangle box, Sprite s)> visible = new List<(Rectangle, Sprite)>();
		for (int row = first; row <= last; row++)
		{
			for (int col = 0; col < columns; col++)
			{
				int i = row * columns + col;
				if (i >= results.Count)
				{
					break;
				}
				Sprite s = results[i];
				Rectangle r = new Rectangle(inner.X + col * (cell + gap), inner.Y + row * (cell + gap) - (int)_scroll, cell, cell);
				bool picked = _picked.HasValue && _picked.Value.Kind == s.Kind && _picked.Value.Type == s.Type;
				bool hover = HubInput.In(r);
				HubKit.Slot(sb, r, picked ? Color.Lerp(Theme.Slot, Theme.Accent, 0.55f) : (hover ? Color.Lerp(Theme.Slot, Color.White, 0.15f) : Theme.Slot));
				if (hover && HubInput.Hover("spriteart:" + s.Kind + ":" + s.Type, s.Name + "  #" + s.Type))
				{
					_picked = s;
					_frame = 0;
					SoundEngine.PlaySound(12);
				}
				visible.Add((r, s));
			}
		}
		Sharp(sb);
		foreach ((Rectangle box, Sprite s) in visible)
		{
			Texture2D texture = Texture(s, out int frames);
			if (texture != null)
			{
				Rectangle b = new Rectangle(box.X + 5, box.Y + 5, box.Width - 10, box.Height - 10);
				DrawSprite(sb, texture, Frame(texture, frames, 0), b, false, 1f);
			}
		}
		HubInput.Restart(sb, HubInput.Clipping);
		HubInput.EndClip(sb);
		if (outer)
		{
			HubInput.BeginClip(sb, outerClip);
		}
	}

	private static void DrawPicked(SpriteBatch sb, Rectangle area)
	{
		HubKit.Box(sb, area, Theme.Card * HubPrefs.Opacity);
		if (!_picked.HasValue)
		{
			HubIcon.Item(I.Paintbrush).Draw(sb, new Rectangle(area.Center.X - 20, area.Y + 120, 40, 40), Color.White * 0.5f, 3f);
			HubKit.Text(sb, "Pick a sprite on the left.", area.X + 16, area.Y + 172, Theme.Muted, 0.72f, area.Width - 32);
			return;
		}
		Sprite s = _picked.Value;
		Texture2D texture = Texture(s, out int frames, wait: true);
		_frame = Utils.Clamp(_frame, 0, frames - 1);
		// A checkered stage, the sprite as big as fits in whole steps.
		Rectangle stage = new Rectangle(area.X + 10, area.Y + 10, area.Width - 20, Math.Max(80, area.Height - 236));
		const int check = 10;
		for (int cy = 0; cy < stage.Height; cy += check)
		{
			for (int cx = 0; cx < stage.Width; cx += check)
			{
				bool dark = ((cx / check) + (cy / check)) % 2 == 0;
				HubKit.Fill(sb, new Rectangle(stage.X + cx, stage.Y + cy, Math.Min(check, stage.Width - cx), Math.Min(check, stage.Height - cy)), dark ? new Color(40, 44, 70) : new Color(50, 55, 86));
			}
		}
		if (texture != null)
		{
			Sharp(sb);
			DrawSprite(sb, texture, Frame(texture, frames, _frame), new Rectangle(stage.X + 8, stage.Y + 8, stage.Width - 16, stage.Height - 16), Flip, 8f);
			HubInput.Restart(sb, HubInput.Clipping);
		}
		int y = stage.Bottom + 8;
		HubKit.RawText(sb, s.Name, area.X + 12, y, Color.White, 0.84f, area.Width - 24);
		y += 20;
		HubKit.RawText(sb, IsaClient.Languages.L10n.T(KindNames[(int)s.Kind]) + "  #" + s.Type, area.X + 12, y, Theme.Muted, 0.6f);
		// Frames of animated sprites.
		if (frames > 1)
		{
			string text = IsaClient.Languages.L10n.F("Frame {0}/{1}", _frame + 1, frames);
			if (HubKit.Button(sb, "spriteart:prev", new Rectangle(area.Right - 112, y - 4, 26, 24), "<", Theme.Slot))
			{
				_frame = (_frame + frames - 1) % frames;
			}
			HubKit.RawText(sb, (_frame + 1) + "/" + frames, area.Right - 80, y, Color.White, 0.66f);
			if (HubKit.Button(sb, "spriteart:next", new Rectangle(area.Right - 38, y - 4, 26, 24), ">", Theme.Slot, text))
			{
				_frame = (_frame + 1) % frames;
			}
		}
		y += 24;
		// Size in blocks.
		string key = s.Kind + ":" + s.Type + ":" + _frame + ":" + Half + ":" + Flip;
		if (key != _pixelsKey)
		{
			_pixelsKey = key;
			try
			{
				_pixels = Pixels(s, _frame, out _pixelsW, out _pixelsH);
			}
			catch
			{
				_pixels = null;
				_pixelsW = _pixelsH = 0;
			}
		}
		Color[] pixels = _pixels;
		int w = _pixelsW;
		int h = _pixelsH;
		int scale = CharacterPresets.ArtScale;
		HubKit.RawText(sb, IsaClient.Languages.L10n.F("{0} x {1} blocks", w * scale, h * scale), area.X + 12, y, Theme.Accent, 0.72f);
		y += 22;
		// Options.
		HubKit.Text(sb, "Size", area.X + 12, y + 5, Theme.Muted, 0.64f);
		for (int i = 1; i <= 4; i++)
		{
			if (HubKit.Button(sb, "spriteart:scale:" + i, new Rectangle(area.X + 60 + (i - 1) * 44, y, 40, 24), "x" + i, (scale == i) ? Color.Lerp(Theme.Slot, Theme.Accent, 0.45f) : Theme.Slot))
			{
				CharacterPresets.ArtScale = i;
				ClientSettings.Save();
			}
		}
		y += 30;
		int half = (area.Width - 26) / 2;
		Toggle(sb, "spriteart:half", "Art pixels", "Terraria draws sprites with 2x2 pixels: one block per art pixel (off: one per screen pixel, twice as big).", Half, v => Half = v, new Rectangle(area.X + 10, y, half, 30));
		Toggle(sb, "spriteart:flip", "Mirror", "Faces the other way.", Flip, v => Flip = v, new Rectangle(area.X + 16 + half, y, half, 30));
		y += 34;
		Toggle(sb, "spriteart:walk", "Walk-through", "Blocks you can walk through (actuated).", CharacterPresets.ArtWalkThrough, v => CharacterPresets.ArtWalkThrough = v, new Rectangle(area.X + 10, y, half, 30));
		Toggle(sb, "spriteart:glow", "Glow", "Glows in the dark (Illuminant Coating).", CharacterPresets.ArtGlow, v => CharacterPresets.ArtGlow = v, new Rectangle(area.X + 16 + half, y, half, 30));
		Rectangle paste = new Rectangle(area.X + 10, area.Bottom - 42, area.Width - 20, 32);
		bool can = pixels != null && w > 0 && !Main.gameMenu;
		string tip = (w * h * scale * scale > 60000) ? "Very big: it takes a while to paste." : "Arms the paste: left click in the world to place it. Ctrl+Z undoes it.";
		if (HubKit.Button(sb, "spriteart:paste", paste, "Paste as pixel art", new Color(60, 140, 80), tip, can, HubIcon.Item(I.Paintbrush)))
		{
			Paste();
		}
	}

	/// <summary>An option as a little card: its name and the panel's usual switch; the whole card toggles it.</summary>
	private static void Toggle(SpriteBatch sb, string id, string label, string tip, bool value, Action<bool> set, Rectangle card)
	{
		bool hover = HubInput.In(card);
		float glow = HubInput.Anim(id + ":card", hover);
		HubKit.Box(sb, card, Color.Lerp(Theme.Slot, Color.White, 0.08f * glow) * 0.9f);
		HubKit.Text(sb, label, card.X + 8, card.Y + 8, value ? Color.White : Theme.Muted, 0.64f, card.Width - 56);
		HubKit.Switch(sb, id, new Rectangle(card.Right - 50, card.Y + 4, 44, 22), value, hover);
		if (hover && HubInput.Hover(id + "card", tip))
		{
			set(!value);
			ClientSettings.Save();
		}
	}

	#endregion
}
