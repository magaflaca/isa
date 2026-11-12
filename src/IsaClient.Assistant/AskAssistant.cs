using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IsaClient.Community;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Terraria;

namespace IsaClient.Assistant;

/// <summary>
/// .ask: questions about anything, answered with web search (<see cref="AskEngine"/>). Answers are shown only to you,
/// as clean chat text with numbered sources. One question at a time; the last few are remembered for follow-ups.
/// Settings are in the panel (AI Assistant > Ask).
/// </summary>
public static class AskAssistant
{
	public static readonly string[] AnswerModels = { "qwen/qwen3.8-27b", "openai/gpt-oss-120b", "openai/gpt-oss-20b" };

	public static readonly string[] RouterModels = { "openai/gpt-oss-20b", "openai/gpt-oss-120b", "qwen/qwen3.8-27b" };

	/// <summary>Tone name, and how the model is told to sound.</summary>
	public static readonly (string Name, string Prompt)[] Tones =
	{
		("Friendly", "friendly and warm, like a helpful friend who knows a lot"),
		("Neutral", "neutral, clear and direct"),
		("Expert", "precise and expert, with exact numbers and names"),
		("Playful", "playful with a touch of humor, but always accurate"),
		("Formal", "formal and polite")
	};

	/// <summary>Length name and the characters the answer should fit in.</summary>
	public static readonly (string Name, int Chars)[] Lengths =
	{
		("Short", 320),
		("Normal", 650),
		("Detailed", 1300)
	};

	/// <summary>DuckDuckGo regions: label and code.</summary>
	public static readonly (string Name, string Code)[] Regions =
	{
		("Anywhere", "wt-wt"),
		("United States", "us-en"),
		("Mexico", "mx-es"),
		("Spain", "es-es"),
		("Argentina", "ar-es"),
		("Colombia", "co-es"),
		("Chile", "cl-es"),
		("Peru", "pe-es"),
		("Brazil", "br-pt"),
		("United Kingdom", "uk-en")
	};

	[Setting("AskSearch")]
	public static string SearchMode = "auto";

	/// <summary>"groq" (Groq's built-in browser, no captchas) or "free" (DuckDuckGo, Brave, Wikipedia from this computer).</summary>
	[Setting("AskEngine")]
	public static string Engine = "groq";

	/// <summary>"auto" (the question's language), "game" (the game's language) or a language code.</summary>
	[Setting("AskLanguage")]
	public static string LanguageCode = "auto";

	[Setting("AskTone")]
	public static string Tone = "Friendly";

	[Setting("AskLength")]
	public static string Length = "Normal";

	[Setting("AskModel")]
	public static string AnswerModel = AnswerModels[0];

	[Setting("AskRouterModel")]
	public static string RouterModel = RouterModels[0];

	[Setting("AskPages", Min = 1, Max = 5)]
	public static int MaxPages = 3;

	/// <summary>Research rounds at most: another round with new searches while the answer isn't found.</summary>
	[Setting("AskRounds", Min = 1, Max = 4)]
	public static int MaxRounds = 2;

	/// <summary>Depth name and rounds.</summary>
	public static readonly (string Name, int Rounds)[] Depths =
	{
		("Quick", 1),
		("Normal", 2),
		("Deep", 3),
		("Thorough", 4)
	};

	[Setting("AskRegion")]
	public static string Region = "wt-wt";

	/// <summary>1 strict, -1 moderate, -2 off.</summary>
	[Setting("AskSafeSearch", Min = -2, Max = 1)]
	public static int SafeSearch = -1;

	[Setting("AskShowSources")]
	public static bool ShowSources = true;

	[Setting("AskShowProgress")]
	public static bool ShowProgress = true;

	[Setting("AskRemember")]
	public static bool Remember = true;

	/// <summary>Optional Brave Search API key (free plan at brave.com/search/api): reliable search without captchas.</summary>
	[Setting("AskBraveKey")]
	public static string BraveKey = "";

	/// <summary>The player's own rules ("call me Isa", "answer like a pirate"...).</summary>
	[Setting("AskInstructions")]
	public static string Instructions = "";

	public static readonly Color DefaultNameColor = new Color(120, 200, 255);

	/// <summary>Light orange, like Claude Code's working line.</summary>
	public static readonly Color DefaultStatusColor = new Color(255, 178, 120);

	/// <summary>The assistant's name: "Isa: ..." in the chat and how it calls itself.</summary>
	[Setting("AskName")]
	public static string Name = "Isa";

	[Setting("AskNameColor")]
	public static string NameColorText = "120,200,255";

