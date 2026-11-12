using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Psp;

/// <summary>A game of the library as its card shows it.</summary>
public sealed class PspEntry
{
	public string Path = "";

	public string Title = "";

	public string DiscId = "";

	/// <summary>What its saves and pictures go under.</summary>
	public string Key = "";

	public PspFormat Format;

	/// <summary>Still being read (the title is the file's until then).</summary>
	public bool Reading = true;

	public bool Missing;

	/// <summary>Its pictures on disk (null when it has none).</summary>
	public string Icon;

	public string Background;
}

/// <summary>
/// The games: what's written on each one (read on a background thread, its pictures kept in IsaClient/PSP/Covers) and
/// adding them. Files stay where they are; what's inside a .zip or .7z is copied out to IsaClient/PSP/Roms first, which
/// takes a while for a whole UMD and happens in the background with its progress.
/// </summary>
public static class PspLibrary
{
	private static readonly ConcurrentDictionary<string, PspEntry> Entries = new ConcurrentDictionary<string, PspEntry>(StringComparer.OrdinalIgnoreCase);

	private static string Covers => System.IO.Path.Combine(PspGame.Folder, "Covers");

	private static string Roms => System.IO.Path.Combine(PspGame.Folder, "Roms");

	/// <summary>A game's card (it fills in by itself a moment later the first time).</summary>
	public static PspEntry Entry(string path)
	{
		if (Entries.TryGetValue(path, out PspEntry e))
		{
			return e;
		}
		e = new PspEntry { Path = path, Title = PspRom.FileTitle(path), Key = "PSP-" + path.GetHashCode().ToString("x8") };
		if (!Entries.TryAdd(path, e))
		{
			return Entries[path];
		}
		PspEntry entry = e;
		Thread reader = new Thread(() => ReadEntry(entry)) { IsBackground = true, Name = "IsaClient PSP library", Priority = ThreadPriority.BelowNormal };
		reader.Start();
		return e;
	}

	/// <summary>Reads a game now (for what can't wait, like starting it).</summary>
	public static PspEntry EntryNow(string path)
	{
		PspEntry e = Entry(path);
		for (int i = 0; i < 300 && e.Reading; i++)
		{
			Thread.Sleep(10);
		}
		return e;
	}

	public static void Forget(string path) => Entries.TryRemove(path, out _);

	private static void ReadEntry(PspEntry e)
	{
		try
		{
			if (!File.Exists(e.Path))
			{
				e.Missing = true;
				return;
			}
			PspInfo info = PspRom.Read(e.Path);
			e.Format = info.Format;
			e.Missing = info.Format == PspFormat.None;
			e.Title = info.Title;
			e.DiscId = info.DiscId;
			e.Key = PspRom.Key(e.Path, info);
			e.Icon = Cover(e.Key + ".icon.png", info.Icon);
			e.Background = Cover(e.Key + ".back.png", info.Background);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		finally
		{
			e.Reading = false;
		}
	}

	/// <summary>A game's picture on disk, on black (the PSP's menu has them over its background and the textures here are opaque).</summary>
	private static string Cover(string name, byte[] png)
	{
		string path = System.IO.Path.Combine(Covers, name);
		if (File.Exists(path))
		{
			return path;
		}
		if (png == null)
		{
			return null;
		}
		try
		{
			Directory.CreateDirectory(Covers);
			using MemoryStream source = new MemoryStream(png);
			using Image image = Image.FromStream(source);
			using Bitmap flat = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
			using (Graphics g = Graphics.FromImage(flat))
			{
				g.Clear(System.Drawing.Color.Black);
				g.DrawImage(image, 0, 0, image.Width, image.Height);
			}
			flat.Save(path + ".tmp", ImageFormat.Png);
			File.Move(path + ".tmp", path);
			return path;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			return null;
		}
	}

	#region Adding games

	/// <summary>An archive is being copied out: its name and 0..1.</summary>
	public static string Importing { get; private set; }

	public static float ImportProgress { get; private set; }

	private static volatile bool _importBusy;

	public static bool ImportBusy => _importBusy;

	/// <summary>A file that could be a PSP game by its name, or an archive that may have one.</summary>
	public static bool Accepts(string path) => PspRom.IsGameFile(path) || path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);

