using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace IsaClient.Assistant;

/// <summary>
/// The whole answer in one call: a gpt-oss model with Groq's built-in browser searches, opens pages and searches
/// again by itself on Groq's servers (no captchas, nothing from this computer), then writes the answer citing the
/// pages by address. The addresses become numbered, clickable sources.
/// </summary>
internal static class GroqBrowser
{
	public sealed class Outcome
	{
		public string Answer;

		public List<AskSource> Sources = new List<AskSource>();

		public List<string> Queries = new List<string>();

		public List<string> Opened = new List<string>();
	}

	/// <summary>Citation marks of the browser tool: 【1†L4-L5】.</summary>
	private static readonly Regex Marks = new Regex("【[^】]*】", RegexOptions.Compiled);

	private static readonly Regex Cited = new Regex("\\[(?<u>https?://[^\\]\\s]+)\\]|\\((?<u>https?://[^)\\s]+)\\)|(?<u>https?://[^\\s)\\]<>\"]+)", RegexOptions.Compiled);

	private static readonly Regex SourcesLine = new Regex("^\\s*(?:[-*]\\s*)?(?:sources?|fuentes?|references?|referencias|links?|enlaces)\\s*:[^\\n]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

	private static readonly Regex OpenedUrl = new Regex("URL: (?<u>https?://\\S+)", RegexOptions.Compiled);

	private static readonly Regex Titles = new Regex("'title': (['\"])(?<t>.*?)\\1, 'url': (['\"])(?<u>.*?)\\3", RegexOptions.Compiled);

	public static Outcome Answer(string question, AskOptions options, string today, string model, string effort, bool mustSearch)
	{
		StringBuilder system = new StringBuilder();
		system.Append("You are ").Append(options.Name).Append(", a helpful assistant inside the game Terraria (the player uses Terraria 1.4.5.8 on PC). Today is ").Append(today).Append(".\n");
		system.Append("When the question needs facts, use the browser: search, open the most relevant pages (two or three, official and well-known ones first), and search again with other words if needed. ");
		system.Append("Read them carefully and answer only from what they say, never from memory: report exactly what a page states (what was added is not what was changed or fixed, a rumor is not a fact) and add no guesses or explanations of your own. If you couldn't find something, say so briefly. Never invent names, numbers, dates or stats. When facts change over time, say the date they refer to.\n");
		system.Append("Cite each fact with the full address of its page in square brackets right after it, like [https://example.com/page]. Don't add a list of sources at the end.\n");
		system.Append("Language: ").Append(options.Language).Append(". Tone: ").Append(options.Tone).Append(".\n");
		system.Append("Format: plain text for a small game chat box. No markdown: no headings, bold, tables or code. Short paragraphs; lists as lines starting with \"- \" (a comparison too: one line per item). Go straight to the answer. ");
		system.Append("Keep it under ").Append(options.MaxChars).Append(" characters.");
		if (!string.IsNullOrWhiteSpace(options.Instructions))
		{
			system.Append("\nThe player's own instructions: ").Append(options.Instructions.Trim());
		}
		StringBuilder user = new StringBuilder();
		if (options.History.Count > 0)
		{
			user.Append("Earlier in this conversation:\n");
			foreach ((string q, string a) in options.History)
			{
				user.Append("Player: ").Append(q).Append("\nAssistant: ").Append(a.Length > 400 ? a.Substring(0, 400) + "..." : a).Append('\n');
			}
			user.Append('\n');
		}
		user.Append(question);
		string reply = GroqClient.CompleteWithBrowser(model, system.ToString(), user.ToString(), 2500, effort, mustSearch, out JArray tools);
		return Build(reply, tools, options);
	}

	private static Outcome Build(string reply, JArray tools, AskOptions options)
	{
		Outcome outcome = new Outcome();
		Dictionary<string, string> titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		List<string> found = new List<string>();
		foreach (JToken tool in tools)
		{
			string name = (string)tool["name"] ?? (string)tool["type"] ?? "";
			if (name.EndsWith("search", StringComparison.OrdinalIgnoreCase))
			{
				string query = (string)GroqClient.ParseJson(tool["arguments"]?.ToString())?["query"];
				if (!string.IsNullOrWhiteSpace(query))
				{
					outcome.Queries.Add(query);
				}
			}
			bool opened = name.EndsWith("open", StringComparison.OrdinalIgnoreCase);
			if (opened)
			{
				Match page = OpenedUrl.Match(tool["output"]?.ToString() ?? "");
				if (page.Success)
				{
					outcome.Opened.Add(page.Groups["u"].Value);
				}
			}
			foreach (Match hit in Titles.Matches(tool["search_results"]?.ToString() ?? ""))
			{
				string url = hit.Groups["u"].Value;
				if (opened)
				{
					outcome.Opened.Add(url);
				}
				else
				{
					found.Add(url);
				}
				if (!titles.ContainsKey(url) || !opened)
				{
					titles[url] = Regex.Replace(hit.Groups["t"].Value, "\\s*-\\s*viewing lines.*$", "");
				}
			}
		}

		// Addresses become [1], [2]... in order of first use; the cleaner then writes them as (1), (2).
		List<string> urls = new List<string>();
		string text = Marks.Replace(reply ?? "", "");
		text = Cited.Replace(text, delegate(Match m)
		{
			string url = m.Groups["u"].Value.TrimEnd('.', ',', ';', ':', '/');
			int index = urls.FindIndex(u => string.Equals(u, url, StringComparison.OrdinalIgnoreCase));
			if (index < 0)
			{
				urls.Add(url);
				index = urls.Count - 1;
			}
			return "[" + (index + 1) + "]";
		});
		text = SourcesLine.Replace(text, "");
		outcome.Answer = AnswerFormat.Limit(AnswerFormat.Clean(text), (int)(options.MaxChars * 1.4));
		// No addresses written: the pages it opened are the sources (or the best search results).
		if (urls.Count == 0)
		{
			urls.AddRange((outcome.Opened.Count > 0 ? outcome.Opened : found).Select(u => u.TrimEnd('/')).Where(u => !u.Contains("exa.ai/search")).Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
		}
		for (int i = 0; i < urls.Count; i++)
		{
			string title = titles.FirstOrDefault(t => string.Equals(t.Key.TrimEnd('/'), urls[i], StringComparison.OrdinalIgnoreCase)).Value;
			AskNote page = new AskNote { Url = urls[i] };
			outcome.Sources.Add(new AskSource { Number = i + 1, Url = urls[i], Title = string.IsNullOrEmpty(title) ? page.Domain : title, Domain = page.Domain });
		}
		return outcome;
	}
}
