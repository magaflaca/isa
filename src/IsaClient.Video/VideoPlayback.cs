using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Xna.Framework.Graphics;
using static IsaClient.Video.MediaInterop;

namespace IsaClient.Video;

/// <summary>Where one stream of a video comes from: a file (or URL Windows opens itself) or a download cache.</summary>
internal sealed class MediaInput
{
	public string Path;

	public RangeCache Cache;

	/// <summary>Tells Windows what the cache holds ("video/mp4", "audio/mp4").</summary>
	public string ContentType = "video/mp4";

	public static MediaInput File(string path)
	{
		return new MediaInput
		{
			Path = path
		};
	}

	internal IMFSourceReader CreateReader(IMFAttributes attributes)
	{
		if (Cache == null)
		{
			Check(MFCreateSourceReaderFromURL(Path, attributes, out var fromUrl), "Could not open the video");
			return fromUrl;
		}
		Check(MFCreateMFByteStreamOnStream(Cache.OpenView(), out var byteStream), "MFCreateMFByteStreamOnStream");
		if (byteStream is IMFAttributes streamAttributes)
		{
			streamAttributes.SetString(ByteStreamContentType, ContentType);
		}
		Check(MFCreateSourceReaderFromByteStream(byteStream, attributes, out var reader), "Windows could not read this video");
		return reader;
	}
}

/// <summary>
/// Plays one video: Media Foundation decodes the picture (to RGB, any size) and the sound (to PCM) on two threads.
/// The sound plays through waveOut and is the clock; the picture shows each frame when the clock reaches it and skips
/// frames it is late for. Speed changes keep the pitch (TimeStretch). Frames go to a texture on the main thread.
/// </summary>
internal sealed class VideoPlayback : IDisposable
{
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

	[StructLayout(LayoutKind.Sequential)]
	private struct MmTime
	{
		public int WType;

		public uint Cb;

		public int Pad;
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

	[DllImport("winmm.dll", EntryPoint = "waveOutGetPosition")]
	private static extern int WaveOutGetPosition(IntPtr handle, ref MmTime time, int size);

	private const int WaveMapper = -1;

	private const int WHDR_DONE = 1;

	private const int TimeBytes = 4;

	private const int BufferCount = 8;

	private static readonly int _headerSize = IntPtr.Size * 3 + 20 + IntPtr.Size;

	private static readonly int _flagsOffset = IntPtr.Size * 2 + 8;

	#endregion

	private readonly MediaInput _videoInput;

	private readonly MediaInput _audioInput;

	private readonly Thread _videoThread;

	private readonly Thread _audioThread;

	private volatile bool _stop;

	private volatile bool _paused;

	private volatile float _volume;

	private volatile float _speed;

	private long _videoSeek = -1;

	private long _audioSeek = -1;

	private volatile bool _speedChanged;

	// The clock: media time at a moment of the stopwatch, moving at the play speed while it runs.
	private readonly object _clockLock = new object();

	private readonly Stopwatch _watch = Stopwatch.StartNew();

	private long _clockTicks;

	private long _clockStamp;

	private bool _clockRunning;

	private bool _clockFree;

	/// <summary>0 opening, 1 playing, 2 no sound in this video.</summary>
	private volatile int _audioState;

	private volatile bool _audioDone;

	private volatile bool _videoDone;

	private volatile bool _audioStarved;

	private long _durationTicks;

	// Frames: the video thread fills _back and swaps it with _front; the main thread uploads _front.
	private readonly object _frameLock = new object();

	private uint[] _front;

	private uint[] _back;

	private int _frontWidth;

	private int _frontHeight;

	private bool _newFrame;

	private Texture2D _texture;

	private volatile string _error;

	public event Action Finished;


	public event Action<string> Failed;

	public bool IsPaused => _paused;

	/// <summary>The first picture is in.</summary>
	public bool HasPicture { get; private set; }

	public bool HasVideo { get; private set; }

	public bool HasSound => _audioState == 1;

	public int VideoWidth { get; private set; }

	public int VideoHeight { get; private set; }

	public bool Ended => (_videoDone || !HasVideo) && (_audioDone || _audioState == 2);

