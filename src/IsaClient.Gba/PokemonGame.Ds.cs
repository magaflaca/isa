using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Gba;

/// <summary>How the DS's two screens are shown.</summary>
public enum DsLayout
{
	/// <summary>One screen, big and 4:3 like any game; the other one small at the side (the swap key trades them).</summary>
	Single,

	/// <summary>Both, one above the other, like the console.</summary>
	Stacked,

	/// <summary>Both, side by side.</summary>
	SideBySide
}

/// <summary>
/// Nintendo DS games in the same console: the emulator (melonDS or DeSmuME) isn't in IsaClient, it's downloaded once
/// the first time (<see cref="DsDownload"/>). The DS saves its games itself in IsaClient/GBA/Saves/DS; the continue
/// state and the save slots work as for the GBA (per emulator, since their states don't mix).
/// </summary>
public static partial class PokemonGame
{
	/// <summary>The game in the console is a DS game.</summary>
	public static bool IsDs { get; private set; }

	public static DsLayout DsLayout = DsLayout.Single;

	/// <summary>The touch screen is the main one (big in <see cref="DsLayout.Single"/>, first in the others).</summary>
	public static bool DsSwap;

	/// <summary>"melonds" or "desmume".</summary>
	public static string DsEngineId = DsEngine.MelonDS.Id;

	public static DsEngine Engine => DsEngine.ById(DsEngineId);

	private static bool _swapHeld;

	/// <summary>Emulated speed in % of the real DS over the last second (100 when it keeps up; GBA too).</summary>
	public static int SpeedPercent { get; private set; } = 100;

	/// <summary>Seconds in a row it ran below 90%.</summary>
	public static int SlowSeconds { get; private set; }

	private static readonly Stopwatch _speedClock = Stopwatch.StartNew();

	private static long _speedFrames = -1;

	private static double _speedAt;

	private static DateTime _dsStartedAt;

	public static string DsSaveFolder => Path.Combine(Folder, "Saves", "DS");

	/// <summary>Where the emulator keeps the game's own save (named after the ROM file).</summary>
	private static string DsSaveFile
	{
		get
		{
			string ext = (Engine == DsEngine.DeSmuME) ? ".dsv" : ".sav";
			return Path.Combine(DsSaveFolder, Path.GetFileNameWithoutExtension(RomPath ?? "") + ext);
		}
	}

	private static bool DsHasSave
	{
		get
		{
			try
			{
				return File.Exists(DsSaveFile);
			}
			catch
			{
				return false;
			}
		}
	}

	/// <summary>When the DS game last saved itself while playing (default when it hasn't).</summary>
	public static DateTime DsLastSaved
	{
		get
		{
			try
			{
				string f = DsSaveFile;
				if (File.Exists(f))
				{
					DateTime t = File.GetLastWriteTime(f);
					return (t > _dsStartedAt) ? t : default(DateTime);
				}
			}
			catch
			{
			}
			return default(DateTime);
		}
	}

	/// <summary>The firmware language from Terraria's (multi-language games then start in it).</summary>
	internal static string DsLanguage
	{
		get
		{
			string name = "";
			try
			{
				name = Terraria.Localization.Language.ActiveCulture?.Name ?? "";
			}
			catch
			{
			}
			if (name.StartsWith("es"))
			{
				return "Spanish";
			}
			if (name.StartsWith("fr"))
			{
				return "French";
			}
			if (name.StartsWith("de"))
			{
				return "German";
			}
			if (name.StartsWith("it"))
			{
				return "Italian";
			}
			if (name.StartsWith("ja"))
			{
				return "Japanese";
			}
			return "English";
		}
	}

	/// <summary>Turns a DS game on (the emulator must be downloaded: the window offers it first).</summary>
	private static void StartDs(bool resume)
	{
		Problem = null;
		if (!Engine.Installed)
		{
			if (!DsDownload.Busy)
			{
				DsDownload.Start(Engine, () => StartDs(resume));
			}
			return;
		}
		try
		{
			byte[] state = null;
			if (resume && HasResume)
			{
				state = File.ReadAllBytes(StatePath("resume"));
			}
			LaunchDs(state, null);
		}
		catch (Exception ex)
		{
			Problem = "Couldn't start the game: " + ex.Message;
			ClientLog.LogException(ex);
		}
	}

