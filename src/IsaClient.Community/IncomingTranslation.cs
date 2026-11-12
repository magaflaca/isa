using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IsaClient.Core;
using Terraria;
using Terraria.GameContent.UI.Chat;
using Terraria.UI.Chat;

namespace IsaClient.Community;

/// <summary>
/// Translated chat, the other way: messages that arrive are shown in your language, looking exactly as they came
/// (names, items and colors stay; the text inside [c/hex:text] is translated and keeps its color). A message waits a
/// moment for its translation and takes its place in order; if the service is slow it shows as it came and changes
/// when the translation arrives. Ctrl + right click on a message translates it (or shows the original again).
/// </summary>
public static class IncomingTranslation
{
	/// <summary>Who wrote a chat line.</summary>
	public enum Source
	{
		Player,
		Server,
		Client,
		Mine
	}

	[Setting("TranslateIncoming")]
	public static bool Enabled;

	/// <summary>The language messages are shown in ("game" = the game's language).</summary>
	[Setting("TranslateIncomingTo")]
	public static string ToCode = "game";

	[Setting("TranslateIncomingPlayers")]
	public static bool Players = true;

	[Setting("TranslateIncomingServer")]
	public static bool Server;

	[Setting("TranslateIncomingClient")]
	public static bool Client;

	[Setting("TranslateIncomingMine")]
	public static bool Mine;

	/// <summary>Hold a message (up to <see cref="HoldMilliseconds"/>) so it appears already translated.</summary>
	[Setting("TranslateIncomingWait")]
	public static bool Wait = true;

	[Setting("TranslateOriginalOnHover")]
	public static bool OriginalOnHover = true;

	[Setting("CtrlRightClickTranslates")]
	public static bool CtrlRightClick = true;

	private const int HoldMilliseconds = 2500;

	private const int CacheSize = 300;

	/// <summary>What ChatHelper.DisplayMessage is adding right now (null = the client's own output).</summary>
	private sealed class Arrival
	{
		public Source Source;

		public byte Author;

		public string Received;

		public int KeepPrefix;
	}

	/// <summary>A message waiting for its translation before it is put in the chat.</summary>
	private sealed class Held
	{
		public ChatMessageContainer Message;

		public List<ChatMessageContainer> Messages;

		/// <summary>The message that was newest when this one arrived: this one goes right above it.</summary>
		public ChatMessageContainer Below;

		public DateTime Until;
	}

	private sealed class Info
	{
		public int KeepPrefix;

		public bool Busy;
	}

	private static Arrival _arriving;

	private static readonly List<Held> _held = new List<Held>();

	private static readonly ConditionalWeakTable<ChatMessageContainer, Info> _infos = new ConditionalWeakTable<ChatMessageContainer, Info>();

	private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>();

	private static readonly Queue<string> _cacheOrder = new Queue<string>();

	private static readonly SemaphoreSlim _parallel = new SemaphoreSlim(3);

