using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Community;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.UI.Chat;

namespace IsaClient.ChatImages;

/// <summary>
/// Picture links in a chat message become a row of framed thumbnails under its text. The chat draws messages a line at
/// a time (22 px each, and only the lines that fit), so the row takes a few empty lines, each holding a GallerySlice:
/// slices only say they were drawn, and at the end of the chat (EndFrame) each gallery is drawn once, cut to the lines
/// that showed. Clicking a thumbnail opens the viewer.
/// </summary>
internal static class ChatImageLayout
{
	private const int Gap = 8;

	private const int Border = 3;

	private static readonly List<Gallery> _drawn = new List<Gallery>();

	private static int _version;

	private static int _settings = -1;

	private static uint _checkedAt = uint.MaxValue;

	/// <summary>Every message is laid out again (something they show arrived, e.g. a linked IsaClient item).</summary>
	public static void Relayout()
	{
		_version++;
	}

	/// <summary>Changes when a setting that shapes the chat changes: every message is laid out again.</summary>
	public static int Version
	{
		get
		{
			// Every message asks every tick: the settings are looked at once a tick.
			if (_checkedAt != Main.GameUpdateCount)
			{
				_checkedAt = Main.GameUpdateCount;
				int settings = (ChatImages.ShowsThumbnails ? 100 : 0) + ChatImages.ThumbLines;
				if (settings != _settings)
				{
					_settings = settings;
					_version++;
				}
			}
			return _version;
		}
	}

	private static readonly Color FrameOuter = new Color(14, 18, 38);

	private static readonly Color FrameColor = new Color(73, 94, 171);

	private static readonly Color FrameLight = new Color(122, 146, 222);

	private static readonly Color FrameHover = new Color(255, 214, 110);

	/// <summary>Called by ChatMessageContainer after it wraps and links a message: picture links become a gallery.</summary>
	public static void Apply(List<List<TextSnippet>> lines)
	{
		if (!ChatImages.ShowsThumbnails)
		{
			return;
		}
		List<string> urls = null;
		foreach (List<TextSnippet> line in lines)
		{
			for (int i = 0; i < line.Count; i++)
			{
				if (line[i] is ChatLinks.LinkSnippet link && link.ShowsAddress && ChatImages.IsImageLink(link.Url))
				{
					urls = urls ?? new List<string>();
					if (!urls.Contains(link.Url, StringComparer.OrdinalIgnoreCase))
					{
						urls.Add(link.Url);
					}
					line.RemoveAt(i);
					i--;
				}
			}
		}
		if (urls == null)
		{
			return;
		}
		// Lines left with nothing but spaces go (a message of only pictures keeps the name line).
		lines.RemoveAll(line => line.All(s => string.IsNullOrWhiteSpace(s.Text) && !(s is GallerySlice)));
		Gallery gallery = new Gallery(urls.Take(ChatImages.MaxPerMessage).Select(ChatPictures.Get).ToList(), Math.Max(2, Math.Min(8, ChatImages.ThumbLines)));
		for (int band = 0; band < gallery.Rows; band++)
		{
			lines.Add(new List<TextSnippet> { new GallerySlice(gallery, band) });
		}
	}

	/// <summary>RemadeChatMonitor, after drawing the chat lines: the galleries whose lines showed.</summary>
	public static void EndFrame(SpriteBatch sb)
	{
		foreach (Gallery gallery in _drawn)
		{
			gallery.Draw(sb);
		}
		_drawn.Clear();
	}

	internal static void Drawn(Gallery gallery)
	{
		if (!_drawn.Contains(gallery))
		{
			_drawn.Add(gallery);
		}
	}

	#region Gallery

	/// <summary>The thumbnails of one message.</summary>
	internal sealed class Gallery
	{
		public readonly List<ChatPicture> Pictures;

		public readonly int Rows;

		/// <summary>Screen position of the first line, and which of its lines showed this frame.</summary>
		public Vector2 Origin;

		public int FirstBand = int.MaxValue;