	/// <summary>Whether a .zip has a PSP game inside (GBA and DS games come in .zip too).</summary>
	public static bool ZipHasGame(string path)
	{
		try
		{
			using Ionic.Zip.ZipFile zip = Ionic.Zip.ZipFile.Read(path);
			return zip.Any(e => !e.IsDirectory && IsGameEntry(e.FileName));
		}
		catch
		{
			return false;
		}
	}

	private static bool IsGameEntry(string name)
	{
		string file = System.IO.Path.GetFileName(name.Replace('\\', '/'));
		string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
		return ext == ".iso" || ext == ".cso" || ext == ".chd" || ext == ".pbp";
	}

	/// <summary>
	/// Adds games in the background: <paramref name="done"/> gets the first one (null if there was none) on the main
	/// thread. Nothing else is added meanwhile.
	/// </summary>
	public static void Add(string[] files, Action<string> done)
	{
		if (_importBusy)
		{
			PspGame.Problem = "Wait until the game being added is ready.";
			done?.Invoke(null);
			return;
		}
		_importBusy = true;
		ImportProgress = 0f;
		PspGame.Problem = null;
		Thread worker = new Thread(() =>
		{
			List<string> added = new List<string>();
			string problem = null;
			foreach (string file in files)
			{
				try
				{
					added.AddRange(GamesIn(file, ref problem));
				}
				catch (Exception ex)
				{
					problem = System.IO.Path.GetFileName(file) + ": " + ex.Message;
					ClientLog.LogException(ex);
				}
			}
			Main.QueueMainThreadAction(() =>
			{
				_importBusy = false;
				Importing = null;
				foreach (string game in added)
				{
					if (!PspConfig.Games.Exists(g => string.Equals(g, game, StringComparison.OrdinalIgnoreCase)))
					{
						PspConfig.Games.Add(game);
					}
				}
				PspConfig.Save();
				if (problem != null)
				{
					PspGame.Problem = problem;
				}
				done?.Invoke(added.FirstOrDefault());
			});
		})
		{
			IsBackground = true,
			Name = "IsaClient PSP import",
			Priority = ThreadPriority.BelowNormal
		};
		worker.Start();
	}

