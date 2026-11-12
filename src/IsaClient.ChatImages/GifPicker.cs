using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IsaClient.Core;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;

namespace IsaClient.ChatImages;

/// <summary>
/// The GIF search (the GIF button on the chat bar, .gif): "GIFs" shows what is trending on Klipy and searches it; "Anime"
/// has nekos.best's reactions by category (hug, pat, kiss...) and its search. Moving GIF thumbnails in a scrolling grid
/// that loads more at the bottom. A click attaches the GIF above the chat bar (Shift+click keeps the window open), and it
/// goes with your next message like a picture. One of the music player's floating surfaces: centered, dragged by its
/// top bar.
/// </summary>
public static class GifPicker
{
	private const string Layer = "gifs";

	private const int Width = 620;

	private const int Height = 520;

	private sealed class Thumb
	{
		public ImageFrames Frames;

		public bool Loading;

		public bool Failed;
	}

	public static bool Visible { get; private set; }

	private static bool _registered;

	private static Rectangle _screenRect;

	private static Vector2? _position;

	private static bool _dragging;

	private static Vector2 _dragOffset;

	/// <summary>0 GIFs (Klipy), 1 Anime (nekos.best).</summary>
	private static int _tab;

	private static string _query = "";

	private static string _shownQuery;

	private static double _typedAt;

	private static string _category = "hug";

	private static readonly List<GifItem> _items = new List<GifItem>();

	private static readonly Dictionary<string, Thumb> _thumbs = new Dictionary<string, Thumb>();

	private static int _page = 1;

	private static bool _more;

	private static bool _loading;

	private static string _error;

	/// <summary>Bumped on every new list: answers for an older one are dropped.</summary>
	private static int _generation;

	private static string _note;

	private static double _noteUntil;

	/// <summary>The chat was open (with this text) when the window opened: it opens again when a GIF is picked.</summary>
	private static string _chatText;

	private static bool _hadChat;

	private static RasterizerState _scissor;

	public static void Open(string query = null)
	{
		if (!_registered)
		{
			_registered = true;
			MusicPlayerWindow.AddSurface(new MusicPlayerWindow.FloatingSurface
			{
				Layer = Layer,
				Shown = () => Visible,
				ScreenRect = () => _screenRect,
				Draw = Draw
			});
		}
		// The search field takes the keyboard: the chat closes meanwhile (its text is kept).
		_hadChat = Main.drawingPlayerChat;
		_chatText = Main.chatText;
		if (_hadChat)
		{
			Main.ClosePlayerChat();
		}
		Visible = true;
		MusicPlayerWindow.BringSurfaceToFront(Layer);
		MusicPlayerWindow.FocusedField = "gifs:search";
		if (!string.IsNullOrWhiteSpace(query))
		{
			_query = query.Trim();
			_tab = 0;
			Reload();
		}
		else if (_items.Count == 0)
		{
			Reload();
		}
		SoundEngine.PlaySound(10);
	}

	public static void Close()
	{
		if (!Visible)
		{
			return;
		}
		Visible = false;
		_screenRect = Rectangle.Empty;
		_dragging = false;
		if (MusicPlayerWindow.FocusedField == "gifs:search")
		{
			MusicPlayerWindow.FocusedField = null;
		}
		ClearThumbs();
		_items.Clear();
		_shownQuery = null;
		SoundEngine.PlaySound(11);
		ReopenChat();
	}

	public static void Toggle()
	{
		if (Visible)
		{
			Close();
		}
		else
		{
			Open();
		}
	}

	private static void ReopenChat()
	{
		if (!_hadChat || Main.gameMenu)
		{
			return;
		}
		_hadChat = false;
		string text = _chatText;
		Main.QueueMainThreadAction(delegate
		{
			if (!Main.drawingPlayerChat)
			{
				Main.OpenPlayerChat();
				Main.chatText = text ?? "";
			}
		});
	}

	private static void Note(string text)
	{
		_note = IsaClient.Languages.L10n.T(text);
		_noteUntil = Main.timeForVisualEffects + 150.0;
	}

	#region Loading

	/// <summary>A new list: what the tab and the search say.</summary>
	private static void Reload()
	{
		_generation++;
		ClearThumbs();
		_items.Clear();
		_page = 1;
		_more = false;
		_error = null;
		_shownQuery = (_query ?? "").Trim();
		MusicPlayerWindow.ScrollOffsets["gifs:grid"] = 0f;
		Fetch();
	}

