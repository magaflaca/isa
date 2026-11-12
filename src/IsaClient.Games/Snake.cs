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
/// Snake, the old phone game, in a floating window like <see cref="Tetris"/>: a snake of bricks eats Terraria fruit on a
/// grassy field, grows and goes faster; a life fruit shows up now and then for a while, worth more the sooner it's
/// eaten. While you play, the window has the keyboard (your character stands still); clicking elsewhere pauses it. A
/// finished game goes to <see cref="Scores"/> (its own board, and the global one).
/// </summary>
public static class Snake
{
	private enum State
	{
		Idle,
		Playing,
		Paused,
		Over
	}

	public const int Cols = 22;

	public const int Rows = 16;

	/// <summary>0 slow, 1 normal, 2 fast: how fast it goes at the start (faster games give more a fruit).</summary>
	[IsaClient.Core.Setting("SnakeSpeed", Min = 0, Max = 2)]
	public static int Speed = 1;

	/// <summary>1 solid (the edge ends the game, more a fruit), 0 wrap (you come out the other side).</summary>
	[IsaClient.Core.Setting("SnakeWalls", Min = 0, Max = 1)]
	public static int Walls = 1;

	/// <summary>The bricks of the snake: see <see cref="Skins"/>.</summary>
	[IsaClient.Core.Setting("SnakeSkin", Min = 0, Max = 3)]
	public static int Skin;

	public static readonly GameOption SpeedOption = new GameOption
	{
		Name = "Speed",
		Hint = "How fast the snake goes at the start. Faster games give more points a fruit.",
		Labels = new[] { "Slow", "Normal", "Fast" },
		Values = new[] { 0, 1, 2 },
		Get = () => Speed,
		Set = v => Speed = v
	};

	public static readonly GameOption WallsOption = new GameOption
	{
		Name = "Walls",
		Hint = "Solid: touching the edge ends the game (more points a fruit). Wrap: you come out the other side.",
		Labels = new[] { "Wrap", "Solid" },
		Values = new[] { 0, 1 },
		Get = () => Walls,
		Set = v => Walls = v
	};

	public static readonly GameOption SkinOption = new GameOption
	{
		Name = "snake::Skin",
		Hint = "What the snake is made of. Only the look changes.",
		Labels = new[] { "Jungle", "Rainbow", "Candy", "Hellstone" },
		Values = new[] { 0, 1, 2, 3 },
		Get = () => Skin,
		Set = v => Skin = v
	};

	/// <summary>The brick of each skin (Rainbow is white snow brick, colored along the body).</summary>
	private static readonly ushort[] Skins = { TileID.ChlorophyteBrick, TileID.SnowBrick, TileID.CandyCaneBlock, TileID.HellstoneBrick };

	/// <summary>Each skin's color for sparks and glows.</summary>
	private static readonly Color[] SkinGlow = { new Color(120, 230, 90), new Color(255, 255, 255), new Color(255, 120, 130), new Color(255, 140, 60) };

	private static readonly short[] Fruits =
	{
		ItemID.Apple, ItemID.Apricot, ItemID.Banana, ItemID.BlackCurrant, ItemID.BloodOrange, ItemID.Cherry, ItemID.Coconut, ItemID.Dragonfruit,
		ItemID.Elderberry, ItemID.Grapefruit, ItemID.Lemon, ItemID.Mango, ItemID.Peach, ItemID.Pineapple, ItemID.Plum, ItemID.Rambutan, ItemID.Starfruit
	};

	/// <summary>The juice of each fruit (the sparks when it's eaten).</summary>
	private static readonly Color[] Juice =
	{
		new Color(230, 50, 50), new Color(250, 170, 80), new Color(250, 220, 80), new Color(120, 60, 160), new Color(240, 90, 50), new Color(220, 30, 60), new Color(240, 240, 230), new Color(240, 60, 140),
		new Color(90, 60, 170), new Color(250, 130, 110), new Color(250, 240, 90), new Color(250, 180, 60), new Color(250, 170, 130), new Color(250, 210, 70), new Color(150, 60, 170), new Color(230, 60, 80), new Color(250, 230, 90)
	};

	private const short BonusItem = ItemID.LifeFruit;

	/// <summary>How long the life fruit stays.</summary>
	private const double BonusTime = 7.0;

	private static State _state;

	private static readonly List<Point> _body = new List<Point>();

	/// <summary>Where each piece of the body was before the last step (it slides from there).</summary>
	private static readonly List<Point> _prev = new List<Point>();

	private static Point _dir = new Point(1, 0);

	/// <summary>Turns pressed, done one a step (so quick turns aren't lost).</summary>
	private static readonly List<Point> _turns = new List<Point>();

	private static Point _food;

	private static int _foodKind;

	private static double _foodAt;

