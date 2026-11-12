using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using IsaClient.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IsaClient.Languages;

/// <summary>
/// IsaClient in other languages. The code stays in English; what reaches the screen goes through <see cref="T"/>, which
/// looks it up in the language pack: JSON files of "English text": "translation". The packs are downloaded from the
/// public repository (<see cref="LanguageRepo"/>) when they are used; the player's own files in IsaClient/Languages add
/// to or replace them. IsaClient carries only the English catalog (every text it shows). Text built
/// from parts ("Playing: " + title) is found by templates: "Playing: {0}" -> "Reproduciendo: {0}", the parts
/// translated on their own when they are known. Commands (.video...) are never translated, only what is said about them.
/// The language is the game's own until one is picked (<see cref="Choice"/>).
/// A word that means different things in different places is written with where it is ("tetris::Hold",
/// "autoclick::Hold"): a pack may translate each one on its own; when it doesn't, the plain word's translation is used,
/// and in English only the word is shown.
/// </summary>
public static class L10n
{
	/// <summary>The language picked in the language window ("" = the game's language).</summary>
	[Setting("Language")]
	public static string Choice = "";

	/// <summary>Writes every text with no translation to IsaClient/Languages/Missing-&lt;code&gt;.txt (for whoever translates).</summary>
	[Setting("LanguageLogMissing")]
	public static bool LogMissing;

	public const string English = "en";

	private static readonly object _lock = new object();

	private static Pack _pack;

	private static string _loadedFor;

	private static Dictionary<string, string> _exact = new Dictionary<string, string>(StringComparer.Ordinal);

	private static List<Template> _templates = new List<Template>();

	/// <summary>Templates by their first literal part, grouped by its length (looked up with the start of a text).</summary>
	private static Dictionary<int, Dictionary<string, List<Template>>> _byPrefix = new Dictionary<int, Dictionary<string, List<Template>>>();

	/// <summary>Templates that start with a part ("{0} pictures").</summary>
	private static List<Template> _openStart = new List<Template>();

