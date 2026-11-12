using System;
using System.Globalization;
using Microsoft.Xna.Framework;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Rendering;

/// <summary>
/// Render → "Extended Zoom Out": the game zoom can go below Terraria's 100% minimum (down to <see cref="MinZoom"/>) to
/// see much more of the world. Below 100% the world is drawn straight to the screen (no screen-sized render targets),
/// so the extra area is drawn too; tiles and lighting already follow the zoomed camera. Zoom out with the normal
/// zoom keys, or .zoom 25.
/// </summary>
public static class Zoom
{
	public const float LowestZoom = 0.1f;

	[Setting("ExtendedZoomOut")]
	public static bool Enabled;

	[Setting("ExtendedZoomMin", Min = LowestZoom, Max = 1f)]
	public static float MinZoom = 0.35f;

	/// <summary>The smallest GameZoomTarget allowed right now (1 = vanilla).</summary>
	public static float Min => Enabled ? MathHelper.Clamp(MinZoom, LowestZoom, 1f) : 1f;

	/// <summary>The world view is zoomed out past vanilla: draw it without the fixed-size render targets.</summary>
	public static bool ZoomedOut => !Main.gameMenu && Main.GameViewMatrix.Zoom.X < 0.999f;

	public static void SetEnabled(bool on)
	{
		Enabled = on;
		if (on)
		{
			Main.GameZoomTarget = Min;
		}
		else if (Main.GameZoomTarget < 1f)
		{
			Main.GameZoomTarget = 1f;
		}
		ClientSettings.Save();
	}

	/// <summary>.zoom [percent]: sets the game zoom (below 100% turns Extended Zoom Out on).</summary>
	internal static void ZoomCommand(CommandContext command)
	{
		string[] args = command.Parts;
		if (args.Length < 2 || !float.TryParse(args[1].TrimEnd('%').Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
		{
			ClientChat.Notice("Zoom: " + Math.Round(Main.GameZoomTarget * 100f) + "%  (minimum " + Math.Round(Min * 100f) + "%). Usage: .zoom <" + Math.Round(LowestZoom * 100f) + "-200>");
			return;
		}
		float zoom = MathHelper.Clamp(percent / 100f, LowestZoom, 2f);
		if (zoom < 1f)
		{
			Enabled = true;
			if (zoom < MinZoom)
			{
				MinZoom = zoom;
			}
			ClientSettings.Save();
		}
		Main.GameZoomTarget = zoom;
		ClientChat.Notice("Zoom: " + Math.Round(zoom * 100f) + "%");
	}

	#region Chat commands

	#endregion
}
