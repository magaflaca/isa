using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.MultiClient;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// Bots > Sets: teams of bots (your accounts) that join a server together and run steps when they arrive (/login,
/// chat, .pixie...). Cards for every set, an editor for the picked one (icon, server, members with their logins,
/// steps, options) and orders for all its bots at once. Also the batch bar of Sessions and the steps editor.
/// </summary>
internal static class HubBotSets
{
	private static readonly Color Green = new Color(56, 128, 84);

	private static readonly Color Red = new Color(150, 58, 58);

	private static readonly Color Gold = new Color(255, 205, 90);

	private static readonly Color Online = new Color(110, 220, 120);

	private static HubTheme Theme => HubTheme.Current;

	private static string _selected;

	private static string _numberBase = "";

	private static int _numberCount = 5;

	private static string _runLine = "";

	private static string _batchLine = "";

	private static string _batchTarget = "all";

	private static string _openMember;

	private static double _deleteArmedAt;

	private static bool _charactersLoaded;

	#region Page

	/// <summary>Sessions > Save as set: a new set with every bot you have now (it is picked in Sets).</summary>
	internal static void SaveAllAsSet()
	{
		BotSet set = BotSets.Create();
		set.Host = "";
		int added = BotSets.ImportRunning(set);
		if (!string.IsNullOrEmpty(set.Host) && MultiClientManager.TryGetCurrentServer(out string host, out int port) && set.Port == port && Possession.SameHost(host, set.Host) && !string.IsNullOrWhiteSpace(Main.worldName))
		{
			set.Name = (Main.worldName.Length > 24) ? Main.worldName.Substring(0, 24) : Main.worldName;
		}
		MultiClientManager.Save();
		_selected = set.Id;
		MultiClientManager.Announce("Saved " + added + ((added == 1) ? " bot" : " bots") + " as the set \"" + set.Name + "\" (Bots > Sets).");
	}

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		if (!_charactersLoaded)
		{
			_charactersLoaded = true;
			try
			{
				MultiClientManager.RefreshCharacters();
			}
			catch
			{
			}
		}
		if (MultiClientManager.TryGetCurrentServer(out string hereHost, out int herePort) && HubKit.Button(sb, "bs:here", new Rectangle(x + width - 170, y, 170, 32), "New set here", Green, "A set for " + hereHost + ":" + herePort + ", where you are", true, HubIcon.Item(I.WorldGlobe)))
		{
			BotSet here = BotSets.Create();
			here.Host = hereHost;
			here.Port = herePort;
			if (!string.IsNullOrWhiteSpace(Main.worldName))
			{
				here.Name = Main.worldName.Length > 24 ? Main.worldName.Substring(0, 24) : Main.worldName;
			}
			MultiClientManager.Save();
			_selected = here.Id;
		}
		y += Header(sb, HubIcon.Item(I.RobotHat), "Sets", "Your accounts as a team: they join together and do their steps", x, y, width - 180);
		y += Cards(sb, x, y, width) + 16;
		BotSet set = BotSets.Sets.FirstOrDefault(s => s.Id == _selected);
		if (set == null)
		{
			y += Hint(sb, x, y, width, BotSets.Sets.Count == 0 ? "Make your first set: + New set. Add your accounts with their passwords, write what they do when they arrive (/login {pass}, a hello...) and start them all with one click." : "Click a set to edit it.");
			return y - start;
		}
		y += Editor(sb, set, x, y, width);
		return y - start;
	}

	private static int Cards(SpriteBatch sb, int x, int y, int width)
	{
		int columns = (width >= 640) ? 2 : 1;
		const int gap = 8;
		int tileW = (width - (columns - 1) * gap) / columns;
		const int h = 86;
		int count = BotSets.Sets.Count + 1;
		for (int i = 0; i < count; i++)
		{
			Rectangle r = new Rectangle(x + i % columns * (tileW + gap), y + i / columns * (h + gap), tileW, h);
			if (!HubInput.Visible(r))
			{
				continue;
			}
			if (i == BotSets.Sets.Count)
			{
				NewCard(sb, r);
			}
			else
			{
				SetCard(sb, BotSets.Sets[i], r);
			}
		}
		int rows = (count + columns - 1) / columns;
		return rows * (h + gap) - gap;
	}

	private static void NewCard(SpriteBatch sb, Rectangle r)
	{
		bool hover = HubInput.In(r);
		if (hover && HubInput.Hover("bs:new", "A new set of bots"))
		{
			_selected = BotSets.Create().Id;
			HubInput.Sound(12);
		}
		float glow = HubInput.Anim("bs:new", hover);
		HubKit.Box(sb, r, Color.Lerp(new Color(10, 12, 24) * 0.5f, Theme.CardHover, glow * 0.6f));
		HubKit.Fill(sb, new Rectangle(r.X + 6, r.Y + 6, r.Width - 12, 1), Theme.Muted * 0.3f);
		HubKit.Fill(sb, new Rectangle(r.X + 6, r.Bottom - 7, r.Width - 12, 1), Theme.Muted * 0.3f);
		Rectangle plus = new Rectangle(r.X + 22, r.Y + (r.Height - 40) / 2, 40, 40);
		HubKit.Box(sb, plus, Color.Lerp(Theme.Slot, Theme.Accent, 0.25f + glow * 0.3f));
		HubKit.TextCentered(sb, "+", plus, Color.White, 1.3f);
		HubKit.Text(sb, "New set", plus.Right + 16, r.Y + 22, Theme.Text, 0.86f);
		HubKit.Text(sb, "Your accounts, one server, steps on arrival", plus.Right + 16, r.Y + 46, Theme.Muted, 0.62f, r.Right - plus.Right - 26);
	}

	private static void SetCard(SpriteBatch sb, BotSet set, Rectangle r)
	{
		string id = "bs:card:" + set.Id;
		bool picked = _selected == set.Id;
		int online = BotSets.Online(set);
		int waiting = BotSets.Waiting(set);
		bool running = online > 0 || waiting > 0 || BotSets.Members(set).Count > 0;
		Rectangle button = new Rectangle(r.Right - 98, r.Y + (r.Height - 34) / 2, 88, 34);
		bool overButton = HubInput.In(button);
		bool hover = HubInput.In(r) && !overButton;
		if (hover && HubInput.Hover(id, picked ? "Editing it below" : "Click: edit it"))
		{
			_selected = set.Id;
			HubInput.Sound(12);
		}
		float glow = HubInput.Anim(id, hover || picked);
		HubKit.Box(sb, r, Color.Lerp(picked ? Color.Lerp(Theme.Card, Theme.Slot, 0.45f) : Theme.Card, Theme.CardHover, glow * 0.5f) * HubPrefs.Opacity);
		if (picked)
		{
			HubKit.Fill(sb, new Rectangle(r.X + 3, r.Y + 10, 3, r.Height - 20), Gold);
		}
		Rectangle icon = new Rectangle(r.X + 14, r.Y + 14, 58, 58);
		HubKit.Box(sb, icon, (running ? Color.Lerp(Theme.Sidebar, new Color(30, 90, 60), 0.6f) : Theme.Sidebar) * 0.9f);
		HubIcon.Item(set.Icon).Draw(sb, new Rectangle(icon.X + 9, icon.Y + 9, 40, 40), Color.White);
		int tx = icon.Right + 14;
		int room = button.X - tx - 8;
		HubKit.Text(sb, string.IsNullOrWhiteSpace(set.Name) ? "Unnamed set" : set.Name, tx, r.Y + 12, string.IsNullOrWhiteSpace(set.Name) ? Theme.Muted : Color.White, 0.88f, room);
		HubKit.Text(sb, string.IsNullOrEmpty(set.Host) ? "No server yet" : (set.Host + ":" + set.Port), tx, r.Y + 36, string.IsNullOrEmpty(set.Host) ? Gold : Theme.Muted, 0.62f, room);
		int cx = tx;
		int enabled = set.Members.Count(m => m.Enabled);
		cx = HubKit.Chip(sb, enabled + ((enabled == 1) ? " member" : " members"), cx, r.Y + 57, Theme.Slot, Theme.Text, 0.58f).Right + 4;
		if (online > 0)
		{
			cx = HubKit.Chip(sb, online + " online", cx, r.Y + 57, Online * 0.35f, Color.White, 0.58f).Right + 4;
		}
		if (waiting > 0)
		{
			cx = HubKit.Chip(sb, waiting + " joining", cx, r.Y + 57, Gold * 0.35f, Color.White, 0.58f).Right + 4;
		}
		if (set.Steps.Count > 0)
		{
			HubKit.Chip(sb, set.Steps.Count + ((set.Steps.Count == 1) ? " step" : " steps"), cx, r.Y + 57, new Color(70, 90, 160) * 0.6f, Color.White, 0.58f);
		}
		if (running)
		{
			if (HubKit.Button(sb, "bs:stop:" + set.Id, button, "Stop", Red, "All its bots leave", true, HubIcon.Item(I.TrashCan)))
			{
				BotSets.Stop(set);
			}
		}
		else if (HubKit.Button(sb, "bs:start:" + set.Id, button, "Start", Green, "Its members join, one after another", enabled > 0, HubIcon.Item(I.RecallPotion)))
		{
			BotSets.Start(set);
		}
	}

	#endregion

	#region Editor

	private static int Editor(SpriteBatch sb, BotSet set, int x, int y, int width)
	{
		int start = y;
		Rectangle panel = new Rectangle(x, y, width, 0);
		int inner = x + 14;
		int innerW = width - 28;
		y += 12;

		// Name and icon.
		HubIcon.Item(set.Icon).Draw(sb, new Rectangle(inner, y + 2, 30, 30), Color.White);
		string name = HubKit.Field(sb, "bs:name:" + set.Id, new Rectangle(inner + 40, y, Math.Min(360, innerW - 40), 34), set.Name, "Name of the set", 30);
		if (name != set.Name)
		{
			set.Name = name;
			MultiClientManager.Save();
		}
		y += 44;
		y += IconStrip(sb, set, inner, y, innerW) + 14;

		// Server.
		y += Sub(sb, HubIcon.Item(I.WorldGlobe), "Server", "Where they join", inner, y, innerW);
		int portW = 90;
		string host = HubKit.Field(sb, "bs:host:" + set.Id, new Rectangle(inner, y, innerW - portW - 8, 34), set.Host, "ip or domain", 80, HubIcon.Item(I.WorldGlobe));
		if (host != set.Host)
		{
			MultiClientManager.ParseHostPort(host, out string h, out int p);
			set.Host = (host.Contains(":") && h != null) ? h : host.Trim();
			if (host.Contains(":") && p > 0)
			{
				set.Port = p;
			}
			MultiClientManager.Save();
		}
		string port = NumberField(sb, "bs:port:" + set.Id, new Rectangle(inner + innerW - portW, y, portW, 34), set.Port.ToString(), "7777", 5, decimals: false);
		if (int.TryParse(port, out int newPort) && newPort > 0 && newPort <= 65535 && newPort != set.Port)
		{
			set.Port = newPort;
			MultiClientManager.Save();
		}
		y += 42;
		string pw = HubKit.Field(sb, "bs:spw:" + set.Id, new Rectangle(inner, y, innerW, 34), set.ServerPassword, "Server password (only if the server asks for one when joining)", 60, HubIcon.Item(I.GoldenKey), secret: true);
		if (pw != set.ServerPassword)
		{
			set.ServerPassword = pw;
			MultiClientManager.Save();
		}
		y += 42;
		y += KnownServers(sb, set, inner, y, innerW) + 14;

		// Members.
		y += Sub(sb, HubIcon.Item(I.Mannequin), "Members (" + set.Members.Count + ")", "Each one is an account: its name, look and login", inner, y, innerW);
		y += MyCharacters(sb, set, inner, y, innerW);
		for (int i = 0; i < set.Members.Count; i++)
		{
			y += MemberRow(sb, set, set.Members[i], i, inner, y, innerW) + 6;
		}
		y += AddMembers(sb, set, inner, y, innerW) + 14;

		// Steps.
		y += Sub(sb, HubIcon.Item(I.Book), "When they arrive", "Each member does these, in order. {pass} is its login, {name} its name", inner, y, innerW);
		y += StepsEditor(sb, "bs:steps:" + set.Id, set.Steps, inner, y, innerW) + 14;

		// Options.
		y += Sub(sb, HubIcon.Item(I.Cog), "Options", "How the set behaves", inner, y, innerW);
		y += Stepper(sb, "bs:gap:" + set.Id, "Between joins", "Seconds between two members joining (servers dislike many joins at once)", set.JoinGap, 0.5f, 30f, 0.5f, v => set.JoinGap = v, inner, y, innerW) + 6;
		y += Stepper(sb, "bs:sgap:" + set.Id, "Between steps", "Seconds between two steps", set.StepGap, 0.2f, 10f, 0.1f, v => set.StepGap = v, inner, y, innerW) + 8;
		y += Switches(sb, inner, y, innerW, new (string, string, string, bool, Action)[]
		{
			("bs:o:again:" + set.Id, "Steps again on reconnect", "A member that drops and comes back logs in again", set.StepsOnReconnect, () => set.StepsOnReconnect = !set.StepsOnReconnect),
			("bs:o:follow:" + set.Id, "Follow me", "After their steps they follow you (in your world)", set.FollowMe, () => set.FollowMe = !set.FollowMe),
			("bs:o:fight:" + set.Id, "Fight enemies", "They hit enemies near them with their hotbar weapon", set.Fight, () => { set.Fight = !set.Fight; foreach (ClientSession s in BotSets.Members(set)) { s.FightOverride = set.Fight; } }),
			("bs:o:ai:" + set.Id, "AI answers", "They answer with the AI when someone says their name", set.Ai, () => { set.Ai = !set.Ai; foreach (ClientSession s in BotSets.Members(set)) { s.AiEnabled = set.Ai; } })
		}) + 14;

		// Orders now.
		y += Sub(sb, HubIcon.Item(I.Megaphone), "All of them, now", "Orders for every bot of this set that is online", inner, y, innerW);
		y += Orders(sb, "bs:run:" + set.Id, BotSets.Members(set), ref _runLine, inner, y, innerW) + 10;
		Rectangle reconnect = new Rectangle(inner, y, 150, 32);
		if (HubKit.Button(sb, "bs:rec:" + set.Id, reconnect, "Reconnect all", Theme.Slot, "Every member connects again", BotSets.Members(set).Count > 0, HubIcon.Item(I.Teleporter)))
		{
			BotSets.ReconnectAll(set);
		}
		Rectangle dup = new Rectangle(reconnect.Right + 8, y, 130, 32);
		if (HubKit.Button(sb, "bs:dup:" + set.Id, dup, "Duplicate", Theme.Slot, "A copy of this set (members, steps, options)", true, HubIcon.Item(I.Book)))
		{
			_selected = BotSets.Duplicate(set).Id;
		}
		bool armed = GameArtNow() - _deleteArmedAt < 3.0;
		Rectangle del = new Rectangle(inner + innerW - 140, y, 140, 32);
		if (HubKit.Button(sb, "bs:del:" + set.Id, del, armed ? "Sure? Click" : "Delete set", Red, armed ? "Click again to delete it (its bots leave)" : "Deletes the set (asks first)", true, HubIcon.Item(I.TrashCan)))
		{
			if (armed)
			{
				BotSets.Delete(set);
				_selected = null;
				_deleteArmedAt = 0;
			}
			else
			{
				_deleteArmedAt = GameArtNow();
			}
		}
		y += 44;
		panel.Height = y - start;
		// The frame goes behind (drawn last, faint, so the controls above stay crisp).
		HubKit.Fill(sb, new Rectangle(panel.X, panel.Y, 2, panel.Height), Gold * 0.35f);
		return y - start;
	}

	private static double GameArtNow()
	{
		return Main.GlobalTimeWrappedHourly + 3600.0 * (Main.GameUpdateCount / (60 * 3600));
	}

	private static int IconStrip(SpriteBatch sb, BotSet set, int x, int y, int width)
	{
		const int size = 34;
		const int gap = 4;
		int perRow = Math.Max(1, (width + gap) / (size + gap));
		int[] icons = BotSets.Icons;
		for (int i = 0; i < icons.Length; i++)
		{
			Rectangle r = new Rectangle(x + i % perRow * (size + gap), y + i / perRow * (size + gap), size, size);
			string id = "bs:icon:" + set.Id + ":" + i;
			bool active = set.Icon == icons[i];
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover(id) && !active)
			{
				set.Icon = icons[i];
				MultiClientManager.Save();
				HubInput.Sound(12);
			}
			float glow = HubInput.Anim(id, hover || active);
			HubKit.Box(sb, r, Color.Lerp(Theme.Sidebar, Theme.CardHover, glow * 0.7f) * 0.95f);
			if (active)
			{
				Frame(sb, r, Gold);
			}
			HubIcon.Item(icons[i]).Draw(sb, new Rectangle(r.X + 5, r.Y + 5, size - 10, size - 10), Color.White * (active || hover ? 1f : 0.75f));
		}
		int rows = (icons.Length + perRow - 1) / perRow;
		return rows * (size + gap) - gap;
	}

	/// <summary>The servers you know (where you are, your bots', saved and recent ones) as tiles: one click and it's set.</summary>
	private static int KnownServers(SpriteBatch sb, BotSet set, int x, int y, int width)
	{
		List<ServerEntry> servers;
		try
		{
			servers = MultiClientManager.KnownServers();
		}
		catch
		{
			return 0;
		}
		if (servers.Count == 0)
		{
			return 0;
		}
		int start = y;
		HubKit.Text(sb, "Or pick one you know:", x + 2, y, Theme.Muted, 0.62f);
		y += 20;
		const int gap = 6;
		int columns = Math.Max(1, Math.Min(4, width / 210));
		int tileW = (width - (columns - 1) * gap) / columns;
		const int h = 44;
		List<ServerEntry> shown = servers.Take(columns * 2).ToList();
		for (int i = 0; i < shown.Count; i++)
		{
			ServerEntry s = shown[i];
			Rectangle r = new Rectangle(x + i % columns * (tileW + gap), y + i / columns * (h + gap), tileW, h);
			string id = "bs:srv:" + set.Id + ":" + s.Host + ":" + s.Port;
			bool active = string.Equals(set.Host, s.Host, StringComparison.OrdinalIgnoreCase) && set.Port == s.Port;
			bool here = s.Label == "Where you are";
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover(id, "Use " + s.Host + ":" + s.Port) && !active)
			{
				set.Host = s.Host;
				set.Port = s.Port;
				MultiClientManager.Save();
				HubInput.Sound(12);
			}
			HubKit.Box(sb, r, Color.Lerp(Color.Lerp(Theme.Card, Theme.CardHover, HubInput.Anim(id, hover)), Color.Lerp(Theme.Slot, Gold, 0.18f), active ? 1f : 0f) * HubPrefs.Opacity);
			if (active)
			{
				Frame(sb, r, Gold);
			}
			HubIcon.Item(here ? I.WorldGlobe : (s.Label.StartsWith("Saved") ? I.Book : (s.Label.StartsWith("Server of") ? I.RobotHat : I.Compass))).Draw(sb, new Rectangle(r.X + 8, r.Y + 10, 24, 24), Color.White);
			string title = s.Label.StartsWith("Recent: ") ? s.Label.Substring(8) : s.Label;
			HubKit.Text(sb, title, r.X + 40, r.Y + 5, here ? new Color(140, 228, 155) : Color.White, 0.66f, r.Width - 46);
			HubKit.Text(sb, s.Host + ":" + s.Port, r.X + 40, r.Y + 24, Theme.Muted, 0.56f, r.Width - 46);
		}
		y += (shown.Count + columns - 1) / columns * (h + gap) - gap;
		return y - start;
	}

	private static int MemberRow(SpriteBatch sb, BotSet set, BotMember m, int index, int x, int y, int width)
	{
		string id = "bs:m:" + set.Id + ":" + index;
		bool open = _openMember == id;
		Rectangle r = new Rectangle(x, y, width, 48);
		ClientSession live = BotSets.Members(set).FirstOrDefault(s => s.Member == m || string.Equals(s.Name, m.Name, StringComparison.OrdinalIgnoreCase));
		HubKit.Box(sb, r, (m.Enabled ? Theme.Card : Theme.Card * 0.6f) * HubPrefs.Opacity);
		Color dot = (live == null) ? Theme.Muted * 0.4f : ((live.State == ClientSessionState.InGame) ? Online : ((live.State == ClientSessionState.Disconnected) ? new Color(230, 100, 100) : Gold));
		HubKit.Fill(sb, new Rectangle(r.X + 3, r.Y + 10, 3, r.Height - 20), dot);
		HubKit.Text(sb, "#" + (index + 1), r.X + 12, r.Y + 15, Theme.Muted, 0.62f);
		// Look: the character's portrait, or the robot of a generated look; click to change.
		Rectangle look = new Rectangle(r.X + 40, r.Y + 6, 36, 36);
		CharacterEntry character = string.IsNullOrEmpty(m.PlayerFile) ? null : MultiClientManager.Characters.FirstOrDefault(c => string.Equals(c.Path, m.PlayerFile, StringComparison.OrdinalIgnoreCase));
		HubKit.Box(sb, look, Theme.Sidebar * 0.9f);
		if (character?.Player != null)
		{
			HubKit.Portrait(sb, character.Player, new Rectangle(look.X - 6, look.Y - 4, look.Width + 12, look.Height + 8), 0.6f, walking: false);
		}
		else
		{
			HubIcon.Item(I.RobotHat).Draw(sb, new Rectangle(look.X + 6, look.Y + 6, 24, 24), Color.White * 0.85f);
		}
		if (HubInput.In(look) && HubInput.Hover(id + ":look", (character != null ? character.Name : "Generated look") + ". Click: next character, right click: back"))
		{
			CycleLook(m, 1);
		}
		if (HubInput.In(look) && HubInput.RightClicked)
		{
			CycleLook(m, -1);
		}
		// Name, login.
		int fx = look.Right + 8;
		int switchW = 52;
		int buttonsW = 2 * 30 + switchW + 12;
		int fieldsW = r.Right - fx - buttonsW - 8;
		int nameW = fieldsW * 45 / 100;
		string name = HubKit.Field(sb, id + ":name", new Rectangle(fx, r.Y + 7, nameW, 34), m.Name, "Account name", 20);
		if (name != m.Name)
		{
			m.Name = name;
			MultiClientManager.Save();
		}
		string login = HubKit.Field(sb, id + ":login", new Rectangle(fx + nameW + 6, r.Y + 7, fieldsW - nameW - 6, 34), m.Login, "Its password ({pass})", 60, HubIcon.Item(I.GoldenKey), secret: true);
		if (login != m.Login)
		{
			m.Login = login;
			MultiClientManager.Save();
		}
		int bx = r.Right - buttonsW;
		Rectangle sw = new Rectangle(bx, r.Y + 13, 44, 22);
		bool swHover = HubInput.In(sw);
		if (swHover && HubInput.Hover(id + ":on", m.Enabled ? "Joins with the set (click: leave it out)" : "Left out (click: it joins with the set)"))
		{
			m.Enabled = !m.Enabled;
			MultiClientManager.Save();
		}
		HubKit.Switch(sb, id + ":sw", sw, m.Enabled, swHover);
		Rectangle steps = new Rectangle(bx + switchW, r.Y + 9, 28, 30);
		if (HubKit.BareIcon(sb, id + ":steps", steps, HubIcon.Item(I.Book), open ? "Hide its own steps" : ((m.Steps.Count > 0) ? (m.Steps.Count + " steps of its own (before the set's)") : "Steps only this one does (before the set's)"), (m.Steps.Count > 0 || open) ? Color.White : Color.White * 0.55f, 1f))
		{
			_openMember = open ? null : id;
			HubInput.Sound(12);
		}
		Rectangle remove = new Rectangle(steps.Right + 4, r.Y + 9, 28, 30);
		if (HubKit.BareIcon(sb, id + ":rm", remove, HubIcon.Texture("Images/UI/ButtonDelete"), "Remove from the set", Color.White, 1f))
		{
			set.Members.RemoveAt(index);
			_openMember = null;
			MultiClientManager.Save();
			return r.Height;
		}
		if (!open)
		{
			return r.Height;
		}
		int sy = r.Bottom + 6;
		HubKit.Text(sb, "Only " + (string.IsNullOrEmpty(m.Name) ? "this one" : m.Name) + ", before the set's steps:", x + 40, sy, Theme.Muted, 0.62f);
		sy += 22;
		sy += StepsEditor(sb, id + ":own", m.Steps, x + 40, sy, width - 40);
		return sy - y;
	}

	/// <summary>Your characters (.plr): a click adds one as a member, with its name and look.</summary>
	private static int MyCharacters(SpriteBatch sb, BotSet set, int x, int y, int width)
	{
		List<CharacterEntry> characters = MultiClientManager.Characters;
		if (characters == null || characters.Count == 0)
		{
			return 0;
		}
		int start = y;
		HubKit.Text(sb, "Your characters: click one to add it (it joins with its look and items)", x + 2, y, Theme.Muted, 0.62f, width - 130);
		if (HubKit.Button(sb, "bs:allchars:" + set.Id, new Rectangle(x + width - 120, y - 6, 120, 26), "Add them all", Theme.Slot, "Every character of yours becomes a member", true))
		{
			foreach (CharacterEntry c in characters)
			{
				AddCharacter(set, c);
			}
		}
		y += 24;
		const int w = 74;
		const int h = 92;
		const int gap = 6;
		int perRow = Math.Max(1, (width + gap) / (w + gap));
		int count = Math.Min(characters.Count, perRow * 3);
		for (int i = 0; i < count; i++)
		{
			CharacterEntry c = characters[i];
			Rectangle r = new Rectangle(x + i % perRow * (w + gap), y + i / perRow * (h + gap), w, h);
			if (!HubInput.Visible(r))
			{
				continue;
			}
			string id = "bs:ch:" + set.Id + ":" + i;
			bool member = set.Members.Any(m => string.Equals(m.PlayerFile, c.Path, StringComparison.OrdinalIgnoreCase));
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover(id, member ? (c.Name + " is already a member") : ("Add " + c.Name + " (" + c.Summary + ")")) && !member)
			{
				AddCharacter(set, c);
				HubInput.Sound(12);
			}
			HubKit.Box(sb, r, Color.Lerp(member ? Color.Lerp(Theme.Card, new Color(30, 80, 50), 0.6f) : Theme.Card, Theme.CardHover, HubInput.Anim(id, hover) * 0.6f) * HubPrefs.Opacity);
			if (c.Player != null)
			{
				HubKit.Portrait(sb, c.Player, new Rectangle(r.X, r.Y + 2, r.Width, r.Height - 22), 0.9f, walking: hover);
			}
			HubKit.Text(sb, c.Name, r.X + 5, r.Bottom - 19, member ? Online : Theme.Text, 0.56f, r.Width - 10);
			if (member)
			{
				HubKit.Fill(sb, new Rectangle(r.Right - 12, r.Y + 5, 7, 7), Online);
			}
		}
		y += (count + perRow - 1) / perRow * (h + gap);
		return y - start + 4;
	}

	private static void AddCharacter(BotSet set, CharacterEntry c)
	{
		if (set.Members.Any(m => string.Equals(m.PlayerFile, c.Path, StringComparison.OrdinalIgnoreCase)))
		{
			return;
		}
		string name = (c.Name ?? "Bot").Trim();
		if (name.Length > 20)
		{
			name = name.Substring(0, 20);
		}
		set.Members.Add(new BotMember { Name = name, PlayerFile = c.Path });
		MultiClientManager.Save();
	}

	private static void CycleLook(BotMember m, int step)
	{
		List<CharacterEntry> characters = MultiClientManager.Characters;
		int at = characters.FindIndex(c => string.Equals(c.Path, m.PlayerFile, StringComparison.OrdinalIgnoreCase));
		int next = at + step;
		if (next < -1)
		{
			next = characters.Count - 1;
		}
		else if (next >= characters.Count)
		{
			next = -1;
		}
		m.PlayerFile = (next < 0) ? "" : characters[next].Path;
		MultiClientManager.Save();
		HubInput.Sound(12);
	}

	private static int AddMembers(SpriteBatch sb, BotSet set, int x, int y, int width)
	{
		Rectangle one = new Rectangle(x, y, 130, 32);
		if (HubKit.Button(sb, "bs:add1:" + set.Id, one, "+ Member", Green, "One more account", true, HubIcon.Item(I.Mannequin)))
		{
			set.Members.Add(new BotMember { Name = "" });
			MultiClientManager.Save();
		}
		Rectangle mine = new Rectangle(one.Right + 8, y, 150, 32);
		if (HubKit.Button(sb, "bs:import:" + set.Id, mine, "Take my bots", Theme.Slot, "The bots you have online now become members", MultiClientManager.Count > 0, HubIcon.Item(I.RobotHat)))
		{
			int n = BotSets.ImportRunning(set);
			MultiClientManager.Announce(set.Name + ": " + n + " bots added.");
		}
		// Numbered: Base1, Base2...
		int nx = mine.Right + 14;
		int baseW = Math.Max(90, x + width - nx - 36 - 48 - 36 - 90 - 20);
		_numberBase = HubKit.Field(sb, "bs:nbase:" + set.Id, new Rectangle(nx, y, baseW, 32), _numberBase, "Isa -> Isa1, Isa2...  or  Ana, Luis, Eva", 200);
		bool list = _numberBase.Contains(",");
		Rectangle minus = new Rectangle(nx + baseW + 6, y, 32, 32);
		Rectangle count = new Rectangle(minus.Right + 2, y, 48, 32);
		Rectangle plus = new Rectangle(count.Right + 2, y, 32, 32);
		if (HubKit.Button(sb, "bs:n-:" + set.Id, minus, "-", Theme.Slot, null, !list && _numberCount > 1))
		{
			_numberCount--;
		}
		string typed = NumberField(sb, "bs:ncount:" + set.Id, count, _numberCount.ToString(), "5", 2, decimals: false);
		if (int.TryParse(typed, out int wanted) && wanted >= 1)
		{
			_numberCount = Math.Min(wanted, MultiClientManager.MaxSessions);
		}
		if (HubKit.Button(sb, "bs:n+:" + set.Id, plus, "+", Theme.Slot, null, !list && _numberCount < MultiClientManager.MaxSessions))
		{
			_numberCount++;
		}
		Rectangle add = new Rectangle(plus.Right + 6, y, 84, 32);
		string[] names = list ? _numberBase.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToArray() : null;
		string tip = list ? ("Adds " + names.Length + ": " + string.Join(", ", names.Take(6)) + ((names.Length > 6) ? "..." : "")) : ("Adds " + _numberCount + " numbered: " + (string.IsNullOrWhiteSpace(_numberBase) ? "Bot" : _numberBase.Trim()) + "1, 2, 3...");
		if (HubKit.Button(sb, "bs:addn:" + set.Id, add, "Add", Green, tip, !list || names.Length > 0))
		{
			if (list)
			{
				BotSets.AddNames(set, names);
			}
			else
			{
				BotSets.AddNumbered(set, _numberBase, _numberCount);
			}
			_numberBase = "";
		}
		return 32;
	}

	#endregion

	#region Steps

	private static readonly (string Label, string Line, int Icon, string Tip)[] Quick =
	{
		("Login", "/login {pass}", I.GoldenKey, "TShock login with its password"),
		("Say", "Hi, I'm {name}!", I.Megaphone, "A chat line"),
		("Command", "/", I.Book, "A server command"),
		("Wait", ".wait 2", I.Stopwatch, "Waits before the next step"),
		("Pixie", ".pixie me", I.FairyBell, "Flies around you on the Pixie mount"),
		("Follow me", ".follow me", I.Compass, "Follows you"),
		("Come here", ".goto me", I.MagicMirror, "Goes to where you are"),
		("Fight", ".fight on", I.CopperShortsword, "Fights enemies near it"),
		("AI", ".ai on", I.WizardHat, "Answers with the AI")
	};

	/// <summary>Picks the icon of a step by what it does.</summary>
	private static int IconOf(string line)
	{
		string l = (line ?? "").Trim().ToLowerInvariant();
		if (l.StartsWith("/login") || l.StartsWith("/register"))
		{
			return I.GoldenKey;
		}
		if (l.StartsWith("/"))
		{
			return I.Book;
		}
		if (l.StartsWith(".wait") || l.StartsWith(".w ") || l.StartsWith(".sleep"))
		{
			return I.Stopwatch;
		}
		if (l.StartsWith(".pixie") || l.StartsWith(".fairy"))
		{
			return I.FairyBell;
		}
		if (l.StartsWith(".follow"))
		{
			return I.Compass;
		}
		if (l.StartsWith(".goto"))
		{
			return I.MagicMirror;
		}
		if (l.StartsWith(".fight"))
		{
			return I.CopperShortsword;
		}
		if (l.StartsWith(".ai"))
		{
			return I.WizardHat;
		}
		if (l.StartsWith(".stop"))
		{
			return I.Lever;
		}
		if (l.StartsWith(".leave") || l.StartsWith(".quit"))
		{
			return I.TrashCan;
		}
		if (l.StartsWith("#"))
		{
			return I.Sign;
		}
		return I.Megaphone;
	}

	/// <summary>A list of steps: each one editable, moved up or down, removed; quick buttons add the usual ones.</summary>
	internal static int StepsEditor(SpriteBatch sb, string id, List<string> steps, int x, int y, int width)
	{
		int start = y;
		if (steps.Count == 0)
		{
			Rectangle empty = new Rectangle(x, y, width, 40);
			HubKit.Box(sb, empty, new Color(10, 12, 24) * 0.4f);
			HubKit.Text(sb, "No steps: they just arrive. Add some with the buttons below.", empty.X + 14, empty.Y + 12, Theme.Muted, 0.62f, empty.Width - 28);
			y += empty.Height + 6;
		}
		for (int i = 0; i < steps.Count; i++)
		{
			Rectangle r = new Rectangle(x, y, width, 38);
			if (HubInput.Visible(r))
			{
				string rowId = id + ":" + i;
				HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
				HubKit.Text(sb, (i + 1).ToString(), r.X + 10, r.Y + 11, Theme.Muted, 0.62f);
				HubIcon.Item(IconOf(steps[i])).Draw(sb, new Rectangle(r.X + 30, r.Y + 8, 22, 22), Color.White);
				int fieldW = r.Width - 60 - 3 * 28 - 12;
				string line = HubKit.Field(sb, rowId + ":f", new Rectangle(r.X + 58, r.Y + 3, fieldW, 32), steps[i], "Chat, /command or .step", 200);
				if (line != steps[i])
				{
					steps[i] = line;
					MultiClientManager.Save();
				}
				int bx = r.X + 58 + fieldW + 6;
				if (Arrow(sb, rowId + ":up", new Rectangle(bx, r.Y + 7, 24, 24), up: true, i > 0, "Move up"))
				{
					(steps[i - 1], steps[i]) = (steps[i], steps[i - 1]);
					MultiClientManager.Save();
				}
				if (Arrow(sb, rowId + ":dn", new Rectangle(bx + 28, r.Y + 7, 24, 24), up: false, i < steps.Count - 1, "Move down"))
				{
					(steps[i + 1], steps[i]) = (steps[i], steps[i + 1]);
					MultiClientManager.Save();
				}
				if (HubKit.BareIcon(sb, rowId + ":rm", new Rectangle(bx + 56, r.Y + 7, 24, 24), HubIcon.Texture("Images/UI/ButtonDelete"), "Remove the step", Color.White, 1f))
				{
					steps.RemoveAt(i);
					MultiClientManager.Save();
					break;
				}
			}
			y += r.Height + 4;
		}
		y += 2;
		// Quick add.
		int cx = x;
		int rowY = y;
		foreach (var q in Quick)
		{
			int w = (int)HubKit.Measure("+ " + q.Label, 0.64f) + 44;
			if (cx + w > x + width)
			{
				cx = x;
				rowY += 34;
			}
			Rectangle r = new Rectangle(cx, rowY, w, 30);
			string qid = id + ":q:" + q.Label;
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover(qid, q.Tip + ": " + q.Line))
			{
				steps.Add(q.Line);
				MultiClientManager.Save();
				HubInput.Sound(12);
			}
			HubKit.Box(sb, r, Color.Lerp(Theme.Slot, Theme.CardHover, HubInput.Anim(qid, hover)) * 0.95f);
			HubIcon.Item(q.Icon).Draw(sb, new Rectangle(r.X + 8, r.Y + 6, 18, 18), Color.White);
			HubKit.Text(sb, "+ " + q.Label, r.X + 30, r.Y + 7, Theme.Text, 0.64f);
			cx += w + 6;
		}
		y = rowY + 34;
		// What the bot steps are (one line, hover for all).
		Rectangle help = new Rectangle(x, y, width, 20);
		HubKit.Text(sb, "Bot steps: " + string.Join("  ", BotScript.Known.Select(k => k.Step.Split(' ')[0])) + "   ·   {name} {pass} {n} {me} {set}", x + 2, y, Theme.Muted * 0.9f, 0.58f, width - 4);
		if (HubInput.In(help))
		{
			HubInput.Hover(id + ":help", string.Join("\n", BotScript.Known.Select(k => k.Step + ": " + k.Help)) + "\nAnything else is said in the chat; /commands go to the server.\n{name} its name, {pass} its login, {n} its number, {me} your name, {set} the set, {server} the server.");
		}
		y += 24;
		return y - start;
	}

	#endregion

	#region Orders

	/// <summary>A line to run on <paramref name="bots"/> plus one-click orders.</summary>
	private static int Orders(SpriteBatch sb, string id, List<ClientSession> bots, ref string line, int x, int y, int width)
	{
		int start = y;
		int online = bots.Count(b => b.State == ClientSessionState.InGame && !b.Possessed);
		Rectangle run = new Rectangle(x + width - 110, y, 110, 34);
		line = HubKit.Field(sb, id + ":line", new Rectangle(x, y, width - 118, 34), line, "Say, /command or .step for all of them", 200, HubIcon.Item(I.Megaphone));
		if (HubKit.Button(sb, id + ":go", run, "Run", Green, online + " online will do it", online > 0 && !string.IsNullOrWhiteSpace(line), HubIcon.Item(I.RecallPotion)) || (HubInput.Focus == id + ":line" && Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Enter) && !Main.oldKeyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Enter) && online > 0 && !string.IsNullOrWhiteSpace(line)))
		{
			int done = BotSets.RunNow(bots, line);
			MultiClientManager.Announce(done + ((done == 1) ? " bot: " : " bots: ") + line);
			line = "";
		}
		y += 42;
		(string Label, string Line, int Icon, string Tip)[] orders =
		{
			("Follow me", ".follow me", I.Compass, "They follow you"),
			("Come here", ".goto me", I.MagicMirror, "They go to where you are"),
			("Pixies", ".pixie me", I.FairyBell, "They fly around you on the Pixie mount"),
			("Stop", ".stop", I.Lever, "They stop where they are"),
			("Login", "/login {pass}", I.GoldenKey, "Each one logs in with its password")
		};
		int cx = x;
		int rowY = y;
		foreach (var o in orders)
		{
			int w = (int)HubKit.Measure(o.Label, 0.78f) + 56;
			if (cx + w > x + width)
			{
				cx = x;
				rowY += 38;
			}
			if (HubKit.Button(sb, id + ":o:" + o.Label, new Rectangle(cx, rowY, w, 32), o.Label, Theme.Slot, o.Tip, online > 0, HubIcon.Item(o.Icon)))
			{
				BotSets.RunNow(bots, o.Line);
			}
			cx += w + 6;
		}
		y = rowY + 38;
		return y - start;
	}

	/// <summary>Sessions: one bar to give an order to every bot, or to one set.</summary>
	internal static int BatchBar(SpriteBatch sb, int x, int y, int width)
	{
		List<ClientSession> all = MultiClientManager.All.Where(s => !s.UserRemoved).ToList();
		if (all.Count == 0)
		{
			return 0;
		}
		int start = y;
		Rectangle box = new Rectangle(x, y, width, 0);
		int inner = x + 12;
		int innerW = width - 24;
		y += 10;
		HubIcon.Item(I.Megaphone).Draw(sb, new Rectangle(inner, y, 22, 22), Color.White);
		HubKit.Text(sb, "All at once", inner + 30, y + 2, Theme.Accent, 0.82f);
		// Who: every bot or a set that has bots now.
		int cx = inner + 30 + (int)HubKit.Measure("All at once", 0.82f) + 16;
		List<(string Key, string Label)> targets = new List<(string, string)> { ("all", "Every bot (" + all.Count + ")") };
		foreach (BotSet set in BotSets.Sets)
		{
			int n = BotSets.Members(set).Count;
			if (n > 0)
			{
				targets.Add((set.Id, set.Name + " (" + n + ")"));
			}
		}
		if (targets.All(t => t.Key != _batchTarget))
		{
			_batchTarget = "all";
		}
		foreach (var t in targets)
		{
			int w = (int)Math.Min(HubKit.Measure(t.Label, 0.62f) + 22, 200);
			if (cx + w > inner + innerW)
			{
				break;
			}
			if (Chip(sb, "bs:bt:" + t.Key, new Rectangle(cx, y - 2, w, 26), t.Label, _batchTarget == t.Key))
			{
				_batchTarget = t.Key;
			}
			cx += w + 6;
		}
		y += 32;
		BotSet target = BotSets.Sets.FirstOrDefault(s => s.Id == _batchTarget);
		List<ClientSession> bots = (target != null) ? BotSets.Members(target) : all;
		y += Orders(sb, "bs:batch", bots, ref _batchLine, inner, y, innerW);
		Rectangle reconnect = new Rectangle(inner, y, 160, 30);
		if (HubKit.Button(sb, "bs:b:rec", reconnect, "Reconnect them", Theme.Slot, "The dropped ones connect again", bots.Any(b => b.State == ClientSessionState.Disconnected), HubIcon.Item(I.Teleporter)))
		{
			foreach (ClientSession s in bots.Where(b => b.State == ClientSessionState.Disconnected))
			{
				MultiClientManager.Reconnect(s);
			}
		}
		bool armed = GameArtNow() - _deleteArmedAt < 3.0;
		Rectangle leave = new Rectangle(reconnect.Right + 8, y, 160, 30);
		if (HubKit.Button(sb, "bs:b:leave", leave, armed ? "Sure? Click" : "All leave", Red, armed ? "Click again: they all leave" : "They all leave (asks first)", true, HubIcon.Item(I.TrashCan)))
		{
			if (armed)
			{
				foreach (ClientSession s in bots.ToList())
				{
					MultiClientManager.Remove(s);
				}
				_deleteArmedAt = 0;
			}
			else
			{
				_deleteArmedAt = GameArtNow();
			}
		}
		y += 40;
		box.Height = y - start;
		Frame(sb, box, Theme.Accent * 0.35f);
		return y - start;
	}

	#endregion

	#region Options page: pixie bots

	private static readonly int[] PixieBuffs =
	{
		Terraria.ID.BuffID.PixieMount, Terraria.ID.BuffID.Shine, Terraria.ID.BuffID.Lovestruck, Terraria.ID.BuffID.Stinky,
		Terraria.ID.BuffID.Regeneration, Terraria.ID.BuffID.Ironskin, Terraria.ID.BuffID.Wrath, Terraria.ID.BuffID.Inferno
	};

	/// <summary>Options: the buff pixie bots carry, and whether they go after what you hit.</summary>
	internal static int PixieSection(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y += Header(sb, HubIcon.Item(I.FairyBell), "Pixie bots", "Bots after .pixie", x, y, width);
		Rectangle r = new Rectangle(x, y, width, 60);
		HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
		int buff = BotSets.PixieBuff;
		Rectangle icon = new Rectangle(r.X + 12, r.Y + 12, 36, 36);
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
		HubKit.Text(sb, "Their buff", r.X + 60, r.Y + 8, Theme.Text, 0.76f);
		string name = (buff > 0 && buff < Terraria.ID.BuffID.Count) ? Lang.GetBuffName(buff) : "None";
		HubKit.Text(sb, name + ((buff > 0) ? (" (" + buff + ")") : "") + ": everyone sees it on them while they are pixies", r.X + 60, r.Y + 32, Theme.Muted, 0.6f, r.Width - 60 - 130);
		string typed = NumberField(sb, "bs:pxbuff", new Rectangle(r.Right - 118, r.Y + 14, 106, 32), buff.ToString(), "384", 4, decimals: false);
		if (int.TryParse(typed, out int id) && id != buff && id >= 0 && id < Terraria.ID.BuffID.Count)
		{
			BotSets.SetPixieBuff(id);
		}
		y += r.Height + 6;
		// Quick picks.
		int cx = x;
		foreach (int b in PixieBuffs)
		{
			Rectangle chip = new Rectangle(cx, y, 40, 40);
			if (chip.Right > x + width)
			{
				break;
			}
			string chipId = "bs:pxb:" + b;
			bool active = buff == b;
			bool hover = HubInput.In(chip);
			if (hover && HubInput.Hover(chipId, Lang.GetBuffName(b) + " (" + b + ")") && !active)
			{
				BotSets.SetPixieBuff(b);
				HubInput.Sound(12);
			}
			HubKit.Box(sb, chip, Color.Lerp(Theme.Sidebar, Theme.CardHover, HubInput.Anim(chipId, hover || active) * 0.7f) * 0.95f);
			if (active)
			{
				Frame(sb, chip, Gold);
			}
			try
			{
				sb.Draw(Terraria.GameContent.TextureAssets.Buff[b].Value, new Rectangle(chip.X + 5, chip.Y + 5, 30, 30), Color.White);
			}
			catch
			{
			}
			cx += 46;
		}
		y += 48;
		y += Switches(sb, x, y, width, new (string, string, string, bool, Action)[]
		{
			("bs:px:attack", "Attack what I attack", "They fly at the enemy you hit (kill aura 1) and come back to you after", BotSets.PixiesAttack, () => BotSets.PixiesAttack = !BotSets.PixiesAttack),
			("bs:px:pvp", "PvP with me", "Their PvP goes on and off with yours (on your server); they fly at the players you hit and hit them", BotSets.PixiesPvp, () => BotSets.PixiesPvp = !BotSets.PixiesPvp)
		}) + 6;
		y += Stepper(sb, "bs:px:dmg", "PvP damage", "Each pixie hit on a player (keep it under the server's MaxDamage)", BotSets.PixiePvpDamage, 1f, 1000f, 10f, v => BotSets.PixiePvpDamage = (int)v, x, y, width) + 6;
		y += Stepper(sb, "bs:px:chase", "Keep after it", "Seconds they keep after an enemy since your last hit on it", BotSets.ChaseSeconds, 1f, 60f, 1f, v => BotSets.ChaseSeconds = v, x, y, width);
		return y - start;
	}

	#endregion

	#region Options page: steps for any bot

	/// <summary>Options: what every bot that is not in a set does when it arrives.</summary>
	internal static int AnyStepsSection(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y += Header(sb, HubIcon.Item(I.Book), "When any bot arrives", "Bots not in a set do these steps", x, y, width);
		y += StepsEditor(sb, "bs:any", BotSets.AnySteps, x, y, width) + 6;
		y += Switches(sb, x, y, width, new (string, string, string, bool, Action)[]
		{
			("bs:any:again", "Steps again on reconnect", "A bot that drops and comes back does them again", BotSets.AnyStepsOnReconnect, () => BotSets.AnyStepsOnReconnect = !BotSets.AnyStepsOnReconnect)
		});
		return y - start;
	}

	#endregion

	#region Pieces

	private static readonly Dictionary<string, string> _typing = new Dictionary<string, string>();

	/// <summary>A field for a number: while you type it keeps exactly what you typed (even empty), only digits.</summary>
	internal static string NumberField(SpriteBatch sb, string id, Rectangle r, string value, string placeholder, int maxLength, bool decimals)
	{
		bool focused = HubInput.Focus == id;
		string shown = (focused && _typing.TryGetValue(id, out string t)) ? t : value;
		string typed = HubKit.Field(sb, id, r, shown, placeholder, maxLength, clearButton: false);
		typed = new string(typed.Where(c => char.IsDigit(c) || (decimals && (c == '.' || c == ','))).ToArray());
		if (HubInput.Focus == id)
		{
			_typing[id] = typed;
		}
		else
		{
			_typing.Remove(id);
		}
		return typed;
	}

	internal static int Header(SpriteBatch sb, HubIcon icon, string title, string hint, int x, int y, int width)
	{
		icon.Draw(sb, new Rectangle(x, y + 2, 24, 24), Color.White);
		HubKit.Text(sb, title, x + 32, y + 4, Theme.Accent, 0.9f);
		float tw = HubKit.Measure(title, 0.9f);
		HubKit.Text(sb, hint, x + 44 + tw, y + 7, Theme.Muted, 0.66f, width - 44 - tw);
		return 40;
	}

	internal static int Sub(SpriteBatch sb, HubIcon icon, string title, string hint, int x, int y, int width)
	{
		icon.Draw(sb, new Rectangle(x, y + 1, 20, 20), Color.White);
		HubKit.Text(sb, title, x + 28, y + 2, Theme.Text, 0.8f);
		float tw = HubKit.Measure(title, 0.8f);
		HubKit.Text(sb, hint, x + 38 + tw, y + 4, Theme.Muted, 0.6f, width - 38 - tw);
		return 30;
	}

	internal static int Hint(SpriteBatch sb, int x, int y, int width, string text)
	{
		Rectangle r = new Rectangle(x, y, width, 58);
		HubKit.Box(sb, r, new Color(10, 12, 24) * 0.45f);
		HubIcon.Item(I.Sign).Draw(sb, new Rectangle(r.X + 14, r.Y + 13, 30, 30), Color.White * 0.9f);
		string[] lines = HubKit.Wrap(text, r.Width - 74, 0.64f, 2);
		for (int i = 0; i < lines.Length; i++)
		{
			HubKit.Text(sb, lines[i], r.X + 58, r.Y + 12 + i * 18, Theme.Muted, 0.64f);
		}
		return r.Height;
	}

	/// <summary>A small button with a pixel triangle pointing up or down.</summary>
	private static bool Arrow(SpriteBatch sb, string id, Rectangle r, bool up, bool enabled, string tip)
	{
		bool hover = enabled && HubInput.In(r);
		bool clicked = hover && HubInput.Hover(id, tip);
		HubKit.Box(sb, r, Color.Lerp(Theme.Slot, Theme.CardHover, HubInput.Anim(id, hover)) * (enabled ? 0.95f : 0.4f));
		Color color = enabled ? Color.White : Theme.Muted * 0.5f;
		const int rows = 5;
		int cx = r.Center.X;
		int top = r.Center.Y - rows + 1;
		for (int i = 0; i < rows; i++)
		{
			int half = up ? i : (rows - 1 - i);
			HubKit.Fill(sb, new Rectangle(cx - half * 2 - 1, top + i * 2, half * 4 + 2, 2), color);
		}
		if (clicked)
		{
			HubInput.Sound(12);
		}
		return clicked;
	}

	private static void Frame(SpriteBatch sb, Rectangle r, Color color)
	{
		HubKit.Fill(sb, new Rectangle(r.X, r.Y, r.Width, 2), color);
		HubKit.Fill(sb, new Rectangle(r.X, r.Bottom - 2, r.Width, 2), color);
		HubKit.Fill(sb, new Rectangle(r.X, r.Y, 2, r.Height), color);
		HubKit.Fill(sb, new Rectangle(r.Right - 2, r.Y, 2, r.Height), color);
	}

	internal static bool Chip(SpriteBatch sb, string id, Rectangle r, string label, bool active)
	{
		bool hover = HubInput.In(r);
		bool clicked = hover && HubInput.Hover(id) && !active;
		if (clicked)
		{
			HubInput.Sound(12);
		}
		float glow = HubInput.Anim(id, hover || active);
		HubKit.Box(sb, r, (active ? Color.Lerp(Theme.Slot, Theme.Accent, 0.45f) : Color.Lerp(Theme.Card, Theme.CardHover, glow)) * HubPrefs.Opacity);
		HubKit.Text(sb, label, r.X + 10, r.Y + (r.Height - 16) / 2, active ? Color.White : Theme.Text, 0.62f, r.Width - 14);
		return clicked;
	}

	internal static int Stepper(SpriteBatch sb, string id, string title, string tip, float value, float min, float max, float step, Action<float> set, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 48);
		HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
		HubKit.Text(sb, title, r.X + 12, r.Y + 6, Theme.Text, 0.74f);
		HubKit.Text(sb, tip, r.X + 12, r.Y + 26, Theme.Muted, 0.58f, r.Width - 190);
		Rectangle minus = new Rectangle(r.Right - 160, r.Y + 9, 32, 30);
		Rectangle box = new Rectangle(minus.Right + 4, r.Y + 9, 76, 30);
		Rectangle plus = new Rectangle(box.Right + 4, r.Y + 9, 32, 30);
		float fast = Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift) ? 5f : 1f;
		if (HubKit.Button(sb, id + ":-", minus, "-", Theme.Slot, "Less (Shift: 5 times)", value > min))
		{
			set(Math.Max(min, (float)Math.Round((value - step * fast) / step) * step));
			MultiClientManager.Save();
		}
		string typed = NumberField(sb, id + ":v", box, value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), "0", 5, decimals: true);
		if (float.TryParse(typed.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) && v >= min && v <= max && Math.Abs(v - value) > 0.001f)
		{
			set(v);
			MultiClientManager.Save();
		}
		if (HubKit.Button(sb, id + ":+", plus, "+", Theme.Slot, "More (Shift: 5 times)", value < max))
		{
			set(Math.Min(max, (float)Math.Round((value + step * fast) / step) * step));
			MultiClientManager.Save();
		}
		return r.Height;
	}

	internal static int Switches(SpriteBatch sb, int x, int y, int width, (string Id, string Title, string Tip, bool On, Action Flip)[] toggles)
	{
		int columns = (width >= 600 && toggles.Length > 1) ? 2 : 1;
		const int gap = 8;
		int tileW = (width - (columns - 1) * gap) / columns;
		const int height = 54;
		for (int i = 0; i < toggles.Length; i++)
		{
			var t = toggles[i];
			Rectangle r = new Rectangle(x + i % columns * (tileW + gap), y + i / columns * (height + gap), tileW, height);
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover(t.Id, t.Tip))
			{
				t.Flip();
				MultiClientManager.Save();
				HubInput.Sound(t.On ? 11 : 10);
			}
			HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.CardHover, HubInput.Anim(t.Id, hover)) * HubPrefs.Opacity);
			HubKit.Text(sb, t.Title, r.X + 12, r.Y + 8, t.On ? Theme.Text : Theme.Muted, 0.76f, r.Width - 76);
			HubKit.Text(sb, t.Tip, r.X + 12, r.Y + 30, Theme.Muted * 0.9f, 0.58f, r.Width - 76);
			HubKit.Switch(sb, t.Id + ":sw", new Rectangle(r.Right - 56, r.Y + 16, 44, 22), t.On, hover);
		}
		int rows = (toggles.Length + columns - 1) / columns;
		return rows * height + (rows - 1) * gap;
	}

	#endregion
}
