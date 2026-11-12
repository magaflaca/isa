using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Sharing;

/// <summary>
/// Characters linked in chat. The .plr goes with its ".plr.isa" (the IsaClient items in its inventory), unencrypted and
/// compressed so it's smaller on the pipe; the receiver encrypts it again, so the file it keeps is byte for byte the
/// same. Received characters are saved under IsaClient/Received/Characters and open in the character file dialog.
/// </summary>
public static class CharacterShare
{
	private static readonly byte[] Key = new UnicodeEncoding().GetBytes("h3y_gUyZ");

	public static string Folder => Path.Combine(ClientSettings.IsaClientFolder, "Received", "Characters");

	/// <summary>A character file (and its IsaClient items) ready to send. Null with a reason when it can't be.</summary>
	public static byte[] Pack(string plrPath, out string error)
	{
		error = null;
		try
		{
			byte[] plr = File.ReadAllBytes(plrPath);
			string isaPath = plrPath + ".isa";
			byte[] isa = File.Exists(isaPath) ? File.ReadAllBytes(isaPath) : new byte[0];
			byte[] plain = Decrypt(plr);
			bool decrypted = plain != null;
			using MemoryStream inner = new MemoryStream();
			using (BinaryWriter w = new BinaryWriter(inner, Encoding.UTF8, leaveOpen: true))
			{
				byte[] body = decrypted ? plain : plr;
				w.Write(body.Length);
				w.Write(body);
				w.Write(isa.Length);
				w.Write(isa);
			}
			using MemoryStream output = new MemoryStream();
			output.WriteByte(1);
			output.WriteByte((byte)(decrypted ? 1 : 0));
			using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
			{
				inner.Position = 0;
				inner.CopyTo(deflate);
			}
			byte[] payload = output.ToArray();
			if (payload.Length > ShareTransfer.MaxBytes)
			{
				error = "That character is too big to send (" + payload.Length / 1024 + " KB).";
				return null;
			}
			return payload;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			error = "Could not read the character: " + ex.Message;
			return null;
		}
	}

	/// <summary>A received character written to the Received folder; returns the .plr's path (null if it's not valid).</summary>
	public static string Unpack(byte[] payload, string name, string code)
	{
		try
		{
			if (payload == null || payload.Length < 3 || payload[0] != 1)
			{
				return null;
			}
			bool decrypted = payload[1] == 1;
			byte[] body;
			byte[] isa;
			using (MemoryStream input = new MemoryStream(payload, 2, payload.Length - 2))
			using (DeflateStream deflate = new DeflateStream(input, CompressionMode.Decompress))
			using (BinaryReader r = new BinaryReader(deflate))
			{
				body = r.ReadBytes(r.ReadInt32());
				isa = r.ReadBytes(r.ReadInt32());
			}
			byte[] plr = decrypted ? Encrypt(body) : body;
			Directory.CreateDirectory(Folder);
			string path = Path.Combine(Folder, Safe(name) + "-" + code + ".plr");
			File.WriteAllBytes(path, plr);
			string isaPath = path + ".isa";
			if (isa.Length > 0)
			{
				File.WriteAllBytes(isaPath, isa);
			}
			else if (File.Exists(isaPath))
			{
				File.Delete(isaPath);
			}
			return path;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			return null;
		}
	}

	/// <summary>The character's name inside a payload (it's the first text of the file), for naming it before its card shows.</summary>
	public static string NameIn(byte[] payload)
	{
		try
		{
			if (payload == null || payload.Length < 3 || payload[0] != 1 || payload[1] != 1)
			{
				return "";
			}
			using MemoryStream input = new MemoryStream(payload, 2, payload.Length - 2);
			using DeflateStream deflate = new DeflateStream(input, CompressionMode.Decompress);
			using BinaryReader r = new BinaryReader(deflate);
			byte[] body = r.ReadBytes(r.ReadInt32());
			using BinaryReader plr = new BinaryReader(new MemoryStream(body));
			int version = plr.ReadInt32();
			if (version >= 135)
			{
				// The file metadata (magic, type, revision, flags) comes first.
				plr.ReadUInt64();
				plr.ReadUInt32();
				plr.ReadUInt64();
			}
			return plr.ReadString();
		}
		catch
		{
			return "";
		}
	}

	private static string Safe(string name)
	{
		string clean = string.IsNullOrWhiteSpace(name) ? "Character" : name.Trim();
		foreach (char c in Path.GetInvalidFileNameChars())
		{
			clean = clean.Replace(c, '_');
		}
		return (clean.Length > 24) ? clean.Substring(0, 24) : clean;
	}

