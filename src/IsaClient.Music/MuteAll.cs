using System;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Music;

/// <summary>
/// .mute: every game sound off in one go (music, sounds, ambient and the music module), and back to the exact volumes
/// you had. The volumes you had are kept in the settings, so it survives restarts and updates while muted.
/// </summary>
public static class MuteAll
{
	[Setting("GameMuted")]
	public static bool Muted;

	/// <summary>Music, sound, ambient, music module.</summary>
	[Setting("MutedVolumes")]
	public static float[] SavedVolumes = new float[4] { 1f, 1f, 1f, 0.6f };

	public static void Toggle()
	{
		bool silent = Main.musicVolume <= 0f && Main.soundVolume <= 0f && Main.ambientVolume <= 0f;
		if (Muted && silent)
		{
			Restore();
		}
		else
		{
			Mute();
		}
		Main.SaveSettings();
		ClientSettings.Save();
	}

	private static void Mute()
	{
		SavedVolumes = new float[4] { Main.musicVolume, Main.soundVolume, Main.ambientVolume, MusicPlayer.Volume };
		Main.musicVolume = 0f;
		Main.soundVolume = 0f;
		Main.ambientVolume = 0f;
		MusicPlayer.SetVolume(0f);
		Muted = true;
		ClientChat.Info("Game sound muted. Use .mute again to get your volumes back.");
	}

	private static void Restore()
	{
		float[] saved = ((SavedVolumes != null && SavedVolumes.Length >= 4) ? SavedVolumes : new float[4] { 1f, 1f, 1f, 0.6f });
		bool nothingSaved = saved[0] <= 0f && saved[1] <= 0f && saved[2] <= 0f;
		Main.musicVolume = (nothingSaved ? 1f : Clamp(saved[0]));
		Main.soundVolume = (nothingSaved ? 1f : Clamp(saved[1]));
		Main.ambientVolume = (nothingSaved ? 1f : Clamp(saved[2]));
		MusicPlayer.SetVolume(Clamp(saved[3]));
		Muted = false;
		ClientChat.Info("Game sound back: music " + Percent(Main.musicVolume) + ", sound " + Percent(Main.soundVolume) + ", ambient " + Percent(Main.ambientVolume) + ".");
	}

	private static float Clamp(float value)
	{
		return Math.Max(0f, Math.Min(1f, value));
	}

	private static string Percent(float value)
	{
		return (int)Math.Round(value * 100f) + "%";
	}

	#region Chat commands

	/// <summary>.mute</summary>
	internal static void MuteCommand(CommandContext command)
	{
		Toggle();
	}

	#endregion
}
