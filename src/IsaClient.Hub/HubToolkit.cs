using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Chat;
using Terraria.UI.Chat;
using I = Terraria.ID.ItemID;

namespace IsaClient.Hub;

/// <summary>
/// My Toolkit: buttons the user makes (client commands and chat lines, run in order) and setups (saved sets of features
/// that are on, applied with one click).
/// </summary>
internal static class HubToolkit
{
	private static HubTheme Theme => HubTheme.Current;

	/// <summary>Icons to pick from for a button (left/right click on the icon), plus "use the held item".</summary>
	private static readonly int[] _icons =
	{
		I.FallenStar, I.Toolbox, I.MagicMirror, I.LifeCrystal, I.ManaCrystal, I.TerraBlade, I.Zenith, I.HermesBoots, I.AngelWings, I.Picksaw,
		I.Drax, I.WorldGlobe, I.SuspiciousLookingEye, I.SlimeCrown, I.GoldCoin, I.PlatinumCoin, I.Chest, I.GoldChest, I.PiggyBank, I.Bed,
		I.Torch, I.UltrabrightTorch, I.MusicBoxOverworldDay, I.Book, I.SpellTome, I.Megaphone, I.Sign, I.TeleportationPotion, I.RecallPotion, I.WormholePotion,
		I.RegenerationPotion, I.SuperHealingPotion, I.Bomb, I.Dynamite, I.PaintRoller, I.RainbowDye, I.Binoculars, I.Stopwatch, I.Cog, I.Wrench
	};

	private static CustomAction _editing;

	private static bool _editingNew;

	private static string _name = "";

	private static string _commands = "";

	private static int _icon = I.FallenStar;

	private static string _profileName = "";

	private static string _confirm;

	private static double _confirmUntil;

	public static void StartNewAction()
	{
		_editing = new CustomAction();
		_editingNew = true;
		_name = "";
		_commands = "";
		_icon = I.FallenStar;
		HubInput.SetFocus("act:name");
	}

	private static void Edit(CustomAction action)
	{
		_editing = action;
		_editingNew = false;
		_name = action.Name;
		_commands = action.Commands;
		_icon = action.Icon;
	}

