using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IsaClient.Games;
using IsaClient.Gba;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace IsaClient.Psp;

/// <summary>The Games page's "Find games": free PSP games made by the community (see <see cref="PspFinder"/>), as cards.</summary>
public static partial class PspWindow
{
	private static string _findQuery = "";

	private static FinderSource? _findSource;

	private static float _findScroll;

	private static List<FoundGame> _findResults;

	private static List<FoundGame> _findResultsOf;

	private static string _findResultsQuery;

	private static FinderSource? _findResultsSource;

	private static readonly Color ItchColor = new Color(196, 64, 72);

	private static readonly Color ArchiveColor = new Color(70, 92, 150);

	private static readonly Color PspTagColor = new Color(44, 60, 150);

	private static void OpenFinder()
	{
		PokemonWindow.EnsureFinderFolders();
		_page = Page.Find;
		if (PspFinder.Games.Count == 0 || DateTime.UtcNow - PspFinder.LoadedAt > TimeSpan.FromDays(3))
		{
			PspFinder.Load(refresh: false);
		}
	}

	/// <summary>The Games page's card next to "Add games".</summary>
	private static void DrawFindCard(SpriteBatch sb, Rectangle card, Rectangle box)
	{
		bool hover = DashedCard(sb, card, box);
		PokemonWindow.MagnifyingGlass(sb, new Vector2(card.Center.X, card.Y + 68), 18, new Color(120, 200, 255) * (hover ? 0.9f : 0.5f));
		Tetris.Centered(sb, IsaClient.Languages.L10n.T("Find games"), card, card.Y + 110, Color.White, 0.72f);
		Tetris.Centered(sb, IsaClient.Languages.L10n.T("Free PSP games by the community"), card, card.Y + 132, MusicPlayerWindow.MutedColor, 0.56f);
		Tetris.Centered(sb, IsaClient.Languages.L10n.T("archive.org, itch.io"), card, card.Y + 150, MusicPlayerWindow.MutedColor * 0.8f, 0.5f);
		if (hover && MusicPlayerWindow.Clicked)
		{
			MusicPlayerWindow.Clicked = false;
			OpenFinder();
		}
	}

	private static void DrawFindPage(SpriteBatch sb, Rectangle area)
	{
		if (PageHeader(sb, area, "Find games", "Free PSP games made by the community, from archive.org's homebrew library and itch.io."))
		{
			if (MusicPlayerWindow.FocusedField == "psp:find")
			{
				MusicPlayerWindow.FocusedField = null;
			}
			_page = Page.Games;
			return;
		}
		TakeFoundGames();
		PokemonWindow.NewPictureFrame();
		int y = area.Y + 58;
		Rectangle field = new Rectangle(area.X + 12, y, Math.Min(260, area.Width / 3), 30);
		string query = MusicDrawing.Field(sb, "psp:find", field, _findQuery, "Search games...", 40, searchIcon: true);
		if (query != _findQuery)
		{
			_findQuery = query;
			_findScroll = 0f;
		}
		int x = field.Right + 10;
		x = SourceButton(sb, x, y, "All", null);
		x = SourceButton(sb, x, y, "archive.org", FinderSource.Archive);
		x = SourceButton(sb, x, y, "itch.io", FinderSource.Itch);
		Rectangle refresh = new Rectangle(area.Right - 112, y, 100, 30);
		if (MusicDrawing.TextButton(sb, "psp:find:refresh", refresh, PspFinder.Loading ? "Looking..." : "Refresh", MusicPlayerWindow.PanelColor, "Look at the sites again (the list is kept for a few days)", !PspFinder.Loading))
		{
			PspFinder.Load(refresh: true);
		}
		List<FoundGame> results = FindResults();
		string count = (PspFinder.Loading && PspFinder.Games.Count == 0) ? "" : IsaClient.Languages.L10n.F("{0} games", results.Count);
		if (PspFinder.Loading && PspFinder.Games.Count > 0)
		{
			count = IsaClient.Languages.L10n.T("Updating the list...") + "  " + count;
		}
		float countW = MusicDrawing.Measure(count, 0.62f);
		if (x + countW + 12 < refresh.X)
		{
			MusicDrawing.RawText(sb, count, refresh.X - 12 - countW, y + 7, MusicPlayerWindow.MutedColor, 0.62f);
		}
		Rectangle box = new Rectangle(area.X + 12, y + 38, area.Width - 24, area.Bottom - 30 - (y + 38));
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
		if (PokemonWindow.Choice(sb, "psp:find:src:" + label, new Rectangle(x, y, w, 30), label, _findSource == source, null))
		{
			_findSource = source;
			_findScroll = 0f;
		}
		return x + w + 6;
	}

