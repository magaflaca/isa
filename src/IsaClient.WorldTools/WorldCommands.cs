using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.WorldBuilding;
using IsaClient.Building;
using IsaClient.Core;
using IsaClient.Movement;
using IsaClient.Networking;

namespace IsaClient.WorldTools;

/// <summary>World tools: regenerate, scan/remove tiles, convert, ground items, actuation and plates.</summary>
public static class WorldCommands
{
	public static bool BlockWirePayment;

	/// <summary>.regen [seed] (single player): regenerates the open world in place.</summary>
	internal static void RegenerateTheWorld(string seed)
	{
		Task.Run(delegate
		{
			try
			{
				string seedText = seed ?? Main.ActiveWorldFileData.SeedText;
				WorldGenerationOptions.SelectOption(WorldGenerationOptions.GetOptionFromSeedText(seedText ?? ""));
				if (seed != null)
				{
					Main.ActiveWorldFileData.SetSeed(seed);
				}
				WorldGen.GenerateWorld();
				ClientChat.Info("World Regen Complete.");
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
				ClientChat.Error("World regen failed: " + ex.Message);
			}
		});
	}

	public static bool PurifyingWorld;

	public static async void ScanWorldForSpecificTile(int tileId, bool teleport = false)
	{
		await Task.Run(delegate
		{
			bool flag = false;
			bool flag2 = false;
			int num = 0;
			for (int i = 0; i < Main.maxTilesX; i++)
			{
				for (int j = 0; j < Main.maxTilesY; j++)
				{
					Tile tile = Framing.GetTileSafely(i, j);
					if (tile != null && tile.active() && tile.type == tileId)
					{
						flag = true;
						num++;
						if (num < 50 && !flag2)
						{
							ClientChat.Info($"Tile Located at - [ X:{i} Y:{j} ] with framing [ FrameX: {tile.frameX} ] | [ FrameY: {tile.frameY} ]");
						}
						if (teleport && !flag2)
						{
							flag2 = true;
							Main.LocalPlayer.position = new Vector2((float)i * 16f - (float)(Main.LocalPlayer.width / 2) + 8f, (float)j * 16f - (float)Main.LocalPlayer.height);
							NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
						}
					}
				}
			}
			if (flag && num > 50 && !flag2)
			{
				ClientChat.Info("+ " + (num - 50) + " more... (Total " + num + ")");
			}
			else if (flag && num > 1 && flag2)
			{
				ClientChat.Info("+ " + (num - 1) + " more... (Total " + num + ")");
			}
			if (!flag)
			{
				ClientChat.Info("Tile ID " + tileId + " not found in world and/or loaded sections.");
			}
		});
	}

