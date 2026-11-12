using System;
using System.Collections.Generic;
using System.Text;

namespace IsaClient.Games;

/// <summary>
/// A chess position and its rules: legal moves (castling, en passant, promotion), check, mate, and the draws (stalemate,
/// fifty moves, threefold repetition, too little material). Squares 0-63: a1 = 0, h1 = 7, a8 = 56. Pieces 1-6 (pawn,
/// knight, bishop, rook, queen, king), positive for white, negative for black. A move is an int:
/// from | to &lt;&lt; 6 | promotion &lt;&lt; 12 | flags &lt;&lt; 15. Plain logic with no game types, so the computer can think on a
/// copy in the background.
/// </summary>
internal sealed class ChessPosition
{
	public const int Pawn = 1;

	public const int Knight = 2;

	public const int Bishop = 3;

	public const int Rook = 4;

	public const int Queen = 5;

	public const int King = 6;

	public const int FlagEnPassant = 1;

	public const int FlagCastle = 2;

	public const int FlagDouble = 4;

	public readonly sbyte[] Squares = new sbyte[64];

	public bool WhiteToMove = true;

	/// <summary>1 white short, 2 white long, 4 black short, 8 black long.</summary>
	public int Castling;

	/// <summary>The square a pawn can capture en passant on, or -1.</summary>
	public int EnPassant = -1;

	/// <summary>Moves since the last capture or pawn move (fifty-move rule counts to 100).</summary>
	public int HalfMoves;

	public ulong Key;

	/// <summary>King squares: [0] white, [1] black.</summary>
	public readonly int[] KingAt = new int[2];

	/// <summary>The keys of the positions before this one (for repetitions).</summary>
	public readonly List<ulong> History = new List<ulong>();

	private struct Undo
	{
		public sbyte Captured;

		public int Castling;

		public int EnPassant;

		public int HalfMoves;

		public ulong Key;
	}

	private Undo[] _undo = new Undo[256];

	private int _depth;

	#region Tables

	private static readonly int[][] KnightTargets = new int[64][];

	private static readonly int[][] KingTargets = new int[64][];

	private static readonly int[] CastleMask = new int[64];

	private static readonly ulong[,] ZPiece = new ulong[13, 64];

	private static readonly ulong[] ZCastle = new ulong[16];

	private static readonly ulong[] ZEnPassant = new ulong[8];

	private static readonly ulong ZBlack;

	private static readonly int[] DiagonalF = { 1, 1, -1, -1 };

	private static readonly int[] DiagonalR = { 1, -1, 1, -1 };

	private static readonly int[] StraightF = { 1, -1, 0, 0 };

	private static readonly int[] StraightR = { 0, 0, 1, -1 };

	static ChessPosition()
	{
		int[] kf = { 1, 2, 2, 1, -1, -2, -2, -1 };
		int[] kr = { 2, 1, -1, -2, -2, -1, 1, 2 };
		for (int sq = 0; sq < 64; sq++)
		{
			List<int> knight = new List<int>();
			List<int> king = new List<int>();
			int f = sq & 7;
			int r = sq >> 3;
			for (int i = 0; i < 8; i++)
			{
				if (On(f + kf[i], r + kr[i]))
				{
					knight.Add((r + kr[i]) * 8 + f + kf[i]);
				}
			}
			for (int df = -1; df <= 1; df++)
			{
				for (int dr = -1; dr <= 1; dr++)
				{
					if ((df != 0 || dr != 0) && On(f + df, r + dr))
					{
						king.Add((r + dr) * 8 + f + df);
					}
				}
			}
			KnightTargets[sq] = knight.ToArray();
			KingTargets[sq] = king.ToArray();
			CastleMask[sq] = 15;
		}
		CastleMask[4] = 15 & ~3;
		CastleMask[7] = 15 & ~1;
		CastleMask[0] = 15 & ~2;
		CastleMask[60] = 15 & ~12;
		CastleMask[63] = 15 & ~4;
		CastleMask[56] = 15 & ~8;
		// Fixed seed: both players' copies agree (only repetitions use the keys, but it keeps things predictable).
		ulong state = 0x9E3779B97F4A7C15UL;
		Func<ulong> next = () =>
		{
			state ^= state << 13;
			state ^= state >> 7;
			state ^= state << 17;
			return state;
		};
		for (int p = 0; p < 13; p++)
		{
			for (int sq = 0; sq < 64; sq++)
			{
				ZPiece[p, sq] = next();
			}
		}
		for (int i = 0; i < 16; i++)
		{
			ZCastle[i] = next();
		}
		for (int i = 0; i < 8; i++)
		{
			ZEnPassant[i] = next();
		}
		ZBlack = next();
	}

