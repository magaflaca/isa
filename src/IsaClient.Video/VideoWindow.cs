using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IsaClient.Core;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;

namespace IsaClient.Video;

/// <summary>
/// The video window, drawn like the music windows (and stacked with them): a movable, resizable player with the
/// picture, a seek bar that shows what is downloaded, play / ±10 s / volume / speed / quality / loop / download, a
/// floating mini player (picture in picture) and a full screen mode. Its browse page searches YouTube, continues
/// recent videos and plays saved ones.
/// </summary>
public static class VideoWindow
{
	public enum WindowMode
	{
		Normal,
		Mini,
		Maximized
	}

	private enum Page
	{
		Player,
		Browse
	}

	private enum BrowseTab
	{
		Results,
		Recent,
		Saved
	}

	private const string Layer = "video";

	[Setting("VideoWindowMode")]
	public static WindowMode Mode = WindowMode.Normal;

	[Setting("VideoWindowX")]
	public static float WindowX = -1f;

	[Setting("VideoWindowY")]
	public static float WindowY = -1f;

	[Setting("VideoWindowWidth", Min = 420, Max = 4000)]
	public static int WindowWidth = 720;

	[Setting("VideoMiniX")]
	public static float MiniX = -1f;

	[Setting("VideoMiniY")]
	public static float MiniY = -1f;

	[Setting("VideoMiniWidth", Min = 220, Max = 1200)]
	public static int MiniWidth = 340;

	public static bool Visible;

	private static readonly Color _red = new Color(255, 82, 92);

	private static Page _page = Page.Browse;

	private static BrowseTab _tab = BrowseTab.Recent;

	private static Rectangle _screenRect;

	private static bool _dragWindow;

	private static bool _dragMini;

	private static bool _seeking;

	private static float _seekValue;

	private static bool _volumeDrag;

	private static bool _resizing;

	private static int _resizeStartWidth;

	private static int _resizeStartX;

	private static bool _wasMoving;

	private static double _lastClick = -100.0;

	private static Point _lastMouse;

	private static double _lastMouseMove;

	private static double _volumeShownUntil;

	private static string _searchText = "";

	private static bool _searching;

	private static string _searchedFor = "";

	private static List<VideoInfo> _results = new List<VideoInfo>();

	private static RasterizerState _scissor;

	#region Showing

	internal static void Register()
	{
		// A new session never starts in full screen.
		if (Mode == WindowMode.Maximized)
		{
			Mode = WindowMode.Normal;
		}
		MusicPlayerWindow.AddSurface(new MusicPlayerWindow.FloatingSurface
		{
			Layer = Layer,
			Shown = () => Visible,
			ScreenRect = () => _screenRect,
			Draw = Draw
		});
	}

	public static void Show()
	{
		if (!Visible)
		{
			SoundEngine.PlaySound(10);
		}
		Visible = true;
		_page = (VideoPlayer.HasVideo || VideoPlayer.Loading) ? Page.Player : Page.Browse;
		MusicPlayerWindow.BringSurfaceToFront(Layer);
	}

	/// <summary>Something starts playing: the window shows it (in the mode it is in).</summary>
	public static void ShowPlayer()
	{
		Show();
		_page = Page.Player;
	}

	public static void Hide()
	{
		if (Visible)
		{
			SoundEngine.PlaySound(11);
		}
		Visible = false;
		_screenRect = Rectangle.Empty;
		MusicPlayerWindow.FocusedField = null;
	}

	public static void Toggle()
	{
		if (Visible)
		{
			Hide();
		}
		else
		{
			Show();
		}
	}

	public static void SetMode(WindowMode mode)
	{
		Mode = mode;
		Visible = true;
		if (mode != WindowMode.Normal && VideoPlayer.HasVideo)
		{
			_page = Page.Player;
		}
		MusicPlayerWindow.BringSurfaceToFront(Layer);
		ClientSettings.Save();
	}

	/// <summary>.video search &lt;words&gt;: the browse page with the results.</summary>
	public static void Search(string query)
	{
		Visible = true;
		Mode = WindowMode.Normal;
		_page = Page.Browse;
		_tab = BrowseTab.Results;
		MusicPlayerWindow.BringSurfaceToFront(Layer);
		query = (query ?? "").Trim();
		if (query.Length == 0)
		{
			return;
		}
		_searchText = query;
		if (VideoSource.IsLink(query))
		{
			VideoPlayer.Play(query);
			return;
		}
		_searching = true;
		_searchedFor = query;
		YtDlp.EnsureYtDlp(delegate
		{
			Task.Run(delegate
			{
				List<VideoInfo> results = new List<VideoInfo>();
				try
				{
					results = VideoSource.Search(query, 15);
				}
				catch (Exception ex)
				{
					ClientLog.LogException(ex);
				}
				finally
				{
					_results = results;
					_searching = false;
				}
			});
		});
	}

	#endregion

	#region Frame

	private static void Draw(SpriteBatch sb, int screenW, int screenH)
	{
		Point mouse = MusicPlayerWindow.MousePoint;
		if (mouse != _lastMouse)
		{
			_lastMouse = mouse;
			_lastMouseMove = Main.timeForVisualEffects;
		}
		if (_page == Page.Player && !VideoPlayer.HasVideo && !VideoPlayer.Loading)
		{
			_page = Page.Browse;
		}
		switch (Mode)
		{
		case WindowMode.Mini:
			DrawMini(sb, screenW, screenH);
			break;
		case WindowMode.Maximized:
			DrawMaximized(sb, screenW, screenH);
			break;
		default:
			DrawNormal(sb, screenW, screenH);
			break;
		}
		bool moving = _dragWindow || _dragMini || _resizing || _seeking || _volumeDrag;
		if (moving)
		{
			MusicPlayerWindow.SurfaceDragging = true;
		}
		if (_wasMoving && !moving)
		{
			ClientSettings.Save();
		}
		_wasMoving = moving;
	}

	private static bool DoubleClicked()
	{
		bool twice = Main.timeForVisualEffects - _lastClick < 18.0;
		_lastClick = twice ? (-100.0) : Main.timeForVisualEffects;
		return twice;
	}

	#endregion

	#region Normal window

