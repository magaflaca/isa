using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace IsaClient.Assistant;

/// <summary>One search hit.</summary>
public sealed class SearchResult
{
	public string Title;

	public string Url;

	public string Snippet;

	/// <summary>"terraria.wiki.gg" (no "www.").</summary>
	public string Domain
	{
		get
		{
			try
			{
				string host = new Uri(Url).Host;
				return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host.Substring(4) : host;
			}
			catch
			{
				return Url;
			}
		}
	}
}

/// <summary>
/// Web search without keys: DuckDuckGo's HTML page, Brave Search, and Wikipedia's search as the last resort. An
/// engine that asks for a captcha or says "too many requests" rests for a few minutes and the next one is used.
/// Searches are spaced a little so no engine sees a burst. Ads are skipped and redirect links are turned back into
/// the real address.
/// </summary>
internal static class WebSearch
{
	public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";

	private static readonly Regex DuckAnchor = new Regex("class=\"result__a\"[^>]*href=\"(?<url>[^\"]+)\"[^>]*>(?<title>.*?)</a>|href=\"(?<url>[^\"]+)\"[^>]*class=\"result__a\"[^>]*>(?<title>.*?)</a>", RegexOptions.Compiled | RegexOptions.Singleline);

	private static readonly Regex DuckSnippet = new Regex("class=\"result__snippet\"[^>]*>(?<text>.*?)</(?:a|div|td)>", RegexOptions.Compiled | RegexOptions.Singleline);

	private static readonly Regex BraveLink = new Regex("<a href=\"(?<url>https?://[^\"]+)\"", RegexOptions.Compiled);

	private static readonly Regex BraveTitle = new Regex("class=\"title[^\"]*\"[^>]*>(?<text>.*?)</div>", RegexOptions.Compiled | RegexOptions.Singleline);

	private static readonly Regex BraveSnippet = new Regex("class=\"content[^\"]*\"[^>]*>(?<text>.*?)</div>", RegexOptions.Compiled | RegexOptions.Singleline);

	private static readonly Regex Tags = new Regex("<[^>]+>", RegexOptions.Compiled);

	private static readonly object _pace = new object();

	private static DateTime _nextSearch = DateTime.MinValue;

	/// <summary>DuckDuckGo, Brave, Wikipedia: until when each one rests.</summary>
	private static readonly DateTime[] _restUntil = new DateTime[3];

	private static readonly string[] EngineNames = { "DuckDuckGo", "Brave", "Wikipedia" };

