using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace IsaClient.Psp;

/// <summary>The kinds of files a PSP game comes in.</summary>
public enum PspFormat
{
	None,

	/// <summary>A UMD image.</summary>
	Iso,

	/// <summary>A compressed UMD image.</summary>
	Cso,

	/// <summary>A compressed disc image (MAME's format).</summary>
	Chd,

	/// <summary>An EBOOT.PBP: homebrew and PSN games.</summary>
	Pbp,

	/// <summary>A bare program (.elf or .prx): homebrew.</summary>
	Elf
}

/// <summary>What's written on a game: its name, its id and its pictures (PNG files, as they're stored).</summary>
public sealed class PspInfo
{
	public PspFormat Format;

	public string Title = "";

	/// <summary>"ULES00000" and the like ("" for homebrew without one).</summary>
	public string DiscId = "";

	public byte[] Icon;

	public byte[] Background;
}

/// <summary>
/// Reads PSP games without running them: the format from the first bytes, and PARAM.SFO, ICON0 and PIC1 from inside the
/// UMD's file system (ISO, also compressed as CSO) or an EBOOT.PBP. CHD and bare programs have only a file name.
/// </summary>
public static class PspRom
{
	/// <summary>Files that are a game by their name (what's inside says if it's really one).</summary>
	public static readonly string[] Extensions = { ".iso", ".cso", ".chd", ".pbp", ".elf", ".prx" };

	public static bool IsGameFile(string name)
	{
		string ext = Path.GetExtension(name ?? "").ToLowerInvariant();
		return Array.IndexOf(Extensions, ext) >= 0;
	}

	/// <summary>Archives a game can come in.</summary>
	public static bool IsArchive(string name)
	{
		string ext = Path.GetExtension(name ?? "").ToLowerInvariant();
		return ext == ".zip" || ext == ".7z";
	}

	/// <summary>The format of a file by its first bytes, or None. <paramref name="why"/> says what's wrong with it when it's known.</summary>
	public static PspFormat Identify(string path, out string why)
	{
		why = null;
		try
		{
			using FileStream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			byte[] head = new byte[0x8010];
			int n = Fill(s, head);
			if (n >= 4)
			{
				if (Tag(head, "CISO"))
				{
					return PspFormat.Cso;
				}
				if (Tag(head, "ZISO"))
				{
					why = "ZSO images don't run: use the .iso or .cso of the game.";
					return PspFormat.None;
				}
				if (head[0] == 0 && head[1] == (byte)'P' && head[2] == (byte)'B' && head[3] == (byte)'P')
				{
					if (IsPs1Classic(s, head))
					{
						why = "That's a PlayStation 1 game: the PSP emulator only plays PSP games.";
						return PspFormat.None;
					}
					return PspFormat.Pbp;
				}
				if ((head[0] == 0x7F && head[1] == (byte)'E' && head[2] == (byte)'L' && head[3] == (byte)'F') || Tag(head, "~PSP"))
				{
					return PspFormat.Elf;
				}
			}
			if (n >= 8 && Tag(head, "MComprHD"))
			{
				return PspFormat.Chd;
			}
			if (n >= 0x8006 && head[0x8001] == (byte)'C' && head[0x8002] == (byte)'D' && head[0x8003] == (byte)'0' && head[0x8004] == (byte)'0' && head[0x8005] == (byte)'1')
			{
				return PspFormat.Iso;
			}
		}
		catch (Exception ex)
		{
			why = "Couldn't read it: " + ex.Message;
		}
		return PspFormat.None;
	}

	private static bool Tag(byte[] data, string tag)
	{
		for (int i = 0; i < tag.Length; i++)
		{
			if (data[i] != tag[i])
			{
				return false;
			}
		}
		return true;
	}

	private static int Fill(Stream s, byte[] into)
	{
		int at = 0;
		while (at < into.Length)
		{
			int n = s.Read(into, at, into.Length - at);
			if (n <= 0)
			{
				break;
			}
			at += n;
		}
		return at;
	}

	/// <summary>The PBP's data starts with PSISOIMG / PSTITLEIMG on PS1 classics.</summary>
	private static bool IsPs1Classic(Stream s, byte[] head)
	{
		try
		{
			long psar = BitConverter.ToUInt32(head, 0x24);
			if (psar <= 0 || psar + 10 > s.Length)
			{
				return false;
			}
			s.Position = psar;
			byte[] tag = new byte[10];
			Fill(s, tag);
			string text = Encoding.ASCII.GetString(tag);
			return text.StartsWith("PSISOIMG") || text.StartsWith("PSTITLEIMG");
		}
		catch
		{
			return false;
		}
	}

