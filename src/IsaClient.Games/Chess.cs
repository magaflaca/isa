using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using P = IsaClient.Games.ChessPosition;

namespace IsaClient.Games;

/// <summary>
/// Chess with Terraria-styled pixel pieces (Hallowed white, Shadow black). A move is [from, to, promotion] (squares 0-63
/// from a1, promotion 0 or a piece kind); both players run the same rules (<see cref="ChessPosition"/>) so they agree on
/// mates and draws. Moves by click or by dragging; pieces slide, captures burst, the king in check glows, a mated king
/// falls. Whoever moves first plays white.
/// </summary>
internal sealed class Chess : IGameBoard, IGameHud
{
	public static readonly PipeGame Game = new PipeGame
	{
		Id = 2,
		Name = "Chess",
		Description = "The classic, with Terraria pieces: castling, en passant, promotion and every draw rule. Checkmate wins.",
		Sprite = GameArt.Piece(P.Knight, true),
		Create = () => new Chess(),
		ScoreBoard = Scores.Chess,
		BoardSizes = new[] { 280, 344, 408 },
		BotDelay = 0.45,
		First = new GameOption
		{
			Name = "Your color",
			Hint = "White moves first; in a rematch you swap colors.",
			Labels = new[] { "chess::White", "chess::Black", "Random" },
			Values = new[] { 0, 1, 2 },
			Get = () => Side,
			Set = v => Side = v
		},
		TurnTime = new GameOption
		{
			Name = "Time per move",
			Hint = "In the games you invite to. Who runs out loses the game.",
			Labels = new[] { "No limit", "30s", "60s", "2 min", "4 min" },
			Values = new[] { 0, 30, 60, 120, 240 },
			Get = () => TurnSeconds,
			Set = v => TurnSeconds = v
		},
		BotLevel = new GameOption
		{
			Name = "Computer",
			Hint = "Easy blunders, Normal looks two moves ahead, Hard and Master think deeper.",
			Labels = ChessBot.Levels,
			Values = new[] { 0, 1, 2, 3 },
			Get = () => BotSkill,
			Set = v => BotSkill = v
		}
	};

	/// <summary>0 white, 1 black, 2 random (in games you start).</summary>
	[IsaClient.Core.Setting("ChessSide", Min = 0, Max = 2)]
	public static int Side = 2;

	[IsaClient.Core.Setting("ChessTurnSeconds", Min = 0, Max = 255)]
	public static int TurnSeconds = 60;

	[IsaClient.Core.Setting("ChessBot", Min = 0, Max = 3)]
	public static int BotSkill = 1;

	private const int FrameWidth = 12;

	private static readonly int[] Worth = { 0, 1, 3, 3, 5, 9, 0 };

	private static readonly Color LastMove = new Color(255, 214, 80);

	private static readonly Color Selected = new Color(110, 200, 255);

	private P _pos = P.Start();

	private int _whiteSeat;

	private List<int> _legal = new List<int>();

	private readonly List<string> _san = new List<string>();

	/// <summary>Kinds of pieces each side took.</summary>
	private readonly List<int> _takenByWhite = new List<int>();

	private readonly List<int> _takenByBlack = new List<int>();

	private int _lastFrom = -1;

	private int _lastTo = -1;

	private string _ending;

	#region Animation state

	private struct Slide
	{
		public sbyte Piece;

		public int From;

		public int To;
	}

	private readonly List<Slide> _slides = new List<Slide>();

	private sbyte _captured;

	private int _capturedAt = -1;

	private double _movedAt = -10.0;

	private double _resetAt;

	private double _endedAt;

	#endregion

	#region Input state

	private int _selected = -1;

	private bool _pressed;

	private bool _dragging;

	private Vector2 _pressAt;

	private int _promoFrom = -1;

	private int _promoTo = -1;

	private bool _promoDragged;

	private bool _noSlideNext;

	#endregion

	public int Turn => _pos.WhiteToMove ? _whiteSeat : (1 - _whiteSeat);

