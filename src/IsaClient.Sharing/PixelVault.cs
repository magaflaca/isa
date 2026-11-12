using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace IsaClient.Sharing;

/// <summary>
/// A file hidden in the pixels of a picture, so a character or a schematic can travel as an ordinary Imgur image: the
/// picture looks like a card, and its two lowest bits of every color carry the data (a change of at most 3 in 255,
/// invisible). Made to survive a picture host's small changes:
/// <list type="bullet">
/// <item>The bits are spread over the whole picture in an order drawn from a fixed key, so damage in one area
/// (a watermark, a few changed pixels) lands as scattered single errors.</item>
/// <item>The data goes in Reed-Solomon blocks (32 check bytes per 255, repairing 16 damaged bytes in each),
/// interleaved, so a run of damage is shared between many blocks.</item>
/// <item>The header (sizes, kind, CRC) is written three times and read by majority, with its own CRC.</item>
/// <item>When the picture has room, the whole thing is written up to three times: each block is repaired from the
/// copies' majority, or from any copy that alone is good enough.</item>
/// <item>A CRC32 of the whole file confirms it at the end: a picture changed past repair is refused, never misread.</item>
/// <item>Written as a plain PNG (8-bit RGB, no gamma or color profile chunks) by this class, so nothing on the way
/// adjusts the colors; read back from any PNG form (palette, filters, metadata stripped all give the same pixels).</item>
/// </list>
/// Lossy recompression (JPEG, WebP) wipes the low bits: then <see cref="Extract"/> says no, and the sender, who checks
/// every upload by reading it back, falls back to another way.
/// </summary>
public static class PixelVault
{
	private const uint Magic = 0x49534156u; // "ISAV"

	private const int HeaderBytes = 32;

	private const int HeaderCopies = 3;

	private const int Nsym = 32;

	private const int BlockData = 255 - Nsym;

	private const uint Key = 0x5EC12E7Bu;

	/// <summary>Bytes a picture of this size can carry (once; with room to spare it carries them more times).</summary>
	public static int Capacity(int width, int height)
	{
		long bytes = Room(width, height) - HeaderBytes * HeaderCopies;
		long blocks = bytes / 255;
		return (int)Math.Max(0, blocks * BlockData);
	}

	/// <summary>Bytes of room in the picture's low bits.</summary>
	private static long Room(int width, int height) => (long)width * height * 3 * 2 / 8;

	/// <summary>How many copies fit (1 to 3).</summary>
	private static int CopiesFor(int width, int height, int payload)
	{
		long one = HeaderBytes * HeaderCopies + (long)(payload + BlockData - 1) / BlockData * Nsym + payload;
		return (int)Math.Max(1, Math.Min(3, Room(width, height) / one));
	}

	#region Hiding

	/// <summary>
	/// The file hidden in a picture (24-bit, any size with room for it): the PNG to upload, or null when it doesn't fit.
	/// </summary>
	public static byte[] Hide(Bitmap cover, byte kind, byte[] payload)
	{
		int w = cover.Width;
		int h = cover.Height;
		if (payload == null || payload.Length == 0 || payload.Length > Capacity(w, h))
		{
			return null;
		}
		byte[] bgr = Pixels(cover, out int stride);
		int copies = CopiesFor(w, h, payload.Length);
		byte[] stream = Frame(kind, payload, copies);
		int[] order = Order(w, h);
		int place = 0;
		for (int c = 0; c < copies; c++)
		{
			for (int i = 0; i < stream.Length * 4; i++)
			{
				int value = (stream[i >> 2] >> (6 - (i & 3) * 2)) & 3;
				int at = Offset(order[place++], w, stride);
				bgr[at] = (byte)((bgr[at] & 0xFC) | value);
			}
		}
		return Png(bgr, w, h, stride);
	}

	/// <summary>Header three times, then the Reed-Solomon blocks interleaved byte by byte.</summary>
	private static byte[] Frame(byte kind, byte[] payload, int copies)
	{
		int blocks = (payload.Length + BlockData - 1) / BlockData;
		byte[] header = new byte[HeaderBytes];
		Put(header, 0, Magic);
		header[4] = 1;
		header[5] = kind;
		header[6] = (byte)copies;
		Put(header, 8, (uint)payload.Length);
		Put(header, 12, (uint)blocks);
		Put(header, 16, Crc32(payload, 0, payload.Length));
		Put(header, 28, Crc32(header, 0, 28));
		byte[][] coded = new byte[blocks][];
		for (int b = 0; b < blocks; b++)
		{
			int length = Math.Min(BlockData, payload.Length - b * BlockData);
			byte[] chunk = new byte[length];
			Array.Copy(payload, b * BlockData, chunk, 0, length);
			coded[b] = ReedSolomon.Encode(chunk, Nsym);
		}
		using MemoryStream s = new MemoryStream();
		for (int c = 0; c < HeaderCopies; c++)
		{
			s.Write(header, 0, header.Length);
		}
		for (int i = 0; i < 255; i++)
		{
			for (int b = 0; b < blocks; b++)
			{
				if (i < coded[b].Length)
				{
					s.WriteByte(coded[b][i]);
				}
			}
		}
		return s.ToArray();
	}

