using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using IsaClient.Networking;
using Terraria;
using Terraria.IO;

namespace IsaClient.MultiClient;

/// <summary>How bots pick the proxies they connect through.</summary>
public enum BotRouteMode
{
	/// <summary>The best working proxies of the pool, anywhere.</summary>
	Fastest,
	/// <summary>Only proxies of one country.</summary>
	Country,
	/// <summary>Only the proxies the player picked.</summary>
	Chosen
}

/// <summary>
/// Where each bot connects from. Servers often let in only a few connections from one IP: with this on, bots fill the
/// free places of your own IP first (you count as one) and the rest go through proxies of the pool (the same tested
/// SOCKS/HTTP proxies the Proxy page uses), never more than the server's limit on any one of them. A bot keeps the
/// proxy that worked when it reconnects; a proxy that fails is reported to the pool and the next one is tried.
/// </summary>
public static class BotRoutes
{
	public const string Direct = "direct";

	/// <summary>Bots connect through proxies (when your own IP is full).</summary>
	public static bool Enabled;

	/// <summary>How many connections the server takes from one IP (you count too).</summary>
	public static int PerIp = 3;

	/// <summary>Bots use the free places of your own IP before any proxy.</summary>
	public static bool MyIpFirst = true;

	public static BotRouteMode Mode = BotRouteMode.Fastest;

	/// <summary>The country of <see cref="BotRouteMode.Country"/> ("de").</summary>
	public static string Country = "";

	/// <summary>The proxies of <see cref="BotRouteMode.Chosen"/> (keys like "socks5://1.2.3.4:1080").</summary>
	public static List<string> Chosen = new List<string>();

	/// <summary>When no proxy works, the bot connects from your IP anyway (the server may refuse it).</summary>
	public static bool FallbackDirect;

	public const int MinPerIp = 1;

	public const int MaxPerIp = 10;

	private static readonly object _lock = new object();

	#region Settings

	internal static void Load(Preferences config)
	{
		config.Get("BotProxy", ref Enabled);
		PerIp = Math.Max(MinPerIp, Math.Min(MaxPerIp, config.Get("BotProxyPerIp", 3)));
		config.Get("BotProxyMyIpFirst", ref MyIpFirst);
		config.Get("BotProxyFallback", ref FallbackDirect);
		Enum.TryParse(config.Get("BotProxyMode", "Fastest"), ignoreCase: true, out Mode);
		Country = config.Get("BotProxyCountry", "") ?? "";
		Chosen = config.Get("BotProxyChosen", new List<string>()) ?? new List<string>();
		if (Enabled)
		{
			ProxyPool.Start();
		}
	}

	internal static void Save(Preferences config)
	{
		config.Put("BotProxy", Enabled);
		config.Put("BotProxyPerIp", PerIp);
		config.Put("BotProxyMyIpFirst", MyIpFirst);
		config.Put("BotProxyFallback", FallbackDirect);
		config.Put("BotProxyMode", Mode.ToString());
		config.Put("BotProxyCountry", Country);
		config.Put("BotProxyChosen", Chosen);
	}

	public static void TurnOn(bool on)
	{
		Enabled = on;
		if (on)
		{
			ProxyPool.Start();
		}
		MultiClientManager.Save();
	}

	public static void UseFastest()
	{
		Mode = BotRouteMode.Fastest;
		MultiClientManager.Save();
	}

	public static void UseCountry(string code)
	{
		Mode = BotRouteMode.Country;
		Country = code ?? "";
		MultiClientManager.Save();
	}

	/// <summary>Adds or removes a proxy of the picked list (and switches to it).</summary>
	public static void ToggleChosen(ProxyServer server)
	{
		lock (_lock)
		{
			List<string> chosen = new List<string>(Chosen);
			if (!chosen.Remove(server.Key))
			{
				chosen.Add(server.Key);
			}
			Chosen = chosen;
		}
		Mode = BotRouteMode.Chosen;
		MultiClientManager.Save();
	}

	public static bool IsChosen(ProxyServer server)
	{
		return Chosen.Contains(server.Key);
	}

	#endregion

	#region Reading

	/// <summary>The bots that use one exit (your IP or a proxy) on one server, now.</summary>
	public sealed class Exit
	{
		public string Key;

		/// <summary>Null for your own IP.</summary>
		public ProxyServer Proxy;

		public string Host;

		public int Port;

		public bool You;

		public List<ClientSession> Bots = new List<ClientSession>();

		public int Used => Bots.Count + (You ? 1 : 0);
	}

