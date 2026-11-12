using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IsaClient.Core;
using IsaClient.Identity;
using IsaClient.Net.Pipe;
using Newtonsoft.Json.Linq;
using Terraria;
using Terraria.GameContent;

namespace IsaClient.Games;

/// <summary>A player's best score in a game.</summary>
public sealed class ScoreEntry
{
	/// <summary>Their <see cref="ClientIdentity"/> id.</summary>
	public ulong Id;

	/// <summary>The character that made it.</summary>
	public string Name = "";

	public int Score;

	/// <summary>When (unix seconds).</summary>
	public long When;

	/// <summary>When this copy got here or changed (unix seconds): what a meeting already sent is not sent again.</summary>
	public long Updated;

	/// <summary>Games against players (Chess, Tic Tac Toe).</summary>
	public int Wins;

	public int Losses;

	public int Draws;

	/// <summary>Made here from the other boards (the global one), not a real entry.</summary>
	public bool Computed;

	public int Games => Wins + Losses + Draws;

	/// <summary>How their head looked (hair, colors, helmet), to draw it when they're not here; null = unknown.</summary>
	public PlayerLook Look;
}

/// <summary>Enough of a character to draw its head: hair, skin, colors and what it wears on the head.</summary>
public sealed class PlayerLook
{
	public int Hair;

	public int Variant;

	public Microsoft.Xna.Framework.Color HairColor;

	public Microsoft.Xna.Framework.Color SkinColor;

	public Microsoft.Xna.Framework.Color EyeColor;

	/// <summary>Vanity helmet, else the helmet (0 none).</summary>
	public int Head;

	private Player _dummy;

	public const int Size = 14;

	public static PlayerLook Of(Player player)
	{
		if (player == null)
		{
			return null;
		}
		int head = (!player.armor[10].IsAir) ? player.armor[10].type : ((!player.armor[0].IsAir) ? player.armor[0].type : 0);
		return new PlayerLook
		{
			Hair = player.hair,
			Variant = player.skinVariant,
			HairColor = player.hairColor,
			SkinColor = player.skinColor,
			EyeColor = player.eyeColor,
			Head = head
		};
	}

	public bool Same(PlayerLook other)
	{
		return other != null && other.Hair == Hair && other.Variant == Variant && other.HairColor == HairColor && other.SkinColor == SkinColor && other.EyeColor == EyeColor && other.Head == Head;
	}

	/// <summary>A stand-in character wearing this look (made once).</summary>
	public Player Dummy()
	{
		if (_dummy == null)
		{
			Player p = new Player();
			p.hair = Math.Max(0, Math.Min(Hair, TextureAssets.PlayerHair.Length - 1));
			p.skinVariant = Math.Max(0, Math.Min(Variant, Terraria.ID.PlayerVariantID.Count - 1));
			p.hairColor = HairColor;
			p.skinColor = SkinColor;
			p.eyeColor = EyeColor;
			if (Head > 0 && Head < Terraria.ID.ItemID.Count)
			{
				p.armor[0].SetDefaults(Head);
			}
			p.PlayerFrame();
			_dummy = p;
		}
		return _dummy;
	}

	public void Write(List<byte> data)
	{
		data.AddRange(BitConverter.GetBytes((ushort)Hair));
		data.Add((byte)Variant);
		foreach (Microsoft.Xna.Framework.Color c in new[] { HairColor, SkinColor, EyeColor })
		{
			data.Add(c.R);
			data.Add(c.G);
			data.Add(c.B);
		}
		data.AddRange(BitConverter.GetBytes((ushort)Head));
	}

	public static PlayerLook Read(byte[] data, int at)
	{
		return new PlayerLook
		{
			Hair = BitConverter.ToUInt16(data, at),
			Variant = data[at + 2],
			HairColor = new Microsoft.Xna.Framework.Color(data[at + 3], data[at + 4], data[at + 5]),
			SkinColor = new Microsoft.Xna.Framework.Color(data[at + 6], data[at + 7], data[at + 8]),
			EyeColor = new Microsoft.Xna.Framework.Color(data[at + 9], data[at + 10], data[at + 11]),
			Head = BitConverter.ToUInt16(data, at + 12)
		};
	}

