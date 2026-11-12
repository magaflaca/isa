using IsaClient.Building;
using IsaClient.Menu;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Terraria;
using Terraria.Cinematics;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.Drawing;
using Terraria.GameInput;
using Terraria.ID;
namespace IsaClient.Core;

/// <summary>Chat commands that do not belong to one module (settings, resets, small tweaks).</summary>
public static class GeneralCommands
{

	#region Chat commands

	/// <summary>.mychar / .mycharacter</summary>
	internal static void MyCharacterCommand(CommandContext command)
	{
		InputState.TildeTerminal = true;
		CharacterWindow.Open();
	}

	/// <summary>.undo / .undopaste</summary>
	internal static void UndoPasteCommand(CommandContext command)
	{
		PasteUndo.UndoLastPaste();
	}

	/// <summary>.lockonplayers / .targetplayers</summary>
	internal static void LockOnPlayersCommand(CommandContext command)
	{
		LockOnHelper.ChangeEntityTargetType();
		ClientChat.Info("Lock-On can " + (LockOnHelper.targetPlayers ? "now" : "no longer") + " target hostile players.");
	}

	/// <summary>.changemusic / .swapmusic / .sm</summary>
	internal static void ChangeMusicCommand(CommandContext command)
	{
		Main.swapMusic = !Main.swapMusic;
		if (Main.drunkWorld && !Main.remixWorld)
		{
			ClientChat.Info(Main.swapMusic ? "Music swapped to normal!" : "Music returned to Otherworldly.");
		}
		else
		{
			ClientChat.Info(Main.swapMusic ? "Music swapped to Otherworldly music!" : "Music returned to normal.");
		}
	}

	/// <summary>.rotw / .rotateworld</summary>
	internal static void RotateWorldCommand(CommandContext command)
	{
		if (WorldGen.generatingWorld || WorldGen.TransformingWorld)
		{
			ClientChat.Error("You can't do that while the world is being generated!");
		}
		else if (Main.netMode != 0)
		{
			ClientChat.Error("You cannot rotate the world while in an online game.");
		}
		else
		{
			System.Threading.Tasks.Task.Run(delegate
				{
					try
					{
						Terraria.WorldBuilding.WorldUtils.DebugRotate();
						ClientChat.Info("World rotated!");
					}
					catch (Exception ex)
					{
						ClientLog.LogException(ex);
					}
				});
		}
	}

	/// <summary>.reud / .reusedelay</summary>
	internal static void ReuseDelayCommand(CommandContext command)
	{
		if (command.Parts.Length > 1 && int.TryParse(command.Parts[1], out var delay) && delay >= 0)
		{
			command.Player.HeldItem.reuseDelay = delay;
			ClientChat.Info("Reuse delay set to " + delay);
		}
		else
		{
			ClientChat.Error((command.Parts.Length > 1) ? "Invalid Amount" : "Value Required");
		}
	}

	/// <summary>.ar / .autoreuse</summary>
	internal static void AutoReuseCommand(CommandContext command)
	{
		command.Player.HeldItem.autoReuse = !command.Player.HeldItem.autoReuse;
		ClientChat.Info("Auto Reuse for current item " + (command.Player.HeldItem.autoReuse ? "en" : "dis") + "abled");
	}

	/// <summary>.seu / .shootseveryuse</summary>
	internal static void ShootsEveryUseCommand(CommandContext command)
	{
		command.Player.HeldItem.shootsEveryUse = !command.Player.HeldItem.shootsEveryUse;
		ClientChat.Info("Shoots Every Use for current item " + (command.Player.HeldItem.shootsEveryUse ? "en" : "dis") + "abled");
	}

	/// <summary>.invis</summary>
	internal static void InvisCommand(CommandContext command)
	{
		if (command.Parts.Length > 1 && int.TryParse(command.Parts[1], out var ticks))
		{
			if (ticks < -1)
			{
				ticks = 1800;
			}
			else if (ticks == -1)
			{
				ticks = int.MaxValue;
			}
			command.Player.AddBuff(10, ticks);
			NetMessage.SendData(50, -1, -1, null, Main.myPlayer);
			ClientChat.Info("Invisible for " + ((ticks == int.MaxValue) ? "ever" : (ticks + " ticks")));
		}
		else
		{
			ClientChat.Error((command.Parts.Length > 1) ? "Invalid Value" : "Value Needed (ticks, -1 = forever)");
		}
	}

