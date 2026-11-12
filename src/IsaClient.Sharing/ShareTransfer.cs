using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using IsaClient.Core;
using IsaClient.Net.Pipe;
using Terraria;

namespace IsaClient.Sharing;

/// <summary>
/// Bigger things linked in chat (a character, a schematic) going to IsaClient players through the pipe, in pieces.
/// The sender pushes every piece to everyone right after the chat line goes out; a receiver that misses some asks the
/// sender for those (directed, so they're retried), and one that missed the whole thing (it joined later) asks
/// everyone for it by its code. Each piece says what it belongs to, so no handshake is needed. The code is the hash
/// of the whole payload, checked when the last piece lands.
/// </summary>
public static class ShareTransfer
{
	public const byte Channel = 76;

	private const byte Piece = 1;

	private const byte Need = 2;

	private const byte Ask = 3;

	/// <summary>Bytes of payload per piece (the pipe carries ~0.9 KB/s, so a piece takes about half a second).</summary>
	private const int PieceSize = 384;

	/// <summary>The biggest payload sent this way (bigger schematics only go through archive.org).</summary>
	public const int MaxBytes = 60 * 1024;

	private const int HeaderSize = 14;

	/// <summary>One transfer, sending or receiving: what the chat card and the panel show.</summary>
	public sealed class Transfer
	{
		public string Code;

		public ShareKind Kind;

		public bool Sending;

		/// <summary>The whole payload (the sender's, or once every piece arrived).</summary>
		public byte[] Bytes;

		public int Pieces;

		public int Total;

		/// <summary>Pieces that got through (sending) or arrived (receiving).</summary>
		public int Done;

		/// <summary>Pieces whose push nobody took (sending): with no IsaClient player around they all fail.</summary>
		public int Lost;

		public bool Pushing;

		public int From = -1;

		public DateTime Started = DateTime.Now;

		public DateTime LastPiece = DateTime.Now;

		public DateTime NextNeed = DateTime.MinValue;

		public bool Failed;

		/// <summary>With the pipe off it goes through archive.org instead: the upload (sending) or the download.</summary>
		public ArchiveStore.Job Archive;

		/// <summary>With the pipe off: its Imgur picture going up (sending) or coming down (receiving).</summary>
		public ArchiveStore.Job Image;

		internal byte[][] Received;

		public bool Complete => Bytes != null;

		public float Fraction => (Pieces <= 0) ? (Complete ? 1f : 0f) : MathHelperClamp((float)Done / Pieces);

		private static float MathHelperClamp(float v) => (v < 0f) ? 0f : ((v > 1f) ? 1f : v);
	}

	private static readonly Dictionary<string, Transfer> _transfers = new Dictionary<string, Transfer>();

	private static readonly Dictionary<string, DateTime> _asked = new Dictionary<string, DateTime>();

	/// <summary>When a card was first seen without its payload (its sender is probably pushing it right now).</summary>
	private static readonly Dictionary<string, DateTime> _firstSeen = new Dictionary<string, DateTime>();

	private static readonly object _lock = new object();

	private static bool _hooked;

	/// <summary>A payload arrived whole (main thread): its code, kind and bytes.</summary>
	public static event Action<string, ShareKind, byte[]> Completed;

	public static int Version;

	public static string CodeOf(byte[] bytes)
	{
		uint h = 2166136261u;
		foreach (byte b in bytes)
		{
			h = (h ^ b) * 16777619u;
		}
		return h.ToString("x8");
	}

	public static Transfer Get(string code)
	{
		lock (_lock)
		{
			_transfers.TryGetValue(code ?? "", out Transfer t);
			return t;
		}
	}

	public static List<Transfer> All()
	{
		lock (_lock)
		{
			return _transfers.Values.ToList();
		}
	}

	/// <summary>A payload of yours, ready to go when its chat line is sent. Returns its code.</summary>
	public static string Register(ShareKind kind, byte[] bytes)
	{
		string code = CodeOf(bytes);
		lock (_lock)
		{
			if (!_transfers.TryGetValue(code, out Transfer t) || !t.Complete)
			{
				_transfers[code] = new Transfer
				{
					Code = code,
					Kind = kind,
					Sending = true,
					Bytes = bytes,
					Total = bytes.Length,
					Pieces = PiecesOf(bytes.Length)
				};
			}
		}
		Version++;
		return code;
	}