	/// <summary>
	/// Up to <paramref name="max"/> results. <paramref name="region"/> like "wt-wt" (anywhere) or "mx-es";
	/// <paramref name="safe"/> 1 strict, -1 moderate, -2 off.
	/// </summary>
	public static List<SearchResult> Search(string query, string region, int safe, int max = 10, Action<string> note = null)
	{
		// With a Brave Search API key (free plan) the official API goes first: no captchas.
		if (!string.IsNullOrWhiteSpace(BraveApiKey))
		{
			try
			{
				List<SearchResult> api = SearchBraveApi(query, region, safe, max);
				if (api.Count > 0)
				{
					return api;
				}
			}
			catch (AssistantException ex)
			{
				note?.Invoke("Brave API unavailable (" + ex.Message + "), trying the next engine");
			}
		}
		Pace();
		AssistantException failure = null;
		// Engines that aren't resting first, in order; the resting ones only if nothing else worked.
		List<int> order = new List<int>();
		for (int pass = 0; pass < 2; pass++)
		{
			for (int engine = 0; engine < _restUntil.Length; engine++)
			{
				bool resting = DateTime.UtcNow < _restUntil[engine];
				if (resting == (pass == 1))
				{
					order.Add(engine);
				}
			}
		}
		foreach (int engine in order)
		{
			try
			{
				List<SearchResult> results = engine switch
				{
					0 => SearchDuck(query, region, safe, max),
					1 => SearchBrave(query, region, safe, max),
					_ => SearchWikipedia(query, region, max)
				};
				if (results.Count > 0)
				{
					return results;
				}
			}
			catch (AssistantException ex)
			{
				failure = ex;
				// Asked for a captcha or "too many requests": leave that engine alone for a while.
				if (ex.Message.IndexOf("captcha", StringComparison.OrdinalIgnoreCase) >= 0 || ex.Message.IndexOf("too many", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					_restUntil[engine] = DateTime.UtcNow.AddMinutes(3);
				}
				note?.Invoke(EngineNames[engine] + " unavailable (" + ex.Message + "), trying the next engine");
			}
		}
		if (failure != null)
		{
			throw failure;
		}
		return new List<SearchResult>();
	}

	/// <summary>At most a search a second, from any thread.</summary>
	private static void Pace()
	{
		TimeSpan wait;
		lock (_pace)
		{
			DateTime now = DateTime.UtcNow;
			DateTime start = (_nextSearch > now) ? _nextSearch : now;
			_nextSearch = start.AddMilliseconds(1000);
			wait = start - now;
		}
		if (wait > TimeSpan.Zero)
		{
			Thread.Sleep(wait);
		}
	}

	#region DuckDuckGo

	private static List<SearchResult> SearchDuck(string query, string region, int safe, int max)
	{
		string form = "q=" + Uri.EscapeDataString(query) + "&kl=" + Uri.EscapeDataString(string.IsNullOrEmpty(region) ? "wt-wt" : region) + "&kp=" + safe;
		string html = Fetch("https://html.duckduckgo.com/html/", form, "en-US");
		if (html.IndexOf("anomaly-modal", StringComparison.OrdinalIgnoreCase) >= 0 || html.IndexOf("challenge-form", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			throw new AssistantException("DuckDuckGo asked for a captcha");
		}
		List<SearchResult> results = new List<SearchResult>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		MatchCollection anchors = DuckAnchor.Matches(html);
		for (int i = 0; i < anchors.Count && results.Count < max; i++)
		{
			Match anchor = anchors[i];
			string url = DuckUrl(WebUtility.HtmlDecode(anchor.Groups["url"].Value));
			if (url == null || !seen.Add(url.TrimEnd('/')))
			{
				continue;
			}
			// The snippet sits between this result and the next one.
			int end = (i + 1 < anchors.Count) ? anchors[i + 1].Index : html.Length;
			Match snippet = DuckSnippet.Match(html, anchor.Index, end - anchor.Index);
			results.Add(new SearchResult
			{
				Url = url,
				Title = Clean(anchor.Groups["title"].Value),
				Snippet = snippet.Success ? Clean(snippet.Groups["text"].Value) : ""
			});
		}
		return results;
	}

	/// <summary>The real address of a result; null for ads.</summary>
	private static string DuckUrl(string url)
	{
		if (url.StartsWith("//", StringComparison.Ordinal))
		{
			url = "https:" + url;
		}
		int uddg = url.IndexOf("uddg=", StringComparison.Ordinal);
		if (url.Contains("duckduckgo.com/l/") && uddg >= 0)
		{
			string encoded = url.Substring(uddg + 5);
			int amp = encoded.IndexOf('&');
			url = Uri.UnescapeDataString((amp >= 0) ? encoded.Substring(0, amp) : encoded);
		}
		if (url.Contains("duckduckgo.com/y.js") || url.Contains("ad_domain=") || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		return url;
	}

	#endregion

	#region Brave

	private static List<SearchResult> SearchBrave(string query, string region, int safe, int max)
	{
		string url = "https://search.brave.com/search?q=" + Uri.EscapeDataString(query) + "&source=web&safesearch=" + (safe == 1 ? "strict" : (safe == -2 ? "off" : "moderate"));
		string language = (region != null && region.Length == 5 && region != "wt-wt") ? region.Substring(3, 2) : "en";
		string html = Fetch(url, null, language);
		List<SearchResult> results = new List<SearchResult>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string[] blocks = html.Split(new[] { "data-type=\"web\"" }, StringSplitOptions.None);
		for (int i = 1; i < blocks.Length && results.Count < max; i++)
		{
			string block = blocks[i];
			Match link = BraveLink.Match(block);
			if (!link.Success)
			{
				continue;
			}
			string address = WebUtility.HtmlDecode(link.Groups["url"].Value);
			if (address.Contains("brave.com/") || !seen.Add(address.TrimEnd('/')))
			{
				continue;
			}
			Match title = BraveTitle.Match(block);
			Match snippet = BraveSnippet.Match(block);
			results.Add(new SearchResult
			{
				Url = address,
				Title = title.Success ? Clean(title.Groups["text"].Value) : address,
				Snippet = snippet.Success ? Clean(snippet.Groups["text"].Value) : ""
			});
		}
		if (results.Count == 0 && html.IndexOf("captcha", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			throw new AssistantException("Brave asked for a captcha");
		}
		return results;
	}

	#endregion

	#region Brave API

	/// <summary>Optional key of the Brave Search API (search.brave.com/api, free plan).</summary>
	public static string BraveApiKey = "";

	private static List<SearchResult> SearchBraveApi(string query, string region, int safe, int max)
	{
		string url = "https://api.search.brave.com/res/v1/web/search?q=" + Uri.EscapeDataString(query) + "&count=" + Math.Min(max, 20) + "&safesearch=" + (safe == 1 ? "strict" : (safe == -2 ? "off" : "moderate"));
		if (region != null && region.Length == 5 && region != "wt-wt")
		{
			url += "&country=" + region.Substring(0, 2) + "&search_lang=" + region.Substring(3, 2);
		}
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		string body;
		try
		{
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			request.Accept = "application/json";
			request.Headers["X-Subscription-Token"] = BraveApiKey.Trim();
			request.UserAgent = "IsaClient/" + IsaClient.Core.ClientInfo.Version + " (Terraria client assistant)";
			request.Timeout = 12000;
			request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
			using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			using StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
			body = reader.ReadToEnd();
		}
		catch (WebException ex)
		{
			int code = (ex.Response is HttpWebResponse response) ? (int)response.StatusCode : 0;
			throw new AssistantException(code == 401 || code == 403 ? "the Brave API key was refused" : (code == 429 ? "the Brave API limit was reached" : ("error " + ex.Status)));
		}
		List<SearchResult> results = new List<SearchResult>();
		foreach (Newtonsoft.Json.Linq.JToken hit in (Newtonsoft.Json.Linq.JObject.Parse(body)["web"]?["results"] as Newtonsoft.Json.Linq.JArray) ?? new Newtonsoft.Json.Linq.JArray())
		{
			string link = (string)hit["url"];
			if (string.IsNullOrEmpty(link))
			{
				continue;
			}
			results.Add(new SearchResult
			{
				Url = link,
				Title = Clean((string)hit["title"] ?? link),
				Snippet = Clean(((string)hit["description"] ?? "") + " " + string.Join(" ", ((hit["extra_snippets"] as Newtonsoft.Json.Linq.JArray) ?? new Newtonsoft.Json.Linq.JArray()).Select(e => (string)e)))
			});
		}
		return results;
	}

	#endregion

	#region Terraria wiki

	/// <summary>The official Terraria wiki's own search (terraria.wiki.gg): exact game data, never a captcha.</summary>
	public static List<SearchResult> SearchTerrariaWiki(string query, int max = 5)
	{
		string url = "https://terraria.wiki.gg/api.php?action=query&list=search&format=json&srlimit=" + max + "&srsearch=" + Uri.EscapeDataString(query);
		Newtonsoft.Json.Linq.JObject data = Newtonsoft.Json.Linq.JObject.Parse(FetchApi(url));
		List<SearchResult> results = new List<SearchResult>();
		foreach (Newtonsoft.Json.Linq.JToken hit in (data["query"]?["search"] as Newtonsoft.Json.Linq.JArray) ?? new Newtonsoft.Json.Linq.JArray())
		{
			string title = (string)hit["title"] ?? "";
			results.Add(new SearchResult
			{
				Title = title + " - Terraria Wiki",
				Url = "https://terraria.wiki.gg/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_')),
				Snippet = Clean((string)hit["snippet"] ?? "")
			});
		}
		return results;
	}

	#endregion

	#region Wikipedia

	/// <summary>Wikipedia's own search (in the region's language, else English): always there, good for general knowledge.</summary>
	private static List<SearchResult> SearchWikipedia(string query, string region, int max)
	{
		string language = (region != null && region.Length == 5 && region != "wt-wt") ? region.Substring(3, 2) : "en";
		string url = "https://" + language + ".wikipedia.org/w/api.php?action=query&list=search&format=json&srlimit=" + Math.Min(max, 6) + "&srsearch=" + Uri.EscapeDataString(query);
		Newtonsoft.Json.Linq.JObject data = Newtonsoft.Json.Linq.JObject.Parse(FetchApi(url));
		List<SearchResult> results = new List<SearchResult>();
		foreach (Newtonsoft.Json.Linq.JToken hit in (data["query"]?["search"] as Newtonsoft.Json.Linq.JArray) ?? new Newtonsoft.Json.Linq.JArray())
		{
			string title = (string)hit["title"] ?? "";
			results.Add(new SearchResult
			{
				Title = title + " - Wikipedia",
				Url = "https://" + language + ".wikipedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_')),
				Snippet = Clean((string)hit["snippet"] ?? "")
			});
		}
		return results;
	}

	#endregion

	/// <summary>MediaWiki APIs (the Terraria wiki, Wikipedia) ask clients to name themselves in the User-Agent.</summary>
	private static string FetchApi(string url)
	{
		return Fetch(url, null, "en", "IsaClient/" + IsaClient.Core.ClientInfo.Version + " (Terraria client assistant)", "application/json");
	}

	/// <summary>GET (or POST with <paramref name="form"/>) a results page.</summary>
	private static string Fetch(string url, string form, string language, string agent = UserAgent, string accept = "text/html,application/xhtml+xml")
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		try
		{
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			request.UserAgent = agent;
			request.Accept = accept;
			request.Headers[HttpRequestHeader.AcceptLanguage] = language + ",en;q=0.8";
			request.Timeout = 12000;
			request.ReadWriteTimeout = 12000;
			request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
			if (form != null)
			{
				request.Method = "POST";
				request.ContentType = "application/x-www-form-urlencoded";
				byte[] data = Encoding.UTF8.GetBytes(form);
				using Stream stream = request.GetRequestStream();
				stream.Write(data, 0, data.Length);
			}
			using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			using StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
			string html = reader.ReadToEnd();
			// DuckDuckGo answers a blocked search with 202 and a captcha page.
			if ((int)response.StatusCode == 202 && form != null)
			{
				throw new AssistantException("DuckDuckGo asked for a captcha");
			}
			return html;
		}
		catch (WebException ex)
		{
			int code = (ex.Response is HttpWebResponse response) ? (int)response.StatusCode : 0;
			throw new AssistantException(code == 429 ? "the search engine says too many requests" : ("the web search failed (" + ex.Status + ")"));
		}
	}

	private static string Clean(string html)
	{
		return Regex.Replace(WebUtility.HtmlDecode(Tags.Replace(html, " ")), "\\s+", " ").Trim();
	}
}
