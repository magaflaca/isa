using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;
using IsaClient.Core;
using IsaClient.Movement;
using IsaClient.Networking;

namespace IsaClient.Building;

/// <summary>World edit brush settings and state: which brush is on, sizes, shape, pattern and the tiles marked for the next stroke.</summary>
public static class BrushState
{
	public const string UseBrushKeyName = "Use Brush";

	public const string UseBrushMapKeyName = "Use Brush (Map)";

	public static bool TileSyncExploit;

	public static bool BrushMode;

	public static bool TileBrush;

	public static bool LiquidBrush;

	public static bool EraseBrush;

	public static Vector2 TileBrushFalseCoords;

	[Setting("TileBrushSize", Remembered = true)]
	public static int TileBrushSize;

	[Setting("EraseBrushSize", Remembered = true)]
	public static int EraseBrushSize;

	[Setting("LiquidBrushSize", Remembered = true)]
	public static int LiquidBrushSize;

	public static bool WallBrush;

	[Setting("WallBrushSize", Remembered = true)]
	public static int WallBrushSize;

	public static bool EraseBrushMap;

	public static int EraseBrushMapSize;

	[Setting("SuperPaintBrushSize", Remembered = true)]
	public static int TilePaintBrushSize;

	public static byte TilePaintBrushPaintID;

	public static bool TilePaintBrush;

	[Old("Deprecated. Use tilesToBrush.")]
	public static ConcurrentDictionary<Point, int> MarkedTilesForTilePaintBrush = new ConcurrentDictionary<Point, int>();

	public static bool RunningBrushThread = false;

	public static bool RunningBrushMapThread = false;

	[Old("Deprecated. Use tilesToBrush.")]
	public static ConcurrentDictionary<Point, Point> MarkedTilesForTileBrush = new ConcurrentDictionary<Point, Point>();

	[Old("Deprecated. Use tilesToBrush.")]
	public static ConcurrentDictionary<Point, int> MarkedTilesForEraseBrush = new ConcurrentDictionary<Point, int>();

	[Old("Deprecated. Use tilesToBrush.")]
	public static int SavedSelectedItemForTileBrush;

	[Old("Deprecated. Use tilesToBrush.")]
	public static int SavedCreateTileForTileBrush;

	[Old("Deprecated. Use tilesToBrush.")]
	public static int SavedPlaceStyleForTileBrush;

	public static bool TileBrushMap;

	public static int TileBrushMapSize;

	[Old("Deprecated. Use tilesToBrush.")]
	public static ConcurrentDictionary<Point, int> MarkedTilesForWallBrush = new ConcurrentDictionary<Point, int>();

	[Old("Deprecated. Use tilesToBrush.")]
	public static ConcurrentDictionary<Point, int> MarkedTilesForLiquidAndDryBrush = new ConcurrentDictionary<Point, int>();

	[Setting("BrushCircle", Remembered = true)]
	public static bool BrushCircle;

	[Setting("BrushPattern", Remembered = true, Min = 0, Max = 40)]
	public static int BrushPattern = 0;

	public static int PrevTileBrushSize;

	public static int PrevEraseBrushSize;

	public static int PrevLiquidBrushSize;

	public static int PrevWallBrushSize;

	public static bool TileExploitBrush;

	public static short TileExploitBrushFX;

	public static short TileExploitBrushFY;

	public static bool TileExploitBrushActive;

	public static ushort TileExploitBrushType;

	public static int TileExploitBrushSize;

	public static bool HoldingAnyPaintTool
	{
		get
		{
			if (!HoldingPaintBrush && !HoldingPaintRoller)
			{
				return HoldingPaintScraper;
			}
			return true;
		}
	}

	public static bool HoldingAnyLiquidBrush
	{
		get
		{
			if (!ItemID.Sets.LiquidBuckets[Main.LocalPlayer.HeldItem.type])
			{
				return ItemID.Sets.LiquidRemoval[Main.LocalPlayer.HeldItem.type];
			}
			return true;
		}
	}

