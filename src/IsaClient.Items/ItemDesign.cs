using System;
using System.IO;
using System.Text;
using Terraria;
using Terraria.ID;

namespace IsaClient.Items;

/// <summary>
/// An IsaClient item written down: the base item, its prefix and every stat the Item Editor can change, with its
/// <see cref="ItemMark"/>. It's what your collection keeps (one JSON per design), what the inventory keeper writes
/// next to your character, and what goes through the pipe when you link the item in chat.
/// </summary>
public sealed class ItemDesign
{
	public int Version = 1;

	public ItemMark Mark;

	public int Type;

	public byte Prefix;

	public int Stack = 1;

	/// <summary>The item's own name ("" for the usual one).</summary>
	public string Name = "";

	public int Damage;
	public int Crit;
	public float Knockback;
	public float Scale = 1f;
	public int Mana;
	public int UseTime;
	public int UseAnimation;
	public int UseStyle;
	public int HoldStyle;
	public bool AutoReuse;
	public bool UseTurn;
	public bool Channel;
	public int Shoot;
	public float ShootSpeed;
	public int UseAmmo;
	public int Ammo;
	public int TimesToShoot = 1;
	public int ShootStyle;
	public bool ShootsEveryUse;
	public int HealLife;
	public int HealMana;
	public int BuffType;
	public int BuffTime;
	public int Pick;
	public int Axe;
	public int Hammer;
	public int FishingPole;
	public bool Consumable;
	public int TileBoost;
	public int CreateTile = -1;
	public int CreateWall = -1;
	public int PlaceStyle;
	public bool Melee;
	public bool Ranged;
	public bool Magic;
	public bool Summon;
	public bool Sentry;
	public bool DD2Summon;
	public int HeadSlot = -1;
	public int BodySlot = -1;
	public int LegSlot = -1;
	public bool Accessory;
	public bool Vanity;
	public int WingSlot = -1;
	public int Defense;
	public int LifeRegen;
	public int ManaIncrease;
	public bool NoMelee;
	public bool NoUseGraphic;
	public bool HitsInvincible;
	public uint Color;
	public int Rare;
	public int Value;

	/// <summary>Older item presets: the preset that changed it (kept so it still says so).</summary>
	public string LegacyPreset;

	public long Created;

	public long Updated;

	public static long Now => DateTime.UtcNow.Ticks;

	public string Title => (Mark != null && !string.IsNullOrWhiteSpace(Mark.Title)) ? Mark.Title : ItemName;

	/// <summary>The item's name as it shows (its own name, or the game's).</summary>
	public string ItemName => !string.IsNullOrWhiteSpace(Name) ? Name : Lang.GetItemNameValue(Type);

	/// <summary>An item as it is now, written down (its mark copied).</summary>
	public static ItemDesign Capture(Item item)
	{
		ItemDesign d = new ItemDesign
		{
			Mark = item.isaMark?.Copy(),
			Type = item.type,
			Prefix = (byte)item.prefix,
			Stack = Math.Max(1, item.stack),
			Name = item.NameOverride ?? "",
			Damage = item.damage,
			Crit = item.crit,
			Knockback = item.knockBack,
			Scale = item.scale,
			Mana = item.mana,
			UseTime = item.useTime,
			UseAnimation = item.useAnimation,
			UseStyle = item.useStyle,
			HoldStyle = item.holdStyle,
			AutoReuse = item.autoReuse,
			UseTurn = item.useTurn,
			Channel = item.channel,
			Shoot = item.shoot,
			ShootSpeed = item.shootSpeed,
			UseAmmo = item.useAmmo,
			Ammo = item.ammo,
			TimesToShoot = item.timesToShoot,
			ShootStyle = item.itemShootBehavior,
			ShootsEveryUse = item.shootsEveryUse,
			HealLife = item.healLife,
			HealMana = item.healMana,
			BuffType = item.buffType,
			BuffTime = item.buffTime,
			Pick = item.pick,
			Axe = item.axe,
			Hammer = item.hammer,
			FishingPole = item.fishingPole,
			Consumable = item.consumable,
			TileBoost = item.tileBoost,
			CreateTile = item.createTile,
			CreateWall = item.createWall,
			PlaceStyle = item.placeStyle,
			Melee = item.melee,
			Ranged = item.ranged,
			Magic = item.magic,
			Summon = item.summon,
			Sentry = item.sentry,
			DD2Summon = item.DD2Summon,
			HeadSlot = item.headSlot,
			BodySlot = item.bodySlot,
			LegSlot = item.legSlot,
			Accessory = item.accessory,
			Vanity = item.vanity,
			WingSlot = item.wingSlot,
			Defense = item.defense,
			LifeRegen = item.lifeRegen,
			ManaIncrease = item.manaIncrease,
			NoMelee = item.noMelee,
			NoUseGraphic = item.noUseGraphic,
			HitsInvincible = item.hitsInvincibleNpc,
			Color = item.color.PackedValue,
			Rare = item.rare,
			Value = item.value,
			LegacyPreset = item.modifiedByItemMods ? item.itemModPresetName : null,
			Created = Now,
			Updated = Now
		};
		return d;
	}

