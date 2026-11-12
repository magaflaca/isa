#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.NetModules;
using Terraria.Net;

namespace IsaClient.Net.Pipe;

/// <summary>
/// Carries the transfer engine's frames inside Terraria's map pings (NetModule 2). The dedicated server rebroadcasts a
/// ping to every other client, so it doubles as a client-to-client link without touching the server. A ping holds a
/// Vector2 = 8 bytes; we reserve one of them (the high byte of X) as a magic (<see cref="Magic"/>) that a real map ping
/// — always a finite, positive, in-world position — can never carry, so <see cref="OnClientPing"/> tells our pings from
/// real ones with no false positives and hides the minimap marker for ours. The remaining 7 bytes carry one of four
/// fragments; four pings rebuild an 18-byte engine frame, checked with a CRC-8.
///
/// Sends are queued and flushed from the main thread (<see cref="Pump"/>), because Terraria's socket is not meant to be
/// written from the engine's background pump. Plain byte arrays only (no Span), so the module needs no extra assemblies.
/// </summary>
public sealed class PingCarrier : ITerraChannel
{
	/// <summary>High byte of a carrier ping's X. A real ping's X is a positive world coordinate, whose high byte is never this.</summary>
	public const byte Magic = 0xB7;

	private const int PerPing = 6;

	private const int Frags = 4;

	private const int FrameBytes = 3 * PerPing; // 18

	private static readonly TimeSpan PartialTtl = TimeSpan.FromMilliseconds(1500);

	private readonly ConcurrentQueue<Vector2> _outbox = new ConcurrentQueue<Vector2>();

	private readonly Dictionary<int, Partial> _inbox = new Dictionary<int, Partial>();

	private readonly object _rxSync = new object();

	private int _sfid;

	private readonly object _txSync = new object();

	/// <summary>The one carrier the ping hook feeds. Set while a <see cref="PipeNet"/> owns this carrier.</summary>
	internal static PingCarrier? Active;

	public int FrameSize => FrameBytes;

	public string Name => "ping";

	public event Action<byte[]>? OnFrame;

	public int FlushCapPerTick = 24;

	private sealed class Partial
	{
		public readonly byte[]?[] Parts = new byte[Frags][];

		public DateTime Touch = DateTime.UtcNow;
	}

	public void SendFrame(byte[] frame)
	{
		if (frame.Length > FrameBytes)
		{
			throw new ArgumentException($"frame too large ({frame.Length} > {FrameBytes})", nameof(frame));
		}
		if (!PipeNet.Enabled)
		{
			// Off: nothing goes out (the session still listens; acknowledgements are dropped here too).
			return;
		}
		byte[] full = new byte[FrameBytes];
		Array.Copy(frame, 0, full, 0, frame.Length);
		byte crc = Bits.Crc8(full, 0, FrameBytes);
		// Serialize the whole 4-ping burst so two threads never interleave fragments under the same frame id.
		lock (_txSync)
		{
			int sfid = _sfid;
			_sfid = (_sfid + 1) & 0x3F;
			for (int i = 0; i < Frags; i++)
			{
				byte[] seven = new byte[7];
				seven[0] = (byte)(((sfid & 0x3F) << 2) | (i & 0x03));
				if (i < 3)
				{
					Array.Copy(full, i * PerPing, seven, 1, PerPing);
				}
				else
				{
					seven[1] = crc;
				}
				_outbox.Enqueue(Encode(seven));
			}
		}
	}

	/// <summary>Called on the main thread each tick: sends the queued pings to the server.</summary>
	public void Pump()
	{
		if (Main.netMode != 1 || Netplay.Connection == null || Netplay.Connection.Socket == null)
		{
			// Not connected: drop anything queued so it does not burst out on reconnect.
			while (_outbox.TryDequeue(out Vector2 _))
			{
			}
			return;
		}
		int sent = 0;
		while (sent < FlushCapPerTick && _outbox.TryDequeue(out Vector2 pos))
		{
			try
			{
				NetManager.Instance.SendToServer(NetPingModule.Serialize(pos));
			}
			catch
			{
			}
			sent++;
		}
	}

	/// <summary>
	/// The ping hook calls this for every incoming ping. True means it was carrier data (fed to the engine, marker hidden);
	/// false means a real map ping the game should show as usual.
	/// </summary>
	public static bool OnClientPing(Vector2 position)
	{
		byte[]? seven = Decode(position);
		if (seven == null)
		{
			return false;
		}
		Active?.Feed(seven);
		return true;
	}

	private void Feed(byte[] seven)
	{
		byte h = seven[0];
		int sfid = (h >> 2) & 0x3F;
		int frag = h & 0x03;
		DateTime now = DateTime.UtcNow;
		byte[]? frame = null;
		lock (_rxSync)
		{
			if (_inbox.Count > 0)
			{
				List<int> dead = new List<int>();
				foreach (KeyValuePair<int, Partial> kv in _inbox)
				{
					if (now - kv.Value.Touch > PartialTtl)
					{
						dead.Add(kv.Key);
					}
				}
				foreach (int k in dead)
				{
					_inbox.Remove(k);
				}
			}
			if (!_inbox.TryGetValue(sfid, out Partial part))
			{
				part = new Partial();
				_inbox[sfid] = part;
			}
			byte[] body = new byte[PerPing];
			Array.Copy(seven, 1, body, 0, PerPing);
			part.Parts[frag] = body;
			part.Touch = now;
			if (part.Parts[0] != null && part.Parts[1] != null && part.Parts[2] != null && part.Parts[3] != null)
			{
				byte[] f = new byte[FrameBytes];
				for (int i = 0; i < 3; i++)
				{
					Array.Copy(part.Parts[i]!, 0, f, i * PerPing, PerPing);
				}
				_inbox.Remove(sfid);
				if (Bits.Crc8(f, 0, FrameBytes) == part.Parts[3]![0])
				{
					frame = f;
				}
			}
		}
		if (frame != null)
		{
			OnFrame?.Invoke(frame);
		}
	}

	/// <summary>Packs 7 bytes into a Vector2 with the magic in X's high byte.</summary>
	private static Vector2 Encode(byte[] seven)
	{
		byte[] xb = new byte[4] { seven[0], seven[1], seven[2], Magic };
		byte[] yb = new byte[4] { seven[3], seven[4], seven[5], seven[6] };
		return new Vector2(BitConverter.ToSingle(xb, 0), BitConverter.ToSingle(yb, 0));
	}

	/// <summary>The 7 bytes a carrier ping holds, or null if this ping is a real map ping.</summary>
	private static byte[]? Decode(Vector2 position)
	{
		byte[] xb = BitConverter.GetBytes(position.X);
		if (xb[3] != Magic)
		{
			return null;
		}
		byte[] yb = BitConverter.GetBytes(position.Y);
		return new byte[7] { xb[0], xb[1], xb[2], yb[0], yb[1], yb[2], yb[3] };
	}
}