	private static bool On(int file, int rank)
	{
		return file >= 0 && file < 8 && rank >= 0 && rank < 8;
	}

	private static ulong Z(sbyte piece, int sq)
	{
		return ZPiece[piece + 6, sq];
	}

	#endregion

	public static ChessPosition Start()
	{
		ChessPosition p = new ChessPosition();
		sbyte[] back = { Rook, Knight, Bishop, Queen, King, Bishop, Knight, Rook };
		for (int f = 0; f < 8; f++)
		{
			p.Squares[f] = back[f];
			p.Squares[8 + f] = Pawn;
			p.Squares[48 + f] = -Pawn;
			p.Squares[56 + f] = (sbyte)(-back[f]);
		}
		p.Castling = 15;
		p.KingAt[0] = 4;
		p.KingAt[1] = 60;
		p.Key = p.ComputeKey();
		return p;
	}

	public ChessPosition Clone()
	{
		ChessPosition p = new ChessPosition();
		Array.Copy(Squares, p.Squares, 64);
		p.WhiteToMove = WhiteToMove;
		p.Castling = Castling;
		p.EnPassant = EnPassant;
		p.HalfMoves = HalfMoves;
		p.Key = Key;
		p.KingAt[0] = KingAt[0];
		p.KingAt[1] = KingAt[1];
		p.History.AddRange(History);
		return p;
	}

	public ulong ComputeKey()
	{
		ulong key = 0;
		for (int sq = 0; sq < 64; sq++)
		{
			if (Squares[sq] != 0)
			{
				key ^= Z(Squares[sq], sq);
			}
		}
		key ^= ZCastle[Castling];
		if (EnPassant >= 0)
		{
			key ^= ZEnPassant[EnPassant & 7];
		}
		if (!WhiteToMove)
		{
			key ^= ZBlack;
		}
		return key;
	}

	public static int From(int move)
	{
		return move & 63;
	}

	public static int To(int move)
	{
		return (move >> 6) & 63;
	}

	public static int Promotion(int move)
	{
		return (move >> 12) & 7;
	}

	public static int Flags(int move)
	{
		return move >> 15;
	}

	private static int Encode(int from, int to, int promotion = 0, int flags = 0)
	{
		return from | (to << 6) | (promotion << 12) | (flags << 15);
	}

	#region Making moves

	public void Make(int move)
	{
		int from = move & 63;
		int to = (move >> 6) & 63;
		int promotion = (move >> 12) & 7;
		int flags = move >> 15;
		sbyte piece = Squares[from];
		int side = (piece > 0) ? 1 : -1;
		if (_depth == _undo.Length)
		{
			Array.Resize(ref _undo, _undo.Length * 2);
		}
		ref Undo undo = ref _undo[_depth++];
		undo.Castling = Castling;
		undo.EnPassant = EnPassant;
		undo.HalfMoves = HalfMoves;
		undo.Key = Key;
		History.Add(Key);
		ulong key = Key ^ Z(piece, from);
		sbyte captured;
		if ((flags & FlagEnPassant) != 0)
		{
			int at = to - 8 * side;
			captured = Squares[at];
			Squares[at] = 0;
			key ^= Z(captured, at);
		}
		else
		{
			captured = Squares[to];
			if (captured != 0)
			{
				key ^= Z(captured, to);
			}
		}
		undo.Captured = captured;
		sbyte placed = (promotion != 0) ? (sbyte)(promotion * side) : piece;
		Squares[to] = placed;
		Squares[from] = 0;
		key ^= Z(placed, to);
		if (piece == King || piece == -King)
		{
			KingAt[(side > 0) ? 0 : 1] = to;
			if ((flags & FlagCastle) != 0)
			{
				int rookFrom = (to > from) ? (from + 3) : (from - 4);
				int rookTo = (to > from) ? (from + 1) : (from - 1);
				sbyte rook = Squares[rookFrom];
				Squares[rookTo] = rook;
				Squares[rookFrom] = 0;
				key ^= Z(rook, rookFrom) ^ Z(rook, rookTo);
			}
		}
		int castling = Castling & CastleMask[from] & CastleMask[to];
		key ^= ZCastle[Castling] ^ ZCastle[castling];
		Castling = castling;
		if (EnPassant >= 0)
		{
			key ^= ZEnPassant[EnPassant & 7];
		}
		EnPassant = ((flags & FlagDouble) != 0) ? (from + 8 * side) : -1;
		if (EnPassant >= 0)
		{
			key ^= ZEnPassant[EnPassant & 7];
		}
		HalfMoves = (piece == Pawn || piece == -Pawn || captured != 0) ? 0 : (HalfMoves + 1);
		WhiteToMove = !WhiteToMove;
		Key = key ^ ZBlack;
	}

