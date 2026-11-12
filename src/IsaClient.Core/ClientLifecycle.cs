using IsaClient.Gameplay;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.Drawing;
using Terraria.GameContent.Events;
using Terraria.GameInput;
using IsaClient.Building;
using IsaClient.Community;
using IsaClient.Menu;
using IsaClient.Movement;
using IsaClient.Music;
using IsaClient.Networking;
using IsaClient.Rendering;
using IsaClient.WorldTools;
using IsaClient.Assistant;

namespace IsaClient.Core;

/// <summary>Frame lifecycle of the client: initialization, the early and normal per-frame updates, entering a world.</summary>
public static class ClientLifecycle
{
	/// <summary>Every frame from DoModUpdate.</summary>
	private static void UpdateUtilityFeatures()
	{
		if (Main.gameMenu)
		{
			NpcControl.ControlledNpc = null;
			NpcControl.ControlledNpcType = 0;
			RemoteMenus.ForcedContainer = false;
			RemoteMenus.ForcedClothesWindow = false;
			RemoteMenus.ForcedHairWindow = false;
			RemoteMenus.ForcedGuideCraftingMenu = false;
			RemoteMenus.ForcedReforgeMenu = false;
			return;
		}
		NpcControl.PossessNPC(early: true);
		if (ItemTools.ReflectAllMyProjectiles)
		{
			for (int i = 0; i < Main.maxProjectiles; i++)
			{
				Projectile projectile = Main.projectile[i];
				if (projectile.active && projectile.owner == Main.myPlayer)
				{
					projectile.reflected = true;
					projectile.hostile = true;
					projectile.friendly = false;
				}
			}
		}
		if (RemoteMenus.ForcedContainer && Main.LocalPlayer.chest >= -1)
		{
			RemoteMenus.ForcedContainer = false;
		}
		if (!Main.playerInventory)
		{
			RemoteMenus.ForcedGuideCraftingMenu = false;
			RemoteMenus.ForcedReforgeMenu = false;
		}
	}

	/// <summary>Every frame from DoModUpdate (menu included, for the auto-rejoin).</summary>
	private static void UpdateFeatures()
	{
		Rejoin.UpdateAutoRejoin();
		if (Main.gameMenu)
		{
			return;
		}
		Player player = Main.LocalPlayer;
		if (WorldView.ServerSideCursor && !FreeCamera.FreeCam && !Spectate.SpectatingPlayer && Spectate.SpectatePlayerTarget == -1 && Main.hasFocus)
		{
			ParticleOrchestrator.BroadcastOrRequestParticleSpawn(ParticleOrchestraType.ShimmerBlock, new ParticleOrchestraSettings
			{
				PositionInWorld = Main.MouseWorld
			});
		}
		if (Combat.AutoKillProjectiles)
		{
			Combat.KillAllProjectiles();
		}
		if (Combat.CrystalKillAuraEnabled && DD2Event.Ongoing && !player.dead)
		{
			Combat.CrystalKillAura();
		}
		if (Combat.ShimmerGodMode && player != null && !player.dead && player.FindBuffIndex(353) == -1)
		{
			player.AddBuff(353, 300);
		}
	}


	public static bool ShowWelcomeMessage = true;

	[Setting("HideCustomMOTD")]
	public static bool HideWelcomeMessage = false;

	public static bool HasLoadedIntoWorld;

	public static void Initialize()
	{
		Main.OnTickForRunningModUpdates += OnUpdate;
		Main.OnTickForThirdPartySoftwareOnly += OnEarlyUpdate;
		TileItemLookup.ActivateTileAndWallSearchDict();
		ImageToSchematic.BuildColorFilter();
		TestHarness.Init();
		IsaClient.Feedback.FeedbackReports.Initialize();
		IsaClient.Networking.ProxyPool.Start();
	}

