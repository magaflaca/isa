using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace IsaClient.Gba;

/// <summary>
/// A running console (Game Boy Advance, Nintendo DS or PSP): a libretro core on its own thread at the console's frame
/// rate, the latest picture (RGBA, ready for a texture; the DS gives both screens, one above the other), sound handed to
/// <see cref="AudioOut"/>, the buttons and the touch screen, and the cartridge's save written to disk whenever the game
/// saves. Knows nothing about the game's window or XNA.
/// </summary>
public sealed class GbaMachine : IDisposable
{
	/// <summary>The Game Boy Advance's screen.</summary>
	public const int Width = 240;

	public const int Height = 160;

	/// <summary>The core of the machine running now (the callbacks are static: one machine at a time).</summary>
	private static Libretro _core;

	/// <summary>The GBA core (<see cref="EnsureCore"/>).</summary>
	private static Libretro _gbaCore;

	private static readonly Dictionary<string, Libretro> Cores = new Dictionary<string, Libretro>(StringComparer.OrdinalIgnoreCase);

	private static readonly object CoreLock = new object();

	private Libretro _lib;

	private Thread _thread;

	private volatile bool _running;

	private readonly ConcurrentQueue<Action> _jobs = new ConcurrentQueue<Action>();

	private uint[] _back = new uint[Width * Height];

	private uint[] _front = new uint[Width * Height];

	private int _frontW = Width;

	private int _frontH = Height;

	/// <summary>Size of the latest picture (240x160 for the GBA; 256x384 for the DS, top screen above).</summary>
	public int FrameWidth => _frontW;

	public int FrameHeight => _frontH;

	private readonly object _frameLock = new object();

	private int _frameSerial;

	private string _savePath;

	private byte[] _lastSram;

	private int _sramChangedAt = -1;

	private long _frames;

	private long _lastSramCheck;

	private readonly ManualResetEventSlim _loaded = new ManualResetEventSlim();

	/// <summary>Waits until the game is in the core (its frame and sound rates are known then).</summary>
	public bool WaitLoaded(int milliseconds) => _loaded.Wait(milliseconds);

	/// <summary>Pressed buttons as libretro joypad bits (<see cref="Libretro.A"/>...). Set from any thread.</summary>
	public volatile int Buttons;

	/// <summary>The stylus on the picture, -32767..32767 across the whole frame (both DS screens).</summary>
	public volatile int PointerX;

	public volatile int PointerY;

	public volatile bool Touching;

	/// <summary>The left analog stick, -32767..32767 (the PSP's nub).</summary>
	public volatile int AnalogX;

	public volatile int AnalogY;

	public volatile bool Paused;

	/// <summary>Frames run per real frame (1 normal, 2-4 fast forward; sound plays only at 1).</summary>
	public volatile int Speed = 1;

	/// <summary>Sound out: interleaved stereo 16-bit samples, count of frames (pairs). Called on the machine's thread.</summary>
	public Action<short[], int> AudioOut;

	/// <summary>Seconds of sound queued in the output, to keep picture and sound together (null: by the clock only).</summary>
	public Func<double> AudioQueued;

	public string CoreName => _lib?.Name + " " + _lib?.Version;

	public double Fps => _lib?.Fps ?? 59.7275;

	public double SampleRate => _lib?.SampleRate ?? 32768.0;

	public long FrameCount => Interlocked.Read(ref _frames);

	/// <summary>Grows by one with every new picture.</summary>
	public int FrameSerial => Volatile.Read(ref _frameSerial);

	/// <summary>The last error on the machine's thread (it stops on errors).</summary>
	public string Error { get; private set; }

	/// <summary>When the cartridge's save was last written to disk.</summary>
	public DateTime LastSaved { get; private set; }

	public bool Running => _running;

	/// <summary>Loads the GBA core from <paramref name="corePath"/> the first time.</summary>
	public static void EnsureCore(string corePath, string systemDir)
	{
		_gbaCore = LoadCore(corePath, systemDir, systemDir, null);
	}

