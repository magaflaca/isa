using System.Collections.Generic;
using IsaClient.Rendering;
using Microsoft.Xna.Framework;
using Terraria.Audio;
using Terraria.UI;

namespace IsaClient.Menu;

/// <summary>Cheats → Biome Spoofers: zones forced on for your player.</summary>
public static class BiomeSpooferMenu
{
	internal static readonly List<string> Entries = new List<string>
	{
		"Enforce Only Overrides", "Corruption", "Crimson", "Hallow", "Jungle", "Glowing Mushroom", "Snow", "Desert", "Dungeon", "Jungle Temple",
		"Underground Desert", "Hive", "Aether", "Gem Cave", "Marble", "Granite", "Meteor", "Graveyard", "Rain", "Sandstorm",
		"Overworld", "Underground", "Caverns", "Underworld", "Space", "Beach", "Solar Tower", "Vortex Tower", "Nebula Tower", "Stardust Tower",
		"Old One's Army", "Water Candle", "Peace Candle", "Shadow Candle", "Disable All"
	};

	internal static UIElement Create(string cheat, Color color)
	{
		switch (cheat)
		{
		case "Disable All":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				BiomeSpoofers.DisableBiomeSpoofers();
			});
		case "Enforce Only Overrides":
			return MenuControls.MakeToggle(cheat, color, () => BiomeSpoofers.SuppressZones, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref BiomeSpoofers.SuppressZones);
			});
		case "Corruption":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneCorrupt, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneCorrupt);
			});
		case "Crimson":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneCrimson, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneCrimson);
			});
		case "Hallow":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneHallow, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneHallow);
			});
		case "Jungle":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneJungle, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneJungle);
			});
		case "Glowing Mushroom":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneGlowshroom, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneGlowshroom);
			});
		case "Snow":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneSnow, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneSnow);
			});
		case "Desert":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneDesert, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneDesert);
			});
		case "Dungeon":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneDungeon, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneDungeon);
			});
		case "Jungle Temple":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneLihzhardTemple, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneLihzhardTemple);
			});
		case "Underground Desert":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneUndergroundDesert, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneUndergroundDesert);
			});
		case "Hive":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneHive, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneHive);
			});
		case "Aether":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneShimmer, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneShimmer);
			});
		case "Gem Cave":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneGemCave, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneGemCave);
			});
		case "Marble":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneMarble, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneMarble);
			});
		case "Granite":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneGranite, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneGranite);
			});
		case "Meteor":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneMeteor, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneMeteor);
			});
		case "Graveyard":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneGraveyard, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneGraveyard);
			});
		case "Rain":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneRain, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneRain);
			});
		case "Sandstorm":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneSandstorm, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneSandstorm);
			});
		case "Overworld":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneOverworldHeight, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneOverworldHeight);
			});
		case "Underground":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneDirtLayerHeight, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneDirtLayerHeight);
			});
		case "Caverns":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneRockLayerHeight, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneRockLayerHeight);
			});
		case "Underworld":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneUnderworldHeight, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneUnderworldHeight);
			});
		case "Space":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneSkyHeight, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneSkyHeight);
			});
		case "Beach":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneBeach, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneBeach);
			});
		case "Solar Tower":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneTowerSolar, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneTowerSolar);
			});
		case "Vortex Tower":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneTowerVortex, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneTowerVortex);
			});
		case "Nebula Tower":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneTowerNebula, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneTowerNebula);
			});
		case "Stardust Tower":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneTowerStardust, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneTowerStardust);
			});
		case "Old One's Army":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneOldOnesArmy, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneOldOnesArmy);
			});
		case "Water Candle":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneWaterCandle, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneWaterCandle);
			});
		case "Peace Candle":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZonePeaceCandle, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZonePeaceCandle);
			});
		case "Shadow Candle":
			return MenuControls.MakeToggle(cheat, color, () => SpoofedZones.ZoneShadowCandle, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref SpoofedZones.ZoneShadowCandle);
			});
		default:
			return null;
		}
	}

	/// <summary>Hover text of a row of this category (null: none).</summary>
	internal static string Tooltip(string name)
	{
		return name switch
		{
			"Enforce Only Overrides" => "Disables vanilla biome behavior in favor of only using the enabled overrides.\nNote that most of these will do nothing on servers.",
			_ => null,
		};
	}
}
