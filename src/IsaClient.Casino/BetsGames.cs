using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace IsaClient.Casino;

/// <summary>Random numbers nobody can guess from the last ones.</summary>
internal static class BetsDice
{
	private static readonly RNGCryptoServiceProvider _rng = new RNGCryptoServiceProvider();

	public static int Next(int max)
	{
		byte[] bytes = new byte[4];
		uint limit = uint.MaxValue - uint.MaxValue % (uint)max;
		uint value;
		do
		{
			_rng.GetBytes(bytes);
			value = BitConverter.ToUInt32(bytes, 0);
		}
		while (value >= limit);
		return (int)(value % (uint)max);
	}

	public static int Seed() => Next(int.MaxValue);
}

/// <summary>One line that paid on the slot machine.</summary>
public sealed class SlotWin
{
	public int Line;

	public int Symbol;

	/// <summary>Symbols that count, from the left (2 for two cherries).</summary>
	public int Count;

	/// <summary>Times the line's bet.</summary>
	public int Pay;
}

/// <summary>
/// The slot machine: three reels, three rows, five lines. Every reel has the same 32 symbols; what a spin pays is worked
/// out from the three stops. It gives back 94.7% of what is bet (see <see cref="ReturnRate"/>).
/// </summary>
public static class Slots
{
	public const int Cherry = 0;

	public const int Seven = 8;

	public const int Symbols = 9;

	public const int Rows = 3;

	public const int Lines = 5;

	/// <summary>How many of each symbol a reel has.</summary>
	private static readonly int[] Weights = { 7, 6, 5, 4, 3, 3, 2, 1, 1 };

	/// <summary>Three in a line pay this many times the line's bet.</summary>
	public static readonly int[] Pays = { 10, 20, 30, 50, 100, 150, 300, 1000, 2500 };

	/// <summary>Two cherries from the left.</summary>
	public const int TwoCherries = 3;

	/// <summary>The rows each line goes through, reel by reel.</summary>
	public static readonly int[][] LineRows =
	{
		new[] { 1, 1, 1 },
		new[] { 0, 0, 0 },
		new[] { 2, 2, 2 },
		new[] { 0, 1, 2 },
		new[] { 2, 1, 0 }
	};

	/// <summary>The bets you can make (all five lines).</summary>
	public static readonly int[] Bets = { 10, 25, 50, 100, 250, 500, 1000 };

	public static readonly int[][] Strips = MakeStrips();

	private static int[][] MakeStrips()
	{
		int[][] strips = new int[3][];
		for (int reel = 0; reel < 3; reel++)
		{
			List<int> strip = new List<int>();
			for (int s = 0; s < Symbols; s++)
			{
				strip.AddRange(Enumerable.Repeat(s, Weights[s]));
			}
			// The same order for everyone: a shuffle with a fixed seed per reel.
			Random random = new Random(7001 + reel * 13);
			for (int i = strip.Count - 1; i > 0; i--)
			{
				int j = random.Next(i + 1);
				(strip[i], strip[j]) = (strip[j], strip[i]);
			}
			strips[reel] = strip.ToArray();
		}
		return strips;
	}

	/// <summary>The symbol on a reel, <paramref name="offset"/> places down from its stop.</summary>
	public static int Symbol(int reel, int stop, int offset)
	{
		int[] strip = Strips[reel];
		return strip[((stop + offset) % strip.Length + strip.Length) % strip.Length];
	}

	public static int[] Spin() => new[] { BetsDice.Next(Strips[0].Length), BetsDice.Next(Strips[1].Length), BetsDice.Next(Strips[2].Length) };

	public static List<SlotWin> Evaluate(int[] stops)
	{
		List<SlotWin> wins = new List<SlotWin>();
		for (int line = 0; line < Lines; line++)
		{
			int a = Symbol(0, stops[0], LineRows[line][0]);
			int b = Symbol(1, stops[1], LineRows[line][1]);
			int c = Symbol(2, stops[2], LineRows[line][2]);
			if (a == b && b == c)
			{
				wins.Add(new SlotWin { Line = line, Symbol = a, Count = 3, Pay = Pays[a] });
			}
			else if (a == Cherry && b == Cherry)
			{
				wins.Add(new SlotWin { Line = line, Symbol = Cherry, Count = 2, Pay = TwoCherries });
			}
		}
		return wins;
	}

