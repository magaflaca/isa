using System;
using System.Collections.Generic;
using Terraria.GameContent.Items;
using Terraria.ID;
using Terraria;

namespace IsaClient.Gameplay;

/// <summary>
/// Held Item "Shoot Style" (numbers match the older presets, so they keep working).
/// 1.4.5.8's own Player.ItemCheck_Shoot is kept
/// and every special case in it that looks at the item type looks at <see cref="ShootAs" /> instead: an item shooting
/// with another style runs exactly the code of the weapon that style comes from (Minishark, Star Wrath, Zenith...).
/// With its own style an item shoots like vanilla, new 1.4.5 weapons included. The Custom_* styles are the client's own.
/// </summary>
public static class ShootBehaviors
{
	public const int Count = 94;

	/// <summary>How many projectiles the random rain / around-cursor styles shoot (.rngamount).</summary>
	public static ushort RandomCount = 8;

	/// <summary>The weapon each style comes from (0: plain shot; -1: custom styles).</summary>
	private static readonly int[] _source = new int[Count]
	{
		0, 486, 46, 65, 190, 273, 368, 3836, 674, 675,
		3858, 757, 1826, 1569, 3473, 3065, 4923, 4722, 4956, 4381,
		3019, 3854, 98, 1319, 964, 3350, 5117, 434, 2270, 534,
		4703, 3788, 3029, 1229, 679, 1258, 3859, 1910, 1929, 1265,
		1156, 4953, 2797, 3107, 2624, 3540, 3475, 1553, 3546, 3930,
		1244, 1121, 3787, 3852, 1308, 2188, 4270, 518, 5065, 1155,
		1801, 1931, 1930, 3870, 3006, 3053, 3779, 3210, 2623, 4952,
		3014, 2750, 4715, 3542, 3570, 1309, 1157, 2535, 2551, 2584,
		3531, 1572, 3834, 3569, 3384, 4262, 4682, 5134, 5451, 3989,
		-1, -1, -1, -1
	};

	/// <summary>The style of every item that has one (all other items: 0).</summary>
	private static readonly Dictionary<int, int> _defaults = new Dictionary<int, int>
	{
		{ 486, 1 }, { 4463, 1 }, { 46, 2 }, { 65, 3 }, { 190, 4 }, { 273, 5 }, { 368, 6 }, { 3836, 7 }, { 674, 8 }, { 675, 9 },
		{ 3858, 10 }, { 757, 11 }, { 1826, 12 }, { 1569, 13 }, { 3473, 14 }, { 3065, 15 }, { 4923, 16 }, { 4722, 17 }, { 4956, 18 }, { 4381, 19 },
		{ 3019, 20 }, { 3854, 21 }, { 98, 22 }, { 533, 22 }, { 1319, 23 }, { 964, 24 }, { 3350, 25 }, { 5117, 26 }, { 434, 27 }, { 2270, 28 },
		{ 534, 29 }, { 4703, 30 }, { 3788, 31 }, { 3029, 32 }, { 1229, 33 }, { 679, 34 }, { 1258, 35 }, { 3859, 36 }, { 1910, 37 }, { 1929, 38 },
		{ 1265, 39 }, { 1156, 40 }, { 4953, 41 }, { 2797, 42 }, { 3107, 43 }, { 2624, 44 }, { 3540, 45 }, { 3475, 46 }, { 1553, 47 }, { 3546, 48 },
		{ 3930, 49 }, { 1244, 50 }, { 1256, 50 }, { 1121, 51 }, { 3787, 52 }, { 3852, 53 }, { 1308, 54 }, { 2188, 55 }, { 4270, 56 }, { 518, 57 },
		{ 5065, 58 }, { 1155, 59 }, { 1801, 60 }, { 1931, 61 }, { 1930, 62 }, { 3870, 63 }, { 3006, 64 }, { 3053, 65 }, { 3779, 66 }, { 3210, 67 },
		{ 2623, 68 }, { 4952, 69 }, { 3014, 70 }, { 2750, 71 }, { 4715, 72 }, { 3542, 73 }, { 3570, 74 },
		{ 1309, 75 }, { 1802, 75 }, { 2364, 75 }, { 2365, 75 }, { 2621, 75 }, { 2749, 75 }, { 3249, 75 }, { 3474, 75 }, { 4269, 75 }, { 4273, 75 },
		{ 4281, 75 }, { 4607, 75 }, { 4758, 75 }, { 5005, 75 }, { 5069, 75 }, { 5114, 75 },
		{ 1157, 76 }, { 2535, 77 }, { 2551, 78 }, { 2584, 79 }, { 3531, 80 }, { 1572, 81 }, { 2366, 81 }, { 5119, 81 }, { 3834, 82 }, { 3569, 83 },
		{ 3571, 83 }, { 3384, 84 }, { 4262, 85 }, { 4682, 86 }, { 5134, 87 }, { 5451, 88 }, { 3989, 89 }
	};

	/// <summary>The weapon a style comes from (0: plain shot, -1: IsaClient's own styles).</summary>
	public static int Source(int style)
	{
		return (style >= 0 && style < Count) ? _source[style] : 0;
	}

	public static int DefaultFor(int itemType)
	{
		int style;
		return _defaults.TryGetValue(itemType, out style) ? style : 0;
	}

	public static bool IsCustom(int style)
	{
		return style >= (int)ItemShootBehavior.Custom_RandomProj && style < Count;
	}

	/// <summary>The item type ItemCheck_Shoot treats this item as.</summary>
	public static int ShootAs(Item item)
	{
		int style = item.itemShootBehavior;
		if (style == DefaultFor(item.type))
		{
			return item.type;
		}
		if (style <= 0 || style >= Count)
		{
			return 0;
		}
		return Math.Max(_source[style], 0);
	}

	public static string Name(int style)
	{
		if (style >= 0 && style < Count)
		{
			return ((ItemShootBehavior)style).ToString();
		}
		return "Unknown";
	}

	/// <summary>Name and, while it differs from the item's own style, the weapon it shoots like.</summary>
	public static string Describe(Item item)
	{
		int style = item.itemShootBehavior;
		string text = Name(style);
		if (style > 0 && style < Count && _source[style] > 0 && style != DefaultFor(item.type))
		{
			text += " (shoots like " + Lang.GetItemNameValue(_source[style]) + ")";
		}
		return text;
	}

	/// <summary>A random projectile a weapon may shoot (nothing hostile, no pets, hooks or world breakers; flails are re-rolled).</summary>
	public static int RandomProjectile()
	{
		while (true)
		{
			int type = Main.rand.Next(1, ProjectileID.Count);
			if (!Main.projHostile[type] && !ProjectileID.Sets.LightPet[type] && !Main.projPet[type] && !Main.projHook[type] && type != 281 && type != 23 && type != 190 && type != 262 && type != 271 && type != 600 && type != 697 && type != 707 && type != 708 && type != 722)
			{
				return type;
			}
		}
	}

	/// <summary>Projectiles that only work held by the player (killed right away when a random style rolls one).</summary>
	public static bool NeedsHolding(Projectile projectile)
	{
		int ai = projectile.aiStyle;
		return ai == 3 || ai == 15 || ai == 7 || ai == 9 || ai == 13 || ai == 19 || ai == 20 || ai == 61 || ai == 75 || ai == 160 || ai == 99 || ai == 165;
	}
}
