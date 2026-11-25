using System;
using IsaClient.Games;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using L10n = IsaClient.Languages.L10n;

namespace IsaClient.Gta;

public static class GtaSix
{
	private enum Stage
	{
		Closed,
		Controller,
		Loading,
		Intro,
		Title,
		Outro
	}

	private const string Layer = "games:gta";

	private const string FocusOwner = "gta";

	private static Stage _stage;

	private static double _stageAt;

	private static double _fadeAt = -1.0;

	private static bool _registered;

	private static bool _padWas;

	private static double _padAt = -10.0;

	private static bool _withPad;

	private static GamePadState _prevPad;

	private static KeyboardState _prevKeys;

	private static Texture2D _blurred;

	private static Texture2D _backdrop;

	private static bool _skip;

	private static float _volume;

	private static Action _pending;

	private static double _startAt = -1.0;

	private static float _titleZoom = 1f;

	private static readonly string[] Tips =
	{
		"Tip: the police remember your face. Wear a hat.",
		"Tip: Leonida's beaches are best at sunset. So are its car chases.",
		"Tip: honking at a flamingo is not a crime. Yet.",
		"Tip: your phone has 74 apps. Three of them are legal.",
		"Tip: stay hydrated. Florida is hot."
	};

	public static bool IsOpen => _stage != Stage.Closed;

	public static bool HidesCursor => _stage == Stage.Loading || _stage == Stage.Intro || (_stage == Stage.Title && _startAt >= 0.0) || (_stage == Stage.Outro && Now - _stageAt < 1.1);

	private static double Now => GameArt.Now;

	public static string StatusText
	{
		get
		{
			if (GtaTrailer.Downloading)
			{
				return L10n.F("Installing... {0}%", (int)(GtaTrailer.Progress * 100f));
			}
			return GtaTrailer.Ready ? L10n.T("Installed") : L10n.T("212 GB download");
		}
	}

	public static void Open()
	{
		if (Main.gameMenu)
		{
			return;
		}
		Register();
		IsaClient.Menu.InputState.TildeTerminal = false;
		_stage = Stage.Controller;
		_stageAt = Now;
		_fadeAt = -1.0;
		_withPad = false;
		_padWas = PadConnected();
		_prevPad = PadState();
		_prevKeys = Main.keyState;
		GtaTrailer.Prepare();
		MediaFocus.Claim(FocusOwner);
		MusicPlayerWindow.BringSurfaceToFront(Layer);
	}

	public static void Close()
	{
		if (_stage == Stage.Closed)
		{
			return;
		}
		_stage = Stage.Closed;
		_pending = null;
		GtaTrailer.Stop();
		GtaVolume.Restore();
		_blurred?.Dispose();
		_blurred = null;
		MediaFocus.Release(FocusOwner);
	}

	private static void Register()
	{
		if (_registered)
		{
			return;
		}
		_registered = true;
		MusicPlayerWindow.AddSurface(new MusicPlayerWindow.FloatingSurface
		{
			Layer = Layer,
			Shown = () => _stage != Stage.Closed && !Main.gameMenu,
			ScreenRect = () => new Rectangle(0, 0, Main.screenWidth, Main.screenHeight),
			Draw = Draw
		});
		MediaFocus.CinemaPlaying = () => _stage != Stage.Closed;
		Main.instance.Exiting += (_, _) => GtaTrailer.Stop();
	}

	private static GamePadState PadState()
	{
		for (PlayerIndex i = PlayerIndex.One; i <= PlayerIndex.Four; i++)
		{
			try
			{
				GamePadState state = GamePad.GetState(i);
				if (state.IsConnected)
				{
					return state;
				}
			}
			catch
			{
				return default(GamePadState);
			}
		}
		return default(GamePadState);
	}

	private static bool PadConnected() => PadState().IsConnected;

