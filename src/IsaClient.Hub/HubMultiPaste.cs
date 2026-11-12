using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Building;
using IsaClient.MultiClient;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// Experimental > Multi Paste, under its switch: how it's going, which bots paste (a default set, or the ones here),
/// whether you paste too, the plumber outfit, and each bot's packet rate.
/// </summary>
internal static class HubMultiPaste
{
	private static readonly Color Green = new Color(56, 128, 84);

	private static readonly Color Red = new Color(150, 58, 58);

	private static readonly Color Gold = new Color(255, 205, 90);

	private static HubTheme Theme => HubTheme.Current;

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y += Status(sb, x, y, width) + 10;
		y += HubBotSets.Switches(sb, x, y, width, new (string, string, string, bool, Action)[]
		{
			("mp:here", "Use the bots already here", "No set is called: the bots on this server paste with you", MultiPaste.UseBotsHere, () => MultiPaste.UseBotsHere = !MultiPaste.UseBotsHere),
			("mp:me", "I paste too", "You take a strip as well (the one under you)", MultiPaste.YouPasteToo, () => MultiPaste.YouPasteToo = !MultiPaste.YouPasteToo),
			("mp:plumber", "Plumber outfit", "While they paste: plumber's clothes with a simple random dye; their own " + MultiPaste.OutfitSeconds.ToString("0") + " s after", MultiPaste.Plumbers, () => MultiPaste.Plumbers = !MultiPaste.Plumbers)
		}) + 10;
		y += BuffRow(sb, x, y, width) + 16;
		if (!MultiPaste.UseBotsHere)
		{
			y += DefaultSet(sb, x, y, width) + 16;
		}
		y += Pacing(sb, x, y, width) + 16;
		y += Bots(sb, x, y, width);
		return y - start;
	}

	private static int Status(SpriteBatch sb, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 70);
		bool on = MultiPaste.Enabled;
		PasteGroup group = MultiPaste.Running;
		HubKit.Box(sb, r, (on ? Color.Lerp(Theme.Card, new Color(30, 90, 60), 0.35f) : Theme.Card) * HubPrefs.Opacity);
		Rectangle icon = new Rectangle(r.X + 12, r.Y + 11, 48, 48);
		HubKit.Box(sb, icon, Theme.Sidebar * 0.9f);
		HubIcon.Item(I.PlumbersHat).Draw(sb, new Rectangle(icon.X + 8, icon.Y + 8, 32, 32), Color.White);
		int tx = icon.Right + 14;
		int room = r.Width - (tx - r.X) - 150;
		string title;
		string line;
		if (group != null)
		{
			title = "Pasting together";
			line = group.Done + "/" + group.Jobs.Count + " strips done, " + (DateTime.UtcNow - group.Started).TotalSeconds.ToString("0") + " s";
		}
		else if (on)
		{
			int ready = MultiPaste.Helpers().Count;
			title = "On: your building is shared out";
			line = ready + ((ready == 1) ? " bot ready" : " bots ready") + (MultiPaste.YouPasteToo ? " + you" : "") + ((ready == 0) ? ". Until one is, you build alone." : ". Build as usual.");
		}
		else
		{
			title = "Off";
			line = "Turn it on and your pastes, undos, brushes, fills, erases and conversions are cut in strips: you and your bots do them at the same time (TShock).";
		}
		HubKit.Text(sb, title, tx, r.Y + 12, on ? Color.White : Theme.Text, 0.86f, room);
		HubKit.Text(sb, line, tx, r.Y + 38, Theme.Muted, 0.62f, room);
		Rectangle button = new Rectangle(r.Right - 136, r.Y + 18, 124, 34);
		if (HubKit.Button(sb, "mp:toggle", button, on ? "Turn off" : "Turn on", on ? Red : Green, on ? "You paste alone again" : "Your bots paste with you", true, HubIcon.Item(on ? I.TrashCan : I.PlumbersHat)))
		{
			MultiPaste.SetEnabled(!on);
		}
		return r.Height;
	}

	/// <summary>The buff building bots show (230, the Witch's Broom, by default): its picture, its name, its number.</summary>
	private static int BuffRow(SpriteBatch sb, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 56);
		HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
		int buff = MultiPaste.PasteBuff;
		Rectangle icon = new Rectangle(r.X + 12, r.Y + 10, 36, 36);
		HubKit.Box(sb, icon, Theme.Sidebar * 0.9f);
		if (buff > 0 && buff < Terraria.ID.BuffID.Count)
		{
			try
			{
				sb.Draw(Terraria.GameContent.TextureAssets.Buff[buff].Value, new Rectangle(icon.X + 3, icon.Y + 3, 30, 30), Color.White);
			}
			catch
			{
			}
		}
		HubKit.Text(sb, "Their buff while Multi Paste is on", r.X + 60, r.Y + 7, Theme.Text, 0.74f, r.Width - 190);
		string name = (buff > 0 && buff < Terraria.ID.BuffID.Count) ? (Lang.GetBuffName(buff) + " (" + buff + ")") : IsaClient.Languages.L10n.T("None");
		HubKit.Text(sb, IsaClient.Languages.L10n.F("{0}: everyone sees it on them (a mount's buff puts them on the mount); 0 = none", name), r.X + 60, r.Y + 29, Theme.Muted, 0.58f, r.Width - 190);
		Rectangle reset = new Rectangle(r.Right - 222, r.Y + 13, 92, 30);
		if (buff != Terraria.ID.BuffID.WitchBroom && HubKit.Button(sb, "mp:buff:reset", reset, "Default", Theme.Slot, "Back to 230 (Witch's Broom)", true))
		{
			MultiPaste.SetPasteBuff(Terraria.ID.BuffID.WitchBroom);
		}
		string typed = HubBotSets.NumberField(sb, "mp:buff", new Rectangle(r.Right - 118, r.Y + 13, 106, 30), buff.ToString(), "230", 4, decimals: false);
		if (int.TryParse(typed, out int id) && id != buff && id >= 0 && id < Terraria.ID.BuffID.Count)
		{
			MultiPaste.SetPasteBuff(id);
		}
		return r.Height;
	}

	private static int DefaultSet(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y += HubBotSets.Sub(sb, HubIcon.Item(I.GoldCrown), "Default set", "Its bots join when you turn it on, do their steps and come to you", x, y, width);
		int cx = x;
		int rowY = y;
		List<(string Id, string Name)> choices = new List<(string, string)> { ("", "None (bots here)") };
		choices.AddRange(BotSets.Sets.Select(s => (s.Id, string.IsNullOrWhiteSpace(s.Name) ? "Unnamed set" : s.Name)));
		foreach (var c in choices)
		{
			int w = (int)HubKit.Measure(c.Name, 0.62f) + 26;
			if (cx + w > x + width)
			{
				cx = x;
				rowY += 38;
			}
			if (HubBotSets.Chip(sb, "mp:set:" + c.Id, new Rectangle(cx, rowY, w, 32), c.Name, (MultiPaste.SetId ?? "") == c.Id))
			{
				MultiPaste.SetId = c.Id;
				MultiClientManager.Save();
			}
			cx += w + 6;
		}
		y = rowY + 40;
		BotSet set = MultiPaste.Set;
		if (set != null)
		{
			Rectangle r = new Rectangle(x, y, width, 52);
			HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
			HubIcon.Item(set.Icon).Draw(sb, new Rectangle(r.X + 12, r.Y + 10, 32, 32), Color.White);
			bool here = MultiClientManager.TryGetCurrentServer(out string host, out int port) && set.Port == port && Possession.SameHost(host, set.Host);
			string where = string.IsNullOrWhiteSpace(set.Host) ? "No server yet" : (set.Host + ":" + set.Port);
			HubKit.Text(sb, set.Name, r.X + 56, r.Y + 7, Theme.Text, 0.74f, r.Width - 220);
			HubKit.Text(sb, where + (here ? "" : "  (not where you are)") + "  ·  " + BotSets.Online(set) + " online, " + BotSets.Waiting(set) + " joining", r.X + 56, r.Y + 28, here ? Theme.Muted : Gold, 0.6f, r.Width - 220);
			if (HubKit.Button(sb, "mp:call", new Rectangle(r.Right - 150, r.Y + 9, 138, 34), "Call them now", Green, "Its members not here yet join", here, HubIcon.Item(I.RecallPotion)))
			{
				BotSets.Start(set);
			}
			y += r.Height;
		}
		return y - start;
	}

	private static int Pacing(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y += HubBotSets.Sub(sb, HubIcon.Item(I.Stopwatch), "Bots' speed", "For every packet they send: tiles, walls, liquids, paint, erases (yours stay as in World Edit)", x, y, width);
		int half = (width - 6) / 2;
		if (HubBotSets.Chip(sb, "mp:mode:100", new Rectangle(x, y, half, 32), "Packet 100/ms: its .pl packets, then 100 ms", !MultiPaste.BotMaxMode))
		{
			MultiPaste.BotMaxMode = false;
			MultiClientManager.Save();
		}
		if (HubBotSets.Chip(sb, "mp:mode:max", new Rectangle(x + half + 6, y, half, 32), "Packet Maximum: its .pl packets, then 500 ms", MultiPaste.BotMaxMode))
		{
			MultiPaste.BotMaxMode = true;
			MultiClientManager.Save();
		}
		y += 40;
		y += HubBotSets.Stepper(sb, "mp:rate", "Every bot's .pl", "The same for all of them: packets (tiles, walls, liquids, paint, erases) before each pause. Below, one bot at a time", MultiPaste.DefaultRate, 1f, 1000f, 1f, v => MultiPaste.SetAllRates((int)v), x, y, width);
		return y - start;
	}

	private static int Bots(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y += HubBotSets.Sub(sb, HubIcon.Item(I.RobotHat), "Each bot", "Its own .pl, and how it is", x, y, width);
		List<ClientSession> bots = MultiClientManager.All.Where(MultiPaste.IsCandidate).ToList();
		if (bots.Count == 0)
		{
			y += HubBotSets.Hint(sb, x, y, width, MultiPaste.UseBotsHere || MultiPaste.Set == null ? "No bots yet: add some in Bots, or pick a default set above." : "None of the set's bots is here yet: turn Multi Paste on (or Call them now) and they join.");
			return y - start;
		}
		foreach (ClientSession s in bots)
		{
			string name = s.Name;
			y += HubBotSets.Stepper(sb, "mp:bot:" + name, name, MultiPaste.StatusOf(s) + (MultiPaste.Rates.ContainsKey(name) ? "" : "  ·  default .pl"), MultiPaste.RateOf(s), 1f, 1000f, 1f, v => MultiPaste.SetRate(name, (int)v), x, y, width) + 6;
		}
		return y - start;
	}
}
