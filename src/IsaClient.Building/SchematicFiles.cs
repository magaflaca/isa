using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using IsaClient.Core;
using Terraria;
using Terraria.ID;
using Terraria.Utilities.FileBrowser;

namespace IsaClient.Building;

/// <summary>Reads and writes TEdit schematic files (.TEditSch).</summary>
public static class SchematicFiles
{
	/// <summary>Schematic version written by this client: 10000 + the 1.4.5.8 world file version (326).</summary>
	public const int SchematicVersion = 10326;

	/// <summary>Oldest accepted schematic version (TEdit files and older schematics made on 1.4.4.9 worlds).</summary>
	public const int MinSchematicVersion = 10279;

	/// <summary>When set, Save/Load skip the native file dialog and use this path (used by the test harness).</summary>
	public static string ForcedDialogPath;

	public static async void Save()
	{
		try
		{
			if (Selection.CopyingFromFillOrDelMode)
			{
				ClientChat.Error("Please wait for the copying to finish.");
				return;
			}
			if (Selection.SavingTEDITSchematic)
			{
				ClientChat.Error("Already saving a schematic, please wait for the operation to finish.");
				return;
			}
			Selection.SavingTEDITSchematic = true;
			await Task.Run(delegate
			{
				if (Selection.SelectionMode)
				{
					int num = Math.Min(Selection.SelectionA.X, Selection.SelectionB.X);
					int num2 = Math.Min(Selection.SelectionA.Y, Selection.SelectionB.Y);
					Selection.SchematicSizeX = Math.Abs(Selection.SelectionA.X - Selection.SelectionB.X) + 1;
					Selection.SchematicSizeY = Math.Abs(Selection.SelectionA.Y - Selection.SelectionB.Y) + 1;
					Selection.CopiedTiles.Clear();
					for (int i = num; i < num + Selection.SchematicSizeX; i++)
					{
						for (int j = num2; j < num2 + Selection.SchematicSizeY; j++)
						{
							Tile tile = Framing.GetTileSafely(i, j);
							if (tile == null)
							{
								tile = default(Tile);
							}
							if (WorldGen.InWorld(i, j))
							{
								TileCopy item = new TileCopy(tile);
								Selection.CopiedTiles.Add(item);
							}
						}
					}
				}
				if (Selection.CopiedTiles.Count <= 0)
				{
					ClientChat.Error("You haven't loaded any schematics nor have you copied any tiles.");
					Selection.SavingTEDITSchematic = false;
				}
				else
				{
					ClientChat.Info("Saving T-Edit Schematics...");
					string text = ForcedDialogPath ?? FileBrowser.OpenFileSavePanel("Save TEdit Schematic File", "TEditSch");
					ForcedDialogPath = null;
					if (!Directory.Exists(Path.Combine(Main.SavePath, "Schematics")))
					{
						Directory.CreateDirectory(Path.Combine(Main.SavePath, "Schematics"));
					}
					if (text != null)
					{
						if (!text.ToLower().EndsWith(".teditsch"))
						{
							text += ".TEditSch";
						}
						using FileStream output = new FileStream(text, FileMode.Create);
						using BinaryWriter binaryWriter = new BinaryWriter(output);
						string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(text);
						int num3 = ((!Selection.SelectionMode) ? (Math.Abs(Selection.CopyStartA.X - Selection.CopyStartB.X) + 1) : (Math.Abs(Selection.SelectionA.X - Selection.SelectionB.X) + 1));
						int num4 = ((!Selection.SelectionMode) ? (Math.Abs(Selection.CopyStartA.Y - Selection.CopyStartB.Y) + 1) : (Math.Abs(Selection.SelectionA.Y - Selection.SelectionB.Y) + 1));
						if (num3 >= 1000 || num4 >= 1000)
						{
							ClientChat.Info("Larger sizes take longer to save...");
						}
						short value = (short)TileID.Count;
						binaryWriter.Write(fileNameWithoutExtension);
						binaryWriter.Write(SchematicVersion);
						binaryWriter.Write(value);
						binaryWriter.Write(TileItemLookup.BuildTileFrameImportantBits());
						binaryWriter.Write(num3);
						binaryWriter.Write(num4);
						SaveAllTileData(binaryWriter, num3, num4);
						binaryWriter.Write(0);
						short value2 = 0;
						binaryWriter.Write(value2);
						binaryWriter.Write(0);
						binaryWriter.Write(fileNameWithoutExtension);
						binaryWriter.Write(SchematicVersion);
						binaryWriter.Write(num3);
						binaryWriter.Write(num4);
						ClientChat.Info($"Saved: {fileNameWithoutExtension}  |  Size: [X: {num3} Y: {num4}]  |  Version: {SchematicVersion}");
						Selection.SavingTEDITSchematic = false;
					}
					Selection.SavingTEDITSchematic = false;
				}
			});
			Selection.SavingTEDITSchematic = false;
		}
		catch (Exception ex)
		{
			ClientChat.Error($"An error has occured while attempting to save TEditSch files\n{ex}");
			Selection.SavingTEDITSchematic = false;
			ClientLog.LogException(ex);
		}
		Selection.SavingTEDITSchematic = false;
	}

