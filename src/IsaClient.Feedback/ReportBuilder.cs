using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using IsaClient.Core;
using Newtonsoft.Json.Linq;
using Terraria;

namespace IsaClient.Feedback;

/// <summary>
/// What a report says about the game and the PC (version, where you were, plugins...) and the Discord embed built from
/// it. Every piece is read on its own, so a broken game state (a crash) still gives the rest.
/// </summary>
internal static class ReportBuilder
{
	/// <summary>The lines of game and system info, as the panel shows them before sending.</summary>
	public static List<(string Name, string Value)> Info()
	{
		return new List<(string, string)>
		{
			("Version", Safe(() => ClientInfo.DisplayName + " · Terraria " + Main.versionNumber)),
			("IsaClient ID", Safe(() => IsaClient.Identity.ClientIdentity.Text)),
			("Where", Safe(Where)),
			("System", Safe(SystemLine)),
			("Memory", Safe(() => (Process.GetCurrentProcess().WorkingSet64 >> 20) + " MB used")),
			("Plugins", Safe(Plugins))
		};
	}

	public static string PlayerName()
	{
		return Safe(() => (Main.LocalPlayer != null && !string.IsNullOrWhiteSpace(Main.LocalPlayer.name)) ? Main.LocalPlayer.name : "");
	}

	private static string Where()
	{
		if (Main.gameMenu)
		{
			return "Main menu";
		}
		return (Main.netMode == 1) ? "Multiplayer" : ("Singleplayer (" + Main.worldName + ")");
	}

	private static string SystemLine()
	{
		string os = Environment.OSVersion.VersionString.Replace("Microsoft ", "");
		return os + (Environment.Is64BitOperatingSystem ? " 64-bit" : " 32-bit") + " · " + Main.screenWidth + "x" + Main.screenHeight;
	}

	private static string Plugins()
	{
		if (IsaClient.Plugins.PluginHost.SystemOff)
		{
			return "Off";
		}
		List<string> names = IsaClient.Plugins.PluginLoader.Entries.Where(e => e.Loaded).Select(e => e.Name).ToList();
		return (names.Count == 0) ? "None loaded" : string.Join(", ", names);
	}

	private static string Safe(Func<string> read)
	{
		try
		{
			return read() ?? "";
		}
		catch
		{
			return "?";
		}
	}

	/// <summary>The Discord message: one embed in the color of the kind, user text first, info as fields.</summary>
	public static string Payload(FeedbackReport report, string details, IEnumerable<(string Name, string Value)> extra, bool withInfo, string errors)
	{
		string emoji = report.Kind switch
		{
			ReportKind.Bug => "\U0001F41E",
			ReportKind.Suggestion => "\U0001F4A1",
			ReportKind.Crash => "\U0001F4A5",
			_ => "\U0001F4AC"
		};
		Microsoft.Xna.Framework.Color color = FeedbackReport.KindColor(report.Kind);
		JArray fields = new JArray();
		foreach ((string name, string value) in extra)
		{
			Field(fields, name, value, inline: true);
		}
		if (withInfo)
		{
			foreach ((string name, string value) in Info())
			{
				Field(fields, name, value, inline: name != "Plugins");
			}
		}
		if (!string.IsNullOrWhiteSpace(errors))
		{
			Field(fields, "Recent errors", "```\n" + Cut(errors, 980) + "\n```", inline: false);
		}
		JObject embed = new JObject
		{
			["title"] = Cut(emoji + " " + FeedbackReport.KindName(report.Kind) + ": " + report.Title, 250),
			["description"] = Cut(details, 4000),
			["color"] = (color.R << 16) | (color.G << 8) | color.B,
			["fields"] = fields,
			["footer"] = new JObject { ["text"] = ClientInfo.DisplayName + " · report " + report.Id + " · user " + FeedbackReports.InstallTag + " · id " + Safe(() => IsaClient.Identity.ClientIdentity.Text) },
			["timestamp"] = report.Time.ToUniversalTime().ToString("o")
		};
		string player = PlayerName();
		if (player.Length > 0)
		{
			embed["author"] = new JObject { ["name"] = Cut(player, 200) };
		}
		JObject payload = new JObject
		{
			["username"] = "IsaClient Reports",
			["embeds"] = new JArray { embed },
			// Whatever the user typed never pings anyone.
			["allowed_mentions"] = new JObject { ["parse"] = new JArray() }
		};
		return payload.ToString(Newtonsoft.Json.Formatting.None);
	}

	private static void Field(JArray fields, string name, string value, bool inline)
	{
		if (string.IsNullOrWhiteSpace(value) || fields.Count >= 25)
		{
			return;
		}
		fields.Add(new JObject { ["name"] = Cut(name, 250), ["value"] = Cut(value, 1020), ["inline"] = inline });
	}

	public static string Cut(string text, int max)
	{
		text ??= "";
		return (text.Length <= max) ? text : (text.Substring(0, max - 1) + "…");
	}
}
