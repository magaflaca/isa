using System.Collections.Generic;
using IsaClient.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>Your own Lucy lines: write one and add it, try one, take one out. The same list as .lucy add / lines / remove.</summary>
public static class HubLucy
{
	private static HubTheme Theme => HubTheme.Current;

	private const int RowH = 40;

	private static string _draft = "";

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y = Note(sb, "Lines Lucy says for you when Lines is Your lines or Both. IsaClient players see them over the one she talks over, in her color and voice; players without IsaClient see one of her own lines instead (if you want).", x, y, width) + 12;
		// A new line.
		Rectangle field = new Rectangle(x, y, width - 96, 32);
		_draft = HubKit.Field(sb, "lucy:new", field, _draft, "Something for Lucy to say...", 80, HubIcon.Item(I.LucyTheAxe));
		bool canAdd = !string.IsNullOrWhiteSpace(_draft);
		if (HubKit.Button(sb, "lucy:add", new Rectangle(field.Right + 6, y, 90, 32), "Add", new Color(60, 140, 80), "Adds it to your lines.", canAdd))
		{
			LucyChorus.SetCustomLines(LucyChorus.CustomLines.Concat1(_draft));
			_draft = "";
		}
		y += 40;
		List<string> lines = LucyChorus.CustomLines;
		HubKit.Text(sb, "Your lines", x, y, Theme.Muted, 0.72f);
		HubKit.Chip(sb, lines.Count.ToString(), x + (int)HubKit.Measure(IsaClient.Languages.L10n.T("Your lines"), 0.72f) + 8, y - 2, Color.Lerp(Theme.Slot, Theme.Accent, 0.3f), Color.White, 0.66f);
		y += 22;
		if (lines.Count == 0)
		{
			return Note(sb, "No lines yet: write one above.", x, y, width) - start;
		}
		int remove = -1;
		for (int i = 0; i < lines.Count; i++)
		{
			Rectangle row = new Rectangle(x, y, width, RowH);
			HubKit.Box(sb, row, Theme.Card * HubPrefs.Opacity);
			HubKit.RawText(sb, lines[i], row.X + 12, row.Y + 11, LucyChorus.LucyColor, 0.8f, width - 200);
			if (HubKit.Button(sb, "lucy:del:" + i, new Rectangle(row.Right - 86, row.Y + 6, 78, 28), "Remove", new Color(150, 58, 58), null))
			{
				remove = i;
			}
			if (HubKit.Button(sb, "lucy:try:" + i, new Rectangle(row.Right - 170, row.Y + 6, 78, 28), "Say it", Theme.Slot, "Lucy says it over the one in Over Whom (IsaClient players see it).", !Main.gameMenu))
			{
				LucyChorus.SayNow(lines[i]);
			}
			y = row.Bottom + 6;
		}
		if (remove >= 0)
		{
			lines.RemoveAt(remove);
			LucyChorus.SetCustomLines(lines);
		}
		return y - start;
	}

	private static List<string> Concat1(this List<string> list, string more)
	{
		List<string> all = new List<string>(list) { more.Trim() };
		return all;
	}

	private static int Note(SpriteBatch sb, string text, int x, int y, int width)
	{
		string[] wrapped = HubKit.Wrap(text, width - 28, 0.7f, 6);
		Rectangle r = new Rectangle(x, y, width, 18 + wrapped.Length * 18);
		HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
		HubKit.RawText(sb, string.Join("\n", wrapped), r.X + 14, r.Y + 9, Theme.Muted, 0.7f);
		return r.Bottom;
	}
}
