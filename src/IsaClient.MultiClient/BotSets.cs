using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.IO;

namespace IsaClient.MultiClient;

/// <summary>One account of a set: its name, look, login and own steps.</summary>
public class BotMember
{
	public string Name = "";

	/// <summary>A .plr to look like (and carry), or "" for a generated look.</summary>
	public string PlayerFile = "";

	/// <summary>Its own UUID ("" = by the UUID mode of Options).</summary>
	public string Uuid = "";

	/// <summary>Its account password on the server, for steps like "/login {pass}". Kept on this PC only.</summary>
	public string Login = "";

	public bool Enabled = true;

	/// <summary>Steps only this one runs, before the set's.</summary>
	public List<string> Steps = new List<string>();
}

/// <summary>A team of bots that join a server together and run the same steps when they arrive.</summary>
public class BotSet
{
	public string Id = Guid.NewGuid().ToString("N").Substring(0, 10);

	public string Name = "New set";

	/// <summary>The item shown as its icon.</summary>
	public int Icon = Terraria.ID.ItemID.RobotHat;

	public string Host = "";

	public int Port = 7777;

	public string ServerPassword = "";

	public List<BotMember> Members = new List<BotMember>();

	/// <summary>What every member does when it arrives: chat, /server commands and bot steps (.wait, .pixie...).</summary>
	public List<string> Steps = new List<string>();

	/// <summary>Seconds between two members joining (servers dislike many joins at once).</summary>
	public float JoinGap = 2f;

	/// <summary>Seconds between two steps.</summary>
	public float StepGap = 1.5f;

	/// <summary>Run the steps again when a member reconnects (it has to /login again).</summary>
	public bool StepsOnReconnect = true;

	/// <summary>Members follow you after their steps (when they are in your world).</summary>
	public bool FollowMe;

	/// <summary>Members fight enemies near them (in your world).</summary>
	public bool Fight = true;

	/// <summary>Members answer with the AI when someone says their name.</summary>
	public bool Ai;
}

/// <summary>
/// Sets of bots: create them, start them (members join one after another, never past the bot limit), stop them,
/// reconnect them, and give an order to all of them at once. Saved in IsaBots.json with the rest of the bots.
/// </summary>
public static class BotSets
{
	public static List<BotSet> Sets = new List<BotSet>();

	/// <summary>Steps for every bot that is not in a set.</summary>
	public static List<string> AnySteps = new List<string>();

	public static float AnyStepGap = 1.5f;

	public static bool AnyStepsOnReconnect = true;

	/// <summary>The buff pixie bots carry while they are pixies (384 = the Pixie mount's; 0 = none).</summary>
	public static int PixieBuff = Terraria.ID.BuffID.PixieMount;

	/// <summary>Pixie bots go after the enemy you hit (for <see cref="ChaseSeconds"/> after your last hit on it).</summary>
	public static bool PixiesAttack = true;

	public static float ChaseSeconds = 6f;

	private static int _target = -1;

	private static byte _targetGeneration;

	private static uint _targetAt;

	/// <summary>Pixie bots turn PvP on and off with you (on your server), and go after the players you hit.</summary>
	public static bool PixiesPvp = true;

	/// <summary>Damage of a pixie's hit on a player (under the server's MaxDamage; TShock's default is 1175).</summary>
	public static int PixiePvpDamage = 100;

	private static int _pvpTarget = -1;

	private static uint _pvpAt;

	/// <summary>The client sent a PvP hit on a player (you hit them): pixies go after them.</summary>
	public static void NotePlayerStrike(int player)
	{
		if (player < 0 || player >= 255 || !Main.player[player].active || IsOwn(player))
		{
			return;
		}
		_pvpTarget = player;
		_pvpAt = Main.GameUpdateCount;
	}

	/// <summary>The player you are fighting now (hit in the last seconds, PvP on, alive), or null.</summary>
	public static Player YourPlayerTarget
	{
		get
		{
			if (!PixiesPvp || _pvpTarget < 0 || Main.GameUpdateCount - _pvpAt > (uint)(ChaseSeconds * 60f))
			{
				return null;
			}
			Player p = Main.player[_pvpTarget];
			return (p != null && p.active && !p.dead && p.hostile && !IsOwn(_pvpTarget)) ? p : null;
		}
	}

	/// <summary>One of your own players: you or one of your bots (never a target).</summary>
	private static bool IsOwn(int player)
	{
		return player == Main.myPlayer || MultiClientManager.All.Any(s => s.MyIndex == player);
	}

