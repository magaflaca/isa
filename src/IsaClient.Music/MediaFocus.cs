using System;
using IsaClient.Core;
using Microsoft.Xna.Framework.Audio;
using Terraria;

namespace IsaClient.Music;

/// <summary>
/// One thing plays at a time: a song and a video pause each other (a paused song comes back when the video closes),
/// and while either one plays the game's own sounds fade out so only it is heard (Quiet Game While Playing).
/// </summary>
public static class MediaFocus
{
	public const string MusicOwner = "music";

	public const string VideoOwner = "video";

	/// <summary>Game sounds and ambience fade out while a song or a video plays.</summary>
	[Setting("QuietGameWhileMediaPlays")]
	public static bool QuietGame = true;

	/// <summary>Raised when something starts playing (its owner name): the others pause.</summary>
	public static event Action<string> Claimed;

	/// <summary>Raised when something stops for good (closed, finished): what it paused may come back.</summary>
	public static event Action<string> Released;

	/// <summary>Set by the video player: a video is playing (not paused).</summary>
	public static Func<bool> VideoPlaying = () => false;

	/// <summary>Set by the video player: a video is open (playing or paused), for the game music.</summary>
	public static Func<bool> VideoOpen = () => false;

	private static float _gameVolume = 1f;

	/// <summary>
	/// A factor on Terraria's sound effects and ambience (read by ActiveSound and LegacySoundPlayer). Unlike
	/// SoundEffect.MasterVolume it leaves other voices alone (the GBA player's sound keeps playing).
	/// </summary>
	public static float GameSoundScale = 1f;

	private static bool _touched;

	/// <summary>Set by the games: one with its own music is being played (Tetris).</summary>
	public static Func<bool> GamePlaying = () => false;

	/// <summary>Read by Main.UpdateAudio: fades Terraria's music out while a song, a video or a game's music is on.</summary>
	public static bool SilencesGameMusic => MusicPlayer.SilencesGameMusic || (MusicPlayer.MuteGameMusic && VideoOpen()) || GamePlaying() || ConsolePlaying() || CasinoPlaying();

	/// <summary>Set by the GBA player: the console is on and playing (its own slot, so a game setting GamePlaying can't drop it).</summary>
	public static Func<bool> ConsolePlaying = () => false;

	/// <summary>Set by the casino: its lounge music is playing.</summary>
	public static Func<bool> CasinoPlaying = () => false;

	public static void Claim(string owner)
	{
		Claimed?.Invoke(owner);
	}

	public static void Release(string owner)
	{
		Released?.Invoke(owner);
	}

	/// <summary>Per frame (main thread): fades the game's sound effects and ambience in and out.</summary>
	public static void Update()
	{
		bool quiet = QuietGame && (MusicPlayer.IsPlaying || VideoPlaying());
		float target = quiet ? 0f : 1f;
		if (!_touched && !quiet)
		{
			return;
		}
		_gameVolume += Math.Sign(target - _gameVolume) * Math.Min(Math.Abs(target - _gameVolume), 1f / 20f);
		try
		{
			SoundEffect.MasterVolume = _gameVolume;
			_touched = _gameVolume < 1f;
		}
		catch
		{
			// No sound device: nothing to fade.
			_touched = false;
		}
	}
}
