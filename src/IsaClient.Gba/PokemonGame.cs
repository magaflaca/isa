using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using IsaClient.Core;
using IsaClient.Gba.Trainer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.IO;

namespace IsaClient.Gba;

/// <summary>
/// Pokémon FireRed inside Terraria: the user's own ROM (never shipped), the mGBA core embedded in the client, and the
/// player sprite swapped for their Terraria character (no helmet or armor) with <see cref="TrainerBuilder"/>. The game's
/// save lives in IsaClient/GBA/Saves and a "continue where you left" state is kept when the window closes. No scores:
/// it's just for fun. The window is <see cref="PokemonWindow"/>. Nintendo DS games run too (PokemonGame.Ds.cs), on an
/// emulator downloaded the first time one is played.
/// </summary>
public static partial class PokemonGame
{
	private const string CoreResource = "IsaClient.Gba.mgba_libretro.dll.gz";

	/// <summary>Where the ROM is (the user picks it; it's only read).</summary>
	[Setting("GbaRomPath")]
	public static string RomPath = "";

	/// <summary>"auto" the character you're playing, "original" Red/Leaf, else a .plr path.</summary>
	[Setting("GbaCharacter")]
	public static string Character = "auto";

	/// <summary>0 by gender, 1 Red's body, 2 Leaf's body.</summary>
	[Setting("GbaBody", Min = 0, Max = 2)]
	public static int Body;

	/// <summary>Screen zoom: 2, 3 or 4 GBA pixels a screen pixel.</summary>
	[Setting("GbaScale", Min = 2, Max = 4)]
	/// <summary>Screen size; 3x by default (2x when 3x doesn't fit the screen). The size button changes and saves it.</summary>
	public static int Scale = 3;

	[Setting("GbaVolume", Min = 0, Max = 100)]
	public static int Volume = 70;

	/// <summary>Opening the window goes on from where it was closed.</summary>
	[Setting("GbaResume")]
	public static bool Resume = true;

	/// <summary>Smooth the screen when it's zoomed (off = sharp pixels).</summary>
	[Setting("GbaSmooth")]
	public static bool Smooth;

	public static string Folder => Path.Combine(ClientSettings.IsaClientFolder, "GBA");

	private static byte[] _rom;

	private static string _romLoadedFrom;

	private static GbaMachine _machine;

	private static GbaAudio _audio;

	private static Texture2D _screen;

	private static uint[] _pixels = new uint[GbaMachine.Width * GbaMachine.Height];

	private static int _serial = -1;

	private static Task<TrainerResult> _building;

	private static string _buildingKey;

	private static TrainerResult _trainer;

	private static string _trainerKey;

	/// <summary>Last problem to show in the window (null when all is fine).</summary>
	public static string Problem;

	/// <summary>A short message the window shows for a moment ("Saved", "State loaded"...).</summary>
	public static string Toast;

	public static double ToastAt = -10.0;

	public static GbaMachine Machine => (_machine != null && _machine.Running) ? _machine : null;

	public static bool Playing => Machine != null;

	public static Texture2D Screen => _screen;

	public static bool Building => _building != null && !_building.IsCompleted;

	/// <summary>The current character as a FireRed player (null while building or with the original look).</summary>
	public static TrainerResult Trainer => _trainer;

	public static string RomName { get; private set; } = "";

	public static string RomCode { get; private set; } = "";

	#region The ROM

	public static bool HasRom => LoadRom();

	/// <summary>The ROM as read from disk (never changed; patched copies are made from it).</summary>
	internal static byte[] OriginalRom => LoadRom() ? _rom : null;

	/// <summary>The game can turn its player into a Terraria character (Pokémon FireRed, LeafGreen, Emerald).</summary>
	public static bool SupportsTrainer => LoadRom() && !IsDs && TrainerGame.For(_rom) != null;

	/// <summary>The trainer family of the game (null for games without one).</summary>
	public static TrainerGame TrainerFamily => (LoadRom() && !IsDs) ? TrainerGame.For(_rom) : null;

