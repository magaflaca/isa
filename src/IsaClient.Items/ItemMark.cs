using System;
using IsaClient.Identity;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace IsaClient.Items;

/// <summary>
/// What makes an item an IsaClient item: who made it, its tag color, a text of its own, the name it has in your
/// collection and the id servers are told it is. An item carries one (<see cref="Item.isaMark"/>) from the moment you
/// change it in the Item Editor; its stats live on the item itself (an <see cref="ItemDesign"/> keeps both). Marks are
/// shared between copies of an item, so they are never changed in place: change a <see cref="Copy"/>.
/// </summary>
public sealed class ItemMark
{
	/// <summary>Stable id of the design (12 hex digits); copies of the same design share it.</summary>
	public string Id;

	/// <summary>Its name in your collection (null: the item's name).</summary>
	public string Title;

	/// <summary>Extra lines for its tooltip (lines split by \n).</summary>
	public string Description;

	/// <summary>Color of the wire tag on its icon (<see cref="TagArt"/>).</summary>
	public byte Tag;

	/// <summary>Who made it: the name it had then, and their IsaClient ID (names follow the ID when it's seen again).</summary>
	public string Creator;

	public ulong CreatorId;

	/// <summary>The item id servers and other players are told (0: its own). The item stays what it is here.</summary>
	public int NetId;

	/// <summary>
	/// The item's whole info (null: the game's), lines split by \n, with placeholders like {damage}
	/// (<see cref="ItemTooltip.Placeholders"/>); a line that is just {info} is the game's info.
	/// </summary>
	public string Info;

	/// <summary>The name its creator chose to show as the maker (null: their current name).</summary>
	public string CreatorShown;

	public const int MaxInfo = 900;

	public const int MaxDescription = 220;

	public const int MaxTitle = 40;

	private static readonly Random _random = new Random();

	public ItemMark Copy()
	{
		return (ItemMark)MemberwiseClone();
	}

	/// <summary>A new mark made by you: yellow tag, you as the creator.</summary>
	public static ItemMark New()
	{
		return new ItemMark
		{
			Id = NewId(),
			Tag = TagArt.Yellow,
			Creator = (Main.LocalPlayer != null && !string.IsNullOrEmpty(Main.LocalPlayer.name)) ? Main.LocalPlayer.name : "Terrarian",
			CreatorId = ClientIdentity.Id
		};
	}

	public static string NewId()
	{
		byte[] bytes = new byte[6];
		lock (_random)
		{
			_random.NextBytes(bytes);
		}
		return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
	}

	/// <summary>The creator's name as of now (their latest name when we've seen their IsaClient ID).</summary>
	public string CreatorName => string.IsNullOrWhiteSpace(CreatorShown) ? CreatorNames.Of(CreatorId, Creator) : CreatorShown;

	public bool MadeByMe => CreatorId != 0 && CreatorId == ClientIdentity.Id;
}

/// <summary>The id an item goes by on the network: its mark's <see cref="ItemMark.NetId"/>, or the usual one.</summary>
public static class ItemNet
{
	public static int Id(Item item)
	{
		if (item == null)
		{
			return 0;
		}
		if (item.isaMark != null && item.isaMark.NetId > 0 && item.isaMark.NetId < ItemID.Count && !item.IsAir)
		{
			return IsaClient.Gameplay.LegacyItems.NetId(item.isaMark.NetId);
		}
		return IsaClient.Gameplay.LegacyItems.NetId(item.type);
	}
}

/// <summary>
/// The little wire knot on the icons of IsaClient items, as the game draws it on signed traps, in the color of the
/// item's tag. Yellow, red, blue and green are the game's own wires; the rest are a gray wire tinted.
/// </summary>
public static class TagArt
{
	public const byte Yellow = 0;

	public const byte Red = 1;

	public const byte Blue = 2;

	public const byte Green = 3;

	public const byte Rainbow = 10;

	public static readonly string[] Names = { "Yellow", "Red", "Blue", "Green", "Orange", "Pink", "Purple", "Cyan", "White", "Black", "Rainbow" };

	public static readonly Color[] Colors =
	{
		new Color(255, 220, 60),
		new Color(235, 60, 60),
		new Color(60, 130, 255),
		new Color(60, 220, 90),
		new Color(255, 150, 40),
		new Color(255, 120, 200),
		new Color(170, 90, 255),
		new Color(60, 230, 230),
		new Color(245, 245, 245),
		new Color(40, 40, 46),
		Color.White
	};

	public static int Count => Names.Length;

	private static readonly Rectangle Knot = new Rectangle(4, 58, 8, 8);

	private static Texture2D _gray;

	/// <summary>The tag an item shows, or -1 for none: its mark's, yellow for older preset edits and IsaClient's own items.</summary>
	public static int TagOf(Item item)
	{
		if (item == null || item.IsAir)
		{
			return -1;
		}
		if (item.isaMark != null)
		{
			return item.isaMark.Tag;
		}
		if (item.type == ItemID.FlinxinatorStaff || item.type == ItemID.RubblemakerPots || ItemID.Sets.CustomClientItem[item.type])
		{
			return Green;
		}
		if (item.modifiedByItemMods)
		{
			return Yellow;
		}
		return -1;
	}

	public static Color ColorOf(int tag)
	{
		if (tag == Rainbow)
		{
			return Main.hslToRgb(Main.GlobalTimeWrappedHourly * 0.4f % 1f, 0.9f, 0.6f);
		}
		return (tag >= 0 && tag < Colors.Length) ? Colors[tag] : Colors[0];
	}

	/// <summary>Draws the knot centered on <paramref name="center"/>.</summary>
	public static void Draw(SpriteBatch sb, int tag, Vector2 center, float scale, Color light, float rotation = 0f)
	{
		Texture2D texture;
		Color color = light;
		switch (tag)
		{
		case Yellow:
			texture = TextureAssets.Wire4.Value;
			break;
		case Red:
			texture = TextureAssets.Wire.Value;
			break;
		case Blue:
			texture = TextureAssets.Wire2.Value;
			break;
		case Green:
			texture = TextureAssets.Wire3.Value;
			break;
		default:
			texture = Gray();
			color = light.MultiplyRGBA(ColorOf(tag));
			break;
		}
		if (texture == null)
		{
			return;
		}
		Rectangle source = (texture == _gray) ? new Rectangle(0, 0, 8, 8) : Knot;
		sb.Draw(texture, center, source, color, rotation, new Vector2(4f), scale, SpriteEffects.None, 0f);
	}

	/// <summary>The yellow wire's knot in grays, to tint (made once).</summary>
	private static Texture2D Gray()
	{
		if (_gray != null)
		{
			return _gray;
		}
		try
		{
			Texture2D wire = TextureAssets.Wire4.Value;
			Color[] data = new Color[64];
			wire.GetData(0, Knot, data, 0, 64);
			float max = 1f;
			for (int i = 0; i < 64; i++)
			{
				max = Math.Max(max, Math.Max(data[i].R, Math.Max(data[i].G, data[i].B)));
			}
			for (int i = 0; i < 64; i++)
			{
				Color c = data[i];
				int v = (int)Math.Min(255f, Math.Max(c.R, Math.Max(c.G, c.B)) * 255f / max);
				// Premultiplied, like the game's textures.
				v = v * c.A / 255;
				data[i] = new Color(v, v, v, c.A);
			}
			_gray = new Texture2D(Main.instance.GraphicsDevice, 8, 8);
			_gray.SetData(data);
		}
		catch
		{
			return null;
		}
		return _gray;
	}
}