	public static void OnEarlyUpdate()
	{
		if (!Main._isAsyncLoadComplete || IsaClient.Networking.ServerModes.CheatsBlocked)
		{
			return;
		}
		try
		{
			if (TeleportTools.MapTeleportation && PlayerInput.Triggers.Current.KeyStatusHandler("Map TP") && Main.mapFullscreen && TeleportTools.TryGetMapCoordinates(PlayerInput.UnscaledMouseX, PlayerInput.UnscaledMouseY, out var result) && Main.hasFocus && !MenuState.CustomUIMousedOver)
			{
				Vector2 val = new Vector2((float)result.X * 16f, (float)result.Y * 16f);
				Main.LocalPlayer.position = val + (Main.LocalPlayer.velocity = Vector2.Zero);
				Main.LocalPlayer.oldPosition = Main.LocalPlayer.position;
				Main.LocalPlayer.fallStart = (int)val.X / 16;
				Main.LocalPlayer.noFallDmg = true;
				if (Main.netMode != 0)
				{
					NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
				}
			}
			if (Selection.PasteMode && !Main.gameMenu && Main.hasFocus && Main.mapFullscreen && (PlayerInput.Triggers.JustPressed.MouseRight || (PlayerInput.Triggers.JustPressed.MouseLeft && PlayerInput.Triggers.Current.KeyStatusHandler("Hold for Activator (Map)"))) && !MenuState.CustomUIMousedOver)
			{
				Point result2 = default(Point);
				if (TeleportTools.TryGetMapCoordinatesV2(PlayerInput.UnscaledMouseX, PlayerInput.UnscaledMouseY, 0, out result2) && result2.X > -1 && result2.Y > -1)
				{
					PasteEngine.PasteTiles(result2);
				}
			}
			if (BrushState.IsInTileBrush && !Selection.PasteMode && Main.mapFullscreen && PlayerInput.Triggers.Current.KeyStatusHandler("Hold for Activator (Map)") && PlayerInput.Triggers.Current.KeyStatusHandler("Use Brush (Map)") && !MenuState.CustomUIMousedOver)
			{
				Point result3 = default(Point);
				if (TeleportTools.TryGetMapCoordinatesV2(PlayerInput.UnscaledMouseX, PlayerInput.UnscaledMouseY, 0, out result3) && result3.X > -1 && result3.Y > -1)
				{
					BrushOperations.TileBrush(result3, BrushState.TileBrushSize);
				}
			}
			if (BrushState.TileExploitBrush && !Selection.PasteMode && !Main.gameMenu && Main.mapFullscreen && PlayerInput.Triggers.Current.KeyStatusHandler("Hold for Activator (Map)") && PlayerInput.Triggers.Current.KeyStatusHandler("Use Brush (Map)") && !MenuState.CustomUIMousedOver)
			{
				Point result4 = default(Point);
				if (TeleportTools.TryGetMapCoordinatesV2(PlayerInput.UnscaledMouseX, PlayerInput.UnscaledMouseY, 0, out result4) && result4.X > -1 && result4.Y > -1)
				{
					BrushOperations.TileExploitBrushV2(result4, BrushState.TileExploitBrushSize, BrushState.TileExploitBrushType, BrushState.TileExploitBrushFX, BrushState.TileExploitBrushFY, BrushState.TileExploitBrushActive);
				}
			}
			if (BrushState.IsInWallBrush && !Selection.PasteMode && Main.mapFullscreen && PlayerInput.Triggers.Current.KeyStatusHandler("Hold for Activator (Map)") && PlayerInput.Triggers.Current.KeyStatusHandler("Use Brush (Map)") && !MenuState.CustomUIMousedOver)
			{
				Point result5 = default(Point);
				if (TeleportTools.TryGetMapCoordinatesV2(PlayerInput.UnscaledMouseX, PlayerInput.UnscaledMouseY, 0, out result5) && result5.X > -1 && result5.Y > -1)
				{
					BrushOperations.WallBrush(result5, BrushState.WallBrushSize);
				}
			}
			if (BrushState.IsInEraseBrush && !Selection.PasteMode && Main.mapFullscreen && PlayerInput.Triggers.Current.KeyStatusHandler("Hold for Activator (Map)") && PlayerInput.Triggers.Current.KeyStatusHandler("Use Brush (Map)") && !MenuState.CustomUIMousedOver)
			{
				Point result6 = default(Point);
				if (TeleportTools.TryGetMapCoordinatesV2(PlayerInput.UnscaledMouseX, PlayerInput.UnscaledMouseY, 0, out result6) && result6.X > -1 && result6.Y > -1)
				{
					BrushOperations.EraseBrush(result6, BrushState.EraseBrushSize);
				}
			}
			if (BrushState.IsInLiquidBrush && !Selection.PasteMode && Main.mapFullscreen && PlayerInput.Triggers.Current.KeyStatusHandler("Hold for Activator (Map)") && PlayerInput.Triggers.Current.KeyStatusHandler("Use Brush (Map)") && !MenuState.CustomUIMousedOver)
			{
				Point result7 = default(Point);
				if (TeleportTools.TryGetMapCoordinatesV2(PlayerInput.UnscaledMouseX, PlayerInput.UnscaledMouseY, 0, out result7) && result7.X > -1 && result7.Y > -1)
				{
					BrushOperations.LiquidAndDryBrush(result7, BrushState.LiquidBrushSize);
				}
			}
			if (BrushState.IsInTilePaintBrush && !Selection.PasteMode && Main.mapFullscreen && PlayerInput.Triggers.Current.KeyStatusHandler("Hold for Activator (Map)") && PlayerInput.Triggers.Current.KeyStatusHandler("Use Brush (Map)") && !MenuState.CustomUIMousedOver)
			{
				Point result8 = default(Point);
				if (TeleportTools.TryGetMapCoordinatesV2(PlayerInput.UnscaledMouseX, PlayerInput.UnscaledMouseY, 0, out result8) && result8.X > -1 && result8.Y > -1)
				{
					BrushOperations.TilePaintBrush(result8, BrushState.TilePaintBrushSize);
				}
			}
		}
		catch
		{
		}
	}

