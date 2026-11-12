using System;

namespace IsaClient.Video;

/// <summary>
/// Plays audio faster or slower without changing its pitch (WSOLA): the sound is cut in short overlapping pieces
/// that are laid out closer together or further apart, each one moved a little to where it lines up best with the
/// last, and cross-faded. At 1x the samples go through untouched.
/// </summary>
internal sealed class TimeStretch
{
	private readonly int _channels;

	private readonly int _hop;

	private readonly int _overlap;

	private readonly int _search;

	private float[] _in = new float[0];

	private int _inFrames;

	private double _analysis;

	private readonly float[] _tail;

	private bool _haveTail;

	private float[] _out = new float[0];

	private int _outFrames;

	public float Speed = 1f;

	public TimeStretch(int sampleRate, int channels)
	{
		_channels = channels;
		_hop = Math.Max(64, sampleRate * 30 / 1000);
		_overlap = Math.Max(32, sampleRate * 10 / 1000);
		_search = Math.Max(16, sampleRate * 8 / 1000);
		_tail = new float[_overlap * channels];
	}

	public void Reset()
	{
		_inFrames = 0;
		_outFrames = 0;
		_analysis = 0.0;
		_haveTail = false;
	}

	/// <summary>Adds 16-bit interleaved PCM.</summary>
	public void Put(byte[] pcm, int offset, int bytes)
	{
		int frames = bytes / (2 * _channels);
		Ensure(ref _in, (_inFrames + frames) * _channels);
		int at = _inFrames * _channels;
		for (int i = 0; i < frames * _channels; i++)
		{
			_in[at + i] = (short)(pcm[offset + i * 2] | (pcm[offset + i * 2 + 1] << 8)) / 32768f;
		}
		_inFrames += frames;
		Process();
	}

	/// <summary>Adds 16-bit interleaved PCM that goes out as it is (1x).</summary>
	public void Queue(byte[] pcm, int offset, int bytes)
	{
		int frames = bytes / (2 * _channels);
		Ensure(ref _out, (_outFrames + frames) * _channels);
		int at = _outFrames * _channels;
		for (int i = 0; i < frames * _channels; i++)
		{
			_out[at + i] = (short)(pcm[offset + i * 2] | (pcm[offset + i * 2 + 1] << 8)) / 32768f;
		}
		_outFrames += frames;
	}

	/// <summary>Takes up to <paramref name="maxFrames"/> stretched frames as 16-bit PCM; returns the frames written.</summary>
	public int Take(short[] destination, int maxFrames)
	{
		int frames = Math.Min(maxFrames, _outFrames);
		int count = frames * _channels;
		for (int i = 0; i < count; i++)
		{
			float v = _out[i] * 32767f;
			destination[i] = (short)((v > 32767f) ? 32767f : ((v < -32768f) ? (-32768f) : v));
		}
		Array.Copy(_out, count, _out, 0, _outFrames * _channels - count);
		_outFrames -= frames;
		return frames;
	}

	public int Buffered => _outFrames;

	private void Process()
	{
		int ch = _channels;
		while ((int)_analysis + _search + _hop + _overlap + 1 < _inFrames)
		{
			int target = (int)_analysis;
			int best = target;
			if (_haveTail)
			{
				double bestScore = double.NegativeInfinity;
				int from = Math.Max(0, target - _search);
				int to = target + _search;
				for (int c = from; c <= to; c += 2)
				{
					double dot = 0.0;
					double energy = 1e-9;
					int baseIndex = c * ch;
					for (int i = 0; i < _overlap; i++)
					{
						float x = 0f;
						float y = 0f;
						for (int k = 0; k < ch; k++)
						{
							x += _in[baseIndex + i * ch + k];
							y += _tail[i * ch + k];
						}
						dot += x * y;
						energy += x * x;
					}
					double score = dot / Math.Sqrt(energy);
					if (score > bestScore)
					{
						bestScore = score;
						best = c;
					}
				}
			}
			Ensure(ref _out, (_outFrames + _hop) * ch);
			int o = _outFrames * ch;
			int s = best * ch;
			for (int i = 0; i < _hop; i++)
			{
				float w = (i < _overlap && _haveTail) ? ((float)i / _overlap) : 1f;
				for (int k = 0; k < ch; k++)
				{
					float v = _in[s + i * ch + k];
					_out[o + i * ch + k] = (w < 1f) ? (_tail[i * ch + k] * (1f - w) + v * w) : v;
				}
			}
			_outFrames += _hop;
			Array.Copy(_in, (best + _hop) * ch, _tail, 0, _overlap * ch);
			_haveTail = true;
			_analysis += _hop * (double)Speed;
			int drop = Math.Min(_inFrames, (int)_analysis - _search - 1);
			if (drop > 0)
			{
				Array.Copy(_in, drop * ch, _in, 0, (_inFrames - drop) * ch);
				_inFrames -= drop;
				_analysis -= drop;
			}
		}
	}

	private static void Ensure(ref float[] array, int size)
	{
		if (array.Length < size)
		{
			Array.Resize(ref array, Math.Max(size, array.Length * 2));
		}
	}
}