	/// <summary>Loads a core the first time (later calls give the same one); <paramref name="options"/> are its settings.</summary>
	/// <param name="prepare">Sets the core up before it starts (only the first time).</param>
	internal static Libretro LoadCore(string corePath, string systemDir, string saveDir, IDictionary<string, string> options, Action<Libretro> prepare = null)
	{
		lock (CoreLock)
		{
			if (Cores.TryGetValue(corePath, out Libretro loaded))
			{
				return loaded;
			}
			Directory.CreateDirectory(systemDir);
			Directory.CreateDirectory(saveDir);
			Libretro core = new Libretro(corePath);
			if (options != null)
			{
				foreach (KeyValuePair<string, string> o in options)
				{
					core.Options[o.Key] = o.Value;
				}
			}
			prepare?.Invoke(core);
			core.Init(systemDir, saveDir, OnVideo, OnAudioBatch, OnAudioSample, OnInput);
			Cores[corePath] = core;
			return core;
		}
	}

	private static GbaMachine _active;

	/// <summary>Starts a game. Only one machine runs at a time (the core is one per process).</summary>
	/// <param name="sram">The cartridge's save to put in instead of the file's (a save slot being loaded).</param>
	public static GbaMachine Start(byte[] rom, string savePath, byte[] state = null, byte[] sram = null)
	{
		if (_gbaCore == null)
		{
			throw new InvalidOperationException("The GBA core isn't loaded");
		}
		return Start(_gbaCore, rom, "game.gba", savePath, state, sram);
	}

	/// <summary>Starts a game on <paramref name="core"/> (<paramref name="rom"/> may be null when the core reads <paramref name="romPath"/>).</summary>
	/// <param name="savePath">Where the cartridge's save goes (null when the core writes its own saves).</param>
	internal static GbaMachine Start(Libretro core, byte[] rom, string romPath, string savePath, byte[] state = null, byte[] sram = null)
	{
		_active?.Dispose();
		_core = core;
		GbaMachine m = new GbaMachine { _savePath = savePath, _lib = core };
		// a roomy stack: the DS emulators go deeper than mGBA
		m._thread = new Thread(() => m.Loop(rom, romPath, state, sram), 16 * 1024 * 1024) { IsBackground = true, Name = "IsaClient console", Priority = ThreadPriority.AboveNormal };
		m._running = true;
		_active = m;
		m._thread.Start();
		return m;
	}

	/// <summary>Runs <paramref name="job"/> on the machine's thread between two frames.</summary>
	public void Queue(Action job) => _jobs.Enqueue(job);

	/// <summary>Copies the latest picture (RGBA as XNA's Color packs it) when it's newer than <paramref name="serial"/>.</summary>
	public bool CopyFrame(uint[] into, ref int serial)
	{
		lock (_frameLock)
		{
			if (_frameSerial == serial)
			{
				return false;
			}
			serial = _frameSerial;
			Array.Copy(_front, into, Math.Min(_front.Length, into.Length));
			return true;
		}
	}

	/// <summary>Like <see cref="CopyFrame(uint[], ref int)"/> for pictures of any size (<paramref name="into"/> grows to fit).</summary>
	public bool CopyFrame(ref uint[] into, ref int serial, out int width, out int height)
	{
		lock (_frameLock)
		{
			width = _frontW;
			height = _frontH;
			if (_frameSerial == serial)
			{
				return false;
			}
			serial = _frameSerial;
			int n = width * height;
			if (into == null || into.Length != n)
			{
				into = new uint[n];
			}
			Array.Copy(_front, into, Math.Min(n, _front.Length));
			return true;
		}
	}

