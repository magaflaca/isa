using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using IsaClient.ChatImages;

namespace IsaClient.Transfer;

/// <summary>A file sent as pictures: the code to give out, and what takes the pictures down again.</summary>
public sealed class ImageTransferResult
{
	/// <summary>"isaf:ID.ID.ID": everything a receiver needs (<see cref="ImageTransfer.Download"/>).</summary>
	public string Code;

	public string Name;

	public long Bytes;

	public int Pictures;

	/// <summary>One per picture: <see cref="ImageTransfer.Delete"/> takes them off Imgur.</summary>
	public string[] DeleteHashes;
}

/// <summary>A received file.</summary>
public sealed class ImageTransferFile
{
	public string Name;

	public byte[] Data;
}

/// <summary>
/// Any file through Imgur, as pictures. Measured (2026-11-01, random data, read back byte by byte): Imgur keeps a PNG
/// exactly as long as the upload is under 2 MiB (2,097,152 bytes); from there on it turns it into a JPEG and the data is
/// gone. So a file is (deflated when that helps and) cut into pieces of <see cref="PieceBytes"/>, each the pixels of
/// its own plain PNG (3 bytes a pixel: about one picture per 2 MB). Every picture is read back after the upload and
/// compared; the receiver checks each piece (CRC32) and the whole file (SHA-256), so a damaged file is refused, never
/// misread. Slow calls: use them off the main thread (or the Async forms).
/// </summary>
public static class ImageTransfer
{
	public const string Prefix = "isaf:";

	/// <summary>File bytes in one picture: its PNG stays near 2.0 MB, safely under Imgur's 2 MiB.</summary>
	public const int PieceBytes = 2000000;

	/// <summary>Biggest file sent or received (all of it is kept in memory).</summary>
	public const long MaxFileBytes = 256L * 1024 * 1024;

	private const uint Magic = 0x46415349u; // "ISAF"

	private const byte Version = 1;

	private const int PieceHeader = 4 + 1 + 2 + 2 + 4 + 8 + 4;

	/// <summary>How many pictures a file of this size takes (before compression).</summary>
	public static int PicturesFor(long bytes)
	{
		return (int)Math.Max(1L, (bytes + 300 + PieceBytes - 1) / PieceBytes);
	}

	/// <summary>True when <paramref name="text"/> is (or holds) a transfer code.</summary>
	public static bool IsCode(string text)
	{
		return Ids(text) != null;
	}

	#region Sending

	/// <summary>
	/// Sends a file: deflate when smaller, cut into pictures, upload each and read it back. <paramref name="progress"/>
	/// gets 0..1. Throws with a short message (and takes down what it uploaded) when anything fails.
	/// </summary>
	public static ImageTransferResult Upload(byte[] data, string name, Action<float> progress = null, Func<bool> cancelled = null)
	{
		if (data == null || data.LongLength > MaxFileBytes)
		{
			throw new InvalidOperationException("files up to " + (MaxFileBytes >> 20) + " MB");
		}
		name = Path.GetFileName(string.IsNullOrEmpty(name) ? "file" : name);
		byte[] package = Package(data, name);
		List<byte[]> pieces = new List<byte[]>();
		for (int at = 0; at < package.Length; at += PieceBytes)
		{
			pieces.Add(Slice(package, at, Math.Min(PieceBytes, package.Length - at)));
		}
		if (pieces.Count > ushort.MaxValue)
		{
			throw new InvalidOperationException("too many pictures");
		}
		byte[] id = new byte[8];
		Array.Copy(Sha256(package), id, 8);
		List<ImgurClient.Upload> done = new List<ImgurClient.Upload>();
		try
		{
			for (int i = 0; i < pieces.Count; i++)
			{
				byte[] png = Png(Piece(pieces[i], i, pieces.Count, id));
				float from = (float)i / pieces.Count;
				float span = 1f / pieces.Count;
				ImgurClient.Upload upload = SendChecked(png, i, p => progress?.Invoke(from + span * p), cancelled);
				done.Add(upload);
			}
		}
		catch
		{
			foreach (ImgurClient.Upload upload in done)
			{
				TryDelete(upload.DeleteHash);
			}
			throw;
		}
		progress?.Invoke(1f);
		return new ImageTransferResult
		{
			Code = Prefix + string.Join(".", done.Select(u => u.Id)),
			Name = name,
			Bytes = data.LongLength,
			Pictures = done.Count,
			DeleteHashes = done.Select(u => u.DeleteHash).ToArray()
		};
	}

