using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria.IO;
using IsaClient.Community;
using IsaClient.Core;
using IsaClient.Assistant;
using Terraria;

namespace IsaClient.MultiClient;

/// <summary>How new sessions pick the UUID they send to the server.</summary>
public enum SessionUuidMode
{
	/// <summary>A new random UUID every time a session is created.</summary>
	Random,
	/// <summary>One random UUID per session name, remembered in IsaBots.json (same name = same UUID next time).</summary>
	PerName,
	/// <summary>Your own current UUID (Main.clientUUID).</summary>
	Mine,
	/// <summary>A fixed UUID you typed.</summary>
	Custom
}

/// <summary>A saved session: who (name or character file) and where (server).</summary>
public class SessionProfile
{
	public string Name = "";

	public string PlayerFile = "";

	public string Host = "";

	public int Port = 7777;

	public string Password = "";

	/// <summary>UUID it used (kept so a restored session is the same player to the server).</summary>
	public string Uuid = "";

	public bool AiEnabled;

	public string Describe()
	{
		string who = string.IsNullOrEmpty(PlayerFile) ? Name : (Name + " (" + System.IO.Path.GetFileName(PlayerFile) + ")");
		return who + " @ " + Host + ":" + Port;
	}
}

/// <summary>One of the player's .plr characters, offered as a look for new sessions.</summary>
public class CharacterEntry
{
	public string Path;

	public string Name;

	public string Summary;

	public Player Player;
}

/// <summary>A server offered in the "new session" form.</summary>
public class ServerEntry
{
	public string Label;

	public string Host;

	public int Port;
}

/// <summary>
/// Manages the extra client sessions (MultiClient, hard cap 30). Sessions are extra headless connections the player owns;
/// they join the server the player is on or any other, can look like one of the player's characters, chat and move. The player can
/// talk through one (.bot chat) or become one (.bot control, see <see cref="Possession"/>). Sessions stay online when the player
/// leaves the world. Config in IsaClient/IsaBots.json.
/// </summary>
public static class MultiClientManager
{
	public const int MaxSessions = 300;

	private static readonly List<ClientSession> _sessions = new List<ClientSession>();

	private static readonly object _sync = new object();

	private static Preferences _config;

	private static readonly Color _sessionColor = new Color(120, 220, 160);

	private static readonly string[] _namePool = new string[24]
	{
		"Alex", "Sam", "Robin", "Kai", "Nova", "Luna", "Milo", "Zoe", "Remy", "Toby",
		"Iris", "Finn", "Wren", "Vera", "Otto", "Cleo", "Rex", "Ivy", "Leo", "Mia",
		"Ash", "Beck", "Cy", "Dot"
	};

	/// <summary>Session your typed chat is sent through (.bot chat), or null.</summary>
	public static ClientSession ChatTarget { get; private set; }

	public static List<ClientSession> All
	{
		get
		{
			lock (_sync)
			{
				return _sessions.OrderBy((ClientSession s) => s.Id).ToList();
			}
		}
	}

	public static int Count
	{
		get
		{
			lock (_sync)
			{
				return _sessions.Count;
			}
		}
	}

	public static int OnlineCount
	{
		get
		{
			lock (_sync)
			{
				return _sessions.Count((ClientSession s) => s.State == ClientSessionState.InGame);
			}
		}
	}

	#region Config

	public static SessionUuidMode UuidMode = SessionUuidMode.PerName;

	public static string CustomUuid = "";

	private static Dictionary<string, string> _nameUuids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public static bool RandomNames = true;

	public static bool ShowSessionChat = true;

	/// <summary>Sessions stay online when you leave the world.</summary>
	public static bool KeepOnLeave = true;

	/// <summary>Sessions in your world hit nearby enemies with the weapon on their hotbar (slot 1 first).</summary>
	public static bool SessionsFight = true;

	/// <summary>Reconnect sessions that lost their connection (not kicked ones).</summary>
	public static bool AutoReconnect = true;

	public static List<SessionProfile> Profiles = new List<SessionProfile>();

