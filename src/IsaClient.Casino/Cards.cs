using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace IsaClient.Casino;

/// <summary>
/// Playing cards as bytes: 0..51, rank = card % 13 (0 is a two, 12 an ace), suit = card / 13 (spades, hearts,
/// diamonds, clubs). <see cref="NoCard"/> is a card nobody can see (face down).
/// </summary>
public static class Cards
{
	public const byte NoCard = 255;

	public const int Ace = 12;

	public static int Rank(byte card) => card % 13;

	public static int Suit(byte card) => card / 13;

	public static bool Red(byte card) => Suit(card) == 1 || Suit(card) == 2;

	private static readonly string[] RankTexts = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A" };

	private static readonly string[] RankNames = { "Twos", "Threes", "Fours", "Fives", "Sixes", "Sevens", "Eights", "Nines", "Tens", "Jacks", "Queens", "Kings", "Aces" };

	private static readonly string[] HighNames = { "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Jack", "Queen", "King", "Ace" };

	public static string RankText(int rank) => RankTexts[rank];

	public static string RankPlural(int rank) => RankNames[rank];

	public static string RankName(int rank) => HighNames[rank];

	private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

	/// <summary>A fair random number in [0, max) (no modulo bias).</summary>
	public static int Fair(int max)
	{
		if (max <= 1)
		{
			return 0;
		}
		byte[] b = new byte[4];
		uint limit = uint.MaxValue - uint.MaxValue % (uint)max;
		uint v;
		do
		{
			lock (Rng)
			{
				Rng.GetBytes(b);
			}
			v = BitConverter.ToUInt32(b, 0);
		}
		while (v >= limit);
		return (int)(v % (uint)max);
	}

	/// <summary><paramref name="decks"/> shuffled decks (Fisher–Yates with a cryptographic source).</summary>
	public static List<byte> Shuffled(int decks = 1)
	{
		List<byte> cards = new List<byte>(52 * decks);
		for (int d = 0; d < decks; d++)
		{
			for (int c = 0; c < 52; c++)
			{
				cards.Add((byte)c);
			}
		}
		for (int i = cards.Count - 1; i > 0; i--)
		{
			int j = Fair(i + 1);
			byte t = cards[i];
			cards[i] = cards[j];
			cards[j] = t;
		}
		return cards;
	}
}

/// <summary>
/// Poker hands: the best five of up to seven cards as one comparable number (higher is better):
/// category &lt;&lt; 20, then up to five ranks of 4 bits, most important first.
/// </summary>
public static class PokerHands
{
	public const int HighCard = 0;

	public const int Pair = 1;

	public const int TwoPair = 2;

	public const int Trips = 3;

	public const int Straight = 4;

	public const int Flush = 5;

	public const int FullHouse = 6;

	public const int Quads = 7;

	public const int StraightFlush = 8;

	public static int Category(int score) => score >> 20;

	private static int Pack(int category, params int[] ranks)
	{
		int v = category << 20;
		for (int i = 0; i < 5; i++)
		{
			v |= ((i < ranks.Length) ? ranks[i] : 0) << (16 - i * 4);
		}
		return v;
	}

	private static int Kicker(int score, int i) => (score >> (16 - i * 4)) & 15;

