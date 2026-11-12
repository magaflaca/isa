using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace IsaClient.Menu;

/// <summary>
/// The small clickable icons of the main menu overlay (credits row, version and proxy buttons) and the music note: every
/// icon is scaled to fit the same <see cref="Size"/> box, so items and textures of any size look alike.
/// </summary>
public static class MenuIcon
{
	/// <summary>The largest side of every icon, in UI pixels.</summary>
	public const float Size = 20f;

	/// <summary>Hovered icons grow a little.</summary>
	private const float HoverScale = 1.12f;

	/// <summary>Draws an item's picture centered on <paramref name="center"/>; returns true while the mouse is over it.</summary>
	public static bool DrawItem(SpriteBatch spriteBatch, int itemId, Vector2 center, Point mouse, Color color)
	{
		Main.instance.LoadItem(itemId);
		Texture2D texture = TextureAssets.Item[itemId].Value;
		Rectangle frame = (Main.itemAnimations[itemId] != null) ? Main.itemAnimations[itemId].GetFrame(texture) : texture.Frame();
		bool hover = Contains(TextureBox(texture, frame, center), new Vector2(mouse.X, mouse.Y));
		DrawTexture(spriteBatch, texture, frame, center, color, hover);
		return hover;
	}

	/// <summary>Draws a texture (or a frame of it) fitted to <see cref="Size"/>, centered.</summary>
	public static void DrawTexture(SpriteBatch spriteBatch, Texture2D texture, Rectangle? frame, Vector2 center, Color color, bool hover, float size = Size)
	{
		Rectangle source = frame ?? texture.Frame();
		float scale = FitScale(source, size) * (hover ? HoverScale : 1f);
		spriteBatch.Draw(texture, center, source, color, 0f, source.Size() / 2f, scale, SpriteEffects.None, 0f);
	}

	/// <summary>The screen box the fitted texture takes.</summary>
	public static Rectangle TextureBox(Texture2D texture, Rectangle? frame, Vector2 center, float fitSize = Size)
	{
		Vector2 size = (frame ?? texture.Frame()).Size() * FitScale(frame ?? texture.Frame(), fitSize);
		return new Rectangle((int)(center.X - size.X / 2f), (int)(center.Y - size.Y / 2f), (int)Math.Ceiling(size.X), (int)Math.Ceiling(size.Y));
	}

	/// <summary>True when the point is inside the box, with a couple of pixels of slack for small icons.</summary>
	public static bool Contains(Rectangle box, Vector2 point)
	{
		box.Inflate(2, 2);
		return box.Contains((int)point.X, (int)point.Y);
	}

	private static float FitScale(Rectangle source, float size = Size)
	{
		return size / Math.Max(source.Width, source.Height);
	}
}
