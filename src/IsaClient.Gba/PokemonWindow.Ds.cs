using System;
using System.Collections.Generic;
using IsaClient.Games;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace IsaClient.Gba;

/// <summary>
/// The DS in the console window: a graphite handheld with four buttons, the two screens as the player likes them
/// (one big 4:3 screen with the other at the side, both stacked like the console, or side by side), the mouse as the
/// stylus on the touch screen, and the card that downloads the emulator the first time.
/// </summary>
public static partial class PokemonWindow
{
	/// <summary>Pixels between the two screens (the hinge).</summary>
	private const int Hinge = 14;

	private static readonly Color DsShell = new Color(54, 58, 70);

	private static readonly Color DsShellLight = new Color(96, 104, 124);

	private static readonly Color DsShellDark = new Color(28, 30, 38);

	/// <summary>Where the touch screen is in the window this frame (empty when it isn't shown).</summary>
	private static Rectangle _touchRect;

	/// <summary>The stylus is down (it went down on the touch screen and the button is still held).</summary>
	private static bool _touching;

	private static readonly Dictionary<string, Texture2D> _dsIcons = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

	#region Screens

	/// <summary>Where the main screen and the other one go inside the screen area (the other is empty for one screen).</summary>
	private static void DsRects(Rectangle area, out Rectangle main, out Rectangle other)
	{
		switch (PokemonGame.DsLayout)
		{
		case DsLayout.Stacked:
		{
			int h = (area.Height - Hinge) / 2;
			main = new Rectangle(area.X, area.Y, area.Width, h);
			other = new Rectangle(area.X, area.Bottom - h, area.Width, h);
			break;
		}
		case DsLayout.SideBySide:
		{
			int w = (area.Width - Hinge) / 2;
			main = new Rectangle(area.X, area.Y, w, area.Height);
			other = new Rectangle(area.Right - w, area.Y, w, area.Height);
			break;
		}
		default:
			main = area;
			other = Rectangle.Empty;
			break;
		}
	}

	/// <summary>The top and bottom screens in the emulator's picture (both, one above the other).</summary>
	private static void DsSources(Texture2D tex, out Rectangle top, out Rectangle bottom)
	{
		if (tex.Height >= tex.Width * 3 / 2 - 2)
		{
			top = new Rectangle(0, 0, tex.Width, tex.Height / 2);
			bottom = new Rectangle(0, tex.Height / 2, tex.Width, tex.Height / 2);
		}
		else
		{
			top = new Rectangle(0, 0, tex.Width, tex.Height);
			bottom = top;
		}
	}

	private static void DrawDsScreens(SpriteBatch sb, Rectangle screen)
	{
		DsRects(screen, out Rectangle main, out Rectangle other);
		Texture2D tex = PokemonGame.Screen;
		GbaMachine m = PokemonGame.Machine;
		if (tex == null || tex.IsDisposed || m == null || m.FrameCount == 0)
		{
			MusicDrawing.Fill(sb, main, Color.Black);
			if (!other.IsEmpty)
			{
				MusicDrawing.Fill(sb, other, Color.Black);
			}
			Tetris.Centered(sb, IsaClient.Languages.L10n.T("Starting..."), main, main.Center.Y - 10, MusicPlayerWindow.MutedColor, 0.7f);
			return;
		}
		DsSources(tex, out Rectangle top, out Rectangle bottom);
		bool swap = PokemonGame.DsSwap;
		sb.End();
		// the DS is scaled by a fraction (2.5x at 3x): smoothed, so no row of pixels comes out thicker than the others
		sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
		sb.Draw(tex, main, swap ? bottom : top, Color.White);
		if (!other.IsEmpty)
		{
			sb.Draw(tex, other, swap ? top : bottom, Color.White);
		}
		GameArt.Smooth(sb);
		// the touch screen takes the mouse as the stylus
		Rectangle touch = swap ? main : other;
		if (!touch.IsEmpty)
		{
			TouchArea(touch);
		}
		if (!HasKeys)
		{
			string hint = (m.Paused) ? IsaClient.Languages.L10n.T("Paused") : IsaClient.Languages.L10n.T("Click to play");
			float w = MusicDrawing.Measure(hint, 0.66f);
			Rectangle tag = new Rectangle(main.Right - (int)w - 26, main.Y + 8, (int)w + 18, 24);
			MusicDrawing.Box(sb, tag, Color.Black * 0.6f);
			MusicDrawing.RawText(sb, hint, tag.X + 9, tag.Y + 4, MusicPlayerWindow.AccentColor, 0.66f);
		}
		if (MusicPlayerWindow.Clicked && MusicPlayerWindow.MouseIn(screen))
		{
			MusicPlayerWindow.Clicked = false;
			_focused = true;
		}
	}

