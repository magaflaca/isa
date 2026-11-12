using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using IsaClient.Core;

namespace IsaClient.Plugins;

/// <summary>
/// Puts the native ImGui library (cimgui.dll, x86, embedded in the executable) next to the client's settings and loads
/// it, so ImGui.NET's calls find it already in the process. Failing here only turns ImGui and the plugins off.
/// </summary>
internal static class ImGuiNativeLoader
{
	private const string ResourceName = "IsaClient.Plugins.Native.cimgui.dll";

	private static bool? _loaded;

	/// <summary>Why the native library could not be loaded; null when it loaded.</summary>
	public static string Error { get; private set; }

	[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern IntPtr LoadLibrary(string path);

	public static bool EnsureLoaded()
	{
		if (_loaded.HasValue)
		{
			return _loaded.Value;
		}
		try
		{
			using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
			if (stream == null)
			{
				throw new InvalidOperationException("cimgui.dll is not embedded in this build");
			}
			byte[] bytes = new byte[stream.Length];
			stream.Read(bytes, 0, bytes.Length);
			string folder = Path.Combine(ClientSettings.IsaClientFolder, "Libraries");
			Directory.CreateDirectory(folder);
			string path = Path.Combine(folder, "cimgui.dll");
			// Rewritten only when it changed (another running client may have it loaded).
			if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length || !SameBytes(File.ReadAllBytes(path), bytes))
			{
				File.WriteAllBytes(path, bytes);
			}
			if (LoadLibrary(path) == IntPtr.Zero)
			{
				throw new InvalidOperationException("LoadLibrary failed (error " + Marshal.GetLastWin32Error() + ")");
			}
			_loaded = true;
		}
		catch (Exception ex)
		{
			Error = ex.Message;
			ClientLog.LogException(ex);
			_loaded = false;
		}
		return _loaded.Value;
	}

	private static bool SameBytes(byte[] a, byte[] b)
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
}
