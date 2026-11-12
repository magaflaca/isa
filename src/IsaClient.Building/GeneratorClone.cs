using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Terraria;

namespace IsaClient.Building;

/// <summary>
/// The "Terraria Pixel Art Generator" (the exe that saves .tilemap), reproduced step by step so a picture comes out
/// block for block as it makes it:
/// <list type="bullet">
/// <item>its own data: tile colors and paint colors (tileColorInfo.bin) and its list of usable blocks
/// (validTileWallInfo.bin), embedded as they ship;</item>
/// <item>blocks only (its shader has the wall loop commented out), paints 0-12 and 25-30, its paint formula;</item>
/// <item>plain RGB distance, the first entry wins ties (tiles by id, then paints in that order);</item>
/// <item>its sizing (original size up to 2048, height from the aspect ratio) and its gaussian downsampling.</item>
/// </list>
/// </summary>
internal static class GeneratorClone
{
	private struct Entry
	{
		public ushort Tile;

		public byte Paint;

		public float R;

		public float G;

		public float B;
	}

	private static Entry[] _palette;

	private static readonly object _lock = new object();

	private static Entry[] Palette
	{
		get
		{
			lock (_lock)
			{
				return _palette ?? (_palette = Build());
			}
		}
	}

	private static Stream Resource(string name)
	{
		return Assembly.GetExecutingAssembly().GetManifestResourceStream("IsaClient.Assets." + name) ?? throw new FileNotFoundException(name);
	}

	private static Vector4[] ReadVectors(BinaryReader reader)
	{
		Vector4[] list = new Vector4[reader.ReadInt32()];
		for (int i = 0; i < list.Length; i++)
		{
			list[i] = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
		}
		return list;
	}

	private static Entry[] Build()
	{
		Vector4[] tiles;
		Vector4[] paints;
		using (BinaryReader reader = new BinaryReader(Resource("GeneratorTileColors.bin")))
		{
			tiles = ReadVectors(reader);
			ReadVectors(reader);
			paints = ReadVectors(reader);
		}
		List<int> valid = new List<int>();
		using (BinaryReader reader = new BinaryReader(Resource("GeneratorValidTiles.bin")))
		{
			int count = reader.ReadInt32();
			for (int i = 0; i < count; i++)
			{
				valid.Add(reader.ReadInt32());
			}
		}
		// It walks its "is valid" table by id: ascending.
		valid.Sort();
		List<Entry> entries = new List<Entry>();
		foreach (int type in valid)
		{
			if (type < 0 || type >= tiles.Length)
			{
				continue;
			}
			for (int paint = 0; paint < 13; paint++)
			{
				Add(entries, tiles, paints, type, paint);
			}
			for (int paint = 25; paint < 31; paint++)
			{
				Add(entries, tiles, paints, type, paint);
			}
		}
		return entries.ToArray();
	}

	/// <summary>GetTileColor of its PaletteComputeShader (LAB off).</summary>
	private static void Add(List<Entry> entries, Vector4[] tiles, Vector4[] paints, int type, int paint)
	{
		Vector4 original = tiles[type];
		if (original.W == 0f)
		{
			return;
		}
		Vector3 paintColor = new Vector3(paints[paint].X, paints[paint].Y, paints[paint].Z);
		Vector3 color = new Vector3(original.X, original.Y, original.Z);
		Vector3 copy = color;
		if (copy.Z > copy.X)
		{
			copy.X = copy.Z;
		}
		if (copy.Y > copy.X)
		{
			float temp = copy.X;
			copy.X = copy.Y;
			copy.Y = temp;
		}
		switch (paint)
		{
		case 0:
		case 31:
			break;
		case 29:
			color = paintColor * copy.Y * 0.3f;
			break;
		case 30:
			color = Vector3.One - color;
			break;
		default:
			color = paintColor * copy.X;
			break;
		}
		entries.Add(new Entry
		{
			Tile = (ushort)type,
			Paint = (byte)paint,
			R = color.X,
			G = color.Y,
			B = color.Z
		});
	}

