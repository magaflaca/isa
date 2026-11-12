using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.IO;
using Terraria.UI;
using IsaClient.Menu;
using IsaClient.Community;
using Terraria;

namespace IsaClient.Music;

/// <summary>
/// The music player: a movable window (Now Playing, Queue, Library, Favorites, playlists, YouTube search, and a player
/// bar with shuffle / previous / play / next / repeat / volume / seek) and a floating mini player that can be moved,
/// collapsed to a small pill or closed. Immediate mode, drawn on top of the game UI (and in the main menu), with
/// Terraria's own icons; every song gets an achievement icon as its cover.
/// </summary>
public static class MusicPlayerWindow
{
	private enum View
	{
		NowPlaying,
		Queue,
		Library,
		Favorites,
		Playlist,
		Search
	}

	internal sealed class MenuEntry
	{
		public string Label;

		public Action Action;

		public bool Header;
	}

	// Look
	internal static readonly Color BackColor = new Color(33, 43, 79);

	internal static readonly Color PanelColor = new Color(44, 57, 105);

	internal static readonly Color PanelDarkColor = new Color(26, 34, 66);

	private static readonly Color _row = new Color(52, 66, 120);

	internal static readonly Color RowHoverColor = new Color(72, 92, 160);

	internal static readonly Color SelectedColor = new Color(86, 112, 190);

	internal static readonly Color AccentColor = new Color(255, 231, 69);

	private static readonly Color _pink = new Color(255, 150, 210);

	internal static readonly Color MutedColor = new Color(154, 167, 190);

	private static readonly Color _green = new Color(120, 220, 120);

	// Settings (IsaMusic.json)
	public static bool WindowOpen;

	public static bool MiniVisible = true;

	public static bool MiniCollapsed;

	private static Vector2 _winPos = new Vector2(float.NaN, float.NaN);

	internal static Vector2 MiniPosition = new Vector2(float.NaN, float.NaN);

	// State
	private static View _view;

	private static string _playlist;

	internal static readonly Dictionary<string, float> ScrollOffsets = new Dictionary<string, float>();

	internal static readonly Dictionary<string, float> ContentHeights = new Dictionary<string, float>();

	internal static string FocusedField;

	private static string _libraryFilter = "";

	private static string _searchText = "";

	private static string _newPlaylist = "";

	private static string _renameText = "";

	private static bool _creatingPlaylist;

	private static bool _renaming;

	internal static List<MenuEntry> MenuEntries;

	private static Vector2 _menuPos;

	private static Rectangle _menuRect;

	private static bool _menuOpenedNow;

	private static bool _focusSetNow;

	private static string _confirm;

	private static double _confirmUntil;

	internal static string HoveredControl;

	private static string _lastHover;

	internal static string PendingTooltip;

	internal static Point MousePoint;

	internal static bool Clicked;

	private static bool _rightClick;

	private static bool _dragWin;

	internal static bool DraggingMini;

	private static bool _dragSeek;

	private static string _seekOwner;

	private static bool _dragVolume;

	private static Vector2 _dragOffset;

	private static Rectangle _winRect;

	internal static Rectangle MiniRect;

	private static Rectangle _winScreen;

	internal static Rectangle MiniScreenRect;

	internal static int WheelDelta;

	private static bool _stashed;

	private static bool _stLeft;

	private static bool _stLeftRelease;

	private static bool _stRight;

	private static bool _stRightRelease;

	internal static Rectangle ClipRect;

	internal static bool Clipping;

	internal static readonly object InputMarker = new object();

	private static int _libraryVersion = -1;

	private static List<MusicLibrary.Song> _libraryCache = new List<MusicLibrary.Song>();

	private static string _libraryCacheFilter;

	// Stacking: the music window, the mini player and the client's panel (Cheats...) are drawn back to front in this
	// order; whatever is clicked comes to the front, and only the top-most one under the mouse gets the mouse.
	private const string LayerWindow = "win";

	private const string LayerMini = "mini";

	private const string LayerPanel = "panel";

	private static readonly List<string> _zOrder = new List<string> { LayerPanel, LayerMini, LayerWindow };

	/// <summary>Another floating window that stacks and shares the mouse with the music windows (the video player).</summary>
	internal sealed class FloatingSurface
	{
		public string Layer;

		public Func<bool> Shown;

		/// <summary>Where it is on the screen (pixels), for the mouse.</summary>
		public Func<Rectangle> ScreenRect;

		public Action<SpriteBatch, int, int> Draw;
	}

	private static readonly List<FloatingSurface> _surfaces = new List<FloatingSurface>();

	/// <summary>A floating surface is dragging something (its window, a slider): the mouse stays with it.</summary>
	internal static bool SurfaceDragging;

	private static readonly Point _offscreen = new Point(-100000, -100000);

	private static string _owner;

	private static string _layer;

	private static string _dragLayer;

	private static bool _panelWasOpen;

	private static bool _frameBegun;

	private static bool _frameActive;

	private static Point _realMouse;

	private static bool _mouseDown;

	private static bool _clickedRaw;

	private static bool _menuWasOpen;

	private static bool _menuClickInside;

	private static int _screenW;

	private static int _screenH;

	private static string _toast;

	private static double _toastUntil;

	/// <summary>Controls → "Music (IsaClient)": hotkeys for the player (unbound by default).</summary>
	public static readonly List<string> HotkeyTriggers = new List<string> { "Music Player", "Mini Player", "Play/Pause Music", "Stop Music", "Next Song", "Previous Song", "Music Volume Up", "Music Volume Down", "Music Shuffle", "Music Repeat", "Favorite Song" };

	#region Settings

	public static void LoadSettings(Preferences config)
	{
		MiniVisible = config.Get("MiniPlayer", true);
		MiniCollapsed = config.Get("MiniPlayerCollapsed", false);
		MiniPosition = LoadPosition(config, "MiniPlayerX", "MiniPlayerY");
		_winPos = LoadPosition(config, "WindowX", "WindowY");
	}

	private static Vector2 LoadPosition(Preferences config, string x, string y)
	{
		Vector2 pos = new Vector2(config.Get(x, -1f), config.Get(y, -1f));
		if (pos.X < 0f || pos.Y < 0f)
		{
			return new Vector2(float.NaN, float.NaN);
		}
		return pos;
	}

	public static void SaveSettings(Preferences config)
	{
		config.Put("MiniPlayer", MiniVisible);
		config.Put("MiniPlayerCollapsed", MiniCollapsed);
		config.Put("MiniPlayerX", float.IsNaN(MiniPosition.X) ? -1f : MiniPosition.X);
		config.Put("MiniPlayerY", float.IsNaN(MiniPosition.Y) ? -1f : MiniPosition.Y);
		config.Put("WindowX", float.IsNaN(_winPos.X) ? -1f : _winPos.X);
		config.Put("WindowY", float.IsNaN(_winPos.Y) ? -1f : _winPos.Y);
	}

	/// <summary>True when the music window or the mini player is under this point (UI coordinates).</summary>
	internal static bool CoversUiPoint(Point point)
	{
		return _winRect.Contains(point) || MiniRect.Contains(point);
	}

	internal static void AddSurface(FloatingSurface surface)
	{
		_surfaces.Add(surface);
		_zOrder.Add(surface.Layer);
	}

	private static FloatingSurface SurfaceFor(string layer)
	{
		foreach (FloatingSurface surface in _surfaces)
		{
			if (surface.Layer == layer)
			{
				return surface;
			}
		}
		return null;
	}

	private static bool AnySurfaceShown
	{
		get
		{
			foreach (FloatingSurface surface in _surfaces)
			{
				if (surface.Shown())
				{
					return true;
				}
			}
			return false;
		}
	}

	internal static void BringSurfaceToFront(string layer)
	{
		BringToFront(layer);
	}

	internal static bool MouseDown => _mouseDown;

	internal static bool RightClicked => _rightClick;

	/// <summary>A floating surface starts dragging something: the mouse stays with it until the button is let go.</summary>
	internal static void StartSurfaceDrag()
	{
		SurfaceDragging = true;
		_dragLayer = _layer;
		Clicked = false;
		Main.mouseLeftRelease = false;
	}

	public static void ToggleWindow()
	{
		WindowOpen = !WindowOpen;
		FocusedField = null;
		MenuEntries = null;
		if (WindowOpen)
		{
			BringToFront(LayerWindow);
		}
		SoundEngine.PlaySound(WindowOpen ? 10 : 11);
	}

	public static void ToggleMini()
	{
		MiniVisible = !MiniVisible;
		if (MiniVisible)
		{
			BringToFront(LayerMini);
		}
		MusicPlayer.Save();
		MusicPlayer.Say("Mini player " + (MiniVisible ? "on: it shows while a song plays (drag it anywhere)." : "off (.miniplayer brings it back)."));
	}

	public static void OnSongStarted()
	{
	}

	private static bool MiniShown
	{
		get
		{
			if (MiniVisible && MusicPlayer.Enabled)
			{
				if (!MusicPlayer.HasSong)
				{
					return MusicPlayer.Loading;
				}
				return true;
			}
			return false;
		}
	}

	#endregion

	#region Input plumbing

	/// <summary>Mod tick: hotkeys, and the mouse wheel over a music window scrolls it, not the hotbar.</summary>
	public static void Update()
	{
		HandleHotkeys();
		Point raw = new Point(PlayerInput.MouseX, PlayerInput.MouseY);
		string owner = PlayerFileDropDialog.IsOpen ? null : OwnerAt(raw);
		bool over = owner != null && owner != LayerPanel;
		if (over && PlayerInput.ScrollWheelDelta != 0)
		{
			WheelDelta += PlayerInput.ScrollWheelDelta;
			PlayerInput.ScrollWheelDelta = 0;
			PlayerInput.ScrollWheelDeltaForUI = 0;
		}
	}

	private static bool Dragging => _dragWin || DraggingMini || _dragSeek || _dragVolume || SurfaceDragging;

	/// <summary>The client's panel (Cheats, inventory viewer, character window) is open in game.</summary>
	private static bool PanelVisible => !Main.gameMenu && InputState.TildeTerminal && MenuState.UiRework && MenuState.CustomUI2 != null && MenuState.CustomUI2.CurrentState != null;

