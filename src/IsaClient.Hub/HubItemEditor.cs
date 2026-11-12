using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IsaClient.Core;
using IsaClient.Gameplay;
using IsaClient.Items;
using IsaClient.Menu;
using IsaClient.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI;
using Terraria.ID;
using Terraria.UI;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// Items > Item Editor: the item you're changing, live, with every option in sight. Pick what to change (the item in
/// your hand, any inventory slot, or a new one from the catalog on the workbench), change it by tabs (stats, how it's
/// used, shooting with a preview of its pattern, tools and building, type, looks and text, how servers see it), undo
/// and redo, then keep it in My Items, take copies or link it in chat. Every changed item becomes an IsaClient item:
/// it carries a yellow wire tag (or the color you give it) and you as its creator.
/// </summary>
internal static class HubItemEditor
{
	private enum Tab
	{
		Stats,
		Use,
		Shooting,
		Tools,
		Type,
		Looks,
		Network
	}

	private enum Picker
	{
		None,
		Inventory,
		Catalog,
		Projectile,
		Buff,
		NetId,
		PlaceTile,
		PlaceWall
	}

	private static HubTheme Theme => HubTheme.Current;

	private static readonly string[] TabNames = { "Stats", "Use", "Shooting", "Tools & Building", "Type & Defense", "Looks & Text", "Network" };

	private static readonly int[] TabIcons = { I.CopperShortsword, I.Stopwatch, I.Megashark, I.Picksaw, I.WarriorEmblem, I.Paintbrush, I.WorldGlobe };

	/// <summary>-1: the item in your hand, -2: the workbench, 0..57: that inventory slot.</summary>
	private static int _source = -1;

	private static Item _bench = new Item();

	private static Tab _tab;

	private static Picker _picker;

	private static string _pickerSearch = "";

	private static float _pickerScroll;

	private static string _pickerDrag;

	private static float _pickerDragFrom;

	private static readonly Dictionary<string, string> _typing = new Dictionary<string, string>();

	private static readonly List<ItemDesign> _undo = new List<ItemDesign>();

	private static readonly List<ItemDesign> _redo = new List<ItemDesign>();

	private static string _undoControl;

	private static uint _undoAt;

	private static int _undoType;

	private static string _confirm;

	private static uint _confirmAt;

	#region The item being changed

	private static Item Target
	{
		get
		{
			Player p = Main.LocalPlayer;
			if (_source == -2)
			{
				return _bench;
			}
			if (p == null || Main.gameMenu)
			{
				return null;
			}
			if (_source >= 0 && _source < 58)
			{
				return p.inventory[_source];
			}
			return p.HeldItem;
		}
	}

	private static string SourceName
	{
		get
		{
			if (_source == -2)
			{
				return IsaClient.Languages.L10n.T("Workbench");
			}
			if (_source >= 0)
			{
				return IsaClient.Languages.L10n.F("Inventory slot {0}", _source + 1);
			}
			return IsaClient.Languages.L10n.T("In your hand");
		}
	}

	/// <summary>Before a change: remembered for undo (one step per control while you keep changing it).</summary>
	private static void Before(string control)
	{
		Item item = Target;
		if (item == null || item.IsAir)
		{
			return;
		}
		uint now = Main.GameUpdateCount;
		if (control == _undoControl && now - _undoAt < 45 && _undoType == item.type)
		{
			_undoAt = now;
			return;
		}
		_undoControl = control;
		_undoAt = now;
		_undoType = item.type;
		_undo.Add(ItemDesign.Capture(item));
		if (_undo.Count > 60)
		{
			_undo.RemoveAt(0);
		}
		_redo.Clear();
	}

	/// <summary>After a change: the item is an IsaClient item now, and the cursor / server copies follow.</summary>
	private static void After(Item item, bool resend = false)
	{
		if (item == null || item.IsAir)
		{
			return;
		}
		if (item.isaMark == null)
		{
			item.isaMark = ItemMark.New();
		}
		Player p = Main.LocalPlayer;
		if (_source == -1 && p != null && p.selectedItem == 58)
		{
			Main.mouseItem = item.Clone();
		}
		if (resend && Main.netMode == 1 && p != null && _source != -2)
		{
			int slot = (_source >= 0) ? _source : p.selectedItem;
			if (slot >= 0 && slot < 58)
			{
				NetMessage.SendData(5, -1, -1, null, Main.myPlayer, slot);
			}
		}
	}

	/// <summary>A new mark for the item when it is changed (same mark object is shared by copies).</summary>
	private static ItemMark EditMark(Item item)
	{
		item.isaMark = (item.isaMark == null) ? ItemMark.New() : item.isaMark.Copy();
		return item.isaMark;
	}

	private static void SetTarget(int source)
	{
		_source = source;
		_undo.Clear();
		_redo.Clear();
		_picker = Picker.None;
		_typing.Clear();
	}

	/// <summary>Opens the editor on a design (on the workbench).</summary>
	public static void EditDesign(ItemDesign d)
	{
		_bench = d.Make();
		SetTarget(-2);
		ClientHub.OpenSection("items", 1);
		InputState.TildeTerminal = true;
	}

	private static void Undo(bool redo)
	{
		Item item = Target;
		List<ItemDesign> from = redo ? _redo : _undo;
		List<ItemDesign> to = redo ? _undo : _redo;
		if (item == null || from.Count == 0)
		{
			return;
		}
		ItemDesign d = from[from.Count - 1];
		from.RemoveAt(from.Count - 1);
		to.Add(ItemDesign.Capture(item));
		if (d.Type != item.type)
		{
			return;
		}
		if (d.Prefix != item.prefix)
		{
			item.prefix = d.Prefix;
		}
		d.Apply(item);
		_undoControl = null;
		After(item, resend: true);
		HubInput.Sound(12);
	}

	#endregion

	#region Page

	public static int Draw(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		if (Main.gameMenu && _source != -2)
		{
			return ClientHub.EmptyNote(sb, x, y, width, HubIcon.Item(I.TinkerersWorkshop), "Item Editor", "Join a world to change your items. Items from My Items can still be opened on the workbench.") - start;
		}
		Item item = Target;
		y = DrawHeader(sb, x, y, width, item) + 10;
		// with an empty hand you still pick from the inventory or the catalog
		if (_picker == Picker.Inventory || _picker == Picker.Catalog)
		{
			return DrawPicker(sb, x, y, width) - start;
		}
		if (item == null || item.IsAir)
		{
			string note = (_source == -1) ? "Hold an item, or pick one from your inventory or the catalog above." : "That slot is empty: pick another item above.";
			return ClientHub.EmptyNote(sb, x, y, width, HubIcon.Item(I.TinkerersWorkshop), "Nothing to change", note) + y - start;
		}
		if (_picker != Picker.None)
		{
			return DrawPicker(sb, x, y, width) - start;
		}
		y = DrawTabs(sb, x, y, width) + 10;
		switch (_tab)
		{
		case Tab.Stats:
			y = DrawStats(sb, item, x, y, width);
			break;
		case Tab.Use:
			y = DrawUse(sb, item, x, y, width);
			break;
		case Tab.Shooting:
			y = DrawShooting(sb, item, x, y, width);
			break;
		case Tab.Tools:
			y = DrawTools(sb, item, x, y, width);
			break;
		case Tab.Type:
			y = DrawType(sb, item, x, y, width);
			break;
		case Tab.Looks:
			y = DrawLooks(sb, item, x, y, width);
			break;
		case Tab.Network:
			y = DrawNetwork(sb, item, x, y, width);
			break;
		}
		return y + 10 - start;
	}