	/// <summary>The console off: a night sky on each screen and the start card (or the emulator's download).</summary>
	private static void DrawDsOff(SpriteBatch sb, Rectangle screen)
	{
		DsRects(screen, out Rectangle main, out Rectangle other);
		NightSky(sb, main);
		if (!other.IsEmpty)
		{
			NightSky(sb, other);
		}
		// the card wants room: on the two stacked or side by side screens it goes over both
		Rectangle cardArea = (main.Height >= 320 && main.Width >= 400) ? main : screen;
		if (!PokemonGame.Engine.Installed || DsDownload.Busy)
		{
			DrawDsInstallCard(sb, cardArea);
		}
		else
		{
			DrawStartCard(sb, cardArea);
		}
	}

	/// <summary>The touch screen is at <paramref name="r"/> this frame: a click there puts the stylus down.</summary>
	private static void TouchArea(Rectangle r)
	{
		_touchRect = r;
		if (MusicPlayerWindow.Clicked && MusicPlayerWindow.MouseIn(r))
		{
			MusicPlayerWindow.Clicked = false;
			_focused = true;
			_touching = true;
		}
	}

	/// <summary>After drawing: the stylus follows the mouse while the button stays down.</summary>
	private static void UpdateTouch()
	{
		if (!PokemonGame.IsDs || !PokemonGame.Playing || _touchRect.IsEmpty || !MusicPlayerWindow.MouseDown)
		{
			_touching = false;
		}
		if (!_touching)
		{
			PokemonGame.Touch(false, 0f, 0f);
			return;
		}
		Point p = MusicPlayerWindow.MousePoint;
		Rectangle r = _touchRect;
		PokemonGame.Touch(true, (p.X - r.X + 0.5f) / r.Width, (p.Y - r.Y + 0.5f) / r.Height);
	}

	#endregion

	#region The handheld

	private static void DrawDsConsole(SpriteBatch sb, Rectangle body, Rectangle screen)
	{
		int held = PokemonGame.Held;
		bool Down(int bit) => (held & (1 << bit)) != 0;
		DsShoulder(sb, new Rectangle(body.X + 10, body.Y - 2, 90, 14), "L", Down(Libretro.L));
		DsShoulder(sb, new Rectangle(body.Right - 100, body.Y - 2, 90, 14), "R", Down(Libretro.R));
		MusicDrawing.Box(sb, new Rectangle(body.X, body.Y + 6, body.Width, body.Height - 6), DsShellDark);
		MusicDrawing.Box(sb, new Rectangle(body.X + 2, body.Y + 6, body.Width - 4, body.Height - 10), DsShell);
		GameArt.Gradient(sb, new Rectangle(body.X + 8, body.Y + 10, body.Width - 16, 26), DsShellLight * 0.5f, Color.Transparent);
		DsRects(screen, out Rectangle main, out Rectangle other);
		// one bezel around the screens; between two of them, the hinge
		Rectangle bezel = new Rectangle(screen.X - 12, screen.Y - 10, screen.Width + 24, screen.Height + 20);
		MusicDrawing.Box(sb, bezel, new Color(20, 22, 28));
		foreach (Rectangle s in new[] { main, other })
		{
			if (!s.IsEmpty)
			{
				MusicDrawing.Fill(sb, new Rectangle(s.X - 2, s.Y - 2, s.Width + 4, s.Height + 4), Color.Black);
			}
		}
		if (!other.IsEmpty)
		{
			Rectangle hinge = (PokemonGame.DsLayout == DsLayout.Stacked)
				? new Rectangle(bezel.X - 4, main.Bottom + 2, bezel.Width + 8, Hinge - 4)
				: new Rectangle(main.Right + 2, bezel.Y - 4, Hinge - 4, bezel.Height + 8);
			MusicDrawing.Fill(sb, hinge, DsShellDark);
			MusicDrawing.Fill(sb, (PokemonGame.DsLayout == DsLayout.Stacked) ? new Rectangle(hinge.X, hinge.Y + 2, hinge.Width, 2) : new Rectangle(hinge.X + 2, hinge.Y, 2, hinge.Height), DsShellLight * 0.4f);
		}
		bool on = PokemonGame.Playing;
		MusicDrawing.Fill(sb, new Rectangle(bezel.X + 5, bezel.Bottom - 8, 4, 4), on ? new Color(110, 200, 255) : new Color(60, 66, 76));
		// the controls sit by the touch screen (the lower one when they're stacked)
		Rectangle lower = (PokemonGame.DsLayout == DsLayout.Stacked) ? other : screen;
		int cy = lower.Center.Y - 6;
		DPad(sb, body.X + Pad / 2 + 4, cy, held);
		int bx = body.Right - Pad / 2 - 4;
		FaceButton(sb, new Vector2(bx, cy - 21), "X", Down(Libretro.X));
		FaceButton(sb, new Vector2(bx + 21, cy), "A", Down(Libretro.A));
		FaceButton(sb, new Vector2(bx, cy + 21), "B", Down(Libretro.B));
		FaceButton(sb, new Vector2(bx - 21, cy), "Y", Down(Libretro.Y));
		Pill(sb, new Rectangle(bx - 30, cy + 44, 60, 14), "START", Down(Libretro.Start));
		Pill(sb, new Rectangle(bx - 30, cy + 66, 60, 14), "SELECT", Down(Libretro.Select));
		// speakers by the top screen
		Rectangle upper = main;
		for (int side = 0; side < 2; side++)
		{
			int sx = (side == 0) ? (body.X + Pad / 2 - 6) : (body.Right - Pad / 2 - 6);
			for (int i = 0; i < 3; i++)
			{
				for (int j = 0; j < 3; j++)
				{
					MusicDrawing.Fill(sb, new Rectangle(sx + i * 5, upper.Y + 12 + j * 5, 2, 2), DsShellDark);
				}
			}
		}
		MusicDrawing.RawText(sb, "IsaClient  DS", body.X + 14, body.Bottom - 22, DsShellLight, 0.6f);
	}

