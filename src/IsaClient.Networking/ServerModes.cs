using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Networking;

/// <summary>What a server running the IsaClientModes plugin lets IsaClient do there.</summary>
public enum IsaMode : byte
{
	/// <summary>No plugin, or the plugin leaves IsaClient alone.</summary>
	None = 0,
	/// <summary>The server only knows you use IsaClient.</summary>
	Detect = 1,
	/// <summary>The client's cheats are off while you play there.</summary>
	NoCheats = 2,
	/// <summary>The whole client is off: commands, panel, plugins and the pipe; the game plays as vanilla.</summary>
	Vanilla = 3
}

/// <summary>
/// Server modes: a server with the IsaClientModes plugin (github.com/magaflaca/IsaClientModes) asks the client what it is
/// and tells it what it may do there (<see cref="IsaMode"/>). It is a layer on top of the client, nothing in the
/// features changes:
/// - the cheats are switches (static fields, <see cref="CheatFields"/>): while a mode limits them, their values are kept
///   aside and put back the moment you leave the server; settings saved meanwhile save the kept values, so nothing you
///   had on is ever lost;
/// - commands, panel pages, plugins and the pipe are stopped at their entry points (see the callers of the Blocks* members).
///
/// Transport: packet 82 (NetModules) with module id <see cref="Module"/>, which no Terraria registers. A vanilla server or
/// client drops an unknown module without doing anything (TShock lets it through to the game, which ignores it), the
/// server never relays it to other players, and nothing shows in game. The client never speaks first: it only answers a
/// server that asks, so on servers without the plugin nothing is ever sent.
/// </summary>
public static class ServerModes
{
	/// <summary>NetModule id of this protocol (vanilla registers 0..14).</summary>
	public const ushort Module = 0xC1A5;

	/// <summary>"ISM" + protocol version, after the module id.</summary>
	private static readonly byte[] Magic = { 0x49, 0x53, 0x4D, 0x01 };

	private const byte Ask = 1;

	private const byte SetMode = 2;

	/// <summary>A card from the server's plugins (title, text, style, icon, seconds).</summary>
	private const byte Popup = 3;

	private const byte Hello = 10;

	private const byte Ack = 11;

	/// <summary>The mode of the server you are on (None outside a server).</summary>
	public static IsaMode Mode { get; private set; }

	/// <summary>What the server's admins wrote about it (may be empty).</summary>
	public static string ServerNote { get; private set; } = "";

	public static bool CheatsBlocked => Mode >= IsaMode.NoCheats;

	public static bool ClientBlocked => Mode == IsaMode.Vanilla;

	#region What is limited

	/// <summary>Cheat switches turned off by <see cref="IsaMode.NoCheats"/> (and Vanilla). Every one is "off" at false / 0.</summary>
	private static readonly string[] CheatFields =
	{
		"IsaClient.Gameplay.PlayerCheats:AntiDeathGod,DodgeEverything,InstantRegen,InfiniteMana,AntiDebuff,InstantRespawn,ExtendedPickUpRange,ExtendedPlacementRange,InfiniteMinions,InfiniteAmmo,InfiniteBreath,NoFallDamage,IgnoreLiquids,IgnoreDamageBlocks,UnrestrictTeleportRod,SuppressHorrified,SuppressCreativeShock,IgnoreCC,ExtraAccSlots,EquipDupeAccessory,GrappleMounted,ItemsOnTransformMounts,NoSwapRestriction,HammerAnything,AlwaysPvpDeath,FreeCraft,CraftAnywhere,AlwaysSolarExplosion,AlwaysStardustGuardian,AlwaysGhostHeal,AlwaysGhostHurt,AlwaysNebulaSet,JourneyMenu,GodMode,FreeShops,DeathItemDropMode,PersistBuffsMode,ForceDashType,DreamLuckMode",
		"IsaClient.Gameplay.Combat:ShimmerGodMode,CrystalKillAuraEnabled,AutoKillProjectiles,KillAura,AutoButcher",
		"IsaClient.Gameplay.PlayerTools:AnyItemAnySlot",
		"IsaClient.Gameplay.SmartAim:Enabled,AutoAim",
		"IsaClient.Movement.FreeCamera:FreeCam",
		"IsaClient.Movement.Noclip:Enabled,RapidPosUpdate",
		"IsaClient.Movement.PixieMode:Active",
		"IsaClient.Movement.TeleportTools:BypassTeleportRequests,GotoMouse,MapTeleportation",
		"IsaClient.Movement.Travel:AsphaltSurfaces,BouncySurfaces,InfiniteFlight,InfiniteCarpet,AlwaysHoverable,MaxWings,PortalPhysics,InfiniteReach,UnlimitedWinds",
		"IsaClient.Movement.CarpetBroom:Enabled",
		"IsaClient.Building.Placement:PlaceAnywhere,ForceProtectedWalls,BuilderMenu",
		"IsaClient.Building.Bombs:BombFiller",
		"IsaClient.Building.BrushState:TileBrush,WallBrush,EraseBrush,LiquidBrush,TileExploitBrush,TilePaintBrush",
		"IsaClient.Building.Selection:CopyMode,PasteMode,SelectionMode",
		"IsaClient.Networking.PacketPacing:BypassPacketProtections",
		"IsaClient.Rendering.WorldView:ForceSpelunker,ForceHunter,ForceDangersense,ForceBiomeSight,RenderInvisibleTiles,SeeInvisPlayers,InvisibleEnemies,NoScreenClamp",
		"IsaClient.Rendering.FullbrightLighting:Percent",
		"IsaClient.Rendering.Hitboxes:PlayerHitboxes,NpcHitboxes,ProjectileHitboxes,ItemHitboxes",
		"IsaClient.Rendering.InfoOverlays:ShowIDs,SeeTileData,ShowItemOwner,ExtraItemInfo,SeeNpcData",
		"IsaClient.Community.Spectate:SpectatingPlayer,ShowSpectatedInventory",
		"IsaClient.Community.PlayerListPanel:PlayerTracker"
	};

