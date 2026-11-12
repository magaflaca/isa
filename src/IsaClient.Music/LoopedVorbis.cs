using System;
using System.IO;
using NVorbis;

namespace IsaClient.Music;

/// <summary>
/// An Ogg Vorbis track decoded by NVorbis (the decoder Terraria already ships), looped sample-exactly: it plays from
/// the start, and from <c>loopEnd</c> (or the end of the file) goes on at <c>loopStart</c> with no gap, forever.
/// </summary>
public sealed class LoopedVorbis : IPcmSource
{
	private readonly VorbisReader _reader;

	private readonly long _loopStart;

	private readonly long _loopEnd;

	private float[] _floats = new float[0];

	public int Channels { get; }

	public int SampleRate { get; }

	/// <param name="loopStart">Where it goes on after the loop end, in samples per channel (negative: no loop).</param>
	/// <param name="loopEnd">Where the loop ends, in samples per channel (0: the end of the file).</param>
	public LoopedVorbis(byte[] ogg, long loopStart, long loopEnd)
	{
		_reader = new VorbisReader(new MemoryStream(ogg, writable: false), true);
		Channels = _reader.Channels;
		SampleRate = _reader.SampleRate;
		_loopStart = loopStart;
		_loopEnd = loopEnd;
	}

	public int Read(short[] buffer, int count)
	{
		count -= count % Channels;
		if (_floats.Length < count)
		{
			_floats = new float[count];
		}
		int done = 0;
		int emptyReads = 0;
		while (done < count)
		{
			int want = count - done;
			if (_loopEnd > 0)
			{
				want = (int)Math.Min(want, Math.Max(0L, (_loopEnd - _reader.DecodedPosition) * Channels));
			}
			int got = (want > 0) ? _reader.ReadSamples(_floats, 0, want) : 0;
			for (int i = 0; i < got; i++)
			{
				float v = _floats[i];
				buffer[done + i] = (short)((v >= 1f) ? 32767 : ((v <= -1f) ? -32767 : (v * 32767f)));
			}
			done += got;
			if (got < want || want == 0)
			{
				// The loop end (or the file's end): on from the loop start, unless it doesn't loop.
				if (_loopStart < 0 || ++emptyReads > 2)
				{
					break;
				}
				_reader.DecodedPosition = _loopStart;
			}
			else
			{
				emptyReads = 0;
			}
		}
		return done;
	}

	public void Dispose()
	{
		_reader.Dispose();
	}
}
