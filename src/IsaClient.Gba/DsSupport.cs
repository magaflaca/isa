using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using IsaClient.Core;

namespace IsaClient.Gba;

/// <summary>
/// A Nintendo DS emulator the console can run DS games with. None comes inside IsaClient: the one chosen is downloaded
/// once (a libretro core from libretro's build server) into IsaClient/Libraries the first time a DS game is played.
/// </summary>
public sealed class DsEngine
{
	public string Id = "";

	public string Name = "";

	public string Dll = "";

	public string License = "";

	public string About = "";

	/// <summary>The zipped core on libretro's build server (Windows, 32 bits like Terraria).</summary>
	public string Url = "";

	/// <summary>The core's settings: both screens one above the other with no gap, the stylus as an absolute pointer.</summary>
	public Func<string, Dictionary<string, string>> Options;

	public static readonly DsEngine MelonDS = new DsEngine
	{
		Id = "melonds",
		Name = "melonDS",
		Dll = "melonds_libretro.dll",
		License = "GPL 3.0",
		About = "Accurate, plays almost everything",
		Url = "https://buildbot.libretro.com/nightly/windows/x86/latest/melonds_libretro.dll.zip",
		Options = lang => new Dictionary<string, string>
		{
			["melonds_console_mode"] = "DS",
			["melonds_boot_directly"] = "enabled",
			["melonds_use_fw_settings"] = "disabled",
			["melonds_language"] = lang,
			["melonds_screen_layout"] = "Top/Bottom",
			["melonds_screen_gap"] = "0",
			["melonds_touch_mode"] = "Touch",
			["melonds_threaded_renderer"] = "enabled",
			["melonds_opengl_renderer"] = "disabled"
		}
	};

	public static readonly DsEngine DeSmuME = new DsEngine
	{
		Id = "desmume",
		Name = "DeSmuME",
		Dll = "desmume2015_libretro.dll",
		License = "GPL 2.0",
		About = "Lighter: try it if a game runs slow",
		Url = "https://buildbot.libretro.com/nightly/windows/x86/latest/desmume2015_libretro.dll.zip",
		Options = lang => new Dictionary<string, string>
		{
			["desmume_cpu_mode"] = "jit",
			["desmume_num_cores"] = Math.Max(1, Math.Min(4, Environment.ProcessorCount - 1)).ToString(),
			["desmume_frameskip"] = "0",
			["desmume_internal_resolution"] = "256x192",
			["desmume_firmware_language"] = lang,
			["desmume_screens_layout"] = "top/bottom",
			["desmume_screens_gap"] = "0",
			["desmume_pointer_mouse"] = "enabled",
			["desmume_pointer_type"] = "touch",
			["desmume_pointer_device_l"] = "none",
			["desmume_pointer_device_r"] = "none",
			["desmume_load_to_memory"] = "disabled",
			["desmume_mic_mode"] = "internal"
		}
	};

	public static readonly DsEngine[] All = { MelonDS, DeSmuME };

	public static DsEngine ById(string id)
	{
		foreach (DsEngine e in All)
		{
			if (string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))
			{
				return e;
			}
		}
		return MelonDS;
	}

	public string Path => System.IO.Path.Combine(ClientSettings.IsaClientFolder, "Libraries", Dll);

	public bool Installed
	{
		get
		{
			try
			{
				return File.Exists(Path) && new FileInfo(Path).Length > 200_000;
			}
			catch
			{
				return false;
			}
		}
	}
}

/// <summary>Downloads a DS engine (one at a time, in the background) with its progress for the window.</summary>
public static class DsDownload
{
	public static DsEngine Engine { get; private set; }

	/// <summary>0..1 while downloading.</summary>
	public static float Progress { get; private set; }

	public static string Error { get; private set; }

	public static bool Busy => Engine != null;