	private static void DsShoulder(SpriteBatch sb, Rectangle r, string label, bool down)
	{
		MusicDrawing.Box(sb, r, down ? DsShellLight : DsShellDark);
		float w = MusicDrawing.Measure(label, 0.5f);
		MusicDrawing.RawText(sb, label, r.Center.X - w / 2f, r.Y, down ? Color.White : MusicPlayerWindow.MutedColor, 0.5f);
	}

	private static void FaceButton(SpriteBatch sb, Vector2 c, string label, bool down)
	{
		GameArt.Disc(sb, c + new Vector2(1.5f, 2.5f), 24f, 24f, Color.Black * 0.45f);
		GameArt.Disc(sb, c, 21f, 21f, down ? MusicPlayerWindow.AccentColor : new Color(34, 36, 46));
		GameArt.Disc(sb, c - new Vector2(3f, 4f), 8f, 6f, Color.White * (down ? 0.45f : 0.16f));
		float w = MusicDrawing.Measure(label, 0.5f);
		MusicDrawing.RawText(sb, label, c.X - w / 2f, c.Y - 7f, down ? Color.Black : Color.White * 0.9f, 0.5f);
	}

	/// <summary>A DS game card in pixels: nearly square, a ridged top with a cut corner and a label with the game's icon.</summary>
	private static void DsCartridge(SpriteBatch sb, Rectangle r, Texture2D icon, string title)
	{
		int h = r.Height;
		int w = Math.Min(r.Width, h * 33 / 35);
		Rectangle c = new Rectangle(r.Center.X - w / 2, r.Y, w, h);
		Color shell = new Color(62, 64, 74);
		Color dark = new Color(36, 38, 46);
		Color light = new Color(104, 108, 122);
		int notch = Math.Max(3, w / 7);
		MusicDrawing.Fill(sb, new Rectangle(c.X + 2, c.Y + 3, c.Width, c.Height), Color.Black * 0.35f);
		MusicDrawing.Fill(sb, new Rectangle(c.X, c.Y, c.Width - notch, c.Height), shell);
		MusicDrawing.Fill(sb, new Rectangle(c.Right - notch, c.Y + notch, notch, c.Height - notch), shell);
		MusicDrawing.Fill(sb, new Rectangle(c.X, c.Y, c.Width - notch, Math.Max(1, h / 30)), light);
		// grip ridges along the top
		int band = Math.Max(3, h / 8);
		for (int x = c.X + 4; x < c.Right - notch - 3; x += 5)
		{
			MusicDrawing.Fill(sb, new Rectangle(x, c.Y + band / 3, 2, band / 2 + 1), dark);
		}
		MusicDrawing.Fill(sb, new Rectangle(c.X, c.Bottom - Math.Max(2, h / 12), c.Width, Math.Max(2, h / 12)), dark);
		// the label
		Rectangle lab = new Rectangle(c.X + w / 9, c.Y + band + h / 14, w - 2 * (w / 9), h - band - h / 14 - Math.Max(2, h / 12) - h / 10);
		MusicDrawing.Fill(sb, new Rectangle(lab.X - 2, lab.Y - 2, lab.Width + 4, lab.Height + 4), dark);
		GameArt.Gradient(sb, lab, new Color(236, 238, 244), new Color(196, 202, 216));
		if (icon != null)
		{
			int room = Math.Min(lab.Width, lab.Height) - 4;
			int k = Math.Max(1, room / 32);
			int size = (room >= 32) ? 32 * k : Math.Max(8, room);
			GameArt.Sharp(sb);
			sb.Draw(icon, new Rectangle(lab.Center.X - size / 2, lab.Center.Y - size / 2, size, size), Color.White);
			GameArt.Smooth(sb);
		}
		else
		{
			string initials = Initials(title);
			float s = Math.Min(1.3f, lab.Height / 40f);
			float tw = MusicDrawing.Measure(initials, s);
			MusicDrawing.RawText(sb, initials, lab.Center.X - tw / 2f, lab.Center.Y - 14f * s, new Color(60, 64, 80), s);
		}
		GameArt.Frame(sb, lab, Color.Black * 0.45f, 1);
	}

