using System;
using System.Globalization;
using IsaClient.Building;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// Building > Picture to Blocks: every option of .drawpic in one place (what it is made of, size, color matching,
/// dithering, filters), with the picture and the result side by side. Changing an option remakes the picture a moment
/// later; the paste follows when it still holds that picture.
/// </summary>
internal static class HubPictureArt
{
	private static HubTheme Theme => HubTheme.Current;

	private static readonly Color Tint = new Color(255, 150, 110);

	private static Texture2D _before;

	private static Texture2D _after;

	private static int _textureVersion = -1;

	/// <summary>When the options last changed (remade once they stay still a moment).</summary>
	private static DateTime _changedAt = DateTime.MaxValue;

	private static bool _showBefore;

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		Remake();
		// Intro.
		Rectangle intro = new Rectangle(x, y, width, 50);
		HubKit.Box(sb, intro, Color.Lerp(Theme.Card, Tint, 0.12f) * HubPrefs.Opacity);
		HubIcon.Item(I.PaintingTheSeason).Draw(sb, new Rectangle(intro.X + 12, intro.Y + 9, 32, 32), Color.White, 3f);
		HubKit.Text(sb, "Picture to Blocks", intro.X + 54, intro.Y + 6, Color.White, 0.88f);
		HubKit.Text(sb, "Any picture made of real walls and blocks with their real colors. Open one, tune it, then paste it (.drawpic).", intro.X + 54, intro.Y + 27, Theme.Muted, 0.64f, width - 70);
		y = intro.Bottom + 10;
		const int optionsW = 300;
		Rectangle options = new Rectangle(x, y, optionsW, 0);
		int optionsBottom = Options(sb, options);
		Rectangle side = new Rectangle(x + optionsW + 14, y, width - optionsW - 14, Math.Max(optionsBottom - y, ClientHub.PageViewHeight - (y - start) - 20));
		DrawPreview(sb, side);
		return Math.Max(optionsBottom, side.Bottom) + 8 - start;
	}

	#region Options

	private static int Options(SpriteBatch sb, Rectangle area)
	{
		int x = area.X;
		int w = area.Width;
		int y = area.Y;
		y = Header(sb, "Looks right on", x, y, w);
		y = Choice(sb, "pic:target", new[] { "Map", "World", "Generator" }, new[] { "Matches the colors the map shows (each block is one flat color there).", "Matches how blocks look in the world (their textures and paint), in full light.", "Block for block what the Terraria Pixel Art Generator (.tilemap) makes: its colors, its blocks, its math. Only Scale applies." }, PhotoImport.Target, v => PhotoImport.Target = v, x, y, w);
		y = Header(sb, "Made of", x, y + 4, w);
		y = Choice(sb, "pic:type", new[] { "Blocks", "Walls", "Both" }, new[] { "Blocks only: solid, but each kind gets its own border next to another kind.", "Walls only: seamless and sharp, the best for pictures (default).", "Walls and blocks: more colors, with block borders where they meet." }, PixelArt.DrawPhotoType, v => PixelArt.DrawPhotoType = v, x, y, w);
		y = Header(sb, "Size", x, y + 4, w);
		y = Slider(sb, "pic:scale", "Scale", PixelArt.DrawPhotoScale.ToString("0.#", CultureInfo.InvariantCulture) + "%", "Shrinks the picture first: 100% is one block per pixel.", (PixelArt.DrawPhotoScale - 1f) / 99f, v => PixelArt.DrawPhotoScale = (float)Math.Round(1f + v * 99f), x, y, w);
		y = Slider(sb, "pic:max", "Biggest side", PixelArt.DrawPhotoMaxSize + " blocks", "Bigger pictures are fitted to this (every block of a paste is kept for undo).", (PixelArt.DrawPhotoMaxSize - 16) / 4080f, v => PixelArt.DrawPhotoMaxSize = 16 + (int)Math.Round(v * 4080f / 16f) * 16, x, y, w);
		int half = (w - 6) / 2;
		Toggle(sb, "pic:sharp", "Pixel art", "Shrinks keeping hard pixels (for pixel art). Off: smooth, for photos and drawings.", PhotoImport.SharpResize, v => PhotoImport.SharpResize = v, new Rectangle(x, y, half, 30));
		Toggle(sb, "pic:flip", "Mirror", "Flips the picture left to right.", PhotoImport.Flip, v => PhotoImport.Flip = v, new Rectangle(x + half + 6, y, half, 30));
		y += 36;
		y = Header(sb, "Colors", x, y + 4, w);
		y = Choice(sb, "pic:match", new[] { "OKLab", "Redmean", "RGB" }, new[] { "Compares colors as eyes see them (best).", "Weighted RGB: the old way, a bit warmer.", "Plain RGB distance." }, PhotoImport.Match, v => PhotoImport.Match = v, x, y, w);
		y = Slider(sb, "pic:dither", "Dithering", PhotoImport.Dither + "%", "Mixes nearby colors to fake the missing ones (smoother gradients, grainier up close).", PhotoImport.Dither / 100f, v => PhotoImport.Dither = (int)Math.Round(v * 100f), x, y, w);
		if (PhotoImport.Target == 1)
		y = Slider(sb, "pic:plain", "Prefer plain textures", PhotoImport.Plain + "%", "Avoids bricks, bubbles and other busy textures unless the color is much better.", PhotoImport.Plain / 100f, v => PhotoImport.Plain = (int)Math.Round(v * 100f), x, y, w);
		Toggle(sb, "pic:paint", "Paint", "Uses paints (many more colors; the paste needs paint).", PhotoImport.UsePaint, v => PhotoImport.UsePaint = v, new Rectangle(x, y, half, 30));
		Toggle(sb, "pic:odd", "Shadow & Negative", "Also the Shadow and Negative paints.", PhotoImport.OddPaints, v => PhotoImport.OddPaints = v, new Rectangle(x + half + 6, y, half, 30), PhotoImport.UsePaint);
		y += 36;
		Toggle(sb, "pic:glow", "Glow (Illuminant Coating)", "Looks the same in the dark: the picture is not shaded by the world's light (the paste needs Illuminant Coating).", PhotoImport.Glow, v => PhotoImport.Glow = v, new Rectangle(x, y, w, 30));
		y += 36;
		y = Header(sb, "Filters", x, y + 4, w);
		y = Slider(sb, "pic:bright", "Brightness", Signed(PhotoImport.Brightness), null, (PhotoImport.Brightness + 100) / 200f, v => PhotoImport.Brightness = (int)Math.Round(v * 200f) - 100, x, y, w);
		y = Slider(sb, "pic:contrast", "Contrast", Signed(PhotoImport.Contrast), null, (PhotoImport.Contrast + 100) / 200f, v => PhotoImport.Contrast = (int)Math.Round(v * 200f) - 100, x, y, w);
		y = Slider(sb, "pic:sat", "Saturation", PhotoImport.Saturation + "%", "0% is black and white.", PhotoImport.Saturation / 200f, v => PhotoImport.Saturation = (int)Math.Round(v * 200f), x, y, w);
		y = Slider(sb, "pic:alpha", "See-through below", PhotoImport.AlphaCut.ToString(), "Pixels more transparent than this stay empty.", PhotoImport.AlphaCut / 255f, v => PhotoImport.AlphaCut = (int)Math.Round(v * 255f), x, y, w);
		int half2 = (w - 6) / 2;
		if (HubKit.Button(sb, "pic:reset", new Rectangle(x, y + 2, half2, 28), "Reset filters", Theme.Slot, "Brightness, contrast, saturation and transparency back to normal."))
		{
			PhotoImport.Brightness = 0;
			PhotoImport.Contrast = 0;
			PhotoImport.Saturation = 100;
			PhotoImport.AlphaCut = 30;
			Changed();
		}
		if (HubKit.Button(sb, "pic:best", new Rectangle(x + half2 + 6, y + 2, w - half2 - 6, 28), "Recommended", Color.Lerp(Theme.Slot, Tint, 0.35f), "The options that look best on the map: map colors, walls and blocks, OKLab, no dithering (closest color per pixel), no filters."))
		{
			PhotoImport.Recommended();
			Changed();
		}
		return y + 36;
	}

	private static string Signed(int value)
	{
		return (value > 0) ? ("+" + value) : value.ToString();
	}

	private static int Header(SpriteBatch sb, string text, int x, int y, int w)
	{
		HubKit.Text(sb, text, x + 2, y, Tint, 0.7f);
		HubKit.Line(sb, x, y + 20, w, Theme.Muted * 0.3f);
		return y + 26;
	}

	private static int Choice(SpriteBatch sb, string id, string[] labels, string[] tips, int value, Action<int> set, int x, int y, int w)
	{
		int each = (w - (labels.Length - 1) * 5) / labels.Length;
		for (int i = 0; i < labels.Length; i++)
		{
			bool on = value == i;
			if (HubKit.Button(sb, id + ":" + i, new Rectangle(x + i * (each + 5), y, each, 28), labels[i], on ? Color.Lerp(Theme.Slot, Theme.Accent, 0.45f) : Theme.Slot, tips[i]) && !on)
			{
				set(i);
				Changed();
			}
		}
		return y + 34;
	}

	private static int Slider(SpriteBatch sb, string id, string label, string value, string tip, float at, Action<float> set, int x, int y, int w)
	{
		Rectangle row = new Rectangle(x, y, w, 20);
		HubKit.Text(sb, label, x + 2, y, Theme.Muted, 0.64f);
		HubKit.RawText(sb, value, x + w - HubKit.Measure(value, 0.64f) - 2, y, Color.White, 0.64f);
		if (tip != null && HubInput.In(row))
		{
			HubInput.Hover(id + ":label", tip);
		}
		float? slid = HubKit.Slider(sb, id, new Rectangle(x, y + 20, w, 16), at, true, ref ClientHub.SliderDrag);
		if (slid.HasValue && Math.Abs(slid.Value - at) > 0.0001f)
		{
			set(slid.Value);
			Changed();
		}
		return y + 44;
	}

	private static void Toggle(SpriteBatch sb, string id, string label, string tip, bool value, Action<bool> set, Rectangle card, bool enabled = true)
	{
		bool hover = enabled && HubInput.In(card);
		float glow = HubInput.Anim(id + ":card", hover);
		HubKit.Box(sb, card, Color.Lerp(Theme.Slot, Color.White, 0.08f * glow) * (enabled ? 0.9f : 0.5f));
		HubKit.Text(sb, label, card.X + 8, card.Y + 8, (value && enabled) ? Color.White : Theme.Muted, 0.64f, card.Width - 58);
		HubKit.Switch(sb, id, new Rectangle(card.Right - 50, card.Y + 4, 44, 22), value, hover, enabled);
		if (hover && HubInput.Hover(id + "card", tip))
		{
			set(!value);
			Changed();
		}
	}

	private static void Changed()
	{
		_changedAt = DateTime.UtcNow;
	}

	/// <summary>Remakes the picture once the options stay still a moment (and saves them).</summary>
	private static void Remake()
	{
		if (_changedAt == DateTime.MaxValue || ClientHub.SliderDrag != null || (DateTime.UtcNow - _changedAt).TotalMilliseconds < 350)
		{
			return;
		}
		_changedAt = DateTime.MaxValue;
		ClientSettings.Save();
		PhotoImport.Refresh(arm: false);
	}

	#endregion

	#region Preview

	private static void DrawPreview(SpriteBatch sb, Rectangle area)
	{
		HubKit.Box(sb, area, Theme.Card * HubPrefs.Opacity);
		PhotoResult last = PhotoImport.Last;
		int x = area.X + 10;
		int y = area.Y + 10;
		int w = area.Width - 20;
		// Open, paste.
		int bw = (w - 12) / 3;
		if (HubKit.Button(sb, "pic:open", new Rectangle(x, y, bw, 32), "Open picture...", Color.Lerp(Theme.Slot, Tint, 0.35f), "Pick a picture (png, jpg, webp, gif...). You can also drop one on the game window.", !Main.gameMenu, HubIcon.Item(I.PaintingTheSeason)))
		{
			PixelArt.LoadPhotosFromFile();
		}
		bool canPaste = last != null && !Main.gameMenu && !PhotoImport.Busy;
		if (HubKit.Button(sb, "pic:paste", new Rectangle(x + bw + 6, y, bw, 32), "Paste it", new Color(60, 140, 80), "Arms the paste: left click in the world to place it. Ctrl+Z undoes it.", canPaste, HubIcon.Item(I.Paintbrush)))
		{
			PhotoImport.Arm();
			SoundEngine.PlaySound(12);
		}
		// Leaving paste mode (it was only .paste before).
		if (HubKit.Button(sb, "pic:stop", new Rectangle(x + (bw + 6) * 2, y, w - (bw + 6) * 2, 32), "Stop pasting", Color.Lerp(Theme.Slot, new Color(170, 60, 60), 0.5f), "Leaves paste mode: clicking the world no longer places the picture. What is already pasting keeps going (Building > Paste > Stop Paste stops it).", Selection.PasteMode, HubIcon.Item(I.TrashCan)))
		{
			Selection.PasteMode = false;
			SoundEngine.PlaySound(12);
		}
		y += 40;
		// Before / after.
		string[] tabs = { "Result", "Picture" };
		int tw = 90;
		for (int i = 0; i < tabs.Length; i++)
		{
			bool on = _showBefore == (i == 1);
			if (HubKit.Button(sb, "pic:tab:" + i, new Rectangle(x + i * (tw + 5), y, tw, 26), tabs[i], on ? Color.Lerp(Theme.Slot, Theme.Accent, 0.45f) : Theme.Slot, (i == 0) ? "How the blocks will look (their average colors)." : "The picture after size and filters."))
			{
				_showBefore = i == 1;
			}
		}
		const int tmw = 130;
		if (HubKit.Button(sb, "pic:tilemap", new Rectangle(x + w - tmw, y, tmw, 26), "Open .tilemap", Theme.Slot, "Loads a .tilemap saved by the Terraria Pixel Art Generator (\"Guardar TileMap\") to paste it and compare. You can also drop it on the game window.", !Main.gameMenu))
		{
			TileMapFiles.Open(null);
		}
		string status = PhotoImport.Status;
		if (status != null)
		{
			HubKit.RawText(sb, status, x + 2 * (tw + 5) + 6, y + 6, Theme.Accent, 0.62f, w - 2 * (tw + 5) - tmw - 16);
		}
		y += 34;
		Rectangle stage = new Rectangle(x, y, w, Math.Max(120, area.Bottom - y - 70));
		Checker(sb, stage);
		if (last == null)
		{
			HubIcon.Item(I.PaintingTheSeason).Draw(sb, new Rectangle(stage.Center.X - 20, stage.Center.Y - 40, 40, 40), Color.White * 0.5f, 3f);
			string hint = IsaClient.Languages.L10n.T("Open a picture to see it here.");
			HubKit.RawText(sb, hint, stage.Center.X - HubKit.Measure(hint, 0.72f) / 2f, stage.Center.Y + 10, Theme.Muted, 0.72f);
			return;
		}
		Textures(last);
		Texture2D shown = _showBefore ? _before : _after;
		if (shown != null)
		{
			float scale = Math.Min((float)(stage.Width - 8) / shown.Width, (float)(stage.Height - 8) / shown.Height);
			if (scale >= 1f)
			{
				scale = (float)Math.Floor(scale);
			}
			Vector2 size = new Vector2(shown.Width, shown.Height) * scale;
			Sharp(sb);
			sb.Draw(shown, new Vector2((float)Math.Round(stage.Center.X - size.X / 2f), (float)Math.Round(stage.Center.Y - size.Y / 2f)), null, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
			HubInput.Restart(sb, HubInput.Clipping);
		}
		// Facts.
		y = stage.Bottom + 8;
		HubKit.RawText(sb, last.Name, x, y, Color.White, 0.76f, w * 0.6f);
		string size2 = IsaClient.Languages.L10n.F("{0} x {1} blocks", last.Width, last.Height);
		HubKit.RawText(sb, size2, x + w - HubKit.Measure(size2, 0.72f), y, Theme.Accent, 0.72f);
		y += 22;
		string facts = IsaClient.Languages.L10n.F("{0} walls, {1} blocks · {2} kinds · {3} paints · made in {4} s", last.Walls, last.Blocks, last.Kinds, last.Paints, last.Seconds.ToString("0.0", CultureInfo.InvariantCulture));
		if (last.Fitted)
		{
			facts += "  ·  " + IsaClient.Languages.L10n.F("fitted from {0}x{1}", last.SourceWidth, last.SourceHeight);
		}
		HubKit.RawText(sb, facts, x, y, Theme.Muted, 0.62f, w);
	}

	private static void Checker(SpriteBatch sb, Rectangle stage)
	{
		const int check = 10;
		for (int cy = 0; cy < stage.Height; cy += check)
		{
			for (int cx = 0; cx < stage.Width; cx += check)
			{
				bool dark = ((cx / check) + (cy / check)) % 2 == 0;
				HubKit.Fill(sb, new Rectangle(stage.X + cx, stage.Y + cy, Math.Min(check, stage.Width - cx), Math.Min(check, stage.Height - cy)), dark ? new Color(40, 44, 70) : new Color(50, 55, 86));
			}
		}
	}

	/// <summary>The before/after pictures as textures, remade when a new result arrives.</summary>
	private static void Textures(PhotoResult last)
	{
		if (_textureVersion == last.Version)
		{
			return;
		}
		_textureVersion = last.Version;
		_before?.Dispose();
		_after?.Dispose();
		_before = Make(last.Before, last.PreviewWidth, last.PreviewHeight);
		_after = Make(last.After, last.PreviewWidth, last.PreviewHeight);
	}

	private static Texture2D Make(Color[] pixels, int width, int height)
	{
		try
		{
			Texture2D texture = new Texture2D(Main.instance.GraphicsDevice, width, height);
			texture.SetData(pixels);
			return texture;
		}
		catch
		{
			return null;
		}
	}

	private static void Sharp(SpriteBatch sb)
	{
		sb.End();
		sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp, DepthStencilState.None, HubInput.Clipping ? new RasterizerState
		{
			CullMode = CullMode.None,
			ScissorTestEnable = true
		} : RasterizerState.CullCounterClockwise, null, HubInput.Matrix);
	}

	#endregion
}
