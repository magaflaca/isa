using System;
using System.Collections.Generic;
using System.Threading;
using static IsaClient.Video.MediaInterop;

namespace IsaClient.Video;

/// <summary>
/// The frames of a video file for a GIF (videos sent in the chat): Windows decodes the picture, every frame that falls on
/// the GIF's frame rate is shrunk to <c>maxWidth</c> (turned upright when the phone that filmed it says so) and kept as
/// RGB bytes, up to <c>maxSeconds</c>. GifEncoder writes them.
/// </summary>
public static class VideoToGif
{
	/// <summary>MF_MT_VIDEO_ROTATION: degrees the picture must turn to stand upright (phone videos).</summary>
	private static readonly Guid VideoRotation = new Guid("c380465d-2271-428c-9b83-ecea3b4a85c1");

	/// <summary>
	/// Frames as RGB (3 bytes a pixel), all <paramref name="width"/> x <paramref name="height"/>, <paramref name="fps"/> a second
	/// (lowered for long videos); throws with a message for the chat.
	/// </summary>
	public static List<byte[]> ReadFrames(string path, int maxWidth, ref int fps, double maxSeconds, long maxPixels, Action<float> progress, Func<bool> cancelled, out int width, out int height)
	{
		width = 0;
		height = 0;
		List<byte[]> frames = new List<byte[]>();
		IMFSourceReader reader = null;
		bool started = false;
		try
		{
			Check(MFStartup(MfVersion, 0), "MFStartup");
			started = true;
			Check(MFCreateAttributes(out var attributes, 1), "MFCreateAttributes");
			attributes.SetUINT32(EnableAdvancedVideoProcessing, 1);
			reader = MediaInput.File(path).CreateReader(attributes);
			Release(attributes);
			reader.SetStreamSelection(AllStreams, selected: false);
			Check(reader.SetStreamSelection(FirstVideoStream, selected: true), "This file has no picture");
			int rotation = 0;
			long nativeSize = 0L;
			if (reader.GetNativeMediaType(FirstVideoStream, 0, out var native) >= 0)
			{
				if (native.GetUINT32(VideoRotation, out var degrees) >= 0)
				{
					rotation = degrees % 360;
				}
				native.GetUINT64(FrameSize, out nativeSize);
				Release(native);
			}
			Check(MFCreateMediaType(out var request), "MFCreateMediaType");
			request.SetGUID(MajorType, MediaTypeVideo);
			request.SetGUID(Subtype, VideoFormatRgb32);
			Check(reader.SetCurrentMediaType(FirstVideoStream, IntPtr.Zero, request), "Windows cannot decode this video (it needs an H.264 or another format Windows plays)");
			Release(request);
			ReadFormat(reader, out var sourceWidth, out var sourceHeight, out var stride);
			// Windows' video processing turns the picture itself (its size comes out swapped): then it is not turned again.
			// When the size did not change, a quarter turn is still to do here; a half turn is left to Windows.
			bool turnedByWindows = (rotation == 90 || rotation == 270) ? (sourceWidth != sourceHeight && (int)(nativeSize >> 32) == sourceHeight && (int)(nativeSize & 0xFFFFFFFFL) == sourceWidth) : true;
			if (turnedByWindows)
			{
				rotation = 0;
			}
			double length = maxSeconds;
			if (reader.GetPresentationAttribute(MediaSource, MediaInterop.Duration, out var duration) >= 0 && duration.HVal > 0)
			{
				length = Math.Min(maxSeconds, duration.HVal / (double)TimeSpan.TicksPerSecond);
			}
			bool sideways = rotation == 90 || rotation == 270;
			int uprightWidth = sideways ? sourceHeight : sourceWidth;
			int uprightHeight = sideways ? sourceWidth : sourceHeight;
			width = Math.Min(maxWidth, uprightWidth) & ~1;
			height = Math.Max(2, (int)Math.Round((double)uprightHeight * width / uprightWidth)) & ~1;
			// Long videos at a big size would not fit in memory: fewer frames a second then.
			long perSecond = (long)width * height * fps;
			if (perSecond * length > maxPixels)
			{
				fps = Math.Max(5, (int)(maxPixels / Math.Max(1.0, length) / ((long)width * height)));
			}
			long step = TimeSpan.TicksPerSecond / fps;
			long next = 0L;
			long end = (long)(length * TimeSpan.TicksPerSecond);
			while (true)
			{
				if (cancelled != null && cancelled())
				{
					throw new OperationCanceledException();
				}
				int hr = reader.ReadSample(FirstVideoStream, 0, out _, out var flags, out var timestamp, out var sample);
				if (hr < 0)
				{
					throw new InvalidOperationException("The video stopped decoding (0x" + hr.ToString("X8") + ").");
				}
				if ((flags & CurrentMediaTypeChangedFlag) != 0)
				{
					ReadFormat(reader, out sourceWidth, out sourceHeight, out stride);
				}
				if ((flags & EndOfStreamFlag) != 0 || timestamp >= end)
				{
					Release(sample);
					break;
				}
				if (sample == null)
				{
					continue;
				}
				try
				{
					// A video frame fills every GIF frame time up to it (the same bytes when the video has fewer frames a
					// second: GifEncoder joins repeated frames); frames between GIF frame times are skipped.
					byte[] shrunk = null;
					while (next <= timestamp + step / 2 && next < end)
					{
						shrunk = shrunk ?? Shrink(sample, sourceWidth, sourceHeight, stride, width, height, rotation);
						if (shrunk == null)
						{
							break;
						}
						frames.Add(shrunk);
						next += step;
						progress?.Invoke((float)Math.Min(1.0, (double)next / end));
					}
				}
				finally
				{
					Release(sample);
				}
			}
		}
		finally
		{
			Release(reader);
			if (started)
			{
				MFShutdown();
			}
		}
		if (frames.Count == 0)
		{
			throw new InvalidOperationException("The video has no frames Windows could read.");
		}
		return frames;
	}

