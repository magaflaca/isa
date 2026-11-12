using Terraria;
using IsaClient.Core;

namespace IsaClient.Music;

/// <summary>Audio tweaks: forced soundtrack and the classic or disabled themes.</summary>
public static class AudioTweaks
{
	/// <summary>0 no override, 1 Terraria, 2 Terraria: Otherworld.</summary>
	[Setting("ForcedSoundtrackType", Remembered = true, Min = 0, Max = 2)]
	public static int ForceSoundtrackId;

	[Setting("RevertJungleNightTheme", Remembered = true)]
	public static bool OldMus_JungleNight;

	[Setting("RevertUndergroundJungleTheme", Remembered = true)]
	public static bool OldMus_UndergroundJungle;

	[Setting("ClassicDungeonTheme", Remembered = true)]
	public static bool OldMus_Dungeon;

	[Setting("ClassicUnderworldTheme", Remembered = true)]
	public static bool OldMus_Underworld;

	[Setting("RevertUndergroundDesertTheme", Remembered = true)]
	public static bool OldMus_UndergroundDesert;

	[Setting("RevertDukeFishronTheme", Remembered = true)]
	public static bool OldMus_Fishron;

	[Setting("ClassicGoblinArmyTheme", Remembered = true)]
	public static bool OldMus_Goblins;

	[Setting("ClassicPiratesTheme", Remembered = true)]
	public static bool OldMus_Pirates;

	[Setting("DisableMorningRainTheme", Remembered = true)]
	public static bool OldMus_MorningRain;

	[Setting("DisableStormTheme", Remembered = true)]
	public static bool OldMus_Storm;

	[Setting("DisableWindyDayTheme", Remembered = true)]
	public static bool OldMus_WindyDay;

	[Setting("DisableSlimeRainTheme", Remembered = true)]
	public static bool OldMus_SlimeRain;

	[Setting("DisableGraveyardTheme", Remembered = true)]
	public static bool OldMus_Graveyard;

	[Setting("DisableTownThemes", Remembered = true)]
	public static bool OldMus_NoTown;

	public static void DisableAudioTweaks()
	{
		Main.swapMusic = false;
		ForceSoundtrackId = 0;
		OldMus_JungleNight = false;
		OldMus_UndergroundJungle = false;
		OldMus_Dungeon = false;
		OldMus_Underworld = false;
		OldMus_UndergroundDesert = false;
		OldMus_Fishron = false;
		OldMus_Goblins = false;
		OldMus_Pirates = false;
		OldMus_MorningRain = false;
		OldMus_Storm = false;
		OldMus_WindyDay = false;
		OldMus_SlimeRain = false;
		OldMus_Graveyard = false;
		OldMus_NoTown = false;
	}

	/// <summary>Main.UpdateWindyDayState: Disable Windy Day / Storm themes.</summary>
	public static void ApplyWeatherMusicOverrides()
	{
		if (OldMus_WindyDay)
		{
			Main._shouldUseWindyDayMusic = false;
		}
		if (OldMus_Storm)
		{
			Main._shouldUseStormMusic = false;
		}
	}
}
