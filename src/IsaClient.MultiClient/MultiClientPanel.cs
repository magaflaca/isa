using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.UI;
using Terraria.UI.Chat;
using Terraria;

namespace IsaClient.MultiClient;

/// <summary>
/// The Bots page of the IsaClient panel. Drawn in immediate mode so every row is laid out from measured sizes: text is
/// truncated to the room left by its buttons, nothing overflows, and the content area scrolls with its own clipping.
/// Views: Sessions (live list), New session (character + server + count), Saved (profiles), Options.
/// </summary>
public class MultiClientPanel : UIElement
{
	private enum View
	{
		Sessions,
		New,
		Saved,
		Options
	}

	private static readonly string[] _viewNames = new string[4] { "Sessions", "New session", "Saved", "Options" };

	private static readonly Color _cardColor = new Color(44, 57, 105) * 0.92f;

	private static readonly Color _cardSelected = new Color(70, 110, 170) * 0.95f;

	private static readonly Color _green = new Color(56, 128, 84);

	private static readonly Color _blue = new Color(63, 82, 151);

	private static readonly Color _red = new Color(150, 58, 58);

	private static readonly Color _gold = new Color(150, 120, 50);

	private static readonly Color _muted = new Color(154, 167, 190);

	private static readonly Color _accent = new Color(255, 231, 69);

	private const float Pad = 8f;

	private const float HeaderHeight = 38f;

	private View _view;

	private float _scroll;

	private float _contentHeight;

	private bool _draggingThumb;

	private float _dragOffset;

	// Hit testing state for the frame being drawn.
	private Vector2 _mouse;

	private Rectangle _contentRect;

	private bool _clicked;

	private string _hoverId;

	private string _lastHoverId;

	// Form state (New session).
	private int _character = -1;

	private int _server;

	private string _customHost = "";

	private string _customPort = "7777";

	private string _password = "";

	private string _name = "";

	private int _count = 1;

	private string _customUuid = "";

	private string _focus;

	private uint _lastDrawFrame;

	private static DynamicSpriteFont Font => FontAssets.MouseText.Value;

	public MultiClientPanel()
	{
		Width.Set(0f, 1f);
		Height.Set(-50f, 1f);
		VAlign = 1f;
	}

	public override void ScrollWheel(UIScrollWheelEvent evt)
	{
		base.ScrollWheel(evt);
		_scroll -= evt.ScrollWheelValue / 3f;
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		if (_focus != null)
		{
			if (Main.GameUpdateCount - _lastDrawFrame > 2)
			{
				_focus = null; // page not visible any more
			}
			else
			{
				PlayerInput.WritingText = true;
				Main.CurrentInputTextTakerOverride = this;
			}
		}
		if (IsMouseHovering)
		{
			Main.LocalPlayer.mouseInterface = true;
		}
	}

	#region Frame

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		_lastDrawFrame = Main.GameUpdateCount;
		CalculatedStyle d = GetDimensions();
		Rectangle area = new Rectangle((int)d.X, (int)d.Y, (int)d.Width, (int)d.Height);
		_mouse = Main.MouseScreen;
		_clicked = Main.mouseLeft && Main.mouseLeftRelease && area.Contains(_mouse.ToPoint());
		_hoverId = null;
		bool clickedSomething = false;

		// Sub tabs (fixed, not scrolled).
		float tabW = (area.Width - Pad * 2f - 3 * 6f) / 4f;
		for (int i = 0; i < 4; i++)
		{
			Rectangle r = new Rectangle((int)(area.X + Pad + i * (tabW + 6f)), area.Y + 4, (int)tabW, 30);
			bool selected = (int)_view == i;
			string label = _viewNames[i];
			if (i == 0)
			{
				label += " (" + MultiClientManager.Count + ")";
			}
			if (Button("tab" + i, r, label, selected ? new Color(90, 115, 190) : _blue * 0.75f, clip: false, bold: selected))
			{
				if (_view != (View)i)
				{
					_view = (View)i;
					_scroll = 0f;
					_focus = null;
					if (_view == View.New)
					{
						MultiClientManager.RefreshCharacters();
					}
				}
				clickedSomething = true;
			}
		}

