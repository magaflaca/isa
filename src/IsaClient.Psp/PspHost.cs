using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using IsaClient.Gba;

namespace IsaClient.Psp;

/// <summary>
/// The emulator's own process: this same executable started with <see cref="Argument"/>, with no window. PPSSPP can't live
/// inside Terraria: in 32 bits it needs its memory below 2 GB, which a running Terraria fills, and it only sets itself up
/// once per process. Here each game starts on an empty process, what the emulator uses doesn't count against Terraria,
/// and if it crashes Terraria goes on. Nothing of XNA or Terraria can be used from this class: none of it is loaded here.
/// </summary>
internal static class PspHost
{
	public const string Argument = "--isaclient-psp-host";

	[DllImport("winmm")]
	private static extern uint timeBeginPeriod(uint milliseconds);

	[StructLayout(LayoutKind.Sequential)]
	private struct Message
	{
		public IntPtr Window;
		public uint Id;
		public IntPtr WParam, LParam;
		public uint Time;
		public int X, Y;
	}

	[DllImport("user32", EntryPoint = "PeekMessageW")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PeekMessage(out Message message, IntPtr window, uint first, uint last, uint remove);

	[DllImport("user32", EntryPoint = "DispatchMessageW")]
	private static extern IntPtr DispatchMessage(ref Message message);

	private static PspLink _link;

	private static GbaMachine _machine;

	private static StreamWriter _log;

	private static int _logLines;

	private static readonly object LogLock = new object();

	/// <summary>Arguments: <see cref="Argument"/>, the link's name and the game's process id.</summary>
	public static void Run(string[] args)
	{
		try
		{
			_link = PspLink.Open(args[1]);
		}
		catch
		{
			return;
		}
		try
		{
			Process parent = Process.GetProcessById(int.Parse(args[2]));
			// the machine's clock sleeps in milliseconds: Windows' default step is 15
			timeBeginPeriod(1);
			Play(parent);
		}
		catch (Exception ex)
		{
			Fail(ex.Message);
		}
		finally
		{
			lock (LogLock)
			{
				_log?.Dispose();
				_log = null;
			}
		}
	}

	private static void Fail(string error)
	{
		Log(3, error);
		_link.Error = error;
		_link.State = PspLink.Failed;
	}

	private static void Log(int level, string text)
	{
		// a long session shouldn't fill the disk; the search for memory below is thousands of the same line
		if (level == 0 || _logLines > ((level >= 2) ? 20000 : 4000))
		{
			return;
		}
		lock (LogLock)
		{
			try
			{
				_log?.WriteLine(text);
				_logLines++;
			}
			catch
			{
			}
		}
	}

	private static void Play(Process parent)
	{
		// the lines of PspRemote.Start
		string[] c = _link.Config;
		string corePath = c[0];
		string system = c[1];
		string saves = c[2];
		string game = c[3];
		string statePath = c[4];
		string logPath = c[5];
		string language = c[6];
		int resolution = _link.Resolution;
		Directory.CreateDirectory(Path.GetDirectoryName(logPath));
		_log = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
		bool graphicsCard = LibretroGl.Prepare();
		Log(1, "Graphics: " + (graphicsCard ? LibretroGl.Renderer : ("software (" + LibretroGl.Problem + ")")));
		Log(1, "Game: " + game);
		Directory.CreateDirectory(Path.Combine(saves, "PSP", "SAVEDATA"));
		Dictionary<string, string> options = PspEmulator.Options(graphicsCard, resolution, language);
		Libretro core = GbaMachine.LoadCore(corePath, system, saves, options, x =>
		{
			x.UsesGraphicsCard = true;
			x.Utf8Paths = true;
			x.AnswerDefaults = true;
			x.Log = Log;
		});
		byte[] state = (statePath.Length > 0) ? File.ReadAllBytes(statePath) : null;
		GbaMachine m = GbaMachine.Start(core, null, game, null, state);
		_machine = m;
		m.AudioOut = _link.PutAudio;
		m.AudioQueued = () => _link.QueuedMs / 1000.0;
		uint[] pixels = null;
		int serial = -1;
		bool announced = false;
		int handled = 0;
		int shownResolution = resolution;
		int parentCheck = 0;
		while (true)
		{
			while (PeekMessage(out Message message, IntPtr.Zero, 0, 0, 1))
			{
				DispatchMessage(ref message);
			}
			if (!m.Running)
			{
				Fail(m.Error ?? core.LastError ?? "The emulator stopped.");
				m.Dispose();
				return;
			}
			if (!announced && m.WaitLoaded(0))
			{
				announced = true;
				_link.FpsMilli = (int)(m.Fps * 1000.0);
				_link.SampleRate = (int)m.SampleRate;
				_link.OnGraphicsCard = core.OnGraphicsCard;
				_link.State = PspLink.Running;
			}
			m.Buttons = _link.Buttons;
			m.AnalogX = _link.AnalogX;
			m.AnalogY = _link.AnalogY;
			m.Paused = _link.Paused;
			m.Speed = Math.Max(1, _link.Speed);
			if (m.CopyFrame(ref pixels, ref serial, out int w, out int h))
			{
				_link.PutFrame(pixels, w, h);
			}
			_link.FrameCount = (int)m.FrameCount;
			int wanted = _link.Resolution;
			if (wanted != shownResolution)
			{
				shownResolution = wanted;
				core.SetOption("ppsspp_internal_resolution", PspEmulator.ResolutionText(wanted));
			}
			int command = _link.CommandSerial;
			if (command != handled)
			{
				handled = command;
				if (Handle(m, command))
				{
					return;
				}
			}
			if (++parentCheck >= 250)
			{
				parentCheck = 0;
				if (parent.HasExited)
				{
					m.Dispose();
					return;
				}
			}
			Thread.Sleep(2);
		}
	}

	private static void Done(int serial, bool ok)
	{
		_link.DoneOk = ok;
		_link.DoneSerial = serial;
	}

	/// <summary>A command from the game. True when it was to stop.</summary>
	private static bool Handle(GbaMachine m, int serial)
	{
		string path = _link.CommandPath;
		switch (_link.CommandKind)
		{
		case PspLink.CommandSave:
			m.Snapshot((state, sram, frame, width) => Done(serial, Keep(path, state, frame, width)));
			break;
		case PspLink.CommandLoad:
			m.LoadState(File.ReadAllBytes(path), ok => Done(serial, ok));
			break;
		case PspLink.CommandReset:
			m.Reset();
			Done(serial, true);
			break;
		case PspLink.CommandStop:
			if (path.Length > 0 && m.FrameSerial >= 8)
			{
				using ManualResetEventSlim saved = new ManualResetEventSlim();
				m.Snapshot((state, sram, frame, width) =>
				{
					Keep(path, state, frame, width);
					saved.Set();
				});
				saved.Wait(10000);
			}
			m.Dispose();
			Done(serial, true);
			_link.State = PspLink.Stopped;
			return true;
		}
		return false;
	}

	/// <summary>A moment on disk, with its picture.</summary>
	private static bool Keep(string path, byte[] state, uint[] frame, int width)
	{
		if (state == null)
		{
			return false;
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllBytes(path + ".tmp", state);
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			File.Move(path + ".tmp", path);
			Picture(path + ".png", frame, width);
			return true;
		}
		catch (Exception ex)
		{
			Log(3, "Couldn't keep the moment: " + ex.Message);
			return false;
		}
	}

	private static void Picture(string path, uint[] frame, int width)
	{
		if (frame == null || width <= 0 || frame.Length < width)
		{
			return;
		}
		int height = frame.Length / width;
		using Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
		BitmapData data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
		int[] row = new int[width];
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				uint p = frame[y * width + x];
				// XNA packs R in the low byte; GDI wants 0xAARRGGBB
				row[x] = unchecked((int)(0xFF000000u | ((p & 0xFF) << 16) | (p & 0xFF00) | ((p >> 16) & 0xFF)));
			}
			Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, width);
		}
		bmp.UnlockBits(data);
		bmp.Save(path, ImageFormat.Png);
	}
}
