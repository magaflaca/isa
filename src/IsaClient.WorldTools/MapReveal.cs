using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.IO;
using IsaClient.Building;
using IsaClient.Core;
using IsaClient.WorldTools;
using Terraria;

namespace IsaClient.WorldTools;

/// <summary>
/// Map reveal (.revmap) and world download (.wlddown) for multiplayer.
/// Section loading follows the WldFlow flow for 1.4.5.8: the modern RequestSection packet (159) is tried
/// first, and when the server does not answer it the client falls back to the raw projectile 1020 (packet 27)
/// method: the 1.4.5.8 layout (ProjectileKey) first, then the pre-1.4.5.8 layout (identity + owner).
/// Saving keeps the world name and unique id.
/// Requests are pipelined: many sections are asked for at once (the server answers each as it comes, once), nearest
/// to you first; one that doesn't arrive in time is asked again a few times. The legacy methods move several drones
/// at once. Everything is in the panel (World, Map Reveal).
/// </summary>
public static class MapReveal
{
	public enum RevealMode
	{
		Modern,
		RawLegacy,
		Legacy,
		Bed
	}

	private const int SectionWidth = 200;

	private const int SectionHeight = 150;

	/// <summary>Sections asked for at once with RequestSection (159).</summary>
	[Setting("RevealWindow", Min = 1, Max = 1024)]
	public static int RevealWindow = 128;

	/// <summary>New section requests sent per tick (60 a second) with RequestSection.</summary>
	[Setting("RevealPerTick", Min = 1, Max = 512)]
	public static int RevealPerTick = 32;

	/// <summary>Seconds before a section that didn't arrive is asked for again.</summary>
	[Setting("RevealTimeout", Min = 1, Max = 60)]
	public static int RevealTimeoutSeconds = 4;

	/// <summary>How many more times a missing section is asked for.</summary>
	[Setting("RevealRetries", Min = 0, Max = 20)]
	public static int RevealRetries = 2;

	/// <summary>Drones at once for the legacy methods (each one makes the server send the sections around it).</summary>
	[Setting("RevealDrones", Min = 1, Max = 16)]
	public static int RevealDrones = 6;

	/// <summary>Ask for the sections near you first (the map fills from where you are).</summary>
	[Setting("RevealNearFirst")]
	public static bool RevealNearFirst = true;

	/// <summary>When RequestSection gets no answer, switch to the legacy method by itself.</summary>
	[Setting("RevealAutoFallback")]
	public static bool RevealAutoFallback = true;

	/// <summary>
	/// Far chests and tile entities (mannequins, racks...) are kept small while revealing, so worlds full of them fit
	/// in memory; they come back as you get near and go whole into a world download. Off: as before, all kept as is.
	/// </summary>
	[Setting("RevealKeepFarSmall")]
	public static bool RevealKeepFarSmall = true;

	/// <summary>Empty tiles far from you are let go with every method. Off: as before, only with the legacy one.</summary>
	[Setting("RevealTrimAllMethods")]
	public static bool RevealTrimAllMethods = true;

	/// <summary>
	/// The reveal waits while sections are still being processed and when memory is nearly full, and stops before
	/// the game runs out of it. Off: as before, it asks without waiting.
	/// </summary>
	[Setting("RevealMemoryGuard")]
	public static bool RevealMemoryGuard = true;

	/// <summary>Every section, in the order they're asked for (index = x * maxSectionsY + y).</summary>
	private static int[] _order;

	private static int _cursor;

	/// <summary>Sections asked for and not here yet: when.</summary>
	private static readonly Dictionary<int, long> _inFlight = new Dictionary<int, long>();

	/// <summary>Times each section was asked for.</summary>
	private static readonly Dictionary<int, int> _tries = new Dictionary<int, int>();

	/// <summary>Legacy: which drone each section's request uses (a drone moves to its next section once this one arrives).</summary>
	private static readonly Dictionary<int, int> _droneOf = new Dictionary<int, int>();

	private const int LegacyProjectileType = 1020;

	private const string LegacyProjectileMarker = "IsaClient Map Revealing Projectile";

	private const double ModernProbeSeconds = 3.0;

	private const double ModernStallSeconds = 12.0;

	private const double GiveUpSeconds = 45.0;

	private static readonly Tile EmptyTile = new Tile();

	private static int _initialized;

	private static int _saving;

	private static bool _revealing;

	private static RevealMode _mode;

	private static bool _automatic;

	private static bool _downloadWhenDone;

	private static bool _refreshingMap;

	private static int _sectionX;

	private static int _sectionY;

	private static int _requestCooldown;

	private static int _loadedByCommand;

	private static int _lastReportedLoaded;

	private static int _lastCollectedLoaded;

	private static int _requestAttempts;

	private static int _receivedTileBlocks;

	private static long _nextRequestUtcTicks;

	private static int _rawLegacySlot;

	private static bool _rawOldLayout;

	private static bool _rawLayoutForced;

	private static long _rawProbeDeadlineUtcTicks;

	private static int _rawProbeStartPending;

	private static int _compactedEmptyTiles;

	private static int _pendingTilePostProcess;

	private static bool[] _sectionsLoadedBeforeReveal;

	private static int _automaticInitialPending;

	private static long _automaticProbeDeadlineUtcTicks;

	private static int _lastProgressPending;

	private static long _lastProgressUtcTicks;

	private static SpriteBatch _overlaySpriteBatch;

	private static readonly object LogSync = new object();

	private static StreamWriter _networkLog;

	private static string _networkLogPath;

	public static bool IsRevealing => _revealing;

	public static bool IsSaving => Volatile.Read(ref _saving) != 0;

