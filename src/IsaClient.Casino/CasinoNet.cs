using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Core;
using IsaClient.Games;
using IsaClient.Net.Pipe;
using Terraria;
using Terraria.Audio;

namespace IsaClient.Casino;

public enum CasinoGame : byte
{
	Poker = 1,
	Blackjack = 2,
	Slots = 3,
	Roulette = 4,
	Horses = 5
}

/// <summary>A table someone opened, as its host announces it.</summary>
public sealed class TableAd
{
	public CasinoGame Game;

	public byte Id;

	public int Host;

	/// <summary>Poker: the big blind. Blackjack: the smallest bet.</summary>
	public int Stake;

	public int Seated;

	public int Max;

	public double Until;
}

/// <summary>
/// The casino's tables, alone or with other IsaClient players over the pipe (channel <see cref="Channel"/>). One
/// table at a time. Whoever opens a table runs it: deck, cards, turns, the computer players; the others send their
/// actions and get its state (Poker's hole cards go only to their owner). Each player's chips: what they take to the
/// table leaves their wallet, and every hand or round that ends counts as won or lost (<see cref="CasinoWallet"/>).
/// Messages: [kind, game, table id, ...].
/// </summary>
public static class CasinoNet
{
	public const byte Channel = 79;

	private const byte Ad = 1;

	private const byte Closed = 2;

	private const byte Join = 3;

	private const byte Welcome = 4;

	private const byte Refuse = 5;

	private const byte Leave = 6;

	private const byte State = 7;

	private const byte Private = 8;

	private const byte Action = 9;

	private const byte Rebuy = 10;

	/// <summary>Seconds between a host's announcements of its open table.</summary>
	private const double AdEvery = 6.0;

	private const double AdLasts = 16.0;

	/// <summary>A table whose host says nothing for this long is gone.</summary>
	private const double HostSilence = 60.0;

	public static readonly List<TableAd> Ads = new List<TableAd>();

	/// <summary>Tables already told in the chat (an announcement lost for a while doesn't tell it again).</summary>
	private static readonly HashSet<string> _announced = new HashSet<string>();

	public static CasinoGame Game;

	public static bool Hosting;

	/// <summary>The host's player (you when hosting or alone).</summary>
	public static int Host = -1;

	public static byte TableId;

	/// <summary>Open to the others (announced); else just you and the computer.</summary>
	public static bool Open;

	public static PokerTable Poker;

	public static BlackjackTable Blackjack;

	/// <summary>Your two cards and the hand they're for.</summary>
	public static readonly byte[] MyHole = { Cards.NoCard, Cards.NoCard };

	public static ushort MyHoleHand;

	public static bool AtTable => Poker != null || Blackjack != null;

	public static int MySeat => (Poker != null) ? Poker.SeatOf(Me) : ((Blackjack != null) ? Blackjack.SeatOf(Me) : -1);

	/// <summary>You at a table: your player, or 0 alone (seat ids are player numbers).</summary>
	public static int Me => Main.myPlayer;

	/// <summary>Raised on every new state (old, new): animations and sounds.</summary>
	public static event Action<PokerTable, PokerTable> PokerChanged;

	public static event Action<BlackjackTable, BlackjackTable> BlackjackChanged;

	/// <summary>Something to tell the player at the table ("Guide raises to 40").</summary>
	public static string Notice;

	public static double NoticeAt;

	private static bool _hooked;

	private static double _lastAd;

	private static double _lastHeard;

	private static double _lastSent = -1.0;

	private static ushort _sentSeq;

	/// <summary>The host's last state shown on its own screen.</summary>
	private static int _seenSeq = -1;

	private static double _joinedAt;

	private static ushort _sentHoles;

	private static PokerTable _pokerSeen;

	private static BlackjackTable _blackjackSeen;

	/// <summary>Your chips when the hand (or round) began, to count what it won or lost.</summary>
	private static int _stackAtStart = -1;

	private static int _startedHand = -1;

	private static readonly Random _random = new Random();

	private static double _botAt;

	private static string _botFor;

	private static double Now => GameArt.Now;

