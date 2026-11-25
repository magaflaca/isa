using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using IsaClient.Core;

namespace IsaClient.Gta;

internal static class GtaVolume
{
	private static float _before = -1f;

	private static bool _mutedBefore;

	private static readonly object Gate = new object();

	public static void Raise(float level)
	{
		Task.Run(delegate
		{
			lock (Gate)
			{
				try
				{
					IAudioEndpointVolume endpoint = Endpoint(out IMMDevice device);
					Guid none = Guid.Empty;
					if (_before < 0f)
					{
						endpoint.GetMasterVolumeLevelScalar(out _before);
						endpoint.GetMute(out _mutedBefore);
					}
					endpoint.GetMasterVolumeLevelScalar(out float now);
					endpoint.SetMute(false, ref none);
					if (now < level)
					{
						endpoint.SetMasterVolumeLevelScalar(level, ref none);
					}
					Guid managerId = typeof(IAudioSessionManager).GUID;
					device.Activate(ref managerId, 1, IntPtr.Zero, out object managerObject);
					IAudioSessionManager manager = (IAudioSessionManager)managerObject;
					Guid session = Guid.Empty;
					manager.GetSimpleAudioVolume(ref session, 0, out ISimpleAudioVolume own);
					own.SetMute(false, ref none);
					own.SetMasterVolume(1f, ref none);
					Marshal.ReleaseComObject(own);
					Marshal.ReleaseComObject(manager);
					Marshal.ReleaseComObject(endpoint);
					Marshal.ReleaseComObject(device);
				}
				catch (Exception ex)
				{
					ClientLog.LogOnce(ex);
				}
			}
		});
	}

	public static void Restore()
	{
		Task.Run(delegate
		{
			lock (Gate)
			{
				if (_before < 0f)
				{
					return;
				}
				try
				{
					IAudioEndpointVolume endpoint = Endpoint(out IMMDevice device);
					Guid none = Guid.Empty;
					endpoint.SetMasterVolumeLevelScalar(_before, ref none);
					endpoint.SetMute(_mutedBefore, ref none);
					Marshal.ReleaseComObject(endpoint);
					Marshal.ReleaseComObject(device);
				}
				catch (Exception ex)
				{
					ClientLog.LogOnce(ex);
				}
				_before = -1f;
			}
		});
	}

	private static IAudioEndpointVolume Endpoint(out IMMDevice device)
	{
		IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
		try
		{
			Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out device));
		}
		finally
		{
			Marshal.ReleaseComObject(enumerator);
		}
		Guid id = typeof(IAudioEndpointVolume).GUID;
		Marshal.ThrowExceptionForHR(device.Activate(ref id, 1, IntPtr.Zero, out object endpoint));
		return (IAudioEndpointVolume)endpoint;
	}

	[ComImport]
	[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
	private class MMDeviceEnumerator
	{
	}

	[ComImport]
	[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMMDeviceEnumerator
	{
		[PreserveSig]
		int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

		[PreserveSig]
		int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
	}

	[ComImport]
	[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMMDevice
	{
		[PreserveSig]
		int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
	}

	[ComImport]
	[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioEndpointVolume
	{
		void RegisterControlChangeNotify(IntPtr notify);

		void UnregisterControlChangeNotify(IntPtr notify);

		void GetChannelCount(out uint count);

		void SetMasterVolumeLevel(float levelDb, ref Guid context);

		void SetMasterVolumeLevelScalar(float level, ref Guid context);

		void GetMasterVolumeLevel(out float levelDb);

		void GetMasterVolumeLevelScalar(out float level);

		void SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);

		void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);

		void GetChannelVolumeLevel(uint channel, out float levelDb);

		void GetChannelVolumeLevelScalar(uint channel, out float level);

		void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);

		void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
	}

	[ComImport]
	[Guid("BFA971F1-4D5E-40BB-935E-967039BFBEE4")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioSessionManager
	{
		void GetAudioSessionControl(ref Guid session, int streamFlags, out IntPtr control);

		void GetSimpleAudioVolume(ref Guid session, int streamFlags, out ISimpleAudioVolume volume);
	}

	[ComImport]
	[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface ISimpleAudioVolume
	{
		void SetMasterVolume(float level, ref Guid context);

		void GetMasterVolume(out float level);

		void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);

		void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
	}
}
