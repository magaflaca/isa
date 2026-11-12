using System;
using IsaClient.Core;
using Microsoft.Xna.Framework.Audio;
using Terraria;

namespace IsaClient.Games;

/// <summary>
/// Tetris's tune, Korobeiniki (a Russian folk song), made here as an 8-bit loop: a square-wave lead over a triangle
/// bass that bounces in octaves. Built once into a sound and looped while a game runs; follows the music volume.
/// </summary>
internal static class TetrisMusic
{
	/// <summary>Play Tetris's music while a game runs.</summary>
	[Setting("TetrisMusic")]
	public static bool Enabled = true;

	private const int Rate = 22050;

	/// <summary>Seconds per eighth note at level 1; each level plays it a little faster.</summary>
	private const double Eighth = 0.17;

	/// <summary>Up to this many levels speed the music up (6% each).</summary>
	private const int TempoSteps = 12;

	/// <summary>The lead: note (semitones from A4, 99 = rest) and length in eighths.</summary>
	private static readonly int[] Lead =
	{
		// A: E B C D C B A A C E D C B C D E C A A
		7, 2, 2, 1, 3, 1, 5, 2, 3, 1, 2, 1, 0, 2, 0, 1, 3, 1, 7, 2, 5, 1, 3, 1, 2, 3, 3, 1, 5, 2, 7, 2, 3, 2, 0, 2, 0, 4,
		// B: D F A G F E C E D C B B C D E C A A
		5, 3, 8, 1, 12, 2, 10, 1, 8, 1, 7, 3, 3, 1, 7, 2, 5, 1, 3, 1, 2, 2, 2, 1, 3, 1, 5, 2, 7, 2, 3, 2, 0, 2, 0, 2, 99, 2
	};

	/// <summary>Bass root of each half bar (semitones from A2): E E A A G# E A A | D D C C G E A A.</summary>
	private static readonly int[] Bass = { -5, -5, 0, 0, -1, -5, 0, 0, -7, -7, 3, 3, -2, -5, 0, 0 };

	/// <summary>One loop per tempo, made when first needed.</summary>
	private static readonly SoundEffect[] _sounds = new SoundEffect[TempoSteps];

	private static int _step = -1;

	private static SoundEffectInstance _instance;

	private static bool _failed;

	/// <summary>Every tick: plays while <paramref name="playing"/>, quiet otherwise.</summary>
	public static void Update(bool playing, int level)
	{
		bool want = playing && Enabled && Main.musicVolume > 0f && !(IsaClient.Music.MusicPlayer.HasSong && !IsaClient.Music.MusicPlayer.IsPaused);
		if (!want)
		{
			if (_instance != null && _instance.State == SoundState.Playing)
			{
				_instance.Pause();
			}
			return;
		}
		// A new level: the loop at the new tempo (from its start).
		int step = Math.Max(0, Math.Min(TempoSteps - 1, level - 1));
		if (step != _step && _instance != null)
		{
			_instance.Stop();
			_instance.Dispose();
			_instance = null;
		}
		if (_instance == null && !_failed)
		{
			try
			{
				_sounds[step] ??= new SoundEffect(Build(Eighth / (1.0 + 0.06 * step)), Rate, AudioChannels.Mono);
				_instance = _sounds[step].CreateInstance();
				_instance.IsLooped = true;
				_step = step;
			}
			catch (Exception ex)
			{
				_failed = true;
				ClientLog.LogException(ex);
				return;
			}
		}
		if (_instance == null)
		{
			return;
		}
		_instance.Volume = Math.Max(0f, Math.Min(1f, Main.musicVolume * 0.55f));
		if (_instance.State != SoundState.Playing)
		{
			if (_instance.State == SoundState.Paused)
			{
				_instance.Resume();
			}
			else
			{
				_instance.Play();
			}
		}
	}

	/// <summary>From the start next time (a new game).</summary>
	public static void Restart()
	{
		_instance?.Stop();
	}

	private static double Frequency(double semitonesFromA4)
	{
		return 440.0 * Math.Pow(2.0, semitonesFromA4 / 12.0);
	}

	/// <summary>The whole loop as 16-bit PCM.</summary>
	private static byte[] Build(double eighth)
	{
		int eighths = 0;
		for (int i = 1; i < Lead.Length; i += 2)
		{
			eighths += Lead[i];
		}
		int perEighth = (int)(Rate * eighth);
		int total = eighths * perEighth;
		float[] mix = new float[total];
		// Lead: a 25% square, a short attack and a gentle decay, a hair of space between notes.
		int at = 0;
		for (int i = 0; i < Lead.Length; i += 2)
		{
			int length = Lead[i + 1] * perEighth;
			if (Lead[i] != 99)
			{
				double freq = Frequency(Lead[i]);
				int sounding = length - perEighth / 6;
				for (int s = 0; s < sounding; s++)
				{
					double phase = (s * freq / Rate) % 1.0;
					float wave = (phase < 0.25) ? 1f : -0.33f;
					float env = Math.Min(1f, s / 120f) * (float)Math.Max(0.35, 1.0 - (double)s / sounding * 0.65);
					mix[at + s] += wave * env * 0.32f;
				}
			}
			at += length;
		}
		// Bass: eighths bouncing root, octave, in a triangle wave (a root per half bar).
		int halves = total / (perEighth * 4);
		for (int half = 0; half < halves; half++)
		{
			for (int e = 0; e < 4; e++)
			{
				double freq = Frequency(Bass[half % Bass.Length] - 24 + ((e % 2 == 1) ? 12 : 0));
				int start = (half * 4 + e) * perEighth;
				int sounding = perEighth - perEighth / 5;
				for (int s = 0; s < sounding && start + s < total; s++)
				{
					double phase = (s * freq / Rate) % 1.0;
					float wave = (float)(4.0 * Math.Abs(phase - 0.5) - 1.0);
					float env = Math.Min(1f, s / 60f) * (1f - (float)s / sounding * 0.4f);
					mix[start + s] += wave * env * 0.3f;
				}
			}
		}
		byte[] pcm = new byte[total * 2];
		for (int i = 0; i < total; i++)
		{
			short v = (short)(Math.Max(-1f, Math.Min(1f, mix[i])) * 26000f);
			pcm[i * 2] = (byte)(v & 0xFF);
			pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
		}
		return pcm;
	}
}
