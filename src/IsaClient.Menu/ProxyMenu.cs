using System;
using IsaClient.Hub;
using IsaClient.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.UI;
using I = Terraria.ID.ItemID;

namespace IsaClient.Menu;

/// <summary>
/// The proxy window of the main menu (the Möbius strip under the UUID block): the connection card on the left, and
/// Locations / My Proxies / Settings on the right (<see cref="HubProxy"/> draws them, the panel shows the same pages in
/// game). The main menu is hidden behind it (an empty menu page, menuMode 888) and comes back when it closes.
/// </summary>
public static class ProxyMenu
{
	private static readonly UIState _blank = new UIState();

	private static UIState _returnState;

	private static int _returnMenuMode;

	private static bool _open;

	private static int _tab;

	private static float _scroll;

	private static float _contentHeight;

	private static string _scrollDrag;

	private static float _scrollFrom;

	private static float _fade;

	private static readonly string[] Tabs = { "Locations", "My Proxies", "Settings" };

	private static readonly int[] TabIcons = { I.WorldGlobe, I.GoldenKey, I.Cog };

	public static bool IsOpen => _open && Main.gameMenu;

	/// <summary>Only on plain menu screens, not while loading or connecting.</summary>
	public static bool CanOpen => Main.gameMenu && Main.netMode == 0 && Main.menuMode != 10 && Main.menuMode != 14 && Main.menuMode != 15;

	public static void Toggle()
	{
		if (IsOpen)
		{
			Close();
		}
		else
		{
			Open();
		}
	}

	public static void Open()
	{
		if (!CanOpen || IsOpen)
		{
			return;
		}
		Proxy.EnsureLoaded();
		ProxyPool.Start();
		_returnState = (Main.menuMode == 888) ? Main.MenuUI.CurrentState : null;
		_returnMenuMode = Main.menuMode;
		Main.MenuUI.SetState(_blank);
		Main.menuMode = 888;
		_open = true;
		_fade = 0f;
		_scroll = 0f;
	}

	public static void Close()
	{
		if (!_open)
		{
			return;
		}
		_open = false;
		HubInput.Focus = null;
		RestoreMenu();
	}

	/// <summary>.proxy: in game the panel's Proxy section, in the menu this window. False when neither can open.</summary>
	public static bool OpenFromCommand()
	{
		if (!Main.gameMenu)
		{
			ClientHub.OpenSection("proxy");
			InputState.TildeTerminal = true;
			return true;
		}
		if (CanOpen)
		{
			Open();
			return true;
		}
		return false;
	}

	private static void RestoreMenu()
	{
		if (!Main.gameMenu || Main.menuMode != 888 || Main.MenuUI.CurrentState != _blank)
		{
			// Something else already moved the menu on: leave it there.
			_returnState = null;
			return;
		}
		if (_returnState != null)
		{
			Main.MenuUI.SetState(_returnState);
			Main.menuMode = 888;
		}
		else
		{
			Main.menuMode = (_returnMenuMode == 888) ? 0 : _returnMenuMode;
		}
		_returnState = null;
	}

	/// <summary>Drawn after the main menu (Main.DrawMenu).</summary>
	public static void Draw(SpriteBatch sb)
	{
		if (!_open)
		{
			return;
		}
		// Leaving the blank page by any other route (starting to connect, the game) closes the window.
		if (!Main.gameMenu || Main.menuMode != 888 || Main.MenuUI.CurrentState != _blank)
		{
			_open = false;
			HubInput.Focus = null;
			return;
		}
		try
		{
			HubPrefs.EnsureLoaded();
			HubInput.Begin(true);
			if (ScrollWheel != 0)
			{
				HubInput.Wheel = ScrollWheel;
				ScrollWheel = 0;
			}
			_fade = HubPrefs.Animations ? Math.Min(1f, _fade + 0.12f) : 1f;
			HubInput.Matrix = Matrix.CreateTranslation(0f, (1f - _fade) * 16f, 0f) * HubInput.Matrix;
			HubInput.Restart(sb);
			DrawWindow(sb);
			if (HubInput.Clicked)
			{
				HubInput.Consume();
			}
			HubInput.End(sb);
			string tooltip = HubInput.Tooltip;
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
			if (tooltip != null)
			{
				Main.instance.MouseText(IsaClient.Languages.L10n.T(tooltip), 0, 0);
			}
			if (Main.keyState.IsKeyDown(Keys.Escape) && !Main.oldKeyState.IsKeyDown(Keys.Escape) && HubInput.Focus == null)
			{
				Close();
			}
		}
		catch (Exception ex)
		{
			IsaClient.Core.ClientLog.LogOnce(ex);
			HubInput.Clipping = false;
			try
			{
				sb.End();
			}
			catch
			{
			}
			sb.GraphicsDevice.ScissorRectangle = sb.GraphicsDevice.Viewport.Bounds;
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
		}
	}

	/// <summary>The wheel of this frame (read in Update, before the menu takes it).</summary>
	private static int ScrollWheel;

	public static void Update()
	{
		ProxyPool.PlayingOnline = !Main.gameMenu && Main.netMode == 1;
		if (!IsOpen)
		{
			return;
		}
		if (PlayerInput.ScrollWheelDelta != 0)
		{
			ScrollWheel += PlayerInput.ScrollWheelDelta;
			PlayerInput.ScrollWheelDelta = 0;
			PlayerInput.ScrollWheelDeltaForUI = 0;
		}
	}

