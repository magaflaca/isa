using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// The proxy as a VPN: a big switch with the location it connects through, countries with flags (servers, best ping,
/// signal bars, favorites), your own proxies, and the settings of the background search. Drawn in the main menu window
/// (<see cref="IsaClient.Menu.ProxyMenu"/>) and in the panel's Proxy section.
/// </summary>
internal static class HubProxy
{
	private static readonly Color Green = new Color(80, 220, 140);

	private static string _search = "";

	private static int _sort;

	private static string _expanded;

	private static string _newProxy = "";

	private static ProxyProtocol _newProtocol = ProxyProtocol.Socks5;

	private static string _addError;

	private static string _newBlock = "";

	private static HubTheme Theme => HubTheme.Current;

	/// <summary>Set by the window: clicking the location card opens the Locations tab.</summary>
	public static Action ShowLocations;

	/// <summary>"On, Germany" for a card.</summary>
	public static string Summary()
	{
		Proxy.EnsureLoaded();
		return Proxy.Enabled ? ("On, " + Proxy.ChoiceName) : "Off";
	}

	#region Connection card

	/// <summary>Height the connection card wants at this width.</summary>
	public static int HeroHeight(int width)
	{
		return (width >= 560) ? 172 : 430;
	}

	/// <summary>The switch, the status and the location: side by side when wide, stacked in a column when narrow.</summary>
	public static void DrawHero(SpriteBatch sb, Rectangle r)
	{
		Proxy.EnsureLoaded();
		ProxyPool.Start();
		bool on = Proxy.Enabled;
		float glow = HubInput.Anim("px:on", on, 0.12f);
		HubKit.Box(sb, r, Color.Lerp(Theme.Card, new Color(20, 70, 50), glow * 0.55f) * HubPrefs.Opacity);
		bool wide = r.Width >= 560;
		int size = wide ? 116 : 136;
		Rectangle power = wide ? new Rectangle(r.X + 26, r.Y + (r.Height - size) / 2, size, size) : new Rectangle(r.Center.X - size / 2, r.Y + 22, size, size);
		PowerButton(sb, power, on, glow);

		int tx = wide ? power.Right + 26 : r.X + 18;
		int ty = wide ? r.Y + 22 : power.Bottom + 18;
		int tw = wide ? r.Right - tx - 18 : r.Width - 36;
		string state = Proxy.Connected ? "Connected" : (on ? "Protected" : "Not protected");
		Color stateColor = on ? Color.Lerp(Green, Color.White, 0.15f) : Theme.Muted;
		float stateScale = 1.25f;
		if (wide)
		{
			HubKit.Text(sb, state, tx, ty, stateColor, stateScale, tw);
		}
		else
		{
			HubKit.TextCentered(sb, state, new Rectangle(r.X, ty, r.Width, 30), stateColor, stateScale);
		}
		ty += 34;
		string line = StatusText();
		string[] lines = HubKit.Wrap(line, tw, 0.7f, 3);
		for (int i = 0; i < lines.Length; i++)
		{
			if (wide)
			{
				HubKit.Text(sb, lines[i], tx, ty + i * 17, Theme.Muted, 0.7f);
			}
			else
			{
				HubKit.TextCentered(sb, lines[i], new Rectangle(r.X + 10, ty + i * 17, r.Width - 20, 17), Theme.Muted, 0.7f);
			}
		}
		ty += Math.Max(2, lines.Length) * 17 + 10;

		// The location: click to change it.
		Rectangle card = new Rectangle(tx, ty, wide ? Math.Min(tw, 420) : tw, 50);
		LocationCard(sb, card);
		ty = card.Bottom + 10;

		if (!wide)
		{
			Rectangle fastest = new Rectangle(tx, ty, tw, 32);
			if (HubKit.Button(sb, "px:fast", fastest, "Fastest location", Proxy.Mode == ProxyMode.Fastest && on ? Theme.Slot : Theme.On * 0.55f, "Connect through the best proxy anywhere", true, HubIcon.Item(I.SwiftnessPotion)))
			{
				Proxy.UseFastest();
				HubInput.Sound(12);
			}
			ty = fastest.Bottom + 12;
			PoolStatus(sb, new Rectangle(tx, r.Bottom - 54, tw, 42), true);
		}
		else
		{
			Rectangle fastest = new Rectangle(card.Right + 10, card.Y + 9, 150, 32);
			if (fastest.Right <= r.Right - 14 && HubKit.Button(sb, "px:fast", fastest, "Fastest", Proxy.Mode == ProxyMode.Fastest && on ? Theme.Slot : Theme.On * 0.55f, "Connect through the best proxy anywhere", true, HubIcon.Item(I.SwiftnessPotion)))
			{
				Proxy.UseFastest();
				HubInput.Sound(12);
			}
		}
	}