	private static int DrawHeader(SpriteBatch sb, int x, int y, int width, Item item)
	{
		Rectangle card = new Rectangle(x, y, width, 150);
		HubKit.Box(sb, card, Color.Lerp(Theme.Card, new Color(255, 214, 90), 0.08f) * HubPrefs.Opacity);
		// The item, big, on a checkered stage.
		Rectangle stage = new Rectangle(card.X + 10, card.Y + 10, 130, 130);
		Checker(sb, stage);
		bool has = item != null && !item.IsAir;
		if (has)
		{
			DrawItem(sb, item, stage, 3f);
		}
		int tx = stage.Right + 14;
		int tw = card.Right - tx - 12;
		if (has)
		{
			HubKit.RawText(sb, item.AffixName(), tx, card.Y + 10, NameColor(item.expert ? -12 : item.rare), 0.95f, tw - 160);
			string sub = Lang.GetItemNameValue(item.type) + "  #" + item.type;
			if (item.isaMark != null && item.isaMark.NetId > 0)
			{
				sub += "  " + IsaClient.Languages.L10n.F("(others see: {0})", Lang.GetItemNameValue(item.isaMark.NetId));
			}
			HubKit.RawText(sb, sub, tx, card.Y + 34, Theme.Muted, 0.62f, tw - 160);
			// Who made it and its tag.
			int tag = TagArt.TagOf(item);
			string made = (item.isaMark != null) ? IsaClient.Languages.L10n.F("IsaClient item by {0}", item.isaMark.CreatorName) : IsaClient.Languages.L10n.T("Plain item (changing it makes it yours)");
			HubKit.RawText(sb, made, tx, card.Y + 54, (tag >= 0) ? Color.Lerp(TagArt.ColorOf(tag), Color.White, 0.3f) : Theme.Muted, 0.62f, tw - 160);
		}
		// Undo / redo / reset at the top right.
		int bx = card.Right - 12;
		bool canUndo = _undo.Count > 0 && has;
		bx -= 64;
		if (HubKit.Button(sb, "itemed:redo", new Rectangle(bx, card.Y + 10, 62, 28), "Redo", Theme.Slot, null, _redo.Count > 0 && has))
		{
			Undo(redo: true);
		}
		bx -= 66;
		if (HubKit.Button(sb, "itemed:undo", new Rectangle(bx, card.Y + 10, 62, 28), "Undo", Theme.Slot, null, canUndo))
		{
			Undo(redo: false);
		}
		bx -= 32;
		if (HubKit.IconButton(sb, "itemed:reset", new Rectangle(bx, card.Y + 10, 28, 28), HubIcon.Item(I.IronAnvil), Confirming("reset") ? "Click again to put every stat back to normal" : "Reset to the normal item (click twice)", Confirming("reset"), has))
		{
			if (Ask("reset"))
			{
				Before("reset");
				int stack = item.stack;
				byte prefix = item.prefix;
				bool fav = item.favorited;
				item.SetDefaults(item.type);
				if (prefix > 0)
				{
					item.Prefix(prefix);
				}
				item.stack = stack;
				item.favorited = fav;
				Player p = Main.LocalPlayer;
				if (_source == -1 && p != null && p.selectedItem == 58)
				{
					Main.mouseItem = item.Clone();
				}
				HubInput.Sound(SoundID.Item37.Style);
			}
		}
		// What to change.
		int ry = card.Y + 78;
		int bx2 = tx;
		Color on = Color.Lerp(Theme.Slot, Theme.Accent, 0.45f);
		if (Fit(sb, "itemed:src:hand", ref bx2, ry, "In your hand", (_source == -1) ? on : Theme.Slot, "Change the item you're holding (or carrying on the cursor).", !Main.gameMenu, I.CopperShortsword))
		{
			SetTarget(-1);
		}
		if (Fit(sb, "itemed:src:inv", ref bx2, ry, "Inventory", (_source >= 0 || _picker == Picker.Inventory) ? on : Theme.Slot, "Pick any item of your inventory.", !Main.gameMenu, I.PiggyBank))
		{
			OpenPicker(Picker.Inventory);
		}
		if (Fit(sb, "itemed:src:cat", ref bx2, ry, "Catalog", (_picker == Picker.Catalog) ? on : Theme.Slot, "Start from any item of the game, on the workbench (it's not in your inventory until you take it).", true, I.Book))
		{
			OpenPicker(Picker.Catalog);
		}
		if (Fit(sb, "itemed:src:bench", ref bx2, ry, "Workbench", (_source == -2) ? on : Theme.Slot, "The item on the workbench.", !_bench.IsAir, I.WorkBench))
		{
			SetTarget(-2);
		}
		// Keep it, take it, link it.
		ry += 34;
		bx2 = tx;
		ItemDesign saved = (has && item.isaMark != null) ? ItemLibrary.Get(item.isaMark.Id) : null;
		if (Fit(sb, "itemed:save", ref bx2, ry, (saved != null) ? "Update in My Items" : "Save to My Items", new Color(60, 120, 70), "Keeps this item in My Items (and the Journey menu's IsaClient tab).", has, I.Book))
		{
			SaveToLibrary(item);
		}
		if (Fit(sb, "itemed:give", ref bx2, ry, "Take a copy", Theme.Slot, "Puts a copy in your inventory.", has && !Main.gameMenu, I.Chest))
		{
			ItemDesign d = ItemDesign.Capture(item);
			EnsureMark(d);
			Give(d);
		}
		if (Fit(sb, "itemed:share", ref bx2, ry, "Link in chat", Theme.Slot, "Puts it in the chat line: IsaClient players see it as it is and can keep it.", has && !Main.gameMenu, I.Megaphone))
		{
			ItemDesign d = ItemDesign.Capture(item);
			EnsureMark(d);
			item.isaMark = d.Mark.Copy();
			ItemShare.PutInChat(d);
		}
		if (Fit(sb, "itemed:mine", ref bx2, ry, "My Items", Theme.Slot, "Your saved IsaClient items.", true, I.Safe))
		{
			ClientHub.OpenSection("items", 2);
		}
		return card.Bottom;
	}

	/// <summary>A button as wide as its text (and icon), placed at <paramref name="x"/>, which moves past it.</summary>
	private static bool Fit(SpriteBatch sb, string id, ref int x, int y, string label, Color color, string tip, bool enabled, int icon)
	{
		int w = (int)HubKit.Measure(IsaClient.Languages.L10n.T(label), 0.78f) + 48;
		bool clicked = HubKit.Button(sb, id, new Rectangle(x, y, w, 28), label, color, tip, enabled, HubIcon.Item(icon));
		x += w + 5;
		return clicked;
	}

	private static void EnsureMark(ItemDesign d)
	{
		if (d.Mark == null)
		{
			d.Mark = ItemMark.New();
		}
	}

	private static void SaveToLibrary(Item item)
	{
		if (item.isaMark == null)
		{
			item.isaMark = ItemMark.New();
		}
		ItemDesign d = ItemDesign.Capture(item);
		ItemDesign old = ItemLibrary.Get(d.Mark.Id);
		if (old != null)
		{
			d.Created = old.Created;
		}
		ItemLibrary.Save(d);
		HubInput.Sound(SoundID.Item37.Style);
		ClientChat.Info(IsaClient.Languages.L10n.F("Saved {0} to My Items.", d.Title));
	}

	/// <summary>Puts a design's item in your inventory (first free slot, else the cursor).</summary>
	public static bool Give(ItemDesign d)
	{
		Player p = Main.LocalPlayer;
		if (p == null || Main.gameMenu)
		{
			return false;
		}
		Item item = d.Make();
		if (item.IsAir)
		{
			return false;
		}
		for (int i = 0; i < 50; i++)
		{
			if (p.inventory[i].IsAir)
			{
				p.inventory[i] = item;
				if (Main.netMode == 1)
				{
					NetMessage.SendData(5, -1, -1, null, Main.myPlayer, i);
				}
				HubInput.Sound(7);
				return true;
			}
		}
		if (Main.mouseItem.IsAir)
		{
			Main.mouseItem = item;
			Main.playerInventory = true;
			HubInput.Sound(7);
			return true;
		}
		ClientChat.Error("Your inventory is full.");
		return false;
	}

	private static int DrawTabs(SpriteBatch sb, int x, int y, int width)
	{
		int count = TabNames.Length;
		int w = (width - (count - 1) * 4) / count;
		for (int i = 0; i < count; i++)
		{
			bool on = (int)_tab == i;
			Rectangle r = new Rectangle(x + i * (w + 4), y, w, 34);
			bool hover = HubInput.In(r);
			float glow = HubInput.Anim("itemed:tab:" + i, hover || on);
			HubKit.Box(sb, r, Color.Lerp(Theme.Card, on ? Color.Lerp(Theme.Slot, Theme.Accent, 0.5f) : Theme.CardHover, glow) * HubPrefs.Opacity);
			if (on)
			{
				HubKit.Fill(sb, new Rectangle(r.X + 8, r.Bottom - 3, r.Width - 16, 2), Theme.Accent);
			}
			HubIcon.Item(TabIcons[i]).Draw(sb, new Rectangle(r.X + 6, r.Y + 7, 20, 20), Color.White, 1f);
			HubKit.Text(sb, TabNames[i], r.X + 30, r.Y + 9, on ? Color.White : Theme.Muted, 0.66f, r.Width - 34);
			if (hover && HubInput.Hover("itemed:tab:" + i))
			{
				_tab = (Tab)i;
				HubInput.Sound(12);
			}
		}
		return y + 34;
	}

	#endregion

	#region Tabs

	private static int DrawStats(SpriteBatch sb, Item item, int x, int y, int width)
	{
		int half = (width - 8) / 2;
		int left = y;
		left = Number(sb, "dmg", "Damage", I.CopperShortsword, x, left, half, item.damage, 0, 1000, 999999, 0, v => item.damage = (int)v);
		left = Number(sb, "crit", "Critical chance %", I.EyeoftheGolem, x, left, half, item.crit, 0, 100, 100, 0, v => item.crit = (int)v);
		left = Number(sb, "kb", "Knockback", I.KOCannon, x, left, half, item.knockBack, 0, 20, 1000, 2, v => item.knockBack = (float)v);
		left = Number(sb, "mana", "Mana cost", I.ManaCrystal, x, left, half, item.mana, 0, 400, 10000, 0, v => item.mana = (int)v);
		int right = y;
		int rx = x + half + 8;
		right = Number(sb, "heal", "Heals health", I.SuperHealingPotion, rx, right, half, item.healLife, 0, 500, 100000, 0, v => item.healLife = (int)v);
		right = Number(sb, "healmana", "Restores mana", I.SuperManaPotion, rx, right, half, item.healMana, 0, 400, 100000, 0, v => item.healMana = (int)v);
		right = Number(sb, "def", "Defense", I.CobaltShield, rx, right, half, item.defense, 0, 200, 10000, 0, v => item.defense = (int)v);
		right = Number(sb, "regen", "Life regeneration", I.BandofRegeneration, rx, right, half, item.lifeRegen, 0, 50, 10000, 0, v => item.lifeRegen = (int)v);
		y = Math.Max(left, right) + 8;
		// Prefixes: every one this item can roll, all in sight.
		y = Section(sb, "Prefix", "Changing it keeps the stats you changed by hand.", x, y, width);
		int[] prefixes = item.GetRollablePrefixes() ?? new int[0];
		List<(int, string)> chips = new List<(int, string)> { (0, IsaClient.Languages.L10n.T("None")) };
		chips.AddRange(prefixes.Where(p => p > 0 && p < Lang.prefix.Length).Select(p => (p, Lang.prefix[p].Value)));
		y = Chips(sb, "prefix", chips, item.prefix, x, y, width, p => SetPrefix(item, p));
		y += 6;
		y = Section(sb, "Value", null, x, y, width);
		y = Number(sb, "value", "Sells for (copper)", I.GoldCoin, x, y, width, item.value, 0, 1000000, 999999999, 0, v => item.value = (int)v);
		return y;
	}

