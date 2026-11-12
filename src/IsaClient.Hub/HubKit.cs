using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI.Chat;

using IsaClient.Languages;

namespace IsaClient.Hub;

/// <summary>
/// The new panel's drawing kit: Terraria's own panel, slot, slider and scrollbar textures put together as immediate
/// mode controls (each returns true on click). Everything is in hub units (see <see cref="HubInput"/>).
/// </summary>
internal static class HubKit
{
	public static DynamicSpriteFont Font => FontAssets.MouseText.Value;

	public static DynamicSpriteFont BigFont => FontAssets.DeathText.Value;

	public static Texture2D Pixel => TextureAssets.MagicPixel.Value;

	public static Texture2D Tex(string path)
	{
		return Main.Assets.Request<Texture2D>(path, AssetRequestMode.ImmediateLoad).Value;
	}

	private static HubTheme Theme => HubTheme.Current;

	#region Shapes

	public static void Fill(SpriteBatch sb, Rectangle r, Color c)
	{
		sb.Draw(Pixel, r, c);
	}

	/// <summary>Rounded inventory-style box.</summary>
	public static void Box(SpriteBatch sb, Rectangle r, Color c)
	{
		if (r.Width < 20 || r.Height < 20)
		{
			Fill(sb, r, c);
			return;
		}
		Utils.DrawInvBG(sb, r, c);
	}

	/// <summary>The panel of Terraria's menus (background + border).</summary>
	public static void Panel(SpriteBatch sb, Rectangle r, Color back, Color border)
	{
		Utils.DrawSplicedPanel(sb, Tex("Images/UI/PanelBackground"), r.X, r.Y, r.Width, r.Height, 12, 12, 12, 12, back);
		Utils.DrawSplicedPanel(sb, Tex("Images/UI/PanelBorder"), r.X, r.Y, r.Width, r.Height, 12, 12, 12, 12, border);
	}

	/// <summary>An inventory slot (the frame items sit in).</summary>
	public static void Slot(SpriteBatch sb, Rectangle r, Color c)
	{
		sb.Draw(TextureAssets.InventoryBack.Value, r, c);
	}

	/// <summary>A soft horizontal line.</summary>
	public static void Line(SpriteBatch sb, int x, int y, int width, Color c)
	{
		Fill(sb, new Rectangle(x, y, width, 1), c);
		Fill(sb, new Rectangle(x, y + 1, width, 1), c * 0.35f);
	}

	#endregion

	#region Text

	// Every text goes through the player's language (IsaClient.Languages.L10n) where it comes in; the Raw versions
	// work on text that is already translated (or typed by the player, which is never translated).

	public static Vector2 Size(string text, float scale)
	{
		return RawSize(L10n.T(text), scale);
	}

	private static Vector2 RawSize(string text, float scale)
	{
		return ChatManager.GetStringSize(Font, text ?? "", new Vector2(scale));
	}

	private static float RawMeasure(string text, float scale)
	{
		return RawSize(text, scale).X;
	}

	public static float Measure(string text, float scale)
	{
		return Size(text, scale).X;
	}

	/// <summary>Cuts the text with "..." to fit the width.</summary>
	public static string Fit(string text, float maxWidth, float scale)
	{
		return RawFit(L10n.T(text), maxWidth, scale);
	}

	private static string RawFit(string text, float maxWidth, float scale)
	{
		text = text ?? "";
		if (RawMeasure(text, scale) <= maxWidth)
		{
			return text;
		}
		int lo = 0;
		int hi = text.Length;
		while (lo < hi)
		{
			int mid = (lo + hi + 1) / 2;
			if (RawMeasure(text.Substring(0, mid) + "...", scale) <= maxWidth)
			{
				lo = mid;
			}
			else
			{
				hi = mid - 1;
			}
		}
		return text.Substring(0, lo).TrimEnd() + "...";
	}

	/// <summary>Plain text (chat tags are not parsed), top-left at x, y.</summary>
	public static void Text(SpriteBatch sb, string text, float x, float y, Color color, float scale = 0.8f, float maxWidth = 0f)
	{
		RawText(sb, L10n.T(text), x, y, color, scale, maxWidth);
	}

