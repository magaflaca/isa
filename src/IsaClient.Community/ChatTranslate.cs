using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Community;

/// <summary>
/// Translated chat (.translate es en): what you type is translated before it is sent. Server commands (/...), client
/// commands (.) and everything in brackets ([i:58], [c/FF66FF:text], [anything]) or links go out exactly as typed;
/// only the words between them are translated. Messages are translated one after another so they keep their order.
/// </summary>
public static class ChatTranslate
{
	[Setting("TranslateChat")]
	public static bool Enabled;

	/// <summary>The language you write in ("auto" = detect it).</summary>
	[Setting("TranslateFrom")]
	public static string FromCode = "auto";

	/// <summary>The language your messages go out in.</summary>
	[Setting("TranslateTo")]
	public static string ToCode = "en";

	/// <summary>Brackets (with "\]" escapes, like the game reads them) and links.</summary>
	private static readonly Regex Protected = new Regex(@"\[(?:\\.|[^\]\\\r\n])*\]|https?://\S+|www\.\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	private static Task _queue = Task.CompletedTask;

	private static readonly object _queueLock = new object();

	public static ChatLanguage From
	{
		get => ChatLanguages.Find(FromCode, allowAuto: true) ?? ChatLanguages.Auto;
		set => FromCode = value.Code;
	}

	public static ChatLanguage To
	{
		get => ChatLanguages.Find(ToCode) ?? ChatLanguages.All[0];
		set => ToCode = value.Code;
	}

	/// <summary>"Spanish to English" (or "Any language to English").</summary>
	public static string Describe()
	{
		return ((From == ChatLanguages.Auto) ? "Any language" : From.Name) + " to " + To.Name;
	}

	/// <summary>
	/// Sends a typed line through <paramref name="send"/>: right away when there is nothing to translate, else once the
	/// translation arrives (on the game thread). If the translation fails the message goes out as typed.
	/// </summary>
	public static void Send(string text, Action<string> send)
	{
		if (!ShouldTranslate(text))
		{
			send(text);
			return;
		}
		ChatLanguage from = From;
		ChatLanguage to = To;
		Enqueue(delegate
		{
			string result;
			try
			{
				result = TranslateMessage(text, from, to);
			}
			catch (Exception ex)
			{
				result = text;
				string reason = (ex is TranslatorException) ? ex.Message : ex.GetType().Name;
				Main.QueueMainThreadAction(() => ClientChat.Notice("Could not translate (" + reason + "); sent as you typed it."));
			}
			Main.QueueMainThreadAction(delegate
			{
				if (!Main.gameMenu)
				{
					send(result);
				}
			});
		});
	}

	/// <summary>Translates in the background and gives the result to <paramref name="done"/> on the game thread (null + error on failure).</summary>
	public static void Preview(string text, ChatLanguage from, ChatLanguage to, Action<string, string> done)
	{
		Enqueue(delegate
		{
			string result = null;
			string error = null;
			try
			{
				result = TranslateMessage(text, from, to);
			}
			catch (Exception ex)
			{
				error = ex.Message;
			}
			Main.QueueMainThreadAction(() => done(result, error));
		});
	}

	private static void Enqueue(Action work)
	{
		lock (_queueLock)
		{
			_queue = _queue.ContinueWith(_ => work(), TaskScheduler.Default);
		}
	}

	/// <summary>On, a real message (not a command) and some words outside the protected parts.</summary>
	public static bool ShouldTranslate(string text)
	{
		if (!Enabled || string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		char first = text.TrimStart()[0];
		if (first == '/' || first == '.' || first == ChatCommands.CommandSpecifier)
		{
			return false;
		}
		return Protected.Replace(text, "").Any(char.IsLetter);
	}

	/// <summary>
	/// "Hola [i:58] como andas?" becomes "Hello [i:58] how are you?": the parts between protected ones are translated
	/// (together, one per line, when the service keeps the lines; else one by one) and keep their spaces.
	/// <paramref name="insideColorTags"/> also translates the text of [c/hex:text] tags (keeping the tag and its color);
	/// the first <paramref name="keepPrefix"/> characters ("[Admin] Name: ") are never translated.
	/// </summary>
	public static string TranslateMessage(string text, ChatLanguage from, ChatLanguage to, bool insideColorTags = false, int keepPrefix = 0)
	{
		List<Part> parts = new List<Part>();
		keepPrefix = Math.Max(0, Math.Min(keepPrefix, text.Length));
		Add(parts, text.Substring(0, keepPrefix), false);
		int last = keepPrefix;
		foreach (Match match in Protected.Matches(text, keepPrefix))
		{
			Add(parts, text.Substring(last, match.Index - last), true);
			Match color = insideColorTags ? ColorTag.Match(match.Value) : Match.Empty;
			if (color.Success)
			{
				Add(parts, color.Groups["head"].Value, false);
				Add(parts, color.Groups["text"].Value.Replace("\\]", "]"), true, escape: true);
				Add(parts, "]", false);
			}
			else
			{
				Add(parts, match.Value, false);
			}
			last = match.Index + match.Length;
		}
		Add(parts, text.Substring(last), true);

		List<Part> words = parts.Where(p => p.Translate && p.Text.Any(char.IsLetter)).ToList();
		if (words.Count == 0)
		{
			return text;
		}
		string[] cores = words.Select(p => p.Text.Trim()).ToArray();
		string[] translated = null;
		if (cores.Length > 1)
		{
			string[] lines = Translator.Translate(string.Join("\n", cores), from, to).Split('\n');
			if (lines.Length == cores.Length)
			{
				translated = lines;
			}
		}
		translated ??= cores.Select(core => Translator.Translate(core, from, to)).ToArray();

		for (int k = 0; k < words.Count; k++)
		{
			Part part = words[k];
			string lead = part.Text.Substring(0, part.Text.Length - part.Text.TrimStart().Length);
			string trail = part.Text.Substring(part.Text.TrimEnd().Length);
			string core = translated[k].Trim();
			if (part.Escape)
			{
				// Inside a tag: a ']' would close it, a trailing '\' would escape the closing bracket.
				core = core.Replace("]", "\\]");
				if (core.EndsWith("\\", StringComparison.Ordinal))
				{
					core += " ";
				}
			}
			part.Text = lead + core + trail;
		}
		return string.Concat(parts.Select(p => p.Text));
	}

	/// <summary>A piece of a message: kept as it is, or translated.</summary>
	private sealed class Part
	{
		public string Text;

		public bool Translate;

		public bool Escape;
	}

	/// <summary>[c/FF66FF:text] or [color/FF66FF:text]: the head up to ':' and the text.</summary>
	private static readonly Regex ColorTag = new Regex(@"^(?<head>\[(?:c|color)/[^:\]]*:)(?<text>.*)\]$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

	private static void Add(List<Part> parts, string text, bool translate, bool escape = false)
	{
		if (text.Length > 0)
		{
			parts.Add(new Part { Text = text, Translate = translate, Escape = escape });
		}
	}

	#region Chat commands

	/// <summary>.translate [on|off] | &lt;from&gt; &lt;to&gt; | &lt;to&gt; | swap | list | test &lt;text&gt; | in ... (incoming messages)</summary>
	internal static void TranslateCommand(CommandContext command)
	{
		string[] args = command.Parts.Skip(1).ToArray();
		string word = (args.Length > 0) ? args[0].ToLowerInvariant() : "";
		switch (word)
		{
		case "":
			Enabled = !Enabled;
			break;
		case "on":
			Enabled = true;
			break;
		case "off":
			Enabled = false;
			break;
		case "list":
		case "languages":
		case "langs":
			ClientChat.Info("ChatLanguages: auto (detect), " + ChatLanguages.Codes() + ".");
			return;
		case "swap":
			if (From == ChatLanguages.Auto)
			{
				ClientChat.Error("Pick the language you write in first: .translate <from> <to> (e.g. .translate es en).");
				return;
			}
			ChatLanguage old = From;
			From = To;
			To = old;
			break;
		case "in":
		case "incoming":
		case "read":
			IncomingTranslation.Command(args.Skip(1).ToArray());
			return;
		case "test":
		case "try":
			string sample = string.Join(" ", args.Skip(1));
			if (sample.Length == 0)
			{
				ClientChat.Error("Usage: .translate test <text>");
				return;
			}
			ClientChat.Info("Translating...");
			Preview(sample, From, To, delegate(string result, string error)
			{
				if (error != null)
				{
					ClientChat.Error("Could not translate: " + error);
				}
				else
				{
					ClientChat.Info(Describe() + ": " + result);
				}
			});
			return;
		default:
			ChatLanguage first = ChatLanguages.Find(args[0], allowAuto: true);
			ChatLanguage second = (args.Length > 1) ? ChatLanguages.Find(args[1]) : null;
			if (first == null || (args.Length > 1 && second == null) || (args.Length == 1 && first == ChatLanguages.Auto))
			{
				string bad = (first == null) ? args[0] : args[Math.Min(1, args.Length - 1)];
				ClientChat.Error("Unknown language '" + bad + "'. Usage: .translate <from> <to> (e.g. .translate es en), .translate <to>, on, off, swap, list, test <text>, in <language>");
				return;
			}
			ChatLanguage from = (args.Length == 1) ? ChatLanguages.Auto : first;
			ChatLanguage to = (args.Length == 1) ? first : second;
			if (from == to)
			{
				ClientChat.Error("Pick two different languages.");
				return;
			}
			From = from;
			To = to;
			Enabled = true;
			break;
		}
		ClientSettings.Save();
		ClientChat.Info("Translated chat " + (Enabled ? "on" : "off") + ": " + Describe() + (Enabled ? ". Commands and [tags] are never translated." : "  (.translate on to use it)"));
	}

	#endregion
}