	public void Unmake(int move)
	{
		int from = move & 63;
		int to = (move >> 6) & 63;
		int promotion = (move >> 12) & 7;
		int flags = move >> 15;
		Undo undo = _undo[--_depth];
		WhiteToMove = !WhiteToMove;
		int side = WhiteToMove ? 1 : -1;
		sbyte piece = (promotion != 0) ? (sbyte)(Pawn * side) : Squares[to];
		Squares[from] = piece;
		if ((flags & FlagEnPassant) != 0)
		{
			Squares[to] = 0;
			Squares[to - 8 * side] = undo.Captured;
		}
		else
		{
			Squares[to] = undo.Captured;
		}
		if (piece == King || piece == -King)
		{
			KingAt[(side > 0) ? 0 : 1] = from;
			if ((flags & FlagCastle) != 0)
			{
				int rookFrom = (to > from) ? (from + 3) : (from - 4);
				int rookTo = (to > from) ? (from + 1) : (from - 1);
				Squares[rookFrom] = Squares[rookTo];
				Squares[rookTo] = 0;
			}
		}
		Castling = undo.Castling;
		EnPassant = undo.EnPassant;
		HalfMoves = undo.HalfMoves;
		Key = undo.Key;
		History.RemoveAt(History.Count - 1);
	}

	/// <summary>The piece a move takes (0 for none).</summary>
	public sbyte CapturedBy(int move)
	{
		if ((Flags(move) & FlagEnPassant) != 0)
		{
			return (sbyte)(WhiteToMove ? -Pawn : Pawn);
		}
		return Squares[To(move)];
	}

	#endregion

	#region Attacks

