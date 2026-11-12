using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace IsaClient.Assistant;

/// <summary>
/// Turns a model reply into clean chat text: no markdown (headings, bold, code, links, quotes, rules), tables become
/// "- Column: value, Column: value" lines, bullets become "- ", emojis and thinking are dropped (Terraria's font can't
/// draw emojis), and nothing can form a chat tag by accident.
/// </summary>
internal static class AnswerFormat
{
	private static readonly Regex Think = new Regex("<think>.*?</think>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

	private static readonly Regex Fence = new Regex("^\\s*(```|~~~).*$", RegexOptions.Compiled | RegexOptions.Multiline);

	private static readonly Regex Heading = new Regex("^\\s{0,3}#{1,6}\\s+(?<t>.+?)\\s*#*\\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

	private static readonly Regex Image = new Regex("!\\[[^\\]]*\\]\\([^)]*\\)", RegexOptions.Compiled);

	private static readonly Regex Link = new Regex("\\[(?<t>[^\\]]+)\\]\\((?<u>[^)\\s]+)[^)]*\\)", RegexOptions.Compiled);

	private static readonly Regex Bold = new Regex("(\\*\\*|__)(?=\\S)(?<t>.+?)(?<=\\S)\\1", RegexOptions.Compiled);

	private static readonly Regex Italic = new Regex("(?<![\\w*])([*_])(?=\\S)(?<t>[^*_\\n]+?)(?<=\\S)\\1(?![\\w*])", RegexOptions.Compiled);

	private static readonly Regex Strike = new Regex("~~(?<t>.+?)~~", RegexOptions.Compiled);

	private static readonly Regex Code = new Regex("`(?<t>[^`]+)`", RegexOptions.Compiled);

	private static readonly Regex Bullet = new Regex("^(?<i>\\s*)[*+\\u2022\\u25CF\\u25AA\\u2013-]\\s+", RegexOptions.Compiled | RegexOptions.Multiline);

	private static readonly Regex Quote = new Regex("^\\s*>\\s?", RegexOptions.Compiled | RegexOptions.Multiline);

	private static readonly Regex Rule = new Regex("^\\s*([-*_=])(\\s*\\1){2,}\\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

	private static readonly Regex Citation = new Regex("\\[(?<n>\\d{1,2}(?:\\s*[,;]\\s*\\d{1,2})*)\\]", RegexOptions.Compiled);

	private static readonly Regex Latex = new Regex("\\\\[()\\[\\]]|\\$\\$?", RegexOptions.Compiled);

	public static string Clean(string reply)
	{
		string text = IsaClient.Core.ChatText.Plain(reply ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
		text = Think.Replace(text, "");
		text = Tables(text);
		text = Fence.Replace(text, "");
		text = Rule.Replace(text, "");
		text = Heading.Replace(text, "${t}:");
		text = Image.Replace(text, "");
		text = Link.Replace(text, m => m.Groups["t"].Value == m.Groups["u"].Value ? m.Groups["u"].Value : m.Groups["t"].Value + " (" + m.Groups["u"].Value + ")");
		text = Quote.Replace(text, "");
		text = Bullet.Replace(text, m => (m.Groups["i"].Value.Length >= 2 ? "   " : "") + "- ");
		// Bold and italics after bullets, so "* item" isn't read as italics.
		text = Bold.Replace(text, "${t}");
		text = Italic.Replace(text, "${t}");
		text = Strike.Replace(text, "${t}");
		text = Code.Replace(text, "${t}");
		text = Latex.Replace(text, "");
		text = text.Replace("::", ":");
		text = NoEmojis(text);
		// Nothing may read as a chat tag ([c/..:..], [i:..]): citations become "(1)" and other brackets parentheses.
		text = Citation.Replace(text, m => "(" + Regex.Replace(m.Groups["n"].Value, "\\s*[,;]\\s*", ", ") + ")");
		text = text.Replace("[", "(").Replace("]", ")");
		// "(1)(2)" or "(1) (2)" reads as "(1, 2)".
		string merged;
		while ((merged = Regex.Replace(text, "\\((\\d{1,2}(?:, \\d{1,2})*)\\)\\s?\\((\\d{1,2})", "($1, $2")) != text)
		{
			text = merged;
		}
		text = Regex.Replace(text, "[ \\t]+([:,.;!?])", "$1");
		text = Regex.Replace(text, "[ \\t]+\\n", "\n");
		text = Regex.Replace(text, "\\n{3,}", "\n\n");
		text = Regex.Replace(text, "[ \\t]{2,}", " ");
		return text.Trim();
	}

	/// <summary>Markdown tables as lines: "- Name: Zenith, Damage: 190" (two columns: "- Zenith: 190").</summary>
	private static string Tables(string text)
	{
		string[] lines = text.Split('\n');
		StringBuilder result = new StringBuilder(text.Length);
		int i = 0;
		while (i < lines.Length)
		{
			if (IsRow(lines[i]) && i + 1 < lines.Length && IsSeparator(lines[i + 1]))
			{
				string[] header = Cells(lines[i]);
				i += 2;
				while (i < lines.Length && IsRow(lines[i]))
				{
					string[] cells = Cells(lines[i]);
					if (header.Length == 2 && cells.Length >= 2)
					{
						result.Append("- ").Append(cells[0]).Append(": ").Append(cells[1]).Append('\n');
					}
					else
					{
						List<string> pairs = new List<string>();
						for (int c = 0; c < cells.Length; c++)
						{
							if (cells[c].Length == 0)
							{
								continue;
							}
							string name = (c < header.Length && header[c].Length > 0 && c > 0) ? header[c] + ": " : "";
							pairs.Add(name + cells[c]);
						}
						result.Append("- ").Append(string.Join(", ", pairs)).Append('\n');
					}
					i++;
				}
				continue;
			}
			result.Append(lines[i]).Append('\n');
			i++;
		}
		return result.ToString();
	}

	private static bool IsRow(string line)
	{
		string t = line.Trim();
		return t.Length > 2 && t.StartsWith("|", StringComparison.Ordinal) && t.IndexOf('|', 1) > 0;
	}

	private static bool IsSeparator(string line)
	{
		return Regex.IsMatch(line.Trim(), "^\\|?\\s*:?-{2,}:?\\s*(\\|\\s*:?-{2,}:?\\s*)*\\|?$");
	}

	private static string[] Cells(string line)
	{
		string t = line.Trim().Trim('|');
		return t.Split('|').Select(c => c.Trim()).ToArray();
	}

	private static string NoEmojis(string text)
	{
		StringBuilder builder = new StringBuilder(text.Length);
		foreach (char c in text)
		{
			if (char.IsSurrogate(c) || c == '️' || c == '‍')
			{
				continue;
			}
			UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
			if (category == UnicodeCategory.OtherSymbol)
			{
				continue;
			}
			builder.Append(c);
		}
		return builder.ToString();
	}

	/// <summary>Cuts <paramref name="text"/> to about <paramref name="max"/> characters at the end of a sentence or line.</summary>
	public static string Limit(string text, int max)
	{
		if (text.Length <= max)
		{
			return text;
		}
		int cut = -1;
		for (int i = Math.Min(max, text.Length - 1); i > max / 2; i--)
		{
			char c = text[i];
			if (c == '\n' || ((c == '.' || c == '!' || c == '?') && (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1]))))
			{
				cut = i + 1;
				break;
			}
		}
		return (cut > 0) ? text.Substring(0, cut).TrimEnd() : (text.Substring(0, max).TrimEnd() + "...");
	}
}
