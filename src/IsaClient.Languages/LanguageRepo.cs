using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using IsaClient.Core;
using Newtonsoft.Json;
using Terraria;

namespace IsaClient.Languages;

/// <summary>
/// IsaClient's languages live in a public repository (github.com/magaflaca/isa-l10n), not inside the client:
/// index.json lists every language with how much of IsaClient it translates, and a language is downloaded the first
/// time it is used (picked in the language window, or the game's own language). Downloaded languages are kept in
/// IsaClient/Languages/Downloaded and brought up to date by themselves when the repository changes; without internet
/// the ones already there keep working. Every file is checked against the index (SHA-256) before it is used.
/// </summary>
public static class LanguageRepo
{
	public const string RepoUrl = "https://github.com/magaflaca/isa-l10n";

	/// <summary>
	/// Where the files are read from: GitHub, then a CDN copy of the same repository. The environment variable
	/// ISA_L10N_URL puts another place first (a mirror, or a local folder as file:///... for testing).
	/// </summary>
	private static readonly string[] Bases = BuildBases();

	private static string[] BuildBases()
	{
		List<string> bases = new List<string>();
		string custom = Environment.GetEnvironmentVariable("ISA_L10N_URL");
		if (!string.IsNullOrWhiteSpace(custom))
		{
			bases.Add(custom.TrimEnd('/') + "/");
		}
		bases.Add("https://raw.githubusercontent.com/magaflaca/isa-l10n/main/");
		bases.Add("https://cdn.jsdelivr.net/gh/magaflaca/isa-l10n@main/");
		return bases.ToArray();
	}

	/// <summary>A language as index.json lists it.</summary>
	public sealed class Entry
	{
		public string Code;

		public string Name;

		public string EnglishName;

		public string Flag;

		public string Author;

		/// <summary>Its file in the repository ("packs/es.json").</summary>
		public string File;

		public string Sha256;

		public long Bytes;

		public int Translated;

		public int Total;

		/// <summary>0..100, of the texts of the newest IsaClient.</summary>
		public float Percent;

		public string Updated;
	}

	private sealed class IndexFile
	{
		public int Format;

		public string Updated;

		public List<Entry> Languages;
	}

	/// <summary>A download going on (or the last one, when it failed).</summary>
	private sealed class Job
	{
		public long Done;

		public long Total;

		public bool Running;

		public string Error;

		public DateTime FailedAt;
	}

	private static readonly object _sync = new object();

	private static List<Entry> _index;

	private static readonly Dictionary<string, Job> _jobs = new Dictionary<string, Job>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, string> _localSha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private static bool _started;

	private static volatile bool _checking;

	/// <summary>When the index was last read from the repository (UTC), or null.</summary>
	public static DateTime? CheckedAt { get; private set; }

	/// <summary>Why the last check failed (no internet...), or null.</summary>
	public static string CheckError { get; private set; }

	public static bool Checking => _checking;

	/// <summary>Said when a download finishes or fails (the language window shows it).</summary>
	public static event Action<string> Notice;

	/// <summary>IsaClient/Languages/Downloaded.</summary>
	public static string Folder => Path.Combine(L10n.Folder, "Downloaded");

	private static string IndexPath => Path.Combine(Folder, "index.json");

	/// <summary>The languages the repository has (the last index read, kept on disk for when there is no internet).</summary>
	public static List<Entry> Index
	{
		get
		{
			lock (_sync)
			{
				if (_index == null)
				{
					_index = ReadIndex(IndexPath) ?? new List<Entry>();
				}
				return _index;
			}
		}
	}