	#endregion

	#region Finding

	/// <summary>The file hidden in a picture (any image format the system reads), or null when there is none or it's broken.</summary>
	public static byte[] Extract(byte[] image, out byte kind)
	{
		kind = 0;
		try
		{
			using MemoryStream input = new MemoryStream(image);
			using Bitmap bitmap = new Bitmap(input);
			return Extract(bitmap, out kind);
		}
		catch (Exception)
		{
			return null;
		}
	}

	public static byte[] Extract(Bitmap bitmap, out byte kind)
	{
		kind = 0;
		int w = bitmap.Width;
		int h = bitmap.Height;
		if (Capacity(w, h) <= 0)
		{
			return null;
		}
		byte[] bgr = Pixels(bitmap, out int stride);
		int[] order = Order(w, h);
		int next = 0;
		byte Read()
		{
			int value = 0;
			for (int k = 0; k < 4; k++)
			{
				value = (value << 2) | (bgr[Offset(order[next++], w, stride)] & 3);
			}
			return (byte)value;
		}
		// The header by majority, bit by bit.
		byte[][] heads = new byte[HeaderCopies][];
		for (int c = 0; c < HeaderCopies; c++)
		{
			heads[c] = new byte[HeaderBytes];
			for (int i = 0; i < HeaderBytes; i++)
			{
				heads[c][i] = Read();
			}
		}
		byte[] header = new byte[HeaderBytes];
		for (int i = 0; i < HeaderBytes; i++)
		{
			header[i] = (byte)((heads[0][i] & heads[1][i]) | (heads[0][i] & heads[2][i]) | (heads[1][i] & heads[2][i]));
		}
		if (Get(header, 0) != Magic || header[4] != 1 || Get(header, 28) != Crc32(header, 0, 28))
		{
			return null;
		}
		kind = header[5];
		int copies = header[6];
		int length = (int)Get(header, 8);
		int blocks = (int)Get(header, 12);
		if (length <= 0 || blocks != (length + BlockData - 1) / BlockData || length > Capacity(w, h) || copies < 1 || copies > 3)
		{
			return null;
		}
		int streamBytes = HeaderBytes * HeaderCopies + blocks * Nsym + length;
		if ((long)streamBytes * copies > Room(w, h))
		{
			return null;
		}
		// Every copy's blocks (the later copies' headers are skipped: the first one's is enough).
		byte[][][] coded = new byte[copies][][];
		for (int c = 0; c < copies; c++)
		{
			next = (c * streamBytes + HeaderBytes * HeaderCopies) * 4;
			coded[c] = new byte[blocks][];
			for (int b = 0; b < blocks; b++)
			{
				coded[c][b] = new byte[Math.Min(BlockData, length - b * BlockData) + Nsym];
			}
			for (int i = 0; i < 255; i++)
			{
				for (int b = 0; b < blocks; b++)
				{
					if (i < coded[c][b].Length)
					{
						coded[c][b][i] = Read();
					}
				}
			}
		}
		byte[] payload = new byte[length];
		for (int b = 0; b < blocks; b++)
		{
			byte[] good = Repair(coded, b, copies);
			if (good == null)
			{
				return null;
			}
			Array.Copy(good, 0, payload, b * BlockData, good.Length - Nsym);
		}
		return (Crc32(payload, 0, payload.Length) == Get(header, 16)) ? payload : null;
	}

	/// <summary>One block from its copies: their majority first, then each copy alone.</summary>
	private static byte[] Repair(byte[][][] coded, int b, int copies)
	{
		if (copies == 3)
		{
			byte[] vote = new byte[coded[0][b].Length];
			for (int i = 0; i < vote.Length; i++)
			{
				byte x = coded[0][b][i];
				byte y = coded[1][b][i];
				byte z = coded[2][b][i];
				vote[i] = (byte)((x & y) | (x & z) | (y & z));
			}
			if (ReedSolomon.Decode(vote, Nsym))
			{
				return vote;
			}
		}
		for (int c = 0; c < copies; c++)
		{
			byte[] one = (byte[])coded[c][b].Clone();
			if (ReedSolomon.Decode(one, Nsym))
			{
				return one;
			}
		}
		return null;
	}

	#endregion

