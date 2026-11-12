using System.Collections.Generic;

namespace IsaClient.Menu;

/// <summary>
/// How rows are laid out in a category box. By default two rows share a line; the rows listed here take the whole
/// width (long values, sliders) or half the width, centered (the "Disable All" style buttons).
/// </summary>
internal static class PanelLayout
{
	private static readonly HashSet<string> FullWidth = new HashSet<string>
	{
		// Travel, World Edit, Render, Misc
		"Rapid Position Updates", "Noclip Speed", "Packet Mode", "Tile & Wall Packet Rate", "Erase Packet Rate", "Liquid Packet Rate", "Paint Packet Rate",
		"Bypass FPS & Timeout Protections", "Minimum Zoom", "Global Brightness", "Selection Mode", "Clear Copied Tiles", "Fill Selection",
		"Remove From Selection", "Paste Mode", "Auto-Exit Paste", "Brush Shape", "Tile Brush", "Tile Brush Size", "Erase Brush", "Erase Brush Size",
		"Wall Brush", "Wall Brush Size", "Liquid Brush", "Liquid Brush Size", "Super Paint Brush", "Super Paint Brush Size", "Special Paint Pattern",
		"Special Paint Patterns 1-3 Use Deep Paint", "Brush Pattern", "World Border Size", "Reveal Map", "Kill Aura", "Disable All", "Disable & Reset All",
		// Held Item / Item Mod Presets: values and sliders
		"Item Name", "Prefix", "Damage", "Crit Chance", "Knockback", "Scale", "Mana Cost", "Use Time", "Animation Time", "Projectile", "Use Ammo",
		"Shoot Speed", "Shoot Style", "Times To Shoot", "Place Style", "Heal Life", "Heal Mana", "Pickaxe Power", "Axe Power", "Hammer Power",
		"Hits Invincible NPCs", "Current Preset", "Preset Author", "Preset Tied To Character Name", "Apply Preset", "Save Item Mod", "Create New Preset",
		// buttons with long labels, sliders, values
		"Save Copied Tiles as Schematic", "Load Tiles From Schematic", "Cancel Fill/Delete Action", "Cancel Paste", "Load Image (Draw Photo)", "Image Scale",
		"Undo Last Paste", "Free Camera Speed", "Use Windows Cursor", "Now Playing", "Music Volume", "Auto Clicker Speed", "Auto Clicker Hold/Release",
		"Auto Clicker Timer", "Item Drop Mode", "Buff Loss Mode", "Force Dash Type", "Luck Override", "Suppress Creative Shock Debuff", "AI Model",
		"AI Temperature", "AI Reply Delay", "AI Cooldown", "AI Context Messages", "Edit AI Config (Prompt, About Me, Key...)",
		// Travel/Render extras and World Information values
		"Surface Friction", "Conveyer Belt Handling", "Hitbox Style", "Dust Level", "Disable Invisibility on Players", "World Name", "World Size",
		"World ID", "World GUID", "World Generator Version", "Seed", "Hardmode", "World Infection", "Spawn Alternate Evil Boss", "Spawn Wall of Flesh",
		"Spawn Mechdusa", "Clouds", "Rock Layer", "World Surface", "Spawn Position", "Dungeon Position", "Generate Hardmode Biomes"
	};

	private static readonly HashSet<string> HalfWidthCentered = new HashSet<string> { "Reset All", "Disable All", "Disable & Reset All", "Delete Current Preset" };

	/// <summary>Half the width, centered (checked before <see cref="IsFullWidth"/>).</summary>
	public static bool IsHalfWidthCentered(string row)
	{
		return HalfWidthCentered.Contains(row);
	}

	public static bool IsFullWidth(string row)
	{
		return FullWidth.Contains(row);
	}

	/// <summary>True when the row never shares its line.</summary>
	public static bool HasOwnLine(string row)
	{
		return IsFullWidth(row) || IsHalfWidthCentered(row);
	}
}
