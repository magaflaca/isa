using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.GameContent;
using Terraria.UI;
using Terraria.UI.Chat;
using Terraria;

namespace IsaClient.Menu;

/// <summary>Panel row with an on/off toggle.</summary>
public class MenuToggle : UIElement
{
	private Color _color;

	private Func<string> _TextDisplayFunction;

	private Func<bool> _IsOnFunction;

	public Func<bool> DisableCondition;

	private Asset<Texture2D> _toggleTexture;

	public MenuToggle(Func<string> getText, Func<bool> getStatus, Color color, Func<bool> disableCon = null)
	{
		_color = color;
		_toggleTexture = Main.Assets.Request<Texture2D>("Images/UI/Settings_Toggle", (AssetRequestMode)1);
		_TextDisplayFunction = ((getText != null) ? getText : ((Func<string>)(() => "???")));
		_IsOnFunction = ((getStatus != null) ? getStatus : ((Func<bool>)(() => false)));
		DisableCondition = ((disableCon != null) ? disableCon : ((Func<bool>)(() => false)));
	}

	/// <summary>The row's text and state, for the new panel.</summary>
	internal string LabelText => _TextDisplayFunction();

	internal bool IsOn => _IsOnFunction();

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		float num = 6f;
		base.DrawSelf(spriteBatch);
		CalculatedStyle dimensions = GetDimensions();
		float num2 = dimensions.Width + 1f;
		Vector2 val = new Vector2(dimensions.X, dimensions.Y);
		Vector2 baseScale = default(Vector2);
		baseScale = new Vector2(0.8f);
		Color val2 = (base.IsMouseHovering ? Color.White : Color.Silver);
		val2 = (DisableCondition() ? Color.Lerp(val2, new Color(160, 160, 160), 0f) : Color.Lerp(val2, Color.White, base.IsMouseHovering ? 0.5f : 0f));
		Color color = (DisableCondition() ? _color.MultiplyRGBA(new Color(140, 140, 140)) : (base.IsMouseHovering ? _color : _color.MultiplyRGBA(new Color(180, 180, 180))));
		Vector2 position = val;
		Utils.DrawSettingsPanel(spriteBatch, position, num2, color);
		position.X += 8f;
		position.Y += 2f + num;
		ChatManager.DrawColorCodedStringWithShadow(spriteBatch, FontAssets.ItemStack.Value, IsaClient.Languages.L10n.T(_TextDisplayFunction()), position, val2, 0f, Vector2.Zero, baseScale, num2);
		position.X -= 17f;
		Rectangle val3 = default(Rectangle);
		val3 = new Rectangle(_IsOnFunction() ? ((_toggleTexture.Width() - 2) / 2 + 2) : 0, 0, (_toggleTexture.Width() - 2) / 2, _toggleTexture.Height());
		Vector2 val4 = default(Vector2);
		val4 = new Vector2((float)val3.Width, 0f);
		Vector2 val5 = default(Vector2);
		val5 = new Vector2(dimensions.X + dimensions.Width - val4.X - 10f, dimensions.Y + 2f + num);
		spriteBatch.Draw(_toggleTexture.Value, val5, (Rectangle?)val3, Color.White, 0f, Vector2.Zero, Vector2.One, (SpriteEffects)0, 0f);
	}
}
