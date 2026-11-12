using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace IsaClient.Core;

/// <summary>
/// Color names the commands understand (".chatcolor red", ".textcolor sky") and the presets of the panel's color picker.
/// </summary>
public static class NamedColors
{
	public static readonly IReadOnlyList<(string Name, Color Color)> All = new[]
	{
		("Red", new Color(255, 70, 70)),
		("Orange", new Color(255, 150, 50)),
		("Yellow", new Color(255, 230, 80)),
		("Gold", new Color(255, 200, 40)),
		("Lime", new Color(160, 255, 90)),
		("Green", new Color(60, 200, 90)),
		("Teal", new Color(45, 200, 180)),
		("Cyan", new Color(80, 230, 255)),
		("Sky", new Color(140, 200, 255)),
		("Blue", new Color(80, 120, 255)),
		("Purple", new Color(160, 95, 255)),
		("Lavender", new Color(200, 170, 255)),
		("Pink", new Color(255, 150, 210)),
		("Magenta", new Color(255, 102, 255)),
		("White", new Color(255, 255, 255)),
		("Gray", new Color(150, 150, 150))
	};

	public static bool TryGet(string name, out Color color)
	{
		string wanted = (name ?? "").Trim();
		foreach ((string Name, Color Color) entry in All)
		{
			if (string.Equals(entry.Name, wanted, StringComparison.OrdinalIgnoreCase) || (wanted.Equals("grey", StringComparison.OrdinalIgnoreCase) && entry.Name == "Gray"))
			{
				color = entry.Color;
				return true;
			}
		}
		color = default(Color);
		return false;
	}

	/// <summary>The preset's name when <paramref name="color"/> is one, else null.</summary>
	public static string NameOf(Color color)
	{
		foreach ((string Name, Color Color) entry in All)
		{
			if (entry.Color.R == color.R && entry.Color.G == color.G && entry.Color.B == color.B)
			{
				return entry.Name;
			}
		}
		return null;
	}

	/// <summary>"FF66FF"</summary>
	public static string Hex(Color color)
	{
		return color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
	}

	/// <summary>Every name, each written in its own color (for chat output).</summary>
	public static string ColoredList()
	{
		List<string> names = new List<string>();
		foreach ((string Name, Color Color) entry in All)
		{
			names.Add(ClientChat.Tint(entry.Name.ToLowerInvariant(), entry.Color));
		}
		return string.Join(", ", names);
	}
}
