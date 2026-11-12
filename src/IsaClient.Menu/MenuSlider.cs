using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.UI;
using Terraria.UI.Chat;
using Terraria;

namespace IsaClient.Menu;

/// <summary>Panel row with a slider.</summary>
public class MenuSlider : UIElement
{
	public Color Color;

	private Func<string> _TextDisplayFunction;

	public Func<float> GetStatusFunction;

	private Action<float> _SlideKeyboardAction;

	private int _sliderIDInPage;

	private Asset<Texture2D> _toggleTexture;

	public float Perc;

	public MenuSlider(Func<string> getText, Func<float> getStatus, Action<float> setStatusKeyboard, Action setStatusGamepad, int sliderIDInPage, Color color)
	{
		Color = color;
		_toggleTexture = Main.Assets.Request<Texture2D>("Images/UI/Settings_Toggle", (AssetRequestMode)1);
		_TextDisplayFunction = ((getText != null) ? getText : ((Func<string>)(() => "???")));
		GetStatusFunction = ((getStatus != null) ? getStatus : ((Func<float>)(() => 0f)));
		_SlideKeyboardAction = ((setStatusKeyboard != null) ? setStatusKeyboard : ((Action<float>)delegate
		{
		}));
		_sliderIDInPage = sliderIDInPage;
		Perc = GetStatusFunction();
	}

	/// <summary>The slider's text and value (0-1), for the new panel.</summary>
	internal string LabelText => _TextDisplayFunction();

	internal float Value => GetStatusFunction();

	internal void SetValue(float value)
	{
		_SlideKeyboardAction(value);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		float num = 6f;
		base.DrawSelf(spriteBatch);
		int num2 = 0;
		IngameOptions.rightHover = -1;
		if (!Main.mouseLeft)
		{
			IngameOptions.rightLock = -1;
		}
		if (IngameOptions.rightLock == _sliderIDInPage)
		{
			num2 = 1;
		}
		else if (IngameOptions.rightLock != -1)
		{
			num2 = 2;
		}
		CalculatedStyle dimensions = GetDimensions();
		float num3 = dimensions.Width + 1f;
		Vector2 val = new Vector2(dimensions.X, dimensions.Y);
		bool flag = base.IsMouseHovering;
		if (num2 == 1)
		{
			flag = true;
		}
		if (num2 == 2)
		{
			flag = false;
		}
		Vector2 baseScale = default(Vector2);
		baseScale = new Vector2(0.8f);
		Color val2 = (flag ? Color.White : Color.Silver);
		val2 = Color.Lerp(val2, Color.White, flag ? 0.5f : 0f);
		Color color = (flag ? Color : Color.MultiplyRGBA(new Color(180, 180, 180)));
		Vector2 val3 = val;
		Utils.DrawSettingsPanel(spriteBatch, val3, num3, color);
		val3.X += 8f;
		val3.Y += 2f + num;
		ChatManager.DrawColorCodedStringWithShadow(spriteBatch, FontAssets.ItemStack.Value, IsaClient.Languages.L10n.T(_TextDisplayFunction()), val3, val2, 0f, Vector2.Zero, baseScale, num3);
		val3.X -= 17f;
		TextureAssets.ColorBar.Frame();
		val3 = new Vector2(dimensions.X + dimensions.Width - 10f, dimensions.Y + 10f + num);
		IngameOptions.valuePosition = val3;
		float obj = IngameOptions.DrawValueBar(spriteBatch, 1f, GetStatusFunction(), num2);
		if (IngameOptions.inBar || IngameOptions.rightLock == _sliderIDInPage)
		{
			Perc = GetStatusFunction();
			IngameOptions.rightHover = _sliderIDInPage;
			if (PlayerInput.Triggers.Current.MouseLeft && !PlayerInput.UsingGamepad && IngameOptions.rightLock == _sliderIDInPage && !Main.grabbingMap)
			{
				CheatPanel.DraggingSlider = true;
				_SlideKeyboardAction(obj);
			}
		}
		if (IngameOptions.rightHover != -1 && IngameOptions.rightLock == -1)
		{
			IngameOptions.rightLock = IngameOptions.rightHover;
		}
	}
}