	public int Result { get; private set; } = -1;

	public void Reset(int firstSeat)
	{
		_whiteSeat = firstSeat;
		_pos = P.Start();
		_legal = _pos.Legal();
		_san.Clear();
		_takenByWhite.Clear();
		_takenByBlack.Clear();
		_lastFrom = _lastTo = -1;
		_ending = null;
		Result = -1;
		_slides.Clear();
		_capturedAt = -1;
		_selected = -1;
		_pressed = _dragging = false;
		_promoFrom = -1;
		_resetAt = GameArt.Now;
	}

	public bool Apply(int seat, byte[] move)
	{
		bool noSlide = _noSlideNext;
		_noSlideNext = false;
		if (Result != -1 || seat != Turn || move == null || move.Length < 2 || move[0] > 63 || move[1] > 63)
		{
			return false;
		}
		int promotion = (move.Length > 2) ? move[2] : 0;
		int found = 0;
		foreach (int m in _legal)
		{
			if (P.From(m) == move[0] && P.To(m) == move[1] && (P.Promotion(m) == promotion || (promotion == 0 && P.Promotion(m) == P.Queen)))
			{
				found = m;
				break;
			}
		}
		if (found == 0)
		{
			return false;
		}
		string san = _pos.San(found);
		sbyte piece = _pos.Squares[move[0]];
		sbyte captured = _pos.CapturedBy(found);
		_slides.Clear();
		if (!noSlide)
		{
			_slides.Add(new Slide { Piece = piece, From = move[0], To = move[1] });
		}
		if ((P.Flags(found) & P.FlagCastle) != 0)
		{
			int from = move[0];
			bool kingSide = move[1] > from;
			_slides.Add(new Slide { Piece = _pos.Squares[kingSide ? (from + 3) : (from - 4)], From = kingSide ? (from + 3) : (from - 4), To = kingSide ? (from + 1) : (from - 1) });
		}
		_captured = captured;
		_capturedAt = (captured == 0) ? -1 : (((P.Flags(found) & P.FlagEnPassant) != 0) ? (move[1] - ((piece > 0) ? 8 : -8)) : move[1]);
		_movedAt = GameArt.Now;
		_pos.Make(found);
		if (captured != 0)
		{
			((captured < 0) ? _takenByWhite : _takenByBlack).Add(Math.Abs(captured));
		}
		_san.Add(san);
		_lastFrom = move[0];
		_lastTo = move[1];
		_selected = -1;
		_promoFrom = -1;
		_legal = _pos.Legal();
		if (_legal.Count == 0)
		{
			bool mate = _pos.InCheck();
			Result = mate ? seat : 2;
			_ending = mate ? IsaClient.Languages.L10n.T("Checkmate") : IsaClient.Languages.L10n.T("Stalemate");
		}
		else if (_pos.HalfMoves >= 100)
		{
			Result = 2;
			_ending = IsaClient.Languages.L10n.T("Fifty moves without a capture");
		}
		else if (_pos.Repetitions() >= 3)
		{
			Result = 2;
			_ending = IsaClient.Languages.L10n.T("Threefold repetition");
		}
		else if (_pos.InsufficientMaterial())
		{
			Result = 2;
			_ending = IsaClient.Languages.L10n.T("Not enough pieces to mate");
		}
		if (Result != -1)
		{
			_endedAt = GameArt.Now;
		}
		if (captured != 0)
		{
			SoundEngine.PlaySound(SoundID.Dig);
		}
		else if (Result == -1 && _pos.InCheck())
		{
			SoundEngine.PlaySound(SoundID.Item37);
		}
		return true;
	}

	public Func<byte[]> Think(int level)
	{
		P copy = _pos.Clone();
		return () =>
		{
			int m = new ChessBot(copy, new Random()).Think(level);
			return (m == 0) ? null : new[] { (byte)P.From(m), (byte)P.To(m), (byte)P.Promotion(m) };
		};
	}

	#region Drawing

	private Rectangle _board;

