using System;
using System.Collections.Generic;

namespace IsaClient.Gba.Trainer;

/// <summary>
/// FireRed / LeafGreen player sprite access (t2p/rom.py). Tables are found by their content, not fixed offsets, so any
/// language works. The ROM is patched in memory: the file on disk is never touched.
/// </summary>
public sealed class FireRedRom
{
	public const uint Gba = 0x08000000;

	public const int PalTagPlayer = 0x1100;

	public const int PalTagReflection = 0x1101;

	public readonly byte[] Data;

	public readonly string Code;

	public readonly TrainerGame Game;

	private readonly int _table;

	private readonly Dictionary<int, int> _palEntries = new Dictionary<int, int>();

	public FireRedRom(byte[] data)
	{
		Data = data;
		Code = GameCode(data);
		Game = TrainerGame.For(data);
		if (Game == null)
		{
			throw new InvalidOperationException("Not a FireRed/LeafGreen/Emerald ROM (game code " + Code + ")");
		}
		_table = FindGraphicsTable();
		FindPaletteEntries();
	}

	public static string GameCode(byte[] data)
	{
		if (data == null || data.Length < 0xC0)
		{
			return "";
		}
		char[] c = new char[4];
		for (int i = 0; i < 4; i++)
		{
			byte b = data[0xAC + i];
			c[i] = (b >= 32 && b < 127) ? (char)b : '?';
		}
		return new string(c);
	}

	/// <summary>A game whose player can be turned into a Terraria character.</summary>
	public static bool IsFireRedFamily(byte[] data) => TrainerGame.For(data) != null;

	public uint U32(int o) => (uint)(Data[o] | (Data[o + 1] << 8) | (Data[o + 2] << 16) | (Data[o + 3] << 24));

	public int U16(int o) => Data[o] | (Data[o + 1] << 8);

	public short S16(int o) => (short)U16(o);

	public void Put32(int o, uint v)
	{
		Data[o] = (byte)v;
		Data[o + 1] = (byte)(v >> 8);
		Data[o + 2] = (byte)(v >> 16);
		Data[o + 3] = (byte)(v >> 24);
	}

	public void Put16(int o, int v)
	{
		Data[o] = (byte)v;
		Data[o + 1] = (byte)(v >> 8);
	}

	/// <summary>bytes.find: first index of <paramref name="pattern"/> at or after <paramref name="from"/>, or -1.</summary>
	public int Find(byte[] pattern, int from = 0)
	{
		int last = Data.Length - pattern.Length;
		byte first = pattern[0];
		for (int i = Math.Max(0, from); i <= last; i++)
		{
			if (Data[i] != first)
			{
				continue;
			}
			int k = 1;
			while (k < pattern.Length && Data[i + k] == pattern[k])
			{
				k++;
			}
			if (k == pattern.Length)
			{
				return i;
			}
		}
		return -1;
	}

	public static byte[] Pack16(params int[] values)
	{
		byte[] b = new byte[values.Length * 2];
		for (int i = 0; i < values.Length; i++)
		{
			b[i * 2] = (byte)values[i];
			b[i * 2 + 1] = (byte)(values[i] >> 8);
		}
		return b;
	}

	public static byte[] Pack32(uint v) => new[] { (byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24) };

	private int FindGraphicsTable()
	{
		byte[] sig = Pack16(0xFFFF, PalTagPlayer);
		int pos = 0;
		while (true)
		{
			int gi = Find(sig, pos);
			if (gi < 0)
			{
				throw new InvalidOperationException("Player graphics info not found");
			}
			if (gi + 12 > Data.Length || U16(gi + 6) != 512 || U16(gi + 8) != 16 || U16(gi + 10) != 32)
			{
				pos = gi + 2;
				continue;
			}
			int t = Find(Pack32((uint)gi + Gba));
			// RED_NORMAL is entry 0: the next one must be RED_BIKE (same palette)
			if (t >= 0 && t % 4 == 0)
			{
				long next = (long)U32(t + 4) - Gba;
				if (next >= 0 && next + 4 < Data.Length && U16((int)next + 2) == PalTagPlayer)
				{
					return t;
				}
			}
			pos = gi + 4;
		}
	}

	private void FindPaletteEntries()
	{
		foreach (int tag in Game.AllPaletteTags)
		{
			byte[] pat = Pack16(tag, 0);
			int pos = 0;
			while (true)
			{
				int o = Find(pat, pos);
				if (o < 0)
				{
					break;
				}
				uint p = (o >= 4) ? U32(o - 4) : 0u;
				if ((o - 4) % 4 == 0 && p >> 24 == 8 && U32(o + 4) >> 24 == 8)
				{
					_palEntries[tag] = o - 4;
					break;
				}
				pos = o + 2;
			}
		}
		if (_palEntries.Count != Game.PaletteTags.Length * 2)
		{
			throw new InvalidOperationException("Player palettes not found");
		}
	}

	private int PaletteOffset(int tag) => (int)(U32(_palEntries[tag]) - Gba);

	public Rgb[] ReadPalette(int tag)
	{
		int o = PaletteOffset(tag);
		Rgb[] pal = new Rgb[16];
		for (int i = 0; i < 16; i++)
		{
			pal[i] = Bgr555(U16(o + 2 * i));
		}
		return pal;
	}

	/// <summary>The palette as the game copies it to memory (16 BGR555 colors, 32 bytes).</summary>
	public byte[] RawPalette(int tag)
	{
		byte[] b = new byte[32];
		Buffer.BlockCopy(Data, PaletteOffset(tag), b, 0, 32);
		return b;
	}

