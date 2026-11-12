#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IsaClient.Net.Pipe;

/// <summary>Knobs for one <see cref="PipeSession"/>.</summary>
public sealed class PipeOptions
{
	/// <summary>Frames per second the sender is allowed to put on the carrier (each ping frame is 4 pings).</summary>
	public int SendRatePerSecond = 40;

	public int WindowChunks = 32;

	public TimeSpan AckInterval = TimeSpan.FromMilliseconds(200);

	public TimeSpan AckMinGap = TimeSpan.FromMilliseconds(20);

	public TimeSpan ControlResend = TimeSpan.FromMilliseconds(500);

	public TimeSpan RetransmitTimeout = TimeSpan.FromMilliseconds(500);

	public TimeSpan Timeout = TimeSpan.FromSeconds(20);

	/// <summary>
	/// A broadcast whose Open nobody acknowledged after this many tries is sent anyway, without acknowledgements:
	/// players listening with the pipe off (they never answer) still get it.
	/// </summary>
	public int BlindAfterOpens = 3;

	/// <summary>How many times each chunk of an unacknowledged broadcast is sent (nothing asks again for lost ones).</summary>
	public int BlindPasses = 2;

	/// <summary>One byte that tags this sender's frames so a receiver keeps transfers from different peers apart.</summary>
	public byte SenderTag = (byte)PipeRandom.Next(1, 256);
}

/// <summary>Shared thread-safe random (net48 has no Random.Shared).</summary>
internal static class PipeRandom
{
	private static readonly Random _rng = new Random();

	public static int Next(int min, int max)
	{
		lock (_rng)
		{
			return _rng.Next(min, max);
		}
	}
}

/// <summary>How far along a transfer is.</summary>
public struct PipeProgress
{
	public long Done;

	public long Total;

	public bool Sending;

	public PipeProgress(long done, long total, bool sending)
	{
		Done = done;
		Total = total;
		Sending = sending;
	}

	public double Fraction => (Total <= 0) ? 1.0 : ((double)Done / Total);
}

/// <summary>
/// The reliable transfer engine: a sliding-window ARQ that carries one blob at a time over any <see cref="ITerraChannel"/>.
/// Open(len, chunks, crc) -> Data(seq + chunk) -> Ack(base + 32-bit selective bitmap) -> Fin -> Done(crc ok?). It survives
/// loss and reordering; the carrier only has to deliver frames mostly intact. Carrier-agnostic: this file never mentions
/// pings or particles, and uses only plain byte arrays (no Span), so it needs nothing beyond the framework.
/// </summary>
public sealed class PipeSession : IDisposable
{
	private readonly ITerraChannel _ch;

	private readonly int _dataBytes;

	private readonly PipeOptions _opt;

	private readonly object _sync = new object();

	private readonly CancellationTokenSource _cts = new CancellationTokenSource();

	private readonly Task _pump;

	private Outgoing? _tx;

	private readonly Dictionary<int, Incoming> _rx = new Dictionary<int, Incoming>();

	public event Action<byte[]>? OnBlobReceived;

	public event Action<PipeProgress>? OnProgress;

	public event Action<string>? OnLog;

	public PipeSession(ITerraChannel channel, PipeOptions? options = null)
	{
		_ch = channel;
		_dataBytes = Math.Max(1, _ch.FrameSize - (PipeFrame.HeaderSize + 2));
		_opt = options ?? new PipeOptions();
		_ch.OnFrame += OnFrame;
		_pump = Task.Run((Func<Task>)PumpLoop);
	}

	/// <param name="broadcast">For everyone: when nobody acknowledges it, it's sent blind (see <see cref="PipeOptions.BlindAfterOpens"/>).</param>
	public async Task SendAsync(byte[] blob, CancellationToken ct = default, bool broadcast = false)
	{
		Outgoing tx = new Outgoing(blob, _opt, _dataBytes) { Broadcast = broadcast };
		lock (_sync)
		{
			if (_tx != null && !_tx.Done.Task.IsCompleted)
			{
				throw new InvalidOperationException("a send is already in progress");
			}
			_tx = tx;
		}
		Log($"send start: {blob.Length} B in {tx.ChunkCount} chunks, stream {tx.StreamId}, tag {tx.SenderTag}");
		using (ct.Register(() => tx.Done.TrySetCanceled()))
		using (CancellationTokenSource timeout = new CancellationTokenSource(_opt.Timeout))
		using (timeout.Token.Register(() => tx.Done.TrySetException(new TimeoutException("transfer timed out"))))
		{
			await tx.Done.Task.ConfigureAwait(false);
		}
		Log("send complete");
	}