	private static void Fetch()
	{
		if (_loading)
		{
			return;
		}
		_loading = true;
		int generation = _generation;
		int tab = _tab;
		string query = _shownQuery;
		string category = _category;
		int page = _page;
		Task.Run(delegate
		{
			List<GifItem> found = null;
			bool more = false;
			string error = null;
			try
			{
				if (tab == 0)
				{
					found = (query.Length > 0) ? GifSources.KlipySearch(query, page, out more) : GifSources.KlipyTrending(page, out more);
				}
				else if (tab == 1)
				{
					found = (query.Length > 0) ? GifSources.GiphySearch(query, page, out more) : GifSources.GiphyTrending(page, out more);
				}
				else
				{
					found = (query.Length > 0) ? GifSources.AnimeSearch(query, 20) : GifSources.Anime(category, 20);
					// nekos.best gives random ones: there are always more.
					more = found.Count > 0;
				}
			}
			catch (Exception ex)
			{
				error = ex.Message;
			}
			Main.QueueMainThreadAction(delegate
			{
				_loading = false;
				if (generation != _generation)
				{
					return;
				}
				if (found == null)
				{
					_error = error;
					return;
				}
				foreach (GifItem item in found)
				{
					if (!_items.Any(i => i.Url == item.Url))
					{
						_items.Add(item);
					}
				}
				_more = more && _items.Count < 400;
				_page++;
			});
		});
	}

	private static Thumb ThumbOf(GifItem item)
	{
		if (_thumbs.TryGetValue(item.Preview, out Thumb thumb))
		{
			return thumb;
		}
		thumb = new Thumb { Loading = true };
		_thumbs[item.Preview] = thumb;
		string url = item.Preview;
		int generation = _generation;
		ChatPictures.Run(delegate
		{
			ImageFrames frames = null;
			try
			{
				if (generation == _generation && Visible)
				{
					byte[] data = ImgurClient.Download(url, 12 * 1024 * 1024, () => generation != _generation || !Visible);
					frames = ImageFrames.Open(data, 220, 220, ChatImages.Animate);
				}
			}
			catch
			{
			}
			Main.QueueMainThreadAction(delegate
			{
				thumb.Loading = false;
				if (generation != _generation || !Visible || !_thumbs.ContainsValue(thumb))
				{
					frames?.Dispose();
					return;
				}
				thumb.Frames = frames;
				thumb.Failed = frames == null;
			});
		});
		return thumb;
	}

	private static void ClearThumbs()
	{
		foreach (Thumb thumb in _thumbs.Values)
		{
			thumb.Frames?.Dispose();
		}
		_thumbs.Clear();
	}

	#endregion

	#region Drawing