	public static Task<ImageTransferResult> UploadAsync(byte[] data, string name, Action<float> progress = null, Func<bool> cancelled = null)
	{
		return Task.Run(() => Upload(data, name, progress, cancelled));
	}

	/// <summary>Uploads one picture and reads it back; a copy Imgur changed is taken down and sent again once.</summary>
	private static ImgurClient.Upload SendChecked(byte[] png, int index, Action<float> progress, Func<bool> cancelled)
	{
		byte[] want = Pixels(png);
		for (int attempt = 0; attempt < 2; attempt++)
		{
			ImgurClient.Upload upload = ImgurClient.Send(png, "isaf" + index + ".png", "image/png", p => progress?.Invoke(p * 0.8f), cancelled);
			if (string.IsNullOrEmpty(upload?.Id))
			{
				throw new InvalidOperationException("Imgur didn't take the picture");
			}
			byte[] back = ImgurClient.Download("https://i.imgur.com/" + upload.Id + ".png", 8 << 20, cancelled, "image/png");
			if (Same(Pixels(back), want))
			{
				progress?.Invoke(1f);
				return upload;
			}
			TryDelete(upload.DeleteHash);
		}
		throw new InvalidOperationException("Imgur changed the picture (it turns PNGs over 2 MiB into JPEG)");
	}

	/// <summary>Takes a sent file's pictures off Imgur (the code stops working).</summary>
	public static void Delete(ImageTransferResult result)
	{
		foreach (string hash in result?.DeleteHashes ?? new string[0])
		{
			TryDelete(hash);
		}
	}

	private static void TryDelete(string hash)
	{
		try
		{
			if (!string.IsNullOrEmpty(hash))
			{
				ImgurClient.Delete(hash);
			}
		}
		catch
		{
		}
	}

	#endregion

	#region Receiving

	/// <summary>Downloads a file from its code; checks every piece and the whole file. Throws with a short message.</summary>
	public static ImageTransferFile Download(string code, Action<float> progress = null, Func<bool> cancelled = null)
	{
		string[] ids = Ids(code) ?? throw new InvalidOperationException("not a file code");
		byte[][] pieces = new byte[ids.Length][];
		byte[] fileId = null;
		for (int i = 0; i < ids.Length; i++)
		{
			if (cancelled != null && cancelled())
			{
				throw new OperationCanceledException();
			}
			byte[] png = ImgurClient.Download("https://i.imgur.com/" + ids[i] + ".png", 8 << 20, cancelled, "image/png");
			ReadPiece(Pixels(png), out int index, out int count, out byte[] id, out byte[] data);
			if (count != ids.Length || index != i)
			{
				throw new InvalidDataException("the pictures are not in order");
			}
			if (fileId == null)
			{
				fileId = id;
			}
			else if (!Same(fileId, id))
			{
				throw new InvalidDataException("the pictures belong to different files");
			}
			pieces[i] = data;
			progress?.Invoke((i + 1f) / ids.Length);
		}
		using MemoryStream all = new MemoryStream();
		foreach (byte[] piece in pieces)
		{
			all.Write(piece, 0, piece.Length);
		}
		byte[] package = all.ToArray();
		byte[] hash = Sha256(package);
		for (int k = 0; k < 8; k++)
		{
			if (hash[k] != fileId[k])
			{
				throw new InvalidDataException("the file doesn't check out");
			}
		}
		return Unpackage(package);
	}

	public static Task<ImageTransferFile> DownloadAsync(string code, Action<float> progress = null, Func<bool> cancelled = null)
	{
		return Task.Run(() => Download(code, progress, cancelled));
	}

