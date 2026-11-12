using System;
using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;

using IsaClient.Languages;

namespace IsaClient.Core;

/// <summary>Client messages in the local chat: normal output, notices and errors, each with its own color.</summary>
public static class ClientChat
{
	/// <summary>Normal client text when the user did not pick a color: a very light yellow.</summary>
	public static readonly Color DefaultTextColor = new Color(255, 248, 190);

	/// <summary>The user's color for normal client text ("R,G,B"); empty for <see cref="DefaultTextColor"/>. Set with .textcolor.</summary>
	[Setting("ClientTextColor")]
	public static string TextColor = "";

	public static Color ErrorColor = new Color(255, 25, 25);

	/// <summary>Notices: warnings, hints, server behavior (orange, so they stand out from normal text).</summary>
	public static Color InfoColor = new Color(255, 170, 60);

	public static bool DebugMessages;

	private static string _parsedText;

	private static Color _parsedColor = DefaultTextColor;

	/// <summary>Color of normal client text (<see cref="TextColor"/>, or the default).</summary>
	public static Color AccentColor
	{
		get
		{
			if (!ReferenceEquals(_parsedText, TextColor))
			{
				_parsedText = TextColor;
				_parsedColor = TryParseColor(TextColor, out Color color) ? color : DefaultTextColor;
			}
			return _parsedColor;
		}
	}

	/// <summary>Normal client output (the client text color).</summary>
	public static void Info(string text)
	{
		Main.NewText(L10n.T(text), AccentColor);
	}

	/// <summary>Something worth noticing: warnings, hints, server behavior (orange).</summary>
	public static void Notice(string text)
	{
		Main.NewText(L10n.T(text), InfoColor);
	}

	/// <summary>Errors and wrong command usage (red).</summary>
	public static void Error(string text)
	{
		Main.NewText(L10n.T(text), ErrorColor);
	}

	/// <summary>Chat tag that draws <paramref name="text"/> in <paramref name="color"/> inside a message (text without ']').</summary>
	public static string Tint(string text, Color color)
	{
		return "[c/" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2") + ":" + text + "]";
	}

	/// <summary>Reads "255,248,190", "255 248 190", "FFF8BE" / "#FFF8BE" or a color name (<see cref="NamedColors"/>).</summary>
	public static bool TryParseColor(string text, out Color color)
	{
		color = default(Color);
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		if (NamedColors.TryGet(text, out color))
		{
			return true;
		}
		string[] parts = text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 3)
		{
			if (byte.TryParse(parts[0], out byte r) && byte.TryParse(parts[1], out byte g) && byte.TryParse(parts[2], out byte b))
			{
				color = new Color(r, g, b);
				return true;
			}
			return false;
		}
		string hex = text.Trim().TrimStart('#');
		if (hex.Length == 6 && int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int rgb))
		{
			color = new Color((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
			return true;
		}
		return false;
	}

	#region Chat commands

	/// <summary>.textcolor / .clientcolor</summary>
	internal static void TextColorCommand(CommandContext command)
	{
		if (command.ArgumentCount == 0)
		{
			Info("Client text color: " + Describe(AccentColor) + ((TextColor.Length == 0) ? " (default)" : "") + ".  Usage: .textcolor <name> | <RRGGBB> | <r> <g> <b> | reset");
			return;
		}
		if (command.Parts[1].Equals("reset", StringComparison.OrdinalIgnoreCase) || command.Parts[1].Equals("default", StringComparison.OrdinalIgnoreCase))
		{
			TextColor = "";
		}
		else if (TryParseColor(command.Rest, out Color color))
		{
			TextColor = color.R + "," + color.G + "," + color.B;
		}
		else
		{
			Error("Usage: .textcolor <name> (" + NamedColors.ColoredList() + "), .textcolor <RRGGBB>, .textcolor <r> <g> <b> or .textcolor reset");
			return;
		}
		ClientSettings.Save();
		Info("Client text color set to " + Describe(AccentColor) + ((TextColor.Length == 0) ? " (default)" : "") + ".");
	}

	private static string Describe(Color color)
	{
		string name = NamedColors.NameOf(color);
		return Tint((name != null) ? (name + " (" + NamedColors.Hex(color) + ")") : NamedColors.Hex(color), color);
	}

	#endregion
}