	public static void Update()
	{
		if (_stage == Stage.Closed)
		{
			return;
		}
		if (Main.gameMenu)
		{
			Close();
			return;
		}
		Action pending = _pending;
		_pending = null;
		if (pending != null)
		{
			pending();
			if (_stage == Stage.Closed)
			{
				return;
			}
		}
		GamePadState pad = PadState();
		KeyboardState keys = Main.keyState;
		bool focus = Main.hasFocus;
		bool Pressed(Keys k) => focus && keys.IsKeyDown(k) && !_prevKeys.IsKeyDown(k);
		bool PadPressed(Buttons b) => pad.IsButtonDown(b) && !_prevPad.IsButtonDown(b);
		bool back = Pressed(Keys.Escape) || PadPressed(Buttons.B) || PadPressed(Buttons.Back);
		bool accept = Pressed(Keys.Enter) || PadPressed(Buttons.A) || PadPressed(Buttons.Start);
		if (pad.IsConnected && !_padWas)
		{
			_padAt = Now;
		}
		_padWas = pad.IsConnected;
		switch (_stage)
		{
		case Stage.Controller:
			if (back)
			{
				Close();
			}
			else if (accept && pad.IsConnected)
			{
				Begin(true);
			}
			break;
		case Stage.Loading:
			if (back)
			{
				Close();
				break;
			}
			if (GtaTrailer.Error != null && !GtaTrailer.Downloading)
			{
				if (accept)
				{
					GtaTrailer.Prepare();
				}
				break;
			}
			if (_fadeAt < 0.0 && GtaTrailer.Ready && Now - _stageAt > 3.2)
			{
				_fadeAt = Now;
			}
			if (_fadeAt >= 0.0 && Now - _fadeAt > 0.8)
			{
				_volume = 1f;
				GtaVolume.Raise(0.75f);
				GtaTrailer.Play(_volume);
				_stage = Stage.Intro;
				_stageAt = Now;
				_fadeAt = -1.0;
				_skip = false;
			}
			break;
		case Stage.Intro:
		{
			if (back || accept)
			{
				_skip = true;
			}
			double length = GtaTrailer.Length;
			bool stuck = !GtaTrailer.HasPicture && Now - _stageAt > 10.0;
			if (_fadeAt < 0.0 && (_skip || stuck || GtaTrailer.Ended || GtaTrailer.Position >= length - 0.9))
			{
				_fadeAt = Now;
			}
			if (_fadeAt >= 0.0)
			{
				GtaTrailer.SetVolume(_volume * GameArt.Clamp01(1.0 - (Now - _fadeAt) / 0.8));
				if (Now - _fadeAt > 0.9)
				{
					GtaTrailer.Stop();
					_stage = Stage.Title;
					_stageAt = Now;
					_fadeAt = -1.0;
					_startAt = -1.0;
				}
			}
			break;
		}
		case Stage.Title:
			if (_startAt < 0.0)
			{
				if (back)
				{
					Close();
				}
				else if (accept && Now - _stageAt > 0.8)
				{
					_startAt = Now;
				}
			}
			else if (Now - _startAt > 3.4)
			{
				BeginOutro();
			}
			break;
		case Stage.Outro:
		{
			double t = Now - _stageAt;
			if (t > 1.6)
			{
				if (back || accept)
				{
					Close();
				}
				else if (PadPressed(Buttons.X))
				{
					Replay();
				}
			}
			break;
		}
		}
		_prevPad = pad;
		_prevKeys = keys;
	}

	private static void Begin(bool pad)
	{
		_withPad = pad;
		_stage = Stage.Loading;
		_stageAt = Now;
		_fadeAt = -1.0;
		GtaTrailer.Prepare();
	}

	private static void BeginOutro()
	{
		_blurred?.Dispose();
		_blurred = GtaArt.Blurred(GtaArt.KeyArt ?? GtaArt.Cover);
		_stage = Stage.Outro;
		_stageAt = Now;
	}

	private static void Replay()
	{
		_blurred?.Dispose();
		_blurred = null;
		_stage = Stage.Loading;
		_stageAt = Now - 4.0;
		_fadeAt = Now - 0.7;
	}

