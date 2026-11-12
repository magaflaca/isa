using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace IsaClient.Assistant;

/// <summary>What one question is answered with (a copy of the settings when it was asked).</summary>
public sealed class AskOptions
{
	public string AnswerModel;

	public string RouterModel;

	/// <summary>"auto", "always" or "never".</summary>
	public string SearchMode;

	/// <summary>The assistant's name (the player can change it).</summary>
	public string Name = "Isa";

	/// <summary>"groq": Groq's built-in browser (it searches on Groq's servers); "free": DuckDuckGo, Brave and Wikipedia from here.</summary>
	public string Engine = "groq";

	/// <summary>"the same language as the question", "Spanish"...</summary>
	public string Language;

	public string Tone;

	/// <summary>Characters the answer should fit in.</summary>
	public int MaxChars;

	/// <summary>Pages read per research round.</summary>
	public int MaxPages = 3;

	/// <summary>Research rounds at most (another round when the notes don't answer the question yet).</summary>
	public int MaxRounds = 3;

	public string Region;

	public int SafeSearch;

	public string Instructions;

	public List<(string Question, string Answer)> History = new List<(string, string)>();

	/// <summary>Pictures that came with the question (attached in the chat, or links in it).</summary>
	public List<AskImage> Images = new List<AskImage>();

	/// <summary>What the pictures show, as the vision model wrote it (set while answering).</summary>
	public string ImageNotes;
}

/// <summary>A page or search hit the answer is based on.</summary>
public sealed class AskSource
{
	public int Number;

	public string Title;

	public string Url;

	public string Domain;
}

/// <summary>What was learned from one page or tool.</summary>
public sealed class AskNote
{
	public string Title;

	public string Url;

	public string Text;

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

public sealed class AskResult
{
	public string Answer;

	public List<AskSource> Sources = new List<AskSource>();

	public bool Searched;

	public List<string> Queries = new List<string>();

	public int Rounds;

	public int PagesRead;
}

/// <summary>
/// Answers a question with the web, spending as few tokens as it can:
/// 0. main path, one call: a gpt-oss model with Groq's built-in browser searches, reads and writes the answer (it
///    skips the search for small talk). Daily token limits are tracked per model: a spent model is skipped.
/// Backup, step by step, when no browser model is available (or "Free engines" is picked):
/// 1. plan: search or not, search queries, and official Terraria wiki pages for game questions;
/// 2. search (DuckDuckGo, Brave, Wikipedia; the Terraria wiki's own search) and choose the pages worth opening;
/// 3. read them at once: a small model pulls out of each page the facts about the question (or says it has none);
/// 4. check: do the notes answer it? If not, search again with new queries (more rounds with a deeper research);
/// 5. answer from the notes alone, citing them, and saying so when something wasn't found.
/// </summary>
internal static class AskEngine
{
	/// <summary>Characters of a page handed to the reader model (passages about the question).</summary>
	private const int PageBudget = 6500;

	private const string SmallModel = "openai/gpt-oss-20b";

	private const string BigModel = "openai/gpt-oss-120b";

