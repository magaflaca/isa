using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using IsaClient.Core;
using IsaClient.Gba;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IsaClient.Psp;

/// <summary>
/// Finds free PSP games made by the community and gets them:
/// <list type="bullet">
/// <item>archive.org: the games of its PSP homebrew library (one search gives the whole list); each game's files are only
/// looked at when it's got.</item>
/// <item>itch.io: the free games with its PSP tags (the browse pages as JSON). Files come the way the site gives them
/// (see <see cref="GbaFinder.FromItch"/>); the upload that looks like the PSP one is tried first.</item>
/// </list>
/// What comes is checked to really have a PSP game, then added like any other (<see cref="PspLibrary"/>). Commercial
/// games aren't in these lists: the library is homebrew only.
/// </summary>
public static class PspFinder
{
	private const string ArchiveQuery = "collection:psp-homebrew-library AND subject:game";

	private static readonly string[] ItchTags = { "tag-psp", "tag-playstation-portable" };

	private static readonly TimeSpan CacheAge = TimeSpan.FromDays(3);

	private static readonly Regex OtherSystems = new Regex("vita|windows|win32|win64|linux|mac|osx|android|apk|3ds|switch|source|src|ost|soundtrack|music|manual|pdf|wallpaper|html|web|\\.exe", RegexOptions.Compiled);

	private static List<FoundGame> _games = new List<FoundGame>();

	public static List<FoundGame> Games => _games;

	public static volatile bool Loading;

	public static volatile string LoadProblem;

	public static DateTime LoadedAt;

	/// <summary>Games downloaded in the background, for the main thread to add to the library.</summary>
	public static readonly ConcurrentQueue<FoundGame> Finished = new ConcurrentQueue<FoundGame>();

	private static string Folder => Path.Combine(PspGame.Folder, "Finder");

	private static string CacheFile => Path.Combine(Folder, "games2.json");

	/// <summary>Cards waiting for their weight: the latest shown first, they're the ones on screen.</summary>
	private static readonly ConcurrentStack<FoundGame> Weighing = new ConcurrentStack<FoundGame>();

	private static int _weigher;

	/// <summary>Loads the list (from the cache when it's fresh, unless <paramref name="refresh"/>), in the background.</summary>
	public static void Load(bool refresh)
	{
		if (Loading)
		{
			return;
		}
		Loading = true;
		LoadProblem = null;
		GbaFinder.Low(() =>
		{
			try
			{
				List<FoundGame> cached = ReadCache(out DateTime stamp);
				if (cached != null)
				{
					// shown right away, even if it's old: the new list replaces it when it comes
					Publish(cached, stamp);
					if (!refresh && DateTime.UtcNow - stamp < CacheAge)
					{
						return;
					}
				}
				List<FoundGame> games = Fetch();
				if (games.Count > 0)
				{
					Publish(games, DateTime.UtcNow);
					WriteCache(games);
				}
			}
			catch (Exception ex)
			{
				LoadProblem = ex.Message;
				ClientLog.LogException(ex);
			}
			finally
			{
				Loading = false;
			}
		});
	}

	private static void Publish(List<FoundGame> games, DateTime stamp)
	{
		foreach (FoundGame g in games)
		{
			g.SearchTitle = (g.Title ?? "").ToLowerInvariant();
			g.SearchText = ((g.Author ?? "") + " " + (g.Description ?? "")).ToLowerInvariant();
		}
		_games = games;
		LoadedAt = stamp;
	}

	private static List<FoundGame> ReadCache(out DateTime stamp)
	{
		stamp = default(DateTime);
		try
		{
			if (!File.Exists(CacheFile))
			{
				return null;
			}
			stamp = File.GetLastWriteTimeUtc(CacheFile);
			using StreamReader file = File.OpenText(CacheFile);
			using JsonTextReader json = new JsonTextReader(file);
			return new JsonSerializer().Deserialize<List<FoundGame>>(json);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			return null;
		}
	}