		// Scrolling content.
		_contentRect = new Rectangle(area.X, (int)(area.Y + HeaderHeight + 4f), area.Width - 14, (int)(area.Height - HeaderHeight - 8f));
		float maxScroll = Math.Max(0f, _contentHeight - _contentRect.Height);
		_scroll = MathHelper.Clamp(_scroll, 0f, maxScroll);
		RasterizerState oldRaster = spriteBatch.GraphicsDevice.RasterizerState;
		Rectangle oldScissor = spriteBatch.GraphicsDevice.ScissorRectangle;
		spriteBatch.End();
		spriteBatch.GraphicsDevice.ScissorRectangle = ToScissor(_contentRect, oldScissor);
		RasterizerState clipRaster = new RasterizerState
		{
			CullMode = CullMode.None,
			ScissorTestEnable = true
		};
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, clipRaster, null, Main.UIScaleMatrix);
		float y = _contentRect.Y - _scroll;
		float top = y;
		switch (_view)
		{
		case View.Sessions:
			y = DrawSessions(spriteBatch, y);
			break;
		case View.New:
			y = DrawNew(spriteBatch, y);
			break;
		case View.Saved:
			y = DrawSaved(spriteBatch, y);
			break;
		case View.Options:
			y = DrawOptions(spriteBatch, y);
			break;
		}
		_contentHeight = y - top + Pad;
		spriteBatch.End();
		spriteBatch.GraphicsDevice.ScissorRectangle = oldScissor;
		spriteBatch.GraphicsDevice.RasterizerState = oldRaster;
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, oldRaster, null, Main.UIScaleMatrix);

		DrawScrollbar(spriteBatch, new Rectangle(area.Right - 10, _contentRect.Y, 8, _contentRect.Height));

		if (_clicked && _hoverId != null)
		{
			clickedSomething = true;
		}
		if (_clicked && !clickedSomething && _focus != null && !(_hoverId ?? "").StartsWith("field:"))
		{
			_focus = null;
		}
		if (_hoverId != null && _hoverId != _lastHoverId)
		{
			SoundEngine.PlaySound(12);
		}
		_lastHoverId = _hoverId;
		if (_clicked && (clickedSomething || _hoverId != null))
		{
			Main.mouseLeftRelease = false;
		}
	}

	private static Rectangle ToScissor(Rectangle ui, Rectangle current)
	{
		Vector2 a = Vector2.Transform(new Vector2(ui.X, ui.Y), Main.UIScaleMatrix);
		Vector2 b = Vector2.Transform(new Vector2(ui.Right, ui.Bottom), Main.UIScaleMatrix);
		Rectangle r = new Rectangle((int)a.X, (int)a.Y, (int)(b.X - a.X), (int)(b.Y - a.Y));
		Rectangle clipped = Rectangle.Intersect(r, current);
		return (clipped.Width > 0 && clipped.Height > 0) ? clipped : new Rectangle(0, 0, 1, 1);
	}

	private void DrawScrollbar(SpriteBatch sb, Rectangle track)
	{
		if (_contentHeight <= _contentRect.Height + 1f)
		{
			return;
		}
		Texture2D pixel = TextureAssets.MagicPixel.Value;
		sb.Draw(pixel, track, new Color(20, 26, 50) * 0.8f);
		float ratio = _contentRect.Height / _contentHeight;
		int thumbH = Math.Max(24, (int)(track.Height * ratio));
		float maxScroll = _contentHeight - _contentRect.Height;
		int thumbY = track.Y + (int)((track.Height - thumbH) * (_scroll / maxScroll));
		Rectangle thumb = new Rectangle(track.X + 1, thumbY, track.Width - 2, thumbH);
		bool hover = track.Contains(_mouse.ToPoint());
		if (hover && Main.mouseLeft && Main.mouseLeftRelease)
		{
			_draggingThumb = true;
			_dragOffset = thumb.Contains(_mouse.ToPoint()) ? (_mouse.Y - thumbY) : thumbH / 2f;
			Main.mouseLeftRelease = false;
		}
		if (_draggingThumb)
		{
			if (!Main.mouseLeft)
			{
				_draggingThumb = false;
			}
			else
			{
				float t = (_mouse.Y - _dragOffset - track.Y) / Math.Max(1f, track.Height - thumbH);
				_scroll = MathHelper.Clamp(t, 0f, 1f) * maxScroll;
			}
		}
		sb.Draw(pixel, thumb, (hover || _draggingThumb) ? new Color(150, 170, 230) : new Color(110, 125, 180));
	}

	#endregion

	#region Widgets

	private bool Visible(Rectangle r)
	{
		return r.Bottom > _contentRect.Top && r.Top < _contentRect.Bottom;
	}

	private bool MouseIn(Rectangle r, bool clip)
	{
		if (!r.Contains(_mouse.ToPoint()))
		{
			return false;
		}
		return !clip || _contentRect.Contains(_mouse.ToPoint());
	}

	private static void Card(SpriteBatch sb, Rectangle r, Color c)
	{
		Utils.DrawInvBG(sb, r, c);
	}

	/// <summary>Draws text cut to <paramref name="maxWidth"/> (adds "..").</summary>
	private static void Text(SpriteBatch sb, string text, float x, float y, Color color, float scale = 0.85f, float maxWidth = 0f)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		if (maxWidth > 0f)
		{
			text = Fit(text, maxWidth, scale);
		}
		ChatManager.DrawColorCodedStringWithShadow(sb, Font, IsaClient.Languages.L10n.T(text), new Vector2(x, y), color, 0f, Vector2.Zero, new Vector2(scale), -1f, 1.2f);
	}

	private static float Measure(string text, float scale)
	{
		return ChatManager.GetStringSize(Font, IsaClient.Languages.L10n.T(text ?? ""), new Vector2(scale)).X;
	}

	private static string Fit(string text, float maxWidth, float scale)
	{
		if (Measure(text, scale) <= maxWidth)
		{
			return text;
		}
		// Color tags make cutting unsafe; drop them before cutting.
		string plain = System.Text.RegularExpressions.Regex.Replace(text, "\\[c/[0-9A-Fa-f]{6}:([^\\]]*)\\]", "$1");
		int lo = 0;
		int hi = plain.Length;
		while (lo < hi)
		{
			int mid = (lo + hi + 1) / 2;
			if (Measure(plain.Substring(0, mid) + "..", scale) <= maxWidth)
			{
				lo = mid;
			}
			else
			{
				hi = mid - 1;
			}
		}
		return plain.Substring(0, lo).TrimEnd() + "..";
	}

	private bool Button(string id, Rectangle r, string label, Color color, bool clip = true, bool enabled = true, bool bold = false)
	{
		if (clip && !Visible(r))
		{
			return false;
		}
		bool hover = enabled && MouseIn(r, clip);
		if (hover)
		{
			_hoverId = id;
			Main.LocalPlayer.mouseInterface = true;
		}
		Color bg = !enabled ? new Color(60, 60, 75) * 0.8f : (hover ? Color.Lerp(color, Color.White, 0.18f) : color);
		Utils.DrawInvBG(Main.spriteBatch, r, bg * 0.95f);
		float scale = 0.8f;
		string shown = Fit(label, r.Width - 10f, scale);
		Vector2 size = ChatManager.GetStringSize(Font, IsaClient.Languages.L10n.T(shown), new Vector2(scale));
		Color textColor = !enabled ? new Color(140, 140, 150) : (bold ? _accent : (hover ? Color.White : new Color(225, 230, 240)));
		ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, Font, IsaClient.Languages.L10n.T(shown), new Vector2(r.X + (r.Width - size.X) / 2f, r.Y + (r.Height - size.Y) / 2f + 3f), textColor, 0f, Vector2.Zero, new Vector2(scale), -1f, 1.2f);
		return hover && _clicked;
	}

	/// <summary>A selectable card row; returns true when clicked.</summary>
	private bool SelectRow(string id, Rectangle r, bool selected)
	{
		if (!Visible(r))
		{
			return false;
		}
		bool hover = MouseIn(r, clip: true);
		if (hover)
		{
			_hoverId = id;
		}
		Card(Main.spriteBatch, r, selected ? _cardSelected : (hover ? Color.Lerp(_cardColor, Color.White, 0.08f) : _cardColor));
		if (selected)
		{
			Texture2D pixel = TextureAssets.MagicPixel.Value;
			Main.spriteBatch.Draw(pixel, new Rectangle(r.X + 3, r.Y + 5, 3, r.Height - 10), _accent);
		}
		return hover && _clicked;
	}

	private string Field(string id, Rectangle r, string value, string placeholder, int maxLength, bool secret = false)
	{
		string fid = "field:" + id;
		bool focused = _focus == fid;
		if (Visible(r))
		{
			bool hover = MouseIn(r, clip: true);
			if (hover)
			{
				_hoverId = fid;
				Main.LocalPlayer.mouseInterface = true;
				if (_clicked && !focused)
				{
					_focus = fid;
					focused = true;
					Main.clrInput();
				}
			}
			Utils.DrawInvBG(Main.spriteBatch, r, focused ? new Color(40, 55, 110) : new Color(25, 32, 64) * 0.95f);
		}
		if (focused)
		{
			PlayerInput.WritingText = true;
			Main.instance.HandleIME();
			string typed = Main.GetInputText(value ?? "");
			if (typed.Length > maxLength)
			{
				typed = typed.Substring(0, maxLength);
			}
			value = typed;
			if (Main.inputTextEnter || Main.inputTextEscape)
			{
				Main.inputTextEscape = false;
				_focus = null;
				focused = false;
			}
		}
		if (Visible(r))
		{
			string shown = string.IsNullOrEmpty(value) ? placeholder : (secret ? new string('*', value.Length) : value);
			Color color = string.IsNullOrEmpty(value) ? new Color(120, 130, 150) : Color.White;
			string fitted = shown;
			while (fitted.Length > 0 && Measure(fitted + "|", 0.8f) > r.Width - 14f)
			{
				fitted = fitted.Substring(1); // keep the end visible while typing
			}
			if (focused && Main.GameUpdateCount / 30 % 2 == 0)
			{
				fitted = (string.IsNullOrEmpty(value) ? "" : fitted) + "|";
				color = Color.White;
			}
			Text(Main.spriteBatch, fitted, r.X + 7, r.Y + (r.Height - 20) / 2f + 2f, color, 0.8f);
		}
		return value;
	}

	private float Section(SpriteBatch sb, float y, string title, string hint = null)
	{
		Text(sb, title, _contentRect.X + Pad, y + 4f, _accent, 0.9f);
		if (hint != null)
		{
			float x = _contentRect.X + Pad + Measure(title, 0.9f) + 12f;
			Text(sb, hint, x, y + 6f, _muted, 0.75f, _contentRect.Right - Pad - x);
		}
		return y + 28f;
	}

	/// <summary>Buttons laid out right-to-left inside a row; returns the left edge they reached.</summary>
	private float RightButtons(Rectangle row, float right, params (string id, string label, float width, Color color, Action onClick)[] buttons)
	{
		float x = right;
		foreach (var b in buttons)
		{
			x -= b.width;
			Rectangle r = new Rectangle((int)x, row.Y + (row.Height - 28) / 2, (int)b.width, 28);
			if (Button(b.id, r, b.label, b.color))
			{
				b.onClick?.Invoke();
			}
			x -= 6f;
		}
		return x;
	}

	#endregion

	#region Sessions

	private float DrawSessions(SpriteBatch sb, float y)
	{
		float left = _contentRect.X + Pad;
		float width = _contentRect.Width - Pad * 2f;
		string notice = MultiClientManager.LastNotice;
		// You
		Rectangle you = new Rectangle((int)left, (int)y, (int)width, 54);
		if (Visible(you))
		{
			Card(sb, you, new Color(70, 64, 40) * 0.9f);
			float textRight = you.Right - 10f;
			if (Possession.Active)
			{
				textRight = RightButtons(you, you.Right - 10f, ("ret-you", "Go back to you", 130f, _green, (Action)delegate
				{
					MultiClientManager.Control(null);
				}));
			}
			Badge(sb, you.X + 10, you.Y + 15, "YOU", _accent);
			float tx = you.X + 56f;
			Text(sb, Possession.Active ? Possession.OriginalName : Main.LocalPlayer.name, tx, you.Y + 8f, _accent, 0.9f, textRight - tx);
			string sub;
			if (Possession.Active)
			{
				sub = "kept online at " + Possession.MainSession.Host + ":" + Possession.MainSession.Port + " while you play as " + Possession.Current.Name;
			}
			else
			{
				MultiClientManager.TryGetCurrentServer(out var h, out var p);
				sub = (h != null) ? ("you are here - " + h + ":" + p) : "not on a server";
			}
			Text(sb, sub, tx, you.Y + 30f, _muted, 0.75f, textRight - tx);
		}
		y += you.Height + 8f;

		List<ClientSession> sessions = MultiClientManager.All;
		if (sessions.Count == 0)
		{
			Rectangle empty = new Rectangle((int)left, (int)y, (int)width, 60);
			if (Visible(empty))
			{
				Card(sb, empty, _cardColor * 0.7f);
				Text(sb, "No sessions yet.", empty.X + 14, empty.Y + 10, Color.White, 0.85f);
				Text(sb, "Use \"Add 1 here\" or open \"New session\" to pick a character and a server.", empty.X + 14, empty.Y + 32, _muted, 0.75f, empty.Width - 28);
			}
			y += empty.Height + 8f;
		}
		foreach (ClientSession s in sessions)
		{
			Rectangle row = new Rectangle((int)left, (int)y, (int)width, 56);
			y += row.Height + 6f;
			if (!Visible(row))
			{
				continue;
			}
			Card(sb, row, s.Possessed ? new Color(40, 90, 80) * 0.92f : _cardColor);
			ClientSession bot = s;
			float textRight;
			if (s.Possessed)
			{
				textRight = RightButtons(row, row.Right - 10f, ("ret" + s.Id, "Go back to you", 130f, _green, (Action)delegate
				{
					MultiClientManager.Control(null);
				}));
			}
			else if (s.State == ClientSessionState.InGame)
			{
				bool here = Possession.IsOnMyServer(s);
				bool chatting = MultiClientManager.ChatTarget == s;
				textRight = RightButtons(row, row.Right - 10f,
					("x" + s.Id, "X", 30f, _red, (Action)delegate { MultiClientManager.Remove(bot); }),
					("save" + s.Id, "Save", 52f, _blue * 0.85f, (Action)delegate { MultiClientManager.SaveProfile(bot); }),
					("ai" + s.Id, bot.AiEnabled ? "AI on" : "AI", 56f, bot.AiEnabled ? _gold : _blue * 0.85f, (Action)delegate { MultiClientManager.SetAi(bot, !bot.AiEnabled); }),
					("fol" + s.Id, s.IsFollowing ? "Stop" : "Follow", 62f, here ? _blue : (_blue * 0.5f), (Action)delegate
					{
						if (bot.IsFollowing)
						{
							bot.StopMoving();
						}
						else
						{
							MultiClientManager.FollowPlayer(bot, Main.myPlayer);
						}
					}),
					("chat" + s.Id, chatting ? "Chatting" : "Chat", 72f, chatting ? _gold : _blue, (Action)delegate { MultiClientManager.SetChatTarget(chatting ? null : bot); }),
					("play" + s.Id, "Play as", 70f, _green, (Action)delegate { MultiClientManager.Control(bot); }));
			}
			else if (s.State == ClientSessionState.Disconnected)
			{
				textRight = RightButtons(row, row.Right - 10f,
					("x" + s.Id, "X", 30f, _red, (Action)delegate { MultiClientManager.Remove(bot); }),
					("save" + s.Id, "Save", 52f, _blue * 0.85f, (Action)delegate { MultiClientManager.SaveProfile(bot); }),
					("rec" + s.Id, "Reconnect", 90f, _green, (Action)delegate { MultiClientManager.Reconnect(bot); }));
			}
			else
			{
				textRight = RightButtons(row, row.Right - 10f, ("x" + s.Id, "X", 30f, _red, (Action)delegate { MultiClientManager.Remove(bot); }));
			}
			Color dot = s.Possessed ? new Color(110, 230, 210) : (s.State == ClientSessionState.InGame ? new Color(110, 220, 120) : (s.State == ClientSessionState.Disconnected ? new Color(230, 100, 100) : new Color(240, 200, 90)));
			Badge(sb, row.X + 10, row.Y + 16, s.Id.ToString(), dot);
			float tx = row.X + 50f;
			Text(sb, s.Name, tx, row.Y + 8f, Color.White, 0.88f, textRight - tx);
			string look = (s.Template != null) ? (System.IO.Path.GetFileNameWithoutExtension(s.TemplatePath ?? "") + ".plr") : "generated";
			string where = s.Host + ":" + s.Port + (Possession.IsOnMyServer(s) ? "" : "  (other server)");
			Text(sb, MultiClientManager.Describe(s) + "  -  " + where + "  -  " + look, tx, row.Y + 31f, _muted, 0.72f, textRight - tx);
		}

		// Actions
		Rectangle bar = new Rectangle((int)left, (int)y + 2, (int)width, 34);
		if (Visible(bar))
		{
			float bw = (width - 18f) / 4f;
			bool canHere = MultiClientManager.TryGetCurrentServer(out var ch, out var cp);
			if (Button("addhere", new Rectangle(bar.X, bar.Y, (int)bw, 32), "Add 1 here", canHere ? _green : _green * 0.5f, enabled: canHere))
			{
				MultiClientManager.AddSessions(1, ch, cp);
			}
			if (Button("new", new Rectangle((int)(bar.X + bw + 6f), bar.Y, (int)bw, 32), "New session...", _blue))
			{
				_view = View.New;
				_scroll = 0f;
				MultiClientManager.RefreshCharacters();
			}
			if (Button("recall", new Rectangle((int)(bar.X + (bw + 6f) * 2f), bar.Y, (int)bw, 32), "Reconnect all", _blue * 0.85f))
			{
				foreach (ClientSession s in sessions.Where((ClientSession s) => s.State == ClientSessionState.Disconnected))
				{
					MultiClientManager.Reconnect(s);
				}
			}
			if (Button("remall", new Rectangle((int)(bar.X + (bw + 6f) * 3f), bar.Y, (int)bw, 32), "Remove all", _red, enabled: sessions.Count > 0))
			{
				MultiClientManager.RemoveAll();
			}
		}
		y += bar.Height + 8f;
		if (!string.IsNullOrEmpty(notice))
		{
			Text(sb, notice, left + 2f, y, Color.White, 0.75f, width - 4f);
			y += 22f;
		}
		Text(sb, "Commands: .bot control <id>, .bot return, .bot chat <id|off>, .bot follow <id>, .bot as <character>", left + 2f, y, _muted, 0.7f, width - 4f);
		return y + 20f;
	}

	private static void Badge(SpriteBatch sb, int x, int y, string text, Color color)
	{
		float w = Math.Max(24f, Measure(text, 0.75f) + 10f);
		Rectangle box = new Rectangle(x, y, (int)w, 24);
		Texture2D pixel = TextureAssets.MagicPixel.Value;
		sb.Draw(pixel, box, color * 0.25f);
		sb.Draw(pixel, new Rectangle(box.X, box.Y, box.Width, 2), color);
		sb.Draw(pixel, new Rectangle(box.X, box.Bottom - 2, box.Width, 2), color);
		sb.Draw(pixel, new Rectangle(box.X, box.Y, 2, box.Height), color);
		sb.Draw(pixel, new Rectangle(box.Right - 2, box.Y, 2, box.Height), color);
		float tw = Measure(text, 0.75f);
		Text(sb, text, box.X + (box.Width - tw) / 2f, box.Y + 4f, color, 0.75f);
	}

	#endregion

	#region New session

	private float DrawNew(SpriteBatch sb, float y)
	{
		float left = _contentRect.X + Pad;
		float width = _contentRect.Width - Pad * 2f;

		// 1. Look
		y = Section(sb, y, "1. Look", "a character of yours (its look and items) or a generated one");
		Rectangle refresh = new Rectangle((int)(left + width - 86f), (int)y - 30, 86, 26);
		if (Button("refreshchars", refresh, "Refresh", _blue * 0.85f))
		{
			MultiClientManager.RefreshCharacters();
		}
		Rectangle gen = new Rectangle((int)left, (int)y, (int)width, 34);
		if (SelectRow("char-gen", gen, _character < 0))
		{
			_character = -1;
		}
		if (Visible(gen))
		{
			Text(sb, "Generated", gen.X + 14, gen.Y + 8, Color.White, 0.85f);
			Text(sb, MultiClientManager.RandomNames ? "random name, empty inventory" : "Bot1, Bot2..., empty inventory", gen.X + 120, gen.Y + 10, _muted, 0.75f, gen.Width - 130);
		}
		y += gen.Height + 4f;
		List<CharacterEntry> chars = MultiClientManager.Characters;
		if (chars.Count == 0)
		{
			Text(sb, "No local .plr characters found in " + Main.PlayerPath, left + 4f, y + 2f, _muted, 0.72f, width - 8f);
			y += 22f;
		}
		for (int i = 0; i < chars.Count; i++)
		{
			CharacterEntry c = chars[i];
			Rectangle r = new Rectangle((int)left, (int)y, (int)width, 34);
			int index = i;
			if (SelectRow("char" + i, r, _character == i))
			{
				_character = index;
			}
			if (Visible(r))
			{
				float nameW = Math.Min(Measure(c.Name, 0.85f), width * 0.45f);
				Text(sb, c.Name, r.X + 14, r.Y + 8, Color.White, 0.85f, width * 0.45f);
				Text(sb, c.Summary + "  -  " + System.IO.Path.GetFileName(c.Path), r.X + 26 + nameW, r.Y + 10, _muted, 0.72f, r.Right - (r.X + 34 + nameW));
			}
			y += r.Height + 4f;
		}
		y += 4f;
		Rectangle nameRow = new Rectangle((int)left, (int)y, (int)width, 32);
		if (Visible(nameRow))
		{
			Text(sb, "Name", nameRow.X + 4, nameRow.Y + 7, Color.White, 0.8f);
		}
		_name = Field("name", new Rectangle(nameRow.X + 90, nameRow.Y, nameRow.Width - 90, 32), _name, (_character >= 0 && _character < chars.Count) ? ("(" + chars[_character].Name + ")") : "(random)", 20);
		y += nameRow.Height + 12f;

		// 2. Server
		y = Section(sb, y, "2. Server", "where it connects");
		List<ServerEntry> servers = MultiClientManager.KnownServers();
		if (_server > servers.Count)
		{
			_server = 0;
		}
		for (int i = 0; i < servers.Count; i++)
		{
			ServerEntry s = servers[i];
			Rectangle r = new Rectangle((int)left, (int)y, (int)width, 34);
			int index = i;
			if (SelectRow("srv" + i, r, _server == i))
			{
				_server = index;
			}
			if (Visible(r))
			{
				string addr = s.Host + ":" + s.Port;
				float addrW = Measure(addr, 0.85f);
				Text(sb, addr, r.X + 14, r.Y + 8, Color.White, 0.85f, width * 0.55f);
				Text(sb, s.Label, r.X + 26 + Math.Min(addrW, width * 0.55f), r.Y + 10, _muted, 0.72f, r.Right - (r.X + 34 + Math.Min(addrW, width * 0.55f)));
			}
			y += r.Height + 4f;
		}
		Rectangle custom = new Rectangle((int)left, (int)y, (int)width, 40);
		if (SelectRow("srv-custom", custom, _server == servers.Count))
		{
			_server = servers.Count;
		}
		if (Visible(custom))
		{
			Text(sb, "Other:", custom.X + 14, custom.Y + 11, Color.White, 0.85f);
		}
		float fx = custom.X + 80;
		float portW = 80f;
		string host = Field("host", new Rectangle((int)fx, custom.Y + 5, (int)(custom.Right - fx - portW - 16), 30), _customHost, "ip or domain", 60);
		string port = Field("port", new Rectangle(custom.Right - (int)portW - 8, custom.Y + 5, (int)portW, 30), _customPort, "7777", 5);
		if (host != _customHost || port != _customPort)
		{
			_server = servers.Count;
		}
		_customHost = host;
		_customPort = new string(port.Where(char.IsDigit).ToArray());
		y += custom.Height + 6f;
		Rectangle pwRow = new Rectangle((int)left, (int)y, (int)width, 32);
		if (Visible(pwRow))
		{
			Text(sb, "Password", pwRow.X + 4, pwRow.Y + 7, Color.White, 0.8f);
		}
		_password = Field("pw", new Rectangle(pwRow.X + 90, pwRow.Y, pwRow.Width - 90, 32), _password, "(only if the server has one; empty = same as yours when it is your server)", 60, secret: true);
		y += pwRow.Height + 12f;

		// 3. How many + UUID
		y = Section(sb, y, "3. How many");
		Rectangle countRow = new Rectangle((int)left, (int)y, (int)width, 40);
		if (Visible(countRow))
		{
			Card(sb, countRow, _cardColor);
			if (Button("cminus", new Rectangle(countRow.X + 10, countRow.Y + 6, 30, 28), "-", _blue))
			{
				_count = Math.Max(1, _count - 1);
			}
			string cnt = _count.ToString();
			Text(sb, cnt, countRow.X + 58 - Measure(cnt, 0.9f) / 2f, countRow.Y + 10, Color.White, 0.9f);
			if (Button("cplus", new Rectangle(countRow.X + 76, countRow.Y + 6, 30, 28), "+", _blue))
			{
				_count = Math.Min(Math.Min(10, MultiClientManager.MaxSessions - MultiClientManager.Count), _count + 1);
				_count = Math.Max(1, _count);
			}
			float ux = countRow.X + 124;
			float uRight = RightButtons(countRow, countRow.Right - 10f, ("uuidmode", "Change", 80f, _blue, (Action)MultiClientManager.CycleUuidMode));
			Text(sb, "UUID: " + MultiClientManager.UuidModeText, ux, countRow.Y + 11, _muted, 0.75f, uRight - ux);
		}
		y += countRow.Height + 12f;

		// Connect
		ServerEntry chosen = (_server < servers.Count) ? servers[_server] : null;
		string targetHost = (chosen != null) ? chosen.Host : _customHost.Trim();
		int targetPort = (chosen != null) ? chosen.Port : (int.TryParse(_customPort, out var cp2) ? cp2 : 7777);
		bool ok = !string.IsNullOrWhiteSpace(targetHost) && MultiClientManager.Count < MultiClientManager.MaxSessions;
		CharacterEntry character = (_character >= 0 && _character < chars.Count) ? chars[_character] : null;
		string who = !string.IsNullOrWhiteSpace(_name) ? _name.Trim() : (character?.Name ?? "random name");
		Rectangle go = new Rectangle((int)left, (int)y, (int)(width * 0.62f), 38);
		if (Button("connect", go, ok ? ("Connect " + _count + " as " + who + " to " + targetHost + ":" + targetPort) : "Choose a server", _green, enabled: ok))
		{
			MultiClientManager.AddSessions(_count, targetHost, targetPort, string.IsNullOrWhiteSpace(_name) ? null : _name, character, string.IsNullOrEmpty(_password) ? null : _password);
			_view = View.Sessions;
			_scroll = 0f;
		}
		Rectangle goSave = new Rectangle(go.Right + 6, (int)y, (int)(width - go.Width - 6), 38);
		if (Button("connectsave", goSave, "Connect + save", _blue, enabled: ok))
		{
			MultiClientManager.AddSessions(_count, targetHost, targetPort, string.IsNullOrWhiteSpace(_name) ? null : _name, character, string.IsNullOrEmpty(_password) ? null : _password);
			foreach (ClientSession s in MultiClientManager.All.Where((ClientSession s) => s.Host == targetHost && s.Port == targetPort && s.State != ClientSessionState.Disconnected).Reverse().Take(_count))
			{
				MultiClientManager.SaveProfile(s);
			}
			_view = View.Sessions;
			_scroll = 0f;
		}
		y += go.Height + 8f;
		if (!string.IsNullOrEmpty(MultiClientManager.LastNotice))
		{
			Text(sb, MultiClientManager.LastNotice, left + 2f, y, Color.White, 0.75f, width - 4f);
			y += 22f;
		}
		return y;
	}

	#endregion

	#region Saved

	private float DrawSaved(SpriteBatch sb, float y)
	{
		float left = _contentRect.X + Pad;
		float width = _contentRect.Width - Pad * 2f;
		List<SessionProfile> last = MultiClientManager.LastSessions.ToList();
		if (last.Count > 0)
		{
			y = Section(sb, y, "Online last time", "call them back when you want; nothing connects on its own");
			foreach (SessionProfile entry in last)
			{
				SessionProfile p = entry;
				Rectangle row = new Rectangle((int)left, (int)y, (int)width, 50);
				y += row.Height + 6f;
				if (!Visible(row))
				{
					continue;
				}
				Card(sb, row, _cardColor);
				float textRight = RightButtons(row, row.Right - 10f,
					("ldis" + p.GetHashCode(), "X", 30f, _red, (Action)delegate { MultiClientManager.DismissLast(p); }),
					("lcon" + p.GetHashCode(), "Call back", 90f, _green, (Action)delegate { MultiClientManager.ConnectLast(p); }));
				Text(sb, p.Name + (p.AiEnabled ? "   [c/FFE645:AI]" : ""), row.X + 14, row.Y + 6, Color.White, 0.88f, textRight - row.X - 20);
				string look = string.IsNullOrEmpty(p.PlayerFile) ? "generated look" : System.IO.Path.GetFileName(p.PlayerFile);
				Text(sb, p.Host + ":" + p.Port + "  -  " + look, row.X + 14, row.Y + 28, _muted, 0.72f, textRight - row.X - 20);
			}
			Rectangle callAll = new Rectangle((int)left, (int)y + 2, 200, 32);
			if (Button("lconall", callAll, "Call all back", _green))
			{
				foreach (SessionProfile p in last)
				{
					MultiClientManager.ConnectLast(p);
				}
			}
			y += 46f;
		}
		y = Section(sb, y, "Saved sessions", "\"Save\" on a session keeps its name, look and server here");
		List<SessionProfile> profiles = MultiClientManager.Profiles.ToList();
		if (profiles.Count == 0)
		{
			Rectangle empty = new Rectangle((int)left, (int)y, (int)width, 40);
			if (Visible(empty))
			{
				Card(sb, empty, _cardColor * 0.7f);
				Text(sb, "Nothing saved yet.", empty.X + 14, empty.Y + 10, _muted, 0.8f);
			}
			return y + empty.Height + 8f;
		}
		foreach (SessionProfile profile in profiles)
		{
			SessionProfile p = profile;
			Rectangle row = new Rectangle((int)left, (int)y, (int)width, 50);
			y += row.Height + 6f;
			if (!Visible(row))
			{
				continue;
			}
			Card(sb, row, _cardColor);
			float textRight = RightButtons(row, row.Right - 10f,
				("pdel" + p.GetHashCode(), "X", 30f, _red, (Action)delegate { MultiClientManager.DeleteProfile(p); }),
				("pcon" + p.GetHashCode(), "Connect", 80f, _green, (Action)delegate { MultiClientManager.ConnectProfile(p); }));
			Text(sb, p.Name, row.X + 14, row.Y + 6, Color.White, 0.88f, textRight - row.X - 20);
			string look = string.IsNullOrEmpty(p.PlayerFile) ? "generated look" : System.IO.Path.GetFileName(p.PlayerFile);
			Text(sb, p.Host + ":" + p.Port + "  -  " + look + (string.IsNullOrEmpty(p.Password) ? "" : "  -  password saved"), row.X + 14, row.Y + 28, _muted, 0.72f, textRight - row.X - 20);
		}
		Rectangle all = new Rectangle((int)left, (int)y + 2, 200, 32);
		if (Button("pconall", all, "Connect all saved", _green))
		{
			foreach (SessionProfile p in profiles)
			{
				MultiClientManager.ConnectProfile(p);
			}
		}
		y += 42f;
		if (!string.IsNullOrEmpty(MultiClientManager.LastNotice))
		{
			Text(sb, MultiClientManager.LastNotice, left + 2f, y, Color.White, 0.75f, width - 4f);
			y += 22f;
		}
		return y;
	}

	#endregion

	#region Options

	private float DrawOptions(SpriteBatch sb, float y)
	{
		float left = _contentRect.X + Pad;
		float width = _contentRect.Width - Pad * 2f;
		y = Section(sb, y, "Identity");
		y = OptionRow(sb, y, "UUID for new sessions", MultiClientManager.UuidModeText, "Change", delegate
		{
			MultiClientManager.CycleUuidMode();
		});
		Rectangle custom = new Rectangle((int)left, (int)y, (int)width, 40);
		if (Visible(custom))
		{
			Card(sb, custom, _cardColor);
			Text(sb, "Fixed UUID", custom.X + 12, custom.Y + 11, Color.White, 0.8f);
		}
		_customUuid = Field("uuid", new Rectangle(custom.X + 110, custom.Y + 5, custom.Width - 110 - 100, 30), _customUuid, string.IsNullOrEmpty(MultiClientManager.CustomUuid) ? "paste a UUID" : MultiClientManager.CustomUuid, 40);
		if (Button("setuuid", new Rectangle(custom.Right - 90, custom.Y + 6, 80, 28), "Use it", _blue, enabled: Guid.TryParse(_customUuid, out _)))
		{
			MultiClientManager.CustomUuid = Guid.Parse(_customUuid).ToString();
			MultiClientManager.UuidMode = SessionUuidMode.Custom;
			MultiClientManager.Save();
			_customUuid = "";
		}
		y += custom.Height + 6f;
		y = OptionRow(sb, y, "Remembered per-name UUIDs", "same name keeps the same UUID", "Forget", delegate
		{
			MultiClientManager.ForgetNameUuids();
		});
		y = Toggle(sb, y, "Random names", "Alex42, Luna17... instead of Bot1, Bot2", MultiClientManager.RandomNames, delegate
		{
			MultiClientManager.RandomNames = !MultiClientManager.RandomNames;
		});
		y += 8f;
		y = Section(sb, y, "Behaviour");
		y = Toggle(sb, y, "Fight enemies", "in your world: hotbar slot 1 if it is a weapon, else the first weapon on the hotbar (tools never)", MultiClientManager.SessionsFight, delegate
		{
			MultiClientManager.SessionsFight = !MultiClientManager.SessionsFight;
		});
		y += 8f;
		y = Section(sb, y, "Staying online");
		y = Toggle(sb, y, "Keep sessions when I leave", "they stay connected in the menu and in other worlds", MultiClientManager.KeepOnLeave, delegate
		{
			MultiClientManager.KeepOnLeave = !MultiClientManager.KeepOnLeave;
		});
		y = Toggle(sb, y, "Reconnect dropped sessions", "up to 5 tries; never after a kick", MultiClientManager.AutoReconnect, delegate
		{
			MultiClientManager.AutoReconnect = !MultiClientManager.AutoReconnect;
		});
		y += 8f;
		y = Section(sb, y, "Switching");
		y = Toggle(sb, y, "Remember my world", "needed to come back after playing as a session on another server (applies from your next join)", Possession.RecordOwnWorld, delegate
		{
			Possession.RecordOwnWorld = !Possession.RecordOwnWorld;
		});
		y = Toggle(sb, y, "Show what my chat session sees", "only for sessions on other servers (.bot chat)", MultiClientManager.ShowSessionChat, delegate
		{
			MultiClientManager.ShowSessionChat = !MultiClientManager.ShowSessionChat;
		});
		return y;
	}

	private float OptionRow(SpriteBatch sb, float y, string title, string value, string button, Action onClick)
	{
		Rectangle row = new Rectangle((int)(_contentRect.X + Pad), (int)y, (int)(_contentRect.Width - Pad * 2f), 44);
		if (Visible(row))
		{
			Card(sb, row, _cardColor);
			float textRight = RightButtons(row, row.Right - 10f, ("opt:" + title, button, 90f, _blue, onClick));
			Text(sb, title, row.X + 12, row.Y + 5, Color.White, 0.82f, textRight - row.X - 18);
			Text(sb, value, row.X + 12, row.Y + 24, new Color(140, 228, 155), 0.72f, textRight - row.X - 18);
		}
		return y + row.Height + 6f;
	}

	private float Toggle(SpriteBatch sb, float y, string title, string hint, bool on, Action flip)
	{
		return OptionRow(sb, y, title, (on ? "On" : "Off") + "  -  " + hint, on ? "Turn off" : "Turn on", delegate
		{
			flip();
			MultiClientManager.Save();
		});
	}

	#endregion
}
