using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using IsaClient.Gameplay;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Utilities;
using IsaClient.Core;
using IsaClient.Movement;
using IsaClient.Networking;
using System;
using System.IO;
using Terraria.Utilities.FileBrowser;

namespace IsaClient.Building;

/// <summary>Pixel art: draws images and player portraits into the world as blocks (.drawphoto, .plrart, .pixelart).</summary>
public static class PixelArt
{
	[Setting("DrawPhotoScale")]
	public static float DrawPhotoScale = 100f;

	/// <summary>
	/// The most blocks per side a picture becomes; bigger ones are fitted. Every block of a paste is remembered for
	/// undo and drawn in the preview, so this keeps a huge photo from eating the game's memory.
	/// </summary>
	[Setting("DrawPhotoMaxSize", Min = 16, Max = 4096)]
	public static int DrawPhotoMaxSize = 2048;

	/// <summary>.plrart &lt;player|index|me&gt;: paste that character as pixel art made of real blocks.</summary>
	internal static void PixelPlayerCommand(CommandContext command)
	{
		string[] array = command.Parts;
		string query = (array.Length > 1) ? string.Join(" ", array, 1, array.Length - 1) : "me";
		Player target = CharacterPresets.FindOnline(query);
		if (target == null)
		{
			ClientChat.Error("No player matches '" + query + "'.");
			return;
		}
		CharacterPresets.RequestPixelArt(target, target.name);
		ClientChat.Info("Building " + target.name + " (" + CharacterPresets.ArtOptionsText() + ")...");
	}

	/// <summary>.pixelart [size 1-4] [walk on|off] [glow on|off]</summary>
	internal static void PixelArtCommand(CommandContext command)
	{
		string[] array = command.Parts;
		for (int i = 1; i < array.Length; i++)
		{
			string key = array[i].ToLowerInvariant();
			string value = (i + 1 < array.Length) ? array[i + 1].ToLowerInvariant() : "";
			bool? flag = (value == "on" || value == "yes" || value == "1") ? true : ((value == "off" || value == "no" || value == "0") ? false : null);
			switch (key)
			{
			case "size":
			case "scale":
				if (int.TryParse(value, out int size))
				{
					CharacterPresets.ArtScale = Utils.Clamp(size, 1, 4);
					i++;
				}
				break;
			case "walk":
			case "walkthrough":
			case "actuated":
				CharacterPresets.ArtWalkThrough = flag ?? !CharacterPresets.ArtWalkThrough;
				if (flag.HasValue)
				{
					i++;
				}
				break;
			case "glow":
				CharacterPresets.ArtGlow = flag ?? !CharacterPresets.ArtGlow;
				if (flag.HasValue)
				{
					i++;
				}
				break;
			}
		}
		ClientSettings.Save();
		ClientChat.Info("Pixel art: " + CharacterPresets.ArtOptionsText() + "   (.pixelart size <1-4> walk on|off glow on|off)");
	}

	public static bool DrawPhotoMode;

	public static int DrawPhotoWidth;

	public static int DrawPhotoHeight;

	/// <summary>What pictures are made of: 0 blocks, 1 walls (seamless in the world), 2 both (twice as many colors on the map).</summary>
	[Setting("DrawPhotoType", Min = 0, Max = 2)]
	public static int DrawPhotoType = 2;

	public static List<ColorMatching.WallColorTable> WallColors = new List<ColorMatching.WallColorTable>();

	public static List<Color> PixelColors = new List<Color>();

	public static List<string> PixelColorsInHex = new List<string>();