	public JObject ToJson()
	{
		return new JObject
		{
			["Hair"] = Hair,
			["Variant"] = Variant,
			["HairColor"] = HairColor.PackedValue,
			["SkinColor"] = SkinColor.PackedValue,
			["EyeColor"] = EyeColor.PackedValue,
			["Head"] = Head
		};
	}

	public static PlayerLook FromJson(JToken json)
	{
		if (!(json is JObject))
		{
			return null;
		}
		return new PlayerLook
		{
			Hair = (int?)json["Hair"] ?? 0,
			Variant = (int?)json["Variant"] ?? 0,
			HairColor = new Microsoft.Xna.Framework.Color { PackedValue = (uint?)json["HairColor"] ?? 0xFF375AD7u },
			SkinColor = new Microsoft.Xna.Framework.Color { PackedValue = (uint?)json["SkinColor"] ?? 0xFF5A7DFFu },
			EyeColor = new Microsoft.Xna.Framework.Color { PackedValue = (uint?)json["EyeColor"] ?? 0xFF4B5A69u },
			Head = (int?)json["Head"] ?? 0
		};
	}
}

/// <summary>
/// Best scores of everyone, with no server of ours: each IsaClient keeps the board it knows (IsaClient/scores.json)
/// and, when it meets another one over the pipe (channel <see cref="Channel"/>), they sync like a shared database: the
/// first time each sends the whole board it knows (the best <see cref="FirstSync"/> of each game), later only what
/// arrived or changed since their last meeting. So everyone's board fills with everyone they, and the people they met,
/// ever met, from server to server. One entry per player id (<see cref="ClientIdentity"/>), so
/// switching characters or sharing a name doesn't matter. A new best of yours goes to everyone here at once.
/// Messages: Entries [game, count, (id 8, score 4, when 4, name length 1, name)...].
/// </summary>
public static class Scores
{
	/// <summary>The pipe channel of scores ('S').</summary>
	public const byte Channel = 74;

	/// <summary>Score games (not the pipe game ids).</summary>
	public const byte Tetris = 1;

	/// <summary>Rank points from games against players: 3 a win, 1 a draw (none against the computer).</summary>
	public const byte TicTacToe = 2;

	/// <summary>Elo rating from games against players (everyone starts at <see cref="StartElo"/>).</summary>
	public const byte Chess = 3;

	/// <summary>Rank points from Tetris Versus against players: 3 a win.</summary>
	public const byte TetrisVersus = 4;

	/// <summary>Your best Snake score.</summary>
	public const byte Snake = 5;

	/// <summary>Poker: chips won or lost, on top of <see cref="CasinoBase"/>.</summary>
	public const byte Poker = 6;

	/// <summary>Blackjack: chips won or lost, on top of <see cref="CasinoBase"/>.</summary>
	public const byte Blackjack = 7;

	/// <summary>Slot machine, Roulette and races: chips won or lost, on top of <see cref="CasinoBase"/>.</summary>
	public const byte Bets = 8;

	/// <summary>The casino boards count from here (a board keeps only scores above 0): score = this + chips won.</summary>
	public const int CasinoBase = 100000;

	/// <summary>Everything together (made here, never sent): see <see cref="GlobalParts"/>.</summary>
	public const byte Global = 0;

	public const int StartElo = 1200;

	private const int EloK = 32;

	/// <summary>The boards in the order the panel shows them.</summary>
	public static readonly byte[] Boards = { Global, Tetris, TetrisVersus, Snake, Chess, TicTacToe, Poker, Blackjack, Bets };

	private const byte Entries = 1;

	/// <summary>Entries with each head's look after the name.</summary>
	private const byte EntriesWithLook = 2;

	/// <summary>Entries with the look, then wins, losses and draws.</summary>
	private const byte EntriesFull = 3;

	/// <summary>How many of each board a first meeting sends (the pipe is slow; later meetings send only news).</summary>
	private const int FirstSync = 60;

	/// <summary>Per id of IsaClient met: the newest <see cref="ScoreEntry.Updated"/> already sent to it.</summary>
	private static readonly Dictionary<ulong, long> _synced = new Dictionary<ulong, long>();

	private static long UnixNow => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

	private const int PerMessage = 4;

	private const int Kept = 500;

	private static readonly Dictionary<byte, Dictionary<ulong, ScoreEntry>> _books = new Dictionary<byte, Dictionary<ulong, ScoreEntry>>();

