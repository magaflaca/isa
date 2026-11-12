using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.States;
using Terraria.IO;
using IsaClient.Core;
using IsaClient.Gameplay;
using Terraria;

namespace IsaClient.Menu;

/// <summary>
/// Shown when a .plr file is dropped on the game window: what to do with that character. In a world: wear it (you can
/// restore yours later), paste it as pixel art, save it to your characters, or nothing. In the main menu only saving
/// makes sense. A dropped .wld world gets the same kind of dialog to add it to your worlds (menu or in game).
/// Modal: while it is open the rest of the game ignores the mouse.
/// </summary>
public static class PlayerFileDropDialog
{
	private static Player _player;

	private static Player _preview;

	private static string _path;

	private static bool _blockedInput;

	private static bool _stashed;

	private static bool _mouseLeft;

	private static bool _mouseLeftRelease;

	private static WorldFileData _world;

	private static string _worldPath;

	private static string _worldSavedAs;

	public static bool IsOpen => _player != null || _world != null || IsaClient.Sharing.ShareDialog.IsOpen || IsaClient.Sharing.FileShareDialog.IsOpen;

	public static void OpenWorld(string path)
	{
		WorldFileData data = null;
		try
		{
			data = WorldFile.GetAllMetadata(path, cloudSave: false);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		if (data == null || !data.IsValid)
		{
			if (!Main.gameMenu)
			{
				ClientChat.Error(Path.GetFileName(path) + " is not a Terraria world this version can read.");
			}
			SoundEngine.PlaySound(11);
			return;
		}
		Close();
		_world = data;
		_worldPath = path;
		_worldSavedAs = null;
		SoundEngine.PlaySound(10);
	}

	public static void Open(string path)
	{
		IsaClient.Sharing.ShareDialog.Close();
		IsaClient.Sharing.FileShareDialog.Close();
		Player player = CharacterPresets.LoadFile(path, out string error);
		if (player == null)
		{
			if (!Main.gameMenu)
			{
				ClientChat.Error(error);
			}
			SoundEngine.PlaySound(11);
			return;
		}
		_world = null;
		_worldPath = null;
		_worldSavedAs = null;
		_path = path;
		_player = player;
		_preview = player.SafeSerializedClone();
		SoundEngine.PlaySound(10);
		CharacterWindow.AddDropped(path, player);
	}

	public static void Close()
	{
		_player = null;
		_preview = null;
		_path = null;
		_world = null;
		_worldPath = null;
		_worldSavedAs = null;
		if (_blockedInput)
		{
			Main.blockInput = false;
			_blockedInput = false;
		}
	}

	/// <summary>Called before the game's interface draws: nothing under the dialog may see the clicks.</summary>
	public static void BeforeInterface()
	{
		_stashed = false;
		if (!IsOpen)
		{
			// A window closed by its own button (the style, schematic and file windows): the game gets its keys back.
			if (_blockedInput)
			{
				Main.blockInput = false;
				_blockedInput = false;
			}
			return;
		}
		_stashed = true;
		_mouseLeft = Main.mouseLeft;
		_mouseLeftRelease = Main.mouseLeftRelease;
		Main.mouseLeft = false;
		Main.mouseLeftRelease = false;
		if (!Main.gameMenu && !Main.blockInput)
		{
			Main.blockInput = true;
			_blockedInput = true;
		}
	}

	public static void Draw(SpriteBatch sb)
	{
		if (!IsOpen)
		{
			return;
		}
		if (_stashed)
		{
			Main.mouseLeft = _mouseLeft;
			Main.mouseLeftRelease = _mouseLeftRelease;
			_stashed = false;
		}
		Main.LocalPlayer.mouseInterface = true;
		bool inWorld = !Main.gameMenu;
		// The scale the interface is really drawn at (the game lowers it when the window is small), so the shade
		// covers the whole window and the dialog sits in its middle.
		float ui = (Main.UIScaleMatrix.M11 > 0.01f) ? Main.UIScaleMatrix.M11 : Main.UIScale;
		// The real drawing size when the game's own numbers lag behind it (a restored or resized window).
		Microsoft.Xna.Framework.Graphics.PresentationParameters pp = Main.instance.GraphicsDevice.PresentationParameters;
		int screenW = (int)Math.Ceiling((float)Math.Max(Main.screenWidth, pp.BackBufferWidth) / ui);
		int screenH = (int)Math.Ceiling((float)Math.Max(Main.screenHeight, pp.BackBufferHeight) / ui);
		// The shade is much bigger than any window: the size the game reports can lag behind the real one (a resized or
		// restored window), and a shade that falls short leaves bright edges.
		sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(-4000, -4000, screenW + 8000, screenH + 8000), Color.Black * 0.55f);
		if (IsaClient.Sharing.ShareDialog.IsOpen)
		{
			IsaClient.Sharing.ShareDialog.Draw(sb, screenW, screenH);
			return;
		}
		if (IsaClient.Sharing.FileShareDialog.IsOpen)
		{
			IsaClient.Sharing.FileShareDialog.Draw(sb, screenW, screenH);
			return;
		}
		if (_world != null)
		{
			DrawWorld(sb, screenW, screenH);
			return;
		}
		int width = Math.Min(520, screenW - 32);
		int height = inWorld ? 376 : 250;
		Rectangle panel = new Rectangle((screenW - width) / 2, (screenH - height) / 2, width, height);
		UiKit.Panel(sb, panel, new Color(33, 43, 79) * 0.97f);
		Utils.DrawBorderStringBig(sb, IsaClient.Languages.L10n.T("Character file"), new Vector2(panel.Center.X, panel.Y + 28), new Color(255, 231, 69), 0.5f, 0.5f, 0.5f);
		Rectangle portrait = new Rectangle(panel.X + 24, panel.Y + 56, 96, 110);
		UiKit.Portrait(sb, _preview, portrait, 1.4f, new Color(20, 26, 50) * 0.9f);
		float textX = portrait.Right + 16;
		float textW = panel.Right - 20 - textX;
		UiKit.Text(sb, _player.name, textX, portrait.Y + 16, Color.White, 1f, textW);
		UiKit.Text(sb, UiKit.Summary(_player), textX, portrait.Y + 44, Color.White, 0.85f, textW);
		UiKit.Text(sb, "[c/9AA7BE:" + Path.GetFileName(_path) + "]", textX, portrait.Y + 70, Color.White, 0.75f, textW);
		if (inWorld)
		{
			UiKit.Text(sb, "[c/9AA7BE:Wearing keeps your name; your own character file is never changed.]", textX, portrait.Y + 96, Color.White, 0.7f, textW);
		}
		int bw = (panel.Width - 48 - 10) / 2;
		int by = portrait.Bottom + 18;
		if (inWorld)
		{
			if (UiKit.Button(sb, new Rectangle(panel.X + 24, by, bw, 38), "Wear it (clone)", UiKit.Green, enabled: true, "Puts this character's look, inventory, equipment, loadouts, life and mana on you.\nRestore My Character (your Player List row, or .wear restore) brings yours back."))
			{
				CharacterPresets.Wear(_player, WearParts.All, _player.name);
				Close();
				return;
			}
			if (UiKit.Button(sb, new Rectangle(panel.X + 34 + bw, by, bw, 38), "Paste as pixel art", UiKit.Purple, enabled: true, "Builds this character out of real blocks and starts a paste (" + CharacterPresets.ArtOptionsText() + ")."))
			{
				CharacterPresets.RequestPixelArt(_player, _player.name);
				Close();
				return;
			}
			by += 46;
		}
		if (UiKit.Button(sb, new Rectangle(panel.X + 24, by, bw, 38), "Save to my characters", UiKit.Blue, enabled: true, "Copies the file into your Players folder (never overwrites one of yours)."))
		{
			CharacterPresets.ImportFile(_path, _player);
			if (Main.gameMenu)
			{
				Main.LoadPlayers();
			}
			Close();
			return;
		}
		if (inWorld)
		{
			// Row of its own: send it, or nothing.
			if (UiKit.Button(sb, new Rectangle(panel.X + 34 + bw, by, bw, 38), "Send in chat", new Color(40, 110, 100), enabled: true, "Links this character in the chat line: IsaClient players get the whole file (with its IsaClient items) through the pipe."))
			{
				string path = _path;
				Player player = _player;
				Close();
				IsaClient.Sharing.ShareChat.LinkCharacter(path, player);
				return;
			}
			by += 46;
			if (UiKit.Button(sb, new Rectangle(panel.X + 24, by, panel.Width - 48, 38), "Cancel", UiKit.Red))
			{
				Close();
				return;
			}
		}
		else if (UiKit.Button(sb, new Rectangle(panel.X + 34 + bw, by, bw, 38), "Cancel", UiKit.Red))
		{
			Close();
			return;
		}
		UiKit.EndFrame();
		if (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape))
		{
			Close();
		}
	}

	private static void DrawWorld(SpriteBatch sb, int screenW, int screenH)
	{
		int width = Math.Min(520, screenW - 32);
		int height = 236;
		Rectangle panel = new Rectangle((screenW - width) / 2, (screenH - height) / 2, width, height);
		UiKit.Panel(sb, panel, new Color(33, 43, 79) * 0.97f);
		Utils.DrawBorderStringBig(sb, IsaClient.Languages.L10n.T("World file"), new Vector2(panel.Center.X, panel.Y + 28), new Color(255, 231, 69), 0.5f, 0.5f, 0.5f);
		float textX = panel.X + 24;
		float textW = panel.Width - 48;
		UiKit.Text(sb, _world.Name, textX, panel.Y + 66, Color.White, 1f, textW);
		UiKit.Text(sb, Describe(_world), textX, panel.Y + 94, Color.White, 0.85f, textW);
		UiKit.Text(sb, "[c/9AA7BE:" + Path.GetFileName(_worldPath) + "]", textX, panel.Y + 118, Color.White, 0.75f, textW);
		int bw = (panel.Width - 48 - 10) / 2;
		int by = panel.Bottom - 62;
		if (_worldSavedAs != null)
		{
			UiKit.Text(sb, "[c/8CF08C:Added to your worlds as " + _worldSavedAs + ".]", textX, panel.Y + 146, Color.White, 0.8f, textW);
			if (UiKit.Button(sb, new Rectangle(panel.X + 24, by, bw, 38), "Open Worlds folder", UiKit.Blue, enabled: true, "Opens the folder with your world files."))
			{
				try
				{
					Process.Start(Main.WorldPath);
				}
				catch (Exception ex)
				{
					ClientLog.LogException(ex);
				}
			}
			if (UiKit.Button(sb, new Rectangle(panel.X + 34 + bw, by, bw, 38), "OK", UiKit.Green))
			{
				Close();
				return;
			}
		}
		else
		{
			bool exists = Main.WorldList.Any((WorldFileData w) => w != null && w.Name == _world.Name);
			UiKit.Text(sb, exists ? ("[c/FFE645:You already have a world called " + _world.Name + ": this one is added as a new file.]") : "[c/9AA7BE:Copies the file into your Worlds folder; none of your worlds is overwritten.]", textX, panel.Y + 146, Color.White, 0.72f, textW);
			if (UiKit.Button(sb, new Rectangle(panel.X + 24, by, bw, 38), "Save to my worlds", UiKit.Blue, enabled: true, "Copies the world into your Worlds folder (never overwrites one of yours)."))
			{
				SaveWorld();
			}
			if (UiKit.Button(sb, new Rectangle(panel.X + 34 + bw, by, bw, 38), "Cancel", UiKit.Red))
			{
				Close();
				return;
			}
		}
		UiKit.EndFrame();
		if (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape))
		{
			Close();
		}
	}

	private static string Describe(WorldFileData world)
	{
		string mode = world.GameMode switch
		{
			1 => "Expert",
			2 => "Master",
			3 => "Journey",
			_ => "Classic",
		};
		string size = (world.WorldSizeX >= 8400) ? "Large" : ((world.WorldSizeX >= 6400) ? "Medium" : "Small");
		string special = world.ZenithWorld ? ", Zenith seed" : (world.RemixWorld ? ", Remix seed" : (world.DrunkWorld ? ", Drunk seed" : (world.NotTheBees ? ", Not the Bees seed" : (world.ForTheWorthy ? ", For the Worthy seed" : (world.DontStarve ? ", Constant seed" : (world.Anniversary ? ", Anniversary seed" : (world.NoTrapsWorld ? ", No Traps seed" : (world.SkyblockWorld ? ", Skyblock seed" : ""))))))));
		return size + " (" + world.WorldSizeX + "x" + world.WorldSizeY + "), " + mode + ", " + (world.HasCorruption ? "Corruption" : "Crimson") + special;
	}

	private static void SaveWorld()
	{
		try
		{
			Directory.CreateDirectory(Main.WorldPath);
			string target = Main.GetWorldPathFromName(_world.Name, cloudSave: false);
			File.Copy(_worldPath, target, overwrite: false);
			_worldSavedAs = Path.GetFileName(target);
			SoundEngine.PlaySound(12);
			Main.LoadWorlds();
			if (Main.gameMenu && Main.MenuUI?.CurrentState is UIWorldSelect worldSelect)
			{
				worldSelect.Activate();
			}
			if (!Main.gameMenu)
			{
				ClientChat.Info("Added " + _worldSavedAs + " to your worlds.");
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			SoundEngine.PlaySound(11);
			if (!Main.gameMenu)
			{
				ClientChat.Error("Could not copy the world: " + ex.Message);
			}
			Close();
		}
	}
}
