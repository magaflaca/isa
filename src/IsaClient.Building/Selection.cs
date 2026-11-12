using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.Utilities;
using IsaClient.Core;
using IsaClient.Menu;
using IsaClient.Movement;
using IsaClient.Networking;

namespace IsaClient.Building;

/// <summary>World edit selection: select, copy, fill, erase, convert and paste areas.</summary>
public static class Selection
{
	[Setting("RememberEnabledCheats")]
	public static bool RememberEnabled = true;

	public static List<TileCopy> CopiedTiles = new List<TileCopy>();

	public static Point CopyStartA;

	public static Point CopyStartB;

	public static bool CopyMode;

	public static bool PasteMode;

	public static Point SelectionA;

	public static Point SelectionB;

	public static bool SelectionMode;

	[Setting("ShowExtraPasteSelectionInfo", Remembered = true)]
	public static bool SelectionModeInfo;

	private static bool _edgeA;

	private static bool _edgeB;

	public static Point PasteImagePreviewPos;

	public static bool MotionlessPaste;

	public static Point PasteImagePreviewPos2;

	[Setting("SelectionUseTileSyncing", Remembered = true)]
	public static bool SelectionUseTileSync;

	[Setting("PasteInTshock", Remembered = true)]
	public static bool PasteInTShock = true;

	public static bool CancelPaste;

	public static bool CancelAction;

	public static int SchematicSizeX;

	public static int SchematicSizeY;

	public static int ConcurrentPastes;

	public static int ConcurrentFillOrDels = 0;

	public static int ConcurrentConversions = 0;

	public static bool CopyingFromFillOrDelMode;

	public static bool SavingTEDITSchematic;

	public static bool OpeningTEDITSchematic;

	public static int FixUpperTime;

	[Setting("AutoExitPaste", Remembered = true)]
	public static bool AutoExitPaste = false;

	[Setting("PasteDelay", Min = 0, Max = 120)]
	public static int PasteDelay = 45;

	public static int PasteDelayCounter = 0;

	public static bool ConvertingToSchematic = false;

	public static bool SelectionTileSyncingMode
	{
		get
		{
			if (!SelectionUseTileSync)
			{
				return Main.netMode == 2;
			}
			return true;
		}
		set
		{
			SelectionUseTileSync = value;
		}
	}

	public static bool PasteCancelEnabled => ConcurrentPastes > 0;

	public static bool FillOrDelCancelEnabled
	{
		get
		{
			if (ConcurrentFillOrDels <= 0)
			{
				return ConcurrentConversions > 0;
			}
			return true;
		}
	}

	public static bool SaveSchematicEnabled
	{
		get
		{
			if (CopiedTiles.Count <= 0)
			{
				if (SelectionMode && SelectionA != Point.Zero)
				{
					return SelectionB != Point.Zero;
				}
				return false;
			}
			return true;
		}
	}

	public static void FillOrEraseSelectionPoints()
	{
		if (!SelectionMode || Main.gameMenu)
		{
			return;
		}
		if (!Main.mapFullscreen && !Main.player[Main.myPlayer].mouseInterface)
		{
			if (Main.mouseLeft)
			{
				if (SelectionB == Point.Zero)
				{
					SelectionB = Main.MouseWorld.ToTileCoordinates();
				}
				SelectionA = Main.MouseWorld.ToTileCoordinates();
				_edgeA = true;
			}
			if (Main.mouseRight)
			{
				if (SelectionA == Point.Zero)
				{
					SelectionA = Main.MouseWorld.ToTileCoordinates();
				}
				SelectionB = Main.MouseWorld.ToTileCoordinates();
				_edgeB = true;
			}
			if ((!_edgeA && !_edgeB) || SelectionA == Point.Zero || SelectionB == Point.Zero)
			{
				return;
			}
		}
		else
		{
			if (!TeleportTools.TryGetMapCoordinatesV2(PlayerInput.UnscaledMouseX, PlayerInput.UnscaledMouseY, 0, out var result) || (!MenuState.UiRework && InputState.TildeTerminal))
			{
				return;
			}
			if (Main.mouseLeft && PlayerInput.Triggers.Current.KeyStatusHandler("Hold for Activator (Map)") && !MenuState.CustomUIMousedOver && !Main.grabbingMap)
			{
				if (SelectionB == Point.Zero)
				{
					SelectionB = result;
				}
				SelectionA = result;
				_edgeA = true;
			}
			if (Main.mouseRight && PlayerInput.Triggers.Current.KeyStatusHandler("Hold for Activator (Map)") && !MenuState.CustomUIMousedOver && !Main.grabbingMap)
			{
				if (SelectionA == Point.Zero)
				{
					SelectionA = result;
				}
				SelectionB = result;
				_edgeB = true;
			}
			if ((!_edgeA && !_edgeB) || SelectionA == Point.Zero || SelectionB == Point.Zero)
			{
				return;
			}
		}
		if (!Main.mouseLeft && Main.mouseLeftRelease)
		{
			_edgeA = false;
		}
		if (!Main.mouseRight && Main.mouseRightRelease)
		{
			_edgeB = false;
		}
	}

