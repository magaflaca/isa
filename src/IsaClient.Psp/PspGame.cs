using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using IsaClient.Core;
using IsaClient.Gba;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;

namespace IsaClient.Psp;

/// <summary>
/// PSP games inside Terraria: the user's own games and PPSSPP, which is downloaded the first time
/// (<see cref="PspEmulator"/>) and runs in a process of its own (<see cref="PspHost"/>, reached through
/// <see cref="PspRemote"/>). It draws on the graphics card and keeps the games' own saves on its memory stick,
/// IsaClient/PSP/Saves. Turning it off keeps the moment to go on from it, away from the main thread. No scores.
/// </summary>
public static class PspGame
{
	/// <summary>The screen's shape in the window: 240x136 times the zoom, so 2x is the PSP's own 480x272.</summary>
	public const int ScreenW = 240;

	public const int ScreenH = 136;

	public static string RomPath = "";

	/// <summary>Screen zoom (2 to 4), 3 by default like the other consoles.</summary>
	public static int Scale = 3;

	public static int Volume = 70;

	/// <summary>Opening a game goes on from where it was closed.</summary>
	public static bool Resume = true;

	/// <summary>How many times the PSP's resolution the game is drawn at (1 to 3): sharper for a little more work.</summary>
	public static int Resolution = 2;

	public static string Folder => Path.Combine(ClientSettings.IsaClientFolder, "PSP");

	public static string SaveFolder => Path.Combine(Folder, "Saves");

	/// <summary>Last problem to show in the window (null when all is fine).</summary>
	public static string Problem;

	public static string Toast;

	public static double ToastAt = -10.0;

	/// <summary>Buttons held this frame (libretro bits) and the stick, for the window's drawing.</summary>
	public static int Held;

	public static Vector2 Stick;

	/// <summary>Emulated speed in % of a real PSP over the last second.</summary>
	public static int SpeedPercent { get; private set; } = 100;

	/// <summary>Seconds in a row it ran below 90%.</summary>
	public static int SlowSeconds { get; private set; }

	private static PspRemote _machine;

	private static Texture2D _screen;

	private static uint[] _pixels;

	private static int _serial = -1;

	private static bool _starting;

	private static Task _stopping;

	private static readonly Stopwatch SpeedClock = Stopwatch.StartNew();

	private static long _speedFrames = -1;

	private static double _speedAt;

	internal static PspRemote Machine => (_machine != null && _machine.Running) ? _machine : null;

	public static bool Playing => Machine != null;

	/// <summary>Turning on: the game is on its way and no picture has come yet.</summary>
	public static bool Booting => _starting || (Machine != null && Machine.FrameSerial < 8);

	/// <summary>The moment is still being written after turning it off.</summary>
	public static bool Stopping => _stopping != null && !_stopping.IsCompleted;

	public static Texture2D Screen => _screen;

	/// <summary>The game draws on the graphics card (false: on the processor, or nothing is running).</summary>
	public static bool OnGraphicsCard => _machine != null && _machine.OnGraphicsCard;

	public static bool HasGame
	{
		get
		{
			PspConfig.EnsureLoaded();
			return !string.IsNullOrEmpty(RomPath);
		}
	}

	/// <summary>The game in the console.</summary>
	public static PspEntry Current => HasGame ? PspLibrary.Entry(RomPath) : null;

	public static string Title => Current?.Title ?? "";

	/// <summary>The console is on, playing and not muted (Terraria goes quiet then).</summary>
	public static bool SoundOn
	{
		get
		{
			PspRemote m = Machine;
			return m != null && !m.Paused && Volume > 0;
		}
	}

	private static string StatesFolder => Path.Combine(Folder, "States");

	public static string StatePath(string key, string slot) => Path.Combine(StatesFolder, key + "." + slot + ".state");

	/// <summary>The moment the game was left at (null if there's none).</summary>
	public static string ResumeFile
	{
		get
		{
			PspEntry e = Current;
			if (e == null || e.Reading)
			{
				return null;
			}
			string path = StatePath(e.Key, "resume");
			return File.Exists(path) ? path : null;
		}
	}

	public static bool HasResume => ResumeFile != null;

	/// <summary>The game has saved something on the memory stick.</summary>
	public static bool HasSave
	{
		get
		{
			PspEntry e = Current;
			if (e == null || e.DiscId.Length == 0)
			{
				return false;
			}
			try
			{
				string data = Path.Combine(SaveFolder, "PSP", "SAVEDATA");
				return Directory.Exists(data) && Directory.GetDirectories(data, e.DiscId.Replace("-", "") + "*").Length > 0;
			}
			catch
			{
				return false;
			}
		}
	}

	public static void ShowToast(string text)
	{
		Toast = text;
		ToastAt = IsaClient.Games.GameArt.Now;
	}

