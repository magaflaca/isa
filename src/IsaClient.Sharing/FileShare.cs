using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using IsaClient.Core;
using IsaClient.Transfer;
using Terraria;

namespace IsaClient.Sharing;

/// <summary>What a shared file is, by its name and content (what its card and its window offer).</summary>
public enum FileType
{
	Other,
	Picture,
	Character,
	Schematic,
	Item,
	RainbowLetters,
	TileMap,
	World,
	Text,
	Archive,
	Audio
}

/// <summary>
/// Any file in the chat, beside the pictures, characters, styles and schematics. The paperclip at the end of the chat
/// bar (and Ctrl+V of copied files) sends each thing as what it is (<see cref="Attach"/>): pictures as pictures, a .plr
/// as a character, a schematic as a schematic, a .isaitem as IsaClient items, a style as a style; anything else as a
/// file card. The card's tag travels like the other shared things (the pipe, or the chat's word packets with it off),
/// and the file itself always goes as Imgur pictures (<see cref="ImageTransfer"/>: the pipe is too narrow for files).
/// The tag: "[i/a&lt;hex&gt;:1869]" with flags, an 8-byte id (start of its SHA-256), the size, the Imgur ids (or the id of
/// an index picture holding them, for big files) and the name.
/// </summary>
public static class FileShare
{
	public const char Letter = 'a';

	/// <summary>A Present for players without IsaClient.</summary>
	public const int VanillaItem = 1869;

	/// <summary>Pictures named in the tag itself; more go in an index picture (the tag stays short for the pipe).</summary>
	private const int DirectIds = 6;

	/// <summary>Received files up to this size download as soon as their card shows; bigger ones on a click.</summary>
	public const long AutoDownload = 8L * 1024 * 1024;

	public sealed class Entry
	{
		public string Id;

		public string Name;

		public long Size;

		public bool Mine;

		/// <summary>Where the file is on this computer once it's here (or the original, for yours).</summary>
		public string Path;

		/// <summary>The chat tag, once its pictures are up (yours) or as it arrived.</summary>
		public string Tag;

		public bool Indexed;

		public string[] ImgurIds;

		public volatile float Progress;

		public volatile bool Busy;

		public volatile bool Ready;

		public volatile string Error;

		public ImageTransferResult Sent;

		public FileType Type => TypeOf(Name, Path);

		public bool Here => Path != null && File.Exists(Path);
	}

	private static readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

	public static string ReceivedFolder => System.IO.Path.Combine(ClientSettings.IsaClientFolder, "Received", "Files");

	public static Entry Get(string id)
	{
		lock (_entries)
		{
			return (id != null && _entries.TryGetValue(id, out Entry e)) ? e : null;
		}
	}

	#region What a file is

	private static readonly string[] _audio = { ".mp3", ".ogg", ".wav", ".m4a", ".flac", ".opus" };

	private static readonly string[] _archives = { ".zip", ".rar", ".7z", ".gz", ".tar" };

	private static readonly string[] _text = { ".txt", ".md", ".json", ".log", ".cfg", ".ini", ".csv" };

	public static FileType TypeOf(string name, string path = null)
	{
		string ext = System.IO.Path.GetExtension(name ?? "").ToLowerInvariant();
		if ((name ?? "").EndsWith(".rainbow.xml", StringComparison.OrdinalIgnoreCase))
		{
			return FileType.RainbowLetters;
		}
		if (IsaClient.ChatImages.PictureFiles.IsPicture(name ?? "") || IsaClient.ChatImages.PictureFiles.IsVideo(name ?? ""))
		{
			return FileType.Picture;
		}
		switch (ext)
		{
		case ".plr":
			return FileType.Character;
		case ".teditsch":
			return FileType.Schematic;
		case ".isaitem":
			return FileType.Item;
		case ".tilemap":
			return FileType.TileMap;
		case ".wld":
			return FileType.World;
		case ".xml":
			return (IsRainbow(path)) ? FileType.RainbowLetters : FileType.Text;
		}
		if (_audio.Contains(ext))
		{
			return FileType.Audio;
		}
		if (_archives.Contains(ext))
		{
			return FileType.Archive;
		}
		return _text.Contains(ext) ? FileType.Text : FileType.Other;
	}

