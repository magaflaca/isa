using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace IsaClient.Core;

/// <summary>
/// The picture, GIF or short video of a release, shown in the update popup above the notes (<see cref="Updater"/>).
/// tools/publish.py --media converts any of them to one small "ISAM" file: a header and the frames as PNG/JPEG with their
/// durations (a picture is one frame; GIFs and videos become a frame sequence, no sound). The file is uploaded as a
/// normal blob and named in the manifest ("media"), so the update worker needs nothing new.
/// Frames stay encoded in memory; while it plays, the next frame is decoded on a background thread and uploaded to a
/// single texture, so even a few hundred frames cost a few MB.
/// </summary>
public sealed class UpdateMedia
{
	public const int MaxBytes = 48 * 1024 * 1024;

	private readonly List<byte[]> _frames = new List<byte[]>();

	private readonly List<int> _delays = new List<int>();

	private Texture2D _texture;

	private int _shown = -1;

	private int _decoding = -1;

	private Microsoft.Xna.Framework.Color[] _ready;

	private int _readyIndex = -1;

	private readonly Stopwatch _clock = new Stopwatch();

	private long _frameStartMs;

	private readonly object _sync = new object();

	public int Width { get; private set; }

	public int Height { get; private set; }

	public int FrameCount => _frames.Count;

	public bool Animated => _frames.Count > 1;

	/// <summary>The first frame is on screen.</summary>
	public bool HasPicture => _texture != null && _shown >= 0;

	/// <summary>0..1 position in the animation (for the little progress line under it).</summary>
	public float PlaybackProgress
	{
		get
		{
			if (!Animated || _shown < 0)
			{
				return 0f;
			}
			long total = 0L;
			long before = 0L;
			for (int i = 0; i < _delays.Count; i++)
			{
				if (i < _shown)
				{
					before += _delays[i];
				}
				total += _delays[i];
			}
			long inFrame = Math.Min(_clock.ElapsedMilliseconds - _frameStartMs, _delays[_shown]);
			return (total <= 0) ? 0f : MathHelper.Clamp((float)(before + inFrame) / (float)total, 0f, 1f);
		}
	}

	/// <summary>Downloads (chunked, like the update itself), checks the SHA-256 and reads the file. Null when anything fails.</summary>
	public static UpdateMedia Download(Func<string, byte[]> downloadChunk, string sha256, long size, int chunks)
	{
		if (size <= 0 || size > MaxBytes || chunks <= 0 || string.IsNullOrEmpty(sha256))
		{
			return null;
		}
		using MemoryStream all = new MemoryStream((int)size);
		for (int i = 0; i < chunks; i++)
		{
			byte[] part = downloadChunk("/api/blob/" + sha256 + "/" + i);
			all.Write(part, 0, part.Length);
			if (all.Length > MaxBytes)
			{
				return null;
			}
		}
		byte[] data = all.ToArray();
		using (SHA256 sha = SHA256.Create())
		{
			if (!string.Equals(BitConverter.ToString(sha.ComputeHash(data)).Replace("-", ""), sha256, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
		}
		return Parse(data);
	}

	/// <summary>"ISAM" 1: u16 width, u16 height, u16 frames, then per frame u16 duration (ms), u32 length, PNG/JPEG bytes.</summary>
	public static UpdateMedia Parse(byte[] data)
	{
		using BinaryReader reader = new BinaryReader(new MemoryStream(data));
		if (reader.ReadByte() != 73 || reader.ReadByte() != 83 || reader.ReadByte() != 65 || reader.ReadByte() != 77 || reader.ReadByte() != 1)
		{
			return null;
		}
		UpdateMedia media = new UpdateMedia
		{
			Width = reader.ReadUInt16(),
			Height = reader.ReadUInt16()
		};
		int count = reader.ReadUInt16();
		if (media.Width <= 0 || media.Height <= 0 || media.Width > 2048 || media.Height > 2048 || count <= 0)
		{
			return null;
		}
		for (int i = 0; i < count; i++)
		{
			int delay = reader.ReadUInt16();
			int length = reader.ReadInt32();
			if (length <= 0 || length > data.Length)
			{
				return null;
			}
			media._delays.Add(Math.Max(20, delay));
			media._frames.Add(reader.ReadBytes(length));
		}
		// The first frame is decoded right away (still on the download thread).
		media._ready = media.Decode(0);
		media._readyIndex = 0;
		return media;
	}

	/// <summary>Main thread, every frame the popup is drawn: advances the animation and returns the texture to draw.</summary>
	public Texture2D Update()
	{
		if (_texture == null)
		{
			_texture = new Texture2D(Main.instance.GraphicsDevice, Width, Height);
			_clock.Start();
		}
		lock (_sync)
		{
			int next = (_shown + 1) % _frames.Count;
			bool due = _shown < 0 || (Animated && _clock.ElapsedMilliseconds - _frameStartMs >= _delays[_shown]);
			if (due && _ready != null && _readyIndex == ((_shown < 0) ? 0 : next))
			{
				_texture.SetData(_ready);
				_shown = _readyIndex;
				_ready = null;
				_readyIndex = -1;
				// Catch up without drifting when the game hitches, but never skip more than one frame's time.
				long now = _clock.ElapsedMilliseconds;
				_frameStartMs = (_shown == 0 || now - _frameStartMs > 2 * _delays[Math.Max(0, _shown - 1)]) ? now : (_frameStartMs + _delays[Math.Max(0, _shown - 1)]);
			}
		}
		if (Animated)
		{
			RequestDecode((_shown + 1) % _frames.Count);
		}
		return _texture;
	}

	private void RequestDecode(int index)
	{
		lock (_sync)
		{
			if (_ready != null || _decoding >= 0)
			{
				return;
			}
			_decoding = index;
		}
		Task.Run(delegate
		{
			Microsoft.Xna.Framework.Color[] pixels = null;
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
				_decoding = -1;
				if (pixels != null)
				{
					_ready = pixels;
					_readyIndex = index;
				}
			}
		});
	}

	/// <summary>PNG/JPEG frame to premultiplied XNA colors at the media size.</summary>
	private Microsoft.Xna.Framework.Color[] Decode(int index)
	{
		using MemoryStream stream = new MemoryStream(_frames[index]);
		using System.Drawing.Bitmap source = new System.Drawing.Bitmap(stream);
		System.Drawing.Bitmap bitmap = (source.Width == Width && source.Height == Height) ? source : new System.Drawing.Bitmap(source, Width, Height);
		try
		{
			BitmapData bits = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			byte[] raw = new byte[Width * 4];
			Microsoft.Xna.Framework.Color[] pixels = new Microsoft.Xna.Framework.Color[Width * Height];
			try
			{
				for (int y = 0; y < Height; y++)
				{
					Marshal.Copy(bits.Scan0 + y * bits.Stride, raw, 0, raw.Length);
					int row = y * Width;
					for (int x = 0; x < Width; x++)
					{
						int b = raw[x * 4];
						int g = raw[x * 4 + 1];
						int r = raw[x * 4 + 2];
						int a = raw[x * 4 + 3];
						pixels[row + x] = (a == 255) ? new Microsoft.Xna.Framework.Color(r, g, b, 255) : new Microsoft.Xna.Framework.Color(r * a / 255, g * a / 255, b * a / 255, a);
					}
				}
			}
			finally
			{
				bitmap.UnlockBits(bits);
			}
			return pixels;
		}
		finally
		{
			if (bitmap != source)
			{
				bitmap.Dispose();
			}
		}
	}

	public void Dispose()
	{
		_texture?.Dispose();
		_texture = null;
	}
}