	/// <summary>Ranks the last time you looked at each board (for the arrows).</summary>
	private static readonly Dictionary<byte, Dictionary<ulong, int>> _seen = new Dictionary<byte, Dictionary<ulong, int>>();

	/// <summary>The arrows of the board on screen: rank change since your previous look.</summary>
	private static readonly Dictionary<byte, Dictionary<ulong, int>> _moves = new Dictionary<byte, Dictionary<ulong, int>>();

	private static readonly Dictionary<byte, double> _lastLook = new Dictionary<byte, double>();

	private static bool _loaded;

	private static bool _hooked;

	private static double _saveAt = -1.0;

	/// <summary>The board changed (to redraw or recount).</summary>
	public static int Version { get; private set; }

	private static string FilePath => Path.Combine(ClientSettings.IsaClientFolder, "scores.json");

	public static string GameName(byte game)
	{
		switch (game)
		{
		case Global:
			return IsaClient.Languages.L10n.T("Global");
		case Tetris:
			return "Tetris";
		case TicTacToe:
			return IsaClient.Languages.L10n.T("Tic Tac Toe");
		case Chess:
			return IsaClient.Languages.L10n.T("Chess");
		case TetrisVersus:
			return IsaClient.Languages.L10n.T("Tetris Versus");
		case Snake:
			return "Snake";
		case Poker:
			return IsaClient.Languages.L10n.T("Poker");
		case Blackjack:
			return IsaClient.Languages.L10n.T("Blackjack");
		case Bets:
			return IsaClient.Languages.L10n.T("Bets");
		default:
			return "#" + game;
		}
	}

	/// <summary>Tetris and Snake keep your best; the others keep the latest (a rating goes down too).</summary>
	private static bool KeepsBest(byte game)
	{
		return game == Tetris || game == Snake;
	}

	/// <summary>Every tick: hooks the pipe once, saves a little after changes.</summary>
	public static void Update()
	{
		Load();
		if (!_hooked)
		{
			_hooked = true;
			PipeNet.OnMessage += OnMessage;
			ClientIdentity.PeerSeen += OnPeer;
			ClientIdentity.Rotated += MoveMine;
		}
		if (_saveAt >= 0.0 && GameArt.Now > _saveAt)
		{
			_saveAt = -1.0;
			Save();
		}
	}

	#region The board

	/// <summary>Best first; equal scores: who made it first.</summary>
	public static List<ScoreEntry> Ranking(byte game)
	{
		Load();
		if (game == Global)
		{
			return GlobalRanking();
		}
		if (!_books.TryGetValue(game, out Dictionary<ulong, ScoreEntry> book))
		{
			return new List<ScoreEntry>();
		}
		return book.Values.OrderByDescending(e => e.Score).ThenBy(e => e.When).ThenBy(e => e.Id).ToList();
	}

	/// <summary>1-based rank of that id, 0 when it has no score.</summary>
	public static int RankOf(byte game, ulong id)
	{
		List<ScoreEntry> ranking = Ranking(game);
		int i = ranking.FindIndex(e => e.Id == id);
		return i + 1;
	}

	public static ScoreEntry Mine(byte game)
	{
		Load();
		if (game == Global)
		{
			return GlobalRanking().FirstOrDefault(e => e.Id == ClientIdentity.Id);
		}
		return (_books.TryGetValue(game, out Dictionary<ulong, ScoreEntry> book) && book.TryGetValue(ClientIdentity.Id, out ScoreEntry e)) ? e : null;
	}

	public static int Best(byte game)
	{
		return Mine(game)?.Score ?? 0;
	}

	/// <summary>
	/// The character to draw for an entry: you, them if they're here (their look is kept for later), else a stand-in
	/// with the last look we know; null when we never saw it.
	/// </summary>
	public static Player HeadOf(ScoreEntry entry)
	{
		if (entry.Id == ClientIdentity.Id && !Main.gameMenu)
		{
			return Main.LocalPlayer;
		}
		int here = ClientIdentity.PlayerWith(entry.Id);
		if (here >= 0)
		{
			PlayerLook look = PlayerLook.Of(Main.player[here]);
			if (!entry.Computed && !look.Same(entry.Look))
			{
				entry.Look = look;
				Changed();
			}
			return Main.player[here];
		}
		return entry.Look?.Dummy();
	}

