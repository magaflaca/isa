using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace IsaClient.Music;

/// <summary>
/// Audio player for the Music module: Windows Media Foundation decodes the file (m4a/AAC, mp3, wav, wma, flac...)
/// to 16-bit PCM and waveOut plays it on its own thread, so it keeps going smoothly while the game is busy
/// (world loading, saving...). No extra DLLs are needed.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
	#region Media Foundation interop

	[StructLayout(LayoutKind.Explicit, Size = 16)]
	private struct PropVariant
	{
		[FieldOffset(0)]
		public ushort Vt;

		[FieldOffset(8)]
		public long HVal;
	}

	[ComImport]
	[Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMFSourceReader
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
	[Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMFMediaType
	{
		void GetItem();

		void GetItemType();

		void CompareItem();

		void Compare();

		[PreserveSig]
		int GetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid key, out int value);

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

		[PreserveSig]
		int SetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);

		void SetUINT64();

		void SetDouble();

		[PreserveSig]
		int SetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPStruct)] Guid value);
	}

	[ComImport]
	[Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMFSample
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

		void GetSampleTime();

		void SetSampleTime();

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
	private interface IMFMediaBuffer
	{
		[PreserveSig]
		int Lock(out IntPtr buffer, out int maxLength, out int currentLength);

		[PreserveSig]
		int Unlock();

		[PreserveSig]
		int GetCurrentLength(out int length);
	}

	[DllImport("mfplat.dll", ExactSpelling = true)]
	private static extern int MFStartup(int version, int flags);

	[DllImport("mfplat.dll", ExactSpelling = true)]
	private static extern int MFShutdown();

	[DllImport("mfplat.dll", ExactSpelling = true)]
	private static extern int MFCreateMediaType(out IMFMediaType mediaType);

	[DllImport("mfreadwrite.dll", ExactSpelling = true)]
	private static extern int MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr attributes, out IMFSourceReader reader);

	private const int MF_VERSION = 0x20070;

	private const int FirstAudioStream = -3;

	private const int AllStreams = -2;

	private const int MediaSource = -1;

	private const int EndOfStreamFlag = 0x2;

	private static readonly Guid _mF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");

	private static readonly Guid _mF_MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");

	private static readonly Guid _mFMediaType_Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");

	private static readonly Guid _mFAudioFormat_PCM = new Guid("00000001-0000-0010-8000-00AA00389B71");

	private static readonly Guid _mF_MT_AUDIO_NUM_CHANNELS = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");

	private static readonly Guid _mF_MT_AUDIO_SAMPLES_PER_SECOND = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");

	private static readonly Guid _mF_MT_AUDIO_BITS_PER_SAMPLE = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");

	private static readonly Guid _mF_PD_DURATION = new Guid("6c990d33-bb8e-477a-8598-0d5d96fcd88a");

	#endregion

	#region waveOut interop

	[StructLayout(LayoutKind.Sequential)]
	private struct WaveFormatEx
	{
		public short WFormatTag;

		public short NChannels;

		public int NSamplesPerSec;

		public int NAvgBytesPerSec;

		public short NBlockAlign;

		public short WBitsPerSample;

		public short CbSize;
	}

	[DllImport("winmm.dll", EntryPoint = "waveOutOpen")]
	private static extern int WaveOutOpen(out IntPtr handle, int deviceId, ref WaveFormatEx format, IntPtr callback, IntPtr instance, int flags);

	[DllImport("winmm.dll", EntryPoint = "waveOutPrepareHeader")]
	private static extern int WaveOutPrepareHeader(IntPtr handle, IntPtr header, int size);

	[DllImport("winmm.dll", EntryPoint = "waveOutUnprepareHeader")]
	private static extern int WaveOutUnprepareHeader(IntPtr handle, IntPtr header, int size);

	[DllImport("winmm.dll", EntryPoint = "waveOutWrite")]
	private static extern int WaveOutWrite(IntPtr handle, IntPtr header, int size);

	[DllImport("winmm.dll", EntryPoint = "waveOutPause")]
	private static extern int WaveOutPause(IntPtr handle);

	[DllImport("winmm.dll", EntryPoint = "waveOutRestart")]
	private static extern int WaveOutRestart(IntPtr handle);

	[DllImport("winmm.dll", EntryPoint = "waveOutReset")]
	private static extern int WaveOutReset(IntPtr handle);

	[DllImport("winmm.dll", EntryPoint = "waveOutClose")]
	private static extern int WaveOutClose(IntPtr handle);

	private const int WaveMapper = -1;

	private const int WHDR_DONE = 1;

	private const int BufferCount = 6;

	private static readonly int _headerSize = IntPtr.Size * 3 + 20 + IntPtr.Size;

	private static readonly int _flagsOffset = IntPtr.Size * 2 + 8;

	#endregion

	private readonly string _path;

	/// <summary>Set instead of a file: the samples come from here (the casino's looped Vorbis music).</summary>
	private readonly IPcmSource _source;

	private readonly Thread _thread;

	private volatile bool _stop;

	private volatile bool _paused;

	private long _seekRequest = -1;

	private volatile float _volume;

	private long _positionTicks;

	private long _durationTicks;

	/// <summary>Raised on the audio thread when the track ends by itself (not when stopped).</summary>
	public event Action Finished;

	/// <summary>Raised on the audio thread when the file cannot be decoded or played.</summary>
	public event Action<string> Failed;

	public bool IsPaused => _paused;

	public bool IsAlive => _thread.IsAlive;

	public float Volume
	{
		get
		{
			return _volume;
		}
		set
		{
			_volume = Math.Max(0f, Math.Min(1f, value));
		}
	}

	public TimeSpan Position => TimeSpan.FromTicks(Interlocked.Read(ref _positionTicks));

	public TimeSpan Duration => TimeSpan.FromTicks(Interlocked.Read(ref _durationTicks));

	public AudioPlayer(string path, float volume)
	{
		_path = path;
		_volume = volume;
		_thread = new Thread(Run)
		{
			IsBackground = true,
			Name = "IsaClient Music",
			Priority = ThreadPriority.AboveNormal
		};
		_thread.SetApartmentState(ApartmentState.MTA);
	}

	/// <summary>Plays samples made by the client (decoded or generated) instead of a file. Seeking isn't supported.</summary>
	public AudioPlayer(IPcmSource source, float volume)
		: this((string)null, volume)
	{
		_source = source;
	}

	public void Start()
	{
		_thread.Start();
	}

	public void Pause()
	{
		_paused = true;
	}

	public void Resume()
	{
		_paused = false;
	}

	public void Seek(TimeSpan position)
	{
		Interlocked.Exchange(ref _seekRequest, Math.Max(0L, position.Ticks));
	}

	public void Stop()
	{
		_stop = true;
	}

	public void Dispose()
	{
		_stop = true;
	}

	private void Run()
	{
		IMFSourceReader reader = null;
		IntPtr wave = IntPtr.Zero;
		IntPtr[] headers = new IntPtr[BufferCount];
		IntPtr[] data = new IntPtr[BufferCount];
		bool started = false;
		bool finishedNaturally = false;
		try
		{
			int channels;
			int sampleRate;
			if (_source != null)
			{
				channels = _source.Channels;
				sampleRate = _source.SampleRate;
			}
			else
			{
				Check(MFStartup(MF_VERSION, 0), "MFStartup");
				started = true;
				Check(MFCreateSourceReaderFromURL(_path, IntPtr.Zero, out reader), "Could not open the file");
				reader.SetStreamSelection(AllStreams, selected: false);
				reader.SetStreamSelection(FirstAudioStream, selected: true);
				Check(MFCreateMediaType(out var request), "MFCreateMediaType");
				request.SetGUID(_mF_MT_MAJOR_TYPE, _mFMediaType_Audio);
				request.SetGUID(_mF_MT_SUBTYPE, _mFAudioFormat_PCM);
				request.SetUINT32(_mF_MT_AUDIO_BITS_PER_SAMPLE, 16);
				Check(reader.SetCurrentMediaType(FirstAudioStream, IntPtr.Zero, request), "This audio format is not supported by Windows");
				Marshal.ReleaseComObject(request);
				Check(reader.GetCurrentMediaType(FirstAudioStream, out var current), "GetCurrentMediaType");
				current.GetUINT32(_mF_MT_AUDIO_NUM_CHANNELS, out channels);
				current.GetUINT32(_mF_MT_AUDIO_SAMPLES_PER_SECOND, out sampleRate);
				Marshal.ReleaseComObject(current);
			}
			if (channels < 1 || channels > 8 || sampleRate < 4000)
			{
				throw new InvalidOperationException("Unsupported audio layout (" + channels + " channels, " + sampleRate + " Hz).");
			}
			if (reader != null && reader.GetPresentationAttribute(MediaSource, _mF_PD_DURATION, out var duration) >= 0)
			{
				Interlocked.Exchange(ref _durationTicks, duration.HVal);
			}
			int blockAlign = channels * 2;
			WaveFormatEx format = new WaveFormatEx
			{
				WFormatTag = 1,
				NChannels = (short)channels,
				NSamplesPerSec = sampleRate,
				NAvgBytesPerSec = sampleRate * blockAlign,
				NBlockAlign = (short)blockAlign,
				WBitsPerSample = 16
			};
			Check(WaveOutOpen(out wave, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0), "No audio output device");
			// Six buffers of ~120 ms: about 0.7 s queued in the sound card.
			int bufferBytes = Math.Max(blockAlign * 256, sampleRate * blockAlign / 8 / blockAlign * blockAlign);
			for (int i = 0; i < BufferCount; i++)
			{
				data[i] = Marshal.AllocHGlobal(bufferBytes);
				headers[i] = Marshal.AllocHGlobal(_headerSize);
				for (int b = 0; b < _headerSize; b += 4)
				{
					Marshal.WriteInt32(headers[i], b, 0);
				}
				Marshal.WriteIntPtr(headers[i], 0, data[i]);
				Marshal.WriteInt32(headers[i], IntPtr.Size, bufferBytes);
				WaveOutPrepareHeader(wave, headers[i], _headerSize);
				Marshal.WriteInt32(headers[i], _flagsOffset, Marshal.ReadInt32(headers[i], _flagsOffset) | WHDR_DONE);
			}
			long[] bufferEndTicks = new long[BufferCount];
			bool[] queued = new bool[BufferCount];
			byte[] pending = new byte[0];
			int pendingPos = 0;
			long baseTicks = 0L;
			long submittedBytes = 0L;
			bool endOfStream = false;
			bool pausedOut = false;
			short[] scratch = new short[bufferBytes / 2];
			while (!_stop)
			{
				long seek = Interlocked.Exchange(ref _seekRequest, -1L);
				if (seek >= 0 && reader != null)
				{
					WaveOutReset(wave);
					for (int i = 0; i < BufferCount; i++)
					{
						queued[i] = false;
					}
					PropVariant target = new PropVariant
					{
						Vt = 20,
						HVal = seek
					};
					reader.SetCurrentPosition(Guid.Empty, ref target);
					pending = new byte[0];
					pendingPos = 0;
					baseTicks = seek;
					submittedBytes = 0L;
					endOfStream = false;
					Interlocked.Exchange(ref _positionTicks, seek);
					if (pausedOut)
					{
						WaveOutPause(wave);
					}
				}
				if (_paused != pausedOut)
				{
					pausedOut = _paused;
					if (pausedOut)
					{
						WaveOutPause(wave);
					}
					else
					{
						WaveOutRestart(wave);
					}
				}
				bool anyQueued = false;
				for (int i = 0; i < BufferCount; i++)
				{
					bool done = (Marshal.ReadInt32(headers[i], _flagsOffset) & WHDR_DONE) != 0;
					if (queued[i] && done)
					{
						queued[i] = false;
						Interlocked.Exchange(ref _positionTicks, bufferEndTicks[i]);
					}
					if (queued[i])
					{
						anyQueued = true;
						continue;
					}
					if (endOfStream)
					{
						continue;
					}
					// Fill this buffer with decoded PCM.
					int filled = 0;
					while (filled < bufferBytes && !endOfStream)
					{
						if (pendingPos >= pending.Length)
						{
							pending = (_source != null) ? ReadSource(out endOfStream) : ReadChunk(reader, out endOfStream);
							pendingPos = 0;
							continue;
						}
						int take = Math.Min(bufferBytes - filled, pending.Length - pendingPos);
						Buffer.BlockCopy(pending, pendingPos, scratch, filled, take);
						filled += take;
						pendingPos += take;
					}
					filled -= filled % blockAlign;
					if (filled == 0)
					{
						continue;
					}
					float volume = _volume * _volume;
					int samples = filled / 2;
					for (int s = 0; s < samples; s++)
					{
						scratch[s] = (short)((float)scratch[s] * volume);
					}
					Marshal.Copy(scratch, 0, data[i], samples);
					Marshal.WriteInt32(headers[i], IntPtr.Size, filled);
					Marshal.WriteInt32(headers[i], _flagsOffset, Marshal.ReadInt32(headers[i], _flagsOffset) & ~WHDR_DONE);
					submittedBytes += filled;
					bufferEndTicks[i] = baseTicks + submittedBytes * 10000000L / format.NAvgBytesPerSec;
					WaveOutWrite(wave, headers[i], _headerSize);
					queued[i] = true;
					anyQueued = true;
				}
				if (endOfStream && !anyQueued)
				{
					finishedNaturally = true;
					break;
				}
				Thread.Sleep(15);
			}
		}
		catch (Exception ex)
		{
			if (!_stop)
			{
				Failed?.Invoke(ex.Message);
			}
		}
		finally
		{
			if (wave != IntPtr.Zero)
			{
				WaveOutReset(wave);
				for (int i = 0; i < BufferCount; i++)
				{
					if (headers[i] != IntPtr.Zero)
					{
						WaveOutUnprepareHeader(wave, headers[i], _headerSize);
					}
				}
				WaveOutClose(wave);
			}
			for (int i = 0; i < BufferCount; i++)
			{
				if (headers[i] != IntPtr.Zero)
				{
					Marshal.FreeHGlobal(headers[i]);
				}
				if (data[i] != IntPtr.Zero)
				{
					Marshal.FreeHGlobal(data[i]);
				}
			}
			if (reader != null)
			{
				Marshal.ReleaseComObject(reader);
			}
			_source?.Dispose();
			if (started)
			{
				MFShutdown();
			}
		}
		if (finishedNaturally && !_stop)
		{
			Finished?.Invoke();
		}
	}

	/// <summary>The next chunk of samples from the client's source (about 50 ms).</summary>
	private byte[] ReadSource(out bool endOfStream)
	{
		short[] samples = new short[Math.Max(256, _source.SampleRate / 20) * _source.Channels];
		int count = _source.Read(samples, samples.Length);
		endOfStream = count <= 0;
		if (count <= 0)
		{
			return new byte[0];
		}
		byte[] bytes = new byte[count * 2];
		Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
		return bytes;
	}

	private static byte[] ReadChunk(IMFSourceReader reader, out bool endOfStream)
	{
		endOfStream = false;
		int hr = reader.ReadSample(FirstAudioStream, 0, out _, out var flags, out _, out var sample);
		if (hr < 0 || (flags & EndOfStreamFlag) != 0)
		{
			endOfStream = true;
			if (sample != null)
			{
				Marshal.ReleaseComObject(sample);
			}
			return new byte[0];
		}
		if (sample == null)
		{
			return new byte[0];
		}
		byte[] bytes = new byte[0];
		try
		{
			if (sample.ConvertToContiguousBuffer(out var buffer) >= 0)
			{
				try
				{
					if (buffer.Lock(out var pointer, out _, out var length) >= 0)
					{
						bytes = new byte[length];
						Marshal.Copy(pointer, bytes, 0, length);
						buffer.Unlock();
					}
				}
				finally
				{
					Marshal.ReleaseComObject(buffer);
				}
			}
		}
		finally
		{
			Marshal.ReleaseComObject(sample);
		}
		return bytes;
	}

	private static void Check(int hr, string what)
	{
		if (hr < 0)
		{
			throw new InvalidOperationException(what + " (0x" + hr.ToString("X8") + ").");
		}
	}
}
