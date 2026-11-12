#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Net.Pipe;

/// <summary>A message delivered to a subscriber of <see cref="PipeNet.OnMessage"/>. Raised on the main thread.</summary>
public readonly struct PipeInbound
{
	/// <summary>The player slot the message came from (as the sender reported it).</summary>
	public readonly int FromPlayer;

	/// <summary>The player slot it was addressed to, or <see cref="PipeNet.Everyone"/> for a broadcast.</summary>
	public readonly int ToPlayer;

	/// <summary>A number the sender and receiver agree on so unrelated features can share the pipe. Pick your own.</summary>
	public readonly byte Channel;

	public readonly byte[] Data;

	public bool Broadcast => ToPlayer == PipeNet.Everyone;

	public PipeInbound(int from, int to, byte channel, byte[] data)
	{
		FromPlayer = from;
		ToPlayer = to;
		Channel = channel;
		Data = data;
	}
}

/// <summary>
/// The thing to build on. A small, reliable message bus between IsaClient players that rides on map pings (invisible to
/// IsaClient users, shown as a brief marker only to vanilla clients, which never get the data). Address a single player
/// or everyone, tag messages with a channel of your own, and subscribe to <see cref="OnMessage"/>.
///
/// Example: <c>PipeNet.SendTo(playerIndex, channel: 10, myBytes);</c> and, elsewhere,
/// <c>PipeNet.OnMessage += m =&gt; { if (m.Channel == 10) Handle(m.FromPlayer, m.Data); };</c>
///
/// It is best-effort-reliable: a directed message retransmits until the peer acknowledges it or ~20 s pass; a broadcast
/// lingers a couple of seconds. Keep messages small (a few hundred bytes); the ping carrier is a trickle, meant for game
/// moves and signals, not files. For bulk data, run <see cref="PipeMessage"/> through a <see cref="PipeSession"/> on a
/// fatter carrier.
/// </summary>
public static class PipeNet
{
	/// <summary>Address for <see cref="Broadcast"/>: every IsaClient player on the server.</summary>
	public const int Everyone = 255;

	private const byte EnvelopeVersion = 1;

	[Setting("PipeEnabled")]
	public static bool Enabled = true;

	/// <summary>When on, the engine's log lines are shown to you in the chat (for trying the pipe out).</summary>
	[Setting("PipeTrace")]
	public static bool Trace = false;

	/// <summary>Raised on the main thread for each message meant for you (or broadcast). Never throws into the engine.</summary>
	public static event Action<PipeInbound>? OnMessage;

	private static PingCarrier? _carrier;

	private static PipeSession? _session;

	private static int _sessionPlayer = -1;

	private static Task? _sendWorker;

	private static readonly BlockingCollection<Pending> _sendQueue = new BlockingCollection<Pending>(new ConcurrentQueue<Pending>());

	private static readonly ConcurrentQueue<PipeInbound> _inbound = new ConcurrentQueue<PipeInbound>();

	private readonly struct Pending
	{
		public readonly byte[] Blob;

		public readonly int Milliseconds;

		/// <summary>Told (off the main thread) whether the transfer went through, when someone wants to know.</summary>
		public readonly Action<bool>? Done;

		public Pending(byte[] blob, int ms, Action<bool>? done = null)
		{
			Blob = blob;
			Milliseconds = ms;
			Done = done;
		}
	}