	public static bool TryHandle(string rawText)
	{
		EnsureInitialized();
		if (string.IsNullOrWhiteSpace(rawText))
		{
			return false;
		}
		string text = rawText.Trim();
		if (text.Length < 2 || (text[0] != '.' && text[0] != '!' && text[0] != ChatCommands.CommandSpecifier))
		{
			return false;
		}
		string[] parts = text.Substring(1).Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0)
		{
			return false;
		}
		switch (parts[0].ToLowerInvariant())
		{
		case "wlddown":
		case "worlddownload":
		case "wlddownload":
		case "wld":
			StartWorldDownload(revealFirst: true);
			return true;
		case "wlddownnow":
		case "worlddownloadnow":
			StartWorldDownload(revealFirst: false);
			return true;
		case "revmap":
		case "revealmap":
			_rawLayoutForced = false;
			ToggleRevealMap(RevealMode.Modern, automatic: true);
			return true;
		case "revmapmodern":
		case "revealmapmodern":
			ToggleRevealMap(RevealMode.Modern, automatic: false);
			return true;
		case "rawlegacyrevmap":
		case "rawlegacyrevealmap":
		case "revmaplegacy":
			_rawLayoutForced = false;
			ToggleRevealMap(RevealMode.RawLegacy, automatic: false);
			return true;
		case "revmapold":
		case "revmaplegacyold":
			_rawLayoutForced = true;
			ToggleRevealMap(RevealMode.RawLegacy, automatic: false);
			return true;
		case "legacyrevmap":
		case "legacyrevealmap":
			ToggleRevealMap(RevealMode.Legacy, automatic: false);
			return true;
		case "revmapbed":
		case "revealmapbed":
			ToggleRevealMap(RevealMode.Bed, automatic: false);
			return true;
		case "netlog":
			StartNetworkLog("manual");
			return true;
		case "netlogstop":
			StopNetworkLog();
			return true;
		default:
			return false;
		}
	}

	private static void EnsureInitialized()
	{
		if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
		{
		}
	}

	/// <summary>Called by the panel's "Reveal Map" button and Mods.RevealTheMap().</summary>
	public static void RevealMapFromUI()
	{
		EnsureInitialized();
		_rawLayoutForced = false;
		ToggleRevealMap(RevealMode.Modern, automatic: true);
	}

	public static void Update()
	{
		CleanupLegacyRevealProjectiles();
		if (Main.gameMenu)
		{
			ForgetSectionMethod();
			ForgetStored();
		}
		else
		{
			RestoreNearSections();
		}
		if (!_revealing)
		{
			return;
		}
		if (Main.gameMenu || Main.netMode != 1 || !Netplay.Connection.IsActive || Main.sectionManager == null)
		{
			CancelReveal("Canceled retrieving tile data (connection closed).");
			return;
		}
		int pending = CountUnloadedSections();
		long now = DateTime.UtcNow.Ticks;
		if (pending < _lastProgressPending)
		{
			_lastProgressPending = pending;
			_lastProgressUtcTicks = now;
		}
		if (_automatic && RevealAutoFallback && _mode == RevealMode.Modern)
		{
			if (_automaticProbeDeadlineUtcTicks != 0 && now >= _automaticProbeDeadlineUtcTicks)
			{
				_automaticProbeDeadlineUtcTicks = 0;
				if (pending >= _automaticInitialPending)
				{
					Log("AUTO modern timeout pending=" + pending + " initial=" + _automaticInitialPending + "; switching to rawlegacy");
					Message("The server did not answer section requests, switching to the legacy method.", ClientChat.InfoColor);
					_sectionMethod = SectionMethod.Drone;
					SwitchMode(RevealMode.RawLegacy);
					return;
				}
				Message("The server answers section requests (packet 159), continuing.", ClientChat.AccentColor);
			}
			else if (_automaticProbeDeadlineUtcTicks == 0 && now - _lastProgressUtcTicks >= TimeSpan.FromSeconds(ModernStallSeconds).Ticks)
			{
				Log("AUTO modern stalled pending=" + pending + "; switching to rawlegacy");
				Message("Section requests stopped answering, switching to the legacy method.", ClientChat.InfoColor);
				_sectionMethod = SectionMethod.Drone;
				SwitchMode(RevealMode.RawLegacy);
				return;
			}
		}
		if (_mode == RevealMode.RawLegacy && !_rawOldLayout && _rawProbeDeadlineUtcTicks != 0 && now >= _rawProbeDeadlineUtcTicks)
		{
			_rawProbeDeadlineUtcTicks = 0;
			if (pending >= _rawProbeStartPending)
			{
				// Servers built from pre-1.4.5.8 sources still read packet 27 as identity + owner.
				Log("RAWLEGACY 1.4.5.8 layout timeout pending=" + pending + "; switching to the old layout");
				Message("The server ignored the 1.4.5.8 drone packet, trying the older packet layout.", ClientChat.InfoColor);
				_rawOldLayout = true;
				_sectionMethod = SectionMethod.DroneOld;
				SwitchMode(RevealMode.RawLegacy);
				return;
			}
		}
		if (now - _lastProgressUtcTicks >= TimeSpan.FromSeconds(GiveUpSeconds).Ticks)
		{
			Log("REVEAL gave up pending=" + pending);
			Message("The server stopped sending sections.", ClientChat.InfoColor);
			FinishReveal();
			return;
		}
		Pump(pending, now);
	}

	private static bool Loaded(int section) => Main.sectionManager.SectionLoaded(section / Main.maxSectionsY, section % Main.maxSectionsY);

	/// <summary>Every section in the order to ask for them: nearest to you first, or column by column.</summary>
	private static void BuildOrder()
	{
		int sx = Main.maxSectionsX;
		int sy = Main.maxSectionsY;
		IEnumerable<int> all = Enumerable.Range(0, sx * sy);
		if (RevealNearFirst && Main.LocalPlayer != null)
		{
			int px = Utils.Clamp((int)(Main.LocalPlayer.Center.X / 16f) / SectionWidth, 0, sx - 1);
			int py = Utils.Clamp((int)(Main.LocalPlayer.Center.Y / 16f) / SectionHeight, 0, sy - 1);
			// Sections are wider than tall: distance in tiles, so the map fills in a circle.
			all = all.OrderBy(s => Math.Pow((s / sy - px) * SectionWidth, 2) + Math.Pow((s % sy - py) * SectionHeight, 2));
		}
		_order = all.ToArray();
		_cursor = 0;
		_inFlight.Clear();
		_tries.Clear();
		_droneOf.Clear();
	}

	/// <summary>
	/// One tick of asking: what arrived leaves the in-flight list (what took too long too, to be asked again), then new
	/// requests go out until the window is full. Done when nothing is missing, or nothing is left to ask for.
	/// </summary>
	private static void Pump(int pending, long now)
	{
		if (_order == null || _order.Length != Main.maxSectionsX * Main.maxSectionsY)
		{
			BuildOrder();
		}
		bool modern = _mode == RevealMode.Modern;
		int window = modern ? RevealWindow : ((_mode == RevealMode.Bed) ? 1 : RevealDrones);
		int perTick = modern ? RevealPerTick : window;
		long timeout = TimeSpan.FromSeconds(Math.Max(1, RevealTimeoutSeconds)).Ticks;
		foreach (int s in _inFlight.Keys.ToList())
		{
			if (Loaded(s))
			{
				_inFlight.Remove(s);
				_droneOf.Remove(s);
				_loadedByCommand++;
			}
			else if (now - _inFlight[s] >= timeout)
			{
				_inFlight.Remove(s);
				_droneOf.Remove(s);
			}
		}
		if (_loadedByCommand >= _lastCollectedLoaded + 100)
		{
			_lastCollectedLoaded = _loadedByCommand;
			GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
		}
		if (pending == 0)
		{
			if (Volatile.Read(ref _pendingTilePostProcess) <= 0)
			{
				FinishReveal();
			}
			return;
		}
		// Back pressure: sections that arrived and weren't trimmed yet (legacy) hold whole Tile objects; asking for
		// more before they're done piles a big world up in memory until it runs out.
		if (RevealMemoryGuard && Volatile.Read(ref _pendingTilePostProcess) > Math.Max(4, window * 2))
		{
			// Still trimming: not stuck.
			_lastProgressUtcTicks = Math.Max(_lastProgressUtcTicks, now - TimeSpan.FromSeconds(GiveUpSeconds / 2.0).Ticks);
			return;
		}
		if (RevealMemoryGuard && LowOnMemory(now))
		{
			// If it never frees up, the reveal gives up (as when the server stops) instead of crashing the game.
			return;
		}
		int sent = 0;
		for (int scanned = 0; scanned < _order.Length && sent < perTick && _inFlight.Count < window; scanned++)
		{
			int s = _order[_cursor];
			_cursor = (_cursor + 1) % _order.Length;
			if (_inFlight.ContainsKey(s) || Loaded(s))
			{
				continue;
			}
			_tries.TryGetValue(s, out int tries);
			if (tries > RevealRetries)
			{
				continue;
			}
			_tries[s] = tries + 1;
			_sectionX = s / Main.maxSectionsY;
			_sectionY = s % Main.maxSectionsY;
			RequestCurrentSection(DroneFor(s));
			_inFlight[s] = now;
			sent++;
		}
		if (sent == 0 && _inFlight.Count == 0)
		{
			// Nothing in the air and nothing left to ask for: what's missing never came.
			if (Volatile.Read(ref _pendingTilePostProcess) <= 0)
			{
				FinishReveal();
			}
			return;
		}
		if (_loadedByCommand >= _lastReportedLoaded + 200)
		{
			_lastReportedLoaded = _loadedByCommand;
			Message(pending + " sections left.", ClientChat.AccentColor);
		}
	}

	private static long _memoryCheckUtcTicks;

	private static bool _memoryLow;

	private static long _memoryLimit;

	private static long _memoryLowSinceUtcTicks;

	/// <summary>
	/// True while the whole process (tiles, chests, map textures...) is near what a 32-bit process can hold (2.9 GB with
	/// the large address flag, 1.5 GB without): the requests wait while the garbage collector makes room. If it never
	/// does, the reveal stops with a message instead of the game running out of memory.
	/// </summary>
	private static bool LowOnMemory(long now)
	{
		if (now - _memoryCheckUtcTicks < TimeSpan.FromSeconds(0.5).Ticks)
		{
			return _memoryLow;
		}
		_memoryCheckUtcTicks = now;
		if (_memoryLimit == 0)
		{
			_memoryLimit = MemoryLimit();
			Log("REVEAL memory limit " + (_memoryLimit >> 20) + "MB");
		}
		long used = MemoryUsed();
		if (!_memoryLow && used > _memoryLimit)
		{
			_memoryLow = true;
			_memoryLowSinceUtcTicks = now;
			Log("REVEAL memory high used=" + (used >> 20) + "MB managed=" + (GC.GetTotalMemory(false) >> 20) + "MB, waiting");
			Message("Low on memory (" + (used >> 20) + " MB): the map reveal waits for it to free up.", ClientChat.InfoColor);
			GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
		}
		else if (_memoryLow && used < _memoryLimit * 9 / 10)
		{
			_memoryLow = false;
			Log("REVEAL memory ok used=" + (used >> 20) + "MB");
		}
		else if (_memoryLow && now - _memoryLowSinceUtcTicks > TimeSpan.FromSeconds(8).Ticks)
		{
			// It won't free up: stop here, with what's revealed so far, rather than crash.
			_memoryLow = false;
			Log("REVEAL memory stop used=" + (used >> 20) + "MB");
			Message("This world doesn't fit in the game's memory: the map reveal stopped here (" + (used >> 20) + " MB). What's revealed stays.", ClientChat.ErrorColor);
			FinishReveal();
			return true;
		}
		else if (_memoryLow)
		{
			GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
		}
		return _memoryLow;
	}

	/// <summary>The process's own memory (private bytes), or the managed heap when that can't be read.</summary>
	public static long MemoryUsed()
	{
		long managed = GC.GetTotalMemory(forceFullCollection: false);
		try
		{
			using System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess();
			return Math.Max(managed, process.PrivateMemorySize64);
		}
		catch
		{
			return managed;
		}
	}


	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
	private sealed class MemoryStatus
	{
		public uint Length = 64;

		public uint MemoryLoad;

		public ulong TotalPhys;

		public ulong AvailPhys;

		public ulong TotalPageFile;

		public ulong AvailPageFile;

		public ulong TotalVirtual;

		public ulong AvailVirtual;

		public ulong AvailExtendedVirtual;
	}

	[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
	[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] MemoryStatus status);

	/// <summary>
	/// How much the process may use before the reveal waits: about 72% of the address space Windows really gives it
	/// (4 GB with the large address flag on a 64-bit Windows: 2.9 GB; 2 GB without: 1.5 GB). Asked to Windows, so it is
	/// right whichever exe runs; the exe's own header is only the fallback.
	/// </summary>
	private static long MemoryLimit()
	{
		if (Environment.Is64BitProcess)
		{
			return 2900L << 20;
		}
		try
		{
			MemoryStatus status = new MemoryStatus();
			if (GlobalMemoryStatusEx(status) && status.TotalVirtual >= (1UL << 30))
			{
				return (long)(status.TotalVirtual / 100 * 72);
			}
		}
		catch
		{
		}
		return (Environment.Is64BitOperatingSystem && LargeAddressAware()) ? (2900L << 20) : (1500L << 20);
	}

	/// <summary>Whether this exe has the large address aware flag (up to 4 GB on a 64-bit Windows).</summary>
	private static bool LargeAddressAware()
	{
		try
		{
			using FileStream stream = File.OpenRead(System.Reflection.Assembly.GetEntryAssembly().Location);
			using BinaryReader reader = new BinaryReader(stream);
			stream.Position = 0x3C;
			int pe = reader.ReadInt32();
			stream.Position = pe + 4 + 18;
			return (reader.ReadUInt16() & 0x20) != 0;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>A free drone for a legacy request (each in-flight section keeps its own).</summary>
	private static int DroneFor(int section)
	{
		if (_mode != RevealMode.RawLegacy)
		{
			return 0;
		}
		HashSet<int> used = new HashSet<int>(_droneOf.Values);
		int drone = 0;
		while (used.Contains(drone) && drone < 15)
		{
			drone++;
		}
		_droneOf[section] = drone;
		return drone;
	}

	public static int CountUnloadedSections()
	{
		if (Main.sectionManager == null)
		{
			return Main.maxSectionsX * Main.maxSectionsY;
		}
		int count = 0;
		for (int x = 0; x < Main.maxSectionsX; x++)
		{
			for (int y = 0; y < Main.maxSectionsY; y++)
			{
				if (!Main.sectionManager.SectionLoaded(x, y))
				{
					count++;
				}
			}
		}
		return count;
	}

	private static void ToggleRevealMap(RevealMode mode, bool automatic)
	{
		if (_revealing)
		{
			_downloadWhenDone = false;
			CancelReveal("Canceled retrieving tile data.");
			return;
		}
		if (Main.gameMenu)
		{
			return;
		}
		if (Main.netMode == 0)
		{
			RefreshMapAsync(allTiles: true);
			return;
		}
		BeginRevealMap(mode, automatic);
	}

	private static bool BeginRevealMap(RevealMode mode, bool automatic)
	{
		if (Main.gameMenu || Main.netMode != 1 || !Netplay.Connection.IsActive || Main.sectionManager == null)
		{
			Message("You can only do this in an online game.", ClientChat.ErrorColor);
			return false;
		}
		int unloaded = CountUnloadedSections();
		if (unloaded == 0)
		{
			Message("Refreshing Map.", ClientChat.AccentColor);
			RefreshMapAsync(allTiles: false);
			return false;
		}
		_sectionX = 0;
		_sectionY = 0;
		_requestCooldown = 0;
		_loadedByCommand = 0;
		_lastReportedLoaded = 0;
		_lastCollectedLoaded = 0;
		_requestAttempts = 0;
		_receivedTileBlocks = 0;
		_compactedEmptyTiles = 0;
		_pendingTilePostProcess = 0;
		_nextRequestUtcTicks = 0L;
		_automatic = automatic;
		_automaticInitialPending = unloaded;
		_automaticProbeDeadlineUtcTicks = (automatic && mode == RevealMode.Modern) ? DateTime.UtcNow.AddSeconds(ModernProbeSeconds).Ticks : 0;
		_lastProgressPending = unloaded;
		_lastProgressUtcTicks = DateTime.UtcNow.Ticks;
		_mode = mode;
		BuildOrder();
		_sectionsLoadedBeforeReveal = (mode == RevealMode.RawLegacy) ? CaptureLoadedSections() : null;
		_rawOldLayout = _rawLayoutForced;
		StartRawProbe();
		_revealing = true;
		MapTools.RevealMapCooldown = true;
		Log("REVEAL start mode=" + mode + " automatic=" + automatic + " pending=" + unloaded);
		Message("Retrieving Tile Data.", ClientChat.AccentColor);
		Message("Speeds vary based off connection speed and on how many tiles have already been loaded.", ClientChat.AccentColor);
		switch (mode)
		{
		case RevealMode.Modern:
			Message("Requesting " + unloaded + " sections with RequestSection (packet 159), " + RevealWindow + " at a time" + ((automatic && RevealAutoFallback) ? "; falls back to the legacy method if the server ignores it." : "."), ClientChat.AccentColor);
			break;
		case RevealMode.RawLegacy:
		case RevealMode.Legacy:
			Message("Map chunk data retrieval may require prior login depending on the server protection levels.", ClientChat.AccentColor);
			break;
		case RevealMode.Bed:
			Message("Retrieving Tile Data with Packet 8. This method only works on vanilla servers.", ClientChat.AccentColor);
			break;
		}
		Message("Use the command again to cancel.", ClientChat.AccentColor);
		return true;
	}

	private static void SwitchMode(RevealMode mode)
	{
		_mode = mode;
		BuildOrder();
		_automaticProbeDeadlineUtcTicks = 0;
		_sectionX = 0;
		_sectionY = 0;
		_requestCooldown = 0;
		_nextRequestUtcTicks = 0L;
		_lastProgressPending = CountUnloadedSections();
		_lastProgressUtcTicks = DateTime.UtcNow.Ticks;
		if (mode == RevealMode.RawLegacy && _sectionsLoadedBeforeReveal == null)
		{
			_sectionsLoadedBeforeReveal = CaptureLoadedSections();
		}
		StartRawProbe();
	}

	/// <summary>Gives the 1.4.5.8 drone layout a few seconds before falling back to the old one.</summary>
	private static void StartRawProbe()
	{
		bool probe = _mode == RevealMode.RawLegacy && !_rawOldLayout;
		_rawProbeDeadlineUtcTicks = probe ? DateTime.UtcNow.AddSeconds(ModernProbeSeconds).Ticks : 0;
		_rawProbeStartPending = CountUnloadedSections();
	}

	private static void RequestCurrentSection(int drone = 0)
	{
		_requestAttempts++;
		switch (_mode)
		{
		case RevealMode.Modern:
			Log("REQUEST mode=modern attempt=" + _requestAttempts + " section=" + _sectionX + "," + _sectionY);
			NetMessage.SendData(159, -1, -1, null, _sectionX, _sectionY);
			break;
		case RevealMode.Bed:
		{
			int x = Utils.Clamp(_sectionX * SectionWidth + SectionWidth / 2, 40, Main.maxTilesX - 40);
			int y = Utils.Clamp(_sectionY * SectionHeight + SectionHeight / 2, 40, Main.maxTilesY - 40);
			Log("REQUEST mode=bed attempt=" + _requestAttempts + " section=" + _sectionX + "," + _sectionY);
			NetMessage.SendData(8, -1, -1, null, x, y);
			break;
		}
		case RevealMode.RawLegacy:
		{
			// A few fixed drone slots, each moved to its next section: the server updates the same projectiles
			// instead of creating (and counting as spam) a new one every section.
			Vector2 position = new Vector2((float)(_sectionX * SectionWidth) * 16f + 2f, (float)(_sectionY * SectionHeight) * 16f + 2f);
			int slot = drone;
			Log("REQUEST mode=rawlegacy layout=" + (_rawOldLayout ? "old" : "1.4.5.8") + " attempt=" + _requestAttempts + " section=" + _sectionX + "," + _sectionY + " slot=" + slot);
			if (_rawOldLayout)
			{
				SendRawLegacyProjectile((short)(999 - slot), position, new Vector2(1f, 1f));
			}
			else
			{
				SendRawDroneProjectile(900 + slot, position, new Vector2(1f, 1f));
			}
			break;
		}
		case RevealMode.Legacy:
		{
			Vector2 position2 = new Vector2((float)(_sectionX * SectionWidth) * 16f + 8f, (float)(_sectionY * SectionHeight) * 16f + 8f);
			int index = Projectile.NewProjectile(Main.LocalPlayer.GetProjectileSource_Misc(LegacyProjectileType), position2, new Vector2(1f, 1f), LegacyProjectileType, 0, 0f, Main.myPlayer);
			if (index < 0 || index >= Main.maxProjectiles)
			{
				Log("LEGACY-SPAWN failed index=" + index);
				break;
			}
			Projectile projectile = Main.projectile[index];
			projectile.velocity = Vector2.Zero;
			projectile.timeLeft = 4;
			projectile.miscText = LegacyProjectileMarker;
			// The first packet 27 was already serialized by NewProjectile; neutralizing the projectile afterwards
			// keeps what the server sees while the drone never takes control of the player.
			projectile.type = 0;
			projectile.aiStyle = 0;
			projectile.tileCollide = false;
			projectile.friendly = false;
			projectile.hostile = false;
			projectile.netUpdate = false;
			break;
		}
		}
	}

	#region Single sections (free camera, spectate)

	/// <summary>How this server answers a section request; learned per connection (and from .revmap).</summary>
	private enum SectionMethod
	{
		Modern,
		Drone,
		DroneOld
	}

	private static SectionMethod _sectionMethod;

	private static bool _sectionMethodConfirmed;

	private static int _probeX = -1;

	private static int _probeY = -1;

	private static long _probeSinceUtcTicks;

	private const double SectionProbeSeconds = 2.5;

	private static void ForgetSectionMethod()
	{
		_sectionMethod = SectionMethod.Modern;
		_sectionMethodConfirmed = false;
		_probeX = _probeY = -1;
	}

	/// <summary>
	/// Asks the server for one section (the free camera and spectate call this for what they look at). Starts with
	/// RequestSection (159); when a requested section does not arrive in a few seconds it moves on to the drone
	/// packet (1.4.5.8 layout, then the old one), and keeps the method that worked until you leave the server.
	/// </summary>
	public static void RequestSingleSection(int x, int y)
	{
		if (Main.netMode != 1 || !Netplay.Connection.IsActive || Main.sectionManager == null)
		{
			return;
		}
		long now = DateTime.UtcNow.Ticks;
		if (_probeX >= 0)
		{
			if (Main.sectionManager.SectionLoaded(_probeX, _probeY))
			{
				_sectionMethodConfirmed = true;
				_probeX = _probeY = -1;
			}
			else if (!_sectionMethodConfirmed && now - _probeSinceUtcTicks >= TimeSpan.FromSeconds(SectionProbeSeconds).Ticks)
			{
				if (_sectionMethod != SectionMethod.DroneOld)
				{
					_sectionMethod++;
					Log("SECTION method -> " + _sectionMethod);
				}
				_probeX = _probeY = -1;
			}
		}
		if (_probeX < 0 && !_sectionMethodConfirmed)
		{
			_probeX = x;
			_probeY = y;
			_probeSinceUtcTicks = now;
		}
		if (_sectionMethod == SectionMethod.Modern)
		{
			NetMessage.SendData(159, -1, -1, null, x, y);
			return;
		}
		Vector2 position = new Vector2((float)(x * SectionWidth) * 16f + 2f, (float)(y * SectionHeight) * 16f + 2f);
		int slot = (_rawLegacySlot++ & 1);
		if (_sectionMethod == SectionMethod.DroneOld)
		{
			SendRawLegacyProjectile((short)(998 + slot), position, new Vector2(1f, 1f));
		}
		else
		{
			SendRawDroneProjectile(900 + slot, position, new Vector2(1f, 1f));
		}
	}

	#endregion

	/// <summary>
	/// 1.4.5.8 packet 27: int ProjectileKey (spawner | index &lt;&lt; 8 | generation &lt;&lt; 18), position, velocity,
	/// short type, flags. The spawner must be our own slot; a fixed generation lets the server update the
	/// same projectile instead of creating (and counting as spam) a new one every section.
	/// </summary>
	private static void SendRawDroneProjectile(int index, Vector2 position, Vector2 velocity)
	{
		if (!Netplay.Connection.IsActive || Netplay.Connection.Socket == null)
		{
			return;
		}
		int key = (Main.myPlayer & 0xFF) | ((index & 0x3FF) << 8) | ((1 & 0x3FFF) << 18);
		using MemoryStream stream = new MemoryStream(32);
		using BinaryWriter writer = new BinaryWriter(stream);
		writer.Write((ushort)0);
		writer.Write((byte)27);
		writer.Write(key);
		writer.Write(position.X);
		writer.Write(position.Y);
		writer.Write(velocity.X);
		writer.Write(velocity.Y);
		writer.Write((short)LegacyProjectileType);
		writer.Write((byte)0);
		byte[] packet = stream.ToArray();
		byte[] length = BitConverter.GetBytes((ushort)packet.Length);
		packet[0] = length[0];
		packet[1] = length[1];
		Netplay.Connection.Socket.AsyncSend(packet, 0, packet.Length, delegate
		{
		});
	}

	/// <summary>Pre-1.4.5.8 packet 27: short identity, position, velocity, byte owner, short type, flags.</summary>
	private static void SendRawLegacyProjectile(short identity, Vector2 position, Vector2 velocity)
	{
		if (!Netplay.Connection.IsActive || Netplay.Connection.Socket == null)
		{
			return;
		}
		using MemoryStream stream = new MemoryStream(32);
		using BinaryWriter writer = new BinaryWriter(stream);
		writer.Write((ushort)0);
		writer.Write((byte)27);
		writer.Write(identity);
		writer.Write(position.X);
		writer.Write(position.Y);
		writer.Write(velocity.X);
		writer.Write(velocity.Y);
		writer.Write((byte)Main.myPlayer);
		writer.Write((short)LegacyProjectileType);
		writer.Write((byte)0);
		byte[] packet = stream.ToArray();
		byte[] length = BitConverter.GetBytes((ushort)packet.Length);
		packet[0] = length[0];
		packet[1] = length[1];
		Netplay.Connection.Socket.AsyncSend(packet, 0, packet.Length, delegate
		{
		});
	}

	private static void CleanupLegacyRevealProjectiles()
	{
		if (Main.gameMenu)
		{
			return;
		}
		for (int i = 0; i < Main.maxProjectiles; i++)
		{
			Projectile projectile = Main.projectile[i];
			if (projectile.active && projectile.owner == Main.myPlayer && !(projectile.miscText != LegacyProjectileMarker) && projectile.timeLeft <= 2)
			{
				projectile.type = LegacyProjectileType;
				projectile.position = new Vector2(8f, 8f);
				projectile.velocity = new Vector2(-200f, -200f);
				NetMessage.SendData(27, -1, -1, null, i);
				projectile.active = false;
				projectile.miscText = string.Empty;
			}
		}
	}

	private static void DrawOverlay(GameTime gameTime)
	{
		if (!_revealing || Main.gameMenu || Main.netMode != 1)
		{
			return;
		}
		bool began = false;
		try
		{
			if (_overlaySpriteBatch == null)
			{
				_overlaySpriteBatch = new SpriteBatch(Main.spriteBatch.GraphicsDevice);
			}
			_overlaySpriteBatch.Begin();
			began = true;
			string text = "Sections Remaining: " + CountUnloadedSections() + (_downloadWhenDone ? "  (world download pending)" : "");
			Utils.DrawBorderString(_overlaySpriteBatch, text, new Vector2((float)Main.screenWidth * 0.5f, 40f), Color.White, 1f, 0.5f, 0.5f);
		}
		catch
		{
		}
		finally
		{
			if (began)
			{
				try
				{
					_overlaySpriteBatch.End();
				}
				catch
				{
				}
			}
		}
	}

	private static void AdvanceSection()
	{
		_sectionY++;
		if (_sectionY >= Main.maxSectionsY)
		{
			_sectionY = 0;
			_sectionX++;
		}
		_requestCooldown = 0;
	}

	private static void FinishReveal()
	{
		_revealing = false;
		MapTools.RevealMapCooldown = false;
		int remaining = CountUnloadedSections();
		Log("REVEAL finish mode=" + _mode + " remaining=" + remaining + " attempts=" + _requestAttempts);
		if (_mode == RevealMode.RawLegacy)
		{
			RevealSectionsLoadedBeforeCommand();
			Main.refreshMap = true;
			Message("Map Revealed", ClientChat.AccentColor);
		}
		else
		{
			RefreshMapAsync(allTiles: false);
		}
		if (remaining > 0)
		{
			Message(remaining + " sections are still unloaded.", ClientChat.InfoColor);
		}
		if (_downloadWhenDone)
		{
			_downloadWhenDone = false;
			SaveDownloadedWorld();
		}
	}

	private static void CancelReveal(string reason)
	{
		_revealing = false;
		_automatic = false;
		_automaticProbeDeadlineUtcTicks = 0;
		MapTools.RevealMapCooldown = false;
		MapTools.StopMapReveal = false;
		if (_downloadWhenDone)
		{
			_downloadWhenDone = false;
			reason += " World download canceled.";
		}
		Log("REVEAL cancel reason=" + reason + " pending=" + CountUnloadedSections() + " attempts=" + _requestAttempts + " tileBlocks=" + _receivedTileBlocks);
		Message(reason, ClientChat.AccentColor);
	}

	/// <summary>Hooked at the end of NetMessage.DecompressTileBlock_Inner, once the section is marked as loaded.</summary>
	public static void AfterTileBlock(int xStart, int yStart, int width, int height)
	{
		_receivedTileBlocks++;
		if (Main.sectionManager != null && Main.maxSectionsY > 0)
		{
			// The server sent this section again: its own chests and entities are the news, not what was kept.
			DropStored(SectionIndex(xStart, yStart));
		}
		// Legacy as always; with RevealTrimAllMethods / RevealKeepFarSmall (on by default) every method: a huge world
		// doesn't fit in memory unless what's far from you is let go or kept small as it arrives.
		if (!_revealing || (_mode != RevealMode.RawLegacy && !RevealTrimAllMethods && !RevealKeepFarSmall) || Main.Map == null || Main.tile == null)
		{
			return;
		}
		Interlocked.Increment(ref _pendingTilePostProcess);
		Main.QueueMainThreadAction(delegate
		{
			try
			{
				if (_mode == RevealMode.RawLegacy || RevealTrimAllMethods)
				{
					ProcessTileBlockForMapAndMemory(xStart, yStart, width, height);
				}
				if (RevealKeepFarSmall)
				{
					StripIfFar(xStart, yStart, width, height);
				}
			}
			catch (Exception ex)
			{
				Log("POSTPROCESS error tiles=" + xStart + "," + yStart + " error=" + ex);
			}
			finally
			{
				Interlocked.Decrement(ref _pendingTilePostProcess);
			}
		});
	}

	// Keeps the minimap fullbright and releases Tile objects that are pure air (a reveal pulls the whole world into
	// memory at once).
	private static void ProcessTileBlockForMapAndMemory(int xStart, int yStart, int width, int height)
	{
		if (Main.Map == null || Main.tile == null)
		{
			return;
		}
		int xEnd = Math.Min(Main.maxTilesX, xStart + width);
		int yEnd = Math.Min(Main.maxTilesY, yStart + height);
		int playerX = (int)(Main.LocalPlayer.Center.X / 16f);
		int playerY = (int)(Main.LocalPlayer.Center.Y / 16f);
		int compacted = 0;
		for (int x = Math.Max(0, xStart); x < xEnd; x++)
		{
			for (int y = Math.Max(0, yStart); y < yEnd; y++)
			{
				if (WorldGen.InWorld(x, y, 40))
				{
					Main.Map.Update(x, y, byte.MaxValue);
				}
				Main.Map.ConsumeUpdate(x, y);
				Tile tile = Main.tile[x, y];
				if (tile != null && (Math.Abs(x - playerX) > 400 || Math.Abs(y - playerY) > 300) && tile.type == 0 && tile.wall == 0 && tile.liquid == 0 && tile.sTileHeader == 0 && tile.bTileHeader == 0 && tile.bTileHeader2 == 0 && tile.bTileHeader3 == 0 && tile.frameX == 0 && tile.frameY == 0)
				{
					Main.tile[x, y] = null;
					compacted++;
				}
			}
		}
		_compactedEmptyTiles += compacted;
	}

	#region Far chests and tile entities

	/// <summary>
	/// With <see cref="RevealKeepFarSmall"/>: what a far section's chests and tile entities (mannequins, racks,
	/// frames...) hold, kept small while revealing. Each holds dozens of Item objects, and a world full of them (a plots
	/// server) doesn't fit in a 32-bit game. A kept chest still takes what the server sends for it afterwards (its items,
	/// packet 32; its size, 155). They're made again here when you come near, and written straight into a world
	/// download (<see cref="WorldFile.SaveChests"/>, <see cref="WorldFile.SaveTileEntities"/>), so nothing is lost.
	/// Signs are never touched.
	/// </summary>
	private sealed class Stored
	{
		public readonly List<KeptChest> Chests = new List<KeptChest>();

		/// <summary>Each entity as TileEntity.Write saves it.</summary>
		public readonly List<byte[]> Entities = new List<byte[]>();
	}

	/// <summary>A chest kept small: where, its name and its slots as plain numbers.</summary>
	private sealed class KeptChest
	{
		public int Index;

		public int Section;

		public int X;

		public int Y;

		public string Name;

		public int[] Type;

		public short[] Stack;

		public byte[] Prefix;

		public static KeptChest Of(int index, int section, Chest chest)
		{
			int slots = chest.maxItems;
			KeptChest kept = new KeptChest { Index = index, Section = section, X = chest.x, Y = chest.y, Name = chest.name ?? "" };
			kept.Resize(slots);
			for (int l = 0; l < slots && chest.item != null && l < chest.item.Length; l++)
			{
				Item item = chest.item[l];
				if (item != null && !item.IsAir)
				{
					kept.Set(l, item.type, (short)item.stack, item.prefix);
				}
			}
			return kept;
		}

		public void Resize(int slots)
		{
			slots = Math.Max(1, slots);
			Array.Resize(ref Type, slots);
			Array.Resize(ref Stack, slots);
			Array.Resize(ref Prefix, slots);
		}

		public void Set(int slot, int type, short stack, byte prefix)
		{
			if (slot < 0 || slot >= Type.Length)
			{
				return;
			}
			bool air = type <= 0 || stack <= 0;
			Type[slot] = air ? 0 : type;
			Stack[slot] = air ? (short)0 : stack;
			Prefix[slot] = air ? (byte)0 : prefix;
		}

		/// <summary>As WorldFile.SaveChests writes a chest.</summary>
		public void Write(BinaryWriter writer)
		{
			writer.Write(X);
			writer.Write(Y);
			writer.Write(Name ?? "");
			writer.Write(Type.Length);
			for (int l = 0; l < Type.Length; l++)
			{
				if (Stack[l] <= 0 || Type[l] <= 0 || ItemID.Sets.ItemsThatShouldNotBeInInventory[Type[l]])
				{
					writer.Write((short)0);
					continue;
				}
				writer.Write(Stack[l]);
				writer.Write(Type[l]);
				writer.Write(Prefix[l]);
			}
		}
	}

	private static readonly Dictionary<int, Stored> _stored = new Dictionary<int, Stored>();

	/// <summary>Kept chests by their index (to take what the server sends for them).</summary>
	private static readonly Dictionary<int, KeptChest> _keptChests = new Dictionary<int, KeptChest>();

	private static readonly object StoredSync = new object();

	private static int _restoreTick;

	private static int SectionIndex(int tileX, int tileY)
	{
		return (tileX / SectionWidth) * Main.maxSectionsY + tileY / SectionHeight;
	}

	/// <summary>Farther than the sections around you (and the screen) from where you are.</summary>
	private static bool IsFar(int section)
	{
		Player me = Main.LocalPlayer;
		if (me == null)
		{
			return true;
		}
		int px = (int)(me.Center.X / 16f) / SectionWidth;
		int py = (int)(me.Center.Y / 16f) / SectionHeight;
		return Math.Abs(section / Main.maxSectionsY - px) > 2 || Math.Abs(section % Main.maxSectionsY - py) > 3;
	}

	private static void ForgetStored()
	{
		lock (StoredSync)
		{
			_stored.Clear();
			_keptChests.Clear();
		}
	}

	/// <summary>The section came from the server again: what was kept of it is old now.</summary>
	private static void DropStored(int section)
	{
		lock (StoredSync)
		{
			if (_stored.TryGetValue(section, out Stored stored))
			{
				foreach (KeptChest chest in stored.Chests)
				{
					_keptChests.Remove(chest.Index);
				}
				_stored.Remove(section);
			}
		}
	}

	/// <summary>Packet 32 for a chest that isn't in memory: true when it's a kept one (then it took the item).</summary>
	public static bool KeptChestItem(int index, int slot, int type, int stack, int prefix)
	{
		lock (StoredSync)
		{
			if (!_keptChests.TryGetValue(index, out KeptChest chest))
			{
				return false;
			}
			chest.Set(slot, type, (short)stack, (byte)prefix);
			return true;
		}
	}

	/// <summary>Packet 155 for a chest that isn't in memory: true when it's a kept one (then it took the size).</summary>
	public static bool KeptChestResize(int index, int slots)
	{
		lock (StoredSync)
		{
			if (!_keptChests.TryGetValue(index, out KeptChest chest))
			{
				return false;
			}
			chest.Resize(slots);
			return true;
		}
	}

	private static void StripIfFar(int xStart, int yStart, int width, int height)
	{
		if (Main.netMode != 1)
		{
			return;
		}
		int section = SectionIndex(xStart, yStart);
		if (!IsFar(section))
		{
			return;
		}
		Rectangle area = new Rectangle(xStart, yStart, width, height);
		Stored stored = new Stored();
		for (int i = 0; i < Main.chest.Length; i++)
		{
			Chest chest = Main.chest[i];
			if (chest != null && area.Contains(chest.x, chest.y) && !Chest.IsPlayerInChest(i))
			{
				stored.Chests.Add(KeptChest.Of(i, section, chest));
				Chest.RemoveChest(i);
			}
		}
		List<TileEntity> entities = new List<TileEntity>();
		lock (TileEntity.EntityCreationLock)
		{
			foreach (TileEntity entity in TileEntity.ByID.Values)
			{
				if (area.Contains(entity.Position.X, entity.Position.Y) && !(entity is Terraria.GameContent.Tile_Entities.TETeleportationPylon))
				{
					entities.Add(entity);
				}
			}
		}
		foreach (TileEntity entity in entities)
		{
			using MemoryStream stream = new MemoryStream(64);
			using BinaryWriter writer = new BinaryWriter(stream);
			TileEntity.Write(writer, entity);
			writer.Flush();
			stored.Entities.Add(stream.ToArray());
			TileEntity.Remove(entity);
		}
		if (stored.Chests.Count > 0 || stored.Entities.Count > 0)
		{
			lock (StoredSync)
			{
				_stored[section] = stored;
				foreach (KeptChest chest in stored.Chests)
				{
					_keptChests[chest.Index] = chest;
				}
			}
		}
	}

	/// <summary>Twice a second: kept sections you came near get their chests and entities back, here (no request).</summary>
	private static void RestoreNearSections()
	{
		if (++_restoreTick % 30 != 0 || Main.sectionManager == null || IsSaving)
		{
			// Not while a world download is being written (it reads what's kept).
			return;
		}
		List<KeyValuePair<int, Stored>> near;
		lock (StoredSync)
		{
			if (_stored.Count == 0)
			{
				return;
			}
			near = _stored.Where(p => !IsFar(p.Key)).ToList();
			foreach (KeyValuePair<int, Stored> pair in near)
			{
				_stored.Remove(pair.Key);
				foreach (KeptChest chest in pair.Value.Chests)
				{
					_keptChests.Remove(chest.Index);
				}
			}
		}
		foreach (KeyValuePair<int, Stored> pair in near)
		{
			Restore(pair.Value);
		}
	}

	private static void Restore(Stored stored)
	{
		foreach (KeptChest kept in stored.Chests)
		{
			try
			{
				Chest existing = Main.chest[kept.Index];
				if (existing != null && (existing.x != kept.X || existing.y != kept.Y))
				{
					// The index is another chest's now: the server's newer news wins.
					continue;
				}
				Chest made = Chest.CreateWorldChest(kept.Index, kept.X, kept.Y);
				made.name = kept.Name;
				if (made.maxItems != kept.Type.Length)
				{
					made.Resize(kept.Type.Length);
				}
				for (int l = 0; l < kept.Type.Length && l < made.item.Length; l++)
				{
					if (kept.Stack[l] > 0 && kept.Type[l] > 0)
					{
						made.item[l].netDefaults(kept.Type[l]);
						made.item[l].Prefix(kept.Prefix[l]);
						made.item[l].stack = kept.Stack[l];
					}
				}
			}
			catch (Exception ex)
			{
				Log("RESTORE chest error=" + ex.Message);
			}
		}
		foreach (byte[] bytes in stored.Entities)
		{
			try
			{
				using BinaryReader reader = new BinaryReader(new MemoryStream(bytes));
				TileEntity entity = TileEntity.Read(reader, Main.curRelease);
				if (!TileEntity.ByPosition.ContainsKey(entity.Position))
				{
					TileEntity.Add(entity);
				}
			}
			catch (Exception ex)
			{
				Log("RESTORE entity error=" + ex.Message);
			}
		}
	}

	/// <summary>
	/// World download (WorldFile.SaveChests): the kept chests still standing, in the world file's format, and how
	/// many; taken in one go so the count and the records always agree.
	/// </summary>
	public static byte[] StoredChestsForSave(out int count)
	{
		count = 0;
		using MemoryStream stream = new MemoryStream();
		using BinaryWriter writer = new BinaryWriter(stream);
		lock (StoredSync)
		{
			foreach (KeptChest chest in _keptChests.Values)
			{
				if (Main.chest[chest.Index] == null && StoredChestStands(chest.X, chest.Y))
				{
					chest.Write(writer);
					count++;
				}
			}
		}
		writer.Flush();
		return stream.ToArray();
	}

	/// <summary>
	/// World download (WorldFile.SaveTileEntities): the kept tile entities as TileEntity.Write wrote them, and how
	/// many; one the server sent again since (already in memory at its spot) isn't written twice.
	/// </summary>
	public static byte[] StoredEntitiesForSave(out int count)
	{
		count = 0;
		using MemoryStream stream = new MemoryStream();
		using BinaryWriter writer = new BinaryWriter(stream);
		lock (StoredSync)
		{
			foreach (Stored stored in _stored.Values)
			{
				foreach (byte[] bytes in stored.Entities)
				{
					// type (1), ID (4), then x and y.
					Point16 position = new Point16(BitConverter.ToInt16(bytes, 5), BitConverter.ToInt16(bytes, 7));
					if (TileEntity.ByPosition.ContainsKey(position))
					{
						continue;
					}
					writer.Write(bytes);
					count++;
				}
			}
		}
		writer.Flush();
		return stream.ToArray();
	}

	/// <summary>As SaveChests does for the others: only a chest whose tiles are still a container.</summary>
	private static bool StoredChestStands(int x, int y)
	{
		for (int j = x; j <= x + 1; j++)
		{
			for (int k = y; k <= y + 1; k++)
			{
				if (j < 0 || k < 0 || j >= Main.maxTilesX || k >= Main.maxTilesY)
				{
					return false;
				}
				Tile tile = NormalizeTile(Main.tile[j, k]);
				if (!tile.active() || !Main.tileContainer[tile.type])
				{
					return false;
				}
			}
		}
		return true;
	}

	#endregion

	private static bool[] CaptureLoadedSections()
	{
		if (Main.sectionManager == null)
		{
			return null;
		}
		bool[] loaded = new bool[Main.maxSectionsX * Main.maxSectionsY];
		for (int x = 0; x < Main.maxSectionsX; x++)
		{
			for (int y = 0; y < Main.maxSectionsY; y++)
			{
				loaded[x * Main.maxSectionsY + y] = Main.sectionManager.SectionLoaded(x, y);
			}
		}
		return loaded;
	}

	private static void RevealSectionsLoadedBeforeCommand()
	{
		bool[] loaded = _sectionsLoadedBeforeReveal;
		_sectionsLoadedBeforeReveal = null;
		if (loaded == null || Main.Map == null || Main.tile == null)
		{
			return;
		}
		for (int x = 0; x < Main.maxSectionsX; x++)
		{
			for (int y = 0; y < Main.maxSectionsY; y++)
			{
				int index = x * Main.maxSectionsY + y;
				if (index < loaded.Length && loaded[index])
				{
					int tileX = x * SectionWidth;
					int tileY = y * SectionHeight;
					try
					{
						ProcessTileBlockForMapAndMemory(tileX, tileY, Math.Min(SectionWidth, Main.maxTilesX - tileX), Math.Min(SectionHeight, Main.maxTilesY - tileY));
					}
					catch (Exception ex)
					{
						Log("MAP preloaded section error=" + x + "," + y + " error=" + ex);
					}
				}
			}
		}
	}

	private static void RefreshMapAsync(bool allTiles)
	{
		if (_refreshingMap || Main.Map == null)
		{
			return;
		}
		_refreshingMap = true;
		Task.Factory.StartNew(delegate
		{
			try
			{
				if (allTiles)
				{
					for (int x = 0; x < Main.maxTilesX; x++)
					{
						for (int y = 0; y < Main.maxTilesY; y++)
						{
							if (WorldGen.InWorld(x, y, 40))
							{
								Main.Map.Update(x, y, byte.MaxValue);
							}
						}
					}
				}
				else
				{
					for (int sx = 0; sx < Main.maxSectionsX; sx++)
					{
						for (int sy = 0; sy < Main.maxSectionsY; sy++)
						{
							if (Main.sectionManager == null || Main.sectionManager.SectionLoaded(sx, sy))
							{
								Main.Map.UnlockMapSection(sx, sy);
							}
						}
					}
				}
				Main.refreshMap = true;
				QueueMessage("Map Revealed", ClientChat.AccentColor);
			}
			catch (Exception ex)
			{
				QueueMessage("Reveal Map error!\n" + ex.Message, ClientChat.ErrorColor);
				ClientLog.LogException(ex);
			}
			finally
			{
				_refreshingMap = false;
			}
		});
	}

	public static void StartWorldDownload(bool revealFirst)
	{
		EnsureInitialized();
		if (Main.netMode != 1 || Main.gameMenu || !Netplay.Connection.IsActive)
		{
			Message("You can only do this in an online game.", ClientChat.ErrorColor);
			return;
		}
		if (Selection.SavingTEDITSchematic)
		{
			Message("You are saving a schematic, please wait for that operation to finish.", ClientChat.ErrorColor);
			return;
		}
		if (IsSaving || _downloadWhenDone)
		{
			Message("You are already saving a world! Please wait for the operation to finish.", ClientChat.ErrorColor);
			return;
		}
		int unloaded = CountUnloadedSections();
		if (revealFirst && unloaded > 0)
		{
			if (_revealing)
			{
				_downloadWhenDone = true;
				Message(unloaded + " sections are unloaded. The world will be downloaded when the map reveal finishes.", ClientChat.AccentColor);
				return;
			}
			Message(unloaded + " sections are unloaded. Revealing the map before downloading the world.", ClientChat.AccentColor);
			if (BeginRevealMap(RevealMode.Modern, automatic: true))
			{
				_downloadWhenDone = true;
			}
			return;
		}
		SaveDownloadedWorld();
	}

	private static void SaveDownloadedWorld()
	{
		if (Main.ActiveWorldFileData == null)
		{
			Message("There is no world data available.", ClientChat.ErrorColor);
			return;
		}
		if (Interlocked.CompareExchange(ref _saving, 1, 0) != 0)
		{
			Message("You are already saving a world! Please wait for the operation to finish.", ClientChat.ErrorColor);
			return;
		}
		int unloaded = CountUnloadedSections();
		if (unloaded > 0)
		{
			Message(unloaded + " sections are unloaded. Your world download will be incomplete!", ClientChat.InfoColor);
		}
		WorldFileData serverMetadata = Main.ActiveWorldFileData;
		FileMetadata serverFileMetadata = Main.WorldFileMetadata;
		Guid uniqueId = serverMetadata.UniqueId;
		ulong generatorVersion = serverMetadata.WorldGeneratorVersion;
		int worldId = serverMetadata.WorldId;
		string worldName = string.IsNullOrWhiteSpace(Main.worldName) ? "World" : Main.worldName;
		MapTools.WorldDownload = true;
		Message("Downloading world...", ClientChat.AccentColor);
		Task.Factory.StartNew(delegate
		{
			try
			{
				GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
				WorldFileData downloadMetadata = WorldFile.CreateMetadata(worldName, cloudSave: false, Main.GameMode);
				downloadMetadata.SetSeed("0");
				downloadMetadata.UniqueId = uniqueId;
				downloadMetadata.WorldGeneratorVersion = generatorVersion;
				downloadMetadata.WorldId = worldId;
				Main.ActiveWorldFileData = downloadMetadata;
				Main.WorldFileMetadata = FileMetadata.FromCurrentSettings(FileType.World);
				string path = downloadMetadata.Path;
				WorldFile.SaveWorldToLocalFile();
				QueueMessage("World download complete! Saved as " + Path.GetFileName(path) + " in Terraria/Worlds.", ClientChat.AccentColor);
			}
			catch (Exception ex)
			{
				QueueMessage("An error occured during world download!\n" + ex.Message, ClientChat.ErrorColor);
				ClientLog.LogException(ex);
			}
			finally
			{
				Main.ActiveWorldFileData = serverMetadata;
				Main.WorldFileMetadata = serverFileMetadata;
				MapTools.WorldDownload = false;
				Interlocked.Exchange(ref _saving, 0);
			}
		});
	}

	/// <summary>Hooked into WorldFile.SaveWorldTiles/SaveChests: sections that were never received hold null tiles.</summary>
	public static Tile NormalizeTile(Tile tile)
	{
		return tile ?? EmptyTile;
	}

	private static void StartNetworkLog(string reason)
	{
		lock (LogSync)
		{
			if (_networkLog != null)
			{
				Message("Network log already active: " + _networkLogPath, ClientChat.InfoColor);
				return;
			}
			try
			{
				Directory.CreateDirectory(ClientSettings.IsaClientFolder);
				_networkLogPath = Path.Combine(ClientSettings.IsaClientFolder, "WldDownload-network-" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
				_networkLog = new StreamWriter(new FileStream(_networkLogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
				_networkLog.AutoFlush = true;
				_networkLog.WriteLine("# Started=" + DateTime.Now.ToString("O") + " reason=" + reason + " world=" + Main.worldName + " tiles=" + Main.maxTilesX + "x" + Main.maxTilesY + " pending=" + CountUnloadedSections());
				Message("Network log active: " + _networkLogPath, ClientChat.AccentColor);
			}
			catch (Exception ex)
			{
				_networkLog = null;
				Message("Could not create the network log: " + ex.Message, ClientChat.ErrorColor);
			}
		}
	}

	private static void StopNetworkLog()
	{
		string path;
		lock (LogSync)
		{
			if (_networkLog == null)
			{
				Message("No network log is active.", ClientChat.InfoColor);
				return;
			}
			path = _networkLogPath;
			_networkLog.Dispose();
			_networkLog = null;
		}
		Message("Network log saved: " + path, ClientChat.AccentColor);
	}

	private static void Log(string text)
	{
		lock (LogSync)
		{
			_networkLog?.WriteLine(DateTime.Now.ToString("O") + " [tick=" + Main.GameUpdateCount + "] " + text);
		}
	}

	private static void QueueMessage(string text, Color color)
	{
		Main.QueueMainThreadAction(delegate
		{
			Message(text, color);
		});
	}

	private static void Message(string text, Color color)
	{
		Main.NewText(text, color);
	}
}
