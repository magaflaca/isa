using System;
using System.Collections.Generic;

namespace IsaClient.Core;

/// <summary>Runs one chat command.</summary>
public delegate void CommandHandler(CommandContext command);

/// <summary>
/// Every chat command of the client. Modules register their commands by name (<see cref="Register"/>); a few modules parse
/// their own command lines and register a parser instead (<see cref="RegisterParser"/>). Commands are looked up in
/// registration order: names registered before a parser win over it, names registered after it only run when it declines.
/// For a name registered twice, the first registration wins.
/// </summary>
public static class CommandRegistry
{
	private sealed class Stage
	{
		public readonly Dictionary<string, CommandHandler> Handlers = new Dictionary<string, CommandHandler>(StringComparer.Ordinal);

		public Func<CommandContext, bool> Parser;
	}

	private static readonly List<Stage> _stages = new List<Stage>();

	private static readonly HashSet<string> _names = new HashSet<string>(StringComparer.Ordinal);

	private static readonly List<CommandInfo> _help = new List<CommandInfo>();

	/// <summary>Commands described for .help, in the order they were described.</summary>
	public static IReadOnlyList<CommandInfo> HelpEntries => _help;

	/// <summary>
	/// Registers a command under all its names (lower case). Chain <see cref="CommandInfo.Help"/> to list it in .help:
	/// <c>Register(Module.ThingCommand, "thing", "th").Help(CommandSection.World, "[size]", "Does the thing.");</c>
	/// </summary>
	public static CommandInfo Register(CommandHandler handler, params string[] names)
	{
		if (_stages.Count == 0 || _stages[_stages.Count - 1].Parser != null)
		{
			_stages.Add(new Stage());
		}
		var handlers = _stages[_stages.Count - 1].Handlers;
		foreach (string name in names)
		{
			if (_names.Add(name))
			{
				handlers[name] = handler;
			}
		}
		return new CommandInfo(names);
	}

	/// <summary>
	/// Describes for .help a command that is not registered by name: one handled by a parser (<see cref="RegisterParser"/>),
	/// by its own chat hook (music, world download) or one sub-command of a shared handler.
	/// </summary>
	public static CommandInfo Describe(CommandSection section, string usage, string summary, params string[] names)
	{
		return new CommandInfo(names).Help(section, usage, summary);
	}

	internal static void AddHelp(CommandInfo info)
	{
		_help.Add(info);
	}

	/// <summary>The .help entry with this name or alias; null when there is none.</summary>
	public static CommandInfo FindHelp(string name)
	{
		foreach (CommandInfo info in _help)
		{
			foreach (string candidate in info.Names)
			{
				if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
				{
					return info;
				}
			}
		}
		return null;
	}

	/// <summary>Registers a module that parses its own command lines; it returns false for commands that are not its own.</summary>
	public static void RegisterParser(Func<CommandContext, bool> parser)
	{
		_stages.Add(new Stage { Parser = parser });
	}

	/// <summary>True when a command with this name was registered (parsers are not asked).</summary>
	public static bool IsRegistered(string name)
	{
		return _names.Contains(name);
	}

	/// <summary>Runs the command; false when no module knows it.</summary>
	public static bool TryExecute(CommandContext command)
	{
		foreach (Stage stage in _stages)
		{
			if (stage.Parser != null)
			{
				if (stage.Parser(command))
				{
					return true;
				}
			}
			else if (stage.Handlers.TryGetValue(command.Name, out var handler))
			{
				handler(command);
				return true;
			}
		}
		return false;
	}
}