	public static void OnUpdate()
	{
		if (!Main._isAsyncLoadComplete)
		{
			return;
		}
		try
		{
			TestHarness.Update();
			IsaClient.Networking.ServerModes.Update();
			if (Main.ServerSideCharacter)
			{
				ServerDetection.OnTshockServer = true;
			}
			InputState.ControlTildeTerminal = false;
			if (Main.gameMenu)
			{
				HasLoadedIntoWorld = false;
				ServerDetection.DidTShockDisableYou = false;
				if (!Main.ServerSideCharacter)
				{
					ServerDetection.OnTshockServer = false;
				}
				InputState.TildeTerminal = false;
				MenuState.CustomUI.SetState(null);
				BrushState.MarkedTilesForEraseBrush.Clear();
				BrushState.MarkedTilesForLiquidAndDryBrush.Clear();
				BrushState.MarkedTilesForWallBrush.Clear();
				BrushState.MarkedTilesForTileBrush.Clear();
				BrushState.MarkedTilesForTilePaintBrush.Clear();
				PacketPacing.HandlingPackets = false;
				Spectate.SpectatingPlayer = false;
				Spectate.SpectatePlayerTarget = -1;
				Spectate.ShowSpectatedInventory = false;
				CharacterPresets.OnLeftWorld();
				FreeCamera.FreeCam = false;
				BrushState.DisableBrushes();
				Selection.CopyMode = false;
				Selection.PasteMode = false;
				MapTools.LoadingChunks = false;
				PlayerListPanel.PrevPlrCount = 0;
			}
			if (PlayerInput.Triggers.Current.KeyStatusHandler("Open Menu") && !Main.drawingPlayerChat && !Main.editSign && !Main.editChest && !PlayerInput.WritingText)
			{
				InputState.ControlTildeTerminal = true;
			}
			if (InputState.ControlTildeTerminal)
			{
				if (InputState.ReleaseTildeTerminal)
				{
					InputState.TildeTerminal = !InputState.TildeTerminal;
					InputState.ReleaseTildeTerminal = false;
				}
				InputState.ReleaseTildeTerminal = false;
			}
			else
			{
				InputState.ReleaseTildeTerminal = true;
			}
			bool canUseHotkeys = !Main.drawingPlayerChat && !Main.gameMenu && !Main.editSign && !Main.editChest && !PlayerInput.WritingText && Main.hasFocus && !IsaClient.Networking.ServerModes.CheatsBlocked;
			if (canUseHotkeys && PlayerInput.Triggers.JustPressed.KeyStatusHandler("Noclip"))
			{
				ChatCommands.Toggle(1, "Noclip", ref Noclip.Enabled);
			}
			if (canUseHotkeys && Noclip.Enabled && PlayerInput.Triggers.JustPressed.KeyStatusHandler("Noclip Speed Modifier"))
			{
				Noclip.CycleSpeed();
			}
			if (canUseHotkeys && PlayerInput.Triggers.JustPressed.KeyStatusHandler("Free Camera"))
			{
				FreeCamera.FreeCam = !FreeCamera.FreeCam;
				ClientChat.Info("Free Camera " + (FreeCamera.FreeCam ? "En" : "Dis") + "abled");
			}
			if (canUseHotkeys && PlayerInput.Triggers.JustPressed.KeyStatusHandler("AFK AI"))
			{
				AiAssistant.Toggle();
			}
			if (canUseHotkeys && PlayerInput.Triggers.JustPressed.KeyStatusHandler("Auto Clicker"))
			{
				AutoClicker.SetEnabled(!AutoClicker.Enabled);
			}
			if (canUseHotkeys && PlayerInput.Triggers.JustPressed.KeyStatusHandler("Ghost Mode"))
			{
				PlayerTools.ToggleGhost();
			}
			if (canUseHotkeys && PlayerInput.Triggers.Current.KeyStatusHandler("Hold for Activator") && PlayerInput.Triggers.JustPressed.KeyStatusHandler("Undo Paste"))
			{
				PasteUndo.UndoLastPaste();
			}
			FileDrop.EnsureDropTarget();
			ClientSettings.AutoSaveSettings();
			IsaClient.MultiClient.MultiClientManager.UpdateMainThread();
			if (!Main.drawingPlayerChat && !Main.gameMenu && PlayerInput.Triggers.Current.KeyStatusHandler("Hold for Activator"))
			{
				if (PlayerInput.Triggers.JustPressed.KeyStatusHandler("Load Config"))
				{
					ClientSettings.Load(noCheats: true);
					ClientChat.Info("Loaded configuration!");
				}
			}
			Selection.CopySelection();
			Player player = Main.player[Main.myPlayer];
			if (!Main.gameMenu)
			{
				UpdateInWorld();
			}
			if (TeleportTools.GotoMouse && !Main.gameMenu && !Main.drawingPlayerChat && !Main.editSign && !Main.editChest && !PlayerInput.WritingText && !Main.LocalPlayer.mouseInterface && Main.hasFocus)
			{
				Vector2 pos = new Vector2(Main.MouseWorld.X, Main.MouseWorld.Y);
				if ((!PlayerInput.Triggers.Current.KeyStatusHandler("Hold for Rapidfire") && PlayerInput.Triggers.JustPressed.KeyStatusHandler("Mouse TP")) || (PlayerInput.Triggers.Current.KeyStatusHandler("Hold for Rapidfire") && PlayerInput.Triggers.Current.KeyStatusHandler("Mouse TP")))
				{
					TeleportTools.MouseTP(pos);
				}
			}
			if (Noclip.Enabled && !player.dead && !Main.gameMenu)
			{
				bool canMove = !Main.drawingPlayerChat && !Main.editSign && !Main.editChest && !PlayerInput.WritingText;
				if ((player.controlUp || player.controlJump) && !Spectate.SpectatingPlayer && canMove)
				{
					player.oldPosition = new Vector2(player.oldPosition.X, player.oldPosition.Y - (float)Noclip.Speed * player.gravDir);
				}
				if (player.controlLeft && !Spectate.SpectatingPlayer && canMove)
				{
					player.oldPosition = new Vector2(player.oldPosition.X - (float)Noclip.Speed, player.oldPosition.Y);
				}
				if (player.controlRight && !Spectate.SpectatingPlayer && canMove)
				{
					player.oldPosition = new Vector2(player.oldPosition.X + (float)Noclip.Speed, player.oldPosition.Y);
				}
				if (player.controlDown && !Spectate.SpectatingPlayer && canMove)
				{
					player.oldPosition = new Vector2(player.oldPosition.X, player.oldPosition.Y + (float)Noclip.Speed * player.gravDir);
				}
				if (player.oldPosition.X < Main.leftWorld + (float)WorldView.WorldBorderSize * 16f + 16f && WorldView.WorldBorderSize > -2)
				{
					player.oldPosition.X = Main.leftWorld + (float)WorldView.WorldBorderSize * 16f + 16f;
				}
				if (player.oldPosition.X + (float)player.width > Main.rightWorld - (float)WorldView.WorldBorderSize * 16f - 32f && WorldView.WorldBorderSize > -2)
				{
					player.oldPosition.X = Main.rightWorld - (float)WorldView.WorldBorderSize * 16f - 32f - (float)player.width;
				}
				if (player.oldPosition.Y < Main.topWorld + (float)WorldView.WorldBorderSize * 16f + 16f && WorldView.WorldBorderSize > -2)
				{
					player.oldPosition.Y = Main.topWorld + (float)WorldView.WorldBorderSize * 16f + 16f;
				}
				if (player.oldPosition.Y > Main.bottomWorld - (float)WorldView.WorldBorderSize * 16f - 32f + (float)player.height && WorldView.WorldBorderSize > -2)
				{
					player.oldPosition.Y = Main.bottomWorld - (float)WorldView.WorldBorderSize * 16f - 32f + (float)player.height;
				}
				player.position = (player.oldPosition += (player.velocity = Vector2.Zero));
				player.fallStart = (int)(player.position.Y / 16f);
				if ((player.shimmering || player.shimmerWet) && !player.IsShimmerGodMode)
				{
					player.ClearBuff(353);
				}
				player.noFallDmg = true;
			}
			if (MapTools.RevealMapCooldown)
			{
				Main.LocalPlayer.isOperatingAnotherEntity = false;
				if (Main.gameMenu)
				{
					MapTools.RevealMapCooldown = false;
				}
			}
			if (BrushState.IsInTileBrush && Main.hasFocus && !Selection.PasteMode && !Main.mapFullscreen && PlayerInput.Triggers.Current.KeyStatusHandler("Use Brush") && !Main.LocalPlayer.mouseInterface)
			{
				BrushOperations.TileBrush(Main.MouseWorld.ToTileCoordinates(), BrushState.TileBrushSize);
			}
			if (BrushState.TileExploitBrush && Main.hasFocus && !Selection.PasteMode && !Main.mapFullscreen && PlayerInput.Triggers.Current.KeyStatusHandler("Use Brush") && !Main.LocalPlayer.mouseInterface)
			{
				BrushOperations.TileExploitBrushV2(Main.MouseWorld.ToTileCoordinates(), BrushState.TileExploitBrushSize, BrushState.TileExploitBrushType, BrushState.TileExploitBrushFX, BrushState.TileExploitBrushFY, BrushState.TileExploitBrushActive);
			}
			if (BrushState.IsInWallBrush && Main.hasFocus && !Selection.PasteMode && !Main.mapFullscreen && PlayerInput.Triggers.Current.KeyStatusHandler("Use Brush") && !Main.LocalPlayer.mouseInterface)
			{
				BrushOperations.WallBrush(Main.MouseWorld.ToTileCoordinates(), BrushState.WallBrushSize);
			}
			if (BrushState.IsInEraseBrush && Main.hasFocus && !Selection.PasteMode && !Main.mapFullscreen && PlayerInput.Triggers.Current.KeyStatusHandler("Use Brush") && !Main.LocalPlayer.mouseInterface)
			{
				BrushOperations.EraseBrush(Main.MouseWorld.ToTileCoordinates(), BrushState.EraseBrushSize);
			}
			if (BrushState.IsInLiquidBrush && Main.hasFocus && !Selection.PasteMode && !Main.mapFullscreen && PlayerInput.Triggers.Current.KeyStatusHandler("Use Brush") && !Main.LocalPlayer.mouseInterface)
			{
				BrushOperations.LiquidAndDryBrush(Main.MouseWorld.ToTileCoordinates(), BrushState.LiquidBrushSize);
			}
			if (BrushState.IsInTilePaintBrush && Main.hasFocus && !Selection.PasteMode && !Main.mapFullscreen && PlayerInput.Triggers.Current.KeyStatusHandler("Use Brush") && !Main.LocalPlayer.mouseInterface)
			{
				BrushOperations.TilePaintBrush(Main.MouseWorld.ToTileCoordinates(), BrushState.TilePaintBrushSize);
			}
			if (Selection.PasteMode && Selection.PasteDelayCounter <= 0 && !Main.gameMenu && Main.hasFocus && PlayerInput.Triggers.JustPressed.MouseLeft && !Main.LocalPlayer.mouseInterface && !Main.mapFullscreen)
			{
				PasteEngine.PasteTiles(Selection.PasteImagePreviewPos);
			}
			if (Selection.SelectionMode && Main.hasFocus)
			{
				Selection.FillOrEraseSelectionPoints();
			}
			if (!PacketPacing.HandlingPackets && !Main.gameMenu && Main.netMode != 0 && BrushOperations.TilesToBrush.Count > 0 && !IsaClient.MultiClient.MultiPaste.TryStartBrush())
			{
				BrushOperations.HandleBrushNetMessagesV3();
			}
			if (PlayerTools.GhostCheat && Main.LocalPlayer != null && !Main.LocalPlayer.ghost)
			{
				PlayerTools.GhostCheat = false;
			}
			UpdateInfoFeatures();
			UpdateFeatures();
			UpdateUtilityFeatures();
			MusicPlayer.Update();
			PlayerCheats.UpdatePlayerCheats();
			BuffTools.Update();
			UpdateTimers();
		}
		catch
		{
		}
	}