	private static bool PipeOk => Main.netMode == 1 && PipeNet.Enabled && PipeNet.Ready;

	#region Opening, joining, leaving

	/// <summary>Opens a Poker table: <paramref name="bots"/> computer players, your <paramref name="buyIn"/> chips, open to others or not.</summary>
	public static bool HostPoker(int bigBlind, int buyIn, int bots, bool open)
	{
		if (!Ready(buyIn))
		{
			return false;
		}
		Close(quiet: true);
		Game = CasinoGame.Poker;
		Hosting = true;
		Host = Me;
		Open = open && PipeOk;
		TableId = (byte)_random.Next(1, 256);
		Poker = new PokerTable { Id = TableId, BigBlind = bigBlind, SmallBlind = Math.Max(1, bigBlind / 2) };
		CasinoWallet.Take(buyIn);
		Poker.Sit(SeatKind.Player, Me, 0, buyIn, 0);
		int first = _random.Next(PokerBot.Names.Length);
		for (int i = 0; i < bots; i++)
		{
			Poker.Sit(SeatKind.Bot, -1, (first + i * 5) % PokerBot.Names.Length, bigBlind * 100);
		}
		AfterOpen();
		return true;
	}

	public static bool HostBlackjack(int minBet, int buyIn, bool open)
	{
		if (!Ready(buyIn))
		{
			return false;
		}
		Close(quiet: true);
		Game = CasinoGame.Blackjack;
		Hosting = true;
		Host = Me;
		Open = open && PipeOk;
		TableId = (byte)_random.Next(1, 256);
		Blackjack = new BlackjackTable { Id = TableId, MinBet = minBet };
		CasinoWallet.Take(buyIn);
		Blackjack.Sit(SeatKind.Player, Me, buyIn);
		Blackjack.Clock = BlackjackTable.BetSeconds;
		AfterOpen();
		return true;
	}

	private static bool Ready(int buyIn)
	{
		if (Main.gameMenu)
		{
			return false;
		}
		CasinoWallet.GiftIfBroke(buyIn);
		if (CasinoWallet.Free < buyIn)
		{
			ClientChat.Error(IsaClient.Languages.L10n.F("You have {0} chips: not enough for that.", CasinoWallet.Free.ToString("N0")));
			return false;
		}
		return true;
	}

	private static void AfterOpen()
	{
		_lastHeard = Now;
		_lastAd = -100.0;
		_sentSeq = 0;
		_seenSeq = -1;
		_sentHoles = 0;
		_stackAtStart = -1;
		_startedHand = -1;
		_pokerSeen = null;
		_blackjackSeen = null;
		MyHole[0] = MyHole[1] = Cards.NoCard;
		SoundEngine.PlaySound(10);
		Publish();
		if (Open)
		{
			ClientChat.Info(IsaClient.Languages.L10n.F("Your {0} table is open: every IsaClient player here can join it.", GameName(Game)));
		}
	}

	/// <summary>Sits at someone's open table with <paramref name="buyIn"/> chips.</summary>
	public static bool JoinTable(TableAd ad, int buyIn)
	{
		if (!PipeOk || !GameLobby.Online(ad.Host) || !Ready(buyIn))
		{
			return false;
		}
		Close(quiet: true);
		Game = ad.Game;
		Hosting = false;
		Host = ad.Host;
		Open = true;
		TableId = ad.Id;
		_lastHeard = Now;
		_stackAtStart = -1;
		_startedHand = -1;
		_pokerSeen = null;
		_blackjackSeen = null;
		MyHole[0] = MyHole[1] = Cards.NoCard;
		CasinoWallet.Take(buyIn);
		// Until the host answers, an empty table shows (it fills in when the state comes).
		if (Game == CasinoGame.Poker)
		{
			Poker = new PokerTable { Id = ad.Id, BigBlind = ad.Stake, SmallBlind = Math.Max(1, ad.Stake / 2) };
		}
		else
		{
			Blackjack = new BlackjackTable { Id = ad.Id, MinBet = ad.Stake };
		}
		_joinChips = buyIn;
		_joinedAt = Now;
		PipeNet.SendTo(Host, Channel, Message(Join, Game, ad.Id, BitConverter.GetBytes(buyIn)));
		SoundEngine.PlaySound(10);
		return true;
	}

