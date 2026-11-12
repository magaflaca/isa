using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IsaClient.Casino;

public enum BlackjackPhase : byte
{
	/// <summary>Everyone puts their bet; the cards come when all are ready (or the time is up).</summary>
	Betting,
	Playing,
	Dealer,
	/// <summary>The results; the next round's betting comes in a moment.</summary>
	Payout
}

public static class BlackjackAction
{
	public const byte Bet = 1;

	public const byte Hit = 2;

	public const byte Stand = 3;

	public const byte Double = 4;

	public const byte Split = 5;
}

/// <summary>One hand of a player (two after a split).</summary>
public sealed class BlackjackHand
{
	public List<byte> Cards = new List<byte>();

	public int Bet;

	public bool Doubled;

	public bool Done;

	/// <summary>Came from a split (a 21 then is not a blackjack).</summary>
	public bool FromSplit;

	/// <summary>0 not settled, 1 lost, 2 push, 3 won, 4 blackjack.</summary>
	public byte Result;

	public int Value => BlackjackHands.Value(Cards);

	public bool Natural => !FromSplit && BlackjackHands.Natural(Cards);

	public bool Bust => Value > 21;
}

public sealed class BlackjackSeat
{
	public SeatKind Kind;

	public int Player = -1;

	public int Stack;

	/// <summary>The bet for the next round (while betting).</summary>
	public int Bet;

	public bool Ready;

	public bool Leaving;

	public List<BlackjackHand> Hands = new List<BlackjackHand>();

	/// <summary>The bet of the last round (to bet the same again).</summary>
	public int LastBet;

	public bool Empty => Kind == SeatKind.Empty;

	public void Clear()
	{
		Kind = SeatKind.Empty;
		Player = -1;
		Stack = Bet = LastBet = 0;
		Ready = Leaving = false;
		Hands.Clear();
	}
}

/// <summary>
/// Blackjack against the house (the dealer), alone or with friends at the same table, each playing their own hands.
/// Six decks, the dealer stands on all 17s and peeks for blackjack, blackjack pays 3:2, double on any two cards, one
/// split (split aces take one card each). The host runs it; everyone gets the state (the dealer's hidden card stays
/// hidden until the dealer plays).
/// </summary>
public sealed class BlackjackTable
{
	public const int MaxSeats = 4;

	public const int Decks = 6;

	public const double BetSeconds = 15.0;

	public const double TurnSeconds = 20.0;

	public const double PayoutSeconds = 4.5;

	public byte Id;

	public int MinBet = 10;

	public int MaxBet => MinBet * 50;

	public readonly BlackjackSeat[] Seats = new BlackjackSeat[MaxSeats];

	public BlackjackPhase Phase;

	public int ToActSeat = -1;

	public int ToActHand;

	public readonly List<byte> Dealer = new List<byte>();

	/// <summary>The dealer's second card is shown.</summary>
	public bool HoleShown;

	public ushort RoundNo;

	public ushort Seq;

	public double Clock;

	private List<byte> _shoe = new List<byte>();

	private int _shoeSize;

	public BlackjackTable()
	{
		for (int i = 0; i < MaxSeats; i++)
		{
			Seats[i] = new BlackjackSeat();
		}
	}

	public int SeatOf(int player) => Array.FindIndex(Seats, s => s.Kind == SeatKind.Player && s.Player == player);

	public int Seated => Seats.Count(s => !s.Empty);

	private void Changed()
	{
		Seq++;
	}

	#region Seats

	public int Sit(SeatKind kind, int player, int stack)
	{
		int seat = Array.FindIndex(Seats, s => s.Empty);
		if (seat < 0)
		{
			return -1;
		}
		Seats[seat].Clear();
		Seats[seat].Kind = kind;
		Seats[seat].Player = player;
		Seats[seat].Stack = stack;
		Changed();
		return seat;
	}

	/// <summary>Gets up: at once when not in the round, else their hands stand and they go at the end of it.</summary>
	public void Stand(int seat)
	{
		BlackjackSeat s = Seats[seat];
		if (s.Empty)
		{
			return;
		}
		if (Phase == BlackjackPhase.Betting || s.Hands.Count == 0)
		{
			s.Clear();
		}
		else
		{
			s.Leaving = true;
			foreach (BlackjackHand h in s.Hands)
			{
				h.Done = true;
			}
			if (ToActSeat == seat)
			{
				NextTurn();
			}
		}
		Changed();
	}