	private static void Draw(SpriteBatch sb, int screenW, int screenH)
	{
		Rectangle screen = new Rectangle(0, 0, screenW, screenH);
		GtaArt.Smooth(sb);
		GtaArt.Fill(sb, screen, Color.Black);
		switch (_stage)
		{
		case Stage.Controller:
			DrawController(sb, screen);
			break;
		case Stage.Loading:
			DrawLoading(sb, screen);
			break;
		case Stage.Intro:
			DrawIntro(sb, screen);
			break;
		case Stage.Title:
			DrawTitle(sb, screen);
			break;
		case Stage.Outro:
			DrawOutro(sb, screen);
			break;
		}
		GameArt.Smooth(sb);
		PlayerInput.WritingText = true;
		Main.chatRelease = false;
		if (Main.LocalPlayer != null)
		{
			Main.LocalPlayer.mouseInterface = true;
		}
		MusicPlayerWindow.Clicked = false;
	}

	private static Texture2D Backdrop
	{
		get
		{
			if (_backdrop == null || _backdrop.IsDisposed)
			{
				_backdrop = GtaArt.Blurred(GtaArt.Cover);
			}
			return _backdrop;
		}
	}

	private static void DrawController(SpriteBatch sb, Rectangle screen)
	{
		double t = Now - _stageAt;
		float appear = GameArt.EaseOut(GameArt.Clamp01(t / 0.6));
		GtaArt.CoverFit(sb, Backdrop, screen, Color.White * appear, 1.08f + 0.02f * (float)Math.Sin(Now * 0.25));
		GtaArt.Fill(sb, screen, Color.Black * 0.42f);
		GtaArt.VerticalShade(sb, new Rectangle(0, screen.Height / 2, screen.Width, screen.Height - screen.Height / 2), Color.Transparent, Color.Black * 0.75f);
		float cx = screen.Width / 2f;
		int column = Math.Min(620, screen.Width - 80);
		float rise = (1f - appear) * 18f;
		Texture2D logo = GtaArt.Logo;
		int logoH = Math.Min(160, screen.Height / 6);
		int y = (int)Math.Max(28f, screen.Height / 2f - 300f + rise);
		if (logo != null)
		{
			int logoW = logoH * logo.Width / logo.Height;
			sb.Draw(logo, new Rectangle((int)(cx - logoW / 2f), y, logoW, logoH), Color.White * appear);
		}
		y += logoH + 34;
		bool connected = PadConnected();
		float pop = GameArt.Clamp01((Now - _padAt) / 0.5);
		float bounce = connected ? (1f + 0.08f * (float)Math.Sin(pop * Math.PI)) : 1f;
		Color padColor = connected ? Color.Lerp(Color.White, new Color(120, 230, 150), 0.55f) : Color.White;
		float breathe = connected ? 1f : (0.55f + 0.45f * (float)(0.5 + 0.5 * Math.Sin(Now * 2.4)));
		int padH = 92;
		Vector2 padCenter = new Vector2(cx, y + padH / 2f);
		GameArt.Disc(sb, padCenter, padH * 2.6f, padH * 1.9f, (connected ? new Color(80, 220, 140) : GtaArt.Pink) * (0.16f * appear * breathe));
		Texture2D pad = GtaArt.Pad;
		if (pad != null)
		{
			float h = padH * bounce;
			float w = h * pad.Width / pad.Height;
			sb.Draw(pad, new Rectangle((int)(padCenter.X - w / 2f), (int)(padCenter.Y - h / 2f), (int)w, (int)h), padColor * (appear * (connected ? 1f : (0.7f + 0.3f * breathe))));
		}
		y += padH + 26;
		string title = L10n.T(connected ? "Controller connected" : "Connect a controller");
		float titleScale = Math.Min(0.86f, column / Math.Max(1f, GtaArt.Measure(title, 1f, true).X));
		GtaArt.Centered(sb, title, cx, y, Color.White * appear, titleScale, true);
		y += (int)GtaArt.Measure(title, titleScale, true).Y + 2;
		string sub = L10n.T(connected ? "You're all set. Press A or click Continue to start." : "Grand Theft Auto VI plays best with a controller. Plug one in or turn it on.");
		GtaArt.Centered(sb, GtaArt.FitText(sub, column, 0.92f), cx, y, GtaArt.Soft * appear, 0.92f);
		y += 40;
		string status = L10n.T(connected ? "Ready" : "Waiting for a controller");
		Vector2 statusSize = GtaArt.Measure(status, 0.78f);
		Rectangle pill = new Rectangle((int)(cx - (statusSize.X + 44f) / 2f), y, (int)statusSize.X + 44, 30);
		GtaArt.Fill(sb, pill, Color.Black * (0.45f * appear));
		Color dot = connected ? new Color(110, 235, 150) : GtaArt.Pink;
		float blink = connected ? 1f : (0.35f + 0.65f * (float)(0.5 + 0.5 * Math.Sin(Now * 4.0)));
		GameArt.Disc(sb, new Vector2(pill.X + 18, pill.Center.Y), 10f, 10f, dot * (blink * appear));
		GtaArt.Text(sb, status, new Vector2(pill.X + 30, pill.Y + 6), Color.White * (0.9f * appear), 0.78f);
		y += 52;
		int bw = Math.Min(300, column);
		Rectangle go = new Rectangle((int)(cx - bw / 2f), y, bw, 48);
		if (GtaArt.Button(sb, "gta:continue", go, "Continue", true, connected, GtaArt.Pad, appear))
		{
			_pending = () => Begin(true);
		}
		y += 60;
		Rectangle keys = new Rectangle((int)(cx - bw / 2f), y, bw, 44);
		if (GtaArt.Button(sb, "gta:keyboard", keys, "Play with keyboard", false, true, GtaArt.Keys, appear))
		{
			_pending = () => Begin(false);
		}
		y += 54;
		GtaArt.Centered(sb, L10n.T("No controller? You can set one up later in Settings."), cx, y, GtaArt.Soft * (0.7f * appear), 0.74f);
		Hint(sb, screen, L10n.T("Esc  Back"), appear * 0.8f);
	}

