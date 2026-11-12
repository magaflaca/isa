using System;
using Microsoft.Xna.Framework.Audio;

namespace IsaClient.Gba;

/// <summary>
/// The console's sound through XNA: a streaming voice fed from the machine's thread, a buffer per emulated frame.
/// Rates above 48 kHz (XNA's limit) are halved.
/// </summary>
internal sealed class GbaAudio : IDisposable
{
	private DynamicSoundEffectInstance _voice;

	private readonly int _rate;

	private readonly int _halve;

	private byte[] _bytes = new byte[0];

	private readonly double _bufferSeconds;

	private volatile float _volume = 1f;

	private readonly object _lock = new object();

	public GbaAudio(double sourceRate, double fps)
	{
		_halve = (sourceRate > 48000.0) ? 2 : 1;
		_rate = Math.Max(8000, Math.Min(48000, (int)Math.Round(sourceRate / _halve)));
		_bufferSeconds = 1.0 / fps;
		_voice = new DynamicSoundEffectInstance(_rate, AudioChannels.Stereo);
		_voice.Play();
	}

	/// <summary>0..1.</summary>
	public float Volume
	{
		get => _volume;
		set
		{
			_volume = Math.Max(0f, Math.Min(1f, value));
			lock (_lock)
			{
				if (_voice != null && !_voice.IsDisposed)
				{
					_voice.Volume = _volume;
				}
			}
		}
	}

	/// <summary>Seconds of sound waiting to play.</summary>
	public double Queued
	{
		get
		{
			lock (_lock)
			{
				return (_voice == null || _voice.IsDisposed) ? 0.0 : _voice.PendingBufferCount * _bufferSeconds;
			}
		}
	}

	/// <summary>Interleaved stereo samples from the core (machine thread).</summary>
	public void Submit(short[] samples, int frames)
	{
		int outFrames = frames / _halve;
		int need = outFrames * 4;
		if (need <= 0)
		{
			return;
		}
		// a new array each time: XNA may still be reading the previous one
		_bytes = new byte[need];
		for (int i = 0, o = 0; i < outFrames; i++)
		{
			int l;
			int r;
			if (_halve == 2)
			{
				l = (samples[i * 4] + samples[i * 4 + 2]) / 2;
				r = (samples[i * 4 + 1] + samples[i * 4 + 3]) / 2;
			}
			else
			{
				l = samples[i * 2];
				r = samples[i * 2 + 1];
			}
			_bytes[o++] = (byte)l;
			_bytes[o++] = (byte)(l >> 8);
			_bytes[o++] = (byte)r;
			_bytes[o++] = (byte)(r >> 8);
		}
		lock (_lock)
		{
			if (_voice == null || _voice.IsDisposed)
			{
				return;
			}
			// too much queued (a hiccup): drop this one instead of lagging behind the picture
			if (_voice.PendingBufferCount > 10)
			{
				return;
			}
			try
			{
				_voice.SubmitBuffer(_bytes, 0, need);
				if (_voice.State != SoundState.Playing)
				{
					_voice.Play();
				}
			}
			catch
			{
			}
		}
	}

	public void Dispose()
	{
		lock (_lock)
		{
			try
			{
				_voice?.Stop();
				_voice?.Dispose();
			}
			catch
			{
			}
			_voice = null;
		}
	}
}
