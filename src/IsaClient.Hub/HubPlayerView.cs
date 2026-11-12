using System;
using System.Collections.Generic;
using IsaClient.Community;
using IsaClient.Core;
using IsaClient.Gameplay;
using IsaClient.Menu;
using IsaClient.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.UI;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// Multiplayer > Players > a player: the inventory viewer in the panel's style. Their portrait over the biome they are in,
/// who they are (IsaClient, zMod or TerraAngel user, team, PvP, spam flags), life and mana, where they are, the actions
/// (teleport, spectate, copy their look or inventory, pixel art, save a .plr), and their items: inventory, coins, ammo,
/// held item, trash, the four personal storages, armor, accessories, vanity, dyes, equipment, loadouts (click one to
/// preview it) and buffs. Read-only like the classic viewer: Shift+click copies an item to you, a double click spawns it on
/// you, the favorite key with the chat open shares it. It shares the classic viewer's state
/// (<see cref="InventoryViewer.OurPlayer"/>), which mirrors the player live.
/// </summary>
internal static class HubPlayerView
{
	/// <summary>The player shown, or -1 for the list.</summary>
	public static int Target = -1;

	private static HubTheme Theme => HubTheme.Current;

	private static int Index => InventoryViewer.TargetPlayer;

	private static Player Owner => Main.player[Index];

	private static Player View => InventoryViewer.OurPlayer;

	/// <summary>Size of an item slot this frame, in panel units.</summary>
	private static int _slot = 40;

	// The slot or buff under the mouse: its game tooltip is asked for after the panel's batch (see DrawHoverTooltip).
	private static Item[] _hoverItems;

	private static int _hoverContext;

	private static int _hoverIndex;

	private static int _hoverBuff = -1;

	private static bool _hoverArmor;

	private static readonly Color LockedBack = new Color(80, 80, 80, 80);

	public static void Open(int index)
	{
		if (IsaClient.Networking.ServerModes.BlocksTool())
		{
			return;
		}
		Target = Utils.Clamp(index, 0, 254);
		InventoryViewer.Select(Target);
		HubInput.Sound(10);
	}

	public static void Close()
	{
		Target = -1;
		InventoryViewer.TemporaryLoadoutIndex = -1;
		HubInput.Sound(11);
	}

