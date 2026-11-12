using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace IsaClient.Psp;

/// <summary>
/// Reads .7z archives (PSP games are mostly shared like that): the file list and its LZMA, LZMA2 or stored data, written
/// out as it's decoded (a game is too big to hold in memory). Other methods and passwords aren't supported.
/// </summary>
internal sealed class SevenZip : IDisposable
{
	public sealed class Entry
	{
		public string Name = "";

		public long Size;

		public bool IsDirectory;

		/// <summary>The compressed block it's in (-1: empty), and where it starts once that block is decoded.</summary>
		public int Folder = -1;

		public long Offset;
	}

	private sealed class Block
	{
		public ulong Method;

		public byte[] Properties;

		public long UnpackSize;

		public long PackPosition;

		public long PackSize;

		public int Files = 1;

		public bool Supported;
	}

	private const ulong MethodCopy = 0, MethodLzma2 = 0x21, MethodLzma = 0x030101;

	private readonly FileStream _file;

	private readonly List<Block> _blocks = new List<Block>();

	private long[] _packSizes = new long[0];

	private long _packStart;

	private long[] _subSizes;

	public readonly List<Entry> Entries = new List<Entry>();

	public SevenZip(string path)
	{
		_file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
		byte[] head = new byte[32];
		if (_file.Read(head, 0, 32) != 32 || !IsSevenZip(head))
		{
			throw new InvalidDataException("That isn't a 7z archive.");
		}
		long next = BitConverter.ToInt64(head, 12);
		long size = BitConverter.ToInt64(head, 20);
		if (size == 0)
		{
			return;
		}
		if (next < 0 || size < 0 || size > 256L * 1024 * 1024 || 32 + next + size > _file.Length)
		{
			throw new InvalidDataException("The 7z archive is cut short.");
		}
		byte[] header = new byte[size];
		_file.Position = 32 + next;
		ReadAll(header);
		Parse(header);
	}

	public static bool IsSevenZip(byte[] head) => head.Length >= 6 && head[0] == 0x37 && head[1] == 0x7A && head[2] == 0xBC && head[3] == 0xAF && head[4] == 0x27 && head[5] == 0x1C;

	public void Dispose() => _file.Dispose();

	private void ReadAll(byte[] into)
	{
		int at = 0;
		while (at < into.Length)
		{
			int n = _file.Read(into, at, into.Length - at);
			if (n <= 0)
			{
				throw new EndOfStreamException("The 7z archive is cut short.");
			}
			at += n;
		}
	}

	#region The header

	private sealed class Reader
	{
		public byte[] Data;

		public int At;

		public byte Byte() => Data[At++];

		public ulong Number()
		{
			byte first = Byte();
			byte mask = 0x80;
			ulong value = 0;
			for (int i = 0; i < 8; i++)
			{
				if ((first & mask) == 0)
				{
					value |= (ulong)(first & (mask - 1)) << (8 * i);
					return value;
				}
				value |= (ulong)Byte() << (8 * i);
				mask >>= 1;
			}
			return value;
		}

		public int Int()
		{
			ulong n = Number();
			if (n > int.MaxValue)
			{
				throw new InvalidDataException("The 7z header is damaged.");
			}
			return (int)n;
		}

		public void Skip(ulong n) => At += (int)n;

		public bool[] Bits(int count)
		{
			bool[] bits = new bool[count];
			int mask = 0;
			int value = 0;
			for (int i = 0; i < count; i++)
			{
				if (mask == 0)
				{
					value = Byte();
					mask = 0x80;
				}
				bits[i] = (value & mask) != 0;
				mask >>= 1;
			}
			return bits;
		}

		/// <summary>Bits with an "all set" shortcut in front.</summary>
		public bool[] OptionalBits(int count)
		{
			if (Byte() == 0)
			{
				return Bits(count);
			}
			bool[] all = new bool[count];
			for (int i = 0; i < count; i++)
			{
				all[i] = true;
			}
			return all;
		}
	}

