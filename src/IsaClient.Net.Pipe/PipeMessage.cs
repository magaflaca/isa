#nullable enable
using System;
using System.IO;
using System.Text;

namespace IsaClient.Net.Pipe;

/// <summary>What a transferred blob is: text, a raw byte array, a named file or an image. Self-describing, so the
/// receiver knows how to treat it. For bulk transfers; small game messages use <see cref="PipeNet"/>'s own envelope.</summary>
public enum PipeKind : byte
{
	Raw = 0,
	Text = 1,
	File = 2,
	Image = 3,
}

/// <summary>A self-describing blob: ["TP"][ver][kind][nameLen][name][u32 len][data].</summary>
public sealed class PipeMessage
{
	private const byte MagicT = (byte)'T';

	private const byte MagicP = (byte)'P';

	public const byte Version = 1;

	public PipeKind Kind { get; }

	public string Name { get; }

	public byte[] Data { get; }

	public PipeMessage(PipeKind kind, string name, byte[] data)
	{
		Kind = kind;
		Name = name ?? "";
		Data = data ?? Array.Empty<byte>();
	}

	public byte[] Encode()
	{
		byte[] name = Encoding.UTF8.GetBytes(Name);
		if (name.Length > 255)
		{
			Array.Resize(ref name, 255);
		}
		byte[] buf = new byte[2 + 3 + name.Length + 4 + Data.Length];
		int o = 0;
		buf[o++] = MagicT;
		buf[o++] = MagicP;
		buf[o++] = Version;
		buf[o++] = (byte)Kind;
		buf[o++] = (byte)name.Length;
		Array.Copy(name, 0, buf, o, name.Length);
		o += name.Length;
		Bits.WriteU32(buf, o, (uint)Data.Length);
		o += 4;
		Array.Copy(Data, 0, buf, o, Data.Length);
		return buf;
	}

	public static bool TryDecode(byte[] blob, out PipeMessage? msg)
	{
		msg = null;
		if (blob == null || blob.Length < 5 || blob[0] != MagicT || blob[1] != MagicP)
		{
			return false;
		}
		int o = 2;
		if (blob[o++] != Version)
		{
			return false;
		}
		PipeKind kind = (PipeKind)blob[o++];
		int nameLen = blob[o++];
		if (blob.Length < o + nameLen + 4)
		{
			return false;
		}
		string name = Encoding.UTF8.GetString(blob, o, nameLen);
		o += nameLen;
		long dataLen = Bits.ReadU32(blob, o);
		o += 4;
		if (blob.Length < o + dataLen)
		{
			return false;
		}
		byte[] data = new byte[dataLen];
		Array.Copy(blob, o, data, 0, (int)dataLen);
		msg = new PipeMessage(kind, name, data);
		return true;
	}

	public static PipeMessage Text(string text)
	{
		return new PipeMessage(PipeKind.Text, "", Encoding.UTF8.GetBytes(text ?? ""));
	}

	public static PipeMessage FromFile(string path)
	{
		byte[] data = File.ReadAllBytes(path);
		string ext = Path.GetExtension(path).ToLowerInvariant();
		PipeKind kind = (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp" || ext == ".webp") ? PipeKind.Image : PipeKind.File;
		return new PipeMessage(kind, Path.GetFileName(path), data);
	}
}
