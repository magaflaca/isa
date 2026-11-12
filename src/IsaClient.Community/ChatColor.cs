using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Terraria.UI.Chat;

namespace IsaClient.Community;

/// <summary>
/// Colored chat (.chatcolor): what you type goes out wrapped in Terraria's color tag, [c/FF66FF:text], so every
/// player sees it in that color. Item, name, achievement and glyph tags stay as they are, and brackets typed in the
/// message are escaped so they cannot break the tag. Server commands (/...) are never touched.
/// </summary>
public static class ChatColor
{
	public static readonly Color DefaultColor = new Color(255, 102, 255);

	[Setting("ColoredChat")]
	public static bool Enabled;

	/// <summary>"R,G,B" (like the client text color).</summary>
	[Setting("ColoredChatColor")]
	public static string Value = "255,102,255";

	/// <summary>Tags the game draws by itself (ChatInitializer); anything else is plain text.</summary>
	private static readonly HashSet<string> KnownTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "c", "color", "i", "item", "n", "name", "a", "achievement", "g", "glyph", "gx" };

	private static string _parsedText;

	private static Color _parsedColor = DefaultColor;

	public static Color Current
	{
		get
		{
			if (!ReferenceEquals(_parsedText, Value))
			{
				_parsedText = Value;
				_parsedColor = ClientChat.TryParseColor(Value, out Color color) ? color : DefaultColor;
			}
			return _parsedColor;
		}
		set
		{
			Value = value.R + "," + value.G + "," + value.B;
		}
	}

	/// <summary>Called with the typed line right before it is sent: colored when the option is on.</summary>
	public static string ApplyToOutgoing(string text)
	{
		if (!Enabled || string.IsNullOrWhiteSpace(text) || text[0] == '/')
		{
			return text;
		}
		return Colorize(text, Current);
	}

	/// <summary>
	/// "Hola [i:58] como andas?" becomes "[c/FF66FF:Hola ][i:58][c/FF66FF: como andas?]". Tags the game knows are
	/// kept (a [c/..] you typed keeps its own color); the text between them is wrapped, with ']' escaped.
	/// </summary>
	public static string Colorize(string text, Color color)
	{
		string hex = NamedColors.Hex(color);
		StringBuilder result = new StringBuilder(text.Length + 32);
		StringBuilder plain = new StringBuilder();
		int i = 0;
		while (i < text.Length)
		{
			if (text[i] == '[' && (i == 0 || text[i - 1] != '\\'))
			{
				Match match = ChatManager.Regexes.Format.Match(text, i);
				if (match.Success && match.Index == i && KnownTags.Contains(match.Groups["tag"].Value))
				{
					AppendPlain(result, plain, hex);
					result.Append(match.Value);
					i += match.Length;
					continue;
				}
			}
			plain.Append(text[i]);
			i++;
		}
		AppendPlain(result, plain, hex);
		return result.ToString();
	}

	private static void AppendPlain(StringBuilder result, StringBuilder plain, string hex)
	{
		if (plain.Length == 0)
		{
			return;
		}
		string text = plain.ToString();
		plain.Clear();
		if (text.Trim().Length == 0)
		{
			result.Append(text);
			return;
		}
		// The game reads "\]" as a literal ']' inside a tag. A trailing '\' would escape the closing bracket.
		text = text.Replace("]", "\\]");
		if (text.EndsWith("\\", StringComparison.Ordinal))
		{
			text += " ";
		}
		result.Append("[c/").Append(hex).Append(':').Append(text).Append(']');
	}

	#region Chat commands

	/// <summary>.chatcolor [on|off|name|RRGGBB|r g b|reset|list]</summary>
	internal static void ChatColorCommand(CommandContext command)
	{
		string arg = command.Rest.Trim();
		string word = arg.ToLowerInvariant();
		switch (word)
		{
		case "":
			Enabled = !Enabled;
			break;
		case "on":
			Enabled = true;
			break;
		case "off":
			Enabled = false;
			break;
		case "list":
		case "colors":
			ClientChat.Info("Colors: " + NamedColors.ColoredList() + ". Any hex works too: .chatcolor ff66ff");
			return;
		case "reset":
		case "default":
			Current = DefaultColor;
			break;
		default:
			if (!ClientChat.TryParseColor(arg, out Color color))
			{
				ClientChat.Error("Unknown color '" + arg + "'. Usage: .chatcolor <name> | <RRGGBB> | <r> <g> <b> | on | off | reset | list");
				return;
			}
			Current = color;
			Enabled = true;
			break;
		}
		ClientSettings.Save();
		Color current = Current;
		string name = NamedColors.NameOf(current);
		string sample = ClientChat.Tint((name ?? "Custom") + " (" + NamedColors.Hex(current) + ")", current);
		ClientChat.Info("Colored chat " + (Enabled ? "on" : "off") + ": " + sample + (Enabled ? "" : "  (.chatcolor on to use it)"));
	}

	#endregion
}
