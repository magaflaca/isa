using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;

namespace IsaClient.Sharing;

/// <summary>What is linked in chat: its tag option letter and the item everyone else sees in its place.</summary>
public enum ShareKind : byte
{
	Character = 1,
	Style = 2,
	Schematic = 3
}

public static class ShareKinds
{
	/// <summary>The item vanilla players see where the card is (Mannequin, Paintbrush, Architect Gizmo Pack).</summary>
	public static int Item(ShareKind kind) => kind switch
	{
		ShareKind.Character => 498,
		ShareKind.Style => 1071,
		_ => 3061
	};

	public static char Letter(ShareKind kind) => kind switch
	{
		ShareKind.Character => 'k',
		ShareKind.Style => 'y',
		_ => 'w'
	};

	public static string Hex(byte[] bytes)
	{
		StringBuilder b = new StringBuilder(bytes.Length * 2);
		foreach (byte x in bytes)
		{
			b.Append(x.ToString("x2"));
		}
		return b.ToString();
	}

	public static byte[] FromHex(string hex)
	{
		if (hex == null || hex.Length % 2 != 0)
		{
			return null;
		}
		byte[] bytes = new byte[hex.Length / 2];
		for (int i = 0; i < bytes.Length; i++)
		{
			if (!byte.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
			{
				return null;
			}
		}
		return bytes;
	}

	/// <summary>A name for a chat tag: short, as hex (tags can't hold brackets or colons).</summary>
	public static string NameHex(string name)
	{
		// At most 24 bytes, so a tag always fits in the pipe's attachment message (accents take two bytes).
		name = (name ?? "").Trim();
		while (Encoding.UTF8.GetByteCount(name) > 24)
		{
			name = name.Substring(0, name.Length - 1);
		}
		return Hex(Encoding.UTF8.GetBytes(name));
	}

	public static string NameFromHex(string hex)
	{
		byte[] bytes = FromHex(hex);
		return (bytes == null) ? "" : Encoding.UTF8.GetString(bytes);
	}
}

/// <summary>
/// A character-creation style: hair, clothing style, voice and every body color, the same things as the game's
/// "Copy player template" in character creation. Small enough (28 bytes) to ride inside its chat tag.
/// </summary>
public sealed class StyleLook
{
	public const int Size = 28;

	public int Hair;

	public int SkinVariant;

	public int Voice;

	public float VoicePitch;

	public Color HairColor = Color.White;

	public Color EyeColor = Color.White;

	public Color SkinColor = Color.White;

	public Color ShirtColor = Color.White;

	public Color UnderShirtColor = Color.White;

	public Color PantsColor = Color.White;

	public Color ShoeColor = Color.White;

	/// <summary>Who it came from or what it's called (shown on its card).</summary>
	public string Name = "";

	public static StyleLook Of(Player p)
	{
		return new StyleLook
		{
			Hair = p.hair,
			SkinVariant = p.skinVariant,
			Voice = p.voiceVariant,
			VoicePitch = p.voicePitchOffset,
			HairColor = p.hairColor,
			EyeColor = p.eyeColor,
			SkinColor = p.skinColor,
			ShirtColor = p.shirtColor,
			UnderShirtColor = p.underShirtColor,
			PantsColor = p.pantsColor,
			ShoeColor = p.shoeColor,
			Name = p.name ?? ""
		};
	}

	/// <summary>Puts this look on <paramref name="p"/> (nothing else changes).</summary>
	public void ApplyTo(Player p)
	{
		if (Hair >= 0 && Hair < Main.maxHairStyles)
		{
			p.hair = Hair;
		}
		if (SkinVariant >= 0 && SkinVariant < Terraria.ID.PlayerVariantID.Count)
		{
			p.skinVariant = SkinVariant;
		}
		p.voiceVariant = Voice;
		p.voicePitchOffset = VoicePitch;
		p.hairColor = HairColor;
		p.eyeColor = EyeColor;
		p.skinColor = SkinColor;
		p.shirtColor = ShirtColor;
		p.underShirtColor = UnderShirtColor;
		p.pantsColor = PantsColor;
		p.shoeColor = ShoeColor;
	}

	/// <summary>A bare character wearing this look (for heads and portraits; never a real player).</summary>
	public Player Mannequin()
	{
		Player p = new Player();
		p.name = Name;
		ApplyTo(p);
		p.hairDye = 0;
		p.PlayerFrame();
		return p;
	}

	public byte[] ToBytes()
	{
		byte[] b = new byte[Size];
		b[0] = 1;
		BitConverter.GetBytes((ushort)Math.Max(0, Hair)).CopyTo(b, 1);
		b[3] = (byte)SkinVariant;
		b[4] = (byte)Voice;
		BitConverter.GetBytes((short)Math.Round(VoicePitch * 1000f)).CopyTo(b, 5);
		int o = 7;
		foreach (Color c in new[] { HairColor, EyeColor, SkinColor, ShirtColor, UnderShirtColor, PantsColor, ShoeColor })
		{
			b[o++] = c.R;
			b[o++] = c.G;
			b[o++] = c.B;
		}
		return b;
	}

