using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;
using Terraria.GameContent.UI.Chat;
using Terraria.GameInput;
using Terraria.UI;
using Terraria.UI.Chat;
using IsaClient.Building;
using IsaClient.Gameplay;
using IsaClient.Movement;
using IsaClient.Networking;
using IsaClient.WorldTools;
using Terraria;

namespace IsaClient.Menu;

/// <summary>Panel row with a label and a value on the right.</summary>
public class MenuListItem : UIElement
{
	public Color Color;

	private string _name;

	public Func<bool> DisableCondition;

	public MenuListItem(string name, Color color, Func<bool> disableCon = null)
	{
		_name = name;
		Color = color;
		DisableCondition = ((disableCon != null) ? disableCon : ((Func<bool>)(() => false)));
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		float num = 6f;
		base.DrawSelf(spriteBatch);
		CalculatedStyle dimensions = GetDimensions();
		float num2 = dimensions.Width + 1f;
		Vector2 val = new Vector2(dimensions.X, dimensions.Y);
		bool flag = PlayerInput.ListeningTrigger == _name;
		Vector2 baseScale = default(Vector2);
		baseScale = new Vector2(0.8f);
		Color val2 = (flag ? Color.Gold : (base.IsMouseHovering ? Color.White : Color.Silver));
		val2 = Color.Lerp(val2, Color.White, base.IsMouseHovering ? 0.5f : 0f);
		Color color = (DisableCondition() ? Color.MultiplyRGBA(new Color(140, 140, 140)) : (base.IsMouseHovering ? Color : Color.MultiplyRGBA(new Color(180, 180, 180))));
		Vector2 position = val;
		Utils.DrawSettingsPanel(spriteBatch, position, num2, color);
		position.X += 8f;
		position.Y += 2f + num;
		ChatManager.DrawColorCodedStringWithShadow(spriteBatch, FontAssets.ItemStack.Value, IsaClient.Languages.L10n.T(_name), position, val2, 0f, Vector2.Zero, baseScale, num2);
		position.X -= 17f;
		string text = DisplayValue(out bool missing);
		if (missing && !flag)
		{
			val2 = new Color(80, 80, 80);
		}
		Vector2 stringSize = ChatManager.GetStringSize(FontAssets.ItemStack.Value, IsaClient.Languages.L10n.T(text), baseScale);
		position = new Vector2(dimensions.X + dimensions.Width - stringSize.X - 10f, dimensions.Y + 2f + num);
		GlyphTagHandler.GlyphsScale = 0.85f;
		ChatManager.DrawColorCodedStringWithShadow(spriteBatch, FontAssets.ItemStack.Value, IsaClient.Languages.L10n.T(text), position, val2, 0f, Vector2.Zero, baseScale, num2);
		GlyphTagHandler.GlyphsScale = 1f;
	}
	/// <summary>The row's name, for the new panel.</summary>
	internal string Name => _name;

	/// <summary>The value drawn on the right; <paramref name="missing"/> when there is none (a grey placeholder).</summary>
	internal string DisplayValue(out bool missing)
	{
		string text = GenInput(_name);
		missing = string.IsNullOrEmpty(text);
		if (!missing)
		{
			return text;
		}
		switch (_name)
		{
		case "Fill Selection":
			return "<No Tile/Wall Held>";
		case "Remove From Selection":
			return "<No Tile Held>";
		case "Seed":
			return "<Unavailble On Servers>";
		case "Dungeon Position":
		case "Smashed Orb Count":
		case "Smashed Altar Count":
			return "<Unavailble>";
		default:
			return "<Unknown...>";
		}
	}

