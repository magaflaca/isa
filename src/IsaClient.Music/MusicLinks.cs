using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using IsaClient.Core;
using Newtonsoft.Json.Linq;

namespace IsaClient.Music;

/// <summary>Turns what the user typed into something playable: links, Spotify/Apple/Deezer pages, searches.</summary>
public static class MusicLinks
{
	private static readonly Regex _anyLink = new Regex("https?://[^\\s<>\"']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex _bareLink = new Regex("^(?:[a-z0-9-]+\\.)+[a-z]{2,}(?::\\d+)?/\\S*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex _spotifyUri = new Regex("^spotify:(track|album|playlist|episode|artist):([A-Za-z0-9]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	/// <summary>Sites yt-dlp cannot download from (DRM): the song is found by its name instead.</summary>
	private static readonly string[] _lookupHosts = new string[14] { "open.spotify.com", "play.spotify.com", "spotify.link", "music.apple.com", "itunes.apple.com", "deezer.com", "deezer.page.link", "link.deezer.com", "tidal.com", "music.amazon.", "amazon.com/music", "shazam.com", "music.yandex.", "last.fm/music" };

	private static readonly string[] _shortLinkHosts = new string[5] { "spotify.link", "deezer.page.link", "link.deezer.com", "on.soundcloud.com", "apple.co" };

	/// <summary>
	/// Turns what the user typed into something playable: links without "https://", links inside a sentence or between
	/// &lt;&gt;, spotify: URIs and file:/// paths. Anything else is a search.
	/// </summary>
	public static string NormalizeQuery(string query)
	{
		query = (query ?? "").Trim().Trim('"', '\'', '<', '>', '`').Trim();
		if (query.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
		{
			try
			{
				return new Uri(query).LocalPath;
			}
			catch
			{
				return query;
			}
		}
		Match spotify = _spotifyUri.Match(query);
		if (spotify.Success)
		{
			return "https://open.spotify.com/" + spotify.Groups[1].Value.ToLowerInvariant() + "/" + spotify.Groups[2].Value;
		}
		Match link = _anyLink.Match(query);
		if (link.Success)
		{
			query = link.Value;
		}
		else if (query.IndexOf(' ') < 0 && _bareLink.IsMatch(query))
		{
			query = "https://" + query;
		}
		if (query.StartsWith("http", StringComparison.OrdinalIgnoreCase))
		{
			query = query.TrimEnd('.', ',', ';', '!', ')', ']', '}');
		}
		return query;
	}

	public static bool IsLink(string query)
	{
		return NormalizeQuery(query).StartsWith("http", StringComparison.OrdinalIgnoreCase);
	}

	private static bool HostIs(string url, string[] hosts)
	{
		string lower = url.ToLowerInvariant();
		int start = lower.IndexOf("://", StringComparison.Ordinal);
		string rest = (start >= 0) ? lower.Substring(start + 3) : lower;
		if (rest.StartsWith("www."))
		{
			rest = rest.Substring(4);
		}
		return hosts.Any((string h) => rest.StartsWith(h) || rest.Contains("." + h));
	}

	internal static bool IsLookupService(string url)
	{
		return HostIs(url, _lookupHosts);
	}

	internal static bool IsShortLink(string url)
	{
		return HostIs(url, _shortLinkHosts);
	}

	/// <summary>Links that are a list of songs: YouTube playlists, SoundCloud sets, Bandcamp albums, Spotify / Deezer playlists and albums.</summary>
	internal static bool IsPlaylistLink(string url)
	{
		string lower = url.ToLowerInvariant();
		if (lower.Contains("list=") && !MusicPlayer.YouTubeIdPattern.IsMatch(url))
		{
			return true;
		}
		if (lower.Contains("soundcloud.com/") && lower.Contains("/sets/"))
		{
			return true;
		}
		if (lower.Contains("bandcamp.com/album/"))
		{
			return true;
		}
		if (IsLookupService(url) && (lower.Contains("/playlist/") || lower.Contains("/album/")) && !lower.Contains("?i=") && (lower.Contains("spotify") || lower.Contains("deezer")))
		{
			return true;
		}
		return false;
	}

	private static HttpWebRequest WebRequestFor(string url)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
		request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";
		request.Accept = "text/html,application/json;q=0.9,*/*;q=0.8";
		request.Headers[HttpRequestHeader.AcceptLanguage] = "en-US,en;q=0.8";
		request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		request.AllowAutoRedirect = true;
		request.MaximumAutomaticRedirections = 10;
		request.Timeout = 15000;
		request.ReadWriteTimeout = 15000;
		return request;
	}

	private static string FetchPage(string url)
	{
		using HttpWebResponse response = (HttpWebResponse)WebRequestFor(url).GetResponse();
		using Stream stream = response.GetResponseStream();
		using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
		char[] buffer = new char[4 * 1024 * 1024];
		int read = reader.ReadBlock(buffer, 0, buffer.Length);
		return new string(buffer, 0, read);
	}

	internal static string FollowRedirects(string url)
	{
		try
		{
			using HttpWebResponse response = (HttpWebResponse)WebRequestFor(url).GetResponse();
			string final = response.ResponseUri.ToString();
			if (final != url)
			{
				return final;
			}
			// Some short links redirect from the page itself.
			using StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
			string html = reader.ReadToEnd();
			Match target = Regex.Match(html, "(?:og:url\"\\s+content=\"|http-equiv=\"refresh\"[^>]*url=|window\\.location(?:\\.href)?\\s*=\\s*\")(https?://[^\"'>\\s]+)", RegexOptions.IgnoreCase);
			return target.Success ? WebUtility.HtmlDecode(target.Groups[1].Value) : url;
		}
		catch
		{
			return url;
		}
	}

	private static string MetaContent(string html, string property)
	{
		Match match = Regex.Match(html, "<meta[^>]+(?:property|name)=[\"']" + Regex.Escape(property) + "[\"'][^>]*content=[\"']([^\"']*)[\"']", RegexOptions.IgnoreCase);
		if (!match.Success)
		{
			match = Regex.Match(html, "<meta[^>]+content=[\"']([^\"']*)[\"'][^>]*(?:property|name)=[\"']" + Regex.Escape(property) + "[\"']", RegexOptions.IgnoreCase);
		}
		return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value).Trim() : null;
	}

	/// <summary>"Song - song and lyrics by Artist | Spotify" and the like, cleaned into "Song Artist" for a search.</summary>
	internal static string SongNameFromPage(string url)
	{
		try
		{
			Match deezer = Regex.Match(url, "deezer\\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?track/(\\d+)", RegexOptions.IgnoreCase);
			if (deezer.Success)
			{
				JObject song = JObject.Parse(FetchPage("https://api.deezer.com/track/" + deezer.Groups[1].Value));
				string songTitle = (string)song["title"];
				if (!string.IsNullOrEmpty(songTitle))
				{
					return ((string)song["artist"]?["name"] + " " + songTitle).Trim();
				}
			}
			Match spotifyTrack = Regex.Match(url, "spotify\\.com/(?:intl-[a-z-]+/)?(track|episode)/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
			if (spotifyTrack.Success)
			{
				// The normal page is drawn by script; the embed page carries the song: "entity":{..."title":..,"artists":[{"name":..}]}.
				string embed = FetchPage("https://open.spotify.com/embed/" + spotifyTrack.Groups[1].Value.ToLowerInvariant() + "/" + spotifyTrack.Groups[2].Value);
				int entity = embed.IndexOf("\"entity\":{", StringComparison.Ordinal);
				if (entity >= 0)
				{
					string json = JsonArrayAt(embed, entity + 9);
					string songTitle = JsonString(json, "title") ?? JsonString(json, "name");
					int artists = json.IndexOf("\"artists\":[", StringComparison.Ordinal);
					string artist = (artists >= 0) ? JsonString(json.Substring(artists), "name") : null;
					if (!string.IsNullOrEmpty(songTitle))
					{
						return ((artist ?? "") + " " + songTitle).Trim();
					}
				}
			}
			string html = FetchPage(url);
			Match titleTag = Regex.Match(html, "<title[^>]*>([^<]*)</title>", RegexOptions.IgnoreCase);
			string text = titleTag.Success ? WebUtility.HtmlDecode(titleTag.Groups[1].Value) : null;
			string og = MetaContent(html, "og:title");
			if (url.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(og))
			{
				// Spotify: og:title is the song, og:description "Artist · Album · Song · 1987".
				string artist = MetaContent(html, "music:musician_description");
				if (string.IsNullOrEmpty(artist))
				{
					artist = (MetaContent(html, "og:description") ?? "").Split('\u00b7')[0].Trim();
				}
				return (artist + " " + og).Trim();
			}
			if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 3 || Regex.IsMatch(text, "Web Player", RegexOptions.IgnoreCase))
			{
				text = og;
			}
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}
			text = text.Replace("\u200e", "").Replace("\u200f", "").Replace("\u00a0", " ").Trim();
			text = Regex.Replace(text, "\\s*[|\\-\u2013\u2014\u00b7]\\s*(Spotify|Apple\\s+Music|Deezer|TIDAL|Amazon\\s+Music|Shazam|Last\\.fm|Yandex\\s+Music|YouTube\\s+Music|YouTube|SoundCloud)\\s*$", "", RegexOptions.IgnoreCase);
			text = Regex.Replace(text, "\\s+on\\s+(Apple\\s+Music|Spotify|Deezer|TIDAL|Amazon\\s+Music)\\s*$", "", RegexOptions.IgnoreCase);
			text = Regex.Replace(text, "\\s+[-\u2013\u2014]\\s+(song\\s+and\\s+lyrics\\s+by|song\\s+with\\s+lyrics\\s+by|song\\s+by|single\\s+by|ep\\s+by|lyrics\\s+by|track\\s+by)\\s+", " ", RegexOptions.IgnoreCase);
			text = Regex.Replace(text, "\\s+by\\s+", " ", RegexOptions.IgnoreCase);
			text = Regex.Replace(text, "\\s+", " ").Trim();
			if (text.Length < 2 || Regex.IsMatch(text, "^(Spotify|Apple Music|Deezer|TIDAL|Amazon Music|Shazam|Web Player.*)$", RegexOptions.IgnoreCase))
			{
				return null;
			}
			return text;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			return null;
		}
	}