	public static bool HoldingPaintBrush
	{
		get
		{
			if (Main.LocalPlayer.HeldItem.type != 1071)
			{
				return Main.LocalPlayer.HeldItem.type == 1543;
			}
			return true;
		}
	}

	public static bool HoldingPaintRoller
	{
		get
		{
			if (Main.LocalPlayer.HeldItem.type != 1072)
			{
				return Main.LocalPlayer.HeldItem.type == 1544;
			}
			return true;
		}
	}

	public static bool HoldingPaintScraper => ItemID.Sets.IsPaintScraper[Main.LocalPlayer.HeldItem.type];

	public static bool IsInTileBrush
	{
		get
		{
			if (!Main.gameMenu && TileBrush && (Main.LocalPlayer.HeldItem.createTile >= 0 || Main.LocalPlayer.HeldItem.type == 4262 || Main.LocalPlayer.HeldItem.type == 496))
			{
				return !Selection.SelectionMode;
			}
			return false;
		}
	}

	public static bool IsInWallBrush
	{
		get
		{
			if (!Main.gameMenu && WallBrush && Main.LocalPlayer.HeldItem.createWall > 0 && !IsInTileBrush)
			{
				return !Selection.SelectionMode;
			}
			return false;
		}
	}

	public static bool IsInLiquidBrush
	{
		get
		{
			if (!Main.gameMenu && LiquidBrush && HoldingAnyLiquidBrush)
			{
				return !Selection.SelectionMode;
			}
			return false;
		}
	}

	public static bool IsInTilePaintBrush
	{
		get
		{
			if (!Main.gameMenu && TilePaintBrush && HoldingAnyPaintTool)
			{
				return !Selection.SelectionMode;
			}
			return false;
		}
	}

	public static bool IsInEraseBrush
	{
		get
		{
			if (!Main.gameMenu && EraseBrush && !IsInTileBrush && !IsInWallBrush && !IsInLiquidBrush && !HoldingAnyLiquidBrush && !IsInTilePaintBrush && !HoldingAnyPaintTool)
			{
				return !Selection.SelectionMode;
			}
			return false;
		}
	}

	public static bool IsInAnyBrush
	{
		get
		{
			if (IsInTileBrush || IsInWallBrush || IsInLiquidBrush || IsInTilePaintBrush || IsInEraseBrush || TileExploitBrush)
			{
				return !Selection.SelectionMode;
			}
			return false;
		}
	}

	public static bool IsInAnyWorldEdit
	{
		get
		{
			if (!IsInAnyBrush && !Selection.SelectionMode && !PixelArt.DrawPhotoMode && !Selection.PasteMode)
			{
				return Selection.CopyMode;
			}
			return true;
		}
	}

	public static string BrushOrRollerOrSscraper()
	{
		if (HoldingPaintRoller)
		{
			return "Roller";
		}
		if (HoldingPaintScraper)
		{
			return "Scraper";
		}
		return "Brush";
	}

	public static bool WithinScreenBounds(int x, int y, int fluff = -1)
	{
		Vector2 val = default(Vector2);
		val = new Vector2((float)x - Main.screenPosition.X / 16f, (float)y - Main.screenPosition.Y / 16f);
		if (val.X >= (float)fluff && val.X < 150f - (float)fluff && val.Y >= (float)fluff)
		{
			return val.Y < 80f - (float)fluff;
		}
		return false;
	}

	public static void DisableBrushes()
	{
		TileBrush = false;
		EraseBrush = false;
		LiquidBrush = false;
		LiquidBrush = false;
		TileExploitBrush = false;
		TilePaintBrush = false;
	}