	[Old("Deprecated.")]
	public static async void FillTilesSelectionMode()
	{
		await Task.Run(delegate
		{
			int num = Math.Min(SelectionA.X, SelectionB.X);
			int num2 = Math.Min(SelectionA.Y, SelectionB.Y);
			int num3 = Math.Abs(SelectionA.X - SelectionB.X);
			int num4 = Math.Abs(SelectionA.Y - SelectionB.Y);
			int selectedItem = Main.LocalPlayer.selectedItem;
			int createTile = Main.LocalPlayer.inventory[Main.LocalPlayer.selectedItem].createTile;
			int createWall = Main.LocalPlayer.inventory[Main.LocalPlayer.selectedItem].createWall;
			int num5 = 0;
			for (int i = num; i <= num + num3; i++)
			{
				_ = NetMessage.buffer[256].spamCount;
				for (int j = num2; j <= num2 + num4; j++)
				{
					if (WorldGen.InWorld(i, j))
					{
						bool flag = false;
						Tile tile = Framing.GetTileSafely(i, j);
						if (Main.gameMenu)
						{
							ClientChat.Info("You can't do this here! (This should never appear)");
							return;
						}
						if (tile == null)
						{
							tile = default(Tile);
						}
						if (tile.type != createTile && tile.type >= 0 && createWall < 0)
						{
							tile.ClearTile();
						}
						if (!tile.active())
						{
							if (createTile >= 0)
							{
								tile.active(active: true);
								tile.type = (ushort)createTile;
								WorldGen.SquareTileFrame(i, j, resetFrame: true, noBreak: true);
								flag = true;
								if (Main.netMode != 0 && !SelectionTileSyncingMode)
								{
									TeleportTools.FakeNetMessage13(new Vector2((float)((i + 2) * 16), (float)(j * 16)), selectedItem);
									NetMessage.SendData(17, -1, -1, null, 1, i, j, createTile);
								}
							}
							else if (createWall > 0)
							{
								tile.wall = (ushort)createWall;
								WorldGen.SquareWallFrame(i, j);
								flag = true;
								if (Main.netMode != 0 && !SelectionTileSyncingMode)
								{
									TeleportTools.FakeNetMessage13(new Vector2((float)((i + 2) * 16), (float)(j * 16)), selectedItem);
									NetMessage.SendData(17, -1, -1, null, 3, i, j, createWall);
								}
							}
						}
						if (flag)
						{
							while (PacketPacing.QueuedPackets >= PacketPacing.TilePacketRate || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard)
							{
								Thread.Sleep(150);
							}
							Main.Map.UpdateLighting(i, j, byte.MaxValue);
							Main.refreshMap = true;
							num5++;
						}
					}
				}
			}
			if (Main.netMode != 0 && SelectionTileSyncingMode)
			{
				for (int k = num; k <= num + num3; k += 10)
				{
					for (int l = num2; l <= num2 + num4; l += 10)
					{
						NetMessage.SendTileSquare(-1, k, l, 10, 10);
					}
				}
			}
			ClientChat.Info(num5 + " Tiles Filled.");
		});
	}