	/// <summary>Previous / next player with data (online first-come order of the slots).</summary>
	private static void Step(int direction)
	{
		int index = Index;
		for (int tries = 0; tries < 255; tries++)
		{
			index = (index + direction + 255) % 255;
			Player p = Main.player[index];
			if (p != null && (p.active || !string.IsNullOrEmpty(p.name)))
			{
				break;
			}
		}
		Open(index);
	}

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		InventoryViewer.CloneFromIndex();
		y += DrawHeader(sb, x, y, width);
		y += 10;
		int left = (width >= 720) ? 236 : Math.Max(190, width / 3);
		int sideH = DrawSide(sb, x, y, left);
		int rx = x + left + 14;
		int rw = width - left - 14;
		float gameScale = Main.inventoryScale;
		Color gameBack = Main.inventoryBack;
		Player drawPlayer = ItemSlot.IsaDrawPlayer;
		int itemsH;
		try
		{
			itemsH = DrawItems(sb, rx, y, rw);
		}
		finally
		{
			ItemSlot.IsaDrawPlayer = drawPlayer;
			Main.inventoryScale = gameScale;
			Main.inventoryBack = gameBack;
		}
		y += Math.Max(sideH, itemsH);
		return y - start;
	}

	#region Header

	private static int DrawHeader(SpriteBatch sb, int x, int y, int width)
	{
		if (HubKit.Button(sb, "view:back", new Rectangle(x, y, 112, 32), "Players", Theme.Slot, "Back to the list of players", true, HubIcon.Texture("Images/UI/IconPrev")))
		{
			Close();
		}
		if (HubKit.IconButton(sb, "view:prev", new Rectangle(x + 118, y, 32, 32), HubIcon.Texture("Images/UI/Bestiary/Button_Back"), "Previous player"))
		{
			Step(-1);
		}
		if (HubKit.IconButton(sb, "view:next", new Rectangle(x + 154, y, 32, 32), HubIcon.Texture("Images/UI/Bestiary/Button_Forward"), "Next player"))
		{
			Step(1);
		}
		Player p = View;
		Player live = Owner;
		bool active = live.active;
		bool me = Index == Main.myPlayer;
		int nx = x + 198;
		int kind = active && !me ? live.IsaClientKind : 0;
		int statue = (kind == 1) ? I.AlphabetStatueI : ((kind == 2) ? I.AlphabetStatueZ : ((kind == 3) ? I.AlphabetStatueT : 0));
		if (statue > 0)
		{
			Rectangle statueRect = new Rectangle(nx, y + 2, 22, 28);
			HubIcon.Item(statue).Draw(sb, statueRect, Color.Lerp(Color.White, Color.LightCyan, 0.5f), 1f);
			if (HubInput.In(statueRect))
			{
				HubInput.Hover("view:statue", (kind == 1) ? "IsaClient user" : ((kind == 2) ? "zMod user" : "TerraAngel user"));
			}
			nx += 28;
		}
		string name = string.IsNullOrEmpty(p.name) ? ("Player " + Index) : p.name;
		Color team = (active && p.team > 0 && p.team < Main.teamColor.Length) ? Main.teamColor[p.team] : Color.White;
		int idW = (int)HubKit.Measure("#" + Index, 0.72f) + 8;
		float room = x + width - idW - nx - 8;
		HubKit.RawText(sb, name, nx, y + 4, active ? team : Color.DarkGray, 1.05f, room);
		float nameW = Math.Min(HubKit.Measure(name, 1.05f), room);
		int cx = (int)(nx + nameW + 10);
		cx = Badge(sb, me ? "You" : null, cx, y + 8, Theme.Slot);
		cx = Badge(sb, !active ? "Offline" : null, cx, y + 8, new Color(90, 90, 100));
		cx = Badge(sb, (active && live.joining && !me) ? "Joining" : null, cx, y + 8, new Color(120, 110, 60));
		cx = Badge(sb, (active && live.wasDetectedInVanish && !me) ? "Vanished" : null, cx, y + 8, new Color(110, 110, 120));
		cx = Badge(sb, (active && live.hostile) ? "PvP" : null, cx, y + 8, new Color(160, 60, 60));
		Badge(sb, (InventoryViewer.TemporaryLoadoutIndex >= 0) ? Language.GetTextValue("UI.Loadout" + (InventoryViewer.TemporaryLoadoutIndex + 1)) : null, cx, y + 8, new Color(70, 110, 160));
		HubKit.RawText(sb, "#" + Index, x + width - idW, y + 9, Theme.Muted, 0.72f);
		return 34;
	}

	private static int Badge(SpriteBatch sb, string text, int x, int y, Color back)
	{
		if (text == null)
		{
			return x;
		}
		return HubKit.Chip(sb, text, x, y, back, Color.White, 0.62f).Right + 4;
	}

	#endregion

	#region Side: portrait, info, actions

	private static int DrawSide(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		Player p = View;
		Player live = Owner;
		bool active = live.active;
		bool me = Index == Main.myPlayer;

		// The character over the map background of their biome.
		Rectangle card = new Rectangle(x, y, width, 176);
		HubKit.Box(sb, card, Theme.Sidebar * HubPrefs.Opacity);
		Rectangle scene = new Rectangle(card.X + 4, card.Y + 4, card.Width - 8, card.Height - 8);
		DrawBiome(sb, scene, active ? InventoryViewerButtons.BiomeBackground(p) : TextureAssets.MapBGs[0], active ? 1f : 0.45f);
		HubKit.Fill(sb, new Rectangle(scene.X, scene.Bottom - 26, scene.Width, 26), Color.Black * 0.25f);
		if (active && p.team > 0 && p.team < Main.teamColor.Length)
		{
			HubKit.Fill(sb, new Rectangle(scene.X, scene.Y, scene.Width, 3), Main.teamColor[p.team]);
		}
		HubKit.Portrait(sb, p, new Rectangle(scene.X, scene.Y + 10, scene.Width, scene.Height - 10), 1.6f, walking: active);
		y = card.Bottom + 8;

		// Life and mana
		Bar(sb, new Rectangle(x, y, width, 16), p.statLife, p.statLifeMax2, new Color(220, 60, 70), Lang.inter[0].Value.TrimEnd(':'), HubIcon.Item(I.LifeCrystal));
		y += 20;
		Bar(sb, new Rectangle(x, y, width, 16), p.statMana, p.statManaMax2, new Color(70, 120, 230), Lang.inter[2].Value.TrimEnd(':'), HubIcon.Item(I.ManaCrystal));
		y += 24;

		// Facts
		List<KeyValuePair<HubIcon, string>> facts = new List<KeyValuePair<HubIcon, string>>();
		facts.Add(new KeyValuePair<HubIcon, string>(HubIcon.Item(I.Heart), DifficultyName(p)));
		if (active)
		{
			if (Spectate.IsInsideWorld(live))
			{
				string where = Fmt("Position: {0}, {1}", (int)live.Bottom.X / 16, (int)live.Bottom.Y / 16);
				if (!me && Main.LocalPlayer != null)
				{
					where += "  ·  " + Fmt("{0} blocks", (int)(Vector2.Distance(Main.LocalPlayer.Center, live.Center) / 16f));
				}
				facts.Add(new KeyValuePair<HubIcon, string>(HubIcon.Item(I.GPS), where));
			}
			else
			{
				facts.Add(new KeyValuePair<HubIcon, string>(HubIcon.Item(I.GPS), IsaClient.Languages.L10n.T("Position: Unavailable!")));
			}
		}
		else
		{
			facts.Add(new KeyValuePair<HubIcon, string>(HubIcon.Item(I.Tombstone), IsaClient.Languages.L10n.T("Offline: their last known data")));
		}
		if (active && !me)
		{
			int kind = live.IsaClientKind;
			if (kind > 0)
			{
				int statue = (kind == 1) ? I.AlphabetStatueI : ((kind == 2) ? I.AlphabetStatueZ : I.AlphabetStatueT);
				facts.Add(new KeyValuePair<HubIcon, string>(HubIcon.Item(statue), IsaClient.Languages.L10n.T((kind == 1) ? "IsaClient User" : ((kind == 2) ? "zMod User" : "TerraAngel User"))));
			}
		}
		foreach (KeyValuePair<HubIcon, string> fact in facts)
		{
			fact.Key.Draw(sb, new Rectangle(x + 2, y, 18, 18), Color.White);
			HubKit.RawText(sb, fact.Value, x + 26, y + 1, Theme.Text, 0.72f, width - 28);
			y += 22;
		}
		if (active && !me)
		{
			// The client's own spam detection.
			int sx = x;
			int sy = y;
			sx = Flag(sb, live.isProjectileSpammer ? "Projectile spammer" : null, sx, ref sy, x, width);
			sx = Flag(sb, live.isParticleSpammer ? "Particle spammer" : null, sx, ref sy, x, width);
			sx = Flag(sb, live.isSoundSpammer ? "Sound spammer" : null, sx, ref sy, x, width);
			sx = Flag(sb, live.isNebulaSpammer ? "Nebula spammer" : null, sx, ref sy, x, width);
			y = (sx != x || sy != y) ? (sy + 24) : y;
		}
		y += 4;
		y = DrawActions(sb, x, y, width);
		return y - start;
	}

	private static int Flag(SpriteBatch sb, string text, int cx, ref int cy, int x, int width)
	{
		if (text == null)
		{
			return cx;
		}
		float w = HubKit.Measure(text, 0.62f) + 14f;
		if (cx > x && cx + w > x + width)
		{
			cx = x;
			cy += 22;
		}
		return HubKit.Chip(sb, text, cx, cy, new Color(150, 60, 60), Color.White, 0.62f).Right + 4;
	}

	private static string Fmt(string format, params object[] args)
	{
		return IsaClient.Languages.L10n.F(format, args);
	}

	/// <summary>The map background, cut to cover the box without stretching.</summary>
	private static void DrawBiome(SpriteBatch sb, Rectangle box, ReLogic.Content.Asset<Texture2D> asset, float alpha)
	{
		Texture2D texture = null;
		try
		{
			texture = asset?.Value;
		}
		catch
		{
		}
		if (texture == null || texture.Width <= 0 || texture.Height <= 0)
		{
			HubKit.Fill(sb, box, new Color(40, 60, 110));
			return;
		}
		float ratio = (float)box.Width / box.Height;
		Rectangle source = new Rectangle(0, 0, texture.Width, texture.Height);
		if ((float)texture.Width / texture.Height > ratio)
		{
			source.Width = (int)(texture.Height * ratio);
			source.X = (texture.Width - source.Width) / 2;
		}
		else
		{
			source.Height = (int)(texture.Width / ratio);
			source.Y = texture.Height - source.Height;
		}
		sb.Draw(texture, box, source, Color.White * alpha);
	}

	private static void Bar(SpriteBatch sb, Rectangle r, int value, int max, Color color, string label, HubIcon icon)
	{
		HubKit.Fill(sb, r, new Color(15, 18, 32) * 0.9f);
		float fill = (max > 0) ? MathHelper.Clamp((float)value / max, 0f, 1f) : 0f;
		HubKit.Fill(sb, new Rectangle(r.X + 1, r.Y + 1, (int)((r.Width - 2) * fill), r.Height - 2), color * 0.85f);
		HubKit.Fill(sb, new Rectangle(r.X + 1, r.Y + 1, (int)((r.Width - 2) * fill), 4), Color.White * 0.15f);
		icon.Draw(sb, new Rectangle(r.X + 3, r.Y + 1, 14, 14), Color.White);
		HubKit.RawText(sb, label + "  " + value + " / " + max, r.X + 22, r.Y, Color.White, 0.62f, r.Width - 26);
	}

	private static string DifficultyName(Player p)
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

	private static int DrawActions(SpriteBatch sb, int x, int y, int width)
	{
		Player live = Owner;
		bool active = live.active;
		bool me = Index == Main.myPlayer;
		bool hasCharacter = !string.IsNullOrEmpty(View.name);
		int half = (width - 6) / 2;
		const int h = 30;
		Color color = Theme.Slot;
		HubKit.Text(sb, "Actions", x, y, Theme.Muted, 0.66f);
		y += 18;
		if (active && !me)
		{
			bool inside = Spectate.IsInsideWorld(live);
			if (HubKit.Button(sb, "view:tp", new Rectangle(x, y, half, h), "Teleport", color, inside ? "Teleport to them" : "Their position is not known", inside, HubIcon.Item(I.MagicMirror)))
			{
				Spectate.TeleportToPlayer(Index);
				SoundEngine.PlaySound(12);
			}
			bool spectating = Spectate.SpectatePlayerTarget == Index;
			Rectangle spec = new Rectangle(x + half + 6, y, half, h);
			if (HubKit.Button(sb, "view:spec", spec, spectating ? "Stop" : "Spectate", spectating ? Color.Lerp(color, Theme.Accent, 0.5f) : color, spectating ? "Stop spectating" : "Left click: spectate.\nRight click: spectate and see their inventory in your HUD.", inside || spectating, HubIcon.Item(I.Binoculars)))
			{
				SpectateThem(withInventory: false);
			}
			if (HubInput.In(spec) && HubInput.RightClicked)
			{
				SpectateThem(withInventory: true);
				HubInput.Consume();
			}
			y += h + 6;
		}
		else if (Spectate.SpectatePlayerTarget != -1)
		{
			if (HubKit.Button(sb, "view:specstop", new Rectangle(x, y, width, h), "Stop Spectating", color, null, true, HubIcon.Item(I.Binoculars)))
			{
				Spectate.SpectatePlayerTarget = -1;
				Spectate.ShowSpectatedInventory = false;
				SoundEngine.PlaySound(12);
			}
			y += h + 6;
		}
		if (active && Main.netMode == 1)
		{
			// Lucy talking over them until clicked again.
			int target = me ? -1 : Index;
			bool lucy = LucyChorus.On && LucyChorus.Target == target;
			if (HubKit.Button(sb, "view:lucy", new Rectangle(x, y, width, h), lucy ? "Stop Lucy" : "Send Lucy", lucy ? Color.Lerp(color, LucyChorus.LucyColor, 0.5f) : color, lucy ? "Lucy stops talking over them." : ("Lucy talks over " + (me ? "you" : live.name) + ", " + LucyChorus.Rate + " lines a second, until you click again.\nLines, speed and your own lines: Experimental, Lucy."), true, HubIcon.Item(I.LucyTheAxe)))
			{
				if (lucy)
				{
					LucyChorus.Stop();
				}
				else
				{
					LucyChorus.Start(target);
				}
				SoundEngine.PlaySound(12);
			}
			y += h + 6;
		}
		if (hasCharacter && !me)
		{
			if (HubKit.Button(sb, "view:look", new Rectangle(x, y, half, h), "Copy Look", color, "Wear their hair, skin and colors (your name stays).\nRestore My Character puts yours back.", true, HubIcon.Item(I.FamiliarWig)))
			{
				CharacterPresets.Wear(View, WearParts.Look, View.name);
			}
			if (HubKit.Button(sb, "view:inv", new Rectangle(x + half + 6, y, half, h), "Copy Items", color, "Wear their inventory, equipment, loadouts, life and mana.\nRestore My Character puts yours back.", true, HubIcon.Item(I.Chest)))
			{
				CharacterPresets.Wear(View, WearParts.Inventory | WearParts.Equipment | WearParts.Stats, View.name);
			}
			y += h + 6;
		}
		if (CharacterPresets.Wearing)
		{
			if (HubKit.Button(sb, "view:restore", new Rectangle(x, y, width, h), "Restore My Character", Color.Lerp(color, Theme.Accent, 0.35f), "Puts your own look, inventory, equipment, life and mana back.", true, HubIcon.Item(I.Mannequin)))
			{
				CharacterPresets.Restore();
			}
			y += h + 6;
		}
		if (hasCharacter)
		{
			Rectangle art = new Rectangle(x, y, half, h);
			if (HubKit.Button(sb, "view:art", art, "Pixel Art", color, "Builds this character out of real blocks and starts a paste.\nOptions: " + CharacterPresets.ArtOptionsText() + "\nRight click: change size. Options are in your own row's window and .pixelart", true, HubIcon.Item(I.Paintbrush)))
			{
				CharacterPresets.RequestPixelArt(View, View.name);
				SoundEngine.PlaySound(12);
			}
			if (HubInput.In(art) && HubInput.RightClicked)
			{
				CharacterPresets.ArtScale = (CharacterPresets.ArtScale % 4) + 1;
				SoundEngine.PlaySound(12);
				HubInput.Consume();
			}
			bool confirm = Main.timeForVisualEffects < _saveArmedUntil;
			if (HubKit.Button(sb, "view:save", new Rectangle(x + half + 6, y, half, h), confirm ? "Click again" : "Save .plr", confirm ? (Theme.Danger * 0.85f) : color, "Saves a copy of this character to your Players folder.\nNew file name, none of your characters is overwritten.", true, HubIcon.Item(I.Book)))
			{
				if (confirm)
				{
					_saveArmedUntil = 0.0;
					SoundEngine.PlaySound(12);
					CharacterPresets.SaveCopy(Owner);
				}
				else
				{
					_saveArmedUntil = Main.timeForVisualEffects + 150.0;
				}
			}
			y += h + 6;
		}
		if (me)
		{
			bool share = PlayerListPanel.ShareMyInfo;
			if (HubKit.Button(sb, "view:share", new Rectangle(x, y, width, h), share ? "Show Me As IsaClient: On" : "Show Me As IsaClient: Off", color, "Lets other IsaClient users see that you use IsaClient (the I statue in their Player List).\nzMod and vanilla clients never see it. Saved in your settings.", true, HubIcon.Item(I.AlphabetStatueI)))
			{
				PlayerListPanel.ShareMyInfo = !share;
				ClientSettings.Save();
				SoundEngine.PlaySound(12);
			}
			y += h + 6;
		}
		y = DrawChatActions(sb, x, y, width);
		return y;
	}

	#region Heal, Ban and the custom button

	/// <summary>What Ban writes in the chat. Placeholders: &lt;player&gt; (or &lt;jugador&gt;, &lt;name&gt;) and &lt;id&gt;.</summary>
	[Setting("PlayerViewBanCommand")]
	public static string BanCommand = "/ban add \"<player>\"";

	/// <summary>What the custom button writes in the chat (same placeholders); empty until set.</summary>
	[Setting("PlayerViewCustomCommand")]
	public static string CustomCommand = "";

	[Setting("PlayerViewCustomLabel")]
	public static string CustomLabel = "Custom";

	/// <summary>"ban" or "custom" while its command is being edited.</summary>
	private static string _editing;

	private static double _banArmedUntil;

	private static int DrawChatActions(SpriteBatch sb, int x, int y, int width)
	{
		Player live = Owner;
		if (!live.active)
		{
			return y;
		}
		bool me = Index == Main.myPlayer;
		string name = live.name ?? "";
		int half = (width - 6) / 2;
		const int h = 30;
		const int pencil = 26;
		Color color = Theme.Slot;
		// Another IsaClient player: games over the pipe.
		if (!me && live.IsaClientKind == 1)
		{
			bool invited = IsaClient.Games.GameLobby.Outgoing != null && IsaClient.Games.GameLobby.Outgoing.To == Index;
			bool can = IsaClient.Games.GameLobby.CanPlay && !IsaClient.Games.GameLobby.InOnlineMatch && !invited;
			List<IsaClient.Games.PipeGame> games = IsaClient.Games.GameLobby.Games;
			int each = (width - 6 * (games.Count - 1)) / games.Count;
			for (int i = 0; i < games.Count; i++)
			{
				IsaClient.Games.PipeGame game = games[i];
				bool thisOne = invited && IsaClient.Games.GameLobby.Outgoing.Game == game;
				if (HubKit.Button(sb, "view:game:" + game.Id, new Rectangle(x + i * (each + 6), y, each, h), thisOne ? "Invited..." : game.Name, Color.Lerp(color, new Color(255, 170, 110), 0.25f), can ? IsaClient.Languages.L10n.F("Invite to {0}: they get a little invitation and the board opens when they accept.", IsaClient.Languages.L10n.T(game.Name)) : "You need a server, the pipe on and no game going on.", can, HubIcon.Game(game)))
				{
					IsaClient.Games.GameLobby.InvitePlayer(game, Index);
				}
			}
			y += h + 6;
		}
		if (HubKit.Button(sb, "view:heal", new Rectangle(x, y, half, h), "Heal", color, IsaClient.Languages.L10n.F("Drops hearts on {0}, like .heal", me ? IsaClient.Languages.L10n.T("you") : name), true, HubIcon.Item(I.Heart)))
		{
			ItemTools.Heal(live, 50);
			SoundEngine.PlaySound(12);
		}
		// Ban: writes its command in the chat (TShock's by default); a second click confirms it.
		Rectangle ban = new Rectangle(x + half + 6, y, half - pencil - 4, h);
		bool banReady = !me && !string.IsNullOrWhiteSpace(BanCommand);
		bool armed = Main.timeForVisualEffects < _banArmedUntil;
		if (HubKit.Button(sb, "view:ban", ban, armed ? "Click again" : "Ban", armed ? (Theme.Danger * 0.85f) : Color.Lerp(color, Theme.Danger, 0.3f), banReady ? (IsaClient.Languages.L10n.T("Writes in the chat:") + " " + Fill(BanCommand, live) + "\n" + IsaClient.Languages.L10n.T("The pencil changes the command.")) : "Set its command with the pencil.", banReady, HubIcon.Item(1513)))
		{
			if (armed)
			{
				_banArmedUntil = 0.0;
				Run(BanCommand, live);
			}
			else
			{
				_banArmedUntil = Main.timeForVisualEffects + 150.0;
			}
		}
		if (HubKit.IconButton(sb, "view:banedit", new Rectangle(ban.Right + 4, y, pencil, h), HubIcon.Texture("Images/UI/ButtonRename"), "Change what Ban writes in the chat", _editing == "ban"))
		{
			ToggleEditing("ban");
		}
		y += h + 6;
		if (_editing == "ban")
		{
			y = EditField(sb, "view:banfield", x, y, width, ref BanCommand, "/ban add \"<player>\"");
		}
		// The custom button: anything in the chat.
		Rectangle custom = new Rectangle(x, y, width - pencil - 4, h);
		bool customReady = !string.IsNullOrWhiteSpace(CustomCommand);
		string label = string.IsNullOrWhiteSpace(CustomLabel) ? "Custom" : CustomLabel;
		if (HubKit.Button(sb, "view:custom", custom, label, color, customReady ? (IsaClient.Languages.L10n.T("Writes in the chat:") + " " + Fill(CustomCommand, live)) : "Set what it writes in the chat with the pencil.", customReady, HubIcon.Item(I.Megaphone)))
		{
			Run(CustomCommand, live);
		}
		if (HubKit.IconButton(sb, "view:customedit", new Rectangle(custom.Right + 4, y, pencil, h), HubIcon.Texture("Images/UI/ButtonRename"), "Change the custom button", _editing == "custom"))
		{
			ToggleEditing("custom");
		}
		y += h + 6;
		if (_editing == "custom")
		{
			y = EditField(sb, "view:customlabel", x, y, width, ref CustomLabel, "Button name");
			y = EditField(sb, "view:customfield", x, y, width, ref CustomCommand, "/tp <player>  or  .heal <id>");
		}
		return y;
	}

	private static void ToggleEditing(string which)
	{
		bool closing = _editing == which;
		if (_editing != null)
		{
			ClientSettings.Save();
		}
		_editing = closing ? null : which;
		if (_editing != null)
		{
			HubInput.SetFocus("view:" + which + "field");
		}
	}

	private static int EditField(SpriteBatch sb, string id, int x, int y, int width, ref string value, string placeholder)
	{
		value = HubKit.Field(sb, id, new Rectangle(x, y, width, 28), value, placeholder, 200);
		y += 32;
		return y;
	}

	/// <summary>Placeholders: &lt;player&gt;, &lt;jugador&gt;, &lt;name&gt; (the name) and &lt;id&gt; (their slot).</summary>
	private static string Fill(string template, Player player)
	{
		string name = player.name ?? "";
		return (template ?? "").Replace("<player>", name).Replace("<jugador>", name).Replace("<name>", name).Replace("<id>", player.whoAmI.ToString());
	}

	/// <summary>Writes the line in the chat as if typed: client commands run, the rest (server commands too) is sent.</summary>
	private static void Run(string template, Player player)
	{
		string line = Fill(template, player).Trim();
		if (line.Length == 0)
		{
			return;
		}
		if (_editing != null)
		{
			_editing = null;
			ClientSettings.Save();
		}
		SoundEngine.PlaySound(12);
		Main.SubmitChatLine(line);
	}

	#endregion

	/// <summary>Save .plr asks for a second click within this time.</summary>
	private static double _saveArmedUntil;

	private static void SpectateThem(bool withInventory)
	{
		if (Spectate.SpectatePlayerTarget == Index)
		{
			Spectate.SpectatePlayerTarget = -1;
			Spectate.ShowSpectatedInventory = false;
			SoundEngine.PlaySound(12);
		}
		else if (Owner.active && Spectate.IsInsideWorld(Owner))
		{
			Spectate.ToggleSpectate(Index, withInventory);
			SoundEngine.PlaySound(12);
		}
	}

	#endregion

	#region Items

	private static int DrawItems(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		Player owner = Owner;
		Player view = View;
		// Ten columns, then coins and ammo after a small gap.
		_slot = (int)MathHelper.Clamp((width - 12 * 4 - 10) / 12f, 28f, 46f);
		int step = _slot + 4;
		Main.inventoryScale = _slot / 52f;

		// Storage tabs
		for (int i = 0; i < InventoryViewer._storageIcons.Length; i++)
		{
			Rectangle tab = new Rectangle(x + i * 40, y, 36, 34);
			bool selected = InventoryViewer.Storage == i;
			bool received = StorageSync.Received(Index, i);
			string tip = InventoryViewer._storageNames[i] + ((!received) ? "\nNot received from the server: may be empty or old." : "");
			if (HubKit.IconButton(sb, "view:storage:" + i, tab, HubIcon.Item(InventoryViewer._storageIcons[i]), tip, selected))
			{
				InventoryViewer.Storage = i;
			}
			if (!received)
			{
				HubKit.Fill(sb, new Rectangle(tab.Right - 9, tab.Y + 3, 6, 6), Theme.Muted * 0.8f);
			}
		}
		int labelX = x + InventoryViewer._storageIcons.Length * 40 + 6;
		HubKit.Text(sb, InventoryViewer._storageNames[InventoryViewer.Storage], labelX, y + 2, Color.White, 0.86f);
		if (InventoryViewer.Storage > 0 && Index != Main.myPlayer)
		{
			bool received = StorageSync.Received(Index, InventoryViewer.Storage);
			HubKit.Text(sb, received ? "Sent by the server" : "Not received from the server", labelX, y + 20, received ? (Theme.On * 0.9f) : Theme.Muted, 0.62f);
		}
		y += 42;

		// Grid
		Item[] storage = StorageItems(owner);
		int gridY = y;
		ItemSlot.IsaDrawPlayer = owner;
		if (storage != null)
		{
			for (int slot = 0; slot < 40 && slot < storage.Length; slot++)
			{
				DrawSlot(sb, storage, 3, 3, slot, x + slot % 10 * step, gridY + slot / 10 * step);
			}
		}
		else
		{
			for (int slot = 0; slot < 50; slot++)
			{
				DrawSlot(sb, view.inventory, (owner.selectedItem != slot) ? 1 : 13, 0, slot, x + slot % 10 * step, gridY + slot / 10 * step);
			}
		}
		// Coins and ammo, the held item and the trash under them.
		int coinsX = x + 10 * step + 10;
		int ammoX = coinsX + step;
		HubKit.Text(sb, Lang.inter[26].Value, coinsX, gridY - 14, Theme.Muted, 0.55f, _slot + 4);
		HubKit.Text(sb, Lang.inter[27].Value, ammoX, gridY - 14, Theme.Muted, 0.55f, _slot + 4);
		for (int k = 0; k < 4; k++)
		{
			DrawSlot(sb, view.inventory, 1, 1, 50 + k, coinsX, gridY + k * step);
			DrawSlot(sb, view.inventory, 2, 2, 54 + k, ammoX, gridY + k * step);
		}
		int lastRow = gridY + 4 * step;
		if (owner.selectedItem == 58 || !view.inventory[58].IsAir)
		{
			DrawSlot(sb, view.inventory, 13, 0, 58, coinsX, lastRow);
		}
		DrawSlot(sb, new Item[1] { view.trashItem }, 6, 6, 0, ammoX, lastRow);
		y = gridY + 5 * step + 10;

		// Equipment, drawn as the viewed player (loadout colors, shared loadout items).
		ItemSlot.IsaDrawPlayer = view;
		y = DrawEquipment(sb, owner, view, x, y, width, step);

		// Buffs
		y = DrawBuffs(sb, owner, x, y, width);
		return y - start;
	}

	private static Item[] StorageItems(Player owner)
	{
		switch (InventoryViewer.Storage)
		{
		case 1:
			return owner.bank.item;
		case 2:
			return owner.bank2.item;
		case 3:
			return owner.bank3.item;
		case 4:
			return owner.bank4.item;
		default:
			return null;
		}
	}

	private static int DrawEquipment(SpriteBatch sb, Player owner, Player view, int x, int y, int width, int step)
	{
		HubKit.Line(sb, x, y, width, Theme.Slot * 0.8f);
		y += 8;
		HubKit.Text(sb, "Equipment", x, y, Color.White, 0.86f);
		// Loadouts on the right of the title: click one to preview it, the worn one to go back.
		Texture2D loadoutTex = TextureAssets.Extra[259].Value;
		for (int li = 0; li < owner.Loadouts.Length; li++)
		{
			Rectangle button = new Rectangle(x + width - (owner.Loadouts.Length - li) * 36, y - 4, 32, 32);
			int frameX = (li == view.CurrentLoadoutIndex) ? 1 : 0;
			Rectangle frame = loadoutTex.Frame(3, 3, frameX, li);
			sb.Draw(loadoutTex, button.Center.ToVector2(), frame, Color.White, 0f, frame.Size() / 2f, 1f, SpriteEffects.None, 0f);
			if (HubInput.In(button))
			{
				Rectangle glow = loadoutTex.Frame(3, 3, 2, li);
				sb.Draw(loadoutTex, button.Center.ToVector2(), glow, Main.OurFavoriteColor, 0f, glow.Size() / 2f, 1f, SpriteEffects.None, 0f);
				string tip = Language.GetTextValue("UI.Loadout" + (li + 1)) + IsaClient.Languages.L10n.T((li == owner.CurrentLoadoutIndex) ? " (worn)" : " (click to preview)");
				if (HubInput.Hover("view:loadout:" + li, tip))
				{
					InventoryViewer.TemporaryLoadoutIndex = (li == owner.CurrentLoadoutIndex) ? (-1) : li;
					SoundEngine.PlaySound(12);
					InventoryViewer.CloneFromIndex();
				}
			}
		}
		y += 30;
		bool demonHeart = view.CanDemonHeartAccessoryBeShown();
		bool masterSlot = view.CanMasterModeAccessoryBeShown();
		int labelW = 0;
		// Rows of ten: armor and accessories, vanity, dyes. Slots that can't be used are dimmed.
		y = EquipRow(sb, "Armor & accessories", x, y, step, labelW, m => DrawEquip(sb, view.armor, (m > 2) ? 10 : 8, m, !view.IsItemSlotUnlockedAndUsable(m), x + m * step, 0, (m > 2) ? (bool?)view.hideVisibleAccessory[m] : null), demonHeart, masterSlot);
		y = EquipRow(sb, "Vanity", x, y, step, labelW, m => DrawEquip(sb, view.armor, (m > 2) ? 11 : 9, m + 10, !view.IsItemSlotUnlockedAndUsable(m + 10), x + m * step, 0, null), demonHeart, masterSlot);
		y = EquipRow(sb, "Dyes", x, y, step, labelW, m => DrawEquip(sb, view.dye, 12, m, !view.IsItemSlotUnlockedAndUsable(m), x + m * step, 0, null), demonHeart, masterSlot);
		// Pet, light pet, minecart, mount, hook and their dyes, in one row.
		HubKit.Text(sb, "Pet, light pet, minecart, mount, hook  ·  their dyes", x, y, Theme.Muted, 0.62f, width);
		y += 15;
		_rowY = y;
		for (int slot = 0; slot < 5; slot++)
		{
			int context = slot switch
			{
				0 => 19,
				1 => 20,
				2 => 18,
				3 => 17,
				_ => 16,
			};
			DrawEquip(sb, view.miscEquips, context, slot, false, x + slot * step, 0, (slot < 2) ? (bool?)view.hideMisc[slot] : null);
			if (slot == 2 && view.unlockedSuperCart)
			{
				Texture2D cart = view.enabledSuperCart ? TextureAssets.Extra[255].Value : TextureAssets.Extra[256].Value;
				sb.Draw(cart, new Vector2(x + slot * step + _slot - cart.Width * 0.6f, y - 2f), null, Color.White, 0f, Vector2.Zero, 0.85f, SpriteEffects.None, 0f);
			}
			DrawEquip(sb, view.miscDyes, 33, slot, false, x + (5 + slot) * step + 10, 0, null);
		}
		y += step + 8;
		return y;
	}

	private static int _rowY;

	private static int EquipRow(SpriteBatch sb, string label, int x, int y, int step, int labelW, Action<int> draw, bool demonHeart, bool masterSlot)
	{
		HubKit.Text(sb, label, x, y, Theme.Muted, 0.62f);
		y += 15;
		_rowY = y;
		for (int m = 0; m < 10; m++)
		{
			if ((m == 8 && !demonHeart) || (m == 9 && !masterSlot))
			{
				continue;
			}
			draw(m);
		}
		return y + step + 4;
	}

	/// <summary>An equipment slot on the current row; <paramref name="hidden"/> draws the eye toggle (read-only).</summary>
	private static void DrawEquip(SpriteBatch sb, Item[] items, int context, int slot, bool locked, int x, int dy, bool? hidden)
	{
		int y = _rowY + dy;
		DrawSlot(sb, items, context, context, slot, x, y, locked, armor: true);
		if (hidden.HasValue)
		{
			Texture2D tick = hidden.Value ? TextureAssets.InventoryTickOff.Value : TextureAssets.InventoryTickOn.Value;
			Vector2 at = new Vector2(x + _slot - tick.Width * 0.7f, y - 3f);
			sb.Draw(tick, at, null, Color.White * 0.8f, 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 0f);
			Rectangle r = new Rectangle((int)at.X, (int)at.Y, (int)(tick.Width * 0.8f), (int)(tick.Height * 0.8f));
			if (HubInput.In(r))
			{
				HubInput.Hover("view:tick:" + context + ":" + slot, Lang.inter[hidden.Value ? 60 : 59].Value);
			}
		}
	}

	/// <summary>An item slot: drawn like the game's, read-only. Shift+click copies it to you, double click spawns it on you.</summary>
	private static void DrawSlot(SpriteBatch sb, Item[] items, int drawContext, int hoverContext, int slot, int x, int y, bool locked = false, bool armor = false)
	{
		if (items == null || slot >= items.Length || items[slot] == null)
		{
			return;
		}
		Rectangle r = new Rectangle(x, y, _slot, _slot);
		if (!HubInput.Visible(r))
		{
			return;
		}
		Color back = Main.inventoryBack;
		if (locked)
		{
			Main.inventoryBack = LockedBack;
		}
		ItemSlot.Draw(sb, items, drawContext, slot, new Vector2(x, y));
		Main.inventoryBack = back;
		if (!HubInput.In(r))
		{
			return;
		}
		ItemSlot.OverrideHoverFavoriteAndShift(items, hoverContext, slot);
		if (Main.cursorOverride == 2)
		{
			ItemSlot.LeftClickShareInChat(items[slot]);
		}
		if (Index != Main.myPlayer)
		{
			ItemSlot.LeftClickSteal(items, slot);
		}
		InventoryViewer.CheckDoubleClickSpawn(items, slot);
		HubInput.Hover("view:slot:" + hoverContext + ":" + slot);
		_hoverItems = items;
		_hoverContext = hoverContext;
		_hoverIndex = slot;
		_hoverArmor = armor;
	}

	private static int DrawBuffs(SpriteBatch sb, Player owner, int x, int y, int width)
	{
		int count = 0;
		for (int b = 0; b < Player.maxBuffs; b++)
		{
			if (owner.buffType[b] > 0)
			{
				count++;
			}
		}
		if (count == 0)
		{
			return y;
		}
		HubKit.Line(sb, x, y, width, Theme.Slot * 0.8f);
		y += 8;
		HubKit.Text(sb, "Buffs", x, y, Color.White, 0.86f);
		y += 24;
		int perRow = Math.Max(1, width / 38);
		int shown = 0;
		bool timers = Index == Main.myPlayer;
		for (int b = 0; b < Player.maxBuffs; b++)
		{
			int type = owner.buffType[b];
			if (type <= 0)
			{
				continue;
			}
			Rectangle r = new Rectangle(x + shown % perRow * 38, y + shown / perRow * 46, 32, 32);
			shown++;
			if (!HubInput.Visible(r))
			{
				continue;
			}
			Texture2D texture = TextureAssets.Buff[type].Value;
			sb.Draw(texture, r, Color.White);
			// Only your own timers are real: the server doesn't send other players' buff times.
			int time = owner.buffTime[b];
			if (timers && time > 2 && !Main.vanityPet[type] && !Main.lightPet[type] && !Main.buffNoTimeDisplay[type])
			{
				string text = Lang.LocalizedDuration(new TimeSpan(0, 0, time / 60), abbreviated: true, showAllAvailableUnits: false);
				HubKit.RawText(sb, text, r.X, r.Bottom, Theme.Muted, 0.55f);
			}
			if (HubInput.In(r))
			{
				HubInput.Hover("view:buff:" + b);
				_hoverBuff = b;
			}
		}
		return y + (shown + perRow - 1) / perRow * 46;
	}

	#endregion

	/// <summary>
	/// After the panel's batch, back in the game's UI space: the game's tooltip of the item or buff under the mouse.
	/// Returns true when one was shown (the panel's own hover text is skipped then).
	/// </summary>
	public static bool DrawHoverTooltip()
	{
		bool shown = false;
		if (_hoverItems != null)
		{
			Main.instance.MouseText("", 0, 0);
			if (_hoverArmor)
			{
				Main.armorHide = true;
			}
			ItemSlot.MouseHover(_hoverItems, _hoverContext, _hoverIndex);
			shown = true;
		}
		else if (_hoverBuff >= 0 && Target >= 0)
		{
			Player owner = Owner;
			int type = owner.buffType[_hoverBuff];
			if (type > 0)
			{
				string name = Lang.GetBuffName(type) + (InfoOverlays.ShowIDs ? (" (ID: " + type + ")") : "");
				string tooltip = Main.GetBuffTooltip(owner, type);
				Main.instance.MouseText("", 0, 0);
				if (type == 147)
				{
					Main.bannerMouseOver = true;
				}
				if (Main.meleeBuff[type])
				{
					Main.instance.MouseTextHackZoom(name, -10, 0, tooltip);
				}
				else
				{
					Main.instance.MouseTextHackZoom(name, tooltip);
				}
				shown = true;
			}
		}
		_hoverItems = null;
		_hoverBuff = -1;
		_hoverArmor = false;
		return shown;
	}
}
