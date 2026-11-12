using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

using IsaClient.Languages;

namespace IsaClient.Core;

/// <summary>
/// .help, built from the descriptions each command gets when it is registered (<see cref="CommandCatalog"/>):
/// <c>.help</c> lists the sections, <c>.help 3</c> / <c>.help combat [page]</c> lists a section's commands a page at a
/// time (like TShock), <c>.help .killaura</c> explains one command.
/// </summary>
public static class CommandHelp
{
	private const int PageSize = 8;

	private static readonly Color Muted = new Color(175, 175, 175);

	/// <summary>.help</summary>
	internal static void HelpCommand(CommandContext command)
	{
		if (command.ArgumentCount == 0 || command.Parts[1].Length == 0)
		{
			PrintSections();
			return;
		}
		string topic = command.Parts[1];
		bool isCommand = topic.Length > 1 && (topic[0] == '.' || topic[0] == ChatCommands.CommandSpecifier);
		if (!isCommand)
		{
			CommandSection section = CommandSection.Find(topic);
			if (section != null)
			{
				int page = 1;
				if (command.ArgumentCount > 1)
				{
					int.TryParse(command.Parts[2], out page);
				}
				PrintSection(section, page);
				return;
			}
		}
		PrintCommand((isCommand ? topic.Substring(1) : topic).ToLowerInvariant());
	}

	private static string Prefix => ChatCommands.CommandSpecifier.ToString();

	private static void PrintSections()
	{
		// Each part in the player's language (L10n); the commands themselves stay as they are typed.
		ClientChat.Info(ClientChat.Tint(L10n.T("IsaClient commands"), ClientChat.AccentColor) + ClientChat.Tint(L10n.T("  -  " + Prefix + "help <number|section> lists them, " + Prefix + "help ." + "<command> explains one"), Muted));
		foreach (CommandSection section in CommandSection.All)
		{
			int count = EntriesOf(section).Count;
			ClientChat.Info(ClientChat.Tint(section.Number + ". " + L10n.T(section.Title), section.Color) + ClientChat.Tint(" (" + count + ")", Muted) + "  " + L10n.T(section.Summary));
		}
	}

	private static void PrintSection(CommandSection section, int page)
	{
		List<CommandInfo> entries = EntriesOf(section);
		int pages = Math.Max(1, (entries.Count + PageSize - 1) / PageSize);
		page = Math.Max(1, Math.Min(page, pages));
		ClientChat.Info(ClientChat.Tint(section.Number + ". " + L10n.T(section.Title), section.Color) + ClientChat.Tint(L10n.T("  page " + page + "/" + pages), Muted));
		foreach (CommandInfo info in entries.Skip((page - 1) * PageSize).Take(PageSize))
		{
			ClientChat.Info(ClientChat.Tint(Prefix + info.Name, section.Color) + Arguments(info) + ClientChat.Tint(" - ", Muted) + L10n.T(info.Summary));
		}
		string footer = (page < pages)
			? Prefix + "help " + section.Key + " " + (page + 1) + " for the next page, " + Prefix + "help .<command> for details"
			: Prefix + "help .<command> for details and other names";
		ClientChat.Info(ClientChat.Tint(L10n.T(footer), Muted));
	}

	private static void PrintCommand(string name)
	{
		if (ChatCommands.CommandAbbreviations.TryGetValue(name, out string target))
		{
			string expanded = target.TrimStart('.', ChatCommands.CommandSpecifier).Split(' ')[0].ToLowerInvariant();
			ClientChat.Info(ClientChat.Tint(Prefix + name, ClientChat.AccentColor) + " is your abbreviation for " + Prefix + target.TrimStart('.', ChatCommands.CommandSpecifier) + ".");
			name = expanded;
		}
		CommandInfo info = CommandRegistry.FindHelp(name);
		if (info == null)
		{
			if (CommandRegistry.IsRegistered(name))
			{
				ClientChat.Notice(Prefix + name + " has no description yet.");
			}
			else
			{
				ClientChat.Error("There is no command or section called '" + name + "'. Type " + Prefix + "help to see the sections.");
			}
			return;
		}
		Color color = info.Section.Color;
		ClientChat.Info(ClientChat.Tint(Prefix + info.Name, color) + Arguments(info));
		ClientChat.Info(info.Summary);
		if (info.Names.Count > 1)
		{
			ClientChat.Info(ClientChat.Tint(L10n.T("Other names: " + string.Join(", ", info.Names.Skip(1).Select((string n) => Prefix + n))), Muted));
		}
		ClientChat.Info(ClientChat.Tint(L10n.T("Section: "), Muted) + ClientChat.Tint(L10n.T(info.Section.Title), color) + ClientChat.Tint(" (" + Prefix + "help " + info.Section.Key + ")", Muted));
	}

	private static string Arguments(CommandInfo info)
	{
		return (info.Usage.Length > 0) ? (" " + info.Usage) : "";
	}

	/// <summary>A section's commands, alphabetical.</summary>
	private static List<CommandInfo> EntriesOf(CommandSection section)
	{
		return CommandRegistry.HelpEntries.Where((CommandInfo e) => e.Section == section).OrderBy((CommandInfo e) => e.Name, StringComparer.Ordinal).ToList();
	}
}