	/// <summary>Chips you took to a table you're joining (given back if the host says no).</summary>
	private static int _joinChips;

	/// <summary>Gets up and leaves the table (what's lost in a hand you're in stays lost).</summary>
	public static void Close(bool quiet = false)
	{
		if (!AtTable)
		{
			return;
		}
		SettleLeaving();
		if (Hosting && Open && PipeOk)
		{
			PipeNet.Broadcast(Channel, Message(Closed, Game, TableId));
			foreach (int p in Members())
			{
				PipeNet.SendTo(p, Channel, Message(Closed, Game, TableId));
			}
		}
		else if (!Hosting && PipeOk && GameLobby.Online(Host))
		{
			PipeNet.SendTo(Host, Channel, Message(Leave, Game, TableId));
		}
		Poker = null;
		Blackjack = null;
		Hosting = false;
		Host = -1;
		CasinoWallet.Return();
		if (!quiet)
		{
			SoundEngine.PlaySound(11);
		}
	}

	/// <summary>Leaving in the middle of a hand: the chips already in it are lost.</summary>
	private static void SettleLeaving()
	{
		int seat = MySeat;
		if (seat < 0 || _stackAtStart < 0)
		{
			return;
		}
		if (Poker != null && Poker.Phase >= PokerPhase.Preflop && Poker.Phase <= PokerPhase.River && Poker.Seats[seat].Dealt && Poker.HandNo == _startedHand)
		{
			int now = Poker.Seats[seat].Stack;
			if (now != _stackAtStart)
			{
				CasinoWallet.Settle(poker: true, now - _stackAtStart, now, -1);
			}
		}
		else if (Blackjack != null && (Blackjack.Phase == BlackjackPhase.Playing || Blackjack.Phase == BlackjackPhase.Dealer) && Blackjack.Seats[seat].Hands.Count > 0 && Blackjack.RoundNo == _startedHand)
		{
			int now = Blackjack.Seats[seat].Stack;
			if (now != _stackAtStart)
			{
				CasinoWallet.Settle(poker: false, now - _stackAtStart, now, -1);
			}
		}
		_stackAtStart = -1;
	}

	/// <summary>More chips on the table (between hands; Poker after busting, Blackjack any time you bet).</summary>
	public static void AddChips(int chips)
	{
		int seat = MySeat;
		if (seat < 0 || chips <= 0)
		{
			return;
		}
		CasinoWallet.GiftIfBroke(chips);
		if (!CasinoWallet.Take(Math.Min(chips, CasinoWallet.Free)))
		{
			return;
		}
		chips = (int)Math.Min(chips, CasinoWallet.OnTable);
		if (Hosting)
		{
			if (Poker != null)
			{
				Poker.AddChips(seat, chips);
			}
			else
			{
				Blackjack?.AddChips(seat, chips);
			}
			Publish();
		}
		else
		{
			PipeNet.SendTo(Host, Channel, Message(Rebuy, Game, TableId, BitConverter.GetBytes(chips)));
		}
		SoundEngine.PlaySound(Terraria.ID.SoundID.CoinPickup);
	}

	/// <summary>Your action at the table (Poker: fold/check/call/raise to/all in; Blackjack: bet/hit/stand/double/split).</summary>
	public static void Act(byte action, int amount)
	{
		int seat = MySeat;
		if (seat < 0)
		{
			return;
		}
		if (Hosting)
		{
			bool ok = (Poker != null) ? Poker.Act(seat, action, amount) : (Blackjack != null && Blackjack.Act(seat, action, amount));
			if (ok)
			{
				Publish();
			}
			return;
		}
		ushort hand = (Poker != null) ? Poker.HandNo : (Blackjack?.RoundNo ?? 0);
		byte[] payload = new byte[7];
		BitConverter.GetBytes(hand).CopyTo(payload, 0);
		payload[2] = action;
		BitConverter.GetBytes(amount).CopyTo(payload, 3);
		PipeNet.SendTo(Host, Channel, Message(Action, Game, TableId, payload));
	}

