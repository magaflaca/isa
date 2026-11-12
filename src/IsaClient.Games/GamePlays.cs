using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IsaClient.Core;

namespace IsaClient.Games;

/// <summary>
/// How many times you've played each game (opened its window or started a match), so the Games page lists the ones
/// you play most first. Ties keep the default order.
/// </summary>
public static class GamePlays
{
	public const string Casino = "casino";

	public const string Chess = "chess";

	public const string Gba = "gba";

	public const string Tetris = "tetris";

	public const string Snake = "snake";

	public const string TetrisVersus = "versus";

	public const string TicTacToe = "ttt";

	/// <summary>The order before you've played anything.</summary>
	public static readonly string[] Default = { Casino, Gba, Chess, Tetris, Snake, TetrisVersus, TicTacToe };

	/// <summary>Plays per game, as "casino=3;chess=12".</summary>
	[Setting("GamesPlayed")]
	public static string Played = "";

	/// <summary>One more play of <paramref name="key"/>.</summary>
	public static void Count(string key)
	{
		Dictionary<string, int> counts = Read();
		counts.TryGetValue(key, out int count);
		counts[key] = count + 1;
		Played = string.Join(";", counts.Select(p => p.Key + "=" + p.Value.ToString(CultureInfo.InvariantCulture)));
		try
		{
			ClientSettings.Save();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	/// <summary>A pipe game's match started.</summary>
	public static void CountMatch(PipeGame game)
	{
		if (game == IsaClient.Games.Chess.Game)
		{
			Count(Chess);
		}
		else if (game == TetrisBattle.Game)
		{
			Count(TetrisVersus);
		}
		else if (game == IsaClient.Games.TicTacToe.Game)
		{
			Count(TicTacToe);
		}
	}

	/// <summary>The games, most played first (ties in the default order).</summary>
	public static string[] Order()
	{
		Dictionary<string, int> counts = Read();
		return Default.Select((key, index) => (key, index, plays: counts.TryGetValue(key, out int n) ? n : 0))
			.OrderByDescending(g => g.plays)
			.ThenBy(g => g.index)
			.Select(g => g.key)
			.ToArray();
	}

	private static Dictionary<string, int> Read()
	{
		Dictionary<string, int> counts = new Dictionary<string, int>();
		foreach (string part in (Played ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
		{
			int eq = part.IndexOf('=');
			if (eq > 0 && int.TryParse(part.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0)
			{
				counts[part.Substring(0, eq)] = n;
			}
		}
		return counts;
	}
}