	private static int DrawUse(SpriteBatch sb, Item item, int x, int y, int width)
	{
		int half = (width - 8) / 2;
		int a = Number(sb, "usetime", "Use time (ticks)", I.Stopwatch, x, y, half, item.useTime, 0, 60, 1000, 0, v => item.useTime = Math.Max(0, (int)v), SpeedName(item.useTime));
		int b = Number(sb, "anim", "Animation time (ticks)", I.Stopwatch, x + half + 8, y, half, item.useAnimation, 1, 60, 1000, 0, v => item.useAnimation = Math.Max(1, (int)v), SpeedName(item.useAnimation));
		y = Math.Max(a, b);
		y = Number(sb, "scale", "Size", I.TitanGlove, x, y, width, item.scale, 0.1, 10, 100, 2, v => item.scale = (float)v);
		y += 4;
		y = Section(sb, "Use style", "How the item is swung, held or used.", x, y, width);
		List<Choice> styles = new List<Choice>();
		for (int s = 0; s <= 16; s++)
		{
			styles.Add(new Choice(s, StyleIcon(s), InfoOverlays.UseStyleName(s)));
		}
		y = Cards(sb, "usestyle", styles, item.useStyle, x, y, width, 150, v =>
		{
			item.useStyle = v;
		});
		y += 6;
		y = Section(sb, "Hold style", "How it's held while you're not using it.", x, y, width);
		y = Cards(sb, "holdstyle", HoldStyles(), item.holdStyle, x, y, width, 170, v =>
		{
			item.holdStyle = v;
		});
		y += 6;
		y = Section(sb, "Behavior", null, x, y, width);
		int cw = (width - 8) / 3;
		int row = y;
		Toggle(sb, "auto", "Auto swing", "Keeps using while you hold the button.", I.FeralClaws, item.autoReuse, v => item.autoReuse = v, new Rectangle(x, row, cw, 34));
		Toggle(sb, "turn", "Turn while using", "You can turn around mid-use.", I.Cog, item.useTurn, v => item.useTurn = v, new Rectangle(x + cw + 4, row, cw, 34));
		Toggle(sb, "channel", "Hold to use", "Keeps going while the button is held (like Last Prism).", I.LastPrism, item.channel, v => item.channel = v, new Rectangle(x + (cw + 4) * 2, row, cw, 34));
		row += 38;
		Toggle(sb, "nomelee", "No melee hitbox", "The swing itself doesn't hit.", I.Cog, item.noMelee, v => item.noMelee = v, new Rectangle(x, row, cw, 34));
		Toggle(sb, "nographic", "Hidden while used", "The item isn't drawn while it's being used.", I.InvisibilityPotion, item.noUseGraphic, v => item.noUseGraphic = v, new Rectangle(x + cw + 4, row, cw, 34));
		Toggle(sb, "consumable", "Consumable", "Using it spends one.", I.Apple, item.consumable, v => item.consumable = v, new Rectangle(x + (cw + 4) * 2, row, cw, 34));
		return row + 38;
	}

	private static int DrawShooting(SpriteBatch sb, Item item, int x, int y, int width)
	{
		const int previewW = 380;
		int lw = width - previewW - 10;
		int left = y;
		// Projectile.
		Rectangle proj = new Rectangle(x, left, lw, 52);
		HubKit.Box(sb, proj, Theme.Card * HubPrefs.Opacity);
		Rectangle slot = new Rectangle(proj.X + 8, proj.Y + 6, 40, 40);
		HubKit.Slot(sb, slot, Theme.Slot);
		if (item.shoot > 0)
		{
			DrawProjectile(sb, item.shoot, slot);
		}
		HubKit.Text(sb, "Projectile", proj.X + 56, proj.Y + 6, Theme.Muted, 0.6f);
		HubKit.RawText(sb, (item.shoot > 0) ? (Lang.GetProjectileName(item.shoot).Value + "  #" + item.shoot) : IsaClient.Languages.L10n.T("Shoots nothing"), proj.X + 56, proj.Y + 24, Color.White, 0.74f, lw - 200);
		if (HubKit.Button(sb, "itemed:proj", new Rectangle(proj.Right - 128, proj.Y + 11, 80, 30), "Change", Theme.Slot, "Pick any projectile of the game."))
		{
			OpenPicker(Picker.Projectile);
		}
		if (HubKit.Button(sb, "itemed:projnone", new Rectangle(proj.Right - 44, proj.Y + 11, 36, 30), "X", Theme.Slot, "Shoot nothing.", item.shoot > 0))
		{
			Before("proj");
			item.shoot = 0;
			After(item);
		}
		left = proj.Bottom + 6;
		left = Number(sb, "speed", "Shot speed", I.Megashark, x, left, lw, item.shootSpeed, 0, 40, 1000, 2, v => item.shootSpeed = (float)v);
		left = Number(sb, "times", "Shots per use", I.Shotgun, x, left, lw, item.timesToShoot, 1, 20, 1000, 0, v => item.timesToShoot = (ushort)Math.Max(1, (int)v));
		int tw = (lw - 4) / 2;
		Toggle(sb, "everyuse", "Shoots every use", "Shoots on every use, not only when the animation starts.", I.Cog, item.shootsEveryUse, v => item.shootsEveryUse = v, new Rectangle(x, left, tw, 34));
		Toggle(sb, "invincible", "Hits invincible NPCs", "It (and what it shoots) hurts NPCs that can't be hurt.", I.DemonScythe, item.hitsInvincibleNpc, v => item.hitsInvincibleNpc = v, new Rectangle(x + tw + 4, left, tw, 34));
		left += 40;
		// Ammo, under the shooting options (next to the preview).
		left = Section(sb, "Ammo it uses", null, x, left, lw);
		List<Choice> ammo = Ammo.Select(a => new Choice(a, HubIcon.Item((a == 0) ? I.Cog : a), (a == 0) ? IsaClient.Languages.L10n.T("No ammo") : Lang.GetItemNameValue(a))).ToList();
		left = Icons(sb, "ammo", ammo, item.useAmmo, x, left, lw, v => item.useAmmo = v) + 6;
		// The pattern preview on the right.
		Rectangle preview = new Rectangle(x + lw + 10, y, previewW, Math.Max(300, left - y - 6));
		DrawPatternPreview(sb, item, preview);
		y = Math.Max(left, preview.Bottom + 6);
		// Shot pattern: every weapon's way of shooting, by its icon.
		y = Section(sb, "Shot pattern", "Shoots like the weapon you pick (its real code). The preview shows one use.", x, y, width);
		List<Choice> patterns = new List<Choice>();
		for (int s = 0; s < ShootBehaviors.Count; s++)
		{
			int source = ShootBehaviors.Source(s);
			string name = ShootBehaviors.Name(s);
			HubIcon icon = (source > 0) ? HubIcon.Item(source) : HubIcon.Item((s == 0) ? I.WoodenArrow : I.Confetti);
			string tip = (source > 0) ? IsaClient.Languages.L10n.F("{0} (like {1})", name, Lang.GetItemNameValue(source)) : name;
			patterns.Add(new Choice(s, icon, tip));
		}
		y = Icons(sb, "pattern", patterns, item.itemShootBehavior, x, y, width, v => item.itemShootBehavior = v);
		return y;
	}

	private static int DrawTools(SpriteBatch sb, Item item, int x, int y, int width)
	{
		int half = (width - 8) / 2;
		int a = y;
		a = Number(sb, "pick", "Pickaxe power %", I.Picksaw, x, a, half, item.pick, 0, 250, 10000, 0, v => item.pick = (int)v);
		a = Number(sb, "axe", "Axe power", I.TheAxe, x, a, half, item.axe, 0, 50, 10000, 0, v => item.axe = (int)v);
		a = Number(sb, "hammer", "Hammer power %", I.Pwnhammer, x, a, half, item.hammer, 0, 150, 10000, 0, v => item.hammer = (int)v);
		int b = y;
		b = Number(sb, "range", "Extra reach (tiles)", I.Toolbelt, x + half + 8, b, half, item.tileBoost, -10, 50, 1000, 0, v => item.tileBoost = (int)v);
		b = Number(sb, "fish", "Fishing power", I.GoldenFishingRod, x + half + 8, b, half, item.fishingPole, 0, 100, 10000, 0, v => item.fishingPole = (int)v);
		b = Number(sb, "placestyle", "Place style", I.StoneBlock, x + half + 8, b, half, item.placeStyle, 0, 120, 10000, 0, v => item.placeStyle = (int)v);
		y = Math.Max(a, b) + 6;
		y = Section(sb, "What it places", "Pick an item that places something: this one will place the same.", x, y, width);
		y = PlaceRow(sb, "tile", "Places block", (item.createTile >= 0) ? TileName(item.createTile) : null, x, y, width, Picker.PlaceTile, () => { item.createTile = -1; });
		y = PlaceRow(sb, "wall", "Places wall", (item.createWall > 0) ? WallName(item.createWall) : null, x, y, width, Picker.PlaceWall, () => { item.createWall = -1; });
		return y;
	}