	public static void UpdateInWorld()
	{
		IsaClient.Rendering.Thor.Update();
		IsaClient.Rendering.LucyChorus.Update();
		IsaClient.Movement.PixieMode.Update();
		IsaClient.Movement.NpcBrain.Update();
		IsaClient.Movement.NpcLook.Update();
		IsaClient.WorldTools.TileSearch.Update();
		if (Bombs.BombFiller && Selection.SelectionMode && ServerDetection.TShockDisableTime <= 0)
		{
			Bombs.BombFillSelection();
		}
	}

	public static void UpdateTimers()
	{
		if (ServerDetection.DidTShockDisableYou && ServerDetection.TShockDisableTime <= 0)
		{
			ServerDetection.DidTShockDisableYou = false;
		}
		if (ServerDetection.TShockDisableTime > 0)
		{
			ServerDetection.TShockDisableTime--;
		}
		if (Selection.ConcurrentPastes < 0)
		{
			Selection.ConcurrentPastes = 0;
		}
		if (AntiLag.NoKillSoundTime > 0)
		{
			AntiLag.NoKillSoundTime--;
		}
		if (MapTools.QueuedMapRefreshTime > 0)
		{
			MapTools.QueuedMapRefreshTime--;
			if (MapTools.QueuedMapRefreshTime == 0)
			{
				Main.refreshMap = true;
			}
		}
		if (Selection.FixUpperTime > 0)
		{
			Selection.CancelPaste = true;
			Selection.CancelAction = true;
			Selection.ConcurrentPastes = 0;
			Selection.ConcurrentFillOrDels = 0;
			Selection.ConcurrentConversions = 0;
			Selection.OpeningTEDITSchematic = false;
			Selection.SavingTEDITSchematic = false;
			Selection.FixUpperTime--;
		}
		if (ServerDetection.AwaitTshockResponseTime > 0 && Netplay.Connection.TimeOutTimer < 40)
		{
			ServerDetection.AwaitTshockResponseTime--;
		}
		if (ServerDetection.AwaitingTshockPvpResponseTime > 0 && Netplay.Connection.TimeOutTimer < 40)
		{
			ServerDetection.AwaitingTshockPvpResponseTime--;
		}
		if (Selection.PasteDelayCounter > 0)
		{
			Selection.PasteDelayCounter--;
		}
	}