	public static void Load()
	{
		try
		{
			Directory.CreateDirectory(ClientSettings.IsaClientFolder);
			_config = new Preferences(System.IO.Path.Combine(ClientSettings.IsaClientFolder, "IsaBots.json"));
			if (!_config.Load())
			{
				Save();
				return;
			}
			if (_config.Contains("UuidMode"))
			{
				Enum.TryParse(_config.Get("UuidMode", "PerName"), ignoreCase: true, out UuidMode);
			}
			else if (!_config.Get("RandomUuid", true))
			{
				UuidMode = SessionUuidMode.Mine; // old boolean setting
			}
			CustomUuid = _config.Get("CustomUuid", "") ?? "";
			Dictionary<string, string> saved = _config.Get("NameUuids", new Dictionary<string, string>());
			_nameUuids = new Dictionary<string, string>(saved ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
			_config.Get("RandomNames", ref RandomNames);
			_config.Get("ShowSessionChat", ref ShowSessionChat);
			_config.Get("KeepOnLeave", ref KeepOnLeave);
			_config.Get("AutoReconnect", ref AutoReconnect);
			_config.Get("SessionsFight", ref SessionsFight);
			Possession.RecordOwnWorld = _config.Get("RememberMyWorld", true);
			Profiles = _config.Get("Profiles", new List<SessionProfile>()) ?? new List<SessionProfile>();
			LastSessions = _config.Get("ActiveSessions", new List<SessionProfile>()) ?? new List<SessionProfile>();
			BotRoutes.Load(_config);
			BotSets.Load(_config);
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
				_config = new Preferences(System.IO.Path.Combine(ClientSettings.IsaClientFolder, "IsaBots.json"));
			}
			_config.Clear();
			_config.Put("UuidMode", UuidMode.ToString());
			_config.Put("CustomUuid", CustomUuid);
			_config.Put("NameUuids", _nameUuids);
			_config.Put("RandomNames", RandomNames);
			_config.Put("ShowSessionChat", ShowSessionChat);
			_config.Put("KeepOnLeave", KeepOnLeave);
			_config.Put("AutoReconnect", AutoReconnect);
			_config.Put("SessionsFight", SessionsFight);
			_config.Put("RememberMyWorld", Possession.RecordOwnWorld);
			_config.Put("Profiles", Profiles);
			_config.Put("ActiveSessions", ActiveSnapshotPlusLast());
			BotRoutes.Save(_config);
			BotSets.Save(_config);
			_config.Save();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	/// <summary>Sessions that were online when the game last ran. Shown in Saved so you can call them back; never auto-connected.</summary>
	public static List<SessionProfile> LastSessions = new List<SessionProfile>();

	private static SessionProfile ToProfile(ClientSession b)
	{
		return new SessionProfile
		{
			Name = b.Name,
			PlayerFile = b.TemplatePath ?? "",
			Host = b.Host ?? "",
			Port = b.Port,
			Password = b.Password ?? "",
			Uuid = b.Uuid ?? "",
			AiEnabled = b.Possessed ? AiAssistant.Enabled : b.AiEnabled
		};
	}

	private static bool SameSession(SessionProfile a, SessionProfile b)
	{
		return string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) && a.Port == b.Port && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>What is written as "ActiveSessions": the sessions you have now plus the ones from last time not called back yet.</summary>
	private static List<SessionProfile> ActiveSnapshotPlusLast()
	{
		List<SessionProfile> now = All.Where((ClientSession b) => !b.UserRemoved && !string.IsNullOrEmpty(b.Host) && b.Port > 0).Select(ToProfile).ToList();
		foreach (SessionProfile last in LastSessions)
		{
			if (!now.Any((SessionProfile n) => SameSession(n, last)))
			{
				now.Add(last);
			}
		}
		return now;
	}

	/// <summary>Calls back one session from last time (same name, look, UUID, server and AI switch).</summary>
	public static void ConnectLast(SessionProfile saved)
	{
		LastSessions.Remove(saved);
		if (All.Any((ClientSession s) => s.State != ClientSessionState.Disconnected && SameSession(ToProfile(s), saved)))
		{
			Notice(saved.Name + " is already connected there.", error: true);
			Save();
			return;
		}
		if (Count >= MaxSessions)
		{
			Notice("Bot limit reached (" + MaxSessions + ").", error: true);
			return;
		}
		CharacterEntry character = string.IsNullOrEmpty(saved.PlayerFile) ? null : FindCharacter(saved.PlayerFile);
		ClientSession bot = CreateSession(saved.Name, character, string.IsNullOrEmpty(saved.Uuid) ? null : saved.Uuid);
		bot.AiEnabled = saved.AiEnabled;
		bot.Connect(saved.Host, saved.Port, string.IsNullOrEmpty(saved.Password) ? null : saved.Password);
		Save();
		Notice("Calling back " + saved.Name + " to " + saved.Host + ":" + saved.Port + ".");
	}

	public static void DismissLast(SessionProfile saved)
	{
		LastSessions.Remove(saved);
		Save();
	}

	public static string UuidModeText => UuidMode switch
	{
		SessionUuidMode.Random => "random each time",
		SessionUuidMode.PerName => "one per name (remembered)",
		SessionUuidMode.Mine => "your own UUID",
		SessionUuidMode.Custom => "fixed: " + (string.IsNullOrEmpty(CustomUuid) ? "(not set)" : CustomUuid),
		_ => UuidMode.ToString()
	};

	/// <summary>Cycles Random -> PerName -> Mine -> Custom (Custom only if one was set).</summary>
	public static void CycleUuidMode()
	{
		UuidMode = UuidMode switch
		{
			SessionUuidMode.Random => SessionUuidMode.PerName,
			SessionUuidMode.PerName => SessionUuidMode.Mine,
			SessionUuidMode.Mine => string.IsNullOrEmpty(CustomUuid) ? SessionUuidMode.Random : SessionUuidMode.Custom,
			_ => SessionUuidMode.Random
		};
		Save();
	}

	private static string UuidFor(string name)
	{
		switch (UuidMode)
		{
		case SessionUuidMode.Mine:
			return Main.clientUUID;
		case SessionUuidMode.Custom:
			if (!string.IsNullOrEmpty(CustomUuid))
			{
				return CustomUuid;
			}
			break;
		case SessionUuidMode.PerName:
		{
			if (!_nameUuids.TryGetValue(name, out var uuid))
			{
				uuid = Guid.NewGuid().ToString();
				_nameUuids[name] = uuid;
				Save();
			}
			return uuid;
		}
		}
		return Guid.NewGuid().ToString();
	}

	public static void ForgetNameUuids()
	{
		_nameUuids.Clear();
		Save();
	}

	#endregion

	#region Messages

	private static void Say(string text)
	{
		Main.NewText(IsaClient.Languages.L10n.T("[Bots] " + text), _sessionColor.R, _sessionColor.G, _sessionColor.B);
	}

	private static void SayErr(string text)
	{
		ClientChat.Error("[Bots] " + text);
	}

	/// <summary>Chat line from any thread.</summary>
	public static void Announce(string text)
	{
		Main.QueueMainThreadAction(delegate
		{
			Say(text);
		});
	}

	public static void AnnounceError(string text)
	{
		Main.QueueMainThreadAction(delegate
		{
			SayErr(text);
		});
	}

	/// <summary>Last thing worth showing in the Bots page (errors, results of actions).</summary>
	public static string LastNotice = "";

	private static void Notice(string text, bool error = false)
	{
		LastNotice = (error ? "[c/E48C8C:" : "[c/8CE49B:") + text + "]";
		if (error)
		{
			SayErr(text);
		}
		else
		{
			Say(text);
		}
	}

	#endregion

	#region Servers and characters

	/// <summary>Server whose world the game has loaded right now, or false in singleplayer/menu.</summary>
	public static bool TryGetCurrentServer(out string host, out int port)
	{
		host = null;
		port = 0;
		if (Main.netMode != 1 || !Netplay.Connection.IsActive)
		{
			return false;
		}
		try
		{
			host = Netplay.ServerIPText;
			port = Netplay.ListenPort;
			if (string.IsNullOrEmpty(host) && Netplay.Connection.Socket != null)
			{
				string remote = Netplay.Connection.Socket.GetRemoteAddress()?.ToString();
				if (!string.IsNullOrEmpty(remote))
				{
					int colon = remote.LastIndexOf(':');
					if (colon > 0)
					{
						int.TryParse(remote.Substring(colon + 1), out port);
						host = remote.Substring(0, colon);
					}
					else
					{
						host = remote;
					}
				}
			}
		}
		catch
		{
		}
		return !string.IsNullOrEmpty(host) && port > 0;
	}

	/// <summary>The current server, the servers of your sessions and profiles, then Terraria's recent servers (no duplicates).</summary>
	public static List<ServerEntry> KnownServers()
	{
		List<ServerEntry> result = new List<ServerEntry>();
		void Add(string label, string host, int port)
		{
			if (string.IsNullOrWhiteSpace(host) || port <= 0 || result.Any((ServerEntry s) => s.Port == port && string.Equals(s.Host, host, StringComparison.OrdinalIgnoreCase)))
			{
				return;
			}
			result.Add(new ServerEntry
			{
				Label = label,
				Host = host,
				Port = port
			});
		}
		if (TryGetCurrentServer(out var h, out var p))
		{
			Add("Where you are", h, p);
		}
		if (Possession.MainSession != null)
		{
			Add("Your character's server", Possession.MainSession.Host, Possession.MainSession.Port);
		}
		foreach (ClientSession s in All)
		{
			Add("Server of " + s.Name, s.Host, s.Port);
		}
		foreach (SessionProfile profile in Profiles)
		{
			Add("Saved: " + profile.Name, profile.Host, profile.Port);
		}
		try
		{
			for (int i = 0; i < Main.recentIP.Length; i++)
			{
				if (!string.IsNullOrEmpty(Main.recentIP[i]))
				{
					Add(string.IsNullOrEmpty(Main.recentWorld[i]) ? "Recent" : ("Recent: " + Main.recentWorld[i]), Main.recentIP[i], Main.recentPort[i]);
				}
			}
		}
		catch
		{
		}
		return result;
	}

	private static List<CharacterEntry> _characters;

	public static List<CharacterEntry> Characters
	{
		get
		{
			if (_characters == null)
			{
				RefreshCharacters();
			}
			return _characters;
		}
	}

	/// <summary>Reads your .plr characters (local saves) so they can be used as the look of new sessions.</summary>
	public static void RefreshCharacters()
	{
		List<CharacterEntry> list = new List<CharacterEntry>();
		try
		{
			if (Directory.Exists(Main.PlayerPath))
			{
				foreach (string file in Directory.GetFiles(Main.PlayerPath, "*.plr").OrderBy((string f) => f, StringComparer.OrdinalIgnoreCase))
				{
					try
					{
						PlayerFileData data = Player.LoadPlayer(file, cloudSave: false);
						Player player = data?.Player;
						if (player == null || data.IsCloudSave || player.loadStatus != 0)
						{
							continue;
						}
						string difficulty = player.difficulty switch
						{
							1 => "mediumcore",
							2 => "hardcore",
							3 => "journey",
							_ => "classic"
						};
						list.Add(new CharacterEntry
						{
							Path = file,
							Name = player.name,
							Summary = difficulty + ", " + player.statLifeMax + " hp, " + player.statManaMax + " mana",
							Player = player
						});
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
		_characters = list;
	}

	private static CharacterEntry FindCharacter(string pathOrName)
	{
		if (string.IsNullOrEmpty(pathOrName))
		{
			return null;
		}
		return Characters.FirstOrDefault((CharacterEntry c) => string.Equals(c.Path, pathOrName, StringComparison.OrdinalIgnoreCase)) ?? Characters.FirstOrDefault((CharacterEntry c) => string.Equals(c.Name, pathOrName, StringComparison.OrdinalIgnoreCase)) ?? Characters.FirstOrDefault((CharacterEntry c) => c.Name.StartsWith(pathOrName, StringComparison.OrdinalIgnoreCase));
	}

	#endregion

	#region Adding / removing

	private static int NextFreeId()
	{
		lock (_sync)
		{
			int id = 1;
			while (_sessions.Any((ClientSession s) => s.Id == id))
			{
				id++;
			}
			return id;
		}
	}

	private static string NextName()
	{
		if (RandomNames)
		{
			return _namePool[Main.rand.Next(_namePool.Length)] + Main.rand.Next(10, 100);
		}
		return "Bot" + NextFreeId();
	}

	/// <summary>Adds sessions. <paramref name="character"/> gives them that character's look and items; <paramref name="name"/> overrides the name.</summary>
	public static void AddSessions(int count, string host, int port, string name = null, CharacterEntry character = null, string password = null)
	{
		if (string.IsNullOrWhiteSpace(host) || port <= 0)
		{
			Notice("Choose a server first.", error: true);
			return;
		}
		count = Utils.Clamp(count, 1, MaxSessions);
		int room = MaxSessions - Count;
		if (room <= 0)
		{
			Notice("Bot limit reached (" + MaxSessions + ").", error: true);
			return;
		}
		if (count > room)
		{
			count = room;
		}
		if (password == null && TryGetCurrentServer(out var ch, out var cp) && cp == port && Possession.SameHost(ch, host))
		{
			password = Netplay.ServerPassword;
		}
		string baseName = string.IsNullOrWhiteSpace(name) ? character?.Name : name.Trim();
		for (int i = 0; i < count; i++)
		{
			string botName = (baseName != null) ? ((count > 1) ? (baseName + (i + 1)) : baseName) : NextName();
			if (botName.Length > 20)
			{
				botName = botName.Substring(0, 20);
			}
			ClientSession bot = CreateSession(botName, character, null);
			bot.Connect(host, port, password);
		}
		Save();
		Notice("Connecting " + count + " session(s) to " + host + ":" + port + ". Total " + Count + "/" + MaxSessions + ".");
	}

	/// <summary>A session for a set member: its name, the look of <paramref name="playerFile"/> (or generated), its UUID.</summary>
	internal static ClientSession CreateFor(string name, string playerFile, string uuid)
	{
		CharacterEntry character = string.IsNullOrEmpty(playerFile) ? null : FindCharacter(playerFile);
		return CreateSession(name, character, uuid);
	}

	/// <summary>Creates, wires and lists a session (not connected yet). <paramref name="uuid"/> null = pick one by the UUID mode.</summary>
	private static ClientSession CreateSession(string name, CharacterEntry character, string uuid)
	{
		ClientSession bot = new ClientSession(NextFreeId(), name, uuid ?? UuidFor(name));
		if (character != null)
		{
			try
			{
				bot.Template = character.Player.SerializedClone();
				bot.TemplatePath = character.Path;
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		}
		Wire(bot);
		lock (_sync)
		{
			_sessions.Add(bot);
		}
		return bot;
	}

	private static void Wire(ClientSession bot)
	{
		bot.OnChat += SessionChatReceived;
		bot.OnStateChanged += SessionStateChanged;
	}

	public static void SessionStateChanged(ClientSession b, string status)
	{
		if (b.State != ClientSessionState.InGame && b.State != ClientSessionState.Disconnected)
		{
			return;
		}
		string who = b.IsMainSession ? ("Your character " + b.Name) : ("#" + b.Id + " " + b.Name);
		string line = who + ": " + ((b.State == ClientSessionState.InGame) ? ("joined " + b.Host + ":" + b.Port) : status);
		Main.QueueMainThreadAction(delegate
		{
			if (!Main.gameMenu)
			{
				Main.NewText(IsaClient.Languages.L10n.T("[Bots] " + line), _sessionColor.R, _sessionColor.G, _sessionColor.B);
			}
		});
	}

	/// <summary>Chat a headless session receives. Only shown for your .bot chat target when it is on another server
	/// (on your own server you already see the same chat).</summary>
	private sealed class PendingRelay
	{
		public string Line;

		public string Text;

		public Color Color;

		public DateTime Due;
	}

	private static readonly List<PendingRelay> _pendingRelays = new List<PendingRelay>();

	private static readonly List<KeyValuePair<string, DateTime>> _recentGameChat = new List<KeyValuePair<string, DateTime>>();

	/// <summary>Every line the game itself shows in your chat (hooked in ChatHelper.DisplayMessage).</summary>
	public static void NoteGameChat(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		lock (_recentGameChat)
		{
			_recentGameChat.Add(new KeyValuePair<string, DateTime>(text, DateTime.UtcNow));
			while (_recentGameChat.Count > 0 && (_recentGameChat.Count > 200 || (DateTime.UtcNow - _recentGameChat[0].Value).TotalSeconds > 6.0))
			{
				_recentGameChat.RemoveAt(0);
			}
		}
	}

	private static bool GameShowedRecently(string text)
	{
		lock (_recentGameChat)
		{
			return _recentGameChat.Any((KeyValuePair<string, DateTime> l) => l.Key == text);
		}
	}

	/// <summary>Shows the lines your .bot chat session got that you did not see yourself (command answers, whispers, other servers).</summary>
	private static void FlushRelays()
	{
		List<PendingRelay> due;
		lock (_pendingRelays)
		{
			if (_pendingRelays.Count == 0)
			{
				return;
			}
			DateTime now = DateTime.UtcNow;
			due = _pendingRelays.Where((PendingRelay r) => r.Due <= now).ToList();
			_pendingRelays.RemoveAll((PendingRelay r) => r.Due <= now);
		}
		foreach (PendingRelay r in due)
		{
			if (!Main.gameMenu && !GameShowedRecently(r.Text))
			{
				Main.NewText(IsaClient.Languages.L10n.T(r.Line), r.Color.R, r.Color.G, r.Color.B);
			}
		}
	}

	public static void SessionChatReceived(ClientSession bot, string text, string sender, Color color)
	{
		if (!ShowSessionChat || string.IsNullOrEmpty(text) || ChatTarget != bot || bot.Possessed)
		{
			return;
		}
		{
			{
				// Only the "[name sees]" prefix is ours (colored); the line itself goes out exactly as the server sent it, in its
				// own color, so item icons, colors and other chat tags keep working.
				string prefix = "[c/AAC8AA:[" + bot.Name.Replace("]", "\\]") + " sees\\]] ";
				string who = (sender != null) ? (Terraria.GameContent.UI.Chat.NameTagHandler.GenerateTag(sender) + " ") : "";
				// Wait a moment: if you are on the same server the game usually shows the same line itself, and then it is skipped.
				lock (_pendingRelays)
				{
					_pendingRelays.Add(new PendingRelay
					{
						Line = prefix + who + text,
						Text = text,
						Color = color,
						Due = DateTime.UtcNow.AddMilliseconds(1200.0)
					});
				}
			}
		}
	}

	public static ClientSession Find(string idOrName)
	{
		lock (_sync)
		{
			if (int.TryParse(idOrName, out var id))
			{
				return _sessions.FirstOrDefault((ClientSession b) => b.Id == id);
			}
			return _sessions.FirstOrDefault((ClientSession b) => string.Equals(b.Name, idOrName, StringComparison.OrdinalIgnoreCase)) ?? _sessions.FirstOrDefault((ClientSession b) => b.Name.StartsWith(idOrName, StringComparison.OrdinalIgnoreCase));
		}
	}

	/// <summary>Drops a session from the list without touching its connection (used once it is already gone).</summary>
	public static void Forget(ClientSession session)
	{
		lock (_sync)
		{
			_sessions.Remove(session);
		}
		if (ChatTarget == session)
		{
			ChatTarget = null;
		}
		Save();
	}

	public static void RemoveAll()
	{
		List<ClientSession> copy;
		lock (_sync)
		{
			copy = _sessions.Where((ClientSession b) => !b.Possessed).ToList();
			_sessions.RemoveAll((ClientSession b) => !b.Possessed);
		}
		foreach (ClientSession bot in copy)
		{
			bot.UserRemoved = true;
			bot.Disconnect("removed");
		}
		if (ChatTarget != null && !ChatTarget.Possessed)
		{
			ChatTarget = null;
		}
		Save();
		Notice("Removed " + copy.Count + " session(s)." + (Possession.Active ? (" " + Possession.Current.Name + " stays: you are playing as it.") : ""));
	}

	public static bool Remove(ClientSession bot)
	{
		if (bot.Possessed)
		{
			Notice("You are playing as " + bot.Name + ". Go back first.", error: true);
			return false;
		}
		lock (_sync)
		{
			_sessions.Remove(bot);
		}
		if (ChatTarget == bot)
		{
			ChatTarget = null;
		}
		bot.UserRemoved = true;
		bot.Disconnect("removed");
		Save();
		Notice("Removed #" + bot.Id + " " + bot.Name + ".");
		return true;
	}

	public static void Reconnect(ClientSession bot)
	{
		if (bot.Possessed || bot.State == ClientSessionState.Connecting || bot.State == ClientSessionState.Handshaking)
		{
			return;
		}
		bot.ReconnectTries = 0;
		if (bot.State == ClientSessionState.Disconnected && !bot.WasKicked)
		{
			// It dropped by itself (not kicked): back through another exit.
			bot.AvoidCurrentRoute();
		}
		bot.Reconnect();
		Notice("Reconnecting " + bot.Name + "...");
	}

	#endregion

	#region Profiles

	public static void SaveProfile(ClientSession bot)
	{
		SessionProfile profile = ToProfile(bot);
		Profiles.RemoveAll((SessionProfile p) => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase) && p.Port == profile.Port && string.Equals(p.Host, profile.Host, StringComparison.OrdinalIgnoreCase));
		Profiles.Add(profile);
		Save();
		Notice("Saved " + profile.Describe() + ".");
	}

	public static void ConnectProfile(SessionProfile profile)
	{
		if (All.Any((ClientSession s) => s.State != ClientSessionState.Disconnected && string.Equals(s.Name, profile.Name, StringComparison.OrdinalIgnoreCase) && s.Port == profile.Port && string.Equals(s.Host, profile.Host, StringComparison.OrdinalIgnoreCase)))
		{
			Notice(profile.Name + " is already connected there.", error: true);
			return;
		}
		if (Count >= MaxSessions)
		{
			Notice("Bot limit reached (" + MaxSessions + ").", error: true);
			return;
		}
		// Same name, look, UUID and AI switch as when it was saved.
		CharacterEntry character = string.IsNullOrEmpty(profile.PlayerFile) ? null : FindCharacter(profile.PlayerFile);
		ClientSession bot = CreateSession(profile.Name, character, string.IsNullOrEmpty(profile.Uuid) ? null : profile.Uuid);
		bot.AiEnabled = profile.AiEnabled;
		bot.Connect(profile.Host, profile.Port, string.IsNullOrEmpty(profile.Password) ? null : profile.Password);
		Save();
		Notice("Connecting " + profile.Name + " to " + profile.Host + ":" + profile.Port + ".");
	}

	public static void DeleteProfile(SessionProfile profile)
	{
		Profiles.Remove(profile);
		Save();
	}

	#endregion

	#region Chat and control

	public static void SetChatTarget(ClientSession bot)
	{
		ChatTarget = bot;
		if (bot == null)
		{
			Notice("Your chat goes out as yourself again.");
		}
		else
		{
			Notice("Your chat is now sent as " + bot.Name + ". Commands starting with " + ChatCommands.CommandSpecifier + " still run here.");
		}
	}

	/// <summary>Plays as <paramref name="bot"/> (null = back to yourself).</summary>
	public static void Control(ClientSession bot)
	{
		if (!Possession.Begin(bot, out var error))
		{
			Notice(error, error: true);
			return;
		}
		if (bot != null && ChatTarget == bot)
		{
			ChatTarget = null; // you are it now; plain chat already goes out as it
		}
	}

	/// <summary>Called from the chat pipeline: with a .bot chat target, typed text (and /commands) go out as that session.</summary>
	public static bool InterceptChat(string chatText)
	{
		ClientSession target = ChatTarget;
		if (target == null || string.IsNullOrEmpty(chatText))
		{
			return false;
		}
		if (chatText[0] == ChatCommands.CommandSpecifier || chatText[0] == '.')
		{
			return false; // local IsaClient commands run normally
		}
		if (target.Possessed || target.State != ClientSessionState.InGame)
		{
			SayErr(target.Name + " can't chat right now (" + target.StatusText + "). .bot chat off to talk as yourself.");
			return true;
		}
		IsaClient.Community.ChatTranslate.Send(chatText, text => target.Say(IsaClient.Community.ChatColor.ApplyToOutgoing(text)));
		return true;
	}

	#endregion

	#region AI

	/// <summary>Names of all your characters (you and every session): their chat is never answered by a session's AI.</summary>
	public static List<string> OwnNames()
	{
		List<string> names = All.Select((ClientSession s) => s.Name).ToList();
		try
		{
			names.Add(Possession.OriginalName);
			if (Main.LocalPlayer != null)
			{
				names.Add(Main.LocalPlayer.name);
			}
		}
		catch
		{
		}
		return names;
	}

	/// <summary>Short situation line for a session's AI prompt.</summary>
	public static string GameInfoFor(ClientSession s)
	{
		string where = s.Host + ":" + s.Port;
		try
		{
			if (Possession.IsOnMyServer(s) && !Main.gameMenu)
			{
				where = "\"" + Main.worldName + "\" (" + (Main.hardMode ? "hardmode" : "pre-hardmode") + ")";
			}
		}
		catch
		{
		}
		return "you are on " + where + ", " + s.StatLife + "/" + s.StatLifeMax + " HP.";
	}

	public static void SetAi(ClientSession s, bool on)
	{
		s.AiEnabled = on;
		Save();
		Notice(s.Name + ": AI " + (on ? "on - it answers when someone says its name" : "off") + ".");
	}

	#endregion

	#region Per-tick

	/// <summary>Main thread, every tick (also in the menu): movement of sessions, auto-reconnect.</summary>
	public static void UpdateMainThread()
	{
		FlushRelays();
		List<ClientSession> sessions;
		lock (_sync)
		{
			sessions = _sessions.ToList();
		}
		try
		{
			BotSets.Update(sessions); // sets joining one by one, and every bot's steps
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		if (sessions.Count == 0 && Possession.MainSession == null)
		{
			return;
		}
		bool inWorld = Main.netMode == 1 && !Main.gameMenu && !Possession.InTransition;
		TryGetCurrentServer(out var host, out var port);
		DateTime now = DateTime.UtcNow;
		foreach (ClientSession s in sessions)
		{
			try
			{
				if (s.State == ClientSessionState.InGame)
				{
					bool worldHere = inWorld && host != null && s.Port == port && Possession.SameHost(host, s.Host);
					s.UpdateMovement(worldHere);
				}
				else if (s.State == ClientSessionState.Disconnected && AutoReconnect && !s.WasKicked && !s.UserRemoved && s.ReconnectTries < 5)
				{
					if (s.ReconnectAt == DateTime.MaxValue)
					{
						s.ReconnectAt = now.AddSeconds(5 * (s.ReconnectTries + 1));
					}
					else if (now >= s.ReconnectAt)
					{
						// It dropped without a kick: often a bad proxy, so it comes back through another exit.
						s.AvoidCurrentRoute();
						s.Reconnect();
					}
				}
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		}
		Possession.MainSession?.UpdateMovement(inWorld && Possession.IsOnMyServer(Possession.MainSession));
	}

	public static void OnLeftWorld()
	{
		Possession.OnGameDisconnected();
		ChatTarget = null;
		foreach (ClientSession s in All)
		{
			if (s.IsFollowing)
			{
				s.StopMoving(); // the player indexes of that world mean nothing now
			}
		}
		if (!KeepOnLeave && Count > 0)
		{
			RemoveAll();
		}
	}

	#endregion

	#region Command

	public static bool HandleCommand(string text, string chatText, string[] array)
	{
		if (text != "bot" && text != "bots")
		{
			return false;
		}
		if (array.Length < 2)
		{
			PrintHelp();
			return true;
		}
		string sub = array[1].ToLowerInvariant();
		if (chatText.IndexOf('"') >= 0 && (sub == "add" || sub == "here" || sub == "as" || sub == "connect"))
		{
			// "quoted names" may contain spaces: .bot add "my bot"   .bot as "My Character" 2
			array = ChatCommands.SplitArguments(chatText.Substring(1)).ToArray();
		}
		string arg = (array.Length > 2) ? array[2].ToLowerInvariant() : "";
		switch (sub)
		{
		case "ui":
		case "panel":
		case "menu":
			if (!IsaClient.Menu.BotsMenu.OpenFromCommand())
			{
				SayErr("The Bots window can't open on this screen.");
			}
			return true;
		case "add":
		case "here":
		{
			if (!TryGetCurrentServer(out var host, out var port))
			{
				SayErr("Join a server first, or use .bot connect <ip[:port]> [count] [name].");
				return true;
			}
			int count = 1;
			string name = null;
			if (array.Length > 2 && !int.TryParse(array[2], out count))
			{
				count = 1;
				name = array[2];
			}
			if (array.Length > 3)
			{
				name = array[3];
			}
			AddSessions(count, host, port, name);
			return true;
		}
		case "as":
		{
			// .bot as <character> [count] [ip[:port]]
			if (array.Length < 3)
			{
				SayErr("Usage: .bot as <character name> [count] [ip[:port]]");
				return true;
			}
			RefreshCharacters();
			CharacterEntry character = FindCharacter(array[2]);
			if (character == null)
			{
				SayErr("No character '" + array[2] + "'. Yours: " + string.Join(", ", Characters.Select((CharacterEntry c) => c.Name)));
				return true;
			}
			int count = 1;
			if (array.Length > 3)
			{
				int.TryParse(array[3], out count);
			}
			string host;
			int port;
			if (array.Length > 4)
			{
				ParseHostPort(array[4], out host, out port);
			}
			else if (!TryGetCurrentServer(out host, out port))
			{
				SayErr("Join a server first or give an ip[:port].");
				return true;
			}
			AddSessions(Math.Max(1, count), host, port, null, character);
			return true;
		}
		case "connect":
		{
			if (array.Length < 3)
			{
				SayErr("Usage: .bot connect <ip[:port]> [count] [name]");
				return true;
			}
			ParseHostPort(array[2], out var host, out var port);
			int count = 1;
			if (array.Length > 3)
			{
				int.TryParse(array[3], out count);
			}
			AddSessions(count, host, port, (array.Length > 4) ? array[4] : null);
			return true;
		}
		case "list":
			PrintList();
			return true;
		case "remove":
		case "kick":
			if (arg == "all")
			{
				RemoveAll();
			}
			else if (array.Length > 2)
			{
				ClientSession bot = Find(array[2]);
				if (bot == null)
				{
					SayErr("No bot '" + array[2] + "'.");
				}
				else
				{
					Remove(bot);
				}
			}
			else
			{
				SayErr("Usage: .bot remove <id|name|all>");
			}
			return true;
		case "reconnect":
			if (arg == "all")
			{
				foreach (ClientSession b in All.Where((ClientSession b) => b.State == ClientSessionState.Disconnected))
				{
					Reconnect(b);
				}
			}
			else
			{
				ClientSession bot = (array.Length > 2) ? Find(array[2]) : null;
				if (bot == null)
				{
					SayErr("Usage: .bot reconnect <id|name|all>");
				}
				else
				{
					Reconnect(bot);
				}
			}
			return true;
		case "save":
		{
			ClientSession bot = (array.Length > 2) ? Find(array[2]) : null;
			if (bot == null)
			{
				SayErr("Usage: .bot save <id|name>  (remember it in Saved)");
			}
			else
			{
				SaveProfile(bot);
			}
			return true;
		}
		case "control":
		case "play":
		case "switch":
		case "be":
			if (arg == "off" || arg == "none" || arg == "me" || arg == "return")
			{
				Control(null);
			}
			else if (array.Length > 2)
			{
				ClientSession bot = Find(array[2]);
				if (bot == null)
				{
					SayErr("No bot '" + array[2] + "'.");
				}
				else
				{
					Control(bot);
				}
			}
			else
			{
				SayErr("Usage: .bot control <id|name>  (become that session)  |  .bot return  (back to yourself)");
			}
			return true;
		case "return":
		case "back":
		case "me":
			Control(null);
			return true;
		case "chat":
		case "talk":
			if (arg == "off" || arg == "none" || arg == "me")
			{
				SetChatTarget(null);
			}
			else if (array.Length > 2)
			{
				ClientSession bot = Find(array[2]);
				if (bot == null)
				{
					SayErr("No bot '" + array[2] + "'.");
				}
				else
				{
					SetChatTarget(bot);
				}
			}
			else
			{
				SayErr("Usage: .bot chat <id|name|off>  (your chat is sent as that session)");
			}
			return true;
		case "say":
		{
			if (array.Length < 4)
			{
				SayErr("Usage: .bot say <id|name|all> <text>");
				return true;
			}
			string message = string.Join(" ", array.Skip(3));
			if (arg == "all")
			{
				foreach (ClientSession b in All)
				{
					b.Say(message);
				}
			}
			else
			{
				ClientSession bot = Find(array[2]);
				if (bot == null)
				{
					SayErr("No bot '" + array[2] + "'.");
				}
				else
				{
					bot.Say(message);
				}
			}
			return true;
		}
		case "goto":
		{
			ClientSession bot = (array.Length > 2) ? Find(array[2]) : null;
			if (bot == null || array.Length < 5)
			{
				SayErr("Usage: .bot goto <id|name> <tileX> <tileY>");
				return true;
			}
			if (float.TryParse(array[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var tx) && float.TryParse(array[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var ty))
			{
				bot.GoTo(new Vector2(tx * 16f, ty * 16f));
				Say(bot.Name + " walking to (" + (int)tx + ", " + (int)ty + ").");
			}
			return true;
		}
		case "follow":
		{
			ClientSession bot = (array.Length > 2) ? Find(array[2]) : null;
			if (bot == null)
			{
				SayErr("Usage: .bot follow <id|name> [me|player|off]");
				return true;
			}
			string who = (array.Length > 3) ? array[3] : "me";
			if (who.Equals("off", StringComparison.OrdinalIgnoreCase) || who.Equals("stop", StringComparison.OrdinalIgnoreCase))
			{
				bot.StopMoving();
				Say(bot.Name + " stopped.");
				return true;
			}
			FollowPlayer(bot, string.Equals(who, "me", StringComparison.OrdinalIgnoreCase) ? Main.myPlayer : (ChatTools.FindPlayer(who, out var _)?.whoAmI ?? -1));
			return true;
		}
		case "ai":
		{
			// .bot ai <id|name|all> [on|off]
			if (array.Length < 3)
			{
				SayErr("Usage: .bot ai <id|name|all> [on|off]");
				return true;
			}
			string mode = (array.Length > 3) ? array[3].ToLowerInvariant() : "";
			List<ClientSession> targets = (arg == "all") ? All : new List<ClientSession> { Find(array[2]) };
			foreach (ClientSession b in targets.Where((ClientSession b) => b != null))
			{
				bool on = (mode == "on") || (mode != "off" && !b.AiEnabled);
				if (b.Possessed)
				{
					AiAssistant.SetEnabled(on);
				}
				SetAi(b, on);
			}
			if (targets.All((ClientSession b) => b == null))
			{
				SayErr("No bot '" + array[2] + "'.");
			}
			return true;
		}
		case "fight":
			SessionsFight = (arg == "on") || (arg != "off" && !SessionsFight);
			Save();
			Say("Sessions " + (SessionsFight ? "fight nearby enemies with the weapon on their hotbar (slot 1 first)." : "no longer fight."));
			return true;
		case "uuid":
			HandleUuidCommand(array);
			return true;
		case "proxy":
		case "proxies":
			HandleProxyCommand(array);
			return true;
		case "set":
		case "sets":
			HandleSetCommand(chatText);
			return true;
		case "run":
		case "do":
			HandleRunCommand(chatText);
			return true;
		case "names":
			RandomNames = !RandomNames;
			Save();
			Say("Random bot names " + (RandomNames ? "on" : "off") + ".");
			return true;
		default:
			PrintHelp();
			return true;
		}
	}

	public static void FollowPlayer(ClientSession bot, int target)
	{
		if (target < 0 || target >= 255 || !Main.player[target].active)
		{
			Notice("No such player.", error: true);
			return;
		}
		if (!Possession.IsOnMyServer(bot))
		{
			Notice(bot.Name + " is on another server; it can only follow players in the world you are in.", error: true);
			return;
		}
		bot.Follow(target);
		Notice(bot.Name + " is following " + Main.player[target].name + ".");
	}

	/// <summary>The bots a word means: all, a set's name, an id or a bot's name.</summary>
	private static List<ClientSession> Targets(string who, out string label)
	{
		label = who;
		if (who.Equals("all", StringComparison.OrdinalIgnoreCase) || who == "*")
		{
			label = "every bot";
			return All.Where(s => !s.UserRemoved).ToList();
		}
		BotSet set = BotSets.Sets.FirstOrDefault(x => string.Equals(x.Name, who, StringComparison.OrdinalIgnoreCase)) ?? BotSets.Sets.FirstOrDefault(x => x.Name.StartsWith(who, StringComparison.OrdinalIgnoreCase));
		if (set != null)
		{
			label = set.Name;
			return BotSets.Members(set);
		}
		ClientSession one = Find(who);
		return (one != null) ? new List<ClientSession> { one } : new List<ClientSession>();
	}

	/// <summary>.bot run &lt;all|set|id|name&gt; &lt;chat, /command or .step&gt;</summary>
	private static void HandleRunCommand(string chatText)
	{
		string[] parts = chatText.TrimStart('.', ChatCommands.CommandSpecifier).Split(new[] { ' ' }, 4, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 4)
		{
			SayErr("Usage: .bot run <all|set|id|name> <what>   e.g. .bot run all /login {pass}   .bot run Fairies .pixie me");
			return;
		}
		List<ClientSession> bots = Targets(parts[2], out string label);
		if (bots.Count == 0)
		{
			SayErr("No bot or set called " + parts[2] + ".");
			return;
		}
		int done = BotSets.RunNow(bots, parts[3]);
		Say(label + ": " + done + ((done == 1) ? " bot did it." : " bots did it.") + ((done < bots.Count) ? (" " + (bots.Count - done) + " not online.") : ""));
	}

	/// <summary>.bot set [list | start|stop|reconnect &lt;name&gt;]</summary>
	private static void HandleSetCommand(string chatText)
	{
		string[] parts = chatText.TrimStart('.', ChatCommands.CommandSpecifier).Split(new[] { ' ' }, 4, StringSplitOptions.RemoveEmptyEntries);
		string verb = (parts.Length > 2) ? parts[2].ToLowerInvariant() : "list";
		if (verb == "list" || parts.Length < 4)
		{
			if (BotSets.Sets.Count == 0)
			{
				Say("No sets yet: make one in Bots > Sets.");
				return;
			}
			Say("Sets (.bot set start|stop|reconnect <name>):");
			foreach (BotSet x in BotSets.Sets)
			{
				ClientChat.Notice("  " + x.Name + ": " + x.Members.Count + " members, " + BotSets.Online(x) + " online, " + (string.IsNullOrEmpty(x.Host) ? "no server" : (x.Host + ":" + x.Port)));
			}
			return;
		}
		BotSet set = BotSets.Sets.FirstOrDefault(x => string.Equals(x.Name, parts[3], StringComparison.OrdinalIgnoreCase)) ?? BotSets.Sets.FirstOrDefault(x => x.Name.StartsWith(parts[3], StringComparison.OrdinalIgnoreCase));
		if (set == null)
		{
			SayErr("No set called " + parts[3] + ".");
			return;
		}
		switch (verb)
		{
		case "start":
		case "on":
			BotSets.Start(set);
			break;
		case "stop":
		case "off":
			BotSets.Stop(set);
			break;
		case "reconnect":
			BotSets.ReconnectAll(set);
			Say(set.Name + ": reconnecting.");
			break;
		default:
			SayErr("Usage: .bot set [list | start|stop|reconnect <name>]");
			break;
		}
	}

	/// <summary>.bot proxy [on|off|&lt;per ip&gt;|auto|&lt;country code&gt;]: bots through proxies when your IP is full.</summary>
	private static void HandleProxyCommand(string[] array)
	{
		string arg = (array.Length > 2) ? array[2].ToLowerInvariant() : "";
		if (arg == "on" || arg == "off")
		{
			BotRoutes.TurnOn(arg == "on");
		}
		else if (int.TryParse(arg, out int perIp))
		{
			BotRoutes.PerIp = Math.Max(BotRoutes.MinPerIp, Math.Min(BotRoutes.MaxPerIp, perIp));
			Save();
		}
		else if (arg == "auto" || arg == "fastest")
		{
			BotRoutes.UseFastest();
		}
		else if (arg.Length == 2)
		{
			BotRoutes.UseCountry(arg);
		}
		else if (arg.Length > 0)
		{
			SayErr("Usage: .bot proxy [on|off|<connections per ip>|auto|<country code, like de>]");
			return;
		}
		Say("Bots through proxies: " + BotRoutes.Summary + ". Proxies: " + ((BotRoutes.Mode == BotRouteMode.Country) ? IsaClient.Networking.CountryNames.Name(BotRoutes.Country) : ((BotRoutes.Mode == BotRouteMode.Chosen) ? (BotRoutes.Chosen.Count + " picked") : "the fastest")) + ". The panel has it all: Bots > Connection.");
	}

	private static void HandleUuidCommand(string[] array)
	{
		if (array.Length < 3)
		{
			Say("New bots use: " + UuidModeText + ".  Options: .bot uuid random | pername | mine | <uuid> | forget");
			return;
		}
		string value = array[2];
		switch (value.ToLowerInvariant())
		{
		case "random":
			UuidMode = SessionUuidMode.Random;
			break;
		case "pername":
		case "name":
		case "stable":
			UuidMode = SessionUuidMode.PerName;
			break;
		case "mine":
		case "own":
			UuidMode = SessionUuidMode.Mine;
			break;
		case "forget":
			ForgetNameUuids();
			Say("Forgot the remembered per-name UUIDs.");
			return;
		default:
			if (!Guid.TryParse(value, out var guid))
			{
				SayErr("Not a UUID. Use random, pername, mine, forget or a UUID like " + Guid.NewGuid() + ".");
				return;
			}
			CustomUuid = guid.ToString();
			UuidMode = SessionUuidMode.Custom;
			break;
		}
		Save();
		Say("New bots use: " + UuidModeText + ". It applies to bots added from now on.");
	}

	public static void ParseHostPort(string text, out string host, out int port)
	{
		text = (text ?? "").Trim();
		host = text;
		port = 7777;
		int colon = text.LastIndexOf(':');
		if (colon > 0)
		{
			host = text.Substring(0, colon);
			int.TryParse(text.Substring(colon + 1), out port);
			if (port <= 0)
			{
				port = 7777;
			}
		}
	}

	public static string Describe(ClientSession bot)
	{
		if (bot.Possessed)
		{
			return "you are playing as it";
		}
		switch (bot.State)
		{
		case ClientSessionState.InGame:
		{
			string extra = "";
			if (ChatTarget == bot)
			{
				extra += ", your chat goes through it";
			}
			if (bot.IsFollowing && bot.FollowingName != null)
			{
				extra += ", following " + bot.FollowingName;
			}
			int fight = bot.FightTarget;
			if (fight >= 0 && fight < Main.maxNPCs && Main.npc[fight].active)
			{
				extra += ", fighting " + Main.npc[fight].GivenOrTypeName;
			}
			return "online" + extra;
		}
		case ClientSessionState.Disconnected:
			if (!bot.WasKicked && !bot.UserRemoved && AutoReconnect && bot.ReconnectTries < 5 && bot.ReconnectAt != DateTime.MaxValue)
			{
				int secs = Math.Max(0, (int)(bot.ReconnectAt - DateTime.UtcNow).TotalSeconds);
				return bot.StatusText + " - retrying in " + secs + "s";
			}
			return bot.StatusText;
		default:
			return bot.StatusText;
		}
	}

	private static void PrintList()
	{
		List<ClientSession> copy = All;
		if (Possession.Active)
		{
			ClientChat.Notice("  You: " + Possession.OriginalName + " - kept online while you play as " + Possession.Current.Name + " (.bot return)");
		}
		if (copy.Count == 0)
		{
			Say("No bots. .bot add [count] [name] joins the server you are on.");
			return;
		}
		Say(copy.Count + "/" + MaxSessions + " bots:");
		foreach (ClientSession bot in copy)
		{
			ClientChat.Notice("  #" + bot.Id + " " + bot.Name + " @ " + bot.Host + ":" + bot.Port + " - " + Describe(bot));
		}
	}

	private static void PrintHelp()
	{
		Say("Bots (max " + MaxSessions + "). Easiest: .bot ui opens the Bots section of the IsaClient panel.");
		ClientChat.Notice("  .bot add [count] [\"name\"] | .bot as <character> [count] [ip[:port]] | .bot connect <ip[:port]> [count] [name]");
		ClientChat.Notice("  .bot control <id> (become it) | .bot return | .bot chat <id|off> | .bot say <id|all> <text>");
		ClientChat.Notice("  .bot fight [on|off] (sessions in your world hit enemies with their hotbar weapon) | .bot ai <id|all> [on|off] (AFK AI answers as that session)");
		ClientChat.Notice("  .bot follow <id> [player|off] | .bot goto <id> <x> <y> | .bot list | .bot remove <id|all> | .bot reconnect <id|all> | .bot save <id>");
		ClientChat.Notice("  .bot uuid [random|pername|mine|<uuid>|forget] (now: " + UuidModeText + ") | .bot names");
		ClientChat.Notice("  .bot set [list | start|stop|reconnect <name>]: sets of bots (Bots > Sets)");
		ClientChat.Notice("  .bot run <all|set|id|name> <chat, /command or .step>: e.g. .bot run all /login {pass}  |  .bot run Fairies .pixie me");
		ClientChat.Notice("  .bot proxy [on|off|<per ip>|auto|<country>]: bots through proxies when your IP is full (now: " + (BotRoutes.Enabled ? "on" : "off") + ")");
	}

	#endregion
}
