using Terraria;

namespace IsaClient.Core;

/// <summary>One chat command as it was typed: <c>.name arg1 arg2 ...</c></summary>
public sealed class CommandContext
{
	public CommandContext(string text)
	{
		Text = text;
		Parts = text.Substring(1).Split(' ');
		Name = Parts[0].ToLower();
	}

	/// <summary>The whole line, prefix included.</summary>
	public string Text { get; }

	/// <summary>The line split on spaces: [0] is the command name as typed, [1] the first argument.</summary>
	public string[] Parts { get; }

	/// <summary>The command name in lower case.</summary>
	public string Name { get; }

	/// <summary>How many arguments follow the command name.</summary>
	public int ArgumentCount => Parts.Length - 1;

	/// <summary>Everything after the command name and its space ("" when there is nothing).</summary>
	public string Rest => (Text.Length > Parts[0].Length + 2) ? Text.Substring(Parts[0].Length + 2) : "";

	/// <summary>The local player.</summary>
	public Player Player => Main.LocalPlayer;
}
