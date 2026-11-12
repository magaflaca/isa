using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace IsaClient.Gba;

/// <summary>
/// A libretro core (mGBA for the GBA, melonDS for the DS, PPSSPP for the PSP; x86) loaded with LoadLibrary: the exports as
/// delegates and the callbacks it needs. One instance per core DLL (libretro cores keep global state). Every call must
/// come from the same thread. Cores that draw on the graphics card get <see cref="LibretroGl"/>.
/// </summary>
internal sealed class Libretro : IDisposable
{
	#region Native

	[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern IntPtr LoadLibrary(string path);

	[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
	private static extern IntPtr GetProcAddress(IntPtr module, string name);

	[DllImport("kernel32", SetLastError = true)]
	private static extern bool FreeLibrary(IntPtr module);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.U1)]
	public delegate bool EnvironmentFn(uint cmd, IntPtr data);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void VideoRefreshFn(IntPtr data, uint width, uint height, UIntPtr pitch);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void AudioSampleFn(short left, short right);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate UIntPtr AudioSampleBatchFn(IntPtr data, UIntPtr frames);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void InputPollFn();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate short InputStateFn(uint port, uint device, uint index, uint id);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void VoidFn();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate uint UIntFn();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void SetCallbackFn(IntPtr callback);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void InfoFn(IntPtr info);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.U1)]
	private delegate bool LoadGameFn(IntPtr gameInfo);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate UIntPtr SizeFn();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.U1)]
	private delegate bool SerializeFn(IntPtr data, UIntPtr size);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate IntPtr MemoryDataFn(uint id);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate UIntPtr MemorySizeFn(uint id);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void PortDeviceFn(uint port, uint device);

	/// <summary>retro_log_printf_t: it's variadic, so the first arguments are taken as they come (cdecl: the caller cleans up).</summary>
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void LogFn(int level, IntPtr format, IntPtr a, IntPtr b, IntPtr c, IntPtr d);

	#endregion

	public const uint DeviceJoypad = 1;

	public const uint DeviceAnalog = 5;

	public const uint DevicePointer = 6;

	public const uint MemorySaveRam = 0;

	/// <summary>RETRO_DEVICE_ID_JOYPAD_* bits.</summary>
	public const int B = 0, Y = 1, Select = 2, Start = 3, Up = 4, Down = 5, Left = 6, Right = 7, A = 8, X = 9, L = 10, R = 11;

	private IntPtr _module;

	private readonly VoidFn _init;

	private readonly VoidFn _deinit;

	private readonly UIntFn _apiVersion;

	private readonly SetCallbackFn _setEnvironment;

	private readonly SetCallbackFn _setVideoRefresh;

	private readonly SetCallbackFn _setAudioSample;

	private readonly SetCallbackFn _setAudioSampleBatch;

	private readonly SetCallbackFn _setInputPoll;

	private readonly SetCallbackFn _setInputState;

	private readonly InfoFn _getSystemInfo;

	private readonly InfoFn _getAvInfo;

	private readonly LoadGameFn _loadGame;

	private readonly VoidFn _unloadGame;

	private readonly VoidFn _run;

	private readonly VoidFn _reset;

	private readonly SizeFn _serializeSize;

	private readonly SerializeFn _serialize;

	private readonly SerializeFn _unserialize;

	private readonly MemoryDataFn _memoryData;

	private readonly MemorySizeFn _memorySize;

	private readonly PortDeviceFn _setPortDevice;

	// Kept alive: the core holds raw pointers to them.
	private EnvironmentFn _env;

	private VideoRefreshFn _video;

	private AudioSampleFn _audio;

	private AudioSampleBatchFn _audioBatch;

	private InputPollFn _poll;

	private InputStateFn _input;

	private LogFn _log;

	private VoidFn _hwReset;

	private VoidFn _hwDestroy;

	private bool _hwBottomLeft;

	private bool _hwActive;

	private IntPtr _systemDir;

	private IntPtr _saveDir;

	private IntPtr _game;

	private IntPtr _gameData;

	private bool _gameLoaded;

	private bool _inited;

	/// <summary>0 0RGB1555, 1 XRGB8888, 2 RGB565.</summary>
	public int PixelFormat { get; private set; }

	public double Fps { get; private set; } = 59.7275;

	public double SampleRate { get; private set; } = 32768.0;

	public string Name { get; private set; } = "";

	public string Version { get; private set; } = "";

	/// <summary>The core reads the game from its file (the ROM isn't passed in memory).</summary>
	public bool NeedFullPath { get; private set; }

	/// <summary>The biggest picture the core makes (the DS: both screens).</summary>
	public int MaxWidth { get; private set; } = 256;

	public int MaxHeight { get; private set; } = 384;

	/// <summary>The core may draw on the graphics card (see <see cref="LibretroGl"/>). Set before loading a game.</summary>
	public bool UsesGraphicsCard;

	/// <summary>The core draws on the graphics card in this game (it asked and OpenGL is there).</summary>
	public bool OnGraphicsCard => _hwActive;

	/// <summary>Paths go to the core in UTF-8 (the system's own code page otherwise, as older cores expect).</summary>
	public bool Utf8Paths;

	/// <summary>Settings not in <see cref="Options"/> are answered with the first value the core offers, its default.</summary>
	public bool AnswerDefaults;

	/// <summary>Where the core's messages go (null: nowhere). Called on whatever thread the core logs from.</summary>
	public Action<int, string> Log;

	/// <summary>The core gave up (a game that doesn't boot): it mustn't run again.</summary>
	public volatile bool ShutdownAsked;

	/// <summary>The last error the core logged.</summary>
	public string LastError { get; private set; }

	/// <summary>The core's settings, answered when it asks (GET_VARIABLE). Set before loading a game.</summary>
	public readonly Dictionary<string, string> Options = new Dictionary<string, string>();

	/// <summary>The settings the core offers: key, "Description; default|other|...".</summary>
	public readonly Dictionary<string, string> Offered = new Dictionary<string, string>();

	private readonly Dictionary<string, IntPtr> _optionText = new Dictionary<string, IntPtr>();

	private volatile bool _optionsChanged;

	/// <summary>Settings for the next game: read when it loads, so the core isn't told they changed.</summary>
	public void PutOptions(IDictionary<string, string> options)
	{
		lock (Options)
		{
			foreach (KeyValuePair<string, string> o in options)
			{
				Options[o.Key] = o.Value;
			}
		}
	}

	/// <summary>Changes a setting while a game runs (the core picks it up on its next frame).</summary>
	public void SetOption(string key, string value)
	{
		lock (Options)
		{
			Options[key] = value;
		}
		_optionsChanged = true;
	}

	public Libretro(string dllPath)
	{
		_module = LoadLibrary(dllPath);
		if (_module == IntPtr.Zero)
		{
			throw new InvalidOperationException("LoadLibrary failed for the core (error " + Marshal.GetLastWin32Error() + ")");
		}
		T Fn<T>(string name) where T : Delegate
		{
			IntPtr p = GetProcAddress(_module, name);
			if (p == IntPtr.Zero)
			{
				throw new MissingMethodException("The core has no " + name);
			}
			return (T)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
		}
		_init = Fn<VoidFn>("retro_init");
		_deinit = Fn<VoidFn>("retro_deinit");
		_apiVersion = Fn<UIntFn>("retro_api_version");
		_setEnvironment = Fn<SetCallbackFn>("retro_set_environment");
		_setVideoRefresh = Fn<SetCallbackFn>("retro_set_video_refresh");
		_setAudioSample = Fn<SetCallbackFn>("retro_set_audio_sample");
		_setAudioSampleBatch = Fn<SetCallbackFn>("retro_set_audio_sample_batch");
		_setInputPoll = Fn<SetCallbackFn>("retro_set_input_poll");
		_setInputState = Fn<SetCallbackFn>("retro_set_input_state");
		_getSystemInfo = Fn<InfoFn>("retro_get_system_info");
		_getAvInfo = Fn<InfoFn>("retro_get_system_av_info");
		_loadGame = Fn<LoadGameFn>("retro_load_game");
		_unloadGame = Fn<VoidFn>("retro_unload_game");
		_run = Fn<VoidFn>("retro_run");
		_reset = Fn<VoidFn>("retro_reset");
		_serializeSize = Fn<SizeFn>("retro_serialize_size");
		_serialize = Fn<SerializeFn>("retro_serialize");
		_unserialize = Fn<SerializeFn>("retro_unserialize");
		_memoryData = Fn<MemoryDataFn>("retro_get_memory_data");
		_memorySize = Fn<MemorySizeFn>("retro_get_memory_size");
		_setPortDevice = Fn<PortDeviceFn>("retro_set_controller_port_device");
		if (_apiVersion() != 1)
		{
			throw new InvalidOperationException("Unsupported libretro API version");
		}
	}

	/// <summary>Hooks the callbacks and starts the core.</summary>
	public void Init(string systemDir, string saveDir, VideoRefreshFn video, AudioSampleBatchFn audioBatch, AudioSampleFn audio, InputStateFn input)
	{
		_systemDir = PathText(systemDir);
		_saveDir = PathText(saveDir);
		_env = Environment;
		_video = video;
		_audioBatch = audioBatch;
		_audio = audio;
		_input = input;
		_poll = () => { };
		_setEnvironment(Marshal.GetFunctionPointerForDelegate(_env));
		_init();
		_inited = true;
		_setVideoRefresh(Marshal.GetFunctionPointerForDelegate(_video));
		_setAudioSample(Marshal.GetFunctionPointerForDelegate(_audio));
		_setAudioSampleBatch(Marshal.GetFunctionPointerForDelegate(_audioBatch));
		_setInputPoll(Marshal.GetFunctionPointerForDelegate(_poll));
		_setInputState(Marshal.GetFunctionPointerForDelegate(_input));
		IntPtr info = Marshal.AllocHGlobal(64);
		try
		{
			_getSystemInfo(info);
			Name = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(info)) ?? "";
			Version = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(info, IntPtr.Size)) ?? "";
			NeedFullPath = Marshal.ReadByte(info, IntPtr.Size * 3) != 0;
		}
		finally
		{
			Marshal.FreeHGlobal(info);
		}
	}

	private static IntPtr Ansi(string s) => Bytes(Encoding.Default.GetBytes(s + "\0"));

	private IntPtr PathText(string s) => Utf8Paths ? Bytes(Encoding.UTF8.GetBytes(s + "\0")) : Ansi(s);

	private static IntPtr Bytes(byte[] b)
	{
		IntPtr p = Marshal.AllocHGlobal(b.Length);
		Marshal.Copy(b, 0, p, b.Length);
		return p;
	}

	/// <summary>
	/// Loads a ROM from memory (the core keeps a pointer to it until it's unloaded), or from <paramref name="path"/>
	/// when the core reads files itself (<paramref name="rom"/> may be null then).
	/// </summary>
	public bool LoadGame(byte[] rom, string path)
	{
		UnloadGame();
		int length = 0;
		if (rom != null && !NeedFullPath)
		{
			length = rom.Length;
			_gameData = Marshal.AllocHGlobal(length);
			Marshal.Copy(rom, 0, _gameData, length);
		}
		if (UsesGraphicsCard && !LibretroGl.Bind())
		{
			// no OpenGL here: the core is told so when it asks, and draws on the processor
			Log?.Invoke(2, "OpenGL isn't available: " + (LibretroGl.Problem ?? "no context"));
		}
		IntPtr pathText = PathText(path);
		_game = Marshal.AllocHGlobal(IntPtr.Size * 4);
		Marshal.WriteIntPtr(_game, 0, pathText);
		Marshal.WriteIntPtr(_game, IntPtr.Size, _gameData);
		Marshal.WriteIntPtr(_game, IntPtr.Size * 2, (IntPtr)length);
		Marshal.WriteIntPtr(_game, IntPtr.Size * 3, IntPtr.Zero);
		_hwActive = false;
		ShutdownAsked = false;
		LastError = null;
		_gameLoaded = _loadGame(_game);
		if (!_gameLoaded)
		{
			_hwActive = false;
			ReleaseGraphicsCard();
			return false;
		}
		_setPortDevice(0, DeviceJoypad);
		ReadAvInfo();
		if (_hwActive)
		{
			// room for every internal resolution, so changing it needs nothing from here
			LibretroGl.Size(Math.Max(MaxWidth, 1920), Math.Max(MaxHeight, 1088));
			_hwReset?.Invoke();
		}
		return true;
	}

	private void ReleaseGraphicsCard()
	{
		if (UsesGraphicsCard)
		{
			LibretroGl.Release();
		}
	}

	/// <summary>The picture the core just drew on the card (a frame late: see <see cref="LibretroGl.Read"/>).</summary>
	internal bool ReadGraphicsCard(int width, int height, ref uint[] into, out int gotWidth, out int gotHeight)
	{
		return LibretroGl.Read(width, height, _hwBottomLeft, ref into, out gotWidth, out gotHeight);
	}

	private void ReadAvInfo()
	{
		IntPtr av = Marshal.AllocHGlobal(64);
		try
		{
			for (int i = 0; i < 64; i += 4)
			{
				Marshal.WriteInt32(av, i, 0);
			}
			_getAvInfo(av);
			ReadGeometry(av);
			// geometry: 4 unsigned + float = 20 bytes; timing (two doubles) at 24 with 8-byte alignment, else at 20
			foreach (int at in new[] { 24, 20 })
			{
				double fps = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(av, at));
				double rate = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(av, at + 8));
				if (fps > 30.0 && fps < 120.0 && rate > 8000.0 && rate < 200000.0)
				{
					Fps = fps;
					SampleRate = rate;
					break;
				}
			}
		}
		finally
		{
			Marshal.FreeHGlobal(av);
		}
	}

	/// <summary>retro_game_geometry: base width, base height, max width, max height, aspect.</summary>
	private void ReadGeometry(IntPtr geometry)
	{
		int maxW = Marshal.ReadInt32(geometry, 8);
		int maxH = Marshal.ReadInt32(geometry, 12);
		int w = Marshal.ReadInt32(geometry, 0);
		int h = Marshal.ReadInt32(geometry, 4);
		if (maxW > 0 && maxW <= 4096 && maxH > 0 && maxH <= 4096)
		{
			MaxWidth = Math.Max(maxW, Math.Max(w, 1));
			MaxHeight = Math.Max(maxH, Math.Max(h, 1));
		}
	}

	public void UnloadGame()
	{
		if (_gameLoaded)
		{
			// in the order frontends do it: the core lets go of the card first
			if (_hwActive)
			{
				_hwDestroy?.Invoke();
			}
			_unloadGame();
			_gameLoaded = false;
			if (_hwActive)
			{
				LibretroGl.FreeTargets();
			}
			_hwActive = false;
			ReleaseGraphicsCard();
		}
		if (_game != IntPtr.Zero)
		{
			Marshal.FreeHGlobal(Marshal.ReadIntPtr(_game));
			Marshal.FreeHGlobal(_game);
			_game = IntPtr.Zero;
		}
		if (_gameData != IntPtr.Zero)
		{
			Marshal.FreeHGlobal(_gameData);
			_gameData = IntPtr.Zero;
		}
	}

	public void Run() => _run();

	public void Reset() => _reset();

	public byte[] Serialize()
	{
		int size = (int)_serializeSize();
		if (size <= 0)
		{
			return null;
		}
		IntPtr buf = Marshal.AllocHGlobal(size);
		try
		{
			if (!_serialize(buf, (UIntPtr)size))
			{
				return null;
			}
			byte[] b = new byte[size];
			Marshal.Copy(buf, b, 0, size);
			return b;
		}
		finally
		{
			Marshal.FreeHGlobal(buf);
		}
	}

	public bool Unserialize(byte[] state)
	{
		IntPtr buf = Marshal.AllocHGlobal(state.Length);
		try
		{
			Marshal.Copy(state, 0, buf, state.Length);
			return _unserialize(buf, (UIntPtr)state.Length);
		}
		finally
		{
			Marshal.FreeHGlobal(buf);
		}
	}

	/// <summary>The cartridge's battery memory (where the game saves), live inside the core.</summary>
	public IntPtr SaveRam(out int size)
	{
		size = (int)_memorySize(MemorySaveRam);
		return (size > 0) ? _memoryData(MemorySaveRam) : IntPtr.Zero;
	}

	private bool Environment(uint cmd, IntPtr data)
	{
		try
		{
			switch (cmd & 0xFFFF)
			{
			case 3: // GET_CAN_DUPE
				Marshal.WriteByte(data, 1);
				return true;
			case 9: // GET_SYSTEM_DIRECTORY
				Marshal.WriteIntPtr(data, _systemDir);
				return true;
			case 31: // GET_SAVE_DIRECTORY
				Marshal.WriteIntPtr(data, _saveDir);
				return true;
			case 10: // SET_PIXEL_FORMAT
			{
				int f = Marshal.ReadInt32(data);
				if (f == 1 || f == 2 || f == 0)
				{
					PixelFormat = f;
					return true;
				}
				return false;
			}
			case 15: // GET_VARIABLE
				return GetVariable(data);
			case 16: // SET_VARIABLES
				ReadOffered(data);
				return true;
			case 17: // GET_VARIABLE_UPDATE
				Marshal.WriteByte(data, (byte)(_optionsChanged ? 1 : 0));
				_optionsChanged = false;
				return true;
			case 51: // GET_INPUT_BITMASKS
				return true;
			case 7: // SHUTDOWN
				ShutdownAsked = true;
				return true;
			case 14: // SET_HW_RENDER
				return SetHardware(data);
			case 56: // GET_PREFERRED_HW_RENDER
				if (!UsesGraphicsCard || !LibretroGl.Available)
				{
					return false;
				}
				Marshal.WriteInt32(data, HardwareOpenGl);
				return true;
			case 27: // GET_LOG_INTERFACE
				if (Log == null)
				{
					return false;
				}
				_log ??= OnLog;
				Marshal.WriteIntPtr(data, Marshal.GetFunctionPointerForDelegate(_log));
				return true;
			case 47: // GET_AUDIO_VIDEO_ENABLE
				Marshal.WriteInt32(data, 3);
				return true;
			case 1: // SET_ROTATION
			case 6: // SET_MESSAGE
			case 8: // SET_PERFORMANCE_LEVEL
			case 11: // SET_INPUT_DESCRIPTORS
			case 18: // SET_SUPPORT_NO_GAME
			case 36: // SET_MEMORY_MAPS
				return true;
			case 32: // SET_SYSTEM_AV_INFO
			case 37: // SET_GEOMETRY
				ReadGeometry(data);
				return true;
			case 35: // SET_CONTROLLER_INFO
				return true;
			default:
				return false;
			}
		}
		catch
		{
			return false;
		}
	}

	private const int HardwareOpenGl = 1;

	private const int HardwareOpenGlCore = 3;

	/// <summary>
	/// retro_hw_render_callback: context_type, context_reset, get_current_framebuffer, get_proc_address, depth, stencil,
	/// bottom_left_origin, version_major, version_minor, cache_context, context_destroy, debug_context. A no sends the core
	/// to its next choice (for the PSP's in the end its software renderer).
	/// </summary>
	private bool SetHardware(IntPtr data)
	{
		int type = Marshal.ReadInt32(data);
		if (!UsesGraphicsCard || !LibretroGl.Available || (type != HardwareOpenGl && type != HardwareOpenGlCore))
		{
			return false;
		}
		int p = IntPtr.Size;
		IntPtr reset = Marshal.ReadIntPtr(data, p);
		_hwReset = (reset != IntPtr.Zero) ? (VoidFn)Marshal.GetDelegateForFunctionPointer(reset, typeof(VoidFn)) : null;
		Marshal.WriteIntPtr(data, p * 2, LibretroGl.CurrentFramebufferPointer);
		Marshal.WriteIntPtr(data, p * 3, LibretroGl.ProcAddressPointer);
		_hwBottomLeft = Marshal.ReadByte(data, p * 4 + 2) != 0;
		IntPtr destroy = Marshal.ReadIntPtr(data, p * 4 + 16);
		_hwDestroy = (destroy != IntPtr.Zero) ? (VoidFn)Marshal.GetDelegateForFunctionPointer(destroy, typeof(VoidFn)) : null;
		_hwActive = true;
		return true;
	}

	private void OnLog(int level, IntPtr format, IntPtr a, IntPtr b, IntPtr c, IntPtr d)
	{
		try
		{
			string text = FormatLog(Marshal.PtrToStringAnsi(format) ?? "", new[] { a, b, c, d });
			if (level >= 3)
			{
				LastError = text;
			}
			Log?.Invoke(level, text);
		}
		catch
		{
		}
	}

	/// <summary>The printf of the core's messages: only %s and whole numbers are filled in, the rest is left as it comes.</summary>
	private static string FormatLog(string format, IntPtr[] args)
	{
		StringBuilder text = new StringBuilder();
		int next = 0;
		for (int i = 0; i < format.Length; i++)
		{
			char ch = format[i];
			if (ch != '%' || i + 1 >= format.Length)
			{
				text.Append(ch);
				continue;
			}
			int end = i + 1;
			while (end < format.Length && "-+ #0123456789.lhzt".IndexOf(format[end]) >= 0)
			{
				end++;
			}
			char kind = (end < format.Length) ? format[end] : '%';
			if (kind == '%')
			{
				text.Append('%');
			}
			else if (next >= args.Length)
			{
				text.Append('?');
			}
			else if (kind == 's')
			{
				text.Append(Marshal.PtrToStringAnsi(args[next++]) ?? "");
			}
			else if (kind == 'd' || kind == 'i' || kind == 'u')
			{
				text.Append(args[next++].ToInt32());
			}
			else if (kind == 'x' || kind == 'X' || kind == 'p')
			{
				text.Append(args[next++].ToInt32().ToString("x"));
			}
			else
			{
				// a float takes two slots and the rest can't be told apart: what follows would be wrong
				text.Append('?');
				next = args.Length;
			}
			i = end;
		}
		return text.ToString().TrimEnd('\n', '\r');
	}

	/// <summary>retro_variable { key, value }: the value from <see cref="Options"/>, else the core's default.</summary>
	private bool GetVariable(IntPtr data)
	{
		string key = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(data));
		if (key == null)
		{
			return false;
		}
		string value;
		lock (Options)
		{
			if (!Options.TryGetValue(key, out value))
			{
				value = AnswerDefaults ? DefaultOf(key) : null;
				if (value == null)
				{
					return false;
				}
			}
		}
		string cacheKey = key + "=" + value;
		if (!_optionText.TryGetValue(cacheKey, out IntPtr text))
		{
			text = Ansi(value);
			_optionText[cacheKey] = text;
		}
		Marshal.WriteIntPtr(data, IntPtr.Size, text);
		return true;
	}

	/// <summary>"Description; first|second|...": the first value is the default.</summary>
	private string DefaultOf(string key)
	{
		if (!Offered.TryGetValue(key, out string offer))
		{
			return null;
		}
		int at = offer.IndexOf("; ", StringComparison.Ordinal);
		if (at < 0)
		{
			return null;
		}
		string values = offer.Substring(at + 2);
		int bar = values.IndexOf('|');
		return (bar < 0) ? values : values.Substring(0, bar);
	}

	private void ReadOffered(IntPtr data)
	{
		if (data == IntPtr.Zero)
		{
			return;
		}
		for (int i = 0; i < 512; i++)
		{
			IntPtr item = data + i * IntPtr.Size * 2;
			string key = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(item));
			if (key == null)
			{
				break;
			}
			Offered[key] = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(item, IntPtr.Size)) ?? "";
		}
	}

	public void Dispose()
	{
		try
		{
			UnloadGame();
			if (_inited)
			{
				_deinit();
				_inited = false;
			}
		}
		catch
		{
		}
		// The module stays loaded: cores don't always clean their globals for a second LoadLibrary.
	}
}
