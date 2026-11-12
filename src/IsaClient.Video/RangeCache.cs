using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using IsaClient.Core;

namespace IsaClient.Video;

/// <summary>
/// One remote file (a video or audio stream) downloaded into a file on disk in the background, a few megabytes per
/// request (like yt-dlp does for YouTube), starting wherever the player is reading. Readers (<see cref="View"/>) wait
/// for the bytes they need; a seek far away moves the download there. When every byte is in, the file is a normal
/// copy of the stream, which is what "Download" saves.
/// </summary>
internal sealed class RangeCache
{
	private const int ChunkSize = 256 * 1024;

	private const int RequestSize = 4 * 1024 * 1024;

	/// <summary>How far ahead of the player the stream is fetched while only watching.</summary>
	private const long ReadAhead = 64L * 1024 * 1024;

	/// <summary>The end of the file, fetched apart when Windows looks there.</summary>
	private const long TailSize = 1024 * 1024;

	private bool _tailFetching;

	private bool _tailFailed;

	private readonly string _url;

	private readonly Dictionary<string, string> _headers;

	private readonly object _lock = new object();

	private readonly Thread _thread;

	private FileStream _file;

	private long _length = -1;

	private bool[] _have = new bool[0];

	private int _haveCount;

	/// <summary>Where the player read last: the download goes on from there.</summary>
	private long _lastRead;

	private long _requestStart = -1;

	private long _requestEnd = -1;

	private long _requestDone = -1;

	private bool _restart;

	private volatile bool _stopped;

	private int _references = 1;

	/// <summary>Where the bytes are kept.</summary>
	public string FilePath { get; }

	/// <summary>Keep the file when the last reference goes (a download took it over).</summary>
	public bool KeepFile;

	/// <summary>Fetch the whole file, not only what is ahead of the player (downloads).</summary>
	public volatile bool DownloadAll;

	/// <summary>
	/// Hide the edit lists of the tracks (YouTube's DASH videos): Windows reads YouTube's edit list as "no frames" and
	/// the picture ends before it starts. Without it the frames come 40 ms late at most.
	/// </summary>
	public bool HideEditLists;

	private List<long> _patches;


	public string Error { get; private set; }

	/// <summary>A reader is waiting for bytes that have not arrived yet.</summary>
	public bool Waiting { get; private set; }

	public long Length => _length;

	public bool Complete
	{
		get
		{
			lock (_lock)
			{
				return _length >= 0 && _haveCount == _have.Length;
			}
		}
	}

	public float Progress
	{
		get
		{
			lock (_lock)
			{
				return (_have.Length == 0) ? 0f : ((float)_haveCount / _have.Length);
			}
		}
	}

	public RangeCache(string url, Dictionary<string, string> headers, string filePath)
	{
		_url = url;
		_headers = headers ?? new Dictionary<string, string>();
		FilePath = filePath;
		_thread = new Thread(Run)
		{
			IsBackground = true,
			Name = "IsaClient Video Download",
			Priority = ThreadPriority.BelowNormal
		};
		_thread.Start();
	}

	/// <summary>Waits until the size of the file is known (the first answer of the server).</summary>
	public void WaitForLength(int timeoutMs)
	{
		DateTime until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
		lock (_lock)
		{
			while (_length < 0 && Error == null && !_stopped)
			{
				int left = (int)(until - DateTime.UtcNow).TotalMilliseconds;
				if (left <= 0)
				{
					throw new IOException("The video server did not answer.");
				}
				Monitor.Wait(_lock, Math.Min(left, 250));
			}
		}
		if (Error != null)
		{
			throw new IOException(Error);
		}
	}

	public void AddReference()
	{
		lock (_lock)
		{
			_references++;
		}
	}

	/// <summary>Drops one user of the cache; the last one stops the download and deletes the file (unless kept).</summary>
	public void Release()
	{
		lock (_lock)
		{
			if (--_references > 0)
			{
				return;
			}
			_stopped = true;
			Monitor.PulseAll(_lock);
		}
		new Thread((ThreadStart)delegate
		{
			_thread.Join(5000);
			lock (_lock)
			{
				_file?.Dispose();
				_file = null;
			}
			// The antivirus may still be reading a big file: try a few times.
			for (int attempt = 0; attempt < 5 && !KeepFile && File.Exists(FilePath); attempt++)
			{
				try
				{
					File.Delete(FilePath);
				}
				catch
				{
					Thread.Sleep(1000);
				}
			}
		})
		{
			IsBackground = true
		}.Start();
	}