	/// <summary>Every tick: pixie bots on your server have PvP on exactly while you do.</summary>
	private static void SyncPvp(List<ClientSession> sessions)
	{
		bool inWorld = Main.netMode == 1 && !Main.gameMenu;
		bool mine = inWorld && Main.LocalPlayer.hostile;
		foreach (ClientSession s in sessions)
		{
			if (s.State != ClientSessionState.InGame || s.Possessed)
			{
				continue;
			}
			bool want = PixiesPvp && s.IsPixie && mine && Possession.IsOnMyServer(s);
			if (s.Hostile != want && (want || s.IsPixie || s.Hostile))
			{
				s.SetHostile(want);
			}
		}
	}

	/// <summary>The client sent a strike on an NPC (you, or a command you ran as a bot): pixies go after it.</summary>
	public static void NoteStrike(int npc)
	{
		if (npc < 0 || npc >= Main.maxNPCs || Main.npc[npc] == null || !Main.npc[npc].active || Main.npc[npc].friendly)
		{
			return;
		}
		_target = npc;
		_targetGeneration = Main.npc[npc].generation;
		_targetAt = Main.GameUpdateCount;
	}

	/// <summary>The enemy you are fighting now (hit in the last seconds, still alive), or null.</summary>
	public static NPC YourTarget
	{
		get
		{
			if (!PixiesAttack || _target < 0 || Main.GameUpdateCount - _targetAt > (uint)(ChaseSeconds * 60f))
			{
				return null;
			}
			NPC npc = Main.npc[_target];
			return (npc != null && npc.active && !npc.friendly && npc.life > 0 && npc.generation == _targetGeneration) ? npc : null;
		}
	}

	/// <summary>Another buff for pixie bots; the ones flying now show it at once.</summary>
	public static void SetPixieBuff(int buff)
	{
		PixieBuff = (buff >= 0 && buff < Terraria.ID.BuffID.Count) ? buff : 0;
		foreach (ClientSession s in MultiClientManager.All.Where(s => s.IsPixie))
		{
			s.ResendBuffs();
		}
		MultiClientManager.Save();
	}

	/// <summary>Icons a set can have.</summary>
	public static readonly int[] Icons =
	{
		Terraria.ID.ItemID.RobotHat, Terraria.ID.ItemID.Mannequin, Terraria.ID.ItemID.GoldCrown, Terraria.ID.ItemID.PlatinumCrown, Terraria.ID.ItemID.FairyBell,
		Terraria.ID.ItemID.CopperShortsword, Terraria.ID.ItemID.Zenith, Terraria.ID.ItemID.TerraBlade, Terraria.ID.ItemID.WizardHat, Terraria.ID.ItemID.PirateHat,
		Terraria.ID.ItemID.NinjaHood, Terraria.ID.ItemID.SantaHat, Terraria.ID.ItemID.CowboyHat, Terraria.ID.ItemID.Heart, Terraria.ID.ItemID.FallenStar,
		Terraria.ID.ItemID.SlimeCrown, Terraria.ID.ItemID.SuspiciousLookingEye, Terraria.ID.ItemID.GuideVoodooDoll, Terraria.ID.ItemID.LifeCrystal, Terraria.ID.ItemID.Diamond
	};

	private sealed class PendingJoin
	{
		public BotSet Set;

		public BotMember Member;

		public DateTime Due;
	}

	private static readonly List<PendingJoin> _pending = new List<PendingJoin>();

	#region Settings

	internal static void Load(Preferences config)
	{
		Sets = config.Get("Sets", new List<BotSet>()) ?? new List<BotSet>();
		MultiPaste.Load(config);
		foreach (BotSet set in Sets)
		{
			set.Members ??= new List<BotMember>();
			set.Steps ??= new List<string>();
			foreach (BotMember m in set.Members)
			{
				m.Steps ??= new List<string>();
			}
		}
		AnySteps = config.Get("AnySteps", new List<string>()) ?? new List<string>();
		AnyStepGap = config.Get("AnyStepGap", 1.5f);
		config.Get("AnyStepsOnReconnect", ref AnyStepsOnReconnect);
		PixieBuff = config.Get("PixieBuff", (int)Terraria.ID.BuffID.PixieMount);
		config.Get("PixiesAttack", ref PixiesAttack);
		ChaseSeconds = config.Get("PixieChaseSeconds", 6f);
		config.Get("PixiesPvp", ref PixiesPvp);
		PixiePvpDamage = Math.Max(1, config.Get("PixiePvpDamage", 100));
	}

