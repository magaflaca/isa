using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Core;
using IsaClient.Menu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using I = Terraria.ID.ItemID;

using IsaClient.Languages;

namespace IsaClient.Hub;

/// <summary>
/// The new IsaClient panel (opened with the Open Menu key, like the classic one): a movable, resizable window with a
/// sidebar of sections, a search over every feature, favorites, your own buttons and saved setups. Immediate mode,
/// drawn in place of the classic panel with Terraria's own textures and icons. The classic panel stays one click away
/// (Settings > Classic Interface, or .menu classic) and both drive the same features.
/// </summary>
public static class ClientHub
{
	private const int HeaderHeight = 58;

	private const int FooterHeight = 28;

	private const int TabsHeight = 34;

	private const int Pad = 12;

	private static HubSection _section;

	private static int _page;

	private static string _search = "";

	private static Rectangle _win;

	private static bool _dragWin;

	private static bool _resizing;

	private static Vector2 _dragOffset;

	private static readonly Dictionary<string, float> _scroll = new Dictionary<string, float>();

	private static readonly Dictionary<string, float> _scrollTarget = new Dictionary<string, float>();

	private static readonly Dictionary<string, float> _contentHeight = new Dictionary<string, float>();

	private static string _scrollDrag;

	private static float _scrollDragFrom;

	internal static string SliderDrag;

	private static string _confirmId;

	private static double _confirmUntil;

	private static float _open;

	private static float _pageSlide = 1f;

	private static bool _wasShown;

	private static bool _closed = true;

	private static HubEmbed _embedShown;

	private static Rectangle _embedArea;

	private static string _searchCacheText;

	private static int _searchCacheLanguage = -1;

	private static List<HubFeature> _searchCache = new List<HubFeature>();

	// Popup menu (right click on a card)
	private sealed class PopupEntry
	{
		public string Label;

		public HubIcon Icon;

		public Action Action;
	}

	private static List<PopupEntry> _popup;

	private static Vector2 _popupAt;

	private static bool _popupOpenedNow;

	/// <summary>The panel has the mouse (the game must not use clicks there).</summary>
	public static bool MouseOver { get; private set; }

	/// <summary>The window in real screen pixels (the music window uses it for stacking).</summary>
	public static Rectangle ScreenRect { get; private set; }

	public static bool Dragging => _dragWin || _resizing || SliderDrag != null || _scrollDrag != null;

	private static HubTheme Theme => HubTheme.Current;

	private static float Opacity => HubPrefs.Opacity;

	/// <summary>The new panel draws instead of the classic one (in game, panel open, classic not chosen).</summary>
	public static bool TakesPanel
	{
		get
		{
			HubPrefs.EnsureLoaded();
			return (!ClassicNow || HubCharacterEditor.IsOpen) && !Main.gameMenu && InputState.TildeTerminal && MenuState.UiRework && MenuState.CustomUI2 != null && MenuState.CustomUI2.CurrentState == CheatPanel.UiStateNewCheatPanel;
		}
	}

	/// <summary>
	/// The classic panel is shown: it's chosen, and no server limits cheats (the classic panel has no per-page limits,
	/// so the new one shows there instead; the choice itself isn't changed).
	/// </summary>
	internal static bool ClassicNow => HubPrefs.UseClassic && !IsaClient.Networking.ServerModes.CheatsBlocked;

	#region Switching interfaces

	public static void SetClassic(bool classic)
	{
		HubPrefs.EnsureLoaded();
		HubPrefs.UseClassic = classic;
		HubPrefs.Save();
		if (classic)
		{
			CheatPanel.RefreshUI();
		}
		HubInput.Focus = null;
		ClientChat.Info(classic ? "Classic panel on. Back to the new one: Miscellaneous > New Interface, or .menu new" : "New panel on. Back to the classic one: Settings > Classic Interface, or .menu classic");
	}

	public static void ResetWindow()
	{
		HubPrefs.WindowX = HubPrefs.WindowY = HubPrefs.WindowW = HubPrefs.WindowH = float.NaN;
		HubPrefs.Scale = 1f;
		HubPrefs.Changed();
	}

	#endregion

	#region Game hooks

	/// <summary>Game tick: the mouse wheel over the panel scrolls it (not the hotbar), Ctrl+F focuses the search.</summary>
	public static void Update()
	{
		bool shown = TakesPanel;
		if (!shown)
		{
			// Only a real close (panel key, leaving the world, classic panel) starts over on Home next time; the
			// inventory viewer or My Character opened from a page bring you back to that page.
			if (!InputState.TildeTerminal || Main.gameMenu || ClassicNow)
			{
				_closed = true;
				HubCharacterEditor.Dismiss();
			}
			if (_wasShown)
			{
				HubInput.Focus = null;
				_popup = null;
				HubColorPicker.Close();
				_dragWin = _resizing = false;
				SliderDrag = _scrollDrag = null;
				HubPrefs.SaveIfChanged();
			}
			_wasShown = false;
			MouseOver = false;
			return;
		}
		if (!_wasShown && (_closed || _section == null))
		{
			_closed = false;
			_open = 0f;
			if (HubPrefs.OpenOnHome || _section == null)
			{
				HubSection last = HubCatalog.FindSection(HubPrefs.LastSection);
				_section = HubPrefs.OpenOnHome ? HubCatalog.Home : (last ?? HubCatalog.Home);
				_page = (_section == last) ? Utils.Clamp(HubPrefs.LastPage, 0, Math.Max(0, _section.Pages.Count - 1)) : 0;
			}
		}
		_wasShown = true;
		Point raw = new Point(PlayerInput.MouseX, PlayerInput.MouseY);
		if (ScreenRect.Contains(raw) && PlayerInput.ScrollWheelDelta != 0 && !IsaClient.Music.MusicPlayerWindow.MouseOwnedByMusic)
		{
			bool overEmbed = _embedShown != null && _embedShown.ScreenArea.Contains(new Point((int)(raw.X / Main.UIScale), (int)(raw.Y / Main.UIScale)));
			if (!overEmbed)
			{
				HubInput.Wheel += PlayerInput.ScrollWheelDelta;
				PlayerInput.ScrollWheelDeltaForUI = 0;
			}
			PlayerInput.ScrollWheelDelta = 0;
		}
		bool ctrl = Main.keyState.IsKeyDown(Keys.LeftControl) || Main.keyState.IsKeyDown(Keys.RightControl);
		if (ctrl && Main.keyState.IsKeyDown(Keys.F) && !Main.oldKeyState.IsKeyDown(Keys.F) && !Main.drawingPlayerChat && Main.hasFocus)
		{
			HubInput.SetFocus("search");
		}
	}

