using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using IsaClient.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IsaClient.Networking;

/// <summary>
/// The free proxies, found and kept fresh in the background from the moment the client starts: the public lists are
/// downloaded, every proxy is put through the real tunnel test (<see cref="ProxyTunnel.Test"/>) twice, the good ones are
/// located (country via ip-api.com) and tested again every few minutes. Results are kept in IsaClient/Proxy/Pool.json,
/// so the next start already knows the good ones. Nothing is shown in the chat.
/// </summary>
public static class ProxyPool
{
	/// <summary>A public list and what it gave this session.</summary>
	public sealed class Source
	{
		public string Name;

		public string Url;

		public ProxyProtocol Protocol;

		/// <summary>Lists like proxifly's mix protocols ("socks4://ip:port"): the scheme of each line decides.</summary>
		public bool Mixed;

		public bool Enabled = true;

		public int Downloaded;

		public string Error;
	}

	/// <summary>Measured on 2026-10-27 with the real tunnel test: best yield first. The huge lists (proxifly, hookzof...) are
	/// mostly 45.74.* proxies that answer but drop the game, so they come last and without those.</summary>
	public static readonly List<Source> Sources = new List<Source>
	{
		new Source { Name = "hideip.me (zloi-user)", Protocol = ProxyProtocol.Socks5, Url = "https://raw.githubusercontent.com/zloi-user/hideip.me/main/socks5.txt" },
		new Source { Name = "ProxyScrape SOCKS4", Protocol = ProxyProtocol.Socks4, Url = "https://api.proxyscrape.com/v4/free-proxy-list/get?request=display_proxies&protocol=socks4&proxy_format=ipport&format=text" },
		new Source { Name = "ProxyScrape SOCKS5", Protocol = ProxyProtocol.Socks5, Url = "https://api.proxyscrape.com/v4/free-proxy-list/get?request=display_proxies&protocol=socks5&proxy_format=ipport&format=text" },
		new Source { Name = "monosans HTTP", Protocol = ProxyProtocol.Http, Url = "https://raw.githubusercontent.com/monosans/proxy-list/main/proxies/http.txt" },
		new Source { Name = "jetkai SOCKS5", Protocol = ProxyProtocol.Socks5, Url = "https://raw.githubusercontent.com/jetkai/proxy-list/main/online-proxies/txt/proxies-socks5.txt" },
		new Source { Name = "monosans SOCKS4", Protocol = ProxyProtocol.Socks4, Url = "https://raw.githubusercontent.com/monosans/proxy-list/main/proxies/socks4.txt" },
		new Source { Name = "monosans SOCKS5", Protocol = ProxyProtocol.Socks5, Url = "https://raw.githubusercontent.com/monosans/proxy-list/main/proxies/socks5.txt" },
		new Source { Name = "ProxyScrape HTTP", Protocol = ProxyProtocol.Http, Url = "https://api.proxyscrape.com/v4/free-proxy-list/get?request=display_proxies&protocol=http&proxy_format=ipport&format=text" },
		new Source { Name = "TheSpeedX SOCKS4", Protocol = ProxyProtocol.Socks4, Url = "https://raw.githubusercontent.com/TheSpeedX/SOCKS-List/master/socks4.txt" },
		new Source { Name = "TheSpeedX SOCKS5", Protocol = ProxyProtocol.Socks5, Url = "https://raw.githubusercontent.com/TheSpeedX/SOCKS-List/master/socks5.txt" },
		new Source { Name = "TheSpeedX HTTP", Protocol = ProxyProtocol.Http, Url = "https://raw.githubusercontent.com/TheSpeedX/SOCKS-List/master/http.txt" },
		new Source { Name = "dpangestuw SOCKS5", Protocol = ProxyProtocol.Socks5, Url = "https://raw.githubusercontent.com/dpangestuw/Free-Proxy/main/socks5_proxies.txt" },
		new Source { Name = "r00tee SOCKS5", Protocol = ProxyProtocol.Socks5, Url = "https://raw.githubusercontent.com/r00tee/Proxy-List/main/Socks5.txt" },
		new Source { Name = "proxifly (all)", Protocol = ProxyProtocol.Socks5, Mixed = true, Url = "https://raw.githubusercontent.com/proxifly/free-proxy-list/main/proxies/all/data.txt" },
		new Source { Name = "hookzof SOCKS5", Protocol = ProxyProtocol.Socks5, Url = "https://raw.githubusercontent.com/hookzof/socks5_list/master/proxy.txt" }
	};