	public void WritePalette(int tag, Rgb[] colors)
	{
		int o = PaletteOffset(tag);
		for (int i = 0; i < colors.Length; i++)
		{
			Put16(o + 2 * i, ToBgr555(colors[i]));
		}
	}

	public static Rgb Bgr555(int c) => new Rgb((c & 31) * 8, ((c >> 5) & 31) * 8, ((c >> 10) & 31) * 8);

	public static int ToBgr555(Rgb c) => (c.R >> 3) | ((c.G >> 3) << 5) | ((c.B >> 3) << 10);

	/// <summary>Offset, width and height of each unique frame of a graphics id.</summary>
	public List<(int Off, int W, int H)> Frames(int gfxId, int count)
	{
		int gi = (int)(U32(_table + 4 * gfxId) - Gba);
		int w = S16(gi + 8);
		int h = S16(gi + 10);
		int imgs = (int)(U32(gi + 0x1C) - Gba);
		List<(int, int, int)> list = new List<(int, int, int)>();
		HashSet<int> seen = new HashSet<int>();
		int k = 0;
		while (list.Count < count)
		{
			int p = (int)(U32(imgs + 8 * k) - Gba);
			k++;
			if (!seen.Add(p))
			{
				continue;
			}
			list.Add((p, w, h));
		}
		return list;
	}

	public byte[,] ReadFrame(int off, int w, int h) => Decode4bpp(Data, off, w, h);

	public void WriteFrame(int off, byte[,] idx)
	{
		byte[] b = Encode4bpp(idx);
		Buffer.BlockCopy(b, 0, Data, off, b.Length);
	}

	public static byte[,] Decode4bpp(byte[] buf, int start, int w, int h)
	{
		byte[,] a = new byte[h, w];
		int n = 0;
		for (int ty = 0; ty < h / 8; ty++)
		{
			for (int tx = 0; tx < w / 8; tx++)
			{
				for (int y = 0; y < 8; y++)
				{
					for (int x = 0; x < 8; x += 2)
					{
						byte b = buf[start + n * 32 + y * 4 + x / 2];
						a[ty * 8 + y, tx * 8 + x] = (byte)(b & 15);
						a[ty * 8 + y, tx * 8 + x + 1] = (byte)(b >> 4);
					}
				}
				n++;
			}
		}
		return a;
	}

	public static byte[] Encode4bpp(byte[,] a)
	{
		int h = a.GetLength(0);
		int w = a.GetLength(1);
		byte[] o = new byte[w * h / 2];
		int i = 0;
		for (int ty = 0; ty < h / 8; ty++)
		{
			for (int tx = 0; tx < w / 8; tx++)
			{
				for (int y = 0; y < 8; y++)
				{
					for (int x = 0; x < 8; x += 2)
					{
						o[i++] = (byte)(a[ty * 8 + y, tx * 8 + x] | (a[ty * 8 + y, tx * 8 + x + 1] << 4));
					}
				}
			}
		}
		return o;
	}
}

/// <summary>GBA BIOS LZ77 (type 0x10), byte-exact with t2p/lz77.py.</summary>
public static class Lz77
{
	public static byte[] Decompress(byte[] data, int off)
	{
		if (data[off] != 0x10)
		{
			throw new InvalidOperationException("not LZ77");
		}
		int size = data[off + 1] | (data[off + 2] << 8) | (data[off + 3] << 16);
		byte[] o = new byte[size];
		int n = 0;
		int i = off + 4;
		while (n < size)
		{
			int flags = data[i++];
			for (int b = 0; b < 8 && n < size; b++)
			{
				if ((flags & (0x80 >> b)) != 0)
				{
					int len = (data[i] >> 4) + 3;
					int disp = (((data[i] & 15) << 8) | data[i + 1]) + 1;
					i += 2;
					for (int k = 0; k < len && n < size; k++)
					{
						o[n] = o[n - disp];
						n++;
					}
				}
				else
				{
					o[n++] = data[i++];
				}
			}
		}
		return o;
	}

	/// <summary>Greedy compression (VRAM safe: displacement at least 2), padded to 4 bytes.</summary>
	public static byte[] Compress(byte[] src)
	{
		List<byte> o = new List<byte> { 0x10, (byte)src.Length, (byte)(src.Length >> 8), (byte)(src.Length >> 16) };
		int i = 0;
		int n = src.Length;
		while (i < n)
		{
			int flagPos = o.Count;
			o.Add(0);
			int flags = 0;
			for (int b = 0; b < 8; b++)
			{
				if (i >= n)
				{
					break;
				}
				int bestLen = 0;
				int bestDisp = 0;
				int maxDisp = Math.Min(4096, i);
				for (int disp = 2; disp <= maxDisp; disp++)
				{
					int l = 0;
					while (l < 18 && i + l < n && src[i + l] == src[i - disp + l])
					{
						l++;
					}
					if (l > bestLen)
					{
						bestLen = l;
						bestDisp = disp;
						if (l == 18)
						{
							break;
						}
					}
				}
				if (bestLen >= 3)
				{
					flags |= 0x80 >> b;
					int d = bestDisp - 1;
					o.Add((byte)(((bestLen - 3) << 4) | (d >> 8)));
					o.Add((byte)(d & 255));
					i += bestLen;
				}
				else
				{
					o.Add(src[i]);
					i++;
				}
			}
			o[flagPos] = (byte)flags;
		}
		while (o.Count % 4 != 0)
		{
			o.Add(0);
		}
		return o.ToArray();
	}
}
