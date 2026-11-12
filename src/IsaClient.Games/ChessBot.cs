using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace IsaClient.Games;

/// <summary>
/// The computer's chess: alpha-beta with iterative deepening, quiescence on captures, a transposition table, killer
/// and history move ordering, and a material + piece-square evaluation that also knows to corner a lone king. Levels:
/// 0 Easy (looks one move ahead and often errs), 1 Normal (two moves, some noise), 2 Hard (under a second), 3 Master
/// (a few seconds). Runs on its own copy of the position, off the game thread.
/// </summary>
internal sealed class ChessBot
{
	public static readonly string[] Levels = { "Easy", "Normal", "Hard", "Master" };

	private const int Mate = 100000;

	private const int Infinity = 1000000;

	private const int MaxPly = 96;

	private static readonly int[] Value = { 0, 100, 320, 330, 500, 900, 0 };

	#region Piece-square tables (white's view, a8 first)

	private static readonly int[] PawnTable =
	{
		0, 0, 0, 0, 0, 0, 0, 0,
		50, 50, 50, 50, 50, 50, 50, 50,
		10, 10, 20, 30, 30, 20, 10, 10,
		5, 5, 10, 25, 25, 10, 5, 5,
		0, 0, 0, 20, 20, 0, 0, 0,
		5, -5, -10, 0, 0, -10, -5, 5,
		5, 10, 10, -20, -20, 10, 10, 5,
		0, 0, 0, 0, 0, 0, 0, 0
	};

	private static readonly int[] KnightTable =
	{
		-50, -40, -30, -30, -30, -30, -40, -50,
		-40, -20, 0, 0, 0, 0, -20, -40,
		-30, 0, 10, 15, 15, 10, 0, -30,
		-30, 5, 15, 20, 20, 15, 5, -30,
		-30, 0, 15, 20, 20, 15, 0, -30,
		-30, 5, 10, 15, 15, 10, 5, -30,
		-40, -20, 0, 5, 5, 0, -20, -40,
		-50, -40, -30, -30, -30, -30, -40, -50
	};

	private static readonly int[] BishopTable =
	{
		-20, -10, -10, -10, -10, -10, -10, -20,
		-10, 0, 0, 0, 0, 0, 0, -10,
		-10, 0, 5, 10, 10, 5, 0, -10,
		-10, 5, 5, 10, 10, 5, 5, -10,
		-10, 0, 10, 10, 10, 10, 0, -10,
		-10, 10, 10, 10, 10, 10, 10, -10,
		-10, 5, 0, 0, 0, 0, 5, -10,
		-20, -10, -10, -10, -10, -10, -10, -20
	};

	private static readonly int[] RookTable =
	{
		0, 0, 0, 0, 0, 0, 0, 0,
		5, 10, 10, 10, 10, 10, 10, 5,
		-5, 0, 0, 0, 0, 0, 0, -5,
		-5, 0, 0, 0, 0, 0, 0, -5,
		-5, 0, 0, 0, 0, 0, 0, -5,
		-5, 0, 0, 0, 0, 0, 0, -5,
		-5, 0, 0, 0, 0, 0, 0, -5,
		0, 0, 0, 5, 5, 0, 0, 0
	};

	private static readonly int[] QueenTable =
	{
		-20, -10, -10, -5, -5, -10, -10, -20,
		-10, 0, 0, 0, 0, 0, 0, -10,
		-10, 0, 5, 5, 5, 5, 0, -10,
		-5, 0, 5, 5, 5, 5, 0, -5,
		0, 0, 5, 5, 5, 5, 0, -5,
		-10, 5, 5, 5, 5, 5, 0, -10,
		-10, 0, 5, 0, 0, 0, 0, -10,
		-20, -10, -10, -5, -5, -10, -10, -20
	};

	private static readonly int[] KingMiddle =
	{
		-30, -40, -40, -50, -50, -40, -40, -30,
		-30, -40, -40, -50, -50, -40, -40, -30,
		-30, -40, -40, -50, -50, -40, -40, -30,
		-30, -40, -40, -50, -50, -40, -40, -30,
		-20, -30, -30, -40, -40, -30, -30, -20,
		-10, -20, -20, -20, -20, -20, -20, -10,
		20, 20, 0, 0, 0, 0, 20, 20,
		20, 30, 10, 0, 0, 10, 30, 20
	};

