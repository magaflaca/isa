using System.Text;

namespace IsaClient.Core;

/// <summary>
/// Typographic characters the game's font doesn't have (it draws them as *), turned into the plain ones: dashes and
/// hyphens (AI answers love ‑ and —), curly quotes, odd spaces, the ellipsis. Applied to every chat line sent and
/// shown, wherever it comes from. Invisible characters are left alone (other features use them).
/// </summary>
public static class ChatText
{
	private static readonly System.Text.RegularExpressions.Regex EmptyColorTags = new System.Text.RegularExpressions.Regex(@"\[c(?:olor)?/[0-9A-Fa-f]{6}:\s*\]", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

	/// <summary>Nothing to read: empty, only spaces and line breaks, or colour tags with nothing inside.</summary>
	public static bool IsBlank(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}
		return text.IndexOf('[') >= 0 && string.IsNullOrWhiteSpace(EmptyColorTags.Replace(text, ""));
	}

	public static string Plain(string text)
	{
		if (string.IsNullOrEmpty(text) || !NeedsWork(text))
		{
			return text;
		}
		StringBuilder plain = new StringBuilder(text.Length);
		foreach (char c in text)
		{
			switch (c)
			{
			case '‐':
			case '‑':
			case '‒':
			case '–':
			case '—':
			case '―':
			case '−':
			case '⁃':
			case '﹘':
			case '﹣':
			case '－':
				plain.Append('-');
				break;
			case '‘':
			case '’':
			case '‚':
			case '‛':
			case '′':
				plain.Append('\'');
				break;
			case '“':
			case '”':
			case '„':
			case '‟':
			case '″':
				plain.Append('"');
				break;
			case ' ':
			case ' ':
			case ' ':
			case ' ':
			case ' ':
			case ' ':
			case ' ':
			case ' ':
			case ' ':
			case ' ':
			case ' ':
			case ' ':
			case '　':
				plain.Append(' ');
				break;
			case '…':
				plain.Append("...");
				break;
			case '•':
				plain.Append('-');
				break;
			default:
				plain.Append(c);
				break;
			}
		}
		return plain.ToString();
	}

	private static bool NeedsWork(string text)
	{
		foreach (char c in text)
		{
			if (c == ' ' || (c >= ' ' && c <= '⁃' && c != '​' && c != '‌' && c != '‍' && c != '‎' && c != '‏') || c == ' ' || c == '−' || c == '　' || c == '﹘' || c == '﹣' || c == '－')
			{
				return true;
			}
		}
		return false;
	}
}
