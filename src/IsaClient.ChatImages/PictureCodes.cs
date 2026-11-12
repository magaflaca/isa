using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace IsaClient.ChatImages;

/// <summary>
/// Picture links travel as short codes of English words between « and » («calm frog lamp nine oak hill pond»), so other
/// players read a few odd words instead of a long Imgur link. IsaClient turns a code back into its link before the message
/// is laid out, so for IsaClient players nothing changes: the picture shows. A code holds the Imgur id, its length and its
/// extension (as base-256 digits, one word each) plus a check word, so ordinary words between « » are not taken for one.
/// Links that don't fit (unusual ids) are sent as they are.
/// </summary>
public static class PictureCodes
{
	[IsaClient.Core.Setting("ChatImagesCodes")]
	public static bool Enabled = true;

	/// <summary>Codes in one message at most this long; more pictures go in the next message.</summary>
	public const int MaxPerMessage = 100;

	private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

	private static readonly string[] Extensions = { "png", "jpg", "gif", "webp" };

	private static readonly string[] Words =
	{
		"able", "acid", "aged", "also", "area", "army", "away", "baby", "back", "ball", "band", "bank", "base", "bath", "bear", "beat",
		"bell", "belt", "best", "bird", "blow", "blue", "boat", "body", "bone", "book", "boot", "born", "boss", "both", "bowl", "busy",
		"cake", "call", "calm", "camp", "card", "care", "case", "cash", "cast", "cat", "city", "clay", "club", "coal", "coat", "code",
		"cold", "cook", "cool", "copy", "corn", "cost", "crew", "crop", "cube", "cute", "dark", "data", "dawn", "deal", "dear", "deep",
		"desk", "diet", "dirt", "dish", "dock", "door", "down", "draw", "drop", "drum", "duck", "dust", "duty", "each", "easy", "echo",
		"edge", "epic", "even", "exit", "face", "fact", "fair", "fall", "farm", "fast", "fear", "feel", "fern", "film", "fire", "fish",
		"five", "flag", "flat", "flow", "foam", "fog", "folk", "food", "foot", "fork", "form", "fort", "four", "free", "frog", "fuel",
		"full", "fun", "game", "gate", "gear", "gift", "girl", "glad", "glow", "goal", "goat", "gold", "golf", "good", "gray", "grid",
		"grow", "gulf", "hair", "half", "hall", "hand", "hard", "harp", "hat", "hawk", "head", "heat", "help", "herb", "hero", "high",
		"hill", "hint", "home", "hope", "horn", "host", "hour", "huge", "idea", "inch", "iron", "item", "jade", "jazz", "jump", "just",
		"keen", "keep", "kept", "kind", "king", "kite", "knee", "lake", "lamp", "land", "lane", "last", "late", "lava", "lazy", "leaf",
		"left", "lemon", "life", "lift", "light", "lime", "line", "lion", "list", "live", "loaf", "lock", "long", "loop", "lord", "loud",
		"love", "luck", "lucky", "magic", "main", "map", "mark", "mask", "meal", "meat", "melt", "milk", "mind", "mint", "moon", "moss",
		"most", "moth", "move", "much", "mud", "nail", "name", "navy", "near", "neat", "nest", "news", "nice", "nine", "node", "noon",
		"nose", "note", "oak", "oat", "ocean", "odd", "oil", "old", "open", "oven", "over", "owl", "pack", "page", "pain", "pale",
		"park", "part", "path", "peak", "pear", "pink", "pine", "pipe", "plan", "play", "plum", "poem", "pond", "pool", "pork", "ruby",
	};

	private static readonly Dictionary<string, int> _index = Words.Select((w, i) => new KeyValuePair<string, int>(w, i)).ToDictionary(p => p.Key, p => p.Value);

	private static readonly Regex _code = new Regex("«([a-z]+(?: [a-z]+){1,10})»", RegexOptions.Compiled);

	private static readonly Regex _link = new Regex(@"^https://i\.imgur\.com/([A-Za-z0-9]{5,9})\.(png|jpg|gif|webp)$", RegexOptions.Compiled);

	/// <summary>The code for a picture link, or the link itself when it can't be coded (or codes are off).</summary>
	public static string Encode(string link)
	{
		List<int> bytes = Enabled ? ToBytes(link) : null;
		if (bytes == null)
		{
			return link;
		}
		return "«" + string.Join(" ", bytes.Select(b => Words[b])) + "»";
	}

