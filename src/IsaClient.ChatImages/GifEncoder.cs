using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IsaClient.ChatImages;

/// <summary>
/// Writes animated GIFs (videos sent in the chat become GIFs): one palette of 255 colors for the whole animation (median
/// cut over all the frames), ordered dithering (the same color in the same place always gives the same pixel, so still
/// parts stay still), and each frame only stores the rectangle that changed, with unchanged pixels transparent. A frame
/// that does not change at all makes the one before last longer.
/// </summary>
internal static class GifEncoder
{
	private const int Transparent = 255;

	private static readonly int[] Bayer =
	{
		0, 8, 2, 10,
		12, 4, 14, 6,
		3, 11, 1, 9,
		15, 7, 13, 5
	};

	/// <summary>
	/// <paramref name="frames"/>: RGB, 3 bytes a pixel (the same array twice is fine). <paramref name="delays"/>: per
	/// frame, in hundredths of a second.
	/// </summary>
	public static byte[] Encode(List<byte[]> frames, int width, int height, List<int> delays, Action<float> progress, Func<bool> cancelled)
	{
		byte[] palette = BuildPalette(frames, width, height);
		short[] lookup = new short[32768];
		for (int i = 0; i < lookup.Length; i++)
		{
			lookup[i] = -1;
		}
		int pixels = width * height;
		byte[] previous = new byte[pixels];
		byte[] current = new byte[pixels];
		using MemoryStream output = new MemoryStream();
		BinaryWriter writer = new BinaryWriter(output);
		writer.Write(new[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' });
		writer.Write((ushort)width);
		writer.Write((ushort)height);
		// Global color table of 256 entries.
		writer.Write((byte)0xF7);
		writer.Write((byte)0);
		writer.Write((byte)0);
		writer.Write(palette);
		// Plays forever.
		writer.Write(new byte[] { 0x21, 0xFF, 0x0B });
		writer.Write(new[] { (byte)'N', (byte)'E', (byte)'T', (byte)'S', (byte)'C', (byte)'A', (byte)'P', (byte)'E', (byte)'2', (byte)'.', (byte)'0' });
		writer.Write(new byte[] { 3, 1, 0, 0, 0 });
		LzwEncoder lzw = new LzwEncoder();
		// The frame waiting to be written (its time grows while the next frames bring nothing new).
		byte[] pendingData = null;
		Rectangle pendingRect = default(Rectangle);
		int pendingDelay = 0;
		bool pendingTransparent = false;
		for (int f = 0; f < frames.Count; f++)
		{
			if (cancelled != null && cancelled())
			{
				throw new OperationCanceledException();
			}
			Quantize(frames[f], width, height, palette, lookup, current);
			int delay = Math.Max(2, delays[f]);
			Rectangle changed;
			if (f == 0)
			{
				changed = new Rectangle(0, 0, width, height);
			}
			else
			{
				changed = Changed(previous, current, width, height);
				if (changed.Width == 0)
				{
					pendingDelay += delay;
					continue;
				}
			}
			byte[] data = new byte[changed.Width * changed.Height];
			for (int y = 0; y < changed.Height; y++)
			{
				int row = (changed.Y + y) * width + changed.X;
				for (int x = 0; x < changed.Width; x++)
				{
					byte value = current[row + x];
					data[y * changed.Width + x] = (f > 0 && value == previous[row + x]) ? (byte)Transparent : value;
				}
			}
			Array.Copy(current, previous, pixels);
			if (pendingData != null)
			{
				WriteFrame(writer, lzw, pendingData, pendingRect, pendingDelay, pendingTransparent);
			}
			pendingData = data;
			pendingRect = changed;
			pendingDelay = delay;
			pendingTransparent = f > 0;
			progress?.Invoke((float)(f + 1) / frames.Count);
		}
		if (pendingData != null)
		{
			WriteFrame(writer, lzw, pendingData, pendingRect, pendingDelay, pendingTransparent);
		}
		writer.Write((byte)0x3B);
		writer.Flush();
		return output.ToArray();
	}

	private struct Rectangle
	{
		public int X;

		public int Y;

		public int Width;

		public int Height;

		public Rectangle(int x, int y, int width, int height)
		{
			X = x;
			Y = y;
			Width = width;
			Height = height;
		}
	}

	private static Rectangle Changed(byte[] previous, byte[] current, int width, int height)
	{
		int left = width;
		int right = -1;
		int top = height;
		int bottom = -1;
		for (int y = 0; y < height; y++)
		{
			int row = y * width;
			for (int x = 0; x < width; x++)
			{
				if (previous[row + x] != current[row + x])
				{
					if (x < left)
					{
						left = x;
					}
					if (x > right)
					{
						right = x;
					}
					if (y < top)
					{
						top = y;
					}
					bottom = y;
				}
			}
		}
		return (right < 0) ? default(Rectangle) : new Rectangle(left, top, right - left + 1, bottom - top + 1);
	}

	private static void WriteFrame(BinaryWriter writer, LzwEncoder lzw, byte[] data, Rectangle rect, int delay, bool transparent)
	{
		// Graphic control: keep the frame under the next one (disposal 1), its time, the transparent index.
		writer.Write(new byte[] { 0x21, 0xF9, 4, (byte)(4 | (transparent ? 1 : 0)) });
		writer.Write((ushort)Math.Min(65535, delay));
		writer.Write((byte)Transparent);
		writer.Write((byte)0);
		writer.Write((byte)0x2C);
		writer.Write((ushort)rect.X);
		writer.Write((ushort)rect.Y);
		writer.Write((ushort)rect.Width);
		writer.Write((ushort)rect.Height);
		writer.Write((byte)0);
		writer.Flush();
		lzw.Encode(writer.BaseStream, data);
	}

	private static void Quantize(byte[] rgb, int width, int height, byte[] palette, short[] lookup, byte[] result)
	{
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				int p = y * width + x;
				int at = p * 3;
				// -6..+6 around the color, the same for the same place every frame.
				int offset = Bayer[((y & 3) << 2) | (x & 3)] * 13 / 16 - 6;
				int r = Clamp(rgb[at] + offset);
				int g = Clamp(rgb[at + 1] + offset);
				int b = Clamp(rgb[at + 2] + offset);
				int key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
				short index = lookup[key];
				if (index < 0)
				{
					index = Nearest(palette, (r & 0xF8) | 4, (g & 0xF8) | 4, (b & 0xF8) | 4);
					lookup[key] = index;
				}
				result[p] = (byte)index;
			}
		}
	}

