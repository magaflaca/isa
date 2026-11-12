using Terraria;
using IsaClient.Core;

namespace IsaClient.Rendering;

/// <summary>How the world is drawn: forced sights, shaders, invisible things, screen clamp and world border.</summary>
public static class WorldView
{
	[Setting("AlwaysRenderSpelunker", Remembered = true)]
	public static bool ForceSpelunker;

	[Setting("AlwaysRenderHunter", Remembered = true)]
	public static bool ForceHunter;

	[Setting("AlwaysRenderDangersense", Remembered = true)]
	public static bool ForceDangersense;

	[Setting("AlwaysRenderBiomeSight", Remembered = true)]
	public static bool ForceBiomeSight;

	[Setting("AlwaysRenderInvisibleBlocks", Remembered = true)]
	public static bool RenderInvisibleTiles;

	[Setting("RenderOtherWireKites", Remembered = true)]
	public static bool RenderOtherWireKites;

	[Setting("DisablePlayerInvisibility", Remembered = true)]
	public static bool SeeInvisPlayers;

	[Setting("UseCRTShader", Remembered = true)]
	public static bool UseCrt;

	[Setting("UseGameBoyShader", Remembered = true)]
	public static bool UseGameBoy;

	[Setting("DisableBloodMoonShader", Remembered = true)]
	public static bool NoBloodMoonShader;

	[Setting("DisableMoonLordShader", Remembered = true)]
	public static bool NoMoonLordShader;

	[Setting("ParticleCursor", Remembered = true)]
	public static bool ServerSideCursor;

	public static void DisableAll()
	{
		Hitboxes.PlayerHitboxes = false;
		Hitboxes.NpcHitboxes = false;
		Hitboxes.ProjectileHitboxes = false;
		Hitboxes.ItemHitboxes = false;
		Hitboxes.HitBoxesTexture = 0;
		Hitboxes.UseExtraProjHitboxes = false;
		ForceSpelunker = false;
		ForceHunter = false;
		ForceDangersense = false;
		ForceBiomeSight = false;
		RenderInvisibleTiles = false;
		RenderOtherWireKites = false;
		SeeInvisPlayers = false;
		UseCrt = false;
		UseGameBoy = false;
		NoBloodMoonShader = false;
		NoMoonLordShader = false;
		AntiLag.DustLevel = 100;
	}

	/// <summary>PlayerDrawSet.BoringSetup: "Disable Invisibility on Players" draws other players at least 80% visible.</summary>
	public static void RevealInvisiblePlayer(Player player)
	{
		if (!SeeInvisPlayers || player == null || player.whoAmI == Main.myPlayer || player.dead || Main.gameMenu || player.isDisplayDollOrInanimate)
		{
			return;
		}
		player.invis = false;
		if (player.stealth < 0.8f)
		{
			player.stealth = 0.8f;
		}
		if (player.immuneAlpha > 0 && player.immuneAlpha < 51 && !player.immune)
		{
			player.immuneAlpha = 51;
		}
	}

	[Setting("WorldBorderSize", Remembered = true, Min = -2, Max = 40)]
	public static int WorldBorderSize = 40;

	[Setting("DontClampScreen", Remembered = true)]
	public static bool NoScreenClamp;

	[Setting("KeepVanillaBorder", Remembered = true)]
	public static bool KeepThinVanillaBorder;

	[Setting("WorldBorderLighting", Remembered = true, Min = 0, Max = 255)]
	public static int OutsideWorldMapLight = 100;

	/// <summary>World border thickness in pixels (vanilla: 640 = 40 tiles). -1/-2 shrink it to a single tile at the edge.</summary>
	public static int WorldBorderPixels()
	{
		if (WorldBorderSize <= -1)
		{
			return 16;
		}
		return WorldBorderSize * 16 + 16;
	}

	/// <summary>
	/// Tiles at the world's edges the map leaves out (vanilla: 40). A smaller border lets you reach and build there, so the
	/// map follows it.
	/// </summary>
	public static int MapEdgeTiles()
	{
		return System.Math.Max(1, System.Math.Min(40, WorldBorderSize));
	}

	[Setting("InvisibleEnemies", Remembered = true)]
	public static bool InvisibleEnemies;

	/// <summary>Hooked in Main.DrawNPC: hides hostile NPCs when Invisible Enemies is on.</summary>
	public static bool HideNPC(NPC npc)
	{
		if (!InvisibleEnemies || npc == null || npc.friendly || npc.townNPC || npc.isLikeATownNPC || npc.CountsAsACritter)
		{
			return false;
		}
		return !npc.boss;
	}

	#region Chat commands

	/// <summary>.invisenemies / .invisibleenemies</summary>
	internal static void InvisibleEnemiesCommand(CommandContext command)
	{
		InvisibleEnemies = !InvisibleEnemies;
		ClientChat.Info("Invisible Enemies " + (InvisibleEnemies ? "En" : "Dis") + "abled");
	}

	/// <summary>.wirekites</summary>
	internal static void WireKitesCommand(CommandContext command)
	{
		RenderOtherWireKites = !RenderOtherWireKites;
		ClientChat.Info("Rendering other's wire kite projectiles " + (RenderOtherWireKites ? "en" : "dis") + "abled");
	}

	/// <summary>.noinvis</summary>
	internal static void RevealInvisiblePlayersCommand(CommandContext command)
	{
		SeeInvisPlayers = !SeeInvisPlayers;
		ClientChat.Info(SeeInvisPlayers ? "You now see invisible players." : "You no longer see invisible players.");
	}

	/// <summary>.crt / .crtshader</summary>
	internal static void CrtShaderCommand(CommandContext command)
	{
		UseCrt = !UseCrt;
		ClientChat.Info("Using CRT Shader " + (UseCrt ? "en" : "dis") + "abled.");
	}

	/// <summary>.gameboy</summary>
	internal static void GameBoyCommand(CommandContext command)
	{
		UseGameBoy = !UseGameBoy;
		ClientChat.Info("GameBoy Shader " + (UseGameBoy ? "en" : "dis") + "abled.");
	}

	/// <summary>.seeinvis</summary>
	internal static void SeeInvisibleTilesCommand(CommandContext command)
	{
		RenderInvisibleTiles = !RenderInvisibleTiles;
		ClientChat.Info("Always visible echo tiles " + (RenderInvisibleTiles ? "en" : "dis") + "abled.");
	}

	/// <summary>.mapoutsidelight / .mpol</summary>
	internal static void MapOutsideLightCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (byte.TryParse(command.Parts[1], out var light))
			{
				OutsideWorldMapLight = light;
				ClientChat.Info("Map lighting for tiles outside the normal world border set to " + OutsideWorldMapLight);
			}
			else
			{
				ClientChat.Error("Invalid light amount.");
			}
		}
		else
		{
			ClientChat.Info("Current light amount: " + OutsideWorldMapLight);
		}
	}

	#endregion
}