	public static Color[] HexValues = (Color[])(object)new Color[339]
		{
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("583D2E"),
			ColorMatching.FromHex("3D3A4E"),
			ColorMatching.FromHex("493324"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("5B1E1E"),
			ColorMatching.FromHex("1B1F2A"),
			ColorMatching.FromHex("1F271A"),
			ColorMatching.FromHex("291C24"),
			ColorMatching.FromHex("4A3E0C"),
			ColorMatching.FromHex("2E383B"),
			ColorMatching.FromHex("4B200B"),
			ColorMatching.FromHex("432525"),
			ColorMatching.FromHex("0F0F0F"),
			ColorMatching.FromHex("342B2D"),
			ColorMatching.FromHex("583D2E"),
			ColorMatching.FromHex("1B1F2A"),
			ColorMatching.FromHex("1F271A"),
			ColorMatching.FromHex("291C24"),
			ColorMatching.FromHex("0F0F0F"),
			ColorMatching.FromHex("7792FF"),
			ColorMatching.FromHex("716363"),
			ColorMatching.FromHex("26262B"),
			ColorMatching.FromHex("352729"),
			ColorMatching.FromHex("0B233E"),
			ColorMatching.FromHex("153F46"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("515465"),
			ColorMatching.FromHex("581717"),
			ColorMatching.FromHex("1C5817"),
			ColorMatching.FromHex("4E5763"),
			ColorMatching.FromHex("561128"),
			ColorMatching.FromHex("312F53"),
			ColorMatching.FromHex("454329"),
			ColorMatching.FromHex("333346"),
			ColorMatching.FromHex("573B37"),
			ColorMatching.FromHex("454329"),
			ColorMatching.FromHex("313931"),
			ColorMatching.FromHex("4E4F49"),
			ColorMatching.FromHex("556667"),
			ColorMatching.FromHex("34323E"),
			ColorMatching.FromHex("472A2C"),
			ColorMatching.FromHex("494232"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("3C3B33"),
			ColorMatching.FromHex("30392F"),
			ColorMatching.FromHex("474D55"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("313024"),
			ColorMatching.FromHex("2B2120"),
			ColorMatching.FromHex("1F2831"),
			ColorMatching.FromHex("302334"),
			ColorMatching.FromHex("583D2E"),
			ColorMatching.FromHex("013414"),
			ColorMatching.FromHex("37271A"),
			ColorMatching.FromHex("27211A"),
			ColorMatching.FromHex("1E5030"),
			ColorMatching.FromHex("35501E"),
			ColorMatching.FromHex("1E5030"),
			ColorMatching.FromHex("1E5030"),
			ColorMatching.FromHex("35501E"),
			ColorMatching.FromHex("1E5030"),
			ColorMatching.FromHex("2B2A44"),
			ColorMatching.FromHex("1E4650"),
			ColorMatching.FromHex("4E6987"),
			ColorMatching.FromHex("34540C"),
			ColorMatching.FromHex("BECCDF"),
			ColorMatching.FromHex("403E50"),
			ColorMatching.FromHex("414123"),
			ColorMatching.FromHex("142E68"),
			ColorMatching.FromHex("3D0D10"),
			ColorMatching.FromHex("3F271A"),
			ColorMatching.FromHex("332F60"),
			ColorMatching.FromHex("403E50"),
			ColorMatching.FromHex("653333"),
			ColorMatching.FromHex("4D4022"),
			ColorMatching.FromHex("3E2629"),
			ColorMatching.FromHex("304E5D"),
			ColorMatching.FromHex("363F45"),
			ColorMatching.FromHex("8A4926"),
			ColorMatching.FromHex("320F08"),
			ColorMatching.FromHex("5F6DFF"),
			ColorMatching.FromHex("6272FF"),
			ColorMatching.FromHex("6576FF"),
			ColorMatching.FromHex("687BFF"),
			ColorMatching.FromHex("6B80FF"),
			ColorMatching.FromHex("6E84FF"),
			ColorMatching.FromHex("20282D"),
			ColorMatching.FromHex("2C2932"),
			ColorMatching.FromHex("48324D"),
			ColorMatching.FromHex("4E3245"),
			ColorMatching.FromHex("242D2C"),
			ColorMatching.FromHex("263132"),
			ColorMatching.FromHex("20282D"),
			ColorMatching.FromHex("2C2932"),
			ColorMatching.FromHex("48324D"),
			ColorMatching.FromHex("4E3245"),
			ColorMatching.FromHex("242D2C"),
			ColorMatching.FromHex("263132"),
			ColorMatching.FromHex("4A4DFF"),
			ColorMatching.FromHex("4D51FF"),
			ColorMatching.FromHex("8A4926"),
			ColorMatching.FromHex("5E1911"),
			ColorMatching.FromHex("7D247A"),
			ColorMatching.FromHex("33231B"),
			ColorMatching.FromHex("320F08"),
			ColorMatching.FromHex("873A00"),
			ColorMatching.FromHex("41340F"),
			ColorMatching.FromHex("272A33"),
			ColorMatching.FromHex("591A1B"),
			ColorMatching.FromHex("7E7B73"),
			ColorMatching.FromHex("083213"),
			ColorMatching.FromHex("5F1518"),
			ColorMatching.FromHex("111F41"),
			ColorMatching.FromHex("C0AD8F"),
			ColorMatching.FromHex("727283"),
			ColorMatching.FromHex("887707"),
			ColorMatching.FromHex("084803"),
			ColorMatching.FromHex("758452"),
			ColorMatching.FromHex("646672"),
			ColorMatching.FromHex("1E76E2"),
			ColorMatching.FromHex("5D0666"),
			ColorMatching.FromHex("4028A9"),
			ColorMatching.FromHex("2722B4"),
			ColorMatching.FromHex("575E7D"),
			ColorMatching.FromHex("060606"),
			ColorMatching.FromHex("4548BA"),
			ColorMatching.FromHex("823E10"),
			ColorMatching.FromHex("167BA3"),
			ColorMatching.FromHex("285697"),
			ColorMatching.FromHex("B74B0F"),
			ColorMatching.FromHex("535064"),
			ColorMatching.FromHex("734144"),
			ColorMatching.FromHex("776C51"),
			ColorMatching.FromHex("3B4347"),
			ColorMatching.FromHex("DED8CA"),
			ColorMatching.FromHex("5A7069"),
			ColorMatching.FromHex("3E1C57"),
			ColorMatching.FromHex("748DFF"),
			ColorMatching.FromHex("783B13"),
			ColorMatching.FromHex("3B3B3B"),
			ColorMatching.FromHex("E5DAA1"),
			ColorMatching.FromHex("493B32"),
			ColorMatching.FromHex("84A5FF"),
			ColorMatching.FromHex("664B22"),
			ColorMatching.FromHex("3E3AFF"),
			ColorMatching.FromHex("FF914F"),
			ColorMatching.FromHex("DD4FFF"),
			ColorMatching.FromHex("F0F0F7"),
			ColorMatching.FromHex("4FFF59"),
			ColorMatching.FromHex("9A5331"),
			ColorMatching.FromHex("6B319A"),
			ColorMatching.FromHex("555976"),
			ColorMatching.FromHex("319A44"),
			ColorMatching.FromHex("9A314D"),
			ColorMatching.FromHex("31319A"),
			ColorMatching.FromHex("9A9431"),
			ColorMatching.FromHex("FF4F4F"),
			ColorMatching.FromHex("4F66FF"),
			ColorMatching.FromHex("FAFF4F"),
			ColorMatching.FromHex("464433"),
			ColorMatching.FromHex("6E84FF"),
			ColorMatching.FromHex("050505"),
			ColorMatching.FromHex("3B2716"),
			ColorMatching.FromHex("3B2716"),
			ColorMatching.FromHex("A36000"),
			ColorMatching.FromHex("5EA32E"),
			ColorMatching.FromHex("75203B"),
			ColorMatching.FromHex("140BCB"),
			ColorMatching.FromHex("4A4558"),
			ColorMatching.FromHex("3C1E1E"),
			ColorMatching.FromHex("6F7587"),
			ColorMatching.FromHex("6F7587"),
			ColorMatching.FromHex("191736"),
			ColorMatching.FromHex("191736"),
			ColorMatching.FromHex("4A4781"),
			ColorMatching.FromHex("6F7587"),
			ColorMatching.FromHex("191736"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("260942"),
			ColorMatching.FromHex("955033"),
			ColorMatching.FromHex("523F50"),
			ColorMatching.FromHex("413D4D"),
			ColorMatching.FromHex("40415C"),
			ColorMatching.FromHex("4C3554"),
			ColorMatching.FromHex("904334"),
			ColorMatching.FromHex("953030"),
			ColorMatching.FromHex("6F2024"),
			ColorMatching.FromHex("933037"),
			ColorMatching.FromHex("614333"),
			ColorMatching.FromHex("70503E"),
			ColorMatching.FromHex("583D2E"),
			ColorMatching.FromHex("7F5E4C"),
			ColorMatching.FromHex("8F327B"),
			ColorMatching.FromHex("887883"),
			ColorMatching.FromHex("DB5C8F"),
			ColorMatching.FromHex("714096"),
			ColorMatching.FromHex("4A433C"),
			ColorMatching.FromHex("3C4E3B"),
			ColorMatching.FromHex("003615"),
			ColorMatching.FromHex("4A6148"),
			ColorMatching.FromHex("282523"),
			ColorMatching.FromHex("4D3F42"),
			ColorMatching.FromHex("6F0606"),
			ColorMatching.FromHex("58433B"),
			ColorMatching.FromHex("585750"),
			ColorMatching.FromHex("474743"),
			ColorMatching.FromHex("4C343C"),
			ColorMatching.FromHex("59303B"),
			ColorMatching.FromHex("9E6440"),
			ColorMatching.FromHex("3E2D4B"),
			ColorMatching.FromHex("390E0C"),
			ColorMatching.FromHex("604885"),
			ColorMatching.FromHex("433750"),
			ColorMatching.FromHex("40251D"),
			ColorMatching.FromHex("46335B"),
			ColorMatching.FromHex("331204"),
			ColorMatching.FromHex("393734"),
			ColorMatching.FromHex("444444"),
			ColorMatching.FromHex("948A4A"),
			ColorMatching.FromHex("5F89BF"),
			ColorMatching.FromHex("A0024B"),
			ColorMatching.FromHex("6437A4"),
			ColorMatching.FromHex("007565"),
			ColorMatching.FromHex("6E5A4E"),
			ColorMatching.FromHex("2F454B"),
			ColorMatching.FromHex("5B4346"),
			ColorMatching.FromHex("3C2427"),
			ColorMatching.FromHex("8C4B30"),
			ColorMatching.FromHex("7F312C"),
			ColorMatching.FromHex("C82C12"),
			ColorMatching.FromHex("185D42"),
			ColorMatching.FromHex("A057EA"),
			ColorMatching.FromHex("066AFF"),
			ColorMatching.FromHex("687BFF"),
			ColorMatching.FromHex("050505"),
			ColorMatching.FromHex("050505"),
			ColorMatching.FromHex("3F271A"),
			ColorMatching.FromHex("666666"),
			ColorMatching.FromHex("3D3A4E"),
			ColorMatching.FromHex("342B2D"),
			ColorMatching.FromHex("515465"),
			ColorMatching.FromHex("556667"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("283832"),
			ColorMatching.FromHex("313024"),
			ColorMatching.FromHex("2B2120"),
			ColorMatching.FromHex("1F2831"),
			ColorMatching.FromHex("302334"),
			ColorMatching.FromHex("583D2E"),
			ColorMatching.FromHex("37271A"),
			ColorMatching.FromHex("27211A"),
			ColorMatching.FromHex("2B2A44"),
			ColorMatching.FromHex("1E4650"),
			ColorMatching.FromHex("4E6987"),
			ColorMatching.FromHex("332F60"),
			ColorMatching.FromHex("653333"),
			ColorMatching.FromHex("3E2629"),
			ColorMatching.FromHex("3B2716"),
			ColorMatching.FromHex("3B2716"),
			ColorMatching.FromHex("6F7587"),
			ColorMatching.FromHex("191736"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("955033"),
			ColorMatching.FromHex("523F50"),
			ColorMatching.FromHex("413D4D"),
			ColorMatching.FromHex("40415C"),
			ColorMatching.FromHex("4C3554"),
			ColorMatching.FromHex("904334"),
			ColorMatching.FromHex("953030"),
			ColorMatching.FromHex("6F2024"),
			ColorMatching.FromHex("933037"),
			ColorMatching.FromHex("614333"),
			ColorMatching.FromHex("70503E"),
			ColorMatching.FromHex("583D2E"),
			ColorMatching.FromHex("7F5E4C"),
			ColorMatching.FromHex("8F327B"),
			ColorMatching.FromHex("887883"),
			ColorMatching.FromHex("DB5C8F"),
			ColorMatching.FromHex("714096"),
			ColorMatching.FromHex("4A433C"),
			ColorMatching.FromHex("3C4E3B"),
			ColorMatching.FromHex("003615"),
			ColorMatching.FromHex("4A6148"),
			ColorMatching.FromHex("282523"),
			ColorMatching.FromHex("4D3F42"),
			ColorMatching.FromHex("6F0606"),
			ColorMatching.FromHex("58433B"),
			ColorMatching.FromHex("585750"),
			ColorMatching.FromHex("474743"),
			ColorMatching.FromHex("4C343C"),
			ColorMatching.FromHex("59303B"),
			ColorMatching.FromHex("9E6440"),
			ColorMatching.FromHex("3E2D4B"),
			ColorMatching.FromHex("390E0C"),
			ColorMatching.FromHex("604885"),
			ColorMatching.FromHex("433750"),
			ColorMatching.FromHex("40251D"),
			ColorMatching.FromHex("46335B"),
			ColorMatching.FromHex("331204"),
			ColorMatching.FromHex("4E6E33"),
			ColorMatching.FromHex("4E6E33"),
			ColorMatching.FromHex("343434"),
			ColorMatching.FromHex("413FFF"),
			ColorMatching.FromHex("493324"),
			ColorMatching.FromHex("801A34"),
			ColorMatching.FromHex("897843"),
			ColorMatching.FromHex("676767"),
			ColorMatching.FromHex("DB4726"),
			ColorMatching.FromHex("FD3E03"),
			ColorMatching.FromHex("60F802"),
			ColorMatching.FromHex("694ACA"),
			ColorMatching.FromHex("1DF0FF"),
			ColorMatching.FromHex("FECA50"),
			ColorMatching.FromHex("83FCF5"),
			ColorMatching.FromHex("1F1F1F"),
			ColorMatching.FromHex("1CD85E"),
			ColorMatching.FromHex("9288CD"),
			ColorMatching.FromHex("DFE8E9"),
			ColorMatching.FromHex("FBD1F0"),
			ColorMatching.FromHex("5BBAF0"),
			ColorMatching.FromHex("5CF05B"),
			ColorMatching.FromHex("F05B93"),
			ColorMatching.FromHex("949EB8"),
			ColorMatching.FromHex("4C392C"),
			ColorMatching.FromHex("7D3D41"),
			ColorMatching.FromHex("1E1A54"),
			ColorMatching.FromHex("B2683A"),
			ColorMatching.FromHex("AC9B6E")
		};

	[Old("Deprecated.")]
	public async static void DrawPhotoV2(Point mouseToTile)
	{
		await Task.Run(delegate
		{
			int num = 0;
			for (int i = mouseToTile.X; i < mouseToTile.X + DrawPhotoWidth; i++)
			{
				_ = NetMessage.buffer[256].spamCount;
				for (int j = mouseToTile.Y; j < mouseToTile.Y + DrawPhotoHeight; j++)
				{
					if (WorldGen.InWorld(i, j))
					{
						int num2 = ColorMatching.ClosestBlockColor(num);
						bool flag = false;
						Tile tile = Framing.GetTileSafely(i, j);
						if (!Main.sectionManager.SectionLoaded(Netplay.GetSectionX(i), Netplay.GetSectionY(j)))
						{
							ClientChat.Info("Tiles are not loaded, reveal map.");
							return;
						}
						tile.ClearTile();
						if (!tile.active())
						{
							if (j % 15 == 0)
							{
								Thread.Sleep(20);
							}
							if (ColorMatching.IsNonSolidPixelTile(num2))
							{
								tile.active(active: true);
								tile.type = (ushort)num2;
								WorldGen.SquareTileFrame(i, j);
							}
							else
							{
								tile.wall = (ushort)num2;
								WorldGen.SquareWallFrame(i, j);
							}
							flag = true;
							if (Main.netMode == 1)
							{
								if (!ColorMatching.IsNonSolidPixelTile(num2))
								{
									num2 = Placement.ConvertUnsafeWall((ushort)num2);
								}
								TeleportTools.FakeNetMessage13(new Vector2((float)((i + 2) * 16), (float)(j * 16)), Main.LocalPlayer.selectedItem);
								try
								{
									RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, Main.LocalPlayer.selectedItem, 0f, ColorMatching.IsNonSolidPixelTile(num2) ? ((float)TileItemLookup.NonSolidTileIDToItemID[num2]) : ((float)TileItemLookup.WallIDToItemID[num2]));
								}
								catch
								{
								}
								NetMessage.SendData(17, -1, -1, null, (num2 >= 313) ? 1 : 3, i, j, num2);
							}
						}
						if (!PacketPacing.TilePacketMaxMode)
						{
							if ((Main.netMode == 1 && num % PacketPacing.TilePacketRate == 0) || Performance.LowFpsGuard)
							{
								Thread.Sleep(100);
							}
						}
						else
						{
							while ((Main.netMode == 1 && (PacketPacing.QueuedPackets > PacketPacing.TilePacketRate || PacketPacing.ServerSilentGuard)) || Performance.LowFpsGuard)
							{
								Thread.Sleep(500);
							}
						}
						if (flag)
						{
							Main.Map.UpdateLighting(i, j, byte.MaxValue);
							Main.refreshMap = true;
						}
						num++;
					}
				}
			}
			ClientChat.Info("Drawing Complete.");
		});
	}

	#region Chat commands

	/// <summary>.picscale / .drawscale / .imagescale [percent | max &lt;blocks&gt;]</summary>
	internal static void ImageScaleCommand(CommandContext command)
	{
		if (command.Parts.Length > 1 && command.Parts[1].Equals("max", StringComparison.OrdinalIgnoreCase))
		{
			if (command.Parts.Length > 2 && int.TryParse(command.Parts[2], out int max))
			{
				DrawPhotoMaxSize = Utils.Clamp(max, 16, 4096);
				ClientSettings.Save();
			}
			ClientChat.Info("Biggest picture for .drawpic: " + DrawPhotoMaxSize + " blocks per side (bigger ones are fitted). .picscale max <16-4096>");
			return;
		}
		if (command.Parts.Length > 1 && float.TryParse(command.Parts[1].Replace(',', '.').TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
		{
			DrawPhotoScale = MathHelper.Clamp(scale, 0.1f, 100f);
		}
		ClientChat.Info("Image scale for .drawpic: " + DrawPhotoScale.ToString("0.##", CultureInfo.InvariantCulture) + "%");
	}

	/// <summary>.dmode</summary>
	internal static void DrawModeCommand(CommandContext command)
	{
		if (command.Parts.Length > 1 && int.TryParse(command.Parts[1], out var result18) && result18 >= 0 && result18 < 3)
		{
			DrawPhotoType = result18;
			ClientChat.Info($"Draw photo mode set to {DrawPhotoType}.");
		}
		else
		{
			ClientChat.Info($"Current draw photo mode: {DrawPhotoType} (0 = blocks, 1 = walls, 2 = blocks and walls). More options: Building > Picture to Blocks.");
		}
	}

	#endregion

	/// <summary>.drawpic, the World Edit button and dropped pictures: see <see cref="PhotoImport"/>.</summary>
	public static void LoadPhotosFromFile()
	{
		DrawPhotoMode = false;
		string path = SchematicFiles.ForcedDialogPath;
		SchematicFiles.ForcedDialogPath = null;
		PhotoImport.Start(path);
	}
}
