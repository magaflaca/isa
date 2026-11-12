using System;
using System.Collections.Generic;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace IsaClient.Games;

/// <summary>Tic-tac-toe: a move is one byte, the cell 0-8 (left to right, top to bottom). X is whoever moves first.</summary>
internal sealed class TicTacToe : IGameBoard, IGameHud
{
	public static readonly PipeGame Game = new PipeGame
	{
		Id = 1,
		Name = "Tic Tac Toe",
		Description = "Three in a row wins. Whoever starts plays X; you take turns starting.",
		Icon = 859,
		Sprite = GameArt.MarkX,
		Create = () => new TicTacToe(),
		ScoreBoard = Scores.TicTacToe,
		BoardSizes = new[] { 186, 240, 300 },
		BotDelay = 0.55,
		First = new GameOption
		{
			Name = "Who starts",
			Hint = "Who plays X in the first round (then you take turns).",
			Labels = new[] { "Me", "The other", "Random" },
			Values = new[] { 0, 1, 2 },
			Get = () => StartChoice,
			Set = v => StartChoice = v
		},
		TurnTime = new GameOption
		{
			Name = "Time per turn",
			Hint = "In the games you invite to. Who runs out loses the round.",
			Labels = new[] { "No limit", "10s", "15s", "30s", "60s" },
			Values = new[] { 0, 10, 15, 30, 60 },
			Get = () => GameLobby.TurnSeconds,
			Set = v => GameLobby.TurnSeconds = v
		},
		BotLevel = new GameOption
		{
			Name = "Computer",
			Hint = "Easy makes mistakes, Normal wins and blocks, Perfect never loses.",
			Labels = new[] { "Easy", "Normal", "Perfect" },
			Values = new[] { 0, 1, 2 },
			Get = () => BotSkill,
			Set = v => BotSkill = v
		}
	};

