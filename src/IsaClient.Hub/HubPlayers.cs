using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Community;
using IsaClient.Menu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.Localization;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// Multiplayer > Players: a card per player (the whole character walking, your crown, the I / Z statue of IsaClient and
/// zMod users, team color, health and mana, distance) with Inventory (inventory, equipment, loadouts and storage),
/// Teleport and Spectate. Inactive slots that still hold data and the server can be listed too, like the classic list.
/// </summary>
internal static class HubPlayers
{
	/// <summary>Rows of the character select frame (Images/UI/IconCompletion, 64x70) above its box: the crown.</summary>
	private const int CrownHeight = 8;

	private static Texture2D _crownFrame;

	/// <summary>
	/// The character select frame made to fit a box of <paramref name="width"/> x <paramref name="height"/>, its crown on
	/// top. Scaling it would drop rows and columns of its pixels; it is drawn 2x, so it is made wider and taller by
	/// repeating pairs of pixel columns (beside the crown) and rows (halfway down the sides). Made once, kept.
	/// </summary>
	private static Texture2D CrownFrame(int width, int height)
	{
		if (_crownFrame != null && !_crownFrame.IsDisposed && _crownFrame.Width == width && _crownFrame.Height == height + CrownHeight)
		{
			return _crownFrame;
		}
		try
		{
			Texture2D source = Main.Assets.Request<Texture2D>("Images/UI/IconCompletion", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
			int sw = source.Width;
			int sh = source.Height;
			int extraX = width - sw;
			int extraY = height + CrownHeight - sh;
			if (extraX < 0 || extraY < 0)
			{
				return null;
			}
			Color[] from = new Color[sw * sh];
			source.GetData(from);
			// Columns: the extra goes beside the crown, half on each side; rows: halfway down the sides.
			List<int> columns = Stretch(sw, new[] { sw / 4, sw * 3 / 4 }, extraX);
			List<int> rows = Stretch(sh, new[] { CrownHeight + (sh - CrownHeight) / 2 }, extraY);
			Color[] to = new Color[columns.Count * rows.Count];
			for (int y = 0; y < rows.Count; y++)
			{
				for (int x = 0; x < columns.Count; x++)
				{
					to[y * columns.Count + x] = from[rows[y] * sw + columns[x]];
				}
			}
			_crownFrame?.Dispose();
			_crownFrame = new Texture2D(Main.instance.GraphicsDevice, columns.Count, rows.Count);
			_crownFrame.SetData(to);
			return _crownFrame;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>Source indexes 0..length-1 with <paramref name="extra"/> more, made by repeating the pixel pair at each cut.</summary>
	private static List<int> Stretch(int length, int[] cuts, int extra)
	{
		List<int> result = new List<int>();
		int pairs = extra / 2;
		int odd = extra % 2;
		for (int i = 0; i < length; i++)
		{
			for (int c = 0; c < cuts.Length; c++)
			{
				int at = cuts[c] & ~1;
				if (i == at)
				{
					int share = pairs / cuts.Length + ((c < pairs % cuts.Length) ? 1 : 0);
					for (int k = 0; k < share; k++)
					{
						result.Add(at);
						result.Add(at + 1);
					}
					if (odd > 0 && c == 0)
					{
						result.Add(at);
					}
				}
			}
			result.Add(i);
		}
		return result;
	}

	private static string _filter = "";

	/// <summary>Also list inactive slots that still hold data, and the server.</summary>
	private static bool _showInactive = true;

	/// <summary>One classic row per slot, never drawn: it keeps the copy of the player the card shows.</summary>
	private static readonly Dictionary<int, CharacterListItem> _rows = new Dictionary<int, CharacterListItem>();

	private static HubTheme Theme => HubTheme.Current;

	private static CharacterListItem Row(int index)
	{
		if (!_rows.TryGetValue(index, out CharacterListItem row))
		{
			row = new CharacterListItem(index);
			_rows[index] = row;
		}
		return row;
	}

	private static bool Listed(int index)
	{
		if (index == 255)
		{
			return false;
		}
		Player p = Main.player[index];
		if (p == null)
		{
			return false;
		}
		return p.active || (_showInactive && index != Main.myPlayer && !string.IsNullOrEmpty(p.name));
	}

	private static bool IsActive(int index)
	{
		return index == 255 || (Main.player[index] != null && Main.player[index].active);
	}

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		if (HubPlayerView.Target >= 0)
		{
			return HubPlayerView.Draw(sb, x, y, width);
		}
		int start = y;
		int online = Main.player.Count(p => p != null && p.active && p.whoAmI < 255);
		HubKit.Text(sb, online + ((online == 1) ? " player online" : " players online"), x + 2, y + 7, Theme.Accent, 0.9f);
		int fieldW = Math.Min(240, width / 3);
		_filter = HubKit.Field(sb, "players:filter", new Rectangle(x + width - fieldW, y, fieldW, 30), _filter, "Find a player...", 30, HubIcon.Texture("Images/UI/Cursor_2"));
		Rectangle toggle = new Rectangle(x + width - fieldW - 190, y + 4, 180, 22);
		bool toggleHover = HubInput.In(toggle);
		HubKit.Switch(sb, "players:inactive", new Rectangle(toggle.X, toggle.Y, 40, 22), _showInactive, toggleHover);
		HubKit.Text(sb, "Show offline", toggle.X + 46, toggle.Y + 3, Theme.Muted, 0.72f);
		if (toggleHover && HubInput.Hover("players:inactivetoggle", "Also show players who left (their last known data)."))
		{
			_showInactive = !_showInactive;
			HubInput.Sound(_showInactive ? 10 : 11);
		}
		y += 40;
		string filter = (_filter ?? "").Trim();
		List<int> players = Enumerable.Range(0, 256).Where(Listed).OrderBy(i => (i == Main.myPlayer) ? -1 : (IsActive(i) && i != 255 ? i : 1000 + i)).ToList();
		if (filter.Length > 0)
		{
			players = players.Where(i => (Row(i).OurPlayer.name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
		}
		if (players.Count == 0)
		{
			y += ClientHub.EmptyNote(sb, x, y, width, HubIcon.Item(I.Binoculars), "Nobody here", "No player matches \"" + filter + "\".");
			return y - start;
		}
		int columns = (width >= 640) ? 2 : 1;
		int gap = 8;
		int cardW = (width - gap * (columns - 1)) / columns;
		int cardH = 100;
		for (int i = 0; i < players.Count; i++)
		{
			Rectangle r = new Rectangle(x + i % columns * (cardW + gap), y + i / columns * (cardH + gap), cardW, cardH);
			if (HubInput.Visible(r))
			{
				CharacterListItem row = Row(players[i]);
				row.Refresh();
				Card(sb, players[i], row, r);
			}
		}
		y += (players.Count + columns - 1) / columns * (cardH + gap);
		return y - start;
	}

	private static void Card(SpriteBatch sb, int index, CharacterListItem row, Rectangle r)
	{
		Player live = Main.player[index];
		Player p = row.OurPlayer;
		bool server = index == 255;
		bool active = IsActive(index);
		bool me = index == Main.myPlayer;
		int kind = (live != null && !server && active) ? live.IsaClientKind : 0;
		string id = "player:" + index;
		bool hover = HubInput.In(r);
		float glow = HubInput.Anim(id, hover);
		Color team = (active && !server && p.team > 0 && p.team < Main.teamColor.Length) ? Main.teamColor[p.team] : Color.White;
		bool spectating = Spectate.SpectatePlayerTarget == index;
		HubKit.Box(sb, r, Color.Lerp(me ? Color.Lerp(Theme.Card, Theme.Slot, 0.4f) : Theme.Card, Theme.CardHover, glow * 0.7f) * (HubPrefs.Opacity * (active ? 1f : 0.6f)));
		if (active && !server && p.team > 0)
		{
			HubKit.Fill(sb, new Rectangle(r.X + 3, r.Y + 10, 3, r.Height - 20), team);
		}

		// The whole character, walking like on the character select screen; your crown on top.
		Rectangle portrait = new Rectangle(r.X + 10, r.Y + 10, 72, 80);
		HubKit.Box(sb, portrait, Theme.Sidebar * (HubPrefs.Opacity * 0.7f));
		if (server)
		{
			HubIcon.Item(I.Cog).Draw(sb, new Rectangle(portrait.X + 16, portrait.Y + 20, 40, 40), Color.White);
		}
		else
		{
			HubKit.Portrait(sb, p, new Rectangle(portrait.X, portrait.Y + 4, portrait.Width, portrait.Height), 1f, walking: active);
		}
		if (me)
		{
			// The golden frame around the dark box, its crown above it (inside the card, which the list clips).
			Texture2D frame = CrownFrame(portrait.Width, portrait.Height);
			if (frame != null)
			{
				sb.Draw(frame, new Vector2(portrait.X, portrait.Y - CrownHeight), Color.White);
			}
		}

		// Buttons on the right
		// Small icon buttons (their names show on hover), so names and numbers keep their room.
		int bw = 30;
		int bx = r.Right - bw - 8;
		Rectangle inventory = new Rectangle(bx, r.Y + 8, bw, 26);
		Rectangle teleport = new Rectangle(bx, r.Y + 37, bw, 26);
		Rectangle spectate = new Rectangle(bx, r.Y + 66, bw, 26);

		// Name: the I statue (IsaClient), the Z statue (zMod) or the T statue (TerraAngel) in front, like the classic list.
		int tx = portrait.Right + 12;
		int textRoom = bx - tx - 8;
		string name = string.IsNullOrEmpty(p.name) ? (server ? "Server" : ("Player " + index)) : p.name;
		Color nameColor = !active ? Color.DarkGray : (server ? Color.DeepPink : team);
		int statue = (kind == 1) ? I.AlphabetStatueI : ((kind == 2) ? I.AlphabetStatueZ : ((kind == 3) ? I.AlphabetStatueT : 0));
		int nameX = tx;
		if (statue > 0)
		{
			Rectangle statueRect = new Rectangle(tx, r.Y + 6, 20, 24);
			HubIcon.Item(statue).Draw(sb, statueRect, Color.Lerp(Color.White, Color.LightCyan, 0.5f), 1f);
			if (HubInput.In(statueRect))
			{
				HubInput.Hover(id + ":statue", me ? "Other IsaClient users see you as an IsaClient user." : ((kind == 1) ? "IsaClient user" : ((kind == 2) ? "zMod user" : "TerraAngel user")));
			}
			nameX += 24;
		}
		float nameRoom = bx - nameX - 8;
		HubKit.Text(sb, name, nameX, r.Y + 10, nameColor, 0.92f, nameRoom);
		float nameW = Math.Min(HubKit.Measure(name, 0.92f), nameRoom);
		int chipX = (int)(nameX + nameW + 8);
		chipX = Badge(sb, me ? "You" : null, chipX, r.Y + 10, Theme.Slot);
		chipX = Badge(sb, (!active && !server) ? "Offline" : null, chipX, r.Y + 10, new Color(90, 90, 100));
		chipX = Badge(sb, (active && live.joining && !me) ? "Joining" : null, chipX, r.Y + 10, new Color(120, 110, 60));
		chipX = Badge(sb, (active && live.wasDetectedInVanish && !me) ? "Vanished" : null, chipX, r.Y + 10, new Color(110, 110, 120));
		Badge(sb, (active && !server && live.hostile) ? "PvP" : null, chipX, r.Y + 10, new Color(160, 60, 60));

		// Bars and details
		int barW = Math.Max(60, textRoom);
		if (server)
		{
			HubKit.Text(sb, "The server: life and mana without limits.", tx, r.Y + 40, Theme.Muted, 0.72f, textRoom);
		}
		else
		{
			Bar(sb, new Rectangle(tx, r.Y + 38, barW, 14), p.statLife, p.statLifeMax2, new Color(220, 60, 70), "Life");
			Bar(sb, new Rectangle(tx, r.Y + 56, barW, 14), p.statMana, p.statManaMax2, new Color(70, 120, 230), "Mana");
		}
		string where = me ? "here" : (active ? (Distance(live) + " blocks") : "offline");
		string info = server ? ("#" + index) : (Difficulty(p) + "  ·  " + where + "  ·  #" + index);
		HubKit.Text(sb, info, tx, r.Y + 76, Theme.Muted, 0.68f, textRoom);

		if (hover && !server)
		{
			HubInput.Hover(id, me ? "Click: My Character" : ("Click: see " + name + "'s inventory, equipment, loadouts and storage"));
			if (HubInput.Clicked && !inventory.Contains(HubInput.Mouse) && !teleport.Contains(HubInput.Mouse) && !spectate.Contains(HubInput.Mouse))
			{
				OpenInventory(index);
			}
		}
		if (HubKit.IconButton(sb, id + ":inv", inventory, HubIcon.Item(me ? I.Mannequin : I.PiggyBank), me ? "My Character" : "Inventory, equipment, loadouts and storage", false, !server))
		{
			OpenInventory(index);
		}
		bool canAct = !me && !server && active;
		if (HubKit.IconButton(sb, id + ":tp", teleport, HubIcon.Item(I.MagicMirror), "Teleport to " + name, false, canAct && Spectate.IsInsideWorld(live)))
		{
			Spectate.TeleportToPlayer(index);
			SoundEngine.PlaySound(12);
		}
		if (HubKit.IconButton(sb, id + ":spec", spectate, HubIcon.Item(I.Binoculars), spectating ? "Stop spectating" : "Spectate. Right click: with their inventory in your HUD.", spectating, canAct))
		{
			Spectate.ToggleSpectate(index, withInventory: false);
		}
		if (canAct && HubInput.In(spectate) && HubInput.RightClicked)
		{
			Spectate.ToggleSpectate(index, withInventory: true);
			HubInput.Consume();
		}
	}

	private static void OpenInventory(int index)
	{
		if (index == Main.myPlayer)
		{
			CharacterWindow.Open();
			return;
		}
		HubPlayerView.Open(index);
	}

	private static int Badge(SpriteBatch sb, string text, int x, int y, Color back)
	{
		if (text == null)
		{
			return x;
		}
		Rectangle r = HubKit.Chip(sb, text, x, y, back, Color.White, 0.62f);
		return r.Right + 4;
	}

	private static void Bar(SpriteBatch sb, Rectangle r, int value, int max, Color color, string label)
	{
		HubKit.Fill(sb, r, new Color(15, 18, 32) * 0.9f);
		float fill = (max > 0) ? MathHelper.Clamp((float)value / max, 0f, 1f) : 0f;
		HubKit.Fill(sb, new Rectangle(r.X + 1, r.Y + 1, (int)((r.Width - 2) * fill), r.Height - 2), color * 0.85f);
		HubKit.Fill(sb, new Rectangle(r.X + 1, r.Y + 1, (int)((r.Width - 2) * fill), 3), Color.White * 0.15f);
		HubKit.Text(sb, label + " " + value + " / " + max, r.X + 6, r.Y - 1, Color.White, 0.6f, r.Width - 8);
	}

	private static string Difficulty(Player p)
	{
		switch (p.difficulty)
		{
		case 1:
			return Language.GetTextValue("UI.Mediumcore");
		case 2:
			return Language.GetTextValue("UI.Hardcore");
		case 3:
			return Language.GetTextValue("UI.Creative");
		default:
			return Language.GetTextValue("UI.Softcore");
		}
	}

	private static int Distance(Player p)
	{
		Player me = Main.LocalPlayer;
		return (me == null || p == null) ? 0 : (int)(Vector2.Distance(me.Center, p.Center) / 16f);
	}
}
