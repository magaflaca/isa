using System;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Chat;
using Terraria.UI.Chat;

namespace IsaClient.Assistant;

/// <summary>
/// The working line of .ask at the bottom of the chat, like Claude Code's: a pulsing ✻ and a made-up verb
/// ("✻ Spelunking..."), in one plain color. It is one chat message that keeps moving to the bottom: a new verb every few
/// seconds, the gray detail lines (searches, pages read) go above it, and it disappears with the answer.
/// </summary>
internal static class AskStatus
{
	private static readonly string[] Verbs =
	{
		"Vibing", "Pondering", "Noodling", "Cogitating", "Spelunking", "Conjuring", "Brewing", "Percolating", "Tinkering",
		"Moseying", "Wrangling", "Rummaging", "Mulling", "Simmering", "Sleuthing", "Unravelling", "Smelting", "status::Crafting",
		"Enchanting", "Shimmering", "Reforging", "Excavating", "Discombobulating", "Flibbertigibbeting", "Bamboozling",
		"Hatching", "Marinating", "Schlepping", "Finagling", "Lollygagging", "Puttering", "Befuddling", "Galumphing",
		"Mining", "Summoning", "Divining", "Whittling", "Pixelating", "Boulderizing", "Slime-wrangling"
	};

	private static readonly Random _random = new Random();

	private static ChatMessageContainer _line;

	private static Timer _timer;

	private static bool _active;

	private static bool _registered;

	private static string _verb = "Vibing";

	private static DateTime _started = DateTime.Now;

	private static int _tokens;

	/// <summary>Tokens the models wrote since the line appeared (shown after it: "(4m 34s · ↓ 2.8k tokens)").</summary>
	public static void AddTokens(int count)
	{
		if (count > 0)
		{
			Interlocked.Add(ref _tokens, count);
		}
	}