	[Old("Deprecated.")]
	public static void CopySelection()
	{
		try
		{
			if (!CopyMode || Main.gameMenu || PasteMode || !Main.hasFocus)
			{
				return;
			}
			if (ConcurrentPastes > 0)
			{
				ClientChat.Error("You cannot copy tiles while pasting.");
				CopyMode = false;
			}
			else if (!Main.mapFullscreen && !Main.player[Main.myPlayer].mouseInterface)
			{
				if (Main.mouseLeft)
				{
					if (CopyStartB == Point.Zero)
					{
						CopyStartB = Main.MouseWorld.ToTileCoordinates();
					}
					CopyStartA = Main.MouseWorld.ToTileCoordinates();
					_edgeA = true;
				}
				if (Main.mouseRight)
				{
					if (CopyStartA == Point.Zero)
					{
						CopyStartA = Main.MouseWorld.ToTileCoordinates();
					}
					CopyStartB = Main.MouseWorld.ToTileCoordinates();
					_edgeB = true;
				}
				else
				{
					if ((!_edgeA && !_edgeB) || !(CopyStartA != Point.Zero) || !(CopyStartB != Point.Zero))
					{
						return;
					}
					int num = Math.Min(CopyStartA.X, CopyStartB.X);
					int num2 = Math.Min(CopyStartA.Y, CopyStartB.Y);
					SchematicSizeX = Math.Abs(CopyStartA.X - CopyStartB.X) + 1;
					SchematicSizeY = Math.Abs(CopyStartA.Y - CopyStartB.Y) + 1;
					Placement.TileLoopCount = 0;
					if (CopiedTiles.Capacity > 0 && CopiedTiles.Count > 0)
					{
						CopiedTiles.Clear();
						CopiedTiles.TrimExcess();
					}
					for (int i = num; i < num + SchematicSizeX; i++)
					{
						for (int j = num2; j < num2 + SchematicSizeY; j++)
						{
							Tile tile = Framing.GetTileSafely(i, j);
							if (tile == null)
							{
								ClientChat.Info("You cannot copy tiles that have not been loaded");
								break;
							}
							if (WorldGen.InWorld(i, j))
							{
								TileCopy item = new TileCopy(tile);
								CopiedTiles.Add(item);
								Placement.TileLoopCount++;
							}
						}
					}
					if (Main.mouseLeftRelease)
					{
						if (_edgeA)
						{
							ClientChat.Info(Placement.TileLoopCount + " Tiles copied");
						}
						_edgeA = false;
					}
					if (Main.mouseRightRelease)
					{
						if (_edgeB)
						{
							ClientChat.Info(Placement.TileLoopCount + " Tiles copied");
						}
						_edgeB = false;
					}
				}
			}
			else
			{
				if (!TeleportTools.TryGetMapCoordinatesV2(PlayerInput.UnscaledMouseX, PlayerInput.UnscaledMouseY, 0, out var result) || InputState.TildeTerminal)
				{
					return;
				}
				if (Main.mouseLeft && Main.keyState.IsKeyDown((Keys)160))
				{
					if (CopyStartB == Point.Zero)
					{
						CopyStartB = result;
					}
					CopyStartA = result;
					_edgeA = true;
				}
				if (Main.mouseRight && Main.keyState.IsKeyDown((Keys)160))
				{
					if (CopyStartA == Point.Zero)
					{
						CopyStartA = result;
					}
					CopyStartB = result;
					_edgeB = true;
				}
				else
				{
					if ((!_edgeA && !_edgeB) || !(CopyStartA != Point.Zero) || !(CopyStartB != Point.Zero))
					{
						return;
					}
					int num3 = Math.Min(CopyStartA.X, CopyStartB.X);
					int num4 = Math.Min(CopyStartA.Y, CopyStartB.Y);
					SchematicSizeX = Math.Abs(CopyStartA.X - CopyStartB.X) + 1;
					SchematicSizeY = Math.Abs(CopyStartA.Y - CopyStartB.Y) + 1;
					Placement.TileLoopCount = 0;
					if (CopiedTiles.Capacity > 0 && CopiedTiles.Count > 0)
					{
						CopiedTiles.Clear();
						CopiedTiles.TrimExcess();
					}
					for (int k = num3; k < num3 + SchematicSizeX; k++)
					{
						for (int l = num4; l < num4 + SchematicSizeY; l++)
						{
							Tile tile2 = Framing.GetTileSafely(k, l);
							if (tile2 == null)
							{
								ClientChat.Info("You cannot copy tiles that have not been loaded.");
								break;
							}
							if (WorldGen.InWorld(k, l))
							{
								TileCopy item2 = new TileCopy(tile2);
								CopiedTiles.Add(item2);
								Placement.TileLoopCount++;
							}
						}
					}
					if (Main.mouseLeftRelease && _edgeA)
					{
						_edgeA = false;
						ClientChat.Info(Placement.TileLoopCount + " Tiles copied");
					}
					else if (Main.mouseRightRelease && _edgeB)
					{
						_edgeB = false;
					}
				}
			}
		}
		catch (Exception ex)
		{
			CopyMode = false;
			ClientChat.Error($"Copy Tles Error!\n{ex}");
			ClientLog.LogException(ex);
		}
	}

	public static void LoadTilesFromFile()
	{
		ClientChat.Error("This operation is deprecated and may not return in the future.");
	}

	public static void SaveTilesToFile()
	{
		ClientChat.Error("This operation is deprecated and will not return.");
	}

	/// <summary>The selection as a rectangle of tiles (both corners in).</summary>
	internal static Rectangle SelectionRect()
	{
		return new Rectangle(Math.Min(SelectionA.X, SelectionB.X), Math.Min(SelectionA.Y, SelectionB.Y), Math.Abs(SelectionA.X - SelectionB.X) + 1, Math.Abs(SelectionA.Y - SelectionB.Y) + 1);
	}