	/// <summary>The parts already downloaded, as fractions of the file (for the seek bar).</summary>
	public List<KeyValuePair<float, float>> Ranges()
	{
		List<KeyValuePair<float, float>> ranges = new List<KeyValuePair<float, float>>();
		lock (_lock)
		{
			int count = _have.Length;
			int i = 0;
			while (i < count)
			{
				if (!_have[i])
				{
					i++;
					continue;
				}
				int start = i;
				while (i < count && _have[i])
				{
					i++;
				}
				ranges.Add(new KeyValuePair<float, float>((float)start / count, (float)i / count));
			}
		}
		return ranges;
	}

	/// <summary>A new reader over the file, with its own position.</summary>
	public View OpenView()
	{
		return new View(this);
	}

	#region Reading

	/// <summary>Bytes that can be read right now from <paramref name="position"/> on.</summary>
	private long AvailableAt(long position)
	{
		if (_length < 0 || position >= _length)
		{
			return 0L;
		}
		long end = position;
		int chunk = (int)(position / ChunkSize);
		while (chunk < _have.Length && _have[chunk])
		{
			chunk++;
			end = Math.Min(_length, (long)chunk * ChunkSize);
		}
		// The request being written covers a few more bytes than its finished chunks.
		if (_requestStart >= 0 && end >= _requestStart && end < _requestDone)
		{
			end = _requestDone;
		}
		return end - position;
	}

	internal int Read(long position, byte[] buffer, int count)
	{
		while (true)
		{
			long tailStart = -1L;
			lock (_lock)
			{
				bool known = _length >= 0;
				if (_stopped || Error != null || (known && (position >= _length || AvailableAt(position) >= Math.Min(count, _length - position))))
				{
					Waiting = false;
					if (known && position < _length - TailSize)
					{
						_lastRead = position;
					}
					return Copy(position, buffer, count);
				}
				bool coming = _requestStart >= 0 && position >= _requestStart && position <= _requestEnd;
				if (!coming && known && position >= _length - TailSize && !_tailFailed)
				{
					// Windows looks at the end of the file first: fetch the end on the side, the download stays where it is.
					if (!_tailFetching)
					{
						_tailFetching = true;
						tailStart = Math.Max(0L, _length - TailSize) / ChunkSize * ChunkSize;
					}
				}
				else
				{
					// Move the download here unless the running request is about to bring these bytes.
					if (!coming)
					{
						_restart = true;
					}
					_lastRead = position;
				}
				if (tailStart < 0)
				{
					Waiting = true;
					Monitor.Wait(_lock, 200);
					continue;
				}
			}
			FetchTail(tailStart);
		}
	}

	private int Copy(long position, byte[] buffer, int count)
	{
		if (_file == null || _length < 0 || position >= _length)
		{
			return 0;
		}
		int n = (int)Math.Min(count, AvailableAt(position));
		_file.Position = position;
		int read = 0;
		while (read < n)
		{
			int got = _file.Read(buffer, read, n - read);
			if (got <= 0)
			{
				break;
			}
			read += got;
		}
		if (HideEditLists)
		{
			FindPatches();
			foreach (long patch in _patches ?? new List<long>())
			{
				for (int k = 0; k < 4; k++)
				{
					long at = patch + k - position;
					if (at >= 0 && at < read)
					{
						buffer[at] = (byte)"free"[k];
					}
				}
			}
		}
		return read;
	}