	private string GenInput(string name)
	{
		try
		{
			string heldItemValue = HeldItemMenu.HeldItemValue(name);
			if (heldItemValue != null)
			{
				return heldItemValue;
			}
			string playerCheatValue = PlayerCheats.PlayerCheatValue(name);
			if (playerCheatValue != null)
			{
				return playerCheatValue;
			}
			string panelValue = WorldInfo.PanelValue(name);
			if (panelValue != null)
			{
				return panelValue;
			}
			switch (name)
			{
			case "Special Paint Pattern":
				return GetRainbowPaintName();
			case "Brush Pattern":
				return GetBrushPatternName();
			case "Packet Mode":
				return PacketPacing.TilePacketMaxMode ? "Packet Maximum" : "Packet 100/ms";
			case "Tile Packet Type":
				return (!Selection.SelectionTileSyncingMode) ? "Singular" : "Chunk";
			case "Fill Selection":
			case "Remove From Selection":
				return GetSelectionFillModeText();
			case "Brush Shape":
				return BrushState.BrushCircle ? "Circle" : "Square";
			case "Noclip Speed":
				return Noclip.Speed switch
				{
					9 => "Slow (9)", 
					64 => "Fast (64)", 
					_ => $"Default ({Noclip.Speed})", 
				};
			default:
				return "<Wrong UIElement...>";
			}
		}
		catch
		{
			return "Error!";
		}
	}

	private string GetRainbowPaintName()
	{
		return Placement.RainbowPaintType switch
		{
			0 => "Disabled", 
			1 => "Diagonal Rainbow", 
			2 => "Horizontal Rainbow", 
			3 => "Veritcal Rainbow", 
			4 => "Rainbow Ripple", 
			5 => "Black & White Rings", 
			6 => "Rainbow Rings", 
			7 => "Rainbow Rings (Big)", 
			8 => "Centered Rainbow Diamond", 
			9 => "Centered Rainbow Diamond (Huge)", 
			10 => "Rainbow Diamonds", 
			11 => "Noisy Rainbow", 
			12 => "Weaving Rainbow", 
			13 => "Wavy Checkered Rainbow", 
			14 => "Rainbow Diamonds 2", 
			15 => "Rainbow Diamonds 3", 
			16 => "Tetris", 
			_ => $"{Placement.RainbowPaintType}", 
		};
	}

	private string GetBrushPatternName()
	{
		return BrushState.BrushPattern switch
		{
			0 => "Normal", 
			1 => "Alternating Dither", 
			2 => "Inverted Alternating Dither", 
			3 => "Two-Spaced H. Dither", 
			4 => "Inverted Two-Spaced H. Dither", 
			5 => "Three-Spaced H. Dither", 
			6 => "Inverted Three-Spaced H. Dither", 
			7 => "Two-Spaced V. Dither", 
			8 => "Inverted Two-Spaced V. Dither", 
			9 => "Three-Spaced V. Dither", 
			10 => "Inverted Three-Spaced V. Dither", 
			11 => "Right Diagonal", 
			12 => "Inverted Right Diagonal", 
			13 => "Left Diagonal", 
			14 => "Inverted Left Diagonal", 
			15 => "Alternating U-D Dither", 
			16 => "Inverted Alternating U-D Dither", 
			17 => "Right Three-Spaced Diagonal", 
			18 => "Inv. Right Three-Spaced Diagonal", 
			19 => "Left Three-Spaced Diagonal", 
			20 => "Inv. Left Three-Spaced Diagonal", 
			21 => "R-Alt. Two-Spaced U-D Dither", 
			22 => "Inv. R-Alt. Two-Spaced U-D Dither", 
			23 => "L-Alt. Two-Spaced U-D Dither", 
			24 => "Inv. L-Alt. Two-Spaced U-D Dither", 
			25 => "Horizontal Lines", 
			26 => "Inverted Horizontal Lines", 
			27 => "Vertical Lines", 
			28 => "Inverted Vertical Lines", 
			29 => "Cross-Stitch", 
			30 => "Every-Other", 
			31 => "Empty Diamonds", 
			32 => "Inverted Empty Diamonds", 
			33 => "Right Snakes", 
			34 => "Inverted Right Snakes", 
			35 => "Left Snakes", 
			36 => "Inverted Left Snakes", 
			37 => "Coins", 
			38 => "Inverted Coins", 
			39 => "Empty Squares", 
			40 => "Inverted Empty Squares", 
			_ => $"{BrushState.BrushPattern}", 
		};
	}

	private string GetSelectionFillModeText()
	{
		Player localPlayer = Main.LocalPlayer;
		if (localPlayer.HeldItem.createTile <= -1 && localPlayer.HeldItem.createWall <= -1)
		{
			return "";
		}
		return "(" + Main.LocalPlayer.HeldItem.Name + ")";
	}
}
