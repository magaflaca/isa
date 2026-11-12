using System.Collections.Generic;

namespace IsaClient.Gba.Trainer;

/// <summary>A clothing role and its tone for each body zone (torso, legs, feet).</summary>
public readonly struct ZoneRoles
{
	public readonly (BodyRole Role, byte Tone)[] Zones;

	public ZoneRoles(BodyRole torso, int t0, BodyRole legs, int t1, BodyRole feet, int t2)
	{
		Zones = new[] { (torso, (byte)t0), (legs, (byte)t1), (feet, (byte)t2) };
	}

	public static ZoneRoles All(BodyRole role, int tone) => new ZoneRoles(role, tone, role, tone, role, tone);
}

/// <summary>Where a trainer picture's head is and how its body is recolored.</summary>
public sealed class PicSpec
{
	public double Cx;

	public double Cy;

	public int Neck;

	public int X0;

	public int X1;

	public int Yaw;

	/// <summary>(ya, yb, xa, xb, ids): hair beside the body removed with nothing behind it.</summary>
	public (int, int, int, int, int[])[] Strip;

	/// <summary>Rows where the torso and the legs end (the feet below); null = one zone.</summary>
	public (int TorsoEnd, int LegsEnd)? ZoneRows;

	public Dictionary<int, ZoneRoles> Roles;

	public int Zone(int y) => (ZoneRows == null) ? 0 : ((y < ZoneRows.Value.TorsoEnd) ? 0 : ((y < ZoneRows.Value.LegsEnd) ? 1 : 2));
}

/// <summary>
/// The player sprites of one Pokémon game family, as the trainer converter needs them: graphics ids and unique frame
/// counts of each look (male/female share counts and order), where the head sits in the standing frames, the palette
/// index of every clothing role, and the battle back / trainer card pictures. FireRed/LeafGreen (Red, Leaf) and
/// Emerald (Brendan, May).
/// </summary>
public sealed class TrainerGame
{
	public string Name;

	public string MaleName;

	public string FemaleName;

	/// <summary>Look, male graphics id, female graphics id, unique frames.</summary>
	public (string Kind, int Male, int Female, int Count)[] Looks;

	/// <summary>First body row of the standing frames.</summary>
	public int NeckY;

	/// <summary>Head centre row in the standing frames.</summary>
	public double AnchorY;

	/// <summary>(player palette tag, reflection palette tag) per gender: index 0 male, 1 female (FRLG share one).</summary>
	public (int Player, int Reflection)[] PaletteTags;

	public Dictionary<int, ZoneRoles> MaleRoles;

	public Dictionary<int, ZoneRoles> FemaleRoles;

	public int[] MaleHat;

	public int[] FemaleHat;

	/// <summary>Leaf's long hair over her back is removed from the female body (FRLG).</summary>
	public bool StripFemaleHair;

	public int BackPicSize;

	public int BackFrames;

	public int BackMale;

	public int BackFemale;

	public int FrontCount;

	public int FrontMale;

	public int FrontFemale;

	public PicSpec Back;

	public PicSpec FrontMaleSpec;

	public PicSpec FrontFemaleSpec;

	public IEnumerable<int> AllPaletteTags
	{
		get
		{
			foreach (var p in PaletteTags)
			{
				yield return p.Player;
				yield return p.Reflection;
			}
		}
	}

	/// <summary>The family of a ROM (by game code), or null when its player can't be changed.</summary>
	public static TrainerGame For(byte[] rom)
	{
		string code = FireRedRom.GameCode(rom);
		if (code.StartsWith("BPR") || code.StartsWith("BPG"))
		{
			return FireRed;
		}
		if (code.StartsWith("BPE"))
		{
			return Emerald;
		}
		return null;
	}

	private static ZoneRoles Z(BodyRole r, int t) => ZoneRoles.All(r, t);

