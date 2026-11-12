using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;

namespace IsaClient.Games;

/// <summary>
/// One Tetris well and its rules, used by the solo window (<see cref="Tetris"/>) and by versus games
/// (<see cref="TetrisBattle"/>). Kept simple: a 7-piece bag, three next pieces, hold, ghost piece, soft and hard drop,
/// half a second to lock (a few moves buy a little more), levels that speed up (how fast: the speed option). In versus, clearing 2, 3 or 4 lines
/// sends 1, 2 or 4 garbage lines (gray rows with one hole) that push the other well up from below; lines you clear
/// first cancel garbage on its way to you. Blocks are Terraria's gemspark (one gem per piece), garbage gray brick.
/// </summary>
public sealed class TetrisField
{
	public const int Cols = 10;

	public const int Rows = 20;

	/// <summary>Rows above the top where pieces appear.</summary>
	public const int Hidden = 2;

	/// <summary>Kinds: 1 I, 2 O, 3 T, 4 S, 5 Z, 6 J, 7 L, 8 garbage.</summary>
	private static readonly ushort[] Tiles = { 0, TileID.DiamondGemspark, TileID.TopazGemspark, TileID.AmethystGemspark, TileID.EmeraldGemspark, TileID.RubyGemspark, TileID.SapphireGemspark, TileID.AmberGemspark, TileID.GrayBrick };

	private static readonly Color[] Glow =
	{
		Color.White, new Color(200, 240, 255), new Color(255, 220, 90), new Color(205, 120, 255), new Color(110, 235, 120),
		new Color(255, 90, 90), new Color(90, 150, 255), new Color(255, 160, 60), new Color(150, 150, 160)
	};

	private static readonly int[][] Shapes =
	{
		null,
		new[] { 0, 1, 1, 1, 2, 1, 3, 1 },
		new[] { 0, 0, 1, 0, 0, 1, 1, 1 },
		new[] { 1, 0, 0, 1, 1, 1, 2, 1 },
		new[] { 1, 0, 2, 0, 0, 1, 1, 1 },
		new[] { 0, 0, 1, 0, 1, 1, 2, 1 },
		new[] { 0, 0, 0, 1, 1, 1, 2, 1 },
		new[] { 2, 0, 0, 1, 1, 1, 2, 1 }
	};

	private static readonly int[] BoxSize = { 0, 4, 2, 3, 3, 3, 3, 3 };

	private static readonly Point[][][] Rotations = BuildRotations();

	/// <summary>Ticks per row by level (1..).</summary>
	private static readonly int[] Gravity = { 30, 25, 21, 17, 14, 11, 9, 7, 6, 5, 4, 4, 3, 3, 2, 2, 2, 1 };

	/// <summary>Per speed (slow, normal, fast): how much slower than the table pieces fall, and lines per level.</summary>
	private static readonly double[] SpeedScale = { 1.7, 1.3, 1.0 };

	private static readonly int[] SpeedLines = { 10, 8, 6 };

	private readonly int _speed;

	private int LinesPerLevel => SpeedLines[_speed];

	/// <summary>A well; <paramref name="speed"/> 0 slow, 1 normal, 2 fast.</summary>
	public TetrisField(int speed = 1)
	{
		_speed = Math.Max(0, Math.Min(2, speed));
	}

	private static readonly int[] LinePoints = { 0, 100, 300, 500, 800 };

	/// <summary>Garbage sent for 0..4 lines cleared at once.</summary>
	private static readonly int[] Attack = { 0, 0, 1, 2, 4 };

	private readonly int[,] _grid = new int[Rows + Hidden, Cols];

	private readonly Random _random = new Random();

	private int _kind;

	private int _rot;

	private int _x;

	private int _y;

	private readonly List<int> _bag = new List<int>();

	private readonly List<int> _next = new List<int>();

	private int _fall;

	private int _lockTicks;

	private int _lockMoves;

	private int _lowest;

	private readonly List<int> _clearing = new List<int>();

	private double _clearAt;

	private int _dasDir;