	private static IEnumerable<string> GamesIn(string file, ref string problem)
	{
		string name = System.IO.Path.GetFileName(file);
		if (Directory.Exists(file))
		{
			// a homebrew's folder
			string eboot = System.IO.Path.Combine(file, "EBOOT.PBP");
			if (File.Exists(eboot))
			{
				return new[] { eboot };
			}
			problem = name + " has no EBOOT.PBP inside.";
			return new string[0];
		}
		if (file.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
		{
			problem = name + ": extract the .rar first, then add the game.";
			return new string[0];
		}
		if (PspRom.IsArchive(file))
		{
			Importing = name;
			List<string> games = file.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ? FromSevenZip(file) : FromZip(file);
			if (games.Count == 0)
			{
				problem = name + " has no PSP game inside.";
			}
			return games;
		}
		if (PspRom.Identify(file, out string why) != PspFormat.None)
		{
			return new[] { file };
		}
		problem = why ?? (name + ": that isn't a PSP game.");
		return new string[0];
	}

	/// <summary>
	/// Where each file of an archive goes. UMD images go to Roms by themselves; an EBOOT.PBP brings its whole folder (a
	/// homebrew's data is next to it). Null for the files that aren't needed.
	/// </summary>
	private static Dictionary<string, string> Plan(IEnumerable<string> names, string archive, List<string> games)
	{
		List<string> all = names.Select(n => n.Replace('\\', '/')).ToList();
		Dictionary<string, string> targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string root = System.IO.Path.GetFullPath(Roms) + System.IO.Path.DirectorySeparatorChar;
		void Put(string entry, string relative)
		{
			string target = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
			if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			{
				targets[entry] = target;
			}
		}
		foreach (string entry in all)
		{
			string file = entry.Substring(entry.LastIndexOf('/') + 1);
			string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
			if (ext == ".iso" || ext == ".cso" || ext == ".chd")
			{
				Put(entry, file);
				if (targets.TryGetValue(entry, out string target))
				{
					games.Add(target);
				}
			}
		}
		// homebrew: the EBOOT.PBP nearest the top of each folder is the game (others under it are other versions of it,
		// "%" folders are loaders for old firmwares); any .pbp if there's no EBOOT
		bool Named(string entry) => string.Equals(entry.Substring(entry.LastIndexOf('/') + 1), "EBOOT.PBP", StringComparison.OrdinalIgnoreCase);
		bool Loose(string entry) => entry.EndsWith(".pbp", StringComparison.OrdinalIgnoreCase);
		List<string> boots = all.Where(Named).ToList();
		if (boots.Count == 0)
		{
			boots = all.Where(Loose).Take(1).ToList();
		}
		List<string> folders = new List<string>();
		foreach (string entry in boots.OrderBy(e => e.Count(c => c == '/')))
		{
			int slash = entry.LastIndexOf('/');
			string folder = (slash >= 0) ? entry.Substring(0, slash + 1) : "";
			string gameName = (slash >= 0) ? System.IO.Path.GetFileName(folder.TrimEnd('/')) : System.IO.Path.GetFileNameWithoutExtension(archive);
			if (gameName.StartsWith("%") || folders.Any(f => folder.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}
			folders.Add(folder);
			foreach (string other in all)
			{
				if (other.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
				{
					Put(other, gameName + "/" + other.Substring(folder.Length));
				}
			}
			if (targets.TryGetValue(entry, out string target))
			{
				games.Add(target);
			}
		}
		return targets;
	}

	/// <summary>Whether a downloaded file is, or has, a PSP game.</summary>
	internal static bool HasGame(string file)
	{
		try
		{
			if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
			{
				return ZipHasGame(file);
			}
			if (file.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
			{
				using SevenZip archive = new SevenZip(file);
				return archive.Entries.Any(e => !e.IsDirectory && IsGameEntry(e.Name));
			}
			return PspRom.Identify(file, out _) != PspFormat.None;
		}
		catch
		{
			return false;
		}
	}

	private static List<string> FromSevenZip(string file)
	{
		List<string> games = new List<string>();
		using SevenZip archive = new SevenZip(file);
		Dictionary<string, string> targets = Plan(archive.Entries.Where(e => !e.IsDirectory).Select(e => e.Name), file, games);
		archive.Extract(e =>
		{
			if (!targets.TryGetValue(e.Name.Replace('\\', '/'), out string target))
			{
				return null;
			}
			// already out, from an earlier time
			return (File.Exists(target) && new FileInfo(target).Length == e.Size) ? null : target;
		}, p => ImportProgress = p);
		return games;
	}

	private static List<string> FromZip(string file)
	{
		List<string> games = new List<string>();
		using Ionic.Zip.ZipFile zip = Ionic.Zip.ZipFile.Read(file);
		List<Ionic.Zip.ZipEntry> entries = zip.Where(e => !e.IsDirectory).ToList();
		Dictionary<string, string> targets = Plan(entries.Select(e => e.FileName), file, games);
		long total = entries.Where(e => targets.ContainsKey(e.FileName.Replace('\\', '/'))).Sum(e => e.UncompressedSize);
		long done = 0;
		byte[] buffer = new byte[1 << 16];
		foreach (Ionic.Zip.ZipEntry entry in entries)
		{
			if (!targets.TryGetValue(entry.FileName.Replace('\\', '/'), out string target))
			{
				continue;
			}
			if (File.Exists(target) && new FileInfo(target).Length == entry.UncompressedSize)
			{
				done += entry.UncompressedSize;
				continue;
			}
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
			string tmp = target + ".tmp";
			using (FileStream o = File.Create(tmp))
			using (Stream s = entry.OpenReader())
			{
				int n;
				while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
				{
					o.Write(buffer, 0, n);
					done += n;
					ImportProgress = (total > 0) ? (float)done / total : 1f;
				}
			}
			if (File.Exists(target))
			{
				File.Delete(target);
			}
			File.Move(tmp, target);
		}
		return games;
	}

	#endregion
}
