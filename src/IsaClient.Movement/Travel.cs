using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using IsaClient.Core;

namespace IsaClient.Movement;

/// <summary>Travel tweaks: surfaces (asphalt, bouncy, friction, conveyors), infinite flight, wing stats, portal physics.</summary>
public static class Travel
{
	[Setting("SurfacesAsphalt", Remembered = true)]
	public static bool AsphaltSurfaces;

	[Setting("SurfacesBouncy", Remembered = true)]
	public static bool BouncySurfaces;

	/// <summary>0 normal, 1 no friction override (no ice), 2 everything icy, 3 everything frictionless.</summary>
	[Setting("SurfacesFrictionType", Remembered = true, Min = 0, Max = 3)]
	public static int SurfaceFrictionMode;

	/// <summary>0 normal, 1 ignored, 2 every floor is a clockwise belt, 3 every floor is a counter-clockwise belt.</summary>
	[Setting("SurfacesConveyerType", Remembered = true, Min = 0, Max = 3)]
	public static int ConveyerBeltMode;

	[Setting("InfiniteFlight", Remembered = true)]
	public static bool InfiniteFlight;

	/// <summary>The Flying Carpet never runs out (Player.CarpetMovement).</summary>
	[Setting("InfiniteCarpetFlight", Remembered = true)]
	public static bool InfiniteCarpet;

	[Setting("AlwaysHoverAndBoost", Remembered = true)]
	public static bool AlwaysHoverable;

	[Setting("MaxWingStats", Remembered = true)]
	public static bool MaxWings;

	[Setting("PortalPhysics", Remembered = true)]
	public static bool PortalPhysics;

	public static bool InfiniteReach;

	public static bool UnlimitedWinds;

	public static void DisableAll()
	{
		AsphaltSurfaces = false;
		BouncySurfaces = false;
		SurfaceFrictionMode = 0;
		ConveyerBeltMode = 0;
		InfiniteFlight = false;
		InfiniteCarpet = false;
		AlwaysHoverable = false;
		MaxWings = false;
		PortalPhysics = false;
		TeleportTools.BypassTeleportRequests = false;
		Noclip.RapidPosUpdate = false;
	}

	/// <summary>Player.WingMovement, local player with "Can Always Hover & Boost Upwards": every wing gets hover stats.</summary>
	public static void AlwaysHoverWingMovement(Player player, ref float num2, ref float num5, ref float num4, ref float num3)
	{
		num2 = 0.5f;
		num5 = 0.1f;
		num4 = 0.5f;
		num3 = 1.66f;
		if (player.TryingToHoverUp)
		{
			player.velocity.Y -= 0.2f * player.gravDir;
			if (player.gravDir == 1f)
			{
				if (player.velocity.Y > 0f)
				{
					player.velocity.Y -= 1f;
				}
				else if (player.velocity.Y > 0f - Player.jumpSpeed)
				{
					player.velocity.Y -= 0.2f;
				}
				if (player.velocity.Y < (0f - Player.jumpSpeed) * 3f)
				{
					player.velocity.Y = (0f - Player.jumpSpeed) * 3f;
				}
			}
			else
			{
				if (player.velocity.Y < 0f)
				{
					player.velocity.Y += 1f;
				}
				else if (player.velocity.Y < Player.jumpSpeed)
				{
					player.velocity.Y += 0.2f;
				}
				if (player.velocity.Y > Player.jumpSpeed * 3f)
				{
					player.velocity.Y = Player.jumpSpeed * 3f;
				}
			}
		}
		else if (player.TryingToHoverDown && !player.controlJump && player.velocity.Y != 0f)
		{
			player.velocity.Y += 0.4f;
		}
	}

	/// <summary>Player.FloorVisuals, local player: Surface Friction and Asphalt Surfaces.</summary>
	public static void ApplySurfaceOverrides(Player player)
	{
		switch (SurfaceFrictionMode)
		{
		case 1:
			player.slippy = false;
			player.slippy2 = false;
			break;
		case 2:
			player.slippy = true;
			player.slippy2 = false;
			break;
		case 3:
			player.slippy = false;
			player.slippy2 = true;
			break;
		}
		if (AsphaltSurfaces)
		{
			player.powerrun = true;
		}
	}

	/// <summary>Collision.StepConveyorBelt, local player: Conveyer Belt Handling.</summary>
	public static int ConveyorDirectionOverride(Player player, Tile tile, int direction)
	{
		switch (ConveyerBeltMode)
		{
		case 1:
			return 0;
		case 2:
		case 3:
		{
			bool solid = Main.tileSolid[tile.type] && !TileID.Sets.Platforms[tile.type];
			bool platform = (Main.tileSolidTop[tile.type] || TileID.Sets.Platforms[tile.type]) && IsPlayerOnGround(player);
			if (solid || platform)
			{
				return (ConveyerBeltMode == 2) ? 1 : (-1);
			}
			return direction;
		}
		default:
			return direction;
		}
	}

	public static bool IsPlayerOnGround(Player plr)
	{
		if (plr == null)
		{
			return false;
		}
		bool fall = false;
		bool fallThrough = plr.controlDown;
		if (plr.gravDir == -1f || (plr.mount.Active && (plr.mount.Cart || plr.mount.Type == 12 || plr.mount.Type == 7 || plr.mount.Type == 8 || plr.mount.Type == 23 || plr.mount.Type == 44 || plr.mount.Type == 48)) || plr.GoingDownWithGrapple || plr.pulley)
		{
			fall = true;
			fallThrough = true;
		}
		Vector2 velocity = Collision.TileCollision(plr.position, plr.velocity, plr.width, plr.height, fallThrough, fall, (int)plr.gravDir);
		return velocity.Y <= plr.gravity && velocity.Y >= 0f - plr.gravity;
	}

	#region Chat commands

	/// <summary>.winds</summary>
	internal static void WindsCommand(CommandContext command)
	{
		UnlimitedWinds = !UnlimitedWinds;
		ClientChat.Info("Unlimited Weather Pain projectiles " + (UnlimitedWinds ? "en" : "dis") + "abled");
	}

	/// <summary>.infinitereach / .infreach</summary>
	internal static void InfiniteReachCommand(CommandContext command)
	{
		InfiniteReach = !InfiniteReach;
		ClientChat.Info("Infinite Reach " + (InfiniteReach ? "En" : "Dis") + "abled");
	}

	#endregion
}