	private static int PiecesOf(int length) => Math.Max(1, (length + PieceSize - 1) / PieceSize);

	/// <summary>Something on its way another way (archive.org): its transfer, to show and to finish with <see cref="Deliver"/>.</summary>
	public static Transfer Expect(string code, ShareKind kind)
	{
		lock (_lock)
		{
			if (!_transfers.TryGetValue(code, out Transfer t))
			{
				t = new Transfer { Code = code, Kind = kind };
				_transfers[code] = t;
			}
			return t;
		}
	}

	/// <summary>A payload that came whole another way (main thread): done, as if its last piece landed.</summary>
	public static void Deliver(string code, ShareKind kind, byte[] bytes)
	{
		Transfer t = Expect(code, kind);
		if (t.Complete || CodeOf(bytes) != code)
		{
			return;
		}
		t.Bytes = bytes;
		t.Total = bytes.Length;
		t.Received = null;
		t.Failed = false;
		Version++;
		Completed?.Invoke(code, kind, bytes);
	}

	/// <summary>Its chat line went out: every piece to every IsaClient player here.</summary>
	public static void Push(string code)
	{
		Transfer t = Get(code);
		if (t == null || !t.Complete || !PipeNet.CanSend || t.Pushing)
		{
			return;
		}
		t.Sending = true;
		t.Pushing = true;
		t.Done = 0;
		t.Lost = 0;
		t.Started = DateTime.Now;
		// Even when no IsaClient player shows here: someone may be listening with the pipe off (it goes blind).
		for (int i = 0; i < t.Pieces; i++)
		{
			PipeNet.Send(PipeNet.Everyone, Channel, PieceMessage(t, i), 6000, ok =>
			{
				if (ok)
				{
					Interlocked.Increment(ref t.Done);
				}
				else
				{
					Interlocked.Increment(ref t.Lost);
				}
				if (t.Done + t.Lost >= t.Pieces)
				{
					t.Pushing = false;
				}
				Version++;
			});
		}
		Version++;
	}

	private static byte[] PieceMessage(Transfer t, int index)
	{
		int offset = index * PieceSize;
		int length = Math.Min(PieceSize, t.Bytes.Length - offset);
		byte[] data = new byte[HeaderSize + length];
		data[0] = Piece;
		BitConverter.GetBytes(Convert.ToUInt32(t.Code, 16)).CopyTo(data, 1);
		data[5] = (byte)t.Kind;
		BitConverter.GetBytes((ushort)index).CopyTo(data, 6);
		BitConverter.GetBytes((ushort)t.Pieces).CopyTo(data, 8);
		BitConverter.GetBytes(t.Bytes.Length).CopyTo(data, 10);
		Array.Copy(t.Bytes, offset, data, HeaderSize, length);
		return data;
	}

	/// <summary>A chat card whose payload we don't have: ask everyone (now and then) and get ready to collect it.</summary>
	public static void Want(string code, ShareKind kind)
	{
		Transfer t = Get(code);
		if (t != null && (t.Complete || (DateTime.Now - t.LastPiece).TotalSeconds < 8))
		{
			return;
		}
		lock (_lock)
		{
			if (!_firstSeen.TryGetValue(code, out DateTime seen))
			{
				_firstSeen[code] = DateTime.Now;
				return;
			}
			// The line and its pieces leave together: ask only when nothing came for a while (joined late, missed it).
			if ((DateTime.Now - seen).TotalSeconds < 6 || (_asked.TryGetValue(code, out DateTime at) && (DateTime.Now - at).TotalSeconds < 12))
			{
				return;
			}
			_asked[code] = DateTime.Now;
		}
		if (!PipeNet.CanSend)
		{
			return;
		}
		byte[] data = new byte[6];
		data[0] = Ask;
		BitConverter.GetBytes(Convert.ToUInt32(code, 16)).CopyTo(data, 1);
		data[5] = (byte)kind;
		PipeNet.Broadcast(Channel, data);
	}

