using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IsaClient.Core;
using IsaClient.Net.Pipe;
using Terraria;
using Terraria.Audio;

namespace IsaClient.Games;

/// <summary>An invitation someone sent you (or everyone).</summary>
public sealed class GameInvite
{
	public PipeGame Game;

	public int From;

	public byte Match;

	public bool ToEveryone;

	public double Until;

	/// <summary>Who moves first as the inviter chose it (0 the inviter, 1 you, 2 random).</summary>
	public int First = 2;

	/// <summary>Seconds per turn (-1 unknown: an older IsaClient).</summary>
	public int TurnSeconds = -1;
}

/// <summary>Your invitation, waiting for an answer.</summary>
public sealed class OutgoingInvite
{
	public PipeGame Game;

	/// <summary>A player, or <see cref="PipeNet.Everyone"/>.</summary>
	public int To;

	public byte Match;

	public double Until;
}

public enum MatchState
{
	/// <summary>You accepted: waiting for the other player to start it.</summary>
	Joining,
	Playing,
	Over,
	/// <summary>The other player left or disconnected.</summary>
	Abandoned
}

/// <summary>A game with another player: rounds, turns and the score.</summary>
public sealed class GameMatch
{
	public PipeGame Game;

	public int Opponent;

	/// <summary>Your seat: 0 if you invited, 1 if you accepted.</summary>
	public int MySeat;

	public byte Id;

	public MatchState State;

	public IGameBoard Board;

	/// <summary>Seat that moves first in round 0; it alternates every round.</summary>
	public int FirstSeat;

	public int Round;

	/// <summary>Moves made this round: the next move must carry this number.</summary>
	public int Ply;

	public int MyWins;

	public int TheirWins;

	public int Draws;

	public bool IWantRematch;

	public bool TheyWantRematch;

	public double JoiningUntil;

	public string OpponentName = "";

	/// <summary>Seconds for each turn (0 = no limit), chosen by who invited.</summary>
	public int TurnSeconds;

	/// <summary>When the current turn began (in ticks).</summary>
	public double TurnStarted;

	/// <summary>A result decided by time (0/1 the seat that won), -1 when the board decides.</summary>
	public int ForcedResult = -1;

	/// <summary>Against the computer: no pipe, the computer thinks in the background.</summary>
	public bool VsBot;

	public int BotLevel;

	public Task<byte[]> BotTask;

	public int BotRound;

	public int BotPly;

	/// <summary>The computer moves no sooner than this (<see cref="GameArt.Now"/>).</summary>
	public double BotAt;

	/// <summary>The computer is thinking about its move.</summary>
	public bool BotThinking => VsBot && State == MatchState.Playing && Board.Turn != MySeat && Result < 0;

	public bool MyTurn => State == MatchState.Playing && (Game.RealTime || Board.Turn == MySeat);

	/// <summary>-1 still playing, 0 or 1 that seat won, 2 a draw.</summary>
	public int Result => (ForcedResult >= 0) ? ForcedResult : Board.Result;

	/// <summary>Seconds left for the current turn (0 when there is no limit).</summary>
	public float SecondsLeft => (TurnSeconds <= 0) ? 0f : Math.Max(0f, TurnSeconds - (float)((Main.timeForVisualEffects - TurnStarted) / 60.0));
}

/// <summary>
/// Games between IsaClient players over the pipe (<see cref="PipeNet"/>, channel <see cref="Channel"/>): invitations to
/// one player or to everyone, accept/decline, turns, results, rematches, and someone leaving or disconnecting halfway.
/// One match at a time. Every message: [kind, game id, match id, ...payload].
/// <list type="bullet">
/// <item>Invite → Accept → Start (who goes first). An invite to everyone goes to the first who accepts; the rest get
/// Cancel, and anyone accepting late gets Taken.</item>
/// <item>Move [round, ply, move...]: round and ply numbers make repeats and stragglers harmless.</item>
/// <item>Rematch [round]: when both asked, the next round starts and the other seat begins.</item>
/// <item>Leave: the match ends for the other player; a player who disconnects ends it the same way.</item>
/// <item>Turn clock (seconds sent with Start): who runs out sends OutOfTime and loses the round; a silent opponent gets
/// a few more seconds, then TimeClaim gives you the round.</item>
/// </list>
/// </summary>
public static class GameLobby
{
	/// <summary>The pipe channel of games ('G').</summary>
	public const byte Channel = 71;

