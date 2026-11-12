using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Core;
using IsaClient.Gameplay;
using IsaClient.Menu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.Creative;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;

namespace IsaClient.Hub;

/// <summary>
/// The character editor, shown inside the new panel: look (hair, clothes, every color with hue/saturation/lightness,
/// name, difficulty), items (inventory, equipment, dyes, the four storages, filled from a searchable list of every item
/// like the Journey menu) and stats (life, mana, permanent upgrades). It edits a copy: "Apply to me" puts it on your
/// character, "Save" writes the .plr (the old file is kept as .plr.isabak), "Create" makes a new character file.
/// </summary>
internal static class HubCharacterEditor
{
	private enum Target
	{
		Me,
		File,
		New
	}

	private enum Tab
	{
		Look,
		Items,
		Stats
	}

	private static Player _p;

	private static Target _target;

	private static string _path;

	private static bool _returnToCharacterWindow;

	private static Tab _tab;

	private static bool _dirty;

	public static bool IsOpen => _p != null;

	private static HubTheme Theme => HubTheme.Current;

	#region Open / close

	/// <summary>Edits your own character (applied to you in game).</summary>
	public static void OpenMe(bool fromCharacterWindow)
	{
		if (IsaClient.Networking.ServerModes.BlocksTool())
		{
			return;
		}
		if (Main.gameMenu || Main.LocalPlayer == null)
		{
			return;
		}
		Start(Main.LocalPlayer.SafeSerializedClone(), Target.Me, null, fromCharacterWindow);
	}

	/// <summary>Edits a saved character (written back to its .plr).</summary>
	public static void OpenFile(Player player, string path, bool fromCharacterWindow)
	{
		if (IsaClient.Networking.ServerModes.BlocksTool())
		{
			return;
		}
		if (player == null)
		{
			return;
		}
		Start(player.SafeSerializedClone(), Target.File, path, fromCharacterWindow);
	}

