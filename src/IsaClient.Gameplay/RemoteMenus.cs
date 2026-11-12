using Terraria;
using Terraria.Audio;
using Terraria.UI;
using IsaClient.Core;

namespace IsaClient.Gameplay;

/// <summary>Opens storage and NPC menus from anywhere: piggy bank, safe, forge, void vault, dresser, stylist, guide, reforge.</summary>
public static class RemoteMenus
{
	public static bool ForcedContainer;

	public static bool ForcedClothesWindow;

	public static bool ForcedHairWindow;

	public static bool ForcedGuideCraftingMenu;

	public static bool ForcedReforgeMenu;

	internal static void OpenRemoteContainer(int chest)
	{
		Player player = Main.LocalPlayer;
		ForcedContainer = true;
		player.chest = chest;
		for (int i = 0; i < 40; i++)
		{
			ItemSlot.SetGlow(i, -1f, chest: true);
		}
		player.chestX = (int)(player.Center.X / 16f);
		player.chestY = (int)(player.Center.Y / 16f);
		player.SetTalkNPC(-1);
		Main.SetNPCShopIndex(0);
		Main.playerInventory = true;
		SoundEngine.PlaySound(10);
	}

	#region Chat commands

	/// <summary>.dresser / .clothesmenu / .cltmen</summary>
	internal static void ClothesMenuCommand(CommandContext command)
	{
		ForcedClothesWindow = true;
		ForcedGuideCraftingMenu = false;
		ForcedReforgeMenu = false;
		Main.playerInventory = false;
		command.Player.chest = -1;
		command.Player.SetTalkNPC(-1);
		Main.npcChatCornerItem = 0;
		Main.npcChatText = "";
		Main.interactedDresserTopLeftX = (int)(command.Player.position.X / 16f);
		Main.interactedDresserTopLeftY = (int)(command.Player.position.Y / 16f);
		if (Main.hairWindow)
		{
			Main.CancelHairWindow();
		}
		Main.OpenClothesWindow();
	}

	/// <summary>.stylist / .hairmenu / .hmen</summary>
	internal static void HairMenuCommand(CommandContext command)
	{
		ForcedHairWindow = true;
		ForcedGuideCraftingMenu = false;
		ForcedReforgeMenu = false;
		Main.playerInventory = false;
		command.Player.chest = -1;
		command.Player.SetTalkNPC(-1);
		Main.npcChatCornerItem = 0;
		Main.npcChatText = "";
		if (Main.clothesWindow)
		{
			Main.CancelClothesWindow();
		}
		Main.OpenHairWindow();
	}

	/// <summary>.guide / .guidemenu / .craftingguide / .recipeguide</summary>
	internal static void CraftingGuideCommand(CommandContext command)
	{
		if (Main.clothesWindow)
		{
			Main.CancelClothesWindow();
		}
		if (Main.hairWindow)
		{
			Main.CancelHairWindow();
		}
		ForcedGuideCraftingMenu = true;
		ForcedReforgeMenu = false;
		Main.InReforgeMenu = false;
		Main.NPCChatText_GuideReverseCrafting();
	}

	/// <summary>.reforge / .reforgemenu / .goblin / .tinkerer</summary>
	internal static void ReforgeMenuCommand(CommandContext command)
	{
		if (Main.clothesWindow)
		{
			Main.CancelClothesWindow();
		}
		if (Main.hairWindow)
		{
			Main.CancelHairWindow();
		}
		ForcedGuideCraftingMenu = false;
		ForcedReforgeMenu = true;
		Main.InGuideCraftMenu = false;
		Main.NPCChatText_TinkererReforge();
	}

	/// <summary>.bank / .piggybank</summary>
	internal static void PiggyBankCommand(CommandContext command)
	{
		OpenRemoteContainer(-2);
	}

	/// <summary>.safe</summary>
	internal static void SafeCommand(CommandContext command)
	{
		OpenRemoteContainer(-3);
	}

	/// <summary>.forge / .defenderforge / .defforge</summary>
	internal static void DefenderForgeCommand(CommandContext command)
	{
		OpenRemoteContainer(-4);
	}

	/// <summary>.void / .voidvault / .voidbag</summary>
	internal static void VoidVaultCommand(CommandContext command)
	{
		OpenRemoteContainer(-5);
	}

	#endregion
}
