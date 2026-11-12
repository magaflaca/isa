using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Core;
using IsaClient.Games;
using IsaClient.Net.Pipe;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// Games: "Play" shows your match, the invitations you got, a card per game (a little preview, its options, play
/// against the computer or invite everyone) and the IsaClient players here (invite one to any game); "Customize" has
/// the invitations, the window and each game's options, and the X and O icons. The board is its own window.
/// </summary>
internal static class HubGames
{
	private static HubTheme Theme => HubTheme.Current;

	private static readonly Color Green = new Color(60, 140, 80);

	private static Color On => Color.Lerp(Theme.Slot, Theme.Accent, 0.45f);

	public static int DrawPlay(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		GameMatch match = GameLobby.Match;
		if (match != null)
		{
			y = DrawMatch(sb, match, x, y, width) + 12;
		}
		foreach (GameInvite invite in GameLobby.Invites.ToArray())
		{
			Rectangle r = new Rectangle(x, y, width, 48);
			HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.Accent, 0.12f) * HubPrefs.Opacity);
			HubIcon.Game(invite.Game).Draw(sb, new Rectangle(r.X + 10, r.Y + 8, 32, 32), Color.White, 3f);
			string text = IsaClient.Languages.L10n.F(invite.ToEveryone ? "{0} invites everyone to {1}" : "{0} invites you to {1}", GameLobby.Name(invite.From), IsaClient.Languages.L10n.T(invite.Game.Name));
			HubKit.RawText(sb, text, r.X + 52, r.Y + 15, Color.White, 0.78f, width - 260);
			if (HubKit.Button(sb, "games:inv:yes:" + invite.From, new Rectangle(r.Right - 196, r.Y + 9, 90, 30), "Join", Green, null))
			{
				GameLobby.AcceptInvite(invite);
				GameOverlay.Show();
			}
			if (HubKit.Button(sb, "games:inv:no:" + invite.From, new Rectangle(r.Right - 100, r.Y + 9, 90, 30), "Decline", Theme.Slot, null))
			{
				GameLobby.DeclineInvite(invite);
			}
			y = r.Bottom + 8;
		}
		y += 2;
		y = DrawPlayers(sb, x, y, width) + 10;
		HubKit.Text(sb, "Games", x, y, Theme.Muted, 0.72f);
		y += 22;
		// The games you play most first (Casino, GBA/DS and PSP, Chess, Tetris, Snake, Tetris Versus, Tic Tac Toe until then). The
		// order is taken when the page opens and kept while it's shown, so a card doesn't move under the mouse.
		if (_order == null || GameArt.Now - _shownAt > 0.5)
		{
			_order = GamePlays.Order();
		}
		_shownAt = GameArt.Now;
		foreach (string key in _order)
		{
			switch (key)
			{
			case GamePlays.Casino:
				y = DrawCasinoCard(sb, x, y, width) + 10;
				break;
			case GamePlays.Gba:
				y = DrawPokemonCard(sb, x, y, width) + 10;
				y = DrawPspCard(sb, x, y, width) + 10;
				break;
			case GamePlays.Tetris:
				y = DrawTetrisCard(sb, x, y, width) + 10;
				break;
			case GamePlays.Snake:
				y = DrawSnakeCard(sb, x, y, width) + 10;
				break;
			default:
			{
				PipeGame game = PipeGameFor(key);
				if (game != null && GameLobby.Games.Contains(game))
				{
					y = DrawGameCard(sb, game, x, y, width) + 10;
				}
				break;
			}
			}
		}
		// Any other game over the pipe (none yet) after them.
		foreach (PipeGame game in GameLobby.Games)
		{
			if (game != Chess.Game && game != TetrisBattle.Game && game != TicTacToe.Game)
			{
				y = DrawGameCard(sb, game, x, y, width) + 10;
			}
		}
		return y - start;
	}

	private static string[] _order;

	private static double _shownAt;

	private static PipeGame PipeGameFor(string key)
	{
		return key switch
		{
			GamePlays.Chess => Chess.Game,
			GamePlays.TetrisVersus => TetrisBattle.Game,
			GamePlays.TicTacToe => TicTacToe.Game,
			_ => null
		};
	}

	private static float _playersScroll;

	private static string _playersDrag;

	private static float _playersDragFrom;

	/// <summary>
	/// The IsaClient players here, each with a button per game; three rows show, more scroll inside the box (the wheel
	/// or its bar) so the games stay close.
	/// </summary>
	private static int DrawPlayers(SpriteBatch sb, int x, int y, int width)
	{
		HubKit.Text(sb, "IsaClient players here", x, y, Theme.Muted, 0.72f);
		if (!GameLobby.CanPlay)
		{
			string why = !PipeNet.Enabled ? "The pipe is off: turn it on with .pipe on to play with others." : "Join a server to play with other IsaClient players. The computer is always here.";
			Rectangle note = new Rectangle(x, y + 22, width, 40);
			HubKit.Box(sb, note, Theme.Card * HubPrefs.Opacity);
			HubKit.Text(sb, why, note.X + 14, note.Y + 12, Theme.Muted, 0.7f, width - 28);
			return note.Bottom;
		}
		List<int> players = GameLobby.Players().ToList();
		HubKit.Chip(sb, players.Count.ToString(), x + (int)HubKit.Measure(IsaClient.Languages.L10n.T("IsaClient players here"), 0.72f) + 8, y - 2, Color.Lerp(Theme.Slot, new Color(110, 230, 110), 0.25f), Color.White, 0.66f);
		y += 22;
		if (players.Count == 0)
		{
			Rectangle note = new Rectangle(x, y, width, 40);
			HubKit.Box(sb, note, Theme.Card * HubPrefs.Opacity);
			HubKit.Text(sb, "Nobody else with IsaClient on this server yet.", note.X + 14, note.Y + 12, Theme.Muted, 0.72f, width - 28);
			return note.Bottom;
		}
		const int rowH = 44;
		const int gap = 5;
		int shown = Math.Min(3, players.Count);
		bool scrolls = players.Count > 3;
		Rectangle list = new Rectangle(x, y, width, shown * (rowH + gap) - gap);
		float content = players.Count * (rowH + gap) - gap;
		int rowW = scrolls ? (width - 16) : width;
		if (scrolls)
		{
			HubInput.InnerScroll = list;
			if (HubInput.Wheel != 0 && HubInput.In(list))
			{
				_playersScroll -= HubInput.Wheel / 2f;
				HubInput.Wheel = 0;
			}
			_playersScroll = MathHelper.Clamp(_playersScroll, 0f, content - list.Height);
			_playersScroll = HubKit.Scrollbar(sb, "games:playersbar", new Rectangle(list.Right - 10, list.Y + 2, 10, list.Height - 4), _playersScroll, content, list.Height, ref _playersDrag, ref _playersDragFrom);
		}
		else
		{
			_playersScroll = 0f;
		}
		// Clip to the box, inside the page's own clip (put back after).
		bool outer = HubInput.Clipping;
		Rectangle outerClip = HubInput.Clip;
		HubInput.BeginClip(sb, outer ? Rectangle.Intersect(list, outerClip) : list);
		bool invitedSomeone = GameLobby.Outgoing != null && GameLobby.Outgoing.To != PipeNet.Everyone;
		for (int i = 0; i < players.Count; i++)
		{
			int player = players[i];
			Rectangle r = new Rectangle(x, list.Y + i * (rowH + gap) - (int)_playersScroll, rowW, rowH);
			if (r.Bottom < list.Y || r.Y > list.Bottom)
			{
				continue;
			}
			HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
			Rectangle face = new Rectangle(r.X + 8, r.Y + 6, 32, 32);
			HubKit.Fill(sb, face, Color.Black * 0.22f);
			HubKit.Head(sb, Main.player[player], new Vector2(face.Center.X, face.Center.Y - 1), 0.72f);
			int buttons = GameLobby.Games.Count;
			int bw = Math.Min(132, (rowW - 220) / Math.Max(1, buttons) - 4);
			HubKit.RawText(sb, GameLobby.Name(player), r.X + 50, r.Y + 6, Color.White, 0.8f, rowW - 60 - buttons * (bw + 4));
			// Where they stand on the global board, when we know.
			ulong id = IsaClient.Identity.ClientIdentity.Of(player);
			int rank = (id != 0) ? Scores.RankOf(Scores.Global, id) : 0;
			string sub = (rank > 0) ? IsaClient.Languages.L10n.F("#{0} global", rank) : IsaClient.Languages.L10n.T("IsaClient");
			HubKit.RawText(sb, sub, r.X + 50, r.Y + 25, (rank > 0) ? Tetris.Medal(rank - 1) : Theme.Muted, 0.6f);
			int bx = r.Right - 6 - buttons * (bw + 4) + 4;
			foreach (PipeGame game in GameLobby.Games)
			{
				bool thisOne = GameLobby.Outgoing != null && GameLobby.Outgoing.To == player && GameLobby.Outgoing.Game == game;
				if (HubKit.Button(sb, "games:invite:" + game.Id + ":" + player, new Rectangle(bx, r.Y + 7, bw, 30), thisOne ? "Invited..." : game.Name, Theme.Slot, IsaClient.Languages.L10n.F("Invite to {0}", IsaClient.Languages.L10n.T(game.Name)), !invitedSomeone && !GameLobby.InOnlineMatch, HubIcon.Game(game)))
				{
					GameLobby.InvitePlayer(game, player);
				}
				bx += bw + 4;
			}
		}
		HubInput.EndClip(sb);
		if (outer)
		{
			HubInput.BeginClip(sb, outerClip);
		}
		// More below or above: a soft fade at the edge.
		if (scrolls && _playersScroll < content - list.Height - 1f)
		{
			HubKit.Fill(sb, new Rectangle(list.X, list.Bottom - 2, rowW, 2), Theme.Accent * 0.35f);
		}
		return list.Bottom;
	}

	/// <summary>The match you're in: the rival, the score, and the board's buttons.</summary>
	private static int DrawMatch(SpriteBatch sb, GameMatch match, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 60);
		HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.Accent, 0.18f) * HubPrefs.Opacity);
		HubIcon.Game(match.Game).Draw(sb, new Rectangle(r.X + 12, r.Y + 12, 36, 36), Color.White, 3f);
		HubKit.Text(sb, match.Game.Name, r.X + 58, r.Y + 9, Theme.Accent, 0.88f);
		string line = IsaClient.Languages.L10n.F("vs {0}  ·  You {1}, them {2}, draws {3}", match.OpponentName, match.MyWins, match.TheirWins, match.Draws);
		HubKit.RawText(sb, line, r.X + 58, r.Y + 33, Theme.Muted, 0.68f, width - 280);
		if (HubKit.Button(sb, "games:show", new Rectangle(r.Right - 206, r.Y + 15, 96, 30), "Board", Theme.Slot, "Show the game window"))
		{
			GameOverlay.Show();
		}
		if (HubKit.Button(sb, "games:leave", new Rectangle(r.Right - 104, r.Y + 15, 94, 30), match.VsBot ? "Close" : "Leave", Theme.Danger * 0.8f, match.VsBot ? "End the game against the computer" : "Leave the game (the other player is told)"))
		{
			GameLobby.LeaveMatch();
		}
		return r.Bottom;
	}

	/// <summary>A game: a preview on the left; its name, text, options and the play buttons on the right.</summary>
	private static int DrawGameCard(SpriteBatch sb, PipeGame game, int x, int y, int width)
	{
		const int preview = 128;
		int textX = x + preview + 24;
		int textW = width - preview - 36;
		string[] lines = HubKit.Wrap(IsaClient.Languages.L10n.T(game.Description), textW, 0.66f, 3);
		int height = Math.Max(preview + 20, 34 + lines.Length * 15 + 8 + 2 * 30 + 6 + 34 + 10);
		Rectangle r = new Rectangle(x, y, width, height);
		HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
		Rectangle art = new Rectangle(r.X + 10, r.Y + (height - preview) / 2, preview, preview);
		HubKit.Box(sb, new Rectangle(art.X - 4, art.Y - 4, art.Width + 8, art.Height + 8), Color.Black * 0.35f);
		if (game == Chess.Game)
		{
			ChessPreview(sb, art);
		}
		else if (game == TetrisBattle.Game)
		{
			TetrisPreview(sb, art);
		}
		else
		{
			TicTacToePreview(sb, art);
		}
		HubIcon.Game(game).Draw(sb, new Rectangle(textX, r.Y + 8, 22, 22), Color.White, 3f);
		HubKit.Text(sb, game.Name, textX + 28, r.Y + 9, Color.White, 0.95f);
		int ly = r.Y + 34;
		foreach (string line in lines)
		{
			HubKit.RawText(sb, line, textX, ly, Theme.Muted, 0.66f);
			ly += 15;
		}
		ly += 6;
		if (game.First != null)
		{
			ly = Choices(sb, "card:" + game.Id + ":first", game.First, textX, ly, textW) + 4;
		}
		if (game.BotLevel != null)
		{
			ly = Choices(sb, "card:" + game.Id + ":bot", game.BotLevel, textX, ly, textW) + 4;
		}
		ly += 2;
		int bw = Math.Min(190, (textW - 8) / 2);
		Rectangle invite = new Rectangle(textX + bw + 8, ly, bw, 32);
		if (game.BotLevel == null)
		{
			// Only against players.
			invite = new Rectangle(textX, ly, bw, 32);
		}
		else if (HubKit.Button(sb, "games:bot:" + game.Id, new Rectangle(textX, ly, bw, 32), "Play vs computer", Green, IsaClient.Languages.L10n.F("A game against the computer ({0}). Works anywhere, even alone.", game.BotLevel?.Current ?? ""), !Main.gameMenu && !GameLobby.InOnlineMatch, HubIcon.Item(I.CyborgHelmet)))
		{
			GameLobby.PlayComputer(game);
		}
		bool waiting = GameLobby.Outgoing != null && GameLobby.Outgoing.Game == game;
		if (waiting)
		{
			if (HubKit.Button(sb, "games:cancel:" + game.Id, invite, "Cancel invitation", Theme.Slot, null))
			{
				GameLobby.CancelInvite();
			}
		}
		else
		{
			string tip = GameLobby.CanPlay ? "Every IsaClient player here gets the invitation; the first who accepts plays." : (!PipeNet.Enabled ? "The pipe is off: turn it on with .pipe on." : "Join a server: games with others are played online.");
			if (HubKit.Button(sb, "games:all:" + game.Id, invite, "Invite everyone", Theme.Slot, tip, GameLobby.CanPlay && !GameLobby.InOnlineMatch, HubIcon.Item(I.Megaphone)))
			{
				GameLobby.InvitePlayer(game, PipeNet.Everyone);
			}
		}
		return r.Bottom;
	}

	/// <summary>An option as a row of small buttons (the chosen one lit); returns the bottom.</summary>
	private static int Choices(SpriteBatch sb, string id, GameOption option, int x, int y, int width)
	{
		string label = IsaClient.Languages.L10n.T(option.Name);
		HubKit.RawText(sb, label, x, y + 6, Theme.Muted, 0.66f);
		int bx = x + (int)Math.Max(96f, HubKit.Measure(label, 0.66f) + 12f);
		int current = option.Get();
		for (int i = 0; i < option.Values.Length; i++)
		{
			string text = IsaClient.Languages.L10n.T(option.Labels[i]);
			int w = (int)HubKit.Measure(text, 0.78f) + 22;
			if (bx + w > x + width)
			{
				break;
			}
			if (HubKit.Button(sb, id + ":" + i, new Rectangle(bx, y, w, 26), text, (current == option.Values[i]) ? On : Theme.Slot, option.Hint))
			{
				option.Set(option.Values[i]);
				ClientSettings.Save();
			}
			bx += w + 4;
		}
		return y + 26;
	}

	#region Tetris and scores

	private static readonly Color Gold = new Color(255, 214, 80);

	/// <summary>Tetris: a falling-blocks preview, your best and rank, play and the board.</summary>
	private static int DrawTetrisCard(SpriteBatch sb, int x, int y, int width)
	{
		const int preview = 128;
		Rectangle r = new Rectangle(x, y, width, preview + 48);
		HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
		Rectangle art = new Rectangle(r.X + 10, r.Y + 24, preview, preview);
		HubKit.Box(sb, new Rectangle(art.X - 4, art.Y - 4, art.Width + 8, art.Height + 8), Color.Black * 0.35f);
		TetrisPreview(sb, art);
		int textX = x + preview + 24;
		int textW = width - preview - 36;
		HubIcon.Item(I.AmethystGemsparkBlock).Draw(sb, new Rectangle(textX, r.Y + 8, 22, 22), Color.White);
		HubKit.Text(sb, "Tetris", textX + 28, r.Y + 9, Color.White, 0.95f);
		string[] lines = HubKit.Wrap(IsaClient.Languages.L10n.T("Gemspark blocks, hold, ghost piece and levels. Your best score joins a board shared by every IsaClient player."), textW, 0.66f, 2);
		int ly = r.Y + 34;
		foreach (string line in lines)
		{
			HubKit.RawText(sb, line, textX, ly, Theme.Muted, 0.66f);
			ly += 15;
		}
		ly += 6;
		int best = Scores.Best(Scores.Tetris);
		int total = Scores.Ranking(Scores.Tetris).Count;
		if (best > 0)
		{
			int rank = Scores.RankOf(Scores.Tetris, IsaClient.Identity.ClientIdentity.Id);
			Rectangle chip = HubKit.Chip(sb, "#" + rank, textX, ly, Tetris.Medal(rank - 1) * 0.35f, Tetris.Medal(rank - 1), 0.75f);
			HubKit.RawText(sb, IsaClient.Languages.L10n.F("Your best: {0}  ·  of {1} players", best.ToString("N0"), total), chip.Right + 8, ly + 3, Color.White, 0.7f, textW - chip.Width - 8);
		}
		else
		{
			HubKit.Text(sb, (total > 0) ? IsaClient.Languages.L10n.F("{0} players on the board. Your turn!", total) : IsaClient.Languages.L10n.T("Nobody on the board yet: be the first."), textX, ly + 3, Theme.Muted, 0.7f, textW);
		}
		ly += 28;
		ly = Choices(sb, "tetris:speed", Tetris.SpeedOption, textX, ly, textW) + 6;
		int bw = Math.Min(190, (textW - 8) / 2);
		if (HubKit.Button(sb, "games:tetris", new Rectangle(textX, ly, bw, 32), Tetris.IsOpen ? "Show Tetris" : "Play Tetris", Green, "Opens the Tetris window. Works anywhere, even alone.", !Main.gameMenu, HubIcon.Item(I.AmethystGemsparkBlock)))
		{
			Tetris.Open();
		}
		if (HubKit.Button(sb, "games:scores", new Rectangle(textX + bw + 8, ly, bw, 32), "Scores", Theme.Slot, "The board of every IsaClient player you've met", true, HubIcon.Item(I.GoldCrown)))
		{
			ClientHub.OpenSection("games", 1);
		}
		return r.Bottom;
	}

	/// <summary>Snake: a snake going round, your best and rank, its options, play and the board.</summary>
	private static int DrawSnakeCard(SpriteBatch sb, int x, int y, int width)
	{
		const int preview = 128;
		Rectangle r = new Rectangle(x, y, width, 240);
		HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
		Rectangle art = new Rectangle(r.X + 10, r.Y + 24, preview, 192);
		HubKit.Box(sb, new Rectangle(art.X - 4, art.Y - 4, art.Width + 8, art.Height + 8), Color.Black * 0.35f);
		Snake.DrawPreview(sb, art, Sharp);
		HubInput.Restart(sb, HubInput.Clipping);
		int textX = x + preview + 24;
		int textW = width - preview - 36;
		HubIcon.Item(I.Apple).Draw(sb, new Rectangle(textX, r.Y + 8, 22, 22), Color.White);
		HubKit.Text(sb, "Snake", textX + 28, r.Y + 9, Color.White, 0.95f);
		string[] lines = HubKit.Wrap(IsaClient.Languages.L10n.T("The snake of the old phones: bricks, Terraria fruit and a life fruit now and then. Your best score joins the shared board."), textW, 0.66f, 2);
		int ly = r.Y + 34;
		foreach (string line in lines)
		{
			HubKit.RawText(sb, line, textX, ly, Theme.Muted, 0.66f);
			ly += 15;
		}
		ly += 6;
		int best = Scores.Best(Scores.Snake);
		int total = Scores.Ranking(Scores.Snake).Count;
		if (best > 0)
		{
			int rank = Scores.RankOf(Scores.Snake, IsaClient.Identity.ClientIdentity.Id);
			Rectangle chip = HubKit.Chip(sb, "#" + rank, textX, ly, Tetris.Medal(rank - 1) * 0.35f, Tetris.Medal(rank - 1), 0.75f);
			HubKit.RawText(sb, IsaClient.Languages.L10n.F("Your best: {0}  ·  of {1} players", best.ToString("N0"), total), chip.Right + 8, ly + 3, Color.White, 0.7f, textW - chip.Width - 8);
		}
		else
		{
			HubKit.Text(sb, (total > 0) ? IsaClient.Languages.L10n.F("{0} players on the board. Your turn!", total) : IsaClient.Languages.L10n.T("Nobody on the board yet: be the first."), textX, ly + 3, Theme.Muted, 0.7f, textW);
		}
		ly += 28;
		ly = Choices(sb, "snake:speed", Snake.SpeedOption, textX, ly, textW) + 6;
		ly = Choices(sb, "snake:walls", Snake.WallsOption, textX, ly, textW) + 6;
		ly = Choices(sb, "snake:skin", Snake.SkinOption, textX, ly, textW) + 6;
		int bw = Math.Min(190, (textW - 8) / 2);
		if (HubKit.Button(sb, "games:snake", new Rectangle(textX, ly, bw, 32), Snake.IsOpen ? "Show Snake" : "Play Snake", Green, "Opens the Snake window. Works anywhere, even alone.", !Main.gameMenu, HubIcon.Item(I.Apple)))
		{
			Snake.Open();
		}
		if (HubKit.Button(sb, "games:snakescores", new Rectangle(textX + bw + 8, ly, bw, 32), "Scores", Theme.Slot, "The board of every IsaClient player you've met", true, HubIcon.Item(I.GoldCrown)))
		{
			_board = Scores.Snake;
			ClientHub.OpenSection("games", 1);
		}
		return Math.Max(r.Bottom, ly + 42);
	}

	/// <summary>The Game Boy Advance (your own games; Pokémon as your Terraria character): a little handheld, play and the games.</summary>
	private static int DrawPokemonCard(SpriteBatch sb, int x, int y, int width)
	{
		const int preview = 128;
		Rectangle r = new Rectangle(x, y, width, 196);
		HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
		// the handheld: a purple shell, a screen with the trainer walking (or a Poké Ball)
		Rectangle art = new Rectangle(r.X + 10, r.Y + 30, preview, 136);
		HubKit.Box(sb, new Rectangle(art.X - 4, art.Y - 4, art.Width + 8, art.Height + 8), Color.Black * 0.35f);
		HubKit.Fill(sb, art, new Color(58, 46, 120));
		HubKit.Fill(sb, new Rectangle(art.X, art.Y, art.Width, 18), new Color(92, 78, 170));
		Rectangle screen = new Rectangle(art.X + 14, art.Y + 22, art.Width - 28, 72);
		HubKit.Fill(sb, new Rectangle(screen.X - 4, screen.Y - 4, screen.Width + 8, screen.Height + 8), new Color(24, 22, 36));
		HubKit.Fill(sb, screen, new Color(112, 176, 112));
		IsaClient.Gba.Trainer.TrainerResult trainer = IsaClient.Gba.PokemonGame.Trainer;
		if (trainer != null)
		{
			IsaClient.Gba.TrainerArt.Walk(sb, trainer, new Rectangle(screen.X, screen.Y + 4, screen.Width, screen.Height - 6), (int)(GameArt.Now / 1.6) % 4, Sharp, s => HubInput.Restart(s, HubInput.Clipping));
		}
		else
		{
			IsaClient.Gba.PixelArt.PokeBall(sb, new Rectangle(screen.Center.X - 20, screen.Center.Y - 20, 40, 40));
		}
		// D-pad and the A/B buttons
		int cy = art.Bottom - 22;
		HubKit.Fill(sb, new Rectangle(art.X + 12, cy - 4, 24, 8), new Color(30, 28, 44));
		HubKit.Fill(sb, new Rectangle(art.X + 20, cy - 12, 8, 24), new Color(30, 28, 44));
		HubKit.Fill(sb, new Rectangle(art.Right - 40, cy - 2, 12, 12), new Color(150, 36, 52));
		HubKit.Fill(sb, new Rectangle(art.Right - 24, cy - 10, 12, 12), new Color(150, 36, 52));
		int textX = x + preview + 24;
		int textW = width - preview - 36;
		IsaClient.Gba.PixelArt.PokeBall(sb, new Rectangle(textX, r.Y + 8, 22, 22));
		HubKit.Text(sb, "Game Boy Advance & Nintendo DS", textX + 28, r.Y + 9, Color.White, 0.95f, textW - 28);
		string[] lines = HubKit.Wrap(IsaClient.Languages.L10n.T("A Game Boy Advance & Nintendo DS inside Terraria: bring your own games (.gba, .nds or .zip). In Pokémon FireRed, LeafGreen and Emerald you play as your Terraria character. Saves, save slots and it goes on where you left it. Just for fun: no scores."), textW, 0.66f, 4);
		int ly = r.Y + 34;
		foreach (string line in lines)
		{
			HubKit.RawText(sb, line, textX, ly, Theme.Muted, 0.66f);
			ly += 15;
		}
		ly += 8;
		int gameCount = IsaClient.Gba.GbaConfig.Games.Count;
		string status = IsaClient.Gba.PokemonGame.HasRom ? IsaClient.Gba.PokemonGame.RomName : IsaClient.Languages.L10n.T("No games added yet");
		if (gameCount > 1)
		{
			status += "  ·  " + IsaClient.Languages.L10n.F("{0} games", gameCount);
		}
		HubKit.Chip(sb, status, textX, ly, Color.Lerp(Theme.Slot, new Color(220, 60, 60), 0.25f), Color.White, 0.66f);
		ly += 30;
		int bw = Math.Min(190, (textW - 8) / 2);
		if (HubKit.Button(sb, "games:pokemon", new Rectangle(textX, ly, bw, 32), IsaClient.Gba.PokemonWindow.IsOpen ? "Show the console" : "Play", Green, "Opens the Game Boy Advance / DS window. Works anywhere, even alone.", !Main.gameMenu, HubIcon.Item(I.GoodieBag)))
		{
			IsaClient.Gba.PokemonWindow.Open();
		}
		if (HubKit.Button(sb, "games:pokemontrainer", new Rectangle(textX + bw + 8, ly, bw, 32), "Games...", Theme.Slot, "Your Game Boy Advance games: switch or add more", !Main.gameMenu, HubIcon.Item(I.FamiliarWig)))
		{
			IsaClient.Gba.PokemonWindow.OpenGames();
		}
		return Math.Max(r.Bottom, ly + 42);
	}

	/// <summary>The PSP (your own games; the emulator comes the first time): a little handheld with the game's picture, play and the games.</summary>
	private static int DrawPspCard(SpriteBatch sb, int x, int y, int width)
	{
		const int preview = 128;
		Rectangle r = new Rectangle(x, y, width, 196);
		HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
		IsaClient.Psp.PspConfig.EnsureLoaded();
		IsaClient.Psp.PspEntry game = IsaClient.Psp.PspGame.Current;
		// the handheld: a long black body, the wide screen in the middle
		Rectangle art = new Rectangle(r.X + 10, r.Y + 58, preview, 78);
		HubKit.Box(sb, new Rectangle(art.X - 4, art.Y - 4, art.Width + 8, art.Height + 8), Color.Black * 0.35f);
		HubKit.Fill(sb, art, new Color(30, 31, 38));
		HubKit.Fill(sb, new Rectangle(art.X, art.Y, art.Width, 6), new Color(62, 64, 76));
		Rectangle screen = new Rectangle(art.X + 24, art.Y + 14, art.Width - 48, 46);
		HubKit.Fill(sb, new Rectangle(screen.X - 2, screen.Y - 2, screen.Width + 4, screen.Height + 4), Color.Black);
		Microsoft.Xna.Framework.Graphics.Texture2D icon = (game?.Icon != null) ? IsaClient.Gba.GbaSaves.Picture(game.Icon) : null;
		if (icon != null)
		{
			sb.Draw(icon, screen, Color.White);
		}
		else
		{
			GameArt.Gradient(sb, screen, new Color(22, 26, 70), new Color(66, 34, 104));
		}
		// the D-pad and the stick, the four buttons
		int cy = art.Y + 30;
		HubKit.Fill(sb, new Rectangle(art.X + 6, cy - 2, 12, 4), new Color(70, 72, 84));
		HubKit.Fill(sb, new Rectangle(art.X + 10, cy - 6, 4, 12), new Color(70, 72, 84));
		HubKit.Fill(sb, new Rectangle(art.X + 8, cy + 20, 8, 8), new Color(70, 72, 84));
		HubKit.Fill(sb, new Rectangle(art.Right - 14, cy - 8, 4, 4), new Color(90, 210, 170));
		HubKit.Fill(sb, new Rectangle(art.Right - 9, cy - 3, 4, 4), new Color(236, 96, 96));
		HubKit.Fill(sb, new Rectangle(art.Right - 14, cy + 2, 4, 4), new Color(130, 160, 240));
		HubKit.Fill(sb, new Rectangle(art.Right - 19, cy - 3, 4, 4), new Color(232, 130, 190));
		int textX = x + preview + 24;
		int textW = width - preview - 36;
		IsaClient.Psp.PspWindow.Handheld(sb, new Rectangle(textX, r.Y + 13, 26, 13));
		HubKit.Text(sb, "PlayStation Portable", textX + 32, r.Y + 9, Color.White, 0.95f);
		string[] lines = HubKit.Wrap(IsaClient.Languages.L10n.T("A PSP inside Terraria: bring your own games (.iso, .cso, .chd, .pbp, or a .zip or .7z with them). The emulator is downloaded the first time you play. Saves, save slots and it goes on where you left it. Just for fun: no scores."), textW, 0.66f, 3);
		int ly = r.Y + 34;
		foreach (string line in lines)
		{
			HubKit.RawText(sb, line, textX, ly, Theme.Muted, 0.66f);
			ly += 15;
		}
		ly += 8;
		int gameCount = IsaClient.Psp.PspConfig.Games.Count;
		string status = (game != null) ? game.Title : IsaClient.Languages.L10n.T("No games added yet");
		if (gameCount > 1)
		{
			status += "  ·  " + IsaClient.Languages.L10n.F("{0} games", gameCount);
		}
		HubKit.Chip(sb, status, textX, ly, Color.Lerp(Theme.Slot, new Color(60, 90, 200), 0.3f), Color.White, 0.66f);
		ly += 30;
		int bw = Math.Min(190, (textW - 8) / 2);
		if (HubKit.Button(sb, "games:psp", new Rectangle(textX, ly, bw, 32), IsaClient.Psp.PspWindow.IsOpen ? "Show the console" : "Play", Green, "Opens the PSP window. Works anywhere, even alone.", !Main.gameMenu, HubIcon.Item(I.GoodieBag)))
		{
			IsaClient.Psp.PspWindow.Open();
		}
		if (HubKit.Button(sb, "games:pspgames", new Rectangle(textX + bw + 8, ly, bw, 32), "Games...", Theme.Slot, "Your PSP games: switch or add more", !Main.gameMenu, HubIcon.Item(I.FamiliarWig)))
		{
			IsaClient.Psp.PspWindow.OpenGames();
		}
		return Math.Max(r.Bottom, ly + 42);
	}

	/// <summary>The casino (Poker and Blackjack with the Moon Lord dealing): a little table, your chips and ranks, play and the boards.</summary>
	private static int DrawCasinoCard(SpriteBatch sb, int x, int y, int width)
	{
		const int preview = 128;
		Rectangle r = new Rectangle(x, y, width, 196);
		HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
		Rectangle art = new Rectangle(r.X + 10, r.Y + 30, preview, 136);
		HubKit.Box(sb, new Rectangle(art.X - 4, art.Y - 4, art.Width + 8, art.Height + 8), Color.Black * 0.35f);
		HubKit.Fill(sb, art, new Color(46, 26, 22));
		HubKit.Fill(sb, new Rectangle(art.X, art.Y + 70, art.Width, art.Height - 70), new Color(92, 18, 30));
		Vector2 core = new Vector2(art.Center.X, art.Y + 84);
		float wave = (float)Math.Sin(GameArt.Now * 3.0);
		IsaClient.Casino.CasinoArt.MoonLord(sb, core, 0.12f, Vector2.Zero, new Vector2(30f + wave * 30f, -250f), 0, 0, new Vector2(0f, 1f));
		HubKit.Fill(sb, new Rectangle(art.X, art.Bottom - 38, art.Width, 4), new Color(70, 38, 22));
		HubKit.Fill(sb, new Rectangle(art.X, art.Bottom - 34, art.Width, 34), new Color(40, 150, 92));
		Sharp(sb);
		IsaClient.Casino.CasinoArt.Card(sb, new Vector2(art.Center.X - 42, art.Bottom - 30), 0.55f, 12, true);
		IsaClient.Casino.CasinoArt.Card(sb, new Vector2(art.Center.X - 20, art.Bottom - 29), 0.55f, 24, true);
		IsaClient.Casino.CasinoArt.Chips(sb, new Vector2(art.Center.X + 34, art.Bottom - 6), 630, 0.9f, 4);
		HubInput.Restart(sb, HubInput.Clipping);
		int textX = x + preview + 24;
		int textW = width - preview - 36;
		HubIcon.Item(IsaClient.Casino.CasinoWindow.IconItem).Draw(sb, new Rectangle(textX, r.Y + 8, 22, 22), Color.White);
		HubKit.Text(sb, "Casino", textX + 28, r.Y + 9, Color.White, 0.95f);
		string[] lines = HubKit.Wrap(IsaClient.Languages.L10n.T("Poker and Blackjack dealt by the Moon Lord, and a slot machine, Roulette and horse races. Alone or with the IsaClient players here. Your chips come from your rank in the other games; what you win or lose counts on the boards."), textW, 0.66f, 3);
		int ly = r.Y + 34;
		foreach (string line in lines)
		{
			HubKit.RawText(sb, line, textX, ly, Theme.Muted, 0.66f);
			ly += 15;
		}
		ly += 8;
		IsaClient.Casino.CasinoWallet.Load();
		HubKit.Chip(sb, IsaClient.Languages.L10n.F("{0} chips", IsaClient.Casino.CasinoWallet.Total.ToString("N0")), textX, ly, Color.Lerp(Theme.Slot, Gold, 0.3f), Gold, 0.66f);
		int tables = IsaClient.Casino.CasinoNet.Ads.Count;
		if (tables > 0)
		{
			HubKit.Chip(sb, IsaClient.Languages.L10n.F("{0} open tables here", tables), textX + 160, ly, Color.Lerp(Theme.Slot, new Color(110, 230, 110), 0.25f), Color.White, 0.66f);
		}
		ly += 30;
		int bw = Math.Min(170, (textW - 16) / 3);
		if (HubKit.Button(sb, "games:casino", new Rectangle(textX, ly, bw, 32), IsaClient.Casino.CasinoNet.AtTable ? "Back to the table" : "Cards", Green, "Poker and Blackjack. Works anywhere, even alone.", !Main.gameMenu, HubIcon.Item(IsaClient.Casino.CasinoWindow.IconItem)))
		{
			IsaClient.Casino.CasinoWindow.OpenCards();
		}
		if (HubKit.Button(sb, "games:casinobets", new Rectangle(textX + bw + 8, ly, bw, 32), "Bets", Green, "Slot machine, Roulette and horse races, with the same chips", !Main.gameMenu, HubIcon.Item(IsaClient.Casino.BetsWindow.BetsItem)))
		{
			IsaClient.Casino.BetsWindow.OpenLobby();
		}
		if (HubKit.Button(sb, "games:casinoscores", new Rectangle(textX + (bw + 8) * 2, ly, bw, 32), "Scores", Theme.Slot, "The casino's boards", true, HubIcon.Item(I.GoldCrown)))
		{
			ShowBoard(Scores.Poker);
		}
		return Math.Max(r.Bottom, ly + 42);
	}

	/// <summary>Opens the Scores page on a board.</summary>
	public static void ShowBoard(byte board)
	{
		_board = board;
		ClientHub.OpenSection("games", 1);
	}

	/// <summary>Pieces fall into a little well, again and again.</summary>
	private static void TetrisPreview(SpriteBatch sb, Rectangle art)
	{
		HubKit.Fill(sb, art, new Color(18, 20, 44));
		int cell = art.Width / 8;
		// A stack at the bottom, and a T dropping row by row into the gap, then the line flashes.
		int[] stack = { 7, 7, 0, 3, 3, 1, 1, 1, 5, 5, 1, 1, 4, 4, 6, 2 };
		ushort[] tiles = { 0, Terraria.ID.TileID.DiamondGemspark, Terraria.ID.TileID.TopazGemspark, Terraria.ID.TileID.AmethystGemspark, Terraria.ID.TileID.EmeraldGemspark, Terraria.ID.TileID.RubyGemspark, Terraria.ID.TileID.SapphireGemspark, Terraria.ID.TileID.AmberGemspark };
		float t = (float)(GameArt.Now % 2.4 / 2.4);
		Sharp(sb);
		void Tile(int kind, int col, float row)
		{
			Main.instance.LoadTiles(tiles[kind]);
			sb.Draw(Terraria.GameContent.TextureAssets.Tile[tiles[kind]].Value, new Rectangle(art.X + col * cell, art.Y + (int)(row * cell), cell, cell), new Rectangle(162, 54, 16, 16), Color.White);
		}
		for (int i = 0; i < stack.Length; i++)
		{
			if (stack[i] != 0)
			{
				Tile(stack[i], i % 8, 6 + i / 8);
			}
		}
		int fall = Math.Min(5, (int)(t / 0.65f * 6f));
		Tile(3, 1, fall);
		Tile(3, 2, fall);
		Tile(3, 3, fall);
		Tile(3, 2, fall + 1);
		HubInput.Restart(sb, HubInput.Clipping);
		if (fall == 5 && t > 0.72f)
		{
			HubKit.Fill(sb, new Rectangle(art.X, art.Y + 6 * cell, art.Width, cell), Color.White * (0.7f * (1f - (t - 0.72f) / 0.28f)));
		}
	}

	/// <summary>The board shown on the Scores page.</summary>
	private static byte _board = Scores.Global;

	private static HubIcon BoardIcon(byte board)
	{
		switch (board)
		{
		case Scores.Tetris:
			return HubIcon.Item(I.AmethystGemsparkBlock);
		case Scores.Chess:
			return HubIcon.Game(Chess.Game);
		case Scores.TicTacToe:
			return HubIcon.Game(TicTacToe.Game);
		case Scores.TetrisVersus:
			return HubIcon.Game(TetrisBattle.Game);
		case Scores.Snake:
			return HubIcon.Item(I.Apple);
		case Scores.Poker:
			return HubIcon.Item(IsaClient.Casino.CasinoWindow.IconItem);
		case Scores.Blackjack:
			return HubIcon.Item(Terraria.ID.ItemID.PlatinumCoin);
		case Scores.Bets:
			return HubIcon.Item(IsaClient.Casino.BetsWindow.BetsItem);
		default:
			return HubIcon.Item(I.GoldCrown);
		}
	}

	/// <summary>What a board counts, under its title.</summary>
	private static string BoardHint(byte board)
	{
		switch (board)
		{
		case Scores.Tetris:
			return IsaClient.Languages.L10n.T("Your best Tetris score.");
		case Scores.Chess:
			return IsaClient.Languages.L10n.F("Elo rating from games against players; everyone starts at {0}. The computer doesn't count.", Scores.StartElo);
		case Scores.TicTacToe:
			return IsaClient.Languages.L10n.T("Rank points from games against players: 3 a win, 1 a draw. The computer doesn't count.");
		case Scores.TetrisVersus:
			return IsaClient.Languages.L10n.T("Rank points from Tetris against players: 3 a win.");
		case Scores.Snake:
			return IsaClient.Languages.L10n.T("Your best Snake score. Faster games and solid walls give more a fruit.");
		case Scores.Poker:
			return IsaClient.Languages.L10n.T("Chips won (or lost) at Poker, with the computer and with players. Hands won, lost and split.");
		case Scores.Blackjack:
			return IsaClient.Languages.L10n.T("Chips won (or lost) at Blackjack. Rounds won, lost and pushed.");
		case Scores.Bets:
			return IsaClient.Languages.L10n.T("Chips won (or lost) on the slot machine, the Roulette and the races.");
		default:
			return IsaClient.Languages.L10n.T("Everything together: Tetris best / 100, Snake best / 5, Versus points x5, Tic Tac Toe points x4, Chess (rating - 1000) x1.5 + 3 a game, and chips won at the casino / 10. Games with players weigh more, Chess the most.");
		}
	}

	private static string Value(byte board, ScoreEntry e)
	{
		if (board == Scores.Poker || board == Scores.Blackjack || board == Scores.Bets)
		{
			long chips = (long)e.Score - Scores.CasinoBase;
			return ((chips > 0) ? "+" : "") + chips.ToString("N0");
		}
		return (board == Scores.TicTacToe || board == Scores.TetrisVersus) ? IsaClient.Languages.L10n.F("{0} pts", e.Score) : e.Score.ToString("N0");
	}

	/// <summary>The Scores page: a tab per board (Global first), your place on top, then everyone.</summary>
	public static int DrawScores(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		// Tabs.
		int tx = x;
		foreach (byte board in Scores.Boards)
		{
			string name = Scores.GameName(board);
			int w = (int)HubKit.Measure(name, 0.78f) + 52;
			// the tabs that don't fit go on a second row
			if (tx > x && tx + w > x + width)
			{
				tx = x;
				y += 40;
			}
			bool on = board == _board;
			if (HubKit.Button(sb, "scores:tab:" + board, new Rectangle(tx, y, w, 34), name, on ? Color.Lerp(Theme.Slot, Gold, 0.4f) : Theme.Slot, null, true, BoardIcon(board)))
			{
				_board = board;
			}
			if (on)
			{
				HubKit.Fill(sb, new Rectangle(tx + 10, y + 32, w - 20, 2), Gold);
			}
			tx += w + 6;
		}
		y += 44;
		List<ScoreEntry> ranking = Scores.Ranking(_board);
		Dictionary<ulong, int> moves = Scores.Moves(_board);
		ulong me = IsaClient.Identity.ClientIdentity.Id;
		int myRank = ranking.FindIndex(e => e.Id == me) + 1;
		// Your card.
		Rectangle head = new Rectangle(x, y, width, 82);
		HubKit.Box(sb, head, Color.Lerp(Theme.Card, Gold, 0.12f) * HubPrefs.Opacity);
		BoardIcon(_board).Draw(sb, new Rectangle(head.X + 14, head.Y + 18, 44, 44), Color.White, 3f);
		HubKit.RawText(sb, Scores.GameName(_board), head.X + 70, head.Y + 9, Gold, 0.92f);
		if (myRank > 0)
		{
			HubKit.RawText(sb, IsaClient.Languages.L10n.F("You're #{0} of {1}  ·  {2}", myRank, ranking.Count, Value(_board, ranking[myRank - 1])), head.X + 70, head.Y + 32, Color.White, 0.78f, width - 250);
		}
		else
		{
			HubKit.Text(sb, (_board == Scores.Chess || _board == Scores.TicTacToe) ? "Play against a player to get on this board." : "Play a game to get on the board.", head.X + 70, head.Y + 32, Color.White, 0.78f, width - 250);
		}
		string[] hint = HubKit.Wrap(BoardHint(_board), width - 250, 0.58f, 2);
		for (int i = 0; i < hint.Length; i++)
		{
			HubKit.RawText(sb, hint[i], head.X + 70, head.Y + 52 + i * 13, Theme.Muted, 0.58f);
		}
		bool tetris = _board == Scores.Tetris || _board == Scores.Global;
		bool snake = _board == Scores.Snake;
		bool casino = _board == Scores.Poker || _board == Scores.Blackjack || _board == Scores.Bets;
		if (HubKit.Button(sb, "scores:play", new Rectangle(head.Right - 160, head.Y + 25, 146, 32), tetris ? "Play Tetris" : (snake ? "Play Snake" : (casino ? "Open the casino" : "Play games")), Green, null, !Main.gameMenu, tetris ? HubIcon.Item(I.AmethystGemsparkBlock) : BoardIcon(_board)))
		{
			if (casino)
			{
				IsaClient.Casino.CasinoWindow.Open();
			}
			else if (tetris)
			{
				Tetris.Open();
			}
			else if (snake)
			{
				Snake.Open();
			}
			else
			{
				ClientHub.OpenSection("games", 0);
			}
		}
		y = head.Bottom + 12;
		if (ranking.Count == 0)
		{
			HubKit.Text(sb, "Nobody on this board yet: be the first.", x + 4, y, Theme.Muted, 0.72f);
			return y + 26 - start;
		}
		HubKit.Text(sb, IsaClient.Languages.L10n.F("{0} players", ranking.Count), x, y, Theme.Muted, 0.72f);
		y += 22;
		int top = Math.Max(1, ranking[0].Score);
		for (int i = 0; i < ranking.Count; i++)
		{
			ScoreEntry e = ranking[i];
			bool mine = e.Id == me;
			bool podium = i < 3;
			Rectangle r = new Rectangle(x, y, width, podium ? 48 : 40);
			if (!HubInput.Visible(r))
			{
				y = r.Bottom + 5;
				continue;
			}
			Color back = mine ? Color.Lerp(Theme.Card, Theme.Accent, 0.25f) : (podium ? Color.Lerp(Theme.Card, Tetris.Medal(i), 0.08f) : Theme.Card);
			HubKit.Box(sb, r, back * HubPrefs.Opacity);
			int bar = (int)((r.Width - 8) * Math.Max(0.02f, (float)e.Score / top));
			HubKit.Fill(sb, new Rectangle(r.X + 4, r.Bottom - 5, bar, 2), Tetris.Medal(i) * 0.5f);
			// Place: a medal for the podium.
			Rectangle place = new Rectangle(r.X + 8, r.Y + (r.Height - 28) / 2, 34, 28);
			if (podium)
			{
				HubKit.Box(sb, place, Tetris.Medal(i) * 0.35f);
			}
			string rankText = (i + 1).ToString();
			HubKit.RawText(sb, rankText, place.X + (place.Width - HubKit.Measure(rankText, 0.82f)) / 2f, place.Y + 5, Tetris.Medal(i), 0.82f);
			// Their head: here now, or the last look we know.
			int mx = r.X + 50;
			Rectangle face = new Rectangle(mx, r.Y + (r.Height - 32) / 2, 32, 32);
			HubKit.Fill(sb, face, Color.Black * 0.22f);
			Player who = Scores.HeadOf(e);
			if (who != null)
			{
				HubKit.Head(sb, who, new Vector2(face.Center.X, face.Center.Y - 1), 0.72f);
			}
			else
			{
				HubIcon.Item(I.FamiliarWig).Draw(sb, new Rectangle(face.X + 6, face.Y + 6, 20, 20), Color.White * 0.6f);
			}
			mx = face.Right + 10;
			HubKit.RawText(sb, e.Name, mx, r.Y + (podium ? 7 : 5), mine ? Theme.Accent : Color.White, podium ? 0.86f : 0.8f, width - 380);
			// Movement since your last look: a little badge after the name.
			if (moves.TryGetValue(e.Id, out int moved) && moved != 0)
			{
				bool newcomer = moved == int.MaxValue;
				string arrow = newcomer ? IsaClient.Languages.L10n.T("new") : (((moved > 0) ? "▲ " : "▼ ") + Math.Abs(moved));
				Color color = newcomer ? new Color(140, 220, 255) : ((moved > 0) ? new Color(120, 220, 120) : new Color(255, 120, 120));
				float nameW = Math.Min(HubKit.Measure(e.Name, podium ? 0.86f : 0.8f), width - 380);
				HubKit.Chip(sb, arrow, mx + (int)nameW + 8, r.Y + (podium ? 6 : 4), color * 0.25f, color, 0.6f);
			}
			// Under the name: who's here, the record, or what makes up the global score.
			string sub;
			if (_board == Scores.Global)
			{
				(int t, int v, int c, int x3, int s) = Scores.GlobalParts(e.Id);
				sub = IsaClient.Languages.L10n.F("Tetris {0}  ·  Snake {1}  ·  Versus {2}  ·  Chess {3}  ·  Tic Tac Toe {4}  ·  Casino {5}", t, s, v, c, x3, Scores.CasinoPart(e.Id));
			}
			else if (_board == Scores.Tetris || _board == Scores.Snake)
			{
				sub = mine ? IsaClient.Languages.L10n.T("you") : Ago(e.When);
			}
			else
			{
				sub = IsaClient.Languages.L10n.F("{0}W  {1}L  {2}D", e.Wins, e.Losses, e.Draws);
			}
			int here = IsaClient.Identity.ClientIdentity.PlayerWith(e.Id);
			int subX = mx;
			if (here >= 0 && !mine)
			{
				HubKit.Fill(sb, new Rectangle(mx, r.Y + (podium ? 30 : 26), 6, 6), new Color(110, 230, 110));
				subX += 10;
				if (_board == Scores.Tetris || _board == Scores.Snake)
				{
					sub = IsaClient.Languages.L10n.F("here now as {0}", Main.player[here].name);
				}
			}
			HubKit.RawText(sb, sub, subX, r.Y + (podium ? 26 : 22), Theme.Muted, 0.58f, width - 390);
			string score = Value(_board, e);
			float sw = HubKit.Measure(score, podium ? 0.95f : 0.85f);
			HubKit.RawText(sb, score, r.Right - 14 - sw, r.Y + (r.Height - 20) / 2f, podium ? Tetris.Medal(i) : Color.White, podium ? 0.95f : 0.85f);
			if (_board == Scores.Chess)
			{
				HubKit.RawText(sb, "Elo", r.Right - 14 - sw - 24, r.Y + (r.Height - 14) / 2f, Theme.Muted, 0.56f);
			}
			y = r.Bottom + 5;
		}
		return y - start;
	}

	private static string Ago(long when)
	{
		if (when <= 0)
		{
			return "";
		}
		TimeSpan ago = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(when);
		if (ago.TotalMinutes < 60)
		{
			return IsaClient.Languages.L10n.F("{0} min ago", Math.Max(1, (int)ago.TotalMinutes));
		}
		if (ago.TotalHours < 24)
		{
			return IsaClient.Languages.L10n.F("{0} h ago", (int)ago.TotalHours);
		}
		return IsaClient.Languages.L10n.F("{0} days ago", (int)ago.TotalDays);
	}

	#endregion

	#region Previews

	/// <summary>Pixel art drawn sharp in the panel's batch; call <see cref="HubInput.Restart"/> after.</summary>
	private static void Sharp(SpriteBatch sb)
	{
		sb.End();
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, HubInput.Clipping ? new RasterizerState
		{
			CullMode = CullMode.None,
			ScissorTestEnable = true
		} : RasterizerState.CullCounterClockwise, null, HubInput.Matrix);
	}

	private static void Sprite(SpriteBatch sb, Rectangle source, Vector2 center, float size, float rotation = 0f)
	{
		Texture2D atlas = GameArt.Atlas;
		if (atlas != null)
		{
			sb.Draw(atlas, center, source, Color.White, rotation, new Vector2(source.Width, source.Height) / 2f, size / source.Width, SpriteEffects.None, 0f);
		}
	}

	/// <summary>A corner of a board mid-game; the knight bobs.</summary>
	private static void ChessPreview(SpriteBatch sb, Rectangle art)
	{
		Texture2D atlas = GameArt.Atlas;
		if (atlas == null)
		{
			return;
		}
		int cell = art.Width / 4;
		Sharp(sb);
		for (int i = 0; i < 16; i++)
		{
			bool light = (i % 4 + i / 4) % 2 == 0;
			sb.Draw(atlas, new Rectangle(art.X + i % 4 * cell, art.Y + i / 4 * cell, cell, cell), light ? GameArt.LightTile : GameArt.DarkTile, Color.White);
		}
		float bob = (float)Math.Sin(GameArt.Now * 2.4) * 2f;
		Vector2 C(int col, int row) => new Vector2(art.X + col * cell + cell / 2f, art.Y + row * cell + cell / 2f);
		Sprite(sb, GameArt.Piece(ChessPosition.King, false), C(1, 0), cell);
		Sprite(sb, GameArt.Piece(ChessPosition.Pawn, false), C(3, 1), cell);
		Sprite(sb, GameArt.Piece(ChessPosition.Queen, true), C(0, 2), cell);
		Sprite(sb, GameArt.Piece(ChessPosition.Knight, true), C(2, 1) + new Vector2(0f, bob - 3f), cell * 1.05f);
		Sprite(sb, GameArt.Piece(ChessPosition.Pawn, true), C(1, 3), cell);
		Sprite(sb, GameArt.Piece(ChessPosition.Rook, false), C(3, 3), cell);
		HubInput.Restart(sb, HubInput.Clipping);
	}

	/// <summary>A finished game of tic-tac-toe: the winning stroke keeps drawing itself.</summary>
	private static void TicTacToePreview(SpriteBatch sb, Rectangle art)
	{
		Texture2D atlas = GameArt.Atlas;
		if (atlas == null)
		{
			return;
		}
		HubKit.Fill(sb, art, new Color(28, 22, 30));
		int gap = 4;
		int cell = (art.Width - gap * 4) / 3;
		Rectangle Cell(int i) => new Rectangle(art.X + gap + i % 3 * (cell + gap), art.Y + gap + i / 3 * (cell + gap), cell, cell);
		string marks = "XO.OX..OX";
		Sharp(sb);
		for (int i = 0; i < 9; i++)
		{
			sb.Draw(atlas, Cell(i), (i % 2 == 0) ? GameArt.LightTile : GameArt.DarkTile, Color.White);
		}
		for (int i = 0; i < 9; i++)
		{
			if (marks[i] != '.')
			{
				Sprite(sb, (marks[i] == 'X') ? GameArt.MarkX : GameArt.MarkO, Cell(i).Center.ToVector2(), cell * 0.8f);
			}
		}
		HubInput.Restart(sb, HubInput.Clipping);
		float t = GameArt.Clamp01((GameArt.Now % 3.0) / 0.8);
		Vector2 a = Cell(0).Center.ToVector2();
		Vector2 b = Vector2.Lerp(a, Cell(8).Center.ToVector2(), GameArt.EaseOut(t));
		Vector2 d = b - a;
		if (d.Length() > 1f)
		{
			sb.Draw(HubKit.Pixel, a, new Rectangle(0, 0, 1, 1), new Color(255, 120, 120), (float)Math.Atan2(d.Y, d.X), new Vector2(0f, 0.5f), new Vector2(d.Length(), 5f), SpriteEffects.None, 0f);
		}
	}

	#endregion

	#region Customize

	private static string _pickFor;

	private static string _search = "";

	public static int DrawCustomize(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y = SwitchRow(sb, "games:invitations", "Get invitations", "Off: invitations to you are declined by themselves and invitations to everyone don't show.", GameLobby.AcceptInvitations, v => GameLobby.AcceptInvitations = v, x, y, width);
		y = SwitchRow(sb, "games:ontop", "Always on top", "The board and invitations stay over everything, the panel too. Off: the last window you used goes on top.", GameOverlay.AlwaysOnTop, v => GameOverlay.AlwaysOnTop = v, x, y, width);
		y = OptionRow(sb, "games:size", new GameOption
		{
			Name = "Board size",
			Hint = "How big the game window is.",
			Labels = new[] { "Small", "Medium", "Large" },
			Values = new[] { 0, 1, 2 },
			Get = () => GameOverlay.Size,
			Set = v => GameOverlay.Size = v
		}, x, y, width);
		foreach (PipeGame game in GameLobby.Games)
		{
			y += 8;
			HubIcon.Game(game).Draw(sb, new Rectangle(x, y, 20, 20), Color.White, 3f);
			HubKit.Text(sb, game.Name, x + 26, y + 2, Theme.Muted, 0.76f);
			y += 26;
			foreach (GameOption option in new[] { game.First, game.TurnTime, game.BotLevel })
			{
				if (option != null)
				{
					y = OptionRow(sb, "games:opt:" + game.Id + ":" + option.Name, option, x, y, width);
				}
			}
		}
		// Snake (not a pipe game: its options are its own).
		y += 8;
		HubIcon.Item(I.Apple).Draw(sb, new Rectangle(x, y, 20, 20), Color.White);
		HubKit.Text(sb, "Snake", x + 26, y + 2, Theme.Muted, 0.76f);
		y += 26;
		foreach (GameOption option in new[] { Snake.SpeedOption, Snake.WallsOption, Snake.SkinOption })
		{
			y = OptionRow(sb, "games:snake:" + option.Name, option, x, y, width);
		}
		// X and O icons
		y += 8;
		HubKit.Text(sb, "Tic Tac Toe icons", x, y, Theme.Muted, 0.72f);
		HubKit.Text(sb, "Any item of the game, only on your screen. Click a mark to change it.", x, y + 16, Theme.Muted, 0.62f, width);
		y += 38;
		y = MarkRow(sb, "X", x, y, width) + 6;
		y = MarkRow(sb, "O", x, y, width) + 10;
		if (_pickFor != null)
		{
			y = DrawPicker(sb, x, y, width);
		}
		return y - start;
	}

	private static int SwitchRow(SpriteBatch sb, string id, string title, string hint, bool value, Action<bool> set, int x, int y, int width)
	{
		Rectangle row = new Rectangle(x, y, width, 44);
		HubKit.Box(sb, row, Theme.Card * HubPrefs.Opacity);
		HubKit.Text(sb, title, x + 14, y + 7, Color.White, 0.82f);
		HubKit.Text(sb, hint, x + 14, y + 25, Theme.Muted, 0.62f, width - 90);
		bool hover = HubInput.In(row);
		HubKit.Switch(sb, id, new Rectangle(row.Right - 56, y + 11, 44, 22), value, hover);
		if (hover && HubInput.Hover(id + "row"))
		{
			set(!value);
			ClientSettings.Save();
		}
		return row.Bottom + 8;
	}

	private static int OptionRow(SpriteBatch sb, string id, GameOption option, int x, int y, int width)
	{
		Rectangle row = new Rectangle(x, y, width, 48);
		HubKit.Box(sb, row, Theme.Card * HubPrefs.Opacity);
		int buttons = 0;
		int[] widths = new int[option.Labels.Length];
		for (int i = 0; i < widths.Length; i++)
		{
			widths[i] = Math.Max(58, (int)HubKit.Measure(IsaClient.Languages.L10n.T(option.Labels[i]), 0.78f) + 22);
			buttons += widths[i] + 6;
		}
		HubKit.Text(sb, option.Name, x + 14, y + 7, Color.White, 0.82f, width - buttons - 30);
		HubKit.Text(sb, option.Hint, x + 14, y + 27, Theme.Muted, 0.6f, width - buttons - 30);
		int bx = row.Right - 8 - buttons + 6;
		int current = option.Get();
		for (int i = 0; i < widths.Length; i++)
		{
			if (HubKit.Button(sb, id + ":" + i, new Rectangle(bx, y + 10, widths[i], 28), option.Labels[i], (current == option.Values[i]) ? On : Theme.Slot))
			{
				option.Set(option.Values[i]);
				ClientSettings.Save();
			}
			bx += widths[i] + 6;
		}
		return row.Bottom + 8;
	}

	private static int MarkRow(SpriteBatch sb, string mark, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 56);
		bool picking = _pickFor == mark;
		HubKit.Box(sb, r, (picking ? Color.Lerp(Theme.Card, Theme.Slot, 0.5f) : Theme.Card) * HubPrefs.Opacity);
		Rectangle preview = new Rectangle(r.X + 8, r.Y + 6, 44, 44);
		HubKit.Box(sb, preview, IsaClient.Music.MusicPlayerWindow.PanelColor);
		int item = (mark == "X") ? TicTacToe.XItem : TicTacToe.OItem;
		HubIcon icon = (item > 0) ? HubIcon.Item(item) : HubIcon.Sprite(() => GameArt.Atlas, (mark == "X") ? GameArt.MarkX : GameArt.MarkO);
		icon.Draw(sb, new Rectangle(preview.X + 6, preview.Y + 6, 32, 32), Color.White, 4f);
		string name = (item > 0) ? Lang.GetItemNameValue(item) : IsaClient.Languages.L10n.T("The pixel mark");
		HubKit.Text(sb, IsaClient.Languages.L10n.F("{0} is drawn as", mark), r.X + 64, r.Y + 9, Theme.Muted, 0.66f);
		HubKit.RawText(sb, name, r.X + 64, r.Y + 26, Color.White, 0.82f, width - 300);
		if (HubKit.Button(sb, "games:pick:" + mark, new Rectangle(r.Right - 210, r.Y + 13, 110, 30), picking ? "Done" : "Choose item", Theme.Slot, null, true, HubIcon.Item(I.Paintbrush)))
		{
			_pickFor = picking ? null : mark;
			_search = "";
			if (_pickFor != null)
			{
				HubInput.SetFocus("games:search");
			}
		}
		if (HubKit.Button(sb, "games:letter:" + mark, new Rectangle(r.Right - 94, r.Y + 13, 84, 30), "Default", Theme.Slot, "Back to the pixel " + mark, item > 0))
		{
			SetMark(mark, 0);
		}
		return r.Bottom;
	}

	private static void SetMark(string mark, int item)
	{
		if (mark == "X")
		{
			TicTacToe.XItem = item;
		}
		else
		{
			TicTacToe.OItem = item;
		}
		ClientSettings.Save();
	}

	private static List<int> _results = new List<int>();

	private static string _resultsFor;

	private static int DrawPicker(SpriteBatch sb, int x, int y, int width)
	{
		_search = HubKit.Field(sb, "games:search", new Rectangle(x, y, width, 30), _search, IsaClient.Languages.L10n.F("Find an item for {0}...", _pickFor), 40, HubIcon.Texture("Images/UI/Cursor_2"));
		y += 38;
		string query = (_search ?? "").Trim();
		if (query != _resultsFor)
		{
			_resultsFor = query;
			_results = Search(query);
		}
		int cell = 44;
		int perRow = Math.Max(1, (width + 4) / (cell + 4));
		for (int i = 0; i < _results.Count; i++)
		{
			int item = _results[i];
			Rectangle r = new Rectangle(x + i % perRow * (cell + 4), y + i / perRow * (cell + 4), cell, cell);
			if (HubKit.IconButton(sb, "games:item:" + item, r, HubIcon.Item(item), Lang.GetItemNameValue(item)))
			{
				SetMark(_pickFor, item);
				_pickFor = null;
				break;
			}
		}
		y += (_results.Count + perRow - 1) / perRow * (cell + 4) + 6;
		return y;
	}

	/// <summary>Items whose name has the text (or a few nice ones when it's empty); up to 40.</summary>
	private static List<int> Search(string query)
	{
		if (query.Length == 0)
		{
			return new List<int> { I.FallenStar, I.Heart, I.LifeCrystal, I.ManaCrystal, I.Diamond, I.Ruby, I.Emerald, I.Sapphire, I.Topaz, I.Amethyst, I.Bomb, I.Skull, I.GoldCoin, I.PlatinumCoin, I.CopperCoin, I.SoulofLight, I.SoulofNight, I.Gel, I.Mushroom, I.SlimeCrown, I.SuspiciousLookingEye, I.Torch, I.PinkGel, I.GoldenKey };
		}
		List<int> found = new List<int>();
		for (int i = 1; i < I.Count && found.Count < 40; i++)
		{
			string name = Lang.GetItemNameValue(i);
			if (!string.IsNullOrEmpty(name) && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				found.Add(i);
			}
		}
		return found;
	}

	#endregion
}