	[Setting("AskStatusColor")]
	public static string StatusColorText = "255,178,120";

	/// <summary>"" = the client text color.</summary>
	[Setting("AskAnswerColor")]
	public static string AnswerColorText = "";

	public static Color NameColor
	{
		get => ClientChat.TryParseColor(NameColorText, out Color color) ? color : DefaultNameColor;
		set => NameColorText = value.R + "," + value.G + "," + value.B;
	}

	public static Color StatusColor
	{
		get => ClientChat.TryParseColor(StatusColorText, out Color color) ? color : DefaultStatusColor;
		set => StatusColorText = value.R + "," + value.G + "," + value.B;
	}

	public static Color AnswerColor
	{
		get => (!string.IsNullOrWhiteSpace(AnswerColorText) && ClientChat.TryParseColor(AnswerColorText, out Color color)) ? color : ClientChat.AccentColor;
		set => AnswerColorText = value.R + "," + value.G + "," + value.B;
	}

	public static string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Isa" : Name.Trim();

	public static readonly Color MutedColor = new Color(150, 160, 185);

	private static readonly Color CiteColor = new Color(110, 185, 255);

	private const int HistorySize = 4;

	private static readonly List<(string Question, string Answer)> _history = new List<(string, string)>();

	/// <summary>A question is being answered.</summary>
	public static bool Busy { get; private set; }

	/// <summary>What it is doing right now ("Searching: ...").</summary>
	public static string Status { get; private set; } = "";

	public static AskResult Last { get; private set; }

	public static string LastQuestion { get; private set; }

	public static string LastError { get; private set; }

	public static int Remembered => _history.Count;

	/// <summary>
	/// Answers <paramref name="question"/> in the background. With <paramref name="toChat"/> progress and the answer go
	/// to the chat; <paramref name="done"/> gets the result (or null and the error) on the game thread.
	/// </summary>
	public static bool Ask(string question, bool toChat, Action<AskResult, string> done = null, List<AskImage> images = null)
	{
		question = (question ?? "").Trim();
		images = images ?? new List<AskImage>();
		// Picture links written in the question are looked at too.
		images.AddRange(AskVision.LinksIn(question));
		if (question.Length == 0 && images.Count > 0)
		{
			question = IsaClient.Languages.L10n.T("What is in this picture?");
		}
		if (question.Length == 0)
		{
			return false;
		}
		if (Busy)
		{
			if (toChat)
			{
				ClientChat.Notice(DisplayName + " is still on your last question.");
			}
			return false;
		}
		Busy = true;
		Status = "Thinking...";
		LastQuestion = question;
		LastError = null;
		AskOptions options = Options();
		options.Images = images;
		string tags = (images.Count > 0) ? (" " + AskVision.Tags(images.Count)) : "";
		if (toChat)
		{
			Main.NewText("> " + Escape(question) + tags, MutedColor.R, MutedColor.G, MutedColor.B);
			AskStatus.Start();
		}
		Task.Run(delegate
		{
			AskResult result = null;
			string error = null;
			try
			{
				result = AskEngine.Run(question, options, delegate(string step)
				{
					Status = step;
					if (toChat && ShowProgress)
					{
						Main.QueueMainThreadAction(() => AskStatus.Detail("  " + Escape(step)));
					}
				});
			}
			catch (Exception ex)
			{
				error = (ex is AssistantException) ? ex.Message : (ex.GetType().Name + ": " + ex.Message);
			}
			Main.QueueMainThreadAction(delegate
			{
				Busy = false;
				Status = "";
				if (toChat)
				{
					AskStatus.Stop();
				}
				if (result != null)
				{
					Last = result;
					if (Remember)
					{
						// Follow-ups about the pictures need what they showed.
						string asked = string.IsNullOrWhiteSpace(options.ImageNotes) ? (question + tags) : (question + tags + "\n(" + options.ImageNotes + ")");
						_history.Add((asked, result.Answer));
						while (_history.Count > HistorySize)
						{
							_history.RemoveAt(0);
						}
					}
					if (toChat)
					{
						Print(result);
					}
				}
				else
				{
					LastError = error;
					if (toChat)
					{
						ClientChat.Error(DisplayName + " couldn't answer: " + error);
					}
				}
				done?.Invoke(result, error);
			});
		});
		return true;
	}

	public static void Forget()
	{
		_history.Clear();
	}

