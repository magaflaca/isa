using System.Collections.Generic;
using System.Globalization;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Terraria.ID;
using Terraria.Map;

namespace IsaClient.Building;

/// <summary>Color math for pixel art and map colors: closest block/wall color, hex parsing, wall color tables.</summary>
public static class ColorMatching
{
	public struct WallColorTable
	{
		public ushort Wall;

		public List<Color> Colors;

		public WallColorTable(ushort wallId)
		{
			Wall = 1;
			Colors = new List<Color>();
			Wall = wallId;
		}
	}

	public static bool IsNonSolidPixelTile(int i)
	{
		if (i != 51 && i != 124 && i != 150 && i != 213 && i != 214 && i != 248 && i != 336 && i != 340 && i != 341 && i != 342 && i != 343 && i != 344 && i != 351 && i != 353 && i != 365 && i != 366 && i != 379 && i != 449 && i != 450 && i != 451 && i != 561 && i != 574 && i != 575 && i != 576 && i != 577)
		{
			return i == 578;
		}
		return true;
	}

	public static int ClosestBlockColor(int iteration)
	{
		int num = int.MaxValue;
		int num2 = -1;
		for (int i = 0; i < PixelArt.HexValues.Length; i++)
		{
			int pixelColorDifferenceDistance = GetPixelColorDifferenceDistance(FromHex(PixelArt.PixelColorsInHex[iteration]), PixelArt.HexValues[i]);
			if (pixelColorDifferenceDistance < num)
			{
				num2 = i;
				num = pixelColorDifferenceDistance;
			}
		}
		try
		{
			if (num2 <= 52)
			{
				return num2 + 1;
			}
			if (num2 > 52 && num2 < 313)
			{
				return num2 + 2;
			}
			if (num2 >= 313)
			{
				return TileItemLookup.IndexToNonSolidTileID[num2];
			}
			ClientChat.Error("Error, Returned 0: " + num2);
			return 0;
		}
		catch
		{
			ClientChat.Error("an error happened on , index: " + num2);
			return 0;
		}
	}

	public static int GetPixelColorDifferenceDistance(Color current, Color match)
	{
		byte b = (byte)(current.R - match.R);
		int num = current.G - match.G;
		int num2 = current.B - match.B;
		return b * b + num * num + num2 * num2;
	}

	public static Color FromHex(string hexColor)
	{
		int num = int.Parse(hexColor.Contains("#") ? hexColor.Replace("#", "") : hexColor, NumberStyles.HexNumber);
		byte b = (byte)((num & 0xFF0000) >> 16);
		byte b2 = (byte)((num & 0xFF00) >> 8);
		byte b3 = (byte)(num & 0xFF);
		return new Color((int)b, (int)b2, (int)b3);
	}

	public static void SetupWallColors()
	{
		try
		{
			for (ushort num = 1; num < WallID.Count; num++)
			{
				WallColorTable item = new WallColorTable(num);
				for (byte b = 0; b < 31; b++)
				{
					item.Colors.Add(MapTileWallColor(num, b));
				}
				PixelArt.WallColors.Add(item);
			}
		}
		catch
		{
		}
	}

	public static Color MapTileWallColor(ushort wallId, byte wallColor)
	{
		int num = 0;
		int num2 = 255;
		int num3 = 0;
		int num4 = 0;
		if (wallId > 0 && wallId < WallID.Count)
		{
			num3 = MapHelper.wallLookup[wallId];
			num = wallColor;
			switch (wallId)
			{
			case 21:
			case 88:
			case 89:
			case 90:
			case 91:
			case 92:
			case 93:
			case 168:
			case 241:
				num = 0;
				break;
			case 27:
				num4 = 0;
				break;
			default:
				num4 = 0;
				break;
			}
		}
		MapTile tile = MapTile.Create((ushort)(num3 + num4), (byte)num2, (byte)num);
		return MapHelper.GetMapTileXnaColor(tile, 0, 0);
	}

	public static KeyValuePair<WallColorTable, byte>? GetClosestWallColor(Color color)
	{
		for (int i = 0; i < 255; i++)
		{
			for (int j = 0; j < PixelArt.WallColors.Count; j++)
			{
				for (byte b = 0; b < 31; b++)
				{
					Color wColor = PixelArt.WallColors[j].Colors[b];
					if (ColorWithinRange(color, wColor, i))
					{
						return new KeyValuePair<WallColorTable, byte>(PixelArt.WallColors[j], b);
					}
				}
			}
		}
		return null;
	}

	public static bool ColorWithinRange(Color color, Color wColor, int range)
	{
		if (wColor.R > color.R - range && wColor.R < color.R + range && wColor.B > color.B - range && wColor.B < color.B + range && wColor.G > color.G - range && wColor.G < color.G + range && wColor.A > 180)
		{
			return true;
		}
		return false;
	}
}