	private static string StatusText()
	{
		if (Proxy.Connected)
		{
			ProxyServer active = Proxy.Active;
			return "You are playing through " + CountryNames.Name(active.Country) + " (" + active.Address + ", " + active.ProtocolName + "). The server sees that address, not yours.";
		}
		if (!Proxy.Enabled)
		{
			return (Proxy.LastError != null) ? ("Last try: " + Proxy.LastError) : "Turn it on to hide your IP from the servers you join.";
		}
		List<ProxyServer> candidates = Proxy.Candidates();
		string next = (Main.gameMenu ? "Your next server connection" : "From your next connection, it") + " goes through " + Proxy.ChoiceName + ".";
		if (candidates.Count == 0)
		{
			return next + " No working server there yet: still looking...";
		}
		return next + " Best server: " + candidates[0].Ping + " ms.";
	}

	private static void PowerButton(SpriteBatch sb, Rectangle r, bool on, float glow)
	{
		bool hover = HubInput.In(r);
		if (hover && HubInput.Hover("px:power", on ? "Turn the proxy off (direct connection)" : "Turn the proxy on"))
		{
			Proxy.TurnOn(!on);
			HubInput.Sound(on ? 11 : 10);
		}
		float h = HubInput.Anim("px:power:h", hover);
		float pulse = 0.5f + 0.5f * (float)Math.Sin(Main.timeForVisualEffects * 0.05);
		Vector2 center = r.Center.ToVector2();
		// Halo, outer ring, the disc and the shield.
		float halo = r.Width * (1.18f + 0.06f * pulse * glow);
		sb.Draw(CountryFlags.Disc, center, null, Green * (0.16f * glow + 0.05f * h), 0f, new Vector2(64f), halo / 128f, SpriteEffects.None, 0f);
		sb.Draw(CountryFlags.Disc, center, null, Color.Lerp(new Color(28, 32, 48), new Color(18, 60, 42), glow), 0f, new Vector2(64f), r.Width / 128f, SpriteEffects.None, 0f);
		sb.Draw(CountryFlags.Ring, center, null, Color.Lerp(new Color(90, 98, 130), Green, glow) * (0.8f + 0.2f * h), 0f, new Vector2(64f), r.Width / 128f, SpriteEffects.None, 0f);
		// The spinner while it is still looking for its first proxies.
		if (on && Proxy.Candidates().Count == 0)
		{
			float angle = (float)(Main.timeForVisualEffects * 0.08);
			for (int i = 0; i < 8; i++)
			{
				float a = angle + i * MathHelper.TwoPi / 8f;
				Vector2 dot = center + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * (r.Width / 2f + 9f);
				sb.Draw(CountryFlags.Disc, dot, null, Green * (0.15f + 0.1f * i), 0f, new Vector2(64f), 7f / 128f, SpriteEffects.None, 0f);
			}
		}
		int icon = (int)(r.Width * (0.5f + 0.04f * h));
		HubIcon.Item(on ? I.CobaltShield : I.CobaltShield).Draw(sb, new Rectangle((int)center.X - icon / 2, (int)center.Y - icon / 2 - 6, icon, icon), on ? Color.White : (Color.White * 0.45f), 4f);
		string label = on ? "ON" : "OFF";
		HubKit.TextCentered(sb, label, new Rectangle(r.X, r.Bottom - 34, r.Width, 20), on ? Green : Theme.Muted, 0.72f);
	}

	private static void LocationCard(SpriteBatch sb, Rectangle r)
	{
		bool hover = HubInput.In(r);
		if (hover && HubInput.Hover("px:loc", "Change the location") && ShowLocations != null)
		{
			ShowLocations();
			HubInput.Sound(12);
		}
		float h = HubInput.Anim("px:loc", hover);
		HubKit.Box(sb, r, Color.Lerp(Theme.Slot, Theme.CardHover, h) * 0.95f);
		CountryFlags.Draw(sb, Proxy.ChoiceFlag, new Rectangle(r.X + 12, r.Y + 13, 36, 24));
		HubKit.Text(sb, "Location", r.X + 60, r.Y + 6, Theme.Muted, 0.62f);
		HubKit.Text(sb, Proxy.ChoiceName, r.X + 60, r.Y + 23, Color.White, 0.82f, r.Width - 60 - (ShowLocations != null ? 70 : 12));
		if (ShowLocations != null)
		{
			HubKit.Text(sb, "Change", r.Right - 66, r.Y + 17, Color.Lerp(Theme.Accent, Color.White, h), 0.68f);
		}
	}

