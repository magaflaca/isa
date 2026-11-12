using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Menu;
using IsaClient.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>The Home page: a welcome card, quick buttons, favorites, what is on, your buttons, recent features and every section.</summary>
internal static class HubHome
{
	private static HubTheme Theme => HubTheme.Current;

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y += Welcome(sb, x, y, width);
		y += QuickButtons(sb, x, y, width);

		List<HubFeature> favorites = HubPrefs.Favorites.Select(HubCatalog.Find).Where(f => f != null).ToList();
		y += Heading(sb, "Favorites", HubIcon.Texture("Images/UI/ButtonFavoriteActive"), x, y, width, favorites.Count > 0 ? favorites.Count.ToString() : null);
		if (favorites.Count == 0)
		{
			y += ClientHub.EmptyNote(sb, x, y, width, HubIcon.Texture("Images/UI/ButtonFavoriteInactive"), "No favorites yet", "Click the star next to any feature (or right click it) to pin it here.");
		}
		else
		{
			y += ClientHub.DrawCards(sb, favorites, x, y, width, null);
		}

		List<HubFeature> active = ClientHub.ActiveFeatures().ToList();
		y += Heading(sb, "On right now", HubIcon.Item(I.UltrabrightTorch), x, y, width, active.Count > 0 ? active.Count.ToString() : null);
		if (active.Count == 0)
		{
			y += ClientHub.EmptyNote(sb, x, y, width, HubIcon.Item(I.Torch), "Nothing is on", "Everything you turn on shows up here, so you can turn it off quickly.");
		}
		else
		{
			y += ClientHub.DrawCards(sb, active, x, y, width, null);
		}

		y += Heading(sb, "My buttons", HubIcon.Item(I.Toolbox), x, y, width, null);
		y += HubToolkit.DrawActionTiles(sb, x, y, width);

		List<HubFeature> recent = HubPrefs.Recent.Select(HubCatalog.Find).Where(f => f != null && !favorites.Contains(f)).Take(6).ToList();
		if (recent.Count > 0)
		{
			y += Heading(sb, "Recently used", HubIcon.Item(I.Stopwatch), x, y, width, null);
			y += ClientHub.DrawCards(sb, recent, x, y, width, null);
		}