	private static void WriteCache(List<FoundGame> games)
	{
		try
		{
			Directory.CreateDirectory(Folder);
			string tmp = CacheFile + ".tmp";
			using (StreamWriter file = File.CreateText(tmp))
			{
				new JsonSerializer().Serialize(file, games);
			}
			if (File.Exists(CacheFile))
			{
				File.Delete(CacheFile);
			}
			File.Move(tmp, CacheFile);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	/// <summary>Both sites; one failing doesn't lose the other (itch.io's first: they're the newest).</summary>
	private static List<FoundGame> Fetch()
	{
		List<FoundGame> games = new List<FoundGame>();
		string problem = null;
		try
		{
			games.AddRange(FetchItch());
		}
		catch (Exception ex)
		{
			problem = ex.Message;
		}
		try
		{
			games.AddRange(FetchArchive());
		}
		catch (Exception ex)
		{
			problem = ex.Message;
		}
		if (games.Count == 0 && problem != null)
		{
			throw new InvalidOperationException(problem);
		}
		return games;
	}

	private static List<FoundGame> FetchItch()
	{
		List<FoundGame> games = new List<FoundGame>();
		HashSet<string> seen = new HashSet<string>();
		foreach (string tag in ItchTags)
		{
			for (int page = 1; page <= 30; page++)
			{
				JObject json = JObject.Parse(GbaFinder.GetText("https://itch.io/games/free/" + tag + "?format=json&page=" + page, null));
				int count = json.Value<int?>("num_items") ?? 0;
				foreach (FoundGame g in GbaFinder.ParseItchCells((string)json["content"] ?? ""))
				{
					// the tag is shared with the PS Vita: only what doesn't say it's just for that
					string title = (g.Title ?? "").ToLowerInvariant();
					bool vitaOnly = title.Contains("vita") && !title.Contains("psp");
					if (!vitaOnly && seen.Add(g.Key))
					{
						g.Key = "psp:" + g.Key;
						games.Add(g);
					}
				}
				if (count < 30)
				{
					break;
				}
			}
		}
		return games;
	}

	private static List<FoundGame> FetchArchive()
	{
		string url = "https://archive.org/advancedsearch.php?q=" + Uri.EscapeDataString(ArchiveQuery) + "&fl[]=identifier&fl[]=title&fl[]=creator&fl[]=description&fl[]=item_size&sort[]=downloads+desc&rows=4000&page=1&output=json";
		JObject json = JObject.Parse(GbaFinder.GetText(url, null));
		List<FoundGame> games = new List<FoundGame>();
		foreach (JObject d in (json["response"]?["docs"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
		{
			string id = GbaFinder.Str(d["identifier"]);
			if (string.IsNullOrEmpty(id))
			{
				continue;
			}
			string description = GbaFinder.Html(GbaFinder.Str(d["description"]) ?? "");
			games.Add(new FoundGame
			{
				Key = "psp:ia:" + id,
				Source = FinderSource.Archive,
				IaId = id,
				Title = GbaFinder.Html(GbaFinder.Str(d["title"]) ?? id),
				Author = GbaFinder.Str(d["creator"]) ?? "",
				Description = description,
				Blurb = GbaFinder.FirstSentence(description),
				Page = "https://archive.org/details/" + id,
				Cover = "https://archive.org/services/img/" + id,
				Tags = new string[0],
				Weight = WeightText(d.Value<long?>("item_size") ?? 0L)
			});
		}
		return games;
	}

	/// <summary>"850 KB", "6.5 MB", "1.2 GB" (null for nothing).</summary>
	private static string WeightText(long bytes)
	{
		System.Globalization.CultureInfo text = System.Globalization.CultureInfo.InvariantCulture;
		if (bytes <= 0)
		{
			return null;
		}
		if (bytes < 1000L * 1024)
		{
			return Math.Max(1L, bytes / 1024).ToString(text) + " KB";
		}
		double mb = bytes / 1048576.0;
		if (mb >= 1000.0)
		{
			return (mb / 1024.0).ToString("0.0", text) + " GB";
		}
		return mb.ToString((mb < 10.0) ? "0.0" : "0", text) + " MB";
	}

	/// <summary>
	/// A card is on screen: itch.io only says what a game weighs on its page, so it's looked at, one at a time, only for
	/// the cards someone sees. Each one is asked once, and the list keeps it.
	/// </summary>
	public static void AskWeight(FoundGame g)
	{
		if (g.Weight != null || g.WeightAsked || g.Source != FinderSource.Itch)
		{
			return;
		}
		g.WeightAsked = true;
		Weighing.Push(g);
		if (Interlocked.CompareExchange(ref _weigher, 1, 0) == 0)
		{
			GbaFinder.Low(Weigh);
		}
	}

	private static void Weigh()
	{
		bool found = false;
		while (Weighing.TryPop(out FoundGame g))
		{
			try
			{
				string html = GbaFinder.GetText(g.Page.Split('?')[0].TrimEnd('/'), null);
				GbaFinder.ItchUpload best = GbaFinder.ScoredUploads(html, Score).Where(u => u.Score > -8 && !string.IsNullOrEmpty(u.SizeText)).OrderByDescending(u => u.Score).FirstOrDefault();
				if (best != null)
				{
					g.Weight = best.SizeText;
					found = true;
				}
			}
			catch
			{
				// no weight on the card, that's all
			}
			// the site is someone else's: no hurry
			Thread.Sleep(200);
		}
		if (found)
		{
			WriteCache(_games);
		}
		Volatile.Write(ref _weigher, 0);
		if (!Weighing.IsEmpty && Interlocked.CompareExchange(ref _weigher, 1, 0) == 0)
		{
			GbaFinder.Low(Weigh);
		}
	}

	/// <summary>The list for the words and the source: title matches first.</summary>
	public static List<FoundGame> Search(string query, FinderSource? source)
	{
		IEnumerable<FoundGame> list = _games;
		if (source.HasValue)
		{
			list = list.Where(g => g.Source == source.Value);
		}
		string words = (query ?? "").Trim().ToLowerInvariant();
		if (words.Length == 0)
		{
			return list.ToList();
		}
		List<FoundGame> hits = list.Where(g => g.SearchTitle.Contains(words) || g.SearchText.Contains(words)).ToList();
		return hits.OrderBy(g => g.SearchTitle.Contains(words) ? 0 : 1).ToList();
	}

	/// <summary>Downloads a game in the background; it goes to <see cref="Finished"/> for the library.</summary>
	public static void Get(FoundGame g)
	{
		if (g.Status == FinderStatus.Working)
		{
			return;
		}
		g.Status = FinderStatus.Working;
		g.Message = "Looking for the game's file...";
		g.Got = 0;
		g.Size = 0;
		GbaFinder.Low(() =>
		{
			try
			{
				List<string> files = (g.Source == FinderSource.Archive) ? FromArchive(g) : FromItch(g);
				g.Roms = files;
				g.Message = "Adding it...";
				Finished.Enqueue(g);
			}
			catch (Exception ex)
			{
				g.Message = (ex is GbaFinder.FinderException) ? ex.Message : ("Couldn't get it: " + ex.Message);
				g.Status = FinderStatus.Failed;
			}
		});
	}

	private static List<string> FromArchive(FoundGame g)
	{
		JObject meta = JObject.Parse(GbaFinder.GetText("https://archive.org/metadata/" + Uri.EscapeDataString(g.IaId) + "/files", null));
		List<string> names = ((meta["result"] ?? meta["files"]) as JArray)?.OfType<JObject>().Select(f => GbaFinder.Str(f["name"]) ?? "").ToList() ?? new List<string>();
		// a game by itself first, then archives; the top ones before "Old Versions" and the like
		List<string> files = names.Where(n => (PspRom.IsGameFile(n) || PspRom.IsArchive(n)) && !n.EndsWith("_jp2.zip", StringComparison.OrdinalIgnoreCase))
			.OrderBy(n => PspRom.IsGameFile(n) ? 0 : 1)
			.ThenBy(n => n.Count(c => c == '/'))
			.Take(3)
			.ToList();
		if (files.Count == 0)
		{
			throw new GbaFinder.FinderException("It has no PSP game to download on archive.org.");
		}
		string why = null;
		foreach (string name in files)
		{
			string url = "https://archive.org/download/" + Uri.EscapeDataString(g.IaId) + "/" + string.Join("/", name.Split('/').Select(Uri.EscapeDataString));
			List<string> got = TryDownload(g, url, name, ref why);
			if (got != null)
			{
				return got;
			}
		}
		throw new GbaFinder.FinderException(why ?? "It has no PSP game to download on archive.org.");
	}

	private static List<string> FromItch(FoundGame g)
	{
		return GbaFinder.FromItch(g, html => GbaFinder.ScoredUploads(html, Score), (string url, ref string why) => TryDownload(g, url, null, ref why), "PSP game");
	}

	/// <summary>How much an upload's name looks like the PSP game.</summary>
	private static int Score(string name, bool forComputers)
	{
		int score = 0;
		if (Regex.IsMatch(name, "psp|eboot|\\.pbp|\\.iso|\\.cso"))
		{
			score += 6;
		}
		if (name.Contains(".zip") || name.Contains(".7z"))
		{
			score += 1;
		}
		if (forComputers)
		{
			score -= 10;
		}
		if (OtherSystems.IsMatch(name))
		{
			score -= 8;
		}
		// what the author works on, and the versions kept for the record
		if (Regex.IsMatch(name, "\\bproject\\b"))
		{
			score -= 8;
		}
		if (Regex.IsMatch(name, "\\bold\\b"))
		{
			score -= 3;
		}
		return score;
	}

	/// <summary>Downloads one file; null (with why) when it has no PSP game.</summary>
	private static List<string> TryDownload(FoundGame g, string url, string name, ref string why)
	{
		string dir = Path.Combine(Folder, "Downloading");
		Directory.CreateDirectory(dir);
		string tmp = Path.Combine(dir, Guid.NewGuid().ToString("N").Substring(0, 12));
		string file = null;
		try
		{
			g.Message = "Downloading...";
			string got = GbaFinder.Download(url, tmp, null, g);
			name = Path.GetFileName(name ?? got ?? new Uri(url).AbsolutePath);
			// the folder keeps it apart from other games with the same file name
			string own = Path.Combine(dir, Regex.Replace(g.Key, "[^A-Za-z0-9_.-]", "_"));
			Directory.CreateDirectory(own);
			file = Path.Combine(own, string.Join("_", name.Split(Path.GetInvalidFileNameChars())));
			if (File.Exists(file))
			{
				File.Delete(file);
			}
			File.Move(tmp, file);
			if (PspLibrary.HasGame(file))
			{
				return new List<string> { PspRom.IsArchive(file) ? file : Keep(file, g.Title) };
			}
			why = name + " has no PSP game.";
			File.Delete(file);
			return null;
		}
		catch (WebException ex)
		{
			why = "The download failed: " + ex.Message;
			return null;
		}
		finally
		{
			try
			{
				if (File.Exists(tmp))
				{
					File.Delete(tmp);
				}
			}
			catch
			{
			}
		}
	}

	/// <summary>A game that came as it is goes with the others; programs in a folder of their own (they're all EBOOT.PBP).</summary>
	private static string Keep(string file, string title)
	{
		string roms = Path.Combine(PspGame.Folder, "Roms");
		string ext = Path.GetExtension(file).ToLowerInvariant();
		if (ext == ".pbp" || ext == ".elf" || ext == ".prx")
		{
			roms = Path.Combine(roms, string.Join("_", (title ?? "Game").Split(Path.GetInvalidFileNameChars())).Trim().TrimEnd('.'));
		}
		Directory.CreateDirectory(roms);
		string target = Path.Combine(roms, Path.GetFileName(file));
		if (File.Exists(target))
		{
			File.Delete(target);
		}
		File.Move(file, target);
		Directory.Delete(Path.GetDirectoryName(file));
		return target;
	}

	/// <summary>The download is in the library: what was copied out of it stays, the archive itself goes.</summary>
	internal static void Forget(IEnumerable<string> downloads)
	{
		foreach (string file in downloads)
		{
			try
			{
				if (PspRom.IsArchive(file) && File.Exists(file))
				{
					File.Delete(file);
					Directory.Delete(Path.GetDirectoryName(file));
				}
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		}
	}
}
