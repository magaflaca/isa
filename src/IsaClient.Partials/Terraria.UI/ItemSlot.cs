using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.UI.Chat;
using Terraria.GameInput;
using Terraria.UI.Chat;
using Terraria.UI.Gamepad;

namespace Terraria.UI;

public partial class ItemSlot
{
	/// <summary>
	/// IsaClient (draw as another player): the player whose slots are being drawn, for the Player List inventory viewer and the
	/// spectated inventory. Null means the local player, as in vanilla.
	/// </summary>
	public static Player IsaDrawPlayer;

	private static Player DrawingPlayer => IsaDrawPlayer ?? Main.LocalPlayer;

	/// <summary>Draws a slot as it looks for <paramref name="player"/> (selected hotbar slot, loadout colors, shared items).</summary>
	public static void DrawAsOther(SpriteBatch spriteBatch, Item[] inv, int context, int slot, Vector2 position, Player player, Color lightColor = default(Color))
	{
		Player previous = IsaDrawPlayer;
		IsaDrawPlayer = player;
		try
		{
			Draw(spriteBatch, inv, context, slot, position, lightColor);
		}
		finally
		{
			IsaDrawPlayer = previous;
		}
	}

	public static void DrawAsOther(SpriteBatch spriteBatch, ref Item inv, int context, Vector2 position, Player player, Color lightColor = default(Color))
	{
		Item[] single = new Item[1] { inv };
		DrawAsOther(spriteBatch, single, context, 0, position, player, lightColor);
		inv = single[0];
	}

	/// <summary>IsaClient: Shift on someone else's item shows the "take" cursor (7); the favorite key with chat open shares it (2).</summary>
	public static void OverrideHoverFavoriteAndShift(Item[] inv, int context = 0, int slot = 0)
	{
		Item item = inv[slot];
		if (!PlayerInput.UsingGamepad)
		{
			UILinkPointNavigator.SuggestUsage(GetGamepadPointForSlot(inv, context, slot));
		}
		if (item.IsAir)
		{
			return;
		}
		if (ShiftInUse && Main.LocalPlayer.ItemSpace(item).CanTakeItemToPersonalInventory)
		{
			Main.cursorOverride = 7;
		}
		else if (Main.keyState.IsKeyDown(Main.FavoriteKey) && Main.drawingPlayerChat && canShareAt[context])
		{
			Main.cursorOverride = 2;
		}
	}

	/// <summary>IsaClient: Shift+click copies someone else's item into your own inventory.</summary>
	public static void LeftClickSteal(Item[] inv, int slot)
	{
		if (Main.mouseLeft && Main.mouseLeftRelease && Main.cursorOverride == 7 && !inv[slot].IsAir)
		{
			Main.mouseLeftRelease = false;
			if (!Main.playerInventory)
			{
				Main.playerInventory = true;
				SoundEngine.PlaySound(10);
			}
			Item item = inv[slot].Clone();
			item.favorited = false;
			item.OnCreated(new JourneyDuplicationItemCreationContext());
			Main.LocalPlayer.GetItem(item, GetItemSettings.LootAllFromChest);
			SoundEngine.PlaySound(12);
		}
	}

	public static void LeftClickSteal(ref Item inv)
	{
		Item[] single = new Item[1] { inv };
		LeftClickSteal(single, 0);
	}

	/// <summary>IsaClient: the favorite key with chat open puts the hovered item's tag into the chat line.</summary>
	public static void LeftClickShareInChat(Item item)
	{
		if (Main.mouseLeft && Main.mouseLeftRelease && Main.cursorOverride == 2 && item != null && !item.IsAir)
		{
			Main.mouseLeftRelease = false;
			if (ChatManager.AddChatText(FontAssets.MouseText.Value, ItemTagHandler.GenerateTag(item), Vector2.One))
			{
				SoundEngine.PlaySound(12);
			}
		}
	}

	/// <summary>IsaClient: the slot being drawn is one of the inventory's hotbar row (no item tag there).</summary>
	private static bool _isaHotbarSlot;
}