	private static int DrawType(SpriteBatch sb, Item item, int x, int y, int width)
	{
		y = Section(sb, "Damage class", "Which bonuses it takes (they can be combined).", x, y, width);
		int cw = (width - 16) / 5;
		Toggle(sb, "melee", "Melee", null, I.WarriorEmblem, item.melee, v => item.melee = v, new Rectangle(x, y, cw, 40));
		Toggle(sb, "ranged", "Ranged", null, I.RangerEmblem, item.ranged, v => item.ranged = v, new Rectangle(x + (cw + 4), y, cw, 40));
		Toggle(sb, "magic", "Magic", null, I.SorcererEmblem, item.magic, v => item.magic = v, new Rectangle(x + (cw + 4) * 2, y, cw, 40));
		Toggle(sb, "summon", "item::Summon", null, I.SummonerEmblem, item.summon, v => item.summon = v, new Rectangle(x + (cw + 4) * 3, y, cw, 40));
		Toggle(sb, "sentry", "Sentry", null, I.QueenSpiderStaff, item.sentry, v => item.sentry = v, new Rectangle(x + (cw + 4) * 4, y, cw, 40));
		y += 48;
		y = Section(sb, "Equipment", null, x, y, width);
		int tw = (width - 8) / 3;
		Toggle(sb, "accessory", "Accessory", "Can go in accessory slots.", I.BandofRegeneration, item.accessory, v => item.accessory = v, new Rectangle(x, y, tw, 34));
		Toggle(sb, "vanity", "Vanity", "Counts as vanity.", I.TopHat, item.vanity, v => item.vanity = v, new Rectangle(x + tw + 4, y, tw, 34));
		y += 40;
		int half = (width - 8) / 2;
		int a = Number(sb, "manabonus", "Max mana bonus", I.ManaCrystal, x, y, half, item.manaIncrease, 0, 200, 10000, 0, v => item.manaIncrease = (int)v);
		int b = Number(sb, "wing", "Wings (slot id, -1 none)", I.AngelWings, x + half + 8, y, half, item.wingSlot, -1, 50, 200, 0, v => item.wingSlot = (sbyte)v);
		y = Math.Max(a, b);
		y = Section(sb, "Buff when used", null, x, y + 4, width);
		Rectangle buff = new Rectangle(x, y, width, 46);
		HubKit.Box(sb, buff, Theme.Card * HubPrefs.Opacity);
		if (item.buffType > 0 && item.buffType < BuffID.Count)
		{
			Texture2D tex = TextureAssets.Buff[item.buffType].Value;
			sb.Draw(tex, new Rectangle(buff.X + 9, buff.Y + 7, 32, 32), Color.White);
			HubKit.RawText(sb, Lang.GetBuffName(item.buffType), buff.X + 50, buff.Y + 14, Color.White, 0.74f, width - 400);
		}
		else
		{
			HubKit.Text(sb, "No buff", buff.X + 14, buff.Y + 14, Theme.Muted, 0.74f);
		}
		if (HubKit.Button(sb, "itemed:buff", new Rectangle(buff.Right - 300, buff.Y + 8, 80, 30), "Change", Theme.Slot, "Pick a buff."))
		{
			OpenPicker(Picker.Buff);
		}
		Number(sb, "bufftime", "Seconds", I.Stopwatch, buff.Right - 214, buff.Y + 6, 206, item.buffTime / 60.0, 0, 600, 100000, 0, v => item.buffTime = (int)(v * 60), null, compact: true);
		return buff.Bottom + 6;
	}

	private static int DrawLooks(SpriteBatch sb, Item item, int x, int y, int width)
	{
		ItemMark mark = item.isaMark;
		y = Section(sb, "Names", null, x, y, width);
		int half = (width - 8) / 2;
		HubKit.Text(sb, "Item name", x, y, Theme.Muted, 0.62f);
		HubKit.Text(sb, "Name in My Items", x + half + 8, y, Theme.Muted, 0.62f);
		y += 18;
		string name = HubKit.Field(sb, "itemed:name", new Rectangle(x, y, half, 30), item.NameOverride ?? "", Lang.GetItemNameValue(item.type), ItemMark.MaxTitle);
		if (name != (item.NameOverride ?? ""))
		{
			Before("name");
			if (string.IsNullOrWhiteSpace(name))
			{
				item.ClearNameOverride();
			}
			else
			{
				item.SetNameOverride(name);
			}
			After(item);
		}
		string title = HubKit.Field(sb, "itemed:title", new Rectangle(x + half + 8, y, half, 30), mark?.Title ?? "", "Same as the item", ItemMark.MaxTitle);
		if (title != (mark?.Title ?? ""))
		{
			Before("title");
			EditMark(item).Title = string.IsNullOrWhiteSpace(title) ? null : title;
			After(item);
		}
		y += 38;
		// Description: three lines of your own under its tooltip.
		y = Section(sb, "Description", "Your own lines, shown at the end of its tooltip (also to others when you link it).", x, y, width);
		string[] lines = (mark?.Description ?? "").Split('\n');
		string[] edited = new string[3];
		bool changed = false;
		for (int i = 0; i < 3; i++)
		{
			string old = (i < lines.Length) ? lines[i] : "";
			edited[i] = HubKit.Field(sb, "itemed:desc" + i, new Rectangle(x, y, width, 28), old, (i == 0) ? "A line about this item..." : "", 70);
			changed |= edited[i] != old;
			y += 32;
		}
		if (changed)
		{
			Before("desc");
			string text = string.Join("\n", edited).TrimEnd('\n');
			if (text.Length > ItemMark.MaxDescription)
			{
				text = text.Substring(0, ItemMark.MaxDescription);
			}
			EditMark(item).Description = string.IsNullOrWhiteSpace(text) ? null : text;
			After(item);
		}
		y += 4;
		y = DrawInfo(sb, item, x, y, width);
		// Tag color.
		y = Section(sb, "Tag color", "The wire knot on its icon: sort your items by color.", x, y, width);
		int current = (mark != null) ? mark.Tag : -1;
		int tx = x;
		for (int t = 0; t < TagArt.Count; t++)
		{
			Rectangle r = new Rectangle(tx, y, 64, 52);
			bool on = current == t;
			bool hover = HubInput.In(r);
			HubKit.Box(sb, r, (on ? Color.Lerp(Theme.Slot, TagArt.ColorOf(t), 0.35f) : (hover ? Theme.CardHover : Theme.Slot)) * 0.95f);
			TagArt.Draw(sb, t, new Vector2(r.Center.X, r.Y + 18), 2.2f, Color.White);
			HubKit.Text(sb, TagArt.Names[t], r.X + 4, r.Y + 32, on ? Color.White : Theme.Muted, 0.52f, r.Width - 8);
			if (hover && HubInput.Hover("itemed:tag:" + t))
			{
				Before("tag");
				EditMark(item).Tag = (byte)t;
				After(item);
				HubInput.Sound(12);
			}
			tx += 68;
			if (tx + 64 > x + width)
			{
				tx = x;
				y += 56;
			}
		}
		y += 60;
		// Rarity (name color) and tint.
		y = Section(sb, "Rarity", "The color of its name.", x, y, width);
		List<(int, string)> rarities = new List<(int, string)>();
		for (int r = -1; r <= 11; r++)
		{
			rarities.Add((r, r.ToString()));
		}
		rarities.Add((-12, IsaClient.Languages.L10n.T("Expert")));
		rarities.Add((-13, IsaClient.Languages.L10n.T("Master")));
		y = Chips(sb, "rare", rarities, item.rare, x, y, width, v =>
		{
			item.rare = v;
			item.expert = v == -12;
		}, NameColor);
		y += 4;
		y = Section(sb, "Tint", "Paints the item's sprite (white: its own colors).", x, y, width);
		Color tint = (item.color == default(Color)) ? Color.White : item.color;
		int third = (width - 70) / 3;
		for (int c = 0; c < 3; c++)
		{
			int ch = c;
			float value = ((c == 0) ? tint.R : ((c == 1) ? tint.G : tint.B)) / 255f;
			string id = "itemed:tint" + c;
			float? moved = HubKit.Slider(sb, id, new Rectangle(x + c * (third + 4), y + 4, third, 22), value, true, ref ClientHub.SliderDrag, f => (ch == 0) ? new Color(f, 0f, 0f) : ((ch == 1) ? new Color(0f, f, 0f) : new Color(0f, 0f, f)));
			if (moved.HasValue)
			{
				Before("tint");
				Color t2 = tint;
				byte b = (byte)(moved.Value * 255f);
				if (ch == 0)
				{
					t2.R = b;
				}
				else if (ch == 1)
				{
					t2.G = b;
				}
				else
				{
					t2.B = b;
				}
				t2.A = 255;
				item.color = (t2 == Color.White) ? default(Color) : t2;
				After(item);
			}
		}
		if (HubKit.Button(sb, "itemed:tintoff", new Rectangle(x + width - 62, y, 62, 30), "None", Theme.Slot, "Its own colors.", item.color != default(Color)))
		{
			Before("tint");
			item.color = default(Color);
			After(item);
		}
		y += 38;
		// Who made it.
		if (mark != null)
		{
			string who = mark.MadeByMe ? IsaClient.Languages.L10n.T("Made by you") : IsaClient.Languages.L10n.F("Made by {0}", mark.CreatorName);
			HubKit.RawText(sb, who + ((mark.CreatorId != 0) ? ("  ·  IsaClient ID " + IsaClient.Identity.ClientIdentity.Hex(mark.CreatorId)) : ""), x, y, Theme.Muted, 0.6f);
			y += 20;
			// Only its maker can choose the name shown as its maker.
			if (mark.MadeByMe)
			{
				HubKit.Text(sb, "Show me as", x, y + 7, Theme.Muted, 0.62f);
				string shown = HubKit.Field(sb, "itemed:maker", new Rectangle(x + 110, y, Math.Min(260, width - 110), 30), mark.CreatorShown ?? "", CreatorNames.Of(mark.CreatorId, mark.Creator), 24);
				if (shown != (mark.CreatorShown ?? ""))
				{
					Before("maker");
					EditMark(item).CreatorShown = string.IsNullOrWhiteSpace(shown) ? null : shown;
					After(item);
				}
				y += 38;
			}
		}
		return y;
	}

	private const int InfoLines = 12;

