using System;
using System.Runtime.InteropServices;
using IsaClient.Core;

namespace IsaClient.Networking;

/// <summary>
/// 1.4.6 crossplay transport: next to TCP the game also tries the server over UDP (Valve's GameNetworkingSockets, through
/// the game folder's GNS_Wrapper.dll) and keeps whichever connects first. Used only when it can't hurt: network version
/// 1.4.6.1 (older servers have no UDP), no proxy (UDP would go around it and show the real address), and the native
/// library is there (game folders older than 1.4.6 don't have it). Otherwise the client joins over TCP alone.
/// </summary>
public static class Crossplay
{
	private static bool? _native;

	public static bool Allowed
	{
		get
		{
			if (NetworkVersion.IsLatest && !Proxy.Enabled)
			{
				return NativeAvailable;
			}
			return false;
		}
	}

	public static bool NativeAvailable
	{
		get
		{
			if (!_native.HasValue)
			{
				_native = Probe();
			}
			return _native.Value;
		}
	}

	private static bool Probe()
	{
		try
		{
			return GameFolder.HasFile("GNS_Wrapper.dll") && LoadLibrary("GNS_Wrapper.dll") != IntPtr.Zero;
		}
		catch (Exception)
		{
			return false;
		}
	}

	[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr LoadLibrary(string fileName);
}