	/// <summary>True when a piece of that colour attacks the square.</summary>
	public bool Attacked(int sq, bool byWhite)
	{
		int f = sq & 7;
		int r = sq >> 3;
		int s = byWhite ? 1 : -1;
		// Pawns attack forward: a white pawn on a lower rank.
		int pr = r - s;
		if (pr >= 0 && pr < 8)
		{
			if (f > 0 && Squares[pr * 8 + f - 1] == Pawn * s)
			{
				return true;
			}
			if (f < 7 && Squares[pr * 8 + f + 1] == Pawn * s)
			{
				return true;
			}
		}
		foreach (int t in KnightTargets[sq])
		{
			if (Squares[t] == Knight * s)
			{
				return true;
			}
		}
		foreach (int t in KingTargets[sq])
		{
			if (Squares[t] == King * s)
			{
				return true;
			}
		}
		for (int d = 0; d < 4; d++)
		{
			if (Ray(f, r, DiagonalF[d], DiagonalR[d], s, Bishop))
			{
				return true;
			}
			if (Ray(f, r, StraightF[d], StraightR[d], s, Rook))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>The first piece along a ray is that colour's <paramref name="slider"/> or queen.</summary>
	private bool Ray(int f, int r, int df, int dr, int s, int slider)
	{
		f += df;
		r += dr;
		while (f >= 0 && f < 8 && r >= 0 && r < 8)
		{
			sbyte p = Squares[r * 8 + f];
			if (p != 0)
			{
				return p == slider * s || p == Queen * s;
			}
			f += df;
			r += dr;
		}
		return false;
	}

	public bool InCheck()
	{
		return Attacked(KingAt[WhiteToMove ? 0 : 1], !WhiteToMove);
	}

	/// <summary>After <see cref="Make"/>: true when the side that just moved left its king attacked.</summary>
	public bool LeftKingInCheck()
	{
		return Attacked(KingAt[WhiteToMove ? 1 : 0], WhiteToMove);
	}

	#endregion

	#region Move generation

	/// <summary>Moves that may leave the king in check (see <see cref="LeftKingInCheck"/>). Returns how many.</summary>
	public int Pseudo(int[] moves, bool capturesOnly = false)
	{
		int n = 0;
		int s = WhiteToMove ? 1 : -1;
		for (int from = 0; from < 64; from++)
		{
			sbyte p = Squares[from];
			if (p == 0 || (p > 0) != WhiteToMove)
			{
				continue;
			}
			int f = from & 7;
			int r = from >> 3;
			switch (p * s)
			{
			case Pawn:
			{
				int forward = from + 8 * s;
				bool promotes = (r + s == 7) || (r + s == 0);
				if (Squares[forward] == 0 && (!capturesOnly || promotes))
				{
					n = AddPawn(moves, n, from, forward, promotes, 0, capturesOnly);
					int startRank = (s > 0) ? 1 : 6;
					if (r == startRank && !capturesOnly && Squares[forward + 8 * s] == 0)
					{
						moves[n++] = Encode(from, forward + 8 * s, 0, FlagDouble);
					}
				}
				for (int df = -1; df <= 1; df += 2)
				{
					if (f + df < 0 || f + df > 7)
					{
						continue;
					}
					int to = forward + df;
					sbyte target = Squares[to];
					if (target != 0 && (target > 0) != WhiteToMove)
					{
						n = AddPawn(moves, n, from, to, promotes, 0, capturesOnly);
					}
					else if (to == EnPassant)
					{
						moves[n++] = Encode(from, to, 0, FlagEnPassant);
					}
				}
				break;
			}
			case Knight:
				n = AddTargets(moves, n, from, KnightTargets[from], capturesOnly);
				break;
			case King:
				n = AddTargets(moves, n, from, KingTargets[from], capturesOnly);
				if (!capturesOnly)
				{
					n = AddCastling(moves, n);
				}
				break;
			default:
			{
				int kind = p * s;
				for (int d = 0; d < 4; d++)
				{
					if (kind != Rook)
					{
						n = Slide(moves, n, from, DiagonalF[d], DiagonalR[d], capturesOnly);
					}
					if (kind != Bishop)
					{
						n = Slide(moves, n, from, StraightF[d], StraightR[d], capturesOnly);
					}
				}
				break;
			}
			}
		}
		return n;
	}

	private int AddPawn(int[] moves, int n, int from, int to, bool promotes, int flags, bool capturesOnly)
	{
		if (!promotes)
		{
			moves[n++] = Encode(from, to, 0, flags);
			return n;
		}
		moves[n++] = Encode(from, to, Queen, flags);
		if (!capturesOnly)
		{
			moves[n++] = Encode(from, to, Knight, flags);
			moves[n++] = Encode(from, to, Rook, flags);
			moves[n++] = Encode(from, to, Bishop, flags);
		}
		return n;
	}

	private int AddTargets(int[] moves, int n, int from, int[] targets, bool capturesOnly)
	{
		foreach (int to in targets)
		{
			sbyte target = Squares[to];
			if (target == 0)
			{
				if (!capturesOnly)
				{
					moves[n++] = Encode(from, to);
				}
			}
			else if ((target > 0) != WhiteToMove)
			{
				moves[n++] = Encode(from, to);
			}
		}
		return n;
	}

	private int Slide(int[] moves, int n, int from, int df, int dr, bool capturesOnly)
	{
		int f = (from & 7) + df;
		int r = (from >> 3) + dr;
		while (f >= 0 && f < 8 && r >= 0 && r < 8)
		{
			int to = r * 8 + f;
			sbyte target = Squares[to];
			if (target == 0)
			{
				if (!capturesOnly)
				{
					moves[n++] = Encode(from, to);
				}
			}
			else
			{
				if ((target > 0) != WhiteToMove)
				{
					moves[n++] = Encode(from, to);
				}
				break;
			}
			f += df;
			r += dr;
		}
		return n;
	}

	private int AddCastling(int[] moves, int n)
	{
		bool white = WhiteToMove;
		int e = white ? 4 : 60;
		int shortRight = white ? 1 : 4;
		int longRight = white ? 2 : 8;
		if ((Castling & (shortRight | longRight)) == 0 || KingAt[white ? 0 : 1] != e || Attacked(e, !white))
		{
			return n;
		}
		if ((Castling & shortRight) != 0 && Squares[e + 1] == 0 && Squares[e + 2] == 0 && !Attacked(e + 1, !white) && !Attacked(e + 2, !white))
		{
			moves[n++] = Encode(e, e + 2, 0, FlagCastle);
		}
		if ((Castling & longRight) != 0 && Squares[e - 1] == 0 && Squares[e - 2] == 0 && Squares[e - 3] == 0 && !Attacked(e - 1, !white) && !Attacked(e - 2, !white))
		{
			moves[n++] = Encode(e, e - 2, 0, FlagCastle);
		}
		return n;
	}

	/// <summary>Only the legal moves.</summary>
	public List<int> Legal()
	{
		int[] buffer = new int[256];
		int count = Pseudo(buffer);
		List<int> legal = new List<int>(count);
		for (int i = 0; i < count; i++)
		{
			Make(buffer[i]);
			if (!LeftKingInCheck())
			{
				legal.Add(buffer[i]);
			}
			Unmake(buffer[i]);
		}
		return legal;
	}

	#endregion

	#region Draws

	/// <summary>How many times the current position has happened (counting now).</summary>
	public int Repetitions()
	{
		int count = 1;
		int last = History.Count - 1;
		for (int i = last - 1; i >= 0 && i >= last - HalfMoves; i -= 2)
		{
			if (History[i] == Key)
			{
				count++;
			}
		}
		return count;
	}

	/// <summary>Neither side can ever mate: kings alone, or with one knight or bishop, or bishops all on one colour.</summary>
	public bool InsufficientMaterial()
	{
		int minors = 0;
		int bishopColors = 0;
		for (int sq = 0; sq < 64; sq++)
		{
			int kind = Math.Abs(Squares[sq]);
			switch (kind)
			{
			case 0:
			case King:
				continue;
			case Knight:
				minors++;
				bishopColors |= 4;
				break;
			case Bishop:
				minors++;
				bishopColors |= (((sq & 7) + (sq >> 3)) % 2 == 0) ? 1 : 2;
				break;
			default:
				return false;
			}
		}
		return minors <= 1 || bishopColors == 1 || bishopColors == 2;
	}

	#endregion

	#region Notation

	private const string Letters = " PNBRQK";

	public static string SquareName(int sq)
	{
		return ((char)('a' + (sq & 7))).ToString() + (char)('1' + (sq >> 3));
	}

	/// <summary>Standard notation of a legal move (before it's made): Nf3, exd5, O-O, e8=Q+, Qh7#.</summary>
	public string San(int move)
	{
		int from = From(move);
		int to = To(move);
		int kind = Math.Abs(Squares[from]);
		StringBuilder text = new StringBuilder();
		if ((Flags(move) & FlagCastle) != 0)
		{
			text.Append((to > from) ? "O-O" : "O-O-O");
		}
		else
		{
			bool capture = CapturedBy(move) != 0;
			if (kind == Pawn)
			{
				if (capture)
				{
					text.Append((char)('a' + (from & 7))).Append('x');
				}
				text.Append(SquareName(to));
				if (Promotion(move) != 0)
				{
					text.Append('=').Append(Letters[Promotion(move)]);
				}
			}
			else
			{
				text.Append(Letters[kind]);
				// Another piece of the same kind that can go there too: say which.
				bool sameFile = false;
				bool sameRank = false;
				bool other = false;
				foreach (int m in Legal())
				{
					if (To(m) == to && From(m) != from && Math.Abs(Squares[From(m)]) == kind)
					{
						other = true;
						sameFile |= (From(m) & 7) == (from & 7);
						sameRank |= (From(m) >> 3) == (from >> 3);
					}
				}
				if (other)
				{
					if (!sameFile)
					{
						text.Append((char)('a' + (from & 7)));
					}
					else if (!sameRank)
					{
						text.Append((char)('1' + (from >> 3)));
					}
					else
					{
						text.Append(SquareName(from));
					}
				}
				if (capture)
				{
					text.Append('x');
				}
				text.Append(SquareName(to));
			}
		}
		Make(move);
		if (InCheck())
		{
			text.Append((Legal().Count == 0) ? '#' : '+');
		}
		Unmake(move);
		return text.ToString();
	}

	#endregion
}
