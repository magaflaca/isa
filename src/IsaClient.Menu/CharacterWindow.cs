using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using IsaClient.Community;
using IsaClient.Core;
using IsaClient.Gameplay;
using IsaClient.MultiClient;
using Terraria;

namespace IsaClient.Menu;

/// <summary>
/// "My Character": opened from your own row of the Player List. Pick any of your saved characters (or one dropped as a
/// .plr), see it, and wear any parts of it in game without changing your name or reconnecting; restore yourself; paste a
/// character as pixel art (with its options); save a copy of yourself; choose whether IsaClient users see you as one.
/// Drawn in immediate mode, laid out from the window size so nothing overflows.
/// </summary>
public class CharacterWindow : UIState
{
	private sealed class Entry
	{
		public string Path;

		public Player Player;

		public Player Preview;

		public bool Dropped;
	}

	private static CharacterWindow _instance;

	private static readonly List<Entry> _dropped = new List<Entry>();

	private List<Entry> _entries = new List<Entry>();

	private int _selected = -1;

	private float _scroll;

	private Player _me;

	private int _meRefresh;

	private static WearParts _parts = WearParts.All;

	private UIElement _area;

	public static bool MousedOver;

	/// <summary>The window shows the buff picker instead of the characters.</summary>
	private bool _buffView;

	public static CharacterWindow Instance => _instance ?? (_instance = new CharacterWindow());

	public static void Open()
	{
		if (IsaClient.Networking.ServerModes.BlocksTool())
		{
			return;
		}
		Instance.Reload();
		MenuState.CustomUI2.SetState(Instance);
		SoundEngine.PlaySound(10);
	}

	/// <summary>Opens straight on the buff picker (from the new panel); Back returns to the panel.</summary>
	public static void OpenBuffs()
	{
		Open();
		Instance._buffView = true;
		Instance._buffsOnly = true;
		BuffPicker.Reset();
	}

	/// <summary>The buff picker was opened on its own: Back goes to the panel, not to My Character.</summary>
	private bool _buffsOnly;

	public static void Reset()
	{
		_instance = null;
	}