		public int LastBand = -1;

		public int Hovered = -1;

		private uint _hoverFrame;

		private readonly List<Rectangle> _tiles = new List<Rectangle>();

		/// <summary>Pictures that did not fit in the width (a "+N" on the last one).</summary>
		private int _hidden;

		private float _hoverGlow;

		public Gallery(List<ChatPicture> pictures, int rows)
		{
			Pictures = pictures;
			Rows = rows;
		}

		public int TileHeight => Rows * ChatImages.ChatLineHeight - 6;

		/// <summary>Tile rectangles from the gallery's corner, for the pictures' current shapes.</summary>
		public int Layout()
		{
			_tiles.Clear();
			int h = TileHeight;
			int x = 0;
			int maxWidth = Math.Max(h * 2, Main.ChatLineWidthLimit);
			_hidden = 0;
			for (int i = 0; i < Pictures.Count; i++)
			{
				int w = (int)MathHelper.Clamp(Pictures[i].Aspect * (h - Border * 2) + Border * 2, h * 0.55f, h * 2.4f);
				if (i > 0 && x + w > maxWidth)
				{
					_hidden = Pictures.Count - i;
					break;
				}
				_tiles.Add(new Rectangle(x, 3, w, h));
				x += w + Gap;
			}
			return Math.Max(0, x - Gap);
		}

		public Rectangle TileOnScreen(int index)
		{
			Rectangle tile = _tiles[index];
			return new Rectangle((int)Origin.X + tile.X, (int)Origin.Y + tile.Y, tile.Width, tile.Height);
		}

		public int TileAt(int x, int y)
		{
			for (int i = 0; i < _tiles.Count; i++)
			{
				if (TileOnScreen(i).Contains(x, y))
				{
					return i;
				}
			}
			return -1;
		}

		public void Hover()
		{
			int index = TileAt(Main.mouseX, Main.mouseY);
			Hovered = index;
			_hoverFrame = Main.GameUpdateCount;
			if (index < 0)
			{
				return;
			}
			ChatPicture picture = Pictures[index];
			string tip;
			switch (picture.State)
			{
			case PictureState.Ready:
				tip = "Click to view" + ((index == _tiles.Count - 1 && _hidden > 0) ? (" (" + Pictures.Count + " pictures)") : "");
				break;
			case PictureState.Failed:
				tip = "Couldn't load the picture: " + picture.Error + "\nClick to try again";
				break;
			case PictureState.Loading:
				tip = "Loading the picture...";
				break;
			default:
				tip = "Click to load the picture";
				break;
			}
			Main.instance.MouseText(IsaClient.Languages.L10n.T(tip), 0, 0);
		}

		public void Click()
		{
			int index = TileAt(Main.mouseX, Main.mouseY);
			if (index < 0)
			{
				return;
			}
			Main.mouseLeftRelease = false;
			ChatPicture picture = Pictures[index];
			if (picture.State == PictureState.Ready)
			{
				ImageViewer.Open(Pictures, index, TileOnScreen(index));
			}
			else
			{
				SoundEngine.PlaySound(12);
				picture.Retry();
			}
		}

		public void Draw(SpriteBatch sb)
		{
			Layout();
			int lineH = ChatImages.ChatLineHeight;
			Rectangle clip = new Rectangle((int)Origin.X - 2, (int)Origin.Y + FirstBand * lineH, 4000, (LastBand - FirstBand + 1) * lineH);
			bool hovering = Main.GameUpdateCount - _hoverFrame <= 1;
			if (!hovering)
			{
				Hovered = -1;
			}
			_hoverGlow = MathHelper.Clamp(_hoverGlow + (hovering && Hovered >= 0 ? 0.2f : -0.12f), 0f, 1f);
			int pixelHeight = (int)Math.Round((TileHeight - Border * 2) * Main.UIScale);
			for (int i = 0; i < _tiles.Count; i++)
			{
				DrawTile(sb, i, TileOnScreen(i), clip, pixelHeight);
			}
			FirstBand = int.MaxValue;
			LastBand = -1;
		}

