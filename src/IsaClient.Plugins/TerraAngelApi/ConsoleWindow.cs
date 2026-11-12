using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Microsoft.Xna.Framework;
using NVector2 = System.Numerics.Vector2;

namespace TerraAngel.UI.ClientWindows.Console;

/// <summary>
/// The plugin console (TerraAngel's ConsoleWindow): plugins write to it and add commands with
/// <c>ClientLoader.Console.AddCommand</c>. Type a command (with or without TerraAngel's #) and press Enter; anything else
/// runs as C# (ScriptConsole). The same works from the chat with <c>.pc &lt;line&gt;</c>.
/// </summary>
public class ConsoleWindow : ClientWindow
{
	/// <summary>One command line: the command and its arguments (TerraAngel's CmdStr).</summary>
	public class CmdStr
	{
		public string Command = "";

		public List<string> Args = new List<string>();

		public string FullMessage = "";

		public string FullArgs = "";

		public CmdStr(string toParse)
		{
			int firstSplit = toParse.IndexOf(' ');
			if (firstSplit > -1)
			{
				FullArgs = toParse.Substring(firstSplit + 1);
				Command = toParse.Substring(0, firstSplit);
				Args = toParse.Substring(firstSplit).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
			}
			else
			{
				Command = toParse;
			}
			FullMessage = toParse;
		}

		public string FullArgsFrom(int index)
		{
			return string.Join(" ", Args.Skip(index));
		}
	}

	public class ConsoleCommand
	{
		public string CommandName;

		public string CommandDescription;

		public Action<CmdStr> CommandAction;

		public ConsoleCommand(string name, Action<CmdStr> action, string description)
		{
			CommandName = name;
			CommandAction = action;
			CommandDescription = description;
		}
	}

	public class ConsoleElement
	{
		public string Text;

		public Color TextColor;

		public int AboveDuplicateCount;

		public ConsoleElement(string text, Color color)
		{
			Text = text;
			TextColor = color;
		}
	}

	public static readonly Color ErrorColor = new Color(255, 110, 110);

	public static readonly Color DefaultColor = new Color(225, 230, 240);

	private const int MaxItems = 600;

	private readonly object _lock = new object();

	private readonly List<string> _history = new List<string>();

	private int _historyPosition = -1;

	private string _input = "";

	private bool _scrollToBottom;

	private bool _focusInput;

	public Dictionary<string, ConsoleCommand> ConsoleCommands = new Dictionary<string, ConsoleCommand>(StringComparer.OrdinalIgnoreCase);

	public List<ConsoleElement> ConsoleItems = new List<ConsoleElement>();

	public override string Title => "Console";

	public override bool DefaultEnabled => false;

	public ConsoleWindow()
	{
		AddCommand("help", delegate
		{
			foreach (ConsoleCommand command in ConsoleCommands.Values.OrderBy(c => c.CommandName))
			{
				WriteLine(command.CommandName + ": " + command.CommandDescription);
			}
		}, "Lists the commands");
		AddCommand("clear", delegate
		{
			lock (_lock)
			{
				ConsoleItems.Clear();
			}
		}, "Clears the console");
		AddCommand("reset", delegate
		{
			IsaClient.Plugins.DevTools.ScriptConsole.Reset();
		}, "Forgets the C# console's variables");
		WriteLine("Type help for the commands; anything else runs as C# (Main.LocalPlayer.statLife = 500).");
	}

	public void WriteLine(string message) => WriteLine(message, DefaultColor);

	public void WriteError(string message) => WriteLine(message, ErrorColor);

	public void WriteLine(string message, Color color)
	{
		lock (_lock)
		{
			ConsoleElement last = ConsoleItems.Count > 0 ? ConsoleItems[ConsoleItems.Count - 1] : null;
			if (last != null && last.Text == message && last.TextColor == color)
			{
				last.AboveDuplicateCount++;
			}
			else
			{
				ConsoleItems.Add(new ConsoleElement(message ?? "", color));
				if (ConsoleItems.Count > MaxItems)
				{
					ConsoleItems.RemoveRange(0, ConsoleItems.Count - MaxItems);
				}
			}
			_scrollToBottom = true;
		}
	}

