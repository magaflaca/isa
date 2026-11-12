using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using IsaClient.Core;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace IsaClient.ChatImages;

/// <summary>
/// One picture ready to draw at one size (a chat thumbnail, the viewer): decoded by GDI+ and shrunk smoothly. An
/// animated GIF stays encoded: while it is drawn, the next frame is decoded on a background thread and put in the same
/// texture, so a long GIF costs one frame of memory.
/// </summary>
internal sealed class ImageFrames : IDisposable
{
	private readonly object _sync = new object();

	private MemoryStream _stream;

	private Image _image;

	private int[] _delays;

	private Texture2D _texture;

	private XnaColor[] _ready;

	private int _readyIndex = -1;

	private int _shown = -1;

	private bool _decoding;

	private bool _disposed;

	private readonly Stopwatch _clock = new Stopwatch();

	private long _frameStart;

	/// <summary>Size of the decoded frames.</summary>
	public int Width { get; private set; }

	public int Height { get; private set; }

	/// <summary>Size of the picture itself.</summary>
	public int SourceWidth { get; private set; }

	public int SourceHeight { get; private set; }

	public int FrameCount { get; private set; } = 1;

	public bool Animated => FrameCount > 1;

	/// <summary>The size of a picture without decoding it (GDI+ reads the header). Throws when it is not a picture.</summary>
	public static Size Measure(byte[] data, out bool animated)
	{
		using MemoryStream stream = new MemoryStream(data);
		using Image image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
		animated = FrameCountOf(image) > 1;
		Size size = image.Size;
		return Turned(image) ? new Size(size.Height, size.Width) : size;
	}

	/// <summary>
	/// Decodes the first frame to fit in <paramref name="maxWidth"/> x <paramref name="maxHeight"/> (never bigger than
	/// the picture). Slow: off the main thread. Throws when GDI+ can't read it.
	/// </summary>
	public static ImageFrames Open(byte[] data, int maxWidth, int maxHeight, bool animate)
	{
		ImageFrames frames = new ImageFrames();
		frames._stream = new MemoryStream(data);
		try
		{
			frames._image = Image.FromStream(frames._stream, useEmbeddedColorManagement: false, validateImageData: false);
			int count = animate ? FrameCountOf(frames._image) : 1;
			if (count <= 1)
			{
				// A photo taken sideways stands up (EXIF orientation).
				ApplyOrientation(frames._image);
			}
			frames.SourceWidth = frames._image.Width;
			frames.SourceHeight = frames._image.Height;
			double scale = Math.Min(1.0, Math.Min((double)maxWidth / frames.SourceWidth, (double)maxHeight / frames.SourceHeight));
			frames.Width = Math.Max(1, (int)Math.Round(frames.SourceWidth * scale));
			frames.Height = Math.Max(1, (int)Math.Round(frames.SourceHeight * scale));
			frames.FrameCount = count;
			if (count > 1)
			{
				frames._delays = Delays(frames._image, count);
			}
			frames._ready = frames.Decode(0);
			frames._readyIndex = 0;
			if (count <= 1)
			{
				// Nothing more to decode: the picture and its bytes go.
				frames.ReleaseImage();
			}
			return frames;
		}
		catch
		{
			frames.ReleaseImage();
			throw;
		}
	}

	private static int FrameCountOf(Image image)
	{
		try
		{
			foreach (Guid guid in image.FrameDimensionsList)
			{
				if (guid == FrameDimension.Time.Guid)
				{
					return image.GetFrameCount(FrameDimension.Time);
				}
			}
		}
		catch
		{
		}
		return 1;
	}

	/// <summary>Frame times in ms (GIF property 0x5100, hundredths of a second); too short means 100 ms, like browsers.</summary>
	private static int[] Delays(Image image, int count)
	{
		int[] delays = new int[count];
		byte[] values = null;
		try
		{
			values = image.GetPropertyItem(0x5100)?.Value;
		}
		catch
		{
		}
		for (int i = 0; i < count; i++)
		{
			int hundredths = (values != null && values.Length >= (i + 1) * 4) ? BitConverter.ToInt32(values, i * 4) : 10;
			delays[i] = (hundredths <= 1) ? 100 : (hundredths * 10);
		}
		return delays;
	}

	private static bool Turned(Image image)
	{
		int orientation = Orientation(image);
		return orientation >= 5 && orientation <= 8;
	}

	private static int Orientation(Image image)
	{
		try
		{
			foreach (int id in image.PropertyIdList)
			{
				if (id == 0x0112)
				{
					return BitConverter.ToUInt16(image.GetPropertyItem(0x0112).Value, 0);
				}
			}
		}
		catch
		{
		}
		return 1;
	}

