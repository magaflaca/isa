using System;
using Microsoft.Xna.Framework;

namespace IsaClient.Feedback;

public enum ReportKind
{
	Bug,
	Suggestion,
	Feedback,
	Crash
}

public enum ReportStatus
{
	/// <summary>On its way.</summary>
	Sending,
	Sent,
	/// <summary>Could not be sent (no internet, rate limit...): can be sent again.</summary>
	Failed,
	/// <summary>A crash kept here because automatic crash reports are off (or it was a repeat): sent only by hand.</summary>
	Saved
}

/// <summary>One report the user sent (or a crash), kept in the history with the exact message to send it again.</summary>
public sealed class FeedbackReport
{
	public string Id;

	public ReportKind Kind;

	public string Title;

	public string Text;

	public DateTime Time;

	public ReportStatus Status;

	/// <summary>Why the last try failed.</summary>
	public string Error;

	/// <summary>The Discord message (JSON), built when the report was made.</summary>
	public string Payload;

	/// <summary>A text file sent with it (full crash log, recent errors), or null.</summary>
	public string Attachment;

	public string AttachmentName;

	/// <summary>Crashes: exception type and first frames, to report each crash only once a day.</summary>
	public string Signature;

	public static string KindName(ReportKind kind)
	{
		return kind switch
		{
			ReportKind.Bug => "Bug",
			ReportKind.Suggestion => "Suggestion",
			ReportKind.Crash => "Crash",
			_ => "Feedback"
		};
	}

	public static Color KindColor(ReportKind kind)
	{
		return kind switch
		{
			ReportKind.Bug => new Color(240, 90, 110),
			ReportKind.Suggestion => new Color(95, 212, 140),
			ReportKind.Crash => new Color(205, 60, 50),
			_ => new Color(90, 170, 255)
		};
	}

	public static int KindIcon(ReportKind kind)
	{
		return kind switch
		{
			ReportKind.Bug => Terraria.ID.ItemID.Buggy,
			ReportKind.Suggestion => Terraria.ID.ItemID.HeartLantern,
			ReportKind.Crash => Terraria.ID.ItemID.Dynamite,
			_ => Terraria.ID.ItemID.Megaphone
		};
	}
}