	/// <summary>
	/// Writes what is copied now (.copy, a loaded schematic, pixel art) to a .TEditSch, the same way Save does, without
	/// dialogs or chat messages (sharing it in chat). False when there's nothing copied.
	/// </summary>
	public static bool WriteCopied(string path, string name)
	{
		List<TileCopy> tiles = Selection.CopiedTiles;
		int width = Selection.SchematicSizeX;
		int height = Selection.SchematicSizeY;
		if (tiles == null || tiles.Count <= 0 || width <= 0 || height <= 0 || tiles.Count < width * height)
		{
			return false;
		}
		using FileStream output = new FileStream(path, FileMode.Create);
		using BinaryWriter w = new BinaryWriter(output);
		w.Write(name);
		w.Write(SchematicVersion);
		w.Write((short)TileID.Count);
		w.Write(TileItemLookup.BuildTileFrameImportantBits());
		w.Write(width);
		w.Write(height);
		SaveAllTileData(w, width, height);
		w.Write(0);
		w.Write((short)0);
		w.Write(0);
		w.Write(name);
		w.Write(SchematicVersion);
		w.Write(width);
		w.Write(height);
		return true;
	}

	/// <summary>
	/// Reads a .TEditSch's name, size and tiles without touching what is copied (for previews). Null when it can't.
	/// </summary>
	public static List<TileCopy> ReadTiles(string path, out string name, out int width, out int height)
	{
		name = "";
		width = 0;
		height = 0;
		using FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		using BinaryReader r = new BinaryReader(input);
		name = r.ReadString();
		int version = r.ReadInt32();
		int worldVersion = (version > 20000) ? (version - 20000) : ((version > 10000) ? (version - 10000) : version);
		if (version < 10000 || worldVersion < 269 || worldVersion > 400)
		{
			return null;
		}
		int tileCount = r.ReadInt16();
		byte[] importantBits = r.ReadBytes((tileCount + 7) / 8);
		width = r.ReadInt32();
		height = r.ReadInt32();
		if (width <= 0 || height <= 0 || (long)width * height > 4000000)
		{
			return null;
		}
		bool[] important = new bool[tileCount];
		for (int i = 0; i < tileCount; i++)
		{
			important[i] = ((importantBits[i / 8] >> (i % 8)) & 1) != 0;
		}
		List<TileCopy> tiles = new List<TileCopy>(width * height);
		FileFrameImportant = important;
		try
		{
			for (int x = 0; x < width; x++)
			{
				for (int y = 0; y < height; y++)
				{
					TileCopy tile = FetchSingleTileData(r, worldVersion, out int compression);
					tiles.Add(tile);
					while (compression > 0 && y + 1 < height)
					{
						y++;
						tiles.Add(tile);
						compression--;
					}
				}
			}
		}
		finally
		{
			FileFrameImportant = null;
		}
		return tiles;
	}