	private int _dasTicks;

	private int _softTicks;

	public int Score { get; private set; }

	public int Lines { get; private set; }

	public int Level { get; private set; } = 1;

	public int Hold { get; private set; }

	public bool CanHold { get; private set; }

	public IReadOnlyList<int> Next => _next;

	public bool Started { get; private set; }

	/// <summary>Topped out.</summary>
	public bool Over { get; private set; }

	public double OverAt { get; private set; }

	/// <summary>Garbage on its way to this well (it rises after the next lock that clears nothing).</summary>
	public int PendingGarbage { get; private set; }

	/// <summary>Versus: garbage lines this well sends to the other one.</summary>
	public Action<int> SendGarbage;

	/// <summary>Set when a piece locks (the versus game sends the well to the other player then).</summary>
	public bool Locked;

	#region Effects

	private sealed class Particle
	{
		public Vector2 At;

		public Vector2 Speed;

		public int Kind;

		public double Born;

		public float Spin;
	}

	private sealed class Popup
	{
		public string Text;

		public Color Color;

		public double Born;

		public float Scale;
	}

	private readonly List<Particle> _particles = new List<Particle>();

	private readonly List<Popup> _popups = new List<Popup>();

	private double _shakeAt = -10.0;

	private double _trailAt = -10.0;

	private int _trailKind;

	private Point[] _trailCells;

	private int _trailFrom;

	private double _garbageAt = -10.0;

	#endregion

	private static Point[][][] BuildRotations()
	{
		Point[][][] all = new Point[8][][];
		for (int kind = 1; kind <= 7; kind++)
		{
			all[kind] = new Point[4][];
			int n = BoxSize[kind];
			Point[] cells = new Point[4];
			for (int i = 0; i < 4; i++)
			{
				cells[i] = new Point(Shapes[kind][i * 2], Shapes[kind][i * 2 + 1]);
			}
			for (int r = 0; r < 4; r++)
			{
				all[kind][r] = cells;
				cells = cells.Select(c => (kind == 2) ? c : new Point(n - 1 - c.Y, c.X)).ToArray();
			}
		}
		return all;
	}

	#region The game

	public void Start()
	{
		Array.Clear(_grid, 0, _grid.Length);
		_bag.Clear();
		_next.Clear();
		_clearing.Clear();
		_particles.Clear();
		_popups.Clear();
		Hold = 0;
		Score = 0;
		Lines = 0;
		Level = 1;
		PendingGarbage = 0;
		Over = false;
		Started = true;
		Spawn(Take());
	}

	private int Take()
	{
		while (_next.Count < 4)
		{
			if (_bag.Count == 0)
			{
				_bag.AddRange(Enumerable.Range(1, 7).OrderBy(_ => _random.Next()));
			}
			_next.Add(_bag[0]);
			_bag.RemoveAt(0);
		}
		int kind = _next[0];
		_next.RemoveAt(0);
		return kind;
	}

	private void Spawn(int kind)
	{
		_kind = kind;
		_rot = 0;
		_x = (kind == 2) ? 4 : 3;
		_y = 0;
		_fall = 0;
		_lockTicks = 0;
		_lockMoves = 0;
		_lowest = 0;
		CanHold = true;
		if (Collides(_kind, _rot, _x, _y))
		{
			TopOut();
		}
	}

	private bool Collides(int kind, int rot, int x, int y)
	{
		foreach (Point c in Rotations[kind][rot])
		{
			int cx = x + c.X;
			int cy = y + c.Y;
			if (cx < 0 || cx >= Cols || cy < 0 || cy >= Rows + Hidden || _grid[cy, cx] != 0)
			{
				return true;
			}
		}
		return false;
	}

	private bool Move(int dx, int dy)
	{
		if (Collides(_kind, _rot, _x + dx, _y + dy))
		{
			return false;
		}
		_x += dx;
		_y += dy;
		if (dx != 0)
		{
			Touched();
		}
		return true;
	}

