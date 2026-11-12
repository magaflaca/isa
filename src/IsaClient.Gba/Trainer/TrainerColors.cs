using System;
using System.Collections.Generic;

namespace IsaClient.Gba.Trainer;

/// <summary>An opaque 8-bit color (alpha lives in <see cref="Rgba"/>).</summary>
public readonly struct Rgb : IEquatable<Rgb>
{
	public readonly byte R;

	public readonly byte G;

	public readonly byte B;

	public Rgb(int r, int g, int b)
	{
		R = (byte)r;
		G = (byte)g;
		B = (byte)b;
	}

	public Rgb Snap8 => new Rgb(R / 8 * 8, G / 8 * 8, B / 8 * 8);

	public bool Equals(Rgb o) => R == o.R && G == o.G && B == o.B;

	public override bool Equals(object obj) => obj is Rgb o && Equals(o);

	public override int GetHashCode() => (R << 16) | (G << 8) | B;

	public override string ToString() => "(" + R + "," + G + "," + B + ")";
}

/// <summary>A pixel of a composed picture: <see cref="A"/> false is transparent.</summary>
public struct Rgba
{
	public Rgb C;

	public bool A;

	public Rgba(Rgb c)
	{
		C = c;
		A = true;
	}
}

/// <summary>
/// The GBA colour ramps of one Terraria character (terraria-to-pokemon t2p/paint.py <c>Look</c>): FRLG-like skin, hair
/// kept readable against the face and the black outline, clothes from the character's own colors. Uses Python's
/// colorsys/round semantics so the result matches the reference converter.
/// </summary>
public sealed class TrainerColors
{
	public static readonly Rgb Outline = new Rgb(16, 16, 16);

	public bool Male;

	/// <summary>0 light, 1 mid, 2 shade, 3 dark.</summary>
	public Rgb[] Skin;

	/// <summary>0 highlight, 1 base, 2 shade, 3 outline.</summary>
	public Rgb[] Hair;

	public Rgb HairOutline;

	public Rgb Eye;

	public Rgb Shirt;

	public Rgb Undershirt;

	public Rgb Pants;

	public Rgb Shoes;

	public TrainerColors(TrainerLook look)
	{
		Male = look.Male;
		Rgb skin = PokemonSkin(look.SkinColor);
		Skin = new[] { Lighten(skin, 0.35), skin, Shade(skin, 0.14), Shade(skin, 0.38) };
		Rgb hair = YouthfulHair(EnsureVisible(look.HairColor, 72));
		if (Lum(hair) > Lum(skin) - 40.0 && Dist(hair, skin) < 110.0)
		{
			hair = Shade(hair, 0.3);
		}
		Hair = new[] { Lighten(hair, 0.35), hair, Shade(hair, 0.28), Shade(hair, 0.55) };
		HairOutline = (Lum(Hair[3]) > 40.0) ? Hair[3] : Outline;
		Eye = (Lum(look.EyeColor) > 70.0) ? Shade(look.EyeColor, 0.55) : look.EyeColor;
		Shirt = EnsureVisible(look.ShirtColor, 40);
		Undershirt = EnsureVisible(look.UndershirtColor, 40);
		Pants = EnsureVisible(look.PantsColor, 40);
		Shoes = EnsureVisible(look.ShoeColor, 40);
	}

	/// <summary>Colors that resist being merged when the palette is cut to 15 (so the face never melts into the hair).</summary>
	public IEnumerable<Rgb> Protected()
	{
		yield return Skin[0];
		yield return Skin[1];
		yield return Skin[2];
		foreach (Rgb h in Hair)
		{
			yield return h;
		}
		yield return Eye;
	}

	/// <summary>A baked head symbol (see tools/gba/make_heads.py) in this character's colors.</summary>
	public Rgb Symbol(int s)
	{
		switch (s)
		{
		case 1:
		case 2:
		case 3:
		case 4:
			return Skin[s - 1];
		case 5:
		case 6:
		case 7:
		case 8:
			return Hair[s - 5];
		case 9:
			return Eye;
		case 11:
			return HairOutline;
		default:
			return Outline;
		}
	}

	/// <summary>compose.role_color: skin tones 1-3, clothes light/mid/dark.</summary>
	public Rgb Role(BodyRole role, int tone)
	{
		if (role == BodyRole.Skin)
		{
			return Skin[1 + tone];
		}
		Rgb b = role switch
		{
			BodyRole.Shirt => Shirt,
			BodyRole.Undershirt => Undershirt,
			BodyRole.Pants => Pants,
			_ => Shoes
		};
		return tone switch
		{
			0 => Lighten(b, 0.08),
			1 => Shade(b, 0.2),
			_ => Shade(b, 0.45)
		};
	}

	#region Colour helpers (t2p/paint.py)

	public static double Lum(Rgb c) => 0.3 * c.R + 0.59 * c.G + 0.11 * c.B;

	private static double Dist(Rgb a, Rgb b)
	{
		double r = a.R - b.R;
		double g = a.G - b.G;
		double bl = a.B - b.B;
		return Math.Sqrt(r * r + g * g + bl * bl);
	}

	private static int PyRound(double v) => (int)Math.Round(v, MidpointRounding.ToEven);