	internal static void Save(Preferences config)
	{
		config.Put("Sets", Sets);
		MultiPaste.Save(config);
		config.Put("AnySteps", AnySteps);
		config.Put("AnyStepGap", AnyStepGap);
		config.Put("AnyStepsOnReconnect", AnyStepsOnReconnect);
		config.Put("PixieBuff", PixieBuff);
		config.Put("PixiesAttack", PixiesAttack);
		config.Put("PixieChaseSeconds", ChaseSeconds);
		config.Put("PixiesPvp", PixiesPvp);
		config.Put("PixiePvpDamage", PixiePvpDamage);
	}

	#endregion

	#region Editing

	public static BotSet Create()
	{
		BotSet set = new BotSet { Name = "Set " + (Sets.Count + 1) };
		if (MultiClientManager.TryGetCurrentServer(out string host, out int port))
		{
			set.Host = host;
			set.Port = port;
		}
		Sets.Add(set);
		MultiClientManager.Save();
		return set;
	}

	public static BotSet Duplicate(BotSet set)
	{
		BotSet copy = new BotSet
		{
			Name = set.Name + " (copy)",
			Icon = set.Icon,
			Host = set.Host,
			Port = set.Port,
			ServerPassword = set.ServerPassword,
			Steps = new List<string>(set.Steps),
			JoinGap = set.JoinGap,
			StepGap = set.StepGap,
			StepsOnReconnect = set.StepsOnReconnect,
			FollowMe = set.FollowMe,
			Fight = set.Fight,
			Ai = set.Ai,
			Members = set.Members.Select(m => new BotMember { Name = m.Name, PlayerFile = m.PlayerFile, Uuid = m.Uuid, Login = m.Login, Enabled = m.Enabled, Steps = new List<string>(m.Steps) }).ToList()
		};
		Sets.Insert(Sets.IndexOf(set) + 1, copy);
		MultiClientManager.Save();
		return copy;
	}

	public static void Delete(BotSet set)
	{
		Stop(set, quiet: true);
		Sets.Remove(set);
		MultiClientManager.Save();
	}

	/// <summary>Adds <paramref name="count"/> members named "Base1", "Base2"... after the ones it has.</summary>
	public static void AddNumbered(BotSet set, string baseName, int count)
	{
		baseName = string.IsNullOrWhiteSpace(baseName) ? "Bot" : baseName.Trim();
		int n = 1;
		for (int i = 0; i < count; i++)
		{
			while (set.Members.Any(m => string.Equals(m.Name, baseName + n, StringComparison.OrdinalIgnoreCase)))
			{
				n++;
			}
			string name = baseName + n;
			if (name.Length > 20)
			{
				name = name.Substring(0, 20);
			}
			set.Members.Add(new BotMember { Name = name });
		}
		MultiClientManager.Save();
	}

	/// <summary>Adds members with these names (the ones it doesn't have yet).</summary>
	public static void AddNames(BotSet set, IEnumerable<string> names)
	{
		foreach (string raw in names)
		{
			string name = raw.Trim();
			if (name.Length == 0 || set.Members.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}
			set.Members.Add(new BotMember { Name = (name.Length > 20) ? name.Substring(0, 20) : name });
		}
		MultiClientManager.Save();
	}