	/// <summary>What can be read from a game (always at least a title, from its file name).</summary>
	public static PspInfo Read(string path)
	{
		PspInfo info = new PspInfo { Format = Identify(path, out _) };
		try
		{
			if (info.Format == PspFormat.Iso || info.Format == PspFormat.Cso)
			{
				using FileStream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				using Disc disc = (info.Format == PspFormat.Cso) ? new CsoDisc(s) : new Disc(s);
				ReadSfo(info, disc.File("PSP_GAME/PARAM.SFO"));
				info.Icon = disc.File("PSP_GAME/ICON0.PNG");
				info.Background = disc.File("PSP_GAME/PIC1.PNG");
			}
			else if (info.Format == PspFormat.Pbp)
			{
				ReadPbp(path, info);
			}
		}
		catch
		{
			// damaged or unusual: the game may still run, it just goes by its file name
		}
		if (info.Title.Length == 0)
		{
			info.Title = FileTitle(path);
		}
		return info;
	}

	/// <summary>A title from the file name: "Name (Europe) (PSP) (PSN).iso" is "Name"; EBOOT.PBP goes by its folder.</summary>
	public static string FileTitle(string path)
	{
		string name = Path.GetFileNameWithoutExtension(path ?? "");
		if (string.Equals(name, "EBOOT", StringComparison.OrdinalIgnoreCase))
		{
			name = Path.GetFileName(Path.GetDirectoryName(path) ?? "") ?? name;
		}
		int bracket = name.IndexOfAny(new[] { '(', '[' });
		if (bracket > 2)
		{
			name = name.Substring(0, bracket);
		}
		return name.Replace('_', ' ').Trim();
	}

	/// <summary>The key a game's saves go under: its disc id and title (any copy of it shares them), else its file.</summary>
	public static string Key(string path, PspInfo info)
	{
		string text = (info.DiscId.Length > 0) ? (info.DiscId + "|" + info.Title) : Path.GetFileName(path).ToLowerInvariant() + "|" + SizeOf(path);
		uint crc = 0;
		foreach (char c in text)
		{
			crc = crc * 31 + c;
		}
		string id = new string((info.DiscId.Length > 0 ? info.DiscId : "PSP").Where(char.IsLetterOrDigit).ToArray());
		return id + "-" + crc.ToString("x8");
	}

	private static long SizeOf(string path)
	{
		try
		{
			return new FileInfo(path).Length;
		}
		catch
		{
			return 0;
		}
	}

	private static void ReadPbp(string path, PspInfo info)
	{
		using FileStream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		byte[] head = new byte[0x28];
		if (Fill(s, head) < 0x28)
		{
			return;
		}
		// offsets: PARAM.SFO, ICON0.PNG, ICON1.PMF, PIC0.PNG, PIC1.PNG, SND0.AT3, DATA.PSP, DATA.PSAR
		long[] at = new long[8];
		for (int i = 0; i < 8; i++)
		{
			at[i] = BitConverter.ToUInt32(head, 8 + i * 4);
		}
		byte[] Part(int i)
		{
			long start = at[i];
			long end = (i + 1 < 8) ? at[i + 1] : s.Length;
			long size = end - start;
			if (start <= 0 || size <= 0 || size > 8 * 1024 * 1024 || end > s.Length)
			{
				return null;
			}
			byte[] b = new byte[size];
			s.Position = start;
			return (Fill(s, b) == size) ? b : null;
		}
		ReadSfo(info, Part(0));
		info.Icon = Part(1);
		info.Background = Part(4);
	}

	/// <summary>PARAM.SFO: a table of named values; TITLE and DISC_ID are what's wanted.</summary>
	private static void ReadSfo(PspInfo info, byte[] sfo)
	{
		if (sfo == null || sfo.Length < 20 || sfo[1] != (byte)'P' || sfo[2] != (byte)'S' || sfo[3] != (byte)'F')
		{
			return;
		}
		int keys = BitConverter.ToInt32(sfo, 8);
		int values = BitConverter.ToInt32(sfo, 12);
		int count = BitConverter.ToInt32(sfo, 16);
		for (int i = 0; i < count; i++)
		{
			int e = 20 + i * 16;
			if (e + 16 > sfo.Length)
			{
				return;
			}
			int keyAt = keys + BitConverter.ToUInt16(sfo, e);
			int length = BitConverter.ToInt32(sfo, e + 4);
			int valueAt = values + BitConverter.ToInt32(sfo, e + 12);
			if (keyAt >= sfo.Length || valueAt < 0 || length < 0 || valueAt + length > sfo.Length)
			{
				continue;
			}
			int end = Array.IndexOf(sfo, (byte)0, keyAt);
			string key = Encoding.ASCII.GetString(sfo, keyAt, ((end < 0) ? sfo.Length : end) - keyAt);
			if (key != "TITLE" && key != "DISC_ID")
			{
				continue;
			}
			string text = Encoding.UTF8.GetString(sfo, valueAt, length).Split('\0')[0];
			if (key == "TITLE")
			{
				info.Title = Clean(text);
			}
			else
			{
				info.DiscId = text.Trim();
			}
		}
	}

	/// <summary>Titles often have ® or ™, which Terraria's font doesn't draw, and line breaks.</summary>
	private static string Clean(string title)
	{
		StringBuilder b = new StringBuilder();
		foreach (char c in title)
		{
			if (c == '\n' || c == '\r')
			{
				b.Append(' ');
			}
			else if (c != '®' && c != '™' && c != '©' && !char.IsControl(c))
			{
				b.Append(c);
			}
		}
		return b.ToString().Replace("  ", " ").Trim();
	}