	private void Parse(byte[] header)
	{
		Reader r = new Reader { Data = header };
		int id = r.Byte();
		while (id == 0x17)
		{
			// an encoded header: the real one is compressed, described by what follows
			ReadStreams(r);
			if (_blocks.Count == 0)
			{
				throw new InvalidDataException("The 7z header is damaged.");
			}
			Block b = _blocks[0];
			using MemoryStream decoded = new MemoryStream();
			Decode(b, (data, offset, count) =>
			{
				decoded.Write(data, offset, count);
				return true;
			});
			r = new Reader { Data = decoded.ToArray() };
			_blocks.Clear();
			_subSizes = null;
			id = r.Byte();
		}
		if (id != 0x01)
		{
			throw new InvalidDataException("The 7z header is damaged.");
		}
		id = r.Byte();
		if (id == 0x02)
		{
			// archive properties
			while (r.Byte() != 0)
			{
				r.Skip(r.Number());
			}
			id = r.Byte();
		}
		if (id == 0x03)
		{
			throw new InvalidDataException("This kind of 7z archive isn't supported.");
		}
		if (id == 0x04)
		{
			ReadStreams(r);
			id = r.Byte();
		}
		if (id == 0x05)
		{
			ReadFiles(r);
		}
	}

	/// <summary>StreamsInfo: where the packed data is, the blocks and the files in each one.</summary>
	private void ReadStreams(Reader r)
	{
		int id = r.Byte();
		if (id == 0x06)
		{
			_packStart = 32 + (long)r.Number();
			int count = r.Int();
			_packSizes = new long[count];
			for (id = r.Byte(); id != 0; id = r.Byte())
			{
				if (id == 0x09)
				{
					for (int i = 0; i < count; i++)
					{
						_packSizes[i] = (long)r.Number();
					}
				}
				else if (id == 0x0A)
				{
					SkipDigests(r, count);
				}
				else
				{
					throw new InvalidDataException("The 7z header is damaged.");
				}
			}
			id = r.Byte();
		}
		if (id == 0x07)
		{
			ReadBlocks(r);
			id = r.Byte();
		}
		if (id == 0x08)
		{
			ReadSubStreams(r);
			id = r.Byte();
		}
		if (id != 0)
		{
			throw new InvalidDataException("The 7z header is damaged.");
		}
	}

	private void ReadBlocks(Reader r)
	{
		if (r.Byte() != 0x0B)
		{
			throw new InvalidDataException("The 7z header is damaged.");
		}
		int count = r.Int();
		if (r.Byte() != 0)
		{
			throw new InvalidDataException("This kind of 7z archive isn't supported.");
		}
		long position = _packStart;
		int pack = 0;
		int[] outs = new int[count];
		_blocks.Clear();
		for (int i = 0; i < count; i++)
		{
			Block b = new Block();
			int coders = r.Int();
			int totalIn = 0;
			int totalOut = 0;
			for (int c = 0; c < coders; c++)
			{
				byte flags = r.Byte();
				int idSize = flags & 0xF;
				ulong method = 0;
				for (int k = 0; k < idSize; k++)
				{
					method = (method << 8) | r.Byte();
				}
				int ins = 1;
				int outsHere = 1;
				if ((flags & 0x10) != 0)
				{
					ins = r.Int();
					outsHere = r.Int();
				}
				byte[] props = null;
				if ((flags & 0x20) != 0)
				{
					props = new byte[r.Int()];
					Array.Copy(r.Data, r.At, props, 0, props.Length);
					r.At += props.Length;
				}
				totalIn += ins;
				totalOut += outsHere;
				b.Method = method;
				b.Properties = props;
			}
			// only one plain coder: filters (for programs) and passwords aren't read
			b.Supported = coders == 1 && totalIn == 1 && totalOut == 1 && (b.Method == MethodCopy || b.Method == MethodLzma || b.Method == MethodLzma2);
			for (int p = 0; p < totalOut - 1; p++)
			{
				r.Number();
				r.Number();
			}
			int packed = totalIn - (totalOut - 1);
			if (packed > 1)
			{
				for (int p = 0; p < packed; p++)
				{
					r.Number();
				}
			}
			b.PackPosition = position;
			for (int p = 0; p < packed && pack < _packSizes.Length; p++)
			{
				if (p == 0)
				{
					b.PackSize = _packSizes[pack];
				}
				position += _packSizes[pack++];
			}
			outs[i] = totalOut;
			_blocks.Add(b);
		}
		if (r.Byte() != 0x0C)
		{
			throw new InvalidDataException("The 7z header is damaged.");
		}
		for (int i = 0; i < count; i++)
		{
			for (int o = 0; o < outs[i]; o++)
			{
				long size = (long)r.Number();
				// with filters the last one is the block's; a plain block has just one
				_blocks[i].UnpackSize = size;
			}
		}
		for (int id = r.Byte(); id != 0; id = r.Byte())
		{
			if (id == 0x0A)
			{
				SkipDigests(r, count);
			}
			else
			{
				throw new InvalidDataException("The 7z header is damaged.");
			}
		}
	}