	private void Loop(byte[] rom, string romPath, byte[] state, byte[] sram)
	{
		try
		{
			lock (CoreLock)
			{
				if (!_lib.LoadGame(rom, romPath))
				{
					throw new InvalidOperationException("The core couldn't load this ROM");
				}
				int most = _lib.MaxWidth * _lib.MaxHeight;
				if (most > _back.Length)
				{
					lock (_frameLock)
					{
						_back = new uint[most];
						_front = new uint[most];
					}
				}
				LoadSram();
				if (sram != null)
				{
					PutSram(sram);
				}
				if (state != null)
				{
					_lib.Unserialize(state);
				}
			}
			_loaded.Set();
			Stopwatch clock = Stopwatch.StartNew();
			double next = 0.0;
			while (_running)
			{
				while (_jobs.TryDequeue(out Action job))
				{
					lock (CoreLock)
					{
						job();
					}
				}
				double frameTime = 1.0 / Fps;
				double now = clock.Elapsed.TotalSeconds;
				if (Paused)
				{
					next = now;
					Thread.Sleep(10);
					continue;
				}
				if (now < next)
				{
					double wait = (next - now) * 1000.0;
					if (wait > 2.0)
					{
						Thread.Sleep((int)(wait - 1.0));
					}
					else
					{
						Thread.Sleep(0);
					}
					continue;
				}
				int speed = Math.Max(1, Math.Min(8, Speed));
				lock (CoreLock)
				{
					for (int k = 0; k < speed && _running; k++)
					{
						_mute = k > 0 || speed > 1;
						_lib.Run();
						if (_lib.ShutdownAsked)
						{
							throw new InvalidOperationException(_lib.LastError ?? "The game stopped.");
						}
						Interlocked.Increment(ref _frames);
					}
				}
				next += frameTime;
				// Keep the sound fed without drifting: a little faster when it runs dry, slower when it piles up.
				double queued = (speed == 1 && AudioQueued != null) ? AudioQueued() : -1.0;
				if (queued >= 0.0)
				{
					if (queued < 0.035)
					{
						next -= frameTime * 0.3;
					}
					else if (queued > 0.11)
					{
						next += frameTime * 0.3;
					}
				}
				if (now - next > 0.2)
				{
					next = now;
				}
				if (_frames - _lastSramCheck >= 30)
				{
					_lastSramCheck = _frames;
					CheckSram(false);
				}
			}
			lock (CoreLock)
			{
				CheckSram(true);
				_lib.UnloadGame();
			}
		}
		catch (Exception ex)
		{
			Error = ex.Message;
			_running = false;
			_loaded.Set();
			try
			{
				lock (CoreLock)
				{
					_lib.UnloadGame();
				}
			}
			catch
			{
			}
		}
	}

	#region The cartridge's save

	/// <summary>Puts a whole cartridge save in the core; it goes to the file once it settles, like a game save.</summary>
	private void PutSram(byte[] data)
	{
		IntPtr p = _lib.SaveRam(out int size);
		if (p == IntPtr.Zero || size <= 0 || data == null)
		{
			return;
		}
		Marshal.Copy(data, 0, p, Math.Min(size, data.Length));
		_lastSram = null;
	}

	private byte[] CopySram()
	{
		IntPtr p = _lib.SaveRam(out int size);
		if (p == IntPtr.Zero || size <= 0)
		{
			return null;
		}
		byte[] b = new byte[size];
		Marshal.Copy(p, b, 0, size);
		return b;
	}

	private void LoadSram()
	{
		IntPtr p = _lib.SaveRam(out int size);
		if (p == IntPtr.Zero || size <= 0)
		{
			return;
		}
		if (File.Exists(_savePath))
		{
			byte[] file = File.ReadAllBytes(_savePath);
			Marshal.Copy(file, 0, p, Math.Min(size, file.Length));
		}
		_lastSram = new byte[size];
		Marshal.Copy(p, _lastSram, 0, size);
	}

