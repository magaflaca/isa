using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using IsaClient.Core;
using Terraria;
using Terraria.Utilities.FileBrowser;

namespace IsaClient.Building;

/// <summary>
/// .tilemap files of the "Terraria Pixel Art Generator" ("Guardar TileMap"): its finished picture, block by block,
/// loaded into the paste clipboard to place it in the world and compare.
/// Format: byte 100, int width, int height, then column by column ushort tiles, ushort walls (65535 = none) and byte
/// paints (the paint of whatever is there), then byte 100.
/// </summary>
internal static class TileMapFiles
{
	public const string Extension = ".tilemap";

	private const ushort None = ushort.MaxValue;

	/// <summary>Where the generator saves them (it calls it TerraAngel's pictures folder).</summary>
	public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Terraria", "TerraAngel", "Imagenes");

	/// <summary>.loadtilemap [name]: a name is looked up in the generator's folder; nothing opens the file dialog.</summary>
	public static void Command(CommandContext command)
	{
		string[] parts = command.Parts;
		if (parts.Length < 2)
		{
			Open(null);
			return;
		}
		string name = string.Join(" ", parts, 1, parts.Length - 1).Trim('"');
		string path = File.Exists(name) ? name : Path.Combine(Folder, name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? name : (name + Extension));
		if (!File.Exists(path))
		{
			ClientChat.Error("No tilemap called " + name + " in " + Folder);
			return;
		}
		Open(path);
	}

	/// <summary>Asks for a .tilemap (or takes <paramref name="path"/>) and arms the paste with it.</summary>
	public static void Open(string path)
	{
		if (Selection.OpeningTEDITSchematic)
		{
			ClientChat.Error("Already loading a schematic, please wait for the operation to finish.");
			return;
		}
		Task.Run(delegate
		{
			try
			{
				if (path == null)
				{
					path = FileBrowser.OpenFilePanel("Select a TileMap", new ExtensionFilter[1]
					{
						new ExtensionFilter("TileMap Files", "tilemap")
					});
				}
				if (string.IsNullOrEmpty(path))
				{
					return;
				}
				List<TileCopy> tiles = Read(path, out int width, out int height);
				PhotoImport.Cancel();
				Main.QueueMainThreadAction(delegate
				{
					BrushState.DisableBrushes();
					Selection.SelectionMode = false;
					Selection.CopyMode = false;
					PixelArt.DrawPhotoMode = false;
					Selection.CopiedTiles = tiles;
					Selection.SchematicSizeX = width;
					Selection.SchematicSizeY = height;
					Selection.PasteMode = true;
					Selection.PasteDelayCounter = Selection.PasteDelay;
					ClientChat.Info("Selected: " + path + "  |  Size: [X: " + width + ", Y: " + height + "]");
				});
			}
			catch (Exception ex)
			{
				ClientChat.Error("Could not load " + Path.GetFileName(path) + ": " + ex.Message);
				ClientLog.LogException(ex);
			}
		});
	}

	/// <summary>The tilemap as paste tiles (column by column, like the clipboard); one shared tile per combination.</summary>
	public static List<TileCopy> Read(string path, out int width, out int height)
	{
		using BinaryReader reader = new BinaryReader(File.OpenRead(path));
		if (reader.ReadByte() != 100)
		{
			throw new InvalidDataException("not a .tilemap file");
		}
		width = reader.ReadInt32();
		height = reader.ReadInt32();
		if (width <= 0 || height <= 0 || (long)width * height > 16L * 1024 * 1024)
		{
			throw new InvalidDataException("bad size " + width + "x" + height);
		}
		int count = width * height;
		ushort[] tiles = new ushort[count];
		ushort[] walls = new ushort[count];
		for (int i = 0; i < count; i++)
		{
			tiles[i] = reader.ReadUInt16();
		}
		for (int i = 0; i < count; i++)
		{
			walls[i] = reader.ReadUInt16();
		}
		byte[] paints = reader.ReadBytes(count);
		if (paints.Length != count)
		{
			throw new InvalidDataException("the file is cut short");
		}
		Dictionary<long, TileCopy> shared = new Dictionary<long, TileCopy>();
		List<TileCopy> list = new List<TileCopy>(count);
		for (int i = 0; i < count; i++)
		{
			long key = ((long)tiles[i] << 24) | ((long)walls[i] << 8) | paints[i];
			if (!shared.TryGetValue(key, out TileCopy tile))
			{
				tile = new TileCopy();
				if (tiles[i] != None && tiles[i] < Terraria.ID.TileID.Count)
				{
					tile.active(active: true);
					tile.type = tiles[i];
					tile.color(paints[i]);
					tile.frameX = 18;
					tile.frameY = 18;
				}
				if (walls[i] != None && walls[i] > 0 && walls[i] < Terraria.ID.WallID.Count)
				{
					tile.wall = walls[i];
					tile.wallColor(paints[i]);
				}
				shared[key] = tile;
			}
			list.Add(tile);
		}
		return list;
	}
}