	public static async void Load()
	{
		try
		{
			if (Selection.OpeningTEDITSchematic)
			{
				ClientChat.Error("Already loading a schematic, please wait for the operation to finish.");
				return;
			}
			Selection.OpeningTEDITSchematic = true;
			PhotoImport.Cancel();
			await Task.Run(delegate
			{
				ClientChat.Info("Opening T-Edit Schematics...");
				string text = ForcedDialogPath ?? FileBrowser.OpenFilePanel("Import TEdit Schematic File", "TEditSch");
				ForcedDialogPath = null;
				if (!Directory.Exists(Path.Combine(Main.SavePath, "Schematics")))
				{
					Directory.CreateDirectory(Path.Combine(Main.SavePath, "Schematics"));
				}
				if (text != null)
				{
					if (!text.ToLower().EndsWith(".teditsch"))
					{
						ClientChat.Error("This isn't a valid TEdit Schematic file. Schematic files use the .TEditSch extension.");
						Selection.OpeningTEDITSchematic = false;
						return;
					}
					Selection.PasteDelayCounter = Selection.PasteDelay;
					using (FileStream input = new FileStream(text, FileMode.Open))
					{
						using BinaryReader binaryReader = new BinaryReader(input);
						string text2 = binaryReader.ReadString();
						int num = binaryReader.ReadInt32();
						// TEdit writes worldVersion + 10000 (v4) or worldVersion + 20000 (v5: v4 + a mod payload after the tile
						// entities, which we never read). The tile layout is the same for every world version since 1.4.4 (269).
						int worldVersion = ((num > 20000) ? (num - 20000) : ((num > 10000) ? (num - 10000) : num));
						if (num < 10000 || worldVersion < 269)
						{
							ClientChat.Error($"Schematic version {worldVersion} isn't compatible with this client.\nThe schematic you've selected was saved on an outdated world.\nRe-save it with TEdit for 1.4.4.9 or newer.");
						}
						else if (worldVersion > 400)
						{
							ClientChat.Error($"Schematic version {num} isn't compatible with this client.\nThe schematic you've selected was saved on a newer, unsupported version.");
						}
						else
						{
							if (Selection.CopiedTiles.Count > 0)
							{
								Selection.CopiedTiles.Clear();
							}
							int tileCount = binaryReader.ReadInt16();
							// The file says which tiles store frames: use that, not this game's table, or one difference shifts every tile after it.
							byte[] importantBits = binaryReader.ReadBytes((tileCount + 7) / 8);
							FileFrameImportant = new bool[tileCount];
							for (int fi = 0; fi < tileCount; fi++)
							{
								FileFrameImportant[fi] = ((importantBits[fi / 8] >> (fi % 8)) & 1) != 0;
							}
							int num2 = binaryReader.ReadInt32();
							int num3 = binaryReader.ReadInt32();
							try
							{
								Selection.CopiedTiles = LoadAllTileData(binaryReader, num2, num3, text2, worldVersion);
							}
							finally
							{
								FileFrameImportant = null;
							}
							ClientChat.Info($"Selected: {text2}  |  Size: [X: {num2}, Y: {num3}]  |  Version: {num}");
							Selection.OpeningTEDITSchematic = false;
						}
					}
					Selection.OpeningTEDITSchematic = false;
					BrushState.DisableBrushes();
					Selection.SelectionMode = false;
					Selection.CopyMode = false;
					PixelArt.DrawPhotoMode = false;
					Selection.PasteMode = true;
					Selection.PasteDelayCounter = Selection.PasteDelay;
				}
				Selection.OpeningTEDITSchematic = false;
			});
		}
		catch (Exception ex)
		{
			ClientChat.Error($"An error has occured while attempting to load TEditSch files\n{ex}");
			ClientLog.LogException(ex);
		}
		Selection.OpeningTEDITSchematic = false;
	}

	/// <summary>Frame-important table of the schematic being read (null: this game's table).</summary>
	[ThreadStatic]
	public static bool[] FileFrameImportant;