	public void AddChips(int seat, int chips)
	{
		if (!Seats[seat].Empty && chips > 0)
		{
			Seats[seat].Stack += chips;
			Changed();
		}
	}

	#endregion

	#region Running it (host)

	public void Tick(double seconds)
	{
		Clock = Math.Max(0.0, Clock - seconds);
		switch (Phase)
		{
		case BlackjackPhase.Betting:
		{
			List<BlackjackSeat> seated = Seats.Where(s => !s.Empty && !s.Leaving).ToList();
			bool anyBet = seated.Any(s => s.Ready && s.Bet > 0);
			if (anyBet && (seated.All(s => s.Ready) || Clock <= 0.0))
			{
				Deal();
			}
			else if (!anyBet)
			{
				// The clock only runs once someone has bet.
				Clock = BetSeconds;
			}
			break;
		}
		case BlackjackPhase.Playing:
			if (Clock <= 0.0 && ToActSeat >= 0)
			{
				Act(ToActSeat, BlackjackAction.Stand, 0);
			}
			break;
		case BlackjackPhase.Dealer:
			if (Clock <= 0.0)
			{
				DealerStep();
			}
			break;
		case BlackjackPhase.Payout:
			if (Clock <= 0.0)
			{
				NewRound();
			}
			break;
		}
	}

	/// <summary>An action: Bet (amount), Hit, Stand, Double, Split. False when it isn't allowed now.</summary>
	public bool Act(int seat, byte action, int amount)
	{
		if (seat < 0 || seat >= MaxSeats || Seats[seat].Empty)
		{
			return false;
		}
		BlackjackSeat s = Seats[seat];
		if (action == BlackjackAction.Bet)
		{
			if (Phase != BlackjackPhase.Betting || amount < 0)
			{
				return false;
			}
			if (amount == 0)
			{
				// Sits this round out.
				s.Bet = 0;
				s.Ready = true;
			}
			else
			{
				if (amount < MinBet || amount > MaxBet || amount > s.Stack)
				{
					return false;
				}
				s.Bet = amount;
				s.Ready = true;
			}
			Changed();
			return true;
		}
		if (Phase != BlackjackPhase.Playing || seat != ToActSeat || ToActHand >= s.Hands.Count)
		{
			return false;
		}
		BlackjackHand h = s.Hands[ToActHand];
		switch (action)
		{
		case BlackjackAction.Hit:
			h.Cards.Add(Draw());
			if (h.Value >= 21)
			{
				h.Done = true;
			}
			break;
		case BlackjackAction.Stand:
			h.Done = true;
			break;
		case BlackjackAction.Double:
			if (h.Cards.Count != 2 || s.Stack < h.Bet)
			{
				return false;
			}
			s.Stack -= h.Bet;
			h.Bet *= 2;
			h.Doubled = true;
			h.Cards.Add(Draw());
			h.Done = true;
			break;
		case BlackjackAction.Split:
			if (!CanSplit(seat))
			{
				return false;
			}
			s.Stack -= h.Bet;
			BlackjackHand second = new BlackjackHand { Bet = h.Bet, FromSplit = true };
			second.Cards.Add(h.Cards[1]);
			h.Cards.RemoveAt(1);
			h.FromSplit = true;
			h.Cards.Add(Draw());
			second.Cards.Add(Draw());
			s.Hands.Add(second);
			if (Cards.Rank(h.Cards[0]) == Cards.Ace)
			{
				// Split aces: one card each.
				h.Done = true;
				second.Done = true;
			}
			else
			{
				h.Done = h.Value >= 21;
				second.Done = second.Value >= 21;
			}
			break;
		default:
			return false;
		}
		if (h.Done || ToActHand >= s.Hands.Count || s.Hands[ToActHand].Done)
		{
			NextTurn();
		}
		else
		{
			Clock = TurnSeconds;
		}
		Changed();
		return true;
	}

	public bool CanSplit(int seat)
	{
		BlackjackSeat s = Seats[seat];
		if (Phase != BlackjackPhase.Playing || seat != ToActSeat || s.Hands.Count != 1 || ToActHand != 0)
		{
			return false;
		}
		BlackjackHand h = s.Hands[0];
		return h.Cards.Count == 2 && BlackjackHands.CardValue(h.Cards[0]) == BlackjackHands.CardValue(h.Cards[1]) && s.Stack >= h.Bet;
	}

