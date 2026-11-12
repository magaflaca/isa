using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace IsaClient.Building;

/// <summary>Minimal 32bpp BGRA bitmap loaded through GDI+ (replaces the SkiaSharp dependency of the original client).</summary>
public sealed class PixelImage : IDisposable
{
	public int Width { get; private set; }

	public int Height { get; private set; }

	public int RowBytes => Width * 4;

	/// <summary>Pixels in BGRA order, row-major, 4 bytes per pixel.</summary>
	public byte[] Data { get; private set; }

	public static PixelImage Decode(Stream stream)
	{
		return Decode(stream, 100f);
	}

	/// <summary>Decodes an image, optionally downscaled to <paramref name="scalePercent"/> % of its size (area-averaged).</summary>
	public static PixelImage Decode(Stream stream, float scalePercent)
	{
		try
		{
			using Bitmap source = new Bitmap(stream);
			float scale = Math.Max(0.001f, Math.Min(100f, scalePercent)) / 100f;
			int width = Math.Max(1, (int)Math.Round((float)source.Width * scale));
			int height = Math.Max(1, (int)Math.Round((float)source.Height * scale));
			return FromBitmap(source, width, height, sharp: false);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// A picture file in any format Windows reads (GDI+, then Windows' codecs: WebP, HEIC, AVIF...), turned upright as the
	/// camera saved it, at <paramref name="scalePercent"/> % and then fitted to <paramref name="maxSide"/> pixels per side
	/// (<paramref name="fitted"/> says so). Null when nothing reads it.
	/// </summary>
	public static PixelImage Load(string path, float scalePercent, int maxSide, bool sharp, out int sourceWidth, out int sourceHeight, out bool fitted)
	{
		sourceWidth = 0;
		sourceHeight = 0;
		fitted = false;
		using Bitmap source = IsaClient.ChatImages.PictureFiles.ReadBitmap(File.ReadAllBytes(path));
		if (source == null)
		{
			return null;
		}
		Upright(source);
		sourceWidth = source.Width;
		sourceHeight = source.Height;
		float scale = Math.Max(0.001f, Math.Min(100f, scalePercent)) / 100f;
		double width = Math.Max(1.0, Math.Round(source.Width * scale));
		double height = Math.Max(1.0, Math.Round(source.Height * scale));
		if (maxSide > 0 && Math.Max(width, height) > maxSide)
		{
			double fit = maxSide / Math.Max(width, height);
			width = Math.Max(1.0, Math.Floor(width * fit));
			height = Math.Max(1.0, Math.Floor(height * fit));
			fitted = true;
		}
		return FromBitmap(source, (int)width, (int)height, sharp);
	}

	/// <summary>Phone photos are saved sideways with an EXIF note saying how to turn them.</summary>
	private static void Upright(Bitmap bitmap)
	{
		try
		{
			if (Array.IndexOf(bitmap.PropertyIdList, 0x0112) < 0)
			{
				return;
			}
			switch (bitmap.GetPropertyItem(0x0112).Value[0])
			{
			case 2:
				bitmap.RotateFlip(RotateFlipType.RotateNoneFlipX);
				break;
			case 3:
				bitmap.RotateFlip(RotateFlipType.Rotate180FlipNone);
				break;
			case 4:
				bitmap.RotateFlip(RotateFlipType.Rotate180FlipX);
				break;
			case 5:
				bitmap.RotateFlip(RotateFlipType.Rotate90FlipX);
				break;
			case 6:
				bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);
				break;
			case 7:
				bitmap.RotateFlip(RotateFlipType.Rotate270FlipX);
				break;
			case 8:
				bitmap.RotateFlip(RotateFlipType.Rotate270FlipNone);
				break;
			}
		}
		catch
		{
		}
	}

	/// <summary>
	/// <paramref name="source"/> drawn at <paramref name="width"/>x<paramref name="height"/>, as BGRA bytes: smooth
	/// (area-averaged), or <paramref name="sharp"/> (nearest pixel: pixel art stays crisp).
	/// </summary>
	private static PixelImage FromBitmap(Bitmap source, int width, int height, bool sharp)
	{
		{
			using Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
			using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
			{
				graphics.Clear(System.Drawing.Color.Transparent);
				if (width == source.Width && height == source.Height)
				{
					graphics.DrawImage(source, 0, 0, source.Width, source.Height);
				}
				else
				{
					graphics.InterpolationMode = sharp ? System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor : System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
					graphics.PixelOffsetMode = sharp ? System.Drawing.Drawing2D.PixelOffsetMode.Half : System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
					graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
					using ImageAttributes imageAttributes = new ImageAttributes();
					imageAttributes.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
					graphics.DrawImage(source, new System.Drawing.Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, System.Drawing.GraphicsUnit.Pixel, imageAttributes);
				}
			}
			BitmapData bitmapData = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			PixelImage pixelImage = new PixelImage
			{
				Width = bitmap.Width,
				Height = bitmap.Height,
				Data = new byte[bitmap.Width * bitmap.Height * 4]
			};
			for (int i = 0; i < bitmap.Height; i++)
			{
				Marshal.Copy(bitmapData.Scan0 + i * bitmapData.Stride, pixelImage.Data, i * pixelImage.RowBytes, pixelImage.RowBytes);
			}
			bitmap.UnlockBits(bitmapData);
			return pixelImage;
		}
	}

	public PixelImage PeekPixels()
	{
		return this;
	}

	public byte[] GetPixelSpan()
	{
		return Data;
	}

	public void Dispose()
	{
	}
}