	public static readonly TrainerGame FireRed = new TrainerGame
	{
		Name = "FireRed / LeafGreen",
		MaleName = "Red",
		FemaleName = "Leaf",
		Looks = new[]
		{
			("normal", 0, 7, 20),
			("bike", 1, 8, 9),
			("surf", 2, 9, 3),
			("item", 3, 10, 9),
			("fish", 4, 11, 12),
			("vsbike", 6, 13, 6)
		},
		NeckY = 23,
		AnchorY = 18.0,
		PaletteTags = new[] { (0x1100, 0x1101) },
		MaleRoles = new Dictionary<int, ZoneRoles>
		{
			[2] = Z(BodyRole.Skin, 0),
			[3] = Z(BodyRole.Skin, 1),
			[4] = Z(BodyRole.Skin, 2),
			[11] = Z(BodyRole.Shirt, 0),
			[12] = Z(BodyRole.Shirt, 1),
			[8] = Z(BodyRole.Shirt, 2),
			[9] = Z(BodyRole.Undershirt, 0),
			[10] = Z(BodyRole.Undershirt, 1),
			[7] = new ZoneRoles(BodyRole.Undershirt, 1, BodyRole.Pants, 0, BodyRole.Shoes, 0),
			[6] = new ZoneRoles(BodyRole.Undershirt, 2, BodyRole.Pants, 1, BodyRole.Shoes, 1)
		},
		FemaleRoles = new Dictionary<int, ZoneRoles>
		{
			[2] = Z(BodyRole.Skin, 0),
			[3] = Z(BodyRole.Skin, 1),
			[7] = Z(BodyRole.Shirt, 0),
			[5] = Z(BodyRole.Shirt, 1),
			[6] = Z(BodyRole.Shirt, 2),
			[11] = Z(BodyRole.Pants, 0),
			[12] = Z(BodyRole.Pants, 1),
			[8] = Z(BodyRole.Pants, 2),
			[9] = new ZoneRoles(BodyRole.Undershirt, 0, BodyRole.Shoes, 0, BodyRole.Shoes, 0),
			[10] = new ZoneRoles(BodyRole.Undershirt, 1, BodyRole.Shoes, 1, BodyRole.Shoes, 1)
		},
		MaleHat = new[] { 1, 8, 9, 10, 11, 12, 15 },
		FemaleHat = new[] { 1, 4, 6, 8, 9, 10, 11, 12, 15 },
		StripFemaleHair = true,
		BackPicSize = 0x2800,
		BackFrames = 5,
		BackMale = 0,
		BackFemale = 1,
		FrontCount = 148,
		FrontMale = 135,
		FrontFemale = 136,
		Back = new PicSpec
		{
			Cx = 41.0,
			Cy = 30.0,
			Neck = 40,
			X0 = 20,
			X1 = 60,
			Yaw = 215,
			Roles = new Dictionary<int, ZoneRoles>
			{
				[11] = Z(BodyRole.Shirt, 0),
				[12] = Z(BodyRole.Shirt, 1),
				[5] = Z(BodyRole.Undershirt, 0),
				[6] = Z(BodyRole.Undershirt, 1),
				[7] = Z(BodyRole.Undershirt, 0),
				[9] = Z(BodyRole.Undershirt, 0),
				[10] = Z(BodyRole.Undershirt, 1),
				[2] = Z(BodyRole.Skin, 0),
				[3] = Z(BodyRole.Skin, 1)
			}
		},
		FrontMaleSpec = new PicSpec
		{
			Cx = 31.5,
			Cy = 18.0,
			Neck = 24,
			X0 = 22,
			X1 = 41,
			Yaw = 0,
			Roles = new Dictionary<int, ZoneRoles>
			{
				[12] = Z(BodyRole.Shirt, 0),
				[13] = Z(BodyRole.Shirt, 1),
				[9] = Z(BodyRole.Undershirt, 1),
				[14] = Z(BodyRole.Undershirt, 0),
				[5] = Z(BodyRole.Pants, 0),
				[6] = Z(BodyRole.Pants, 1),
				[7] = Z(BodyRole.Pants, 2),
				[1] = Z(BodyRole.Skin, 0),
				[2] = Z(BodyRole.Skin, 0),
				[3] = Z(BodyRole.Skin, 1),
				[4] = Z(BodyRole.Skin, 2)
			}
		},
		FrontFemaleSpec = new PicSpec
		{
			Cx = 31.0,
			Cy = 19.0,
			Neck = 25,
			X0 = 21,
			X1 = 41,
			Yaw = 0,
			Strip = new[]
			{
				(17, 31, 36, 52, new[] { 1, 2, 3, 4, 8, 15 }),
				(31, 37, 36, 52, new[] { 4, 8 })
			},
			Roles = new Dictionary<int, ZoneRoles>
			{
				[5] = Z(BodyRole.Shirt, 0),
				[6] = Z(BodyRole.Shirt, 1),
				[7] = Z(BodyRole.Shirt, 2),
				[12] = Z(BodyRole.Pants, 0),
				[13] = Z(BodyRole.Pants, 1),
				[14] = Z(BodyRole.Shoes, 0),
				[9] = Z(BodyRole.Shoes, 1),
				[1] = Z(BodyRole.Skin, 0),
				[2] = Z(BodyRole.Skin, 0),
				[3] = Z(BodyRole.Skin, 1),
				[4] = Z(BodyRole.Skin, 2)
			}
		}
	};

