using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Utilities.FileBrowser;

namespace IsaClient.Building;

/// <summary>A picture turned into blocks: the paste clipboard for it, and small before/after pictures for the panel.</summary>
internal sealed class PhotoResult
{
	public string Path;

	public string Name;

	/// <summary>Size in blocks.</summary>
	public int Width;

	public int Height;

	public int SourceWidth;

	public int SourceHeight;

	public bool Fitted;

	public List<TileCopy> Tiles;

	/// <summary>The picture after size and filters (what is matched), and how the blocks look; both PreviewWidth x PreviewHeight.</summary>
	public Color[] Before;

	public Color[] After;

	public int PreviewWidth;

	public int PreviewHeight;

	public int Blocks;

	public int Walls;

	public int Kinds;

	public int Paints;

	public double Seconds;

	/// <summary>Changes on every new result (the panel remakes its pictures).</summary>
	public int Version;
}

/// <summary>
/// .drawpic and Building > Picture to Blocks: a picture becomes blocks and walls ready to paste.
/// <list type="bullet">
/// <item>Any format Windows reads (GDI+, then Windows' codecs: WebP, HEIC, AVIF...); bigger than
/// <see cref="PixelArt.DrawPhotoMaxSize"/> is fitted, not refused.</item>
/// <item>Colors are the real ones of every block and wall with every paint (<see cref="BlockColors"/>), matched in a
/// perceptual space with a little preference for plain textures, optionally dithered. Chosen with an automatic test
/// bench that renders the result with the game's textures and scores it against the picture.</item>
/// <item>Atomic: built aside and swapped in on the main thread at the end; a newer picture (or a schematic)
/// replaces one still loading instead of waiting for it.</item>
/// </list>
/// </summary>
internal static class PhotoImport
{
	/// <summary>
	/// Where the picture should look right: 0 on the map (flat map colors), 1 in the world (textures, lit), 2 exactly as
	/// the Terraria Pixel Art Generator makes it (<see cref="GeneratorClone"/>).
	/// </summary>
	[Setting("DrawPhotoTarget", Min = 0, Max = 2)]
	public static int Target = 0;

	[Setting("DrawPhotoMatch", Min = 0, Max = 2)]
	public static int Match = 0;

	/// <summary>
	/// Error diffusion strength. The map has no pastel or mint colors at all (paint on the map only saturates), so they
	/// are made by mixing neighbors; 75% scored best seen zoomed out (half the error of none).
	/// </summary>
	[Setting("DrawPhotoDithering", Min = 0, Max = 100)]
	public static int Dither = 0;

	/// <summary>The options that scored best on the map (paste test bench): map colors, walls and blocks, OKLab, 75% dithering.</summary>
	public static void Recommended()
	{
		Target = 0;
		PixelArt.DrawPhotoType = 2;
		Match = 0;
		Dither = 0;
		Plain = 25;
		UsePaint = true;
		OddPaints = true;
		Brightness = 0;
		Contrast = 0;
		Saturation = 100;
		AlphaCut = 30;
	}

	[Setting("DrawPhotoPlain", Min = 0, Max = 100)]
	public static int Plain = 25;

	[Setting("DrawPhotoPaint")]
	public static bool UsePaint = true;

	[Setting("DrawPhotoOddPaints")]
	public static bool OddPaints = true;

	[Setting("DrawPhotoGlow")]
	public static bool Glow = false;

	[Setting("DrawPhotoBrightness", Min = -100, Max = 100)]
	public static int Brightness = 0;

	[Setting("DrawPhotoContrast", Min = -100, Max = 100)]
	public static int Contrast = 0;

	[Setting("DrawPhotoSaturation", Min = 0, Max = 200)]
	public static int Saturation = 100;

	[Setting("DrawPhotoAlphaCut", Min = 0, Max = 255)]
	public static int AlphaCut = 30;

	[Setting("DrawPhotoSharpResize")]
	public static bool SharpResize = false;

	[Setting("DrawPhotoFlip")]
	public static bool Flip = false;

