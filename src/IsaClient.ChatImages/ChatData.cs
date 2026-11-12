using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using IsaClient.Sharing;

namespace IsaClient.ChatImages;

/// <summary>
/// What IsaClient sends in the server's chat when the pipe is off: pictures, GIFs, shared characters, styles and
/// schematics, and reactions (.hug...). It all goes as one packet of real English words (the picture codes' list, one
/// word a byte) at the end of the message, with no marks around it, like secret chat (.secret / .enc): scrambled from a
/// random start, so the same thing never looks the same twice, and checked (24 bits a part), so ordinary words are
/// never taken for one. Whatever doesn't fit in one part of at most <see cref="MaxChars"/> characters goes in more:
/// the first rides with the message, the rest go in lines of their own that IsaClient hides. IsaClient shows the
/// message as it would through the pipe: the pictures and cards under it, a reaction as its own line.
/// <list type="bullet">
/// <item>A part: transfer id (2, mixed with the check so it differs in every part), index (7 bits, top bit = last
/// part), a piece of the packet, 24-bit check. Everything but the id is scrambled from the id and the index.</item>
/// <item>The packet: its length (2), records [kind, length, content] and a 16-bit check. Kinds: 'g' a picture or GIF link, 'k' 'y' 'w'
/// a share tag, 'r' a reaction, 'f' where a shared file is (an Imgur picture holding it).</item>
/// </list>
/// </summary>
public static class ChatData
{
	/// <summary>Characters of words per chat line at most.</summary>
	public const int MaxChars = 100;

	private const int MinBytes = 10;

	private const int MaxBytes = 40;

	private const uint Key = 0x1DA7A5u;

	private static readonly Random _random = new Random();

	/// <summary>What a packet carried.</summary>
	public sealed class Packet
	{
		public readonly List<string> Links = new List<string>();

		/// <summary>A reaction (.hug...): its command, who sent it and to whom ("" alone).</summary>
		public string Reaction;

		public string By = "";

		public string To = "";

		/// <summary>Where shared files are: (tag letter, its key in hex, the picture's link).</summary>
		public readonly List<(char Kind, string Key, string Link)> Files = new List<(char Kind, string Key, string Link)>();
	}

	private sealed class Assembly
	{
		public readonly byte[][] Chunks = new byte[128][];

		public int Count = -1;

		public string Head;

		public bool Done;

		public DateTime Touched = DateTime.Now;
	}

	private static readonly Dictionary<ushort, Assembly> _assembling = new Dictionary<ushort, Assembly>();

	/// <summary>What each first part stands for once all its parts arrived (by its words), oldest first.</summary>
	private static readonly Dictionary<string, Packet> _results = new Dictionary<string, Packet>();

	private static readonly Queue<string> _resultOrder = new Queue<string>();

	private static readonly object _lock = new object();

	/// <summary>Something arrived whole (main thread): each link or share tag it carried.</summary>
	public static event Action<Packet> Arrived;

	/// <summary>The end of a line made of lowercase words (maybe closing a color tag): where a part would be.</summary>
	private static readonly Regex _tail = new Regex("([a-z]+(?: [a-z]+){9,45})\\]?\\s*$", RegexOptions.Compiled);

	private static readonly Regex _emptyColor = new Regex("\\[c/[0-9a-fA-F]{6}:\\s*\\]", RegexOptions.Compiled);

	private static readonly Regex _tag = new Regex("^\\[i/(?<letter>[kywa])(?<hex>[0-9a-fA-F]+):\\d+\\]$", RegexOptions.Compiled);

	private static readonly Regex _giphy = new Regex("^https://i\\.giphy\\.com/media/([A-Za-z0-9]{6,40})/200\\.gif$", RegexOptions.Compiled);

	private static readonly Regex _klipy = new Regex("^https://static\\.klipy\\.com/ii/([0-9a-f]{32})/([0-9a-f]{2})/([0-9a-f]{2})/([A-Za-z0-9]{4,40})\\.gif$", RegexOptions.Compiled);

	private static readonly Regex _nekos = new Regex("^https://nekos\\.best/api/v2/([a-z]{2,16})/([0-9a-f]{8})-([0-9a-f]{4})-([0-9a-f]{4})-([0-9a-f]{4})-([0-9a-f]{12})\\.gif$", RegexOptions.Compiled);

	#region Sending

	/// <summary>Links sent in a packet: share tags always, pictures and GIFs when short codes are on.</summary>
	public static bool Handles(string link)
	{
		if (string.IsNullOrEmpty(link))
		{
			return false;
		}
		return _tag.IsMatch(link) || (PictureCodes.Enabled && ChatImages.IsImageLink(link));
	}

