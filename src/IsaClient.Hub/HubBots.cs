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
/// The Bots section: extra players (headless sessions) you can play as, on your server or any other.
/// Sessions: you and a card per bot (its character walking, life and mana, server, what it does) with its actions, and
/// the picked bot's details (say something, follow you, come to you...). New Bot: pick a character (whole portraits),
/// a server and how many. Saved: who was online last time and saved bots. Options: identity, behaviour, staying online.
/// The main menu shows the same pages (BotsMenu). Same actions as the classic Bots tab (MultiClientPanel).
/// </summary>
internal static class HubBots
{
	private static readonly Color Online = new Color(110, 220, 120);

	private static readonly Color Connecting = new Color(240, 200, 90);

	private static readonly Color Offline = new Color(230, 100, 100);

	private static readonly Color Possessed = new Color(110, 230, 210);

	private static readonly Color Green = new Color(56, 128, 84);

	private static readonly Color Red = new Color(150, 58, 58);

	private static HubTheme Theme => HubTheme.Current;

	/// <summary>Set by whoever hosts the pages (panel or menu window) to switch to a tab: 0 Sessions, 1 New Bot, 2 Saved, 3 Options.</summary>
	internal static Action<int> ShowTab = PanelTab;

	/// <summary>Set by the host like <see cref="ShowTab"/>: scrolls the page to an offset from its top.</summary>
	internal static Action<float> ScrollTo = ClientHub.ScrollPageTo;

	/// <summary>New Bot: a character or a server was just picked, the page goes on to the next step.</summary>
	private static int _scrollToStep;

	/// <summary>In the panel: the Bots section's page.</summary>
	internal static void PanelTab(int tab)
	{
		ClientHub.OpenSection("bots", tab);
	}

	// Sessions
	private static int _selected = -1;

	private static string _say = "";

	// New bot
	private static int _character = -1;

	private static int _server;

	private static string _customHost = "";

	private static string _customPort = "7777";

	private static string _password = "";

	private static string _name = "";

	private static int _count = 1;

	// Options
	private static string _customUuid = "";

	/// <summary>Copies of the characters the cards draw (drawing changes a player's frame, never the real ones).</summary>
	private static readonly Dictionary<string, Player> _looks = new Dictionary<string, Player>();

	/// <summary>"2 online" for a card.</summary>
	public static string Summary()
	{
		return MultiClientManager.OnlineCount + " online of " + MultiClientManager.Count;
	}

	#region Looks

	private static Player LookOf(ClientSession s)
	{
		string key = "s:" + s.Id + ":" + s.Name + ":" + s.TemplatePath;
		if (!_looks.TryGetValue(key, out var player))
		{
			try
			{
				player = s.CreateLocalPlayer();
			}
			catch
			{
				player = null;
			}
			_looks[key] = player;
		}
		return player;
	}

	private static Player LookOf(CharacterEntry c)
	{
		if (c?.Player == null)
		{
			return null;
		}
		string key = "c:" + c.Path;
		if (!_looks.TryGetValue(key, out var player))
		{
			try
			{
				player = c.Player.SerializedClone();
			}
			catch
			{
				player = null;
			}
			_looks[key] = player;
		}
		return player;
	}

	private static CharacterEntry CharacterFor(string playerFile)
	{
		if (string.IsNullOrEmpty(playerFile))
		{
			return null;
		}
		return MultiClientManager.Characters.FirstOrDefault(c => string.Equals(c.Path, playerFile, StringComparison.OrdinalIgnoreCase));
	}

	private static Color StateColor(ClientSession s)
	{
		if (s.Possessed)
		{
			return Possessed;
		}
		return s.State switch
		{
			ClientSessionState.InGame => Online,
			ClientSessionState.Disconnected => Offline,
			_ => Connecting
		};
	}

	private static string StateName(ClientSession s)
	{
		if (s.Possessed)
		{
			return "You are it";
		}
		return s.State switch
		{
			ClientSessionState.InGame => "Online",
			ClientSessionState.Disconnected => "Offline",
			ClientSessionState.Idle => "Idle",
			_ => "Connecting"
		};
	}

	#endregion

	#region Sessions

	public static int DrawSessions(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y += YouCard(sb, x, y, width) + 12;
		List<ClientSession> sessions = MultiClientManager.All;

		// Title line: counts and the main actions.
		int servers = sessions.Select(s => s.Host + ":" + s.Port).Distinct().Count();
		HubKit.Text(sb, sessions.Count + " / " + MultiClientManager.MaxSessions + " bots", x + 2, y + 8, Theme.Accent, 0.9f);
		int chipX = x + 2 + (int)HubKit.Measure(sessions.Count + " / " + MultiClientManager.MaxSessions + " bots", 0.9f) + 10;
		chipX = HubKit.Chip(sb, MultiClientManager.OnlineCount + " online", chipX, y + 8, new Color(40, 90, 60), Color.White, 0.62f).Right + 4;
		if (servers > 1)
		{
			HubKit.Chip(sb, servers + " servers", chipX, y + 8, new Color(60, 70, 120), Color.White, 0.62f);
		}
		bool canHere = MultiClientManager.TryGetCurrentServer(out var hereHost, out var herePort);
		int bx = x + width;
		bx -= 112;
		if (HubKit.Button(sb, "bots:remall", new Rectangle(bx, y, 112, 34), "Remove all", Red, "Disconnect and remove every bot", sessions.Count > 0, HubIcon.Item(I.TrashCan)))
		{
			MultiClientManager.RemoveAll();
			_selected = -1;
		}
		bx -= 128;
		bool anyOff = sessions.Any(s => s.State == ClientSessionState.Disconnected);
		if (HubKit.Button(sb, "bots:recall", new Rectangle(bx, y, 122, 34), "Reconnect all", Theme.Card, "Connect again every bot that dropped", anyOff, HubIcon.Item(I.RecallPotion)))
		{
			foreach (ClientSession s in sessions.Where(s => s.State == ClientSessionState.Disconnected))
			{
				MultiClientManager.Reconnect(s);
			}
		}
		bx -= 116;
		if (HubKit.Button(sb, "bots:new", new Rectangle(bx, y, 110, 34), "New bot", Theme.Slot, "Pick a character, a server and how many", true, HubIcon.Item(I.Mannequin)))
		{
			MultiClientManager.RefreshCharacters();
			ShowTab(1);
		}
		bx -= 116;
		if (HubKit.Button(sb, "bots:addhere", new Rectangle(bx, y, 110, 34), "Add 1 here", Green, canHere ? ("A bot joins " + hereHost + ":" + herePort) : "Join a server first (or use New bot)", canHere, HubIcon.Item(I.RobotHat)))
		{
			MultiClientManager.AddSessions(1, hereHost, herePort);
		}
		bx -= 136;
		if (HubKit.Button(sb, "bots:saveset", new Rectangle(bx, y, 130, 34), "Save as set", Theme.Slot, "Every bot you have becomes a member of a new set (name, look, UUID, server): start them all again in one click from Sets", sessions.Count > 0, HubIcon.Item(I.GoldCrown)))
		{
			HubBotSets.SaveAllAsSet();
		}
		y += 44;

		if (sessions.Count == 0)
		{
			y += ClientHub.EmptyNote(sb, x, y, width, HubIcon.Item(I.RobotHat), "No bots yet", "\"Add 1 here\" joins the server you are on; \"New bot\" picks a character and any server.");
		}
		else
		{
			int columns = (width >= 660) ? 2 : 1;
			const int gap = 10;
			int cardW = (width - gap * (columns - 1)) / columns;
			const int cardH = 132;
			for (int i = 0; i < sessions.Count; i++)
			{
				Rectangle r = new Rectangle(x + i % columns * (cardW + gap), y + i / columns * (cardH + gap), cardW, cardH);
				if (HubInput.Visible(r))
				{
					BotCard(sb, sessions[i], r);
				}
			}
			y += (sessions.Count + columns - 1) / columns * (cardH + gap);
			y += HubBotSets.BatchBar(sb, x, y, width) + 10;
			ClientSession picked = sessions.FirstOrDefault(s => s.Id == _selected);
			if (picked != null)
			{
				y += Details(sb, picked, x, y, width) + 10;
			}
		}
		y += Notice(sb, x, y, width);
		HubKit.Text(sb, "Chat: .bot control <id>  ·  .bot return  ·  .bot chat <id|off>  ·  .bot run <all|set|id> <what>  ·  .bot set start <name>  ·  .bot as <character>", x + 2, y + 4, Theme.Muted * 0.8f, 0.64f, width - 4);
		y += 26;
		return y - start;
	}

