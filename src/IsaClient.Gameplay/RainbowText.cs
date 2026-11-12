using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using IsaClient.Core;

namespace IsaClient.Gameplay;

/// <summary>
/// Writes text in the world with rainbow projectiles (.rainbow). The letters come from <see cref="RainbowFont"/>
/// (the built-in A-Z and 0-9 plus the ones you make in the Rainbow page: any character); how big, how far apart, how
/// high and how fast are settings.
/// </summary>
public static class RainbowText
{
	private static readonly Queue<Vector4> _rainbowQueue = new Queue<Vector4>();

	private const int RainbowProjectile = 251;

	/// <summary>Size of the letters (1 = the normal size).</summary>
	[Setting("RainbowScale")]
	public static float Scale = 1f;

	/// <summary>Extra room between letters (glyph units; negative packs them closer).</summary>
	[Setting("RainbowSpacing", Min = -100, Max = 300)]
	public static int Spacing;

	/// <summary>How far above you the text is written (pixels).</summary>
	[Setting("RainbowHeight", Min = -600, Max = 1200)]
	public static int Height = 200;

	/// <summary>Rainbows sent per tick (more draws faster; the server sees more at once).</summary>
	[Setting("RainbowSpeed", Min = 1, Max = 200)]
	public static int Speed = 30;

	[Setting("RainbowMaxLetters", Min = 1, Max = 200)]
	public static int MaxLetters = 48;

	/// <summary>The text is written right where the cursor is (centered on it) instead of above you.</summary>
	[Setting("RainbowAtCursor")]
	public static bool AtCursor;

	public static bool Drawing => _rainbowQueue.Count > 0;

	internal static string Normalize(string text)
	{
		string decomposed = text.Normalize(NormalizationForm.FormD);
		StringBuilder builder = new StringBuilder(decomposed.Length);
		foreach (char c in decomposed)
		{
			if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
			{
				builder.Append(char.ToLowerInvariant(c));
			}
		}
		return builder.ToString().Normalize(NormalizationForm.FormC);
	}

	/// <summary>The letter for a character: its own, else the same one without its accent.</summary>
	private static RainbowGlyph GlyphFor(char c)
	{
		RainbowGlyph g = RainbowFont.Get(c);
		if (g == null)
		{
			string plain = Normalize(c.ToString());
			if (plain.Length == 1 && plain[0] != c)
			{
				g = RainbowFont.Get(plain[0]);
			}
		}
		return g;
	}

	/// <summary>.rainbow / .rb / .arcoiris</summary>
	internal static void RainbowCommand(CommandContext command)
	{
		if (command.Parts.Length < 2)
		{
			ClientChat.Error("Usage: .rainbow <text>  (.rb)");
			return;
		}
		Draw(command.Text.Substring(command.Parts[0].Length + 2).Trim().Trim('"'));
	}

	/// <summary>Writes the text over you (the command, and the Rainbow page's Try it).</summary>
	public static bool Draw(string text)
	{
		if (Main.gameMenu)
		{
			return false;
		}
		if (_rainbowQueue.Count > 0)
		{
			ClientChat.Error("Wait until the current text finishes drawing.");
			return false;
		}
		string kept = new string((text ?? "").Where(c => c == ' ' || GlyphFor(c) != null).ToArray()).Trim();
		if (kept.Length == 0)
		{
			ClientChat.Error("None of those characters has a rainbow letter. Make one in the Rainbow page (Experimental).");
			return false;
		}
		return Draw(kept, GlyphFor);
	}

	/// <summary>
	/// Writes these letters as they are (letters received in the chat, not in your font yet), in their order, the same
	/// way as text.
	/// </summary>
	public static bool Draw(IList<RainbowGlyph> glyphs)
	{
		if (Main.gameMenu || glyphs == null || glyphs.Count == 0)
		{
			return false;
		}
		if (_rainbowQueue.Count > 0)
		{
			ClientChat.Error("Wait until the current text finishes drawing.");
			return false;
		}
		Dictionary<char, RainbowGlyph> byChar = new Dictionary<char, RainbowGlyph>();
		foreach (RainbowGlyph g in glyphs)
		{
			byChar[g.Char] = g;
		}
		return Draw(new string(glyphs.Select(g => g.Char).ToArray()), c => byChar.TryGetValue(c, out RainbowGlyph g) ? g : GlyphFor(c));
	}

	/// <summary>The text (only characters that have a letter, and spaces), each letter from <paramref name="glyphFor"/>.</summary>
	private static bool Draw(string kept, Func<char, RainbowGlyph> glyphFor)
	{
		if (kept.Length > MaxLetters)
		{
			kept = kept.Substring(0, MaxLetters);
		}
		float scale = MathHelper.Clamp(Scale, 0.2f, 5f);
		// Letter widths first, so the whole text is centered on you.
		List<float> widths = kept.Select(c => ((c == ' ') ? RainbowFont.DefaultAdvance * 0.6f : glyphFor(c).Advance) + Spacing).ToList();
		float total = widths.Sum() - Spacing;
		Vector2 anchor = AtCursor ? Main.MouseWorld : new Vector2(Main.LocalPlayer.Center.X, Main.LocalPlayer.Center.Y - Height);
		float x = anchor.X - total * scale / 2f;
		float y = anchor.Y;
		List<Vector4> points = new List<Vector4>();
		for (int i = 0; i < kept.Length; i++)
		{
			float middle = x + widths[i] * scale / 2f - Spacing * scale / 2f;
			if (kept[i] != ' ')
			{
				foreach (Vector4 point in RainbowFont.Points(glyphFor(kept[i])))
				{
					points.Add(new Vector4(middle + point.X * scale, y + point.Y * scale, point.Z, point.W));
				}
			}
			x += widths[i] * scale;
		}
		if (AtCursor && points.Count > 0)
		{
			// Centered on the cursor up and down too.
			float shift = anchor.Y - (points.Min(p => p.Y) + points.Max(p => p.Y)) / 2f;
			for (int i = 0; i < points.Count; i++)
			{
				points[i] = new Vector4(points[i].X, points[i].Y + shift, points[i].Z, points[i].W);
			}
		}
		foreach (Vector4 point in points)
		{
			_rainbowQueue.Enqueue(point);
		}
		ClientChat.Info("Drawing '" + kept + "' with " + _rainbowQueue.Count + " rainbows...");
		return true;
	}

	/// <summary>Spawns queued rainbow segments a few per tick so the server does not see a burst.</summary>
	internal static void UpdateRainbow()
	{
		if (_rainbowQueue.Count == 0)
		{
			return;
		}
		if (Main.gameMenu)
		{
			_rainbowQueue.Clear();
			return;
		}
		for (int i = 0; i < Math.Max(1, Speed) && _rainbowQueue.Count > 0; i++)
		{
			Vector4 point = _rainbowQueue.Dequeue();
			int index = Projectile.NewProjectile(Main.LocalPlayer.GetProjectileSource_Misc(0), point.X, point.Y, point.Z, point.W, RainbowProjectile, 0, 0f, Main.myPlayer, 1f);
			if (index >= 0 && index < Main.maxProjectiles)
			{
				Projectile projectile = Main.projectile[index];
				projectile.rotation = (float)Math.Atan2(point.W, point.Z);
				projectile.timeLeft = Math.Max(projectile.timeLeft, 180);
			}
		}
		if (_rainbowQueue.Count == 0)
		{
			ClientChat.Info("Text drawn.");
		}
	}
}