	/// <summary>Waiting for the network (or the decoder) while it should be playing.</summary>
	public bool IsBuffering
	{
		get
		{
			if (_paused || Ended)
			{
				return false;
			}
			if ((_videoInput?.Cache?.Waiting ?? false) || (_audioInput?.Cache?.Waiting ?? false))
			{
				return true;
			}
			if (!HasPicture && HasVideo)
			{
				return true;
			}
			return _audioState == 1 && _audioStarved && !_audioDone;
		}
	}

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

	public float Speed
	{
		get
		{
			return _speed;
		}
		set
		{
			float speed = Math.Max(0.25f, Math.Min(3f, value));
			if (Math.Abs(speed - _speed) < 0.001f)
			{
				return;
			}
			lock (_clockLock)
			{
				_clockTicks = PositionTicks;
				_clockStamp = _watch.ElapsedTicks;
				_speed = speed;
			}
			_speedChanged = true;
		}
	}

	public TimeSpan Position => TimeSpan.FromTicks(Math.Max(0L, PositionTicks));

	public TimeSpan Duration => TimeSpan.FromTicks(Interlocked.Read(ref _durationTicks));

	public VideoPlayback(MediaInput video, MediaInput audio, TimeSpan start, float volume, float speed, bool paused)
	{
		_videoInput = video;
		_audioInput = audio;
		_volume = volume;
		_speed = Math.Max(0.25f, Math.Min(3f, speed));
		_paused = paused;
		_clockTicks = start.Ticks;
		_clockStamp = _watch.ElapsedTicks;
		if (start > TimeSpan.Zero)
		{
			_videoSeek = start.Ticks;
			_audioSeek = start.Ticks;
		}
		_videoThread = new Thread(RunVideo)
		{
			IsBackground = true,
			Name = "IsaClient Video Picture"
		};
		_audioThread = new Thread(RunAudio)
		{
			IsBackground = true,
			Name = "IsaClient Video Sound",
			Priority = ThreadPriority.AboveNormal
		};
		_videoThread.SetApartmentState(ApartmentState.MTA);
		_audioThread.SetApartmentState(ApartmentState.MTA);
	}

	public void SetDuration(TimeSpan duration)
	{
		if (duration > TimeSpan.Zero && Interlocked.Read(ref _durationTicks) <= 0)
		{
			Interlocked.Exchange(ref _durationTicks, duration.Ticks);
		}
	}

	public void Start()
	{
		HasVideo = _videoInput != null;
		if (_videoInput != null)
		{
			_videoThread.Start();
		}
		if (_audioInput != null)
		{
			_audioThread.Start();
		}
		else
		{
			_audioState = 2;
			UseFreeClock();
		}
	}

	public void Pause()
	{
		lock (_clockLock)
		{
			_clockTicks = PositionTicks;
			_clockStamp = _watch.ElapsedTicks;
			_paused = true;
		}
	}

	public void Resume()
	{
		lock (_clockLock)
		{
			_clockStamp = _watch.ElapsedTicks;
			_paused = false;
		}
	}

	public void Seek(TimeSpan position)
	{
		long ticks = Math.Max(0L, position.Ticks);
		long duration = Interlocked.Read(ref _durationTicks);
		if (duration > 0)
		{
			ticks = Math.Min(ticks, Math.Max(0L, duration - TimeSpan.TicksPerSecond / 2));
		}
		lock (_clockLock)
		{
			_clockTicks = ticks;
			_clockStamp = _watch.ElapsedTicks;
			_clockRunning = _clockFree;
		}
		_audioDone = false;
		_videoDone = false;
		_finishedRaised = 0;
		Interlocked.Exchange(ref _videoSeek, ticks);
		Interlocked.Exchange(ref _audioSeek, ticks);
	}

	public void Stop()
	{
		_stop = true;
	}

	public void Dispose()
	{
		_stop = true;
		_texture?.Dispose();
		_texture = null;
	}

	#region Clock

