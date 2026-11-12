using System;
using System.Collections.Generic;
using IsaClient.Core;
using IsaClient.Music;
using IsaClient.Net.Pipe;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;

namespace IsaClient.Games;

/// <summary>
/// Over the game, two little windows that start in the middle of the screen and can be dragged by their top bar: the
/// invitations (who invites you, how the game is set, Play / Decline, the time left; and yours while it waits) and your
/// match: a header with the score, the rival above the board and you below it (avatar, side, captures, turn clock), the
/// board (with a move list for chess), and the result and buttons at the bottom; minimizable to a bar. Each is one of
/// the music player's floating surfaces, so it only takes the mouse where it is.
/// </summary>
public static class GameOverlay
{
	/// <summary>0 small, 1 medium, 2 large board.</summary>
	[Setting("GameWindowSize", Min = 0, Max = 2)]
	public static int Size = 1;

	/// <summary>The game's windows come up over everything (the panel, the music player...) when they appear.</summary>
	[Setting("GamesOnTop")]
	public static bool AlwaysOnTop = true;

	internal sealed class Panel
	{
		public string Layer;

		/// <summary>Top-left in UI units; null = centered.</summary>
		public Vector2? Position;

		public Rectangle ScreenRect;

		public Rectangle Rect;
	}

	private static readonly Panel _invites = new Panel { Layer = "games:invites" };

	private static readonly Panel _board = new Panel { Layer = "games:board" };

	private static Panel _dragging;

	private static Vector2 _dragOffset;

	private static bool _registered;

	private static bool _minimized;

	private static readonly Color Win = new Color(255, 214, 80);

	private static readonly Color Lose = new Color(255, 120, 120);

	private static readonly Color Green = new Color(60, 140, 80);

	private static readonly Color Danger = new Color(150, 60, 70);

	private static bool ShowsInvites => !Main.gameMenu && (GameLobby.Outgoing != null || GameLobby.Invites.Count > 0);

	private static bool ShowsBoard => !Main.gameMenu && GameLobby.Match != null;

	public static void Ensure()
	{
		if (_registered)
		{
			return;
		}
		_registered = true;
		MusicPlayerWindow.AddSurface(new MusicPlayerWindow.FloatingSurface
		{
			Layer = _invites.Layer,
			Shown = () => ShowsInvites,
			ScreenRect = () => _invites.ScreenRect,
			Draw = DrawInvites
		});
		MusicPlayerWindow.AddSurface(new MusicPlayerWindow.FloatingSurface
		{
			Layer = _board.Layer,
			Shown = () => ShowsBoard,
			ScreenRect = () => _board.ScreenRect,
			Draw = DrawBoard
		});
	}

	private static bool _boardWasShown;

	private static int _invitesSeen;

	/// <summary>
	/// Every frame: with <see cref="AlwaysOnTop"/>, a window that just appeared (a match, a new invitation) goes on top
	/// once; clicking another window afterwards puts that one in front, as usual.
	/// </summary>
	public static void KeepOnTop()
	{
		if (!_registered)
		{
			return;
		}
		bool board = ShowsBoard;
		int invites = ShowsInvites ? (GameLobby.Invites.Count + ((GameLobby.Outgoing != null) ? 1 : 0)) : 0;
		if (AlwaysOnTop)
		{
			if (board && !_boardWasShown)
			{
				MusicPlayerWindow.BringSurfaceToFront(_board.Layer);
			}
			if (invites > _invitesSeen)
			{
				MusicPlayerWindow.BringSurfaceToFront(_invites.Layer);
			}
		}
		_boardWasShown = board;
		_invitesSeen = invites;
	}

	private static bool _focused;

	private static bool _prevMouse;

	/// <summary>Games without turns (Tetris Versus) take the keyboard while their window is in use.</summary>
	internal static bool BoardHasKeys => _focused && !_minimized && ShowsBoard && Main.hasFocus && !Main.drawingPlayerChat && !Main.editSign && !Main.editChest;

	internal static void FocusBoard()
	{
		_focused = true;
	}