	private int _square;

	private bool _flip;

	private Rectangle SquareRect(int sq)
	{
		int file = sq & 7;
		int rank = sq >> 3;
		int col = _flip ? (7 - file) : file;
		int row = _flip ? rank : (7 - rank);
		return new Rectangle(_board.X + col * _square, _board.Y + row * _square, _square, _square);
	}

	private Vector2 Center(int sq)
	{
		Rectangle r = SquareRect(sq);
		return new Vector2(r.X + r.Width / 2f, r.Y + r.Height / 2f);
	}

	private int SquareAt(Point p)
	{
		if (!_board.Contains(p))
		{
			return -1;
		}
		int col = (p.X - _board.X) / _square;
		int row = (p.Y - _board.Y) / _square;
		if (col < 0 || col > 7 || row < 0 || row > 7)
		{
			return -1;
		}
		int file = _flip ? (7 - col) : col;
		int rank = _flip ? row : (7 - row);
		return rank * 8 + file;
	}

	private bool Mine(int sq, int mySeat)
	{
		sbyte p = _pos.Squares[sq];
		return p != 0 && (p > 0) == (mySeat == _whiteSeat);
	}

	private IEnumerable<int> MovesFrom(int sq)
	{
		return _legal.Where(m => P.From(m) == sq);
	}

	public byte[] Draw(SpriteBatch sb, Rectangle area, int mySeat, bool canMove)
	{
		int size = Math.Min(area.Width, area.Height);
		Rectangle outer = new Rectangle(area.X + (area.Width - size) / 2, area.Y + (area.Height - size) / 2, size, size);
		_square = (size - FrameWidth * 2) / 8;
		int inner = _square * 8;
		_board = new Rectangle(outer.X + (size - inner) / 2, outer.Y + (size - inner) / 2, inner, inner);
		_flip = mySeat != _whiteSeat;
		double now = GameArt.Now;
		canMove &= Result == -1;
		if (!canMove)
		{
			_selected = -1;
			_pressed = _dragging = false;
			_promoFrom = -1;
		}
		byte[] move = HandleInput(mySeat, canMove);
		DrawFrame(sb, outer);
		DrawSquares(sb);
		DrawMarks(sb, mySeat, canMove, now);
		DrawPieces(sb, now);
		if (_promoFrom >= 0)
		{
			move = DrawPromotion(sb, mySeat) ?? move;
		}
		return move;
	}

	private byte[] HandleInput(int mySeat, bool canMove)
	{
		if (!canMove || _promoFrom >= 0)
		{
			return null;
		}
		Point mouse = MusicPlayerWindow.MousePoint;
		int at = MusicPlayerWindow.MouseIn(_board) ? SquareAt(mouse) : -1;
		byte[] move = null;
		if (at >= 0 && (Mine(at, mySeat) || (_selected >= 0 && MovesFrom(_selected).Any(m => P.To(m) == at))))
		{
			MusicDrawing.Hover("chess:square");
		}
		if (MusicPlayerWindow.Clicked && at >= 0)
		{
			MusicPlayerWindow.Clicked = false;
			if (_selected >= 0 && _selected != at && MovesFrom(_selected).Any(m => P.To(m) == at))
			{
				move = Choose(_selected, at, dragged: false);
			}
			else if (Mine(at, mySeat))
			{
				_selected = at;
				_pressed = true;
				_dragging = false;
				_pressAt = mouse.ToVector2();
				SoundEngine.PlaySound(12);
			}
			else
			{
				_selected = -1;
			}
		}
		if (_pressed)
		{
			if (MusicPlayerWindow.MouseDown)
			{
				if (!_dragging && Vector2.Distance(mouse.ToVector2(), _pressAt) > 5f)
				{
					_dragging = true;
				}
			}
			else
			{
				if (_dragging && at >= 0 && at != _selected && _selected >= 0 && MovesFrom(_selected).Any(m => P.To(m) == at))
				{
					move = Choose(_selected, at, dragged: true);
				}
				_pressed = false;
				_dragging = false;
			}
		}
		return move;
	}

