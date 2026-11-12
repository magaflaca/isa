using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using IsaClient.Core;
using IsaClient.Games;
using IsaClient.Gba.Trainer;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameInput;
using Terraria.IO;
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace IsaClient.Gba;

/// <summary>
/// The console window: a pixel handheld (D-pad, A/B, L/R, Start/Select that light up as you press them) around the
/// game's screen, a start card (continue / from the game's save), the games and the trainer picker (each Terraria
/// character next to the FireRed sprite it becomes). DS games get a DS-like handheld and their two screens as the
/// player likes them (PokemonWindow.Ds.cs). While it has the keyboard your character stands still.
/// </summary>
public static partial class PokemonWindow
{
	private enum Page
	{
		Console,
		Trainer,
		Controls,
		Saves,
		Games,
		Find
	}

	private static readonly GameOverlay.Panel _panel = new GameOverlay.Panel { Layer = "games:pokemon" };

	private static bool _registered;

	private static bool _open;

	private static bool _focused;

	private static bool _prevMouse;

	private static Page _page;

	private static bool _pickerOpen;

	private static bool _openWhenInWorld;

	public static bool IsOpen => _open;

	public static bool Minimized { get; private set; }

	private const int HeaderH = 46;

	private const int GbaPad = 66;

	private const int GbaSide = 170;

	/// <summary>Room left and right of the screen for the D-pad and the buttons (the DS has four buttons).</summary>
	private static int Pad => DsConsole ? 78 : GbaPad;

	/// <summary>The side panel's width (the DS's shows the other screen).</summary>
	private static int Side => DsConsole ? 200 : GbaSide;

	/// <summary>The DS handheld is on screen (the pages are the same size for every game).</summary>
	private static bool DsConsole => DsShown && _page == Page.Console;

	private static readonly Color Shell = new Color(58, 46, 120);

	private static readonly Color ShellLight = new Color(92, 78, 170);

	private static readonly Color ShellDark = new Color(34, 26, 74);

	private static readonly Color Red = new Color(220, 60, 60);

	#region Opening

	public static void Open()
	{
		if (Main.gameMenu)
		{
			return;
		}
		Ensure();
		if (!_open)
		{
			IsaClient.Games.GamePlays.Count(IsaClient.Games.GamePlays.Gba);
		}
		_open = true;
		Minimized = false;
		_focused = true;
		PokemonGame.LoadRom();
		MusicPlayerWindow.BringSurfaceToFront(_panel.Layer);
	}

	/// <summary>Opens the window on the trainer picker (or the games, when the game in the console has no trainer).</summary>
	public static void OpenTrainer()
	{
		Open();
		if (!_open)
		{
			return;
		}
		if (PokemonGame.SupportsTrainer)
		{
			OpenTrainerPage();
		}
		else
		{
			_page = Page.Games;
		}
	}

	/// <summary>Opens the window on the games list.</summary>
	public static void OpenGames()
	{
		Open();
		if (_open)
		{
			_page = Page.Games;
		}
	}

	public static void Close()
	{
		PokemonGame.Stop();
		_open = false;
		_focused = false;
		_page = Page.Console;
	}

	private static void Ensure()
	{
		if (_registered)
		{
			return;
		}
		_registered = true;
		MusicPlayerWindow.AddSurface(new MusicPlayerWindow.FloatingSurface
		{
			Layer = _panel.Layer,
			Shown = () => _open && !Main.gameMenu,
			ScreenRect = () => _panel.ScreenRect,
			Draw = Draw
		});
		// closing Terraria keeps the moment too
		Main.instance.Exiting += (_, _) => PokemonGame.Stop();
	}

	/// <summary>Game loop (from the games' update).</summary>
	public static void Update()
	{
		if (!_open && _openWhenInWorld && !Main.gameMenu && Main.LocalPlayer != null && Main.LocalPlayer.active)
		{
			_openWhenInWorld = false;
			OpenGames();
		}
		if (!_open)
		{
			PokemonGame.Update(false);
			return;
		}
		if (Main.gameMenu)
		{
			Close();
			return;
		}
		bool mouse = Main.mouseLeft;
		if (mouse && !_prevMouse && !_panel.ScreenRect.Contains(Main.mouseX, Main.mouseY))
		{
			_focused = false;
		}
		_prevMouse = mouse;
		CaptureKey();
		PokemonGame.Update(HasKeys);
	}

	private static bool HasKeys => _focused && !Minimized && _page == Page.Console && Main.hasFocus && !Main.drawingPlayerChat && !Main.editSign && !Main.editChest && PokemonGame.Playing;

	#endregion

	#region Window

	/// <summary>The biggest zoom that fits the screen (set every frame).</summary>
	private static int _maxZoom = 4;

	private const int MinZoom = 2;

	/// <summary>The same zoom for every game (3x by default), lower only when it doesn't fit.</summary>
	private static int Zoom => Math.Max(MinZoom, Math.Min(_maxZoom, Math.Min(4, PokemonGame.Scale)));

	/// <summary>The DS side panel's height (the other screen, the layout and emulator choices).</summary>
	private const int DsSideHeight = 520;

	private static bool DsShown => PokemonGame.IsDs && PokemonGame.HasRom;

	/// <summary>Screen pixels per DS pixel at zoom 1: the GBA's 160 lines over the DS's 192.</summary>
	private const float DsPixel = 160f / 192f;

	/// <summary>The game window's size (set every frame): the DS's screens shrink to fit it.</summary>
	private static int _screenW = 1920;

	private static int _screenH = 1080;

	/// <summary>Everything around the screen, across: the handheld's sides, the side panel and the margins.</summary>
	private static int FrameW => 12 + Pad + 16 + 16 + Pad + 12 + Side + 12;

	/// <summary>Everything around the screen, down: the header, the bezel and the handheld's bottom.</summary>
	private const int FrameH = HeaderH + 8 + 24 + 24 + 54 + 12;

	/// <summary>The DS side panel needs this much height whatever the screens.</summary>
	private static int SideMinH => DsConsole ? (HeaderH + 20 + DsSideHeight) : 0;

	/// <summary>
	/// The screen's size in pixels at a zoom. The GBA's, and the pages (games, saves, controls...) are always a GBA
	/// screen at the zoom, so their size doesn't depend on the DS. The DS's screens are as tall as the GBA's at the same
	/// zoom, smaller only as much as needed for the layout to fit the game window.
	/// </summary>
	private static Point ScreenSize(int zoom)
	{
		if (!DsConsole)
		{
			return new Point(GbaMachine.Width * zoom, GbaMachine.Height * zoom);
		}
		DsLayout layout = PokemonGame.DsLayout;
		int across = (layout == DsLayout.SideBySide) ? 2 : 1;
		int down = (layout == DsLayout.Stacked) ? 2 : 1;
		int gapW = (across == 2) ? Hinge : 0;
		int gapH = (down == 2) ? Hinge : 0;
		float fitW = (_screenW - 8 - FrameW - gapW) / (256f * across);
		float fitH = (_screenH - 8 - FrameH - gapH) / (192f * down);
		float scale = Math.Max(0.5f, Math.Min(zoom * DsPixel, Math.Min(fitW, fitH)));
		int w = (int)(256 * scale);
		int h = (int)(192 * scale);
		return new Point(w * across + gapW, h * down + gapH);
	}

	private static int WidthFor(int zoom) => FrameW + ScreenSize(zoom).X;

	private static int HeightFor(int zoom) => Math.Max(ConsoleHeightFor(zoom), SideMinH);

	/// <summary>What the handheld itself needs (the window may be taller for the DS side panel).</summary>
	private static int ConsoleHeightFor(int zoom) => FrameH + ScreenSize(zoom).Y;

	private static void Draw(SpriteBatch sb, int screenW, int screenH)
	{
		if (Minimized)
		{
			DrawMinimized(sb, screenW, screenH);
			return;
		}
		_screenW = screenW;
		_screenH = screenH;
		// the zoom goes as far as a GBA screen (and the pages) fit; the DS's layouts then fit themselves
		_maxZoom = MinZoom;
		for (int z = MinZoom + 1; z <= 4; z++)
		{
			// the GBA's window at that zoom (the same on every page and for every game, so it never jumps)
			if (12 + GbaPad + 32 + GbaPad + 12 + GbaSide + 12 + GbaMachine.Width * z <= screenW && FrameH + GbaMachine.Height * z <= screenH)
			{
				_maxZoom = z;
			}
		}
		int zoom = Zoom;
		Point size = ScreenSize(zoom);
		int sw = size.X;
		int sh = size.Y;
		int consoleW = Pad + 16 + sw + 16 + Pad;
		int width = WidthFor(zoom);
		int height = HeightFor(zoom);
		Rectangle r = GameOverlay.Place(_panel, width, height, screenW, screenH, 0);
		GameOverlay.Window(sb, r, HeaderH);
		GameOverlay.Grip(sb, r);
		if (DrawHeader(sb, r))
		{
			return;
		}
		Rectangle console = new Rectangle(r.X + 12, r.Y + HeaderH + 8, consoleW, height - HeaderH - 20);
		// a taller window (the DS side panel): the screen stays in the middle of the handheld
		int extra = height - ConsoleHeightFor(zoom);
		Rectangle screen = new Rectangle(console.X + Pad + 16, console.Y + 24 + extra / 2, sw, sh);
		DrawConsole(sb, console, screen);
		Rectangle side = new Rectangle(console.Right + 12, console.Y, Side, console.Height);
		_touchRect = Rectangle.Empty;
		if (_page == Page.Trainer)
		{
			DrawTrainerPage(sb, new Rectangle(console.X, console.Y, console.Width + 12 + Side, console.Height));
		}
		else if (_page == Page.Controls)
		{
			DrawControlsPage(sb, new Rectangle(console.X, console.Y, console.Width + 12 + Side, console.Height));
		}
		else if (_page == Page.Saves)
		{
			DrawSavesPage(sb, new Rectangle(console.X, console.Y, console.Width + 12 + Side, console.Height));
		}
		else if (_page == Page.Games)
		{
			DrawGamesPage(sb, new Rectangle(console.X, console.Y, console.Width + 12 + Side, console.Height));
		}
		else if (_page == Page.Find)
		{
			DrawFindPage(sb, new Rectangle(console.X, console.Y, console.Width + 12 + Side, console.Height));
		}
		else
		{
			DrawScreen(sb, screen);
			DrawSide(sb, side);
		}
		DrawToast(sb, screen);
		UpdateTouch();
		if (_focused && HasKeys)
		{
			// your character stands still while the keys are the console's
			PlayerInput.WritingText = true;
			Main.chatRelease = false;
		}
		if (MusicPlayerWindow.Clicked && MusicPlayerWindow.MouseIn(r))
		{
			_focused = true;
		}
		GameOverlay.Drag(_panel, new Rectangle(r.X, r.Y, r.Width - 110, HeaderH - 4), screenW, screenH);
	}