	private static void ReadFormat(IMFSourceReader reader, out int width, out int height, out int stride)
	{
		Check(reader.GetCurrentMediaType(FirstVideoStream, out var current), "GetCurrentMediaType");
		current.GetUINT64(FrameSize, out var size);
		width = (int)(size >> 32);
		height = (int)(size & 0xFFFFFFFFL);
		stride = (current.GetUINT32(DefaultStride, out var defaultStride) >= 0) ? defaultStride : (width * 4);
		Release(current);
		if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
		{
			throw new InvalidOperationException("Unsupported picture size " + width + "x" + height + ".");
		}
	}

	/// <summary>One decoded frame (BGRX) averaged down to the GIF size and turned upright, as RGB.</summary>
	private static unsafe byte[] Shrink(IMFSample sample, int sourceWidth, int sourceHeight, int stride, int width, int height, int rotation)
	{
		if (sample.ConvertToContiguousBuffer(out var buffer) < 0)
		{
			return null;
		}
		try
		{
			if (buffer.Lock(out var pointer, out _, out var length) < 0)
			{
				return null;
			}
			try
			{
				int rowBytes = Math.Abs(stride);
				if (rowBytes < sourceWidth * 4 || (long)rowBytes * sourceHeight > length)
				{
					rowBytes = length / sourceHeight;
					if (rowBytes < sourceWidth * 4)
					{
						return null;
					}
				}
				bool sideways = rotation == 90 || rotation == 270;
				// Size of the picture as decoded (before turning it).
				int w = sideways ? height : width;
				int h = sideways ? width : height;
				byte[] upright = new byte[width * height * 3];
				byte* source = (byte*)pointer;
				for (int y = 0; y < h; y++)
				{
					int y0 = y * sourceHeight / h;
					int y1 = Math.Max(y0 + 1, (y + 1) * sourceHeight / h);
					for (int x = 0; x < w; x++)
					{
						int x0 = x * sourceWidth / w;
						int x1 = Math.Max(x0 + 1, (x + 1) * sourceWidth / w);
						// Averages up to 4x4 samples of the area (enough for a smooth picture, still fast).
						int stepY = Math.Max(1, (y1 - y0) / 4);
						int stepX = Math.Max(1, (x1 - x0) / 4);
						int r = 0;
						int g = 0;
						int b = 0;
						int n = 0;
						for (int sy = y0; sy < y1; sy += stepY)
						{
							byte* row = source + (long)((stride < 0) ? (sourceHeight - 1 - sy) : sy) * rowBytes;
							for (int sx = x0; sx < x1; sx += stepX)
							{
								byte* p = row + sx * 4;
								b += p[0];
								g += p[1];
								r += p[2];
								n++;
							}
						}
						int ux;
						int uy;
						switch (rotation)
						{
						case 90:
							ux = h - 1 - y;
							uy = x;
							break;
						case 180:
							ux = w - 1 - x;
							uy = h - 1 - y;
							break;
						case 270:
							ux = y;
							uy = w - 1 - x;
							break;
						default:
							ux = x;
							uy = y;
							break;
						}
						int at = (uy * width + ux) * 3;
						upright[at] = (byte)(r / n);
						upright[at + 1] = (byte)(g / n);
						upright[at + 2] = (byte)(b / n);
					}
				}
				return upright;
			}
			finally
			{
				buffer.Unlock();
			}
		}
		finally
		{
			Release(buffer);
		}
	}
}
