using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace IsaClient.Building;

/// <summary>A block or wall with a paint, and how it looks in the world.</summary>
internal sealed class BlockColor
{
	public bool Wall;

	public ushort Type;

	public byte Paint;

	/// <summary>Average color (0-255, straight alpha) as the game draws it, paint included.</summary>
	public Vector3 Rgb;

	/// <summary>How busy its texture is: mean OKLab distance (x100) of its pixels to its average. Plain ones are low.</summary>
	public float Noise;
}

/// <summary>
/// Every block and wall that pixel art can use, with every paint, measured from the real textures through the game's
/// own paint shader (the one painted tiles are drawn with): the colors are the ones seen in the world, not the map's.
/// Measured once, on the main thread (render target), the first time a picture needs them.
/// </summary>
internal static class BlockColors
{
	/// <summary>Paints measured: none, the 24 colors, black, white, gray, brown, shadow and negative (31 = Illuminant looks like none).</summary>
	public const int Paints = 31;

	private const int Size = 16;

	private const int RowsPerBatch = 64;

	private static List<BlockColor> _all;

	private static readonly object _lock = new object();

	public static bool IsMeasured => _all != null;

	/// <summary>
	/// Only blocks and walls with a single map color. Some change it by position (Rainbow Brick and its kin by (x + y) % 9,
	/// a wall by column): the picture would get rainbow specks where the palette expected one color.
	/// </summary>
	private static bool OneColor(bool wall, int type)
	{
		int[] counts = wall ? Terraria.Map.MapHelper.wallOptionCounts : Terraria.Map.MapHelper.tileOptionCounts;
		return counts == null || counts[type] <= 1;
	}

	private static List<BlockColor> _map;

	/// <summary>
	/// The same blocks and walls with every paint, by the color the map shows for them (flat: one color per tile, the
	/// paint as the map applies it). For pictures meant to be seen on the map. Any thread.
	/// </summary>
	public static List<BlockColor> Map
	{
		get
		{
			if (_map != null)
			{
				return _map;
			}
			List<BlockColor> list = new List<BlockColor>();
			for (int type = 0; type < TileID.Count; type++)
			{
				if (!CharacterArt.Usable(type) || !OneColor(false, type))
				{
					continue;
				}
				for (byte paint = 0; paint < Paints; paint++)
				{
					Color c = Terraria.Map.MapHelper.GetMapTileXnaColor(Terraria.Map.MapTile.Create(Terraria.Map.MapHelper.tileLookup[type], byte.MaxValue, paint), 0, 0);
					list.Add(new BlockColor
					{
						Type = (ushort)type,
						Paint = paint,
						Rgb = new Vector3(c.R, c.G, c.B)
					});
				}
			}
			for (int wall = 1; wall < WallID.Count; wall++)
			{
				if (Terraria.Map.MapHelper.wallLookup[wall] == 0 || !CharacterArt.WallUsable(wall) || !OneColor(true, wall))
				{
					continue;
				}
				for (byte paint = 0; paint < Paints; paint++)
				{
					Color c = ColorMatching.MapTileWallColor((ushort)wall, paint);
					list.Add(new BlockColor
					{
						Wall = true,
						Type = (ushort)wall,
						Paint = paint,
						Rgb = new Vector3(c.R, c.G, c.B)
					});
				}
			}
			_map = list;
			return list;
		}
	}

	/// <summary>All of them. Call from a background thread: the first call waits for the main thread to measure.</summary>
	public static List<BlockColor> All
	{
		get
		{
			if (_all != null)
			{
				return _all;
			}
			lock (_lock)
			{
				if (_all == null)
				{
					List<BlockColor> list = null;
					Exception error = null;
					Task task = Main.RunOnMainThread(delegate
					{
						try
						{
							list = Measure();
						}
						catch (Exception ex)
						{
							error = ex;
						}
					});
					if (!task.Wait(60000))
					{
						throw new TimeoutException("the game didn't measure the block colors in time");
					}
					if (error != null)
					{
						throw new InvalidOperationException("could not measure the block colors: " + error.Message, error);
					}
					_all = list;
				}
			}
			return _all;
		}
	}