	/// <summary>Reads the ROM once (and again when the path changes). False when it's missing or not a GBA game.</summary>
	public static bool LoadRom()
	{
		GbaConfig.EnsureLoaded();
		if (_rom != null && _romLoadedFrom == RomPath)
		{
			return true;
		}
		_rom = null;
		IsDs = false;
		if (string.IsNullOrEmpty(RomPath) || !File.Exists(RomPath))
		{
			return false;
		}
		try
		{
			byte[] head = ReadHead(RomPath);
			if (head != null && DsRom.IsDs(head))
			{
				// a DS game is only read by its emulator: here just its header (the file can be hundreds of MB)
				_rom = head;
				_romLoadedFrom = RomPath;
				IsDs = true;
				RomCode = DsRom.Code(head);
				RomName = DsRom.Title(RomPath, head);
				_trainer = null;
				_trainerKey = null;
				return true;
			}
			IsDs = false;
			byte[] data = File.ReadAllBytes(RomPath);
			string why = CheckRom(data);
			if (why != null)
			{
				Problem = why;
				return false;
			}
			_rom = data;
			_romLoadedFrom = RomPath;
			RomCode = FireRedRom.GameCode(data);
			RomName = GameTitle(data);
			_trainer = null;
			_trainerKey = null;
			return true;
		}
		catch (Exception ex)
		{
			Problem = "Couldn't read the ROM: " + ex.Message;
			return false;
		}
	}

	/// <summary>Why a file can't be used (null when it's a Game Boy Advance or Nintendo DS ROM).</summary>
	public static string CheckRom(byte[] data)
	{
		if (DsRom.IsDs(data))
		{
			return null;
		}
		if (data == null || data.Length < 0x200 || data[0xB2] != 0x96)
		{
			return "That file isn't a Game Boy Advance or DS game.";
		}
		return null;
	}

	/// <summary>A friendly name: known games by their code, else the cartridge's header title.</summary>
	public static string GameTitle(byte[] data)
	{
		if (DsRom.IsDs(data))
		{
			return DsRom.HeaderTitle(data);
		}
		string code = FireRedRom.GameCode(data);
		string lang = (code.Length == 4) ? code[3] switch
		{
			'E' => "English",
			'S' => "Spanish",
			'F' => "French",
			'D' => "German",
			'I' => "Italian",
			'J' => "Japanese",
			_ => code.Substring(3)
		} : "";
		if (code.StartsWith("BPR"))
		{
			return "Pokémon FireRed (" + lang + ")";
		}
		if (code.StartsWith("BPG"))
		{
			return "Pokémon LeafGreen (" + lang + ")";
		}
		if (code.StartsWith("BPE"))
		{
			return "Pokémon Emerald (" + lang + ")";
		}
		if (code.StartsWith("AX4"))
		{
			return "Super Mario Advance 4";
		}
		if (code.StartsWith("BZM"))
		{
			return "Zelda: The Minish Cap";
		}
		string title = System.Text.Encoding.ASCII.GetString(data, 0xA0, 12).TrimEnd('\0', ' ');
		return (title.Length > 0) ? title : "an unknown game";
	}

	/// <summary>Makes <paramref name="path"/> the game in the console (the one playing is turned off first).</summary>
	public static bool SelectGame(string path)
	{
		if (string.Equals(path, RomPath, StringComparison.OrdinalIgnoreCase) && LoadRom())
		{
			return true;
		}
		Stop();
		GbaSaves.Forget();
		RomPath = path;
		_romLoadedFrom = null;
		Problem = null;
		GbaConfig.Save();
		return LoadRom();
	}

	/// <summary>
	/// Adds games to the library: .gba files stay where they are; a .zip's ROMs are copied out to IsaClient/GBA/Roms.
	/// Returns the first game added (null if none was a GBA ROM).
	/// </summary>
	public static string AddGames(IEnumerable<string> files)
	{
		string first = null;
		foreach (string file in files)
		{
			try
			{
				foreach (string rom in RomsIn(file))
				{
					byte[] head = ReadHead(rom);
					if (CheckRom(head) != null)
					{
						Problem = Path.GetFileName(rom) + ": that isn't a Game Boy Advance or DS game.";
						continue;
					}
					if (!GbaConfig.Games.Exists(g => string.Equals(g, rom, StringComparison.OrdinalIgnoreCase)))
					{
						GbaConfig.Games.Add(rom);
					}
					first ??= rom;
				}
			}
			catch (Exception ex)
			{
				Problem = "Couldn't add " + Path.GetFileName(file) + ": " + ex.Message;
				ClientLog.LogException(ex);
			}
		}
		GbaConfig.Save();
		return first;
	}

