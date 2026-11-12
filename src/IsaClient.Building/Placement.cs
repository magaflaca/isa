using Terraria;
using Terraria.ID;
using IsaClient.Core;

namespace IsaClient.Building;

/// <summary>Placement tweaks: place anywhere, forced paint/coating, paint-only filters, protected walls, air blending.</summary>
public static class Placement
{
	/// <summary>World edit: selection erasing also removes dungeon / Lihzahrd walls the game protects (Skeletron / Golem alive).</summary>
	[Setting("ForceProtectedWalls")]
	public static bool ForceProtectedWalls;

	public static bool BuilderMenu;

	[Setting("PlaceAnywhere", Remembered = true)]
	public static bool PlaceAnywhere;

	public static bool PlaceAnywhereTileSync;

	[Setting("SpecialPaintDeep", Remembered = true)]
	public static bool RainbowPaintDeep;

	[Setting("SpecialPaintPattern", Remembered = true, Min = 0, Max = 16)]
	public static int RainbowPaintType = 0;

	public static short PaintOnlyTile = -1;

	public static short PaintOnlyWall = 0;

	[Setting("PasteModeAirBlending", Remembered = true)]
	public static bool BlendAirTileMode;

	public static byte PaintOverride = 0;

	public static byte PaintCoatOverride = 0;

	public static int TileLoopCount;

	public static int TileSafetyIterations = 450;

	/// <summary>Walls the game refuses to break: dungeon walls before Skeletron, Lihzahrd brick wall before Golem.</summary>
	public static bool IsProtectedWall(int wall)
	{
		return (wall < Main.wallDungeon.Length && Main.wallDungeon[wall] && !NPC.downedBoss3) || (wall == 87 && !NPC.downedGolemBoss);
	}

	public static ushort ConvertUnsafeWall(ushort i)
	{
		return i switch
		{
			2 => 16, 
			3 => 246, 
			13 => 177, 
			14 => 20, 
			15 => 247, 
			28 => 248, 
			40 => 249, 
			48 => 250, 
			49 => 251, 
			50 => 252, 
			51 => 253, 
			52 => 254, 
			53 => 255, 
			54 => 256, 
			55 => 257, 
			56 => 258, 
			57 => 259, 
			58 => 260, 
			59 => 261, 
			61 => 262, 
			63 => 66, 
			64 => 67, 
			65 => 68, 
			69 => 264, 
			70 => 265, 
			71 => 266, 
			79 => 267, 
			80 => 74, 
			81 => 268, 
			83 => 269, 
			86 => 108, 
			170 => 270, 
			171 => 271, 
			178 => 183, 
			180 => 184, 
			185 => 274, 
			188 => 276, 
			189 => 277, 
			190 => 278, 
			191 => 279, 
			192 => 280, 
			193 => 281, 
			194 => 282, 
			195 => 283, 
			196 => 284, 
			197 => 285, 
			198 => 286, 
			199 => 287, 
			200 => 288, 
			201 => 289, 
			202 => 290, 
			203 => 291, 
			204 => 292, 
			205 => 293, 
			206 => 294, 
			207 => 295, 
			208 => 296, 
			209 => 297, 
			210 => 298, 
			211 => 299, 
			212 => 300, 
			213 => 301, 
			214 => 302, 
			215 => 303, 
			217 => 305, 
			218 => 306, 
			219 => 307, 
			220 => 308, 
			221 => 309, 
			222 => 310, 
			244 => 78, 
			272 => 183, 
			273 => 184, 
			_ => i, 
		};
	}

	#region Chat commands

	/// <summary>.plb</summary>
	internal static void PlacementBypassCommand(CommandContext command)
	{
		PlaceAnywhere = !PlaceAnywhere;
		ClientChat.Info("Placement bypass " + (PlaceAnywhere ? "en" : "dis") + "abled.");
		ClientSettings.Save();
	}