	public static TileCopy FetchSingleTileData(BinaryReader r, int schematicVersion, out int compression)
	{
		TileCopy legacyTile = new TileCopy();
		byte b = 0;
		byte b2 = 0;
		byte b3 = 0;
		byte b4 = r.ReadByte();
		bool flag = false;
		bool flag2 = false;
		if ((b4 & 1) == 1)
		{
			flag = true;
			b3 = r.ReadByte();
		}
		if (flag && (b3 & 1) == 1)
		{
			flag2 = true;
			b2 = r.ReadByte();
		}
		if (schematicVersion >= 269 && flag2 && (b2 & 1) == 1)
		{
			b = r.ReadByte();
		}
		if ((b4 & 2) == 2)
		{
			legacyTile.active(active: true);
			int num;
			if ((b4 & 0x20) != 32)
			{
				num = r.ReadByte();
			}
			else
			{
				byte b5 = r.ReadByte();
				num = r.ReadByte();
				num = (num << 8) | b5;
			}
			legacyTile.type = (ushort)num;
			bool[] important = FileFrameImportant;
			if (!((important != null && num < important.Length) ? important[num] : Main.tileFrameImportant[num]))
			{
				legacyTile.frameX = -1;
				legacyTile.frameY = -1;
			}
			else
			{
				legacyTile.frameX = r.ReadInt16();
				legacyTile.frameY = r.ReadInt16();
				if (legacyTile.type == 144)
				{
					legacyTile.frameY = -1;
				}
			}
			if ((b2 & 8) == 8)
			{
				legacyTile.color(r.ReadByte());
			}
		}
		if ((b4 & 4) == 4)
		{
			legacyTile.wall = r.ReadByte();
			if ((b2 & 0x10) == 16)
			{
				legacyTile.wallColor(r.ReadByte());
			}
		}
		byte b6 = (byte)((b4 & 0x18) >> 3);
		if (b6 != 0)
		{
			legacyTile.liquid = r.ReadByte();
			if (schematicVersion >= 269 && (b2 & 0x80) == 128)
			{
				legacyTile.shimmer(shimmer: true);
			}
			else if (b6 > 1)
			{
				if (b6 == 2)
				{
					legacyTile.lava(lava: true);
				}
				else
				{
					legacyTile.honey(honey: true);
				}
			}
		}
		if (b3 > 1)
		{
			if ((b3 & 2) == 2)
			{
				legacyTile.wire(wire: true);
			}
			if ((b3 & 4) == 4)
			{
				legacyTile.wire2(wire2: true);
			}
			if ((b3 & 8) == 8)
			{
				legacyTile.wire3(wire3: true);
			}
			b3 = (byte)((b3 & 0x70) >> 4);
			if (b3 != 0 && (Main.tileSolid[legacyTile.type] || TileID.Sets.NonSolidSaveSlopes[legacyTile.type]))
			{
				if (b3 == 1)
				{
					legacyTile.halfBrick(halfBrick: true);
				}
				else
				{
					b3--;
					legacyTile.slope(b3);
				}
			}
		}
		if (b2 > 1)
		{
			if ((b2 & 2) == 2)
			{
				legacyTile.actuator(actuator: true);
			}
			if ((b2 & 4) == 4)
			{
				legacyTile.inActive(inActive: true);
			}
			if ((b2 & 0x20) == 32)
			{
				legacyTile.wire4(wire4: true);
			}
			if (schematicVersion >= 222 && (b2 & 0x40) == 64)
			{
				legacyTile.wall = (ushort)((r.ReadByte() << 8) | legacyTile.wall);
			}
		}
		if (schematicVersion >= 269 && b > 1)
		{
			if ((b & 2) == 2)
			{
				legacyTile.invisibleBlock(invisibleBlock: true);
			}
			if ((b & 4) == 4)
			{
				legacyTile.invisibleWall(invisibleWall: true);
			}
			if ((b & 8) == 8)
			{
				legacyTile.fullbrightBlock(fullbrightBlock: true);
			}
			if ((b & 0x10) == 16)
			{
				legacyTile.fullbrightWall(fullbrightWall: true);
			}
		}
		byte b7 = (byte)((b4 & 0xC0) >> 6);
		compression = 0;
		switch (b7)
		{
		default:
			compression = r.ReadInt16();
			break;
		case 1:
			compression = r.ReadByte();
			break;
		case 0:
			compression = 0;
			break;
		}
		return legacyTile;
	}

	public static List<TileCopy> LoadAllTileData(BinaryReader r, int maxX, int maxY, string schematicName, int schematicVersion = 279)
	{
		List<TileCopy> list = new List<TileCopy>();
		int compression = 0;
		Selection.SchematicSizeX = maxX;
		Selection.SchematicSizeY = maxY;
		for (int i = 0; i < maxX; i++)
		{
			for (int j = 0; j < maxY; j++)
			{
				try
				{
					TileCopy item = FetchSingleTileData(r, schematicVersion, out compression);
					list.Add(item);
					while (compression > 0)
					{
						j++;
						if (j < maxY)
						{
							list.Add(item);
							compression--;
							continue;
						}
						break;
					}
				}
				catch (Exception ex)
				{
					ClientChat.Error($"Something broke in LoadAllTileData!\n{ex}");
					ClientLog.LogException(ex);
					return list;
				}
			}
		}
		return list;
	}

