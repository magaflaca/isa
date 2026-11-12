using System;
using System.Collections.Generic;

namespace IsaClient.Gba.Trainer;

/// <summary>
/// The battle back sprite (5 frames, 64x64) and the trainer card / intro picture of Red and Leaf, rebuilt with the
/// character's head and colors (t2p/pics.py). New palettes and the front picture go to IsaClient's own block at
/// <see cref="FreeBase"/>, in the 0xFF padding at the end of the ROM.
/// </summary>
public sealed class TrainerPics
{
	public const int FreeBase = 0xFE0000;

	private readonly FireRedRom _rom;

	private readonly TrainerGame _game;

	private readonly int _backTable;

	private readonly int _backPalTable;

	private readonly int _frontTable;

	private readonly int _frontPalTable;

	public TrainerPics(FireRedRom rom)
	{
		_rom = rom;
		_game = rom.Game;
		int size = _game.BackPicSize;
		byte[] sig = FireRedRom.Pack16(size, 0);
		int s = rom.Find(sig);
		int t = -1;
		while (s >= 0)
		{
			t = s - 4;
			if (t % 4 == 0 && rom.U32(t) >> 24 == 8 && rom.U16(t + 12) == size && rom.U16(t + 14) == 1)
			{
				break;
			}
			s = rom.Find(sig, s + 2);
		}
		if (s < 0)
		{
			throw new InvalidOperationException("trainer back pic table not found");
		}
		_backTable = t;
		_backPalTable = PalTableAfter(t + 6 * 8, 4);
		int frontCount;
		(_frontTable, frontCount) = FindFrontTable();
		_frontPalTable = PalTableAfter(_frontTable + frontCount * 8, 5);
	}

	private int PalTableAfter(int start, int n)
	{
		for (int o = start; o < start + 0x400; o += 4)
		{
			bool ok = true;
			for (int i = 0; i < n && ok; i++)
			{
				ok = _rom.U16(o + 8 * i + 4) == i && _rom.U32(o + 8 * i) >> 24 == 8;
			}
			if (ok)
			{
				return o;
			}
		}
		throw new InvalidOperationException("palette table not found");
	}

	private (int table, int count) FindFrontTable()
	{
		byte[] sig = FireRedRom.Pack16(0x800, 0);
		int pos = 0;
		while (true)
		{
			int o = _rom.Find(sig, pos);
			if (o < 0)
			{
				throw new InvalidOperationException("trainer front pic table not found");
			}
			int t = o - 4;
			int n = 0;
			while (true)
			{
				int a = _rom.U16(t + 8 * n + 4);
				int b = _rom.U16(t + 8 * n + 6);
				if (!((a == 0x800 || a == 0x1000) && b == n && _rom.U32(t + 8 * n) >> 24 == 8))
				{
					break;
				}
				n++;
			}
			if (n >= _game.FrontCount && n <= _game.FrontCount + 64)
			{
				return (t, n);
			}
			pos = o + 2;
		}
	}

	private int Ptr(int table, int i) => (int)(_rom.U32(table + 8 * i) - FireRedRom.Gba);

	private Rgb[] Pal(int table, int i)
	{
		byte[] raw = Lz77.Decompress(_rom.Data, Ptr(table, i));
		Rgb[] p = new Rgb[16];
		for (int k = 0; k < 16; k++)
		{
			p[k] = FireRedRom.Bgr555(raw[k * 2] | (raw[k * 2 + 1] << 8));
		}
		return p;
	}

	private (List<byte[,]>, Rgb[]) BackFrames()
	{
		int o = Ptr(_backTable, _game.BackMale);
		List<byte[,]> frames = new List<byte[,]>();
		for (int k = 0; k < _game.BackFrames; k++)
		{
			frames.Add(FireRedRom.Decode4bpp(_rom.Data, o + k * 2048, 64, 64));
		}
		return (frames, Pal(_backPalTable, _game.BackMale));
	}

	private (List<byte[,]>, Rgb[]) FrontFrames(bool red)
	{
		int id = red ? _game.FrontMale : _game.FrontFemale;
		byte[] raw = Lz77.Decompress(_rom.Data, Ptr(_frontTable, id));
		return (new List<byte[,]> { FireRedRom.Decode4bpp(raw, 0, 64, 64) }, Pal(_frontPalTable, id));
	}

