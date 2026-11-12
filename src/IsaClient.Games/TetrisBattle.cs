using System;
using System.Collections.Generic;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;

namespace IsaClient.Games;

/// <summary>A game that runs on its own clock (no turns): the lobby ticks it and sends what it says.</summary>
public interface IRealtimeBoard
{
	/// <summary>Every tick while playing; returns events for the other player (may be empty).</summary>
	List<byte[]> Tick(int mySeat, bool input);
}

/// <summary>
/// Tetris versus another IsaClient player, both at once: clearing 2, 3 or 4 lines sends 1, 2 or 4 gray garbage rows
/// that rise under the other well; the first to top out loses. Each side plays its own well (<see cref="TetrisField"/>)
/// and sends: garbage [1, lines], its well after each lock [2, 40 bytes of rows, score 4, lines 2] (a few times a
/// second at most), and that it lost [3]. Counts on the Tetris Versus board (rank points), never against the computer.
/// </summary>
internal sealed class TetrisBattle : IGameBoard, IGameHud, IRealtimeBoard
{
	public static readonly PipeGame Game = new PipeGame
	{
		Id = 3,
		Name = "Tetris Versus",
		Description = "Tetris against a player, at the same time: clear 2, 3 or 4 lines to push gray garbage up under them. Last one standing wins.",
		Icon = ItemID.RubyGemsparkBlock,
		Create = () => new TetrisBattle(),
		ScoreBoard = Scores.TetrisVersus,
		// Without turns there's no turn clock: this option is sent in its place (the inviter's speed is the game's).
		TurnTime = Tetris.SpeedOption,
		BoardSizes = new[] { 400, 460, 540 },
		RealTime = true
	};

	private const byte Garbage = 1;

	private const byte Well = 2;

	private const byte Lost = 3;

	private TetrisField _field = new TetrisField();

	private byte[] _theirWell;

	private int _theirScore;

	private int _theirLines;

	private bool _theyLost;

	private int _sent;

	private int _received;

	private KeyboardState _prevKeys;

	private bool _wellPending;

	private double _wellSentAt;

	private bool _input;

	private double _endedAt;

	/// <summary>The versus game being played now, if any (for its music).</summary>
	public static TetrisBattle Active => (GameLobby.Match != null && GameLobby.Match.State == MatchState.Playing) ? (GameLobby.Match.Board as TetrisBattle) : null;

	public int Level => _field.Level;

	public int Turn => 0;

	public int Result { get; private set; } = -1;

	public void Reset(int firstSeat)
	{
		_field = new TetrisField(GameLobby.Match?.TurnSeconds ?? Tetris.Speed);
		_field.Start();
		_theirWell = null;
		_theirScore = 0;
		_theirLines = 0;
		_theyLost = false;
		_sent = 0;
		_received = 0;
		_wellPending = true;
		Result = -1;
		TetrisMusic.Restart();
	}

	/// <summary>What the other player sent.</summary>
	public bool Apply(int seat, byte[] move)
	{
		if (move == null || move.Length == 0)
		{
			return false;
		}
		switch (move[0])
		{
		case Garbage:
			if (move.Length >= 2 && Result == -1)
			{
				_field.AddGarbage(move[1]);
				_received += move[1];
			}
			return true;
		case Well:
			if (move.Length >= 1 + TetrisField.Rows * 2 + 6)
			{
				_theirWell = new byte[TetrisField.Rows * 2];
				Array.Copy(move, 1, _theirWell, 0, _theirWell.Length);
				_theirScore = BitConverter.ToInt32(move, 1 + TetrisField.Rows * 2);
				_theirLines = BitConverter.ToUInt16(move, 5 + TetrisField.Rows * 2);
			}
			return true;
		case Lost:
			if (Result == -1)
			{
				_theyLost = true;
				Result = 1 - seat;
				_endedAt = GameArt.Now;
			}
			return true;
		}
		return false;
	}

