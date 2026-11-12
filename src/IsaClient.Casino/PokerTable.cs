using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IsaClient.Casino;

public enum SeatKind : byte
{
	Empty,
	Player,
	Bot
}

public enum PokerPhase : byte
{
	/// <summary>Not enough players with chips: waiting for someone to sit.</summary>
	Waiting,
	Preflop,
	Flop,
	Turn,
	River,
	/// <summary>The hand is over: who won, the cards shown; the next one starts in a moment.</summary>
	Showdown
}

public static class PokerAction
{
	public const byte None = 0;

	public const byte Fold = 1;

	public const byte Check = 2;

	public const byte Call = 3;

	public const byte Bet = 4;

	public const byte Raise = 5;

	public const byte AllIn = 6;

	public const byte SmallBlind = 7;

	public const byte BigBlind = 8;

	public static string Name(byte action)
	{
		switch (action)
		{
		case Fold:
			return "Fold";
		case Check:
			return "Check";
		case Call:
			return "Call";
		case Bet:
			return "Bet";
		case Raise:
			return "Raise";
		case AllIn:
			return "All in";
		case SmallBlind:
			return "Small blind";
		case BigBlind:
			return "Big blind";
		default:
			return "";
		}
	}
}

public sealed class PokerSeat
{
	public SeatKind Kind;

	/// <summary>The Terraria player sitting here (online), -1 for the computer.</summary>
	public int Player = -1;

	/// <summary>Which computer player (its look and name).</summary>
	public int Bot;

	public int Stack;

	/// <summary>Put in on this street.</summary>
	public int Bet;

	/// <summary>Put in this hand (all streets).</summary>
	public int Committed;

	public bool Dealt;

	public bool Folded;

	public bool AllIn;

	public bool Acted;

	/// <summary>Out of the next hands until they come back (no chips, or away).</summary>
	public bool SittingOut;

	/// <summary>Leaves once the hand is over.</summary>
	public bool Leaving;

	public byte LastAction;

	/// <summary>Cards shown at the showdown.</summary>
	public byte[] Shown = { Cards.NoCard, Cards.NoCard };

	/// <summary>Won this hand.</summary>
	public int Won;

	/// <summary>Timeouts in a row (twice and they sit out).</summary>
	public int Timeouts;

	public bool Empty => Kind == SeatKind.Empty;

	/// <summary>Still in the hand (dealt and not folded).</summary>
	public bool Live => Kind != SeatKind.Empty && Dealt && !Folded;

	/// <summary>Can still act this hand.</summary>
	public bool CanAct => Live && !AllIn;

	public void Clear()
	{
		Kind = SeatKind.Empty;
		Player = -1;
		Bot = 0;
		Stack = Bet = Committed = Won = Timeouts = 0;
		Dealt = Folded = AllIn = Acted = SittingOut = Leaving = false;
		LastAction = 0;
		Shown = new[] { Cards.NoCard, Cards.NoCard };
	}
}

/// <summary>
/// A No-Limit Texas Hold'em table. The host keeps the deck and the hole cards and runs it (<see cref="Tick"/>,
/// <see cref="Act"/>); everyone else gets its public state (<see cref="Write"/> / <see cref="Read"/>) and only their
/// own two cards. Blinds, streets, minimum raises, all-ins and side pots, split pots, a clock for each turn.
/// </summary>
public sealed class PokerTable
{
	public const int MaxSeats = 6;

	/// <summary>Seconds for each turn.</summary>
	public const double TurnSeconds = 25.0;

	/// <summary>How long the result of a hand stays before the next one.</summary>
	public const double ShowdownSeconds = 5.5;

	public const double QuietWinSeconds = 3.0;

	public byte Id;

	public int SmallBlind = 5;

	public int BigBlind = 10;

	public readonly PokerSeat[] Seats = new PokerSeat[MaxSeats];

	public PokerPhase Phase;

	public int Button = -1;

	public int ToAct = -1;

	public int CurrentBet;

	/// <summary>The smallest raise allowed now (the last raise's size, at least the big blind).</summary>
	public int MinRaise;