	/// <summary>Host, Poker: one more computer player (or one less).</summary>
	public static void AddBot()
	{
		if (!Hosting || Poker == null)
		{
			return;
		}
		HashSet<int> used = new HashSet<int>(Poker.Seats.Where(s => s.Kind == SeatKind.Bot).Select(s => s.Bot));
		int bot = Enumerable.Range(0, PokerBot.Names.Length).FirstOrDefault(b => !used.Contains(b));
		if (Poker.Sit(SeatKind.Bot, -1, bot, Poker.BigBlind * 100) >= 0)
		{
			Publish();
		}
	}

	public static void RemoveBot()
	{
		if (!Hosting || Poker == null)
		{
			return;
		}
		int seat = Array.FindLastIndex(Poker.Seats, s => s.Kind == SeatKind.Bot && !s.Leaving);
		if (seat >= 0)
		{
			Poker.Stand(seat);
			Publish();
		}
	}

	public static string GameName(CasinoGame game) => IsaClient.Languages.L10n.T((game == CasinoGame.Poker) ? "Poker" : "Blackjack");

	/// <summary>A line for whoever is at the table, shown on the table itself.</summary>
	public static void Tell(string text) => Say(text);

	public static string SeatName(PokerSeat s) => (s.Kind == SeatKind.Bot) ? PokerBot.Name(s.Bot) : GameLobby.Name(s.Player);

	#endregion

	#region Every tick

	public static void Update()
	{
		if (!_hooked)
		{
			_hooked = true;
			PipeNet.OnMessage += OnMessage;
		}
		Ads.RemoveAll(a => Now > a.Until || !GameLobby.Online(a.Host));
		if (Main.gameMenu)
		{
			if (AtTable)
			{
				Close(quiet: true);
			}
			Ads.Clear();
			return;
		}
		if (!AtTable)
		{
			return;
		}
		if (Hosting)
		{
			HostTick();
		}
		else if (!GameLobby.Online(Host) || Now - _lastHeard > HostSilence)
		{
			Say(IsaClient.Languages.L10n.T("The table's host is gone: the table closed."));
			Close();
		}
	}

	private static double _lastTick = -1.0;

	private static void HostTick()
	{
		double now = Now;
		double dt = (_lastTick < 0.0) ? 0.0 : Math.Min(0.5, now - _lastTick);
		_lastTick = now;
		// Players who left the server get up.
		if (Poker != null)
		{
			for (int i = 0; i < PokerTable.MaxSeats; i++)
			{
				PokerSeat s = Poker.Seats[i];
				if (s.Kind == SeatKind.Player && s.Player != Me && !GameLobby.Online(s.Player) && !s.Leaving)
				{
					Poker.Stand(i);
				}
				// Computer players that ran out buy in again.
				if (s.Kind == SeatKind.Bot && s.Stack == 0 && Poker.Phase == PokerPhase.Waiting)
				{
					s.Stack = Poker.BigBlind * 100;
				}
			}
			Poker.Tick(dt, BotMove);
		}
		else if (Blackjack != null)
		{
			for (int i = 0; i < BlackjackTable.MaxSeats; i++)
			{
				BlackjackSeat s = Blackjack.Seats[i];
				if (s.Kind == SeatKind.Player && s.Player != Me && !GameLobby.Online(s.Player) && !s.Leaving)
				{
					Blackjack.Stand(i);
				}
			}
			Blackjack.Tick(dt);
		}
		if (Open && PipeOk && now - _lastAd > AdEvery)
		{
			_lastAd = now;
			int seated = (Poker != null) ? Poker.Seated : Blackjack.Seated;
			int max = (Poker != null) ? PokerTable.MaxSeats : BlackjackTable.MaxSeats;
			if (seated < max)
			{
				int stake = (Poker != null) ? Poker.BigBlind : Blackjack.MinBet;
				PipeNet.Broadcast(Channel, Message(Ad, Game, TableId, new[] { (byte)(stake & 255), (byte)(stake >> 8), (byte)seated, (byte)max }));
			}
		}
		Publish();
	}