	private static readonly int[][] Lines =
	{
		new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8 },
		new[] { 0, 3, 6 }, new[] { 1, 4, 7 }, new[] { 2, 5, 8 },
		new[] { 0, 4, 8 }, new[] { 2, 4, 6 }
	};

	/// <summary>Item drawn for X (0 = the pixel X). Only on your screen.</summary>
	[IsaClient.Core.Setting("TicTacToeX")]
	public static int XItem;

	/// <summary>Item drawn for O (0 = the pixel O).</summary>
	[IsaClient.Core.Setting("TicTacToeO")]
	public static int OItem;

	/// <summary>0 you start, 1 the other player, 2 random.</summary>
	[IsaClient.Core.Setting("TicTacToeFirst", Min = 0, Max = 2)]
	public static int StartChoice = 2;

	[IsaClient.Core.Setting("TicTacToeBot", Min = 0, Max = 2)]
	public static int BotSkill = 1;

	private static readonly Color XColor = new Color(255, 120, 120);

	private static readonly Color OColor = new Color(120, 190, 255);

	/// <summary>0 empty, otherwise seat + 1.</summary>
	private readonly int[] _cells = new int[9];

	/// <summary>When each mark was placed (for its pop-in).</summary>
	private readonly double[] _placed = new double[9];

	private int _firstSeat;

	private int[] _winLine;

	private double _wonAt;

	private int _lastCell = -1;

	private double _resetAt;

	public int Turn { get; private set; }

	public int Result { get; private set; } = -1;

	public void Reset(int firstSeat)
	{
		Array.Clear(_cells, 0, 9);
		_firstSeat = firstSeat;
		Turn = firstSeat;
		Result = -1;
		_winLine = null;
		_lastCell = -1;
		_resetAt = GameArt.Now;
	}

	public bool Apply(int seat, byte[] move)
	{
		if (Result != -1 || seat != Turn || move == null || move.Length != 1 || move[0] > 8 || _cells[move[0]] != 0)
		{
			return false;
		}
		_cells[move[0]] = seat + 1;
		_placed[move[0]] = GameArt.Now;
		_lastCell = move[0];
		int[] line = WinningLine(_cells, seat + 1);
		if (line != null)
		{
			Result = seat;
			_winLine = line;
			_wonAt = GameArt.Now;
			return true;
		}
		if (Array.IndexOf(_cells, 0) < 0)
		{
			Result = 2;
			_wonAt = GameArt.Now;
			return true;
		}
		Turn = 1 - seat;
		return true;
	}

	private static int[] WinningLine(int[] cells, int who)
	{
		foreach (int[] line in Lines)
		{
			if (cells[line[0]] == who && cells[line[1]] == who && cells[line[2]] == who)
			{
				return line;
			}
		}
		return null;
	}

	/// <summary>"X" or "O" for a seat.</summary>
	public string MarkOf(int seat)
	{
		return (seat == _firstSeat) ? "X" : "O";
	}

	#region The computer

	public Func<byte[]> Think(int level)
	{
		int[] cells = (int[])_cells.Clone();
		int me = Turn + 1;
		return () => new[] { (byte)BotMove(cells, me, level) };
	}

	private static int BotMove(int[] cells, int me, int level)
	{
		Random random = new Random();
		List<int> free = new List<int>();
		for (int i = 0; i < 9; i++)
		{
			if (cells[i] == 0)
			{
				free.Add(i);
			}
		}
		int them = 3 - me;
		if (level == 0)
		{
			// Easy: sees its own win half the time, otherwise anywhere.
			int win = Finishing(cells, me);
			if (win >= 0 && random.NextDouble() < 0.5)
			{
				return win;
			}
			return free[random.Next(free.Count)];
		}
		if (level == 1)
		{
			int win = Finishing(cells, me);
			if (win >= 0)
			{
				return win;
			}
			int block = Finishing(cells, them);
			if (block >= 0 && random.NextDouble() < 0.9)
			{
				return block;
			}
			if (cells[4] == 0 && random.NextDouble() < 0.6)
			{
				return 4;
			}
			return free[random.Next(free.Count)];
		}
		// Perfect: the best outcome, quickest win, among equals at random.
		int best = -100;
		List<int> choices = new List<int>();
		foreach (int cell in free)
		{
			cells[cell] = me;
			int score = -Negamax(cells, them, 1);
			cells[cell] = 0;
			if (score > best)
			{
				best = score;
				choices.Clear();
			}
			if (score == best)
			{
				choices.Add(cell);
			}
		}
		return choices[random.Next(choices.Count)];
	}

	/// <summary>A cell that completes a line for <paramref name="who"/>, or -1.</summary>
	private static int Finishing(int[] cells, int who)
	{
		for (int i = 0; i < 9; i++)
		{
			if (cells[i] == 0)
			{
				cells[i] = who;
				bool wins = WinningLine(cells, who) != null;
				cells[i] = 0;
				if (wins)
				{
					return i;
				}
			}
		}
		return -1;
	}

	private static int Negamax(int[] cells, int who, int depth)
	{
		if (WinningLine(cells, 3 - who) != null)
		{
			return -(10 - depth);
		}
		int best = -100;
		bool any = false;
		for (int i = 0; i < 9; i++)
		{
			if (cells[i] == 0)
			{
				any = true;
				cells[i] = who;
				best = Math.Max(best, -Negamax(cells, 3 - who, depth + 1));
				cells[i] = 0;
			}
		}
		return any ? best : 0;
	}

	#endregion

	#region Drawing

	public byte[] Draw(SpriteBatch sb, Rectangle area, int mySeat, bool canMove)
	{
		int size = Math.Min(area.Width, area.Height);
		Rectangle board = new Rectangle(area.X + (area.Width - size) / 2, area.Y + (area.Height - size) / 2, size, size);
		const int pad = 10;
		const int gap = 6;
		int cell = (size - pad * 2 - gap * 2) / 3;
		int x0 = board.X + (size - (cell * 3 + gap * 2)) / 2;
		int y0 = board.Y + (size - (cell * 3 + gap * 2)) / 2;
		double now = GameArt.Now;
		// The board: a dark wooden tray with lighter cells.
		MusicDrawing.Box(sb, board, new Color(28, 22, 30) * 0.96f);
		byte[] move = null;
		float appear = GameArt.EaseOut(GameArt.Clamp01((now - _resetAt) / 0.35));
		Rectangle[] rects = new Rectangle[9];
		for (int i = 0; i < 9; i++)
		{
			rects[i] = new Rectangle(x0 + i % 3 * (cell + gap), y0 + i / 3 * (cell + gap), cell, cell);
		}
		GameArt.Sharp(sb);
		for (int i = 0; i < 9; i++)
		{
			Rectangle r = rects[i];
			float delay = GameArt.Clamp01(appear * 1.6f - (i % 3 + i / 3) * 0.15f);
			Color tint = Color.Lerp(new Color(120, 100, 90), Color.White, delay);
			GameArt.Tile(sb, ((i % 2) == 0) ? GameArt.LightTile : GameArt.DarkTile, r, tint, (i % 3 == 1) ? SpriteEffects.FlipHorizontally : SpriteEffects.None);
		}
		GameArt.Smooth(sb);
		for (int i = 0; i < 9; i++)
		{
			Rectangle r = rects[i];
			bool free = _cells[i] == 0;
			bool hover = free && canMove && Result == -1 && MusicPlayerWindow.MouseIn(r);
			bool winning = _winLine != null && Array.IndexOf(_winLine, i) >= 0;
			// Bevel: light top-left, dark bottom-right, so the tiles look set in the tray.
			MusicDrawing.Fill(sb, new Rectangle(r.X, r.Y, r.Width, 2), Color.White * 0.18f);
			MusicDrawing.Fill(sb, new Rectangle(r.X, r.Bottom - 3, r.Width, 3), Color.Black * 0.28f);
			if (winning)
			{
				float glow = 0.35f + 0.25f * GameArt.Pulse(1.1);
				MusicDrawing.Fill(sb, r, new Color(255, 220, 90) * glow);
			}
			else if (hover)
			{
				MusicDrawing.Fill(sb, r, Color.White * (0.12f + 0.08f * GameArt.Pulse(0.9)));
			}
			if (i == _lastCell && !winning && Result == -1)
			{
				GameArt.Frame(sb, new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4), MusicPlayerWindow.AccentColor * 0.55f, 2);
			}
		}
		for (int i = 0; i < 9; i++)
		{
			Rectangle r = rects[i];
			int owner = _cells[i] - 1;
			bool winning = _winLine != null && Array.IndexOf(_winLine, i) >= 0;
			if (owner >= 0)
			{
				float t = GameArt.Clamp01((now - _placed[i]) / 0.32);
				float scale = GameArt.EaseBack(t);
				if (winning)
				{
					float bounce = GameArt.Clamp01((now - _wonAt - Array.IndexOf(_winLine, i) * 0.08) / 0.4);
					scale *= 1f + 0.18f * (float)Math.Sin(bounce * Math.PI);
				}
				else if (Result >= 0 && Result != 2)
				{
					scale *= 1f - 0.1f * GameArt.Clamp01((now - _wonAt) / 0.4);
				}
				float alpha = (Result >= 0 && Result != 2 && !winning) ? 0.55f : 1f;
				DrawMark(sb, r, MarkOf(owner), alpha, scale, (MarkOf(owner) == "X") ? (1f - t) * -0.6f : 0f);
			}
			else if (canMove && Result == -1 && MusicPlayerWindow.MouseIn(r))
			{
				DrawMark(sb, r, MarkOf(mySeat), 0.3f + 0.1f * GameArt.Pulse(0.9), 0.92f, 0f);
				MusicDrawing.Hover("game:cell:" + i);
				if (MusicPlayerWindow.Clicked)
				{
					MusicPlayerWindow.Clicked = false;
					move = new[] { (byte)i };
				}
			}
		}
		// Three in a row: a glowing stroke drawn across them.
		if (_winLine != null)
		{
			float t = GameArt.EaseOut(GameArt.Clamp01((now - _wonAt - 0.15) / 0.4));
			Vector2 a = rects[_winLine[0]].Center.ToVector2();
			Vector2 b = rects[_winLine[2]].Center.ToVector2();
			Vector2 dir = Vector2.Normalize(b - a);
			a -= dir * cell * 0.38f;
			b += dir * cell * 0.38f;
			Vector2 end = Vector2.Lerp(a, b, t);
			Color color = (MarkOf(Result) == "X") ? XColor : OColor;
			GameArt.Line(sb, a, end, cell * 0.16f, Color.Black * 0.35f);
			GameArt.Line(sb, a, end, cell * 0.11f, color);
			GameArt.Line(sb, a, end, cell * 0.04f, Color.White * 0.8f);
		}
		return move;
	}

	/// <summary>An X or an O: the chosen item, or the pixel mark.</summary>
	public static void DrawMark(SpriteBatch sb, Rectangle r, string mark, float alpha, float scale = 1f, float rotation = 0f)
	{
		if (scale <= 0.01f)
		{
			return;
		}
		int item = (mark == "X") ? XItem : OItem;
		Vector2 center = new Vector2(r.Center.X, r.Center.Y);
		if (item > 0 && item < Terraria.ID.ItemID.Count)
		{
			Main.instance.LoadItem(item);
			Texture2D texture = TextureAssets.Item[item].Value;
			Rectangle frame = (Main.itemAnimations[item] != null) ? Main.itemAnimations[item].GetFrame(texture) : texture.Bounds;
			float fit = Math.Min(Math.Min(r.Width * 0.66f / frame.Width, r.Height * 0.66f / frame.Height), 4f) * scale;
			sb.Draw(texture, center, frame, Color.White * alpha, rotation, new Vector2(frame.Width, frame.Height) / 2f, fit, SpriteEffects.None, 0f);
			return;
		}
		if (GameArt.Atlas == null)
		{
			Color color = (mark == "X") ? XColor : OColor;
			float textScale = r.Height / 60f * scale;
			Vector2 size = FontAssets.DeathText.Value.MeasureString(mark) * textScale;
			Utils.DrawBorderStringBig(sb, mark, center - size / 2f + new Vector2(0f, 4f * textScale), color * alpha, textScale);
			return;
		}
		GameArt.Sharp(sb);
		float px = Math.Min(r.Width, r.Height) * 0.8f * scale;
		Rectangle sprite = (mark == "X") ? GameArt.MarkX : GameArt.MarkO;
		GameArt.Sprite(sb, sprite, center + new Vector2(2f, 3f), px, Color.Black * 0.3f * alpha, rotation);
		GameArt.Sprite(sb, sprite, center, px, Color.White * alpha, rotation);
		GameArt.Smooth(sb);
	}

	#endregion

	#region Hud

	public string SeatName(int seat)
	{
		return MarkOf(seat);
	}

	public void DrawSeat(SpriteBatch sb, Rectangle area, int seat)
	{
		DrawMark(sb, area, MarkOf(seat), 1f, 1.15f);
	}

	public void DrawSeatExtras(SpriteBatch sb, Rectangle area, int seat)
	{
	}

	public int SideWidth => 0;

	public void DrawSide(SpriteBatch sb, Rectangle area)
	{
	}

	public string Note => null;

	#endregion
}