	private static void DrawMinimized(SpriteBatch sb, int screenW, int screenH)
	{
		PokemonGame.Touch(false, 0f, 0f);
		Rectangle bar = GameOverlay.Place(_panel, 320, 40, screenW, screenH, 0);
		GameOverlay.Window(sb, bar, 18);
		GameIcon(sb, new Rectangle(bar.X + 10, bar.Y + 9, 22, 22));
		string state = PokemonGame.Playing ? "paused" : "off";
		MusicDrawing.RawText(sb, (PokemonGame.HasRom ? PokemonGame.RomName : "GBA") + "  ·  " + state, bar.X + 40, bar.Y + 11, Color.White, 0.74f, bar.Width - 80);
		if (MusicDrawing.TextButton(sb, "pkmn:restore", new Rectangle(bar.Right - 34, bar.Y + 9, 24, 22), "+", MusicPlayerWindow.PanelColor, "Show"))
		{
			Minimized = false;
			_focused = true;
		}
		GameOverlay.Drag(_panel, new Rectangle(bar.X, bar.Y, bar.Width - 40, bar.Height), screenW, screenH);
	}

	private static bool DrawHeader(SpriteBatch sb, Rectangle r)
	{
		GameIcon(sb, new Rectangle(r.X + 12, r.Y + 8, 30, 30));
		string title = PokemonGame.HasRom ? PokemonGame.RomName : "Game Boy Advance  ·  DS";
		MusicDrawing.RawText(sb, title, r.X + 50, r.Y + 9, MusicPlayerWindow.AccentColor, 0.86f, r.Width - 200);
		string sub;
		if (!PokemonGame.HasRom)
		{
			sub = IsaClient.Languages.L10n.T("Bring your own games to play");
		}
		else
		{
			string state = PokemonGame.Playing ? (HasKeys ? IsaClient.Languages.L10n.T("playing") : IsaClient.Languages.L10n.T("click the screen to play")) : IsaClient.Languages.L10n.T("off");
			sub = PokemonGame.IsDs ? ("Nintendo DS  ·  " + PokemonGame.Engine.Name + "  ·  " + state) : state;
			if (PokemonGame.SupportsTrainer)
			{
				PlayerFileData file = PokemonGame.ChosenFile();
				string who = (file?.Player != null) ? IsaClient.Languages.L10n.F("Trainer: {0}", file.Player.name) : IsaClient.Languages.L10n.T("Trainer: the original");
				sub = who + "  ·  " + state;
			}
		}
		MusicDrawing.RawText(sb, sub, r.X + 50, r.Y + 28, MusicPlayerWindow.MutedColor, 0.64f, r.Width - 200);
		int x = r.Right - 34;
		if (MusicDrawing.BareIcon(sb, "pkmn:close", new Rectangle(x, r.Y + 13, 17, 18), MusicDrawing.Tex("Images/MapDeath"), null, "Close (the game keeps this moment)", Color.White))
		{
			Close();
			return true;
		}
		x -= 32;
		if (MusicDrawing.TextButton(sb, "pkmn:min", new Rectangle(x, r.Y + 11, 24, 22), "-", MusicPlayerWindow.PanelColor, "Minimize (pauses the game)"))
		{
			Minimized = true;
			_focused = false;
		}
		x -= 32;
		bool canGrow = _maxZoom > MinZoom;
		string zoomTip = canGrow ? IsaClient.Languages.L10n.F("Screen size (up to {0}x on your screen)", _maxZoom) : IsaClient.Languages.L10n.T("A bigger screen doesn't fit: lower Terraria's UI scale");
		if (MusicDrawing.TextButton(sb, "pkmn:zoom", new Rectangle(x, r.Y + 11, 28, 22), Zoom + "x", MusicPlayerWindow.PanelColor, zoomTip, canGrow))
		{
			PokemonGame.Scale = (Zoom >= _maxZoom) ? MinZoom : (Zoom + 1);
			GbaConfig.Save();
		}
		return false;
	}

	#endregion

	#region The handheld

	/// <summary>The body, the screen's bezel, the D-pad and buttons (lit while held).</summary>
	private static void DrawConsole(SpriteBatch sb, Rectangle body, Rectangle screen)
	{
		if (PokemonGame.IsDs && PokemonGame.HasRom)
		{
			DrawDsConsole(sb, body, screen);
			return;
		}
		int held = PokemonGame.Held;
		bool Down(int bit) => (held & (1 << bit)) != 0;
		// shoulders
		Shoulder(sb, new Rectangle(body.X + 10, body.Y - 2, 90, 14), "L", Down(Libretro.L));
		Shoulder(sb, new Rectangle(body.Right - 100, body.Y - 2, 90, 14), "R", Down(Libretro.R));
		// body: a rounded slab with a light rim and a darker base
		MusicDrawing.Box(sb, new Rectangle(body.X, body.Y + 6, body.Width, body.Height - 6), ShellDark);
		MusicDrawing.Box(sb, new Rectangle(body.X + 2, body.Y + 6, body.Width - 4, body.Height - 10), Shell);
		GameArt.Gradient(sb, new Rectangle(body.X + 8, body.Y + 10, body.Width - 16, 26), ShellLight * 0.55f, Color.Transparent);
		// bezel
		Rectangle bezel = new Rectangle(screen.X - 12, screen.Y - 10, screen.Width + 24, screen.Height + 20);
		MusicDrawing.Box(sb, bezel, new Color(24, 22, 36));
		MusicDrawing.Fill(sb, new Rectangle(screen.X - 2, screen.Y - 2, screen.Width + 4, screen.Height + 4), Color.Black);
		bool on = PokemonGame.Playing;
		MusicDrawing.Fill(sb, new Rectangle(bezel.X + 5, bezel.Bottom - 8, 4, 4), on ? new Color(110, 255, 110) : new Color(60, 70, 60));
		// D-pad
		int cx = body.X + Pad / 2 + 6;
		int cy = screen.Y + screen.Height / 2 - 10;
		DPad(sb, cx, cy, held);
		// A and B
		int bx = body.Right - Pad / 2 - 6;
		RoundButton(sb, new Vector2(bx + 10, cy - 14), "A", Down(Libretro.A));
		RoundButton(sb, new Vector2(bx - 12, cy + 12), "B", Down(Libretro.B));
		// Start / Select under the screen
		int y = bezel.Bottom + 14;
		Pill(sb, new Rectangle(body.Center.X - 74, y, 60, 14), "SELECT", Down(Libretro.Select));
		Pill(sb, new Rectangle(body.Center.X + 14, y, 60, 14), "START", Down(Libretro.Start));
		// speaker grille
		for (int i = 0; i < 4; i++)
		{
			MusicDrawing.Fill(sb, new Rectangle(body.Right - 54 + i * 9, body.Bottom - 26 - i * 3, 4, 14 + i * 3), ShellDark * 0.9f);
		}
		MusicDrawing.RawText(sb, "IsaClient  GBA", body.X + 14, body.Bottom - 22, ShellLight, 0.6f);
	}

	private static void Shoulder(SpriteBatch sb, Rectangle r, string label, bool down)
	{
		MusicDrawing.Box(sb, r, down ? ShellLight : ShellDark);
		float w = MusicDrawing.Measure(label, 0.5f);
		MusicDrawing.RawText(sb, label, r.Center.X - w / 2f, r.Y, down ? Color.White : MusicPlayerWindow.MutedColor, 0.5f);
	}

	private static void DPad(SpriteBatch sb, int cx, int cy, int held)
	{
		const int arm = 16;
		const int len = 18;
		Color baseC = new Color(30, 28, 44);
		Color lit = MusicPlayerWindow.AccentColor;
		Rectangle center = new Rectangle(cx - arm / 2, cy - arm / 2, arm, arm);
		Rectangle up = new Rectangle(center.X, center.Y - len, arm, len);
		Rectangle down = new Rectangle(center.X, center.Bottom, arm, len);
		Rectangle left = new Rectangle(center.X - len, center.Y, len, arm);
		Rectangle right = new Rectangle(center.Right, center.Y, len, arm);
		// drop shadow
		foreach (Rectangle a in new[] { up, down, left, right, center })
		{
			MusicDrawing.Fill(sb, new Rectangle(a.X + 2, a.Y + 3, a.Width, a.Height), Color.Black * 0.35f);
		}
		void Arm(Rectangle a, int bit)
		{
			bool d = (held & (1 << bit)) != 0;
			MusicDrawing.Fill(sb, a, d ? lit * 0.85f : baseC);
		}
		MusicDrawing.Fill(sb, center, baseC);
		Arm(up, Libretro.Up);
		Arm(down, Libretro.Down);
		Arm(left, Libretro.Left);
		Arm(right, Libretro.Right);
		MusicDrawing.Fill(sb, new Rectangle(cx - 3, cy - 3, 6, 6), Color.Black * 0.4f);
	}

	private static void RoundButton(SpriteBatch sb, Vector2 c, string label, bool down)
	{
		GameArt.Disc(sb, c + new Vector2(2f, 3f), 34f, 34f, Color.Black * 0.45f);
		GameArt.Disc(sb, c, 30f, 30f, down ? new Color(255, 120, 120) : new Color(150, 36, 52));
		GameArt.Disc(sb, c - new Vector2(4f, 5f), 12f, 9f, Color.White * (down ? 0.5f : 0.22f));
		float w = MusicDrawing.Measure(label, 0.62f);
		MusicDrawing.RawText(sb, label, c.X - w / 2f, c.Y - 8f, Color.White, 0.62f);
	}

	private static void Pill(SpriteBatch sb, Rectangle r, string label, bool down)
	{
		MusicDrawing.Box(sb, new Rectangle(r.X + 14, r.Y, r.Width - 28, 8), down ? MusicPlayerWindow.AccentColor : new Color(30, 28, 44));
		float w = MusicDrawing.Measure(label, 0.42f);
		MusicDrawing.RawText(sb, label, r.Center.X - w / 2f, r.Y + 8, ShellLight, 0.42f);
	}

	#endregion

	#region The screen

