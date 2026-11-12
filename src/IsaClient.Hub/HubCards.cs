using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace IsaClient.Hub;

/// <summary>
/// One feature card: its icon in an inventory slot, the name, a short line, and its control on the right (switch,
/// value with arrows, button, slider or value). Left click uses it, right click goes back (values) or opens the menu,
/// the star makes it a favorite.
/// </summary>
internal static class HubCards
{
	private static HubTheme Theme => HubTheme.Current;

	public static void Draw(SpriteBatch sb, HubFeature feature, Rectangle r, string match, bool showWhere)
	{
		string id = "card:" + feature.Id;
		FeatureKind kind = feature.Kind;
		if (kind == FeatureKind.None)
		{
			return;
		}
		bool disabled = feature.Disabled;
		bool on = (kind == FeatureKind.Toggle || kind == FeatureKind.State) && feature.IsOn;
		bool hover = HubInput.In(r);
		bool favorite = HubPrefs.IsFavorite(feature);
		float glow = HubInput.Anim(id, hover);
		float lit = HubInput.Anim(id + ":on", on);
		float opacity = HubPrefs.Opacity;

		Color back = Color.Lerp(Theme.Card, Theme.CardHover, glow * 0.8f);
		back = Color.Lerp(back, Color.Lerp(Theme.Card, Theme.On, 0.35f), lit * 0.45f);
		HubKit.Box(sb, r, back * opacity * (disabled ? 0.7f : 1f));
		if (lit > 0.01f)
		{
			HubKit.Fill(sb, new Rectangle(r.X + 3, r.Y + 8, 3, r.Height - 16), Theme.On * lit);
		}

		// Icon
		int size = Math.Min(r.Height - 12, 40);
		Rectangle slot = new Rectangle(r.X + 8, r.Y + (r.Height - size) / 2, size, size);
		HubKit.Slot(sb, slot, Color.Lerp(Theme.Slot, Theme.On, lit * 0.35f) * 0.95f);
		Rectangle inner = new Rectangle(slot.X + 5, slot.Y + 5, slot.Width - 10, slot.Height - 10);
		float bob = hover ? (float)Math.Sin(Main.GlobalTimeWrappedHourly * 5f) * 1.5f : 0f;
		inner.Y += (int)bob;
		feature.Icon.Draw(sb, inner, Color.White * (disabled ? 0.45f : 1f), 2f, hover ? (float)Math.Sin(Main.GlobalTimeWrappedHourly * 3f) * 0.06f : 0f);

		// Control area on the right
		bool missing;
		string value = feature.ValueText(out missing);
		bool valueLike = kind == FeatureKind.Choice || (kind == FeatureKind.Action && value != null);
		int controlW = 0;
		Rectangle control = Rectangle.Empty;
		switch (kind)
		{
		case FeatureKind.Toggle:
		case FeatureKind.State:
			controlW = 50;
			control = new Rectangle(r.Right - 56, r.Y + (r.Height - 22) / 2, 46, 22);
			break;
		case FeatureKind.Info:
			controlW = (int)Math.Min(HubKit.RichSize(value ?? "", 0.78f).X + 16f, r.Width * 0.55f);
			control = new Rectangle(r.Right - 8 - controlW, r.Y + (r.Height - 24) / 2, controlW, 24);
			break;
		case FeatureKind.Choice:
		case FeatureKind.Action:
			if (valueLike)
			{
				controlW = (int)MathHelper.Clamp(HubKit.RichSize(value ?? "", 0.76f).X + 50f, 96f, r.Width * 0.52f);
			}
			else
			{
				string verb = ClientHub.Confirming(feature) ? "Click again" : (feature.Verb ?? "Run");
				controlW = (int)Math.Max(70f, HubKit.Measure(verb, 0.76f) + 30f);
			}
			control = new Rectangle(r.Right - 8 - controlW, r.Y + (r.Height - 28) / 2, controlW, 28);
			break;
		case FeatureKind.Color:
			controlW = (int)MathHelper.Clamp(HubKit.RichSize(value ?? "", 0.76f).X + 52f, 110f, r.Width * 0.52f);
			control = new Rectangle(r.Right - 8 - controlW, r.Y + (r.Height - 28) / 2, controlW, 28);
			break;
		}
		Rectangle companion = Rectangle.Empty;
		if (feature.Companion != null)
		{
			string verb = ClientHub.Confirming(feature.Companion) ? "Click again" : (feature.Companion.Verb ?? "Run");
			int w = (int)Math.Max(76f, HubKit.Measure(verb, 0.76f) + 30f);
			companion = new Rectangle((control.Width > 0 ? control.X : r.Right - 8) - 8 - w, r.Y + (r.Height - 28) / 2, w, 28);
		}

		// Card hover first (tooltip), then the parts on it.
		if (hover)
		{
			string tooltip = feature.Tooltip;
			if (disabled)
			{
				tooltip = (tooltip == null) ? "Not available right now." : (tooltip + "\nNot available right now.");
			}
			HubInput.Hover(id, tooltip);
		}
		bool clickedCard = hover && HubInput.Clicked;
		bool rightClickedCard = hover && HubInput.RightClicked;

		// Text
		int textX = slot.Right + 10;
		int textRight = ((companion.Width > 0) ? companion.X : ((control.Width > 0) ? control.X : r.Right - 10)) - 8;
		if (kind == FeatureKind.Slider)
		{
			textRight = r.Right - 12;
		}
		bool twoLines = !HubPrefs.Compact && (HubPrefs.ShowDescriptions || kind == FeatureKind.Slider);
		float titleY = twoLines ? (r.Y + 8) : (r.Y + (r.Height - 20) / 2f + 2f);
		float titleRoom = textRight - textX - 22;
		if (kind == FeatureKind.Slider && value != null)
		{
			titleRoom -= HubKit.RichSize(value, 0.78f).X + 10f;
		}
		Color titleColor = disabled ? Theme.Muted : Color.Lerp(new Color(232, 236, 248), Color.White, glow);
		HubKit.Highlighted(sb, feature.Title, match, textX, titleY, titleColor, Theme.Accent, 0.86f, Math.Max(20f, titleRoom));
		float titleW = Math.Min(HubKit.Measure(HubKit.Fit(feature.Title, Math.Max(20f, titleRoom), 0.86f), 0.86f), titleRoom);

		// Favorite star next to the title
		Rectangle star = new Rectangle((int)(textX + titleW + 6), (int)titleY, 16, 16);
		bool starHover = HubInput.In(new Rectangle(star.X - 2, star.Y - 2, star.Width + 4, star.Height + 4));
		if (favorite || hover)
		{
			float alpha = favorite ? 1f : 0.55f;
			HubIcon.Texture(favorite ? "Images/UI/ButtonFavoriteActive" : "Images/UI/ButtonFavoriteInactive").Draw(sb, star, Color.White * (starHover ? 1f : alpha), 1f);
		}
		if (starHover)
		{
			HubInput.Hover(id + ":star", favorite ? "Remove from favorites" : "Add to favorites (shows on Home)");
			if (clickedCard)
			{
				HubPrefs.ToggleFavorite(feature);
				HubInput.Sound(favorite ? 11 : 10);
				clickedCard = false;
			}
		}

		if (twoLines && kind != FeatureKind.Slider)
		{
			string line = showWhere ? (feature.Group.Title + (feature.Section.Pages.Count > 1 ? ("  ·  " + PageOf(feature)) : "")) : feature.Description;
			if (!string.IsNullOrEmpty(line))
			{
				// Up to two lines; the hover box has the whole text.
				string[] lines = HubKit.Wrap(line, textRight - textX, 0.7f, 2);
				for (int i = 0; i < lines.Length; i++)
				{
					HubKit.Text(sb, lines[i], textX, r.Y + 29 + i * 16, showWhere ? (feature.Section.Accent * 0.9f) : (Theme.Muted * 0.95f), 0.7f);
				}
			}
		}

		// Control
		switch (kind)
		{
		case FeatureKind.Toggle:
		case FeatureKind.State:
			HubKit.Switch(sb, id, control, on, hover, kind == FeatureKind.Toggle && !disabled);
			if (kind == FeatureKind.Toggle && clickedCard && !companion.Contains(HubInput.Mouse))
			{
				ClientHub.Activate(feature);
			}
			break;
		case FeatureKind.Info:
			if (!string.IsNullOrEmpty(value))
			{
				Vector2 size2 = HubKit.RichSize(value, 0.78f);
				HubKit.RichText(sb, value, control.Right - size2.X - 4, control.Y + (control.Height - size2.Y) / 2f + 3f, missing ? (Theme.Muted * 0.7f) : Theme.Accent, 0.78f, control.Width);
			}
			break;
		case FeatureKind.Slider:
		{
			if (value != null)
			{
				Vector2 size2 = HubKit.RichSize(value, 0.78f);
				HubKit.RichText(sb, value, r.Right - 12 - size2.X, titleY, Theme.Accent, 0.78f);
			}
			Rectangle bar = new Rectangle(textX, r.Bottom - (HubPrefs.Compact ? 18 : 22), r.Right - 16 - textX, 16);
			float? slid = HubKit.Slider(sb, id + ":slider", bar, feature.Slider, !disabled, ref ClientHub.SliderDrag);
			if (slid.HasValue && !disabled)
			{
				feature.Slide(slid.Value);
				HubPrefs.Used(feature);
			}
			break;
		}
		case FeatureKind.Choice:
		case FeatureKind.Action:
			if (valueLike)
			{
				DrawValueBox(sb, id, control, value, missing, feature.HasSecondary, hover, disabled);
				if (clickedCard && !companion.Contains(HubInput.Mouse))
				{
					ClientHub.Activate(feature);
				}
				if (rightClickedCard && !slot.Contains(HubInput.Mouse) && feature.HasSecondary)
				{
					ClientHub.ActivateSecondary(feature);
					rightClickedCard = false;
				}
			}
			else
			{
				bool confirming = ClientHub.Confirming(feature);
				string verb = confirming ? "Click again" : (feature.Verb ?? "Run");
				Color color = confirming ? (Theme.Danger * 0.85f) : Color.Lerp(Theme.Slot, feature.Section?.Accent ?? Theme.Accent, 0.25f);
				if (HubKit.Button(sb, id + ":run", control, verb, color, null, !disabled))
				{
					ClientHub.Activate(feature);
				}
				if (rightClickedCard && control.Contains(HubInput.Mouse) && feature.HasSecondary)
				{
					ClientHub.ActivateSecondary(feature);
					rightClickedCard = false;
				}
			}
			break;
		case FeatureKind.Color:
			DrawColorBox(sb, id, control, feature, value, hover, disabled);
			if (clickedCard && !slot.Contains(HubInput.Mouse))
			{
				ClientHub.Activate(feature);
			}
			break;
		}
		if (feature.Companion != null)
		{
			HubFeature second = feature.Companion;
			bool confirming = ClientHub.Confirming(second);
			string verb = confirming ? "Click again" : (second.Verb ?? "Run");
			if (HubKit.Button(sb, id + ":second", companion, verb, confirming ? (Theme.Danger * 0.85f) : Color.Lerp(Theme.Slot, Theme.Danger, 0.35f), second.Tooltip, !second.Disabled))
			{
				ClientHub.Activate(second);
			}
		}
		if (rightClickedCard)
		{
			ClientHub.ShowFeatureMenu(feature);
			HubInput.Consume();
		}
	}