	private const int PreviewSide = 640;

	private static readonly ExtensionFilter[] Filters = new ExtensionFilter[]
	{
		new ExtensionFilter("Images", "png", "jpg", "jpeg", "jfif", "webp", "bmp", "gif", "tif", "tiff", "ico", "heic", "heif", "avif", "jxr"),
		new ExtensionFilter("PNG Files", "png"),
		new ExtensionFilter("JPEG Files", "jpg", "jpeg", "jfif"),
		new ExtensionFilter("WEBP Files", "webp"),
		new ExtensionFilter("BMP Files", "bmp"),
		new ExtensionFilter("GIF Files", "gif"),
		new ExtensionFilter("All Files", "*")
	};

	/// <summary>Extensions taken as pictures (drag and drop).</summary>
	internal static readonly string[] Extensions = new string[]
	{
		".png", ".jpg", ".jpeg", ".jfif", ".webp", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".heic", ".heif", ".avif", ".jxr"
	};

	/// <summary>The newest load: an older one that sees another number stops and drops its result.</summary>
	private static int _job;

	private static int _choosing;

	private static int _running;

	private static int _version;

	/// <summary>The last picture made (the panel shows it; changing an option remakes it).</summary>
	public static PhotoResult Last;

	/// <summary>What is being done right now, for the panel; null when idle.</summary>
	public static string Status;

	public static bool Busy => _running > 0;

	/// <summary>Drops a picture still loading (a schematic is being opened instead).</summary>
	public static void Cancel()
	{
		Interlocked.Increment(ref _job);
	}

	/// <summary>Asks for a picture (or takes <paramref name="path"/>), turns it into blocks and arms the paste.</summary>
	public static void Start(string path)
	{
		if (Selection.OpeningTEDITSchematic)
		{
			ClientChat.Error("Already loading a schematic, please wait for the operation to finish.");
			return;
		}
		if (path == null && Interlocked.Exchange(ref _choosing, 1) == 1)
		{
			ClientChat.Info("Pick the picture in the window that is already open.");
			return;
		}
		Task.Run(delegate
		{
			if (path == null)
			{
				try
				{
					ClientChat.Info("Select an image file...");
					path = FileBrowser.OpenFilePanel("Select Photo To Print", Filters);
				}
				finally
				{
					_choosing = 0;
				}
			}
			if (!string.IsNullOrEmpty(path))
			{
				Run(path, arm: true, quiet: false);
			}
		});
	}

	/// <summary>
	/// Makes the last picture again with the options as they are now. The paste is updated too when it still holds that
	/// picture (or when <paramref name="arm"/>).
	/// </summary>
	public static void Refresh(bool arm)
	{
		PhotoResult last = Last;
		if (last == null || Selection.OpeningTEDITSchematic)
		{
			return;
		}
		bool ours = Selection.CopiedTiles == last.Tiles;
		Task.Run(delegate
		{
			Run(last.Path, arm || ours, quiet: true);
		});
	}

	/// <summary>Arms the paste with the last picture as it is.</summary>
	public static void Arm()
	{
		PhotoResult last = Last;
		if (last != null && !Selection.OpeningTEDITSchematic)
		{
			ArmPaste(last);
			ClientChat.Info(IsaClient.Languages.L10n.F("{0} ready to paste ({1}x{2} blocks). Left click in the world to place it; Ctrl+Z undoes it.", last.Name, last.Width, last.Height));
		}
	}