	/// <summary>The game's own encryption (the files are AES with a fixed key); no padding either way keeps it exact.</summary>
	private static byte[] Decrypt(byte[] file)
	{
		if (file.Length == 0 || file.Length % 16 != 0)
		{
			return null;
		}
		try
		{
			using RijndaelManaged aes = new RijndaelManaged { Padding = PaddingMode.None };
			using ICryptoTransform decryptor = aes.CreateDecryptor(Key, Key);
			byte[] plain = decryptor.TransformFinalBlock(file, 0, file.Length);
			// A real character starts with its game version (a small number): otherwise send the file as it is.
			return (BitConverter.ToInt32(plain, 0) is int version && version > 0 && version < 2000) ? plain : null;
		}
		catch
		{
			return null;
		}
	}

	private static byte[] Encrypt(byte[] plain)
	{
		using RijndaelManaged aes = new RijndaelManaged { Padding = PaddingMode.None };
		using ICryptoTransform encryptor = aes.CreateEncryptor(Key, Key);
		return encryptor.TransformFinalBlock(plain, 0, plain.Length);
	}

	/// <summary>The chat tag of a character: its code, look and name ride in the tag, the file goes through the pipe.</summary>
	public static string Tag(string code, Player look)
	{
		StyleLook style = StyleLook.Of(look);
		return "[i/" + ShareKinds.Letter(ShareKind.Character) + code + style.Hex() + ShareKinds.NameHex(look.name) + ":" + ShareKinds.Item(ShareKind.Character) + "]";
	}

	#region archive.org (the pipe off)

	private static string ArchiveItem(string code) => "isaclient-plr-" + code;

	private static string ArchiveFile(string code) => code + ".plr";

	/// <summary>One of yours going up to archive.org, for when the pipe is off (started once; null if it isn't yours).</summary>
	public static ArchiveStore.Job Upload(string code)
	{
		ShareTransfer.Transfer t = ShareTransfer.Get(code);
		if (t == null || !t.Sending || !t.Complete)
		{
			return null;
		}
		lock (t)
		{
			string name = ShareInbox.Find(code, ShareKind.Character)?.Name ?? "";
			return t.Archive ??= ArchiveStore.Upload(ArchiveItem(code), ArchiveFile(code), t.Bytes, "IsaClient character: " + name, "terraria;character;isaclient");
		}
	}

	/// <summary>One of yours going up to Imgur inside its card, for when the pipe is off (started once).</summary>
	public static ArchiveStore.Job UploadImage(string code)
	{
		ShareTransfer.Transfer t = ShareTransfer.Get(code);
		if (t == null || !t.Sending || !t.Complete)
		{
			return null;
		}
		lock (t)
		{
			ShareInbox.Item item = ShareInbox.Find(code, ShareKind.Character);
			string name = item?.Name ?? "";
			StyleLook look = item?.Look;
			byte[] bytes = t.Bytes;
			return t.Image = ShareImage.Upload(ShareKind.Character, code, () => bytes, () => ShareImage.CharacterCover(name, look));
		}
	}

	/// <summary>A character linked in chat with the pipe off, with its Imgur card: fetched from it, else from archive.org.</summary>
	public static void FetchImage(string code, string link)
	{
		ShareTransfer.Transfer t = ShareTransfer.Expect(code, ShareKind.Character);
		if (t.Complete || t.Sending || t.Image != null)
		{
			return;
		}
		t.Image = ShareImage.Fetch(ShareKind.Character, code, link, delegate(byte[] bytes)
		{
			if (ShareTransfer.CodeOf(bytes) != code)
			{
				return false;
			}
			Main.QueueMainThreadAction(() => ShareTransfer.Deliver(code, ShareKind.Character, bytes));
			return true;
		}, () => Main.QueueMainThreadAction(() => Fetch(code)));
	}

	/// <summary>A character linked in chat with the pipe off: fetched from archive.org (once).</summary>
	public static void Fetch(string code)
	{
		ShareTransfer.Transfer t = ShareTransfer.Expect(code, ShareKind.Character);
		if (t.Complete || t.Sending || t.Archive != null)
		{
			return;
		}
		t.Archive = ArchiveStore.Fetch(ArchiveStore.Url(ArchiveItem(code), ArchiveFile(code)), delegate(byte[] bytes)
		{
			if (ShareTransfer.CodeOf(bytes) != code)
			{
				return false;
			}
			Main.QueueMainThreadAction(() => ShareTransfer.Deliver(code, ShareKind.Character, bytes));
			return true;
		}, () => t.Complete);
	}

	#endregion

	/// <summary>Registers a character file to send and returns its chat tag (null, saying why, if it can't go).</summary>
	public static string Link(string plrPath, Player loaded, out ShareInbox.Item item)
	{
		item = null;
		byte[] payload = Pack(plrPath, out string error);
		if (payload == null)
		{
			ClientChat.Error(error);
			return null;
		}
		string code = ShareTransfer.Register(ShareKind.Character, payload);
		item = ShareInbox.Remember(code, ShareKind.Character, loaded?.name ?? Path.GetFileNameWithoutExtension(plrPath), mine: true, look: (loaded != null) ? StyleLook.Of(loaded) : null);
		item.Path ??= plrPath;
		return Tag(code, loaded ?? new Player { name = Path.GetFileNameWithoutExtension(plrPath) });
	}
}