		private void DrawTile(SpriteBatch sb, int index, Rectangle r, Rectangle clip, int pixelHeight)
		{
			ChatPicture picture = Pictures[index];
			bool hover = index == Hovered;
			float glow = hover ? _hoverGlow : 0f;
			Rectangle inner = new Rectangle(r.X + Border, r.Y + Border, r.Width - Border * 2, r.Height - Border * 2);
			// Soft shadow, then the frame (rounded by its corner pixels), then the picture's backdrop.
			Rounded(sb, new Rectangle(r.X + 3, r.Y + 3, r.Width, r.Height), clip, Color.Black * 0.35f);
			Rounded(sb, r, clip, Color.Lerp(FrameOuter, FrameHover * 0.6f, glow * 0.5f));
			Rectangle band = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
			Rounded(sb, band, clip, Color.Lerp(FrameColor, FrameHover, glow));
			Fill(sb, new Rectangle(band.X + 1, band.Y + 1, band.Width - 2, 1), clip, Color.Lerp(FrameLight, Color.White, glow) * 0.8f);
			Fill(sb, inner, clip, new Color(8, 10, 20));
			Texture2D texture = picture.Thumbnail(pixelHeight, (int)Math.Round(inner.Width * Main.UIScale));
			if (texture != null)
			{
				Rectangle fit = Fit(texture.Width, texture.Height, inner);
				DrawClipped(sb, texture, fit, clip, Color.Lerp(Color.White * 0.96f, Color.White, glow));
				if (picture.Animated && ChatImages.Animate)
				{
					Badge(sb, "GIF", new Vector2(inner.X + 4, inner.Bottom - 17), clip);
				}
			}
			else
			{
				DrawStatus(sb, picture, inner, clip);
			}
			if (index == _tiles.Count - 1 && _hidden > 0)
			{
				Fill(sb, inner, clip, Color.Black * 0.55f);
				Centered(sb, "+" + _hidden, inner, clip, Color.White, 1.1f);
			}
		}

		private static void DrawStatus(SpriteBatch sb, ChatPicture picture, Rectangle inner, Rectangle clip)
		{
			switch (picture.State)
			{
			case PictureState.Failed:
				Centered(sb, "Can't show", inner, clip, new Color(255, 140, 140), 0.7f);
				break;
			case PictureState.Idle:
				Centered(sb, "Click to load", inner, clip, new Color(200, 210, 235), 0.7f);
				break;
			default:
			{
				// Three dots that rise in turn.
				for (int d = 0; d < 3; d++)
				{
					float wave = (float)Math.Sin(Main.timeForVisualEffects * 0.15 - d * 0.9);
					int size = 6;
					Rectangle dot = new Rectangle(inner.Center.X - 15 + d * 12, inner.Center.Y - 3 - (int)(Math.Max(0f, wave) * 5f), size, size);
					Fill(sb, dot, clip, Color.Lerp(FrameColor, Color.White, 0.4f + 0.6f * Math.Max(0f, wave)));
				}
				break;
			}
			}
		}
	}

	/// <summary>One chat line of a gallery: as wide as the gallery, it draws nothing itself (see EndFrame).</summary>
	internal sealed class GallerySlice : TextSnippet
	{
		private readonly Gallery _gallery;

		private readonly int _band;

		public GallerySlice(Gallery gallery, int band)
			: base(" ")
		{
			_gallery = gallery;
			_band = band;
			CheckForHover = true;
		}

		public override bool UniqueDraw(bool justCheckingSize, out Vector2 size, SpriteBatch spriteBatch, Vector2 position = default(Vector2), Color color = default(Color), float scale = 1f)
		{
			size = new Vector2(Math.Max(1, _gallery.Layout()), ChatImages.ChatLineHeight);
			// The shadow passes of the chat text come in black: only the real pass counts.
			if (justCheckingSize || (color.R < 20 && color.G < 20 && color.B < 20))
			{
				return true;
			}
			_gallery.Origin = new Vector2(position.X, position.Y - _band * ChatImages.ChatLineHeight);
			_gallery.FirstBand = Math.Min(_gallery.FirstBand, _band);
			_gallery.LastBand = Math.Max(_gallery.LastBand, _band);
			Drawn(_gallery);
			return true;
		}