	/// <summary>Every possible spin worked out: what the machine gives back of what is bet.</summary>
	public static double ReturnRate()
	{
		long paid = 0;
		long spins = 0;
		int[] stops = new int[3];
		for (stops[0] = 0; stops[0] < Strips[0].Length; stops[0]++)
		{
			for (stops[1] = 0; stops[1] < Strips[1].Length; stops[1]++)
			{
				for (stops[2] = 0; stops[2] < Strips[2].Length; stops[2]++)
				{
					paid += Evaluate(stops).Sum(w => w.Pay);
					spins += Lines;
				}
			}
		}
		return (double)paid / spins;
	}
}

/// <summary>
/// European roulette: one zero, 37 pockets. A bet is the set of numbers it covers (a bit each), so every kind of bet
/// pays the same way: 36 divided by the numbers it covers, with what you bet.
/// </summary>
public static class Roulette
{
	public static readonly int[] Wheel = { 0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26 };

	private static readonly HashSet<int> Reds = new HashSet<int> { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };

	public static bool IsRed(int n) => Reds.Contains(n);

	public static bool IsBlack(int n) => n != 0 && !Reds.Contains(n);

	public static ulong Of(params int[] numbers)
	{
		ulong mask = 0;
		foreach (int n in numbers)
		{
			mask |= 1UL << n;
		}
		return mask;
	}

	public static ulong Where(Func<int, bool> test)
	{
		ulong mask = 0;
		for (int n = 1; n <= 36; n++)
		{
			if (test(n))
			{
				mask |= 1UL << n;
			}
		}
		return mask;
	}

	public static readonly ulong Red = Where(IsRed);

	public static readonly ulong Black = Where(IsBlack);

	public static readonly ulong Even = Where(n => n % 2 == 0);

	public static readonly ulong Odd = Where(n => n % 2 == 1);

	public static readonly ulong Low = Where(n => n <= 18);

	public static readonly ulong High = Where(n => n >= 19);

	public static ulong Dozen(int i) => Where(n => (n - 1) / 12 == i);

	/// <summary>A column of the board: 0 is the bottom row (1, 4, 7...), 2 the top one (3, 6, 9...).</summary>
	public static ulong Column(int i) => Where(n => (n - 1) % 3 == i);

	public static int Covers(ulong mask)
	{
		int count = 0;
		while (mask != 0)
		{
			mask &= mask - 1;
			count++;
		}
		return count;
	}

	/// <summary>What a bet gives back with the bet in it (0 when it lost).</summary>
	public static long Payout(ulong mask, long amount, int number)
	{
		if ((mask & (1UL << number)) == 0)
		{
			return 0;
		}
		return amount * (36 / Math.Max(1, Covers(mask)));
	}

	public static int Spin() => BetsDice.Next(37);
}

/// <summary>
/// A horse race. Six horses; each race gives them their form from a seed that everyone at the table gets, so everyone
/// sees the same field and the same odds, and the order they finish in from another seed that comes when the race starts.
/// The chance of each horse is exact: the odds keep <see cref="Margin"/> for the house.
/// </summary>
public sealed class HorseRace
{
	public const int Horses = 6;

	/// <summary>Seconds the race lasts, until the last horse is in.</summary>
	public const double Length = 24.0;

	public const double Margin = 0.08;

	/// <summary>The bets: on a horse to win, or to come in the first two.</summary>
	public const int Win = 0;

	public const int TopTwo = 1;

	public static readonly string[] Names = { "Hay of Cthulhu", "Eater of Hay", "Duke Hoofron", "Wall of Mane", "Lucky Slime", "Moon Lord's Pride" };

	/// <summary>The rider's silks, and the horse's coat (a mount of the game, tinted).</summary>
	public static readonly (byte R, byte G, byte B)[] Silks = { (206, 46, 58), (56, 112, 214), (206, 148, 28), (48, 150, 90), (168, 72, 196), (112, 118, 140) };

	public readonly double[] Chance = new double[Horses];

	public readonly double[] ChanceTopTwo = new double[Horses];

	public readonly double[] OddsWin = new double[Horses];

	public readonly double[] OddsTopTwo = new double[Horses];

	/// <summary>Finishing order (horses), once it's known.</summary>
	public int[] Order;