	/// <summary>Its whole info (every line under the name), written by you, with placeholders for its numbers.</summary>
	private static int DrawInfo(SpriteBatch sb, Item item, int x, int y, int width)
	{
		ItemMark mark = item.isaMark;
		bool on = mark != null && mark.Info != null;
		y = Section(sb, "Full info", "Write all of its info yourself (others see it too when you link it).", x, y, width);
		Color lit = Color.Lerp(Theme.Slot, Theme.Accent, 0.45f);
		int bx = x;
		if (Fit(sb, "itemed:infoon", ref bx, y, "Use my own info", on ? lit : Theme.Slot, "Its info is the text below instead of the game's.", true, I.Sign))
		{
			Before("info");
			EditMark(item).Info = on ? null : Limit(IsaClient.Items.ItemTooltip.GameInfoTemplate(item));
			After(item);
			return y + 38;
		}
		if (on && Fit(sb, "itemed:infogame", ref bx, y, "From the game's info", Theme.Slot, "Start again from the game's info, its numbers as placeholders.", true, I.Book))
		{
			Before("info");
			EditMark(item).Info = Limit(IsaClient.Items.ItemTooltip.GameInfoTemplate(item));
			After(item);
		}
		y += 38;
		if (!on)
		{
			return y;
		}
		HubKit.RawText(sb, IsaClient.Languages.L10n.T("Placeholders:") + " " + string.Join(" ", IsaClient.Items.ItemTooltip.Placeholders, 0, 10), x, y, Theme.Muted, 0.55f);
		y += 16;
		HubKit.RawText(sb, string.Join(" ", IsaClient.Items.ItemTooltip.Placeholders, 10, IsaClient.Items.ItemTooltip.Placeholders.Length - 10) + "  " + IsaClient.Languages.L10n.T("({info} alone on a line: the game's info)"), x, y, Theme.Muted, 0.55f);
		y += 20;
		string[] lines = mark.Info.Split('\n');
		string[] edited = new string[InfoLines];
		bool changed = false;
		int shown = Math.Min(InfoLines, Math.Max(lines.Length + 1, 3));
		for (int i = 0; i < shown; i++)
		{
			string old = (i < lines.Length) ? lines[i] : "";
			edited[i] = HubKit.Field(sb, "itemed:info" + i, new Rectangle(x, y, width, 28), old, (i == 0) ? "{damage} magic damage" : "", 90);
			changed |= edited[i] != old;
			y += 32;
		}
		if (changed)
		{
			Before("info");
			EditMark(item).Info = Limit(string.Join("\n", edited, 0, shown).TrimEnd('\n'));
			After(item);
		}
		return y + 4;
	}

	private static string Limit(string info)
	{
		info = info ?? "";
		return (info.Length > ItemMark.MaxInfo) ? info.Substring(0, ItemMark.MaxInfo) : info;
	}

	private static int DrawNetwork(SpriteBatch sb, Item item, int x, int y, int width)
	{
		y = Section(sb, "How others see it", null, x, y, width);
		Rectangle card = new Rectangle(x, y, width, 86);
		HubKit.Box(sb, card, Theme.Card * HubPrefs.Opacity);
		int net = item.isaMark?.NetId ?? 0;
		Rectangle here = new Rectangle(card.X + 10, card.Y + 10, 44, 44);
		HubKit.Slot(sb, here, Theme.Slot);
		DrawItem(sb, item, here, 1f);
		HubKit.Text(sb, "Here", here.X, here.Bottom + 4, Theme.Muted, 0.56f);
		HubKit.RawText(sb, "→", here.Right + 14, here.Y + 10, Theme.Muted, 1f);
		Rectangle there = new Rectangle(here.Right + 44, card.Y + 10, 44, 44);
		HubKit.Slot(sb, there, Theme.Slot);
		HubIcon.Item((net > 0) ? net : item.type).Draw(sb, new Rectangle(there.X + 6, there.Y + 6, 32, 32), Color.White, 1f);
		HubKit.Text(sb, "Server", there.X, there.Bottom + 4, Theme.Muted, 0.56f);
		int tx = there.Right + 16;
		HubKit.RawText(sb, (net > 0) ? IsaClient.Languages.L10n.F("Servers and other players get it as {0} (#{1}).", Lang.GetItemNameValue(net), net) : IsaClient.Languages.L10n.T("Servers and other players get it as what it is."), tx, card.Y + 14, Color.White, 0.7f, card.Right - tx - 200);
		HubKit.Text(sb, "It stays the same item here; only its id on the network changes (drops, chests, your inventory as the server keeps it, chat links).", tx, card.Y + 38, Theme.Muted, 0.58f, card.Right - tx - 200);
		if (HubKit.Button(sb, "itemed:netid", new Rectangle(card.Right - 186, card.Y + 12, 100, 30), "Change", Theme.Slot, "Pick the item it's sent as."))
		{
			OpenPicker(Picker.NetId);
		}
		if (HubKit.Button(sb, "itemed:netidoff", new Rectangle(card.Right - 80, card.Y + 12, 70, 30), "Own id", Theme.Slot, "Sent as what it is.", net > 0))
		{
			Before("netid");
			EditMark(item).NetId = 0;
			After(item, resend: true);
		}
		y = card.Bottom + 8;
		HubKit.Text(sb, "Servers with server-side characters keep the item under that id: IsaClient turns it back into this one when you join again.", x, y, Theme.Muted, 0.6f, width);
		return y + 40;
	}

	#endregion

	#region Prefix

	/// <summary>A new prefix that keeps every stat you changed by hand (the ones still at the old prefix's value follow the new one).</summary>
	private static void SetPrefix(Item item, int prefix)
	{
		Item plain = new Item();
		plain.SetDefaults(item.type);
		Item oldOne = plain.Clone();
		if (item.prefix > 0)
		{
			oldOne.Prefix(item.prefix);
		}
		Item newOne = plain.Clone();
		if (prefix > 0)
		{
			newOne.Prefix(prefix);
		}
		if (item.damage == oldOne.damage)
		{
			item.damage = newOne.damage;
		}
		if (item.crit == oldOne.crit)
		{
			item.crit = newOne.crit;
		}
		if (item.knockBack == oldOne.knockBack)
		{
			item.knockBack = newOne.knockBack;
		}
		if (item.useTime == oldOne.useTime)
		{
			item.useTime = newOne.useTime;
		}
		if (item.useAnimation == oldOne.useAnimation)
		{
			item.useAnimation = newOne.useAnimation;
		}
		if (item.scale == oldOne.scale)
		{
			item.scale = newOne.scale;
		}
		if (item.mana == oldOne.mana)
		{
			item.mana = newOne.mana;
		}
		if (item.shootSpeed == oldOne.shootSpeed)
		{
			item.shootSpeed = newOne.shootSpeed;
		}
		if (item.value == oldOne.value)
		{
			item.value = newOne.value;
		}
		item.prefix = (byte)prefix;
		HubInput.Sound(SoundID.Item37.Style);
	}

	#endregion

	#region Shot pattern preview

	private static string _shotKey;

	private static uint _shotEnded;

	/// <summary>Plays the pattern with the projectile of the weapon it comes from (instead of this item's).</summary>
	/// <summary>The name's color for a rarity, as the game's tooltip draws it (10, 11, Expert, Master too).</summary>
	private static Color NameColor(int rare)
	{
		Color color = Main.MouseText_DrawItemTooltip_GetItemNameColor(rare, 0);
		return new Color(color.R, color.G, color.B);
	}

	private static bool _patternProjectile;

	/// <summary>Where the player stands on the stage (fractions of it); drag the head to move it.</summary>
	private static Vector2 _stagePlayer = new Vector2(0.2f, 0.68f);

	private static bool _draggingHead;

	/// <summary>World pixels the stage shows from top to bottom (falling stars start ~600 above).</summary>
	private const float StageWorldHeight = 760f;