	/// <summary>Finds the 'edts' boxes of the tracks (once the header is in): their type is read as 'free'.</summary>
	private void FindPatches()
	{
		if (_patches != null)
		{
			return;
		}
		long available = AvailableAt(0L);
		int size = (int)Math.Min(available, 2 * 1024 * 1024);
		if (size < 16)
		{
			return;
		}
		byte[] head = new byte[size];
		_file.Position = 0L;
		int got = 0;
		while (got < size)
		{
			int n = _file.Read(head, got, size - got);
			if (n <= 0)
			{
				break;
			}
			got += n;
		}
		List<long> patches = new List<long>();
		long pos = 0L;
		while (pos + 8 <= got)
		{
			long boxSize = BoxSize(head, pos, got, out var headerLength);
			string type = BoxType(head, pos);
			if (boxSize < 8)
			{
				break;
			}
			if (type == "moov")
			{
				if (pos + boxSize > got && got < size)
				{
					return;
				}
				foreach (long trak in Children(head, pos + headerLength, Math.Min(got, pos + boxSize), "trak"))
				{
					long trakSize = BoxSize(head, trak, got, out var trakHeader);
					foreach (long edts in Children(head, trak + trakHeader, Math.Min(got, trak + trakSize), "edts"))
					{
						patches.Add(edts + 4);
					}
				}
				break;
			}
			pos += boxSize;
		}
		_patches = patches;
	}

	private static IEnumerable<long> Children(byte[] data, long from, long to, string type)
	{
		long pos = from;
		while (pos + 8 <= to)
		{
			long size = BoxSize(data, pos, to, out _);
			if (size < 8)
			{
				yield break;
			}
			if (BoxType(data, pos) == type)
			{
				yield return pos;
			}
			pos += size;
		}
	}

	private static long BoxSize(byte[] data, long pos, long end, out int headerLength)
	{
		headerLength = 8;
		long size = ((long)data[pos] << 24) | ((long)data[pos + 1] << 16) | ((long)data[pos + 2] << 8) | data[pos + 3];
		if (size == 1 && pos + 16 <= end)
		{
			headerLength = 16;
			size = 0L;
			for (int i = 0; i < 8; i++)
			{
				size = (size << 8) | data[pos + 8 + i];
			}
		}
		return size;
	}

	private static string BoxType(byte[] data, long pos)
	{
		return new string(new char[4]
		{
			(char)data[pos + 4],
			(char)data[pos + 5],
			(char)data[pos + 6],
			(char)data[pos + 7]
		});
	}

	#endregion

	#region Downloading

	private void Run()
	{
		int failures = 0;
		byte[] buffer = new byte[64 * 1024];
		while (!_stopped)
		{
			long start;
			long end;
			lock (_lock)
			{
				if (_length >= 0)
				{
					int next = NextMissing();
					if (next < 0)
					{
						// Everything wanted is in: wait for the player to move on (or to be released).
						Monitor.Wait(_lock, 300);
						continue;
					}
					start = (long)next * ChunkSize;
					int last = next;
					while (last + 1 < _have.Length && !_have[last + 1] && (long)(last + 1 - next) * ChunkSize < RequestSize)
					{
						last++;
					}
					end = Math.Min(_length, (long)(last + 1) * ChunkSize) - 1;
				}
				else
				{
					start = 0L;
					end = RequestSize - 1;
				}
				_restart = false;
			}
			try
			{
				Fetch(start, end, buffer);
				failures = 0;
			}
			catch (WebException ex) when (!_stopped)
			{
				int status = (int)((ex.Response as HttpWebResponse)?.StatusCode ?? 0);
				failures++;
				if (status == 403 || status == 404 || status == 410 || failures >= 6)
				{
					Fail((status == 403 || status == 410) ? "The video link expired or was refused (" + status + "). Play it again." : ("Download failed: " + ex.Message));
					return;
				}
				Thread.Sleep(500 * failures);
			}
			catch (Exception ex) when (!_stopped)
			{
				if (DiskFull(ex))
				{
					Fail("Not enough free disk space for the video (free some space on " + Path.GetPathRoot(FilePath) + ").");
					return;
				}
				failures++;
				if (failures >= 6)
				{
					Fail("Download failed: " + ex.Message);
					ClientLog.LogException(ex);
					return;
				}
				Thread.Sleep(500 * failures);
			}
			catch
			{
				// Released while a request was running: the file is gone, stop quietly.
				return;
			}
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle device, int code, IntPtr input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);

	private const int SetSparse = 0x000900C4;

	private static bool DiskFull(Exception ex)
	{
		int code = ex.HResult & 0xFFFF;
		return ex is IOException && (code == 112 || code == 39);
	}

	private void Fail(string message)
	{
		lock (_lock)
		{
			Error = message;
			Monitor.PulseAll(_lock);
		}
	}

	/// <summary>
	/// First missing chunk from where the player reads (up to <see cref="ReadAhead"/> ahead of it); when the whole
	/// file is wanted (<see cref="DownloadAll"/>), then the first missing one anywhere.
	/// </summary>
	private int NextMissing()
	{
		int from = (int)Math.Min(_have.Length, Math.Max(0L, _lastRead / ChunkSize));
		int limit = DownloadAll ? _have.Length : (int)Math.Min(_have.Length, from + ReadAhead / ChunkSize);
		for (int i = from; i < limit; i++)
		{
			if (!_have[i])
			{
				return i;
			}
		}
		if (!DownloadAll)
		{
			return -1;
		}
		for (int i = 0; i < from; i++)
		{
			if (!_have[i])
			{
				return i;
			}
		}
		return -1;
	}

	private HttpWebRequest CreateRequest(long start, long end)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(_url);
		request.Timeout = 20000;
		request.ReadWriteTimeout = 20000;
		request.AutomaticDecompression = DecompressionMethods.None;
		// .NET allows 2 connections per server by default: picture, sound and side requests need more.
		request.ServicePoint.ConnectionLimit = Math.Max(request.ServicePoint.ConnectionLimit, 16);
		foreach (KeyValuePair<string, string> header in _headers)
		{
			SetHeader(request, header.Key, header.Value);
		}
		request.AddRange(start, end);
		return request;
	}

