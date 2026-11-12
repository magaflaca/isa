using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;
using Terraria.UI;
using Terraria.UI.Chat;
using Terraria;

namespace IsaClient.Menu;

/// <summary>Panel row that is a plain button.</summary>
public class MenuSimpleItem : UIElement
{
	public Color Color;

	private Func<string> _GetTextFunction;

	public Func<bool> DisableCondition;

	public bool IsSelectedTab;

	public MenuSimpleItem(Func<string> getText, Color color, Func<bool> disableCon = null)
	{
		Color = color;
		_GetTextFunction = ((getText != null) ? getText : ((Func<string>)(() => "???")));
		DisableCondition = ((disableCon != null) ? disableCon : ((Func<bool>)(() => false)));
	}

	/// <summary>The button's text, for the new panel.</summary>
	internal string LabelText => _GetTextFunction() ?? "";

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		float num = 6f;
		float num2 = ((Height.Pixels > 0f) ? (Height.Pixels / (float)TextureAssets.SettingsPanel.Value.Height) : 1f);
		num *= num2 * num2;
		base.DrawSelf(spriteBatch);
		CalculatedStyle dimensions = GetDimensions();
		float num3 = dimensions.Width + 1f;
		Vector2 val = new Vector2(dimensions.X, dimensions.Y);
		Vector2 baseScale = default(Vector2);
		baseScale = new Vector2(0.8f);
		Color val2 = (base.IsMouseHovering ? Color.White : Color.Silver);
		val2 = (DisableCondition() ? Color.Lerp(val2, new Color(160, 160, 160), 0f) : Color.Lerp(val2, Color.White, base.IsMouseHovering ? 0.5f : 0f));
		Color color = ((!IsSelectedTab && !DisableCondition()) ? (base.IsMouseHovering ? Color : Color.MultiplyRGBA(new Color(180, 180, 180))) : ((base.IsMouseHovering && !DisableCondition()) ? Color.MultiplyRGBA(new Color(220, 220, 220)) : Color.MultiplyRGBA(new Color(140, 140, 140))));
		Vector2 position = val;
		Utils.DrawSettings2Panel(spriteBatch, position, num3, color, num2);
		position.X += 8f;
		position.Y += 2f + num;
		string text = _GetTextFunction() ?? "";
		// Long labels (song titles...) are cut with "..." instead of wrapping out of the button.
		float room = num3 - 20f;
		if (ChatManager.GetStringSize(FontAssets.ItemStack.Value, IsaClient.Languages.L10n.T(text), baseScale).X > room)
		{
			int keep = text.Length;
			while (keep > 1 && ChatManager.GetStringSize(FontAssets.ItemStack.Value, IsaClient.Languages.L10n.T(text.Substring(0, keep).TrimEnd() + "..."), baseScale).X > room)
			{
				keep--;
			}
			text = text.Substring(0, keep).TrimEnd() + "...";
		}
		Vector2 stringSize = ChatManager.GetStringSize(FontAssets.ItemStack.Value, IsaClient.Languages.L10n.T(text), baseScale);
		position.X = dimensions.X + dimensions.Width / 2f - stringSize.X / 2f;
		ChatManager.DrawColorCodedStringWithShadow(spriteBatch, FontAssets.ItemStack.Value, IsaClient.Languages.L10n.T(text), position, val2, 0f, Vector2.Zero, baseScale, num3);
	}
}