	private const byte Invite = 1;

	private const byte Accept = 2;

	private const byte Decline = 3;

	private const byte Cancel = 4;

	private const byte Start = 5;

	private const byte Move = 6;

	private const byte Leave = 7;

	private const byte Rematch = 8;

	private const byte Busy = 9;

	private const byte Taken = 10;

	/// <summary>[round]: the sender ran out of time and loses the round.</summary>
	private const byte OutOfTime = 11;

	/// <summary>[round]: the sender's opponent let their time run out (and didn't say so): the sender wins it.</summary>
	private const byte TimeClaim = 12;

	/// <summary>[round]: the sender gives up the round.</summary>
	private const byte Resign = 13;

	/// <summary>A silent opponent gets this much more than their time before the round is yours.</summary>
	private const double GraceTicks = 10.0 * 60.0;

	/// <summary>Invitations last this long (in ticks of 1/60 s).</summary>
	private const double InviteTicks = 60.0 * 60.0;

	private const double JoinTicks = 20.0 * 60.0;

	[Setting("GamesInvitations")]
	public static bool AcceptInvitations = true;

	/// <summary>Seconds per turn in games you invite to (0 = no limit).</summary>
	[Setting("GamesTurnSeconds", Min = 0, Max = 300)]
	public static int TurnSeconds = 30;

	public static readonly List<PipeGame> Games = new List<PipeGame>();

	public static readonly List<GameInvite> Invites = new List<GameInvite>();

	public static OutgoingInvite Outgoing;

	public static GameMatch Match;

	private static bool _hooked;

	private static readonly Random _random = new Random();

	private static double Now => Main.timeForVisualEffects;

	static GameLobby()
	{
		Register(TicTacToe.Game);
		Register(Chess.Game);
		Register(TetrisBattle.Game);
	}

	public static void Register(PipeGame game)
	{
		if (Games.All(g => g.Id != game.Id))
		{
			Games.Add(game);
		}
	}

	public static PipeGame Find(byte id)
	{
		return Games.FirstOrDefault(g => g.Id == id);
	}

	/// <summary>True when you can play right now (online, the pipe on).</summary>
	public static bool CanPlay => PipeNet.Enabled && PipeNet.Ready && !Main.gameMenu;

	public static bool InMatch => Match != null && Match.State != MatchState.Abandoned && Match.State != MatchState.Over;

	/// <summary>Playing someone over the pipe (a game against the computer can always be dropped for one).</summary>
	public static bool InOnlineMatch => InMatch && !Match.VsBot;

	/// <summary>Seconds per turn of the games you invite to.</summary>
	public static int TurnSecondsFor(PipeGame game)
	{
		if (game.RealTime)
		{
			// No turn clock: games without turns send their own option instead (Tetris Versus: the speed).
			return game.TurnTime?.Get() ?? 0;
		}
		return Math.Max(0, Math.Min(255, game.TurnTime?.Get() ?? TurnSeconds));
	}