	private void Rotate(int dir)
	{
		if (_kind == 2)
		{
			return;
		}
		int rot = (_rot + dir + 4) % 4;
		int[][] kicks = { new[] { 0, 0 }, new[] { -1, 0 }, new[] { 1, 0 }, new[] { -2, 0 }, new[] { 2, 0 }, new[] { 0, 1 } };
		foreach (int[] k in kicks)
		{
			if (!Collides(_kind, rot, _x + k[0], _y + k[1]))
			{
				_rot = rot;
				_x += k[0];
				_y += k[1];
				Touched();
				SoundEngine.PlaySound(12);
				return;
			}
		}
	}

	/// <summary>A move or turn on the ground buys a little more time, only a few times per row reached.</summary>
	private void Touched()
	{
		if (_lockTicks > 0 && _lockMoves < 4)
		{
			_lockTicks = Math.Max(0, _lockTicks - 15);
			_lockMoves++;
		}
	}

	private int DropDistance()
	{
		int d = 0;
		while (!Collides(_kind, _rot, _x, _y + d + 1))
		{
			d++;
		}
		return d;
	}

	private void HardDrop()
	{
		int d = DropDistance();
		_trailCells = Rotations[_kind][_rot].Select(c => new Point(_x + c.X, _y + c.Y)).ToArray();
		_trailFrom = d;
		_trailKind = _kind;
		_trailAt = GameArt.Now;
		_y += d;
		Score += d * 2;
		_shakeAt = GameArt.Now;
		Lock();
	}

	private void HoldPiece()
	{
		if (!CanHold)
		{
			return;
		}
		int held = Hold;
		Hold = _kind;
		Spawn((held != 0) ? held : Take());
		CanHold = false;
		SoundEngine.PlaySound(12);
	}

	private void Lock()
	{
		bool allHidden = true;
		foreach (Point c in Rotations[_kind][_rot])
		{
			_grid[_y + c.Y, _x + c.X] = _kind;
			allHidden &= _y + c.Y < Hidden;
		}
		Locked = true;
		if (allHidden)
		{
			TopOut();
			return;
		}
		SoundEngine.PlaySound(SoundID.Dig);
		_clearing.Clear();
		for (int row = 0; row < Rows + Hidden; row++)
		{
			bool full = true;
			for (int col = 0; col < Cols && full; col++)
			{
				full = _grid[row, col] != 0;
			}
			if (full)
			{
				_clearing.Add(row);
			}
		}
		if (_clearing.Count == 0)
		{
			// Nothing cleared: garbage on its way comes up now.
			if (PendingGarbage > 0)
			{
				RaiseGarbage(PendingGarbage);
				PendingGarbage = 0;
				if (Over)
				{
					return;
				}
			}
			Spawn(Take());
			return;
		}
		int n = _clearing.Count;
		Score += LinePoints[Math.Min(4, n)] * Level;
		Lines += n;
		_clearAt = GameArt.Now;
		_kind = 0;
		// Versus: what you clear cancels garbage coming to you first, the rest goes to the other well.
		int attack = Attack[Math.Min(4, n)];
		int cancel = Math.Min(attack, PendingGarbage);
		PendingGarbage -= cancel;
		attack -= cancel;
		if (attack > 0 && SendGarbage != null)
		{
			SendGarbage(attack);
			AddPopup(IsaClient.Languages.L10n.F("+{0} sent!", attack), new Color(255, 140, 120), 0.6f);
		}
		foreach (int row in _clearing)
		{
			for (int col = 0; col < Cols; col++)
			{
				for (int k = 0; k < 2; k++)
				{
					_particles.Add(new Particle
					{
						At = new Vector2(col + 0.5f, row - Hidden + 0.5f),
						Speed = new Vector2((float)(_random.NextDouble() * 2.0 - 1.0) * 5f, -(float)(_random.NextDouble() * 6.0 + 2.0)),
						Kind = _grid[row, col],
						Born = GameArt.Now,
						Spin = (float)(_random.NextDouble() * 8.0 - 4.0)
					});
				}
			}
		}
		if (n == 4)
		{
			AddPopup("TETRIS!", new Color(255, 214, 80), 1.1f);
			SoundEngine.PlaySound(SoundID.Item4);
		}
		else
		{
			AddPopup((n == 1) ? IsaClient.Languages.L10n.T("Single") : ((n == 2) ? IsaClient.Languages.L10n.T("tetris::Double") : IsaClient.Languages.L10n.T("Triple")), Color.White, 0.75f);
			SoundEngine.PlaySound(SoundID.Item37);
		}
		int level = 1 + Lines / LinesPerLevel;
		if (level > Level)
		{
			Level = level;
			AddPopup(IsaClient.Languages.L10n.F("Level {0}", Level), new Color(140, 220, 255), 0.85f);
		}
	}