	/// <summary>
	/// A board is being looked at: the arrows show how ranks moved since the previous look (a look ends after a few
	/// seconds away). Returns id → places climbed (negative: dropped).
	/// </summary>
	public static Dictionary<ulong, int> Moves(byte game)
	{
		Load();
		double now = GameArt.Now;
		bool newLook = !_lastLook.TryGetValue(game, out double last) || now - last > 3.0;
		_lastLook[game] = now;
		if (newLook || !_moves.ContainsKey(game))
		{
			Dictionary<ulong, int> before = _seen.TryGetValue(game, out Dictionary<ulong, int> s) ? s : new Dictionary<ulong, int>();
			Dictionary<ulong, int> moves = new Dictionary<ulong, int>();
			Dictionary<ulong, int> now_ = new Dictionary<ulong, int>();
			List<ScoreEntry> ranking = Ranking(game);
			for (int i = 0; i < ranking.Count; i++)
			{
				now_[ranking[i].Id] = i + 1;
				// New on the board: no arrow (they get a "new" mark).
				moves[ranking[i].Id] = before.TryGetValue(ranking[i].Id, out int old) ? (old - (i + 1)) : int.MaxValue;
			}
			if (before.Count == 0)
			{
				moves.Clear();
			}
			_moves[game] = moves;
			_seen[game] = now_;
			_saveAt = GameArt.Now + 2.0;
		}
		return _moves[game];
	}

	/// <summary>Your id changed (.isaid new): your scores go with you.</summary>
	public static void MoveMine(ulong from, ulong to)
	{
		Load();
		foreach (Dictionary<ulong, ScoreEntry> book in _books.Values)
		{
			if (book.TryGetValue(from, out ScoreEntry e))
			{
				book.Remove(from);
				e.Id = to;
				book[to] = e;
			}
		}
		Changed();
	}

	/// <summary>A finished game of yours. True when it's your new best (then everyone here hears of it).</summary>
	public static bool Submit(byte game, int score)
	{
		Load();
		if (score <= 0)
		{
			return false;
		}
		ScoreEntry mine = Mine(game);
		if (mine != null && mine.Score >= score)
		{
			return false;
		}
		int before = RankOf(game, ClientIdentity.Id);
		ScoreEntry entry = new ScoreEntry
		{
			Id = ClientIdentity.Id,
			Name = Main.LocalPlayer?.name ?? "",
			Score = score,
			When = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
			Updated = UnixNow,
			Look = PlayerLook.Of(Main.LocalPlayer)
		};
		Book(game)[entry.Id] = entry;
		Changed();
		int after = RankOf(game, entry.Id);
		int total = Ranking(game).Count;
		if (after == 1 && total > 1)
		{
			Notice(IsaClient.Languages.L10n.F("New best in {0}: {1}! You're #1 of {2}.", GameName(game), score.ToString("N0"), total));
		}
		else if (before == 0 || after < before)
		{
			Notice(IsaClient.Languages.L10n.F("New best in {0}: {1}! You're #{2} of {3}.", GameName(game), score.ToString("N0"), after, total));
		}
		if (Main.netMode == 1 && PipeNet.Enabled && PipeNet.Ready)
		{
			PipeNet.Broadcast(Channel, Pack(game, new List<ScoreEntry> { entry }));
		}
		return true;
	}

	private static Dictionary<ulong, ScoreEntry> Book(byte game)
	{
		if (!_books.TryGetValue(game, out Dictionary<ulong, ScoreEntry> book))
		{
			book = new Dictionary<ulong, ScoreEntry>();
			_books[game] = book;
		}
		return book;
	}

