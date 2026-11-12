using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IsaClient.Building;
using IsaClient.Sharing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.IO;

namespace IsaClient.Hub;

/// <summary>
/// Sharing: characters, character styles and schematics to link in chat, and what came from others. Every row has
/// the same shape: a picture (head, map or item), a name with a line under it, and its buttons on the right.
/// </summary>
public static class HubShare
{
	private static HubTheme Theme => HubTheme.Current;

	private const int RowH = 50;

	private const int Gap = 6;

	private static readonly Color Send = new Color(40, 110, 100);

	private static readonly Color Green = new Color(60, 140, 80);

	private static readonly Color Red = new Color(150, 58, 58);

	/// <summary>The Grand Design, for what's copied in world edit.</summary>
	private const int CopiedIcon = 3611;

	private static List<string> _schematicFiles;

	private static string _search = "";

	private static DateTime _schematicFilesAt;

	#region Pieces

	private static int Note(SpriteBatch sb, string text, int x, int y, int width)
	{
		string[] lines = HubKit.Wrap(text, width - 28, 0.7f, 6);
		Rectangle r = new Rectangle(x, y, width, 18 + lines.Length * 18);
		HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
		HubKit.RawText(sb, string.Join("\n", lines), r.X + 14, r.Y + 9, Theme.Muted, 0.7f);
		return r.Bottom;
	}

	private static int Heading(SpriteBatch sb, string text, int x, int y, int count = -1)
	{
		HubKit.Text(sb, text, x, y, Theme.Muted, 0.72f);
		if (count >= 0)
		{
			HubKit.Chip(sb, count.ToString(), x + (int)HubKit.Measure(IsaClient.Languages.L10n.T(text), 0.72f) + 8, y - 2, Color.Lerp(Theme.Slot, Theme.Accent, 0.3f), Color.White, 0.66f);
		}
		return y + 22;
	}

	/// <summary>A row's box and picture slot; returns where its text starts.</summary>
	private static Rectangle Row(SpriteBatch sb, int x, int y, int width, out Rectangle picture, int pictureWidth = 38)
	{
		Rectangle r = new Rectangle(x, y, width, RowH);
		HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
		picture = new Rectangle(r.X + 6, r.Y + 6, pictureWidth, RowH - 12);
		HubKit.Fill(sb, picture, Color.Black * 0.22f);
		return r;
	}

	private static void Lines(SpriteBatch sb, Rectangle row, int textX, string title, string under, int buttonsWidth, Color? underColor = null)
	{
		float max = row.Right - buttonsWidth - 14 - textX;
		HubKit.RawText(sb, title, textX, row.Y + 7, Color.White, 0.8f, max);
		if (!string.IsNullOrEmpty(under))
		{
			if (under.Contains("[c/"))
			{
				HubKit.RichText(sb, under, textX, row.Y + 28, underColor ?? Theme.Muted, 0.66f);
			}
			else
			{
				HubKit.RawText(sb, under, textX, row.Y + 28, underColor ?? Theme.Muted, 0.66f, max);
			}
		}
	}

	/// <summary>Buttons right-aligned in a row (first = rightmost). Returns which one was clicked (-1: none).</summary>
	private static int Buttons(SpriteBatch sb, string id, Rectangle row, params (string Label, Color Color, string Tip, bool Enabled)[] buttons)
	{
		int clicked = -1;
		int right = row.Right - 8;
		for (int i = 0; i < buttons.Length; i++)
		{
			int w = Math.Max(78, (int)HubKit.Measure(IsaClient.Languages.L10n.T(buttons[i].Label), 0.78f) + 26);
			Rectangle b = new Rectangle(right - w, row.Y + 10, w, 30);
			if (HubKit.Button(sb, id + ":" + i, b, buttons[i].Label, buttons[i].Color, buttons[i].Tip, buttons[i].Enabled))
			{
				clicked = i;
			}
			right = b.X - 6;
		}
		return clicked;
	}

	private static int ButtonsWidth(params string[] labels)
	{
		return labels.Sum(l => Math.Max(78, (int)HubKit.Measure(IsaClient.Languages.L10n.T(l), 0.78f) + 26) + 6) + 8;
	}