	/// <summary>Every frame (game loop): hooks the pipe once, expires invitations, notices players who left.</summary>
	public static void Update()
	{
		if (!_hooked)
		{
			_hooked = true;
			PipeNet.OnMessage += OnMessage;
			GameOverlay.Ensure();
		}
		GameOverlay.KeepOnTop();
		IsaClient.Identity.ClientIdentity.Update();
		Scores.Update();
		Tetris.Update();
		Snake.Update();
		SnakeArt.Update();
		TetrisArt.Update();
		IsaClient.Gba.PokemonWindow.Update();
		IsaClient.Psp.PspWindow.Update();
		IsaClient.Casino.CasinoWindow.Update();
		if (Main.gameMenu)
		{
			if (Match != null || Outgoing != null || Invites.Count > 0)
			{
				Match = null;
				Outgoing = null;
				Invites.Clear();
			}
			return;
		}
		if (Match != null && Match.VsBot)
		{
			UpdateBot(Match);
		}
		if (Main.netMode != 1)
		{
			// Alone: only games against the computer.
			Outgoing = null;
			Invites.Clear();
			if (Match != null && !Match.VsBot)
			{
				Match = null;
			}
			return;
		}
		Invites.RemoveAll(i => Now > i.Until || !Online(i.From));
		if (Outgoing != null && (Now > Outgoing.Until || (Outgoing.To != PipeNet.Everyone && !Online(Outgoing.To))))
		{
			if (Outgoing.To != PipeNet.Everyone && Online(Outgoing.To))
			{
				Notice("{0} didn't answer your invitation.", Name(Outgoing.To));
			}
			CancelInvite(quiet: true);
		}
		if (Match == null || Match.VsBot)
		{
			return;
		}
		if (Match.State == MatchState.Joining && Now > Match.JoiningUntil)
		{
			Notice("{0} didn't start the game.", Match.OpponentName);
			Match = null;
			return;
		}
		if ((Match.State == MatchState.Playing || Match.State == MatchState.Over || Match.State == MatchState.Joining) && !Online(Match.Opponent))
		{
			Match.State = MatchState.Abandoned;
			Notice("{0} left the game.", Match.OpponentName);
			SoundEngine.PlaySound(11);
			return;
		}
		// Games without turns run every tick and send what they say.
		if (Match.State == MatchState.Playing && Match.Board is IRealtimeBoard live)
		{
			GameOverlay.UpdateFocus();
			foreach (byte[] e in live.Tick(Match.MySeat, GameOverlay.BoardHasKeys))
			{
				byte[] payload = new byte[2 + e.Length];
				payload[0] = (byte)Match.Round;
				Array.Copy(e, 0, payload, 2, e.Length);
				PipeNet.SendTo(Match.Opponent, Channel, Message(Move, Match.Game, Match.Id, payload));
			}
			CheckOver(Match);
			return;
		}
		// The turn clock.
		if (Match.State == MatchState.Playing && Match.TurnSeconds > 0)
		{
			double elapsed = Now - Match.TurnStarted;
			if (Match.MyTurn && elapsed >= Match.TurnSeconds * 60.0)
			{
				PipeNet.SendTo(Match.Opponent, Channel, Message(OutOfTime, Match.Game, Match.Id, new[] { (byte)Match.Round }));
				Notice("Your time ran out.");
				EndByTime(Match, 1 - Match.MySeat);
			}
			else if (!Match.MyTurn && elapsed >= Match.TurnSeconds * 60.0 + GraceTicks)
			{
				PipeNet.SendTo(Match.Opponent, Channel, Message(TimeClaim, Match.Game, Match.Id, new[] { (byte)Match.Round }));
				Notice("{0} ran out of time.", Match.OpponentName);
				EndByTime(Match, Match.MySeat);
			}
		}
	}

	private static void EndByTime(GameMatch match, int winner)
	{
		match.ForcedResult = winner;
		CheckOver(match);
	}

	/// <summary>The computer's turn: starts its thinking, and plays the move once it's ready and its little wait is over.</summary>
	private static void UpdateBot(GameMatch match)
	{
		if (!match.BotThinking)
		{
			match.BotTask = null;
			return;
		}
		if (match.BotTask == null || match.BotRound != match.Round || match.BotPly != match.Ply)
		{
			match.BotRound = match.Round;
			match.BotPly = match.Ply;
			match.BotAt = GameArt.Now + match.Game.BotDelay;
			match.BotTask = Task.Run(match.Board.Think(match.BotLevel));
			return;
		}
		if (!match.BotTask.IsCompleted || GameArt.Now < match.BotAt)
		{
			return;
		}
		Task<byte[]> task = match.BotTask;
		match.BotTask = null;
		byte[] move = (task.Status == TaskStatus.RanToCompletion) ? task.Result : null;
		if (task.Exception != null)
		{
			ClientLog.LogException(task.Exception);
		}
		if (move == null || !match.Board.Apply(1 - match.MySeat, move))
		{
			Notice("The computer gave up.");
			EndByTime(match, match.MySeat);
			return;
		}
		match.Ply++;
		match.TurnStarted = Now;
		SoundEngine.PlaySound(12);
		CheckOver(match);
	}

