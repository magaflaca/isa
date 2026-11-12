using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ObjectData;
using IsaClient.Core;
using IsaClient.Movement;
using IsaClient.Networking;
using IsaClient.Rendering;
using IsaClient.WorldTools;
using Terraria;

namespace IsaClient.Building;

/// <summary>Applies the world edit brushes (tile, wall, erase, liquid, paint) to the world, locally and over the network.</summary>
public class BrushOperations
{
	private static int _tshockLiquidsThisSecond;

	public struct BrushData
	{
		public byte BrushType;

		public int X;

		public int Y;

		public ushort ToPlace;

		public byte Style;

		public int ItemNetId;

		public bool HasObject;

		public int Alt;

		public int Dir;

		public int Rand;

		public BrushData(byte Brush, int xPos, int yPos, ushort tileWallId = 0, byte placeStyle = 0, bool hasObj = false, int itemType = 0, int direction = -1, int alternate = 0, int random = 0)
		{
			Alt = 0;
			Dir = -1;
			Rand = 0;
			BrushType = Brush;
			X = xPos;
			Y = yPos;
			ToPlace = tileWallId;
			Style = placeStyle;
			HasObject = hasObj;
			ItemNetId = itemType;
			Dir = direction;
			Alt = alternate;
			Rand = random;
		}
	}

	public static List<BrushData> TilesToBrush = new List<BrushData>();

	public static bool TileValidForBrush(Point mouseToTile, int i, int j, int size)
	{
		if (BrushState.BrushCircle)
		{
			int num = Math.Abs(i - mouseToTile.X);
			int num2 = Math.Abs(j - mouseToTile.Y);
			if ((int)Math.Floor((float)Math.Sqrt(num * num + num2 * num2)) > size)
			{
				return false;
			}
		}
		switch (BrushState.BrushPattern)
		{
		default:
			return true;
		case 1:
			return MathUtil.IsEven(i + j);
		case 2:
			return !MathUtil.IsEven(i + j);
		case 3:
			return MathUtil.IsEven(i / 2 + j);
		case 4:
			return !MathUtil.IsEven(i / 2 + j);
		case 5:
			return MathUtil.IsEven(i / 3 + j);
		case 6:
			return !MathUtil.IsEven(i / 3 + j);
		case 7:
			return MathUtil.IsEven(j / 2 + i);
		case 8:
			return !MathUtil.IsEven(j / 2 + i);
		case 9:
			return MathUtil.IsEven(j / 3 + i);
		case 10:
			return !MathUtil.IsEven(j / 3 + i);
		case 11:
			return (i + j * 2) % 3 == 0;
		case 12:
			return (i + j * 2) % 3 != 0;
		case 13:
			return (i - j * 2) % 3 == 0;
		case 14:
			return (i - j * 2) % 3 != 0;
		case 15:
			return (i + j * 2) % 4 == 0;
		case 16:
			return (i + j * 2) % 4 != 0;
		case 17:
			return (i + j * 3) % 4 == 0;
		case 18:
			return (i + j * 3) % 4 != 0;
		case 19:
			return (i - j * 3) % 4 == 0;
		case 20:
			return (i - j * 3) % 4 != 0;
		case 21:
			return (i + j * 3) % 5 == 0;
		case 22:
			return (i + j * 3) % 5 != 0;
		case 23:
			return (i - j * 3) % 5 == 0;
		case 24:
			return (i - j * 3) % 5 != 0;
		case 25:
			return !MathUtil.IsEven(i * 2 + j);
		case 26:
			return MathUtil.IsEven(i * 2 + j);
		case 27:
			return MathUtil.IsEven(j * 2 + i);
		case 28:
			return !MathUtil.IsEven(j * 2 + i);
		case 29:
			if (!MathUtil.IsEven(i * 2 + j))
			{
				return MathUtil.IsEven(j * 2 + i);
			}
			return true;
		case 30:
			if (!MathUtil.IsEven(i * 2 + j))
			{
				return !MathUtil.IsEven(j * 2 + i);
			}
			return false;
		case 31:
			if ((i + j) % 4 != 0)
			{
				return (i + j * 3) % 4 == 0;
			}
			return true;
		case 32:
			if ((i + j) % 4 != 0)
			{
				return (i + j * 3) % 4 != 0;
			}
			return false;
		case 33:
			if ((i + j * 2) % 4 != 0)
			{
				return (i + j * 3) % 5 == 0;
			}
			return true;
		case 34:
			if ((i + j * 2) % 4 != 0)
			{
				return (i + j * 3) % 5 != 0;
			}
			return false;
		case 35:
			if ((i - j * 2) % 4 != 0)
			{
				return (i - j * 3) % 5 == 0;
			}
			return true;
		case 36:
			if ((i - j * 2) % 4 != 0)
			{
				return (i - j * 3) % 5 != 0;
			}
			return false;
		case 37:
			if ((i + j * 2) % 4 != 0 && (i + j * 3) % 5 != 0)
			{
				return (i - j * 3) % 5 == 0;
			}
			return true;
		case 38:
			if ((i + j * 2) % 4 != 0 && (i + j * 3) % 5 != 0)
			{
				return (i - j * 3) % 5 != 0;
			}
			return false;
		case 39:
			if ((i + j * 3) % 5 != 0)
			{
				return (i - j * 3) % 5 == 0;
			}
			return true;
		case 40:
			if ((i + j * 3) % 5 != 0)
			{
				return (i - j * 3) % 5 != 0;
			}
			return false;
		}
	}

	public static void TileBrush(Point mouseToTile, int size)
	{
		if (Main.gameMenu)
		{
			return;
		}
		int num = Main.LocalPlayer.HeldItem.createTile;
		byte b = (byte)Main.LocalPlayer.HeldItem.placeStyle;
		int netID = Main.LocalPlayer.HeldItem.type;
		if (Main.LocalPlayer.inventory[Main.LocalPlayer.selectedItem].type == 4262)
		{
			num = 504;
		}
		if (Main.LocalPlayer.inventory[Main.LocalPlayer.selectedItem].type == 496)
		{
			num = 127;
		}
		if (num == -1)
		{
			return;
		}
		for (int i = mouseToTile.X - size; i <= mouseToTile.X + size; i++)
		{
			for (int j = mouseToTile.Y - size; j <= mouseToTile.Y + size; j++)
			{
				if (!WorldGen.InWorld(i, j) || !TileValidForBrush(mouseToTile, i, j, size))
				{
					continue;
				}
				Tile tile = Framing.GetTileSafely(i, j);
				if (tile.type != num && tile.type >= 0 && !TileObjectData.CustomPlace(num, b))
				{
					tile.ClearTile();
				}
				if (tile.active() && (!tile.active() || tile.type == num))
				{
					continue;
				}
				try
				{
					if (TileID.Sets.BasicChest[num] || num == 82 || num == 227 || !TileObjectData.CustomPlace(num, b))
					{
						if (TileID.Sets.BasicChest[num] && !TileObject.CanPlace(i, j, num, b, Main.LocalPlayer.direction, out var _))
						{
							continue;
						}
						WorldGen.PlaceTile(i, j, num, mute: true, forced: true, Main.myPlayer, b);
						BrushData item = new BrushData(0, i, j, (ushort)num, b, hasObj: false, netID);
						if (Main.netMode != 0)
						{
							try
							{
								if (!TileID.Sets.BasicChest[num] || !TilesToBrush.Contains(item))
								{
									TilesToBrush.Add(item);
								}
							}
							catch
							{
							}
						}
						goto IL_02e1;
					}
					int xCoord;
					int yCoord;
					TileObject objectData2;
					TileObjectData tileObjectData = WorldEditBase.PlaceDirectObject(i, j, num, out xCoord, out yCoord, out objectData2, b, Main.LocalPlayer.direction);
					if (tileObjectData != null)
					{
						if (Main.netMode != 0)
						{
							for (int k = xCoord; k < xCoord + tileObjectData.Width; k++)
							{
								for (int l = yCoord; l < yCoord + tileObjectData.Height; l++)
								{
									BrushData item2 = new BrushData(1, i, j, 0, 0);
									if (Framing.GetTileSafely(k, l).type != num && Framing.GetTileSafely(k, l).active())
									{
										try
										{
											TilesToBrush.Add(item2);
										}
										catch
										{
										}
									}
								}
							}
						}
						WorldGen.SquareTileFrame(i, j, resetFrame: false, noBreak: true);
						BrushData item3 = new BrushData(0, i, j, (ushort)num, b, hasObj: true, netID, Main.LocalPlayer.direction, objectData2.alternate, objectData2.random);
						if (num == 88)
						{
							item3.HasObject = false;
						}
						if (Main.netMode != 0)
						{
							try
							{
								TilesToBrush.Add(item3);
							}
							catch
							{
							}
						}
					}
					goto IL_02e1;
					IL_02e1:
					Main.Map.UpdateLighting(i, j, byte.MaxValue);
					Main.refreshMap = true;
				}
				catch (Exception)
				{
				}
			}
		}
	}