	public List<byte[]> Tick(int mySeat, bool input)
	{
		List<byte[]> events = new List<byte[]>();
		_input = input;
		KeyboardState keys = Main.keyState;
		if (Result == -1)
		{
			_field.SendGarbage = lines =>
			{
				_sent += lines;
				events.Add(new[] { Garbage, (byte)lines });
			};
			_field.Step(keys, _prevKeys, input);
			_field.SendGarbage = null;
			if (_field.Locked)
			{
				_field.Locked = false;
				_wellPending = true;
			}
			if (_field.Over)
			{
				Result = 1 - mySeat;
				_endedAt = GameArt.Now;
				_wellPending = true;
				events.Add(new[] { Lost });
			}
		}
		// Your well for their screen, a few times a second at most.
		if (_wellPending && GameArt.Now - _wellSentAt > 0.6)
		{
			_wellPending = false;
			_wellSentAt = GameArt.Now;
			List<byte> data = new List<byte> { Well };
			data.AddRange(_field.Snapshot());
			data.AddRange(BitConverter.GetBytes(_field.Score));
			data.AddRange(BitConverter.GetBytes((ushort)Math.Min(_field.Lines, 65535)));
			events.Add(data.ToArray());
		}
		_prevKeys = keys;
		return events;
	}

	public Func<byte[]> Think(int level)
	{
		return () => null;
	}

	#region Drawing

	public byte[] Draw(SpriteBatch sb, Rectangle area, int mySeat, bool canMove)
	{
		int cell = (area.Height - 12) / TetrisField.Rows;
		int wellW = cell * TetrisField.Cols;
		int wellH = cell * TetrisField.Rows;
		int small = Math.Max(6, cell / 2);
		int left = 86;
		int theirW = small * TetrisField.Cols;
		int total = left + 6 + 8 + 6 + wellW + 14 + theirW;
		int x = area.X + Math.Max(0, (area.Width - total) / 2);
		int y = area.Y + 6;
		// Hold and next.
		Rectangle hold = new Rectangle(x, y, left, 70);
		Tetris.Box(sb, hold, "tetris::Hold");
		TetrisField.MiniPiece(sb, _field.Hold, new Rectangle(hold.X + 4, hold.Y + 22, hold.Width - 8, hold.Height - 26), 14, _field.CanHold ? 1f : 0.4f);
		Rectangle next = new Rectangle(x, hold.Bottom + 8, left, 120);
		Tetris.Box(sb, next, "Next");
		for (int i = 0; i < Math.Min(2, _field.Next.Count); i++)
		{
			TetrisField.MiniPiece(sb, _field.Next[i], new Rectangle(next.X + 4, next.Y + 24 + i * 46, next.Width - 8, 42), (i == 0) ? 15 : 11, (i == 0) ? 1f : 0.75f);
		}
		Rectangle stats = new Rectangle(x, next.Bottom + 8, left, 110);
		MusicDrawing.Box(sb, stats, MusicPlayerWindow.PanelDarkColor * 0.9f);
		MusicDrawing.Text(sb, "Score", stats.X + 8, stats.Y + 5, MusicPlayerWindow.MutedColor, 0.56f);
		MusicDrawing.RawText(sb, _field.Score.ToString("N0"), stats.X + 8, stats.Y + 18, Color.White, 0.78f, left - 12);
		MusicDrawing.Text(sb, "tetris::Sent", stats.X + 8, stats.Y + 42, MusicPlayerWindow.MutedColor, 0.56f);
		MusicDrawing.RawText(sb, _sent.ToString(), stats.X + 8, stats.Y + 55, new Color(255, 150, 130), 0.78f);
		MusicDrawing.Text(sb, "Level", stats.X + 8, stats.Y + 78, MusicPlayerWindow.MutedColor, 0.56f);
		MusicDrawing.RawText(sb, _field.Level.ToString(), stats.X + 46, stats.Y + 76, new Color(140, 220, 255), 0.72f);
		// Garbage meter, then your well.
		int bx = x + left + 6;
		Rectangle wellRect = new Rectangle(bx + 8 + 6, y, wellW, wellH);
		_field.DrawGarbageBar(sb, new Rectangle(bx, y, 8, wellH), cell);
		GameOverlay.MenuPanel(sb, new Rectangle(wellRect.X - 5, wellRect.Y - 5, wellRect.Width + 10, wellRect.Height + 10), new Color(14, 16, 34), _input ? Main.OurFavoriteColor * 0.8f : Color.Black);
		_field.DrawWell(sb, wellRect, cell);
		// Their well, small.
		Rectangle theirs = new Rectangle(wellRect.Right + 14, y + 26, theirW, small * TetrisField.Rows);
		MusicDrawing.Text(sb, "Rival", theirs.X, y + 2, MusicPlayerWindow.MutedColor, 0.62f);
		GameOverlay.MenuPanel(sb, new Rectangle(theirs.X - 4, theirs.Y - 4, theirs.Width + 8, theirs.Height + 8), new Color(14, 16, 34), Color.Black);
		TetrisField.DrawSnapshot(sb, _theirWell, theirs, small, _theyLost);
		MusicDrawing.RawText(sb, _theirScore.ToString("N0"), theirs.X, theirs.Bottom + 8, Color.White, 0.7f, theirW);
		MusicDrawing.RawText(sb, IsaClient.Languages.L10n.F("{0} lines", _theirLines), theirs.X, theirs.Bottom + 26, MusicPlayerWindow.MutedColor, 0.58f, theirW);
		if (_theyLost)
		{
			Stamp(sb, theirs, "K.O.", new Color(255, 214, 80));
		}
		if (_field.Over)
		{
			Stamp(sb, wellRect, IsaClient.Languages.L10n.T("Topped out"), new Color(255, 120, 120));
		}
		else if (Result == -1 && !_input)
		{
			MusicDrawing.Fill(sb, new Rectangle(wellRect.X, wellRect.Center.Y - 22, wellRect.Width, 44), Color.Black * 0.6f);
			Tetris.Centered(sb, IsaClient.Languages.L10n.T("Click here for the keys"), wellRect, wellRect.Center.Y - 9, MusicPlayerWindow.AccentColor, 0.66f);
		}
		if (Result == -1 && _input)
		{
			// Your character stands still while the keys are the game's.
			PlayerInput.WritingText = true;
			Main.chatRelease = false;
		}
		if (MusicPlayerWindow.Clicked && MusicPlayerWindow.MouseIn(area))
		{
			MusicPlayerWindow.Clicked = false;
			GameOverlay.FocusBoard();
		}
		return null;
	}

