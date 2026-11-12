using System.Collections.Generic;

namespace IsaClient.Core;

/// <summary>
/// What .help knows about one command: its names (the first is the main one), section, arguments and what it does.
/// Created by <see cref="CommandRegistry.Register"/> (then <see cref="Help"/>) or <see cref="CommandRegistry.Describe"/>.
/// </summary>
public sealed class CommandInfo
{
	internal CommandInfo(IReadOnlyList<string> names)
	{
		Names = names;
	}

	/// <summary>All names, main name first; the others are aliases.</summary>
	public IReadOnlyList<string> Names { get; }

	public string Name => Names[0];

	/// <summary>Null until <see cref="Help"/> is called: the command is then left out of .help.</summary>
	public CommandSection Section { get; private set; }

	/// <summary>The arguments, e.g. "[size]" or "&lt;id|name&gt; [seconds]" ("" when it takes none).</summary>
	public string Usage { get; private set; } = "";

	/// <summary>What the command does, one or two short sentences.</summary>
	public string Summary { get; private set; } = "";

	/// <summary>Adds the command to .help.</summary>
	public CommandInfo Help(CommandSection section, string usage, string summary)
	{
		bool listed = Section != null;
		Section = section;
		Usage = usage;
		Summary = summary;
		if (!listed)
		{
			CommandRegistry.AddHelp(this);
		}
		return this;
	}
}
