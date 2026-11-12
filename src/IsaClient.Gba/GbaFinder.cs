using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IsaClient.Gba;

/// <summary>Where a found game comes from.</summary>
public enum FinderSource
{
	HomebrewHub,
	Itch,
	GameJolt,
	Archive,
	UniversalDb
}

/// <summary>Where getting a found game is.</summary>
public enum FinderStatus
{
	None,
	Working,
	Done,
	Failed
}

/// <summary>A game in the finder's catalog (what the sources say), plus how getting it goes (not saved).</summary>
public sealed class FoundGame
{
	public string Key;

	public FinderSource Source;

	public string Title;

	public string Author;

	/// <summary>One line about it (itch's short text, or the start of the description).</summary>
	public string Blurb;

	public string Description;

	/// <summary>Its page on the web.</summary>
	public string Page;

	/// <summary>Its picture (cover or first screenshot), or null.</summary>
	public string Cover;

	public string Genre;

	public string[] Tags;

	/// <summary>Homebrew Hub: where its files are, and the files.</summary>
	public string HhBase;

	public string HhSlug;

	public string[] HhFiles;

	/// <summary>A GitHub repository (its releases are looked at when nothing else has the ROM).</summary>
	public string Repo;

	/// <summary>Game Jolt: the game's id.</summary>
	public long GjId;

	/// <summary>archive.org: the item, and its ROM files (.gba first, then zips).</summary>
	public string IaId;

	public string[] IaFiles;

	/// <summary>For adults (shown only when the player asks for it).</summary>
	public bool Adult;

	/// <summary>A Nintendo DS game (else a Game Boy Advance one).</summary>
	public bool Ds;

	/// <summary>Universal-DB: its downloads (the .nds ones first).</summary>
	public string[] Urls;

	/// <summary>About how much it weighs, as a text ("14 MB"); null when it isn't known. Only the PSP's finder says.</summary>
	public string Weight;

	/// <summary>Its weight has been asked for (it may not have come).</summary>
	[JsonIgnore]
	public bool WeightAsked;

	/// <summary>Everything the search looks in, in lower case (made once).</summary>
	[JsonIgnore]
	public string SearchTitle;

	[JsonIgnore]
	public string SearchText;

	[JsonIgnore]
	public volatile FinderStatus Status;

	[JsonIgnore]
	public volatile string Message;

	[JsonIgnore]
	public long Got;

	[JsonIgnore]
	public long Size;

	/// <summary>The ROMs it brought (when done).</summary>
	[JsonIgnore]
	public List<string> Roms;

	[JsonIgnore]
	public bool Checked;

	/// <summary>A collection: how many of its ROMs are still to get (Get brings the next ones).</summary>
	[JsonIgnore]
	public int More;
}

