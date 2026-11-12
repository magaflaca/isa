using System.Linq;
using IsaClient.Community;
using IsaClient.Core;
using IsaClient.Net.Pipe;
using Terraria;

namespace IsaClient.Games;

internal static class GameCommands
{
	/// <summary>.ttt &lt;player | all | bot | accept | decline | leave&gt;</summary>
	internal static void TicTacToeCommand(CommandContext command)
	{
		GameCommand(command, TicTacToe.Game, ".ttt");
	}

	/// <summary>.chess &lt;player | all | bot | accept | decline | leave&gt;</summary>
	internal static void ChessCommand(CommandContext command)
	{
		GameCommand(command, Chess.Game, ".chess");
	}

	/// <summary>.tetris</summary>
	internal static void TetrisCommand(CommandContext command)
	{
		Tetris.Open();
	}

	/// <summary>.snake</summary>
	internal static void SnakeCommand(CommandContext command)
	{
		Snake.Open();
	}

	/// <summary>.scores: the top 10 in the chat.</summary>
	internal static void ScoresCommand(CommandContext command)
	{
		System.Collections.Generic.List<ScoreEntry> ranking = Scores.Ranking(Scores.Tetris);
		if (ranking.Count == 0)
		{
			ClientChat.Info("No Tetris scores yet: play with .tetris.");
			return;
		}
		ClientChat.Info(IsaClient.Languages.L10n.F("Tetris: top {0} of {1}", System.Math.Min(10, ranking.Count), ranking.Count));
		for (int i = 0; i < System.Math.Min(10, ranking.Count); i++)
		{
			bool me = ranking[i].Id == IsaClient.Identity.ClientIdentity.Id;
			ClientChat.Info("#" + (i + 1) + "  " + ranking[i].Name + "  " + ranking[i].Score.ToString("N0") + (me ? "  <" : ""));
		}
	}

	private static void GameCommand(CommandContext command, PipeGame game, string name)
	{
		string arg = command.Rest.Trim();
		switch (arg.ToLowerInvariant())
		{
		case "":
			ClientChat.Info(IsaClient.Languages.L10n.F("Usage: {0} <player | all | bot | accept | decline | leave>. Also in the panel: Games.", name));
			return;
		case "all":
		case "everyone":
			GameLobby.InvitePlayer(game, PipeNet.Everyone);
			return;
		case "bot":
		case "cpu":
		case "computer":
		case "ai":
			GameLobby.PlayComputer(game);
			return;
		case "accept":
		case "yes":
		{
			GameInvite invite = GameLobby.Invites.LastOrDefault(i => i.Game == game) ?? GameLobby.Invites.LastOrDefault();
			if (invite == null)
			{
				ClientChat.Error("No invitation to answer.");
				return;
			}
			GameLobby.AcceptInvite(invite);
			GameOverlay.Show();
			return;
		}
		case "decline":
		case "no":
		{
			GameInvite invite = GameLobby.Invites.LastOrDefault(i => i.Game == game) ?? GameLobby.Invites.LastOrDefault();
			if (invite != null)
			{
				GameLobby.DeclineInvite(invite);
			}
			return;
		}
		case "leave":
		case "quit":
			GameLobby.CancelInvite(quiet: true);
			GameLobby.LeaveMatch();
			return;
		}
		Player player = ChatTools.FindPlayer(arg, out string error);
		if (player == null)
		{
			ClientChat.Error(error);
			return;
		}
		GameLobby.InvitePlayer(game, player.whoAmI);
	}
}