	public static void WallBrush(Point mouseToTile, int size)
	{
		if (Main.gameMenu)
		{
			return;
		}
		int num = (ushort)Main.LocalPlayer.HeldItem.createWall;
		if (num <= 0)
		{
			return;
		}
		for (int i = mouseToTile.X - size; i <= mouseToTile.X + size; i++)
		{
			for (int j = mouseToTile.Y - size; j <= mouseToTile.Y + size; j++)
			{
				if (!WorldGen.InWorld(i, j) || !TileValidForBrush(mouseToTile, i, j, size))
				{
					continue;
				}
				Tile tile = Framing.GetTileSafely(i, j);
				BrushData item = new BrushData(2, i, j, (ushort)num, 0, hasObj: false, Main.LocalPlayer.HeldItem.type);
				if (!Main.sectionManager.SectionLoaded(Netplay.GetSectionX(i), Netplay.GetSectionY(j)))
				{
					ClientChat.Info("Tiles are not loaded, reveal map.");
					return;
				}
				if (tile.wall != num && tile.wall > 0)
				{
					tile.Clear(TileDataType.Wall);
				}
				if (tile.wall != 0)
				{
					continue;
				}
				tile.wall = (ushort)num;
				WorldGen.SquareWallFrame(i, j);
				if (Main.netMode != 0)
				{
					try
					{
						TilesToBrush.Add(item);
					}
					catch
					{
					}
				}
				Main.Map.UpdateLighting(i, j, byte.MaxValue);
				Main.refreshMap = true;
			}
		}
	}

	public static void EraseBrush(Point mouseToTile, int size)
	{
		if (Main.gameMenu)
		{
			return;
		}
		for (int i = mouseToTile.X - size; i <= mouseToTile.X + size; i++)
		{
			for (int j = mouseToTile.Y - size; j <= mouseToTile.Y + size; j++)
			{
				if (!WorldGen.InWorld(i, j))
				{
					continue;
				}
				bool flag = false;
				bool flag2 = false;
				bool flag3 = false;
				if (!TileValidForBrush(mouseToTile, i, j, size))
				{
					continue;
				}
				Tile tile = Framing.GetTileSafely(i, j);
				BrushData item = new BrushData(1, i, j, 0, 0);
				try
				{
					if (tile.active())
					{
						tile.frameX = -1;
						tile.frameY = -1;
						tile.ClearTile();
						tile.ClearBlockPaintAndCoating();
						WorldGen.SquareTileFrame(i, j);
						flag = true;
						flag2 = true;
					}
					if (tile.wall > 0)
					{
						tile.Clear(TileDataType.Wall);
						tile.ClearWallPaintAndCoating();
						WorldGen.SquareWallFrame(i, j);
						flag = true;
						flag3 = true;
						item.ToPlace = (ushort)((!flag2) ? 1u : 2u);
					}
					if ((flag2 || flag3) && Main.netMode != 0)
					{
						try
						{
							TilesToBrush.Add(item);
						}
						catch
						{
						}
					}
				}
				catch (Exception)
				{
				}
				if (flag)
				{
					Main.Map.UpdateLighting(i, j, byte.MaxValue);
					Main.refreshMap = true;
				}
			}
		}
	}

	public static void LiquidAndDryBrush(Point mouseToTile, int size)
	{
		if (Main.gameMenu)
		{
			return;
		}
		for (int i = mouseToTile.X - size; i <= mouseToTile.X + size; i++)
		{
			for (int j = mouseToTile.Y - size; j <= mouseToTile.Y + size; j++)
			{
				int type = Main.LocalPlayer.inventory[Main.LocalPlayer.selectedItem].type;
				if (!WorldGen.InWorld(i, j) || !TileValidForBrush(mouseToTile, i, j, size))
				{
					continue;
				}
				Tile tile = Framing.GetTileSafely(i, j);
				BrushData item = new BrushData(3, i, j, 0, 0, hasObj: false, type);
				if (!Main.sectionManager.SectionLoaded(Netplay.GetSectionX(i), Netplay.GetSectionY(j)))
				{
					ClientChat.Info("Tiles are not loaded, reveal map.");
					return;
				}
				try
				{
					bool flag = false;
					if (tile.LiquidAmount < byte.MaxValue && (!tile.HasTile || (tile.HasTile && (!Main.tileSolid[tile.TileType] || TileID.Sets.Platforms[tile.TileType] || tile.TileType == 546 || tile.IsActuated) && tile.TileType != 379)) && ItemID.Sets.LiquidBuckets[type])
					{
						switch (type)
						{
						case 206:
						case 3031:
							tile.LiquidType = 0;
							tile.LiquidAmount = byte.MaxValue;
							WorldGen.SquareTileFrame(i, j, resetFrame: false, noBreak: true);
							item.ToPlace = 0;
							item.Style = byte.MaxValue;
							item.ItemNetId = type;
							flag = true;
							break;
						case 207:
						case 4820:
							tile.LiquidType = 1;
							tile.LiquidAmount = byte.MaxValue;
							WorldGen.SquareTileFrame(i, j, resetFrame: false, noBreak: true);
							item.ToPlace = 1;
							item.Style = byte.MaxValue;
							flag = true;
							break;
						case 1128:
						case 5302:
							tile.LiquidType = 2;
							tile.LiquidAmount = byte.MaxValue;
							WorldGen.SquareTileFrame(i, j, resetFrame: false, noBreak: true);
							item.ToPlace = 2;
							item.Style = byte.MaxValue;
							flag = true;
							break;
						case 5364:
							tile.LiquidType = 3;
							tile.LiquidAmount = byte.MaxValue;
							WorldGen.SquareTileFrame(i, j, resetFrame: false, noBreak: true);
							item.ToPlace = 3;
							item.Style = byte.MaxValue;
							flag = true;
							break;
						}
					}
					else if (tile.LiquidAmount > 0 && tile.LiquidAmount > 0 && ((type == 205 && tile.LiquidType != 3) || (type == 3032 && tile.LiquidType == 0) || (type == 4872 && tile.LiquidType == 1) || (type == 5303 && tile.LiquidType == 2) || type == 5304))
					{
						tile.LiquidAmount = 0;
						tile.LiquidType = 0;
						WorldGen.SquareTileFrame(i, j, resetFrame: false);
						item.ToPlace = 0;
						item.Style = 0;
						flag = true;
					}
					if (!flag)
					{
						continue;
					}
					Main.Map.UpdateLighting(i, j, byte.MaxValue);
					Main.refreshMap = true;
					if (Main.netMode != 0)
					{
						try
						{
							TilesToBrush.Add(item);
						}
						catch
						{
						}
					}
				}
				catch (Exception)
				{
				}
			}
		}
	}