	/// <summary>A RainbowLetters XML file (the letters .rainbow writes with)?</summary>
	private static bool IsRainbow(string path)
	{
		try
		{
			return path != null && File.Exists(path) && new FileInfo(path).Length < 4 * 1024 * 1024 && IsaClient.Gameplay.RainbowFont.Parse(File.ReadAllText(path)).Count > 0;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>The item drawn for a type (cards, chips, the window).</summary>
	public static int Icon(FileType type) => type switch
	{
		FileType.Picture => Terraria.ID.ItemID.TheScreamer,
		FileType.Character => Terraria.ID.ItemID.Mannequin,
		FileType.Schematic => Terraria.ID.ItemID.ArchitectGizmoPack,
		FileType.Item => Terraria.ID.ItemID.Chest,
		FileType.RainbowLetters => Terraria.ID.ItemID.RainbowRod,
		FileType.TileMap => Terraria.ID.ItemID.Paintbrush,
		FileType.World => Terraria.ID.ItemID.WorldGlobe,
		FileType.Text => Terraria.ID.ItemID.Book,
		FileType.Archive => Terraria.ID.ItemID.Safe,
		FileType.Audio => Terraria.ID.ItemID.MusicBox,
		_ => VanillaItem
	};

	public static string TypeName(FileType type) => type switch
	{
		FileType.RainbowLetters => "Rainbow letters",
		FileType.TileMap => "Tilemap",
		FileType.World => "World",
		FileType.Text => "Text",
		FileType.Archive => "Archive",
		FileType.Audio => "Audio",
		FileType.Item => "IsaClient items",
		FileType.Character => "Character",
		FileType.Schematic => "Schematic",
		FileType.Picture => "Picture",
		_ => "File"
	};

	#endregion

	#region Sending

	/// <summary>
	/// The paperclip, Ctrl+V and drops: each file goes as what it is. Pictures and videos as pictures, characters,
	/// schematics, IsaClient items and styles as their own cards, anything else as a file. Returns false when nothing
	/// could be attached.
	/// </summary>
	public static bool Attach(string path)
	{
		if (string.IsNullOrEmpty(path) || !File.Exists(path) || Main.gameMenu)
		{
			return false;
		}
		try
		{
			string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
			if ((IsaClient.ChatImages.PictureFiles.IsPicture(path) || IsaClient.ChatImages.PictureFiles.IsVideo(path)) && IsaClient.ChatImages.ChatImages.Enabled)
			{
				return IsaClient.ChatImages.ChatAttachments.AddFile(path);
			}
			if (ext == ".plr")
			{
				return ShareChat.LinkCharacter(path);
			}
			if (ext == ".teditsch")
			{
				ShareChat.LinkSchematic(path);
				return true;
			}
			if (ext == ".isaitem")
			{
				List<IsaClient.Items.ItemDesign> designs = IsaClient.Items.ItemFiles.Read(File.ReadAllText(path));
				if (designs.Count > 0)
				{
					foreach (IsaClient.Items.ItemDesign d in designs)
					{
						IsaClient.Items.ItemShare.PutInChat(d);
					}
					return true;
				}
			}
			if ((ext == ".json" || ext == ".txt") && new FileInfo(path).Length < 64 * 1024)
			{
				StyleLook style = StyleLook.FromTemplate(File.ReadAllText(path));
				if (style != null)
				{
					ShareChat.LinkStyle(style, System.IO.Path.GetFileNameWithoutExtension(path));
					return true;
				}
			}
			return AttachFile(File.ReadAllBytes(path), System.IO.Path.GetFileName(path), path);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			ClientChat.Error("Could not attach " + System.IO.Path.GetFileName(path) + ": " + ex.Message);
			return false;
		}
	}

	/// <summary>Rainbow letters (RainbowLetters XML), attached as a file card the others can add letters from.</summary>
	public static bool AttachRainbow(string xml, string title)
	{
		string name = (string.IsNullOrWhiteSpace(title) ? "Rainbow letters" : title.Trim()) + ".rainbow.xml";
		foreach (char c in System.IO.Path.GetInvalidFileNameChars())
		{
			name = name.Replace(c, '_');
		}
		return AttachFile(Encoding.UTF8.GetBytes(xml), name, null);
	}

	/// <summary>Any bytes as a file card above the chat bar: its pictures start going up now, the message waits for them.</summary>
	public static bool AttachFile(byte[] data, string name, string localPath)
	{
		if (data == null || Main.gameMenu)
		{
			return false;
		}
		if (data.LongLength > ImageTransfer.MaxFileBytes)
		{
			ClientChat.Error(IsaClient.Languages.L10n.F("Files up to {0} MB.", ImageTransfer.MaxFileBytes >> 20));
			return false;
		}
		string id = IdOf(data);
		Entry e = Get(id);
		if (e == null || e.Error != null)
		{
			string path = localPath;
			if (path == null)
			{
				// Made here (rainbow letters...): kept as a file so its card can open it too.
				string folder = System.IO.Path.Combine(ClientSettings.IsaClientFolder, "Shared", "Files");
				Directory.CreateDirectory(folder);
				path = Free(System.IO.Path.Combine(folder, name));
				File.WriteAllBytes(path, data);
			}
			e = new Entry
			{
				Id = id,
				Name = name,
				Size = data.LongLength,
				Mine = true,
				Path = path,
				Busy = true
			};
			lock (_entries)
			{
				_entries[id] = e;
			}
			Entry entry = e;
			Task.Run(delegate
			{
				Upload(entry, data);
			});
		}
		// Its card goes in the chat line where you type, like a character or a schematic; Enter sends it (TakeTag).
		string pending = PendingTag(e);
		lock (_pending)
		{
			_pending[pending] = e;
		}
		if (!Main.drawingPlayerChat)
		{
			Main.OpenPlayerChat();
		}
		Terraria.UI.Chat.ChatManager.AddChatText(Terraria.GameContent.FontAssets.MouseText.Value, pending + " ", Microsoft.Xna.Framework.Vector2.One);
		return true;
	}

	/// <summary>Cards put in the chat line, by their tag, until their message goes.</summary>
	private static readonly Dictionary<string, Entry> _pending = new Dictionary<string, Entry>();

	/// <summary>
	/// The card of a file of yours still going up, for the chat line: flag 2, its id, size and name (its pictures aren't
	/// known yet; the message carries the real tag).
	/// </summary>
	private static string PendingTag(Entry e)
	{
		List<byte> b = new List<byte> { 2 };
		b.AddRange(ShareKinds.FromHex(e.Id));
		uint s = (uint)Math.Min(uint.MaxValue, e.Size);
		b.Add((byte)(s >> 24));
		b.Add((byte)(s >> 16));
		b.Add((byte)(s >> 8));
		b.Add((byte)s);
		b.AddRange(Encoding.UTF8.GetBytes(ShortName(e.Name)));
		return "[i/" + Letter + ShareKinds.Hex(b.ToArray()) + ":" + VanillaItem + "]";
	}

	/// <summary>
	/// Enter in the chat: a file card in the line becomes an attachment of the message, which waits for its pictures
	/// and then carries its real tag (like a schematic waiting for its upload). False when it isn't one of ours.
	/// </summary>
	public static bool TakeTag(string tag)
	{
		Entry e;
		lock (_pending)
		{
			if (!_pending.TryGetValue(tag, out e))
			{
				return false;
			}
		}
		Entry owner = e;
		return IsaClient.ChatImages.ChatAttachments.AddShare(owner.Name, tag, (sb, box) => FileCard.DrawIcon(sb, owner.Type, box, box.Height / 24f, 1f), () => owner.Ready, () => owner.Progress, () => owner.Error, null, () => owner.Tag);
	}

	private static void Upload(Entry e, byte[] data)
	{
		try
		{
			ImageTransferResult result = ImageTransfer.Upload(data, e.Name, p => e.Progress = p * 0.97f);
			string[] ids = Ids(result.Code);
			bool indexed = false;
			if (ids.Length > DirectIds)
			{
				// Many pictures: their list goes in one more picture, and the tag names that one.
				ImageTransferResult index = ImageTransfer.Upload(Encoding.ASCII.GetBytes(result.Code), "index");
				result.DeleteHashes = result.DeleteHashes.Concat(index.DeleteHashes).ToArray();
				ids = Ids(index.Code);
				indexed = true;
			}
			e.Sent = result;
			e.ImgurIds = ids;
			e.Indexed = indexed;
			e.Tag = BuildTag(e.Id, e.Size, ids, indexed, e.Name);
			RememberSent(e, result);
			e.Progress = 1f;
			e.Ready = true;
		}
		catch (Exception ex)
		{
			e.Error = ex.Message;
			ClientLog.LogException(ex);
		}
		finally
		{
			e.Busy = false;
		}
	}

	/// <summary>Your files' pictures, to take them down later (.delfile).</summary>
	private static void RememberSent(Entry e, ImageTransferResult result)
	{
		try
		{
			string list = System.IO.Path.Combine(ClientSettings.IsaClientFolder, "Transfer", "Sent.txt");
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(list));
			File.AppendAllText(list, result.Code + "\t" + e.Name + "\t" + string.Join(",", result.DeleteHashes) + "\t" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + Environment.NewLine);
		}
		catch
		{
		}
	}

	#endregion

	#region Tags

	private static string[] Ids(string code)
	{
		return code.Substring(ImageTransfer.Prefix.Length).Split('.');
	}

	private static string BuildTag(string id, long size, string[] ids, bool indexed, string name)
	{
		List<byte> b = new List<byte> { (byte)(indexed ? 1 : 0) };
		b.AddRange(ShareKinds.FromHex(id));
		uint s = (uint)Math.Min(uint.MaxValue, size);
		b.Add((byte)(s >> 24));
		b.Add((byte)(s >> 16));
		b.Add((byte)(s >> 8));
		b.Add((byte)s);
		b.Add((byte)ids.Length);
		foreach (string imgur in ids)
		{
			byte[] ascii = Encoding.ASCII.GetBytes(imgur);
			b.Add((byte)ascii.Length);
			b.AddRange(ascii);
		}
		string hexName = ShareKinds.Hex(Encoding.UTF8.GetBytes(ShortName(name)));
		return "[i/" + Letter + ShareKinds.Hex(b.ToArray()) + hexName + ":" + VanillaItem + "]";
	}

	/// <summary>A name for the tag (24 bytes at most, so the tag fits the pipe): the end of the name goes, never its extension.</summary>
	private static string ShortName(string name)
	{
		name = (name ?? "").Trim();
		if (Encoding.UTF8.GetByteCount(name) <= 24)
		{
			return name;
		}
		string ext = name.EndsWith(".rainbow.xml", StringComparison.OrdinalIgnoreCase) ? name.Substring(name.Length - 12) : System.IO.Path.GetExtension(name);
		if (Encoding.UTF8.GetByteCount(ext) > 14)
		{
			ext = "";
		}
		string stem = name.Substring(0, name.Length - ext.Length);
		while (stem.Length > 1 && Encoding.UTF8.GetByteCount(stem + "~" + ext) > 24)
		{
			stem = stem.Substring(0, stem.Length - 1);
		}
		return stem + "~" + ext;
	}

	/// <summary>The card for a tag's hex body (what follows the letter); null when it isn't one.</summary>
	public static Terraria.UI.Chat.TextSnippet Snippet(string body)
	{
		byte[] bytes = ShareKinds.FromHex(body);
		if (bytes == null || bytes.Length < 15)
		{
			return null;
		}
		bool indexed = (bytes[0] & 1) != 0;
		string id = ShareKinds.Hex(bytes.Skip(1).Take(8).ToArray());
		if ((bytes[0] & 2) != 0)
		{
			// A card still in someone's chat line (yours while you type): drawn from what is known.
			Entry known = Get(id) ?? new Entry
			{
				Id = id,
				Name = Encoding.UTF8.GetString(bytes, 13, bytes.Length - 13),
				Size = ((long)bytes[9] << 24) | ((long)bytes[10] << 16) | ((long)bytes[11] << 8) | bytes[12]
			};
			return new FileCard(known, pending: true);
		}
		long size = ((long)bytes[9] << 24) | ((long)bytes[10] << 16) | ((long)bytes[11] << 8) | bytes[12];
		int count = bytes[13];
		int at = 14;
		string[] ids = new string[count];
		for (int i = 0; i < count; i++)
		{
			if (at >= bytes.Length || at + 1 + bytes[at] > bytes.Length)
			{
				return null;
			}
			ids[i] = Encoding.ASCII.GetString(bytes, at + 1, bytes[at]);
			at += 1 + bytes[at];
		}
		string name = Encoding.UTF8.GetString(bytes, at, bytes.Length - at);
		Entry e;
		lock (_entries)
		{
			if (!_entries.TryGetValue(id, out e))
			{
				e = new Entry
				{
					Id = id,
					Name = string.IsNullOrWhiteSpace(name) ? "file" : name,
					Size = size,
					ImgurIds = ids,
					Indexed = indexed,
					Tag = "[i/" + Letter + body + ":" + VanillaItem + "]"
				};
				_entries[id] = e;
			}
			else if (e.ImgurIds == null)
			{
				e.ImgurIds = ids;
				e.Indexed = indexed;
			}
		}
		return new FileCard(e);
	}

	#endregion

	#region Receiving

	/// <summary>Starts getting a file (nothing when it's here or on the way).</summary>
	public static void Download(Entry e)
	{
		if (e == null || e.Here || e.Busy || e.ImgurIds == null || e.ImgurIds.Length == 0)
		{
			return;
		}
		e.Busy = true;
		e.Error = null;
		e.Progress = 0f;
		Task.Run(delegate
		{
			try
			{
				string code = ImageTransfer.Prefix + string.Join(".", e.ImgurIds);
				if (e.Indexed)
				{
					code = Encoding.ASCII.GetString(ImageTransfer.Download(code).Data);
				}
				ImageTransferFile file = ImageTransfer.Download(code, p => e.Progress = p);
				if (IdOf(file.Data) != e.Id)
				{
					throw new InvalidDataException("it isn't the file that was linked");
				}
				Directory.CreateDirectory(ReceivedFolder);
				string path = Free(System.IO.Path.Combine(ReceivedFolder, SafeName(file.Name ?? e.Name)));
				File.WriteAllBytes(path, file.Data);
				// The whole name comes with the file (the tag holds a short one).
				e.Name = System.IO.Path.GetFileName(path);
				e.Path = path;
				e.Ready = true;
				e.Progress = 1f;
			}
			catch (Exception ex)
			{
				e.Error = ex.Message;
				ClientLog.LogException(ex);
			}
			finally
			{
				e.Busy = false;
			}
		});
	}

	#endregion

	#region Helpers

	private static string IdOf(byte[] data)
	{
		using SHA256 sha = SHA256.Create();
		return ShareKinds.Hex(sha.ComputeHash(data).Take(8).ToArray());
	}

	private static string SafeName(string name)
	{
		name = System.IO.Path.GetFileName(name ?? "file");
		foreach (char c in System.IO.Path.GetInvalidFileNameChars())
		{
			name = name.Replace(c, '_');
		}
		return string.IsNullOrWhiteSpace(name) ? "file" : name;
	}

	internal static string Free(string path)
	{
		if (!File.Exists(path))
		{
			return path;
		}
		string folder = System.IO.Path.GetDirectoryName(path);
		string name = System.IO.Path.GetFileNameWithoutExtension(path);
		string ext = System.IO.Path.GetExtension(path);
		if (name.EndsWith(".rainbow", StringComparison.OrdinalIgnoreCase))
		{
			ext = ".rainbow" + ext;
			name = name.Substring(0, name.Length - 8);
		}
		for (int i = 2; ; i++)
		{
			string next = System.IO.Path.Combine(folder, name + " (" + i + ")" + ext);
			if (!File.Exists(next))
			{
				return next;
			}
		}
	}

	public static string SizeText(long bytes)
	{
		if (bytes >= 1 << 20)
		{
			return (bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
		}
		return (bytes >= 1024) ? ((bytes / 1024.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " KB") : (bytes + " B");
	}

	#endregion
}
