using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using IsaClient.Building;
using IsaClient.Core;
using IsaClient.Net.Pipe;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI.Chat;

namespace IsaClient.Sharing;

/// <summary>
/// Characters, character styles and schematics in the chat. They are chat attachments, like pictures: chips above
/// the chat bar (Ctrl+V, the panel's buttons, the character dialog), sent with your message (or a blank line) and
/// shown under it to IsaClient players through the pipe. Each one is an item tag with one extra option ("k" a
/// character, "y" a style, "w" a schematic) that IsaClient draws as a card (head or map, name, and a bar while it
/// travels) and opens on click. With the pipe off the tag goes in the server's chat as short codes
/// (<see cref="IsaClient.ChatImages.ChatData"/>) and the files through archive.org.
/// </summary>
public static class ShareChat
{
	[DllImport("user32.dll")]
	private static extern uint GetClipboardSequenceNumber();

	private static bool _hooked;

	private static object _copiedList;

	private static int _copiedCount;

	private static int _copiedWidth;

	private static int _copiedHeight;

	/// <summary>Windows' clipboard counter when the game's copy last changed (-1: nothing copied yet).</summary>
	private static long _copySequence = -1;

	/// <summary>What the Windows clipboard held when the copy settled (null: not taken yet).</summary>
	private static string _copyClipboard;

	/// <summary>Frames since the copy last changed (a drag changes it every frame).</summary>
	private static int _copySettling = -1;

	/// <summary>Which shared things were put in the chat line, by tag: how to send each when Enter is pressed.</summary>
	private sealed class Linked
	{
		public ShareInbox.Item Item;

		public Func<bool> Ready;

		public Func<float> Progress;

		public Func<string> Error;
	}

	private static readonly System.Collections.Generic.Dictionary<string, Linked> _linked = new System.Collections.Generic.Dictionary<string, Linked>();

