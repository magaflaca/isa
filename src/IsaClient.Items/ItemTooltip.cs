using System;
using System.Globalization;
using System.Text;
using IsaClient.Community;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;

namespace IsaClient.Items;

/// <summary>The lines IsaClient items add at the end of their tooltip: their text, who made them, how others see them.</summary>
public static class ItemTooltip
{
	/// <summary>Set while the editor reads the game's own info of an item (no custom info then).</summary>
	public static bool GameInfoOnly;

	/// <summary>What the custom info can show, for the editor's help.</summary>
	public static readonly string[] Placeholders =
	{
		"{name}", "{damage}", "{crit}", "{knockback}", "{usetime}", "{useanimation}", "{mana}", "{defense}", "{velocity}",
		"{shots}", "{heal}", "{healmana}", "{pick}", "{axe}", "{hammer}", "{range}", "{rarity}", "{stack}", "{maker}", "{info}"
	};

	public static void Add(Item item, string[] lines, Color[] colors, ref int count)
	{
		if (item == null || item.IsAir || lines == null || GameInfoOnly)
		{
			return;
		}
		ItemMark mark = item.isaMark;
		if (mark != null && !string.IsNullOrEmpty(mark.Info) && colors != null)
		{
			ReplaceInfo(item, mark, lines, colors, ref count);
		}
		if (mark != null)
		{
			if (!string.IsNullOrWhiteSpace(mark.Description))
			{
				foreach (string line in mark.Description.Split('\n'))
				{
					if (!string.IsNullOrWhiteSpace(line) && count < lines.Length - 3)
					{
						lines[count++] = ChatColor.Colorize(line, new Color(215, 205, 240));
					}
				}
			}
			Color tag = TagArt.ColorOf(mark.Tag);
			string maker = mark.MadeByMe ? IsaClient.Languages.L10n.T("Made by you") : IsaClient.Languages.L10n.F("Made by {0}", mark.CreatorName);
			if (count < lines.Length - 2)
			{
				lines[count++] = ChatColor.Colorize(maker, Color.Lerp(tag, Color.White, 0.25f));
			}
			if (mark.NetId > 0 && mark.NetId < ItemID.Count && count < lines.Length - 1)
			{
				lines[count++] = ChatColor.Colorize(IsaClient.Languages.L10n.F("Others see it as {0}", Lang.GetItemNameValue(mark.NetId)), new Color(150, 160, 185));
			}
			return;
		}
		if ((item.type == ItemID.FlinxinatorStaff || item.type == ItemID.RubblemakerPots || ItemID.Sets.CustomClientItem[item.type]) && count < lines.Length - 1)
		{
			lines[count++] = ChatColor.Colorize(IsaClient.Languages.L10n.T("Taken from ZazaMod"), Color.Lerp(TagArt.ColorOf(TagArt.Green), Color.White, 0.25f));
		}
	}

	/// <summary>The item's info (every line under its name) becomes its mark's <see cref="ItemMark.Info"/>.</summary>
	private static void ReplaceInfo(Item item, ItemMark mark, string[] lines, Color[] colors, ref int count)
	{
		int game = count;
		string[] gameLines = new string[game];
		Color[] gameColors = new Color[game];
		Array.Copy(lines, gameLines, game);
		Array.Copy(colors, gameColors, game);
		count = 1;
		foreach (string raw in mark.Info.Split('\n'))
		{
			if (count >= lines.Length - 4)
			{
				break;
			}
			if (raw.Trim() == "{info}")
			{
				for (int i = 1; i < game && count < lines.Length - 4; i++)
				{
					lines[count] = gameLines[i];
					colors[count] = gameColors[i];
					count++;
				}
				continue;
			}
			lines[count] = Fill(item, mark, raw.TrimEnd('\r'));
			colors[count] = Color.White;
			count++;
		}
	}

