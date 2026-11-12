using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;
using Terraria;

namespace IsaClient.Menu;

/// <summary>Inventory viewer action button: a framed label with an alternate label while its condition holds.</summary>
public class InventoryViewerButton : UIPanel, IManuallyOrderedUIElement
{
	private readonly string _text;

	private readonly string _altText;

	private readonly string _mouseOverText;

	private readonly Func<bool> _altTextCondition;

	private readonly UITextPanel<string> _textPanel;

	public int OrderInUIList { get; set; }

	public InventoryViewerButton(string text, string mouseOverText = "", string altText = "", Func<bool> altTextCon = null)
	{
		_text = text;
		_altText = altText;
		_mouseOverText = mouseOverText;
		_altTextCondition = altTextCon ?? ((Func<bool>)(() => false));
		SetPadding(0f);
		PaddingLeft = 10f;
		PaddingRight = 10f;
		Width.Set(-14f, 1f);
		Height.Set(32f, 0f);
		Left.Set(5f, 0f);
		base.OnMouseOver += HoverIn;
		base.OnMouseOut += HoverOut;
		BorderColor = new Color(89, 116, 213, 255);
		Append(_textPanel = new UITextPanel<string>(IsaClient.Languages.L10n.T(_text))
		{
			IgnoresMouseInteraction = true,
			DrawPanel = false,
			HAlign = 0.5f,
			Top = new StyleDimension(-4f, 0f)
		});
	}

	/// <summary>Short buttons share a row: true = the left or right half of it, false = the full width.</summary>
	public void SetLayout(bool half, bool right = false)
	{
		// A button that once sat directly in the list keeps the list's Top offset: inside a row it must start at the row's top.
		Top.Set(0f, 0f);
		if (half)
		{
			Width.Set(-9f, 0.5f);
			Left.Set(right ? 0f : 5f, right ? 0.5f : 0f);
			PaddingLeft = 4f;
			PaddingRight = 4f;
		}
		else
		{
			Width.Set(-14f, 1f);
			Left.Set(5f, 0f);
			PaddingLeft = 10f;
			PaddingRight = 10f;
		}
		Recalculate();
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		base.DrawSelf(spriteBatch);
		string label = (_altTextCondition() ? _altText : _text);
		// Shrink the label to the button (half-width buttons in particular).
		float room = GetInnerDimensions().Width - 4f;
		float width = Terraria.GameContent.FontAssets.MouseText.Value.MeasureString(IsaClient.Languages.L10n.T(label)).X;
		_textPanel.TextScale = ((width > room && width > 0f) ? Math.Max(0.45f, room / width) : 1f);
		_textPanel.SetText(IsaClient.Languages.L10n.T(label));
		if (base.IsMouseHovering && !string.IsNullOrEmpty(_mouseOverText))
		{
			Main.instance.MouseText(IsaClient.Languages.L10n.T(_mouseOverText), 0, 0);
		}
	}

	public override int CompareTo(object obj)
	{
		if (obj is IManuallyOrderedUIElement manuallyOrderedUIElement)
		{
			return OrderInUIList.CompareTo(manuallyOrderedUIElement.OrderInUIList);
		}
		return base.CompareTo(obj);
	}

	private void HoverIn(UIMouseEvent evt, UIElement listeningElement)
	{
		SoundEngine.PlaySound(12);
		BorderColor = Colors.FancyUIFatButtonMouseOver;
	}

	private void HoverOut(UIMouseEvent evt, UIElement listeningElement)
	{
		BorderColor = new Color(89, 116, 213, 255);
	}
}
