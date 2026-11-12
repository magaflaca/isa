using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using IsaClient.Building;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Map;

namespace IsaClient.Sharing;

/// <summary>
/// TEdit schematics linked in chat. The file goes to archive.org (<see cref="ArchiveStore"/>); small ones also go
/// through the pipe so IsaClient players here get them right away. The chat tag holds its id, size and name; every IsaClient that sees
/// it fetches the file and draws a small map of it on the card.
/// </summary>
public static class SchematicShare
{
	public sealed class Entry
	{
		/// <summary>16 hex: the first 8 are also its pipe code.</summary>
		public string Id;

		public string Name = "";

		public int Width;

		public int Height;

		public string Path;

		public bool Mine;

		public string From = "";

		public DateTime When = DateTime.Now;

		/// <summary>0..1 while going up to archive.org (mine) or coming down (theirs).</summary>
		public float Progress;

		public bool Uploading;

		public bool Uploaded;

		public bool Downloading;

		public string Status = "";

		public bool Failed;

		public Color[] Pixels;

		public int PreviewWidth;

		public int PreviewHeight;

		public Texture2D Preview;

		public bool PreviewStarted;

		/// <summary>With the pipe off: its Imgur card going up (yours) or coming down.</summary>
		public ArchiveStore.Job Image;

		public void SetFrom(string name)
		{
			if (!Mine && !string.IsNullOrEmpty(name))
			{
				From = name;
			}
		}

		public string Code => Id.Substring(0, 8);

		public bool Ready => Path != null && File.Exists(Path);

		public string Item => "isaclient-sch-" + Id;

		public string FileName => Id + ".TEditSch";

		public string Url => ArchiveStore.Url(Item, FileName);
	}

	private static readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

	public static int Version;

	public static string Folder => System.IO.Path.Combine(ClientSettings.IsaClientFolder, "Received", "Schematics");

	public static string MineFolder => System.IO.Path.Combine(ClientSettings.IsaClientFolder, "Shared", "Schematics");

	public static List<Entry> All()
	{
		lock (_entries)
		{
			return new List<Entry>(_entries.Values);
		}
	}

	public static Entry Get(string id)
	{
		lock (_entries)
		{
			_entries.TryGetValue(id ?? "", out Entry e);
			return e;
		}
	}

	private static string IdOf(byte[] bytes)
	{
		ulong h = 14695981039346656037ul;
		foreach (byte b in bytes)
		{
			h = (h ^ b) * 1099511628211ul;
		}
		return ShareTransfer.CodeOf(bytes) + ((uint)(h >> 32) ^ (uint)h).ToString("x8");
	}

	/// <summary>The chat tag: [i/w&lt;id&gt;&lt;width&gt;&lt;height&gt;&lt;name&gt;:3061].</summary>
	public static string Tag(Entry e)
	{
		return "[i/" + ShareKinds.Letter(ShareKind.Schematic) + e.Id + ((ushort)Math.Min(65535, e.Width)).ToString("x4") + ((ushort)Math.Min(65535, e.Height)).ToString("x4") + ShareKinds.NameHex(e.Name) + ":" + ShareKinds.Item(ShareKind.Schematic) + "]";
	}

	/// <summary>A .TEditSch file to share: starts going up right away and returns its chat tag (null if it can't).</summary>
	public static string Link(string path)
	{
		try
		{
			byte[] bytes = File.ReadAllBytes(path);
			List<TileCopy> tiles = SchematicFiles.ReadTiles(path, out string name, out int width, out int height);
			if (tiles == null)
			{
				ClientChat.Error(System.IO.Path.GetFileName(path) + " isn't a TEdit schematic this client can read.");
				return null;
			}
			if (string.IsNullOrWhiteSpace(name))
			{
				name = System.IO.Path.GetFileNameWithoutExtension(path);
			}
			string id = IdOf(bytes);
			Entry e = Get(id);
			if (e == null)
			{
				e = new Entry { Id = id, Name = name, Width = width, Height = height, Path = path, Mine = true, From = Main.LocalPlayer?.name ?? "" };
				lock (_entries)
				{
					_entries[id] = e;
				}
				MakePreview(e, tiles);
			}
			if (bytes.Length <= ShareTransfer.MaxBytes)
			{
				ShareTransfer.Register(ShareKind.Schematic, bytes);
			}
			if (!e.Uploaded && !e.Uploading)
			{
				Upload(e, bytes);
			}
			ShareInbox.Remember(e.Code, ShareKind.Schematic, e.Name, mine: true, schematic: e);
			Version++;
			return Tag(e);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			ClientChat.Error("Could not read the schematic: " + ex.Message);
			return null;
		}
	}