	/// <summary>
	/// The game's info of an item as a starting point for custom info: its lines, with the numbers that have a
	/// placeholder (damage, crit, defense...) turned into it.
	/// </summary>
	public static string GameInfoTemplate(Item item)
	{
		string[] lines = new string[60];
		Color[] colors = new Color[60];
		int count = 1;
		int yoyo = -1;
		GameInfoOnly = true;
		try
		{
			Main.MouseText_DrawItemTooltip_GetLinesInfo(item, ref yoyo, item.knockBack, ref count, lines, colors);
		}
		catch
		{
			count = 1;
		}
		finally
		{
			GameInfoOnly = false;
		}
		ItemMark none = new ItemMark();
		StringBuilder b = new StringBuilder();
		for (int i = 1; i < count; i++)
		{
			string line = lines[i] ?? "";
			line = Swap(line, Fill(item, none, "{damage}"), "{damage}", true);
			line = Swap(line, Fill(item, none, "{crit}") + "%", "{crit}%", true);
			line = Swap(line, Fill(item, none, "{defense}"), "{defense}", true);
			line = Swap(line, " " + Fill(item, none, "{mana}") + " ", " {mana} ", false);
			line = Swap(line, Fill(item, none, "{pick}") + "%", "{pick}%", true);
			line = Swap(line, Fill(item, none, "{axe}") + "%", "{axe}%", true);
			line = Swap(line, Fill(item, none, "{hammer}") + "%", "{hammer}%", true);
			if (b.Length > 0)
			{
				b.Append('\n');
			}
			b.Append(line);
		}
		return b.ToString();
	}

	/// <summary>The first <paramref name="what"/> in a line (at its start only, if asked) becomes <paramref name="with"/>.</summary>
	private static string Swap(string line, string what, string with, bool atStart)
	{
		if (what.Trim().Length == 0 || what.Trim() == "0" || what.Trim() == "0%")
		{
			return line;
		}
		int at = line.IndexOf(what, StringComparison.Ordinal);
		if (at < 0 || (atStart && at != 0) || (atStart && line.Length > what.Length && char.IsDigit(line[what.Length])))
		{
			return line;
		}
		return line.Substring(0, at) + with + line.Substring(at + what.Length);
	}

	/// <summary>A line of custom info with its placeholders filled in (the numbers the game would show).</summary>
	public static string Fill(Item item, ItemMark mark, string line)
	{
		if (line.IndexOf('{') < 0)
		{
			return line;
		}
		Player p = Main.LocalPlayer;
		StringBuilder b = new StringBuilder(line);
		b.Replace("{name}", item.AffixName());
		b.Replace("{damage}", ((p != null) ? p.GetWeaponDamage(item) : item.damage).ToString());
		b.Replace("{crit}", ((p != null && item.damage > 0) ? p.GetWeaponCrit(item) : item.crit).ToString());
		b.Replace("{knockback}", Math.Round(item.knockBack, 1).ToString(CultureInfo.InvariantCulture));
		b.Replace("{usetime}", item.useTime.ToString());
		b.Replace("{useanimation}", item.useAnimation.ToString());
		b.Replace("{mana}", ((p != null) ? (int)((float)item.mana * p.manaCost) : item.mana).ToString());
		b.Replace("{defense}", item.defense.ToString());
		b.Replace("{velocity}", Math.Round(item.shootSpeed, 1).ToString(CultureInfo.InvariantCulture));
		b.Replace("{shots}", Math.Max(1, (int)item.timesToShoot).ToString());
		b.Replace("{heal}", item.healLife.ToString());
		b.Replace("{healmana}", item.healMana.ToString());
		b.Replace("{pick}", item.pick.ToString());
		b.Replace("{axe}", (item.axe * 5).ToString());
		b.Replace("{hammer}", item.hammer.ToString());
		b.Replace("{range}", item.tileBoost.ToString());
		b.Replace("{rarity}", item.rare.ToString());
		b.Replace("{stack}", item.stack.ToString());
		b.Replace("{maker}", mark.CreatorName ?? "");
		return b.ToString();
	}
}

/// <summary>The Journey menu's IsaClient tab: only your saved IsaClient items (My Items).</summary>
public sealed class JourneyFilter : Terraria.GameContent.Creative.IItemEntryFilter, IEntryFilter<Item>
{
	public bool FitsFilter(Item entry)
	{
		return entry != null && entry.isaMark != null;
	}

	public string GetDisplayNameKey()
	{
		return "IsaClient Items";
	}

	public UIElement GetImage()
	{
		Main.instance.LoadItem(ItemID.Safe);
		Texture2D texture = TextureAssets.Item[ItemID.Safe].Value;
		return new UIImageFramed(TextureAssets.Item[ItemID.Safe], texture.Frame())
		{
			HAlign = 0.5f,
			VAlign = 0.5f
		};
	}
}