	private long PositionTicks
	{
		get
		{
			lock (_clockLock)
			{
				if (!_clockRunning || _paused)
				{
					return _clockTicks;
				}
				long elapsed = (_watch.ElapsedTicks - _clockStamp) * TimeSpan.TicksPerSecond / Stopwatch.Frequency;
				if (!_clockFree)
				{
					// The sound reports its position every few milliseconds: never run far past the last report.
					elapsed = Math.Min(elapsed, TimeSpan.TicksPerSecond / 8);
				}
				return _clockTicks + (long)(elapsed * _speed);
			}
		}
	}

	/// <summary>No sound to follow (none in the video, or it ended): the clock runs by itself.</summary>
	private void UseFreeClock()
	{
		lock (_clockLock)
		{
			if (_clockFree)
			{
				return;
			}
			_clockTicks = PositionTicks;
			_clockStamp = _watch.ElapsedTicks;
			_clockFree = true;
			_clockRunning = true;
		}
	}

	private void SetClockFromSound(long ticks, bool running)
	{
		lock (_clockLock)
		{
			if (_clockFree)
			{
				return;
			}
			_clockTicks = ticks;
			_clockStamp = _watch.ElapsedTicks;
			_clockRunning = running;
		}
	}

	#endregion

	#region Picture

	/// <summary>Main thread: the newest frame as a texture (null before the first one).</summary>
	public Texture2D Frame(GraphicsDevice device)
	{
		lock (_frameLock)
		{
			if (_newFrame && _front != null)
			{
				if (_texture == null || _texture.Width != _frontWidth || _texture.Height != _frontHeight)
				{
					_texture?.Dispose();
					_texture = new Texture2D(device, _frontWidth, _frontHeight, mipMap: false, SurfaceFormat.Color);
				}
				_texture.SetData(_front, 0, _frontWidth * _frontHeight);
				_newFrame = false;
			}
		}
		return _texture;
	}

	private void RunVideo()
	{
		IMFSourceReader reader = null;
		bool started = false;
		try
		{
			Check(MFStartup(MfVersion, 0), "MFStartup");
			started = true;
			Check(MFCreateAttributes(out var attributes, 1), "MFCreateAttributes");
			attributes.SetUINT32(EnableAdvancedVideoProcessing, 1);
			reader = _videoInput.CreateReader(attributes);
			Release(attributes);
			reader.SetStreamSelection(AllStreams, selected: false);
			Check(reader.SetStreamSelection(FirstVideoStream, selected: true), "This file has no picture");
			Check(MFCreateMediaType(out var request), "MFCreateMediaType");
			request.SetGUID(MajorType, MediaTypeVideo);
			request.SetGUID(Subtype, VideoFormatRgb32);
			Check(reader.SetCurrentMediaType(FirstVideoStream, IntPtr.Zero, request), "Windows cannot decode this video format");
			Release(request);
			ReadVideoFormat(reader, out var width, out var height, out var stride);
			if (reader.GetPresentationAttribute(MediaSource, MediaInterop.Duration, out var duration) >= 0 && duration.HVal > 0)
			{
				Interlocked.Exchange(ref _durationTicks, duration.HVal);
			}
			bool showNext = true;
			long seekTarget = -1L;
			while (!_stop)
			{
				long seek = Interlocked.Exchange(ref _videoSeek, -1L);
				if (seek >= 0)
				{
					PropVariant target = new PropVariant
					{
						Vt = 20,
						HVal = seek
					};
					reader.SetCurrentPosition(Guid.Empty, ref target);
					seekTarget = seek;
					showNext = true;
					_videoDone = false;
				}
				if (_videoDone)
				{
					Thread.Sleep(20);
					continue;
				}
				int hr = reader.ReadSample(FirstVideoStream, 0, out _, out var flags, out var timestamp, out var sample);
				if (hr < 0)
				{
					throw new InvalidOperationException("The picture stopped decoding (0x" + hr.ToString("X8") + ").");
				}
				if ((flags & CurrentMediaTypeChangedFlag) != 0)
				{
					ReadVideoFormat(reader, out width, out height, out stride);
				}
				if ((flags & EndOfStreamFlag) != 0)
				{
					Release(sample);
					_videoDone = true;
					if (_audioState != 1 || _audioDone)
					{
						CheckFinished();
					}
					continue;
				}
				if (sample == null)
				{
					continue;
				}
				try
				{
					if (Interlocked.Read(ref _videoSeek) >= 0)
					{
						continue;
					}
					if (seekTarget >= 0)
					{
						// Decoding from the key frame before the target: skip up to it.
						if (timestamp < seekTarget - TimeSpan.TicksPerSecond / 20)
						{
							continue;
						}
						seekTarget = -1L;
					}
					if (!showNext && !WaitForClock(timestamp))
					{
						continue;
					}
					Publish(sample, width, height, stride);
					showNext = false;
				}
				finally
				{
					Release(sample);
				}
			}
		}
		catch (Exception ex)
		{
			if (!_stop)
			{
				_error = ex.Message;
				_stop = true;
				Failed?.Invoke(ex.Message);
			}
		}
		finally
		{
			Release(reader);
			if (started)
			{
				MFShutdown();
			}
		}
	}