	/// <summary>Tags that are not words ([n:..], [i:..], [a:..], [g:..]); color tags have words inside.</summary>
	private static readonly Regex NonWordTags = new Regex(@"\[(?!c/|color/)(?:\\.|[^\]\\\r\n])*\]|https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	private static double _lastFailureNote = double.MinValue;

	/// <summary>What was picked: a language, or <see cref="ChatLanguages.Game"/>.</summary>
	public static ChatLanguage Choice
	{
		get => ChatLanguages.Find(ToCode, allowGame: true) ?? ChatLanguages.Game;
		set => ToCode = value.Code;
	}

	/// <summary>The language messages are translated to (the game's one when that is the choice).</summary>
	public static ChatLanguage To => (Choice == ChatLanguages.Game) ? ChatLanguages.GameLanguage : Choice;

	public static bool Allowed(Source source)
	{
		return source switch
		{
			Source.Player => Players,
			Source.Server => Server,
			Source.Mine => Mine,
			_ => Client
		};
	}

	#region Hooks

	/// <summary>ChatHelper.DisplayMessage, before it adds the line (<paramref name="received"/> without the name tag).</summary>
	public static void BeginMessage(string received, byte author)
	{
		Arrival arrival = new Arrival { Author = author, Received = received };
		if (author < byte.MaxValue)
		{
			arrival.Source = (author == Main.myPlayer) ? Source.Mine : Source.Player;
		}
		else
		{
			// Servers like TShock send chat as their own message: "[Admin] Name: text".
			int player = FindSpeaker(received, out arrival.KeepPrefix);
			arrival.Source = (player < 0) ? Source.Server : ((player == Main.myPlayer) ? Source.Mine : Source.Player);
		}
		_arriving = arrival;
	}

	public static void EndMessage()
	{
		_arriving = null;
	}

	/// <summary>
	/// The chat monitor is adding <paramref name="message"/>. Starts its translation when it should be translated;
	/// true = the message is held and will be put in <paramref name="messages"/> later.
	/// </summary>
	public static bool TryHold(ChatMessageContainer message, List<ChatMessageContainer> messages)
	{
		Arrival arrival = _arriving;
		_arriving = null;
		Source source = arrival?.Source ?? Source.Client;
		int keepPrefix = arrival?.KeepPrefix ?? 0;
		_infos.GetValue(message, _ => new Info()).KeepPrefix = keepPrefix;
		if (!Enabled || !Allowed(source) || !WorthTranslating(message.OriginalText, keepPrefix))
		{
			return false;
		}
		ChatLanguage to = To;
		string text = message.OriginalText;
		if (TryCached(text, to, out string cached))
		{
			Apply(message, text, cached, arrival);
			return false;
		}
		Held held = null;
		if (Wait)
		{
			held = new Held
			{
				Message = message,
				Messages = messages,
				Below = (_held.Count > 0) ? _held[_held.Count - 1].Message : ((messages.Count > 0) ? messages[0] : null),
				Until = DateTime.UtcNow.AddMilliseconds(HoldMilliseconds)
			};
			_held.Add(held);
		}
		Translate(text, keepPrefix, to, delegate(string translated, string error)
		{
			if (translated != null)
			{
				Apply(message, text, translated, arrival);
			}
			else if (error != null)
			{
				NoteFailure(error);
			}
			if (held != null)
			{
				Release(held);
			}
		});
		return held != null;
	}

	/// <summary>Every frame (chat monitor update): messages that waited too long show as they came.</summary>
	public static void Update()
	{
		if (_held.Count == 0)
		{
			return;
		}
		DateTime now = DateTime.UtcNow;
		foreach (Held held in _held.Where(h => now >= h.Until).ToList())
		{
			Release(held);
		}
	}

	#endregion

	#region Ctrl + right click

	/// <summary>Translates one message, or shows its original again; returns the note shown by the mouse.</summary>
	public static string Toggle(ChatMessageContainer message)
	{
		if (message.ShowsTranslation)
		{
			message.ShowInstead(null);
			return "Original";
		}
		Info info = _infos.GetValue(message, _ => new Info());
		if (info.Busy)
		{
			return "Translating...";
		}
		string text = message.OriginalText;
		int keepPrefix = info.KeepPrefix;
		if (keepPrefix == 0)
		{
			FindSpeaker(text, out keepPrefix);
		}
		if (!WorthTranslating(text, keepPrefix))
		{
			return "Nothing to translate";
		}
		ChatLanguage to = To;
		if (TryCached(text, to, out string cached))
		{
			return Show(message, text, cached, to);
		}
		info.Busy = true;
		Translate(text, keepPrefix, to, delegate(string translated, string error)
		{
			info.Busy = false;
			if (translated == null)
			{
				ChatCopy.Note("Could not translate");
				ClientChat.Error("Could not translate: " + (error ?? "the translator gave back nothing"));
				return;
			}
			ChatCopy.Note(Show(message, text, translated, to));
		});
		return "Translating...";
	}

	private static string Show(ChatMessageContainer message, string text, string translated, ChatLanguage to)
	{
		if (Same(text, translated))
		{
			return "Already in " + to.Name;
		}
		message.ShowInstead(translated);
		return "Translated";
	}

	#endregion

	#region Translation

	private static void Translate(string text, int keepPrefix, ChatLanguage to, Action<string, string> done)
	{
		Task.Run(delegate
		{
			string translated = null;
			string error = null;
			_parallel.Wait();
			try
			{
				translated = ChatTranslate.TranslateMessage(text, ChatLanguages.Auto, to, insideColorTags: true, keepPrefix);
				// An empty answer would turn the message into a blank line: it stays as it came.
				if (Blank(translated, keepPrefix))
				{
					translated = null;
				}
				else
				{
					Remember(text, to, translated);
				}
			}
			catch (Exception ex)
			{
				error = ex.Message;
			}
			finally
			{
				_parallel.Release();
			}
			Main.QueueMainThreadAction(() => done(translated, error));
		});
	}

	private static void Apply(ChatMessageContainer message, string text, string translated, Arrival arrival)
	{
		// Changed meanwhile (merged spam) or already in the right language.
		if (message.OriginalText != text || Same(text, translated))
		{
			return;
		}
		message.ShowInstead(translated);
		if (arrival == null || arrival.Author >= byte.MaxValue)
		{
			return;
		}
		// The bubble over the player's head too.
		Player player = Main.player[arrival.Author];
		string tag = NameTagHandler.GenerateTag(player.name) + " ";
		if (player.chatOverhead.timeLeft > 0 && player.chatOverhead.chatText == arrival.Received && translated.StartsWith(tag, StringComparison.Ordinal))
		{
			player.chatOverhead.NewMessage(translated.Substring(tag.Length), player.chatOverhead.timeLeft);
		}
	}

	/// <summary>Puts a held message in the chat, right above the one that was newest when it arrived.</summary>
	private static void Release(Held held)
	{
		if (!_held.Remove(held))
		{
			return;
		}
		ChatMessageContainer below = held.Below;
		// Nothing below: it was the first message, it goes to the bottom.
		bool bottom = below == null;
		// The one below may still be waiting too: then go above whatever it is waiting above.
		while (below != null && !held.Messages.Contains(below))
		{
			Held waiting = _held.FirstOrDefault(h => h.Message == below);
			if (waiting == null)
			{
				// Gone (the chat was cleared): newest.
				below = null;
				break;
			}
			below = waiting.Below;
			bottom = below == null;
		}
		int index = (below != null) ? held.Messages.IndexOf(below) : (bottom ? held.Messages.Count : 0);
		held.Messages.Insert(index, held.Message);
		while (held.Messages.Count > 500)
		{
			held.Messages.RemoveAt(held.Messages.Count - 1);
		}
	}

	private static bool TryCached(string text, ChatLanguage to, out string translated)
	{
		lock (_cache)
		{
			return _cache.TryGetValue(to.Code + "|" + text, out translated);
		}
	}

	private static void Remember(string text, ChatLanguage to, string translated)
	{
		lock (_cache)
		{
			string key = to.Code + "|" + text;
			if (_cache.ContainsKey(key))
			{
				return;
			}
			_cache[key] = translated;
			_cacheOrder.Enqueue(key);
			while (_cacheOrder.Count > CacheSize)
			{
				_cache.Remove(_cacheOrder.Dequeue());
			}
		}
	}

	private static void NoteFailure(string error)
	{
		// Once a minute at most: a dead connection must not flood the chat.
		if (Main.timeForVisualEffects - _lastFailureNote < 3600.0)
		{
			return;
		}
		_lastFailureNote = Main.timeForVisualEffects;
		ClientChat.Notice("Could not translate the chat (" + error + "); messages show as they came.");
	}

	#endregion

	#region Helpers

	/// <summary>Nothing to read after the speaker's prefix (colour tags with nothing inside included).</summary>
	private static bool Blank(string text, int keepPrefix)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}
		string rest = (keepPrefix > 0 && keepPrefix < text.Length) ? text.Substring(keepPrefix) : ((keepPrefix >= text.Length) ? "" : text);
		return string.IsNullOrWhiteSpace(EmptyColorTags.Replace(rest, ""));
	}

	private static readonly Regex EmptyColorTags = new Regex(@"\[c(?:olor)?/[0-9A-Fa-f]{6}:\s*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	/// <summary>At least three letters of words ("gg" and "xd" stay as they are).</summary>
	private static bool WorthTranslating(string text, int keepPrefix)
	{
		if (string.IsNullOrWhiteSpace(text) || keepPrefix >= text.Length)
		{
			return false;
		}
		// Words IsaClient sent with the pipe off would be translated into nonsense: those lines stay as they are.
		if (IsaClient.ChatImages.ChatData.Find(text, out _, out _))
		{
			return false;
		}
		return NonWordTags.Replace(text.Substring(keepPrefix), " ").Count(char.IsLetter) >= 3;
	}

	private static bool Same(string a, string b)
	{
		return string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>"[Admin] Name: hi": the player who speaks and how long "[Admin] Name: " is; -1 when no player name leads the text.</summary>
	private static int FindSpeaker(string text, out int prefixLength)
	{
		prefixLength = 0;
		int found = -1;
		int best = int.MaxValue;
		for (int i = 0; i < Main.maxPlayers; i++)
		{
			Player player = Main.player[i];
			if (player == null || !player.active || string.IsNullOrEmpty(player.name))
			{
				continue;
			}
			int at = text.IndexOf(player.name + ":", StringComparison.Ordinal);
			if (at < 0 || at > 48 || at >= best)
			{
				continue;
			}
			best = at;
			found = i;
			prefixLength = at + player.name.Length + 1;
		}
		while (found >= 0 && prefixLength < text.Length && text[prefixLength] == ' ')
		{
			prefixLength++;
		}
		return found;
	}

	#endregion

	#region Chat commands

	/// <summary>.translate in [on|off] | &lt;language&gt; | players|server|client|mine [on|off]</summary>
	internal static void Command(string[] args)
	{
		string word = (args.Length > 0) ? args[0].ToLowerInvariant() : "";
		bool? value = (args.Length > 1) ? ((args[1].ToLowerInvariant() == "on") ? true : ((args[1].ToLowerInvariant() == "off") ? false : null)) : null;
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
		case "players":
			Players = value ?? !Players;
			break;
		case "server":
			Server = value ?? !Server;
			break;
		case "client":
			Client = value ?? !Client;
			break;
		case "mine":
		case "me":
			Mine = value ?? !Mine;
			break;
		default:
			ChatLanguage language = ChatLanguages.Find(args[0], allowGame: true);
			if (language == null)
			{
				ClientChat.Error("Unknown language '" + args[0] + "'. Usage: .translate in <language|game> | on | off | players | server | client | mine [on|off]");
				return;
			}
			Choice = language;
			Enabled = true;
			break;
		}
		ClientSettings.Save();
		ClientChat.Info("Incoming translation " + (Enabled ? "on" : "off") + ": messages in " + To.Name + ((Choice == ChatLanguages.Game) ? " (the game's language)" : "") + ". Translating: " + Filters() + ". Ctrl + right click a message to translate just that one.");
	}

	/// <summary>"players, server" (what gets translated).</summary>
	public static string Filters()
	{
		List<string> list = new List<string>();
		if (Players)
		{
			list.Add("players");
		}
		if (Mine)
		{
			list.Add("your messages");
		}
		if (Server)
		{
			list.Add("server");
		}
		if (Client)
		{
			list.Add("client");
		}
		return (list.Count == 0) ? "nothing" : string.Join(", ", list);
	}

	#endregion
}