	/// <summary>"terraria" and "wiki" in a query (left out when the query goes to the Terraria wiki's own search).</summary>
	private static readonly Regex TerrariaWord = new Regex("\\b(?:terraria|wiki)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	public static AskResult Run(string question, AskOptions options, Action<string> progress)
	{
		AskResult result = new AskResult();
		string today = DateTime.Now.ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " (UTC" + TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).ToString("hh\\:mm") + ")";
		List<AskNote> notes = new List<AskNote>();

		// Pictures: the vision model writes down what they show, and that goes with the question to every model that
		// can't see. Only skipped when the answering model sees them and nothing searches.
		bool answerSees = AskVision.CanSee(options.AnswerModel);
		if (options.Images.Count > 0 && !(answerSees && options.SearchMode == "never"))
		{
			progress?.Invoke(IsaClient.Languages.L10n.F("Looking at {0}", AskVision.Tags(options.Images.Count)));
			try
			{
				options.ImageNotes = AskVision.Describe(options.Images, question);
			}
			catch (AssistantException ex)
			{
				if (!answerSees)
				{
					throw new AssistantException("couldn't look at the pictures: " + ex.Message);
				}
			}
			if (!string.IsNullOrWhiteSpace(options.ImageNotes))
			{
				question = question + "\n\nThe user attached pictures. What they show (a vision model looked at them):\n" + options.ImageNotes;
			}
		}

		// 0. Groq's browser: research and answer in one call. The steps below are the backup.
		if (options.Engine == "groq" && options.SearchMode != "never")
		{
			AskResult direct = BrowserAnswer(question, options, today, progress);
			if (direct != null)
			{
				return direct;
			}
		}

		// 1. Plan
		bool search = options.SearchMode != "never";
		List<string> queries = new List<string> { question };
		List<string> wiki = new List<string>();
		JObject plan = (options.SearchMode == "never") ? null : Plan(question, options, today);
		if (plan != null)
		{
			if (options.SearchMode == "auto" && (bool?)plan["search"] == false)
			{
				search = false;
			}
			List<string> planned = Strings(plan["queries"], 2);
			if (planned.Count > 0)
			{
				queries = planned;
			}
			wiki = Strings(plan["wiki"], 3);
		}

		if (search)
		{
			result.Searched = true;
			// About Terraria: the official wiki is read at the same time (its own search, by API).
			if (wiki.Count == 0 && (question + " " + string.Join(" ", queries)).IndexOf("terraria", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				wiki = queries.Select(q => TerrariaWord.Replace(q, " ").Trim()).Where(q => q.Length > 1).Take(1).ToList();
			}
			Task<List<AskNote>> wikiNotes = (wiki.Count > 0) ? Task.Run(() => ReadWiki(question, wiki, today, options)) : null;
			// The wiki pages are already being read: the free engines only search the web.
			Research(question, options, today, queries, new List<string>(), notes, result, progress);
			if (wikiNotes != null)
			{
				List<AskNote> fromWiki = wikiNotes.Result.Where(w => !notes.Any(n => SameUrl(n.Url, w.Url))).ToList();
				notes.InsertRange(0, fromWiki);
				result.PagesRead += fromWiki.Count;
			}
		}

		// 5. Answer
		List<AskNote> numbered = notes.ToList();
		string system = AnswerSystem(options, today, result.Searched || numbered.Count > 0);
		string user = AnswerUser(question, options, numbered, result);
		string reply;
		if (options.Images.Count > 0 && answerSees)
		{
			// A model that sees gets the pictures themselves too.
			JArray messages = new JArray
			{
				new JObject { ["role"] = "system", ["content"] = system },
				new JObject { ["role"] = "user", ["content"] = AskVision.WithImages(user, options.Images) }
			};
			reply = GroqClient.CompleteMessages(options.AnswerModel, messages, 2500, 0.25f);
		}
		else
		{
			reply = GroqClient.Complete(options.AnswerModel, system, user, 2500, 0.25f);
		}
		string answer = AnswerFormat.Clean(reply);
		result.Answer = AnswerFormat.Limit(answer, (int)(options.MaxChars * 1.4));
		for (int i = 0; i < numbered.Count; i++)
		{
			int n = i + 1;
			// Only the sources the answer cites (all of them if it cites none).
			if (Regex.IsMatch(result.Answer, "\\((?:\\d{1,2}, )*" + n + "(?:, \\d{1,2})*\\)"))
			{
				result.Sources.Add(new AskSource { Number = n, Title = numbered[i].Title, Url = numbered[i].Url, Domain = numbered[i].Domain });
			}
		}
		if (result.Sources.Count == 0)
		{
			result.Sources = numbered.Select((note, i) => new AskSource { Number = i + 1, Title = note.Title, Url = note.Url, Domain = note.Domain }).ToList();
		}
		return result;
	}

	#region Research

	/// <summary>
	/// One call to a gpt-oss model with Groq's browser (the big one first, the small one when it is out of tokens). Null when no
	/// browser model is available: then the step-by-step research with the free engines answers.
	/// </summary>
	private static AskResult BrowserAnswer(string question, AskOptions options, string today, Action<string> progress)
	{
		string effort = (options.MaxRounds >= 4) ? "high" : ((options.MaxRounds >= 3) ? "medium" : "low");
		// The big model reads better; the small one (its own daily tokens) takes over when the big one is spent.
		foreach (string model in new[] { BigModel, SmallModel }.Where(GroqClient.IsAvailable))
		{
			try
			{
				GroqBrowser.Outcome outcome = GroqBrowser.Answer(question, options, today, model, effort, options.SearchMode == "always");
				if (string.IsNullOrWhiteSpace(outcome.Answer))
				{
					continue;
				}
				AskResult result = new AskResult
				{
					Answer = outcome.Answer,
					Sources = outcome.Sources,
					Searched = outcome.Queries.Count > 0,
					Queries = outcome.Queries,
					Rounds = 1,
					PagesRead = outcome.Opened.Distinct().Count()
				};
				if (result.Searched)
				{
					foreach (string query in outcome.Queries.Distinct(StringComparer.OrdinalIgnoreCase).Take(3))
					{
						progress?.Invoke("Searched \"" + query + "\"");
					}
					if (outcome.Opened.Count > 0)
					{
						progress?.Invoke("Read " + string.Join(", ", outcome.Opened.Select(Domain).Distinct().Take(4)));
					}
				}
				return result;
			}
			catch (AssistantException)
			{
				// Quietly on to the next model (or the backup).
			}
		}
		return null;
	}

	/// <summary>The official Terraria wiki pages for <paramref name="titles"/>, read by a small model.</summary>
	private static List<AskNote> ReadWiki(string question, List<string> titles, string today, AskOptions options)
	{
		List<AskNote> notes = new List<AskNote>();
		try
		{
			List<SearchResult> pages = new List<SearchResult>();
			foreach (string title in titles.Take(3))
			{
				SearchResult page = WebSearch.SearchTerrariaWiki(title, 1).FirstOrDefault();
				if (page != null && !pages.Any(p => SameUrl(p.Url, page.Url)))
				{
					pages.Add(page);
				}
			}
			AskNote[] read = new AskNote[pages.Count];
			Parallel.For(0, pages.Count, i => read[i] = ReadPage(question, pages[i], today, (i % 2 == 0) ? SmallModel : Other(SmallModel)));
			notes.AddRange(read.Where(n => n != null));
		}
		catch
		{
		}
		return notes;
	}

	private static string Domain(string url)
	{
		return new AskNote { Url = url }.Domain;
	}

	private static void Research(string question, AskOptions options, string today, List<string> queries, List<string> wiki, List<AskNote> notes, AskResult result, Action<string> progress)
	{
		List<SearchResult> known = new List<SearchResult>();
		HashSet<string> read = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> usedQueries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> usedWiki = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		int rounds = Math.Max(1, Math.Min(4, options.MaxRounds));
		for (int round = 1; round <= rounds; round++)
		{
			result.Rounds = round;
			// Follow-up rounds search less: the engines limit how often one asks.
			queries = queries.Where(usedQueries.Add).Take(round == 1 ? 2 : 1).ToList();
			wiki = wiki.Where(usedWiki.Add).ToList();
			result.Queries.AddRange(queries);

			// 2. Search, new hits first
			if (queries.Count > 0 || wiki.Count > 0)
			{
				foreach (string query in queries)
				{
					progress?.Invoke("Searched \"" + query + "\"");
				}
				List<SearchResult> hits = SearchAll(queries, wiki, options);
				known.InsertRange(0, hits.Where(h => !known.Any(k => SameUrl(k.Url, h.Url))));
			}
			List<SearchResult> unread = known.Where(k => !read.Contains(Key(k.Url))).ToList();
			if (unread.Count == 0)
			{
				break;
			}

			// 3. Choose and read
			List<SearchResult> chosen = Choose(question, unread, notes, options);
			progress?.Invoke("Read " + string.Join(", ", chosen.Select(c => c.Domain).Distinct()));
			AskNote[] found = new AskNote[chosen.Count];
			Parallel.For(0, chosen.Count, new ParallelOptions { MaxDegreeOfParallelism = 5 }, delegate(int i)
			{
				found[i] = ReadPage(question, chosen[i], today, (i % 2 == 0) ? options.RouterModel : Other(options.RouterModel));
			});
			foreach (SearchResult page in chosen)
			{
				read.Add(Key(page.Url));
			}
			result.PagesRead += chosen.Count;
			notes.AddRange(found.Where(n => n != null));

			// 4. Enough?
			if (round == rounds)
			{
				break;
			}
			JObject verdict = Evaluate(question, notes, usedQueries, today, options);
			if (verdict == null || (bool?)verdict["answered"] != false)
			{
				break;
			}
			queries = Strings(verdict["queries"], 2);
			wiki = Strings(verdict["wiki"], 2);
			string missing = ((string)verdict["missing"] ?? "").Trim();
			bool moreToRead = known.Any(k => !read.Contains(Key(k.Url)));
			if (queries.All(usedQueries.Contains) && wiki.All(usedWiki.Contains) && !moreToRead)
			{
				break;
			}
			// Another round, quietly.
		}

		// The snippets of hits that weren't read often hold the fact too (prices, dates).
		List<SearchResult> rest = known.Where(k => !read.Contains(Key(k.Url)) && !string.IsNullOrWhiteSpace(k.Snippet)).Take(6).ToList();
		if (rest.Count > 0)
		{
			notes.Add(new AskNote
			{
				Title = "Other search results",
				Url = rest[0].Url,
				Text = string.Join("\n", rest.Select(r => "- " + r.Title + " (" + r.Domain + "): " + r.Snippet))
			});
		}
	}

	private static List<SearchResult> SearchAll(List<string> queries, List<string> wiki, AskOptions options)
	{
		// Terraria questions also search the official wiki itself, a page per title (its hits go first).
		List<SearchResult>[] lists = new List<SearchResult>[wiki.Count + queries.Count];
		Parallel.For(0, lists.Length, delegate(int i)
		{
			try
			{
				lists[i] = (i < wiki.Count) ? WebSearch.SearchTerrariaWiki(wiki[i], 2) : WebSearch.Search(queries[i - wiki.Count], options.Region, options.SafeSearch);
			}
			catch (AssistantException)
			{
				lists[i] = new List<SearchResult>();
			}
		});
		// Interleave: the best hit of every list first.
		List<SearchResult> merged = new List<SearchResult>();
		for (int rank = 0; rank < 10; rank++)
		{
			foreach (List<SearchResult> list in lists)
			{
				if (rank < list.Count && !merged.Any(m => SameUrl(m.Url, list[rank].Url)))
				{
					merged.Add(list[rank]);
				}
			}
		}
		return merged.Take(20).ToList();
	}

	/// <summary>Opens a page and has a small model write down what it says about the question (null: nothing useful).</summary>
	private static AskNote ReadPage(string question, SearchResult page, string today, string model)
	{
		string text;
		try
		{
			text = WebReader.Read(page.Url, out string title);
			if (!string.IsNullOrWhiteSpace(title))
			{
				page.Title = title;
			}
		}
		catch
		{
			text = null;
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			// Couldn't open it: its snippet is all there is.
			return string.IsNullOrWhiteSpace(page.Snippet) ? null : new AskNote { Title = page.Title, Url = page.Url, Text = "(search snippet) " + page.Snippet };
		}
		string passages = WebReader.Passages(text, new[] { question }, PageBudget);
		string system = "You read a web page for a research assistant. Today is " + today + ".\n" +
			"Write down every fact on the page that helps answer the question: exact names, numbers, prices, stats, dates, conditions, and complete lists (keep every item). Note the date the information refers to when the page gives one.\n" +
			"Copy the facts faithfully. Never add anything that isn't on the page. If the page has nothing that helps, reply exactly: NONE\n" +
			"Plain text, short lines, at most 1000 characters.";
		string user = "Question: " + question + "\n\nPage: " + page.Title + " (" + page.Url + ")\n\n" + passages;
		try
		{
			string facts = GroqClient.Complete(model, system, user, 1500, 0.1f).Trim();
			if (facts.Length < 8 || facts.StartsWith("NONE", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			return new AskNote { Title = page.Title, Url = page.Url, Text = Shorten(facts, 1400) };
		}
		catch
		{
			// The reader model is busy: hand over the passages themselves, shorter.
			return new AskNote { Title = page.Title, Url = page.Url, Text = Shorten(passages, 1800) };
		}
	}

	#endregion

	#region Models

	private static JObject Plan(string question, AskOptions options, string today)
	{
		string system = "You plan the research for an assistant inside the game Terraria (the player runs Terraria 1.4.5.8 on PC). Today is " + today + ".\n" +
			"Reply with JSON only: {\"search\": true|false, \"queries\": [\"...\"], \"wiki\": [\"...\"]}.\n" +
			"search: true for anything factual or current. The answering model has NO reliable knowledge of its own, so when in doubt, search. false only for greetings, small talk, pure arithmetic, rewriting a given text, or creative writing.\n" +
			"queries: 1 or 2 short, standalone web search queries, the way a person would type them (resolve 'it' or 'that' with the conversation). Use the language with the best sources for the topic.\n" +
			"wiki: only for questions about Terraria, the titles of 1 to 3 pages of the official wiki (terraria.wiki.gg) likely to answer it, one per thing asked about; otherwise [].";
		StringBuilder user = new StringBuilder();
		AppendHistory(user, options);
		user.Append("Question: ").Append(question);
		try
		{
			return GroqClient.ParseJson(GroqClient.Complete(options.RouterModel, system, user.ToString(), 1200, 0.1f, json: true));
		}
		catch
		{
			return null;
		}
	}

	private static List<SearchResult> Choose(string question, List<SearchResult> hits, List<AskNote> notes, AskOptions options)
	{
		int count = Math.Max(1, Math.Min(options.MaxPages, hits.Count));
		string system = "You choose which search results to open to answer a question. Reply with JSON only: {\"pick\": [ids]} with up to " + count + " ids, best first.\n" +
			"Prefer: pages that directly answer it; official, reference and well-known sources; recent pages for anything that changes; pages likely to hold what the notes so far are missing. Avoid: videos (YouTube, TikTok), social media, shops, pages about a different topic or an old version, and two pages from the same site (except different wiki pages, one per thing the question compares).";
		StringBuilder user = new StringBuilder("Question: ").Append(question).Append('\n');
		if (notes.Count > 0)
		{
			user.Append("\nAlready known:\n");
			foreach (AskNote note in notes)
			{
				user.Append("- ").Append(note.Domain).Append(": ").Append(Shorten(note.Text.Replace('\n', ' '), 200)).Append('\n');
			}
		}
		user.Append("\nResults:\n");
		for (int i = 0; i < hits.Count; i++)
		{
			user.Append(i).Append(". ").Append(hits[i].Title).Append(" | ").Append(hits[i].Domain).Append(" | ").Append(Shorten(hits[i].Snippet, 200)).Append('\n');
		}
		List<SearchResult> chosen = new List<SearchResult>();
		try
		{
			JObject pick = GroqClient.ParseJson(GroqClient.Complete(Other(options.RouterModel), system, user.ToString(), 1200, 0.1f, json: true));
			foreach (JToken id in (pick?["pick"] as JArray) ?? new JArray())
			{
				int index = (int?)id ?? -1;
				if (index >= 0 && index < hits.Count && !chosen.Contains(hits[index]) && chosen.Count < count)
				{
					chosen.Add(hits[index]);
				}
			}
		}
		catch
		{
		}
		// Fallback (or not enough picks): the first hits that aren't videos or social media.
		foreach (SearchResult hit in hits)
		{
			if (chosen.Count >= count)
			{
				break;
			}
			if (!chosen.Contains(hit) && !IsLowValue(hit.Domain) && (hit.Domain == "terraria.wiki.gg" || !chosen.Any(c => c.Domain == hit.Domain)))
			{
				chosen.Add(hit);
			}
		}
		return chosen;
	}

	/// <summary>{answered, missing, queries, wiki}: do the notes answer the question, and if not, where to look next.</summary>
	private static JObject Evaluate(string question, List<AskNote> notes, HashSet<string> usedQueries, string today, AskOptions options)
	{
		string system = "You check a research assistant's notes. Today is " + today + ".\n" +
			"Reply with JSON only: {\"answered\": true|false, \"missing\": \"...\", \"queries\": [\"...\"], \"wiki\": [\"...\"]}.\n" +
			"answered: true only if the notes answer the question specifically and completely (with current data when it changes over time). A vague or partial answer, or notes about a different thing, is false.\n" +
			"If false: missing = what is still unknown, in a few words; queries = 1 or 2 NEW web search queries that would find it (different wording, broader or more specific, or another language; never repeat the queries already used); wiki = official Terraria wiki page titles that may hold it (only for Terraria questions, else []).";
		StringBuilder user = new StringBuilder("Question: ").Append(question).Append("\n\nQueries already used: ").Append(string.Join(" | ", usedQueries)).Append("\n\nNotes:\n");
		if (notes.Count == 0)
		{
			user.Append("(nothing useful found yet)\n");
		}
		foreach (AskNote note in notes)
		{
			user.Append("- ").Append(note.Title).Append(" (").Append(note.Domain).Append("): ").Append(Shorten(note.Text, 700)).Append('\n');
		}
		try
		{
			return GroqClient.ParseJson(GroqClient.Complete(BigModel, system, user.ToString(), 1200, 0.1f, json: true));
		}
		catch
		{
			return null;
		}
	}

	private static string AnswerSystem(AskOptions options, string today, bool researched)
	{
		StringBuilder system = new StringBuilder();
		system.Append("You are ").Append(options.Name).Append(", a helpful assistant inside the game Terraria (the player uses Terraria 1.4.5.8 on PC). Today is ").Append(today).Append(".\n");
		system.Append("Answer the player's question. Language: ").Append(options.Language).Append(". Tone: ").Append(options.Tone).Append(".\n");
		if (researched)
		{
			system.Append("Facts must come ONLY from the numbered notes given with the question. Cite them right after the fact as [1], [2]. ");
			system.Append("Give the most useful, specific answer the notes allow: lists of names, numbers, steps. ");
			system.Append("If the notes don't have part of the answer, say briefly what you couldn't confirm, never fill the gap from memory. ");
			system.Append("Never invent names, numbers, prices, dates, versions or stats. If notes disagree, prefer the most official and recent one. ");
			system.Append("When the facts change over time, say the date or time they refer to.\n");
		}
		else
		{
			system.Append("No research was done. Don't state uncertain facts; if the question needs current or specific data, say so.\n");
		}
		system.Append("Format: plain text for a small game chat box. No markdown at all: no headings, no bold or italics, no tables, no code blocks, no links. ");
		system.Append("Short paragraphs; for a list, one item per line starting with \"- \". If the player asks for a table or comparison, give the same data as a list, one line per item like \"- Zenith: 190 damage, ...\" (never say you can't make tables). Go straight to the answer, no preamble, don't repeat the question. ");
		system.Append("Keep it under ").Append(options.MaxChars).Append(" characters.");
		if (!string.IsNullOrWhiteSpace(options.Instructions))
		{
			system.Append("\nThe player's own instructions: ").Append(options.Instructions.Trim());
		}
		return system.ToString();
	}

	private static string AnswerUser(string question, AskOptions options, List<AskNote> notes, AskResult result)
	{
		StringBuilder user = new StringBuilder();
		AppendHistory(user, options);
		if (result.Searched || notes.Count > 0)
		{
			user.Append("Notes:\n");
			if (notes.Count == 0)
			{
				user.Append("(the research found nothing useful)\n");
			}
			for (int i = 0; i < notes.Count; i++)
			{
				user.Append('[').Append(i + 1).Append("] ").Append(notes[i].Title).Append(" - ").Append(notes[i].Url).Append('\n').Append(notes[i].Text.Trim()).Append("\n\n");
			}
		}
		user.Append("Question: ").Append(question);
		return user.ToString();
	}

	#endregion

	#region Helpers

	private static void AppendHistory(StringBuilder text, AskOptions options)
	{
		if (options.History.Count == 0)
		{
			return;
		}
		text.Append("Earlier in this conversation:\n");
		foreach ((string q, string a) in options.History)
		{
			text.Append("Player: ").Append(q).Append("\nAssistant: ").Append(Shorten(a, 400)).Append('\n');
		}
		text.Append('\n');
	}

	/// <summary>A JSON string or array as up to <paramref name="max"/> distinct, non-empty strings.</summary>
	private static List<string> Strings(JToken token, int max)
	{
		IEnumerable<string> values = token switch
		{
			JArray array => array.Select(t => (string)t),
			JValue value when value.Type == JTokenType.String => new[] { (string)value },
			_ => Enumerable.Empty<string>()
		};
		return values.Select(v => (v ?? "").Trim()).Where(v => v.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).Take(max).ToList();
	}

	/// <summary>The other small model, so two jobs at once don't share one rate limit.</summary>
	private static string Other(string model)
	{
		return (model == SmallModel) ? BigModel : SmallModel;
	}

	private static bool IsLowValue(string domain)
	{
		return new[] { "youtube.com", "youtu.be", "tiktok.com", "facebook.com", "instagram.com", "x.com", "twitter.com", "pinterest.com" }.Any(d => domain.EndsWith(d, StringComparison.OrdinalIgnoreCase));
	}

	private static string Key(string url)
	{
		return (url ?? "").TrimEnd('/');
	}

	private static bool SameUrl(string a, string b)
	{
		return string.Equals(Key(a), Key(b), StringComparison.OrdinalIgnoreCase);
	}

	private static string Shorten(string text, int max)
	{
		text = text ?? "";
		return (text.Length <= max) ? text : (text.Substring(0, max) + "...");
	}

	#endregion
}