	/// <summary>Waits until the clock reaches a frame; false when the frame is too late to show (or a seek came).</summary>
	private bool WaitForClock(long timestamp)
	{
		while (!_stop && Interlocked.Read(ref _videoSeek) < 0)
		{
			if (_audioState == 0)
			{
				Thread.Sleep(5);
				continue;
			}
			long now = PositionTicks;
			long ahead = timestamp - now;
			if (ahead <= TimeSpan.TicksPerMillisecond * 4)
			{
				return ahead > -TimeSpan.TicksPerMillisecond * 120;
			}
			Thread.Sleep((int)Math.Max(1L, Math.Min(10L, ahead / TimeSpan.TicksPerMillisecond / 2)));
		}
		return false;
	}

	private void ReadVideoFormat(IMFSourceReader reader, out int width, out int height, out int stride)
	{
		Check(reader.GetCurrentMediaType(FirstVideoStream, out var current), "GetCurrentMediaType");
		current.GetUINT64(FrameSize, out var size);
		width = (int)(size >> 32);
		height = (int)(size & 0xFFFFFFFFL);
		stride = (current.GetUINT32(DefaultStride, out var defaultStride) >= 0) ? defaultStride : (width * 4);
		Release(current);
		if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
		{
			throw new InvalidOperationException("Unsupported picture size " + width + "x" + height + ".");
		}
		VideoWidth = width;
		VideoHeight = height;
	}

	private unsafe void Publish(IMFSample sample, int width, int height, int stride)
	{
		if (sample.ConvertToContiguousBuffer(out var buffer) < 0)
		{
			return;
		}
		try
		{
			if (buffer.Lock(out var pointer, out _, out var length) < 0)
			{
				return;
			}
			try
			{
				int rowBytes = Math.Abs(stride);
				if (rowBytes < width * 4 || (long)rowBytes * height > length)
				{
					rowBytes = length / height;
					if (rowBytes < width * 4)
					{
						return;
					}
				}
				int pixels = width * height;
				uint[] back = _back;
				if (back == null || back.Length < pixels)
				{
					back = new uint[pixels];
				}
				byte* source = (byte*)pointer;
				fixed (uint* destination = back)
				{
					for (int y = 0; y < height; y++)
					{
						uint* row = (uint*)(source + (long)((stride < 0) ? (height - 1 - y) : y) * rowBytes);
						uint* to = destination + (long)y * width;
						for (int x = 0; x < width; x++)
						{
							// BGRX -> RGBA
							uint p = row[x];
							to[x] = 0xFF000000u | (p & 0xFF00u) | ((p >> 16) & 0xFFu) | ((p & 0xFFu) << 16);
						}
					}
				}
				lock (_frameLock)
				{
					_back = _front;
					_front = back;
					_frontWidth = width;
					_frontHeight = height;
					_newFrame = true;
				}
				HasPicture = true;
			}
			finally
			{
				buffer.Unlock();
			}
		}
		finally
		{
			Release(buffer);
		}
	}

	#endregion

	#region Sound