	/// <summary>A brand new character (saved as a new .plr).</summary>
	public static void OpenNew(bool fromCharacterWindow)
	{
		if (IsaClient.Networking.ServerModes.BlocksTool())
		{
			return;
		}
		Player p = new Player();
		p.name = "";
		p.difficulty = 0;
		p.savedPerPlayerFieldsThatArentInThePlayerClass = new Player.SavedPlayerDataWithAnnoyingRules();
		try
		{
			CreativePowerManager.Instance.ResetDataForNewPlayer(p);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		RandomLook(p);
		StarterItems(p);
		Start(p, Target.New, null, fromCharacterWindow);
	}

	private static void Start(Player p, Target target, string path, bool fromCharacterWindow)
	{
		_p = p;
		_target = target;
		_path = path;
		_returnToCharacterWindow = fromCharacterWindow;
		_tab = Tab.Look;
		_dirty = false;
		_slotArray = null;
		_slot = -1;
		_storage = 0;
		_color = 0;
		_name = p.name ?? "";
		// The editor lives in the new panel: show it even if the classic panel is the one in use.
		InputState.TildeTerminal = true;
		MenuState.CustomUI2.SetState(CheatPanel.UiStateNewCheatPanel);
		SoundEngine.PlaySound(10);
	}

	/// <summary>Leaves the editor without going anywhere else (the panel was closed, or another section picked).</summary>
	public static void Dismiss()
	{
		_p = null;
	}

	public static void Close()
	{
		_p = null;
		HubInput.Focus = null;
		if (_returnToCharacterWindow && !Main.gameMenu)
		{
			CharacterWindow.Open();
		}
		else
		{
			SoundEngine.PlaySound(11);
		}
	}

	public static string Title
	{
		get
		{
			switch (_target)
			{
			case Target.Me:
				return "Edit " + Main.LocalPlayer.name;
			case Target.New:
				return "New character";
			default:
				return "Edit " + (_p?.name ?? "character");
			}
		}
	}

	public static string Subtitle
	{
		get
		{
			string text = SubtitleText;
			return _dirty ? ("Not saved yet  ·  " + text) : text;
		}
	}

	private static string SubtitleText
	{
		get
		{
			switch (_target)
			{
			case Target.Me:
				return "Changes go on your character when you press Apply (everyone sees them).";
			case Target.New:
				return "Pick a name, a look and some items, then Create.";
			default:
				return System.IO.Path.GetFileName(_path) + "  ·  Save writes the file (the old one is kept as .isabak).";
			}
		}
	}

	#endregion

	#region Frame

	private static string _name = "";

	public static void Draw(SpriteBatch sb, Rectangle body)
	{
		// Tabs and the actions on the right
		int x = body.X;
		string[] tabs = { "Look", "Items", "Stats" };
		int[] icons = { ItemID.FamiliarWig, ItemID.Chest, ItemID.LifeCrystal };
		for (int i = 0; i < tabs.Length; i++)
		{
			Rectangle tab = new Rectangle(x, body.Y + 2, 112, 30);
			bool selected = (int)_tab == i;
			if (HubKit.Button(sb, "editor:tab" + i, tab, tabs[i], selected ? Theme.Slot : Theme.Sidebar, null, true, HubIcon.Item(icons[i])) && !selected)
			{
				_tab = (Tab)i;
				HubInput.Focus = null;
			}
			if (selected)
			{
				HubKit.Fill(sb, new Rectangle(tab.X + 10, tab.Bottom - 3, tab.Width - 20, 2), Theme.Accent);
			}
			x = tab.Right + 6;
		}
		DrawActions(sb, new Rectangle(x + 6, body.Y + 2, body.Right - x - 6, 30));
		Rectangle area = new Rectangle(body.X, body.Y + 40, body.Width, body.Height - 40);
		switch (_tab)
		{
		case Tab.Look:
			DrawLook(sb, area);
			break;
		case Tab.Items:
			DrawItems(sb, area);
			break;
		default:
			DrawStats(sb, area);
			break;
		}
	}

	private static void DrawActions(SpriteBatch sb, Rectangle r)
	{
		int right = r.Right;
		Rectangle back = new Rectangle(right - 90, r.Y, 90, 30);
		if (HubKit.Button(sb, "editor:back", back, _dirty ? "Discard" : "Back", Theme.Card, _dirty ? "Leave without applying the changes" : null))
		{
			Close();
			return;
		}
		right = back.X - 6;
		string main;
		string tip;
		bool enabled = true;
		switch (_target)
		{
		case Target.Me:
			main = "Apply to me";
			tip = "Puts this look, these items and stats on your character now.";
			break;
		case Target.New:
			main = "Create";
			tip = "Saves this character as a new .plr in your Players folder.";
			enabled = !string.IsNullOrWhiteSpace(_name);
			break;
		default:
			main = "Save";
			tip = "Writes the changes to " + System.IO.Path.GetFileName(_path) + " (the old file is kept as .isabak).";
			break;
		}
		Rectangle commit = new Rectangle(right - 130, r.Y, 130, 30);
		if (HubKit.Button(sb, "editor:commit", commit, _dirty ? (main + " *") : main, _dirty ? (Theme.On * 0.8f) : (Theme.On * 0.5f), tip, enabled))
		{
			Commit();
			return;
		}
		right = commit.X - 6;
		if (_target != Target.New)
		{
			Rectangle copy = new Rectangle(right - 120, r.Y, 120, 30);
			if (HubKit.Button(sb, "editor:copy", copy, "Save as new", Theme.Card, "Saves the edited character as a new .plr (nothing is overwritten)."))
			{
				_p.name = (_target == Target.Me) ? Main.LocalPlayer.name : _name;
				CharacterPresets.SaveCopy(_p);
			}
			right = copy.X - 6;
		}
		if (!Main.gameMenu)
		{
			Rectangle send = new Rectangle(right - 120, r.Y, 120, 30);
			if (HubKit.Button(sb, "editor:send", send, "Send in chat", new Color(40, 110, 100), "Puts the character as you see it here in the chat line: IsaClient players get the whole character (with its IsaClient items)."))
			{
				IsaClient.Sharing.ShareChat.LinkPlayer(_p, (_target == Target.Me) ? Main.LocalPlayer.name : _name);
			}
			right = send.X - 6;
		}
	}

	private static void Commit()
	{
		try
		{
			switch (_target)
			{
			case Target.Me:
				CharacterPresets.ApplyToMe(_p, WearParts.All, storages: true);
				ClientChat.Info("Your character was updated.");
				SoundEngine.PlaySound(10);
				_dirty = false;
				_p = Main.LocalPlayer.SafeSerializedClone();
				_slotArray = null;
				_slot = -1;
				break;
			case Target.New:
			{
				_p.name = _name.Trim();
				string path = Main.GetPlayerPathFromName(_p.name, cloudSave: false);
				if (CharacterPresets.WriteFile(_p, path))
				{
					ClientChat.Info("Created " + System.IO.Path.GetFileName(path) + ". You can play it from the character list.");
					SoundEngine.PlaySound(10);
					_dirty = false;
					Close();
				}
				break;
			}
			default:
				_p.name = string.IsNullOrWhiteSpace(_name) ? _p.name : _name.Trim();
				if (CharacterPresets.WriteFile(_p, _path))
				{
					ClientChat.Info("Saved " + System.IO.Path.GetFileName(_path) + ".");
					SoundEngine.PlaySound(10);
					_dirty = false;
				}
				break;
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			ClientChat.Error("Could not save the character: " + ex.Message);
		}
	}

	private static void Changed()
	{
		_dirty = true;
	}

	#endregion

	#region Look

	private static int _color;

	private static readonly string[] _colorNames = { "Hair", "Skin", "Eyes", "Shirt", "Undershirt", "Pants", "Shoes" };

	private static Color GetColor(int index)
	{
		switch (index)
		{
		case 0:
			return _p.hairColor;
		case 1:
			return _p.skinColor;
		case 2:
			return _p.eyeColor;
		case 3:
			return _p.shirtColor;
		case 4:
			return _p.underShirtColor;
		case 5:
			return _p.pantsColor;
		default:
			return _p.shoeColor;
		}
	}

	private static void SetColor(int index, Color color)
	{
		switch (index)
		{
		case 0:
			_p.hairColor = color;
			break;
		case 1:
			_p.skinColor = color;
			break;
		case 2:
			_p.eyeColor = color;
			break;
		case 3:
			_p.shirtColor = color;
			break;
		case 4:
			_p.underShirtColor = color;
			break;
		case 5:
			_p.pantsColor = color;
			break;
		default:
			_p.shoeColor = color;
			break;
		}
		Changed();
	}

	private static void DrawLook(SpriteBatch sb, Rectangle area)
	{
		// Preview
		int previewW = Math.Min(240, area.Width / 3);
		Rectangle preview = new Rectangle(area.X, area.Y, previewW, area.Height - (_target == Target.Me ? 0 : 74));
		HubKit.Box(sb, preview, Theme.Sidebar * HubPrefs.Opacity);
		float scale = MathHelper.Clamp((preview.Height - 40) / 60f, 1.5f, 3.5f);
		HubKit.Portrait(sb, _p, preview, scale);
		if (_target != Target.Me)
		{
			HubKit.Text(sb, "Name", area.X + 2, preview.Bottom + 8, Theme.Muted, 0.72f);
			string before = _name;
			_name = HubKit.Field(sb, "editor:name", new Rectangle(area.X, preview.Bottom + 28, previewW, 30), _name, "Character name", Player.nameLen);
			if (_name != before)
			{
				Changed();
			}
		}

		int x = preview.Right + 16;
		int w = area.Right - x;
		int y = area.Y;
		// Hair and clothes
		y = Row(sb, "Hair", x, y, w, "Style " + (_p.hair + 1), delegate(bool back)
		{
			_p.hair = (_p.hair + (back ? Main.maxHairStyles - 1 : 1)) % Main.maxHairStyles;
			Changed();
		});
		y = Row(sb, "Clothes", x, y, w, ClothesName(_p.skinVariant), delegate(bool back)
		{
			_p.skinVariant = (_p.skinVariant + (back ? PlayerVariantID.Count - 1 : 1)) % PlayerVariantID.Count;
			Changed();
		});
		if (_target != Target.Me)
		{
			y = Row(sb, "Difficulty", x, y, w, DifficultyName(_p.difficulty), delegate(bool back)
			{
				_p.difficulty = (byte)((_p.difficulty + (back ? 3 : 1)) % 4);
				Changed();
			});
		}
		// Colors
		y += 6;
		HubKit.Text(sb, "Colors", x, y, Theme.Accent, 0.86f);
		if (HubKit.Button(sb, "editor:random", new Rectangle(area.Right - 150, y - 4, 150, 26), "Random look", Theme.Card, "A random hair style and colors, like when you create a character.", true, HubIcon.Texture("Images/UI/CharCreation/Randomize")))
		{
			RandomLook(_p);
			Changed();
		}
		y += 28;
		int swatchW = (w - 6 * 6) / 7;
		for (int i = 0; i < _colorNames.Length; i++)
		{
			Rectangle r = new Rectangle(x + i * (swatchW + 6), y, swatchW, 46);
			bool selected = _color == i;
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover("editor:color" + i, _colorNames[i]))
			{
				_color = i;
			}
			HubKit.Box(sb, r, (selected ? Theme.Slot : Theme.Card) * HubPrefs.Opacity);
			HubKit.Fill(sb, new Rectangle(r.X + 8, r.Y + 6, r.Width - 16, 16), GetColor(i));
			HubKit.Fill(sb, new Rectangle(r.X + 8, r.Y + 6, r.Width - 16, 1), Color.Black * 0.4f);
			HubKit.TextCentered(sb, _colorNames[i], new Rectangle(r.X, r.Y + 24, r.Width, 20), selected ? Color.White : Theme.Muted, 0.64f);
			if (selected)
			{
				HubKit.Fill(sb, new Rectangle(r.X + 8, r.Bottom - 3, r.Width - 16, 2), Theme.Accent);
			}
		}
		y += 56;
		Vector3 hsl = Main.rgbToHsl(GetColor(_color));
		y = HslSlider(sb, "Hue", x, y, w, hsl.X, v => SetColor(_color, Main.hslToRgb(v, hsl.Y, hsl.Z)));
		y = HslSlider(sb, "Saturation", x, y, w, hsl.Y, v => SetColor(_color, Main.hslToRgb(hsl.X, v, hsl.Z)));
		y = HslSlider(sb, "Lightness", x, y, w, hsl.Z, v => SetColor(_color, Main.hslToRgb(hsl.X, hsl.Y, v)));
		Color c = GetColor(_color);
		HubKit.Text(sb, _colorNames[_color] + ": #" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"), x, y + 4, Theme.Muted, 0.72f);
		if (HubKit.Button(sb, "editor:randomcolor", new Rectangle(area.Right - 150, y, 150, 26), "Random color", Theme.Card))
		{
			SetColor(_color, Main.hslToRgb(Main.rand.NextFloat(), Main.rand.NextFloat(), 0.15f + Main.rand.NextFloat() * 0.7f));
		}
	}

	private static int Row(SpriteBatch sb, string label, int x, int y, int w, string value, Action<bool> cycle)
	{
		HubKit.Text(sb, label, x, y + 7, Color.White, 0.84f);
		Rectangle box = new Rectangle(x + 120, y, Math.Min(260, w - 120), 30);
		bool hover = HubInput.In(box);
		HubKit.Box(sb, box, Color.Lerp(Theme.Sidebar, Theme.Slot, hover ? 0.6f : 0.3f) * HubPrefs.Opacity);
		HubIcon.Texture("Images/UI/IconPrev").Draw(sb, new Rectangle(box.X + 6, box.Y + 8, 12, 14), Color.White, 1f);
		HubIcon.Texture("Images/UI/IconNext").Draw(sb, new Rectangle(box.Right - 18, box.Y + 8, 12, 14), Color.White, 1f);
		HubKit.TextCentered(sb, value, new Rectangle(box.X + 20, box.Y, box.Width - 40, box.Height), Color.White, 0.8f);
		if (hover)
		{
			if (HubInput.Hover("editor:row:" + label, "Left click: next. Right click: previous."))
			{
				cycle(false);
				SoundEngine.PlaySound(12);
			}
			else if (HubInput.RightClicked)
			{
				cycle(true);
				SoundEngine.PlaySound(12);
				HubInput.Consume();
			}
		}
		return y + 38;
	}

	private static int HslSlider(SpriteBatch sb, string label, int x, int y, int w, float value, Action<float> set)
	{
		HubKit.Text(sb, label, x, y + 2, Theme.Muted, 0.74f);
		float? v = HubKit.Slider(sb, "editor:hsl:" + label, new Rectangle(x + 120, y + 2, w - 130, 16), value, true, ref ClientHub.SliderDrag);
		if (v.HasValue)
		{
			set(v.Value);
		}
		return y + 30;
	}

	private static string ClothesName(int variant)
	{
		string[] names = { "Style 1", "Style 2", "Style 3", "Style 4", "Style 5 (dress)", "Style 6", "Style 7", "Style 8", "Style 9", "Style 10", "Style 11", "Style 12" };
		return (variant >= 0 && variant < names.Length) ? names[variant] : ("Style " + (variant + 1));
	}

	private static string DifficultyName(byte difficulty)
	{
		switch (difficulty)
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

	private static void RandomLook(Player p)
	{
		p.hair = Main.rand.Next(Main.maxHairStyles);
		p.eyeColor = Main.hslToRgb(Main.rand.NextFloat(), Main.rand.NextFloat(), 0.15f + Main.rand.NextFloat() * 0.5f);
		float shade = Math.Min(1f, Main.rand.Next(60, 120) * 0.01f);
		p.skinColor = new Color((int)(Main.rand.Next(240, 255) * shade), (int)(Main.rand.Next(110, 140) * shade), (int)(Main.rand.Next(75, 110) * shade));
		p.hairColor = RandomClothColor();
		p.shirtColor = RandomClothColor();
		p.underShirtColor = RandomClothColor();
		p.pantsColor = RandomClothColor();
		p.shoeColor = RandomClothColor();
		p.skinVariant = Main.rand.Next(PlayerVariantID.Count);
	}

	private static Color RandomClothColor()
	{
		return Main.hslToRgb(Main.rand.NextFloat(), Main.rand.NextFloat(), Main.rand.NextFloat() * 0.85f + 0.15f);
	}

	/// <summary>The copper tools a new character starts with (as in the game's character creation).</summary>
	private static void StarterItems(Player p)
	{
		p.inventory[0].SetDefaults(ItemID.CopperShortsword);
		p.inventory[1].SetDefaults(ItemID.CopperPickaxe);
		p.inventory[2].SetDefaults(ItemID.CopperAxe);
	}

	#endregion

	#region Items

	private static int _storage;

	private static Item[] _slotArray;

	private static int _slot = -1;

	private static int _slotContext;

	private static string _itemSearch = "";

	private static int _category;

	private static bool _maxStacks;

	private static float _browserScroll;

	private static readonly string[] _storageNames = { "Inventory", "Equipment", "Piggy Bank", "Safe", "Forge", "Void Vault" };

	private static readonly int[] _storageIcons = { ItemID.Chest, ItemID.IronChainmail, ItemID.PiggyBank, ItemID.Safe, ItemID.DefendersForge, ItemID.VoidVault };

	private static readonly string[] _categories = { "All", "Weapons", "Tools", "Armor", "Vanity", "Accessories", "Blocks", "Potions", "Ammo", "Other" };

	private static List<int> _allItems;

	private static string _filterKey;

	private static List<int> _filtered = new List<int>();

	private static void DrawItems(SpriteBatch sb, Rectangle area)
	{
		int leftW = Math.Min(10 * 42 + 12, area.Width * 56 / 100);
		Rectangle left = new Rectangle(area.X, area.Y, leftW, area.Height);
		Rectangle right = new Rectangle(left.Right + 12, area.Y, area.Right - left.Right - 12, area.Height);
		DrawSlots(sb, left);
		DrawBrowser(sb, right);
	}

	private static void DrawSlots(SpriteBatch sb, Rectangle area)
	{
		// Storage chips
		int x = area.X;
		for (int i = 0; i < _storageNames.Length; i++)
		{
			Rectangle r = new Rectangle(x, area.Y, 34, 30);
			bool selected = _storage == i;
			if (HubKit.IconButton(sb, "editor:storage" + i, r, HubIcon.Item(_storageIcons[i]), _storageNames[i], selected))
			{
				_storage = i;
				_slotArray = null;
				_slot = -1;
			}
			x = r.Right + 4;
		}
		HubKit.Text(sb, _storageNames[_storage], x + 6, area.Y + 7, Color.White, 0.82f);
		int y = area.Y + 40;
		int size = Math.Min(40, (area.Width - 9 * 2) / 10);
		float gameScale = Main.inventoryScale;
		Main.inventoryScale = size / 52f;
		switch (_storage)
		{
		case 0:
			y = SlotRows(sb, _p.inventory, 0, 0, 50, area.X, y, size, "Inventory");
			y = SlotRows(sb, _p.inventory, 1, 50, 4, area.X, y, size, null, sameRow: true);
			y = SlotRows(sb, _p.inventory, 2, 54, 4, area.X + 5 * (size + 2), y - (size + 2), size, null);
			break;
		case 1:
			y = SlotRows(sb, _p.armor, 8, 0, 10, area.X, y, size, "Armor & accessories");
			y = SlotRows(sb, _p.armor, 9, 10, 10, area.X, y, size, "Vanity");
			y = SlotRows(sb, _p.dye, 12, 0, 10, area.X, y, size, "Dyes");
			y = SlotRows(sb, _p.miscEquips, 16, 0, 5, area.X, y, size, "Pet, light, cart, mount, hook");
			y = SlotRows(sb, _p.miscDyes, 12, 0, 5, area.X + 5 * (size + 2), y - (size + 2), size, null);
			break;
		default:
			Item[] bank = (_storage == 2) ? _p.bank.item : ((_storage == 3) ? _p.bank2.item : ((_storage == 4) ? _p.bank3.item : _p.bank4.item));
			y = SlotRows(sb, bank, 4, 0, 40, area.X, y, size, null);
			break;
		}
		Main.inventoryScale = gameScale;
		y += 8;
		DrawSelected(sb, new Rectangle(area.X, y, area.Width, area.Bottom - y));
	}

	/// <summary>Draws a run of slots ten per row; click selects, right click empties. Returns the y after them.</summary>
	private static int SlotRows(SpriteBatch sb, Item[] items, int context, int first, int count, int x, int y, int size, string label, bool sameRow = false)
	{
		if (label != null)
		{
			HubKit.Text(sb, label, x, y, Theme.Muted, 0.66f);
			y += 16;
		}
		for (int i = 0; i < count; i++)
		{
			int slot = first + i;
			if (slot >= items.Length)
			{
				break;
			}
			Rectangle r = new Rectangle(x + i % 10 * (size + 2), y + i / 10 * (size + 2), size, size);
			bool selected = _slotArray == items && _slot == slot;
			ItemSlot.Draw(sb, items, context, slot, new Vector2(r.X, r.Y));
			if (selected)
			{
				HubKit.Fill(sb, new Rectangle(r.X, r.Y, r.Width, 2), Theme.Accent);
				HubKit.Fill(sb, new Rectangle(r.X, r.Bottom - 2, r.Width, 2), Theme.Accent);
				HubKit.Fill(sb, new Rectangle(r.X, r.Y, 2, r.Height), Theme.Accent);
				HubKit.Fill(sb, new Rectangle(r.Right - 2, r.Y, 2, r.Height), Theme.Accent);
			}
			if (HubInput.In(r))
			{
				Item item = items[slot];
				string tip = (item == null || item.IsAir) ? "Empty: click to choose it, then pick an item on the right." : (item.AffixName() + ((item.stack > 1) ? (" (" + item.stack + ")") : "") + "\nRight click: empty the slot");
				if (HubInput.Hover("editor:slot:" + context + ":" + slot, tip))
				{
					_slotArray = items;
					_slot = slot;
					_slotContext = context;
					SoundEngine.PlaySound(12);
				}
				else if (HubInput.RightClicked)
				{
					items[slot] = new Item();
					Changed();
					SoundEngine.PlaySound(11);
					HubInput.Consume();
				}
			}
		}
		int rows = (count + 9) / 10;
		return sameRow ? (y + size + 2) : (y + rows * (size + 2) + 4);
	}

	private static void DrawSelected(SpriteBatch sb, Rectangle r)
	{
		if (r.Height < 60)
		{
			return;
		}
		HubKit.Box(sb, r, Theme.Card * HubPrefs.Opacity);
		if (_slotArray == null || _slot < 0 || _slot >= _slotArray.Length)
		{
			HubKit.Text(sb, "Click a slot, then pick an item on the right.", r.X + 12, r.Y + 12, Theme.Muted, 0.76f, r.Width - 24);
			HubKit.Text(sb, "Right click a slot to empty it. Shift + click an item: full stack.", r.X + 12, r.Y + 34, Theme.Muted * 0.8f, 0.7f, r.Width - 24);
			return;
		}
		Item item = _slotArray[_slot] ?? new Item();
		Rectangle icon = new Rectangle(r.X + 10, r.Y + 10, 44, 44);
		HubKit.Slot(sb, icon, Theme.Slot);
		if (!item.IsAir)
		{
			HubIcon.Item(item.type).Draw(sb, new Rectangle(icon.X + 6, icon.Y + 6, 32, 32), Color.White);
		}
		HubKit.Text(sb, item.IsAir ? "Empty slot" : item.AffixName(), icon.Right + 10, r.Y + 12, item.IsAir ? Theme.Muted : Color.White, 0.84f, r.Width - 80);
		if (item.IsAir)
		{
			return;
		}
		HubKit.Text(sb, "ID " + item.type + ((item.maxStack > 1) ? ("  ·  stack up to " + item.maxStack) : ""), icon.Right + 10, r.Y + 34, Theme.Muted, 0.68f);
		int y = r.Y + 62;
		int bx = r.X + 10;
		if (item.maxStack > 1)
		{
			HubKit.Text(sb, "Stack " + item.stack, bx, y + 5, Color.White, 0.78f);
			bx += 90;
			if (HubKit.Button(sb, "editor:stack-", new Rectangle(bx, y, 30, 26), "-", Theme.Slot, "Shift: -10"))
			{
				item.stack = Math.Max(1, item.stack - (Main.keyState.IsKeyDown(Keys.LeftShift) ? 10 : 1));
				Changed();
			}
			if (HubKit.Button(sb, "editor:stack+", new Rectangle(bx + 34, y, 30, 26), "+", Theme.Slot, "Shift: +10"))
			{
				item.stack = Math.Min(item.maxStack, item.stack + (Main.keyState.IsKeyDown(Keys.LeftShift) ? 10 : 1));
				Changed();
			}
			if (HubKit.Button(sb, "editor:stackmax", new Rectangle(bx + 68, y, 50, 26), "Max", Theme.Slot))
			{
				item.stack = item.maxStack;
				Changed();
			}
			y += 32;
			bx = r.X + 10;
		}
		if (item.maxStack == 1 && (item.damage > 0 || item.accessory || item.defense > 0))
		{
			HubKit.Text(sb, "Prefix", bx, y + 5, Color.White, 0.78f);
			Rectangle box = new Rectangle(bx + 90, y, Math.Min(220, r.Width - 110), 26);
			bool hover = HubInput.In(box);
			HubKit.Box(sb, box, Color.Lerp(Theme.Sidebar, Theme.Slot, hover ? 0.6f : 0.3f));
			string prefix = (item.prefix > 0) ? Lang.prefix[item.prefix].Value : "None";
			HubIcon.Texture("Images/UI/IconPrev").Draw(sb, new Rectangle(box.X + 5, box.Y + 6, 12, 14), Color.White, 1f);
			HubIcon.Texture("Images/UI/IconNext").Draw(sb, new Rectangle(box.Right - 17, box.Y + 6, 12, 14), Color.White, 1f);
			HubKit.TextCentered(sb, prefix, new Rectangle(box.X + 18, box.Y, box.Width - 36, box.Height), Color.White, 0.76f);
			if (hover)
			{
				if (HubInput.Hover("editor:prefix", "Left click: next prefix. Right click: previous."))
				{
					CyclePrefix(item, 1);
				}
				else if (HubInput.RightClicked)
				{
					CyclePrefix(item, -1);
					HubInput.Consume();
				}
			}
			y += 32;
		}
		if (HubKit.Button(sb, "editor:clear", new Rectangle(r.X + 10, Math.Min(y, r.Bottom - 34), 110, 26), "Empty slot", Theme.Danger * 0.6f))
		{
			_slotArray[_slot] = new Item();
			Changed();
		}
	}

	/// <summary>Next (or previous) prefix this item can really have; 0 = none.</summary>
	private static void CyclePrefix(Item item, int step)
	{
		int current = item.prefix;
		for (int tries = 1; tries < PrefixID.Count; tries++)
		{
			int candidate = ((current + step * tries) % PrefixID.Count + PrefixID.Count) % PrefixID.Count;
			Item test = new Item();
			test.SetDefaults(item.type);
			if (candidate == 0)
			{
				ReplaceSelected(test, item.stack);
				return;
			}
			if (test.Prefix(candidate) && test.prefix == candidate)
			{
				ReplaceSelected(test, item.stack);
				return;
			}
		}
	}

	private static void ReplaceSelected(Item item, int stack)
	{
		item.stack = Math.Max(1, Math.Min(stack, item.maxStack));
		_slotArray[_slot] = item;
		Changed();
		SoundEngine.PlaySound(12);
	}

	private static void DrawBrowser(SpriteBatch sb, Rectangle area)
	{
		HubKit.Box(sb, area, Theme.Sidebar * (HubPrefs.Opacity * 0.8f));
		Rectangle inner = new Rectangle(area.X + 8, area.Y + 8, area.Width - 16, area.Height - 16);
		_itemSearch = HubKit.Field(sb, "editor:itemsearch", new Rectangle(inner.X, inner.Y, inner.Width, 30), _itemSearch, "Search items (name or ID)...", 40, HubIcon.Texture("Images/UI/Cursor_2"));
		int y = inner.Y + 36;
		int x = inner.X;
		for (int i = 0; i < _categories.Length; i++)
		{
			float w = HubKit.Measure(_categories[i], 0.66f) + 16f;
			if (x + w > inner.Right)
			{
				x = inner.X;
				y += 24;
			}
			Rectangle chip = new Rectangle(x, y, (int)w, 20);
			bool selected = _category == i;
			bool hover = HubInput.In(chip);
			HubKit.Box(sb, chip, selected ? Theme.Slot : (hover ? Theme.CardHover : Theme.Card));
			HubKit.TextCentered(sb, _categories[i], chip, selected ? Color.White : Theme.Muted, 0.66f);
			if (hover && HubInput.Hover("editor:cat" + i) && !selected)
			{
				_category = i;
				_browserScroll = 0f;
			}
			x = chip.Right + 4;
		}
		y += 26;
		HubKit.Switch(sb, "editor:maxstack", new Rectangle(inner.X, y, 36, 18), _maxStacks, HubInput.In(new Rectangle(inner.X, y, 150, 18)));
		HubKit.Text(sb, "Full stacks", inner.X + 42, y + 1, Theme.Muted, 0.68f);
		if (HubInput.In(new Rectangle(inner.X, y, 150, 18)) && HubInput.Hover("editor:maxstacktoggle", "New items come as a full stack (Shift + click does it once)."))
		{
			_maxStacks = !_maxStacks;
		}
		List<int> items = Filtered();
		HubKit.Text(sb, items.Count + " items", inner.Right - HubKit.Measure(items.Count + " items", 0.66f), y + 1, Theme.Muted * 0.8f, 0.66f);
		y += 24;
		Rectangle grid = new Rectangle(inner.X, y, inner.Width - 14, inner.Bottom - y);
		int cell = 40;
		int columns = Math.Max(1, (grid.Width + 4) / (cell + 4));
		int rows = (items.Count + columns - 1) / columns;
		float content = rows * (cell + 4);
		if (HubInput.Wheel != 0 && HubInput.In(grid))
		{
			_browserScroll -= HubInput.Wheel / 2f;
			HubInput.Wheel = 0;
		}
		_browserScroll = MathHelper.Clamp(_browserScroll, 0f, Math.Max(0f, content - grid.Height));
		_browserScroll = HubKit.Scrollbar(sb, "editor:browserbar", new Rectangle(grid.Right + 4, grid.Y + 6, 10, grid.Height - 12), _browserScroll, content, grid.Height, ref _browserDrag, ref _browserDragFrom);
		HubInput.BeginClip(sb, grid);
		int firstRow = (int)(_browserScroll / (cell + 4));
		int lastRow = Math.Min(rows - 1, firstRow + grid.Height / (cell + 4) + 1);
		for (int row = firstRow; row <= lastRow; row++)
		{
			for (int col = 0; col < columns; col++)
			{
				int index = row * columns + col;
				if (index >= items.Count)
				{
					break;
				}
				int type = items[index];
				Rectangle r = new Rectangle(grid.X + col * (cell + 4), grid.Y + row * (cell + 4) - (int)_browserScroll, cell, cell);
				bool hover = HubInput.In(r);
				HubKit.Slot(sb, r, (hover ? Theme.CardHover : Theme.Slot) * 0.95f);
				HubIcon.Item(type).Draw(sb, new Rectangle(r.X + 6, r.Y + 6, cell - 12, cell - 12), Color.White, 1.5f);
				if (hover && HubInput.Hover("editor:item:" + type, Lang.GetItemNameValue(type) + "  (ID " + type + ")"))
				{
					PutItem(type);
				}
			}
		}
		HubInput.EndClip(sb);
	}

	private static string _browserDrag;

	private static float _browserDragFrom;

	private static void PutItem(int type)
	{
		if (_slotArray == null || _slot < 0 || _slot >= _slotArray.Length)
		{
			ClientChat.Notice("Click a slot on the left first.");
			return;
		}
		Item item = new Item();
		item.SetDefaults(type);
		bool full = _maxStacks || Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);
		item.stack = full ? item.maxStack : 1;
		_slotArray[_slot] = item;
		Changed();
		SoundEngine.PlaySound(7);
		// Like filling a chest: move on to the next empty slot of the same group.
		for (int next = _slot + 1; next < _slotArray.Length; next++)
		{
			if (_slotArray[next] == null || _slotArray[next].IsAir)
			{
				if (!(_slotArray == _p.inventory && next >= 50))
				{
					_slot = next;
				}
				break;
			}
		}
	}

	private static List<int> Filtered()
	{
		if (_allItems == null)
		{
			_allItems = new List<int>();
			for (int type = 1; type < ItemID.Count; type++)
			{
				if (!ItemID.Sets.Deprecated[type] && ContentSamples.ItemsByType.ContainsKey(type))
				{
					_allItems.Add(type);
				}
			}
		}
		string search = (_itemSearch ?? "").Trim();
		string key = _category + "|" + search;
		if (key == _filterKey)
		{
			return _filtered;
		}
		_filterKey = key;
		int id;
		bool byId = int.TryParse(search, out id);
		_filtered = _allItems.Where(type => InCategory(ContentSamples.ItemsByType[type], _category) && (search.Length == 0 || (byId ? type == id : Lang.GetItemNameValue(type).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))).ToList();
		return _filtered;
	}

	private static bool InCategory(Item item, int category)
	{
		bool tool = item.pick > 0 || item.axe > 0 || item.hammer > 0 || item.fishingPole > 0;
		bool armor = (item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0) && !item.vanity;
		bool block = item.createTile >= 0 || item.createWall >= 0;
		bool potion = item.consumable && (item.healLife > 0 || item.healMana > 0 || item.buffType > 0) && item.damage <= 0;
		bool ammo = item.ammo > 0 && !item.notAmmo;
		bool weapon = item.damage > 0 && !tool && !ammo && !item.accessory;
		switch (category)
		{
		case 1:
			return weapon;
		case 2:
			return tool;
		case 3:
			return armor;
		case 4:
			return item.vanity;
		case 5:
			return item.accessory;
		case 6:
			return block && !weapon && !tool;
		case 7:
			return potion;
		case 8:
			return ammo;
		case 9:
			return !weapon && !tool && !armor && !item.vanity && !item.accessory && !block && !potion && !ammo;
		default:
			return true;
		}
	}

	#endregion

	#region Stats

	private static void DrawStats(SpriteBatch sb, Rectangle area)
	{
		int x = area.X;
		int w = Math.Min(560, area.Width);
		int y = area.Y;
		HubKit.Text(sb, "Life and mana", x, y, Theme.Accent, 0.88f);
		y += 30;
		y = StatSlider(sb, "Max life", x, y, w, _p.statLifeMax, 100, 600, 20, v =>
		{
			_p.statLifeMax = v;
			_p.statLife = Math.Min(Math.Max(_p.statLife, 1), v);
		});
		y = StatSlider(sb, "Max mana", x, y, w, _p.statManaMax, 20, 400, 20, v =>
		{
			_p.statManaMax = v;
			_p.statMana = Math.Min(_p.statMana, v);
		});
		y += 10;
		HubKit.Text(sb, "Permanent upgrades", x, y, Theme.Accent, 0.88f);
		y += 30;
		(string Name, int Icon, Func<bool> Get, Action<bool> Set)[] upgrades =
		{
			("Demon Heart (extra accessory slot)", ItemID.DemonHeart, () => _p.extraAccessory, v => _p.extraAccessory = v),
			("Vital Crystal", ItemID.AegisCrystal, () => _p.usedAegisCrystal, v => _p.usedAegisCrystal = v),
			("Aegis Fruit", ItemID.AegisFruit, () => _p.usedAegisFruit, v => _p.usedAegisFruit = v),
			("Arcane Crystal", ItemID.ArcaneCrystal, () => _p.usedArcaneCrystal, v => _p.usedArcaneCrystal = v),
			("Ambrosia", ItemID.Ambrosia, () => _p.usedAmbrosia, v => _p.usedAmbrosia = v),
			("Gummy Worm", ItemID.GummyWorm, () => _p.usedGummyWorm, v => _p.usedGummyWorm = v),
			("Galaxy Pearl", ItemID.GalaxyPearl, () => _p.usedGalaxyPearl, v => _p.usedGalaxyPearl = v),
			("Artisan Loaf", ItemID.ArtisanLoaf, () => _p.ateArtisanBread, v => _p.ateArtisanBread = v),
			("Torch God's Favor", ItemID.TorchGodsFavor, () => _p.unlockedBiomeTorches, v => _p.unlockedBiomeTorches = v),
			("Minecart Upgrade Kit", ItemID.MinecartPowerup, () => _p.unlockedSuperCart, v => _p.unlockedSuperCart = v)
		};
		int columns = (area.Width >= 640) ? 2 : 1;
		int cardW = (area.Width - 8 * (columns - 1)) / columns;
		for (int i = 0; i < upgrades.Length; i++)
		{
			var upgrade = upgrades[i];
			Rectangle r = new Rectangle(x + i % columns * (cardW + 8), y + i / columns * 46, cardW, 40);
			bool on = upgrade.Get();
			bool hover = HubInput.In(r);
			HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.CardHover, hover ? 0.6f : 0f) * HubPrefs.Opacity);
			HubIcon.Item(upgrade.Icon).Draw(sb, new Rectangle(r.X + 8, r.Y + 6, 28, 28), Color.White);
			HubKit.Text(sb, upgrade.Name, r.X + 44, r.Y + 11, Color.White, 0.78f, r.Width - 110);
			HubKit.Switch(sb, "editor:up" + i, new Rectangle(r.Right - 54, r.Y + 9, 44, 22), on, hover);
			if (hover && HubInput.Hover("editor:upgrade" + i))
			{
				upgrade.Set(!on);
				Changed();
				SoundEngine.PlaySound(on ? 11 : 10);
			}
		}
	}

	private static int StatSlider(SpriteBatch sb, string label, int x, int y, int w, int value, int min, int max, int step, Action<int> set)
	{
		HubKit.Text(sb, label, x, y + 2, Color.White, 0.8f);
		HubKit.Text(sb, value.ToString(), x + w - HubKit.Measure(value.ToString(), 0.8f), y + 2, Theme.Accent, 0.8f);
		float? v = HubKit.Slider(sb, "editor:stat:" + label, new Rectangle(x + 120, y + 4, w - 180, 16), (float)(value - min) / (max - min), true, ref ClientHub.SliderDrag);
		if (v.HasValue)
		{
			int next = min + (int)Math.Round(v.Value * (max - min) / step) * step;
			if (next != value)
			{
				set(next);
				Changed();
			}
		}
		return y + 34;
	}

	#endregion
}
