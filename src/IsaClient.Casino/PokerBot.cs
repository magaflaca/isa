using System;
using System.Linq;

namespace IsaClient.Casino;

/// <summary>
/// The computer players at a poker table. Each estimates how often its hand wins (a quick Monte Carlo against the
/// hands still in), weighs that against what calling costs, and has a character of its own: some bet more, some call
/// lighter, and now and then one bluffs.
/// </summary>
public static class PokerBot
{
	/// <summary>Names of the computer players (their index is the seat's <see cref="PokerSeat.Bot"/>).</summary>
	public static readonly string[] Names = { "Guide", "Merchant", "Nurse", "Demolitionist", "Arms Dealer", "Goblin Tinkerer", "Wizard", "Mechanic", "Tavernkeep", "Pirate", "Stylist", "Angler", "Princess", "Tax Collector", "Golfer", "Zoologist" };

	public static string Name(int bot) => Names[Math.Abs(bot) % Names.Length];

	/// <summary>How much it likes to bet (1 normal).</summary>
	private static double Aggression(int bot) => 0.8 + (bot * 37 % 7) * 0.08;

	/// <summary>How light it calls (0 tight).</summary>
	private static double Looseness(int bot) => (bot * 53 % 5) * 0.025;

	public static (byte action, int amount) Decide(PokerTable t, int seat, byte[] hole, Random random)
	{
		PokerSeat s = t.Seats[seat];
		int opponents = t.Seats.Count(x => x.Live) - 1;
		double equity = PokerHands.Equity(hole, t.Board.Take(t.BoardCount).ToArray(), Math.Max(1, opponents), 260, random);
		double aggression = Aggression(s.Bot);
		double loose = Looseness(s.Bot);
		int toCall = t.ToCall(seat);
		int pot = t.Pot;
		int stack = s.Stack;
		double roll = random.NextDouble();
		if (toCall == 0)
		{
			bool strong = equity > 0.62 / aggression;
			bool bluff = opponents <= 2 && roll < 0.07 * aggression;
			if (strong || bluff)
			{
				double size = (equity > 0.85) ? (0.75 + random.NextDouble() * 0.5) : (0.45 + random.NextDouble() * 0.3);
				int to = t.CurrentBet + Math.Max(t.BigBlind, (int)(pot * size));
				return Raise(t, seat, to);
			}
			return (PokerAction.Check, 0);
		}
		double potOdds = toCall / (double)(pot + toCall);
		if (equity > 0.78 / Math.Sqrt(aggression) || (equity > 0.6 && roll < 0.25 * aggression))
		{
			// Strong: raise (or shove when the stack is small next to the pot).
			if (stack <= pot * 1.2 || (equity > 0.9 && roll < 0.3))
			{
				return (PokerAction.AllIn, 0);
			}
			int to = t.CurrentBet + Math.Max(t.MinRaise, (int)(pot * (0.6 + random.NextDouble() * 0.5)));
			return Raise(t, seat, to);
		}
		if (equity + loose >= potOdds + 0.04 || (toCall <= t.BigBlind && equity > 0.22))
		{
			return (PokerAction.Call, 0);
		}
		return (PokerAction.Fold, 0);
	}

	private static (byte, int) Raise(PokerTable t, int seat, int to)
	{
		int min = t.MinRaiseTo(seat);
		int max = t.MaxRaiseTo(seat);
		to = Math.Max(min, Math.Min(max, to));
		// Round to the big blind, like people do.
		if (to < max)
		{
			to = Math.Min(max, Math.Max(min, (to + t.BigBlind / 2) / t.BigBlind * t.BigBlind));
		}
		if (to >= max)
		{
			return (PokerAction.AllIn, 0);
		}
		return ((t.CurrentBet == 0) ? PokerAction.Bet : PokerAction.Raise, to);
	}
}