	[Old("Deprecated. Use HandleBrushNetMessagesV3 instead.")]
	public static async void HandleBrushNetMessagesV2()
	{
		PacketPacing.HandlingPackets = true;
		try
		{
			int waitTime = 500;
			int value;
			if (MarkedTilesForEraseBrush.Count > 0 && ((Main.netMode == 2 && !Main.dedServ) ? 2000 : PacketPacing.TilePacketRate) > 0)
			{
				int num3 = 0;
				foreach (KeyValuePair<Point, int> keyValuePair2 in MarkedTilesForEraseBrush.ToList())
				{
					if (Main.gameMenu)
					{
						ClientChat.Info("A tile was null when sending data. Or you entered the game menu.");
						return;
					}
					if (keyValuePair2.Value == 1)
					{
						NetMessage.SendData(17, -1, -1, null, 4, keyValuePair2.Key.X, keyValuePair2.Key.Y);
					}
					else if (keyValuePair2.Value == 2)
					{
						NetMessage.SendData(17, -1, -1, null, 2, keyValuePair2.Key.X, keyValuePair2.Key.Y);
					}
					else
					{
						NetMessage.SendData(17, -1, -1, null, 4, keyValuePair2.Key.X, keyValuePair2.Key.Y);
						NetMessage.SendData(17, -1, -1, null, 2, keyValuePair2.Key.X, keyValuePair2.Key.Y);
					}
					if (!PacketPacing.TilePacketMaxMode)
					{
						if (num3 % ((Main.netMode == 2 && !Main.dedServ) ? 2000 : PacketPacing.TilePacketRate) == 0 || Performance.LowFpsGuard)
						{
							await Task.Delay(100);
						}
					}
					else
					{
						while (PacketPacing.QueuedPackets > ((Main.netMode == 2 && !Main.dedServ) ? 2000 : PacketPacing.TilePacketRate) || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard)
						{
							await Task.Delay(waitTime);
						}
					}
					try
					{
						MarkedTilesForEraseBrush.TryRemove(keyValuePair2.Key, out value);
					}
					catch
					{
					}
					value = num3++;
				}
			}
			if (MarkedTilesForTileBrush.Count > 0)
			{
				int num3 = 0;
				foreach (KeyValuePair<Point, Point> keyValuePair3 in MarkedTilesForTileBrush.ToList())
				{
					if (Framing.GetTileSafely(keyValuePair3.Key.X, keyValuePair3.Key.Y) == null || Main.gameMenu)
					{
						ClientChat.Info("A tile was null when sending data. Or you entered the game menu.");
						return;
					}
					if (keyValuePair3.Value.X == 1)
					{
						TeleportTools.FakeNetMessage13(new Vector2((float)((keyValuePair3.Key.X + 2) * 16), (float)(keyValuePair3.Key.Y * 16)), SavedSelectedItemForTileBrush);
						NetMessage.SendData(17, -1, -1, null, 1, keyValuePair3.Key.X, keyValuePair3.Key.Y, SavedCreateTileForTileBrush, SavedPlaceStyleForTileBrush);
						NetMessage.SendData(17, -1, -1, null, 14, keyValuePair3.Key.X, keyValuePair3.Key.Y);
					}
					if (keyValuePair3.Value.X == 2)
					{
						TeleportTools.FakeNetMessage13(new Vector2((float)((keyValuePair3.Key.X + 2) * 16), (float)(keyValuePair3.Key.Y * 16)), SavedSelectedItemForTileBrush);
						NetMessage.SendObjectPlacement(Main.myPlayer, keyValuePair3.Key.X, keyValuePair3.Key.Y, SavedCreateTileForTileBrush, keyValuePair3.Value.Y, 0, 0, Main.LocalPlayer.direction);
					}
					if (!PacketPacing.TilePacketMaxMode)
					{
						if (num3 % ((Main.netMode == 2 && !Main.dedServ) ? 2000 : PacketPacing.TilePacketRate) == 0 || Performance.LowFpsGuard)
						{
							await Task.Delay(100);
						}
					}
					else
					{
						while (PacketPacing.QueuedPackets > ((Main.netMode == 2 && !Main.dedServ) ? 2000 : PacketPacing.TilePacketRate) || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard)
						{
							await Task.Delay(waitTime);
						}
					}
					try
					{
						MarkedTilesForTileBrush.TryRemove(keyValuePair3.Key, out var _);
					}
					catch
					{
					}
					value = num3++;
				}
			}
			if (MarkedTilesForWallBrush.Count > 0)
			{
				int num3 = 0;
				foreach (KeyValuePair<Point, int> keyValuePair2 in MarkedTilesForWallBrush.ToList())
				{
					if (Framing.GetTileSafely(keyValuePair2.Key.X, keyValuePair2.Key.Y) == null || Main.gameMenu)
					{
						ClientChat.Info("A tile was null when sending data. Or you entered the game menu.");
						return;
					}
					if (keyValuePair2.Value == 1)
					{
						TeleportTools.FakeNetMessage13(new Vector2((float)((keyValuePair2.Key.X + 2) * 16), (float)(keyValuePair2.Key.Y * 16)), Main.LocalPlayer.selectedItem);
						NetMessage.SendData(17, -1, -1, null, 3, keyValuePair2.Key.X, keyValuePair2.Key.Y, Main.LocalPlayer.HeldItem.createWall, Main.LocalPlayer.HeldItem.placeStyle);
					}
					if (!PacketPacing.TilePacketMaxMode)
					{
						if (num3 % ((Main.netMode == 2 && !Main.dedServ) ? 2000 : PacketPacing.TilePacketRate) == 0 || Performance.LowFpsGuard)
						{
							await Task.Delay(100);
						}
					}
					else
					{
						while (PacketPacing.QueuedPackets > ((Main.netMode == 2 && !Main.dedServ) ? 2000 : PacketPacing.TilePacketRate) || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard)
						{
							await Task.Delay(waitTime);
						}
					}
					try
					{
						MarkedTilesForWallBrush.TryRemove(keyValuePair2.Key, out value);
					}
					catch
					{
					}
					value = num3++;
				}
			}
			if (MarkedTilesForLiquidAndDryBrush.Count > 0)
			{
				int num3 = 0;
				foreach (KeyValuePair<Point, int> keyValuePair2 in MarkedTilesForLiquidAndDryBrush.ToList())
				{
					if (Framing.GetTileSafely(keyValuePair2.Key.X, keyValuePair2.Key.Y) == null || Main.gameMenu)
					{
						ClientChat.Info("A tile was null when sending data. Or you entered the game menu.");
						return;
					}
					if (keyValuePair2.Value == 0 || keyValuePair2.Value == 1 || keyValuePair2.Value == 2 || keyValuePair2.Value == 3 || keyValuePair2.Value == 4)
					{
						TeleportTools.FakeNetMessage13(new Vector2((float)((keyValuePair2.Key.X + 2) * 16), (float)(keyValuePair2.Key.Y * 16)), Main.LocalPlayer.selectedItem);
						NetMessage.sendWater(keyValuePair2.Key.X, keyValuePair2.Key.Y);
						if (ServerDetection.OnTshockServer && num3 > 0 && num3 % 150 == 0)
						{
							// TShock disables you (and reverts the liquid) past 200 liquid edits per second.
							await Task.Delay(1050);
						}
					}
					if (!PacketPacing.TilePacketMaxMode)
					{
						if (num3 % ((Main.netMode == 2 && !Main.dedServ) ? 2000 : PacketPacing.TilePacketRate) == 0 || Performance.LowFpsGuard)
						{
							await Task.Delay(100);
						}
					}
					else
					{
						while (PacketPacing.QueuedPackets > ((Main.netMode == 2 && !Main.dedServ) ? 2000 : PacketPacing.TilePacketRate) || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard)
						{
							await Task.Delay(waitTime);
						}
					}
					try
					{
						MarkedTilesForLiquidAndDryBrush.TryRemove(keyValuePair2.Key, out value);
					}
					catch
					{
					}
					value = num3++;
				}
			}
			if (MarkedTilesForTilePaintBrush.Count > 0)
			{
				int num3 = 0;
				foreach (KeyValuePair<Point, int> keyValuePair2 in MarkedTilesForTilePaintBrush.ToList())
				{
					if (Main.gameMenu)
					{
						ClientChat.Info("A tile was null when sending data. Or you entered the game menu.");
						return;
					}
					byte b = Framing.GetTileSafely(keyValuePair2.Key.X, keyValuePair2.Key.Y).color();
					byte b2 = 0;
					if (Framing.GetTileSafely(keyValuePair2.Key.X, keyValuePair2.Key.Y).fullbrightBlock())
					{
						b2 = 1;
					}
					if (Framing.GetTileSafely(keyValuePair2.Key.X, keyValuePair2.Key.Y).invisibleBlock())
					{
						b2 = 2;
					}
					byte b3 = Framing.GetTileSafely(keyValuePair2.Key.X, keyValuePair2.Key.Y).wallColor();
					byte b4 = 0;
					if (Framing.GetTileSafely(keyValuePair2.Key.X, keyValuePair2.Key.Y).fullbrightWall())
					{
						b4 = 1;
					}
					if (Framing.GetTileSafely(keyValuePair2.Key.X, keyValuePair2.Key.Y).invisibleWall())
					{
						b4 = 2;
					}
					TeleportTools.FakeNetMessage13(new Vector2((float)((keyValuePair2.Key.X + 2) * 16), (float)(keyValuePair2.Key.Y * 16)), Main.LocalPlayer.selectedItem);
					if (keyValuePair2.Value == 1)
					{
						NetMessage.SendData(64, -1, -1, null, keyValuePair2.Key.X, keyValuePair2.Key.Y, (int)b3);
					}
					else if (keyValuePair2.Value == 2)
					{
						NetMessage.SendData(63, -1, -1, null, keyValuePair2.Key.X, keyValuePair2.Key.Y, (int)b2, 1f);
					}
					else if (keyValuePair2.Value == 3)
					{
						NetMessage.SendData(64, -1, -1, null, keyValuePair2.Key.X, keyValuePair2.Key.Y, (int)b4, 1f);
					}
					else
					{
						NetMessage.SendData(63, -1, -1, null, keyValuePair2.Key.X, keyValuePair2.Key.Y, (int)b);
					}
					if (!PacketPacing.TilePacketMaxMode)
					{
						if (num3 % ((Main.netMode == 2 && !Main.dedServ) ? 2000 : PacketPacing.TilePacketRate) == 0 || Performance.LowFpsGuard)
						{
							await Task.Delay(100);
						}
					}
					else
					{
						while (PacketPacing.QueuedPackets > ((Main.netMode == 2 && !Main.dedServ) ? 2000 : PacketPacing.TilePacketRate) || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard)
						{
							await Task.Delay(waitTime);
						}
					}
					try
					{
						MarkedTilesForTilePaintBrush.TryRemove(keyValuePair2.Key, out value);
					}
					catch
					{
					}
					value = num3++;
				}
			}
		}
		catch (Exception ex)
		{
			ClientChat.Error($"Handling Brush Packets Error!\n{ex}");
			MarkedTilesForEraseBrush.Clear();
			MarkedTilesForLiquidAndDryBrush.Clear();
			MarkedTilesForTileBrush.Clear();
			MarkedTilesForWallBrush.Clear();
			MarkedTilesForTilePaintBrush.Clear();
			ClientLog.LogException(ex);
		}
		PacketPacing.HandlingPackets = false;
	}