	private static void Stamp(SpriteBatch sb, Rectangle r, string text, Color color)
	{
		MusicDrawing.Fill(sb, r, Color.Black * 0.45f);
		float scale = Math.Min(0.6f, r.Width / 220f);
		string fitted = MusicDrawing.FitBig(text, r.Width - 8, scale);
		Vector2 size = Terraria.GameContent.FontAssets.DeathText.Value.MeasureString(fitted) * scale;
		Utils.DrawBorderStringBig(sb, fitted, new Vector2(r.Center.X - size.X / 2f, r.Center.Y - size.Y / 2f), color, scale);
	}

	#endregion

	#region Hud

	public string SeatName(int seat)
	{
		return "";
	}

	public void DrawSeat(SpriteBatch sb, Rectangle area, int seat)
	{
	}

	public void DrawSeatExtras(SpriteBatch sb, Rectangle area, int seat)
	{
	}

	public int SideWidth => 0;

	public void DrawSide(SpriteBatch sb, Rectangle area)
	{
	}

	public string Note
	{
		get
		{
			if (Result != -1)
			{
				return _theyLost ? IsaClient.Languages.L10n.T("They topped out") : IsaClient.Languages.L10n.T("You topped out");
			}
			return (_field.PendingGarbage > 0) ? IsaClient.Languages.L10n.F("{0} garbage lines coming!", _field.PendingGarbage) : IsaClient.Languages.L10n.F("Sent {0} · got {1}", _sent, _received);
		}
	}

	#endregion
}
