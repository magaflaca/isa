using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria;

namespace IsaClient.Assistant.PluginMaker;

/// <summary>
/// The Plugin Maker: a coding agent that makes and changes plugins while you play. One conversation at a time (saved,
/// and others can be resumed); each message runs on its own thread: a quick router decides how to handle it, then the
/// model works with the tools (read, write, build, look the API up) until it has an answer. Models that fail hand over
/// to the next of the chain. Stop once: it stops after the step it's on; twice: right away. Messages starting with /
/// are commands (/model, /effort, /goal, /new, /resume...). The panel page and .pluginai show the same conversation.
/// </summary>
public static class PluginAgent
{
	public enum EntryKind
	{
		User,
		Assistant,
		Step,
		Note,
		Error
	}

	public sealed class Entry
	{
		public EntryKind Kind;

		public string Text;

		public string Detail;

		public bool Failed;

		public string Plugin;

		public DateTime Time = DateTime.Now;
	}

	/// <summary>A saved conversation (IsaClient/PluginMaker/Chats/&lt;id&gt;.json).</summary>
	public sealed class Conversation
	{
		public string Id;

		public string Title = "";

		public string Goal = "";

		public DateTime Created = DateTime.Now;

		public DateTime Updated = DateTime.Now;

		public List<Entry> Entries = new List<Entry>();

		/// <summary>The messages as the model sees them (pictures as notes).</summary>
		public string History = "[]";

		/// <summary>How files were before the agent changed them, by message (for /rewind).</summary>
		public List<FileCheckpoint> Checkpoints = new List<FileCheckpoint>();

		[JsonIgnore]
		public int Messages => Entries.Count(e => e.Kind == EntryKind.User);
	}

	/// <summary>A file as it was before message <see cref="Turn"/> changed it (null content: it didn't exist).</summary>
	public sealed class FileCheckpoint
	{
		public int Turn;

		public string Path;

		public string Content;
	}

	private sealed class Message
	{
		public string Text;

		public bool ToChat;

		public List<AskImage> Images;
	}

	/// <summary>A slash command: name, arguments, what it does.</summary>
	public sealed class SlashCommand
	{
		public string Name;

		public string Args;

		public string Help;
	}

	public static readonly SlashCommand[] Commands =
	{
		new SlashCommand { Name = "/model", Args = "[name | auto]", Help = "The model it uses (alone: the list)." },
		new SlashCommand { Name = "/effort", Args = "[low | medium | high]", Help = "How thorough it is (and how much the models that can think do)." },
		new SlashCommand { Name = "/goal", Args = "[text | clear]", Help = "What this conversation is about: it keeps it in mind in every message." },
		new SlashCommand { Name = "/new", Args = "", Help = "A new conversation (this one is saved)." },
		new SlashCommand { Name = "/clear", Args = "", Help = "Same as /new." },
		new SlashCommand { Name = "/resume", Args = "[number | words]", Help = "Goes back to a saved conversation (alone: the list)." },
		new SlashCommand { Name = "/retry", Args = "", Help = "Sends your last message again." },
		new SlashCommand { Name = "/rewind", Args = "[how many] [chat]", Help = "Goes back before your last message (or several): the conversation and the files it changed since (chat: only the conversation)." },
		new SlashCommand { Name = "/stop", Args = "", Help = "Stops it (twice: right away)." },
		new SlashCommand { Name = "/plugins", Args = "", Help = "Lists your plugins." },
		new SlashCommand { Name = "/folder", Args = "", Help = "Opens Plugins/Sources." },
		new SlashCommand { Name = "/help", Args = "", Help = "These commands." }
	};

	public const int MaxPictures = 5;

	private static readonly List<Entry> _transcript = new List<Entry>();

	private static readonly List<JObject> _history = new List<JObject>();

	private static readonly Queue<Message> _queue = new Queue<Message>();

	private static readonly object _lock = new object();

	private static volatile bool _stop;

	/// <summary>Grows with every message started and every hard stop: a thread of an older one stops touching anything.</summary>
	private static int _generation;

	private static Conversation _conversation = NewConversation();

	private static int _tokens;

	public static int Version { get; private set; }

	public static bool Busy { get; private set; }

	public static bool Stopping => Busy && _stop;

	public static string ModelLabel { get; private set; } = "";

	public static DateTime StartedAt { get; private set; }

	public static int StepCount { get; private set; }

	public static string TaskTitle { get; private set; } = "";

	public static string LastPlugin { get; private set; }

	public static int Tokens => _tokens;

	public static Conversation Current => _conversation;

	public static string Goal => _conversation.Goal ?? "";

	public static bool HasAnyKey => AiProvider.All.Any(p => p.HasKey);

	public static List<Entry> Transcript
	{
		get
		{
			lock (_lock)
			{
				return _transcript.ToList();
			}
		}
	}

	internal static void AddTokens(int count)
	{
		Interlocked.Add(ref _tokens, Math.Max(0, count));
	}