	/// <summary>Remembers a dropped .plr so it can be picked here too (this session only).</summary>
	public static void AddDropped(string path, Player player)
	{
		_dropped.RemoveAll((Entry e) => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
		_dropped.Insert(0, new Entry
		{
			Path = path,
			Player = player,
			Preview = player.SafeSerializedClone(),
			Dropped = true
		});
		_instance?.Reload();
	}

	private void Reload()
	{
		MultiClientManager.RefreshCharacters();
		List<Entry> list = new List<Entry>(_dropped);
		foreach (CharacterEntry c in MultiClientManager.Characters)
		{
			if (c.Player == null)
			{
				continue;
			}
			list.Add(new Entry
			{
				Path = c.Path,
				Player = c.Player,
				Preview = c.Player.SafeSerializedClone()
			});
		}
		_entries = list;
		_selected = Utils.Clamp(_selected, -1, _entries.Count - 1);
	}

	public override void OnInitialize()
	{
		UIElement outer = new UIElement();
		outer.Width.Set(0f, 0.875f);
		outer.MaxWidth.Set(900f, 0f);
		outer.MinWidth.Set(640f, 0f);
		outer.Top.Set(120f, 0f);
		outer.Height.Set(-120f, 1f);
		outer.HAlign = 0.5f;
		Append(outer);
		UIPanel panel = new UIPanel();
		panel.Width.Set(0f, 1f);
		panel.Height.Set(-90f, 1f);
		panel.BackgroundColor = new Color(33, 43, 79) * 0.8f;
		outer.Append(panel);
		_area = panel;
	}

	public override void ScrollWheel(UIScrollWheelEvent evt)
	{
		base.ScrollWheel(evt);
		if (_buffView)
		{
			BuffPicker.Scroll -= evt.ScrollWheelValue / 2f;
			return;
		}
		_scroll -= evt.ScrollWheelValue / 2f;
	}

	public override void Update(GameTime gameTime)
	{
		MousedOver = false;
		base.Update(gameTime);
		if (_area != null && _area.IsMouseHovering)
		{
			Main.LocalPlayer.mouseInterface = true;
			MousedOver = true;
		}
		if (--_meRefresh <= 0 || _me == null)
		{
			_meRefresh = 30;
			try
			{
				_me = Main.LocalPlayer.SafeSerializedClone();
			}
			catch
			{
			}
		}
	}

	public override void Draw(SpriteBatch sb)
	{
		base.Draw(sb);
		try
		{
			DrawContents(sb);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		UiKit.EndFrame();
	}

	private Entry Selected => (_selected >= 0 && _selected < _entries.Count) ? _entries[_selected] : null;

	private void DrawContents(SpriteBatch sb)
	{
		CalculatedStyle d = _area.GetInnerDimensions();
		Rectangle area = new Rectangle((int)d.X, (int)d.Y, (int)d.Width, (int)d.Height);
		if (_buffView)
		{
			Utils.DrawBorderStringBig(sb, IsaClient.Languages.L10n.T("Buffs"), new Vector2(area.X + 4, area.Y + 2), new Color(255, 231, 69), 0.5f);
			if (UiKit.Button(sb, new Rectangle(area.Right - 90, area.Y, 90, 30), "Back", UiKit.Blue, enabled: true, _buffsOnly ? "Back to the panel" : "Back to My Character"))
			{
				_buffView = false;
				BuffPicker.Reset();
				SoundEngine.PlaySound(11);
				if (_buffsOnly)
				{
					_buffsOnly = false;
					MenuState.CustomUI2.SetState(CheatPanel.UiStateNewCheatPanel);
				}
				return;
			}
			BuffPicker.Draw(sb, new Rectangle(area.X, area.Y + 40, area.Width, area.Height - 40));
			return;
		}
		// Title and top buttons.
		Utils.DrawBorderStringBig(sb, IsaClient.Languages.L10n.T("My Character"), new Vector2(area.X + 4, area.Y + 2), new Color(255, 231, 69), 0.5f);
		if (UiKit.Button(sb, new Rectangle(area.Right - 90, area.Y, 90, 30), "Back", UiKit.Blue))
		{
			MenuState.CustomUI2.SetState(CheatPanel.UiStateNewCheatPanel);
			SoundEngine.PlaySound(11);
			return;
		}
		if (UiKit.Button(sb, new Rectangle(area.Right - 90 - 8 - 150, area.Y, 150, 30), "View my inventory", UiKit.Blue))
		{
			InventoryViewer.Open(Main.myPlayer);
			return;
		}
		int top = area.Y + 40;
		int listWidth = Math.Min(300, area.Width * 2 / 5);
		Rectangle list = new Rectangle(area.X, top, listWidth, area.Bottom - top);
		Rectangle right = new Rectangle(list.Right + 12, top, area.Right - list.Right - 12, area.Bottom - top);
		DrawList(sb, list);
		DrawDetails(sb, right);
		DrawGhostMenu(sb);
	}

	private void DrawList(SpriteBatch sb, Rectangle box)
	{
		UiKit.Text(sb, "Your characters", box.X + 4, box.Y + 10, Color.White, 0.85f, box.Width - 90);
		if (UiKit.Button(sb, new Rectangle(box.Right - 80, box.Y, 80, 24), "Refresh", UiKit.Blue, enabled: true, "Reads your Players folder again. Drop a .plr on the window to add one."))
		{
			Reload();
		}
		Rectangle view = new Rectangle(box.X, box.Y + 30, box.Width, box.Height - 30);
		UiKit.Panel(sb, view, new Color(20, 26, 50) * 0.75f);
		const int rowHeight = 52;
		const int gap = 4;
		float content = (_entries.Count + 1) * (rowHeight + gap);
		float maxScroll = Math.Max(0f, content - (view.Height - 8));
		_scroll = MathHelper.Clamp(_scroll, 0f, maxScroll);
		Rectangle clip = new Rectangle(view.X + 4, view.Y + 4, view.Width - 8, view.Height - 8);
		RasterizerState oldRaster = sb.GraphicsDevice.RasterizerState;
		Rectangle oldScissor = sb.GraphicsDevice.ScissorRectangle;
		sb.End();
		sb.GraphicsDevice.ScissorRectangle = ToScissor(clip, oldScissor);
		RasterizerState clipRaster = new RasterizerState
		{
			CullMode = CullMode.None,
			ScissorTestEnable = true
		};
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.SamplerStateForCursor, DepthStencilState.None, clipRaster, null, Main.UIScaleMatrix);
		bool mouseInClip = clip.Contains(UiKit.Mouse);
		float y = clip.Y - _scroll;
		for (int i = -1; i < _entries.Count; i++)
		{
			Rectangle row = new Rectangle(clip.X, (int)y, clip.Width, rowHeight);
			y += rowHeight + gap;
			if (row.Bottom < clip.Top || row.Top > clip.Bottom)
			{
				continue;
			}
			Entry entry = (i >= 0) ? _entries[i] : null;
			bool selected = i == _selected;
			bool hover = mouseInClip && row.Contains(UiKit.Mouse);
			Color back = selected ? new Color(90, 110, 190) : (hover ? new Color(70, 88, 160) : new Color(50, 62, 115));
			UiKit.Panel(sb, row, back * 0.92f);
			Player shown = (entry != null) ? entry.Preview : _me;
			// Portraits need an immediate sprite batch; the scissor still applies.
			DrawPortraitClipped(sb, shown, new Rectangle(row.X + 4, row.Y + 2, 44, 48), clipRaster);
			string name = (entry != null) ? entry.Player.name : "You (current)";
			if (entry != null && entry.Dropped)
			{
				name += "  [c/C8A0FF:(dropped)]";
			}
			UiKit.Text(sb, name, row.X + 54, row.Y + 16, Color.White, 0.85f, row.Width - 60);
			UiKit.Text(sb, (entry != null) ? UiKit.Summary(entry.Player) : (CharacterPresets.Wearing ? ("[c/FFE645:wearing " + CharacterPresets.WornName + "]") : UiKit.Summary(Main.LocalPlayer)), row.X + 54, row.Y + 37, Color.White, 0.7f, row.Width - 60);
			if (hover)
			{
				Main.LocalPlayer.mouseInterface = true;
				if (UiKit.Clicked)
				{
					Main.mouseLeftRelease = false;
					_selected = i;
					SoundEngine.PlaySound(12);
				}
			}
		}
		sb.End();
		sb.GraphicsDevice.ScissorRectangle = oldScissor;
		sb.GraphicsDevice.RasterizerState = oldRaster;
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.SamplerStateForCursor, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
	}

	private static void DrawPortraitClipped(SpriteBatch sb, Player p, Rectangle box, RasterizerState clipRaster)
	{
		if (p == null)
		{
			return;
		}
		try
		{
			p.isaAlwaysLit = true;
			p.socialIgnoreLight = true;
			p.direction = 1;
			p.gravDir = 1f;
			p.ResetEffects();
			p.ResetVisibleAccessories();
			p.UpdateMiscCounter();
			p.UpdateDyes();
			p.PlayerFrame();
			p.bodyFrame.Y = (p.legFrame.Y = (p.headFrame.Y = 0));
			p.isaAlwaysLit = true;
			int held = p.selectedItem;
			Item heldItem = p.inventory[held];
			p.inventory[held] = new Item();
			sb.End();
			sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, clipRaster, null, Main.UIScaleMatrix);
			Vector2 topLeft = new Vector2((float)box.Center.X - (float)p.width / 2f, (float)box.Center.Y - (float)p.height / 2f);
			Main.PlayerRenderer.DrawPlayer(Main.Camera, p, topLeft + Main.screenPosition, 0f, Vector2.Zero, 0f, 1f);
			p.inventory[held] = heldItem;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		finally
		{
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.SamplerStateForCursor, DepthStencilState.None, clipRaster, null, Main.UIScaleMatrix);
		}
	}