	private static void DrawLoading(SpriteBatch sb, Rectangle screen)
	{
		double t = Now - _stageAt;
		float appear = GameArt.Clamp01(t / 0.9);
		float zoom = 1f + 0.06f * GameArt.Clamp01(t / 14.0);
		GtaArt.CoverFit(sb, GtaArt.Cover, screen, Color.White * appear, zoom, new Vector2(-14f * GameArt.Clamp01(t / 14.0), 0f));
		int band = screen.Height * 2 / 5;
		GtaArt.VerticalShade(sb, new Rectangle(0, screen.Height - band, screen.Width, band), Color.Transparent, Color.Black * 0.88f);
		int margin = Math.Max(28, screen.Width / 28);
		int baseY = screen.Height - margin;
		GtaArt.Text(sb, GtaArt.Spaced("Grand Theft Auto VI"), new Vector2(margin, baseY - 64), GtaArt.Pink * appear, 0.8f);
		string tip = L10n.T(Tips[(int)(Now / 6.0) % Tips.Length]);
		float tipFade = (float)Math.Min(1.0, Math.Min(Now % 6.0 / 0.5, (6.0 - Now % 6.0) / 0.5));
		GtaArt.Text(sb, GtaArt.FitText(tip, screen.Width * 0.6f, 0.95f), new Vector2(margin, baseY - 36), Color.White * (appear * tipFade), 0.95f);
		if (GtaTrailer.Error != null && !GtaTrailer.Downloading)
		{
			DrawFailed(sb, screen, appear);
		}
		else
		{
			Vector2 spin = new Vector2(screen.Width - margin - 18, baseY - 22);
			GtaArt.Spinner(sb, spin, 15f, Color.White * appear);
			string what = GtaTrailer.Ready ? L10n.T("Loading") : (GtaTrailer.Downloading ? L10n.F("Installing  {0} / 212 GB", (GtaTrailer.Progress * 212f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)) : L10n.T("Getting ready"));
			Vector2 size = GtaArt.Measure(what, 0.86f);
			GtaArt.Text(sb, what, new Vector2(spin.X - 30 - size.X, spin.Y - size.Y / 2f + 3f), Color.White * (0.92f * appear), 0.86f);
			if (GtaTrailer.Downloading)
			{
				Rectangle bar = new Rectangle((int)(spin.X - 30 - 220), (int)spin.Y + 20, 220, 3);
				GtaArt.Fill(sb, bar, Color.White * (0.15f * appear));
				GtaArt.HorizontalShade(sb, new Rectangle(bar.X, bar.Y, (int)(bar.Width * GtaTrailer.Progress), bar.Height), GtaArt.Pink * appear, GtaArt.Orange * appear);
			}
		}
		if (_fadeAt >= 0.0)
		{
			GtaArt.Fill(sb, screen, Color.Black * GameArt.Clamp01((Now - _fadeAt) / 0.7));
		}
	}