	private struct Source
	{
		public bool Wall;

		public ushort Type;

		public Texture2D Texture;

		public Rectangle Frame;

		public TreePaintingSettings Settings;
	}

	private static List<Source> Sources()
	{
		List<Source> sources = new List<Source>();
		for (int type = 0; type < TileID.Count; type++)
		{
			if (!CharacterArt.Usable(type) || !OneColor(false, type))
			{
				continue;
			}
			Texture2D texture = Load(TextureAssets.Tile[type]);
			// The middle of a block surrounded by the same block.
			if (texture != null && texture.Width >= 34 && texture.Height >= 34)
			{
				sources.Add(new Source
				{
					Type = (ushort)type,
					Texture = texture,
					Frame = new Rectangle(18, 18, Size, Size),
					Settings = Terraria.GameContent.TreePaintSystemData.GetTileSettings(type, 0)
				});
			}
		}
		for (int wall = 1; wall < WallID.Count; wall++)
		{
			if (Terraria.Map.MapHelper.wallLookup[wall] == 0 || !CharacterArt.WallUsable(wall) || !OneColor(true, wall))
			{
				continue;
			}
			Texture2D texture = Load(TextureAssets.Wall[wall]);
			// The middle of a wall surrounded by walls (its 32x32 frame only has the middle 16x16 filled).
			if (texture != null && texture.Width >= 60 && texture.Height >= 60)
			{
				sources.Add(new Source
				{
					Wall = true,
					Type = (ushort)wall,
					Texture = texture,
					Frame = new Rectangle(44, 44, Size, Size),
					Settings = Terraria.GameContent.TreePaintSystemData.GetWallSettings(wall)
				});
			}
		}
		return sources;
	}

