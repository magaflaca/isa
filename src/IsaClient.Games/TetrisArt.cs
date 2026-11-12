using System;
using System.Collections.Generic;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace IsaClient.Games;

/// <summary>
/// .tetrisart: Tetris played with real blocks in the world, so everyone here sees it. A closed box of gray brick goes up
/// around the cursor (what's inside is cleared first) and the game is <see cref="TetrisField"/>, the same as the
/// window's: each tick what the well shows is compared with the blocks already put and only the difference goes out
/// (<see cref="WorldBlocks"/>, at World Edit's Block &amp; Wall Speed), so the world catches up as fast as blocks can be
/// sent. Keys that don't move your character: arrows move, rotate (up) and drop softly (down); End, Right Ctrl or
/// numpad 0 drop at once; Right Shift or Page Down hold. Scores count on the Tetris board.
/// </summary>
public static class TetrisArt
{
	private const int Cols = TetrisField.Cols;

	private const int Rows = TetrisField.Rows;

	private const ushort WallTile = TileID.GrayBrick;

	private const short WallItem = ItemID.GrayBrick;

	/// <summary>The item that places each kind's block (1..7 the gemsparks, 8 garbage).</summary>
	private static readonly short[] Items =
	{
		0, ItemID.DiamondGemsparkBlock, ItemID.TopazGemsparkBlock, ItemID.AmethystGemsparkBlock, ItemID.EmeraldGemsparkBlock,
		ItemID.RubyGemsparkBlock, ItemID.SapphireGemsparkBlock, ItemID.AmberGemsparkBlock, ItemID.GrayBrick
	};

	private enum State
	{
		Off,
		Building,
		Playing,
		Over
	}

	private static State _state;

	/// <summary>The inside of the well, in tiles.</summary>
	private static Rectangle _well;

	private static TetrisField _field;

	/// <summary>The kind of block put in each cell of the well (what the world shows, once sent).</summary>
	private static readonly int[,] _placed = new int[Rows, Cols];

	private static readonly WorldBlocks _blocks = new WorldBlocks();

	private static int _lines;

	private static double _overAt;

	private static KeyboardState _prevKeys;

	public static bool Active => _state != State.Off;

	private static double Now => GameArt.Now;

	/// <summary>.tetrisart [stop | clear]</summary>
	internal static void Command(CommandContext command)
	{
		string arg = command.Rest.Trim().ToLowerInvariant();
		if (Main.gameMenu)
		{
			return;
		}
		if (arg == "stop" || arg == "off" || (arg.Length == 0 && Active))
		{
			Stop(false);
			return;
		}
		if (arg == "clear" || arg == "remove")
		{
			Stop(true);
			return;
		}
		Point center = Main.MouseWorld.ToTileCoordinates();
		_well = new Rectangle(center.X - Cols / 2, center.Y - Rows / 2, Cols, Rows);
		if (!WorldGen.InWorld(_well.X - 1, _well.Y - 1, 10) || !WorldGen.InWorld(_well.Right, _well.Bottom, 10))
		{
			ClientChat.Error("Too close to the edge of the world.");
			return;
		}
		Build();
		ClientChat.Info(IsaClient.Languages.L10n.F("Tetris art: building the well at the cursor ({0} blocks). Arrows move, rotate and drop; End drops at once, Right Shift holds. .tetrisart stops, .tetrisart clear removes it.", _blocks.Count));
	}

	/// <summary>Clears the inside, then the walls, the floor and the roof.</summary>
	private static void Build()
	{
		_blocks.Clear();
		Array.Clear(_placed, 0, _placed.Length);
		_field = null;
		for (int y = _well.Y; y < _well.Bottom; y++)
		{
			for (int x = _well.X; x < _well.Right; x++)
			{
				if (Main.tile[x, y] != null && Main.tile[x, y].active())
				{
					_blocks.Remove(x, y);
				}
			}
		}
		for (int y = _well.Y - 1; y <= _well.Bottom; y++)
		{
			Wall(_well.X - 1, y);
			Wall(_well.Right, y);
		}
		for (int x = _well.X; x < _well.Right; x++)
		{
			Wall(x, _well.Y - 1);
			Wall(x, _well.Bottom);
		}
		_state = State.Building;
	}

	private static void Wall(int x, int y)
	{
		Tile tile = Main.tile[x, y];
		if (tile != null && tile.active() && tile.type == WallTile)
		{
			return;
		}
		if (tile != null && tile.active())
		{
			_blocks.Remove(x, y);
		}
		_blocks.Place(x, y, WallTile, WallItem);
	}