	#region What the player does

	/// <summary>Invites a player, or everyone (<see cref="PipeNet.Everyone"/>).</summary>
	public static void InvitePlayer(PipeGame game, int player)
	{
		if (!CanPlay)
		{
			ClientChat.Error("Games need a server and the pipe on (.pipe on).");
			return;
		}
		if (InOnlineMatch)
		{
			ClientChat.Error("Finish or leave your game first.");
			return;
		}
		if (player != PipeNet.Everyone && (player == Main.myPlayer || !Online(player)))
		{
			return;
		}
		if (Match != null && !Match.VsBot)
		{
			Match = null;
		}
		Outgoing = new OutgoingInvite
		{
			Game = game,
			To = player,
			Match = NewMatchId(),
			Until = Now + InviteTicks
		};
		byte[] message = Message(Invite, game, Outgoing.Match, new[] { (byte)(game.First?.Get() ?? 2), (byte)TurnSecondsFor(game) });
		if (player == PipeNet.Everyone)
		{
			PipeNet.Broadcast(Channel, message);
			ClientChat.Info(IsaClient.Languages.L10n.F("You invited everyone with IsaClient to {0}. The first who accepts plays.", IsaClient.Languages.L10n.T(game.Name)));
		}
		else
		{
			PipeNet.SendTo(player, Channel, message);
			ClientChat.Info(IsaClient.Languages.L10n.F("You invited {0} to {1}.", Name(player), IsaClient.Languages.L10n.T(game.Name)));
		}
		SoundEngine.PlaySound(12);
	}

	public static void CancelInvite(bool quiet = false)
	{
		if (Outgoing == null)
		{
			return;
		}
		byte[] message = Message(Cancel, Outgoing.Game, Outgoing.Match);
		if (Outgoing.To == PipeNet.Everyone)
		{
			PipeNet.Broadcast(Channel, message);
		}
		else if (Online(Outgoing.To))
		{
			PipeNet.SendTo(Outgoing.To, Channel, message);
		}
		Outgoing = null;
		if (!quiet)
		{
			SoundEngine.PlaySound(11);
		}
	}

	public static void AcceptInvite(GameInvite invite)
	{
		Invites.Remove(invite);
		if (InOnlineMatch)
		{
			ClientChat.Error("Finish or leave your game first.");
			return;
		}
		CancelInvite(quiet: true);
		Match = new GameMatch
		{
			Game = invite.Game,
			Opponent = invite.From,
			OpponentName = Name(invite.From),
			MySeat = 1,
			Id = invite.Match,
			State = MatchState.Joining,
			Board = invite.Game.Create(),
			JoiningUntil = Now + JoinTicks
		};
		GamePlays.CountMatch(Match.Game);
		PipeNet.SendTo(invite.From, Channel, Message(Accept, invite.Game, invite.Match));
		SoundEngine.PlaySound(12);
	}

	public static void DeclineInvite(GameInvite invite)
	{
		Invites.Remove(invite);
		if (!invite.ToEveryone)
		{
			PipeNet.SendTo(invite.From, Channel, Message(Decline, invite.Game, invite.Match));
		}
		SoundEngine.PlaySound(11);
	}

	/// <summary>A move the board made for you: applied here and sent.</summary>
	public static void PlayMove(byte[] move)
	{
		GameMatch match = Match;
		if (match == null || !match.MyTurn || !match.Board.Apply(match.MySeat, move))
		{
			return;
		}
		byte[] payload = new byte[2 + move.Length];
		payload[0] = (byte)match.Round;
		payload[1] = (byte)match.Ply;
		Array.Copy(move, 0, payload, 2, move.Length);
		match.Ply++;
		match.TurnStarted = Now;
		if (!match.VsBot)
		{
			PipeNet.SendTo(match.Opponent, Channel, Message(Move, match.Game, match.Id, payload));
		}
		SoundEngine.PlaySound(12);
		CheckOver(match);
	}