	private static void Run(string path, bool arm, bool quiet)
	{
		int job = Interlocked.Increment(ref _job);
		Interlocked.Increment(ref _running);
		string name = Path.GetFileName(path);
		try
		{
			Stopwatch watch = Stopwatch.StartNew();
			Status = IsaClient.Languages.L10n.F("Loading {0}...", name);
			if (!quiet)
			{
				ClientChat.Info("Loading " + name + "...");
			}
			bool unreadable = false;
			PhotoResult result = (Target == 2) ? GeneratorClone.Convert(path, PixelArt.DrawPhotoScale, () => job != _job) : Make(path, name, job, quiet, out unreadable);
			if (job != _job)
			{
				return;
			}
			if (result == null)
			{
				if (unreadable || Target == 2)
				{
					ClientChat.Error(name + " is not a picture Windows can read.");
				}
				return;
			}
			result.Path = path;
			result.Name = name;
			result.Seconds = watch.Elapsed.TotalSeconds;
			result.Version = Interlocked.Increment(ref _version);
			Main.QueueMainThreadAction(delegate
			{
				if (job != _job || Selection.OpeningTEDITSchematic)
				{
					return;
				}
				Last = result;
				if (arm)
				{
					ArmPaste(result);
				}
				if (!quiet)
				{
					ClientChat.Info("Selected: " + path + "  |  Size: [X: " + result.Width + ", Y: " + result.Height + "]  (" + result.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s)");
				}
			});
		}
		catch (Exception ex)
		{
			if (job == _job)
			{
				ClientChat.Error("Could not load " + name + ": " + ex.Message);
				ClientLog.LogException(ex);
			}
		}
		finally
		{
			Interlocked.Decrement(ref _running);
			if (_running == 0)
			{
				Status = null;
			}
		}
	}

	/// <summary>The client's own conversion (map or world colors, options of the panel). Null when cancelled or unreadable.</summary>
	private static PhotoResult Make(string path, string name, int job, bool quiet, out bool unreadable)
	{
		unreadable = false;
		int maxSide = PixelArt.DrawPhotoMaxSize;
		using PixelImage image = PixelImage.Load(path, PixelArt.DrawPhotoScale, maxSide, SharpResize, out int sourceWidth, out int sourceHeight, out bool fitted);
		if (job != _job)
		{
			return null;
		}
		if (image == null)
		{
			unreadable = true;
			return null;
		}
		if (fitted && !quiet)
		{
			ClientChat.Info(name + " is " + sourceWidth + "x" + sourceHeight + ": fitted to " + image.Width + "x" + image.Height + " (at most " + maxSide + " blocks per side, change it with .picscale max <blocks>).");
		}
		Adjust(image);
		Status = (Target == 0 || BlockColors.IsMeasured ? "" : IsaClient.Languages.L10n.T("Measuring block colors (only the first time)...") + " ") + IsaClient.Languages.L10n.T("Matching colors...");
		PhotoPalette palette = PhotoPalette.Current();
		int[] picks = Pick(image, palette, () => job != _job);
		if (picks == null || job != _job)
		{
			return null;
		}
		PhotoResult result = Build(image, palette, picks);
		result.SourceWidth = sourceWidth;
		result.SourceHeight = sourceHeight;
		result.Fitted = fitted;
		return result;
	}

	private static void ArmPaste(PhotoResult result)
	{
		BrushState.DisableBrushes();
		Selection.SelectionMode = false;
		Selection.CopyMode = false;
		Selection.PasteMode = false;
		PixelArt.DrawPhotoMode = false;
		PixelArt.DrawPhotoWidth = result.Width;
		PixelArt.DrawPhotoHeight = result.Height;
		Selection.CopiedTiles = result.Tiles;
		Selection.SchematicSizeX = result.Width;
		Selection.SchematicSizeY = result.Height;
		Selection.PasteMode = true;
		Selection.PasteDelayCounter = Selection.PasteDelay;
	}

