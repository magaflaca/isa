using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IsaClient.Core;
using IsaClient.Games;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace IsaClient.Gba;

/// <summary>
/// The Games page's "Find games": free Game Boy Advance and Nintendo DS games made by the community (Homebrew Hub,
/// Universal-DB, itch.io, Game Jolt and archive.org, see GbaFinder) as cards with their pictures and a GBA / DS tag, a
/// search box (also looked up on Game Jolt and archive.org once you stop typing), the console and the sources to pick
/// from. Games for adults show only with "18+" on. Get downloads the game and puts it in the
/// library; Play starts it. Each card's page opens in the browser.
/// </summary>
public static partial class PokemonWindow
{
	/// <summary>Show games for adults too.</summary>
	[Setting("GbaFinderAdult")]
	public static bool FinderShowsAdult;

	private static string _findQuery = "";

	/// <summary>When the search words last changed (Game Jolt is asked once they stay put for a moment).</summary>
	private static double _findTypedAt;

	private static bool _findResultsOnline;

	private static bool _findResultsAdult;

	private static int _findResultsVersion;

	private static FinderSource? _findSource;

	/// <summary>Only DS games (true), only GBA ones (false), or both.</summary>
	private static bool? _findSystem;

	private static bool? _findResultsSystem;

	/// <summary>Card pictures made into textures this frame (a few per frame, so a page full of them doesn't stutter).</summary>
	private static int _findUploads;

	private const int FindUploadsPerFrame = 3;

	private static float _findScroll;

	private static List<FoundGame> _findResults;

	private static List<FoundGame> _findResultsOf;

	private static string _findResultsQuery;

	private static FinderSource? _findResultsSource;

	/// <summary>Card pictures made into textures (null: it has none), the most recently used last.</summary>
	private static readonly Dictionary<string, Texture2D> _findTextures = new Dictionary<string, Texture2D>();

	private static readonly LinkedList<string> _findTextureOrder = new LinkedList<string>();

	/// <summary>At most this many card pictures at once (the rest are made again from the disk cache when shown).</summary>
	private const int FindTextureLimit = 72;

	/// <summary>The finder's folders (the PSP's finder keeps its pictures there too).</summary>
	internal static void EnsureFinderFolders()
	{
		if (GbaFinder.Folder == null)
		{
			GbaFinder.Folder = Path.Combine(PokemonGame.Folder, "Finder");
			GbaFinder.RomFolder = Path.Combine(PokemonGame.Folder, "Roms", "Downloads");
		}
	}

	/// <summary>A new frame for <see cref="FoundPicture"/>: it can make a few more textures.</summary>
	internal static void NewPictureFrame() => _findUploads = 0;

	private static void OpenFinder()
	{
		EnsureFinderFolders();
		_page = Page.Find;
		if (GbaFinder.Games.Count == 0 || DateTime.UtcNow - GbaFinder.LoadedAt > TimeSpan.FromHours(24))
		{
			GbaFinder.Load(refresh: false);
		}
	}