	private static readonly Regex _tags = new Regex("\\[i/([kw])([0-9a-f]{8})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	/// <summary>A whole shared tag in a typed line: its option ("k...", "y...", "w...").</summary>
	private static readonly Regex _shared = new Regex("\\[i/(?<option>[kywa][0-9a-fA-F]{24,}):\\d+\\]", RegexOptions.Compiled);

	/// <summary>Called every frame (main thread).</summary>
	public static void Update()
	{
		if (!_hooked)
		{
			_hooked = true;
			ShareTransfer.Completed += ShareInbox.OnCompleted;
			IsaClient.ChatImages.ChatData.Arrived += OnChatArrived;
		}
		ShareTransfer.Update();
		// A new .copy (or a loaded schematic / pixel art): Ctrl+V in chat sends it until Windows' clipboard changes.
		try
		{
			var tiles = Selection.CopiedTiles;
			if (tiles != null && (!ReferenceEquals(tiles, _copiedList) || tiles.Count != _copiedCount || Selection.SchematicSizeX != _copiedWidth || Selection.SchematicSizeY != _copiedHeight))
			{
				bool first = _copiedList == null;
				_copiedList = tiles;
				_copiedCount = tiles.Count;
				_copiedWidth = Selection.SchematicSizeX;
				_copiedHeight = Selection.SchematicSizeY;
				if (!first && tiles.Count > 0)
				{
					_copySequence = GetClipboardSequenceNumber();
					_copyClipboard = null;
					_copySettling = 0;
				}
			}
			else if (_copySettling >= 0 && ++_copySettling > 20)
			{
				// The copy settled: remember what Windows' clipboard held then.
				_copySettling = -1;
				_copyClipboard = ClipboardSignature();
			}
		}
		catch
		{
		}
	}

	/// <summary>What the Windows clipboard holds, in short (formats and text), to tell whether it changed.</summary>
	private static string ClipboardSignature()
	{
		try
		{
			IDataObject data = Clipboard.GetDataObject();
			if (data == null)
			{
				return "";
			}
			string formats = string.Join(",", data.GetFormats());
			string text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
			string files = Clipboard.ContainsFileDropList() ? string.Join("|", Clipboard.GetFileDropList().Cast<string>()) : "";
			return formats + "#" + text.Length + ":" + text.GetHashCode() + "#" + files;
		}
		catch
		{
			return "?";
		}
	}

	/// <summary>A chat line going out: what it links starts going to the IsaClient players here.</summary>
	public static void OnSend(string text)
	{
		if (string.IsNullOrEmpty(text) || text.IndexOf("[i/", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return;
		}
		foreach (Match m in _tags.Matches(text))
		{
			ShareTransfer.Push(m.Groups[2].Value.ToLowerInvariant());
		}
	}

	#region Attaching

	private static ShareKind KindOf(string tag) => tag.StartsWith("[i/k") ? ShareKind.Character : (tag.StartsWith("[i/y") ? ShareKind.Style : ShareKind.Schematic);

	private static readonly Regex _wholeTag = new Regex(@"^\[i/[kywa][0-9a-fA-F]{24,}:\d+\]$", RegexOptions.Compiled);

	/// <summary>Is this exactly one shared tag and nothing else (what may come through the pipe as an attachment)?</summary>
	public static bool IsWholeShareTag(string link) => link != null && _wholeTag.IsMatch(link);

	/// <summary>Is this an attachment link of ours (a tag), not a picture link?</summary>
	public static bool IsShareTag(string link) => link != null && _shared.IsMatch(link);

	/// <summary>Does this text (a message's attachments) hold one of ours?</summary>
	public static bool HasShareTag(string text) => text != null && text.IndexOf("[i/", StringComparison.Ordinal) >= 0 && _shared.IsMatch(text);

	/// <summary>
	/// A shared thing attached above the chat bar, like a picture: it goes with your next message (or a blank line),
	/// to IsaClient players through the pipe, and shows under that message with the server's own format.
	/// </summary>
	private static void Attach(string tag, ShareInbox.Item item, Func<bool> ready = null, Func<float> progress = null, Func<string> error = null)
	{
		if (string.IsNullOrEmpty(tag) || item == null || Main.gameMenu)
		{
			return;
		}
		_linked[tag] = new Linked { Item = item, Ready = ready, Progress = progress, Error = error };
		if (!Main.drawingPlayerChat)
		{
			Main.OpenPlayerChat();
		}
		// Its card shows in the chat line where you type; Enter sends it (see TakeShared).
		ChatManager.AddChatText(FontAssets.MouseText.Value, tag + " ", Vector2.One);
	}

	/// <summary>
	/// Enter in the chat: the shared things in the line become attachments, sent the way pictures are (the text, or a
	/// blank line, through the server; they under it through the pipe). Returns the line without them.
	/// </summary>
	public static string TakeShared(string text)
	{
		if (string.IsNullOrEmpty(text) || text.IndexOf("[i/", StringComparison.Ordinal) < 0)
		{
			return text;
		}
		bool any = false;
		string rest = _shared.Replace(text, m =>
		{
			string tag = m.Value;
			if (tag[3] == FileShare.Letter)
			{
				// A file of yours put in the line becomes an attachment; a file's card copied from a message goes as it is.
				if (FileShare.TakeTag(tag))
				{
					any = true;
					return "";
				}
				return tag;
			}
			_linked.TryGetValue(tag, out Linked linked);
			ShareKind kind = KindOf(tag);
			ShareInbox.Item item = linked?.Item;
			string name = (item == null || string.IsNullOrWhiteSpace(item.Name)) ? kind.ToString() : item.Name;
			Action<Microsoft.Xna.Framework.Graphics.SpriteBatch, Rectangle> draw = (item == null) ? null : (sb, box) => ShareCard.DrawArt(sb, kind, item, box, box.Height / 24f, 1f);
			if (IsaClient.ChatImages.ChatAttachments.AddShare(name, tag, draw, linked?.Ready, linked?.Progress, linked?.Error, () => Sent(tag)))
			{
				any = true;
				return "";
			}
			return tag;
		});
		return any ? Regex.Replace(rest, "  +", " ").Trim() : text;
	}

	/// <summary>Its message went: the pieces start going to the IsaClient players here.</summary>
	private static void Sent(string tag)
	{
		Match m = _shared.Match(tag);
		if (!m.Success)
		{
			return;
		}
		string option = m.Groups["option"].Value.ToLowerInvariant();
		if (option[0] != 'y' && option[0] != FileShare.Letter)
		{
			ShareTransfer.Push(option.Substring(1, 8));
		}
	}

	/// <summary>A character file, attached to the chat line.</summary>
	public static bool LinkCharacter(string plrPath, Player loaded = null)
	{
		loaded ??= IsaClient.Gameplay.CharacterPresets.LoadFile(plrPath, out string error);
		if (loaded == null)
		{
			ClientChat.Error("That .plr can't be read, so it can't be sent.");
			return false;
		}
		string tag = CharacterShare.Link(plrPath, loaded, out ShareInbox.Item item);
		string code = item?.Code;
		// With the pipe off the file goes inside an Imgur picture, with a spare copy on archive.org (both start now).
		Imaged(code);
		Archived(code);
		Attach(tag, item, delegate
		{
			ArchiveStore.Job image = Imaged(code);
			ArchiveStore.Job archive = Archived(code);
			return image == null || image.Done || (!image.Busy && archive != null && archive.Done);
		}, () => (Imaged(code) is ArchiveStore.Job image && image.Busy) ? image.Progress : (Archived(code)?.Progress ?? 1f), delegate
		{
			ArchiveStore.Job image = Imaged(code);
			ArchiveStore.Job archive = Archived(code);
			return (image != null && image.Failed && archive != null && archive.Failed) ? "it couldn't be uploaded (Imgur nor archive.org)" : null;
		});
		return tag != null;
	}

	/// <summary>A character of yours inside an Imgur picture, when the pipe is off (null when it goes through the pipe).</summary>
	private static ArchiveStore.Job Imaged(string code) => (code == null || ViaPipe) ? null : CharacterShare.UploadImage(code);

	/// <summary>Where a shared file is, for the chat packet (pipe off): its Imgur picture once it's up and checked.</summary>
	public static byte[] FileRecord(string tag)
	{
		Match m = _shared.Match(tag ?? "");
		if (!m.Success)
		{
			return null;
		}
		string option = m.Groups["option"].Value.ToLowerInvariant();
		string key = (option[0] == 'k') ? option.Substring(1, 8) : ((option[0] == 'w') ? option.Substring(1, 16) : null);
		if (key == null)
		{
			return null;
		}
		ArchiveStore.Job job = ShareImage.Uploaded((option[0] == 'k') ? ShareKind.Character : ShareKind.Schematic, key);
		return (job != null && job.Done && job.Link != null) ? IsaClient.ChatImages.ChatData.FileRecord(option[0], key, job.Link) : null;
	}

	/// <summary>True when shared things go through the pipe right now (else in the chat, with archive.org).</summary>
	public static bool ViaPipe => PipeNet.Enabled && PipeNet.Ready;

	/// <summary>A character of yours on archive.org, when the pipe is off (null when it goes through the pipe).</summary>
	private static ArchiveStore.Job Archived(string code) => (code == null || ViaPipe) ? null : CharacterShare.Upload(code);

	/// <summary>
	/// Something linked with the pipe off arrived whole in the chat: characters and schematics are fetched from their
	/// Imgur picture (characters from archive.org when there's none or it fails; schematics try archive.org alongside).
	/// </summary>
	private static void OnChatArrived(IsaClient.ChatImages.ChatData.Packet packet)
	{
		foreach (string link in packet.Links)
		{
			Match m = _shared.Match(link ?? "");
			if (!m.Success)
			{
				continue;
			}
			string option = m.Groups["option"].Value.ToLowerInvariant();
			if (option[0] == 'k')
			{
				string code = option.Substring(1, 8);
				ShareTransfer.Transfer t = ShareTransfer.Get(code);
				if (t != null && (t.Sending || t.Complete))
				{
					continue;
				}
				string picture = packet.Files.Where(f => f.Kind == 'k' && f.Key == code).Select(f => f.Link).FirstOrDefault();
				if (picture != null)
				{
					CharacterShare.FetchImage(code, picture);
				}
				else
				{
					CharacterShare.Fetch(code);
				}
			}
			else if (option[0] == 'w')
			{
				string id = option.Substring(1, 16);
				string picture = packet.Files.Where(f => f.Kind == 'w' && f.Key == id).Select(f => f.Link).FirstOrDefault();
				if (picture != null)
				{
					// Its card makes the entry (and starts archive.org as the spare).
					Snippet(option);
					SchematicShare.FetchImage(id, picture);
				}
			}
		}
	}

	/// <summary>A character as it is now (you, or one being edited): saved to IsaClient/Shared/Characters and attached.</summary>
	public static bool LinkPlayer(Player player, string name = null)
	{
		if (player == null)
		{
			return false;
		}
		try
		{
			Player copy = player.SafeSerializedClone();
			copy.name = string.IsNullOrWhiteSpace(name) ? player.name : name;
			string folder = Path.Combine(ClientSettings.IsaClientFolder, "Shared", "Characters");
			Directory.CreateDirectory(folder);
			string file = copy.name;
			foreach (char c in Path.GetInvalidFileNameChars())
			{
				file = file.Replace(c, '_');
			}
			string path = Path.Combine(folder, (string.IsNullOrWhiteSpace(file) ? "Character" : file) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".plr");
			return IsaClient.Gameplay.CharacterPresets.WriteFile(copy, path) && LinkCharacter(path, copy);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			ClientChat.Error("Could not send the character: " + ex.Message);
			return false;
		}
	}

	/// <summary>A style (look), attached to the chat line. It travels inside its tag.</summary>
	public static void LinkStyle(StyleLook look, string name)
	{
		if (look == null)
		{
			return;
		}
		look.Name = name ?? look.Name ?? "";
		ShareInbox.Item item = ShareInbox.Remember(look.Hex(), ShareKind.Style, look.Name, mine: true, look: look);
		Attach("[i/" + ShareKinds.Letter(ShareKind.Style) + look.Hex() + ShareKinds.NameHex(look.Name) + ":" + ShareKinds.Item(ShareKind.Style) + "]", item);
	}

	public static void LinkSchematic(string path)
	{
		AttachSchematic(SchematicShare.Link(path));
	}

	public static void LinkCopied()
	{
		AttachSchematic(SchematicShare.LinkCopied());
	}

	/// <summary>A schematic waits for its archive.org upload before its message goes (when it's too big for the pipe).</summary>
	private static void AttachSchematic(string tag)
	{
		Match m = (tag == null) ? null : _shared.Match(tag);
		if (m == null || !m.Success)
		{
			return;
		}
		SchematicShare.Entry e = SchematicShare.Get(m.Groups["option"].Value.Substring(1, 16).ToLowerInvariant());
		if (e == null)
		{
			return;
		}
		ShareInbox.Item item = ShareInbox.Remember(e.Code, ShareKind.Schematic, e.Name, mine: true, schematic: e);
		// Small ones go through the pipe right away. With the pipe off (or too big for it) the message waits for its
		// Imgur picture (checked), or for archive.org when that fails.
		bool fits = ShareTransfer.Get(e.Code) != null;
		Func<bool> viaPipe = () => fits && ViaPipe;
		Func<ArchiveStore.Job> image = () => ViaPipe ? null : SchematicShare.UploadImage(e);
		image();
		Attach(tag, item, delegate
		{
			if (viaPipe())
			{
				return true;
			}
			ArchiveStore.Job j = image();
			return (j != null && j.Done) || ((j == null || !j.Busy) && !e.Uploading);
		}, () => (image() is ArchiveStore.Job j && j.Busy) ? j.Progress : (e.Uploading ? e.Progress : 1f), delegate
		{
			ArchiveStore.Job j = image();
			return (!viaPipe() && (j == null || j.Failed) && e.Failed && !e.Uploaded) ? "it couldn't be uploaded (Imgur nor archive.org)" : null;
		});
	}

	/// <summary>
	/// Something was just copied with .copy and nothing else was copied in Windows after it: Windows' counter didn't
	/// move, or (it moves on its own sometimes) the clipboard still holds the same thing, and it isn't a picture.
	/// </summary>
	public static bool CopyIsFresh
	{
		get
		{
			if (_copySequence < 0 || Selection.CopiedTiles == null || Selection.CopiedTiles.Count <= 0)
			{
				return false;
			}
			if (GetClipboardSequenceNumber() == (uint)_copySequence)
			{
				return true;
			}
			string now = ClipboardSignature();
			string then = _copyClipboard ?? now;
			return now == then && !Clipboard.ContainsImage() && !Clipboard.ContainsFileDropList();
		}
	}

	/// <summary>
	/// Ctrl+V in the chat: .plr / .TEditSch files, a style template, or a fresh .copy are attached; false when the
	/// clipboard has none of them (pictures and text are pasted as usual).
	/// </summary>
	public static bool TryPaste()
	{
		if (Main.gameMenu)
		{
			return false;
		}
		try
		{
			if (Clipboard.ContainsFileDropList())
			{
				StringCollection files = Clipboard.GetFileDropList();
				bool linked = false;
				foreach (string file in files.Cast<string>())
				{
					string ext = Path.GetExtension(file).ToLowerInvariant();
					if (ext == ".plr")
					{
						linked |= LinkCharacter(file);
					}
					else if (ext == ".teditsch")
					{
						LinkSchematic(file);
						linked = true;
					}
					else if (File.Exists(file) && !IsaClient.ChatImages.PictureFiles.IsPicture(file) && !IsaClient.ChatImages.PictureFiles.IsVideo(file))
					{
						// Items, styles, rainbow letters or any other file (pictures stay with the pictures' paste).
						linked |= FileShare.Attach(file);
					}
				}
				if (linked)
				{
					// Pictures copied with them are attached too.
					IsaClient.ChatImages.ChatAttachments.TryPaste();
					return true;
				}
			}
			if (Clipboard.ContainsText())
			{
				StyleLook style = StyleLook.FromTemplate(Clipboard.GetText());
				if (style != null)
				{
					LinkStyle(style, "Style");
					return true;
				}
			}
			if (CopyIsFresh)
			{
				_copySequence = -1;
				LinkCopied();
				return true;
			}
		}
		catch (Exception ex)
		{
			// The clipboard can be busy (another program holds it).
			ClientLog.LogException(ex);
		}
		return false;
	}

	#endregion

	#region Cards

	/// <summary>The card for a tag option ("k...", "y...", "w..."), or null when it isn't one of ours.</summary>
	public static TextSnippet Snippet(string option)
	{
		if (string.IsNullOrEmpty(option) || option.Length < 9 || Main.dedServ)
		{
			return null;
		}
		try
		{
			string body = option.Substring(1).ToLowerInvariant();
			switch (option[0])
			{
			case 'k':
			{
				if (body.Length < 8 + StyleLook.Size * 2 || !IsHex(body))
				{
					return null;
				}
				string code = body.Substring(0, 8);
				StyleLook look = StyleLook.FromHex(body.Substring(8, StyleLook.Size * 2));
				string name = ShareKinds.NameFromHex(body.Substring(8 + StyleLook.Size * 2));
				if (look == null)
				{
					return null;
				}
				look.Name = name;
				ShareInbox.Item item = ShareInbox.Remember(code, ShareKind.Character, name, mine: false, look: look);
				return new ShareCard(ShareKind.Character, item);
			}
			case 'y':
			{
				if (body.Length < StyleLook.Size * 2 || !IsHex(body))
				{
					return null;
				}
				StyleLook look = StyleLook.FromHex(body.Substring(0, StyleLook.Size * 2));
				if (look == null)
				{
					return null;
				}
				look.Name = ShareKinds.NameFromHex(body.Substring(StyleLook.Size * 2));
				ShareInbox.Item item = ShareInbox.Remember(look.Hex(), ShareKind.Style, look.Name, mine: false, look: look);
				return new ShareCard(ShareKind.Style, item);
			}
			case FileShare.Letter:
				return IsHex(body) ? FileShare.Snippet(body) : null;
			case 'w':
			{
				if (body.Length < 24 || !IsHex(body))
				{
					return null;
				}
				string id = body.Substring(0, 16);
				int width = Convert.ToInt32(body.Substring(16, 4), 16);
				int height = Convert.ToInt32(body.Substring(20, 4), 16);
				string name = ShareKinds.NameFromHex(body.Substring(24));
				SchematicShare.Entry entry = SchematicShare.Seen(id, width, height, name);
				ShareInbox.Item item = ShareInbox.Remember(entry.Code, ShareKind.Schematic, entry.Name, mine: false, schematic: entry);
				return new ShareCard(ShareKind.Schematic, item);
			}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		return null;
	}

	private static bool IsHex(string s)
	{
		foreach (char c in s)
		{
			if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
			{
				return false;
			}
		}
		return true;
	}

	/// <summary>The schematic card under the mouse this frame: its map is drawn bigger over the chat.</summary>
	internal static SchematicShare.Entry HoverPreview;

	/// <summary>After the chat's lines are drawn (so nothing covers it).</summary>
	public static void EndFrame(SpriteBatch sb)
	{
		SchematicShare.Entry e = HoverPreview;
		HoverPreview = null;
		Texture2D preview = SchematicShare.PreviewOf(e);
		if (preview == null)
		{
			return;
		}
		float fit = Math.Min(260f / preview.Width, 170f / preview.Height);
		int w = (int)(preview.Width * fit);
		int h = (int)(preview.Height * fit);
		Rectangle box = new Rectangle(Main.mouseX + 18, Main.mouseY - h - 44, w + 16, h + 16);
		if (box.Y < 4)
		{
			box.Y = Main.mouseY + 24;
		}
		float ui = (Main.UIScaleMatrix.M11 > 0.01f) ? Main.UIScaleMatrix.M11 : 1f;
		int right = (int)(Main.screenWidth / ui) - 4;
		if (box.Right > right)
		{
			box.X = right - box.Width;
		}
		Utils.DrawInvBG(sb, box, new Color(25, 35, 70) * 0.95f);
		sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(box.X + 8, box.Y + 8, w, h), new Rectangle(0, 0, 1, 1), new Color(10, 14, 28));
		sb.Draw(preview, new Rectangle(box.X + 8, box.Y + 8, w, h), Color.White);
	}

	#endregion
}