	/// <summary>
	/// Emerald (read from the Spanish BPES ROM): Brendan's white cap (5, 9, E) with its green band (A, B), May's green
	/// bandana and brown hair (4, 7, 8). The body starts a row higher than in FireRed. Battle back: Brendan's for both
	/// (his cap's spikes are part of the head box), 4 frames.
	/// </summary>
	public static readonly TrainerGame Emerald = new TrainerGame
	{
		Name = "Emerald",
		MaleName = "Brendan",
		FemaleName = "May",
		Looks = new[]
		{
			("normal", 0, 89, 18),
			("machbike", 1, 90, 9),
			("acrobike", 63, 91, 27),
			("surf", 2, 92, 6),
			("fieldmove", 3, 93, 5),
			("fish", 137, 138, 12),
			("watering", 191, 192, 6),
			("decorating", 193, 194, 1)
		},
		NeckY = 22,
		AnchorY = 17.0,
		PaletteTags = new[] { (0x1100, 0x1101), (0x1110, 0x1111) },
		MaleRoles = new Dictionary<int, ZoneRoles>
		{
			[1] = Z(BodyRole.Skin, 0),
			[2] = Z(BodyRole.Skin, 0),
			[3] = Z(BodyRole.Skin, 1),
			[4] = new ZoneRoles(BodyRole.Skin, 2, BodyRole.Skin, 2, BodyRole.Shoes, 1),
			[8] = new ZoneRoles(BodyRole.Shirt, 1, BodyRole.Pants, 1, BodyRole.Shoes, 1),
			[12] = new ZoneRoles(BodyRole.Undershirt, 0, BodyRole.Pants, 0, BodyRole.Shoes, 0),
			[13] = new ZoneRoles(BodyRole.Undershirt, 1, BodyRole.Pants, 0, BodyRole.Shoes, 0)
		},
		FemaleRoles = new Dictionary<int, ZoneRoles>
		{
			[1] = Z(BodyRole.Skin, 0),
			[2] = Z(BodyRole.Skin, 0),
			[3] = Z(BodyRole.Skin, 1),
			[4] = new ZoneRoles(BodyRole.Skin, 2, BodyRole.Skin, 2, BodyRole.Shoes, 1),
			[12] = new ZoneRoles(BodyRole.Shirt, 0, BodyRole.Pants, 0, BodyRole.Shoes, 0),
			[13] = new ZoneRoles(BodyRole.Shirt, 1, BodyRole.Pants, 0, BodyRole.Shoes, 0),
			[6] = new ZoneRoles(BodyRole.Shirt, 2, BodyRole.Pants, 1, BodyRole.Pants, 1),
			[14] = new ZoneRoles(BodyRole.Undershirt, 0, BodyRole.None, 0, BodyRole.None, 0),
			[9] = new ZoneRoles(BodyRole.Undershirt, 1, BodyRole.None, 0, BodyRole.None, 0)
		},
		MaleHat = new[] { 5, 8, 9, 10, 11, 14, 15 },
		FemaleHat = new[] { 4, 5, 7, 8, 9, 10, 11, 14, 15 },
		StripFemaleHair = false,
		BackPicSize = 0x2000,
		BackFrames = 4,
		BackMale = 0,
		BackFemale = 1,
		FrontCount = 93,
		FrontMale = 71,
		FrontFemale = 72,
		Back = new PicSpec
		{
			Cx = 37.0,
			Cy = 28.0,
			Neck = 40,
			X0 = 8,
			X1 = 50,
			Yaw = 215,
			Roles = new Dictionary<int, ZoneRoles>
			{
				[1] = Z(BodyRole.Skin, 0),
				[2] = Z(BodyRole.Skin, 0),
				[3] = Z(BodyRole.Skin, 1),
				[4] = Z(BodyRole.Skin, 2),
				[5] = Z(BodyRole.Shirt, 0),
				[6] = Z(BodyRole.Shirt, 1),
				[7] = Z(BodyRole.Shirt, 2),
				[8] = Z(BodyRole.Shirt, 2),
				[12] = Z(BodyRole.Undershirt, 0),
				[13] = Z(BodyRole.Undershirt, 1)
			}
		},
		FrontMaleSpec = new PicSpec
		{
			Cx = 29.5,
			Cy = 17.0,
			Neck = 24,
			X0 = 14,
			X1 = 40,
			Yaw = 0,
			ZoneRows = (38, 55),
			Roles = new Dictionary<int, ZoneRoles>
			{
				[1] = Z(BodyRole.Skin, 0),
				[2] = Z(BodyRole.Skin, 0),
				[3] = Z(BodyRole.Skin, 1),
				[4] = Z(BodyRole.Skin, 2),
				[5] = new ZoneRoles(BodyRole.Shirt, 0, BodyRole.Pants, 0, BodyRole.Shoes, 0),
				[6] = new ZoneRoles(BodyRole.Shirt, 1, BodyRole.Pants, 1, BodyRole.Shoes, 1),
				[7] = new ZoneRoles(BodyRole.Shirt, 2, BodyRole.Pants, 2, BodyRole.Shoes, 2),
				[8] = new ZoneRoles(BodyRole.Shirt, 2, BodyRole.Pants, 2, BodyRole.Shoes, 2),
				[12] = new ZoneRoles(BodyRole.Undershirt, 0, BodyRole.Undershirt, 0, BodyRole.Shoes, 0),
				[13] = new ZoneRoles(BodyRole.Undershirt, 1, BodyRole.Undershirt, 1, BodyRole.Shoes, 0)
			}
		},
		FrontFemaleSpec = new PicSpec
		{
			Cx = 28.0,
			Cy = 17.0,
			Neck = 25,
			X0 = 12,
			X1 = 42,
			Yaw = 0,
			ZoneRows = (40, 53),
			Roles = new Dictionary<int, ZoneRoles>
			{
				[1] = Z(BodyRole.Skin, 0),
				[2] = Z(BodyRole.Skin, 0),
				[3] = Z(BodyRole.Skin, 1),
				[4] = Z(BodyRole.Skin, 2),
				[12] = new ZoneRoles(BodyRole.Shirt, 0, BodyRole.Pants, 0, BodyRole.Shoes, 0),
				[13] = new ZoneRoles(BodyRole.Shirt, 1, BodyRole.Pants, 1, BodyRole.Shoes, 1),
				[14] = new ZoneRoles(BodyRole.Undershirt, 0, BodyRole.None, 0, BodyRole.None, 0),
				[9] = new ZoneRoles(BodyRole.Undershirt, 1, BodyRole.None, 0, BodyRole.None, 0),
				[5] = new ZoneRoles(BodyRole.None, 0, BodyRole.Pants, 0, BodyRole.Shoes, 2),
				[6] = new ZoneRoles(BodyRole.Shirt, 2, BodyRole.Pants, 1, BodyRole.Shoes, 2)
			}
		}
	};
}
