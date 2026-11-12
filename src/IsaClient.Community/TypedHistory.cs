using System.Collections.Generic;
using Terraria;

namespace IsaClient.Community;

/// <summary>
/// The lines you sent in the chat, like a terminal's history: Shift+Up brings back the one before, Shift+Down the next
/// (past the newest, what you were writing). Up/Down alone still scroll the chat. This session only.
/// </summary>
public static class TypedHistory
{
	private const int Max = 100;

	private static readonly List<string> _lines = new List<string>();

	/// <summary>Which line is shown (-1: not browsing).</summary>
	private static int _index = -1;

	/// <summary>What was being written before browsing.</summary>
	private static string _draft;

	/// <summary>A line sent (Enter): the newest of the history.</summary>
	public static void Add(string line)
	{
		_index = -1;
		if (string.IsNullOrWhiteSpace(line))
		{
			return;
		}
		if (_lines.Count > 0 && _lines[_lines.Count - 1] == line)
		{
			return;
		}
		_lines.Add(line);
		if (_lines.Count > Max)
		{
			_lines.RemoveAt(0);
		}
	}

	/// <summary>-1 an older line, 1 a newer one; false when there is nothing to show.</summary>
	public static bool Step(int direction)
	{
		if (_lines.Count == 0)
		{
			return false;
		}
		if (_index == -1)
		{
			if (direction > 0)
			{
				return false;
			}
			_draft = Main.chatText ?? "";
			_index = _lines.Count;
		}
		_index += direction;
		if (_index < 0)
		{
			_index = 0;
		}
		if (_index >= _lines.Count)
		{
			_index = -1;
			Main.chatText = _draft ?? "";
			return true;
		}
		Main.chatText = _lines[_index];
		return true;
	}

	/// <summary>The chat closed: browsing starts again from the newest next time.</summary>
	public static void Reset()
	{
		_index = -1;
	}
}