	/// <summary>A picture link as a few bytes (its last one a check), or null when it can't be coded.</summary>
	public static List<int> ToBytes(string link)
	{
		Match match = _link.Match(link ?? "");
		if (!match.Success)
		{
			return null;
		}
		string id = match.Groups[1].Value;
		ulong n = 0;
		foreach (char c in id)
		{
			n = n * 62 + (ulong)Base62.IndexOf(c);
		}
		n = (n * 5 + (ulong)(id.Length - 5)) * 4 + (ulong)System.Array.IndexOf(Extensions, match.Groups[2].Value);
		List<int> bytes = new List<int>();
		do
		{
			bytes.Add((int)(n & 0xFF));
			n >>= 8;
		}
		while (n > 0);
		bytes.Add(Check(bytes));
		return bytes;
	}

	/// <summary>Bytes as words (one each), for <see cref="ChatData"/>.</summary>
	internal static string WordsOf(IEnumerable<int> bytes)
	{
		return string.Join(" ", bytes.Select(b => Words[b]));
	}

	/// <summary>A code word's byte, or -1 when it isn't one.</summary>
	internal static int ByteOf(string word)
	{
		return _index.TryGetValue(word, out int value) ? value : -1;
	}

	/// <summary>The link a code stands for, or null when it isn't one.</summary>
	public static string Decode(string words)
	{
		string[] parts = (words ?? "").Split(' ');
		List<int> bytes = new List<int>();
		foreach (string part in parts)
		{
			if (!_index.TryGetValue(part, out int value))
			{
				return null;
			}
			bytes.Add(value);
		}
		return FromBytes(bytes);
	}

	/// <summary>The link of a picture's bytes (see <see cref="ToBytes"/>), or null when they aren't one.</summary>
	public static string FromBytes(List<int> bytes)
	{
		if (bytes == null || bytes.Count < 2 || bytes.Count > 9)
		{
			return null;
		}
		int check = bytes[bytes.Count - 1];
		bytes = bytes.GetRange(0, bytes.Count - 1);
		if (Check(bytes) != check)
		{
			return null;
		}
		ulong n = 0;
		for (int i = bytes.Count - 1; i >= 0; i--)
		{
			n = (n << 8) | (uint)bytes[i];
		}
		string extension = Extensions[(int)(n % 4)];
		n /= 4;
		int length = (int)(n % 5) + 5;
		n /= 5;
		char[] id = new char[length];
		for (int i = length - 1; i >= 0; i--)
		{
			id[i] = Base62[(int)(n % 62)];
			n /= 62;
		}
		if (n != 0)
		{
			return null;
		}
		return "https://i.imgur.com/" + new string(id) + "." + extension;
	}

	private static int Check(List<int> bytes)
	{
		int c = 0x5A + bytes.Count * 17;
		foreach (int b in bytes)
		{
			c = (c * 31 + b + 7) & 0xFF;
		}
		return c;
	}

	/// <summary>A message with its codes turned back into links (for laying it out); the same text when it has none.</summary>
	public static string Expand(string text)
	{
		if (string.IsNullOrEmpty(text) || text.IndexOf('«') < 0)
		{
			return text;
		}
		return _code.Replace(text, m => Decode(m.Groups[1].Value) ?? m.Value);
	}

	/// <summary>
	/// The text a message is laid out from: its codes as links. A translated message loses its codes (their words get
	/// translated): the links of the original are put back at its end.
	/// </summary>
	public static string ExpandShown(string shown, string original)
	{
		if (shown == original || string.IsNullOrEmpty(original) || original.IndexOf('«') < 0)
		{
			return Expand(shown);
		}
		List<string> links = Links(original);
		if (links.Count == 0)
		{
			return Expand(shown);
		}
		string text = _code.Replace(shown ?? "", "").TrimEnd();
		return text + " " + string.Join(" ", links);
	}

	/// <summary>The links of the codes in a message (a translated message gets them from the original).</summary>
	public static List<string> Links(string text)
	{
		List<string> links = new List<string>();
		if (!string.IsNullOrEmpty(text) && text.IndexOf('«') >= 0)
		{
			foreach (Match m in _code.Matches(text))
			{
				string link = Decode(m.Groups[1].Value);
				if (link != null)
				{
					links.Add(link);
				}
			}
		}
		return links;
	}

	/// <summary>The pictures to send, coded and grouped so each message's codes stay within <see cref="MaxPerMessage"/>.</summary>
	public static List<string> Group(IEnumerable<string> links)
	{
		List<string> groups = new List<string>();
		StringBuilder current = new StringBuilder();
		foreach (string link in links)
		{
			string piece = Encode(link);
			if (current.Length > 0 && current.Length + 1 + piece.Length > MaxPerMessage)
			{
				groups.Add(current.ToString());
				current.Clear();
			}
			if (current.Length > 0)
			{
				current.Append(' ');
			}
			current.Append(piece);
		}
		if (current.Length > 0)
		{
			groups.Add(current.ToString());
		}
		return groups;
	}
}