	/// <summary>A click outside the game window lets go of the keyboard.</summary>
	internal static void UpdateFocus()
	{
		bool mouse = Main.mouseLeft;
		if (mouse && !_prevMouse && !_board.ScreenRect.Contains(Main.mouseX, Main.mouseY))
		{
			_focused = false;
		}
		_prevMouse = mouse;
	}

	/// <summary>Shows the board again if it was minimized, in front.</summary>
	public static void Show()
	{
		_minimized = false;
		_focused = true;
		if (_registered)
		{
			MusicPlayerWindow.BringSurfaceToFront(_board.Layer);
		}
	}

	private static int BoardSize(PipeGame game)
	{
		return game.BoardSizes[Math.Max(0, Math.Min(game.BoardSizes.Length - 1, Size))];
	}

	#region Windows

	/// <summary>Where a window of this size goes: where it was dragged (kept on screen), or centered.</summary>
	internal static Rectangle Place(Panel panel, int width, int height, int screenW, int screenH, int centerOffsetY)
	{
		Vector2 at = panel.Position ?? new Vector2((screenW - width) / 2f, (screenH - height) / 2f + centerOffsetY);
		at.X = MathHelper.Clamp(at.X, 0f, Math.Max(0, screenW - width));
		at.Y = MathHelper.Clamp(at.Y, 0f, Math.Max(0, screenH - height));
		panel.Rect = new Rectangle((int)at.X, (int)at.Y, width, height);
		panel.ScreenRect = MusicPlayerWindow.ToScreen(panel.Rect);
		return panel.Rect;
	}

	/// <summary>Dragging by <paramref name="handle"/>; the click on the window is the window's.</summary>
	internal static void Drag(Panel panel, Rectangle handle, int screenW, int screenH)
	{
		if (_dragging == panel)
		{
			if (!MusicPlayerWindow.MouseDown)
			{
				_dragging = null;
			}
			else
			{
				Vector2 mouse = MusicPlayerWindow.MousePoint.ToVector2();
				panel.Position = new Vector2(MathHelper.Clamp(mouse.X - _dragOffset.X, 0f, screenW - panel.Rect.Width), MathHelper.Clamp(mouse.Y - _dragOffset.Y, 0f, screenH - panel.Rect.Height));
			}
		}
		else if (MusicPlayerWindow.Clicked && MusicPlayerWindow.MouseIn(handle))
		{
			_dragging = panel;
			_dragOffset = MusicPlayerWindow.MousePoint.ToVector2() - new Vector2(panel.Rect.X, panel.Rect.Y);
			MusicPlayerWindow.BringSurfaceToFront(panel.Layer);
			MusicPlayerWindow.StartSurfaceDrag();
		}
		if (MusicPlayerWindow.Clicked && MusicPlayerWindow.MouseIn(panel.Rect))
		{
			MusicPlayerWindow.Clicked = false;
		}
	}

	/// <summary>A window: the inventory-style box with a soft lighter band on top.</summary>
	internal static void Window(SpriteBatch sb, Rectangle r, int band)
	{
		MusicDrawing.Box(sb, r, MusicPlayerWindow.BackColor * 0.97f);
		GameArt.Gradient(sb, new Rectangle(r.X + 6, r.Y + 5, r.Width - 12, band), new Color(76, 94, 168) * 0.55f, Color.Transparent);
	}

	/// <summary>The little grip on a window's top bar: it can be dragged.</summary>
	internal static void Grip(SpriteBatch sb, Rectangle bar)
	{
		for (int i = 0; i < 3; i++)
		{
			MusicDrawing.Fill(sb, new Rectangle(bar.Center.X - 12 + i * 10, bar.Y + 5, 6, 2), MusicPlayerWindow.MutedColor * 0.45f);
		}
	}

	/// <summary>A small rounded label.</summary>
	internal static int Chip(SpriteBatch sb, string text, int x, int y, Color back, Color color, int maxRight)
	{
		if (maxRight - x < 40)
		{
			return x;
		}
		float w = Math.Min(MusicDrawing.Measure(text, 0.62f), maxRight - x - 14);
		Rectangle r = new Rectangle(x, y, (int)w + 14, 20);
		MusicDrawing.Box(sb, r, back);
		MusicDrawing.RawText(sb, text, x + 7, y + 3, color, 0.62f, w + 2f);
		return r.Right + 5;
	}

