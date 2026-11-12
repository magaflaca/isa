using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using IsaClient.Community;
using IsaClient.Core;
using IsaClient.WorldTools;

namespace IsaClient.Movement;

/// <summary>Free camera: moves the view without moving your player.</summary>
public static class FreeCamera
{
	[Setting("FreeCameraSpeed", Min = 1f, Max = 500f)]
	public static float FreeCamSpeed = 25f;

	public static bool FreeCam;

	public static bool FreeCam2;

	public static bool FreeCamDebug;

	public static Vector2 SavedFreeCam;

	private static int _freeCamSectionCooldown;

	public static Vector2 FreeCamScreenPosition(Vector2 playerScreenPosition)
	{
		if (Spectate.IsSpectating && !FreeCam)
		{
			// Spectating: the camera sits on the spectated player like it would on you.
			Player target = Main.player[Spectate.SpectatePlayerTarget];
			SavedFreeCam = target.Bottom + new Vector2(0f, target.gfxOffY - 21f) + new Vector2(Main.cameraX, Main.cameraY) - Main.Camera.UnscaledSize * 0.5f;
			return SavedFreeCam;
		}
		if (!FreeCam || Main.gameMenu)
		{
			SavedFreeCam = playerScreenPosition;
			return playerScreenPosition;
		}
		// Moves like noclip: the player's own movement keys, .freecam <speed> sets the base speed,
		// Hold for Rapidfire (LeftControl) slows it down and Hold for Activator (Map) (LeftShift) speeds it up.
		float speed = FreeCamSpeed;
		TriggersSet current = PlayerInput.Triggers.Current;
		if (current.KeyStatusHandler("Hold for Rapidfire"))
		{
			speed *= 0.35f;
		}
		else if (current.KeyStatusHandler("Hold for Activator (Map)"))
		{
			speed *= 2.5f;
		}
		if (!Main.gamePaused && !Main.drawingPlayerChat && !Main.ingameOptionsWindow && !Main.editSign && !Main.editChest && !PlayerInput.WritingText && Main.hasFocus)
		{
			if (current.Up || current.Jump)
			{
				SavedFreeCam.Y -= speed;
			}
			if (current.Left)
			{
				SavedFreeCam.X -= speed;
			}
			if (current.Right)
			{
				SavedFreeCam.X += speed;
			}
			if (current.Down)
			{
				SavedFreeCam.Y += speed;
			}
		}
		Vector2 min = new Vector2(Main.leftWorld, Main.topWorld);
		Vector2 max = new Vector2(Main.rightWorld - (float)Main.screenWidth / Main.GameViewMatrix.Zoom.X, Main.bottomWorld - (float)Main.screenHeight / Main.GameViewMatrix.Zoom.Y);
		SavedFreeCam = Vector2.Clamp(SavedFreeCam, min, Vector2.Max(min, max));
		if (FreeCamDebug)
		{
			Dust.QuickDust((SavedFreeCam + new Vector2(Main.screenWidth, Main.screenHeight) * 0.5f).ToTileCoordinates(), Color.White);
		}
		return SavedFreeCam;
	}

	// The server only streams sections around the real player, so the free camera requests the ones it looks at,
	// with the same methods as .revmap (RequestSection, or the drone packet on servers that block it).
	internal static void UpdateFreeCamSections()
	{
		if (Main.netMode != 1 || FreeCam2 || Main.sectionManager == null || MapReveal.IsRevealing)
		{
			return;
		}
		if (_freeCamSectionCooldown > 0)
		{
			_freeCamSectionCooldown--;
			return;
		}
		Vector2 center = Spectate.IsSpectating ? Main.player[Spectate.SpectatePlayerTarget].Center : (SavedFreeCam + new Vector2(Main.screenWidth, Main.screenHeight) * 0.5f);
		int cx = Netplay.GetSectionX((int)(center.X / 16f));
		int cy = Netplay.GetSectionY((int)(center.Y / 16f));
		for (int r = 0; r <= 2; r++)
		{
			for (int x = cx - r; x <= cx + r; x++)
			{
				for (int y = cy - r; y <= cy + r; y++)
				{
					if (x >= 0 && y >= 0 && x < Main.maxSectionsX && y < Main.maxSectionsY && !Main.sectionManager.SectionLoaded(x, y))
					{
						MapReveal.RequestSingleSection(x, y);
						_freeCamSectionCooldown = 3;
						return;
					}
				}
			}
		}
	}

	#region Chat commands

	/// <summary>.freecam</summary>
	internal static void FreeCamCommand(CommandContext command)
	{
		if (command.Parts.Length > 1 && float.TryParse(command.Parts[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
		{
			FreeCamSpeed = MathHelper.Clamp(speed, 1f, 500f);
			FreeCam = true;
			ClientChat.Info("Free Camera Enabled  Speed: " + FreeCamSpeed.ToString("0.##", CultureInfo.InvariantCulture));
		}
		else
		{
			FreeCam = !FreeCam;
			ClientChat.Info("Free Camera " + (FreeCam ? "En" : "Dis") + "abled" + (FreeCam ? ("  Speed: " + FreeCamSpeed.ToString("0.##", CultureInfo.InvariantCulture)) : ""));
		}
	}

	/// <summary>.freecam2</summary>
	internal static void FreeCamAltCommand(CommandContext command)
	{
		FreeCam = !FreeCam;
		FreeCam2 = FreeCam;
		ClientChat.Info("Free Camera (No chunk load) " + (FreeCam2 ? "En" : "Dis") + "abled");
	}

	/// <summary>.freecamd</summary>
	internal static void FreeCamDebugCommand(CommandContext command)
	{
		FreeCamDebug = !FreeCamDebug;
		ClientChat.Info("Freecam Debug " + (FreeCamDebug ? "En" : "Dis") + "abled");
	}

	#endregion
}