	/// <summary>.changename</summary>
	internal static void ChangeNameCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			command.Player.name = command.Text.Substring(command.Parts[0].Length + 2).Trim();
			NetMessage.SendData(4, -1, -1, null, Main.myPlayer);
			ClientChat.Info("Name Changed: " + command.Player.name);
		}
		else
		{
			ClientChat.Error("You need a name for yourself.");
		}
	}

	/// <summary>.iname</summary>
	internal static void INameCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			command.Player.HeldItem.SetNameOverride(command.Text.Substring(command.Parts[0].Length + 2));
			ClientChat.Info("Name Changed: " + command.Player.HeldItem.Name);
		}
		else
		{
			ClientChat.Error("You need a name for the item.");
		}
	}

	/// <summary>.debugmsg</summary>
	internal static void DebugMsgCommand(CommandContext command)
	{
		ClientChat.DebugMessages = !ClientChat.DebugMessages;
		ClientChat.Info("Debug messages " + (ClientChat.DebugMessages ? "ON" : "OFF"));
	}

	/// <summary>.anim</summary>
	internal static void AnimCommand(CommandContext command)
	{
		if (command.Parts.Length < 3)
		{
			ClientChat.Error((command.Parts.Length > 1) ? "Item time needed." : "Item rotation needed.");
			return;
		}
		if (!float.TryParse(command.Parts[1], out var rotation))
		{
			ClientChat.Error("Invalid rotation (float).");
			return;
		}
		if (!short.TryParse(command.Parts[2], out var time))
		{
			ClientChat.Error("Invalid animation time (short).");
			return;
		}
		command.Player.itemRotation = rotation;
		command.Player.itemAnimation = time;
		if (Main.netMode != 0)
		{
			NetMessage.SendData(41, -1, -1, null, Main.myPlayer);
		}
		ClientChat.Info("Played item animation on yourself.");
	}

	/// <summary>.seed</summary>
	internal static void SeedCommand(CommandContext command)
	{
		string seed = (Main.ActiveWorldFileData != null) ? Main.ActiveWorldFileData.GetFullSeedText() : "";
		ClientChat.Info("Seed: " + (string.IsNullOrEmpty(seed) ? "unknown" : seed));
	}

	/// <summary>.fakeitem / .fakeitm</summary>
	internal static void FakeItemCommand(CommandContext command)
	{
		if (command.Parts.Length <= 1 || !int.TryParse(command.Parts[1], out var fakeId) || fakeId <= 0 || fakeId >= ItemID.Count)
		{
			ClientChat.Error("Usage: .fakeitem <item id> (shows an item flying from you to every player)");
			return;
		}
		for (int i = 0; i < Main.maxPlayers; i++)
		{
			Player player = Main.player[i];
			if (player.active && i != Main.myPlayer)
			{
				ParticleOrchestrator.BroadcastOrRequestParticleSpawn(ParticleOrchestraType.ItemTransfer, new ParticleOrchestraSettings
					{
						PositionInWorld = command.Player.Center,
						MovementVector = player.Center - command.Player.Center,
						UniqueInfoPiece = fakeId
					});
			}
		}
	}

	/// <summary>.coins</summary>
	internal static void CoinsCommand(CommandContext command)
	{
		for (int pass = 0; pass < 5; pass++)
		{
			for (int i = 0; i < Main.maxNPCs; i++)
			{
				if (Main.npc[i].active)
				{
					NetMessage.SendData(92, -1, -1, null, i, 100000000f, Main.npc[i].position.X, Main.npc[i].position.Y);
				}
			}
		}
		ClientChat.Info("Gave every NPC 500 platinum coins.");
	}

	/// <summary>.fillall / .fa</summary>
	internal static void FillAllCommand(CommandContext command)
	{
		IEntitySource source = WorldGen.GetItemSource_FromTileBreak((int)Main.MouseWorld.X, (int)Main.MouseWorld.Y);
		foreach (Player player in Main.player.Where((Player p) => p.active && p.whoAmI != Main.myPlayer))
		{
			foreach (Item item in player.inventory.Where((Item it) => it.stack > 100 && it.stack < it.maxStack).ToList())
			{
				player.QuickSpawnItem(source, item.type, item.maxStack - item.stack);
			}
			ClientChat.Info("Filled " + player.name);
		}
	}

	/// <summary>.spawnpos / .setworldspawn</summary>
	internal static void SetWorldSpawnCommand(CommandContext command)
	{
		if (Main.netMode == 0)
		{
			Main.spawnTileX = (int)command.Player.Bottom.X / 16;
			Main.spawnTileY = (int)command.Player.Bottom.Y / 16;
			ClientChat.Info("Set world spawn to " + Main.spawnTileX + ", " + Main.spawnTileY);
		}
		else
		{
			ClientChat.Error("You can't change the world spawn in an online game.");
		}
	}

	/// <summary>.dungeonpos / .setdungeonpos</summary>
	internal static void SetDungeonPosCommand(CommandContext command)
	{
		if (Main.netMode == 0)
		{
			Main.dungeonX = (int)command.Player.Bottom.X / 16;
			Main.dungeonY = (int)command.Player.Bottom.Y / 16;
			ClientChat.Info("Set dungeon position to " + Main.dungeonX + ", " + Main.dungeonY);
		}
		else
		{
			ClientChat.Error("You can't change the dungeon position in an online game.");
		}
	}

	/// <summary>.clothingstyle / .clothing / .clothstyle / .cltst</summary>
	internal static void ClothingStyleCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out var variant) && variant >= 0 && variant < PlayerVariantID.Count)
			{
				command.Player.skinVariant = variant;
				NetMessage.SendData(4, -1, -1, null, Main.myPlayer);
				ClientChat.Info("Skin variant (Clothes and Gender) set to " + variant + ((variant >= 10) ? " (mannequin)" : ""));
			}
			else
			{
				ClientChat.Error("Invalid skin variant (0-" + (PlayerVariantID.Count - 1) + ").");
			}
		}
		else
		{
			ClientChat.Info("Current skin variant: " + command.Player.skinVariant);
		}
	}

	/// <summary>.icerod</summary>
	internal static void IceRodCommand(CommandContext command)
	{
		if (command.Player.HeldItem.type == 496)
		{
			bool placesIce = command.Player.HeldItem.createTile == 127;
			command.Player.HeldItem.createTile = (placesIce ? (-1) : 127);
			command.Player.HeldItem.shoot = (placesIce ? 80 : 0);
			command.Player.HeldItem.mana = (placesIce ? 6 : 0);
			if (command.Player.selectedItem == 58)
			{
				Main.mouseItem = command.Player.HeldItem.Clone();
			}
			ClientChat.Info((!placesIce) ? "Your Ice Rod now places Magical Ice directly." : "Your Ice Rod no longer places Magical Ice directly.");
		}
		else
		{
			ClientChat.Error("You need to hold an Ice Rod to use this.");
		}
	}

	/// <summary>.dd2film</summary>
	internal static void Dd2FilmCommand(CommandContext command)
	{
		CinematicManager.Instance.PlayFilm(new DD2Film());
	}

	/// <summary>.dstfilm</summary>
	internal static void DstFilmCommand(CommandContext command)
	{
		CinematicManager.Instance.PlayFilm(new DSTFilm());
	}

	/// <summary>.luck</summary>
	internal static void LuckCommand(CommandContext command)
	{
		ClientChat.Info("Overall Luck: " + command.Player.luck);
		ClientChat.Info("Coin Luck: " + command.Player.coinLuck);
		ClientChat.Info("Torch Luck: " + command.Player.torchLuck);
		ClientChat.Info("Luck Potion: " + command.Player.luckPotion);
		ClientChat.Info("Ladybug Luck Time: " + command.Player.ladyBugLuckTimeLeft);
		ClientChat.Info("Garden Gnome Nearby: " + command.Player.HasGardenGnomeNearby);
	}

	/// <summary>.loadbp</summary>
	internal static void LoadBpCommand(CommandContext command)
	{
		ClientChat.Info("Removed, use .loadsch to use the new TEdit Schematics.");
	}

	/// <summary>.savebp</summary>
	internal static void SaveBpCommand(CommandContext command)
	{
		ClientChat.Info("Removed, use .savesch to use the new TEdit Schematics.");
	}

	/// <summary>.loadsch / .opensch</summary>
	internal static void LoadsChCommand(CommandContext command)
	{
		SchematicFiles.Load();
	}

	/// <summary>.savesch</summary>
	internal static void SavesChCommand(CommandContext command)
	{
		SchematicFiles.Save();
	}

	/// <summary>.drawphoto / .drawpic / .loadpic</summary>
	internal static void DrawPhotoCommand(CommandContext command)
	{
		PixelArt.LoadPhotosFromFile();
	}

	/// <summary>.tiledata</summary>
	internal static void TileDataCommand(CommandContext command)
	{
		Point p = Main.MouseWorld.ToTileCoordinates();
		if (!WorldGen.InWorld(p.X, p.Y) || Framing.GetTileSafely(p.X, p.Y) == null)
		{
			ClientChat.Error("No tile under the cursor.");
			return;
		}
		Tile tile = Framing.GetTileSafely(p.X, p.Y);
		ClientChat.Notice("-Tile Data-");
		ClientChat.Notice($"Tile Active: {tile.active()} | Tile Type: {tile.type}, Tile Wall: {tile.wall} | Tile FrameX: {tile.frameX} | Tile FrameY: {tile.frameY} | Tile Liquid Type: {tile.liquidType()} | Tile Liquid Amount: {tile.liquid}");
		ClientChat.Notice($"Tile Wire: {tile.wire()} | Tile Wire2: {tile.wire2()} | Tile Wire3: {tile.wire3()} | Tile Wire4: {tile.wire4()} | Tile Actuator: {tile.actuator()} | Tile Inactive: {tile.inActive()}");
		ClientChat.Notice($"Tile Paint: {tile.color()} | Tile Wall Paint: {tile.wallColor()} | Tile Fullbright: {tile.fullbrightBlock()} | Tile Fullbright Wall: {tile.fullbrightWall()} | Tile Invisible: {tile.invisibleBlock()} | Tile Invisible Wall: {tile.invisibleWall()}");
		ClientChat.Notice($"Tile Wall FrameX: {tile.wallFrameX()} | Tile Wall FrameY: {tile.wallFrameY()}");
		ClientChat.Notice("-----------------");
	}

	/// <summary>.menu / .ui</summary>
	internal static void MenuCommand(CommandContext command)
	{
		string mode = (command.Parts.Length > 1) ? command.Parts[1].ToLower() : "";
		if (mode == "check")
		{
			int features = System.Linq.Enumerable.Count(IsaClient.Hub.HubCatalog.AllFeatures);
			System.Collections.Generic.List<string> problems = IsaClient.Hub.HubCatalog.Problems;
			ClientChat.Info("New panel: " + features + " features, " + problems.Count + " problems.");
			foreach (string problem in problems)
			{
				ClientChat.Notice(problem);
			}
			return;
		}
		if (mode == "classic" || mode == "old" || mode == "new")
		{
			IsaClient.Hub.ClientHub.SetClassic(mode != "new");
			InputState.TildeTerminal = true;
			return;
		}
		if (command.Parts.Length > 1)
		{
			InputState.TildeTerminal = command.Parts[1].ToLower() == "on" || command.Parts[1] == "1";
		}
		else
		{
			InputState.TildeTerminal = !InputState.TildeTerminal;
		}
		ClientChat.Info("IsaClient panel " + (InputState.TildeTerminal ? "opened" : "closed") + ".");
	}

	/// <summary>.refui</summary>
	internal static void RefUiCommand(CommandContext command)
	{
		CheatPanel.RefreshUI();
		ClientChat.Info("UI refreshed.");
	}

	/// <summary>.config / .saveconfig</summary>
	internal static void SaveConfigCommand(CommandContext command)
	{
		ClientSettings.Save();
		ClientChat.Info("Saved configuration!");
	}

	/// <summary>.loadconfig</summary>
	internal static void LoadConfigCommand(CommandContext command)
	{
		ClientSettings.Load(noCheats: true);
		ClientChat.Info("Loaded configuration!");
	}

	#endregion
}
