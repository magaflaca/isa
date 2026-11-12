using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using IsaClient.Core;
using Terraria;
using Terraria.IO;

namespace IsaClient.Networking;

public enum ProxyMode
{
	/// <summary>The best proxy anywhere.</summary>
	Fastest,
	/// <summary>The best proxy of one country.</summary>
	Country,
	/// <summary>One proxy of the pool the user picked (others of its country if it fails).</summary>
	Server,
	/// <summary>One of the user's own proxies.</summary>
	Custom
}

/// <summary>
/// Personal proxy for the player's game connection, not for
/// evading bans. When on, TcpSocket.Connect goes through the chosen location: the fastest proxy, the best of a country,
/// one server, or one of your own (SOCKS5, SOCKS4 or HTTP). Free proxies come from <see cref="ProxyPool"/>.
/// Config: IsaClient/IsaProxy.json. Window: ProxyMenu (main menu) and the panel's Proxy section.
/// </summary>
public static class Proxy
{
	public static bool Enabled;

	public static ProxyMode Mode = ProxyMode.Fastest;

	/// <summary>Country code ("de") for <see cref="ProxyMode.Country"/>.</summary>
	public static string Country = "";

	/// <summary>The pool proxy (its key) for <see cref="ProxyMode.Server"/>.</summary>
	public static string ServerKey = "";

	/// <summary>The user's proxy (its key) for <see cref="ProxyMode.Custom"/>.</summary>
	public static string CustomKey = "";

	/// <summary>If no proxy works, connect directly instead of failing (off = privacy first).</summary>
	public static bool FallbackDirect;

	/// <summary>Countries starred in the list.</summary>
	public static List<string> FavoriteCountries = new List<string>();

	/// <summary>The user's own proxies, as "socks5://user:pass@host:port".</summary>
	public static List<string> SavedProxies = new List<string>();

	private static readonly List<ProxyServer> _mine = new List<ProxyServer>();

	private static Preferences _config;

	private static bool _loaded;

	public static string ConfigPath => Path.Combine(ClientSettings.IsaClientFolder, "IsaProxy.json");

	/// <summary>The proxy of the current (or last) connection, and when it was made.</summary>
	public static ProxyServer Active { get; private set; }

	public static DateTime ActiveSince { get; private set; }

	/// <summary>Why the last connection through a proxy failed (null when it worked).</summary>
	public static string LastError { get; private set; }

	/// <summary>A connection through a proxy is on right now.</summary>
	public static bool Connected => Active != null && !Main.gameMenu && Main.netMode == 1;

	#region Config

	public static void EnsureLoaded()
	{
		if (!_loaded)
		{
			Load();
		}
	}