	private static readonly int[] KingEnd =
	{
		-50, -40, -30, -20, -20, -30, -40, -50,
		-30, -20, -10, 0, 0, -10, -20, -30,
		-30, -10, 20, 30, 30, 20, -10, -30,
		-30, -10, 30, 40, 40, 30, -10, -30,
		-30, -10, 30, 40, 40, 30, -10, -30,
		-30, -10, 20, 30, 30, 20, -10, -30,
		-30, -30, 0, 0, 0, 0, -30, -30,
		-50, -30, -30, -30, -30, -30, -30, -50
	};

	private static readonly int[][] Tables = { null, PawnTable, KnightTable, BishopTable, RookTable, QueenTable, null };

	#endregion

	private struct Entry
	{
		public ulong Key;

		public int Move;

		public int Score;

		public sbyte Depth;

		/// <summary>0 exact, 1 lower bound, 2 upper bound.</summary>
		public byte Bound;
	}

	private const int TableBits = 17;

	private readonly Entry[] _table = new Entry[1 << TableBits];

	private readonly ChessPosition _p;

	private readonly Random _random;

	private readonly Stopwatch _clock = new Stopwatch();

	private long _limitMs;

	private bool _stop;

	private long _nodes;

	private readonly int[][] _moves = new int[MaxPly + 8][];

	private readonly int[][] _scores = new int[MaxPly + 8][];

	private readonly int[,] _killers = new int[MaxPly + 8, 2];

	private readonly int[] _history = new int[64 * 64];

	public ChessBot(ChessPosition position, Random random)
	{
		_p = position;
		_random = random;
		for (int i = 0; i < _moves.Length; i++)
		{
			_moves[i] = new int[256];
			_scores[i] = new int[256];
		}
	}

	/// <summary>A move for the side to move, or 0 when there is none.</summary>
	public int Think(int level)
	{
		List<int> legal = _p.Legal();
		if (legal.Count == 0)
		{
			return 0;
		}
		if (legal.Count == 1)
		{
			return legal[0];
		}
		_clock.Restart();
		switch (level)
		{
		case 0:
			if (_random.NextDouble() < 0.12)
			{
				return legal[_random.Next(legal.Count)];
			}
			return Noisy(legal, 1, 140);
		case 1:
			return Noisy(legal, 2, 35);
		case 2:
			return Deepen(legal, 800);
		default:
			return Deepen(legal, 2500);
		}
	}

	/// <summary>Every move searched to a fixed depth, a little random noise on each score, the best one.</summary>
	private int Noisy(List<int> legal, int depth, int noise)
	{
		_limitMs = long.MaxValue;
		int best = legal[0];
		int bestScore = -Infinity;
		foreach (int move in legal)
		{
			_p.Make(move);
			int score = -Search(depth - 1, -Infinity, Infinity, 1) + _random.Next(-noise, noise + 1);
			_p.Unmake(move);
			if (score > bestScore)
			{
				bestScore = score;
				best = move;
			}
		}
		return best;
	}

	/// <summary>Deeper and deeper until the time runs out; the best move of the last finished depth.</summary>
	private int Deepen(List<int> legal, long ms)
	{
		_limitMs = ms;
		_stop = false;
		// Shuffled first, so equal moves vary from game to game.
		for (int i = legal.Count - 1; i > 0; i--)
		{
			int j = _random.Next(i + 1);
			int t = legal[i];
			legal[i] = legal[j];
			legal[j] = t;
		}
		int best = legal[0];
		for (int depth = 1; depth <= 64; depth++)
		{
			int alpha = -Infinity;
			int depthBest = 0;
			foreach (int move in legal)
			{
				_p.Make(move);
				int score = -Search(depth - 1, -Infinity, -alpha, 1);
				_p.Unmake(move);
				if (_stop)
				{
					break;
				}
				if (score > alpha)
				{
					alpha = score;
					depthBest = move;
				}
			}
			if (_stop)
			{
				// The old best goes first: any move that finished above it at this depth is better.
				if (depthBest != 0)
				{
					best = depthBest;
				}
				break;
			}
			best = depthBest;
			legal.Remove(best);
			legal.Insert(0, best);
			// A mate found: no need to look further.
			if (alpha > Mate - 200 || _clock.ElapsedMilliseconds * 3 > _limitMs)
			{
				break;
			}
		}
		return best;
	}

