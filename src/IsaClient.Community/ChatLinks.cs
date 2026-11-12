using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.UI.Chat;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Community;

/// <summary>
/// Clickable links in the chat: http(s):// and www. addresses are drawn underlined in a link color; hovering fades in a
/// soft selection box and shows the address; clicking (with the chat open) opens it in your browser. The local tag
/// [isalink/escaped-address:text] draws any text as a link (the citations of .ask). Video links (YouTube, TikTok...)
/// open in the client's video player instead; Shift+click opens them in the browser.
/// </summary>
public static class ChatLinks
{
	/// <summary>True for a link the video player plays (set by IsaClient.Video).</summary>
	public static Predicate<string> IsVideoLink;

	/// <summary>Plays a video link in the client (set by IsaClient.Video).</summary>
	public static Action<string> PlayVideo;

	private static bool _tagRegistered;

	/// <summary>[isalink/https%3A%2F%2F...:(1)]: <paramref name="text"/> that opens <paramref name="url"/>. Only for messages shown locally.</summary>
	public static string Tag(string url, string text)
	{
		RegisterTag();
		return "[isalink/" + Uri.EscapeDataString(url) + ":" + text.Replace("]", "\\]") + "]";
	}

	public static void RegisterTag()
	{
		if (!_tagRegistered)
		{
			_tagRegistered = true;
			ChatManager.Register<LinkTagHandler>("isalink");
		}
	}

	private sealed class LinkTagHandler : ITagHandler
	{
		public TextSnippet Parse(string text, Color baseColor, string options)
		{
			string url;
			try
			{
				url = Uri.UnescapeDataString(options ?? "");
			}
			catch
			{
				url = options ?? "";
			}
			return new LinkSnippet(url, text);
		}
	}

	private static readonly Regex _urlPattern = new Regex("\\b(?:https?://|www\\.)[^\\s<>\"\\[\\]]+[^\\s<>\"\\[\\].,;:!?)]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Color _linkColor = new Color(110, 185, 255);

	/// <summary>Splits the plain text pieces of each wrapped chat line around the links in them.</summary>
	public static void Linkify(List<List<TextSnippet>> lines)
	{
		foreach (List<TextSnippet> line in lines)
		{
			for (int i = 0; i < line.Count; i++)
			{
				TextSnippet snippet = line[i];
				if (snippet.GetType() != typeof(TextSnippet) || string.IsNullOrEmpty(snippet.Text) || snippet.Text.IndexOf('.') < 0)
				{
					continue;
				}
				MatchCollection matches = _urlPattern.Matches(snippet.Text);
				if (matches.Count == 0)
				{
					continue;
				}
				List<TextSnippet> parts = new List<TextSnippet>();
				int at = 0;
				foreach (Match m in matches)
				{
					if (m.Index > at)
					{
						parts.Add(snippet.CopyMorph(snippet.Text.Substring(at, m.Index - at)));
					}
					parts.Add(new LinkSnippet(m.Value));
					at = m.Index + m.Length;
				}
				if (at < snippet.Text.Length)
				{
					parts.Add(snippet.CopyMorph(snippet.Text.Substring(at)));
				}
				line.RemoveAt(i);
				line.InsertRange(i, parts);
				i += parts.Count - 1;
			}
		}
	}

	public static void Open(string url)
	{
		if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			url = "https://" + url;
		}
		try
		{
			Process.Start(new ProcessStartInfo(url)
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			ClientChat.Error("Could not open the link: " + ex.Message);
		}
	}

	/// <summary>A link inside a chat line.</summary>
	public sealed class LinkSnippet : TextSnippet
	{
		private double _lastHover = -100.0;

		private float _glow;

		private readonly string _url;

		public string Url => _url;

		/// <summary>The address is what it shows (not a [isalink] citation).</summary>
		public bool ShowsAddress => Text == _url;

		/// <summary>A link that shows <paramref name="label"/> (the address itself when null).</summary>
		public LinkSnippet(string url, string label = null)
			: base(label ?? url, _linkColor)
		{
			_url = url;
			CheckForHover = true;
		}

		public override void OnHover()
		{
			_lastHover = Main.timeForVisualEffects;
			string click = WatchesHere ? "Click to watch it here (Shift+click: open it in your browser)" : "Click to open";
			Main.instance.MouseText(Main.drawingPlayerChat ? (_url + "\n" + click) : (_url + "\nOpen the chat to click links"), 0, 0);
		}

		public override void OnClick()
		{
			if (!Main.drawingPlayerChat)
			{
				return;
			}
			Main.mouseLeftRelease = false;
			SoundEngine.PlaySound(12);
			if (WatchesHere && !Main.keyState.PressingShift())
			{
				PlayVideo(_url);
				return;
			}
			Open(_url);
		}

		private bool WatchesHere => PlayVideo != null && IsVideoLink != null && IsVideoLink(_url);

		public override Color GetVisibleColor()
		{
			return Color;
		}

		public override bool UniqueDraw(bool justCheckingSize, out Vector2 size, SpriteBatch spriteBatch, Vector2 position = default(Vector2), Color color = default(Color), float scale = 1f)
		{
			DynamicSpriteFont font = FontAssets.MouseText.Value;
			size = font.MeasureString(Text) * scale;
			if (justCheckingSize)
			{
				return true;
			}
			bool shadow = color.R < 20 && color.G < 20 && color.B < 20;
			if (!shadow)
			{
				bool hovered = Main.timeForVisualEffects - _lastHover <= 2.0;
				_glow = MathHelper.Clamp(_glow + (hovered ? 0.15f : -0.1f), 0f, 1f);
				if (_glow > 0f)
				{
					// Soft selection box, slowly breathing while hovered.
					float pulse = 0.85f + 0.15f * (float)Math.Sin(Main.timeForVisualEffects * 0.12);
					Rectangle box = new Rectangle((int)position.X - 2, (int)(position.Y + 1f * scale), (int)size.X + 4, (int)(size.Y * 0.78f));
					spriteBatch.Draw(TextureAssets.MagicPixel.Value, box, new Color(110, 185, 255) * (0.22f * _glow * pulse));
				}
				color = Color.Lerp(_linkColor, Color.White, _glow * 0.6f);
			}
			spriteBatch.DrawString(font, Text, position, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
			float underlineY = position.Y + size.Y * 0.72f;
			spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle((int)position.X, (int)underlineY, (int)size.X, Math.Max(1, (int)(1.5f * scale))), color);
			return true;
		}
	}
}