	/// <summary>Keeps the better of what we had and what came. True when the board changed.</summary>
	private static bool Merge(byte game, ScoreEntry entry)
	{
		if (entry.Id == 0 || entry.Score <= 0 || entry.Id == ClientIdentity.Id)
		{
			// Your own score is only what you played (a stale copy from someone else never replaces it).
			return false;
		}
		Dictionary<ulong, ScoreEntry> book = Book(game);
		// Tetris: the better score wins; ratings and points: the newer one.
		book.TryGetValue(entry.Id, out ScoreEntry old);
		bool keepOld = old != null && (KeepsBest(game) ? (old.Score > entry.Score || (old.Score == entry.Score && old.When >= entry.When)) : (old.When >= entry.When));
		if (keepOld)
		{
			// Same score: still take a look we didn't have.
			if (old.Look == null && entry.Look != null)
			{
				old.Look = entry.Look;
				old.Updated = UnixNow;
				return true;
			}
			return false;
		}
		if (entry.Look == null && old != null)
		{
			entry.Look = old.Look;
		}
		entry.Updated = UnixNow;
		book[entry.Id] = entry;
		if (book.Count > Kept)
		{
			foreach (ScoreEntry low in book.Values.OrderBy(e => e.Score).Take(book.Count - Kept).ToList())
			{
				book.Remove(low.Id);
			}
		}
		return true;
	}

	#region Matches and the global board

	/// <summary>
	/// A finished game against a player (never the computer): Chess moves your rating (Elo, against theirs as we
	/// know it), Tic Tac Toe adds rank points. <paramref name="result"/>: 1 won, 0.5 draw, 0 lost. Your entry changes
	/// here and everyone here hears of it; theirs changes on their side the same way.
	/// </summary>
	public static void RecordMatch(byte game, ulong opponent, double result)
	{
		Load();
		if (game != Chess && game != TicTacToe && game != TetrisVersus)
		{
			return;
		}
		ScoreEntry mine = Mine(game);
		ScoreEntry entry = new ScoreEntry
		{
			Id = ClientIdentity.Id,
			Name = Main.LocalPlayer?.name ?? "",
			Score = mine?.Score ?? ((game == Chess) ? StartElo : 0),
			Wins = mine?.Wins ?? 0,
			Losses = mine?.Losses ?? 0,
			Draws = mine?.Draws ?? 0,
			Look = PlayerLook.Of(Main.LocalPlayer)
		};
		int before = entry.Score;
		if (game == Chess)
		{
			int theirs = (opponent != 0 && Book(Chess).TryGetValue(opponent, out ScoreEntry them)) ? them.Score : StartElo;
			double expected = 1.0 / (1.0 + Math.Pow(10.0, (theirs - before) / 400.0));
			entry.Score = Math.Max(100, (int)Math.Round(before + EloK * (result - expected)));
		}
		else
		{
			entry.Score += (result >= 1.0) ? 3 : ((result > 0.0) ? 1 : 0);
		}
		if (result >= 1.0)
		{
			entry.Wins++;
		}
		else if (result > 0.0)
		{
			entry.Draws++;
		}
		else
		{
			entry.Losses++;
		}
		entry.When = UnixNow;
		entry.Updated = UnixNow;
		Book(game)[entry.Id] = entry;
		Changed();
		int change = entry.Score - before;
		string delta = ((change >= 0) ? "+" : "") + change;
		if (game == Chess)
		{
			Notice(IsaClient.Languages.L10n.F("Chess rating: {0} ({1}). You're #{2} of {3}.", entry.Score, delta, RankOf(game, entry.Id), Ranking(game).Count));
		}
		else if (change > 0)
		{
			Notice(IsaClient.Languages.L10n.F("{0}: {1} rank points ({2}). You're #{3} of {4}.", GameName(game), entry.Score, delta, RankOf(game, entry.Id), Ranking(game).Count));
		}
		if (Main.netMode == 1 && PipeNet.Enabled && PipeNet.Ready)
		{
			PipeNet.Broadcast(Channel, Pack(game, new List<ScoreEntry> { entry }));
		}
	}

	/// <summary>Weights of the global board: multiplayer counts more than Tetris, and Chess the most.</summary>
	public const double TetrisWeight = 0.01;

	public const double TicTacToeWeight = 4.0;

	public const double ChessRatingWeight = 1.5;

	public const int ChessGamePoints = 3;

	public const double VersusWeight = 5.0;

	public const double SnakeWeight = 0.2;

	/// <summary>Chips won (or lost) at the casino count this much on the global board.</summary>
	public const double CasinoWeight = 0.1;

