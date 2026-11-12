using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Core;
using IsaClient.Games;
using IsaClient.Net.Pipe;
using Terraria;
using Terraria.Audio;

namespace IsaClient.Casino;

/// <summary>What happens in a shared game's round.</summary>
public enum BetsPhase : byte
{
	Betting,
	Running,
	Result
}

/// <summary>The Roulette or Horses table of a server, as whoever runs it announces it.</summary>
public sealed class BetsAd
{
	public CasinoGame Game;

	public byte Id;

	public int Host;

	public int Players;

	public double Until;
}

/// <summary>
/// The shared tables: one Roulette and one race track per server, over the pipe (channel <see cref="Channel"/>). Whoever
/// gets there first runs the round's clock and throws the ball or starts the race, and everyone who comes later joins
/// that same table; when that player leaves the next one takes over and the table goes on. Each player's bets are their
/// own: chips leave the wallet when they go on the table, and every client works out what it won from the result the
/// table sends. With nobody else (or no server) the round waits for you. Messages: [kind, game, table id, ...].
/// </summary>
public static class BetsNet
{
	public const byte Channel = 80;

	private const byte Ad = 1;

	private const byte Closed = 2;

	private const byte Join = 3;

	private const byte Leave = 4;

	private const byte State = 5;

	private const byte Move = 6;

	private const byte MyBets = 7;

	private const double AdEvery = 5.0;

	private const double AdLasts = 14.0;

	/// <summary>A table that says nothing for this long moves to the next player.</summary>
	private const double HostSilence = 15.0;

	private const double Heartbeat = 6.0;

	public static readonly List<BetsAd> Ads = new List<BetsAd>();

	public static CasinoGame Game;

	public static bool AtTable;

	public static bool Hosting;

	public static int Host = -1;

	public static byte TableId;

	/// <summary>Everyone else at the table, in the order they would take it over.</summary>
	public static readonly List<int> Members = new List<int>();

	public static ushort Round;

	public static BetsPhase Phase;

	/// <summary>When the phase ends (<see cref="double.MaxValue"/>: the round waits for you).</summary>
	public static double Ends = double.MaxValue;

	/// <summary>When the ball was thrown or the race started.</summary>
	public static double RunStart;

	/// <summary>The race's field.</summary>
	public static int Seed;

	/// <summary>The number the ball falls in, or the seed of how the race ends.</summary>
	public static int Outcome;

	/// <summary>Roulette: the last numbers. Horses: the last winners.</summary>
	public static readonly List<int> History = new List<int>();

	public static HorseRace Race;

	/// <summary>Your bets on the table: a number mask (Roulette) or a horse's key, and the chips.</summary>
	public static readonly Dictionary<ulong, int> Mine = new Dictionary<ulong, int>();

	/// <summary>What you bet last round, to repeat it.</summary>
	public static readonly Dictionary<ulong, int> Last = new Dictionary<ulong, int>();

	/// <summary>The others' bets on the table.</summary>
	public static readonly Dictionary<int, Dictionary<ulong, int>> Others = new Dictionary<int, Dictionary<ulong, int>>();

	/// <summary>What the round paid you (with what you bet) and what you had bet: shown until the next round.</summary>
	public static long LastPayout;

	public static long LastStake;

	/// <summary>Bets just paid, for the screen.</summary>
	public static event Action RoundPaid;

	private static readonly List<(ulong Key, int Amount)> _undo = new List<(ulong, int)>();

	private static bool _hooked;

	private static double _lastAd = -100.0;

	private static double _lastState = -100.0;

	private static double _lastHeard;

	private static double _lastBets = -100.0;

	private static bool _betsChanged;

	private static bool _paid = true;

	private static double Now => GameArt.Now;

	private static bool PipeOk => Main.netMode == 1 && PipeNet.Enabled && PipeNet.Ready;

	public static int Me => Main.myPlayer;

	public static double BettingSeconds => (Game == CasinoGame.Horses) ? 40.0 : 30.0;

	public static double RunSeconds => (Game == CasinoGame.Horses) ? HorseRace.Length + 1.0 : 10.0;

	public static double ResultSeconds => (Game == CasinoGame.Horses) ? 8.0 : 6.0;

	public static bool Alone => Members.Count == 0;

	public static long Staked => Mine.Values.Sum(v => (long)v);