	/// <summary>The Imgur ids of a code found in <paramref name="text"/>; null when there is none.</summary>
	private static string[] Ids(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		int at = text.IndexOf(Prefix, StringComparison.OrdinalIgnoreCase);
		if (at < 0)
		{
			return null;
		}
		int end = at + Prefix.Length;
		while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '.'))
		{
			end++;
		}
		string[] ids = text.Substring(at + Prefix.Length, end - at - Prefix.Length).Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
		return (ids.Length == 0 || ids.Any(i => i.Length < 5 || i.Length > 10)) ? null : ids;
	}

	#endregion

	#region Format

	/// <summary>
	/// The file as sent: flags (1 = deflated), name (UTF-8, length-prefixed), original length, SHA-256 of the original,
	/// then the (deflated) bytes.
	/// </summary>
	private static byte[] Package(byte[] data, string name)
	{
		byte[] packed;
		using (MemoryStream buffer = new MemoryStream())
		{
			using (DeflateStream deflate = new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
			{
				deflate.Write(data, 0, data.Length);
			}
			packed = buffer.ToArray();
		}
		bool deflated = packed.Length < data.Length;
		using MemoryStream stream = new MemoryStream();
		using BinaryWriter writer = new BinaryWriter(stream);
		writer.Write((byte)(deflated ? 1 : 0));
		byte[] nameBytes = Encoding.UTF8.GetBytes(name);
		writer.Write((ushort)Math.Min(nameBytes.Length, 1024));
		writer.Write(nameBytes, 0, Math.Min(nameBytes.Length, 1024));
		writer.Write(data.LongLength);
		writer.Write(Sha256(data));
		byte[] body = deflated ? packed : data;
		writer.Write(body, 0, body.Length);
		writer.Flush();
		return stream.ToArray();
	}

	private static ImageTransferFile Unpackage(byte[] package)
	{
		using BinaryReader reader = new BinaryReader(new MemoryStream(package));
		bool deflated = (reader.ReadByte() & 1) != 0;
		string name = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16()));
		long length = reader.ReadInt64();
		byte[] sha = reader.ReadBytes(32);
		if (length < 0 || length > MaxFileBytes)
		{
			throw new InvalidDataException("bad size");
		}
		byte[] data = new byte[length];
		Stream body = reader.BaseStream;
		using (Stream source = deflated ? new DeflateStream(body, CompressionMode.Decompress) : body)
		{
			int got = 0;
			while (got < data.Length)
			{
				int n = source.Read(data, got, data.Length - got);
				if (n <= 0)
				{
					throw new InvalidDataException("the file is cut short");
				}
				got += n;
			}
		}
		if (!Same(Sha256(data), sha))
		{
			throw new InvalidDataException("the file doesn't check out");
		}
		return new ImageTransferFile
		{
			Name = Path.GetFileName(name),
			Data = data
		};
	}

	/// <summary>One picture's bytes: magic, version, index, count, length, file id, CRC32 of the data, then the data.</summary>
	private static byte[] Piece(byte[] data, int index, int count, byte[] id)
	{
		using MemoryStream stream = new MemoryStream(PieceHeader + data.Length);
		using BinaryWriter writer = new BinaryWriter(stream);
		writer.Write(Magic);
		writer.Write(Version);
		writer.Write((ushort)index);
		writer.Write((ushort)count);
		writer.Write(data.Length);
		writer.Write(id, 0, 8);
		writer.Write(Crc32(data));
		writer.Write(data);
		writer.Flush();
		return stream.ToArray();
	}

	private static void ReadPiece(byte[] pixels, out int index, out int count, out byte[] id, out byte[] data)
	{
		using BinaryReader reader = new BinaryReader(new MemoryStream(pixels));
		if (pixels.Length < PieceHeader || reader.ReadUInt32() != Magic)
		{
			throw new InvalidDataException("a picture holds no file");
		}
		if (reader.ReadByte() != Version)
		{
			throw new InvalidDataException("made by a newer IsaClient");
		}
		index = reader.ReadUInt16();
		count = reader.ReadUInt16();
		int length = reader.ReadInt32();
		id = reader.ReadBytes(8);
		uint crc = reader.ReadUInt32();
		if (length < 0 || length > pixels.Length - PieceHeader)
		{
			throw new InvalidDataException("a picture is cut short");
		}
		data = reader.ReadBytes(length);
		if (Crc32(data) != crc)
		{
			throw new InvalidDataException("a picture was changed on the way");
		}
	}

	#endregion

	#region Pictures

	/// <summary>Bytes as the pixels (R, G, B) of a near-square plain PNG: 8-bit RGB, no gamma or color profile.</summary>
	private static byte[] Png(byte[] bytes)
	{
		int pixels = Math.Max(1, (bytes.Length + 2) / 3);
		int width = (int)Math.Ceiling(Math.Sqrt(pixels));
		int height = (pixels + width - 1) / width;
		byte[] raw = new byte[height * (width * 3 + 1)];
		for (int y = 0; y < height; y++)
		{
			int from = y * width * 3;
			int count = Math.Min(width * 3, bytes.Length - from);
			if (count > 0)
			{
				Buffer.BlockCopy(bytes, from, raw, y * (width * 3 + 1) + 1, count);
			}
		}
		using MemoryStream png = new MemoryStream();
		png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
		byte[] ihdr = new byte[13];
		BigEndian(ihdr, 0, (uint)width);
		BigEndian(ihdr, 4, (uint)height);
		ihdr[8] = 8;
		ihdr[9] = 2;
		Chunk(png, "IHDR", ihdr);
		Chunk(png, "IDAT", Zlib(raw));
		Chunk(png, "IEND", new byte[0]);
		return png.ToArray();
	}

	/// <summary>A PNG's pixels as R, G, B bytes in order (whatever form Imgur saved it in).</summary>
	private static byte[] Pixels(byte[] png)
	{
		using MemoryStream stream = new MemoryStream(png);
		using Bitmap bitmap = new Bitmap(stream);
		int width = bitmap.Width;
		int height = bitmap.Height;
		BitmapData locked = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
		try
		{
			byte[] row = new byte[locked.Stride];
			byte[] result = new byte[width * height * 3];
			for (int y = 0; y < height; y++)
			{
				Marshal.Copy(locked.Scan0 + y * locked.Stride, row, 0, locked.Stride);
				int o = y * width * 3;
				for (int x = 0; x < width; x++)
				{
					// GDI+ keeps them B, G, R.
					result[o + x * 3] = row[x * 3 + 2];
					result[o + x * 3 + 1] = row[x * 3 + 1];
					result[o + x * 3 + 2] = row[x * 3];
				}
			}
			return result;
		}
		finally
		{
			bitmap.UnlockBits(locked);
		}
	}

	private static byte[] Zlib(byte[] raw)
	{
		using MemoryStream output = new MemoryStream();
		output.WriteByte(0x78);
		output.WriteByte(0x01);
		using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
		{
			deflate.Write(raw, 0, raw.Length);
		}
		uint a = 1;
		uint b = 0;
		foreach (byte v in raw)
		{
			a = (a + v) % 65521;
			b = (b + a) % 65521;
		}
		byte[] adler = new byte[4];
		BigEndian(adler, 0, (b << 16) | a);
		output.Write(adler, 0, 4);
		return output.ToArray();
	}

	private static void Chunk(Stream png, string type, byte[] data)
	{
		byte[] head = new byte[8];
		BigEndian(head, 0, (uint)data.Length);
		Encoding.ASCII.GetBytes(type, 0, 4, head, 4);
		png.Write(head, 0, 8);
		png.Write(data, 0, data.Length);
		byte[] typeAndData = new byte[4 + data.Length];
		Buffer.BlockCopy(head, 4, typeAndData, 0, 4);
		Buffer.BlockCopy(data, 0, typeAndData, 4, data.Length);
		byte[] crc = new byte[4];
		BigEndian(crc, 0, Crc32(typeAndData));
		png.Write(crc, 0, 4);
	}

	private static void BigEndian(byte[] buffer, int at, uint value)
	{
		buffer[at] = (byte)(value >> 24);
		buffer[at + 1] = (byte)(value >> 16);
		buffer[at + 2] = (byte)(value >> 8);
		buffer[at + 3] = (byte)value;
	}

	#endregion

	#region Helpers

	private static byte[] Slice(byte[] data, int at, int count)
	{
		byte[] slice = new byte[count];
		Buffer.BlockCopy(data, at, slice, 0, count);
		return slice;
	}

	private static bool Same(byte[] a, byte[] b)
	{
		if (a == null || b == null || a.Length < b.Length)
		{
			return false;
		}
		// The picture may hold a few padding bytes past the data.
		for (int i = 0; i < b.Length; i++)
		{
			if (a[i] != b[i])
			{
				return false;
			}
		}
		return true;
	}

	private static byte[] Sha256(byte[] data)
	{
		using SHA256 sha = SHA256.Create();
		return sha.ComputeHash(data);
	}

	private static readonly uint[] _crcTable = MakeCrcTable();

	private static uint[] MakeCrcTable()
	{
		uint[] table = new uint[256];
		for (uint n = 0; n < 256; n++)
		{
			uint c = n;
			for (int k = 0; k < 8; k++)
			{
				c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
			}
			table[n] = c;
		}
		return table;
	}

	private static uint Crc32(byte[] data)
	{
		uint c = 0xFFFFFFFFu;
		foreach (byte b in data)
		{
			c = _crcTable[(c ^ b) & 0xFF] ^ (c >> 8);
		}
		return c ^ 0xFFFFFFFFu;
	}

	#endregion
}