	public bool CanDouble(int seat)
	{
		BlackjackSeat s = Seats[seat];
		return Phase == BlackjackPhase.Playing && seat == ToActSeat && ToActHand < s.Hands.Count && s.Hands[ToActHand].Cards.Count == 2 && s.Stack >= s.Hands[ToActHand].Bet;
	}

	private byte Draw()
	{
		if (_shoe.Count == 0)
		{
			_shoe = Cards.Shuffled(Decks);
			_shoeSize = _shoe.Count;
		}
		byte c = _shoe[_shoe.Count - 1];
		_shoe.RemoveAt(_shoe.Count - 1);
		return c;
	}

	private void Deal()
	{
		// A fresh shoe when three quarters are gone.
		if (_shoe.Count < _shoeSize / 4 || _shoe.Count < 30)
		{
			_shoe = Cards.Shuffled(Decks);
			_shoeSize = _shoe.Count;
		}
		RoundNo++;
		Dealer.Clear();
		HoleShown = false;
		List<BlackjackSeat> playing = new List<BlackjackSeat>();
		foreach (BlackjackSeat s in Seats)
		{
			s.Hands.Clear();
			if (!s.Empty && s.Ready && s.Bet > 0 && s.Bet <= s.Stack && !s.Leaving)
			{
				s.Stack -= s.Bet;
				s.Hands.Add(new BlackjackHand { Bet = s.Bet });
				s.LastBet = s.Bet;
				playing.Add(s);
			}
			s.Bet = 0;
			s.Ready = false;
		}
		if (playing.Count == 0)
		{
			Phase = BlackjackPhase.Betting;
			Clock = BetSeconds;
			Changed();
			return;
		}
		for (int round = 0; round < 2; round++)
		{
			foreach (BlackjackSeat s in playing)
			{
				s.Hands[0].Cards.Add(Draw());
			}
			Dealer.Add(Draw());
		}
		foreach (BlackjackSeat s in playing)
		{
			if (s.Hands[0].Natural)
			{
				s.Hands[0].Done = true;
			}
		}
		Phase = BlackjackPhase.Playing;
		// The dealer peeks with an ace or a ten up: a blackjack ends the round at once.
		if (BlackjackHands.Natural(Dealer))
		{
			HoleShown = true;
			Settle();
			Changed();
			return;
		}
		ToActSeat = -1;
		ToActHand = 0;
		NextTurn();
		Changed();
	}

	/// <summary>The next hand that still plays, else the dealer's turn.</summary>
	private void NextTurn()
	{
		for (int seat = Math.Max(0, ToActSeat); seat < MaxSeats; seat++)
		{
			BlackjackSeat s = Seats[seat];
			int start = (seat == ToActSeat) ? ToActHand : 0;
			for (int h = start; h < s.Hands.Count; h++)
			{
				if (!s.Hands[h].Done)
				{
					ToActSeat = seat;
					ToActHand = h;
					Clock = TurnSeconds;
					return;
				}
			}
		}
		ToActSeat = -1;
		ToActHand = 0;
		Phase = BlackjackPhase.Dealer;
		HoleShown = true;
		// Everyone busted: the dealer doesn't need to draw.
		Clock = 0.9;
	}

	/// <summary>The dealer draws one card at a time (a moment between), standing on 17.</summary>
	private void DealerStep()
	{
		bool anyStanding = Seats.Any(s => s.Hands.Any(h => !h.Bust && !h.Natural));
		if (anyStanding && BlackjackHands.Value(Dealer) < 17)
		{
			Dealer.Add(Draw());
			Clock = 0.8;
			Changed();
			return;
		}
		Settle();
		Changed();
	}

	private void Settle()
	{
		int dealer = BlackjackHands.Value(Dealer);
		bool dealerNatural = BlackjackHands.Natural(Dealer);
		bool dealerBust = dealer > 21;
		foreach (BlackjackSeat s in Seats)
		{
			foreach (BlackjackHand h in s.Hands)
			{
				int value = h.Value;
				if (h.Bust)
				{
					h.Result = 1;
				}
				else if (h.Natural && !dealerNatural)
				{
					h.Result = 4;
					s.Stack += h.Bet + h.Bet * 3 / 2;
				}
				else if (dealerNatural)
				{
					h.Result = h.Natural ? (byte)2 : (byte)1;
					if (h.Natural)
					{
						s.Stack += h.Bet;
					}
				}
				else if (dealerBust || value > dealer)
				{
					h.Result = 3;
					s.Stack += h.Bet * 2;
				}
				else if (value == dealer)
				{
					h.Result = 2;
					s.Stack += h.Bet;
				}
				else
				{
					h.Result = 1;
				}
				h.Done = true;
			}
		}
		Phase = BlackjackPhase.Payout;
		ToActSeat = -1;
		Clock = PayoutSeconds;
	}

