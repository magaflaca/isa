using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using IStream = System.Runtime.InteropServices.ComTypes.IStream;

namespace IsaClient.ChatImages;

/// <summary>
/// Windows Imaging Component, called directly: it reads what GDI+ can't (WebP, HEIC, AVIF, JPEG XR... with the codecs
/// Windows has). Not through WPF: WPF's imaging makes the whole game DPI aware, and Windows stops scaling its window
/// (everything looks smaller).
/// </summary>
internal static class WicDecoder
{
	private static readonly Guid FactoryClass = new Guid("cacaf262-9370-4615-a13b-9f5539da4c0a");

	private static readonly Guid Bgra32 = new Guid("6fddc324-4e03-4bfe-b185-3d77768dc90f");

	[ComImport]
	[Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IWICImagingFactory
	{
		void CreateDecoderFromFilename();

		[PreserveSig]
		int CreateDecoderFromStream(IStream stream, IntPtr vendor, int options, out IWICBitmapDecoder decoder);
	}

	[ComImport]
	[Guid("9edde9e7-8dee-47ea-99df-e6faf2ed44bf")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IWICBitmapDecoder
	{
		void QueryCapability();

		void Initialize();

		void GetContainerFormat();

		void GetDecoderInfo();

		void CopyPalette();

		void GetMetadataQueryReader();

		void GetPreview();

		void GetColorContexts();

		void GetThumbnail();

		void GetFrameCount();

		[PreserveSig]
		int GetFrame(int index, out IWICBitmapSource frame);
	}

	[ComImport]
	[Guid("00000120-a8f2-4877-ba0a-fd2b6645fb94")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IWICBitmapSource
	{
		[PreserveSig]
		int GetSize(out int width, out int height);

		void GetPixelFormat();

		void GetResolution();

		void CopyPalette();

		[PreserveSig]
		int CopyPixels(IntPtr rectangle, int stride, int bufferSize, IntPtr buffer);
	}

	[DllImport("windowscodecs.dll")]
	private static extern int WICConvertBitmapSource([MarshalAs(UnmanagedType.LPStruct)] Guid format, IWICBitmapSource source, out IWICBitmapSource converted);

	[DllImport("shlwapi.dll")]
	private static extern IStream SHCreateMemStream(byte[] data, int size);

	/// <summary>The first frame as a 32-bit bitmap; throws when no codec reads it.</summary>
	public static Bitmap Decode(byte[] data)
	{
		IWICImagingFactory factory = (IWICImagingFactory)Activator.CreateInstance(Type.GetTypeFromCLSID(FactoryClass));
		IStream stream = SHCreateMemStream(data, data.Length) ?? throw new InvalidOperationException("No memory stream.");
		IWICBitmapDecoder decoder = null;
		IWICBitmapSource frame = null;
		IWICBitmapSource converted = null;
		try
		{
			// 1: cache the metadata on load.
			Check(factory.CreateDecoderFromStream(stream, IntPtr.Zero, 1, out decoder));
			Check(decoder.GetFrame(0, out frame));
			Check(WICConvertBitmapSource(Bgra32, frame, out converted));
			Check(converted.GetSize(out var width, out var height));
			if (width <= 0 || height <= 0 || (long)width * height > 64L * 1024 * 1024)
			{
				throw new InvalidOperationException("Unsupported picture size.");
			}
			Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
			BitmapData locked = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
			try
			{
				Check(converted.CopyPixels(IntPtr.Zero, locked.Stride, locked.Stride * height, locked.Scan0));
			}
			finally
			{
				bitmap.UnlockBits(locked);
			}
			return bitmap;
		}
		finally
		{
			Release(converted);
			Release(frame);
			Release(decoder);
			Release(stream);
			Release(factory);
		}
	}

	private static void Check(int hr)
	{
		if (hr < 0)
		{
			Marshal.ThrowExceptionForHR(hr);
		}
	}

	private static void Release(object com)
	{
		if (com != null && Marshal.IsComObject(com))
		{
			Marshal.ReleaseComObject(com);
		}
	}
}