	private static AskOptions Options()
	{
		WebSearch.BraveApiKey = BraveKey ?? "";
		return new AskOptions
		{
			AnswerModel = AnswerModel,
			RouterModel = RouterModel,
			SearchMode = SearchMode,
			Engine = Engine,
			Language = LanguagePrompt(),
			Tone = Tones.FirstOrDefault(t => t.Name == Tone).Prompt ?? Tones[0].Prompt,
			MaxChars = MaxChars,
			MaxPages = Math.Max(1, Math.Min(5, MaxPages)),
			MaxRounds = Math.Max(1, Math.Min(4, MaxRounds)),
			Region = Region,
			SafeSearch = SafeSearch,
			Instructions = Instructions,
			Name = DisplayName,
			History = Remember ? _history.ToList() : new List<(string, string)>()
		};
	}

	public static int MaxChars => Lengths.FirstOrDefault(l => l.Name == Length).Chars is int chars && chars > 0 ? chars : 650;

	/// <summary>The language picked: null = the question's own.</summary>
	public static ChatLanguage Language => LanguageCode switch
	{
		"auto" => null,
		"game" => ChatLanguages.GameLanguage,
		_ => ChatLanguages.Find(LanguageCode)
	};

	private static string LanguagePrompt()
	{
		ChatLanguage language = Language;
		return (language == null) ? "the same language the question is written in" : language.Name;
	}

	#region Chat output

	/// <summary>The answer, a paragraph per message, with the citations tinted, then the sources.</summary>
	private static void Print(AskResult result)
	{
		string[] paragraphs = result.Answer.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < paragraphs.Length; i++)
		{
			string line = Cite(Escape(paragraphs[i].TrimEnd()), result);
			Color color = AnswerColor;
			Main.NewText(IsaClient.Languages.L10n.T((i == 0 ? ClientChat.Tint(Escape(DisplayName) + ": ", NameColor) : "") + line), color.R, color.G, color.B);
		}
		if (ShowSources && result.Sources.Count > 0)
		{
			string sources = string.Join("   ", result.Sources.Select(s => ChatLinks.Tag(s.Url, "(" + s.Number + ") " + s.Domain)));
			Main.NewText(IsaClient.Languages.L10n.T("Sources: " + sources), MutedColor.R, MutedColor.G, MutedColor.B);
		}
	}

	/// <summary>"(1)" and "(1, 2)" as links to their sources: "(1)(2)", each one opens its page.</summary>
	private static string Cite(string text, AskResult result)
	{
		return Regex.Replace(text, "\\((\\d{1,2}(?:, \\d{1,2})*)\\)", delegate(Match m)
		{
			StringBuilder links = new StringBuilder();
			foreach (string number in m.Groups[1].Value.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
			{
				AskSource source = result.Sources.FirstOrDefault(s => s.Number.ToString() == number);
				links.Append((source != null) ? ChatLinks.Tag(source.Url, "(" + number + ")") : ClientChat.Tint("(" + number + ")", CiteColor));
			}
			return links.ToString();
		});
	}

	/// <summary>Text that can't form a chat tag.</summary>
	private static string Escape(string text)
	{
		return (text ?? "").Replace("[", "(").Replace("]", ")");
	}

	#endregion

	#region Chat commands

	/// <summary>.ask &lt;question&gt; | forget | sources</summary>
	internal static void AskCommand(CommandContext command)
	{
		string rest = command.Rest.Trim();
		switch (rest.ToLowerInvariant())
		{
		case "":
			if (IsaClient.ChatImages.ChatAttachments.ReadyCount > 0)
			{
				Ask("", toChat: true, images: AttachedPictures());
				return;
			}
			ClientChat.Info("Ask me anything: .ask <question>   (attach pictures to ask about them; .ask sources: links of the last answer, .ask forget: start a new conversation). Settings: AI Assistant > Ask.");
			return;
		case "forget":
		case "new":
		case "reset":
			Forget();
			ClientChat.Info("Ask: new conversation.");
			return;
		case "sources":
		case "links":
			if (Last == null || Last.Sources.Count == 0)
			{
				ClientChat.Info("Ask: the last answer has no sources.");
				return;
			}
			foreach (AskSource source in Last.Sources)
			{
				Main.NewText(IsaClient.Languages.L10n.T(ClientChat.Tint("(" + source.Number + ")", CiteColor) + " " + Escape(source.Title) + "  " + source.Url), MutedColor.R, MutedColor.G, MutedColor.B);
			}
			return;
		}
		Ask(rest, toChat: true, images: AttachedPictures());
	}

	/// <summary>The pictures attached above the chat bar, taken for the question.</summary>
	internal static List<AskImage> AttachedPictures()
	{
		return IsaClient.ChatImages.ChatAttachments.TakeReady().Select(p => new AskImage { Data = p.Data, Type = p.Type, Url = p.Url }).ToList();
	}

	#endregion
}