	/// <summary>A .gba or .nds as it is, or the games inside a .zip (copied out once).</summary>
	private static IEnumerable<string> RomsIn(string file)
	{
		if (!file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
		{
			return new[] { file };
		}
		List<string> roms = new List<string>();
		string dir = Path.Combine(Folder, "Roms");
		Directory.CreateDirectory(dir);
		using (Ionic.Zip.ZipFile zip = Ionic.Zip.ZipFile.Read(file))
		{
			foreach (Ionic.Zip.ZipEntry entry in zip)
			{
				if (entry.IsDirectory || !(entry.FileName.EndsWith(".gba", StringComparison.OrdinalIgnoreCase) || DsRom.IsDsFile(entry.FileName)))
				{
					continue;
				}
				string target = Path.Combine(dir, Path.GetFileName(entry.FileName));
				if (!File.Exists(target) || new FileInfo(target).Length != entry.UncompressedSize)
				{
					using FileStream o = File.Create(target);
					entry.Extract(o);
				}
				roms.Add(target);
			}
		}
		if (roms.Count == 0)
		{
			Problem = Path.GetFileName(file) + " has no .gba or .nds inside.";
		}
		return roms;
	}

	/// <summary>The cartridge header (first 0xC0 bytes) of a ROM file, or null.</summary>
	public static byte[] ReadHead(string path)
	{
		try
		{
			using FileStream s = File.OpenRead(path);
			byte[] b = new byte[Math.Min(0x200, (int)s.Length)];
			s.Read(b, 0, b.Length);
			return b;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>Takes a game out of the library (its saves stay on disk).</summary>
	public static void RemoveGame(string path)
	{
		if (string.Equals(path, RomPath, StringComparison.OrdinalIgnoreCase))
		{
			Stop();
			RomPath = "";
			_rom = null;
			_romLoadedFrom = null;
			GbaSaves.Forget();
		}
		GbaConfig.Games.RemoveAll(g => string.Equals(g, path, StringComparison.OrdinalIgnoreCase));
		if (RomPath.Length == 0 && GbaConfig.Games.Count > 0)
		{
			RomPath = GbaConfig.Games[0];
		}
		GbaConfig.Save();
	}

	/// <summary>The key a game's saves go under (for any ROM file, without loading it).</summary>
	public static string KeyOf(byte[] head)
	{
		if (DsRom.IsDs(head))
		{
			return DsRom.Key(head);
		}
		uint crc = 0;
		for (int i = 0xA0; i < 0xC0 && i < head.Length; i++)
		{
			crc = (crc * 31) + head[i];
		}
		string code = FireRedRom.GameCode(head);
		return (code.Length == 4 ? code : "GBA") + "-" + crc.ToString("x8");
	}

	/// <summary>The picture of where a game was left (saved when it's turned off), or null.</summary>
	public static string ResumePicture(byte[] head) => Path.Combine(Folder, "States", StateKey(head) + ".resume.state.png");

	/// <summary>The key of a game's states: a DS game's states are per emulator (they don't fit the other one).</summary>
	private static string StateKey(byte[] head) => KeyOf(head) + (DsRom.IsDs(head) ? ("." + DsEngine.ById(DsEngineId).Id) : "");

	/// <summary>Saves and states are per game and language (any copy of the same ROM shares them).</summary>
	private static string GameKey => (_rom != null) ? KeyOf(_rom) : "GBA-00000000";

	public static string SavePath => Path.Combine(Folder, "Saves", GameKey + ".sav");

	public static string StatePath(string slot) => Path.Combine(Folder, "States", ((_rom != null) ? StateKey(_rom) : GameKey) + "." + slot + ".state");

	public static bool HasResume => _rom != null && File.Exists(StatePath("resume"));

	public static bool HasSave => _rom != null && (IsDs ? DsHasSave : File.Exists(SavePath));

	#endregion

	#region Characters

	/// <summary>Characters to choose from (the game's list, without armor).</summary>
	public static List<PlayerFileData> Characters()
	{
		if (Main.PlayerList == null || Main.PlayerList.Count == 0)
		{
			try
			{
				Main.LoadPlayers();
			}
			catch
			{
			}
		}
		return Main.PlayerList?.Where(p => p?.Player != null).ToList() ?? new List<PlayerFileData>();
	}

	/// <summary>The file chosen now ("auto" resolves to the character you're playing).</summary>
	public static PlayerFileData ChosenFile()
	{
		if (Character == "original")
		{
			return null;
		}
		if (Character == "auto" || string.IsNullOrEmpty(Character))
		{
			return Main.ActivePlayerFileData;
		}
		return Characters().FirstOrDefault(p => string.Equals(p.Path, Character, StringComparison.OrdinalIgnoreCase)) ?? Main.ActivePlayerFileData;
	}

	public static TrainerLook LookOf(Player p)
	{
		Rgb C(Color c) => new Rgb(c.R, c.G, c.B);
		return new TrainerLook
		{
			Name = p.name,
			Hair = (p.hair >= 0 && p.hair < 228) ? p.hair : 0,
			SkinVariant = p.skinVariant,
			HairColor = C(p.hairColor),
			SkinColor = C(p.skinColor),
			EyeColor = C(p.eyeColor),
			ShirtColor = C(p.shirtColor),
			UndershirtColor = C(p.underShirtColor),
			PantsColor = C(p.pantsColor),
			ShoeColor = C(p.shoeColor)
		};
	}

	private static bool? BodyChoice => Body switch
	{
		1 => true,
		2 => false,
		_ => null
	};

	/// <summary>Builds (in the background) the patched ROM for <paramref name="look"/>, or returns the cached one.</summary>
	public static Task<TrainerResult> BuildFor(TrainerLook look)
	{
		if (!LoadRom())
		{
			return Task.FromResult<TrainerResult>(null);
		}
		if (IsDs || TrainerGame.For(_rom) == null)
		{
			return Task.FromResult<TrainerResult>(null);
		}
		string key = RomCode + "|" + look.Key + "|" + Body;
		if (_trainerKey == key && _trainer != null)
		{
			return Task.FromResult(_trainer);
		}
		if (_buildingKey == key && _building != null)
		{
			return _building;
		}
		byte[] rom = _rom;
		bool? body = BodyChoice;
		_buildingKey = key;
		_building = Task.Run(() => TrainerBuilder.Build(rom, look, body));
		_building.ContinueWith(t =>
		{
			if (t.Status == TaskStatus.RanToCompletion && _buildingKey == key)
			{
				_trainer = t.Result;
				_trainerKey = key;
			}
			else if (t.Exception != null)
			{
				Problem = "Couldn't make your character: " + t.Exception.GetBaseException().Message;
				ClientLog.LogException(t.Exception.GetBaseException());
			}
		});
		return _building;
	}

	/// <summary>The ROM to run: the original, or with the chosen character (built now if needed).</summary>
	private static async Task<byte[]> RomToRun()
	{
		PlayerFileData file = ChosenFile();
		if (file?.Player == null || TrainerGame.For(_rom) == null)
		{
			return _rom;
		}
		try
		{
			TrainerResult r = await BuildFor(LookOf(file.Player));
			return r?.Rom ?? _rom;
		}
		catch (Exception ex)
		{
			ClientLog.LogOnce(ex.GetBaseException());
			return _rom;
		}
	}

	#endregion

	#region Running

	/// <summary>Turns the console on: from the resume state when <paramref name="resume"/>, else from the game's own save.</summary>
	public static async void Start(bool resume)
	{
		if (Main.gameMenu || !LoadRom())
		{
			return;
		}
		if (IsDs)
		{
			StartDs(resume);
			return;
		}
		try
		{
			Problem = null;
			if (!EnsureCore())
			{
				return;
			}
			byte[] rom = await RomToRun();
			byte[] state = null;
			byte[] statePal = null;
			if (resume && HasResume)
			{
				state = File.ReadAllBytes(StatePath("resume"));
				statePal = ReadPal(StatePath("resume"));
			}
			Launch(rom, state, statePal);
		}
		catch (Exception ex)
		{
			Problem = "Couldn't start the game: " + ex.Message;
			ClientLog.LogException(ex);
		}
	}

	/// <summary>Player + reflection palettes of the ROM running now (what states saved now will have in memory).</summary>
	private static byte[] _runningPal;

	internal static byte[] RunningPal => _runningPal;

	private static byte[] _originalPal;

	private static byte[] _originalPalOf;

	/// <summary>The player's and its reflection's palettes in a ROM (64 bytes), null if they can't be found.</summary>
	private static byte[] PalettesOf(byte[] rom)
	{
		try
		{
			FireRedRom r = new FireRedRom(rom);
			byte[] pal = new byte[64];
			Buffer.BlockCopy(r.RawPalette(FireRedRom.PalTagPlayer), 0, pal, 0, 32);
			Buffer.BlockCopy(r.RawPalette(FireRedRom.PalTagReflection), 0, pal, 32, 32);
			return pal;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>The original palettes of the game's other genders (Emerald's May has her own), 64 bytes each.</summary>
	private static IEnumerable<byte[]> OriginalExtraPalettes()
	{
		TrainerGame family = (_rom != null) ? TrainerGame.For(_rom) : null;
		if (family == null || family.PaletteTags.Length < 2)
		{
			yield break;
		}
		FireRedRom r;
		try
		{
			r = new FireRedRom(_rom);
		}
		catch
		{
			yield break;
		}
		for (int i = 1; i < family.PaletteTags.Length; i++)
		{
			byte[] pal = new byte[64];
			Buffer.BlockCopy(r.RawPalette(family.PaletteTags[i].Player), 0, pal, 0, 32);
			Buffer.BlockCopy(r.RawPalette(family.PaletteTags[i].Reflection), 0, pal, 32, 32);
			yield return pal;
		}
	}

	private static byte[] ReadPal(string statePath)
	{
		try
		{
			string p = statePath + ".pal";
			return File.Exists(p) ? File.ReadAllBytes(p) : null;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// A state keeps the player's palette of whatever trainer was used when its map loaded: before loading it, the
	/// palette slots in it get <paramref name="newPal"/> (see <see cref="StatePalette"/>).
	/// </summary>
	internal static void Recolor(byte[] state, byte[] statePal, byte[] newPal)
	{
		if (state == null || newPal == null || newPal.Length != 64)
		{
			return;
		}
		if (_originalPalOf != _rom)
		{
			_originalPal = (_rom != null) ? PalettesOf(_rom) : null;
			_originalPalOf = _rom;
		}
		List<byte[]> oldPlayers = new List<byte[]>();
		List<byte[]> oldReflections = new List<byte[]>();
		List<byte[]> known = new List<byte[]> { statePal, _originalPal };
		known.AddRange(OriginalExtraPalettes());
		foreach (byte[] old in known)
		{
			if (old != null && old.Length == 64)
			{
				oldPlayers.Add(old.Take(32).ToArray());
				oldReflections.Add(old.Skip(32).ToArray());
			}
		}
		StatePalette.Patch(state, newPal.Take(32).ToArray(), newPal.Skip(32).ToArray(), oldPlayers, oldReflections, out _);
	}

	/// <summary>Turns the console on at a save slot's moment (its cartridge save goes in too).</summary>
	internal static async void StartWith(byte[] state, byte[] sram, byte[] statePal)
	{
		if (Main.gameMenu || !LoadRom())
		{
			return;
		}
		if (IsDs)
		{
			LaunchDs(state, sram);
			return;
		}
		try
		{
			Problem = null;
			if (!EnsureCore())
			{
				return;
			}
			byte[] rom = await RomToRun();
			Launch(rom, state, statePal, sram);
		}
		catch (Exception ex)
		{
			Problem = "Couldn't start the game: " + ex.Message;
			ClientLog.LogException(ex);
		}
	}

	private static void Launch(byte[] rom, byte[] state, byte[] statePal = null, byte[] sram = null)
	{
		StopMachine(false);
		byte[] pal = PalettesOf(rom);
		if (state != null)
		{
			state = (byte[])state.Clone();
			Recolor(state, statePal, pal);
		}
		GbaMachine m = GbaMachine.Start(rom, SavePath, state, sram);
		m.WaitLoaded(3000);
		_runningPal = pal;
		Attach(m);
	}

	/// <summary>The sound for a machine that just started, and it becomes the one playing.</summary>
	private static void Attach(GbaMachine m)
	{
		_audio?.Dispose();
		try
		{
			_audio = new GbaAudio(m.SampleRate, m.Fps) { Volume = Volume / 100f };
			GbaAudio audio = _audio;
			m.AudioOut = audio.Submit;
			m.AudioQueued = () => audio.Queued;
		}
		catch (Exception ex)
		{
			// no sound device: play without sound
			ClientLog.LogException(ex);
			_audio = null;
		}
		_machine = m;
		_serial = -1;
	}

	/// <summary>The character or body changed while playing: same moment, new look (shows from the next map).</summary>
	public static async void ApplyCharacter()
	{
		GbaMachine m = Machine;
		if (m == null)
		{
			return;
		}
		byte[] rom = await RomToRun();
		byte[] oldPal = _runningPal;
		m.SaveState(state => Main.QueueMainThreadAction(() =>
		{
			if (Machine == m)
			{
				Launch(rom, state, oldPal);
				ShowToast("New look!");
			}
		}));
	}

	/// <summary>Stops, keeping the moment to continue from it.</summary>
	public static void Stop() => StopMachine(true);

	private static void StopMachine(bool keepResume)
	{
		GbaMachine m = _machine;
		if (m == null)
		{
			return;
		}
		_machine = null;
		if (keepResume && m.Running)
		{
			string path = StatePath("resume");
			byte[] pal = _runningPal;
			System.Threading.ManualResetEventSlim done = new System.Threading.ManualResetEventSlim();
			m.Snapshot((state, sram, frame, width) =>
			{
				if (WriteFile(path, state))
				{
					if (pal != null)
					{
						WriteFile(path + ".pal", pal);
					}
					GbaSaves.SavePng(path + ".png", frame, width);
				}
				done.Set();
			});
			done.Wait(2000);
		}
		m.Dispose();
		_audio?.Dispose();
		_audio = null;
		// Terraria's sound comes back right away (the menu may not run the games' update)
		IsaClient.Music.MediaFocus.GameSoundScale = 1f;
	}

	public static void Reset()
	{
		Machine?.Reset();
		ShowToast("Reset");
	}

	private static bool WriteFile(string path, byte[] data)
	{
		if (data == null)
		{
			return false;
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllBytes(path + ".tmp", data);
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			File.Move(path + ".tmp", path);
			return true;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			return false;
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
		if (_audio != null)
		{
			_audio.Volume = Volume / 100f;
		}
	}

	/// <summary>Extracts the embedded mGBA core next to the settings and loads it (once).</summary>
	private static bool EnsureCore()
	{
		try
		{
			string libs = Path.Combine(ClientSettings.IsaClientFolder, "Libraries");
			string path = Path.Combine(libs, "mgba_libretro.dll");
			using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(CoreResource))
			{
				if (s == null)
				{
					Problem = "The GBA core isn't in this build.";
					return false;
				}
				using GZipStream gz = new GZipStream(s, CompressionMode.Decompress);
				using MemoryStream ms = new MemoryStream();
				gz.CopyTo(ms);
				byte[] bytes = ms.ToArray();
				Directory.CreateDirectory(libs);
				if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length || !File.ReadAllBytes(path).SequenceEqual(bytes))
				{
					try
					{
						File.WriteAllBytes(path, bytes);
					}
					catch (IOException)
					{
						// another client has it loaded: it's the same file
					}
				}
			}
			GbaMachine.EnsureCore(path, Path.Combine(Folder, "System"));
			return true;
		}
		catch (Exception ex)
		{
			Problem = "Couldn't load the GBA core: " + ex.Message;
			ClientLog.LogException(ex);
			return false;
		}
	}

	#endregion

	#region Every frame

	/// <summary>Buttons held this frame (libretro bits), for the window's drawing.</summary>
	public static int Held;

	/// <summary>Game loop: the keys go to the console while the window has them; the newest picture goes to the screen.</summary>
	private static bool _focusHooked;

	/// <summary>Terraria's music goes quiet while the console plays (like for Tetris), and its sounds fade out.</summary>
	private static void UpdateGameSound()
	{
		if (!_focusHooked)
		{
			_focusHooked = true;
			IsaClient.Music.MediaFocus.ConsolePlaying = () => SoundOn || IsaClient.Psp.PspGame.SoundOn;
		}
		bool on = SoundOn || IsaClient.Psp.PspGame.SoundOn;
		float target = on ? 0f : 1f;
		float now = IsaClient.Music.MediaFocus.GameSoundScale;
		if (now != target)
		{
			IsaClient.Music.MediaFocus.GameSoundScale = (Math.Abs(target - now) < 0.05f) ? target : (now + Math.Sign(target - now) * 0.05f);
		}
		if (on && IsaClient.Music.MediaFocus.GameSoundScale == 0f)
		{
			// rain, wind and other ambient tracks don't go through the music volume: hold every fade at zero
			for (int i = 0; i < Main.musicFade.Length; i++)
			{
				Main.musicFade[i] = 0f;
			}
		}
	}

	/// <summary>The console is on, playing and not muted.</summary>
	private static bool SoundOn
	{
		get
		{
			GbaMachine m = Machine;
			return m != null && !m.Paused && Volume > 0;
		}
	}

	public static void Update(bool hasKeys)
	{
		GbaConfig.EnsureLoaded();
		UpdateGameSound();
		GbaMachine m = Machine;
		if (_machine != null && m == null)
		{
			// the machine stopped by itself (an error)
			Problem = _machine.Error ?? "The game stopped.";
			StopMachine(false);
			return;
		}
		if (m == null)
		{
			Held = 0;
			return;
		}
		int buttons = 0;
		bool fast = false;
		bool swap = false;
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
			Map(GbaConfig.Down(k, GbaButton.Up), Libretro.Up);
			Map(GbaConfig.Down(k, GbaButton.Down), Libretro.Down);
			Map(GbaConfig.Down(k, GbaButton.Left), Libretro.Left);
			Map(GbaConfig.Down(k, GbaButton.Right), Libretro.Right);
			Map(GbaConfig.Down(k, GbaButton.A), Libretro.A);
			Map(GbaConfig.Down(k, GbaButton.B), Libretro.B);
			Map(GbaConfig.Down(k, GbaButton.L), Libretro.L);
			Map(GbaConfig.Down(k, GbaButton.R), Libretro.R);
			Map(GbaConfig.Down(k, GbaButton.Start), Libretro.Start);
			Map(GbaConfig.Down(k, GbaButton.Select), Libretro.Select);
			Map(GbaConfig.Down(k, GbaButton.X), Libretro.X);
			Map(GbaConfig.Down(k, GbaButton.Y), Libretro.Y);
			swap = GbaConfig.Down(k, GbaButton.Swap);
			fast = GbaConfig.Down(k, GbaButton.Fast);
			try
			{
				GamePadState g = GamePad.GetState(PlayerIndex.One);
				if (g.IsConnected)
				{
					Map(g.DPad.Up == ButtonState.Pressed || g.ThumbSticks.Left.Y > 0.5f, Libretro.Up);
					Map(g.DPad.Down == ButtonState.Pressed || g.ThumbSticks.Left.Y < -0.5f, Libretro.Down);
					Map(g.DPad.Left == ButtonState.Pressed || g.ThumbSticks.Left.X < -0.5f, Libretro.Left);
					Map(g.DPad.Right == ButtonState.Pressed || g.ThumbSticks.Left.X > 0.5f, Libretro.Right);
					Map(g.Buttons.A == ButtonState.Pressed, Libretro.A);
					if (IsDs)
					{
						// the DS has all four: each Xbox button is the DS one with its letter
						Map(g.Buttons.B == ButtonState.Pressed, Libretro.B);
						Map(g.Buttons.X == ButtonState.Pressed, Libretro.X);
						Map(g.Buttons.Y == ButtonState.Pressed, Libretro.Y);
						swap |= g.Triggers.Left > 0.5f;
					}
					else
					{
						Map(g.Buttons.B == ButtonState.Pressed || g.Buttons.X == ButtonState.Pressed, Libretro.B);
					}
					Map(g.Buttons.LeftShoulder == ButtonState.Pressed, Libretro.L);
					Map(g.Buttons.RightShoulder == ButtonState.Pressed, Libretro.R);
					Map(g.Buttons.Start == ButtonState.Pressed, Libretro.Start);
					Map(g.Buttons.Back == ButtonState.Pressed, Libretro.Select);
					fast |= g.Triggers.Right > 0.5f;
				}
			}
			catch
			{
			}
		}
		if (!IsDs)
		{
			// the GBA has no X and Y (their keys may be free for other things)
			buttons &= ~((1 << Libretro.X) | (1 << Libretro.Y));
		}
		if (swap && !_swapHeld && IsDs)
		{
			SwapScreens();
		}
		_swapHeld = swap;
		m.Buttons = buttons;
		Held = buttons;
		MeasureSpeed(m);
		m.Speed = fast ? 3 : 1;
		// Paused while Terraria is in the background, so it never plays alone.
		m.Paused = !Main.hasFocus || PokemonWindow.Minimized;
		if (m.CopyFrame(ref _pixels, ref _serial, out int fw, out int fh))
		{
			try
			{
				if (_screen == null || _screen.IsDisposed || _screen.Width != fw || _screen.Height != fh)
				{
					_screen?.Dispose();
					_screen = new Texture2D(Main.instance.GraphicsDevice, fw, fh);
				}
				Main.instance.GraphicsDevice.Textures[0] = null;
				_screen.SetData(_pixels);
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		}
	}

	#endregion
}
