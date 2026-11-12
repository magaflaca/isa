using System;
using System.Collections.Generic;
using IsaClient.Core;
using IsaClient.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace IsaClient.Games;

/// <summary>
/// .snakeart: Snake played with real blocks in the world, so everyone here sees it. A frame of diamond gemspark
/// goes up around the cursor (what's inside is cleared first), the snake is emerald gemspark and the fruit ruby
/// gemspark. Blocks go out at World Edit's "Block &amp; Wall Speed" (<see cref="PacketPacing"/>), the
/// way its paste does on TShock (a fake position and the block in hand), so the snake moves only as fast as its blocks
/// can be sent. The walls are solid or wrap as in the Snake window's setting. Arrow keys turn (your character keeps
/// WASD); after a game over an arrow plays again. Scores count on the Snake board.
/// </summary>
public static class SnakeArt
{
	private const ushort FrameTile = TileID.DiamondGemspark;

	private const short FrameItem = ItemID.DiamondGemsparkBlock;

	private const ushort BodyTile = TileID.EmeraldGemspark;

	private const short BodyItem = ItemID.EmeraldGemsparkBlock;

	private const ushort FruitTile = TileID.RubyGemspark;

	private const short FruitItem = ItemID.RubyGemsparkBlock;

	private enum State
	{
		Off,
		Building,
		Playing,
		Over
	}

	private static State _state;

	/// <summary>The playing field (inside the frame), in tiles.</summary>
	private static Rectangle _field;

	private static readonly List<Point> _body = new List<Point>();

	private static Point _dir;

	private static readonly List<Point> _turns = new List<Point>();

	private static Point _fruit = new Point(-1, -1);

	private static int _grow;

	private static int _score;

	private static double _nextStep;

	private static double _overAt;

	private static readonly Random _rng = new Random();

	private static readonly WorldBlocks _ops = new WorldBlocks();

	private static KeyboardState _prevKeys;

	public static bool Active => _state != State.Off;

	private static double Now => IsaClient.Games.GameArt.Now;

	/// <summary>How long a step waits at least (the speed of the Snake window's setting).</summary>
	private static double BaseStep => (Snake.Speed == 0) ? 0.3 : ((Snake.Speed == 2) ? 0.14 : 0.2);

	/// <summary>.snakeart [width height | stop | clear]</summary>
	internal static void Command(CommandContext command)
	{
		string[] args = command.Rest.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (Main.gameMenu)
		{
			return;
		}
		if (args.Length > 0 && (args[0] == "stop" || args[0] == "off"))
		{
			Stop(false);
			return;
		}
		if (args.Length > 0 && (args[0] == "clear" || args[0] == "remove"))
		{
			Stop(true);
			return;
		}
		if (Active && args.Length == 0)
		{
			Stop(false);
			return;
		}
		int width = 30;
		int height = 16;
		if (args.Length >= 2 && int.TryParse(args[0], out int w) && int.TryParse(args[1], out int h))
		{
			width = Math.Max(10, Math.Min(80, w));
			height = Math.Max(6, Math.Min(50, h));
		}
		if (Active)
		{
			Stop(true);
		}
		Point center = Main.MouseWorld.ToTileCoordinates();
		_field = new Rectangle(center.X - width / 2, center.Y - height / 2, width, height);
		if (!WorldGen.InWorld(_field.X - 1, _field.Y - 1, 10) || !WorldGen.InWorld(_field.Right, _field.Bottom, 10))
		{
			ClientChat.Error("Too close to the edge of the world.");
			return;
		}
		Build();
		ClientChat.Info(IsaClient.Languages.L10n.F("Snake art: building a {0}x{1} field at the cursor ({2} blocks). Arrow keys play; .snakeart stops, .snakeart clear removes it.", width, height, _ops.Count));
	}

