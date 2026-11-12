using System;
using System.Collections.Generic;
using IsaClient.Identity;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;

namespace IsaClient.Games;

/// <summary>
/// Tetris alone, in a floating window (the well and its rules are <see cref="TetrisField"/>; versus games are
/// <see cref="TetrisBattle"/>). While you play, the window has the keyboard (your character stands still); clicking
/// elsewhere pauses it. A finished game goes to <see cref="Scores"/>, the board shared with every IsaClient.
/// </summary>
public static class Tetris
{
	private enum State
	{
		Idle,
		Playing,
		Paused,
		Over
	}

	private static State _state;

	/// <summary>0 slow, 1 normal, 2 fast: how fast pieces fall at the start and how soon it speeds up.</summary>
	[IsaClient.Core.Setting("TetrisSpeed", Min = 0, Max = 2)]
	public static int Speed = 1;

	/// <summary>The speed choice (the panel's card, and Tetris Versus games you invite to).</summary>
	public static readonly GameOption SpeedOption = new GameOption
	{
		Name = "Speed",
		Hint = "How fast pieces fall at the start and how soon it speeds up.",
		Labels = new[] { "Slow", "Normal", "Fast" },
		Values = new[] { 0, 1, 2 },
		Get = () => Speed,
		Set = v => Speed = v
	};

	private static TetrisField _field = new TetrisField();

	private static bool _newBest;

	private static double _overAt;

	private static readonly GameOverlay.Panel _panel = new GameOverlay.Panel { Layer = "games:tetris" };

	private static bool _registered;

	private static bool _open;

	private static bool _minimized;

	private static bool _focused;

	private static KeyboardState _prevKeys;

	private static bool _prevMouse;

	public static bool IsOpen => _open;

	/// <summary>A solo game is running in the window (its music plays, Terraria's is quiet).</summary>
	public static bool Playing => _open && !_minimized && _state == State.Playing && !Main.gameMenu;

