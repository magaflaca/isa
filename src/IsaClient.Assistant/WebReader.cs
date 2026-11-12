using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace IsaClient.Assistant;

/// <summary>
/// Opens a web page and keeps what a person would read: the main text without menus, scripts, styles or footers.
/// <see cref="Passages"/> then keeps only the parts that talk about the question, so the model reads little and well.
/// </summary>
internal static class WebReader
{
	private const int MaxBytes = 2_500_000;

	private static readonly Regex Junk = new Regex("<(script|style|noscript|svg|template|iframe|nav|footer|header|aside|form|button|select)\\b[^>]*>.*?</\\1\\s*>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

	private static readonly Regex Comments = new Regex("<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);

	private static readonly Regex Blocks = new Regex("<(?:/?(?:p|div|li|tr|h[1-6]|section|article|table|ul|ol|dl|dt|dd|blockquote|pre)|br)\\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	private static readonly Regex Cells = new Regex("</t[dh]\\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	private static readonly Regex Tags = new Regex("<[^>]+>", RegexOptions.Compiled);

	private static readonly Regex Title = new Regex("<title[^>]*>(?<t>.*?)</title>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

	private static readonly Regex Charset = new Regex("charset=[\"']?(?<c>[\\w-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	private static readonly Regex Words = new Regex("[\\p{L}\\p{N}][\\p{L}\\p{N}.,'-]*", RegexOptions.Compiled);

	private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"the", "a", "an", "of", "to", "in", "on", "for", "and", "or", "is", "are", "was", "what", "which", "who", "how", "does", "do", "with", "by", "at", "from", "it", "this", "that",
		"el", "la", "los", "las", "un", "una", "de", "del", "en", "y", "o", "que", "como", "cual", "cuales", "es", "son", "por", "para", "con", "se", "al", "lo", "su", "sus", "hay", "tiene", "esta"
	};

	/// <summary>The readable text of <paramref name="url"/> and its title; throws on failure.</summary>
	public static string Read(string url, out string title)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		Match wiki = Regex.Match(url, "^https?://terraria\\.wiki\\.gg/wiki/(?<page>[^#?]+)", RegexOptions.IgnoreCase);
		if (wiki.Success)
		{
			return ReadTerrariaWiki(Uri.UnescapeDataString(wiki.Groups["page"].Value), out title);
		}
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
		request.UserAgent = WebSearch.UserAgent;
		request.Accept = "text/html,application/xhtml+xml,text/plain";
		request.Headers[HttpRequestHeader.AcceptLanguage] = "en,es;q=0.8";
		request.Timeout = 9000;
		request.ReadWriteTimeout = 9000;
		request.AllowAutoRedirect = true;
		request.MaximumAutomaticRedirections = 5;
		request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
		string type = response.ContentType ?? "";
		if (type.Length > 0 && type.IndexOf("html", StringComparison.OrdinalIgnoreCase) < 0 && type.IndexOf("text/plain", StringComparison.OrdinalIgnoreCase) < 0)
		{
			throw new IOException("not a web page (" + type + ")");
		}
		byte[] data = ReadLimited(response.GetResponseStream());
		string html = Decode(data, response.CharacterSet);
		Match t = Title.Match(html);
		title = t.Success ? Collapse(WebUtility.HtmlDecode(t.Groups["t"].Value)) : "";
		return type.IndexOf("text/plain", StringComparison.OrdinalIgnoreCase) >= 0 ? html : ToText(MainPart(html));
	}

	/// <summary>A page of the official Terraria wiki through its API (the rendered article, no menus).</summary>
	private static string ReadTerrariaWiki(string page, out string title)
	{
		string url = "https://terraria.wiki.gg/api.php?action=parse&format=json&prop=text&redirects=1&disableeditsection=1&page=" + Uri.EscapeDataString(page);
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
		request.UserAgent = "IsaClient/" + IsaClient.Core.ClientInfo.Version + " (Terraria client assistant)";
		request.Accept = "application/json";
		request.Timeout = 9000;
		request.ReadWriteTimeout = 9000;
		request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
		Newtonsoft.Json.Linq.JObject data = Newtonsoft.Json.Linq.JObject.Parse(Encoding.UTF8.GetString(ReadLimited(response.GetResponseStream())));
		title = ((string)data["parse"]?["title"] ?? page.Replace('_', ' ')) + " - Terraria Wiki";
		string html = (string)data["parse"]?["text"]?["*"];
		if (string.IsNullOrEmpty(html))
		{
			throw new IOException("the wiki has no page " + page);
		}
		return ToText(html);
	}

	private static byte[] ReadLimited(Stream stream)
	{
		using MemoryStream memory = new MemoryStream();
		byte[] buffer = new byte[16384];
		int read;
		while ((read = stream.Read(buffer, 0, buffer.Length)) > 0 && memory.Length < MaxBytes)
		{
			memory.Write(buffer, 0, read);
		}
		return memory.ToArray();
	}

	private static string Decode(byte[] data, string headerCharset)
	{
		Encoding encoding = Encoding.UTF8;
		string head = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 4096));
		Match meta = Charset.Match(head);
		string name = meta.Success ? meta.Groups["c"].Value : headerCharset;
		if (!string.IsNullOrEmpty(name))
		{
			try
			{
				encoding = Encoding.GetEncoding(name);
			}
			catch
			{
			}
		}
		return encoding.GetString(data);
	}

	/// <summary>The article of the page when it has one (wikis, news), else the body.</summary>
	private static string MainPart(string html)
	{
		html = Comments.Replace(html, " ");
		foreach (string marker in new[] { "id=\"mw-content-text\"", "<article", "<main", "id=\"content\"", "<body" })
		{
			int at = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
			if (at >= 0)
			{
				string part = html.Substring(at);
				int stop = part.IndexOf("class=\"printfooter\"", StringComparison.OrdinalIgnoreCase);
				return (stop > 0) ? part.Substring(0, stop) : part;
			}
		}
		return html;
	}

	private static string ToText(string html)
	{
		html = Junk.Replace(html, " ");
		html = Cells.Replace(html, " | ");
		html = Blocks.Replace(html, "\n");
		string text = WebUtility.HtmlDecode(Tags.Replace(html, " "));
		StringBuilder result = new StringBuilder(text.Length / 2);
		foreach (string raw in text.Split('\n'))
		{
			string line = Collapse(raw).Trim(' ', '|');
			if (line.Length >= 2)
			{
				result.Append(line).Append('\n');
			}
		}
		return result.ToString();
	}

	private static string Collapse(string text)
	{
		return Regex.Replace(text, "[\\s\\u00a0]+", " ").Trim();
	}

	/// <summary>
	/// The parts of <paramref name="text"/> that best match <paramref name="terms"/>, in page order, up to
	/// <paramref name="budget"/> characters. Short pieces are joined into passages of a few hundred characters.
	/// </summary>
	public static string Passages(string text, IEnumerable<string> terms, int budget)
	{
		if (string.IsNullOrEmpty(text) || text.Length <= budget)
		{
			return text ?? "";
		}
		HashSet<string> keys = new HashSet<string>(terms.SelectMany(Terms), StringComparer.OrdinalIgnoreCase);
		List<string> chunks = new List<string>();
		StringBuilder chunk = new StringBuilder();
		foreach (string line in text.Split('\n'))
		{
			if (chunk.Length > 0 && chunk.Length + line.Length > 600)
			{
				chunks.Add(chunk.ToString());
				chunk.Clear();
			}
			chunk.Append(line).Append('\n');
		}
		if (chunk.Length > 0)
		{
			chunks.Add(chunk.ToString());
		}
		// Words in every passage (the page's own subject) say little; rare ones ("summon") say where the answer is.
		string[] lowered = chunks.Select(c => c.ToLowerInvariant()).ToArray();
		Dictionary<string, double> weight = new Dictionary<string, double>();
		foreach (string key in keys)
		{
			int containing = lowered.Count(c => c.Contains(key));
			weight[key] = (containing == 0) ? 0.0 : Math.Log(1.0 + (double)chunks.Count / containing) * (key.Any(char.IsDigit) ? 2.0 : 1.0);
		}
		List<(int Index, double Score)> scored = new List<(int, double)>();
		for (int i = 0; i < chunks.Count; i++)
		{
			double score = 0;
			string lower = lowered[i];
			foreach (string key in keys)
			{
				int count = 0;
				int at = 0;
				while ((at = lower.IndexOf(key, at, StringComparison.Ordinal)) >= 0 && count < 4)
				{
					count++;
					at += key.Length;
				}
				// The first mention counts most.
				score += (count == 0) ? 0.0 : weight[key] * (1.0 + 0.3 * (count - 1));
			}
			// The top of a page usually says what it is about.
			if (i < 2)
			{
				score += 1.5;
			}
			scored.Add((i, score));
		}
		HashSet<int> keep = new HashSet<int>();
		int used = 0;
		foreach ((int index, double score) in scored.OrderByDescending(s => s.Score))
		{
			if (score <= 0 && keep.Count > 0)
			{
				break;
			}
			if (used + chunks[index].Length > budget)
			{
				continue;
			}
			keep.Add(index);
			used += chunks[index].Length;
		}
		StringBuilder result = new StringBuilder();
		int last = -2;
		foreach (int index in keep.OrderBy(i => i))
		{
			if (index != last + 1 && result.Length > 0)
			{
				result.Append("...\n");
			}
			result.Append(chunks[index]);
			last = index;
		}
		return result.ToString();
	}

	private static IEnumerable<string> Terms(string text)
	{
		foreach (Match word in Words.Matches(text ?? ""))
		{
			string w = word.Value.Trim('.', ',', '\'', '-').ToLowerInvariant();
			if (w.Length >= 3 && !StopWords.Contains(w))
			{
				yield return w;
			}
		}
	}
}