	public static StyleLook FromBytes(byte[] b)
	{
		if (b == null || b.Length < Size || b[0] != 1)
		{
			return null;
		}
		StyleLook s = new StyleLook
		{
			Hair = BitConverter.ToUInt16(b, 1),
			SkinVariant = b[3],
			Voice = b[4],
			VoicePitch = BitConverter.ToInt16(b, 5) / 1000f
		};
		Color[] colors = new Color[7];
		for (int i = 0; i < 7; i++)
		{
			colors[i] = new Color(b[7 + i * 3], b[8 + i * 3], b[9 + i * 3]);
		}
		s.HairColor = colors[0];
		s.EyeColor = colors[1];
		s.SkinColor = colors[2];
		s.ShirtColor = colors[3];
		s.UnderShirtColor = colors[4];
		s.PantsColor = colors[5];
		s.ShoeColor = colors[6];
		return s;
	}

	public string Hex() => ShareKinds.Hex(ToBytes());

	public static StyleLook FromHex(string hex) => FromBytes(ShareKinds.FromHex(hex));

	/// <summary>The game's own template text (Character creation > paste player template takes it).</summary>
	public string ToTemplate()
	{
		return JsonConvert.SerializeObject(new Dictionary<string, object>
		{
			{ "version", 1 },
			{ "hairStyle", Hair },
			{ "clothingStyle", SkinVariant },
			{ "voiceStyle", Voice },
			{ "voicePitch", VoicePitch },
			{ "hairColor", HexOf(HairColor) },
			{ "eyeColor", HexOf(EyeColor) },
			{ "skinColor", HexOf(SkinColor) },
			{ "shirtColor", HexOf(ShirtColor) },
			{ "underShirtColor", HexOf(UnderShirtColor) },
			{ "pantsColor", HexOf(PantsColor) },
			{ "shoeColor", HexOf(ShoeColor) }
		}, Formatting.Indented);
	}

	private static string HexOf(Color c) => "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

	/// <summary>A template copied from character creation (or this client), or null when the text isn't one.</summary>
	public static StyleLook FromTemplate(string text)
	{
		if (string.IsNullOrEmpty(text) || text.IndexOf("hairStyle", StringComparison.OrdinalIgnoreCase) < 0 || text.IndexOf("skinColor", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return null;
		}
		try
		{
			int start = text.IndexOf('{');
			int end = text.LastIndexOf('}');
			if (start < 0 || end <= start)
			{
				return null;
			}
			Dictionary<string, object> raw = JsonConvert.DeserializeObject<Dictionary<string, object>>(text.Substring(start, end - start + 1));
			if (raw == null)
			{
				return null;
			}
			Dictionary<string, object> d = new Dictionary<string, object>();
			foreach (KeyValuePair<string, object> pair in raw)
			{
				d[pair.Key.ToLowerInvariant()] = pair.Value;
			}
			StyleLook s = new StyleLook();
			s.Hair = Int(d, "hairstyle", 0);
			s.SkinVariant = Int(d, "clothingstyle", 0);
			s.Voice = Int(d, "voicestyle", 1);
			s.VoicePitch = d.TryGetValue("voicepitch", out object pitch) ? Convert.ToSingle(pitch, CultureInfo.InvariantCulture) : 0f;
			s.HairColor = ReadColor(d, "haircolor", s.HairColor);
			s.EyeColor = ReadColor(d, "eyecolor", s.EyeColor);
			s.SkinColor = ReadColor(d, "skincolor", s.SkinColor);
			s.ShirtColor = ReadColor(d, "shirtcolor", s.ShirtColor);
			s.UnderShirtColor = ReadColor(d, "undershirtcolor", s.UnderShirtColor);
			s.PantsColor = ReadColor(d, "pantscolor", s.PantsColor);
			s.ShoeColor = ReadColor(d, "shoecolor", s.ShoeColor);
			return s;
		}
		catch
		{
			return null;
		}
	}

	private static int Int(Dictionary<string, object> d, string key, int fallback)
	{
		return d.TryGetValue(key, out object v) ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : fallback;
	}

	private static Color ReadColor(Dictionary<string, object> d, string key, Color fallback)
	{
		if (!d.TryGetValue(key, out object v) || !(v is string s))
		{
			return fallback;
		}
		s = s.TrimStart('#');
		if (s.Length < 6 || !int.TryParse(s.Substring(0, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
		{
			return fallback;
		}
		return new Color((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
	}
}