	private static string PageOf(HubFeature feature)
	{
		foreach (HubPage page in feature.Section.Pages)
		{
			if (page.Groups.Contains(feature.Group))
			{
				return page.Title;
			}
		}
		return "";
	}

	/// <summary>A swatch of the feature's color and its name; clicking the card opens the picker.</summary>
	private static void DrawColorBox(SpriteBatch sb, string id, Rectangle r, HubFeature feature, string value, bool hover, bool disabled)
	{
		float glow = HubInput.Anim(id + ":value", hover);
		HubKit.Box(sb, r, Color.Lerp(Theme.Sidebar, Theme.Slot, 0.3f + 0.3f * glow) * HubPrefs.Opacity);
		Color color = Color.White;
		try
		{
			color = feature.GetColor?.Invoke() ?? Color.White;
		}
		catch
		{
		}
		Rectangle swatch = new Rectangle(r.X + 6, r.Y + 5, 18, r.Height - 10);
		HubKit.Fill(sb, new Rectangle(swatch.X - 1, swatch.Y - 1, swatch.Width + 2, swatch.Height + 2), Color.Black * 0.8f);
		HubKit.Fill(sb, swatch, color * (disabled ? 0.5f : 1f));
		HubIcon.Texture("Images/UI/IconNext").Draw(sb, new Rectangle(r.Right - 17, r.Y + (r.Height - 14) / 2, 12, 14), disabled ? (Theme.Muted * 0.5f) : Color.Lerp(Theme.Muted, Color.White, glow), 1f);
		string text = value ?? ("#" + IsaClient.Core.NamedColors.Hex(color));
		float left = swatch.Right + 8;
		float room = r.Right - 20 - left;
		// Never wrapped (a hex code broke in two lines): a little smaller when it doesn't fit.
		float scale = 0.76f;
		Vector2 size = Terraria.UI.Chat.ChatManager.GetStringSize(Terraria.GameContent.FontAssets.MouseText.Value, IsaClient.Languages.L10n.T(text), new Vector2(scale));
		if (size.X > room && size.X > 0f)
		{
			scale *= room / size.X;
			size *= room / size.X;
		}
		HubKit.RichText(sb, text, left + Math.Max(0f, (room - size.X) / 2f), r.Y + (r.Height - size.Y) / 2f + 3f, disabled ? Theme.Muted : Color.White, scale);
	}

