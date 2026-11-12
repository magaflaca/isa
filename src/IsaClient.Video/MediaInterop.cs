using System;
using System.Runtime.InteropServices;

namespace IsaClient.Video;

/// <summary>
/// The part of Windows Media Foundation the video player uses: source readers (decode H.264/AAC from files or from
/// our own byte streams), sink writers (copy the streams into one .mp4 without encoding again) and media types.
/// </summary>
internal static class MediaInterop
{
	[StructLayout(LayoutKind.Explicit, Size = 16)]
	internal struct PropVariant
	{
		[FieldOffset(0)]
		public ushort Vt;

		[FieldOffset(8)]
		public long HVal;
	}

	[ComImport]
	[Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMFAttributes
	{
		void GetItem();

		void GetItemType();

		void CompareItem();

		void Compare();

		[PreserveSig]
		int GetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid key, out int value);

		[PreserveSig]
		int GetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid key, out long value);

		void GetDouble();

		[PreserveSig]
		int GetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid key, out Guid value);

		void GetStringLength();

		void GetString();

		void GetAllocatedString();

		void GetBlobSize();

		void GetBlob();

		void GetAllocatedBlob();

		void GetUnknown();

		void SetItem();

		void DeleteItem();

		void DeleteAllItems();

		[PreserveSig]
		int SetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);

		[PreserveSig]
		int SetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid key, long value);

		void SetDouble();

		[PreserveSig]
		int SetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPStruct)] Guid value);

		[PreserveSig]
		int SetString([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
	}

	[ComImport]
	[Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMFMediaType
	{
		void GetItem();

		void GetItemType();

		void CompareItem();

		void Compare();

		[PreserveSig]
		int GetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid key, out int value);

		[PreserveSig]
		int GetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid key, out long value);

		void GetDouble();

		[PreserveSig]
		int GetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid key, out Guid value);

		void GetStringLength();

		void GetString();

		void GetAllocatedString();

		void GetBlobSize();

		void GetBlob();

		void GetAllocatedBlob();

		void GetUnknown();

		void SetItem();

		void DeleteItem();

		void DeleteAllItems();

		[PreserveSig]
		int SetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);

		[PreserveSig]
		int SetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid key, long value);

		void SetDouble();

		[PreserveSig]
		int SetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPStruct)] Guid value);
	}

	[ComImport]
	[Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMFSample
	{
		void GetItem();

		void GetItemType();

		void CompareItem();

		void Compare();

		void GetUINT32();

		void GetUINT64();

		void GetDouble();

		void GetGUID();

		void GetStringLength();

		void GetString();

		void GetAllocatedString();

		void GetBlobSize();

		void GetBlob();

		void GetAllocatedBlob();

		void GetUnknown();

		void SetItem();

		void DeleteItem();

		void DeleteAllItems();

		void SetUINT32();

		void SetUINT64();

		void SetDouble();

		void SetGUID();

		void SetString();

		void SetBlob();

		void SetUnknown();

		void LockStore();

		void UnlockStore();

		void GetCount();

		void GetItemByIndex();

		void CopyAllItems();

		void GetSampleFlags();

		void SetSampleFlags();

		[PreserveSig]
		int GetSampleTime(out long time);

		[PreserveSig]
		int SetSampleTime(long time);

		void GetSampleDuration();

		void SetSampleDuration();

		void GetBufferCount();

		void GetBufferByIndex();

		[PreserveSig]
		int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
	}

	[ComImport]
	[Guid("045FA593-8799-42b8-BC8D-8968C6453507")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMFMediaBuffer
	{
		[PreserveSig]
		int Lock(out IntPtr buffer, out int maxLength, out int currentLength);

		[PreserveSig]
		int Unlock();

		[PreserveSig]
		int GetCurrentLength(out int length);
	}

	[ComImport]
	[Guid("ad4c1b00-4bf7-422f-9175-756693d9130d")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMFByteStream
	{
	}

	[ComImport]
	[Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMFSourceReader
	{
		[PreserveSig]
		int GetStreamSelection(int streamIndex, [MarshalAs(UnmanagedType.Bool)] out bool selected);

		[PreserveSig]
		int SetStreamSelection(int streamIndex, [MarshalAs(UnmanagedType.Bool)] bool selected);

		[PreserveSig]
		int GetNativeMediaType(int streamIndex, int mediaTypeIndex, out IMFMediaType mediaType);

		[PreserveSig]
		int GetCurrentMediaType(int streamIndex, out IMFMediaType mediaType);

		[PreserveSig]
		int SetCurrentMediaType(int streamIndex, IntPtr reserved, IMFMediaType mediaType);

		[PreserveSig]
		int SetCurrentPosition([MarshalAs(UnmanagedType.LPStruct)] Guid timeFormat, ref PropVariant position);

		[PreserveSig]
		int ReadSample(int streamIndex, int controlFlags, out int actualStreamIndex, out int streamFlags, out long timestamp, out IMFSample sample);

		[PreserveSig]
		int Flush(int streamIndex);

		[PreserveSig]
		int GetServiceForStream(int streamIndex, [MarshalAs(UnmanagedType.LPStruct)] Guid service, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr result);

		[PreserveSig]
		int GetPresentationAttribute(int streamIndex, [MarshalAs(UnmanagedType.LPStruct)] Guid attribute, out PropVariant value);
	}

	[ComImport]
	[Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMFSinkWriter
	{
		[PreserveSig]
		int AddStream(IMFMediaType targetMediaType, out int streamIndex);

		[PreserveSig]
		int SetInputMediaType(int streamIndex, IMFMediaType inputMediaType, IntPtr encodingParameters);

		[PreserveSig]
		int BeginWriting();

		[PreserveSig]
		int WriteSample(int streamIndex, IMFSample sample);

		[PreserveSig]
		int SendStreamTick(int streamIndex, long timestamp);

		void PlaceMarker();

		void NotifyEndOfSegment();

		void Flush();

		[PreserveSig]
		int DoFinalize();
	}

	[DllImport("mfplat.dll", ExactSpelling = true)]
	internal static extern int MFStartup(int version, int flags);

	[DllImport("mfplat.dll", ExactSpelling = true)]
	internal static extern int MFShutdown();

	[DllImport("mfplat.dll", ExactSpelling = true)]
	internal static extern int MFCreateMediaType(out IMFMediaType mediaType);

	[DllImport("mfplat.dll", ExactSpelling = true)]
	internal static extern int MFCreateAttributes(out IMFAttributes attributes, int initialSize);

	[DllImport("mfplat.dll", ExactSpelling = true)]
	internal static extern int MFCreateMFByteStreamOnStream(System.Runtime.InteropServices.ComTypes.IStream stream, out IMFByteStream byteStream);

	[DllImport("mfreadwrite.dll", ExactSpelling = true)]
	internal static extern int MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IMFAttributes attributes, out IMFSourceReader reader);

	[DllImport("mfreadwrite.dll", ExactSpelling = true)]
	internal static extern int MFCreateSourceReaderFromByteStream(IMFByteStream byteStream, IMFAttributes attributes, out IMFSourceReader reader);

	[DllImport("mfreadwrite.dll", ExactSpelling = true)]
	internal static extern int MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr byteStream, IMFAttributes attributes, out IMFSinkWriter writer);

	internal const int MfVersion = 0x20070;

	internal const int FirstVideoStream = -4;

	internal const int FirstAudioStream = -3;

	internal const int AllStreams = -2;

	internal const int MediaSource = -1;

	internal const int EndOfStreamFlag = 0x2;

	internal const int CurrentMediaTypeChangedFlag = 0x20;

	internal const int StreamTickFlag = 0x100;

	internal static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");

	internal static readonly Guid Subtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");

	internal static readonly Guid MediaTypeAudio = new Guid("73647561-0000-0010-8000-00AA00389B71");

	internal static readonly Guid MediaTypeVideo = new Guid("73646976-0000-0010-8000-00AA00389B71");

	internal static readonly Guid AudioFormatPcm = new Guid("00000001-0000-0010-8000-00AA00389B71");

	internal static readonly Guid VideoFormatRgb32 = new Guid("00000016-0000-0010-8000-00AA00389B71");

	internal static readonly Guid AudioChannels = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");

	internal static readonly Guid AudioSampleRate = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");

	internal static readonly Guid AudioBitsPerSample = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");

	internal static readonly Guid FrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");

	internal static readonly Guid DefaultStride = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");

	internal static readonly Guid Duration = new Guid("6c990d33-bb8e-477a-8598-0d5d96fcd88a");

	internal static readonly Guid ByteStreamContentType = new Guid("fc358289-3cb6-460c-a424-b6681260375a");

	internal static readonly Guid EnableAdvancedVideoProcessing = new Guid("0f81da2c-b537-4672-a8b2-a681b17307a3");

	internal static readonly Guid SinkWriterDisableThrottling = new Guid("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");

	internal static void Check(int hr, string what)
	{
		if (hr < 0)
		{
			throw new InvalidOperationException(what + " (0x" + hr.ToString("X8") + ").");
		}
	}

	internal static void Release(object com)
	{
		if (com != null && Marshal.IsComObject(com))
		{
			Marshal.ReleaseComObject(com);
		}
	}

	/// <summary>Copies a sample's bytes out (decoded PCM, for example).</summary>
	internal static byte[] SampleBytes(IMFSample sample)
	{
		if (sample.ConvertToContiguousBuffer(out var buffer) < 0)
		{
			return new byte[0];
		}
		try
		{
			if (buffer.Lock(out var pointer, out _, out var length) < 0)
			{
				return new byte[0];
			}
			byte[] bytes = new byte[length];
			Marshal.Copy(pointer, bytes, 0, length);
			buffer.Unlock();
			return bytes;
		}
		finally
		{
			Release(buffer);
		}
	}
}
