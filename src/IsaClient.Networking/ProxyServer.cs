using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace IsaClient.Networking;

public enum ProxyProtocol
{
	Socks5,
	Socks4,
	Http
}

/// <summary>
/// One proxy: where it is, how to talk to it, and what the tests found (round trip, how often it worked, country).
/// Pool proxies and the user's own ones are both this.
/// </summary>
public sealed class ProxyServer
{
	public string Host;

	public int Port;

	public ProxyProtocol Protocol;

	public string Username = "";

	public string Password = "";

	/// <summary>The list it came from (empty for the user's own).</summary>
	public string Source = "";

	/// <summary>Round trip of the last good test through the proxy, in ms (-1 = never worked).</summary>
	public int Ping = -1;

	public int Successes;

	public int Failures;

	/// <summary>Failed tests in a row: two and it stops counting as working.</summary>
	public int Strikes;

	public DateTime LastTest;

	public DateTime LastOk;

	/// <summary>ISO code in lower case ("de"), "" while unknown.</summary>
	public string Country = "";

	public string CountryName = "";

	public string City = "";

	[JsonIgnore]
	public string Address => Host + ":" + Port;

	/// <summary>"socks5://1.2.3.4:1080" (no password): the key of a proxy everywhere.</summary>
	[JsonIgnore]
	public string Key => SchemeOf(Protocol) + "://" + Address;

	/// <summary>Passed a real tunnel twice and has not failed twice since.</summary>
	[JsonIgnore]
	public bool Working => Successes >= 2 && Strikes < 2 && Ping >= 0;

	[JsonIgnore]
	public bool Tested => Successes + Failures > 0;

	/// <summary>Share of the tests it passed (0..1).</summary>
	[JsonIgnore]
	public float Reliability => (Successes + Failures == 0) ? 0f : (float)Successes / (Successes + Failures);

	/// <summary>Lower is better: the round trip, worse when it fails sometimes.</summary>
	[JsonIgnore]
	public float Score => Ping * (1f + 2f * (1f - Reliability)) + Strikes * 400f;

	[JsonIgnore]
	public string ProtocolName => Protocol switch
	{
		ProxyProtocol.Socks4 => "SOCKS4",
		ProxyProtocol.Http => "HTTP",
		_ => "SOCKS5"
	};

	public static string SchemeOf(ProxyProtocol protocol)
	{
		return protocol switch
		{
			ProxyProtocol.Socks4 => "socks4",
			ProxyProtocol.Http => "http",
			_ => "socks5"
		};
	}

	private static readonly Regex _pattern = new Regex("^(?:(socks5h?|socks4a?|https?)://)?(?:([^:@\\s]+):([^@\\s]*)@)?([A-Za-z0-9.\\-]+):(\\d{1,5})$", RegexOptions.IgnoreCase);

	/// <summary>"1.2.3.4:1080", "socks4://host:port", "http://user:pass@host:port"... null when it is not a proxy address.</summary>
	public static ProxyServer Parse(string text, ProxyProtocol fallback = ProxyProtocol.Socks5)
	{
		Match m = _pattern.Match((text ?? "").Trim());
		if (!m.Success || !int.TryParse(m.Groups[5].Value, out int port) || port <= 0 || port > 65535)
		{
			return null;
		}
		string scheme = m.Groups[1].Value.ToLowerInvariant();
		return new ProxyServer
		{
			Protocol = scheme.StartsWith("socks4") ? ProxyProtocol.Socks4 : (scheme.StartsWith("http") ? ProxyProtocol.Http : (scheme.Length > 0 ? ProxyProtocol.Socks5 : fallback)),
			Username = m.Groups[2].Value,
			Password = m.Groups[3].Value,
			Host = m.Groups[4].Value,
			Port = port
		};
	}

	/// <summary>The text that <see cref="Parse"/> reads back (with the login, for the saved list).</summary>
	public string ToSavedText()
	{
		string login = string.IsNullOrEmpty(Username) ? "" : (Username + ":" + Password + "@");
		return SchemeOf(Protocol) + "://" + login + Address;
	}

	public void CountTest(bool ok, int ms)
	{
		LastTest = DateTime.Now;
		if (ok)
		{
			Successes++;
			Strikes = 0;
			LastOk = LastTest;
			Ping = (Ping < 0) ? ms : (Ping * 2 + ms) / 3;
		}
		else
		{
			Failures++;
			Strikes++;
		}
	}
}