	/// <summary>A value between two arrows (the arrows show that right click goes back).</summary>
	private static void DrawValueBox(SpriteBatch sb, string id, Rectangle r, string value, bool missing, bool canGoBack, bool hover, bool disabled)
	{
		float glow = HubInput.Anim(id + ":value", hover);
		HubKit.Box(sb, r, Color.Lerp(Theme.Sidebar, Theme.Slot, 0.3f + 0.3f * glow) * HubPrefs.Opacity);
		Color arrow = disabled ? (Theme.Muted * 0.5f) : Color.Lerp(Theme.Muted, Color.White, glow);
		int left = r.X + 6;
		int right = r.Right - 6;
		if (canGoBack)
		{
			HubIcon.Texture("Images/UI/IconPrev").Draw(sb, new Rectangle(r.X + 5, r.Y + (r.Height - 14) / 2, 12, 14), arrow, 1f);
			left = r.X + 20;
		}
		HubIcon.Texture("Images/UI/IconNext").Draw(sb, new Rectangle(r.Right - 17, r.Y + (r.Height - 14) / 2, 12, 14), arrow, 1f);
		right = r.Right - 20;
		string text = value ?? "";
		Vector2 size = HubKit.RichSize(text, 0.76f);
		float room = right - left;
		float x = left + Math.Max(0f, (room - size.X) / 2f);
		HubKit.RichText(sb, text, x, r.Y + (r.Height - size.Y) / 2f + 3f, missing ? (Theme.Muted * 0.7f) : (disabled ? Theme.Muted : Color.White), 0.76f, room);
	}
}
