using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace IsaClient.MultiClient;

/// <summary>
/// TCP transport with Terraria framing for a MultiClient session. Runs on two dedicated background threads (read + write) so it never
/// touches the game's main thread or the real Netplay socket. Frames are [ushort length incl. header][byte type][payload].
/// The underlying TcpClient can be handed over without closing it (<see cref="Detach"/>) and an existing one can be adopted
/// (<see cref="Adopt"/>); that is how the game swaps which session it drives without reconnecting.
/// net48/x86 friendly: blocking sockets, no async, no channels.
/// </summary>
public sealed class ClientConnection
{
	private TcpClient _tcp;

	private NetworkStream _stream;

	private readonly object _sendLock = new object();

	private readonly System.Collections.Generic.Queue<byte[]> _sendQueue = new System.Collections.Generic.Queue<byte[]>();

	private readonly ManualResetEventSlim _sendSignal = new ManualResetEventSlim(false);

	private Thread _readThread;

	private Thread _writeThread;

	private volatile bool _closed;

	private volatile bool _detaching;

	private bool _detached;

	private int _closeReported;

	// Owned by the read thread until it exits.
	private byte[] _buffer = new byte[1 << 16];

	private int _filled;

	/// <summary>(type, payload) delivered on the read thread. Payload is a fresh array owned by the callback.</summary>
	public event Action<byte, byte[]> OnPacket;

	public event Action<string> OnClosed;

	public bool IsConnected => !_closed && !_detached && _tcp != null && _tcp.Connected;

	public void Connect(string host, int port)
	{
		TcpClient tcp = new TcpClient
		{
			NoDelay = true
		};
		tcp.Connect(host, port);
		Start(tcp, null);
	}

	/// <summary>Takes over an already connected socket. <paramref name="leftover"/> are bytes already read from it that belong to the next frame(s).</summary>
	public void Adopt(TcpClient tcp, byte[] leftover)
	{
		Start(tcp, leftover);
	}

	private void Start(TcpClient tcp, byte[] leftover)
	{
		_tcp = tcp;
		_tcp.NoDelay = true;
		_stream = _tcp.GetStream();
		if (leftover != null && leftover.Length > 0)
		{
			EnsureCapacity(leftover.Length);
			Buffer.BlockCopy(leftover, 0, _buffer, 0, leftover.Length);
			_filled = leftover.Length;
		}
		_readThread = new Thread(ReadLoop)
		{
			IsBackground = true,
			Name = "IsaClient Session Read"
		};
		_writeThread = new Thread(WriteLoop)
		{
			IsBackground = true,
			Name = "IsaClient Session Write"
		};
		_readThread.Start();
		_writeThread.Start();
	}

	/// <summary>
	/// Stops both threads without closing the socket (queued packets are flushed first) and returns it together with the
	/// bytes read but not yet parsed. Returns null if the connection is already gone.
	/// </summary>
	public TcpClient Detach(out byte[] leftover)
	{
		leftover = new byte[0];
		if (_closed || _detached || _tcp == null)
		{
			return null;
		}
		_detaching = true;
		_sendSignal.Set();
		bool readDone = _readThread == null || _readThread.Join(2000);
		bool writeDone = _writeThread == null || _writeThread.Join(2000);
		if (!readDone || !writeDone || _closed)
		{
			Close("could not hand over the connection");
			return null;
		}
		leftover = new byte[_filled];
		Buffer.BlockCopy(_buffer, 0, leftover, 0, _filled);
		_filled = 0;
		_detached = true;
		TcpClient tcp = _tcp;
		_tcp = null;
		return tcp;
	}

	public void Send(byte[] packet)
	{
		if (_closed || _detached)
		{
			return;
		}
		lock (_sendLock)
		{
			_sendQueue.Enqueue(packet);
		}
		_sendSignal.Set();
	}

	private void WriteLoop()
	{
		try
		{
			while (!_closed)
			{
				byte[] packet = null;
				lock (_sendLock)
				{
					if (_sendQueue.Count > 0)
					{
						packet = _sendQueue.Dequeue();
					}
					else
					{
						_sendSignal.Reset();
					}
				}
				if (packet == null)
				{
					if (_detaching)
					{
						return;
					}
					_sendSignal.Wait(1000);
					continue;
				}
				_stream.Write(packet, 0, packet.Length);
				_stream.Flush();
			}
		}
		catch (Exception ex)
		{
			if (!_detaching)
			{
				Close(ex.Message);
			}
		}
	}

	private void EnsureCapacity(int needed)
	{
		if (_buffer.Length < needed)
		{
			int size = _buffer.Length;
			while (size < needed)
			{
				size *= 2;
			}
			Array.Resize(ref _buffer, size);
		}
	}

	private void ReadLoop()
	{
		try
		{
			ParseFrames();
			while (!_closed && !_detaching)
			{
				// Poll instead of blocking in Read so a hand-over can stop this thread without closing the socket.
				if (_tcp.Available == 0)
				{
					if (!_tcp.Client.Poll(50000, SelectMode.SelectRead))
					{
						continue;
					}
					if (_tcp.Available == 0)
					{
						break; // readable with nothing to read = closed by the server
					}
				}
				if (_filled == _buffer.Length)
				{
					EnsureCapacity(_buffer.Length * 2);
				}
				int n = _stream.Read(_buffer, _filled, Math.Min(_buffer.Length - _filled, Math.Max(1, _tcp.Available)));
				if (n <= 0)
				{
					break;
				}
				_filled += n;
				ParseFrames();
			}
			if (!_detaching)
			{
				Close(null);
			}
		}
		catch (Exception ex)
		{
			if (!_detaching)
			{
				Close(ex.Message);
			}
		}
	}

	private void ParseFrames()
	{
		int offset = 0;
		while (_filled - offset >= 2)
		{
			int len = _buffer[offset] | (_buffer[offset + 1] << 8);
			if (len < 3)
			{
				throw new InvalidDataException("Invalid packet length " + len);
			}
			if (_filled - offset < len)
			{
				break;
			}
			byte type = _buffer[offset + 2];
			byte[] payload = new byte[len - 3];
			Buffer.BlockCopy(_buffer, offset + 3, payload, 0, len - 3);
			Action<byte, byte[]> handler = OnPacket;
			if (handler != null)
			{
				try
				{
					handler(type, payload);
				}
				catch
				{
				}
			}
			offset += len;
		}
		if (offset > 0)
		{
			Buffer.BlockCopy(_buffer, offset, _buffer, 0, _filled - offset);
			_filled -= offset;
		}
	}

	public void Close(string reason = null)
	{
		if (_detached)
		{
			return;
		}
		if (Interlocked.Exchange(ref _closeReported, 1) != 0)
		{
			_closed = true;
			return;
		}
		_closed = true;
		_sendSignal.Set();
		try
		{
			_tcp?.Close();
		}
		catch
		{
		}
		Action<string> handler = OnClosed;
		if (handler != null)
		{
			handler(reason);
		}
	}
}
