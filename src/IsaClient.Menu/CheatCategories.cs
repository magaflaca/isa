using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace IsaClient.Menu;

/// <summary>
/// Every category of the panel. To add a row: put its name in the category's Entries and build it in the category's
/// Create (and give it a tooltip in its Tooltip). To add a category: write its class and add it here.
/// </summary>
internal static class CheatCategories
{
	/// <summary>The Cheats tab, in display order.</summary>
	public static readonly CheatCategory[] CheatsTab =
	{
		new CheatCategory("Player", Color.LimeGreen, PlayerMenu.Entries, PlayerMenu.Create),
		new CheatCategory("Travel", Color.Goldenrod, TravelMenu.Entries, TravelMenu.Create),
		new CheatCategory("Informational", Color.HotPink, InfoMenu.Entries, InfoMenu.Create),
		new CheatCategory("Render", Color.AliceBlue, RenderMenu.Entries, RenderMenu.Create),
		new CheatCategory("Cosmetic", Color.Violet, CosmeticMenu.Entries, CosmeticMenu.Create),
		new CheatCategory("Miscellaneous", Color.Turquoise, MiscMenu.Entries, MiscMenu.Create),
		new CheatCategory("World Edit", Color.Coral, WorldEditMenu.Entries, WorldEditMenu.Create),
		new CheatCategory("Held Item", Color.OrangeRed, HeldItemMenu.Entries, HeldItemMenu.Create),
		new CheatCategory("Item Mod Presets", Color.Gold, HeldItemMenu.PresetEntries, HeldItemMenu.CreatePreset),
		new CheatCategory("Biome Spoofers", Color.ForestGreen, BiomeSpooferMenu.Entries, BiomeSpooferMenu.Create),
		new CheatCategory("Audio Tweaks", Color.Lavender, AudioMenu.Entries, AudioMenu.Create),
		new CheatCategory("Music", Color.Orchid, MusicMenu.Entries, MusicMenu.Create),
		new CheatCategory("AI", Color.DeepSkyBlue, AiMenu.Entries, AiMenu.Create),
	};

	private static readonly Color[] WorldInfoTints =
	{
		Color.SkyBlue, Color.Goldenrod, Color.Crimson, Color.Coral, Color.HotPink, Color.Turquoise, Color.MediumSlateBlue
	};

	/// <summary>The World Information tab, in display order.</summary>
	public static readonly CheatCategory[] WorldInfoTab = Enumerable.Range(0, WorldInfoMenu.SectionEntries.Length)
		.Select(section => new CheatCategory(WorldInfoMenu.SectionTitles[section], WorldInfoTints[section], WorldInfoMenu.SectionEntries[section],
			(cheat, color) => WorldInfoMenu.Create(cheat, color, section), header => WorldInfoMenu.DecorateHeader(header, section)))
		.ToArray();

	/// <summary>Every row name of every category.</summary>
	public static IEnumerable<string> AllRows => CheatsTab.Concat(WorldInfoTab).SelectMany(category => category.Entries);
}
