using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.UI.Chat;

using IsaClient.Languages;

namespace IsaClient.Music;

/// <summary>Drawing helpers of the music windows.</summary>
public static class MusicDrawing
{
	private static DynamicSpriteFont Font => FontAssets.MouseText.Value;

	internal static Texture2D Tex(string path)
	{
		return Main.Assets.Request<Texture2D>(path, AssetRequestMode.ImmediateLoad).Value;
	}

	private static Texture2D Pixel => TextureAssets.MagicPixel.Value;

	// Texts go through the player's language (IsaClient.Languages.L10n) where they come in; the Raw versions work on
	// text already translated (or typed by the player).

	internal static float Measure(string text, float scale)
	{
		return RawMeasure(L10n.T(text), scale);
	}

	private static float RawMeasure(string text, float scale)
	{
		return ChatManager.GetStringSize(Font, text ?? "", new Vector2(scale)).X;
	}

	private static string Fit(string text, float maxWidth, float scale)
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

	/// <summary>Like Fit, for DrawBorderStringBig (a much wider font than the one Fit measures).</summary>
	internal static string FitBig(string text, float maxWidth, float scale)
	{
		DynamicSpriteFont big = FontAssets.DeathText.Value;
		text = L10n.T(text) ?? "";
		if (big.MeasureString(text).X * scale <= maxWidth)
		{
			return text;
		}
		int lo = 0;
		int hi = text.Length;
		while (lo < hi)
		{
			int mid = (lo + hi + 1) / 2;
			if (big.MeasureString(text.Substring(0, mid) + "...").X * scale <= maxWidth)
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

	/// <summary>A big title that shrinks a little before it gets cut with "...".</summary>
	internal static void BigTitle(SpriteBatch sb, string text, Vector2 pos, float maxWidth, float scale, float minScale, Color color)
	{
		DynamicSpriteFont big = FontAssets.DeathText.Value;
		text = L10n.T(text) ?? "";
		maxWidth -= 6f;
		float width = big.MeasureString(text).X;
		if (width * scale > maxWidth)
		{
			scale = Math.Max(minScale, maxWidth / Math.Max(1f, width));
		}
		Utils.DrawBorderStringBig(sb, FitBig(text, maxWidth, scale), pos + new Vector2(0f, (0.55f - scale) * 30f), color, scale);
	}

	/// <summary>Plain text (no chat tags: song titles may contain brackets), top-left at x, y.</summary>
	internal static void Text(SpriteBatch sb, string text, float x, float y, Color color, float scale = 0.8f, float maxWidth = 0f)
	{
		RawText(sb, L10n.T(text), x, y, color, scale, maxWidth);
	}

	/// <summary>Text drawn as it is (what the player typed, text already translated).</summary>
	internal static void RawText(SpriteBatch sb, string text, float x, float y, Color color, float scale = 0.8f, float maxWidth = 0f)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		if (maxWidth > 0f)
		{
			text = Fit(text, maxWidth, scale);
		}
		ChatManager.DrawColorCodedStringWithShadow(sb, Font, new TextSnippet[1]
		{
			new TextSnippet(text, color)
		}, new Vector2(x, y), 0f, Vector2.Zero, new Vector2(scale), out _, -1f, 1.2f);
	}

	private static void TextCentered(SpriteBatch sb, string text, Rectangle r, Color color, float scale = 0.8f)
	{
		text = Fit(L10n.T(text), r.Width - 6, scale);
		Vector2 size = ChatManager.GetStringSize(Font, text, new Vector2(scale));
		RawText(sb, text, r.X + (r.Width - size.X) / 2f, r.Y + (r.Height - size.Y) / 2f + 3f * scale, color, scale);
	}

	internal static void Box(SpriteBatch sb, Rectangle r, Color c)
	{
		Utils.DrawInvBG(sb, r, c);
	}

	internal static void Fill(SpriteBatch sb, Rectangle r, Color c)
	{
		sb.Draw(Pixel, r, c);
	}

	internal static void Icon(SpriteBatch sb, Texture2D tex, Rectangle? source, Rectangle area, Color color, float maxScale = 2f)
	{
		Rectangle src = source ?? tex.Bounds;
		float scale = Math.Min(Math.Min((float)area.Width / src.Width, (float)area.Height / src.Height), maxScale);
		Vector2 size = new Vector2(src.Width, src.Height) * scale;
		Vector2 pos = new Vector2(area.X + (area.Width - size.X) / 2f, area.Y + (area.Height - size.Y) / 2f);
		sb.Draw(tex, pos, src, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
	}

	internal static void Hover(string id, string tooltip = null)
	{
		MusicPlayerWindow.HoveredControl = id;
		if (tooltip != null)
		{
			MusicPlayerWindow.PendingTooltip = L10n.T(tooltip);
		}
		if (Main.LocalPlayer != null)
		{
			Main.LocalPlayer.mouseInterface = true;
		}
	}

	/// <summary>Square icon button (Terraria inventory frame); returns true on click.</summary>
	internal static bool IconButton(SpriteBatch sb, string id, Rectangle r, Texture2D tex, Rectangle? src, string tooltip, bool active = false, bool enabled = true, Color? tint = null)
	{
		if (!MusicPlayerWindow.Visible(r))
		{
			return false;
		}
		bool hover = enabled && MusicPlayerWindow.MouseIn(r);
		if (hover)
		{
			Hover(id, tooltip);
		}
		Color back = active ? MusicPlayerWindow.SelectedColor : (hover ? MusicPlayerWindow.RowHoverColor : MusicPlayerWindow.PanelColor);
		Box(sb, r, back * (enabled ? 0.95f : 0.5f));
		Color color = tint ?? Color.White;
		if (!enabled)
		{
			color *= 0.4f;
		}
		else if (hover)
		{
			color = Color.Lerp(color, Color.White, 0.3f);
		}
		int pad = Math.Max(4, r.Height / 5);
		Icon(sb, tex, src, new Rectangle(r.X + pad, r.Y + pad, r.Width - pad * 2, r.Height - pad * 2), color);
		return hover && MusicPlayerWindow.Clicked;
	}

	/// <summary>Icon with no frame (heart, close...); returns true on click.</summary>
	internal static bool BareIcon(SpriteBatch sb, string id, Rectangle r, Texture2D tex, Rectangle? src, string tooltip, Color color)
	{
		if (!MusicPlayerWindow.Visible(r))
		{
			return false;
		}
		bool hover = MusicPlayerWindow.MouseIn(r);
		if (hover)
		{
			Hover(id, tooltip);
			color = Color.Lerp(color, Color.White, 0.35f);
		}
		float grow = hover ? 1.15f : 1f;
		Rectangle area = new Rectangle((int)(r.Center.X - r.Width * grow / 2f), (int)(r.Center.Y - r.Height * grow / 2f), (int)(r.Width * grow), (int)(r.Height * grow));
		Icon(sb, tex, src, area, color, 3f);
		return hover && MusicPlayerWindow.Clicked;
	}

	internal static bool TextButton(SpriteBatch sb, string id, Rectangle r, string label, Color color, string tooltip = null, bool enabled = true)
	{
		if (!MusicPlayerWindow.Visible(r))
		{
			return false;
		}
		bool hover = enabled && MusicPlayerWindow.MouseIn(r);
		if (hover)
		{
			Hover(id, tooltip);
		}
		Box(sb, r, (enabled ? (hover ? Color.Lerp(color, Color.White, 0.18f) : color) : (color * 0.45f)) * 0.95f);
		TextCentered(sb, label, r, enabled ? (hover ? Color.White : new Color(230, 234, 245)) : MusicPlayerWindow.MutedColor, 0.78f);
		return hover && MusicPlayerWindow.Clicked;
	}

	internal static string Field(SpriteBatch sb, string id, Rectangle r, string value, string placeholder, int maxLength, bool searchIcon)
	{
		bool focused = MusicPlayerWindow.FocusedField == id;
		if (MusicPlayerWindow.Visible(r))
		{
			bool hover = MusicPlayerWindow.MouseIn(r);
			if (hover)
			{
				Hover("field:" + id);
				if (MusicPlayerWindow.Clicked && !focused)
				{
					MusicPlayerWindow.FocusedField = id;
					focused = true;
					Main.clrInput();
				}
			}
			Box(sb, r, focused ? new Color(40, 55, 110) : (MusicPlayerWindow.PanelDarkColor * 0.95f));
		}
		if (focused)
		{
			PlayerInput.WritingText = true;
			Main.CurrentInputTextTakerOverride = MusicPlayerWindow.InputMarker;
			Main.chatRelease = false;
			Main.instance.HandleIME();
			string typed = Main.GetInputText(value ?? "");
			if (typed.Length > maxLength)
			{
				typed = typed.Substring(0, maxLength);
			}
			value = typed;
			if (Main.inputTextEscape)
			{
				Main.inputTextEscape = false;
				MusicPlayerWindow.FocusedField = null;
				focused = false;
			}
		}
		if (MusicPlayerWindow.Visible(r))
		{
			float left = r.X + 8;
			if (searchIcon)
			{
				Icon(sb, Tex("Images/UI/Cursor_2"), null, new Rectangle(r.X + 6, r.Y + (r.Height - 18) / 2, 18, 18), Color.White);
				left = r.X + 30;
			}
			float room = r.Right - left - 30;
			string shown = string.IsNullOrEmpty(value) ? L10n.T(placeholder) : value;
			Color color = string.IsNullOrEmpty(value) ? new Color(120, 130, 160) : Color.White;
			while (shown.Length > 0 && RawMeasure(shown + "|", 0.8f) > room)
			{
				shown = shown.Substring(1);
			}
			if (focused && Main.GameUpdateCount / 30 % 2 == 0)
			{
				shown = (string.IsNullOrEmpty(value) ? "" : shown) + "|";
				color = Color.White;
			}
			RawText(sb, shown, left, r.Y + (r.Height - 18) / 2f + 1f, color, 0.8f);
			if (!string.IsNullOrEmpty(value) && BareIcon(sb, "clear:" + id, new Rectangle(r.Right - 24, r.Y + (r.Height - 18) / 2, 18, 18), Tex("Images/UI/SearchCancel"), null, "Clear", Color.White))
			{
				value = "";
				Main.clrInput();
			}
		}
		return value;
	}

	/// <summary>A song's cover: its achievement icon in the achievement frame.</summary>
	internal static void Cover(SpriteBatch sb, string key, Rectangle r, float alpha = 1f)
	{
		if (!MusicPlayerWindow.Visible(r))
		{
			return;
		}
		int index = MusicLibrary.IconFor(key ?? "?");
		Texture2D sheet = Tex("Images/UI/Achievements");
		Rectangle frame = new Rectangle(index % 8 * 66, index / 8 * 66, 64, 64);
		if (frame.Bottom > sheet.Height)
		{
			frame = new Rectangle(0, 0, 64, 64);
		}
		int inset = Math.Max(2, r.Width / 14);
		sb.Draw(sheet, new Rectangle(r.X + inset, r.Y + inset, r.Width - inset * 2, r.Height - inset * 2), frame, Color.White * alpha);
		sb.Draw(Tex("Images/UI/Achievement_Borders"), r, Color.White * alpha);
	}

	internal static void ProgressBar(SpriteBatch sb, Rectangle r, float value, Color fill, bool hover)
	{
		Fill(sb, r, new Color(15, 20, 40) * 0.9f);
		Fill(sb, new Rectangle(r.X, r.Y, (int)(r.Width * MathHelper.Clamp(value, 0f, 1f)), r.Height), fill);
		if (hover)
		{
			Fill(sb, new Rectangle(r.X, r.Y, r.Width, 1), Color.White * 0.25f);
		}
		int knobX = r.X + (int)(r.Width * MathHelper.Clamp(value, 0f, 1f));
		Fill(sb, new Rectangle(knobX - 2, r.Y - 3, 5, r.Height + 6), hover ? Color.White : new Color(230, 230, 240));
	}

	internal static float BeginScroll(string key, Rectangle area)
	{
		if (!MusicPlayerWindow.ScrollOffsets.TryGetValue(key, out var value))
		{
			value = 0f;
		}
		float content = MusicPlayerWindow.ContentHeights.TryGetValue(key, out var h) ? h : 0f;
		if (MusicPlayerWindow.WheelDelta != 0 && area.Contains(MusicPlayerWindow.MousePoint) && MusicPlayerWindow.MenuEntries == null)
		{
			value -= MusicPlayerWindow.WheelDelta / 2.5f;
			MusicPlayerWindow.WheelDelta = 0;
		}
		value = MathHelper.Clamp(value, 0f, Math.Max(0f, content - area.Height));
		MusicPlayerWindow.ScrollOffsets[key] = value;
		return value;
	}

	internal static void EndScroll(SpriteBatch sb, string key, Rectangle area, float height)
	{
		MusicPlayerWindow.ContentHeights[key] = height;
		if (height <= area.Height + 1f)
		{
			return;
		}
		Rectangle track = new Rectangle(area.Right - 6, area.Y + 2, 4, area.Height - 4);
		Fill(sb, track, new Color(15, 20, 40) * 0.7f);
		float ratio = area.Height / height;
		int thumbH = Math.Max(20, (int)(track.Height * ratio));
		float max = height - area.Height;
		int thumbY = track.Y + (int)((track.Height - thumbH) * (MusicPlayerWindow.ScrollOffsets[key] / Math.Max(1f, max)));
		Fill(sb, new Rectangle(track.X, thumbY, track.Width, thumbH), new Color(150, 170, 230));
	}
}