	private void FinishClear()
	{
		foreach (int row in _clearing.OrderBy(r => r))
		{
			for (int r = row; r > 0; r--)
			{
				for (int c = 0; c < Cols; c++)
				{
					_grid[r, c] = _grid[r - 1, c];
				}
			}
			for (int c = 0; c < Cols; c++)
			{
				_grid[0, c] = 0;
			}
		}
		_clearing.Clear();
		Spawn(Take());
	}

	/// <summary>Versus: garbage from the other player, waiting until your next lock.</summary>
	public void AddGarbage(int lines)
	{
		if (Over || lines <= 0)
		{
			return;
		}
		PendingGarbage = Math.Min(20, PendingGarbage + lines);
		SoundEngine.PlaySound(SoundID.Item10);
	}

	/// <summary>Gray rows with one hole (the same column) push everything up from the bottom.</summary>
	private void RaiseGarbage(int lines)
	{
		lines = Math.Min(lines, Rows + Hidden);
		for (int row = 0; row < lines; row++)
		{
			for (int c = 0; c < Cols; c++)
			{
				if (_grid[row, c] != 0)
				{
					// Pushed out of the top: that's the end.
					TopOut();
				}
			}
		}
		for (int row = 0; row < Rows + Hidden - lines; row++)
		{
			for (int c = 0; c < Cols; c++)
			{
				_grid[row, c] = _grid[row + lines, c];
			}
		}
		int hole = _random.Next(Cols);
		for (int row = Rows + Hidden - lines; row < Rows + Hidden; row++)
		{
			for (int c = 0; c < Cols; c++)
			{
				_grid[row, c] = (c == hole) ? 0 : 8;
			}
		}
		_garbageAt = GameArt.Now;
		_shakeAt = GameArt.Now;
		SoundEngine.PlaySound(SoundID.Item14);
	}

	private void TopOut()
	{
		if (Over)
		{
			return;
		}
		Over = true;
		OverAt = GameArt.Now;
		_kind = 0;
	}

	private void AddPopup(string text, Color color, float scale)
	{
		_popups.Add(new Popup { Text = text, Color = color, Born = GameArt.Now, Scale = scale });
	}

	private static bool Down(KeyboardState keys, params Keys[] any)
	{
		return any.Any(keys.IsKeyDown);
	}

	private static bool Pressed(KeyboardState keys, KeyboardState before, params Keys[] any)
	{
		return any.Any(k => keys.IsKeyDown(k) && !before.IsKeyDown(k));
	}