	public static void Load()
	{
		_loaded = true;
		try
		{
			Directory.CreateDirectory(ClientSettings.IsaClientFolder);
			_config = new Preferences(ConfigPath);
			if (!_config.Load())
			{
				Save();
				return;
			}
			Enabled = _config.Get("Enabled", false);
			Mode = Enum.TryParse(_config.Get("Mode", "Fastest"), out ProxyMode mode) ? mode : ProxyMode.Fastest;
			Country = _config.Get("Country", "") ?? "";
			ServerKey = _config.Get("Server", "") ?? "";
			CustomKey = _config.Get("Custom", "") ?? "";
			FallbackDirect = _config.Get("FallbackDirect", false);
			FavoriteCountries = _config.Get("FavoriteCountries", new List<string>()) ?? new List<string>();
			SavedProxies = _config.Get("SavedProxies", new List<string>()) ?? new List<string>();
			ProxyPool.Blocked = _config.Get("BlockedPrefixes", new List<string> { "45.74." }) ?? new List<string>();
			List<string> off = _config.Get("DisabledLists", new List<string>()) ?? new List<string>();
			foreach (ProxyPool.Source source in ProxyPool.Sources)
			{
				source.Enabled = !off.Contains(source.Name);
			}
			// Before 1.4.4 a single proxy was set with Host/Port (+ login): it becomes one of yours.
			string host = _config.Get("Host", "") ?? "";
			if (host.Length > 0)
			{
				ProxyServer old = new ProxyServer { Host = host, Port = _config.Get("Port", 1080), Username = _config.Get("Username", "") ?? "", Password = _config.Get("Password", "") ?? "" };
				if (!SavedProxies.Any(s => ProxyServer.Parse(s)?.Key == old.Key))
				{
					SavedProxies.Insert(0, old.ToSavedText());
				}
				Mode = ProxyMode.Custom;
				CustomKey = old.Key;
				Save();
			}
			RebuildMine();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	public static void Save()
	{
		try
		{
			Directory.CreateDirectory(ClientSettings.IsaClientFolder);
			_config ??= new Preferences(ConfigPath);
			_config.Clear();
			_config.Put("Enabled", Enabled);
			_config.Put("Mode", Mode.ToString());
			_config.Put("Country", Country);
			_config.Put("Server", ServerKey);
			_config.Put("Custom", CustomKey);
			_config.Put("FallbackDirect", FallbackDirect);
			_config.Put("FavoriteCountries", FavoriteCountries);
			_config.Put("SavedProxies", SavedProxies);
			_config.Put("BlockedPrefixes", ProxyPool.Blocked);
			_config.Put("DisabledLists", ProxyPool.Sources.Where(s => !s.Enabled).Select(s => s.Name).ToList());
			_config.Save();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	#endregion

	#region Choosing

	/// <summary>Your own proxies (with their last test).</summary>
	public static List<ProxyServer> Mine
	{
		get
		{
			lock (_mine)
			{
				return _mine.ToList();
			}
		}
	}

	private static void RebuildMine()
	{
		lock (_mine)
		{
			Dictionary<string, ProxyServer> old = _mine.ToDictionary(s => s.Key, s => s);
			_mine.Clear();
			foreach (string text in SavedProxies)
			{
				ProxyServer server = ProxyServer.Parse(text);
				if (server == null)
				{
					continue;
				}
				if (old.TryGetValue(server.Key, out ProxyServer known))
				{
					known.Username = server.Username;
					known.Password = server.Password;
					server = known;
				}
				_mine.Add(server);
			}
		}
	}

	public static void TurnOn(bool on)
	{
		Enabled = on;
		Save();
	}

	public static void UseFastest()
	{
		Mode = ProxyMode.Fastest;
		Enabled = true;
		Save();
	}

	public static void UseCountry(string code)
	{
		Mode = ProxyMode.Country;
		Country = code;
		Enabled = true;
		Save();
	}

	public static void UseServer(ProxyServer server)
	{
		Mode = ProxyMode.Server;
		ServerKey = server.Key;
		Country = server.Country;
		Enabled = true;
		Save();
	}

	public static void UseCustom(ProxyServer server)
	{
		Mode = ProxyMode.Custom;
		CustomKey = server.Key;
		Enabled = true;
		Save();
	}

	/// <summary>Adds one of your own; null when it worked, else what is wrong with the text.</summary>
	public static string AddMine(string text, ProxyProtocol protocol)
	{
		ProxyServer server = ProxyServer.Parse(text, protocol);
		if (server == null)
		{
			return "Write it as ip:port (1.2.3.4:1080), optionally with a login: user:pass@1.2.3.4:1080";
		}
		SavedProxies.RemoveAll(s => ProxyServer.Parse(s)?.Key == server.Key);
		SavedProxies.Insert(0, server.ToSavedText());
		RebuildMine();
		Save();
		ProxyServer added = Mine.FirstOrDefault(s => s.Key == server.Key);
		if (added != null)
		{
			ProxyPool.TestNow(added);
		}
		return null;
	}

	public static void RemoveMine(ProxyServer server)
	{
		SavedProxies.RemoveAll(s => ProxyServer.Parse(s)?.Key == server.Key);
		if (CustomKey == server.Key && Mode == ProxyMode.Custom)
		{
			Mode = ProxyMode.Fastest;
		}
		RebuildMine();
		Save();
	}

	/// <summary>Keeps a pool proxy among your own.</summary>
	public static void KeepServer(ProxyServer server)
	{
		if (!SavedProxies.Any(s => ProxyServer.Parse(s)?.Key == server.Key))
		{
			SavedProxies.Insert(0, server.ToSavedText());
			RebuildMine();
			ProxyServer kept = Mine.FirstOrDefault(s => s.Key == server.Key);
			if (kept != null)
			{
				kept.Ping = server.Ping;
				kept.Successes = server.Successes;
				kept.Country = server.Country;
				kept.CountryName = server.CountryName;
				kept.City = server.City;
			}
			Save();
		}
	}

	public static bool IsMine(ProxyServer server)
	{
		lock (_mine)
		{
			return _mine.Any(s => s.Key == server.Key);
		}
	}

	public static void ToggleFavorite(string code)
	{
		if (!FavoriteCountries.Remove(code))
		{
			FavoriteCountries.Add(code);
		}
		Save();
	}

	/// <summary>The proxies to try for the next connection, best first.</summary>
	public static List<ProxyServer> Candidates()
	{
		List<ProxyServer> list = new List<ProxyServer>();
		switch (Mode)
		{
		case ProxyMode.Custom:
		{
			ProxyServer mine = Mine.FirstOrDefault(s => s.Key == CustomKey);
			if (mine != null)
			{
				list.Add(mine);
			}
			break;
		}
		case ProxyMode.Server:
		{
			ProxyServer chosen = ProxyPool.Find(ServerKey);
			if (chosen != null)
			{
				list.Add(chosen);
			}
			list.AddRange(ProxyPool.Best(Country, 5));
			break;
		}
		case ProxyMode.Country:
			list.AddRange(ProxyPool.Best(Country, 6));
			break;
		default:
			list.AddRange(ProxyPool.Best(null, 6));
			break;
		}
		return list.GroupBy(s => s.Key).Select(g => g.First()).ToList();
	}

	/// <summary>"Germany", "Fastest location", "1.2.3.4:1080"... what the connect button would use.</summary>
	public static string ChoiceName
	{
		get
		{
			switch (Mode)
			{
			case ProxyMode.Country:
				return CountryNames.Name(Country);
			case ProxyMode.Server:
			{
				ProxyServer server = ProxyPool.Find(ServerKey);
				return (server != null) ? (CountryNames.Name(server.Country) + " · " + server.Address) : "A saved server";
			}
			case ProxyMode.Custom:
			{
				ProxyServer mine = Mine.FirstOrDefault(s => s.Key == CustomKey);
				return (mine != null) ? mine.Address : "Your proxy";
			}
			default:
				return "Fastest location";
			}
		}
	}

	/// <summary>The country flag code of the choice ("" = none, "auto" = fastest).</summary>
	public static string ChoiceFlag
	{
		get
		{
			switch (Mode)
			{
			case ProxyMode.Country:
				return Country;
			case ProxyMode.Server:
				return ProxyPool.Find(ServerKey)?.Country ?? "";
			case ProxyMode.Custom:
				return Mine.FirstOrDefault(s => s.Key == CustomKey)?.Country ?? "";
			default:
				return "auto";
			}
		}
	}

	public static string StatusLine
	{
		get
		{
			EnsureLoaded();
			if (!Enabled)
			{
				return "off";
			}
			if (Connected)
			{
				return "connected via " + CountryNames.Name(Active.Country) + " (" + Active.Address + ")";
			}
			return "on, " + ChoiceName;
		}
	}

	#endregion

	#region Connecting

	/// <summary>
	/// Called from TcpSocket.Connect. When the proxy is on, returns a connection to <paramref name="target"/> made through
	/// it (trying the next candidates when one fails). Null = connect directly. Throws when nothing works and falling
	/// back to a direct connection is off.
	/// </summary>
	public static TcpClient TryConnect(IPAddress target, int targetPort)
	{
		EnsureLoaded();
		if (!Enabled)
		{
			Active = null;
			return null;
		}
		List<ProxyServer> candidates = Candidates();
		if (candidates.Count == 0)
		{
			LastError = (Mode == ProxyMode.Country) ? ("No working proxy in " + CountryNames.Name(Country) + " yet.") : "No working proxy found yet.";
			if (FallbackDirect)
			{
				return null;
			}
			throw new IOException("IsaProxy: " + LastError + " Pick another location in the proxy window, or wait a moment while it tests.");
		}
		string lastError = null;
		foreach (ProxyServer server in candidates.Take(6))
		{
			TcpClient client = null;
			try
			{
				Stopwatch watch = Stopwatch.StartNew();
				client = ProxyTunnel.ConnectTo(server, 5000);
				ProxyTunnel.Open(client.GetStream(), server, target, targetPort, 8000);
				ProxyPool.Report(server, true, (int)watch.ElapsedMilliseconds);
				Active = server;
				ActiveSince = DateTime.Now;
				LastError = null;
				return client;
			}
			catch (Exception ex)
			{
				lastError = ex.Message;
				ProxyPool.Report(server, false, 0);
				client?.Close();
			}
		}
		Active = null;
		LastError = "No proxy could connect: " + lastError;
		if (FallbackDirect)
		{
			return null;
		}
		throw new IOException("IsaProxy: " + LastError);
	}

	#endregion

	#region Command

	public static bool HandleCommand(string text, string chatText, string[] array)
	{
		if (text != "proxy" && text != "vpn")
		{
			return false;
		}
		EnsureLoaded();
		if (array.Length < 2)
		{
			if (!IsaClient.Menu.ProxyMenu.OpenFromCommand())
			{
				Say("Proxy: " + StatusLine + ". " + ProxyPool.WorkingCount + " working proxies.");
				ClientChat.Notice("  .proxy on | off | fastest | country <code or name> | add <ip:port> | refresh | direct");
			}
			return true;
		}
		string sub = array[1].ToLowerInvariant();
		string rest = (array.Length > 2) ? string.Join(" ", array.Skip(2)) : "";
		switch (sub)
		{
		case "on":
			TurnOn(true);
			Say("On: " + ChoiceName + ". It applies on your next server connection.");
			break;
		case "off":
			TurnOn(false);
			Say("Off. Your next connection goes direct.");
			break;
		case "best":
		case "fastest":
			UseFastest();
			Say("On: fastest location (" + ProxyPool.WorkingCount + " working proxies).");
			break;
		case "country":
		{
			string code = CountryNames.Find(rest);
			if (code == null)
			{
				Say("Which country? For example .proxy country de or .proxy country germany");
				break;
			}
			UseCountry(code);
			int servers = ProxyPool.Best(code).Count;
			Say("On: " + CountryNames.Name(code) + " (" + servers + " working " + (servers == 1 ? "server" : "servers") + (servers == 0 ? ", still looking" : "") + ").");
			break;
		}
		case "add":
		case "set":
		{
			string error = AddMine(rest, ProxyProtocol.Socks5);
			if (error != null)
			{
				Say(error);
				break;
			}
			UseCustom(Mine[0]);
			Say("Added and chosen: " + Mine[0].Address + ". It applies on your next server connection.");
			break;
		}
		case "fetch":
		case "refresh":
			ProxyPool.Refresh();
			Say("Downloading the lists again; new proxies are tested in the background.");
			break;
		case "direct":
			FallbackDirect = !FallbackDirect;
			Save();
			Say("When no proxy works: " + (FallbackDirect ? "connect directly" : "don't connect (privacy first)") + ".");
			break;
		case "status":
		case "list":
			Say(StatusLine + ". " + ProxyPool.WorkingCount + " working of " + ProxyPool.TestedCount + " tested (" + ProxyPool.KnownCount + " known).");
			break;
		default:
			Say("Unknown option. .proxy on | off | fastest | country <code> | add <ip:port> | refresh | direct | status");
			break;
		}
		return true;
	}

	private static void Say(string text)
	{
		ClientChat.Info("[Proxy] " + text);
	}

	#endregion
}
