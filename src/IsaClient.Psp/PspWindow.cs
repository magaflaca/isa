using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using IsaClient.Core;
using IsaClient.Games;
using IsaClient.Gba;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameInput;
using Color = Microsoft.Xna.Framework.Color;
using Keys = Microsoft.Xna.Framework.Input.Keys;
using KeyboardState = Microsoft.Xna.Framework.Input.KeyboardState;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace IsaClient.Psp;

/// <summary>
/// The PSP's window: a black handheld (D-pad, stick, the four face buttons, L/R, Start/Select, all lit as you press them)
/// around the wide screen, what to do with the game when it's off, and the games, saved games and controls pages. The
/// window's size only depends on its zoom: every page and every game looks the same size. While it has the keyboard your
/// character stands still.
/// </summary>
public static partial class PspWindow
{
	private enum Page
	{
		Console,
		Controls,
		Saves,
		Games,
		Find
	}

	private static readonly GameOverlay.Panel _panel = new GameOverlay.Panel { Layer = "games:psp" };

	private static bool _registered;

	private static bool _open;

	private static bool _focused;

	private static bool _prevMouse;

	private static Page _page;

	private static bool _pickerOpen;

	private static bool _openWhenInWorld;

	public static bool IsOpen => _open;

	public static bool Minimized { get; private set; }

	/// <summary>The game fills Terraria's window (Esc goes back). The mouse isn't drawn then.</summary>
	public static bool FullScreen { get; private set; }

	/// <summary>When it went full screen, for the hint that fades.</summary>
	private static double _fullScreenAt;

	private const int HeaderH = 46;

	/// <summary>Room at each side of the screen for the D-pad and the stick, and for the face buttons.</summary>
	private const int Pad = 104;

	private const int Side = 190;

	/// <summary>Everything around the screen, across and down.</summary>
	private const int FrameW = 12 + Pad + 16 + 16 + Pad + 12 + Side + 12;

	private const int FrameH = HeaderH + 8 + 22 + 22 + 44 + 12;

	/// <summary>What the side panel needs, whatever the zoom.</summary>
	private const int MinHeight = HeaderH + 20 + 412;

	private const int MinZoom = 2;

	private static int _maxZoom = 4;

	private static int Zoom => Math.Max(MinZoom, Math.Min(_maxZoom, Math.Min(4, PspGame.Scale)));

	private static readonly Color Body = new Color(30, 31, 38);

	private static readonly Color BodyLight = new Color(62, 64, 76);

	private static readonly Color BodyDark = new Color(14, 14, 18);

	private static readonly Color Key = new Color(46, 47, 56);

	private static readonly Color Green = new Color(60, 140, 80);

	private static readonly Color CardBlue = new Color(63, 82, 151);

