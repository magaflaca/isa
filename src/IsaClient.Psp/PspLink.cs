using System;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace IsaClient.Psp;

/// <summary>
/// What the game and the emulator's process share (see <see cref="PspHost"/>): one block of memory with the buttons going
/// one way, the pictures and the sound the other, and one command at a time. Pictures go round three buffers, so one is
/// always whole. Only the two ends of the sound ring move their own index, so nothing needs a lock.
/// </summary>
internal sealed unsafe class PspLink : IDisposable
{
	public const int Starting = 0, Running = 1, Stopped = 2, Failed = 3;

	public const int CommandSave = 1, CommandLoad = 2, CommandReset = 3, CommandStop = 4;

	/// <summary>The biggest picture: three times the PSP's.</summary>
	public const int MaxPixels = 1440 * 816;

	/// <summary>Frames of stereo sound in the ring (about a second and a half).</summary>
	public const int AudioFrames = 1 << 16;

	private const int ConfigAt = 0x1000, ErrorAt = 0x4000, PathAt = 0x6000, TextRoom = 0x2000;

	private const int AudioAt = 0x10000;

	private const int FramesAt = AudioAt + AudioFrames * 4;

	private const int Total = FramesAt + MaxPixels * 4 * 3;

	private readonly MemoryMappedFile _file;

	private readonly MemoryMappedViewAccessor _view;

	private readonly byte* _base;

	private PspLink(MemoryMappedFile file)
	{
		_file = file;
		_view = file.CreateViewAccessor(0, Total);
		byte* p = null;
		_view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
		_base = p;
	}

	public static PspLink Create(string name) => new PspLink(MemoryMappedFile.CreateNew(name, Total));

	public static PspLink Open(string name) => new PspLink(MemoryMappedFile.OpenExisting(name));

	public void Dispose()
	{
		_view.SafeMemoryMappedViewHandle.ReleasePointer();
		_view.Dispose();
		_file.Dispose();
	}

	private int Get(int at) => Volatile.Read(ref *(int*)(_base + at));

	private void Set(int at, int value) => Volatile.Write(ref *(int*)(_base + at), value);

	public int State { get => Get(0); set => Set(0, value); }

	public int Buttons { get => Get(4); set => Set(4, value); }

	public int AnalogX { get => Get(8); set => Set(8, value); }

	public int AnalogY { get => Get(12); set => Set(12, value); }

	public bool Paused { get => Get(16) != 0; set => Set(16, value ? 1 : 0); }

	public int Speed { get => Get(20); set => Set(20, value); }

	public int FrameSerial { get => Get(24); set => Set(24, value); }

	public int FrameCount { get => Get(28); set => Set(28, value); }

	/// <summary>In thousandths, and the sound's rate.</summary>
	public int FpsMilli { get => Get(32); set => Set(32, value); }

	public int SampleRate { get => Get(36); set => Set(36, value); }

	public bool OnGraphicsCard { get => Get(40) != 0; set => Set(40, value ? 1 : 0); }

	/// <summary>Sound waiting to play at the game's end (negative: there's no sound).</summary>
	public int QueuedMs { get => Get(44); set => Set(44, value); }

	public int Resolution { get => Get(48); set => Set(48, value); }

	/// <summary>The command last sent, its kind, and the last one finished with how it went.</summary>
	public int CommandSerial { get => Get(52); set => Set(52, value); }

	public int CommandKind { get => Get(56); set => Set(56, value); }

	public int DoneSerial { get => Get(60); set => Set(60, value); }

	public bool DoneOk { get => Get(64) != 0; set => Set(64, value ? 1 : 0); }

	private int FrameIndex { get => Get(68); set => Set(68, value); }

	private int FrameW { get => Get(72); set => Set(72, value); }

	private int FrameH { get => Get(76); set => Set(76, value); }

	private int AudioWrite { get => Get(80); set => Set(80, value); }

	private int AudioRead { get => Get(84); set => Set(84, value); }

	private string GetText(int at)
	{
		int length = Get(at);
		return (length <= 0) ? "" : Encoding.UTF8.GetString(_base + at + 4, length);
	}

	private void SetText(int at, string text)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
		int n = Math.Min(bytes.Length, TextRoom - 4);
		for (int i = 0; i < n; i++)
		{
			_base[at + 4 + i] = bytes[i];
		}
		Set(at, n);
	}

	/// <summary>What to run: lines of settings, written before the emulator's process starts.</summary>
	public string[] Config { get => GetText(ConfigAt).Split('\n'); set => SetText(ConfigAt, string.Join("\n", value)); }

	public string Error { get => GetText(ErrorAt); set => SetText(ErrorAt, value); }

	public string CommandPath { get => GetText(PathAt); set => SetText(PathAt, value); }

	/// <summary>A new picture (RGBA as XNA's Color packs it), in the buffer that isn't the latest nor the one before.</summary>
	public void PutFrame(uint[] pixels, int width, int height)
	{
		int count = width * height;
		if (count <= 0 || count > MaxPixels || count > pixels.Length)
		{
			return;
		}
		int next = (FrameIndex + 1) % 3;
		fixed (uint* source = pixels)
		{
			Buffer.MemoryCopy(source, _base + FramesAt + (long)next * MaxPixels * 4, (long)MaxPixels * 4, (long)count * 4);
		}
		FrameW = width;
		FrameH = height;
		FrameIndex = next;
		FrameSerial = FrameSerial + 1;
	}

	/// <summary>The latest picture, when it's newer than <paramref name="serial"/> (<paramref name="into"/> grows to fit).</summary>
	public bool TakeFrame(ref uint[] into, ref int serial, out int width, out int height)
	{
		int now = FrameSerial;
		width = FrameW;
		height = FrameH;
		if (now == serial || width <= 0 || height <= 0 || width * height > MaxPixels)
		{
			return false;
		}
		serial = now;
		int count = width * height;
		if (into == null || into.Length != count)
		{
			into = new uint[count];
		}
		int index = FrameIndex;
		fixed (uint* target = into)
		{
			Buffer.MemoryCopy(_base + FramesAt + (long)index * MaxPixels * 4, target, (long)count * 4, (long)count * 4);
		}
		return true;
	}

	/// <summary>Sound in (interleaved stereo); what doesn't fit is dropped.</summary>
	public void PutAudio(short[] samples, int frames)
	{
		int write = AudioWrite;
		int room = AudioFrames - 1 - (write - AudioRead);
		int n = Math.Min(frames, room);
		int* ring = (int*)(_base + AudioAt);
		fixed (short* source = samples)
		{
			int* from = (int*)source;
			for (int i = 0; i < n; i++)
			{
				ring[(write + i) & (AudioFrames - 1)] = from[i];
			}
		}
		AudioWrite = write + n;
	}

	/// <summary>Sound out: up to <paramref name="most"/> frames into <paramref name="samples"/>; how many came.</summary>
	public int TakeAudio(short[] samples, int most)
	{
		int read = AudioRead;
		int n = Math.Min(most, AudioWrite - read);
		if (n <= 0)
		{
			return 0;
		}
		int* ring = (int*)(_base + AudioAt);
		fixed (short* target = samples)
		{
			int* to = (int*)target;
			for (int i = 0; i < n; i++)
			{
				to[i] = ring[(read + i) & (AudioFrames - 1)];
			}
		}
		AudioRead = read + n;
		return n;
	}
}
