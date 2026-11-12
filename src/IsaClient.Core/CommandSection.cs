using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace IsaClient.Core;

/// <summary>
/// A group of chat commands in .help (".help 3" or ".help combat"). Each command picks its section when it is
/// registered (<see cref="CommandInfo.Help"/>); <see cref="All"/> is the order .help lists them in.
/// </summary>
public sealed class CommandSection
{
	public static readonly CommandSection General = new CommandSection("general", "General", new Color(255, 255, 255), "help, panel, settings, text color, command prefix, abbreviations");

	public static readonly CommandSection Player = new CommandSection("player", "Player", new Color(110, 230, 120), "god mode, infinite mana/ammo, dodge, ghost, difficulty, character");

	public static readonly CommandSection Items = new CommandSection("items", "Items", new Color(255, 170, 70), "spawn items and buffs, held item stats, item tweaks");

	public static readonly CommandSection Movement = new CommandSection("movement", "Movement", new Color(90, 210, 255), "noclip, teleports, free camera, reach");

	public static readonly CommandSection Combat = new CommandSection("combat", "Combat & NPCs", new Color(255, 95, 95), "kill aura, butcher, projectiles, spawn and control NPCs");

	public static readonly CommandSection World = new CommandSection("world", "World", new Color(215, 175, 120), "map reveal, world download, chests, seed, world tools");

	public static readonly CommandSection Building = new CommandSection("building", "World Edit", new Color(255, 225, 90), "brushes, copy/paste, schematics, pictures, paint, packet rates");

	public static readonly CommandSection Render = new CommandSection("render", "Render & Cosmetic", new Color(225, 140, 255), "fullbright, zoom, shaders, rainbow effects, anti-lag");

	public static readonly CommandSection Info = new CommandSection("info", "Info & Server", new Color(120, 160, 255), "player list, IDs and data overlays, server detection, rejoin, proxy");

	public static readonly CommandSection Chat = new CommandSection("chat", "Chat", new Color(180, 235, 235), "chat color, translation, chat spam, anti-spam, chat box");

	public static readonly CommandSection Music = new CommandSection("music", "Music", new Color(255, 120, 190), "play songs and links, queue, volume, music window");

	public static readonly CommandSection Bots = new CommandSection("bots", "Bots & AI", new Color(80, 225, 190), "extra clients, possession, AFK AI assistant");

	public static readonly CommandSection Menus = new CommandSection("menus", "Menus Anywhere", new Color(240, 200, 150), "piggy bank, safe, forges, stylist, reforge, Journey menu");

	public static readonly CommandSection Plugins = new CommandSection("plugins", "Plugins", new Color(170, 200, 255), "plugin overlay, plugin console, commands added by plugins");

	/// <summary>Every section, in .help order (".help 1" is the first).</summary>
	public static readonly IReadOnlyList<CommandSection> All = new[] { General, Player, Items, Movement, Combat, World, Building, Render, Info, Chat, Music, Bots, Menus, Plugins };

	private CommandSection(string key, string title, Color color, string summary)
	{
		Key = key;
		Title = title;
		Color = color;
		Summary = summary;
	}

	/// <summary>One word to open it: ".help combat".</summary>
	public string Key { get; }

	public string Title { get; }

	public Color Color { get; }

	/// <summary>What is inside, shown in the .help overview.</summary>
	public string Summary { get; }

	/// <summary>1-based position in <see cref="All"/>.</summary>
	public int Number => ((IList<CommandSection>)All).IndexOf(this) + 1;

	/// <summary>The section for "3", "combat" or "Combat & NPCs"; null when nothing matches.</summary>
	public static CommandSection Find(string text)
	{
		if (int.TryParse(text, out int number))
		{
			return (number >= 1 && number <= All.Count) ? All[number - 1] : null;
		}
		foreach (CommandSection section in All)
		{
			if (string.Equals(section.Key, text, StringComparison.OrdinalIgnoreCase) || string.Equals(section.Title, text, StringComparison.OrdinalIgnoreCase))
			{
				return section;
			}
		}
		return null;
	}
}
