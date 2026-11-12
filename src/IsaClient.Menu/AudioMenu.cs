using System.Collections.Generic;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.UI;

namespace IsaClient.Menu;

/// <summary>Cheats → Audio Tweaks: swap music, forced soundtrack and the classic/disabled themes.</summary>
internal static class AudioMenu
{
	internal static UIElement Create(string cheat, Color color)
	{
		switch (cheat)
		{
		case "Swap Music":
			return MenuControls.MakeToggle(cheat, color, () => Main.swapMusic, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Main.swapMusic);
			});
		case "Force Soundtrack":
			return MenuControls.MakeCycle(cheat, color, delegate(bool back)
			{
				AudioTweaks.ForceSoundtrackId = MenuControls.Cycle(AudioTweaks.ForceSoundtrackId, 0, 2, back);
			});
		case "Revert Jungle Night Theme":
			return MenuControls.MakeToggle(cheat, color, () => AudioTweaks.OldMus_JungleNight, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AudioTweaks.OldMus_JungleNight);
			});
		case "Revert Underground Jungle Theme":
			return MenuControls.MakeToggle(cheat, color, () => AudioTweaks.OldMus_UndergroundJungle, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AudioTweaks.OldMus_UndergroundJungle);
			});
		case "Classic Dungeon Theme":
			return MenuControls.MakeToggle(cheat, color, () => AudioTweaks.OldMus_Dungeon, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AudioTweaks.OldMus_Dungeon);
			});
		case "Classic Underworld Theme":
			return MenuControls.MakeToggle(cheat, color, () => AudioTweaks.OldMus_Underworld, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AudioTweaks.OldMus_Underworld);
			});
		case "Revert Underground Desert Theme":
			return MenuControls.MakeToggle(cheat, color, () => AudioTweaks.OldMus_UndergroundDesert, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AudioTweaks.OldMus_UndergroundDesert);
			});
		case "Revert Duke Fishron Theme":
			return MenuControls.MakeToggle(cheat, color, () => AudioTweaks.OldMus_Fishron, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AudioTweaks.OldMus_Fishron);
			});
		case "Classic Goblin Army Theme":
			return MenuControls.MakeToggle(cheat, color, () => AudioTweaks.OldMus_Goblins, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AudioTweaks.OldMus_Goblins);
			});
		case "Classic Pirate Invasion Theme":
			return MenuControls.MakeToggle(cheat, color, () => AudioTweaks.OldMus_Pirates, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AudioTweaks.OldMus_Pirates);
			});
		case "Disable Morning Rain Theme":
			return MenuControls.MakeToggle(cheat, color, () => AudioTweaks.OldMus_MorningRain, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AudioTweaks.OldMus_MorningRain);
			});
		case "Disable Storm Theme":
			return MenuControls.MakeToggle(cheat, color, () => AudioTweaks.OldMus_Storm, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AudioTweaks.OldMus_Storm);
			});
		case "Disable Windy Day Theme":
			return MenuControls.MakeToggle(cheat, color, () => AudioTweaks.OldMus_WindyDay, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AudioTweaks.OldMus_WindyDay);
			});
		case "Disable Slime Rain Theme":
			return MenuControls.MakeToggle(cheat, color, () => AudioTweaks.OldMus_SlimeRain, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AudioTweaks.OldMus_SlimeRain);
			});
		case "Disable Graveyard Theme":
			return MenuControls.MakeToggle(cheat, color, () => AudioTweaks.OldMus_Graveyard, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AudioTweaks.OldMus_Graveyard);
			});
		case "Disable Town Themes":
			return MenuControls.MakeToggle(cheat, color, () => AudioTweaks.OldMus_NoTown, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref AudioTweaks.OldMus_NoTown);
			});
		case "Disable & Reset All":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				AudioTweaks.DisableAudioTweaks();
			});
		default:
			return new UIElement();
		}
	}
	internal static readonly List<string> Entries = new List<string>
	{
		"Swap Music", "Force Soundtrack", "Revert Jungle Night Theme", "Revert Underground Jungle Theme", "Classic Dungeon Theme", "Classic Underworld Theme", "Revert Underground Desert Theme", "Revert Duke Fishron Theme", "Classic Goblin Army Theme", "Classic Pirate Invasion Theme",
		"Disable Morning Rain Theme", "Disable Storm Theme", "Disable Windy Day Theme", "Disable Slime Rain Theme", "Disable Graveyard Theme", "Disable Town Themes", "Disable & Reset All"
	};

	/// <summary>Hover text of a row of this category (null: none).</summary>
	internal static string Tooltip(string name)
	{
		return name switch
		{
			"Swap Music" => "Temporarily change the current soundtrack in use until you exit the world.",
			"Force Soundtrack" => "Force a specific soundtrack to always play, disregarding Drunk worlds & Swap Music.",
			"Revert Jungle Night Theme" => "Reverts the Jungle Night theme to the Jungle theme,\nrestoring pre-1.4 behavior.",
			"Revert Underground Jungle Theme" => "Reverts the Underground Jungle theme to the Jungle theme,\nrestoring pre-1.4 behavior.",
			"Classic Dungeon Theme" => "Reverts the Dungeon theme to the Eerie theme,\nrestoring pre-1.2 behavior.",
			"Classic Underworld Theme" => "Reverts the Underworld theme to the Eerie theme,\nrestoring pre-1.3 behavior.",
			"Revert Underground Desert Theme" => "Reverts the Underground Desert theme to the Desert theme,\nrestoring pre-1.4 behavior.",
			"Classic Goblin Army Theme" => "Reverts the Goblin Invasion theme to the Boss 1 theme,\nrestoring pre-1.3 behavior.",
			"Classic Pirate Invasion Theme" => "Reverts the Pirate Invasion theme to the Bos 1 theme,\nrestoring pre-1.3 behavior.",
			"Disable Morning Rain Theme" => "Disables the Morning Rain theme, so only the normal Rain theme will play,\nrestoring pre-1.4 behavior.",
			"Disable Storm Theme" => "Disables the Storm theme, so only the normal Rain theme will play,\nrestoring pre-1.4 behavior.",
			"Disable Windy Day Theme" => "Disables the Windy Day theme, instead playing either of the two normal Day themes,\nrestoring pre-1.4 behavior.",
			"Disable Slime Rain Theme" => "Disables the Slime Rain theme, instead playing whatever is more appropriate for the current scenario,\nrestoring pre-1.4 behavior.",
			"Disable Graveyard Theme" => "Disables the Graveyard theme, instead playing whatever is more appropriate for the current scenario,\nrestoring pre-1.4 behavior.",
			"Disable Town Themes" => "Disables the Town Day & Town Night themes, instead playing whatever is more appropriate for the current scenario,\nrestoring pre-1.4 behavior.",
			"Revert Duke Fishron Theme" => "Reverts the Duke Fishron theme to the Boss 1 theme,\nrestoring pre-1.3 behavior.",
			_ => null,
		};
	}
}