	/// <summary>What else <see cref="IsaMode.Vanilla"/> turns off: looks that other players see and the pipe.</summary>
	private static readonly string[] VanillaFields =
	{
		"IsaClient.Rendering.Cosmetics:ShimmerWalkParticles,DeathParticleCosmeticEnabled,DeathGoreCosmeticEnabled,DeathSoundCosmeticEnabled,DeathCosmeticsForAll,ArmorShadowCosmetic,ArmorOutlineCosmetic,RgbPlayerOutline,RgbNpcIcons,RgbBossIcons,WalkParticleType",
		"IsaClient.Rendering.WorldView:UseCrt,UseGameBoy,NoBloodMoonShader,NoMoonLordShader,ServerSideCursor,RenderOtherWireKites",
		"IsaClient.Net.Pipe.PipeNet:Enabled"
	};

	/// <summary>Panel sections that are cheats (NoCheats hides their pages and greys their cards everywhere).</summary>
	private static readonly HashSet<string> CheatSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"player", "combat", "movement", "items", "building", "world", "bosses", "bots", "experimental", "more"
	};

	/// <summary>Command sections that stay usable with NoCheats.</summary>
	private static readonly HashSet<CommandSection> AllowedSections = new HashSet<CommandSection>
	{
		CommandSection.General, CommandSection.Render, CommandSection.Info, CommandSection.Chat, CommandSection.Music
	};

	private static List<FieldInfo> _cheatFields;

	private static List<FieldInfo> _vanillaFields;

	private static List<FieldInfo> Resolve(string[] spec)
	{
		List<FieldInfo> fields = new List<FieldInfo>();
		Assembly assembly = typeof(ServerModes).Assembly;
		foreach (string line in spec)
		{
			string[] parts = line.Split(':');
			Type type = assembly.GetType(parts[0], throwOnError: false);
			if (type == null)
			{
				Console.WriteLine("[ServerModes] no type " + parts[0]);
				continue;
			}
			foreach (string name in parts[1].Split(','))
			{
				FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				if (field == null || field.IsInitOnly || field.IsLiteral || (field.FieldType != typeof(bool) && field.FieldType != typeof(int) && field.FieldType != typeof(float)))
				{
					// renamed or not a switch: skipped, never fatal
					Console.WriteLine("[ServerModes] no switch " + parts[0] + "." + name);
					continue;
				}
				fields.Add(field);
			}
		}
		return fields;
	}

	private static object Off(FieldInfo field)
	{
		if (field.FieldType == typeof(bool))
		{
			return false;
		}
		if (field.FieldType == typeof(int))
		{
			return 0;
		}
		return 0f;
	}

	#endregion

	#region The kept values

	/// <summary>The values the switches had before the server limited them (put back when leaving).</summary>
	private static readonly Dictionary<FieldInfo, object> _kept = new Dictionary<FieldInfo, object>();

	private static List<IsaClient.Plugins.PluginEntry> _suspendedPlugins = new List<IsaClient.Plugins.PluginEntry>();

	/// <summary>For the settings file: the value to save for a switch the server turned off (your own, not the forced one).</summary>
	public static bool TryGetKept(FieldInfo field, out object value)
	{
		return _kept.TryGetValue(field, out value);
	}

	/// <summary>Turns off the switches of the current mode and puts back the ones it no longer limits.</summary>
	private static void ApplyFields()
	{
		if (_cheatFields == null)
		{
			_cheatFields = Resolve(CheatFields);
			_vanillaFields = Resolve(VanillaFields);
		}
		HashSet<FieldInfo> wanted = new HashSet<FieldInfo>();
		if (CheatsBlocked)
		{
			wanted.UnionWith(_cheatFields);
		}
		if (ClientBlocked)
		{
			wanted.UnionWith(_vanillaFields);
		}
		foreach (FieldInfo field in new List<FieldInfo>(_kept.Keys))
		{
			if (!wanted.Contains(field))
			{
				Restore(field);
			}
		}
		foreach (FieldInfo field in wanted)
		{
			if (!_kept.ContainsKey(field))
			{
				try
				{
					_kept[field] = field.GetValue(null);
				}
				catch (Exception ex)
				{
					ClientLog.LogException(ex);
				}
			}
		}
		Enforce(quiet: true);
	}

	private static void Restore(FieldInfo field)
	{
		try
		{
			field.SetValue(null, _kept[field]);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		_kept.Remove(field);
	}

	/// <summary>Every frame while limited: whatever was turned back on goes off again. True when something was.</summary>
	private static bool Enforce(bool quiet)
	{
		bool changed = false;
		foreach (FieldInfo field in _kept.Keys)
		{
			try
			{
				object off = Off(field);
				if (!Equals(field.GetValue(null), off))
				{
					field.SetValue(null, off);
					changed = true;
				}
			}
			catch
			{
			}
		}
		// switches that need their own off (they undo something)
		if (CheatsBlocked)
		{
			if (IsaClient.Gameplay.AutoClicker.Enabled)
			{
				IsaClient.Gameplay.AutoClicker.SetEnabled(false);
				changed = true;
			}
			if (IsaClient.Gameplay.PlayerTools.GhostCheat)
			{
				IsaClient.Gameplay.PlayerTools.ToggleGhost();
				changed = true;
			}
			if (IsaClient.Plugins.PluginLoader.AnyLoaded)
			{
				_suspendedPlugins.AddRange(IsaClient.Plugins.PluginLoader.UnloadAllForServer());
				changed = true;
			}
		}
		// what lets you see other players' inventories or edit your own character closes
		if (CheatsBlocked && CloseCheatWindows())
		{
			changed = true;
		}
		if (ClientBlocked && IsaClient.Menu.InputState.TildeTerminal)
		{
			IsaClient.Menu.InputState.TildeTerminal = false;
			changed = true;
		}
		if (changed && !quiet)
		{
			Notice("That's not allowed on this server.");
		}
		return changed;
	}

	/// <summary>Leaving the server (or the server lifted the limits): everything back as it was.</summary>
	private static void ReleaseAll()
	{
		foreach (FieldInfo field in new List<FieldInfo>(_kept.Keys))
		{
			Restore(field);
		}
		if (_suspendedPlugins.Count > 0)
		{
			List<IsaClient.Plugins.PluginEntry> plugins = _suspendedPlugins;
			_suspendedPlugins = new List<IsaClient.Plugins.PluginEntry>();
			try
			{
				IsaClient.Plugins.PluginLoader.LoadAgain(plugins);
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		}
	}

	#endregion

	#region Entry points (commands, panel)

	/// <summary>A command (".god", ".i 1") the server doesn't allow: true = don't run it (the notice is shown).</summary>
	public static bool BlocksCommand(string name)
	{
		if (!CheatsBlocked)
		{
			return false;
		}
		bool blocked = ClientBlocked;
		if (!blocked)
		{
			// the commands are registered on first use: a first command could be checked before
			ChatCommands.EnsureRegistered();
			CommandSection section = CommandRegistry.FindHelp(name ?? "")?.Section;
			blocked = section == null || !AllowedSections.Contains(section);
		}
		if (blocked)
		{
			// the panel command stays: with the classic panel on, the new one shows while cheats are off
			if (!ClientBlocked && (name == "menu" || name == "ui" || name == "help"))
			{
				return false;
			}
			Notice(ClientBlocked ? "IsaClient commands are off on this server." : "That command is not allowed on this server.");
		}
		return blocked;
	}

	/// <summary>A typed line that is a client command the server doesn't allow (it is neither run nor sent to the chat).</summary>
	public static bool BlocksTypedCommand(string line)
	{
		if (!CheatsBlocked || string.IsNullOrEmpty(line) || line.Length < 2 || line[0] != ChatCommands.CommandSpecifier)
		{
			return false;
		}
		string name = line.Substring(1).Split(' ')[0].ToLowerInvariant();
		return BlocksCommand(name);
	}

	/// <summary>A panel section the server doesn't allow.</summary>
	public static bool BlocksHubSection(string id)
	{
		return CheatsBlocked && id != null && CheatSections.Contains(id);
	}

	/// <summary>Pages of allowed sections that are cheats (section:page title).</summary>
	private static readonly HashSet<string> CheatPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"multiplayer:Players"
	};

	/// <summary>A panel page the server doesn't allow (its whole section, or that page).</summary>
	public static bool BlocksHubPage(string section, string page)
	{
		return BlocksHubSection(section) || (CheatsBlocked && section != null && page != null && CheatPages.Contains(section + ":" + page));
	}

	/// <summary>
	/// Other players' inventories, the player view and My Character (inventory, life, buffs): true = don't open it
	/// (the notice is shown).
	/// </summary>
	public static bool BlocksTool()
	{
		if (!CheatsBlocked)
		{
			return false;
		}
		Notice("That's not allowed on this server.");
		return true;
	}

	/// <summary>Closes the inventory viewer, the classic character window and My Character if one is open.</summary>
	private static bool CloseCheatWindows()
	{
		bool closed = false;
		try
		{
			if (IsaClient.Hub.HubCharacterEditor.IsOpen)
			{
				IsaClient.Hub.HubCharacterEditor.Dismiss();
				closed = true;
			}
			Terraria.UI.UserInterface ui = IsaClient.Menu.MenuState.CustomUI2;
			if (ui != null && (ui.CurrentState == IsaClient.Menu.InventoryViewer.Instance || ui.CurrentState == IsaClient.Menu.CharacterWindow.Instance))
			{
				ui.SetState(IsaClient.Menu.CheatPanel.UiStateNewCheatPanel);
				closed = true;
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogOnce(ex);
		}
		return closed;
	}

	private static double _lastNotice = -1000.0;

	private static void Notice(string text)
	{
		if (Main.timeForVisualEffects - _lastNotice < 240.0)
		{
			return;
		}
		_lastNotice = Main.timeForVisualEffects;
		ClientChat.Notice(text);
	}

	#endregion

	#region Every frame

	/// <summary>ClientLifecycle.OnUpdate: forgets the mode when you leave, keeps the limits while you are there.</summary>
	public static void Update()
	{
		try
		{
			// only a real disconnect (joining shows menus for a moment, the mode is already set then)
			if (Mode != IsaMode.None && Main.netMode != 1)
			{
				Mode = IsaMode.None;
				ServerNote = "";
				ReleaseAll();
				ServerPopups.Clear();
				return;
			}
			if (CheatsBlocked)
			{
				Enforce(quiet: false);
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	#endregion

	#region The packets

	/// <summary>NetManager.Read: true when the module is this protocol's (it's never passed to the game).</summary>
	public static bool TryRead(ushort module, BinaryReader reader)
	{
		if (module != Module)
		{
			return false;
		}
		try
		{
			if (Main.netMode != 1)
			{
				return true;
			}
			for (int i = 0; i < Magic.Length; i++)
			{
				if (reader.ReadByte() != Magic[i])
				{
					return true;
				}
			}
			byte type = reader.ReadByte();
			if (type == Ask)
			{
				Send(Hello, w =>
				{
					w.Write(ClientInfo.FullVersion);
					w.Write((byte)IsaMode.Vanilla);
				});
			}
			else if (type == SetMode)
			{
				byte mode = reader.ReadByte();
				string note = reader.ReadString();
				Apply((IsaMode)Math.Min((byte)3, mode), note);
			}
			else if (type == Popup)
			{
				string title = reader.ReadString();
				string body = reader.ReadString();
				byte style = reader.ReadByte();
				short icon = reader.ReadInt16();
				ushort seconds = reader.ReadUInt16();
				ServerPopups.Show(title, body, (PopupStyle)Math.Min((byte)3, style), icon, seconds);
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		return true;
	}

	private static void Apply(IsaMode mode, string note)
	{
		IsaMode before = Mode;
		bool closeWindows = mode == IsaMode.Vanilla && before != IsaMode.Vanilla;
		Mode = mode;
		ServerNote = (note ?? "").Trim();
		if (ServerNote.Length > 200)
		{
			ServerNote = ServerNote.Substring(0, 200);
		}
		if (mode < IsaMode.NoCheats)
		{
			ReleaseAll();
		}
		else
		{
			ApplyFields();
		}
		if (closeWindows)
		{
			CloseWindows();
		}
		Send(Ack, w => w.Write((byte)mode));
		if (mode != before)
		{
			ShowModeCard(mode, before);
		}
	}

	/// <summary>The card that tells what the server's mode means (instead of a line in the chat).</summary>
	private static void ShowModeCard(IsaMode mode, IsaMode before)
	{
		string title;
		string body;
		PopupStyle style;
		int icon;
		switch (mode)
		{
		case IsaMode.Detect:
			title = IsaClient.Languages.L10n.T("This server knows you use IsaClient");
			body = IsaClient.Languages.L10n.T("- Nothing is limited here: play as usual") + "\n" + IsaClient.Languages.L10n.T("- The server's staff can see your client and its version");
			style = PopupStyle.Info;
			icon = Terraria.ID.ItemID.Binoculars;
			break;
		case IsaMode.NoCheats:
			title = IsaClient.Languages.L10n.T("Cheats are off on this server");
			body = IsaClient.Languages.L10n.T("- God mode, flight, noclip and teleports") + "\n" + IsaClient.Languages.L10n.T("- Items, the Item Editor, building and world tools") + "\n" + IsaClient.Languages.L10n.T("- Aim, kill aura, vision tools and fullbright") + "\n" + IsaClient.Languages.L10n.T("- Plugins (TerraAngel too)") + "\n\n" + IsaClient.Languages.L10n.T("Chat, music, games and the rest of IsaClient work as always. Everything comes back when you leave.");
			style = PopupStyle.Warning;
			icon = Terraria.ID.ItemID.ChestLock;
			break;
		case IsaMode.Vanilla:
			title = IsaClient.Languages.L10n.T("IsaClient is off on this server");
			body = IsaClient.Languages.L10n.T("- No commands, panel or plugins") + "\n" + IsaClient.Languages.L10n.T("- The chat, your looks and the pipe are the game's own") + "\n\n" + IsaClient.Languages.L10n.T("You play the vanilla game here. Everything comes back when you leave.");
			style = PopupStyle.Danger;
			icon = Terraria.ID.ItemID.ChestLock;
			break;
		default:
			if (before < IsaMode.NoCheats)
			{
				return;
			}
			title = IsaClient.Languages.L10n.T("This server lifted its limits");
			body = IsaClient.Languages.L10n.T("IsaClient works fully again.");
			style = PopupStyle.Success;
			icon = Terraria.ID.ItemID.GoldenKey;
			break;
		}
		// once per server: coming back to the same server (same mode and message) shows nothing
		if (!ServerPopups.FirstModeCard((int)mode + "|" + ServerNote))
		{
			return;
		}
		if (ServerNote.Length > 0)
		{
			body += "\n\n[c/9AA7BE:" + IsaClient.Languages.L10n.T("The server says:") + "] " + ServerNote.Replace("]", "");
		}
		ServerPopups.Show(title, body, style, icon);
	}

	private static void CloseWindows()
	{
		void Try(Action close)
		{
			try
			{
				close();
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		}
		Try(() => { if (IsaClient.Casino.CasinoWindow.IsOpen) { IsaClient.Casino.CasinoWindow.Close(); } });
		Try(() => { if (IsaClient.Gba.PokemonWindow.IsOpen) { IsaClient.Gba.PokemonWindow.Close(); } });
		Try(() => { if (IsaClient.Psp.PspWindow.IsOpen) { IsaClient.Psp.PspWindow.Close(); } });
		Try(() => { if (IsaClient.Games.Snake.IsOpen) { IsaClient.Games.Snake.Close(); } });
		Try(() => { if (IsaClient.Games.Tetris.IsOpen) { IsaClient.Games.Tetris.Close(); } });
		Try(() => { if (IsaClient.Video.VideoPlayer.HasVideo) { IsaClient.Video.VideoPlayer.Close(); } });
	}

	private static void Send(byte type, Action<BinaryWriter> body)
	{
		try
		{
			if (Netplay.Connection?.Socket == null || !Netplay.Connection.Socket.IsConnected())
			{
				return;
			}
			MemoryStream stream = new MemoryStream();
			BinaryWriter w = new BinaryWriter(stream);
			w.Write((ushort)0);
			w.Write((byte)82);
			w.Write(Module);
			w.Write(Magic);
			w.Write(type);
			body?.Invoke(w);
			w.Flush();
			byte[] packet = stream.ToArray();
			BitConverter.GetBytes((ushort)packet.Length).CopyTo(packet, 0);
			Netplay.Connection.Socket.AsyncSend(packet, 0, packet.Length, Netplay.Connection.ClientWriteCallBack);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	#endregion
}