	/// <summary>"4m 34s · ↓ 2.8k tokens" (no tokens part while there are none).</summary>
	public static string Stats(TimeSpan elapsed, int tokens)
	{
		string time = (elapsed.TotalSeconds < 60) ? ((int)elapsed.TotalSeconds + "s") : ((int)elapsed.TotalMinutes + "m " + elapsed.Seconds + "s");
		if (tokens <= 0)
		{
			return time;
		}
		string amount = (tokens >= 1000) ? ((tokens / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "k") : tokens.ToString();
		string arrow = FontAssets.MouseText.Value.IsCharacterSupported('\u2193') ? "\u2193 " : "";
		return time + " \u00b7 " + arrow + amount + " tokens";
	}

	/// <summary>Shows the line (game thread) and changes its verb every few seconds until <see cref="Stop"/>.</summary>
	public static void Start()
	{
		Register();
		_active = true;
		_started = DateTime.Now;
		_tokens = 0;
		_verb = Verbs[_random.Next(Verbs.Length)];
		Place();
		_timer?.Dispose();
		_timer = new Timer(delegate
		{
			Main.QueueMainThreadAction(delegate
			{
				if (_active)
				{
					_verb = Next();
					Place();
				}
			});
		}, null, 2600, 2600);
	}

	/// <summary>A gray detail line above the working line (game thread).</summary>
	public static void Detail(string text)
	{
		Take();
		Color muted = AskAssistant.MutedColor;
		Main.NewText(IsaClient.Languages.L10n.T(text), muted.R, muted.G, muted.B);
		if (_active)
		{
			Place();
		}
	}

	public static void Stop()
	{
		_active = false;
		_timer?.Dispose();
		_timer = null;
		Take();
	}

	private static string Next()
	{
		string verb;
		do
		{
			verb = Verbs[_random.Next(Verbs.Length)];
		}
		while (verb == _verb);
		return verb;
	}

	/// <summary>The line at the bottom of the chat (taking the old one out first).</summary>
	private static void Place()
	{
		Take();
		if (Main.gameMenu || !(Main.chatMonitor is RemadeChatMonitor monitor))
		{
			return;
		}
		Color color = AskAssistant.StatusColor;
		Main.NewText(IsaClient.Languages.L10n.T("[isawork/" + color.R + "," + color.G + "," + color.B + ":" + IsaClient.Languages.L10n.T(_verb) + "...]"));
		_line = monitor.Newest;
	}

	private static void Take()
	{
		if (_line != null && Main.chatMonitor is RemadeChatMonitor monitor)
		{
			monitor.Remove(_line);
		}
		_line = null;
	}

	private static void Register()
	{
		if (!_registered)
		{
			_registered = true;
			ChatManager.Register<WorkTagHandler>("isawork");
		}
	}

	private sealed class WorkTagHandler : ITagHandler
	{
		public TextSnippet Parse(string text, Color baseColor, string options)
		{
			string[] rgb = (options ?? "").Split(',');
			Color color = (rgb.Length == 3 && byte.TryParse(rgb[0], out byte r) && byte.TryParse(rgb[1], out byte g) && byte.TryParse(rgb[2], out byte b)) ? new Color(r, g, b) : baseColor;
			return new WorkSnippet(text, color);
		}
	}

	/// <summary>"✻ Verb...": the star grows and shrinks and turns slowly; the font has no ✻, so it is drawn with strokes.</summary>
	private sealed class WorkSnippet : TextSnippet
	{
		private const float IconWidth = 22f;

		public WorkSnippet(string text, Color color)
			: base(text, color)
		{
			CheckForHover = true;
		}

		public override void OnHover()
		{
			Main.instance.MouseText(IsaClient.Languages.L10n.T("Play Tetris while you wait"), 0, 0);
		}

		/// <summary>A click on the working line: Tetris to pass the time.</summary>
		public override void OnClick()
		{
			Main.mouseLeftRelease = false;
			IsaClient.Games.Tetris.Open();
		}

		public override bool UniqueDraw(bool justCheckingSize, out Vector2 size, SpriteBatch spriteBatch, Vector2 position = default(Vector2), Color color = default(Color), float scale = 1f)
		{
			DynamicSpriteFont font = FontAssets.MouseText.Value;
			Vector2 textSize = font.MeasureString(IsaClient.Languages.L10n.T(Text)) * scale;
			string stats = _active ? ("  (" + Stats(DateTime.Now - _started, _tokens) + ")") : "";
			float statsWidth = font.MeasureString(stats).X * scale * 0.85f;
			size = new Vector2(IconWidth * scale + textSize.X + statsWidth, textSize.Y);
			if (justCheckingSize)
			{
				return true;
			}
			bool shadow = color.R < 20 && color.G < 20 && color.B < 20;
			Color ink = shadow ? color : Color;
			double time = Main.timeForVisualEffects;
			float pulse = 0.72f + 0.28f * (float)Math.Sin(time * 0.11);
			Vector2 center = position + new Vector2(9f * scale, textSize.Y * 0.42f);
			float radius = 7f * scale * pulse;
			float turn = (float)(time * 0.02);
			for (int i = 0; i < 4; i++)
			{
				float angle = turn + i * MathHelper.PiOver4;
				spriteBatch.Draw(TextureAssets.MagicPixel.Value, center, new Rectangle(0, 0, 1, 1), ink, angle, new Vector2(0.5f, 0.5f), new Vector2(radius * 2f, Math.Max(1.5f, 2.4f * scale * pulse)), SpriteEffects.None, 0f);
			}
			spriteBatch.DrawString(font, IsaClient.Languages.L10n.T(Text), position + new Vector2(IconWidth * scale, 0f), ink, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
			if (stats.Length > 0)
			{
				Color gray = shadow ? color : new Color(150, 150, 160);
				spriteBatch.DrawString(font, stats, position + new Vector2(IconWidth * scale + textSize.X, textSize.Y * 0.08f), gray, 0f, Vector2.Zero, scale * 0.85f, SpriteEffects.None, 0f);
			}
			return true;
		}
	}
}