	public static void SetVolume(int v)
	{
		Volume = Math.Max(0, Math.Min(100, v));
		_machine?.SetVolume(Volume);
	}

	/// <summary>Changes how sharp the game is drawn, right away if it's playing.</summary>
	public static void SetResolution(int times)
	{
		Resolution = Math.Max(1, Math.Min(3, times));
		PspConfig.Save();
		_machine?.SetResolution(Resolution);
	}

	/// <summary>Makes <paramref name="path"/> the game in the console (the one playing is turned off first).</summary>
	public static void SelectGame(string path)
	{
		if (string.Equals(path, RomPath, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		Stop();
		PspSaves.Forget();
		RomPath = path;
		Problem = null;
		PspConfig.Save();
	}

	/// <summary>Takes a game out of the library (its saves stay on disk, and so does its file).</summary>
	public static void RemoveGame(string path)
	{
		if (string.Equals(path, RomPath, StringComparison.OrdinalIgnoreCase))
		{
			Stop();
			RomPath = "";
			PspSaves.Forget();
		}
		PspConfig.Games.RemoveAll(g => string.Equals(g, path, StringComparison.OrdinalIgnoreCase));
		PspLibrary.Forget(path);
		if (RomPath.Length == 0 && PspConfig.Games.Count > 0)
		{
			RomPath = PspConfig.Games[0];
		}
		PspConfig.Save();
	}

	#region Turning it on

	/// <summary>Turns the console on (from the moment it was left when <paramref name="resume"/>).</summary>
	public static void Start(bool resume)
	{
		string state = resume ? ResumeFile : null;
		StartFrom(state);
	}

	/// <summary>Turns the console on at the moment in <paramref name="statePath"/> (null: from the start).</summary>
	public static void StartFrom(string statePath)
	{
		if (Main.gameMenu || !HasGame || _starting)
		{
			return;
		}
		Problem = null;
		if (!PspEmulator.Installed)
		{
			if (!PspEmulator.Busy)
			{
				PspEmulator.Download(() => StartFrom(statePath));
			}
			return;
		}
		string game = RomPath;
		if (!File.Exists(game))
		{
			Problem = "The game's file isn't there anymore.";
			return;
		}
		_starting = true;
		Task previous = _stopping ?? Task.CompletedTask;
		// the last game may still be being turned off
		previous.ContinueWith(_ => Main.QueueMainThreadAction(() =>
		{
			_starting = false;
			// closed, or another game chosen, while it was on its way: nothing to start
			if (PspWindow.IsOpen && string.Equals(game, RomPath, StringComparison.OrdinalIgnoreCase))
			{
				Launch(game, statePath);
			}
		}));
	}

	private static void Launch(string game, string statePath)
	{
		try
		{
			_machine = PspRemote.Start(game, statePath, CoreLogPath, Resolution, PokemonGame.DsLanguage, Volume);
			_serial = -1;
			SlowSeconds = 0;
			SpeedPercent = 100;
			_speedFrames = -1;
		}
		catch (Exception ex)
		{
			Problem = "Couldn't start the PSP emulator: " + ex.Message;
			ClientLog.LogException(ex);
		}
	}

	public static void Reset()
	{
		Machine?.Reset();
		ShowToast("Reset");
	}

	#endregion

	#region Turning it off

	/// <summary>Turns it off keeping the moment. It goes on being written in the background.</summary>
	public static void Stop()
	{
		PspRemote m = _machine;
		if (m != null)
		{
			_stopping = Shutdown(m);
		}
	}

	/// <summary>Like <see cref="Stop"/>, waiting for it (closing Terraria).</summary>
	public static void StopNow()
	{
		PspRemote m = _machine;
		Task stop = (m != null) ? Shutdown(m) : _stopping;
		try
		{
			stop?.Wait(12000);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static Task Shutdown(PspRemote m)
	{
		_machine = null;
		Held = 0;
		// Terraria's sound comes back right away (the menu may not run the games' update)
		IsaClient.Music.MediaFocus.GameSoundScale = 1f;
		PspEntry game = Current;
		string path = (game != null && !game.Reading) ? StatePath(game.Key, "resume") : null;
		return Task.Run(() => m.Stop(path));
	}

	#endregion

	#region The core's log

	/// <summary>What the emulator says about the last game (a game that won't run explains itself here).</summary>
	private static string CoreLogPath => Path.Combine(Folder, "emulator-log.txt");

	#endregion

	#region Every frame

	public static void Update(bool hasKeys)
	{
		PspConfig.EnsureLoaded();
		PspRemote m = Machine;
		if (_machine != null && m == null)
		{
			// it stopped by itself: the game didn't boot, or it crashed
			PspRemote failed = _machine;
			Problem = failed.Why();
			_machine = null;
			_stopping = Task.Run(() => failed.Stop(null));
			return;
		}
		if (m == null)
		{
			Held = 0;
			Stick = Vector2.Zero;
			return;
		}
		m.Update();
		ReadInput(m, hasKeys);
		MeasureSpeed(m);
		m.Paused = !Main.hasFocus || PspWindow.Minimized;
		if (m.TakeFrame(ref _pixels, ref _serial, out int w, out int h))
		{
			try
			{
				if (_screen == null || _screen.IsDisposed || _screen.Width != w || _screen.Height != h)
				{
					_screen?.Dispose();
					_screen = new Texture2D(Main.instance.GraphicsDevice, w, h);
				}
				Main.instance.GraphicsDevice.Textures[0] = null;
				_screen.SetData(_pixels, 0, w * h);
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		}
	}

	private static void ReadInput(PspRemote m, bool hasKeys)
	{
		int buttons = 0;
		float sx = 0f;
		float sy = 0f;
		bool fast = false;
		if (hasKeys)
		{
			KeyboardState k = Main.keyState;
			void Map(bool down, int bit)
			{
				if (down)
				{
					buttons |= 1 << bit;
				}
			}
			Map(PspConfig.Down(k, PspButton.Up), Libretro.Up);
			Map(PspConfig.Down(k, PspButton.Down), Libretro.Down);
			Map(PspConfig.Down(k, PspButton.Left), Libretro.Left);
			Map(PspConfig.Down(k, PspButton.Right), Libretro.Right);
			// libretro's four face buttons by place: B is the bottom one, A the right, Y the left, X the top
			Map(PspConfig.Down(k, PspButton.Cross), Libretro.B);
			Map(PspConfig.Down(k, PspButton.Circle), Libretro.A);
			Map(PspConfig.Down(k, PspButton.Square), Libretro.Y);
			Map(PspConfig.Down(k, PspButton.Triangle), Libretro.X);
			Map(PspConfig.Down(k, PspButton.L), Libretro.L);
			Map(PspConfig.Down(k, PspButton.R), Libretro.R);
			Map(PspConfig.Down(k, PspButton.Start), Libretro.Start);
			Map(PspConfig.Down(k, PspButton.Select), Libretro.Select);
			fast = PspConfig.Down(k, PspButton.Fast);
			sx = (PspConfig.Down(k, PspButton.StickRight) ? 1f : 0f) - (PspConfig.Down(k, PspButton.StickLeft) ? 1f : 0f);
			sy = (PspConfig.Down(k, PspButton.StickDown) ? 1f : 0f) - (PspConfig.Down(k, PspButton.StickUp) ? 1f : 0f);
			try
			{
				GamePadState g = GamePad.GetState(PlayerIndex.One);
				if (g.IsConnected)
				{
					Map(g.DPad.Up == ButtonState.Pressed, Libretro.Up);
					Map(g.DPad.Down == ButtonState.Pressed, Libretro.Down);
					Map(g.DPad.Left == ButtonState.Pressed, Libretro.Left);
					Map(g.DPad.Right == ButtonState.Pressed, Libretro.Right);
					Map(g.Buttons.A == ButtonState.Pressed, Libretro.B);
					Map(g.Buttons.B == ButtonState.Pressed, Libretro.A);
					Map(g.Buttons.X == ButtonState.Pressed, Libretro.Y);
					Map(g.Buttons.Y == ButtonState.Pressed, Libretro.X);
					Map(g.Buttons.LeftShoulder == ButtonState.Pressed, Libretro.L);
					Map(g.Buttons.RightShoulder == ButtonState.Pressed, Libretro.R);
					Map(g.Buttons.Start == ButtonState.Pressed, Libretro.Start);
					Map(g.Buttons.Back == ButtonState.Pressed, Libretro.Select);
					fast |= g.Triggers.Right > 0.5f;
					if (g.ThumbSticks.Left != Vector2.Zero)
					{
						sx = g.ThumbSticks.Left.X;
						sy = -g.ThumbSticks.Left.Y;
					}
				}
			}
			catch
			{
			}
		}
		// keys go all the way: on a diagonal, a square stick's corner is what the core expects
		sx = Math.Max(-1f, Math.Min(1f, sx));
		sy = Math.Max(-1f, Math.Min(1f, sy));
		m.Input(buttons, (int)(sx * 32767f), (int)(sy * 32767f));
		m.Speed = fast ? 3 : 1;
		Held = buttons;
		Stick = new Vector2(sx, sy);
	}

	/// <summary>Once a second: how fast the game really runs.</summary>
	private static void MeasureSpeed(PspRemote m)
	{
		double now = SpeedClock.Elapsed.TotalSeconds;
		if (m.Paused || m.Speed != 1 || _speedFrames < 0 || Booting)
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

	#endregion
}
