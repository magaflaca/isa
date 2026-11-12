using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using IsaClient.Core;
using IsaClient.Hub;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Keys = Microsoft.Xna.Framework.Input.Keys;
using KeyboardState = Microsoft.Xna.Framework.Input.KeyboardState;
using Newtonsoft.Json;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;

namespace IsaClient.Languages;

/// <summary>
/// The language window (the flag next to the music note in the main menu, Settings in the panel, .language): pick
/// IsaClient's language or follow the game's (a language not downloaded yet comes from the repository, with its
/// progress shown on its row), import a language file, export the texts to translate or correct them, or go help
/// translate. One of the music player's floating surfaces, so it works in the main menu and in the game.
/// </summary>
public static class LanguageWindow
{
	private const string Layer = "language";

	private const int Width = 560;

	private const int Height = 600;

	public static bool Visible { get; private set; }

	private static bool _registered;

	private static Rectangle _screenRect;

	private static KeyboardState _keys;

	private static string _note;

	private static double _noteUntil;

	private static bool _pickerOpen;

	private static bool _listening;

	private static readonly Color Blue = new Color(70, 120, 210);

	private static readonly Color Green = new Color(60, 150, 95);

	private static readonly Color Red = new Color(170, 60, 60);

	private static readonly Color Amber = new Color(150, 115, 50);

