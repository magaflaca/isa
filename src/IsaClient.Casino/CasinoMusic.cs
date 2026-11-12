using System;
using System.IO;
using IsaClient.Core;
using IsaClient.Music;
using Terraria;

namespace IsaClient.Casino;

/// <summary>
/// The casino's lounge music while its window is open. To stay small it's Ogg Vorbis (40 kbps) cut at the end of its
/// first repeat: the song's second half is the same 40.75-second passage over and over, so it plays the start once
/// and then loops that passage sample-exactly, forever (NVorbis decodes it, the music player's waveOut plays it).
/// Follows the music volume, fades in and out, makes way for a song of the music player and quiets Terraria's music.
/// </summary>
internal static class CasinoMusic
{
	/// <summary>Play the casino's music while it's open.</summary>
	[Setting("CasinoMusic")]
	public static bool Enabled = true;

	private const string Resource = "IsaClient.Assets.CasinoMusic.ogg";

	/// <summary>The repeated passage, in samples (32 kHz): 106.33 s to 147.08 s, the end of the file.</summary>
	private const long LoopStart = 3402428L;

	private const long LoopEnd = 4706461L;

	private static byte[] _ogg;

	private static AudioPlayer _player;

	private static float _fade;

	private static bool _failed;

	private static bool _hooked;

	/// <summary>Heard now (Terraria's music fades out meanwhile).</summary>
	public static bool Playing => _player != null && _fade > 0f;

	/// <summary>Every tick: plays while <paramref name="open"/>, fades out and stops otherwise.</summary>
	public static void Update(bool open)
	{
		if (!_hooked)
		{
			_hooked = true;
			MediaFocus.CasinoPlaying = () => Playing;
		}
		bool want = open && Enabled && !_failed && Main.musicVolume > 0f && !(MusicPlayer.HasSong && !MusicPlayer.IsPaused);
		_fade = Math.Max(0f, Math.Min(1f, _fade + (want ? 1f / 60f : -1f / 40f)));
		if (!want && _fade <= 0f)
		{
			Stop();
			return;
		}
		if (_player == null)
		{
			if (!want)
			{
				return;
			}
			Start();
			if (_player == null)
			{
				return;
			}
		}
		_player.Volume = Loudness(Main.musicVolume) * _fade;
	}

	/// <summary>The loudness Terraria gives its own music for a volume (as the title music's track).</summary>
	private static float Loudness(float volume)
	{
		if (volume <= 0f)
		{
			return 0f;
		}
		return (float)Math.Sqrt(Math.Pow(10.0, (31.0 * volume - 25.0 - 11.94) / 20.0));
	}

	private static void Start()
	{
		try
		{
			if (_ogg == null)
			{
				using Stream stream = typeof(CasinoMusic).Assembly.GetManifestResourceStream(Resource);
				using MemoryStream copy = new MemoryStream();
				stream.CopyTo(copy);
				_ogg = copy.ToArray();
			}
			AudioPlayer player = new AudioPlayer(new LoopedVorbis(_ogg, LoopStart, LoopEnd), 0f);
			player.Failed += error =>
			{
				_failed = true;
				ClientLog.LogException(new Exception("Casino music: " + error));
			};
			_player = player;
			player.Start();
		}
		catch (Exception ex)
		{
			_failed = true;
			_player = null;
			ClientLog.LogException(ex);
		}
	}

	private static void Stop()
	{
		AudioPlayer player = _player;
		_player = null;
		player?.Stop();
	}
}