	/// <summary>Draws the panel where the classic panel would be (called by the game's interface layer).</summary>
	public static void Draw(SpriteBatch sb, GameTime time, bool mouseAllowed)
	{
		try
		{
			HubPrefs.EnsureLoaded();
			if (_section == null)
			{
				_section = HubCatalog.Home;
			}
			_open = HubPrefs.Animations ? Math.Min(1f, _open + 0.16f) : 1f;
			_pageSlide = HubPrefs.Animations ? Math.Min(1f, _pageSlide + 0.14f) : 1f;
			HubInput.Begin(mouseAllowed && !IsaClient.Music.MusicPlayerWindow.MouseOwnedByMusic);
			HubInput.Matrix = Matrix.CreateTranslation(0f, (1f - Ease(_open)) * 14f, 0f) * HubInput.Matrix;
			HubInput.PopupOpen = _popup != null || HubColorPicker.IsOpen;
			_popupOpenedNow = false;
			_embedShown = null;
			HubInput.Restart(sb);
			Layout();
			bool overWindow = HubInput.In(_win) || Dragging || (_popup != null) || HubColorPicker.IsOpen;
			MouseOver = overWindow;
			if (overWindow && Main.LocalPlayer != null)
			{
				Main.LocalPlayer.mouseInterface = true;
			}
			DrawWindow(sb, time, mouseAllowed);
			DrawPopup(sb);
			HubColorPicker.Draw(sb);
			// Like the classic panel: some classic sliders only show live values after this.
			if (SliderDrag == null && WorldEditMenu.SyncSliders())
			{
				MiscMenu.SyncSliders();
			}
			if (_confirmId != null && Main.timeForVisualEffects > _confirmUntil)
			{
				_confirmId = null;
			}
			if (HubInput.Clicked && overWindow)
			{
				HubInput.Consume();
			}
			HubInput.End(sb);
			string tooltip = HubInput.Tooltip;
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.SamplerStateForCursor, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
			if (_popup == null && HubPlayerView.DrawHoverTooltip())
			{
			}
			else if (tooltip != null && _popup == null)
			{
				Main.instance.MouseText(IsaClient.Languages.L10n.T(tooltip), 0, 0);
			}
			_failures = 0;
		}
		catch (Exception ex)
		{
			OnFailure(sb, ex);
		}
	}

	private static int _failures;

	private static double _lastLogged = -10000.0;

	/// <summary>
	/// A frame that threw: the batch is put back the way the game expects it. If it keeps failing, the classic panel
	/// takes over so the client never stays without a panel.
	/// </summary>
	private static void OnFailure(SpriteBatch sb, Exception ex)
	{
		if (Main.timeForVisualEffects - _lastLogged > 300.0)
		{
			_lastLogged = Main.timeForVisualEffects;
			ClientLog.LogException(ex);
		}
		try
		{
			sb.End();
		}
		catch
		{
		}
		HubInput.Clipping = false;
		sb.GraphicsDevice.ScissorRectangle = sb.GraphicsDevice.Viewport.Bounds;
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.SamplerStateForCursor, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
		_popup = null;
		_dragWin = _resizing = false;
		SliderDrag = _scrollDrag = null;
		if (++_failures >= 30)
		{
			_failures = 0;
			HubPrefs.UseClassic = true;
			HubPrefs.Save();
			CheatPanel.RefreshUI();
			ClientChat.Error("The new panel ran into an error, so the classic panel is back. Try the new one again with .menu new (details in the client log).");
		}
	}

	private static float Ease(float t)
	{
		return 1f - (1f - t) * (1f - t);
	}

	#endregion

	#region Window

	private static void Layout()
	{
		float uiW = Main.instance.GraphicsDevice.Viewport.Width / Main.UIScale / HubInput.Scale;
		float uiH = Main.instance.GraphicsDevice.Viewport.Height / Main.UIScale / HubInput.Scale;
		int minW = 640;
		int minH = 420;
		if (float.IsNaN(HubPrefs.WindowW))
		{
			int w = (int)MathHelper.Clamp(uiW - 120f, minW, 980f);
			int h = (int)MathHelper.Clamp(uiH - 200f, minH, 660f);
			_win = new Rectangle((int)((uiW - w) / 2f), (int)Math.Max(70f, (uiH - h) / 2f + 10f), w, h);
		}
		else
		{
			_win = new Rectangle((int)HubPrefs.WindowX, (int)HubPrefs.WindowY, (int)HubPrefs.WindowW, (int)HubPrefs.WindowH);
		}
		_win.Width = (int)MathHelper.Clamp(_win.Width, minW, Math.Max(minW, uiW - 8f));
		_win.Height = (int)MathHelper.Clamp(_win.Height, minH, Math.Max(minH, uiH - 8f));
		_win.X = (int)MathHelper.Clamp(_win.X, 4f - _win.Width + 120f, uiW - 120f);
		_win.Y = (int)MathHelper.Clamp(_win.Y, 4f, uiH - 60f);
		ScreenRect = HubInput.ToScreen(_win);
	}