	private static void DrawFailed(SpriteBatch sb, Rectangle screen, float appear)
	{
		int w = Math.Min(560, screen.Width - 60);
		Rectangle box = new Rectangle((screen.Width - w) / 2, screen.Height / 2 - 110, w, 200);
		GtaArt.Fill(sb, box, Color.Black * (0.78f * appear));
		GtaArt.Fill(sb, new Rectangle(box.X, box.Y, box.Width, 2), GtaArt.Pink * appear);
		GtaArt.Centered(sb, L10n.T("Couldn't install the game"), box.Center.X, box.Y + 24, Color.White * appear, 1.05f);
		GtaArt.Centered(sb, GtaArt.FitText(L10n.T(GtaTrailer.Error), w - 40, 0.78f), box.Center.X, box.Y + 62, GtaArt.Soft * appear, 0.78f);
		Rectangle retry = new Rectangle(box.X + 30, box.Bottom - 70, (w - 76) / 2, 44);
		Rectangle leave = new Rectangle(retry.Right + 16, retry.Y, retry.Width, 44);
		if (GtaArt.Button(sb, "gta:retry", retry, "Try again", true, true, null, appear))
		{
			GtaTrailer.Prepare();
		}
		if (GtaArt.Button(sb, "gta:leave", leave, "Back", false, true, null, appear))
		{
			_pending = Close;
		}
	}

	private static void DrawIntro(SpriteBatch sb, Rectangle screen)
	{
		double t = Now - _stageAt;
		Texture2D frame = GtaTrailer.Frame();
		if (frame != null)
		{
			GtaArt.CoverFit(sb, frame, screen, Color.White);
		}
		else
		{
			GtaArt.Spinner(sb, new Vector2(screen.Width - 46, screen.Height - 46), 15f, Color.White * 0.8f);
		}
		GtaArt.Fill(sb, screen, Color.Black * (1f - GameArt.Clamp01(t / 0.8)));
		float hint = GameArt.Clamp01((t - 1.0) / 0.6) * GameArt.Clamp01((5.0 - t) / 0.8);
		Hint(sb, screen, L10n.T(_withPad ? "B  Skip" : "Esc  Skip"), hint * 0.55f);
		if (_fadeAt >= 0.0)
		{
			GtaArt.Fill(sb, screen, Color.Black * GameArt.Clamp01((Now - _fadeAt) / 0.8));
		}
	}

