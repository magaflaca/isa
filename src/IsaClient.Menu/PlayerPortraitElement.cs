using Terraria.UI;
using Terraria;

namespace IsaClient.Menu;

/// <summary>Draws a player's portrait in a UI element.</summary>
internal class PlayerPortraitElement : UIElement
{
	private static Player OurPlayer => InventoryViewer.OurPlayer;

	public UIElement ProvideUIElement()
	{
		UIElement holder = new UIElement();
		holder.Width = new StyleDimension(0f, 1f);
		holder.Height = new StyleDimension(112f, 0f);
		holder.IgnoresMouseInteraction = true;
		holder.SetPadding(0f);
		holder.Append(new PlayerPortrait(OurPlayer)
		{
			Left = new StyleDimension(4f, 0f),
			HAlign = 0f,
			IgnoresMouseInteraction = true
		});
		return holder;
	}
}