	private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);

	private static readonly HashSet<string> _missing = new HashSet<string>(StringComparer.Ordinal);

	private static int _version;

	/// <summary>Changes every time the language changes (things that keep translated text redo it).</summary>
	public static int Version => _version;

	/// <summary>The language code in use ("en", "es", "pt-BR"...).</summary>
	public static string Current
	{
		get
		{
			EnsureLoaded();
			return _pack?.Code ?? English;
		}
	}

	public static Pack CurrentPack
	{
		get
		{
			EnsureLoaded();
			return _pack;
		}
	}

	public static string Folder => Path.Combine(ClientSettings.IsaClientFolder, "Languages");

	/// <summary>The last pack file that could not be read, and why (shown in the language window).</summary>
	public static string ReadError;

	#region Translating

	/// <summary>The text in the player's language (itself when there is no translation, or the language is English).</summary>
	public static string T(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text;
		}
		EnsureLoaded();
		if (_pack == null)
		{
			return (text.IndexOf("::", StringComparison.Ordinal) >= 0) ? StripContexts(text) : text;
		}
		lock (_lock)
		{
			if (_cache.TryGetValue(text, out var known))
			{
				return known;
			}
			string result = Translate(text, depth: 0) ?? ((text.IndexOf("::", StringComparison.Ordinal) >= 0) ? StripContexts(text) : text);
			if (_cache.Count > 30000)
			{
				_cache.Clear();
			}
			_cache[text] = result;
			if (LogMissing && (object)result == text && LooksLikeWords(text) && _missing.Add(text))
			{
				WriteMissing(text);
			}
			return result;
		}
	}

	/// <summary><paramref name="text"/> (with {0}, {1}...) translated, then filled ("{0} of {1}").</summary>
	public static string F(string text, params object[] args)
	{
		string translated = T(text);
		try
		{
			return string.Format(translated, args);
		}
		catch (FormatException)
		{
			return string.Format(text, args);
		}
	}

	/// <summary>Null when nothing matched.</summary>
	private static string Translate(string text, int depth)
	{
		if (_exact.TryGetValue(text, out var exact))
		{
			return exact;
		}
		// "tetris::Hold" not translated on its own: as the plain word.
		int context = ContextLength(text);
		if (context > 0)
		{
			string plain = text.Substring(context);
			return Translate(plain, depth) ?? plain;
		}
		string templated = MatchTemplate(text, depth);
		if (templated != null)
		{
			return templated;
		}
		// The same text with spaces around it (layout padding) keeps them.
		string trimmed = text.Trim();
		if (trimmed.Length != text.Length && trimmed.Length > 0)
		{
			string inner = Translate(trimmed, depth);
			if (inner != null)
			{
				int start = text.IndexOf(trimmed, StringComparison.Ordinal);
				return text.Substring(0, start) + inner + text.Substring(start + trimmed.Length);
			}
			return null;
		}
		// Lines put together (tooltips): each line on its own.
		if (depth < 2 && text.IndexOf('\n') >= 0)
		{
			string[] lines = text.Split('\n');
			bool any = false;
			for (int i = 0; i < lines.Length; i++)
			{
				string line = Translate(lines[i], depth + 1);
				if (line != null)
				{
					lines[i] = line;
					any = true;
				}
			}
			return any ? string.Join("\n", lines) : null;
		}
		return null;
	}

	private static string MatchTemplate(string text, int depth)
	{
		Template best = null;
		string[] bestArgs = null;
		foreach (Template template in Candidates(text))
		{
			string[] args = template.Match(text);
			if (args != null && (best == null || template.LiteralLength > best.LiteralLength))
			{
				best = template;
				bestArgs = args;
			}
		}
		if (best == null)
		{
			return null;
		}
		// The parts are translated when they are texts of their own ("on", "the whole queue"...).
		for (int i = 0; i < bestArgs.Length; i++)
		{
			if (depth < 2)
			{
				bestArgs[i] = Translate(bestArgs[i], depth + 1) ?? bestArgs[i];
			}
		}
		return best.Fill(bestArgs);
	}

	private static IEnumerable<Template> Candidates(string text)
	{
		foreach (KeyValuePair<int, Dictionary<string, List<Template>>> group in _byPrefix)
		{
			if (group.Key <= text.Length && group.Value.TryGetValue(text.Substring(0, group.Key), out var list))
			{
				foreach (Template template in list)
				{
					yield return template;
				}
			}
		}
		foreach (Template template in _openStart)
		{
			yield return template;
		}
	}

	/// <summary>Length of the "area::" a text starts with (lowercase letters, then ::), or 0.</summary>
	public static int ContextLength(string text)
	{
		if (text == null || text.Length < 4)
		{
			return 0;
		}
		int i = 0;
		while (i < text.Length && i <= 20 && text[i] >= 'a' && text[i] <= 'z')
		{
			i++;
		}
		return (i >= 2 && i + 2 < text.Length && text[i] == ':' && text[i + 1] == ':') ? i + 2 : 0;
	}

	private static readonly Regex _contexts = new Regex(@"(?<![\w:])[a-z]{2,20}::(?=\S)", RegexOptions.Compiled);

	/// <summary>The text without its "area::" marks (what English shows).</summary>
	public static string StripContexts(string text)
	{
		return string.IsNullOrEmpty(text) ? text : _contexts.Replace(text, "");
	}

	private static bool LooksLikeWords(string text)
	{
		int letters = 0;
		foreach (char c in text)
		{
			if (char.IsLetter(c) && ++letters >= 3)
			{
				return true;
			}
		}
		return false;
	}

	private static void WriteMissing(string text)
	{
		try
		{
			Directory.CreateDirectory(Folder);
			File.AppendAllText(Path.Combine(Folder, "Missing-" + _pack.Code + ".txt"), JsonConvert.ToString(text) + "\n", Encoding.UTF8);
		}
		catch
		{
		}
	}

	#endregion

	#region Templates

	/// <summary>"Playing: {0}" and its translation: matched against a text, filled with its parts.</summary>
	internal sealed class Template
	{
		/// <summary>The literal parts: before {0}, between, after (Parts.Length = Holes + 1).</summary>
		private readonly string[] _parts;

		private readonly int[] _holes;

		private readonly string _translation;

		public readonly int LiteralLength;

		private Template(string[] parts, int[] holes, string translation)
		{
			_parts = parts;
			_holes = holes;
			_translation = translation;
			LiteralLength = parts.Sum(p => p.Length);
		}

		public string First => _parts[0];

		/// <summary>Null when the key has no {n} (a plain text) or is nothing but parts.</summary>
		public static Template Parse(string key, string translation)
		{
			List<string> parts = new List<string>();
			List<int> holes = new List<int>();
			StringBuilder part = new StringBuilder();
			for (int i = 0; i < key.Length; i++)
			{
				char c = key[i];
				if (c == '{' && i + 1 < key.Length && key[i + 1] == '{')
				{
					part.Append('{');
					i++;
				}
				else if (c == '}' && i + 1 < key.Length && key[i + 1] == '}')
				{
					part.Append('}');
					i++;
				}
				else if (c == '{')
				{
					int close = key.IndexOf('}', i);
					if (close < 0 || !int.TryParse(key.Substring(i + 1, close - i - 1), out var hole))
					{
						return null;
					}
					parts.Add(part.ToString());
					part.Clear();
					holes.Add(hole);
					i = close;
				}
				else
				{
					part.Append(c);
				}
			}
			parts.Add(part.ToString());
			if (holes.Count == 0)
			{
				return null;
			}
			// Two parts next to each other can't be told apart; and a template must say something.
			for (int i = 1; i < parts.Count - 1; i++)
			{
				if (parts[i].Length == 0)
				{
					return null;
				}
			}
			if (parts.Sum(p => p.Count(char.IsLetter)) < 2)
			{
				return null;
			}
			return new Template(parts.ToArray(), holes.ToArray(), translation);
		}

		/// <summary>The parts of <paramref name="text"/> by hole number, or null when it does not fit.</summary>
		public string[] Match(string text)
		{
			if (!text.StartsWith(_parts[0], StringComparison.Ordinal) || !text.EndsWith(_parts[_parts.Length - 1], StringComparison.Ordinal) || text.Length < LiteralLength)
			{
				return null;
			}
			int max = _holes.Max();
			string[] values = new string[max + 1];
			int at = _parts[0].Length;
			int end = text.Length - _parts[_parts.Length - 1].Length;
			for (int h = 0; h < _holes.Length; h++)
			{
				int next;
				if (h == _holes.Length - 1)
				{
					next = end;
				}
				else
				{
					next = text.IndexOf(_parts[h + 1], at, StringComparison.Ordinal);
					if (next < 0 || next > end)
					{
						return null;
					}
				}
				if (next < at)
				{
					return null;
				}
				values[_holes[h]] = text.Substring(at, next - at);
				at = next + ((h == _holes.Length - 1) ? 0 : _parts[h + 1].Length);
			}
			return values;
		}

		public string Fill(string[] values)
		{
			StringBuilder result = new StringBuilder();
			for (int i = 0; i < _translation.Length; i++)
			{
				char c = _translation[i];
				if (c == '{' && i + 1 < _translation.Length && _translation[i + 1] == '{')
				{
					result.Append('{');
					i++;
					continue;
				}
				if (c == '}' && i + 1 < _translation.Length && _translation[i + 1] == '}')
				{
					result.Append('}');
					i++;
					continue;
				}
				if (c == '{')
				{
					int close = _translation.IndexOf('}', i);
					if (close > 0 && int.TryParse(_translation.Substring(i + 1, close - i - 1), out var hole) && hole >= 0 && hole < values.Length)
					{
						result.Append(values[hole]);
						i = close;
						continue;
					}
				}
				result.Append(c);
			}
			return result.ToString();
		}
	}

	#endregion

	#region Packs

	/// <summary>One language: its code, names, flag and texts.</summary>
	public sealed class Pack
	{
		public string Code;

		/// <summary>The language's own name ("Español").</summary>
		public string Name;

		/// <summary>Its name in English ("Spanish").</summary>
		public string EnglishName;

		/// <summary>Country code of the flag shown for it ("es", "br"...).</summary>
		public string Flag;

		/// <summary>Who made it (shown in the language window).</summary>
		public string Author;

		public Dictionary<string, string> Strings = new Dictionary<string, string>(StringComparer.Ordinal);

		/// <summary>A file of the player's (IsaClient/Languages), not one IsaClient carries.</summary>
		[JsonIgnore]
		public bool FromFolder;

		/// <summary>The player's file changed some texts of a pack IsaClient carries.</summary>
		[JsonIgnore]
		public bool Customized;

		/// <summary>Coverage, worked out once (-1 until then).</summary>
		[JsonIgnore]
		public float CoverageDone = -1f;

		/// <summary>In the repository but not downloaded yet (no texts here: <see cref="RemoteCoverage"/> says how much it has).</summary>
		[JsonIgnore]
		public bool Remote;

		/// <summary>The repository's figure (0..1) for a language not downloaded.</summary>
		[JsonIgnore]
		public float RemoteCoverage;
	}

	private static List<Pack> _available;

	private static Dictionary<string, string> _catalog;

	/// <summary>Every language there is: English, the downloaded ones, the player's own and the rest of the repository's.</summary>
	public static List<Pack> Available
	{
		get
		{
			if (_available == null)
			{
				_available = LoadAll();
			}
			return _available;
		}
	}

	/// <summary>Every text IsaClient shows, in English (IsaClient.Assets/Languages/en.json, made by tools/refactor extract-strings).</summary>
	public static Dictionary<string, string> Catalog
	{
		get
		{
			if (_catalog == null)
			{
				_catalog = ReadEmbedded(English)?.Strings ?? new Dictionary<string, string>();
			}
			return _catalog;
		}
	}

	private static List<Pack> LoadAll()
	{
		Dictionary<string, Pack> packs = new Dictionary<string, Pack>(StringComparer.OrdinalIgnoreCase);
		// The languages downloaded from the repository.
		try
		{
			if (Directory.Exists(LanguageRepo.Folder))
			{
				foreach (string file in Directory.GetFiles(LanguageRepo.Folder, "*.json"))
				{
					if (string.Equals(Path.GetFileName(file), "index.json", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					Pack pack = ReadFile(file);
					if (pack != null && !string.IsNullOrWhiteSpace(pack.Code) && pack.Code != English)
					{
						packs[pack.Code] = pack;
					}
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		// The player's packs: a new language, or texts that replace some of a carried one.
		try
		{
			if (Directory.Exists(Folder))
			{
				foreach (string file in Directory.GetFiles(Folder, "*.json"))
				{
					Pack mine = ReadFile(file);
					if (mine == null || string.IsNullOrWhiteSpace(mine.Code))
					{
						continue;
					}
					mine.FromFolder = true;
					if (packs.TryGetValue(mine.Code, out var carried) && carried != null && !carried.FromFolder)
					{
						foreach (KeyValuePair<string, string> pair in mine.Strings)
						{
							if (!string.IsNullOrEmpty(pair.Value))
							{
								carried.Strings[pair.Key] = pair.Value;
							}
						}
						carried.Customized = true;
						if (!string.IsNullOrWhiteSpace(mine.Name))
						{
							carried.Name = mine.Name;
						}
					}
					else
					{
						packs[mine.Code] = mine;
					}
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		// The rest of the repository's languages: listed (with how much they translate), downloaded when picked.
		foreach (LanguageRepo.Entry entry in LanguageRepo.Index)
		{
			if (!packs.ContainsKey(entry.Code) && entry.Code != English)
			{
				packs[entry.Code] = new Pack
				{
					Code = entry.Code,
					Name = string.IsNullOrWhiteSpace(entry.Name) ? entry.Code : entry.Name,
					EnglishName = string.IsNullOrWhiteSpace(entry.EnglishName) ? (entry.Name ?? entry.Code) : entry.EnglishName,
					Flag = (entry.Flag ?? "").Trim().ToLowerInvariant(),
					Author = entry.Author,
					Remote = true,
					RemoteCoverage = Math.Max(0f, Math.Min(1f, entry.Percent / 100f))
				};
			}
		}
		if (!packs.ContainsKey(English))
		{
			packs[English] = new Pack
			{
				Code = English,
				Name = "English",
				EnglishName = "English",
				Flag = "us"
			};
		}
		return packs.Values.OrderBy(p => p.Code == English ? 0 : 1).ThenBy(p => p.EnglishName ?? p.Name).ToList();
	}

	private static Pack ReadEmbedded(string code)
	{
		try
		{
			using Stream stream = typeof(L10n).Assembly.GetManifestResourceStream("IsaClient.Languages." + code + ".json");
			if (stream == null)
			{
				return null;
			}
			using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
			return Parse(reader.ReadToEnd());
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			return null;
		}
	}

	internal static Pack ReadFile(string file)
	{
		try
		{
			return Parse(File.ReadAllText(file, Encoding.UTF8));
		}
		catch (Exception ex)
		{
			System.Diagnostics.Trace.WriteLine("Language pack " + Path.GetFileName(file) + " could not be read: " + ex.Message);
			ReadError = Path.GetFileName(file) + ": " + ex.Message;
			return null;
		}
	}

	/// <summary>A pack's JSON: {"Code", "Name", "EnglishName", "Flag", "Author", "Strings": {english: translation}}.</summary>
	internal static Pack Parse(string json)
	{
		JObject root = JObject.Parse(json);
		Pack pack = new Pack
		{
			Code = ((string)root["Code"])?.Trim(),
			Name = (string)root["Name"],
			EnglishName = (string)root["EnglishName"],
			Flag = ((string)root["Flag"])?.Trim().ToLowerInvariant(),
			Author = (string)root["Author"]
		};
		if (root["Strings"] is JObject strings)
		{
			foreach (JProperty property in strings.Properties())
			{
				string value = (string)property.Value;
				if (!string.IsNullOrEmpty(value))
				{
					pack.Strings[property.Name] = value;
				}
			}
		}
		pack.Name = string.IsNullOrWhiteSpace(pack.Name) ? pack.Code : pack.Name;
		pack.EnglishName = string.IsNullOrWhiteSpace(pack.EnglishName) ? pack.Name : pack.EnglishName;
		return pack;
	}

	/// <summary>The language that should be in use (its code as the packs have it), or null for English.</summary>
	public static string WantedCode
	{
		get
		{
			Pack pack = Find(Wanted());
			return (pack == null || pack.Code == English) ? null : pack.Code;
		}
	}

	/// <summary>The language to use now: the one picked, else the game's (its pack, or the same language from another country).</summary>
	private static string Wanted()
	{
		string wanted = Choice;
		if (string.IsNullOrWhiteSpace(wanted))
		{
			wanted = GameLanguage;
		}
		return wanted;
	}

	public static string GameLanguage
	{
		get
		{
			try
			{
				return Terraria.Localization.Language.ActiveCulture?.Name ?? English;
			}
			catch
			{
				return English;
			}
		}
	}

	/// <summary>The pack for a culture or code ("es-ES" -> "es", "zh-Hans" -> "zh-Hans", "pt-PT" -> "pt-BR").</summary>
	public static Pack Find(string code)
	{
		if (string.IsNullOrWhiteSpace(code))
		{
			return null;
		}
		List<Pack> all = Available;
		Pack exact = all.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
		if (exact != null)
		{
			return exact;
		}
		string primary = code.Split('-', '_')[0];
		return all.FirstOrDefault(p => string.Equals(p.Code.Split('-', '_')[0], primary, StringComparison.OrdinalIgnoreCase));
	}

	private static void EnsureLoaded()
	{
		string wanted = Wanted();
		if (wanted == _loadedFor)
		{
			return;
		}
		lock (_lock)
		{
			if (wanted == _loadedFor)
			{
				return;
			}
			LanguageRepo.Start();
			Pack pack = Find(wanted);
			// Not downloaded yet: English until it arrives (then everything switches by itself).
			if (pack != null && pack.Remote)
			{
				LanguageRepo.Want(pack.Code);
			}
			Use((pack == null || pack.Code == English || pack.Strings.Count == 0) ? null : pack);
			_loadedFor = wanted;
		}
	}

	private static void Use(Pack pack)
	{
		Dictionary<string, string> exact = new Dictionary<string, string>(StringComparer.Ordinal);
		List<Template> templates = new List<Template>();
		if (pack != null)
		{
			foreach (KeyValuePair<string, string> pair in pack.Strings)
			{
				Template template = (pair.Key.IndexOf('{') >= 0) ? Template.Parse(pair.Key, pair.Value) : null;
				if (template != null)
				{
					templates.Add(template);
				}
				else
				{
					exact[Unescape(pair.Key)] = Unescape(pair.Value);
				}
			}
		}
		Dictionary<int, Dictionary<string, List<Template>>> byPrefix = new Dictionary<int, Dictionary<string, List<Template>>>();
		List<Template> openStart = new List<Template>();
		foreach (Template template in templates)
		{
			if (template.First.Length == 0)
			{
				openStart.Add(template);
				continue;
			}
			if (!byPrefix.TryGetValue(template.First.Length, out var group))
			{
				group = new Dictionary<string, List<Template>>(StringComparer.Ordinal);
				byPrefix[template.First.Length] = group;
			}
			if (!group.TryGetValue(template.First, out var list))
			{
				list = new List<Template>();
				group[template.First] = list;
			}
			list.Add(template);
		}
		_pack = pack;
		_exact = exact;
		_templates = templates;
		_byPrefix = byPrefix;
		_openStart = openStart;
		_cache.Clear();
		_missing.Clear();
		_version++;
	}

	/// <summary>Plain keys keep their braces doubled like templates ("{{" in the file is "{" on screen).</summary>
	private static string Unescape(string text)
	{
		return (text.IndexOf("{{", StringComparison.Ordinal) >= 0 || text.IndexOf("}}", StringComparison.Ordinal) >= 0) ? text.Replace("{{", "{").Replace("}}", "}") : text;
	}

	/// <summary>Picks a language ("" = the game's) and saves it.</summary>
	public static void Select(string code)
	{
		Choice = code ?? "";
		_loadedFor = null;
		ClientSettings.Save();
		EnsureLoaded();
	}

	/// <summary>Reads the packs again (after importing or editing one).</summary>
	public static void Reload()
	{
		lock (_lock)
		{
			_available = null;
			_loadedFor = null;
		}
		EnsureLoaded();
	}

	/// <summary>How much of IsaClient a pack translates (0..1).</summary>
	public static float Coverage(Pack pack)
	{
		if (pack == null || pack.Code == English)
		{
			return 1f;
		}
		if (pack.Remote)
		{
			return pack.RemoteCoverage;
		}
		if (pack.CoverageDone < 0f)
		{
			Dictionary<string, string> catalog = Catalog;
			pack.CoverageDone = (catalog.Count == 0) ? 0f : ((float)catalog.Keys.Count(k => pack.Strings.ContainsKey(k)) / catalog.Count);
		}
		return pack.CoverageDone;
	}

	#endregion
}