	public async Task<byte[]> ReceiveAsync(CancellationToken ct = default)
	{
		TaskCompletionSource<byte[]> tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
		void H(byte[] b) => tcs.TrySetResult(b);
		OnBlobReceived += H;
		try
		{
			using (ct.Register(() => tcs.TrySetCanceled()))
			{
				return await tcs.Task.ConfigureAwait(false);
			}
		}
		finally
		{
			OnBlobReceived -= H;
		}
	}

	private void OnFrame(byte[] f)
	{
		if (f.Length < PipeFrame.HeaderSize)
		{
			return;
		}
		FrameType type = (FrameType)f[PipeFrame.TypeOffset];
		byte senderTag = f[PipeFrame.SenderOffset];
		byte streamId = f[PipeFrame.StreamOffset];
		switch (type)
		{
		case FrameType.Ack:
			HandleAck(senderTag, streamId, f);
			break;
		case FrameType.Done:
			HandleDone(senderTag, streamId, f);
			break;
		case FrameType.Open:
			HandleOpen(senderTag, streamId, f);
			break;
		case FrameType.Data:
			HandleData(senderTag, streamId, f);
			break;
		case FrameType.Fin:
			HandleFin(senderTag, streamId);
			break;
		case FrameType.Abort:
			HandleAbort(senderTag, streamId);
			break;
		}
	}

	private void HandleAck(byte senderTag, byte streamId, byte[] f)
	{
		lock (_sync)
		{
			if (_tx == null || _tx.SenderTag != senderTag || _tx.StreamId != streamId || f.Length < 3 + 6)
			{
				return;
			}
			int baseSeq = Bits.ReadU16(f, 3);
			uint bitmap = Bits.ReadU32(f, 5);
			_tx.ApplyAck(baseSeq, bitmap);
		}
	}

	private void HandleDone(byte senderTag, byte streamId, byte[] f)
	{
		bool ok = f.Length >= 4 && f[3] == 1;
		lock (_sync)
		{
			if (_tx == null || _tx.SenderTag != senderTag || _tx.StreamId != streamId)
			{
				return;
			}
			if (ok)
			{
				_tx.Done.TrySetResult(true);
			}
			else
			{
				_tx.Done.TrySetException(new IOException("receiver reported CRC mismatch"));
			}
		}
	}

	private void HandleOpen(byte senderTag, byte streamId, byte[] f)
	{
		if (f.Length < 3 + 8)
		{
			return;
		}
		long totalLen = Bits.ReadU32(f, 3);
		int chunks = Bits.ReadU16(f, 7);
		ushort crc = Bits.ReadU16(f, 9);
		// Guard: a bogus Open must not make us allocate a giant buffer.
		if (totalLen < 0 || totalLen > PipeFrame.MaxBlobBytes || chunks <= 0)
		{
			return;
		}
		int key = (senderTag << 8) | streamId;
		lock (_sync)
		{
			if (_rx.TryGetValue(key, out Incoming ex) && ex.ChunkCount == chunks && ex.Crc == crc)
			{
				return;
			}
			_rx[key] = new Incoming(senderTag, streamId, totalLen, chunks, crc, _dataBytes);
			Log($"recv open: {totalLen} B in {chunks} chunks from tag {senderTag}/{streamId}");
		}
	}