	/// <summary>Starts the DS emulator on the game (at <paramref name="state"/>'s moment when given).</summary>
	private static void LaunchDs(byte[] state, byte[] sram)
	{
		Problem = null;
		DsEngine engine = Engine;
		if (!engine.Installed)
		{
			StartDs(false);
			return;
		}
		if (!File.Exists(RomPath))
		{
			Problem = "The game's file isn't there anymore.";
			return;
		}
		try
		{
			StopMachine(false);
			Libretro core = GbaMachine.LoadCore(engine.Path, Path.Combine(Folder, "System"), DsSaveFolder, engine.Options(DsLanguage));
			// most DS emulators read the file themselves; the others get it in memory
			byte[] rom = core.NeedFullPath ? null : File.ReadAllBytes(RomPath);
			GbaMachine m = GbaMachine.Start(core, rom, RomPath, null, state, sram);
			_runningPal = null;
			_machine = m;
			_serial = -1;
			_dsStartedAt = DateTime.Now;
			SlowSeconds = 0;
			SpeedPercent = 100;
			_speedFrames = -1;
			// the sound starts once the game is in (its rate is known then), without stopping the game meanwhile
			Task.Run(() =>
			{
				m.WaitLoaded(60000);
				Main.QueueMainThreadAction(() =>
				{
					if (_machine == m && m.Running)
					{
						Attach(m);
					}
				});
			});
		}
		catch (Exception ex)
		{
			Problem = "Couldn't start the DS emulator: " + ex.Message;
			ClientLog.LogException(ex);
		}
	}

	/// <summary>Chooses the DS emulator (the console turns off first: each one keeps its own moments and saves).</summary>
	public static void SetEngine(DsEngine engine)
	{
		if (engine == Engine)
		{
			return;
		}
		if (Playing && IsDs)
		{
			Stop();
		}
		DsEngineId = engine.Id;
		GbaSaves.Forget();
		GbaConfig.Save();
	}

	public static void SwapScreens()
	{
		DsSwap = !DsSwap;
		GbaConfig.Save();
	}

	public static void SetLayout(DsLayout layout)
	{
		DsLayout = layout;
		GbaConfig.Save();
	}

	/// <summary>
	/// The stylus from the window: <paramref name="u"/>, <paramref name="v"/> are 0..1 across the touch screen. The
	/// emulator gets it across its whole picture (both screens, the touch screen below).
	/// </summary>
	public static void Touch(bool down, float u, float v)
	{
		GbaMachine m = Machine;
		if (m == null)
		{
			return;
		}
		if (down)
		{
			u = Math.Max(0f, Math.Min(1f, u));
			v = Math.Max(0f, Math.Min(1f, v));
			m.PointerX = (int)Math.Round(u * 65534.0 - 32767.0);
			m.PointerY = (int)Math.Round((0.5 + v * 0.5) * 65534.0 - 32767.0);
		}
		m.Touching = down;
	}

	/// <summary>Once a second: how fast the game really runs (the DS can be heavy for an old PC).</summary>
	private static void MeasureSpeed(GbaMachine m)
	{
		double now = _speedClock.Elapsed.TotalSeconds;
		if (m.Paused || m.Speed != 1 || _speedFrames < 0)
		{
			_speedFrames = m.FrameCount;
			_speedAt = now;
			return;
		}
		if (now - _speedAt < 1.0)
		{
			return;
		}
		long frames = m.FrameCount - _speedFrames;
		SpeedPercent = (int)Math.Round(100.0 * frames / ((now - _speedAt) * m.Fps));
		SlowSeconds = (SpeedPercent < 90) ? (SlowSeconds + 1) : 0;
		_speedFrames = m.FrameCount;
		_speedAt = now;
	}
}