	/// <summary>Mirror, brightness, contrast and saturation, on the picture's BGRA bytes.</summary>
	private static void Adjust(PixelImage image)
	{
		byte[] data = image.Data;
		int width = image.Width;
		if (Flip)
		{
			for (int y = 0; y < image.Height; y++)
			{
				int row = y * width * 4;
				for (int x = 0; x < width / 2; x++)
				{
					int a = row + x * 4;
					int b = row + (width - 1 - x) * 4;
					for (int k = 0; k < 4; k++)
					{
						byte t = data[a + k];
						data[a + k] = data[b + k];
						data[b + k] = t;
					}
				}
			}
		}
		if (Brightness == 0 && Contrast == 0 && Saturation == 100)
		{
			return;
		}
		float bright = Brightness * 2.55f;
		float c = Contrast * 2.55f;
		float contrast = 259f * (c + 255f) / (255f * (259f - c));
		float saturation = Saturation / 100f;
		Parallel.For(0, image.Height, delegate(int y)
		{
			int end = (y + 1) * width * 4;
			for (int o = y * width * 4; o < end; o += 4)
			{
				float b = (data[o] - 128f) * contrast + 128f + bright;
				float g = (data[o + 1] - 128f) * contrast + 128f + bright;
				float r = (data[o + 2] - 128f) * contrast + 128f + bright;
				float luma = 0.299f * r + 0.587f * g + 0.114f * b;
				data[o] = (byte)MathHelper.Clamp(luma + (b - luma) * saturation, 0f, 255f);
				data[o + 1] = (byte)MathHelper.Clamp(luma + (g - luma) * saturation, 0f, 255f);
				data[o + 2] = (byte)MathHelper.Clamp(luma + (r - luma) * saturation, 0f, 255f);
			}
		});
	}

	/// <summary>The palette entry of every pixel, row by row (-1: see-through). Null when cancelled.</summary>
	private static int[] Pick(PixelImage image, PhotoPalette palette, Func<bool> cancelled)
	{
		int width = image.Width;
		int height = image.Height;
		byte[] data = image.Data;
		int cut = AlphaCut;
		int[] picks = new int[width * height];
		if (Dither <= 0)
		{
			ParallelLoopResult result = Parallel.For(0, height, delegate(int y, ParallelLoopState state)
			{
				if (cancelled())
				{
					state.Stop();
					return;
				}
				for (int x = 0; x < width; x++)
				{
					int i = y * width + x;
					int o = i * 4;
					picks[i] = (data[o + 3] < cut || data[o + 3] == 0) ? -1 : palette.Nearest(data[o + 2], data[o + 1], data[o]);
				}
			});
			return result.IsCompleted ? picks : null;
		}
		// Floyd-Steinberg, back and forth: the error of each block is spread to the pixels not done yet.
		float strength = Dither / 100f;
		Vector3[] here = new Vector3[width + 2];
		Vector3[] next = new Vector3[width + 2];
		for (int y = 0; y < height; y++)
		{
			if (cancelled())
			{
				return null;
			}
			Array.Clear(next, 0, next.Length);
			bool left = (y & 1) == 0;
			for (int step = 0; step < width; step++)
			{
				int x = left ? step : (width - 1 - step);
				int i = y * width + x;
				int o = i * 4;
				if (data[o + 3] < cut || data[o + 3] == 0)
				{
					picks[i] = -1;
					continue;
				}
				Vector3 want = new Vector3(data[o + 2], data[o + 1], data[o]) + here[x + 1];
				want = Vector3.Clamp(want, Vector3.Zero, new Vector3(255f));
				int pick = palette.Nearest((int)want.X, (int)want.Y, (int)want.Z);
				picks[i] = pick;
				Vector3 error = (want - palette.Rgb[pick]) * strength;
				int ahead = left ? 1 : -1;
				here[x + 1 + ahead] += error * (7f / 16f);
				next[x + 1 - ahead] += error * (3f / 16f);
				next[x + 1] += error * (5f / 16f);
				next[x + 1 + ahead] += error * (1f / 16f);
			}
			Vector3[] swap = here;
			here = next;
			next = swap;
		}
		return picks;
	}