	public readonly byte[] Board = { Cards.NoCard, Cards.NoCard, Cards.NoCard, Cards.NoCard, Cards.NoCard };

	public int BoardCount;

	public ushort HandNo;

	/// <summary>Goes up with every change (the newest state wins).</summary>
	public ushort Seq;

	/// <summary>Seconds left for whoever acts (or until the next hand).</summary>
	public double Clock;

	/// <summary>The hand was won without a showdown (everyone else folded).</summary>
	public bool QuietWin;

	/// <summary>What the winning hand(s) were, for the showdown banner.</summary>
	public int WinningScore;

	// Host only.
	private List<byte> _deck = new List<byte>();

	private readonly byte[][] _hole = new byte[MaxSeats][];

	public PokerTable()
	{
		for (int i = 0; i < MaxSeats; i++)
		{
			Seats[i] = new PokerSeat();
			_hole[i] = new[] { Cards.NoCard, Cards.NoCard };
		}
	}

	public int Pot => Seats.Sum(s => s.Committed);

	public int Seated => Seats.Count(s => !s.Empty);

	public int SeatOf(int player) => Array.FindIndex(Seats, s => s.Kind == SeatKind.Player && s.Player == player);

	public byte[] Hole(int seat) => _hole[seat];

	public int ToCall(int seat) => Math.Max(0, CurrentBet - Seats[seat].Bet);

	/// <summary>The least a raise can go to (the whole bet), or the seat's all-in when that's less.</summary>
	public int MinRaiseTo(int seat) => Math.Min(CurrentBet + Math.Max(MinRaise, BigBlind), Seats[seat].Bet + Seats[seat].Stack);

	public int MaxRaiseTo(int seat) => Seats[seat].Bet + Seats[seat].Stack;

	private void Changed()
	{
		Seq++;
	}

	#region Seats

	/// <summary>Sits someone at a free seat (the first one, or <paramref name="preferred"/>); -1 when full.</summary>
	public int Sit(SeatKind kind, int player, int bot, int stack, int preferred = -1)
	{
		int seat = (preferred >= 0 && preferred < MaxSeats && Seats[preferred].Empty) ? preferred : Array.FindIndex(Seats, s => s.Empty);
		if (seat < 0)
		{
			return -1;
		}
		PokerSeat s = Seats[seat];
		s.Clear();
		s.Kind = kind;
		s.Player = player;
		s.Bot = bot;
		s.Stack = stack;
		Changed();
		return seat;
	}

	/// <summary>Someone gets up: at once between hands, else they fold and leave when it's over. Returns their stack when gone now.</summary>
	public void Stand(int seat)
	{
		PokerSeat s = Seats[seat];
		if (s.Empty)
		{
			return;
		}
		if (Phase != PokerPhase.Waiting && Phase != PokerPhase.Showdown && s.Live)
		{
			s.Leaving = true;
			if (ToAct == seat)
			{
				Act(seat, PokerAction.Fold, 0);
			}
			else
			{
				// Not their turn: whoever is acting goes on (unless only one is left now).
				s.Folded = true;
				s.LastAction = PokerAction.Fold;
				if (Seats.Count(x => x.Live) == 1)
				{
					AfterFold();
				}
			}
			Changed();
			return;
		}
		if (Phase == PokerPhase.Showdown || (Phase != PokerPhase.Waiting && s.Dealt))
		{
			s.Leaving = true;
			Changed();
			return;
		}
		s.Clear();
		Changed();
	}

	/// <summary>More chips to a seat between hands (a rebuy), and back in.</summary>
	public void AddChips(int seat, int chips)
	{
		PokerSeat s = Seats[seat];
		if (s.Empty || chips <= 0)
		{
			return;
		}
		s.Stack += chips;
		s.SittingOut = false;
		s.Timeouts = 0;
		Changed();
	}

	public void SetSittingOut(int seat, bool value)
	{
		if (!Seats[seat].Empty && Seats[seat].SittingOut != value)
		{
			Seats[seat].SittingOut = value;
			Seats[seat].Timeouts = 0;
			Changed();
		}
	}