	/// <summary>The Games page's card next to "Add games".</summary>
	private static void DrawFindCard(SpriteBatch sb, Rectangle card, Rectangle box)
	{
		bool hover = MusicPlayerWindow.MouseIn(card) && MusicPlayerWindow.MouseIn(box);
		MusicDrawing.Box(sb, card, hover ? new Color(52, 78, 120) : new Color(34, 52, 82));
		Color dash = new Color(120, 200, 255) * (hover ? 0.9f : 0.5f);
		for (int x = card.X + 6; x < card.Right - 6; x += 10)
		{
			MusicDrawing.Fill(sb, new Rectangle(x, card.Y + 4, 5, 2), dash);
			MusicDrawing.Fill(sb, new Rectangle(x, card.Bottom - 6, 5, 2), dash);
		}
		for (int y = card.Y + 6; y < card.Bottom - 6; y += 10)
		{
			MusicDrawing.Fill(sb, new Rectangle(card.X + 4, y, 2, 5), dash);
			MusicDrawing.Fill(sb, new Rectangle(card.Right - 6, y, 2, 5), dash);
		}
		MagnifyingGlass(sb, new Vector2(card.Center.X, card.Y + 68), 18, dash);
		Tetris.Centered(sb, IsaClient.Languages.L10n.T("Find games"), card, card.Y + 110, Color.White, 0.72f);
		Tetris.Centered(sb, IsaClient.Languages.L10n.T("Free GBA and DS games by the community"), card, card.Y + 132, MusicPlayerWindow.MutedColor, 0.56f);
		Tetris.Centered(sb, IsaClient.Languages.L10n.T("itch.io, Universal-DB, Game Jolt..."), card, card.Y + 150, MusicPlayerWindow.MutedColor * 0.8f, 0.5f);
		if (hover && MusicPlayerWindow.Clicked)
		{
			MusicPlayerWindow.Clicked = false;
			OpenFinder();
		}
	}

	/// <summary>A magnifying glass of little squares (a ring and a handle).</summary>
	internal static void MagnifyingGlass(SpriteBatch sb, Vector2 c, int radius, Color color)
	{
		Vector2 center = c + new Vector2(-radius * 0.3f, -radius * 0.3f);
		for (int i = 0; i < 40; i++)
		{
			float a = i / 40f * MathHelper.TwoPi;
			Vector2 p = center + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * radius;
			MusicDrawing.Fill(sb, new Rectangle((int)p.X - 2, (int)p.Y - 2, 5, 5), color);
		}
		for (int i = 0; i < 9; i++)
		{
			Vector2 p = center + new Vector2(1f, 1f) * (radius * 0.75f + i * 2.2f);
			MusicDrawing.Fill(sb, new Rectangle((int)p.X - 3, (int)p.Y - 3, 7, 7), color);
		}
	}

