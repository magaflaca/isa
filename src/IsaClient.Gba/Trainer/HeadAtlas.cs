using System;
using System.IO;
using System.IO.Compression;

namespace IsaClient.Gba.Trainer;

/// <summary>A baked head picture: a symbol per pixel (see tools/gba/make_heads.py) and the "behind the body" flag.</summary>
public sealed class HeadImage
{
	public int W;

	public int H;

	/// <summary>0 empty, 1-4 skin, 5-8 hair, 9 eye, 10 outline, 11 hair outline.</summary>
	public byte[,] Sym;

	/// <summary>Depth below 3: long hair that the torso hides in front views.</summary>
	public bool[,] Back;

	public HeadImage Mirror()
	{
		HeadImage m = new HeadImage { W = W, H = H, Sym = new byte[H, W], Back = new bool[H, W] };
		for (int y = 0; y < H; y++)
		{
			for (int x = 0; x < W; x++)
			{
				m.Sym[y, x] = Sym[y, W - 1 - x];
				m.Back[y, x] = Back[y, W - 1 - x];
			}
		}
		return m;
	}

	public Rgba[,] Paint(TrainerColors look)
	{
		Rgba[,] img = new Rgba[H, W];
		for (int y = 0; y < H; y++)
		{
			for (int x = 0; x < W; x++)
			{
				if (Sym[y, x] != 0)
				{
					img[y, x] = new Rgba(look.Symbol(Sym[y, x]));
				}
			}
		}
		return img;
	}
}

/// <summary>The five head pictures of one hairstyle for one gender.</summary>
public sealed class HeadSet
{
	public HeadImage Down;

	public HeadImage Up;

	public HeadImage Left;

	public HeadImage Right;

	/// <summary>96x96, skull centre at 48,48 (trainer card / intro picture).</summary>
	public HeadImage Front;

	/// <summary>96x96 (battle back sprite).</summary>
	public HeadImage Back;

	public HeadImage Of(HeadDir dir) => dir switch
	{
		HeadDir.Down => Down,
		HeadDir.Up => Up,
		HeadDir.Left => Left,
		_ => Right
	};
}

/// <summary>
/// The Pokémon heads of every Terraria hairstyle, baked from terraria-to-pokemon's 3D head model
/// (IsaClient.Assets/PokemonHeads.bin, embedded). Read once.
/// </summary>
public static class HeadAtlas
{
	public const string ResourceName = "IsaClient.Assets.PokemonHeads.bin";

	private static byte[] _raw;

	private static int[] _offsets;

	public static int Count => Load() ? _offsets.Length : 0;

	/// <summary>For tests: the atlas from a file instead of the embedded copy.</summary>
	public static void UseFile(string path)
	{
		_raw = Inflate(File.ReadAllBytes(path));
		Index();
	}

	private static bool Load()
	{
		if (_raw != null)
		{
			return true;
		}
		using Stream s = typeof(HeadAtlas).Assembly.GetManifestResourceStream(ResourceName);
		if (s == null)
		{
			return false;
		}
		byte[] packed = new byte[s.Length];
		s.Read(packed, 0, packed.Length);
		_raw = Inflate(packed);
		Index();
		return true;
	}

	private static byte[] Inflate(byte[] zlib)
	{
		// zlib = 2 header bytes + deflate + adler32
		using MemoryStream src = new MemoryStream(zlib, 2, zlib.Length - 6);
		using DeflateStream d = new DeflateStream(src, CompressionMode.Decompress);
		using MemoryStream o = new MemoryStream();
		d.CopyTo(o);
		return o.ToArray();
	}

	private static void Index()
	{
		if (_raw[0] != 'P' || _raw[1] != 'K' || _raw[2] != 'H' || _raw[3] != '1')
		{
			throw new InvalidDataException("Bad head atlas");
		}
		int count = _raw[4] | (_raw[5] << 8);
		_offsets = new int[count];
		int p = 6;
		for (int i = 0; i < count; i++)
		{
			_offsets[i] = p;
			int variants = (_raw[p] > 0) ? 2 : 1;
			p++;
			for (int v = 0; v < variants * 5; v++)
			{
				p += 4 + _raw[p + 2] * _raw[p + 3];
			}
		}
	}

	/// <summary>The beard level Terraria's hairstyle implies (0 none).</summary>
	public static int Beard(int hair)
	{
		Load();
		return _raw[_offsets[Clamp(hair)]];
	}

	private static int Clamp(int hair) => (hair < 0 || hair >= _offsets.Length) ? 0 : hair;

	public static HeadSet Get(int hair, bool male)
	{
		if (!Load())
		{
			throw new InvalidOperationException("The Pokémon head atlas is not in this build");
		}
		int p = _offsets[Clamp(hair)];
		bool two = _raw[p] > 0;
		p++;
		if (two && !male)
		{
			for (int v = 0; v < 5; v++)
			{
				p += 4 + _raw[p + 2] * _raw[p + 3];
			}
		}
		HeadImage Read(int w, int h)
		{
			HeadImage img = new HeadImage { W = w, H = h, Sym = new byte[h, w], Back = new bool[h, w] };
			int x0 = _raw[p];
			int y0 = _raw[p + 1];
			int cw = _raw[p + 2];
			int ch = _raw[p + 3];
			p += 4;
			for (int y = 0; y < ch; y++)
			{
				for (int x = 0; x < cw; x++)
				{
					byte b = _raw[p++];
					img.Sym[y0 + y, x0 + x] = (byte)(b & 15);
					img.Back[y0 + y, x0 + x] = (b & 0x80) != 0;
				}
			}
			return img;
		}
		HeadSet set = new HeadSet { Down = Read(32, 44), Up = Read(32, 44), Left = Read(32, 44) };
		set.Right = set.Left.Mirror();
		set.Front = Read(96, 96);
		set.Back = Read(96, 96);
		return set;
	}
}