	private static Point _bonus = new Point(-1, -1);

	private static double _bonusUntil;

	private static int _grow;

	private static int _eaten;

	private static int _score;

	private static double _stepEvery;

	private static double _nextStep;

	private static double _overAt;

	private static double _shakeAt = -10.0;

	private static bool _newBest;

	private static readonly Random _rng = new Random();

	private struct Spark
	{
		public Vector2 At;

		public Vector2 Speed;

		public Color Color;

		public double Born;
	}

	private static readonly List<Spark> _sparks = new List<Spark>();

	private struct Popup
	{
		public Vector2 At;

		public string Text;

		public Color Color;

		public double Born;
	}

	private static readonly List<Popup> _popups = new List<Popup>();

	private static readonly GameOverlay.Panel _panel = new GameOverlay.Panel { Layer = "games:snake" };

	private static bool _registered;

	private static bool _open;

	private static bool _minimized;

	private static bool _focused;

	private static KeyboardState _prevKeys;

	private static bool _prevMouse;

	public static bool IsOpen => _open;

	public static int Length => _body.Count;

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
			GamePlays.Count(GamePlays.Snake);
		}
		_open = true;
		_minimized = false;
		if (_body.Count == 0)
		{
			Reset();
		}
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
		MusicPlayerWindow.AddSurface(new MusicPlayerWindow.FloatingSurface
		{
			Layer = _panel.Layer,
			Shown = () => _open && !Main.gameMenu,
			ScreenRect = () => _panel.ScreenRect,
			Draw = Draw
		});
	}

	#endregion

	#region The game

	/// <summary>A snake of four in the middle, heading right, and a fruit.</summary>
	private static void Reset()
	{
		_body.Clear();
		_prev.Clear();
		_turns.Clear();
		_sparks.Clear();
		_popups.Clear();
		_dir = new Point(1, 0);
		for (int i = 0; i < 4; i++)
		{
			_body.Add(new Point(Cols / 2 - 3 - i, Rows / 2));
		}
		_prev.AddRange(_body);
		_grow = 0;
		_eaten = 0;
		_score = 0;
		_bonus = new Point(-1, -1);
		_stepEvery = StartStep;
		PlaceFood();
	}

	private static double StartStep => (Speed == 0) ? 0.16 : ((Speed == 2) ? 0.085 : 0.12);

	private static double FastestStep => (Speed == 0) ? 0.09 : ((Speed == 2) ? 0.05 : 0.065);

	/// <summary>Points a fruit: more when it's fast or the walls are solid.</summary>
	private static int FruitPoints => 10 + 5 * Speed + ((Walls == 1) ? 5 : 0);

	/// <summary>The life fruit: five fruits' worth when it's new, down to two as it fades.</summary>
	private static int BonusPoints => FruitPoints * (2 + (int)Math.Round(3.0 * GameArt.Clamp01((_bonusUntil - GameArt.Now) / BonusTime)));

	private static void NewGame()
	{
		Reset();
		_newBest = false;
		_state = State.Playing;
		_focused = true;
		_nextStep = GameArt.Now + _stepEvery;
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
		_shakeAt = GameArt.Now;
		_newBest = Scores.Submit(Scores.Snake, _score);
		if (_newBest)
		{
			SoundEngine.PlaySound(SoundID.AchievementComplete);
		}
		else
		{
			SoundEngine.PlaySound(SoundID.PlayerKilled);
		}
	}

	private static bool Occupied(Point p, bool tailMoves)
	{
		int count = tailMoves ? (_body.Count - 1) : _body.Count;
		for (int i = 0; i < count; i++)
		{
			if (_body[i] == p)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>A free cell (null when the snake fills the field).</summary>
	private static Point? FreeCell()
	{
		List<Point> free = new List<Point>();
		for (int y = 0; y < Rows; y++)
		{
			for (int x = 0; x < Cols; x++)
			{
				Point p = new Point(x, y);
				if (!Occupied(p, false) && p != _food && p != _bonus)
				{
					free.Add(p);
				}
			}
		}
		return (free.Count == 0) ? (Point?)null : free[_rng.Next(free.Count)];
	}

	private static void PlaceFood()
	{
		Point? cell = FreeCell();
		_food = cell ?? new Point(-1, -1);
		_foodKind = _rng.Next(Fruits.Length);
		_foodAt = GameArt.Now;
	}

	/// <summary>One cell forward: turn, move, eat or crash.</summary>
	private static void Step()
	{
		while (_turns.Count > 0)
		{
			Point turn = _turns[0];
			_turns.RemoveAt(0);
			if (turn.X != -_dir.X || turn.Y != -_dir.Y)
			{
				_dir = turn;
				break;
			}
		}
		Point head = new Point(_body[0].X + _dir.X, _body[0].Y + _dir.Y);
		if (head.X < 0 || head.X >= Cols || head.Y < 0 || head.Y >= Rows)
		{
			if (Walls == 1)
			{
				Finish();
				return;
			}
			head = new Point((head.X + Cols) % Cols, (head.Y + Rows) % Rows);
		}
		if (Occupied(head, _grow == 0))
		{
			Finish();
			return;
		}
		_prev.Clear();
		for (int i = 0; i <= _body.Count; i++)
		{
			_prev.Add(_body[Math.Min(i, _body.Count - 1)]);
		}
		_body.Insert(0, head);
		if (_grow > 0)
		{
			_grow--;
		}
		else
		{
			_body.RemoveAt(_body.Count - 1);
			_prev.RemoveAt(_prev.Count - 1);
		}
		if (head == _food)
		{
			Eat(FruitPoints, Juice[_foodKind]);
			_eaten++;
			_stepEvery = Math.Max(FastestStep, _stepEvery * 0.97);
			PlaceFood();
			if (_eaten % 5 == 0 && _bonus.X < 0)
			{
				Point? cell = FreeCell();
				if (cell.HasValue)
				{
					_bonus = cell.Value;
					_bonusUntil = GameArt.Now + BonusTime;
				}
			}
			if (_food.X < 0)
			{
				// The field is full: nothing left to eat.
				Finish();
			}
		}
		else if (head == _bonus)
		{
			Eat(BonusPoints, new Color(255, 90, 120));
			_grow += 2;
			_bonus = new Point(-1, -1);
			SoundEngine.PlaySound(SoundID.Item4);
		}
	}

	private static void Eat(int points, Color juice)
	{
		_score += points;
		_grow++;
		SoundEngine.PlaySound(SoundID.Item2);
		Vector2 at = new Vector2(_body[0].X + 0.5f, _body[0].Y + 0.5f);
		for (int i = 0; i < 12; i++)
		{
			double angle = _rng.NextDouble() * Math.PI * 2.0;
			float speed = 1.5f + (float)_rng.NextDouble() * 2.5f;
			_sparks.Add(new Spark { At = at, Speed = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * speed, Color = juice, Born = GameArt.Now });
		}
		_popups.Add(new Popup { At = at, Text = "+" + points, Color = Color.Lerp(juice, Color.White, 0.4f), Born = GameArt.Now });
	}

	private static Point? KeyTurn(KeyboardState keys)
	{
		bool Pressed(Keys a, Keys b) => (keys.IsKeyDown(a) && !_prevKeys.IsKeyDown(a)) || (keys.IsKeyDown(b) && !_prevKeys.IsKeyDown(b));
		if (Pressed(Keys.Up, Keys.W))
		{
			return new Point(0, -1);
		}
		if (Pressed(Keys.Down, Keys.S))
		{
			return new Point(0, 1);
		}
		if (Pressed(Keys.Left, Keys.A))
		{
			return new Point(-1, 0);
		}
		if (Pressed(Keys.Right, Keys.D))
		{
			return new Point(1, 0);
		}
		return null;
	}

	/// <summary>Every tick (game loop): keys and steps.</summary>
	public static void Update()
	{
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
		double now = GameArt.Now;
		if (_state == State.Playing && !canInput)
		{
			_state = State.Paused;
		}
		else if (canInput && keys.IsKeyDown(Keys.P) && !_prevKeys.IsKeyDown(Keys.P) && (_state == State.Playing || _state == State.Paused))
		{
			_state = (_state == State.Playing) ? State.Paused : State.Playing;
			_nextStep = now + _stepEvery;
		}
		else if (canInput && (_state == State.Idle || (_state == State.Over && now - _overAt > 1.2)) && (keys.IsKeyDown(Keys.Enter) && !_prevKeys.IsKeyDown(Keys.Enter) || keys.IsKeyDown(Keys.Space) && !_prevKeys.IsKeyDown(Keys.Space)))
		{
			NewGame();
		}
		if (_state == State.Playing)
		{
			Point? turn = KeyTurn(keys);
			if (turn.HasValue && _turns.Count < 3)
			{
				Point last = (_turns.Count > 0) ? _turns[_turns.Count - 1] : _dir;
				if (turn.Value != last && (turn.Value.X != -last.X || turn.Value.Y != -last.Y))
				{
					_turns.Add(turn.Value);
				}
			}
			if (_bonus.X >= 0 && now > _bonusUntil)
			{
				_bonus = new Point(-1, -1);
			}
			// Steps by the clock, so the speed is the same at any frame rate.
			int steps = 0;
			while (_state == State.Playing && now >= _nextStep && steps++ < 3)
			{
				Step();
				_nextStep += _stepEvery;
			}
			if (now > _nextStep)
			{
				_nextStep = now + _stepEvery;
			}
		}
		_prevKeys = keys;
	}

	#endregion

	#region Drawing

	private const int Side = 132;

	private const int HeaderH = 46;

	private static int CellSize => (GameOverlay.Size == 0) ? 18 : ((GameOverlay.Size == 2) ? 26 : 22);

	private static void Draw(SpriteBatch sb, int screenW, int screenH)
	{
		int cell = CellSize;
		int border = cell / 2;
		int fieldW = Cols * cell;
		int fieldH = Rows * cell;
		int width = 12 + border + fieldW + border + 12 + Side + 12;
		if (_minimized)
		{
			Rectangle bar = GameOverlay.Place(_panel, 300, 40, screenW, screenH, 0);
			GameOverlay.Window(sb, bar, 18);
			DrawIcon(sb, new Rectangle(bar.X + 10, bar.Y + 9, 22, 22));
			MusicDrawing.RawText(sb, "Snake  ·  " + _score.ToString("N0"), bar.X + 40, bar.Y + 11, Color.White, 0.74f, bar.Width - 80);
			if (MusicDrawing.TextButton(sb, "snake:restore", new Rectangle(bar.Right - 34, bar.Y + 9, 24, 22), "+", MusicPlayerWindow.PanelColor, "Show"))
			{
				_minimized = false;
			}
			GameOverlay.Drag(_panel, new Rectangle(bar.X, bar.Y, bar.Width - 40, bar.Height), screenW, screenH);
			return;
		}
		int height = HeaderH + border + fieldH + border + 12 + 26;
		Rectangle r = GameOverlay.Place(_panel, width, height, screenW, screenH, 0);
		GameOverlay.Window(sb, r, HeaderH);
		GameOverlay.Grip(sb, r);
		if (DrawHeader(sb, r))
		{
			return;
		}
		float shake = 1f - GameArt.Clamp01((GameArt.Now - _shakeAt) / 0.3);
		int jolt = (shake > 0f) ? (int)(Math.Sin(GameArt.Now * 70.0) * 4f * shake) : 0;
		Rectangle field = new Rectangle(r.X + 12 + border + jolt, r.Y + HeaderH + border, fieldW, fieldH);
		DrawField(sb, field, cell, border);
		DrawSide(sb, new Rectangle(field.Right + border + 12 - jolt, r.Y + HeaderH, Side, fieldH + border * 2));
		DrawOverlay(sb, field);
		if (_focused && _state == State.Playing)
		{
			// Your character stands still while the keys are the game's.
			PlayerInput.WritingText = true;
			Main.chatRelease = false;
		}
		string help = IsaClient.Languages.L10n.T("Arrows/WASD turn · P pause · Space start");
		float hw = Math.Min(MusicDrawing.Measure(help, 0.6f), r.Width - 24);
		MusicDrawing.RawText(sb, help, r.X + (r.Width - hw) / 2f, field.Bottom + border + 10, MusicPlayerWindow.MutedColor * 0.85f, 0.6f, r.Width - 24);
		if (MusicPlayerWindow.Clicked && MusicPlayerWindow.MouseIn(field))
		{
			MusicPlayerWindow.Clicked = false;
			_focused = true;
			if (_state == State.Paused)
			{
				_state = State.Playing;
				_nextStep = GameArt.Now + _stepEvery;
			}
		}
		if (MusicPlayerWindow.Clicked && MusicPlayerWindow.MouseIn(r))
		{
			_focused = true;
		}
		GameOverlay.Drag(_panel, new Rectangle(r.X, r.Y, r.Width - 76, HeaderH - 4), screenW, screenH);
	}

	/// <summary>A little snake of bricks with an apple.</summary>
	public static void DrawIcon(SpriteBatch sb, Rectangle area)
	{
		int c = area.Width / 3;
		GameArt.Sharp(sb);
		Brick(sb, Skins[Skin], new Rectangle(area.X, area.Y + c * 2, c, c), SkinTint(2, 3));
		Brick(sb, Skins[Skin], new Rectangle(area.X + c, area.Y + c * 2, c, c), SkinTint(1, 3));
		Brick(sb, Skins[Skin], new Rectangle(area.X + c, area.Y + c, c, c), SkinTint(0, 3));
		Item(sb, Fruits[0], new Rectangle(area.X + c * 2, area.Y, c, c), Color.White);
		GameArt.Smooth(sb);
	}

	private static bool DrawHeader(SpriteBatch sb, Rectangle r)
	{
		DrawIcon(sb, new Rectangle(r.X + 12, r.Y + 8, 30, 30));
		MusicDrawing.RawText(sb, "Snake", r.X + 50, r.Y + 9, MusicPlayerWindow.AccentColor, 0.86f);
		int best = Scores.Best(Scores.Snake);
		int rank = Scores.RankOf(Scores.Snake, ClientIdentity.Id);
		int total = Scores.Ranking(Scores.Snake).Count;
		string mode = SpeedOption.Current + " · " + WallsOption.Current;
		string sub = (best > 0) ? IsaClient.Languages.L10n.F("Your best {0}  ·  #{1} of {2}", best.ToString("N0"), rank, total) : IsaClient.Languages.L10n.T("No score yet: play a game!");
		MusicDrawing.RawText(sb, sub + "  ·  " + mode, r.X + 50, r.Y + 28, MusicPlayerWindow.MutedColor, 0.64f, r.Width - 140);
		if (MusicDrawing.TextButton(sb, "snake:min", new Rectangle(r.Right - 66, r.Y + 11, 24, 22), "-", MusicPlayerWindow.PanelColor, "Minimize"))
		{
			_minimized = true;
			_focused = false;
		}
		if (MusicDrawing.BareIcon(sb, "snake:close", new Rectangle(r.Right - 34, r.Y + 13, 17, 18), MusicDrawing.Tex("Images/MapDeath"), null, "Close", Color.White))
		{
			Close();
			return true;
		}
		return false;
	}

	/// <summary>A brick of a tile (its lone-block frame, so each one has its border). In a sharp batch.</summary>
	private static void Brick(SpriteBatch sb, ushort tile, Rectangle r, Color color)
	{
		try
		{
			Main.instance.LoadTiles(tile);
			sb.Draw(TextureAssets.Tile[tile].Value, r, new Rectangle(162, 54, 16, 16), color);
		}
		catch
		{
			sb.Draw(TextureAssets.MagicPixel.Value, r, new Rectangle(0, 0, 1, 1), color);
		}
	}

	/// <summary>An item's picture fitted in a box (its first frame when it has several).</summary>
	private static void Item(SpriteBatch sb, int type, Rectangle box, Color color, float rotation = 0f)
	{
		try
		{
			Main.instance.LoadItem(type);
			Texture2D tex = TextureAssets.Item[type].Value;
			Rectangle frame = (Main.itemAnimations[type] != null) ? Main.itemAnimations[type].GetFrame(tex) : tex.Frame();
			float scale = Math.Min((float)box.Width / frame.Width, (float)box.Height / frame.Height);
			sb.Draw(tex, new Vector2(box.Center.X, box.Center.Y), frame, color, rotation, new Vector2(frame.Width, frame.Height) / 2f, scale, SpriteEffects.None, 0f);
		}
		catch
		{
		}
	}

	/// <summary>The color of piece <paramref name="i"/> of <paramref name="count"/>: rainbow along the body, else a soft stripe.</summary>
	private static Color SkinTint(int i, int count)
	{
		if (Skin == 1)
		{
			float hue = (float)((i * 0.045 + GameArt.Now * 0.25) % 1.0);
			return Main.hslToRgb(hue, 0.9f, 0.62f);
		}
		return (i % 2 == 0) ? Color.White : new Color(215, 215, 215);
	}

	/// <summary>Grass, the walls (stone when solid, leaves when you can go through), the fruit and the snake.</summary>
	private static void DrawField(SpriteBatch sb, Rectangle field, int cell, int border)
	{
		double now = GameArt.Now;
		// The grass: a checkerboard, darker at the bottom.
		GameArt.Gradient(sb, field, new Color(38, 74, 40), new Color(24, 50, 30));
		for (int y = 0; y < Rows; y++)
		{
			for (int x = (y & 1); x < Cols; x += 2)
			{
				MusicDrawing.Fill(sb, new Rectangle(field.X + x * cell, field.Y + y * cell, cell, cell), Color.Black * 0.09f);
			}
		}
		GameArt.Sharp(sb);
		ushort wall = (Walls == 1) ? TileID.GrayBrick : TileID.LeafBlock;
		Color wallColor = (Walls == 1) ? Color.White : (Color.White * 0.55f);
		for (int x = -1; x <= Cols * 2; x++)
		{
			Brick(sb, wall, new Rectangle(field.X + x * border, field.Y - border, border, border), wallColor);
			Brick(sb, wall, new Rectangle(field.X + x * border, field.Bottom, border, border), wallColor);
		}
		for (int y = 0; y < Rows * 2; y++)
		{
			Brick(sb, wall, new Rectangle(field.X - border, field.Y + y * border, border, border), wallColor);
			Brick(sb, wall, new Rectangle(field.Right, field.Y + y * border, border, border), wallColor);
		}
		GameArt.Smooth(sb);
		Rectangle Cell(Vector2 p, float size)
		{
			float s = cell * size;
			return new Rectangle((int)(field.X + (p.X + 0.5f) * cell - s / 2f), (int)(field.Y + (p.Y + 0.5f) * cell - s / 2f), (int)s, (int)s);
		}
		// The fruit: it pops in, bobs and glows.
		if (_food.X >= 0 && _body.Count > 0)
		{
			float pop = GameArt.EaseBack(GameArt.Clamp01((now - _foodAt) / 0.3));
			Vector2 at = new Vector2(_food.X, _food.Y - 0.08f * (float)Math.Sin(now * 4.0));
			GameArt.Disc(sb, new Vector2(field.X + (at.X + 0.5f) * cell, field.Y + (_food.Y + 0.85f) * cell), cell * 0.8f, cell * 0.3f, Color.Black * 0.35f);
			GameArt.Disc(sb, new Vector2(field.X + (at.X + 0.5f) * cell, field.Y + (at.Y + 0.5f) * cell), cell * 1.6f, cell * 1.6f, Juice[_foodKind] * 0.25f);
			GameArt.Sharp(sb);
			Item(sb, Fruits[_foodKind], Cell(at, 0.95f * pop), Color.White);
			GameArt.Smooth(sb);
		}
		// The life fruit: blinks when it's about to go, with its time as a bar.
		if (_bonus.X >= 0)
		{
			double left = _bonusUntil - now;
			bool blink = left < 2.0 && (int)(now * 8.0) % 2 == 0;
			Vector2 at = new Vector2(_bonus.X, _bonus.Y);
			GameArt.Disc(sb, new Vector2(field.X + (at.X + 0.5f) * cell, field.Y + (at.Y + 0.5f) * cell), cell * (1.8f + 0.3f * GameArt.Pulse(0.8)), cell * (1.8f + 0.3f * GameArt.Pulse(0.8)), new Color(255, 80, 110) * 0.35f);
			GameArt.Sharp(sb);
			Item(sb, BonusItem, Cell(at, 1.05f), Color.White * (blink ? 0.35f : 1f));
			GameArt.Smooth(sb);
			Rectangle c = Cell(at, 1f);
			float part = GameArt.Clamp01(left / BonusTime);
			MusicDrawing.Fill(sb, new Rectangle(c.X, c.Bottom + 1, c.Width, 3), Color.Black * 0.5f);
			MusicDrawing.Fill(sb, new Rectangle(c.X, c.Bottom + 1, (int)(c.Width * part), 3), new Color(255, 90, 120));
		}
		DrawSnake(sb, field, cell);
		// Juice and points.
		for (int i = _sparks.Count - 1; i >= 0; i--)
		{
			Spark s = _sparks[i];
			float age = (float)(now - s.Born);
			if (age > 0.6f)
			{
				_sparks.RemoveAt(i);
				continue;
			}
			Vector2 p = s.At + s.Speed * age + new Vector2(0f, 3f * age * age);
			int size = Math.Max(2, (int)(cell * 0.22f * (1f - age / 0.6f)));
			MusicDrawing.Fill(sb, new Rectangle((int)(field.X + p.X * cell) - size / 2, (int)(field.Y + p.Y * cell) - size / 2, size, size), s.Color * (1f - age / 0.6f));
		}
		for (int i = _popups.Count - 1; i >= 0; i--)
		{
			Popup p = _popups[i];
			float age = (float)(now - p.Born);
			if (age > 0.9f)
			{
				_popups.RemoveAt(i);
				continue;
			}
			float w = MusicDrawing.Measure(p.Text, 0.7f);
			MusicDrawing.RawText(sb, p.Text, field.X + p.At.X * cell - w / 2f, field.Y + (p.At.Y - 1f - age * 1.2f) * cell, p.Color * (1f - age / 0.9f), 0.7f);
		}
	}

	/// <summary>The body slides from cell to cell; the tail thins out, the head has eyes (and a tongue now and then).</summary>
	private static void DrawSnake(SpriteBatch sb, Rectangle field, int cell)
	{
		if (_body.Count == 0)
		{
			return;
		}
		double now = GameArt.Now;
		float t = (_state == State.Playing) ? GameArt.Clamp01(1.0 - (_nextStep - now) / _stepEvery) : 1f;
		float stone = (_state == State.Over) ? GameArt.Clamp01((now - _overAt) / 0.8) : 0f;
		Vector2 Where(int i)
		{
			Point to = _body[i];
			Point from = (i < _prev.Count) ? _prev[i] : to;
			if (Math.Abs(to.X - from.X) > 1 || Math.Abs(to.Y - from.Y) > 1)
			{
				// Through the edge (wrap): no sliding across the field.
				return new Vector2(to.X, to.Y);
			}
			return Vector2.Lerp(new Vector2(from.X, from.Y), new Vector2(to.X, to.Y), t);
		}
		Rectangle Box(Vector2 p, float size)
		{
			float s = cell * size;
			return new Rectangle((int)Math.Round(field.X + (p.X + 0.5f) * cell - s / 2f), (int)Math.Round(field.Y + (p.Y + 0.5f) * cell - s / 2f), (int)s, (int)s);
		}
		int count = _body.Count;
		ushort tile = Skins[Skin];
		// A soft shadow under the body first.
		for (int i = count - 1; i >= 0; i--)
		{
			Vector2 p = Where(i);
			GameArt.Disc(sb, new Vector2(field.X + (p.X + 0.5f) * cell + 2, field.Y + (p.Y + 0.5f) * cell + 4), cell * 1.1f, cell * 0.8f, Color.Black * 0.22f);
		}
		GameArt.Sharp(sb);
		for (int i = count - 1; i >= 0; i--)
		{
			// The last pieces get thinner.
			int fromTail = count - 1 - i;
			float size = (i == 0) ? 1.08f : ((fromTail < 3) ? (0.72f + fromTail * 0.08f) : 0.96f);
			Color color = SkinTint(i, count);
			if (stone > 0f && i < stone * count * 1.3f)
			{
				color = Color.Lerp(color, new Color(90, 90, 100), 0.75f);
			}
			Brick(sb, tile, Box(Where(i), size), color);
		}
		GameArt.Smooth(sb);
		// The head: eyes looking where it goes; crossed out when it crashed.
		Rectangle head = Box(Where(0), 1.08f);
		Vector2 c = new Vector2(head.Center.X, head.Center.Y);
		Vector2 fwd = new Vector2(_dir.X, _dir.Y);
		Vector2 sideV = new Vector2(-_dir.Y, _dir.X);
		float e = cell * 0.22f;
		int eye = Math.Max(3, (int)(cell * 0.24f));
		for (int k = -1; k <= 1; k += 2)
		{
			Vector2 at = c + fwd * (cell * 0.12f) + sideV * (e * k);
			Rectangle white = new Rectangle((int)(at.X - eye / 2f), (int)(at.Y - eye / 2f), eye, eye);
			MusicDrawing.Fill(sb, white, Color.White);
			if (_state == State.Over)
			{
				MusicDrawing.Fill(sb, new Rectangle(white.X, white.Center.Y - 1, white.Width, 2), Color.Black);
			}
			else
			{
				int pupil = Math.Max(2, eye / 2);
				Vector2 look = at + fwd * (eye * 0.22f);
				MusicDrawing.Fill(sb, new Rectangle((int)(look.X - pupil / 2f), (int)(look.Y - pupil / 2f), pupil, pupil), Color.Black);
			}
		}
		// A flick of the tongue every second or so.
		if (_state != State.Over && now % 1.4 < 0.22)
		{
			Vector2 from = c + fwd * (cell * 0.55f);
			Vector2 to = from + fwd * (cell * 0.35f);
			GameArt.Line(sb, from, to, 2f, new Color(230, 40, 60));
			GameArt.Line(sb, to, to + (fwd + sideV) * (cell * 0.12f), 2f, new Color(230, 40, 60));
			GameArt.Line(sb, to, to + (fwd - sideV) * (cell * 0.12f), 2f, new Color(230, 40, 60));
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

	/// <summary>Score, length and best, then the top of the shared board.</summary>
	private static void DrawSide(SpriteBatch sb, Rectangle area)
	{
		int best = Math.Max(Scores.Best(Scores.Snake), _score);
		int y = Stat(sb, area.X, area.Y, area.Width, "Score", _score.ToString("N0"), Color.White, 0.95f);
		y = Stat(sb, area.X, y, area.Width, "Length", _body.Count.ToString(), SkinGlow[Skin], 0.85f);
		y = Stat(sb, area.X, y, area.Width, "Best", (best > 0) ? best.ToString("N0") : "-", MusicPlayerWindow.AccentColor, 0.85f);
		Tetris.DrawTop(sb, Scores.Snake, new Rectangle(area.X, y + 4, area.Width, area.Bottom - y - 4));
	}

	/// <summary>Start, paused, game over: a card over the field.</summary>
	private static void DrawOverlay(SpriteBatch sb, Rectangle field)
	{
		if (_state == State.Playing)
		{
			return;
		}
		MusicDrawing.Fill(sb, field, Color.Black * ((_state == State.Over) ? 0.35f * GameArt.Clamp01((GameArt.Now - _overAt - 0.6) / 0.4) : 0.45f));
		if (_state == State.Over && GameArt.Now - _overAt < 0.9)
		{
			return;
		}
		float appear = (_state == State.Over) ? GameArt.EaseBack(GameArt.Clamp01((GameArt.Now - _overAt - 0.9) / 0.35)) : 1f;
		int cardW = Math.Min(300, field.Width - 40);
		Rectangle card = new Rectangle(field.Center.X - cardW / 2, field.Center.Y - 90 + (int)((1f - appear) * 30f), cardW, 180);
		GameOverlay.Window(sb, card, 30);
		string title = (_state == State.Idle) ? "Snake" : ((_state == State.Paused) ? IsaClient.Languages.L10n.T("Paused") : IsaClient.Languages.L10n.T("Game over"));
		Tetris.Title(sb, title, card, (_state == State.Over) ? new Color(255, 140, 140) : MusicPlayerWindow.AccentColor);
		int y = card.Y + 58;
		if (_state == State.Over)
		{
			Tetris.Centered(sb, IsaClient.Languages.L10n.F("{0}  ·  length {1}", _score.ToString("N0"), _body.Count), card, y, Color.White, 0.9f);
			int rank = Scores.RankOf(Scores.Snake, ClientIdentity.Id);
			int total = Scores.Ranking(Scores.Snake).Count;
			string line = _newBest ? IsaClient.Languages.L10n.F("New best!  #{0} of {1}", rank, total) : IsaClient.Languages.L10n.F("Best {0}  ·  #{1} of {2}", Scores.Best(Scores.Snake).ToString("N0"), rank, total);
			Tetris.Centered(sb, line, card, y + 26, _newBest ? new Color(255, 214, 80) : MusicPlayerWindow.MutedColor, 0.7f);
		}
		else
		{
			Tetris.Centered(sb, (_state == State.Paused) ? IsaClient.Languages.L10n.T("Click the field or press P to go on.") : IsaClient.Languages.L10n.T("Eat the fruit, don't bite yourself."), card, y + 8, MusicPlayerWindow.MutedColor, 0.66f);
		}
		string button = (_state == State.Paused) ? "Continue" : ((_state == State.Over) ? "Play again" : "Start");
		if (MusicDrawing.TextButton(sb, "snake:play", new Rectangle(card.Center.X - 70, card.Bottom - 46, 140, 32), button, new Color(60, 140, 80)))
		{
			MusicPlayerWindow.Clicked = false;
			if (_state == State.Paused)
			{
				_state = State.Playing;
				_focused = true;
				_nextStep = GameArt.Now + _stepEvery;
			}
			else
			{
				NewGame();
			}
		}
	}

	/// <summary>
	/// The panel's card: a snake going round a little field, eating. <paramref name="sharp"/> restarts the batch for
	/// pixel art inside the panel's clip; the caller restarts it after.
	/// </summary>
	public static void DrawPreview(SpriteBatch sb, Rectangle art, Action<SpriteBatch> sharp)
	{
		int cell = art.Width / 8;
		int rows = art.Height / cell;
		MusicDrawing.Fill(sb, art, new Color(24, 50, 30));
		for (int y = 0; y < rows; y++)
		{
			for (int x = (y & 1); x < 8; x += 2)
			{
				MusicDrawing.Fill(sb, new Rectangle(art.X + x * cell, art.Y + y * cell, cell, cell), new Color(38, 74, 40));
			}
		}
		// A loop one cell in from the edge.
		List<Point> loop = new List<Point>();
		for (int x = 1; x < 7; x++)
		{
			loop.Add(new Point(x, 1));
		}
		for (int y = 2; y < rows - 1; y++)
		{
			loop.Add(new Point(6, y));
		}
		for (int x = 5; x >= 1; x--)
		{
			loop.Add(new Point(x, rows - 2));
		}
		for (int y = rows - 3; y >= 2; y--)
		{
			loop.Add(new Point(1, y));
		}
		double steps = GameArt.Now * 6.0;
		int head = (int)steps % loop.Count;
		float t = (float)(steps - Math.Floor(steps));
		int length = 6;
		Point fruit = loop[(head + 9) % loop.Count];
		sharp(sb);
		Item(sb, Fruits[(int)(steps / loop.Count) % Fruits.Length], new Rectangle(art.X + fruit.X * cell + 2, art.Y + fruit.Y * cell + 2, cell - 4, cell - 4), Color.White);
		for (int i = length - 1; i >= 0; i--)
		{
			Point to = loop[(head - i + loop.Count * 2) % loop.Count];
			Point from = loop[(head - i - 1 + loop.Count * 2) % loop.Count];
			Vector2 p = Vector2.Lerp(new Vector2(from.X, from.Y), new Vector2(to.X, to.Y), t);
			int size = (i >= length - 2) ? (cell - 4) : cell;
			Brick(sb, Skins[Skin], new Rectangle((int)(art.X + p.X * cell) + (cell - size) / 2, (int)(art.Y + p.Y * cell) + (cell - size) / 2, size, size), SkinTint(i, length));
		}
	}

	#endregion
}