	private void ReadSubStreams(Reader r)
	{
		int id = r.Byte();
		if (id == 0x0D)
		{
			foreach (Block b in _blocks)
			{
				b.Files = r.Int();
			}
			id = r.Byte();
		}
		List<long> sizes = new List<long>();
		foreach (Block b in _blocks)
		{
			if (b.Files == 0)
			{
				continue;
			}
			long left = b.UnpackSize;
			for (int i = 0; i < b.Files - 1; i++)
			{
				long size = (id == 0x09) ? (long)r.Number() : 0;
				sizes.Add(size);
				left -= size;
			}
			sizes.Add(left);
		}
		if (id == 0x09)
		{
			id = r.Byte();
		}
		while (id != 0)
		{
			if (id == 0x0A)
			{
				// how many digests is worked out from what the blocks already have; none are checked here
				int missing = 0;
				foreach (Block b in _blocks)
				{
					missing += b.Files;
				}
				SkipDigests(r, missing);
			}
			else
			{
				throw new InvalidDataException("The 7z header is damaged.");
			}
			id = r.Byte();
		}
		_subSizes = sizes.ToArray();
	}

	private static void SkipDigests(Reader r, int count)
	{
		bool[] defined = r.OptionalBits(count);
		foreach (bool d in defined)
		{
			if (d)
			{
				r.At += 4;
			}
		}
	}

	private void ReadFiles(Reader r)
	{
		int count = r.Int();
		Entry[] files = new Entry[count];
		for (int i = 0; i < count; i++)
		{
			files[i] = new Entry();
		}
		bool[] empty = new bool[count];
		bool[] emptyFile = null;
		int emptyCount = 0;
		while (true)
		{
			int id = (int)r.Number();
			if (id == 0)
			{
				break;
			}
			ulong size = r.Number();
			int end = r.At + (int)size;
			if (id == 0x0E)
			{
				empty = r.Bits(count);
				foreach (bool e in empty)
				{
					emptyCount += e ? 1 : 0;
				}
			}
			else if (id == 0x0F)
			{
				emptyFile = r.Bits(emptyCount);
			}
			else if (id == 0x11)
			{
				if (r.Byte() != 0)
				{
					throw new InvalidDataException("This kind of 7z archive isn't supported.");
				}
				for (int i = 0; i < count; i++)
				{
					int start = r.At;
					while (r.Data[r.At] != 0 || r.Data[r.At + 1] != 0)
					{
						r.At += 2;
					}
					files[i].Name = Encoding.Unicode.GetString(r.Data, start, r.At - start);
					r.At += 2;
				}
			}
			r.At = end;
		}
		int block = 0;
		int inBlock = 0;
		long offset = 0;
		int sub = 0;
		int emptyAt = 0;
		for (int i = 0; i < count; i++)
		{
			Entry e = files[i];
			if (empty[i])
			{
				e.IsDirectory = emptyFile == null || !emptyFile[emptyAt];
				emptyAt++;
				Entries.Add(e);
				continue;
			}
			while (block < _blocks.Count && inBlock >= _blocks[block].Files)
			{
				block++;
				inBlock = 0;
				offset = 0;
			}
			if (block >= _blocks.Count)
			{
				throw new InvalidDataException("The 7z header is damaged.");
			}
			e.Size = (_subSizes != null) ? _subSizes[sub] : _blocks[block].UnpackSize;
			sub++;
			e.Folder = block;
			e.Offset = offset;
			offset += e.Size;
			inBlock++;
			Entries.Add(e);
		}
	}

