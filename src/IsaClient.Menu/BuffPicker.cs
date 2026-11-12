using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using IsaClient.Core;
using IsaClient.Gameplay;
using Terraria;

namespace IsaClient.Menu;

/// <summary>
/// The "Buffs" view of My Character: every buff as an icon grid (like Journey's item menu), with search and filters.
/// Hover: name and what it does. Right click: infinite. Left click: a small panel to pick the time (presets, minutes,
/// infinite, remove). Drawn in immediate mode inside the window.
/// </summary>
public static class BuffPicker
{
	private const int Cell = 44;

	private const int Gap = 6;

	private static readonly object _marker = new object();

	private static readonly string[] _filterNames = new string[4] { "All", "Buffs", "Debuffs", "Active" };

	private static readonly int[] _presetSeconds = new int[6] { 30, 60, 300, 900, 1800, 3600 };

	private static readonly string[] _presetNames = new string[6] { "30s", "1m", "5m", "15m", "30m", "1h" };

	private static List<int> _all;

	private static string _search = "";

	private static string _focus;

	private static int _filter;

	public static float Scroll;

	private static float _contentHeight;

	private static int _popup = -1;

	private static Rectangle _popupRect;

	private static string _minutes = "";

	private static bool _openedNow;

	private static List<int> All
	{
		get
		{
			if (_all == null)
			{
				_all = new List<int>();
				for (int i = 1; i < BuffID.Count; i++)
				{
					if (!string.IsNullOrWhiteSpace(Lang.GetBuffName(i)) && TextureAssets.Buff[i] != null)
					{
						_all.Add(i);
					}
				}
			}
			return _all;
		}
	}

	public static void Reset()
	{
		_popup = -1;
		_focus = null;
	}

