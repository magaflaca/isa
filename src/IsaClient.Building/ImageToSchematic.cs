using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Terraria;

namespace IsaClient.Building;

/// <summary>Turns a picture into a TEdit-style schematic of blocks and walls.</summary>
internal class ImageToSchematic
{
	public class TileWallData
	{
		public string Type { get; set; }

		public int Id { get; set; }

		public string Name { get; set; }

		public int Paint { get; set; }

		public string Color { get; set; }

		public bool BuildSafe { get; set; }
	}

	public static List<TileWallData> TileWallDataList { get; set; }

	public static Color[] Clrs { get; set; } = (Color[])(object)new Color[1];

	public static List<TileWallData> ClrsTileWallData { get; set; }

	public static async void ConvertPixelArt(PixelImage btm, bool buildSchematic = true)
	{
		try
		{
			await Task.Run(delegate
			{
				new List<Color>();
				Color val = Color.Lime;
				for (int i = 0; i < btm.Width; i++)
				{
					for (int j = 0; j < btm.Height; j++)
					{
						val = DirectColor(btm, i, j, includeTransparent: false);
						if (buildSchematic)
						{
							TileCopy legacyTile = new TileCopy();
							if (val.A < 25)
							{
								Selection.CopiedTiles.Add(legacyTile);
							}
							else
							{
								string finalHtmlColor = $"#{val.A:X2}{val.R:X2}{val.G:X2}{val.B:X2}";
								TileWallData tileWallData = ClrsTileWallData.FirstOrDefault((TileWallData t) => t.Color.Equals(finalHtmlColor, StringComparison.OrdinalIgnoreCase));
								if (tileWallData != null)
								{
									legacyTile.wall = (ushort)tileWallData.Id;
									legacyTile.wallColor((byte)tileWallData.Paint);
									Selection.CopiedTiles.Add(legacyTile);
								}
							}
						}
					}
				}
			});
		}
		catch (Exception)
		{
		}
		Selection.ConvertingToSchematic = false;
	}

	private static Color DirectColor(PixelImage bmp, int x, int y, bool includeTransparent)
	{
		PixelImage val = bmp.PeekPixels();
		byte[] pixelSpan = val.GetPixelSpan();
		int num = y * val.RowBytes + x * 4;
		Color lime = default(Color);
		lime = new Color((int)pixelSpan[num + 2], (int)pixelSpan[num + 1], (int)pixelSpan[num], (int)pixelSpan[num + 3]);
		if (includeTransparent || lime.A != 0)
		{
			if (includeTransparent && lime.A == 0)
			{
				lime = Color.Lime;
			}
			return Clr((Color[])(object)new Color[1] { lime });
		}
		return lime;
	}

	public static Color Clr(Color[] cs)
	{
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		for (int i = 0; i < cs.Length; i++)
		{
			num += cs[i].R;
			num2 += cs[i].G;
			num3 += cs[i].B;
		}
		num /= cs.Length;
		num2 /= cs.Length;
		num3 /= cs.Length;
		int num4 = 1000;
		int num5 = 0;
		for (int j = 0; j < Clrs.Length; j++)
		{
			int num6 = Clrs[j].R - num;
			int num7 = Clrs[j].G - num2;
			int num8 = Clrs[j].B - num3;
			if (num6 < 0)
			{
				num6 = -num6;
			}
			if (num7 < 0)
			{
				num7 = -num7;
			}
			if (num8 < 0)
			{
				num8 = -num8;
			}
			int num9 = num6 + num7 + num8;
			if (num9 < num4)
			{
				num5 = j;
				num4 = num9;
			}
		}
		return Clrs[num5];
	}

	public static async void BuildColorFilter()
	{
		TileWallDataList = new List<TileWallData>();
		using (Stream stream = FromEmbeddedPath("IsaClient.Assets.MapColors.xml"))
		{
			TileWallDataList = await ReadDataAsync(stream, null);
		}
		await Task.Run(delegate
		{
			bool useTiles = true;
			bool useWalls = true;
			bool buildSafe = true;
			useTiles = false;
			useWalls = true;
			buildSafe = true;
			List<TileWallData> list = TileWallDataList.Where((TileWallData data) => ((useTiles && data.Type == "Tile") || (useWalls && data.Type == "Wall")) && (!buildSafe || data.BuildSafe)).ToList();
			if (false)
			{
				list = (from data in list
					group data by data.Color into @group
					select @group.First()).ToList();
			}
			int num = 0;
			Clrs = (Color[])(object)new Color[list.Count];
			foreach (TileWallData item in list)
			{
				try
				{
					Clrs[num] = HexStringToMicrosoftColor_WithAlpha(item.Color);
				}
				catch
				{
					Clrs[num] = Color.Transparent;
				}
				num++;
			}
			ClrsTileWallData = list;
		});
	}

	private static Color HexStringToMicrosoftColor_WithAlpha(string hexColor)
	{
		string obj = (hexColor.Contains("#") ? hexColor.Replace("#", "") : hexColor);
		byte b = byte.Parse(obj.Substring(0, 2), NumberStyles.HexNumber);
		byte b2 = byte.Parse(obj.Substring(2, 2), NumberStyles.HexNumber);
		byte b3 = byte.Parse(obj.Substring(4, 2), NumberStyles.HexNumber);
		byte b4 = byte.Parse(obj.Substring(6, 2), NumberStyles.HexNumber);
		return new Color((int)b2, (int)b3, (int)b4, (int)b);
	}

	public static async Task<List<TileWallData>> ReadDataAsync(Stream stream, IProgress<int> progress)
	{
		List<TileWallData> dataList = new List<TileWallData>();
		List<XElement> list = (from e in (await Task.Run(() => XDocument.Load(stream))).Descendants()
			where e.Name == "Tile" || e.Name == "Wall"
			select e).ToList();
		int count = list.Count;
		for (int num = 0; num < count; num++)
		{
			XElement xElement = list[num];
			TileWallData item = new TileWallData
			{
				Type = xElement.Name.LocalName,
				Id = int.Parse(xElement.Attribute("Id").Value),
				Name = xElement.Attribute("Name").Value,
				Paint = int.Parse(xElement.Attribute("Paint").Value),
				Color = xElement.Attribute("Color").Value,
				BuildSafe = bool.Parse(xElement.Attribute("BuildSafe").Value)
			};
			dataList.Add(item);
			progress?.Report((num + 1) * 100 / count);
		}
		return dataList;
	}

	public static Stream FromEmbeddedPath(string path)
	{
		return Assembly.GetExecutingAssembly().GetManifestResourceStream(path);
	}
}
