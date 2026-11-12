using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using IsaClient.Core;

namespace IsaClient.Psp;

/// <summary>
/// The PSP emulator: PPSSPP (GPL 2.0), which isn't in IsaClient. The first time someone wants to play a PSP game its
/// libretro core goes to IsaClient/Libraries and its data files (fonts and the system's own) to IsaClient/PSP/System,
/// both from libretro's build server (Windows, 32 bits like Terraria).
/// </summary>
public static class PspEmulator
{
	public const string Name = "PPSSPP";

	public const string License = "GPL 2.0";

	private const string Dll = "ppsspp_libretro.dll";

	private const string CoreUrl = "https://buildbot.libretro.com/nightly/windows/x86/latest/ppsspp_libretro.dll.zip";

	private const string AssetsUrl = "https://buildbot.libretro.com/assets/system/PPSSPP.zip";

	/// <summary>What the two downloads weigh, for the progress across both (it's only for the bar).</summary>
	private const long CoreBytes = 6L * 1024 * 1024;

	private const long AssetsBytes = 10L * 1024 * 1024;

	public static string CorePath => Path.Combine(ClientSettings.IsaClientFolder, "Libraries", Dll);

	public static string SystemFolder => Path.Combine(PspGame.Folder, "System");

	private static string AssetsMark => Path.Combine(SystemFolder, "PPSSPP", "compat.ini");

	/// <summary>The core and its data files are on disk.</summary>
	public static bool Installed
	{
		get
		{
			try
			{
				return File.Exists(CorePath) && new FileInfo(CorePath).Length > 1_000_000 && File.Exists(AssetsMark);
			}
			catch
			{
				return false;
			}
		}
	}

	/// <summary>0..1 while downloading.</summary>
	public static float Progress { get; private set; }

	public static string Error { get; private set; }

	public static bool Busy { get; private set; }

	static PspEmulator()
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
	}

	/// <summary>Starts downloading; <paramref name="done"/> runs on the main thread when it's ready to play.</summary>
	public static void Download(Action done = null)
	{
		if (Busy)
		{
			return;
		}
		Busy = true;
		Progress = 0f;
		Error = null;
		Task.Run(() =>
		{
			string error = null;
			try
			{
				Fetch();
			}
			catch (Exception ex)
			{
				error = ex.Message;
				ClientLog.LogException(ex);
			}
			Terraria.Main.QueueMainThreadAction(() =>
			{
				Busy = false;
				Error = error;
				if (error == null)
				{
					done?.Invoke();
				}
			});
		});
	}

	private static void Fetch()
	{
		const float coreShare = (float)CoreBytes / (CoreBytes + AssetsBytes);
		if (!File.Exists(CorePath) || new FileInfo(CorePath).Length <= 1_000_000)
		{
			byte[] zip = Get(CoreUrl, p => Progress = p * coreShare);
			InstallCore(zip);
		}
		Progress = coreShare;
		if (!File.Exists(AssetsMark))
		{
			byte[] zip = Get(AssetsUrl, p => Progress = coreShare + p * (0.97f - coreShare));
			InstallAssets(zip);
		}
		Progress = 1f;
	}

	private static byte[] Get(string url, Action<float> progress)
	{
		HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
		r.UserAgent = "IsaClient";
		r.Timeout = 30000;
		r.ReadWriteTimeout = 60000;
		r.AllowAutoRedirect = true;
		using HttpWebResponse response = (HttpWebResponse)r.GetResponse();
		using Stream s = response.GetResponseStream();
		using MemoryStream ms = new MemoryStream();
		long total = response.ContentLength;
		byte[] buffer = new byte[65536];
		int n;
		while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
		{
			ms.Write(buffer, 0, n);
			if (total > 0)
			{
				progress(Math.Min(1f, (float)ms.Length / total));
			}
			if (ms.Length > 96L * 1024 * 1024)
			{
				throw new InvalidDataException("The download is too big.");
			}
		}
		return ms.ToArray();
	}

	private static void InstallCore(byte[] zip)
	{
		byte[] dll = null;
		using (MemoryStream zs = new MemoryStream(zip))
		using (Ionic.Zip.ZipFile z = Ionic.Zip.ZipFile.Read(zs))
		{
			foreach (Ionic.Zip.ZipEntry entry in z)
			{
				if (!entry.IsDirectory && string.Equals(Path.GetFileName(entry.FileName), Dll, StringComparison.OrdinalIgnoreCase))
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
			throw new InvalidDataException("The download has no " + Dll + ".");
		}
		int pe = BitConverter.ToInt32(dll, 0x3C);
		if (pe <= 0 || pe + 6 > dll.Length || BitConverter.ToUInt16(dll, pe + 4) != 0x14C)
		{
			throw new InvalidDataException(Dll + " isn't a 32-bit Windows library.");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(CorePath));
		string tmp = CorePath + ".tmp";
		File.WriteAllBytes(tmp, dll);
		if (File.Exists(CorePath))
		{
			File.Delete(CorePath);
		}
		File.Move(tmp, CorePath);
	}

	/// <summary>The zip has a PPSSPP folder with everything: it goes into the system folder as it is, the mark file last.</summary>
	private static void InstallAssets(byte[] zip)
	{
		string root = Path.GetFullPath(SystemFolder) + Path.DirectorySeparatorChar;
		Directory.CreateDirectory(root);
		Ionic.Zip.ZipEntry mark = null;
		using MemoryStream zs = new MemoryStream(zip);
		using Ionic.Zip.ZipFile z = Ionic.Zip.ZipFile.Read(zs);
		void Write(Ionic.Zip.ZipEntry entry, string target)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(target));
			using FileStream o = File.Create(target);
			entry.Extract(o);
		}
		foreach (Ionic.Zip.ZipEntry entry in z)
		{
			if (entry.IsDirectory)
			{
				continue;
			}
			string target = Path.GetFullPath(Path.Combine(root, entry.FileName.Replace('/', Path.DirectorySeparatorChar)));
			if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			if (string.Equals(target, Path.GetFullPath(AssetsMark), StringComparison.OrdinalIgnoreCase))
			{
				mark = entry;
				continue;
			}
			Write(entry, target);
		}
		if (mark == null)
		{
			throw new InvalidDataException("The PSP data files are incomplete.");
		}
		Write(mark, AssetsMark);
	}

	/// <summary>
	/// The core's settings: the graphics card when there's OpenGL (else its own software drawing), the game in the
	/// language of Terraria, X confirms like on most of the world's PSPs, and nothing goes online.
	/// </summary>
	internal static Dictionary<string, string> Options(bool graphicsCard, int resolution, string language) => new Dictionary<string, string>
	{
		["ppsspp_backend"] = graphicsCard ? "opengl" : "none",
		["ppsspp_internal_resolution"] = ResolutionText(resolution),
		["ppsspp_language"] = language,
		["ppsspp_button_preference"] = "Cross",
		["ppsspp_cpu_core"] = "JIT",
		["ppsspp_fast_memory"] = "enabled",
		["ppsspp_cache_iso"] = "disabled",
		["ppsspp_detect_vsync_swap_interval"] = "disabled",
		["ppsspp_enable_wlan"] = "disabled",
		["ppsspp_enable_upnp"] = "disabled"
	};

	/// <summary>The PSP's 480x272 times 1, 2 or 3.</summary>
	internal static string ResolutionText(int times) => (480 * times) + "x" + ((272 * times));
}