	/// <summary>Terraria's menu panel (the character and world lists): its border turns gold when it's chosen.</summary>
	internal static void MenuPanel(SpriteBatch sb, Rectangle r, Color back, Color border)
	{
		Utils.DrawSplicedPanel(sb, MusicDrawing.Tex("Images/UI/PanelBackground"), r.X, r.Y, r.Width, r.Height, 10, 10, 10, 10, back);
		Utils.DrawSplicedPanel(sb, MusicDrawing.Tex("Images/UI/PanelBorder"), r.X, r.Y, r.Width, r.Height, 10, 10, 10, 10, border);
	}

	private static void Head(SpriteBatch sb, Player player, Vector2 center, float scale)
	{
		try
		{
			// The game centers a map head on (x, y) at scale 1; every layer scales around (x + 4, y + 14) instead.
			Vector2 corner = new Vector2(center.X - 4f + 4f * scale, center.Y - 14f + 9f * scale);
			Main.PlayerRenderer.DrawPlayerHead(Main.Camera, player, corner, 1f, scale, Color.Transparent);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		// Dye shaders may have changed the batch.
		GameArt.Smooth(sb);
	}

	#endregion

	#region Invitations

	private const int InviteWidth = 400;

	private const int InviteHeight = 96;

	private static void DrawInvites(SpriteBatch sb, int screenW, int screenH)
	{
		List<GameInvite> invites = new List<GameInvite>(GameLobby.Invites);
		OutgoingInvite outgoing = GameLobby.Outgoing;
		int height = invites.Count * (InviteHeight + 6) + ((outgoing != null) ? 62 : 0) + 4;
		int boardHalf = (GameLobby.Match != null) ? (BoardSize(GameLobby.Match.Game) / 2 + 120) : 60;
		Rectangle r = Place(_invites, InviteWidth, height, screenW, screenH, -boardHalf);
		int y = r.Y;
		foreach (GameInvite invite in invites)
		{
			DrawInvite(sb, invite, new Rectangle(r.X, y, r.Width, InviteHeight));
			y += InviteHeight + 6;
		}
		if (outgoing != null)
		{
			DrawOutgoing(sb, outgoing, new Rectangle(r.X, y, r.Width, 56));
		}
		// Each card's top bar drags them all.
		Drag(_invites, new Rectangle(r.X, r.Y, r.Width - 120, 24), screenW, screenH);
	}

	private static void DrawInvite(SpriteBatch sb, GameInvite invite, Rectangle r)
	{
		Window(sb, r, 30);
		float glow = 0.6f + 0.4f * GameArt.Pulse(1.6);
		MusicDrawing.Fill(sb, new Rectangle(r.X + 4, r.Y + 10, 3, r.Height - 20), MusicPlayerWindow.AccentColor * glow);
		Grip(sb, r);
		Rectangle icon = new Rectangle(r.X + 16, r.Y + 22, 44, 44);
		MusicDrawing.Box(sb, icon, MusicPlayerWindow.PanelDarkColor * 0.9f);
		invite.Game.DrawIcon(sb, new Rectangle(icon.X + 4, icon.Y + 4, 36, 36), Color.White);
		MusicDrawing.RawText(sb, GameLobby.Name(invite.From), r.X + 72, r.Y + 12, MusicPlayerWindow.AccentColor, 0.86f, r.Width - 196);
		string text = invite.ToEveryone ? IsaClient.Languages.L10n.F("invites everyone to {0}", IsaClient.Languages.L10n.T(invite.Game.Name)) : IsaClient.Languages.L10n.F("invites you to {0}", IsaClient.Languages.L10n.T(invite.Game.Name));
		MusicDrawing.RawText(sb, text, r.X + 72, r.Y + 33, Color.White, 0.74f, r.Width - 196);
		// How it's set: who starts / your color, and the clock.
		int cx = r.X + 72;
		if (invite.Game.First != null)
		{
			cx = Chip(sb, IsaClient.Languages.L10n.T(invite.Game.First.Name) + ": " + invite.Game.FirstFor(invite.First, invited: true), cx, r.Y + 58, MusicPlayerWindow.PanelColor, Color.White * 0.9f, r.Right - 122);
		}
		if (invite.TurnSeconds >= 0 && invite.Game.RealTime && invite.Game.TurnTime != null)
		{
			Chip(sb, IsaClient.Languages.L10n.T(invite.Game.TurnTime.Name) + ": " + invite.Game.TurnTime.Label(invite.TurnSeconds), cx, r.Y + 58, MusicPlayerWindow.PanelColor, Color.White * 0.9f, r.Right - 122);
		}
		else if (invite.TurnSeconds >= 0)
		{
			Chip(sb, (invite.TurnSeconds == 0) ? IsaClient.Languages.L10n.T("No time limit") : IsaClient.Languages.L10n.F("{0}s per turn", invite.TurnSeconds), cx, r.Y + 58, MusicPlayerWindow.PanelColor, Color.White * 0.9f, r.Right - 122);
		}
		if (MusicDrawing.TextButton(sb, "games:accept:" + invite.From, new Rectangle(r.Right - 112, r.Y + 14, 100, 30), "Join", Green, "Accept the invitation"))
		{
			GameLobby.AcceptInvite(invite);
			Show();
		}
		if (MusicDrawing.TextButton(sb, "games:decline:" + invite.From, new Rectangle(r.Right - 112, r.Y + 50, 100, 26), "Decline", MusicPlayerWindow.PanelColor, null))
		{
			GameLobby.DeclineInvite(invite);
		}
		TimeBar(sb, r, invite.Until);
	}

	private static void DrawOutgoing(SpriteBatch sb, OutgoingInvite outgoing, Rectangle r)
	{
		Window(sb, r, 20);
		Grip(sb, r);
		outgoing.Game.DrawIcon(sb, new Rectangle(r.X + 14, r.Y + 13, 30, 30), Color.White);
		string who = (outgoing.To == PipeNet.Everyone) ? IsaClient.Languages.L10n.T("everyone") : GameLobby.Name(outgoing.To);
		string dots = new string('.', 1 + (int)(GameArt.Now * 2.0) % 3);
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.F("Waiting for {0}", who) + dots, r.X + 54, r.Y + 19, Color.White, 0.78f, r.Width - 176);
		if (MusicDrawing.TextButton(sb, "games:cancel", new Rectangle(r.Right - 112, r.Y + 14, 100, 28), "Cancel", MusicPlayerWindow.PanelColor, "Take your invitation back"))
		{
			GameLobby.CancelInvite();
		}
		TimeBar(sb, r, outgoing.Until);
	}

