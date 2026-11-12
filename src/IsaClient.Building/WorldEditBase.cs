using Terraria.ID;
using Terraria.ObjectData;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Building;

/// <summary>Shared world edit helpers.</summary>
public class WorldEditBase
{
	public static TileObjectData PlaceDirectObject(int i, int j, int tileType, out int xCoord, out int yCoord, out TileObject objectData, int placeStyle = 0, int direction = 1, int alternate = -1, int forcedRandom = -1)
	{
		xCoord = -1;
		yCoord = -1;
		objectData = default(TileObject);
		if (!TileObjectData.CustomPlace(tileType, placeStyle))
		{
			ClientChat.Info($"PlaceDirectObject was called for a non-tile object. ({TileID.Search.GetName(tileType)}, {tileType})");
			return null;
		}
		if (WorldGen.InWorld(i, j, 10))
		{
			TileObjectData tileData = TileObjectData.GetTileData(tileType, placeStyle);
			if (!TileObject.CanPlace(i, j, tileType, placeStyle, direction, out objectData, onlyCheck: false, (forcedRandom != -1) ? new int?(forcedRandom) : ((int?)null), Main.netMode == 0 && Placement.PlaceAnywhere))
			{
				return null;
			}
			if (forcedRandom != -1)
			{
				objectData.random = forcedRandom;
			}
			ushort num = (ushort)objectData.type;
			int num2 = 0;
			int num3 = 0;
			if (tileData.AlternatesCount != 0)
			{
				num3 = tileData.AlternatesCount;
				tileData = TileObjectData.GetTileData(tileType, placeStyle, (alternate != -1) ? alternate : ((direction != -1) ? 1 : 0));
				objectData.alternate = ((alternate != -1) ? alternate : ((direction != -1) ? 1 : 0));
			}
			else
			{
				tileData = TileObjectData.GetTileData(tileType, placeStyle);
			}
			int num4 = tileData.CalculatePlacementStyle(objectData.style, objectData.alternate, objectData.random);
			int num5 = 0;
			xCoord = objectData.xCoord;
			yCoord = objectData.yCoord;
			for (int k = 0; k < tileData.Width; k++)
			{
				_ = tileData.CoordinateWidth;
				_ = tileData.CoordinatePadding;
				for (int l = 0; l < tileData.Height; l++)
				{
					if (Framing.GetTileSafely(xCoord + k, yCoord + l).active() && Framing.GetTileSafely(xCoord + k, yCoord + l).type == num)
					{
						return null;
					}
				}
			}
			if (tileData.StyleWrapLimit > 0)
			{
				num5 = num4 / tileData.StyleWrapLimit * tileData.StyleLineSkip;
				num4 %= tileData.StyleWrapLimit;
			}
			if (tileData.StyleHorizontal)
			{
				num2 = tileData.CoordinateFullWidth * num4;
				num3 = tileData.CoordinateFullHeight * num5;
			}
			else
			{
				num2 = tileData.CoordinateFullWidth * num5;
				num3 = tileData.CoordinateFullHeight * num4;
			}
			for (int m = 0; m < tileData.Width; m++)
			{
				int num6 = num2 + m * (tileData.CoordinateWidth + tileData.CoordinatePadding);
				int num7 = num3;
				for (int n = 0; n < tileData.Height; n++)
				{
					Tile tileSafely = Framing.GetTileSafely(xCoord + m, yCoord + n);
					if (tileSafely.active() && tileSafely.type == num)
					{
						return null;
					}
					tileSafely.active(active: true);
					tileSafely.frameX = (short)num6;
					tileSafely.frameY = (short)num7;
					tileSafely.type = num;
					num7 += tileData.CoordinateHeights[n] + tileData.CoordinatePadding;
				}
			}
			if (Main.netMode == 0)
			{
				TileObjectData.CallPostPlacementPlayerHook(i, j, objectData.type, objectData.style, direction, objectData.alternate, objectData);
			}
			return tileData;
		}
		return null;
	}
}