	private static void SaveWindow()
	{
		HubPrefs.WindowX = _win.X;
		HubPrefs.WindowY = _win.Y;
		HubPrefs.WindowW = _win.Width;
		HubPrefs.WindowH = _win.Height;
		HubPrefs.Changed();
	}

	private static int SidebarWidth => HubPrefs.CollapsedSidebar ? 60 : 200;

	private static void DrawWindow(SpriteBatch sb, GameTime time, bool mouseAllowed)
	{
		HubKit.Panel(sb, _win, Theme.Window * Opacity, Color.Black * Math.Min(1f, Opacity + 0.1f));
		Rectangle side = new Rectangle(_win.X + 6, _win.Y + 6, SidebarWidth, _win.Height - 12);
		DrawSidebar(sb, side);
		Rectangle main = new Rectangle(side.Right + 8, _win.Y + 6, _win.Right - side.Right - 14, _win.Height - 12);
		Rectangle header = new Rectangle(main.X, main.Y, main.Width, HeaderHeight);
		if (HubCharacterEditor.IsOpen)
		{
			// The character editor takes the whole page: its own title, tabs and buttons.
			HubIcon.Item(Terraria.ID.ItemID.Mannequin).Draw(sb, new Rectangle(header.X + 6, header.Y + 12, 34, 34), Color.White);
			HubKit.Title(sb, HubCharacterEditor.Title, header.X + 50, header.Y + 8, header.Width - 60, 0.6f, Color.White);
			HubKit.Text(sb, HubCharacterEditor.Subtitle, header.X + 52, header.Y + 36, Theme.Muted, 0.72f, header.Width - 60);
			HubKit.Line(sb, header.X, header.Bottom - 4, header.Width, Theme.Muted * 0.18f);
			HubCharacterEditor.Draw(sb, new Rectangle(main.X, header.Bottom + 4, main.Width - 4, main.Bottom - header.Bottom - 8));
			DrawResizeGrip(sb);
			HandleWindowDrag(header);
			return;
		}
		DrawHeader(sb, header);
		Rectangle footer = new Rectangle(main.X, main.Bottom - FooterHeight, main.Width, FooterHeight);
		Rectangle body = new Rectangle(main.X, header.Bottom, main.Width, footer.Y - header.Bottom - 4);
		bool searching = !string.IsNullOrWhiteSpace(_search);
		if (!searching && !_section.Special && _section.Pages.Count > 1)
		{
			DrawTabs(sb, new Rectangle(body.X, body.Y, body.Width, TabsHeight));
			body = new Rectangle(body.X, body.Y + TabsHeight + 4, body.Width, body.Height - TabsHeight - 4);
		}
		DrawBody(sb, body, time, mouseAllowed);
		DrawFooter(sb, footer);
		DrawResizeGrip(sb);
		HandleWindowDrag(header);
	}

	private static void HandleWindowDrag(Rectangle header)
	{
		if (_dragWin)
		{
			if (!HubInput.Down)
			{
				_dragWin = false;
				SaveWindow();
			}
			else
			{
				_win.X = (int)(HubInput.Mouse.X - _dragOffset.X);
				_win.Y = (int)(HubInput.Mouse.Y - _dragOffset.Y);
				SaveWindow();
			}
		}
		if (_resizing)
		{
			if (!HubInput.Down)
			{
				_resizing = false;
				SaveWindow();
			}
			else
			{
				_win.Width = (int)(HubInput.Mouse.X - _win.X + _dragOffset.X);
				_win.Height = (int)(HubInput.Mouse.Y - _win.Y + _dragOffset.Y);
				SaveWindow();
			}
		}
		// Empty parts of the header and the sidebar logo move the window.
		bool grab = (HubInput.In(header) || HubInput.In(new Rectangle(_win.X, _win.Y, SidebarWidth + 8, 64))) && HubInput.Hovered == null;
		if (grab && !Dragging)
		{
			if (HubInput.Clicked)
			{
				_dragWin = true;
				_dragOffset = new Vector2(HubInput.Mouse.X - _win.X, HubInput.Mouse.Y - _win.Y);
				HubInput.Consume();
			}
		}
	}

	private static void DrawResizeGrip(SpriteBatch sb)
	{
		Rectangle grip = new Rectangle(_win.Right - 18, _win.Bottom - 18, 14, 14);
		bool hover = HubInput.In(grip);
		Color color = (hover || _resizing) ? Theme.Accent : (Theme.Muted * 0.6f);
		for (int i = 0; i < 3; i++)
		{
			for (int j = 0; j <= i; j++)
			{
				HubKit.Fill(sb, new Rectangle(grip.Right - 3 - i * 4 + j * 4, grip.Bottom - 3 - j * 4, 2, 2), color);
			}
		}
		if (hover && HubInput.Hover("resize", "Drag to resize"))
		{
			_resizing = true;
			_dragOffset = new Vector2(_win.Right - HubInput.Mouse.X, _win.Bottom - HubInput.Mouse.Y);
		}
	}

	#endregion

	#region Sidebar, header, tabs, footer

	private static IEnumerable<HubSection> VisibleSections()
	{
		yield return HubCatalog.Home;
		foreach (HubSection section in HubCatalog.Sections)
		{
			if (!section.Special && !HubPrefs.HiddenSections.Contains(section.Id))
			{
				yield return section;
			}
		}
	}

	/// <summary>How far the section list is scrolled when it doesn't fit.</summary>
	private static float _navScroll;