	/// <summary>One tick: the keys (when <paramref name="input"/>), falling and locking.</summary>
	public void Step(KeyboardState keys, KeyboardState before, bool input)
	{
		if (!Started || Over)
		{
			return;
		}
		if (_clearing.Count > 0)
		{
			if (GameArt.Now - _clearAt > 0.32)
			{
				FinishClear();
			}
			return;
		}
		if (_kind == 0)
		{
			return;
		}
		if (input)
		{
			if (Pressed(keys, before, Keys.Space))
			{
				HardDrop();
				return;
			}
			if (Pressed(keys, before, Keys.C, Keys.LeftShift, Keys.RightShift))
			{
				HoldPiece();
			}
			if (Pressed(keys, before, Keys.Up, Keys.W, Keys.X))
			{
				Rotate(1);
			}
			if (Pressed(keys, before, Keys.Z, Keys.Q))
			{
				Rotate(-1);
			}
			int dir = (Down(keys, Keys.Left, Keys.A) ? -1 : 0) + (Down(keys, Keys.Right, Keys.D) ? 1 : 0);
			if (dir != _dasDir)
			{
				_dasDir = dir;
				_dasTicks = 0;
				if (dir != 0)
				{
					Move(dir, 0);
				}
			}
			else if (dir != 0 && ++_dasTicks >= 10 && (_dasTicks - 10) % 2 == 0)
			{
				Move(dir, 0);
			}
			bool soft = Down(keys, Keys.Down, Keys.S);
			if (soft && ++_softTicks % 2 == 0 && Move(0, 1))
			{
				Score++;
				_fall = 0;
			}
			if (!soft)
			{
				_softTicks = 0;
			}
		}
		if (++_fall >= Math.Max(1, (int)Math.Round(Gravity[Math.Min(Level - 1, Gravity.Length - 1)] * SpeedScale[_speed])))
		{
			_fall = 0;
			Move(0, 1);
		}
		if (_y > _lowest)
		{
			_lowest = _y;
			_lockTicks = 0;
			_lockMoves = 0;
		}
		if (Collides(_kind, _rot, _x, _y + 1) && ++_lockTicks >= 30)
		{
			Lock();
		}
	}

	/// <summary>
	/// The visible well with the falling piece in it, as kinds (0 empty), [row, col] with row 0 the top; for the well
	/// built with blocks (<see cref="TetrisArt"/>).
	/// </summary>
	public int[,] VisibleKinds()
	{
		int[,] kinds = new int[Rows, Cols];
		for (int row = 0; row < Rows; row++)
		{
			for (int c = 0; c < Cols; c++)
			{
				kinds[row, c] = _grid[row + Hidden, c];
			}
		}
		if (!Over && _kind != 0 && _clearing.Count == 0)
		{
			foreach (Point c in Rotations[_kind][_rot])
			{
				int row = _y + c.Y - Hidden;
				if (row >= 0 && row < Rows && _x + c.X >= 0 && _x + c.X < Cols)
				{
					kinds[row, _x + c.X] = _kind;
				}
			}
		}
		return kinds;
	}

	/// <summary>The gemspark tile of a kind (gray brick for garbage).</summary>
	public static ushort TileOf(int kind)
	{
		return Tiles[kind];
	}

	/// <summary>The visible well as one row bitmask per row (top first), for the other player's view.</summary>
	public byte[] Snapshot()
	{
		byte[] data = new byte[Rows * 2];
		for (int row = 0; row < Rows; row++)
		{
			int mask = 0;
			for (int c = 0; c < Cols; c++)
			{
				if (_grid[row + Hidden, c] != 0)
				{
					mask |= 1 << c;
				}
			}
			data[row * 2] = (byte)(mask & 0xFF);
			data[row * 2 + 1] = (byte)(mask >> 8);
		}
		return data;
	}

	#endregion

	#region Drawing

	/// <summary>A block: the gemspark of the kind (the lone-block frame, so each cell has its border). In a sharp batch.</summary>
	public static void Block(SpriteBatch sb, int kind, Rectangle r, float alpha)
	{
		if (kind <= 0 || kind >= Tiles.Length)
		{
			return;
		}
		int tile = Tiles[kind];
		try
		{
			Main.instance.LoadTiles(tile);
			sb.Draw(TextureAssets.Tile[tile].Value, r, new Rectangle(162, 54, 16, 16), Color.White * alpha);
		}
		catch
		{
			sb.Draw(TextureAssets.MagicPixel.Value, r, new Rectangle(0, 0, 1, 1), Glow[kind] * alpha);
		}
	}