	public static void AskRematch()
	{
		GameMatch match = Match;
		if (match == null || match.State != MatchState.Over || match.IWantRematch)
		{
			return;
		}
		if (match.VsBot)
		{
			NextRound(match);
			return;
		}
		match.IWantRematch = true;
		PipeNet.SendTo(match.Opponent, Channel, Message(Rematch, match.Game, match.Id, new[] { (byte)(match.Round + 1) }));
		SoundEngine.PlaySound(12);
		if (match.TheyWantRematch)
		{
			NextRound(match);
		}
	}

	/// <summary>Leaves the match (the other player is told) or closes one that already ended.</summary>
	public static void LeaveMatch()
	{
		GameMatch match = Match;
		if (match == null)
		{
			return;
		}
		if (!match.VsBot && match.State != MatchState.Abandoned && Online(match.Opponent))
		{
			PipeNet.SendTo(match.Opponent, Channel, Message(Leave, match.Game, match.Id));
		}
		Match = null;
		SoundEngine.PlaySound(11);
	}

	/// <summary>A game against the computer (its level and who starts come from the game's options).</summary>
	public static void PlayComputer(PipeGame game)
	{
		if (Main.gameMenu)
		{
			return;
		}
		if (InOnlineMatch)
		{
			ClientChat.Error("Finish or leave your game first.");
			return;
		}
		int level = game.BotLevel?.Get() ?? 1;
		int choice = game.First?.Get() ?? 2;
		int first = (choice == 2) ? _random.Next(2) : choice;
		string levelName = game.BotLevel?.Label(level) ?? "";
		Match = new GameMatch
		{
			Game = game,
			Opponent = -1,
			OpponentName = IsaClient.Languages.L10n.F("Computer ({0})", levelName),
			MySeat = 0,
			State = MatchState.Playing,
			Board = game.Create(),
			FirstSeat = first,
			VsBot = true,
			BotLevel = level,
			TurnStarted = Now
		};
		GamePlays.CountMatch(Match.Game);
		Match.Board.Reset(first);
		SoundEngine.PlaySound(10);
		GameOverlay.Show();
	}

	/// <summary>Gives up the round (the other player wins it).</summary>
	public static void ResignRound()
	{
		GameMatch match = Match;
		if (match == null || match.State != MatchState.Playing)
		{
			return;
		}
		if (!match.VsBot)
		{
			PipeNet.SendTo(match.Opponent, Channel, Message(Resign, match.Game, match.Id, new[] { (byte)match.Round }));
		}
		Notice("You resigned.");
		EndByTime(match, 1 - match.MySeat);
	}

	#endregion

	#region Messages

