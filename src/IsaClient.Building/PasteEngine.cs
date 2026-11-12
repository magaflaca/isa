using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ObjectData;
using Terraria.Utilities;
using IsaClient.Core;
using IsaClient.Movement;
using IsaClient.Networking;
using Terraria;

namespace IsaClient.Building;

/// <summary>Pastes copied tiles and schematics into the world: tiles, walls, liquids, chests, tile objects, paint.</summary>
public class PasteEngine
{

	public static void PasteTiles(Point mouseToTile)
	{
		if (Selection.ConcurrentPastes < 6 && !Selection.OpeningTEDITSchematic && Selection.CopiedTiles.Count > 0 && (Selection.SchematicSizeX > 0 || Selection.SchematicSizeY > 0))
		{
			PasteUndo.PushUndo(PasteUndo.CaptureRegion(mouseToTile, Selection.SchematicSizeX, Selection.SchematicSizeY));
		}
		// Multi Paste on: your bots paste it with you, each its own strip.
		if (IsaClient.MultiClient.MultiPaste.TryStart(mouseToTile, Selection.CopiedTiles, Selection.SchematicSizeX, Selection.SchematicSizeY, Placement.BlendAirTileMode || CharacterArt.ClipboardIsArt))
		{
			return;
		}
		PasteTilesInternal(mouseToTile, null);
	}

	/// <summary>One part of a Multi Paste (yours or a bot's), on its own thread.</summary>
	public static void PastePart(PasteJob job)
	{
		PasteTilesInternal(job.Origin, null, job);
	}