	private static readonly Color Bad = new Color(255, 150, 150);

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
			GamePlays.Count(GamePlays.Gba);
		}
		_open = true;
		Minimized = false;
		_focused = true;
		PspConfig.EnsureLoaded();
		MusicPlayerWindow.BringSurfaceToFront(_panel.Layer);
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
		PspGame.Stop();
		_open = false;
		_focused = false;
		FullScreen = false;
		_page = Page.Console;
	}

	private static void EnterFullScreen()
	{
		FullScreen = true;
		_fullScreenAt = GameArt.Now;
		_focused = true;
		MusicPlayerWindow.BringSurfaceToFront(_panel.Layer);
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
			ScreenRect = () => FullScreen ? new Rectangle(0, 0, Main.screenWidth, Main.screenHeight) : _panel.ScreenRect,
			Draw = Draw
		});
		// closing Terraria keeps the moment too
		Main.instance.Exiting += (_, _) => PspGame.StopNow();
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
			PspGame.Update(false);
			return;
		}
		if (Main.gameMenu)
		{
			Close();
			return;
		}
		if (FullScreen && (!PspGame.Playing || (Main.hasFocus && Main.keyState.IsKeyDown(Keys.Escape) && !_prevKeys.IsKeyDown(Keys.Escape))))
		{
			FullScreen = false;
		}
		bool mouse = Main.mouseLeft;
		if (mouse && !_prevMouse && !FullScreen && !_panel.ScreenRect.Contains(Main.mouseX, Main.mouseY))
		{
			_focused = false;
		}
		_prevMouse = mouse;
		CaptureKey();
		PspGame.Update(HasKeys);
	}

	private static bool HasKeys => _focused && !Minimized && _page == Page.Console && Main.hasFocus && !Main.drawingPlayerChat && !Main.editSign && !Main.editChest && PspGame.Playing;

	#endregion

	#region Window

	private static void Draw(SpriteBatch sb, int screenW, int screenH)
	{
		if (FullScreen)
		{
			DrawFullScreen(sb, screenW, screenH);
			return;
		}
		if (Minimized)
		{
			DrawMinimized(sb, screenW, screenH);
			return;
		}
		_maxZoom = MinZoom;
		for (int z = MinZoom + 1; z <= 4; z++)
		{
			if (FrameW + PspGame.ScreenW * z <= screenW && Math.Max(FrameH + PspGame.ScreenH * z, MinHeight) <= screenH)
			{
				_maxZoom = z;
			}
		}
		int zoom = Zoom;
		int sw = PspGame.ScreenW * zoom;
		int sh = PspGame.ScreenH * zoom;
		int width = FrameW + sw;
		int height = Math.Max(FrameH + sh, MinHeight);
		Rectangle r = GameOverlay.Place(_panel, width, height, screenW, screenH, 0);
		GameOverlay.Window(sb, r, HeaderH);
		GameOverlay.Grip(sb, r);
		if (DrawHeader(sb, r))
		{
			return;
		}
		int consoleW = Pad + 16 + sw + 16 + Pad;
		Rectangle console = new Rectangle(r.X + 12, r.Y + HeaderH + 8, consoleW, height - HeaderH - 20);
		int extra = height - (FrameH + sh);
		Rectangle screen = new Rectangle(console.X + Pad + 16, console.Y + 22 + extra / 2, sw, sh);
		Rectangle page = new Rectangle(console.X, console.Y, console.Width + 12 + Side, console.Height);
		switch (_page)
		{
		case Page.Controls:
			DrawControlsPage(sb, page);
			break;
		case Page.Saves:
			DrawSavesPage(sb, page);
			break;
		case Page.Games:
			DrawGamesPage(sb, page);
			break;
		case Page.Find:
			DrawFindPage(sb, page);
			break;
		default:
			DrawConsole(sb, console, screen);
			DrawScreen(sb, screen);
			DrawSide(sb, new Rectangle(console.Right + 12, console.Y, Side, console.Height));
			break;
		}
		DrawToast(sb, screen);
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
		GameOverlay.Drag(_panel, new Rectangle(r.X, r.Y, r.Width - 150, HeaderH - 4), screenW, screenH);
	}

	/// <summary>The game as big as it fits, its shape kept, on black.</summary>
	private static void DrawFullScreen(SpriteBatch sb, int screenW, int screenH)
	{
		MusicDrawing.Fill(sb, new Rectangle(0, 0, screenW, screenH), Color.Black);
		float scale = Math.Min(screenW / (float)PspGame.ScreenW, screenH / (float)PspGame.ScreenH);
		int w = (int)(PspGame.ScreenW * scale);
		int h = (int)(PspGame.ScreenH * scale);
		Rectangle screen = new Rectangle((screenW - w) / 2, (screenH - h) / 2, w, h);
		Texture2D tex = PspGame.Screen;
		if (tex != null && !tex.IsDisposed)
		{
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
			sb.Draw(tex, screen, Color.White);
			GameArt.Smooth(sb);
		}
		bool paused = PspGame.Machine?.Paused == true;
		double age = GameArt.Now - _fullScreenAt;
		if (paused || age < 3.5)
		{
			string hint = IsaClient.Languages.L10n.T(paused ? "Paused" : "Esc to leave full screen");
			float a = paused ? 1f : GameArt.Clamp01((3.5 - age) / 0.5);
			float tw = MusicDrawing.Measure(hint, 0.74f);
			Rectangle tag = new Rectangle(screenW / 2 - (int)tw / 2 - 14, 18, (int)tw + 28, 30);
			MusicDrawing.Box(sb, tag, Color.Black * 0.7f * a);
			MusicDrawing.RawText(sb, hint, tag.X + 14, tag.Y + 6, MusicPlayerWindow.AccentColor * a, 0.74f);
		}
		DrawToast(sb, screen);
		// the keys stay the console's, and the mouse does nothing here
		PlayerInput.WritingText = true;
		Main.chatRelease = false;
		MusicPlayerWindow.Clicked = false;
	}

	private static void DrawMinimized(SpriteBatch sb, int screenW, int screenH)
	{
		Rectangle bar = GameOverlay.Place(_panel, 320, 40, screenW, screenH, 0);
		GameOverlay.Window(sb, bar, 18);
		Handheld(sb, new Rectangle(bar.X + 8, bar.Y + 13, 30, 14));
		string state = IsaClient.Languages.L10n.T(PspGame.Playing ? "paused" : "off");
		MusicDrawing.RawText(sb, (PspGame.HasGame ? PspGame.Title : "PSP") + "  ·  " + state, bar.X + 46, bar.Y + 11, Color.White, 0.74f, bar.Width - 86);
		if (MusicDrawing.TextButton(sb, "psp:restore", new Rectangle(bar.Right - 34, bar.Y + 9, 24, 22), "+", MusicPlayerWindow.PanelColor, "Show"))
		{
			Minimized = false;
			_focused = true;
		}
		GameOverlay.Drag(_panel, new Rectangle(bar.X, bar.Y, bar.Width - 40, bar.Height), screenW, screenH);
	}

	private static bool DrawHeader(SpriteBatch sb, Rectangle r)
	{
		Handheld(sb, new Rectangle(r.X + 12, r.Y + 14, 34, 16));
		string title = PspGame.HasGame ? PspGame.Title : "PlayStation Portable";
		MusicDrawing.RawText(sb, title, r.X + 54, r.Y + 9, MusicPlayerWindow.AccentColor, 0.86f, r.Width - 250);
		string sub;
		if (!PspGame.HasGame)
		{
			sub = IsaClient.Languages.L10n.T("Bring your own games to play");
		}
		else if (PspGame.Booting)
		{
			sub = IsaClient.Languages.L10n.T("turning on...");
		}
		else
		{
			sub = IsaClient.Languages.L10n.T(PspGame.Playing ? (HasKeys ? "playing" : "click the screen to play") : "off");
		}
		MusicDrawing.RawText(sb, sub, r.X + 54, r.Y + 28, MusicPlayerWindow.MutedColor, 0.64f, r.Width - 250);
		int x = r.Right - 34;
		if (MusicDrawing.BareIcon(sb, "psp:close", new Rectangle(x, r.Y + 13, 17, 18), MusicDrawing.Tex("Images/MapDeath"), null, "Close (the game keeps this moment)", Color.White))
		{
			Close();
			return true;
		}
		x -= 32;
		if (MusicDrawing.TextButton(sb, "psp:min", new Rectangle(x, r.Y + 11, 24, 22), "-", MusicPlayerWindow.PanelColor, "Minimize (pauses the game)"))
		{
			Minimized = true;
			_focused = false;
		}
		x -= 32;
		bool canGrow = _maxZoom > MinZoom;
		string tip = canGrow ? IsaClient.Languages.L10n.F("Screen size (up to {0}x on your screen)", _maxZoom) : IsaClient.Languages.L10n.T("A bigger screen doesn't fit: lower Terraria's UI scale");
		if (MusicDrawing.TextButton(sb, "psp:zoom", new Rectangle(x, r.Y + 11, 28, 22), Zoom + "x", MusicPlayerWindow.PanelColor, tip, canGrow))
		{
			PspGame.Scale = (Zoom >= _maxZoom) ? MinZoom : (Zoom + 1);
			PspConfig.Save();
		}
		x -= 36;
		Rectangle full = new Rectangle(x, r.Y + 11, 28, 22);
		bool canFill = PspGame.Playing;
		if (MusicDrawing.TextButton(sb, "psp:full", full, "", MusicPlayerWindow.PanelColor, canFill ? "Full screen (Esc to leave)" : "Full screen: turn the console on first", canFill))
		{
			EnterFullScreen();
		}
		// four corners
		Color corner = Color.White * (canFill ? 0.9f : 0.35f);
		Rectangle mark = new Rectangle(full.X + 7, full.Y + 5, 14, 12);
		foreach (Point c in new[] { new Point(mark.X, mark.Y), new Point(mark.Right - 5, mark.Y), new Point(mark.X, mark.Bottom - 2), new Point(mark.Right - 5, mark.Bottom - 2) })
		{
			MusicDrawing.Fill(sb, new Rectangle(c.X, c.Y, 5, 2), corner);
		}
		foreach (Point c in new[] { new Point(mark.X, mark.Y), new Point(mark.Right - 2, mark.Y), new Point(mark.X, mark.Bottom - 5), new Point(mark.Right - 2, mark.Bottom - 5) })
		{
			MusicDrawing.Fill(sb, new Rectangle(c.X, c.Y, 2, 5), corner);
		}
		return false;
	}

	private static void DrawToast(SpriteBatch sb, Rectangle screen)
	{
		double age = GameArt.Now - PspGame.ToastAt;
		if (PspGame.Toast == null || age > 2.2)
		{
			return;
		}
		float a = GameArt.Clamp01((2.2 - age) / 0.4);
		string text = IsaClient.Languages.L10n.T(PspGame.Toast);
		float w = MusicDrawing.Measure(text, 0.7f);
		Rectangle tag = new Rectangle(screen.Center.X - (int)w / 2 - 12, screen.Bottom - 40, (int)w + 24, 28);
		MusicDrawing.Box(sb, tag, Color.Black * 0.75f * a);
		MusicDrawing.RawText(sb, text, tag.X + 12, tag.Y + 5, MusicPlayerWindow.AccentColor * a, 0.7f);
	}

	#endregion

	#region The handheld

	/// <summary>A little PSP: for the title bar and the hub.</summary>
	internal static void Handheld(SpriteBatch sb, Rectangle r)
	{
		MusicDrawing.Fill(sb, new Rectangle(r.X + 2, r.Y, r.Width - 4, r.Height), BodyLight);
		MusicDrawing.Fill(sb, new Rectangle(r.X, r.Y + 2, r.Width, r.Height - 4), BodyLight);
		MusicDrawing.Fill(sb, new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), Body);
		int side = Math.Max(3, r.Width / 5);
		MusicDrawing.Fill(sb, new Rectangle(r.X + side, r.Y + 2, r.Width - side * 2, r.Height - 4), new Color(56, 92, 170));
	}

	private static void DrawConsole(SpriteBatch sb, Rectangle body, Rectangle screen)
	{
		int held = PspGame.Held;
		bool Down(int bit) => (held & (1 << bit)) != 0;
		// the shoulders peek out over the top
		Shoulder(sb, new Rectangle(body.X + 14, body.Y - 2, 84, 12), "L", Down(Libretro.L));
		Shoulder(sb, new Rectangle(body.Right - 98, body.Y - 2, 84, 12), "R", Down(Libretro.R));
		// a black slab: a sheen over the top and a thin light rim
		MusicDrawing.Box(sb, new Rectangle(body.X, body.Y + 6, body.Width, body.Height - 6), BodyDark);
		MusicDrawing.Box(sb, new Rectangle(body.X + 2, body.Y + 6, body.Width - 4, body.Height - 10), Body);
		GameArt.Gradient(sb, new Rectangle(body.X + 10, body.Y + 10, body.Width - 20, 30), BodyLight * 0.5f, Color.Transparent);
		// the glass goes across the screen, as on the real thing
		Rectangle bezel = new Rectangle(screen.X - 10, screen.Y - 8, screen.Width + 20, screen.Height + 16);
		MusicDrawing.Box(sb, bezel, new Color(8, 8, 11));
		MusicDrawing.Fill(sb, new Rectangle(screen.X - 2, screen.Y - 2, screen.Width + 4, screen.Height + 4), Color.Black);
		// the D-pad up high, the stick under it
		int leftX = body.X + Pad / 2 + 6;
		int top = screen.Y + screen.Height / 2 - 46;
		DPad(sb, leftX, top, held);
		Nub(sb, leftX, top + 96);
		// the face buttons
		int rightX = body.Right - Pad / 2 - 6;
		int cy = screen.Y + screen.Height / 2 - 12;
		Face(sb, new Vector2(rightX, cy - 30), 0, Down(Libretro.X));
		Face(sb, new Vector2(rightX + 30, cy), 1, Down(Libretro.A));
		Face(sb, new Vector2(rightX, cy + 30), 2, Down(Libretro.B));
		Face(sb, new Vector2(rightX - 30, cy), 3, Down(Libretro.Y));
		// the power light, and Select and Start under the screen
		bool on = PspGame.Playing;
		MusicDrawing.Fill(sb, new Rectangle(body.Right - 34, body.Bottom - 26, 10, 4), on ? new Color(110, 255, 130) : new Color(50, 58, 54));
		int y = bezel.Bottom + 12;
		Pill(sb, new Rectangle(body.Center.X - 74, y, 60, 14), "SELECT", Down(Libretro.Select));
		Pill(sb, new Rectangle(body.Center.X + 14, y, 60, 14), "START", Down(Libretro.Start));
		MusicDrawing.RawText(sb, "IsaClient  PSP", body.X + 16, body.Bottom - 24, BodyLight * 1.6f, 0.6f);
	}

	private static void Shoulder(SpriteBatch sb, Rectangle r, string label, bool down)
	{
		MusicDrawing.Box(sb, r, down ? BodyLight : BodyDark);
		float w = MusicDrawing.Measure(label, 0.46f);
		MusicDrawing.RawText(sb, label, r.Center.X - w / 2f, r.Y - 1, down ? Color.White : MusicPlayerWindow.MutedColor, 0.46f);
	}

	/// <summary>Four separate keys around a hollow middle.</summary>
	private static void DPad(SpriteBatch sb, int cx, int cy, int held)
	{
		const int wide = 17;
		const int len = 22;
		const int gap = 7;
		Rectangle up = new Rectangle(cx - wide / 2, cy - gap - len, wide, len);
		Rectangle down = new Rectangle(cx - wide / 2, cy + gap, wide, len);
		Rectangle left = new Rectangle(cx - gap - len, cy - wide / 2, len, wide);
		Rectangle right = new Rectangle(cx + gap, cy - wide / 2, len, wide);
		GameArt.Disc(sb, new Vector2(cx, cy), 82f, 82f, BodyDark);
		void Arm(Rectangle a, int bit)
		{
			bool d = (held & (1 << bit)) != 0;
			MusicDrawing.Fill(sb, new Rectangle(a.X + 1, a.Y + 2, a.Width, a.Height), Color.Black * 0.5f);
			MusicDrawing.Fill(sb, a, d ? MusicPlayerWindow.AccentColor * 0.9f : Key);
		}
		Arm(up, Libretro.Up);
		Arm(down, Libretro.Down);
		Arm(left, Libretro.Left);
		Arm(right, Libretro.Right);
	}

	/// <summary>The stick: a well, and the nub moving with the keys.</summary>
	private static void Nub(SpriteBatch sb, int cx, int cy)
	{
		Vector2 center = new Vector2(cx, cy);
		GameArt.Disc(sb, center, 40f, 40f, BodyDark);
		Vector2 at = center + PspGame.Stick * 8f;
		bool moved = PspGame.Stick != Vector2.Zero;
		GameArt.Disc(sb, at + new Vector2(1f, 2f), 26f, 26f, Color.Black * 0.5f);
		GameArt.Disc(sb, at, 25f, 25f, moved ? new Color(96, 98, 112) : new Color(68, 70, 82));
		GameArt.Disc(sb, at - new Vector2(3f, 3f), 9f, 7f, Color.White * 0.14f);
	}

	/// <summary>A face button with its mark: 0 triangle, 1 circle, 2 cross, 3 square.</summary>
	private static void Face(SpriteBatch sb, Vector2 c, int mark, bool down)
	{
		GameArt.Disc(sb, c + new Vector2(1f, 2f), 29f, 29f, Color.Black * 0.5f);
		GameArt.Disc(sb, c, 27f, 27f, down ? new Color(106, 108, 124) : Key);
		Mark(sb, c, mark, 6.5f, Color.Lerp(MarkColor(mark), Color.White, down ? 0.6f : 0f));
	}

	private static Color MarkColor(int mark) => mark switch
	{
		0 => new Color(90, 210, 170),
		1 => new Color(236, 96, 96),
		2 => new Color(130, 160, 240),
		_ => new Color(232, 130, 190)
	};

	private static void Mark(SpriteBatch sb, Vector2 c, int mark, float s, Color color)
	{
		switch (mark)
		{
		case 0:
		{
			Vector2 a = c + new Vector2(0f, -s);
			Vector2 b = c + new Vector2(s, s * 0.75f);
			Vector2 d = c + new Vector2(-s, s * 0.75f);
			GameArt.Line(sb, a, b, 1.6f, color);
			GameArt.Line(sb, b, d, 1.6f, color);
			GameArt.Line(sb, d, a, 1.6f, color);
			break;
		}
		case 1:
			GameArt.Disc(sb, c, s * 2.1f, s * 2.1f, color);
			GameArt.Disc(sb, c, s * 1.45f, s * 1.45f, Key);
			break;
		case 2:
			GameArt.Line(sb, c + new Vector2(-s, -s), c + new Vector2(s, s), 1.8f, color);
			GameArt.Line(sb, c + new Vector2(-s, s), c + new Vector2(s, -s), 1.8f, color);
			break;
		default:
			GameArt.Frame(sb, new Rectangle((int)(c.X - s * 0.9f), (int)(c.Y - s * 0.9f), (int)(s * 1.8f), (int)(s * 1.8f)), color, 2);
			break;
		}
	}

	private static void Pill(SpriteBatch sb, Rectangle r, string label, bool down)
	{
		MusicDrawing.Box(sb, new Rectangle(r.X + 14, r.Y, r.Width - 28, 8), down ? MusicPlayerWindow.AccentColor : Key);
		float w = MusicDrawing.Measure(label, 0.42f);
		MusicDrawing.RawText(sb, label, r.Center.X - w / 2f, r.Y + 8, BodyLight * 1.6f, 0.42f);
	}

	#endregion

	#region The screen

	private static void DrawScreen(SpriteBatch sb, Rectangle screen)
	{
		Texture2D tex = PspGame.Screen;
		if (PspGame.Playing && !PspGame.Booting && tex != null && !tex.IsDisposed)
		{
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
			sb.Draw(tex, screen, Color.White);
			GameArt.Smooth(sb);
			if (!HasKeys)
			{
				string hint = IsaClient.Languages.L10n.T(PspGame.Machine?.Paused == true ? "Paused" : "Click to play");
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
		Waves(sb, screen);
		PspEntry game = PspGame.Current;
		Texture2D back = (game?.Background != null) ? GbaSaves.Picture(game.Background) : null;
		if (back != null)
		{
			GameArt.Sharp(sb);
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
			sb.Draw(back, screen, Color.White * 0.55f);
			GameArt.Smooth(sb);
		}
		if (PspGame.Booting)
		{
			DrawBooting(sb, screen);
		}
		else if (PspLibrary.ImportBusy)
		{
			DrawImportCard(sb, screen);
		}
		else if (!PspGame.HasGame)
		{
			DrawWelcomeCard(sb, screen);
		}
		else if (!PspEmulator.Installed)
		{
			DrawInstallCard(sb, screen);
		}
		else
		{
			DrawStartCard(sb, screen);
		}
	}

	/// <summary>The console off: slow waves of light, like the PSP's own menu.</summary>
	private static void Waves(SpriteBatch sb, Rectangle screen)
	{
		GameArt.Gradient(sb, screen, new Color(22, 26, 70), new Color(66, 34, 104));
		float t = (float)GameArt.Now;
		for (int wave = 0; wave < 4; wave++)
		{
			float mid = screen.Y + screen.Height * (0.48f + wave * 0.075f);
			float amp = screen.Height * (0.07f + wave * 0.018f);
			float speed = 0.28f + wave * 0.09f;
			Color color = Color.White * (0.2f - wave * 0.035f);
			Vector2 last = Vector2.Zero;
			const int steps = 32;
			for (int i = 0; i <= steps; i++)
			{
				float u = i / (float)steps;
				float y = mid + (float)Math.Sin(u * 5.2f + t * speed + wave * 1.7f) * amp + (float)Math.Sin(u * 2.3f - t * speed * 0.6f) * amp * 0.6f;
				Vector2 p = new Vector2(screen.X + u * screen.Width, y);
				if (i > 0)
				{
					GameArt.Line(sb, last, p, 2f, color);
				}
				last = p;
			}
		}
	}

	private static void DrawBooting(SpriteBatch sb, Rectangle screen)
	{
		string text = IsaClient.Languages.L10n.T("Turning on...");
		float w = MusicDrawing.Measure(text, 0.8f);
		int dots = (int)(GameArt.Now * 3.0) % 4;
		Rectangle tag = new Rectangle(screen.Center.X - (int)w / 2 - 16, screen.Bottom - 56, (int)w + 32, 34);
		MusicDrawing.Box(sb, tag, Color.Black * 0.6f);
		MusicDrawing.RawText(sb, text, tag.X + 16, tag.Y + 7, MusicPlayerWindow.AccentColor, 0.8f);
		for (int i = 0; i < 3; i++)
		{
			MusicDrawing.Fill(sb, new Rectangle(tag.Center.X - 16 + i * 12, tag.Bottom + 6, 6, 6), Color.White * ((i < dots) ? 0.9f : 0.25f));
		}
	}

	private static void DrawImportCard(SpriteBatch sb, Rectangle screen)
	{
		int w = Math.Min(420, screen.Width - 30);
		Rectangle card = new Rectangle(screen.Center.X - w / 2, screen.Center.Y - 56, w, 112);
		GameOverlay.MenuPanel(sb, card, CardBlue * 0.95f, Color.Black);
		ImportBar(sb, card);
	}

	/// <summary>An archive being copied out: its name, a bar and that it takes a moment.</summary>
	private static void ImportBar(SpriteBatch sb, Rectangle card)
	{
		Tetris.Centered(sb, IsaClient.Languages.L10n.T("Adding the game..."), card, card.Y + 12, MusicPlayerWindow.AccentColor, 0.76f);
		string name = PspLibrary.Importing ?? "";
		MusicDrawing.RawText(sb, name, card.X + 18, card.Y + 36, Color.White * 0.85f, 0.58f, card.Width - 36);
		Rectangle bar = new Rectangle(card.X + 18, card.Y + 58, card.Width - 36, 10);
		MusicDrawing.Fill(sb, bar, Color.Black * 0.5f);
		MusicDrawing.Fill(sb, new Rectangle(bar.X, bar.Y, (int)(bar.Width * PspLibrary.ImportProgress), bar.Height), new Color(110, 200, 120));
		Tetris.Centered(sb, (int)(PspLibrary.ImportProgress * 100f) + "%", card, card.Y + 74, MusicPlayerWindow.MutedColor, 0.56f);
	}

	private static void DrawWelcomeCard(SpriteBatch sb, Rectangle screen)
	{
		int w = Math.Min(440, screen.Width - 30);
		Rectangle card = new Rectangle(screen.Center.X - w / 2, screen.Center.Y - 94, w, 186);
		GameOverlay.MenuPanel(sb, card, CardBlue * 0.95f, Color.Black);
		Handheld(sb, new Rectangle(card.Center.X - 34, card.Y + 12, 68, 30));
		Tetris.Centered(sb, IsaClient.Languages.L10n.T("Bring your own games"), card, card.Y + 48, MusicPlayerWindow.AccentColor, 0.82f);
		int y = card.Y + 72;
		foreach (string line in PokemonWindow.Wrap(IsaClient.Languages.L10n.T("IsaClient doesn't include any game. Add your own PSP games: .iso, .cso, .chd, .pbp, .elf, .prx, or a .zip or .7z with them. They're only read, never changed."), w - 30, 0.6f))
		{
			Tetris.Centered(sb, line, card, y, Color.White * 0.9f, 0.62f);
			y += 16;
		}
		if (PspGame.Problem != null)
		{
			Tetris.Centered(sb, PspGame.Problem, card, y + 2, Bad, 0.56f);
		}
		if (MusicDrawing.TextButton(sb, "psp:add", new Rectangle(card.Center.X - 90, card.Bottom - 42, 180, 32), _pickerOpen ? "Choosing..." : "Add games", Green, "Or drop them on Terraria's window", !_pickerOpen))
		{
			PickGames();
		}
	}

	/// <summary>The game's picture and name, for the cards.</summary>
	private static void CardTop(SpriteBatch sb, Rectangle card, PspEntry game)
	{
		Rectangle icon = new Rectangle(card.Center.X - 62, card.Y + 10, 124, 69);
		DrawIcon(sb, icon, game);
		Tetris.Centered(sb, game?.Title ?? "", card, card.Y + 84, Color.White, 0.74f);
	}

	private static void DrawInstallCard(SpriteBatch sb, Rectangle screen)
	{
		int w = Math.Min(440, screen.Width - 30);
		Rectangle card = new Rectangle(screen.Center.X - w / 2, screen.Center.Y - 124, w, 238);
		GameOverlay.MenuPanel(sb, card, CardBlue * 0.95f, Color.Black);
		CardTop(sb, card, PspGame.Current);
		string text = IsaClient.Languages.L10n.F("PSP games need an emulator. IsaClient doesn't include one: {0} is downloaded once (about 16 MB) from libretro's build server.", PspEmulator.Name);
		int y = card.Y + 106;
		foreach (string line in PokemonWindow.Wrap(text, w - 30, 0.58f))
		{
			Tetris.Centered(sb, line, card, y, Color.White * 0.88f, 0.58f);
			y += 15;
		}
		if (PspEmulator.Busy)
		{
			Rectangle bar = new Rectangle(card.X + 20, card.Bottom - 50, w - 40, 10);
			MusicDrawing.Fill(sb, bar, Color.Black * 0.5f);
			MusicDrawing.Fill(sb, new Rectangle(bar.X, bar.Y, (int)(bar.Width * PspEmulator.Progress), bar.Height), new Color(110, 200, 120));
			Tetris.Centered(sb, IsaClient.Languages.L10n.F("Downloading {0}... {1}%", PspEmulator.Name, (int)(PspEmulator.Progress * 100f)), card, card.Bottom - 34, MusicPlayerWindow.AccentColor, 0.62f);
			return;
		}
		string problem = (PspEmulator.Error != null) ? IsaClient.Languages.L10n.F("Couldn't download it: {0}", PspEmulator.Error) : PspGame.Problem;
		if (problem != null)
		{
			Tetris.Centered(sb, problem, card, card.Bottom - 60, Bad, 0.54f);
		}
		Rectangle main = new Rectangle(card.X + 12, card.Bottom - 40, (w - 36) * 3 / 5, 30);
		Rectangle games = new Rectangle(main.Right + 12, main.Y, card.Right - 12 - main.Right - 12, 30);
		if (MusicDrawing.TextButton(sb, "psp:get", main, PspEmulator.Error != null ? "Try again" : "Download and play", Green, PspEmulator.Name + " (" + PspEmulator.License + ")"))
		{
			_focused = true;
			PspEmulator.Download(() => PspGame.Start(PspGame.Resume));
		}
		if (MusicDrawing.TextButton(sb, "psp:games2", games, "Games...", MusicPlayerWindow.PanelColor, "Your games: switch or add more"))
		{
			_page = Page.Games;
		}
	}

	private static void DrawStartCard(SpriteBatch sb, Rectangle screen)
	{
		int w = Math.Min(400, screen.Width - 30);
		bool resume = PspGame.HasResume && PspGame.Resume;
		Rectangle card = new Rectangle(screen.Center.X - w / 2, screen.Center.Y - 110, w, 212);
		GameOverlay.MenuPanel(sb, card, CardBlue * 0.95f, Color.Black);
		PspEntry game = PspGame.Current;
		CardTop(sb, card, game);
		string line = IsaClient.Languages.L10n.T(PspGame.HasSave ? "Your saves are kept in IsaClient/PSP." : "No save yet: a new adventure!");
		Tetris.Centered(sb, line, card, card.Y + 104, MusicPlayerWindow.MutedColor, 0.58f);
		string problem = PspGame.Problem ?? (PspGame.Stopping ? IsaClient.Languages.L10n.T("Keeping this moment...") : null);
		if (problem != null)
		{
			Tetris.Centered(sb, problem, card, card.Y + 120, PspGame.Problem != null ? Bad : MusicPlayerWindow.AccentColor, 0.54f);
		}
		int third = (w - 48) / 3;
		int y = card.Bottom - 74;
		Rectangle b1 = new Rectangle(card.X + 12, y, third, 30);
		Rectangle b2 = new Rectangle(b1.Right + 12, y, third, 30);
		Rectangle b3 = new Rectangle(b2.Right + 12, y, third, 30);
		if (resume)
		{
			if (MusicDrawing.TextButton(sb, "psp:continue", b1, "Continue", Green, "Right where you closed it"))
			{
				_focused = true;
				PspGame.Start(true);
			}
		}
		else if (MusicDrawing.TextButton(sb, "psp:start", b1, "Play", Green, "Turns the console on: title screen, then your in-game save"))
		{
			_focused = true;
			PspGame.Start(false);
		}
		if (MusicDrawing.TextButton(sb, "psp:loadgame", b2, "Load game", MusicPlayerWindow.PanelColor, "Your saved games (slots)"))
		{
			_page = Page.Saves;
		}
		if (resume && MusicDrawing.TextButton(sb, "psp:boot", b3, "Power on", MusicPlayerWindow.PanelColor, "Turns the console on: title screen, then your in-game save"))
		{
			_focused = true;
			PspGame.Start(false);
		}
		int bw = (w - 36) / 2;
		if (MusicDrawing.TextButton(sb, "psp:games3", new Rectangle(card.Center.X - bw / 2, card.Bottom - 38, bw, 26), "Games...", MusicPlayerWindow.PanelColor, "Your games: switch or add more"))
		{
			_page = Page.Games;
		}
	}

	/// <summary>A game's ICON0 (144x80) in a thin frame; without one, a dark card with its initials.</summary>
	private static void DrawIcon(SpriteBatch sb, Rectangle r, PspEntry game)
	{
		MusicDrawing.Fill(sb, new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), Color.Black * 0.7f);
		Texture2D icon = (game?.Icon != null) ? GbaSaves.Picture(game.Icon) : null;
		if (icon != null)
		{
			sb.Draw(icon, r, Color.White);
			return;
		}
		GameArt.Gradient(sb, r, new Color(40, 44, 92), new Color(70, 40, 110));
		string title = game?.Title ?? "";
		string initials = "";
		foreach (string w in title.Replace(":", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
		{
			if (char.IsLetterOrDigit(w[0]) && initials.Length < 3)
			{
				initials += char.ToUpperInvariant(w[0]);
			}
		}
		Tetris.Centered(sb, (initials.Length > 0) ? initials : "PSP", r, r.Center.Y - 12, Color.White * 0.85f, 0.9f);
	}

	#endregion

	#region Side panel

	private static void DrawSide(SpriteBatch sb, Rectangle side)
	{
		MusicDrawing.Box(sb, side, MusicPlayerWindow.PanelDarkColor * 0.9f);
		int x = side.X + 10;
		int w = side.Width - 20;
		int y = side.Y + 10;
		bool playing = PspGame.Playing;
		Rectangle icon = new Rectangle(x + 2, y + 2, w - 4, (w - 4) * 80 / 144);
		DrawIcon(sb, icon, PspGame.Current);
		y = icon.Bottom + 10;
		if (MusicDrawing.TextButton(sb, "psp:side:games", new Rectangle(x, y, w, 28), "Games", MusicPlayerWindow.PanelColor, "Your games: switch or add more"))
		{
			_page = Page.Games;
		}
		y += 36;
		MusicDrawing.Text(sb, "Saved games", x, y, MusicPlayerWindow.MutedColor, 0.6f);
		y += 18;
		int half = (w - 6) / 2;
		if (MusicDrawing.TextButton(sb, "psp:qs", new Rectangle(x, y, half, 28), "Save", MusicPlayerWindow.PanelColor, "Save this moment in a slot (or over one)", playing))
		{
			_page = Page.Saves;
		}
		if (MusicDrawing.TextButton(sb, "psp:ql", new Rectangle(x + half + 6, y, half, 28), "Load", MusicPlayerWindow.PanelColor, "Go back to one of your saved games", PspGame.HasGame))
		{
			_page = Page.Saves;
		}
		y += 34;
		MusicDrawing.Text(sb, "Volume", x, y, MusicPlayerWindow.MutedColor, 0.6f);
		y += 18;
		if (MusicDrawing.TextButton(sb, "psp:vol-", new Rectangle(x, y, 28, 24), "-", MusicPlayerWindow.PanelColor))
		{
			PspGame.SetVolume(PspGame.Volume - 10);
			PspConfig.Save();
		}
		Rectangle bar = new Rectangle(x + 34, y + 8, w - 68, 8);
		MusicDrawing.Fill(sb, bar, Color.Black * 0.5f);
		MusicDrawing.Fill(sb, new Rectangle(bar.X, bar.Y, bar.Width * PspGame.Volume / 100, bar.Height), MusicPlayerWindow.AccentColor);
		if (MusicDrawing.TextButton(sb, "psp:vol+", new Rectangle(x + w - 28, y, 28, 24), "+", MusicPlayerWindow.PanelColor))
		{
			PspGame.SetVolume(PspGame.Volume + 10);
			PspConfig.Save();
		}
		y += 32;
		if (MusicDrawing.TextButton(sb, "psp:reset", new Rectangle(x, y, half, 26), "Reset", MusicPlayerWindow.PanelColor, "Restarts the console (unsaved progress is lost)", playing))
		{
			PspGame.Reset();
		}
		bool canPower = playing || (PspGame.HasGame && !PspGame.Booting);
		if (MusicDrawing.TextButton(sb, "psp:power", new Rectangle(x + half + 6, y, half, 26), playing ? "Off" : "On", playing ? new Color(150, 60, 60) : Green, playing ? "Turns it off and keeps this moment" : "Turns it on", canPower))
		{
			if (playing)
			{
				PspGame.Stop();
			}
			else
			{
				_focused = true;
				PspGame.Start(PspGame.Resume);
			}
		}
		y += 34;
		MusicDrawing.Text(sb, "Sharpness", x, y, MusicPlayerWindow.MutedColor, 0.6f);
		y += 18;
		int third = (w - 12) / 3;
		for (int i = 1; i <= 3; i++)
		{
			string tip = (i == 1) ? "The PSP's own 480x272: the lightest" : (i == 2) ? "Twice the PSP's resolution: sharp, and light for any graphics card" : "Three times the PSP's resolution: the sharpest";
			if (PokemonWindow.Choice(sb, "psp:res" + i, new Rectangle(x + (i - 1) * (third + 6), y, third, 26), i + "x", PspGame.Resolution == i, tip))
			{
				PspGame.SetResolution(i);
			}
		}
		y += 34;
		if (MusicDrawing.TextButton(sb, "psp:controls", new Rectangle(x, y, w, 28), "Controls...", MusicPlayerWindow.PanelColor, "Change the keys"))
		{
			_page = Page.Controls;
		}
		y += 36;
		DrawSideStatus(sb, side, x, y, w);
	}

	private static void DrawSideStatus(SpriteBatch sb, Rectangle side, int x, int y, int w)
	{
		string speed = null;
		Color speedColor = MusicPlayerWindow.MutedColor;
		if (PspGame.Playing && !PspGame.Booting && PspGame.SlowSeconds >= 3)
		{
			speed = IsaClient.Languages.L10n.F("Running at {0}%: try a lower sharpness", PspGame.SpeedPercent);
			speedColor = new Color(255, 200, 120);
		}
		else if (PspGame.Playing && !PspGame.Booting && !PspGame.OnGraphicsCard)
		{
			speed = IsaClient.Languages.L10n.T("Drawing without the graphics card (slower)");
			speedColor = new Color(255, 200, 120);
		}
		if (speed != null)
		{
			foreach (string line in PokemonWindow.Wrap(speed, w, 0.54f))
			{
				MusicDrawing.RawText(sb, line, x, y, speedColor, 0.54f, w);
				y += 14;
			}
			y += 4;
		}
		(PspButton[] b, string n)[] shown =
		{
			(new[] { PspButton.StickUp, PspButton.StickLeft, PspButton.StickDown, PspButton.StickRight }, "Move"),
			(new[] { PspButton.Triangle, PspButton.Square, PspButton.Cross, PspButton.Circle }, "Buttons"),
			(new[] { PspButton.Start }, "Menu"),
			(new[] { PspButton.Fast }, "Fast")
		};
		foreach (var (keys, name) in shown)
		{
			if (y > side.Bottom - 34)
			{
				break;
			}
			string text = "";
			foreach (PspButton k in keys)
			{
				text += GbaConfig.KeyName(PspConfig.Bindings[(int)k, 0]);
			}
			MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T(name), x, y, MusicPlayerWindow.MutedColor, 0.56f, 62);
			MusicDrawing.RawText(sb, text, x + 64, y, Color.White * 0.85f, 0.56f, w - 64);
			y += 15;
		}
		MusicDrawing.RawText(sb, PspEmulator.Name + " (" + PspEmulator.License + ")", x, side.Bottom - 18, MusicPlayerWindow.MutedColor * 0.6f, 0.5f, w);
	}

	#endregion

	#region Pages

	private static void Resume(SpriteBatch sb)
	{
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
	}

	/// <summary>A page's title row: the title, a line under it and a Back button.</summary>
	private static bool PageHeader(SpriteBatch sb, Rectangle area, string title, string sub)
	{
		MusicDrawing.Box(sb, area, MusicPlayerWindow.PanelDarkColor * 0.97f);
		Handheld(sb, new Rectangle(area.X + 12, area.Y + 16, 30, 14));
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T(title), area.X + 50, area.Y + 9, MusicPlayerWindow.AccentColor, 0.82f, area.Width - 170);
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T(sub), area.X + 50, area.Y + 28, MusicPlayerWindow.MutedColor, 0.58f, area.Width - 170);
		MusicDrawing.Fill(sb, new Rectangle(area.X + 12, area.Y + 48, area.Width - 24, 2), Color.Black * 0.35f);
		return MusicDrawing.TextButton(sb, "psp:pageback", new Rectangle(area.Right - 102, area.Y + 11, 90, 28), "Back", MusicPlayerWindow.PanelColor);
	}

	private static float _gamesScroll;

	private static string _confirmGame;

	private static double _confirmUntil;

	private static void DrawGamesPage(SpriteBatch sb, Rectangle area)
	{
		if (PageHeader(sb, area, "Games", "Your PSP games. Each one keeps its own saves."))
		{
			_page = Page.Console;
			return;
		}
		List<string> games = PspConfig.Games;
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
				}
				else if (i == games.Count + 1)
				{
					DrawFindCard(sb, card, box);
				}
				else if (DrawGameCard(sb, card, box, PspLibrary.Entry(games[i])))
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
			PspGame.RemoveGame(remove);
		}
	}

	private static string FormatName(PspFormat format) => format switch
	{
		PspFormat.Iso => "ISO",
		PspFormat.Cso => "CSO",
		PspFormat.Chd => "CHD",
		PspFormat.Pbp => "PBP",
		PspFormat.Elf => "ELF",
		_ => "PSP"
	};

	/// <summary>One game: its picture, name and tags. Click to put it in the console. True when it's taken out.</summary>
	private static bool DrawGameCard(SpriteBatch sb, Rectangle card, Rectangle box, PspEntry e)
	{
		bool current = string.Equals(e.Path, PspGame.RomPath, StringComparison.OrdinalIgnoreCase);
		bool hover = MusicPlayerWindow.MouseIn(card) && MusicPlayerWindow.MouseIn(box);
		PokemonWindow.ListPanel(sb, card, current, hover);
		int lift = hover ? 3 : 0;
		DrawIcon(sb, new Rectangle(card.X + 14, card.Y + 16 - lift, card.Width - 28, (card.Width - 28) * 80 / 144), e);
		MusicDrawing.RawText(sb, e.Title, card.X + 10, card.Y + 124, Color.White, 0.66f, card.Width - 20);
		int cy = card.Y + 146;
		int cx = card.X + 10;
		if (e.Missing)
		{
			MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T("File not found"), cx, cy, Bad, 0.56f, card.Width - 20);
		}
		else
		{
			if (current)
			{
				cx = GameOverlay.Chip(sb, IsaClient.Languages.L10n.T(PspGame.Playing ? "Playing" : "In the console"), cx, cy, Green, Color.White, card.Right - 10);
			}
			GameOverlay.Chip(sb, FormatName(e.Format), cx, cy, new Color(70, 92, 150), Color.White, card.Right - 10);
		}
		int by = card.Bottom - 30;
		bool removed = false;
		if (!e.Missing && MusicDrawing.TextButton(sb, "psp:game:play:" + e.Path, new Rectangle(card.X + 10, by, card.Width - 50, 24), current && PspGame.Playing ? "Back to it" : "Play", Green, null, !e.Reading))
		{
			bool wasPlaying = current && PspGame.Playing;
			PspGame.SelectGame(e.Path);
			_page = Page.Console;
			_focused = true;
			if (!wasPlaying)
			{
				PspGame.Start(PspGame.Resume);
			}
		}
		Rectangle del = new Rectangle(card.Right - 34, by, 24, 24);
		string ask = "game:" + e.Path;
		bool asking = _confirmGame == ask && GameArt.Now < _confirmUntil;
		if (MusicDrawing.TextButton(sb, "psp:game:del:" + e.Path, del, asking ? "?" : "x", asking ? new Color(160, 50, 50) : MusicPlayerWindow.PanelColor, asking ? "Click again to take it out (its saves stay)" : "Take it out of the list"))
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
			PspGame.SelectGame(e.Path);
		}
		return removed;
	}

	/// <summary>A card with a dashed border, for what isn't a game.</summary>
	private static bool DashedCard(SpriteBatch sb, Rectangle card, Rectangle box)
	{
		bool hover = MusicPlayerWindow.MouseIn(card) && MusicPlayerWindow.MouseIn(box);
		MusicDrawing.Box(sb, card, hover ? new Color(52, 64, 120) : new Color(34, 42, 82));
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
		return hover;
	}

	private static void DrawAddCard(SpriteBatch sb, Rectangle card, Rectangle box)
	{
		bool hover = DashedCard(sb, card, box);
		if (PspLibrary.ImportBusy)
		{
			ImportBar(sb, new Rectangle(card.X, card.Y + 40, card.Width, card.Height - 40));
			return;
		}
		Color dash = MusicPlayerWindow.MutedColor * (hover ? 0.9f : 0.5f);
		Vector2 c = new Vector2(card.Center.X, card.Y + 70);
		MusicDrawing.Fill(sb, new Rectangle((int)c.X - 18, (int)c.Y - 4, 36, 8), dash);
		MusicDrawing.Fill(sb, new Rectangle((int)c.X - 4, (int)c.Y - 18, 8, 36), dash);
		Tetris.Centered(sb, IsaClient.Languages.L10n.T(_pickerOpen ? "Choosing..." : "Add games"), card, card.Y + 110, Color.White, 0.72f);
		Tetris.Centered(sb, IsaClient.Languages.L10n.T(".iso .cso .chd .pbp .zip .7z"), card, card.Y + 132, MusicPlayerWindow.MutedColor, 0.56f);
		Tetris.Centered(sb, IsaClient.Languages.L10n.T("You bring the games: none come with IsaClient."), card, card.Y + 150, MusicPlayerWindow.MutedColor * 0.8f, 0.5f);
		if (PspGame.Problem != null)
		{
			Tetris.Centered(sb, PspGame.Problem, card, card.Y + 12, Bad, 0.5f);
		}
		if (hover && MusicPlayerWindow.Clicked && !_pickerOpen)
		{
			MusicPlayerWindow.Clicked = false;
			PickGames();
		}
	}

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
		if (PageHeader(sb, area, "Saved games", "Each slot keeps the exact moment. The game's own saves stay on its memory stick."))
		{
			_page = Page.Console;
			return;
		}
		bool playing = PspGame.Playing;
		int top = area.Y + 58;
		const int cols = 2;
		const int gap = 8;
		int rows = (PspSaves.Count + cols - 1) / cols;
		int cardW = (area.Width - 24 - gap * (cols - 1)) / cols;
		int cardH = Math.Min(130, (area.Bottom - 12 - top - gap * (rows - 1)) / rows);
		for (int i = 1; i <= PspSaves.Count; i++)
		{
			int c = (i - 1) % cols;
			int r = (i - 1) / cols;
			DrawSlot(sb, new Rectangle(area.X + 12 + c * (cardW + gap), top + r * (cardH + gap), cardW, cardH), i, playing);
		}
	}

	private static void DrawSlot(SpriteBatch sb, Rectangle card, int i, bool playing)
	{
		SaveSlot slot = PspSaves.Info(i);
		bool hover = MusicPlayerWindow.MouseIn(card);
		MusicDrawing.Box(sb, card, slot.Used ? (hover ? new Color(62, 78, 146) : new Color(50, 64, 124)) : new Color(38, 46, 88));
		int th = card.Height - 12;
		int tw = Math.Min(th * 30 / 17, card.Width / 2 - 10);
		th = tw * 17 / 30;
		Rectangle thumb = new Rectangle(card.X + 6, card.Y + (card.Height - th) / 2, tw, th);
		MusicDrawing.Fill(sb, new Rectangle(thumb.X - 2, thumb.Y - 2, thumb.Width + 4, thumb.Height + 4), Color.Black);
		Texture2D tex = slot.Used ? PspSaves.Thumb(i) : null;
		if (tex != null)
		{
			sb.Draw(tex, thumb, Color.White);
		}
		else
		{
			MusicDrawing.Fill(sb, thumb, new Color(20, 24, 46));
			Tetris.Centered(sb, IsaClient.Languages.L10n.T("Empty"), thumb, thumb.Center.Y - 8, MusicPlayerWindow.MutedColor * 0.7f, 0.6f);
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
		bool busy = PspSaves.Busy >= 0;
		if (slot.Used && MusicDrawing.TextButton(sb, "psp:slot:load" + i, new Rectangle(x, by, bw, 26), "Load", Green, playing ? "Go back to this moment (what you haven't saved is lost)" : "Turn the console on at this moment", !busy))
		{
			PspSaves.Load(i);
			_page = Page.Console;
			_focused = true;
		}
		string saveLabel = Asking(i, "save") ? "Overwrite?" : (slot.Used ? "Save here" : "Save");
		Rectangle saveButton = slot.Used ? new Rectangle(x + bw + 4, by, bw, 26) : new Rectangle(x, by, bw, 26);
		if (MusicDrawing.TextButton(sb, "psp:slot:save" + i, saveButton, saveLabel, Asking(i, "save") ? new Color(160, 110, 40) : MusicPlayerWindow.PanelColor, playing ? (slot.Used ? "Click twice to save over this slot" : "Save this moment here") : "Turn the console on to save", playing && !busy))
		{
			if (!slot.Used || Confirmed(i, "save"))
			{
				PspSaves.Save(i);
			}
		}
		if (slot.Used)
		{
			Rectangle del = new Rectangle(card.Right - 30, by, 24, 26);
			if (MusicDrawing.TextButton(sb, "psp:slot:del" + i, del, Asking(i, "delete") ? "?" : "x", Asking(i, "delete") ? new Color(160, 50, 50) : MusicPlayerWindow.PanelColor, Asking(i, "delete") ? "Click again to delete it" : "Delete this save", !busy))
			{
				if (Confirmed(i, "delete"))
				{
					PspSaves.Delete(i);
				}
			}
		}
		if (PspSaves.Busy == i)
		{
			MusicDrawing.Box(sb, card, Color.Black * 0.45f);
			Tetris.Centered(sb, IsaClient.Languages.L10n.T("Saving..."), card, card.Center.Y - 8, Color.White, 0.7f);
		}
	}

	private static PspButton? _listenButton;

	private static int _listenSlot;

	private static KeyboardState _prevKeys;

	/// <summary>While a slot waits for a key: the first key pressed goes there (Escape cancels).</summary>
	private static void CaptureKey()
	{
		KeyboardState keys = Main.keyState;
		if (_listenButton.HasValue && _page == Page.Controls && Main.hasFocus)
		{
			foreach (Keys k in keys.GetPressedKeys())
			{
				if (_prevKeys.IsKeyDown(k))
				{
					continue;
				}
				if (k != Keys.Escape)
				{
					PspConfig.Bind(_listenButton.Value, _listenSlot, k);
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
		PspButton[][] columns =
		{
			new[] { PspButton.StickUp, PspButton.StickDown, PspButton.StickLeft, PspButton.StickRight, PspButton.Up, PspButton.Down, PspButton.Left, PspButton.Right, PspButton.Fast },
			new[] { PspButton.Cross, PspButton.Circle, PspButton.Square, PspButton.Triangle, PspButton.L, PspButton.R, PspButton.Start, PspButton.Select }
		};
		int colW = (area.Width - 36) / 2;
		int rowH = Math.Min(36, (area.Height - 62 - 80) / 9);
		for (int c = 0; c < 2; c++)
		{
			int x = area.X + 12 + c * (colW + 12);
			int y = area.Y + 60;
			foreach (PspButton b in columns[c])
			{
				Rectangle row = new Rectangle(x, y, colW, rowH - 3);
				MusicDrawing.Box(sb, row, new Color(46, 58, 110));
				Glyph(sb, b, new Vector2(row.X + 19, row.Center.Y));
				MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T(PspConfig.Names[(int)b]), row.X + 40, row.Center.Y - 9, Color.White, 0.66f, 110);
				int slotW = Math.Max(60, (row.Width - 170) / 2);
				for (int s = 0; s < 2; s++)
				{
					Rectangle slot = new Rectangle(row.Right - (2 - s) * (slotW + 6), row.Y + 2, slotW, row.Height - 4);
					bool listening = _listenButton == b && _listenSlot == s;
					string label = listening ? "..." : GbaConfig.KeyName(PspConfig.Bindings[(int)b, s]);
					if (MusicDrawing.TextButton(sb, "psp:key:" + b + s, slot, label, listening ? new Color(150, 120, 40) : MusicPlayerWindow.PanelColor, listening ? "Press a key (Esc cancels)" : "Click, then press a key"))
					{
						_listenButton = b;
						_listenSlot = s;
					}
					if (MusicPlayerWindow.RightClicked && MusicPlayerWindow.MouseIn(slot))
					{
						PspConfig.Bind(b, s, Keys.None);
					}
				}
				y += rowH;
			}
		}
		int by = area.Bottom - 44;
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T("Gamepad: left stick, D-pad, A = Cross, B = Circle, X = Square, Y = Triangle, LB / RB, Start, Back = Select, RT = fast forward."), area.X + 14, by - 22, MusicPlayerWindow.MutedColor, 0.56f, area.Width - 28);
		if (MusicDrawing.TextButton(sb, "psp:keys:reset", new Rectangle(area.X + 12, by, 220, 32), "Default keys", MusicPlayerWindow.PanelColor, "WASD = stick, arrows = D-pad, I J K L = the four buttons, Q / E = L / R, Enter = Start, Backspace = Select, Space = fast"))
		{
			PspConfig.ResetKeys();
			PspConfig.Save();
			_listenButton = null;
		}
		if (MusicDrawing.TextButton(sb, "psp:keys:done", new Rectangle(area.Right - 152, by, 140, 32), "Done", Green))
		{
			_page = Page.Console;
			_listenButton = null;
		}
	}

	/// <summary>A little picture of a button for the controls list.</summary>
	private static void Glyph(SpriteBatch sb, PspButton b, Vector2 c)
	{
		int mark = b switch
		{
			PspButton.Triangle => 0,
			PspButton.Circle => 1,
			PspButton.Cross => 2,
			PspButton.Square => 3,
			_ => -1
		};
		if (mark >= 0)
		{
			GameArt.Disc(sb, c, 24f, 24f, Key);
			Mark(sb, c, mark, 5.5f, MarkColor(mark));
			return;
		}
		switch (b)
		{
		case PspButton.StickUp:
		case PspButton.StickDown:
		case PspButton.StickLeft:
		case PspButton.StickRight:
		{
			Vector2 way = b == PspButton.StickUp ? new Vector2(0f, -1f) : b == PspButton.StickDown ? new Vector2(0f, 1f) : b == PspButton.StickLeft ? new Vector2(-1f, 0f) : new Vector2(1f, 0f);
			GameArt.Disc(sb, c, 24f, 24f, BodyDark);
			GameArt.Disc(sb, c + way * 4f, 14f, 14f, new Color(96, 98, 112));
			break;
		}
		case PspButton.Up:
		case PspButton.Down:
		case PspButton.Left:
		case PspButton.Right:
		{
			MusicDrawing.Fill(sb, new Rectangle((int)c.X - 10, (int)c.Y - 3, 20, 7), Key);
			MusicDrawing.Fill(sb, new Rectangle((int)c.X - 3, (int)c.Y - 10, 7, 20), Key);
			Rectangle lit = b == PspButton.Up ? new Rectangle((int)c.X - 3, (int)c.Y - 10, 7, 7) : b == PspButton.Down ? new Rectangle((int)c.X - 3, (int)c.Y + 3, 7, 7) : b == PspButton.Left ? new Rectangle((int)c.X - 10, (int)c.Y - 3, 7, 7) : new Rectangle((int)c.X + 3, (int)c.Y - 3, 7, 7);
			MusicDrawing.Fill(sb, lit, MusicPlayerWindow.AccentColor);
			break;
		}
		default:
		{
			string text = b == PspButton.L ? "L" : b == PspButton.R ? "R" : b == PspButton.Start ? "ST" : b == PspButton.Select ? "SE" : ">>";
			float w = MusicDrawing.Measure(text, 0.6f);
			MusicDrawing.Box(sb, new Rectangle((int)c.X - 12, (int)c.Y - 9, 24, 18), Key);
			MusicDrawing.RawText(sb, text, c.X - w / 2f, c.Y - 8f, Color.White * 0.9f, 0.6f);
			break;
		}
		}
	}

	#endregion

	#region Adding games

	/// <summary>Windows' file picker on its own thread (the game keeps drawing).</summary>
	private static void PickGames()
	{
		if (_pickerOpen || PspLibrary.ImportBusy)
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
					Title = "Add PSP games",
					Filter = "PSP games (*.iso, *.cso, *.chd, *.pbp, *.elf, *.prx, *.zip, *.7z)|*.iso;*.cso;*.chd;*.pbp;*.elf;*.prx;*.zip;*.7z|All files|*.*",
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
				if (files != null)
				{
					AddPicked(files, null);
				}
			});
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.IsBackground = true;
		thread.Start();
	}

	/// <summary>Adds files to the library and puts the first new game in the console (if nothing is playing).</summary>
	private static void AddPicked(string[] files, Action<string> then)
	{
		PspLibrary.Add(files, first =>
		{
			if (first != null)
			{
				if (!PspGame.Playing || !PspGame.HasGame)
				{
					PspGame.SelectGame(first);
				}
				PspGame.ShowToast("Games added");
			}
			then?.Invoke(first);
		});
	}

	/// <summary>Games dropped on Terraria's window: the Games page opens (on the main menu, once a world is entered).</summary>
	public static void AddDroppedGames(string[] files)
	{
		PspConfig.EnsureLoaded();
		AddPicked(files, null);
		if (Main.gameMenu)
		{
			_openWhenInWorld = true;
		}
		else
		{
			OpenGames();
		}
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