	/// <summary>What is copied now (.copy, a loaded schematic, pixel art) as a schematic to share.</summary>
	public static string LinkCopied()
	{
		try
		{
			Directory.CreateDirectory(MineFolder);
			int w = Selection.SchematicSizeX;
			int h = Selection.SchematicSizeY;
			string name = "Copy " + w + "x" + h;
			string path = System.IO.Path.Combine(MineFolder, "copy-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".TEditSch");
			if (!SchematicFiles.WriteCopied(path, name))
			{
				ClientChat.Error("There's nothing copied (.copy) to send.");
				return null;
			}
			return Link(path);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			ClientChat.Error("Could not save what you copied: " + ex.Message);
			return null;
		}
	}

	/// <summary>A card in chat for a schematic: known, or fetched now (from the pipe or archive.org).</summary>
	public static Entry Seen(string id, int width, int height, string name)
	{
		Entry e = Get(id);
		if (e != null)
		{
			return e;
		}
		e = new Entry { Id = id, Name = name, Width = width, Height = height };
		lock (_entries)
		{
			_entries[id] = e;
		}
		string local = System.IO.Path.Combine(Folder, e.FileName);
		if (File.Exists(local))
		{
			Arrived(e, local);
			return e;
		}
		byte[] early = ShareTransfer.Get(e.Code)?.Bytes;
		if (early != null && IdOf(early) == id)
		{
			FromPipe(e.Code, early);
			return e;
		}
		ShareTransfer.Want(e.Code, ShareKind.Schematic);
		Download(e);
		return e;
	}

	/// <summary>One of yours going up to Imgur inside its card, for when the pipe is off (started once).</summary>
	public static ArchiveStore.Job UploadImage(Entry e)
	{
		if (e == null || !e.Mine || e.Path == null)
		{
			return null;
		}
		lock (e)
		{
			string path = e.Path;
			return e.Image = ShareImage.Upload(ShareKind.Schematic, e.Id, () => File.ReadAllBytes(path), () => ShareImage.SchematicCover(e));
		}
	}

	/// <summary>A schematic linked with the pipe off, with its Imgur card: fetched from it (archive.org keeps trying alongside).</summary>
	public static void FetchImage(string id, string link)
	{
		Entry e = Get(id);
		if (e == null || e.Ready || e.Mine || e.Image != null)
		{
			return;
		}
		e.Image = ShareImage.Fetch(ShareKind.Schematic, id, link, delegate(byte[] bytes)
		{
			if (IdOf(bytes) != id)
			{
				return false;
			}
			Main.QueueMainThreadAction(() => FromPipe(e.Code, bytes));
			return true;
		}, null);
	}

	/// <summary>Its bytes came through the pipe (faster than archive.org when the sender is here).</summary>
	public static void FromPipe(string code, byte[] bytes)
	{
		string id = IdOf(bytes);
		Entry e = Get(id);
		if (e == null || e.Ready)
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(Folder);
			string local = System.IO.Path.Combine(Folder, e.FileName);
			File.WriteAllBytes(local, bytes);
			Arrived(e, local);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static void Arrived(Entry e, string path)
	{
		e.Path = path;
		e.Downloading = false;
		e.Progress = 1f;
		e.Status = "";
		e.Failed = false;
		Version++;
		Task.Run(delegate
		{
			try
			{
				List<TileCopy> tiles = SchematicFiles.ReadTiles(path, out string name, out int width, out int height);
				if (tiles != null)
				{
					if (!string.IsNullOrWhiteSpace(name))
					{
						e.Name = name;
					}
					e.Width = width;
					e.Height = height;
					MakePreview(e, tiles);
				}
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		});
	}

	#region archive.org

	private static void Upload(Entry e, byte[] bytes)
	{
		e.Uploading = true;
		e.Progress = 0f;
		e.Status = "Uploading";
		Task.Run(delegate
		{
			try
			{
				if (ArchiveStore.Exists(e.Url))
				{
					e.Uploaded = true;
					return;
				}
				ArchiveStore.Put(e.Item, e.FileName, bytes, "IsaClient schematic: " + e.Name, "terraria;tedit;schematic;isaclient", delegate(float p)
				{
					e.Progress = p * 0.95f;
					Version++;
				});
				e.Uploaded = true;
			}
			catch (Exception ex)
			{
				e.Failed = true;
				e.Status = "Upload failed";
				ClientLog.LogOnce(ex);
				string reason = ex.Message;
				Main.QueueMainThreadAction(() => ClientChat.Error("Couldn't upload " + e.Name + " to archive.org: " + reason));
			}
			finally
			{
				e.Uploading = false;
				e.Progress = 1f;
				Version++;
			}
		});
	}

	/// <summary>
	/// Fetches it from archive.org. A just-uploaded file can take a while to show up there, so it keeps trying for a
	/// few minutes (the pipe usually brings it first anyway).
	/// </summary>
	private static void Download(Entry e)
	{
		e.Downloading = true;
		e.Status = "Receiving";
		Task.Run(async delegate
		{
			for (int attempt = 0; attempt < 40 && !e.Ready; attempt++)
			{
				if (attempt > 0)
				{
					e.Status = "Waiting for archive.org";
					Version++;
					await Task.Delay(Math.Min(15000, 4000 + attempt * 1000));
					if (e.Ready)
					{
						return;
					}
				}
				try
				{
					byte[] bytes = ArchiveStore.Get(e.Url, delegate(float p)
					{
						e.Status = "Receiving";
						e.Progress = p;
						Version++;
					});
					if (e.Ready)
					{
						return;
					}
					if (IdOf(bytes) != e.Id)
					{
						continue;
					}
					Directory.CreateDirectory(Folder);
					string local = System.IO.Path.Combine(Folder, e.FileName);
					File.WriteAllBytes(local, bytes);
					Arrived(e, local);
					return;
				}
				catch (WebException)
				{
				}
				catch (Exception ex)
				{
					ClientLog.LogException(ex);
				}
			}
			if (!e.Ready)
			{
				e.Downloading = false;
				e.Failed = true;
				e.Status = "Couldn't get it";
				Version++;
			}
		});
	}

	#endregion

	#region Preview

	/// <summary>A small map of the schematic (map colors, one pixel per few tiles), drawn off the main thread.</summary>
	private static void MakePreview(Entry e, List<TileCopy> tiles)
	{
		int width = e.Width;
		int height = e.Height;
		if (tiles == null || width <= 0 || height <= 0 || tiles.Count < width * height)
		{
			return;
		}
		float step = Math.Max(1f, Math.Max(width / 240f, height / 160f));
		int pw = Math.Max(1, (int)(width / step));
		int ph = Math.Max(1, (int)(height / step));
		Color[] pixels = new Color[pw * ph];
		for (int py = 0; py < ph; py++)
		{
			for (int px = 0; px < pw; px++)
			{
				int x = Math.Min(width - 1, (int)((px + 0.5f) * step));
				int y = Math.Min(height - 1, (int)((py + 0.5f) * step));
				pixels[py * pw + px] = ColorOf(tiles[x * height + y]);
			}
		}
		e.PreviewWidth = pw;
		e.PreviewHeight = ph;
		e.Pixels = pixels;
		e.Preview = null;
		Version++;
	}

	private static Color ColorOf(TileCopy tile)
	{
		try
		{
			if (tile.active() && tile.type < MapHelper.tileLookup.Length)
			{
				return MapHelper.GetMapTileXnaColor(MapTile.Create(MapHelper.tileLookup[tile.type], byte.MaxValue, tile.color()), 0, 0);
			}
			if (tile.liquid > 0)
			{
				return tile.liquidType() switch
				{
					1 => new Color(253, 90, 3),
					2 => new Color(254, 194, 20),
					3 => new Color(220, 180, 255),
					_ => new Color(30, 90, 210)
				};
			}
			if (tile.wall > 0 && tile.wall < MapHelper.wallLookup.Length)
			{
				Color wall = MapHelper.GetMapTileXnaColor(MapTile.Create(MapHelper.wallLookup[tile.wall], byte.MaxValue, tile.wallColor()), 0, 0);
				return new Color(wall.R * 3 / 4, wall.G * 3 / 4, wall.B * 3 / 4);
			}
		}
		catch
		{
		}
		return Color.Transparent;
	}

	/// <summary>Its preview as a texture (made on the main thread the first time it's drawn). Null until it's known.</summary>
	public static Texture2D PreviewOf(Entry e)
	{
		if (e == null || e.Pixels == null)
		{
			return null;
		}
		if (e.Preview == null || e.Preview.IsDisposed)
		{
			try
			{
				Texture2D texture = new Texture2D(Main.instance.GraphicsDevice, e.PreviewWidth, e.PreviewHeight);
				texture.SetData(e.Pixels);
				e.Preview = texture;
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
				e.Pixels = null;
				return null;
			}
		}
		return e.Preview;
	}

	#endregion

	/// <summary>Pastes it (as dropping the file on the game does).</summary>
	public static void Paste(Entry e)
	{
		if (e == null || !e.Ready)
		{
			return;
		}
		SchematicFiles.ForcedDialogPath = e.Path;
		SchematicFiles.Load();
	}

	/// <summary>A copy in the game's Schematics folder (never over another file).</summary>
	public static void SaveCopy(Entry e)
	{
		if (e == null || !e.Ready)
		{
			return;
		}
		try
		{
			string folder = System.IO.Path.Combine(Main.SavePath, "Schematics");
			Directory.CreateDirectory(folder);
			string baseName = e.Name;
			foreach (char c in System.IO.Path.GetInvalidFileNameChars())
			{
				baseName = baseName.Replace(c, '_');
			}
			if (string.IsNullOrWhiteSpace(baseName))
			{
				baseName = "Schematic";
			}
			string target = System.IO.Path.Combine(folder, baseName + ".TEditSch");
			for (int i = 2; File.Exists(target); i++)
			{
				target = System.IO.Path.Combine(folder, baseName + " " + i + ".TEditSch");
			}
			File.Copy(e.Path, target);
			ClientChat.Info("Saved " + System.IO.Path.GetFileName(target) + " in your Schematics folder.");
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			ClientChat.Error("Could not save the schematic: " + ex.Message);
		}
	}
}