	private static void Draw(SpriteBatch sb, int screenW, int screenH)
	{
		if (Main.gameMenu)
		{
			Close();
			return;
		}
		int w = Math.Min(Width, screenW - 20);
		int h = Math.Min(Height, screenH - 20);
		Vector2 at = _position ?? new Vector2((screenW - w) / 2f, (screenH - h) / 2f);
		at.X = MathHelper.Clamp(at.X, 0f, Math.Max(0, screenW - w));
		at.Y = MathHelper.Clamp(at.Y, 0f, Math.Max(0, screenH - h));
		Rectangle r = new Rectangle((int)at.X, (int)at.Y, w, h);
		_screenRect = MusicPlayerWindow.ToScreen(r);
		if (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape) && !Main.oldKeyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape))
		{
			Close();
			return;
		}
		MusicDrawing.Box(sb, r, MusicPlayerWindow.BackColor * 0.98f);
		// Header: title, the two tabs, close.
		for (int i = 0; i < 3; i++)
		{
			MusicDrawing.Fill(sb, new Rectangle(r.Center.X - 12 + i * 10, r.Y + 4, 6, 2), MusicPlayerWindow.MutedColor * 0.45f);
		}
		Utils.DrawBorderStringBig(sb, "GIF", new Vector2(r.X + 16, r.Y + 8), MusicPlayerWindow.AccentColor, 0.55f);
		string[] tabs = { "GIFs", "Giphy", "Anime" };
		string[] tabTips = { "Any GIF: trending and search (Klipy)", "Any GIF: trending and search (GIPHY)", "Anime reactions by kind: hug, pat, kiss... (nekos.best)" };
		for (int i = 0; i < tabs.Length; i++)
		{
			Rectangle tab = new Rectangle(r.X + 84 + i * 96, r.Y + 12, 90, 26);
			if (MusicDrawing.TextButton(sb, "gifs:tab:" + i, tab, tabs[i], (_tab == i) ? MusicPlayerWindow.SelectedColor : MusicPlayerWindow.PanelColor, tabTips[i]) && _tab != i)
			{
				_tab = i;
				Reload();
			}
		}
		if (MusicDrawing.BareIcon(sb, "gifs:close", new Rectangle(r.Right - 30, r.Y + 13, 17, 18), MusicDrawing.Tex("Images/MapDeath"), null, "Close (Esc)", Color.White))
		{
			Close();
			return;
		}
		// Search: it runs a moment after you stop typing (or on Enter).
		Rectangle field = new Rectangle(r.X + 14, r.Y + 48, r.Width - 28, 30);
		string placeholder = (_tab == 2) ? "Search anime GIFs (or pick a kind below)..." : "Search GIFs...";
		string typed = MusicDrawing.Field(sb, "gifs:search", field, _query, placeholder, 60, true);
		if (typed != _query)
		{
			_query = typed;
			_typedAt = Main.timeForVisualEffects;
		}
		bool enter = MusicPlayerWindow.FocusedField == "gifs:search" && Main.inputTextEnter;
		if ((enter || Main.timeForVisualEffects - _typedAt > 35.0) && (_query ?? "").Trim() != _shownQuery)
		{
			Reload();
		}
		Rectangle area = new Rectangle(r.X + 10, field.Bottom + 8, r.Width - 20, r.Bottom - field.Bottom - 8 - 30);
		DrawGrid(sb, area);
		// Footer: who the GIFs are from, and what a click does.
		string credit = (_tab == 0) ? "Powered by KLIPY" : ((_tab == 1) ? "Powered by GIPHY" : "GIFs from nekos.best");
		MusicDrawing.Text(sb, credit, r.X + 14, r.Bottom - 24, MusicPlayerWindow.MutedColor, 0.62f);
		MusicDrawing.Text(sb, "Click: attach to your message · Shift+click: attach and keep looking", r.X + 150, r.Bottom - 24, MusicPlayerWindow.MutedColor * 0.85f, 0.62f, r.Width - 164);
		if (_note != null && Main.timeForVisualEffects < _noteUntil)
		{
			Vector2 size = FontAssets.MouseText.Value.MeasureString(_note) * 0.8f;
			Rectangle pill = new Rectangle(r.Center.X - (int)size.X / 2 - 14, r.Bottom + 8, (int)size.X + 28, 28);
			MusicDrawing.Box(sb, pill, MusicPlayerWindow.PanelColor * 0.98f);
			Utils.DrawBorderString(sb, _note, new Vector2(pill.X + 14, pill.Y + 6), Color.White, 0.8f);
		}
		// Dragging by the top bar.
		Rectangle handle = new Rectangle(r.X, r.Y, r.Width - 40, 44);
		if (_dragging)
		{
			if (!MusicPlayerWindow.MouseDown)
			{
				_dragging = false;
			}
			else
			{
				Vector2 mouse = MusicPlayerWindow.MousePoint.ToVector2();
				_position = mouse - _dragOffset;
			}
		}
		else if (MusicPlayerWindow.Clicked && MusicPlayerWindow.MouseIn(handle) && !MusicPlayerWindow.MouseIn(new Rectangle(r.X + 84, r.Y + 12, 282, 26)))
		{
			_dragging = true;
			_dragOffset = MusicPlayerWindow.MousePoint.ToVector2() - new Vector2(r.X, r.Y);
			MusicPlayerWindow.StartSurfaceDrag();
		}
		if (MusicPlayerWindow.Clicked && MusicPlayerWindow.MouseIn(r))
		{
			MusicPlayerWindow.Clicked = false;
		}
	}

	private static void DrawGrid(SpriteBatch sb, Rectangle area)
	{
		MusicDrawing.Box(sb, area, MusicPlayerWindow.PanelDarkColor * 0.95f);
		Rectangle inner = new Rectangle(area.X + 6, area.Y + 6, area.Width - 12, area.Height - 12);
		float scroll = MusicDrawing.BeginScroll("gifs:grid", inner);
		Clip(sb, inner);
		int y = inner.Y - (int)scroll;
		int width = inner.Width - 10;
		// Anime: the kinds of reaction, as chips.
		if (_tab == 2 && _shownQuery.Length == 0)
		{
			int cx = inner.X;
			foreach (string category in GifSources.AnimeCategories)
			{
				float textW = MusicDrawing.Measure(category, 0.66f);
				int chipW = (int)textW + 16;
				if (cx + chipW > inner.X + width)
				{
					cx = inner.X;
					y += 24;
				}
				Rectangle chip = new Rectangle(cx, y, chipW, 20);
				if (MusicPlayerWindow.Visible(chip) && MusicDrawing.TextButton(sb, "gifs:cat:" + category, chip, category, (_category == category) ? MusicPlayerWindow.SelectedColor : MusicPlayerWindow.PanelColor, null) && _category != category)
				{
					_category = category;
					Reload();
				}
				cx += chipW + 4;
			}
			y += 30;
		}
		// The grid
		int columns = 4;
		int gap = 6;
		int cell = (width - gap * (columns - 1)) / columns;
		int cellH = (int)(cell * 0.75f);
		for (int i = 0; i < _items.Count; i++)
		{
			Rectangle r = new Rectangle(inner.X + i % columns * (cell + gap), y + i / columns * (cellH + gap), cell, cellH);
			if (!MusicPlayerWindow.Visible(r))
			{
				continue;
			}
			DrawCell(sb, _items[i], r);
		}
		int rows = (_items.Count + columns - 1) / columns;
		y += rows * (cellH + gap);
		if (_loading)
		{
			MusicDrawing.Text(sb, "Loading...", inner.X + width / 2 - 30, y + 6, MusicPlayerWindow.MutedColor, 0.75f);
			y += 30;
		}
		else if (_error != null)
		{
			MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T("Couldn't load GIFs: ") + _error, inner.X + 4, y + 6, new Color(255, 140, 140), 0.72f, width - 8);
			y += 30;
		}
		else if (_items.Count == 0)
		{
			MusicDrawing.Text(sb, "No GIFs found.", inner.X + 4, y + 6, MusicPlayerWindow.MutedColor, 0.75f);
			y += 30;
		}
		float height = y + scroll - inner.Y;
		Clip(sb, null);
		MusicDrawing.EndScroll(sb, "gifs:grid", inner, height);
		// Near the bottom: the next page.
		if (_more && !_loading && _error == null && scroll + inner.Height > height - cellH * 2)
		{
			Fetch();
		}
	}

	private static void DrawCell(SpriteBatch sb, GifItem item, Rectangle r)
	{
		bool hover = MusicPlayerWindow.MouseIn(r);
		MusicDrawing.Box(sb, r, hover ? MusicPlayerWindow.RowHoverColor : MusicPlayerWindow.PanelColor);
		Thumb thumb = ThumbOf(item);
		Texture2D texture = thumb.Frames?.Update(ChatImages.Animate);
		Rectangle inner = new Rectangle(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6);
		if (texture != null)
		{
			sb.Draw(texture, ChatImageLayout.Fit(texture.Width, texture.Height, inner), Color.White);
		}
		else if (thumb.Failed)
		{
			MusicDrawing.Text(sb, "!", inner.Center.X - 3, inner.Center.Y - 10, new Color(255, 140, 140), 0.9f);
		}
		else
		{
			// Loading: a soft pulse.
			float pulse = 0.25f + 0.15f * (float)Math.Sin(Main.timeForVisualEffects / 10.0);
			MusicDrawing.Fill(sb, inner, Color.White * pulse * 0.3f);
		}
		if (!hover)
		{
			return;
		}
		MusicDrawing.Fill(sb, new Rectangle(r.X, r.Y, r.Width, 2), MusicPlayerWindow.AccentColor);
		MusicDrawing.Fill(sb, new Rectangle(r.X, r.Bottom - 2, r.Width, 2), MusicPlayerWindow.AccentColor);
		MusicDrawing.Hover("gifs:item:" + item.Url, string.IsNullOrWhiteSpace(item.Title) ? null : item.Title);
		if (MusicPlayerWindow.Clicked)
		{
			MusicPlayerWindow.Clicked = false;
			Pick(item);
		}
	}

	private static void Pick(GifItem item)
	{
		if (!ChatAttachments.AddLink(item))
		{
			return;
		}
		SoundEngine.PlaySound(12);
		bool keep = Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift) || Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightShift);
		if (keep)
		{
			Note("Attached: it goes with your next message.");
			return;
		}
		// The chat opens with the GIF attached: write something (or not) and press Enter.
		_hadChat = true;
		Close();
	}

	/// <summary>Draws inside <paramref name="clip"/> only (null: the whole screen again).</summary>
	private static void Clip(SpriteBatch sb, Rectangle? clip)
	{
		sb.End();
		RasterizerState rasterizer = RasterizerState.CullCounterClockwise;
		if (clip.HasValue)
		{
			Rectangle scissor = Rectangle.Intersect(MusicPlayerWindow.ToScreen(clip.Value), sb.GraphicsDevice.Viewport.Bounds);
			sb.GraphicsDevice.ScissorRectangle = (scissor.Width > 0 && scissor.Height > 0) ? scissor : new Rectangle(0, 0, 1, 1);
			rasterizer = _scissor ?? (_scissor = new RasterizerState { CullMode = CullMode.None, ScissorTestEnable = true });
			MusicPlayerWindow.ClipRect = clip.Value;
			MusicPlayerWindow.Clipping = true;
		}
		else
		{
			sb.GraphicsDevice.ScissorRectangle = sb.GraphicsDevice.Viewport.Bounds;
			MusicPlayerWindow.Clipping = false;
		}
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, rasterizer, null, Main.UIScaleMatrix);
	}

	#endregion

	/// <summary>.gif [search]: opens the GIF search.</summary>
	internal static void GifCommand(CommandContext command)
	{
		string rest = command.Rest.Trim();
		// The chat closes after a command: it opens again (empty) only when a GIF is picked.
		Open(rest);
		_hadChat = false;
		_chatText = "";
	}
}