	private static void TimeBar(SpriteBatch sb, Rectangle r, double until)
	{
		float left = MathHelper.Clamp((float)((until - Main.timeForVisualEffects) / 3600.0), 0f, 1f);
		MusicDrawing.Fill(sb, new Rectangle(r.X + 10, r.Bottom - 6, r.Width - 20, 2), MusicPlayerWindow.PanelDarkColor);
		MusicDrawing.Fill(sb, new Rectangle(r.X + 10, r.Bottom - 6, (int)((r.Width - 20) * left), 2), MusicPlayerWindow.AccentColor * 0.75f);
	}

	#endregion

	#region Match window

	private const int Pad = 12;

	private const int HeaderH = 46;

	private const int StripH = 40;

	private const int FooterH = 44;

	/// <summary>Round and state last drawn: a change starts the result banner.</summary>
	private static int _seenRound = -1;

	private static MatchState _seenState;

	private static double _bannerAt = -10.0;

	private static double _resignArmedUntil;

	private static void DrawBoard(SpriteBatch sb, int screenW, int screenH)
	{
		GameMatch match = GameLobby.Match;
		if (match == null)
		{
			return;
		}
		IGameHud hud = match.Board as IGameHud;
		int boardSize = BoardSize(match.Game);
		int side = (hud != null && hud.SideWidth > 0 && match.State != MatchState.Joining) ? hud.SideWidth : 0;
		int width = Math.Max(340, Pad * 2 + boardSize + ((side > 0) ? (side + 8) : 0));
		if (_seenRound != match.Round || _seenState != match.State)
		{
			if (match.State == MatchState.Over && _seenState == MatchState.Playing)
			{
				_bannerAt = GameArt.Now;
			}
			_seenRound = match.Round;
			_seenState = match.State;
		}
		if (_minimized)
		{
			DrawMinimized(sb, match, width, screenW, screenH);
			return;
		}
		int height = HeaderH + StripH + 8 + boardSize + 8 + StripH + FooterH + 6;
		Rectangle r = Place(_board, width, height, screenW, screenH, 0);
		Window(sb, r, HeaderH);
		Grip(sb, r);
		if (DrawHeader(sb, match, r))
		{
			return;
		}
		int contentW = boardSize + ((side > 0) ? (side + 8) : 0);
		int cx = r.X + (r.Width - contentW) / 2;
		int y = r.Y + HeaderH;
		Rectangle top = new Rectangle(r.X + Pad, y, r.Width - Pad * 2, StripH);
		DrawStrip(sb, match, hud, top, 1 - match.MySeat);
		y = top.Bottom + 8;
		Rectangle board = new Rectangle(cx, y, boardSize, boardSize);
		if (match.State == MatchState.Joining)
		{
			MusicDrawing.Box(sb, board, MusicPlayerWindow.PanelDarkColor * 0.8f);
			string dots = new string('.', 1 + (int)(GameArt.Now * 2.0) % 3);
			MusicDrawing.RawText(sb, IsaClient.Languages.L10n.T("Starting") + dots, board.X + 20, board.Center.Y - 10, Color.White, 0.85f);
		}
		else
		{
			byte[] move = match.Board.Draw(sb, board, match.MySeat, match.MyTurn);
			if (move != null)
			{
				GameLobby.PlayMove(move);
			}
			if (side > 0)
			{
				hud.DrawSide(sb, new Rectangle(board.Right + 8, board.Y, side, boardSize));
			}
			DrawBanner(sb, match, hud, board);
		}
		Rectangle bottom = new Rectangle(r.X + Pad, board.Bottom + 8, r.Width - Pad * 2, StripH);
		DrawStrip(sb, match, hud, bottom, match.MySeat);
		DrawFooter(sb, match, hud, new Rectangle(r.X + Pad, bottom.Bottom + 6, r.Width - Pad * 2, FooterH - 8));
		Drag(_board, new Rectangle(r.X, r.Y, r.Width - 76, HeaderH - 4), screenW, screenH);
	}