	private static bool PanelContains(Point raw)
	{
		if (!PanelVisible)
		{
			return false;
		}
		if (IsaClient.Hub.ClientHub.TakesPanel)
		{
			return IsaClient.Hub.ClientHub.ScreenRect.Contains(raw);
		}
		try
		{
			UIState state = MenuState.CustomUI2.CurrentState;
			UIElement at = state.GetElementAt(new Vector2(raw.X, raw.Y) / Main.UIScale);
			return at != null && at != state;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>The top-most surface under a screen point (music window, mini player or the client's panel), or null.</summary>
	private static string OwnerAt(Point raw)
	{
		for (int i = _zOrder.Count - 1; i >= 0; i--)
		{
			string layer = _zOrder[i];
			if ((layer == LayerWindow && WindowOpen && _winScreen.Contains(raw)) || (layer == LayerMini && MiniShown && MiniScreenRect.Contains(raw)) || (layer == LayerPanel && PanelContains(raw)))
			{
				return layer;
			}
			FloatingSurface surface = SurfaceFor(layer);
			if (surface != null && surface.Shown() && surface.ScreenRect().Contains(raw))
			{
				return layer;
			}
		}
		return null;
	}

	private static void BringToFront(string layer)
	{
		if (_zOrder.Remove(layer))
		{
			_zOrder.Add(layer);
		}
	}

	/// <summary>The mouse belongs to a music window this frame: the client's panel behind it must not see it.</summary>
	public static bool MouseOwnedByMusic
	{
		get
		{
			if (PlayerFileDropDialog.IsOpen || (!WindowOpen && !MiniShown && !AnySurfaceShown))
			{
				return false;
			}
			return MenuEntries != null || Dragging || _owner == LayerWindow || _owner == LayerMini || SurfaceFor(_owner) != null;
		}
	}

	/// <summary>Before the game draws its interface: works out who is on top under the mouse; clicks on a music window are kept for it alone.</summary>
	public static void BeforeInterface()
	{
		_stashed = false;
		_frameBegun = false;
		bool panelOpen = PanelVisible;
		if (panelOpen && !_panelWasOpen)
		{
			BringToFront(LayerPanel);
		}
		_panelWasOpen = panelOpen;
		if (PlayerFileDropDialog.IsOpen)
		{
			_owner = null;
			return;
		}
		Point raw = new Point(PlayerInput.MouseX, PlayerInput.MouseY);
		if (Dragging)
		{
			_owner = _dragLayer;
		}
		else if (panelOpen && MenuState.DraggingCustomUISlider)
		{
			_owner = LayerPanel;
		}
		else
		{
			_owner = OwnerAt(raw);
		}
		bool press = (Main.mouseLeft && Main.mouseLeftRelease) || (Main.mouseRight && Main.mouseRightRelease);
		bool inMenu = MenuEntries != null && ToScreen(_menuRect).Contains(raw);
		if (press && !Dragging && _owner != null && !inMenu)
		{
			BringToFront(_owner);
		}
		bool over = MenuEntries != null || Dragging || _owner == LayerWindow || _owner == LayerMini || SurfaceFor(_owner) != null;
		if (!over)
		{
			return;
		}
		_stashed = true;
		_stLeft = Main.mouseLeft;
		_stLeftRelease = Main.mouseLeftRelease;
		_stRight = Main.mouseRight;
		_stRightRelease = Main.mouseRightRelease;
		Main.mouseLeft = false;
		Main.mouseLeftRelease = false;
		Main.mouseRight = false;
		Main.mouseRightRelease = false;
		if (Main.LocalPlayer != null)
		{
			Main.LocalPlayer.mouseInterface = true;
		}
	}

	internal static Rectangle ToScreen(Rectangle ui)
	{
		Vector2 a = Vector2.Transform(new Vector2(ui.X, ui.Y), Main.UIScaleMatrix);
		Vector2 b = Vector2.Transform(new Vector2(ui.Right, ui.Bottom), Main.UIScaleMatrix);
		return new Rectangle((int)a.X, (int)a.Y, (int)(b.X - a.X), (int)(b.Y - a.Y));
	}

	/// <summary>The menu draws with point sampling, the game UI layers with linear: restart the batch the same way.</summary>
	private static SamplerState Sampler => Main.gameMenu ? SamplerState.PointClamp : SamplerState.LinearClamp;

	internal static void BeginClip(SpriteBatch sb, Rectangle area)
	{
		Rectangle screen = ToScreen(area);
		Rectangle viewport = sb.GraphicsDevice.Viewport.Bounds;
		Rectangle scissor = Rectangle.Intersect(screen, viewport);
		if (scissor.Width <= 0 || scissor.Height <= 0)
		{
			scissor = new Rectangle(0, 0, 1, 1);
		}
		sb.End();
		sb.GraphicsDevice.ScissorRectangle = scissor;
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Sampler, DepthStencilState.None, new RasterizerState
		{
			CullMode = CullMode.None,
			ScissorTestEnable = true
		}, null, Main.UIScaleMatrix);
		ClipRect = area;
		Clipping = true;
	}

	internal static void EndClip(SpriteBatch sb)
	{
		sb.End();
		// Terraria's UI clips its lists against the current scissor: leave the whole screen, not our last box.
		sb.GraphicsDevice.ScissorRectangle = sb.GraphicsDevice.Viewport.Bounds;
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Sampler, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
		Clipping = false;
	}

	internal static bool MouseIn(Rectangle r)
	{
		if (!r.Contains(MousePoint))
		{
			return false;
		}
		if (Clipping && !ClipRect.Contains(MousePoint))
		{
			return false;
		}
		if (MenuEntries != null && _menuRect.Contains(MousePoint))
		{
			return false;
		}
		return true;
	}

	internal static bool Visible(Rectangle r)
	{
		return !Clipping || (r.Bottom > ClipRect.Top && r.Top < ClipRect.Bottom);
	}
	#endregion
	#region Drawing helpers

	#endregion

	#region Frame

	/// <summary>In game, just before the client's panel: the music windows that are behind it.</summary>
	public static void DrawBehindPanel(SpriteBatch sb)
	{
		if (!PanelVisible || !BeginFrame())
		{
			return;
		}
		int panel = _zOrder.IndexOf(LayerPanel);
		for (int i = 0; i < panel; i++)
		{
			DrawSurface(sb, _zOrder[i]);
		}
	}

	/// <summary>On top of the game UI (and in the main menu): the music windows in front of the panel, menus, tooltips.</summary>
	public static void Draw(SpriteBatch sb)
	{
		DrawToast(sb);
		if (!BeginFrame())
		{
			RestoreMouse();
			_frameBegun = false;
			return;
		}
		int first = PanelVisible ? (_zOrder.IndexOf(LayerPanel) + 1) : 0;
		for (int i = first; i < _zOrder.Count; i++)
		{
			if (_zOrder[i] != LayerPanel)
			{
				DrawSurface(sb, _zOrder[i]);
			}
		}
		_layer = null;
		MousePoint = _realMouse;
		if (MenuEntries != null)
		{
			DrawMenu(sb, _screenW, _screenH);
		}
		EndFrame();
	}

	/// <summary>Once per frame, by whichever pass draws first: the mouse state the music windows work with.</summary>
	private static bool BeginFrame()
	{
		if (_frameBegun)
		{
			return _frameActive;
		}
		_frameBegun = true;
		bool mini = MiniShown;
		if (!WindowOpen)
		{
			_winScreen = Rectangle.Empty;
			_winRect = Rectangle.Empty;
		}
		if (!mini)
		{
			MiniScreenRect = Rectangle.Empty;
			MiniRect = Rectangle.Empty;
		}
		if (!WindowOpen && !mini && !AnySurfaceShown)
		{
			SurfaceDragging = false;
			MenuEntries = null;
			FocusedField = null;
			_dragWin = false;
			DraggingMini = false;
			_dragSeek = false;
			_dragVolume = false;
			_frameActive = false;
			return false;
		}
		_realMouse = new Point(Main.mouseX, Main.mouseY);
		bool left = _stashed ? _stLeft : Main.mouseLeft;
		bool leftRelease = _stashed ? _stLeftRelease : Main.mouseLeftRelease;
		bool right = _stashed ? _stRight : Main.mouseRight;
		bool rightRelease = _stashed ? _stRightRelease : Main.mouseRightRelease;
		_mouseDown = left;
		if (!left)
		{
			SurfaceDragging = false;
		}
		Clicked = left && leftRelease;
		_rightClick = right && rightRelease;
		if (PlayerFileDropDialog.IsOpen)
		{
			Clicked = false;
			_rightClick = false;
			WheelDelta = 0;
		}
		_clickedRaw = Clicked || _rightClick;
		HoveredControl = null;
		PendingTooltip = null;
		_menuOpenedNow = false;
		_focusSetNow = false;
		// The real screen in UI units (Main.screenWidth is already divided by the UI scale while the UI draws).
		_screenW = (int)(Main.instance.GraphicsDevice.Viewport.Width / Main.UIScale);
		_screenH = (int)(Main.instance.GraphicsDevice.Viewport.Height / Main.UIScale);
		_menuWasOpen = MenuEntries != null;
		_menuClickInside = _menuWasOpen && _menuRect.Contains(_realMouse);
		_frameActive = true;
		return true;
	}

	/// <summary>Only the surface that owns the mouse sees it; the others are drawn as if it were far away.</summary>
	private static void DrawSurface(SpriteBatch sb, string layer)
	{
		_layer = layer;
		MousePoint = (_owner == layer) ? _realMouse : _offscreen;
		if (layer == LayerMini && MiniShown)
		{
			MiniPlayer.DrawMini(sb, _screenW, _screenH);
		}
		else if (layer == LayerWindow && WindowOpen)
		{
			DrawWindow(sb, _screenW, _screenH);
		}
		else
		{
			FloatingSurface surface = SurfaceFor(layer);
			if (surface != null && surface.Shown())
			{
				surface.Draw(sb, _screenW, _screenH);
			}
		}
	}

	private static void RestoreMouse()
	{
		if (_stashed)
		{
			Main.mouseLeft = _stLeft;
			Main.mouseLeftRelease = _stLeftRelease;
			Main.mouseRight = _stRight;
			Main.mouseRightRelease = _stRightRelease;
			_stashed = false;
		}
	}

	private static void EndFrame()
	{
		if (_menuWasOpen && (Clicked || _rightClick) && !_menuClickInside && MenuEntries != null && !_menuOpenedNow)
		{
			MenuEntries = null;
		}
		bool overAny = _owner == LayerWindow || _owner == LayerMini || SurfaceFor(_owner) != null || (MenuEntries != null && _menuRect.Contains(_realMouse));
		if (overAny && Main.LocalPlayer != null)
		{
			Main.LocalPlayer.mouseInterface = true;
		}
		if (Clicked && FocusedField != null && !_focusSetNow && (HoveredControl == null || !HoveredControl.StartsWith("field:")))
		{
			FocusedField = null;
		}
		if (HoveredControl != null && HoveredControl != _lastHover && !HoveredControl.StartsWith("field:") && !HoveredControl.StartsWith("row:"))
		{
			SoundEngine.PlaySound(12);
		}
		_lastHover = HoveredControl;
		bool consumed = _stashed;
		RestoreMouse();
		if (_clickedRaw && (overAny || consumed))
		{
			Main.mouseLeftRelease = false;
			Main.mouseRightRelease = false;
		}
		if (PendingTooltip != null)
		{
			Main.instance.MouseText(IsaClient.Languages.L10n.T(PendingTooltip), 0, 0);
		}
		WheelDelta = 0;
		if (_confirm != null && Main.timeForVisualEffects > _confirmUntil)
		{
			_confirm = null;
		}
		_frameBegun = false;
	}

	/// <summary>A short note at the top of the screen for hotkeys (volume, shuffle...).</summary>
	private static void Toast(string text)
	{
		_toast = text;
		_toastUntil = Main.timeForVisualEffects + 110.0;
	}

	private static void DrawToast(SpriteBatch sb)
	{
		if (_toast == null)
		{
			return;
		}
		double left = _toastUntil - Main.timeForVisualEffects;
		if (left <= 0.0)
		{
			_toast = null;
			return;
		}
		float alpha = (float)Math.Min(1.0, left / 20.0);
		int screenW = (int)(Main.instance.GraphicsDevice.Viewport.Width / Main.UIScale);
		float textW = MusicDrawing.Measure(_toast, 0.85f);
		Rectangle r = new Rectangle((int)((screenW - textW - 56f) / 2f), 84, (int)textW + 56, 36);
		MusicDrawing.Box(sb, r, BackColor * 0.95f * alpha);
		MusicDrawing.Icon(sb, MusicDrawing.Tex("Images/UI/CharCreation/Voice"), null, new Rectangle(r.X + 10, r.Y + 6, 20, 24), Color.White * alpha);
		MusicDrawing.Text(sb, _toast, r.X + 38, r.Y + 9, Color.White * alpha, 0.85f);
	}

	private static void HandleHotkeys()
	{
		if (!Main.hasFocus || PlayerInput.WritingText || Main.drawingPlayerChat || Main.editSign || Main.editChest || PlayerInput.CurrentlyRebinding || Main.inFancyUI && Main.gameMenu)
		{
			return;
		}
		TriggersSet pressed = PlayerInput.Triggers.JustPressed;
		if (pressed.KeyStatusHandler("Music Player"))
		{
			ToggleWindow();
		}
		if (pressed.KeyStatusHandler("Mini Player"))
		{
			ToggleMini();
		}
		if (pressed.KeyStatusHandler("Play/Pause Music"))
		{
			bool had = MusicPlayer.HasSong;
			TogglePlay();
			if (had)
			{
				Toast(MusicPlayer.IsPaused ? "Paused" : "Playing");
			}
		}
		if (pressed.KeyStatusHandler("Stop Music") && MusicPlayer.HasSong)
		{
			MusicPlayer.Stop(clearQueue: false);
			Toast("Stopped");
		}
		if (pressed.KeyStatusHandler("Next Song"))
		{
			MusicPlayer.Skip();
			Toast("Next song");
		}
		if (pressed.KeyStatusHandler("Previous Song"))
		{
			MusicPlayer.Previous();
			Toast("Previous song");
		}
		if (pressed.KeyStatusHandler("Music Volume Up") || pressed.KeyStatusHandler("Music Volume Down"))
		{
			float step = pressed.KeyStatusHandler("Music Volume Up") ? 0.05f : -0.05f;
			MusicPlayer.SetVolume(MathHelper.Clamp((float)Math.Round((MusicPlayer.Volume + step) * 20f) / 20f, 0f, 1f));
			MusicPlayer.Save();
			Toast("Volume " + (int)Math.Round(MusicPlayer.Volume * 100f) + "%");
		}
		if (pressed.KeyStatusHandler("Music Shuffle"))
		{
			MusicPlayer.ToggleShuffle();
			Toast("Shuffle " + (MusicPlayer.Shuffle ? "on" : "off"));
		}
		if (pressed.KeyStatusHandler("Music Repeat"))
		{
			MusicPlayer.CycleLoop();
			Toast("Repeat: " + ((MusicPlayer.Loop == MusicPlayer.LoopMode.Off) ? "off" : ((MusicPlayer.Loop == MusicPlayer.LoopMode.Song) ? "this song" : "the whole queue")));
		}
		if (pressed.KeyStatusHandler("Favorite Song") && MusicPlayer.CurrentKey != null)
		{
			string key = MusicPlayer.CurrentKey;
			bool favorite = !MusicLibrary.IsFavorite(key);
			MusicLibrary.SetFavorite(key, favorite);
			Toast(favorite ? "Added to favorites" : "Removed from favorites");
		}
	}

	internal static Rectangle ClampToScreen(ref Vector2 pos, int w, int h, int screenW, int screenH, Vector2 fallback)
	{
		if (float.IsNaN(pos.X) || float.IsNaN(pos.Y))
		{
			pos = fallback;
		}
		pos.X = MathHelper.Clamp(pos.X, 0f, Math.Max(0, screenW - w));
		pos.Y = MathHelper.Clamp(pos.Y, 0f, Math.Max(0, screenH - h));
		return new Rectangle((int)pos.X, (int)pos.Y, w, h);
	}

	internal static void HandleDrag(ref bool dragging, ref Vector2 pos, Rectangle handle, string blockedBy)
	{
		if (dragging)
		{
			if (!_mouseDown)
			{
				dragging = false;
				MusicPlayer.Save();
			}
			else
			{
				// Never above or left of the screen: a negative position is saved as "not placed yet" and would jump back.
				pos = Vector2.Max(Vector2.Zero, new Vector2(MousePoint.X, MousePoint.Y) - _dragOffset);
			}
		}
		else if (Clicked && handle.Contains(MousePoint) && HoveredControl == null && (MenuEntries == null || !_menuRect.Contains(MousePoint)))
		{
			dragging = true;
			_dragLayer = _layer;
			_dragOffset = new Vector2(MousePoint.X, MousePoint.Y) - pos;
			Clicked = false;
			Main.mouseLeftRelease = false;
		}
	}
	#endregion
	#region Mini player

	#endregion

	#region Shared controls

	internal static void TogglePlay()
	{
		if (MusicPlayer.HasSong)
		{
			MusicPlayer.TogglePause();
			return;
		}
		List<MusicPlayer.Track> queue = MusicPlayer.QueueSnapshot();
		if (queue.Count > 0)
		{
			MusicPlayer.JumpTo(0);
			return;
		}
		List<MusicLibrary.Song> favorites = FavoriteSongs();
		List<MusicLibrary.Song> songs = (favorites.Count > 0) ? favorites : LibrarySongs("");
		if (songs.Count > 0)
		{
			MusicPlayer.PlayList(songs.Select(MusicPlayer.TrackFor).ToList(), MusicPlayer.Shuffle ? Main.rand.Next(songs.Count) : 0, (favorites.Count > 0) ? "your favorites" : "your library");
		}
		else
		{
			MusicPlayer.Say("Nothing to play yet: search a song (Search tab) or use .play <song>.");
		}
	}

	/// <summary>Shuffle, previous, play/pause, next, repeat (and the heart of the current song).</summary>
	internal static int Controls(SpriteBatch sb, string id, int x, int y, int size, string key, bool small)
	{
		int gap = small ? 4 : 6;
		Texture2D playPause = MusicDrawing.Tex("Images/UI/IconPlayPause");
		if (MusicDrawing.IconButton(sb, id + ":shuffle", new Rectangle(x, y, size, size), MusicDrawing.Tex("Images/UI/CharCreation/Randomize"), null, "Shuffle: " + (MusicPlayer.Shuffle ? "on" : "off"), MusicPlayer.Shuffle))
		{
			MusicPlayer.ToggleShuffle();
		}
		x += size + gap;
		if (MusicDrawing.IconButton(sb, id + ":prev", new Rectangle(x, y, size, size), MusicDrawing.Tex("Images/UI/IconPrev"), null, "Previous (restarts the song after its first seconds)"))
		{
			MusicPlayer.Previous();
		}
		x += size + gap;
		int big = small ? size : (size + 8);
		if (MusicDrawing.IconButton(sb, id + ":play", new Rectangle(x, y - (big - size) / 2, big, big), playPause, new Rectangle(0, MusicPlayer.IsPlaying ? 16 : 0, 16, 16), MusicPlayer.IsPlaying ? "Pause" : "Play", active: MusicPlayer.IsPlaying))
		{
			TogglePlay();
		}
		x += big + gap;
		if (MusicDrawing.IconButton(sb, id + ":next", new Rectangle(x, y, size, size), MusicDrawing.Tex("Images/UI/IconNext"), null, "Next", enabled: MusicPlayer.HasSong || MusicPlayer.QueueCount > 0))
		{
			MusicPlayer.Skip();
		}
		x += size + gap;
		string loop = (MusicPlayer.Loop == MusicPlayer.LoopMode.Off) ? "off" : ((MusicPlayer.Loop == MusicPlayer.LoopMode.Song) ? "this song" : "the whole queue");
		Rectangle repeat = new Rectangle(x, y, size, size);
		if (MusicDrawing.IconButton(sb, id + ":repeat", repeat, MusicDrawing.Tex("Images/UI/IconReset"), null, "Repeat: " + loop, MusicPlayer.Loop != MusicPlayer.LoopMode.Off))
		{
			MusicPlayer.CycleLoop();
		}
		if (MusicPlayer.Loop == MusicPlayer.LoopMode.Song)
		{
			MusicDrawing.Text(sb, "1", repeat.Right - 9, repeat.Bottom - 15, AccentColor, 0.6f);
		}
		x += size + gap;
		if (key != null)
		{
			bool favorite = MusicLibrary.IsFavorite(key);
			if (MusicDrawing.BareIcon(sb, id + ":heart", new Rectangle(x + 2, y + (size - 20) / 2, 20, 20), TextureAssets.Heart.Value, null, favorite ? "Remove from favorites" : "Add to favorites", favorite ? Color.White : new Color(70, 70, 90)))
			{
				MusicLibrary.SetFavorite(key, !favorite);
				SoundEngine.PlaySound(favorite ? 11 : 10);
			}
			x += 26;
		}
		return x;
	}

	internal static void Seekbar(SpriteBatch sb, string id, Rectangle bar, bool showTimes)
	{
		TimeSpan length = MusicPlayer.Length;
		float value = ((length.TotalSeconds > 0.0) ? ((float)(MusicPlayer.Position.TotalSeconds / length.TotalSeconds)) : 0f);
		Rectangle hit = new Rectangle(bar.X, bar.Y - 5, bar.Width, bar.Height + 10);
		bool hover = MouseIn(hit) && MusicPlayer.HasSong;
		if (hover)
		{
			MusicDrawing.Hover(id + ":seek", MusicPlayer.FormatTime(TimeSpan.FromSeconds(length.TotalSeconds * MathHelper.Clamp((float)(MousePoint.X - bar.X) / bar.Width, 0f, 1f))));
		}
		if (hover && Clicked)
		{
			_dragSeek = true;
			_dragLayer = _layer;
			_seekOwner = id;
			Clicked = false;
		}
		if (_dragSeek && _seekOwner == id)
		{
			value = MathHelper.Clamp((float)(MousePoint.X - bar.X) / bar.Width, 0f, 1f);
			if (!_mouseDown)
			{
				_dragSeek = false;
				MusicPlayer.SeekTo(TimeSpan.FromSeconds(length.TotalSeconds * value));
			}
		}
		MusicDrawing.ProgressBar(sb, bar, value, _pink, hover);
		if (showTimes)
		{
			string pos = MusicPlayer.FormatTime(MusicPlayer.Position);
			string len = MusicPlayer.FormatTime(length);
			MusicDrawing.Text(sb, pos, bar.X - MusicDrawing.Measure(pos, 0.65f) - 8, bar.Y - 7, MutedColor, 0.65f);
			MusicDrawing.Text(sb, len, bar.Right + 8, bar.Y - 7, MutedColor, 0.65f);
		}
	}

	private static void VolumeSlider(SpriteBatch sb, Rectangle r)
	{
		MusicDrawing.Icon(sb, MusicDrawing.Tex("Images/UI/CharCreation/Voice"), null, new Rectangle(r.X, r.Y - 4, 18, 22), Color.White);
		Rectangle bar = new Rectangle(r.X + 24, r.Y + 5, r.Width - 24, 6);
		Rectangle hit = new Rectangle(bar.X - 4, r.Y - 4, bar.Width + 8, 22);
		bool hover = MouseIn(hit);
		if (hover)
		{
			MusicDrawing.Hover("volume", "Volume " + (int)Math.Round(MusicPlayer.Volume * 100f) + "%  (.volume 0-100)");
			if (Clicked)
			{
				_dragVolume = true;
				_dragLayer = _layer;
				Clicked = false;
			}
		}
		if (_dragVolume)
		{
			if (!_mouseDown)
			{
				_dragVolume = false;
				MusicPlayer.Save();
			}
			else
			{
				MusicPlayer.SetVolume(MathHelper.Clamp((float)(MousePoint.X - bar.X) / bar.Width, 0f, 1f));
			}
		}
		MusicDrawing.ProgressBar(sb, bar, MusicPlayer.Volume, new Color(120, 190, 255), hover || _dragVolume);
	}

	#endregion

	#region Window

	private static void DrawWindow(SpriteBatch sb, int screenW, int screenH)
	{
		int w = Math.Min(860, screenW - 24);
		int h = Math.Min(560, screenH - 24);
		Rectangle r = ClampToScreen(ref _winPos, w, h, screenW, screenH, new Vector2((screenW - w) / 2, (screenH - h) / 2));
		_winRect = r;
		_winScreen = ToScreen(r);
		MusicDrawing.Box(sb, r, BackColor * 0.97f);
		// Header.
		Rectangle header = new Rectangle(r.X, r.Y, r.Width, 40);
		MusicDrawing.Icon(sb, MusicDrawing.Tex("Images/UI/CharCreation/Voice"), null, new Rectangle(r.X + 12, r.Y + 6, 22, 28), Color.White);
		Utils.DrawBorderStringBig(sb, IsaClient.Languages.L10n.T("Music"), new Vector2(r.X + 42, r.Y + 6), AccentColor, 0.5f);
		MusicDrawing.Text(sb, MiniPlayer.StatusLine(), r.X + 120, r.Y + 13, MutedColor, 0.72f, r.Width - 120 - 110);
		if (MusicDrawing.BareIcon(sb, "win:close", new Rectangle(r.Right - 30, r.Y + 11, 18, 19), MusicDrawing.Tex("Images/MapDeath"), null, "Close (.musicui)", Color.White))
		{
			ToggleWindow();
			return;
		}
		if (MusicDrawing.BareIcon(sb, "win:mini", new Rectangle(r.Right - 58, r.Y + 10, 20, 20), MusicDrawing.Tex("Images/UI/TexturePackButtons"), new Rectangle(32, 0, 32, 32), MiniVisible ? "Hide the window (the mini player keeps playing controls)" : "Show the mini player and hide the window", Color.White))
		{
			if (!MiniVisible)
			{
				MiniVisible = true;
				MusicPlayer.Save();
			}
			ToggleWindow();
			return;
		}
		// Player bar.
		int barH = 84;
		Rectangle bar = new Rectangle(r.X + 8, r.Bottom - barH - 8, r.Width - 16, barH);
		DrawPlayerBar(sb, bar);
		// Sidebar and content.
		Rectangle side = new Rectangle(r.X + 8, header.Bottom + 2, 196, bar.Y - header.Bottom - 10);
		Rectangle content = new Rectangle(side.Right + 8, header.Bottom + 2, r.Right - side.Right - 16, side.Height);
		DrawSidebar(sb, side);
		DrawContent(sb, content);
		HandleDrag(ref _dragWin, ref _winPos, header, null);
	}

	private static void DrawPlayerBar(SpriteBatch sb, Rectangle bar)
	{
		MusicDrawing.Box(sb, bar, PanelColor * 0.95f);
		MusicPlayer.Track current = MusicPlayer.Current;
		string key = current?.Id;
		MusicDrawing.Cover(sb, key, new Rectangle(bar.X + 10, bar.Y + 10, 64, 64));
		float textX = bar.X + 84;
		int size = 30;
		int controlsW = 5 * size + 8 + 4 * 6 + 26;
		int cx = bar.Center.X - controlsW / 2 + 40;
		float textW = Math.Max(60f, cx - textX - 16f);
		string title = ((current != null) ? (current.Title ?? current.Source) : (MusicPlayer.Loading ? "Loading..." : "Nothing playing"));
		MiniPlayer.Marquee(sb, title, new Rectangle((int)textX, bar.Y + 10, (int)textW, 20), Color.White, 0.85f);
		MusicDrawing.Text(sb, MiniPlayer.StatusLine(), textX, bar.Y + 33, MutedColor, 0.64f, textW);
		Controls(sb, "bar", cx, bar.Y + 12, size, key, small: false);
		int volumeX = Math.Max(cx + controlsW + 16, bar.Right - 140);
		VolumeSlider(sb, new Rectangle(volumeX, bar.Y + 21, bar.Right - 14 - volumeX, 12));
		// Seek bar along the bottom, times at its ends.
		string pos = MusicPlayer.FormatTime(MusicPlayer.Position);
		string len = MusicPlayer.FormatTime(MusicPlayer.Length);
		float posW = MusicDrawing.Measure("00:00", 0.62f) + 6f;
		Rectangle seek = new Rectangle((int)(textX + posW), bar.Bottom - 16, (int)(bar.Right - 14 - posW - textX - posW), 6);
		Seekbar(sb, "bar", seek, showTimes: false);
		MusicDrawing.Text(sb, pos, textX, seek.Y - 6, MutedColor, 0.62f);
		MusicDrawing.Text(sb, len, seek.Right + 6, seek.Y - 6, MutedColor, 0.62f);
	}

	private static void DrawSidebar(SpriteBatch sb, Rectangle side)
	{
		MusicDrawing.Box(sb, side, PanelDarkColor * 0.9f);
		int y = side.Y + 8;
		int x = side.X + 8;
		int w = side.Width - 16;
		NavItem(sb, View.NowPlaying, null, new Rectangle(x, y, w, 32), "Now Playing", MusicDrawing.Tex("Images/UI/CharCreation/Voice"), null, null);
		y += 36;
		NavItem(sb, View.Queue, null, new Rectangle(x, y, w, 32), "Queue", MusicDrawing.Tex("Images/UI/IconNext"), null, MusicPlayer.QueueCount.ToString());
		y += 36;
		Main.instance.LoadItem(576);
		NavItem(sb, View.Library, null, new Rectangle(x, y, w, 32), "Library", TextureAssets.Item[576].Value, null, MusicLibrary.Count.ToString());
		y += 36;
		NavItem(sb, View.Favorites, null, new Rectangle(x, y, w, 32), "Favorites", TextureAssets.Heart.Value, null, MusicLibrary.Favorites.Count.ToString());
		y += 36;
		NavItem(sb, View.Search, null, new Rectangle(x, y, w, 32), "Search Music", MusicDrawing.Tex("Images/UI/Cursor_2"), null, null);
		y += 42;
		MusicDrawing.Text(sb, "Playlists", x + 2, y, AccentColor, 0.8f);
		if (MusicDrawing.IconButton(sb, "pl:new", new Rectangle(side.Right - 34, y - 3, 24, 24), MusicDrawing.Tex("Images/UI/ButtonFavoriteActive"), null, "New playlist"))
		{
			_creatingPlaylist = !_creatingPlaylist;
			_newPlaylist = "";
			FocusedField = _creatingPlaylist ? "newpl" : null;
			_focusSetNow = true;
			Main.clrInput();
		}
		y += 26;
		if (_creatingPlaylist)
		{
			_newPlaylist = MusicDrawing.Field(sb, "newpl", new Rectangle(x, y, w, 28), _newPlaylist, "Name, then Enter", 40, searchIcon: false);
			if (FocusedField == "newpl" && Main.inputTextEnter)
			{
				CreatePlaylistNow();
			}
			y += 32;
		}
		bool settings = side.Height >= 360;
		int settingsH = settings ? 95 : 0;
		if (settings)
		{
			int sy = side.Bottom - settingsH;
			MusicDrawing.Fill(sb, new Rectangle(x + 2, sy, w - 4, 1), MutedColor * 0.35f);
			if (ToggleRow(sb, "set:autoplay", new Rectangle(x - 4, sy + 6, w + 8, 26), "Autoplay", MusicPlayer.AutoplayEnabled, "When the queue ends, keep playing songs related to the last one (.autoplay)"))
			{
				MusicPlayer.SetAutoplay(!MusicPlayer.AutoplayEnabled);
			}
			if (ToggleRow(sb, "set:background", new Rectangle(x - 4, sy + 35, w + 8, 26), "Play in background", MusicPlayer.PlayInBackground, "Keep the music playing while the game is minimized or behind other windows (.background)"))
			{
				MusicPlayer.SetPlayInBackground(!MusicPlayer.PlayInBackground);
			}
			if (ToggleRow(sb, "set:note", new Rectangle(x - 4, sy + 64, w + 8, 26), "Music note button", IsaClient.Menu.HudButtons.MusicNote, "The small note that opens this window, on the main menu and in game. Hold it and drag to move it."))
			{
				IsaClient.Menu.HudButtons.SetEnabled("note", !IsaClient.Menu.HudButtons.MusicNote);
			}
		}
		Rectangle list = new Rectangle(side.X + 4, y, side.Width - 8, side.Bottom - y - 6 - settingsH);
		float scroll = MusicDrawing.BeginScroll("side", list);
		BeginClip(sb, list);
		float ly = list.Y - scroll;
		float top = ly;
		List<MusicLibrary.Playlist> playlists = MusicLibrary.Playlists;
		if (playlists.Count == 0)
		{
			MusicDrawing.Text(sb, "No playlists yet: + to make one.", x + 2, ly + 4, MutedColor, 0.66f, w);
			ly += 24f;
		}
		foreach (MusicLibrary.Playlist playlist in playlists)
		{
			Rectangle item = new Rectangle(x - 4, (int)ly, w + 8, 30);
			NavItem(sb, View.Playlist, playlist.Name, item, playlist.Name, null, playlist.Songs.FirstOrDefault(), playlist.Songs.Count.ToString());
			ly += 33f;
		}
		EndClip(sb);
		MusicDrawing.EndScroll(sb, "side", list, ly - top);
	}

	/// <summary>A setting row with Terraria's own on/off toggle; returns true on click.</summary>
	private static bool ToggleRow(SpriteBatch sb, string id, Rectangle r, string label, bool on, string tooltip)
	{
		if (!Visible(r))
		{
			return false;
		}
		bool hover = MouseIn(r);
		if (hover)
		{
			MusicDrawing.Hover(id, tooltip);
		}
		MusicDrawing.Box(sb, r, (hover ? RowHoverColor : _row) * 0.8f);
		Texture2D toggle = MusicDrawing.Tex("Images/UI/Settings_Toggle");
		int frameW = (toggle.Width - 2) / 2;
		Rectangle src = new Rectangle(on ? (frameW + 2) : 0, 0, frameW, toggle.Height);
		MusicDrawing.Text(sb, label, r.X + 9, r.Y + (r.Height - 18) / 2f + 1f, on ? Color.White : MutedColor, 0.72f, r.Width - frameW - 26);
		sb.Draw(toggle, new Vector2(r.Right - frameW - 9, r.Y + (r.Height - toggle.Height) / 2), src, Color.White);
		if (hover && Clicked)
		{
			SoundEngine.PlaySound(12);
			Clicked = false;
			return true;
		}
		return false;
	}

	private static void CreatePlaylistNow()
	{
		string name = MusicLibrary.CreatePlaylist(string.IsNullOrWhiteSpace(_newPlaylist) ? "My playlist" : _newPlaylist);
		_creatingPlaylist = false;
		FocusedField = null;
		_view = View.Playlist;
		_playlist = name;
		SoundEngine.PlaySound(10);
	}

	private static void NavItem(SpriteBatch sb, View view, string playlist, Rectangle r, string label, Texture2D icon, string coverKey, string badge)
	{
		if (!Visible(r))
		{
			return;
		}
		bool selected = _view == view && (view != View.Playlist || _playlist == playlist);
		bool hover = MouseIn(r);
		if (hover)
		{
			MusicDrawing.Hover("nav:" + view + playlist);
		}
		MusicDrawing.Box(sb, r, selected ? SelectedColor : (hover ? RowHoverColor : _row) * 0.9f);
		if (selected)
		{
			MusicDrawing.Fill(sb, new Rectangle(r.X + 3, r.Y + 5, 3, r.Height - 10), AccentColor);
		}
		Rectangle iconArea = new Rectangle(r.X + 9, r.Y + 4, r.Height - 8, r.Height - 8);
		if (icon != null)
		{
			MusicDrawing.Icon(sb, icon, null, iconArea, Color.White);
		}
		else
		{
			MusicDrawing.Cover(sb, coverKey ?? playlist, iconArea);
		}
		float textX = iconArea.Right + 8;
		float badgeW = (badge != null) ? (MusicDrawing.Measure(badge, 0.68f) + 8f) : 0f;
		MusicDrawing.Text(sb, label, textX, r.Y + (r.Height - 18) / 2f + 1f, selected ? AccentColor : Color.White, 0.78f, r.Right - textX - badgeW - 6);
		if (badge != null)
		{
			MusicDrawing.Text(sb, badge, r.Right - badgeW, r.Y + (r.Height - 16) / 2f + 1f, MutedColor, 0.68f);
		}
		if (hover && Clicked)
		{
			if (_view != view || _playlist != playlist)
			{
				_view = view;
				_playlist = playlist;
				_renaming = false;
				_confirm = null;
				ScrollOffsets["content"] = 0f;
				SoundEngine.PlaySound(12);
			}
		}
		if (hover && _rightClick && view == View.Playlist)
		{
			OpenPlaylistMenu(playlist);
		}
	}

	#endregion

	#region Content views

	private static void DrawContent(SpriteBatch sb, Rectangle area)
	{
		MusicDrawing.Box(sb, area, PanelDarkColor * 0.6f);
		Rectangle head = new Rectangle(area.X + 10, area.Y + 8, area.Width - 20, 34);
		int listTop = head.Bottom + 8;
		List<Entry> entries = null;
		string emptyText = "";
		string context = "";
		switch (_view)
		{
		case View.NowPlaying:
			DrawNowPlaying(sb, area);
			return;
		case View.Queue:
			entries = QueueHeader(sb, head);
			emptyText = "The queue is empty. Play a playlist, your favorites or a search result.";
			context = "queue";
			break;
		case View.Library:
			entries = LibraryHeader(sb, head, ref listTop);
			emptyText = string.IsNullOrEmpty(_libraryFilter) ? "No songs yet. Every song you play is kept here." : "No song matches \"" + _libraryFilter + "\".";
			context = "library";
			break;
		case View.Favorites:
			entries = FavoritesHeader(sb, head);
			emptyText = "No favorites yet: click the heart of any song.";
			context = "favorites";
			break;
		case View.Playlist:
			entries = PlaylistHeader(sb, head, ref listTop);
			if (entries == null)
			{
				return;
			}
			emptyText = "This playlist is empty. Right click any song (or its + button) to add it here.";
			context = "playlist";
			break;
		case View.Search:
			entries = SearchHeader(sb, head, ref listTop);
			emptyText = MusicPlayer.Searching ? ("Searching \"" + MusicPlayer.SearchedFor + "\"...") : (string.IsNullOrEmpty(MusicPlayer.SearchedFor) ? "Type a song or artist and press Enter." : "No results for \"" + MusicPlayer.SearchedFor + "\".");
			context = "search";
			break;
		}
		Rectangle list = new Rectangle(area.X + 4, listTop, area.Width - 8, area.Bottom - listTop - 6);
		DrawSongList(sb, list, entries ?? new List<Entry>(), emptyText, context);
	}

	private sealed class Entry
	{
		public string Key;

		public string Title;

		public double Duration;

		public string Sub;

		public bool Downloaded;

		public MusicPlayer.Track Track;

		public MusicPlayer.SearchResult Result;

		public int Index;
	}

	private static Entry EntryFor(MusicLibrary.Song song, int index)
	{
		return new Entry
		{
			Key = song.Key,
			Title = song.Display,
			Duration = song.Duration,
			Downloaded = song.Downloaded,
			Sub = (song.Downloaded ? ((song.Plays > 0) ? (song.Plays + ((song.Plays == 1) ? " play" : " plays")) : "Saved") : "Not downloaded yet (downloads when played)"),
			Index = index
		};
	}

	private static List<MusicLibrary.Song> LibrarySongs(string filter)
	{
		if (_libraryVersion != MusicLibrary.Version || _libraryCacheFilter != filter)
		{
			_libraryVersion = MusicLibrary.Version;
			_libraryCacheFilter = filter;
			_libraryCache = MusicLibrary.Search(filter).OrderByDescending((MusicLibrary.Song s) => (s.LastPlayed > s.Added) ? s.LastPlayed : s.Added).ToList();
		}
		return _libraryCache;
	}

	private static List<MusicLibrary.Song> FavoriteSongs()
	{
		return MusicLibrary.Favorites.Select(MusicLibrary.Get).Where((MusicLibrary.Song s) => s != null).ToList();
	}

	private static List<MusicPlayer.Track> TracksOf(List<Entry> entries)
	{
		return entries.Select((Entry e) => (e.Track != null) ? e.Track : ((e.Result != null) ? MusicPlayer.TrackFor(e.Result) : TrackForKey(e.Key))).Where((MusicPlayer.Track t) => t != null).ToList();
	}

	private static MusicPlayer.Track TrackForKey(string key)
	{
		MusicLibrary.Song song = MusicLibrary.Get(key);
		return (song != null) ? MusicPlayer.TrackFor(song) : null;
	}

	private static void Title(SpriteBatch sb, Rectangle head, string title, string sub, float buttonsWidth)
	{
		Utils.DrawBorderStringBig(sb, IsaClient.Languages.L10n.T(MusicDrawing.FitBig(title, head.Width - buttonsWidth - 16f, 0.5f)), new Vector2(head.X, head.Y - 2), Color.White, 0.5f);
		if (sub != null)
		{
			MusicDrawing.Text(sb, sub, head.X, head.Y + 22, MutedColor, 0.66f, head.Width - buttonsWidth - 10f);
		}
	}

	/// <summary>Header buttons laid out from the right; returns the index clicked or -1.</summary>
	private static int HeaderButtons(SpriteBatch sb, string id, Rectangle head, string[] labels, string[] tips, bool[] enabled = null)
	{
		int x = head.Right;
		int clicked = -1;
		for (int i = labels.Length - 1; i >= 0; i--)
		{
			int bw = (int)MusicDrawing.Measure(labels[i], 0.78f) + 22;
			x -= bw;
			if (MusicDrawing.TextButton(sb, id + i, new Rectangle(x, head.Y + 2, bw, 28), labels[i], PanelColor, tips?[i], enabled?[i] ?? true))
			{
				clicked = i;
			}
			x -= 6;
		}
		return clicked;
	}

	private static float HeaderWidth(string[] labels)
	{
		return labels.Sum((string l) => MusicDrawing.Measure(l, 0.78f) + 28f);
	}

	private static List<Entry> QueueHeader(SpriteBatch sb, Rectangle head)
	{
		List<MusicPlayer.Track> queue = MusicPlayer.QueueSnapshot();
		string[] labels = new string[3] { "Shuffle", "Save as playlist", "Clear" };
		Title(sb, head, "Queue", queue.Count + " song" + ((queue.Count == 1) ? "" : "s") + " up next" + (MusicPlayer.AutoplayEnabled ? "  •  autoplay on" : ""), HeaderWidth(labels));
		switch (HeaderButtons(sb, "qh", head, labels, new string[3] { "Shuffle the queue (and turn shuffle on)", "Make a playlist from the current song and the queue", "Remove every song from the queue" }, new bool[3] { queue.Count > 1, queue.Count > 0 || MusicPlayer.HasSong, queue.Count > 0 }))
		{
		case 0:
			if (!MusicPlayer.Shuffle)
			{
				MusicPlayer.ToggleShuffle();
			}
			else
			{
				MusicPlayer.ToggleShuffle();
				MusicPlayer.ToggleShuffle();
			}
			break;
		case 1:
		{
			string name = MusicLibrary.CreatePlaylist("Queue " + DateTime.Now.ToString("MMM d HH:mm"));
			List<string> keys = new List<string>();
			if (MusicPlayer.CurrentKey != null)
			{
				keys.Add(MusicPlayer.CurrentKey);
			}
			keys.AddRange(queue.Where((MusicPlayer.Track t) => t.Id != null).Select((MusicPlayer.Track t) => t.Id));
			foreach (MusicPlayer.Track track in queue.Where((MusicPlayer.Track t) => t.Id != null && MusicLibrary.Get(t.Id) == null))
			{
				MusicLibrary.Register(track.Id, track.Title, track.Duration, null, track.Source.StartsWith("http") ? track.Source : null);
			}
			MusicLibrary.AddToPlaylist(name, keys);
			_view = View.Playlist;
			_playlist = name;
			SoundEngine.PlaySound(10);
			break;
		}
		case 2:
			MusicPlayer.ClearQueue();
			break;
		}
		List<Entry> entries = new List<Entry>();
		for (int i = 0; i < queue.Count; i++)
		{
			MusicPlayer.Track track = queue[i];
			entries.Add(new Entry
			{
				Key = track.Id ?? track.Source,
				Title = track.Title ?? track.Source,
				Duration = track.Duration,
				Downloaded = track.FilePath != null,
				Sub = (track.Autoplay ? "Autoplay pick" : ((track.FilePath != null) ? "Ready" : ((i < MusicPlayer.PrefetchCount) ? "Downloading..." : "Waiting"))),
				Track = track,
				Index = i
			});
		}
		return entries;
	}

	private static List<Entry> LibraryHeader(SpriteBatch sb, Rectangle head, ref int listTop)
	{
		List<MusicLibrary.Song> songs = LibrarySongs(_libraryFilter);
		string[] labels = new string[3] { "Play all", "Shuffle all", "Open folder" };
		Title(sb, head, "Library", songs.Count + " song" + ((songs.Count == 1) ? "" : "s") + "  •  saved in Music/Songs with their real names", HeaderWidth(labels));
		switch (HeaderButtons(sb, "lh", head, labels, new string[3] { "Play every song shown, in order", "Play every song shown, shuffled", "Open the Music folder" }, new bool[3] { songs.Count > 0, songs.Count > 0, true }))
		{
		case 0:
			PlayEntries(songs.Select(EntryFor).ToList(), 0, shuffle: false, "your library");
			break;
		case 1:
			PlayEntries(songs.Select(EntryFor).ToList(), Main.rand.Next(Math.Max(1, songs.Count)), shuffle: true, "your library");
			break;
		case 2:
			YtDlp.OpenFolder();
			break;
		}
		_libraryFilter = MusicDrawing.Field(sb, "libfilter", new Rectangle(head.X, head.Bottom + 6, head.Width, 30), _libraryFilter, "Search your songs...", 80, searchIcon: true);
		listTop = head.Bottom + 44;
		return songs.Select(EntryFor).ToList();
	}

	private static List<Entry> FavoritesHeader(SpriteBatch sb, Rectangle head)
	{
		List<MusicLibrary.Song> songs = FavoriteSongs();
		string[] labels = new string[3] { "Play", "Shuffle", "Add to queue" };
		Title(sb, head, "Favorites", songs.Count + " song" + ((songs.Count == 1) ? "" : "s") + "  •  never removed to save space", HeaderWidth(labels));
		List<Entry> entries = songs.Select(EntryFor).ToList();
		switch (HeaderButtons(sb, "fh", head, labels, null, new bool[3] { songs.Count > 0, songs.Count > 0, songs.Count > 0 }))
		{
		case 0:
			PlayEntries(entries, 0, shuffle: false, "your favorites");
			break;
		case 1:
			PlayEntries(entries, Main.rand.Next(Math.Max(1, entries.Count)), shuffle: true, "your favorites");
			break;
		case 2:
			MusicPlayer.Enqueue(TracksOf(entries));
			break;
		}
		return entries;
	}

	private static List<Entry> PlaylistHeader(SpriteBatch sb, Rectangle head, ref int listTop)
	{
		MusicLibrary.Playlist playlist = MusicLibrary.GetPlaylist(_playlist);
		if (playlist == null)
		{
			_view = View.Library;
			return null;
		}
		List<Entry> entries = new List<Entry>();
		for (int i = 0; i < playlist.Songs.Count; i++)
		{
			MusicLibrary.Song song = MusicLibrary.Get(playlist.Songs[i]);
			if (song != null)
			{
				Entry entry = EntryFor(song, i);
				entries.Add(entry);
			}
		}
		double total = entries.Sum((Entry e) => e.Duration);
		string[] labels = new string[5] { "Play", "Shuffle", "Add to queue", "Rename", (_confirm == "delpl") ? "Sure? Click again" : "Delete" };
		Title(sb, head, playlist.Name, entries.Count + " song" + ((entries.Count == 1) ? "" : "s") + ((total > 0.0) ? ("  •  " + MusicPlayer.FormatTime(TimeSpan.FromSeconds(total))) : ""), HeaderWidth(labels));
		switch (HeaderButtons(sb, "ph", head, labels, new string[5] { "Play this playlist", "Play it shuffled", "Add it to the queue", "Rename the playlist", "Delete the playlist (the songs stay in your library)" }, new bool[5] { entries.Count > 0, entries.Count > 0, entries.Count > 0, true, true }))
		{
		case 0:
			PlayEntries(entries, 0, shuffle: false, playlist.Name);
			break;
		case 1:
			PlayEntries(entries, Main.rand.Next(Math.Max(1, entries.Count)), shuffle: true, playlist.Name);
			break;
		case 2:
			MusicPlayer.Enqueue(TracksOf(entries));
			break;
		case 3:
			_renaming = !_renaming;
			_renameText = playlist.Name;
			FocusedField = _renaming ? "rename" : null;
			_focusSetNow = true;
			Main.clrInput();
			break;
		case 4:
			if (_confirm == "delpl")
			{
				MusicLibrary.DeletePlaylist(playlist.Name);
				_confirm = null;
				_view = View.Library;
				SoundEngine.PlaySound(11);
				return null;
			}
			_confirm = "delpl";
			_confirmUntil = Main.timeForVisualEffects + 180.0;
			break;
		}
		if (_renaming)
		{
			_renameText = MusicDrawing.Field(sb, "rename", new Rectangle(head.X, head.Bottom + 6, head.Width, 30), _renameText, "New name, then Enter", 40, searchIcon: false);
			if (FocusedField == "rename" && Main.inputTextEnter)
			{
				_playlist = MusicLibrary.RenamePlaylist(playlist.Name, _renameText);
				_renaming = false;
				FocusedField = null;
			}
			listTop = head.Bottom + 44;
		}
		return entries;
	}

	private static List<Entry> SearchHeader(SpriteBatch sb, Rectangle head, ref int listTop)
	{
		List<MusicPlayer.SearchResult> results = MusicPlayer.SearchResults;
		string[] labels = new string[2] { MusicPlayer.Searching ? "Searching..." : "Search", "Play all" };
		Title(sb, head, "Search Music", MusicPlayer.Searching ? "Looking for songs..." : ((results.Count > 0) ? (results.Count + " results for \"" + MusicPlayer.SearchedFor + "\"") : "Find any song; click a result to play it"), HeaderWidth(labels));
		int clicked = HeaderButtons(sb, "sh", head, labels, new string[2] { "Search (or press Enter)", "Play every result" }, new bool[2] { !MusicPlayer.Searching && _searchText.Trim().Length > 0, results.Count > 0 });
		_searchText = MusicDrawing.Field(sb, "ytsearch", new Rectangle(head.X, head.Bottom + 6, head.Width, 30), _searchText, "Song, artist or link...", 120, searchIcon: true);
		bool enter = FocusedField == "ytsearch" && Main.inputTextEnter;
		if ((clicked == 0 || enter) && _searchText.Trim().Length > 0)
		{
			string query = _searchText.Trim();
			if (MusicLinks.IsLink(query))
			{
				MusicPlayer.PlayNow(new MusicPlayer.Track
				{
					Source = MusicLinks.NormalizeQuery(query)
				});
			}
			else
			{
				MusicPlayer.SearchYouTube(query);
				ScrollOffsets["content"] = 0f;
			}
			FocusedField = null;
		}
		List<Entry> entries = results.Select((MusicPlayer.SearchResult res, int i) => new Entry
		{
			Key = res.Id,
			Title = res.Title,
			Duration = res.Duration,
			Downloaded = MusicLibrary.TryGetFile(res.Id, out _),
			Sub = (string.IsNullOrEmpty(res.Channel) ? "Online" : res.Channel) + (MusicLibrary.TryGetFile(res.Id, out _) ? "  •  in your library" : ""),
			Result = res,
			Index = i
		}).ToList();
		if (clicked == 1)
		{
			PlayEntries(entries, 0, shuffle: false, "the search results");
		}
		listTop = head.Bottom + 44;
		return entries;
	}

	private static void PlayEntries(List<Entry> entries, int start, bool shuffle, string label)
	{
		List<MusicPlayer.Track> tracks = TracksOf(entries);
		if (tracks.Count == 0)
		{
			return;
		}
		bool was = MusicPlayer.Shuffle;
		if (shuffle && !was)
		{
			MusicPlayer.Shuffle = true;
		}
		MusicPlayer.PlayList(tracks, start, label);
		if (shuffle && !was)
		{
			MusicPlayer.Shuffle = false;
		}
		SoundEngine.PlaySound(10);
	}

	private static void DrawNowPlaying(SpriteBatch sb, Rectangle area)
	{
		MusicPlayer.Track current = MusicPlayer.Current;
		string key = current?.Id;
		int coverSize = Math.Min(180, area.Height - 120);
		Rectangle cover = new Rectangle(area.X + 24, area.Y + 24, coverSize, coverSize);
		MusicDrawing.Cover(sb, key, cover);
		float x = cover.Right + 24;
		float w = area.Right - x - 20;
		string title = ((current != null) ? (current.Title ?? current.Source) : (MusicPlayer.Loading ? "Loading..." : "Nothing playing"));
		MusicDrawing.BigTitle(sb, title, new Vector2(x, cover.Y), w, 0.55f, 0.42f, Color.White);
		MusicLibrary.Song song = MusicLibrary.Get(key);
		string info = ((current == null) ? "Pick something from your library, favorites or a playlist, or search for music." : ((MusicPlayer.Length.Ticks > 0) ? MusicPlayer.FormatTime(MusicPlayer.Length) : "") + ((song != null && song.Plays > 0) ? ("  •  played " + song.Plays + ((song.Plays == 1) ? " time" : " times")) : "") + (current.Autoplay ? "  •  autoplay pick" : ""));
		MusicDrawing.Text(sb, info, x, cover.Y + 36, MutedColor, 0.75f, w);
		if (current != null)
		{
			int by = cover.Y + 66;
			// Three buttons sharing the room next to the cover (narrow windows make them smaller, never wider than it).
			int bw = (int)Math.Min(150f, (w - 16f) / 3f);
			bool favorite = MusicLibrary.IsFavorite(key);
			if (MusicDrawing.TextButton(sb, "np:fav", new Rectangle((int)x, by, bw, 30), favorite ? "In favorites" : "Add to favorites", favorite ? new Color(150, 60, 100) : PanelColor))
			{
				MusicLibrary.SetFavorite(key, !favorite);
				SoundEngine.PlaySound(favorite ? 11 : 10);
			}
			if (key != null && MusicDrawing.TextButton(sb, "np:pl", new Rectangle((int)x + bw + 8, by, bw, 30), "Add to playlist...", PanelColor))
			{
				OpenAddToPlaylist(new List<string> { key }, new Vector2(x + bw + 8, by + 32));
			}
			if (current.Source != null && current.Source.StartsWith("http") && MusicDrawing.TextButton(sb, "np:link", new Rectangle((int)x + (bw + 8) * 2, by, bw, 30), "Open link", PanelColor, "Opens the song in your browser"))
			{
				ChatLinks.Open(current.Source);
			}
		}
		// Up next.
		List<MusicPlayer.Track> queue = MusicPlayer.QueueSnapshot();
		int y = cover.Bottom + 18;
		MusicDrawing.Text(sb, "Up next", area.X + 24, y, AccentColor, 0.85f);
		if (ToggleRow(sb, "np:autoplay", new Rectangle(area.Right - 24 - 132, y - 4, 132, 24), "Autoplay", MusicPlayer.AutoplayEnabled, "When the queue ends, keep playing songs related to the last one"))
		{
			MusicPlayer.SetAutoplay(!MusicPlayer.AutoplayEnabled);
		}
		y += 26;
		if (queue.Count == 0)
		{
			MusicDrawing.Text(sb, MusicPlayer.AutoplayEnabled ? "Autoplay will pick a related song." : "Nothing queued.", area.X + 24, y, MutedColor, 0.72f);
			return;
		}
		int rowH = 40;
		for (int i = 0; i < queue.Count && y + rowH <= area.Bottom - 6; i++)
		{
			MusicPlayer.Track track = queue[i];
			Rectangle row = new Rectangle(area.X + 18, y, area.Width - 36, rowH - 4);
			bool hover = MouseIn(row);
			if (hover)
			{
				MusicDrawing.Hover("row:up" + i, "Click: play now  •  Right click: remove from the queue");
			}
			MusicDrawing.Box(sb, row, hover ? RowHoverColor : _row * 0.85f);
			MusicDrawing.Cover(sb, track.Id ?? track.Source, new Rectangle(row.X + 4, row.Y + 3, row.Height - 6, row.Height - 6));
			MusicDrawing.Text(sb, track.Title ?? track.Source, row.X + row.Height + 6, row.Y + 9, Color.White, 0.75f, row.Width - row.Height - 80);
			string d = (track.Duration > 0.0) ? MusicPlayer.FormatTime(TimeSpan.FromSeconds(track.Duration)) : "";
			MusicDrawing.Text(sb, d, row.Right - MusicDrawing.Measure(d, 0.7f) - 10, row.Y + 9, MutedColor, 0.7f);
			if (hover && Clicked)
			{
				MusicPlayer.JumpTo(i);
				break;
			}
			if (hover && _rightClick)
			{
				MusicPlayer.RemoveQueued(i);
				_rightClick = false;
				SoundEngine.PlaySound(12);
				break;
			}
			y += rowH;
		}
	}

	/// <summary>The scrolling list of songs every view uses.</summary>
	private static void DrawSongList(SpriteBatch sb, Rectangle list, List<Entry> entries, string emptyText, string context)
	{
		float scroll = MusicDrawing.BeginScroll("content", list);
		BeginClip(sb, list);
		float y = list.Y - scroll;
		float top = y;
		if (entries.Count == 0)
		{
			MusicDrawing.Text(sb, emptyText, list.X + 12, y + 10, MutedColor, 0.75f, list.Width - 24);
			y += 40f;
		}
		const int rowH = 46;
		string currentKey = MusicPlayer.CurrentKey;
		for (int i = 0; i < entries.Count; i++)
		{
			Entry entry = entries[i];
			Rectangle row = new Rectangle(list.X + 6, (int)y, list.Width - 18, rowH - 4);
			y += rowH;
			if (!Visible(row))
			{
				continue;
			}
			bool playing = entry.Key != null && entry.Key == currentKey && context != "queue";
			bool hover = MouseIn(row);
			MusicDrawing.Box(sb, row, playing ? SelectedColor * 0.95f : (hover ? RowHoverColor : _row * 0.85f));
			if (playing)
			{
				MusicDrawing.Fill(sb, new Rectangle(row.X + 3, row.Y + 6, 3, row.Height - 12), AccentColor);
			}
			MusicDrawing.Cover(sb, entry.Key, new Rectangle(row.X + 8, row.Y + 3, row.Height - 6, row.Height - 6), entry.Downloaded || context == "search" || context == "queue" ? 1f : 0.55f);
			// Right side buttons (right to left).
			int bx = row.Right - 30;
			int by = row.Y + (row.Height - 24) / 2;
			string id = context + ":" + i + ":";
			bool clickedButton = false;
			if (context == "queue")
			{
				if (MusicDrawing.BareIcon(sb, id + "rm", new Rectangle(bx + 4, by + 4, 16, 17), MusicDrawing.Tex("Images/MapDeath"), null, "Remove from the queue", Color.White))
				{
					MusicPlayer.RemoveQueued(entry.Index);
					clickedButton = true;
				}
				bx -= 26;
				if (entry.Index < entries.Count - 1 && MusicDrawing.BareIcon(sb, id + "down", new Rectangle(bx, by + 2, 20, 20), MusicDrawing.Tex("Images/UI/TexturePackButtons"), new Rectangle(32, 0, 32, 32), "Move down", Color.White))
				{
					MusicPlayer.MoveQueued(entry.Index, entry.Index + 1);
					clickedButton = true;
				}
				bx -= 24;
				if (entry.Index > 0 && MusicDrawing.BareIcon(sb, id + "up", new Rectangle(bx, by + 2, 20, 20), MusicDrawing.Tex("Images/UI/TexturePackButtons"), new Rectangle(0, 0, 32, 32), "Move up", Color.White))
				{
					MusicPlayer.MoveQueued(entry.Index, entry.Index - 1);
					clickedButton = true;
				}
				bx -= 30;
			}
			else if (context == "playlist")
			{
				if (MusicDrawing.BareIcon(sb, id + "rm", new Rectangle(bx + 4, by + 4, 16, 17), MusicDrawing.Tex("Images/MapDeath"), null, "Remove from this playlist", Color.White))
				{
					MusicLibrary.RemoveFromPlaylist(_playlist, entry.Key);
					clickedButton = true;
				}
				bx -= 26;
				if (entry.Index < entries.Count - 1 && MusicDrawing.BareIcon(sb, id + "down", new Rectangle(bx, by + 2, 20, 20), MusicDrawing.Tex("Images/UI/TexturePackButtons"), new Rectangle(32, 0, 32, 32), "Move down", Color.White))
				{
					MusicLibrary.MoveInPlaylist(_playlist, entry.Index, entry.Index + 1);
					clickedButton = true;
				}
				bx -= 24;
				if (entry.Index > 0 && MusicDrawing.BareIcon(sb, id + "up", new Rectangle(bx, by + 2, 20, 20), MusicDrawing.Tex("Images/UI/TexturePackButtons"), new Rectangle(0, 0, 32, 32), "Move up", Color.White))
				{
					MusicLibrary.MoveInPlaylist(_playlist, entry.Index, entry.Index - 1);
					clickedButton = true;
				}
				bx -= 30;
			}
			else if (context == "library")
			{
				bool armed = _confirm == "del:" + entry.Key;
				if (MusicDrawing.BareIcon(sb, id + "del", new Rectangle(bx + 2, by, 20, 24), MusicDrawing.Tex("Images/Trash"), null, armed ? "Click again to delete the song and its file" : "Delete from your library", armed ? new Color(255, 90, 90) : Color.White))
				{
					if (armed)
					{
						MusicLibrary.Delete(entry.Key);
						_confirm = null;
						SoundEngine.PlaySound(11);
					}
					else
					{
						_confirm = "del:" + entry.Key;
						_confirmUntil = Main.timeForVisualEffects + 180.0;
					}
					clickedButton = true;
				}
				bx -= 30;
			}
			// Add to playlist / queue.
			if (MusicDrawing.BareIcon(sb, id + "pl", new Rectangle(bx, by + 1, 22, 22), MusicDrawing.Tex("Images/UI/ButtonFavoriteActive"), null, "Add to a playlist", Color.White))
			{
				RememberEntry(entry);
				OpenAddToPlaylist(new List<string> { entry.Key }, new Vector2(MousePoint.X - 10, MousePoint.Y + 12));
				clickedButton = true;
			}
			bx -= 28;
			if (context != "queue" && MusicDrawing.BareIcon(sb, id + "q", new Rectangle(bx + 2, by + 4, 18, 18), MusicDrawing.Tex("Images/UI/IconNext"), null, "Add to the queue (right click: play next)", Color.White))
			{
				MusicPlayer.Enqueue(TracksOf(new List<Entry> { entry }));
				clickedButton = true;
			}
			if (context != "queue" && MouseIn(new Rectangle(bx + 2, by + 4, 18, 18)) && _rightClick)
			{
				MusicPlayer.Enqueue(TracksOf(new List<Entry> { entry }), next: true);
				_rightClick = false;
				clickedButton = true;
			}
			bx -= 28;
			bool favorite = MusicLibrary.IsFavorite(entry.Key);
			if (entry.Key != null && MusicDrawing.BareIcon(sb, id + "fav", new Rectangle(bx, by + 2, 20, 20), TextureAssets.Heart.Value, null, favorite ? "Remove from favorites" : "Add to favorites", favorite ? Color.White : new Color(70, 70, 90)))
			{
				RememberEntry(entry);
				MusicLibrary.SetFavorite(entry.Key, !favorite);
				SoundEngine.PlaySound(favorite ? 11 : 10);
				clickedButton = true;
			}
			bx -= 12;
			// Title, subtitle, duration.
			float textX = row.X + row.Height + 8;
			string duration = (entry.Duration > 0.0) ? MusicPlayer.FormatTime(TimeSpan.FromSeconds(entry.Duration)) : "";
			float durW = MusicDrawing.Measure(duration, 0.7f);
			float textW = bx - textX - durW - 14;
			MusicDrawing.Text(sb, entry.Title, textX, row.Y + 5, playing ? AccentColor : Color.White, 0.8f, textW);
			MusicDrawing.Text(sb, entry.Sub, textX, row.Y + 24, MutedColor, 0.62f, textW);
			MusicDrawing.Text(sb, duration, bx - durW - 4, row.Y + 14, MutedColor, 0.7f);
			if (hover && HoveredControl == null)
			{
				MusicDrawing.Hover("row:" + id, (context == "queue") ? "Click: play now  •  Right click: more" : "Click: play  •  Right click: more");
			}
			if (hover && !clickedButton && HoveredControl == "row:" + id)
			{
				if (Clicked)
				{
					if (context == "queue")
					{
						MusicPlayer.JumpTo(entry.Index);
					}
					else if (context == "search")
					{
						MusicPlayer.PlayNow(MusicPlayer.TrackFor(entry.Result));
					}
					else
					{
						PlayEntries(entries, i, shuffle: false, null);
					}
				}
				else if (_rightClick)
				{
					OpenSongMenu(entry, entries, i, context);
				}
			}
		}
		EndClip(sb);
		MusicDrawing.EndScroll(sb, "content", list, y - top + 6f);
	}

	private static void RememberEntry(Entry entry)
	{
		if (entry.Result != null)
		{
			MusicPlayer.Remember(entry.Result);
		}
		else if (entry.Track != null && entry.Track.Id != null && MusicLibrary.Get(entry.Track.Id) == null)
		{
			MusicLibrary.Register(entry.Track.Id, entry.Track.Title, entry.Track.Duration, entry.Track.FilePath, (entry.Track.Source != null && entry.Track.Source.StartsWith("http")) ? entry.Track.Source : null);
		}
	}

	#endregion

	#region Menus

	internal static void OpenMenu(List<MenuEntry> entries, Vector2 at)
	{
		MenuEntries = entries;
		_menuPos = at;
		_menuOpenedNow = true;
		SoundEngine.PlaySound(12);
	}

	private static void OpenSongMenu(Entry entry, List<Entry> entries, int index, string context)
	{
		List<MenuEntry> menu = new List<MenuEntry>
		{
			new MenuEntry
			{
				Label = entry.Title,
				Header = true
			},
			new MenuEntry
			{
				Label = "Play now",
				Action = delegate
				{
					if (context == "queue")
					{
						MusicPlayer.JumpTo(entry.Index);
					}
					else
					{
						MusicPlayer.PlayNow(TracksOf(new List<Entry> { entry }).FirstOrDefault());
					}
				}
			}
		};
		if (context != "queue")
		{
			menu.Add(new MenuEntry
			{
				Label = "Play next",
				Action = delegate
				{
					MusicPlayer.Enqueue(TracksOf(new List<Entry> { entry }), next: true);
				}
			});
			menu.Add(new MenuEntry
			{
				Label = "Add to queue",
				Action = delegate
				{
					MusicPlayer.Enqueue(TracksOf(new List<Entry> { entry }));
				}
			});
		}
		if (entry.Key != null)
		{
			bool favorite = MusicLibrary.IsFavorite(entry.Key);
			menu.Add(new MenuEntry
			{
				Label = (favorite ? "Remove from favorites" : "Add to favorites"),
				Action = delegate
				{
					RememberEntry(entry);
					MusicLibrary.SetFavorite(entry.Key, !favorite);
				}
			});
			menu.Add(new MenuEntry
			{
				Label = "Add to playlist...",
				Action = delegate
				{
					RememberEntry(entry);
					OpenAddToPlaylist(new List<string> { entry.Key }, new Vector2(MousePoint.X, MousePoint.Y));
				}
			});
		}
		if (context == "playlist")
		{
			menu.Add(new MenuEntry
			{
				Label = "Remove from this playlist",
				Action = delegate
				{
					MusicLibrary.RemoveFromPlaylist(_playlist, entry.Key);
				}
			});
		}
		if (context == "queue")
		{
			menu.Add(new MenuEntry
			{
				Label = "Remove from the queue",
				Action = delegate
				{
					MusicPlayer.RemoveQueued(entry.Index);
				}
			});
		}
		MusicLibrary.Song song = MusicLibrary.Get(entry.Key);
		string link = (entry.Result != null) ? ("https://www.youtube.com/watch?v=" + entry.Result.Id) : ((song != null && !song.Key.StartsWith("file:")) ? song.Source : null);
		if (link != null)
		{
			menu.Add(new MenuEntry
			{
				Label = "Open link",
				Action = delegate
				{
					ChatLinks.Open(link);
				}
			});
		}
		OpenMenu(menu, new Vector2(MousePoint.X + 4, MousePoint.Y + 4));
	}

	private static void OpenPlaylistMenu(string name)
	{
		MusicLibrary.Playlist playlist = MusicLibrary.GetPlaylist(name);
		if (playlist == null)
		{
			return;
		}
		List<Entry> entries = playlist.Songs.Select(MusicLibrary.Get).Where((MusicLibrary.Song s) => s != null).Select(EntryFor).ToList();
		OpenMenu(new List<MenuEntry>
		{
			new MenuEntry
			{
				Label = playlist.Name,
				Header = true
			},
			new MenuEntry
			{
				Label = "Play",
				Action = delegate
				{
					PlayEntries(entries, 0, shuffle: false, playlist.Name);
				}
			},
			new MenuEntry
			{
				Label = "Shuffle",
				Action = delegate
				{
					PlayEntries(entries, Main.rand.Next(Math.Max(1, entries.Count)), shuffle: true, playlist.Name);
				}
			},
			new MenuEntry
			{
				Label = "Add to queue",
				Action = delegate
				{
					MusicPlayer.Enqueue(TracksOf(entries));
				}
			},
			new MenuEntry
			{
				Label = "Delete playlist",
				Action = delegate
				{
					MusicLibrary.DeletePlaylist(playlist.Name);
					if (_playlist == playlist.Name)
					{
						_view = View.Library;
					}
				}
			}
		}, new Vector2(MousePoint.X + 4, MousePoint.Y + 4));
	}

	private static void OpenAddToPlaylist(List<string> keys, Vector2 at)
	{
		List<MenuEntry> menu = new List<MenuEntry>
		{
			new MenuEntry
			{
				Label = "Add to playlist",
				Header = true
			}
		};
		foreach (MusicLibrary.Playlist playlist in MusicLibrary.Playlists)
		{
			string name = playlist.Name;
			bool already = keys.All(playlist.Songs.Contains);
			menu.Add(new MenuEntry
			{
				Label = (already ? (name + "  (already there)") : name),
				Action = delegate
				{
					int added = MusicLibrary.AddToPlaylist(name, keys);
					MusicPlayer.Say((added > 0) ? ("Added to " + name + ".") : ("Already in " + name + "."));
				}
			});
		}
		menu.Add(new MenuEntry
		{
			Label = "+ New playlist",
			Action = delegate
			{
				string name = MusicLibrary.CreatePlaylist("Playlist " + (MusicLibrary.Playlists.Count + 1));
				MusicLibrary.AddToPlaylist(name, keys);
				WindowOpen = true;
				_view = View.Playlist;
				_playlist = name;
				_renaming = true;
				_renameText = name;
				FocusedField = "rename";
				_focusSetNow = true;
				Main.clrInput();
			}
		});
		OpenMenu(menu, at);
	}

	private static void DrawMenu(SpriteBatch sb, int screenW, int screenH)
	{
		float scale = 0.78f;
		int w = (int)Math.Min(320f, Math.Max(170f, MenuEntries.Max((MenuEntry e) => MusicDrawing.Measure(e.Label, scale)) + 28f));
		// Long menus (a video's audio languages) get tighter rows so they fit on the screen.
		int rowH = Math.Max(18, Math.Min(26, (screenH - 18) / Math.Max(1, MenuEntries.Count)));
		int h = MenuEntries.Count * rowH + 10;
		Vector2 pos = _menuPos;
		pos.X = MathHelper.Clamp(pos.X, 4f, screenW - w - 4);
		pos.Y = MathHelper.Clamp(pos.Y, 4f, screenH - h - 4);
		_menuRect = new Rectangle((int)pos.X, (int)pos.Y, w, h);
		MusicDrawing.Box(sb, _menuRect, PanelDarkColor * 0.98f);
		int y = _menuRect.Y + 5;
		MenuEntry clicked = null;
		foreach (MenuEntry entry in MenuEntries)
		{
			Rectangle row = new Rectangle(_menuRect.X + 4, y, w - 8, rowH - 2);
			if (entry.Header)
			{
				MusicDrawing.Text(sb, entry.Label, row.X + 6, row.Y + 3, AccentColor, scale, row.Width - 12);
				MusicDrawing.Fill(sb, new Rectangle(row.X + 4, row.Bottom, row.Width - 8, 1), MutedColor * 0.5f);
			}
			else
			{
				bool hover = row.Contains(MousePoint);
				if (hover)
				{
					HoveredControl = "menu:" + entry.Label;
					MusicDrawing.Fill(sb, row, RowHoverColor * 0.9f);
					// Not the click that opened it: a menu moved to fit the screen can open under the mouse.
					if (Clicked && !_menuOpenedNow)
					{
						clicked = entry;
					}
				}
				MusicDrawing.Text(sb, entry.Label, row.X + 8, row.Y + 3, hover ? Color.White : new Color(225, 230, 240), scale, row.Width - 16);
			}
			y += rowH;
		}
		if (clicked != null)
		{
			MenuEntries = null;
			SoundEngine.PlaySound(12);
			clicked.Action?.Invoke();
		}
	}

	#endregion
}
