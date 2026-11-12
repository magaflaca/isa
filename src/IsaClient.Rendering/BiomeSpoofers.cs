using Terraria;

namespace IsaClient.Rendering;

/// <summary>Applies the biome spoofers to the local player.</summary>
public static class BiomeSpoofers
{
	/// <summary>"Enforce Only Overrides": vanilla zones are cleared, only the spoofed ones stay.</summary>
	public static bool SuppressZones;

	public static void DisableBiomeSpoofers()
	{
		SuppressZones = false;
		SpoofedZones.ZoneDungeon = false;
		SpoofedZones.ZoneCrimson = false;
		SpoofedZones.ZoneCorrupt = false;
		SpoofedZones.ZoneHallow = false;
		SpoofedZones.ZoneSnow = false;
		SpoofedZones.ZoneDesert = false;
		SpoofedZones.ZoneGemCave = false;
		SpoofedZones.ZoneGlowshroom = false;
		SpoofedZones.ZoneGranite = false;
		SpoofedZones.ZoneHive = false;
		SpoofedZones.ZoneLihzhardTemple = false;
		SpoofedZones.ZoneMarble = false;
		SpoofedZones.ZoneUndergroundDesert = false;
		SpoofedZones.ZoneShimmer = false;
		SpoofedZones.ZoneWaterCandle = false;
		SpoofedZones.ZonePeaceCandle = false;
		SpoofedZones.ZoneShadowCandle = false;
		SpoofedZones.ZoneMeteor = false;
		SpoofedZones.ZoneJungle = false;
		SpoofedZones.ZoneGraveyard = false;
		SpoofedZones.ZoneRain = false;
		SpoofedZones.ZoneSandstorm = false;
		SpoofedZones.ZoneOverworldHeight = false;
		SpoofedZones.ZoneDirtLayerHeight = false;
		SpoofedZones.ZoneRockLayerHeight = false;
		SpoofedZones.ZoneUnderworldHeight = false;
		SpoofedZones.ZoneSkyHeight = false;
		SpoofedZones.ZoneBeach = false;
		SpoofedZones.ZoneTowerSolar = false;
		SpoofedZones.ZoneTowerVortex = false;
		SpoofedZones.ZoneTowerNebula = false;
		SpoofedZones.ZoneTowerStardust = false;
		SpoofedZones.ZoneOldOnesArmy = false;
	}

	/// <summary>Player.UpdateBiomes, after the vanilla zones were read from the scene.</summary>
	public static void ApplyBiomeSpoofers(Player player)
	{
		if (player.whoAmI != Main.myPlayer || Main.gameMenu)
		{
			return;
		}
		if (SuppressZones)
		{
			player.ZoneSnow = false;
			player.ZoneMarble = false;
			player.ZoneOverworldHeight = false;
			player.ZoneSandstorm = false;
			player.ZoneHallow = false;
			player.ZoneDungeon = false;
			player.ZoneLihzhardTemple = false;
			player.ZoneUndergroundDesert = false;
			player.ZoneJungle = false;
			player.ZoneDesert = false;
			player.ZoneGlowshroom = false;
			player.ZoneBeach = false;
			player.ZoneGranite = false;
			player.ZoneHive = false;
			player.ZoneGemCave = false;
			player.ZoneGraveyard = false;
			player.ZoneShimmer = false;
			player.ZoneMeteor = false;
			player.ZoneTowerSolar = false;
			player.ZoneTowerVortex = false;
			player.ZoneTowerNebula = false;
			player.ZoneTowerStardust = false;
			player.ZoneOldOneArmy = false;
			player.ZoneSkyHeight = false;
			player.ZoneDirtLayerHeight = false;
			player.ZoneRockLayerHeight = false;
			player.ZoneUnderworldHeight = false;
			player.ZoneRain = false;
			player.ZoneCorrupt = false;
			player.ZoneCrimson = false;
			player.ZoneWaterCandle = false;
			player.ZonePeaceCandle = false;
			player.ZoneShadowCandle = false;
		}
		player.ZoneDungeon |= SpoofedZones.ZoneDungeon;
		player.ZoneCorrupt |= SpoofedZones.ZoneCorrupt;
		player.ZoneCrimson |= SpoofedZones.ZoneCrimson;
		player.ZoneHallow |= SpoofedZones.ZoneHallow;
		player.ZoneJungle |= SpoofedZones.ZoneJungle;
		player.ZoneSnow |= SpoofedZones.ZoneSnow;
		player.ZoneDesert |= SpoofedZones.ZoneDesert;
		player.ZoneGemCave |= SpoofedZones.ZoneGemCave;
		player.ZoneGlowshroom |= SpoofedZones.ZoneGlowshroom;
		player.ZoneGranite |= SpoofedZones.ZoneGranite;
		player.ZoneHive |= SpoofedZones.ZoneHive;
		player.ZoneLihzhardTemple |= SpoofedZones.ZoneLihzhardTemple;
		player.ZoneMarble |= SpoofedZones.ZoneMarble;
		player.ZoneUndergroundDesert |= SpoofedZones.ZoneUndergroundDesert;
		player.ZoneShimmer |= SpoofedZones.ZoneShimmer;
		player.ZoneWaterCandle |= SpoofedZones.ZoneWaterCandle;
		player.ZonePeaceCandle |= SpoofedZones.ZonePeaceCandle;
		player.ZoneShadowCandle |= SpoofedZones.ZoneShadowCandle;
		player.ZoneMeteor |= SpoofedZones.ZoneMeteor;
		player.ZoneGraveyard |= SpoofedZones.ZoneGraveyard;
		player.ZoneRain |= SpoofedZones.ZoneRain;
		player.ZoneSandstorm |= SpoofedZones.ZoneSandstorm;
		player.ZoneOverworldHeight |= SpoofedZones.ZoneOverworldHeight;
		player.ZoneDirtLayerHeight |= SpoofedZones.ZoneDirtLayerHeight;
		player.ZoneRockLayerHeight |= SpoofedZones.ZoneRockLayerHeight;
		player.ZoneUnderworldHeight |= SpoofedZones.ZoneUnderworldHeight;
		player.ZoneSkyHeight |= SpoofedZones.ZoneSkyHeight;
		player.ZoneBeach |= SpoofedZones.ZoneBeach;
		player.ZoneTowerSolar |= SpoofedZones.ZoneTowerSolar;
		player.ZoneTowerVortex |= SpoofedZones.ZoneTowerVortex;
		player.ZoneTowerNebula |= SpoofedZones.ZoneTowerNebula;
		player.ZoneTowerStardust |= SpoofedZones.ZoneTowerStardust;
		player.ZoneOldOneArmy |= SpoofedZones.ZoneOldOnesArmy;
	}
}