	public void AddCommand(string name, Action<CmdStr> action, string description = "No Description Given")
	{
		ConsoleCommands[name] = new ConsoleCommand(name, action, description);
	}

	public void RemoveCommand(string name)
	{
		ConsoleCommands.Remove(name);
	}

	/// <summary>Runs one command line; false when there is no such command.</summary>
	public bool ExecuteCommand(string line)
	{
		line = (line ?? "").Trim();
		if (line.StartsWith("#"))
		{
			line = line.Substring(1);
		}
		if (line.Length == 0)
		{
			return true;
		}
		CmdStr cmd = new CmdStr(line);
		if (!ConsoleCommands.TryGetValue(cmd.Command, out ConsoleCommand command))
		{
			// Not a command: C# (TerraAngel's REPL), compiled by the developer tools.
			IsaClient.Plugins.DevTools.ScriptConsole.Run(line);
			return true;
		}
		try
		{
			command.CommandAction(cmd);
		}
		catch (Exception ex)
		{
			WriteError(cmd.Command + ": " + ex.Message);
		}
		return true;
	}

	public override void Draw(ImGuiIOPtr io)
	{
		NVector2 size = new NVector2(io.DisplaySize.X / 2.8f, io.DisplaySize.Y / 2.2f);
		ImGui.SetNextWindowPos(new NVector2(io.DisplaySize.X - size.X - 20f, io.DisplaySize.Y - size.Y - 20f), ImGuiCond.FirstUseEver);
		ImGui.SetNextWindowSize(size, ImGuiCond.FirstUseEver);
		bool open = true;
		if (!ImGui.Begin("Console", ref open))
		{
			ImGui.End();
			IsEnabled = open;
			return;
		}
		IsEnabled = open;
		float footer = ImGui.GetStyle().ItemSpacing.Y + ImGui.GetFrameHeightWithSpacing();
		ImGui.BeginChild("ConsoleScroll", new NVector2(0f, -footer), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
		lock (_lock)
		{
			foreach (ConsoleElement item in ConsoleItems)
			{
				ImGui.PushStyleColor(ImGuiCol.Text, item.TextColor.PackedValue);
				ImGui.TextWrapped(item.AboveDuplicateCount > 0 ? (item.Text + " (" + (item.AboveDuplicateCount + 1) + ")") : item.Text);
				ImGui.PopStyleColor();
			}
			if (_scrollToBottom)
			{
				ImGui.SetScrollHereY(1f);
				_scrollToBottom = false;
			}
		}
		ImGui.EndChild();
		ImGui.SetNextItemWidth(-1f);
		if (_focusInput)
		{
			ImGui.SetKeyboardFocusHere();
			_focusInput = false;
		}
		if (ImGui.InputTextWithHint("##ConsoleInput", "Command (help) or C#", ref _input, 2048, ImGuiInputTextFlags.EnterReturnsTrue))
		{
			string line = _input;
			_input = "";
			_focusInput = true;
			if (line.Trim().Length > 0)
			{
				_history.Remove(line);
				_history.Add(line);
				_historyPosition = -1;
				WriteLine("> " + line, new Color(150, 160, 185));
				ExecuteCommand(line);
			}
		}
		if (ImGui.IsItemFocused() && _history.Count > 0)
		{
			if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
			{
				_historyPosition = (_historyPosition < 0) ? _history.Count - 1 : Math.Max(0, _historyPosition - 1);
				_input = _history[_historyPosition];
			}
			else if (ImGui.IsKeyPressed(ImGuiKey.DownArrow) && _historyPosition >= 0)
			{
				_historyPosition++;
				_input = (_historyPosition < _history.Count) ? _history[_historyPosition] : "";
				if (_historyPosition >= _history.Count)
				{
					_historyPosition = -1;
				}
			}
		}
		ImGui.End();
	}
}