	private bool Eligible(PokerSeat s) => !s.Empty && !s.SittingOut && !s.Leaving && s.Stack > 0;

	#endregion

	#region Running it (host)

	/// <summary>
	/// The host's clock: starts hands, takes the time away from whoever doesn't act (check, else fold), and asks the
	/// computer players (<paramref name="bot"/> returns its action and amount, or null to wait).
	/// </summary>
	public void Tick(double seconds, Func<int, (byte action, int amount)?> bot)
	{
		Clock = Math.Max(0.0, Clock - seconds);
		switch (Phase)
		{
		case PokerPhase.Waiting:
			if (Seats.Count(Eligible) >= 2)
			{
				if (Clock <= 0.0)
				{
					StartHand();
				}
			}
			else if (Clock < 2.0)
			{
				// Someone sits: a moment before the cards come.
				Clock = 2.0;
			}
			break;
		case PokerPhase.Showdown:
			if (Clock <= 0.0)
			{
				EndHand();
			}
			break;
		default:
			if (ToAct < 0)
			{
				break;
			}
			PokerSeat s = Seats[ToAct];
			if (s.Kind == SeatKind.Bot)
			{
				(byte action, int amount)? choice = bot?.Invoke(ToAct);
				if (choice.HasValue && !Act(ToAct, choice.Value.action, choice.Value.amount))
				{
					Act(ToAct, (ToCall(ToAct) == 0) ? PokerAction.Check : PokerAction.Fold, 0);
				}
			}
			else if (Clock <= 0.0)
			{
				int seat = ToAct;
				s.Timeouts++;
				Act(seat, (ToCall(seat) == 0) ? PokerAction.Check : PokerAction.Fold, 0);
				if (Seats[seat].Timeouts >= 2)
				{
					Seats[seat].SittingOut = true;
				}
			}
			break;
		}
	}

	private int Next(int from, Func<PokerSeat, bool> ok)
	{
		for (int i = 1; i <= MaxSeats; i++)
		{
			int seat = ((from < 0 ? -1 : from) + i + MaxSeats) % MaxSeats;
			if (ok(Seats[seat]))
			{
				return seat;
			}
		}
		return -1;
	}

	private void StartHand()
	{
		List<int> playing = Enumerable.Range(0, MaxSeats).Where(i => Eligible(Seats[i])).ToList();
		if (playing.Count < 2)
		{
			Phase = PokerPhase.Waiting;
			Changed();
			return;
		}
		HandNo++;
		QuietWin = false;
		WinningScore = 0;
		_deck = Cards.Shuffled();
		foreach (PokerSeat s in Seats)
		{
			s.Bet = s.Committed = s.Won = 0;
			s.Folded = s.AllIn = s.Acted = false;
			s.LastAction = 0;
			s.Shown = new[] { Cards.NoCard, Cards.NoCard };
			s.Dealt = Eligible(s);
		}
		for (int i = 0; i < 5; i++)
		{
			Board[i] = Cards.NoCard;
		}
		BoardCount = 0;
		Button = Next(Button, Eligible);
		bool headsUp = playing.Count == 2;
		int sb = headsUp ? Button : Next(Button, s => s.Dealt);
		int bb = Next(sb, s => s.Dealt);
		// Two cards each, one at a time from the button's left.
		for (int round = 0; round < 2; round++)
		{
			int seat = sb;
			for (int k = 0; k < playing.Count; k++)
			{
				_hole[seat][round] = Draw();
				seat = Next(seat, s => s.Dealt);
			}
		}
		Post(sb, SmallBlind, PokerAction.SmallBlind);
		Post(bb, BigBlind, PokerAction.BigBlind);
		CurrentBet = BigBlind;
		MinRaise = BigBlind;
		Phase = PokerPhase.Preflop;
		ToAct = Next(bb, s => s.CanAct);
		// The blinds haven't acted: the big blind still gets its option.
		foreach (PokerSeat s in Seats)
		{
			s.Acted = false;
		}
		Clock = TurnSeconds;
		if (ToAct < 0 || Seats.Count(s => s.CanAct) == 0 || (Seats.Count(s => s.CanAct) == 1 && Seats.Where(s => s.CanAct).All(s => s.Bet >= CurrentBet)))
		{
			RunOut();
		}
		Changed();
	}