	public static byte[] StoreSingleTileData(TileCopy tile, BinaryWriter bw, out int dataIndex, out int headerIndex)
	{
		byte[] array = new byte[16];
		dataIndex = 4;
		byte b = 0;
		byte b2 = 0;
		byte b3 = 0;
		byte b4 = 0;
		if (tile.active() && tile.type <= TileID.Count && tile.type != 127)
		{
			b4 |= 2;
			array[dataIndex++] = (byte)tile.type;
			if (tile.type > 255)
			{
				array[dataIndex++] = (byte)(tile.type >> 8);
				b4 |= 0x20;
			}
			if (Main.tileFrameImportant[tile.type])
			{
				array[dataIndex++] = (byte)(tile.frameX & 0xFF);
				array[dataIndex++] = (byte)((tile.frameX & 0xFF00) >> 8);
				array[dataIndex++] = (byte)(tile.frameY & 0xFF);
				array[dataIndex++] = (byte)((tile.frameY & 0xFF00) >> 8);
			}
			if (tile.color() != 0 && tile.color() != 31)
			{
				byte b5 = tile.color();
				b2 |= 8;
				array[dataIndex++] = b5;
			}
		}
		if (tile.wall != 0 && tile.wall <= WallID.Count)
		{
			b4 |= 4;
			array[dataIndex++] = (byte)tile.wall;
			if (tile.wallColor() != 0 && tile.wallColor() != 31)
			{
				byte b6 = tile.wallColor();
				b2 |= 0x10;
				array[dataIndex++] = b6;
			}
		}
		if (tile.liquid != 0)
		{
			if (tile.liquidType() != 3)
			{
				b4 = ((tile.liquidType() == 1) ? ((byte)(b4 | 0x10)) : ((tile.liquidType() != 2) ? ((byte)(b4 | 8)) : ((byte)(b4 | 0x18))));
			}
			else
			{
				b2 |= 0x80;
				b4 |= 8;
			}
			array[dataIndex++] = tile.liquid;
		}
		if (tile.wire())
		{
			b3 |= 2;
		}
		if (tile.wire2())
		{
			b3 |= 4;
		}
		if (tile.wire3())
		{
			b3 |= 8;
		}
		int num = (tile.halfBrick() ? 16 : ((tile.slope() != 0) ? (tile.slope() + 1 << 4) : 0));
		b3 |= (byte)num;
		if (tile.actuator())
		{
			b2 |= 2;
		}
		if (tile.inActive())
		{
			b2 |= 4;
		}
		if (tile.wire4())
		{
			b2 |= 0x20;
		}
		if (tile.wall > 255)
		{
			b2 |= 0x40;
			array[dataIndex++] = (byte)(tile.wall >> 8);
		}
		if (tile.invisibleBlock())
		{
			b |= 2;
		}
		if (tile.invisibleWall())
		{
			b |= 4;
		}
		if (tile.fullbrightBlock() || tile.color() == 31)
		{
			b |= 8;
		}
		if (tile.fullbrightWall() || tile.wallColor() == 31)
		{
			b |= 0x10;
		}
		headerIndex = 3;
		if (b != 0)
		{
			b2 |= 1;
			array[headerIndex--] = b;
		}
		if (b2 != 0)
		{
			b3 |= 1;
			array[headerIndex--] = b2;
		}
		if (b3 != 0)
		{
			b4 |= 1;
			array[headerIndex--] = b3;
		}
		array[headerIndex] = b4;
		return array;
	}

	public static void SaveAllTileData(BinaryWriter bw, int maxX, int maxY)
	{
		int num = 0;
		try
		{
			for (int i = 0; i < maxX; i++)
			{
				int num2;
				for (num2 = 0; num2 < maxY; num2++)
				{
					TileCopy legacyTile = Selection.CopiedTiles[num];
					int dataIndex;
					int headerIndex;
					byte[] array = StoreSingleTileData(legacyTile, bw, out dataIndex, out headerIndex);
					byte b = array[headerIndex];
					short num3 = 0;
					int num4 = num + 1;
					int num5 = maxY - num2 - 1;
					while (num5 > 0 && legacyTile.isTheSameAs(Selection.CopiedTiles[num4]) && TileID.Sets.AllowsSaveCompressionBatching[legacyTile.type])
					{
						num3++;
						num5--;
						num4++;
						num++;
					}
					num2 += num3;
					if (num3 > 0)
					{
						array[dataIndex++] = (byte)(num3 & 0xFF);
						if (num3 <= 255)
						{
							b |= 0x40;
						}
						else
						{
							b |= 0x80;
							array[dataIndex++] = (byte)((num3 & 0xFF00) >> 8);
						}
					}
					array[headerIndex] = b;
					bw.Write(array, headerIndex, dataIndex - headerIndex);
					num++;
				}
			}
		}
		catch (Exception ex)
		{
			ClientChat.Error($"Something broke in SaveAllTileData!\n{ex}");
			ClientLog.LogException(ex);
		}
	}
}
