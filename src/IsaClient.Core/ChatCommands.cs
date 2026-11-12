using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IsaClient.Music;
using Terraria;
using IsaClient.WorldTools;

namespace IsaClient.Core;

/// <summary>
/// Entry point of the chat commands: a line that starts with <see cref="CommandSpecifier"/> runs through the
/// <see cref="CommandRegistry"/> (filled by <see cref="CommandCatalog"/>) instead of going to the chat. Also owns the
/// user's command abbreviations and a few helpers shared by command handlers.
/// </summary>
public static class ChatCommands
{
	[Setting("CommandSpecifierCharacter")]
	public static char CommandSpecifier = '.';

	/// <summary>User abbreviations: ".x args" runs ".command args".</summary>
	public static Dictionary<string, string> CommandAbbreviations = new Dictionary<string, string>();

	private static bool _registered;

	/// <summary>Runs the line when it is a command. Returns false for plain chat.</summary>
	public static bool TryHandle(string chatText)
	{
		if (chatText.Length == 0 || (chatText[0] != CommandSpecifier && !IsPrefixRescue(chatText)))
		{
			return false;
		}
		try
		{
			Execute(chatText);
		}
		catch (Exception ex)
		{
			ClientChat.Error($"Command error!\n{ex}");
			ClientLog.LogException(ex);
		}
		return true;
	}

	/// <summary>Registers every command once (the first command typed does it).</summary>
	internal static void EnsureRegistered()
	{
		if (!_registered)
		{
			_registered = true;
			CommandCatalog.RegisterAll();
		}
	}

	public static void Execute(string chatText)
	{
		EnsureRegistered();
		CommandContext context = new CommandContext(chatText);
		if (IsaClient.Networking.ServerModes.BlocksCommand(context.Name))
		{
			return;
		}
		if (!CommandRegistry.TryExecute(context))
		{
			ClientChat.Error("Unknown command. Type .help for a list of commands.");
		}
	}

	/// <summary>".prefix ..." works whatever the prefix is, so a forgotten prefix can always be changed back.</summary>
	private static bool IsPrefixRescue(string chatText)
	{
		return chatText.StartsWith(".prefix", StringComparison.OrdinalIgnoreCase) && (chatText.Length == 7 || chatText[7] == ' ');
	}

	/// <summary>Characters that cannot start a command: server commands, tags, quotes, letters, digits and spaces.</summary>
	internal static bool IsValidSpecifier(char c)
	{
		return !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && !char.IsControl(c) && c != '/' && c != '[' && c != ']' && c != '"' && c != '\\';
	}

	/// <summary>.prefix [character]</summary>
	internal static void PrefixCommand(CommandContext command)
	{
		string arg = command.Rest.Trim();
		if (arg.Length == 0)
		{
			ClientChat.Info("Commands start with " + CommandSpecifier + " (e.g. " + CommandSpecifier + "help). Change it with .prefix <character>, like .prefix !");
			return;
		}
		if (arg.Length != 1 || !IsValidSpecifier(arg[0]))
		{
			ClientChat.Error("The prefix must be one symbol that is not / [ ] \\ \" or a letter/number. Example: .prefix !");
			return;
		}
		CommandSpecifier = arg[0];
		ClientSettings.Save();
		ClientChat.Info("Commands now start with " + CommandSpecifier + " (e.g. " + CommandSpecifier + "help). .prefix . puts the dot back.");
	}

	/// <summary>True when the text is an IsaClient command (runs locally) rather than a server command or chat.</summary>
	internal static bool IsLocalCommand(string text)
	{
		return !string.IsNullOrEmpty(text) && text.Length >= 2 && text != ".nochat" && text != ".fakechat" && text[0] == CommandSpecifier;
	}

	/// <summary>Runs an IsaClient command as if it had been typed in the chat (used by .spamchat).</summary>
	public static void RunLocalCommand(string text)
	{
		string expanded = ExpandAbbreviation(text);
		string head = expanded.Substring(1).Split(' ')[0].ToLowerInvariant();
		if (head == "spamchat")
		{
			return;
		}
		if (!MapReveal.TryHandle(expanded) && !MusicCommands.TryHandleTyped(expanded))
		{
			TryHandle(expanded);
		}
	}