	private int Search(int depth, int alpha, int beta, int ply)
	{
		if ((++_nodes & 1023) == 0 && _clock.ElapsedMilliseconds > _limitMs)
		{
			_stop = true;
		}
		if (_stop)
		{
			return 0;
		}
		if (_p.HalfMoves >= 100 || _p.Repetitions() >= 2 || _p.InsufficientMaterial())
		{
			return 0;
		}
		bool inCheck = _p.InCheck();
		if (inCheck)
		{
			depth++;
		}
		if (depth <= 0 || ply >= MaxPly)
		{
			return Quiesce(alpha, beta, ply);
		}
		ref Entry entry = ref _table[(int)(_p.Key & ((1 << TableBits) - 1))];
		int hashMove = 0;
		if (entry.Key == _p.Key)
		{
			hashMove = entry.Move;
			if (entry.Depth >= depth)
			{
				int stored = FromTable(entry.Score, ply);
				if (entry.Bound == 0 || (entry.Bound == 1 && stored >= beta) || (entry.Bound == 2 && stored <= alpha))
				{
					return stored;
				}
			}
		}
		int[] moves = _moves[ply];
		int[] scores = _scores[ply];
		int count = _p.Pseudo(moves);
		Order(moves, scores, count, hashMove, ply);
		int legal = 0;
		int best = -Infinity;
		int bestMove = 0;
		int startAlpha = alpha;
		for (int i = 0; i < count; i++)
		{
			Pick(moves, scores, count, i);
			int move = moves[i];
			_p.Make(move);
			if (_p.LeftKingInCheck())
			{
				_p.Unmake(move);
				continue;
			}
			legal++;
			int score = -Search(depth - 1, -beta, -alpha, ply + 1);
			_p.Unmake(move);
			if (_stop)
			{
				return 0;
			}
			if (score > best)
			{
				best = score;
				bestMove = move;
			}
			if (score > alpha)
			{
				alpha = score;
			}
			if (alpha >= beta)
			{
				if (_p.CapturedBy(move) == 0)
				{
					if (_killers[ply, 0] != move)
					{
						_killers[ply, 1] = _killers[ply, 0];
						_killers[ply, 0] = move;
					}
					_history[move & 4095] += depth * depth;
				}
				break;
			}
		}
		if (legal == 0)
		{
			return inCheck ? (-Mate + ply) : 0;
		}
		entry.Key = _p.Key;
		entry.Move = bestMove;
		entry.Depth = (sbyte)Math.Min(depth, 127);
		entry.Score = ToTable(best, ply);
		entry.Bound = (byte)((best >= beta) ? 1 : ((best <= startAlpha) ? 2 : 0));
		return best;
	}

	private int Quiesce(int alpha, int beta, int ply)
	{
		if ((++_nodes & 1023) == 0 && _clock.ElapsedMilliseconds > _limitMs)
		{
			_stop = true;
		}
		if (_stop)
		{
			return 0;
		}
		int stand = Evaluate();
		if (stand >= beta)
		{
			return stand;
		}
		if (stand > alpha)
		{
			alpha = stand;
		}
		if (ply >= MaxPly)
		{
			return stand;
		}
		int[] moves = _moves[ply];
		int[] scores = _scores[ply];
		int count = _p.Pseudo(moves, capturesOnly: true);
		Order(moves, scores, count, 0, ply);
		for (int i = 0; i < count; i++)
		{
			Pick(moves, scores, count, i);
			int move = moves[i];
			_p.Make(move);
			if (_p.LeftKingInCheck())
			{
				_p.Unmake(move);
				continue;
			}
			int score = -Quiesce(-beta, -alpha, ply + 1);
			_p.Unmake(move);
			if (_stop)
			{
				return 0;
			}
			if (score >= beta)
			{
				return score;
			}
			if (score > alpha)
			{
				alpha = score;
			}
		}
		return alpha;
	}

	private static int ToTable(int score, int ply)
	{
		return (score > Mate - 1000) ? (score + ply) : ((score < -Mate + 1000) ? (score - ply) : score);
	}

	private static int FromTable(int score, int ply)
	{
		return (score > Mate - 1000) ? (score - ply) : ((score < -Mate + 1000) ? (score + ply) : score);
	}