	public static bool CanBet => AtTable && Phase == BetsPhase.Betting && (Hosting || _joined);

	/// <summary>The table answered (you're still waiting to hear from it otherwise).</summary>
	private static bool _joined;

	private static double _joinedAt;

	public static bool Waiting => AtTable && !Hosting && !_joined;

	public static double SecondsLeft => (Ends == double.MaxValue) ? -1.0 : Math.Max(0.0, Ends - Now);

	#region Sitting down

	/// <summary>Goes to this server's table of the game: the one that's there, or a new one with you running it.</summary>
	public static void Enter(CasinoGame game)
	{
		if (AtTable && Game == game)
		{
			return;
		}
		Exit();
		Game = game;
		AtTable = true;
		Mine.Clear();
		Last.Clear();
		Others.Clear();
		History.Clear();
		_undo.Clear();
		_paid = true;
		LastPayout = 0;
		LastStake = 0;
		BetsAd ad = PipeOk ? Ads.FirstOrDefault(a => a.Game == game && GameLobby.Online(a.Host)) : null;
		if (ad != null)
		{
			JoinAd(ad);
		}
		else
		{
			Hosting = true;
			Host = Me;
			TableId = (byte)BetsDice.Next(255);
			TableId++;
			Members.Clear();
			Round = (ushort)BetsDice.Next(1000);
			NewRound();
			_lastAd = -100.0;
		}
		SoundEngine.PlaySound(10);
	}

	private static void JoinAd(BetsAd ad)
	{
		Hosting = false;
		Host = ad.Host;
		TableId = ad.Id;
		Members.Clear();
		_joined = false;
		_joinedAt = Now;
		_lastHeard = Now;
		Phase = BetsPhase.Betting;
		Ends = double.MaxValue;
		PipeNet.SendTo(Host, Channel, Message(Join, Game, TableId));
	}

	/// <summary>Gets up: bets not played yet come back, and the table goes on with the others.</summary>
	public static void Exit()
	{
		if (!AtTable)
		{
			return;
		}
		if (Phase == BetsPhase.Betting)
		{
			RefundAll();
		}
		else
		{
			Pay();
		}
		if (PipeOk)
		{
			if (Hosting)
			{
				int next = NextHost();
				if (next >= 0)
				{
					foreach (int p in Members)
					{
						PipeNet.SendTo(p, Channel, Message(Move, Game, TableId, new[] { (byte)next }));
					}
				}
				else
				{
					PipeNet.Broadcast(Channel, Message(Closed, Game, TableId));
				}
			}
			else if (GameLobby.Online(Host))
			{
				PipeNet.SendTo(Host, Channel, Message(Leave, Game, TableId));
			}
		}
		AtTable = false;
		Hosting = false;
		Host = -1;
		Members.Clear();
		Others.Clear();
		Race = null;
		SoundEngine.PlaySound(11);
	}

	#endregion

	#region Your bets

	public static bool Place(ulong key, int amount)
	{
		if (!CanBet || amount <= 0)
		{
			return false;
		}
		CasinoWallet.GiftIfBroke(BetsLimits.Least);
		amount = (int)Math.Min(amount, CasinoWallet.Free);
		if (amount <= 0 || !CasinoWallet.ChargeBet(amount))
		{
			return false;
		}
		Mine.TryGetValue(key, out int had);
		Mine[key] = had + amount;
		_undo.Add((key, amount));
		_betsChanged = true;
		SoundEngine.PlaySound(Terraria.ID.SoundID.CoinPickup);
		return true;
	}

	/// <summary>Takes back the last chips you put.</summary>
	public static void Undo()
	{
		if (!CanBet || _undo.Count == 0)
		{
			return;
		}
		(ulong key, int amount) = _undo[_undo.Count - 1];
		_undo.RemoveAt(_undo.Count - 1);
		if (Mine.TryGetValue(key, out int had))
		{
			int back = Math.Min(had, amount);
			CasinoWallet.RefundBet(back);
			if (had - back > 0)
			{
				Mine[key] = had - back;
			}
			else
			{
				Mine.Remove(key);
			}
			_betsChanged = true;
		}
	}