	/// <summary>The move from → to: right away, or after the promotion picker when a pawn reaches the end.</summary>
	private byte[] Choose(int from, int to, bool dragged)
	{
		if (MovesFrom(from).Any(m => P.To(m) == to && P.Promotion(m) != 0))
		{
			_promoFrom = from;
			_promoTo = to;
			_promoDragged = dragged;
			_pressed = _dragging = false;
			return null;
		}
		_noSlideNext = dragged;
		_pressed = _dragging = false;
		return new[] { (byte)from, (byte)to, (byte)0 };
	}

	private void DrawFrame(SpriteBatch sb, Rectangle outer)
	{
		// Dark wood around the board, a lighter lip on the inside edge.
		MusicDrawing.Fill(sb, outer, new Color(52, 32, 26));
		GameArt.Gradient(sb, new Rectangle(outer.X + 2, outer.Y + 2, outer.Width - 4, outer.Height - 4), new Color(104, 64, 44), new Color(70, 42, 32));
		GameArt.Frame(sb, new Rectangle(_board.X - 2, _board.Y - 2, _board.Width + 4, _board.Height + 4), new Color(36, 22, 18), 2);
		GameArt.Frame(sb, new Rectangle(outer.X, outer.Y, outer.Width, outer.Height), new Color(150, 104, 70) * 0.6f);
		float scale = Math.Min(0.6f, FrameWidth / 20f);
		Color label = new Color(232, 206, 160) * 0.85f;
		for (int i = 0; i < 8; i++)
		{
			string file = ((char)('a' + (_flip ? (7 - i) : i))).ToString();
			string rank = ((char)('8' - (_flip ? (7 - i) : i))).ToString();
			float fw = MusicDrawing.Measure(file, scale);
			MusicDrawing.RawText(sb, file, _board.X + i * _square + (_square - fw) / 2f, _board.Bottom + 1, label, scale);
			MusicDrawing.RawText(sb, rank, outer.X + 3, _board.Y + i * _square + (_square - 14f * scale) / 2f, label, scale);
		}
	}

	private void DrawSquares(SpriteBatch sb)
	{
		GameArt.Sharp(sb);
		for (int sq = 0; sq < 64; sq++)
		{
			bool light = ((sq & 7) + (sq >> 3)) % 2 == 1;
			// Flipped tiles so the wood grain doesn't repeat.
			SpriteEffects flip = (SpriteEffects)(((sq * 7) ^ (sq >> 2)) & 3);
			GameArt.Tile(sb, light ? GameArt.LightTile : GameArt.DarkTile, SquareRect(sq), Color.White, flip);
		}
		GameArt.Smooth(sb);
	}

	private void DrawMarks(SpriteBatch sb, int mySeat, bool canMove, double now)
	{
		if (_lastFrom >= 0)
		{
			MusicDrawing.Fill(sb, SquareRect(_lastFrom), LastMove * 0.28f);
			MusicDrawing.Fill(sb, SquareRect(_lastTo), LastMove * 0.38f);
		}
		if (_selected >= 0)
		{
			Rectangle r = SquareRect(_selected);
			MusicDrawing.Fill(sb, r, Selected * 0.42f);
			GameArt.Frame(sb, r, Selected * 0.9f, 2);
		}
		// The king in check glows red.
		if (Result == -1 && _pos.InCheck())
		{
			int king = _pos.KingAt[_pos.WhiteToMove ? 0 : 1];
			float pulse = 0.55f + 0.3f * GameArt.Pulse(0.8);
			GameArt.Disc(sb, Center(king), _square * 1.15f, _square * 1.15f, new Color(255, 40, 40) * pulse);
		}
		// The winner's king shines.
		if (Result == 0 || Result == 1)
		{
			bool whiteWon = Result == _whiteSeat;
			float t = GameArt.Clamp01((now - _endedAt) / 0.6);
			GameArt.Disc(sb, Center(_pos.KingAt[whiteWon ? 0 : 1]), _square * 1.3f, _square * 1.3f, new Color(255, 214, 80) * (0.35f + 0.25f * GameArt.Pulse(1.4)) * t);
		}
		if (_selected >= 0 && canMove)
		{
			Point mouse = MusicPlayerWindow.MousePoint;
			foreach (int m in MovesFrom(_selected))
			{
				int to = P.To(m);
				if (P.Promotion(m) != 0 && P.Promotion(m) != P.Queen)
				{
					continue;
				}
				Vector2 c = Center(to);
				bool over = SquareRect(to).Contains(mouse);
				if (_pos.CapturedBy(m) != 0 && (P.Flags(m) & P.FlagEnPassant) == 0)
				{
					GameArt.Ring(sb, c, _square * 0.96f, Color.Black * (over ? 0.5f : 0.32f));
				}
				else
				{
					float dot = _square * (over ? 0.36f : 0.28f);
					GameArt.Disc(sb, c, dot, dot, Color.Black * 0.3f);
				}
				if (over)
				{
					GameArt.Frame(sb, SquareRect(to), Color.White * 0.5f, 2);
				}
			}
		}
	}