	#region Your difficulty, life and mana (clickable)

	private static readonly object _editMarker = new object();

	private string _editing;

	private string _editText = "";

	private bool _ghostMenu;

	private Rectangle _ghostAnchor;

	/// <summary>"Classic  500 life  200 mana" for you: the difficulty opens ghost mode, life and mana can be typed in.</summary>
	private void DrawMyStats(SpriteBatch sb, float x, float centerY)
	{
		Player p = Main.LocalPlayer;
		const float scale = 0.8f;
		string difficulty = p.difficulty switch
		{
			1 => "Mediumcore",
			2 => "Hardcore",
			3 => "Journey",
			_ => "Classic"
		};
		if (p.ghost)
		{
			difficulty += " (ghost)";
		}
		if (Chip(sb, difficulty, x, centerY, scale, p.ghost ? new Color(190, 200, 255) : Color.White, "Click: ghost mode", out var diffRect))
		{
			_ghostMenu = !_ghostMenu;
			_ghostAnchor = diffRect;
			_editing = null;
		}
		x = diffRect.Right + 8;
		x = StatChip(sb, "life", p.statLifeMax, new Color(255, 110, 110), x, centerY, scale);
		x = StatChip(sb, "mana", p.statManaMax, new Color(110, 150, 255), x, centerY, scale);
		if (UiKit.Button(sb, new Rectangle((int)x, (int)centerY - 12, 74, 24), "Buffs", UiKit.Green, enabled: true, "Give yourself buffs: hover one to see what it does, right click for infinite, left click to choose the time."))
		{
			_buffView = true;
			_buffsOnly = false;
			_editing = null;
			_ghostMenu = false;
			BuffPicker.Reset();
		}
	}