	/// <summary>The best five-card hand among <paramref name="cards"/> (5 to 7 of them).</summary>
	public static int Best(IList<byte> cards)
	{
		int n = cards.Count;
		int[] counts = new int[13];
		int[] suitCounts = new int[4];
		int[] suitMask = new int[4];
		int rankMask = 0;
		for (int i = 0; i < n; i++)
		{
			int r = Cards.Rank(cards[i]);
			int s = Cards.Suit(cards[i]);
			counts[r]++;
			suitCounts[s]++;
			suitMask[s] |= 1 << r;
			rankMask |= 1 << r;
		}
		// Straight flush.
		for (int s = 0; s < 4; s++)
		{
			if (suitCounts[s] >= 5)
			{
				int top = StraightTop(suitMask[s]);
				if (top >= 0)
				{
					return Pack(StraightFlush, top);
				}
			}
		}
		// Groups, highest first.
		List<int> quads = new List<int>();
		List<int> trips = new List<int>();
		List<int> pairs = new List<int>();
		List<int> singles = new List<int>();
		for (int r = 12; r >= 0; r--)
		{
			switch (counts[r])
			{
			case 4:
				quads.Add(r);
				break;
			case 3:
				trips.Add(r);
				break;
			case 2:
				pairs.Add(r);
				break;
			case 1:
				singles.Add(r);
				break;
			}
		}
		if (quads.Count > 0)
		{
			int q = quads[0];
			int kicker = HighestExcept(counts, q);
			return Pack(Quads, q, kicker);
		}
		if (trips.Count > 0 && (trips.Count > 1 || pairs.Count > 0))
		{
			int t = trips[0];
			int p = (trips.Count > 1) ? Math.Max(trips[1], (pairs.Count > 0) ? pairs[0] : -1) : pairs[0];
			return Pack(FullHouse, t, p);
		}
		for (int s = 0; s < 4; s++)
		{
			if (suitCounts[s] >= 5)
			{
				List<int> ranks = new List<int>();
				for (int r = 12; r >= 0 && ranks.Count < 5; r--)
				{
					if ((suitMask[s] & (1 << r)) != 0)
					{
						ranks.Add(r);
					}
				}
				return Pack(Flush, ranks.ToArray());
			}
		}
		int straight = StraightTop(rankMask);
		if (straight >= 0)
		{
			return Pack(Straight, straight);
		}
		if (trips.Count > 0)
		{
			int t = trips[0];
			List<int> k = Highest(counts, 2, t);
			return Pack(Trips, t, k[0], k[1]);
		}
		if (pairs.Count >= 2)
		{
			int hi = pairs[0];
			int lo = pairs[1];
			List<int> k = Highest(counts, 1, hi, lo);
			return Pack(TwoPair, hi, lo, k[0]);
		}
		if (pairs.Count == 1)
		{
			int p = pairs[0];
			List<int> k = Highest(counts, 3, p);
			return Pack(Pair, p, k[0], k[1], k[2]);
		}
		List<int> high = Highest(counts, 5);
		return Pack(HighCard, high.ToArray());
	}

	/// <summary>The top rank of the best straight in a set of ranks (the wheel A-2-3-4-5 tops at the five), or -1.</summary>
	private static int StraightTop(int mask)
	{
		for (int top = 12; top >= 4; top--)
		{
			int need = 31 << (top - 4);
			if ((mask & need) == need)
			{
				return top;
			}
		}
		// A-2-3-4-5
		const int wheel = (1 << 12) | 15;
		return ((mask & wheel) == wheel) ? 3 : -1;
	}

	private static int HighestExcept(int[] counts, int except)
	{
		for (int r = 12; r >= 0; r--)
		{
			if (r != except && counts[r] > 0)
			{
				return r;
			}
		}
		return 0;
	}

	/// <summary>The highest <paramref name="howMany"/> ranks present, skipping some (one card each: kickers).</summary>
	private static List<int> Highest(int[] counts, int howMany, params int[] except)
	{
		List<int> list = new List<int>();
		for (int r = 12; r >= 0 && list.Count < howMany; r--)
		{
			if (counts[r] > 0 && Array.IndexOf(except, r) < 0)
			{
				list.Add(r);
			}
		}
		while (list.Count < howMany)
		{
			list.Add(0);
		}
		return list;
	}

