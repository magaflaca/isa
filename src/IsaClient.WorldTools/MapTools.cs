using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;
using Terraria.Map;
using XnaColor = Microsoft.Xna.Framework.Color;
using IsaClient.Core;

namespace IsaClient.WorldTools;

/// <summary>Map: reveal, refresh, sections and export as an image.</summary>
public static class MapTools
{
	/// <summary>.savemap: saves the whole map as a PNG in Terraria/Exported Maps (what you have revealed online).</summary>
	public static void SaveMapImage()
	{
		if (string.IsNullOrEmpty(Main.worldName) || Main.ActiveWorldFileData == null)
		{
			ClientChat.Error("There is no world data available, or the world name is blank.");
			return;
		}
		if (MapHelper.colorLookup == null)
		{
			MapHelper.Initialize();
		}
		if (SavingMapImage)
		{
			ClientChat.Error("Already saving the map.");
			return;
		}
		SavingMapImage = true;
		ClientChat.Info("Saving image of the world...");
		Task.Run(delegate
		{
			try
			{
				int width = Main.maxTilesX;
				int height = Main.maxTilesY;
				byte[] pixels = new byte[width * height * 4];
				bool online = Main.netMode == 1;
				for (int y = 0; y < height; y++)
				{
					int row = y * width * 4;
					for (int x = 0; x < width; x++)
					{
						XnaColor color;
						if (online)
						{
							MapTile known = Main.Map[x, y];
							color = (known.Light > 0) ? MapHelper.GetMapTileXnaColor(known, x, y) : XnaColor.Black;
						}
						else
						{
							color = MapHelper.GetMapTileXnaColor(MapHelper.CreateMapTile(x, y, byte.MaxValue), x, y);
						}
						int k = row + x * 4;
						pixels[k] = color.B;
						pixels[k + 1] = color.G;
						pixels[k + 2] = color.R;
						pixels[k + 3] = byte.MaxValue;
					}
				}
				string folder = Path.Combine(Main.SavePath, "Exported Maps");
				Directory.CreateDirectory(folder);
				char[] invalid = Path.GetInvalidFileNameChars();
				string name = new string(Main.worldName.Select((char c) => (invalid.Contains(c) || c == ' ' || c == '.' || c == '*') ? '-' : c).ToArray());
				string fileName = name + "-" + Main.ActiveWorldFileData.UniqueId + ".png";
				using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
				{
					BitmapData data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
					try
					{
						for (int y = 0; y < height; y++)
						{
							Marshal.Copy(pixels, y * width * 4, data.Scan0 + y * data.Stride, width * 4);
						}
					}
					finally
					{
						bitmap.UnlockBits(data);
					}
					bitmap.Save(Path.Combine(folder, fileName), ImageFormat.Png);
				}
				ClientChat.Info("Image of the world saved as " + fileName + " in Terraria/Exported Maps.");
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
				ClientChat.Error("Map saving failed: " + ex.Message);
			}
			finally
			{
				SavingMapImage = false;
			}
		});
	}

	public static bool LoadingChunks;

	public static bool LoadingMap = false;

	public static int QueuedMapRefreshTime = 0;

	public static bool RevealMapCooldown;

	public static bool DownloadingWorld;

	public static bool SavingMapImage;

	public static int DetermineUnloadedSections
	{
		get
		{
			int num = 0;
			for (int i = 0; i < Main.maxSectionsX; i++)
			{
				for (int j = 0; j < Main.maxSectionsY; j++)
				{
					if (!Main.sectionManager.SectionLoaded(i, j))
					{
						num++;
					}
				}
			}
			return num;
		}
	}

	public static void LoadChunk(int x, int y)
	{
		if (!Main.gameMenu)
		{
			Vector2 position = default(Vector2);
			position = new Vector2((float)(x * 200) * 16f + 8f, (float)(y * 150) * 16f + 8f);
			new Projectile();
			Projectile obj = Main.projectile[Projectile.NewProjectile(Main.LocalPlayer.GetProjectileSource_Misc(1020), position, new Vector2(1f, 1f), 1020, 0, 0f, Main.myPlayer)];
			obj.velocity = Vector2.Zero;
			obj.timeLeft = 4;
			obj.miscText = "IsaClient Map Revealing Projectile";
		}
	}

	public static async void UpdateAndRefreshMap()
	{
		await Task.Run(delegate
		{
			for (int i = 0; i < Main.maxTilesX; i++)
			{
				for (int j = 0; j < Main.maxTilesY; j++)
				{
					if (WorldGen.InWorld(i, j, 5))
					{
						Main.Map.Update(i, j, byte.MaxValue);
					}
				}
			}
			Main.refreshMap = true;
			ClientChat.Info("Map Revealed");
		});
	}

	public static bool WorldDownload;

	public static bool StopMapReveal;

	/// <summary>Panel/command entry for Reveal Map: the full world in singleplayer, section download online.</summary>
	public static void RevealTheMap()
	{
		if (Main.gameMenu)
		{
			return;
		}
		MapReveal.RevealMapFromUI();
	}

	#region Chat commands

	/// <summary>.savemap / .exportmap</summary>
	internal static void ExportMapCommand(CommandContext command)
	{
		SaveMapImage();
	}

	#endregion
}