	private static void DrawFindPage(SpriteBatch sb, Rectangle area)
	{
		if (PageHeader(sb, area, "Find games", "Free Game Boy Advance and DS games made by the community, from Homebrew Hub, Universal-DB, itch.io, Game Jolt and archive.org."))
		{
			if (MusicPlayerWindow.FocusedField == "pkmn:find")
			{
				MusicPlayerWindow.FocusedField = null;
			}
			_page = Page.Games;
			return;
		}
		TakeFoundGames();
		_findUploads = 0;
		// The bar: search, sources, how many, refresh.
		int y = area.Y + 58;
		Rectangle field = new Rectangle(area.X + 12, y, Math.Min(260, area.Width / 4), 30);
		string query = MusicDrawing.Field(sb, "pkmn:find", field, _findQuery, "Search games...", 40, searchIcon: true);
		if (query != _findQuery)
		{
			_findQuery = query;
			_findTypedAt = GameArt.Now;
			_findScroll = 0f;
		}
		int x = field.Right + 10;
		x = SourceButton(sb, x, y, "All", null);
		x = SourceButton(sb, x, y, "Homebrew Hub", FinderSource.HomebrewHub);
		x = SourceButton(sb, x, y, "Universal-DB", FinderSource.UniversalDb);
		x = SourceButton(sb, x, y, "itch.io", FinderSource.Itch);
		x = SourceButton(sb, x, y, "Game Jolt", FinderSource.GameJolt);
		x = SourceButton(sb, x, y, "archive.org", FinderSource.Archive);
		// "18+": a check box with its label.
		Rectangle adult = new Rectangle(x + 4, y, 66, 30);
		bool adultClicked = MusicDrawing.TextButton(sb, "pkmn:find:adult", adult, "", FinderShowsAdult ? new Color(150, 46, 64) : MusicPlayerWindow.PanelColor, FinderShowsAdult ? "Games for adults are shown (click to hide them)" : "Show games for adults too");
		Rectangle tick = new Rectangle(adult.X + 8, adult.Y + 9, 12, 12);
		MusicDrawing.Fill(sb, tick, Color.Black * 0.5f);
		GameArt.Frame(sb, tick, Color.White * 0.8f, 1);
		if (FinderShowsAdult)
		{
			MusicDrawing.Fill(sb, new Rectangle(tick.X + 3, tick.Y + 3, 6, 6), Color.White);
		}
		MusicDrawing.RawText(sb, "18+", adult.X + 26, adult.Y + 6, Color.White, 0.72f);
		if (adultClicked)
		{
			FinderShowsAdult = !FinderShowsAdult;
			_findScroll = 0f;
			ClientSettings.Save();
		}
		x = adult.Right + 6;
		Rectangle refresh = new Rectangle(area.Right - 112, y, 100, 30);
		if (MusicDrawing.TextButton(sb, "pkmn:find:refresh", refresh, GbaFinder.Loading ? "Looking..." : "Refresh", MusicPlayerWindow.PanelColor, "Look at the sites again (the list is kept for a day)", !GbaFinder.Loading))
		{
			GbaFinder.Load(refresh: true);
		}
		// second row: the console, and how many
		int y2 = y + 38;
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T("Console"), area.X + 14, y2 + 6, MusicPlayerWindow.MutedColor, 0.62f);
		int sx = area.X + 14 + (int)MusicDrawing.Measure(IsaClient.Languages.L10n.T("Console"), 0.62f) + 10;
		sx = SystemButton(sb, sx, y2, "Both", null, "GBA and DS games");
		sx = SystemButton(sb, sx, y2, "GBA", false, "Only Game Boy Advance games");
		sx = SystemButton(sb, sx, y2, "DS", true, "Only Nintendo DS games (the DS emulator is downloaded the first time you play one)");
		List<FoundGame> results = FindResults();
		string count = GbaFinder.Loading && GbaFinder.Games.Count == 0 ? "" : IsaClient.Languages.L10n.F("{0} games", results.Count);
		if (GbaFinder.Loading && GbaFinder.Games.Count > 0)
		{
			count = IsaClient.Languages.L10n.T("Updating the list...") + "  " + count;
		}
		if (GbaFinder.OnlineSearches > 0)
		{
			count = IsaClient.Languages.L10n.T("Searching Game Jolt and archive.org...") + "  " + count;
		}
		float countW = MusicDrawing.Measure(count, 0.62f);
		if (sx + countW + 12 < area.Right - 12)
		{
			MusicDrawing.RawText(sb, count, area.Right - 12 - countW, y2 + 6, MusicPlayerWindow.MutedColor, 0.62f);
		}
		Rectangle box = new Rectangle(area.X + 12, y2 + 36, area.Width - 24, area.Bottom - 30 - (y2 + 36));
		// The notice under the list.
		Tetris.Centered(sb, IsaClient.Languages.L10n.T("These games come from other sites: what you download, and what you do with it, is your own responsibility."), area, area.Bottom - 22, MusicPlayerWindow.MutedColor * 0.85f, 0.5f);
		if (results.Count == 0)
		{
			DrawFindEmpty(sb, box);
			return;
		}
		DrawFoundGrid(sb, box, results);
	}

	private static int SourceButton(SpriteBatch sb, int x, int y, string label, FinderSource? source)
	{
		int w = (int)MusicDrawing.Measure(IsaClient.Languages.L10n.T(label), 0.78f) + 26;
		bool on = _findSource == source;
		if (Choice(sb, "pkmn:find:src:" + label, new Rectangle(x, y, w, 30), label, on, null))
		{
			_findSource = source;
			_findScroll = 0f;
		}
		return x + w + 6;
	}

	private static int SystemButton(SpriteBatch sb, int x, int y, string label, bool? ds, string tip)
	{
		int w = (int)MusicDrawing.Measure(IsaClient.Languages.L10n.T(label), 0.78f) + 26;
		if (Choice(sb, "pkmn:find:sys:" + label, new Rectangle(x, y, w, 28), label, _findSystem == ds, tip))
		{
			_findSystem = ds;
			_findScroll = 0f;
		}
		return x + w + 6;
	}

	/// <summary>The search's results, worked out again only when the words, the source or the list change.</summary>
	private static List<FoundGame> FindResults()
	{
		List<FoundGame> catalog = GbaFinder.Games;
		bool online = GameArt.Now - _findTypedAt > 0.7;
		int version = System.Threading.Volatile.Read(ref GbaFinder.OnlineVersion);
		if (_findResults == null || _findResultsOf != catalog || _findResultsQuery != _findQuery || _findResultsSource != _findSource || _findResultsSystem != _findSystem || _findResultsOnline != online || _findResultsAdult != FinderShowsAdult || _findResultsVersion != version)
		{
			_findResults = GbaFinder.Search(_findQuery, _findSource, _findSystem, FinderShowsAdult, online);
			_findResultsSystem = _findSystem;
			_findResultsOf = catalog;
			_findResultsQuery = _findQuery;
			_findResultsSource = _findSource;
			_findResultsOnline = online;
			_findResultsAdult = FinderShowsAdult;
			_findResultsVersion = version;
		}
		return _findResults;
	}

	private static void DrawFindEmpty(SpriteBatch sb, Rectangle box)
	{
		MusicDrawing.Box(sb, box, Color.Black * 0.18f);
		string text;
		string sub = null;
		if (GbaFinder.Loading)
		{
			text = IsaClient.Languages.L10n.T("Looking for games...");
			int total = Math.Max(1, GbaFinder.LoadTotal);
			float part = Math.Min(1f, GbaFinder.LoadDone / (float)total);
			Rectangle bar = new Rectangle(box.Center.X - 120, box.Center.Y + 14, 240, 8);
			MusicDrawing.Fill(sb, bar, Color.Black * 0.4f);
			MusicDrawing.Fill(sb, new Rectangle(bar.X, bar.Y, (int)(bar.Width * part), bar.Height), MusicPlayerWindow.AccentColor);
		}
		else if (GbaFinder.Games.Count == 0)
		{
			text = IsaClient.Languages.L10n.T("No games could be found.");
			sub = GbaFinder.LoadProblem;
			if (MusicDrawing.TextButton(sb, "pkmn:find:retry", new Rectangle(box.Center.X - 60, box.Center.Y + 40, 120, 30), "Try again", new Color(60, 140, 80)))
			{
				GbaFinder.Load(refresh: true);
			}
		}
		else
		{
			text = IsaClient.Languages.L10n.T("Nothing matches that.");
			sub = IsaClient.Languages.L10n.T("Try other words, or both sources.");
		}
		Tetris.Centered(sb, text, box, box.Center.Y - 22, Color.White, 0.8f);
		if (sub != null)
		{
			Tetris.Centered(sb, sub, box, box.Center.Y + 2, MusicPlayerWindow.MutedColor, 0.56f);
		}
	}

	private static void DrawFoundGrid(SpriteBatch sb, Rectangle box, List<FoundGame> results)
	{
		const int cardW = 190;
		const int cardH = 252;
		const int gap = 12;
		int cols = Math.Max(1, (box.Width - 8 + gap) / (cardW + gap));
		int rows = (results.Count + cols - 1) / cols;
		float content = rows * (cardH + gap) - gap;
		if (MusicPlayerWindow.WheelDelta != 0 && MusicPlayerWindow.MouseIn(box))
		{
			_findScroll -= MusicPlayerWindow.WheelDelta / 2f;
			MusicPlayerWindow.WheelDelta = 0;
		}
		_findScroll = MathHelper.Clamp(_findScroll, 0f, Math.Max(0f, content - box.Height));
		int left = box.X + (box.Width - 8 - (cols * (cardW + gap) - gap)) / 2;
		GraphicsDevice gd = Main.instance.GraphicsDevice;
		Rectangle oldScissor = gd.ScissorRectangle;
		RasterizerState clip = new RasterizerState { CullMode = CullMode.None, ScissorTestEnable = true };
		sb.End();
		gd.ScissorRectangle = Rectangle.Intersect(MusicPlayerWindow.ToScreen(box), gd.Viewport.Bounds);
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, clip, null, Main.UIScaleMatrix);
		GameArt.Raster = clip;
		try
		{
			int first = Math.Max(0, (int)(_findScroll / (cardH + gap)) * cols);
			for (int i = first; i < results.Count; i++)
			{
				Rectangle card = new Rectangle(left + (i % cols) * (cardW + gap), box.Y + (i / cols) * (cardH + gap) - (int)_findScroll, cardW, cardH);
				if (card.Y > box.Bottom)
				{
					break;
				}
				if (card.Bottom < box.Y)
				{
					continue;
				}
				DrawFoundCard(sb, card, box, results[i]);
			}
		}
		finally
		{
			GameArt.Raster = RasterizerState.CullCounterClockwise;
			sb.End();
			gd.ScissorRectangle = oldScissor;
			Resume(sb);
		}
		if (content > box.Height)
		{
			Rectangle track = new Rectangle(box.Right - 4, box.Y, 4, box.Height);
			MusicDrawing.Fill(sb, track, Color.Black * 0.4f);
			int thumbH = Math.Max(24, (int)(box.Height * box.Height / content));
			int thumbY = track.Y + (int)((box.Height - thumbH) * (_findScroll / Math.Max(1f, content - box.Height)));
			MusicDrawing.Fill(sb, new Rectangle(track.X, thumbY, 4, thumbH), MusicPlayerWindow.MutedColor);
		}
	}

	private static readonly Color HubColor = new Color(46, 132, 96);

	private static readonly Color ItchColor = new Color(196, 64, 72);

	private static readonly Color GameJoltColor = new Color(96, 132, 24);

	private static readonly Color ArchiveColor = new Color(70, 92, 150);

	private static readonly Color UniversalDbColor = new Color(150, 96, 40);

	private static readonly Color GbaTagColor = new Color(98, 78, 182);

	private static readonly Color DsTagColor = new Color(44, 120, 176);

	private static void DrawFoundCard(SpriteBatch sb, Rectangle card, Rectangle box, FoundGame g)
	{
		if (!g.Checked)
		{
			// Got before (another day): it's in its folder.
			g.Checked = true;
			if (g.Status == FinderStatus.None)
			{
				List<string> got = GbaFinder.AlreadyGot(g);
				if (got != null)
				{
					g.Roms = got;
					g.Status = FinderStatus.Done;
					if (g.Source == FinderSource.Archive && g.IaFiles != null)
					{
						// A collection got in part: the rest is still there to get.
						g.More = Math.Max(0, g.IaFiles.Count(f => f.EndsWith(g.Ds ? ".nds" : ".gba", StringComparison.OrdinalIgnoreCase)) - got.Count);
					}
				}
			}
		}
		bool hover = MusicPlayerWindow.MouseIn(card) && MusicPlayerWindow.MouseIn(box);
		bool done = g.Status == FinderStatus.Done;
		MusicDrawing.Box(sb, card, done ? new Color(50, 70, 104) : (hover ? new Color(58, 72, 136) : new Color(42, 52, 100)));
		// The picture, filling its frame (cut to fit), or a cartridge with its initials.
		Rectangle cover = new Rectangle(card.X + 8, card.Y + 8, card.Width - 16, 116);
		MusicDrawing.Fill(sb, cover, new Color(14, 16, 30));
		Texture2D tex = FoundPicture(g.Cover);
		if (tex != null)
		{
			float scale = Math.Max(cover.Width / (float)tex.Width, cover.Height / (float)tex.Height);
			int sw = Math.Min(tex.Width, (int)Math.Ceiling(cover.Width / scale));
			int sh = Math.Min(tex.Height, (int)Math.Ceiling(cover.Height / scale));
			Rectangle src = new Rectangle((tex.Width - sw) / 2, (tex.Height - sh) / 2, sw, sh);
			sb.Draw(tex, cover, src, Color.White);
		}
		else if (g.Ds)
		{
			DsCartridge(sb, new Rectangle(cover.X + 40, cover.Y + 10, cover.Width - 80, cover.Height - 20), null, g.Title ?? "");
		}
		else
		{
			Cartridge(sb, new Rectangle(cover.X + 34, cover.Y + 10, cover.Width - 68, cover.Height - 20), g.Source switch
			{
				FinderSource.HomebrewHub => new Color(70, 120, 110),
				FinderSource.GameJolt => new Color(110, 130, 60),
				FinderSource.Archive => new Color(80, 100, 140),
				_ => new Color(120, 80, 110)
			}, null, g.Title ?? "");
		}
		GameArt.Frame(sb, cover, Color.Black * 0.6f, 1);
		bool overCover = hover && MusicPlayerWindow.MouseIn(cover);
		if (overCover && !string.IsNullOrEmpty(g.Description))
		{
			MusicDrawing.Hover("pkmn:find:about:" + g.Key, Wrap(g.Description, 56, 9));
		}
		// the console it's for, then where it comes from
		int chipX = GameOverlay.Chip(sb, g.Ds ? "DS" : "GBA", cover.X + 4, cover.Y + 4, (g.Ds ? DsTagColor : GbaTagColor) * 0.95f, Color.White, cover.Right - 4);
		chipX = GameOverlay.Chip(sb, g.Source switch
		{
			FinderSource.HomebrewHub => "Homebrew Hub",
			FinderSource.UniversalDb => "Universal-DB",
			FinderSource.GameJolt => "Game Jolt",
			FinderSource.Archive => "archive.org",
			_ => "itch.io"
		}, chipX, cover.Y + 4, g.Source switch
		{
			FinderSource.HomebrewHub => HubColor,
			FinderSource.UniversalDb => UniversalDbColor,
			FinderSource.GameJolt => GameJoltColor,
			FinderSource.Archive => ArchiveColor,
			_ => ItchColor
		} * 0.92f, Color.White, cover.Right - 4);
		if (g.Adult)
		{
			GameOverlay.Chip(sb, "18+", chipX, cover.Y + 4, new Color(170, 50, 70) * 0.95f, Color.White, cover.Right - 4);
		}
		// Name, who made it, what it is.
		MusicDrawing.RawText(sb, g.Title, card.X + 9, card.Y + 130, Color.White, 0.66f, card.Width - 18);
		if (!string.IsNullOrEmpty(g.Author))
		{
			MusicDrawing.RawText(sb, IsaClient.Languages.L10n.F("by {0}", g.Author), card.X + 9, card.Y + 148, MusicPlayerWindow.MutedColor, 0.54f, card.Width - 18);
		}
		int by = card.Bottom - 30;
		int statusY = card.Y + 166;
		switch (g.Status)
		{
		case FinderStatus.Working:
		{
			long size = System.Threading.Interlocked.Read(ref g.Size);
			long got = System.Threading.Interlocked.Read(ref g.Got);
			Rectangle bar = new Rectangle(card.X + 9, statusY + 6, card.Width - 18, 6);
			MusicDrawing.Fill(sb, bar, Color.Black * 0.45f);
			if (size > 0)
			{
				MusicDrawing.Fill(sb, new Rectangle(bar.X, bar.Y, (int)(bar.Width * Math.Min(1.0, got / (double)size)), bar.Height), MusicPlayerWindow.AccentColor);
			}
			else
			{
				// Not known yet: a band going back and forth.
				int run = (int)(GameArt.Now * 90.0 % (bar.Width + 40)) - 40;
				Rectangle band = Rectangle.Intersect(new Rectangle(bar.X + run, bar.Y, 40, bar.Height), bar);
				MusicDrawing.Fill(sb, band, MusicPlayerWindow.AccentColor);
			}
			MusicDrawing.RawText(sb, g.Message, card.X + 9, statusY + 18, MusicPlayerWindow.MutedColor, 0.5f, card.Width - 18);
			break;
		}
		case FinderStatus.Failed:
		{
			string[] lines = Wrap(g.Message ?? "", 34, 3).Split('\n');
			for (int l = 0; l < lines.Length; l++)
			{
				MusicDrawing.RawText(sb, lines[l], card.X + 9, statusY + l * 14, new Color(255, 150, 150), 0.5f, card.Width - 18);
			}
			break;
		}
		case FinderStatus.Done:
			GameOverlay.Chip(sb, IsaClient.Languages.L10n.T("In your games"), card.X + 9, statusY + 2, new Color(60, 140, 80), Color.White, card.Right - 9);
			break;
		default:
			if (!string.IsNullOrEmpty(g.Blurb) && g.Blurb != g.Title)
			{
				string[] about = Wrap(g.Blurb, 34, 3).Split('\n');
				for (int l = 0; l < about.Length; l++)
				{
					MusicDrawing.RawText(sb, about[l], card.X + 9, statusY + l * 14, new Color(200, 206, 226), 0.5f, card.Width - 18);
				}
			}
			break;
		}
		// Get / Play, and its page.
		Rectangle main = new Rectangle(card.X + 9, by, card.Width - 18 - 32, 24);
		bool more = g.Status == FinderStatus.Done && g.More > 0;
		string label = more ? IsaClient.Languages.L10n.F("Get more ({0})", g.More) : g.Status switch
		{
			FinderStatus.Working => "Getting...",
			FinderStatus.Done => "Play",
			FinderStatus.Failed => "Try again",
			_ => "Get"
		};
		string tip = more ? "The next ROMs of this collection (the ones you have are in your games)" : g.Status switch
		{
			FinderStatus.Done => "Play it now",
			FinderStatus.Working => null,
			_ => "Download it and add it to your games"
		};
		if (MusicDrawing.TextButton(sb, "pkmn:find:get:" + g.Key, main, label, (g.Status == FinderStatus.Failed) ? new Color(150, 90, 60) : new Color(60, 140, 80), tip, g.Status != FinderStatus.Working))
		{
			if (more)
			{
				GbaFinder.Get(g);
			}
			else if (g.Status == FinderStatus.Done && g.Roms != null && g.Roms.Count > 0)
			{
				PlayFound(g);
			}
			else
			{
				GbaFinder.Get(g);
			}
		}
		if (MusicDrawing.TextButton(sb, "pkmn:find:page:" + g.Key, new Rectangle(card.Right - 9 - 26, by, 26, 24), "i", MusicPlayerWindow.PanelColor, "Open its page in your browser (more about it)") && !string.IsNullOrEmpty(g.Page))
		{
			OpenPage(g.Page);
		}
	}

	/// <summary>Its first ROM in the console, playing (it's put back in the library if it was taken out).</summary>
	private static void PlayFound(FoundGame g)
	{
		string rom = g.Roms.FirstOrDefault(File.Exists);
		if (rom == null)
		{
			g.Status = FinderStatus.None;
			g.Roms = null;
			return;
		}
		PokemonGame.AddGames(g.Roms.Where(File.Exists));
		bool wasCurrent = string.Equals(rom, PokemonGame.RomPath, StringComparison.OrdinalIgnoreCase);
		if (PokemonGame.SelectGame(rom))
		{
			_page = Page.Console;
			_focused = true;
			if (!(wasCurrent && PokemonGame.Playing))
			{
				PokemonGame.Start(PokemonGame.HasResume && PokemonGame.Resume);
			}
		}
	}

	/// <summary>The games got in the background, into the library (main thread).</summary>
	private static void TakeFoundGames()
	{
		while (GbaFinder.Finished.TryDequeue(out FoundGame g))
		{
			string first = PokemonGame.AddGames(g.Roms);
			if (first == null)
			{
				g.Status = FinderStatus.Failed;
				g.Message = PokemonGame.Problem ?? "It isn't a Game Boy Advance or DS game.";
				continue;
			}
			_entries.Clear();
			PokemonGame.ShowToast(IsaClient.Languages.L10n.F("Added to your games: {0}", g.Title));
		}
	}

	/// <summary>A card's picture as a texture (null while it comes or when it has none); only the latest few are kept.</summary>
	internal static Texture2D FoundPicture(string url)
	{
		if (string.IsNullOrEmpty(url))
		{
			return null;
		}
		if (_findTextures.TryGetValue(url, out Texture2D tex))
		{
			_findTextureOrder.Remove(url);
			_findTextureOrder.AddLast(url);
			return (tex == null || tex.IsDisposed) ? null : tex;
		}
		if (_findUploads >= FindUploadsPerFrame)
		{
			// the rest next frame
			return null;
		}
		GbaFinder.Picture p = GbaFinder.TakePicture(url, out bool failed);
		if (p == null && !failed)
		{
			return null;
		}
		_findUploads++;
		if (p != null)
		{
			try
			{
				tex = new Texture2D(Main.instance.GraphicsDevice, p.Width, p.Height);
				Color[] px = new Color[p.Argb.Length];
				for (int k = 0; k < px.Length; k++)
				{
					int c = p.Argb[k];
					px[k] = new Color((c >> 16) & 0xFF, (c >> 8) & 0xFF, c & 0xFF, (c >> 24) & 0xFF);
				}
				tex.SetData(px);
			}
			catch (Exception ex)
			{
				ClientLog.LogOnce(ex);
				tex = null;
			}
		}
		_findTextures[url] = tex;
		_findTextureOrder.AddLast(url);
		while (_findTextureOrder.Count > FindTextureLimit)
		{
			string old = _findTextureOrder.First.Value;
			_findTextureOrder.RemoveFirst();
			if (_findTextures.TryGetValue(old, out Texture2D drop))
			{
				drop?.Dispose();
				_findTextures.Remove(old);
			}
		}
		return tex;
	}

	/// <summary>Words in lines of about <paramref name="width"/> letters, at most <paramref name="maxLines"/> (the last ends in "...").</summary>
	internal static string Wrap(string text, int width, int maxLines)
	{
		string[] words = (text ?? "").Replace("\r", "").Replace("\n", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		List<string> lines = new List<string>();
		StringBuilder line = new StringBuilder();
		foreach (string w in words)
		{
			if (line.Length > 0 && line.Length + 1 + w.Length > width)
			{
				lines.Add(line.ToString());
				line.Clear();
				if (lines.Count == maxLines)
				{
					break;
				}
			}
			if (line.Length > 0)
			{
				line.Append(' ');
			}
			line.Append(w);
		}
		if (lines.Count < maxLines && line.Length > 0)
		{
			lines.Add(line.ToString());
			line.Clear();
		}
		bool more = line.Length > 0 || lines.Count == maxLines && string.Join(" ", lines).Length < string.Join(" ", words).Length;
		if (more && lines.Count > 0)
		{
			string last = lines[lines.Count - 1];
			lines[lines.Count - 1] = ((last.Length > width - 3) ? last.Substring(0, width - 3) : last) + "...";
		}
		return string.Join("\n", lines);
	}

	/// <summary>A web page in the browser (only http and https links).</summary>
	internal static void OpenPage(string url)
	{
		if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		try
		{
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
		}
		catch (Exception ex)
		{
			ClientLog.LogOnce(ex);
		}
	}
}