	/// <summary>The bots you have now become members (name, look, UUID, server).</summary>
	public static int ImportRunning(BotSet set)
	{
		int added = 0;
		foreach (ClientSession s in MultiClientManager.All.Where(s => !s.UserRemoved))
		{
			if (set.Members.Any(m => string.Equals(m.Name, s.Name, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}
			set.Members.Add(new BotMember { Name = s.Name, PlayerFile = s.TemplatePath ?? "", Uuid = s.Uuid ?? "" });
			if (string.IsNullOrEmpty(set.Host) && !string.IsNullOrEmpty(s.Host))
			{
				set.Host = s.Host;
				set.Port = s.Port;
			}
			s.SetId = set.Id;
			s.Member = set.Members[set.Members.Count - 1];
			added++;
		}
		MultiClientManager.Save();
		return added;
	}

	#endregion

	#region Running

	/// <summary>The bots of <paramref name="set"/> you have now (connected or not).</summary>
	public static List<ClientSession> Members(BotSet set)
	{
		return MultiClientManager.All.Where(s => s.SetId == set.Id && !s.UserRemoved).ToList();
	}

	public static int Online(BotSet set)
	{
		return Members(set).Count(s => s.State == ClientSessionState.InGame || s.Possessed);
	}

	public static int Waiting(BotSet set)
	{
		lock (_pending)
		{
			return _pending.Count(p => p.Set == set);
		}
	}

	public static bool IsRunning(BotSet set)
	{
		return Waiting(set) > 0 || Members(set).Count > 0;
	}

	public static BotSet SetOf(ClientSession s)
	{
		return (s.SetId == null) ? null : Sets.FirstOrDefault(x => x.Id == s.SetId);
	}

	/// <summary>Its members join one after another (the ones not there yet).</summary>
	public static void Start(BotSet set)
	{
		if (string.IsNullOrWhiteSpace(set.Host) || set.Port <= 0)
		{
			MultiClientManager.AnnounceError(set.Name + ": choose its server first.");
			return;
		}
		List<ClientSession> have = Members(set);
		List<BotMember> todo = set.Members.Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.Name) && !have.Any(s => string.Equals(s.Name, m.Name, StringComparison.OrdinalIgnoreCase))).ToList();
		lock (_pending)
		{
			todo.RemoveAll(m => _pending.Any(p => p.Member == m));
			int room = MultiClientManager.MaxSessions - MultiClientManager.Count - _pending.Count;
			if (todo.Count == 0)
			{
				MultiClientManager.Announce(set.Name + ": every member is already in.");
				return;
			}
			if (room <= 0)
			{
				MultiClientManager.AnnounceError("Bot limit reached (" + MultiClientManager.MaxSessions + ").");
				return;
			}
			int skipped = Math.Max(0, todo.Count - room);
			todo = todo.Take(room).ToList();
			DateTime at = DateTime.UtcNow;
			foreach (BotMember m in todo)
			{
				_pending.Add(new PendingJoin { Set = set, Member = m, Due = at });
				at = at.AddSeconds(Math.Max(0.2f, set.JoinGap));
			}
			MultiClientManager.Announce(set.Name + ": " + todo.Count + " joining " + set.Host + ":" + set.Port + ((todo.Count > 1) ? (", one every " + set.JoinGap.ToString("0.#") + " s") : "") + "." + ((skipped > 0) ? (" " + skipped + " left out: bot limit " + MultiClientManager.MaxSessions + ".") : ""));
		}
	}

	/// <summary>Its bots leave (and the ones still waiting to join don't).</summary>
	public static void Stop(BotSet set, bool quiet = false)
	{
		lock (_pending)
		{
			_pending.RemoveAll(p => p.Set == set);
		}
		List<ClientSession> members = Members(set);
		foreach (ClientSession s in members)
		{
			MultiClientManager.Remove(s);
		}
		if (!quiet && members.Count > 0)
		{
			MultiClientManager.Announce(set.Name + ": " + members.Count + " bots left.");
		}
	}

	public static void ReconnectAll(BotSet set)
	{
		foreach (ClientSession s in Members(set).Where(s => !s.Possessed))
		{
			MultiClientManager.Reconnect(s);
		}
	}

	/// <summary>Runs one step (chat, /command or .step) on each of <paramref name="bots"/> now.</summary>
	public static int RunNow(IEnumerable<ClientSession> bots, string line)
	{
		int done = 0;
		foreach (ClientSession s in bots)
		{
			if (s.State == ClientSessionState.InGame && !s.Possessed)
			{
				BotScript.RunLine(s, line);
				done++;
			}
		}
		return done;
	}

	/// <summary>Main thread, every tick: members due to join join; every bot's steps go on.</summary>
	internal static void Update(List<ClientSession> sessions)
	{
		DateTime now = DateTime.UtcNow;
		PendingJoin due = null;
		lock (_pending)
		{
			if (_pending.Count > 0 && now >= _pending[0].Due)
			{
				due = _pending[0];
				_pending.RemoveAt(0);
			}
		}
		if (due != null && Sets.Contains(due.Set))
		{
			Join(due.Set, due.Member);
		}
		foreach (ClientSession s in sessions)
		{
			BotScript.Tick(s, now);
		}
		if (Main.GameUpdateCount % 10 == 0)
		{
			SyncPvp(sessions);
		}
		MultiPaste.Update();
	}

	private static void Join(BotSet set, BotMember m)
	{
		if (MultiClientManager.Count >= MultiClientManager.MaxSessions)
		{
			return;
		}
		ClientSession bot = MultiClientManager.CreateFor(m.Name, m.PlayerFile, string.IsNullOrEmpty(m.Uuid) ? null : m.Uuid);
		bot.SetId = set.Id;
		bot.Member = m;
		bot.AiEnabled = set.Ai;
		bot.FightOverride = set.Fight;
		bot.Connect(set.Host, set.Port, string.IsNullOrEmpty(set.ServerPassword) ? null : set.ServerPassword);
		if (string.IsNullOrEmpty(m.Uuid))
		{
			m.Uuid = bot.Uuid ?? ""; // the same UUID every time from now on (accounts on the server are tied to it)
		}
		MultiClientManager.Save();
	}

	#endregion
}