	/// <summary>Puts the stats and the mark on an item of this type (it isn't reset first).</summary>
	public void Apply(Item item)
	{
		item.damage = Damage;
		item.crit = Crit;
		item.knockBack = Knockback;
		item.scale = Scale;
		item.mana = Mana;
		item.useTime = Math.Max(0, UseTime);
		item.useAnimation = Math.Max(1, UseAnimation);
		item.useStyle = UseStyle;
		item.holdStyle = HoldStyle;
		item.autoReuse = AutoReuse;
		item.useTurn = UseTurn;
		item.channel = Channel;
		item.shoot = Utils.Clamp(Shoot, 0, ProjectileID.Count - 1);
		item.shootSpeed = ShootSpeed;
		item.useAmmo = UseAmmo;
		item.ammo = Ammo;
		item.timesToShoot = (ushort)Utils.Clamp(TimesToShoot, 1, 1000);
		item.itemShootBehavior = Utils.Clamp(ShootStyle, 0, IsaClient.Gameplay.ShootBehaviors.Count - 1);
		item.shootsEveryUse = ShootsEveryUse;
		item.healLife = HealLife;
		item.healMana = HealMana;
		item.buffType = Utils.Clamp(BuffType, 0, BuffID.Count - 1);
		item.buffTime = BuffTime;
		item.pick = Pick;
		item.axe = Axe;
		item.hammer = Hammer;
		item.fishingPole = FishingPole;
		item.consumable = Consumable;
		item.tileBoost = TileBoost;
		item.createTile = Utils.Clamp(CreateTile, -1, TileID.Count - 1);
		item.createWall = Utils.Clamp(CreateWall, -1, WallID.Count - 1);
		item.placeStyle = PlaceStyle;
		item.melee = Melee;
		item.ranged = Ranged;
		item.magic = Magic;
		item.summon = Summon;
		item.sentry = Sentry;
		item.DD2Summon = DD2Summon;
		item.headSlot = HeadSlot;
		item.bodySlot = BodySlot;
		item.legSlot = LegSlot;
		item.accessory = Accessory;
		item.vanity = Vanity;
		item.wingSlot = (sbyte)WingSlot;
		item.defense = Defense;
		item.lifeRegen = LifeRegen;
		item.manaIncrease = ManaIncrease;
		item.noMelee = NoMelee;
		item.noUseGraphic = NoUseGraphic;
		item.hitsInvincibleNpc = HitsInvincible;
		item.color = new Microsoft.Xna.Framework.Color { PackedValue = Color };
		item.rare = Rare;
		item.value = Value;
		if (!string.IsNullOrWhiteSpace(Name))
		{
			item.SetNameOverride(Name);
		}
		else
		{
			item.ClearNameOverride();
		}
		item.isaMark = Mark?.Copy();
		if (LegacyPreset != null)
		{
			item.modifiedByItemMods = true;
			item.itemModPresetName = LegacyPreset;
		}
		item.modifiedByServer = false;
	}