	/// <summary>Text that highlights on hover and can be clicked.</summary>
	private static bool Chip(SpriteBatch sb, string text, float x, float centerY, float scale, Color color, string tooltip, out Rectangle rect)
	{
		float width = UiKit.Measure(text, scale);
		rect = new Rectangle((int)x - 4, (int)(centerY - 12f), (int)width + 8, 24);
		bool hover = rect.Contains(UiKit.Mouse);
		if (hover)
		{
			Utils.DrawInvBG(sb, rect, new Color(90, 110, 190) * 0.75f);
			Main.LocalPlayer.mouseInterface = true;
			Main.instance.MouseText(IsaClient.Languages.L10n.T(tooltip), 0, 0);
		}
		else
		{
			sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(rect.X + 4, rect.Bottom - 4, rect.Width - 8, 1), color * 0.35f);
		}
		UiKit.Text(sb, text, x, centerY, color, scale);
		if (hover && UiKit.Clicked)
		{
			Main.mouseLeftRelease = false;
			SoundEngine.PlaySound(12);
			return true;
		}
		return false;
	}

	/// <summary>"500 life": click it to type a new max value (Enter applies, Esc or clicking elsewhere cancels).</summary>
	private float StatChip(SpriteBatch sb, string stat, int value, Color color, float x, float centerY, float scale)
	{
		if (_editing == stat)
		{
			Rectangle field = new Rectangle((int)x - 4, (int)(centerY - 12f), (int)UiKit.Measure("00000 " + stat, scale) + 22, 24);
			Utils.DrawInvBG(sb, field, new Color(20, 26, 50) * 0.95f);
			PlayerInput.WritingText = true;
			Main.CurrentInputTextTakerOverride = _editMarker;
			Main.chatRelease = false;
			Main.instance.HandleIME();
			string typed = Main.GetInputText(_editText);
			_editText = new string(typed.Where(char.IsDigit).Take(5).ToArray());
			string caret = (Main.GameUpdateCount / 30 % 2 == 0) ? "|" : " ";
			UiKit.Text(sb, _editText + caret + " " + stat, x + 2, centerY, color, scale);
			if (field.Contains(UiKit.Mouse))
			{
				Main.LocalPlayer.mouseInterface = true;
				Main.instance.MouseText(IsaClient.Languages.L10n.T("Type your new max " + stat + ", then Enter (Esc cancels)"), 0, 0);
			}
			if (Main.inputTextEnter)
			{
				ApplyStat(stat);
				_editing = null;
			}
			else if (Main.inputTextEscape)
			{
				Main.inputTextEscape = false;
				_editing = null;
			}
			else if (UiKit.Clicked && !field.Contains(UiKit.Mouse))
			{
				_editing = null;
			}
			return field.Right + 8;
		}
		if (Chip(sb, value + " " + stat, x, centerY, scale, color, "Click to set your max " + stat, out var rect))
		{
			_editing = stat;
			_editText = value.ToString();
			_ghostMenu = false;
			Main.clrInput();
		}
		return rect.Right + 8;
	}

	private void ApplyStat(string stat)
	{
		if (!int.TryParse(_editText, out var value))
		{
			return;
		}
		Player p = Main.LocalPlayer;
		if (stat == "life")
		{
			value = Utils.Clamp(value, 1, 32767);
			p.statLifeMax = value;
			p.statLife = value;
			if (Main.netMode == 1)
			{
				NetMessage.SendData(16, -1, -1, null, Main.myPlayer);
			}
		}
		else
		{
			value = Utils.Clamp(value, 0, 32767);
			p.statManaMax = value;
			p.statMana = value;
			if (Main.netMode == 1)
			{
				NetMessage.SendData(42, -1, -1, null, Main.myPlayer);
			}
		}
		SoundEngine.PlaySound(10);
		ClientChat.Info("Max " + stat + " set to " + value + ".");
	}

	private void DrawGhostMenu(SpriteBatch sb)
	{
		if (!_ghostMenu)
		{
			return;
		}
		Rectangle r = new Rectangle(_ghostAnchor.X, _ghostAnchor.Bottom + 4, 230, 74);
		Utils.DrawInvBG(sb, r, new Color(26, 34, 66) * 0.97f);
		UiKit.Text(sb, "Ghost mode (.ghost)", r.X + 10, r.Y + 15, UiKit.Muted, 0.75f, r.Width - 20);
		bool inside = r.Contains(UiKit.Mouse);
		if (inside)
		{
			Main.LocalPlayer.mouseInterface = true;
		}
		if (UiKit.Toggle(sb, new Rectangle(r.X + 8, r.Y + 32, r.Width - 16, 32), "Ghost", Main.LocalPlayer.ghost, "Fly through everything, unseen by monsters. Turn it off to return to normal."))
		{
			PlayerTools.ToggleGhost();
			_ghostMenu = false;
		}
		else if (!inside && UiKit.Clicked)
		{
			_ghostMenu = false;
		}
	}

	#endregion

	private void DrawDetails(SpriteBatch sb, Rectangle box)
	{
		Entry entry = Selected;
		Player shown = (entry != null) ? entry.Preview : _me;
		string title = (entry != null) ? entry.Player.name : "You";
		// Fixed-height controls below, the preview takes what is left (so nothing ever spills out of the window).
		const int header = 20;
		const int controlsHeight = header + 30 + 12 + header + 28 + 6 + 32 + 12 + header + 32 + 6 + 28 + 12 + header + 30 + 12 + header + 30;
		int previewHeight = Utils.Clamp(box.Height - controlsHeight - 12, 70, 170);
		int previewWidth = Math.Min(Math.Max(previewHeight - 20, 70), box.Width / 3);
		Rectangle preview = new Rectangle(box.X, box.Y, previewWidth, previewHeight);
		float portraitScale = (previewHeight >= 140) ? 2f : ((previewHeight >= 100) ? 1.5f : 1f);
		UiKit.Portrait(sb, shown, preview, portraitScale, new Color(20, 26, 50) * 0.85f);
		float tx = preview.Right + 12;
		float tw = box.Right - tx;
		// Beside a small preview (a short window) the lines sit closer and the hint goes, so they never reach the buttons.
		bool tight = previewHeight < 100;
		int lineGap = tight ? 20 : 24;
		int top = tight ? 8 : 12;
		UiKit.Text(sb, title, tx, box.Y + top, Color.White, tight ? 0.9f : 1f, tw);
		if (entry != null)
		{
			UiKit.Text(sb, UiKit.Summary(entry.Player), tx, box.Y + top + lineGap + 2, Color.White, 0.8f, tw);
		}
		else
		{
			DrawMyStats(sb, tx, box.Y + top + lineGap + 2);
		}
		string status = CharacterPresets.Wearing ? ("[c/FFE645:You are wearing " + CharacterPresets.WornName + ".]") : "[c/9AA7BE:You are wearing your own character.]";
		UiKit.Text(sb, status, tx, box.Y + top + lineGap * 2 + 2, Color.White, 0.75f, tw);
		if (!tight)
		{
			if (entry == null)
			{
				UiKit.Text(sb, "[c/9AA7BE:Pick a character on the left to wear it.]", tx, box.Y + 84, Color.White, 0.72f, tw);
			}
			else if (entry.Dropped)
			{
				UiKit.Text(sb, "[c/9AA7BE:" + Path.GetFileName(entry.Path) + "]", tx, box.Y + 84, Color.White, 0.7f, tw);
			}
		}
		int y = preview.Bottom + 12;
		int half = (box.Width - 8) / 2;
		int third = (box.Width - 16) / 3;
		int quarter = (box.Width - 24) / 4;
		Color sectionColor = new Color(170, 185, 215);

		// Editor: look, items and stats, like character creation.
		UiKit.Text(sb, "Edit (look, items, stats)", box.X, y + 8, sectionColor, 0.8f, box.Width);
		y += header;
		if (UiKit.Button(sb, new Rectangle(box.X, y, third, 30), "Edit selected", UiKit.Purple, entry != null, (entry == null) ? "Pick a character first." : ("Change " + entry.Player.name + "'s look, items and stats and save the file.")))
		{
			IsaClient.Hub.HubCharacterEditor.OpenFile(entry.Player, entry.Path, fromCharacterWindow: true);
		}
		if (UiKit.Button(sb, new Rectangle(box.X + third + 8, y, third, 30), "Edit me", UiKit.Purple, !Main.gameMenu, "Change your own look, items and stats right now."))
		{
			IsaClient.Hub.HubCharacterEditor.OpenMe(fromCharacterWindow: true);
		}
		if (UiKit.Button(sb, new Rectangle(box.X + 2 * (third + 8), y, third, 30), "New character", UiKit.Green, enabled: true, "Make a new character file with any look and items."))
		{
			IsaClient.Hub.HubCharacterEditor.OpenNew(fromCharacterWindow: true);
		}
		y += 30 + 12;

		// Wear: which parts.
		UiKit.Text(sb, "Wear (your name never changes)", box.X, y + 8, sectionColor, 0.8f, box.Width);
		y += header;
		WearParts[] parts = new WearParts[4] { WearParts.Look, WearParts.Inventory, WearParts.Equipment, WearParts.Stats };
		string[] partNames = new string[4] { "Look", "Inventory", "Equipment", "Life & mana" };
		string[] partTips = new string[4] { "Hair, skin and colors.", "Inventory, coins and ammo (not the item on your cursor).", "Armor, accessories, vanity, dyes, equipment slots and all three loadouts.", "Max life and mana and permanent upgrades (extra slot, Aegis fruit, Artisan loaf...)." };
		for (int i = 0; i < 4; i++)
		{
			bool on = (_parts & parts[i]) != 0;
			if (UiKit.Toggle(sb, new Rectangle(box.X + i * (quarter + 8), y, quarter, 28), partNames[i], on, partTips[i]))
			{
				_parts ^= parts[i];
			}
		}
		y += 28 + 6;
		bool canWear = entry != null && _parts != WearParts.None && !Main.gameMenu;
		if (UiKit.Button(sb, new Rectangle(box.X, y, half, 32), "Wear selected", UiKit.Green, canWear, (entry == null) ? "Pick a character first." : ("Wear " + CharacterPresets.Describe(_parts) + " of " + entry.Player.name + ".")))
		{
			CharacterPresets.Wear(entry.Player, _parts, entry.Player.name);
		}
		if (UiKit.Button(sb, new Rectangle(box.X + half + 8, y, half, 32), "Restore my character", UiKit.Amber, CharacterPresets.Wearing, "Puts your own look, inventory, equipment, life and mana back."))
		{
			CharacterPresets.Restore();
		}
		y += 32 + 12;

		// Pixel art.
		UiKit.Text(sb, "Pixel art (paste a character made of real tiles)", box.X, y + 8, sectionColor, 0.8f, box.Width);
		y += header;
		if (UiKit.Button(sb, new Rectangle(box.X, y, half, 32), "Paste selected", UiKit.Purple, entry != null && !Main.gameMenu, "Builds the selected character out of tiles and starts a paste."))
		{
			CharacterPresets.RequestPixelArt(entry.Player, entry.Player.name);
		}
		if (UiKit.Button(sb, new Rectangle(box.X + half + 8, y, half, 32), "Paste me", UiKit.Purple, !Main.gameMenu, "Builds your current character out of tiles and starts a paste."))
		{
			CharacterPresets.RequestPixelArt(Main.LocalPlayer, Main.LocalPlayer.name);
		}
		y += 32 + 6;
		if (UiKit.Button(sb, new Rectangle(box.X, y, third, 28), "Size: x" + CharacterPresets.ArtScale, UiKit.Blue, enabled: true, "Tiles per pixel of the character's sprite (x1 = its real size, about 20x42 tiles)."))
		{
			CharacterPresets.ArtScale = (CharacterPresets.ArtScale % 4) + 1;
			ClientSettings.Save();
		}
		if (UiKit.Toggle(sb, new Rectangle(box.X + third + 8, y, third, 28), "Walk-through", CharacterPresets.ArtWalkThrough, "The outline is always made of blocks (walls and blocks may be used inside).\nOn: blocks are left deactivated with an actuator (placed, toggled and removed), so you can walk through.\nOff: solid blocks. The game draws deactivated blocks at 40% brightness."))
		{
			CharacterPresets.ArtWalkThrough = !CharacterPresets.ArtWalkThrough;
			ClientSettings.Save();
		}
		if (UiKit.Toggle(sb, new Rectangle(box.X + 2 * (third + 8), y, third, 28), "Glow", CharacterPresets.ArtGlow, "Illuminant Coating on every tile: the art stays visible in the dark."))
		{
			CharacterPresets.ArtGlow = !CharacterPresets.ArtGlow;
			ClientSettings.Save();
		}
		y += 28 + 12;

		// Share in chat: IsaClient players get the character (or just its look) under your message.
		UiKit.Text(sb, "Send in chat (to IsaClient players)", box.X, y + 8, sectionColor, 0.8f, box.Width);
		y += header;
		Color share = new Color(40, 110, 100);
		if (UiKit.Button(sb, new Rectangle(box.X, y, third, 30), "Send selected", share, entry != null && !Main.gameMenu, (entry == null) ? "Pick a character first." : ("Puts " + entry.Player.name + " in the chat line (the whole character, with its IsaClient items).")))
		{
			IsaClient.Sharing.ShareChat.LinkCharacter(entry.Path, entry.Player);
		}
		if (UiKit.Button(sb, new Rectangle(box.X + third + 8, y, third, 30), "Send me", share, !Main.gameMenu, "Puts your character as it is now in the chat line."))
		{
			IsaClient.Sharing.ShareChat.LinkPlayer(Main.LocalPlayer);
		}
		if (UiKit.Button(sb, new Rectangle(box.X + 2 * (third + 8), y, third, 30), "Send look", share, !Main.gameMenu, "Only the look (hair, clothes, colors) of " + ((entry != null) ? entry.Player.name : "you") + ", to try on or use in character creation."))
		{
			Player lookOf = (entry != null) ? entry.Player : Main.LocalPlayer;
			IsaClient.Sharing.ShareChat.LinkStyle(IsaClient.Sharing.StyleLook.Of(lookOf), lookOf.name);
		}
		y += 30 + 12;

		// You.
		UiKit.Text(sb, "You", box.X, y + 8, sectionColor, 0.8f, box.Width);
		y += header;
		if (UiKit.Toggle(sb, new Rectangle(box.X, y, third, 30), "Show me as IsaClient", PlayerListPanel.ShareMyInfo, "Other IsaClient users see the I statue next to you in their Player List.\nzMod and vanilla clients never see it. Saved in your settings."))
		{
			PlayerListPanel.ShareMyInfo = !PlayerListPanel.ShareMyInfo;
			ClientSettings.Save();
		}
		if (UiKit.Button(sb, new Rectangle(box.X + third + 8, y, third, 30), "Save a .plr copy", UiKit.Blue, !Main.gameMenu, "Writes a copy of your current character to your Players folder (new file name)."))
		{
			CharacterPresets.SaveCopy(Main.LocalPlayer);
			Reload();
		}
		if (UiKit.Button(sb, new Rectangle(box.X + 2 * (third + 8), y, third, 30), "Players folder", UiKit.Blue, enabled: true, "Opens the folder with your character files."))
		{
			try
			{
				Directory.CreateDirectory(Main.PlayerPath);
				Process.Start(Main.PlayerPath);
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		}
	}

	private static Rectangle ToScissor(Rectangle ui, Rectangle current)
	{
		Vector2 a = Vector2.Transform(new Vector2(ui.X, ui.Y), Main.UIScaleMatrix);
		Vector2 b = Vector2.Transform(new Vector2(ui.Right, ui.Bottom), Main.UIScaleMatrix);
		Rectangle r = Rectangle.Intersect(new Rectangle((int)a.X, (int)a.Y, (int)(b.X - a.X), (int)(b.Y - a.Y)), current);
		return (r.Width > 0 && r.Height > 0) ? r : new Rectangle(0, 0, 1, 1);
	}
}