	/// <summary>Icon, name, rival and score; minimize and close. True when the window closed.</summary>
	private static bool DrawHeader(SpriteBatch sb, GameMatch match, Rectangle r)
	{
		Rectangle icon = new Rectangle(r.X + 12, r.Y + 9, 30, 30);
		match.Game.DrawIcon(sb, icon, Color.White);
		MusicDrawing.Text(sb, match.Game.Name, r.X + 50, r.Y + 9, MusicPlayerWindow.AccentColor, 0.86f, r.Width - 190);
		string score = (match.Draws > 0) ? IsaClient.Languages.L10n.F("{0} - {1}  ({2} draws)", match.MyWins, match.TheirWins, match.Draws) : (match.MyWins + " - " + match.TheirWins);
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.F("vs {0}", match.OpponentName) + "   " + score, r.X + 50, r.Y + 28, MusicPlayerWindow.MutedColor, 0.64f, r.Width - 140);
		if (MusicDrawing.TextButton(sb, "games:min", new Rectangle(r.Right - 66, r.Y + 11, 24, 22), "-", MusicPlayerWindow.PanelColor, "Minimize"))
		{
			_minimized = true;
		}
		bool playing = match.State == MatchState.Playing || match.State == MatchState.Joining;
		if (MusicDrawing.BareIcon(sb, "games:close", new Rectangle(r.Right - 34, r.Y + 13, 17, 18), MusicDrawing.Tex("Images/MapDeath"), null, playing ? (match.VsBot ? "Close the game" : "Leave the game") : "Close", Color.White))
		{
			GameLobby.LeaveMatch();
			return true;
		}
		return false;
	}

	/// <summary>A player's strip: avatar, name, side, extras (captures) and, on their turn, a glow and the clock.</summary>
	private static void DrawStrip(SpriteBatch sb, GameMatch match, IGameHud hud, Rectangle r, int seat)
	{
		bool mine = seat == match.MySeat;
		bool theirTurn = match.State == MatchState.Playing && !match.Game.RealTime && match.Board.Turn == seat && match.Result < 0;
		Color back = theirTurn ? Color.Lerp(MusicPlayerWindow.PanelDarkColor, MusicPlayerWindow.PanelColor, 0.7f) : MusicPlayerWindow.PanelDarkColor;
		Color border = theirTurn ? Color.Lerp(Main.OurFavoriteColor * 0.7f, Main.OurFavoriteColor, GameArt.Pulse(1.2)) : Color.Black;
		MenuPanel(sb, r, back * 0.95f, border);
		// Avatar, in a little slot.
		Rectangle slot = new Rectangle(r.X + 6, r.Y + 5, r.Height - 10, r.Height - 10);
		MusicDrawing.Fill(sb, slot, Color.Black * 0.25f);
		Player player = mine ? Main.LocalPlayer : (GameLobby.Online(match.Opponent) ? Main.player[match.Opponent] : null);
		if (!mine && match.VsBot)
		{
			Main.instance.LoadItem(ItemID.CyborgHelmet);
			Texture2D helmet = Terraria.GameContent.TextureAssets.Item[ItemID.CyborgHelmet].Value;
			MusicDrawing.Icon(sb, helmet, null, new Rectangle(slot.X + 2, slot.Y + 2, slot.Width - 4, slot.Height - 4), Color.White);
		}
		else if (player != null)
		{
			Head(sb, player, new Vector2(slot.Center.X, slot.Center.Y - 1), 0.72f);
		}
		string name = mine ? IsaClient.Languages.L10n.T("You") : match.OpponentName;
		float nameW = Math.Min(MusicDrawing.Measure(name, 0.8f), r.Width * 0.42f);
		MusicDrawing.RawText(sb, name, r.X + 46, r.Y + 4, Color.White, 0.8f, r.Width * 0.42f);
		// Side: a little picture and its name.
		int ex = r.X + 46;
		if (hud != null && match.State != MatchState.Joining)
		{
			hud.DrawSeat(sb, new Rectangle(r.X + 46, r.Y + 22, 14, 14), seat);
			MusicDrawing.RawText(sb, hud.SeatName(seat), r.X + 64, r.Y + 21, MusicPlayerWindow.MutedColor, 0.6f);
			ex = (int)Math.Max(r.X + 46 + nameW + 12, r.X + 64 + MusicDrawing.Measure(hud.SeatName(seat), 0.6f) + 12);
		}
		// The clock (or the computer thinking) on the right; extras in between.
		int right = r.Right - 8;
		if (theirTurn && match.VsBot && !mine)
		{
			string dots = new string('.', 1 + (int)(GameArt.Now * 2.5) % 3);
			string thinking = IsaClient.Languages.L10n.T("Thinking") + dots;
			float w = MusicDrawing.Measure(IsaClient.Languages.L10n.T("Thinking") + "...", 0.7f);
			MusicDrawing.RawText(sb, thinking, right - w, r.Y + 11, MusicPlayerWindow.AccentColor, 0.7f);
			right -= (int)w + 8;
		}
		else if (theirTurn && match.TurnSeconds > 0 && !match.Game.RealTime)
		{
			float left = match.SecondsLeft;
			int whole = (int)Math.Ceiling(left);
			string clock = (whole / 60) + ":" + (whole % 60).ToString("00");
			Color color = (left <= 5f) ? Lose : (mine ? MusicPlayerWindow.AccentColor : Color.White);
			Rectangle box = new Rectangle(right - 60, r.Y + 7, 60, r.Height - 14);
			MusicDrawing.Box(sb, box, Color.Black * 0.35f);
			float tw = MusicDrawing.Measure(clock, 0.8f);
			MusicDrawing.RawText(sb, clock, box.X + (box.Width - tw) / 2f, box.Y + 4, color, 0.8f);
			float part = MathHelper.Clamp(left / match.TurnSeconds, 0f, 1f);
			MusicDrawing.Fill(sb, new Rectangle(r.X + 6, r.Bottom - 5, (int)((r.Width - 12) * part), 2), color * 0.85f);
			right = box.X - 8;
		}
		if (hud != null && match.State != MatchState.Joining && right - ex > 30)
		{
			hud.DrawSeatExtras(sb, new Rectangle(ex, r.Y + 8, right - ex, r.Height - 16), seat);
		}
	}

	/// <summary>When a round ends: a banner over the board for a few seconds.</summary>
	private static void DrawBanner(SpriteBatch sb, GameMatch match, IGameHud hud, Rectangle board)
	{
		double age = GameArt.Now - _bannerAt;
		if (match.State != MatchState.Over || age > 3.2)
		{
			return;
		}
		float appear = GameArt.EaseBack(GameArt.Clamp01(age / 0.4));
		float fade = 1f - GameArt.Clamp01((age - 2.6) / 0.6);
		int result = match.Result;
		string title = (result == 2) ? IsaClient.Languages.L10n.T("Draw!") : ((result == match.MySeat) ? IsaClient.Languages.L10n.T("You win!") : IsaClient.Languages.L10n.F("{0} wins", match.OpponentName));
		Color color = (result == 2) ? Color.White : ((result == match.MySeat) ? Win : Lose);
		int h = 64;
		Rectangle band = new Rectangle(board.X, board.Center.Y - h / 2, board.Width, h);
		MusicDrawing.Fill(sb, band, Color.Black * 0.62f * fade);
		MusicDrawing.Fill(sb, new Rectangle(band.X, band.Y, band.Width, 2), color * 0.8f * fade);
		MusicDrawing.Fill(sb, new Rectangle(band.X, band.Bottom - 2, band.Width, 2), color * 0.8f * fade);
		float scale = 0.62f * appear;
		string fitted = MusicDrawing.FitBig(title, board.Width - 20, scale);
		Vector2 size = Terraria.GameContent.FontAssets.DeathText.Value.MeasureString(fitted) * scale;
		Utils.DrawBorderStringBig(sb, fitted, new Vector2(band.Center.X - size.X / 2f, band.Y + 6), color * fade, scale);
		string note = hud?.Note;
		if (!string.IsNullOrEmpty(note))
		{
			float nw = MusicDrawing.Measure(note, 0.7f);
			MusicDrawing.RawText(sb, note, band.Center.X - nw / 2f, band.Bottom - 22, Color.White * 0.85f * fade, 0.7f);
		}
	}

	/// <summary>Status on the left, the buttons on the right.</summary>
	private static void DrawFooter(SpriteBatch sb, GameMatch match, IGameHud hud, Rectangle r)
	{
		int buttonsW = 0;
		if (match.State == MatchState.Playing)
		{
			bool armed = Main.timeForVisualEffects < _resignArmedUntil;
			Rectangle b = new Rectangle(r.Right - 104, r.Y + 4, 104, 28);
			if (MusicDrawing.TextButton(sb, "games:resign", b, armed ? "Sure?" : "Resign", armed ? Danger : Color.Lerp(MusicPlayerWindow.PanelColor, Danger, 0.35f), "Give up this round (click twice)"))
			{
				if (armed)
				{
					_resignArmedUntil = 0.0;
					GameLobby.ResignRound();
				}
				else
				{
					_resignArmedUntil = Main.timeForVisualEffects + 180.0;
				}
			}
			buttonsW = 112;
		}
		else if (match.State == MatchState.Over)
		{
			string label = match.VsBot ? "Play again" : (match.IWantRematch ? "Waiting..." : (match.TheyWantRematch ? "Accept rematch" : "Rematch"));
			Rectangle b = new Rectangle(r.Right - 130, r.Y + 4, 130, 28);
			bool glow = match.TheyWantRematch && !match.IWantRematch;
			Color color = glow ? Color.Lerp(Green, Color.White, 0.2f * GameArt.Pulse(1.0)) : Green;
			if (MusicDrawing.TextButton(sb, "games:rematch", b, label, color, match.VsBot ? "Another round (you swap who starts)" : "Play another round (the other player starts)", !match.IWantRematch))
			{
				GameLobby.AskRematch();
			}
			buttonsW = 138;
		}
		else if (match.State == MatchState.Abandoned)
		{
			if (MusicDrawing.TextButton(sb, "games:closebtn", new Rectangle(r.Right - 100, r.Y + 4, 100, 28), "Close", MusicPlayerWindow.PanelColor))
			{
				GameLobby.LeaveMatch();
				return;
			}
			buttonsW = 108;
		}
		string status = Status(match, hud);
		Color statusColor = match.MyTurn ? MusicPlayerWindow.AccentColor : ((match.State == MatchState.Abandoned) ? Lose : Color.White);
		if (match.State == MatchState.Over)
		{
			int result = match.Result;
			statusColor = (result == 2) ? Color.White : ((result == match.MySeat) ? Win : Lose);
		}
		MusicDrawing.RawText(sb, status, r.X + 2, r.Y + 9, statusColor, 0.76f, r.Width - buttonsW - 6);
	}

	private static void DrawMinimized(SpriteBatch sb, GameMatch match, int width, int screenW, int screenH)
	{
		Rectangle bar = Place(_board, Math.Min(width, 360), 40, screenW, screenH, 0);
		Window(sb, bar, 18);
		match.Game.DrawIcon(sb, new Rectangle(bar.X + 9, bar.Y + 8, 24, 24), Color.White);
		string status = Status(match, match.Board as IGameHud);
		bool myTurn = match.MyTurn;
		Color color = myTurn ? Color.Lerp(MusicPlayerWindow.AccentColor, Color.White, 0.3f * GameArt.Pulse(1.0)) : Color.White;
		MusicDrawing.RawText(sb, status, bar.X + 40, bar.Y + 11, color, 0.72f, bar.Width - 80);
		if (MusicDrawing.TextButton(sb, "games:restore", new Rectangle(bar.Right - 34, bar.Y + 9, 24, 22), "+", MusicPlayerWindow.PanelColor, "Show the board"))
		{
			_minimized = false;
		}
		Drag(_board, new Rectangle(bar.X, bar.Y, bar.Width - 40, bar.Height), screenW, screenH);
	}

	private static string Status(GameMatch match, IGameHud hud)
	{
		Func<string, string> t = IsaClient.Languages.L10n.T;
		string note = hud?.Note;
		switch (match.State)
		{
		case MatchState.Joining:
			return IsaClient.Languages.L10n.F("Joining {0}'s game...", match.OpponentName);
		case MatchState.Abandoned:
			return IsaClient.Languages.L10n.F("{0} left the game.", match.OpponentName);
		case MatchState.Over:
		{
			int result = match.Result;
			string text = (result == 2) ? t("Draw!") : ((result == match.MySeat) ? t("You win!") : IsaClient.Languages.L10n.F("{0} wins.", match.OpponentName));
			if (match.ForcedResult < 0 && !string.IsNullOrEmpty(note))
			{
				text = note + "  ·  " + text;
			}
			if (match.TheyWantRematch && !match.IWantRematch)
			{
				text += " " + t("Rematch?");
			}
			return text;
		}
		default:
		{
			if (match.Game.RealTime)
			{
				return note ?? t("Playing");
			}
			string seatName = (hud != null) ? (" (" + hud.SeatName(match.MySeat) + ")") : "";
			string text = match.MyTurn ? (t("Your turn") + seatName) : (match.VsBot ? IsaClient.Languages.L10n.F("{0} is thinking", match.OpponentName) : IsaClient.Languages.L10n.F("{0}'s turn", match.OpponentName));
			if (!string.IsNullOrEmpty(note))
			{
				text += "  ·  " + note;
			}
			return text;
		}
		}
	}

	#endregion
}