	private static void OnMessage(PipeInbound m)
	{
		if (m.Channel != Channel || m.Data == null || m.Data.Length < 3 || m.FromPlayer == Main.myPlayer || Main.gameMenu)
		{
			return;
		}
		try
		{
			Handle(m.FromPlayer, m.Data, m.Broadcast);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static void Handle(int from, byte[] data, bool broadcast)
	{
		byte kind = data[0];
		PipeGame game = Find(data[1]);
		byte id = data[2];
		if (game == null || from < 0 || from >= 255)
		{
			return;
		}
		switch (kind)
		{
		case Invite:
			OnInvite(game, from, id, broadcast, data);
			break;
		case Accept:
			OnAccept(game, from, id);
			break;
		case Decline:
			if (Outgoing != null && Outgoing.Match == id && Outgoing.To == from)
			{
				Notice("{0} declined your invitation.", Name(from));
				Outgoing = null;
			}
			break;
		case Cancel:
			Invites.RemoveAll(i => i.From == from && i.Match == id);
			break;
		case Start:
			if (Match != null && Match.State == MatchState.Joining && Match.Opponent == from && Match.Id == id && data.Length >= 4)
			{
				Match.FirstSeat = data[3] & 1;
				Match.TurnSeconds = (data.Length >= 5) ? data[4] : 0;
				Match.State = MatchState.Playing;
				Match.Board.Reset(Match.FirstSeat);
				Match.TurnStarted = Now;
				Notice("{0} vs {1}: the game starts!", IsaClient.Languages.L10n.T(game.Name), Match.OpponentName);
				SoundEngine.PlaySound(10);
			}
			break;
		case Taken:
		case Busy:
			if (Match != null && Match.State == MatchState.Joining && Match.Opponent == from && Match.Id == id)
			{
				Notice((kind == Taken) ? "Someone else accepted {0}'s invitation first." : "{0} is already playing.", Match.OpponentName);
				Match = null;
			}
			else if (Outgoing != null && Outgoing.Match == id && Outgoing.To == from)
			{
				Notice("{0} is already playing.", Name(from));
				Outgoing = null;
			}
			break;
		case Move:
			OnMove(from, id, data);
			break;
		case Leave:
			if (Match != null && Match.Opponent == from && Match.Id == id && Match.State != MatchState.Abandoned)
			{
				Match.State = MatchState.Abandoned;
				Notice("{0} left the game.", Match.OpponentName);
				SoundEngine.PlaySound(11);
			}
			break;
		case OutOfTime:
		case TimeClaim:
			if (Match != null && Match.Opponent == from && Match.Id == id && Match.State == MatchState.Playing && data.Length >= 4 && data[3] == (byte)Match.Round)
			{
				// OutOfTime: they lost the round. TimeClaim: they say you let your time run out.
				if (kind == OutOfTime)
				{
					Notice("{0} ran out of time.", Match.OpponentName);
					EndByTime(Match, Match.MySeat);
				}
				else
				{
					Notice("Your time ran out.");
					EndByTime(Match, 1 - Match.MySeat);
				}
			}
			break;
		case Resign:
			if (Match != null && !Match.VsBot && Match.Opponent == from && Match.Id == id && Match.State == MatchState.Playing && data.Length >= 4 && data[3] == (byte)Match.Round)
			{
				Notice("{0} resigned.", Match.OpponentName);
				EndByTime(Match, Match.MySeat);
			}
			break;
		case Rematch:
			if (Match != null && Match.Opponent == from && Match.Id == id && Match.State == MatchState.Over && data.Length >= 4 && data[3] == (byte)(Match.Round + 1))
			{
				Match.TheyWantRematch = true;
				if (Match.IWantRematch)
				{
					NextRound(Match);
				}
				else
				{
					Notice("{0} wants a rematch.", Match.OpponentName);
				}
			}
			break;
		}
	}

	private static void OnInvite(PipeGame game, int from, byte id, bool everyone, byte[] data)
	{
		if (!AcceptInvitations)
		{
			if (!everyone)
			{
				PipeNet.SendTo(from, Channel, Message(Decline, game, id));
			}
			return;
		}
		if (InOnlineMatch)
		{
			if (!everyone)
			{
				PipeNet.SendTo(from, Channel, Message(Busy, game, id));
			}
			return;
		}
		Invites.RemoveAll(i => i.From == from);
		Invites.Add(new GameInvite
		{
			Game = game,
			From = from,
			Match = id,
			ToEveryone = everyone,
			Until = Now + InviteTicks,
			First = (data.Length >= 4 && data[3] <= 2) ? data[3] : 2,
			TurnSeconds = (data.Length >= 5) ? data[4] : -1
		});
		ClientChat.Info(IsaClient.Languages.L10n.F(everyone ? "{0} invites everyone to {1}: accept it in the game window." : "{0} invites you to {1}: accept it in the game window.", Name(from), IsaClient.Languages.L10n.T(game.Name)));
		SoundEngine.PlaySound(10);
		GameOverlay.Show();
	}

	private static void OnAccept(PipeGame game, int from, byte id)
	{
		if (Outgoing == null || Outgoing.Match != id || Outgoing.Game != game || (Outgoing.To != PipeNet.Everyone && Outgoing.To != from) || InOnlineMatch)
		{
			// An invite to everyone that someone else took, or one you cancelled.
			PipeNet.SendTo(from, Channel, Message(Taken, game, id));
			return;
		}
		bool everyone = Outgoing.To == PipeNet.Everyone;
		Outgoing = null;
		int choice = game.First?.Get() ?? 2;
		int first = (choice == 2) ? _random.Next(2) : choice;
		Match = new GameMatch
		{
			Game = game,
			Opponent = from,
			OpponentName = Name(from),
			MySeat = 0,
			Id = id,
			State = MatchState.Playing,
			Board = game.Create(),
			FirstSeat = first,
			TurnSeconds = TurnSecondsFor(game),
			TurnStarted = Now
		};
		GamePlays.CountMatch(Match.Game);
		Match.Board.Reset(first);
		PipeNet.SendTo(from, Channel, Message(Start, game, id, new[] { (byte)first, (byte)Match.TurnSeconds }));
		if (everyone)
		{
			PipeNet.Broadcast(Channel, Message(Cancel, game, id));
		}
		Notice("{0} accepted: the game starts!", Match.OpponentName);
		SoundEngine.PlaySound(10);
		GameOverlay.Show();
	}

	private static void OnMove(int from, byte id, byte[] data)
	{
		GameMatch match = Match;
		if (match == null || match.Opponent != from || match.Id != id || match.State != MatchState.Playing || data.Length < 6)
		{
			return;
		}
		// Retransmissions and stragglers carry an old round or ply: only the next move of this round counts.
		if (data[3] != (byte)match.Round || (!match.Game.RealTime && data[4] != (byte)match.Ply))
		{
			return;
		}
		byte[] move = new byte[data.Length - 5];
		Array.Copy(data, 5, move, 0, move.Length);
		if (!match.Board.Apply(1 - match.MySeat, move))
		{
			return;
		}
		match.Ply++;
		match.TurnStarted = Now;
		if (!match.Game.RealTime)
		{
			SoundEngine.PlaySound(12);
		}
		CheckOver(match);
	}

	private static void CheckOver(GameMatch match)
	{
		int result = match.Result;
		if (result < 0)
		{
			return;
		}
		match.State = MatchState.Over;
		match.IWantRematch = false;
		match.TheyWantRematch = false;
		// Against a player it counts on the boards (Elo, rank points); against the computer it doesn't.
		if (!match.VsBot && match.Game.ScoreBoard != 0)
		{
			Scores.RecordMatch(match.Game.ScoreBoard, IsaClient.Identity.ClientIdentity.Of(match.Opponent), (result == 2) ? 0.5 : ((result == match.MySeat) ? 1.0 : 0.0));
		}
		if (result == 2)
		{
			match.Draws++;
			Notice("Draw against {0}.", match.OpponentName);
		}
		else if (result == match.MySeat)
		{
			match.MyWins++;
			Notice("You beat {0}!", match.OpponentName);
			SoundEngine.PlaySound(Terraria.ID.SoundID.AchievementComplete);
		}
		else
		{
			match.TheirWins++;
			Notice("{0} won this one.", match.OpponentName);
		}
	}

	private static void NextRound(GameMatch match)
	{
		match.Round++;
		match.Ply = 0;
		match.IWantRematch = false;
		match.TheyWantRematch = false;
		match.State = MatchState.Playing;
		match.ForcedResult = -1;
		match.Board.Reset(match.FirstSeat ^ (match.Round & 1));
		match.TurnStarted = Now;
		SoundEngine.PlaySound(10);
	}

	private static byte[] Message(byte kind, PipeGame game, byte match, byte[] payload = null)
	{
		byte[] data = new byte[3 + (payload?.Length ?? 0)];
		data[0] = kind;
		data[1] = game.Id;
		data[2] = match;
		if (payload != null)
		{
			Array.Copy(payload, 0, data, 3, payload.Length);
		}
		return data;
	}

	#endregion

	private static byte NewMatchId()
	{
		return (byte)_random.Next(1, 256);
	}

	public static bool Online(int player)
	{
		return player >= 0 && player < 255 && Main.player[player] != null && Main.player[player].active;
	}

	public static string Name(int player)
	{
		string name = (player >= 0 && player < 255) ? Main.player[player]?.name : null;
		return string.IsNullOrEmpty(name) ? ("#" + player) : name;
	}

	/// <summary>IsaClient players here who can receive an invitation.</summary>
	public static IEnumerable<int> Players()
	{
		for (int i = 0; i < 255; i++)
		{
			if (i != Main.myPlayer && Online(i) && Main.player[i].IsaClientKind == 1)
			{
				yield return i;
			}
		}
	}

	private static void Notice(string format, params object[] args)
	{
		ClientChat.Info(IsaClient.Languages.L10n.F(format, args));
	}
}