	/// <summary>A computer player's move once it has "thought" a moment (it waits a little, like a person).</summary>
	private static (byte, int)? BotMove(int seat)
	{
		string key = Poker.HandNo + ":" + seat + ":" + Poker.BoardCount + ":" + Poker.CurrentBet;
		if (_botFor != key)
		{
			_botFor = key;
			_botAt = Now + 0.9 + _random.NextDouble() * 1.4;
			return null;
		}
		if (Now < _botAt)
		{
			return null;
		}
		_botFor = null;
		(byte action, int amount) move = PokerBot.Decide(Poker, seat, Poker.Hole(seat), _random);
		return move;
	}

	/// <summary>The host's new state: to its own screen and to everyone at the table (and each one's own cards).</summary>
	private static void Publish()
	{
		if (Poker != null && Hosting)
		{
			if (Poker.Seq != _seenSeq)
			{
				// Your own view gets a copy (what anyone else would see), with your own cards.
				_seenSeq = Poker.Seq;
				PokerTable copy = PokerTable.Read(Poker.Write(), 0);
				int mine = Poker.SeatOf(Me);
				if (mine >= 0 && Poker.Seats[mine].Dealt && Poker.Phase != PokerPhase.Waiting)
				{
					MyHole[0] = Poker.Hole(mine)[0];
					MyHole[1] = Poker.Hole(mine)[1];
					MyHoleHand = Poker.HandNo;
				}
				Seen(copy);
			}
			if (Poker.Seq != _sentSeq)
			{
				if (Open && PipeOk && (Now - _lastSent > 0.2 || Poker.Phase == PokerPhase.Showdown))
				{
					_lastSent = Now;
					_sentSeq = Poker.Seq;
					byte[] state = Message(State, Game, TableId, Poker.Write());
					foreach (int p in Members())
					{
						PipeNet.SendTo(p, Channel, state);
					}
					if (_sentHoles != Poker.HandNo && Poker.Phase >= PokerPhase.Preflop && Poker.Phase <= PokerPhase.River)
					{
						_sentHoles = Poker.HandNo;
						for (int i = 0; i < PokerTable.MaxSeats; i++)
						{
							PokerSeat s = Poker.Seats[i];
							if (s.Kind == SeatKind.Player && s.Player != Me && s.Dealt)
							{
								byte[] hole = Poker.Hole(i);
								PipeNet.SendTo(s.Player, Channel, Message(Private, Game, TableId, new[] { (byte)(Poker.HandNo & 255), (byte)(Poker.HandNo >> 8), hole[0], hole[1] }));
							}
						}
					}
				}
				else if (!Open || !PipeOk)
				{
					_sentSeq = Poker.Seq;
				}
			}
		}
		else if (Blackjack != null && Hosting && (Blackjack.Seq != _sentSeq || Blackjack.Seq != _seenSeq))
		{
			if (Blackjack.Seq != _seenSeq)
			{
				_seenSeq = Blackjack.Seq;
				Seen(BlackjackTable.Read(Blackjack.Write(), 0));
			}
			if (Blackjack.Seq == _sentSeq)
			{
				return;
			}
			if (Open && PipeOk && (Now - _lastSent > 0.2 || Blackjack.Phase == BlackjackPhase.Payout))
			{
				_lastSent = Now;
				_sentSeq = Blackjack.Seq;
				byte[] state = Message(State, Game, TableId, Blackjack.Write());
				foreach (int p in Members())
				{
					PipeNet.SendTo(p, Channel, state);
				}
			}
			else if (!Open || !PipeOk)
			{
				_sentSeq = Blackjack.Seq;
			}
		}
	}

	/// <summary>The other players sitting at your table.</summary>
	private static IEnumerable<int> Members()
	{
		if (Poker != null)
		{
			return Poker.Seats.Where(s => s.Kind == SeatKind.Player && s.Player != Me).Select(s => s.Player).ToList();
		}
		if (Blackjack != null)
		{
			return Blackjack.Seats.Where(s => s.Kind == SeatKind.Player && s.Player != Me).Select(s => s.Player).ToList();
		}
		return Enumerable.Empty<int>();
	}