	/// <summary>"Finding proxies: 1,240 tested, 87 working in 23 countries" with a moving bar while it works.</summary>
	private static void PoolStatus(SpriteBatch sb, Rectangle r, bool compact)
	{
		int working = ProxyPool.WorkingCount;
		int countries = ProxyPool.Countries().Count;
		string activity = ProxyPool.Activity;
		string text;
		if (!ProxyPool.CanTest)
		{
			text = "Can't reach the test server: check your internet.";
		}
		else if (ProxyPool.Paused)
		{
			text = "Background search paused (Settings).";
		}
		else
		{
			text = working + " working in " + countries + " " + (countries == 1 ? "country" : "countries");
		}
		HubKit.Text(sb, text, r.X, r.Y, Theme.Text * 0.9f, 0.68f, r.Width);
		string sub = (activity != null) ? (activity + "... " + ProxyPool.Waiting + " to go") : (ProxyPool.Enough ? ("Found " + ProxyPool.Target + ": search resting, the good ones are checked every few minutes") : ("Updated " + Ago(ProxyPool.ListsUpdated)));
		HubKit.Text(sb, sub, r.X, r.Y + 17, Theme.Muted, 0.62f, r.Width);
		Rectangle bar = new Rectangle(r.X, r.Bottom - 4, r.Width, 3);
		HubKit.Fill(sb, bar, Theme.Slot * 0.8f);
		if (activity != null)
		{
			float t = (float)(Main.timeForVisualEffects * 0.012 % 1.0);
			int w = r.Width / 4;
			int x = r.X + (int)((r.Width + w) * t) - w;
			Rectangle moving = Rectangle.Intersect(new Rectangle(x, bar.Y, w, bar.Height), bar);
			HubKit.Fill(sb, moving, Theme.Accent);
		}
		else
		{
			HubKit.Fill(sb, bar, Green * 0.5f);
		}
	}

	#endregion

	#region Locations

	/// <summary>Panel page: the connection card, then the countries.</summary>
	public static int DrawConnectPage(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		ShowLocations = null;
		int heroH = HeroHeight(width);
		DrawHero(sb, new Rectangle(x, y, width, heroH));
		y += heroH + 8;
		PoolStatus(sb, new Rectangle(x + 4, y, width - 8, 40), false);
		y += 50;
		y += DrawLocations(sb, x, y, width);
		return y - start;
	}

	/// <summary>Search, sort, Fastest location, favorites and every country with working proxies.</summary>
	public static int DrawLocations(SpriteBatch sb, int x, int y, int width)
	{
		Proxy.EnsureLoaded();
		ProxyPool.Start();
		int start = y;
		Rectangle field = new Rectangle(x, y, Math.Max(160, width - 300), 32);
		_search = HubKit.Field(sb, "px:search", field, _search, "Search a country", 40, HubIcon.Texture("Images/UI/Cursor_2"));
		string[] sorts = { "Fastest", "A-Z", "Most servers" };
		int cx = field.Right + 8;
		for (int i = 0; i < sorts.Length; i++)
		{
			int w = (int)HubKit.Measure(sorts[i], 0.68f) + 20;
			if (Chip(sb, "px:sort:" + i, new Rectangle(cx, y + 2, w, 28), sorts[i], _sort == i, Theme.Accent))
			{
				_sort = i;
			}
			cx += w + 5;
		}
		y += 44;

		List<ProxyPool.CountryInfo> countries = ProxyPool.Countries();
		string query = _search.Trim();
		if (query.Length > 0)
		{
			countries = countries.Where(c => CountryNames.Name(c.Code).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || c.Code.Equals(query, StringComparison.OrdinalIgnoreCase)).ToList();
		}
		countries = _sort switch
		{
			1 => countries.OrderBy(c => CountryNames.Name(c.Code)).ToList(),
			2 => countries.OrderByDescending(c => c.Servers).ThenBy(c => c.BestPing).ToList(),
			_ => countries.OrderBy(c => c.BestPing).ToList()
		};

		if (query.Length == 0)
		{
			y += FastestRow(sb, x, y, width) + 6;
		}
		List<ProxyPool.CountryInfo> favorites = countries.Where(c => Proxy.FavoriteCountries.Contains(c.Code)).ToList();
		if (favorites.Count > 0)
		{
			y += Heading(sb, "Favorites", x, y);
			foreach (ProxyPool.CountryInfo country in favorites)
			{
				y += CountryRow(sb, country, x, y, width, "fav") + 4;
			}
			y += 8;
		}
		y += Heading(sb, (query.Length > 0) ? ("Results (" + countries.Count + ")") : ("All countries (" + countries.Count + ")"), x, y);
		if (countries.Count == 0)
		{
			y += Empty(sb, x, y, width, query.Length > 0 ? "No country with working proxies matches." : "Finding working proxies...", query.Length > 0 ? "Try another name, or wait: new ones keep coming." : "The first time this takes a minute. They are tested for real, so the ones here connect.");
		}
		foreach (ProxyPool.CountryInfo country in countries)
		{
			y += CountryRow(sb, country, x, y, width, "all") + 4;
		}
		return y - start;
	}

