using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace IsaClient.Gba.Trainer;

/// <summary>
/// Reads the look of a .plr file (t2p/plr.py, mirrors Player.LoadPlayer): name, hairstyle, skin variant and the seven
/// colors. Used for characters that aren't in the game's player list (and by the tests, without the game).
/// </summary>
public static class PlrReader
{
	private static readonly byte[] Key = Encoding.Unicode.GetBytes("h3y_gUyZ");

	public static TrainerLook Read(string path)
	{
		byte[] raw = File.ReadAllBytes(path);
		byte[] data;
		using (RijndaelManaged aes = new RijndaelManaged { Mode = CipherMode.CBC, Padding = PaddingMode.None, Key = Key, IV = Key })
		using (ICryptoTransform dec = aes.CreateDecryptor())
		{
			int n = raw.Length / 16 * 16;
			data = dec.TransformFinalBlock(raw, 0, n);
		}
		using BinaryReader r = new BinaryReader(new MemoryStream(data));
		int release = r.ReadInt32();
		if (release < 230)
		{
			throw new InvalidDataException("Player file too old (release " + release + ")");
		}
		r.ReadBytes(20);
		TrainerLook look = new TrainerLook { Name = r.ReadString() };
		r.ReadByte();
		r.ReadInt64();
		look.Hair = r.ReadInt32();
		if (look.Hair >= 228)
		{
			look.Hair = 0;
		}
		r.ReadByte();
		if (release >= 283)
		{
			r.ReadByte();
		}
		r.ReadBytes(3);
		look.SkinVariant = r.ReadByte();
		r.ReadBytes(16);
		r.ReadBytes(3);
		if (release >= 256)
		{
			r.ReadByte();
		}
		if (release >= 324)
		{
			r.ReadByte();
		}
		if (release >= 260)
		{
			r.ReadBytes(6);
		}
		if (release >= 182)
		{
			r.ReadByte();
		}
		if (release >= 128)
		{
			r.ReadInt32();
		}
		if (release >= 254)
		{
			r.ReadInt64();
		}
		Rgb C() => new Rgb(r.ReadByte(), r.ReadByte(), r.ReadByte());
		look.HairColor = C();
		look.SkinColor = C();
		look.EyeColor = C();
		look.ShirtColor = C();
		look.UndershirtColor = C();
		look.PantsColor = C();
		look.ShoeColor = C();
		return look;
	}
}