	/// <summary>Spotify and Deezer playlists / albums: their songs, as names to search.</summary>
	internal static List<MusicPlayer.Track> ReadServiceCollection(string url)
	{
		List<MusicPlayer.Track> tracks = new List<MusicPlayer.Track>();
		Match spotify = Regex.Match(url, "spotify\\.com/(?:intl-[a-z-]+/)?(playlist|album)/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
		if (spotify.Success)
		{
			string html = FetchPage("https://open.spotify.com/embed/" + spotify.Groups[1].Value.ToLowerInvariant() + "/" + spotify.Groups[2].Value);
			int start = html.IndexOf("\"trackList\":[", StringComparison.Ordinal);
			if (start < 0)
			{
				return tracks;
			}
			string list = JsonArrayAt(html, start + 12);
			foreach (string item in Regex.Split(list, "\\},\\s*\\{"))
			{
				string title = JsonString(item, "title");
				string artist = JsonString(item, "subtitle");
				if (string.IsNullOrEmpty(title))
				{
					continue;
				}
				Match ms = Regex.Match(item, "\"duration\":(\\d+)");
				string name = string.IsNullOrEmpty(artist) ? title : (artist + " - " + title);
				tracks.Add(new MusicPlayer.Track
				{
					Source = name,
					Title = name,
					Duration = ms.Success ? (double.Parse(ms.Groups[1].Value, CultureInfo.InvariantCulture) / 1000.0) : 0.0
				});
			}
			return tracks;
		}
		Match deezer = Regex.Match(url, "deezer\\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?(playlist|album)/(\\d+)", RegexOptions.IgnoreCase);
		if (deezer.Success)
		{
			JObject json = JObject.Parse(FetchPage("https://api.deezer.com/" + deezer.Groups[1].Value.ToLowerInvariant() + "/" + deezer.Groups[2].Value + "/tracks?limit=500"));
			if (json["data"] is JArray data)
			{
				foreach (JToken item in data)
				{
					string title = (string)item["title"];
					string artist = (string)item["artist"]?["name"];
					if (string.IsNullOrEmpty(title))
					{
						continue;
					}
					string name = string.IsNullOrEmpty(artist) ? title : (artist + " - " + title);
					tracks.Add(new MusicPlayer.Track
					{
						Source = name,
						Title = name,
						Duration = (double?)item["duration"] ?? 0.0
					});
				}
			}
		}
		return tracks;
	}

	/// <summary>The JSON array that starts at text[start] ('['), strings respected.</summary>
	private static string JsonArrayAt(string text, int start)
	{
		int depth = 0;
		bool inString = false;
		for (int i = start; i < text.Length; i++)
		{
			char c = text[i];
			if (inString)
			{
				if (c == '\\')
				{
					i++;
				}
				else if (c == '"')
				{
					inString = false;
				}
				continue;
			}
			if (c == '"')
			{
				inString = true;
			}
			else if (c == '[' || c == '{')
			{
				depth++;
			}
			else if (c == ']' || c == '}')
			{
				depth--;
				if (depth == 0)
				{
					return text.Substring(start, i - start + 1);
				}
			}
		}
		return text.Substring(start);
	}

	private static string JsonString(string json, string name)
	{
		Match match = Regex.Match(json, "\"" + name + "\":\"((?:[^\"\\\\]|\\\\.)*)\"");
		if (!match.Success)
		{
			return null;
		}
		try
		{
			return Regex.Unescape(match.Groups[1].Value);
		}
		catch
		{
			return match.Groups[1].Value;
		}
	}
}