	/// <summary>Its nearest entry: euclidean RGB in 0-1, strictly smaller wins.</summary>
	private static int Nearest(Entry[] palette, int r, int g, int b)
	{
		float cr = r / 255f;
		float cg = g / 255f;
		float cb = b / 255f;
		int best = -1;
		float bestDistance = float.PositiveInfinity;
		for (int i = 0; i < palette.Length; i++)
		{
			float dr = palette[i].R - cr;
			float dg = palette[i].G - cg;
			float db = palette[i].B - cb;
			float d = (float)Math.Sqrt(dr * dr + dg * dg + db * db);
			if (d < bestDistance)
			{
				bestDistance = d;
				best = i;
			}
		}
		return best;
	}

	/// <summary>Its size for a picture: the original (at most 2048 wide), or <paramref name="scalePercent"/> of it; height from the aspect ratio.</summary>
	public static void Size(int width, int height, float scalePercent, out int tileWidth, out int tileHeight)
	{
		tileWidth = Math.Max(1, Math.Min(2048, (scalePercent >= 100f) ? width : (int)Math.Round(width * scalePercent / 100f)));
		tileHeight = Math.Max(1, (int)((float)tileWidth * ((float)height / (float)width)));
	}

	/// <summary>Its InterpolatedSample when shrinking or keeping the size (a gaussian over the pixels each block covers).</summary>
	private static void Sample(byte[] data, int width, int height, int x, int y, int tileWidth, int tileHeight, out byte r, out byte g, out byte b, out byte a)
	{
		float sx = (float)width / (float)tileWidth;
		float sy = (float)height / (float)tileHeight;
		if (sx == 1f && sy == 1f)
		{
			int o = (y * width + x) * 4;
			b = data[o];
			g = data[o + 1];
			r = data[o + 2];
			a = data[o + 3];
			return;
		}
		int x0 = (int)Math.Floor((float)x * sx);
		int y0 = (int)Math.Floor((float)y * sy);
		int w = (int)Math.Ceiling(sx);
		int h = (int)Math.Ceiling(sy);
		float sr = 0f;
		float sg = 0f;
		float sb = 0f;
		float sa = 0f;
		float total = 0f;
		float stddev = 1.8f + ((sx + sy) / 2f / 5f - 1f);
		float variance = stddev * stddev;
		for (int i = 0; i < w; i++)
		{
			for (int j = 0; j < h; j++)
			{
				// It clamps to the size itself (one past the end); the last pixel is what it means.
				int px = Math.Min(Math.Max(x0 + i, 0), width - 1);
				int py = Math.Min(Math.Max(y0 + j, 0), height - 1);
				int o = (py * width + px) * 4;
				float dx = (float)i - (float)w / 2f;
				float dy = (float)j - (float)h / 2f;
				float weight = 1f / ((float)Math.PI * 2f * variance) * (float)Math.Pow(Math.E, -((dx * dx + dy * dy) / (2f * variance)));
				sr += data[o + 2] * weight;
				sg += data[o + 1] * weight;
				sb += data[o] * weight;
				sa += data[o + 3] * weight;
				total += weight;
			}
		}
		r = Pack(sr / total / 255f);
		g = Pack(sg / total / 255f);
		b = Pack(sb / total / 255f);
		a = Pack(sa / total / 255f);
	}

	/// <summary>ImageSharp's float to byte: times 255, plus a half, clamped.</summary>
	private static byte Pack(float v)
	{
		return (byte)MathHelper.Clamp(v * 255f + 0.5f, 0f, 255f);
	}