	private static int Clamp(int value)
	{
		return (value < 0) ? 0 : ((value > 255) ? 255 : value);
	}

	private static short Nearest(byte[] palette, int r, int g, int b)
	{
		int best = 0;
		int bestDistance = int.MaxValue;
		for (int i = 0; i < Transparent; i++)
		{
			int dr = palette[i * 3] - r;
			int dg = palette[i * 3 + 1] - g;
			int db = palette[i * 3 + 2] - b;
			// Green counts most, like the eye.
			int distance = dr * dr * 3 + dg * dg * 4 + db * db * 2;
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = i;
			}
		}
		return (short)best;
	}

	#region Palette (median cut)

	private sealed class Box
	{
		public int Start;

		public int End;

		public long Count;

		public int Axis;

		public int Range;
	}

	/// <summary>255 colors (256 entries, the last one the transparent one) for all the frames.</summary>
	private static byte[] BuildPalette(List<byte[]> frames, int width, int height)
	{
		long[] histogram = new long[32768];
		HashSet<byte[]> seen = new HashSet<byte[]>();
		int pixels = width * height;
		// Every other pixel of every other different frame is plenty.
		int frameStep = Math.Max(1, frames.Count / 60);
		for (int f = 0; f < frames.Count; f += frameStep)
		{
			byte[] rgb = frames[f];
			if (!seen.Add(rgb))
			{
				continue;
			}
			for (int p = 0; p < pixels; p += 2)
			{
				int at = p * 3;
				histogram[((rgb[at] >> 3) << 10) | ((rgb[at + 1] >> 3) << 5) | (rgb[at + 2] >> 3)]++;
			}
		}
		int[] bins = Enumerable.Range(0, 32768).Where(i => histogram[i] > 0).ToArray();
		List<Box> boxes = new List<Box>();
		Box first = new Box
		{
			Start = 0,
			End = bins.Length
		};
		Measure(first, bins, histogram);
		boxes.Add(first);
		while (boxes.Count < Transparent)
		{
			Box split = null;
			long bestScore = 0L;
			foreach (Box box in boxes)
			{
				long score = box.Count * box.Range;
				if (box.End - box.Start > 1 && box.Range > 0 && score > bestScore)
				{
					bestScore = score;
					split = box;
				}
			}
			if (split == null)
			{
				break;
			}
			int axis = split.Axis;
			Array.Sort(bins, split.Start, split.End - split.Start, Comparer<int>.Create((a, b) => Channel(a, axis).CompareTo(Channel(b, axis))));
			long half = split.Count / 2;
			long sum = 0L;
			int cut = split.Start;
			while (cut < split.End - 1)
			{
				sum += histogram[bins[cut]];
				cut++;
				if (sum >= half)
				{
					break;
				}
			}
			Box second = new Box
			{
				Start = cut,
				End = split.End
			};
			split.End = cut;
			Measure(split, bins, histogram);
			Measure(second, bins, histogram);
			boxes.Add(second);
		}
		byte[] palette = new byte[768];
		for (int i = 0; i < boxes.Count; i++)
		{
			Box box = boxes[i];
			long r = 0L;
			long g = 0L;
			long b = 0L;
			for (int k = box.Start; k < box.End; k++)
			{
				int bin = bins[k];
				long count = histogram[bin];
				r += (Channel(bin, 0) * 8 + 4) * count;
				g += (Channel(bin, 1) * 8 + 4) * count;
				b += (Channel(bin, 2) * 8 + 4) * count;
			}
			long total = Math.Max(1L, box.Count);
			palette[i * 3] = (byte)(r / total);
			palette[i * 3 + 1] = (byte)(g / total);
			palette[i * 3 + 2] = (byte)(b / total);
		}
		// Unused entries (a picture with few colors) repeat the first color: never the nearest by accident.
		for (int i = boxes.Count; i < Transparent; i++)
		{
			palette[i * 3] = palette[0];
			palette[i * 3 + 1] = palette[1];
			palette[i * 3 + 2] = palette[2];
		}
		return palette;
	}

	private static int Channel(int bin, int axis)
	{
		return (bin >> (10 - axis * 5)) & 31;
	}

	private static void Measure(Box box, int[] bins, long[] histogram)
	{
		int[] min = { 31, 31, 31 };
		int[] max = { 0, 0, 0 };
		long count = 0L;
		for (int k = box.Start; k < box.End; k++)
		{
			int bin = bins[k];
			count += histogram[bin];
			for (int axis = 0; axis < 3; axis++)
			{
				int value = Channel(bin, axis);
				min[axis] = Math.Min(min[axis], value);
				max[axis] = Math.Max(max[axis], value);
			}
		}
		box.Count = count;
		box.Axis = 0;
		box.Range = 0;
		for (int axis = 0; axis < 3; axis++)
		{
			// Green weighs more: the eye sees its steps first.
			int range = (max[axis] - min[axis]) * ((axis == 1) ? 4 : ((axis == 0) ? 3 : 2));
			if (range > box.Range)
			{
				box.Range = range;
				box.Axis = axis;
			}
		}
	}

	#endregion

	#region LZW

	/// <summary>GIF's variable-length LZW (8-bit pixels), in 255-byte sub-blocks.</summary>
	private sealed class LzwEncoder
	{
		private const int MaxBits = 12;

		private const int MaxMaxCode = 1 << MaxBits;

		private const int HashSize = 5003;

		private readonly int[] _hashes = new int[HashSize];

		private readonly int[] _codes = new int[HashSize];

		private readonly byte[] _block = new byte[256];

		private int _blockCount;

		private int _bits;

		private int _maxCode;

		private int _freeEntry;

		private bool _clearFlag;

		private int _initBits;

		private int _clearCode;

		private int _endCode;

		private int _accumulator;

		private int _accumulated;

		private Stream _output;

		public void Encode(Stream output, byte[] pixels)
		{
			_output = output;
			output.WriteByte(8);
			_initBits = 9;
			_clearFlag = false;
			_bits = _initBits;
			_maxCode = (1 << _bits) - 1;
			_clearCode = 256;
			_endCode = 257;
			_freeEntry = 258;
			_accumulator = 0;
			_accumulated = 0;
			_blockCount = 0;
			ClearHash();
			Output(_clearCode);
			int entry = pixels.Length > 0 ? pixels[0] : 0;
			for (int p = 1; p < pixels.Length; p++)
			{
				int c = pixels[p];
				int fcode = (c << MaxBits) + entry;
				int i = (c << 4) ^ entry;
				if (_hashes[i] == fcode)
				{
					entry = _codes[i];
					continue;
				}
				bool found = false;
				if (_hashes[i] >= 0)
				{
					int displacement = (i == 0) ? 1 : (HashSize - i);
					do
					{
						i -= displacement;
						if (i < 0)
						{
							i += HashSize;
						}
						if (_hashes[i] == fcode)
						{
							entry = _codes[i];
							found = true;
							break;
						}
					}
					while (_hashes[i] >= 0);
				}
				if (found)
				{
					continue;
				}
				Output(entry);
				entry = c;
				if (_freeEntry < MaxMaxCode)
				{
					_codes[i] = _freeEntry++;
					_hashes[i] = fcode;
				}
				else
				{
					ClearHash();
					_freeEntry = _clearCode + 2;
					_clearFlag = true;
					Output(_clearCode);
				}
			}
			Output(entry);
			Output(_endCode);
			output.WriteByte(0);
		}

		private void ClearHash()
		{
			for (int i = 0; i < HashSize; i++)
			{
				_hashes[i] = -1;
			}
		}

		private void Output(int code)
		{
			_accumulator &= (1 << _accumulated) - 1;
			_accumulator = (_accumulated > 0) ? (_accumulator | (code << _accumulated)) : code;
			_accumulated += _bits;
			while (_accumulated >= 8)
			{
				Add((byte)(_accumulator & 0xFF));
				_accumulator >>= 8;
				_accumulated -= 8;
			}
			if (_freeEntry > _maxCode || _clearFlag)
			{
				if (_clearFlag)
				{
					_bits = _initBits;
					_maxCode = (1 << _bits) - 1;
					_clearFlag = false;
				}
				else
				{
					_bits++;
					_maxCode = (_bits == MaxBits) ? MaxMaxCode : ((1 << _bits) - 1);
				}
			}
			if (code == _endCode)
			{
				while (_accumulated > 0)
				{
					Add((byte)(_accumulator & 0xFF));
					_accumulator >>= 8;
					_accumulated -= 8;
				}
				Flush();
			}
		}

		private void Add(byte value)
		{
			_block[_blockCount++] = value;
			if (_blockCount >= 254)
			{
				Flush();
			}
		}

		private void Flush()
		{
			if (_blockCount > 0)
			{
				_output.WriteByte((byte)_blockCount);
				_output.Write(_block, 0, _blockCount);
				_blockCount = 0;
			}
		}
	}

	#endregion
}