	/// <summary>A new state seen (yours as host, or the host's): your chips are counted, the screen animates.</summary>
	private static void Seen(PokerTable now)
	{
		PokerTable before = _pokerSeen;
		_pokerSeen = now;
		int seat = now.SeatOf(Me);
		if (seat >= 0)
		{
			PokerSeat s = now.Seats[seat];
			// A new hand you're in: your chips before it.
			if (s.Dealt && now.HandNo != _startedHand && now.Phase >= PokerPhase.Preflop)
			{
				_startedHand = now.HandNo;
				_stackAtStart = s.Stack + s.Committed;
			}
			// It's over: what it won or lost.
			if (now.Phase == PokerPhase.Showdown && now.HandNo == _startedHand && _stackAtStart >= 0)
			{
				int delta = s.Stack - _stackAtStart;
				CasinoWallet.Settle(poker: true, delta, s.Stack, Math.Sign(delta));
				_stackAtStart = -1;
				if (s.Won > 0)
				{
					SoundEngine.PlaySound(Terraria.ID.SoundID.CoinPickup);
					if (delta > 0 && s.Won >= now.BigBlind * 20)
					{
						CasinoNews.Won(CasinoGame.Poker, s.Won);
					}
				}
			}
		}
		Describe(before, now);
		PokerChanged?.Invoke(before, now);
	}

	private static void Seen(BlackjackTable now)
	{
		BlackjackTable before = _blackjackSeen;
		_blackjackSeen = now;
		int seat = now.SeatOf(Me);
		if (seat >= 0)
		{
			BlackjackSeat s = now.Seats[seat];
			if (s.Hands.Count > 0 && now.RoundNo != _startedHand && now.Phase != BlackjackPhase.Betting)
			{
				_startedHand = now.RoundNo;
				_stackAtStart = s.Stack + s.Hands.Sum(h => h.Bet);
			}
			if (now.Phase == BlackjackPhase.Payout && now.RoundNo == _startedHand && _stackAtStart >= 0)
			{
				int delta = s.Stack - _stackAtStart;
				CasinoWallet.Settle(poker: false, delta, s.Stack, Math.Sign(delta));
				_stackAtStart = -1;
				if (delta > 0)
				{
					SoundEngine.PlaySound(Terraria.ID.SoundID.CoinPickup);
					CasinoNews.Won(CasinoGame.Blackjack, delta);
				}
			}
		}
		BlackjackChanged?.Invoke(before, now);
	}

	/// <summary>A line about what just happened at the Poker table.</summary>
	private static void Describe(PokerTable before, PokerTable now)
	{
		if (before == null)
		{
			return;
		}
		for (int i = 0; i < PokerTable.MaxSeats; i++)
		{
			PokerSeat a = before.Seats[i];
			PokerSeat b = now.Seats[i];
			if (b.Empty || a.Kind != b.Kind || b.LastAction == a.LastAction && b.Bet == a.Bet && b.Folded == a.Folded)
			{
				continue;
			}
			if (b.LastAction == PokerAction.SmallBlind || b.LastAction == PokerAction.BigBlind || b.LastAction == PokerAction.None)
			{
				continue;
			}
			string who = SeatName(b);
			string what = PokerAction.Name(b.LastAction);
			if (b.LastAction == PokerAction.Bet || b.LastAction == PokerAction.Raise || b.LastAction == PokerAction.Call || b.LastAction == PokerAction.AllIn)
			{
				what = IsaClient.Languages.L10n.T(what) + " " + b.Bet.ToString("N0");
			}
			else
			{
				what = IsaClient.Languages.L10n.T(what);
			}
			Say(who + ": " + what);
		}
		if (now.Phase == PokerPhase.Showdown && before.Phase != PokerPhase.Showdown)
		{
			List<PokerSeat> winners = now.Seats.Where(s => s.Won > 0).ToList();
			if (winners.Count > 0)
			{
				string names = string.Join(", ", winners.Select(SeatName));
				string hand = (now.QuietWin || now.WinningScore == 0) ? "" : (" (" + IsaClient.Languages.L10n.T(PokerHands.Name(now.WinningScore)) + ")");
				Say(IsaClient.Languages.L10n.F("{0} wins {1}", names, winners.Sum(w => w.Won).ToString("N0")) + hand);
			}
		}
	}