	private static Texture2D Load(Asset<Texture2D> asset)
	{
		try
		{
			return asset.IsLoaded ? asset.Value : Main.Assets.Request<Texture2D>(asset.Name, AssetRequestMode.ImmediateLoad).Value;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>Draws every source with every paint into a render target, the way the game paints tiles, and reads it back.</summary>
	private static List<BlockColor> Measure()
	{
		List<Source> sources = Sources();
		List<BlockColor> result = new List<BlockColor>();
		GraphicsDevice device = Main.instance.GraphicsDevice;
		Effect shader = Main.tileShader;
		RenderTargetBinding[] previous = device.GetRenderTargets();
		RenderTarget2D target = new RenderTarget2D(device, Paints * Size, RowsPerBatch * Size, mipMap: false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
		Color[] data = new Color[target.Width * target.Height];
		try
		{
			for (int first = 0; first < sources.Count; first += RowsPerBatch)
			{
				int count = Math.Min(RowsPerBatch, sources.Count - first);
				device.SetRenderTarget(target);
				device.Clear(Color.Transparent);
				for (int row = 0; row < count; row++)
				{
					Source source = sources[first + row];
					for (int paint = 0; paint < Paints; paint++)
					{
						Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
						source.Settings.ApplyShader(paint, shader);
						Main.spriteBatch.Draw(source.Texture, new Rectangle(paint * Size, row * Size, Size, Size), source.Frame, Color.White);
						Main.spriteBatch.End();
					}
				}
				device.SetRenderTarget(null);
				target.GetData(data);
				for (int row = 0; row < count; row++)
				{
					Source source = sources[first + row];
					for (int paint = 0; paint < Paints; paint++)
					{
						BlockColor color = Read(data, target.Width, paint * Size, row * Size);
						if (color == null)
						{
							// See-through (glass-like): not usable at all.
							if (paint == 0)
							{
								break;
							}
							continue;
						}
						color.Wall = source.Wall;
						color.Type = source.Type;
						color.Paint = (byte)paint;
						result.Add(color);
					}
				}
			}
		}
		finally
		{
			if (previous != null && previous.Length > 0)
			{
				device.SetRenderTargets(previous);
			}
			else
			{
				device.SetRenderTarget(null);
			}
			target.Dispose();
		}
		return result;
	}

	/// <summary>One 16x16 cell: its average straight color and how busy it is; null when it is not solid.</summary>
	private static BlockColor Read(Color[] data, int stride, int x0, int y0)
	{
		float r = 0f;
		float g = 0f;
		float b = 0f;
		float a = 0f;
		Vector3[] straight = new Vector3[Size * Size];
		int n = 0;
		for (int y = 0; y < Size; y++)
		{
			for (int x = 0; x < Size; x++)
			{
				Color c = data[(y0 + y) * stride + x0 + x];
				// Premultiplied: sums of color over sum of alpha give the straight average.
				r += c.R;
				g += c.G;
				b += c.B;
				a += c.A;
				float alpha = Math.Max(1, (int)c.A) / 255f;
				straight[n++] = new Vector3(Math.Min(255f, c.R / alpha), Math.Min(255f, c.G / alpha), Math.Min(255f, c.B / alpha));
			}
		}
		if (a < 0.97f * 255f * Size * Size)
		{
			return null;
		}
		Vector3 average = new Vector3(r, g, b) * (255f / a);
		Vector3 mean = ColorSpace.OkLab(average);
		float noise = 0f;
		for (int i = 0; i < n; i++)
		{
			noise += Vector3.Distance(ColorSpace.OkLab(straight[i]), mean);
		}
		return new BlockColor
		{
			Rgb = average,
			Noise = noise / n
		};
	}
}

/// <summary>Color spaces for matching: coordinates where plain (euclidean) distance is how different colors look.</summary>
internal static class ColorSpace
{
	private static readonly float[] _linear = BuildLinear();

	private static float[] BuildLinear()
	{
		float[] table = new float[256];
		for (int i = 0; i < 256; i++)
		{
			double c = i / 255.0;
			table[i] = (float)((c <= 0.04045) ? (c / 12.92) : Math.Pow((c + 0.055) / 1.055, 2.4));
		}
		return table;
	}

	private static float Linear(float c)
	{
		int i = (int)c;
		if (i <= 0)
		{
			return 0f;
		}
		if (i >= 255)
		{
			return 1f;
		}
		return _linear[i] + (_linear[i + 1] - _linear[i]) * (c - i);
	}

	/// <summary>OKLab x100 of a 0-255 sRGB color (Björn Ottosson's perceptual space).</summary>
	public static Vector3 OkLab(Vector3 rgb)
	{
		float r = Linear(rgb.X);
		float g = Linear(rgb.Y);
		float b = Linear(rgb.Z);
		double l = Math.Pow(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b, 1.0 / 3.0);
		double m = Math.Pow(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b, 1.0 / 3.0);
		double s = Math.Pow(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b, 1.0 / 3.0);
		return new Vector3((float)(0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s) * 100f, (float)(1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s) * 100f, (float)(0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s) * 100f);
	}

	/// <summary>
	/// The coordinates of a 0-255 color in a matching space: 0 OKLab (best), 1 "redmean" weighted RGB, 2 plain RGB.
	/// All are scaled so one unit is about the same visible difference.
	/// </summary>
	public static Vector3 For(int space, Vector3 rgb)
	{
		switch (space)
		{
		case 1:
		{
			float wr = (float)Math.Sqrt(2.0 + rgb.X / 256.0);
			float wb = (float)Math.Sqrt(2.0 + (255.0 - rgb.X) / 256.0);
			return new Vector3(rgb.X * wr, rgb.Y * 2f, rgb.Z * wb) * 0.12f;
		}
		case 2:
			return rgb * 0.2f;
		default:
			return OkLab(rgb);
		}
	}
}