	/// <summary>One player's share of the global score from Poker and Blackjack (negative when they lost chips).</summary>
	public static int CasinoPart(ulong id)
	{
		long chips = 0;
		if (Book(Poker).TryGetValue(id, out ScoreEntry p))
		{
			chips += p.Score - CasinoBase;
		}
		if (Book(Blackjack).TryGetValue(id, out ScoreEntry b))
		{
			chips += b.Score - CasinoBase;
		}
		if (Book(Bets).TryGetValue(id, out ScoreEntry s))
		{
			chips += s.Score - CasinoBase;
		}
		return (int)(chips * CasinoWeight);
	}

	/// <summary>
	/// Your Poker or Blackjack record: <paramref name="chips"/> won in all (negative: lost) and the hands won, lost and
	/// pushed. Everyone here hears of it.
	/// </summary>
	public static void RecordCasino(byte game, long chips, int wins, int losses, int draws)
	{
		Load();
		if (game != Poker && game != Blackjack && game != Bets)
		{
			return;
		}
		ScoreEntry entry = new ScoreEntry
		{
			Id = ClientIdentity.Id,
			Name = Main.LocalPlayer?.name ?? "",
			Score = (int)Math.Max(1L, Math.Min(int.MaxValue, CasinoBase + chips)),
			Wins = wins,
			Losses = losses,
			Draws = draws,
			When = UnixNow,
			Updated = UnixNow,
			Look = PlayerLook.Of(Main.LocalPlayer)
		};
		Book(game)[entry.Id] = entry;
		Changed();
		if (Main.netMode == 1 && PipeNet.Enabled && PipeNet.Ready)
		{
			PipeNet.Broadcast(Channel, Pack(game, new List<ScoreEntry> { entry }));
		}
	}

	/// <summary>One player's share of the global score from each board.</summary>
	public static (int Tetris, int Versus, int Chess, int TicTacToe, int Snake) GlobalParts(ulong id)
	{
		int snake = Book(Snake).TryGetValue(id, out ScoreEntry s) ? (int)(s.Score * SnakeWeight) : 0;
		int versus = Book(TetrisVersus).TryGetValue(id, out ScoreEntry v) ? (int)(v.Score * VersusWeight) : 0;
		int tetris = Book(Tetris).TryGetValue(id, out ScoreEntry t) ? (int)(t.Score * TetrisWeight) : 0;
		int chess = 0;
		if (Book(Chess).TryGetValue(id, out ScoreEntry c) && c.Games > 0)
		{
			chess = Math.Max(0, (int)((c.Score - 1000) * ChessRatingWeight) + c.Games * ChessGamePoints);
		}
		int ttt = Book(TicTacToe).TryGetValue(id, out ScoreEntry x) ? (int)(x.Score * TicTacToeWeight) : 0;
		return (tetris, versus, chess, ttt, snake);
	}

	private static List<ScoreEntry> _global;

	private static int _globalVersion = -1;

	/// <summary>Everyone on any board, by the sum of their parts (kept until a board changes).</summary>
	private static List<ScoreEntry> GlobalRanking()
	{
		if (_global != null && _globalVersion == Version)
		{
			return _global;
		}
		Dictionary<ulong, ScoreEntry> all = new Dictionary<ulong, ScoreEntry>();
		foreach (byte game in new[] { Tetris, TetrisVersus, Chess, TicTacToe, Snake, Poker, Blackjack })
		{
			foreach (ScoreEntry e in Book(game).Values)
			{
				// The newest name and look of each player.
				if (!all.TryGetValue(e.Id, out ScoreEntry g) || e.When > g.When)
				{
					all[e.Id] = new ScoreEntry { Id = e.Id, Name = e.Name, When = e.When, Look = e.Look ?? g?.Look, Computed = true };
				}
			}
		}
		foreach (ScoreEntry g in all.Values)
		{
			(int tetris, int versus, int chess, int ttt, int snake) = GlobalParts(g.Id);
			g.Score = tetris + versus + chess + ttt + snake + CasinoPart(g.Id);
		}
		_global = all.Values.Where(g => g.Score > 0).OrderByDescending(g => g.Score).ThenBy(g => g.When).ThenBy(g => g.Id).ToList();
		_globalVersion = Version;
		return _global;
	}

	#endregion

	private static void Changed()
	{
		Version++;
		_saveAt = GameArt.Now + 2.0;
	}

	#endregion

	#region Pipe