	/// <summary>Counts as using its exit: connecting or connected (also while you play as it).</summary>
	private static bool Holds(ClientSession s)
	{
		return s.RouteKey != null && (s.Possessed || s.State == ClientSessionState.Connecting || s.State == ClientSessionState.Handshaking || s.State == ClientSessionState.InGame);
	}

	/// <summary>Every exit in use, per server: your IP first, then the proxies.</summary>
	public static List<Exit> Exits()
	{
		List<Exit> exits = new List<Exit>();
		foreach (ClientSession s in MultiClientManager.All)
		{
			if (!Holds(s))
			{
				continue;
			}
			Exit exit = exits.FirstOrDefault(e => e.Key == s.RouteKey && SameServer(e.Host, e.Port, s.Host, s.Port));
			if (exit == null)
			{
				exit = new Exit { Key = s.RouteKey, Proxy = s.Route, Host = s.Host, Port = s.Port };
				exit.You = s.RouteKey == Direct && YouAreOn(s.Host, s.Port);
				exits.Add(exit);
			}
			exit.Bots.Add(s);
		}
		return exits.OrderBy(e => (e.Key == Direct) ? 0 : 1).ThenByDescending(e => e.Bots.Count).ToList();
	}

	/// <summary>Bots on proxies now.</summary>
	public static int ProxiedCount => MultiClientManager.All.Count(s => Holds(s) && s.RouteKey != Direct);

	/// <summary>The proxies the current choice would use (best first), before counting places.</summary>
	public static List<ProxyServer> Candidates()
	{
		List<ProxyServer> list;
		switch (Mode)
		{
		case BotRouteMode.Country:
			list = ProxyPool.Best(Country);
			break;
		case BotRouteMode.Chosen:
		{
			list = new List<ProxyServer>();
			List<ProxyServer> mine = Proxy.Mine;
			foreach (string key in Chosen)
			{
				ProxyServer server = ProxyPool.Find(key) ?? mine.FirstOrDefault(m => m.Key == key);
				if (server != null)
				{
					list.Add(server);
				}
			}
			list = list.OrderBy(s => s.Working ? 0 : 1).ThenBy(s => s.Score).ToList();
			break;
		}
		default:
			list = ProxyPool.Best();
			break;
		}
		return list;
	}

	/// <summary>A short line for the panel: where bots come from now.</summary>
	public static string Summary
	{
		get
		{
			if (!Enabled)
			{
				return "Off: every bot connects from your IP";
			}
			int proxied = ProxiedCount;
			int proxies = Exits().Count(e => e.Key != Direct);
			return (proxied == 0) ? ("On: up to " + PerIp + " per IP, bots go through proxies when your IP is full") : (proxied + " bots on " + proxies + ((proxies == 1) ? " proxy" : " proxies") + ", " + PerIp + " per IP");
		}
	}

	#endregion

	#region Connecting

	/// <summary>
	/// A connection to <paramref name="host"/>:<paramref name="port"/> for <paramref name="bot"/>: from your IP while it has
	/// room (or always, when this is off), else through a proxy with room. Sets the bot's route. Throws when nothing works.
	/// Runs on the bot's connecting task.
	/// </summary>
	public static TcpClient Open(ClientSession bot, string host, int port)
	{
		if (!Enabled)
		{
			Reserve(bot, Direct, null);
			return ConnectDirect(host, port);
		}
		ProxyPool.Start();
		IPAddress target = Resolve(host);
		// Exits it dropped on are left out first; only when nothing else works are they tried again.
		HashSet<string> avoid;
		lock (bot.AvoidRoutes)
		{
			avoid = new HashSet<string>(bot.AvoidRoutes);
		}
		HashSet<string> broken = new HashSet<string>();
		HashSet<string> failed = new HashSet<string>(avoid);
		string lastError = null;
		for (int attempt = 0; attempt < 12; attempt++)
		{
			string key;
			ProxyServer proxy;
			lock (_lock)
			{
				key = Pick(bot, host, port, failed, out proxy);
				if (key == null && avoid.Count > 0)
				{
					avoid.Clear();
					failed = new HashSet<string>(broken);
					key = Pick(bot, host, port, failed, out proxy);
				}
				if (key == null)
				{
					break;
				}
				Reserve(bot, key, proxy);
			}
			if (proxy == null)
			{
				bot.StatusText = "connecting from your IP";
				return ConnectDirect(host, port);
			}
			bot.StatusText = "connecting through " + Place(proxy);
			TcpClient client = null;
			try
			{
				Stopwatch watch = Stopwatch.StartNew();
				client = ProxyTunnel.ConnectTo(proxy, 5000);
				ProxyTunnel.Open(client.GetStream(), proxy, target, port, 8000);
				ProxyPool.Report(proxy, true, (int)watch.ElapsedMilliseconds);
				bot.LastRouteKey = key;
				return client;
			}
			catch (Exception ex)
			{
				lastError = ex.Message;
				ProxyPool.Report(proxy, false, 0);
				client?.Close();
				failed.Add(key);
				broken.Add(key);
				lock (_lock)
				{
					Reserve(bot, null, null);
				}
			}
		}
		if (FallbackDirect)
		{
			Reserve(bot, Direct, null);
			bot.StatusText = "no proxy worked, connecting from your IP";
			return ConnectDirect(host, port);
		}
		Reserve(bot, null, null);
		string why = Candidates().Count == 0
			? ((Mode == BotRouteMode.Country) ? ("no working proxy in " + CountryNames.Name(Country) + " yet") : ((Mode == BotRouteMode.Chosen) ? "none of your picked proxies is available" : "no working proxy found yet"))
			: ((lastError == null) ? ("every proxy already has " + PerIp + " bots") : ("no proxy could connect (" + lastError + ")"));
		throw new IOException(why);
	}