	private static void DrawScreen(SpriteBatch sb, Rectangle screen)
	{
		Texture2D tex = PokemonGame.Screen;
		if (PokemonGame.IsDs && PokemonGame.HasRom && PokemonGame.Playing)
		{
			DrawDsScreens(sb, screen);
			return;
		}
		if (PokemonGame.Playing && tex != null && !tex.IsDisposed)
		{
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, PokemonGame.Smooth ? SamplerState.LinearClamp : SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
			sb.Draw(tex, screen, Color.White);
			GameArt.Smooth(sb);
			if (!HasKeys)
			{
				// not listening: a hint over the corner
				string hint = PokemonGame.Machine?.Paused == true ? IsaClient.Languages.L10n.T("Paused") : IsaClient.Languages.L10n.T("Click to play");
				float w = MusicDrawing.Measure(hint, 0.66f);
				Rectangle tag = new Rectangle(screen.Right - (int)w - 26, screen.Y + 8, (int)w + 18, 24);
				MusicDrawing.Box(sb, tag, Color.Black * 0.6f);
				MusicDrawing.RawText(sb, hint, tag.X + 9, tag.Y + 4, MusicPlayerWindow.AccentColor, 0.66f);
			}
			if (MusicPlayerWindow.Clicked && MusicPlayerWindow.MouseIn(screen))
			{
				MusicPlayerWindow.Clicked = false;
				_focused = true;
			}
			return;
		}
		if (PokemonGame.IsDs && PokemonGame.HasRom)
		{
			DrawDsOff(sb, screen);
			return;
		}
		// off: a night-sky title card
		NightSky(sb, screen);
		if (!PokemonGame.HasRom)
		{
			DrawRomCard(sb, screen);
		}
		else
		{
			DrawStartCard(sb, screen);
		}
	}

	/// <summary>The console off: a night sky with twinkling stars and pixel grass.</summary>
	private static void NightSky(SpriteBatch sb, Rectangle screen)
	{
		GameArt.Gradient(sb, screen, new Color(28, 30, 70), new Color(12, 14, 34));
		double now = GameArt.Now;
		for (int i = 0; i < 26; i++)
		{
			int sx = screen.X + (int)((i * 97 + 13) % screen.Width);
			int sy = screen.Y + (int)((i * 53 + 7) % (screen.Height * 2 / 3));
			float tw = 0.4f + 0.6f * (float)Math.Abs(Math.Sin(now * 1.3 + i));
			MusicDrawing.Fill(sb, new Rectangle(sx, sy, 2, 2), Color.White * tw * 0.8f);
		}
		// grass along the bottom, in pixels
		int cell = Math.Max(2, Zoom) * 4;
		for (int x = screen.X; x < screen.Right; x += cell)
		{
			int hgt = cell * (2 + (x / cell * 7) % 3);
			MusicDrawing.Fill(sb, new Rectangle(x, screen.Bottom - hgt, Math.Min(cell, screen.Right - x), hgt), new Color(40, 110, 60));
			MusicDrawing.Fill(sb, new Rectangle(x, screen.Bottom - hgt, Math.Min(cell, screen.Right - x), cell / 2), new Color(80, 170, 90));
		}
	}

	private static void DrawRomCard(SpriteBatch sb, Rectangle screen)
	{
		int w = Math.Min(420, screen.Width - 30);
		Rectangle card = new Rectangle(screen.Center.X - w / 2, screen.Center.Y - 100, w, 186);
		GameOverlay.MenuPanel(sb, card, new Color(63, 82, 151) * 0.95f, Color.Black);
		Cartridge(sb, new Rectangle(card.Center.X - 50, card.Y + 10, 44, 34), new Color(112, 112, 132), null, "GBA");
		DsCartridge(sb, new Rectangle(card.Center.X + 10, card.Y + 8, 36, 38), null, "DS");
		Tetris.Centered(sb, IsaClient.Languages.L10n.T("Bring your own games"), card, card.Y + 50, MusicPlayerWindow.AccentColor, 0.82f);
		string[] lines = Wrap(IsaClient.Languages.L10n.T("IsaClient doesn't include any game. Add your own Game Boy Advance or Nintendo DS games (.gba, .nds or .zip): they're only read, never changed. In Pokémon FireRed, LeafGreen and Emerald you can play as your Terraria character."), w - 30, 0.6f);
		int y = card.Y + 74;
		foreach (string line in lines)
		{
			Tetris.Centered(sb, line, card, y, Color.White * 0.9f, 0.62f);
			y += 16;
		}
		if (PokemonGame.Problem != null)
		{
			Tetris.Centered(sb, PokemonGame.Problem, card, y + 2, new Color(255, 150, 150), 0.58f);
		}
		if (MusicDrawing.TextButton(sb, "pkmn:rom", new Rectangle(card.Center.X - 90, card.Bottom - 42, 180, 32), _pickerOpen ? "Choosing..." : "Add games", new Color(60, 140, 80), ".gba, .nds or .zip, several at once", !_pickerOpen))
		{
			PickGames();
		}
	}

	private static void DrawStartCard(SpriteBatch sb, Rectangle screen)
	{
		int w = Math.Min(380, screen.Width - 30);
		bool resume = PokemonGame.HasResume && PokemonGame.Resume;
		Rectangle card = new Rectangle(screen.Center.X - w / 2, screen.Center.Y - 104, w, 196);
		GameOverlay.MenuPanel(sb, card, new Color(63, 82, 151) * 0.95f, Color.Black);
		bool trainer = PokemonGame.SupportsTrainer;
		TrainerResult t = PokemonGame.Trainer;
		PlayerFileData file = trainer ? PokemonGame.ChosenFile() : null;
		if (file?.Player != null && t == null && !PokemonGame.Building)
		{
			PokemonGame.BuildFor(PokemonGame.LookOf(file.Player));
		}
		Rectangle sprite = new Rectangle(card.Center.X - 24, card.Y + 6, 48, 72);
		if (!trainer && PokemonGame.IsDs)
		{
			DsCartridge(sb, new Rectangle(card.Center.X - 33, card.Y + 6, 66, 70), DsIcon(PokemonGame.RomPath), PokemonGame.RomName);
		}
		else if (!trainer)
		{
			// other games: their cartridge, with where you left it on the label
			byte[] head = PokemonGame.OriginalRom;
			Texture2D label = (head != null) ? GbaSaves.Picture(PokemonGame.ResumePicture(head)) : null;
			Cartridge(sb, new Rectangle(card.Center.X - 46, card.Y + 8, 92, 68), CartColor(PokemonGame.RomCode), label, PokemonGame.RomName);
		}
		else if (file?.Player != null && t != null)
		{
			TrainerArt.Walk(sb, t, sprite, (int)(GameArt.Now / 1.6) % 4);
		}
		else if (file?.Player != null)
		{
			Tetris.Centered(sb, "...", card, card.Y + 30, MusicPlayerWindow.MutedColor, 0.8f);
		}
		else
		{
			PixelArt.PokeBall(sb, new Rectangle(card.Center.X - 18, card.Y + 18, 36, 36));
		}
		string line = !trainer ? PokemonGame.RomName : ((file?.Player != null) ? file.Player.name : IsaClient.Languages.L10n.F("{0} / {1} (original)", PokemonGame.TrainerFamily.MaleName, PokemonGame.TrainerFamily.FemaleName));
		Tetris.Centered(sb, line, card, card.Y + 80, Color.White, 0.74f);
		string save = PokemonGame.HasSave ? IsaClient.Languages.L10n.T("Your save is kept in IsaClient/GBA.") : IsaClient.Languages.L10n.T("No save yet: a new adventure!");
		if (PokemonGame.IsDs)
		{
			save = IsaClient.Languages.L10n.F("{0}  ·  {1}", PokemonGame.Engine.Name, save);
		}
		Tetris.Centered(sb, save, card, card.Y + 100, MusicPlayerWindow.MutedColor, 0.58f);
		if (PokemonGame.Problem != null)
		{
			Tetris.Centered(sb, PokemonGame.Problem, card, card.Y + 116, new Color(255, 150, 150), 0.56f);
		}
		int bw = (w - 36) / 2;
		Rectangle main = new Rectangle(card.X + 12, card.Bottom - 74, bw, 30);
		Rectangle other = new Rectangle(main.Right + 12, main.Y, bw, 30);
		int third = (w - 48) / 3;
		Rectangle b1 = new Rectangle(card.X + 12, main.Y, third, 30);
		Rectangle b2 = new Rectangle(b1.Right + 12, main.Y, third, 30);
		Rectangle b3 = new Rectangle(b2.Right + 12, main.Y, third, 30);
		if (resume)
		{
			if (MusicDrawing.TextButton(sb, "pkmn:continue", b1, "Continue", new Color(60, 140, 80), "Right where you closed it"))
			{
				_focused = true;
				PokemonGame.Start(true);
			}
		}
		else if (MusicDrawing.TextButton(sb, "pkmn:start", b1, "Play", new Color(60, 140, 80), "Turns the console on: title screen, then your in-game save"))
		{
			_focused = true;
			PokemonGame.Start(false);
		}
		if (MusicDrawing.TextButton(sb, "pkmn:loadgame", b2, "Load game", MusicPlayerWindow.PanelColor, "Your saved games (slots)"))
		{
			_page = Page.Saves;
		}
		if (resume && MusicDrawing.TextButton(sb, "pkmn:boot", b3, "Power on", MusicPlayerWindow.PanelColor, "Turns the console on: title screen, then your in-game save"))
		{
			_focused = true;
			PokemonGame.Start(false);
		}
		if (MusicDrawing.TextButton(sb, "pkmn:games", trainer ? new Rectangle(card.X + 12, card.Bottom - 38, bw, 26) : new Rectangle(card.Center.X - bw / 2, card.Bottom - 38, bw, 26), "Games...", MusicPlayerWindow.PanelColor, "Your games: switch or add more"))
		{
			_page = Page.Games;
		}
		if (trainer && MusicDrawing.TextButton(sb, "pkmn:trainer", new Rectangle(card.X + 24 + bw, card.Bottom - 38, bw, 26), "Trainer...", MusicPlayerWindow.PanelColor, "Play as one of your Terraria characters"))
		{
			OpenTrainerPage();
		}
	}

	private static void DrawToast(SpriteBatch sb, Rectangle screen)
	{
		double age = GameArt.Now - PokemonGame.ToastAt;
		if (PokemonGame.Toast == null || age > 2.2)
		{
			return;
		}
		float a = GameArt.Clamp01((2.2 - age) / 0.4);
		float w = MusicDrawing.Measure(PokemonGame.Toast, 0.7f);
		Rectangle tag = new Rectangle(screen.Center.X - (int)w / 2 - 12, screen.Bottom - 40, (int)w + 24, 28);
		MusicDrawing.Box(sb, tag, Color.Black * 0.75f * a);
		MusicDrawing.RawText(sb, PokemonGame.Toast, tag.X + 12, tag.Y + 5, MusicPlayerWindow.AccentColor * a, 0.7f);
	}

	#endregion

	#region Side panel