	/// <summary>All the chips on one bet back (right click).</summary>
	public static void Remove(ulong key)
	{
		if (CanBet && Mine.TryGetValue(key, out int had))
		{
			CasinoWallet.RefundBet(had);
			Mine.Remove(key);
			_undo.RemoveAll(u => u.Key == key);
			_betsChanged = true;
		}
	}

	public static void ClearBets()
	{
		if (CanBet)
		{
			RefundAll();
		}
	}

	/// <summary>The same bets as last round.</summary>
	public static void Repeat()
	{
		if (CanBet)
		{
			foreach (KeyValuePair<ulong, int> bet in Last.ToArray())
			{
				Place(bet.Key, bet.Value);
			}
		}
	}

	private static void RefundAll()
	{
		foreach (int amount in Mine.Values)
		{
			CasinoWallet.RefundBet(amount);
		}
		Mine.Clear();
		_undo.Clear();
		_betsChanged = true;
	}

	/// <summary>Alone at the table: the ball goes or the race starts now.</summary>
	public static void Go()
	{
		if (Hosting && Phase == BetsPhase.Betting)
		{
			StartRun();
		}
	}

	#endregion

	#region The round

	private static void NewRound()
	{
		Round++;
		Phase = BetsPhase.Betting;
		Ends = Alone ? double.MaxValue : Now + BettingSeconds;
		Seed = BetsDice.Seed();
		Outcome = 0;
		OnNewRound();
		SendState();
	}

	private static void OnNewRound()
	{
		Race = (Game == CasinoGame.Horses) ? new HorseRace(Seed) : null;
		Others.Clear();
		_undo.Clear();
		_paid = true;
	}

	private static void StartRun()
	{
		Phase = BetsPhase.Running;
		Ends = Now + RunSeconds;
		RunStart = Now;
		Outcome = (Game == CasinoGame.Roulette) ? Roulette.Spin() : BetsDice.Seed();
		OnRun();
		SendState();
	}

	private static void OnRun()
	{
		_paid = false;
		_undo.Clear();
		Race?.Run(Outcome);
		SoundEngine.PlaySound(Terraria.ID.SoundID.Grab);
	}

	private static void ToResult()
	{
		Phase = BetsPhase.Result;
		Ends = Now + ResultSeconds;
		Pay();
	}