	private void DrawPieces(SpriteBatch sb, double now)
	{
		float slideT = GameArt.Clamp01((now - _movedAt) / 0.26);
		bool sliding = slideT < 1f && _slides.Count > 0;
		Vector2 mouse = MusicPlayerWindow.MousePoint.ToVector2();
		// Everything to draw: (piece, where, lift, scale, alpha, rotation).
		List<(sbyte piece, Vector2 at, float lift, float scale, float alpha, float rotation, Color tint)> draws = new List<(sbyte, Vector2, float, float, float, float, Color)>();
		int loserKing = -1;
		if (Result == 0 || Result == 1)
		{
			loserKing = _pos.KingAt[(Result == _whiteSeat) ? 1 : 0];
		}
		for (int sq = 0; sq < 64; sq++)
		{
			sbyte piece = _pos.Squares[sq];
			if (piece == 0 || (sliding && _slides.Any(s => s.To == sq)))
			{
				continue;
			}
			Vector2 at = Center(sq);
			float alpha = 1f;
			float lift = 0f;
			// A new game: the pieces drop in, back ranks first.
			int row = _flip ? (sq >> 3) : (7 - (sq >> 3));
			float drop = GameArt.Clamp01((now - _resetAt - (piece > 0 ? 7 - row : row) * 0.035 - (sq & 7) * 0.012) / 0.32);
			if (drop < 1f)
			{
				lift = (1f - GameArt.EaseOut(drop)) * _square * 0.7f;
				alpha = drop;
			}
			if (_dragging && sq == _selected)
			{
				alpha = 0.3f;
			}
			float rotation = 0f;
			Color tint = Color.White;
			if (sq == loserKing)
			{
				float fall = GameArt.EaseBack(GameArt.Clamp01((now - _endedAt - 0.25) / 0.5));
				rotation = fall * MathHelper.PiOver2 * 0.92f;
				tint = Color.Lerp(Color.White, new Color(255, 120, 120), GameArt.Clamp01((now - _endedAt) / 0.6));
			}
			draws.Add((piece, at, lift, 1f, alpha, rotation, tint));
		}
		// A captured piece fades where it was taken.
		if (sliding && _capturedAt >= 0)
		{
			float fade = 1f - GameArt.Clamp01((slideT - 0.45f) / 0.4f);
			if (fade > 0f)
			{
				draws.Insert(0, (_captured, Center(_capturedAt), 0f, 1f - (1f - fade) * 0.4f, fade, 0f, Color.White));
			}
		}
		if (sliding)
		{
			float e = GameArt.EaseInOut(slideT);
			foreach (Slide s in _slides)
			{
				Vector2 at = Vector2.Lerp(Center(s.From), Center(s.To), e);
				float hop = (float)Math.Sin(slideT * Math.PI);
				draws.Add((s.Piece, at, hop * _square * 0.16f, 1f + hop * 0.1f, 1f, 0f, Color.White));
			}
		}
		// Shadows (soft) under every piece, then the pieces (sharp).
		foreach (var d in draws)
		{
			float shrink = 1f - Math.Min(0.4f, d.lift / _square);
			GameArt.Disc(sb, d.at + new Vector2(0f, _square * 0.36f), _square * 0.66f * shrink * d.scale, _square * 0.17f * shrink, Color.Black * 0.32f * d.alpha);
		}
		// The landing of a capture: a ring bursts.
		double burst = (now - _movedAt - 0.2) / 0.35;
		if (_capturedAt >= 0 && burst > 0.0 && burst < 1.0)
		{
			float u = (float)burst;
			GameArt.Ring(sb, Center(_lastTo), _square * (0.5f + 0.9f * GameArt.EaseOut(u)), new Color(255, 236, 170) * (1f - u));
		}
		GameArt.Sharp(sb);
		foreach (var d in draws)
		{
			Vector2 at = d.at - new Vector2(0f, d.lift + _square * 0.03f);
			GameArt.Sprite(sb, GameArt.Piece(Math.Abs(d.piece), d.piece > 0), at, _square * d.scale, d.tint * d.alpha, d.rotation);
		}
		if (_dragging && _selected >= 0 && _pos.Squares[_selected] != 0)
		{
			sbyte piece = _pos.Squares[_selected];
			GameArt.Sprite(sb, GameArt.Piece(Math.Abs(piece), piece > 0), mouse - new Vector2(0f, _square * 0.12f), _square * 1.15f, Color.White);
		}
		GameArt.Smooth(sb);
	}

