using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;
using Terraria.UI;
using Terraria;

namespace IsaClient.Menu;

/// <summary>Name line at the top of the inventory viewer's side panel.</summary>
public class NamePlate : UIElement
{
	/// <summary>Plain centered text: names are shown as typed, chat tags in them are not parsed.</summary>
	public sealed class PlainText : UIElement
	{
		private string _text = "";

		public void SetText(string text)
		{
			_text = text ?? "";
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle d = GetDimensions();
			float scale = 1f;
			float width = FontAssets.MouseText.Value.MeasureString(_text).X;
			if (width > d.Width - 8f && width > 0f)
			{
				scale = (d.Width - 8f) / width;
			}
			Utils.DrawBorderString(spriteBatch, _text, new Vector2(d.X + d.Width / 2f, d.Y + d.Height / 2f + 2f), Color.White, scale, 0.5f, 0.5f);
		}
	}

	public PlainText UIText;

	private static int PlayerIndex => InventoryViewer.TargetPlayer;

	public UIElement ProvideUIElement()
	{
		PlainText text = new PlainText
		{
			Width = new StyleDimension(0f, 1f),
			Height = new StyleDimension(0f, 1f),
			IgnoresMouseInteraction = true
		};
		text.SetText(Main.player[PlayerIndex].name);
		UIElement holder = new UIElement();
		holder.Width = new StyleDimension(0f, 1f);
		holder.Height = new StyleDimension(24f, 0f);
		holder.Append(text);
		UIText = text;
		return holder;
	}
}