	/// <summary>Splits a command line honouring "quoted arguments".</summary>
	public static List<string> SplitArguments(string text)
	{
		List<string> list = new List<string>();
		StringBuilder current = new StringBuilder();
		bool quoted = false;
		foreach (char c in text)
		{
			if (c == '"')
			{
				quoted = !quoted;
				continue;
			}
			if (c == ' ' && !quoted)
			{
				if (current.Length > 0)
				{
					list.Add(current.ToString());
					current.Clear();
				}
				continue;
			}
			current.Append(c);
		}
		if (current.Length > 0)
		{
			list.Add(current.ToString());
		}
		return list;
	}

	#region Abbreviations

	/// <summary>Replaces a custom abbreviation (".x args") by its command (".command args") before chat commands run.</summary>
	public static string ExpandAbbreviation(string chatText)
	{
		if (string.IsNullOrEmpty(chatText) || CommandAbbreviations.Count == 0 || (chatText[0] != CommandSpecifier && chatText[0] != '.'))
		{
			return chatText;
		}
		string body = chatText.Substring(1);
		int space = body.IndexOf(' ');
		string head = ((space < 0) ? body : body.Substring(0, space));
		if (head.Length == 0 || !CommandAbbreviations.TryGetValue(head, out var command))
		{
			return chatText;
		}
		return chatText[0] + command + ((space < 0) ? "" : body.Substring(space));
	}

	/// <summary>.abbrev / .abbreviation / .alias: list, add or remove abbreviations.</summary>
	internal static void AbbreviationsCommand(CommandContext command)
	{
		string[] args = command.Parts.Skip(1).Where((string a) => a.Length > 0).ToArray();
		if (args.Length == 0 || (args.Length == 1 && args[0].ToLower() == "list"))
		{
			if (CommandAbbreviations.Count == 0)
			{
				ClientChat.Info("No abbreviations yet. Usage: .abbrev <command> <abbreviation>   .abbrev remove <abbreviation>");
				return;
			}
			ClientChat.Info("Abbreviations:");
			foreach (KeyValuePair<string, string> item in CommandAbbreviations.OrderBy((KeyValuePair<string, string> k) => k.Key))
			{
				ClientChat.Info("  ." + item.Key + "  ->  ." + item.Value);
			}
			return;
		}
		if (args.Length == 2 && (args[0].ToLower() == "remove" || args[0].ToLower() == "del"))
		{
			string key = args[1].TrimStart('.', CommandSpecifier);
			ClientChat.Info(CommandAbbreviations.Remove(key) ? ("Removed abbreviation ." + key) : ("There is no abbreviation ." + key));
			ClientSettings.Save();
			return;
		}
		if (args.Length < 2)
		{
			ClientChat.Error("Usage: .abbrev <command> <abbreviation>   (e.g. .abbrev wlddown wd)");
			return;
		}
		string abbreviation = args[args.Length - 1].TrimStart('.', CommandSpecifier);
		string target = string.Join(" ", args.Take(args.Length - 1)).TrimStart('.', CommandSpecifier);
		if (abbreviation.Length == 0 || target.Length == 0 || abbreviation.Contains(" "))
		{
			ClientChat.Error("Invalid abbreviation.");
			return;
		}
		if (string.Equals(target.Split(' ')[0], abbreviation, StringComparison.OrdinalIgnoreCase))
		{
			ClientChat.Error("An abbreviation cannot point to itself.");
			return;
		}
		CommandAbbreviations[abbreviation] = target;
		ClientSettings.Save();
		ClientChat.Info("." + abbreviation + " now runs ." + target);
	}

	#endregion

	#region Helpers for command handlers

	/// <summary>Flips a toggle and (mode 1) reports it.</summary>
	public static void Toggle(int mode, string cmdName, ref bool boolean)
	{
		boolean = !boolean;
		if (mode == 1)
		{
			ClientChat.Info(cmdName + " " + (boolean ? "En" : "Dis") + "abled");
		}
		if (cmdName == "Noclip" && Main.netMode != 0)
		{
			NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
			NetMessage.SendData(50, -1, -1, null, Main.myPlayer);
		}
	}

	/// <summary>".x" flips the mode, ".x 5" turns it on with size 5.</summary>
	internal static void ToggleWithSize(string[] array, ref bool enable, ref int size, string commandName)
	{
		if (array.Length <= 1)
		{
			enable = !enable;
			ClientChat.Info(commandName + (enable ? " En" : " Dis") + "abled" + (enable ? ("  Size: " + size) : ""));
		}
		else if (int.TryParse(array[1], out size))
		{
			enable = true;
			ClientChat.Info(commandName + " Enabled  Size: " + size);
		}
		else
		{
			ClientChat.Error("Invalid Value ");
		}
	}

	#endregion
}