	/// <summary>How a hand reads ("Two pair, Kings and Sevens").</summary>
	public static string Name(int score)
	{
		int a = Kicker(score, 0);
		int b = Kicker(score, 1);
		switch (Category(score))
		{
		case StraightFlush:
			return (a == Cards.Ace) ? "Royal flush" : ("Straight flush, " + Cards.RankName(a) + " high");
		case Quads:
			return "Four " + Cards.RankPlural(a);
		case FullHouse:
			return "Full house, " + Cards.RankPlural(a) + " over " + Cards.RankPlural(b);
		case Flush:
			return "Flush, " + Cards.RankName(a) + " high";
		case Straight:
			return "Straight, " + Cards.RankName(a) + " high";
		case Trips:
			return "Three " + Cards.RankPlural(a);
		case TwoPair:
			return "Two pair, " + Cards.RankPlural(a) + " and " + Cards.RankPlural(b);
		case Pair:
			return "Pair of " + Cards.RankPlural(a);
		default:
			return Cards.RankName(a) + " high";
		}
	}

	/// <summary>
	/// How often these hole cards win (ties count half) against <paramref name="opponents"/> random hands, with the
	/// rest of the board dealt at random: a Monte Carlo of <paramref name="trials"/> deals.
	/// </summary>
	public static double Equity(byte[] hole, IList<byte> board, int opponents, int trials, Random random)
	{
		if (opponents <= 0)
		{
			return 1.0;
		}
		bool[] used = new bool[52];
		foreach (byte c in hole)
		{
			used[c] = true;
		}
		foreach (byte c in board)
		{
			used[c] = true;
		}
		List<byte> rest = new List<byte>();
		for (byte c = 0; c < 52; c++)
		{
			if (!used[c])
			{
				rest.Add(c);
			}
		}
		byte[] deck = rest.ToArray();
		int need = (5 - board.Count) + opponents * 2;
		double wins = 0.0;
		byte[] mine = new byte[7];
		byte[] theirs = new byte[7];
		for (int t = 0; t < trials; t++)
		{
			// Partial shuffle of what's needed.
			for (int i = 0; i < need; i++)
			{
				int j = i + random.Next(deck.Length - i);
				byte x = deck[i];
				deck[i] = deck[j];
				deck[j] = x;
			}
			int at = 0;
			int boardCount = board.Count;
			for (int i = 0; i < boardCount; i++)
			{
				mine[2 + i] = board[i];
			}
			for (int i = boardCount; i < 5; i++)
			{
				mine[2 + i] = deck[at++];
			}
			mine[0] = hole[0];
			mine[1] = hole[1];
			int myScore = Best(mine);
			bool lost = false;
			int ties = 0;
			Array.Copy(mine, 2, theirs, 2, 5);
			for (int o = 0; o < opponents; o++)
			{
				theirs[0] = deck[at++];
				theirs[1] = deck[at++];
				int s = Best(theirs);
				if (s > myScore)
				{
					lost = true;
					break;
				}
				if (s == myScore)
				{
					ties++;
				}
			}
			if (!lost)
			{
				wins += (ties == 0) ? 1.0 : (1.0 / (ties + 1));
			}
		}
		return wins / trials;
	}
}

/// <summary>Blackjack hand values: aces count 11 unless that busts; face cards 10.</summary>
public static class BlackjackHands
{
	public static int CardValue(byte card)
	{
		int r = Cards.Rank(card);
		if (r == Cards.Ace)
		{
			return 11;
		}
		return (r >= 8) ? 10 : (r + 2);
	}

	public static int Value(IList<byte> cards, out bool soft)
	{
		int total = 0;
		int aces = 0;
		foreach (byte c in cards)
		{
			if (c == Cards.NoCard)
			{
				continue;
			}
			total += CardValue(c);
			if (Cards.Rank(c) == Cards.Ace)
			{
				aces++;
			}
		}
		while (total > 21 && aces > 0)
		{
			total -= 10;
			aces--;
		}
		soft = aces > 0;
		return total;
	}

	public static int Value(IList<byte> cards) => Value(cards, out _);