	private void HandleData(byte senderTag, byte streamId, byte[] f)
	{
		if (f.Length < 3 + 2)
		{
			return;
		}
		int seq = Bits.ReadU16(f, 3);
		int dataOff = 5;
		int dataLen = f.Length - 5;
		int key = (senderTag << 8) | streamId;
		Incoming? inc;
		lock (_sync)
		{
			_rx.TryGetValue(key, out inc);
		}
		if (inc == null)
		{
			return;
		}
		bool complete;
		lock (inc.Sync)
		{
			complete = inc.Store(seq, f, dataOff, dataLen);
		}
		if (inc.Received % 64 == 0 || complete)
		{
			OnProgress?.Invoke(new PipeProgress(inc.Received, inc.ChunkCount, false));
		}
		if (complete)
		{
			FinishIncoming(key, inc);
			return;
		}
		DateTime now = DateTime.UtcNow;
		if (now >= inc.NextFastAckAt)
		{
			SendAck(inc);
			inc.NextFastAckAt = now + _opt.AckMinGap;
			inc.NextAckAt = now + _opt.AckInterval;
		}
	}

	private void HandleFin(byte senderTag, byte streamId)
	{
		int key = (senderTag << 8) | streamId;
		Incoming? inc;
		lock (_sync)
		{
			_rx.TryGetValue(key, out inc);
		}
		if (inc == null)
		{
			return;
		}
		inc.FinSeen = true;
		if (inc.Completed)
		{
			SendDone(inc, inc.CrcOk);
			inc.DropAt = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		}
		else
		{
			SendAck(inc);
		}
	}

	private void HandleAbort(byte senderTag, byte streamId)
	{
		int key = (senderTag << 8) | streamId;
		lock (_sync)
		{
			_rx.Remove(key);
			if (_tx != null && _tx.SenderTag == senderTag && _tx.StreamId == streamId)
			{
				_tx.Done.TrySetException(new IOException("peer aborted"));
			}
		}
	}

	private void FinishIncoming(int key, Incoming inc)
	{
		byte[] blob;
		bool ok;
		lock (inc.Sync)
		{
			if (inc.Completed)
			{
				SendDone(inc, inc.CrcOk);
				return;
			}
			blob = inc.Buffer;
			ok = Bits.Crc16(blob) == inc.Crc;
			inc.Completed = true;
			inc.CrcOk = ok;
		}
		Log($"recv complete: {blob.Length} B, crc {(ok ? "ok" : "MISMATCH")}");
		SendDone(inc, ok);
		OnProgress?.Invoke(new PipeProgress(inc.ChunkCount, inc.ChunkCount, false));
		if (ok)
		{
			OnBlobReceived?.Invoke(blob);
		}
		inc.DropAt = DateTime.UtcNow + TimeSpan.FromSeconds(5);
	}

