using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria.Chat;
using Terraria.IO;
using Terraria.Localization;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Assistant;

/// <summary>
/// AFK assistant: while enabled (.ai / .afk), chat messages that mention the player (name or nicknames) are answered by
/// an LLM (Groq, OpenAI compatible API) on the player's behalf. It keeps a memory of the whole conversation (with and
/// without it, marking what the player and the AI wrote), knows who it is and where it is, writes in the game's
/// language by default and can decide not to answer. Settings live in Terraria/IsaClient/IsaAI.json.
/// </summary>
public static class AiAssistant
{
	internal sealed class ChatEntry
	{
		public DateTime Time;

		public string Sender;

		public string Text;

		public bool Mine;

		public bool ByAI;
	}

	public const string DefaultApiKey = "";

	public const string DefaultModel = "openai/gpt-oss-120b";

	public const string DefaultEndpoint = "https://api.groq.com/openai/v1/chat/completions";

	public static readonly string[] SuggestedModels = new string[3] { "openai/gpt-oss-120b", "openai/gpt-oss-20b", "qwen/qwen3.8-27b" };

	private const string LegacyDefaultPrompt = "You are {name}, a Terraria player in a multiplayer server chat. {name} is away from the keyboard (AFK) and you answer on their behalf. Other names people use for {name}: {nicks}. Keep every reply short (one or two sentences, under 150 characters), casual and friendly, and always in the same language the others are writing in. Do not use emojis or line breaks. Never say you are an AI unless someone asks directly. If someone needs something only {name} can do, say {name} will answer when back.";

	private const string PromptEnglish = "You are {name}, a real person playing Terraria on a multiplayer server, chatting with friends. Right now {name} is away (AFK) and you write in the chat in their place, as if you were them. Other names people use for you: {nicks}. {about}\nWhat is going on: {game}\nRead the whole conversation to understand the context, who is talking to whom and what you (and your earlier automatic replies) already said; do not repeat yourself and keep your story consistent. Answer like a normal player in a game chat: short (usually one sentence, never more than two), natural, relaxed, friendly. Default language: {language}, but if the others write in another language, answer in theirs. Do not say you are an AI or a bot unless someone asks you directly and seriously. If they need something only the real {name} can do (give items, come over, decide something important), say you will do it when you are back. Never invent that you did things in the game.";

	private const string PromptSpanish = "Eres {name}, una persona real jugando Terraria en un servidor multijugador, hablando con amigos. Ahora mismo {name} esta ausente (AFK) y tu escribes en el chat en su lugar, como si fueras {name}. Otros nombres con los que te llaman: {nicks}. {about}\nLo que esta pasando: {game}\nLee toda la conversacion para entender el contexto, quien habla con quien y lo que tu (y tus respuestas automaticas anteriores) ya dijiste; no te repitas y se coherente. Responde como un jugador normal en el chat de un juego: corto (normalmente una frase, nunca mas de dos), natural, relajado y amable. Idioma por defecto: {language}, pero si los demas escriben en otro idioma, responde en el suyo. No digas que eres una IA o un bot salvo que alguien te lo pregunte en serio y directamente. Si necesitan algo que solo el {name} real puede hacer (dar items, ir a algun sitio, decidir algo importante), di que lo haras cuando vuelvas. Nunca inventes que hiciste cosas en el juego.";

	public static bool Enabled;