	/// <summary>A UMD's ISO 9660 file system, 2048 bytes a sector.</summary>
	private class Disc : IDisposable
	{
		protected const int Sector = 2048;

		protected readonly Stream Source;

		public Disc(Stream source)
		{
			Source = source;
		}

		public void Dispose()
		{
		}

		/// <summary>Reads whole sectors (short at the end of the disc).</summary>
		protected virtual byte[] Sectors(long first, int count)
		{
			byte[] b = new byte[count * Sector];
			Source.Position = first * Sector;
			Fill(Source, b);
			return b;
		}

		/// <summary>A file by its path, or null. Big files aren't for here: only what the game's card needs.</summary>
		public byte[] File(string path)
		{
			byte[] volume = Sectors(16, 1);
			// the root directory's record is inside the volume descriptor, at 156
			long extent = BitConverter.ToUInt32(volume, 156 + 2);
			long size = BitConverter.ToUInt32(volume, 156 + 10);
			string[] parts = path.Split('/');
			for (int i = 0; i < parts.Length; i++)
			{
				if (!Find(extent, size, parts[i], out extent, out size))
				{
					return null;
				}
			}
			if (size <= 0 || size > 8 * 1024 * 1024)
			{
				return null;
			}
			byte[] data = Sectors(extent, (int)((size + Sector - 1) / Sector));
			Array.Resize(ref data, (int)size);
			return data;
		}

		private bool Find(long extent, long size, string name, out long fileExtent, out long fileSize)
		{
			fileExtent = 0;
			fileSize = 0;
			if (size <= 0 || size > 4 * 1024 * 1024)
			{
				return false;
			}
			byte[] dir = Sectors(extent, (int)((size + Sector - 1) / Sector));
			int at = 0;
			while (at + 34 <= size)
			{
				int length = dir[at];
				if (length == 0)
				{
					// records don't cross sectors: on to the next one
					at = (at / Sector + 1) * Sector;
					continue;
				}
				int nameLength = dir[at + 32];
				if (at + 33 + nameLength <= dir.Length)
				{
					string entry = Encoding.ASCII.GetString(dir, at + 33, nameLength);
					int version = entry.IndexOf(';');
					if (version >= 0)
					{
						entry = entry.Substring(0, version);
					}
					if (string.Equals(entry, name, StringComparison.OrdinalIgnoreCase))
					{
						fileExtent = BitConverter.ToUInt32(dir, at + 2);
						fileSize = BitConverter.ToUInt32(dir, at + 10);
						return true;
					}
				}
				at += length;
			}
			return false;
		}
	}

	/// <summary>A CSO: the same disc in blocks, each deflated (or stored) on its own, found through an index.</summary>
	private sealed class CsoDisc : Disc
	{
		private readonly uint[] _index;

		private readonly int _align;

		private readonly int _block;

		public CsoDisc(Stream source)
			: base(source)
		{
			byte[] head = new byte[24];
			Source.Position = 0;
			Fill(Source, head);
			long total = BitConverter.ToInt64(head, 8);
			_block = BitConverter.ToInt32(head, 16);
			_align = head[21];
			if (_block < Sector || _block > 1 << 20 || total <= 0 || total > 16L * 1024 * 1024 * 1024)
			{
				throw new InvalidDataException("Not a CSO");
			}
			int blocks = (int)((total + _block - 1) / _block);
			byte[] table = new byte[(blocks + 1) * 4];
			Fill(Source, table);
			_index = new uint[blocks + 1];
			Buffer.BlockCopy(table, 0, _index, 0, table.Length);
		}

		protected override byte[] Sectors(long first, int count)
		{
			byte[] b = new byte[count * Sector];
			long at = first * Sector;
			int done = 0;
			while (done < b.Length)
			{
				long block = at / _block;
				if (block + 1 >= _index.Length)
				{
					break;
				}
				byte[] data = Block((int)block);
				int offset = (int)(at % _block);
				int n = Math.Min(b.Length - done, data.Length - offset);
				if (n <= 0)
				{
					break;
				}
				Buffer.BlockCopy(data, offset, b, done, n);
				done += n;
				at += n;
			}
			return b;
		}

		private byte[] Block(int i)
		{
			bool stored = (_index[i] & 0x80000000u) != 0;
			long start = (long)(_index[i] & 0x7FFFFFFFu) << _align;
			long end = (long)(_index[i + 1] & 0x7FFFFFFFu) << _align;
			byte[] raw = new byte[Math.Max(0, end - start)];
			Source.Position = start;
			Fill(Source, raw);
			if (stored)
			{
				return raw;
			}
			byte[] data = new byte[_block];
			using DeflateStream d = new DeflateStream(new MemoryStream(raw), CompressionMode.Decompress);
			Fill(d, data);
			return data;
		}
	}
}