	public static void TilePaintBrush(Point mouseToTile, int size)
	{
		byte b = 0;
		byte b2 = 0;
		byte b3 = 0;
		if (BrushState.HoldingPaintBrush)
		{
			b = 1;
		}
		if (BrushState.HoldingPaintRoller)
		{
			b = 2;
		}
		if (b > 0)
		{
			Item item = Main.LocalPlayer.FindPaintOrCoating();
			if (item != null)
			{
				if (item.paintCoating != 0)
				{
					b3 = item.paintCoating;
				}
				if (item.paint != 0)
				{
					b2 = item.paint;
				}
			}
			if (Placement.PaintOverride > 0)
			{
				b2 = Placement.PaintOverride;
			}
			if (Placement.PaintCoatOverride > 0)
			{
				b3 = Placement.PaintCoatOverride;
			}
		}
		bool flag = false;
		for (int i = mouseToTile.X - size; i <= mouseToTile.X + size; i++)
		{
			for (int j = mouseToTile.Y - size; j <= mouseToTile.Y + size; j++)
			{
				flag = false;
				bool flag2 = false;
				if (!WorldGen.InWorld(i, j) || !TileValidForBrush(mouseToTile, i, j, size))
				{
					continue;
				}
				BrushData item2 = new BrushData(4, i, j, 0, 0, hasObj: false, Main.LocalPlayer.HeldItem.type);
				switch (b)
				{
				case 1:
					if (b3 != 0 && (Placement.PaintOnlyTile == -1 || Framing.GetTileSafely(i, j).type == Placement.PaintOnlyTile) && WorldGen.paintCoatTile(i, j, b3, broadcast: false, coatingEffects: false))
					{
						item2.Style = 2;
						item2.ToPlace = b3;
						flag = true;
					}
					if (Placement.RainbowPaintType > 0)
					{
						b2 = GetRainbowPaintColor(i, j);
					}
					if (b2 != 0 && (Placement.PaintOnlyTile == -1 || Framing.GetTileSafely(i, j).type == Placement.PaintOnlyTile) && WorldGen.paintTile(i, j, b2, broadCast: false, paintEffects: false))
					{
						item2.Style = 0;
						item2.ToPlace = b2;
						flag = true;
					}
					break;
				case 2:
					if (b3 != 0 && (Placement.PaintOnlyWall == 0 || Framing.GetTileSafely(i, j).wall == Placement.PaintOnlyWall) && WorldGen.paintCoatWall(i, j, b3, broadcast: false, coatingEffects: false))
					{
						item2.Style = 3;
						item2.ToPlace = b3;
						flag = true;
					}
					if (Placement.RainbowPaintType > 0)
					{
						b2 = GetRainbowPaintColor(i, j);
					}
					if (b2 != 0 && (Placement.PaintOnlyWall == 0 || Framing.GetTileSafely(i, j).wall == Placement.PaintOnlyWall) && WorldGen.paintWall(i, j, b2, broadCast: false, paintEffects: false))
					{
						item2.Style = 1;
						item2.ToPlace = b2;
						flag = true;
					}
					break;
				default:
					if ((Placement.PaintOnlyTile == -1 || Framing.GetTileSafely(i, j).type == Placement.PaintOnlyTile) && WorldGen.paintTile(i, j, 0, broadCast: false, paintEffects: false))
					{
						item2.Style = 0;
						item2.ToPlace = 0;
						flag = true;
						if (Main.netMode != 0)
						{
							try
							{
								TilesToBrush.Add(item2);
							}
							catch
							{
							}
						}
						flag2 = true;
					}
					if ((Placement.PaintOnlyWall == 0 || Framing.GetTileSafely(i, j).wall == Placement.PaintOnlyWall) && WorldGen.paintWall(i, j, 0, broadCast: false, paintEffects: false))
					{
						item2.Style = 1;
						item2.ToPlace = 0;
						flag = true;
						if (Main.netMode != 0)
						{
							try
							{
								TilesToBrush.Add(item2);
							}
							catch
							{
							}
						}
						flag2 = true;
					}
					if ((Placement.PaintOnlyTile == -1 || Framing.GetTileSafely(i, j).type == Placement.PaintOnlyTile) && WorldGen.paintCoatTile(i, j, 0, broadcast: false, coatingEffects: false))
					{
						item2.Style = 2;
						item2.ToPlace = 0;
						flag = true;
						if (Main.netMode != 0)
						{
							try
							{
								TilesToBrush.Add(item2);
							}
							catch
							{
							}
						}
						flag2 = true;
					}
					if ((Placement.PaintOnlyWall != 0 && Framing.GetTileSafely(i, j).wall != Placement.PaintOnlyWall) || !WorldGen.paintCoatWall(i, j, 0, broadcast: false, coatingEffects: false))
					{
						break;
					}
					item2.Style = 3;
					item2.ToPlace = 0;
					flag = true;
					if (Main.netMode != 0)
					{
						try
						{
							TilesToBrush.Add(item2);
						}
						catch
						{
						}
					}
					flag2 = true;
					break;
				}
				if (!flag)
				{
					continue;
				}
				Main.Map.UpdateLighting(i, j, byte.MaxValue);
				Main.refreshMap = true;
				if (Main.netMode != 0 && !flag2)
				{
					try
					{
						TilesToBrush.Add(item2);
					}
					catch
					{
					}
				}
			}
		}
	}