	private static void DrawSidebar(SpriteBatch sb, Rectangle side)
	{
		HubKit.Box(sb, side, Theme.Sidebar * Opacity);
		bool slim = HubPrefs.CollapsedSidebar;
		// Logo
		Rectangle logo = new Rectangle(side.X + 8, side.Y + 8, 40, 40);
		Texture2D tree = ClientLogo.Tree;
		float bob = (float)Math.Sin(Main.GlobalTimeWrappedHourly * 1.6f) * 1.5f;
		if (tree != null)
		{
			float scale = Math.Min(40f / tree.Width, 40f / tree.Height);
			sb.Draw(tree, new Vector2(logo.Center.X, logo.Center.Y + bob), null, Color.White, 0f, new Vector2(tree.Width, tree.Height) / 2f, scale, SpriteEffects.None, 0f);
		}
		if (!slim)
		{
			HubKit.Title(sb, ClientInfo.Name, logo.Right + 8, logo.Y + 2, side.Width - 64, 0.5f, Theme.Accent);
			HubKit.Text(sb, "v" + ClientInfo.FullVersion, logo.Right + 10, logo.Y + 26, Theme.Muted, 0.68f);
		}
		List<HubSection> sections = VisibleSections().ToList();
		HubSection settings = sections.FirstOrDefault(s => s.Id == "settings");
		if (settings != null)
		{
			sections.Remove(settings);
		}
		int top = side.Y + 60;
		int bottomReserved = (settings != null) ? 40 : 0;
		int available = side.Bottom - 8 - bottomReserved - top;
		int itemH = (int)MathHelper.Clamp(available / Math.Max(1f, sections.Count), 22f, 36f);
		// More sections than room: the list scrolls with the wheel (arrows show there is more).
		Rectangle list = new Rectangle(side.X, top, side.Width, available);
		int overflow = Math.Max(0, sections.Count * itemH - available);
		if (overflow > 0 && HubInput.Wheel != 0 && HubInput.In(list))
		{
			_navScroll -= HubInput.Wheel / 3f;
			HubInput.Wheel = 0;
		}
		_navScroll = MathHelper.Clamp(_navScroll, 0f, overflow);
		if (overflow > 0)
		{
			HubInput.BeginClip(sb, list);
		}
		int y = top - (int)_navScroll;
		foreach (HubSection section in sections)
		{
			if (y + itemH > top && y < top + available)
			{
				NavItem(sb, section, new Rectangle(side.X + 4, y, side.Width - 8, itemH - 2), slim);
			}
			y += itemH;
		}
		if (overflow > 0)
		{
			HubInput.EndClip(sb);
			if (_navScroll > 1f)
			{
				HubKit.Fill(sb, new Rectangle(list.X + 10, list.Y, list.Width - 20, 2), Theme.Accent * 0.5f);
			}
			if (_navScroll < overflow - 1f)
			{
				HubKit.Fill(sb, new Rectangle(list.X + 10, list.Bottom - 2, list.Width - 20, 2), Theme.Accent * 0.5f);
			}
		}
		if (settings != null)
		{
			HubKit.Line(sb, side.X + 10, side.Bottom - 44, side.Width - 20, Theme.Muted * 0.25f);
			NavItem(sb, settings, new Rectangle(side.X + 4, side.Bottom - 38, side.Width - 8, 32), slim);
		}
	}

	private static void NavItem(SpriteBatch sb, HubSection section, Rectangle r, bool slim)
	{
		bool selected = string.IsNullOrWhiteSpace(_search) && _section == section;
		bool hover = HubInput.In(r);
		string id = "nav:" + section.Id;
		int active = section.Special ? 0 : section.ActiveCount;
		string tip = slim ? (section.Title + "\n" + section.Subtitle) : section.Subtitle;
		if (hover && HubInput.Hover(id, tip))
		{
			Open(section);
		}
		float glow = HubInput.Anim(id, hover || selected);
		if (glow > 0.01f)
		{
			Color back = selected ? Color.Lerp(Theme.Card, Theme.Slot, 0.6f) : Theme.Card;
			HubKit.Box(sb, r, back * glow * Opacity);
		}
		if (selected)
		{
			HubKit.Fill(sb, new Rectangle(r.X + 2, r.Y + 6, 3, r.Height - 12), section.Accent);
		}
		int iconSize = Math.Min(26, r.Height - 6);
		float lift = HubInput.Anim(id + ":lift", hover) * 2f;
		Rectangle icon = new Rectangle(slim ? (r.X + (r.Width - iconSize) / 2) : (r.X + 10), (int)(r.Y + (r.Height - iconSize) / 2 - lift), iconSize, iconSize);
		section.Icon.Draw(sb, icon, Color.White);
		if (!slim)
		{
			Color text = selected ? Color.White : Color.Lerp(new Color(205, 212, 232), Color.White, glow);
			float room = r.Width - 52 - (active > 0 ? 30 : 0);
			HubKit.Text(sb, section.Title, icon.Right + 10, r.Y + (r.Height - 18) / 2f + 2f, text, 0.82f, room);
			if (active > 0)
			{
				HubKit.Chip(sb, active.ToString(), r.Right - 6, r.Y + (r.Height - 20) / 2, Theme.On * 0.55f, Color.White, 0.66f, alignRight: true);
			}
		}
		else if (active > 0)
		{
			HubKit.Fill(sb, new Rectangle(icon.Right - 2, icon.Y, 6, 6), Theme.On);
		}
	}

	private static void Open(HubSection section, int page = 0)
	{
		HubCharacterEditor.Dismiss();
		if (_section != section || _page != page || !string.IsNullOrWhiteSpace(_search))
		{
			_pageSlide = 0f;
		}
		_section = section;
		_page = page;
		_search = "";
		HubInput.Focus = (HubInput.Focus == "search") ? null : HubInput.Focus;
		HubPrefs.LastSection = section.Id;
		HubPrefs.LastPage = page;
		HubPrefs.Changed();
	}