	/// <summary>A new item made from this design (<paramref name="stack"/> 0: the design's).</summary>
	public Item Make(int stack = 0)
	{
		Item item = new Item();
		if (Type <= 0 || Type >= ItemID.Count)
		{
			return item;
		}
		item.SetDefaults(Type);
		if (Prefix > 0)
		{
			item.Prefix(Prefix);
		}
		Apply(item);
		item.stack = Utils.Clamp((stack > 0) ? stack : Stack, 1, Math.Max(1, item.maxStack));
		return item;
	}

	public ItemDesign Copy()
	{
		ItemDesign copy = (ItemDesign)MemberwiseClone();
		copy.Mark = Mark?.Copy();
		return copy;
	}

	#region Binary (the pipe)

	public byte[] ToBytes()
	{
		using MemoryStream stream = new MemoryStream();
		using BinaryWriter w = new BinaryWriter(stream, Encoding.UTF8);
		w.Write((byte)1);
		ItemMark m = Mark ?? new ItemMark();
		Str(w, m.Id, 12);
		Str(w, m.Title, ItemMark.MaxTitle);
		Str(w, m.Description, ItemMark.MaxDescription);
		w.Write(m.Tag);
		Str(w, m.Creator, 24);
		w.Write(m.CreatorId);
		w.Write((short)m.NetId);
		w.Write((short)Type);
		w.Write(Prefix);
		Str(w, Name, ItemMark.MaxTitle);
		w.Write(Damage);
		w.Write((short)Crit);
		w.Write(Knockback);
		w.Write(Scale);
		w.Write((short)Mana);
		w.Write((short)UseTime);
		w.Write((short)UseAnimation);
		w.Write((byte)UseStyle);
		w.Write((byte)HoldStyle);
		w.Write(Bits(AutoReuse, UseTurn, Channel, ShootsEveryUse, Consumable, Melee, Ranged, Magic));
		w.Write(Bits(Summon, Sentry, DD2Summon, Accessory, Vanity, NoMelee, NoUseGraphic, HitsInvincible));
		w.Write((short)Shoot);
		w.Write(ShootSpeed);
		w.Write((short)UseAmmo);
		w.Write((short)Ammo);
		w.Write((short)TimesToShoot);
		w.Write((byte)ShootStyle);
		w.Write((short)HealLife);
		w.Write((short)HealMana);
		w.Write((short)BuffType);
		w.Write(BuffTime);
		w.Write((short)Pick);
		w.Write((short)Axe);
		w.Write((short)Hammer);
		w.Write((short)FishingPole);
		w.Write((short)TileBoost);
		w.Write((short)CreateTile);
		w.Write((short)CreateWall);
		w.Write((short)PlaceStyle);
		w.Write((short)HeadSlot);
		w.Write((short)BodySlot);
		w.Write((short)LegSlot);
		w.Write((short)WingSlot);
		w.Write((short)Defense);
		w.Write((short)LifeRegen);
		w.Write((short)ManaIncrease);
		w.Write(Color);
		w.Write((short)Rare);
		w.Write(Value);
		// Added later: older clients stop reading before this.
		w.Write((byte)2);
		Str(w, m.Info, ItemMark.MaxInfo);
		Str(w, m.CreatorShown, 24);
		return stream.ToArray();
	}