	/// <summary>Someone with IsaClient is here: they get the top of every board we know.</summary>
	private static void OnPeer(int player, ulong id)
	{
		Load();
		bool first = !_synced.TryGetValue(id, out long since);
		long newest = since;
		foreach (KeyValuePair<byte, Dictionary<ulong, ScoreEntry>> pair in _books)
		{
			// What they don't have from us yet (all of it the first time), best first; never their own entry.
			List<ScoreEntry> send = Ranking(pair.Key).Where(e => e.Id != id && (first || e.Updated > since)).Take(FirstSync).ToList();
			ScoreEntry mine = Mine(pair.Key);
			if (mine != null && (first || mine.Updated > since) && !send.Contains(mine))
			{
				send.Add(mine);
			}
			foreach (ScoreEntry e in send)
			{
				newest = Math.Max(newest, e.Updated);
			}
			for (int i = 0; i < send.Count; i += PerMessage)
			{
				PipeNet.SendTo(player, Channel, Pack(pair.Key, send.Skip(i).Take(PerMessage).ToList()));
			}
		}
		_synced[id] = Math.Max(newest, first ? UnixNow : since);
		_saveAt = GameArt.Now + 2.0;
	}

	private static byte[] Pack(byte game, List<ScoreEntry> entries)
	{
		List<byte> data = new List<byte> { EntriesFull, game, (byte)entries.Count };
		foreach (ScoreEntry e in entries)
		{
			data.AddRange(BitConverter.GetBytes(e.Id));
			data.AddRange(BitConverter.GetBytes(e.Score));
			data.AddRange(BitConverter.GetBytes((uint)Math.Max(0L, Math.Min(uint.MaxValue, e.When))));
			byte[] name = Encoding.UTF8.GetBytes(e.Name ?? "");
			if (name.Length > 30)
			{
				name = Encoding.UTF8.GetBytes(TrimName(e.Name, 30));
			}
			data.Add((byte)name.Length);
			data.AddRange(name);
			data.Add((byte)((e.Look != null) ? 1 : 0));
			e.Look?.Write(data);
			data.AddRange(BitConverter.GetBytes((ushort)Math.Min(e.Wins, 65535)));
			data.AddRange(BitConverter.GetBytes((ushort)Math.Min(e.Losses, 65535)));
			data.AddRange(BitConverter.GetBytes((ushort)Math.Min(e.Draws, 65535)));
		}
		return data.ToArray();
	}

	private static string TrimName(string name, int bytes)
	{
		while (name.Length > 0 && Encoding.UTF8.GetByteCount(name) > bytes)
		{
			name = name.Substring(0, name.Length - 1);
		}
		return name;
	}