	/// <summary>Address starts that are never used: 45.74.* answer every test now and then but drop Terraria.</summary>
	public static List<string> Blocked = new List<string> { "45.74." };

	/// <summary>Enough good proxies: at this many, no new candidates are tested (the good ones still are, to keep them
	/// fresh); when some drop out, the search goes on.</summary>
	public const int Target = 250;

	private const int Workers = 48;

	private static readonly object _lock = new object();

	private static readonly Dictionary<string, ProxyServer> _all = new Dictionary<string, ProxyServer>();

	/// <summary>Candidates never seen working.</summary>
	private static readonly ConcurrentQueue<ProxyServer> _queue = new ConcurrentQueue<ProxyServer>();

	/// <summary>Proxies that worked before, tested again first (always, even with <see cref="Target"/> reached).</summary>
	private static readonly ConcurrentQueue<ProxyServer> _retest = new ConcurrentQueue<ProxyServer>();

	private static readonly HashSet<string> _queued = new HashSet<string>();

	private static readonly ConcurrentQueue<ProxyServer> _toLocate = new ConcurrentQueue<ProxyServer>();

	private static bool _started;

	private static volatile bool _refreshWanted;

	private static volatile int _running;

	private static int _testsDone;

	private static bool _dirty;

	private static DateTime _lastSave = DateTime.Now;

	/// <summary>When the lists were last downloaded (MinValue = not yet).</summary>
	public static DateTime ListsUpdated { get; private set; } = DateTime.MinValue;

	/// <summary>What the pool is doing, for the window ("Downloading lists 3/15", "Testing proxies"...); null = resting.</summary>
	public static string Activity { get; private set; }

	/// <summary>False when the test server cannot be reached from here: tests would say every proxy is dead.</summary>
	public static bool CanTest { get; private set; } = true;

	public static bool Paused;

	/// <summary>Set every tick by the main thread: while playing online the tests go slowly.</summary>
	public static volatile bool PlayingOnline;

	private static string Folder => Path.Combine(ClientSettings.IsaClientFolder, "Proxy");

	private static string CachePath => Path.Combine(Folder, "Pool.json");

	#region Start

	/// <summary>Starts the background work once (the client calls it at start).</summary>
	public static void Start()
	{
		if (_started)
		{
			return;
		}
		_started = true;
		Proxy.EnsureLoaded();
		Thread loop = new Thread(Loop, 256 * 1024)
		{
			IsBackground = true,
			Name = "IsaProxy pool",
			Priority = ThreadPriority.BelowNormal
		};
		loop.Start();
		for (int i = 0; i < Workers; i++)
		{
			int index = i;
			new Thread(() => Work(index), 128 * 1024)
			{
				IsBackground = true,
				Name = "IsaProxy test " + i,
				Priority = ThreadPriority.BelowNormal
			}.Start();
		}
		new Thread(Locate, 128 * 1024)
		{
			IsBackground = true,
			Name = "IsaProxy locate",
			Priority = ThreadPriority.BelowNormal
		}.Start();
	}

	/// <summary>Downloads the lists again and tests everything new (the Refresh button).</summary>
	public static void Refresh()
	{
		Start();
		_refreshWanted = true;
	}

	/// <summary>Tests the good ones again right now.</summary>
	public static void RetestWorking()
	{
		lock (_lock)
		{
			foreach (ProxyServer server in _all.Values.Where(s => s.Working))
			{
				Enqueue(server);
			}
		}
	}

	#endregion

	#region Background

