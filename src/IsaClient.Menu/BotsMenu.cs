using System;
using IsaClient.Hub;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.UI;
using I = Terraria.ID.ItemID;

namespace IsaClient.Menu;

/// <summary>
/// The Bots window of the main menu, opened with the robot head under the credits: the panel's Bots section
/// (<see cref="HubBots"/>: Sessions, New Bot, Saved, Options) in a window of its own. Bots keep running from the menu and
/// New Bot connects to any server. The main menu hides behind it (an empty menu page, menuMode 888, like the proxy
/// window) and comes back when it closes (Esc or the X).
/// </summary>
public static class BotsMenu
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

	/// <summary>Where a pick asked the page to go (eased to), or -1.</summary>
	private static float _scrollTarget = -1f;

	private static int _scrollWheel;

	private static readonly string[] Tabs = { "Sessions", "New Bot", "Saved", "Options", "Connection", "Sets" };

	private static readonly int[] TabIcons = { I.RobotHat, I.Mannequin, I.Book, I.Cog, I.WorldGlobe, I.GoldCrown };

	private static HubTheme Theme => HubTheme.Current;

	public static bool IsOpen => _open && Main.gameMenu;

	/// <summary>Only on plain menu screens, not while loading or connecting.</summary>
	public static bool CanOpen => ProxyMenu.CanOpen;

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
		ProxyMenu.Close();
		_returnState = (Main.menuMode == 888) ? Main.MenuUI.CurrentState : null;
		_returnMenuMode = Main.menuMode;
		Main.MenuUI.SetState(_blank);
		Main.menuMode = 888;
		_open = true;
		_fade = 0f;
		_scroll = 0f;
		SoundEngine.PlaySound(10);
	}

	public static void Close()
	{
		if (!_open)
		{
			return;
		}
		_open = false;
		HubInput.Focus = null;
		SoundEngine.PlaySound(11);
		if (!Main.gameMenu || Main.menuMode != 888 || Main.MenuUI.CurrentState != _blank)
		{
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
			Main.MenuUI.SetState(null);
			Main.menuMode = (_returnMenuMode == 888) ? 0 : _returnMenuMode;
		}
		_returnState = null;
	}

	/// <summary>.bot ui: in game the panel's Bots section, in the menu this window. False when neither can open.</summary>
	public static bool OpenFromCommand()
	{
		if (!Main.gameMenu)
		{
			ClientHub.OpenSection("bots");
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

	/// <summary>Main update: the wheel goes to the window, not to the menu behind it.</summary>
	public static void Update()
	{
		if (!IsOpen)
		{
			return;
		}
		if (PlayerInput.ScrollWheelDelta != 0)
		{
			_scrollWheel += PlayerInput.ScrollWheelDelta;
			PlayerInput.ScrollWheelDelta = 0;
			PlayerInput.ScrollWheelDeltaForUI = 0;
		}
	}

	/// <summary>Drawn after the main menu (Main.DrawMenu).</summary>
	public static void Draw(SpriteBatch sb)
	{
		if (!_open)
		{
			return;
		}
		// Leaving the blank page by any other route (connecting, the proxy window, the game) closes the window.
		if (!Main.gameMenu || Main.menuMode != 888 || Main.MenuUI.CurrentState != _blank)
		{
			_open = false;
			HubInput.Focus = null;
			return;
		}
		Action<int> panelTab = HubBots.ShowTab;
		Action<float> panelScroll = HubBots.ScrollTo;
		try
		{
			HubPrefs.EnsureLoaded();
			HubInput.Begin(true);
			if (_scrollWheel != 0)
			{
				HubInput.Wheel = _scrollWheel;
				_scrollWheel = 0;
			}
			_fade = HubPrefs.Animations ? Math.Min(1f, _fade + 0.12f) : 1f;
			HubInput.Matrix = Matrix.CreateTranslation(0f, (1f - _fade) * 16f, 0f) * HubInput.Matrix;
			HubInput.Restart(sb);
			HubBots.ShowTab = tab =>
			{
				_tab = tab;
				_scroll = 0f;
			};
			HubBots.ScrollTo = offset => _scrollTarget = Math.Max(0f, offset);
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
		finally
		{
			HubBots.ShowTab = panelTab;
			HubBots.ScrollTo = panelScroll;
		}
	}

	private static void DrawWindow(SpriteBatch sb)
	{
		float uiW = Main.instance.GraphicsDevice.Viewport.Width / Main.UIScale / HubInput.Scale;
		float uiH = Main.instance.GraphicsDevice.Viewport.Height / Main.UIScale / HubInput.Scale;
		int w = (int)MathHelper.Clamp(uiW - 60f, 640f, 1000f);
		int h = (int)MathHelper.Clamp(uiH - 110f, 420f, 720f);
		Rectangle win = new Rectangle((int)(uiW - w) / 2, (int)Math.Max(20f, (uiH - h) / 2f + 10f), w, h);
		HubKit.Fill(sb, new Rectangle(-10, -10, (int)uiW + 20, (int)uiH + 20), Color.Black * (0.35f * _fade));
		HubKit.Panel(sb, win, Theme.Window * Math.Max(0.92f, HubPrefs.Opacity), Color.Black);
		if (HubInput.In(win))
		{
			HubInput.Hover("bm:window");
		}

		// Header: robot, title, line and close.
		Rectangle header = new Rectangle(win.X + 16, win.Y + 10, win.Width - 32, 50);
		float bob = (float)Math.Sin(Main.GlobalTimeWrappedHourly * 2f) * 2f;
		HubIcon.Item(I.RobotHat).Draw(sb, new Rectangle(header.X, (int)(header.Y + 6 + bob), 36, 36), Color.White);
		HubKit.Title(sb, "Bots", header.X + 48, header.Y + 2, 300, 0.62f, Color.White);
		HubKit.Text(sb, "Extra players you control: play as them, here or on a friend's server, at the same time.", header.X + 50, header.Y + 30, Theme.Muted, 0.7f, header.Width - 100);
		if (HubKit.IconButton(sb, "bm:close", new Rectangle(header.Right - 32, header.Y + 8, 32, 32), HubIcon.Texture("Images/UI/SearchCancel"), "Close (Esc)"))
		{
			Close();
			return;
		}
		HubKit.Line(sb, win.X + 12, header.Bottom + 4, win.Width - 24, Theme.Muted * 0.18f);

		// Tabs.
		Rectangle tabs = new Rectangle(win.X + 16, header.Bottom + 12, win.Width - 32, 38);
		int tx = tabs.X;
		// Six tabs: narrower padding when they would not fit in one row.
		int needed = 0;
		for (int i = 0; i < Tabs.Length; i++)
		{
			needed += (int)HubKit.Measure(Tabs[i] + ((i == 0) ? " (00)" : ""), 0.8f) + 54 + 6;
		}
		int pad = (needed > tabs.Width) ? Math.Max(40, 54 - (needed - tabs.Width) / Tabs.Length - 1) : 54;
		for (int i = 0; i < Tabs.Length; i++)
		{
			string label = Tabs[i] + ((i == 0) ? (" (" + IsaClient.MultiClient.MultiClientManager.Count + ")") : "");
			int tw = (int)HubKit.Measure(label, 0.8f) + pad;
			Rectangle tab = new Rectangle(tx, tabs.Y, tw, tabs.Height);
			bool active = _tab == i;
			bool hover = HubInput.In(tab);
			if (hover && HubInput.Hover("bm:tab:" + i) && !active)
			{
				_tab = i;
				_scroll = 0f;
				if (i == 1)
				{
					IsaClient.MultiClient.MultiClientManager.RefreshCharacters();
				}
				HubInput.Sound(12);
			}
			float glow = HubInput.Anim("bm:tab:" + i, hover || active);
			HubKit.Box(sb, tab, (active ? Color.Lerp(Theme.Slot, Theme.Accent, 0.35f) : Color.Lerp(Theme.Card, Theme.CardHover, glow)) * 0.95f);
			HubIcon.Item(TabIcons[i]).Draw(sb, new Rectangle(tab.X + 10, tab.Y + 9, 20, 20), Color.White);
			HubKit.Text(sb, label, tab.X + 36, tab.Y + 10, active ? Color.White : Theme.Text, 0.8f);
			tx += tw + 6;
		}

		Rectangle body = new Rectangle(win.X + 16, tabs.Bottom + 10, win.Width - 32 - 16, win.Bottom - tabs.Bottom - 22);
		float view = body.Height;
		float max = Math.Max(0f, _contentHeight - view);
		if (HubInput.Wheel != 0 && HubInput.In(new Rectangle(body.X, body.Y, body.Width + 16, body.Height)))
		{
			_scroll -= HubInput.Wheel * 0.4f;
		}
		if (_scrollTarget >= 0f)
		{
			float goal = Math.Min(_scrollTarget, max);
			_scroll = HubPrefs.Animations ? MathHelper.Lerp(_scroll, goal, 0.25f) : goal;
			if (Math.Abs(_scroll - goal) < 1f || HubInput.Wheel != 0)
			{
				_scrollTarget = -1f;
			}
		}
		_scroll = MathHelper.Clamp(_scroll, 0f, max);
		HubInput.BeginClip(sb, body);
		int y = body.Y - (int)_scroll;
		int used = _tab switch
		{
			1 => HubBots.DrawNew(sb, body.X, y, body.Width - 4),
			2 => HubBots.DrawSaved(sb, body.X, y, body.Width - 4),
			3 => HubBots.DrawOptions(sb, body.X, y, body.Width - 4),
			4 => IsaClient.Hub.HubBotRoutes.Draw(sb, body.X, y, body.Width - 4),
			5 => IsaClient.Hub.HubBotSets.Draw(sb, body.X, y, body.Width - 4),
			_ => HubBots.DrawSessions(sb, body.X, y, body.Width - 4)
		};
		HubInput.EndClip(sb);
		_contentHeight = used + 10;
		if (_contentHeight > view)
		{
			_scroll = HubKit.Scrollbar(sb, "bm:scroll", new Rectangle(body.Right + 4, body.Y, 12, body.Height), _scroll, _contentHeight, view, ref _scrollDrag, ref _scrollFrom);
		}
	}
}