	/// <summary>A piece centered in a box, cells of <paramref name="cell"/> pixels.</summary>
	public static void MiniPiece(SpriteBatch sb, int kind, Rectangle area, int cell, float alpha)
	{
		if (kind < 1 || kind > 7)
		{
			return;
		}
		Point[] cells = Rotations[kind][0];
		int minX = cells.Min(c => c.X);
		int minY = cells.Min(c => c.Y);
		int w = (cells.Max(c => c.X) - minX + 1) * cell;
		int h = (cells.Max(c => c.Y) - minY + 1) * cell;
		int x0 = area.X + (area.Width - w) / 2;
		int y0 = area.Y + (area.Height - h) / 2;
		GameArt.Sharp(sb);
		foreach (Point c in cells)
		{
			Block(sb, kind, new Rectangle(x0 + (c.X - minX) * cell, y0 + (c.Y - minY) * cell, cell, cell), alpha);
		}
		GameArt.Smooth(sb);
	}

	private static void Backdrop(SpriteBatch sb, Rectangle well, int cell)
	{
		GameArt.Gradient(sb, well, new Color(18, 20, 44), new Color(30, 26, 60));
		for (int c = 1; c < Cols; c++)
		{
			MusicDrawing.Fill(sb, new Rectangle(well.X + c * cell, well.Y, 1, well.Height), Color.White * 0.04f);
		}
		for (int row = 1; row < Rows; row++)
		{
			MusicDrawing.Fill(sb, new Rectangle(well.X, well.Y + row * cell, well.Width, 1), Color.White * 0.04f);
		}
	}

	/// <summary>The other player's well from their last snapshot (gray when they're out).</summary>
	public static void DrawSnapshot(SpriteBatch sb, byte[] rows, Rectangle well, int cell, bool out_)
	{
		Backdrop(sb, well, cell);
		if (rows == null)
		{
			return;
		}
		GameArt.Sharp(sb);
		for (int row = 0; row < Rows; row++)
		{
			int mask = rows[row * 2] | (rows[row * 2 + 1] << 8);
			for (int c = 0; c < Cols; c++)
			{
				if ((mask & (1 << c)) != 0)
				{
					Block(sb, out_ ? 8 : 6, new Rectangle(well.X + c * cell, well.Y + row * cell, cell, cell), out_ ? 0.5f : 0.9f);
				}
			}
		}
		GameArt.Smooth(sb);
	}