	/// <summary>A link (picture, GIF or share tag) as a record, or null when it can't be one.</summary>
	public static byte[] LinkRecord(string link)
	{
		Match tag = _tag.Match(link ?? "");
		if (tag.Success)
		{
			return Record((byte)tag.Groups["letter"].Value[0], ShareKinds.FromHex(tag.Groups["hex"].Value.ToLowerInvariant()));
		}
		return Record((byte)'g', PackLink(link));
	}

	/// <summary>Where a shared file is (a character's code or a schematic's id, and the Imgur picture holding it).</summary>
	public static byte[] FileRecord(char kind, string keyHex, string link)
	{
		byte[] key = ShareKinds.FromHex(keyHex);
		byte[] where = PackLink(link);
		if (key == null || where == null)
		{
			return null;
		}
		List<byte> b = new List<byte> { (byte)kind, (byte)key.Length };
		b.AddRange(key);
		b.AddRange(where);
		return Record((byte)'f', b.ToArray());
	}

	/// <summary>A reaction (.hug...) as a record: each IsaClient shows it in its own language.</summary>
	public static byte[] ReactionRecord(string command, string by, string to)
	{
		List<byte> b = new List<byte>();
		foreach (string s in new[] { command, by, to })
		{
			byte[] text = Encoding.UTF8.GetBytes(Cut(s ?? "", 40));
			b.Add((byte)text.Length);
			b.AddRange(text);
		}
		return Record((byte)'r', b.ToArray());
	}

	private static string Cut(string s, int bytes)
	{
		while (Encoding.UTF8.GetByteCount(s) > bytes)
		{
			s = s.Substring(0, s.Length - 1);
		}
		return s;
	}

	private static byte[] Record(byte kind, byte[] content)
	{
		if (content == null || content.Length > 255)
		{
			return null;
		}
		byte[] r = new byte[content.Length + 2];
		r[0] = kind;
		r[1] = (byte)content.Length;
		content.CopyTo(r, 2);
		return r;
	}

	/// <summary>
	/// Records as parts to send: the first goes at the end of the message, the others alone (raw, one per line). Null
	/// when there's nothing or it's too big.
	/// </summary>
	public static List<string> Encode(IEnumerable<byte[]> records)
	{
		List<byte> packet = new List<byte>();
		foreach (byte[] r in records)
		{
			if (r != null)
			{
				packet.AddRange(r);
			}
		}
		if (packet.Count == 0 || packet.Count > 4000)
		{
			return null;
		}
		int length = packet.Count;
		packet.Insert(0, (byte)(length >> 8));
		packet.Insert(1, (byte)(length & 0xFF));
		ushort check = Check16(packet, 0, packet.Count);
		packet.Add((byte)(check & 0xFF));
		packet.Add((byte)(check >> 8));
		byte[] stream = packet.ToArray();
		ushort tid;
		lock (_random)
		{
			tid = (ushort)_random.Next(1, 65536);
		}
		List<string> parts = new List<string>();
		int at = 0;
		while (at < stream.Length)
		{
			if (parts.Count >= 127)
			{
				return null;
			}
			// As much as fits in one line.
			int remaining = stream.Length - at;
			string part = null;
			int taken = 0;
			for (int n = Math.Min(remaining, MaxBytes - 6); n >= 1; n--)
			{
				string candidate = Part(tid, parts.Count, n == remaining, stream, at, n);
				if (candidate.Length <= MaxChars)
				{
					part = candidate;
					taken = n;
					break;
				}
			}
			if (part == null)
			{
				return null;
			}
			parts.Add(part);
			at += taken;
		}
		return parts;
	}

	private static string Part(ushort tid, int index, bool last, byte[] stream, int at, int count)
	{
		// The last part is padded to the minimum size (the packet's lengths tell where it ends).
		int pad = last ? Math.Max(0, MinBytes - 6 - count) : 0;
		List<int> bytes = new List<int>(count + pad + 6) { 0, 0, index | (last ? 0x80 : 0) };
		for (int i = 0; i < count; i++)
		{
			bytes.Add(stream[at + i]);
		}
		for (int i = 0; i < pad; i++)
		{
			bytes.Add(0);
		}
		Scramble(bytes, tid, encoding: true);
		int check = Check24(bytes, 2, bytes.Count - 2);
		int shown = tid ^ IdMask(check);
		bytes[0] = shown & 0xFF;
		bytes[1] = shown >> 8;
		bytes.Add(check & 0xFF);
		bytes.Add((check >> 8) & 0xFF);
		bytes.Add(check >> 16);
		return PictureCodes.WordsOf(bytes);
	}