	/// <summary>The picture converted as the generator does it. Null when cancelled.</summary>
	public static PhotoResult Convert(string path, float scalePercent, Func<bool> cancelled)
	{
		using PixelImage image = PixelImage.Load(path, 100f, 0, sharp: false, out int sourceWidth, out int sourceHeight, out bool _);
		if (image == null)
		{
			return null;
		}
		Size(image.Width, image.Height, scalePercent, out int tileWidth, out int tileHeight);
		Entry[] palette = Palette;
		byte[] data = image.Data;
		Color[] sampled = new Color[tileWidth * tileHeight];
		int[] picks = new int[tileWidth * tileHeight];
		// Each color once: its palette is a full RGB table, so the same color always gives the same block.
		Dictionary<int, int> known = new Dictionary<int, int>();
		HashSet<int> wanted = new HashSet<int>();
		ParallelLoopResult loop = Parallel.For(0, tileHeight, delegate(int y, ParallelLoopState state)
		{
			if (cancelled())
			{
				state.Stop();
				return;
			}
			for (int x = 0; x < tileWidth; x++)
			{
				Sample(data, image.Width, image.Height, x, y, tileWidth, tileHeight, out byte r, out byte g, out byte b, out byte a);
				sampled[y * tileWidth + x] = new Color(r, g, b, a);
			}
		});
		if (!loop.IsCompleted || cancelled())
		{
			return null;
		}
		foreach (Color c in sampled)
		{
			if (c.A != 0)
			{
				wanted.Add((c.R << 16) | (c.G << 8) | c.B);
			}
		}
		int[] keys = new int[wanted.Count];
		wanted.CopyTo(keys);
		int[] values = new int[keys.Length];
		loop = Parallel.For(0, keys.Length, delegate(int i, ParallelLoopState state)
		{
			if ((i & 1023) == 0 && cancelled())
			{
				state.Stop();
				return;
			}
			values[i] = Nearest(palette, keys[i] >> 16, (keys[i] >> 8) & 0xFF, keys[i] & 0xFF);
		});
		if (!loop.IsCompleted || cancelled())
		{
			return null;
		}
		for (int i = 0; i < keys.Length; i++)
		{
			known[keys[i]] = values[i];
		}
		for (int i = 0; i < sampled.Length; i++)
		{
			Color c = sampled[i];
			picks[i] = (c.A == 0) ? -1 : known[(c.R << 16) | (c.G << 8) | c.B];
		}
		// Clipboard: column by column, one shared tile per block and paint.
		TileCopy empty = new TileCopy();
		TileCopy[] tiles = new TileCopy[palette.Length];
		List<TileCopy> list = new List<TileCopy>(tileWidth * tileHeight);
		HashSet<int> kinds = new HashSet<int>();
		HashSet<int> paints = new HashSet<int>();
		int blocks = 0;
		for (int x = 0; x < tileWidth; x++)
		{
			for (int y = 0; y < tileHeight; y++)
			{
				int pick = picks[y * tileWidth + x];
				if (pick < 0)
				{
					list.Add(empty);
					continue;
				}
				TileCopy tile = tiles[pick];
				if (tile == null)
				{
					tile = new TileCopy();
					tile.active(active: true);
					tile.type = palette[pick].Tile;
					tile.color(palette[pick].Paint);
					tile.frameX = 18;
					tile.frameY = 18;
					tiles[pick] = tile;
					kinds.Add(palette[pick].Tile);
					if (palette[pick].Paint != 0)
					{
						paints.Add(palette[pick].Paint);
					}
				}
				list.Add(tile);
				blocks++;
			}
		}
		// Small pictures for the panel.
		float shrink = Math.Min(1f, 640f / Math.Max(tileWidth, tileHeight));
		int pw = Math.Max(1, (int)(tileWidth * shrink));
		int ph = Math.Max(1, (int)(tileHeight * shrink));
		Color[] before = new Color[pw * ph];
		Color[] after = new Color[pw * ph];
		for (int y = 0; y < ph; y++)
		{
			int sy = Math.Min(tileHeight - 1, (int)(y / shrink));
			for (int x = 0; x < pw; x++)
			{
				int i = sy * tileWidth + Math.Min(tileWidth - 1, (int)(x / shrink));
				before[y * pw + x] = (sampled[i].A == 0) ? Color.Transparent : new Color(sampled[i].R, sampled[i].G, sampled[i].B);
				after[y * pw + x] = (picks[i] < 0) ? Color.Transparent : new Color(palette[picks[i]].R, palette[picks[i]].G, palette[picks[i]].B);
			}
		}
		return new PhotoResult
		{
			Width = tileWidth,
			Height = tileHeight,
			SourceWidth = sourceWidth,
			SourceHeight = sourceHeight,
			Fitted = tileWidth < sourceWidth && scalePercent >= 100f,
			Tiles = list,
			Before = before,
			After = after,
			PreviewWidth = pw,
			PreviewHeight = ph,
			Blocks = blocks,
			Kinds = kinds.Count,
			Paints = paints.Count
		};
	}
}