	private static void DrawHeader(SpriteBatch sb, Rectangle r)
	{
		bool searching = !string.IsNullOrWhiteSpace(_search);
		int searchW = (int)MathHelper.Clamp(r.Width * 0.38f, 180f, 300f);
		Rectangle close = new Rectangle(r.Right - 34, r.Y + 14, 28, 28);
		Rectangle field = new Rectangle(close.X - 8 - searchW, r.Y + 13, searchW, 30);
		HubIcon icon = searching ? HubIcon.Texture("Images/UI/Cursor_2") : _section.Icon;
		icon.Draw(sb, new Rectangle(r.X + 6, r.Y + 12, 34, 34), Color.White);
		float textX = r.X + 50;
		float room = field.X - textX - 10;
		string title = searching ? "Search" : _section.Title;
		string subtitle = searching ? (SearchResults().Count + " features found for \"" + _search.Trim() + "\"") : _section.Subtitle;
		HubKit.Title(sb, title, textX, r.Y + 8, room, 0.6f, Color.White);
		HubKit.Text(sb, subtitle, textX + 2, r.Y + 36, Theme.Muted, 0.72f, room);
		string before = _search;
		_search = HubKit.Field(sb, "search", field, _search, "Search features...  (Ctrl+F)", 40, HubIcon.Texture("Images/UI/Cursor_2"));
		if (_search != before)
		{
			_pageSlide = 0.5f;
			_scrollTarget["search"] = 0f;
			_scroll["search"] = 0f;
		}
		if (HubKit.BareIcon(sb, "close", close, HubIcon.Texture("Images/UI/SearchCancel"), "Close (Open Menu key)", Color.White, 1.6f))
		{
			InputState.TildeTerminal = false;
			HubInput.Sound(11);
		}
		HubKit.Line(sb, r.X, r.Bottom - 4, r.Width, Theme.Muted * 0.18f);
	}

	private static void DrawTabs(SpriteBatch sb, Rectangle r)
	{
		int x = r.X;
		for (int i = 0; i < _section.Pages.Count; i++)
		{
			HubPage page = _section.Pages[i];
			float w = HubKit.Measure(page.Title, 0.8f) + 44f;
			Rectangle tab = new Rectangle(x, r.Y + 2, (int)w, r.Height - 4);
			if (tab.Right > r.Right)
			{
				break;
			}
			bool selected = i == _page;
			bool hover = HubInput.In(tab);
			string id = "tab:" + _section.Id + ":" + i;
			if (hover && HubInput.Hover(id) && !selected)
			{
				_page = i;
				_pageSlide = 0f;
				HubPrefs.LastPage = i;
				HubPrefs.Changed();
			}
			float glow = HubInput.Anim(id, hover || selected);
			HubKit.Box(sb, tab, Color.Lerp(Theme.Sidebar, selected ? Theme.Slot : Theme.CardHover, glow) * Opacity);
			if (selected)
			{
				HubKit.Fill(sb, new Rectangle(tab.X + 10, tab.Bottom - 4, tab.Width - 20, 2), _section.Accent);
			}
			page.Icon.Draw(sb, new Rectangle(tab.X + 8, tab.Y + (tab.Height - 20) / 2, 20, 20), Color.White);
			HubKit.Text(sb, page.Title, tab.X + 34, tab.Y + (tab.Height - 18) / 2f + 2f, selected ? Color.White : new Color(205, 212, 232), 0.8f);
			x = tab.Right + 6;
		}
	}

	private static void DrawFooter(SpriteBatch sb, Rectangle r)
	{
		HubKit.Line(sb, r.X, r.Y, r.Width - 20, Theme.Muted * 0.18f);
		int active = ActiveFeatures().Count();
		int x = r.X + 2;
		Rectangle on = HubKit.Chip(sb, active + " on", x, r.Y + 5, (active > 0 ? Theme.On * 0.5f : Theme.Off) * Opacity, Color.White, 0.68f);
		if (HubInput.In(on) && HubInput.Hover("footer:on", "What is on right now (Home)"))
		{
			Open(HubCatalog.Home);
		}
		x = on.Right + 6;
		Rectangle fav = HubKit.Chip(sb, HubPrefs.Favorites.Count + " favorites", x, r.Y + 5, Theme.Card * Opacity, Theme.Accent, 0.68f);
		if (HubInput.In(fav) && HubInput.Hover("footer:fav", "Your favorites (Home)"))
		{
			Open(HubCatalog.Home);
		}
		string hint = "Right click a card for more  ·  Star = favorite";
		float hintW = HubKit.Measure(hint, 0.66f);
		Rectangle classic = new Rectangle(r.Right - 132, r.Y + 4, 110, 21);
		if (fav.Right + 12 + hintW < classic.X - 8)
		{
			HubKit.Text(sb, hint, fav.Right + 12, r.Y + 7, Theme.Muted * 0.8f, 0.66f);
		}
		if (HubKit.Button(sb, "footer:classic", classic, "Classic panel", Theme.Card, "Switch to the old panel (Settings has the same option)"))
		{
			SetClassic(true);
		}
	}

	#endregion

	#region Body

	/// <summary>Scrolls the page shown to <paramref name="offset"/> (from its top), smoothly.</summary>
	/// <summary>How tall the page area is (pages that fill it exactly, without scrolling).</summary>
	internal static int PageViewHeight = 500;

	internal static void ScrollPageTo(float offset)
	{
		_scrollTarget[ScrollKey] = Math.Max(0f, offset);
	}