	/// <summary>Queen, rook, bishop or knight for the pawn reaching the end; a click elsewhere cancels.</summary>
	private byte[] DrawPromotion(SpriteBatch sb, int mySeat)
	{
		MusicDrawing.Fill(sb, _board, Color.Black * 0.5f);
		bool white = mySeat == _whiteSeat;
		int[] kinds = { P.Queen, P.Rook, P.Bishop, P.Knight };
		Rectangle target = SquareRect(_promoTo);
		bool down = target.Y + target.Height * 4 <= _board.Bottom;
		Rectangle[] boxes = new Rectangle[4];
		for (int i = 0; i < 4; i++)
		{
			boxes[i] = new Rectangle(target.X, down ? (target.Y + i * _square) : (target.Y - i * _square), _square, _square);
		}
		Rectangle column = Rectangle.Union(boxes[0], boxes[3]);
		MusicDrawing.Box(sb, new Rectangle(column.X - 4, column.Y - 4, column.Width + 8, column.Height + 8), MusicPlayerWindow.BackColor * 0.98f);
		int hover = -1;
		for (int i = 0; i < 4; i++)
		{
			if (MusicPlayerWindow.MouseIn(boxes[i]))
			{
				hover = i;
				MusicDrawing.Fill(sb, boxes[i], MusicPlayerWindow.RowHoverColor);
				MusicDrawing.Hover("chess:promote", "Promote");
			}
		}
		GameArt.Sharp(sb);
		for (int i = 0; i < 4; i++)
		{
			float grow = (i == hover) ? 1.1f : 0.95f;
			GameArt.Sprite(sb, GameArt.Piece(kinds[i], white), boxes[i].Center.ToVector2(), _square * grow, Color.White);
		}
		GameArt.Smooth(sb);
		if (MusicPlayerWindow.Clicked)
		{
			MusicPlayerWindow.Clicked = false;
			int from = _promoFrom;
			int to = _promoTo;
			_promoFrom = -1;
			if (hover >= 0)
			{
				_noSlideNext = _promoDragged;
				return new[] { (byte)from, (byte)to, (byte)kinds[hover] };
			}
		}
		return null;
	}

	#endregion

	#region Hud

	public string SeatName(int seat)
	{
		return IsaClient.Languages.L10n.T((seat == _whiteSeat) ? "chess::White" : "chess::Black");
	}