	private static void DrawPatternPreview(SpriteBatch sb, Item item, Rectangle area)
	{
		HubKit.Box(sb, area, new Color(14, 16, 30) * 0.95f);
		HubKit.Text(sb, "Pattern preview", area.X + 10, area.Y + 8, Theme.Muted, 0.6f);
		Toggle(sb, "patternproj", "Pattern's own shot", "Plays it with the projectile of the weapon the pattern comes from.", I.Megashark, _patternProjectile, v =>
		{
			_patternProjectile = v;
			_shotKey = null;
		}, new Rectangle(area.Right - 196, area.Y + 4, 190, 28), changesItem: false);
		Rectangle stage = new Rectangle(area.X + 6, area.Y + 36, area.Width - 12, area.Height - 42);
		HubKit.Fill(sb, stage, Color.Black * 0.35f);
		if (Main.gameMenu)
		{
			HubKit.Text(sb, "Join a world to see it.", stage.X + 10, stage.Y + 10, Theme.Muted, 0.62f);
			return;
		}
		uint now = Main.GameUpdateCount;
		float scale = stage.Height / StageWorldHeight;
		Vector2 player = new Vector2(stage.X + stage.Width * _stagePlayer.X, stage.Y + stage.Height * _stagePlayer.Y);
		// Drag the head to move the player; right click to aim somewhere else.
		bool overHead = Vector2.Distance(new Vector2(HubInput.Mouse.X, HubInput.Mouse.Y), player) < 22f;
		if (overHead && HubInput.Clicked)
		{
			_draggingHead = true;
		}
		if (_draggingHead)
		{
			if (HubInput.Down)
			{
				_stagePlayer = new Vector2(MathHelper.Clamp((HubInput.Mouse.X - stage.X) / (float)stage.Width, 0.03f, 0.97f), MathHelper.Clamp((HubInput.Mouse.Y - stage.Y) / (float)stage.Height, 0.05f, 0.95f));
				player = new Vector2(stage.X + stage.Width * _stagePlayer.X, stage.Y + stage.Height * _stagePlayer.Y);
				HubInput.Consume();
			}
			else
			{
				_draggingHead = false;
			}
		}
		if (HubInput.In(stage))
		{
			HubInput.Hover("itemed:stage", "Drag the head to move the player. Right click to aim there.");
			if (HubInput.RightClicked)
			{
				ShotPreview.Aim = (new Vector2(HubInput.Mouse.X, HubInput.Mouse.Y) - player) / scale;
				HubInput.Consume();
			}
		}
		string key = item.type + ":" + item.shoot + ":" + item.shootSpeed + ":" + item.itemShootBehavior + ":" + item.timesToShoot + ":" + item.useAmmo + ":" + item.damage + ":" + _patternProjectile + ":" + ShotPreview.Aim;
		bool done = ((ShotPreview.Count == 0 || ShotPreview.Alive == 0) && !ShotPreview.HasDust) || ShotPreview.Ticks > 360;
		if (done && _shotEnded == 0)
		{
			_shotEnded = now;
		}
		// A new use when something changed, or a short pause after the last one ended.
		if (key != _shotKey || (done && now - _shotEnded > 25))
		{
			_shotKey = key;
			_shotEnded = 0;
			Item copy = item.Clone();
			int source = ShootBehaviors.Source(item.itemShootBehavior);
			if (_patternProjectile && source > 0 && ContentSamples.ItemsByType.TryGetValue(source, out Item weapon))
			{
				copy.shoot = weapon.shoot;
				copy.shootSpeed = weapon.shootSpeed;
				copy.useAmmo = weapon.useAmmo;
			}
			ShotPreview.Start(copy);
		}
		ShotPreview.Step();
		// The aim mark and the player stay put, as in game when you hold still.
		Vector2 aim = player + ShotPreview.Aim * scale;
		HubKit.Fill(sb, new Rectangle((int)aim.X - 7, (int)aim.Y, 15, 1), Color.Red * 0.7f);
		HubKit.Fill(sb, new Rectangle((int)aim.X, (int)aim.Y - 7, 1, 15), Color.Red * 0.7f);
		HubKit.Head(sb, Main.LocalPlayer, player, 1f);
		if (ShotPreview.Count == 0 && !ShotPreview.HasDust)
		{
			string why = (item.shoot <= 0 && !_patternProjectile) ? "It shoots nothing." : ((item.useAmmo > 0) ? "Nothing to show: no ammo for it in your inventory?" : "Nothing to show for one use.");
			HubKit.Text(sb, why, stage.X + 10, stage.Y + 10, Theme.Muted, 0.62f, stage.Width - 20);
			return;
		}
		ShotPreview.Draw(sb, stage, player, scale, HubInput.Matrix, HubInput.Clipping);
		HubKit.RawText(sb, IsaClient.Languages.L10n.F("{0} projectiles per use", ShotPreview.Count), stage.X + 6, stage.Bottom - 18, Theme.Muted, 0.56f);
	}

	private static void DrawProjectile(SpriteBatch sb, int type, Rectangle box)
	{
		if (type <= 0 || type >= ProjectileID.Count)
		{
			return;
		}
		// Projectiles the game draws with an item's sprite.
		int asItem = type switch
		{
			857 => I.FirstFractal,
			933 => I.Zenith,
			_ => 0
		};
		if (asItem > 0)
		{
			HubIcon.Item(asItem).Draw(sb, new Rectangle(box.X + 2, box.Y + 2, box.Width - 4, box.Height - 4), Color.White, 1f);
			return;
		}
		if (ContentSamples.ProjectilesByType.TryGetValue(type, out Projectile sample) && (sample.alpha >= 255 || sample.hide))
		{
			// Seen in game only as its dust (powders, bolts, beams).
			HubIcon.Item(I.Cloud).Draw(sb, new Rectangle(box.X + 6, box.Y + 6, box.Width - 12, box.Height - 12), Color.White * 0.5f, 1f);
			return;
		}
		ReLogic.Content.Asset<Texture2D> asset = TextureAssets.Projectile[type];
		if (!asset.IsLoaded)
		{
			// Still loading (it would draw the magenta placeholder).
			Main.instance.LoadProjectile(type);
			return;
		}
		Texture2D tex = asset.Value;
		int frames = Math.Max(1, Main.projFrames[type]);
		int h = Math.Max(1, tex.Height / frames);
		float fit = Math.Min((box.Width - 4) / (float)tex.Width, (box.Height - 4) / (float)h);
		// Small sprites grow (whole steps, never blurry); big ones shrink to fit.
		float scale = (fit >= 1f) ? Math.Min(3f, (float)Math.Floor(fit)) : fit;
		sb.Draw(tex, new Vector2(box.Center.X, box.Center.Y), new Rectangle(0, 0, tex.Width, h), Color.White, 0f, new Vector2(tex.Width, h) / 2f, scale, SpriteEffects.None, 0f);
	}

	#endregion

	#region Pickers

	private sealed class PickEntry
	{
		public int Id;

		public string Name;

		public int Icon;

		public PickEntry(int id, string name, int icon)
		{
			Id = id;
			Name = name;
			Icon = icon;
		}
	}

	private static readonly Dictionary<Picker, List<PickEntry>> _pickLists = new Dictionary<Picker, List<PickEntry>>();

	private static void OpenPicker(Picker picker)
	{
		_picker = (_picker == picker) ? Picker.None : picker;
		_pickerSearch = "";
		_pickerScroll = 0f;
		HubInput.Sound(12);
	}