	public static async void RemoveTilesOfType(int tileId)
	{
		await Task.Run(delegate
		{
			bool flag = false;
			int num = 0;
			int num2 = 0;
			if (Selection.ConcurrentFillOrDels >= 6)
			{
				ClientChat.Error("Cannot have more than 6 actions running at once!");
			}
			else
			{
				Selection.ConcurrentFillOrDels++;
				if (Selection.CancelAction)
				{
					Selection.CancelAction = false;
				}
				int num3 = ((Selection.SelectionA.X != 0 && Selection.SelectionMode) ? Math.Min(Selection.SelectionA.X, Selection.SelectionB.X) : 0);
				int num4 = ((Selection.SelectionA.Y != 0 && Selection.SelectionMode) ? Math.Min(Selection.SelectionA.Y, Selection.SelectionB.Y) : 0);
				int num5 = ((Selection.SelectionB.X != 0 && Selection.SelectionMode) ? Math.Abs(Selection.SelectionA.X - Selection.SelectionB.X) : Main.maxTilesX);
				int num6 = ((Selection.SelectionB.X != 0 && Selection.SelectionMode) ? Math.Abs(Selection.SelectionA.Y - Selection.SelectionB.Y) : Main.maxTilesY);
				try
				{
					for (int i = num3; i <= num3 + num5; i++)
					{
						for (int j = num4; j <= num4 + num6; j++)
						{
							if (Main.gameMenu)
							{
								Selection.CancelAction = false;
								Selection.ConcurrentFillOrDels--;
								return;
							}
							if (Selection.CancelAction)
							{
								NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
								ClientChat.Error($"Caneled removing tiles of type {tileId}.");
								Selection.CancelAction = false;
								Selection.ConcurrentFillOrDels--;
								return;
							}
							Tile tile = Framing.GetTileSafely(i, j);
							if (tile != null && tile.active() && tile.type == tileId)
							{
								flag = true;
								num++;
								if (Main.netMode != 0)
								{
									num2++;
									if (tile.type != 504)
									{
										tile.type = 504;
										TeleportTools.FakeNetMessage13(new Vector2((float)i * 16f, (float)j * 16f), Main.LocalPlayer.selectedItem);
										NetMessage.SendData(17, -1, -1, null, 1, i, j, 504f);
										tile.active(active: false);
										tile.halfBrick(halfBrick: false);
										tile.frameX = -1;
										tile.frameY = -1;
										tile.ClearBlockPaintAndCoating();
										tile.frameNumber(0);
										if (tile.type == 419)
										{
											Wiring.PokeLogicGate(i, j + 1);
										}
										else if (TileID.Sets.BlocksWaterDrawingBehindSelf[tile.type])
										{
											WorldGen.SquareWallFrame(i, j);
										}
										tile.type = 0;
										tile.inActive(inActive: false);
										WorldGen.SquareTileFrame(i, j);
										NetMessage.SendData(17, -1, -1, null, 0, i, j);
										num2++;
									}
									else
									{
										tile.active(active: false);
										tile.halfBrick(halfBrick: false);
										tile.frameX = -1;
										tile.frameY = -1;
										tile.ClearBlockPaintAndCoating();
										tile.frameNumber(0);
										if (tile.type == 419)
										{
											Wiring.PokeLogicGate(i, j + 1);
										}
										else if (TileID.Sets.BlocksWaterDrawingBehindSelf[tile.type])
										{
											WorldGen.SquareWallFrame(i, j);
										}
										tile.type = 0;
										tile.inActive(inActive: false);
										WorldGen.SquareTileFrame(i, j);
										NetMessage.SendData(17, -1, -1, null, 0, i, j);
									}
									while (PacketPacing.TilePacketMaxMode && (num2 > PacketPacing.ErasePacketRate || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
									{
										num2 = 0;
										Thread.Sleep(500);
									}
									if (!PacketPacing.TilePacketMaxMode && (num % PacketPacing.ErasePacketRate == 0 || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
									{
										Thread.Sleep(100);
									}
								}
								else
								{
									tile.active(active: false);
									tile.halfBrick(halfBrick: false);
									tile.frameX = -1;
									tile.frameY = -1;
									tile.ClearBlockPaintAndCoating();
									tile.frameNumber(0);
									if (tile.type == 419)
									{
										Wiring.PokeLogicGate(i, j + 1);
									}
									else if (TileID.Sets.BlocksWaterDrawingBehindSelf[tile.type])
									{
										WorldGen.SquareWallFrame(i, j);
									}
									tile.type = 0;
									tile.inActive(inActive: false);
									WorldGen.SquareTileFrame(i, j);
								}
							}
						}
					}
				}
				catch (Exception ex)
				{
					ClientChat.Info($"Remove specific tiles error!\n{ex}");
					ClientLog.LogException(ex);
				}
				if (flag && num >= 1)
				{
					ClientChat.Info($"Removed {num} tiles of type {tileId}");
					NetMessage.SendData(13);
				}
				if (!flag)
				{
					ClientChat.Info($"Tile ID {tileId} was not found in the world and/or loaded sections.");
				}
				Selection.ConcurrentFillOrDels--;
			}
		});
	}

	public static async void ConvertWorld(int conversionType = 0, bool selection = false, PasteJob job = null)
	{
		// Multi Paste on: your bots convert the selection with you, each a strip.
		if (selection && job == null && Main.netMode == 1)
		{
			Rectangle inner = new Rectangle(Math.Min(Selection.SelectionA.X, Selection.SelectionB.X) + 1, Math.Min(Selection.SelectionA.Y, Selection.SelectionB.Y) + 1, Math.Abs(Selection.SelectionA.X - Selection.SelectionB.X) - 1, Math.Abs(Selection.SelectionA.Y - Selection.SelectionB.Y) - 1);
			if (inner.Width > 1 && inner.Height > 0 && IsaClient.MultiClient.MultiPaste.TryStartRegion("Convert", inner, part => ConvertWorld(conversionType, selection: true, part)))
			{
				return;
			}
		}
		await Task.Run(delegate
		{
			PasteJob.Enter(job);
			try
			{
			if (!PurifyingWorld || selection)
			{
				if (!selection)
				{
					PurifyingWorld = true;
				}
				else
				{
					if (job == null && Selection.ConcurrentConversions >= 3)
					{
						PasteJob.Error("Cannot have more than 3 conversions running at once!");
						return;
					}
					Selection.ConcurrentConversions++;
				}
				if (PasteJob.CancelActionNow)
				{
					Selection.CancelAction = false;
				}
				int num = 0;
				int num2 = (selection ? Math.Min(Selection.SelectionA.X, Selection.SelectionB.X) : 10);
				int num3 = (selection ? Math.Min(Selection.SelectionA.Y, Selection.SelectionB.Y) : 10);
				int num4 = (selection ? (num2 + Math.Abs(Selection.SelectionA.X - Selection.SelectionB.X)) : (Main.maxTilesX - 11));
				int num5 = (selection ? (num3 + Math.Abs(Selection.SelectionA.Y - Selection.SelectionB.Y)) : (Main.maxTilesY - 11));
				if (job != null)
				{
					// A strip: the loops go from num2 + 1 to num4 - 1.
					num2 = job.Origin.X - 1;
					num3 = job.Origin.Y - 1;
					num4 = job.Origin.X + job.SizeX;
					num5 = job.Origin.Y + job.SizeY;
				}
				try
				{
					for (int i = num2 + 1; i < num4; i++)
					{
						for (int j = num3 + 1; j < num5; j++)
						{
							if (PasteJob.CancelActionNow)
							{
								PasteJob.Error("Caneled world conversion.");
								Selection.CancelAction = false;
								if (!selection)
								{
									PurifyingWorld = false;
								}
								else
								{
									Selection.ConcurrentConversions--;
								}
								return;
							}
							if (Main.gameMenu)
							{
								if (!selection)
								{
									PurifyingWorld = false;
								}
								else
								{
									Selection.ConcurrentConversions--;
								}
								return;
							}
							if (Framing.GetTileSafely(i, j).active() || Framing.GetTileSafely(i, j).wall > 0)
							{
								WorldGen.Convert(i, j, conversionType, 1);
								if (selection && Main.netMode != 0)
								{
									num++;
									while (selection && PasteJob.MaxModeNow && (num / 2 > PasteJob.TileRateNow || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
									{
										num = 0;
										Thread.Sleep(500);
									}
									if (selection && !PasteJob.MaxModeNow && (num / 2 % PasteJob.TileRateNow == 0 || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
									{
										Thread.Sleep(100);
									}
								}
							}
						}
					}
				}
				catch
				{
				}
				if (!selection)
				{
					PurifyingWorld = false;
					Main.NewText(IsaClient.Languages.L10n.T("The world has been changed to " + ConversionType(conversionType) + "."), 50, byte.MaxValue, 130);
				}
				else
				{
					Selection.ConcurrentConversions--;
					if (job == null)
					{
						Main.NewText(IsaClient.Languages.L10n.T("Comverted selection to " + ConversionType(conversionType) + "."), 50, byte.MaxValue, 130);
					}
				}
			}
			}
			finally
			{
				PasteJob.Leave();
			}
		});
	}

	public static string ConversionType(int type)
	{
		return type switch
		{
			1 => "Corruption", 
			2 => "Hallow", 
			3 => "Mushrooms", 
			4 => "Crimson", 
			5 => "Desert", 
			6 => "Snow", 
			7 => "Forest", 
			_ => "Purity", 
		};
	}

	[Setting("AutoWipeGroundItems", Remembered = true)]
	public static bool AutoDeleteItems;

	[Setting("UnrestrictPlates", Remembered = true)]
	public static bool AnythingOnPlates;

	[Setting("SuperActuationRod", Remembered = true)]
	public static bool ActuatorRodPlus;

	/// <summary>Removes the items on the ground (online, for everyone: <see cref="GroundWipe"/>). autoMode 1: quietly.</summary>
	public static void DeleteGroundItemsCall(int autoMode)
	{
		if (Main.netMode == 1)
		{
			GroundWipe.Start(quiet: autoMode == 1);
			return;
		}
		int num = 0;
		for (int i = 0; i < Main.maxItems; i++)
		{
			WorldItem item = Main.item[i];
			if (item.active && item.type > 0 && !item.beingGrabbed)
			{
				Vector2 position = item.position;
				item.playerIndexTheItemIsReservedFor = 255;
				item.TurnToAir();
				num++;
				if (Main.netMode != 0)
				{
					TeleportTools.FakeNetMessage13(position, Main.LocalPlayer.selectedItem);
					NetMessage.SendData(21, -1, -1, null, i);
				}
			}
		}
		if (num > 0 && Main.netMode != 0)
		{
			NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
		}
		if (autoMode != 1)
		{
			ClientChat.Info("Total items removed: " + num);
		}
	}

	/// <summary>"Super Actuation Rod": actuates a tile without an actuator, placing one online.</summary>
	public static void SuperActuate(int x, int y)
	{
		Wiring.ActuateForced(x, y);
		if (Main.netMode == 1)
		{
			TeleportTools.FakeNetMessage13(Main.LocalPlayer.position, 58);
			RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 849f);
			NetMessage.SendData(17, -1, -1, null, 8, x, y);
			NetMessage.SendData(17, -1, -1, null, 19, x, y);
			NetMessage.SendData(5, -1, -1, null, Main.myPlayer, 58f, Main.mouseItem.prefix);
			NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
		}
	}

	#region Chat commands

	/// <summary>.autowipe / .aw</summary>
	internal static void AutoWipeCommand(CommandContext command)
	{
		AutoDeleteItems = !AutoDeleteItems;
		ClientChat.Info("Automatic item deleter " + (AutoDeleteItems ? "en" : "dis") + "abled.");
	}

	/// <summary>.wipe / .delgrounditems / .wipeitems / .wi</summary>
	internal static void DeleteGroundItemsCommand(CommandContext command)
	{
		DeleteGroundItemsCall(0);
	}

	/// <summary>.superrod / .actuatorrod</summary>
	internal static void ActuatorRodCommand(CommandContext command)
	{
		ActuatorRodPlus = !ActuatorRodPlus;
		ClientChat.Info("Super Actuation Rod " + (ActuatorRodPlus ? "En" : "Dis") + "abled");
	}

	/// <summary>.plates / .anythingonplates</summary>
	internal static void AnythingOnPlatesCommand(CommandContext command)
	{
		AnythingOnPlates = !AnythingOnPlates;
		ClientChat.Info("Anything Fits on Plates/Weapon Racks " + (AnythingOnPlates ? "En" : "Dis") + "abled");
	}

	/// <summary>.freewire</summary>
	internal static void FreeWireCommand(CommandContext command)
	{
		BlockWirePayment = !BlockWirePayment;
		ClientChat.Info("Free mass wire operations " + (BlockWirePayment ? "en" : "dis") + "abled.");
	}

	/// <summary>.regen / .regenworld</summary>
	internal static void RegenWorldCommand(CommandContext command)
	{
		if (WorldGen.generatingWorld)
		{
			ClientChat.Error("The world is already regenerating!");
		}
		else if (Main.netMode != 0)
		{
			ClientChat.Error("You cannot regenerate the world while in an online game.");
		}
		else if (command.Parts.Length <= 1)
		{
			ClientChat.Info("Regenerating world with same seed...");
			RegenerateTheWorld(null);
		}
		else
		{
			string seed = command.Text.Substring(command.Parts[0].Length + 2).Trim();
			ClientChat.Info("Regenerating world using seed '" + seed + "'...");
			RegenerateTheWorld(seed);
		}
	}

	/// <summary>.scanworld</summary>
	internal static void ScanWorldCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			int result3 = -1;
			bool result4 = false;
			if (!int.TryParse(command.Parts[1], out result3) || result3 < 0 || result3 >= TileID.Count)
			{
				ClientChat.Error("Invalid Tile ID.");
				return;
			}
			if (command.Parts.Length > 2)
			{
				bool.TryParse(command.Parts[2], out result4);
			}
			ScanWorldForSpecificTile(result3, result4);
		}
		else
		{
			ClientChat.Error("Value Required.");
		}
	}

	/// <summary>.remalltile / .remtile</summary>
	internal static void RemoveAllTilesCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			int result5 = -1;
			if (!int.TryParse(command.Parts[1], out result5) || result5 < 0 || result5 >= TileID.Count)
			{
				ClientChat.Error("Invalid Tile ID.");
			}
			else
			{
				RemoveTilesOfType(result5);
			}
		}
		else
		{
			ClientChat.Error("Value Required.");
		}
	}

	#endregion
}