	private static void Loop()
	{
		try
		{
			LoadCache();
			CanTest = ProxyTunnel.TestServerReachable();
			// What worked last time is tested first, so the list is right within seconds.
			lock (_lock)
			{
				foreach (ProxyServer server in _all.Values.Where(s => s.Successes > 0).OrderBy(s => s.Score))
				{
					Enqueue(server);
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogOnce(ex);
		}
		DateTime lastRetest = DateTime.Now;
		while (true)
		{
			try
			{
				if (_refreshWanted || ListsUpdated == DateTime.MinValue || (DateTime.Now - ListsUpdated).TotalMinutes > 45)
				{
					_refreshWanted = false;
					if (!CanTest)
					{
						CanTest = ProxyTunnel.TestServerReachable();
					}
					Download();
				}
				if ((DateTime.Now - lastRetest).TotalMinutes >= 6)
				{
					lastRetest = DateTime.Now;
					lock (_lock)
					{
						foreach (ProxyServer server in _all.Values.Where(s => s.Working && (DateTime.Now - s.LastTest).TotalMinutes >= 5))
						{
							Enqueue(server);
						}
					}
				}
				Activity = (_retest.Count > 0 || _running > 0 || (_queue.Count > 0 && !Enough)) ? "Testing proxies" : null;
				if (_dirty && (DateTime.Now - _lastSave).TotalSeconds > 20)
				{
					SaveCache();
				}
			}
			catch (Exception ex)
			{
				ClientLog.LogOnce(ex);
			}
			Thread.Sleep(1000);
		}
	}

	/// <summary>How many tests may run at once: fewer while you play online, none while paused.</summary>
	private static int Allowed
	{
		get
		{
			if (Paused || !CanTest)
			{
				return 0;
			}
			if (PlayingOnline)
			{
				return 6;
			}
			return Workers;
		}
	}

	private static void Work(int index)
	{
		while (true)
		{
			try
			{
				ProxyServer server = null;
				if (index >= Allowed || (!_retest.TryDequeue(out server) && (Enough || !_queue.TryDequeue(out server))))
				{
					Thread.Sleep(400);
					continue;
				}
				Interlocked.Increment(ref _running);
				try
				{
					Test(server);
				}
				finally
				{
					Interlocked.Decrement(ref _running);
				}
			}
			catch (Exception ex)
			{
				ClientLog.LogOnce(ex);
				Thread.Sleep(1000);
			}
		}
	}

	private static void Test(ProxyServer server)
	{
		lock (_lock)
		{
			_queued.Remove(server.Key);
		}
		int ms = ProxyTunnel.Test(server);
		bool wasWorking = server.Working;
		lock (_lock)
		{
			server.CountTest(ms > 0, ms);
			_dirty = true;
			_testsDone++;
		}
		// One good test is not enough (many pass once and then drop): a second one right away.
		if (ms > 0 && server.Successes == 1)
		{
			int again = ProxyTunnel.Test(server);
			lock (_lock)
			{
				server.CountTest(again > 0, again);
			}
		}
		if (server.Working && !wasWorking && server.Country.Length == 0)
		{
			_toLocate.Enqueue(server);
		}
	}

	private static void Enqueue(ProxyServer server)
	{
		if (!_queued.Add(server.Key))
		{
			return;
		}
		((server.Successes > 0) ? _retest : _queue).Enqueue(server);
	}

	private static readonly Regex _line = new Regex("(?:([A-Za-z0-9]+)://)?(\\d{1,3}\\.\\d{1,3}\\.\\d{1,3}\\.\\d{1,3}):(\\d{2,5})");

	private static void Download()
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		List<Source> sources = Sources.Where(s => s.Enabled).ToList();
		int added = 0;
		for (int i = 0; i < sources.Count; i++)
		{
			Source source = sources[i];
			Activity = "Downloading lists " + (i + 1) + "/" + sources.Count;
			string text;
			try
			{
				text = DownloadText(source.Url, 25000);
				source.Error = null;
			}
			catch (Exception ex)
			{
				source.Error = ex.Message;
				continue;
			}
			int count = 0;
			List<ProxyServer> fresh = new List<ProxyServer>();
			foreach (Match m in _line.Matches(text))
			{
				ProxyProtocol protocol = source.Protocol;
				if (source.Mixed)
				{
					string scheme = m.Groups[1].Value.ToLowerInvariant();
					if (scheme.StartsWith("socks5"))
					{
						protocol = ProxyProtocol.Socks5;
					}
					else if (scheme.StartsWith("socks4"))
					{
						protocol = ProxyProtocol.Socks4;
					}
					else if (scheme == "http" || scheme == "https")
					{
						protocol = ProxyProtocol.Http;
					}
					else
					{
						continue;
					}
				}
				string host = m.Groups[2].Value;
				if (IsBlocked(host) || !int.TryParse(m.Groups[3].Value, out int port) || port <= 0 || port > 65535)
				{
					continue;
				}
				count++;
				ProxyServer server = new ProxyServer { Host = host, Port = port, Protocol = protocol, Source = source.Name };
				lock (_lock)
				{
					if (_all.TryGetValue(server.Key, out ProxyServer known))
					{
						if (!known.Tested || (known.Strikes > 0 && (DateTime.Now - known.LastTest).TotalMinutes > 30))
						{
							fresh.Add(known);
						}
						continue;
					}
					_all[server.Key] = server;
					fresh.Add(server);
					added++;
				}
			}
			source.Downloaded = count;
			lock (_lock)
			{
				foreach (ProxyServer server in fresh)
				{
					Enqueue(server);
				}
			}
		}
		ListsUpdated = DateTime.Now;
		Activity = null;
		lock (_lock)
		{
			// Never-working proxies that are not in any list anymore are forgotten.
			if (_all.Count > 60000)
			{
				foreach (string key in _all.Where(p => p.Value.Successes == 0 && p.Value.Tested).Select(p => p.Key).ToList())
				{
					_all.Remove(key);
				}
			}
		}
	}

	private static string DownloadText(string url, int timeoutMs)
	{
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
		request.Timeout = timeoutMs;
		request.ReadWriteTimeout = timeoutMs;
		request.UserAgent = ClientInfo.Name + "/" + ClientInfo.FullVersion;
		request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		using WebResponse response = request.GetResponse();
		using StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
		return reader.ReadToEnd();
	}

	/// <summary>Countries of the good proxies, 100 at a time (ip-api.com allows 15 of those a minute).</summary>
	private static void Locate()
	{
		while (true)
		{
			try
			{
				List<ProxyServer> batch = new List<ProxyServer>();
				while (batch.Count < 100 && _toLocate.TryDequeue(out ProxyServer server))
				{
					if (server.Country.Length == 0)
					{
						batch.Add(server);
					}
				}
				if (batch.Count == 0)
				{
					Thread.Sleep(1500);
					continue;
				}
				JArray ips = new JArray(batch.Select(s => s.Host).Distinct());
				HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://ip-api.com/batch?fields=status,countryCode,country,city,query");
				request.Method = "POST";
				request.ContentType = "application/json";
				request.Timeout = 15000;
				request.UserAgent = ClientInfo.Name + "/" + ClientInfo.FullVersion;
				byte[] body = Encoding.UTF8.GetBytes(ips.ToString(Formatting.None));
				using (Stream stream = request.GetRequestStream())
				{
					stream.Write(body, 0, body.Length);
				}
				string json;
				using (WebResponse response = request.GetResponse())
				using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
				{
					json = reader.ReadToEnd();
				}
				Dictionary<string, JObject> byIp = new Dictionary<string, JObject>();
				foreach (JObject item in JArray.Parse(json).OfType<JObject>())
				{
					if ((string)item["status"] == "success")
					{
						byIp[(string)item["query"]] = item;
					}
				}
				lock (_lock)
				{
					foreach (ProxyServer server in batch)
					{
						if (byIp.TryGetValue(server.Host, out JObject info))
						{
							server.Country = ((string)info["countryCode"] ?? "").ToLowerInvariant();
							server.CountryName = (string)info["country"] ?? "";
							server.City = (string)info["city"] ?? "";
						}
						else
						{
							server.Country = "xx";
							server.CountryName = "Unknown";
						}
					}
					_dirty = true;
				}
				Thread.Sleep(4500);
			}
			catch (Exception)
			{
				// Rate limit or no internet: try again later (the proxies stay in the queue on the next success).
				Thread.Sleep(30000);
				lock (_lock)
				{
					foreach (ProxyServer server in _all.Values.Where(s => s.Working && s.Country.Length == 0))
					{
						_toLocate.Enqueue(server);
					}
				}
			}
		}
	}

	public static bool IsBlocked(string host)
	{
		List<string> blocked = Blocked;
		return blocked != null && blocked.Any(prefix => prefix.Length > 0 && host.StartsWith(prefix, StringComparison.Ordinal));
	}

	#endregion

	#region Reading

	public static int KnownCount
	{
		get
		{
			lock (_lock)
			{
				return _all.Count;
			}
		}
	}

	/// <summary>What the window reads many times a frame, rebuilt at most twice a second.</summary>
	private sealed class Snapshot
	{
		public List<ProxyServer> Working;

		public List<CountryInfo> Countries;

		public int Tested;

		public int At;
	}

	private static Snapshot _snapshot;

	private static Snapshot Current
	{
		get
		{
			Snapshot snapshot = _snapshot;
			if (snapshot != null && unchecked(Environment.TickCount - snapshot.At) < 500)
			{
				return snapshot;
			}
			lock (_lock)
			{
				List<ProxyServer> working = _all.Values.Where(s => s.Working).OrderBy(s => s.Score).ToList();
				snapshot = new Snapshot
				{
					Working = working,
					Tested = _all.Values.Count(s => s.Tested),
					Countries = working.Where(s => s.Country.Length > 0).GroupBy(s => s.Country).Select(g => new CountryInfo
					{
						Code = g.Key,
						Name = g.First().CountryName,
						Servers = g.Count(),
						BestPing = g.Min(s => s.Ping),
						Reliability = g.Max(s => s.Reliability)
					}).ToList(),
					At = Environment.TickCount
				};
			}
			_snapshot = snapshot;
			return snapshot;
		}
	}

	public static int TestedCount => Current.Tested;

	public static int WorkingCount => Current.Working.Count;

	/// <summary>Proxies left to test (the untested ones don't count once <see cref="Target"/> is reached).</summary>
	public static int Waiting => _retest.Count + (Enough ? 0 : _queue.Count) + _running;

	/// <summary><see cref="Target"/> working proxies found: the search rests.</summary>
	public static bool Enough => WorkingCount >= Target;

	/// <summary>The good proxies, best first; <paramref name="country"/> narrows them to one country ("de").</summary>
	public static List<ProxyServer> Best(string country = null, int max = int.MaxValue)
	{
		return Current.Working.Where(s => country == null || s.Country == country).Take(max).ToList();
	}

	/// <summary>A country with good proxies, for the location list.</summary>
	public sealed class CountryInfo
	{
		public string Code;

		public string Name;

		public int Servers;

		public int BestPing;

		public float Reliability;
	}

	public static List<CountryInfo> Countries()
	{
		return Current.Countries.ToList();
	}

	/// <summary>The pool's entry for a proxy (its test results), or null.</summary>
	public static ProxyServer Find(string key)
	{
		lock (_lock)
		{
			return (key != null && _all.TryGetValue(key, out ProxyServer server)) ? server : null;
		}
	}

	/// <summary>A real connection through it worked or failed: counts like a test.</summary>
	public static void Report(ProxyServer server, bool ok, int ms)
	{
		lock (_lock)
		{
			if (_all.TryGetValue(server.Key, out ProxyServer known))
			{
				known.CountTest(ok, ok ? ms : 0);
				_dirty = true;
			}
		}
	}

	/// <summary>Tests one proxy (the user's own) in the background; the result lands on the object itself.</summary>
	public static void TestNow(ProxyServer server)
	{
		ThreadPool.QueueUserWorkItem(delegate
		{
			int ms = ProxyTunnel.Test(server);
			lock (_lock)
			{
				server.CountTest(ms > 0, ms);
			}
			if (ms > 0 && server.Country.Length == 0)
			{
				_toLocate.Enqueue(server);
			}
		});
	}

	/// <summary>Forgets every result and starts over (Settings > Start over).</summary>
	public static void Reset()
	{
		lock (_lock)
		{
			_all.Clear();
			_queued.Clear();
			while (_retest.TryDequeue(out _))
			{
			}
			while (_queue.TryDequeue(out _))
			{
			}
			_dirty = true;
		}
		Refresh();
	}

	#endregion

	#region Cache

	private sealed class Cache
	{
		public DateTime Saved;

		public List<ProxyServer> Proxies = new List<ProxyServer>();
	}

	private static void LoadCache()
	{
		try
		{
			if (!File.Exists(CachePath))
			{
				return;
			}
			Cache cache = JsonConvert.DeserializeObject<Cache>(File.ReadAllText(CachePath));
			lock (_lock)
			{
				foreach (ProxyServer server in cache?.Proxies ?? new List<ProxyServer>())
				{
					if (server?.Host != null && !IsBlocked(server.Host))
					{
						_all[server.Key] = server;
					}
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogOnce(ex);
		}
	}

	private static void SaveCache()
	{
		try
		{
			string json;
			lock (_lock)
			{
				_dirty = false;
				_lastSave = DateTime.Now;
				// Only what ever worked: the rest is cheap to find again from the lists.
				json = JsonConvert.SerializeObject(new Cache { Saved = DateTime.Now, Proxies = _all.Values.Where(s => s.Successes > 0).OrderBy(s => s.Score).Take(3000).ToList() });
			}
			Directory.CreateDirectory(Folder);
			string temp = CachePath + ".tmp";
			File.WriteAllText(temp, json);
			if (File.Exists(CachePath))
			{
				File.Delete(CachePath);
			}
			File.Move(temp, CachePath);
		}
		catch (Exception ex)
		{
			ClientLog.LogOnce(ex);
		}
	}

	#endregion
}