	private static string ScrollKey
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(_search))
			{
				return "search";
			}
			return _section.Id + ":" + _page;
		}
	}

	private static void DrawBody(SpriteBatch sb, Rectangle body, GameTime time, bool mouseAllowed)
	{
		bool searching = !string.IsNullOrWhiteSpace(_search);
		if (!searching && _section != HubCatalog.Home && IsaClient.Networking.ServerModes.BlocksHubPage(_section.Id, (_section.Pages.Count > 0) ? _section.Pages[Utils.Clamp(_page, 0, _section.Pages.Count - 1)].Title : null))
		{
			EmptyNote(sb, body.X + 4, body.Y + 8, body.Width - 24, HubIcon.Item(Terraria.ID.ItemID.ChestLock), "Not on this server", "This server turned off IsaClient's cheats while you play here. Everything comes back when you leave.");
			return;
		}
		HubPage page = (!searching && !_section.Special && _section.Pages.Count > 0) ? _section.Pages[Utils.Clamp(_page, 0, _section.Pages.Count - 1)] : null;
		if (page?.Embed != null)
		{
			Rectangle area = new Rectangle(body.X, body.Y + 2, body.Width - 2, body.Height - 4);
			HubKit.Box(sb, area, Theme.Sidebar * (Opacity * 0.6f));
			_embedShown = page.Embed;
			_embedArea = area;
			page.Embed.Draw(sb, new Rectangle(area.X + 6, area.Y + 6, area.Width - 12, area.Height - 12), mouseAllowed && _popup == null && !IsaClient.Music.MusicPlayerWindow.MouseOwnedByMusic, time);
			return;
		}
		string key = ScrollKey;
		Rectangle view = new Rectangle(body.X, body.Y, body.Width - 18, body.Height);
		float target = _scrollTarget.TryGetValue(key, out float t) ? t : 0f;
		float content = _contentHeight.TryGetValue(key, out float h) ? h : 0f;
		float max = Math.Max(0f, content - view.Height);
		if (HubInput.Wheel != 0 && HubInput.In(body) && _popup == null && !HubInput.InnerScroll.Contains(HubInput.Mouse))
		{
			target -= HubInput.Wheel / 2f;
			HubInput.Wheel = 0;
		}
		PageViewHeight = view.Height;
		target = MathHelper.Clamp(target, 0f, max);
		float shown = _scroll.TryGetValue(key, out float s) ? s : target;
		shown = HubPrefs.Animations ? MathHelper.Lerp(shown, target, 0.35f) : target;
		if (Math.Abs(shown - target) < 0.5f)
		{
			shown = target;
		}
		float bar = HubKit.Scrollbar(sb, "scroll:" + key, new Rectangle(body.Right - 13, body.Y + 8, 12, body.Height - 16), shown, content, view.Height, ref _scrollDrag, ref _scrollDragFrom);
		if (_scrollDrag == "scroll:" + key)
		{
			shown = target = bar;
		}
		_scrollTarget[key] = target;
		_scroll[key] = shown;
		HubInput.BeginClip(sb, view);
		int slide = (int)((1f - Ease(_pageSlide)) * 24f);
		int x = view.X + 2 + slide;
		int y = view.Y + 6 - (int)shown;
		int width = view.Width - 6;
		int used;
		HubInput.InnerScroll = Rectangle.Empty;
		if (searching)
		{
			used = DrawSearch(sb, x, y, width);
		}
		else if (_section == HubCatalog.Home)
		{
			used = HubHome.Draw(sb, x, y, width);
		}
		else if (page?.Custom != null)
		{
			used = page.Custom(sb, x, y, width);
		}
		else
		{
			used = DrawGroups(sb, page?.Groups ?? new List<HubGroup>(), x, y, width, null);
			if (page?.Below != null)
			{
				used += 10 + page.Below(sb, x, y + used + 10, width);
			}
		}
		_contentHeight[key] = used + 12;
		HubInput.EndClip(sb);
		// Soft edges where the list goes under the header/footer.
		if (shown > 1f)
		{
			HubKit.Fill(sb, new Rectangle(view.X, view.Y, view.Width, 2), Color.Black * 0.25f);
		}
	}

	private static List<HubFeature> SearchResults()
	{
		string text = (_search ?? "").Trim().ToLowerInvariant();
		if (text == _searchCacheText && _searchCacheLanguage == L10n.Version)
		{
			return _searchCache;
		}
		_searchCacheText = text;
		_searchCacheLanguage = L10n.Version;
		string[] words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		_searchCache = HubCatalog.AllFeatures
			.Where(feature => words.All(word => feature.SearchText.Contains(word)))
			.OrderByDescending(feature => Score(feature, text))
			.ThenBy(feature => feature.Title)
			.ToList();
		return _searchCache;
	}

	private static int Score(HubFeature feature, string text)
	{
		string title = L10n.T(feature.Title ?? "").ToLowerInvariant();
		if (title == text)
		{
			return 100;
		}
		if (title.StartsWith(text))
		{
			return 80;
		}
		if (title.Contains(text))
		{
			return 60;
		}
		if ((feature.Command ?? "").Contains(text))
		{
			return 50;
		}
		if ((feature.Keywords ?? "").Contains(text))
		{
			return 40;
		}
		return 10;
	}

	private static int DrawSearch(SpriteBatch sb, int x, int y, int width)
	{
		List<HubFeature> results = SearchResults();
		if (results.Count == 0)
		{
			return EmptyNote(sb, x, y, width, HubIcon.Item(I.Binoculars), "Nothing found", "Try other words: \"fly\", \"light\", \"boss\", \".noclip\"...");
		}
		string match = _search.Trim().Split(' ')[0];
		int start = y;
		foreach (IGrouping<HubSection, HubFeature> section in results.GroupBy(feature => feature.Section))
		{
			HubGroup group = new HubGroup(section.Key.Title, section.Key.Icon);
			group.Features.AddRange(section);
			y += DrawGroup(sb, group, x, y, width, match, section.Key.Accent, showWhere: true);
		}
		return y - start;
	}

	internal static int EmptyNote(SpriteBatch sb, int x, int y, int width, HubIcon icon, string title, string text)
	{
		Rectangle r = new Rectangle(x, y, width, 70);
		HubKit.Box(sb, r, Theme.Card * (Opacity * 0.7f));
		float bob = (float)Math.Sin(Main.GlobalTimeWrappedHourly * 2f) * 2f;
		icon.Draw(sb, new Rectangle(r.X + 14, (int)(r.Y + 15 + bob), 40, 40), Color.White);
		HubKit.Text(sb, title, r.X + 66, r.Y + 16, Color.White, 0.9f);
		HubKit.Text(sb, text, r.X + 66, r.Y + 40, Theme.Muted, 0.74f, width - 80);
		return r.Height + 10;
	}

	/// <summary>Groups of feature cards; returns the height used.</summary>
	internal static int DrawGroups(SpriteBatch sb, IEnumerable<HubGroup> groups, int x, int y, int width, string match)
	{
		int start = y;
		foreach (HubGroup group in groups)
		{
			y += DrawGroup(sb, group, x, y, width, match, _section?.Accent ?? Theme.Accent, showWhere: false);
		}
		return y - start;
	}

	internal static int DrawGroup(SpriteBatch sb, HubGroup group, int x, int y, int width, string match, Color accent, bool showWhere)
	{
		int start = y;
		group.Icon.Draw(sb, new Rectangle(x + 2, y + 2, 22, 22), Color.White);
		HubKit.Text(sb, group.Title, x + 30, y + 4, accent, 0.9f);
		float titleW = HubKit.Measure(group.Title, 0.9f);
		int on = group.Features.Count(f => f.Cheat && f.Kind == FeatureKind.Toggle && f.IsOn);
		int right = x + width;
		foreach (HubFeature action in group.HeaderActions)
		{
			string label = (_confirmId == action.Id) ? "Click again" : (action.Verb ?? action.Title);
			float w = HubKit.Measure(label, 0.74f) + 26f;
			Rectangle button = new Rectangle((int)(right - w), y, (int)w, 24);
			if (HubKit.Button(sb, "head:" + action.Id, button, label, (_confirmId == action.Id) ? Theme.Danger * 0.8f : Theme.Card, action.Description))
			{
				Activate(action);
			}
			right = button.X - 6;
		}
		if (on > 0)
		{
			HubKit.Chip(sb, on + " on", right, y + 2, Theme.On * 0.5f, Color.White, 0.66f, alignRight: true);
		}
		HubKit.Line(sb, (int)(x + 36 + titleW), y + 14, Math.Max(0, (int)(right - x - 46 - titleW - (on > 0 ? 50 : 0))), Theme.Muted * 0.15f);
		y += 28;
		if (!string.IsNullOrEmpty(group.Hint) && !HubPrefs.Compact)
		{
			HubKit.Text(sb, group.Hint, x + 30, y - 4, Theme.Muted * 0.9f, 0.68f, width - 40);
			y += 16;
		}
		y += 2;
		y += DrawCards(sb, group.Features, x, y, width, match, showWhere);
		return y - start + 10;
	}

	internal static int DrawCards(SpriteBatch sb, IList<HubFeature> features, int x, int y, int width, string match, bool showWhere = false)
	{
		int start = y;
		int columns = (width >= 600) ? 2 : 1;
		int gap = 8;
		int cardW = (width - gap * (columns - 1)) / columns;
		int column = 0;
		int rowHeight = 0;
		foreach (HubFeature feature in features)
		{
			bool wide = feature.Wide && columns > 1 && feature.Kind != FeatureKind.Toggle;
			int h = CardHeight(feature);
			if (wide && column > 0)
			{
				y += rowHeight + gap;
				column = 0;
				rowHeight = 0;
			}
			Rectangle r = wide ? new Rectangle(x, y, width, h) : new Rectangle(x + column * (cardW + gap), y, cardW, h);
			if (HubInput.Visible(r))
			{
				HubCards.Draw(sb, feature, r, match, showWhere);
			}
			rowHeight = Math.Max(rowHeight, h);
			if (wide)
			{
				y += rowHeight + gap;
				rowHeight = 0;
				column = 0;
				continue;
			}
			column++;
			if (column >= columns)
			{
				column = 0;
				y += rowHeight + gap;
				rowHeight = 0;
			}
		}
		if (column > 0)
		{
			y += rowHeight + gap;
		}
		return y - start;
	}

	internal static int CardHeight(HubFeature feature)
	{
		bool slider = feature.Kind == FeatureKind.Slider;
		if (HubPrefs.Compact)
		{
			return slider ? 46 : 38;
		}
		return HubPrefs.ShowDescriptions ? (slider ? 58 : 68) : (slider ? 56 : 42);
	}

	#endregion

	#region Actions

	/// <summary>The feature's main action (with the "click again" step for the ones that need it).</summary>
	internal static void Activate(HubFeature feature)
	{
		if (feature.Disabled)
		{
			HubInput.Sound(11);
			return;
		}
		if (feature.NeedsConfirm && _confirmId != feature.Id)
		{
			_confirmId = feature.Id;
			_confirmUntil = Main.timeForVisualEffects + 150.0;
			HubInput.Sound(12);
			return;
		}
		_confirmId = null;
		bool native = feature.Row == null;
		bool wasOn = feature.IsOn;
		feature.Primary();
		if (native)
		{
			HubInput.Sound((feature.Kind == FeatureKind.Toggle) ? (wasOn ? 11 : 10) : 12);
		}
		Remember(feature);
	}

	/// <summary>Recently used: only features the catalog knows (not the panel's own buttons).</summary>
	private static void Remember(HubFeature feature)
	{
		if (HubCatalog.Find(feature.Id) != null)
		{
			HubPrefs.Used(feature);
		}
	}

	internal static void ActivateSecondary(HubFeature feature)
	{
		if (feature.Disabled)
		{
			return;
		}
		feature.Secondary();
		if (feature.Row == null)
		{
			HubInput.Sound(12);
		}
		Remember(feature);
	}

	internal static bool Confirming(HubFeature feature)
	{
		return _confirmId == feature.Id;
	}

	internal static IEnumerable<HubFeature> ActiveFeatures()
	{
		return HubCatalog.AllFeatures.Where(feature => feature.Cheat && feature.Kind == FeatureKind.Toggle && feature.IsOn);
	}

	/// <summary>Turns off every feature that is on (click again to confirm).</summary>
	internal static void TurnAllOff()
	{
		foreach (HubFeature feature in ActiveFeatures().ToList())
		{
			feature.Primary();
		}
		HubInput.Sound(11);
	}

	internal static void GoTo(HubFeature feature)
	{
		HubSection section = feature.Section;
		int page = Math.Max(0, section.Pages.FindIndex(p => p.Groups.Contains(feature.Group)));
		Open(section, page);
	}

	internal static void OpenSection(string id, int page = 0)
	{
		HubSection section = (id == "home") ? HubCatalog.Home : HubCatalog.FindSection(id);
		if (section != null)
		{
			Open(section, page);
		}
	}

	#endregion

	#region Popup

	internal static void ShowFeatureMenu(HubFeature feature)
	{
		List<PopupEntry> entries = new List<PopupEntry>();
		bool favorite = HubPrefs.IsFavorite(feature);
		entries.Add(new PopupEntry
		{
			Label = favorite ? "Remove from favorites" : "Add to favorites",
			Icon = HubIcon.Texture(favorite ? "Images/UI/ButtonFavoriteInactive" : "Images/UI/ButtonFavoriteActive"),
			Action = () => HubPrefs.ToggleFavorite(feature)
		});
		if (_section != feature.Section || !string.IsNullOrWhiteSpace(_search))
		{
			entries.Add(new PopupEntry
			{
				Label = "Show in " + feature.Section.Title,
				Icon = feature.Section.Icon,
				Action = () => GoTo(feature)
			});
		}
		if (!string.IsNullOrEmpty(feature.Command))
		{
			entries.Add(new PopupEntry
			{
				Label = "Make a button for " + feature.Command,
				Icon = HubIcon.Item(I.Toolbox),
				Action = delegate
				{
					HubPrefs.Actions.Add(new CustomAction
					{
						Name = feature.Title,
						Commands = feature.Command,
						Icon = Math.Max(1, feature.Icon.ItemType)
					});
					HubPrefs.Changed();
					OpenSection("toolkit");
				}
			});
			entries.Add(new PopupEntry
			{
				Label = "Copy command",
				Icon = HubIcon.Item(I.Sign),
				Action = delegate
				{
					try
					{
						ReLogic.OS.Platform.Get<ReLogic.OS.IClipboard>().Value = feature.Command;
					}
					catch
					{
					}
				}
			});
		}
		ShowPopup(entries);
	}

	private static void ShowPopup(List<PopupEntry> entries)
	{
		_popup = entries;
		_popupAt = new Vector2(HubInput.Mouse.X + 4, HubInput.Mouse.Y + 4);
		_popupOpenedNow = true;
		HubInput.Sound(12);
	}

	private static void DrawPopup(SpriteBatch sb)
	{
		if (_popup == null)
		{
			HubInput.Popup = Rectangle.Empty;
			return;
		}
		int w = (int)Math.Max(180f, _popup.Max(e => HubKit.Measure(e.Label, 0.8f)) + 50f);
		int h = _popup.Count * 30 + 10;
		Rectangle r = new Rectangle((int)_popupAt.X, (int)_popupAt.Y, w, h);
		float uiW = Main.instance.GraphicsDevice.Viewport.Width / Main.UIScale / HubInput.Scale;
		float uiH = Main.instance.GraphicsDevice.Viewport.Height / Main.UIScale / HubInput.Scale;
		r.X = (int)Math.Min(r.X, uiW - w - 4);
		r.Y = (int)Math.Min(r.Y, uiH - h - 4);
		HubInput.Popup = r;
		HubInput.DrawingPopup(true);
		HubKit.Panel(sb, r, Theme.Sidebar, Color.Black);
		int y = r.Y + 5;
		PopupEntry chosen = null;
		foreach (PopupEntry entry in _popup)
		{
			Rectangle row = new Rectangle(r.X + 5, y, r.Width - 10, 28);
			bool hover = HubInput.In(row);
			if (hover)
			{
				HubKit.Box(sb, row, Theme.CardHover);
				if (HubInput.Hover("popup:" + entry.Label) && !_popupOpenedNow)
				{
					chosen = entry;
				}
			}
			entry.Icon.Draw(sb, new Rectangle(row.X + 6, row.Y + 5, 18, 18), Color.White);
			HubKit.Text(sb, entry.Label, row.X + 32, row.Y + 6, hover ? Color.White : new Color(215, 220, 238), 0.8f);
			y += 30;
		}
		HubInput.DrawingPopup(false);
		if (chosen != null)
		{
			_popup = null;
			chosen.Action();
			return;
		}
		if ((HubInput.Clicked || HubInput.RightClicked) && !_popupOpenedNow && !r.Contains(HubInput.Mouse))
		{
			_popup = null;
			HubInput.Consume();
		}
	}

	#endregion
}