	private byte Draw()
	{
		byte c = _deck[_deck.Count - 1];
		_deck.RemoveAt(_deck.Count - 1);
		return c;
	}

	private void Post(int seat, int amount, byte action)
	{
		PokerSeat s = Seats[seat];
		int paid = Math.Min(amount, s.Stack);
		s.Stack -= paid;
		s.Bet += paid;
		s.Committed += paid;
		s.LastAction = action;
		if (s.Stack == 0)
		{
			s.AllIn = true;
		}
	}

	/// <summary>An action by a seat: fold, check, call, bet/raise to <paramref name="amount"/> (the whole bet), all in. False when it isn't allowed.</summary>
	public bool Act(int seat, byte action, int amount)
	{
		if (seat != ToAct || seat < 0 || Phase < PokerPhase.Preflop || Phase > PokerPhase.River)
		{
			return false;
		}
		PokerSeat s = Seats[seat];
		int toCall = ToCall(seat);
		switch (action)
		{
		case PokerAction.Fold:
			s.Folded = true;
			break;
		case PokerAction.Check:
			if (toCall != 0)
			{
				return false;
			}
			break;
		case PokerAction.Call:
		{
			if (toCall == 0)
			{
				action = PokerAction.Check;
				break;
			}
			int paid = Math.Min(toCall, s.Stack);
			Put(s, paid);
			if (s.AllIn)
			{
				action = PokerAction.AllIn;
			}
			break;
		}
		case PokerAction.Bet:
		case PokerAction.Raise:
		case PokerAction.AllIn:
		{
			int to = (action == PokerAction.AllIn) ? (s.Bet + s.Stack) : amount;
			int max = s.Bet + s.Stack;
			if (to > max)
			{
				to = max;
			}
			if (to <= CurrentBet)
			{
				// Not more than the bet: a call (an all-in short of it is a call too).
				if (action != PokerAction.AllIn)
				{
					return false;
				}
				Put(s, Math.Min(toCall, s.Stack));
				action = PokerAction.AllIn;
				break;
			}
			int raise = to - CurrentBet;
			bool full = raise >= Math.Max(MinRaise, BigBlind);
			if (!full && to < max)
			{
				// Less than a full raise is only allowed all in.
				return false;
			}
			Put(s, to - s.Bet);
			if (full)
			{
				MinRaise = raise;
				// Everyone else acts again.
				foreach (PokerSeat other in Seats)
				{
					if (other != s && other.CanAct)
					{
						other.Acted = false;
					}
				}
			}
			action = s.AllIn ? PokerAction.AllIn : ((CurrentBet == 0) ? PokerAction.Bet : PokerAction.Raise);
			CurrentBet = to;
			break;
		}
		default:
			return false;
		}
		s.Acted = true;
		s.LastAction = action;
		if (action != PokerAction.Fold)
		{
			s.Timeouts = 0;
		}
		if (action == PokerAction.Fold)
		{
			AfterFold();
		}
		else
		{
			Advance();
		}
		Changed();
		return true;
	}

	private static void Put(PokerSeat s, int chips)
	{
		chips = Math.Min(chips, s.Stack);
		s.Stack -= chips;
		s.Bet += chips;
		s.Committed += chips;
		if (s.Stack == 0)
		{
			s.AllIn = true;
		}
	}

	private void AfterFold()
	{
		if (Seats.Count(s => s.Live) == 1)
		{
			// Everyone else folded: the last one takes it all, no cards shown.
			QuietWin = true;
			CollectBets();
			Award(Seats.First(s => s.Live), Pot);
			Finish(QuietWinSeconds);
			return;
		}
		Advance();
	}

	/// <summary>Whose turn next, or the next street when everyone has acted and matched the bet.</summary>
	private void Advance()
	{
		int next = Next(ToAct, s => s.CanAct && (!s.Acted || s.Bet < CurrentBet));
		if (next >= 0)
		{
			ToAct = next;
			Clock = TurnSeconds;
			return;
		}
		NextStreet();
	}