	private static Conversation NewConversation()
	{
		return new Conversation { Id = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 4) };
	}

	#region Sending

	/// <summary>Sends a message, with pictures if any (queued when it's still working). "/..." runs a command.</summary>
	public static void Send(string text, bool toChat, List<AskImage> images = null)
	{
		text = (text ?? "").Trim();
		PluginAiSettings.EnsureLoaded();
		if (text.StartsWith("/") && Slash(text, toChat))
		{
			return;
		}
		images = (images ?? new List<AskImage>()).Where(i => i != null && (i.Data != null || i.Url != null)).Take(MaxPictures).ToList();
		if (text.Length == 0 && images.Count == 0)
		{
			return;
		}
		if (text.Length == 0)
		{
			text = "Look at this picture.";
		}
		text = HideKeys(text, out bool hidden);
		if (hidden)
		{
			Note("Your message had an API key in it: it was hidden before sending (keys go in AI Assistant > Connection).", toChat);
		}
		Message message = new Message { Text = text, ToChat = toChat, Images = images };
		lock (_lock)
		{
			if (Busy)
			{
				_queue.Enqueue(message);
				AddLocked(new Entry { Kind = EntryKind.Note, Text = "Queued: it reads this when it finishes what it's doing." });
				return;
			}
			Busy = true;
		}
		Start(message);
	}

	private static readonly Regex _keys = new Regex(@"(?:nvapi-[A-Za-z0-9_\-]{20,}|gsk_[A-Za-z0-9]{20,}|mstrl_[A-Za-z0-9_\-]{16,}|sk-[A-Za-z0-9_\-]{20,})", RegexOptions.Compiled);

	/// <summary>API keys pasted by mistake never leave the game.</summary>
	private static string HideKeys(string text, out bool hidden)
	{
		bool found = false;
		string result = _keys.Replace(text, m =>
		{
			found = true;
			return "(key hidden)";
		});
		hidden = found;
		return result;
	}

	/// <summary>Stop: once, after the step it's on (the answer being waited for is dropped); twice, right away.</summary>
	public static void Stop()
	{
		if (!Busy)
		{
			return;
		}
		if (_stop)
		{
			HardStop();
			return;
		}
		_stop = true;
		lock (_lock)
		{
			_queue.Clear();
		}
		AiCall.AbortCurrent();
		Add(new Entry { Kind = EntryKind.Note, Text = "Stopping after this step... (Stop again to stop right now)" });
		// A step that takes long (a build, a slow tool): it doesn't keep you waiting.
		int generation = _generation;
		_stopTimer?.Dispose();
		_stopTimer = new Timer(delegate
		{
			if (Busy && _stop && _generation == generation)
			{
				HardStop();
			}
		}, null, 4000, Timeout.Infinite);
	}

	private static volatile bool _switchModel;

	/// <summary>Kept so the collector doesn't take the timer before it fires.</summary>
	private static Timer _stopTimer;

	/// <summary>The model was changed while working: the answer being waited for is dropped and the step is asked again of the new model.</summary>
	public static void ModelChanged()
	{
		if (Busy && !_stop)
		{
			_switchModel = true;
			AiCall.AbortCurrent();
		}
	}

	private static void HardStop()
	{
		lock (_lock)
		{
			_generation++;
			_queue.Clear();
			Busy = false;
			CloseToolCalls();
			AddLocked(new Entry { Kind = EntryKind.Note, Text = "Stopped." });
		}
		AiCall.AbortCurrent();
		Main.QueueMainThreadAction(AskStatus.Stop);
		Save();
	}

	private static void Start(Message message)
	{
		int generation;
		lock (_lock)
		{
			generation = ++_generation;
		}
		string text = message.Text;
		bool toChat = message.ToChat;
		_stop = false;
		_switchModel = false;
		StartedAt = DateTime.Now;
		StepCount = 0;
		_tokens = 0;
		if (string.IsNullOrEmpty(_conversation.Title))
		{
			_conversation.Title = Flat(text, 60);
		}
		Add(new Entry { Kind = EntryKind.User, Text = text, Detail = (message.Images.Count > 0) ? ("+" + message.Images.Count + ((message.Images.Count == 1) ? " picture" : " pictures")) : null });
		lock (_lock)
		{
			_turn = _transcript.Count(e => e.Kind == EntryKind.User);
		}
		if (toChat)
		{
			Main.QueueMainThreadAction(delegate
			{
				if (PluginAiSettings.EchoToChat)
				{
					Color muted = AskAssistant.MutedColor;
					Main.NewText(IsaClient.Languages.L10n.T(ClientChat.Tint("You: ", new Color(150, 220, 255)) + text.Replace("[", "(").Replace("]", ")") + ((message.Images.Count > 0) ? ("  (+" + message.Images.Count + " pictures)") : "")), muted.R, muted.G, muted.B);
				}
				AskStatus.Start();
			});
		}
		Thread thread = new Thread((ThreadStart)delegate
		{
			try
			{
				Work(generation, text, toChat, message.Images);
			}
			catch (Exception ex)
			{
				if (Alive(generation))
				{
					ClientLog.LogException(ex);
					Fail(generation, "Something went wrong: " + ex.Message, toChat);
				}
			}
			finally
			{
				Finish(generation, toChat);
			}
		});
		thread.IsBackground = true;
		thread.Name = "Plugin Maker";
		thread.Start();
	}

	private static void Finish(int generation, bool toChat)
	{
		Message next = null;
		lock (_lock)
		{
			if (generation != _generation)
			{
				// A hard stop already ended this one.
				return;
			}
			CloseToolCalls();
			if (_queue.Count > 0 && !_stop)
			{
				next = _queue.Dequeue();
			}
			else
			{
				Busy = false;
				Version++;
			}
		}
		if (toChat)
		{
			Main.QueueMainThreadAction(AskStatus.Stop);
		}
		Save();
		if (next != null)
		{
			Start(next);
		}
	}

	private static bool Alive(int generation) => generation == _generation;

	#endregion

	#region Transcript

	private static void Add(Entry entry)
	{
		lock (_lock)
		{
			AddLocked(entry);
		}
	}

	private static void AddLocked(Entry entry)
	{
		_transcript.Add(entry);
		if (_transcript.Count > 800)
		{
			_transcript.RemoveRange(0, 100);
		}
		Version++;
	}

	/// <summary>An entry from the worker of <paramref name="generation"/>, dropped when that work was stopped.</summary>
	private static void Add(int generation, Entry entry)
	{
		lock (_lock)
		{
			if (generation == _generation)
			{
				AddLocked(entry);
			}
		}
	}

	private static void Note(string text, bool toChat)
	{
		Add(new Entry { Kind = EntryKind.Note, Text = text });
		if (toChat)
		{
			Main.QueueMainThreadAction(() => ClientChat.Info("Plugin Maker: " + text));
		}
	}

	private static void Fail(int generation, string text, bool toChat)
	{
		if (!Alive(generation))
		{
			return;
		}
		Add(generation, new Entry { Kind = EntryKind.Error, Text = text });
		if (toChat)
		{
			Main.QueueMainThreadAction(() => ClientChat.Error("Plugin Maker: " + text));
		}
	}

	#endregion

	#region The work

	private static void Work(int generation, string text, bool toChat, List<AskImage> images)
	{
		if (!HasAnyKey)
		{
			Fail(generation, "There's no API key yet: put your NVIDIA key in AI Assistant > Connection (free at build.nvidia.com).", toChat);
			return;
		}
		string route = "work";
		string focus = null;
		if (PluginAiSettings.Router)
		{
			Route(generation, text, out route, out focus);
		}
		if (_stop || !Alive(generation))
		{
			return;
		}
		JArray messages = new JArray
		{
			new JObject { ["role"] = "system", ["content"] = PluginPrompt.System },
			new JObject { ["role"] = "system", ["content"] = PluginPrompt.State(focus ?? LastPlugin, Goal, PluginAiSettings.Effort) }
		};
		lock (_lock)
		{
			CloseToolCalls();
			_history.Add(new JObject { ["role"] = "user", ["content"] = (images.Count > 0) ? (JToken)AskVision.WithImages(text, images) : text });
			Compact();
			foreach (JObject m in _history)
			{
				messages.Add(m.DeepClone());
			}
		}
		List<AiModel> chain = AiModel.MainChain();
		if (route == "quick" && PluginAiSettings.Model == "auto")
		{
			chain = AiModel.RouterModels().Take(1).Concat(chain).Distinct().ToList();
		}
		if (images.Count > 0)
		{
			// Pictures: the models that see them first.
			chain = chain.Where(m => m.Sees).Concat(chain.Where(m => !m.Sees)).ToList();
		}
		if (chain.Count == 0)
		{
			Fail(generation, "No model has a key: add your NVIDIA key in AI Assistant > Connection.", toChat);
			return;
		}
		JArray tools = PluginTools.Schemas();
		int maxSteps = (route == "quick") ? Math.Min(8, PluginAiSettings.MaxSteps) : PluginAiSettings.MaxSteps;
		int maxTokens = (PluginAiSettings.Effort == "low") ? 4096 : ((PluginAiSettings.Effort == "high") ? 16384 : 8192);
		int modelIndex = 0;
		int broken = 0;
		for (int step = 0; step <= maxSteps; step++)
		{
			if (_stop || !Alive(generation))
			{
				Add(generation, new Entry { Kind = EntryKind.Note, Text = "Stopped." });
				return;
			}
			bool last = step == maxSteps;
			JObject reply = null;
			while (reply == null)
			{
				if (modelIndex >= chain.Count)
				{
					Fail(generation, "No model could answer right now (all of them failed). Try again in a moment, or pick another model (/model).", toChat);
					return;
				}
				AiModel model = chain[modelIndex];
				ModelLabel = model.Label;
				Version++;
				try
				{
					reply = AiCall.Send(model, messages, last ? null : tools, maxTokens, PluginAiSettings.TimeoutSeconds * 1000, () => _stop || !Alive(generation));
				}
				catch (AssistantException ex)
				{
					if (_stop || !Alive(generation))
					{
						return;
					}
					if (_switchModel)
					{
						// The step again, with the model just chosen.
						_switchModel = false;
						chain = AiModel.MainChain();
						if (images.Count > 0)
						{
							chain = chain.Where(m => m.Sees).Concat(chain.Where(m => !m.Sees)).ToList();
						}
						modelIndex = 0;
						broken = 0;
						if (chain.Count > 0)
						{
							ModelLabel = chain[0].Label;
							Add(generation, new Entry { Kind = EntryKind.Note, Text = "Switched to " + chain[0].Label + "." });
						}
						continue;
					}
					modelIndex++;
					broken = 0;
					string next = (modelIndex < chain.Count) ? (", trying " + chain[modelIndex].Label) : "";
					Add(generation, new Entry { Kind = EntryKind.Note, Text = ex.Message + next + "." });
					continue;
				}
				// A reply with the model's own tool-call markup as text ("<invoke ...", "]<]minimax[>"): asked again,
				// then the next model.
				if (Broken(reply))
				{
					broken++;
					reply = null;
					if (broken >= 2)
					{
						modelIndex++;
						broken = 0;
						if (modelIndex < chain.Count)
						{
							Add(generation, new Entry { Kind = EntryKind.Note, Text = chain[modelIndex - 1].Label + " answered garbled text, trying " + chain[modelIndex].Label + "." });
						}
					}
					else
					{
						messages.Add(new JObject { ["role"] = "user", ["content"] = "(Your last reply came out as broken tool-call markup. Call the tools through the function calling interface, or answer in plain text.)" });
					}
				}
			}
			if (!Alive(generation))
			{
				return;
			}
			string content = Clean((string)reply["content"]);
			JArray calls = reply["tool_calls"] as JArray;
			JObject assistant = new JObject { ["role"] = "assistant", ["content"] = content ?? "" };
			if (calls != null && calls.Count > 0)
			{
				assistant["tool_calls"] = calls;
			}
			messages.Add(assistant);
			lock (_lock)
			{
				if (!Alive(generation))
				{
					return;
				}
				_history.Add((JObject)assistant.DeepClone());
			}
			if (calls == null || calls.Count == 0)
			{
				string answer = string.IsNullOrWhiteSpace(content) ? "(no answer)" : content.Trim();
				Add(generation, new Entry { Kind = EntryKind.Assistant, Text = answer });
				if (toChat)
				{
					Main.QueueMainThreadAction(() => Print(answer));
				}
				return;
			}
			if (!string.IsNullOrWhiteSpace(content))
			{
				Add(generation, new Entry { Kind = EntryKind.Assistant, Text = content.Trim() });
				if (toChat)
				{
					string said = content.Trim();
					Main.QueueMainThreadAction(() => AskStatus.Detail(Flat(said, 200)));
				}
			}
			foreach (JToken call in calls)
			{
				if (_stop || !Alive(generation))
				{
					break;
				}
				string id = (string)call["id"] ?? ("call" + step);
				string name = (string)call["function"]?["name"] ?? "";
				string result;
				PluginTools.Step done;
				try
				{
					JToken raw = call["function"]?["arguments"];
					JObject args = (raw is JObject o) ? o : JObject.Parse(string.IsNullOrWhiteSpace((string)raw) ? "{}" : (string)raw);
					result = PluginTools.Run(name, args, () => _stop || !Alive(generation), out done);
				}
				catch (Exception ex)
				{
					done = new PluginTools.Step { Title = name, Failed = true, Detail = "bad arguments" };
					result = "Error: the arguments are not valid JSON (" + ex.Message + "). Call the tool again with valid JSON.";
				}
				if (!Alive(generation))
				{
					return;
				}
				StepCount++;
				if (done.Plugin != null)
				{
					LastPlugin = done.Plugin;
				}
				Add(generation, new Entry { Kind = EntryKind.Step, Text = done.Title, Detail = done.Detail, Failed = done.Failed, Plugin = done.Plugin });
				if (toChat)
				{
					string line = (done.Failed ? "x " : "- ") + done.Title + ((done.Detail != null) ? ("  (" + done.Detail + ")") : "");
					Main.QueueMainThreadAction(() => AskStatus.Detail(line));
				}
				JObject toolMessage = new JObject { ["role"] = "tool", ["tool_call_id"] = id, ["name"] = name, ["content"] = result ?? "" };
				messages.Add(toolMessage);
				lock (_lock)
				{
					if (!Alive(generation))
					{
						return;
					}
					_history.Add((JObject)toolMessage.DeepClone());
				}
			}
		}
	}

	private static readonly string[] _brokenMarks = { "<invoke", "</invoke>", "<tool_call", "</tool_call>", "</query>", "<parameter name", "minimax:tool_call", "]<]", "[>[", "<|tool", "<function=" };

	private static bool Broken(JObject reply)
	{
		if (reply["tool_calls"] is JArray calls && calls.Count > 0)
		{
			return false;
		}
		string content = (string)reply["content"] ?? "";
		return _brokenMarks.Any(m => content.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);
	}

	/// <summary>Every tool call needs its answer before the next message (a stop can leave some without one).</summary>
	private static void CloseToolCalls()
	{
		for (int i = 0; i < _history.Count; i++)
		{
			if ((string)_history[i]["role"] != "assistant" || !(_history[i]["tool_calls"] is JArray calls))
			{
				continue;
			}
			int end = i + 1;
			while (end < _history.Count && (string)_history[end]["role"] == "tool")
			{
				end++;
			}
			HashSet<string> answered = new HashSet<string>();
			for (int j = i + 1; j < end; j++)
			{
				answered.Add((string)_history[j]["tool_call_id"] ?? "");
			}
			foreach (JToken call in calls)
			{
				string id = (string)call["id"] ?? "";
				if (!answered.Contains(id))
				{
					_history.Insert(end, new JObject { ["role"] = "tool", ["tool_call_id"] = id, ["name"] = (string)call["function"]?["name"], ["content"] = "Stopped by the user before this ran." });
					end++;
				}
			}
		}
	}

	private static void Route(int generation, string text, out string route, out string focus)
	{
		route = "work";
		focus = null;
		List<AiModel> models = AiModel.RouterModels();
		// The model you picked plans too when it's quick, so the line shows it from the start.
		AiModel chosen = (PluginAiSettings.Model != "auto") ? AiModel.MainChain().FirstOrDefault() : null;
		if (chosen != null && chosen.Quick)
		{
			models = new[] { chosen }.Concat(models.Where(m => m.Key != chosen.Key)).ToList();
		}
		if (models.Count == 0)
		{
			return;
		}
		string plugins = "";
		try
		{
			plugins = string.Join(", ", Directory.GetDirectories(IsaClient.Plugins.PluginLoader.SourcesFolder).Select(Path.GetFileName));
		}
		catch
		{
		}
		string recent;
		lock (_lock)
		{
			recent = string.Join("\n", _transcript.Where(e => e.Kind == EntryKind.User || e.Kind == EntryKind.Assistant).Reverse().Skip(1).Take(4).Reverse().Select(e => (e.Kind == EntryKind.User ? "User: " : "Agent: ") + Flat(e.Text, 300)));
		}
		JArray messages = new JArray
		{
			new JObject { ["role"] = "system", ["content"] = PluginPrompt.Router },
			new JObject { ["role"] = "user", ["content"] = "Plugins: " + ((plugins.Length > 0) ? plugins : "(none)") + "\nPlugin worked on last: " + (LastPlugin ?? "(none)") + "\nGoal of the conversation: " + ((Goal.Length > 0) ? Goal : "(none)") + "\nRecent conversation:\n" + ((recent.Length > 0) ? recent : "(none)") + "\n\nLatest message: " + text }
		};
		foreach (AiModel model in models.Take(2))
		{
			if (_stop || !Alive(generation))
			{
				return;
			}
			try
			{
				ModelLabel = (chosen == null || model == chosen) ? model.Label : (chosen.Label + ", planning with " + model.Label);
				JObject reply = AiCall.Send(model, messages, null, 300, 15000, () => _stop || !Alive(generation));
				JObject json = GroqClient.ParseJson((string)reply["content"]);
				if (json == null)
				{
					continue;
				}
				route = ((string)json["route"] == "quick") ? "quick" : "work";
				string plugin = ((string)json["plugin"] ?? "").Trim();
				if (plugin.Length > 0 && plugins.Split(new[] { ", " }, StringSplitOptions.None).Contains(plugin))
				{
					focus = plugin;
				}
				string title = ((string)json["title"] ?? "").Trim();
				if (title.Length > 0)
				{
					TaskTitle = Flat(title, 60);
					if (_conversation.Messages <= 1)
					{
						_conversation.Title = TaskTitle;
					}
				}
				return;
			}
			catch (AssistantException)
			{
			}
		}
	}

	private static void Compact()
	{
		int lastUser = _history.FindLastIndex(m => (string)m["role"] == "user");
		for (int i = 0; i < lastUser; i++)
		{
			if ((string)_history[i]["role"] == "user" && _history[i]["content"] is JArray parts)
			{
				_history[i]["content"] = PlainText(parts);
			}
		}
		int keep = Math.Max(0, _history.Count - 16);
		for (int i = 0; i < keep; i++)
		{
			JObject m = _history[i];
			string content = (string)m["content"] ?? "";
			if ((string)m["role"] == "tool" && content.Length > 1500)
			{
				m["content"] = content.Substring(0, 400) + "\n... (old output trimmed; call the tool again if you need it)";
			}
		}
		while (_history.Count > 160)
		{
			int cut = _history.FindIndex(1, m => (string)m["role"] == "user");
			if (cut <= 0)
			{
				break;
			}
			_history.RemoveRange(0, cut);
		}
	}

	public static string PlainText(JArray parts)
	{
		int pictures = parts.Count(p => (string)p["type"] == "image_url");
		string text = string.Join("\n", parts.Where(p => (string)p["type"] == "text").Select(p => (string)p["text"]));
		return text + ((pictures > 0) ? ("\n(" + pictures + ((pictures == 1) ? " picture was" : " pictures were") + " attached here)") : "");
	}

	private static string Clean(string content)
	{
		if (string.IsNullOrEmpty(content))
		{
			return content;
		}
		int end = content.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
		if (end >= 0)
		{
			content = content.Substring(end + 8);
		}
		return content.Trim();
	}

	public static string Flat(string text, int max)
	{
		text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Replace("  ", " ").Trim();
		return (text.Length > max) ? (text.Substring(0, max) + "...") : text;
	}

	private static void Print(string answer)
	{
		string clean = AnswerFormat.Clean(answer);
		string[] lines = clean.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
		Color color = AskAssistant.AnswerColor;
		for (int i = 0; i < lines.Length && i < 30; i++)
		{
			string line = lines[i].TrimEnd().Replace("[", "(").Replace("]", ")");
			Main.NewText(IsaClient.Languages.L10n.T((i == 0 ? ClientChat.Tint("Plugin Maker: ", new Color(150, 220, 255)) : "") + line), color.R, color.G, color.B);
		}
		if (lines.Length > 30)
		{
			ClientChat.Info("(the rest is in AI Assistant > Plugin Maker)");
		}
	}

	#endregion

	#region Rewind

	/// <summary>The message being worked on (1 = the first of the conversation), for the checkpoints.</summary>
	private static int _turn;

	/// <summary>Your message that /rewind took back (the panel puts it back in the box to edit).</summary>
	public static string Rewound;

	/// <summary>Before the agent writes, edits or deletes a file: how it was, the first time this message touches it.</summary>
	internal static void Checkpoint(string path)
	{
		lock (_lock)
		{
			List<FileCheckpoint> list = _conversation.Checkpoints ?? (_conversation.Checkpoints = new List<FileCheckpoint>());
			if (list.Any(c => c.Turn == _turn && string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)))
			{
				return;
			}
			string content = null;
			try
			{
				if (File.Exists(path))
				{
					content = File.ReadAllText(path);
				}
			}
			catch
			{
			}
			list.Add(new FileCheckpoint { Turn = _turn, Path = path, Content = content });
		}
	}

	/// <summary>/rewind [n] [chat]: back to before your last n messages, files included unless "chat".</summary>
	private static void Rewind(string args, bool toChat)
	{
		int count = 1;
		bool files = true;
		foreach (string word in args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
		{
			if (int.TryParse(word, out int n) && n > 0)
			{
				count = n;
			}
			else if (word.Equals("chat", StringComparison.OrdinalIgnoreCase))
			{
				files = false;
			}
		}
		if (Busy)
		{
			HardStop();
		}
		List<string> restored = new List<string>();
		HashSet<string> plugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string firstText;
		int removedMessages;
		lock (_lock)
		{
			List<int> userEntries = _transcript.Select((e, i) => (e, i)).Where(p => p.e.Kind == EntryKind.User).Select(p => p.i).ToList();
			if (userEntries.Count == 0)
			{
				AddLocked(new Entry { Kind = EntryKind.Note, Text = "Nothing to rewind." });
				return;
			}
			count = Math.Min(count, userEntries.Count);
			int firstTurn = userEntries.Count - count + 1;
			int cut = userEntries[userEntries.Count - count];
			firstText = _transcript[cut].Text;
			_transcript.RemoveRange(cut, _transcript.Count - cut);
			// The model's side: from the count-th last user message on.
			List<int> userMessages = _history.Select((m, i) => (m, i)).Where(p => (string)p.m["role"] == "user").Select(p => p.i).ToList();
			if (userMessages.Count > 0)
			{
				int from = userMessages[Math.Max(0, userMessages.Count - count)];
				_history.RemoveRange(from, _history.Count - from);
			}
			removedMessages = count;
			// Files: each one back to how it was before the first rewound message touched it.
			List<FileCheckpoint> checkpoints = _conversation.Checkpoints ?? new List<FileCheckpoint>();
			List<FileCheckpoint> undone = checkpoints.Where(c => c.Turn >= firstTurn).ToList();
			if (files)
			{
				foreach (IGrouping<string, FileCheckpoint> group in undone.GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase))
				{
					FileCheckpoint oldest = group.OrderBy(c => c.Turn).First();
					try
					{
						if (oldest.Content == null)
						{
							if (File.Exists(oldest.Path))
							{
								File.Delete(oldest.Path);
							}
						}
						else
						{
							Directory.CreateDirectory(Path.GetDirectoryName(oldest.Path));
							File.WriteAllText(oldest.Path, oldest.Content);
						}
						restored.Add(oldest.Path);
						string sources = IsaClient.Plugins.PluginLoader.SourcesFolder.TrimEnd('\\') + "\\";
						if (oldest.Path.StartsWith(sources, StringComparison.OrdinalIgnoreCase))
						{
							plugins.Add(oldest.Path.Substring(sources.Length).Split('\\')[0]);
						}
					}
					catch (Exception ex)
					{
						ClientLog.LogException(ex);
					}
				}
			}
			checkpoints.RemoveAll(c => c.Turn >= firstTurn);
			_turn = firstTurn - 1;
			Rewound = firstText;
			string what = "Rewound " + removedMessages + ((removedMessages == 1) ? " message" : " messages") + (files ? ((restored.Count > 0) ? (", " + restored.Count + ((restored.Count == 1) ? " file" : " files") + " put back as they were") : ", no files to put back") : " (files left as they are)") + ".";
			AddLocked(new Entry { Kind = EntryKind.Note, Text = what });
		}
		// Plugins whose files went back are built again (a plugin it had just created goes away).
		foreach (string plugin in plugins)
		{
			string folder = Path.Combine(IsaClient.Plugins.PluginLoader.SourcesFolder, plugin);
			Main.QueueMainThreadAction(delegate
			{
				try
				{
					bool gone = !Directory.Exists(folder) || Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories).Length == 0;
					if (gone && Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0)
					{
						Directory.Delete(folder);
					}
					IsaClient.Plugins.PluginLoader.Scan();
					if (!gone)
					{
						IsaClient.Plugins.PluginEntry entry = IsaClient.Plugins.PluginLoader.Entries.FirstOrDefault(e => string.Equals(e.File.TrimEnd('\\'), folder, StringComparison.OrdinalIgnoreCase));
						if (entry != null && IsaClient.Plugins.PluginLoader.IsEnabled(entry))
						{
							IsaClient.Plugins.PluginLoader.Reload(entry);
						}
					}
				}
				catch (Exception ex)
				{
					ClientLog.LogException(ex);
				}
			});
		}
		Save();
		if (toChat)
		{
			Main.QueueMainThreadAction(() => ClientChat.Info("Plugin Maker: rewound. Your message was: " + Flat(firstText, 160)));
		}
	}

	#endregion

	#region Conversations

	public static string ChatsFolder => Path.Combine(ClientSettings.IsaClientFolder, "PluginMaker", "Chats");

	/// <summary>Saves the current conversation (when it has anything in it).</summary>
	public static void Save()
	{
		Conversation c;
		lock (_lock)
		{
			c = _conversation;
			if (_transcript.Count == 0)
			{
				return;
			}
			c.Entries = _transcript.ToList();
			JArray history = new JArray();
			foreach (JObject m in _history)
			{
				JObject copy = (JObject)m.DeepClone();
				if (copy["content"] is JArray parts)
				{
					copy["content"] = PlainText(parts);
				}
				history.Add(copy);
			}
			c.History = history.ToString(Formatting.None);
			c.Updated = DateTime.Now;
		}
		try
		{
			Directory.CreateDirectory(ChatsFolder);
			File.WriteAllText(Path.Combine(ChatsFolder, c.Id + ".json"), JsonConvert.SerializeObject(c, Formatting.Indented));
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	/// <summary>The saved conversations, newest first.</summary>
	public static List<Conversation> Saved()
	{
		List<Conversation> list = new List<Conversation>();
		try
		{
			if (Directory.Exists(ChatsFolder))
			{
				foreach (string file in Directory.GetFiles(ChatsFolder, "*.json"))
				{
					try
					{
						Conversation c = JsonConvert.DeserializeObject<Conversation>(File.ReadAllText(file));
						if (c?.Id != null)
						{
							list.Add(c);
						}
					}
					catch
					{
					}
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		return list.OrderByDescending(c => c.Updated).ToList();
	}

	/// <summary>A new conversation (the current one is saved first).</summary>
	public static void New()
	{
		if (Busy)
		{
			HardStop();
		}
		Save();
		lock (_lock)
		{
			_conversation = NewConversation();
			_transcript.Clear();
			_history.Clear();
			TaskTitle = "";
			LastPlugin = null;
			_turn = 0;
			Version++;
		}
	}

	public static void Resume(Conversation c)
	{
		if (c == null)
		{
			return;
		}
		if (Busy)
		{
			HardStop();
		}
		Save();
		lock (_lock)
		{
			_conversation = c;
			_transcript.Clear();
			_transcript.AddRange(c.Entries ?? new List<Entry>());
			_history.Clear();
			try
			{
				foreach (JToken m in JArray.Parse(c.History ?? "[]"))
				{
					if (m is JObject o)
					{
						_history.Add(o);
					}
				}
			}
			catch
			{
			}
			TaskTitle = c.Title ?? "";
			LastPlugin = c.Entries?.LastOrDefault(e => e.Plugin != null)?.Plugin;
			_turn = _transcript.Count(e => e.Kind == EntryKind.User);
			AddLocked(new Entry { Kind = EntryKind.Note, Text = "Resumed \"" + c.Title + "\"." });
		}
	}

	public static void Delete(Conversation c)
	{
		try
		{
			File.Delete(Path.Combine(ChatsFolder, c.Id + ".json"));
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		if (c.Id == _conversation.Id)
		{
			lock (_lock)
			{
				_conversation = NewConversation();
				_transcript.Clear();
				_history.Clear();
				Version++;
			}
		}
	}

	public static void SetGoal(string goal)
	{
		_conversation.Goal = (goal ?? "").Trim();
		Version++;
		Save();
	}

	#endregion

	#region Commands

	/// <summary>A "/..." line; true when it was a command (unknown ones say so).</summary>
	public static bool Slash(string line, bool toChat)
	{
		string[] parts = line.Trim().Split(new[] { ' ' }, 2);
		string name = parts[0].ToLowerInvariant();
		string args = (parts.Length > 1) ? parts[1].Trim() : "";
		switch (name)
		{
		case "/help":
		case "/?":
			Note("Commands: " + string.Join("   ", Commands.Select(c => c.Name + ((c.Args.Length > 0) ? (" " + c.Args) : "") + ": " + c.Help)), toChat);
			return true;
		case "/new":
		case "/clear":
			New();
			if (toChat)
			{
				ClientChat.Info("Plugin Maker: new conversation.");
			}
			return true;
		case "/stop":
			Stop();
			return true;
		case "/rewind":
			Rewind(args, toChat);
			return true;
		case "/retry":
		{
			Entry last = Transcript.LastOrDefault(e => e.Kind == EntryKind.User);
			if (last == null)
			{
				Note("Nothing to send again.", toChat);
			}
			else
			{
				Send(last.Text, toChat);
			}
			return true;
		}
		case "/goal":
			if (args.Length == 0)
			{
				Note((Goal.Length > 0) ? ("Goal: " + Goal + "   (/goal clear removes it)") : "No goal yet: /goal <what this conversation is about>.", toChat);
			}
			else if (args.Equals("clear", StringComparison.OrdinalIgnoreCase) || args.Equals("none", StringComparison.OrdinalIgnoreCase))
			{
				SetGoal("");
				Note("Goal removed.", toChat);
			}
			else
			{
				SetGoal(args);
				Note("Goal: " + args, toChat);
			}
			return true;
		case "/effort":
			if (PluginAiSettings.Efforts.Contains(args.ToLowerInvariant()))
			{
				PluginAiSettings.Effort = args.ToLowerInvariant();
				PluginAiSettings.Save();
				Note("Effort: " + PluginAiSettings.Effort + ".", toChat);
			}
			else
			{
				Note("Effort is " + PluginAiSettings.Effort + ". /effort low (quick, few checks), medium, or high (thorough: reads more, double-checks, thinks more).", toChat);
			}
			return true;
		case "/model":
			if (args.Length == 0)
			{
				Note("Model: " + ModelName(PluginAiSettings.Model) + ". Choose: auto, " + string.Join(", ", AiModel.Catalog.Where(m => m.Provider.HasKey).Select(m => m.Label)) + ".", toChat);
				return true;
			}
			if (args.Equals("auto", StringComparison.OrdinalIgnoreCase))
			{
				PluginAiSettings.Model = "auto";
			}
			else
			{
				AiModel match = AiModel.Catalog.FirstOrDefault(m => m.Label.Equals(args, StringComparison.OrdinalIgnoreCase) || m.Id.Equals(args, StringComparison.OrdinalIgnoreCase))
					?? AiModel.Catalog.FirstOrDefault(m => (m.Label + " " + m.Id).IndexOf(args, StringComparison.OrdinalIgnoreCase) >= 0);
				if (match == null)
				{
					Note("No model like \"" + args + "\". /model alone lists them.", toChat);
					return true;
				}
				if (!match.Provider.HasKey)
				{
					Note(match.Label + " needs a " + match.Provider.Name + " key (AI Assistant > Connection).", toChat);
					return true;
				}
				PluginAiSettings.Model = match.Key;
			}
			PluginAiSettings.Save();
			ModelChanged();
			Note("Model: " + ModelName(PluginAiSettings.Model) + ".", toChat);
			return true;
		case "/resume":
		{
			List<Conversation> saved = Saved().Where(c => c.Id != _conversation.Id).ToList();
			if (saved.Count == 0)
			{
				Note("No saved conversations yet.", toChat);
				return true;
			}
			Conversation pick = null;
			if (args.Length > 0)
			{
				pick = (int.TryParse(args, out int n) && n >= 1 && n <= saved.Count) ? saved[n - 1] : saved.FirstOrDefault(c => (c.Title ?? "").IndexOf(args, StringComparison.OrdinalIgnoreCase) >= 0);
			}
			if (pick == null)
			{
				Note("Saved conversations: " + string.Join("   ", saved.Take(10).Select((c, i) => (i + 1) + ". " + c.Title + " (" + c.Updated.ToString("MMM d HH:mm") + ")")) + "   -> /resume <number>", toChat);
				return true;
			}
			Resume(pick);
			if (toChat)
			{
				ClientChat.Info("Plugin Maker: resumed \"" + pick.Title + "\".");
			}
			return true;
		}
		case "/plugins":
		{
			string list = string.Join(", ", IsaClient.Plugins.PluginLoader.Entries.Select(e => e.Key + (IsaClient.Plugins.PluginLoader.IsEnabled(e) ? " (on)" : "")));
			Note("Plugins: " + ((list.Length > 0) ? list : "none yet"), toChat);
			return true;
		}
		case "/folder":
			try
			{
				Directory.CreateDirectory(IsaClient.Plugins.PluginLoader.SourcesFolder);
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(IsaClient.Plugins.PluginLoader.SourcesFolder) { UseShellExecute = true });
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
			return true;
		}
		Note("Unknown command " + name + ". /help lists them.", toChat);
		return true;
	}

	/// <summary>
	/// Plugin Maker mode (.pai alone): what you type in the game's chat goes to the Plugin Maker instead of the server,
	/// as in its panel (client commands starting with "." still run; "/..." are the Plugin Maker's commands).
	/// </summary>
	public static bool ChatMode { get; private set; }

	public static void SetChatMode(bool on)
	{
		ChatMode = on;
		if (on)
		{
			ClientChat.Info("Plugin Maker mode: what you write in the chat goes to the Plugin Maker (pictures too). /help for its commands; .pai again to leave.");
		}
		else
		{
			ClientChat.Info("Plugin Maker mode off: the chat goes to the server again.");
		}
	}

	/// <summary>A line typed in the game's chat; true when Plugin Maker mode took it.</summary>
	public static bool TryChatMode(string line, bool withPictures)
	{
		if (!ChatMode || string.IsNullOrWhiteSpace(line) && !(withPictures && IsaClient.ChatImages.ChatAttachments.ReadyCount > 0))
		{
			return false;
		}
		string trimmed = (line ?? "").Trim();
		if (trimmed.StartsWith(".") || trimmed.StartsWith(","))
		{
			// Client commands (and .pai itself) still work.
			return false;
		}
		Send(trimmed, toChat: true, withPictures ? AskAssistant.AttachedPictures() : null);
		return true;
	}

	public static string ModelName(string key)
	{
		return (key == "auto") ? "Auto (MiniMax M3.1 first)" : (AiModel.Get(key)?.Label ?? key);
	}

	/// <summary>.pluginai &lt;message&gt; | /command | new | stop</summary>
	internal static void Command(CommandContext command)
	{
		string rest = command.Rest.Trim();
		switch (rest.ToLowerInvariant())
		{
		case "":
			if (IsaClient.ChatImages.ChatAttachments.ReadyCount > 0 && !ChatMode)
			{
				Send("", toChat: true, AskAssistant.AttachedPictures());
				return;
			}
			SetChatMode(!ChatMode);
			return;
		case "on":
			SetChatMode(true);
			return;
		case "off":
			SetChatMode(false);
			return;
		case "open":
			IsaClient.Hub.ClientHub.OpenSection("ai", 4);
			return;
		case "new":
		case "clear":
		case "forget":
			Slash("/new", toChat: true);
			return;
		case "stop":
			Stop();
			return;
		}
		Send(rest, toChat: true, AskAssistant.AttachedPictures());
	}

	#endregion
}