	private static void Say(string text)
	{
		Notice = text;
		NoticeAt = Now;
	}

	#endregion

	#region Messages

	private static byte[] Message(byte kind, CasinoGame game, byte table, byte[] payload = null)
	{
		byte[] data = new byte[3 + (payload?.Length ?? 0)];
		data[0] = kind;
		data[1] = (byte)game;
		data[2] = table;
		payload?.CopyTo(data, 3);
		return data;
	}

	private static void OnMessage(PipeInbound m)
	{
		if (m.Channel != Channel || m.Data == null || m.Data.Length < 3 || m.FromPlayer == Main.myPlayer || Main.gameMenu)
		{
			return;
		}
		try
		{
			Handle(m.FromPlayer, m.Data);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static void Handle(int from, byte[] d)
	{
		byte kind = d[0];
		CasinoGame game = (CasinoGame)d[1];
		byte table = d[2];
		if (game != CasinoGame.Poker && game != CasinoGame.Blackjack)
		{
			return;
		}
		bool mine = AtTable && Game == game && TableId == table;
		switch (kind)
		{
		case Ad:
			if (d.Length >= 7)
			{
				TableAd ad = Ads.FirstOrDefault(a => a.Host == from && a.Id == table);
				bool news = ad == null;
				if (news)
				{
					ad = new TableAd { Host = from, Id = table, Game = game };
					Ads.Add(ad);
				}
				ad.Stake = d[3] | (d[4] << 8);
				ad.Seated = d[5];
				ad.Max = d[6];
				ad.Until = Now + AdLasts;
				if (news && !(mine && !Hosting) && _announced.Add(from + ":" + table))
				{
					CasinoNews.TableOpened(from, game, table, (game == CasinoGame.Poker) ? ((ad.Stake / 2).ToString("N0") + "/" + ad.Stake.ToString("N0")) : (ad.Stake.ToString("N0") + "+"));
				}
			}
			break;
		case Closed:
			Ads.RemoveAll(a => a.Host == from && a.Id == table);
			if (mine && !Hosting && from == Host)
			{
				Say(IsaClient.Languages.L10n.T("The host closed the table."));
				ClientChat.Info(IsaClient.Languages.L10n.F("{0} closed the {1} table.", GameLobby.Name(from), GameName(game)));
				Close();
			}
			break;
		case Join:
			if (Hosting && mine && d.Length >= 7)
			{
				OnJoin(from, BitConverter.ToInt32(d, 3));
			}
			else
			{
				PipeNet.SendTo(from, Channel, Message(Refuse, game, table));
			}
			break;
		case Refuse:
			if (mine && !Hosting && from == Host)
			{
				ClientChat.Error(IsaClient.Languages.L10n.T("The table is full or closed."));
				Close();
			}
			break;
		case Welcome:
			if (mine && !Hosting && from == Host)
			{
				_lastHeard = Now;
				_joinChips = 0;
				ClientChat.Info(IsaClient.Languages.L10n.F("You sat at {0}'s {1} table.", GameLobby.Name(from), GameName(game)));
			}
			break;
		case Leave:
			if (Hosting && mine)
			{
				if (Poker != null)
				{
					int seat = Poker.SeatOf(from);
					if (seat >= 0)
					{
						Poker.Stand(seat);
					}
				}
				else if (Blackjack != null)
				{
					int seat = Blackjack.SeatOf(from);
					if (seat >= 0)
					{
						Blackjack.Stand(seat);
					}
				}
				Publish();
			}
			break;
		case State:
			if (mine && !Hosting && from == Host)
			{
				_lastHeard = Now;
				OnState(d);
			}
			break;
		case Private:
			if (mine && !Hosting && from == Host && d.Length >= 7)
			{
				MyHoleHand = (ushort)(d[3] | (d[4] << 8));
				MyHole[0] = d[5];
				MyHole[1] = d[6];
			}
			break;
		case Action:
			if (Hosting && mine && d.Length >= 10)
			{
				ushort hand = BitConverter.ToUInt16(d, 3);
				byte action = d[5];
				int amount = BitConverter.ToInt32(d, 6);
				if (Poker != null)
				{
					int seat = Poker.SeatOf(from);
					if (seat >= 0 && hand == Poker.HandNo)
					{
						Poker.Act(seat, action, amount);
					}
				}
				else if (Blackjack != null)
				{
					int seat = Blackjack.SeatOf(from);
					if (seat >= 0 && (hand == Blackjack.RoundNo || action == BlackjackAction.Bet))
					{
						Blackjack.Act(seat, action, amount);
					}
				}
				Publish();
			}
			break;
		case Rebuy:
			if (Hosting && mine && d.Length >= 7)
			{
				int chips = Math.Max(0, Math.Min(1000000, BitConverter.ToInt32(d, 3)));
				if (Poker != null)
				{
					int seat = Poker.SeatOf(from);
					if (seat >= 0 && (Poker.Phase == PokerPhase.Waiting || Poker.Phase == PokerPhase.Showdown || !Poker.Seats[seat].Dealt))
					{
						Poker.AddChips(seat, chips);
					}
				}
				else if (Blackjack != null)
				{
					int seat = Blackjack.SeatOf(from);
					if (seat >= 0)
					{
						Blackjack.AddChips(seat, chips);
					}
				}
				Publish();
			}
			break;
		}
	}

	private static void OnJoin(int from, int buyIn)
	{
		buyIn = Math.Max(1, Math.Min(1000000, buyIn));
		int seat;
		if (Poker != null)
		{
			seat = Poker.SeatOf(from);
			if (seat < 0)
			{
				// A computer player gives up its seat when the table is full of them.
				if (Poker.Seats.All(s => !s.Empty))
				{
					int bot = Array.FindLastIndex(Poker.Seats, s => s.Kind == SeatKind.Bot && !s.Dealt);
					if (bot >= 0)
					{
						Poker.Seats[bot].Clear();
					}
				}
				seat = Poker.Sit(SeatKind.Player, from, 0, buyIn);
			}
		}
		else
		{
			seat = Blackjack.SeatOf(from);
			if (seat < 0)
			{
				seat = Blackjack.Sit(SeatKind.Player, from, buyIn);
			}
		}
		if (seat < 0)
		{
			PipeNet.SendTo(from, Channel, Message(Refuse, Game, TableId));
			return;
		}
		PipeNet.SendTo(from, Channel, Message(Welcome, Game, TableId, new[] { (byte)seat }));
		Say(IsaClient.Languages.L10n.F("{0} sat down.", GameLobby.Name(from)));
		_sentSeq = 0;
		_lastSent = -1.0;
		Publish();
	}

	private static void OnState(byte[] d)
	{
		if (Game == CasinoGame.Poker)
		{
			PokerTable t = PokerTable.Read(d, 3);
			if (Poker != null && Poker.HandNo == t.HandNo && (short)(t.Seq - Poker.Seq) <= 0 && Poker.Seated > 0)
			{
				// An older state that came late.
				return;
			}
			Poker = t;
			if (t.SeatOf(Me) >= 0)
			{
				_joinChips = 0;
			}
			else if (_joinChips > 0 && Now - _joinedAt > 12.0)
			{
				// Not seated after a while: the chips come back.
				Close();
				return;
			}
			if (t.HandNo != MyHoleHand)
			{
				MyHole[0] = MyHole[1] = Cards.NoCard;
			}
			Seen(PokerTable.Read(d, 3));
		}
		else
		{
			BlackjackTable t = BlackjackTable.Read(d, 3);
			if (Blackjack != null && Blackjack.RoundNo == t.RoundNo && (short)(t.Seq - Blackjack.Seq) <= 0 && Blackjack.Seated > 0)
			{
				return;
			}
			Blackjack = t;
			Seen(BlackjackTable.Read(d, 3));
		}
	}

	#endregion
}