	private static void DrawNormal(SpriteBatch sb, int screenW, int screenH)
	{
		int w = Math.Min(WindowWidth, screenW - 16);
		int videoH = (w - 16) * 9 / 16;
		int h = 42 + videoH + 70;
		if (h > screenH - 16)
		{
			videoH = screenH - 16 - 42 - 70;
			w = videoH * 16 / 9 + 16;
			h = screenH - 16;
		}
		Vector2 pos = new Vector2((WindowX < 0f) ? float.NaN : WindowX, (WindowY < 0f) ? float.NaN : WindowY);
		Rectangle r = MusicPlayerWindow.ClampToScreen(ref pos, w, h, screenW, screenH, new Vector2((screenW - w) / 2, Math.Max(8, (screenH - h) / 2 - 40)));
		_screenRect = MusicPlayerWindow.ToScreen(r);
		MusicDrawing.Box(sb, r, MusicPlayerWindow.BackColor * 0.97f);
		Rectangle header = new Rectangle(r.X, r.Y, r.Width, 40);
		Rectangle video = new Rectangle(r.X + 8, header.Bottom, r.Width - 16, videoH);
		Rectangle bar = new Rectangle(r.X + 8, video.Bottom + 4, r.Width - 16, 62);
		if (!DrawHeader(sb, header))
		{
			return;
		}
		if (_page == Page.Browse)
		{
			DrawBrowse(sb, video);
		}
		else
		{
			DrawPicture(sb, video, canMaximize: true);
		}
		DrawControls(sb, bar, roomy: r.Width >= 600);
		DrawResizeHandle(sb, r);
		MusicPlayerWindow.HandleDrag(ref _dragWindow, ref pos, header, null);
		WindowX = pos.X;
		WindowY = pos.Y;
	}

	/// <summary>Icon, name, title and the window buttons; false when the window was closed.</summary>
	private static bool DrawHeader(SpriteBatch sb, Rectangle header)
	{
		MusicDrawing.Icon(sb, MusicDrawing.Tex("Images/UI/Camera_0"), null, new Rectangle(header.X + 10, header.Y + 8, 26, 24), Color.White);
		Utils.DrawBorderStringBig(sb, IsaClient.Languages.L10n.T("Video"), new Vector2(header.X + 42, header.Y + 6), MusicPlayerWindow.AccentColor, 0.5f);
		int buttons = header.Right - 4 * 28 - 8;
		VideoInfo current = VideoPlayer.Current;
		string title = VideoPlayer.Loading ? VideoPlayer.LoadingWhat : (current?.Title ?? "Search a video or paste a link");
		MiniPlayer.Marquee(sb, title, new Rectangle(header.X + 118, header.Y + 9, buttons - header.X - 128, 20), (current != null) ? Color.White : MusicPlayerWindow.MutedColor, 0.82f);
		int x = buttons;
		if (MusicDrawing.BareIcon(sb, "video:browse", new Rectangle(x, header.Y + 10, 20, 20), ItemTexture(ItemID.Book), null, (_page == Page.Browse) ? "Back to the video" : "Search, continue watching, saved videos", (_page == Page.Browse) ? MusicPlayerWindow.AccentColor : Color.White))
		{
			_page = (_page == Page.Browse && (VideoPlayer.HasVideo || VideoPlayer.Loading)) ? Page.Player : Page.Browse;
		}
		x += 28;
		if (MusicDrawing.BareIcon(sb, "video:mini", new Rectangle(x, header.Y + 10, 20, 20), MusicDrawing.Tex("Images/UI/TexturePackButtons"), new Rectangle(32, 0, 32, 32), "Mini player (keeps playing in a small window)", Color.White))
		{
			SetMode(WindowMode.Mini);
		}
		x += 28;
		if (MusicDrawing.BareIcon(sb, "video:max", new Rectangle(x, header.Y + 10, 20, 20), MusicDrawing.Tex("Images/UI/TexturePackButtons"), new Rectangle(0, 0, 32, 32), "Full screen (double click the video)", Color.White))
		{
			SetMode(WindowMode.Maximized);
		}
		x += 28;
		if (MusicDrawing.BareIcon(sb, "video:close", new Rectangle(x + 2, header.Y + 11, 17, 18), MusicDrawing.Tex("Images/MapDeath"), null, "Close the video", Color.White))
		{
			VideoPlayer.Close();
			Hide();
			return false;
		}
		return true;
	}

	private static void DrawResizeHandle(SpriteBatch sb, Rectangle r)
	{
		Rectangle grip = new Rectangle(r.Right - 16, r.Bottom - 16, 14, 14);
		bool hover = MusicPlayerWindow.MouseIn(grip);
		if (hover)
		{
			MusicDrawing.Hover("video:resize", "Drag to resize");
			if (MusicPlayerWindow.Clicked)
			{
				_resizing = true;
				_resizeStartWidth = r.Width;
				_resizeStartX = MusicPlayerWindow.MousePoint.X;
				MusicPlayerWindow.StartSurfaceDrag();
			}
		}
		if (_resizing)
		{
			if (!MusicPlayerWindow.MouseDown)
			{
				_resizing = false;
			}
			else
			{
				WindowWidth = Math.Max(420, _resizeStartWidth + MusicPlayerWindow.MousePoint.X - _resizeStartX);
			}
		}
		Color color = (hover || _resizing) ? Color.White : (MusicPlayerWindow.MutedColor * 0.8f);
		for (int i = 0; i < 3; i++)
		{
			int d = 4 + i * 4;
			MusicDrawing.Fill(sb, new Rectangle(grip.Right - d, grip.Bottom - 2, d, 2), color);
			MusicDrawing.Fill(sb, new Rectangle(grip.Right - 2, grip.Bottom - d, 2, d), color);
		}
	}

	#endregion

	#region Picture