	/// <summary>Solo or versus: Tetris music is on and Terraria's is quiet.</summary>
	public static bool MusicOn => TetrisMusic.Enabled && (Playing || TetrisBattle.Active != null);

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
			GamePlays.Count(GamePlays.Tetris);
		}
		_open = true;
		_minimized = false;
		MusicPlayerWindow.BringSurfaceToFront(_panel.Layer);
	}

	public static void Close()
	{
		if (_state == State.Playing || _state == State.Paused)
		{
			Finish();
		}
		_open = false;
		_focused = false;
	}

	private static void Ensure()
	{
		if (_registered)
		{
			return;
		}
		_registered = true;
		IsaClient.Music.MediaFocus.GamePlaying = () => MusicOn;
		MusicPlayerWindow.AddSurface(new MusicPlayerWindow.FloatingSurface
		{
			Layer = _panel.Layer,
			Shown = () => _open && !Main.gameMenu,
			ScreenRect = () => _panel.ScreenRect,
			Draw = Draw
		});
	}

	#endregion

	private static void NewGame()
	{
		_field = new TetrisField(Speed);
		_field.Start();
		_newBest = false;
		_state = State.Playing;
		_focused = true;
		TetrisMusic.Restart();
		SoundEngine.PlaySound(SoundID.MenuOpen);
	}

	private static void Finish()
	{
		if (_state == State.Over)
		{
			return;
		}
		_state = State.Over;
		_overAt = GameArt.Now;
		_newBest = Scores.Submit(Scores.Tetris, _field.Score);
		if (_newBest)
		{
			SoundEngine.PlaySound(SoundID.AchievementComplete);
		}
		else
		{
			SoundEngine.PlaySound(SoundID.PlayerKilled);
		}
	}

	/// <summary>Every tick (game loop): keys and falling.</summary>
	public static void Update()
	{
		Ensure();
		TetrisBattle battle = TetrisBattle.Active;
		TetrisMusic.Update(MusicOn, (battle != null) ? battle.Level : _field.Level);
		if (!_open)
		{
			return;
		}
		if (Main.gameMenu)
		{
			_open = false;
			_state = State.Idle;
			return;
		}
		// A click outside the window: it lets go of the keyboard.
		bool mouse = Main.mouseLeft;
		if (mouse && !_prevMouse && !_panel.ScreenRect.Contains(Main.mouseX, Main.mouseY))
		{
			_focused = false;
		}
		_prevMouse = mouse;
		KeyboardState keys = Main.keyState;
		bool canInput = _focused && Main.hasFocus && !Main.drawingPlayerChat && !Main.editSign && !Main.editChest && !_minimized;
		if (_state == State.Playing && !canInput)
		{
			_state = State.Paused;
		}
		else if (canInput && keys.IsKeyDown(Keys.P) && !_prevKeys.IsKeyDown(Keys.P))
		{
			_state = (_state == State.Playing) ? State.Paused : ((_state == State.Paused) ? State.Playing : _state);
		}
		if (_state == State.Playing)
		{
			_field.Step(keys, _prevKeys, canInput);
			if (_field.Over)
			{
				Finish();
			}
		}
		_prevKeys = keys;
	}

	#region Drawing

	private const int Side = 116;

	private const int HeaderH = 46;

	private static int CellSize => (GameOverlay.Size == 0) ? 18 : ((GameOverlay.Size == 2) ? 26 : 22);

	private static void Draw(SpriteBatch sb, int screenW, int screenH)
	{
		int cell = CellSize;
		int wellW = TetrisField.Cols * cell;
		int wellH = TetrisField.Rows * cell;
		int width = 12 + Side + 10 + wellW + 10 + Side + 12;
		if (_minimized)
		{
			Rectangle bar = GameOverlay.Place(_panel, 300, 40, screenW, screenH, 0);
			GameOverlay.Window(sb, bar, 18);
			DrawIcon(sb, new Rectangle(bar.X + 10, bar.Y + 9, 22, 22));
			MusicDrawing.RawText(sb, "Tetris  ·  " + _field.Score.ToString("N0"), bar.X + 40, bar.Y + 11, Color.White, 0.74f, bar.Width - 80);
			if (MusicDrawing.TextButton(sb, "tetris:restore", new Rectangle(bar.Right - 34, bar.Y + 9, 24, 22), "+", MusicPlayerWindow.PanelColor, "Show"))
			{
				_minimized = false;
			}
			GameOverlay.Drag(_panel, new Rectangle(bar.X, bar.Y, bar.Width - 40, bar.Height), screenW, screenH);
			return;
		}
		int height = HeaderH + wellH + 14 + 26;
		Rectangle r = GameOverlay.Place(_panel, width, height, screenW, screenH, 0);
		GameOverlay.Window(sb, r, HeaderH);
		GameOverlay.Grip(sb, r);
		if (DrawHeader(sb, r))
		{
			return;
		}
		Rectangle well = new Rectangle(r.X + 12 + Side + 10, r.Y + HeaderH, wellW, wellH);
		GameOverlay.MenuPanel(sb, new Rectangle(well.X - 6, well.Y - 6, well.Width + 12, well.Height + 12), new Color(14, 16, 34), (_focused && _state == State.Playing) ? Main.OurFavoriteColor * 0.8f : Color.Black);
		_field.DrawWell(sb, well, cell);
		DrawHoldAndStats(sb, _field, new Rectangle(r.X + 12, well.Y, Side, wellH), Math.Max(Scores.Best(Scores.Tetris), _field.Score));
		DrawRight(sb, new Rectangle(well.Right + 10, well.Y, Side, wellH));
		DrawOverlay(sb, well);
		if (_focused && _state == State.Playing)
		{
			// Your character stands still while the keys are the game's.
			PlayerInput.WritingText = true;
			Main.chatRelease = false;
		}
		Help(sb, r, well.Bottom + 12);
		if (MusicPlayerWindow.Clicked && MusicPlayerWindow.MouseIn(well))
		{
			MusicPlayerWindow.Clicked = false;
			_focused = true;
			if (_state == State.Paused)
			{
				_state = State.Playing;
			}
		}
		if (MusicPlayerWindow.Clicked && MusicPlayerWindow.MouseIn(r))
		{
			_focused = true;
		}
		GameOverlay.Drag(_panel, new Rectangle(r.X, r.Y, r.Width - 76, HeaderH - 4), screenW, screenH);
	}

	/// <summary>The keys, under the well.</summary>
	public static void Help(SpriteBatch sb, Rectangle r, int y)
	{
		string help = IsaClient.Languages.L10n.T("Arrows/WASD move · Up rotate · Space drop · C hold · P pause");
		float hw = Math.Min(MusicDrawing.Measure(help, 0.6f), r.Width - 24);
		MusicDrawing.RawText(sb, help, r.X + (r.Width - hw) / 2f, y, MusicPlayerWindow.MutedColor * 0.85f, 0.6f, r.Width - 24);
	}

	/// <summary>A little T of amethyst.</summary>
	public static void DrawIcon(SpriteBatch sb, Rectangle area)
	{
		int c = area.Width / 3;
		Rectangle[] cells =
		{
			new Rectangle(area.X, area.Y + c / 2, c, c), new Rectangle(area.X + c, area.Y + c / 2, c, c),
			new Rectangle(area.X + 2 * c, area.Y + c / 2, c, c), new Rectangle(area.X + c, area.Y + c / 2 + c, c, c)
		};
		GameArt.Sharp(sb);
		foreach (Rectangle cell in cells)
		{
			TetrisField.Block(sb, 3, cell, 1f);
		}
		GameArt.Smooth(sb);
	}

	private static bool DrawHeader(SpriteBatch sb, Rectangle r)
	{
		DrawIcon(sb, new Rectangle(r.X + 12, r.Y + 8, 30, 30));
		MusicDrawing.RawText(sb, "Tetris", r.X + 50, r.Y + 9, MusicPlayerWindow.AccentColor, 0.86f);
		int best = Scores.Best(Scores.Tetris);
		int rank = Scores.RankOf(Scores.Tetris, ClientIdentity.Id);
		int total = Scores.Ranking(Scores.Tetris).Count;
		string sub = (best > 0) ? IsaClient.Languages.L10n.F("Your best {0}  ·  #{1} of {2}", best.ToString("N0"), rank, total) : IsaClient.Languages.L10n.T("No score yet: play a game!");
		MusicDrawing.RawText(sb, sub, r.X + 50, r.Y + 28, MusicPlayerWindow.MutedColor, 0.64f, r.Width - 140);
		if (MusicDrawing.TextButton(sb, "tetris:min", new Rectangle(r.Right - 66, r.Y + 11, 24, 22), "-", MusicPlayerWindow.PanelColor, "Minimize"))
		{
			_minimized = true;
			_focused = false;
		}
		if (MusicDrawing.BareIcon(sb, "tetris:close", new Rectangle(r.Right - 34, r.Y + 13, 17, 18), MusicDrawing.Tex("Images/MapDeath"), null, "Close", Color.White))
		{
			Close();
			return true;
		}
		return false;
	}

	/// <summary>Hold, then the numbers (shared with versus).</summary>
	public static void DrawHoldAndStats(SpriteBatch sb, TetrisField field, Rectangle area, int best)
	{
		Rectangle hold = new Rectangle(area.X, area.Y, area.Width, 92);
		Box(sb, hold, "tetris::Hold");
		TetrisField.MiniPiece(sb, field.Hold, new Rectangle(hold.X + 8, hold.Y + 24, hold.Width - 16, hold.Height - 30), 18, field.CanHold ? 1f : 0.4f);
		int y = hold.Bottom + 10;
		y = Stat(sb, area.X, y, area.Width, "Score", field.Score.ToString("N0"), Color.White, 0.95f);
		y = Stat(sb, area.X, y, area.Width, "Level", field.Level.ToString(), new Color(140, 220, 255), 0.85f);
		y = Stat(sb, area.X, y, area.Width, "Lines", field.Lines.ToString(), new Color(170, 230, 140), 0.85f);
		if (best > 0)
		{
			Stat(sb, area.X, y, area.Width, "Best", best.ToString("N0"), MusicPlayerWindow.AccentColor, 0.85f);
		}
	}

	private static int Stat(SpriteBatch sb, int x, int y, int width, string label, string value, Color color, float scale)
	{
		Rectangle r = new Rectangle(x, y, width, 46);
		MusicDrawing.Box(sb, r, MusicPlayerWindow.PanelDarkColor * 0.9f);
		MusicDrawing.Text(sb, label, r.X + 10, r.Y + 5, MusicPlayerWindow.MutedColor, 0.6f);
		MusicDrawing.RawText(sb, value, r.X + 10, r.Y + 20, color, scale, width - 16);
		return r.Bottom + 6;
	}

	public static void Box(SpriteBatch sb, Rectangle r, string title)
	{
		MusicDrawing.Box(sb, r, MusicPlayerWindow.PanelDarkColor * 0.9f);
		MusicDrawing.Text(sb, title, r.X + 10, r.Y + 5, MusicPlayerWindow.MutedColor, 0.62f);
	}

	/// <summary>The next pieces in a box (shared with versus).</summary>
	public static Rectangle DrawNext(SpriteBatch sb, TetrisField field, Rectangle area, int count)
	{
		Rectangle next = new Rectangle(area.X, area.Y, area.Width, (count >= 3) ? 176 : 96);
		Box(sb, next, "Next");
		for (int i = 0; i < Math.Min(count, field.Next.Count); i++)
		{
			int cell = (i == 0) ? 18 : 13;
			int top = (i == 0) ? (next.Y + 24) : (next.Y + 84 + (i - 1) * 44);
			TetrisField.MiniPiece(sb, field.Next[i], new Rectangle(next.X, top, next.Width, (i == 0) ? 56 : 40), cell, (i == 0) ? 1f : 0.75f);
		}
		return next;
	}

	/// <summary>The next pieces, then the top of the shared board.</summary>
	private static void DrawRight(SpriteBatch sb, Rectangle area)
	{
		Rectangle next = DrawNext(sb, _field, area, 3);
		DrawTop(sb, Scores.Tetris, new Rectangle(area.X, next.Bottom + 10, area.Width, area.Bottom - next.Bottom - 10));
	}

	/// <summary>The top of a shared board in a box (as many as fit; shared with Snake).</summary>
	public static void DrawTop(SpriteBatch sb, byte game, Rectangle top5)
	{
		Box(sb, top5, "Top players");
		List<ScoreEntry> ranking = Scores.Ranking(game);
		int y = top5.Y + 26;
		int shown = Math.Max(0, Math.Min(ranking.Count, (top5.Height - 30) / 22));
		for (int i = 0; i < shown; i++)
		{
			ScoreEntry e = ranking[i];
			bool me = e.Id == ClientIdentity.Id;
			if (me)
			{
				MusicDrawing.Fill(sb, new Rectangle(top5.X + 4, y - 2, top5.Width - 8, 21), MusicPlayerWindow.AccentColor * 0.15f);
			}
			MusicDrawing.RawText(sb, (i + 1).ToString(), top5.X + 8, y, Medal(i), 0.66f);
			MusicDrawing.RawText(sb, e.Name, top5.X + 26, y, me ? MusicPlayerWindow.AccentColor : Color.White, 0.6f, top5.Width - 32);
			MusicDrawing.RawText(sb, e.Score.ToString("N0"), top5.X + 26, y + 10, MusicPlayerWindow.MutedColor, 0.5f);
			y += 22;
		}
		if (ranking.Count == 0)
		{
			MusicDrawing.Text(sb, "Nobody yet.", top5.X + 10, top5.Y + 28, MusicPlayerWindow.MutedColor, 0.6f, top5.Width - 16);
		}
	}

	public static Color Medal(int index)
	{
		return (index == 0) ? new Color(255, 214, 80) : ((index == 1) ? new Color(210, 220, 235) : ((index == 2) ? new Color(230, 150, 90) : new Color(170, 180, 200)));
	}

	/// <summary>Start, paused, game over: a card over the well.</summary>
	private static void DrawOverlay(SpriteBatch sb, Rectangle well)
	{
		if (_state == State.Playing)
		{
			return;
		}
		MusicDrawing.Fill(sb, well, Color.Black * ((_state == State.Over) ? 0.35f * GameArt.Clamp01((GameArt.Now - _overAt - 0.6) / 0.4) : 0.45f));
		if (_state == State.Over && GameArt.Now - _overAt < 0.9)
		{
			return;
		}
		float appear = (_state == State.Over) ? GameArt.EaseBack(GameArt.Clamp01((GameArt.Now - _overAt - 0.9) / 0.35)) : 1f;
		Rectangle card = new Rectangle(well.X + 12, well.Center.Y - 90 + (int)((1f - appear) * 30f), well.Width - 24, 180);
		GameOverlay.Window(sb, card, 30);
		string title = (_state == State.Idle) ? "Tetris" : ((_state == State.Paused) ? IsaClient.Languages.L10n.T("Paused") : IsaClient.Languages.L10n.T("Game over"));
		Color color = (_state == State.Over) ? new Color(255, 140, 140) : MusicPlayerWindow.AccentColor;
		Title(sb, title, card, color);
		int y = card.Y + 58;
		if (_state == State.Over)
		{
			Centered(sb, _field.Score.ToString("N0"), card, y, Color.White, 0.95f);
			int rank = Scores.RankOf(Scores.Tetris, ClientIdentity.Id);
			int total = Scores.Ranking(Scores.Tetris).Count;
			string line = _newBest ? IsaClient.Languages.L10n.F("New best!  #{0} of {1}", rank, total) : IsaClient.Languages.L10n.F("Best {0}  ·  #{1} of {2}", Scores.Best(Scores.Tetris).ToString("N0"), rank, total);
			Centered(sb, line, card, y + 26, _newBest ? new Color(255, 214, 80) : MusicPlayerWindow.MutedColor, 0.7f);
		}
		else
		{
			Centered(sb, (_state == State.Paused) ? IsaClient.Languages.L10n.T("Click the board or press P to go on.") : IsaClient.Languages.L10n.T("Clear lines with gemspark blocks."), card, y + 8, MusicPlayerWindow.MutedColor, 0.66f);
		}
		string button = (_state == State.Paused) ? "Continue" : ((_state == State.Over) ? "Play again" : "Start");
		if (MusicDrawing.TextButton(sb, "tetris:play", new Rectangle(card.Center.X - 70, card.Bottom - 46, 140, 32), button, new Color(60, 140, 80)))
		{
			MusicPlayerWindow.Clicked = false;
			if (_state == State.Paused)
			{
				_state = State.Playing;
				_focused = true;
			}
			else
			{
				NewGame();
			}
		}
	}

	public static void Title(SpriteBatch sb, string title, Rectangle card, Color color)
	{
		float scale = 0.6f;
		string fitted = MusicDrawing.FitBig(title, card.Width - 20, scale);
		Vector2 size = FontAssets.DeathText.Value.MeasureString(fitted) * scale;
		Utils.DrawBorderStringBig(sb, fitted, new Vector2(card.Center.X - size.X / 2f, card.Y + 12), color, scale);
	}

	public static void Centered(SpriteBatch sb, string text, Rectangle card, int y, Color color, float scale)
	{
		float w = Math.Min(MusicDrawing.Measure(text, scale), card.Width - 16);
		MusicDrawing.RawText(sb, text, card.Center.X - w / 2f, y, color, scale, card.Width - 16);
	}

	#endregion
}