	private static void DrawTitle(SpriteBatch sb, Rectangle screen)
	{
		double t = Now - _stageAt;
		float appear = GameArt.Clamp01(t / 1.2);
		_titleZoom = 1f + 0.05f * GameArt.Clamp01(t / 24.0);
		GtaArt.CoverFit(sb, GtaArt.KeyArt ?? GtaArt.Cover, screen, Color.White, _titleZoom);
		int band = screen.Height / 3;
		GtaArt.VerticalShade(sb, new Rectangle(0, screen.Height - band, screen.Width, band), Color.Transparent, Color.Black * 0.8f);
		float cx = screen.Width / 2f;
		int margin = Math.Max(28, screen.Width / 28);
		if (_startAt < 0.0)
		{
			float pulse = 0.55f + 0.45f * (float)(0.5 + 0.5 * Math.Sin(Now * 3.2));
			float ready = GameArt.Clamp01((t - 0.8) / 0.6);
			string press = L10n.T(_withPad ? "Press A to start" : "Press Enter to start");
			Vector2 size = GtaArt.Measure(press, 1.05f);
			Rectangle pill = new Rectangle((int)(cx - size.X / 2f - 34f), screen.Height - margin - 70, (int)size.X + 68, 46);
			GtaArt.Fill(sb, pill, Color.Black * (0.55f * ready));
			GtaArt.HorizontalShade(sb, new Rectangle(pill.X, pill.Bottom - 2, pill.Width, 2), GtaArt.Pink * ready, GtaArt.Orange * ready);
			GtaArt.Centered(sb, press, cx, pill.Y + 11, Color.White * (pulse * ready), 1.05f);
			if (ready >= 1f && MusicPlayerWindow.MouseIn(screen))
			{
				MusicDrawing.Hover("gta:start");
				if (MusicPlayerWindow.Clicked)
				{
					_pending = () => _startAt = Now;
				}
			}
			Hint(sb, screen, L10n.T(_withPad ? "B  Back" : "Esc  Back"), ready * 0.5f);
		}
		else
		{
			double s = Now - _startAt;
			float dim = GameArt.Clamp01(s / 0.6);
			GtaArt.Fill(sb, screen, Color.Black * (0.25f * dim));
			Vector2 spin = new Vector2(screen.Width - margin - 18, screen.Height - margin - 22);
			GtaArt.Spinner(sb, spin, 15f, Color.White * dim);
			string what = L10n.T("Loading Story Mode");
			Vector2 size = GtaArt.Measure(what, 0.86f);
			GtaArt.Text(sb, what, new Vector2(spin.X - 30 - size.X, spin.Y - size.Y / 2f + 3f), Color.White * (0.92f * dim), 0.86f);
		}
		GtaArt.Fill(sb, screen, Color.Black * (1f - appear));
	}