	/// <summary>A line of dashes as wide as the widest of <paramref name="lines"/> in the chat font.</summary>
	private static string WelcomeRule(string[] lines)
	{
		try
		{
			ReLogic.Graphics.DynamicSpriteFont font = Terraria.GameContent.FontAssets.MouseText.Value;
			float widest = 0f;
			foreach (string line in lines)
			{
				widest = System.Math.Max(widest, font.MeasureString(IsaClient.Languages.L10n.T(line)).X);
			}
			float dash = font.MeasureString("-").X;
			if (dash > 0f)
			{
				return new string('-', System.Math.Max(1, (int)System.Math.Round(widest / dash)));
			}
		}
		catch
		{
		}
		return new string('-', 42);
	}

	public static void OnEnterWorld()
	{
		IsaClient.Plugins.PluginHost.OnEnterWorld();
		IsaClient.Networking.VanishDetection.ResetJoins();
		if (ShowWelcomeMessage)
		{
			string[] welcome = { "IsaClient for Terraria 1.4.5.8", "Press the Open Menu key (default: ~ or F9) to open the panel.", "Type .help for commands." };
			string rule = WelcomeRule(welcome);
			ClientChat.Info(rule);
			foreach (string line in welcome)
			{
				ClientChat.Info(line);
			}
			ClientChat.Info(rule);
			ShowWelcomeMessage = false;
		}
		string[] array = new string[7] { "Open Menu", "Mouse TP", "Map TP", "Use Brush", "Use Brush (Map)", "Hold for Activator", "Hold for Activator (Map)" };
		foreach (string text in array)
		{
			if (PlayerInput.CurrentProfile.InputModes[InputMode.Keyboard].KeyStatus.TryGetValue(text, out var value) && value.Count <= 0)
			{
				ClientChat.Notice("Warning: " + text + " is not bound! Please open your controls and bind the key!");
			}
		}
		HasLoadedIntoWorld = true;
		if (!Main.ServerSideCharacter && !ServerDetection.OnTshockServer && Main.netMode == 1)
		{
			RawPackets.SendModifiedData(147);
			NetMessage.SendData(147, -1, -1, null, Main.myPlayer, Main.LocalPlayer.CurrentLoadoutIndex);
			ServerDetection.AwaitTshockResponseTime = 300;
		}
		ServerDetection.AwaitingTshockPvpResponseTime = 0;
		Selection.ConcurrentPastes = 0;
		IsaClient.Gameplay.ItemModPresets.OnEnterWorld();
	}

