using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using IsaClient.Community;
using Terraria;

namespace IsaClient.Menu;

/// <summary>Info block of the inventory viewer: index, life, mana, tile position, client and spam flags.</summary>
public class FlavorText : UIElement
{
	public UIText UIText;

	private static int PlayerIndex => InventoryViewer.TargetPlayer;

	public UIElement ProvideUIElement()
	{
		UIPanel panel = new UIPanel(Main.Assets.Request<Texture2D>("Images/UI/Bestiary/Stat_Panel", AssetRequestMode.ImmediateLoad), null, 12, 7)
		{
			Width = new StyleDimension(-11f, 1f),
			Height = new StyleDimension(109f, 0f),
			IgnoresMouseInteraction = true,
			BackgroundColor = new Color(43, 56, 101),
			BorderColor = Color.Transparent,
			Left = new StyleDimension(3f, 0f),
			PaddingLeft = 4f,
			PaddingRight = 4f
		};
		UIText text = new UIText(IsaClient.Languages.L10n.T(GenerateStuff()), 0.8f)
		{
			HAlign = 0f,
			VAlign = 0f,
			IgnoresMouseInteraction = true,
			Width = StyleDimension.FromPixelsAndPercent(0f, 1f),
			Height = StyleDimension.FromPixelsAndPercent(0f, 1f),
			IsWrapped = true
		};
		text.OnInternalTextChange += delegate
		{
			panel.Height = new StyleDimension(text.MinHeight.Pixels, 0f);
		};
		UIText = text;
		panel.Append(text);
		return panel;
	}

	public string GenerateStuff()
	{
		List<string> list = new List<string>();
		Player player = Main.player[PlayerIndex];
		list.Add("Index: " + PlayerIndex);
		list.Add(Lang.inter[0].Value + " " + player.statLife + "/" + player.statLifeMax2);
		list.Add(Lang.inter[2].Value + ": " + player.statMana + "/" + player.statManaMax2);
		if (player.active)
		{
			if (!Spectate.IsInsideWorld(player))
			{
				list.Add("Position: Unavailable!");
			}
			else
			{
				list.Add("Position: " + (int)player.Bottom.X / 16 + ", " + (int)player.Bottom.Y / 16);
			}
			if (PlayerIndex != Main.myPlayer)
			{
				switch (player.IsaClientKind)
				{
				case 1:
					list.Add("IsaClient User");
					break;
				case 2:
					list.Add("zMod User");
					break;
				case 3:
					list.Add("TerraAngel User");
					break;
				}
				if (player.isProjectileSpammer)
				{
					list.Add("Projectile Spammer: True");
				}
				if (player.isParticleSpammer)
				{
					list.Add("Particle Spammer: True");
				}
				if (player.isSoundSpammer)
				{
					list.Add("Sound Spammer: True");
				}
				if (player.isNebulaSpammer)
				{
					list.Add("Nebula Spammer: True");
				}
			}
		}
		else
		{
			list.Add("Active: False");
		}
		return string.Join("\n", list);
	}
}
