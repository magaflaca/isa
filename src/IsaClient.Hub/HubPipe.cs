using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Games;
using IsaClient.Net.Pipe;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// The pipe (data between IsaClient players, hidden in map pings): whether it's on, and a switch. Turning it off asks
/// first, saying what stops working and what goes another way (the same list .pipe off shows).
/// </summary>
public static class HubPipe
{
	private static HubTheme Theme => HubTheme.Current;

	private static readonly Color Red = new Color(150, 58, 58);

	private static readonly Color Green = new Color(60, 140, 80);

	private static readonly Color Warning = new Color(255, 200, 90);

	/// <summary>"Turn off" was clicked: the warning shows until it's confirmed or cancelled.</summary>
	private static bool _asking;

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		bool on = PipeNet.Enabled;
		if (!on)
		{
			_asking = false;
		}
		// Its state, with the switch.
		Rectangle card = new Rectangle(x, y, width, 66);
		HubKit.Box(sb, card, Theme.Card * HubPrefs.Opacity);
		Rectangle picture = new Rectangle(card.X + 8, card.Y + 8, 50, 50);
		HubKit.Fill(sb, picture, Color.Black * 0.22f);
		HubIcon.Item(I.Wire).Draw(sb, new Rectangle(picture.Center.X - 16, picture.Center.Y - 16, 32, 32), on ? Color.White : Color.Gray, 1.8f);
		int textX = picture.Right + 12;
		HubKit.Text(sb, "IsaClient Pipe", textX, card.Y + 12, Color.White, 0.85f);
		HubKit.Chip(sb, on ? "ON" : "OFF", textX + (int)HubKit.Measure(IsaClient.Languages.L10n.T("IsaClient Pipe"), 0.85f) + 10, card.Y + 11, on ? Green : Red, Color.White, 0.66f);
		HubKit.Text(sb, Status(on), textX, card.Y + 38, Theme.Muted, 0.68f, card.Right - 150 - textX);
		Rectangle button = new Rectangle(card.Right - 132, card.Center.Y - 16, 120, 32);
		if (on)
		{
			if (HubKit.Button(sb, "pipe:off", button, "Turn off...", Red, "Stops everything that goes between IsaClient players (it asks first).", !_asking))
			{
				_asking = true;
			}
		}
		else if (HubKit.Button(sb, "pipe:on", button, "Turn on", Green, "Games, the IsaClient chat and sharing through the pipe work again."))
		{
			IsaClient.Net.Pipe.PipeCommands.SetEnabled(true);
		}
		y = card.Bottom + 10;
		if (_asking)
		{
			return Ask(sb, x, y, width) - start;
		}
		y = Note(sb, on ? "The pipe carries data between IsaClient players hidden in map pings, so the server needs no mods: games, the IsaClient chat, pictures only IsaClient players see, IsaClient items and shared characters and schematics. It's slow (about 1 KB a second) and only reaches IsaClient players on the same server." : "With the pipe off nothing goes out through it, but it still listens: what others send through it reaches you. Pictures, GIFs, characters, styles and schematics still go, in the server's chat as short codes (characters and schematics through archive.org).", x, y, width);
		return y - start;
	}

	private static string Status(bool on)
	{
		if (!on)
		{
			return (Main.netMode == 1 && PipeNet.Ready) ? "Off: listening only, nothing goes out." : "Off: nothing goes out (it still listens on a server).";
		}
		if (Main.netMode != 1 || !PipeNet.Ready)
		{
			return "On. It connects when you join a server.";
		}
		int others = GameLobby.Players().Count();
		return (others == 1) ? "On and connected: 1 IsaClient player here." : IsaClient.Languages.L10n.F("On and connected: {0} IsaClient players here.", others);
	}

	/// <summary>The warning, with what stops and what changes, and the buttons to confirm or not.</summary>
	private static int Ask(SpriteBatch sb, int x, int y, int width)
	{
		const float scale = 0.72f;
		int textWidth = width - 52;
		List<string[]> stops = PipeCommands.OffStops.Select(l => HubKit.Wrap(l, textWidth, scale, 4)).ToList();
		List<string[]> changes = PipeCommands.OffChanges.Select(l => HubKit.Wrap(l, textWidth, scale, 4)).ToList();
		int lines = stops.Sum(l => l.Length) + changes.Sum(l => l.Length);
		int height = 40 + 26 + 26 + lines * 18 + (stops.Count + changes.Count) * 4 + 16 + 46;
		Rectangle card = new Rectangle(x, y, width, height);
		HubKit.Box(sb, card, Theme.Card * HubPrefs.Opacity);
		HubKit.Fill(sb, new Rectangle(card.X, card.Y, 3, card.Height), Warning);
		HubKit.Text(sb, "Turn the pipe off?", card.X + 18, card.Y + 12, Warning, 0.9f);
		int ty = card.Y + 40;
		ty = Bullets(sb, "What stops working", stops, Theme.Danger, card.X + 18, ty, scale);
		ty = Bullets(sb, "What still works, another way", changes, Theme.On, card.X + 18, ty + 4, scale);
		int bw = (width - 36 - 10) / 2;
		Rectangle yes = new Rectangle(card.X + 18, card.Bottom - 44, bw, 32);
		Rectangle no = new Rectangle(yes.Right + 10, yes.Y, bw, 32);
		if (HubKit.Button(sb, "pipe:sure", yes, "Yes, turn it off", Red, "You can turn it on again here or with .pipe on."))
		{
			_asking = false;
			PipeCommands.SetEnabled(false);
		}
		if (HubKit.Button(sb, "pipe:cancel", no, "Cancel", Theme.Slot))
		{
			_asking = false;
		}
		return card.Bottom + 10;
	}

	private static int Bullets(SpriteBatch sb, string title, List<string[]> items, Color dot, int x, int y, float scale)
	{
		HubKit.Text(sb, title, x, y, Theme.Muted, 0.72f);
		y += 26;
		foreach (string[] item in items)
		{
			HubKit.Fill(sb, new Rectangle(x + 4, y + 6, 6, 6), dot);
			HubKit.RawText(sb, string.Join("\n", item), x + 18, y, Color.White, scale);
			y += item.Length * 18 + 4;
		}
		return y;
	}

	private static int Note(SpriteBatch sb, string text, int x, int y, int width)
	{
		string[] lines = HubKit.Wrap(text, width - 28, 0.7f, 8);
		Rectangle r = new Rectangle(x, y, width, 18 + lines.Length * 18);
		HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
		HubKit.RawText(sb, string.Join("\n", lines), r.X + 14, r.Y + 9, Theme.Muted, 0.7f);
		return r.Bottom;
	}
}