	/// <summary>Text drawn as it is (what the player typed, names, text already translated).</summary>
	public static void RawText(SpriteBatch sb, string text, float x, float y, Color color, float scale = 0.8f, float maxWidth = 0f)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		if (maxWidth > 0f)
		{
			text = RawFit(text, maxWidth, scale);
		}
		ChatManager.DrawColorCodedStringWithShadow(sb, Font, new TextSnippet[1]
		{
			new TextSnippet(text, color)
		}, new Vector2(x, y), 0f, Vector2.Zero, new Vector2(scale), out _, -1f, 1.2f);
	}

	/// <summary>Text with Terraria chat tags ([c/FF0000:red], [i:29]), as the classic rows write their values.</summary>
	public static void RichText(SpriteBatch sb, string text, float x, float y, Color color, float scale = 0.8f, float maxWidth = 0f)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		text = L10n.T(text);
		if (maxWidth > 0f && !text.Contains("[") && RawMeasure(text, scale) > maxWidth)
		{
			text = RawFit(text, maxWidth, scale);
		}
		ChatManager.DrawColorCodedStringWithShadow(sb, Font, text, new Vector2(x, y), color, 0f, Vector2.Zero, new Vector2(scale), maxWidth > 0f ? maxWidth : -1f, 1.2f);
	}

	public static Vector2 RichSize(string text, float scale)
	{
		return RawSize(L10n.T(text), scale);
	}

	public static void TextCentered(SpriteBatch sb, string text, Rectangle r, Color color, float scale = 0.8f)
	{
		text = Fit(text, r.Width - 6, scale);
		Vector2 size = RawSize(text, scale);
		RawText(sb, text, r.X + (r.Width - size.X) / 2f, r.Y + (r.Height - size.Y) / 2f + 3f * scale, color, scale);
	}

	/// <summary>A big title in the font of Terraria's menus; shrinks a little before it gets cut.</summary>
	public static void Title(SpriteBatch sb, string text, float x, float y, float maxWidth, float scale, Color color)
	{
		text = L10n.T(text) ?? "";
		float width = BigFont.MeasureString(text).X;
		if (width * scale > maxWidth)
		{
			scale = Math.Max(scale * 0.75f, maxWidth / Math.Max(1f, width));
		}
		Utils.DrawBorderStringBig(sb, text, new Vector2(x, y), color, scale);
	}

	/// <summary>Text with the part that matches the search drawn in the accent color.</summary>
	public static void Highlighted(SpriteBatch sb, string text, string match, float x, float y, Color color, Color highlight, float scale, float maxWidth)
	{
		text = Fit(text, maxWidth, scale);
		int at = string.IsNullOrEmpty(match) ? -1 : text.IndexOf(match, StringComparison.OrdinalIgnoreCase);
		if (at < 0)
		{
			RawText(sb, text, x, y, color, scale);
			return;
		}
		ChatManager.DrawColorCodedStringWithShadow(sb, Font, new TextSnippet[3]
		{
			new TextSnippet(text.Substring(0, at), color),
			new TextSnippet(text.Substring(at, match.Length), highlight),
			new TextSnippet(text.Substring(at + match.Length), color)
		}, new Vector2(x, y), 0f, Vector2.Zero, new Vector2(scale), out _, -1f, 1.2f);
	}

	/// <summary>Word-wraps the text to the width; returns the lines.</summary>
	public static string[] Wrap(string text, float maxWidth, float scale, int maxLines)
	{
		if (string.IsNullOrEmpty(text))
		{
			return new string[0];
		}
		text = L10n.T(text);
		System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();
		foreach (string paragraph in text.Split('\n'))
		{
			string line = "";
			foreach (string word in paragraph.Split(' '))
			{
				System.Collections.Generic.List<string> pieces = Pieces(word);
				for (int p = 0; p < pieces.Count; p++)
				{
					string next = (line.Length == 0) ? pieces[p] : (line + ((p == 0) ? " " : "") + pieces[p]);
					if (RawMeasure(next, scale) > maxWidth && line.Length > 0)
					{
						lines.Add(line);
						line = pieces[p];
					}
					else
					{
						line = next;
					}
				}
			}
			lines.Add(line);
		}
		if (lines.Count > maxLines)
		{
			lines.RemoveRange(maxLines, lines.Count - maxLines);
			lines[maxLines - 1] = RawFit(lines[maxLines - 1] + " ...", maxWidth, scale);
		}
		return lines.ToArray();
	}

	/// <summary>Characters a Chinese or Japanese line must not start with, or end with.</summary>
	private const string NoLineStart = "、。，．！？）」』】〕〉》：；ー・…ゃゅょっャュョッぁぃぅぇぉァィゥェォ,.!?):;";

	private const string NoLineEnd = "（「『【〔〈《(";

	private static bool IsCjk(char c)
	{
		return (c >= '　' && c <= 'ヿ') || (c >= '㐀' && c <= '䶿') || (c >= '一' && c <= '鿿') || (c >= '豈' && c <= '﫿') || (c >= '＀' && c <= '￯');
	}

	/// <summary>
	/// A word cut where Chinese and Japanese let a line break (they barely use spaces: between their characters, never
	/// before closing punctuation or after an opening bracket). Words without those characters stay whole.
	/// </summary>
	private static System.Collections.Generic.List<string> Pieces(string word)
	{
		System.Collections.Generic.List<string> pieces = new System.Collections.Generic.List<string>();
		int start = 0;
		for (int i = 1; i < word.Length; i++)
		{
			if ((IsCjk(word[i]) || IsCjk(word[i - 1])) && NoLineStart.IndexOf(word[i]) < 0 && NoLineEnd.IndexOf(word[i - 1]) < 0)
			{
				pieces.Add(word.Substring(start, i - start));
				start = i;
			}
		}
		pieces.Add(word.Substring(start));
		return pieces;
	}

	#endregion

	#region Characters

	/// <summary>A player's head (no outline), centered on <paramref name="center"/>. Never changes the player.</summary>
	public static void Head(SpriteBatch sb, Player player, Vector2 center, float scale)
	{
		try
		{
			// The game centers a map head on (x, y) at scale 1; here every layer scales around (x + 4, y + 14) instead.
			Vector2 corner = new Vector2(center.X - 4f + 4f * scale, center.Y - 14f + 9f * scale);
			Main.PlayerRenderer.DrawPlayerHead(Main.Camera, player, corner, 1f, scale, Color.Transparent);
		}
		catch (Exception ex)
		{
			IsaClient.Core.ClientLog.LogException(ex);
		}
		// Dye shaders may have been applied: start the panel's batch again.
		HubInput.Restart(sb, HubInput.Clipping);
	}

	/// <summary>
	/// A whole character standing in the box (the character select look). Only for copies: drawing runs the player's
	/// frame and visual updates.
	/// </summary>
	public static void Portrait(SpriteBatch sb, Player player, Rectangle box, float scale, bool walking = false)
	{
		if (player == null)
		{
			return;
		}
		try
		{
			player.isaAlwaysLit = true;
			player.socialIgnoreLight = true;
			player.direction = 1;
			player.gravDir = 1f;
			player.ResetEffects();
			player.ResetVisibleAccessories();
			player.UpdateMiscCounter();
			player.UpdateDyes();
			player.PlayerFrame();
			player.bodyFrame.Y = (player.legFrame.Y = (player.headFrame.Y = 0));
			if (walking)
			{
				// The walk cycle of the character select screen.
				int frame = (int)(Main.GlobalTimeWrappedHourly / 0.07f) % 14 + 6;
				player.bodyFrame.Y = (player.legFrame.Y = (player.headFrame.Y = frame * 56));
				player.WingFrame(wingFlap: false);
			}
			player.isaAlwaysLit = true;
			int held = player.selectedItem;
			Item heldItem = player.inventory[held];
			player.inventory[held] = new Item();
			sb.End();
			sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, HubInput.Clipping ? new RasterizerState
			{
				CullMode = CullMode.None,
				ScissorTestEnable = true
			} : RasterizerState.CullCounterClockwise, null, HubInput.Matrix);
			// DrawPlayer scales around the bottom middle of the hitbox: place the hitbox so the scaled one is centered.
			Vector2 topLeft = new Vector2(box.Center.X - player.width / 2f, box.Center.Y + player.height * scale / 2f - player.height);
			Main.PlayerRenderer.DrawPlayer(Main.Camera, player, topLeft + Main.screenPosition, 0f, Vector2.Zero, 0f, scale);
			player.inventory[held] = heldItem;
		}
		catch (Exception ex)
		{
			IsaClient.Core.ClientLog.LogException(ex);
		}
		finally
		{
			HubInput.Restart(sb, HubInput.Clipping);
		}
	}

	#endregion

	#region Controls

	/// <summary>Square button with an icon in a slot frame.</summary>
	public static bool IconButton(SpriteBatch sb, string id, Rectangle r, HubIcon icon, string tooltip, bool active = false, bool enabled = true)
	{
		if (!HubInput.Visible(r))
		{
			return false;
		}
		bool hover = enabled && HubInput.In(r);
		bool clicked = hover && HubInput.Hover(id, tooltip);
		float glow = HubInput.Anim(id, hover || active);
		Color back = Color.Lerp(Theme.Card, active ? Theme.Slot : Theme.CardHover, glow);
		Box(sb, r, back * (enabled ? 0.95f : 0.5f));
		int pad = Math.Max(4, r.Height / 5);
		Rectangle area = new Rectangle(r.X + pad, r.Y + pad, r.Width - pad * 2, r.Height - pad * 2);
		area.Inflate((int)(glow * 2f), (int)(glow * 2f));
		icon.Draw(sb, area, Color.White * (enabled ? 1f : 0.4f));
		return clicked;
	}

	/// <summary>An icon with no frame; grows a little under the mouse.</summary>
	public static bool BareIcon(SpriteBatch sb, string id, Rectangle r, HubIcon icon, string tooltip, Color color, float maxScale = 2f)
	{
		if (!HubInput.Visible(r))
		{
			return false;
		}
		bool hover = HubInput.In(r);
		bool clicked = hover && HubInput.Hover(id, tooltip);
		float grow = 1f + 0.15f * HubInput.Anim(id, hover);
		Rectangle area = new Rectangle((int)(r.Center.X - r.Width * grow / 2f), (int)(r.Center.Y - r.Height * grow / 2f), (int)(r.Width * grow), (int)(r.Height * grow));
		icon.Draw(sb, area, hover ? Color.Lerp(color, Color.White, 0.35f) : color, maxScale);
		return clicked;
	}

	public static bool Button(SpriteBatch sb, string id, Rectangle r, string label, Color color, string tooltip = null, bool enabled = true, HubIcon icon = default(HubIcon))
	{
		if (!HubInput.Visible(r))
		{
			return false;
		}
		bool hover = enabled && HubInput.In(r);
		bool clicked = hover && HubInput.Hover(id, tooltip);
		float glow = HubInput.Anim(id, hover);
		Color back = enabled ? Color.Lerp(color, Color.White, 0.2f * glow) : (color * 0.45f);
		Box(sb, r, back * 0.95f);
		Color text = enabled ? Color.Lerp(new Color(230, 234, 245), Color.White, glow) : Theme.Muted;
		if (icon.IsEmpty)
		{
			TextCentered(sb, label, r, text, 0.78f);
		}
		else
		{
			float width = Measure(label, 0.78f) + 22f;
			float x = r.X + Math.Max(6f, (r.Width - width) / 2f);
			icon.Draw(sb, new Rectangle((int)x, r.Y + (r.Height - 18) / 2, 18, 18), Color.White * (enabled ? 1f : 0.5f));
			Text(sb, label, x + 22f, r.Y + (r.Height - 18) / 2f + 2f, text, 0.78f, r.Right - x - 26f);
		}
		return clicked;
	}

	/// <summary>An on/off switch: a track with a sliding knob (no click handling).</summary>
	public static void Switch(SpriteBatch sb, string id, Rectangle r, bool on, bool hover, bool enabled = true)
	{
		float t = HubInput.Anim(id + ":knob", on, 0.28f);
		Color track = Color.Lerp(Theme.Off, Theme.On * 0.85f, t);
		if (!enabled)
		{
			track *= 0.5f;
		}
		Box(sb, r, Color.Lerp(track, Color.White, hover ? 0.12f : 0f));
		int knob = r.Height - 6;
		int x = (int)MathHelper.Lerp(r.X + 3, r.Right - 3 - knob, t);
		Rectangle k = new Rectangle(x, r.Y + 3, knob, knob);
		Slot(sb, k, Color.Lerp(new Color(200, 205, 225), Color.White, t) * (enabled ? 1f : 0.6f));
		if (t > 0.5f)
		{
			Fill(sb, new Rectangle(k.X + knob / 2 - 1, k.Y + knob / 2 - 1, 3, 3), Theme.On * ((t - 0.5f) * 2f));
		}
	}

	/// <summary>Terraria's slider (the volume bar of the settings); returns the new value while dragged, else null.</summary>
	public static float? Slider(SpriteBatch sb, string id, Rectangle r, float value, bool enabled, ref string dragging, Func<float, Color> gradient = null)
	{
		value = MathHelper.Clamp(value, 0f, 1f);
		bool hover = enabled && HubInput.In(new Rectangle(r.X - 4, r.Y - 6, r.Width + 8, r.Height + 12));
		if (hover)
		{
			HubInput.Hover(id, null);
		}
		if (hover && HubInput.Clicked)
		{
			dragging = id;
		}
		bool active = dragging == id;
		Texture2D bar = TextureAssets.ColorBar.Value;
		// Ends of the bar kept, middle stretched.
		int cap = 5;
		sb.Draw(bar, new Rectangle(r.X, r.Y, cap, r.Height), new Rectangle(0, 0, cap, bar.Height), Color.White);
		sb.Draw(bar, new Rectangle(r.X + cap, r.Y, r.Width - cap * 2, r.Height), new Rectangle(cap, 0, bar.Width - cap * 2, bar.Height), Color.White);
		sb.Draw(bar, new Rectangle(r.Right - cap, r.Y, cap, r.Height), new Rectangle(bar.Width - cap, 0, cap, bar.Height), Color.White);
		Rectangle inner = new Rectangle(r.X + cap, r.Y + 4, r.Width - cap * 2, Math.Max(2, r.Height - 8));
		Fill(sb, inner, new Color(20, 24, 40));
		if (gradient != null)
		{
			// The whole bar shows what each position gives (color picker bars).
			for (int i = 0; i < inner.Width; i += 2)
			{
				Fill(sb, new Rectangle(inner.X + i, inner.Y, Math.Min(2, inner.Width - i), inner.Height), gradient((float)i / Math.Max(1, inner.Width - 1)));
			}
		}
		int filled = (gradient != null) ? 0 : (int)(inner.Width * value);
		Color fill = enabled ? Theme.Accent : Theme.Muted;
		for (int i = 0; i < filled; i += 2)
		{
			float p = (float)i / Math.Max(1, inner.Width);
			Fill(sb, new Rectangle(inner.X + i, inner.Y, Math.Min(2, filled - i), inner.Height), Color.Lerp(fill * 0.55f, fill, p));
		}
		if (hover || active)
		{
			sb.Draw(TextureAssets.ColorHighlight.Value, new Rectangle(r.X, r.Y, r.Width, r.Height), Main.OurFavoriteColor * 0.8f);
		}
		Texture2D knob = TextureAssets.ColorSlider.Value;
		sb.Draw(knob, new Vector2(inner.X + inner.Width * value, inner.Center.Y), null, Color.White, 0f, new Vector2(knob.Width, knob.Height) / 2f, 1f, SpriteEffects.None, 0f);
		if (active)
		{
			if (!HubInput.Down)
			{
				dragging = null;
				return null;
			}
			HubInput.Consume();
			return MathHelper.Clamp((HubInput.Mouse.X - inner.X) / (float)Math.Max(1, inner.Width), 0f, 1f);
		}
		return null;
	}

	/// <summary>A text field; returns the (possibly changed) value. <paramref name="secret"/> shows dots instead of the text unless it is being typed in.</summary>
	public static string Field(SpriteBatch sb, string id, Rectangle r, string value, string placeholder, int maxLength, HubIcon icon = default(HubIcon), bool secret = false, bool clearButton = true)
	{
		bool focused = HubInput.Focus == id;
		if (HubInput.Visible(r))
		{
			bool hover = HubInput.In(r);
			if (hover && HubInput.Hover("field:" + id) && !focused)
			{
				HubInput.SetFocus(id);
				focused = true;
			}
			float glow = HubInput.Anim("field:" + id, focused || hover);
			Box(sb, r, Color.Lerp(Theme.Sidebar, Theme.CardHover, glow * 0.6f) * 0.98f);
			if (focused)
			{
				Fill(sb, new Rectangle(r.X + 8, r.Bottom - 4, r.Width - 16, 2), Theme.Accent * 0.8f);
			}
		}
		if (focused)
		{
			value = HubInput.TypeInto(value, maxLength);
			focused = HubInput.Focus == id;
		}
		if (HubInput.Visible(r))
		{
			float left = r.X + 10;
			if (!icon.IsEmpty)
			{
				icon.Draw(sb, new Rectangle(r.X + 8, r.Y + (r.Height - 18) / 2, 18, 18), Color.White * 0.9f);
				left = r.X + 32;
			}
			float room = r.Right - left - (clearButton ? 28 : 8);
			bool empty = string.IsNullOrEmpty(value);
			string shown = empty ? L10n.T(placeholder) : ((secret && !focused) ? new string('•', Math.Min(value.Length, 24)) : value);
			while (!empty && shown.Length > 0 && RawMeasure(shown + "|", 0.8f) > room)
			{
				shown = shown.Substring(1);
			}
			Color color = empty ? Theme.Muted * 0.9f : Color.White;
			if (focused && Main.GameUpdateCount / 30 % 2 == 0)
			{
				shown = (empty ? "" : shown) + "|";
				color = Color.White;
			}
			RawText(sb, shown, left, r.Y + (r.Height - 18) / 2f + 2f, color, 0.8f, empty && !focused ? room : 0f);
			if (clearButton && !empty && BareIcon(sb, "clear:" + id, new Rectangle(r.Right - 24, r.Y + (r.Height - 16) / 2, 16, 16), HubIcon.Texture("Images/UI/SearchCancel"), "Clear", Color.White))
			{
				value = "";
				Main.clrInput();
			}
		}
		return value;
	}

	/// <summary>A small rounded label (counts, tags).</summary>
	public static Rectangle Chip(SpriteBatch sb, string text, int x, int y, Color back, Color color, float scale = 0.7f, bool alignRight = false)
	{
		text = L10n.T(text);
		Vector2 size = RawSize(text, scale);
		int h = 20;
		int w = Math.Max(h + 4, (int)size.X + 14);
		Rectangle r = new Rectangle(alignRight ? (x - w) : x, y, w, h);
		Box(sb, r, back);
		RawText(sb, text, r.X + (w - size.X) / 2f, r.Y + (h - size.Y) / 2f + 3f * scale, color, scale);
		return r;
	}

	/// <summary>Terraria's scrollbar on the right of an area; returns the new offset.</summary>
	public static float Scrollbar(SpriteBatch sb, string id, Rectangle track, float offset, float content, float view, ref string dragging, ref float dragFrom)
	{
		float max = Math.Max(0f, content - view);
		if (max <= 0f)
		{
			return 0f;
		}
		DrawBar(sb, Tex("Images/UI/Scrollbar"), track, Color.White);
		int thumbH = Math.Max(24, (int)(track.Height * (view / content)));
		int thumbY = track.Y + (int)((track.Height - thumbH) * (offset / max));
		Rectangle thumb = new Rectangle(track.X + 2, thumbY, track.Width - 4, thumbH);
		bool hover = HubInput.In(new Rectangle(track.X - 2, track.Y, track.Width + 4, track.Height));
		if (hover)
		{
			HubInput.Hover(id);
			if (HubInput.Clicked)
			{
				dragging = id;
				dragFrom = thumb.Contains(HubInput.Mouse) ? (HubInput.Mouse.Y - thumbY) : (thumbH / 2f);
			}
		}
		if (dragging == id)
		{
			if (!HubInput.Down)
			{
				dragging = null;
			}
			else
			{
				float y = HubInput.Mouse.Y - dragFrom - track.Y;
				offset = MathHelper.Clamp(y / Math.Max(1f, track.Height - thumbH), 0f, 1f) * max;
				HubInput.Consume();
			}
		}
		DrawBar(sb, Tex("Images/UI/ScrollbarInner"), thumb, Color.White * ((hover || dragging == id) ? 1f : 0.85f));
		return offset;
	}

	private static void DrawBar(SpriteBatch sb, Texture2D texture, Rectangle r, Color color)
	{
		sb.Draw(texture, new Rectangle(r.X, r.Y - 6, r.Width, 6), new Rectangle(0, 0, texture.Width, 6), color);
		sb.Draw(texture, new Rectangle(r.X, r.Y, r.Width, r.Height), new Rectangle(0, 6, texture.Width, 4), color);
		sb.Draw(texture, new Rectangle(r.X, r.Y + r.Height, r.Width, 6), new Rectangle(0, texture.Height - 6, texture.Width, 6), color);
	}

	#endregion
}
