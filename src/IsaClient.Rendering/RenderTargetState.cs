using System;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;

namespace IsaClient.Rendering;

/// <summary>
/// Whether a render target can be drawn to. While the window is resized or the graphics device is lost, XNA releases the
/// targets' native textures and makes them again after the reset; a target in between is not disposed but has no
/// texture, and drawing to it throws. The game's on-request targets (map, chat and NPC heads) wait for a working device
/// and make a new target instead of using one without its texture.
/// </summary>
public static class RenderTargetState
{
	private static readonly FieldInfo NativeTexture = typeof(Texture2D).GetField("pComPtr", BindingFlags.Instance | BindingFlags.NonPublic);

	public static bool DeviceReady(GraphicsDevice device)
	{
		if (device != null && !device.IsDisposed)
		{
			return device.GraphicsDeviceStatus == GraphicsDeviceStatus.Normal;
		}
		return false;
	}

	public static unsafe bool Usable(RenderTarget2D target)
	{
		if (target == null || target.IsDisposed)
		{
			return false;
		}
		if (NativeTexture == null)
		{
			return true;
		}
		try
		{
			return Pointer.Unbox(NativeTexture.GetValue(target)) != null;
		}
		catch (Exception)
		{
			return true;
		}
	}
}