	public static void Update()
	{
		if (!_hooked)
		{
			_hooked = true;
			PipeNet.OnMessage += OnMessage;
		}
		if (!PipeNet.CanSend)
		{
			return;
		}
		// Pieces that didn't come: ask whoever sent the others for them.
		foreach (Transfer t in All())
		{
			if (t.Sending || t.Complete || t.Received == null || t.From < 0 || DateTime.Now < t.NextNeed || (DateTime.Now - t.LastPiece).TotalSeconds < 3)
			{
				continue;
			}
			if (!Main.player[t.From].active)
			{
				t.Failed = true;
				continue;
			}
			List<int> missing = new List<int>();
			for (int i = 0; i < t.Received.Length && missing.Count < 60; i++)
			{
				if (t.Received[i] == null)
				{
					missing.Add(i);
				}
			}
			byte[] data = new byte[6 + missing.Count * 2];
			data[0] = Need;
			BitConverter.GetBytes(Convert.ToUInt32(t.Code, 16)).CopyTo(data, 1);
			data[5] = (byte)missing.Count;
			for (int i = 0; i < missing.Count; i++)
			{
				BitConverter.GetBytes((ushort)missing[i]).CopyTo(data, 6 + i * 2);
			}
			PipeNet.SendTo(t.From, Channel, data);
			t.NextNeed = DateTime.Now.AddSeconds(Math.Max(4, missing.Count * PieceSize / 700 + 3));
		}
	}

	private static void OnMessage(PipeInbound m)
	{
		if (m.Channel != Channel || m.Data == null || m.Data.Length < 6)
		{
			return;
		}
		try
		{
			string code = BitConverter.ToUInt32(m.Data, 1).ToString("x8");
			switch (m.Data[0])
			{
			case Piece:
				OnPiece(m, code);
				break;
			case Need:
				OnNeed(m, code);
				break;

			case Ask:
			{
				// Someone saw the card and has nothing: everything, to them only.
				Transfer t = Get(code);
				if (t != null && t.Complete)
				{
					for (int i = 0; i < t.Pieces; i++)
					{
						PipeNet.SendTo(m.FromPlayer, Channel, PieceMessage(t, i));
					}
				}
				break;
			}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static void OnNeed(PipeInbound m, string code)
	{
		Transfer t = Get(code);
		if (t == null || !t.Complete)
		{
			return;
		}
		int count = m.Data[5];
		for (int i = 0; i < count && 6 + i * 2 + 1 < m.Data.Length; i++)
		{
			int index = BitConverter.ToUInt16(m.Data, 6 + i * 2);
			if (index < t.Pieces)
			{
				PipeNet.SendTo(m.FromPlayer, Channel, PieceMessage(t, index));
			}
		}
	}

	private static void OnPiece(PipeInbound m, string code)
	{
		if (m.Data.Length < HeaderSize)
		{
			return;
		}
		ShareKind kind = (ShareKind)m.Data[5];
		int index = BitConverter.ToUInt16(m.Data, 6);
		int pieces = BitConverter.ToUInt16(m.Data, 8);
		int total = BitConverter.ToInt32(m.Data, 10);
		if (pieces <= 0 || index >= pieces || total <= 0 || total > MaxBytes || pieces != PiecesOf(total))
		{
			return;
		}
		Transfer t;
		lock (_lock)
		{
			if (!_transfers.TryGetValue(code, out t))
			{
				t = new Transfer { Code = code, Kind = kind, Pieces = pieces, Total = total };
				_transfers[code] = t;
			}
		}
		if (t.Complete || t.Sending)
		{
			return;
		}
		if (t.Received == null || t.Received.Length != pieces)
		{
			t.Received = new byte[pieces][];
			t.Pieces = pieces;
			t.Total = total;
			t.Done = 0;
		}
		t.From = m.FromPlayer;
		t.LastPiece = DateTime.Now;
		t.Failed = false;
		if (t.Received[index] == null)
		{
			byte[] part = new byte[m.Data.Length - HeaderSize];
			Array.Copy(m.Data, HeaderSize, part, 0, part.Length);
			t.Received[index] = part;
			t.Done++;
		}
		Version++;
		if (t.Done < t.Pieces)
		{
			return;
		}
		byte[] whole = new byte[total];
		int at = 0;
		foreach (byte[] part in t.Received)
		{
			int n = Math.Min(part.Length, total - at);
			Array.Copy(part, 0, whole, at, n);
			at += n;
		}
		if (at != total || CodeOf(whole) != code)
		{
			// A piece from something else with the same code: start over.
			t.Received = null;
			t.Done = 0;
			t.Failed = true;
			return;
		}
		t.Bytes = whole;
		t.Received = null;
		Completed?.Invoke(code, kind, whole);
	}
}