	private static void DrawSide(SpriteBatch sb, Rectangle side)
	{
		MusicDrawing.Box(sb, side, MusicPlayerWindow.PanelDarkColor * 0.9f);
		int x = side.X + 10;
		int w = side.Width - 20;
		int y = side.Y + 10;
		// trainer card: the Terraria character and the sprite it became
		bool trainer = PokemonGame.SupportsTrainer;
		PlayerFileData file = trainer ? PokemonGame.ChosenFile() : null;
		Rectangle card = new Rectangle(x, y, w, 84);
		if (PokemonGame.IsDs)
		{
			// the DS: the other screen (or the game's card when it's off)
			card = DrawDsSideTop(sb, x, y, w);
		}
		else
		{
			GameOverlay.MenuPanel(sb, card, new Color(63, 82, 151) * 0.85f, Color.Black);
			if (!trainer)
			{
				byte[] head = PokemonGame.OriginalRom;
				Texture2D label = (head != null) ? GbaSaves.Picture(PokemonGame.ResumePicture(head)) : null;
				Cartridge(sb, new Rectangle(card.Center.X - 44, card.Y + 8, 88, 68), CartColor(PokemonGame.RomCode), label, PokemonGame.RomName);
			}
			else if (file?.Player != null)
			{
				TrainerArt.Portrait(sb, file.Player, new Rectangle(card.X + 6, card.Y + 6, 52, 72));
				TrainerResult t = PokemonGame.Trainer;
				if (t != null)
				{
					TrainerArt.Walk(sb, t, new Rectangle(card.Right - 58, card.Y + 6, 48, 72), (int)(GameArt.Now / 1.6) % 4);
				}
				PixelArt.Arrow(sb, new Vector2(card.Center.X + 2, card.Center.Y));
			}
			else
			{
				PixelArt.PokeBall(sb, new Rectangle(card.Center.X - 18, card.Center.Y - 18, 36, 36));
			}
		}
		y = card.Bottom + 8;
		int halfTop = (w - 6) / 2;
		if (MusicDrawing.TextButton(sb, "pkmn:side:games", trainer ? new Rectangle(x, y, halfTop, 28) : new Rectangle(x, y, w, 28), "Games", MusicPlayerWindow.PanelColor, "Your games: switch or add more"))
		{
			_page = Page.Games;
		}
		if (trainer && MusicDrawing.TextButton(sb, "pkmn:side:trainer", new Rectangle(x + halfTop + 6, y, halfTop, 28), "Trainer", MusicPlayerWindow.PanelColor, "Play as one of your Terraria characters"))
		{
			OpenTrainerPage();
		}
		y += 34;
		bool playing = PokemonGame.Playing;
		y += 4;
		MusicDrawing.Text(sb, "Saved games", x, y, MusicPlayerWindow.MutedColor, 0.6f);
		y += 18;
		int half = (w - 6) / 2;
		if (MusicDrawing.TextButton(sb, "pkmn:qs", new Rectangle(x, y, half, 28), "Save", MusicPlayerWindow.PanelColor, "Save this moment in a slot (or over one)", playing))
		{
			_page = Page.Saves;
		}
		if (MusicDrawing.TextButton(sb, "pkmn:ql", new Rectangle(x + half + 6, y, half, 28), "Load", MusicPlayerWindow.PanelColor, "Go back to one of your saved games"))
		{
			_page = Page.Saves;
		}
		y += 34;
		MusicDrawing.Text(sb, "Volume", x, y, MusicPlayerWindow.MutedColor, 0.6f);
		y += 18;
		if (MusicDrawing.TextButton(sb, "pkmn:vol-", new Rectangle(x, y, 28, 24), "-", MusicPlayerWindow.PanelColor))
		{
			PokemonGame.SetVolume(PokemonGame.Volume - 10);
			GbaConfig.Save();
		}
		Rectangle bar = new Rectangle(x + 34, y + 8, w - 68, 8);
		MusicDrawing.Fill(sb, bar, Color.Black * 0.5f);
		MusicDrawing.Fill(sb, new Rectangle(bar.X, bar.Y, bar.Width * PokemonGame.Volume / 100, bar.Height), MusicPlayerWindow.AccentColor);
		if (MusicDrawing.TextButton(sb, "pkmn:vol+", new Rectangle(x + w - 28, y, 28, 24), "+", MusicPlayerWindow.PanelColor))
		{
			PokemonGame.SetVolume(PokemonGame.Volume + 10);
			GbaConfig.Save();
		}
		y += 32;
		half = (w - 6) / 2;
		if (MusicDrawing.TextButton(sb, "pkmn:reset", new Rectangle(x, y, half, 26), "Reset", MusicPlayerWindow.PanelColor, "Restarts the console (unsaved progress is lost)", playing))
		{
			PokemonGame.Reset();
		}
		if (MusicDrawing.TextButton(sb, "pkmn:power", new Rectangle(x + half + 6, y, half, 26), playing ? "Off" : "On", playing ? new Color(150, 60, 60) : new Color(60, 140, 80), playing ? "Turns it off and keeps this moment" : "Turns it on"))
		{
			if (playing)
			{
				PokemonGame.Stop();
			}
			else if (PokemonGame.HasRom)
			{
				PokemonGame.Start(PokemonGame.HasResume && PokemonGame.Resume);
			}
		}
		y += 34;
		if (PokemonGame.IsDs)
		{
			y = DrawDsSideOptions(sb, x, y, w);
		}
		if (Button(sb, "pkmn:side:controls", x, ref y, w, "Controls...", "Change the keys (PokeMMO's by default)"))
		{
			_page = Page.Controls;
		}
		if (PokemonGame.IsDs)
		{
			DrawDsSideBottom(sb, side, x, y, w);
			return;
		}
		DateTime saved = PokemonGame.Machine?.LastSaved ?? default(DateTime);
		string savedText = (saved != default(DateTime)) ? IsaClient.Languages.L10n.F("Game saved at {0}", saved.ToString("HH:mm")) : (PokemonGame.HasSave ? IsaClient.Languages.L10n.T("Your game save is on disk") : IsaClient.Languages.L10n.T("Not saved in the game yet"));
		MusicDrawing.RawText(sb, savedText, x, y, (saved != default(DateTime)) ? new Color(140, 230, 140) : MusicPlayerWindow.MutedColor, 0.56f, w);
		y += 20;
		(GbaButton b, string n)[] shown = { (GbaButton.Up, "Move"), (GbaButton.A, "A"), (GbaButton.B, "B"), (GbaButton.Start, "Menu"), (GbaButton.Select, "gba::Select"), (GbaButton.Fast, "Fast") };
		foreach (var (b, n) in shown)
		{
			if (y > side.Bottom - 34)
			{
				break;
			}
			string keys = (b == GbaButton.Up) ? (GbaConfig.KeyName(GbaConfig.Bindings[0, 0]) + GbaConfig.KeyName(GbaConfig.Bindings[2, 0]) + GbaConfig.KeyName(GbaConfig.Bindings[1, 0]) + GbaConfig.KeyName(GbaConfig.Bindings[3, 0])) : GbaConfig.KeysOf(b);
			MusicDrawing.RawText(sb, n, x, y, MusicPlayerWindow.MutedColor, 0.56f, 50);
			MusicDrawing.RawText(sb, keys, x + 52, y, Color.White * 0.85f, 0.56f, w - 52);
			y += 15;
		}
		MusicDrawing.RawText(sb, "mGBA core (MPL 2.0)", x, side.Bottom - 18, MusicPlayerWindow.MutedColor * 0.6f, 0.5f, w);
	}

	private static bool Button(SpriteBatch sb, string id, int x, ref int y, int w, string label, string tip)
	{
		bool hit = MusicDrawing.TextButton(sb, id, new Rectangle(x, y, w, 28), label, MusicPlayerWindow.PanelColor, tip);
		y += 34;
		return hit;
	}

	#endregion

	#region Trainer page

	private static string _selected;

	private static float _listScroll;

	private static int _selectedBody;

	private static readonly Color Ink = new Color(26, 30, 58);

	/// <summary>The player sprites of the game in the console (FireRed's when there's none).</summary>
	private static TrainerGame Family => PokemonGame.TrainerFamily ?? TrainerGame.FireRed;

	private static void OpenTrainerPage()
	{
		_page = Page.Trainer;
		_selected = PokemonGame.Character;
		_selectedBody = PokemonGame.Body;
	}

