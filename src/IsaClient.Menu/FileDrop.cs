using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using IsaClient.Building;
using IsaClient.Core;

namespace IsaClient.Menu;

/// <summary>Files dropped on the game window: pictures for pixel art, .plr characters and .gba / .nds / PSP games.</summary>
public static class FileDrop
{
	private static bool _dropTargetRegistered;

	private static readonly string[] _imageExtensions = IsaClient.Building.PhotoImport.Extensions;

	/// <summary>Registers the game window as a drop target (main thread, once the window exists).</summary>
	internal static void EnsureDropTarget()
	{
		if (_dropTargetRegistered || Main.instance == null)
		{
			return;
		}
		_dropTargetRegistered = true;
		try
		{
			if (Control.FromHandle(Main.instance.Window.Handle) is Form form)
			{
				form.AllowDrop = true;
				form.DragEnter += delegate(object sender, DragEventArgs e)
				{
					e.Effect = (e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None);
				};
				form.DragDrop += delegate(object sender, DragEventArgs e)
				{
					if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length != 0)
					{
						string file = files[0];
						Main.QueueMainThreadAction(delegate
						{
							// a .zip can be any console's: what's inside says (the look is quick, it's only its list)
							string[] psp = files.Where(IsPspFile).ToArray();
							string[] roms = files.Where(f => IsGbaFile(f) && !psp.Contains(f)).ToArray();
							if (psp.Length != 0)
							{
								IsaClient.Psp.PspWindow.AddDroppedGames(psp);
								SoundEngine.PlaySound(10);
							}
							if (roms.Length != 0)
							{
								OnGamesDropped(roms);
							}
							else if (psp.Length == 0)
							{
								OnFileDropped(file);
							}
						});
					}
				};
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static bool IsPspFile(string path)
	{
		if (Directory.Exists(path))
		{
			return File.Exists(Path.Combine(path, "EBOOT.PBP"));
		}
		string ext = Path.GetExtension(path).ToLowerInvariant();
		return IsaClient.Psp.PspLibrary.Accepts(path) || (ext == ".zip" && IsaClient.Psp.PspLibrary.ZipHasGame(path));
	}

	private static bool IsGbaFile(string path)
	{
		string ext = Path.GetExtension(path).ToLowerInvariant();
		return ext == ".gba" || ext == ".nds" || ext == ".zip";
	}

	/// <summary>Game Boy Advance and DS games (.gba, .nds, or a .zip with them): into the library, Games page opened.</summary>
	private static void OnGamesDropped(string[] files)
	{
		if (IsaClient.Gba.PokemonWindow.AddDroppedGames(files))
		{
			SoundEngine.PlaySound(10);
			return;
		}
		if (!Main.gameMenu)
		{
			ClientChat.Error(IsaClient.Gba.PokemonGame.Problem ?? "That isn't a Game Boy Advance or DS game.");
		}
	}

	private static void OnFileDropped(string path)
	{
		string ext = Path.GetExtension(path).ToLowerInvariant();
		if (ext == ".plr")
		{
			PlayerFileDropDialog.Open(path);
			return;
		}
		if (ext == ".wld")
		{
			PlayerFileDropDialog.OpenWorld(path);
			return;
		}
		if (ext == IsaClient.Building.TileMapFiles.Extension && !Main.gameMenu)
		{
			IsaClient.Building.TileMapFiles.Open(path);
			return;
		}
		bool isSchematic = ext == ".teditsch";
		bool isImage = _imageExtensions.Contains(ext);
		if (!isSchematic && !isImage)
		{
			if (!Main.gameMenu)
			{
				ClientChat.Error("Drop a .plr character, a .wld world, a .TEditSch schematic, a .gba / .nds / PSP game or an image (png, jpg, webp, bmp, gif...).");
			}
			return;
		}
		if (Main.gameMenu)
		{
			SoundEngine.PlaySound(12);
			return;
		}
		SoundEngine.PlaySound(10);
		ClientChat.Info("Loaded " + Path.GetFileName(path) + " from drag & drop.");
		if (isSchematic)
		{
			SchematicFiles.ForcedDialogPath = path;
			SchematicFiles.Load();
		}
		else
		{
			PixelArt.DrawPhotoMode = false;
			SchematicFiles.ForcedDialogPath = path;
			PixelArt.LoadPhotosFromFile();
		}
	}
}