	public static Entry EntryFor(string code)
	{
		return string.IsNullOrEmpty(code) ? null : Index.FirstOrDefault(e => string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase));
	}

	public static string PathOf(string code)
	{
		return Path.Combine(Folder, Safe(code) + ".json");
	}

	public static bool IsDownloaded(string code)
	{
		return File.Exists(PathOf(code));
	}

	/// <summary>Downloaded, but the repository has a newer version.</summary>
	public static bool NeedsUpdate(string code)
	{
		Entry entry = EntryFor(code);
		return entry != null && !string.IsNullOrEmpty(entry.Sha256) && IsDownloaded(code) && !string.Equals(LocalSha(code), entry.Sha256, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>0..1 while it downloads, else null.</summary>
	public static float? Progress(string code)
	{
		lock (_sync)
		{
			if (!_jobs.TryGetValue(code, out Job job) || !job.Running)
			{
				return null;
			}
			return (job.Total > 0) ? Math.Min(1f, (float)job.Done / job.Total) : 0f;
		}
	}

	/// <summary>Why its last download failed, or null.</summary>
	public static string Failure(string code)
	{
		lock (_sync)
		{
			return (_jobs.TryGetValue(code, out Job job) && !job.Running) ? job.Error : null;
		}
	}

	/// <summary>Once, when IsaClient first needs a language: the index is read again in the background.</summary>
	public static void Start()
	{
		if (_started)
		{
			return;
		}
		_started = true;
		Refresh();
	}

	/// <summary>Reads index.json from the repository; then brings downloaded languages up to date and gets the one in use.</summary>
	public static void Refresh()
	{
		if (_checking)
		{
			return;
		}
		_checking = true;
		Task.Run(delegate
		{
			try
			{
				byte[] data = Fetch("index.json", null);
				List<Entry> index = Parse(Encoding.UTF8.GetString(data));
				if (index == null)
				{
					throw new InvalidDataException("index.json has no languages");
				}
				Directory.CreateDirectory(Folder);
				File.WriteAllBytes(IndexPath, data);
				lock (_sync)
				{
					_index = index;
				}
				CheckedAt = DateTime.UtcNow;
				CheckError = null;
				Main.QueueMainThreadAction(delegate
				{
					L10n.Reload();
					// Downloaded languages follow the repository; the one in use comes now if it isn't here yet.
					foreach (Entry entry in index)
					{
						if (NeedsUpdate(entry.Code))
						{
							Download(entry.Code);
						}
					}
					string wanted = L10n.WantedCode;
					if (wanted != null && !IsDownloaded(wanted) && EntryFor(wanted) != null)
					{
						Download(wanted);
					}
				});
			}
			catch (Exception ex)
			{
				CheckError = Short(ex);
			}
			finally
			{
				_checking = false;
			}
		});
	}

	/// <summary>The language is needed (picked, or the game's): downloaded if it isn't yet (not again right after a failure).</summary>
	public static void Want(string code)
	{
		Start();
		if (string.IsNullOrEmpty(code) || IsDownloaded(code) || EntryFor(code) == null)
		{
			return;
		}
		lock (_sync)
		{
			if (_jobs.TryGetValue(code, out Job job) && (job.Running || DateTime.UtcNow - job.FailedAt < TimeSpan.FromSeconds(60)))
			{
				return;
			}
		}
		Download(code);
	}

	/// <summary>Downloads a language (checked against the index), keeps it, and uses it if it is the one in use.</summary>
	public static void Download(string code)
	{
		Entry entry = EntryFor(code);
		if (entry == null)
		{
			return;
		}
		Job job;
		lock (_sync)
		{
			if (_jobs.TryGetValue(code, out job) && job.Running)
			{
				return;
			}
			job = new Job { Running = true, Total = entry.Bytes };
			_jobs[code] = job;
		}
		bool update = IsDownloaded(code);
		Task.Run(delegate
		{
			try
			{
				string repoFile = string.IsNullOrEmpty(entry.File) ? ("packs/" + entry.Code + ".json") : entry.File;
				byte[] data = Fetch(repoFile, job, entry.Sha256);
				// It must be a language file IsaClient can read before it replaces anything.
				L10n.Pack pack = L10n.Parse(Encoding.UTF8.GetString(data));
				if (pack == null || string.IsNullOrWhiteSpace(pack.Code))
				{
					throw new InvalidDataException("not a language file");
				}
				Directory.CreateDirectory(Folder);
				string target = PathOf(code);
				string temp = Path.ChangeExtension(target, ".part");
				File.WriteAllBytes(temp, data);
				if (File.Exists(target))
				{
					File.Delete(target);
				}
				File.Move(temp, target);
				lock (_sync)
				{
					_localSha[code] = Sha(data);
					job.Running = false;
					job.Error = null;
				}
				Main.QueueMainThreadAction(delegate
				{
					L10n.Reload();
					Notice?.Invoke(L10n.F(update ? "{0} updated" : "{0} is ready", entry.Name));
				});
			}
			catch (Exception ex)
			{
				lock (_sync)
				{
					job.Running = false;
					job.Error = Short(ex);
					job.FailedAt = DateTime.UtcNow;
				}
				Main.QueueMainThreadAction(delegate
				{
					Notice?.Invoke(L10n.F("{0} could not be downloaded: {1}", entry.Name, job.Error));
				});
			}
		});
	}

	/// <summary>Takes a downloaded language away (it can be downloaded again).</summary>
	public static void Remove(string code)
	{
		try
		{
			string path = PathOf(code);
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			lock (_sync)
			{
				_localSha.Remove(code);
				_jobs.Remove(code);
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		L10n.Reload();
	}

	#region Files

	/// <summary>A file of the repository: from GitHub, else the CDN; checked against <paramref name="sha256"/> when given.</summary>
	private static byte[] Fetch(string file, Job job, string sha256 = null)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		Exception last = null;
		foreach (string root in Bases)
		{
			try
			{
				byte[] data = Get(root + file + (root.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? ("?t=" + DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute) : ""), job);
				if (!string.IsNullOrEmpty(sha256) && !string.Equals(Sha(data), sha256, StringComparison.OrdinalIgnoreCase))
				{
					// A copy that isn't up to date yet (the CDN keeps files a while): the next place, or later.
					throw new InvalidDataException("the file doesn't match the index yet");
				}
				return data;
			}
			catch (Exception ex)
			{
				last = ex;
			}
		}
		throw last ?? new IOException("no source");
	}

	private static byte[] Get(string url, Job job)
	{
		WebRequest request = WebRequest.Create(url);
		request.Timeout = 15000;
		if (request is HttpWebRequest http)
		{
			http.UserAgent = "IsaClient/" + ClientInfo.Version;
			http.ReadWriteTimeout = 30000;
			http.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		}
		using WebResponse response = request.GetResponse();
		using Stream stream = response.GetResponseStream();
		using MemoryStream memory = new MemoryStream();
		if (job != null && response.ContentLength > 0 && job.Total <= 0)
		{
			job.Total = response.ContentLength;
		}
		byte[] buffer = new byte[16384];
		int read;
		while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
		{
			memory.Write(buffer, 0, read);
			if (job != null)
			{
				job.Done = memory.Length;
			}
		}
		return memory.ToArray();
	}

	private static List<Entry> Parse(string json)
	{
		IndexFile index = JsonConvert.DeserializeObject<IndexFile>(json);
		return index?.Languages?.Where(e => e != null && !string.IsNullOrWhiteSpace(e.Code)).ToList();
	}

	private static List<Entry> ReadIndex(string path)
	{
		try
		{
			return File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : null;
		}
		catch
		{
			return null;
		}
	}

	private static string LocalSha(string code)
	{
		lock (_sync)
		{
			if (_localSha.TryGetValue(code, out string known))
			{
				return known;
			}
		}
		try
		{
			string sha = Sha(File.ReadAllBytes(PathOf(code)));
			lock (_sync)
			{
				_localSha[code] = sha;
			}
			return sha;
		}
		catch
		{
			return null;
		}
	}

	private static string Sha(byte[] data)
	{
		using SHA256 sha = SHA256.Create();
		return string.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2")));
	}

	private static string Safe(string code)
	{
		return new string((code ?? "").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
	}

	private static string Short(Exception ex)
	{
		if (ex is WebException web && web.Response is HttpWebResponse http)
		{
			return "HTTP " + (int)http.StatusCode;
		}
		string message = ex.InnerException?.Message ?? ex.Message;
		return (message.Length > 80) ? (message.Substring(0, 80) + "...") : message;
	}

	#endregion
}