	/// <summary>You: your character, where you are, and the way back while you play as a bot.</summary>
	private static int YouCard(SpriteBatch sb, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 84);
		Color gold = new Color(255, 214, 90);
		HubKit.Box(sb, r, Color.Lerp(Theme.Card, new Color(120, 96, 30), 0.35f) * HubPrefs.Opacity);
		HubKit.Fill(sb, new Rectangle(r.X + 3, r.Y + 10, 3, r.Height - 20), gold);
		Rectangle head = new Rectangle(r.X + 14, r.Y + 12, 60, 60);
		HubKit.Box(sb, head, Theme.Sidebar * (HubPrefs.Opacity * 0.8f));
		if (Main.LocalPlayer != null)
		{
			HubKit.Head(sb, Main.LocalPlayer, new Vector2(head.Center.X, head.Center.Y + 2), 1.6f);
		}
		int tx = head.Right + 14;
		int right = r.Right - 12;
		if (Possession.Active)
		{
			Rectangle back = new Rectangle(right - 150, r.Y + 24, 150, 36);
			if (HubKit.Button(sb, "bots:youback", back, "Go back to you", Green, "Stop playing as " + Possession.Current.Name + " and be yourself again (.bot return)", true, HubIcon.Item(I.MagicMirror)))
			{
				MultiClientManager.Control(null);
			}
			right = back.X - 10;
		}
		string name = Possession.Active ? Possession.OriginalName : Main.LocalPlayer?.name;
		if (string.IsNullOrEmpty(name))
		{
			name = "You";
		}
		HubKit.Text(sb, name, tx, r.Y + 12, gold, 0.95f, right - tx - 50);
		HubKit.Chip(sb, "You", tx + (int)Math.Min(HubKit.Measure(name, 0.95f), right - tx - 50) + 8, r.Y + 13, Theme.Slot, Color.White, 0.62f);
		string where;
		if (Possession.Active)
		{
			where = "Kept online at " + Possession.MainSession.Host + ":" + Possession.MainSession.Port + " while you play as " + Possession.Current.Name + ".";
		}
		else if (MultiClientManager.TryGetCurrentServer(out var h, out var p))
		{
			where = "You are on " + h + ":" + p + ". Bots can join you here or play on other servers at the same time.";
		}
		else
		{
			where = "Not on a server. Bots still connect to any server from here.";
		}
		HubIcon.Item(I.WorldGlobe).Draw(sb, new Rectangle(tx, r.Y + 42, 16, 16), Color.White);
		HubKit.Text(sb, where, tx + 22, r.Y + 42, Theme.Text * 0.85f, 0.7f, right - tx - 22);
		HubKit.Text(sb, Possession.Active ? "Your character is safe and saved as it was." : "Play as a bot to control it; your own character stays online.", tx, r.Y + 62, Theme.Muted, 0.64f, right - tx);
		return r.Height;
	}

	private static void BotCard(SpriteBatch sb, ClientSession s, Rectangle r)
	{
		string id = "bot:" + s.Id;
		bool picked = _selected == s.Id;
		bool hover = HubInput.In(r);
		float glow = HubInput.Anim(id, hover || picked);
		Color state = StateColor(s);
		bool online = s.State == ClientSessionState.InGame;
		HubKit.Box(sb, r, Color.Lerp(picked ? Color.Lerp(Theme.Card, Theme.Slot, 0.45f) : Theme.Card, Theme.CardHover, glow * 0.6f) * HubPrefs.Opacity);
		HubKit.Fill(sb, new Rectangle(r.X + 3, r.Y + 10, 3, r.Height - 20), state * (online || s.Possessed ? 1f : 0.6f));

		// The bot's character, walking while it is online.
		Rectangle portrait = new Rectangle(r.X + 12, r.Y + 10, 74, 86);
		HubKit.Box(sb, portrait, Theme.Sidebar * (HubPrefs.Opacity * 0.7f));
		Player look = LookOf(s);
		if (look != null)
		{
			HubKit.Portrait(sb, look, new Rectangle(portrait.X, portrait.Y + 4, portrait.Width, portrait.Height), 1f, walking: online);
		}
		HubKit.Chip(sb, "#" + s.Id, portrait.X + 2, portrait.Bottom + 6, new Color(20, 24, 44), state, 0.62f);
		int tx = portrait.Right + 12;
		int textRoom = r.Right - tx - 10;

		// Name and chips.
		float nameRoom = textRoom - 150;
		HubKit.Text(sb, s.Name, tx, r.Y + 10, Color.White, 0.9f, nameRoom);
		int cx = tx + (int)Math.Min(HubKit.Measure(s.Name, 0.9f), nameRoom) + 8;
		cx = HubKit.Chip(sb, StateName(s), cx, r.Y + 11, state * 0.45f, Color.White, 0.6f).Right + 4;
		if (s.AiEnabled)
		{
			cx = HubKit.Chip(sb, "AI", cx, r.Y + 11, new Color(150, 120, 50), Color.White, 0.6f).Right + 4;
		}
		if (MultiClientManager.ChatTarget == s)
		{
			cx = HubKit.Chip(sb, "Chat", cx, r.Y + 11, new Color(70, 90, 160), Color.White, 0.6f).Right + 4;
		}
		BotSet set = BotSets.SetOf(s);
		if (set != null && cx < r.Right - 120)
		{
			Rectangle setChip = HubKit.Chip(sb, "    " + set.Name, cx, r.Y + 11, new Color(90, 70, 30), Color.White, 0.6f);
			HubIcon.Item(set.Icon).Draw(sb, new Rectangle(setChip.X + 4, setChip.Y + 2, setChip.Height - 4, setChip.Height - 4), Color.White);
			cx = setChip.Right + 4;
		}
		if (s.Pasting && cx < r.Right - 60)
		{
			cx = HubKit.Chip(sb, "Pasting", cx, r.Y + 11, new Color(50, 110, 70), Color.White, 0.6f).Right + 4;
		}
		if (s.IsPixie && cx < r.Right - 60)
		{
			cx = HubKit.Chip(sb, "Pixie", cx, r.Y + 11, new Color(170, 80, 160), Color.White, 0.6f).Right + 4;
		}
		if (s.KillAuraSize > 0 && cx < r.Right - 60)
		{
			cx = HubKit.Chip(sb, "Aura " + s.KillAuraSize, cx, r.Y + 11, new Color(160, 50, 50), Color.White, 0.6f).Right + 4;
		}
		if (s.SpamText != null && cx < r.Right - 60)
		{
			cx = HubKit.Chip(sb, "Spam", cx, r.Y + 11, new Color(150, 110, 40), Color.White, 0.6f).Right + 4;
		}
		if ((!s.God || s.Dead) && cx < r.Right - 60)
		{
			cx = HubKit.Chip(sb, s.Dead ? "Dead" : "Mortal", cx, r.Y + 11, new Color(110, 40, 40), Color.White, 0.6f).Right + 4;
		}
		string progress = s.Script?.Progress;
		if (progress != null && cx < r.Right - 60)
		{
			HubKit.Chip(sb, "Step " + progress, cx, r.Y + 11, new Color(60, 110, 150), Color.White, 0.6f);
		}

		// Where: this server or another one.
		bool here = Possession.IsOnMyServer(s);
		HubIcon.Item(here ? I.WorldGlobe : I.Teleporter).Draw(sb, new Rectangle(tx, r.Y + 34, 16, 16), Color.White);
		string where = s.Host + ":" + s.Port + (here ? "  ·  your server" : "  ·  another server");
		int routeW = RouteChip(sb, s, r.Right - 10, r.Y + 32);
		HubKit.Text(sb, where, tx + 20, r.Y + 34, here ? Theme.Text * 0.9f : new Color(150, 200, 255), 0.66f, textRoom - 24 - routeW);

		// Life and mana.
		int barW = Math.Max(60, (textRoom - 8) / 2);
		Bar(sb, new Rectangle(tx, r.Y + 56, barW, 14), s.StatLife, s.StatLifeMax, new Color(220, 60, 70), "Life");
		Bar(sb, new Rectangle(tx + barW + 8, r.Y + 56, barW, 14), s.StatMana, s.StatManaMax, new Color(70, 120, 230), "Mana");

		// What it is doing.
		HubKit.Text(sb, MultiClientManager.Describe(s), tx, r.Y + 76, Theme.Muted, 0.64f, textRoom);

		// Actions.
		Rectangle buttonsArea = new Rectangle(tx, r.Bottom - 34, textRoom, 28);
		bool overButton = Actions(sb, s, buttonsArea, compact: true);
		if (hover && !overButton)
		{
			HubInput.Hover("card:" + id, picked ? "Click: hide the details" : "Click: details and more actions");
			if (HubInput.Clicked)
			{
				_selected = picked ? -1 : s.Id;
				_say = "";
				HubInput.Sound(12);
			}
		}
	}

	/// <summary>Where the bot connects from (its proxy's flag and country, or your IP), right-aligned; returns its width.</summary>
	private static int RouteChip(SpriteBatch sb, ClientSession s, int right, int y)
	{
		if (s.RouteKey == null || !IsaClient.MultiClient.BotRoutes.Enabled)
		{
			return 0;
		}
		IsaClient.Networking.ProxyServer proxy = s.Route;
		string label = (proxy == null) ? "Your IP" : (string.IsNullOrEmpty(proxy.Country) ? "Proxy" : IsaClient.Networking.CountryNames.Name(proxy.Country));
		float w = HubKit.Measure(label, 0.58f);
		int flagW = (proxy != null && !string.IsNullOrEmpty(proxy.Country)) ? 24 : 0;
		Rectangle chip = new Rectangle(right - (int)w - 16 - flagW, y, (int)w + 16 + flagW, 20);
		HubKit.Box(sb, chip, ((proxy == null) ? Theme.Slot : new Color(30, 80, 60)) * 0.9f);
		if (flagW > 0)
		{
			CountryFlags.Draw(sb, proxy.Country, new Rectangle(chip.X + 6, chip.Y + 4, 18, 12));
		}
		HubKit.Text(sb, label, chip.X + 8 + flagW, chip.Y + 3, Color.White, 0.58f);
		if (HubInput.In(chip))
		{
			HubInput.Hover("bot:route:" + s.Id, (proxy == null) ? "Connected from your own IP" : ("Connected through " + proxy.ProtocolName + " " + proxy.Address + ((proxy.Ping >= 0) ? (", " + proxy.Ping + " ms") : "")));
		}
		return chip.Width + 6;
	}

	/// <summary>The icon buttons of a bot (right to left); true when the mouse is on one of them.</summary>
	private static bool Actions(SpriteBatch sb, ClientSession s, Rectangle area, bool compact)
	{
		string id = "bot:" + s.Id;
		const int size = 28;
		int x = area.Right - size;
		int y = area.Y;
		bool over = false;
		bool Icon(string key, HubIcon icon, string tip, bool active = false, bool enabled = true)
		{
			Rectangle b = new Rectangle(x, y, size, size);
			x -= size + 4;
			over |= HubInput.In(b);
			return HubKit.IconButton(sb, id + ":" + key, b, icon, tip, active, enabled);
		}
		if (Icon("remove", HubIcon.Item(I.TrashCan), "Disconnect and remove " + s.Name))
		{
			MultiClientManager.Remove(s);
			if (_selected == s.Id)
			{
				_selected = -1;
			}
			return true;
		}
		if (s.Possessed)
		{
			if (Icon("back", HubIcon.Item(I.MagicMirror), "Go back to you"))
			{
				MultiClientManager.Control(null);
			}
			return over;
		}
		if (s.State == ClientSessionState.Disconnected)
		{
			if (Icon("save", HubIcon.Item(I.Book), "Save it (name, look, server) in Saved"))
			{
				MultiClientManager.SaveProfile(s);
			}
			if (Icon("reconnect", HubIcon.Item(I.RecallPotion), "Reconnect"))
			{
				MultiClientManager.Reconnect(s);
			}
			return over;
		}
		if (s.State != ClientSessionState.InGame)
		{
			return over;
		}
		bool here = Possession.IsOnMyServer(s);
		bool chatting = MultiClientManager.ChatTarget == s;
		if (Icon("save", HubIcon.Item(I.Book), "Save it (name, look, server) in Saved"))
		{
			MultiClientManager.SaveProfile(s);
		}
		if (Icon("ai", HubIcon.Item(I.SpellTome), s.AiEnabled ? "AI on: it answers when someone says its name. Click: off" : "AI: let it answer when someone says its name", s.AiEnabled))
		{
			MultiClientManager.SetAi(s, !s.AiEnabled);
		}
		if (Icon("follow", HubIcon.Item(I.Compass), s.IsFollowing ? ("Following " + (s.FollowingName ?? "someone") + ". Click: stop") : (here ? "Follow you" : "It is on another server: it can only follow players in your world"), s.IsFollowing, here || s.IsFollowing))
		{
			if (s.IsFollowing)
			{
				s.StopMoving();
			}
			else
			{
				MultiClientManager.FollowPlayer(s, Main.myPlayer);
			}
		}
		if (Icon("chat", HubIcon.Item(I.Megaphone), chatting ? "Your chat goes through it. Click: back to your own chat" : "Chat as it: what you type is said by " + s.Name, chatting))
		{
			MultiClientManager.SetChatTarget(chatting ? null : s);
		}
		if (Icon("play", HubIcon.Item(I.GhostMask), "Play as " + s.Name + (here ? "" : " (on its server: your own character stays online)")))
		{
			MultiClientManager.Control(s);
		}
		return over;
	}

	/// <summary>The picked bot: a bigger look, everything about it, say something, and its actions with names.</summary>
	private static int Details(SpriteBatch sb, ClientSession s, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 196);
		Color state = StateColor(s);
		HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.Slot, 0.3f) * HubPrefs.Opacity);
		HubKit.Fill(sb, new Rectangle(r.X + 3, r.Y + 12, 3, r.Height - 24), state);
		Rectangle portrait = new Rectangle(r.X + 16, r.Y + 14, 110, 168);
		HubKit.Box(sb, portrait, Theme.Sidebar * (HubPrefs.Opacity * 0.75f));
		Player look = LookOf(s);
		if (look != null)
		{
			HubKit.Portrait(sb, look, new Rectangle(portrait.X, portrait.Y + 20, portrait.Width, portrait.Height - 40), 1.6f, walking: s.State == ClientSessionState.InGame);
		}
		int tx = portrait.Right + 16;
		int room = r.Right - tx - 14;
		if (HubKit.BareIcon(sb, "bots:closedetails", new Rectangle(r.Right - 26, r.Y + 10, 16, 16), HubIcon.Texture("Images/UI/SearchCancel"), "Hide the details", Color.White))
		{
			_selected = -1;
		}
		HubKit.Title(sb, s.Name, tx, r.Y + 8, room - 30, 0.55f, Color.White);
		string look2 = (s.Template != null) ? System.IO.Path.GetFileName(s.TemplatePath ?? "character") : "generated look";
		string weapon = s.WeaponName;
		List<(int Icon, string Text)> facts = new List<(int, string)>
		{
			(I.WorldGlobe, s.Host + ":" + s.Port + (Possession.IsOnMyServer(s) ? "  (your server)" : "  (another server)")),
			(I.Mannequin, look2 + "  ·  #" + s.Id),
			(I.CopperShortsword, (weapon != null) ? ("Fights with " + weapon + (MultiClientManager.SessionsFight ? "" : " (fighting is off in Options)")) : "No weapon on its hotbar"),
			(I.GoldenKey, "UUID " + (string.IsNullOrEmpty(s.Uuid) ? "-" : s.Uuid))
		};
		int fy = r.Y + 40;
		foreach (var fact in facts)
		{
			HubIcon.Item(fact.Icon).Draw(sb, new Rectangle(tx, fy, 16, 16), Color.White);
			HubKit.Text(sb, fact.Text, tx + 22, fy, Theme.Text * 0.85f, 0.68f, room - 22);
			fy += 20;
		}
		HubKit.Text(sb, StateName(s) + ": " + MultiClientManager.Describe(s), tx, fy + 2, state, 0.68f, room);

		// Say something as it.
		bool online = s.State == ClientSessionState.InGame && !s.Possessed;
		int sayY = r.Bottom - 80;
		Rectangle field = new Rectangle(tx, sayY, room - 96, 32);
		_say = HubKit.Field(sb, "bots:say", field, _say, online ? ("Say something as " + s.Name + "...") : "It has to be online to talk", 200, HubIcon.Item(I.Megaphone));
		bool enter = HubInput.Focus == "bots:say" && Main.inputTextEnter;
		if ((HubKit.Button(sb, "bots:saysend", new Rectangle(field.Right + 6, sayY, 90, 32), "Say", Theme.Slot, "Send it to the chat of its server", online && _say.Trim().Length > 0) || enter) && online && _say.Trim().Length > 0)
		{
			s.Say(_say.Trim());
			_say = "";
		}

		// Actions with names.
		int bx = tx;
		int by = r.Bottom - 40;
		bool here = Possession.IsOnMyServer(s);
		bool Do(string key, string label, HubIcon icon, Color color, string tip, bool enabled = true)
		{
			int w = (int)HubKit.Measure(label, 0.78f) + 40;
			Rectangle b = new Rectangle(bx, by, w, 30);
			bx += w + 6;
			return b.Right <= r.Right - 8 && HubKit.Button(sb, "bots:d:" + key, b, label, color, tip, enabled, icon);
		}
		if (s.Possessed)
		{
			if (Do("back", "Go back to you", HubIcon.Item(I.MagicMirror), Green, "Stop playing as it"))
			{
				MultiClientManager.Control(null);
			}
			return r.Height;
		}
		if (Do("play", "Play as", HubIcon.Item(I.GhostMask), Green, "Control it; your own character stays online", online))
		{
			MultiClientManager.Control(s);
		}
		if (Do("come", "Come to me", HubIcon.Item(I.MagicMirror), Theme.Card, here ? "It walks to where you are" : "Only for bots in your world", online && here && Main.LocalPlayer != null))
		{
			s.GoTo(Main.LocalPlayer.Bottom);
		}
		if (Do("follow", s.IsFollowing ? "Stop" : "Follow me", HubIcon.Item(I.Compass), Theme.Card, s.IsFollowing ? "Stop walking" : "It follows you around", online && (here || s.IsFollowing)))
		{
			if (s.IsFollowing)
			{
				s.StopMoving();
			}
			else
			{
				MultiClientManager.FollowPlayer(s, Main.myPlayer);
			}
		}
		if (s.State == ClientSessionState.Disconnected && Do("reconnect", "Reconnect", HubIcon.Item(I.RecallPotion), Green, "Connect it again"))
		{
			MultiClientManager.Reconnect(s);
		}
		if (Do("save", "Save", HubIcon.Item(I.Book), Theme.Card, "Keep its name, look and server in Saved"))
		{
			MultiClientManager.SaveProfile(s);
		}
		if (Do("remove", "Remove", HubIcon.Item(I.TrashCan), Red, "Disconnect and remove it"))
		{
			MultiClientManager.Remove(s);
			_selected = -1;
		}
		return r.Height;
	}

	private static void Bar(SpriteBatch sb, Rectangle r, int value, int max, Color color, string label)
	{
		HubKit.Fill(sb, r, new Color(15, 18, 32) * 0.9f);
		float fill = (max > 0) ? MathHelper.Clamp((float)value / max, 0f, 1f) : 0f;
		HubKit.Fill(sb, new Rectangle(r.X + 1, r.Y + 1, (int)((r.Width - 2) * fill), r.Height - 2), color * 0.85f);
		HubKit.Fill(sb, new Rectangle(r.X + 1, r.Y + 1, (int)((r.Width - 2) * fill), 3), Color.White * 0.15f);
		HubKit.Text(sb, label + " " + value + " / " + max, r.X + 6, r.Y - 1, Color.White, 0.6f, r.Width - 8);
	}

	private static int Notice(SpriteBatch sb, int x, int y, int width)
	{
		string notice = MultiClientManager.LastNotice;
		if (string.IsNullOrEmpty(notice))
		{
			return 0;
		}
		// The notice is a chat line ("[c/8CE49B:text]"): the text in its color.
		Color color = Theme.Text;
		System.Text.RegularExpressions.Match tag = System.Text.RegularExpressions.Regex.Match(notice, "^\\[c/([0-9A-Fa-f]{6}):(.*)\\]$", System.Text.RegularExpressions.RegexOptions.Singleline);
		if (tag.Success)
		{
			uint rgb = Convert.ToUInt32(tag.Groups[1].Value, 16);
			color = new Color((int)(rgb >> 16) & 0xFF, (int)(rgb >> 8) & 0xFF, (int)rgb & 0xFF);
			notice = tag.Groups[2].Value;
		}
		bool error = color.R > color.G;
		Rectangle r = new Rectangle(x, y, width, 30);
		HubKit.Box(sb, r, Color.Lerp(new Color(10, 12, 24), color, 0.12f) * 0.55f);
		HubKit.Fill(sb, new Rectangle(r.X + 3, r.Y + 6, 3, r.Height - 12), color);
		(error ? HubIcon.Texture("Images/UI/SearchCancel") : HubIcon.Item(I.RobotHat)).Draw(sb, new Rectangle(r.X + 12, r.Y + 6, 18, 18), Color.White);
		HubKit.Text(sb, notice, r.X + 36, r.Y + 7, color, 0.7f, r.Width - 44);
		return r.Height + 6;
	}

	#endregion

	#region New bot

	public static int DrawNew(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		List<CharacterEntry> chars = MultiClientManager.Characters;
		if (_character >= chars.Count)
		{
			_character = -1;
		}

		// 1. Look
		y += Step(sb, "1", "Look", "One of your characters (its look and items) or a generated one", x, y, width);
		if (HubKit.Button(sb, "bots:refreshchars", new Rectangle(x + width - 100, y - 32, 100, 28), "Refresh", Theme.Card, "Read your characters again", true, HubIcon.Item(I.Book)))
		{
			MultiClientManager.RefreshCharacters();
			_looks.Clear();
			chars = MultiClientManager.Characters;
		}
		const int tileW = 112;
		const int tileH = 150;
		const int gap = 8;
		int columns = Math.Max(1, (width + gap) / (tileW + gap));
		int count = chars.Count + 1;
		for (int i = 0; i < count; i++)
		{
			Rectangle r = new Rectangle(x + i % columns * (tileW + gap), y + i / columns * (tileH + gap), tileW, tileH);
			if (HubInput.Visible(r))
			{
				CharacterTile(sb, r, i - 1, (i == 0) ? null : chars[i - 1]);
			}
		}
		y += (count + columns - 1) / columns * (tileH + gap);
		if (chars.Count == 0)
		{
			HubKit.Text(sb, "No characters of yours found in " + Main.PlayerPath, x + 2, y, Theme.Muted, 0.68f, width);
			y += 22;
		}
		CharacterEntry character = (_character >= 0) ? chars[_character] : null;
		y += 4;
		HubKit.Text(sb, "Name", x + 2, y + 8, Theme.Text, 0.78f);
		_name = HubKit.Field(sb, "bots:name", new Rectangle(x + 80, y, Math.Min(360, width - 80), 32), _name, (character != null) ? character.Name : (MultiClientManager.RandomNames ? "random name" : "Bot1, Bot2..."), 20, HubIcon.Item(I.Sign));
		y += 46;

		// 2. Server
		int serverStep = y - start;
		y += Step(sb, "2", "Server", "Where it plays: your server or any other, at the same time as you", x, y, width);
		List<ServerEntry> servers = MultiClientManager.KnownServers();
		if (_server > servers.Count)
		{
			_server = 0;
		}
		int serverColumns = (width >= 560) ? 2 : 1;
		int serverW = (width - (serverColumns - 1) * gap) / serverColumns;
		for (int i = 0; i < servers.Count; i++)
		{
			Rectangle r = new Rectangle(x + i % serverColumns * (serverW + gap), y + i / serverColumns * 52, serverW, 46);
			ServerEntry s = servers[i];
			int index = i;
			if (Tile(sb, "bots:srv:" + i, r, _server == i, HubIcon.Item(I.WorldGlobe), s.Host + ":" + s.Port, s.Label))
			{
				_server = index;
				_scrollToStep = 3;
			}
		}
		y += (servers.Count + serverColumns - 1) / serverColumns * 52;
		Rectangle custom = new Rectangle(x, y, width, 46);
		bool customPicked = _server == servers.Count;
		if (Tile(sb, "bots:srv:custom", custom, customPicked, HubIcon.Item(I.Teleporter), "Other", null, fieldsRight: true))
		{
			_server = servers.Count;
		}
		int fx = custom.X + 110;
		string host = HubKit.Field(sb, "bots:host", new Rectangle(fx, custom.Y + 7, custom.Width - 110 - 110, 32), _customHost, "IP or domain of a friend's server", 60);
		string port = HubKit.Field(sb, "bots:port", new Rectangle(custom.Right - 100, custom.Y + 7, 92, 32), _customPort, "7777", 5);
		if (host != _customHost || port != _customPort)
		{
			_server = servers.Count;
		}
		_customHost = host;
		_customPort = new string((port ?? "").Where(char.IsDigit).ToArray());
		y += 54;
		HubKit.Text(sb, "Password", x + 2, y + 8, Theme.Text, 0.78f);
		_password = HubKit.Field(sb, "bots:pw", new Rectangle(x + 80, y, width - 80, 32), _password, "Only if the server has one (empty: yours when it is your server)", 60, HubIcon.Item(I.GoldenKey));
		y += 46;

		// 3. How many
		int countStep = y - start;
		y += Step(sb, "3", "How many", "Up to " + MultiClientManager.MaxSessions + " bots in all", x, y, width);
		Rectangle countRow = new Rectangle(x, y, width, 48);
		HubKit.Box(sb, countRow, Theme.Card * HubPrefs.Opacity);
		int room = Math.Max(1, Math.Min(10, MultiClientManager.MaxSessions - MultiClientManager.Count));
		_count = Utils.Clamp(_count, 1, room);
		if (HubKit.Button(sb, "bots:minus", new Rectangle(countRow.X + 10, countRow.Y + 9, 30, 30), "-", Theme.Slot, "One less", _count > 1))
		{
			_count--;
		}
		string countText = _count.ToString();
		HubKit.Text(sb, countText, countRow.X + 62 - HubKit.Measure(countText, 1f) / 2f, countRow.Y + 12, Color.White, 1f);
		if (HubKit.Button(sb, "bots:plus", new Rectangle(countRow.X + 84, countRow.Y + 9, 30, 30), "+", Theme.Slot, "One more", _count < room))
		{
			_count++;
		}
		HubKit.Text(sb, "UUID", countRow.X + 134, countRow.Y + 15, Theme.Muted, 0.72f);
		int ux = countRow.X + 180;
		foreach (SessionUuidMode mode in new[] { SessionUuidMode.Random, SessionUuidMode.PerName, SessionUuidMode.Mine, SessionUuidMode.Custom })
		{
			if (mode == SessionUuidMode.Custom && string.IsNullOrEmpty(MultiClientManager.CustomUuid))
			{
				continue;
			}
			string label = mode switch
			{
				SessionUuidMode.Random => "Random",
				SessionUuidMode.PerName => "One per name",
				SessionUuidMode.Mine => "Mine",
				_ => "Fixed"
			};
			int w = (int)HubKit.Measure(label, 0.7f) + 22;
			Rectangle chip = new Rectangle(ux, countRow.Y + 10, w, 28);
			if (chip.Right > countRow.Right - 6)
			{
				break;
			}
			if (Chip(sb, "bots:uuid:" + mode, chip, label, MultiClientManager.UuidMode == mode))
			{
				MultiClientManager.UuidMode = mode;
				MultiClientManager.Save();
			}
			ux += w + 6;
		}
		y += 60;

		// Connect
		ServerEntry chosen = (_server < servers.Count) ? servers[_server] : null;
		string targetHost = (chosen != null) ? chosen.Host : _customHost.Trim();
		int targetPort = (chosen != null) ? chosen.Port : (int.TryParse(_customPort, out var p2) ? p2 : 7777);
		bool ok = !string.IsNullOrWhiteSpace(targetHost) && MultiClientManager.Count < MultiClientManager.MaxSessions;
		string who = !string.IsNullOrWhiteSpace(_name) ? _name.Trim() : (character?.Name ?? "a random name");
		Rectangle go = new Rectangle(x, y, (int)(width * 0.64f), 42);
		string label2 = ok ? ("Connect " + _count + " as " + who + " to " + targetHost + ":" + targetPort) : "Pick a server";
		if (HubKit.Button(sb, "bots:connect", go, label2, Green, "The bots join now; they show up in Sessions", ok, HubIcon.Item(I.RobotHat)))
		{
			Connect(targetHost, targetPort, character, save: false);
		}
		if (HubKit.Button(sb, "bots:connectsave", new Rectangle(go.Right + 8, y, width - go.Width - 8, 42), "Connect + save", Theme.Slot, "Also keep them in Saved, to call them back later", ok, HubIcon.Item(I.Book)))
		{
			Connect(targetHost, targetPort, character, save: true);
		}
		y += 50;
		y += Notice(sb, x, y, width);
		if (_scrollToStep != 0)
		{
			// Picking goes on to the next step, so nobody misses what is further down.
			ScrollTo(((_scrollToStep == 2) ? serverStep : countStep) - 8);
			_scrollToStep = 0;
		}
		return y - start;
	}

	private static void Connect(string host, int port, CharacterEntry character, bool save)
	{
		MultiClientManager.AddSessions(_count, host, port, string.IsNullOrWhiteSpace(_name) ? null : _name, character, string.IsNullOrEmpty(_password) ? null : _password);
		if (save)
		{
			foreach (ClientSession s in MultiClientManager.All.Where(s => s.Host == host && s.Port == port && s.State != ClientSessionState.Disconnected).Reverse().Take(_count))
			{
				MultiClientManager.SaveProfile(s);
			}
		}
		ShowTab(0);
	}

	private static void CharacterTile(SpriteBatch sb, Rectangle r, int index, CharacterEntry c)
	{
		string id = "bots:char:" + index;
		bool picked = _character == index;
		bool hover = HubInput.In(r);
		if (hover && HubInput.Hover(id, (c == null) ? "A generated look with an empty inventory" : (c.Summary + "\n" + System.IO.Path.GetFileName(c.Path))) && !picked)
		{
			_character = index;
			_scrollToStep = 2;
			HubInput.Sound(12);
		}
		float glow = HubInput.Anim(id, hover);
		float on = HubInput.Anim(id + ":on", picked);
		HubKit.Box(sb, r, Color.Lerp(Color.Lerp(Theme.Card, Theme.CardHover, glow), Color.Lerp(Theme.Card, Theme.Accent, 0.3f), on) * HubPrefs.Opacity);
		if (picked)
		{
			HubKit.Fill(sb, new Rectangle(r.X + 10, r.Bottom - 5, r.Width - 20, 3), Theme.Accent);
		}
		Rectangle portrait = new Rectangle(r.X + 8, r.Y + 8, r.Width - 16, 92);
		HubKit.Box(sb, portrait, Theme.Sidebar * (HubPrefs.Opacity * 0.7f));
		if (c == null)
		{
			float bob = (float)Math.Sin(Main.GlobalTimeWrappedHourly * 2f) * 2f;
			HubIcon.Item(I.RobotHat).Draw(sb, new Rectangle(portrait.Center.X - 22, (int)(portrait.Center.Y - 22 + bob), 44, 44), Color.White);
			HubKit.TextCentered(sb, "Generated", new Rectangle(r.X, portrait.Bottom + 6, r.Width, 20), picked ? Color.White : Theme.Text, 0.8f);
			HubKit.TextCentered(sb, MultiClientManager.RandomNames ? "random name" : "Bot1, Bot2...", new Rectangle(r.X, portrait.Bottom + 26, r.Width, 18), Theme.Muted, 0.62f);
			return;
		}
		Player look = LookOf(c);
		if (look != null)
		{
			HubKit.Portrait(sb, look, new Rectangle(portrait.X, portrait.Y + 4, portrait.Width, portrait.Height), 1f, walking: hover || picked);
		}
		HubKit.TextCentered(sb, c.Name, new Rectangle(r.X, portrait.Bottom + 6, r.Width, 20), picked ? Color.White : Theme.Text, 0.8f);
		string kind = c.Summary.Split(',')[0];
		HubKit.TextCentered(sb, kind + "  ·  " + c.Player.statLifeMax + " hp", new Rectangle(r.X, portrait.Bottom + 26, r.Width, 18), Theme.Muted, 0.62f);
	}

	/// <summary>A big numbered step title.</summary>
	private static int Step(SpriteBatch sb, string number, string title, string hint, int x, int y, int width)
	{
		Rectangle badge = new Rectangle(x, y, 28, 28);
		HubKit.Box(sb, badge, Color.Lerp(Theme.Slot, Theme.Accent, 0.35f));
		HubKit.TextCentered(sb, number, badge, Color.White, 0.85f);
		HubKit.Text(sb, title, x + 38, y + 4, Theme.Accent, 0.92f);
		float tw = HubKit.Measure(title, 0.92f);
		HubKit.Text(sb, hint, x + 50 + tw, y + 7, Theme.Muted, 0.68f, width - 60 - tw - 110);
		return 38;
	}

	/// <summary>A selectable row with an icon, a title and a line; true when clicked.</summary>
	private static bool Tile(SpriteBatch sb, string id, Rectangle r, bool picked, HubIcon icon, string title, string line, bool fieldsRight = false)
	{
		bool hover = HubInput.In(r);
		bool clicked = hover && !fieldsRight && HubInput.Hover(id);
		if (fieldsRight && hover && r.X + 110 > HubInput.Mouse.X)
		{
			clicked = HubInput.Hover(id);
		}
		float glow = HubInput.Anim(id, hover);
		float on = HubInput.Anim(id + ":on", picked);
		HubKit.Box(sb, r, Color.Lerp(Color.Lerp(Theme.Card, Theme.CardHover, glow), Color.Lerp(Theme.Card, Theme.Accent, 0.28f), on) * HubPrefs.Opacity);
		HubKit.Fill(sb, new Rectangle(r.X + 4, r.Y + 8, 3, r.Height - 16), Theme.Accent * on);
		icon.Draw(sb, new Rectangle(r.X + 14, r.Y + (r.Height - 26) / 2, 26, 26), Color.White);
		if (line == null)
		{
			HubKit.Text(sb, title, r.X + 48, r.Y + (r.Height - 20) / 2f + 2f, picked ? Color.White : Theme.Text, 0.82f);
		}
		else
		{
			HubKit.Text(sb, title, r.X + 48, r.Y + 6, picked ? Color.White : Theme.Text, 0.82f, r.Width - 56);
			HubKit.Text(sb, line, r.X + 48, r.Y + 26, Theme.Muted, 0.64f, r.Width - 56);
		}
		if (clicked)
		{
			HubInput.Sound(12);
		}
		return clicked;
	}

	private static bool Chip(SpriteBatch sb, string id, Rectangle r, string label, bool active)
	{
		bool hover = HubInput.In(r);
		bool clicked = hover && HubInput.Hover(id);
		if (clicked)
		{
			HubInput.Sound(12);
		}
		float glow = HubInput.Anim(id, hover || active);
		Color back = active ? Color.Lerp(Theme.Slot, Theme.Accent, 0.4f) : Color.Lerp(Theme.Card, Theme.CardHover, glow);
		HubKit.Box(sb, r, back * HubPrefs.Opacity);
		HubKit.TextCentered(sb, label, r, active ? Color.White : Theme.Text, 0.7f);
		return clicked;
	}

	#endregion

	#region Saved

	public static int DrawSaved(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		List<SessionProfile> last = MultiClientManager.LastSessions.ToList();
		if (last.Count > 0)
		{
			y += Header(sb, HubIcon.Item(I.RecallPotion), "Online last time", "Call them back when you want; nothing connects by itself", x, y, width);
			if (HubKit.Button(sb, "bots:lastall", new Rectangle(x + width - 150, y - 36, 150, 30), "Call all back", Green, null, true, HubIcon.Item(I.RecallPotion)))
			{
				foreach (SessionProfile p in last)
				{
					MultiClientManager.ConnectLast(p);
				}
			}
			y += ProfileCards(sb, last, x, y, width, "Call back", (SessionProfile p) => MultiClientManager.ConnectLast(p), (SessionProfile p) => MultiClientManager.DismissLast(p), "Forget it") + 10;
		}
		List<SessionProfile> profiles = MultiClientManager.Profiles.ToList();
		y += Header(sb, HubIcon.Item(I.Book), "Saved bots", "\"Save\" on a bot keeps its name, look and server here", x, y, width);
		if (profiles.Count > 0 && HubKit.Button(sb, "bots:connectall", new Rectangle(x + width - 170, y - 36, 170, 30), "Connect all saved", Green, null, true, HubIcon.Item(I.RobotHat)))
		{
			foreach (SessionProfile p in profiles)
			{
				MultiClientManager.ConnectProfile(p);
			}
		}
		if (profiles.Count == 0)
		{
			y += ClientHub.EmptyNote(sb, x, y, width, HubIcon.Item(I.Book), "Nothing saved yet", "Use the book on a bot (or Connect + save) to keep it here.");
		}
		else
		{
			y += ProfileCards(sb, profiles, x, y, width, "Connect", (SessionProfile p) => MultiClientManager.ConnectProfile(p), (SessionProfile p) => MultiClientManager.DeleteProfile(p), "Delete it");
		}
		y += Notice(sb, x, y + 6, width) + 6;
		return y - start;
	}

	private static int Header(SpriteBatch sb, HubIcon icon, string title, string hint, int x, int y, int width)
	{
		icon.Draw(sb, new Rectangle(x, y + 2, 24, 24), Color.White);
		HubKit.Text(sb, title, x + 32, y + 4, Theme.Accent, 0.9f);
		float tw = HubKit.Measure(title, 0.9f);
		HubKit.Text(sb, hint, x + 44 + tw, y + 7, Theme.Muted, 0.66f, width - 44 - tw - 180);
		return 40;
	}

	private static int ProfileCards(SpriteBatch sb, List<SessionProfile> profiles, int x, int y, int width, string action, Action<SessionProfile> go, Action<SessionProfile> remove, string removeTip)
	{
		int columns = (width >= 600) ? 2 : 1;
		const int gap = 8;
		int cardW = (width - gap * (columns - 1)) / columns;
		const int cardH = 64;
		for (int i = 0; i < profiles.Count; i++)
		{
			SessionProfile p = profiles[i];
			Rectangle r = new Rectangle(x + i % columns * (cardW + gap), y + i / columns * (cardH + gap), cardW, cardH);
			if (!HubInput.Visible(r))
			{
				continue;
			}
			string id = "bots:profile:" + action + ":" + i;
			bool hover = HubInput.In(r);
			HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.CardHover, HubInput.Anim(id, hover) * 0.6f) * HubPrefs.Opacity);
			Rectangle head = new Rectangle(r.X + 10, r.Y + 8, 48, 48);
			HubKit.Box(sb, head, Theme.Sidebar * (HubPrefs.Opacity * 0.7f));
			CharacterEntry c = CharacterFor(p.PlayerFile);
			Player look = LookOf(c);
			if (look != null)
			{
				HubKit.Head(sb, look, new Vector2(head.Center.X, head.Center.Y + 2), 1.3f);
			}
			else
			{
				HubIcon.Item(I.RobotHat).Draw(sb, new Rectangle(head.X + 8, head.Y + 8, 32, 32), Color.White);
			}
			Rectangle remove2 = new Rectangle(r.Right - 36, r.Y + 17, 28, 28);
			int goW = (int)HubKit.Measure(action, 0.78f) + 42;
			Rectangle goButton = new Rectangle(remove2.X - goW - 6, r.Y + 16, goW, 30);
			int tx = head.Right + 12;
			int room = goButton.X - tx - 8;
			HubKit.Text(sb, p.Name, tx, r.Y + 10, Color.White, 0.86f, room - (p.AiEnabled ? 34 : 0));
			if (p.AiEnabled)
			{
				HubKit.Chip(sb, "AI", tx + (int)Math.Min(HubKit.Measure(p.Name, 0.86f), room - 34) + 6, r.Y + 10, new Color(150, 120, 50), Color.White, 0.58f);
			}
			string look2 = string.IsNullOrEmpty(p.PlayerFile) ? "generated look" : System.IO.Path.GetFileName(p.PlayerFile);
			HubKit.Text(sb, p.Host + ":" + p.Port + "  ·  " + look2 + (string.IsNullOrEmpty(p.Password) ? "" : "  ·  password saved"), tx, r.Y + 36, Theme.Muted, 0.64f, room);
			if (HubKit.Button(sb, id + ":go", goButton, action, Green, null, true, HubIcon.Item(I.RobotHat)))
			{
				go(p);
			}
			if (HubKit.IconButton(sb, id + ":x", remove2, HubIcon.Item(I.TrashCan), removeTip))
			{
				remove(p);
			}
		}
		return (profiles.Count + columns - 1) / columns * (cardH + gap);
	}

	#endregion

	#region Options

	public static int DrawOptions(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y += Header(sb, HubIcon.Item(I.GoldenKey), "Identity", "How new bots look to the server", x, y, width);
		Rectangle uuidRow = new Rectangle(x, y, width, 48);
		HubKit.Box(sb, uuidRow, Theme.Card * HubPrefs.Opacity);
		HubKit.Text(sb, "UUID for new bots", uuidRow.X + 12, uuidRow.Y + 6, Theme.Text, 0.76f);
		HubKit.Text(sb, MultiClientManager.UuidModeText, uuidRow.X + 12, uuidRow.Y + 26, new Color(140, 228, 155), 0.64f, 220);
		int ux = uuidRow.X + 250;
		foreach (SessionUuidMode mode in new[] { SessionUuidMode.Random, SessionUuidMode.PerName, SessionUuidMode.Mine, SessionUuidMode.Custom })
		{
			string label = mode switch
			{
				SessionUuidMode.Random => "Random",
				SessionUuidMode.PerName => "One per name",
				SessionUuidMode.Mine => "Mine",
				_ => "Fixed"
			};
			int w = (int)HubKit.Measure(label, 0.7f) + 22;
			Rectangle chip = new Rectangle(ux, uuidRow.Y + 10, w, 28);
			if (chip.Right > uuidRow.Right - 6)
			{
				break;
			}
			bool usable = mode != SessionUuidMode.Custom || !string.IsNullOrEmpty(MultiClientManager.CustomUuid);
			if (Chip(sb, "bots:opt:uuid:" + mode, chip, label, MultiClientManager.UuidMode == mode) && usable)
			{
				MultiClientManager.UuidMode = mode;
				MultiClientManager.Save();
			}
			ux += w + 6;
		}
		y += 56;
		Rectangle fixedRow = new Rectangle(x, y, width, 44);
		HubKit.Box(sb, fixedRow, Theme.Card * HubPrefs.Opacity);
		HubKit.Text(sb, "Fixed UUID", fixedRow.X + 12, fixedRow.Y + 13, Theme.Text, 0.76f);
		_customUuid = HubKit.Field(sb, "bots:uuid", new Rectangle(fixedRow.X + 120, fixedRow.Y + 6, fixedRow.Width - 120 - 104, 32), _customUuid, string.IsNullOrEmpty(MultiClientManager.CustomUuid) ? "Paste a UUID" : MultiClientManager.CustomUuid, 40, HubIcon.Item(I.GoldenKey));
		if (HubKit.Button(sb, "bots:setuuid", new Rectangle(fixedRow.Right - 96, fixedRow.Y + 7, 88, 30), "Use it", Theme.Slot, "New bots use this UUID", Guid.TryParse(_customUuid, out _)))
		{
			MultiClientManager.CustomUuid = Guid.Parse(_customUuid).ToString();
			MultiClientManager.UuidMode = SessionUuidMode.Custom;
			MultiClientManager.Save();
			_customUuid = "";
		}
		y += 52;
		Rectangle forget = new Rectangle(x, y, width, 44);
		HubKit.Box(sb, forget, Theme.Card * HubPrefs.Opacity);
		HubKit.Text(sb, "Remembered UUIDs", forget.X + 12, forget.Y + 5, Theme.Text, 0.76f);
		HubKit.Text(sb, "With \"One per name\", the same name keeps the same UUID", forget.X + 12, forget.Y + 24, Theme.Muted, 0.62f, forget.Width - 140);
		if (HubKit.Button(sb, "bots:forget", new Rectangle(forget.Right - 116, forget.Y + 7, 108, 30), "Forget", Red, "Forget the UUID of every name", true, HubIcon.Item(I.TrashCan)))
		{
			MultiClientManager.ForgetNameUuids();
		}
		y += 52;
		y += Switches(sb, x, y, width, new (string, string, string, bool, Action)[]
		{
			("bots:o:names", "Random names", "Alex42, Luna17... instead of Bot1, Bot2", MultiClientManager.RandomNames, () => MultiClientManager.RandomNames = !MultiClientManager.RandomNames)
		}) + 14;

		y += Header(sb, HubIcon.Item(I.CopperShortsword), "Behaviour", "What bots do by themselves", x, y, width);
		y += Switches(sb, x, y, width, new (string, string, string, bool, Action)[]
		{
			("bots:o:fight", "Fight enemies", "In your world: hotbar slot 1 if it is a weapon, else the first weapon on the hotbar (tools never)", MultiClientManager.SessionsFight, () => MultiClientManager.SessionsFight = !MultiClientManager.SessionsFight)
		}) + 14;

		y += Header(sb, HubIcon.Item(I.RecallPotion), "Staying online", "When you leave or they drop", x, y, width);
		y += Switches(sb, x, y, width, new (string, string, string, bool, Action)[]
		{
			("bots:o:keep", "Keep bots when I leave", "They stay connected in the menu and while you are in other worlds", MultiClientManager.KeepOnLeave, () => MultiClientManager.KeepOnLeave = !MultiClientManager.KeepOnLeave),
			("bots:o:reconnect", "Reconnect dropped bots", "Up to 5 tries; never after a kick", MultiClientManager.AutoReconnect, () => MultiClientManager.AutoReconnect = !MultiClientManager.AutoReconnect)
		}) + 14;

		y += HubBotSets.AnyStepsSection(sb, x, y, width) + 14;
		y += HubBotSets.PixieSection(sb, x, y, width) + 14;

		y += Header(sb, HubIcon.Item(I.GhostMask), "Playing as bots", "Switching between you and them", x, y, width);
		y += Switches(sb, x, y, width, new (string, string, string, bool, Action)[]
		{
			("bots:o:world", "Remember my world", "Needed to come back after playing as a bot on another server (applies from your next join)", Possession.RecordOwnWorld, () => Possession.RecordOwnWorld = !Possession.RecordOwnWorld),
			("bots:o:chat", "Show what my chat bot sees", "Only for bots on other servers (.bot chat)", MultiClientManager.ShowSessionChat, () => MultiClientManager.ShowSessionChat = !MultiClientManager.ShowSessionChat)
		});
		return y - start + 8;
	}

	/// <summary>On/off tiles in one or two columns; a click flips one and saves.</summary>
	private static int Switches(SpriteBatch sb, int x, int y, int width, (string Id, string Title, string Tip, bool On, Action Flip)[] toggles)
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
			HubKit.Text(sb, t.Title, r.X + 12, r.Y + 8, t.On ? Theme.Text : Theme.Muted, 0.78f, r.Width - 76);
			HubKit.Text(sb, t.Tip, r.X + 12, r.Y + 30, Theme.Muted * 0.9f, 0.6f, r.Width - 76);
			HubKit.Switch(sb, t.Id + ":sw", new Rectangle(r.Right - 56, r.Y + 16, 44, 22), t.On, hover);
		}
		int rows = (toggles.Length + columns - 1) / columns;
		return rows * height + (rows - 1) * gap;
	}

	#endregion
}
