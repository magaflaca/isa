using System;
using System.Linq;
using IsaClient.Assistant;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// AI Assistant > Names: the names the AI answers to. Your character's name always counts; the saved nicknames
/// (IsaAI.json "Nicknames") are added and removed here, and the ".ai nick" ones of this session can be kept.
/// </summary>
internal static class HubAiNames
{
	private const int ChipHeight = 30;

	private static string _typed = "";

	private static HubTheme Theme => HubTheme.Current;

	/// <summary>"Isa, isita, isa123" for the card on the main page.</summary>
	public static string Summary()
	{
		string own = Main.LocalPlayer?.name;
		string[] names = new[] { own }.Concat(AiAssistant.Nicknames).Concat(AiAssistant.SessionNicknames).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		return (names.Length == 0) ? "Your name" : string.Join(", ", names);
	}

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		HubIcon.Item(I.Megaphone).Draw(sb, new Rectangle(x + 2, y + 2, 22, 22), Color.White);
		HubKit.Text(sb, "Names it answers to", x + 30, y + 4, Theme.Accent, 0.92f);
		string[] intro = HubKit.Wrap("When a chat message mentions one of these names, the AI replies for you. Your character's name always counts. Separate several with commas.", width - 34, 0.72f, 3);
		for (int i = 0; i < intro.Length; i++)
		{
			HubKit.Text(sb, intro[i], x + 30, y + 28 + i * 18, Theme.Muted, 0.72f);
		}
		y += 36 + intro.Length * 18 + 4;

		// Add
		int buttonW = 90;
		Rectangle field = new Rectangle(x, y, Math.Min(360, width - buttonW - 8), 32);
		_typed = HubKit.Field(sb, "ai:nick", field, _typed, "A nickname, or several: isa, isita", 120, HubIcon.Item(I.Sign));
		bool enter = HubInput.Focus == "ai:nick" && Main.inputTextEnter;
		bool valid = !string.IsNullOrWhiteSpace(_typed);
		if ((HubKit.Button(sb, "ai:nick:add", new Rectangle(field.Right + 8, y, buttonW, 32), "Add", Theme.On * 0.6f, null, valid) || enter) && valid)
		{
			int added = AiAssistant.AddNicknames(_typed);
			HubInput.Sound(added > 0 ? 10 : 11);
			_typed = "";
			Main.clrInput();
		}
		y += 44;

		// The names
		int cx = x;
		int cy = y;
		string own = Main.LocalPlayer?.name;
		if (!string.IsNullOrWhiteSpace(own))
		{
			Chip(sb, "ai:own", own, "character", Theme.Accent, false, null, ref cx, ref cy, x, width);
		}
		foreach (string nick in AiAssistant.Nicknames.ToList())
		{
			if (Chip(sb, "ai:n:" + nick, nick, null, Color.White, true, "Remove " + nick, ref cx, ref cy, x, width))
			{
				AiAssistant.RemoveNickname(nick);
				HubInput.Sound(11);
			}
		}
		foreach (string nick in AiAssistant.SessionNicknames.ToList())
		{
			if (Chip(sb, "ai:s:" + nick, nick, "this session", Theme.Muted, true, "Keep " + nick + " for next time (click the star)", ref cx, ref cy, x, width, keep: true))
			{
				AiAssistant.AddNicknames(nick);
				HubInput.Sound(10);
			}
		}
		y = cy + ChipHeight + 10;
		if (AiAssistant.Nicknames.Count == 0)
		{
			HubKit.Text(sb, "No nicknames yet: it only answers to your character's name.", x, y, Theme.Muted, 0.72f);
			y += 24;
		}
		return y - start;
	}

	/// <summary>One name in a box (with a remove or keep button); flows to the next line when the row is full.</summary>
	private static bool Chip(SpriteBatch sb, string id, string name, string tag, Color color, bool button, string tooltip, ref int cx, ref int cy, int x, int width, bool keep = false)
	{
		float textW = HubKit.Measure(name, 0.8f);
		float tagW = (tag != null) ? HubKit.Measure(tag, 0.62f) + 10f : 0f;
		int w = (int)(textW + tagW + (button ? 44 : 22));
		w = Math.Min(w, width);
		if (cx > x && cx + w > x + width)
		{
			cx = x;
			cy += ChipHeight + 8;
		}
		Rectangle r = new Rectangle(cx, cy, w, ChipHeight);
		cx += w + 8;
		bool hover = HubInput.In(r);
		HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.CardHover, hover ? 0.6f : 0f) * HubPrefs.Opacity);
		HubKit.Text(sb, name, r.X + 11, r.Y + 7, color, 0.8f, textW + 2f);
		if (tag != null)
		{
			HubKit.Text(sb, tag, r.X + 11 + textW + 8, r.Y + 10, Theme.Muted, 0.62f);
		}
		if (!button)
		{
			return false;
		}
		Rectangle icon = new Rectangle(r.Right - 24, r.Y + (ChipHeight - 16) / 2, 16, 16);
		HubIcon texture = HubIcon.Texture(keep ? "Images/UI/ButtonFavoriteInactive" : "Images/UI/SearchCancel");
		return HubKit.BareIcon(sb, id, icon, texture, tooltip, Color.White);
	}
}
