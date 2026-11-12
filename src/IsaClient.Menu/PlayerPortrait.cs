using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using Terraria;

namespace IsaClient.Menu;

/// <summary>Bestiary-style portrait of the viewed player over the map background of the biome they are in.</summary>
public class PlayerPortrait : UIElement
{
	private static UIImage _uIImage;

	public PlayerPortrait(Player player)
	{
		Height.Set(112f, 0f);
		Width.Set(219f, 0f);
		SetPadding(0f);
		UIElement frame = new UIElement
		{
			Width = new StyleDimension(-4f, 1f),
			Height = new StyleDimension(-4f, 1f),
			IgnoresMouseInteraction = true,
			OverflowHidden = true,
			HAlign = 0.5f,
			VAlign = 0.5f
		};
		frame.SetPadding(0f);
		if (!TextureAssets.MapBGs[0].IsLoaded)
		{
			Main.Assets.Request<Texture2D>("Images/MapBG1", AssetRequestMode.ImmediateLoad);
		}
		frame.Append(_uIImage = new UIImage(TextureAssets.MapBGs[0])
		{
			HAlign = 0.5f,
			VAlign = 0.5f,
			ScaleToFit = true,
			IgnoresMouseInteraction = true,
			Width = new StyleDimension(0f, 1f),
			Height = new StyleDimension(0f, 1f),
			Color = Color.White
		});
		UICharacter character = new UICharacter(player, animated: true, hasBackPanel: false)
		{
			HAlign = 0.5f,
			VAlign = 1f,
			IgnoresMouseInteraction = true
		};
		character.SetAnimated(animated: true);
		frame.Append(character);
		Append(frame);
		Append(new UIImage(Main.Assets.Request<Texture2D>("Images/UI/Bestiary/Portrait_Front", AssetRequestMode.ImmediateLoad))
		{
			VAlign = 0.5f,
			HAlign = 0.5f,
			IgnoresMouseInteraction = true
		});
	}

	public static void SetPortraitBG(Asset<Texture2D> asset)
	{
		if (_uIImage == null)
		{
			return;
		}
		_uIImage.SetImage(asset);
		_uIImage.Width = new StyleDimension(0f, 0.875f);
		_uIImage.Height = new StyleDimension(0f, 1f);
		_uIImage.IgnoresMouseInteraction = true;
	}
}