	public static async void EraseTileSelectionMode(int mode, PasteJob job = null)
	{
		// Multi Paste on: your bots do it with you, each a strip of the selection.
		if (job == null && IsaClient.MultiClient.MultiPaste.TryStartRegion("Erase", SelectionRect(), part => EraseTileSelectionMode(mode, part)))
		{
			return;
		}
		await Task.Run(delegate
		{
			PasteJob.Enter(job);
			try
			{
			if (job == null && ConcurrentFillOrDels >= 6)
			{
				PasteJob.Error("Cannot have more than 6 actions running at once!");
			}
			else
			{
				ConcurrentFillOrDels++;
				int num = (job != null) ? job.Origin.X : Math.Min(SelectionA.X, SelectionB.X);
				int num2 = (job != null) ? job.Origin.Y : Math.Min(SelectionA.Y, SelectionB.Y);
				int num3 = (job != null) ? (job.SizeX - 1) : Math.Abs(SelectionA.X - SelectionB.X);
				int num4 = (job != null) ? (job.SizeY - 1) : Math.Abs(SelectionA.Y - SelectionB.Y);
				int num5 = 0;
				int num6 = 0;
				int num7 = 0;
				if (PasteJob.CancelActionNow)
				{
					CancelAction = false;
				}
				try
				{
					for (int i = num; i <= num + num3; i++)
					{
						_ = NetMessage.buffer[256].spamCount;
						for (int j = num2; j <= num2 + num4; j++)
						{
							if (PasteJob.CancelActionNow)
							{
								NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
								PasteJob.Info("Caneled tile erasing.");
								CancelAction = false;
								ConcurrentFillOrDels--;
								return;
							}
							if (WorldGen.InWorld(i, j))
							{
								bool flag = false;
								Tile tile = Framing.GetTileSafely(i, j);
								if (Main.gameMenu)
								{
									PasteJob.Error("You can't do this here! (This should never appear)");
									ConcurrentFillOrDels--;
									return;
								}
								if (tile == null)
								{
									tile = default(Tile);
								}
								if (tile.active() && (mode == 0 || mode == 1))
								{
									flag = true;
									tile.ClearTile();
									WorldGen.SquareTileFrame(i, j);
									if (Main.netMode == 1 && !SelectionTileSyncingMode)
									{
										NetMessage.SendData(17, -1, -1, null, 4, i, j);
										num5++;
										num7++;
									}
								}
								if (tile.wall > 0 && (mode == 0 || mode == 2))
								{
									bool protectedWall = Placement.IsProtectedWall(tile.wall);
									if (!protectedWall || Placement.ForceProtectedWalls || Main.netMode != 1)
									{
										flag = true;
										tile.wall = 0;
										WorldGen.SquareWallFrame(i, j);
										if (Main.netMode == 1 && !SelectionTileSyncingMode)
										{
											if (protectedWall)
											{
												// The server refuses to break it (Skeletron / Golem alive): send the cleared tile itself.
												NetMessage.SendTileSquare(-1, i, j, 1, 1);
											}
											else
											{
												NetMessage.SendData(17, -1, -1, null, 2, i, j);
											}
											num5++;
											num7++;
										}
									}
								}
								if (tile.liquid > 0 && mode == 3)
								{
									flag = true;
									tile.Clear(TileDataType.Liquid);
									WorldGen.SquareTileFrame(i, j, resetFrame: false);
									if (Main.netMode == 1 && !SelectionTileSyncingMode)
									{
										RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 5304f);
										TeleportTools.FakeNetMessage13(new Vector2((float)((i + 2) * 16), (float)(j * 16)), 58);
										NetMessage.sendWater(i, j);
										NetMessage.SendData(5, -1, -1, null, Main.myPlayer, 58f);
										num6++;
										num7++;
									}
								}
								if (flag)
								{
									if (!SelectionTileSyncingMode)
									{
										switch (mode)
										{
										case 0:
										case 1:
										case 2:
											if (!PasteJob.MaxModeNow)
											{
												if ((Main.netMode == 1 && num5 % PasteJob.EraseRateNow == 0) || Performance.LowFpsGuard)
												{
													Thread.Sleep(100);
												}
											}
											else
											{
												while ((Main.netMode == 1 && (num5 > PasteJob.EraseRateNow || PacketPacing.ServerSilentGuard)) || Performance.LowFpsGuard)
												{
													num5 = 0;
													Thread.Sleep(500);
												}
											}
											break;
										case 3:
											if (!PasteJob.MaxModeNow)
											{
												if ((Main.netMode == 1 && num6 % PasteJob.LiquidRateNow == 0) || Performance.LowFpsGuard)
												{
													Thread.Sleep(100);
												}
											}
											else
											{
												while ((Main.netMode == 1 && (num6 > PasteJob.LiquidRateNow || PacketPacing.ServerSilentGuard)) || Performance.LowFpsGuard)
												{
													num6 = 0;
													Thread.Sleep(500);
												}
											}
											break;
										}
									}
									Main.Map.UpdateLighting(i, j, byte.MaxValue);
									Main.refreshMap = true;
								}
							}
						}
					}
					if (Main.netMode == 1 && SelectionTileSyncingMode)
					{
						for (int k = num; k <= num + num3; k += 10)
						{
							for (int l = num2; l <= num2 + num4; l += 10)
							{
								NetMessage.SendTileSquare(-1, k, l, 10, 10);
								while (num5 > PasteJob.EraseRateNow || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard)
								{
									num5 = 0;
									Thread.Sleep(500);
								}
								num5++;
								num7++;
							}
						}
					}
				}
				catch (Exception ex)
				{
					PasteJob.Error($"Erase selection error!\n{ex}");
					ClientLog.LogException(ex);
				}
				PasteJob.Info(num7 + " Tiles Removed.");
				NetMessage.SendData(13);
				ConcurrentFillOrDels--;
			}
			}
			finally
			{
				PasteJob.Leave();
			}
		});
	}

	public async static void FillTilesSelectionModeV2()
	{
		await Task.Run(delegate
		{
			int num = Math.Min(SelectionA.X, SelectionB.X);
			int num2 = Math.Min(SelectionA.Y, SelectionB.Y);
			int num3 = Math.Abs(SelectionA.X - SelectionB.X);
			int num4 = Math.Abs(SelectionA.Y - SelectionB.Y);
			int selectedItem = Main.LocalPlayer.selectedItem;
			int createTile = Main.LocalPlayer.inventory[Main.LocalPlayer.selectedItem].createTile;
			int createWall = Main.LocalPlayer.inventory[Main.LocalPlayer.selectedItem].createWall;
			int num5 = 0;
			if (CancelAction)
			{
				CancelAction = false;
			}
			for (int i = num; i <= num + num3; i++)
			{
				for (int j = num2; j <= num2 + num4; j++)
				{
					if (CancelAction)
					{
						NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
						ClientChat.Info("Caneled filling tiles.");
						CancelAction = false;
						return;
					}
					if (WorldGen.InWorld(i, j))
					{
						bool flag = false;
						Tile tile = Framing.GetTileSafely(i, j);
						if (Main.gameMenu)
						{
							ClientChat.Error("You can't do this here! (This should never appear)");
							return;
						}
						if (tile == null)
						{
							tile = default(Tile);
						}
						if (createTile >= 0)
						{
							if (tile.type != createTile && tile.type >= 0 && createWall < 0)
							{
								tile.ClearTile();
							}
							if (!tile.active())
							{
								tile.active(active: true);
								tile.type = (ushort)createTile;
								WorldGen.SquareTileFrame(i, j, resetFrame: true, noBreak: true);
								flag = true;
								if (Main.netMode != 0 && !SelectionTileSyncingMode)
								{
									TeleportTools.FakeNetMessage13(new Vector2((float)((i + 2) * 16), (float)(j * 16)), selectedItem);
									NetMessage.SendData(17, -1, -1, null, 1, i, j, createTile);
								}
							}
						}
						else if (createWall > 0)
						{
							if (tile.wall != Main.LocalPlayer.HeldItem.createWall && tile.wall > 0)
							{
								tile.Clear(TileDataType.Wall);
							}
							if (tile.wall == 0)
							{
								tile.wall = (ushort)createWall;
								WorldGen.SquareWallFrame(i, j);
								flag = true;
								if (Main.netMode != 0 && !SelectionTileSyncingMode)
								{
									TeleportTools.FakeNetMessage13(new Vector2((float)((i + 2) * 16), (float)(j * 16)), selectedItem);
									NetMessage.SendData(17, -1, -1, null, 3, i, j, createWall);
								}
							}
						}
						if (flag)
						{
							if (!SelectionTileSyncingMode)
							{
								if (!PacketPacing.TilePacketMaxMode)
								{
									if ((Main.netMode != 0 && num5 % PacketPacing.TilePacketRate == 0) || Performance.LowFpsGuard)
									{
										Thread.Sleep(100);
									}
								}
								else
								{
									while ((Main.netMode != 0 && (PacketPacing.QueuedPackets > PacketPacing.TilePacketRate || PacketPacing.ServerSilentGuard)) || Performance.LowFpsGuard)
									{
										Thread.Sleep(500);
									}
								}
							}
							Main.Map.UpdateLighting(i, j, byte.MaxValue);
							Main.refreshMap = true;
							num5++;
						}
					}
				}
			}
			if (Main.netMode != 0 && SelectionTileSyncingMode)
			{
				for (int k = num; k <= num + num3; k += 10)
				{
					for (int l = num2; l <= num2 + num4; l += 10)
					{
						NetMessage.SendTileSquare(-1, k, l, 10, 10);
						while (PacketPacing.QueuedPackets > PacketPacing.TilePacketRate || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard)
						{
							Thread.Sleep(500);
						}
					}
				}
			}
			ClientChat.Info(num5 + " Tiles Filled.");
			NetMessage.SendData(13);
		});
	}

	public static async void FillTilesSelectionModeV3()
	{
		await Task.Run(delegate
		{
			if (ConcurrentFillOrDels >= 6)
			{
				ClientChat.Error("Cannot have more than 6 actions running at once!");
			}
			else
			{
				ConcurrentFillOrDels++;
				int num = Math.Min(SelectionA.X, SelectionB.X);
				int num2 = Math.Min(SelectionA.Y, SelectionB.Y);
				int num3 = Math.Abs(SelectionA.X - SelectionB.X);
				int num4 = Math.Abs(SelectionA.Y - SelectionB.Y);
				int selectedItem = Main.LocalPlayer.selectedItem;
				int createTile = Main.LocalPlayer.inventory[Main.LocalPlayer.selectedItem].createTile;
				int createWall = Main.LocalPlayer.inventory[Main.LocalPlayer.selectedItem].createWall;
				int num5 = 0;
				if (CancelAction)
				{
					CancelAction = false;
				}
				try
				{
					for (int i = num; i <= num + num3; i++)
					{
						for (int j = num2; j <= num2 + num4; j++)
						{
							if (CancelAction)
							{
								NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
								ClientChat.Info("Caneled filling tiles.");
								CancelAction = false;
								ConcurrentFillOrDels--;
								return;
							}
							if (WorldGen.InWorld(i, j))
							{
								bool flag = false;
								Tile tile = Framing.GetTileSafely(i, j);
								if (Main.gameMenu)
								{
									ClientChat.Error("You can't do this here! (This should never appear)");
									ConcurrentFillOrDels--;
									return;
								}
								if (tile == null)
								{
									tile = default(Tile);
								}
								if (createTile >= 0)
								{
									if (tile.type != createTile && tile.type >= 0 && createWall < 0)
									{
										tile.ClearTile();
									}
									if (!tile.active())
									{
										tile.active(active: true);
										tile.type = (ushort)createTile;
										WorldGen.SquareTileFrame(i, j, resetFrame: true, noBreak: true);
										flag = true;
										if (Main.netMode != 0 && !SelectionTileSyncingMode)
										{
											TeleportTools.FakeNetMessage13(new Vector2((float)((i + 2) * 16), (float)(j * 16)), 58);
											NetMessage.SendData(17, -1, -1, null, 1, i, j, createTile);
										}
									}
								}
								else if (createWall > 0)
								{
									if (tile.wall != Main.LocalPlayer.HeldItem.createWall && tile.wall > 0)
									{
										tile.Clear(TileDataType.Wall);
									}
									if (tile.wall == 0)
									{
										tile.wall = (ushort)createWall;
										WorldGen.SquareWallFrame(i, j);
										flag = true;
										if (Main.netMode != 0 && !SelectionTileSyncingMode)
										{
											TeleportTools.FakeNetMessage13(new Vector2((float)((i + 2) * 16), (float)(j * 16)), selectedItem);
											NetMessage.SendData(17, -1, -1, null, 3, i, j, createWall);
										}
									}
								}
								if (flag)
								{
									if (!SelectionTileSyncingMode)
									{
										if (!PacketPacing.TilePacketMaxMode)
										{
											if ((Main.netMode != 0 && num5 % PacketPacing.TilePacketRate == 0) || Performance.LowFpsGuard)
											{
												Thread.Sleep(100);
											}
										}
										else
										{
											while ((Main.netMode != 0 && (PacketPacing.QueuedPackets > PacketPacing.TilePacketRate || PacketPacing.ServerSilentGuard)) || Performance.LowFpsGuard)
											{
												Thread.Sleep(500);
											}
										}
									}
									Main.Map.UpdateLighting(i, j, byte.MaxValue);
									Main.refreshMap = true;
									num5++;
								}
							}
						}
					}
					if (Main.netMode != 0 && SelectionTileSyncingMode)
					{
						for (int k = num; k <= num + num3; k += 10)
						{
							for (int l = num2; l <= num2 + num4; l += 10)
							{
								NetMessage.SendTileSquare(-1, k, l, 10, 10);
								while (PacketPacing.QueuedPackets > PacketPacing.TilePacketRate || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard)
								{
									Thread.Sleep(500);
								}
							}
						}
					}
				}
				catch (Exception ex)
				{
					ClientChat.Info($"Fill tiles in selection error!\n{ex}");
					ClientLog.LogException(ex);
				}
				ClientChat.Info(num5 + " Tiles Filled.");
				NetMessage.SendData(13);
				ConcurrentFillOrDels--;
			}
		});
	}

	public static async void FillTilesSelectionModeV4(Item item, PasteJob job = null)
	{
		// Multi Paste on: your bots do it with you, each a strip of the selection.
		if (job == null && IsaClient.MultiClient.MultiPaste.TryStartRegion("Fill", SelectionRect(), part => FillTilesSelectionModeV4(item, part)))
		{
			return;
		}
		await Task.Run(delegate
		{
			PasteJob.Enter(job);
			try
			{
			if (job == null && ConcurrentFillOrDels >= 6)
			{
				PasteJob.Error("Cannot have more than 6 actions running at once!");
			}
			else
			{
				ConcurrentFillOrDels++;
				int num = (job != null) ? job.Origin.X : Math.Min(SelectionA.X, SelectionB.X);
				int num2 = (job != null) ? job.Origin.Y : Math.Min(SelectionA.Y, SelectionB.Y);
				int num3 = (job != null) ? (job.SizeX - 1) : Math.Abs(SelectionA.X - SelectionB.X);
				int num4 = (job != null) ? (job.SizeY - 1) : Math.Abs(SelectionA.Y - SelectionB.Y);
				int createTile = item.createTile;
				int createWall = item.createWall;
				int num5 = 0;
				if (PasteJob.CancelActionNow)
				{
					CancelAction = false;
				}
				try
				{
					for (int i = num; i <= num + num3; i++)
					{
						for (int j = num2; j <= num2 + num4; j++)
						{
							if (PasteJob.CancelActionNow)
							{
								NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
								PasteJob.Info("Caneled filling tiles.");
								CancelAction = false;
								ConcurrentFillOrDels--;
								return;
							}
							if (WorldGen.InWorld(i, j))
							{
								bool flag = false;
								Tile tile = Framing.GetTileSafely(i, j);
								if (Main.gameMenu)
								{
									PasteJob.Error("You can't do this here! (This should never appear)");
									ConcurrentFillOrDels--;
									return;
								}
								if (tile == null)
								{
									tile = default(Tile);
								}
								if (createTile >= 0)
								{
									if (tile.type != createTile && tile.type >= 0 && createWall < 0)
									{
										tile.ClearTile();
									}
									if (!tile.active())
									{
										tile.active(active: true);
										tile.type = (ushort)createTile;
										WorldGen.SquareTileFrame(i, j, resetFrame: true, noBreak: true);
										flag = true;
										if (Main.netMode != 0 && !SelectionTileSyncingMode)
										{
											RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, item.type);
											TeleportTools.FakeNetMessage13(new Vector2((float)((i + 2) * 16), (float)(j * 16)), 58);
											NetMessage.SendData(17, -1, -1, null, 1, i, j, createTile);
											NetMessage.SendData(5, -1, -1, null, Main.myPlayer, 58f);
										}
									}
								}
								else if (createWall > 0)
								{
									if (tile.wall != item.createWall && tile.wall > 0)
									{
										tile.Clear(TileDataType.Wall);
									}
									if (tile.wall == 0)
									{
										tile.wall = (ushort)createWall;
										WorldGen.SquareWallFrame(i, j);
										flag = true;
										if (Main.netMode != 0 && !SelectionTileSyncingMode)
										{
											RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, item.type);
											TeleportTools.FakeNetMessage13(new Vector2((float)((i + 2) * 16), (float)(j * 16)), 58);
											NetMessage.SendData(17, -1, -1, null, 3, i, j, createWall);
											NetMessage.SendData(5, -1, -1, null, Main.myPlayer, 58f);
										}
									}
								}
								if (flag)
								{
									if (!SelectionTileSyncingMode)
									{
										if (!PasteJob.MaxModeNow)
										{
											if ((Main.netMode != 0 && num5 % PasteJob.TileRateNow == 0) || Performance.LowFpsGuard)
											{
												Thread.Sleep(100);
											}
										}
										else
										{
											while ((Main.netMode != 0 && (num5 > PasteJob.TileRateNow || PacketPacing.ServerSilentGuard)) || Performance.LowFpsGuard)
											{
												num5 = 0;
												Thread.Sleep(500);
											}
										}
									}
									Main.Map.UpdateLighting(i, j, byte.MaxValue);
									Main.refreshMap = true;
									num5++;
								}
							}
						}
					}
					if (Main.netMode != 0 && SelectionTileSyncingMode)
					{
						for (int k = num; k <= num + num3; k += 10)
						{
							for (int l = num2; l <= num2 + num4; l += 10)
							{
								NetMessage.SendTileSquare(-1, k, l, 10, 10);
								while (num5 > PasteJob.TileRateNow || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard)
								{
									num5 = 0;
									Thread.Sleep(500);
								}
								num5++;
							}
						}
					}
				}
				catch (Exception ex)
				{
					PasteJob.Info($"Fill tiles in selection error!\n{ex}");
					ClientLog.LogException(ex);
				}
				PasteJob.Info(num5 + " Tiles Filled.");
				NetMessage.SendData(13);
				ConcurrentFillOrDels--;
			}
			}
			finally
			{
				PasteJob.Leave();
			}
		});
	}

	public static async void CopyTilesFromFillOrDelMode()
	{
		try
		{
			if (CopyingFromFillOrDelMode)
			{
				ClientChat.Error("Already coying tiles, please wait for the operation to finish.");
				return;
			}
			CopyingFromFillOrDelMode = true;
			await Task.Run(delegate
			{
				int num = Math.Min(SelectionA.X, SelectionB.X);
				int num2 = Math.Min(SelectionA.Y, SelectionB.Y);
				SchematicSizeX = Math.Abs(SelectionA.X - SelectionB.X) + 1;
				SchematicSizeY = Math.Abs(SelectionA.Y - SelectionB.Y) + 1;
				CopiedTiles.Clear();
				for (int i = num; i < num + SchematicSizeX; i++)
				{
					for (int j = num2; j < num2 + SchematicSizeY; j++)
					{
						if (WorldGen.InWorld(i, j))
						{
							TileCopy item = new TileCopy(Framing.GetTileSafely(i, j));
							CopiedTiles.Add(item);
						}
					}
				}
				ClientChat.Info("Copied " + CopiedTiles.Count + " Tiles.");
				CopyingFromFillOrDelMode = false;
			});
		}
		catch (Exception ex)
		{
			ClientChat.Error($"Copy tiles error!\n{ex}");
			CopyingFromFillOrDelMode = false;
			ClientLog.LogException(ex);
		}
	}

	public static string SelectionModeString()
	{
		if (!SelectionModeInfo)
		{
			return "Selection Mode";
		}
		int num = Math.Min(SelectionA.X, SelectionB.X);
		int num2 = Math.Min(SelectionA.Y, SelectionB.Y);
		int num3 = Math.Max(SelectionA.X, SelectionB.X);
		int num4 = Math.Max(SelectionA.Y, SelectionB.Y);
		return $"Selection Mode\nTop Left: {num}, {num2}\nBottom Right: {num3}, {num4}\nWidth: {num3 - num + 1}\nHeight: {num4 - num2 + 1}";
	}

	public static string PasteModeString()
	{
		if (!SelectionModeInfo)
		{
			return "";
		}
		string item = "Paste Mode";
		List<string> list = new List<string>();
		list.Add(item);
		Point result = Main.MouseWorld.ToTileCoordinates();
		if (Main.mapFullscreen)
		{
			TeleportTools.TryGetMapCoordinatesV2(PlayerInput.UnscaledMouseX, PlayerInput.UnscaledMouseY, 0, out result);
		}
		if (!Main.mapFullscreen && !Main.LocalPlayer.mouseInterface && Main.mouseRight)
		{
			list.Add("Motionless Mode Active");
		}
		if (WorldGen.InWorld(result.X, result.Y))
		{
			list.Add($"Paste Origin: {result.X}, {result.Y}");
		}
		else
		{
			list.Add("Paste Origin Unavailable");
		}
		list.Add($"Width: {SchematicSizeX}");
		list.Add($"Height: {SchematicSizeY}");
		return item = string.Join("\n", list);
	}

	#region Chat commands

	/// <summary>.fix / .fixup</summary>
	internal static void FixUpCommand(CommandContext command)
	{
		ClientChat.Info($"Current pastes: {ConcurrentPastes}, Current Fill/Delete: {ConcurrentFillOrDels}, Current Conversion: {ConcurrentConversions}");
		FixUpperTime = 300;
	}

	/// <summary>.copy</summary>
	internal static void CopyCommand(CommandContext command)
	{
		CopyMode = !CopyMode;
		ClientChat.Info("Copy Selection Mode " + (CopyMode ? "En" : "Dis") + "abled");
		if (PasteMode || BrushState.BrushMode)
		{
			PasteMode = false;
			BrushState.BrushMode = false;
		}
	}

	/// <summary>.paste</summary>
	internal static void PasteCommand(CommandContext command)
	{
		PasteMode = !PasteMode;
		ClientChat.Info("Paste Mode " + (PasteMode ? "En" : "Dis") + "abled");
		if (CopyMode || BrushState.BrushMode)
		{
			CopyMode = false;
			BrushState.BrushMode = false;
		}
	}

	/// <summary>.checksize</summary>
	internal static void CheckSizeCommand(CommandContext command)
	{
		ClientChat.Info("TilesToBrush - Capacity/Size: " + BrushOperations.TilesToBrush.Count);
		ClientChat.Info("CopiedTiles - Size: " + CopiedTiles.Count);
	}

	/// <summary>.selinfo / .pasteinfo</summary>
	internal static void PasteInfoCommand(CommandContext command)
	{
		SelectionModeInfo = !SelectionModeInfo;
		ClientChat.Info("Extra paste/selection information " + (SelectionModeInfo ? "en" : "dis") + "abled.");
	}

	/// <summary>.reset / .disableall</summary>
	internal static void DisableAllCommand(CommandContext command)
	{
		SelectionMode = false;
		CopyMode = false;
		PasteMode = false;
		BrushState.BrushMode = false;
		PixelArt.DrawPhotoMode = false;
		CancelAction = true;
		CancelPaste = true;
		BrushState.DisableBrushes();
		BrushState.WallBrush = false;
		SelectionA = Point.Zero;
		SelectionB = Point.Zero;
		CopyStartA = Point.Zero;
		CopyStartB = Point.Zero;
		ClientChat.Info("All world edit modes disabled.");
	}

	#endregion
}