	private static PhotoResult Build(PixelImage image, PhotoPalette palette, int[] picks)
	{
		int width = image.Width;
		int height = image.Height;
		PhotoResult result = new PhotoResult
		{
			Width = width,
			Height = height
		};
		// The paste clipboard is column by column. One shared tile per entry: the paste copies each one.
		TileCopy empty = new TileCopy();
		List<TileCopy> tiles = new List<TileCopy>(width * height);
		bool[] used = new bool[palette.Count];
		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				int pick = picks[y * width + x];
				if (pick < 0)
				{
					tiles.Add(empty);
					continue;
				}
				tiles.Add(palette.Tiles[pick]);
				used[pick] = true;
				if (palette.Entries[pick].Wall)
				{
					result.Walls++;
				}
				else
				{
					result.Blocks++;
				}
			}
		}
		result.Tiles = tiles;
		HashSet<int> kinds = new HashSet<int>();
		HashSet<int> paints = new HashSet<int>();
		for (int i = 0; i < used.Length; i++)
		{
			if (used[i])
			{
				BlockColor e = palette.Entries[i];
				kinds.Add((e.Wall ? 100000 : 0) + e.Type);
				if (e.Paint != 0)
				{
					paints.Add(e.Paint);
				}
			}
		}
		result.Kinds = kinds.Count;
		result.Paints = paints.Count;
		// Small pictures for the panel (every n-th pixel).
		float shrink = Math.Min(1f, (float)PreviewSide / Math.Max(width, height));
		int pw = Math.Max(1, (int)(width * shrink));
		int ph = Math.Max(1, (int)(height * shrink));
		Color[] before = new Color[pw * ph];
		Color[] after = new Color[pw * ph];
		byte[] data = image.Data;
		for (int y = 0; y < ph; y++)
		{
			int sy = Math.Min(height - 1, (int)(y / shrink));
			for (int x = 0; x < pw; x++)
			{
				int sx = Math.Min(width - 1, (int)(x / shrink));
				int i = sy * width + sx;
				int o = i * 4;
				before[y * pw + x] = (data[o + 3] < AlphaCut) ? Color.Transparent : new Color(data[o + 2], data[o + 1], data[o]);
				int pick = picks[i];
				after[y * pw + x] = (pick < 0) ? Color.Transparent : new Color(palette.Rgb[pick] / 255f);
			}
		}
		result.Before = before;
		result.After = after;
		result.PreviewWidth = pw;
		result.PreviewHeight = ph;
		return result;
	}
}

/// <summary>
/// The blocks and walls a picture can be made of with the current options, and the closest one to any color: searched
/// along the first coordinate (sorted) and cached in a 64x64x64 table, so each color is looked up once.
/// </summary>
internal sealed class PhotoPalette
{
	private const int CellBits = 6;

	private static PhotoPalette _current;

	private static string _currentKey;

	public readonly BlockColor[] Entries;

	public readonly TileCopy[] Tiles;

	public readonly Vector3[] Rgb;

	private readonly Vector3[] _coords;

	private readonly float[] _xs;

	private readonly float[] _penalty;

	private readonly int _space;

	/// <summary>Best entry + 1 per table cell; 0 = not looked up yet. Int writes are atomic: threads fill it together.</summary>
	private readonly int[] _cells = new int[1 << (CellBits * 3)];

	public int Count => Entries.Length;

	private PhotoPalette(List<BlockColor> entries, int space, float plain, bool glow)
	{
		_space = space;
		Vector3[] coords = new Vector3[entries.Count];
		for (int i = 0; i < coords.Length; i++)
		{
			coords[i] = ColorSpace.For(space, entries[i].Rgb);
		}
		// Sorted along the first coordinate: the search stops once that alone is farther than the best found.
		int[] order = new int[entries.Count];
		float[] keys = new float[entries.Count];
		for (int i = 0; i < order.Length; i++)
		{
			order[i] = i;
			keys[i] = coords[i].X;
		}
		Array.Sort(keys, order);
		Entries = new BlockColor[order.Length];
		Tiles = new TileCopy[order.Length];
		Rgb = new Vector3[order.Length];
		_coords = new Vector3[order.Length];
		_xs = keys;
		_penalty = new float[order.Length];
		for (int i = 0; i < order.Length; i++)
		{
			BlockColor e = entries[order[i]];
			Entries[i] = e;
			Rgb[i] = e.Rgb;
			_coords[i] = coords[order[i]];
			_penalty[i] = plain * e.Noise;
			TileCopy tile = new TileCopy();
			if (e.Wall)
			{
				tile.wall = e.Type;
				tile.wallColor(e.Paint);
				if (glow)
				{
					tile.fullbrightWall(fullbrightWall: true);
				}
			}
			else
			{
				tile.active(active: true);
				tile.type = e.Type;
				tile.color(e.Paint);
				tile.frameX = 18;
				tile.frameY = 18;
				if (glow)
				{
					tile.fullbrightBlock(fullbrightBlock: true);
				}
			}
			Tiles[i] = tile;
		}
	}