	private void NewRound()
	{
		foreach (BlackjackSeat s in Seats)
		{
			if (s.Leaving)
			{
				s.Clear();
				continue;
			}
			s.Hands.Clear();
			s.Bet = 0;
			s.Ready = false;
		}
		Dealer.Clear();
		HoleShown = false;
		Phase = BlackjackPhase.Betting;
		Clock = BetSeconds;
		Changed();
	}

	#endregion

	#region Sending it

	public byte[] Write()
	{
		using MemoryStream ms = new MemoryStream();
		using BinaryWriter w = new BinaryWriter(ms);
		w.Write(Id);
		w.Write(Seq);
		w.Write(RoundNo);
		w.Write((ushort)MinBet);
		w.Write((byte)Phase);
		w.Write((sbyte)ToActSeat);
		w.Write((byte)ToActHand);
		w.Write((byte)Math.Min(255, Math.Ceiling(Clock)));
		w.Write((byte)(HoleShown ? 1 : 0));
		w.Write((byte)Dealer.Count);
		for (int i = 0; i < Dealer.Count; i++)
		{
			w.Write((i == 1 && !HoleShown) ? Cards.NoCard : Dealer[i]);
		}
		foreach (BlackjackSeat s in Seats)
		{
			w.Write((byte)s.Kind);
			if (s.Empty)
			{
				continue;
			}
			w.Write((byte)Math.Max(0, s.Player));
			w.Write(s.Stack);
			w.Write(s.Bet);
			w.Write(s.LastBet);
			w.Write((byte)((s.Ready ? 1 : 0) | (s.Leaving ? 2 : 0)));
			w.Write((byte)s.Hands.Count);
			foreach (BlackjackHand h in s.Hands)
			{
				w.Write(h.Bet);
				w.Write((byte)((h.Doubled ? 1 : 0) | (h.Done ? 2 : 0) | (h.FromSplit ? 4 : 0)));
				w.Write(h.Result);
				w.Write((byte)h.Cards.Count);
				foreach (byte c in h.Cards)
				{
					w.Write(c);
				}
			}
		}
		w.Flush();
		return ms.ToArray();
	}

	public static BlackjackTable Read(byte[] data, int offset)
	{
		using MemoryStream ms = new MemoryStream(data, offset, data.Length - offset);
		using BinaryReader r = new BinaryReader(ms);
		BlackjackTable t = new BlackjackTable
		{
			Id = r.ReadByte(),
			Seq = r.ReadUInt16(),
			RoundNo = r.ReadUInt16(),
			MinBet = r.ReadUInt16(),
			Phase = (BlackjackPhase)r.ReadByte(),
			ToActSeat = r.ReadSByte(),
			ToActHand = r.ReadByte(),
			Clock = r.ReadByte(),
			HoleShown = r.ReadByte() != 0
		};
		int dealer = r.ReadByte();
		for (int i = 0; i < dealer; i++)
		{
			t.Dealer.Add(r.ReadByte());
		}
		foreach (BlackjackSeat s in t.Seats)
		{
			s.Kind = (SeatKind)r.ReadByte();
			if (s.Empty)
			{
				continue;
			}
			s.Player = r.ReadByte();
			s.Stack = r.ReadInt32();
			s.Bet = r.ReadInt32();
			s.LastBet = r.ReadInt32();
			int flags = r.ReadByte();
			s.Ready = (flags & 1) != 0;
			s.Leaving = (flags & 2) != 0;
			int hands = r.ReadByte();
			for (int h = 0; h < hands; h++)
			{
				BlackjackHand hand = new BlackjackHand { Bet = r.ReadInt32() };
				int hf = r.ReadByte();
				hand.Doubled = (hf & 1) != 0;
				hand.Done = (hf & 2) != 0;
				hand.FromSplit = (hf & 4) != 0;
				hand.Result = r.ReadByte();
				int n = r.ReadByte();
				for (int c = 0; c < n; c++)
				{
					hand.Cards.Add(r.ReadByte());
				}
				s.Hands.Add(hand);
			}
		}
		return t;
	}

	#endregion
}