	public void DrawSeat(SpriteBatch sb, Rectangle area, int seat)
	{
		GameArt.Sharp(sb);
		GameArt.Sprite(sb, GameArt.Piece(P.King, seat == _whiteSeat), area.Center.ToVector2(), Math.Min(area.Width, area.Height) * 1.1f, Color.White);
		GameArt.Smooth(sb);
	}

	/// <summary>The pieces this seat took, smallest last, and how far ahead in material it is.</summary>
	public void DrawSeatExtras(SpriteBatch sb, Rectangle area, int seat)
	{
		bool white = seat == _whiteSeat;
		List<int> taken = (white ? _takenByWhite : _takenByBlack).OrderByDescending(k => Worth[k]).ToList();
		int lead = _takenByWhite.Sum(k => Worth[k]) - _takenByBlack.Sum(k => Worth[k]);
		if (!white)
		{
			lead = -lead;
		}
		int size = Math.Min(20, area.Height);
		int step = (taken.Count <= 1) ? size : Math.Max(6, Math.Min(size - 6, (area.Width - size - 30) / Math.Max(1, taken.Count - 1)));
		GameArt.Sharp(sb);
		for (int i = 0; i < taken.Count; i++)
		{
			GameArt.Sprite(sb, GameArt.Piece(taken[i], !white), new Vector2(area.X + size / 2f + i * step, area.Center.Y), size, Color.White);
		}
		GameArt.Smooth(sb);
		if (lead > 0)
		{
			float x = area.X + ((taken.Count > 0) ? (size + (taken.Count - 1) * step + 4) : 0);
			MusicDrawing.RawText(sb, "+" + lead, x, area.Center.Y - 8, new Color(170, 230, 140), 0.7f);
		}
	}

	public int SideWidth => 118;

	/// <summary>The moves so far, two per row; the newest at the bottom.</summary>
	public void DrawSide(SpriteBatch sb, Rectangle area)
	{
		MusicDrawing.Box(sb, area, MusicPlayerWindow.PanelDarkColor * 0.9f);
		MusicDrawing.Text(sb, "Moves", area.X + 10, area.Y + 7, MusicPlayerWindow.MutedColor, 0.68f);
		MusicDrawing.Fill(sb, new Rectangle(area.X + 8, area.Y + 27, area.Width - 16, 1), MusicPlayerWindow.MutedColor * 0.25f);
		const int lineH = 18;
		int rows = (_san.Count + 1) / 2;
		int fits = Math.Max(1, (area.Height - 38) / lineH);
		int first = Math.Max(0, rows - fits);
		int y = area.Y + 33;
		for (int row = first; row < rows; row++)
		{
			if ((row - first) % 2 == 1)
			{
				MusicDrawing.Fill(sb, new Rectangle(area.X + 4, y - 1, area.Width - 8, lineH), Color.White * 0.03f);
			}
			MusicDrawing.RawText(sb, (row + 1) + ".", area.X + 8, y, MusicPlayerWindow.MutedColor * 0.8f, 0.62f);
			for (int side = 0; side < 2; side++)
			{
				int i = row * 2 + side;
				if (i >= _san.Count)
				{
					break;
				}
				bool newest = i == _san.Count - 1;
				MusicDrawing.RawText(sb, _san[i], area.X + 32 + side * 42, y, newest ? MusicPlayerWindow.AccentColor : Color.White * 0.9f, 0.62f, 42f);
			}
			y += lineH;
		}
		if (_san.Count == 0)
		{
			MusicDrawing.Text(sb, "White moves first.", area.X + 10, area.Y + 36, MusicPlayerWindow.MutedColor * 0.8f, 0.6f, area.Width - 16);
		}
	}

	public string Note
	{
		get
		{
			if (Result != -1)
			{
				return _ending;
			}
			return _pos.InCheck() ? IsaClient.Languages.L10n.T("Check!") : null;
		}
	}

	#endregion
}