	#endregion

	#region Taking files out

	/// <summary>
	/// Writes out the files <paramref name="target"/> gives a path for (null: skip it). <paramref name="progress"/> gets
	/// 0..1. Blocks hold many files one after the other, so skipped files are still decoded when they come first.
	/// </summary>
	public void Extract(Func<Entry, string> target, Action<float> progress)
	{
		Dictionary<int, List<KeyValuePair<Entry, string>>> wanted = new Dictionary<int, List<KeyValuePair<Entry, string>>>();
		foreach (Entry e in Entries)
		{
			if (e.IsDirectory)
			{
				continue;
			}
			string path = target(e);
			if (path == null)
			{
				continue;
			}
			if (e.Folder < 0 || e.Size == 0)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllBytes(path, new byte[0]);
				continue;
			}
			if (!wanted.TryGetValue(e.Folder, out List<KeyValuePair<Entry, string>> list))
			{
				wanted[e.Folder] = list = new List<KeyValuePair<Entry, string>>();
			}
			list.Add(new KeyValuePair<Entry, string>(e, path));
		}
		long total = 0;
		foreach (KeyValuePair<int, List<KeyValuePair<Entry, string>>> w in wanted)
		{
			long last = 0;
			foreach (KeyValuePair<Entry, string> f in w.Value)
			{
				last = Math.Max(last, f.Key.Offset + f.Key.Size);
			}
			total += last;
		}
		long done = 0;
		foreach (KeyValuePair<int, List<KeyValuePair<Entry, string>>> w in wanted)
		{
			w.Value.Sort((a, b) => a.Key.Offset.CompareTo(b.Key.Offset));
			done += ExtractBlock(_blocks[w.Key], w.Value, done, total, progress);
		}
	}

	private long ExtractBlock(Block block, List<KeyValuePair<Entry, string>> files, long before, long total, Action<float> progress)
	{
		int next = 0;
		long at = 0;
		FileStream output = null;
		string tmp = null;
		void Finish()
		{
			output.Dispose();
			output = null;
			string path = files[next].Value;
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			File.Move(tmp, path);
			next++;
		}
		try
		{
			Decode(block, (data, offset, count) =>
			{
				while (count > 0 && next < files.Count)
				{
					Entry e = files[next].Key;
					if (at < e.Offset)
					{
						int skip = (int)Math.Min(count, e.Offset - at);
						at += skip;
						offset += skip;
						count -= skip;
						continue;
					}
					if (output == null)
					{
						Directory.CreateDirectory(Path.GetDirectoryName(files[next].Value));
						tmp = files[next].Value + ".tmp";
						output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
					}
					int take = (int)Math.Min(count, e.Offset + e.Size - at);
					output.Write(data, offset, take);
					at += take;
					offset += take;
					count -= take;
					if (at >= e.Offset + e.Size)
					{
						Finish();
					}
				}
				at += count;
				progress?.Invoke(total > 0 ? Math.Min(1f, (float)(before + at) / total) : 1f);
				return next < files.Count;
			});
			if (next < files.Count)
			{
				throw new InvalidDataException("The 7z archive ended too soon.");
			}
		}
		finally
		{
			output?.Dispose();
			if (tmp != null && File.Exists(tmp))
			{
				File.Delete(tmp);
			}
		}
		return at;
	}

	/// <summary>Decoded data as it comes; false stops the decoding.</summary>
	private delegate bool Output(byte[] data, int offset, int count);

	private void Decode(Block block, Output output)
	{
		if (!block.Supported)
		{
			throw new NotSupportedException("That 7z archive uses a method or a password that isn't supported: extract it first.");
		}
		_file.Position = block.PackPosition;
		if (block.Method == MethodCopy)
		{
			byte[] buffer = new byte[1 << 20];
			long left = block.PackSize;
			while (left > 0)
			{
				int n = _file.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
				if (n <= 0)
				{
					throw new EndOfStreamException("The 7z archive is cut short.");
				}
				left -= n;
				if (!output(buffer, 0, n))
				{
					return;
				}
			}
			return;
		}
		if (block.Properties == null || block.Properties.Length < 1)
		{
			throw new InvalidDataException("The 7z header is damaged.");
		}
		Lzma lzma = new Lzma(_file, output);
		if (block.Method == MethodLzma)
		{
			if (block.Properties.Length < 5)
			{
				throw new InvalidDataException("The 7z header is damaged.");
			}
			lzma.DecodeLzma(block.Properties, block.UnpackSize);
		}
		else
		{
			lzma.DecodeLzma2(block.Properties[0], block.UnpackSize);
		}
	}

	#endregion

	/// <summary>The LZMA and LZMA2 decoders (Igor Pavlov's public domain format, after the LZMA specification).</summary>
	private sealed unsafe class Lzma
	{
		private const int NumStates = 12, PosStates = 16;
		private const int IsMatch = 0;
		private const int IsRep = IsMatch + NumStates * PosStates;
		private const int IsRepG0 = IsRep + NumStates;
		private const int IsRepG1 = IsRepG0 + NumStates;
		private const int IsRepG2 = IsRepG1 + NumStates;
		private const int IsRep0Long = IsRepG2 + NumStates;
		private const int PosSlot = IsRep0Long + NumStates * PosStates;
		private const int PosDecoders = PosSlot + 4 * 64;
		private const int Align = PosDecoders + 1 + 128 - 14;
		private const int LenCoder = Align + 16;
		private const int LenSize = 2 + PosStates * 8 * 2 + 256;
		private const int RepLenCoder = LenCoder + LenSize;
		private const int Literal = RepLenCoder + LenSize;
		private const int LowOffset = 2, MidOffset = LowOffset + PosStates * 8, HighOffset = MidOffset + PosStates * 8;

		/// <summary>How much is decoded before it's handed out (for a dictionary bigger than this).</summary>
		private const int FlushEvery = 1 << 20;

		private readonly Stream _source;

		private readonly Output _output;

		private byte[] _in = new byte[1 << 16];

		private int _inAt;

		private int _inEnd;

		/// <summary>False for an LZMA2 chunk: the input is all in <see cref="_in"/>.</summary>
		private bool _refill = true;

		private uint _range;

		private uint _code;

		private ushort[] _probs;

		private int _lc, _lp, _pb;

		private byte[] _dict;

		private int _dictPos;

		private int _flushed;

		/// <summary>Bytes in the dictionary so far (matches can't reach further back), up to its size.</summary>
		private long _valid;

		/// <summary>Bytes since the last state reset (what the position bits count).</summary>
		private long _processed;

		private int _state;

		private uint _rep0, _rep1, _rep2, _rep3;

		private bool _stopped;

		public Lzma(Stream source, Output output)
		{
			_source = source;
			_output = output;
		}

		private void Dictionary(long size)
		{
			if (size > 1536L * 1024 * 1024)
			{
				throw new NotSupportedException("That 7z archive needs too much memory to open here: extract it first.");
			}
			int bytes = (int)Math.Max(size, 1 << 16);
			try
			{
				_dict = new byte[bytes];
			}
			catch (OutOfMemoryException)
			{
				throw new NotSupportedException("That 7z archive needs more memory than there is free: extract it first.");
			}
		}

		private void Properties(int b)
		{
			if (b >= 9 * 5 * 5)
			{
				throw new InvalidDataException("The compressed data is damaged.");
			}
			_lc = b % 9;
			b /= 9;
			_lp = b % 5;
			_pb = b / 5;
			int size = Literal + (0x300 << (_lc + _lp));
			if (_probs == null || _probs.Length != size)
			{
				_probs = new ushort[size];
			}
		}

		private void ResetState()
		{
			for (int i = 0; i < _probs.Length; i++)
			{
				_probs[i] = 1024;
			}
			_state = 0;
			_rep0 = _rep1 = _rep2 = _rep3 = 0;
			_processed = 0;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private byte Next()
		{
			if (_inAt == _inEnd)
			{
				Fill();
			}
			return _in[_inAt++];
		}

		private void Fill()
		{
			if (!_refill)
			{
				throw new InvalidDataException("The compressed data is damaged.");
			}
			_inEnd = _source.Read(_in, 0, _in.Length);
			_inAt = 0;
			if (_inEnd <= 0)
			{
				_inEnd = 0;
				throw new EndOfStreamException("The 7z archive is cut short.");
			}
		}

		private void InitRange()
		{
			if (Next() != 0)
			{
				throw new InvalidDataException("The compressed data is damaged.");
			}
			_range = 0xFFFFFFFFu;
			_code = 0;
			for (int i = 0; i < 4; i++)
			{
				_code = (_code << 8) | Next();
			}
		}

		public void DecodeLzma(byte[] properties, long size)
		{
			Properties(properties[0]);
			Dictionary(BitConverter.ToUInt32(properties, 1));
			ResetState();
			InitRange();
			Run(size);
			Flush();
		}

		public void DecodeLzma2(byte property, long size)
		{
			int bits = property & 0x3F;
			if (bits > 40)
			{
				throw new InvalidDataException("The compressed data is damaged.");
			}
			long dict = (bits == 40) ? uint.MaxValue : (2L | (uint)(bits & 1)) << (bits / 2 + 11);
			Dictionary(Math.Min(dict, Math.Max(size, 1)));
			_refill = true;
			byte[] chunk = new byte[1 << 16];
			byte[] read = new byte[1 << 16];
			byte[] streamBuffer = _in;
			int bufferedAt = 0;
			int bufferedEnd = 0;
			int Byte()
			{
				if (bufferedAt == bufferedEnd)
				{
					bufferedEnd = _source.Read(streamBuffer, 0, streamBuffer.Length);
					bufferedAt = 0;
					if (bufferedEnd <= 0)
					{
						bufferedEnd = 0;
						throw new EndOfStreamException("The 7z archive is cut short.");
					}
				}
				return streamBuffer[bufferedAt++];
			}
			void Bytes(byte[] into, int count)
			{
				for (int i = 0; i < count;)
				{
					if (bufferedAt == bufferedEnd)
					{
						into[i++] = (byte)Byte();
						continue;
					}
					int n = Math.Min(count - i, bufferedEnd - bufferedAt);
					Buffer.BlockCopy(streamBuffer, bufferedAt, into, i, n);
					bufferedAt += n;
					i += n;
				}
			}
			long left = size;
			bool needProperties = true;
			_refill = false;
			while (left > 0 && !_stopped)
			{
				int control = Byte();
				if (control == 0)
				{
					break;
				}
				if (control < 0x80)
				{
					if (control > 2)
					{
						throw new InvalidDataException("The compressed data is damaged.");
					}
					int length = ((Byte() << 8) | Byte()) + 1;
					if (control == 1)
					{
						_valid = 0;
					}
					Bytes(read, length);
					PutRaw(read, length);
					left -= length;
					continue;
				}
				int unpack = (((control & 0x1F) << 16) | (Byte() << 8) | Byte()) + 1;
				int pack = ((Byte() << 8) | Byte()) + 1;
				int mode = (control >> 5) & 3;
				if (mode == 3)
				{
					_valid = 0;
				}
				if (mode >= 2)
				{
					Properties(Byte());
					needProperties = false;
				}
				if (needProperties)
				{
					throw new InvalidDataException("The compressed data is damaged.");
				}
				if (mode >= 1)
				{
					ResetState();
				}
				Bytes(chunk, pack);
				_in = chunk;
				_inAt = 0;
				_inEnd = pack;
				InitRange();
				Run(unpack);
				left -= unpack;
			}
			Flush();
		}

		private void PutRaw(byte[] data, int count)
		{
			for (int i = 0; i < count; i++)
			{
				_dict[_dictPos++] = data[i];
				if (_dictPos == _dict.Length)
				{
					Flush();
					_dictPos = 0;
					_flushed = 0;
				}
			}
			_valid = Math.Min(_valid + count, _dict.Length);
			_processed += count;
		}

		private void Flush()
		{
			if (_dictPos > _flushed && !_stopped)
			{
				if (!_output(_dict, _flushed, _dictPos - _flushed))
				{
					_stopped = true;
				}
			}
			_flushed = _dictPos;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private uint Bit(ushort* p)
		{
			uint v = *p;
			uint bound = (_range >> 11) * v;
			uint bit;
			if (_code < bound)
			{
				_range = bound;
				*p = (ushort)(v + ((2048 - v) >> 5));
				bit = 0;
			}
			else
			{
				_range -= bound;
				_code -= bound;
				*p = (ushort)(v - (v >> 5));
				bit = 1;
			}
			if (_range < (1u << 24))
			{
				_range <<= 8;
				_code = (_code << 8) | Next();
			}
			return bit;
		}

		private uint Tree(ushort* probs, int bits)
		{
			uint m = 1;
			for (int i = 0; i < bits; i++)
			{
				m = (m << 1) + Bit(probs + m);
			}
			return m - (1u << bits);
		}

		private uint ReverseTree(ushort* probs, int bits)
		{
			uint m = 1;
			uint symbol = 0;
			for (int i = 0; i < bits; i++)
			{
				uint b = Bit(probs + m);
				m = (m << 1) + b;
				symbol |= b << i;
			}
			return symbol;
		}

		private uint Direct(int bits)
		{
			uint result = 0;
			for (; bits > 0; bits--)
			{
				_range >>= 1;
				_code -= _range;
				uint t = 0 - (_code >> 31);
				_code += _range & t;
				if (_range < (1u << 24))
				{
					_range <<= 8;
					_code = (_code << 8) | Next();
				}
				result = (result << 1) + t + 1;
			}
			return result;
		}

		private uint Length(ushort* coder, uint posState)
		{
			if (Bit(coder) == 0)
			{
				return Tree(coder + LowOffset + (posState << 3), 3);
			}
			if (Bit(coder + 1) == 0)
			{
				return 8 + Tree(coder + MidOffset + (posState << 3), 3);
			}
			return 16 + Tree(coder + HighOffset, 8);
		}

		/// <summary>Decodes <paramref name="size"/> bytes (or until the stream's end mark) into the dictionary.</summary>
		private void Run(long size)
		{
			int dictSize = _dict.Length;
			uint pbMask = (1u << _pb) - 1;
			uint lpMask = (1u << _lp) - 1;
			int lc = _lc;
			fixed (ushort* probs = _probs)
			fixed (byte* dict = _dict)
			{
				int pos = _dictPos;
				int state = _state;
				long processed = _processed;
				long valid = _valid;
				uint rep0 = _rep0, rep1 = _rep1, rep2 = _rep2, rep3 = _rep3;
				while (size > 0 && !_stopped)
				{
					uint posState = (uint)processed & pbMask;
					if (Bit(probs + IsMatch + (state << 4) + posState) == 0)
					{
						uint previous = (valid != 0) ? dict[(pos == 0 ? dictSize : pos) - 1] : (uint)0;
						ushort* lit = probs + Literal + 0x300 * ((((uint)processed & lpMask) << lc) + (previous >> (8 - lc)));
						uint symbol = 1;
						if (state >= 7)
						{
							int from = pos - (int)rep0 - 1;
							uint match = dict[from < 0 ? from + dictSize : from];
							do
							{
								uint matchBit = (match >> 7) & 1;
								match <<= 1;
								uint bit = Bit(lit + ((1 + matchBit) << 8) + symbol);
								symbol = (symbol << 1) | bit;
								if (matchBit != bit)
								{
									break;
								}
							}
							while (symbol < 0x100);
						}
						while (symbol < 0x100)
						{
							symbol = (symbol << 1) | Bit(lit + symbol);
						}
						dict[pos++] = (byte)symbol;
						state = (state < 4) ? 0 : (state < 10) ? (state - 3) : (state - 6);
						processed++;
						size--;
						if (valid < dictSize)
						{
							valid++;
						}
					}
					else
					{
						uint length;
						if (Bit(probs + IsRep + state) == 0)
						{
							rep3 = rep2;
							rep2 = rep1;
							rep1 = rep0;
							length = Length(probs + LenCoder, posState);
							state = (state < 7) ? 7 : 10;
							uint lenState = (length < 4) ? length : 3;
							uint slot = Tree(probs + PosSlot + (lenState << 6), 6);
							if (slot < 4)
							{
								rep0 = slot;
							}
							else
							{
								int direct = (int)(slot >> 1) - 1;
								rep0 = (2 | (slot & 1)) << direct;
								if (slot < 14)
								{
									rep0 += ReverseTree(probs + PosDecoders + rep0 - slot, direct);
								}
								else
								{
									rep0 += Direct(direct - 4) << 4;
									rep0 += ReverseTree(probs + Align, 4);
								}
								if (rep0 == 0xFFFFFFFFu)
								{
									// the end mark
									break;
								}
							}
						}
						else
						{
							if (valid == 0)
							{
								throw new InvalidDataException("The compressed data is damaged.");
							}
							if (Bit(probs + IsRepG0 + state) == 0)
							{
								if (Bit(probs + IsRep0Long + (state << 4) + posState) == 0)
								{
									// one byte from the last distance
									state = (state < 7) ? 9 : 11;
									int from = pos - (int)rep0 - 1;
									dict[pos] = dict[from < 0 ? from + dictSize : from];
									pos++;
									processed++;
									size--;
									if (valid < dictSize)
									{
										valid++;
									}
									if (pos == dictSize)
									{
										_dictPos = pos;
										Flush();
										pos = 0;
										_dictPos = 0;
										_flushed = 0;
									}
									continue;
								}
							}
							else
							{
								uint distance;
								if (Bit(probs + IsRepG1 + state) == 0)
								{
									distance = rep1;
								}
								else
								{
									if (Bit(probs + IsRepG2 + state) == 0)
									{
										distance = rep2;
									}
									else
									{
										distance = rep3;
										rep3 = rep2;
									}
									rep2 = rep1;
								}
								rep1 = rep0;
								rep0 = distance;
							}
							length = Length(probs + RepLenCoder, posState);
							state = (state < 7) ? 8 : 11;
						}
						if (rep0 >= valid)
						{
							throw new InvalidDataException("The compressed data is damaged.");
						}
						int count = (int)Math.Min(length + 2, size);
						int src = pos - (int)rep0 - 1;
						if (src < 0)
						{
							src += dictSize;
						}
						size -= count;
						processed += count;
						valid = Math.Min(valid + count, dictSize);
						while (count > 0)
						{
							// a run up to whichever end of the ring comes first
							int run = Math.Min(count, Math.Min(dictSize - pos, dictSize - src));
							byte* d = dict + pos;
							byte* s = dict + src;
							for (int i = 0; i < run; i++)
							{
								d[i] = s[i];
							}
							pos += run;
							src += run;
							count -= run;
							if (src == dictSize)
							{
								src = 0;
							}
							if (pos == dictSize)
							{
								_dictPos = pos;
								Flush();
								pos = 0;
								_dictPos = 0;
								_flushed = 0;
							}
						}
					}
					if (pos == dictSize)
					{
						_dictPos = pos;
						Flush();
						pos = 0;
						_dictPos = 0;
						_flushed = 0;
					}
					else if (pos - _flushed >= FlushEvery)
					{
						_dictPos = pos;
						Flush();
					}
				}
				_dictPos = pos;
				_state = state;
				_processed = processed;
				_valid = valid;
				_rep0 = rep0;
				_rep1 = rep1;
				_rep2 = rep2;
				_rep3 = rep3;
			}
		}
	}
}