	private static double PyMod(double a, double b) => a - b * Math.Floor(a / b);

	private static Rgb Mix(Rgb a, Rgb b, double t)
	{
		return new Rgb(PyRound(a.R + (b.R - a.R) * t), PyRound(a.G + (b.G - a.G) * t), PyRound(a.B + (b.B - a.B) * t));
	}

	private static Rgb EnsureVisible(Rgb c, double minLum)
	{
		double l = Lum(c);
		if (l >= minLum)
		{
			return c;
		}
		return Mix(c, new Rgb(96, 96, 112), (minLum - l) / minLum);
	}

	public static Rgb Shade(Rgb c, double amount)
	{
		ToHls(c, out double h, out double l, out double s);
		double l2 = Math.Max(0.0, l * (1.0 - amount));
		double s2 = (s > 0.05) ? Math.Min(1.0, s + amount * 0.3) : s;
		double h2 = PyMod(h + 0.015 * amount, 1.0);
		return FromHls(h2, l2, s2);
	}

	public static Rgb Lighten(Rgb c, double amount)
	{
		ToHls(c, out double h, out double l, out double s);
		double l2 = l + (1.0 - l) * amount;
		double h2 = PyMod(h - 0.01 * amount, 1.0);
		return FromHls(h2, l2, s);
	}

	private static Rgb PokemonSkin(Rgb c)
	{
		ToHls(c, out double h, out double l, out double s);
		if (l > 0.45)
		{
			l += (0.86 - l) * 0.55;
		}
		s *= 0.7;
		return FromHls(h, l, s);
	}

	private static Rgb YouthfulHair(Rgb c)
	{
		ToHls(c, out double h, out double l, out double s);
		if (s < 0.18 && l > 0.55)
		{
			if (s < 0.05)
			{
				h = 0.6;
			}
			s = Math.Max(s, 0.12);
			l = Math.Min(l, 0.86);
		}
		return FromHls(h, l, s);
	}

	/// <summary>Python's colorsys.rgb_to_hls on 0..1 channels.</summary>
	private static void ToHls(Rgb c, out double h, out double l, out double s)
	{
		double r = c.R / 255.0;
		double g = c.G / 255.0;
		double b = c.B / 255.0;
		double maxc = Math.Max(r, Math.Max(g, b));
		double minc = Math.Min(r, Math.Min(g, b));
		double sumc = maxc + minc;
		double rangec = maxc - minc;
		l = sumc / 2.0;
		if (minc == maxc)
		{
			h = 0.0;
			s = 0.0;
			return;
		}
		s = (l <= 0.5) ? (rangec / sumc) : (rangec / (2.0 - maxc - minc));
		double rc = (maxc - r) / rangec;
		double gc = (maxc - g) / rangec;
		double bc = (maxc - b) / rangec;
		if (r == maxc)
		{
			h = bc - gc;
		}
		else if (g == maxc)
		{
			h = 2.0 + rc - bc;
		}
		else
		{
			h = 4.0 + gc - rc;
		}
		h = PyMod(h / 6.0, 1.0);
	}

	private static Rgb FromHls(double h, double l, double s)
	{
		double r;
		double g;
		double b;
		if (s == 0.0)
		{
			r = g = b = l;
		}
		else
		{
			double m2 = (l <= 0.5) ? (l * (1.0 + s)) : (l + s - l * s);
			double m1 = 2.0 * l - m2;
			r = V(m1, m2, h + 1.0 / 3.0);
			g = V(m1, m2, h);
			b = V(m1, m2, h - 1.0 / 3.0);
		}
		return new Rgb(PyRound(r * 255.0), PyRound(g * 255.0), PyRound(b * 255.0));
	}

	private static double V(double m1, double m2, double hue)
	{
		hue = PyMod(hue, 1.0);
		if (hue < 1.0 / 6.0)
		{
			return m1 + (m2 - m1) * hue * 6.0;
		}
		if (hue < 0.5)
		{
			return m2;
		}
		if (hue < 2.0 / 3.0)
		{
			return m1 + (m2 - m1) * (2.0 / 3.0 - hue) * 6.0;
		}
		return m1;
	}

	#endregion
}

/// <summary>What a Terraria character looks like (no armor: only the body colors and the hairstyle).</summary>
public sealed class TrainerLook
{
	public string Name = "";

	/// <summary>0-based hairstyle (texture Player_Hair_{Hair+1}).</summary>
	public int Hair;

	public int SkinVariant;

	public Rgb HairColor;

	public Rgb SkinColor;

	public Rgb EyeColor;

	public Rgb ShirtColor;

	public Rgb UndershirtColor;

	public Rgb PantsColor;

	public Rgb ShoeColor;

	/// <summary>The skin variants Terraria draws as a man.</summary>
	public bool Male => SkinVariant is 0 or 1 or 2 or 3 or 8 or 10;

	/// <summary>A short fingerprint of everything that changes the sprite (to cache patched ROMs).</summary>
	public string Key => Hair + "-" + SkinVariant + "-" + Hex(HairColor) + Hex(SkinColor) + Hex(EyeColor) + Hex(ShirtColor) + Hex(UndershirtColor) + Hex(PantsColor) + Hex(ShoeColor);

	private static string Hex(Rgb c) => c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2");
}