	/// <summary>.forcepaint / .fpaint</summary>
	internal static void ForcePaintCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			byte result = 0;
			if (!byte.TryParse(command.Parts[1], out result) || result >= PaintID.Count)
			{
				ClientChat.Error("Invalid Paint ID.");
				return;
			}
			PaintOverride = result;
			if (PaintCoatOverride != 0 && PaintOverride != 0)
			{
				PaintCoatOverride = 0;
				ClientChat.Info("Paint coating override disabled.");
			}
			if (PaintOverride != 0)
			{
				ClientChat.Info("Paint override set to " + PaintID.Search.GetName((int)result) + ".");
			}
			else
			{
				ClientChat.Info("Paint override disabled.");
			}
		}
		else
		{
			ClientChat.Error("Value Required.");
		}
	}

	/// <summary>.forcepaintcoat / .fpaintcoat</summary>
	internal static void ForcePaintCoatCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			byte result2 = 0;
			if (!byte.TryParse(command.Parts[1], out result2) || result2 >= PaintCoatingID.Count)
			{
				ClientChat.Error("Invalid Paint Coating ID.");
				return;
			}
			PaintCoatOverride = result2;
			if (PaintOverride != 0 && PaintCoatOverride != 0)
			{
				PaintOverride = 0;
				ClientChat.Info("Paint override disabled.");
			}
			if (PaintCoatOverride != 0)
			{
				ClientChat.Info("Paint coat override set to " + PaintCoatingID.Search.GetName((int)result2) + ".");
			}
			else
			{
				ClientChat.Info("Paint coating override disabled.");
			}
		}
		else
		{
			ClientChat.Error("Value Required.");
		}
	}

	/// <summary>.tstest</summary>
	internal static void TileSyncTestCommand(CommandContext command)
	{
		PlaceAnywhereTileSync = !PlaceAnywhereTileSync;
		ClientChat.Info("Test tile syncing " + (PlaceAnywhereTileSync ? "enabled." : "disabled."));
	}

	/// <summary>.paintonlytile / .onlypainttile / .opt</summary>
	internal static void PaintOnlyTileCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (short.TryParse(command.Parts[1], out var result46) && result46 < TileID.Count && result46 >= -1)
			{
				PaintOnlyTile = result46;
				if (result46 == -1)
				{
					ClientChat.Info("Super Paint Brush will no longer paint a specific tile.");
					return;
				}
				ClientChat.Info($"Super Paint Brush will only paint the {TileID.Search.GetName((int)result46)} tile. (ID: {result46}).");
			}
			else
			{
				ClientChat.Error("Invalid Tile ID.");
			}
		}
		else if (PaintOnlyTile == -1)
		{
			ClientChat.Info("No tile target to paint.");
		}
		else
		{
			ClientChat.Info($"Current tile to paint: {TileID.Search.GetName((int)PaintOnlyTile)} (ID: {PaintOnlyTile}).");
		}
	}

	/// <summary>.paintonlywall / .onlypaintwall / .opw</summary>
	internal static void PaintOnlyWallCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (short.TryParse(command.Parts[1], out var result38) && result38 < WallID.Count && result38 >= 0)
			{
				PaintOnlyWall = result38;
				if (result38 == 0)
				{
					ClientChat.Info("Super Paint Brush will no longer paint a specific wall.");
					return;
				}
				ClientChat.Info($"Super Paint Brush will only paint the {WallID.Search.GetName((int)result38)} wall. (ID: {result38}).");
			}
			else
			{
				ClientChat.Error("Invalid Wall ID.");
			}
		}
		else if (PaintOnlyWall == 0)
		{
			ClientChat.Info("No wall target to paint.");
		}
		else
		{
			ClientChat.Info($"Current wall to paint: {WallID.Search.GetName((int)PaintOnlyWall)} (ID: {PaintOnlyWall}).");
		}
	}

	#endregion
}