	private async Task PumpLoop()
	{
		DateTime last = DateTime.UtcNow;
		double budget = 0;
		try
		{
			while (!_cts.IsCancellationRequested)
			{
				DateTime now = DateTime.UtcNow;
				double elapsed = (now - last).TotalSeconds;
				last = now;
				budget = Math.Min(budget + elapsed * _opt.SendRatePerSecond, _opt.WindowChunks);

				Outgoing? tx;
				lock (_sync)
				{
					tx = (_tx != null && !_tx.Done.Task.IsCompleted) ? _tx : null;
				}
				if (tx != null)
				{
					if (!tx.OpenAcked && tx.BlindSent < 0 && now >= tx.NextControlAt)
					{
						SendOpen(tx);
						tx.NextControlAt = now + _opt.ControlResend;
						if (tx.Broadcast && ++tx.OpensSent >= _opt.BlindAfterOpens)
						{
							// Nobody answers: maybe nobody is there, maybe only listeners. It goes anyway.
							tx.BlindSent = 0;
							Log("no answer: sending blind");
						}
					}
					if (!tx.OpenAcked && tx.BlindSent >= 0)
					{
						int total = tx.ChunkCount * _opt.BlindPasses;
						while (budget >= 1 && tx.BlindSent < total)
						{
							SendData(tx, tx.BlindSent % tx.ChunkCount);
							tx.BlindSent++;
							budget -= 1;
						}
						if (tx.BlindSent >= total && budget >= 1)
						{
							SendFin(tx);
							tx.Done.TrySetResult(true);
						}
					}
					if (tx.OpenAcked)
					{
						int seq;
						while (budget >= 1 && (seq = tx.NextFresh()) >= 0)
						{
							SendData(tx, seq);
							budget -= 1;
						}
						if (tx.NextFresh() < 0)
						{
							if (tx.AckedCount < tx.ChunkCount && now >= tx.NextRetransmitAt)
							{
								foreach (int s in tx.Unacked())
								{
									if (budget < 1)
									{
										break;
									}
									SendData(tx, s);
									budget -= 1;
								}
								tx.NextRetransmitAt = now + _opt.RetransmitTimeout;
							}
							if (now >= tx.NextControlAt)
							{
								SendFin(tx);
								tx.NextControlAt = now + _opt.ControlResend;
							}
						}
						OnProgress?.Invoke(new PipeProgress(tx.AckedCount, tx.ChunkCount, true));
					}
				}

				List<Incoming> incs;
				lock (_sync)
				{
					incs = _rx.Values.ToList();
				}
				foreach (Incoming inc in incs)
				{
					if (!inc.Completed && now >= inc.NextAckAt)
					{
						SendAck(inc);
						inc.NextAckAt = now + _opt.AckInterval;
					}
					if (inc.DropAt.HasValue && now >= inc.DropAt.Value)
					{
						lock (_sync)
						{
							_rx.Remove(inc.Key);
						}
					}
				}

				await Task.Delay(5, _cts.Token).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			Log("pump stopped: " + ex.Message);
		}
	}

	private void SendOpen(Outgoing tx)
	{
		byte[] f = new byte[PipeFrame.HeaderSize + 8];
		f[0] = (byte)FrameType.Open;
		f[1] = tx.SenderTag;
		f[2] = tx.StreamId;
		Bits.WriteU32(f, 3, (uint)tx.Blob.Length);
		Bits.WriteU16(f, 7, (ushort)tx.ChunkCount);
		Bits.WriteU16(f, 9, tx.Crc);
		_ch.SendFrame(f);
	}

	private void SendData(Outgoing tx, int seq)
	{
		int off = seq * _dataBytes;
		int n = Math.Min(_dataBytes, tx.Blob.Length - off);
		byte[] f = new byte[PipeFrame.HeaderSize + 2 + n];
		f[0] = (byte)FrameType.Data;
		f[1] = tx.SenderTag;
		f[2] = tx.StreamId;
		Bits.WriteU16(f, 3, (ushort)seq);
		Array.Copy(tx.Blob, off, f, 5, n);
		_ch.SendFrame(f);
		tx.MarkSent(seq);
	}

	private void SendFin(Outgoing tx)
	{
		byte[] f = new byte[PipeFrame.HeaderSize];
		f[0] = (byte)FrameType.Fin;
		f[1] = tx.SenderTag;
		f[2] = tx.StreamId;
		_ch.SendFrame(f);
	}

	private void SendAck(Incoming inc)
	{
		int baseSeq;
		uint bitmap;
		lock (inc.Sync)
		{
			(baseSeq, bitmap) = inc.AckState();
		}
		byte[] f = new byte[PipeFrame.HeaderSize + 6];
		f[0] = (byte)FrameType.Ack;
		f[1] = inc.SenderTag;
		f[2] = inc.StreamId;
		Bits.WriteU16(f, 3, (ushort)baseSeq);
		Bits.WriteU32(f, 5, bitmap);
		_ch.SendFrame(f);
	}

	private void SendDone(Incoming inc, bool ok)
	{
		byte[] f = new byte[PipeFrame.HeaderSize + 1];
		f[0] = (byte)FrameType.Done;
		f[1] = inc.SenderTag;
		f[2] = inc.StreamId;
		f[3] = (byte)(ok ? 1 : 0);
		_ch.SendFrame(f);
	}

	private void Log(string s)
	{
		OnLog?.Invoke(s);
	}

	public void Dispose()
	{
		_ch.OnFrame -= OnFrame;
		_cts.Cancel();
		try
		{
			_pump.Wait(500);
		}
		catch
		{
		}
		_cts.Dispose();
	}

	private sealed class Outgoing
	{
		public readonly byte[] Blob;

		public readonly int ChunkCount;

		public readonly ushort Crc;

		public readonly byte SenderTag;

		public readonly byte StreamId = (byte)PipeRandom.Next(1, 256);

		public readonly TaskCompletionSource<bool> Done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		private readonly BitArray _acked;

		private readonly BitArray _sent;

		private readonly int _window;

		public int AckedCount { get; private set; }

		public bool OpenAcked { get; private set; }

		public DateTime NextControlAt = DateTime.UtcNow;

		public DateTime NextRetransmitAt = DateTime.UtcNow;

		public bool Broadcast;

		public int OpensSent;

		/// <summary>Chunks sent blind so far (-1: not blind; someone acknowledges, or it isn't time yet).</summary>
		public int BlindSent = -1;

		public Outgoing(byte[] blob, PipeOptions opt, int dataBytes)
		{
			Blob = blob;
			ChunkCount = Math.Max(1, (blob.Length + dataBytes - 1) / dataBytes);
			Crc = Bits.Crc16(blob);
			SenderTag = opt.SenderTag;
			_acked = new BitArray(ChunkCount);
			_sent = new BitArray(ChunkCount);
			_window = opt.WindowChunks;
		}

		public void ApplyAck(int baseSeq, uint bitmap)
		{
			OpenAcked = true;
			for (int i = 0; i < baseSeq && i < ChunkCount; i++)
			{
				Set(i);
			}
			for (int b = 0; b < 32; b++)
			{
				if ((bitmap & (1u << b)) != 0)
				{
					int s = baseSeq + b;
					if (s < ChunkCount)
					{
						Set(s);
					}
				}
			}
		}

		private void Set(int i)
		{
			if (!_acked[i])
			{
				_acked[i] = true;
				AckedCount++;
			}
		}

		public int NextFresh()
		{
			int frontier = 0;
			while (frontier < ChunkCount && _acked[frontier])
			{
				frontier++;
			}
			int limit = Math.Min(ChunkCount, frontier + _window);
			for (int i = frontier; i < limit; i++)
			{
				if (!_acked[i] && !_sent[i])
				{
					return i;
				}
			}
			return -1;
		}

		public IEnumerable<int> Unacked()
		{
			for (int i = 0; i < ChunkCount; i++)
			{
				if (!_acked[i] && _sent[i])
				{
					yield return i;
				}
			}
		}

		public void MarkSent(int seq)
		{
			_sent[seq] = true;
		}
	}

	private sealed class Incoming
	{
		public readonly object Sync = new object();

		public readonly byte SenderTag;

		public readonly byte StreamId;

		public readonly int ChunkCount;

		public readonly ushort Crc;

		public readonly byte[] Buffer;

		private readonly int _dataBytes;

		private readonly BitArray _have;

		public int Received { get; private set; }

		public bool FinSeen;

		public bool Completed;

		public bool CrcOk;

		public DateTime NextAckAt = DateTime.UtcNow;

		public DateTime NextFastAckAt = DateTime.UtcNow;

		public DateTime? DropAt;

		public int Key => (SenderTag << 8) | StreamId;

		public Incoming(byte senderTag, byte streamId, long totalLen, int chunks, ushort crc, int dataBytes)
		{
			SenderTag = senderTag;
			StreamId = streamId;
			ChunkCount = chunks;
			Crc = crc;
			_dataBytes = dataBytes;
			Buffer = new byte[totalLen];
			_have = new BitArray(chunks);
		}

		public bool Store(int seq, byte[] src, int srcOff, int len)
		{
			if (seq < 0 || seq >= ChunkCount || _have[seq])
			{
				return Received == ChunkCount;
			}
			int off = seq * _dataBytes;
			int n = Math.Min(len, Buffer.Length - off);
			if (n > 0)
			{
				Array.Copy(src, srcOff, Buffer, off, n);
			}
			_have[seq] = true;
			Received++;
			return Received == ChunkCount;
		}

		public (int, uint) AckState()
		{
			int baseSeq = 0;
			while (baseSeq < ChunkCount && _have[baseSeq])
			{
				baseSeq++;
			}
			uint bitmap = 0;
			for (int b = 0; b < 32; b++)
			{
				int s = baseSeq + b;
				if (s < ChunkCount && _have[s])
				{
					bitmap |= 1u << b;
				}
			}
			return (baseSeq, bitmap);
		}
	}
}