	/// <summary>Downloads the end of the file in one request, in the reader's thread.</summary>
	private void FetchTail(long start)
	{
		bool ok = false;
		try
		{
			long end = _length - 1;
			HttpWebRequest request = CreateRequest(start, end);
			byte[] data = new byte[end - start + 1];
			using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
			{
				if (response.StatusCode != HttpStatusCode.PartialContent)
				{
					request.Abort();
					return;
				}
				using Stream stream = response.GetResponseStream();
				int got = 0;
				while (got < data.Length && !_stopped)
				{
					int n = stream.Read(data, got, data.Length - got);
					if (n <= 0)
					{
						break;
					}
					got += n;
				}
				if (got < data.Length)
				{
					return;
				}
			}
			lock (_lock)
			{
				if (_file == null)
				{
					return;
				}
				_file.Position = start;
				_file.Write(data, 0, data.Length);
				for (int c = (int)(start / ChunkSize); c < _have.Length; c++)
				{
					if (!_have[c])
					{
						_have[c] = true;
						_haveCount++;
					}
				}
				ok = true;
			}
		}
		catch (Exception ex)
		{
		}
		finally
		{
			lock (_lock)
			{
				_tailFetching = false;
				_tailFailed = !ok;
				Monitor.PulseAll(_lock);
			}
		}
	}