	private static void ApplyOrientation(Image image)
	{
		RotateFlipType turn;
		switch (Orientation(image))
		{
		case 2:
			turn = RotateFlipType.RotateNoneFlipX;
			break;
		case 3:
			turn = RotateFlipType.Rotate180FlipNone;
			break;
		case 4:
			turn = RotateFlipType.Rotate180FlipX;
			break;
		case 5:
			turn = RotateFlipType.Rotate90FlipX;
			break;
		case 6:
			turn = RotateFlipType.Rotate90FlipNone;
			break;
		case 7:
			turn = RotateFlipType.Rotate270FlipX;
			break;
		case 8:
			turn = RotateFlipType.Rotate270FlipNone;
			break;
		default:
			return;
		}
		image.RotateFlip(turn);
	}

	/// <summary>One frame at the decoded size, as premultiplied colors for XNA.</summary>
	private unsafe XnaColor[] Decode(int index)
	{
		lock (_sync)
		{
			if (_image == null)
			{
				return null;
			}
			if (FrameCount > 1)
			{
				_image.SelectActiveFrame(FrameDimension.Time, index);
			}
			using Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.CompositingMode = CompositingMode.SourceCopy;
				graphics.InterpolationMode = (Width < SourceWidth) ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
				graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
				using ImageAttributes attributes = new ImageAttributes();
				// No dark seams at the edges.
				attributes.SetWrapMode(WrapMode.TileFlipXY);
				graphics.DrawImage(_image, new Rectangle(0, 0, Width, Height), 0, 0, SourceWidth, SourceHeight, GraphicsUnit.Pixel, attributes);
			}
			BitmapData locked = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			try
			{
				XnaColor[] pixels = new XnaColor[Width * Height];
				for (int y = 0; y < Height; y++)
				{
					byte* row = (byte*)locked.Scan0 + (long)y * locked.Stride;
					for (int x = 0; x < Width; x++)
					{
						byte* p = row + x * 4;
						int a = p[3];
						pixels[y * Width + x] = (a == 255) ? new XnaColor(p[2], p[1], p[0], (byte)255) : new XnaColor(p[2] * a / 255, p[1] * a / 255, p[0] * a / 255, a);
					}
				}
				return pixels;
			}
			finally
			{
				bitmap.UnlockBits(locked);
			}
		}
	}

	/// <summary>Main thread, each time it is drawn: the texture (null until the first frame is ready), animated when <paramref name="animate"/>.</summary>
	public Texture2D Update(bool animate)
	{
		if (_disposed)
		{
			return null;
		}
		lock (_sync)
		{
			if (_ready != null)
			{
				bool due = _shown < 0 || !_clock.IsRunning || _clock.ElapsedMilliseconds - _frameStart >= _delays[_shown];
				if (due)
				{
					if (_texture == null || _texture.IsDisposed)
					{
						_texture = new Texture2D(Main.instance.GraphicsDevice, Width, Height, mipMap: false, SurfaceFormat.Color);
					}
					_texture.SetData(_ready);
					long now = _clock.ElapsedMilliseconds;
					// Keeps time without drifting, but starts over after a pause (not drawn, game hitch).
					_frameStart = (_shown < 0 || now - _frameStart > 2 * _delays[_shown]) ? now : (_frameStart + _delays[_shown]);
					_shown = _readyIndex;
					_ready = null;
					_readyIndex = -1;
					if (!_clock.IsRunning)
					{
						_clock.Start();
					}
				}
			}
		}
		if (Animated && animate && _shown >= 0)
		{
			RequestDecode((_shown + 1) % FrameCount);
		}
		return _texture;
	}

	private void RequestDecode(int index)
	{
		lock (_sync)
		{
			if (_ready != null || _decoding || _image == null)
			{
				return;
			}
			_decoding = true;
		}
		Task.Run(delegate
		{
			XnaColor[] pixels = null;
			try
			{
				pixels = Decode(index);
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
			lock (_sync)
			{
				_decoding = false;
				if (pixels != null && !_disposed)
				{
					_ready = pixels;
					_readyIndex = index;
				}
			}
		});
	}

	private void ReleaseImage()
	{
		lock (_sync)
		{
			_image?.Dispose();
			_image = null;
			_stream?.Dispose();
			_stream = null;
		}
	}

	/// <summary>
	/// Any thread. The texture goes on the next update, not now: it may already be in a sprite batch of this frame (a chip
	/// removed with a click is drawn first), and the batch draws everything at its end.
	/// </summary>
	public void Dispose()
	{
		_disposed = true;
		ReleaseImage();
		lock (_sync)
		{
			_ready = null;
		}
		Texture2D texture = _texture;
		_texture = null;
		if (texture != null)
		{
			Main.QueueMainThreadAction(texture.Dispose);
		}
	}
}
