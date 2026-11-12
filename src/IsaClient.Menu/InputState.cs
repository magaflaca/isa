using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using IsaClient.Gameplay;
using IsaClient.Core;
using Microsoft.Xna.Framework.Input;

namespace IsaClient.Menu;

/// <summary>Keyboard and cursor state shared by the panel and the chat (open keys, Windows cursor).</summary>
public static class InputState
{
	[Setting("UseWindowsCursor")]
	public static bool NativeCursor;

	internal static bool PlayerInputWritingText()
	{
		return Terraria.GameInput.PlayerInput.WritingText;
	}

	/// <summary>Journey menu: deprecated items (the game no longer gives them) get a blue dot.</summary>
	public static void DrawJourneyExtraItemMarker(SpriteBatch spriteBatch, int itemType, Vector2 slotPosition)
	{
		if (!PlayerCheats.JourneyMenu || itemType <= 0 || itemType >= Terraria.ID.ItemID.Count || !Terraria.ID.ItemID.Sets.Deprecated[itemType])
		{
			return;
		}
		// The blue wire's knot (like the item tags), top-left.
		Vector2 knot = slotPosition + new Vector2(10f, 10f) * Main.inventoryScale;
		IsaClient.Items.TagArt.Draw(spriteBatch, IsaClient.Items.TagArt.Blue, knot, Main.inventoryScale * 1.3f, Color.White);
	}
	public static Keys OpenChatKey = Keys.Enter;

	public static bool TildeTerminal;

	public static bool ControlTildeTerminal;

	public static bool ReleaseTildeTerminal;
}