	/// <summary>The table's move first, then captures (best victim, cheapest attacker), killers, history.</summary>
	private void Order(int[] moves, int[] scores, int count, int hashMove, int ply)
	{
		for (int i = 0; i < count; i++)
		{
			int move = moves[i];
			if (move == hashMove)
			{
				scores[i] = 10000000;
				continue;
			}
			sbyte victim = _p.CapturedBy(move);
			int promotion = ChessPosition.Promotion(move);
			if (victim != 0 || promotion != 0)
			{
				int attacker = Math.Abs(_p.Squares[ChessPosition.From(move)]);
				scores[i] = 1000000 + Value[Math.Abs(victim)] * 10 - attacker + Value[promotion];
			}
			else if (move == _killers[ply, 0])
			{
				scores[i] = 900000;
			}
			else if (move == _killers[ply, 1])
			{
				scores[i] = 800000;
			}
			else
			{
				scores[i] = Math.Min(_history[move & 4095], 700000);
			}
		}
	}

	/// <summary>Brings the best remaining move to <paramref name="i"/> (a selection sort, one step at a time).</summary>
	private static void Pick(int[] moves, int[] scores, int count, int i)
	{
		int best = i;
		for (int j = i + 1; j < count; j++)
		{
			if (scores[j] > scores[best])
			{
				best = j;
			}
		}
		if (best != i)
		{
			int m = moves[i];
			moves[i] = moves[best];
			moves[best] = m;
			int s = scores[i];
			scores[i] = scores[best];
			scores[best] = s;
		}
	}

	/// <summary>The score for the side to move, in centipawns.</summary>
	private int Evaluate()
	{
		int score = 0;
		int phase = 0;
		int whiteBishops = 0;
		int blackBishops = 0;
		int whiteMaterial = 0;
		int blackMaterial = 0;
		sbyte[] squares = _p.Squares;
		for (int sq = 0; sq < 64; sq++)
		{
			sbyte piece = squares[sq];
			if (piece == 0)
			{
				continue;
			}
			int kind = Math.Abs(piece);
			if (kind == ChessPosition.King)
			{
				continue;
			}
			// White reads the tables from rank 8 down; black mirrored.
			int index = (piece > 0) ? ((7 - (sq >> 3)) * 8 + (sq & 7)) : sq;
			int value = Value[kind] + Tables[kind][index];
			if (kind >= ChessPosition.Knight)
			{
				phase += (kind == ChessPosition.Queen) ? 4 : ((kind == ChessPosition.Rook) ? 2 : 1);
			}
			if (piece > 0)
			{
				score += value;
				whiteMaterial += Value[kind];
				whiteBishops += (kind == ChessPosition.Bishop) ? 1 : 0;
			}
			else
			{
				score -= value;
				blackMaterial += Value[kind];
				blackBishops += (kind == ChessPosition.Bishop) ? 1 : 0;
			}
		}
		score += ((whiteBishops >= 2) ? 30 : 0) - ((blackBishops >= 2) ? 30 : 0);
		// Kings: sheltered while there is material, active when it's gone.
		phase = Math.Min(phase, 24);
		int wk = _p.KingAt[0];
		int bk = _p.KingAt[1];
		int wIndex = (7 - (wk >> 3)) * 8 + (wk & 7);
		int bIndex = bk;
		score += (KingMiddle[wIndex] * phase + KingEnd[wIndex] * (24 - phase)) / 24;
		score -= (KingMiddle[bIndex] * phase + KingEnd[bIndex] * (24 - phase)) / 24;
		// Ahead in an ending: push the other king to the edge and walk yours up to it, so mates get found.
		int diff = whiteMaterial - blackMaterial;
		if (phase <= 6 && Math.Abs(diff) >= 300)
		{
			int loser = (diff > 0) ? bk : wk;
			int winner = (diff > 0) ? wk : bk;
			int edge = Math.Max(3 - (loser & 7), (loser & 7) - 4) + Math.Max(3 - (loser >> 3), (loser >> 3) - 4);
			int near = 14 - (Math.Abs((loser & 7) - (winner & 7)) + Math.Abs((loser >> 3) - (winner >> 3)));
			int push = edge * 12 + near * 5;
			score += (diff > 0) ? push : -push;
		}
		return _p.WhiteToMove ? score : -score;
	}
}
