using IsaClient.Core;
using IsaClient.Menu;
using Terraria;

namespace IsaClient.Feedback;

/// <summary>.report, .bug and .suggest: open the Report a Bug page, or send a quick report from the chat.</summary>
public static class FeedbackCommands
{
	/// <summary>.report [bug|idea|feedback <text>] | crashes [on|off] | history</summary>
	internal static void ReportCommand(CommandContext command)
	{
		string first = (command.ArgumentCount > 0) ? command.Parts[1].ToLower() : "";
		string rest = (command.ArgumentCount > 0 && command.Rest.Length > command.Parts[1].Length) ? command.Rest.Substring(command.Parts[1].Length).Trim() : "";
		switch (first)
		{
		case "":
			Open(0);
			return;
		case "bug":
			Quick(ReportKind.Bug, rest);
			return;
		case "idea":
		case "suggest":
		case "suggestion":
			Quick(ReportKind.Suggestion, rest);
			return;
		case "feedback":
		case "other":
			Quick(ReportKind.Feedback, rest);
			return;
		case "history":
		case "list":
			Open(2);
			return;
		case "crash":
		case "crashes":
			if (command.ArgumentCount > 1)
			{
				FeedbackReports.AutoCrashReports = command.Parts[2].ToLower() == "on";
				ClientSettings.Save();
			}
			ClientChat.Info("Automatic crash reports: " + (FeedbackReports.AutoCrashReports ? "on" : "off") + ".");
			return;
		default:
			// .report <text> is a bug report.
			Quick(ReportKind.Bug, command.Rest);
			return;
		}
	}

	/// <summary>.bug [text]</summary>
	internal static void BugCommand(CommandContext command)
	{
		if (command.Rest.Trim().Length == 0)
		{
			Open(0, ReportKind.Bug);
			return;
		}
		Quick(ReportKind.Bug, command.Rest);
	}

	/// <summary>.suggest / .idea [text]</summary>
	internal static void SuggestCommand(CommandContext command)
	{
		if (command.Rest.Trim().Length == 0)
		{
			Open(0, ReportKind.Suggestion);
			return;
		}
		Quick(ReportKind.Suggestion, command.Rest);
	}

	private static void Quick(ReportKind kind, string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			Open(0, kind);
			return;
		}
		string error = FeedbackReports.Submit(kind, null, text, null, withInfo: true, withErrors: kind == ReportKind.Bug);
		if (error != null)
		{
			ClientChat.Error(error);
			return;
		}
		ClientChat.Info((kind == ReportKind.Suggestion ? "Suggestion" : (kind == ReportKind.Bug ? "Bug report" : "Feedback")) + " sent. Thanks for helping make IsaClient better!");
	}

	private static void Open(int page, ReportKind? kind = null)
	{
		if (Main.gameMenu)
		{
			ClientChat.Notice("Join a world to open the panel, or send it right here: .bug <what happened> or .suggest <your idea>.");
			return;
		}
		if (kind.HasValue)
		{
			IsaClient.Hub.HubFeedback.Pick(kind.Value);
		}
		IsaClient.Hub.ClientHub.OpenSection("feedback", page);
		InputState.TildeTerminal = true;
		if (IsaClient.Hub.HubPrefs.UseClassic)
		{
			ClientChat.Notice("Reports are in the new panel: .menu new");
		}
	}
}