	/// <summary>Ends the game; <paramref name="remove"/> also takes the well and its blocks out.</summary>
	private static void Stop(bool remove)
	{
		if (_state == State.Playing)
		{
			GameOver();
		}
		_blocks.Clear();
		if (remove && _well.Width > 0)
		{
			for (int y = _well.Y - 1; y <= _well.Bottom; y++)
			{
				for (int x = _well.X - 1; x <= _well.Right; x++)
				{
					Tile tile = Main.tile[x, y];
					bool inside = _well.Contains(x, y);
					if (tile != null && tile.active() && (tile.type == WallTile || (inside && IsPiece(tile.type))))
					{
						_blocks.Remove(x, y);
					}
				}
			}
			_well = Rectangle.Empty;
			ClientChat.Info(IsaClient.Languages.L10n.F("Tetris art: removing it ({0} blocks).", _blocks.Count));
		}
		else
		{
			ClientChat.Info("Tetris art: stopped. The blocks stay (.tetrisart clear removes them).");
		}
		_state = State.Off;
		_field = null;
	}

	private static bool IsPiece(ushort type)
	{
		for (int kind = 1; kind < Items.Length; kind++)
		{
			if (TetrisField.TileOf(kind) == type)
			{
				return true;
			}
		}
		return false;
	}

	private static void NewGame()
	{
		_field = new TetrisField(Tetris.Speed);
		_field.Start();
		_lines = 0;
		_state = State.Playing;
		SoundEngine.PlaySound(SoundID.MenuOpen);
	}

	private static void GameOver()
	{
		_state = State.Over;
		_overAt = Now;
		int score = _field?.Score ?? 0;
		bool best = Scores.Submit(Scores.Tetris, score);
		if (best)
		{
			SoundEngine.PlaySound(SoundID.AchievementComplete);
		}
		else
		{
			SoundEngine.PlaySound(SoundID.PlayerKilled);
		}
		ClientChat.Info(IsaClient.Languages.L10n.F("Tetris art: game over, {0} points, {1} lines. An arrow key plays again.", score.ToString("N0"), _field?.Lines ?? 0));
	}

	/// <summary>The game's keys from ones that don't move your character (see the class).</summary>
	private static KeyboardState GameKeys(KeyboardState keys)
	{
		List<Keys> down = new List<Keys>();
		foreach (Keys k in new[] { Keys.Left, Keys.Right, Keys.Up, Keys.Down })
		{
			if (keys.IsKeyDown(k))
			{
				down.Add(k);
			}
		}
		if (keys.IsKeyDown(Keys.End) || keys.IsKeyDown(Keys.RightControl) || keys.IsKeyDown(Keys.NumPad0))
		{
			down.Add(Keys.Space);
		}
		if (keys.IsKeyDown(Keys.RightShift) || keys.IsKeyDown(Keys.PageDown))
		{
			down.Add(Keys.C);
		}
		return new KeyboardState(down.ToArray());
	}

	/// <summary>Every tick (game loop): the game, then the blocks that changed.</summary>
	public static void Update()
	{
		if (Main.gameMenu)
		{
			_state = State.Off;
			_blocks.Clear();
			_field = null;
			_well = Rectangle.Empty;
			return;
		}
		_blocks.Send();
		if (_state == State.Off)
		{
			return;
		}
		bool canInput = Main.hasFocus && !Main.drawingPlayerChat && !Main.editSign && !Main.editChest;
		KeyboardState keys = canInput ? GameKeys(Main.keyState) : new KeyboardState();
		bool arrow = keys.GetPressedKeys().Length > 0 && _prevKeys.GetPressedKeys().Length == 0;
		if (_state == State.Building)
		{
			if (_blocks.Count == 0)
			{
				ClientChat.Info("Tetris art: ready! An arrow key starts.");
				_state = State.Over;
				_overAt = Now - 10.0;
			}
		}
		else if (_state == State.Over)
		{
			if (arrow && Now - _overAt > 1.0)
			{
				NewGame();
			}
		}
		else if (!_blocks.Frozen)
		{
			// While TShock has you frozen the game waits.
			_field.Step(keys, _prevKeys, canInput);
			if (_field.Lines > _lines)
			{
				CombatText.NewText(new Rectangle(_well.X * 16, _well.Y * 16, _well.Width * 16, 16), new Color(140, 220, 255), "+" + (_field.Lines - _lines) + " " + IsaClient.Languages.L10n.T("lines"));
				_lines = _field.Lines;
			}
			if (_field.Over)
			{
				GameOver();
			}
		}
		_prevKeys = keys;
		Sync();
	}

	/// <summary>When the last blocks are out: the cells that differ from the well go out next.</summary>
	private static void Sync()
	{
		if (_field == null || _blocks.Count > 0)
		{
			return;
		}
		int[,] kinds = _field.VisibleKinds();
		for (int row = Rows - 1; row >= 0; row--)
		{
			for (int col = 0; col < Cols; col++)
			{
				int want = kinds[row, col];
				if (want == _placed[row, col])
				{
					continue;
				}
				int x = _well.X + col;
				int y = _well.Y + row;
				if (_placed[row, col] != 0)
				{
					_blocks.Remove(x, y);
				}
				if (want != 0)
				{
					_blocks.Place(x, y, TetrisField.TileOf(want), Items[want]);
				}
				_placed[row, col] = want;
			}
		}
	}
}