	public static void Draw(SpriteBatch sb, Rectangle area)
	{
		Player player = Main.LocalPlayer;
		_openedNow = false;
		bool popupOpen = _popup > 0;
		Point mouse = UiKit.Mouse;
		bool blocked = popupOpen && _popupRect.Contains(mouse);
		// Top bar: search, filters, stack toggle.
		Rectangle search = new Rectangle(area.X, area.Y, Math.Min(280, area.Width / 3), 30);
		_search = Field(sb, "search", search, _search, "Search buffs...", 40, searchIcon: true, blocked);
		int fx = search.Right + 10;
		for (int i = 0; i < _filterNames.Length; i++)
		{
			int w = (int)UiKit.Measure(_filterNames[i], 0.85f) + 26;
			if (!blocked && UiKit.Button(sb, new Rectangle(fx, area.Y, w, 30), _filterNames[i], (_filter == i) ? UiKit.Green : UiKit.Blue))
			{
				_filter = i;
				Scroll = 0f;
			}
			else if (blocked)
			{
				Utils.DrawInvBG(sb, new Rectangle(fx, area.Y, w, 30), ((_filter == i) ? UiKit.Green : UiKit.Blue) * 0.95f);
				UiKit.Text(sb, _filterNames[i], fx + w / 2, area.Y + 15, Color.White, 0.85f, w - 10, centered: true);
			}
			fx += w + 6;
		}
		Rectangle stack = new Rectangle(Math.Max(fx + 6, area.Right - 170), area.Y, Math.Min(170, area.Right - fx - 6), 30);
		if (stack.Width >= 120 && !blocked && UiKit.Toggle(sb, stack, "Stack Buffs", BuffTools.StackBuffs, "Buffs that replace each other stay together: all the Well Fed tiers, every flask... (also in Cheats > Player)."))
		{
			BuffTools.SetStack(!BuffTools.StackBuffs);
		}
		// Grid.
		Rectangle grid = new Rectangle(area.X, area.Y + 40, area.Width, area.Height - 40 - 26);
		UiKit.Panel(sb, grid, new Color(20, 26, 50) * 0.75f);
		List<int> shown = Filtered(player);
		Rectangle clip = new Rectangle(grid.X + 8, grid.Y + 8, grid.Width - 16, grid.Height - 16);
		int columns = Math.Max(1, (clip.Width + Gap) / (Cell + Gap));
		int rows = (shown.Count + columns - 1) / columns;
		_contentHeight = rows * (Cell + Gap);
		Scroll = MathHelper.Clamp(Scroll, 0f, Math.Max(0f, _contentHeight - clip.Height));
		float leftPad = (clip.Width - (columns * (Cell + Gap) - Gap)) / 2f;
		Rectangle oldScissor = sb.GraphicsDevice.ScissorRectangle;
		RasterizerState clipRaster = new RasterizerState
		{
			CullMode = CullMode.None,
			ScissorTestEnable = true
		};
		sb.End();
		sb.GraphicsDevice.ScissorRectangle = ToScissor(clip, sb.GraphicsDevice);
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.SamplerStateForCursor, DepthStencilState.None, clipRaster, null, Main.UIScaleMatrix);
		int hovered = -1;
		Rectangle hoveredCell = Rectangle.Empty;
		bool mouseInClip = clip.Contains(mouse) && !blocked;
		for (int n = 0; n < shown.Count; n++)
		{
			int type = shown[n];
			Rectangle cell = new Rectangle(clip.X + (int)leftPad + n % columns * (Cell + Gap), clip.Y + n / columns * (Cell + Gap) - (int)Scroll, Cell, Cell);
			if (cell.Bottom < clip.Top || cell.Top > clip.Bottom)
			{
				continue;
			}
			int index = player.FindBuffIndex(type);
			bool active = index >= 0;
			bool hover = mouseInClip && cell.Contains(mouse);
			Color back = active ? new Color(56, 128, 84) : (Main.debuff[type] ? new Color(120, 52, 62) : new Color(63, 82, 151));
			if (hover)
			{
				back = Color.Lerp(back, Color.White, 0.25f);
				hovered = type;
				hoveredCell = cell;
			}
			if (_popup == type)
			{
				back = Color.Lerp(back, new Color(255, 231, 69), 0.35f);
			}
			Utils.DrawInvBG(sb, cell, back * 0.95f);
			Texture2D icon = TextureAssets.Buff[type].Value;
			float scale = Math.Min(1f, 32f / Math.Max(icon.Width, icon.Height));
			sb.Draw(icon, new Vector2(cell.Center.X, cell.Center.Y), null, Color.White, 0f, new Vector2(icon.Width, icon.Height) / 2f, scale * (hover ? 1.1f : 1f), SpriteEffects.None, 0f);
			if (active)
			{
				string time = BuffTools.ShortTime(player.buffTime[index]);
				Vector2 size = FontAssets.ItemStack.Value.MeasureString(IsaClient.Languages.L10n.T(time)) * 0.62f;
				Utils.DrawBorderStringFourWay(sb, FontAssets.ItemStack.Value, IsaClient.Languages.L10n.T(time), cell.Right - size.X - 3f, cell.Bottom - size.Y - 1f, BuffTools.IsInfinite(player.buffTime[index]) ? new Color(255, 231, 69) : Color.White, Color.Black, Vector2.Zero, 0.62f);
			}
		}
		if (shown.Count == 0)
		{
			UiKit.Text(sb, "No buff matches.", clip.X + 10, clip.Y + 16, UiKit.Muted, 0.85f);
		}
		sb.End();
		sb.GraphicsDevice.ScissorRectangle = oldScissor;
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.SamplerStateForCursor, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
		DrawScrollbar(sb, clip);
		if (hovered > 0)
		{
			Main.LocalPlayer.mouseInterface = true;
			int index = player.FindBuffIndex(hovered);
			string state = (index >= 0) ? ("\n[c/78DC78:Active: " + (BuffTools.IsInfinite(player.buffTime[index]) ? "infinite" : BuffTools.ShortTime(player.buffTime[index])) + "]") : "";
			Main.instance.MouseText(IsaClient.Languages.L10n.T(Lang.GetBuffName(hovered) + "\n" + Lang.GetBuffDescription(hovered) + state + "\n[c/9AA7BE:Left click: choose the time   Right click: infinite]"), 0, 0);
			if (UiKit.Clicked)
			{
				Main.mouseLeftRelease = false;
				OpenPopup(hovered, hoveredCell, area);
			}
			else if (Main.mouseRight && Main.mouseRightRelease)
			{
				Main.mouseRightRelease = false;
				Give(hovered, 0, infinite: true);
			}
		}
		int count = 0;
		for (int i = 0; i < Player.maxBuffs; i++)
		{
			if (player.buffType[i] > 0)
			{
				count++;
			}
		}
		UiKit.Text(sb, shown.Count + " buffs   •   " + count + "/" + Player.maxBuffs + " buff slots used   •   infinite buffs stay after death and are saved with your character   •   right click a buff on your buff bar to remove it", area.X + 4, area.Bottom - 11, UiKit.Muted, 0.7f, area.Width - 8);
		if (_popup > 0)
		{
			DrawPopup(sb, area);
		}
	}

	private static List<int> Filtered(Player player)
	{
		string query = (_search ?? "").Trim().ToLowerInvariant();
		IEnumerable<int> list = All;
		switch (_filter)
		{
		case 1:
			list = list.Where((int b) => !Main.debuff[b]);
			break;
		case 2:
			list = list.Where((int b) => Main.debuff[b]);
			break;
		case 3:
			list = list.Where((int b) => player.FindBuffIndex(b) >= 0);
			break;
		}
		if (query.Length > 0)
		{
			list = list.Where((int b) => Lang.GetBuffName(b).ToLowerInvariant().Contains(query) || b.ToString() == query);
		}
		return list.ToList();
	}

	private static void OpenPopup(int type, Rectangle cell, Rectangle area)
	{
		_popup = type;
		_minutes = "";
		_focus = "minutes";
		_openedNow = true;
		Main.clrInput();
		int w = 330;
		int h = 168;
		int x = Utils.Clamp(cell.Center.X - w / 2, area.X, area.Right - w);
		int y = (cell.Bottom + 6 + h <= area.Bottom) ? (cell.Bottom + 6) : (cell.Top - 6 - h);
		_popupRect = new Rectangle(x, Math.Max(area.Y, y), w, h);
		SoundEngine.PlaySound(12);
	}

	private static void DrawPopup(SpriteBatch sb, Rectangle area)
	{
		Player player = Main.LocalPlayer;
		int type = _popup;
		Rectangle r = _popupRect;
		Utils.DrawInvBG(sb, r, new Color(26, 34, 66) * 0.98f);
		Texture2D icon = TextureAssets.Buff[type].Value;
		sb.Draw(icon, new Vector2(r.X + 12, r.Y + 10), Color.White);
		UiKit.Text(sb, Lang.GetBuffName(type), r.X + 52, r.Y + 20, new Color(255, 231, 69), 0.9f, r.Width - 64);
		int index = player.FindBuffIndex(type);
		UiKit.Text(sb, (index >= 0) ? ("Active: " + (BuffTools.IsInfinite(player.buffTime[index]) ? "infinite" : BuffTools.ShortTime(player.buffTime[index]))) : "Not active", r.X + 52, r.Y + 38, UiKit.Muted, 0.7f, r.Width - 64);
		// Presets.
		int pw = (r.Width - 16 - 5 * 4) / 6;
		for (int i = 0; i < _presetSeconds.Length; i++)
		{
			if (UiKit.Button(sb, new Rectangle(r.X + 8 + i * (pw + 4), r.Y + 56, pw, 28), _presetNames[i], UiKit.Blue))
			{
				Give(type, _presetSeconds[i] * 60, infinite: false);
				_popup = -1;
				return;
			}
		}
		// Minutes field + set.
		Rectangle field = new Rectangle(r.X + 8, r.Y + 92, 150, 30);
		_minutes = Field(sb, "minutes", field, _minutes, "Minutes...", 6, searchIcon: false, blocked: false, numeric: true);
		bool valid = float.TryParse(_minutes.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) && minutes > 0f;
		if (UiKit.Button(sb, new Rectangle(field.Right + 6, r.Y + 92, r.Right - 8 - field.Right - 6, 30), "Set time", UiKit.Green, valid) || (valid && _focus == "minutes" && Main.inputTextEnter))
		{
			Give(type, (int)Math.Min(int.MaxValue / 2, minutes * 3600f), infinite: false);
			_popup = -1;
			return;
		}
		// Infinite / remove.
		int half = (r.Width - 20) / 2;
		if (UiKit.Button(sb, new Rectangle(r.X + 8, r.Y + 128, half, 30), "Infinite", UiKit.Purple, enabled: true, "Stays until you remove it, also after dying (right click on the grid does the same)."))
		{
			Give(type, 0, infinite: true);
			_popup = -1;
			return;
		}
		if (UiKit.Button(sb, new Rectangle(r.X + 12 + half, r.Y + 128, half, 30), "Remove", UiKit.Red, index >= 0))
		{
			BuffTools.Remove(type);
			SoundEngine.PlaySound(11);
			_popup = -1;
			return;
		}
		bool inside = r.Contains(UiKit.Mouse);
		if (inside)
		{
			Main.LocalPlayer.mouseInterface = true;
		}
		if (!_openedNow && ((!inside && (UiKit.Clicked || (Main.mouseRight && Main.mouseRightRelease))) || Main.inputTextEscape))
		{
			Main.inputTextEscape = false;
			_popup = -1;
			_focus = null;
		}
	}

	private static void Give(int type, int frames, bool infinite)
	{
		if (BuffTools.Give(type, frames, infinite))
		{
			SoundEngine.PlaySound(10);
		}
		else
		{
			SoundEngine.PlaySound(11);
			ClientChat.Error(Lang.GetBuffName(type) + " could not be added (you are immune to it, or all " + Player.maxBuffs + " buff slots are full).");
		}
	}

	private static string Field(SpriteBatch sb, string id, Rectangle r, string value, string placeholder, int maxLength, bool searchIcon, bool blocked, bool numeric = false)
	{
		bool focused = _focus == id;
		bool hover = !blocked && r.Contains(UiKit.Mouse);
		if (hover)
		{
			Main.LocalPlayer.mouseInterface = true;
			if (UiKit.Clicked && !focused)
			{
				Main.mouseLeftRelease = false;
				_focus = id;
				focused = true;
				Main.clrInput();
			}
		}
		else if (focused && UiKit.Clicked && !blocked)
		{
			_focus = null;
			focused = false;
		}
		Utils.DrawInvBG(sb, r, (focused ? new Color(40, 55, 110) : new Color(26, 34, 66)) * 0.95f);
		if (focused)
		{
			PlayerInput.WritingText = true;
			Main.CurrentInputTextTakerOverride = _marker;
			Main.chatRelease = false;
			Main.instance.HandleIME();
			string typed = Main.GetInputText(value ?? "");
			if (numeric)
			{
				typed = new string(typed.Where((char c) => char.IsDigit(c) || c == '.' || c == ',').ToArray());
			}
			value = (typed.Length > maxLength) ? typed.Substring(0, maxLength) : typed;
			if (Main.inputTextEscape && _popup <= 0)
			{
				Main.inputTextEscape = false;
				_focus = null;
			}
		}
		float left = r.X + 8;
		if (searchIcon)
		{
			Texture2D glass = Main.Assets.Request<Texture2D>("Images/UI/Cursor_2", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
			sb.Draw(glass, new Rectangle(r.X + 7, r.Y + (r.Height - 18) / 2, 18, 18), Color.White);
			left = r.X + 30;
		}
		string shown = string.IsNullOrEmpty(value) ? placeholder : value;
		Color color = string.IsNullOrEmpty(value) ? new Color(120, 130, 160) : Color.White;
		if (focused && Main.GameUpdateCount / 30 % 2 == 0)
		{
			shown = (string.IsNullOrEmpty(value) ? "" : value) + "|";
			color = Color.White;
		}
		UiKit.Text(sb, shown, left, r.Center.Y, color, 0.82f, r.Right - left - 6);
		return value;
	}

	private static void DrawScrollbar(SpriteBatch sb, Rectangle clip)
	{
		if (_contentHeight <= clip.Height)
		{
			return;
		}
		Texture2D pixel = TextureAssets.MagicPixel.Value;
		Rectangle track = new Rectangle(clip.Right + 2, clip.Y, 4, clip.Height);
		sb.Draw(pixel, track, new Color(15, 20, 40) * 0.8f);
		int thumbH = Math.Max(24, (int)(track.Height * clip.Height / _contentHeight));
		int thumbY = track.Y + (int)((track.Height - thumbH) * (Scroll / Math.Max(1f, _contentHeight - clip.Height)));
		sb.Draw(pixel, new Rectangle(track.X, thumbY, track.Width, thumbH), new Color(150, 170, 230));
	}

	private static Rectangle ToScissor(Rectangle ui, GraphicsDevice device)
	{
		Vector2 a = Vector2.Transform(new Vector2(ui.X, ui.Y), Main.UIScaleMatrix);
		Vector2 b = Vector2.Transform(new Vector2(ui.Right, ui.Bottom), Main.UIScaleMatrix);
		Rectangle rect = Rectangle.Intersect(new Rectangle((int)a.X, (int)a.Y, (int)(b.X - a.X), (int)(b.Y - a.Y)), device.Viewport.Bounds);
		return (rect.Width > 0 && rect.Height > 0) ? rect : new Rectangle(0, 0, 1, 1);
	}
}