	private static HubTheme Theme => HubTheme.Current;

	private static void DrawWindow(SpriteBatch sb)
	{
		float uiW = Main.instance.GraphicsDevice.Viewport.Width / Main.UIScale / HubInput.Scale;
		float uiH = Main.instance.GraphicsDevice.Viewport.Height / Main.UIScale / HubInput.Scale;
		int w = (int)MathHelper.Clamp(uiW - 60f, 640f, 1040f);
		int h = (int)MathHelper.Clamp(uiH - 110f, 420f, 680f);
		Rectangle win = new Rectangle((int)(uiW - w) / 2, (int)Math.Max(20f, (uiH - h) / 2f + 10f), w, h);
		// The game behind it dims a little.
		HubKit.Fill(sb, new Rectangle(-10, -10, (int)uiW + 20, (int)uiH + 20), Color.Black * (0.35f * _fade));
		HubKit.Panel(sb, win, Theme.Window * Math.Max(0.92f, HubPrefs.Opacity), Color.Black);
		if (HubInput.In(win))
		{
			HubInput.Hover("px:window");
		}

		// Header: shield, title, line and close.
		Rectangle header = new Rectangle(win.X + 16, win.Y + 10, win.Width - 32, 50);
		HubIcon.Item(I.CobaltShield).Draw(sb, new Rectangle(header.X, header.Y + 6, 36, 36), Color.White);
		HubKit.Title(sb, "Proxy", header.X + 48, header.Y + 2, 300, 0.62f, Color.White);
		HubKit.Text(sb, "Hide your IP from the servers you join: pick a country, like a VPN.", header.X + 50, header.Y + 30, Theme.Muted, 0.7f, header.Width - 100);
		if (HubKit.IconButton(sb, "px:close", new Rectangle(header.Right - 32, header.Y + 8, 32, 32), HubIcon.Texture("Images/UI/SearchCancel"), "Close (Esc)"))
		{
			Close();
			return;
		}
		HubKit.Line(sb, win.X + 12, header.Bottom + 4, win.Width - 24, Theme.Muted * 0.18f);

		int top = header.Bottom + 12;
		int leftW = (win.Width >= 880) ? 320 : 290;
		Rectangle left = new Rectangle(win.X + 12, top, leftW, win.Bottom - top - 12);
		HubProxy.ShowLocations = () =>
		{
			_tab = 0;
			_scroll = 0f;
		};
		HubProxy.DrawHero(sb, left);

		Rectangle right = new Rectangle(left.Right + 12, top, win.Right - left.Right - 24, left.Height);
		Rectangle tabs = new Rectangle(right.X, right.Y, right.Width, 38);
		int tx = tabs.X;
		for (int i = 0; i < Tabs.Length; i++)
		{
			int tw = (int)HubKit.Measure(Tabs[i], 0.8f) + 54;
			Rectangle tab = new Rectangle(tx, tabs.Y, tw, tabs.Height);
			bool active = _tab == i;
			bool hover = HubInput.In(tab);
			if (hover && HubInput.Hover("px:tab:" + i) && !active)
			{
				_tab = i;
				_scroll = 0f;
				HubInput.Sound(12);
			}
			float glow = HubInput.Anim("px:tab:" + i, hover || active);
			HubKit.Box(sb, tab, (active ? Color.Lerp(Theme.Slot, Theme.Accent, 0.35f) : Color.Lerp(Theme.Card, Theme.CardHover, glow)) * 0.95f);
			HubIcon.Item(TabIcons[i]).Draw(sb, new Rectangle(tab.X + 10, tab.Y + 9, 20, 20), Color.White);
			HubKit.Text(sb, Tabs[i], tab.X + 36, tab.Y + 10, active ? Color.White : Theme.Text, 0.8f);
			tx += tw + 6;
		}

		Rectangle body = new Rectangle(right.X, tabs.Bottom + 8, right.Width - 16, right.Bottom - tabs.Bottom - 8);
		float view = body.Height;
		float max = Math.Max(0f, _contentHeight - view);
		if (HubInput.Wheel != 0 && HubInput.In(new Rectangle(body.X, body.Y, body.Width + 16, body.Height)))
		{
			_scroll -= HubInput.Wheel * 0.4f;
		}
		_scroll = MathHelper.Clamp(_scroll, 0f, max);
		HubInput.BeginClip(sb, body);
		int y = body.Y - (int)_scroll;
		int used = _tab switch
		{
			1 => HubProxy.DrawMine(sb, body.X, y, body.Width - 4),
			2 => HubProxy.DrawSettings(sb, body.X, y, body.Width - 4),
			_ => HubProxy.DrawLocations(sb, body.X, y, body.Width - 4)
		};
		HubInput.EndClip(sb);
		_contentHeight = used + 10;
		if (_contentHeight > view)
		{
			_scroll = HubKit.Scrollbar(sb, "px:scroll", new Rectangle(body.Right + 4, body.Y, 12, body.Height), _scroll, _contentHeight, view, ref _scrollDrag, ref _scrollFrom);
		}
	}
}