	#region Chat commands

	/// <summary>.tilebrushexploit / .tbe</summary>
	internal static void TileBrushExploitCommand(CommandContext command)
	{
		if (command.Parts.Length > 5)
		{
			if (short.TryParse(command.Parts[5], out TileExploitBrushFY))
			{
				TileExploitBrush = true;
			}
			else
			{
				ClientChat.Error("Invalid Short ARRAY[5] ");
			}
		}
		if (command.Parts.Length > 4)
		{
			if (short.TryParse(command.Parts[4], out TileExploitBrushFX))
			{
				TileExploitBrush = true;
			}
			else
			{
				ClientChat.Error("Invalid Short ARRAY[4] ");
			}
		}
		if (command.Parts.Length > 3)
		{
			if (bool.TryParse(command.Parts[3], out TileExploitBrushActive))
			{
				TileExploitBrush = true;
			}
			else
			{
				ClientChat.Error("Invalid Boolean ARRAY[3] ");
			}
		}
		if (command.Parts.Length > 2)
		{
			if (ushort.TryParse(command.Parts[2], out TileExploitBrushType))
			{
				TileExploitBrush = true;
			}
			else
			{
				ClientChat.Error("Invalid UShort ARRAY[2] ");
			}
		}
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out TileExploitBrushSize))
			{
				TileExploitBrush = true;
				ClientChat.Info(string.Format("Tile Exploit Brush " + (TileExploitBrush ? "En" : "Dis") + "abled  Size: {0}  Type: {1}  Active: {2}  FX: {3}  FY: {4}", TileExploitBrushSize, TileExploitBrushType, TileExploitBrushActive, TileExploitBrushFX, TileExploitBrushFY));
			}
			else
			{
				ClientChat.Error("Invalid Int ARRAY[1]  ");
			}
		}
		else
		{
			TileExploitBrush = !TileExploitBrush;
			if (TileExploitBrush)
			{
				ClientChat.Info(string.Format("Tile Exploit Brush " + (TileExploitBrush ? "En" : "Dis") + "abled  Size: {0}  Type: {1}  Active: {2}  FX: {3}  FY: {4}", TileExploitBrushSize, TileExploitBrushType, TileExploitBrushActive, TileExploitBrushFX, TileExploitBrushFY));
			}
			else
			{
				ClientChat.Info("Tile Exploit Brush " + (TileExploitBrush ? "En" : "Dis") + "abled");
			}
		}
	}

	/// <summary>.tilebrush / .tb</summary>
	internal static void TileBrushCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out TileBrushSize))
			{
				TileBrush = true;
				ClientChat.Info("Tile Brush " + (TileBrush ? "En" : "Dis") + "abled  Size: " + TileBrushSize);
			}
			else
			{
				ClientChat.Error("Invalid Value ");
			}
		}
		else
		{
			TileBrush = !TileBrush;
			ClientChat.Info("Tile Brush " + (TileBrush ? "En" : "Dis") + "abled" + (TileBrush ? ("  Size: " + TileBrushSize) : ""));
		}
	}

	/// <summary>.paintbrush / .pb</summary>
	internal static void PaintBrushCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out TilePaintBrushSize))
			{
				TilePaintBrush = true;
				ClientChat.Info("Super Paint Brush " + (TilePaintBrush ? "En" : "Dis") + "abled  Size: " + TilePaintBrushSize);
			}
			else
			{
				ClientChat.Error("Invalid Value ");
			}
		}
		else
		{
			TilePaintBrush = !TilePaintBrush;
			ClientChat.Info("Super Paint Brush " + (TilePaintBrush ? "En" : "Dis") + "abled " + (TilePaintBrush ? ("  Size: " + TilePaintBrushSize) : ""));
		}
	}

	/// <summary>.erasebrush / .eb</summary>
	internal static void EraseBrushCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out EraseBrushSize))
			{
				EraseBrush = true;
				ClientChat.Info("Erase Brush " + (EraseBrush ? "En" : "Dis") + "abled  Size: " + EraseBrushSize);
			}
			else
			{
				ClientChat.Error("Invalid Value ");
			}
		}
		else
		{
			EraseBrush = !EraseBrush;
			ClientChat.Info("Erase Brush " + (EraseBrush ? "En" : "Dis") + "abled" + (EraseBrush ? ("  Size: " + EraseBrushSize) : ""));
		}
	}

	/// <summary>.liquidbrush / .lb</summary>
	internal static void LiquidBrushCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out LiquidBrushSize))
			{
				LiquidBrush = true;
				ClientChat.Info("Liquid & Dry Brush " + (LiquidBrush ? "En" : "Dis") + "abled  Size: " + LiquidBrushSize);
			}
			else
			{
				ClientChat.Error("Invalid Value ");
			}
		}
		else
		{
			LiquidBrush = !LiquidBrush;
			ClientChat.Info("Liquid Brush " + (LiquidBrush ? "En" : "Dis") + "abled" + (LiquidBrush ? ("  Size: " + LiquidBrushSize) : ""));
		}
	}

	/// <summary>.wallbrush / .wb</summary>
	internal static void WallBrushCommand(CommandContext command)
	{
		ChatCommands.ToggleWithSize(command.Parts, ref WallBrush, ref WallBrushSize, "Wall Brush");
	}

	#endregion
}