	static DsDownload()
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
	}

	/// <summary>Starts downloading <paramref name="engine"/>; <paramref name="done"/> runs on the main thread when it's ready.</summary>
	public static void Start(DsEngine engine, Action done = null)
	{
		if (Busy)
		{
			return;
		}
		Engine = engine;
		Progress = 0f;
		Error = null;
		Task.Run(() =>
		{
			string error = null;
			try
			{
				Fetch(engine);
			}
			catch (Exception ex)
			{
				error = ex.Message;
				ClientLog.LogException(ex);
			}
			Terraria.Main.QueueMainThreadAction(() =>
			{
				Engine = null;
				Error = error;
				if (error == null)
				{
					done?.Invoke();
				}
			});
		});
	}

	private static void Fetch(DsEngine engine)
	{
		HttpWebRequest r = (HttpWebRequest)WebRequest.Create(engine.Url);
		r.UserAgent = "IsaClient";
		r.Timeout = 30000;
		r.ReadWriteTimeout = 60000;
		r.AllowAutoRedirect = true;
		byte[] zip;
		using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
		using (Stream s = resp.GetResponseStream())
		using (MemoryStream ms = new MemoryStream())
		{
			long total = resp.ContentLength;
			byte[] buf = new byte[65536];
			int n;
			while ((n = s.Read(buf, 0, buf.Length)) > 0)
			{
				ms.Write(buf, 0, n);
				if (total > 0)
				{
					Progress = Math.Min(0.95f, (float)ms.Length / total);
				}
				if (ms.Length > 64L * 1024 * 1024)
				{
					throw new InvalidDataException("The download is too big.");
				}
			}
			zip = ms.ToArray();
		}
		byte[] dll = null;
		using (MemoryStream zs = new MemoryStream(zip))
		using (Ionic.Zip.ZipFile z = Ionic.Zip.ZipFile.Read(zs))
		{
			foreach (Ionic.Zip.ZipEntry entry in z)
			{
				if (!entry.IsDirectory && string.Equals(System.IO.Path.GetFileName(entry.FileName), engine.Dll, StringComparison.OrdinalIgnoreCase))
				{
					using MemoryStream o = new MemoryStream();
					entry.Extract(o);
					dll = o.ToArray();
				}
			}
		}
		// a Windows DLL for 32 bits (what Terraria is): "MZ", then the PE header says i386
		if (dll == null || dll.Length < 0x400 || dll[0] != 'M' || dll[1] != 'Z')
		{
			throw new InvalidDataException("The download has no " + engine.Dll + ".");
		}
		int pe = BitConverter.ToInt32(dll, 0x3C);
		if (pe <= 0 || pe + 6 > dll.Length || BitConverter.ToUInt16(dll, pe + 4) != 0x14C)
		{
			throw new InvalidDataException(engine.Dll + " isn't a 32-bit Windows library.");
		}
		string path = engine.Path;
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
		string tmp = path + ".tmp";
		File.WriteAllBytes(tmp, dll);
		if (File.Exists(path))
		{
			File.Delete(path);
		}
		File.Move(tmp, path);
		Progress = 1f;
	}
}

/// <summary>A DS game's banner: its 32x32 icon and its title.</summary>
public sealed class DsBanner
{
	/// <summary>RGBA as XNA's Color packs it (0 = transparent).</summary>
	public uint[] Icon;

	public string Title = "";

	public string Publisher = "";
}

/// <summary>Reading Nintendo DS ROMs: the header (title, game code) and the banner (icon and names).</summary>
public static class DsRom
{
	private static readonly Dictionary<string, DsBanner> _banners = new Dictionary<string, DsBanner>(StringComparer.OrdinalIgnoreCase);

	/// <summary>A DS cartridge header: the Nintendo logo's checksum is always 0xCF56 (or the header's own CRC matches).</summary>
	public static bool IsDs(byte[] head)
	{
		if (head == null || head.Length < 0x160)
		{
			return false;
		}
		if (BitConverter.ToUInt16(head, 0x15C) == 0xCF56)
		{
			return true;
		}
		return BitConverter.ToUInt16(head, 0x15E) == Crc16(head, 0, 0x15E) && head[0x12] <= 3;
	}

	public static bool IsDsFile(string path)
	{
		string ext = System.IO.Path.GetExtension(path ?? "").ToLowerInvariant();
		return ext == ".nds" || ext == ".srl" || ext == ".dsi";
	}

	private static ushort Crc16(byte[] data, int start, int length)
	{
		ushort crc = 0xFFFF;
		for (int i = start; i < start + length; i++)
		{
			crc ^= data[i];
			for (int b = 0; b < 8; b++)
			{
				crc = (ushort)(((crc & 1) != 0) ? ((crc >> 1) ^ 0xA001) : (crc >> 1));
			}
		}
		return crc;
	}