	private static void Item(SpriteBatch sb, int type, Rectangle box)
	{
		HubIcon.Item(type).Draw(sb, new Rectangle(box.Center.X - 14, box.Center.Y - 14, 28, 28), Color.White, 1.5f);
	}

	private static void Map(SpriteBatch sb, SchematicShare.Entry e, Rectangle box)
	{
		Texture2D preview = SchematicShare.PreviewOf(e);
		if (preview == null)
		{
			Item(sb, ShareKinds.Item(ShareKind.Schematic), box);
			return;
		}
		float fit = Math.Min((float)(box.Width - 4) / preview.Width, (float)(box.Height - 4) / preview.Height);
		int w = Math.Max(1, (int)(preview.Width * fit));
		int h = Math.Max(1, (int)(preview.Height * fit));
		sb.Draw(preview, new Rectangle(box.Center.X - w / 2, box.Center.Y - h / 2, w, h), Color.White);
	}

	private static void Bar(SpriteBatch sb, Rectangle row, int textX, float fraction, int buttonsWidth)
	{
		Rectangle track = new Rectangle(textX, row.Bottom - 9, Math.Max(40, row.Right - buttonsWidth - 14 - textX), 3);
		HubKit.Fill(sb, track, Color.Black * 0.4f);
		HubKit.Fill(sb, new Rectangle(track.X, track.Y, Math.Max(2, (int)(track.Width * MathHelper.Clamp(fraction, 0f, 1f))), track.Height), new Color(120, 230, 140));
	}

	private static bool InWorld(SpriteBatch sb, int x, ref int y, int width)
	{
		if (!Main.gameMenu)
		{
			return true;
		}
		y = Note(sb, "Join a world to link things in chat.", x, y, width) + 10;
		return false;
	}

	#endregion

	#region Characters