	private static void OnMessage(PipeInbound m)
	{
		if (m.Channel != Channel || m.Data == null || m.Data.Length < 3 || (m.Data[0] != Entries && m.Data[0] != EntriesWithLook && m.Data[0] != EntriesFull) || m.FromPlayer == Main.myPlayer)
		{
			return;
		}
		try
		{
			Load();
			byte game = m.Data[1];
			int count = m.Data[2];
			int at = 3;
			int myRank = RankOf(game, ClientIdentity.Id);
			bool changed = false;
			string passedBy = null;
			for (int i = 0; i < count && at + 17 <= m.Data.Length; i++)
			{
				ScoreEntry e = new ScoreEntry
				{
					Id = BitConverter.ToUInt64(m.Data, at),
					Score = BitConverter.ToInt32(m.Data, at + 8),
					When = BitConverter.ToUInt32(m.Data, at + 12)
				};
				int length = m.Data[at + 16];
				at += 17;
				if (at + length > m.Data.Length)
				{
					break;
				}
				e.Name = Encoding.UTF8.GetString(m.Data, at, length);
				at += length;
				if (m.Data[0] >= EntriesWithLook && at < m.Data.Length)
				{
					bool hasLook = m.Data[at++] == 1;
					if (hasLook && at + PlayerLook.Size <= m.Data.Length)
					{
						e.Look = PlayerLook.Read(m.Data, at);
						at += PlayerLook.Size;
					}
				}
				if (m.Data[0] >= EntriesFull && at + 6 <= m.Data.Length)
				{
					e.Wins = BitConverter.ToUInt16(m.Data, at);
					e.Losses = BitConverter.ToUInt16(m.Data, at + 2);
					e.Draws = BitConverter.ToUInt16(m.Data, at + 4);
					at += 6;
				}
				if (game == Global)
				{
					continue;
				}
				if (Merge(game, e))
				{
					changed = true;
					ScoreEntry mine = Mine(game);
					if (mine != null && e.Score > mine.Score)
					{
						passedBy = e.Name;
					}
				}
			}
			if (changed)
			{
				Changed();
				int now = RankOf(game, ClientIdentity.Id);
				if (myRank > 0 && now > myRank && passedBy != null)
				{
					Notice(IsaClient.Languages.L10n.F("{0} passed you in {1}: you're #{2} now.", passedBy, GameName(game), now));
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static void Notice(string text)
	{
		ClientChat.Info(text);
	}

	#endregion

	#region File

	private static void Load()
	{
		if (_loaded)
		{
			return;
		}
		_loaded = true;
		try
		{
			if (!File.Exists(FilePath))
			{
				return;
			}
			JObject root = JObject.Parse(File.ReadAllText(FilePath));
			foreach (JProperty game in (root["Boards"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
			{
				if (!byte.TryParse(game.Name, out byte id))
				{
					continue;
				}
				Dictionary<ulong, ScoreEntry> book = Book(id);
				foreach (JToken e in (game.Value as JArray) ?? new JArray())
				{
					if (ulong.TryParse((string)e["Id"], System.Globalization.NumberStyles.HexNumber, null, out ulong player) && player != 0)
					{
						book[player] = new ScoreEntry
						{
							Id = player,
							Name = (string)e["Name"] ?? "",
							Score = (int?)e["Score"] ?? 0,
							When = (long?)e["When"] ?? 0,
							Updated = (long?)e["Updated"] ?? 0,
							Look = PlayerLook.FromJson(e["Look"]),
							Wins = (int?)e["Wins"] ?? 0,
							Losses = (int?)e["Losses"] ?? 0,
							Draws = (int?)e["Draws"] ?? 0
						};
					}
				}
			}
			foreach (JProperty peer in (root["Synced"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
			{
				if (ulong.TryParse(peer.Name, System.Globalization.NumberStyles.HexNumber, null, out ulong peerId))
				{
					_synced[peerId] = (long)peer.Value;
				}
			}
			foreach (JProperty game in (root["Seen"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
			{
				if (!byte.TryParse(game.Name, out byte id))
				{
					continue;
				}
				Dictionary<ulong, int> ranks = new Dictionary<ulong, int>();
				foreach (JProperty p in (game.Value as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
				{
					if (ulong.TryParse(p.Name, System.Globalization.NumberStyles.HexNumber, null, out ulong player))
					{
						ranks[player] = (int)p.Value;
					}
				}
				_seen[id] = ranks;
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static void Save()
	{
		try
		{
			JObject boards = new JObject();
			foreach (KeyValuePair<byte, Dictionary<ulong, ScoreEntry>> pair in _books)
			{
				boards[pair.Key.ToString()] = new JArray(pair.Value.Values.Select(e => new JObject
				{
					["Id"] = ClientIdentity.Hex(e.Id),
					["Name"] = e.Name,
					["Score"] = e.Score,
					["When"] = e.When,
					["Updated"] = e.Updated,
					["Look"] = e.Look?.ToJson(),
					["Wins"] = e.Wins,
					["Losses"] = e.Losses,
					["Draws"] = e.Draws
				}));
			}
			JObject seen = new JObject();
			foreach (KeyValuePair<byte, Dictionary<ulong, int>> pair in _seen)
			{
				JObject ranks = new JObject();
				foreach (KeyValuePair<ulong, int> r in pair.Value)
				{
					ranks[ClientIdentity.Hex(r.Key)] = r.Value;
				}
				seen[pair.Key.ToString()] = ranks;
			}
			Directory.CreateDirectory(ClientSettings.IsaClientFolder);
			string temp = FilePath + ".tmp";
			JObject synced = new JObject();
			foreach (KeyValuePair<ulong, long> peer in _synced)
			{
				synced[ClientIdentity.Hex(peer.Key)] = peer.Value;
			}
			File.WriteAllText(temp, new JObject { ["Boards"] = boards, ["Seen"] = seen, ["Synced"] = synced }.ToString());
			if (File.Exists(FilePath))
			{
				File.Delete(FilePath);
			}
			File.Move(temp, FilePath);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	#endregion
}