	/// <summary>The video (or what it is doing: loading, buffering, paused, ended, failed) in an area.</summary>
	private static void DrawPicture(SpriteBatch sb, Rectangle area, bool canMaximize)
	{
		MusicDrawing.Fill(sb, area, Color.Black);
		Texture2D frame = VideoPlayer.Frame();
		if (frame != null)
		{
			DrawSmooth(sb, frame, Fit(frame.Width, frame.Height, area), Color.White);
		}
		else if (VideoPlayer.Current?.Thumbnail != null)
		{
			Texture2D thumbnail = VideoThumbnails.Get(VideoPlayer.Current.Thumbnail);
			if (thumbnail != null)
			{
				DrawSmooth(sb, thumbnail, Fit(thumbnail.Width, thumbnail.Height, area), Color.White * 0.45f);
			}
		}
		bool hover = MusicPlayerWindow.MouseIn(area);
		if (VideoPlayer.Error != null)
		{
			MusicDrawing.Fill(sb, area, Color.Black * 0.55f);
			CenteredText(sb, VideoPlayer.Error, area, area.Center.Y - 26, _red, 0.85f);
			Rectangle retry = new Rectangle(area.Center.X - 60, area.Center.Y + 4, 120, 32);
			if (VideoPlayer.Current != null && MusicDrawing.TextButton(sb, "video:retry", retry, "Try again", MusicPlayerWindow.PanelColor))
			{
				VideoPlayer.Play(VideoPlayer.Current, TimeSpan.Zero);
			}
			return;
		}
		if (VideoPlayer.Loading || VideoPlayer.IsBuffering)
		{
			Spinner(sb, new Vector2(area.Center.X, area.Center.Y), Math.Min(1.4f, area.Height / 220f));
			if (VideoPlayer.Loading)
			{
				CenteredText(sb, VideoPlayer.LoadingWhat, area, area.Center.Y + 36, Color.White, 0.8f);
			}
		}
		else if (VideoPlayer.Ended)
		{
			MusicDrawing.Fill(sb, area, Color.Black * 0.5f);
			Rectangle replay = new Rectangle(area.Center.X - 64, area.Center.Y - 16, 128, 34);
			if (MusicDrawing.TextButton(sb, "video:replay", replay, "Watch again", new Color(150, 45, 60)))
			{
				VideoPlayer.Replay();
			}
		}
		else if (VideoPlayer.IsPaused)
		{
			int size = Math.Max(28, Math.Min(64, area.Height / 5));
			MusicDrawing.Fill(sb, area, Color.Black * 0.25f);
			MusicDrawing.Icon(sb, MusicDrawing.Tex("Images/UI/IconPlayPause"), new Rectangle(0, 0, 16, 16), new Rectangle(area.Center.X - size / 2, area.Center.Y - size / 2, size, size), Color.White * 0.9f, 8f);
		}
		if (Main.timeForVisualEffects < _volumeShownUntil)
		{
			string text = VideoPlayer.Muted ? "Muted" : ("Volume " + (int)Math.Round(VideoPlayer.Volume * 100f) + "%");
			float tw = MusicDrawing.Measure(text, 0.8f);
			Rectangle tag = new Rectangle(area.Center.X - (int)tw / 2 - 12, area.Y + 14, (int)tw + 24, 28);
			MusicDrawing.Box(sb, tag, Color.Black * 0.6f);
			MusicDrawing.Text(sb, text, tag.X + 12, tag.Y + 6, Color.White, 0.8f);
		}
		if (!hover)
		{
			return;
		}
		if (MusicPlayerWindow.WheelDelta != 0 && VideoPlayer.HasVideo)
		{
			VideoPlayer.SetVolume(VideoPlayer.Volume + Math.Sign(MusicPlayerWindow.WheelDelta) * 0.05f);
			MusicPlayerWindow.WheelDelta = 0;
			_volumeShownUntil = Main.timeForVisualEffects + 70.0;
		}
		if (MusicPlayerWindow.HoveredControl == null)
		{
			MusicDrawing.Hover("video:picture");
			if (MusicPlayerWindow.Clicked && VideoPlayer.HasVideo && !VideoPlayer.Ended)
			{
				MusicPlayerWindow.Clicked = false;
				VideoPlayer.TogglePause();
				if (DoubleClicked() && canMaximize)
				{
					// The first click paused it: put it back the way it was.
					VideoPlayer.TogglePause();
					SetMode((Mode == WindowMode.Maximized) ? WindowMode.Normal : WindowMode.Maximized);
				}
			}
		}
	}

	private static Rectangle Fit(int width, int height, Rectangle area)
	{
		float scale = Math.Min((float)area.Width / Math.Max(1, width), (float)area.Height / Math.Max(1, height));
		int w = (int)(width * scale);
		int h = (int)(height * scale);
		return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
	}