	private static PlayerFileData SelectedFile(List<PlayerFileData> list)
	{
		if (_selected == "original")
		{
			return null;
		}
		if (_selected == "auto" || string.IsNullOrEmpty(_selected))
		{
			return Main.ActivePlayerFileData;
		}
		return list.Find(p => string.Equals(p.Path, _selected, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>The header's icon: a Poké Ball for Pokémon, else the game's cartridge.</summary>
	private static void GameIcon(SpriteBatch sb, Rectangle r)
	{
		string code = PokemonGame.HasRom ? PokemonGame.RomCode : "";
		if (PokemonGame.HasRom && PokemonGame.IsDs)
		{
			Texture2D icon = DsIcon(PokemonGame.RomPath);
			if (icon != null)
			{
				GameArt.Sharp(sb);
				sb.Draw(icon, r, Color.White);
				GameArt.Smooth(sb);
			}
			else
			{
				DsCartridge(sb, r, null, PokemonGame.RomName);
			}
		}
		else if (code.StartsWith("BP"))
		{
			PixelArt.PokeBall(sb, r);
		}
		else
		{
			Cartridge(sb, new Rectangle(r.X + 2, r.Y + 4, r.Width - 4, r.Height - 8), CartColor(code), null, PokemonGame.HasRom ? PokemonGame.RomName : "GBA");
		}
	}

	/// <summary>The window's usual batch again (after a clipped one ended).</summary>
	private static void Resume(SpriteBatch sb)
	{
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
	}

	/// <summary>A page's title row: the title, a line under it and a Back button.</summary>
	private static bool PageHeader(SpriteBatch sb, Rectangle area, string title, string sub)
	{
		MusicDrawing.Box(sb, area, MusicPlayerWindow.PanelDarkColor * 0.97f);
		PixelArt.PokeBall(sb, new Rectangle(area.X + 14, area.Y + 12, 24, 24));
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T(title), area.X + 46, area.Y + 9, MusicPlayerWindow.AccentColor, 0.82f, area.Width - 160);
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T(sub), area.X + 46, area.Y + 28, MusicPlayerWindow.MutedColor, 0.58f, area.Width - 160);
		MusicDrawing.Fill(sb, new Rectangle(area.X + 12, area.Y + 48, area.Width - 24, 2), Color.Black * 0.35f);
		return MusicDrawing.TextButton(sb, "pkmn:pageback", new Rectangle(area.Right - 102, area.Y + 11, 90, 28), "Back", MusicPlayerWindow.PanelColor);
	}

	private static void DrawTrainerPage(SpriteBatch sb, Rectangle area)
	{
		if (PageHeader(sb, area, "Choose your trainer", "Your Terraria character, without armor, becomes the FireRed player."))
		{
			_page = Page.Console;
			return;
		}
		List<PlayerFileData> list = PokemonGame.Characters();
		List<(string key, PlayerFileData file, string label, string sub)> rows = new List<(string, PlayerFileData, string, string)>();
		rows.Add(("auto", Main.ActivePlayerFileData, Main.ActivePlayerFileData?.Player?.name ?? "", IsaClient.Languages.L10n.T("The one you're playing")));
		foreach (PlayerFileData f in list)
		{
			rows.Add((f.Path, f, f.Player.name, IsaClient.Languages.L10n.F("{0}'s body", f.Player.Male ? Family.MaleName : Family.FemaleName)));
		}
		rows.Add(("original", null, Family.MaleName + " / " + Family.FemaleName, IsaClient.Languages.L10n.T("The game as it is")));
		Rectangle listBox = new Rectangle(area.X + 12, area.Y + 58, Math.Min(250, area.Width / 3), area.Height - 70);
		DrawTrainerList(sb, listBox, rows);
		Rectangle right = new Rectangle(listBox.Right + 12, listBox.Y, area.Right - listBox.Right - 24, listBox.Height);
		DrawTrainerPreview(sb, right, list);
	}

	private static void DrawTrainerList(SpriteBatch sb, Rectangle box, List<(string key, PlayerFileData file, string label, string sub)> rows)
	{
		const int rowH = 42;
		const int gap = 4;
		MusicDrawing.Box(sb, box, Color.Black * 0.25f);
		Rectangle inner = new Rectangle(box.X + 4, box.Y + 4, box.Width - 14, box.Height - 8);
		float content = rows.Count * (rowH + gap) - gap;
		if (MusicPlayerWindow.WheelDelta != 0 && MusicPlayerWindow.MouseIn(box))
		{
			_listScroll -= MusicPlayerWindow.WheelDelta / 2f;
			MusicPlayerWindow.WheelDelta = 0;
		}
		_listScroll = MathHelper.Clamp(_listScroll, 0f, Math.Max(0f, content - inner.Height));
		GraphicsDevice gd = Main.instance.GraphicsDevice;
		Rectangle oldScissor = gd.ScissorRectangle;
		sb.End();
		gd.ScissorRectangle = Rectangle.Intersect(MusicPlayerWindow.ToScreen(inner), gd.Viewport.Bounds);
		RasterizerState clip = new RasterizerState { CullMode = CullMode.None, ScissorTestEnable = true };
		void Clipped(SpriteBatch b)
		{
			b.End();
			b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, clip, null, Main.UIScaleMatrix);
		}
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, clip, null, Main.UIScaleMatrix);
		try
		{
			int y = inner.Y - (int)_listScroll;
			foreach (var (key, file, label, sub) in rows)
			{
				Rectangle row = new Rectangle(inner.X, y, inner.Width, rowH);
				y += rowH + gap;
				if (row.Bottom < inner.Y || row.Y > inner.Bottom)
				{
					continue;
				}
				bool chosen = string.Equals(_selected ?? "auto", key, StringComparison.OrdinalIgnoreCase);
				bool hover = MusicPlayerWindow.MouseIn(row) && MusicPlayerWindow.MouseIn(inner);
				ListPanel(sb, row, chosen, hover);
				if (file?.Player != null)
				{
					TrainerArt.Head(sb, file.Player, new Vector2(row.X + 22, row.Center.Y + 1), Clipped);
				}
				else
				{
					PixelArt.PokeBall(sb, new Rectangle(row.X + 9, row.Y + 9, 24, 24));
				}
				MusicDrawing.RawText(sb, label, row.X + 44, row.Y + 5, Color.White, 0.7f, row.Width - 50);
				MusicDrawing.RawText(sb, sub, row.X + 44, row.Y + 23, chosen ? new Color(255, 240, 160) : MusicPlayerWindow.MutedColor, 0.56f, row.Width - 50);
				if (hover && MusicPlayerWindow.Clicked)
				{
					MusicPlayerWindow.Clicked = false;
					_selected = key;
				}
			}
		}
		finally
		{
			sb.End();
			gd.ScissorRectangle = oldScissor;
			Resume(sb);
		}
		// scrollbar
		if (content > inner.Height)
		{
			Rectangle track = new Rectangle(box.Right - 8, inner.Y, 4, inner.Height);
			MusicDrawing.Fill(sb, track, Color.Black * 0.4f);
			int thumbH = Math.Max(24, (int)(inner.Height * inner.Height / content));
			int thumbY = track.Y + (int)((inner.Height - thumbH) * (_listScroll / Math.Max(1f, content - inner.Height)));
			MusicDrawing.Fill(sb, new Rectangle(track.X, thumbY, 4, thumbH), MusicPlayerWindow.MutedColor);
		}
	}

	/// <summary>The stage (the Terraria character turning into the FireRed one), the two big pictures, the body and Use.</summary>
	private static void DrawTrainerPreview(SpriteBatch sb, Rectangle area, List<PlayerFileData> list)
	{
		PlayerFileData sel = SelectedFile(list);
		int bottomH = 40;
		int stageH = Math.Max(130, Math.Min(190, (area.Height - bottomH - 12) / 2));
		Rectangle stage = new Rectangle(area.X, area.Y, area.Width, stageH);
		// a FireRed-like meadow: pale sky, grass with tufts in pixels
		GameArt.Gradient(sb, stage, new Color(150, 200, 240), new Color(214, 236, 250));
		int ground = stage.Bottom - 34;
		MusicDrawing.Fill(sb, new Rectangle(stage.X, ground, stage.Width, stage.Bottom - ground), new Color(112, 176, 112));
		for (int x = stage.X; x < stage.Right - 4; x += 8)
		{
			int k = (x / 8 * 37) % 5;
			MusicDrawing.Fill(sb, new Rectangle(x, ground - (k == 0 ? 4 : 0), 4, 4 + (k == 0 ? 4 : 0)), new Color(80, 150, 80));
			if (k == 2)
			{
				MusicDrawing.Fill(sb, new Rectangle(x + 2, ground + 10 + (x % 3) * 4, 4, 4), new Color(90, 160, 90));
			}
		}
		GameArt.Frame(sb, stage, Ink, 2);
		TrainerResult t = null;
		if (sel?.Player != null)
		{
			TrainerLook look = PokemonGame.LookOf(sel.Player);
			t = TrainerArt.Preview(look, _selectedBody);
			int third = stage.Width / 3;
			Rectangle portrait = new Rectangle(stage.X + third / 2 - 40, ground - 104, 80, 112);
			GameArt.Disc(sb, new Vector2(portrait.Center.X, ground + 4), 60f, 12f, Color.Black * 0.25f);
			TrainerArt.Portrait(sb, sel.Player, portrait);
			PixelArt.Arrow(sb, new Vector2(stage.Center.X, ground - 44));
			Rectangle walk = new Rectangle(stage.Right - third / 2 - 40, ground - 108, 80, 112);
			if (t != null)
			{
				TrainerArt.Walk(sb, t, walk, (int)(GameArt.Now / 1.6) % 4);
			}
			else
			{
				string msg = TrainerArt.PreviewError ?? IsaClient.Languages.L10n.T("Making the sprite...");
				MusicDrawing.RawText(sb, msg, walk.X - 20, walk.Center.Y, (TrainerArt.PreviewError != null) ? new Color(200, 40, 40) : Ink, 0.6f, walk.Width + 40);
			}
			Tag(sb, sel.Player.name, stage.X + 8, stage.Y + 8);
		}
		else
		{
			PixelArt.PokeBall(sb, new Rectangle(stage.Center.X - 30, ground - 70, 60, 60));
			Tag(sb, IsaClient.Languages.L10n.F("{0} or {1}, as the game made them", Family.MaleName, Family.FemaleName), stage.X + 8, stage.Y + 8);
		}
		// the trainer card and the battle back
		int cardsY = stage.Bottom + 10;
		int cardsH = area.Bottom - bottomH - 8 - cardsY;
		int cardW = (area.Width - 10) / 2;
		DrawPictureCard(sb, new Rectangle(area.X, cardsY, cardW, cardsH), "Trainer card", t?.Front, t?.FrontPalette, "front:" + _selected + _selectedBody);
		DrawPictureCard(sb, new Rectangle(area.X + cardW + 10, cardsY, cardW, cardsH), "In battle", t?.Back?[0], t?.BackPalette, "back:" + _selected + _selectedBody);
		// body and use
		int by = area.Bottom - bottomH + 6;
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T("Body"), area.X, by + 6, MusicPlayerWindow.MutedColor, 0.62f);
		string[] bodies = { "Auto", Family.MaleName, Family.FemaleName };
		string[] tips =
		{
			IsaClient.Languages.L10n.F("{0} for men, {1} for women", Family.MaleName, Family.FemaleName),
			IsaClient.Languages.L10n.F("Always {0}'s clothes and pose", Family.MaleName),
			IsaClient.Languages.L10n.F("Always {0}'s clothes and pose", Family.FemaleName)
		};
		int bx = area.X + 44;
		for (int i = 0; i < 3; i++)
		{
			Rectangle chip = new Rectangle(bx, by, 58, 28);
			bool on = _selectedBody == i;
			if (Choice(sb, "pkmn:body" + i, chip, bodies[i], on, tips[i]))
			{
				_selectedBody = i;
			}
			bx += 62;
		}
		int useW = Math.Min(190, area.Right - bx - 10);
		if (useW > 80 && MusicDrawing.TextButton(sb, "pkmn:use", new Rectangle(area.Right - useW, by, useW, 28), "Use this trainer", new Color(60, 140, 80)))
		{
			bool changed = PokemonGame.Character != (_selected ?? "auto") || PokemonGame.Body != _selectedBody;
			PokemonGame.Character = _selected ?? "auto";
			PokemonGame.Body = _selectedBody;
			GbaConfig.Save();
			_page = Page.Console;
			if (changed && PokemonGame.Playing)
			{
				PokemonGame.ApplyCharacter();
			}
		}
	}

	private static void Tag(SpriteBatch sb, string text, int x, int y)
	{
		float w = MusicDrawing.Measure(text, 0.64f);
		Rectangle r = new Rectangle(x, y, (int)w + 16, 22);
		MusicDrawing.Box(sb, r, Ink * 0.8f);
		MusicDrawing.RawText(sb, text, x + 8, y + 3, Color.White, 0.64f);
	}

	/// <summary>A cream FireRed frame with a 64x64 picture at a whole-pixel zoom.</summary>
	private static void DrawPictureCard(SpriteBatch sb, Rectangle r, string label, byte[,] idx, Rgb[] pal, string key)
	{
		MusicDrawing.Box(sb, r, new Color(248, 248, 232));
		GameArt.Frame(sb, r, Ink, 2);
		MusicDrawing.Fill(sb, new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, 20), new Color(96, 128, 200));
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T(label), r.X + 8, r.Y + 3, Color.White, 0.6f, r.Width - 16);
		int room = Math.Min(r.Width - 16, r.Height - 30);
		int k = Math.Max(1, room / 64);
		int size = (room >= 64) ? 64 * k : room;
		Rectangle pic = new Rectangle(r.Center.X - size / 2, r.Y + 24 + (r.Height - 28 - size) / 2, size, size);
		if (idx != null && pal != null)
		{
			TrainerArt.Picture(sb, idx, pal, pic, key, false);
		}
		else if (_selected == "original")
		{
			Tetris.Centered(sb, IsaClient.Languages.L10n.T("As in the game"), r, pic.Center.Y - 8, Ink * 0.6f, 0.6f);
		}
	}

	#endregion

	#region Games page

	private sealed class GameEntry
	{
		public string Path;

		public byte[] Head;

		public string Title = "";

		public string Code = "";

		public bool Trainer;

		public bool Missing;

		public bool Ds;
	}

	private static readonly Dictionary<string, GameEntry> _entries = new Dictionary<string, GameEntry>(StringComparer.OrdinalIgnoreCase);

	private static float _gamesScroll;

	private static GameEntry Entry(string path)
	{
		if (_entries.TryGetValue(path, out GameEntry e))
		{
			return e;
		}
		e = new GameEntry { Path = path };
		e.Head = PokemonGame.ReadHead(path);
		if (e.Head == null || PokemonGame.CheckRom(e.Head) != null)
		{
			e.Missing = true;
			e.Title = System.IO.Path.GetFileNameWithoutExtension(path);
		}
		else if (DsRom.IsDs(e.Head))
		{
			e.Ds = true;
			e.Title = DsRom.Title(path, e.Head);
			e.Code = DsRom.Code(e.Head);
		}
		else
		{
			e.Title = PokemonGame.GameTitle(e.Head);
			e.Code = FireRedRom.GameCode(e.Head);
			e.Trainer = TrainerGame.For(e.Head) != null;
		}
		_entries[path] = e;
		return e;
	}

	/// <summary>The color of a game's cartridge (by its code).</summary>
	private static Color CartColor(string code)
	{
		if (code.StartsWith("BPR"))
		{
			return new Color(196, 58, 48);
		}
		if (code.StartsWith("BPG"))
		{
			return new Color(70, 150, 72);
		}
		if (code.StartsWith("BPE"))
		{
			return new Color(36, 150, 112);
		}
		if (code.StartsWith("AX4"))
		{
			return new Color(222, 172, 40);
		}
		if (code.StartsWith("BZM"))
		{
			return new Color(92, 128, 52);
		}
		return new Color(112, 112, 132);
	}

	/// <summary>A GBA cartridge in pixels: the shell, its grip ridges and a label with the game's picture.</summary>
	private static void Cartridge(SpriteBatch sb, Rectangle r, Color shell, Texture2D label, string title)
	{
		Color dark = Color.Lerp(shell, Color.Black, 0.35f);
		Color light = Color.Lerp(shell, Color.White, 0.25f);
		int notch = r.Width / 6;
		// shell with a cut corner and a darker base
		MusicDrawing.Fill(sb, new Rectangle(r.X + 2, r.Y + 3, r.Width, r.Height), Color.Black * 0.35f);
		MusicDrawing.Fill(sb, new Rectangle(r.X, r.Y, r.Width - notch, r.Height), shell);
		MusicDrawing.Fill(sb, new Rectangle(r.Right - notch, r.Y + notch / 2, notch, r.Height - notch / 2), shell);
		MusicDrawing.Fill(sb, new Rectangle(r.X, r.Bottom - r.Height / 8, r.Width, r.Height / 8), dark);
		MusicDrawing.Fill(sb, new Rectangle(r.X, r.Y, r.Width - notch, 2), light);
		// grip ridges on top
		for (int i = 0; i < 5; i++)
		{
			MusicDrawing.Fill(sb, new Rectangle(r.X + 8 + i * 6, r.Y + 5, 3, r.Height / 10), dark);
		}
		// the label
		Rectangle lab = new Rectangle(r.X + r.Width / 10, r.Y + r.Height / 5, r.Width * 8 / 10, r.Height * 11 / 20);
		MusicDrawing.Fill(sb, new Rectangle(lab.X - 2, lab.Y - 2, lab.Width + 4, lab.Height + 4), dark);
		if (label != null)
		{
			GameArt.Sharp(sb);
			sb.Draw(label, lab, Color.White);
			GameArt.Smooth(sb);
		}
		else
		{
			GameArt.Gradient(sb, lab, Color.Lerp(shell, Color.White, 0.55f), Color.Lerp(shell, Color.White, 0.15f));
			string initials = Initials(title);
			float s = Math.Min(1.4f, lab.Height / 40f);
			float tw = MusicDrawing.Measure(initials, s);
			MusicDrawing.RawText(sb, initials, lab.Center.X - tw / 2f, lab.Center.Y - 14f * s, Color.White, s);
		}
		GameArt.Frame(sb, lab, Color.Black * 0.5f, 1);
	}

	private static string Initials(string title)
	{
		string result = "";
		foreach (string w in title.Replace(":", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
		{
			if (char.IsLetterOrDigit(w[0]) && result.Length < 3)
			{
				result += char.ToUpperInvariant(w[0]);
			}
		}
		return (result.Length > 0) ? result : "GBA";
	}

	private static void DrawGamesPage(SpriteBatch sb, Rectangle area)
	{
		if (PageHeader(sb, area, "Games", "Your Game Boy Advance and DS games. Each one keeps its own saves. Pokémon GBA games can use your Terraria character."))
		{
			_page = Page.Console;
			return;
		}
		List<string> games = GbaConfig.Games;
		Rectangle box = new Rectangle(area.X + 12, area.Y + 58, area.Width - 24, area.Height - 70);
		const int cardW = 190;
		const int cardH = 196;
		const int gap = 12;
		int cols = Math.Max(1, (box.Width + gap) / (cardW + gap));
		int count = games.Count + 2;
		int rows = (count + cols - 1) / cols;
		float content = rows * (cardH + gap) - gap;
		if (MusicPlayerWindow.WheelDelta != 0 && MusicPlayerWindow.MouseIn(box))
		{
			_gamesScroll -= MusicPlayerWindow.WheelDelta / 2f;
			MusicPlayerWindow.WheelDelta = 0;
		}
		_gamesScroll = MathHelper.Clamp(_gamesScroll, 0f, Math.Max(0f, content - box.Height));
		int left = box.X + (box.Width - (cols * (cardW + gap) - gap)) / 2;
		GraphicsDevice gd = Main.instance.GraphicsDevice;
		Rectangle oldScissor = gd.ScissorRectangle;
		RasterizerState clip = new RasterizerState { CullMode = CullMode.None, ScissorTestEnable = true };
		sb.End();
		gd.ScissorRectangle = Rectangle.Intersect(MusicPlayerWindow.ToScreen(box), gd.Viewport.Bounds);
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, clip, null, Main.UIScaleMatrix);
		string remove = null;
		// The cartridge labels are drawn sharp (the batch restarts): clipped as well.
		GameArt.Raster = clip;
		try
		{
			for (int i = 0; i < count; i++)
			{
				Rectangle card = new Rectangle(left + (i % cols) * (cardW + gap), box.Y + (i / cols) * (cardH + gap) - (int)_gamesScroll, cardW, cardH);
				if (card.Bottom < box.Y || card.Y > box.Bottom)
				{
					continue;
				}
				if (i == games.Count)
				{
					DrawAddCard(sb, card, box);
					continue;
				}
				if (i == games.Count + 1)
				{
					DrawFindCard(sb, card, box);
					continue;
				}
				if (DrawGameCard(sb, card, box, Entry(games[i])))
				{
					remove = games[i];
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
		// scrollbar, when there are more games than fit
		if (content > box.Height)
		{
			Rectangle track = new Rectangle(box.Right - 4, box.Y, 4, box.Height);
			MusicDrawing.Fill(sb, track, Color.Black * 0.4f);
			int thumbH = Math.Max(24, (int)(box.Height * box.Height / content));
			int thumbY = track.Y + (int)((box.Height - thumbH) * (_gamesScroll / Math.Max(1f, content - box.Height)));
			MusicDrawing.Fill(sb, new Rectangle(track.X, thumbY, 4, thumbH), MusicPlayerWindow.MutedColor);
		}
		if (remove != null)
		{
			PokemonGame.RemoveGame(remove);
			_entries.Remove(remove);
		}
	}

	/// <summary>One game: its cartridge, name and badges. Click to put it in the console. True when it's removed.</summary>
	private static bool DrawGameCard(SpriteBatch sb, Rectangle card, Rectangle box, GameEntry e)
	{
		bool current = string.Equals(e.Path, PokemonGame.RomPath, StringComparison.OrdinalIgnoreCase);
		bool hover = MusicPlayerWindow.MouseIn(card) && MusicPlayerWindow.MouseIn(box);
		ListPanel(sb, card, current, hover);
		float lift = hover ? 3f : 0f;
		Rectangle cart = new Rectangle(card.X + 25, card.Y + 12 - (int)lift, card.Width - 50, 104);
		if (e.Ds)
		{
			DsCartridge(sb, new Rectangle(card.Center.X - 49, card.Y + 10 - (int)lift, 98, 104), DsIcon(e.Path), e.Title);
		}
		else
		{
			Texture2D label = (e.Head != null && !e.Missing) ? GbaSaves.Picture(PokemonGame.ResumePicture(e.Head)) : null;
			Cartridge(sb, cart, e.Missing ? new Color(70, 70, 80) : CartColor(e.Code), label, e.Title);
		}
		MusicDrawing.RawText(sb, e.Title, card.X + 10, card.Y + 124, Color.White, 0.66f, card.Width - 20);
		int cy = card.Y + 146;
		int cx = card.X + 10;
		if (e.Missing)
		{
			MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T("File not found"), cx, cy, new Color(255, 150, 150), 0.56f, card.Width - 20);
		}
		else
		{
			if (current)
			{
				cx = GameOverlay.Chip(sb, IsaClient.Languages.L10n.T(PokemonGame.Playing ? "Playing" : "In the console"), cx, cy, new Color(60, 140, 80), Color.White, card.Right - 10);
			}
			if (e.Trainer)
			{
				GameOverlay.Chip(sb, IsaClient.Languages.L10n.T("Your trainer"), cx, cy, new Color(150, 60, 70), Color.White, card.Right - 10);
			}
			else if (e.Ds)
			{
				GameOverlay.Chip(sb, "DS", cx, cy, new Color(70, 92, 150), Color.White, card.Right - 10);
			}
		}
		int by = card.Bottom - 30;
		bool removed = false;
		if (!e.Missing && MusicDrawing.TextButton(sb, "pkmn:game:play:" + e.Path, new Rectangle(card.X + 10, by, card.Width - 50, 24), current && PokemonGame.Playing ? "Back to it" : "Play", new Color(60, 140, 80)))
		{
			bool wasCurrent = current;
			if (PokemonGame.SelectGame(e.Path))
			{
				_page = Page.Console;
				_focused = true;
				if (!(wasCurrent && PokemonGame.Playing))
				{
					PokemonGame.Start(PokemonGame.HasResume && PokemonGame.Resume);
				}
			}
		}
		Rectangle del = new Rectangle(card.Right - 34, by, 24, 24);
		string ask = "game:" + e.Path;
		bool asking = _confirmGame == ask && GameArt.Now < _confirmUntil;
		if (MusicDrawing.TextButton(sb, "pkmn:game:del:" + e.Path, del, asking ? "?" : "x", asking ? new Color(160, 50, 50) : MusicPlayerWindow.PanelColor, asking ? "Click again to take it out (its saves stay)" : "Take it out of the list"))
		{
			if (asking)
			{
				_confirmGame = null;
				removed = true;
			}
			else
			{
				_confirmGame = ask;
				_confirmUntil = GameArt.Now + 3.0;
			}
		}
		else if (hover && !e.Missing && MusicPlayerWindow.Clicked && !MusicPlayerWindow.MouseIn(new Rectangle(card.X, by, card.Width, 24)))
		{
			MusicPlayerWindow.Clicked = false;
			PokemonGame.SelectGame(e.Path);
		}
		return removed;
	}

	private static string _confirmGame;

	private static double _confirmUntil;

	/// <summary>The last card: add .gba or .zip files.</summary>
	private static void DrawAddCard(SpriteBatch sb, Rectangle card, Rectangle box)
	{
		bool hover = MusicPlayerWindow.MouseIn(card) && MusicPlayerWindow.MouseIn(box);
		MusicDrawing.Box(sb, card, hover ? new Color(52, 64, 120) : new Color(34, 42, 82));
		// dashed border
		Color dash = MusicPlayerWindow.MutedColor * (hover ? 0.9f : 0.5f);
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
		Vector2 c = new Vector2(card.Center.X, card.Y + 70);
		MusicDrawing.Fill(sb, new Rectangle((int)c.X - 18, (int)c.Y - 4, 36, 8), dash);
		MusicDrawing.Fill(sb, new Rectangle((int)c.X - 4, (int)c.Y - 18, 8, 36), dash);
		Tetris.Centered(sb, IsaClient.Languages.L10n.T(_pickerOpen ? "Choosing..." : "Add games"), card, card.Y + 110, Color.White, 0.72f);
		Tetris.Centered(sb, IsaClient.Languages.L10n.T(".gba, .nds or .zip, several at once"), card, card.Y + 132, MusicPlayerWindow.MutedColor, 0.56f);
		Tetris.Centered(sb, IsaClient.Languages.L10n.T("You bring the games: none come with IsaClient."), card, card.Y + 150, MusicPlayerWindow.MutedColor * 0.8f, 0.5f);
		if (hover && MusicPlayerWindow.Clicked && !_pickerOpen)
		{
			MusicPlayerWindow.Clicked = false;
			PickGames();
		}
	}

	#endregion

	#region Saves page

	/// <summary>A button that asks twice (overwrite, delete): which slot and what, until when.</summary>
	private static (int slot, string what, double until) _confirm;

	private static bool Confirmed(int slot, string what)
	{
		if (_confirm.slot == slot && _confirm.what == what && GameArt.Now < _confirm.until)
		{
			_confirm = default;
			return true;
		}
		_confirm = (slot, what, GameArt.Now + 3.0);
		return false;
	}

	private static bool Asking(int slot, string what) => _confirm.slot == slot && _confirm.what == what && GameArt.Now < _confirm.until;

	private static void DrawSavesPage(SpriteBatch sb, Rectangle area)
	{
		if (PageHeader(sb, area, "Saved games", "Each slot keeps the exact moment and the game's own save. Load one to go back to it."))
		{
			_page = Page.Console;
			return;
		}
		bool playing = PokemonGame.Playing;
		int top = area.Y + 58;
		int cols = 2;
		int rows = (GbaSaves.Count + cols - 1) / cols;
		int gap = 8;
		int cardW = (area.Width - 24 - gap * (cols - 1)) / cols;
		int cardH = Math.Min(130, (area.Bottom - 12 - top - gap * (rows - 1)) / rows);
		for (int i = 1; i <= GbaSaves.Count; i++)
		{
			int c = (i - 1) % cols;
			int r = (i - 1) / cols;
			DrawSlot(sb, new Rectangle(area.X + 12 + c * (cardW + gap), top + r * (cardH + gap), cardW, cardH), i, playing);
		}
	}

	private static void DrawSlot(SpriteBatch sb, Rectangle card, int i, bool playing)
	{
		SaveSlot slot = GbaSaves.Info(i);
		bool hover = MusicPlayerWindow.MouseIn(card);
		MusicDrawing.Box(sb, card, slot.Used ? (hover ? new Color(62, 78, 146) : new Color(50, 64, 124)) : new Color(38, 46, 88));
		// the picture, 3:2 like the GBA's screen (4:3 for the DS)
		int aw = PokemonGame.IsDs ? 4 : 3;
		int ah = PokemonGame.IsDs ? 3 : 2;
		int th = card.Height - 12;
		int tw = Math.Min(th * aw / ah, card.Width / 2 - 10);
		th = tw * ah / aw;
		Rectangle thumb = new Rectangle(card.X + 6, card.Y + (card.Height - th) / 2, tw, th);
		MusicDrawing.Fill(sb, new Rectangle(thumb.X - 2, thumb.Y - 2, thumb.Width + 4, thumb.Height + 4), Color.Black);
		Texture2D tex = slot.Used ? GbaSaves.Thumb(i) : null;
		if (tex != null)
		{
			GameArt.Sharp(sb);
			sb.Draw(tex, thumb, Color.White);
			GameArt.Smooth(sb);
		}
		else
		{
			MusicDrawing.Fill(sb, thumb, new Color(20, 24, 46));
			if (slot.Used && !PokemonGame.IsDs)
			{
				PixelArt.PokeBall(sb, new Rectangle(thumb.Center.X - 14, thumb.Center.Y - 14, 28, 28));
			}
			else
			{
				Tetris.Centered(sb, IsaClient.Languages.L10n.T("Empty"), thumb, thumb.Center.Y - 8, MusicPlayerWindow.MutedColor * 0.7f, 0.6f);
			}
		}
		int x = thumb.Right + 10;
		int w = card.Right - x - 8;
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.F("Slot {0}", i), x, card.Y + 6, MusicPlayerWindow.AccentColor, 0.72f, w);
		if (slot.Used)
		{
			MusicDrawing.RawText(sb, slot.Trainer, x, card.Y + 26, Color.White, 0.62f, w);
			MusicDrawing.RawText(sb, slot.When.ToString("dd/MM/yyyy  HH:mm"), x, card.Y + 42, MusicPlayerWindow.MutedColor, 0.56f, w);
		}
		else
		{
			MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T("Nothing saved here"), x, card.Y + 26, MusicPlayerWindow.MutedColor, 0.58f, w);
		}
		int by = card.Bottom - 32;
		int bw = Math.Max(56, (w - 34) / 2);
		bool busy = GbaSaves.Busy >= 0;
		if (slot.Used && MusicDrawing.TextButton(sb, "pkmn:slot:load" + i, new Rectangle(x, by, bw, 26), "Load", new Color(60, 140, 80), playing ? "Go back to this moment (what you haven't saved is lost)" : "Turn the console on at this moment", !busy))
		{
			GbaSaves.Load(i);
			_page = Page.Console;
			_focused = true;
		}
		string saveLabel = Asking(i, "save") ? "Overwrite?" : (slot.Used ? "Save here" : "Save");
		Rectangle saveBtn = slot.Used ? new Rectangle(x + bw + 4, by, bw, 26) : new Rectangle(x, by, bw, 26);
		if (MusicDrawing.TextButton(sb, "pkmn:slot:save" + i, saveBtn, saveLabel, Asking(i, "save") ? new Color(160, 110, 40) : MusicPlayerWindow.PanelColor, playing ? (slot.Used ? "Click twice to save over this slot" : "Save this moment here") : "Turn the console on to save", playing && !busy))
		{
			if (!slot.Used || Confirmed(i, "save"))
			{
				GbaSaves.Save(i);
			}
		}
		if (slot.Used)
		{
			Rectangle del = new Rectangle(card.Right - 30, by, 24, 26);
			if (MusicDrawing.TextButton(sb, "pkmn:slot:del" + i, del, Asking(i, "delete") ? "?" : "x", Asking(i, "delete") ? new Color(160, 50, 50) : MusicPlayerWindow.PanelColor, Asking(i, "delete") ? "Click again to delete it" : "Delete this save", !busy))
			{
				if (Confirmed(i, "delete"))
				{
					GbaSaves.Delete(i);
				}
			}
		}
		if (GbaSaves.Busy == i)
		{
			MusicDrawing.Box(sb, card, Color.Black * 0.45f);
			Tetris.Centered(sb, IsaClient.Languages.L10n.T("Saving..."), card, card.Center.Y - 8, Color.White, 0.7f);
		}
	}

	#endregion

	#region Controls page

	private static GbaButton? _listenButton;

	private static int _listenSlot;

	private static Microsoft.Xna.Framework.Input.KeyboardState _prevKeys;

	/// <summary>While a slot waits for a key: the first key pressed goes there (Escape cancels).</summary>
	private static void CaptureKey()
	{
		Microsoft.Xna.Framework.Input.KeyboardState keys = Main.keyState;
		if (_listenButton.HasValue && _page == Page.Controls && Main.hasFocus)
		{
			foreach (Microsoft.Xna.Framework.Input.Keys k in keys.GetPressedKeys())
			{
				if (_prevKeys.IsKeyDown(k))
				{
					continue;
				}
				if (k != Microsoft.Xna.Framework.Input.Keys.Escape)
				{
					GbaConfig.Bind(_listenButton.Value, _listenSlot, k);
				}
				_listenButton = null;
				break;
			}
		}
		else if (_page != Page.Controls)
		{
			_listenButton = null;
		}
		_prevKeys = keys;
	}

	private static void DrawControlsPage(SpriteBatch sb, Rectangle area)
	{
		if (PageHeader(sb, area, "Controls", "Click a key, then press the new one. Right click empties it, Esc cancels."))
		{
			_page = Page.Console;
			_listenButton = null;
			return;
		}
		if (_listenButton.HasValue)
		{
			// the key you press is for the list, not for your character
			PlayerInput.WritingText = true;
			Main.chatRelease = false;
		}
		GbaButton[][] columns =
		{
			new[] { GbaButton.Up, GbaButton.Down, GbaButton.Left, GbaButton.Right, GbaButton.Start, GbaButton.Select, GbaButton.Swap },
			new[] { GbaButton.A, GbaButton.B, GbaButton.X, GbaButton.Y, GbaButton.L, GbaButton.R, GbaButton.Fast }
		};
		int colW = (area.Width - 36) / 2;
		const int rowH = 36;
		for (int c = 0; c < 2; c++)
		{
			int x = area.X + 12 + c * (colW + 12);
			int y = area.Y + 62;
			foreach (GbaButton b in columns[c])
			{
				Rectangle row = new Rectangle(x, y, colW, rowH - 4);
				MusicDrawing.Box(sb, row, new Color(46, 58, 110));
				ButtonGlyph(sb, b, new Rectangle(row.X + 6, row.Y + 3, 26, 26));
				MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T(GbaConfig.Names[(int)b]), row.X + 40, row.Y + 7, Color.White, 0.66f, 110);
				int slotW = Math.Max(60, (row.Width - 160) / 2);
				for (int s = 0; s < 2; s++)
				{
					Rectangle slot = new Rectangle(row.Right - (2 - s) * (slotW + 6), row.Y + 3, slotW, rowH - 10);
					bool listening = _listenButton == b && _listenSlot == s;
					string label = listening ? "..." : GbaConfig.KeyName(GbaConfig.Bindings[(int)b, s]);
					Color back = listening ? new Color(150, 120, 40) : MusicPlayerWindow.PanelColor;
					if (MusicDrawing.TextButton(sb, "pkmn:key:" + b + s, slot, label, back, listening ? "Press a key (Esc cancels)" : "Click, then press a key"))
					{
						_listenButton = b;
						_listenSlot = s;
					}
					if (MusicPlayerWindow.RightClicked && MusicPlayerWindow.MouseIn(slot))
					{
						GbaConfig.Bind(b, s, Microsoft.Xna.Framework.Input.Keys.None);
					}
				}
				y += rowH;
			}
		}
		int by = area.Bottom - 46;
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T("Gamepad: D-pad or stick, A, B, X, Y, LB / RB, Start, Back = Select, RT = fast forward, LT = swap the DS screens."), area.X + 14, by - 26, MusicPlayerWindow.MutedColor, 0.58f, area.Width - 28);
		if (MusicDrawing.TextButton(sb, "pkmn:keys:reset", new Rectangle(area.X + 12, by, 220, 32), "PokeMMO keys", MusicPlayerWindow.PanelColor, "WASD / arrows, Z = A, X = B, C / V = X / Y, Enter = Start, Backspace = Select, Q / E = L / R, Space = fast, Tab = swap screens"))
		{
			GbaConfig.ResetKeys();
			GbaConfig.Save();
			_listenButton = null;
		}
		if (MusicDrawing.TextButton(sb, "pkmn:keys:done", new Rectangle(area.Right - 152, by, 140, 32), "Done", new Color(60, 140, 80)))
		{
			_page = Page.Console;
			_listenButton = null;
		}
	}

	/// <summary>A little picture of a console button for the controls list.</summary>
	private static void ButtonGlyph(SpriteBatch sb, GbaButton b, Rectangle r)
	{
		Vector2 c = new Vector2(r.Center.X, r.Center.Y);
		switch (b)
		{
		case GbaButton.A:
		case GbaButton.B:
			GameArt.Disc(sb, c, r.Width, r.Height, new Color(150, 36, 52));
			Centered(sb, b.ToString(), c, 0.56f);
			break;
		case GbaButton.X:
		case GbaButton.Y:
			GameArt.Disc(sb, c, r.Width, r.Height, new Color(70, 76, 96));
			Centered(sb, b.ToString(), c, 0.56f);
			break;
		case GbaButton.Swap:
			MusicDrawing.Box(sb, new Rectangle(r.X + 6, r.Y + 1, r.Width - 12, r.Height / 2 - 2), new Color(30, 28, 44));
			MusicDrawing.Box(sb, new Rectangle(r.X + 6, r.Center.Y + 1, r.Width - 12, r.Height / 2 - 2), MusicPlayerWindow.AccentColor * 0.8f);
			break;
		case GbaButton.L:
		case GbaButton.R:
			MusicDrawing.Box(sb, new Rectangle(r.X, r.Y + 6, r.Width, r.Height - 12), ShellDark);
			Centered(sb, b.ToString(), c, 0.5f);
			break;
		case GbaButton.Start:
		case GbaButton.Select:
			MusicDrawing.Box(sb, new Rectangle(r.X, r.Center.Y - 4, r.Width, 8), new Color(30, 28, 44));
			break;
		case GbaButton.Fast:
			Centered(sb, ">>", c, 0.62f);
			break;
		default:
		{
			MusicDrawing.Fill(sb, new Rectangle(r.Center.X - 4, r.Y + 2, 8, r.Height - 4), new Color(30, 28, 44));
			MusicDrawing.Fill(sb, new Rectangle(r.X + 2, r.Center.Y - 4, r.Width - 4, 8), new Color(30, 28, 44));
			Rectangle lit = b switch
			{
				GbaButton.Up => new Rectangle(r.Center.X - 4, r.Y + 2, 8, 8),
				GbaButton.Down => new Rectangle(r.Center.X - 4, r.Bottom - 10, 8, 8),
				GbaButton.Left => new Rectangle(r.X + 2, r.Center.Y - 4, 8, 8),
				_ => new Rectangle(r.Right - 10, r.Center.Y - 4, 8, 8)
			};
			MusicDrawing.Fill(sb, lit, MusicPlayerWindow.AccentColor);
			break;
		}
		}
	}

	private static void Centered(SpriteBatch sb, string text, Vector2 c, float scale)
	{
		float w = MusicDrawing.Measure(text, scale);
		MusicDrawing.RawText(sb, text, c.X - w / 2f, c.Y - 9f * scale / 0.6f, Color.White, scale);
	}

	#endregion

	#region Helpers

	/// <summary>
	/// A panel of Terraria's lists (the character and world selection): blue, lighter under the mouse, and with the
	/// gold border when it's the chosen one.
	/// </summary>
	internal static void ListPanel(SpriteBatch sb, Rectangle r, bool chosen, bool hover)
	{
		Color back = chosen ? new Color(73, 94, 171) : (hover ? new Color(68, 88, 160) : new Color(63, 82, 151) * 0.7f);
		Color border = chosen ? Main.OurFavoriteColor : (hover ? Main.OurFavoriteColor * 0.45f : Color.Black);
		GameOverlay.MenuPanel(sb, r, back, border);
	}

	/// <summary>One of a few options (a button): the chosen one gets Terraria's gold panel border.</summary>
	internal static bool Choice(SpriteBatch sb, string id, Rectangle r, string label, bool on, string tip)
	{
		bool hit = MusicDrawing.TextButton(sb, id, r, label, on ? MusicPlayerWindow.SelectedColor : MusicPlayerWindow.PanelColor, tip);
		if (on)
		{
			Utils.DrawSplicedPanel(sb, MusicDrawing.Tex("Images/UI/PanelBorder"), r.X, r.Y, r.Width, r.Height, 10, 10, 10, 10, Main.OurFavoriteColor);
		}
		return hit;
	}

	internal static string[] Wrap(string text, float width, float scale)
	{
		List<string> lines = new List<string>();
		string line = "";
		foreach (string word in text.Split(' '))
		{
			string tryLine = (line.Length == 0) ? word : (line + " " + word);
			if (MusicDrawing.Measure(tryLine, scale) > width && line.Length > 0)
			{
				lines.Add(line);
				line = word;
			}
			else
			{
				line = tryLine;
			}
		}
		if (line.Length > 0)
		{
			lines.Add(line);
		}
		return lines.ToArray();
	}

	/// <summary>Windows' file picker on its own thread (the game keeps drawing): .gba or .zip, several at once.</summary>
	private static void PickGames()
	{
		if (_pickerOpen)
		{
			return;
		}
		_pickerOpen = true;
		IntPtr owner = Main.instance.Window.Handle;
		Thread thread = new Thread(() =>
		{
			string[] files = null;
			try
			{
				using OpenFileDialog dialog = new OpenFileDialog
				{
					Title = "Add Game Boy Advance and DS games",
					Filter = "GBA and DS games (*.gba, *.nds, *.zip)|*.gba;*.nds;*.srl;*.zip|All files|*.*",
					Multiselect = true
				};
				if (dialog.ShowDialog(new Owner(owner)) == DialogResult.OK)
				{
					files = dialog.FileNames;
				}
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
			Main.QueueMainThreadAction(() =>
			{
				_pickerOpen = false;
				if (files == null)
				{
					return;
				}
				AddPicked(files);
			});
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.IsBackground = true;
		thread.Start();
	}

	/// <summary>Adds the chosen files to the library and puts the first new game in the console (if nothing is playing).</summary>
	private static string AddPicked(string[] files)
	{
		string first = PokemonGame.AddGames(files);
		if (first != null)
		{
			if (!PokemonGame.Playing || !PokemonGame.HasRom)
			{
				PokemonGame.SelectGame(first);
			}
			PokemonGame.ShowToast(IsaClient.Languages.L10n.T("Games added"));
		}
		return first;
	}

	/// <summary>
	/// .gba/.nds/.zip files dropped on the game window: added like "Add games" and the Games page opens.
	/// On the main menu they're added now and the window opens once a world is entered.
	/// </summary>
	public static bool AddDroppedGames(string[] files)
	{
		string first = AddPicked(files);
		if (first == null)
		{
			return false;
		}
		if (Main.gameMenu)
		{
			_openWhenInWorld = true;
		}
		else
		{
			OpenGames();
		}
		return true;
	}

	private sealed class Owner : IWin32Window
	{
		public Owner(IntPtr handle)
		{
			Handle = handle;
		}

		public IntPtr Handle { get; }
	}

	#endregion
}