	private readonly double[] _finish = new double[Horses];

	private readonly double[] _wave = new double[Horses];

	private readonly double[] _phase = new double[Horses];

	private readonly double[] _surge = new double[Horses];

	public HorseRace(int seed)
	{
		Random random = new Random(seed);
		double[] w = new double[Horses];
		for (int i = 0; i < Horses; i++)
		{
			// Some strong, some long shots.
			w[i] = 0.5 + Math.Pow(random.NextDouble(), 2.0) * 3.0;
		}
		double total = w.Sum();
		for (int i = 0; i < Horses; i++)
		{
			Chance[i] = w[i] / total;
		}
		for (int i = 0; i < Horses; i++)
		{
			double second = 0.0;
			for (int j = 0; j < Horses; j++)
			{
				if (j != i)
				{
					second += Chance[j] * Chance[i] / (1.0 - Chance[j]);
				}
			}
			ChanceTopTwo[i] = Chance[i] + second;
			OddsWin[i] = Odds(Chance[i]);
			OddsTopTwo[i] = Odds(ChanceTopTwo[i]);
		}
	}

	/// <summary>What a chip gives back, with the chip in it, to a tenth.</summary>
	private static double Odds(double chance) => Math.Max(1.1, Math.Floor((1.0 - Margin) / chance * 10.0) / 10.0);

	/// <summary>The race itself: the order comes first (each place by chance), then how each horse runs to get there.</summary>
	public void Run(int seed)
	{
		Random random = new Random(seed);
		List<int> left = Enumerable.Range(0, Horses).ToList();
		List<int> order = new List<int>();
		while (left.Count > 0)
		{
			double total = left.Sum(h => Chance[h]);
			double pick = random.NextDouble() * total;
			int chosen = left[left.Count - 1];
			foreach (int h in left)
			{
				pick -= Chance[h];
				if (pick <= 0.0)
				{
					chosen = h;
					break;
				}
			}
			left.Remove(chosen);
			order.Add(chosen);
		}
		Order = order.ToArray();
		double t = Length * 0.84;
		for (int place = 0; place < Horses; place++)
		{
			int h = Order[place];
			_finish[h] = t;
			t += Length * (0.012 + random.NextDouble() * 0.03);
			_wave[h] = 0.035 + random.NextDouble() * 0.05;
			_phase[h] = random.NextDouble() * Math.PI * 2.0;
			_surge[h] = 1.5 + random.NextDouble() * 1.5;
		}
	}

	/// <summary>How far a horse is at <paramref name="t"/> seconds: 0 at the start, 1 at the finish, a little more after it.</summary>
	public double Position(int horse, double t)
	{
		if (Order == null || t <= 0.0)
		{
			return 0.0;
		}
		double u = t / _finish[horse];
		if (u >= 1.0)
		{
			// Past the line it slows down.
			return 1.0 + (1.0 - Math.Exp(-(u - 1.0) * 6.0)) * 0.05;
		}
		// Out of the gate slowly, then a wave that lets horses pass each other, gone at the line.
		double start = u * u * (3.0 - 2.0 * u) * 0.25 + u * 0.75;
		return start + _wave[horse] * Math.Sin(u * Math.PI * _surge[horse] + _phase[horse]) * u * (1.0 - u) * 4.0 * 0.5;
	}

	/// <summary>The horses in the order they are at <paramref name="t"/>.</summary>
	public int[] Standing(double t)
	{
		return Enumerable.Range(0, Horses).OrderByDescending(h => Position(h, t)).ToArray();
	}

	/// <summary>A bet's key: its horse and kind.</summary>
	public static ulong Key(int horse, int kind) => (ulong)(horse * 2 + kind);

	public static int HorseOf(ulong key) => (int)(key / 2);

	public static int KindOf(ulong key) => (int)(key % 2);

	/// <summary>What a bet gives back with the bet in it (0 when it lost).</summary>
	public long Payout(ulong key, long amount)
	{
		int horse = HorseOf(key);
		if (KindOf(key) == Win)
		{
			return (Order[0] == horse) ? (long)Math.Floor(amount * OddsWin[horse]) : 0;
		}
		return (Order[0] == horse || Order[1] == horse) ? (long)Math.Floor(amount * OddsTopTwo[horse]) : 0;
	}
}