	public static void Open()
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
		if (!_listening)
		{
			_listening = true;
			LanguageRepo.Notice += Note;
		}
		L10n.Reload();
		// Opening it looks again for languages (not more than once a minute).
		if (LanguageRepo.CheckedAt == null || DateTime.UtcNow - LanguageRepo.CheckedAt.Value > TimeSpan.FromMinutes(1))
		{
			LanguageRepo.Refresh();
		}
		Visible = true;
		_keys = Main.keyState;
		MusicPlayerWindow.BringSurfaceToFront(Layer);
		SoundEngine.PlaySound(10);
	}

	public static void Close()
	{
		if (Visible)
		{
			Visible = false;
			_screenRect = Rectangle.Empty;
			SoundEngine.PlaySound(11);
		}
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

	private static void Note(string text)
	{
		_note = L10n.T(text);
		_noteUntil = Main.timeForVisualEffects + 240.0;
	}

	#region Drawing

	/// <summary>Small text with point sampling (the main menu's) looks eaten away: this window draws smooth.</summary>
	private static void Smooth(SpriteBatch sb, Rectangle? clip)
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

	private static RasterizerState _scissor;

	private static void Draw(SpriteBatch sb, int screenW, int screenH)
	{
		Smooth(sb, null);
		try
		{
			DrawWindow(sb, screenW, screenH);
		}
		finally
		{
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.gameMenu ? SamplerState.PointClamp : SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
		}
	}

	private static void DrawWindow(SpriteBatch sb, int screenW, int screenH)
	{
		int w = Math.Min(Width, screenW - 20);
		int h = Math.Min(Height, screenH - 20);
		Rectangle r = new Rectangle((screenW - w) / 2, (screenH - h) / 2, w, h);
		_screenRect = MusicPlayerWindow.ToScreen(r);
		KeyboardState keys = Main.keyState;
		if (keys.IsKeyDown(Keys.Escape) && !_keys.IsKeyDown(Keys.Escape) && MusicPlayerWindow.FocusedField == null)
		{
			_keys = keys;
			Close();
			return;
		}
		_keys = keys;
		MusicDrawing.Box(sb, r, MusicPlayerWindow.BackColor * 0.98f);
		// Header: the flag of the language in use, the title, close.
		L10n.Pack current = L10n.CurrentPack ?? L10n.Find(L10n.English);
		CountryFlags.Draw(sb, current?.Flag ?? "us", new Rectangle(r.X + 14, r.Y + 13, 30, 20));
		Utils.DrawBorderStringBig(sb, L10n.T("Language"), new Vector2(r.X + 54, r.Y + 8), MusicPlayerWindow.AccentColor, 0.5f);
		if (MusicDrawing.BareIcon(sb, "language:close", new Rectangle(r.Right - 30, r.Y + 13, 17, 18), MusicDrawing.Tex("Images/MapDeath"), null, "Close (Esc)", Color.White))
		{
			Close();
			return;
		}
		MusicDrawing.Text(sb, "IsaClient's menus, buttons and messages. Commands stay as you type them.", r.X + 16, r.Y + 44, MusicPlayerWindow.MutedColor, 0.72f, r.Width - 32);
		DrawSource(sb, new Rectangle(r.X + 10, r.Y + 68, r.Width - 20, 28));
		Rectangle list = new Rectangle(r.X + 10, r.Y + 102, r.Width - 20, r.Height - 102 - 166);
		DrawList(sb, list);
		DrawTools(sb, new Rectangle(r.X + 10, list.Bottom + 8, r.Width - 20, 154));
		if (_note != null && Main.timeForVisualEffects < _noteUntil)
		{
			Vector2 size = FontAssets.MouseText.Value.MeasureString(_note) * 0.8f;
			Rectangle pill = new Rectangle(r.Center.X - (int)size.X / 2 - 14, r.Bottom + 8, (int)size.X + 28, 28);
			MusicDrawing.Box(sb, pill, MusicPlayerWindow.PanelColor * 0.98f);
			Utils.DrawBorderString(sb, _note, new Vector2(pill.X + 14, pill.Y + 6), Color.White, 0.8f);
		}
		// A click anywhere on the window is the window's.
		if (MusicPlayerWindow.Clicked && MusicPlayerWindow.MouseIn(r))
		{
			MusicPlayerWindow.Clicked = false;
		}
		else if (MusicPlayerWindow.Clicked)
		{
			Close();
		}
	}

	/// <summary>Where the languages come from, when they were last looked for, and a button to look again.</summary>
	private static void DrawSource(SpriteBatch sb, Rectangle area)
	{
		MusicDrawing.Box(sb, area, MusicPlayerWindow.PanelDarkColor * 0.7f);
		bool checking = LanguageRepo.Checking;
		int count = LanguageRepo.Index.Count;
		string text;
		Color color = MusicPlayerWindow.MutedColor;
		if (checking)
		{
			text = L10n.T("Looking for languages...");
		}
		else if (LanguageRepo.CheckError != null && count == 0)
		{
			text = L10n.F("No connection to the languages ({0}): only the ones you have", LanguageRepo.CheckError);
			color = new Color(240, 150, 140);
		}
		else if (LanguageRepo.CheckError != null)
		{
			text = L10n.F("{0} languages (offline: the list you had)", count);
			color = new Color(240, 200, 140);
		}
		else
		{
			text = L10n.F("{0} languages from the IsaClient repository", count) + ((LanguageRepo.CheckedAt != null) ? ("  ·  " + Ago(LanguageRepo.CheckedAt.Value)) : "");
		}
		// A small globe: they come from the internet.
		// Item textures load on demand (in the main menu this one may not be loaded yet).
		Main.instance.LoadItem(Terraria.ID.ItemID.WorldGlobe);
		Texture2D globe = TextureAssets.Item[Terraria.ID.ItemID.WorldGlobe]?.Value;
		if (globe != null)
		{
			MusicDrawing.Icon(sb, globe, globe.Bounds, new Rectangle(area.X + 7, area.Y + 4, 20, 20), Color.White * (checking ? (0.5f + 0.5f * (float)Math.Abs(Math.Sin(Main.timeForVisualEffects / 20.0))) : 1f));
		}
		MusicDrawing.RawText(sb, text, area.X + 34, area.Y + 5, color, 0.68f, area.Width - 34 - 112);
		Rectangle again = new Rectangle(area.Right - 106, area.Y + 2, 102, 24);
		if (MusicDrawing.TextButton(sb, "language:check", again, checking ? "Looking..." : "Check now", MusicPlayerWindow.PanelColor, "Looks for new languages and updates the ones you have", !checking))
		{
			LanguageRepo.Refresh();
		}
	}

	private static string Ago(DateTime utc)
	{
		double minutes = (DateTime.UtcNow - utc).TotalMinutes;
		if (minutes < 1)
		{
			return L10n.T("checked just now");
		}
		if (minutes < 60)
		{
			return L10n.F("checked {0} min ago", (int)minutes);
		}
		return L10n.F("checked {0} h ago", (int)(minutes / 60));
	}

	private static void DrawList(SpriteBatch sb, Rectangle area)
	{
		MusicDrawing.Box(sb, area, MusicPlayerWindow.PanelDarkColor * 0.95f);
		Rectangle inner = new Rectangle(area.X + 4, area.Y + 4, area.Width - 8, area.Height - 8);
		float scroll = MusicDrawing.BeginScroll("language:list", inner);
		Smooth(sb, inner);
		int y = inner.Y - (int)scroll;
		int rowH = 44;
		// The game's language first: what IsaClient uses until something else is picked.
		L10n.Pack game = L10n.Find(L10n.GameLanguage) ?? L10n.Find(L10n.English);
		bool auto = string.IsNullOrWhiteSpace(L10n.Choice);
		if (Row(sb, "auto", new Rectangle(inner.X, y, inner.Width - 8, rowH - 4), "auto", L10n.F("Same as the game ({0})", game?.Name ?? "English"), L10n.T("Changes when you change the game's language"), null, Amber, auto, null))
		{
			Pick("");
		}
		y += rowH;
		foreach (L10n.Pack pack in L10n.Available)
		{
			bool selected = !auto && string.Equals(L10n.Choice, pack.Code, StringComparison.OrdinalIgnoreCase);
			string detail = (pack.Code == L10n.English) ? "IsaClient's own language" : L10n.F("{0}% translated", (int)Math.Floor(L10n.Coverage(pack) * 100f));
			string title = (pack.Name == pack.EnglishName) ? pack.Name : (pack.Name + "  (" + pack.EnglishName + ")");
			// Its state: downloading (with a bar), failed, the player's own, an update, or still to download.
			float? progress = LanguageRepo.Progress(pack.Code);
			string failure = (progress == null) ? LanguageRepo.Failure(pack.Code) : null;
			string tag = null;
			Color tagColor = Amber;
			if (progress != null)
			{
				tag = L10n.F("Downloading {0}%", (int)(progress.Value * 100f));
				tagColor = Blue;
			}
			else if (failure != null)
			{
				tag = L10n.T("Retry");
				tagColor = Red;
				detail = L10n.F("Could not download it: {0}", failure);
			}
			else if (pack.FromFolder)
			{
				tag = L10n.T("your file");
			}
			else if (pack.Customized)
			{
				tag = L10n.T("with your changes");
			}
			else if (LanguageRepo.NeedsUpdate(pack.Code))
			{
				tag = L10n.T("Update");
				tagColor = Green;
			}
			else if (pack.Remote)
			{
				tag = L10n.T("Download");
				tagColor = Blue;
			}
			Rectangle row = new Rectangle(inner.X, y, inner.Width - 8, rowH - 4);
			if (Row(sb, "pack:" + pack.Code, row, pack.Flag, title, L10n.T(detail), tag, tagColor, selected, pack.Remote ? L10n.T("Click to download and use it") : null))
			{
				Pick(pack.Code);
			}
			if (progress != null && MusicPlayerWindow.Visible(row))
			{
				Rectangle bar = new Rectangle(row.X + 50, row.Bottom - 6, row.Width - 60, 3);
				MusicDrawing.Box(sb, bar, MusicPlayerWindow.PanelDarkColor);
				MusicDrawing.Box(sb, new Rectangle(bar.X, bar.Y, Math.Max(2, (int)(bar.Width * progress.Value)), bar.Height), MusicPlayerWindow.AccentColor);
			}
			y += rowH;
		}
		Smooth(sb, null);
		MusicDrawing.EndScroll(sb, "language:list", inner, y + scroll - inner.Y);
	}

	/// <summary>A language: flag, name, a line under it, a tag; true when clicked.</summary>
	private static bool Row(SpriteBatch sb, string id, Rectangle r, string flag, string title, string detail, string tag, Color tagColor, bool selected, string hint)
	{
		if (!MusicPlayerWindow.Visible(r))
		{
			return false;
		}
		bool hover = MusicPlayerWindow.MouseIn(r);
		if (hover)
		{
			MusicDrawing.Hover("language:" + id, selected ? null : (hint ?? "Click to use it"));
		}
		MusicDrawing.Box(sb, r, (selected ? MusicPlayerWindow.SelectedColor : (hover ? MusicPlayerWindow.RowHoverColor : MusicPlayerWindow.PanelColor)) * 0.95f);
		CountryFlags.Draw(sb, flag ?? "us", new Rectangle(r.X + 10, r.Y + (r.Height - 20) / 2, 30, 20));
		MusicDrawing.RawText(sb, title, r.X + 50, r.Y + 3, Color.White, 0.85f, r.Width - 160);
		MusicDrawing.RawText(sb, detail, r.X + 50, r.Y + 21, selected ? new Color(230, 236, 255) : MusicPlayerWindow.MutedColor, 0.68f, r.Width - 160);
		if (tag != null)
		{
			float tagW = MusicDrawing.Measure(tag, 0.65f) + 14f;
			Rectangle chip = new Rectangle(r.Right - (int)tagW - (selected ? 40 : 10), r.Y + (r.Height - 18) / 2, (int)tagW, 18);
			MusicDrawing.Box(sb, chip, tagColor * 0.95f);
			MusicDrawing.RawText(sb, tag, chip.X + 7, chip.Y + 2, Color.White, 0.65f);
		}
		if (selected)
		{
			// The star's texture holds its animation frames one under another: only the first.
			Texture2D star = TextureAssets.Item[Terraria.ID.ItemID.FallenStar].Value;
			Rectangle frame = (Main.itemAnimations[Terraria.ID.ItemID.FallenStar] != null) ? Main.itemAnimations[Terraria.ID.ItemID.FallenStar].GetFrame(star) : star.Bounds;
			MusicDrawing.Icon(sb, star, frame, new Rectangle(r.Right - 30, r.Y + (r.Height - 22) / 2, 22, 22), Color.White);
		}
		return hover && MusicPlayerWindow.Clicked;
	}

	private static void DrawTools(SpriteBatch sb, Rectangle area)
	{
		int half = (area.Width - 8) / 2;
		Rectangle import = new Rectangle(area.X, area.Y, half, 32);
		Rectangle export = new Rectangle(area.X + half + 8, area.Y, half, 32);
		if (MusicDrawing.TextButton(sb, "language:import", import, "Import a language file", MusicPlayerWindow.PanelColor, "Pick a .json language file: it is copied to IsaClient/Languages and used"))
		{
			Import();
		}
		if (MusicDrawing.TextButton(sb, "language:export", export, "Export texts to translate", MusicPlayerWindow.PanelColor, "Writes every text of IsaClient (English, and the language in use) to IsaClient/Languages/Export, ready to edit"))
		{
			Export();
		}
		Rectangle folder = new Rectangle(area.X, area.Y + 40, half, 32);
		Rectangle reload = new Rectangle(area.X + half + 8, area.Y + 40, half, 32);
		if (MusicDrawing.TextButton(sb, "language:folder", folder, "Open the languages folder", MusicPlayerWindow.PanelColor, "Your language files: a file for a language IsaClient has only changes the texts it lists"))
		{
			Directory.CreateDirectory(L10n.Folder);
			Utils.OpenFolder(L10n.Folder);
		}
		if (MusicDrawing.TextButton(sb, "language:reload", reload, "Read the files again", MusicPlayerWindow.PanelColor, "After you edit a language file"))
		{
			L10n.Reload();
			Note(L10n.ReadError != null ? ("Could not read " + L10n.ReadError) : "Language files read again");
		}
		Rectangle help = new Rectangle(area.X, area.Y + 80, half, 32);
		Rectangle missing = new Rectangle(area.X + half + 8, area.Y + 80, half, 32);
		if (MusicDrawing.TextButton(sb, "language:help", help, "Help translate", Green, "Opens the languages repository: every language is one file anyone can improve"))
		{
			try
			{
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LanguageRepo.RepoUrl) { UseShellExecute = true });
			}
			catch (Exception ex)
			{
				Note("Could not open the browser: " + ex.Message);
			}
		}
		string label = L10n.T("Note untranslated texts") + ": " + L10n.T(L10n.LogMissing ? "on" : "off");
		if (MusicDrawing.TextButton(sb, "language:missing", missing, label, L10n.LogMissing ? MusicPlayerWindow.SelectedColor : MusicPlayerWindow.PanelDarkColor, "Every text shown without a translation is written to IsaClient/Languages/Missing-<language>.txt (for translators)"))
		{
			L10n.LogMissing = !L10n.LogMissing;
			ClientSettings.Save();
		}
		// Downloaded languages are kept here; any can be taken away (it comes back when picked again).
		L10n.Pack current = L10n.CurrentPack;
		if (current != null && LanguageRepo.IsDownloaded(current.Code))
		{
			Rectangle remove = new Rectangle(area.X, area.Y + 120, area.Width, 26);
			if (MusicDrawing.TextButton(sb, "language:redownload", remove, L10n.F("Download {0} again", current.Name), MusicPlayerWindow.PanelDarkColor, "Gets a fresh copy from the repository"))
			{
				LanguageRepo.Download(current.Code);
			}
		}
	}

	private static void Pick(string code)
	{
		SoundEngine.PlaySound(12);
		L10n.Pack pack = string.IsNullOrEmpty(code) ? null : L10n.Find(code);
		L10n.Select(code);
		if (pack != null && (pack.Remote || LanguageRepo.Failure(code) != null))
		{
			// Not here yet: it comes now, and IsaClient switches to it when it arrives.
			LanguageRepo.Download(code);
			Note(L10n.F("Downloading {0}...", pack.Name));
			return;
		}
		Note(string.IsNullOrEmpty(code) ? "IsaClient follows the game's language" : "Language changed");
	}

	#endregion

	#region Import and export

	/// <summary>Windows' file picker on its own thread (the game keeps drawing); the file is checked, copied and used.</summary>
	private static void Import()
	{
		if (_pickerOpen)
		{
			return;
		}
		_pickerOpen = true;
		IntPtr owner = Main.instance.Window.Handle;
		Thread thread = new Thread((ThreadStart)delegate
		{
			string file = null;
			try
			{
				using OpenFileDialog dialog = new OpenFileDialog
				{
					Title = "IsaClient language file",
					Filter = "Language files (*.json)|*.json|All files|*.*"
				};
				if (dialog.ShowDialog(new Owner(owner)) == DialogResult.OK)
				{
					file = dialog.FileName;
				}
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
			Main.QueueMainThreadAction(delegate
			{
				_pickerOpen = false;
				if (file != null)
				{
					ImportFile(file);
				}
			});
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.IsBackground = true;
		thread.Start();
	}

	private sealed class Owner : IWin32Window
	{
		public Owner(IntPtr handle)
		{
			Handle = handle;
		}

		public IntPtr Handle { get; }
	}

	/// <summary>A language file goes to IsaClient/Languages (named by its code) and is used at once.</summary>
	public static void ImportFile(string file)
	{
		L10n.Pack pack = L10n.ReadFile(file);
		if (pack == null || string.IsNullOrWhiteSpace(pack.Code))
		{
			Note("That is not an IsaClient language file (it needs \"Code\" and \"Strings\")");
			return;
		}
		if (pack.Strings.Count == 0)
		{
			Note("That language file has no texts");
			return;
		}
		try
		{
			Directory.CreateDirectory(L10n.Folder);
			string target = Path.Combine(L10n.Folder, Safe(pack.Code) + ".json");
			if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
			{
				File.Copy(file, target, overwrite: true);
			}
			L10n.Reload();
			L10n.Select(pack.Code);
			Note(L10n.F("{0} imported ({1} texts)", pack.Name, pack.Strings.Count));
		}
		catch (Exception ex)
		{
			Note("Could not import it: " + ex.Message);
		}
	}

	/// <summary>
	/// IsaClient/Languages/Export: en.json (every text, the English as the value: the base for a new language) and the
	/// language in use with all its texts (untranslated ones empty), to fix or finish it.
	/// </summary>
	public static void Export()
	{
		try
		{
			string folder = Path.Combine(L10n.Folder, "Export");
			Directory.CreateDirectory(folder);
			Dictionary<string, string> catalog = L10n.Catalog;
			Write(Path.Combine(folder, "en.json"), new L10n.Pack
			{
				Code = "xx",
				Name = "My language",
				EnglishName = "My language",
				Flag = "us",
				Author = "",
				Strings = catalog.Keys.ToDictionary(k => k, k => k)
			});
			L10n.Pack current = L10n.CurrentPack;
			if (current != null && current.Code != L10n.English)
			{
				Write(Path.Combine(folder, Safe(current.Code) + ".json"), new L10n.Pack
				{
					Code = current.Code,
					Name = current.Name,
					EnglishName = current.EnglishName,
					Flag = current.Flag,
					Author = current.Author,
					Strings = catalog.Keys.ToDictionary(k => k, k => current.Strings.TryGetValue(k, out var value) ? value : "")
				});
			}
			File.WriteAllText(Path.Combine(folder, "README.txt"), ReadMe, new UTF8Encoding(false));
			Utils.OpenFolder(folder);
			Note("Texts exported to IsaClient/Languages/Export");
		}
		catch (Exception ex)
		{
			Note("Could not export: " + ex.Message);
		}
	}

	private static void Write(string path, L10n.Pack pack)
	{
		var json = new
		{
			pack.Code,
			pack.Name,
			pack.EnglishName,
			pack.Flag,
			pack.Author,
			Strings = pack.Strings
		};
		File.WriteAllText(path, JsonConvert.SerializeObject(json, Formatting.Indented), new UTF8Encoding(false));
	}

	private static string Safe(string code)
	{
		return new string(code.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
	}

	private const string ReadMe = "IsaClient language files\r\n\r\n"
		+ "en.json has every text IsaClient shows: the English text on the left, its translation on the right.\r\n"
		+ "To make a new language: copy en.json, set Code (like \"it\" or \"pl\"), Name (the language's own name), EnglishName\r\n"
		+ "and Flag (a country code like \"it\"), and write the translations on the right. Put the file in IsaClient/Languages\r\n"
		+ "(or use Import in the language window).\r\n\r\n"
		+ "The other file is the language you use, with every text (empty ones are not translated yet). To fix some\r\n"
		+ "texts of a language IsaClient has, keep only those lines: your file changes them and leaves the rest.\r\n\r\n"
		+ "Keep {0}, {1}... (they are filled by the game), commands like .help, and chat tags like [c/FF0000:text].\r\n"
		+ "In the language window, \"Read the files again\" picks up your changes without restarting.\r\n";

	#endregion

	#region .language

	/// <summary>.language [code | auto | export | import | folder]</summary>
	internal static void LanguageCommand(CommandContext command)
	{
		string rest = (command.Rest ?? "").Trim();
		switch (rest.ToLowerInvariant())
		{
		case "":
			Open();
			return;
		case "auto":
		case "game":
			L10n.Select("");
			ClientChat.Info("IsaClient follows the game's language.");
			return;
		case "export":
			Export();
			return;
		case "import":
			Open();
			Import();
			return;
		case "folder":
			Directory.CreateDirectory(L10n.Folder);
			Utils.OpenFolder(L10n.Folder);
			return;
		}
		L10n.Pack pack = L10n.Find(rest) ?? L10n.Available.FirstOrDefault(p => string.Equals(p.Name, rest, StringComparison.OrdinalIgnoreCase) || string.Equals(p.EnglishName, rest, StringComparison.OrdinalIgnoreCase));
		if (pack == null)
		{
			ClientChat.Error("There is no language called '" + rest + "'. Languages: " + string.Join(", ", L10n.Available.Select(p => p.Code)));
			return;
		}
		L10n.Select(pack.Code);
		ClientChat.Info("Language: " + pack.Name);
	}

	#endregion
}