	/// <summary>Writes the save once it stopped changing for a second (a game saves over many frames).</summary>
	private void CheckSram(bool now)
	{
		IntPtr p = _lib.SaveRam(out int size);
		if (p == IntPtr.Zero || size <= 0 || _savePath == null)
		{
			return;
		}
		byte[] cur = new byte[size];
		Marshal.Copy(p, cur, 0, size);
		bool changed = _lastSram == null || !Same(cur, _lastSram);
		if (changed)
		{
			_lastSram = cur;
			_sramChangedAt = (int)_frames;
			_dirty = true;
		}
		if (_dirty && (now || _frames - _sramChangedAt >= 60))
		{
			WriteSave(cur);
			_dirty = false;
		}
	}

	private bool _dirty;

	private void WriteSave(byte[] data)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_savePath));
			string tmp = _savePath + ".tmp";
			File.WriteAllBytes(tmp, data);
			if (File.Exists(_savePath))
			{
				// one backup of the previous save, in case something goes wrong mid-way
				File.Copy(_savePath, _savePath + ".bak", true);
				File.Delete(_savePath);
			}
			File.Move(tmp, _savePath);
			LastSaved = DateTime.Now;
		}
		catch (Exception ex)
		{
			Error = "Couldn't write the save: " + ex.Message;
		}
	}

	private static bool Same(byte[] a, byte[] b)
	{
		if (a.Length != b.Length)
		{
			return false;
		}
		for (int i = 0; i < a.Length; i++)
		{
			if (a[i] != b[i])
			{
				return false;
			}
		}
		return true;
	}

	#endregion

	#region States

	/// <summary>The whole machine as bytes (null if it failed). Runs on the machine's thread; <paramref name="done"/> gets it there.</summary>
	public void SaveState(Action<byte[]> done) => Queue(() => done(_lib.Serialize()));

	public void LoadState(byte[] state, Action<bool> done = null) => Queue(() =>
	{
		bool ok = _lib.Unserialize(state);
		done?.Invoke(ok);
	});

	public void Reset() => Queue(() => _lib.Reset());

	/// <summary>Everything for a save slot: the state, the cartridge save and the picture on screen and its width (machine thread).</summary>
	public void Snapshot(Action<byte[], byte[], uint[], int> done) => Queue(() =>
	{
		byte[] state = _lib.Serialize();
		byte[] sram = CopySram();
		uint[] frame;
		int width;
		lock (_frameLock)
		{
			width = _frontW;
			frame = new uint[_frontW * _frontH];
			Array.Copy(_front, frame, frame.Length);
		}
		done(state, sram, frame, width);
	});

	/// <summary>Back to a save slot: its cartridge save, then its state.</summary>
	public void Restore(byte[] state, byte[] sram, Action<bool> done = null) => Queue(() =>
	{
		if (sram != null)
		{
			PutSram(sram);
		}
		bool ok = _lib.Unserialize(state);
		done?.Invoke(ok);
	});

	#endregion

	#region Core callbacks (on the machine's thread)

	[ThreadStatic]
	private static bool _mute;

	private static short[] _audioBuf = new short[8192];

	private static void OnVideo(IntPtr data, uint width, uint height, UIntPtr pitch)
	{
		GbaMachine m = _active;
		if (m == null || data == IntPtr.Zero || width == 0)
		{
			return;
		}
		int w = (int)width;
		int h = (int)height;
		if (data == HardwareFrame)
		{
			m.TakeHardwareFrame(w, h);
			return;
		}
		if (w * h > m._back.Length)
		{
			// bigger than the core said: keep the top left part
			w = Math.Min(w, m._lib.MaxWidth);
			h = Math.Min(h, m._back.Length / w);
		}
		int stride = (int)pitch;
		uint[] back = m._back;
		unsafe
		{
			byte* src = (byte*)data;
			if (_core.PixelFormat == 1)
			{
				for (int y = 0; y < h; y++)
				{
					uint* row = (uint*)(src + y * stride);
					int o = y * w;
					for (int x = 0; x < w; x++)
					{
						uint p = row[x];
						back[o + x] = 0xFF000000u | ((p & 0xFF) << 16) | (p & 0xFF00) | ((p >> 16) & 0xFF);
					}
				}
			}
			else
			{
				bool rgb565 = _core.PixelFormat == 2;
				for (int y = 0; y < h; y++)
				{
					ushort* row = (ushort*)(src + y * stride);
					int o = y * w;
					for (int x = 0; x < w; x++)
					{
						uint p = row[x];
						uint r;
						uint g;
						uint b;
						if (rgb565)
						{
							r = (p >> 11) & 31;
							g = (p >> 5) & 63;
							b = p & 31;
							g = (g << 2) | (g >> 4);
						}
						else
						{
							r = (p >> 10) & 31;
							g = (p >> 5) & 31;
							b = p & 31;
							g = (g << 3) | (g >> 2);
						}
						r = (r << 3) | (r >> 2);
						b = (b << 3) | (b >> 2);
						back[o + x] = 0xFF000000u | (b << 16) | (g << 8) | r;
					}
				}
			}
		}
		m.Publish(back, w, h);
	}

	/// <summary>RETRO_HW_FRAME_BUFFER_VALID: the picture is on the graphics card.</summary>
	private static readonly IntPtr HardwareFrame = new IntPtr(-1);

	private void TakeHardwareFrame(int width, int height)
	{
		uint[] back = _back;
		if (_lib.ReadGraphicsCard(width, height, ref back, out int w, out int h))
		{
			Publish(back, w, h);
		}
		else
		{
			_back = back;
		}
	}

	/// <summary>The back buffer becomes the latest picture.</summary>
	private void Publish(uint[] back, int width, int height)
	{
		lock (_frameLock)
		{
			_back = _front;
			_front = back;
			_frontW = width;
			_frontH = height;
			_frameSerial++;
		}
	}

	private static UIntPtr OnAudioBatch(IntPtr data, UIntPtr frames)
	{
		int n = (int)frames;
		GbaMachine m = _active;
		if (m?.AudioOut == null || _mute || n <= 0)
		{
			return frames;
		}
		if (_audioBuf.Length < n * 2)
		{
			_audioBuf = new short[n * 2];
		}
		Marshal.Copy(data, _audioBuf, 0, n * 2);
		m.AudioOut(_audioBuf, n);
		return frames;
	}

	private static void OnAudioSample(short left, short right)
	{
		GbaMachine m = _active;
		if (m?.AudioOut == null || _mute)
		{
			return;
		}
		m.AudioOut(new[] { left, right }, 1);
	}

	private static short OnInput(uint port, uint device, uint index, uint id)
	{
		GbaMachine m = _active;
		if (m == null || port != 0)
		{
			return 0;
		}
		if (device == Libretro.DevicePointer)
		{
			if (index != 0)
			{
				return 0;
			}
			return id switch
			{
				0 => (short)m.PointerX,
				1 => (short)m.PointerY,
				2 => (short)(m.Touching ? 1 : 0),
				_ => 0
			};
		}
		if (device == Libretro.DeviceAnalog)
		{
			if (index != 0)
			{
				return 0;
			}
			return (short)((id == 0) ? m.AnalogX : (id == 1) ? m.AnalogY : 0);
		}
		if (device != Libretro.DeviceJoypad)
		{
			return 0;
		}
		int buttons = m.Buttons;
		if (id == 256)
		{
			return (short)buttons;
		}
		return (short)((buttons >> (int)id) & 1);
	}

	#endregion

	/// <summary>Stops the machine (the save is written first). Waits for its thread.</summary>
	public void Dispose()
	{
		if (!_running && _thread == null)
		{
			return;
		}
		_running = false;
		try
		{
			_thread?.Join(3000);
		}
		catch
		{
		}
		_thread = null;
		if (_active == this)
		{
			_active = null;
		}
	}
}
