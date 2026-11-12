using Terraria.Enums;
using Terraria.GameContent;
using Terraria.ID;
using IsaClient.Networking;
using Terraria;

namespace IsaClient.Gameplay;

/// <summary>
/// Client-only items, added after the last 1.4.5.8 item: the Flinxinator Staff, the pre-1.4 Legacy True Excalibur,
/// Legacy True Night's Edge and Legacy Terra Blade (their old beams, no 1.4 swing effects) and the Rubblemaker (Pots).
/// They use the textures of the items they come from, carry a small wire mark on their icon, and are sent to servers
/// as that vanilla item (a server would reject an id it does not know): dropped or stored in a server chest they turn
/// into the vanilla item. Vanilla clients loading a character with them simply see an empty slot.
/// </summary>
public static class LegacyItems
{
	public static bool IsCustom(int type)
	{
		return type >= ItemID.FlinxinatorStaff && type < ItemID.Count;
	}

	/// <summary>The vanilla item a custom item is shown as and sent as.</summary>
	public static int VanillaTwin(int type)
	{
		return type switch
		{
			ItemID.FlinxinatorStaff => ItemID.FlinxStaff,
			ItemID.LegacyTrueExcalibur => ItemID.TrueExcalibur,
			ItemID.LegacyTrueNightsEdge => ItemID.TrueNightsEdge,
			ItemID.LegacyTerraBlade => ItemID.TerraBlade,
			ItemID.RubblemakerPots => ItemID.RubblemakerMedium,
			_ => type,
		};
	}

	/// <summary>The id written into a packet for the server.</summary>
	public static int NetId(int type)
	{
		if (Main.netMode == 1 && IsCustom(type))
		{
			return VanillaTwin(type);
		}
		return ProtocolCompat.ItemNetId(type);
	}

	public static void SetDefaults(Item item)
	{
		switch (item.type)
		{
		case ItemID.FlinxinatorStaff:
			item.damage = 32000;
			item.crit = 100;
			item.useStyle = 1;
			item.shootSpeed = 10f;
			item.shoot = 953;
			item.width = 26;
			item.height = 28;
			item.UseSound = SoundID.Item44;
			item.useAnimation = 3;
			item.useTime = 3;
			item.rare = -13;
			item.noMelee = true;
			item.knockBack = 2f;
			item.value = Item.sellPrice(5);
			item.autoReuse = true;
			item.reuseDelay = 0;
			item.itemShootBehavior = 93;
			item.hitsInvincibleNpc = true;
			break;
		case ItemID.LegacyTrueExcalibur:
			item.useStyle = 1;
			item.useAnimation = 16;
			item.useTime = 16;
			item.shoot = 156;
			item.shootSpeed = 11f;
			item.knockBack = 4.5f;
			item.width = 40;
			item.height = 40;
			item.damage = 70;
			item.scale = 1.05f;
			item.UseSound = SoundID.Item1;
			item.rare = 8;
			item.value = Item.sellPrice(0, 10);
			item.melee = true;
			break;
		case ItemID.LegacyTrueNightsEdge:
			item.useStyle = 1;
			item.useAnimation = 26;
			item.useTime = 26;
			item.shoot = 157;
			item.shootSpeed = 10f;
			item.knockBack = 4.75f;
			item.width = 40;
			item.height = 40;
			item.damage = 105;
			item.scale = 1.15f;
			item.UseSound = SoundID.Item1;
			item.rare = 8;
			item.value = Item.sellPrice(0, 10);
			item.melee = true;
			break;
		case ItemID.LegacyTerraBlade:
			item.rare = 8;
			item.UseSound = SoundID.Item1;
			item.useStyle = 1;
			item.damage = 115;
			item.useAnimation = 14;
			item.useTime = 14;
			item.width = 30;
			item.height = 30;
			item.shoot = 132;
			item.scale = 1.1f;
			item.shootSpeed = 12f;
			item.knockBack = 6.5f;
			item.melee = true;
			item.value = Item.sellPrice(0, 20);
			item.autoReuse = true;
			break;
		case ItemID.RubblemakerPots:
			item.DefaultToPlaceableTile((ushort)647, 0);
			item.SetShopValues(ItemRarityColor.StrongRed10, Item.sellPrice(0, 5));
			item.maxStack = 1;
			item.consumable = false;
			item.tileBoost = 3;
			break;
		}
	}

	private static FlexibleTileWand _potsWand;

	/// <summary>Rubblemaker (Pots): the pot rubble of every biome, paid with the material of that biome's pots.</summary>
	public static FlexibleTileWand PotsWand
	{
		get
		{
			if (_potsWand == null)
			{
				FlexibleTileWand wand = new FlexibleTileWand();
				ushort tile = TileID.PotsEcho;
				wand.AddVariations_ByRow(133, tile, 3, 0, 1, 2, 3);
				wand.AddVariations_ByRow(664, tile, 3, 4, 5, 6);
				wand.AddVariations_ByRow(176, tile, 3, 7, 8, 9);
				wand.AddVariations_ByRow(154, tile, 3, 10, 11, 12);
				wand.AddVariations_ByRow(173, tile, 3, 13, 14, 15);
				wand.AddVariations_ByRow(61, tile, 3, 16, 17, 18);
				wand.AddVariations_ByRow(150, tile, 3, 19, 20, 21);
				wand.AddVariations_ByRow(836, tile, 3, 22, 23, 24);
				wand.AddVariations_ByRow(607, tile, 3, 25, 26, 27);
				wand.AddVariations_ByRow(1101, tile, 3, 28, 29, 30);
				wand.AddVariations_ByRow(3081, tile, 3, 31, 32, 33);
				wand.AddVariations_ByRow(607, tile, 3, 34, 35, 36);
				_potsWand = wand;
			}
			return _potsWand;
		}
	}
}