	/// <summary>The well: blocks, ghost, the falling piece and the effects. Shakes a little after drops and garbage.</summary>
	public void DrawWell(SpriteBatch sb, Rectangle well, int cell)
	{
		double now = GameArt.Now;
		float shake = 1f - GameArt.Clamp01((now - _shakeAt) / 0.18);
		well = new Rectangle(well.X, well.Y + (int)(shake * 3f), well.Width, well.Height);
		Backdrop(sb, well, cell);
		Rectangle Cell(int col, int row) => new Rectangle(well.X + col * cell, well.Y + (row - Hidden) * cell, cell, cell);
		float trail = 1f - GameArt.Clamp01((now - _trailAt) / 0.22);
		if (trail > 0f && _trailCells != null)
		{
			foreach (Point p in _trailCells)
			{
				int top = Math.Max(Hidden, p.Y);
				int bottom = p.Y + _trailFrom;
				if (bottom >= top)
				{
					Rectangle t = Cell(p.X, top);
					GameArt.Gradient(sb, new Rectangle(t.X + 2, t.Y, t.Width - 4, (bottom - top + 1) * cell), Color.Transparent, Glow[_trailKind] * 0.35f * trail);
				}
			}
		}
		float stone = Over ? GameArt.Clamp01((now - OverAt) / 0.9) : 0f;
		GameArt.Sharp(sb);
		for (int row = Hidden; row < Rows + Hidden; row++)
		{
			bool clearing = _clearing.Contains(row);
			for (int col = 0; col < Cols; col++)
			{
				int kind = _grid[row, col];
				if (kind == 0)
				{
					continue;
				}
				Rectangle r = Cell(col, row);
				if (clearing)
				{
					float t = GameArt.Clamp01((now - _clearAt) / 0.32);
					int shrink = (int)(cell * 0.5f * GameArt.EaseOut(Math.Max(0f, t - 0.4f) / 0.6f));
					r.Inflate(-shrink, -shrink);
				}
				bool stoned = stone > 0f && (Rows + Hidden - 1 - row) < stone * Rows;
				Block(sb, kind, r, stoned ? 0.35f : 1f);
			}
		}
		if (!Over && _kind != 0)
		{
			int ghost = DropDistance();
			foreach (Point c in Rotations[_kind][_rot])
			{
				if (_y + c.Y + ghost >= Hidden && ghost > 0)
				{
					Block(sb, _kind, Cell(_x + c.X, _y + c.Y + ghost), 0.22f);
				}
			}
			foreach (Point c in Rotations[_kind][_rot])
			{
				if (_y + c.Y >= Hidden)
				{
					Block(sb, _kind, Cell(_x + c.X, _y + c.Y), 1f);
				}
			}
		}
		for (int i = _particles.Count - 1; i >= 0; i--)
		{
			Particle p = _particles[i];
			float age = (float)(now - p.Born);
			if (age > 0.9f)
			{
				_particles.RemoveAt(i);
				continue;
			}
			Vector2 at = p.At + p.Speed * age + new Vector2(0f, 14f * age * age);
			int tile = Tiles[Math.Max(1, Math.Min(8, p.Kind))];
			Main.instance.LoadTiles(tile);
			float size = cell * 0.45f * (1f - age / 0.9f);
			sb.Draw(TextureAssets.Tile[tile].Value, new Vector2(well.X + at.X * cell, well.Y + at.Y * cell), new Rectangle(162, 54, 16, 16), Color.White * (1f - age / 0.9f), p.Spin * age, new Vector2(8f, 8f), size / 16f, SpriteEffects.None, 0f);
		}
		GameArt.Smooth(sb);
		foreach (int row in _clearing)
		{
			float t = GameArt.Clamp01((now - _clearAt) / 0.32);
			MusicDrawing.Fill(sb, new Rectangle(well.X, well.Y + (row - Hidden) * cell, well.Width, cell), Color.White * (0.7f * (1f - t)));
		}
		// Garbage just rose: a red flash from the bottom.
		float rose = 1f - GameArt.Clamp01((now - _garbageAt) / 0.4);
		if (rose > 0f)
		{
			GameArt.Gradient(sb, new Rectangle(well.X, well.Bottom - well.Height / 3, well.Width, well.Height / 3), Color.Transparent, new Color(255, 70, 70) * 0.35f * rose);
		}
		for (int i = _popups.Count - 1; i >= 0; i--)
		{
			Popup p = _popups[i];
			float age = (float)(now - p.Born);
			if (age > 1.3f)
			{
				_popups.RemoveAt(i);
				continue;
			}
			float scale = p.Scale * GameArt.EaseBack(GameArt.Clamp01(age / 0.25f)) * 0.7f * (cell / 22f);
			float alpha = 1f - GameArt.Clamp01((age - 0.9f) / 0.4f);
			Vector2 size = FontAssets.DeathText.Value.MeasureString(p.Text) * scale;
			Vector2 at = new Vector2(well.Center.X - size.X / 2f, well.Y + well.Height * 0.38f - age * 30f + (i - (_popups.Count - 1)) * size.Y);
			Utils.DrawBorderStringBig(sb, p.Text, at, p.Color * alpha, scale);
		}
	}

	/// <summary>Versus: the garbage on its way, a red bar beside the well.</summary>
	public void DrawGarbageBar(SpriteBatch sb, Rectangle bar, int cell)
	{
		MusicDrawing.Fill(sb, bar, Color.Black * 0.4f);
		int h = Math.Min(bar.Height, PendingGarbage * cell);
		if (h > 0)
		{
			float pulse = 0.75f + 0.25f * GameArt.Pulse(0.6);
			MusicDrawing.Fill(sb, new Rectangle(bar.X, bar.Bottom - h, bar.Width, h), new Color(255, 80, 80) * pulse);
		}
	}

	#endregion
}