	/// <summary>Per-tick logic for the Informational/Miscellaneous features (called from DoModUpdate).</summary>
	private static void UpdateInfoFeatures()
	{
		Performance.MeasureUpdateInterval();
		ChatTools.UpdateChatSpammer();
		RainbowText.UpdateRainbow();
		if (AntiLag.GlobalBlockedProjTime > 0)
		{
			AntiLag.GlobalBlockedProjTime--;
		}
		else
		{
			Performance.ShownLowFpsBlockedProjMsg = false;
		}
		if (AntiLag.NoSoundsTime > 0)
		{
			AntiLag.NoSoundsTime--;
		}
		if (AntiLag.DisableProjLightTime > 0)
		{
			AntiLag.DisableProjLightTime--;
		}
		CharacterPresets.Update();
		SscSync.Update();
		ProtectedChests.Update();
		if (Main.gameMenu)
		{
			FreeCamera.FreeCam = false;
			FreeCamera.FreeCam2 = false;
			Spectate.SpectatePlayerTarget = -1;
			Spectate.ShowSpectatedInventory = false;
			VanishDetection.AwaitingVanishResponseTime = 0;
			return;
		}
		Spectate.UpdateSpectate();
		VanishDetection.UpdateVanishSearch();
		if (AntiLag.ParticleLimiter)
		{
			int count = 0;
			for (int i = 0; i < Main.dust.Length; i++)
			{
				if (Main.dust[i].active)
				{
					count++;
				}
			}
			AntiLag.ActiveDustCount = count;
		}
		for (int j = 0; j < 256; j++)
		{
			ChatTools.UpdateSpamCounters(Main.player[j], j);
		}
		Performance.LowFpsMeasures();
		Player player = Main.LocalPlayer;
		if (Combat.AutoButcher && !player.dead)
		{
			Combat.ButcherMobsAndNpcs(Combat.AutoButcherDamage, 1);
		}
		if (Combat.KillAura && Combat.KillAuraSize > 0 && !player.dead)
		{
			Combat.KillBubble(Combat.KillAuraSize);
		}
		if (WorldCommands.AutoDeleteItems && Main.netMode == 0)
		{
			WorldCommands.DeleteGroundItemsCall(1);
		}
		// Online the items go through the server (what it reserves for you), wipe or auto wipe.
		GroundWipe.Update();
		if (FreeCamera.FreeCam || Spectate.IsSpectating)
		{
			FreeCamera.UpdateFreeCamSections();
		}
	}
}
