using System;
using System.Collections.Generic;
using System.IO;
using IsaClient.Core;
using Newtonsoft.Json;
using IsaClient.Games;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI.Chat;

namespace IsaClient.Networking;

/// <summary>How a server card looks (its accent color and default icon).</summary>
public enum PopupStyle : byte
{
	Info = 0,
	Success = 1,
	Warning = 2,
	Danger = 3
}

/// <summary>
/// Cards from the server: what its IsaClient mode means for you (<see cref="ServerModes"/>), and messages the server's
/// plugins send with the IsaClientModes API. One card at a time at the top of the screen (the next ones wait), with
/// the server's name, an item icon, a title and the text (chat tags like [c/ff0:...] and [i:29] work, "- " lines are
/// bullets). It can be dragged by its top, closes with OK or by itself after the server's time.
/// </summary>
public static class ServerPopups
{
	private sealed class Card
	{
		public string Title;

		public string Body;

		public PopupStyle Style;

		public int Icon;

		/// <summary>0 = until closed.</summary>
		public int Seconds;

		public double ShownAt = -1.0;

		/// <summary>What "Don't show again" remembers for this server (the card's text).</summary>
		public string Key;
	}

	#region Servers you have seen

	private sealed class SeenServer
	{
		/// <summary>The mode card already shown here (mode and the server's message).</summary>
		[JsonProperty("modeCard")]
		public string ModeCard;

		/// <summary>Cards you asked not to see again on this server.</summary>
		[JsonProperty("hidden")]
		public List<string> Hidden = new List<string>();
	}

	private static Dictionary<string, SeenServer> _servers;

	private static string ServersPath => Path.Combine(ClientSettings.IsaClientFolder, "ServerCards.json");

	/// <summary>This server: its address and port (the same server under another name counts as another).</summary>
	private static string ServerKey => ((Netplay.ServerIPText ?? "").Trim().ToLowerInvariant()) + ":" + Netplay.ListenPort;

	private static SeenServer Current
	{
		get
		{
			if (_servers == null)
			{
				try
				{
					_servers = File.Exists(ServersPath) ? JsonConvert.DeserializeObject<Dictionary<string, SeenServer>>(File.ReadAllText(ServersPath)) : null;
				}
				catch (Exception ex)
				{
					ClientLog.LogException(ex);
				}
				_servers ??= new Dictionary<string, SeenServer>(StringComparer.OrdinalIgnoreCase);
			}
			if (!_servers.TryGetValue(ServerKey, out SeenServer seen))
			{
				seen = new SeenServer();
				_servers[ServerKey] = seen;
			}
			seen.Hidden ??= new List<string>();
			return seen;
		}
	}