	public static int DrawCharacters(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y = Note(sb, "Link a character in chat and IsaClient players get the whole file through the pipe (inside an Imgur picture when the pipe is off), with its IsaClient items. You can also Ctrl+V a .plr in the chat, or drop one on the game.", x, y, width) + 12;
		bool world = InWorld(sb, x, ref y, width);
		List<PlayerFileData> all = (Main.PlayerList ?? new List<PlayerFileData>()).Where(p => p?.Player != null && p.Path != null).ToList();
		string search = (_search ?? "").Trim();
		List<PlayerFileData> players = (search.Length == 0) ? all : all.Where(p => (p.Player.name ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 || Path.GetFileNameWithoutExtension(p.Path).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
		y = Heading(sb, "Your characters", x, y, players.Count);
		if (HubKit.Button(sb, "share:reload", new Rectangle(x + width - 96, y - 26, 96, 24), "Refresh", Theme.Slot, "Reads your Players folder again."))
		{
			Main.LoadPlayers();
		}
		// Find one by name (or file name).
		_search = HubKit.Field(sb, "share:search", new Rectangle(x, y, width, 30), _search, "Search by name...", 40, HubIcon.Item(ShareKinds.Item(ShareKind.Character)));
		y += 38;
		if (players.Count == 0)
		{
			return Note(sb, (all.Count == 0) ? "No characters found in your Players folder." : "No character has that name.", x, y, width) - start;
		}
		string[] labels = { "Send in chat", "Style", "Open" };
		int bw = ButtonsWidth(labels);
		foreach (PlayerFileData file in players)
		{
			Rectangle row = Row(sb, x, y, width, out Rectangle picture);
			HubKit.Head(sb, file.Player, new Vector2(picture.Center.X, picture.Center.Y - 1), 0.8f);
			Lines(sb, row, picture.Right + 10, file.Player.name, IsaClient.Menu.UiKit.Summary(file.Player) + "   " + Path.GetFileName(file.Path), bw);
			int clicked = Buttons(sb, "share:char:" + file.Path.GetHashCode(), row,
				("Send in chat", Send, "Links this character in the chat line.", world && !file.IsCloudSave),
				("Style", Theme.Slot, "Links only its look (hair, clothes, colors) in the chat line.", world),
				("Open", Theme.Slot, "Wear it, paste it as pixel art...", world));
			if (clicked == 0)
			{
				ShareChat.LinkCharacter(file.Path, file.Player);
			}
			else if (clicked == 1)
			{
				ShareChat.LinkStyle(StyleLook.Of(file.Player), file.Player.name);
			}
			else if (clicked == 2)
			{
				IsaClient.Menu.PlayerFileDropDialog.Open(file.Path);
			}
			y = row.Bottom + Gap;
		}
		return y - start;
	}

	#endregion

	#region Styles

	public static int DrawStyles(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y = Note(sb, "A style is the look from character creation: hair, clothing style and colors. It fits in the chat line itself, so it arrives at once. Copy a template in character creation and Ctrl+V it in the chat to link it.", x, y, width) + 12;
		bool world = InWorld(sb, x, ref y, width);
		if (world && Main.LocalPlayer != null)
		{
			y = Heading(sb, "Your look now", x, y);
			string[] mineLabels = { "Send in chat", "Keep" };
			Rectangle row = Row(sb, x, y, width, out Rectangle picture);
			HubKit.Head(sb, Main.LocalPlayer, new Vector2(picture.Center.X, picture.Center.Y - 1), 0.8f);
			StyleLook now = StyleLook.Of(Main.LocalPlayer);
			Lines(sb, row, picture.Right + 10, Main.LocalPlayer.name, IsaClient.Languages.L10n.F("Hairstyle {0}, clothing style {1}", now.Hair + 1, now.SkinVariant + 1), ButtonsWidth(mineLabels));
			int clicked = Buttons(sb, "share:mylook", row,
				("Send in chat", Send, "Links your look in the chat line.", true),
				("Keep", Theme.Slot, "Saves your look in your styles.", !StyleLibrary.Has(now)));
			if (clicked == 0)
			{
				ShareChat.LinkStyle(now, Main.LocalPlayer.name);
			}
			else if (clicked == 1)
			{
				StyleLibrary.Add(now);
			}
			y = row.Bottom + 12;
		}
		List<StyleLook> styles = StyleLibrary.Styles;
		y = Heading(sb, "Your styles", x, y, styles.Count);
		if (styles.Count == 0)
		{
			return Note(sb, "Styles you keep (from the chat or your look) show here.", x, y, width) - start;
		}
		string[] labels = { "Send in chat", "Try on", "Delete" };
		int bw = ButtonsWidth(labels);
		foreach (StyleLook style in styles)
		{
			Rectangle row = Row(sb, x, y, width, out Rectangle picture);
			HubKit.Head(sb, style.Mannequin(), new Vector2(picture.Center.X, picture.Center.Y - 1), 0.8f);
			string name = string.IsNullOrWhiteSpace(style.Name) ? IsaClient.Languages.L10n.T("Character style") : style.Name;
			Lines(sb, row, picture.Right + 10, name, IsaClient.Languages.L10n.F("Hairstyle {0}, clothing style {1}", style.Hair + 1, style.SkinVariant + 1), bw);
			Swatches(sb, style, picture.Right + 10 + (int)HubKit.Measure(name, 0.8f) + 12, row.Y + 9);
			int clicked = Buttons(sb, "share:style:" + style.Hex() + style.Name, row,
				("Delete", Red, null, true),
				("Try on", Green, "Puts this look on you (Restore My Character brings yours back).", world),
				("Send in chat", Send, "Links it in the chat line.", world));
			if (clicked == 0)
			{
				StyleLibrary.Remove(style);
			}
			else if (clicked == 1)
			{
				IsaClient.Gameplay.CharacterPresets.Wear(style.Mannequin(), IsaClient.Gameplay.WearParts.Look, name);
			}
			else if (clicked == 2)
			{
				ShareChat.LinkStyle(style, style.Name);
			}
			y = row.Bottom + Gap;
		}
		return y - start;
	}

	private static void Swatches(SpriteBatch sb, StyleLook style, int x, int y)
	{
		Color[] colors = { style.HairColor, style.EyeColor, style.SkinColor, style.ShirtColor, style.UnderShirtColor, style.PantsColor, style.ShoeColor };
		for (int i = 0; i < colors.Length; i++)
		{
			Rectangle r = new Rectangle(x + i * 13, y + 2, 11, 11);
			HubKit.Fill(sb, r, Color.Black * 0.5f);
			HubKit.Fill(sb, new Rectangle(r.X + 1, r.Y + 1, 9, 9), colors[i]);
		}
	}

	#endregion

	#region Schematics

	private static List<string> SchematicFilesNow()
	{
		if (_schematicFiles == null || (DateTime.Now - _schematicFilesAt).TotalSeconds > 5)
		{
			_schematicFilesAt = DateTime.Now;
			try
			{
				string folder = Path.Combine(Main.SavePath, "Schematics");
				_schematicFiles = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.TEditSch").OrderByDescending(File.GetLastWriteTime).Take(60).ToList() : new List<string>();
			}
			catch
			{
				_schematicFiles = new List<string>();
			}
		}
		return _schematicFiles;
	}

	public static int DrawSchematics(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y = Note(sb, "Schematics go to archive.org (and through the pipe when they're small), so everyone with IsaClient sees a small map of them in the chat. Ctrl+V a .TEditSch in the chat, or right after .copy press Ctrl+V in the chat to send what you copied.", x, y, width) + 12;
		bool world = InWorld(sb, x, ref y, width);
		if (world && Selection.CopiedTiles != null && Selection.CopiedTiles.Count > 0 && Selection.SchematicSizeX > 0)
		{
			y = Heading(sb, "What you copied", x, y);
			string[] copyLabels = { "Send in chat" };
			Rectangle row = Row(sb, x, y, width, out Rectangle picture);
			Item(sb, CopiedIcon, picture);
			Lines(sb, row, picture.Right + 10, IsaClient.Languages.L10n.F("{0} x {1} tiles", Selection.SchematicSizeX, Selection.SchematicSizeY), IsaClient.Languages.L10n.T(ShareChat.CopyIsFresh ? "Just copied: Ctrl+V in the chat sends it too." : "From .copy, a loaded schematic or pixel art."), ButtonsWidth(copyLabels));
			if (Buttons(sb, "share:copied", row, ("Send in chat", Send, "Saves it as a schematic and links it in the chat line.", true)) == 0)
			{
				ShareChat.LinkCopied();
			}
			y = row.Bottom + 12;
		}
		List<string> files = SchematicFilesNow();
		y = Heading(sb, "Your Schematics folder", x, y, files.Count);
		if (HubKit.Button(sb, "share:schfolder", new Rectangle(x + width - 96, y - 26, 96, 24), "Folder", Theme.Slot, "Opens your Schematics folder."))
		{
			try
			{
				string folder = Path.Combine(Main.SavePath, "Schematics");
				Directory.CreateDirectory(folder);
				System.Diagnostics.Process.Start(folder);
			}
			catch (Exception ex)
			{
				IsaClient.Core.ClientLog.LogException(ex);
			}
		}
		if (files.Count == 0)
		{
			return Note(sb, "No .TEditSch files there yet (.save writes them).", x, y, width) - start;
		}
		string[] labels = { "Send in chat", "Paste" };
		int bw = ButtonsWidth(labels);
		foreach (string file in files)
		{
			Rectangle row = Row(sb, x, y, width, out Rectangle picture);
			Item(sb, ShareKinds.Item(ShareKind.Schematic), picture);
			long size = 0;
			try
			{
				size = new FileInfo(file).Length;
			}
			catch
			{
			}
			Lines(sb, row, picture.Right + 10, Path.GetFileNameWithoutExtension(file), (size >= 1024 ? (size / 1024 + " KB") : (size + " B")) + "   " + File.GetLastWriteTime(file).ToString("g"), bw);
			int clicked = Buttons(sb, "share:sch:" + file.GetHashCode(), row,
				("Paste", Green, "Loads it to paste, like dropping it on the game.", world),
				("Send in chat", Send, "Links it in the chat line.", world));
			if (clicked == 0)
			{
				SchematicFiles.ForcedDialogPath = file;
				SchematicFiles.Load();
			}
			else if (clicked == 1)
			{
				ShareChat.LinkSchematic(file);
			}
			y = row.Bottom + Gap;
		}
		return y - start;
	}

	#endregion

	#region Received

	public static int DrawReceived(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		List<ShareInbox.Item> items = ShareInbox.Items;
		List<ShareInbox.Item> theirs = items.Where(i => !i.Mine).ToList();
		List<ShareInbox.Item> mine = items.Where(i => i.Mine).ToList();
		y = Heading(sb, "From others", x, y, theirs.Count);
		if (theirs.Count == 0)
		{
			y = Note(sb, "Characters, styles and schematics others link in chat show here (this session). Received files are kept in IsaClient/Received.", x, y, width) + 12;
		}
		foreach (ShareInbox.Item item in theirs)
		{
			y = DrawItem(sb, item, x, y, width) + Gap;
		}
		if (mine.Count > 0)
		{
			y = Heading(sb, "Sent by you", x, y + 6, mine.Count);
			foreach (ShareInbox.Item item in mine)
			{
				y = DrawItem(sb, item, x, y, width) + Gap;
			}
		}
		return y - start;
	}

	private static int DrawItem(SpriteBatch sb, ShareInbox.Item item, int x, int y, int width)
	{
		bool schematic = item.Kind == ShareKind.Schematic;
		Rectangle row = Row(sb, x, y, width, out Rectangle picture, schematic ? 60 : 38);
		string kind = item.Kind switch
		{
			ShareKind.Character => "Character",
			ShareKind.Style => "Style",
			_ => "Schematic"
		};
		float fraction = -1f;
		string state;
		if (schematic)
		{
			Map(sb, item.Schematic, picture);
			SchematicShare.Entry e = item.Schematic;
			state = (e == null) ? "" : (e.Width + " x " + e.Height);
			if (e != null && e.Uploading)
			{
				fraction = e.Progress;
				state += "   " + IsaClient.Languages.L10n.T("Uploading");
			}
			else if (e != null && !e.Ready && !e.Mine)
			{
				ShareTransfer.Transfer pipe = ShareTransfer.Get(e.Code);
				fraction = Math.Max(e.Progress, (pipe != null) ? pipe.Fraction : 0f);
				state += "   " + IsaClient.Languages.L10n.T(e.Failed ? "Couldn't get it" : "Receiving");
			}
		}
		else
		{
			Player look = ShareInbox.Character(item) ?? item.Mannequin;
			if (look != null)
			{
				HubKit.Head(sb, look, new Vector2(picture.Center.X, picture.Center.Y - 1), 0.8f);
			}
			else
			{
				Item(sb, ShareKinds.Item(item.Kind), picture);
			}
			state = IsaClient.Languages.L10n.T(kind);
			if (item.Kind == ShareKind.Character)
			{
				ShareTransfer.Transfer t = ShareTransfer.Get(item.Code);
				if (t != null && (t.Pushing || (!t.Sending && !t.Complete)))
				{
					fraction = t.Fraction;
					state += "   " + IsaClient.Languages.L10n.T(t.Sending ? "Uploading" : "Receiving") + " " + (int)(t.Fraction * 100f) + "%";
				}
				else if (ShareInbox.Character(item) is Player loaded)
				{
					state = IsaClient.Menu.UiKit.Summary(loaded);
				}
			}
		}
		if (!string.IsNullOrEmpty(item.From))
		{
			state += "   " + IsaClient.Languages.L10n.F("from {0}", item.From);
		}
		state += "   " + item.When.ToString("t");
		string[] labels = { "Open" };
		int bw = ButtonsWidth(labels);
		string title = string.IsNullOrWhiteSpace(item.Name) ? IsaClient.Languages.L10n.T(kind) : item.Name;
		Lines(sb, row, picture.Right + 10, title, state, bw);
		if (fraction >= 0f)
		{
			Bar(sb, row, picture.Right + 10, fraction, bw);
		}
		bool ready = item.Kind == ShareKind.Style || (item.Kind == ShareKind.Character && item.Path != null) || (schematic && item.Schematic != null);
		if (Buttons(sb, "share:got:" + item.Kind + item.Code, row, ("Open", Theme.Slot, "What to do with it.", ready)) == 0)
		{
			ShareDialog.Open(item.Kind, item);
		}
		return row.Bottom;
	}

	#endregion
}