	/// <summary>An ace and a ten-value card as the first two cards.</summary>
	public static bool Natural(IList<byte> cards) => cards.Count == 2 && Value(cards) == 21;

	/// <summary>The chance one more card busts the hand (a soft hand can't bust on one card).</summary>
	public static double BustChance(IList<byte> cards)
	{
		int value = Value(cards, out bool soft);
		if (soft || value <= 11)
		{
			return 0.0;
		}
		// ranks 2..9 count their value, tens and faces 10, the ace 1
		int busting = 0;
		for (int rank = 0; rank < 13; rank++)
		{
			int v = (rank == Cards.Ace) ? 1 : ((rank >= 8) ? 10 : (rank + 2));
			if (value + v > 21)
			{
				busting++;
			}
		}
		return busting / 13.0;
	}

	/// <summary>How often the dealer busts showing this card (six decks, standing on all 17s).</summary>
	public static double DealerBustChance(byte up)
	{
		switch (CardValue(up))
		{
		case 2:
			return 0.353;
		case 3:
			return 0.376;
		case 4:
			return 0.403;
		case 5:
			return 0.429;
		case 6:
			return 0.421;
		case 7:
			return 0.262;
		case 8:
			return 0.245;
		case 9:
			return 0.228;
		case 10:
			return 0.214;
		default:
			return 0.117;
		}
	}

	/// <summary>
	/// What basic strategy plays (six decks, the dealer stands on 17): a <see cref="BlackjackAction"/> (Hit, Stand,
	/// Double, Split). When doubling isn't allowed it falls back to hitting (or standing on a soft 18).
	/// </summary>
	public static byte BestPlay(IList<byte> cards, byte dealerUp, bool canDouble, bool canSplit)
	{
		int up = CardValue(dealerUp);
		int value = Value(cards, out bool soft);
		if (canSplit && cards.Count == 2)
		{
			int pair = CardValue(cards[0]);
			bool split = pair switch
			{
				11 => true,
				8 => true,
				10 => false,
				5 => false,
				9 => up != 7 && up <= 9,
				7 => up <= 7,
				6 => up <= 6,
				4 => up == 5 || up == 6,
				_ => up <= 7
			};
			if (split)
			{
				return BlackjackAction.Split;
			}
		}
		byte Double(byte otherwise) => canDouble ? BlackjackAction.Double : otherwise;
		if (soft && cards.Count >= 2)
		{
			if (value >= 19)
			{
				return BlackjackAction.Stand;
			}
			if (value == 18)
			{
				if (up >= 3 && up <= 6)
				{
					return Double(BlackjackAction.Stand);
				}
				return (up >= 9) ? BlackjackAction.Hit : BlackjackAction.Stand;
			}
			if (value == 17)
			{
				return (up >= 3 && up <= 6) ? Double(BlackjackAction.Hit) : BlackjackAction.Hit;
			}
			if (value >= 15)
			{
				return (up >= 4 && up <= 6) ? Double(BlackjackAction.Hit) : BlackjackAction.Hit;
			}
			return (up == 5 || up == 6) ? Double(BlackjackAction.Hit) : BlackjackAction.Hit;
		}
		if (value >= 17)
		{
			return BlackjackAction.Stand;
		}
		if (value >= 13)
		{
			return (up <= 6) ? BlackjackAction.Stand : BlackjackAction.Hit;
		}
		if (value == 12)
		{
			return (up >= 4 && up <= 6) ? BlackjackAction.Stand : BlackjackAction.Hit;
		}
		if (value == 11)
		{
			return (up <= 10) ? Double(BlackjackAction.Hit) : BlackjackAction.Hit;
		}
		if (value == 10)
		{
			return (up <= 9) ? Double(BlackjackAction.Hit) : BlackjackAction.Hit;
		}
		if (value == 9)
		{
			return (up >= 3 && up <= 6) ? Double(BlackjackAction.Hit) : BlackjackAction.Hit;
		}
		return BlackjackAction.Hit;
	}
}