	private static void SaveServers()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(ServersPath));
			File.WriteAllText(ServersPath, JsonConvert.SerializeObject(_servers, Formatting.Indented));
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	/// <summary>A short stable fingerprint of a card's text.</summary>
	private static string Fingerprint(string text)
	{
		uint hash = 2166136261;
		foreach (char c in text ?? "")
		{
			hash = (hash ^ c) * 16777619;
		}
		return hash.ToString("x8");
	}

	/// <summary>
	/// The mode card of this server: true when it should be shown (first time here, or the mode or the server's message
	/// changed). Remembered right away, so coming back to the same server doesn't show it again.
	/// </summary>
	public static bool FirstModeCard(string what)
	{
		if (Main.netMode != 1)
		{
			return true;
		}
		SeenServer seen = Current;
		string key = Fingerprint(what);
		if (seen.ModeCard == key)
		{
			return false;
		}
		seen.ModeCard = key;
		SaveServers();
		return true;
	}

	private static bool IsHidden(string key)
	{
		return Main.netMode == 1 && Current.Hidden.Contains(key);
	}

	private static void Hide(string key)
	{
		if (Main.netMode != 1 || string.IsNullOrEmpty(key))
		{
			return;
		}
		SeenServer seen = Current;
		if (!seen.Hidden.Contains(key))
		{
			seen.Hidden.Add(key);
			SaveServers();
		}
	}

	#endregion

	public const int TitleLimit = 80;

	public const int BodyLimit = 700;

	private const int Width = 460;

	private const int MaxWaiting = 6;

	private static readonly Queue<Card> _waiting = new Queue<Card>();

	private static Card _card;

	private static readonly GameOverlay.Panel _panel = new GameOverlay.Panel { Layer = "server:popup" };

	private static bool _registered;

	private static int _height = 140;

	/// <summary>Puts a card on screen (or in line after the one shown). Text is shown as given (it's already the server's language).</summary>
	public static void Show(string title, string body, PopupStyle style = PopupStyle.Info, int icon = 0, int seconds = 0)
	{
		if (Main.dedServ)
		{
			return;
		}
		Ensure();
		title = Clip(title, TitleLimit);
		body = Clip((body ?? "").Replace("\r", ""), BodyLimit);
		if (title.Length == 0 && body.Length == 0)
		{
			return;
		}
		string key = Fingerprint(title + "\n" + body);
		if (IsHidden(key))
		{
			return;
		}
		while (_waiting.Count >= MaxWaiting)
		{
			_waiting.Dequeue();
		}
		_waiting.Enqueue(new Card
		{
			Title = title,
			Body = body,
			Style = style,
			Icon = (icon > 0 && icon < ItemID.Count) ? icon : DefaultIcon(style),
			Seconds = Math.Max(0, Math.Min(600, seconds)),
			Key = key
		});
	}

	/// <summary>Leaving the server: its cards go too.</summary>
	public static void Clear()
	{
		_waiting.Clear();
		_card = null;
	}

	private static string Clip(string text, int max)
	{
		text = (text ?? "").Trim();
		return (text.Length > max) ? text.Substring(0, max) : text;
	}

	private static int DefaultIcon(PopupStyle style)
	{
		return style switch
		{
			PopupStyle.Success => ItemID.LifeCrystal,
			PopupStyle.Warning => ItemID.ChestLock,
			PopupStyle.Danger => ItemID.ChestLock,
			_ => ItemID.Sign
		};
	}

	private static Color Accent(PopupStyle style)
	{
		return style switch
		{
			PopupStyle.Success => new Color(110, 220, 130),
			PopupStyle.Warning => new Color(255, 196, 80),
			PopupStyle.Danger => new Color(255, 105, 105),
			_ => new Color(110, 180, 255)
		};
	}

	private static void Ensure()
	{
		if (_registered)
		{
			return;
		}
		_registered = true;
		MusicPlayerWindow.AddSurface(new MusicPlayerWindow.FloatingSurface
		{
			Layer = _panel.Layer,
			Shown = () => !Main.gameMenu && Next() != null,
			ScreenRect = () => _panel.ScreenRect,
			Draw = Draw
		});
	}

	/// <summary>The card shown now (the next one in line comes up when it closes).</summary>
	private static Card Next()
	{
		if (_card == null && _waiting.Count > 0)
		{
			_card = _waiting.Dequeue();
		}
		return _card;
	}

	/// <summary>Test harness: "Don't show again" on the card shown now.</summary>
	internal static void TestNever()
	{
		if (Next() is Card card)
		{
			Hide(card.Key);
			Close();
		}
	}

	internal static void Close()
	{
		_card = null;
		SoundEngine.PlaySound(12);
	}

	/// <summary>Test harness: draws the card shown now (with its slide already done).</summary>
	internal static void TestDraw(SpriteBatch sb, int screenW, int screenH)
	{
		Card card = Next();
		if (card != null && card.ShownAt < 0.0)
		{
			card.ShownAt = GameArt.Now - 1.0;
		}
		_panel.Position = null;
		Draw(sb, screenW, screenH);
		_panel.Position = null;
	}

	private static void Draw(SpriteBatch sb, int screenW, int screenH)
	{
		Card card = Next();
		if (card == null)
		{
			return;
		}
		if (card.ShownAt < 0.0)
		{
			card.ShownAt = GameArt.Now;
			SoundEngine.PlaySound(10);
			MusicPlayerWindow.BringSurfaceToFront(_panel.Layer);
		}
		double age = GameArt.Now - card.ShownAt;
		if (card.Seconds > 0 && age > card.Seconds)
		{
			_card = null;
			return;
		}
		DynamicSpriteFont font = FontAssets.MouseText.Value;
		int width = Math.Min(Width, screenW - 20);
		if (_panel.Position == null)
		{
			_panel.Position = new Vector2((screenW - width) / 2f, 86f);
		}
		Rectangle r = GameOverlay.Place(_panel, width, _height, screenW, screenH, 0);
		// slides down a little when it appears
		float ease = (float)Math.Min(1.0, age / 0.25);
		r.Y -= (int)((1f - ease) * 14f);
		Color accent = Accent(card.Style);
		GameOverlay.Window(sb, r, 34);
		MusicDrawing.Fill(sb, new Rectangle(r.X + 4, r.Y + 10, 3, r.Height - 20), accent * (0.7f + 0.3f * GameArt.Pulse(2.0)));
		// header: the server, an icon and the title
		Rectangle icon = new Rectangle(r.X + 16, r.Y + 14, 44, 44);
		MusicDrawing.Box(sb, icon, MusicPlayerWindow.PanelDarkColor * 0.9f);
		DrawItem(sb, card.Icon, new Rectangle(icon.X + 6, icon.Y + 6, 32, 32));
		int tx = icon.Right + 12;
		int textW = r.Right - 16 - tx;
		string server = string.IsNullOrWhiteSpace(Main.worldName) ? IsaClient.Languages.L10n.T("This server") : Main.worldName;
		MusicDrawing.RawText(sb, server.ToUpperInvariant(), tx, r.Y + 12, MusicPlayerWindow.MutedColor, 0.6f, textW - 30);
		MusicDrawing.RawText(sb, card.Title, tx, r.Y + 29, accent, 0.92f, textW - 10);
		if (MusicDrawing.TextButton(sb, "server:popup:x", new Rectangle(r.Right - 34, r.Y + 10, 24, 22), "x", MusicPlayerWindow.PanelColor, "Close"))
		{
			Close();
			return;
		}
		// the text: each line wraps; "- " lines are bullets
		float y = r.Y + 68;
		float scale = 0.82f;
		int bodyLeft = r.X + 20;
		int bodyW = r.Width - 40;
		foreach (string raw in card.Body.Split('\n'))
		{
			string line = raw.TrimEnd();
			if (line.Length == 0)
			{
				y += 8f;
				continue;
			}
			bool bullet = line.StartsWith("- ") || line.StartsWith("* ");
			float x = bodyLeft;
			if (bullet)
			{
				line = line.Substring(2);
				MusicDrawing.Fill(sb, new Rectangle(bodyLeft + 3, (int)y + 8, 5, 5), accent);
				x += 16f;
			}
			float room = bodyLeft + bodyW - x;
			Vector2 size = ChatManager.GetStringSize(font, line, new Vector2(scale), room);
			ChatManager.DrawColorCodedStringWithShadow(sb, font, line, new Vector2(x, y), new Color(226, 230, 242), 0f, Vector2.Zero, new Vector2(scale), room, 1.5f);
			y += Math.Max(20f, size.Y) + 2f;
		}
		y += 8f;
		// OK, Don't show again (this card on this server), Disconnect
		Rectangle ok = new Rectangle(r.Right - 100, (int)y, 84, 28);
		Rectangle never = new Rectangle(ok.X - 156, (int)y, 148, 28);
		Rectangle leave = new Rectangle(never.X - 118, (int)y, 110, 28);
		if (MusicDrawing.TextButton(sb, "server:popup:ok", ok, "OK", new Color(60, 120, 80), (_waiting.Count > 0) ? IsaClient.Languages.L10n.F("{0} more", _waiting.Count) : null))
		{
			Close();
			return;
		}
		if (MusicDrawing.TextButton(sb, "server:popup:never", never, "Don't show again", MusicPlayerWindow.PanelColor, "This card won't show again on this server"))
		{
			Hide(card.Key);
			Close();
			return;
		}
		if (Main.netMode == 1 && MusicDrawing.TextButton(sb, "server:popup:leave", leave, "Disconnect", new Color(120, 50, 60), "Leave this server (your character is saved, as with Save & Exit)"))
		{
			Clear();
			SoundEngine.PlaySound(11);
			WorldGen.SaveAndQuit();
			return;
		}
		if (_waiting.Count > 0)
		{
			MusicDrawing.RawText(sb, IsaClient.Languages.L10n.F("{0} more", _waiting.Count), r.X + 18, ok.Y + 7, MusicPlayerWindow.MutedColor, 0.6f, leave.X - r.X - 24);
		}
		_height = (int)(ok.Bottom - r.Y) + 14;
		if (card.Seconds > 0)
		{
			float left = MathHelper.Clamp((float)(1.0 - age / card.Seconds), 0f, 1f);
			MusicDrawing.Fill(sb, new Rectangle(r.X + 10, r.Bottom - 6, r.Width - 20, 2), MusicPlayerWindow.PanelDarkColor);
			MusicDrawing.Fill(sb, new Rectangle(r.X + 10, r.Bottom - 6, (int)((r.Width - 20) * left), 2), accent * 0.8f);
		}
		GameOverlay.Drag(_panel, new Rectangle(r.X, r.Y, r.Width - 40, 26), screenW, screenH);
	}

	private static void DrawItem(SpriteBatch sb, int type, Rectangle box)
	{
		try
		{
			Main.instance.LoadItem(type);
			Texture2D texture = TextureAssets.Item[type].Value;
			Rectangle frame = (Main.itemAnimations[type] != null) ? Main.itemAnimations[type].GetFrame(texture) : texture.Frame();
			float k = Math.Min(1.5f, Math.Min((float)box.Width / frame.Width, (float)box.Height / frame.Height));
			Vector2 size = new Vector2(frame.Width, frame.Height) * k;
			sb.Draw(texture, new Vector2(box.Center.X - size.X / 2f, box.Center.Y - size.Y / 2f), frame, Color.White, 0f, Vector2.Zero, k, SpriteEffects.None, 0f);
		}
		catch
		{
		}
	}
}