	private static byte[,] StripHead(byte[,] idx, PicSpec spec, int dx, int dy)
	{
		byte[,] o = (byte[,])idx.Clone();
		int neck = spec.Neck + dy;
		for (int y = 0; y < 64; y++)
		{
			for (int x = 0; x < 64; x++)
			{
				if (y < neck && x >= spec.X0 + dx && x < spec.X1 + dx)
				{
					o[y, x] = 0;
				}
			}
		}
		if (spec.Strip != null)
		{
			foreach (var (ya, yb, xa, xb, ids) in spec.Strip)
			{
				for (int y = Math.Max(0, ya + dy); y < Math.Min(64, yb + dy); y++)
				{
					for (int x = Math.Max(0, xa + dx); x < Math.Min(64, xb + dx); x++)
					{
						if (Array.IndexOf(ids, (int)idx[y, x]) >= 0)
						{
							o[y, x] = 0;
						}
					}
				}
			}
		}
		// isolated leftovers (single pixels of the old cap/hair)
		bool[,] lone = new bool[64, 64];
		for (int y = 0; y < 64; y++)
		{
			for (int x = 0; x < 64; x++)
			{
				if (o[y, x] == 0)
				{
					continue;
				}
				int n = 0;
				for (int j = y - 1; j <= y + 1; j++)
				{
					for (int i = x - 1; i <= x + 1; i++)
					{
						if (j >= 0 && j < 64 && i >= 0 && i < 64 && o[j, i] > 0)
						{
							n++;
						}
					}
				}
				lone[y, x] = n <= 2;
			}
		}
		for (int y = 0; y < 64; y++)
		{
			for (int x = 0; x < 64; x++)
			{
				if (lone[y, x])
				{
					o[y, x] = 0;
				}
			}
		}
		return o;
	}

	private static (int, int) FindShift(byte[,] reference, byte[,] frame, PicSpec spec)
	{
		List<int> ys = new List<int>();
		List<int> xs = new List<int>();
		List<byte> v = new List<byte>();
		for (int y = 0; y < spec.Neck; y++)
		{
			for (int x = spec.X0; x < spec.X1; x++)
			{
				if (reference[y, x] != 0)
				{
					ys.Add(y);
					xs.Add(x);
					v.Add(reference[y, x]);
				}
			}
		}
		double best = -1e9;
		(int, int) shift = (0, 0);
		for (int dy = -12; dy < 13; dy++)
		{
			for (int dx = -20; dx < 13; dx++)
			{
				int same = 0;
				int empty = 0;
				for (int k = 0; k < ys.Count; k++)
				{
					int yy = ys[k] + dy;
					int xx = xs[k] + dx;
					int got = (yy >= 0 && yy < 64 && xx >= 0 && xx < 64) ? frame[yy, xx] : 0;
					if (got == v[k])
					{
						same++;
					}
					if (got == 0)
					{
						empty++;
					}
				}
				double score = same - 0.5 * empty;
				if (score > best)
				{
					best = score;
					shift = (dx, dy);
				}
			}
		}
		return shift;
	}

	private static Rgba[,] ComposePic(byte[,] idx, Rgb[] pal, PicSpec spec, int dx, int dy, HeadImage head, Rgba[,] headImg, TrainerColors look)
	{
		byte[,] body = StripHead(idx, spec, dx, dy);
		Rgba[,] img = new Rgba[64, 64];
		for (int y = 0; y < 64; y++)
		{
			for (int x = 0; x < 64; x++)
			{
				byte c = body[y, x];
				if (c == 0)
				{
					continue;
				}
				Rgb col = pal[c];
				if (spec.Roles.TryGetValue(c, out ZoneRoles zr))
				{
					var (role, tone) = zr.Zones[spec.Zone(y)];
					if (role == BodyRole.Skin)
					{
						col = look.Skin[tone];
					}
					else if (role != BodyRole.None)
					{
						col = look.Role(role, Math.Min((int)tone, 2));
					}
				}
				img[y, x] = new Rgba(col);
			}
		}
		int ox = (int)Math.Round(spec.Cx + dx - 48.0, MidpointRounding.ToEven);
		int oy = (int)Math.Round(spec.Cy + dy - 48.0, MidpointRounding.ToEven);
		int neck = spec.Neck + dy;
		for (int j = 0; j < head.H; j++)
		{
			for (int i = 0; i < head.W; i++)
			{
				if (!headImg[j, i].A)
				{
					continue;
				}
				int y = j + oy;
				int x = i + ox;
				if (y < 0 || y >= 64 || x < 0 || x >= 64)
				{
					continue;
				}
				// long hair goes behind the torso in front views
				if (spec.Yaw == 0 && img[y, x].A && y >= neck && head.Back[j, i])
				{
					continue;
				}
				img[y, x] = headImg[j, i];
			}
		}
		return img;
	}