	/// <summary>The search's results, worked out again only when the words, the source or the list change.</summary>
	private static List<FoundGame> FindResults()
	{
		List<FoundGame> catalog = PspFinder.Games;
		if (_findResults == null || _findResultsOf != catalog || _findResultsQuery != _findQuery || _findResultsSource != _findSource)
		{
			_findResults = PspFinder.Search(_findQuery, _findSource);
			_findResultsOf = catalog;
			_findResultsQuery = _findQuery;
			_findResultsSource = _findSource;
		}
		return _findResults;
	}

	private static void DrawFindEmpty(SpriteBatch sb, Rectangle box)
	{
		MusicDrawing.Box(sb, box, Color.Black * 0.18f);
		string text;
		string sub = null;
		if (PspFinder.Loading)
		{
			text = IsaClient.Languages.L10n.T("Looking for games...");
		}
		else if (PspFinder.Games.Count == 0)
		{
			text = IsaClient.Languages.L10n.T("No games could be found.");
			sub = PspFinder.LoadProblem;
			if (MusicDrawing.TextButton(sb, "psp:find:retry", new Rectangle(box.Center.X - 60, box.Center.Y + 40, 120, 30), "Try again", Green))
			{
				PspFinder.Load(refresh: true);
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
				if (card.Bottom >= box.Y)
				{
					DrawFoundCard(sb, card, box, results[i]);
				}
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

	private static void DrawFoundCard(SpriteBatch sb, Rectangle card, Rectangle box, FoundGame g)
	{
		if (!g.Checked)
		{
			// got before (another day): it's in the library's list of what came from here
			g.Checked = true;
			if (g.Status == FinderStatus.None && PspConfig.Found.TryGetValue(g.Key, out string got) && File.Exists(got))
			{
				g.Roms = new List<string> { got };
				g.Status = FinderStatus.Done;
			}
		}
		bool hover = MusicPlayerWindow.MouseIn(card) && MusicPlayerWindow.MouseIn(box);
		bool done = g.Status == FinderStatus.Done;
		MusicDrawing.Box(sb, card, done ? new Color(50, 70, 104) : (hover ? new Color(58, 72, 136) : new Color(42, 52, 100)));
		Rectangle cover = new Rectangle(card.X + 8, card.Y + 8, card.Width - 16, 116);
		MusicDrawing.Fill(sb, cover, new Color(14, 16, 30));
		Texture2D tex = PokemonWindow.FoundPicture(g.Cover);
		if (tex != null)
		{
			float scale = Math.Max(cover.Width / (float)tex.Width, cover.Height / (float)tex.Height);
			int sw = Math.Min(tex.Width, (int)Math.Ceiling(cover.Width / scale));
			int sh = Math.Min(tex.Height, (int)Math.Ceiling(cover.Height / scale));
			sb.Draw(tex, cover, new Rectangle((tex.Width - sw) / 2, (tex.Height - sh) / 2, sw, sh), Color.White);
		}
		else
		{
			Handheld(sb, new Rectangle(cover.Center.X - 40, cover.Center.Y - 18, 80, 36));
		}
		GameArt.Frame(sb, cover, Color.Black * 0.6f, 1);
		PspFinder.AskWeight(g);
		if (g.Weight != null)
		{
			// about what it takes: at the picture's corner
			string weight = "~" + g.Weight;
			int ww = (int)MusicDrawing.Measure(weight, 0.54f) + 12;
			Rectangle tag = new Rectangle(cover.Right - ww - 4, cover.Bottom - 22, ww, 18);
			MusicDrawing.Fill(sb, tag, Color.Black * 0.72f);
			MusicDrawing.RawText(sb, weight, tag.X + 6, tag.Y + 1, Color.White, 0.54f);
		}
		if (hover && MusicPlayerWindow.MouseIn(cover) && !string.IsNullOrEmpty(g.Description))
		{
			MusicDrawing.Hover("psp:find:about:" + g.Key, PokemonWindow.Wrap(g.Description, 56, 9));
		}
		int chipX = GameOverlay.Chip(sb, "PSP", cover.X + 4, cover.Y + 4, PspTagColor * 0.95f, Color.White, cover.Right - 4);
		GameOverlay.Chip(sb, (g.Source == FinderSource.Archive) ? "archive.org" : "itch.io", chipX, cover.Y + 4, ((g.Source == FinderSource.Archive) ? ArchiveColor : ItchColor) * 0.92f, Color.White, cover.Right - 4);
		MusicDrawing.RawText(sb, g.Title, card.X + 9, card.Y + 130, Color.White, 0.66f, card.Width - 18);
		if (!string.IsNullOrEmpty(g.Author))
		{
			MusicDrawing.RawText(sb, IsaClient.Languages.L10n.F("by {0}", g.Author), card.X + 9, card.Y + 148, MusicPlayerWindow.MutedColor, 0.54f, card.Width - 18);
		}
		int statusY = card.Y + 166;
		switch (g.Status)
		{
		case FinderStatus.Working:
		{
			long size = System.Threading.Interlocked.Read(ref g.Size);
			long have = System.Threading.Interlocked.Read(ref g.Got);
			Rectangle bar = new Rectangle(card.X + 9, statusY + 6, card.Width - 18, 6);
			MusicDrawing.Fill(sb, bar, Color.Black * 0.45f);
			if (size > 0)
			{
				MusicDrawing.Fill(sb, new Rectangle(bar.X, bar.Y, (int)(bar.Width * Math.Min(1.0, have / (double)size)), bar.Height), MusicPlayerWindow.AccentColor);
			}
			else
			{
				int run = (int)(GameArt.Now * 90.0 % (bar.Width + 40)) - 40;
				MusicDrawing.Fill(sb, Rectangle.Intersect(new Rectangle(bar.X + run, bar.Y, 40, bar.Height), bar), MusicPlayerWindow.AccentColor);
			}
			MusicDrawing.RawText(sb, g.Message, card.X + 9, statusY + 18, MusicPlayerWindow.MutedColor, 0.5f, card.Width - 18);
			break;
		}
		case FinderStatus.Failed:
			FoundLines(sb, card, statusY, g.Message, Bad);
			break;
		case FinderStatus.Done:
			GameOverlay.Chip(sb, IsaClient.Languages.L10n.T("In your games"), card.X + 9, statusY + 2, Green, Color.White, card.Right - 9);
			break;
		default:
			if (!string.IsNullOrEmpty(g.Blurb) && g.Blurb != g.Title)
			{
				FoundLines(sb, card, statusY, g.Blurb, new Color(200, 206, 226));
			}
			break;
		}
		int by = card.Bottom - 30;
		Rectangle main = new Rectangle(card.X + 9, by, card.Width - 18 - 32, 24);
		string label = g.Status switch
		{
			FinderStatus.Working => "Getting...",
			FinderStatus.Done => "Play",
			FinderStatus.Failed => "Try again",
			_ => "Get"
		};
		string tip = g.Status switch
		{
			FinderStatus.Done => "Play it now",
			FinderStatus.Working => null,
			_ => "Download it and add it to your games"
		};
		if (MusicDrawing.TextButton(sb, "psp:find:get:" + g.Key, main, label, (g.Status == FinderStatus.Failed) ? new Color(150, 90, 60) : Green, tip, g.Status != FinderStatus.Working))
		{
			if (done && g.Roms != null && g.Roms.Count > 0)
			{
				PlayFound(g);
			}
			else
			{
				PspFinder.Get(g);
			}
		}
		if (MusicDrawing.TextButton(sb, "psp:find:page:" + g.Key, new Rectangle(card.Right - 35, by, 26, 24), "i", MusicPlayerWindow.PanelColor, "Open its page in your browser (more about it)") && !string.IsNullOrEmpty(g.Page))
		{
			PokemonWindow.OpenPage(g.Page);
		}
	}

	private static void FoundLines(SpriteBatch sb, Rectangle card, int y, string text, Color color)
	{
		string[] lines = PokemonWindow.Wrap(text ?? "", 34, 3).Split('\n');
		for (int l = 0; l < lines.Length; l++)
		{
			MusicDrawing.RawText(sb, lines[l], card.X + 9, y + l * 14, color, 0.5f, card.Width - 18);
		}
	}

	/// <summary>Its game in the console, playing (it's put back in the library if it was taken out).</summary>
	private static void PlayFound(FoundGame g)
	{
		string game = g.Roms.FirstOrDefault(File.Exists);
		if (game == null)
		{
			g.Status = FinderStatus.None;
			g.Roms = null;
			return;
		}
		if (!PspConfig.Games.Exists(p => string.Equals(p, game, StringComparison.OrdinalIgnoreCase)))
		{
			PspConfig.Games.Add(game);
			PspConfig.Save();
		}
		bool wasPlaying = string.Equals(game, PspGame.RomPath, StringComparison.OrdinalIgnoreCase) && PspGame.Playing;
		PspGame.SelectGame(game);
		_page = Page.Console;
		_focused = true;
		if (!wasPlaying)
		{
			PspGame.Start(PspGame.Resume);
		}
	}

	/// <summary>The games downloaded in the background, into the library (one at a time: they may have to be copied out).</summary>
	private static void TakeFoundGames()
	{
		if (PspLibrary.ImportBusy || !PspFinder.Finished.TryDequeue(out FoundGame g))
		{
			return;
		}
		List<string> downloads = g.Roms;
		PspLibrary.Add(downloads.ToArray(), first =>
		{
			if (first == null)
			{
				g.Status = FinderStatus.Failed;
				g.Message = PspGame.Problem ?? "It isn't a PSP game.";
				return;
			}
			PspFinder.Forget(downloads);
			g.Roms = new List<string> { first };
			g.Message = null;
			g.Status = FinderStatus.Done;
			PspConfig.Found[g.Key] = first;
			PspConfig.Save();
			PspGame.ShowToast(IsaClient.Languages.L10n.F("Added to your games: {0}", g.Title));
		});
	}
}