	public static byte GetRainbowPaintColor(int x, int y)
	{
		switch (Placement.RainbowPaintType)
		{
		case 1:
		{
			int num78 = x % 52 + y % 52;
			num78 %= 26;
			if (num78 > 12)
			{
				num78 = 12 - (num78 - 12);
			}
			num78 = Math.Min(12, Math.Max(1, num78));
			return (byte)((Placement.RainbowPaintDeep ? 12 : 0) + num78);
		}
		case 2:
		{
			int num77 = x % 52;
			num77 %= 26;
			if (num77 > 12)
			{
				num77 = 12 - (num77 - 12);
			}
			num77 = Math.Min(12, Math.Max(1, num77));
			return (byte)((Placement.RainbowPaintDeep ? 12 : 0) + num77);
		}
		case 3:
		{
			int num72 = y % 52;
			num72 %= 26;
			if (num72 > 12)
			{
				num72 = 12 - (num72 - 12);
			}
			num72 = Math.Min(12, Math.Max(1, num72));
			return (byte)((Placement.RainbowPaintDeep ? 12 : 0) + num72);
		}
		case 4:
		{
			double num73 = (double)x - (double)Main.maxTilesX / 2.0;
			double num74 = (double)y - (double)Main.maxTilesY / 2.0;
			double num75 = Math.Sqrt(num73 * num73 + num74 * num74);
			double num76 = Math.Atan2(num74, num73);
			return (byte)Utils.Clamp((int)((Math.Sin(num75 * 0.08 + num76 * 7.0) + Math.Cos(num75 * 0.08 - num76 * 5.0) + 2.0) / 4.0 * 30.0) + 1, 1, 31);
		}
		case 5:
		{
			int num66 = Main.maxTilesX / 2;
			int num67 = Main.maxTilesY / 2;
			return (byte)(((int)(Math.Sqrt(Math.Pow(x - num66, 2.0) + Math.Pow(y - num67, 2.0)) / 10.0) % 2 == 0) ? 25u : 26u);
		}
		case 6:
		{
			int num59 = Main.maxTilesX / 2;
			int num60 = Main.maxTilesY / 2;
			double num61 = x - num59;
			double num62 = y - num60;
			double num63 = Math.Atan2(num62, num61);
			double num64 = Math.Sqrt(num61 * num61 + num62 * num62);
			double num65 = Math.Sin(num63 * 6.0 + num64 * 0.1);
			return (byte)(1 + (int)((num65 + 1.0) * 15.0) % 30);
		}
		case 7:
		{
			int num5 = Main.maxTilesX / 2;
			int num6 = Main.maxTilesY / 2;
			double num7 = x - num5;
			double num8 = y - num6;
			double num9 = Math.Atan2(num8, num7);
			double num10 = Math.Sqrt(num7 * num7 + num8 * num8);
			double num11 = 7.0;
			double num12 = Math.Cos(num9 * num11) * 0.5 + 0.5;
			double num13 = Math.Sin(num9 * num11 + num10 * 0.08);
			double num14 = num12 * 0.6 + num13 * 0.4;
			return (byte)(1 + (int)((num14 + 1.0) * 15.0) % 30);
		}
		case 8:
		{
			int num68 = Main.maxTilesX / 2;
			int num69 = Main.maxTilesY / 2;
			int num70 = Math.Abs(x - num68);
			int num71 = Math.Abs(y - num69);
			return (byte)(1 + ((num70 < 2 || num71 < 2) ? ((x + y) % 30) : ((num70 + num71) / 5 % 30)));
		}
		case 9:
		{
			int num53 = Main.maxTilesX / 2;
			int num54 = Main.maxTilesY / 2;
			int num55 = Math.Abs(x - num53);
			int num56 = Math.Abs(y - num54);
			int num57;
			for (num57 = num55 + num56; num57 > 30; num57 >>= 1)
			{
			}
			int num58 = num57 % 30;
			return (byte)(1 + num58);
		}
		case 10:
		{
			double num50 = Math.Sin((double)x * 0.1);
			double num51 = Math.Cos((double)y * 0.1);
			double num52 = (num50 + num51 + 2.0) / 4.0;
			return (byte)(1 + (int)(num52 * 29.0) % 30);
		}
		case 11:
		{
			int num41 = Main.maxTilesX / 2;
			int num42 = Main.maxTilesY / 2;
			double num43 = x - num41;
			double num44 = y - num42;
			double num45 = Math.Sqrt(num43 * num43 + num44 * num44);
			double num46 = 1.0 + Math.Sin(num45 * 0.1) * 0.5;
			int num47 = (int)Math.Round(num43 * num46 / 8.0);
			int num48 = (int)Math.Round(num44 * num46 / 8.0);
			int num49 = ((num47 * 3 + num48 * 5) % 30 + 30) % 30;
			return (byte)(1 + num49);
		}
		case 12:
		{
			int num34 = Main.maxTilesX / 2;
			int num35 = Main.maxTilesY / 2;
			double num36 = 2.0 / 3.0 * (double)(x - num34);
			double num37 = -1.0 / 3.0 * (double)(y - num34) + Math.Sqrt(3.0) / 3.0 * (double)(x - num35);
			int num38 = (int)Math.Round(num36 / 10.0);
			int num39 = (int)Math.Round(num37 / 10.0);
			int num40 = ((num38 + num39 * 2) % 30 + 30) % 30;
			return (byte)(1 + num40);
		}
		case 13:
		{
			int num25 = Main.maxTilesX / 2;
			int num26 = Main.maxTilesY / 2;
			int num27 = x - num25;
			int num28 = y - num26;
			int num29 = (int)(Math.Sin((double)num28 * 0.2) * 3.0);
			int num30 = (int)(Math.Sin((double)num27 * 0.2) * 3.0);
			int num31 = (num27 + num29) / 5 % 6;
			int num32 = (num28 + num30) / 5 % 6;
			int num33 = (num31 + num32) % 30;
			return (byte)(1 + (num33 + 30) % 30);
		}
		case 14:
		{
			int num20 = Main.maxTilesX / 2;
			int num21 = Main.maxTilesY / 2;
			double num22 = (double)(y - num20) * 0.1;
			double num23 = (double)(x - num21) * 0.1;
			int num24 = (int)((Math.Sin(num22 * 3.0) + Math.Cos(num23 * 5.0) + 2.0) / 4.0 * 29.0);
			return (byte)(1 + num24);
		}
		case 15:
		{
			int num15 = x - y;
			int num16 = x + y;
			int num17 = (int)Math.Floor((double)num15 / 8.0);
			int num18 = (int)Math.Floor((double)num16 / 8.0);
			int num19 = ((num17 + num18) % 30 + 30) % 30;
			return (byte)(1 + num19);
		}
		case 16:
		{
			int num = x % 4;
			int num2 = y % 4;
			int num3 = x / 4;
			int num4 = y / 4;
			switch ((num3 + num4 * 5) % 14)
			{
			case 0:
				if (num2 == 1)
				{
					return 23;
				}
				break;
			case 1:
				if (num == 1)
				{
					return 23;
				}
				break;
			case 2:
				if (num < 2 && num2 < 2)
				{
					return 23;
				}
				break;
			case 3:
				if ((num2 == 1 && num < 3) || (num == 1 && num2 == 0))
				{
					return 23;
				}
				break;
			case 4:
				if ((num2 == 1 && num < 3) || (num == 1 && num2 == 2))
				{
					return 23;
				}
				break;
			case 5:
				if ((num >= 1 && num2 == 0) || (num <= 1 && num2 == 1))
				{
					return 23;
				}
				break;
			case 6:
				if ((num <= 1 && num2 == 0) || (num >= 1 && num2 == 1))
				{
					return 23;
				}
				break;
			case 7:
				if ((num == 0 && num2 < 3) || (num2 == 2 && num <= 2))
				{
					return 23;
				}
				break;
			case 8:
				if ((num == 2 && num2 < 3) || (num2 == 2 && num <= 2))
				{
					return 23;
				}
				break;
			case 9:
				if ((num2 == 0 && num < 3) || (num == 2 && num2 <= 2))
				{
					return 23;
				}
				break;
			case 10:
				if ((num2 == 0 && num < 3) || (num == 0 && num2 <= 2))
				{
					return 23;
				}
				break;
			case 11:
				if ((num == 1 && num2 < 3) || (num2 == 1 && num == 0))
				{
					return 23;
				}
				break;
			case 12:
				if ((num == 0 && num2 < 2) || (num == 1 && num2 > 0))
				{
					return 23;
				}
				break;
			case 13:
				if ((num == 1 && num2 < 2) || (num == 2 && num2 > 0))
				{
					return 23;
				}
				break;
			}
			return 0;
		}
		default:
			return 0;
		}
	}