	/// <summary>The 4-letter game code (e.g. "BXYP").</summary>
	public static string Code(byte[] head)
	{
		if (head == null || head.Length < 0x10)
		{
			return "";
		}
		StringBuilder s = new StringBuilder();
		for (int i = 0x0C; i < 0x10; i++)
		{
			char c = (char)head[i];
			if (c < 0x20 || c > 0x7E)
			{
				return "";
			}
			s.Append(c);
		}
		return s.ToString();
	}

	/// <summary>The short title in the header ("COD BLACKOPS"), tidied.</summary>
	public static string HeaderTitle(byte[] head)
	{
		string t = Encoding.ASCII.GetString(head, 0, 12).TrimEnd('\0', ' ');
		return (t.Length > 0) ? t : "a DS game";
	}

	/// <summary>The saves key: the game code and a hash of the header's start (title, code, maker, version).</summary>
	public static string Key(byte[] head)
	{
		uint crc = 0;
		for (int i = 0; i < 0x20 && i < head.Length; i++)
		{
			crc = (crc * 31) + head[i];
		}
		string code = Code(head);
		return "DS-" + ((code.Length == 4) ? code : "NTR") + "-" + crc.ToString("x8");
	}

	/// <summary>The banner of the ROM at <paramref name="path"/> (cached; null when it has none).</summary>
	public static DsBanner Banner(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return null;
		}
		if (_banners.TryGetValue(path, out DsBanner cached))
		{
			return cached;
		}
		DsBanner b = null;
		try
		{
			using FileStream s = File.OpenRead(path);
			byte[] head = new byte[0x70];
			if (s.Read(head, 0, head.Length) == head.Length)
			{
				long at = BitConverter.ToUInt32(head, 0x68);
				if (at >= 0x200 && at + 0x840 <= s.Length)
				{
					byte[] banner = new byte[0x840];
					s.Position = at;
					if (s.Read(banner, 0, banner.Length) == banner.Length)
					{
						b = Parse(banner);
					}
				}
			}
		}
		catch
		{
		}
		_banners[path] = b;
		return b;
	}

	private static DsBanner Parse(byte[] banner)
	{
		DsBanner b = new DsBanner { Icon = new uint[32 * 32] };
		uint[] pal = new uint[16];
		for (int i = 0; i < 16; i++)
		{
			int c = BitConverter.ToUInt16(banner, 0x220 + i * 2);
			uint r = (uint)(c & 31);
			uint g = (uint)((c >> 5) & 31);
			uint bl = (uint)((c >> 10) & 31);
			r = (r << 3) | (r >> 2);
			g = (g << 3) | (g >> 2);
			bl = (bl << 3) | (bl >> 2);
			pal[i] = (i == 0) ? 0u : (0xFF000000u | (bl << 16) | (g << 8) | r);
		}
		// 4x4 tiles of 8x8 pixels, 4 bits a pixel (low nibble first)
		for (int tile = 0; tile < 16; tile++)
		{
			int tx = (tile % 4) * 8;
			int ty = (tile / 4) * 8;
			for (int p = 0; p < 64; p++)
			{
				int v = banner[0x20 + tile * 32 + p / 2];
				int idx = ((p & 1) == 0) ? (v & 15) : (v >> 4);
				b.Icon[(ty + p / 8) * 32 + tx + p % 8] = pal[idx];
			}
		}
		// English title (then Japanese): "Name\nSubtitle\nPublisher"
		foreach (int at in new[] { 0x340, 0x240 })
		{
			string text = Encoding.Unicode.GetString(banner, at, 0x100);
			int end = text.IndexOf('\0');
			if (end >= 0)
			{
				text = text.Substring(0, end);
			}
			// the game font has no (R), (TM) or (C) signs
			text = text.Replace("®", "").Replace("™", "").Replace("©", "");
			string[] lines = text.Replace("\r", "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
			if (lines.Length == 0)
			{
				continue;
			}
			if (lines.Length == 1)
			{
				b.Title = lines[0].Trim();
			}
			else
			{
				b.Publisher = lines[lines.Length - 1].Trim();
				b.Title = string.Join(": ", lines, 0, lines.Length - 1).Trim();
			}
			break;
		}
		return b;
	}

	/// <summary>The game's name: the banner's title, else the header's.</summary>
	public static string Title(string path, byte[] head)
	{
		DsBanner b = Banner(path);
		if (b != null && b.Title.Length > 0)
		{
			return b.Title;
		}
		return (head != null) ? HeaderTitle(head) : System.IO.Path.GetFileNameWithoutExtension(path);
	}
}