	public static bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);

	public static string ApiKey = DefaultApiKey;

	public static string Model = DefaultModel;

	public static string Endpoint = DefaultEndpoint;

	/// <summary>Empty = the built-in prompt in the game's language.</summary>
	public static string SystemPrompt = "";

	/// <summary>Free text about the player ("I like building", "I am the server admin"...), inserted as {about}.</summary>
	public static string AboutMe = "";

	/// <summary>Empty = the game's language.</summary>
	public static string ReplyLanguage = "";

	public static List<string> Nicknames = new List<string>();

	public static float Temperature = 0.8f;

	public static int CooldownSeconds = 6;

	public static float ReplyDelaySeconds = 2f;

	public static int MaxReplyLength = 180;

	public static int ContextMessages = 40;

	public static int ContextMinutes = 30;

	public static string ReplyPrefix = "";

	public static bool ReplyToEveryone;

	/// <summary>After the AI answers someone, their next messages within this time also count as talking to you.</summary>
	public static int FollowUpSeconds = 45;

	public static bool Lowercase = true;

	public static bool RemoveAccents = true;

	public static bool RemoveEmojis = true;

	public static bool IncludeGameInfo = true;

	private static readonly List<string> _sessionNicknames = new List<string>();

	private static readonly List<ChatEntry> _history = new List<ChatEntry>();

	private static readonly List<string> _pendingAIMessages = new List<string>();

	private static readonly Dictionary<string, DateTime> _conversations = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

	private static DateTime _lastReply = DateTime.MinValue;

	private static bool _busy;

	private static Preferences _config;

	private static readonly Regex _chatTag = new Regex("\\[(?:c|i|g|a|n)(?:/[^:\\]]*)?:([^\\]]*)\\]", RegexOptions.Compiled);

	private static readonly Regex _senderPattern = new Regex("^(?:\\[[^\\]]*\\]\\s*)*<?(?<name>[^<>:\\[\\]]{1,32}?)>?\\s*[:>]\\s*(?<msg>.+)$", RegexOptions.Compiled);

	private static readonly Regex _anglePattern = new Regex("^(?:\\[[^\\]]*\\]\\s*)*<(?<name>[^<>]{1,32})>\\s*(?<msg>.+)$", RegexOptions.Compiled);

	public static string ConfigPath => Path.Combine(ClientSettings.IsaClientFolder, "IsaAI.json");

	#region Settings

	public static void Load()
	{
		try
		{
			Directory.CreateDirectory(ClientSettings.IsaClientFolder);
			_config = new Preferences(ConfigPath);
			if (!_config.Load())
			{
				Save();
				return;
			}
			ApiKey = _config.Get("ApiKey", DefaultApiKey);
			Model = _config.Get("Model", DefaultModel);
			Endpoint = _config.Get("Endpoint", DefaultEndpoint);
			SystemPrompt = _config.Get("SystemPrompt", "") ?? "";
			if (SystemPrompt == LegacyDefaultPrompt)
			{
				SystemPrompt = "";
			}
			AboutMe = _config.Get("AboutMe", "") ?? "";
			ReplyLanguage = _config.Get("ReplyLanguage", "") ?? "";
			Nicknames = _config.Get("Nicknames", new List<string>()) ?? new List<string>();
			Temperature = _config.Get("Temperature", 0.8f);
			CooldownSeconds = Utils.Clamp(_config.Get("CooldownSeconds", 6), 0, 600);
			ReplyDelaySeconds = Math.Max(0f, Math.Min(60f, _config.Get("ReplyDelaySeconds", 2f)));
			MaxReplyLength = Utils.Clamp(_config.Get("MaxReplyLength", 180), 20, 500);
			ContextMessages = Utils.Clamp(_config.Get("ContextMessages", 40), 0, 200);
			ContextMinutes = Utils.Clamp(_config.Get("ContextMinutes", 30), 1, 1440);
			ReplyPrefix = _config.Get("ReplyPrefix", "") ?? "";
			ReplyToEveryone = _config.Get("ReplyToEveryone", false);
			FollowUpSeconds = Utils.Clamp(_config.Get("FollowUpSeconds", 45), 0, 3600);
			Lowercase = _config.Get("Lowercase", true);
			RemoveAccents = _config.Get("RemoveAccents", true);
			RemoveEmojis = _config.Get("RemoveEmojis", true);
			IncludeGameInfo = _config.Get("IncludeGameInfo", true);
			Save();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	public static void Save()
	{
		try
		{
			Directory.CreateDirectory(ClientSettings.IsaClientFolder);
			if (_config == null)
			{
				_config = new Preferences(ConfigPath);
			}
			_config.Clear();
			_config.Put("ApiKey", ApiKey);
			_config.Put("Model", Model);
			_config.Put("Endpoint", Endpoint);
			_config.Put("_SystemPromptHelp", "Empty = built-in prompt in the game's language. Placeholders: {name} {nicks} {about} {game} {language}");
			_config.Put("SystemPrompt", SystemPrompt);
			_config.Put("AboutMe", AboutMe);
			_config.Put("ReplyLanguage", ReplyLanguage);
			_config.Put("Nicknames", Nicknames);
			_config.Put("Temperature", Temperature);
			_config.Put("CooldownSeconds", CooldownSeconds);
			_config.Put("ReplyDelaySeconds", ReplyDelaySeconds);
			_config.Put("MaxReplyLength", MaxReplyLength);
			_config.Put("ContextMessages", ContextMessages);
			_config.Put("ContextMinutes", ContextMinutes);
			_config.Put("ReplyPrefix", ReplyPrefix);
			_config.Put("ReplyToEveryone", ReplyToEveryone);
			_config.Put("FollowUpSeconds", FollowUpSeconds);
			_config.Put("Lowercase", Lowercase);
			_config.Put("RemoveAccents", RemoveAccents);
			_config.Put("RemoveEmojis", RemoveEmojis);
			_config.Put("IncludeGameInfo", IncludeGameInfo);
			_config.Save();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	public static void OpenConfigFile()
	{
		Save();
		string quoted = "\"" + ConfigPath + "\"";
		string notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
		// Windows 11 can fail the shell call for notepad ("remote procedure call failed"): start it directly first,
		// then the default program for .json, then show the file in Explorer.
		Exception last = null;
		foreach (System.Diagnostics.ProcessStartInfo start in new[]
		{
			new System.Diagnostics.ProcessStartInfo(notepad, quoted) { UseShellExecute = false },
			new System.Diagnostics.ProcessStartInfo(ConfigPath) { UseShellExecute = true },
			new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select," + quoted) { UseShellExecute = false }
		})
		{
			try
			{
				System.Diagnostics.Process.Start(start);
				return;
			}
			catch (Exception ex)
			{
				last = ex;
			}
		}
		ClientChat.Error("Could not open " + ConfigPath + ": " + last?.Message);
	}

	public static void CycleModel()
	{
		int index = Array.IndexOf(SuggestedModels, Model);
		Model = SuggestedModels[(index + 1) % SuggestedModels.Length];
		Save();
	}

	#endregion

	/// <summary>Names set with ".ai nick" for this session only.</summary>
	public static IReadOnlyList<string> SessionNicknames => _sessionNicknames;

	/// <summary>Adds saved nicknames (comma separated); returns how many were new.</summary>
	public static int AddNicknames(string text)
	{
		int added = 0;
		foreach (string raw in (text ?? "").Split(','))
		{
			string nick = raw.Trim();
			if (nick.Length == 0 || nick.Length > 32 || Nicknames.Any((string n) => string.Equals(n, nick, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}
			Nicknames.Add(nick);
			_sessionNicknames.RemoveAll((string n) => string.Equals(n, nick, StringComparison.OrdinalIgnoreCase));
			added++;
		}
		if (added > 0)
		{
			Save();
		}
		return added;
	}

	public static void RemoveNickname(string nick)
	{
		if (Nicknames.RemoveAll((string n) => string.Equals(n, nick, StringComparison.OrdinalIgnoreCase)) > 0)
		{
			Save();
		}
		_sessionNicknames.RemoveAll((string n) => string.Equals(n, nick, StringComparison.OrdinalIgnoreCase));
	}

	private static IEnumerable<string> AllNames()
	{
		if (Main.LocalPlayer != null && !string.IsNullOrWhiteSpace(Main.LocalPlayer.name))
		{
			yield return Main.LocalPlayer.name;
		}
		foreach (string nick in Nicknames.Concat(_sessionNicknames))
		{
			if (!string.IsNullOrWhiteSpace(nick))
			{
				yield return nick.Trim();
			}
		}
	}

	public static void Toggle()
	{
		Enabled = !Enabled;
		Announce();
	}

	public static void SetEnabled(bool enabled)
	{
		Enabled = enabled;
		Announce();
	}

	private static void Announce()
	{
		ClientChat.Info("AFK AI " + (Enabled ? "En" : "Dis") + "abled" + (Enabled ? (". Answering " + (ReplyToEveryone ? "everyone" : ("mentions of: " + string.Join(", ", AllNames().Distinct(StringComparer.OrdinalIgnoreCase))))) : ""));
	}

	/// <summary>.ai / .afk commands.</summary>
	public static bool HandleCommand(string text, string chatText, string[] array)
	{
		if (text != "ai" && text != "afk")
		{
			return false;
		}
		List<string> args = ChatCommands.SplitArguments(chatText.Substring(1)).Skip(1).ToList();
		if (args.Count == 0)
		{
			Toggle();
			return true;
		}
		string sub = args[0].ToLowerInvariant();
		if (sub == "--nick" || sub == "-n" || sub == "nick")
		{
			_sessionNicknames.Clear();
			if (args.Count > 1)
			{
				_sessionNicknames.AddRange(string.Join(" ", args.Skip(1)).Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select((string n) => n.Trim()).Where((string n) => n.Length > 0));
			}
			Enabled = true;
			Announce();
			return true;
		}
		string rest = string.Join(" ", args.Skip(1));
		switch (sub)
		{
		case "on":
			SetEnabled(enabled: true);
			break;
		case "off":
			SetEnabled(enabled: false);
			break;
		case "model":
			if (args.Count > 1)
			{
				Model = args[1];
				Save();
			}
			ClientChat.Info("AI model: " + Model + "  (suggested: " + string.Join(", ", SuggestedModels) + ")");
			break;
		case "key":
			if (args.Count > 1)
			{
				ApiKey = args[1];
				Save();
				ClientChat.Info("AI API key updated.");
			}
			break;
		case "prompt":
			if (args.Count > 1)
			{
				SystemPrompt = ((rest.ToLowerInvariant() == "default") ? "" : rest);
				Save();
			}
			ClientChat.Info("AI system prompt: " + (string.IsNullOrEmpty(SystemPrompt) ? ("(built-in, " + GameLanguageName() + ")") : SystemPrompt));
			break;
		case "about":
			if (args.Count > 1)
			{
				AboutMe = ((rest.ToLowerInvariant() == "clear") ? "" : rest);
				Save();
			}
			ClientChat.Info("About you: " + ((AboutMe.Length > 0) ? AboutMe : "(nothing, .ai about <text>)"));
			break;
		case "lang":
		case "language":
			if (args.Count > 1)
			{
				ReplyLanguage = ((rest.ToLowerInvariant() == "auto") ? "" : rest);
				Save();
			}
			ClientChat.Info("AI default language: " + TargetLanguage() + (string.IsNullOrEmpty(ReplyLanguage) ? " (game language)" : ""));
			break;
		case "everyone":
		case "all":
			ReplyToEveryone = !ReplyToEveryone;
			Save();
			ClientChat.Info("AI replies to " + (ReplyToEveryone ? "every message it thinks is worth answering" : "mentions only") + ".");
			break;
		case "forget":
		case "clear":
			_history.Clear();
			_conversations.Clear();
			ClientChat.Info("AI conversation memory cleared.");
			break;
		case "config":
		case "edit":
			OpenConfigFile();
			break;
		case "reload":
			Load();
			ClientChat.Info("Reloaded " + ConfigPath);
			break;
		case "test":
			RequestReply("tester", string.IsNullOrWhiteSpace(rest) ? ((GameLanguageCode() == "es") ? ("hola " + Main.LocalPlayer.name + ", estas?") : ("hi " + Main.LocalPlayer.name + ", are you there?")) : rest, sendToChat: false);
			break;
		default:
			ClientChat.Info("Usage: .ai | .afk [--nick a,b,c] | .ai on/off | .ai everyone | .ai about <text> | .ai lang <language|auto> | .ai model <name>");
			ClientChat.Info(".ai key <key> | .ai prompt <text|default> | .ai forget | .ai config | .ai reload | .ai test [message]    Config: " + ConfigPath);
			break;
		}
		return true;
	}

	#region Chat memory

	/// <summary>Hooked in ChatHelper.DisplayMessage (client): every chat line that reaches the chat box.</summary>
	public static void OnChatMessage(string rawText, byte author)
	{
		if (Main.netMode != 1 || Main.gameMenu || string.IsNullOrWhiteSpace(rawText))
		{
			return;
		}
		try
		{
			string text = _chatTag.Replace(rawText, "$1").Trim();
			string sender;
			string message;
			if (author < byte.MaxValue)
			{
				sender = Main.player[author].name;
				message = text;
			}
			else
			{
				Match match = _anglePattern.Match(text);
				if (!match.Success)
				{
					match = _senderPattern.Match(text);
				}
				if (!match.Success)
				{
					return;
				}
				sender = match.Groups["name"].Value.Trim();
				message = match.Groups["msg"].Value.Trim();
			}
			if (sender.Length == 0 || message.Length == 0)
			{
				return;
			}
			bool mine = author == Main.myPlayer || string.Equals(sender, Main.LocalPlayer.name, StringComparison.OrdinalIgnoreCase);
			bool byAI = false;
			if (mine)
			{
				lock (_pendingAIMessages)
				{
					int index = _pendingAIMessages.FindIndex((string m) => string.Equals(m.Trim(), message.Trim(), StringComparison.Ordinal));
					if (index >= 0)
					{
						byAI = true;
						_pendingAIMessages.RemoveAt(index);
					}
				}
			}
			AddHistory(new ChatEntry
			{
				Time = DateTime.Now,
				Sender = sender,
				Text = message,
				Mine = mine,
				ByAI = byAI
			});
			if (!Enabled || mine)
			{
				return;
			}
			bool mentioned = MentionsMe(message);
			bool followUp = _conversations.TryGetValue(sender, out var since) && (DateTime.UtcNow - since).TotalSeconds <= (double)FollowUpSeconds;
			if (!mentioned && !followUp && !ReplyToEveryone)
			{
				return;
			}
			if ((DateTime.UtcNow - _lastReply).TotalSeconds < (double)CooldownSeconds || _busy)
			{
				return;
			}
			// Only a direct mention forces an answer; follow-ups and "everyone" mode let the model decide to stay quiet.
			RequestReply(sender, message, sendToChat: true, mayStayQuiet: !mentioned);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static void AddHistory(ChatEntry entry)
	{
		lock (_history)
		{
			_history.Add(entry);
			DateTime oldest = DateTime.Now.AddMinutes(-ContextMinutes);
			while (_history.Count > 0 && (_history.Count > Math.Max(1, ContextMessages) || _history[0].Time < oldest))
			{
				_history.RemoveAt(0);
			}
		}
	}

	private static bool MentionsMe(string message)
	{
		foreach (string name in AllNames())
		{
			if (Regex.IsMatch(message, "(?<![\\p{L}\\p{N}])@?" + Regex.Escape(name) + "(?![\\p{L}\\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
			{
				return true;
			}
		}
		return false;
	}

	#endregion

	#region Prompt

	private static string GameLanguageCode()
	{
		try
		{
			return Language.ActiveCulture.CultureInfo.TwoLetterISOLanguageName;
		}
		catch
		{
			return "en";
		}
	}

	private static string GameLanguageName()
	{
		try
		{
			return new CultureInfo(GameLanguageCode()).EnglishName;
		}
		catch
		{
			return "English";
		}
	}

	private static string TargetLanguage()
	{
		if (!string.IsNullOrWhiteSpace(ReplyLanguage))
		{
			return ReplyLanguage.Trim();
		}
		return GameLanguageName();
	}

	private static bool Spanish => GameLanguageCode() == "es";

	private static string StyleRules()
	{
		List<string> rules = new List<string>();
		if (Spanish)
		{
			if (Lowercase)
			{
				rules.Add("escribe todo en minusculas");
			}
			if (RemoveAccents)
			{
				rules.Add("sin tildes ni signos de apertura (¿ ¡)");
			}
			if (RemoveEmojis)
			{
				rules.Add("sin emojis ni emoticonos");
			}
			rules.Add("sin saltos de linea");
			return "Estilo: " + string.Join(", ", rules) + ", como escribe la gente en un chat.";
		}
		if (Lowercase)
		{
			rules.Add("write everything in lowercase");
		}
		if (RemoveAccents)
		{
			rules.Add("no accents or diacritics");
		}
		if (RemoveEmojis)
		{
			rules.Add("no emojis or emoticons");
		}
		rules.Add("no line breaks");
		return "Style: " + string.Join(", ", rules) + ", like people type in a game chat.";
	}

	private static string GameInfo()
	{
		if (!IncludeGameInfo || Main.gameMenu)
		{
			return Spanish ? "(sin informacion)" : "(no info)";
		}
		Player me = Main.LocalPlayer;
		List<string> players = new List<string>();
		for (int i = 0; i < 255; i++)
		{
			if (Main.player[i].active && i != Main.myPlayer)
			{
				players.Add(Main.player[i].name);
			}
		}
		string item = ((me.HeldItem != null && !me.HeldItem.IsAir) ? me.HeldItem.Name : "-");
		string time = Utils.GetDayTimeAs24FloatStartingFromMidnight().ToString("0.0", CultureInfo.InvariantCulture);
		if (Spanish)
		{
			return "estas en el mundo \"" + Main.worldName + "\" (" + (Main.hardMode ? "hardmode" : "prehardmode") + ", hora del juego " + time + "h). Jugadores conectados: " + ((players.Count > 0) ? string.Join(", ", players) : "nadie mas") + ". Tu personaje: " + me.statLife + "/" + me.statLifeMax2 + " de vida" + (me.dead ? ", muerto" : "") + ", con " + item + " en la mano.";
		}
		return "you are in the world \"" + Main.worldName + "\" (" + (Main.hardMode ? "hardmode" : "pre-hardmode") + ", in-game time " + time + "h). Players online: " + ((players.Count > 0) ? string.Join(", ", players) : "nobody else") + ". Your character: " + me.statLife + "/" + me.statLifeMax2 + " HP" + (me.dead ? ", dead" : "") + ", holding " + item + ".";
	}

	private static string BuildSystemPrompt(string myName)
	{
		string nicks = string.Join(", ", AllNames().Skip(1).Distinct(StringComparer.OrdinalIgnoreCase));
		string template = (string.IsNullOrWhiteSpace(SystemPrompt) ? (Spanish ? PromptSpanish : PromptEnglish) : SystemPrompt);
		string about = ((AboutMe.Length > 0) ? ((Spanish ? "Sobre ti: " : "About you: ") + AboutMe) : "");
		string prompt = template.Replace("{name}", myName).Replace("{nicks}", (nicks.Length > 0) ? nicks : (Spanish ? "ninguno" : "none")).Replace("{about}", about).Replace("{game}", GameInfo()).Replace("{language}", TargetLanguage());
		return prompt + "\n" + StyleRules();
	}

	private static string BuildTranscript(string myName)
	{
		List<ChatEntry> entries;
		lock (_history)
		{
			entries = _history.ToList();
		}
		if (entries.Count == 0)
		{
			return "";
		}
		StringBuilder builder = new StringBuilder();
		foreach (ChatEntry entry in entries)
		{
			string who = (entry.Mine ? (myName + (entry.ByAI ? (Spanish ? " (tu, respuesta automatica)" : " (you, automatic reply)") : (Spanish ? " (tu)" : " (you)"))) : entry.Sender);
			builder.Append('[').Append(entry.Time.ToString("HH:mm")).Append("] ").Append(who).Append(": ").AppendLine(entry.Text);
		}
		return builder.ToString();
	}

	#endregion

	#region Session agents (MultiClient)

	/// <summary>
	/// The AFK AI of one MultiClient session: its own chat memory, cooldown and conversations, so every session with AI on
	/// answers by its own name even while you are not playing as it. Uses the same model, key and style settings.
	/// </summary>
	public sealed class Agent
	{
		internal readonly List<ChatEntry> History = new List<ChatEntry>();

		internal readonly Dictionary<string, DateTime> Conversations = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

		internal readonly List<string> Pending = new List<string>();

		internal DateTime LastReply = DateTime.MinValue;

		internal volatile bool Busy;
	}

	/// <summary>
	/// A chat line a headless session received. <paramref name="author"/> is the player slot for player chat (255 for server
	/// lines, which are parsed like "&lt;name&gt; text"). <paramref name="ownNames"/> are all your characters: they are never
	/// answered, so your sessions do not talk to each other in a loop.
	/// </summary>
	public static void OnSessionChat(Agent agent, string myName, bool authorIsMe, string authorName, string rawText, ICollection<string> ownNames, string gameInfo, Func<bool> stillOn, Action<string> say)
	{
		if (agent == null || string.IsNullOrWhiteSpace(rawText) || string.IsNullOrWhiteSpace(myName))
		{
			return;
		}
		try
		{
			string text = _chatTag.Replace(rawText, "$1").Trim();
			string sender;
			string message;
			if (!string.IsNullOrEmpty(authorName))
			{
				sender = authorName;
				message = text;
			}
			else
			{
				Match match = _anglePattern.Match(text);
				if (!match.Success)
				{
					match = _senderPattern.Match(text);
				}
				if (!match.Success)
				{
					return;
				}
				sender = match.Groups["name"].Value.Trim();
				message = match.Groups["msg"].Value.Trim();
			}
			if (sender.Length == 0 || message.Length == 0)
			{
				return;
			}
			bool mine = authorIsMe || string.Equals(sender, myName, StringComparison.OrdinalIgnoreCase);
			bool byAI = false;
			if (mine)
			{
				lock (agent.Pending)
				{
					int index = agent.Pending.FindIndex((string m) => string.Equals(m.Trim(), message.Trim(), StringComparison.Ordinal));
					if (index >= 0)
					{
						byAI = true;
						agent.Pending.RemoveAt(index);
					}
				}
			}
			lock (agent.History)
			{
				agent.History.Add(new ChatEntry
				{
					Time = DateTime.Now,
					Sender = sender,
					Text = message,
					Mine = mine,
					ByAI = byAI
				});
				DateTime oldest = DateTime.Now.AddMinutes(-ContextMinutes);
				while (agent.History.Count > 0 && (agent.History.Count > Math.Max(1, ContextMessages) || agent.History[0].Time < oldest))
				{
					agent.History.RemoveAt(0);
				}
			}
			if (mine || (ownNames != null && ownNames.Any((string n) => string.Equals(n, sender, StringComparison.OrdinalIgnoreCase))))
			{
				return;
			}
			bool mentioned = Regex.IsMatch(message, "(?<![\\p{L}\\p{N}])@?" + Regex.Escape(myName) + "(?![\\p{L}\\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
			bool followUp;
			lock (agent.Conversations)
			{
				followUp = agent.Conversations.TryGetValue(sender, out var since) && (DateTime.UtcNow - since).TotalSeconds <= (double)FollowUpSeconds;
			}
			if (!mentioned && !followUp && !ReplyToEveryone)
			{
				return;
			}
			if ((DateTime.UtcNow - agent.LastReply).TotalSeconds < (double)CooldownSeconds || agent.Busy)
			{
				return;
			}
			AgentReply(agent, myName, sender, message, !mentioned, gameInfo, stillOn, say);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static void AgentReply(Agent agent, string myName, string sender, string message, bool mayStayQuiet, string gameInfo, Func<bool> stillOn, Action<string> say)
	{
		agent.Busy = true;
		agent.LastReply = DateTime.UtcNow;
		string template = string.IsNullOrWhiteSpace(SystemPrompt) ? (Spanish ? PromptSpanish : PromptEnglish) : SystemPrompt;
		string system = template.Replace("{name}", myName).Replace("{nicks}", Spanish ? "ninguno" : "none").Replace("{about}", "").Replace("{game}", string.IsNullOrEmpty(gameInfo) ? (Spanish ? "(sin informacion)" : "(no info)") : gameInfo).Replace("{language}", TargetLanguage()) + "\n" + StyleRules();
		StringBuilder builder = new StringBuilder();
		lock (agent.History)
		{
			foreach (ChatEntry entry in agent.History)
			{
				string who = entry.Mine ? (myName + (entry.ByAI ? (Spanish ? " (tu, respuesta automatica)" : " (you, automatic reply)") : (Spanish ? " (tu)" : " (you)"))) : entry.Sender;
				builder.Append('[').Append(entry.Time.ToString("HH:mm")).Append("] ").Append(who).Append(": ").AppendLine(entry.Text);
			}
		}
		string transcript = builder.ToString();
		string instruction = Spanish
			? (((transcript.Length > 0) ? ("Conversacion reciente del chat (de mas antigua a mas nueva):\n" + transcript + "\n") : "") + "Mensaje nuevo de " + sender + ": " + message + "\nEscribe solo tu respuesta como " + myName + ", sin tu nombre delante." + (mayStayQuiet ? " Si el mensaje no va dirigido a ti o no hace falta que respondas, escribe exactamente [skip]." : ""))
			: (((transcript.Length > 0) ? ("Recent chat conversation (oldest first):\n" + transcript + "\n") : "") + "New message from " + sender + ": " + message + "\nWrite only your reply as " + myName + ", without your name in front." + (mayStayQuiet ? " If the message is not meant for you or does not need an answer, write exactly [skip]." : ""));
		Task.Run(delegate
		{
			try
			{
				DateTime started = DateTime.UtcNow;
				string reply = CallApi(system, instruction);
				if (reply.IndexOf("[skip]", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return;
				}
				reply = Clean(reply, myName);
				if (string.IsNullOrWhiteSpace(reply))
				{
					return;
				}
				int wait = (int)(ReplyDelaySeconds * 1000f - (float)(DateTime.UtcNow - started).TotalMilliseconds);
				if (wait > 0)
				{
					Thread.Sleep(wait);
				}
				if (stillOn != null && !stillOn())
				{
					return;
				}
				string text = ReplyPrefix + reply;
				lock (agent.Pending)
				{
					agent.Pending.Add(text);
					if (agent.Pending.Count > 20)
					{
						agent.Pending.RemoveAt(0);
					}
				}
				lock (agent.Conversations)
				{
					agent.Conversations[sender] = DateTime.UtcNow;
				}
				say(text);
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
			finally
			{
				agent.Busy = false;
			}
		});
	}

	#endregion

	#region Request

	private static void RequestReply(string sender, string message, bool sendToChat, bool mayStayQuiet = false)
	{
		_busy = true;
		_lastReply = DateTime.UtcNow;
		string myName = Main.LocalPlayer.name;
		string system = BuildSystemPrompt(myName);
		string transcript = BuildTranscript(myName);
		// Pictures in the message (short codes or links): [Image 1]... here, and the vision model says what they show.
		List<AskImage> pictures = AskVision.LinksIn(IsaClient.ChatImages.PictureCodes.Expand(message));
		if (pictures.Count > 0)
		{
			message = IsaClient.ChatImages.PictureCodes.Expand(message);
			for (int i = 0; i < pictures.Count; i++)
			{
				message = message.Replace(pictures[i].Url, "[Image " + (i + 1) + "]");
			}
		}
		string instruction;
		if (Spanish)
		{
			instruction = ((transcript.Length > 0) ? ("Conversacion reciente del chat (de mas antigua a mas nueva):\n" + transcript + "\n") : "") + "Mensaje nuevo de " + sender + ": " + message + "\nEscribe solo tu respuesta como " + myName + ", sin tu nombre delante." + (mayStayQuiet ? " Si el mensaje no va dirigido a ti o no hace falta que respondas, escribe exactamente [skip]." : "");
		}
		else
		{
			instruction = ((transcript.Length > 0) ? ("Recent chat conversation (oldest first):\n" + transcript + "\n") : "") + "New message from " + sender + ": " + message + "\nWrite only your reply as " + myName + ", without your name in front." + (mayStayQuiet ? " If the message is not meant for you or does not need an answer, write exactly [skip]." : "");
		}
		Task.Run(delegate
		{
			try
			{
				DateTime started = DateTime.UtcNow;
				if (pictures.Count > 0)
				{
					try
					{
						string seen = AskVision.Describe(pictures, message);
						if (!string.IsNullOrWhiteSpace(seen))
						{
							instruction += (Spanish ? "\nLo que muestran las imagenes del mensaje:\n" : "\nWhat the pictures in the message show:\n") + seen;
						}
					}
					catch (Exception)
					{
						// Without the pictures, as before.
					}
				}
				string reply = CallApi(system, instruction);
				if (reply.IndexOf("[skip]", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					if (!sendToChat)
					{
						Main.QueueMainThreadAction(delegate
						{
							ClientChat.Info("[AI] (would stay quiet)");
						});
					}
					return;
				}
				reply = Clean(reply, myName);
				if (string.IsNullOrWhiteSpace(reply))
				{
					return;
				}
				if (sendToChat)
				{
					// Looks more human: wait a bit (minus the time the request already took).
					int wait = (int)(ReplyDelaySeconds * 1000f - (float)(DateTime.UtcNow - started).TotalMilliseconds);
					if (wait > 0)
					{
						Thread.Sleep(wait);
					}
				}
				string text = ReplyPrefix + reply;
				Main.QueueMainThreadAction(delegate
				{
					if (sendToChat && Main.netMode == 1 && !Main.gameMenu && Enabled)
					{
						lock (_pendingAIMessages)
						{
							_pendingAIMessages.Add(text);
							if (_pendingAIMessages.Count > 20)
							{
								_pendingAIMessages.RemoveAt(0);
							}
						}
						_conversations[sender] = DateTime.UtcNow;
						ChatHelper.SendChatMessageFromClient(new ChatMessage(text));
					}
					else if (!sendToChat)
					{
						ClientChat.Info("[AI] " + text);
					}
				});
			}
			catch (Exception ex)
			{
				string error = ex.Message;
				if (ex is WebException ex2 && ex2.Response != null)
				{
					using StreamReader streamReader = new StreamReader(ex2.Response.GetResponseStream());
					error = error + " " + streamReader.ReadToEnd();
				}
				Main.QueueMainThreadAction(delegate
				{
					ClientChat.Error("[AI] Request failed: " + ((error.Length > 300) ? error.Substring(0, 300) : error));
				});
			}
			finally
			{
				_busy = false;
			}
		});
	}

	private static string CallApi(string system, string instruction)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		JArray messages = new JArray
		{
			new JObject
			{
				["role"] = "system",
				["content"] = system
			},
			new JObject
			{
				["role"] = "user",
				["content"] = instruction
			}
		};
		JObject body = new JObject
		{
			["model"] = Model,
			["messages"] = messages,
			["temperature"] = Temperature,
			["max_completion_tokens"] = 600
		};
		if (Model.Contains("gpt-oss"))
		{
			body["reasoning_effort"] = "low";
		}
		else if (Model.Contains("qwen"))
		{
			// Qwen thinking models: keep the reasoning out of the chat reply.
			body["reasoning_format"] = "hidden";
		}
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Endpoint);
		request.Method = "POST";
		request.ContentType = "application/json";
		request.Headers[HttpRequestHeader.Authorization] = "Bearer " + ApiKey;
		request.Timeout = 30000;
		byte[] payload = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
		using (Stream stream = request.GetRequestStream())
		{
			stream.Write(payload, 0, payload.Length);
		}
		using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
		using StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
		JObject result = JObject.Parse(reader.ReadToEnd());
		return (string)result["choices"]?[0]?["message"]?["content"] ?? "";
	}

	private static string Clean(string reply, string myName)
	{
		reply = Regex.Replace(reply ?? "", "\\s+", " ").Trim().Trim('"');
		foreach (string prefix in new string[2] { myName + ":", myName + " (" })
		{
			if (reply.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				int colon = reply.IndexOf(':');
				if (colon >= 0 && colon < prefix.Length + 30)
				{
					reply = reply.Substring(colon + 1).Trim();
				}
			}
		}
		if (RemoveEmojis)
		{
			StringBuilder builder = new StringBuilder(reply.Length);
			for (int i = 0; i < reply.Length; i++)
			{
				char c = reply[i];
				if (char.IsSurrogate(c))
				{
					continue;
				}
				UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
				if (category == UnicodeCategory.OtherSymbol || c == '\ufe0f' || c == '\u200d')
				{
					continue;
				}
				builder.Append(c);
			}
			reply = Regex.Replace(builder.ToString(), "\\s+", " ").Trim();
		}
		if (RemoveAccents)
		{
			StringBuilder builder2 = new StringBuilder(reply.Length);
			foreach (char c2 in reply)
			{
				if (c2 == 'ñ' || c2 == 'Ñ')
				{
					builder2.Append(c2);
					continue;
				}
				if (c2 == '¿' || c2 == '¡')
				{
					continue;
				}
				foreach (char c3 in c2.ToString().Normalize(NormalizationForm.FormD))
				{
					if (CharUnicodeInfo.GetUnicodeCategory(c3) != UnicodeCategory.NonSpacingMark)
					{
						builder2.Append(c3);
					}
				}
			}
			reply = builder2.ToString().Normalize(NormalizationForm.FormC).Trim();
		}
		if (Lowercase)
		{
			reply = reply.ToLower(CultureInfo.InvariantCulture);
		}
		if (reply.Length > MaxReplyLength)
		{
			reply = reply.Substring(0, MaxReplyLength).TrimEnd() + "...";
		}
		return reply;
	}

	#endregion
}