	/// <summary>The new back frames (always Red's body: Leaf's hair covers her back) and the front picture.</summary>
	public (List<Rgba[,]> Back, List<Rgba[,]> Front) Build(HeadSet heads, TrainerColors look, bool red)
	{
		List<Rgba[,]> Make(List<byte[,]> frames, Rgb[] pal, PicSpec spec, HeadImage head)
		{
			Rgba[,] painted = head.Paint(look);
			List<Rgba[,]> imgs = new List<Rgba[,]>();
			for (int k = 0; k < frames.Count; k++)
			{
				(int dx, int dy) = (k == 0) ? (0, 0) : FindShift(frames[0], frames[k], spec);
				imgs.Add(ComposePic(frames[k], pal, spec, dx, dy, head, painted, look));
			}
			return imgs;
		}
		var (backFrames, backPal) = BackFrames();
		List<Rgba[,]> back = Make(backFrames, backPal, _game.Back, heads.Back);
		var (frontFrames, frontPal) = FrontFrames(red);
		List<Rgba[,]> front = Make(frontFrames, frontPal, red ? _game.FrontMaleSpec : _game.FrontFemaleSpec, heads.Front);
		return (back, front);
	}

	private static byte[] PalBytes(Rgb[] pal)
	{
		byte[] b = new byte[pal.Length * 2];
		for (int i = 0; i < pal.Length; i++)
		{
			int c = FireRedRom.ToBgr555(pal[i]);
			b[i * 2] = (byte)c;
			b[i * 2 + 1] = (byte)(c >> 8);
		}
		return b;
	}

	/// <summary>Frames + palette for both Red's and Leaf's slots (the same character in each).</summary>
	public void Write(bool back, List<byte[,]> indexed, Rgb[] pal)
	{
		byte[] data = _rom.Data;
		int cursor = FreeBase + (back ? 0 : 0x4000);
		int Put(byte[] blob)
		{
			int o = cursor;
			Buffer.BlockCopy(blob, 0, data, o, blob.Length);
			cursor = (o + blob.Length + 3) & ~3;
			return o;
		}
		int palOff = Put(Lz77.Compress(PalBytes(pal)));
		int picOff = 0;
		foreach (bool red in new[] { true, false })
		{
			if (back)
			{
				int id = red ? _game.BackMale : _game.BackFemale;
				int o = Ptr(_backTable, id);
				for (int k = 0; k < indexed.Count; k++)
				{
					byte[] enc = FireRedRom.Encode4bpp(indexed[k]);
					Buffer.BlockCopy(enc, 0, data, o + k * 2048, enc.Length);
				}
				_rom.Put32(_backPalTable + 8 * id, (uint)palOff + FireRedRom.Gba);
			}
			else
			{
				int id = red ? _game.FrontMale : _game.FrontFemale;
				if (red)
				{
					picOff = Put(Lz77.Compress(FireRedRom.Encode4bpp(indexed[0])));
				}
				_rom.Put32(_frontTable + 8 * id, (uint)picOff + FireRedRom.Gba);
				_rom.Put32(_frontPalTable + 8 * id, (uint)palOff + FireRedRom.Gba);
			}
		}
	}

	/// <summary>True when IsaClient's block at the end of the ROM is free padding (0xFF or 0x00).</summary>
	public static bool FreeSpace(byte[] data)
	{
		if (data.Length < FreeBase + 0x8000)
		{
			return false;
		}
		byte first = data[FreeBase];
		if (first != 0xFF && first != 0x00)
		{
			return false;
		}
		for (int i = FreeBase; i < FreeBase + 0x8000; i++)
		{
			if (data[i] != first)
			{
				return false;
			}
		}
		return true;
	}
}