	/// <summary>The round is over: what your bets gave back goes to your wallet.</summary>
	private static void Pay()
	{
		if (_paid)
		{
			return;
		}
		_paid = true;
		History.Insert(0, (Game == CasinoGame.Roulette) ? Outcome : (Race?.Order?[0] ?? 0));
		if (History.Count > 14)
		{
			History.RemoveAt(History.Count - 1);
		}
		long stake = Staked;
		long payout = 0;
		long best = 0;
		foreach (KeyValuePair<ulong, int> bet in Mine)
		{
			long back = (Game == CasinoGame.Roulette) ? Roulette.Payout(bet.Key, bet.Value, Outcome) : (Race?.Payout(bet.Key, bet.Value) ?? 0);
			payout += back;
			best = Math.Max(best, back);
		}
		LastStake = stake;
		LastPayout = payout;
		if (stake > 0)
		{
			Last.Clear();
			foreach (KeyValuePair<ulong, int> bet in Mine)
			{
				Last[bet.Key] = bet.Value;
			}
			CasinoWallet.BetResult(stake, payout);
			if (payout > stake)
			{
				SoundEngine.PlaySound(Terraria.ID.SoundID.CoinPickup);
				int detail = (Game == CasinoGame.Roulette) ? Outcome : (Race?.Order?[0] ?? 0);
				CasinoNews.Won(Game, payout - stake, detail);
			}
		}
		Mine.Clear();
		_betsChanged = true;
		RoundPaid?.Invoke();
	}

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
			Exit();
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
		else
		{
			MemberTick();
		}
		if (_betsChanged && PipeOk && Now - _lastBets > 1.0)
		{
			_betsChanged = false;
			_lastBets = Now;
			PipeNet.Broadcast(Channel, Message(MyBets, Game, TableId, PackBets()));
		}
	}

	private static void HostTick()
	{
		if (Members.RemoveAll(p => !GameLobby.Online(p)) > 0)
		{
			SendState();
		}
		if (Phase == BetsPhase.Betting && Alone)
		{
			Ends = double.MaxValue;
		}
		else if (Phase == BetsPhase.Betting && Ends == double.MaxValue)
		{
			Ends = Now + BettingSeconds;
			SendState();
		}
		if (Now >= Ends)
		{
			switch (Phase)
			{
			case BetsPhase.Betting:
				StartRun();
				break;
			case BetsPhase.Running:
				ToResult();
				SendState();
				break;
			default:
				NewRound();
				break;
			}
		}
		if (PipeOk && Now - _lastAd > AdEvery)
		{
			_lastAd = Now;
			PipeNet.Broadcast(Channel, Message(Ad, Game, TableId, new[] { (byte)Math.Min(255, Members.Count + 1) }));
		}
		if (PipeOk && !Alone && Now - _lastState > Heartbeat)
		{
			SendState();
		}
	}

	private static void MemberTick()
	{
		// The result comes from your own clock: the ball stops here when it stops here.
		if (Phase == BetsPhase.Running && Now >= Ends)
		{
			ToResult();
		}
		if (!_joined && Now - _joinedAt > 6.0)
		{
			// Nobody answered: the table is yours.
			TakeOver();
			return;
		}
		if (!GameLobby.Online(Host) || Now - _lastHeard > HostSilence || !PipeOk)
		{
			int next = PipeOk ? NextHost() : -1;
			if (next < 0 || next == Me)
			{
				TakeOver();
			}
			else
			{
				Members.Remove(next);
				Host = next;
				_lastHeard = Now;
			}
		}
	}

	/// <summary>Who takes the table next: the first one still here, or -1.</summary>
	private static int NextHost()
	{
		foreach (int p in Members)
		{
			if (p == Me || GameLobby.Online(p))
			{
				return p;
			}
		}
		return -1;
	}

	private static void TakeOver()
	{
		Pay();
		Hosting = true;
		Host = Me;
		_joined = true;
		Members.Remove(Me);
		_lastAd = -100.0;
		if (Mine.Count > 0 && Phase == BetsPhase.Betting)
		{
			// The chips on the table stay for this new round.
			Dictionary<ulong, int> keep = new Dictionary<ulong, int>(Mine);
			NewRound();
			foreach (KeyValuePair<ulong, int> bet in keep)
			{
				Mine[bet.Key] = bet.Value;
			}
		}
		else
		{
			NewRound();
		}
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

	private static byte[] PackBets()
	{
		List<byte> data = new List<byte> { (byte)(Round & 255), (byte)(Round >> 8) };
		List<KeyValuePair<ulong, int>> bets = Mine.OrderByDescending(b => b.Value).Take(12).ToList();
		data.Add((byte)bets.Count);
		foreach (KeyValuePair<ulong, int> bet in bets)
		{
			data.AddRange(BitConverter.GetBytes(bet.Key));
			data.AddRange(BitConverter.GetBytes(bet.Value));
		}
		return data.ToArray();
	}

	private static void SendState()
	{
		_lastState = Now;
		if (!PipeOk || Alone)
		{
			return;
		}
		double left = (Ends == double.MaxValue) ? 6000.0 : Math.Max(0.0, Ends - Now);
		List<byte> data = new List<byte> { (byte)(Round & 255), (byte)(Round >> 8), (byte)Phase };
		data.AddRange(BitConverter.GetBytes((ushort)Math.Min(60000.0, left * 10.0)));
		data.AddRange(BitConverter.GetBytes(Seed));
		data.AddRange(BitConverter.GetBytes(Outcome));
		data.Add((byte)Members.Count);
		data.AddRange(Members.Select(p => (byte)p));
		byte[] message = Message(State, Game, TableId, data.ToArray());
		foreach (int p in Members)
		{
			PipeNet.SendTo(p, Channel, message);
		}
	}

	private static void OnMessage(PipeInbound m)
	{
		if (m.Channel != Channel || m.Data == null || m.Data.Length < 3 || m.FromPlayer == Me || Main.gameMenu)
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
		if (game != CasinoGame.Roulette && game != CasinoGame.Horses)
		{
			return;
		}
		bool mine = AtTable && Game == game && TableId == table;
		switch (kind)
		{
		case Ad:
			OnAd(from, game, table, d);
			break;
		case Closed:
			Ads.RemoveAll(a => a.Host == from && a.Id == table);
			break;
		case Join:
			if (Hosting && mine)
			{
				if (!Members.Contains(from))
				{
					Members.Add(from);
					CasinoNet.Tell(IsaClient.Languages.L10n.F("{0} came to the table.", GameLobby.Name(from)));
				}
				SendState();
			}
			break;
		case Leave:
			if (Hosting && mine && Members.Remove(from))
			{
				Others.Remove(from);
				SendState();
			}
			break;
		case State:
			if (mine && !Hosting && d.Length >= 16)
			{
				OnState(from, d);
			}
			break;
		case Move:
			if (mine && !Hosting && from == Host && d.Length >= 4)
			{
				int next = d[3];
				Members.Remove(next);
				Host = next;
				_lastHeard = Now;
				if (next == Me)
				{
					TakeOver();
				}
			}
			break;
		case MyBets:
			if (mine && d.Length >= 6)
			{
				ushort round = BitConverter.ToUInt16(d, 3);
				if (round == Round)
				{
					Dictionary<ulong, int> bets = new Dictionary<ulong, int>();
					int count = d[5];
					for (int i = 0; i < count && 6 + i * 12 + 12 <= d.Length; i++)
					{
						bets[BitConverter.ToUInt64(d, 6 + i * 12)] = BitConverter.ToInt32(d, 14 + i * 12);
					}
					Others[from] = bets;
				}
			}
			break;
		}
	}

	private static void OnAd(int from, CasinoGame game, byte table, byte[] d)
	{
		BetsAd ad = Ads.FirstOrDefault(a => a.Host == from && a.Game == game);
		bool news = ad == null && !Ads.Any(a => a.Game == game && a.Id == table);
		if (ad == null)
		{
			ad = new BetsAd { Host = from, Game = game };
			Ads.Add(ad);
		}
		ad.Id = table;
		ad.Players = (d.Length > 3) ? d[3] : 1;
		ad.Until = Now + AdLasts;
		if (AtTable && Game == game)
		{
			if (TableId == table)
			{
				_lastHeard = Now;
			}
			else if (Hosting && from < Me)
			{
				// Two tables started at once: one stays, the lower player's. Yours moves over with its people.
				foreach (int p in Members)
				{
					PipeNet.SendTo(p, Channel, Message(Move, Game, TableId, new[] { (byte)from }));
				}
				if (Phase == BetsPhase.Betting)
				{
					RefundAll();
				}
				else
				{
					Pay();
				}
				JoinAd(ad);
			}
			return;
		}
		if (news)
		{
			CasinoNews.TableOpened(from, game, table, "");
		}
	}

	private static void OnState(int from, byte[] d)
	{
		_lastHeard = Now;
		_joined = true;
		if (from != Host)
		{
			Members.Remove(from);
			Host = from;
		}
		ushort round = BitConverter.ToUInt16(d, 3);
		BetsPhase phase = (BetsPhase)d[5];
		double left = BitConverter.ToUInt16(d, 6) / 10.0;
		int seed = BitConverter.ToInt32(d, 8);
		int outcome = BitConverter.ToInt32(d, 12);
		Members.Clear();
		int count = d[16];
		for (int i = 0; i < count && 17 + i < d.Length; i++)
		{
			Members.Add(d[17 + i]);
		}
		bool newRound = round != Round || Race == null && Game == CasinoGame.Horses;
		if (newRound)
		{
			Pay();
			Round = round;
			Seed = seed;
			OnNewRound();
			Phase = BetsPhase.Betting;
		}
		BetsPhase was = Phase;
		Outcome = outcome;
		if (phase != BetsPhase.Betting && was == BetsPhase.Betting)
		{
			RunStart = Now - Math.Max(0.0, ((phase == BetsPhase.Running) ? RunSeconds - left : RunSeconds));
			OnRun();
		}
		// Your own clock ended the race a moment ago: it stays in the result.
		if (!(phase == BetsPhase.Running && was == BetsPhase.Result))
		{
			Phase = phase;
			Ends = (left >= 6000.0) ? double.MaxValue : Now + left;
		}
		if (Phase == BetsPhase.Result)
		{
			Pay();
		}
	}

	#endregion
}

/// <summary>How much goes on the shared tables.</summary>
public static class BetsLimits
{
	/// <summary>The smallest chip; with less than this the house gives you some.</summary>
	public const int Least = 10;

	public static readonly int[] Chips = { 10, 50, 100, 500, 1000 };
}