	public static async void TileExploitBrushV2(Point mouseToTile, int size, ushort tileType = 10, short frameX = 36, short frameY = 612, bool isActive = true)
	{
		for (int i = mouseToTile.X - size; i <= mouseToTile.X + size; i += 3)
		{
			for (int j = mouseToTile.Y + size; j >= mouseToTile.Y - size; j -= 3)
			{
				TeleportTools.FakeNetMessage13(new Vector2((float)i * 16f, (float)j * 16f), 58);
				TileSyncExploit(new Point(i, j), tileType, frameX, frameY, isActive);
				NetMessage.SendData(5, -1, -1, null, Main.myPlayer, 58f);
				while (PacketPacing.QueuedPackets > PacketPacing.TilePacketRate || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard)
				{
					await Task.Delay(500);
				}
			}
		}
	}

	public static void TileSyncExploit(Point hatRackPos, ushort tileType = 10, short frameX = 36, short frameY = 612, bool tileActive = true)
	{
		bool flag = false;
		try
		{
			if (!WorldGen.InWorld(hatRackPos.X - 1, hatRackPos.Y - 3))
			{
				return;
			}
			for (int i = hatRackPos.X - 1; i <= hatRackPos.X + 1; i++)
			{
				for (int j = hatRackPos.Y - 3; j <= hatRackPos.Y; j++)
				{
					if (!WorldGen.InWorld(i, j))
					{
						continue;
					}
					Tile tile = Framing.GetTileSafely(i, j);
					if (i == hatRackPos.X - 1 && j == hatRackPos.Y - 3)
					{
						if (tile.type == 579 && tile.frameX == 927 && tile.frameY != 0)
						{
						}
					}
					else if (tile.type != tileType || tile.frameX != frameX || tile.frameY != frameY || tile.active() != tileActive)
					{
						flag = true;
						break;
					}
				}
				if (flag)
				{
					break;
				}
			}
			if (!flag)
			{
				return;
			}
			for (int k = hatRackPos.X - 1; k <= hatRackPos.X + 1; k++)
			{
				for (int l = hatRackPos.Y - 3; l <= hatRackPos.Y; l++)
				{
					Tile tile2 = Framing.GetTileSafely(k, l);
					if (k != hatRackPos.X - 1 || l != hatRackPos.Y - 3)
					{
						tile2.active(tileActive);
						tile2.type = tileType;
						tile2.frameX = frameX;
						tile2.frameY = frameY;
					}
					else
					{
						tile2.active(active: true);
						tile2.type = 597;
						tile2.frameX = 927;
						tile2.frameY = 0;
					}
				}
			}
			NetMessage.SendTileSquare(Main.myPlayer, hatRackPos.X - 1, hatRackPos.Y - 3, 3, 4);
			NetMessage.SendData(17, -1, -1, null, 1, hatRackPos.X - 1, hatRackPos.Y - 3, 504f);
			NetMessage.SendData(17, -1, -1, null, 4, hatRackPos.X - 1, hatRackPos.Y - 3);
			AntiLag.NoKillSoundTime = 5;
			if (Main.netMode == 0)
			{
				WorldGen.RangeFrame(hatRackPos.X - 1, hatRackPos.Y - 3, hatRackPos.X + 1, hatRackPos.Y, resetFrame: true);
			}
		}
		catch
		{
		}
	}

	private static readonly int[] _brushCounts = new int[4];