	private static int FastestRow(SpriteBatch sb, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 52);
		bool selected = Proxy.Enabled && Proxy.Mode == ProxyMode.Fastest;
		bool hover = HubInput.In(r);
		if (hover && HubInput.Hover("px:row:fast", "Connect through the best proxy anywhere"))
		{
			Proxy.UseFastest();
			HubInput.Sound(12);
		}
		RowBack(sb, r, "px:row:fast", hover, selected);
		HubIcon.Item(I.SwiftnessPotion).Draw(sb, new Rectangle(r.X + 18, r.Y + 12, 28, 28), Color.White);
		HubKit.Text(sb, "Fastest location", r.X + 62, r.Y + 8, Color.White, 0.84f);
		List<ProxyServer> best = ProxyPool.Best(null, 1);
		HubKit.Text(sb, (best.Count > 0) ? ("Right now: " + CountryNames.Name(best[0].Country) + ", " + best[0].Ping + " ms") : "Picks the best server anywhere", r.X + 62, r.Y + 29, Theme.Muted, 0.66f, r.Width - 200);
		if (best.Count > 0)
		{
			Signal(sb, new Rectangle(r.Right - 60, r.Y + 15, 22, 22), best[0].Ping);
		}
		if (selected)
		{
			HubKit.Chip(sb, "Selected", r.Right - 72, r.Y + 16, Green * 0.35f, Color.White, 0.62f, alignRight: true);
		}
		return r.Height;
	}

	private static int CountryRow(SpriteBatch sb, ProxyPool.CountryInfo country, int x, int y, int width, string list)
	{
		string id = "px:c:" + list + ":" + country.Code;
		bool open = _expanded == list + country.Code;
		List<ProxyServer> servers = open ? ProxyPool.Best(country.Code, 12) : null;
		int height = 52 + (open ? 8 + servers.Count * 34 + 4 : 0);
		Rectangle r = new Rectangle(x, y, width, 52);
		if (!HubInput.Visible(new Rectangle(x, y, width, height)))
		{
			return height;
		}
		bool selected = Proxy.Enabled && ((Proxy.Mode == ProxyMode.Country && Proxy.Country == country.Code) || (Proxy.Mode == ProxyMode.Server && Proxy.Country == country.Code));
		Rectangle star = new Rectangle(r.Right - 112, r.Y + 14, 24, 24);
		Rectangle chevron = new Rectangle(r.Right - 40, r.Y + 12, 28, 28);
		bool overButtons = HubInput.In(star) || HubInput.In(chevron);
		bool hover = HubInput.In(r) && !overButtons;
		if (hover && HubInput.Hover(id, "Connect through " + CountryNames.Name(country.Code)))
		{
			Proxy.UseCountry(country.Code);
			HubInput.Sound(12);
		}
		RowBack(sb, r, id, hover, selected);
		if (open)
		{
			HubKit.Box(sb, new Rectangle(x + 10, r.Bottom - 6, width - 20, height - 46), new Color(8, 10, 20) * 0.45f);
		}
		CountryFlags.Draw(sb, country.Code, new Rectangle(r.X + 16, r.Y + 14, 36, 24));
		HubKit.Text(sb, CountryNames.Name(country.Code), r.X + 64, r.Y + 8, Color.White, 0.84f, r.Width - 250);
		string sub = country.Servers + (country.Servers == 1 ? " server" : " servers") + " · best " + country.BestPing + " ms";
		HubKit.Text(sb, sub, r.X + 64, r.Y + 29, Theme.Muted, 0.66f, r.Width - 250);
		if (selected)
		{
			HubKit.Chip(sb, "Selected", r.Right - 196, r.Y + 16, Green * 0.35f, Color.White, 0.62f, alignRight: true);
		}
		bool favorite = Proxy.FavoriteCountries.Contains(country.Code);
		if (HubKit.BareIcon(sb, id + ":star", star, HubIcon.Texture(favorite ? "Images/UI/ButtonFavoriteActive" : "Images/UI/ButtonFavoriteInactive"), favorite ? "Remove from favorites" : "Add to favorites", Color.White))
		{
			Proxy.ToggleFavorite(country.Code);
			HubInput.Sound(12);
		}
		Signal(sb, new Rectangle(r.Right - 78, r.Y + 15, 22, 22), country.BestPing);
		if (HubKit.BareIcon(sb, id + ":open", chevron, HubIcon.Texture(open ? "Images/UI/ButtonDelete" : "Images/UI/ButtonPlay"), open ? "Hide the servers" : "Show the servers", Color.White, 1f))
		{
			_expanded = open ? null : (list + country.Code);
			HubInput.Sound(12);
		}
		if (open)
		{
			int sy = r.Bottom + 6;
			foreach (ProxyServer server in servers)
			{
				ServerRow(sb, server, new Rectangle(x + 20, sy, width - 40, 30), id);
				sy += 34;
			}
		}
		return height;
	}

	private static void ServerRow(SpriteBatch sb, ProxyServer server, Rectangle r, string parent)
	{
		string id = parent + ":" + server.Key;
		bool selected = Proxy.Enabled && Proxy.Mode == ProxyMode.Server && Proxy.ServerKey == server.Key;
		bool hover = HubInput.In(r);
		HubKit.Fill(sb, r, (selected ? Color.Lerp(Theme.Slot, Green, 0.3f) : Color.Lerp(Theme.Card, Theme.CardHover, HubInput.Anim(id, hover))) * 0.9f);
		HubKit.Chip(sb, server.ProtocolName, r.X + 6, r.Y + 5, ProtocolColor(server.Protocol) * 0.4f, Color.White, 0.56f);
		HubKit.Text(sb, server.Address, r.X + 72, r.Y + 7, Theme.Text, 0.68f);
		string place = string.IsNullOrEmpty(server.City) ? "" : server.City;
		HubKit.Text(sb, place, r.X + 250, r.Y + 7, Theme.Muted, 0.64f, Math.Max(10, r.Width - 250 - 300));
		HubKit.Text(sb, server.Ping + " ms", r.Right - 288, r.Y + 7, PingColor(server.Ping), 0.66f);
		HubKit.Text(sb, (int)Math.Round(server.Reliability * 100) + "% ok", r.Right - 228, r.Y + 7, Theme.Muted, 0.62f);
		bool mine = Proxy.IsMine(server);
		if (HubKit.Button(sb, id + ":keep", new Rectangle(r.Right - 160, r.Y + 3, 70, 24), mine ? "Kept" : "Keep", Theme.Slot, "Keep it in My Proxies", !mine))
		{
			Proxy.KeepServer(server);
			HubInput.Sound(12);
		}
		if (HubKit.Button(sb, id + ":use", new Rectangle(r.Right - 84, r.Y + 3, 80, 24), selected ? "In use" : "Use", selected ? Theme.Slot : Green * 0.5f, "Always connect through this server (others of the country if it fails)", !selected))
		{
			Proxy.UseServer(server);
			HubInput.Sound(12);
		}
	}

	#endregion

	#region My proxies

	public static int DrawMine(SpriteBatch sb, int x, int y, int width)
	{
		Proxy.EnsureLoaded();
		int start = y;
		HubIcon.Item(I.GoldenKey).Draw(sb, new Rectangle(x + 2, y + 2, 24, 24), Color.White);
		HubKit.Text(sb, "Your own proxies", x + 34, y + 4, Theme.Accent, 0.9f);
		HubKit.Text(sb, "A proxy you trust or pay for, or ones you kept from the list. SOCKS5, SOCKS4 and HTTP, with a login if needed.", x + 34, y + 27, Theme.Muted, 0.68f, width - 40);
		y += 56;

		Rectangle field = new Rectangle(x, y, Math.Max(200, width - 128), 34);
		_newProxy = HubKit.Field(sb, "px:new", field, _newProxy, "1.2.3.4:1080  or  user:pass@1.2.3.4:1080", 200, HubIcon.Item(I.GoldenKey));
		bool enter = HubInput.Focus == "px:new" && Main.inputTextEnter;
		if ((HubKit.Button(sb, "px:add", new Rectangle(field.Right + 8, y, 120, 34), "Add", Green * 0.55f, null, _newProxy.Trim().Length > 0, HubIcon.Item(I.GoldenKey)) || enter) && _newProxy.Trim().Length > 0)
		{
			_addError = Proxy.AddMine(_newProxy, _newProtocol);
			if (_addError == null)
			{
				_newProxy = "";
				HubInput.Sound(10);
			}
		}
		y += 42;
		HubKit.Text(sb, "Type", x + 2, y + 6, Theme.Muted, 0.68f);
		int cx = x + 50;
		foreach (ProxyProtocol protocol in new[] { ProxyProtocol.Socks5, ProxyProtocol.Socks4, ProxyProtocol.Http })
		{
			string label = new ProxyServer { Protocol = protocol }.ProtocolName;
			int w = (int)HubKit.Measure(label, 0.68f) + 22;
			if (Chip(sb, "px:proto:" + protocol, new Rectangle(cx, y, w, 28), label, _newProtocol == protocol, ProtocolColor(protocol)))
			{
				_newProtocol = protocol;
			}
			cx += w + 6;
		}
		HubKit.Text(sb, "(or write socks4:// or http:// before it)", cx + 6, y + 7, Theme.Muted * 0.8f, 0.6f, x + width - cx - 10);
		y += 36;
		if (_addError != null)
		{
			HubKit.Text(sb, _addError, x + 2, y, Theme.Danger, 0.66f, width);
			y += 22;
		}
		y += 6;

		List<ProxyServer> mine = Proxy.Mine;
		if (mine.Count == 0)
		{
			y += Empty(sb, x, y, width, "No proxies of your own yet.", "Add one above, or press Keep on a server in Locations.");
		}
		foreach (ProxyServer server in mine)
		{
			y += MineRow(sb, server, x, y, width) + 6;
		}
		return y - start;
	}

	private static int MineRow(SpriteBatch sb, ProxyServer server, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 52);
		string id = "px:m:" + server.Key;
		bool selected = Proxy.Enabled && Proxy.Mode == ProxyMode.Custom && Proxy.CustomKey == server.Key;
		bool hover = HubInput.In(r);
		RowBack(sb, r, id, false, selected);
		CountryFlags.Draw(sb, server.Country.Length > 0 ? server.Country : "xx", new Rectangle(r.X + 16, r.Y + 14, 36, 24));
		HubKit.Text(sb, server.Address + (string.IsNullOrEmpty(server.Username) ? "" : "  (login)"), r.X + 64, r.Y + 8, Color.White, 0.8f, r.Width - 400);
		string state;
		Color stateColor;
		if (!server.Tested)
		{
			state = "Not tested yet";
			stateColor = Theme.Muted;
		}
		else if (server.Strikes == 0 && server.Ping >= 0)
		{
			state = server.ProtocolName + " · " + server.Ping + " ms" + (server.CountryName.Length > 0 ? " · " + server.CountryName : "");
			stateColor = PingColor(server.Ping);
		}
		else
		{
			state = server.ProtocolName + " · not working right now";
			stateColor = Theme.Danger;
		}
		HubKit.Text(sb, state, r.X + 64, r.Y + 29, stateColor, 0.64f, r.Width - 400);
		int bx = r.Right - 12;
		bx -= 34;
		if (HubKit.IconButton(sb, id + ":del", new Rectangle(bx, r.Y + 11, 30, 30), HubIcon.Item(I.TrashCan), "Remove it"))
		{
			Proxy.RemoveMine(server);
			HubInput.Sound(11);
		}
		bx -= 84;
		if (HubKit.Button(sb, id + ":test", new Rectangle(bx, r.Y + 11, 78, 30), "Test", Theme.Slot, "Tests a real connection through it"))
		{
			ProxyPool.TestNow(server);
			HubInput.Sound(12);
		}
		bx -= 92;
		if (HubKit.Button(sb, id + ":use", new Rectangle(bx, r.Y + 11, 86, 30), selected ? "In use" : "Use", selected ? Theme.Slot : Green * 0.55f, "Connect through this proxy", !selected))
		{
			Proxy.UseCustom(server);
			HubInput.Sound(12);
		}
		return r.Height;
	}

	#endregion

	#region Settings

	public static int DrawSettings(SpriteBatch sb, int x, int y, int width)
	{
		Proxy.EnsureLoaded();
		int start = y;
		y += Heading(sb, "Connection", x, y);
		y += Toggle(sb, "px:t:direct", "If no proxy works, connect directly", "Off (privacy first): the connection fails instead of showing your IP.", Proxy.FallbackDirect, v =>
		{
			Proxy.FallbackDirect = v;
			Proxy.Save();
		}, x, y, width) + 6;
		y += Toggle(sb, "px:t:search", "Find and test proxies in the background", "Keeps the list fresh from the moment the game starts. While you play online it tests slowly.", !ProxyPool.Paused, v => ProxyPool.Paused = !v, x, y, width) + 14;

		y += Heading(sb, "Search", x, y);
		Rectangle stats = new Rectangle(x, y, width, 74);
		HubKit.Box(sb, stats, new Color(10, 12, 24) * 0.5f);
		(string, string)[] numbers =
		{
			(ProxyPool.KnownCount.ToString("N0"), "found"),
			(ProxyPool.TestedCount.ToString("N0"), "tested"),
			(ProxyPool.WorkingCount.ToString("N0"), "working"),
			(ProxyPool.Countries().Count.ToString(), "countries")
		};
		int cell = (width - 20) / numbers.Length;
		for (int i = 0; i < numbers.Length; i++)
		{
			Rectangle c = new Rectangle(x + 10 + i * cell, y + 10, cell, 54);
			HubKit.TextCentered(sb, numbers[i].Item1, new Rectangle(c.X, c.Y, c.Width, 28), i == 2 ? Green : Color.White, 1f);
			HubKit.TextCentered(sb, numbers[i].Item2, new Rectangle(c.X, c.Y + 30, c.Width, 18), Theme.Muted, 0.64f);
		}
		y += stats.Height + 10;
		int bw = Math.Min(200, (width - 16) / 3);
		if (HubKit.Button(sb, "px:refresh", new Rectangle(x, y, bw, 32), "Reload the lists", Theme.On * 0.55f, "Downloads every list again and tests what is new", ProxyPool.Activity == null || !ProxyPool.Activity.StartsWith("Downloading"), HubIcon.Item(I.Wrench)))
		{
			ProxyPool.Refresh();
			HubInput.Sound(12);
		}
		if (HubKit.Button(sb, "px:retest", new Rectangle(x + bw + 8, y, bw, 32), "Test the good ones", Theme.Slot, "Tests the working proxies again now", true, HubIcon.Item(I.Stopwatch)))
		{
			ProxyPool.RetestWorking();
			HubInput.Sound(12);
		}
		if (HubKit.Button(sb, "px:reset", new Rectangle(x + (bw + 8) * 2, y, bw, 32), "Start over", Theme.Slot, "Forgets every result and searches from zero", true, HubIcon.Item(I.TrashCan)))
		{
			ProxyPool.Reset();
			HubInput.Sound(11);
		}
		y += 40;
		HubKit.Text(sb, "Lists updated " + Ago(ProxyPool.ListsUpdated) + ". Each proxy is tested with a real connection that carries Terraria's first packet, twice, and again every few minutes.", x + 2, y, Theme.Muted, 0.62f, width);
		y += 28;

		y += Heading(sb, "Proxy lists", x, y);
		Dictionary<string, int> workingBySource = ProxyPool.Best().GroupBy(s => s.Source).ToDictionary(g => g.Key, g => g.Count());
		foreach (ProxyPool.Source source in ProxyPool.Sources)
		{
			Rectangle r = new Rectangle(x, y, width, 40);
			string id = "px:src:" + source.Name;
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover(id, source.Url))
			{
				source.Enabled = !source.Enabled;
				Proxy.Save();
				HubInput.Sound(12);
			}
			HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.CardHover, HubInput.Anim(id, hover)) * HubPrefs.Opacity);
			HubKit.Chip(sb, source.Mixed ? "MIXED" : new ProxyServer { Protocol = source.Protocol }.ProtocolName, r.X + 10, r.Y + 10, ProtocolColor(source.Protocol) * 0.4f, Color.White, 0.56f);
			HubKit.Text(sb, source.Name, r.X + 80, r.Y + 11, source.Enabled ? Theme.Text : Theme.Muted, 0.74f, r.Width - 400);
			workingBySource.TryGetValue(source.Name, out int good);
			string info = (source.Error != null) ? "could not download" : (source.Downloaded > 0 ? (source.Downloaded.ToString("N0") + " listed · " + good + " working") : "");
			HubKit.Text(sb, info, r.Right - 300, r.Y + 12, source.Error != null ? Theme.Danger : Theme.Muted, 0.64f, 230);
			HubKit.Switch(sb, id + ":sw", new Rectangle(r.Right - 56, r.Y + 9, 44, 22), source.Enabled, hover);
			y += 44;
		}
		y += 10;

		y += Heading(sb, "Never use addresses starting with", x, y);
		int cx = x;
		foreach (string prefix in ProxyPool.Blocked.ToList())
		{
			int w = (int)HubKit.Measure(prefix + "*", 0.7f) + 40;
			Rectangle chip = new Rectangle(cx, y, w, 28);
			HubKit.Box(sb, chip, Theme.Danger * 0.35f);
			HubKit.Text(sb, prefix + "*", chip.X + 10, chip.Y + 6, Color.White, 0.7f);
			if (HubKit.BareIcon(sb, "px:unblock:" + prefix, new Rectangle(chip.Right - 22, chip.Y + 7, 14, 14), HubIcon.Texture("Images/UI/SearchCancel"), "Allow it again", Color.White))
			{
				ProxyPool.Blocked.Remove(prefix);
				Proxy.Save();
			}
			cx += w + 6;
		}
		Rectangle blockField = new Rectangle(cx, y - 2, 170, 32);
		_newBlock = HubKit.Field(sb, "px:block", blockField, _newBlock, "e.g. 103.21.", 20);
		if (HubInput.Focus == "px:block" && Main.inputTextEnter && _newBlock.Trim().Length > 2)
		{
			ProxyPool.Blocked.Add(_newBlock.Trim());
			_newBlock = "";
			Proxy.Save();
		}
		y += 40;
		HubKit.Text(sb, "45.74.* proxies answer the tests now and then but drop the game, so they are blocked by default. Press Enter to add one.", x + 2, y, Theme.Muted, 0.62f, width);
		y += 26;
		return y - start;
	}

	#endregion

	#region Pieces

	private static void RowBack(SpriteBatch sb, Rectangle r, string id, bool hover, bool selected)
	{
		float h = HubInput.Anim(id, hover);
		float s = HubInput.Anim(id + ":sel", selected);
		Color back = Color.Lerp(Color.Lerp(Theme.Card, Theme.CardHover, h), Color.Lerp(Theme.Card, Green, 0.22f), s);
		HubKit.Box(sb, r, back * HubPrefs.Opacity);
		if (s > 0.01f)
		{
			HubKit.Fill(sb, new Rectangle(r.X + 5, r.Y + 10, 3, r.Height - 20), Green * s);
		}
	}

	/// <summary>Four bars like a phone's signal: green when fast, red when slow.</summary>
	internal static void Signal(SpriteBatch sb, Rectangle r, int ping)
	{
		int bars = (ping < 0) ? 0 : (ping < 150 ? 4 : (ping < 300 ? 3 : (ping < 600 ? 2 : 1)));
		Color color = PingColor(ping);
		int w = (r.Width - 6) / 4;
		for (int i = 0; i < 4; i++)
		{
			int h = r.Height * (i + 1) / 4;
			HubKit.Fill(sb, new Rectangle(r.X + i * (w + 2), r.Bottom - h, w, h), (i < bars) ? color : (Theme.Muted * 0.25f));
		}
		if (HubInput.In(r))
		{
			HubInput.Hover("px:sig", (ping < 0) ? "Not tested" : (ping + " ms round trip"));
		}
	}

	internal static Color PingColor(int ping)
	{
		if (ping < 0)
		{
			return Theme.Muted;
		}
		return (ping < 150) ? Green : (ping < 300 ? new Color(190, 225, 90) : (ping < 600 ? new Color(255, 190, 80) : new Color(240, 100, 90)));
	}

	internal static Color ProtocolColor(ProxyProtocol protocol)
	{
		return protocol switch
		{
			ProxyProtocol.Socks4 => new Color(120, 170, 255),
			ProxyProtocol.Http => new Color(255, 170, 90),
			_ => new Color(80, 220, 160)
		};
	}

	private static int Heading(SpriteBatch sb, string text, int x, int y)
	{
		HubKit.Text(sb, text, x + 2, y, Theme.Text, 0.8f);
		return 24;
	}

	private static int Empty(SpriteBatch sb, int x, int y, int width, string title, string text)
	{
		Rectangle r = new Rectangle(x, y, width, 62);
		HubKit.Box(sb, r, new Color(10, 12, 24) * 0.45f);
		HubIcon.Item(I.WorldGlobe).Draw(sb, new Rectangle(r.X + 16, r.Y + 15, 32, 32), Color.White * 0.85f);
		HubKit.Text(sb, title, r.X + 60, r.Y + 12, Theme.Text, 0.78f);
		HubKit.Text(sb, text, r.X + 60, r.Y + 34, Theme.Muted, 0.64f, r.Width - 70);
		return r.Height + 8;
	}

	private static bool Chip(SpriteBatch sb, string id, Rectangle r, string label, bool active, Color color)
	{
		bool hover = HubInput.In(r);
		bool clicked = hover && HubInput.Hover(id) && !active;
		if (clicked)
		{
			HubInput.Sound(12);
		}
		float glow = HubInput.Anim(id, hover || active);
		Color back = active ? Color.Lerp(Theme.Slot, color, 0.45f) : Color.Lerp(Theme.Card, Theme.CardHover, glow);
		HubKit.Box(sb, r, back * HubPrefs.Opacity);
		HubKit.Text(sb, label, r.X + 10, r.Y + 6, active ? Color.White : Theme.Text, 0.68f);
		return clicked;
	}

	private static int Toggle(SpriteBatch sb, string id, string title, string tip, bool on, Action<bool> set, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 52);
		bool hover = HubInput.In(r);
		if (hover && HubInput.Hover(id))
		{
			set(!on);
			on = !on;
			HubInput.Sound(12);
		}
		HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.CardHover, HubInput.Anim(id, hover)) * HubPrefs.Opacity);
		HubKit.Text(sb, title, r.X + 14, r.Y + 8, on ? Theme.Text : Theme.Muted, 0.78f, r.Width - 90);
		HubKit.Text(sb, tip, r.X + 14, r.Y + 29, Theme.Muted, 0.62f, r.Width - 90);
		HubKit.Switch(sb, id + ":sw", new Rectangle(r.Right - 58, r.Y + 15, 44, 22), on, hover);
		return r.Height;
	}

	private static string Ago(DateTime time)
	{
		if (time == DateTime.MinValue)
		{
			return "never";
		}
		TimeSpan span = DateTime.Now - time;
		return (span.TotalMinutes < 1) ? "just now" : ((span.TotalHours < 1) ? ((int)span.TotalMinutes + " min ago") : ((int)span.TotalHours + " h ago"));
	}

	#endregion
}