	/// <summary>
	/// Liquids of a paste. Tile packets never carry liquid, so without this the server never had them and they vanished
	/// as soon as it resent those tiles. Each tile gets a liquid packet (48) while "holding" the matching bottomless bucket
	/// (TShock checks it, and the range from the player, like for any other tile edit), at most 150 per second (TShock
	/// disables you at 200 liquid edits per second). Removed liquid (amount 0) needs no bucket.
	/// </summary>
	private static bool PasteTiles_Liquids(HashSet<Point> liquidTiles)
	{
		int sent = 0;
		int perSecond = Math.Max(1, Math.Min(150, (PasteJob.LiquidRateNow > 0) ? PasteJob.LiquidRateNow : 150));
		int heldBucket = -1;
		foreach (Point p in liquidTiles)
		{
			if (Main.gameMenu)
			{
				return false;
			}
			if (PasteJob.CancelNow)
			{
				PasteJob.Info("Caneled paste.");
				Selection.CancelPaste = false;
				Selection.ConcurrentPastes--;
				NetMessage.SendData(5, -1, -1, null, Main.myPlayer, 58f);
				NetMessage.SendData(13);
				return false;
			}
			while (Selection.PasteInTShock && PasteJob.DisabledNow)
			{
				Thread.Sleep(50);
				if (Main.gameMenu)
				{
					return false;
				}
			}
			Tile tile = Framing.GetTileSafely(p.X, p.Y);
			TeleportTools.FakeNetMessage13(new Vector2((float)((p.X + 2) * 16), (float)(p.Y * 16)), 58);
			if (tile.liquid > 0)
			{
				int bucket = tile.liquidType() switch
				{
					1 => 4820,
					2 => 5302,
					3 => 5364,
					_ => 3031,
				};
				if (bucket != heldBucket)
				{
					RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, bucket);
					heldBucket = bucket;
				}
			}
			NetMessage.sendWater(p.X, p.Y);
			sent++;
			if (PasteJob.InBotPart)
			{
				// A bot's liquids go at its own .pl, like the rest of its packets.
				PasteJob.PaceBotLiquid(sent);
			}
			else if (sent % perSecond == 0 || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard)
			{
				Thread.Sleep(1050);
			}
		}
		return true;
	}

	/// <summary>
	/// TShock paste: the block (or wall) already there stays when it is the same type, with its paint and coatings, and
	/// placing over it doesn't clear them. Scrapes (Paint Scraper, which TShock accepts) the paint the pasted tile
	/// doesn't have, and every coating when one must go (the coatings the pasted tile has are put back afterwards: the
	/// flags tell the paste they are gone).
	/// </summary>
	private static void ScrapeLeftoverPaint(Tile tile, TileCopy target, int x, int y, ref bool sameGlowBlock, ref bool sameEchoBlock, ref bool sameGlowWall, ref bool sameEchoWall)
	{
		bool blockPaint = tile.active() && tile.color() > 0 && target.color() == 0;
		bool blockCoat = tile.active() && ((tile.fullbrightBlock() && !target.fullbrightBlock()) || (tile.invisibleBlock() && !target.invisibleBlock()));
		bool wallPaint = tile.wall > 0 && tile.wallColor() > 0 && target.wallColor() == 0;
		bool wallCoat = tile.wall > 0 && ((tile.fullbrightWall() && !target.fullbrightWall()) || (tile.invisibleWall() && !target.invisibleWall()));
		if (!blockPaint && !blockCoat && !wallPaint && !wallCoat)
		{
			return;
		}
		TeleportTools.FakeNetMessage13(new Vector2((float)((x + 2) * 16), (float)(y * 16)), 58);
		RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, ItemID.PaintScraper);
		if (blockPaint)
		{
			NetMessage.SendData(63, -1, -1, null, x, y, 0f, 0f);
		}
		if (blockCoat)
		{
			NetMessage.SendData(63, -1, -1, null, x, y, 0f, 1f);
			sameGlowBlock = false;
			sameEchoBlock = false;
		}
		if (wallPaint)
		{
			NetMessage.SendData(64, -1, -1, null, x, y, 0f, 0f);
		}
		if (wallCoat)
		{
			NetMessage.SendData(64, -1, -1, null, x, y, 0f, 1f);
			sameGlowWall = false;
			sameEchoWall = false;
		}
	}

	internal static async void PasteTilesInternal(Point mouseToTile, PasteUndo.PasteUndoEntry undo, PasteJob job = null)
	{
		List<TileCopy> sourceTiles = (job != null) ? job.Tiles : ((undo != null) ? undo.Tiles : Selection.CopiedTiles);
		int sourceSizeX = (job != null) ? job.SizeX : ((undo != null) ? undo.SizeX : Selection.SchematicSizeX);
		int sourceSizeY = (job != null) ? job.SizeY : ((undo != null) ? undo.SizeY : Selection.SchematicSizeY);
		bool blendAir = (job != null) ? job.BlendAir : (undo == null && (Placement.BlendAirTileMode || CharacterArt.ClipboardIsArt));
		await Task.Run(delegate
		{
			PasteJob.Enter(job);
			try
			{
				Placement.TileLoopCount = 0;
				if (Selection.AutoExitPaste && undo == null)
				{
					Selection.PasteMode = false;
				}
				if (Selection.OpeningTEDITSchematic)
				{
					PasteJob.Error("You can't paste while opening a schematic. Please close the dialog or select a schematic.");
				}
				else
				{
					if (job != null || Selection.ConcurrentPastes < 6)
					{
						int schematicSizeX = sourceSizeX;
						int schematicSizeY = sourceSizeY;
						List<TileCopy> list = new List<TileCopy>();
						for (int i = 0; i < sourceTiles.Count; i++)
						{
							TileCopy legacyTile = new TileCopy();
							legacyTile.CopyFrom(sourceTiles[i]);
							list.Add(legacyTile);
						}
						bool flag = false;
						// Tiles whose liquid changes: tile packets never carry liquid, it goes afterwards with liquid packets.
						HashSet<Point> liquidTiles = new HashSet<Point>();
						lock (list)
						{
							Selection.ConcurrentPastes++;
							Vector2 val = default(Vector2);
							while (true)
							{
								int num = 0;
								int num2 = 0;
								int num3 = schematicSizeX;
								int num4 = schematicSizeY;
								int packetsSent = 0;
								_ = Main.LocalPlayer;
								if (PasteJob.CancelNow)
								{
									Selection.CancelPaste = false;
								}
								if ((num3 > 50 || num4 > 50) && Main.netMode == 1 && !flag)
								{
									PasteJob.Info("Pasting tiles, this may take a while.");
								}
								flag = true;
								if ((num3 <= 0 && num4 <= 0) || list.Count == 0)
								{
									PasteJob.Error("No tiles have been copied.");
									Selection.PasteMode = false;
									Selection.ConcurrentPastes--;
									break;
								}
								bool flag2 = false;
								bool flag3 = false;
								int num5 = ((!Selection.PasteInTShock) ? 1 : 2);
								bool flag4 = false;
								int num6 = 0;
								while (true)
								{
									if (num6 >= num5)
									{
										PasteJob.Barrier();
										if (flag2 && Main.netMode != 0)
										{
											PasteTiles_Chests(list, mouseToTile, packetsSent, num3, num4, out packetsSent);
											if (packetsSent == -1)
											{
												return;
											}
										}
										if (flag3 && Main.netMode != 0 && Selection.PasteInTShock)
										{
											PasteJob.Info("Pasting tile objects...");
											Thread.Sleep(100);
											PasteTiles_TileObjects(list, mouseToTile, packetsSent, num3, num4, out packetsSent, sourceTiles);
											if (packetsSent == -1)
											{
												return;
											}
											PasteJob.Info("Paint cleanup...");
											Thread.Sleep(100);
											for (int j = 0; j < 2; j++)
											{
												PasteTiles_PaintCleanup(list, mouseToTile, num3, num4, out var cancelThePaste);
												if (cancelThePaste)
												{
													return;
												}
											}
										}
										if (liquidTiles.Count > 0 && Main.netMode == 1)
										{
											PasteJob.Info("Pasting liquids...");
											if (!PasteTiles_Liquids(liquidTiles))
											{
												return;
											}
										}
										Thread.Sleep(100);
										Stopwatch stopwatch = new Stopwatch();
										stopwatch.Start();
										WorldGen.RangeFrame(mouseToTile.X - 2, mouseToTile.Y - 2, mouseToTile.X + num3 + 2, mouseToTile.Y + num4 + 2, resetFrame: false, noBreak: true);
										if (Main.netMode != 0)
										{
											if (Selection.PasteInTShock && Main.netMode == 1)
											{
												const int step = 8;
												int startX = mouseToTile.X + step / 2;
												int startY = mouseToTile.Y + step / 2;
												for (int k = startX; k < startX + num3; k += step)
												{
													for (int l = startY; l < startY + num4; l += step)
													{
														if (Selection.PasteInTShock && PasteJob.DisabledNow)
														{
															k = startX;
															l = startY;
															Thread.Sleep(100);
														}
														Tile tile = Framing.GetTileSafely(k, l);
														NetMessage.SendObjectPlacement(Main.myPlayer, k, l, tile.type, 0, 0, 0, 1);
													}
												}
											}
											else
											{
												int num7 = ((num3 < 6) ? num3 : 6);
												int num8 = ((num4 < 13) ? num4 : 13);
												for (int m = mouseToTile.X; m < mouseToTile.X + num3; m += ((num7 == 1) ? num7 : (num7 / 2)))
												{
													for (int n = mouseToTile.Y; n < mouseToTile.Y + num4; n += ((num8 == 1) ? num8 : (num8 - 1)))
													{
														if (WorldGen.InWorld(m - num7, n - num8, 3) && Framing.GetTileSafely(m, n) != null)
														{
															NetMessage.SendTileSquare(-1, m, n, num7, num8);
															while (PasteJob.MaxModeNow && (packetsSent > PasteJob.TileRateNow || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
															{
																packetsSent = 0;
																Thread.Sleep(500);
															}
															if (!PasteJob.MaxModeNow && ((packetsSent > 0 && packetsSent % PasteJob.TileRateNow == 0) || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
															{
																packetsSent = 0;
																Thread.Sleep(100);
															}
														}
													}
												}
											}
										}
										if (Selection.PasteInTShock && Main.netMode == 1)
										{
											int num9 = 500;
											if (num3 > 50 || num4 > 50)
											{
												num9 = 2500;
											}
											int num10 = 0;
											while (num10 < num9)
											{
												if (!PasteJob.DisabledNow)
												{
													Thread.Sleep(1);
													num10++;
													continue;
												}
												goto IL_1254;
											}
										}
										stopwatch.Stop();
										PasteJob.Info((undo != null) ? $"Undo complete: restored {num2} tiles." : $"Pasted {num2} tiles. Ctrl+Z (or .undo) to undo.");
										Selection.ConcurrentPastes--;
										NetMessage.SendData(5, -1, -1, null, Main.myPlayer, 58f);
										NetMessage.SendData(13);
										return;
									}
									num = 0;
									for (int num11 = mouseToTile.X; num11 < mouseToTile.X + num3; num11++)
									{
										for (int num12 = mouseToTile.Y; num12 < mouseToTile.Y + num4; num12++)
										{
											if (Selection.PasteInTShock && PasteJob.DisabledNow)
											{
												PasteJob.Error("Assumed TShock disabled you, pasting will resume after disable ends. Lower your packet rate to avoid this.");
												num11 = mouseToTile.X;
												num12 = mouseToTile.Y;
												num = 0;
												num2 = 0;
												while (PasteJob.DisabledNow)
												{
													Thread.Sleep(20);
													if (!PasteJob.CancelNow)
													{
														continue;
													}
													goto IL_01e7;
												}
												goto IL_01d5;
											}
											goto IL_01e7;
											IL_01e7:
											if (PasteJob.CancelNow)
											{
												PasteJob.Info("Caneled paste.");
												Selection.CancelPaste = false;
												Selection.ConcurrentPastes--;
												NetMessage.SendData(5, -1, -1, null, Main.myPlayer, 58f);
												NetMessage.SendData(13);
												WorldGen.RangeFrame(mouseToTile.X, mouseToTile.Y, mouseToTile.X + num3, mouseToTile.Y + num4, resetFrame: false, noBreak: true);
												return;
											}
											if (Main.gameMenu)
											{
												Selection.ConcurrentPastes = 0;
												return;
											}
											Tile tile2 = Framing.GetTileSafely(num11, num12);
											if (!Main.sectionManager.SectionLoaded(Netplay.GetSectionX(num11), Netplay.GetSectionY(num12)) && ServerDetection.OnTshockServer && !flag4)
											{
												PasteJob.Error("Sections are not fully loaded, paste may be slow initially.");
												flag4 = true;
											}
											if (list[num].wall > 0 && Main.netMode == 1 && Selection.PasteInTShock)
											{
												list[num].wall = Placement.ConvertUnsafeWall(list[num].wall);
											}
											if (Main.netMode != 0 && Selection.PasteInTShock && !ObjectPaste.Recognizes(list[num]))
											{
												LazyTileFixup(list[num]);
											}
											bool flag5 = list[num].active();
											bool flag6 = list[num].wall > 0;
											bool flag7 = tile2.type == list[num].type && flag5;
											bool flag8 = tile2.halfBrick() == list[num].halfBrick();
											bool flag9 = tile2.slope() == list[num].slope();
											bool flag10 = flag7 && tile2.color() == list[num].color();
											bool flag11 = flag7 && tile2.fullbrightBlock() == list[num].fullbrightBlock();
											bool flag12 = flag7 && tile2.invisibleBlock() == list[num].invisibleBlock();
											bool flag13 = tile2.wall == list[num].wall && flag6;
											bool flag14 = flag13 && tile2.wallColor() == list[num].wallColor();
											bool flag15 = flag13 && tile2.fullbrightWall() == list[num].fullbrightWall();
											bool flag16 = flag13 && tile2.invisibleWall() == list[num].invisibleWall();
											bool flag17 = tile2.wire() == list[num].wire();
											bool flag18 = tile2.wire2() == list[num].wire2();
											bool flag19 = tile2.wire3() == list[num].wire3();
											bool flag20 = tile2.wire4() == list[num].wire4();
											bool flag21 = tile2.actuator() == list[num].actuator();
											bool flag22 = tile2.MatchesCopy(list[num]);
											bool flag23 = !blendAir || !TileItemLookup.IsTileAir(list[num]);
											if (TileID.Sets.BasicChest[list[num].type])
											{
												flag2 = true;
											}
											if (Selection.PasteInTShock && Main.netMode == 1 && flag23)
											{
												if (tile2.active() && (!flag7 || !flag8 || !flag9))
												{
													tile2.ClearTile();
													flag8 = false;
													flag9 = false;
													flag10 = false;
													flag11 = false;
													flag12 = false;
													NetMessage.SendData(17, -1, -1, null, 4, num11, num12);
												}
												if (tile2.wall > 0 && !flag13)
												{
													tile2.Clear(TileDataType.Wall);
													flag14 = false;
													flag15 = false;
													flag16 = false;
													NetMessage.SendData(17, -1, -1, null, 2, num11, num12);
												}
												if (tile2.wire() && !flag17)
												{
													NetMessage.SendData(109, -1, -1, null, num11, num12, num11, num12, 33);
												}
												if (tile2.wire2() && !flag18)
												{
													NetMessage.SendData(109, -1, -1, null, num11, num12, num11, num12, 36);
												}
												if (tile2.wire3() && !flag19)
												{
													NetMessage.SendData(109, -1, -1, null, num11, num12, num11, num12, 34);
												}
												if (tile2.wire4() && !flag20)
												{
													NetMessage.SendData(109, -1, -1, null, num11, num12, num11, num12, 40);
												}
												if (tile2.actuator() && !flag21)
												{
													NetMessage.SendData(109, -1, -1, null, num11, num12, num11, num12, 48);
												}
												// A block or wall that stays keeps its paint and coatings on the server (only breaking it clears them),
												// and below only the paint the pasted tile has is sent: scrape off what it doesn't have.
												ScrapeLeftoverPaint(tile2, list[num], num11, num12, ref flag11, ref flag12, ref flag15, ref flag16);
											}
											if (WorldGen.InWorld(num11, num12) && !flag22 && flag23)
											{
												if (Main.netMode == 1 && (tile2.liquid != list[num].liquid || (list[num].liquid > 0 && tile2.liquidType() != list[num].liquidType())))
												{
													liquidTiles.Add(new Point(num11, num12));
												}
												tile2.LoadFrom(list[num]);
												// Objects are placed afterwards with real placement packets, only on a server (single player copies them as they are).
												if (TileObjectData.CustomPlace(tile2.type, 0) && Selection.PasteInTShock && Main.netMode == 1)
												{
													tile2.type = 0;
													tile2.HasTile = false;
													tile2.BlockType = BlockType.Solid;
													tile2.TileFrameX = -1;
													tile2.TileFrameY = -1;
													tile2.ClearBlockPaintAndCoating();
													flag7 = false;
													flag3 = true;
													packetsSent--;
													num2--;
												}
												if (Selection.PasteInTShock && Main.netMode == 1)
												{
													val = new Vector2((float)((num11 + 2) * 16), (float)(num12 * 16));
													TeleportTools.FakeNetMessage13(val, 58);
													if (tile2.wall > 0 && TileItemLookup.WallToItemDictionarySearch.ContainsKey(tile2.wall))
													{
														if (!flag13)
														{
															RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, TileItemLookup.WallToItemDictionarySearch[tile2.wall]);
															NetMessage.SendData(17, -1, -1, null, 3, num11, num12, (int)tile2.wall);
														}
														if (tile2.wallColor() > 0 && !flag14)
														{
															RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 1072f);
															NetMessage.SendData(64, -1, -1, null, num11, num12, (int)tile2.wallColor());
														}
														if (tile2.fullbrightWall() && !flag15)
														{
															RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 1072f);
															NetMessage.SendData(64, -1, -1, null, num11, num12, 1f, 1f);
														}
														if (tile2.invisibleWall() && !flag16)
														{
															RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 1072f);
															NetMessage.SendData(64, -1, -1, null, num11, num12, 2f, 1f);
														}
													}
													Item itemDrop = GetItemDrop(num11, num12, list[num], includeLargeObjectDrops: true);
													if (itemDrop.type == 496)
													{
														itemDrop.createTile = 127;
													}
													if (itemDrop.type == 4262)
													{
														itemDrop.createTile = 504;
													}
													int grownType = tile2.type;
													bool grown = tile2.active() && IsGrownOnBaseTile(grownType);
													if (tile2.active() && !grown && !ShouldPlaceThisTile(tile2) && (itemDrop.IsAir || itemDrop.createTile != tile2.type))
													{
														// What the tile drops doesn't place it (living wood and leaves drop wood, ...): use an item that does.
														int placer = ObjectPaste.FindPlaceItem(tile2.type);
														if (placer > 0)
														{
															itemDrop = new Item(placer);
														}
													}
													if ((tile2.active() && itemDrop.createTile != -1 && itemDrop.createTile == tile2.type) || ShouldPlaceThisTile(tile2) || grown)
													{
														packetsSent = PasteTiles_ConvertTiles(num11, num12, packetsSent, tile2, val);
														if (grown)
														{
															// Grass and moss: the base block is down, now the seeds / moss on it.
															int seeds = ObjectPaste.FindPlaceItem(grownType);
															if (seeds > 0)
															{
																TeleportTools.FakeNetMessage13(val, 58);
																RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, seeds);
																NetMessage.SendData(17, -1, -1, null, 1, num11, num12, grownType);
																packetsSent++;
															}
														}
														else if (tile2.active() && !itemDrop.IsAir)
														{
															TeleportTools.FakeNetMessage13(val, 58);
															RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, itemDrop.type);
															NetMessage.SendData(17, -1, -1, null, 1, num11, num12, (int)tile2.type, itemDrop.placeStyle);
														}
														if (tile2.halfBrick() && tile2.active())
														{
															NetMessage.SendData(17, -1, -1, null, 7, num11, num12, 1f);
														}
														else if (tile2.slope() > 0 && tile2.active())
														{
															NetMessage.SendData(17, -1, -1, null, 14, num11, num12, (int)tile2.slope());
														}
														if (tile2.color() > 0 && tile2.active() && !flag10)
														{
															RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 1071f);
															NetMessage.SendData(63, -1, -1, null, num11, num12, (int)tile2.color());
														}
														if (tile2.fullbrightBlock() && tile2.active() && !flag11)
														{
															RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 1071f);
															NetMessage.SendData(63, -1, -1, null, num11, num12, 1f, 1f);
														}
														if (tile2.invisibleBlock() && tile2.active() && !flag12)
														{
															RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 1071f);
															NetMessage.SendData(63, -1, -1, null, num11, num12, 2f, 1f);
														}
													}
													if (tile2.wire() && !flag17)
													{
														RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 3625f);
														NetMessage.SendData(17, -1, -1, null, 5, num11, num12);
													}
													if (tile2.wire2() && !flag18)
													{
														RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 3625f);
														NetMessage.SendData(17, -1, -1, null, 10, num11, num12);
													}
													if (tile2.wire3() && !flag19)
													{
														RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 3625f);
														NetMessage.SendData(17, -1, -1, null, 12, num11, num12);
													}
													if (tile2.wire4() && !flag20)
													{
														RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 3625f);
														NetMessage.SendData(17, -1, -1, null, 16, num11, num12);
													}
													bool flag24 = false;
													if ((tile2.actuator() && !flag21) || (tile2.HasTile && !tile2.HasActuator && tile2.IsActuated && list[num].inActive()))
													{
														if (tile2.active() && tile2.inActive() && !tile2.actuator() && num6 == 1)
														{
															flag24 = true;
														}
														if (num6 == 0)
														{
															RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 849f);
															NetMessage.SendData(17, -1, -1, null, 8, num11, num12);
														}
														if (tile2.active() && tile2.inActive() && num6 == 0)
														{
															NetMessage.SendData(17, -1, -1, null, 19, num11, num12);
														}
														if (flag24 && num6 == 0)
														{
															RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 510f);
															NetMessage.SendData(17, -1, -1, null, 9, num11, num12);
														}
													}
													if (flag24 && num6 == 0)
													{
														tile2.actuator(actuator: false);
													}
													if (tile2.TileType == 137)
													{
														NetMessage.SendData(20, -1, -1, null, num11, num12, 1f, 1f);
													}
													while (PasteJob.MaxModeNow && (packetsSent > PasteJob.TileRateNow || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
													{
														packetsSent = 0;
														Thread.Sleep(500);
													}
													if (!PasteJob.MaxModeNow && ((packetsSent > 0 && packetsSent % PasteJob.TileRateNow == 0) || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
													{
														packetsSent = 0;
														Thread.Sleep(100);
													}
												}
												num2++;
												packetsSent++;
											}
											num++;
											Main.Map.UpdateLighting(num11, num12, byte.MaxValue);
											Main.refreshMap = true;
										}
									}
									Thread.Sleep(250);
									num6++;
									continue;
									IL_1254:
									PasteJob.Error("Assumed TShock disabled you, pasting will restart after disable ends. Lower your packet rate to avoid this.");
									while (PasteJob.DisabledNow)
									{
										Thread.Sleep(20);
										if (PasteJob.CancelNow)
										{
											PasteJob.Info("Caneled paste.");
											Selection.CancelPaste = false;
											Selection.ConcurrentPastes--;
											NetMessage.SendData(5, -1, -1, null, Main.myPlayer, 58f);
											NetMessage.SendData(13);
											WorldGen.RangeFrame(mouseToTile.X, mouseToTile.Y, mouseToTile.X + num3, mouseToTile.Y + num4, resetFrame: false, noBreak: true);
											return;
										}
									}
									Thread.Sleep(250);
									packetsSent = 0;
									break;
									IL_01d5:
									packetsSent = 0;
									Thread.Sleep(250);
									break;
								}
							}
							return;
						}
					}
					PasteJob.Error("Cannot have more than 6 pastes running at once!");
					Selection.PasteMode = false;
				}
			}
			catch (Exception ex)
			{
				Selection.ConcurrentPastes--;
				Selection.PasteMode = false;
				PasteJob.Error($"Paste Tiles error!\n{ex}");
				ClientLog.LogException(ex);
				NetMessage.SendData(5, -1, -1, null, Main.myPlayer, 58f);
				NetMessage.SendData(13);
			}
			finally
			{
				PasteJob.Leave();
			}
		});
	}

	public static int PasteTiles_ConvertTiles(int x, int y, int packetsSent, Tile tile, Vector2 pastePos)
	{
		bool flag = false;
		bool flag2 = false;
		int num = 0;
		int num2 = 0;
		switch (tile.type)
		{
		case 2:
		case 23:
		case 109:
		case 199:
			num = 2;
			break;
		case 661:
		case 662:
			num = 176;
			num2 = 59;
			flag = true;
			break;
		case 60:
		case 70:
			num = 176;
			num2 = 59;
			break;
		case 477:
			num = 2;
			tile.type = 2;
			flag = true;
			break;
		case 492:
			num = 2;
			tile.type = 109;
			flag = true;
			break;
		case 633:
			num = 172;
			num2 = 57;
			break;
		case 179:
		case 180:
		case 181:
		case 182:
		case 183:
		case 381:
		case 534:
		case 536:
		case 539:
		case 625:
		case 627:
			num = 3;
			num2 = 1;
			break;
		case 512:
		case 513:
		case 514:
		case 515:
		case 516:
		case 517:
		case 535:
		case 537:
		case 540:
		case 626:
		case 628:
			num = 129;
			num2 = 38;
			flag2 = true;
			break;
		}
		if (num > 0)
		{
			TeleportTools.FakeNetMessage13(pastePos, 58);
			RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, num);
			NetMessage.SendData(17, -1, -1, null, 1, x, y, num2);
			packetsSent++;
			if (flag2)
			{
				switch (tile.type)
				{
				case 512:
					num = 4349;
					num2 = 179;
					break;
				case 513:
					num = 4350;
					num2 = 180;
					break;
				case 514:
					num = 4351;
					num2 = 181;
					break;
				case 515:
					num = 4352;
					num2 = 182;
					break;
				case 516:
					num = 4353;
					num2 = 183;
					break;
				case 517:
					num = 4354;
					num2 = 381;
					break;
				case 535:
					num = 4377;
					num2 = 534;
					break;
				case 537:
					num = 4378;
					num2 = 536;
					break;
				case 540:
					num = 4389;
					num2 = 539;
					break;
				case 626:
					num = 5127;
					num2 = 625;
					break;
				case 628:
					num = 5128;
					num2 = 627;
					break;
				}
				RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, num);
				NetMessage.SendData(17, -1, -1, null, 1, x, y, num2);
				packetsSent++;
			}
			if (flag)
			{
				switch (tile.type)
				{
				case 2:
					RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 62f);
					NetMessage.SendData(17, -1, -1, null, 1, x, y, 2f);
					tile.type = 477;
					break;
				case 109:
					RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 369f);
					NetMessage.SendData(17, -1, -1, null, 1, x, y, 109f);
					tile.type = 492;
					break;
				case 661:
					RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 195f);
					NetMessage.SendData(17, -1, -1, null, 1, x, y, 60f);
					tile.type = 661;
					break;
				case 662:
					RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 195f);
					NetMessage.SendData(17, -1, -1, null, 1, x, y, 60f);
					tile.type = 662;
					break;
				}
				NetMessage.SendTileSquare(-1, x, y);
				packetsSent += 2;
			}
		}
		return packetsSent;
	}

	public static void PasteTiles_Chests(List<TileCopy> tilesToPaste, Point mouseToTile, int packets, int pasteWidth, int pasteHeight, out int packetsSent)
	{
		packetsSent = 0;
		int num = 0;
		PasteJob.Info("Pasting chests...");
		Stopwatch stopwatch = new Stopwatch();
		stopwatch.Start();
		for (int i = mouseToTile.X; i < mouseToTile.X + pasteWidth; i++)
		{
			for (int j = mouseToTile.Y; j < mouseToTile.Y + pasteHeight; j++)
			{
				while (Selection.PasteInTShock && PasteJob.DisabledNow)
				{
					PasteJob.Error("Assumed TShock disabled you, pasting will resume after disable ends. Lower your tile packet rate to avoid this.");
					i = mouseToTile.X;
					j = mouseToTile.Y;
					num = 0;
					while (PasteJob.DisabledNow)
					{
						Thread.Sleep(20);
						if (PasteJob.CancelNow)
						{
							goto end_IL_0039;
						}
					}
					packetsSent = 0;
					Thread.Sleep(250);
					continue;
					end_IL_0039:
					break;
				}
				if (PasteJob.CancelNow)
				{
					PasteJob.Info("Caneled paste.");
					Selection.CancelPaste = false;
					Selection.ConcurrentPastes--;
					NetMessage.SendData(5, -1, -1, null, Main.myPlayer, 58f);
					NetMessage.SendData(13);
					WorldGen.RangeFrame(mouseToTile.X, mouseToTile.Y, mouseToTile.X + pasteWidth, mouseToTile.Y + pasteHeight, resetFrame: false, noBreak: true);
					packetsSent = -1;
					return;
				}
				if (Main.gameMenu)
				{
					Selection.ConcurrentPastes = 0;
					packetsSent = -1;
					return;
				}
				if (num >= tilesToPaste.Count)
				{
					continue;
				}
				TileCopy legacyTile = tilesToPaste[num];
				if (legacyTile.active() && (TileID.Sets.BasicChest[legacyTile.type] || legacyTile.type == 82 || legacyTile.type == 227))
				{
					Item itemDrop = GetItemDrop(i, j, legacyTile, includeLargeObjectDrops: true);
					if (WorldGen.InWorld(i, j, 10) && itemDrop.createTile > -1 && itemDrop.createTile < TileID.Count)
					{
						TeleportTools.FakeNetMessage13(new Vector2((float)((i + 2) * 16), (float)(j * 16)), 58);
						RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, itemDrop.type);
						WorldGen.PlaceTile(i, j, legacyTile.type, mute: true, forced: true, Main.myPlayer, itemDrop.placeStyle);
						packetsSent++;
					}
				}
				num++;
				while (PasteJob.MaxModeNow && (packetsSent > PasteJob.TileRateNow || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
				{
					packetsSent = 0;
					Thread.Sleep(500);
				}
				if (!PasteJob.MaxModeNow && ((packetsSent > 0 && packetsSent % PasteJob.TileRateNow == 0) || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
				{
					packetsSent = 0;
					Thread.Sleep(100);
				}
				Main.Map.UpdateLighting(i, j, byte.MaxValue);
				Main.refreshMap = true;
			}
		}
		packetsSent += packets;
		stopwatch.Stop();
	}

	public static void PasteTiles_TileObjects(List<TileCopy> tilesToPaste, Point mouseToTile, int packets, int pasteWidth, int pasteHeight, out int packetsSent, List<TileCopy> originalTiles = null)
	{
		packetsSent = 0;
		// Objects whose frames we can rebuild exactly: placed from their top-left tile; the old per-type guesses only
		// handle the tiles none of these cover.
		ObjectPaste.Plan?[] plans = new ObjectPaste.Plan?[tilesToPaste.Count];
		bool[] covered = new bool[tilesToPaste.Count];
		for (int p = 0; p < tilesToPaste.Count && pasteHeight > 0; p++)
		{
			TileCopy planTile = tilesToPaste[p];
			if (!planTile.active() || TileID.Sets.BasicChest[planTile.type] || !ObjectPaste.TryPlan(planTile, out var plan))
			{
				continue;
			}
			plans[p] = plan;
			TileObjectData planData = TileObjectData.GetTileData(plan.Type, plan.Style, plan.Alternate);
			int px = p / pasteHeight;
			int py = p % pasteHeight;
			for (int ox = 0; ox < ((planData != null) ? planData.Width : 1); ox++)
			{
				for (int oy = 0; oy < ((planData != null) ? planData.Height : 1); oy++)
				{
					if (px + ox < pasteWidth && py + oy < pasteHeight)
					{
						covered[(px + ox) * pasteHeight + py + oy] = true;
					}
				}
			}
		}
		int num = tilesToPaste.Count - 1;
		for (int num2 = mouseToTile.X - 1 + pasteWidth; num2 >= mouseToTile.X; num2--)
		{
			for (int num3 = mouseToTile.Y - 1 + pasteHeight; num3 >= mouseToTile.Y; num3--)
			{
				while (Selection.PasteInTShock && PasteJob.DisabledNow)
				{
					PasteJob.Error("Assumed TShock disabled you, pasting will resume after disable ends. Lower your tile packet rate to avoid this.");
					num2 = mouseToTile.X + pasteWidth - 1;
					num3 = mouseToTile.Y + pasteHeight - 1;
					num = tilesToPaste.Count - 1;
					while (PasteJob.DisabledNow)
					{
						Thread.Sleep(20);
						if (PasteJob.CancelNow)
						{
							goto end_IL_002e;
						}
					}
					packetsSent = 0;
					Thread.Sleep(250);
					continue;
					end_IL_002e:
					break;
				}
				if (PasteJob.CancelNow)
				{
					PasteJob.Info("Caneled paste.");
					Selection.CancelPaste = false;
					Selection.ConcurrentPastes--;
					NetMessage.SendData(5, -1, -1, null, Main.myPlayer, 58f);
					NetMessage.SendData(13);
					WorldGen.RangeFrame(mouseToTile.X, mouseToTile.Y, mouseToTile.X + pasteWidth, mouseToTile.Y + pasteHeight, resetFrame: false, noBreak: true);
					packetsSent = -1;
					return;
				}
				if (Main.gameMenu)
				{
					Selection.ConcurrentPastes = 0;
					packetsSent = -1;
					return;
				}
				if (num >= 0 && num < tilesToPaste.Count && plans[num].HasValue)
				{
					if (WorldGen.InWorld(num2, num3, 10))
					{
						packetsSent += PlaceObjectPlan(num2, num3, plans[num].Value, ObjectPaste.OpenDoorDirection((originalTiles != null && num < originalTiles.Count) ? originalTiles[num] : null));
					}
					while (PasteJob.MaxModeNow && (packetsSent > PasteJob.TileRateNow || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
					{
						packetsSent = 0;
						Thread.Sleep(500);
					}
					if (!PasteJob.MaxModeNow && ((packetsSent > 0 && packetsSent % PasteJob.TileRateNow == 0) || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
					{
						packetsSent = 0;
						Thread.Sleep(100);
					}
				}
				else if (num >= 0 && num < tilesToPaste.Count && !covered[num])
				{
					TileCopy legacyTile = tilesToPaste[num];
					if (legacyTile.active())
					{
						Item itemDrop = GetItemDrop(num2, num3, legacyTile, includeLargeObjectDrops: true);
						// Only objects here (blocks went in the first pass): a gem block (Amethyst...) drops a gem that
						// can be placed as a decoration, but the block itself has no object data.
						if (WorldGen.InWorld(num2, num3, 10) && TileObjectData.GetTileData(legacyTile.type, 0) != null && itemDrop.createTile > -1 && itemDrop.createTile < TileID.Count && TileObjectData.CustomPlace(itemDrop.createTile, itemDrop.placeStyle) && (itemDrop.createTile == legacyTile.type || itemDrop.GetFlexibleTileWand() != null))
						{
							TileObject objectData = default(TileObject);
							if (Main.rand == null)
							{
								Main.rand = new UnifiedRandom();
							}
							Tile legacyAsTile = new Tile();
							legacyAsTile.LoadFrom(legacyTile);
							TileObjectData tileData = TileObjectData.GetTileData(legacyAsTile);
							if (tileData == null)
							{
								PasteJob.Error($"Error!\nGetTileData returned null for {TileID.Search.GetName((int)legacyTile.type)} ({legacyTile.type})!\nPlease report this as soon as possible!");
							}
							if (tileData != null && TileObject.CanPlace(num2, num3, legacyTile.type, itemDrop.placeStyle, Main.LocalPlayer.direction, out objectData))
							{
								int num4 = 0;
								int num5 = -1;
								int num6 = 0;
								int num7 = -1;
								int xCoord = itemDrop.createTile;
								int yCoord;
								switch (xCoord)
								{
								case 15:
								case 497:
									num6 = ((legacyTile.frameX == 18) ? 1 : (-1));
									num5 = ((num6 != -1) ? 1 : 0);
									break;
								case 136:
								case 442:
								case 630:
								case 631:
									num5 = legacyTile.frameX / 18;
									break;
								case 132:
									num5 = legacyTile.frameX / 72;
									break;
								case 55:
								case 395:
								case 425:
								case 510:
								case 511:
								case 573:
									num5 = legacyTile.frameX / 36;
									break;
								case 79:
								case 90:
								case 212:
									num6 = ((legacyTile.frameX != 18) ? 1 : (-1));
									num5 = ((num6 != -1) ? 1 : 0);
									break;
								case 105:
									num6 = ((legacyTile.frameY >= 162) ? 1 : (-1));
									num5 = ((num6 != -1) ? 1 : 0);
									break;
								case 388:
									num6 = ((legacyTile.frameY >= 94) ? 1 : (-1));
									num5 = ((num6 != -1) ? 5 : 0);
									break;
								case 349:
									num6 = ((legacyTile.frameY <= 53) ? 1 : (-1));
									num5 = ((num6 != -1) ? 5 : 0);
									break;
								case 453:
									num6 = ((!MathUtil.IsEven(legacyTile.frameX / 18)) ? 1 : (-1));
									num5 = ((num6 != -1) ? 1 : 0);
									break;
								case 470:
								case 506:
									num6 = ((!MathUtil.IsEven(legacyTile.frameX / 36)) ? 1 : (-1));
									num5 = ((num6 == 1) ? 1 : 0);
									break;
								case 545:
									num6 = ((legacyTile.frameY >= 54) ? 1 : (-1));
									num5 = ((num6 != -1) ? 1 : 0);
									break;
								case 617:
									num6 = ((legacyTile.frameY >= 72) ? 1 : (-1));
									num5 = ((num6 != -1) ? 1 : 0);
									break;
								case 520:
									num6 = ((legacyTile.frameX >= 18) ? 1 : (-1));
									num5 = ((num6 != -1) ? 1 : 0);
									break;
								case 650:
									yCoord = (itemDrop.placeStyle = (objectData.style = legacyTile.frameX / 18));
									break;
								case 649:
								case 652:
									yCoord = (itemDrop.placeStyle = (objectData.style = legacyTile.frameX / 36));
									break;
								case 647:
								case 648:
								case 651:
									yCoord = (itemDrop.placeStyle = (objectData.style = legacyTile.frameX / 54 + ((legacyTile.frameY >= 36) ? tileData.StyleWrapLimit : 0)));
									break;
								}
								if (tileData.RandomStyleRange > 0)
								{
									num7 = (tileData.StyleHorizontal ? (legacyTile.frameX / tileData.CoordinateFullWidth) : (legacyTile.frameY / tileData.CoordinateFullHeight));
								}
								objectData.random = num7;
								int alternatesCount;
								if (tileData.AlternatesCount != 0)
								{
									alternatesCount = tileData.AlternatesCount;
									tileData = TileObjectData.GetTileData(itemDrop.createTile, itemDrop.placeStyle, num5);
								}
								if (num5 > -1)
								{
									objectData.alternate = num5;
								}
								int num8 = tileData.CalculatePlacementStyle(objectData.style, objectData.alternate, objectData.random);
								if (tileData.StyleWrapLimit > 0)
								{
									num4 = num8 / tileData.StyleWrapLimit * tileData.StyleLineSkip;
									num8 %= tileData.StyleWrapLimit;
								}
								int num9;
								if (tileData.StyleHorizontal)
								{
									num9 = tileData.CoordinateFullWidth * num8;
									alternatesCount = tileData.CoordinateFullHeight * num4;
									if (num4 == 0)
									{
										alternatesCount = tileData.Origin.Y * (tileData.CoordinateWidth + tileData.CoordinatePadding);
									}
								}
								else
								{
									num9 = tileData.CoordinateFullWidth * num4;
									alternatesCount = tileData.CoordinateFullHeight * num8;
									if (num8 == 0)
									{
										alternatesCount = tileData.Origin.Y * (tileData.CoordinateWidth + tileData.CoordinatePadding);
									}
								}
								int num10 = num9 + tileData.Origin.X * (tileData.CoordinateWidth + tileData.CoordinatePadding);
								int num11 = alternatesCount;
								int num12 = alternatesCount + tileData.Origin.Y * (tileData.CoordinateWidth + tileData.CoordinatePadding);
								if (itemDrop.createTile == 10)
								{
									legacyTile.frameX = (short)(legacyTile.frameX / 54 * 54);
								}
								if (itemDrop.createTile == 240)
								{
									if (itemDrop.placeStyle > tileData.StyleWrapLimit)
									{
										num11 = num12;
									}
									else
									{
										num12 = num11;
									}
								}
								if (itemDrop.createTile == 388)
								{
									num11 += 2;
								}
								if (itemDrop.createTile == 55 || itemDrop.createTile == 425 || itemDrop.createTile == 573 || itemDrop.createTile == 510 || itemDrop.createTile == 511)
								{
									num10 = 36 * num5;
									if (num5 == 3)
									{
										num10 += 18;
									}
								}
								if (legacyTile.frameX == num10 && (legacyTile.frameY == num11 || legacyTile.frameY == num12))
								{
									WorldEditBase.PlaceDirectObject(num2, num3, legacyTile.type, out xCoord, out yCoord, out objectData, itemDrop.placeStyle, num6, num5, num7);
									TeleportTools.FakeNetMessage13(new Vector2((float)((num2 + 2) * 16), (float)(num3 * 16)), 58);
									RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, itemDrop.type);
									if (!TileID.Sets.BasicChest[itemDrop.createTile] && itemDrop.createTile != 88)
									{
										TileObjectData.CallPostPlacementPlayerHook(num2, num3, itemDrop.createTile, itemDrop.placeStyle, -1, num5, objectData);
										if (!Main.tileContainer[itemDrop.createTile] && itemDrop.createTile != 423)
										{
											NetMessage.SendObjectPlacement(-1, num2, num3, objectData.type, itemDrop.placeStyle, objectData.alternate, objectData.random, num6);
											if (itemDrop.createTile == 15)
											{
												NetMessage.SendTileSquare(-1, num2 - 1, num3 - 1, 3);
											}
											if (itemDrop.createTile == 79 || itemDrop.createTile == 90)
											{
												NetMessage.SendTileSquare(-1, num2, num3, 5);
											}
										}
									}
									else if (itemDrop.createTile == 88)
									{
										NetMessage.SendData(34, -1, -1, null, 2, num2, num3, itemDrop.placeStyle);
									}
									else
									{
										WorldGen.PlaceTile(num2, num3, itemDrop.createTile, mute: true, forced: false, Main.myPlayer, itemDrop.placeStyle);
										packetsSent++;
									}
									packetsSent++;
								}
								while (PasteJob.MaxModeNow && (packetsSent > PasteJob.TileRateNow || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
								{
									packetsSent = 0;
									Thread.Sleep(500);
								}
								if (!PasteJob.MaxModeNow && ((packetsSent > 0 && packetsSent % PasteJob.TileRateNow == 0) || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
								{
									packetsSent = 0;
									Thread.Sleep(100);
								}
							}
						}
					}
				}
				num--;
				Main.Map.UpdateLighting(num2, num3, byte.MaxValue);
				Main.refreshMap = true;
			}
		}
		packetsSent += packets;
	}

	/// <summary>Places one object the way the player would (item on the cursor, placement packet), then opens doors that were open.</summary>
	private static int PlaceObjectPlan(int x, int y, ObjectPaste.Plan plan, int openDoorDirection)
	{
		int sent = 0;
		int ox = x + plan.Origin.X;
		int oy = y + plan.Origin.Y;
		TeleportTools.FakeNetMessage13(new Vector2((float)((ox + 2) * 16), (float)(oy * 16)), 58);
		RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, plan.Item);
		sent++;
		bool placedHere = false;
		TileObject objectData;
		if (TileObject.CanPlace(ox, oy, plan.Type, plan.Style, plan.Direction, out objectData))
		{
			objectData.random = plan.Random;
			placedHere = TileObject.Place(objectData);
		}
		if (placedHere)
		{
			TileObjectData.CallPostPlacementPlayerHook(ox, oy, plan.Type, plan.Style, plan.Direction, objectData.alternate, objectData);
		}
		if (!Main.tileContainer[plan.Type] && plan.Type != 423)
		{
			NetMessage.SendObjectPlacement(-1, ox, oy, plan.Type, plan.Style, placedHere ? objectData.alternate : plan.Alternate, plan.Random, plan.Direction);
			sent++;
		}
		if (openDoorDirection != 0 && plan.Type == 10)
		{
			NetMessage.SendData(19, -1, -1, null, 0, ox, oy, openDoorDirection);
			WorldGen.OpenDoor(ox, oy, openDoorDirection);
			sent++;
		}
		return sent;
	}

	public static void PasteTiles_PaintCleanup(List<TileCopy> tilesToPaste, Point mouseToTile, int pasteWidth, int pasteHeight, out bool cancelThePaste)
	{
		Stopwatch stopwatch = new Stopwatch();
		stopwatch.Start();
		cancelThePaste = false;
		int num = 0;
		int num2 = 0;
		for (int i = mouseToTile.X; i < mouseToTile.X + pasteWidth; i++)
		{
			for (int j = mouseToTile.Y; j < mouseToTile.Y + pasteHeight; j++)
			{
				while (Selection.PasteInTShock && PasteJob.DisabledNow)
				{
					PasteJob.Error("Assumed TShock disabled you, pasting will resume after disable ends. Lower your paint packet rate to avoid this.");
					i = mouseToTile.X;
					j = mouseToTile.Y;
					num = 0;
					while (PasteJob.DisabledNow)
					{
						Thread.Sleep(20);
						if (PasteJob.CancelNow)
						{
							goto end_IL_002d;
						}
					}
					num2 = 0;
					Thread.Sleep(250);
					continue;
					end_IL_002d:
					break;
				}
				if (PasteJob.CancelNow)
				{
					PasteJob.Info("Caneled paste.");
					Selection.CancelPaste = false;
					Selection.ConcurrentPastes--;
					NetMessage.SendData(5, -1, -1, null, Main.myPlayer, 58f);
					NetMessage.SendData(13);
					WorldGen.RangeFrame(mouseToTile.X, mouseToTile.Y, mouseToTile.X + pasteWidth, mouseToTile.Y + pasteHeight, resetFrame: false, noBreak: true);
					cancelThePaste = true;
					return;
				}
				if (Main.gameMenu)
				{
					Selection.ConcurrentPastes = 0;
					cancelThePaste = true;
					return;
				}
				if (num < tilesToPaste.Count)
				{
					Tile tile = Framing.GetTileSafely(i, j);
					TileCopy legacyTile = tilesToPaste[num];
					if (tile.active() && tile.TileColor != legacyTile.color())
					{
						tile.TileColor = legacyTile.color();
						RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 1071f);
						NetMessage.SendData(63, -1, -1, null, i, j, (int)tile.color());
						num2++;
					}
					if (tile.active() && tile.fullbrightBlock() != legacyTile.fullbrightBlock())
					{
						tile.IsTileFullbright = legacyTile.fullbrightBlock();
						RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 1071f);
						NetMessage.SendData(63, -1, -1, null, i, j, 1f, 1f);
						num2++;
					}
					if (tile.active() && tile.invisibleBlock() != legacyTile.invisibleBlock())
					{
						tile.IsTileInvisible = legacyTile.invisibleBlock();
						RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, 1071f);
						NetMessage.SendData(63, -1, -1, null, i, j, 2f, 1f);
						num2++;
					}
					while (PasteJob.MaxModeNow && (num2 > PasteJob.PaintRateNow || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
					{
						num2 = 0;
						Thread.Sleep(500);
					}
					if (!PasteJob.MaxModeNow && ((num2 > 0 && num2 % PasteJob.PaintRateNow == 0) || PacketPacing.ServerSilentGuard || Performance.LowFpsGuard))
					{
						num2 = 0;
						Thread.Sleep(100);
					}
					num++;
					Main.Map.UpdateLighting(i, j, byte.MaxValue);
					Main.refreshMap = true;
				}
			}
		}
		stopwatch.Stop();
	}

	public static void LazyTileFixup(TileCopy tile)
	{
		switch (tile.type)
		{
		case 83:
		case 84:
			tile.type = 82;
			break;
		case 4:
			if (tile.frameX > 65)
			{
				tile.frameX -= 65;
			}
			break;
		case 33:
		case 42:
		case 49:
		case 92:
		case 174:
		case 372:
		case 419:
		case 420:
		case 423:
		case 646:
			while (tile.frameX > 17)
			{
				tile.frameX -= 18;
			}
			break;
		case 35:
		case 95:
		case 100:
		case 173:
		case 411:
		case 564:
		case 565:
			while (tile.frameX > 35)
			{
				tile.frameX -= 36;
			}
			break;
		case 93:
			if ((tile.frameX > 17 && tile.frameX < 36) || tile.frameX > 53)
			{
				tile.frameX -= 18;
			}
			break;
		case 132:
		case 139:
			if ((tile.frameX > 35 && tile.frameX < 72) || tile.frameX > 107)
			{
				tile.frameX -= 36;
			}
			break;
		case 506:
			if (tile.frameX > 71)
			{
				tile.frameX -= 72;
			}
			break;
		case 11:
			if (tile.frameX == 0 || tile.frameX == 54 || tile.frameX == 72 || tile.frameX == 126)
			{
				tile.type = 10;
				tile.frameX = (short)(tile.frameX / 72 * 54);
				break;
			}
			tile.type = 0;
			tile.active(active: false);
			tile.frameX = 0;
			tile.frameY = 0;
			tile.ClearBlockPaintAndCoating();
			break;
		case 244:
		case 405:
		case 452:
			if (tile.frameX > 53)
			{
				tile.frameX -= 54;
			}
			break;
		case 34:
			if ((tile.frameX > 53 && tile.frameX < 108) || tile.frameX > 161)
			{
				tile.frameX -= 54;
			}
			break;
		case 136:
		case 144:
			if (tile.frameY > 17)
			{
				tile.frameY -= 18;
			}
			break;
		case 215:
		case 440:
		case 455:
			while (tile.frameY > 31)
			{
				tile.frameY -= 32;
			}
			break;
		case 207:
		case 209:
		case 406:
		case 410:
		case 480:
		case 509:
		case 657:
		case 658:
			while (tile.frameY > 53)
			{
				tile.frameY -= 54;
			}
			break;
		case 386:
			if ((tile.frameX < 36 && tile.frameY >= 18) || (tile.frameX >= 36 && tile.frameY == 0))
			{
				tile.type = 387;
				tile.frameY = 0;
				if (tile.frameX >= 36)
				{
					tile.frameX -= 36;
				}
			}
			else
			{
				tile.type = 0;
				tile.active(active: false);
				tile.frameX = 0;
				tile.frameY = 0;
				tile.ClearBlockPaintAndCoating();
			}
			break;
		case 389:
			tile.type = 388;
			break;
		case 557:
			tile.type = 546;
			break;
		case 510:
		case 511:
			tile.frameY = (short)((!MathUtil.IsEven(tile.frameY / 18)) ? 18 : 0);
			break;
		case 445:
			if (tile.frameX > 0)
			{
				tile.frameX = 0;
			}
			if (tile.frameY > 0)
			{
				tile.frameY = 0;
			}
			break;
		case 50:
			if (tile.frameX > 72)
			{
				tile.frameX = 0;
			}
			break;
		case 185:
			if (tile.frameY > 17)
			{
				tile.type = 649;
				tile.frameY -= 18;
			}
			else
			{
				tile.type = 649;
			}
			break;
		case 233:
			if (tile.frameY > 35)
			{
				tile.type = 652;
				tile.frameY -= 36;
			}
			else
			{
				tile.type = 233;
			}
			break;
		case 186:
			tile.type = 186;
			break;
		case 187:
			tile.type = 187;
			break;
		case 668:
			tile.type = 0;
			break;
		case 624:
		case 656:
			tile.type = 0;
			tile.frameX = -1;
			tile.frameY = -1;
			tile.ClearTile();
			tile.ClearBlockPaintAndCoating();
			break;
		}
	}

	/// <summary>Grass and moss: pasted as their base block (dirt, mud, ash, stone) plus the seeds or moss item.</summary>
	public static bool IsGrownOnBaseTile(int type)
	{
		switch (type)
		{
		case 2:
		case 23:
		case 60:
		case 70:
		case 109:
		case 179:
		case 180:
		case 181:
		case 182:
		case 183:
		case 199:
		case 381:
		case 534:
		case 536:
		case 539:
		case 625:
		case 627:
		case 633:
			return true;
		default:
			return false;
		}
	}

	public static bool ShouldPlaceThisTile(Tile tile)
	{
		switch (tile.type)
		{
		case 477:
		case 492:
		case 512:
		case 513:
		case 514:
		case 515:
		case 516:
		case 517:
		case 535:
		case 537:
		case 540:
		case 626:
		case 628:
		case 661:
		case 662:
			return true;
		default:
			return false;
		}
	}

	public static void DrawSchematicPreview(SpriteBatch spriteBatch)
	{
		try
		{
			if (Main.gameMenu || !Selection.PasteMode)
			{
				return;
			}
			int num = 0;
			Color val = default(Color);
			val = new Color(210, 210, 210, 100);
			int schematicSizeX = Selection.SchematicSizeX;
			int schematicSizeY = Selection.SchematicSizeY;
			if (!Main.mapFullscreen && !Main.LocalPlayer.mouseInterface)
			{
				Main.instance.MouseText(IsaClient.Languages.L10n.T(Selection.PasteModeString()), 0, 0);
				if (Main.mouseRight)
				{
					if (!Selection.MotionlessPaste)
					{
						Selection.PasteImagePreviewPos = Main.MouseWorld.ToTileCoordinates();
					}
					Selection.MotionlessPaste = true;
				}
				else
				{
					Selection.MotionlessPaste = false;
					Selection.PasteImagePreviewPos = Main.MouseWorld.ToTileCoordinates();
				}
				if ((schematicSizeX <= 0 && schematicSizeY <= 0) || Selection.CopiedTiles.Count <= 0 || !(Selection.PasteImagePreviewPos != Point.Zero))
				{
					return;
				}
				Vector2 val2 = default(Vector2);
				// Only what is on screen (a big picture has millions of tiles): WithinScreenBounds lets 150x80 through.
				List<TileCopy> copied = Selection.CopiedTiles;
				int originX = Selection.PasteImagePreviewPos.X;
				int originY = Selection.PasteImagePreviewPos.Y;
				int screenLeft = (int)(Main.screenPosition.X / 16f) - 2;
				int screenTop = (int)(Main.screenPosition.Y / 16f) - 2;
				int firstX = Math.Max(originX, screenLeft);
				int endX = Math.Min(originX + schematicSizeX, screenLeft + 154);
				int firstY = Math.Max(originY, screenTop);
				int endY = Math.Min(originY + schematicSizeY, screenTop + 84);
				for (int i = firstX; i < endX; i++)
				{
					for (int j = firstY; j < endY; j++)
					{
						num = (i - originX) * schematicSizeY + (j - originY);
						if (num >= copied.Count)
						{
							continue;
						}
						TileCopy legacyTile = copied[num];
						if (WorldGen.InWorld(i, j, 1) && Framing.GetTileSafely(i, j) != null)
						{
							val2 = new Vector2((float)i * 16f, (float)j * 16f);
							SpriteEffects val3 = (SpriteEffects)0;
							val2 -= Main.screenPosition;
							Vector2 val4 = val2;
							if (Main.player[Main.myPlayer].gravDir == -1f)
							{
								val2.Y = (float)Main.screenHeight - val2.Y - 16f;
								val4.Y = (float)Main.screenHeight - val4.Y - 32f;
								val3 |= SpriteEffects.FlipVertically;
							}
							if (BrushState.WithinScreenBounds(i, j) && legacyTile != null)
							{
								if (legacyTile.wall > 0)
								{
									Main.instance.LoadWall(legacyTile.wall);
									Texture2D value = TextureAssets.Wall[legacyTile.wall].Value;
									Texture2D val5 = Main.instance.TilesRenderer._paintSystem.TryGetWallAndRequestIfNotReady(legacyTile.wall, legacyTile.wallColor());
									if (val5 == null)
									{
										val5 = value;
									}
									spriteBatch.Draw(val5, val4, (Rectangle?)new Rectangle(44, 44, 32, 32), val, 0f, Vector2.Zero, 1f, val3, 0f);
								}
								if (legacyTile.active())
								{
									Main.instance.LoadTiles(legacyTile.type);
									Texture2D value2 = TextureAssets.Tile[legacyTile.type].Value;
									Texture2D val6 = Main.instance.TilesRenderer._paintSystem.TryGetTileAndRequestIfNotReady(legacyTile.type, 0, legacyTile.color());
									if (val6 == null)
									{
										val6 = value2;
									}
									spriteBatch.Draw(val6, val2, (Rectangle?)new Rectangle((int)legacyTile.frameX, (int)legacyTile.frameY, 16, 16), val, 0f, Vector2.Zero, 1f, val3, 0f);
								}
							}
						}
					}
				}
			}
			else
			{
				if ((schematicSizeX <= 0 && schematicSizeY <= 0) || Selection.CopiedTiles.Count <= 0 || !Main.mapFullscreen)
				{
					return;
				}
				Main.instance.MouseText(IsaClient.Languages.L10n.T(Selection.PasteModeString()), 0, 0);
				num = 0;
				TeleportTools.TryGetMapCoordinatesV2(Terraria.GameInput.PlayerInput.UnscaledMouseX, Terraria.GameInput.PlayerInput.UnscaledMouseY, 0, out var result);
				if (result.X <= -1 || result.Y <= -1)
				{
					return;
				}
				Rectangle val7 = default(Rectangle);
				Rectangle val8 = default(Rectangle);
				Rectangle val9 = default(Rectangle);
				Rectangle val11 = default(Rectangle);
				Rectangle val12 = default(Rectangle);
				Rectangle val13 = default(Rectangle);
				// Only what is on screen, and when zoomed out one tile per screen pixel (a big picture has millions).
				List<TileCopy> copied = Selection.CopiedTiles;
				TeleportTools.TryGetMapCoordinatesV2(0, 0, 1, out var mapOrigin);
				float mapScale = Math.Max(0.01f, Main.mapFullscreenScale);
				int step = (mapScale >= 1f) ? 1 : (int)Math.Ceiling(1f / mapScale);
				int firstK = Math.Max(result.X, (int)((0f - mapOrigin.X) / mapScale) - 2);
				int endK = Math.Min(result.X + schematicSizeX, (int)((Main.screenWidth - mapOrigin.X) / mapScale) + 3);
				int firstL = Math.Max(result.Y, (int)((0f - mapOrigin.Y) / mapScale) - 2);
				int endL = Math.Min(result.Y + schematicSizeY, (int)((Main.screenHeight - mapOrigin.Y) / mapScale) + 3);
				firstK = result.X + (firstK - result.X) / step * step;
				firstL = result.Y + (firstL - result.Y) / step * step;
				for (int k = firstK; k < endK; k += step)
				{
					for (int l = firstL; l < endL; l += step)
					{
						num = (k - result.X) * schematicSizeY + (l - result.Y);
						if (num >= copied.Count)
						{
							continue;
						}
						TileCopy legacyTile2 = copied[num];
						if (WorldGen.InWorld(k, l, 6) && Framing.GetTileSafely(k, l) != null && legacyTile2 != null)
						{
							TeleportTools.TryGetMapCoordinatesV2(k, l, 1, out var result2);
							TeleportTools.TryGetMapCoordinatesV2(k + step, l + step, 1, out var result3);
							TeleportTools.TryGetMapCoordinatesV2(k + step + 1, l + step + 1, 1, out var result4);
							if (legacyTile2.wall > 0)
							{
								val7 = new Rectangle(result2.X, result2.Y, result4.X - result2.X, result4.Y - result2.Y);
								val8 = new Rectangle(0, 0, Main.screenWidth + 1, Main.screenHeight + 1);
								Rectangle.Intersect(ref val8, ref val7, out val9);
								if (val9.Width > 0 && val9.Height > 0)
								{
									Main.instance.LoadWall(legacyTile2.wall);
									Texture2D value3 = TextureAssets.Wall[legacyTile2.wall].Value;
									Texture2D val10 = Main.instance.TilesRenderer._paintSystem.TryGetWallAndRequestIfNotReady(legacyTile2.wall, legacyTile2.wallColor());
									if (val10 == null)
									{
										val10 = value3;
									}
									spriteBatch.Draw(val10, val9, (Rectangle?)new Rectangle(44, 44, 32, 32), val);
								}
							}
							if (legacyTile2.active())
							{
								val11 = new Rectangle(result2.X, result2.Y, result3.X - result2.X, result3.Y - result2.Y);
								val12 = new Rectangle(0, 0, Main.screenWidth + 1, Main.screenHeight + 1);
								Rectangle.Intersect(ref val12, ref val11, out val13);
								if (val13.Width > 0 && val13.Height > 0)
								{
									Main.instance.LoadTiles(legacyTile2.type);
									Texture2D value4 = TextureAssets.Tile[legacyTile2.type].Value;
									Texture2D val14 = Main.instance.TilesRenderer._paintSystem.TryGetTileAndRequestIfNotReady(legacyTile2.type, 0, legacyTile2.color());
									if (val14 == null)
									{
										val14 = value4;
									}
									spriteBatch.Draw(val14, val13, (Rectangle?)new Rectangle((int)legacyTile2.frameX, (int)legacyTile2.frameY, 16, 16), val);
								}
							}
						}
					}
				}
			}
		}
		catch (ArgumentOutOfRangeException)
		{
			PasteJob.Info("Pick the the file again..");
			Selection.PasteMode = false;
		}
		catch (Exception ex2)
		{
			Selection.PasteMode = false;
			PasteJob.Error($"Drawing Schematic Preview error!\n{ex2}");
			ClientLog.LogException(ex2);
		}
	}

	/// <summary>
	/// Resolves the item that places <paramref name="tileCache"/> (used to fake the held item on TShock servers).
	/// Uses the vanilla 1.4.5.8 drop table so every tile of the current version is covered; chests are resolved from the
	/// schematic frame instead of the world, because the tile is not placed yet when this runs.
	/// </summary>
	public static Item GetItemDrop(int x, int y, TileCopy tileCache, bool includeLargeObjectDrops = false)
	{
		int setDefaultsToType = 0;
		if (includeLargeObjectDrops && (tileCache.type == 21 || tileCache.type == 467))
		{
			setDefaultsToType = WorldGen.GetItemDrop_Chests(tileCache.frameX / 36, tileCache.type == 467);
		}
		else
		{
			Tile tile = new Tile();
			tile.LoadFrom(tileCache);
			try
			{
				WorldGen.KillTile_GetItemDrops(x, y, tile, out setDefaultsToType, out var _, out var _, out var _, out var _, includeLargeObjectDrops);
			}
			catch
			{
				setDefaultsToType = 0;
			}
			if (setDefaultsToType <= 0 && TileItemLookup.TileToItemDictionarySearch.TryGetValue(tileCache.type, out var value))
			{
				setDefaultsToType = value;
			}
		}
		return new Item(setDefaultsToType);
	}
}