		public override void OnHover()
		{
			_gallery.Hover();
		}

		public override void OnClick()
		{
			_gallery.Click();
		}
	}

	#endregion

	#region Drawing cut to a rectangle

	internal static Rectangle Fit(int width, int height, Rectangle area)
	{
		float scale = Math.Min((float)area.Width / width, (float)area.Height / height);
		int w = Math.Max(1, (int)Math.Round(width * scale));
		int h = Math.Max(1, (int)Math.Round(height * scale));
		return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
	}

	private static void Fill(SpriteBatch sb, Rectangle r, Rectangle clip, Color color)
	{
		Rectangle cut = Rectangle.Intersect(r, clip);
		if (cut.Width > 0 && cut.Height > 0)
		{
			sb.Draw(TextureAssets.MagicPixel.Value, cut, color);
		}
	}

	/// <summary>A rectangle with its four corner pixels left out.</summary>
	private static void Rounded(SpriteBatch sb, Rectangle r, Rectangle clip, Color color)
	{
		Fill(sb, new Rectangle(r.X + 1, r.Y, r.Width - 2, r.Height), clip, color);
		Fill(sb, new Rectangle(r.X, r.Y + 1, 1, r.Height - 2), clip, color);
		Fill(sb, new Rectangle(r.Right - 1, r.Y + 1, 1, r.Height - 2), clip, color);
	}

	private static void DrawClipped(SpriteBatch sb, Texture2D texture, Rectangle destination, Rectangle clip, Color color)
	{
		Rectangle cut = Rectangle.Intersect(destination, clip);
		if (cut.Width <= 0 || cut.Height <= 0)
		{
			return;
		}
		float sx = (float)texture.Width / destination.Width;
		float sy = (float)texture.Height / destination.Height;
		Rectangle source = new Rectangle((int)Math.Round((cut.X - destination.X) * sx), (int)Math.Round((cut.Y - destination.Y) * sy), Math.Max(1, (int)Math.Round(cut.Width * sx)), Math.Max(1, (int)Math.Round(cut.Height * sy)));
		sb.Draw(texture, cut, source, color);
	}

	/// <summary>Text only when all of it is inside <paramref name="clip"/> (half a word would look broken).</summary>
	private static void Centered(SpriteBatch sb, string text, Rectangle area, Rectangle clip, Color color, float scale)
	{
		Vector2 size = FontAssets.MouseText.Value.MeasureString(IsaClient.Languages.L10n.T(text)) * scale;
		Vector2 at = new Vector2(area.Center.X - size.X / 2f, area.Center.Y - size.Y / 2f + 2f * scale);
		if (clip.Contains(new Rectangle((int)at.X, (int)at.Y, (int)size.X, (int)(size.Y * 0.8f))))
		{
			Utils.DrawBorderString(sb, IsaClient.Languages.L10n.T(text), at, color, scale);
		}
	}

	private static void Badge(SpriteBatch sb, string text, Vector2 at, Rectangle clip)
	{
		float scale = 0.62f;
		Vector2 size = FontAssets.MouseText.Value.MeasureString(IsaClient.Languages.L10n.T(text)) * scale;
		Rectangle pill = new Rectangle((int)at.X, (int)at.Y, (int)size.X + 8, 14);
		if (!clip.Contains(pill))
		{
			return;
		}
		Rounded(sb, pill, clip, Color.Black * 0.65f);
		Utils.DrawBorderString(sb, IsaClient.Languages.L10n.T(text), new Vector2(pill.X + 4, pill.Y + 1), Color.White, scale);
	}

	#endregion
}