	/// <summary>The exit for the next try (under the lock): your IP while it has room, the proxy it had, then the best with room.</summary>
	private static string Pick(ClientSession bot, string host, int port, HashSet<string> failed, out ProxyServer proxy)
	{
		proxy = null;
		Dictionary<string, int> used = new Dictionary<string, int>();
		foreach (ClientSession s in MultiClientManager.All)
		{
			if (s != bot && Holds(s) && SameServer(s.Host, s.Port, host, port))
			{
				used.TryGetValue(s.RouteKey, out int n);
				used[s.RouteKey] = n + 1;
			}
		}
		// You, when you play on that server from your IP (not through the client's own proxy).
		if (YouAreOn(host, port))
		{
			string yours = (IsaClient.Networking.Proxy.Active != null) ? IsaClient.Networking.Proxy.Active.Key : Direct;
			used.TryGetValue(yours, out int n);
			used[yours] = n + 1;
		}
		int Count(string key) => used.TryGetValue(key, out int n) ? n : 0;
		if (MyIpFirst && !failed.Contains(Direct) && Count(Direct) < PerIp)
		{
			return Direct;
		}
		List<ProxyServer> candidates = Candidates().Where(s => !failed.Contains(s.Key) && Count(s.Key) < PerIp).ToList();
		ProxyServer previous = (bot.LastRouteKey != null) ? candidates.FirstOrDefault(s => s.Key == bot.LastRouteKey) : null;
		// Fill the proxies already in use before opening new ones (fewer proxies to go wrong), fastest first.
		proxy = previous ?? candidates.Where(s => Count(s.Key) > 0).FirstOrDefault() ?? candidates.FirstOrDefault();
		return proxy?.Key;
	}

	private static void Reserve(ClientSession bot, string key, ProxyServer proxy)
	{
		bot.RouteKey = key;
		bot.Route = proxy;
	}

	private static TcpClient ConnectDirect(string host, int port)
	{
		TcpClient tcp = new TcpClient
		{
			NoDelay = true
		};
		try
		{
			tcp.Connect(host, port);
			return tcp;
		}
		catch
		{
			tcp.Close();
			throw;
		}
	}

	private static IPAddress Resolve(string host)
	{
		if (IPAddress.TryParse(host, out IPAddress address))
		{
			return address;
		}
		foreach (IPAddress a in Dns.GetHostAddresses(host))
		{
			if (a.AddressFamily == AddressFamily.InterNetwork)
			{
				return a;
			}
		}
		throw new IOException("could not find " + host);
	}

	private static bool SameServer(string hostA, int portA, string hostB, int portB)
	{
		return portA == portB && string.Equals(hostA, hostB, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>You are online on that server (by address and port).</summary>
	private static bool YouAreOn(string host, int port)
	{
		try
		{
			if (Main.gameMenu || Main.netMode != 1 || Netplay.ListenPort != port || Netplay.ServerIP == null)
			{
				return false;
			}
			if (string.Equals(Netplay.ServerIPText, host, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			return IPAddress.TryParse(host, out IPAddress ip) && ip.Equals(Netplay.ServerIP);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>"Germany (1.2.3.4)" for status lines.</summary>
	public static string Place(ProxyServer proxy)
	{
		return string.IsNullOrEmpty(proxy.Country) ? ("a proxy (" + proxy.Host + ")") : (CountryNames.Name(proxy.Country) + " (" + proxy.Host + ")");
	}

	#endregion
}