	/// <summary>Runs every line of the button: lines starting with the command prefix are client commands, the rest go to the chat (server commands too).</summary>
	public static void Run(CustomAction action)
	{
		string[] lines = (action.Commands ?? "").Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string raw in lines)
		{
			string line = raw.Trim();
			if (line.Length == 0)
			{
				continue;
			}
			try
			{
				if (line[0] == ChatCommands.CommandSpecifier)
				{
					ChatCommands.RunLocalCommand(line);
				}
				else if (Main.netMode == 1 || line[0] == '/')
				{
					ChatHelper.SendChatMessageFromClient(ChatManager.Commands.CreateOutgoingMessage(line));
				}
				else
				{
					Main.NewText(IsaClient.Languages.L10n.T(line));
				}
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
				ClientChat.Error("\"" + line + "\" did not work: " + ex.Message);
			}
		}
		HubInput.Sound(12);
	}

	private static bool Confirm(string id)
	{
		if (_confirm == id && Main.timeForVisualEffects < _confirmUntil)
		{
			_confirm = null;
			return true;
		}
		_confirm = id;
		_confirmUntil = Main.timeForVisualEffects + 150.0;
		return false;
	}

	private static bool Confirming(string id)
	{
		return _confirm == id && Main.timeForVisualEffects < _confirmUntil;
	}

	#region My buttons page

	public static int DrawActions(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y += Intro(sb, x, y, width, HubIcon.Item(I.Toolbox), "Your own buttons",
			"A button runs one or more lines, in order. Lines starting with " + ChatCommands.CommandSpecifier + " are IsaClient commands (.tp spawn), the rest go to the chat (/home, /warp shop). Separate lines with ;");
		if (_editing != null)
		{
			y += Editor(sb, x, y, width);
		}
		else if (HubKit.Button(sb, "act:new", new Rectangle(x, y, 180, 32), "New button", Theme.Slot, null, true, HubIcon.Item(I.Toolbox)))
		{
			StartNewAction();
		}
		if (_editing == null)
		{
			y += 42;
		}
		if (HubPrefs.Actions.Count == 0)
		{
			y += ClientHub.EmptyNote(sb, x, y, width, HubIcon.Item(I.FallenStar), "No buttons yet", "Try one with \".fb ; .noclip\" to turn on fullbright and noclip at once.");
			return y - start;
		}
		foreach (CustomAction action in HubPrefs.Actions.ToList())
		{
			y += ActionCard(sb, action, x, y, width);
		}
		return y - start;
	}

	private static int Intro(SpriteBatch sb, int x, int y, int width, HubIcon icon, string title, string text)
	{
		icon.Draw(sb, new Rectangle(x + 2, y + 2, 22, 22), Color.White);
		HubKit.Text(sb, title, x + 30, y + 4, Theme.Accent, 0.92f);
		string[] lines = HubKit.Wrap(text, width - 34, 0.72f, 3);
		for (int i = 0; i < lines.Length; i++)
		{
			HubKit.Text(sb, lines[i], x + 30, y + 28 + i * 18, Theme.Muted, 0.72f);
		}
		return 36 + lines.Length * 18;
	}

	private static int Editor(SpriteBatch sb, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 156);
		HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.Slot, 0.3f) * HubPrefs.Opacity);
		HubKit.Text(sb, _editingNew ? "New button" : "Edit button", r.X + 12, r.Y + 10, Color.White, 0.86f);
		Rectangle iconSlot = new Rectangle(r.X + 12, r.Y + 38, 56, 56);
		HubKit.Slot(sb, iconSlot, Theme.Slot);
		HubIcon.Item(_icon).Draw(sb, new Rectangle(iconSlot.X + 8, iconSlot.Y + 8, 40, 40), Color.White);
		if (HubInput.In(iconSlot))
		{
			HubInput.Hover("act:icon", "Left click: next icon. Right click: previous.");
			if (HubInput.Clicked)
			{
				_icon = NextIcon(_icon, 1);
				HubInput.Sound(12);
			}
			else if (HubInput.RightClicked)
			{
				_icon = NextIcon(_icon, -1);
				HubInput.Sound(12);
				HubInput.Consume();
			}
		}
		Item held = Main.LocalPlayer?.HeldItem;
		bool canUseHeld = held != null && !held.IsAir;
		if (HubKit.Button(sb, "act:held", new Rectangle(iconSlot.X - 2, iconSlot.Bottom + 6, 60, 22), "Held", Theme.Card, "Use the item in your hand as the icon", canUseHeld))
		{
			_icon = held.type;
		}
		int fx = iconSlot.Right + 12;
		int fw = r.Right - 12 - fx;
		HubKit.Text(sb, "Name", fx, r.Y + 36, Theme.Muted, 0.7f);
		_name = HubKit.Field(sb, "act:name", new Rectangle(fx + 70, r.Y + 32, fw - 70, 28), _name, "My button", 32);
		HubKit.Text(sb, "Lines", fx, r.Y + 72, Theme.Muted, 0.7f);
		_commands = HubKit.Field(sb, "act:lines", new Rectangle(fx + 70, r.Y + 68, fw - 70, 28), _commands, ".fb ; .noclip ; /home", 300);
		int by = r.Bottom - 38;
		bool valid = !string.IsNullOrWhiteSpace(_commands);
		if (HubKit.Button(sb, "act:save", new Rectangle(fx + 70, by, 110, 28), "Save", Theme.On * 0.6f, null, valid))
		{
			_editing.Name = string.IsNullOrWhiteSpace(_name) ? "My button" : _name.Trim();
			_editing.Commands = _commands.Trim();
			_editing.Icon = _icon;
			if (_editingNew)
			{
				HubPrefs.Actions.Add(_editing);
			}
			HubPrefs.Changed();
			HubPrefs.Save();
			_editing = null;
			HubInput.Focus = null;
			HubInput.Sound(10);
		}
		if (HubKit.Button(sb, "act:try", new Rectangle(fx + 188, by, 90, 28), "Try it", Theme.Card, "Runs the lines once without saving", valid))
		{
			Run(new CustomAction { Commands = _commands });
		}
		if (HubKit.Button(sb, "act:cancel", new Rectangle(fx + 286, by, 90, 28), "Cancel", Theme.Card))
		{
			_editing = null;
			HubInput.Focus = null;
			HubInput.Sound(11);
		}
		if (!_editingNew)
		{
			bool sure = Confirming("act:delete");
			if (HubKit.Button(sb, "act:delete", new Rectangle(r.Right - 112, by, 100, 28), sure ? "Click again" : "Delete", Theme.Danger * 0.7f) && Confirm("act:delete"))
			{
				HubPrefs.Actions.Remove(_editing);
				HubPrefs.Changed();
				HubPrefs.Save();
				_editing = null;
				HubInput.Sound(11);
			}
		}
		return r.Height + 10;
	}

	private static int NextIcon(int current, int step)
	{
		int index = Array.IndexOf(_icons, current);
		if (index < 0)
		{
			return _icons[0];
		}
		return _icons[(index + step + _icons.Length) % _icons.Length];
	}

	private static int ActionCard(SpriteBatch sb, CustomAction action, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 50);
		string id = "act:" + HubPrefs.Actions.IndexOf(action);
		bool hover = HubInput.In(r);
		float glow = HubInput.Anim(id, hover);
		HubKit.Box(sb, r, Color.Lerp(Theme.Card, Theme.CardHover, glow * 0.8f) * HubPrefs.Opacity);
		Rectangle slot = new Rectangle(r.X + 8, r.Y + 6, 38, 38);
		HubKit.Slot(sb, slot, Theme.Slot);
		HubIcon.Item(action.Icon).Draw(sb, new Rectangle(slot.X + 5, slot.Y + 5, 28, 28), Color.White);
		Rectangle run = new Rectangle(r.Right - 92, r.Y + 11, 82, 28);
		Rectangle edit = new Rectangle(run.X - 78, r.Y + 11, 70, 28);
		HubKit.Text(sb, action.Name, slot.Right + 10, r.Y + 8, Color.White, 0.86f, edit.X - slot.Right - 20);
		HubKit.Text(sb, action.Commands, slot.Right + 10, r.Y + 29, Theme.Muted, 0.7f, edit.X - slot.Right - 20);
		if (HubKit.Button(sb, id + ":edit", edit, "Edit", Theme.Card))
		{
			Edit(action);
		}
		if (HubKit.Button(sb, id + ":run", run, "Run", Color.Lerp(Theme.Slot, Theme.Accent, 0.25f), action.Commands))
		{
			Run(action);
		}
		return r.Height + 8;
	}

	/// <summary>Small tiles of the user's buttons (Home).</summary>
	public static int DrawActionTiles(SpriteBatch sb, int x, int y, int width)
	{
		List<CustomAction> actions = HubPrefs.Actions;
		int count = actions.Count + 1;
		int columns = Math.Max(2, width / 150);
		int gap = 8;
		int w = (width - gap * (columns - 1)) / columns;
		int h = 46;
		for (int i = 0; i < count; i++)
		{
			Rectangle r = new Rectangle(x + i % columns * (w + gap), y + i / columns * (h + gap), w, h);
			if (!HubInput.Visible(r))
			{
				continue;
			}
			bool isNew = i == actions.Count;
			string id = isNew ? "tiles:new" : ("tiles:" + i);
			bool hover = HubInput.In(r);
			string tip = isNew ? "Make your own button" : (actions[i].Commands + "\nRight click: edit");
			bool clicked = hover && HubInput.Hover(id, tip);
			float glow = HubInput.Anim(id, hover);
			HubKit.Box(sb, r, Color.Lerp(isNew ? Theme.Sidebar : Theme.Card, Theme.CardHover, glow) * HubPrefs.Opacity);
			float lift = glow * 2f;
			HubIcon icon = isNew ? HubIcon.Item(I.Toolbox) : HubIcon.Item(actions[i].Icon);
			icon.Draw(sb, new Rectangle(r.X + 8, (int)(r.Y + 8 - lift), 30, 30), Color.White * (isNew ? 0.8f : 1f));
			HubKit.Text(sb, isNew ? "+ New button" : actions[i].Name, r.X + 46, r.Y + 14, isNew ? Theme.Muted : Color.White, 0.8f, r.Width - 54);
			if (clicked)
			{
				if (isNew)
				{
					StartNewAction();
					ClientHub.OpenSection("toolkit");
				}
				else
				{
					Run(actions[i]);
				}
			}
			else if (hover && HubInput.RightClicked && !isNew)
			{
				Edit(actions[i]);
				ClientHub.OpenSection("toolkit");
				HubInput.Consume();
			}
		}
		int rows = (count + columns - 1) / columns;
		return rows * (h + gap) + 8;
	}

	#endregion

	#region Setups page

	public static int DrawProfiles(SpriteBatch sb, int x, int y, int width)
	{
		int start = y;
		y += Intro(sb, x, y, width, HubIcon.Item(I.Book), "Setups",
			"Save which features are on as a setup (building, boss fight, exploring...) and switch to it with one click. Applying a setup turns on its features and turns off the rest.");
		Rectangle field = new Rectangle(x, y, Math.Min(320, width - 200), 30);
		_profileName = HubKit.Field(sb, "profile:name", field, _profileName, "Name for what is on now", 32);
		int active = ClientHub.ActiveFeatures().Count();
		if (HubKit.Button(sb, "profile:save", new Rectangle(field.Right + 8, y, 180, 30), "Save what is on (" + active + ")", Theme.On * 0.55f, "Saves every feature that is on right now", !string.IsNullOrWhiteSpace(_profileName)))
		{
			string name = _profileName.Trim();
			FeatureProfile existing = HubPrefs.Profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
			FeatureProfile profile = existing ?? new FeatureProfile { Name = name };
			profile.Features = ClientHub.ActiveFeatures().Select(f => f.Id).ToList();
			if (existing == null)
			{
				HubPrefs.Profiles.Add(profile);
			}
			HubPrefs.Changed();
			HubPrefs.Save();
			_profileName = "";
			HubInput.Focus = null;
			HubInput.Sound(10);
		}
		y += 42;
		if (HubPrefs.Profiles.Count == 0)
		{
			y += ClientHub.EmptyNote(sb, x, y, width, HubIcon.Item(I.Book), "No setups yet", "Turn on what you like, give it a name above and save it.");
			return y - start;
		}
		foreach (FeatureProfile profile in HubPrefs.Profiles.ToList())
		{
			y += ProfileCard(sb, profile, x, y, width);
		}
		return y - start;
	}

	private static int ProfileCard(SpriteBatch sb, FeatureProfile profile, int x, int y, int width)
	{
		Rectangle r = new Rectangle(x, y, width, 58);
		string id = "profile:" + HubPrefs.Profiles.IndexOf(profile);
		bool hover = HubInput.In(r);
		float glow = HubInput.Anim(id, hover);
		bool current = IsCurrent(profile);
		HubKit.Box(sb, r, Color.Lerp(current ? Color.Lerp(Theme.Card, Theme.On, 0.25f) : Theme.Card, Theme.CardHover, glow * 0.7f) * HubPrefs.Opacity);
		Rectangle slot = new Rectangle(r.X + 8, r.Y + 9, 40, 40);
		HubKit.Slot(sb, slot, Theme.Slot);
		HubFeature first = profile.Features.Select(HubCatalog.Find).FirstOrDefault(f => f != null);
		(first?.Icon ?? HubIcon.Item(I.Book)).Draw(sb, new Rectangle(slot.X + 6, slot.Y + 6, 28, 28), Color.White);
		Rectangle apply = new Rectangle(r.Right - 96, r.Y + 15, 86, 28);
		Rectangle update = new Rectangle(apply.X - 86, r.Y + 15, 78, 28);
		Rectangle delete = new Rectangle(update.X - 86, r.Y + 15, 78, 28);
		float room = delete.X - slot.Right - 20;
		HubKit.Text(sb, profile.Name + (current ? "  (on now)" : ""), slot.Right + 10, r.Y + 9, current ? Theme.On : Color.White, 0.86f, room);
		string list = string.Join(", ", profile.Features.Select(HubCatalog.Find).Where(f => f != null).Select(f => f.Title));
		HubKit.Text(sb, profile.Features.Count + " features: " + (list.Length > 0 ? list : "none"), slot.Right + 10, r.Y + 32, Theme.Muted, 0.7f, room);
		if (HubKit.Button(sb, id + ":apply", apply, "Apply", Theme.On * 0.55f, "Turns these on and everything else off"))
		{
			Apply(profile);
		}
		if (HubKit.Button(sb, id + ":update", update, "Update", Theme.Card, "Saves what is on now into this setup"))
		{
			profile.Features = ClientHub.ActiveFeatures().Select(f => f.Id).ToList();
			HubPrefs.Changed();
			HubInput.Sound(10);
		}
		bool sure = Confirming(id + ":delete");
		if (HubKit.Button(sb, id + ":delete", delete, sure ? "Sure?" : "Delete", Theme.Danger * 0.6f) && Confirm(id + ":delete"))
		{
			HubPrefs.Profiles.Remove(profile);
			HubPrefs.Changed();
			HubInput.Sound(11);
		}
		return r.Height + 8;
	}

	private static bool IsCurrent(FeatureProfile profile)
	{
		HashSet<string> on = new HashSet<string>(ClientHub.ActiveFeatures().Select(f => f.Id));
		return on.SetEquals(profile.Features.Where(id => HubCatalog.Find(id) != null));
	}

	private static void Apply(FeatureProfile profile)
	{
		HashSet<string> wanted = new HashSet<string>(profile.Features);
		foreach (HubFeature feature in HubCatalog.AllFeatures.Where(f => f.Cheat && f.Kind == FeatureKind.Toggle).ToList())
		{
			if (feature.IsOn != wanted.Contains(feature.Id) && !feature.Disabled)
			{
				feature.Primary();
			}
		}
		HubInput.Sound(10);
		ClientChat.Info("Setup \"" + profile.Name + "\" applied.");
	}

	#endregion
}