	#region Pixels

	/// <summary>Every 2-bit place in the picture (pixel * 3 + color), in an order drawn from the key.</summary>
	private static int[] Order(int w, int h)
	{
		int n = w * h * 3;
		int[] order = new int[n];
		for (int i = 0; i < n; i++)
		{
			order[i] = i;
		}
		uint state = Key ^ (uint)(w * 73856093) ^ (uint)(h * 19349663);
		for (int i = n - 1; i > 0; i--)
		{
			state ^= state << 13;
			state ^= state >> 17;
			state ^= state << 5;
			int j = (int)(state % (uint)(i + 1));
			int t = order[i];
			order[i] = order[j];
			order[j] = t;
		}
		return order;
	}

	private static int Offset(int place, int w, int stride)
	{
		int pixel = place / 3;
		return (pixel / w) * stride + (pixel % w) * 3 + place % 3;
	}

	/// <summary>The picture as 24-bit BGR rows.</summary>
	private static byte[] Pixels(Bitmap bitmap, out int stride)
	{
		Rectangle all = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
		BitmapData data = bitmap.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
		try
		{
			stride = data.Stride;
			byte[] bgr = new byte[stride * bitmap.Height];
			System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bgr, 0, bgr.Length);
			return bgr;
		}
		finally
		{
			bitmap.UnlockBits(data);
		}
	}

	/// <summary>A plain PNG: 8-bit RGB, no filters, no gamma or color profile (nothing that would shift the colors).</summary>
	private static byte[] Png(byte[] bgr, int w, int h, int stride)
	{
		byte[] raw = new byte[(w * 3 + 1) * h];
		int at = 0;
		for (int y = 0; y < h; y++)
		{
			raw[at++] = 0;
			for (int x = 0; x < w; x++)
			{
				int p = y * stride + x * 3;
				raw[at++] = bgr[p + 2];
				raw[at++] = bgr[p + 1];
				raw[at++] = bgr[p];
			}
		}
		using MemoryStream z = new MemoryStream();
		z.WriteByte(0x78);
		z.WriteByte(0x9C);
		using (DeflateStream deflate = new DeflateStream(z, CompressionLevel.Optimal, leaveOpen: true))
		{
			deflate.Write(raw, 0, raw.Length);
		}
		uint a = 1, b = 0;
		foreach (byte v in raw)
		{
			a = (a + v) % 65521;
			b = (b + a) % 65521;
		}
		WriteBig(z, (b << 16) | a);
		using MemoryStream png = new MemoryStream();
		png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
		byte[] ihdr = new byte[13];
		PutBig(ihdr, 0, (uint)w);
		PutBig(ihdr, 4, (uint)h);
		ihdr[8] = 8;
		ihdr[9] = 2;
		Chunk(png, "IHDR", ihdr);
		Chunk(png, "IDAT", z.ToArray());
		Chunk(png, "IEND", new byte[0]);
		return png.ToArray();
	}

	private static void Chunk(Stream s, string type, byte[] data)
	{
		byte[] head = new byte[4 + 4 + data.Length];
		PutBig(head, 0, (uint)data.Length);
		Encoding.ASCII.GetBytes(type).CopyTo(head, 4);
		data.CopyTo(head, 8);
		s.Write(head, 0, head.Length);
		WriteBig(s, Crc32(head, 4, head.Length - 4));
	}

	#endregion

	#region Numbers

	private static readonly uint[] _crc = MakeCrc();

	private static uint[] MakeCrc()
	{
		uint[] table = new uint[256];
		for (uint n = 0; n < 256; n++)
		{
			uint c = n;
			for (int k = 0; k < 8; k++)
			{
				c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
			}
			table[n] = c;
		}
		return table;
	}

	public static uint Crc32(byte[] data, int offset, int count)
	{
		uint c = 0xFFFFFFFFu;
		for (int i = offset; i < offset + count; i++)
		{
			c = _crc[(c ^ data[i]) & 0xFF] ^ (c >> 8);
		}
		return c ^ 0xFFFFFFFFu;
	}

	private static void Put(byte[] b, int at, uint v) => BitConverter.GetBytes(v).CopyTo(b, at);

	private static uint Get(byte[] b, int at) => BitConverter.ToUInt32(b, at);

	private static void PutBig(byte[] b, int at, uint v)
	{
		b[at] = (byte)(v >> 24);
		b[at + 1] = (byte)(v >> 16);
		b[at + 2] = (byte)(v >> 8);
		b[at + 3] = (byte)v;
	}

	private static void WriteBig(Stream s, uint v)
	{
		byte[] b = new byte[4];
		PutBig(b, 0, v);
		s.Write(b, 0, 4);
	}

	#endregion
}