	private static List<PickEntry> PickList(Picker picker)
	{
		if (_pickLists.TryGetValue(picker, out List<PickEntry> list))
		{
			return list;
		}
		list = new List<PickEntry>();
		switch (picker)
		{
		case Picker.Catalog:
		case Picker.NetId:
			for (int t = 1; t < I.Count; t++)
			{
				string name = Lang.GetItemNameValue(t);
				if (!string.IsNullOrWhiteSpace(name) && (picker == Picker.Catalog || !ItemID.Sets.Deprecated[t]))
				{
					list.Add(new PickEntry(t, name, t));
				}
			}
			break;
		case Picker.PlaceTile:
		case Picker.PlaceWall:
			foreach (KeyValuePair<int, Item> pair in ContentSamples.ItemsByType)
			{
				Item s = pair.Value;
				if ((picker == Picker.PlaceTile) ? (s.createTile >= 0) : (s.createWall > 0))
				{
					string name = Lang.GetItemNameValue(pair.Key);
					if (!string.IsNullOrWhiteSpace(name))
					{
						list.Add(new PickEntry(pair.Key, name, pair.Key));
					}
				}
			}
			list.Sort((a, b) => a.Id.CompareTo(b.Id));
			break;
		case Picker.Projectile:
			for (int t = 1; t < ProjectileID.Count; t++)
			{
				string name = Lang.GetProjectileName(t).Value;
				if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("ProjectileName."))
				{
					list.Add(new PickEntry(t, name, 0));
				}
			}
			break;
		case Picker.Buff:
			for (int t = 1; t < BuffID.Count; t++)
			{
				string name = Lang.GetBuffName(t);
				if (!string.IsNullOrWhiteSpace(name))
				{
					list.Add(new PickEntry(t, name, 0));
				}
			}
			break;
		}
		_pickLists[picker] = list;
		return list;
	}

	private static int DrawPicker(SpriteBatch sb, int x, int y, int width)
	{
		string title = _picker switch
		{
			Picker.Inventory => "Pick an item of your inventory",
			Picker.Catalog => "Pick any item: it goes on the workbench",
			Picker.Projectile => "Pick the projectile it shoots",
			Picker.Buff => "Pick the buff it gives",
			Picker.NetId => "Pick the item servers and others see",
			Picker.PlaceTile => "Pick an item: this one will place what it places",
			_ => "Pick an item: this one will place its wall"
		};
		HubKit.Text(sb, title, x, y + 6, Color.White, 0.8f);
		if (HubKit.Button(sb, "itemed:pickclose", new Rectangle(x + width - 100, y, 100, 30), "Back", Theme.Slot, null, true, HubIcon.Texture("Images/UI/SearchCancel")))
		{
			_picker = Picker.None;
			return y + 40;
		}
		y += 38;
		if (_picker == Picker.Inventory)
		{
			return DrawInventoryPicker(sb, x, y, width);
		}
		_pickerSearch = HubKit.Field(sb, "itemed:picksearch", new Rectangle(x, y, width, 30), _pickerSearch, "Search by name or id...", 40, HubIcon.Texture("Images/UI/Cursor_2"));
		y += 38;
		List<PickEntry> all = PickList(_picker);
		string q = _pickerSearch.Trim();
		bool number = int.TryParse(q, out int qid);
		List<PickEntry> results = (q.Length == 0) ? all : all.Where(e => (number && e.Id == qid) || e.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
		Rectangle area = new Rectangle(x, y, width, Math.Max(260, ClientHub.PageViewHeight - 320));
		HubKit.Box(sb, area, Color.Black * 0.25f);
		Rectangle inner = new Rectangle(area.X + 6, area.Y + 6, area.Width - 22, area.Height - 12);
		const int cell = 46;
		const int gap = 4;
		int columns = Math.Max(1, (inner.Width + gap) / (cell + gap));
		int rows = (results.Count + columns - 1) / columns;
		float content = rows * (cell + gap);
		HubInput.InnerScroll = area;
		if (HubInput.Wheel != 0 && HubInput.In(area))
		{
			_pickerScroll -= HubInput.Wheel / 2f;
			HubInput.Wheel = 0;
		}
		_pickerScroll = MathHelper.Clamp(_pickerScroll, 0f, Math.Max(0f, content - inner.Height));
		_pickerScroll = HubKit.Scrollbar(sb, "itemed:pickbar", new Rectangle(area.Right - 12, area.Y + 6, 10, area.Height - 12), _pickerScroll, content, inner.Height, ref _pickerDrag, ref _pickerDragFrom);
		bool outer = HubInput.Clipping;
		Rectangle outerClip = HubInput.Clip;
		HubInput.BeginClip(sb, outer ? Rectangle.Intersect(inner, outerClip) : inner);
		int first = (int)(_pickerScroll / (cell + gap));
		int last = Math.Min(rows - 1, first + inner.Height / (cell + gap) + 1);
		for (int row = first; row <= last; row++)
		{
			for (int col = 0; col < columns; col++)
			{
				int i = row * columns + col;
				if (i >= results.Count)
				{
					break;
				}
				PickEntry e = results[i];
				Rectangle r = new Rectangle(inner.X + col * (cell + gap), inner.Y + row * (cell + gap) - (int)_pickerScroll, cell, cell);
				bool hover = HubInput.In(r);
				HubKit.Slot(sb, r, hover ? Color.Lerp(Theme.Slot, Color.White, 0.15f) : Theme.Slot);
				Rectangle icon = new Rectangle(r.X + 6, r.Y + 6, r.Width - 12, r.Height - 12);
				if (_picker == Picker.Projectile)
				{
					DrawProjectile(sb, e.Id, icon);
				}
				else if (_picker == Picker.Buff)
				{
					sb.Draw(TextureAssets.Buff[e.Id].Value, icon, Color.White);
				}
				else
				{
					HubIcon.Item(e.Icon).Draw(sb, icon, Color.White, 1f);
				}
				if (hover && HubInput.Hover("itemed:pick:" + e.Id, e.Name + "  #" + e.Id))
				{
					Picked(e.Id);
				}
			}
		}
		HubInput.EndClip(sb);
		if (outer)
		{
			HubInput.BeginClip(sb, outerClip);
		}
		HubKit.RawText(sb, IsaClient.Languages.L10n.F("{0} found", results.Count), x, area.Bottom + 4, Theme.Muted, 0.6f);
		return area.Bottom + 24;
	}

	private static void Picked(int id)
	{
		Picker picker = _picker;
		_picker = Picker.None;
		HubInput.Sound(12);
		if (picker == Picker.Catalog)
		{
			_bench = new Item();
			_bench.SetDefaults(id);
			SetTarget(-2);
			return;
		}
		Item item = Target;
		if (item == null || item.IsAir)
		{
			return;
		}
		Before("pick:" + picker);
		switch (picker)
		{
		case Picker.Projectile:
			item.shoot = id;
			if (item.shootSpeed <= 0f)
			{
				item.shootSpeed = 10f;
			}
			break;
		case Picker.Buff:
			item.buffType = id;
			if (item.buffTime <= 0)
			{
				item.buffTime = 3600;
			}
			break;
		case Picker.NetId:
			EditMark(item).NetId = (id == item.type) ? 0 : id;
			After(item, resend: true);
			return;
		case Picker.PlaceTile:
			if (ContentSamples.ItemsByType.TryGetValue(id, out Item tileItem))
			{
				item.createTile = tileItem.createTile;
				item.placeStyle = tileItem.placeStyle;
				item.useStyle = (item.useStyle == 0) ? 1 : item.useStyle;
				item.consumable = item.consumable || tileItem.consumable;
			}
			break;
		case Picker.PlaceWall:
			if (ContentSamples.ItemsByType.TryGetValue(id, out Item wallItem))
			{
				item.createWall = wallItem.createWall;
				item.useStyle = (item.useStyle == 0) ? 1 : item.useStyle;
			}
			break;
		}
		After(item);
	}

	private static int DrawInventoryPicker(SpriteBatch sb, int x, int y, int width)
	{
		Player p = Main.LocalPlayer;
		const int cell = 48;
		const int gap = 4;
		int columns = 10;
		int gridW = columns * (cell + gap);
		int ox = x + Math.Max(0, (width - gridW) / 2);
		for (int i = 0; i < 58; i++)
		{
			int col = (i < 50) ? (i % 10) : ((i - 50) % 4) + ((i < 54) ? 0 : 4);
			int row = (i < 50) ? (i / 10) : 5;
			int gx = ox + col * (cell + gap) + ((i >= 50) ? ((i < 54) ? 0 : 20) : 0);
			Rectangle r = new Rectangle(gx, y + row * (cell + gap) + ((i >= 50) ? 10 : 0), cell, cell);
			Item item = p.inventory[i];
			bool hover = HubInput.In(r);
			bool on = _source == i;
			HubKit.Slot(sb, r, on ? Color.Lerp(Theme.Slot, Theme.Accent, 0.55f) : (hover ? Color.Lerp(Theme.Slot, Color.White, 0.15f) : Theme.Slot));
			if (!item.IsAir)
			{
				DrawItem(sb, item, new Rectangle(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8), 1f);
				if (hover && HubInput.Hover("itemed:inv:" + i, item.AffixName()))
				{
					SetTarget(i);
					HubInput.Sound(12);
				}
			}
		}
		return y + 6 * (cell + gap) + 20;
	}

	private static int PlaceRow(SpriteBatch sb, string id, string label, string current, int x, int y, int width, Picker picker, Action clear)
	{
		Rectangle r = new Rectangle(x, y, width, 40);
		HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
		HubKit.Text(sb, label, r.X + 12, r.Y + 11, Theme.Muted, 0.66f);
		HubKit.RawText(sb, current ?? IsaClient.Languages.L10n.T("Nothing"), r.X + 150, r.Y + 11, (current != null) ? Color.White : Theme.Muted, 0.72f, width - 340);
		if (HubKit.Button(sb, "itemed:place:" + id, new Rectangle(r.Right - 176, r.Y + 5, 90, 30), "Change", Theme.Slot))
		{
			OpenPicker(picker);
		}
		if (HubKit.Button(sb, "itemed:placeoff:" + id, new Rectangle(r.Right - 82, r.Y + 5, 76, 30), "None", Theme.Slot, null, current != null))
		{
			Item item = Target;
			Before("place:" + id);
			clear();
			After(item);
		}
		return r.Bottom + 6;
	}

	#endregion

	#region Controls

	private struct Choice
	{
		public int Value;

		public HubIcon Icon;

		public string Label;

		public Choice(int value, HubIcon icon, string label)
		{
			Value = value;
			Icon = icon;
			Label = label;
		}
	}

	private static readonly int[] Ammo = { 0, 23, 40, 71, 75, 97, 169, 283, 771, 780, 931, 949, 1261, 1783, 1785, 1836, 3108 };

	private static bool Confirming(string what)
	{
		return _confirm == what && Main.GameUpdateCount - _confirmAt < 180;
	}

	private static bool Ask(string what)
	{
		if (Confirming(what))
		{
			_confirm = null;
			return true;
		}
		_confirm = what;
		_confirmAt = Main.GameUpdateCount;
		HubInput.Sound(12);
		return false;
	}

	private static int Section(SpriteBatch sb, string title, string note, int x, int y, int width)
	{
		HubKit.Text(sb, title, x, y, Color.White, 0.74f);
		if (note != null)
		{
			float w = HubKit.Measure(IsaClient.Languages.L10n.T(title), 0.74f);
			HubKit.Text(sb, note, x + w + 12, y + 3, Theme.Muted, 0.58f, width - w - 12);
		}
		return y + 22;
	}

	/// <summary>
	/// A number: its name, Terraria's slider over the usual range and a box to type any value (up to <paramref name="hardMax"/>).
	/// Returns the y below it.
	/// </summary>
	private static int Number(SpriteBatch sb, string id, string label, int icon, int x, int y, int width, double value, double min, double max, double hardMax, int decimals, Action<double> set, string hint = null, bool compact = false)
	{
		Item item = Target;
		Rectangle r = new Rectangle(x, y, width, 34);
		if (!compact)
		{
			HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
			HubIcon.Item(icon).Draw(sb, new Rectangle(r.X + 7, r.Y + 7, 20, 20), Color.White, 1f);
		}
		int labelX = compact ? r.X : (r.X + 32);
		int labelW = compact ? 56 : Math.Min(170, (int)(width * 0.36f));
		HubKit.Text(sb, label, labelX, r.Y + 9, Theme.Muted, 0.6f, labelW);
		if (hint != null)
		{
			HubKit.RawText(sb, hint, labelX, r.Y + 22, Theme.Accent * 0.9f, 0.48f, labelW);
		}
		const int fieldW = 74;
		Rectangle slider = new Rectangle(labelX + labelW + 6, r.Y + 7, r.Right - (labelX + labelW + 6) - fieldW - 14, 20);
		float f = (float)((Math.Min(max, Math.Max(min, value)) - min) / Math.Max(1e-6, max - min));
		float? moved = HubKit.Slider(sb, "itemed:sl:" + id, slider, f, true, ref ClientHub.SliderDrag);
		if (moved.HasValue && item != null)
		{
			double v = min + moved.Value * (max - min);
			v = Math.Round(v, decimals);
			if (Math.Abs(v - value) > 1e-9)
			{
				Before(id);
				set(v);
				After(item);
			}
		}
		string fieldId = "itemed:num:" + id;
		bool focused = HubInput.Focus == fieldId;
		string shown = focused && _typing.TryGetValue(fieldId, out string typed) ? typed : Format(value, decimals);
		string result = HubKit.Field(sb, fieldId, new Rectangle(r.Right - fieldW - 6, r.Y + 3, fieldW, 28), shown, "", 9);
		if (HubInput.Focus == fieldId)
		{
			_typing[fieldId] = result;
		}
		else if (_typing.TryGetValue(fieldId, out string done))
		{
			_typing.Remove(fieldId);
			if (item != null && double.TryParse(done.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
			{
				v = Math.Round(Math.Max(Math.Min(min, 0), Math.Min(hardMax, v)), decimals);
				Before(id + ":typed");
				set(v);
				After(item);
			}
		}
		return r.Bottom + 4;
	}

	private static string Format(double value, int decimals)
	{
		return (decimals <= 0) ? ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture) : value.ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture);
	}

	private static void Toggle(SpriteBatch sb, string id, string label, string tip, int icon, bool value, Action<bool> set, Rectangle card, bool changesItem = true)
	{
		bool hover = HubInput.In(card);
		float glow = HubInput.Anim("itemed:tg:" + id, hover);
		HubKit.Box(sb, card, Color.Lerp(value ? Color.Lerp(Theme.Slot, Theme.On, 0.12f) : Theme.Slot, Color.White, 0.08f * glow) * 0.95f);
		HubIcon.Item(icon).Draw(sb, new Rectangle(card.X + 7, card.Y + (card.Height - 20) / 2, 20, 20), Color.White * (value ? 1f : 0.6f), 1f);
		HubKit.Text(sb, label, card.X + 32, card.Y + (card.Height - 16) / 2, value ? Color.White : Theme.Muted, 0.62f, card.Width - 86);
		HubKit.Switch(sb, "itemed:tg:" + id, new Rectangle(card.Right - 48, card.Y + (card.Height - 22) / 2, 42, 22), value, hover);
		if (hover && HubInput.Hover("itemed:tgrow:" + id, tip))
		{
			Item item = Target;
			if (changesItem)
			{
				Before(id);
			}
			set(!value);
			if (changesItem)
			{
				After(item);
			}
			HubInput.Sound(value ? 11 : 10);
		}
	}

	/// <summary>Text chips, the current one lit; all options in sight.</summary>
	private static int Chips(SpriteBatch sb, string id, List<(int value, string label)> chips, int current, int x, int y, int width, Action<int> set, Func<int, Color> color = null)
	{
		int cx = x;
		foreach ((int value, string label) in chips)
		{
			int w = (int)HubKit.Measure(label, 0.64f) + 18;
			if (cx + w > x + width)
			{
				cx = x;
				y += 30;
			}
			Rectangle r = new Rectangle(cx, y, w, 26);
			bool on = value == current;
			bool hover = HubInput.In(r);
			HubKit.Box(sb, r, (on ? Color.Lerp(Theme.Slot, Theme.Accent, 0.55f) : (hover ? Theme.CardHover : Theme.Slot)) * 0.95f);
			HubKit.RawText(sb, label, r.X + 9, r.Y + 5, (color != null) ? color(value) : (on ? Color.White : Theme.Muted), 0.64f);
			if (hover && HubInput.Hover("itemed:" + id + ":" + value))
			{
				Item item = Target;
				Before(id);
				set(value);
				After(item);
			}
			cx += w + 4;
		}
		return y + 32;
	}

	/// <summary>Icon cells (name on hover), the current one lit.</summary>
	private static int Icons(SpriteBatch sb, string id, List<Choice> choices, int current, int x, int y, int width, Action<int> set)
	{
		const int cell = 40;
		const int gap = 4;
		int columns = Math.Max(1, (width + gap) / (cell + gap));
		for (int i = 0; i < choices.Count; i++)
		{
			Choice c = choices[i];
			Rectangle r = new Rectangle(x + i % columns * (cell + gap), y + i / columns * (cell + gap), cell, cell);
			bool on = c.Value == current;
			bool hover = HubInput.In(r);
			HubKit.Slot(sb, r, on ? Color.Lerp(Theme.Slot, Theme.Accent, 0.6f) : (hover ? Color.Lerp(Theme.Slot, Color.White, 0.15f) : Theme.Slot));
			c.Icon.Draw(sb, new Rectangle(r.X + 5, r.Y + 5, r.Width - 10, r.Height - 10), Color.White, 1f);
			if (hover && HubInput.Hover("itemed:" + id + ":" + c.Value, c.Label))
			{
				Item item = Target;
				Before(id);
				set(c.Value);
				After(item);
				HubInput.Sound(12);
			}
		}
		return y + (choices.Count + columns - 1) / columns * (cell + gap) + 2;
	}

	/// <summary>Cards with an icon and a name, the current one lit.</summary>
	private static int Cards(SpriteBatch sb, string id, List<Choice> choices, int current, int x, int y, int width, int cardW, Action<int> set)
	{
		int columns = Math.Max(1, (width + 4) / (cardW + 4));
		int w = (width - (columns - 1) * 4) / columns;
		for (int i = 0; i < choices.Count; i++)
		{
			Choice c = choices[i];
			Rectangle r = new Rectangle(x + i % columns * (w + 4), y + i / columns * 36, w, 32);
			bool on = c.Value == current;
			bool hover = HubInput.In(r);
			HubKit.Box(sb, r, (on ? Color.Lerp(Theme.Slot, Theme.Accent, 0.55f) : (hover ? Theme.CardHover : Theme.Slot)) * 0.95f);
			c.Icon.Draw(sb, new Rectangle(r.X + 6, r.Y + 6, 20, 20), Color.White, 1f);
			HubKit.RawText(sb, c.Label, r.X + 30, r.Y + 8, on ? Color.White : Theme.Muted, 0.6f, r.Width - 34);
			if (hover && HubInput.Hover("itemed:" + id + ":" + c.Value, "#" + c.Value))
			{
				Item item = Target;
				Before(id);
				set(c.Value);
				After(item);
				HubInput.Sound(12);
			}
		}
		return y + (choices.Count + columns - 1) / columns * 36;
	}

	private static List<Choice> _holdStyles;

	/// <summary>Every hold style the game has (names from ItemHoldStyleID).</summary>
	private static List<Choice> HoldStyles()
	{
		if (_holdStyles != null)
		{
			return _holdStyles;
		}
		_holdStyles = new List<Choice> { new Choice(0, HubIcon.Item(I.Cog), IsaClient.Languages.L10n.T("None")) };
		foreach (System.Reflection.FieldInfo field in typeof(ItemHoldStyleID).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
		{
			if (field.IsLiteral && field.FieldType == typeof(int))
			{
				int value = (int)field.GetRawConstantValue();
				if (value > 0)
				{
					int icon = value switch
					{
						1 => I.Torch,
						2 => I.Umbrella,
						3 => I.MagicMirror,
						4 => I.GolfClubIron,
						5 => I.Flamethrower,
						6 => I.IvyGuitar,
						_ => I.Cog
					};
					_holdStyles.Add(new Choice(value, HubIcon.Item(icon), System.Text.RegularExpressions.Regex.Replace(field.Name, "(?<=[a-z])(?=[A-Z])", " ")));
				}
			}
		}
		return _holdStyles;
	}

	private static HubIcon StyleIcon(int style)
	{
		int item = style switch
		{
			1 => I.CopperBroadsword,
			2 => I.Apple,
			3 => I.CopperShortsword,
			4 => I.LifeCrystal,
			5 => I.Minishark,
			6 => I.Mug,
			7 => I.GolfClubIron,
			8 => I.LesserHealingPotion,
			9 => I.BottledWater,
			10 => I.InvisibilityPotion,
			11 => I.LawnMower,
			12 => I.IvyGuitar,
			13 => I.Gladius,
			14 => I.MagicLantern,
			_ => I.Cog
		};
		return HubIcon.Item(item);
	}

	private static string SpeedName(int ticks)
	{
		if (ticks <= 8)
		{
			return IsaClient.Languages.L10n.T("Insanely fast");
		}
		if (ticks <= 20)
		{
			return IsaClient.Languages.L10n.T("Very fast");
		}
		if (ticks <= 25)
		{
			return IsaClient.Languages.L10n.T("Fast");
		}
		if (ticks <= 30)
		{
			return IsaClient.Languages.L10n.T("Average");
		}
		if (ticks <= 35)
		{
			return IsaClient.Languages.L10n.T("Slow");
		}
		if (ticks <= 45)
		{
			return IsaClient.Languages.L10n.T("Very slow");
		}
		return IsaClient.Languages.L10n.T("Snail");
	}

	private static string TileName(int tile)
	{
		foreach (KeyValuePair<int, Item> pair in ContentSamples.ItemsByType)
		{
			if (pair.Value.createTile == tile)
			{
				return Lang.GetItemNameValue(pair.Key) + "  (#" + tile + ")";
			}
		}
		return "#" + tile;
	}

	private static string WallName(int wall)
	{
		foreach (KeyValuePair<int, Item> pair in ContentSamples.ItemsByType)
		{
			if (pair.Value.createWall == wall)
			{
				return Lang.GetItemNameValue(pair.Key) + "  (#" + wall + ")";
			}
		}
		return "#" + wall;
	}

	private static void Checker(SpriteBatch sb, Rectangle stage)
	{
		const int check = 10;
		for (int cy = 0; cy < stage.Height; cy += check)
		{
			for (int cx = 0; cx < stage.Width; cx += check)
			{
				bool dark = ((cx / check) + (cy / check)) % 2 == 0;
				HubKit.Fill(sb, new Rectangle(stage.X + cx, stage.Y + cy, Math.Min(check, stage.Width - cx), Math.Min(check, stage.Height - cy)), dark ? new Color(40, 44, 70) : new Color(50, 55, 86));
			}
		}
	}

	/// <summary>An item drawn as the inventory draws it (tag, glow and tint included), fitted in a box.</summary>
	internal static void DrawItem(SpriteBatch sb, Item item, Rectangle box, float maxScale)
	{
		if (item == null || item.IsAir)
		{
			return;
		}
		try
		{
			Main.instance.LoadItem(item.type);
			Texture2D tex = TextureAssets.Item[item.type].Value;
			Rectangle frame = (Main.itemAnimations[item.type] != null) ? Main.itemAnimations[item.type].GetFrame(tex, -1) : tex.Frame();
			float scale = Math.Min(maxScale, Math.Min((box.Width - 4f) / frame.Width, (box.Height - 4f) / frame.Height));
			float limit = Math.Max(frame.Width, frame.Height) * scale;
			ItemSlot.DrawItemIcon(item, 31, sb, new Vector2(box.Center.X, box.Center.Y), scale, limit, Color.White);
		}
		catch
		{
		}
	}

	#endregion
}