	/// <summary>Clears the inside, then the frame.</summary>
	private static void Build()
	{
		_ops.Clear();
		_body.Clear();
		_fruit = new Point(-1, -1);
		for (int y = _field.Y; y < _field.Bottom; y++)
		{
			for (int x = _field.X; x < _field.Right; x++)
			{
				if (Main.tile[x, y] != null && Main.tile[x, y].active())
				{
					_ops.Remove(x, y);
				}
			}
		}
		for (int x = _field.X - 1; x <= _field.Right; x++)
		{
			Frame(x, _field.Y - 1);
			Frame(x, _field.Bottom);
		}
		for (int y = _field.Y; y < _field.Bottom; y++)
		{
			Frame(_field.X - 1, y);
			Frame(_field.Right, y);
		}
		_state = State.Building;
	}

	private static void Frame(int x, int y)
	{
		Tile tile = Main.tile[x, y];
		if (tile != null && tile.active() && tile.type == FrameTile)
		{
			return;
		}
		if (tile != null && tile.active())
		{
			_ops.Remove(x, y);
		}
		_ops.Place(x, y, FrameTile, FrameItem);
	}

	/// <summary>Ends the game; <paramref name="remove"/> also takes every block it put down (frame, snake and fruit).</summary>
	private static void Stop(bool remove)
	{
		if (_state == State.Playing)
		{
			GameOver();
		}
		_ops.Clear();
		if (remove && _field.Width > 0)
		{
			for (int y = _field.Y - 1; y <= _field.Bottom; y++)
			{
				for (int x = _field.X - 1; x <= _field.Right; x++)
				{
					Tile tile = Main.tile[x, y];
					if (tile != null && tile.active() && (tile.type == FrameTile || tile.type == BodyTile || tile.type == FruitTile))
					{
						_ops.Remove(x, y);
					}
				}
			}
			_field = Rectangle.Empty;
			ClientChat.Info(IsaClient.Languages.L10n.F("Snake art: removing it ({0} blocks).", _ops.Count));
		}
		else
		{
			ClientChat.Info("Snake art: stopped. The blocks stay (.snakeart clear removes them).");
		}
		_state = State.Off;
		_body.Clear();
	}

	/// <summary>A snake of four on the left, heading right, and a fruit. What's left of the last game goes first.</summary>
	private static void NewGame()
	{
		foreach (Point p in _body)
		{
			_ops.Remove(p.X, p.Y);
		}
		if (_fruit.X >= 0)
		{
			_ops.Remove(_fruit.X, _fruit.Y);
		}
		_body.Clear();
		_turns.Clear();
		_dir = new Point(1, 0);
		int row = _field.Y + _field.Height / 2;
		for (int i = 0; i < 4; i++)
		{
			Point p = new Point(_field.X + 5 - i, row);
			_body.Add(p);
			_ops.Place(p.X, p.Y, BodyTile, BodyItem);
		}
		_grow = 0;
		_score = 0;
		_fruit = new Point(-1, -1);
		PlaceFruit();
		_state = State.Playing;
		_nextStep = Now + 0.6;
	}

