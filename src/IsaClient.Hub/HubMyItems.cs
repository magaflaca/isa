using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Core;
using IsaClient.Items;
using IsaClient.Menu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// Items > My Items: your IsaClient items (what used to be item presets): each with its name, tag color, text and
/// maker. Take copies, open them in the editor, link them in chat, and keep the ones other players link (they keep
/// their maker). The same items are in the Journey menu's IsaClient tab.
/// </summary>
internal static class HubMyItems
{
	private static HubTheme Theme => HubTheme.Current;

	private static string _search = "";

	/// <summary>-1: every color.</summary>
	private static int _tagFilter = -1;

	private static string _focus;

	private static uint _focusAt;

	private static string _confirm;

	private static uint _confirmAt;

	/// <summary>Shows a design here (from a chat link), lit for a moment.</summary>
	public static void Show(ItemDesign d)
	{
		_focus = d?.Mark?.Id;
		_focusAt = Main.GameUpdateCount;
		_search = "";
		_tagFilter = -1;
		// The click on the chat link doesn't land on the panel that opens under it.
		HubInput.IgnoreUntilRelease = true;
		Main.mouseLeftRelease = false;
		InputState.TildeTerminal = true;
		ClientHub.OpenSection("items", 2);
	}

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		List<ItemDesign> all = ItemLibrary.All;
		Rectangle intro = new Rectangle(x, y, width, 58);
		HubKit.Box(sb, intro, Color.Lerp(Theme.Card, new Color(255, 214, 90), 0.1f) * HubPrefs.Opacity);
		HubIcon.Item(I.Safe).Draw(sb, new Rectangle(intro.X + 12, intro.Y + 11, 36, 36), Color.White, 3f);
		HubKit.Text(sb, "My Items", intro.X + 58, intro.Y + 8, Color.White, 0.9f);
		HubKit.Text(sb, "Your IsaClient items and the ones others linked you. Also in the Journey menu (IsaClient tab).", intro.X + 58, intro.Y + 31, Theme.Muted, 0.62f, width - 420);
		Player p = Main.gameMenu ? null : Main.LocalPlayer;
		bool holding = p != null && !p.HeldItem.IsAir;
		if (HubKit.Button(sb, "myitems:savehand", new Rectangle(intro.Right - 340, intro.Y + 14, 180, 30), "Save item in hand", new Color(60, 120, 70), "Keeps the item you're holding here, as it is.", holding, HubIcon.Item(I.CopperShortsword)))
		{
			Item held = p.HeldItem;
			if (held.isaMark == null)
			{
				held.isaMark = ItemMark.New();
			}
			ItemDesign d = ItemDesign.Capture(held);
			ItemLibrary.Save(d);
			_focus = d.Mark.Id;
			_focusAt = Main.GameUpdateCount;
			HubInput.Sound(7);
		}
		if (HubKit.Button(sb, "myitems:import", new Rectangle(intro.Right - 154, intro.Y + 14, 144, 30), "Import presets", Theme.Slot, "Turns the items of your old item presets (Terraria/Item Mods) into items here.", true, HubIcon.Item(I.Book)))
		{
			int added = ItemLibrary.ImportOldPresets();
			ClientChat.Info(IsaClient.Languages.L10n.F("Imported {0} items from your old presets.", added));
		}
		y = intro.Bottom + 10;
		// Files: share items outside the game.
		int fbx = x;
		if (Action(sb, "myitems:importfile", ref fbx, y, "Import from file", Theme.Slot, "Adds the items of " + ItemFiles.Extension + " files (several at once) to My Items.", true, I.Book))
		{
			ItemFiles.Import(d =>
			{
				_focus = d.Mark.Id;
				_focusAt = Main.GameUpdateCount;
			});
		}
		List<ItemDesign> mine = Shown(all);
		if (Action(sb, "myitems:exportall", ref fbx, y, (mine.Count == all.Count) ? "Export all" : "Export these", Theme.Slot, "Saves the items shown here in one " + ItemFiles.Extension + " file, to share or keep.", mine.Count > 0, I.Safe))
		{
			ItemFiles.Export(mine, "My IsaClient items");
		}
		y += 34;
		// Search and tag colors.
		_search = HubKit.Field(sb, "myitems:search", new Rectangle(x, y, Math.Max(200, width - 12 * 28 - 20), 30), _search, "Search your items...", 40, HubIcon.Texture("Images/UI/Cursor_2"));
		int fx = x + Math.Max(200, width - 12 * 28 - 20) + 10;
		for (int t = -1; t < TagArt.Count; t++)
		{
			Rectangle r = new Rectangle(fx, y + 2, 26, 26);
			bool on = _tagFilter == t;
			bool hover = HubInput.In(r);
			HubKit.Box(sb, r, (on ? Color.Lerp(Theme.Slot, Theme.Accent, 0.5f) : (hover ? Theme.CardHover : Theme.Slot)) * 0.95f);
			if (t < 0)
			{
				HubKit.RawText(sb, "*", r.X + 9, r.Y + 5, Color.White, 0.8f);
			}
			else
			{
				TagArt.Draw(sb, t, new Vector2(r.Center.X, r.Center.Y), 1.8f, Color.White);
			}
			if (hover && HubInput.Hover("myitems:tag:" + t, (t < 0) ? "Every color" : TagArt.Names[t]))
			{
				_tagFilter = t;
			}
			fx += 28;
		}
		y += 40;
		// Linked to you this session.
		List<ItemShare.Received> inbox = ItemShare.Inbox;
		if (inbox.Count > 0)
		{
			HubKit.Text(sb, "Linked to you", x, y, Color.White, 0.76f);
			HubKit.Text(sb, "Items other IsaClient players linked in chat. Keep the ones you like.", x + 120, y + 3, Theme.Muted, 0.6f);
			y += 24;
			y = Grid(sb, inbox.Select(r => (r.Design, r.From)).ToList(), x, y, width, fromChat: true) + 10;
		}
		List<ItemDesign> shown = Shown(all);
		HubKit.Text(sb, "Saved", x, y, Color.White, 0.76f);
		HubKit.RawText(sb, IsaClient.Languages.L10n.F("{0} items", shown.Count), x + 70, y + 3, Theme.Muted, 0.6f);
		y += 24;
		if (all.Count == 0)
		{
			return ClientHub.EmptyNote(sb, x, y, width, HubIcon.Item(I.TinkerersWorkshop), "Nothing saved yet", "Change an item in the Item Editor and press Save to My Items, or keep one someone links in chat.") + y - start;
		}
		y = Grid(sb, shown.Select(d => (d, (string)null)).ToList(), x, y, width, fromChat: false);
		return y + 10 - start;
	}

	private static int Grid(SpriteBatch sb, List<(ItemDesign design, string from)> items, int x, int y, int width, bool fromChat)
	{
		const int cardH = 112;
		int columns = (width >= 700) ? 2 : 1;
		int w = (width - (columns - 1) * 8) / columns;
		for (int i = 0; i < items.Count; i++)
		{
			Rectangle r = new Rectangle(x + i % columns * (w + 8), y + i / columns * (cardH + 8), w, cardH);
			if (HubInput.Visible(r))
			{
				Card(sb, items[i].design, items[i].from, r, fromChat);
			}
		}
		return y + (items.Count + columns - 1) / columns * (cardH + 8);
	}

	private static void Card(SpriteBatch sb, ItemDesign d, string from, Rectangle r, bool fromChat)
	{
		string id = (fromChat ? "chat:" : "lib:") + d.Mark.Id;
		bool focused = _focus == d.Mark.Id && Main.GameUpdateCount - _focusAt < 240;
		Color tag = TagArt.ColorOf(d.Mark.Tag);
		HubKit.Box(sb, r, Color.Lerp(Theme.Card, tag, focused ? (0.25f + 0.1f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 6f)) : 0.05f) * HubPrefs.Opacity);
		HubKit.Fill(sb, new Rectangle(r.X, r.Y + 8, 3, r.Height - 16), tag * 0.9f);
		Rectangle slot = new Rectangle(r.X + 12, r.Y + 12, 56, 56);
		HubKit.Slot(sb, slot, Theme.Slot);
		Item sample = Sample(d);
		HubItemEditor.DrawItem(sb, sample, slot, 1.5f);
		bool hoverSlot = HubInput.In(slot);
		if (hoverSlot)
		{
			Main.HoverItem = sample.Clone();
			Main.hoverItemName = sample.Name;
		}
		int tx = slot.Right + 12;
		int tw = r.Right - tx - 10;
		HubKit.RawText(sb, d.Title, tx, r.Y + 10, ItemRarity.GetColor(d.Rare), 0.82f, tw);
		string sub = d.ItemName + ((d.Title != d.ItemName) ? "" : "") + "  ·  " + Lang.GetItemNameValue(d.Type);
		HubKit.RawText(sb, sub, tx, r.Y + 30, Theme.Muted, 0.56f, tw);
		string maker = d.Mark.MadeByMe ? IsaClient.Languages.L10n.T("by you") : IsaClient.Languages.L10n.F("by {0}", d.Mark.CreatorName);
		if (from != null && from != d.Mark.CreatorName)
		{
			maker += "  ·  " + IsaClient.Languages.L10n.F("linked by {0}", from);
		}
		HubKit.RawText(sb, maker, tx, r.Y + 46, Color.Lerp(tag, Color.White, 0.35f), 0.56f, tw);
		if (!string.IsNullOrWhiteSpace(d.Mark.Description))
		{
			HubKit.RawText(sb, d.Mark.Description.Split('\n')[0], tx, r.Y + 62, new Color(200, 200, 220), 0.54f, tw);
		}
		// Actions.
		int by = r.Bottom - 32;
		int bx = r.X + 12;
		bool inWorld = !Main.gameMenu;
		if (fromChat)
		{
			bool saved = ItemLibrary.Has(d.Mark.Id);
			if (Action(sb, id + ":keep", ref bx, by, saved ? "Kept" : "Keep", new Color(60, 120, 70), "Keeps it in My Items (its maker stays on it).", !saved, I.Book))
			{
				ItemLibrary.Save(d.Copy());
				HubInput.Sound(7);
			}
		}
		if (Action(sb, id + ":take", ref bx, by, "Take", Theme.Slot, "Puts a copy in your inventory.", inWorld, I.Chest))
		{
			HubItemEditor.Give(d);
		}
		if (Action(sb, id + ":edit", ref bx, by, "Edit", Theme.Slot, "Opens it in the Item Editor (on the workbench). Saving updates it here.", true, I.TinkerersWorkshop))
		{
			HubItemEditor.EditDesign(d);
		}
		if (Action(sb, id + ":link", ref bx, by, "Link", Theme.Slot, "Puts it in the chat line: IsaClient players see it as it is.", inWorld, I.Megaphone))
		{
			ItemShare.PutInChat(d);
		}
		if (Action(sb, id + ":export", ref bx, by, "Export", Theme.Slot, "Saves it as an " + ItemFiles.Extension + " file, to share or keep.", true, I.Safe))
		{
			ItemFiles.Export(new[] { d }, d.Title);
		}
		if (!fromChat)
		{
			bool asking = _confirm == id && Main.GameUpdateCount - _confirmAt < 180;
			if (Action(sb, id + ":del", ref bx, by, asking ? "Sure?" : "Delete", asking ? new Color(170, 60, 60) : Theme.Slot, "Deletes it from My Items (click twice).", true, I.TrashCan))
			{
				if (asking)
				{
					ItemLibrary.Delete(d.Mark.Id);
					_confirm = null;
					HubInput.Sound(11);
				}
				else
				{
					_confirm = id;
					_confirmAt = Main.GameUpdateCount;
				}
			}
		}
	}

	/// <summary>The items the search and the tag color leave.</summary>
	private static List<ItemDesign> Shown(List<ItemDesign> all)
	{
		string q = _search.Trim();
		return all.Where(d => (_tagFilter < 0 || d.Mark.Tag == _tagFilter) && (q.Length == 0 || d.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || d.ItemName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (d.Mark.Creator ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
	}

	private static bool Action(SpriteBatch sb, string id, ref int x, int y, string label, Color color, string tip, bool enabled, int icon)
	{
		int w = (int)HubKit.Measure(IsaClient.Languages.L10n.T(label), 0.78f) + 44;
		bool clicked = HubKit.Button(sb, id, new Rectangle(x, y, w, 26), label, color, tip, enabled, HubIcon.Item(icon));
		x += w + 4;
		return clicked;
	}

	private static readonly Dictionary<ItemDesign, Item> _samples = new Dictionary<ItemDesign, Item>();

	private static Item Sample(ItemDesign d)
	{
		if (!_samples.TryGetValue(d, out Item item))
		{
			if (_samples.Count > 300)
			{
				_samples.Clear();
			}
			item = d.Make(1);
			_samples[d] = item;
		}
		return item;
	}
}
