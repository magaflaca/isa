using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using IsaClient.Video;

namespace IsaClient.ChatImages;

/// <summary>
/// Turns what is attached into what Imgur gets: PNG, JPEG and GIF files go as they are; any other picture Windows can
/// read (BMP, TIFF, ICO, WebP, HEIC, AVIF... with Windows' codecs) becomes PNG, or JPEG when it is a big photo; a video
/// becomes a GIF (VideoToGif + GifEncoder). Pictures pasted from the clipboard become PNG.
/// </summary>
internal static class PictureFiles
{
	/// <summary>Imgur's limit for a picture (GIFs too, for anonymous uploads).</summary>
	public const int MaxUploadBytes = 20 * 1024 * 1024;

	public static readonly string[] PictureExtensions = { ".png", ".jpg", ".jpeg", ".jfif", ".gif", ".bmp", ".dib", ".tif", ".tiff", ".ico", ".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp" };

	public static readonly string[] VideoExtensions = { ".mp4", ".m4v", ".mov", ".webm", ".mkv", ".avi", ".wmv", ".3gp", ".mpg", ".mpeg", ".ts" };

	public static bool IsPicture(string path)
	{
		return PictureExtensions.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());
	}

	public static bool IsVideo(string path)
	{
		return VideoExtensions.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());
	}

	/// <summary>A file ready to upload: its bytes, a name and its type.</summary>
	internal sealed class Prepared
	{
		public byte[] Data;

		public string FileName;

		public string ContentType;
	}

	/// <summary>What kind of picture <paramref name="data"/> is, by its first bytes (null when it is none Imgur takes as is).</summary>
	public static string Sniff(byte[] data)
	{
		if (data.Length > 8 && data[0] == 0x89 && data[1] == (byte)'P' && data[2] == (byte)'N' && data[3] == (byte)'G')
		{
			return "image/png";
		}
		if (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
		{
			return "image/jpeg";
		}
		if (data.Length > 6 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'8')
		{
			return "image/gif";
		}
		return null;
	}

	/// <summary>A picture file for Imgur (off the main thread). Throws with a message for the chat.</summary>
	public static Prepared FromPictureFile(string path)
	{
		byte[] data = File.ReadAllBytes(path);
		string name = Path.GetFileNameWithoutExtension(path);
		string type = Sniff(data);
		if (type != null && data.Length <= MaxUploadBytes && (type != "image/png" || data.Length <= 8 * 1024 * 1024))
		{
			return new Prepared
			{
				Data = data,
				FileName = name + Extension(type),
				ContentType = type
			};
		}
		if (type == "image/gif")
		{
			throw new InvalidOperationException(Path.GetFileName(path) + " is over Imgur's 20 MB for a GIF.");
		}
		using Bitmap bitmap = ReadBitmap(data) ?? throw new InvalidOperationException("Windows can't read " + Path.GetFileName(path) + " as a picture.");
		return FromBitmap(bitmap, name);
	}

	/// <summary>A picture from the clipboard or a file Windows decoded: PNG, or JPEG when the PNG would be too big.</summary>
	public static Prepared FromBitmap(Bitmap bitmap, string name)
	{
		Bitmap source = bitmap;
		Bitmap shrunk = null;
		try
		{
			// Imgur keeps pictures up to about 5000 px a side; bigger ones are shrunk here (faster to upload too).
			int longest = Math.Max(bitmap.Width, bitmap.Height);
			if (longest > 4096)
			{
				double scale = 4096.0 / longest;
				shrunk = new Bitmap(bitmap, new Size(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale))));
				source = shrunk;
			}
			byte[] png = Save(source, ImageFormat.Png);
			if (png.Length <= 5 * 1024 * 1024 || HasTransparency(source))
			{
				if (png.Length > MaxUploadBytes)
				{
					throw new InvalidOperationException("The picture is too big for Imgur (over 20 MB).");
				}
				return new Prepared
				{
					Data = png,
					FileName = name + ".png",
					ContentType = "image/png"
				};
			}
			return new Prepared
			{
				Data = SaveJpeg(source, 90L),
				FileName = name + ".jpg",
				ContentType = "image/jpeg"
			};
		}
		finally
		{
			shrunk?.Dispose();
		}
	}

	/// <summary>A video file as a GIF (off the main thread): <paramref name="progress"/> 0..1, conversion first then encoding.</summary>
	public static Prepared FromVideo(string path, int width, int fps, int seconds, Action<float> progress, Func<bool> cancelled)
	{
		int used = fps;
		List<byte[]> frames = VideoToGif.ReadFrames(path, width, ref used, seconds, 48L * 1024 * 1024, p => progress?.Invoke(p * 0.6f), cancelled, out var w, out var h);
		List<int> delays = new List<int>();
		for (int i = 0; i < frames.Count; i++)
		{
			// Hundredths that add up exactly (15 fps: 7, 6, 7, 7, 6...).
			delays.Add((int)Math.Round((i + 1) * 100.0 / used) - (int)Math.Round(i * 100.0 / used));
		}
		byte[] gif = GifEncoder.Encode(frames, w, h, delays, p => progress?.Invoke(0.6f + p * 0.4f), cancelled);
		if (gif.Length > MaxUploadBytes)
		{
			throw new InvalidOperationException("The GIF came out over 20 MB: try a shorter part, or a smaller GIF size in the chat image settings.");
		}
		return new Prepared
		{
			Data = gif,
			FileName = Path.GetFileNameWithoutExtension(path) + ".gif",
			ContentType = "image/gif"
		};
	}

	/// <summary>Any picture Windows can read: GDI+ first, then Windows' imaging codecs (WebP, HEIC, AVIF...). Null when neither can.</summary>
	public static Bitmap ReadBitmap(byte[] data)
	{
		try
		{
			using MemoryStream stream = new MemoryStream(data);
			using Image image = Image.FromStream(stream);
			return new Bitmap(image);
		}
		catch
		{
		}
		try
		{
			return WicDecoder.Decode(data);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>Bytes GDI+ can show (a WebP received from elsewhere becomes PNG); null when nothing reads them.</summary>
	public static byte[] Readable(byte[] data)
	{
		if (Sniff(data) != null)
		{
			return data;
		}
		using Bitmap bitmap = ReadBitmap(data);
		return (bitmap == null) ? null : Save(bitmap, ImageFormat.Png);
	}

	private static string Extension(string type)
	{
		return (type == "image/png") ? ".png" : ((type == "image/gif") ? ".gif" : ".jpg");
	}

	private static byte[] Save(Image image, ImageFormat format)
	{
		using MemoryStream stream = new MemoryStream();
		image.Save(stream, format);
		return stream.ToArray();
	}

	private static byte[] SaveJpeg(Image image, long quality)
	{
		ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
		using EncoderParameters parameters = new EncoderParameters(1);
		parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
		using MemoryStream stream = new MemoryStream();
		// JPEG has no transparency: drawn on white, like most viewers show it.
		using (Bitmap flat = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb))
		{
			using (Graphics graphics = Graphics.FromImage(flat))
			{
				graphics.Clear(Color.White);
				graphics.DrawImage(image, 0, 0, image.Width, image.Height);
			}
			flat.Save(stream, codec, parameters);
		}
		return stream.ToArray();
	}

	private static unsafe bool HasTransparency(Bitmap bitmap)
	{
		if (!Image.IsAlphaPixelFormat(bitmap.PixelFormat))
		{
			return false;
		}
		BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		try
		{
			for (int y = 0; y < bitmap.Height; y += 2)
			{
				byte* row = (byte*)data.Scan0 + (long)y * data.Stride;
				for (int x = 0; x < bitmap.Width; x += 2)
				{
					if (row[x * 4 + 3] < 250)
					{
						return true;
					}
				}
			}
			return false;
		}
		finally
		{
			bitmap.UnlockBits(data);
		}
	}
}