		y += Heading(sb, "Explore", HubIcon.Item(I.WorldGlobe), x, y, width, null);
		y += SectionTiles(sb, x, y, width);
		return y - start;
	}

	private static int Heading(SpriteBatch sb, string title, HubIcon icon, int x, int y, int width, string count)
	{
		icon.Draw(sb, new Rectangle(x + 2, y + 2, 22, 22), Color.White);
		HubKit.Text(sb, title, x + 30, y + 4, Theme.Accent, 0.92f);
		float w = HubKit.Measure(title, 0.92f);
		if (count != null)
		{
			Rectangle chip = HubKit.Chip(sb, count, (int)(x + 38 + w), y + 3, Theme.Card, Color.White, 0.66f);
			w += chip.Width + 8;
		}
		HubKit.Line(sb, (int)(x + 40 + w), y + 14, Math.Max(0, (int)(width - 44 - w)), Theme.Muted * 0.15f);
		return 32;
	}

	private static int Welcome(SpriteBatch sb, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 92);
		HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.Slot, 0.35f) * HubPrefs.Opacity);
		// A soft moving shine across the card.
		float t = (float)(Main.GlobalTimeWrappedHourly * 0.25 % 1.0);
		int shineX = r.X + (int)((r.Width + 200) * t) - 100;
		for (int i = 0; i < 60; i++)
		{
			int sx = shineX + i;
			if (sx > r.X + 6 && sx < r.Right - 6)
			{
				HubKit.Fill(sb, new Rectangle(sx, r.Y + 4, 1, r.Height - 8), Color.White * (0.035f * (1f - Math.Abs(i - 30) / 30f)));
			}
		}
		Player player = Main.LocalPlayer;
		Rectangle head = new Rectangle(r.X + 12, r.Y + 12, 68, 68);
		HubKit.Box(sb, head, Theme.Sidebar * (HubPrefs.Opacity * 0.6f));
		if (player != null && player.active)
		{
			HubKit.Head(sb, player, new Vector2(head.Center.X, head.Center.Y), 1.9f);
		}
		string name = (player != null && !string.IsNullOrEmpty(player.name)) ? player.name : "adventurer";
		HubKit.Title(sb, "Welcome back, " + name + "!", head.Right + 14, r.Y + 12, width - 260, 0.62f, Color.White);
		HubKit.Text(sb, WhereText(), head.Right + 16, r.Y + 50, Theme.Muted, 0.76f, width - 280);
		// Stats on the right
		int active = ClientHub.ActiveFeatures().Count();
		int sx2 = r.Right - 12;
		sx2 = Stat(sb, sx2, r.Y + 16, active.ToString(), "on", Theme.On);
		sx2 = Stat(sb, sx2 - 8, r.Y + 16, HubPrefs.Favorites.Count.ToString(), "favorites", Theme.Accent);
		return r.Height + 10;
	}

	private static int Stat(SpriteBatch sb, int right, int y, string number, string label, Color color)
	{
		float w = Math.Max(HubKit.Measure(label, 0.68f), HubKit.BigFont.MeasureString(IsaClient.Languages.L10n.T(number)).X * 0.6f) + 20f;
		Rectangle r = new Rectangle((int)(right - w), y, (int)w, 60);
		HubKit.Box(sb, r, Theme.Sidebar * HubPrefs.Opacity);
		float nw = HubKit.BigFont.MeasureString(IsaClient.Languages.L10n.T(number)).X * 0.6f;
		Utils.DrawBorderStringBig(sb, IsaClient.Languages.L10n.T(number), new Vector2(r.X + (r.Width - nw) / 2f, r.Y + 6), color, 0.6f);
		float lw = HubKit.Measure(label, 0.68f);
		HubKit.Text(sb, label, r.X + (r.Width - lw) / 2f, r.Y + 38, Theme.Muted, 0.68f);
		return r.X;
	}

	private static string WhereText()
	{
		if (Main.netMode == 0)
		{
			return "Single player  ·  " + Main.worldName;
		}
		int players = Main.player.Count(p => p != null && p.active);
		string server = ServerDetection.OnTshockServer ? "TShock server" : "Server";
		return server + "  ·  " + Main.worldName + "  ·  " + players + (players == 1 ? " player" : " players") + " online";
	}

	private static int QuickButtons(SpriteBatch sb, int x, int y, int width)
	{
		int count = 5;
		int gap = 8;
		int w = (width - gap * (count - 1)) / count;
		int h = 34;
		bool confirm = ClientHub.Confirming(_allOff);
		if (HubKit.Button(sb, "home:alloff", new Rectangle(x, y, w, h), confirm ? "Click again" : "Turn all off", confirm ? (Theme.Danger * 0.85f) : Theme.Card, "Turns off every feature that is on.", ClientHub.ActiveFeatures().Any(), HubIcon.Item(I.Torch)))
		{
			ClientHub.Activate(_allOff);
		}
		if (HubKit.Button(sb, "home:music", new Rectangle(x + (w + gap), y, w, h), "Music player", Theme.Card, "Opens the music window.", true, HubIcon.Item(I.MusicBoxOverworldDay)))
		{
			IsaClient.Music.MusicPlayerWindow.ToggleWindow();
		}
		if (HubKit.Button(sb, "home:character", new Rectangle(x + (w + gap) * 2, y, w, h), "My character", Theme.Card, "Your character: inventory, buffs, looks and saved characters.", true, HubIcon.Item(I.Mannequin)))
		{
			CharacterWindow.Open();
		}
		if (HubKit.Button(sb, "home:buffs", new Rectangle(x + (w + gap) * 3, y, w, h), "Buffs", Theme.Card, "Give yourself any buff.", !Main.gameMenu, HubIcon.Item(I.WrathPotion)))
		{
			CharacterWindow.OpenBuffs();
		}
		if (HubKit.Button(sb, "home:new", new Rectangle(x + (w + gap) * 4, y, w, h), "New button", Theme.Card, "Make your own button that runs commands.", true, HubIcon.Item(I.Toolbox)))
		{
			HubToolkit.StartNewAction();
			ClientHub.OpenSection("toolkit");
		}
		return h + 14;
	}

	private static readonly HubFeature _allOff = new HubFeature
	{
		Id = "hub:alloff",
		Title = "Turn all off",
		NativeKind = FeatureKind.Action,
		Confirm = true,
		Cheat = false,
		Run = ClientHub.TurnAllOff
	};

	private static int SectionTiles(SpriteBatch sb, int x, int y, int width)
	{
		List<HubSection> sections = HubCatalog.Sections.Where(s => !s.Special && !HubPrefs.HiddenSections.Contains(s.Id)).ToList();
		int columns = Math.Max(2, width / 190);
		int gap = 8;
		int w = (width - gap * (columns - 1)) / columns;
		int h = 64;
		int start = y;
		for (int i = 0; i < sections.Count; i++)
		{
			HubSection section = sections[i];
			Rectangle r = new Rectangle(x + i % columns * (w + gap), y + i / columns * (h + gap), w, h);
			if (!HubInput.Visible(r))
			{
				continue;
			}
			string id = "tile:" + section.Id;
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover(id, section.Subtitle))
			{
				ClientHub.OpenSection(section.Id);
			}
			float glow = HubInput.Anim(id, hover);
			HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.CardHover, glow) * HubPrefs.Opacity);
			HubKit.Fill(sb, new Rectangle(r.X + 8, r.Bottom - 6, (int)((r.Width - 16) * (0.25f + 0.75f * glow)), 2), section.Accent * (0.5f + 0.5f * glow));
			int iconSize = 34;
			float lift = glow * 3f;
			section.Icon.Draw(sb, new Rectangle(r.X + 10, (int)(r.Y + (h - iconSize) / 2 - lift), iconSize, iconSize), Color.White);
			HubKit.Text(sb, section.Title, r.X + 52, r.Y + 13, Color.White, 0.84f, r.Width - 60);
			int count = section.Features.Count();
			int on = section.ActiveCount;
			HubKit.Text(sb, count + " features" + (on > 0 ? ("  ·  " + on + " on") : ""), r.X + 52, r.Y + 35, on > 0 ? (Theme.On * 0.95f) : Theme.Muted, 0.68f, r.Width - 60);
		}
		int rows = (sections.Count + columns - 1) / columns;
		return rows * (h + gap) + 6;
	}
}
