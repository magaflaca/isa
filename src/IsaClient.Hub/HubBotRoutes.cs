using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.MultiClient;
using IsaClient.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// Bots > Connection: where bots connect from. The switch, the server's limit per IP, where the proxies come from
/// (fastest, one country or the ones you pick) and, right now, every exit (your IP and each proxy) with its bots in it.
/// </summary>
internal static class HubBotRoutes
{
	private static readonly Color Green = new Color(80, 220, 140);

	private static readonly Color Gold = new Color(255, 205, 90);

	private static HubTheme Theme => HubTheme.Current;

	private static string _search = "";

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		ProxyPool.Start();
		int start = y;
		y += Hero(sb, x, y, width) + 14;
		y += Exits(sb, x, y, width) + 14;
		y += Header(sb, HubIcon.Item(I.Sign), "Server limit", "How many players it lets in from one IP", x, y, width);
		y += Limit(sb, x, y, width) + 14;
		y += Header(sb, HubIcon.Item(I.WorldGlobe), "Proxies", "Where the bots that need one come from", x, y, width);
		y += Modes(sb, x, y, width) + 10;
		y += BotRoutes.Mode switch
		{
			BotRouteMode.Country => Countries(sb, x, y, width),
			BotRouteMode.Chosen => Picker(sb, x, y, width),
			_ => Preview(sb, x, y, width)
		};
		y += 10;
		y += Toggle(sb, "br:fallback", "If no proxy works, use my IP", "The bot tries from your IP instead of waiting (the server may turn it away)", BotRoutes.FallbackDirect, v => BotRoutes.FallbackDirect = v, x, y, width) + 8;
		return y - start;
	}

	#region Hero

	private static int Hero(SpriteBatch sb, int x, int y, int width)
	{
		bool on = BotRoutes.Enabled;
		Rectangle r = new Rectangle(x, y, width, 96);
		float glow = HubInput.Anim("br:on", on, 0.12f);
		bool hover = HubInput.In(r);
		if (hover && HubInput.Hover("br:hero", on ? "Click: bots connect only from your IP" : "Click: bots go through proxies when your IP is full"))
		{
			BotRoutes.TurnOn(!on);
			on = !on;
			HubInput.Sound(on ? 10 : 11);
		}
		HubKit.Box(sb, r, Color.Lerp(Color.Lerp(Theme.Card, Theme.CardHover, HubInput.Anim("br:hero", hover) * 0.5f), new Color(20, 70, 50), glow * 0.55f) * HubPrefs.Opacity);
		// A globe with the robot hat in front: bots around the world.
		Rectangle icon = new Rectangle(r.X + 18, r.Y + 14, 68, 68);
		HubKit.Box(sb, icon, Color.Lerp(new Color(16, 20, 38), new Color(18, 60, 44), glow) * 0.9f);
		HubIcon.Item(I.WorldGlobe).Draw(sb, new Rectangle(icon.X + 8, icon.Y + 6, 44, 44), Color.White * (0.55f + 0.45f * glow));
		HubIcon.Item(I.RobotHat).Draw(sb, new Rectangle(icon.Right - 34, icon.Bottom - 32, 28, 28), Color.White);
		int tx = icon.Right + 18;
		int tw = r.Right - tx - 90;
		HubKit.Text(sb, "Bots through proxies", tx, r.Y + 14, on ? Color.Lerp(Green, Color.White, 0.2f) : Theme.Text, 1.05f, tw);
		HubKit.Text(sb, BotRoutes.Summary, tx, r.Y + 44, Theme.Muted, 0.68f, tw);
		int working = ProxyPool.WorkingCount;
		string pool = (working > 0) ? (working + " working proxies in " + ProxyPool.Countries().Count + " countries") : (ProxyPool.Activity ?? "Looking for working proxies...");
		HubKit.Text(sb, pool, tx, r.Y + 66, (working > 0) ? Green * 0.9f : Gold, 0.62f, tw);
		HubKit.Switch(sb, "br:sw", new Rectangle(r.Right - 74, r.Y + 34, 56, 28), on, hover);
		return r.Height;
	}

	#endregion

	#region Exits

	/// <summary>Where every bot is connected from right now: your IP and each proxy, with its places.</summary>
	private static int Exits(SpriteBatch sb, int x, int y, int width)
	{
		List<BotRoutes.Exit> exits = BotRoutes.Exits();
		int start = y;
		HubKit.Text(sb, "Right now", x + 2, y, Theme.Text, 0.8f);
		y += 24;
		if (exits.Count == 0)
		{
			Rectangle empty = new Rectangle(x, y, width, 46);
			HubKit.Box(sb, empty, new Color(10, 12, 24) * 0.45f);
			HubKit.Text(sb, "No bot online. Each one you start shows up here, under the IP it uses.", empty.X + 14, empty.Y + 14, Theme.Muted, 0.66f, empty.Width - 28);
			return y + empty.Height - start;
		}
		int columns = (width >= 640) ? 2 : 1;
		const int gap = 8;
		int tileW = (width - (columns - 1) * gap) / columns;
		const int tileH = 62;
		for (int i = 0; i < exits.Count; i++)
		{
			Rectangle r = new Rectangle(x + i % columns * (tileW + gap), y + i / columns * (tileH + gap), tileW, tileH);
			if (HubInput.Visible(r))
			{
				ExitTile(sb, exits[i], r);
			}
		}
		int rows = (exits.Count + columns - 1) / columns;
		return y + rows * (tileH + gap) - gap - start;
	}

	private static void ExitTile(SpriteBatch sb, BotRoutes.Exit exit, Rectangle r)
	{
		bool mine = exit.Proxy == null;
		HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
		HubKit.Fill(sb, new Rectangle(r.X + 3, r.Y + 10, 3, r.Height - 20), mine ? Theme.Accent : Green);
		Rectangle badge = new Rectangle(r.X + 14, r.Y + 17, 36, 24);
		if (mine)
		{
			HubIcon.Item(I.MagicMirror).Draw(sb, new Rectangle(r.X + 18, r.Y + 13, 30, 30), Color.White);
		}
		else if (!string.IsNullOrEmpty(exit.Proxy.Country))
		{
			CountryFlags.Draw(sb, exit.Proxy.Country, badge);
		}
		else
		{
			HubIcon.Item(I.WorldGlobe).Draw(sb, new Rectangle(r.X + 18, r.Y + 13, 30, 30), Color.White);
		}
		int tx = r.X + 62;
		string title = mine ? "Your IP" : (string.IsNullOrEmpty(exit.Proxy.Country) ? exit.Proxy.Host : CountryNames.Name(exit.Proxy.Country));
		string sub = mine ? (exit.You ? "you and your bots" : "your bots") : (exit.Proxy.ProtocolName + "  " + exit.Proxy.Address);
		HubKit.Text(sb, title, tx, r.Y + 9, Color.White, 0.78f, r.Width - tx + r.X - 120);
		HubKit.Text(sb, sub, tx, r.Y + 30, Theme.Muted, 0.6f, r.Width - tx + r.X - 120);
		// The places: one pip per connection the server allows from this IP.
		int per = BotRoutes.PerIp;
		int pip = 12;
		int px = r.Right - 14 - per * (pip + 4);
		for (int i = 0; i < per; i++)
		{
			Rectangle p = new Rectangle(px + i * (pip + 4), r.Y + 12, pip, pip);
			bool used = i < exit.Used;
			bool you = exit.You && i == 0;
			HubKit.Fill(sb, p, used ? (you ? Theme.Accent : Green) : Theme.Muted * 0.22f);
		}
		HubKit.Text(sb, exit.Used + "/" + per, r.Right - 14 - HubKit.Measure(exit.Used + "/" + per, 0.6f), r.Y + 30, (exit.Used >= per) ? Gold : Theme.Muted, 0.6f);
		if (!mine)
		{
			HubProxy.Signal(sb, new Rectangle(px - 28, r.Y + 9, 18, 18), exit.Proxy.Ping);
		}
		if (HubInput.In(r))
		{
			HubInput.Hover("br:exit:" + exit.Key, string.Join(", ", exit.Bots.Select(b => b.Name)));
		}
	}

	#endregion

	#region Limit

	private static int Limit(SpriteBatch sb, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 64);
		HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
		HubKit.Text(sb, "Connections per IP", r.X + 14, r.Y + 10, Theme.Text, 0.8f);
		int direct = Math.Max(0, BotRoutes.PerIp - 1);
		string hint = BotRoutes.MyIpFirst
			? ((direct == 0) ? "Only you from your IP: every bot goes through a proxy." : ("You and " + direct + ((direct == 1) ? " bot" : " bots") + " from your IP, " + BotRoutes.PerIp + " per proxy."))
			: ("Every bot goes through a proxy, " + BotRoutes.PerIp + " per proxy.");
		HubKit.Text(sb, hint, r.X + 14, r.Y + 34, Theme.Muted, 0.64f, r.Width - 220);
		Rectangle minus = new Rectangle(r.Right - 176, r.Y + 16, 36, 32);
		Rectangle value = new Rectangle(minus.Right + 4, r.Y + 16, 56, 32);
		Rectangle plus = new Rectangle(value.Right + 4, r.Y + 16, 36, 32);
		if (HubKit.Button(sb, "br:per:-", minus, "-", Theme.Slot, "One less", BotRoutes.PerIp > BotRoutes.MinPerIp))
		{
			BotRoutes.PerIp--;
			MultiClientManager.Save();
		}
		HubKit.Box(sb, value, new Color(10, 12, 24) * 0.6f);
		HubKit.TextCentered(sb, BotRoutes.PerIp.ToString(), value, Gold, 0.95f);
		if (HubKit.Button(sb, "br:per:+", plus, "+", Theme.Slot, "One more", BotRoutes.PerIp < BotRoutes.MaxPerIp))
		{
			BotRoutes.PerIp++;
			MultiClientManager.Save();
		}
		y += r.Height + 8;
		return r.Height + 8 + Toggle(sb, "br:myip", "Use my IP first", "Bots take the free places of your own IP before any proxy (faster and steadier)", BotRoutes.MyIpFirst, v => BotRoutes.MyIpFirst = v, x, y, width);
	}

	#endregion

	#region Modes

	private static int Modes(SpriteBatch sb, int x, int y, int width)
	{
		(BotRouteMode Mode, int Icon, string Title, string Line)[] modes =
		{
			(BotRouteMode.Fastest, I.SwiftnessPotion, "Automatic", "The fastest proxies, anywhere"),
			(BotRouteMode.Country, I.Compass, "One country", (BotRoutes.Mode == BotRouteMode.Country && BotRoutes.Country.Length > 0) ? CountryNames.Name(BotRoutes.Country) : "Pick it below"),
			(BotRouteMode.Chosen, I.GoldenKey, "My pick", (BotRoutes.Chosen.Count > 0) ? (BotRoutes.Chosen.Count + " picked") : "Choose them below")
		};
		const int gap = 8;
		int tileW = (width - gap * 2) / 3;
		int h = 58;
		for (int i = 0; i < modes.Length; i++)
		{
			var m = modes[i];
			Rectangle r = new Rectangle(x + i * (tileW + gap), y, tileW, h);
			string id = "br:mode:" + m.Mode;
			bool active = BotRoutes.Mode == m.Mode;
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover(id) && !active)
			{
				if (m.Mode == BotRouteMode.Fastest)
				{
					BotRoutes.UseFastest();
				}
				else if (m.Mode == BotRouteMode.Country)
				{
					BotRoutes.UseCountry(BotRoutes.Country.Length > 0 ? BotRoutes.Country : (ProxyPool.Countries().OrderBy(c => c.BestPing).FirstOrDefault()?.Code ?? ""));
				}
				else
				{
					BotRoutes.Mode = BotRouteMode.Chosen;
					MultiClientManager.Save();
				}
				HubInput.Sound(12);
			}
			float a = HubInput.Anim(id + ":a", active);
			Color back = Color.Lerp(Color.Lerp(Theme.Card, Theme.CardHover, HubInput.Anim(id, hover)), Color.Lerp(Theme.Slot, Theme.Accent, 0.3f), a);
			HubKit.Box(sb, r, back * HubPrefs.Opacity);
			if (a > 0.01f)
			{
				HubKit.Fill(sb, new Rectangle(r.X + 8, r.Bottom - 5, r.Width - 16, 2), Theme.Accent * a);
			}
			HubIcon.Item(m.Icon).Draw(sb, new Rectangle(r.X + 12, r.Y + 15, 28, 28), Color.White);
			HubKit.Text(sb, m.Title, r.X + 50, r.Y + 10, active ? Color.White : Theme.Text, 0.76f, r.Width - 58);
			HubKit.Text(sb, m.Line, r.X + 50, r.Y + 31, Theme.Muted, 0.6f, r.Width - 58);
		}
		return h;
	}

	/// <summary>Automatic: the proxies the next bots would take.</summary>
	private static int Preview(SpriteBatch sb, int x, int y, int width)
	{
		List<ProxyServer> best = ProxyPool.Best(null, 6);
		if (best.Count == 0)
		{
			return Empty(sb, x, y, width, "Finding working proxies...", "The first time this takes a minute: each one is tested with a real Terraria connection.");
		}
		int start = y;
		HubKit.Text(sb, "The next bots would use", x + 2, y, Theme.Muted, 0.66f);
		y += 22;
		foreach (ProxyServer server in best)
		{
			ProxyRow(sb, server, new Rectangle(x, y, width, 34), null);
			y += 38;
		}
		return y - start;
	}

	/// <summary>One country: the countries with working proxies.</summary>
	private static int Countries(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		_search = HubKit.Field(sb, "br:search", new Rectangle(x, y, Math.Min(360, width), 32), _search, "Search a country", 40, HubIcon.Texture("Images/UI/Cursor_2"));
		y += 42;
		string query = _search.Trim();
		List<ProxyPool.CountryInfo> countries = ProxyPool.Countries()
			.Where(c => query.Length == 0 || CountryNames.Name(c.Code).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || c.Code.Equals(query, StringComparison.OrdinalIgnoreCase))
			.OrderByDescending(c => c.Code == BotRoutes.Country).ThenByDescending(c => c.Servers >= 3).ThenBy(c => c.BestPing).ToList();
		if (countries.Count == 0)
		{
			return y - start + Empty(sb, x, y, width, (query.Length > 0) ? "No country matches." : "Finding working proxies...", (query.Length > 0) ? "Try another name, or wait: new proxies keep coming." : "Countries show up as their proxies pass the test.");
		}
		const int gap = 6;
		int columns = (width >= 640) ? 2 : 1;
		int tileW = (width - (columns - 1) * gap) / columns;
		const int h = 46;
		for (int i = 0; i < countries.Count; i++)
		{
			ProxyPool.CountryInfo c = countries[i];
			Rectangle r = new Rectangle(x + i % columns * (tileW + gap), y + i / columns * (h + gap), tileW, h);
			if (!HubInput.Visible(r))
			{
				continue;
			}
			string id = "br:c:" + c.Code;
			bool picked = BotRoutes.Mode == BotRouteMode.Country && BotRoutes.Country == c.Code;
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover(id, "Bots use proxies of " + CountryNames.Name(c.Code)) && !picked)
			{
				BotRoutes.UseCountry(c.Code);
				HubInput.Sound(12);
			}
			Pickable(sb, r, id, hover, picked);
			CountryFlags.Draw(sb, c.Code, new Rectangle(r.X + 14, r.Y + 11, 36, 24));
			HubKit.Text(sb, CountryNames.Name(c.Code), r.X + 60, r.Y + 6, Color.White, 0.74f, r.Width - 120);
			// Room for this many bots: proxies times the limit.
			HubKit.Text(sb, c.Servers + ((c.Servers == 1) ? " proxy" : " proxies") + "  ·  room for " + c.Servers * BotRoutes.PerIp + " bots", r.X + 60, r.Y + 25, Theme.Muted, 0.58f, r.Width - 120);
			HubProxy.Signal(sb, new Rectangle(r.Right - 34, r.Y + 13, 20, 20), c.BestPing);
		}
		int rows = (countries.Count + columns - 1) / columns;
		return y + rows * (h + gap) - start;
	}

	/// <summary>My pick: the best proxies and your own, each one picked or not.</summary>
	private static int Picker(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		List<ProxyServer> mine = Proxy.Mine;
		List<ProxyServer> list = new List<ProxyServer>();
		foreach (string key in BotRoutes.Chosen)
		{
			ProxyServer s = ProxyPool.Find(key) ?? mine.FirstOrDefault(m => m.Key == key);
			if (s != null)
			{
				list.Add(s);
			}
		}
		list.AddRange(mine.Where(m => list.All(l => l.Key != m.Key)));
		list.AddRange(ProxyPool.Best(null, 40).Where(b => list.All(l => l.Key != b.Key)));
		int picked = BotRoutes.Chosen.Count;
		HubKit.Text(sb, (picked == 0) ? "Click the proxies the bots may use. Yours (My Proxies) come first." : (picked + " picked: room for " + picked * BotRoutes.PerIp + " bots"), x + 2, y, (picked == 0) ? Theme.Muted : Green, 0.66f);
		if (picked > 0 && HubKit.Button(sb, "br:clear", new Rectangle(x + width - 96, y - 6, 96, 26), "Clear", Theme.Slot, "Unpick them all"))
		{
			BotRoutes.Chosen = new List<string>();
			MultiClientManager.Save();
		}
		y += 26;
		if (list.Count == 0)
		{
			return y - start + Empty(sb, x, y, width, "Finding working proxies...", "Or add your own in Proxy > My Proxies.");
		}
		foreach (ProxyServer server in list)
		{
			Rectangle r = new Rectangle(x, y, width, 34);
			if (HubInput.Visible(r))
			{
				ProxyRow(sb, server, r, mine);
			}
			y += 38;
		}
		return y - start;
	}

	/// <summary>A proxy line: flag, protocol, address, place, ping. With <paramref name="mine"/> it can be picked.</summary>
	private static void ProxyRow(SpriteBatch sb, ProxyServer server, Rectangle r, List<ProxyServer> mine)
	{
		string id = "br:p:" + server.Key;
		bool pickable = mine != null;
		bool picked = pickable && BotRoutes.IsChosen(server);
		bool hover = HubInput.In(r);
		if (pickable && hover && HubInput.Hover(id, picked ? "Click: don't use it" : "Click: bots may use it"))
		{
			BotRoutes.ToggleChosen(server);
			HubInput.Sound(12);
		}
		Pickable(sb, r, id, hover && pickable, picked);
		int tx = r.X + 12;
		if (pickable)
		{
			Rectangle box = new Rectangle(tx, r.Y + 9, 16, 16);
			HubKit.Box(sb, box, new Color(10, 12, 24) * 0.8f);
			if (picked)
			{
				HubKit.Fill(sb, new Rectangle(box.X + 4, box.Y + 4, 8, 8), Green);
			}
			tx += 26;
		}
		if (!string.IsNullOrEmpty(server.Country))
		{
			CountryFlags.Draw(sb, server.Country, new Rectangle(tx, r.Y + 8, 27, 18));
		}
		tx += 36;
		HubKit.Chip(sb, server.ProtocolName, tx, r.Y + 6, HubProxy.ProtocolColor(server.Protocol) * 0.4f, Color.White, 0.56f);
		tx += 64;
		HubKit.Text(sb, server.Address, tx, r.Y + 8, Theme.Text, 0.66f, 190);
		bool own = mine != null && mine.Any(m => m.Key == server.Key);
		string place = own ? "My proxy" : (string.IsNullOrEmpty(server.Country) ? "" : (CountryNames.Name(server.Country) + (string.IsNullOrEmpty(server.City) ? "" : (", " + server.City))));
		HubKit.Text(sb, place, tx + 200, r.Y + 8, own ? Gold : Theme.Muted, 0.62f, Math.Max(10, r.Right - tx - 200 - 150));
		HubKit.Text(sb, (server.Ping >= 0) ? (server.Ping + " ms") : "untested", r.Right - 140, r.Y + 8, HubProxy.PingColor(server.Ping), 0.62f);
		HubKit.Text(sb, (int)Math.Round(server.Reliability * 100) + "%", r.Right - 70, r.Y + 8, Theme.Muted, 0.6f);
		HubProxy.Signal(sb, new Rectangle(r.Right - 30, r.Y + 8, 18, 18), server.Ping);
	}

	#endregion

	#region Pieces

	private static void Pickable(SpriteBatch sb, Rectangle r, string id, bool hover, bool picked)
	{
		float h = HubInput.Anim(id, hover);
		float s = HubInput.Anim(id + ":sel", picked);
		HubKit.Box(sb, r, Color.Lerp(Color.Lerp(Theme.Card, Theme.CardHover, h), Color.Lerp(Theme.Card, Green, 0.22f), s) * HubPrefs.Opacity);
		if (s > 0.01f)
		{
			HubKit.Fill(sb, new Rectangle(r.X + 4, r.Y + 8, 3, r.Height - 16), Green * s);
		}
	}

	private static int Header(SpriteBatch sb, HubIcon icon, string title, string hint, int x, int y, int width)
	{
		icon.Draw(sb, new Rectangle(x, y + 2, 24, 24), Color.White);
		HubKit.Text(sb, title, x + 32, y + 4, Theme.Accent, 0.9f);
		float tw = HubKit.Measure(title, 0.9f);
		HubKit.Text(sb, hint, x + 44 + tw, y + 7, Theme.Muted, 0.66f, width - 44 - tw);
		return 38;
	}

	private static int Toggle(SpriteBatch sb, string id, string title, string tip, bool on, Action<bool> set, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 52);
		bool hover = HubInput.In(r);
		if (hover && HubInput.Hover(id))
		{
			set(!on);
			on = !on;
			MultiClientManager.Save();
			HubInput.Sound(on ? 10 : 11);
		}
		HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.CardHover, HubInput.Anim(id, hover)) * HubPrefs.Opacity);
		HubKit.Text(sb, title, r.X + 14, r.Y + 8, on ? Theme.Text : Theme.Muted, 0.78f, r.Width - 90);
		HubKit.Text(sb, tip, r.X + 14, r.Y + 29, Theme.Muted, 0.62f, r.Width - 90);
		HubKit.Switch(sb, id + ":sw", new Rectangle(r.Right - 58, r.Y + 15, 44, 22), on, hover);
		return r.Height;
	}

	private static int Empty(SpriteBatch sb, int x, int y, int width, string title, string text)
	{
		Rectangle r = new Rectangle(x, y, width, 62);
		HubKit.Box(sb, r, new Color(10, 12, 24) * 0.45f);
		HubIcon.Item(I.WorldGlobe).Draw(sb, new Rectangle(r.X + 16, r.Y + 15, 32, 32), Color.White * 0.85f);
		HubKit.Text(sb, title, r.X + 60, r.Y + 12, Theme.Text, 0.78f);
		HubKit.Text(sb, text, r.X + 60, r.Y + 34, Theme.Muted, 0.64f, r.Width - 70);
		return r.Height;
	}

	#endregion
}