	/// <summary>A DS game's icon from its banner (null when it has none).</summary>
	private static Texture2D DsIcon(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return null;
		}
		if (_dsIcons.TryGetValue(path, out Texture2D tex) && (tex == null || !tex.IsDisposed))
		{
			return tex;
		}
		tex = null;
		DsBanner b = DsRom.Banner(path);
		if (b?.Icon != null)
		{
			try
			{
				tex = new Texture2D(Main.instance.GraphicsDevice, 32, 32);
				tex.SetData(b.Icon);
			}
			catch
			{
				tex = null;
			}
		}
		_dsIcons[path] = tex;
		return tex;
	}

	#endregion

	#region Download card

	private static void DrawDsInstallCard(SpriteBatch sb, Rectangle screen)
	{
		DsEngine engine = PokemonGame.Engine;
		int w = Math.Min(420, screen.Width - 30);
		Rectangle card = new Rectangle(screen.Center.X - w / 2, screen.Center.Y - 108, w, 212);
		GameOverlay.MenuPanel(sb, card, new Color(63, 82, 151) * 0.95f, Color.Black);
		DsCartridge(sb, new Rectangle(card.Center.X - 22, card.Y + 8, 44, 46), DsIcon(PokemonGame.RomPath), PokemonGame.RomName);
		Tetris.Centered(sb, PokemonGame.RomName, card, card.Y + 58, Color.White, 0.74f);
		string text = IsaClient.Languages.L10n.F("DS games need an emulator. IsaClient doesn't include one: {0} is downloaded once (about 1 MB) from libretro's build server into IsaClient/Libraries.", engine.Name);
		int y = card.Y + 80;
		foreach (string line in Wrap(text, w - 30, 0.58f))
		{
			Tetris.Centered(sb, line, card, y, Color.White * 0.88f, 0.58f);
			y += 15;
		}
		Rectangle main = new Rectangle(card.X + 12, card.Bottom - 40, (w - 36) * 3 / 5, 30);
		Rectangle games = new Rectangle(main.Right + 12, main.Y, card.Right - 12 - main.Right - 12, 30);
		if (DsDownload.Busy)
		{
			Rectangle bar = new Rectangle(card.X + 20, card.Bottom - 58, w - 40, 10);
			MusicDrawing.Fill(sb, bar, Color.Black * 0.5f);
			MusicDrawing.Fill(sb, new Rectangle(bar.X, bar.Y, (int)(bar.Width * DsDownload.Progress), bar.Height), new Color(110, 200, 120));
			Tetris.Centered(sb, IsaClient.Languages.L10n.F("Downloading {0}... {1}%", DsDownload.Engine?.Name ?? engine.Name, (int)(DsDownload.Progress * 100f)), card, card.Bottom - 36, MusicPlayerWindow.AccentColor, 0.62f);
			return;
		}
		if (DsDownload.Error != null)
		{
			Tetris.Centered(sb, IsaClient.Languages.L10n.F("Couldn't download it: {0}", DsDownload.Error), card, card.Bottom - 60, new Color(255, 150, 150), 0.54f);
		}
		else if (PokemonGame.Problem != null)
		{
			Tetris.Centered(sb, PokemonGame.Problem, card, card.Bottom - 60, new Color(255, 150, 150), 0.54f);
		}
		if (MusicDrawing.TextButton(sb, "pkmn:ds:get", main, DsDownload.Error != null ? "Try again" : "Download and play", new Color(60, 140, 80), engine.Name + " (" + engine.License + "): " + engine.About))
		{
			_focused = true;
			DsDownload.Start(engine, () => PokemonGame.Start(PokemonGame.HasResume && PokemonGame.Resume));
		}
		if (MusicDrawing.TextButton(sb, "pkmn:ds:games", games, "Games...", MusicPlayerWindow.PanelColor, "Your games: switch or add more"))
		{
			_page = Page.Games;
		}
	}

	#endregion

	#region Side panel

	/// <summary>The side's top: the other screen while playing on one screen (the touch screen works there too), else the game's card.</summary>
	private static Rectangle DrawDsSideTop(SpriteBatch sb, int x, int y, int w)
	{
		Texture2D tex = PokemonGame.Screen;
		GbaMachine m = PokemonGame.Machine;
		if (m != null && m.FrameCount > 0 && PokemonGame.DsLayout == DsLayout.Single && tex != null && !tex.IsDisposed)
		{
			Rectangle prev = new Rectangle(x, y, w, w * 3 / 4);
			MusicDrawing.Fill(sb, new Rectangle(prev.X - 2, prev.Y - 2, prev.Width + 4, prev.Height + 4), Color.Black);
			DsSources(tex, out Rectangle top, out Rectangle bottom);
			bool showsBottom = !PokemonGame.DsSwap;
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
			sb.Draw(tex, prev, showsBottom ? bottom : top, Color.White);
			GameArt.Smooth(sb);
			string label = showsBottom ? IsaClient.Languages.L10n.T("Touch screen") : IsaClient.Languages.L10n.T("Top screen");
			float lw = MusicDrawing.Measure(label, 0.5f);
			MusicDrawing.Box(sb, new Rectangle(prev.X + 3, prev.Y + 3, (int)lw + 10, 17), Color.Black * 0.6f);
			MusicDrawing.RawText(sb, label, prev.X + 8, prev.Y + 4, Color.White * 0.9f, 0.5f);
			if (showsBottom)
			{
				TouchArea(prev);
			}
			else if (MusicPlayerWindow.Clicked && MusicPlayerWindow.MouseIn(prev))
			{
				MusicPlayerWindow.Clicked = false;
				PokemonGame.SwapScreens();
			}
			string key = GbaConfig.KeyName(GbaConfig.Bindings[(int)GbaButton.Swap, 0]);
			Rectangle swapBtn = new Rectangle(x, prev.Bottom + 6, w, 24);
			if (MusicDrawing.TextButton(sb, "pkmn:ds:swap", swapBtn, IsaClient.Languages.L10n.F("Swap screens ({0})", key), MusicPlayerWindow.PanelColor, "The big screen and this one trade places"))
			{
				PokemonGame.SwapScreens();
			}
			return new Rectangle(x, y, w, swapBtn.Bottom - y);
		}
		Rectangle card = new Rectangle(x, y, w, 84);
		GameOverlay.MenuPanel(sb, card, new Color(63, 82, 151) * 0.85f, Color.Black);
		DsCartridge(sb, new Rectangle(card.X + 8, card.Y + 8, 64, 68), DsIcon(PokemonGame.RomPath), PokemonGame.RomName);
		int tx = card.X + 80;
		int ty = card.Y + 10;
		foreach (string line in Wrap(PokemonGame.RomName, card.Right - tx - 6, 0.62f))
		{
			if (ty > card.Y + 44)
			{
				break;
			}
			MusicDrawing.RawText(sb, line, tx, ty, Color.White, 0.62f, card.Right - tx - 6);
			ty += 16;
		}
		MusicDrawing.RawText(sb, "Nintendo DS", tx, card.Bottom - 22, MusicPlayerWindow.MutedColor, 0.54f, card.Right - tx - 6);
		return card;
	}

	/// <summary>How the screens are laid out and which emulator runs the games.</summary>
	private static int DrawDsSideOptions(SpriteBatch sb, int x, int y, int w)
	{
		(DsLayout layout, string label, string tip)[] layouts =
		{
			(DsLayout.Single, "One", "One big screen like any game; the other one here at the side (the swap key trades them)"),
			(DsLayout.Stacked, "Both", "Both screens, one above the other, like the DS"),
			(DsLayout.SideBySide, "Wide", "Both screens side by side")
		};
		int bw = (w - 8) / 3;
		for (int i = 0; i < layouts.Length; i++)
		{
			Rectangle b = new Rectangle(x + i * (bw + 4), y, bw, 26);
			bool on = PokemonGame.DsLayout == layouts[i].layout;
			if (Choice(sb, "pkmn:ds:layout" + i, b, layouts[i].label, on, layouts[i].tip))
			{
				PokemonGame.SetLayout(layouts[i].layout);
			}
		}
		y += 32;
		int hw = (w - 6) / 2;
		for (int i = 0; i < DsEngine.All.Length; i++)
		{
			DsEngine e = DsEngine.All[i];
			Rectangle b = new Rectangle(x + i * (hw + 6), y, hw, 26);
			bool on = PokemonGame.Engine == e;
			string tip = e.Name + " (" + e.License + "): " + e.About + (e.Installed ? "" : ". Downloaded the first time.") + (on ? "" : " Each emulator keeps its own saves.");
			if (Choice(sb, "pkmn:ds:engine" + i, b, e.Name, on, tip))
			{
				PokemonGame.SetEngine(e);
			}
		}
		return y + 32;
	}

	/// <summary>The side's last lines: saved / speed, the keys and the emulator's credit.</summary>
	private static void DrawDsSideBottom(SpriteBatch sb, Rectangle side, int x, int y, int w)
	{
		if (PokemonGame.Playing && PokemonGame.SlowSeconds >= 3)
		{
			DsEngine other = (PokemonGame.Engine == DsEngine.MelonDS) ? DsEngine.DeSmuME : DsEngine.MelonDS;
			MusicDrawing.RawText(sb, IsaClient.Languages.L10n.F("Running at {0}% speed", PokemonGame.SpeedPercent), x, y, new Color(255, 170, 120), 0.56f, w);
			y += 15;
			MusicDrawing.RawText(sb, IsaClient.Languages.L10n.F("If it stays slow, try {0}", other.Name), x, y, MusicPlayerWindow.MutedColor, 0.52f, w);
			y += 18;
		}
		else
		{
			DateTime saved = PokemonGame.DsLastSaved;
			string text = (saved != default(DateTime)) ? IsaClient.Languages.L10n.F("Game saved at {0}", saved.ToString("HH:mm")) : (PokemonGame.HasSave ? IsaClient.Languages.L10n.T("Your game save is on disk") : IsaClient.Languages.L10n.T("Not saved in the game yet"));
			MusicDrawing.RawText(sb, text, x, y, (saved != default(DateTime)) ? new Color(140, 230, 140) : MusicPlayerWindow.MutedColor, 0.56f, w);
			y += 20;
		}
		string move = GbaConfig.KeyName(GbaConfig.Bindings[0, 0]) + GbaConfig.KeyName(GbaConfig.Bindings[2, 0]) + GbaConfig.KeyName(GbaConfig.Bindings[1, 0]) + GbaConfig.KeyName(GbaConfig.Bindings[3, 0]);
		(string n, string keys)[] shown =
		{
			("Move", move),
			("A  B", GbaConfig.KeysOf(GbaButton.A) + "  " + GbaConfig.KeysOf(GbaButton.B)),
			("X  Y", GbaConfig.KeysOf(GbaButton.X) + "  " + GbaConfig.KeysOf(GbaButton.Y)),
			("gba::Start", GbaConfig.KeysOf(GbaButton.Start)),
			("Touch", IsaClient.Languages.L10n.T("Mouse"))
		};
		foreach (var (n, keys) in shown)
		{
			if (y > side.Bottom - 34)
			{
				break;
			}
			MusicDrawing.RawText(sb, n, x, y, MusicPlayerWindow.MutedColor, 0.56f, 50);
			MusicDrawing.RawText(sb, keys, x + 52, y, Color.White * 0.85f, 0.56f, w - 52);
			y += 15;
		}
		DsEngine engine = PokemonGame.Engine;
		MusicDrawing.RawText(sb, engine.Name + " core (" + engine.License + ")", x, side.Bottom - 18, MusicPlayerWindow.MutedColor * 0.6f, 0.5f, w);
	}

	#endregion
}
