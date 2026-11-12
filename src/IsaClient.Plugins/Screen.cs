using ImGuiNET;
using Microsoft.Xna.Framework;
using Terraria;

namespace IsaClient.Plugins;

/// <summary>World/screen conversions (screen = ImGui's pixels) for drawing over the world or the full map, like TerraAngel's Util.</summary>
public static class Screen
{
	private static Vector2 DisplaySize => new Vector2(ImGui.GetIO().DisplaySize.X, ImGui.GetIO().DisplaySize.Y);

	public static Vector2 WorldToScreenDynamic(Vector2 worldPoint)
	{
		return Main.mapFullscreen ? WorldToScreenFullscreenMap(worldPoint) : WorldToScreenWorld(worldPoint);
	}

	public static Vector2 ScreenToWorldDynamic(Vector2 screenPoint)
	{
		return Main.mapFullscreen ? ScreenToWorldFullscreenMap(screenPoint) : ScreenToWorldWorld(screenPoint);
	}

	public static Vector2 WorldToScreenWorld(Vector2 worldPosition)
	{
		return Vector2.Transform(worldPosition - Main.screenPosition, Main.GameViewMatrix.ZoomMatrix);
	}

	public static Vector2 ScreenToWorldWorld(Vector2 screenPosition)
	{
		return Vector2.Transform(screenPosition, Matrix.Invert(Main.GameViewMatrix.ZoomMatrix)) + Main.screenPosition;
	}

	public static Vector2 WorldToScreenFullscreenMap(Vector2 worldPoint)
	{
		return (worldPoint / 16f - Main.mapFullscreenPos) * Main.mapFullscreenScale + DisplaySize / 2f;
	}

	public static Vector2 ScreenToWorldFullscreenMap(Vector2 screenPoint)
	{
		return ((screenPoint - DisplaySize / 2f) / Main.mapFullscreenScale + Main.mapFullscreenPos) * 16f;
	}
}
