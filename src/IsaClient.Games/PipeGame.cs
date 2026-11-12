using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace IsaClient.Games;

/// <summary>
/// A two-player game played over the pipe or against the computer. <see cref="GameLobby"/> does everything around it
/// (inviting, accepting, turns, rematches, the computer's turns, someone leaving); a game only keeps its board, checks
/// moves, thinks for the computer and draws itself. To add a game: a class implementing <see cref="IGameBoard"/> and one
/// <see cref="GameLobby.Register"/> call with a new id.
/// </summary>
public sealed class PipeGame
{
	/// <summary>Sent in every message: never reuse an id for another game.</summary>
	public byte Id;

	public string Name;

	public string Description;

	/// <summary>Item shown as its icon (when there is no <see cref="Sprite"/>).</summary>
	public int Icon;

	/// <summary>A cell of <see cref="GameArt.Atlas"/> shown as its icon instead of an item.</summary>
	public Rectangle? Sprite;

	public Func<IGameBoard> Create;

	/// <summary>The <see cref="Scores"/> board games against players count on (0 none). Never against the computer.</summary>
	public byte ScoreBoard;

	/// <summary>Both play at once (no turns, no turn clock): the board is an <see cref="IRealtimeBoard"/>.</summary>
	public bool RealTime;

	/// <summary>Board sizes (small, medium, large) in UI pixels.</summary>
	public int[] BoardSizes = { 180, 240, 300 };

	/// <summary>Who moves first in the games you start: 0 you, 1 the other player, 2 random.</summary>
	public GameOption First;

	/// <summary>Seconds per turn in the games you invite to.</summary>
	public GameOption TurnTime;

	/// <summary>How well the computer plays.</summary>
	public GameOption BotLevel;

	/// <summary>The computer waits at least this long before it moves, so it feels like a turn.</summary>
	public double BotDelay = 0.6;

	public void DrawIcon(SpriteBatch sb, Rectangle area, Color color)
	{
		if (Sprite.HasValue)
		{
			GameArt.Sharp(sb);
			float size = Math.Min(area.Width, area.Height);
			GameArt.Sprite(sb, Sprite.Value, new Vector2(area.Center.X, area.Center.Y), size, color);
			GameArt.Smooth(sb);
			return;
		}
		if (Icon <= 0)
		{
			return;
		}
		Main.instance.LoadItem(Icon);
		Texture2D texture = TextureAssets.Item[Icon].Value;
		Rectangle frame = (Main.itemAnimations[Icon] != null) ? Main.itemAnimations[Icon].GetFrame(texture) : texture.Bounds;
		IsaClient.Music.MusicDrawing.Icon(sb, texture, frame, area, color);
	}

	/// <summary>How the "who moves first" choice reads for whoever got the invitation (their first is your second).</summary>
	public string FirstFor(int choice, bool invited)
	{
		if (First == null)
		{
			return null;
		}
		if (invited && choice < 2)
		{
			choice = 1 - choice;
		}
		return First.Label(choice);
	}
}

/// <summary>A choice of a game (who starts, time, the computer's level) kept in the settings.</summary>
public sealed class GameOption
{
	public string Name;

	public string Hint;

	public string[] Labels;

	public int[] Values;

	public Func<int> Get;

	public Action<int> Set;

	public string Label(int value)
	{
		int i = Array.IndexOf(Values, value);
		return IsaClient.Languages.L10n.T((i >= 0) ? Labels[i] : value.ToString());
	}

	public string Current => Label(Get());
}

/// <summary>One round of a game. Seats: 0 = who invited (or you against the computer), 1 = the other. Moves are small byte arrays.</summary>
public interface IGameBoard
{
	/// <summary>A new round; <paramref name="firstSeat"/> moves first.</summary>
	void Reset(int firstSeat);

	/// <summary>The seat whose turn it is.</summary>
	int Turn { get; }

	/// <summary>-1 still playing, 0 or 1 that seat won, 2 a draw.</summary>
	int Result { get; }

	/// <summary>Applies a move by <paramref name="seat"/>; false when it isn't valid (then nothing changes).</summary>
	bool Apply(int seat, byte[] move);

	/// <summary>Draws the board for <paramref name="mySeat"/>; returns the move the player made this frame, or null.</summary>
	byte[] Draw(SpriteBatch sb, Rectangle area, int mySeat, bool canMove);

	/// <summary>
	/// The computer's move for the seat whose turn it is: called on the game thread, it copies what it needs and returns
	/// the thinking, which runs in the background and must not touch the board.
	/// </summary>
	Func<byte[]> Think(int level);
}

/// <summary>Extras a board can show around itself in the game window.</summary>
public interface IGameHud
{
	/// <summary>What a seat plays as ("White", "X").</summary>
	string SeatName(int seat);

	/// <summary>A little picture of the seat's side (a king, an X).</summary>
	void DrawSeat(SpriteBatch sb, Rectangle area, int seat);

	/// <summary>Next to a player's name (captured pieces...).</summary>
	void DrawSeatExtras(SpriteBatch sb, Rectangle area, int seat);

	/// <summary>Width of a column right of the board (0 = none).</summary>
	int SideWidth { get; }

	void DrawSide(SpriteBatch sb, Rectangle area);

	/// <summary>A word about the position ("Check!") or how the round ended ("Checkmate"), or null.</summary>
	string Note { get; }
}