	private void RunAudio()
	{
		IMFSourceReader reader = null;
		IntPtr wave = IntPtr.Zero;
		IntPtr[] headers = new IntPtr[BufferCount];
		IntPtr[] data = new IntPtr[BufferCount];
		bool started = false;
		try
		{
			Check(MFStartup(MfVersion, 0), "MFStartup");
			started = true;
			try
			{
				reader = _audioInput.CreateReader(null);
				reader.SetStreamSelection(AllStreams, selected: false);
				Check(reader.SetStreamSelection(FirstAudioStream, selected: true), "no sound");
				Check(MFCreateMediaType(out var request), "MFCreateMediaType");
				request.SetGUID(MajorType, MediaTypeAudio);
				request.SetGUID(Subtype, AudioFormatPcm);
				request.SetUINT32(AudioBitsPerSample, 16);
				Check(reader.SetCurrentMediaType(FirstAudioStream, IntPtr.Zero, request), "unsupported sound");
				Release(request);
			}
			catch (Exception) when (!_stop && HasVideo)
			{
				// A video without (usable) sound still plays.
				_audioState = 2;
				UseFreeClock();
				return;
			}
			Check(reader.GetCurrentMediaType(FirstAudioStream, out var current), "GetCurrentMediaType");
			current.GetUINT32(AudioChannels, out var channels);
			current.GetUINT32(AudioSampleRate, out var sampleRate);
			Release(current);
			if (channels < 1 || channels > 8 || sampleRate < 4000)
			{
				throw new InvalidOperationException("Unsupported sound layout (" + channels + " channels, " + sampleRate + " Hz).");
			}
			if (reader.GetPresentationAttribute(MediaSource, MediaInterop.Duration, out var duration) >= 0 && duration.HVal > 0 && Interlocked.Read(ref _durationTicks) <= 0)
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
			// Eight buffers of ~50 ms: little delay between the picture and the sound after a seek.
			int framesPerBuffer = Math.Max(256, sampleRate / 20);
			int bufferBytes = framesPerBuffer * blockAlign;
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
			bool[] queued = new bool[BufferCount];
			TimeStretch stretch = new TimeStretch(sampleRate, channels);
			short[] scratch = new short[framesPerBuffer * channels];
			byte[] pending = new byte[0];
			int pendingPos = 0;
			bool endOfStream = false;
			bool pausedOut = false;
			long baseTicks = _clockTicks;
			float segmentSpeed = _speed;
			long sampleTicks = -1L;
			// Sound still to drop after a seek (bytes): the reader starts at a cut point before the target, and that
			// can be several samples back (DASH sound), not only inside the first one.
			long skipBytes = 0L;
			_audioState = 1;
			while (!_stop)
			{
				long seek = Interlocked.Exchange(ref _audioSeek, -1L);
				bool speedChanged = _speedChanged;
				if (seek < 0 && speedChanged)
				{
					_speedChanged = false;
					seek = PositionTicks;
				}
				if (seek >= 0)
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
					stretch.Reset();
					segmentSpeed = _speed;
					stretch.Speed = segmentSpeed;
					baseTicks = seek;
					sampleTicks = -1L;
					skipBytes = 0L;
					endOfStream = false;
					_audioDone = false;
					SetClockFromSound(seek, running: false);
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
					if (queued[i] && (Marshal.ReadInt32(headers[i], _flagsOffset) & WHDR_DONE) != 0)
					{
						queued[i] = false;
					}
					if (queued[i])
					{
						anyQueued = true;
						continue;
					}
					if (endOfStream && stretch.Buffered == 0)
					{
						continue;
					}
					// Feed the stretcher until a buffer's worth of sound is ready.
					while (stretch.Buffered < framesPerBuffer && !endOfStream && !_stop)
					{
						if (pendingPos >= pending.Length)
						{
							pending = ReadSound(reader, out endOfStream, out var time);
							pendingPos = 0;
							if (sampleTicks < 0 && time >= 0 && pending.Length > 0)
							{
								sampleTicks = time;
								if (time > baseTicks)
								{
									// The sound only starts after the target: the clock follows the sound.
									baseTicks = time;
									SetClockFromSound(baseTicks, running: false);
								}
								else
								{
									// It starts at a cut point before the target: all of that is dropped, however many samples it spans.
									skipBytes = (baseTicks - time) * sampleRate / TimeSpan.TicksPerSecond * blockAlign;
								}
							}
							if (skipBytes > 0 && pending.Length > 0)
							{
								int drop = (int)Math.Min(skipBytes, pending.Length - pendingPos);
								drop -= drop % blockAlign;
								pendingPos += drop;
								skipBytes -= drop;
								if (drop == 0)
								{
									skipBytes = 0L;
								}
							}
							continue;
						}
						int take = Math.Min(pending.Length - pendingPos, framesPerBuffer * blockAlign);
						take -= take % blockAlign;
						if (take <= 0)
						{
							pendingPos = pending.Length;
							continue;
						}
						if (segmentSpeed == 1f)
						{
							// Straight through at 1x.
							stretch.Queue(pending, pendingPos, take);
						}
						else
						{
							stretch.Put(pending, pendingPos, take);
						}
						pendingPos += take;
					}
					int frames = stretch.Take(scratch, framesPerBuffer);
					if (frames == 0)
					{
						continue;
					}
					float volume = _volume * _volume;
					int samples = frames * channels;
					if (volume < 0.999f)
					{
						for (int s = 0; s < samples; s++)
						{
							scratch[s] = (short)(scratch[s] * volume);
						}
					}
					Marshal.Copy(scratch, 0, data[i], samples);
					Marshal.WriteInt32(headers[i], IntPtr.Size, frames * blockAlign);
					Marshal.WriteInt32(headers[i], _flagsOffset, Marshal.ReadInt32(headers[i], _flagsOffset) & ~WHDR_DONE);
					WaveOutWrite(wave, headers[i], _headerSize);
					queued[i] = true;
					anyQueued = true;
				}
				// Where the sound card is: bytes played since the last reset, at the speed of this stretch.
				MmTime time2 = new MmTime
				{
					WType = TimeBytes
				};
				if (WaveOutGetPosition(wave, ref time2, Marshal.SizeOf(typeof(MmTime))) == 0 && time2.WType == TimeBytes)
				{
					long played = (long)time2.Cb * TimeSpan.TicksPerSecond / format.NAvgBytesPerSec;
					SetClockFromSound(baseTicks + (long)(played * segmentSpeed), anyQueued && !pausedOut);
				}
				_audioStarved = !anyQueued && !endOfStream;
				if (endOfStream && !anyQueued && stretch.Buffered == 0 && !_audioDone)
				{
					_audioDone = true;
					UseFreeClockAfterSound();
					if (_videoDone || !HasVideo)
					{
						CheckFinished();
					}
				}
				Thread.Sleep(8);
			}
		}
		catch (Exception ex)
		{
			if (!_stop)
			{
				_error = ex.Message;
				_stop = true;
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
			Release(reader);
			if (started)
			{
				MFShutdown();
			}
		}
	}

	/// <summary>The sound ended before the picture: the picture keeps going on the free clock.</summary>
	private void UseFreeClockAfterSound()
	{
		lock (_clockLock)
		{
			_clockStamp = _watch.ElapsedTicks;
			_clockFree = true;
			_clockRunning = true;
		}
	}

	private static byte[] ReadSound(IMFSourceReader reader, out bool endOfStream, out long time)
	{
		endOfStream = false;
		time = -1L;
		int hr = reader.ReadSample(FirstAudioStream, 0, out _, out var flags, out var timestamp, out var sample);
		if (hr < 0 || (flags & EndOfStreamFlag) != 0)
		{
			endOfStream = true;
			Release(sample);
			return new byte[0];
		}
		if (sample == null)
		{
			return new byte[0];
		}
		try
		{
			time = timestamp;
			return SampleBytes(sample);
		}
		finally
		{
			Release(sample);
		}
	}

	#endregion

	private int _finishedRaised;

	private void CheckFinished()
	{
		if (!_stop && Ended && Interlocked.Exchange(ref _finishedRaised, 1) == 0)
		{
			Finished?.Invoke();
		}
	}
}