	/// <summary>
	/// The packets of one brushed tile (player moved next to it, the item it needs held, then the edit), counted in
	/// <paramref name="counts"/>: tiles and walls, erases, liquids, paint. Used by the brush and by Multi Paste's bots.
	/// </summary>
	internal static void SendBrushPackets(BrushData data, int[] counts)
	{
		TeleportTools.FakeNetMessage13(new Vector2((float)((data.X + 2) * 16), (float)(data.Y * 16)), PlayerItemSlotID.InventoryMouseItem);
		if (data.BrushType != 1)
		{
			RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, PlayerItemSlotID.InventoryMouseItem, 0f, data.ItemNetId);
		}
		if (data.BrushType == 0)
		{
			if (!data.HasObject)
			{
				if (!TileID.Sets.BasicChest[data.ToPlace] && data.ToPlace != 88)
				{
					NetMessage.SendData(17, -1, -1, null, 1, data.X, data.Y, (int)data.ToPlace, data.Style);
					NetMessage.SendData(17, -1, -1, null, 14, data.X, data.Y);
				}
				else if (data.ToPlace == 88)
				{
					NetMessage.SendData(34, -1, -1, null, 2, data.X, data.Y, (int)data.Style);
				}
				else
				{
					WorldGen.PlaceTile(data.X, data.Y, data.ToPlace, mute: true, forced: false, Main.myPlayer, data.Style);
				}
			}
			else
			{
				TileObjectData.CallPostPlacementPlayerHook(data: new TileObject
				{
					type = data.ToPlace,
					style = data.Style,
					alternate = data.Alt,
					random = data.Rand
				}, tileX: data.X, tileY: data.Y, type: data.ToPlace, style: data.Style, dir: data.Dir, alternate: data.Alt);
				if (!Main.tileContainer[data.ToPlace] && data.ToPlace != 423)
				{
					NetMessage.SendObjectPlacement(-1, data.X, data.Y, data.ToPlace, data.Style, data.Alt, data.Rand, data.Dir);
				}
			}
			counts[0]++;
		}
		if (data.BrushType == 1)
		{
			if (data.ToPlace == 0)
			{
				NetMessage.SendData(17, -1, -1, null, 4, data.X, data.Y);
			}
			else if (data.ToPlace == 1)
			{
				NetMessage.SendData(17, -1, -1, null, 2, data.X, data.Y);
			}
			else
			{
				NetMessage.SendData(17, -1, -1, null, 4, data.X, data.Y);
				NetMessage.SendData(17, -1, -1, null, 2, data.X, data.Y);
				counts[1]++;
			}
			counts[1]++;
		}
		if (data.BrushType == 2)
		{
			NetMessage.SendData(17, -1, -1, null, 3, data.X, data.Y, (int)data.ToPlace);
			counts[0]++;
		}
		if (data.BrushType == 3)
		{
			NetMessage.sendWater(data.X, data.Y);
			counts[2]++;
		}
		if (data.BrushType == 4)
		{
			switch (data.Style)
			{
			case 0:
				NetMessage.SendData(63, -1, -1, null, data.X, data.Y, (int)data.ToPlace);
				break;
			case 1:
				NetMessage.SendData(64, -1, -1, null, data.X, data.Y, (int)data.ToPlace);
				break;
			case 2:
				NetMessage.SendData(63, -1, -1, null, data.X, data.Y, (int)data.ToPlace, 1f);
				break;
			case 3:
				NetMessage.SendData(64, -1, -1, null, data.X, data.Y, (int)data.ToPlace, 1f);
				break;
			case 4:
				NetMessage.SendData(63, -1, -1, null, data.X, data.Y);
				NetMessage.SendData(64, -1, -1, null, data.X, data.Y);
				NetMessage.SendData(63, -1, -1, null, data.X, data.Y, 0f, 1f);
				NetMessage.SendData(64, -1, -1, null, data.X, data.Y, 0f, 1f);
				counts[3] += 3;
				break;
			}
			counts[3]++;
		}
	}

	public static async void HandleBrushNetMessagesV3()
	{
		PacketPacing.HandlingPackets = true;
		try
		{
			if (TilesToBrush.Count > 0 && ((Main.netMode == 2 && !Main.dedServ) ? 2000 : PacketPacing.TilePacketRate) > 0)
			{
				int sentTilesAndWalls = 0;
				int sentErases = 0;
				int sentLiquids = 0;
				int sentPaint = 0;
				List<BrushData> keys = new List<BrushData>();
				lock (TilesToBrush)
				{
					keys.AddRange(TilesToBrush);
				}
				for (int i = 0; i < keys.Count; i++)
				{
					BrushData data = keys[i];
					if (Main.gameMenu)
					{
						PacketPacing.HandlingPackets = false;
						TilesToBrush.Clear();
						return;
					}
					while (((Main.netMode == 2 && !Main.dedServ) ? 2000 : PacketPacing.TilePacketRate) < 1)
					{
						if (Main.gameMenu)
						{
							PacketPacing.HandlingPackets = false;
							TilesToBrush.Clear();
							return;
						}
					}
					if (!Main.sectionManager.SectionLoaded(Netplay.GetSectionX(data.X), Netplay.GetSectionY(data.Y)))
					{
						ClientChat.Info($"Section at {data.X}, {data.Y} isn't loaded. Attempting to load...");
						MapTools.LoadChunk(Netplay.GetSectionX(data.X), Netplay.GetSectionY(data.Y));
						await Task.Delay(500);
					}
					SendBrushPackets(data, _brushCounts);
					sentTilesAndWalls += _brushCounts[0];
					sentErases += _brushCounts[1];
					sentLiquids += _brushCounts[2];
					sentPaint += _brushCounts[3];
					Array.Clear(_brushCounts, 0, 4);
					if (data.BrushType == 3 && ServerDetection.OnTshockServer && ++_tshockLiquidsThisSecond >= 150)
					{
						// TShock disables you (and reverts the liquid) past 200 liquid edits per second.
						_tshockLiquidsThisSecond = 0;
						await Task.Delay(1050);
					}
					switch (data.BrushType)
					{
					case 0:
					case 2:
						while (PacketPacing.TilePacketMaxMode && (sentTilesAndWalls > PacketPacing.TilePacketRate || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
						{
							sentTilesAndWalls = 0;
							await Task.Delay(500);
						}
						if (!PacketPacing.TilePacketMaxMode && (sentTilesAndWalls % PacketPacing.TilePacketRate == 0 || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
						{
							await Task.Delay(100);
						}
						break;
					case 1:
						while (PacketPacing.TilePacketMaxMode && (sentErases > PacketPacing.ErasePacketRate || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
						{
							sentErases = 0;
							await Task.Delay(500);
						}
						if (!PacketPacing.TilePacketMaxMode && (sentErases % PacketPacing.ErasePacketRate == 0 || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
						{
							await Task.Delay(100);
						}
						break;
					case 3:
						while (PacketPacing.TilePacketMaxMode && (sentLiquids > PacketPacing.LiquidPacketRate || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
						{
							sentLiquids = 0;
							await Task.Delay(500);
						}
						if (!PacketPacing.TilePacketMaxMode && (sentLiquids % PacketPacing.LiquidPacketRate == 0 || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
						{
							await Task.Delay(100);
						}
						break;
					case 4:
						while (PacketPacing.TilePacketMaxMode && (sentPaint > PacketPacing.PaintPacketRate || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
						{
							sentPaint = 0;
							await Task.Delay(500);
						}
						if (!PacketPacing.TilePacketMaxMode && (sentPaint % PacketPacing.PaintPacketRate == 0 || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
						{
							await Task.Delay(100);
						}
						break;
					}
					try
					{
						TilesToBrush.Remove(data);
					}
					catch
					{
					}
				}
			}
		}
		catch (Exception ex)
		{
			ClientChat.Error($"Handling Brush Packets Error!\n{ex}");
			TilesToBrush.Clear();
			ClientLog.LogException(ex);
		}
		PacketPacing.HandlingPackets = false;
		NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
		NetMessage.SendData(5, -1, -1, null, Main.myPlayer, PlayerItemSlotID.InventoryMouseItem, (int)Main.LocalPlayer.inventory[PlayerItemSlotID.InventoryMouseItem].prefix);
	}

	public static void Draw(SpriteBatch spriteBatch)
	{
		if (Main.gameMenu || Selection.SelectionMode || Selection.PasteMode)
		{
			return;
		}
		try
		{
			Rectangle value = default(Rectangle);
			value = new Rectangle(0, 0, 18, 18);
			Color val = new Color(0.9f, 0.9f, 0.9f, 0f) * 0.3f;
			_ = Main.LocalPlayer.HeldItem.createTile;
			_ = Main.LocalPlayer.HeldItem.createWall;
			SpriteEffects val2 = (SpriteEffects)0;
			Point val3 = Main.MouseWorld.ToTileCoordinates();
			if (BrushState.IsInTileBrush)
			{
				if (!Main.LocalPlayer.mouseInterface || Main.mapFullscreen)
				{
					Main.instance.MouseText(IsaClient.Languages.L10n.T("Tile Brush (" + Main.LocalPlayer.HeldItem.Name + ")"), 0, 0);
				}
				Texture2D value2 = TextureAssets.Extra[68].Value;
				if (!Main.mapFullscreen)
				{
					Vector2 val4 = default(Vector2);
					for (int i = val3.X - BrushState.TileBrushSize; i <= val3.X + BrushState.TileBrushSize; i++)
					{
						for (int j = val3.Y - BrushState.TileBrushSize; j <= val3.Y + BrushState.TileBrushSize; j++)
						{
							if (TileValidForBrush(val3, i, j, BrushState.TileBrushSize) && BrushState.WithinScreenBounds(i, j))
							{
								val4 = new Vector2((float)i * 16f, (float)j * 16f);
								val4 -= Main.screenPosition;
								if (Main.player[Main.myPlayer].gravDir == -1f)
								{
									val4.Y = (float)Main.screenHeight - val4.Y - 16f;
								}
								spriteBatch.Draw(value2, val4, (Rectangle?)value, val, 0f, Vector2.Zero, 1f, val2, 0f);
							}
						}
					}
				}
				else
				{
					TeleportTools.TryGetMapCoordinatesV2(Terraria.GameInput.PlayerInput.UnscaledMouseX, Terraria.GameInput.PlayerInput.UnscaledMouseY, 0, out var result);
					if (result.X > -1 && result.Y > -1)
					{
						int tileBrushSize = BrushState.TileBrushSize;
						int num = result.X - tileBrushSize;
						int num2 = result.X + tileBrushSize;
						int num3 = result.Y - tileBrushSize;
						int num4 = result.Y + tileBrushSize;
						Rectangle val5 = default(Rectangle);
						Rectangle val6 = default(Rectangle);
						Rectangle val7 = default(Rectangle);
						for (int k = num; k <= num2; k++)
						{
							int num5 = 0;
							if (BrushState.BrushCircle)
							{
								for (int l = num3; l <= num4; l++)
								{
									int num6 = Math.Abs(k - result.X);
									int num7 = Math.Abs(l - result.Y);
									if ((int)Math.Floor(Math.Sqrt(num6 * num6 + num7 * num7)) <= BrushState.TileBrushSize)
									{
										num5++;
									}
								}
							}
							else
							{
								num5 = num4 - num3 + 1;
							}
							TeleportTools.TryGetMapCoordinatesV2(k, result.Y - num5 / 2, 1, out var result2);
							TeleportTools.TryGetMapCoordinatesV2(k + 1, result.Y - num5 / 2 + num5, 1, out var result3);
							val5 = new Rectangle(result2.X, result2.Y, result3.X - result2.X, result3.Y - result2.Y);
							val6 = new Rectangle(0, 0, Main.screenWidth + 1, Main.screenHeight + 1);
							Rectangle.Intersect(ref val6, ref val5, out val7);
							if (val7.Width > 0 && val7.Height > 0)
							{
								val7.Offset(-val6.X, -val6.Y);
								spriteBatch.Draw(TextureAssets.MagicPixel.Value, val7, (Rectangle?)value, val, 0f, Vector2.Zero, (SpriteEffects)0, 0f);
							}
						}
					}
				}
			}
			if (BrushState.IsInEraseBrush)
			{
				if (!Main.LocalPlayer.mouseInterface || Main.mapFullscreen)
				{
					Main.instance.MouseText(IsaClient.Languages.L10n.T("Erase Brush"), 0, 0);
				}
				if (!Main.mapFullscreen)
				{
					Texture2D value3 = TextureAssets.Extra[68].Value;
					Vector2 val8 = default(Vector2);
					for (int m = val3.X - BrushState.EraseBrushSize; m <= val3.X + BrushState.EraseBrushSize; m++)
					{
						for (int n = val3.Y - BrushState.EraseBrushSize; n <= val3.Y + BrushState.EraseBrushSize; n++)
						{
							if (TileValidForBrush(val3, m, n, BrushState.EraseBrushSize) && BrushState.WithinScreenBounds(m, n))
							{
								val8 = new Vector2((float)m * 16f, (float)n * 16f);
								val8 -= Main.screenPosition;
								if (Main.player[Main.myPlayer].gravDir == -1f)
								{
									val8.Y = (float)Main.screenHeight - val8.Y - 16f;
								}
								spriteBatch.Draw(value3, val8, (Rectangle?)value, val, 0f, Vector2.Zero, 1f, val2, 0f);
							}
						}
					}
				}
				else
				{
					TeleportTools.TryGetMapCoordinatesV2(Terraria.GameInput.PlayerInput.UnscaledMouseX, Terraria.GameInput.PlayerInput.UnscaledMouseY, 0, out var result4);
					if (result4.X > -1 && result4.Y > -1)
					{
						int eraseBrushSize = BrushState.EraseBrushSize;
						int num8 = result4.X - eraseBrushSize;
						int num9 = result4.X + eraseBrushSize;
						int num10 = result4.Y - eraseBrushSize;
						int num11 = result4.Y + eraseBrushSize;
						Rectangle val9 = default(Rectangle);
						Rectangle val10 = default(Rectangle);
						Rectangle val11 = default(Rectangle);
						for (int num12 = num8; num12 <= num9; num12++)
						{
							int num13 = 0;
							if (BrushState.BrushCircle)
							{
								for (int num14 = num10; num14 <= num11; num14++)
								{
									int num15 = Math.Abs(num12 - result4.X);
									int num16 = Math.Abs(num14 - result4.Y);
									if ((int)Math.Floor(Math.Sqrt(num15 * num15 + num16 * num16)) <= BrushState.EraseBrushSize)
									{
										num13++;
									}
								}
							}
							else
							{
								num13 = num11 - num10 + 1;
							}
							TeleportTools.TryGetMapCoordinatesV2(num12, result4.Y - num13 / 2, 1, out var result5);
							TeleportTools.TryGetMapCoordinatesV2(num12 + 1, result4.Y - num13 / 2 + num13, 1, out var result6);
							val9 = new Rectangle(result5.X, result5.Y, result6.X - result5.X, result6.Y - result5.Y);
							val10 = new Rectangle(0, 0, Main.screenWidth + 1, Main.screenHeight + 1);
							Rectangle.Intersect(ref val10, ref val9, out val11);
							if (val11.Width > 0 && val11.Height > 0)
							{
								val11.Offset(-val10.X, -val10.Y);
								spriteBatch.Draw(TextureAssets.MagicPixel.Value, val11, (Rectangle?)value, val, 0f, Vector2.Zero, (SpriteEffects)0, 0f);
							}
						}
					}
				}
			}
			if (BrushState.IsInWallBrush)
			{
				if (!Main.LocalPlayer.mouseInterface || Main.mapFullscreen)
				{
					Main.instance.MouseText(IsaClient.Languages.L10n.T("Wall Brush (" + Main.LocalPlayer.HeldItem.Name + ")"), 0, 0);
				}
				if (!Main.mapFullscreen)
				{
					Texture2D value4 = TextureAssets.Extra[68].Value;
					Vector2 val12 = default(Vector2);
					for (int num17 = val3.X - BrushState.WallBrushSize; num17 <= val3.X + BrushState.WallBrushSize; num17++)
					{
						for (int num18 = val3.Y - BrushState.WallBrushSize; num18 <= val3.Y + BrushState.WallBrushSize; num18++)
						{
							if (TileValidForBrush(val3, num17, num18, BrushState.WallBrushSize) && BrushState.WithinScreenBounds(num17, num18))
							{
								val12 = new Vector2((float)num17 * 16f, (float)num18 * 16f);
								val12 -= Main.screenPosition;
								if (Main.player[Main.myPlayer].gravDir == -1f)
								{
									val12.Y = (float)Main.screenHeight - val12.Y - 16f;
								}
								spriteBatch.Draw(value4, val12, (Rectangle?)value, val, 0f, Vector2.Zero, 1f, val2, 0f);
							}
						}
					}
				}
				else
				{
					TeleportTools.TryGetMapCoordinatesV2(Terraria.GameInput.PlayerInput.UnscaledMouseX, Terraria.GameInput.PlayerInput.UnscaledMouseY, 0, out var result7);
					if (result7.X > -1 && result7.Y > -1)
					{
						int wallBrushSize = BrushState.WallBrushSize;
						int num19 = result7.X - wallBrushSize;
						int num20 = result7.X + wallBrushSize;
						int num21 = result7.Y - wallBrushSize;
						int num22 = result7.Y + wallBrushSize;
						Rectangle val13 = default(Rectangle);
						Rectangle val14 = default(Rectangle);
						Rectangle val15 = default(Rectangle);
						for (int num23 = num19; num23 <= num20; num23++)
						{
							int num24 = 0;
							if (BrushState.BrushCircle)
							{
								for (int num25 = num21; num25 <= num22; num25++)
								{
									int num26 = Math.Abs(num23 - result7.X);
									int num27 = Math.Abs(num25 - result7.Y);
									if ((int)Math.Floor(Math.Sqrt(num26 * num26 + num27 * num27)) <= BrushState.WallBrushSize)
									{
										num24++;
									}
								}
							}
							else
							{
								num24 = num22 - num21 + 1;
							}
							TeleportTools.TryGetMapCoordinatesV2(num23, result7.Y - num24 / 2, 1, out var result8);
							TeleportTools.TryGetMapCoordinatesV2(num23 + 1, result7.Y - num24 / 2 + num24, 1, out var result9);
							val13 = new Rectangle(result8.X, result8.Y, result9.X - result8.X, result9.Y - result8.Y);
							val14 = new Rectangle(0, 0, Main.screenWidth + 1, Main.screenHeight + 1);
							Rectangle.Intersect(ref val14, ref val13, out val15);
							if (val15.Width > 0 && val15.Height > 0)
							{
								val15.Offset(-val14.X, -val14.Y);
								spriteBatch.Draw(TextureAssets.MagicPixel.Value, val15, (Rectangle?)value, val, 0f, Vector2.Zero, (SpriteEffects)0, 0f);
							}
						}
					}
				}
			}
			if (BrushState.IsInLiquidBrush)
			{
				if (!Main.LocalPlayer.mouseInterface || Main.mapFullscreen)
				{
					Main.instance.MouseText(IsaClient.Languages.L10n.T("Liquid Brush (" + Main.LocalPlayer.HeldItem.Name + ")"), 0, 0);
				}
				if (!Main.mapFullscreen)
				{
					Texture2D value5 = TextureAssets.Extra[68].Value;
					Vector2 val16 = default(Vector2);
					for (int num28 = val3.X - BrushState.LiquidBrushSize; num28 <= val3.X + BrushState.LiquidBrushSize; num28++)
					{
						for (int num29 = val3.Y - BrushState.LiquidBrushSize; num29 <= val3.Y + BrushState.LiquidBrushSize; num29++)
						{
							if (TileValidForBrush(val3, num28, num29, BrushState.LiquidBrushSize) && BrushState.WithinScreenBounds(num28, num29))
							{
								val16 = new Vector2((float)num28 * 16f, (float)num29 * 16f);
								val16 -= Main.screenPosition;
								if (Main.player[Main.myPlayer].gravDir == -1f)
								{
									val16.Y = (float)Main.screenHeight - val16.Y - 16f;
								}
								spriteBatch.Draw(value5, val16, (Rectangle?)value, val, 0f, Vector2.Zero, 1f, val2, 0f);
							}
						}
					}
					return;
				}
				TeleportTools.TryGetMapCoordinatesV2(Terraria.GameInput.PlayerInput.UnscaledMouseX, Terraria.GameInput.PlayerInput.UnscaledMouseY, 0, out var result10);
				if (result10.X > -1 && result10.Y > -1)
				{
					int liquidBrushSize = BrushState.LiquidBrushSize;
					int num30 = result10.X - liquidBrushSize;
					int num31 = result10.X + liquidBrushSize;
					int num32 = result10.Y - liquidBrushSize;
					int num33 = result10.Y + liquidBrushSize;
					Rectangle val17 = default(Rectangle);
					Rectangle val18 = default(Rectangle);
					Rectangle val19 = default(Rectangle);
					for (int num34 = num30; num34 <= num31; num34++)
					{
						int num35 = 0;
						if (BrushState.BrushCircle)
						{
							for (int num36 = num32; num36 <= num33; num36++)
							{
								int num37 = Math.Abs(num34 - result10.X);
								int num38 = Math.Abs(num36 - result10.Y);
								if ((int)Math.Floor(Math.Sqrt(num37 * num37 + num38 * num38)) <= BrushState.LiquidBrushSize)
								{
									num35++;
								}
							}
						}
						else
						{
							num35 = num33 - num32 + 1;
						}
						TeleportTools.TryGetMapCoordinatesV2(num34, result10.Y - num35 / 2, 1, out var result11);
						TeleportTools.TryGetMapCoordinatesV2(num34 + 1, result10.Y - num35 / 2 + num35, 1, out var result12);
						val17 = new Rectangle(result11.X, result11.Y, result12.X - result11.X, result12.Y - result11.Y);
						val18 = new Rectangle(0, 0, Main.screenWidth + 1, Main.screenHeight + 1);
						Rectangle.Intersect(ref val18, ref val17, out val19);
						if (val19.Width > 0 && val19.Height > 0)
						{
							val19.Offset(-val18.X, -val18.Y);
							spriteBatch.Draw(TextureAssets.MagicPixel.Value, val19, (Rectangle?)value, val, 0f, Vector2.Zero, (SpriteEffects)0, 0f);
						}
					}
				}
			}
			if (!BrushState.IsInTilePaintBrush)
			{
				return;
			}
			if (!Main.LocalPlayer.mouseInterface || Main.mapFullscreen)
			{
				Item item = Main.LocalPlayer.FindPaintOrCoating();
				Main.instance.MouseText(IsaClient.Languages.L10n.T($"Super Paint {BrushState.BrushOrRollerOrSscraper()} ({((Placement.RainbowPaintType > 0) ? "Rainbow Paint" : ((item != null && !BrushState.HoldingPaintScraper) ? item.Name : (BrushState.HoldingPaintScraper ? Main.LocalPlayer.HeldItem.Name : "No Paint...")))})"), 0, 0);
			}
			if (!Main.mapFullscreen)
			{
				Texture2D value6 = TextureAssets.Extra[68].Value;
				Vector2 val20 = default(Vector2);
				for (int num39 = val3.X - BrushState.TilePaintBrushSize; num39 <= val3.X + BrushState.TilePaintBrushSize; num39++)
				{
					for (int num40 = val3.Y - BrushState.TilePaintBrushSize; num40 <= val3.Y + BrushState.TilePaintBrushSize; num40++)
					{
						if (TileValidForBrush(val3, num39, num40, BrushState.TilePaintBrushSize) && BrushState.WithinScreenBounds(num39, num40))
						{
							val20 = new Vector2((float)num39 * 16f, (float)num40 * 16f);
							val20 -= Main.screenPosition;
							if (Main.player[Main.myPlayer].gravDir == -1f)
							{
								val20.Y = (float)Main.screenHeight - val20.Y - 16f;
							}
							spriteBatch.Draw(value6, val20, (Rectangle?)value, val, 0f, Vector2.Zero, 1f, val2, 0f);
						}
					}
				}
				return;
			}
			TeleportTools.TryGetMapCoordinatesV2(Terraria.GameInput.PlayerInput.UnscaledMouseX, Terraria.GameInput.PlayerInput.UnscaledMouseY, 0, out var result13);
			if (result13.X <= -1 || result13.Y <= -1)
			{
				return;
			}
			int tilePaintBrushSize = BrushState.TilePaintBrushSize;
			int num41 = result13.X - tilePaintBrushSize;
			int num42 = result13.X + tilePaintBrushSize;
			int num43 = result13.Y - tilePaintBrushSize;
			int num44 = result13.Y + tilePaintBrushSize;
			Rectangle val21 = default(Rectangle);
			Rectangle val22 = default(Rectangle);
			Rectangle val23 = default(Rectangle);
			for (int num45 = num41; num45 <= num42; num45++)
			{
				int num46 = 0;
				if (BrushState.BrushCircle)
				{
					for (int num47 = num43; num47 <= num44; num47++)
					{
						int num48 = Math.Abs(num45 - result13.X);
						int num49 = Math.Abs(num47 - result13.Y);
						if ((int)Math.Floor(Math.Sqrt(num48 * num48 + num49 * num49)) <= BrushState.TilePaintBrushSize)
						{
							num46++;
						}
					}
				}
				else
				{
					num46 = num44 - num43 + 1;
				}
				TeleportTools.TryGetMapCoordinatesV2(num45, result13.Y - num46 / 2, 1, out var result14);
				TeleportTools.TryGetMapCoordinatesV2(num45 + 1, result13.Y - num46 / 2 + num46, 1, out var result15);
				val21 = new Rectangle(result14.X, result14.Y, result15.X - result14.X, result15.Y - result14.Y);
				val22 = new Rectangle(0, 0, Main.screenWidth + 1, Main.screenHeight + 1);
				Rectangle.Intersect(ref val22, ref val21, out val23);
				if (val23.Width > 0 && val23.Height > 0)
				{
					val23.Offset(-val22.X, -val22.Y);
					spriteBatch.Draw(TextureAssets.MagicPixel.Value, val23, (Rectangle?)value, val, 0f, Vector2.Zero, (SpriteEffects)0, 0f);
				}
			}
		}
		catch (Exception ex)
		{
			BrushState.DisableBrushes();
			ClientChat.Error($"Drawing Brushes error!\n{ex}");
			ClientLog.LogException(ex);
		}
	}
}