	private void CollectBets()
	{
		foreach (PokerSeat s in Seats)
		{
			s.Bet = 0;
			s.Acted = false;
		}
		CurrentBet = 0;
		MinRaise = BigBlind;
	}

	private void NextStreet()
	{
		CollectBets();
		if (Phase == PokerPhase.River)
		{
			Showdown();
			return;
		}
		DealStreet();
		int canAct = Seats.Count(s => s.CanAct);
		if (canAct < 2)
		{
			// At most one can still bet: the rest of the board comes out, then the showdown.
			RunOut();
			return;
		}
		ToAct = Next(Button, s => s.CanAct);
		Clock = TurnSeconds;
	}

	private void DealStreet()
	{
		_deck.RemoveAt(_deck.Count - 1); // burn
		int cards = (Phase == PokerPhase.Preflop) ? 3 : 1;
		for (int i = 0; i < cards; i++)
		{
			Board[BoardCount++] = Draw();
		}
		Phase = (Phase == PokerPhase.Preflop) ? PokerPhase.Flop : ((Phase == PokerPhase.Flop) ? PokerPhase.Turn : PokerPhase.River);
	}

	private void RunOut()
	{
		CollectBets();
		while (Phase != PokerPhase.River)
		{
			DealStreet();
		}
		Showdown();
	}

	/// <summary>Every live hand is shown; each pot (main and side pots) goes to the best hand that can win it.</summary>
	private void Showdown()
	{
		ToAct = -1;
		List<int> live = Enumerable.Range(0, MaxSeats).Where(i => Seats[i].Live).ToList();
		Dictionary<int, int> scores = new Dictionary<int, int>();
		foreach (int seat in live)
		{
			Seats[seat].Shown = (byte[])_hole[seat].Clone();
			scores[seat] = PokerHands.Best(_hole[seat].Concat(Board.Take(BoardCount)).ToArray());
		}
		// Pots by level of what each put in.
		List<int> levels = Seats.Where(s => s.Committed > 0).Select(s => s.Committed).Distinct().OrderBy(v => v).ToList();
		int previous = 0;
		foreach (int level in levels)
		{
			int pot = 0;
			foreach (PokerSeat s in Seats)
			{
				pot += Math.Max(0, Math.Min(s.Committed, level) - previous);
			}
			List<int> eligible = live.Where(i => Seats[i].Committed >= level).ToList();
			previous = level;
			if (pot == 0)
			{
				continue;
			}
			if (eligible.Count == 0)
			{
				// Nobody still in put this much (they folded): it goes back to whoever put it in.
				foreach (PokerSeat s in Seats.Where(s => s.Committed >= level))
				{
					s.Stack += pot / Math.Max(1, Seats.Count(x => x.Committed >= level));
				}
				continue;
			}
			int best = eligible.Max(i => scores[i]);
			List<int> winners = eligible.Where(i => scores[i] == best).ToList();
			int share = pot / winners.Count;
			int odd = pot - share * winners.Count;
			// The odd chip goes to the first winner left of the button.
			int first = Next(Button, s => winners.Contains(Array.IndexOf(Seats, s)));
			foreach (int w in winners)
			{
				Award(Seats[w], share + ((w == first) ? odd : 0));
			}
			WinningScore = Math.Max(WinningScore, best);
		}
		foreach (PokerSeat s in Seats)
		{
			s.Committed = 0;
		}
		Finish(ShowdownSeconds);
	}

	private static void Award(PokerSeat s, int chips)
	{
		s.Stack += chips;
		s.Won += chips;
	}

	private void Finish(double seconds)
	{
		foreach (PokerSeat s in Seats)
		{
			s.Committed = 0;
			s.Bet = 0;
		}
		Phase = PokerPhase.Showdown;
		ToAct = -1;
		Clock = seconds;
	}

	/// <summary>After the result: who leaves goes, who has no chips sits out, and the next hand comes.</summary>
	private void EndHand()
	{
		foreach (PokerSeat s in Seats)
		{
			if (s.Leaving)
			{
				s.Clear();
				continue;
			}
			s.Dealt = false;
			if (s.Kind == SeatKind.Player && s.Stack == 0)
			{
				s.SittingOut = true;
			}
		}
		Phase = PokerPhase.Waiting;
		ToAct = -1;
		Clock = 1.5;
		Changed();
	}