	private static void PlaceFruit()
	{
		List<Point> free = new List<Point>();
		for (int y = _field.Y; y < _field.Bottom; y++)
		{
			for (int x = _field.X; x < _field.Right; x++)
			{
				Point p = new Point(x, y);
				if (!_body.Contains(p))
				{
					free.Add(p);
				}
			}
		}
		if (free.Count == 0)
		{
			_fruit = new Point(-1, -1);
			return;
		}
		_fruit = free[_rng.Next(free.Count)];
		_ops.Place(_fruit.X, _fruit.Y, FruitTile, FruitItem);
	}

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
		if (!_field.Contains(head))
		{
			if (Snake.Walls == 1)
			{
				GameOver();
				return;
			}
			// Wrap (the Snake setting): out the other side.
			head = new Point(_field.X + ((head.X - _field.X) % _field.Width + _field.Width) % _field.Width, _field.Y + ((head.Y - _field.Y) % _field.Height + _field.Height) % _field.Height);
		}
		bool tailMoves = _grow == 0 && head != _fruit;
		bool bites = _body.IndexOf(head) >= 0 && !(tailMoves && head == _body[_body.Count - 1]);
		if (bites)
		{
			GameOver();
			return;
		}
		if (head == _fruit)
		{
			// The fruit block goes, the head takes its place.
			_ops.Remove(head.X, head.Y);
			int points = 10 + 5 * Snake.Speed + ((Snake.Walls == 1) ? 5 : 0);
			_score += points;
			SoundEngine.PlaySound(SoundID.Item2);
			CombatText.NewText(new Rectangle(head.X * 16, head.Y * 16, 16, 16), new Color(255, 120, 140), "+" + points);
		}
		if (_grow > 0 && head != _fruit)
		{
			_grow--;
		}
		else if (head != _fruit)
		{
			Point tail = _body[_body.Count - 1];
			_body.RemoveAt(_body.Count - 1);
			_ops.Remove(tail.X, tail.Y);
		}
		_body.Insert(0, head);
		_ops.Place(head.X, head.Y, BodyTile, BodyItem);
		if (head == _fruit)
		{
			PlaceFruit();
			if (_fruit.X < 0)
			{
				GameOver();
			}
		}
	}

	private static void GameOver()
	{
		_state = State.Over;
		_overAt = Now;
		bool best = Scores.Submit(Scores.Snake, _score);
		if (best)
		{
			SoundEngine.PlaySound(SoundID.AchievementComplete);
		}
		else
		{
			SoundEngine.PlaySound(SoundID.PlayerKilled);
		}
		ClientChat.Info(IsaClient.Languages.L10n.F("Snake art: game over, {0} points, length {1}. An arrow key plays again.", _score, _body.Count));
	}

	private static Point? KeyTurn(KeyboardState keys)
	{
		bool Pressed(Keys k) => keys.IsKeyDown(k) && !_prevKeys.IsKeyDown(k);
		if (Pressed(Keys.Up))
		{
			return new Point(0, -1);
		}
		if (Pressed(Keys.Down))
		{
			return new Point(0, 1);
		}
		if (Pressed(Keys.Left))
		{
			return new Point(-1, 0);
		}
		if (Pressed(Keys.Right))
		{
			return new Point(1, 0);
		}
		return null;
	}

	/// <summary>Every tick (game loop): sends the next blocks, reads the arrows, moves the snake.</summary>
	public static void Update()
	{
		if (Main.gameMenu)
		{
			_state = State.Off;
			_ops.Clear();
			_body.Clear();
			_field = Rectangle.Empty;
			return;
		}
		_ops.Send();
		if (_state == State.Off)
		{
			return;
		}
		KeyboardState keys = Main.keyState;
		bool canInput = Main.hasFocus && !Main.drawingPlayerChat && !Main.editSign && !Main.editChest;
		Point? turn = canInput ? KeyTurn(keys) : null;
		_prevKeys = keys;
		if (_state == State.Building)
		{
			if (_ops.Count == 0)
			{
				ClientChat.Info("Snake art: ready! Arrow keys to play.");
				_state = State.Over;
				_overAt = Now - 10.0;
			}
			return;
		}
		if (_state == State.Over)
		{
			if (turn.HasValue && Now - _overAt > 1.0)
			{
				NewGame();
			}
			return;
		}
		if (turn.HasValue && _turns.Count < 3)
		{
			Point last = (_turns.Count > 0) ? _turns[_turns.Count - 1] : _dir;
			if (turn.Value != last && (turn.Value.X != -last.X || turn.Value.Y != -last.Y))
			{
				_turns.Add(turn.Value);
			}
		}
		// A step only when the last one's blocks are out, so the world keeps up with the game.
		if (_ops.Frozen)
		{
			// TShock froze you: the snake waits too.
			_nextStep = Now + BaseStep;
		}
		else if (Now >= _nextStep && _ops.Count == 0)
		{
			_nextStep = Now + BaseStep;
			Step();
		}
	}
}