/// <summary>
/// Finds free Game Boy Advance and Nintendo DS games made by the community and gets them (each game says which it is):
/// <list type="bullet">
/// <item>Universal-DB (db.universal-team.net): the DS homebrew games of its database (one JSON), files from their
/// releases.</item>
/// <item>Homebrew Hub (hh3.gbdev.io): its GBA database (the API's pages, all of them); the ROMs are on its own server,
/// and when an entry has none, its GitHub project's releases are looked at.</item>
/// <item>itch.io: the free games tagged Game Boy Advance (the browse pages as JSON). A game's page lists its uploads;
/// free and "name your own price" games are both handled the way the site does it (the download page, then each
/// upload's file link). The upload that looks like the GBA one is tried first; each download is checked, and the next
/// one is tried when it wasn't a ROM. Its adult games aren't in those pages: they come from its RSS feed.</item>
/// <item>Game Jolt: its search (the site's own API), only games with a GBA ROM among their files (each game's
/// packages are looked at); what's typed in the search is also looked up there. Files come from the site's download
/// link.</item>
/// <item>archive.org: items marked as GBA homebrew (its advanced search), each one's files looked at (its metadata):
/// only items with a .gba or a zip (a collection brings all its ROMs). Typed searches are looked up there too, with the
/// same rule.</item>
/// </list>
/// The DS games of itch.io, Game Jolt and archive.org come the same way (their DS tags, .nds files, DS homebrew items).
/// Whatever comes (a .gba, an .nds, a .zip, a zip in a zip) is opened and only real ROMs (by their header) are kept, in
/// IsaClient/GBA/Roms/Downloads. The catalog is cached for a day; pictures are cached on disk. All of it runs on
/// background threads below the game's priority, so Terraria stays smooth while the sites are read.
/// </summary>
public static class GbaFinder
{
	private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) IsaClient GBA";

	private const string HhApi = "https://hh3.gbdev.io/api/search?platform=GBA&page=";

	private const string HhStatic = "https://hh3.gbdev.io/static/";

	/// <summary>itch.io browse pages of free games with these tags.</summary>
	private static readonly string[] ItchTags = { "tag-gameboy-advance", "tag-game-boy-advance", "tag-gba-rom", "tag-gbajam" };

	private static readonly string[] ItchDsTags = { "tag-nds", "tag-ds", "tag-nds-rom", "tag-nds-homebrew", "tag-dsi" };

	private const string UniversalDbJson = "https://db.universal-team.net/data/full.json";

	private const long MaxDownload = 300L << 20;

	private static readonly TimeSpan CacheAge = TimeSpan.FromHours(24);

	/// <summary>IsaClient/GBA/Finder (catalog and pictures).</summary>
	public static string Folder;

	/// <summary>IsaClient/GBA/Roms/Downloads (one folder per game).</summary>
	public static string RomFolder;

	private static List<FoundGame> _games = new List<FoundGame>();

	public static List<FoundGame> Games => _games;

	public static volatile bool Loading;

	public static volatile string LoadProblem;

	public static int LoadDone;

	public static int LoadTotal;

	public static DateTime LoadedAt;

	/// <summary>Games got in the background, for the main thread to add to the library.</summary>
	public static readonly ConcurrentQueue<FoundGame> Finished = new ConcurrentQueue<FoundGame>();

	static GbaFinder()
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 12);
	}

	#region Catalog

	/// <summary>Loads the catalog (from the cache when it's fresh, unless <paramref name="refresh"/>), in the background.</summary>
	public static void Load(bool refresh)
	{
		if (Loading)
		{
			return;
		}
		Loading = true;
		LoadProblem = null;
		LoadDone = 0;
		LoadTotal = 0;
		Low(() =>
		{
			try
			{
				string cache = Path.Combine(Folder, "catalog.json");
				List<FoundGame> cached = ReadCache(cache, out DateTime stamp);
				// a list from before the DS games were looked for is old whatever its age
				bool complete = cached != null && cached.Any(g => g.Source == FinderSource.UniversalDb);
				if (!refresh && complete && DateTime.UtcNow - stamp < CacheAge)
				{
					Publish(cached, stamp);
					return;
				}
				if (cached != null && _games.Count == 0)
				{
					// the last list shows right away while the sites are read again
					Publish(cached, stamp);
				}
				List<FoundGame> fresh = null;
				try
				{
					fresh = Fetch(cached);
				}
				catch (Exception ex)
				{
					LoadProblem = "Couldn't reach the game sites: " + ex.Message;
				}
				if (fresh != null && fresh.Count > 0)
				{
					WriteCache(cache, fresh);
					Publish(fresh, DateTime.UtcNow);
				}
				else if (cached != null)
				{
					Publish(cached, stamp);
				}
			}
			catch (Exception ex)
			{
				LoadProblem = ex.Message;
			}
			finally
			{
				Loading = false;
			}
		});
	}

	/// <summary>Work in the background on its own thread, below the game's priority (Terraria keeps its frames).</summary>
	internal static Task Low(Action work)
	{
		return Task.Factory.StartNew(() =>
		{
			Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
			work();
		}, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
	}

	private static Task<T> Low<T>(Func<T> work)
	{
		return Task.Factory.StartNew(() =>
		{
			Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
			return work();
		}, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
	}

	/// <summary>
	/// Like Parallel.For, on <paramref name="degree"/> threads of their own below the game's priority (the thread pool
	/// is the game's too). The first error is thrown once all are done.
	/// </summary>
	private static void Each(int from, int to, int degree, Action<int> body)
	{
		if (to <= from)
		{
			return;
		}
		int next = from - 1;
		Exception error = null;
		Thread[] threads = new Thread[Math.Max(1, Math.Min(degree, to - from))];
		for (int t = 0; t < threads.Length; t++)
		{
			threads[t] = new Thread(() =>
			{
				int i;
				while ((i = Interlocked.Increment(ref next)) < to)
				{
					try
					{
						body(i);
					}
					catch (Exception ex)
					{
						Interlocked.CompareExchange(ref error, ex, null);
					}
				}
			})
			{
				IsBackground = true,
				Priority = ThreadPriority.BelowNormal,
				Name = "IsaClient finder"
			};
			threads[t].Start();
		}
		foreach (Thread t in threads)
		{
			t.Join();
		}
		if (error != null)
		{
			throw error;
		}
	}

	/// <summary>What the search looks in, made once per game (typing then doesn't make garbage every key).</summary>
	private static void Index(FoundGame g)
	{
		g.SearchTitle = (g.Title ?? "").ToLowerInvariant();
		g.SearchText = ((g.Author ?? "") + " " + (g.Blurb ?? "") + " " + (g.Genre ?? "") + " " + string.Join(" ", g.Tags ?? new string[0]) + " " + (g.Description ?? "") + (g.Ds ? " nds ds nintendo" : " gba advance")).ToLowerInvariant();
	}

	private static void Publish(List<FoundGame> games, DateTime stamp)
	{
		foreach (FoundGame g in games)
		{
			Index(g);
		}
		// What's being got keeps going (same key, same object).
		Dictionary<string, FoundGame> old = _games.ToDictionary(g => g.Key, g => g);
		for (int i = 0; i < games.Count; i++)
		{
			if (old.TryGetValue(games[i].Key, out FoundGame keep) && keep.Status != FinderStatus.None)
			{
				games[i] = keep;
			}
		}
		_games = games;
		LoadedAt = stamp;
	}

	private static List<FoundGame> ReadCache(string path, out DateTime stamp)
	{
		stamp = DateTime.MinValue;
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}
			// read as a stream: the catalog is big, and one huge string would make the game stutter on collection
			using StreamReader file = new StreamReader(path, Encoding.UTF8);
			using JsonTextReader reader = new JsonTextReader(file) { DateTimeZoneHandling = DateTimeZoneHandling.Utc };
			CacheFile cache = JsonSerializer.CreateDefault().Deserialize<CacheFile>(reader);
			if (cache?.Games == null)
			{
				return null;
			}
			stamp = cache.Stamp;
			return cache.Games;
		}
		catch
		{
			return null;
		}
	}

	private static void WriteCache(string path, List<FoundGame> games)
	{
		try
		{
			Directory.CreateDirectory(Folder);
			using (StreamWriter file = new StreamWriter(path + ".tmp", false, new UTF8Encoding(false)))
			{
				JsonSerializer.CreateDefault().Serialize(file, new CacheFile { Stamp = DateTime.UtcNow, Games = games });
			}
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			File.Move(path + ".tmp", path);
		}
		catch
		{
		}
	}

	private sealed class CacheFile
	{
		[JsonProperty("stamp")]
		public DateTime Stamp;

		[JsonProperty("games")]
		public List<FoundGame> Games;
	}

	/// <summary>
	/// All sources, for both consoles; Homebrew Hub and Universal-DB first (their files are always there), then itch.io,
	/// Game Jolt and archive.org, without the same game twice. A source that can't be reached keeps what the last list
	/// had from it.
	/// </summary>
	private static List<FoundGame> Fetch(List<FoundGame> cached)
	{
		// Game Jolt answers 504 when asked too much at once: its DS search waits for the GBA one
		Task<List<FoundGame>> gameJolt = Low(() => FetchGameJolt(false));
		Task<List<FoundGame>> gameJoltDs = Low(() =>
		{
			try
			{
				gameJolt.Wait();
			}
			catch
			{
			}
			return FetchGameJolt(true);
		});
		(FinderSource source, bool ds, string name, Task<List<FoundGame>> task)[] sources =
		{
			(FinderSource.HomebrewHub, false, "Homebrew Hub", Low(FetchHomebrewHub)),
			(FinderSource.UniversalDb, true, "Universal-DB", Low(FetchUniversalDb)),
			(FinderSource.Itch, false, "itch.io", Low(() => FetchItch(false))),
			(FinderSource.Itch, true, "itch.io (DS)", Low(() => FetchItch(true))),
			(FinderSource.GameJolt, false, "Game Jolt", gameJolt),
			(FinderSource.GameJolt, true, "Game Jolt (DS)", gameJoltDs),
			(FinderSource.Archive, false, "archive.org", Low(() => FetchArchive(false))),
			(FinderSource.Archive, true, "archive.org (DS)", Low(() => FetchArchive(true)))
		};
		List<FoundGame> all = new List<FoundGame>();
		HashSet<string> titles = new HashSet<string>();
		List<string> problems = new List<string>();
		bool any = false;
		foreach (var (source, ds, name, task) in sources)
		{
			List<FoundGame> list;
			try
			{
				list = task.Result;
				any |= list.Count > 0;
				if (list.Count == 0)
				{
					throw new FinderException("nothing came");
				}
			}
			catch (Exception ex)
			{
				problems.Add(name + ": " + Inner(ex).Message);
				list = cached?.Where(g => g.Source == source && g.Ds == ds).ToList() ?? new List<FoundGame>();
			}
			foreach (FoundGame g in list)
			{
				// the same name on the two consoles is two games
				if (titles.Add((g.Ds ? "ds:" : "gba:") + Plain(g.Title)))
				{
					all.Add(g);
				}
			}
		}
		if (problems.Count > 0)
		{
			LoadProblem = "Some sites didn't answer (" + string.Join("; ", problems) + ")" + ((cached != null) ? ": their games are from the last list." : ".");
		}
		return any ? all : null;
	}

	private static Exception Inner(Exception ex)
	{
		while (ex is AggregateException && ex.InnerException != null)
		{
			ex = ex.InnerException;
		}
		return ex;
	}

	private static List<FoundGame> FetchHomebrewHub()
	{
		JObject first = JObject.Parse(GetText(HhApi + "1", null));
		int pages = first.Value<int?>("page_total") ?? 1;
		Interlocked.Add(ref LoadTotal, pages);
		Interlocked.Increment(ref LoadDone);
		JObject[] results = new JObject[pages];
		results[0] = first;
		Each(2, pages + 1, 4, p =>
		{
			results[p - 1] = JObject.Parse(GetText(HhApi + p, null));
			Interlocked.Increment(ref LoadDone);
		});
		List<FoundGame> games = new List<FoundGame>();
		HashSet<string> seen = new HashSet<string>();
		foreach (JObject page in results)
		{
			foreach (JObject e in page["entries"] ?? new JArray())
			{
				string slug = Str(e["slug"]);
				if (string.IsNullOrEmpty(slug) || !seen.Add(slug))
				{
					continue;
				}
				string type = Str(e["typetag"]);
				if (type != null && type != "game" && type != "demo")
				{
					continue;
				}
				string basePath = Str(e["basepath"]) ?? "database-gba";
				string entryUrl = HhStatic + basePath + "/entries/" + slug + "/";
				string shot = e["screenshots"]?.Values<string>().FirstOrDefault();
				string description = Str(e["description"]) ?? "";
				List<JObject> files = (e["files"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
				// The default file first, then playable ones.
				files = files.OrderByDescending(f => (bool?)f["default"] == true).ThenByDescending(f => (bool?)f["playable"] == true).ToList();
				string repo = Str(e["repository"]);
				string website = Str(e["website"]);
				games.Add(new FoundGame
				{
					Key = "hh:" + slug,
					Source = FinderSource.HomebrewHub,
					Title = (Str(e["title"]) ?? slug).Trim(),
					Author = Developers(e["developer"]),
					Description = description.Trim(),
					Blurb = FirstSentence(description),
					Page = "https://hh3.gbdev.io/game/" + slug,
					Cover = (shot != null) ? (entryUrl + shot) : null,
					Tags = e["tags"]?.Values<string>().ToArray() ?? new string[0],
					Genre = e["tags"]?.Values<string>().FirstOrDefault(),
					HhBase = entryUrl,
					HhSlug = slug,
					HhFiles = files.Select(f => Str(f["filename"])).Where(f => !string.IsNullOrEmpty(f)).ToArray(),
					Repo = GitHubRepo(repo) ?? GitHubRepo(website)
				});
			}
		}
		return games;
	}

	private static List<FoundGame> FetchItch(bool ds)
	{
		string[] tags = ds ? ItchDsTags : ItchTags;
		List<FoundGame> games = new List<FoundGame>();
		HashSet<string> seen = new HashSet<string>();
		List<FoundGame>[] perTag = new List<FoundGame>[tags.Length];
		Interlocked.Add(ref LoadTotal, tags.Length);
		Each(0, tags.Length, 2, t =>
		{
			List<FoundGame> list = new List<FoundGame>();
			for (int page = 1; page <= 60; page++)
			{
				JObject json = JObject.Parse(GetText("https://itch.io/games/free/" + tags[t] + "?format=json&page=" + page, null));
				int count = json.Value<int?>("num_items") ?? 0;
				list.AddRange(ParseItchCells((string)json["content"] ?? ""));
				if (page == 1 && count >= 30)
				{
					// More pages to come: a rough count for the progress.
					Interlocked.Add(ref LoadTotal, 3);
				}
				if (count < 30)
				{
					break;
				}
				Interlocked.Increment(ref LoadDone);
			}
			Interlocked.Increment(ref LoadDone);
			perTag[t] = list;
		});
		foreach (List<FoundGame> list in perTag)
		{
			foreach (FoundGame g in list ?? new List<FoundGame>())
			{
				if (seen.Add(g.Key))
				{
					games.Add(g);
				}
			}
		}
		// Adult games aren't in the browse pages: the RSS feed of the same tags has them.
		HashSet<string> pages = new HashSet<string>(games.Select(g => g.Page), StringComparer.OrdinalIgnoreCase);
		foreach (string feed in ds ? ItchDsAdultFeeds : ItchAdultFeeds)
		{
			try
			{
				foreach (FoundGame g in ParseItchFeed(GetText("https://itch.io/" + feed, null)))
				{
					if (pages.Add(g.Page))
					{
						g.Adult = true;
						games.Add(g);
					}
				}
			}
			catch
			{
			}
		}
		foreach (FoundGame g in games)
		{
			g.Ds = ds;
			if (ds)
			{
				// the same itch.io game can be in the GBA tags too: a key of its own on the DS side
				g.Key = "ds:" + g.Key;
			}
		}
		return games;
	}

	private static readonly string[] ItchAdultFeeds = { "games/free/tag-gameboy-advance/tag-nsfw.xml", "games/free/tag-game-boy-advance/tag-nsfw.xml" };

	private static readonly string[] ItchDsAdultFeeds = { "games/free/tag-nds/tag-nsfw.xml" };

	#region Universal-DB

	/// <summary>The DS games of Universal-DB (its whole database is one JSON file).</summary>
	private static List<FoundGame> FetchUniversalDb()
	{
		Interlocked.Add(ref LoadTotal, 1);
		JArray all = JArray.Parse(GetText(UniversalDbJson, null));
		Interlocked.Increment(ref LoadDone);
		List<FoundGame> games = new List<FoundGame>();
		foreach (JObject e in all.OfType<JObject>())
		{
			bool ds = e["systems"]?.Values<string>().Any(x => string.Equals(x, "DS", StringComparison.OrdinalIgnoreCase)) == true;
			bool game = e["categories"]?.Values<string>().Any(x => x == "game") == true;
			string slug = Str(e["slug"]);
			if (!ds || !game || string.IsNullOrEmpty(slug))
			{
				continue;
			}
			// .nds files first, then zips; never the 3DS ones (.cia, .3dsx) nor what can't be opened (.7z)
			List<(string name, string url)> files = ((e["downloads"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
				.Select(f => (name: f.Name, url: Str(f.Value?["url"])))
				.Where(f => f.url != null && (f.name.EndsWith(".nds", StringComparison.OrdinalIgnoreCase) || (f.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(f.name, "3ds|cia|manual", RegexOptions.IgnoreCase))))
				.OrderBy(f => f.name.EndsWith(".nds", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
				.ToList();
			string repo = Str(e["github"]);
			if (files.Count == 0 && repo == null)
			{
				continue;
			}
			string shot = Str((e["screenshots"] as JArray)?.OfType<JObject>().FirstOrDefault()?["url"]);
			string description = Str(e["long_description"]) ?? Str(e["description"]) ?? "";
			games.Add(new FoundGame
			{
				Key = "udb:" + slug,
				Source = FinderSource.UniversalDb,
				Ds = true,
				Title = (Str(e["title"]) ?? slug).Trim(),
				Author = Str(e["author"]) ?? "",
				Description = description.Trim(),
				Blurb = FirstSentence(Str(e["description"]) ?? description),
				Page = "https://db.universal-team.net/ds/" + slug,
				Cover = shot ?? Str(e["image"]) ?? Str(e["icon"]),
				Tags = e["categories"]?.Values<string>().ToArray() ?? new string[0],
				Urls = files.Select(f => f.url).ToArray(),
				Repo = repo
			});
		}
		return games;
	}

	private static List<string> GetUniversalDb(FoundGame g)
	{
		string why = null;
		foreach (string url in (g.Urls ?? new string[0]).Take(4))
		{
			List<string> roms = TryDownload(g, url, Path.GetFileName(Uri.UnescapeDataString(new Uri(url).AbsolutePath)), null, ref why);
			if (roms != null)
			{
				return roms;
			}
		}
		if (g.Repo != null)
		{
			List<string> roms = GetGitHub(g, ref why);
			if (roms != null)
			{
				return roms;
			}
		}
		throw new FinderException(why ?? "Its ROM isn't published there: open its page to see where it is.");
	}

	#endregion

	/// <summary>The games in an itch.io RSS feed (free ones only).</summary>
	private static IEnumerable<FoundGame> ParseItchFeed(string xml)
	{
		foreach (Match item in Regex.Matches(xml, "<item>(.*?)</item>", RegexOptions.Singleline))
		{
			string it = item.Groups[1].Value;
			string link = WebUtility.HtmlDecode(Regex.Match(it, "<link>(.*?)</link>", RegexOptions.Singleline).Groups[1].Value.Trim());
			if (link.Length == 0)
			{
				continue;
			}
			string price = Regex.Match(it, "<price>(.*?)</price>").Groups[1].Value;
			if (price.Length > 0 && !Regex.IsMatch(price, "^\\D*0+([.,]0+)?$"))
			{
				continue;
			}
			string title = WebUtility.HtmlDecode(Regex.Match(it, "<plainTitle>(.*?)</plainTitle>", RegexOptions.Singleline).Groups[1].Value);
			string description = Regex.Match(it, "<description><!\\[CDATA\\[(.*?)\\]\\]></description>", RegexOptions.Singleline).Groups[1].Value;
			Match host = Regex.Match(link, "https?://([^.]+)\\.itch\\.io");
			string text = Html(description);
			yield return new FoundGame
			{
				Key = "itch:" + Regex.Replace(link.ToLowerInvariant(), "^https?://", ""),
				Source = FinderSource.Itch,
				Title = (title.Length > 0) ? title : link,
				Page = link,
				Author = host.Success ? host.Groups[1].Value : "",
				Blurb = text,
				Description = text,
				Cover = NullIfEmpty(WebUtility.HtmlDecode(Regex.Match(it, "<imageurl>(.*?)</imageurl>").Groups[1].Value)),
				Tags = new string[0]
			};
		}
	}

	private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

	#region Game Jolt

	/// <summary>What the catalog asks Game Jolt for (its search finds the words anywhere).</summary>
	private static readonly string[] GameJoltQueries = { "gba", "gba rom", "game boy advance" };

	private static readonly string[] GameJoltDsQueries = { "nds", "nintendo ds", "ds homebrew" };

	/// <summary>Pages per search (36 games each): enough for what has a ROM, without asking the site too much.</summary>
	private const int GameJoltPages = 5;

	private static List<FoundGame> FetchGameJolt(bool ds)
	{
		string[] queries = ds ? GameJoltDsQueries : GameJoltQueries;
		Interlocked.Add(ref LoadTotal, queries.Length);
		ConcurrentDictionary<long, JObject> found = new ConcurrentDictionary<long, JObject>();
		Each(0, queries.Length, 2, i =>
		{
			// DS games rarely say "ROM" there: their files tell
			foreach (JObject game in GameJoltSearch(queries[i], ds ? 2 : GameJoltPages, !ds))
			{
				found.TryAdd(game.Value<long>("id"), game);
			}
			Interlocked.Increment(ref LoadDone);
		});
		return GameJoltGames(found.Values.ToList(), ds);
	}

	/// <summary>Search results (only those that say they have a ROM among their files, with <paramref name="romsOnly"/>).</summary>
	private static List<JObject> GameJoltSearch(string query, int pages, bool romsOnly = true)
	{
		List<JObject> list = new List<JObject>();
		for (int page = 1; page <= pages; page++)
		{
			JObject json;
			try
			{
				json = JObject.Parse(GetText("https://gamejolt.com/site-api/web/search/games?q=" + Uri.EscapeDataString(query) + "&page=" + page, null));
			}
			catch (WebException) when (page > 1)
			{
				// a later page that didn't come: what came is kept
				break;
			}
			JArray games = json["payload"]?["games"] as JArray;
			if (games == null || games.Count == 0)
			{
				break;
			}
			list.AddRange(games.OfType<JObject>().Where(g => !romsOnly || (bool?)g["compatibility"]?["type_rom"] == true));
			if (games.Count < 36)
			{
				break;
			}
		}
		return list;
	}

	/// <summary>
	/// Only the ones whose files include a ROM (.gba or .nds, or a zip): each game's packages are looked at. A game with
	/// .nds files is a DS game, with .gba a GBA one, else what it was searched as (<paramref name="ds"/>).
	/// </summary>
	private static List<FoundGame> GameJoltGames(List<JObject> candidates, bool ds)
	{
		ConcurrentBag<(int order, FoundGame game)> games = new ConcurrentBag<(int, FoundGame)>();
		Each(0, candidates.Count, 4, i =>
		{
			JObject g = candidates[i];
			try
			{
				long id = g.Value<long>("id");
				List<(long id, string file, string type)> builds = GameJoltBuilds(id);
				bool isDs = builds.Any(b => DsRom.IsDsFile(b.file)) || (ds && !builds.Any(b => b.file.EndsWith(".gba", StringComparison.OrdinalIgnoreCase)));
				List<string> files = GameJoltRomFiles(builds, isDs).Select(b => b.file).ToList();
				if (files.Count == 0)
				{
					return;
				}
				string cover = Str((g["thumbnail_media_item"] as JObject)?["img_url"]);
				if (cover == null || cover.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
				{
					cover = Str((g["header_media_item"] as JObject)?["img_url"]) ?? cover;
				}
				int age = g.Value<int?>("tigrs_age") ?? 0;
				games.Add((i, new FoundGame
				{
					Key = "gj:" + id,
					Source = FinderSource.GameJolt,
					Ds = isDs,
					GjId = id,
					Title = (Str(g["title"]) ?? "").Trim(),
					Author = Str(g["developer"]?["display_name"]) ?? Str(g["developer"]?["username"]) ?? "",
					Page = "https://gamejolt.com/games/" + (Str(g["path"]) ?? Str(g["slug"]) ?? "game") + "/" + id,
					Cover = (cover != null && !cover.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) ? cover : null,
					Blurb = string.Join(", ", files.Take(2)),
					Description = "",
					Tags = new string[0],
					Adult = (bool?)g["has_adult_content"] == true || age >= 3
				}));
			}
			catch
			{
			}
		});
		return games.OrderBy(x => x.order).Select(x => x.game).ToList();
	}

	/// <summary>A Game Jolt game's builds: (build id, file name, type: rom, downloadable, html...).</summary>
	private static List<(long id, string file, string type)> GameJoltBuilds(long game)
	{
		JObject json = JObject.Parse(GetText("https://gamejolt.com/site-api/web/discover/games/packages/" + game, null));
		JArray builds = json["payload"]?["builds"] as JArray ?? new JArray();
		return builds.OfType<JObject>()
			.Select(b => (id: b.Value<long>("id"), file: Str(b["primary_file"]?["filename"]) ?? "", type: Str(b["type"]) ?? ""))
			.Where(b => b.file.Length > 0).ToList();
	}

	private static readonly Regex GameBoyName = new Regex("\\.(gb|gbc)$|[(\\[ _-](gb|gbc|dmg|game ?boy color)[)\\] _.-]", RegexOptions.IgnoreCase);

	/// <summary>
	/// The builds that may hold a GBA ROM, the .gba ones first: a .gba, or a zip that isn't the web version (html) nor
	/// named for the Game Boy / Game Boy Color. When the game has Game Boy ROMs and no .gba, its zips are those too.
	/// </summary>
	private static List<(long id, string file, string type)> GameJoltRomFiles(List<(long id, string file, string type)> builds, bool ds)
	{
		if (ds)
		{
			// an .nds, or a zip that says it's the DS version (Game Jolt games ship many systems)
			return builds.Where(b => DsRom.IsDsFile(b.file) || (Regex.IsMatch(b.file, "\\.zip$", RegexOptions.IgnoreCase) && b.type != "html" && Regex.IsMatch(b.file, "(^|[^a-z0-9])(nds|ds|dsi)([^a-z0-9]|$)|nintendo", RegexOptions.IgnoreCase) && !Regex.IsMatch(b.file, "3ds|windows|win32|win64|macos|mac-|linux|android|apk", RegexOptions.IgnoreCase)))
				.OrderBy(b => DsRom.IsDsFile(b.file) ? 0 : 1)
				.ToList();
		}
		bool hasGba = builds.Any(b => b.file.EndsWith(".gba", StringComparison.OrdinalIgnoreCase));
		bool gameBoy = builds.Any(b => Regex.IsMatch(b.file, "\\.(gb|gbc)$", RegexOptions.IgnoreCase));
		return builds.Where(b => b.file.EndsWith(".gba", StringComparison.OrdinalIgnoreCase)
				|| (Regex.IsMatch(b.file, "\\.(zip|7z|rar)$", RegexOptions.IgnoreCase) && b.type != "html" && !GameBoyName.IsMatch(b.file) && (hasGba || !gameBoy)))
			.OrderBy(b => b.file.EndsWith(".gba", StringComparison.OrdinalIgnoreCase) ? 0 : (Regex.IsMatch(b.file, "gba|advance", RegexOptions.IgnoreCase) ? 1 : 2))
			.ToList();
	}

	private static readonly ConcurrentDictionary<string, List<FoundGame>> _online = new ConcurrentDictionary<string, List<FoundGame>>(StringComparer.OrdinalIgnoreCase);

	private static readonly ConcurrentDictionary<string, bool> _onlineAsked = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

	/// <summary>Searches going on Game Jolt and archive.org right now.</summary>
	public static int OnlineSearches;

	/// <summary>Goes up each time a Game Jolt search comes back (the results are worked out again).</summary>
	public static int OnlineVersion;

	/// <summary>
	/// What's typed, looked up on Game Jolt and archive.org as well (their searches cover far more than the catalog);
	/// the results join the search once they're here. Null while it's going.
	/// </summary>
	public static List<FoundGame> SearchOnline(string query)
	{
		query = (query ?? "").Trim();
		if (query.Length < 2)
		{
			return null;
		}
		if (_online.TryGetValue(query, out List<FoundGame> done))
		{
			return done;
		}
		if (_onlineAsked.TryAdd(query, true))
		{
			Interlocked.Increment(ref OnlineSearches);
			Low(() =>
			{
				try
				{
					HashSet<string> known = new HashSet<string>(_games.Select(g => g.Key));
					Task<List<FoundGame>> archive = Low(() => ArchiveGames(ArchiveSearch(ArchiveWords(query, false), 60), false).Where(g => !known.Contains(g.Key)).ToList());
					Task<List<FoundGame>> archiveDs = Low(() => ArchiveGames(ArchiveSearch(ArchiveWords(query, true), 40), true).Where(g => !known.Contains(g.Key)).ToList());
					List<FoundGame> results = new List<FoundGame>();
					try
					{
						Dictionary<long, JObject> found = new Dictionary<long, JObject>();
						foreach (string q in new[] { query, query + " gba" })
						{
							foreach (JObject g in GameJoltSearch(q, 2))
							{
								found[g.Value<long>("id")] = g;
							}
						}
						results.AddRange(GameJoltGames(found.Values.Where(g => !known.Contains("gj:" + g.Value<long>("id"))).ToList(), false));
						HashSet<long> had = new HashSet<long>(results.Select(g => g.GjId));
						List<JObject> ds = GameJoltSearch(query + " nds", 1, false).Where(g => !known.Contains("gj:" + g.Value<long>("id")) && !had.Contains(g.Value<long>("id"))).ToList();
						results.AddRange(GameJoltGames(ds, true));
					}
					catch
					{
					}
					foreach (Task<List<FoundGame>> t in new[] { archive, archiveDs })
					{
						try
						{
							results.AddRange(t.Result);
						}
						catch
						{
						}
					}
					foreach (FoundGame g in results)
					{
						Index(g);
					}
					_online[query] = results;
					Interlocked.Increment(ref OnlineVersion);
				}
				catch
				{
					_online[query] = new List<FoundGame>();
				}
				finally
				{
					Interlocked.Decrement(ref OnlineSearches);
				}
			});
		}
		return null;
	}

	private static List<string> GetGameJolt(FoundGame g)
	{
		string why = null;
		List<(long id, string file, string type)> builds = GameJoltRomFiles(GameJoltBuilds(g.GjId), g.Ds).Take(4).ToList();
		if (builds.Count == 0)
		{
			throw new FinderException("It has no " + SystemName(g) + " ROM to download on Game Jolt.");
		}
		foreach (var b in builds)
		{
			g.Message = "Getting " + b.file + "...";
			JObject answer = JObject.Parse(PostText("https://gamejolt.com/site-api/web/discover/games/builds/get-download-url/" + b.id, "{\"forceDownload\":true}", null, "application/json"));
			string url = Str(answer["payload"]?["url"]);
			if (url == null || !url.Contains("download.gamejolt.net"))
			{
				why ??= "Game Jolt didn't give " + b.file + " (it may have to be bought).";
				continue;
			}
			List<string> roms = TryDownload(g, url, b.file, null, ref why);
			if (roms != null)
			{
				return roms;
			}
		}
		throw new FinderException(why ?? ("None of its files is a " + SystemName(g) + " ROM."));
	}

	#endregion

	private static string SystemName(FoundGame g) => g.Ds ? "Nintendo DS" : "Game Boy Advance";

	/// <summary>Where each game's cell starts (the site changes the order of the tag's attributes from page to page).</summary>
	private static readonly Regex CellSplit = new Regex("(?=<div [^>]*data-game_id=\")", RegexOptions.Compiled);

	internal static IEnumerable<FoundGame> ParseItchCells(string html)
	{
		foreach (string cell in CellSplit.Split(html))
		{
			Match id = Regex.Match(cell, "^<div [^>]*data-game_id=\"(\\d+)\"");
			if (!id.Success)
			{
				continue;
			}
			Match link = Regex.Match(cell, "class=\"title game_link\"[^>]*href=\"([^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline);
			if (!link.Success)
			{
				link = Regex.Match(cell, "href=\"([^\"]+)\"[^>]*class=\"title game_link\"[^>]*>(.*?)</a>", RegexOptions.Singleline);
			}
			if (!link.Success)
			{
				continue;
			}
			string text = Html(Regex.Match(cell, "class=\"game_text\"[^>]*>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value);
			string cover = Regex.Match(cell, "data-lazy_src=\"([^\"]+)\"").Groups[1].Value;
			if (cover.Length == 0)
			{
				cover = Regex.Match(cell, "<img[^>]*src=\"(https://img\\.itch\\.zone/[^\"]+)\"").Groups[1].Value;
			}
			yield return new FoundGame
			{
				Key = "itch:" + id.Groups[1].Value,
				Source = FinderSource.Itch,
				Title = Html(link.Groups[2].Value),
				Page = WebUtility.HtmlDecode(link.Groups[1].Value),
				Author = Html(Regex.Match(cell, "class=\"game_author\"><a[^>]*>(.*?)</a>", RegexOptions.Singleline).Groups[1].Value),
				Blurb = text,
				Description = text,
				Genre = Html(Regex.Match(cell, "class=\"game_genre\"[^>]*>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value),
				Cover = (cover.Length > 0) ? WebUtility.HtmlDecode(cover) : null,
				Tags = new string[0]
			};
		}
	}

	#region archive.org

	/// <summary>Software items marked as Game Boy Advance.</summary>
	private const string ArchiveBase = "mediatype:software AND (subject:gba OR title:gba OR subject:\"game boy advance\" OR title:\"game boy advance\" OR subject:\"gameboy advance\" OR title:\"gameboy advance\")";

	/// <summary>Software items marked as Nintendo DS homebrew (the DS ones must say homebrew: the rest are mostly dumps).</summary>
	private const string ArchiveDsBase = "mediatype:software AND (subject:nds OR title:nds OR subject:\"nintendo ds\" OR title:\"nintendo ds\") AND (subject:homebrew OR title:homebrew OR description:homebrew)";

	private static List<FoundGame> FetchArchive(bool ds)
	{
		Interlocked.Add(ref LoadTotal, 2);
		List<JObject> docs = ArchiveSearch(ds ? ArchiveDsBase : ArchiveBase, ds ? 150 : 300);
		Interlocked.Increment(ref LoadDone);
		List<FoundGame> games = ArchiveGames(docs, ds);
		Interlocked.Increment(ref LoadDone);
		return games;
	}

	/// <summary>The homebrew search with the typed words as well.</summary>
	private static string ArchiveWords(string query, bool ds)
	{
		string baseQuery = ds ? ArchiveDsBase : ArchiveBase;
		string words = string.Join(" AND ", query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(w => Regex.Replace(w, "[^\\p{L}\\p{N}]", "")).Where(w => w.Length > 0).Select(w => "(" + w + ")"));
		return (words.Length > 0) ? (baseQuery + " AND " + words) : baseQuery;
	}

	private static List<JObject> ArchiveSearch(string query, int rows)
	{
		string url = "https://archive.org/advancedsearch.php?q=" + Uri.EscapeDataString(query) + "&fl[]=identifier&fl[]=title&fl[]=creator&fl[]=description&rows=" + rows + "&page=1&output=json";
		JObject json = JObject.Parse(GetText(url, null));
		return (json["response"]?["docs"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
	}

	/// <summary>Only items with a ROM (a .gba up to 32 MB or an .nds up to 256 MB, or a zip); collections bring all their ROMs.</summary>
	private static List<FoundGame> ArchiveGames(List<JObject> docs, bool ds)
	{
		ConcurrentBag<(int order, FoundGame game)> games = new ConcurrentBag<(int, FoundGame)>();
		string ext = ds ? ".nds" : ".gba";
		long most = ds ? (256L << 20) : (32L << 20);
		Each(0, docs.Count, 4, i =>
		{
			JObject d = docs[i];
			string id = Str(d["identifier"]);
			if (string.IsNullOrEmpty(id))
			{
				return;
			}
			try
			{
				JObject meta = JObject.Parse(GetText("https://archive.org/metadata/" + Uri.EscapeDataString(id) + "/files", null));
				List<(string name, long size)> files = ((meta["result"] ?? meta["files"]) as JArray)?.OfType<JObject>()
					.Select(f => (name: Str(f["name"]) ?? "", size: long.TryParse(Str(f["size"]), out long n) ? n : 0L)).ToList() ?? new List<(string, long)>();
				List<string> roms = files.Where(f => f.name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) && f.size > 0 && f.size <= most && !RetailDump(f.name)).Select(f => f.name).ToList();
				List<string> zips = files.Where(f => f.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && f.size <= 150L << 20 && !f.name.EndsWith("_jp2.zip", StringComparison.OrdinalIgnoreCase) && !RetailDump(f.name)).Select(f => f.name).ToList();
				if (roms.Count == 0 && zips.Count == 0)
				{
					return;
				}
				string description = Html(Str(d["description"]) ?? "");
				games.Add((i, new FoundGame
				{
					Key = (ds ? "ds:ia:" : "ia:") + id,
					Source = FinderSource.Archive,
					Ds = ds,
					IaId = id,
					IaFiles = roms.Concat(zips).ToArray(),
					Title = Html(Str(d["title"]) ?? id),
					Author = Str(d["creator"]) ?? "",
					Description = description,
					Blurb = FirstSentence(description),
					Page = "https://archive.org/details/" + id,
					Cover = "https://archive.org/services/img/" + id,
					Tags = new string[0]
				}));
			}
			catch
			{
			}
		});
		return games.OrderBy(x => x.order).Select(x => x.game).ToList();
	}

	/// <summary>ROMs a collection brings per Get (the rest with the next Get).</summary>
	private const int ArchiveBatch = 30;

	private static List<string> GetArchive(FoundGame g)
	{
		string why = null;
		string ext = g.Ds ? ".nds" : ".gba";
		List<string> gba = (g.IaFiles ?? new string[0]).Where(f => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)).ToList();
		if (gba.Count > 0)
		{
			// The ones not got yet, the next batch of them.
			string dest = GameFolder(g);
			List<string> missing = gba.Where(f => !File.Exists(Path.Combine(dest, SavedName(Path.GetFileName(f), g.Ds)))).ToList();
			List<string> got = new List<string>();
			int n = 0;
			foreach (string file in missing.Take(ArchiveBatch))
			{
				n++;
				g.Message = (missing.Count > 1) ? ("Getting " + n + " of " + Math.Min(ArchiveBatch, missing.Count) + ": " + Path.GetFileName(file)) : ("Getting " + Path.GetFileName(file) + "...");
				List<string> roms = TryDownload(g, ArchiveUrl(g.IaId, file), Path.GetFileName(file), null, ref why);
				if (roms != null)
				{
					got.AddRange(roms);
				}
			}
			g.More = Math.Max(0, missing.Count - ArchiveBatch);
			if (got.Count > 0)
			{
				return got;
			}
			if (missing.Count == 0)
			{
				// All of them were got before.
				return gba.Select(f => Path.Combine(dest, SavedName(Path.GetFileName(f), g.Ds))).Where(File.Exists).ToList();
			}
		}
		foreach (string file in (g.IaFiles ?? new string[0]).Where(f => !f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)).Take(4))
		{
			List<string> roms = TryDownload(g, ArchiveUrl(g.IaId, file), Path.GetFileName(file), null, ref why);
			if (roms != null)
			{
				return roms;
			}
		}
		throw new FinderException(why ?? ("It has no " + SystemName(g) + " ROM to download on archive.org."));
	}

	private static readonly Regex RegionTag = new Regex("[(\\[](USA|Europe|Japan|World|EUR|JPN|JAP|Germany|France|Spain|Italy|Korea|China|Australia|Brazil|Netherlands|Sweden|U|E|J)([ ,)\\]]|$)", RegexOptions.IgnoreCase);

	
	/// <summary>
	/// A licensed retail release named the way dump sets name them (the region in brackets, and nothing saying it's
	/// unlicensed, homebrew or a prototype): those aren't the community's games, so they're not offered.
	/// </summary>
	private static bool RetailDump(string path)
	{
		string name = Path.GetFileName(path);
		return RegionTag.IsMatch(name);
	}

	private static string ArchiveUrl(string id, string file)
	{
		return "https://archive.org/download/" + Uri.EscapeDataString(id) + "/" + string.Join("/", file.Split('/').Select(Uri.EscapeDataString));
	}

	#endregion

	/// <summary>The games matching the words (all of them, in the title, author, text or tags), best first.</summary>
	/// <param name="ds">Only DS games (true), only GBA ones (false), or both (null).</param>
	public static List<FoundGame> Search(string query, FinderSource? source, bool? ds, bool adult, bool online)
	{
		List<FoundGame> games = _games;
		List<FoundGame> more = (online && (source == null || source == FinderSource.GameJolt || source == FinderSource.Archive)) ? SearchOnline(query) : null;
		if (more != null && more.Count > 0)
		{
			games = games.Concat(more).ToList();
		}
		string[] words = (query ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		List<(FoundGame g, int score, int order)> hits = new List<(FoundGame, int, int)>();
		for (int i = 0; i < games.Count; i++)
		{
			FoundGame g = games[i];
			if ((source.HasValue && g.Source != source.Value) || (ds.HasValue && g.Ds != ds.Value) || (g.Adult && !adult))
			{
				continue;
			}
			if (words.Length == 0)
			{
				hits.Add((g, 0, i));
				continue;
			}
			if (g.SearchText == null)
			{
				Index(g);
			}
			string title = g.SearchTitle;
			string rest = g.SearchText;
			int score = 0;
			bool all = true;
			foreach (string w in words)
			{
				if (title.StartsWith(w))
				{
					score += 6;
				}
				else if (title.Contains(w))
				{
					score += 4;
				}
				else if (rest.Contains(w))
				{
					score += 1;
				}
				else
				{
					all = false;
					break;
				}
			}
			if (all)
			{
				hits.Add((g, score, i));
			}
		}
		return hits.OrderByDescending(h => h.score).ThenBy(h => h.order).Select(h => h.g).ToList();
	}

	#endregion

	#region Pictures

	/// <summary>A picture, decoded and fit in 240x180 (ARGB pixels), for the main thread to make a texture of.</summary>
	public sealed class Picture
	{
		public int Width;

		public int Height;

		public int[] Argb;
	}

	private static readonly ConcurrentDictionary<string, Picture> _pictures = new ConcurrentDictionary<string, Picture>();

	private static readonly ConcurrentDictionary<string, bool> _picturesAsked = new ConcurrentDictionary<string, bool>();

	/// <summary>Pictures read at once (each decode is work: a few at a time, below the game's priority).</summary>
	private static readonly SemaphoreSlim _pictureSlots = new SemaphoreSlim(3);

	/// <summary>The picture at <paramref name="url"/>: null until it's here (it's fetched once, then from the disk cache).</summary>
	public static Picture PictureFor(string url)
	{
		if (string.IsNullOrEmpty(url))
		{
			return null;
		}
		if (_pictures.TryGetValue(url, out Picture p))
		{
			return p;
		}
		if (_picturesAsked.TryAdd(url, true))
		{
			Task.Run(async () =>
			{
				await _pictureSlots.WaitAsync().ConfigureAwait(false);
				Thread me = Thread.CurrentThread;
				ThreadPriority old = me.Priority;
				try
				{
					me.Priority = ThreadPriority.BelowNormal;
					_pictures[url] = LoadPicture(url);
				}
				catch
				{
					_pictures[url] = null;
				}
				finally
				{
					me.Priority = old;
					_pictureSlots.Release();
				}
			});
		}
		return null;
	}

	/// <summary>
	/// The picture once it's here, handed over (the finder forgets it: the caller keeps its texture, and asks again
	/// after dropping it). Null while it's coming; <paramref name="failed"/> when it couldn't be had.
	/// </summary>
	public static Picture TakePicture(string url, out bool failed)
	{
		failed = false;
		Picture p = PictureFor(url);
		if (p == null && (string.IsNullOrEmpty(url) || !_pictures.ContainsKey(url)))
		{
			failed = string.IsNullOrEmpty(url);
			return null;
		}
		_pictures.TryRemove(url, out p);
		_picturesAsked.TryRemove(url, out _);
		failed = p == null;
		return p;
	}

	private static Picture LoadPicture(string url)
	{
		string dir = Path.Combine(Folder, "Pictures");
		Directory.CreateDirectory(dir);
		string file = Path.Combine(dir, Hash(url) + ".img");
		byte[] bytes;
		if (File.Exists(file))
		{
			bytes = File.ReadAllBytes(file);
		}
		else
		{
			bytes = GetBytes(url, null, 8L << 20, null);
			File.WriteAllBytes(file, bytes);
		}
		using MemoryStream ms = new MemoryStream(bytes);
		using Image image = Image.FromStream(ms);
		float scale = Math.Min(1f, Math.Min(240f / image.Width, 180f / image.Height));
		int w = Math.Max(1, (int)(image.Width * scale));
		int h = Math.Max(1, (int)(image.Height * scale));
		using Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
		using (Graphics gr = Graphics.FromImage(bmp))
		{
			// Pixel art stays sharp when it's only made smaller by a whole step or not at all.
			gr.InterpolationMode = (scale >= 0.99f) ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
			gr.PixelOffsetMode = PixelOffsetMode.Half;
			gr.DrawImage(image, new Rectangle(0, 0, w, h));
		}
		BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		int[] argb = new int[w * h];
		for (int y = 0; y < h; y++)
		{
			Marshal.Copy(data.Scan0 + y * data.Stride, argb, y * w, w);
		}
		bmp.UnlockBits(data);
		return new Picture { Width = w, Height = h, Argb = argb };
	}

	#endregion

	#region Getting a game

	/// <summary>Where a game's ROMs go.</summary>
	public static string GameFolder(FoundGame g) => Path.Combine(RomFolder, FolderName(g.Key));

	/// <summary>A short, safe folder name for a game (long keys, like links, are cut and given a hash).</summary>
	private static string FolderName(string key)
	{
		string safe = Safe(key);
		return (safe.Length <= 40) ? safe : (safe.Substring(0, 28).TrimEnd('_', '.') + "-" + Hash(key).Substring(0, 8));
	}

	/// <summary>The ROMs already got for this game earlier (null when none).</summary>
	public static List<string> AlreadyGot(FoundGame g)
	{
		string dir = GameFolder(g);
		if (!Directory.Exists(dir))
		{
			return null;
		}
		List<string> roms = Directory.GetFiles(dir, "*.gba").Concat(Directory.GetFiles(dir, "*.nds")).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
		return (roms.Count > 0) ? roms : null;
	}

	/// <summary>Gets the game in the background; when it's done it lands in <see cref="Finished"/>.</summary>
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
		Low(() =>
		{
			try
			{
				List<string> roms = g.Source switch
				{
					FinderSource.HomebrewHub => GetHomebrewHub(g),
					FinderSource.UniversalDb => GetUniversalDb(g),
					FinderSource.GameJolt => GetGameJolt(g),
					FinderSource.Archive => GetArchive(g),
					_ => GetItch(g)
				};
				if (roms == null || roms.Count == 0)
				{
					throw new FinderException(g.Message ?? ("No " + SystemName(g) + " ROM came with it."));
				}
				g.Roms = roms;
				g.Message = null;
				g.Status = FinderStatus.Done;
				Finished.Enqueue(g);
			}
			catch (Exception ex)
			{
				g.Message = (ex is FinderException) ? ex.Message : ("Couldn't get it: " + ex.Message);
				g.Status = FinderStatus.Failed;
			}
		});
	}

	internal sealed class FinderException : Exception
	{
		public FinderException(string message)
			: base(message)
		{
		}
	}

	private static List<string> GetHomebrewHub(FoundGame g)
	{
		string why = null;
		foreach (string file in g.HhFiles ?? new string[0])
		{
			string url = g.HhBase + string.Join("/", file.Split('/').Select(Uri.EscapeDataString));
			List<string> roms = TryDownload(g, url, Path.GetFileName(file), null, ref why);
			if (roms != null)
			{
				return roms;
			}
		}
		if (g.Repo != null)
		{
			List<string> roms = GetGitHub(g, ref why);
			if (roms != null)
			{
				return roms;
			}
		}
		throw new FinderException(why ?? "Its ROM isn't published there: open its page to see where it is.");
	}

	/// <summary>The ROM from the project's GitHub releases (.gba / .nds files first, then .zip).</summary>
	private static List<string> GetGitHub(FoundGame g, ref string why)
	{
		g.Message = "Looking at its GitHub releases...";
		string ext = g.Ds ? ".nds" : ".gba";
		JArray releases = JArray.Parse(GetText("https://api.github.com/repos/" + g.Repo + "/releases?per_page=10", null));
		var assets = releases.OfType<JObject>().SelectMany(r => (r["assets"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
			.Select(a => (name: (string)a["name"] ?? "", url: (string)a["browser_download_url"], size: (long?)a["size"] ?? 0L))
			.Where(a => a.url != null && (a.name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) || (a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !(g.Ds && Regex.IsMatch(a.name, "3ds", RegexOptions.IgnoreCase)))))
			.OrderBy(a => a.name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
			.Take(4).ToList();
		foreach (var a in assets)
		{
			List<string> roms = TryDownload(g, a.url, a.name, null, ref why);
			if (roms != null)
			{
				return roms;
			}
		}
		why ??= "Its releases have no " + SystemName(g) + " ROM.";
		return null;
	}

	internal sealed class ItchUpload
	{
		public string Id;

		public string Name;

		public string SizeText;

		public bool Platforms;

		public int Score;
	}

	private static List<string> GetItch(FoundGame g)
	{
		return FromItch(g, html => Uploads(html, g.Ds), (string url, ref string why) => TryDownload(g, url, null, null, ref why), SystemName(g) + " ROM");
	}

	/// <summary>Tries one download of a game: what came (null, with why, when it wasn't what's wanted).</summary>
	internal delegate List<string> ItchAttempt(string url, ref string why);

	/// <summary>
	/// A game of itch.io, the way the site does it: its uploads (scored by <paramref name="uploadsOf"/>), each one's link,
	/// and <paramref name="attempt"/> on the best ones. <paramref name="wanted"/> is what the messages call it.
	/// </summary>
	internal static List<string> FromItch(FoundGame g, Func<string, List<ItchUpload>> uploadsOf, ItchAttempt attempt, string wanted)
	{
		CookieContainer cookies = new CookieContainer();
		string gameUrl = g.Page.Split('?')[0].TrimEnd('/');
		string html = GetText(gameUrl, cookies);
		string csrf = Csrf(html);
		List<ItchUpload> uploads = uploadsOf(html);
		string key = Regex.Match(html, "init_GameDownload\\([^,]*,\\s*\\{[^<]*?\"key\":\"([^\"]+)\"").Groups[1].Value;
		bool onDownloadPage = uploads.Any(u => u.Id != null);
		if (!onDownloadPage)
		{
			if (html.Contains("buy_btn") || html.Contains("download_url"))
			{
				// "Name your own price" (or free with a download page): the site's own way to its download page.
				JObject answer = JObject.Parse(PostText(gameUrl + "/download_url", "csrf_token=" + Uri.EscapeDataString(csrf ?? ""), cookies));
				string page = (string)answer["url"];
				if (page == null)
				{
					throw new FinderException("It isn't free on itch.io: open its page.");
				}
				html = GetText(page, cookies);
				csrf = Csrf(html) ?? csrf;
				uploads = uploadsOf(html);
				string k = Regex.Match(html, "init_GameDownload\\([^,]*,\\s*\\{[^<]*?\"key\":\"([^\"]+)\"").Groups[1].Value;
				if (k.Length > 0)
				{
					key = k;
				}
			}
		}
		uploads = uploads.Where(u => u.Id != null).ToList();
		if (uploads.Count == 0)
		{
			throw new FinderException(html.Contains("game_frame") || html.Contains("iframe") ? "It's only played in the browser on itch.io: no ROM to download." : "It has nothing to download on itch.io.");
		}
		List<ItchUpload> tries = uploads.Where(u => u.Score > -8).OrderByDescending(u => u.Score).Take(4).ToList();
		if (tries.Count == 0)
		{
			throw new FinderException("Its downloads are for other systems (" + string.Join(", ", uploads.Select(u => u.Name).Take(3)) + ").");
		}
		string slug = new Uri(gameUrl).AbsolutePath.Trim('/');
		string why = null;
		foreach (ItchUpload u in tries)
		{
			g.Message = "Getting " + u.Name + "...";
			string query = "source=game_download&after_download_lightbox=1&as_props=1" + ((key.Length > 0) ? ("&key=" + Uri.EscapeDataString(key)) : "");
			JObject answer = JObject.Parse(PostText(gameUrl.Substring(0, gameUrl.Length - slug.Length) + slug + "/file/" + u.Id + "?" + query, "csrf_token=" + Uri.EscapeDataString(csrf ?? ""), cookies));
			string url = (string)answer["url"];
			if (url == null)
			{
				why = "itch.io didn't give the file (" + string.Join(", ", answer["errors"]?.Values<string>() ?? new[] { "no link" }) + ").";
				continue;
			}
			List<string> roms = attempt(url, ref why);
			if (roms != null)
			{
				return roms;
			}
		}
		throw new FinderException(why ?? ("None of its downloads is a " + wanted + "."));
	}

	/// <summary>The uploads on a game or download page, each scored by <paramref name="score"/> (its name in lower case, and whether it's marked for a computer).</summary>
	internal static List<ItchUpload> ScoredUploads(string html, Func<string, bool, int> score)
	{
		List<ItchUpload> list = Uploads(html, false);
		foreach (ItchUpload u in list)
		{
			u.Score = score(" " + u.Name.ToLowerInvariant() + " ", u.Platforms);
		}
		return list;
	}

	private static string Csrf(string html)
	{
		Match m = Regex.Match(html, "name=\"csrf_token\" value=\"([^\"]+)\"");
		return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
	}

	private static readonly string[] OtherSystems = { "windows", "win64", "win32", "mac", "osx", "linux", "android", "apk", "3ds", "cia", "nds", "switch", "psp", "vita", "source", "src", "ost", "soundtrack", "music", "manual", "pdf", "wallpaper", "artbook", "html", "web", " pc", "pc ", "exe", "pico-8", "p8" };

	/// <summary>What isn't the DS ROM (the GBA list without "nds", plus the GBA's own files).</summary>
	private static readonly string[] OtherThanDs = OtherSystems.Where(w => w != "nds").Concat(new[] { ".gba", "gba " }).ToArray();

	/// <summary>The uploads on a game or download page, scored by how much they look like the ROM (GBA, or DS with <paramref name="ds"/>).</summary>
	private static List<ItchUpload> Uploads(string html, bool ds)
	{
		List<ItchUpload> list = new List<ItchUpload>();
		foreach (Match m in Regex.Matches(html, "<div class=\"upload\">(.*?)(?=<div class=\"upload\">|</div></div></div>|$)", RegexOptions.Singleline))
		{
			string block = m.Groups[1].Value;
			string id = Regex.Match(block, "data-upload_id=\"(\\d+)\"").Groups[1].Value;
			string name = Html(Regex.Match(block, "class=\"name\"[^>]*>(.*?)</strong>", RegexOptions.Singleline).Groups[1].Value);
			if (name.Length == 0)
			{
				name = WebUtility.HtmlDecode(Regex.Match(block, "<strong title=\"([^\"]*)\"").Groups[1].Value);
			}
			bool platforms = Regex.IsMatch(block, "icon-(windows8|apple|tux|android)");
			string n = " " + name.ToLowerInvariant() + " ";
			int score = 0;
			if (ds)
			{
				if (n.Contains(".nds"))
				{
					score += 6;
				}
				if (Regex.IsMatch(n, "nds|nintendo ?ds|[^a-z0-9]ds[^a-z0-9]"))
				{
					score += 5;
				}
				if (n.Contains("rom"))
				{
					score += 3;
				}
				if (n.Contains(".zip"))
				{
					score += 1;
				}
				if (platforms)
				{
					score -= 10;
				}
				if (OtherThanDs.Any(w => n.Contains(w)))
				{
					score -= 8;
				}
				list.Add(new ItchUpload
				{
					Id = (id.Length > 0) ? id : null,
					Name = (name.Length > 0) ? name : "a download",
					SizeText = Html(Regex.Match(block, "class=\"file_size\"[^>]*>(.*?)</span></span>", RegexOptions.Singleline).Groups[1].Value),
					Platforms = platforms,
					Score = score
				});
				continue;
			}
			if (n.Contains(".gba"))
			{
				score += 6;
			}
			if (Regex.IsMatch(n, "gba|game ?boy ?advance|agb"))
			{
				score += 5;
			}
			if (n.Contains("rom"))
			{
				score += 3;
			}
			if (n.Contains(".zip"))
			{
				score += 1;
			}
			if (platforms)
			{
				score -= 10;
			}
			if (OtherSystems.Any(w => n.Contains(w)))
			{
				score -= 8;
			}
			if (Regex.IsMatch(n, "\\bgbc\\b|\\.gbc\\b|\\.gb\\b|game ?boy color|\\bdmg\\b") && !Regex.IsMatch(n, "gba|advance"))
			{
				score -= 6;
			}
			list.Add(new ItchUpload
			{
				Id = (id.Length > 0) ? id : null,
				Name = (name.Length > 0) ? name : "a download",
				SizeText = Html(Regex.Match(block, "class=\"file_size\"[^>]*>(.*?)</span></span>", RegexOptions.Singleline).Groups[1].Value),
				Platforms = platforms,
				Score = score
			});
		}
		return list;
	}

	/// <summary>
	/// Downloads one file and keeps the GBA ROMs in it (a .gba, or the .gba files in a zip, also in a zip inside it).
	/// Null (with why) when it had none.
	/// </summary>
	private static List<string> TryDownload(FoundGame g, string url, string name, CookieContainer cookies, ref string why)
	{
		string tmpDir = Path.Combine(Folder, "Downloading");
		Directory.CreateDirectory(tmpDir);
		string tmp = Path.Combine(tmpDir, Guid.NewGuid().ToString("N").Substring(0, 12));
		try
		{
			g.Message = "Downloading" + ((name != null) ? (" " + name) : "") + "...";
			string got = Download(url, tmp, cookies, g);
			name ??= got ?? Path.GetFileName(new Uri(url).AbsolutePath);
			string dest = GameFolder(g);
			List<string> roms = Unpack(tmp, name, dest, 0, out string problem);
			if (roms.Count > 0)
			{
				return OneLanguage(roms);
			}
			why = problem ?? (name + " has no " + SystemName(g) + " ROM.");
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
				File.Delete(tmp);
			}
			catch
			{
			}
		}
	}

	/// <summary>The ROMs (GBA or DS, told by their header) in a downloaded file, written to <paramref name="dest"/>.</summary>
	private static List<string> Unpack(string file, string name, string dest, int depth, out string problem)
	{
		problem = null;
		List<string> roms = new List<string>();
		byte[] head = new byte[0xC0];
		int read;
		using (FileStream s = File.OpenRead(file))
		{
			read = s.Read(head, 0, head.Length);
		}
		if (read >= 4 && head[0] == (byte)'P' && head[1] == (byte)'K')
		{
			using Ionic.Zip.ZipFile zip = Ionic.Zip.ZipFile.Read(file);
			List<Ionic.Zip.ZipEntry> entries = zip.Entries.Where(e => !e.IsDirectory && !e.FileName.Contains("__MACOSX")).ToList();
			foreach (Ionic.Zip.ZipEntry entry in entries.Where(e => e.FileName.EndsWith(".gba", StringComparison.OrdinalIgnoreCase) || e.FileName.EndsWith(".agb", StringComparison.OrdinalIgnoreCase) || e.FileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) || DsRom.IsDsFile(e.FileName)))
			{
				if (entry.UncompressedSize > (DsRom.IsDsFile(entry.FileName) ? (256L << 20) : (64L << 20)))
				{
					continue;
				}
				using MemoryStream ms = new MemoryStream();
				entry.Extract(ms);
				byte[] data = ms.ToArray();
				if (IsRom(data))
				{
					roms.Add(Save(dest, Path.GetFileName(entry.FileName), data));
				}
			}
			if (roms.Count == 0 && depth < 2)
			{
				// A zip inside the zip (or a .7z we can't open).
				foreach (Ionic.Zip.ZipEntry entry in entries.Where(e => e.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
				{
					string inner = file + "." + roms.Count + depth + ".zip";
					try
					{
						using (FileStream o = File.Create(inner))
						{
							entry.Extract(o);
						}
						roms.AddRange(Unpack(inner, entry.FileName, dest, depth + 1, out string p));
						problem ??= p;
					}
					finally
					{
						File.Delete(inner);
					}
				}
				if (roms.Count == 0 && entries.Any(e => Regex.IsMatch(e.FileName, "\\.(7z|rar)$", RegexOptions.IgnoreCase)))
				{
					problem = "Its ROM comes packed as .7z or .rar inside the zip, which can't be opened here.";
				}
			}
			return roms;
		}
		if (read >= 6 && head[0] == 0x37 && head[1] == 0x7A && head[2] == 0xBC && head[3] == 0xAF)
		{
			problem = "It comes as a .7z, which can't be opened here: open its page to get it by hand.";
			return roms;
		}
		if (read >= 4 && head[0] == (byte)'R' && head[1] == (byte)'a' && head[2] == (byte)'r' && head[3] == (byte)'!')
		{
			problem = "It comes as a .rar, which can't be opened here: open its page to get it by hand.";
			return roms;
		}
		if (new FileInfo(file).Length <= 256L << 20)
		{
			byte[] data = File.ReadAllBytes(file);
			if (IsRom(data))
			{
				string fileName = Path.GetFileName(name ?? "game");
				roms.Add(Save(dest, fileName, data));
			}
		}
		return roms;
	}

	private static readonly Regex English = new Regex("(^|[^a-z])(en|eng|english|usa|us|u|e)([^a-z]|$)", RegexOptions.IgnoreCase);

	/// <summary>
	/// The same game in several languages (game-en.gba, game-es.gba...): only the English one is kept, so the library
	/// gets one cartridge. Different games or versions all stay.
	/// </summary>
	private static List<string> OneLanguage(List<string> roms)
	{
		if (roms.Count < 2)
		{
			return roms;
		}
		List<string> english = roms.Where(r => English.IsMatch(Path.GetFileNameWithoutExtension(r))).ToList();
		bool languages = roms.Count(r => Regex.IsMatch(Path.GetFileNameWithoutExtension(r), "(^|[^a-z])(en|es|fr|de|it|ja|jp|pt|zh|ar|ru|ko|nl|pl)([^a-z]|$)", RegexOptions.IgnoreCase)) >= 2;
		if (english.Count != 1 || !languages)
		{
			return roms;
		}
		foreach (string r in roms.Where(r => r != english[0]))
		{
			try
			{
				File.Delete(r);
			}
			catch
			{
			}
		}
		return english;
	}

	/// <summary>A Game Boy Advance cartridge header (its fixed byte, and the header checksum), or a DS one.</summary>
	private static bool IsRom(byte[] data)
	{
		if (DsRom.IsDs(data))
		{
			return true;
		}
		if (data == null || data.Length < 0x200 || data.Length > 64 << 20 || data[0xB2] != 0x96)
		{
			return false;
		}
		int sum = 0;
		for (int i = 0xA0; i <= 0xBC; i++)
		{
			sum -= data[i];
		}
		// Homebrew doesn't always fix the checksum: the fixed byte and a plausible entry jump are enough then.
		return (byte)(sum - 0x19) == data[0xBD] || data[3] == 0xEA;
	}

	/// <summary>The name a ROM is kept under (.nds for the DS's, .gba for the rest).</summary>
	private static string SavedName(string fileName, bool ds = false)
	{
		string baseName = Safe(Path.GetFileNameWithoutExtension(fileName));
		return ((baseName.Length == 0) ? "game" : baseName) + (ds ? ".nds" : ".gba");
	}

	private static string Save(string dest, string fileName, byte[] data)
	{
		Directory.CreateDirectory(dest);
		string path = Path.Combine(dest, SavedName(fileName, DsRom.IsDs(data)));
		File.WriteAllBytes(path, data);
		return path;
	}

	#endregion

	#region Web

	private static HttpWebRequest Request(string url, CookieContainer cookies)
	{
		HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
		r.UserAgent = UserAgent;
		r.Timeout = 30000;
		r.ReadWriteTimeout = 60000;
		r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		r.AllowAutoRedirect = true;
		r.CookieContainer = cookies;
		if (url.Contains("api.github.com"))
		{
			r.Accept = "application/vnd.github+json";
		}
		return r;
	}

	/// <summary>A page as text. "Too many requests" and server hiccups (429, 5xx) are tried again after a wait.</summary>
	internal static string GetText(string url, CookieContainer cookies)
	{
		for (int attempt = 0; ; attempt++)
		{
			try
			{
				HttpWebRequest r = Request(url, cookies);
				using HttpWebResponse response = (HttpWebResponse)r.GetResponse();
				using StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
				return reader.ReadToEnd();
			}
			catch (WebException ex) when (attempt < 3 && Retryable(ex, out int wait))
			{
				Thread.Sleep(Math.Max(wait, 1500 * (attempt + 1)));
			}
		}
	}

	private static bool Retryable(WebException ex, out int waitMs)
	{
		waitMs = 0;
		if (ex.Response is HttpWebResponse r)
		{
			int code = (int)r.StatusCode;
			if (int.TryParse(r.Headers["Retry-After"], out int seconds))
			{
				waitMs = Math.Min(10, seconds) * 1000;
			}
			return code == 429 || code >= 500;
		}
		return ex.Status == WebExceptionStatus.Timeout || ex.Status == WebExceptionStatus.ConnectFailure || ex.Status == WebExceptionStatus.ReceiveFailure;
	}

	private static string PostText(string url, string form, CookieContainer cookies, string contentType = "application/x-www-form-urlencoded")
	{
		HttpWebRequest r = Request(url, cookies);
		r.Method = "POST";
		r.ContentType = contentType;
		r.Headers["X-Requested-With"] = "XMLHttpRequest";
		byte[] body = Encoding.UTF8.GetBytes(form);
		r.ContentLength = body.Length;
		using (Stream s = r.GetRequestStream())
		{
			s.Write(body, 0, body.Length);
		}
		try
		{
			using HttpWebResponse response = (HttpWebResponse)r.GetResponse();
			using StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
			return reader.ReadToEnd();
		}
		catch (WebException ex) when (ex.Response != null)
		{
			// Errors come as JSON too ({"errors": [...]}).
			using StreamReader reader = new StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8);
			string text = reader.ReadToEnd();
			if (text.TrimStart().StartsWith("{"))
			{
				return text;
			}
			throw;
		}
	}

	private static byte[] GetBytes(string url, CookieContainer cookies, long max, FoundGame g)
	{
		using MemoryStream ms = new MemoryStream();
		HttpWebRequest r = Request(url, cookies);
		using HttpWebResponse response = (HttpWebResponse)r.GetResponse();
		Copy(response, ms, max, g);
		return ms.ToArray();
	}

	/// <summary>Downloads to a file; returns the file name the server gave (or null).</summary>
	internal static string Download(string url, string path, CookieContainer cookies, FoundGame g)
	{
		HttpWebRequest r = Request(url, cookies);
		using HttpWebResponse response = (HttpWebResponse)r.GetResponse();
		string name = null;
		string disposition = response.Headers["Content-Disposition"];
		if (disposition != null)
		{
			Match m = Regex.Match(disposition, "filename\\*=UTF-8''([^;]+)", RegexOptions.IgnoreCase);
			name = m.Success ? Uri.UnescapeDataString(m.Groups[1].Value) : Regex.Match(disposition, "filename=\"?([^\";]+)\"?", RegexOptions.IgnoreCase).Groups[1].Value;
		}
		if (string.IsNullOrEmpty(name))
		{
			name = Path.GetFileName(Uri.UnescapeDataString(response.ResponseUri.AbsolutePath));
		}
		using (FileStream file = File.Create(path))
		{
			Copy(response, file, MaxDownload, g);
		}
		return string.IsNullOrEmpty(name) ? null : name;
	}

	private static void Copy(HttpWebResponse response, Stream to, long max, FoundGame g)
	{
		long length = response.ContentLength;
		if (length > max)
		{
			throw new FinderException("It's too big (" + (length >> 20) + " MB).");
		}
		if (g != null)
		{
			g.Size = Math.Max(0, length);
			g.Got = 0;
		}
		using Stream from = response.GetResponseStream();
		byte[] buffer = new byte[81920];
		long total = 0;
		int n;
		while ((n = from.Read(buffer, 0, buffer.Length)) > 0)
		{
			to.Write(buffer, 0, n);
			total += n;
			if (total > max)
			{
				throw new FinderException("It's too big.");
			}
			if (g != null)
			{
				Interlocked.Exchange(ref g.Got, total);
			}
		}
	}

	#endregion

	#region Text

	internal static string Html(string s)
	{
		s = Regex.Replace(s ?? "", "<[^>]+>", " ");
		return Regex.Replace(WebUtility.HtmlDecode(s), "\\s+", " ").Trim();
	}

	private static string Plain(string s)
	{
		return new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
	}

	internal static string FirstSentence(string s)
	{
		s = Regex.Replace(s ?? "", "\\s+", " ").Trim();
		int dot = s.IndexOf(". ", StringComparison.Ordinal);
		if (dot > 20 && dot < 160)
		{
			return s.Substring(0, dot + 1);
		}
		return (s.Length > 160) ? (s.Substring(0, 157) + "...") : s;
	}

	/// <summary>A text field that may come as a list (its first text) or something else (null).</summary>
	internal static string Str(JToken t)
	{
		if (t is JArray a)
		{
			return a.Select(Str).FirstOrDefault(x => !string.IsNullOrEmpty(x));
		}
		return (t is JValue v && v.Value != null) ? v.Value.ToString() : null;
	}

	/// <summary>Who made it: up to three names (the field is a list of people or a plain text).</summary>
	private static string Developers(JToken t)
	{
		if (t is JArray a)
		{
			return string.Join(", ", a.Select(d => (d is JObject o) ? Str(o["name"]) : Str(d)).Where(n => !string.IsNullOrEmpty(n)).Take(3));
		}
		return Str(t) ?? "";
	}

	/// <summary>"owner/repo" from a github.com link, or null.</summary>
	private static string GitHubRepo(string url)
	{
		Match m = Regex.Match(url ?? "", "github\\.com/([^/\\s]+)/([^/\\s#?]+)", RegexOptions.IgnoreCase);
		return m.Success ? (m.Groups[1].Value + "/" + Regex.Replace(m.Groups[2].Value, "\\.git$", "")) : null;
	}

	private static string Safe(string s)
	{
		StringBuilder b = new StringBuilder();
		foreach (char c in s ?? "")
		{
			b.Append((char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.') ? c : '_');
		}
		string r = b.ToString().Trim('.', ' ');
		return (r.Length > 60) ? r.Substring(0, 60) : r;
	}

	private static string Hash(string s)
	{
		using System.Security.Cryptography.SHA1 sha = System.Security.Cryptography.SHA1.Create();
		return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(s)).Take(10).Select(b => b.ToString("x2")));
	}

	#endregion
}