	#endregion

	#region Sending it

	/// <summary>What everyone sees (no hole cards except the ones shown).</summary>
	public byte[] Write()
	{
		using MemoryStream ms = new MemoryStream();
		using BinaryWriter w = new BinaryWriter(ms);
		w.Write(Id);
		w.Write(Seq);
		w.Write(HandNo);
		w.Write((ushort)SmallBlind);
		w.Write((ushort)BigBlind);
		w.Write((byte)Phase);
		w.Write((sbyte)Button);
		w.Write((sbyte)ToAct);
		w.Write(CurrentBet);
		w.Write(MinRaise);
		w.Write((byte)Math.Min(255, Math.Ceiling(Clock)));
		w.Write((byte)((QuietWin ? 1 : 0) | (BoardCount << 1)));
		w.Write(WinningScore);
		for (int i = 0; i < 5; i++)
		{
			w.Write(Board[i]);
		}
		foreach (PokerSeat s in Seats)
		{
			w.Write((byte)s.Kind);
			if (s.Empty)
			{
				continue;
			}
			w.Write((byte)Math.Max(0, s.Player));
			w.Write((byte)s.Bot);
			w.Write(s.Stack);
			w.Write(s.Bet);
			w.Write(s.Committed);
			w.Write(s.Won);
			int flags = (s.Dealt ? 1 : 0) | (s.Folded ? 2 : 0) | (s.AllIn ? 4 : 0) | (s.Acted ? 8 : 0) | (s.SittingOut ? 16 : 0) | (s.Leaving ? 32 : 0);
			w.Write((byte)flags);
			w.Write(s.LastAction);
			w.Write(s.Shown[0]);
			w.Write(s.Shown[1]);
		}
		w.Flush();
		return ms.ToArray();
	}

	public static PokerTable Read(byte[] data, int offset)
	{
		using MemoryStream ms = new MemoryStream(data, offset, data.Length - offset);
		using BinaryReader r = new BinaryReader(ms);
		PokerTable t = new PokerTable
		{
			Id = r.ReadByte(),
			Seq = r.ReadUInt16(),
			HandNo = r.ReadUInt16(),
			SmallBlind = r.ReadUInt16(),
			BigBlind = r.ReadUInt16(),
			Phase = (PokerPhase)r.ReadByte(),
			Button = r.ReadSByte(),
			ToAct = r.ReadSByte(),
			CurrentBet = r.ReadInt32(),
			MinRaise = r.ReadInt32(),
			Clock = r.ReadByte()
		};
		byte bits = r.ReadByte();
		t.QuietWin = (bits & 1) != 0;
		t.BoardCount = Math.Min(5, bits >> 1);
		t.WinningScore = r.ReadInt32();
		for (int i = 0; i < 5; i++)
		{
			t.Board[i] = r.ReadByte();
		}
		foreach (PokerSeat s in t.Seats)
		{
			s.Kind = (SeatKind)r.ReadByte();
			if (s.Empty)
			{
				continue;
			}
			s.Player = (s.Kind == SeatKind.Player) ? r.ReadByte() : -1;
			if (s.Kind != SeatKind.Player)
			{
				r.ReadByte();
			}
			s.Bot = r.ReadByte();
			s.Stack = r.ReadInt32();
			s.Bet = r.ReadInt32();
			s.Committed = r.ReadInt32();
			s.Won = r.ReadInt32();
			int flags = r.ReadByte();
			s.Dealt = (flags & 1) != 0;
			s.Folded = (flags & 2) != 0;
			s.AllIn = (flags & 4) != 0;
			s.Acted = (flags & 8) != 0;
			s.SittingOut = (flags & 16) != 0;
			s.Leaving = (flags & 32) != 0;
			s.LastAction = r.ReadByte();
			s.Shown = new[] { r.ReadByte(), r.ReadByte() };
		}
		return t;
	}

	#endregion
}