	/// <summary>Parts that go in lines of their own, as they are (no colors, translation or secret chat).</summary>
	public static void SendAlone(IEnumerable<string> parts)
	{
		foreach (string part in parts)
		{
			Terraria.Main.SendRawChat(part);
		}
	}

	#endregion

	#region Receiving

	/// <summary>
	/// A new chat line (before it's shown): its part is collected. True when it only carries the rest of something
	/// (a part after the first): it isn't shown.
	/// </summary>
	public static bool OnChatLine(string text)
	{
		return Find(text, out _, out int index) && index > 0;
	}

	/// <summary>Does this line end in a part of ours? (Its words, as they are in the line, and its index.)</summary>
	public static bool Find(string text, out string words, out int index)
	{
		words = null;
		index = -1;
		if (string.IsNullOrEmpty(text) || text.Length < 40)
		{
			return false;
		}
		Match m = _tail.Match(text);
		if (!m.Success)
		{
			return false;
		}
		// The run may start inside the text before it (or a glued word): the part is a whole number of words at its end.
		string[] tokens = m.Groups[1].Value.Split(' ');
		int[] bytes = tokens.Select(PictureCodes.ByteOf).ToArray();
		for (int start = 0; tokens.Length - start >= MinBytes; start++)
		{
			if (tokens.Length - start > MaxBytes || bytes[start] < 0)
			{
				continue;
			}
			List<int> part = new List<int>(bytes.Skip(start));
			string candidate = string.Join(" ", tokens.Skip(start));
			if (candidate.Length > MaxChars || part.Any(b => b < 0))
			{
				continue;
			}
			if (TryFeed(candidate, part, out index))
			{
				words = candidate;
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// The text to lay a message out from: its part taken out and, once all of it is here, what it carried, as the
	/// pipe would show it (a reaction as its own line; pictures after the text, cards under it). The same text when
	/// it has none. <paramref name="original"/> is the line as it came (the text may be a translation of it).
	/// </summary>
	public static string Expand(string original, string text)
	{
		if (!Find(original, out string words, out int index))
		{
			return text;
		}
		string rest = text ?? "";
		int at = rest.LastIndexOf(words, StringComparison.Ordinal);
		if (at >= 0)
		{
			// With the space before it.
			int from = (at > 0 && rest[at - 1] == ' ') ? (at - 1) : at;
			rest = _emptyColor.Replace(rest.Remove(from, at + words.Length - from), "").TrimEnd();
		}
		Packet packet = null;
		if (index == 0)
		{
			lock (_lock)
			{
				_results.TryGetValue(words, out packet);
			}
		}
		if (packet == null)
		{
			return rest;
		}
		List<string> pictures = packet.Links.Where(l => !IsTag(l)).ToList();
		List<string> tags = packet.Links.Where(IsTag).ToList();
		if (packet.Reaction != null)
		{
			string line = IsaClient.Community.ChatActions.Line(packet.Reaction, packet.By, packet.To);
			if (line != null)
			{
				rest = line;
			}
		}
		if (pictures.Count > 0)
		{
			rest = rest + " " + string.Join(" ", pictures);
		}
		if (tags.Count > 0)
		{
			rest = rest + "\n" + string.Join(" ", tags);
		}
		return rest;
	}

	/// <summary>Is this a share tag (it goes under the message, not in its text)?</summary>
	internal static bool IsTag(string link) => link != null && link.StartsWith("[i/", StringComparison.Ordinal);

	private static bool TryFeed(string words, List<int> raw, out int index)
	{
		index = -1;
		int n = raw.Count;
		if (n < MinBytes || n > MaxBytes)
		{
			return false;
		}
		int check = raw[n - 3] | (raw[n - 2] << 8) | (raw[n - 1] << 16);
		List<int> bytes = raw.GetRange(0, n - 3);
		if (Check24(bytes, 2, bytes.Count - 2) != check)
		{
			return false;
		}
		ushort tid = (ushort)((bytes[0] | (bytes[1] << 8)) ^ IdMask(check));
		if (tid == 0)
		{
			return false;
		}
		Scramble(bytes, tid, encoding: false);
		index = bytes[2] & 0x7F;
		bool last = (bytes[2] & 0x80) != 0;
		byte[] chunk = bytes.Skip(3).Select(b => (byte)b).ToArray();
		Packet arrived = null;
		lock (_lock)
		{
			Expire();
			if (!_assembling.TryGetValue(tid, out Assembly a))
			{
				a = new Assembly();
				_assembling[tid] = a;
			}
			byte[] had = a.Chunks[index];
			if (had != null && !had.SequenceEqual(chunk))
			{
				// Another transfer that drew the same id: start over with this one.
				a = new Assembly();
				_assembling[tid] = a;
			}
			a.Touched = DateTime.Now;
			if (a.Done)
			{
				return true;
			}
			a.Chunks[index] = chunk;
			if (last)
			{
				a.Count = index + 1;
			}
			if (index == 0)
			{
				a.Head = words;
			}
			if (a.Count > 0 && a.Head != null && Enumerable.Range(0, a.Count).All(i => a.Chunks[i] != null))
			{
				a.Done = true;
				arrived = Unpack(a.Chunks.Take(a.Count).SelectMany(c => c).ToArray());
				if (arrived != null)
				{
					_results[a.Head] = arrived;
					_resultOrder.Enqueue(a.Head);
					while (_resultOrder.Count > 400)
					{
						_results.Remove(_resultOrder.Dequeue());
					}
				}
			}
		}
		if (arrived != null)
		{
			// Messages already shown get what they carried.
			ChatImageLayout.Relayout();
			try
			{
				Arrived?.Invoke(arrived);
			}
			catch (Exception ex)
			{
				IsaClient.Core.ClientLog.LogException(ex);
			}
		}
		return true;
	}

	private static void Expire()
	{
		if (_assembling.Count < 16)
		{
			return;
		}
		foreach (ushort tid in _assembling.Where(p => (DateTime.Now - p.Value.Touched).TotalMinutes > 5).Select(p => p.Key).ToList())
		{
			_assembling.Remove(tid);
		}
	}

	/// <summary>The whole packet back into what it carried (null when it's broken).</summary>
	private static Packet Unpack(byte[] stream)
	{
		// Its length, its records, then the check (the last part may be padded after it).
		if (stream.Length < 4)
		{
			return null;
		}
		int end = 2 + ((stream[0] << 8) | stream[1]);
		List<int> all = stream.Select(b => (int)b).ToList();
		if (end + 2 > stream.Length || Check16(all, 0, end) != (stream[end] | (stream[end + 1] << 8)))
		{
			return null;
		}
		Packet packet = new Packet();
		int at = 2;
		while (at + 2 <= end)
		{
			int kind = stream[at];
			int length = stream[at + 1];
			if (at + 2 + length > end)
			{
				return null;
			}
			byte[] content = new byte[length];
			Array.Copy(stream, at + 2, content, 0, length);
			Read(packet, kind, content);
			at += 2 + length;
		}
		return packet;
	}

	private static void Read(Packet packet, int kind, byte[] content)
	{
		ShareKind? share = (char)kind switch
		{
			'k' => ShareKind.Character,
			'y' => ShareKind.Style,
			'w' => ShareKind.Schematic,
			_ => null
		};
		if (share != null)
		{
			packet.Links.Add("[i/" + (char)kind + ShareKinds.Hex(content) + ":" + ShareKinds.Item(share.Value) + "]");
		}
		else if (kind == IsaClient.Sharing.FileShare.Letter)
		{
			// A file's card (IsaClient.Sharing.FileShare).
			packet.Links.Add("[i/" + (char)kind + ShareKinds.Hex(content) + ":" + IsaClient.Sharing.FileShare.VanillaItem + "]");
		}
		else if (kind == 'g')
		{
			string link = UnpackLink(content);
			if (ChatImages.IsImageLink(link))
			{
				packet.Links.Add(link);
			}
		}
		else if (kind == 'r')
		{
			string[] fields = new string[3];
			int at = 0;
			for (int i = 0; i < 3 && at < content.Length; i++)
			{
				int n = content[at++];
				fields[i] = Encoding.UTF8.GetString(content, at, Math.Min(n, content.Length - at));
				at += n;
			}
			packet.Reaction = fields[0];
			packet.By = fields[1] ?? "";
			packet.To = fields[2] ?? "";
		}
		else if (kind == 'f' && content.Length > 2 && 2 + content[1] < content.Length)
		{
			byte[] key = content.Skip(2).Take(content[1]).ToArray();
			string link = UnpackLink(content.Skip(2 + key.Length).ToArray());
			if (ChatImages.IsImageLink(link))
			{
				packet.Files.Add(((char)content[0], ShareKinds.Hex(key), link));
			}
		}
	}

	#endregion

	#region Bytes

	/// <summary>Picture and GIF links in few bytes: their host's fixed parts left out, ids as bytes.</summary>
	private static byte[] PackLink(string link)
	{
		List<byte> b = new List<byte>();
		Match m;
		List<int> imgur = PictureCodes.ToBytes(link);
		if (imgur != null)
		{
			b.Add(4);
			b.AddRange(imgur.Select(x => (byte)x));
		}
		else if ((m = _giphy.Match(link ?? "")).Success)
		{
			b.Add(1);
			b.AddRange(Encoding.ASCII.GetBytes(m.Groups[1].Value));
		}
		else if ((m = _klipy.Match(link ?? "")).Success)
		{
			b.Add(2);
			b.AddRange(ShareKinds.FromHex(m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value));
			b.AddRange(Encoding.ASCII.GetBytes(m.Groups[4].Value));
		}
		else if ((m = _nekos.Match(link ?? "")).Success)
		{
			b.Add(3);
			b.AddRange(ShareKinds.FromHex(m.Groups[2].Value + m.Groups[3].Value + m.Groups[4].Value + m.Groups[5].Value + m.Groups[6].Value));
			b.AddRange(Encoding.ASCII.GetBytes(m.Groups[1].Value));
		}
		else if (ChatImages.IsImageLink(link))
		{
			b.Add(0);
			b.AddRange(Encoding.UTF8.GetBytes(link));
		}
		else
		{
			return null;
		}
		return b.ToArray();
	}

	private static string UnpackLink(byte[] c)
	{
		try
		{
			switch ((c.Length > 0) ? c[0] : -1)
			{
			case 0:
				return Encoding.UTF8.GetString(c, 1, c.Length - 1);
			case 1:
				return "https://i.giphy.com/media/" + Encoding.ASCII.GetString(c, 1, c.Length - 1) + "/200.gif";
			case 2:
			{
				string hex = ShareKinds.Hex(c.Skip(1).Take(18).ToArray());
				return "https://static.klipy.com/ii/" + hex.Substring(0, 32) + "/" + hex.Substring(32, 2) + "/" + hex.Substring(34, 2) + "/" + Encoding.ASCII.GetString(c, 19, c.Length - 19) + ".gif";
			}
			case 3:
			{
				string hex = ShareKinds.Hex(c.Skip(1).Take(16).ToArray());
				return "https://nekos.best/api/v2/" + Encoding.ASCII.GetString(c, 17, c.Length - 17) + "/" + hex.Substring(0, 8) + "-" + hex.Substring(8, 4) + "-" + hex.Substring(12, 4) + "-" + hex.Substring(16, 4) + "-" + hex.Substring(20, 12) + ".gif";
			}
			case 4:
				return PictureCodes.FromBytes(c.Skip(1).Select(x => (int)x).ToList());
			}
		}
		catch
		{
		}
		return null;
	}

	/// <summary>
	/// Everything after the transfer id, mixed with a stream started from it; after the index, from the index too, so
	/// the parts of one thing don't look alike. Scrambling and unscrambling differ only in where the index is read.
	/// </summary>
	private static void Scramble(List<int> bytes, ushort tid, bool encoding)
	{
		uint state = Key ^ (tid * 2654435761u);
		int index = bytes[2];
		bytes[2] ^= Next(ref state);
		if (!encoding)
		{
			index = bytes[2];
		}
		state ^= (uint)((index & 0x7F) + 1) * 0x9E3779B9u;
		for (int i = 3; i < bytes.Count; i++)
		{
			bytes[i] ^= Next(ref state);
		}
	}

	private static int Next(ref uint state)
	{
		state ^= state << 13;
		state ^= state >> 17;
		state ^= state << 5;
		return (int)((state >> 8) & 0xFF);
	}

	/// <summary>What the id is mixed with in a part (from its check, so it's different in every part).</summary>
	private static int IdMask(int check) => (check * 40503 + 0x5A17) & 0xFFFF;

	private static ushort Check16(IList<int> bytes, int from, int count)
	{
		uint h = 2166136261u ^ (uint)(count * 29);
		for (int i = from; i < from + count; i++)
		{
			h = (h ^ (uint)bytes[i]) * 16777619u;
		}
		return (ushort)(h ^ (h >> 16));
	}

	private static int Check24(IList<int> bytes, int from, int count)
	{
		uint h = 0x811C9DC5u ^ (uint)(count * 131);
		for (int i = from; i < from + count; i++)
		{
			h = (h ^ (uint)bytes[i]) * 16777619u;
		}
		return (int)((h ^ (h >> 24)) & 0xFFFFFF);
	}

	private static ushort Check16(List<byte> bytes, int from, int count)
	{
		return Check16(bytes.Select(b => (int)b).ToList(), from, count);
	}

	#endregion
}