	private static void DrawOutro(SpriteBatch sb, Rectangle screen)
	{
		double t = Now - _stageAt;
		float blur = GameArt.EaseOut(GameArt.Clamp01(t / 1.4));
		if (blur < 1f)
		{
			GtaArt.CoverFit(sb, GtaArt.KeyArt ?? GtaArt.Cover, screen, Color.White, _titleZoom);
			GtaArt.Fill(sb, screen, Color.Black * 0.25f);
		}
		if (_blurred != null)
		{
			GtaArt.CoverFit(sb, _blurred, screen, Color.White * blur, _titleZoom + 0.03f);
		}
		GtaArt.Fill(sb, screen, Color.Black * (0.6f * blur));
		GtaArt.VerticalShade(sb, new Rectangle(0, screen.Height / 2, screen.Width, screen.Height / 2), Color.Transparent, new Color(60, 10, 40) * (0.45f * blur));
		float show = GameArt.EaseOut(GameArt.Clamp01((t - 1.0) / 0.9));
		if (show <= 0f)
		{
			return;
		}
		float rise = (1f - show) * 16f;
		float cx = screen.Width / 2f;
		int column = Math.Min(660, screen.Width - 60);
		int y = (int)(screen.Height / 2f - 230f + rise);
		GtaArt.Centered(sb, GtaArt.Spaced(L10n.T("System check")), cx, y, GtaArt.Pink * show, 0.74f);
		y += 30;
		string title = L10n.T("Your PC isn't powerful enough");
		float titleScale = Math.Min(1f, column / Math.Max(1f, GtaArt.Measure(title, 1f, true).X));
		GtaArt.Centered(sb, title, cx, y, Color.White * show, titleScale, true);
		y += (int)GtaArt.Measure(title, titleScale, true).Y + 4;
		GtaArt.Centered(sb, GtaArt.FitText(L10n.T("Grand Theft Auto VI needs a lot more than this computer can give."), column, 0.94f), cx, y, GtaArt.Soft * show, 0.94f);
		y += 46;
		Rectangle card = new Rectangle((int)(cx - column / 2f), y, column, 34 + 3 * 44);
		GtaArt.Fill(sb, card, Color.Black * (0.5f * show));
		GtaArt.HorizontalShade(sb, new Rectangle(card.X, card.Y, card.Width, 2), GtaArt.Pink * show, GtaArt.Orange * show);
		int labelX = card.X + 20;
		int mineX = card.X + (int)(column * 0.24f);
		int needX = card.X + (int)(column * 0.62f);
		int mineW = needX - mineX - 16;
		int needW = card.Right - needX - 16;
		GtaArt.Text(sb, L10n.T("Your PC"), new Vector2(mineX, card.Y + 10), GtaArt.Soft * (0.7f * show), 0.7f);
		GtaArt.Text(sb, L10n.T("Required"), new Vector2(needX, card.Y + 10), GtaArt.Soft * (0.7f * show), 0.7f);
		(string label, string mine, string need)[] rows =
		{
			(L10n.T("Graphics"), GtaArt.Gpu, "NVIDIA GeForce RTX 6090 Ti"),
			(L10n.T("Processor"), GtaArt.Cpu, "AMD Ryzen 9 11950X3D"),
			(L10n.T("Memory"), GtaArt.Memory, "128 GB")
		};
		for (int i = 0; i < rows.Length; i++)
		{
			int ry = card.Y + 34 + i * 44;
			float rowShow = show * GameArt.Clamp01((t - 1.3 - i * 0.18) / 0.4);
			GtaArt.Fill(sb, new Rectangle(card.X + 16, ry, card.Width - 32, 1), Color.White * (0.08f * show));
			GtaArt.Text(sb, rows[i].label, new Vector2(labelX, ry + 12), GtaArt.Soft * (0.8f * rowShow), 0.8f);
			Cross(sb, new Vector2(mineX + 6, ry + 22), new Color(255, 96, 110) * rowShow);
			GtaArt.Text(sb, GtaArt.FitText(rows[i].mine, mineW - 20, 0.82f), new Vector2(mineX + 20, ry + 12), new Color(255, 170, 178) * rowShow, 0.82f);
			GtaArt.Text(sb, GtaArt.FitText(rows[i].need, needW, 0.82f), new Vector2(needX, ry + 12), Color.White * rowShow, 0.82f);
		}
		y = card.Bottom + 30;
		float buttons = GameArt.Clamp01((t - 1.9) / 0.5);
		int bw = Math.Min(240, (column - 16) / 2);
		Rectangle home = new Rectangle((int)(cx - bw - 8), y, bw, 48);
		Rectangle again = new Rectangle((int)(cx + 8), y, bw, 48);
		if (GtaArt.Button(sb, "gta:home", home, "Back to Terraria", true, true, null, buttons))
		{
			_pending = Close;
			return;
		}
		if (GtaArt.Button(sb, "gta:again", again, "Watch again", false, true, null, buttons))
		{
			_pending = Replay;
		}
		y += 66;
		GtaArt.Centered(sb, L10n.T("Terraria runs great on it, though."), cx, y, GtaArt.Soft * (0.6f * buttons), 0.78f);
		if (_withPad)
		{
			Hint(sb, screen, L10n.T("A  Back to Terraria     X  Watch again"), buttons * 0.7f);
		}
	}

	private static void Cross(SpriteBatch sb, Vector2 center, Color color)
	{
		const float r = 5f;
		GameArt.Line(sb, center + new Vector2(-r, -r), center + new Vector2(r, r), 2f, color);
		GameArt.Line(sb, center + new Vector2(-r, r), center + new Vector2(r, -r), 2f, color);
	}

	private static void Hint(SpriteBatch sb, Rectangle screen, string text, float alpha)
	{
		if (alpha <= 0f)
		{
			return;
		}
		Vector2 size = GtaArt.Measure(text, 0.78f);
		GtaArt.Text(sb, text, new Vector2(screen.Width - 30 - size.X, screen.Height - 30 - size.Y), Color.White * alpha, 0.78f);
	}
}