	/// <summary>The palette for the options as they are now (made again only when they change).</summary>
	public static PhotoPalette Current()
	{
		int mode = Math.Max(0, Math.Min(2, PixelArt.DrawPhotoType));
		string key = mode + "|" + PhotoImport.Target + "|" + PhotoImport.Match + "|" + PhotoImport.Plain + "|" + PhotoImport.UsePaint + "|" + PhotoImport.OddPaints + "|" + PhotoImport.Glow;
		PhotoPalette palette = _current;
		if (palette != null && key == _currentKey)
		{
			return palette;
		}
		Dictionary<int, BlockColor> unique = new Dictionary<int, BlockColor>();
		foreach (BlockColor e in (PhotoImport.Target == 0) ? BlockColors.Map : BlockColors.All)
		{
			if ((mode == 0 && e.Wall) || (mode == 1 && !e.Wall))
			{
				continue;
			}
			if ((!PhotoImport.UsePaint && e.Paint != 0) || (!PhotoImport.OddPaints && (e.Paint == 29 || e.Paint == 30)))
			{
				continue;
			}
			// Same color twice: the plainer texture stays (then the one without paint, then walls before blocks).
			int rgb = ((int)Math.Round(e.Rgb.X) << 16) | ((int)Math.Round(e.Rgb.Y) << 8) | (int)Math.Round(e.Rgb.Z);
			if (!unique.TryGetValue(rgb, out BlockColor had) || e.Noise < had.Noise - 0.01f || (Math.Abs(e.Noise - had.Noise) <= 0.01f && e.Paint < had.Paint))
			{
				unique[rgb] = e;
			}
		}
		if (unique.Count == 0)
		{
			throw new InvalidOperationException("no block or wall can be used with these options");
		}
		palette = new PhotoPalette(new List<BlockColor>(unique.Values), PhotoImport.Match, PhotoImport.Plain / 50f, PhotoImport.Glow);
		_current = palette;
		_currentKey = key;
		return palette;
	}

	/// <summary>The entry that looks closest to this color, counting busy textures a little farther.</summary>
	public int Nearest(int r, int g, int b)
	{
		const int shift = 8 - CellBits;
		int key = ((r >> shift) << (CellBits * 2)) | ((g >> shift) << CellBits) | (b >> shift);
		int hit = _cells[key];
		if (hit != 0)
		{
			return hit - 1;
		}
		// The middle of the cell stands for all of it.
		const int half = 1 << (shift - 1);
		Vector3 want = ColorSpace.For(_space, new Vector3(((r >> shift) << shift) + half, ((g >> shift) << shift) + half, ((b >> shift) << shift) + half));
		int start = Array.BinarySearch(_xs, want.X);
		if (start < 0)
		{
			start = ~start;
		}
		int best = Math.Min(start, _xs.Length - 1);
		float bestCost = float.MaxValue;
		for (int i = start; i < _xs.Length; i++)
		{
			if (_xs[i] - want.X >= bestCost)
			{
				break;
			}
			float cost = Vector3.Distance(want, _coords[i]) + _penalty[i];
			if (cost < bestCost)
			{
				bestCost = cost;
				best = i;
			}
		}
		for (int i = start - 1; i >= 0; i--)
		{
			if (want.X - _xs[i] >= bestCost)
			{
				break;
			}
			float cost = Vector3.Distance(want, _coords[i]) + _penalty[i];
			if (cost < bestCost)
			{
				bestCost = cost;
				best = i;
			}
		}
		_cells[key] = best + 1;
		return best;
	}
}
