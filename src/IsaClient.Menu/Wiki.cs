using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.OS;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.Localization;
using IsaClient.Core;

namespace IsaClient.Menu;

/// <summary>Shift+click wiki lookup of items, NPCs and tiles.</summary>
public static class Wiki
{
	public const int WikiCursorId = 1000;

	[Setting("ShiftWikiLookup")]
	public static bool ShiftWikiLookup = true;

	/// <summary>Hooked in ItemSlot.OverrideHover: holding Shift over an item shows the wiki cursor.</summary>
	public static bool TryWikiHover(Item item)
	{
		if (!ShiftWikiLookup || item == null || item.IsAir || !Main.playerInventory || Main.drawingPlayerChat || InputState.PlayerInputWritingText())
		{
			return false;
		}
		if (!Terraria.GameInput.PlayerInput.Triggers.Current.KeyStatusHandler("Wiki Lookup"))
		{
			return false;
		}
		Main.cursorOverride = WikiCursorId;
		return true;
	}

	/// <summary>Hooked in ItemSlot.OverrideLeftClick.</summary>
	public static bool TryWikiClick(Item item)
	{
		if (Main.cursorOverride != WikiCursorId || item == null || item.IsAir)
		{
			return false;
		}
		OpenWiki(item.type);
		return true;
	}

	public static void OpenWiki(int itemType)
	{
		string name = Lang.GetItemNameValue(itemType);
		string lang = Language.ActiveCulture.CultureInfo.TwoLetterISOLanguageName;
		string prefix = (new string[8] { "de", "es", "fr", "it", "pl", "pt", "ru", "zh" }.Contains(lang) ? ("/" + lang) : "");
		string url = "https://terraria.wiki.gg" + prefix + "/wiki/Special:Search?search=" + Uri.EscapeDataString(name) + "&go=Go";
		SoundEngine.PlaySound(12);
		try
		{
			Platform.Get<IPathService>().OpenURL(url);
		}
		catch (Exception ex)
		{
			ClientChat.Error("Could not open the wiki: " + ex.Message);
		}
	}

	/// <summary>Drawn instead of the vanilla cursor while the wiki cursor is active.</summary>
	public static void DrawWikiCursor(SpriteBatch spriteBatch)
	{
		// The "?" of Terraria's Confused emote (emote sheet Extra_48), gently switching between its two frames.
		Texture2D sheet = TextureAssets.Extra[48].Value;
		int emote = Terraria.GameContent.UI.EmoteID.EmoteConfused;
		int frame = ((int)(Main.timeForVisualEffects / 30.0) % 2 == 0) ? 0 : 1;
		Rectangle cell = sheet.Frame(8, Terraria.GameContent.UI.EmoteBubble.EMOTE_SHEET_VERTICAL_FRAMES, emote * 2 % 8 + frame, 1 + emote / 4);
		Rectangle mark = new Rectangle(cell.X + 8, cell.Y + 2, 14, 18);
		float scale = 1.25f * Main.cursorScale;
		Vector2 position = new Vector2(Main.mouseX, Main.mouseY);
		spriteBatch.Draw(sheet, position + new Vector2(2f, 2f), mark, Color.Black * 0.45f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
		spriteBatch.Draw(sheet, position, mark, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
	}
}