	/// <summary>Called once per tick on the main thread (from Main). Keeps the session alive and delivers messages.</summary>
	public static void Update()
	{
		try
		{
			// Off still listens (it never sends anything, not even acknowledgements: see PingCarrier.SendFrame).
			if (Main.netMode != 1)
			{
				Teardown();
				return;
			}
			int me = Main.myPlayer;
			if (me < 0 || me >= 255)
			{
				return;
			}
			if (_session == null || _sessionPlayer != me)
			{
				Rebuild(me);
			}
			_carrier?.Pump();
			while (_inbound.TryDequeue(out PipeInbound msg))
			{
				try
				{
					OnMessage?.Invoke(msg);
				}
				catch (Exception ex)
				{
					ClientLog.LogException(ex);
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	/// <summary>Sends <paramref name="data"/> to one player on a channel of your choosing.</summary>
	public static void SendTo(int player, byte channel, byte[] data)
	{
		Enqueue(player, channel, data, 20000);
	}

	/// <summary>Sends <paramref name="data"/> to every IsaClient player on a channel of your choosing.</summary>
	public static void Broadcast(byte channel, byte[] data)
	{
		Enqueue(Everyone, channel, data, 2500);
	}

	/// <summary>
	/// Like <see cref="SendTo"/>/<see cref="Broadcast"/> (player <see cref="Everyone"/>), for bigger transfers sent in
	/// pieces: <paramref name="done"/> hears (off the main thread) whether this piece got through, to show progress.
	/// </summary>
	public static void Send(int player, byte channel, byte[] data, int milliseconds, Action<bool> done)
	{
		if (!Enqueue(player, channel, data, milliseconds, done))
		{
			done?.Invoke(false);
		}
	}

	/// <summary>True when the pipe is connected: it hears others (with it off too, it only listens).</summary>
	public static bool Ready => _session != null && Main.netMode == 1;

	/// <summary>True when messages can go out: connected and on.</summary>
	public static bool CanSend => Enabled && Ready;

	private static bool Enqueue(int toPlayer, byte channel, byte[] data, int ms, Action<bool>? done = null)
	{
		if (!Enabled || Main.netMode != 1)
		{
			return false;
		}
		int from = Main.myPlayer;
		if (from < 0 || from >= 255)
		{
			return false;
		}
		data = data ?? Array.Empty<byte>();
		byte[] blob = new byte[4 + data.Length];
		blob[0] = EnvelopeVersion;
		blob[1] = (byte)from;
		blob[2] = (byte)toPlayer;
		blob[3] = channel;
		data.CopyTo(blob, 4);
		try
		{
			_sendQueue.Add(new Pending(blob, ms, done));
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void Rebuild(int me)
	{
		Teardown();
		_carrier = new PingCarrier();
		PingCarrier.Active = _carrier;
		PipeOptions opt = new PipeOptions
		{
			// A stable tag per player keeps transfers from different peers apart on the shared carrier.
			SenderTag = (byte)(me + 1),
		};
		_session = new PipeSession(_carrier, opt);
		_session.OnBlobReceived += OnBlob;
		_session.OnLog += OnEngineLog;
		_sessionPlayer = me;
		if (_sendWorker == null)
		{
			_sendWorker = Task.Run((Func<Task>)SendWorker);
		}
	}

	private static void Teardown()
	{
		if (_session != null)
		{
			_session.OnBlobReceived -= OnBlob;
			_session.OnLog -= OnEngineLog;
			_session.Dispose();
			_session = null;
		}
		if (PingCarrier.Active == _carrier)
		{
			PingCarrier.Active = null;
		}
		_carrier = null;
		_sessionPlayer = -1;
	}

	private static async Task SendWorker()
	{
		foreach (Pending p in _sendQueue.GetConsumingEnumerable())
		{
			PipeSession? s = _session;
			if (s == null)
			{
				Tell(p, false);
				continue;
			}
			bool ok = false;
			try
			{
				using (CancellationTokenSource cts = new CancellationTokenSource(p.Milliseconds))
				{
					await s.SendAsync(p.Blob, cts.Token, broadcast: p.Blob[2] == Everyone).ConfigureAwait(false);
				}
				ok = true;
			}
			catch (OperationCanceledException)
			{
			}
			catch (TimeoutException)
			{
			}
			catch (Exception ex)
			{
				try
				{
					ClientLog.LogException(ex);
				}
				catch
				{
				}
			}
			Tell(p, ok);
		}
	}

	private static void Tell(Pending p, bool ok)
	{
		try
		{
			p.Done?.Invoke(ok);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static void OnBlob(byte[] blob)
	{
		if (blob.Length < 4 || blob[0] != EnvelopeVersion)
		{
			return;
		}
		int from = blob[1];
		int to = blob[2];
		byte channel = blob[3];
		int me = Main.myPlayer;
		if (to != Everyone && to != me)
		{
			// Addressed to someone else (the carrier is a broadcast, so we still see it): not ours.
			return;
		}
		if (from == me)
		{
			// Our own broadcast echoed back by the server: ignore.
			return;
		}
		byte[] data = new byte[blob.Length - 4];
		Array.Copy(blob, 4, data, 0, data.Length);
		_inbound.Enqueue(new PipeInbound(from, to, channel, data));
	}

	private static void OnEngineLog(string line)
	{
		if (Trace)
		{
			try
			{
				ClientChat.Info("[pipe] " + line);
			}
			catch
			{
			}
		}
	}
}