	/// <summary>Draws a picture with smooth scaling (the menu draws with point sampling).</summary>
	private static void DrawSmooth(SpriteBatch sb, Texture2D texture, Rectangle destination, Color color)
	{
		RasterizerState rasterizer = RasterizerState.CullCounterClockwise;
		if (MusicPlayerWindow.Clipping)
		{
			rasterizer = _scissor ?? (_scissor = new RasterizerState
			{
				CullMode = CullMode.None,
				ScissorTestEnable = true
			});
		}
		sb.End();
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, rasterizer, null, Main.UIScaleMatrix);
		sb.Draw(texture, destination, color);
		sb.End();
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.gameMenu ? SamplerState.PointClamp : SamplerState.LinearClamp, DepthStencilState.None, rasterizer, null, Main.UIScaleMatrix);
	}

	private static void Spinner(SpriteBatch sb, Vector2 center, float scale)
	{
		Texture2D sunflower = TextureAssets.LoadingSunflower.Value;
		Rectangle frame = sunflower.Frame(1, 19, 0, (int)(Main.timeForVisualEffects / 3.0) % 19);
		sb.Draw(sunflower, center, frame, Color.White, 0f, frame.Size() / 2f, scale, SpriteEffects.None, 0f);
	}

	private static void CenteredText(SpriteBatch sb, string text, Rectangle area, int y, Color color, float scale)
	{
		float width = Math.Min(MusicDrawing.Measure(text, scale), area.Width - 24);
		MusicDrawing.Text(sb, text, area.Center.X - width / 2f, y, color, scale, area.Width - 24);
	}

	#endregion

	#region Controls

	/// <summary>Seek bar and buttons: play, -10 s, +10 s, time | volume, speed, quality, loop, download.</summary>
	private static void DrawControls(SpriteBatch sb, Rectangle bar, bool roomy)
	{
		bool has = VideoPlayer.HasVideo && !VideoPlayer.Loading;
		Seekbar(sb, new Rectangle(bar.X + 6, bar.Y + 8, bar.Width - 12, 6), has);
		int y = bar.Y + 24;
		int x = bar.X + 4;
		Texture2D playPause = MusicDrawing.Tex("Images/UI/IconPlayPause");
		bool playing = VideoPlayer.IsPlaying;
		if (MusicDrawing.IconButton(sb, "video:play", new Rectangle(x, y, 34, 34), playPause, new Rectangle(0, playing ? 16 : 0, 16, 16), VideoPlayer.Ended ? "Watch again" : (playing ? "Pause" : "Play"), playing, has))
		{
			VideoPlayer.TogglePause();
		}
		x += 40;
		if (MusicDrawing.IconButton(sb, "video:back", new Rectangle(x, y + 3, 28, 28), MusicDrawing.Tex("Images/UI/IconPrev"), null, "Back 10 seconds", enabled: has))
		{
			VideoPlayer.SeekBy(-10.0);
		}
		x += 32;
		if (MusicDrawing.IconButton(sb, "video:forward", new Rectangle(x, y + 3, 28, 28), MusicDrawing.Tex("Images/UI/IconNext"), null, "Forward 10 seconds", enabled: has))
		{
			VideoPlayer.SeekBy(10.0);
		}
		x += 38;
		string time = MusicPlayer.FormatTime(VideoPlayer.Position) + " / " + MusicPlayer.FormatTime(VideoPlayer.Length);
		if (has)
		{
			MusicDrawing.Text(sb, time, x, y + 9, Color.White, 0.72f);
		}
		// Right side, from the right edge.
		int right = bar.Right - 4;
		right = DownloadButton(sb, right, y + 3);
		right -= 4;
		Rectangle loop = new Rectangle(right - 28, y + 3, 28, 28);
		if (MusicDrawing.IconButton(sb, "video:loop", loop, MusicDrawing.Tex("Images/UI/IconReset"), null, "Loop: " + (VideoPlayer.Loop ? "on" : "off"), VideoPlayer.Loop))
		{
			VideoPlayer.Loop = !VideoPlayer.Loop;
		}
		right = loop.X - 4;
		bool local = VideoPlayer.Current?.IsLocal ?? false;
		bool web = VideoPlayer.Current?.Web ?? false;
		string quality = (web && VideoPlayer.VideoHeight > 0) ? (VideoPlayer.VideoHeight + "p") : (local ? "File" : ((VideoPlayer.CurrentHeight > 0) ? (VideoPlayer.CurrentHeight + "p") : (VideoPlayer.Quality + "p")));
		Rectangle qualityButton = new Rectangle(right - 58, y + 4, 58, 26);
		if (MusicDrawing.TextButton(sb, "video:quality", qualityButton, quality, MusicPlayerWindow.PanelColor, web ? "Videos from this site play in the quality they were downloaded in (the Quality setting, up to " + VideoPlayer.Quality + "p)" : (local ? "A file on this computer plays in its own quality" : "Quality"), !local))
		{
			OpenQualityMenu(new Vector2(qualityButton.X, qualityButton.Y));
		}
		right = qualityButton.X - 4;
		List<VideoFormat> tracks = VideoPlayer.Current?.Sounds;
		if (tracks != null && tracks.Count > 1 && VideoPlayer.Current.Sound != null)
		{
			VideoFormat sound = VideoPlayer.Current.Sound;
			string code = (sound.Language ?? "?").Split('-')[0].ToUpperInvariant();
			Rectangle languageButton = new Rectangle(right - 44, y + 4, 44, 26);
			if (MusicDrawing.TextButton(sb, "video:language", languageButton, code, sound.Original ? MusicPlayerWindow.PanelColor : MusicPlayerWindow.SelectedColor, "Audio: " + sound.LanguageName + (sound.Original ? " (original)" : " (dubbed)") + "  -  " + tracks.Count + " languages"))
			{
				OpenLanguageMenu(new Vector2(languageButton.X, languageButton.Y), tracks, sound);
			}
			right = languageButton.X - 4;
		}
		Rectangle speedButton = new Rectangle(right - 48, y + 4, 48, 26);
		if (MusicDrawing.TextButton(sb, "video:speed", speedButton, VideoPlayer.SpeedLabel(VideoPlayer.Speed), (Math.Abs(VideoPlayer.Speed - 1f) > 0.01f) ? MusicPlayerWindow.SelectedColor : MusicPlayerWindow.PanelColor, "Speed (the voice keeps its pitch)"))
		{
			OpenSpeedMenu(new Vector2(speedButton.X, speedButton.Y));
		}
		right = speedButton.X - 8;
		int sliderW = roomy ? 90 : 56;
		if (right - sliderW - 26 > x + MusicDrawing.Measure(time, 0.72f) + 8)
		{
			VolumeControl(sb, new Rectangle(right - sliderW - 26, y + 9, sliderW + 26, 14));
		}
	}

	private static void Seekbar(SpriteBatch sb, Rectangle bar, bool enabled)
	{
		TimeSpan length = VideoPlayer.Length;
		float value = (length.TotalSeconds > 0.0) ? ((float)(VideoPlayer.Position.TotalSeconds / length.TotalSeconds)) : 0f;
		Rectangle hit = new Rectangle(bar.X, bar.Y - 6, bar.Width, bar.Height + 12);
		bool hover = enabled && MusicPlayerWindow.MouseIn(hit);
		float at = MathHelper.Clamp((float)(MusicPlayerWindow.MousePoint.X - bar.X) / bar.Width, 0f, 1f);
		if (hover)
		{
			MusicDrawing.Hover("video:seek", MusicPlayer.FormatTime(TimeSpan.FromSeconds(length.TotalSeconds * at)));
			if (MusicPlayerWindow.Clicked)
			{
				_seeking = true;
				MusicPlayerWindow.StartSurfaceDrag();
			}
		}
		if (_seeking)
		{
			_seekValue = at;
			value = at;
			if (!MusicPlayerWindow.MouseDown)
			{
				_seeking = false;
				VideoPlayer.Seek(TimeSpan.FromSeconds(length.TotalSeconds * _seekValue));
			}
		}
		bool big = hover || _seeking;
		Rectangle track = big ? new Rectangle(bar.X, bar.Y - 1, bar.Width, bar.Height + 2) : bar;
		MusicDrawing.Fill(sb, track, new Color(15, 20, 40) * 0.95f);
		foreach (KeyValuePair<float, float> range in VideoPlayer.Buffered())
		{
			MusicDrawing.Fill(sb, new Rectangle(track.X + (int)(track.Width * range.Key), track.Y, Math.Max(1, (int)(track.Width * (range.Value - range.Key))), track.Height), new Color(120, 130, 165) * 0.55f);
		}
		int played = (int)(track.Width * MathHelper.Clamp(value, 0f, 1f));
		MusicDrawing.Fill(sb, new Rectangle(track.X, track.Y, played, track.Height), _red);
		if (enabled)
		{
			int knob = big ? 14 : 10;
			MusicDrawing.Fill(sb, new Rectangle(track.X + played - knob / 2, track.Center.Y - knob / 2, knob, knob), big ? Color.White : new Color(255, 200, 205));
		}
	}

	private static void VolumeControl(SpriteBatch sb, Rectangle r)
	{
		Rectangle icon = new Rectangle(r.X, r.Y - 4, 18, 22);
		if (MusicDrawing.BareIcon(sb, "video:mute", icon, MusicDrawing.Tex("Images/UI/CharCreation/Voice"), null, VideoPlayer.Muted ? "Unmute" : "Mute", VideoPlayer.Muted ? (MusicPlayerWindow.MutedColor * 0.6f) : Color.White))
		{
			VideoPlayer.ToggleMute();
		}
		if (VideoPlayer.Muted)
		{
			MusicDrawing.Icon(sb, MusicDrawing.Tex("Images/MapDeath"), null, new Rectangle(icon.X + 8, icon.Y + 10, 12, 12), Color.White);
		}
		Rectangle bar = new Rectangle(r.X + 26, r.Y + 4, r.Width - 26, 6);
		Rectangle hit = new Rectangle(bar.X - 4, r.Y - 4, bar.Width + 8, 22);
		bool hover = MusicPlayerWindow.MouseIn(hit);
		if (hover)
		{
			MusicDrawing.Hover("video:volume", "Volume " + (int)Math.Round(VideoPlayer.Volume * 100f) + "%");
			if (MusicPlayerWindow.Clicked)
			{
				_volumeDrag = true;
				MusicPlayerWindow.StartSurfaceDrag();
			}
		}
		if (_volumeDrag)
		{
			VideoPlayer.SetVolume(MathHelper.Clamp((float)(MusicPlayerWindow.MousePoint.X - bar.X) / bar.Width, 0f, 1f));
			if (!MusicPlayerWindow.MouseDown)
			{
				_volumeDrag = false;
			}
		}
		MusicDrawing.ProgressBar(sb, bar, VideoPlayer.Muted ? 0f : VideoPlayer.Volume, new Color(120, 190, 255), hover || _volumeDrag);
	}

	/// <summary>The download button (a chest), with its progress; returns its left edge.</summary>
	private static int DownloadButton(SpriteBatch sb, int right, int y)
	{
		VideoInfo current = VideoPlayer.Current;
		bool can = current != null && (!current.IsLocal || current.Web) && !VideoPlayer.Loading;
		VideoDownloads.Job job = (current != null) ? VideoDownloads.Find(current.Id) : null;
		string tip = "Download (saves an .mp4 in Videos)";
		Color tint = Color.White;
		if (job != null && job.Done)
		{
			tip = "Saved: click to open the folder";
			tint = new Color(140, 255, 150);
		}
		else if (job != null)
		{
			tip = job.Status + " " + (int)(job.Progress * 100f) + "%  (click to cancel)";
		}
		Rectangle button = new Rectangle(right - 28, y, 28, 28);
		if (MusicDrawing.IconButton(sb, "video:download", button, ItemTexture(ItemID.Chest), null, tip, job != null && !job.Done, can, tint))
		{
			if (job != null && job.Done)
			{
				Directory.CreateDirectory(VideoPlayer.VideosFolder);
				Utils.OpenFolder(VideoPlayer.VideosFolder);
			}
			else if (job != null)
			{
				VideoDownloads.Cancel(job);
			}
			else
			{
				VideoDownloads.Start(current, (VideoPlayer.CurrentHeight > 0) ? VideoPlayer.CurrentHeight : VideoPlayer.Quality);
			}
		}
		if (job != null && !job.Done)
		{
			string percent = (int)(job.Progress * 100f) + "%";
			float pw = MusicDrawing.Measure(percent, 0.62f);
			MusicDrawing.Text(sb, percent, button.X - pw - 4, y + 8, MusicPlayerWindow.AccentColor, 0.62f);
			return button.X - (int)pw - 8;
		}
		return button.X;
	}

	private static void OpenSpeedMenu(Vector2 at)
	{
		List<MusicPlayerWindow.MenuEntry> entries = new List<MusicPlayerWindow.MenuEntry>
		{
			new MusicPlayerWindow.MenuEntry
			{
				Label = "Speed",
				Header = true
			}
		};
		foreach (float speed in VideoPlayer.Speeds)
		{
			float chosen = speed;
			entries.Add(new MusicPlayerWindow.MenuEntry
			{
				Label = ((Math.Abs(VideoPlayer.Speed - speed) < 0.01f) ? "> " : "   ") + ((speed == 1f) ? "Normal" : VideoPlayer.SpeedLabel(speed)),
				Action = delegate
				{
					VideoPlayer.SetSpeed(chosen);
				}
			});
		}
		MusicPlayerWindow.OpenMenu(entries, at - new Vector2(0f, entries.Count * 26 + 8));
	}

	private static void OpenLanguageMenu(Vector2 at, List<VideoFormat> tracks, VideoFormat playing)
	{
		List<MusicPlayerWindow.MenuEntry> entries = new List<MusicPlayerWindow.MenuEntry>
		{
			new MusicPlayerWindow.MenuEntry
			{
				Label = "Audio language",
				Header = true
			},
			new MusicPlayerWindow.MenuEntry
			{
				Label = (string.IsNullOrEmpty(VideoPlayer.AudioLanguage) ? "* " : "   ") + "Game's language when there is one",
				Action = delegate
				{
					VideoPlayer.AudioLanguage = "";
					VideoPlayer.SetSoundTrack(VideoSource.ChooseSound(tracks, ""), remember: false);
				}
			}
		};
		foreach (VideoFormat track in tracks)
		{
			VideoFormat chosen = track;
			entries.Add(new MusicPlayerWindow.MenuEntry
			{
				Label = ((track == playing) ? "> " : "   ") + track.LanguageName + (track.Original ? " (original)" : ""),
				Action = delegate
				{
					VideoPlayer.SetSoundTrack(chosen);
				}
			});
		}
		MusicPlayerWindow.OpenMenu(entries, at - new Vector2(0f, entries.Count * 26 + 8));
	}

	private static void OpenQualityMenu(Vector2 at)
	{
		VideoInfo current = VideoPlayer.Current;
		List<int> heights = (current != null && current.Pictures.Count > 0) ? current.Heights : new List<int> { 1080, 720, 480, 360, 240, 144 };
		List<MusicPlayerWindow.MenuEntry> entries = new List<MusicPlayerWindow.MenuEntry>
		{
			new MusicPlayerWindow.MenuEntry
			{
				Label = "Quality",
				Header = true
			}
		};
		foreach (int height in heights)
		{
			int chosen = height;
			bool playing = height == VideoPlayer.CurrentHeight;
			entries.Add(new MusicPlayerWindow.MenuEntry
			{
				Label = (playing ? "> " : "   ") + height + "p" + ((height >= 720) ? " HD" : ""),
				Action = delegate
				{
					VideoPlayer.SetQuality(chosen);
				}
			});
		}
		MusicPlayerWindow.OpenMenu(entries, at - new Vector2(0f, entries.Count * 26 + 8));
	}

	#endregion

	#region Mini and full screen

	private static void DrawMini(SpriteBatch sb, int screenW, int screenH)
	{
		int w = Math.Min(MiniWidth, screenW - 16);
		int h = w * 9 / 16;
		Vector2 pos = new Vector2((MiniX < 0f) ? float.NaN : MiniX, (MiniY < 0f) ? float.NaN : MiniY);
		Rectangle r = MusicPlayerWindow.ClampToScreen(ref pos, w + 8, h + 14, screenW, screenH, new Vector2(screenW - w - 30, screenH - h - 170));
		_screenRect = MusicPlayerWindow.ToScreen(r);
		MusicDrawing.Box(sb, r, MusicPlayerWindow.BackColor * 0.97f);
		Rectangle video = new Rectangle(r.X + 4, r.Y + 4, w, h);
		MusicDrawing.Fill(sb, video, Color.Black);
		Texture2D frame = VideoPlayer.Frame();
		if (frame != null)
		{
			DrawSmooth(sb, frame, Fit(frame.Width, frame.Height, video), Color.White);
		}
		if (VideoPlayer.Loading || VideoPlayer.IsBuffering)
		{
			Spinner(sb, new Vector2(video.Center.X, video.Center.Y), 0.8f);
		}
		bool hover = MusicPlayerWindow.MouseIn(r) || _dragMini;
		if (hover)
		{
			MusicDrawing.Fill(sb, video, Color.Black * 0.35f);
			Texture2D playPause = MusicDrawing.Tex("Images/UI/IconPlayPause");
			bool playing = VideoPlayer.IsPlaying;
			if (MusicDrawing.BareIcon(sb, "videomini:play", new Rectangle(video.Center.X - 18, video.Center.Y - 18, 36, 36), playPause, new Rectangle(0, playing ? 16 : 0, 16, 16), playing ? "Pause" : "Play", Color.White))
			{
				VideoPlayer.TogglePause();
			}
			if (MusicDrawing.BareIcon(sb, "videomini:back", new Rectangle(video.Center.X - 62, video.Center.Y - 12, 24, 24), MusicDrawing.Tex("Images/UI/IconPrev"), null, "Back 10 seconds", Color.White))
			{
				VideoPlayer.SeekBy(-10.0);
			}
			if (MusicDrawing.BareIcon(sb, "videomini:forward", new Rectangle(video.Center.X + 38, video.Center.Y - 12, 24, 24), MusicDrawing.Tex("Images/UI/IconNext"), null, "Forward 10 seconds", Color.White))
			{
				VideoPlayer.SeekBy(10.0);
			}
			if (MusicDrawing.BareIcon(sb, "videomini:restore", new Rectangle(video.Right - 50, video.Y + 6, 18, 18), MusicDrawing.Tex("Images/UI/TexturePackButtons"), new Rectangle(0, 0, 32, 32), "Back to the window (double click)", Color.White))
			{
				SetMode(WindowMode.Normal);
			}
			if (MusicDrawing.BareIcon(sb, "videomini:close", new Rectangle(video.Right - 25, video.Y + 6, 16, 17), MusicDrawing.Tex("Images/MapDeath"), null, "Close the video", Color.White))
			{
				VideoPlayer.Close();
				Hide();
				return;
			}
			if (VideoPlayer.Current != null)
			{
				MusicDrawing.Text(sb, VideoPlayer.Current.Title, video.X + 8, video.Y + 8, Color.White, 0.7f, video.Width - 70);
			}
			if (MusicPlayerWindow.HoveredControl == null && MusicPlayerWindow.Clicked && DoubleClicked())
			{
				MusicPlayerWindow.Clicked = false;
				SetMode(WindowMode.Normal);
			}
		}
		Seekbar(sb, new Rectangle(r.X + 6, r.Bottom - 8, r.Width - 12, 3), VideoPlayer.HasVideo && hover);
		MusicPlayerWindow.HandleDrag(ref _dragMini, ref pos, r, null);
		MiniX = pos.X;
		MiniY = pos.Y;
	}

	private static void DrawMaximized(SpriteBatch sb, int screenW, int screenH)
	{
		Rectangle r = new Rectangle(0, 0, screenW, screenH);
		_screenRect = MusicPlayerWindow.ToScreen(r);
		bool controls = Main.timeForVisualEffects - _lastMouseMove < 150.0 || !VideoPlayer.IsPlaying || _seeking || _volumeDrag || MusicPlayerWindow.MenuEntries != null;
		Rectangle bar = new Rectangle(r.X + 16, r.Bottom - 74, r.Width - 32, 62);
		Rectangle top = new Rectangle(r.X, r.Y, r.Width, 48);
		Rectangle picture = r;
		if (controls)
		{
			// Keep the controls off the picture's clickable area.
			picture = new Rectangle(r.X, top.Bottom, r.Width, bar.Y - top.Bottom - 8);
		}
		MusicDrawing.Fill(sb, r, Color.Black);
		DrawPicture(sb, picture, canMaximize: true);
		if (!controls)
		{
			return;
		}
		MusicDrawing.Fill(sb, top, Color.Black * 0.55f);
		VideoInfo current = VideoPlayer.Current;
		Utils.DrawBorderStringBig(sb, IsaClient.Languages.L10n.T("Video"), new Vector2(top.X + 16, top.Y + 9), MusicPlayerWindow.AccentColor, 0.5f);
		if (current != null)
		{
			MusicDrawing.Text(sb, current.Title, top.X + 96, top.Y + 9, Color.White, 0.9f, top.Width - 96 - 100);
			MusicDrawing.Text(sb, current.Channel, top.X + 96, top.Y + 28, MusicPlayerWindow.MutedColor, 0.68f, top.Width - 96 - 100);
		}
		if (MusicDrawing.BareIcon(sb, "videomax:restore", new Rectangle(top.Right - 66, top.Y + 13, 22, 22), MusicDrawing.Tex("Images/UI/TexturePackButtons"), new Rectangle(32, 0, 32, 32), "Exit full screen (double click)", Color.White))
		{
			SetMode(WindowMode.Normal);
		}
		if (MusicDrawing.BareIcon(sb, "videomax:close", new Rectangle(top.Right - 34, top.Y + 14, 19, 20), MusicDrawing.Tex("Images/MapDeath"), null, "Close the video", Color.White))
		{
			VideoPlayer.Close();
			Hide();
			return;
		}
		MusicDrawing.Box(sb, new Rectangle(bar.X - 8, bar.Y - 4, bar.Width + 16, bar.Height + 8), MusicPlayerWindow.BackColor * 0.85f);
		DrawControls(sb, bar, roomy: true);
	}

	#endregion

	#region Browse

	private static void DrawBrowse(SpriteBatch sb, Rectangle area)
	{
		MusicDrawing.Box(sb, area, MusicPlayerWindow.PanelDarkColor * 0.95f);
		// Search.
		Rectangle field = new Rectangle(area.X + 8, area.Y + 8, area.Width - 16 - 96, 30);
		_searchText = MusicDrawing.Field(sb, "videosearch", field, _searchText, "Search YouTube or paste a link...", 200, searchIcon: true);
		bool enter = MusicPlayerWindow.FocusedField == "videosearch" && Main.inputTextEnter;
		if ((MusicDrawing.TextButton(sb, "video:search", new Rectangle(field.Right + 6, field.Y, 90, 30), _searching ? "..." : "Search", new Color(150, 45, 60), "Search (or press Enter)", !_searching && _searchText.Trim().Length > 0) || enter) && _searchText.Trim().Length > 0)
		{
			MusicPlayerWindow.FocusedField = null;
			Search(_searchText);
		}
		// Tabs.
		int tabY = field.Bottom + 6;
		int x = area.X + 8;
		x = Tab(sb, BrowseTab.Results, "Results" + ((_results.Count > 0) ? (" (" + _results.Count + ")") : ""), x, tabY);
		x = Tab(sb, BrowseTab.Recent, "Continue watching", x, tabY);
		x = Tab(sb, BrowseTab.Saved, "Saved", x, tabY);
		if (_tab == BrowseTab.Saved && MusicDrawing.TextButton(sb, "video:folder", new Rectangle(area.Right - 8 - 110, tabY, 110, 26), "Open folder", MusicPlayerWindow.PanelColor, "Opens Videos (your saved videos)"))
		{
			Directory.CreateDirectory(VideoPlayer.VideosFolder);
			Utils.OpenFolder(VideoPlayer.VideosFolder);
		}
		Rectangle list = new Rectangle(area.X + 4, tabY + 32, area.Width - 8, area.Bottom - tabY - 36);
		switch (_tab)
		{
		case BrowseTab.Results:
			DrawResults(sb, list);
			break;
		case BrowseTab.Recent:
			DrawRecent(sb, list);
			break;
		default:
			DrawSaved(sb, list);
			break;
		}
	}

	private static int Tab(SpriteBatch sb, BrowseTab tab, string label, int x, int y)
	{
		int width = (int)MusicDrawing.Measure(label, 0.78f) + 24;
		if (MusicDrawing.TextButton(sb, "video:tab" + (int)tab, new Rectangle(x, y, width, 26), label, (_tab == tab) ? MusicPlayerWindow.SelectedColor : MusicPlayerWindow.PanelColor))
		{
			_tab = tab;
			MusicPlayerWindow.ScrollOffsets["video:list"] = 0f;
		}
		return x + width + 6;
	}

	private sealed class Row
	{
		public string Key;

		public string Title;

		public string Sub;

		public string Thumbnail;

		public double Duration;

		public float Watched;

		public float Progress = -1f;

		public Action Play;

		public Action Download;

		public Action Remove;

		public string RemoveTip;
	}

	private static void DrawResults(SpriteBatch sb, Rectangle list)
	{
		if (_searching)
		{
			Spinner(sb, new Vector2(list.Center.X, list.Center.Y - 10), 0.9f);
			CenteredText(sb, "Searching \"" + _searchedFor + "\"...", list, list.Center.Y + 24, MusicPlayerWindow.MutedColor, 0.8f);
			return;
		}
		if (_results.Count == 0)
		{
			CenteredText(sb, (_searchedFor.Length > 0) ? ("Nothing found for \"" + _searchedFor + "\".") : "Search YouTube above, or paste any video link.", list, list.Center.Y - 22, Color.White, 0.85f);
			CenteredText(sb, "From the chat: .video <name or link>", list, list.Center.Y + 4, MusicPlayerWindow.MutedColor, 0.75f);
			return;
		}
		List<Row> rows = _results.Select((VideoInfo info) => new Row
		{
			Key = info.Id,
			Title = info.Title,
			Sub = string.IsNullOrEmpty(info.Channel) ? "YouTube" : info.Channel,
			Thumbnail = info.Thumbnail,
			Duration = info.Duration,
			Play = delegate
			{
				VideoPlayer.Play(info, TimeSpan.Zero);
			},
			Download = delegate
			{
				VideoDownloads.Start(info, VideoPlayer.Quality);
			}
		}).ToList();
		DrawRows(sb, list, rows, "");
	}

	private static void DrawRecent(SpriteBatch sb, Rectangle list)
	{
		List<VideoHistory.Entry> recent = VideoHistory.Recent();
		List<Row> rows = recent.Select((VideoHistory.Entry entry) => new Row
		{
			Key = entry.Id,
			Title = entry.Title,
			Sub = (string.IsNullOrEmpty(entry.Channel) ? "" : (entry.Channel + "  •  ")) + ((entry.Position > 0.0) ? ("stopped at " + MusicPlayer.FormatTime(TimeSpan.FromSeconds(entry.Position))) : "watched"),
			Thumbnail = entry.Thumbnail,
			Duration = entry.Duration,
			Watched = entry.Watched,
			Play = delegate
			{
				VideoPlayer.Play(entry.ToInfo(), TimeSpan.FromSeconds(entry.Position));
			},
			Download = (entry.LocalPath == null) ? ((Action)delegate
			{
				VideoDownloads.Start(entry.ToInfo(), VideoPlayer.Quality);
			}) : null,
			Remove = delegate
			{
				VideoHistory.Forget(entry.Id);
			},
			RemoveTip = "Remove from the list"
		}).ToList();
		DrawRows(sb, list, rows, "Videos you watch show up here, and continue where you stopped.");
	}

	private static void DrawSaved(SpriteBatch sb, Rectangle list)
	{
		List<Row> rows = new List<Row>();
		foreach (VideoDownloads.Job job in VideoDownloads.Jobs())
		{
			if (job.Done)
			{
				continue;
			}
			VideoDownloads.Job running = job;
			rows.Add(new Row
			{
				Key = "job" + job.GetHashCode(),
				Title = job.Title ?? "Video",
				Sub = job.Status,
				Thumbnail = (job.Id != null && job.Id.Length == 11) ? ("https://i.ytimg.com/vi/" + job.Id + "/mqdefault.jpg") : null,
				Progress = job.Failed ? (-1f) : job.Progress,
				Remove = delegate
				{
					if (running.Failed || running.Cancelled)
					{
						VideoDownloads.Forget(running);
					}
					else
					{
						VideoDownloads.Cancel(running);
					}
				},
				RemoveTip = (job.Failed || job.Cancelled) ? "Remove from the list" : "Cancel the download"
			});
		}
		foreach (FileInfo file in VideoDownloads.Saved())
		{
			string path = file.FullName;
			string id = IdInName(file.Name);
			rows.Add(new Row
			{
				Key = path,
				Title = Path.GetFileNameWithoutExtension(file.Name),
				Sub = (file.Length / 1048576.0).ToString("0.0") + " MB  •  " + file.LastWriteTime.ToString("d"),
				Thumbnail = (id != null) ? ("https://i.ytimg.com/vi/" + id + "/mqdefault.jpg") : null,
				Play = delegate
				{
					VideoPlayer.Play(VideoSource.FromFile(path), TimeSpan.Zero);
				}
			});
		}
		DrawRows(sb, list, rows, "Videos you download (the chest button) are saved here, in Videos.");
	}

	/// <summary>The YouTube id at the end of a saved file's name ("Title [id].mp4").</summary>
	private static string IdInName(string name)
	{
		int open = name.LastIndexOf('[');
		int close = name.LastIndexOf(']');
		if (open < 0 || close != open + 12)
		{
			return null;
		}
		return name.Substring(open + 1, 11);
	}

	private static void DrawRows(SpriteBatch sb, Rectangle list, List<Row> rows, string empty)
	{
		if (rows.Count == 0)
		{
			CenteredText(sb, empty, list, list.Center.Y - 10, MusicPlayerWindow.MutedColor, 0.8f);
			return;
		}
		const int rowH = 72;
		float scroll = MusicDrawing.BeginScroll("video:list", list);
		MusicPlayerWindow.BeginClip(sb, list);
		int y = list.Y - (int)scroll;
		foreach (Row row in rows)
		{
			Rectangle r = new Rectangle(list.X + 2, y, list.Width - 14, rowH - 4);
			y += rowH;
			if (!MusicPlayerWindow.Visible(r))
			{
				continue;
			}
			bool hover = MusicPlayerWindow.MouseIn(r);
			MusicDrawing.Fill(sb, r, hover ? (MusicPlayerWindow.RowHoverColor * 0.6f) : (MusicPlayerWindow.PanelColor * 0.35f));
			Rectangle thumb = new Rectangle(r.X + 4, r.Y + 3, 110, 62);
			MusicDrawing.Fill(sb, thumb, Color.Black);
			Texture2D picture = VideoThumbnails.Get(row.Thumbnail);
			if (picture != null)
			{
				DrawSmooth(sb, picture, thumb, Color.White);
			}
			else
			{
				MusicDrawing.Icon(sb, MusicDrawing.Tex("Images/UI/Camera_0"), null, new Rectangle(thumb.Center.X - 12, thumb.Center.Y - 12, 24, 24), Color.White * 0.5f);
			}
			if (row.Duration > 0.0)
			{
				string length = MusicPlayer.FormatTime(TimeSpan.FromSeconds(row.Duration));
				float lw = MusicDrawing.Measure(length, 0.6f);
				Rectangle badge = new Rectangle(thumb.Right - (int)lw - 8, thumb.Bottom - 17, (int)lw + 6, 15);
				MusicDrawing.Fill(sb, badge, Color.Black * 0.75f);
				MusicDrawing.Text(sb, length, badge.X + 3, badge.Y + 1, Color.White, 0.6f);
			}
			if (row.Watched > 0f)
			{
				MusicDrawing.Fill(sb, new Rectangle(thumb.X, thumb.Bottom - 3, thumb.Width, 3), Color.Gray * 0.6f);
				MusicDrawing.Fill(sb, new Rectangle(thumb.X, thumb.Bottom - 3, (int)(thumb.Width * row.Watched), 3), _red);
			}
			int buttonsW = ((row.Download != null) ? 32 : 0) + ((row.Remove != null) ? 26 : 0);
			float textX = thumb.Right + 10;
			float textW = r.Right - textX - buttonsW - 10;
			MusicDrawing.Text(sb, row.Title, textX, r.Y + 12, hover ? Color.White : new Color(235, 238, 248), 0.8f, textW);
			MusicDrawing.Text(sb, row.Sub, textX, r.Y + 36, MusicPlayerWindow.MutedColor, 0.68f, textW);
			if (row.Progress >= 0f)
			{
				MusicDrawing.ProgressBar(sb, new Rectangle((int)textX, r.Bottom - 12, (int)Math.Min(textW, 260f), 5), row.Progress, MusicPlayerWindow.AccentColor, hover: false);
			}
			int bx = r.Right - 6;
			if (row.Remove != null)
			{
				bx -= 20;
				if (MusicDrawing.BareIcon(sb, "videorow:remove:" + row.Key, new Rectangle(bx, r.Center.Y - 9, 18, 18), MusicDrawing.Tex("Images/UI/SearchCancel"), null, row.RemoveTip, Color.White))
				{
					row.Remove();
				}
				bx -= 6;
			}
			if (row.Download != null)
			{
				bx -= 28;
				VideoDownloads.Job job = VideoDownloads.Find(row.Key);
				string tip = (job == null) ? "Download" : (job.Done ? "Saved" : (job.Status + " " + (int)(job.Progress * 100f) + "%"));
				if (MusicDrawing.IconButton(sb, "videorow:download:" + row.Key, new Rectangle(bx, r.Center.Y - 14, 28, 28), ItemTexture(ItemID.Chest), null, tip, job != null && !job.Done, job == null, (job != null && job.Done) ? new Color(140, 255, 150) : Color.White))
				{
					row.Download();
				}
			}
			if (hover && MusicPlayerWindow.HoveredControl == null && row.Play != null)
			{
				MusicDrawing.Hover("videorow:" + row.Key, "Play");
				if (MusicPlayerWindow.Clicked)
				{
					MusicPlayerWindow.Clicked = false;
					MusicPlayerWindow.FocusedField = null;
					row.Play();
					_page = Page.Player;
				}
			}
		}
		MusicPlayerWindow.EndClip(sb);
		MusicDrawing.EndScroll(sb, "video:list", list, rows.Count * rowH);
	}

	#endregion

	private static Texture2D ItemTexture(int type)
	{
		Main.instance.LoadItem(type);
		return TextureAssets.Item[type].Value;
	}
}