	private void Fetch(long start, long end, byte[] buffer)
	{
		HttpWebRequest request = CreateRequest(start, end);
		using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
		long total;
		long offset = start;
		if (response.StatusCode == HttpStatusCode.PartialContent)
		{
			total = ParseTotal(response.Headers["Content-Range"]);
		}
		else
		{
			// The server sent the whole file.
			total = response.ContentLength;
			offset = 0L;
		}
		lock (_lock)
		{
			if (_length < 0)
			{
				if (total <= 0)
				{
					throw new IOException("The server did not say how big the video is.");
				}
				Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
				_file = new FileStream(FilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
				// Sparse: only what is downloaded takes disk space, not the whole length up front.
				DeviceIoControl(_file.SafeFileHandle, SetSparse, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
				_file.SetLength(total);
				_have = new bool[(int)((total + ChunkSize - 1) / ChunkSize)];
				_length = total;
				Monitor.PulseAll(_lock);
			}
			_requestStart = offset;
			_requestEnd = (response.StatusCode == HttpStatusCode.PartialContent) ? end : (_length - 1);
			_requestDone = offset;
		}
		using Stream stream = response.GetResponseStream();
		try
		{
			while (!_stopped)
			{
				int got = stream.Read(buffer, 0, buffer.Length);
				if (got <= 0)
				{
					break;
				}
				lock (_lock)
				{
					if (_file == null || _stopped)
					{
						break;
					}
					if (offset + got > _length)
					{
						got = (int)(_length - offset);
					}
					_file.Position = offset;
					_file.Write(buffer, 0, got);
					long before = offset;
					offset += got;
					_requestDone = offset;
					for (int c = (int)(before / ChunkSize); c < _have.Length && (long)c * ChunkSize < offset; c++)
					{
						long chunkStart = (long)c * ChunkSize;
						long chunkEnd = Math.Min(_length, chunkStart + ChunkSize);
						// A chunk counts once all of it came in this request (or it was already in).
						if (!_have[c] && chunkEnd <= offset && chunkStart >= _requestStart)
						{
							_have[c] = true;
							_haveCount++;
						}
					}
					Monitor.PulseAll(_lock);
					if (_restart || offset >= _length)
					{
						break;
					}
				}
			}
		}
		finally
		{
			lock (_lock)
			{
				_requestStart = -1L;
				_requestEnd = -1L;
				_requestDone = -1L;
			}
			if (_restart || _stopped)
			{
				request.Abort();
			}
		}
	}

	private static long ParseTotal(string contentRange)
	{
		// bytes 0-4194303/123456789
		int slash = contentRange?.LastIndexOf('/') ?? -1;
		if (slash < 0 || !long.TryParse(contentRange.Substring(slash + 1), out var total))
		{
			return -1L;
		}
		return total;
	}

	internal static void SetHeader(HttpWebRequest request, string name, string value)
	{
		switch (name.ToLowerInvariant())
		{
		case "user-agent":
			request.UserAgent = value;
			break;
		case "accept":
			request.Accept = value;
			break;
		case "referer":
			request.Referer = value;
			break;
		case "connection":
		case "host":
		case "range":
		case "content-length":
			break;
		default:
			try
			{
				request.Headers[name] = value;
			}
			catch
			{
			}
			break;
		}
	}

	#endregion

	/// <summary>An IStream over the cache for Media Foundation, with its own read position.</summary>
	[ComVisible(true)]
	internal sealed class View : IStream
	{
		private readonly RangeCache _cache;

		private long _position;

		internal View(RangeCache cache)
		{
			_cache = cache;
		}

		public void Read(byte[] pv, int cb, IntPtr pcbRead)
		{
			int read = _cache.Read(_position, pv, cb);
			_position += read;
			if (pcbRead != IntPtr.Zero)
			{
				Marshal.WriteInt32(pcbRead, read);
			}
		}

		public void Write(byte[] pv, int cb, IntPtr pcbWritten)
		{
			throw new UnauthorizedAccessException();
		}

		public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
		{
			long basePosition = dwOrigin switch
			{
				1 => _position,
				2 => Math.Max(0L, _cache.Length),
				_ => 0L
			};
			_position = Math.Max(0L, basePosition + dlibMove);
			if (plibNewPosition != IntPtr.Zero)
			{
				Marshal.WriteInt64(plibNewPosition, _position);
			}
		}

		public void SetSize(long libNewSize)
		{
			throw new NotSupportedException();
		}

		public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten)
		{
			throw new NotSupportedException();
		}

		public void Commit(int grfCommitFlags)
		{
		}

		public void Revert()
		{
		}

		public void LockRegion(long libOffset, long cb, int dwLockType)
		{
			throw new NotSupportedException();
		}

		public void UnlockRegion(long libOffset, long cb, int dwLockType)
		{
			throw new NotSupportedException();
		}

		public void Stat(out System.Runtime.InteropServices.ComTypes.STATSTG pstatstg, int grfStatFlag)
		{
			pstatstg = new System.Runtime.InteropServices.ComTypes.STATSTG
			{
				type = 2,
				cbSize = Math.Max(0L, _cache.Length)
			};
		}

		public void Clone(out IStream ppstm)
		{
			ppstm = new View(_cache)
			{
				_position = _position
			};
		}
	}
}