	public static ItemDesign FromBytes(byte[] data, int offset = 0)
	{
		try
		{
			using MemoryStream stream = new MemoryStream(data, offset, data.Length - offset);
			using BinaryReader r = new BinaryReader(stream, Encoding.UTF8);
			if (r.ReadByte() != 1)
			{
				return null;
			}
			ItemDesign d = new ItemDesign { Mark = new ItemMark() };
			d.Mark.Id = r.ReadString();
			d.Mark.Title = NullIfEmpty(r.ReadString());
			d.Mark.Description = NullIfEmpty(r.ReadString());
			d.Mark.Tag = (byte)Math.Min(r.ReadByte(), TagArt.Count - 1);
			d.Mark.Creator = r.ReadString();
			d.Mark.CreatorId = r.ReadUInt64();
			d.Mark.NetId = r.ReadInt16();
			d.Type = r.ReadInt16();
			d.Prefix = r.ReadByte();
			d.Name = r.ReadString();
			d.Damage = r.ReadInt32();
			d.Crit = r.ReadInt16();
			d.Knockback = r.ReadSingle();
			d.Scale = r.ReadSingle();
			d.Mana = r.ReadInt16();
			d.UseTime = r.ReadInt16();
			d.UseAnimation = r.ReadInt16();
			d.UseStyle = r.ReadByte();
			d.HoldStyle = r.ReadByte();
			byte a = r.ReadByte();
			byte b = r.ReadByte();
			d.AutoReuse = (a & 1) != 0;
			d.UseTurn = (a & 2) != 0;
			d.Channel = (a & 4) != 0;
			d.ShootsEveryUse = (a & 8) != 0;
			d.Consumable = (a & 16) != 0;
			d.Melee = (a & 32) != 0;
			d.Ranged = (a & 64) != 0;
			d.Magic = (a & 128) != 0;
			d.Summon = (b & 1) != 0;
			d.Sentry = (b & 2) != 0;
			d.DD2Summon = (b & 4) != 0;
			d.Accessory = (b & 8) != 0;
			d.Vanity = (b & 16) != 0;
			d.NoMelee = (b & 32) != 0;
			d.NoUseGraphic = (b & 64) != 0;
			d.HitsInvincible = (b & 128) != 0;
			d.Shoot = r.ReadInt16();
			d.ShootSpeed = r.ReadSingle();
			d.UseAmmo = r.ReadInt16();
			d.Ammo = r.ReadInt16();
			d.TimesToShoot = r.ReadInt16();
			d.ShootStyle = r.ReadByte();
			d.HealLife = r.ReadInt16();
			d.HealMana = r.ReadInt16();
			d.BuffType = r.ReadInt16();
			d.BuffTime = r.ReadInt32();
			d.Pick = r.ReadInt16();
			d.Axe = r.ReadInt16();
			d.Hammer = r.ReadInt16();
			d.FishingPole = r.ReadInt16();
			d.TileBoost = r.ReadInt16();
			d.CreateTile = r.ReadInt16();
			d.CreateWall = r.ReadInt16();
			d.PlaceStyle = r.ReadInt16();
			d.HeadSlot = r.ReadInt16();
			d.BodySlot = r.ReadInt16();
			d.LegSlot = r.ReadInt16();
			d.WingSlot = r.ReadInt16();
			d.Defense = r.ReadInt16();
			d.LifeRegen = r.ReadInt16();
			d.ManaIncrease = r.ReadInt16();
			d.Color = r.ReadUInt32();
			d.Rare = r.ReadInt16();
			d.Value = r.ReadInt32();
			if (stream.Position < stream.Length && r.ReadByte() >= 2)
			{
				d.Mark.Info = NullIfEmpty(r.ReadString());
				d.Mark.CreatorShown = NullIfEmpty(r.ReadString());
			}
			if (d.Type <= 0 || d.Type >= ItemID.Count || string.IsNullOrEmpty(d.Mark.Id))
			{
				return null;
			}
			d.Created = d.Updated = Now;
			return d;
		}
		catch
		{
			return null;
		}
	}

	private static void Str(BinaryWriter w, string s, int max)
	{
		s = s ?? "";
		w.Write((s.Length > max) ? s.Substring(0, max) : s);
	}

	private static string NullIfEmpty(string s)
	{
		return string.IsNullOrEmpty(s) ? null : s;
	}

	private static byte Bits(params bool[] bits)
	{
		int b = 0;
		for (int i = 0; i < bits.Length; i++)
		{
			if (bits[i])
			{
				b |= 1 << i;
			}
		}
		return (byte)b;
	}

	#endregion
}
