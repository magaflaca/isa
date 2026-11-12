using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using System.Linq;
using Terraria.DataStructures;
using Terraria.ID;
using IsaClient.Assistant;
using IsaClient.Networking;
using Terraria;

namespace IsaClient.MultiClient;

/// <summary>Connection state of an extra client session.</summary>
public enum ClientSessionState
{
	Idle,
	Connecting,
	Handshaking,
	InGame,
	Disconnected
}

/// <summary>
/// A single lightweight headless Terraria client session (MultiClient). It performs the vanilla 1.4.5.8 (release 326) handshake on
/// its own background connection (or the 1.4.5.6 one, release 319, when the network version is 1.4.5.6: <see cref="ProtocolCompat"/>), appears in the server as a normal player, and can chat and move. It can look like one of
/// your .plr characters (appearance, equipment, inventory) or a generated one. Its connection can be handed to the game and
/// back without reconnecting (see <see cref="Possession"/>). Protocol ported from TerrariaHeadless.Core.
/// </summary>
public sealed class ClientSession
{
	/// <summary>1.4.5.6 protocol, fixed when the session is created (the network version can change while bots stay online).</summary>
	private readonly bool _legacy = ProtocolCompat.Legacy;

	private string VersionString => "Terraria" + (_legacy ? ProtocolCompat.LegacyRelease : ProtocolCompat.ModernRelease);

	public const int PlayerWidth = 20;

	public const int PlayerHeight = 42;

	private const float WalkSpeed = 3.2f;

	private const float Gravity = 0.4f;

	private const float MaxFall = 10f;

	private const float JumpSpeed = 8.2f;

	private ClientConnection _conn = new ClientConnection();

	private Timer _keepAlive;

	private readonly object _sync = new object();

	public int Id { get; internal set; }

	public string Name { get; private set; }

	public string Uuid;

	public string Host { get; private set; }

	public int Port { get; private set; }

	public string Password;

	/// <summary>Character file this session looks like (null = generated look, empty inventory).</summary>
	public Player Template;

	public string TemplatePath;

	/// <summary>What this connection received, used to rebuild its world in the game.</summary>
	public WorldJournal Journal = new WorldJournal(24L * 1024 * 1024);

	public ClientSessionState State { get; private set; } = ClientSessionState.Idle;

	public string StatusText = "idle";

	/// <summary>Where it connects from (<see cref="BotRoutes"/>): "direct" for your IP, else the proxy's key; null = none yet.</summary>
	public string RouteKey;

	/// <summary>The proxy it connects through, null for your own IP.</summary>
	public IsaClient.Networking.ProxyServer Route;

	/// <summary>The exit that last worked, tried first when it reconnects.</summary>
	public string LastRouteKey;

	/// <summary>It left because it was told to (not a drop).</summary>
	private bool _leftOnPurpose;

	/// <summary>Exits it dropped on without being kicked (a bad proxy, most times): it reconnects through another first.</summary>
	public readonly System.Collections.Generic.List<string> AvoidRoutes = new System.Collections.Generic.List<string>();

	/// <summary>It dropped without a kick: the exit it was on goes to the back of the line for its next connections.</summary>
	internal void AvoidCurrentRoute()
	{
		string key = RouteKey ?? LastRouteKey;
		if (key == null || _leftOnPurpose)
		{
			return;
		}
		lock (AvoidRoutes)
		{
			AvoidRoutes.Remove(key);
			AvoidRoutes.Add(key);
			while (AvoidRoutes.Count > 5)
			{
				AvoidRoutes.RemoveAt(0);
			}
		}
		if (LastRouteKey == key)
		{
			LastRouteKey = null;
		}
	}

	/// <summary>The set it belongs to (<see cref="BotSets"/>), or null.</summary>
	public string SetId;

	/// <summary>Its account in that set (login, own steps), or null.</summary>
	public BotMember Member;

	/// <summary>Its steps on arrival (<see cref="BotScript"/>).</summary>
	public BotScript Script;

	/// <summary>Fights or not regardless of the Options switch (its set or a .fight step); null = the switch.</summary>
	public bool? FightOverride;

	/// <summary>The player it flies around as a pixie, or -1: not a pixie.</summary>
	private int _pixieTarget = -1;

	private float _pixieClock;

	public bool IsPixie => _pixieTarget >= 0;

	/// <summary>It pastes its part of a Multi Paste: the paste moves it, so it doesn't move by itself meanwhile.</summary>
	public volatile bool Pasting;

	private DateTime _disabledUntil;

	/// <summary>TShock disabled it (its Webbed debuff): its paste waits, as yours does.</summary>
	public bool TShockDisabled => DateTime.UtcNow < _disabledUntil;

	/// <summary>What it wore in the outfit slots before an outfit (type, stack, prefix for each), or null.</summary>
	private int[] _outfitSaved;

	/// <summary>It wears an outfit put on it (Multi Paste's plumbers).</summary>
	public bool InOutfit => _outfitSaved != null;

	private int[] _outfitWear;

	/// <summary>
	/// The outfit is really on it: every piece still in its slot (the server can put its own clothes back, e.g. when its
	/// saved character is loaded right after it joins).
	/// </summary>
	public bool OutfitIntact
	{
		get
		{
			int[] wear = _outfitWear;
			if (_outfitSaved == null || wear == null)
			{
				return false;
			}
			for (int i = 0; i < OutfitSlots.Length; i++)
			{
				if (_slotType[OutfitSlots[i]] != wear[i])
				{
					return false;
				}
			}
			return true;
		}
	}

	/// <summary>Slots the server (SSC) told it about.</summary>
	private readonly bool[] _slotKnown = new bool[PlayerItemSlotID.Count];

	/// <summary>Vanity head, body, legs and the three dyes on them.</summary>
	private static readonly int[] OutfitSlots =
	{
		PlayerItemSlotID.Armor0 + 10, PlayerItemSlotID.Armor0 + 11, PlayerItemSlotID.Armor0 + 12,
		PlayerItemSlotID.Dye0, PlayerItemSlotID.Dye0 + 1, PlayerItemSlotID.Dye0 + 2
	};

	public int MyIndex = 255;

	public bool Spawned { get; private set; }

	public Vector2 Position;

	public Vector2 Velocity;

	public Vector2 SpawnPosition;

	public int StatLife = 100;

	public int StatLifeMax = 100;

	public int StatMana = 20;

	public int StatManaMax = 20;

	public byte Direction = 1;

	/// <summary>True while the game itself drives this session's connection (the player "is" this session).</summary>
	public bool Possessed { get; private set; }

	/// <summary>True for the player's own connection kept alive headless while they play as another session.</summary>
	public bool IsMainSession { get; private set; }

	/// <summary>Set when the server kicked it (no auto-reconnect then).</summary>
	public bool WasKicked { get; private set; }

	/// <summary>Removed on purpose by the user (no auto-reconnect).</summary>
	public bool UserRemoved;

	public int ReconnectTries;

	public DateTime ReconnectAt = DateTime.MaxValue;

	private Vector2? _gotoTarget;

	private int _followPlayer = -1;

	private int _ticksSinceSend;

	private int _stuckTicks;

	private bool _onGround;

	private float _stepSpeed = 1f;

	private float _gfxOffY;

	private bool _worldRequested;

	private short _spawnTileX = -1;

	private short _spawnTileY = -1;

	private byte _team;

	private bool _controlLeft;

	private bool _controlRight;

	private bool _controlJump;

	/// <summary>AFK AI answers chat as this session (by its name), also while you are not playing as it.</summary>
	public bool AiEnabled;

	public readonly AiAssistant.Agent Ai = new AiAssistant.Agent();

	/// <summary>Mount it is sitting on (-1 = none); sent with its position so others see it mounted.</summary>
	public int MountType = -1;

	/// <summary>Stay exactly where the game left it (no gravity, keeps its mount) until told to move.</summary>
	public bool HoldPosition;

	// What it carries, by PlayerItemSlotID (types/prefixes only; items are looked up in ContentSamples on the main thread).
	private readonly int[] _slotType = new int[PlayerItemSlotID.Count];

	private readonly int[] _slotStack = new int[PlayerItemSlotID.Count];

	private readonly byte[] _slotPrefix = new byte[PlayerItemSlotID.Count];

	private int _pickupTick;

	/// <summary>Items it could not take (full inventory): not retried for a while.</summary>
	private readonly System.Collections.Generic.Dictionary<int, uint> _skipItems = new System.Collections.Generic.Dictionary<int, uint>();

	private int _selectedItem;

	private int _attackCooldown;

	private int _useAnimLeft;

	private int _useAnimMax;

	private float _itemRotation;

	private int _fightTarget = -1;

	private int _fightShownTicks;

	// Follow: which side of the player it keeps, whether it is on its way, and whether it hovers next to a flying player.
	private int _followSide;

	private bool _followMoving;

	private bool _followHover;

	// Projectiles it shows for ranged/magic/whip attacks (keys it owns) and hits waiting for the projectile to arrive.
	private int _projIndex = 900;

	private int _projGeneration = 1;

	private struct PendingHit
	{
		public int Ticks;

		public int Npc;

		public byte Generation;

		public int Damage;

		public float KnockBack;

		public int Direction;

		public bool Crit;

		public int ProjectileKey;

		public bool KillProjectile;
	}

	private readonly System.Collections.Generic.List<PendingHit> _pendingHits = new System.Collections.Generic.List<PendingHit>();

	/// <summary>NPC it is fighting right now (-1 = none).</summary>
	public int FightTarget => (_fightShownTicks > 0) ? _fightTarget : -1;

	/// <summary>Raised on a background thread: (session, message text already stripped of tags, senderName or null).</summary>
	/// <summary>(session, chat line exactly as it arrived - item/color tags kept, sender name or null, the server's color).</summary>
	public event Action<ClientSession, string, string, Color> OnChat;

	public event Action<ClientSession, string> OnStateChanged;

	public ClientSession(int id, string name, string uuid)
	{
		Id = id;
		Name = name;
		Uuid = uuid;
	}

	public bool IsConnected => Possessed || _conn.IsConnected;

	public bool IsFollowing => _followPlayer >= 0;

	public string FollowingName
	{
		get
		{
			int f = _followPlayer;
			return (f >= 0 && f < 255 && Main.player[f] != null) ? Main.player[f].name : null;
		}
	}

	#region Hand-over with the game

	/// <summary>Wraps the player's own, already in-game connection (taken from the game) as a headless session.</summary>
	public static ClientSession AdoptMain(string name, string uuid, string host, int port, int playerIndex, WorldJournal journal)
	{
		return new ClientSession(0, name, uuid)
		{
			IsMainSession = true,
			Host = host,
			Port = port,
			MyIndex = playerIndex,
			State = ClientSessionState.InGame,
			StatusText = "in game (you)",
			Spawned = true,
			Possessed = true,
			Journal = journal
		};
	}

	/// <summary>Hands this session's live socket to the game. Stops the headless side; returns null if the connection is gone.</summary>
	public TcpClient DetachForGame(out byte[] leftover)
	{
		leftover = null;
		if (Possessed || State != ClientSessionState.InGame)
		{
			return null;
		}
		ClientConnection conn = _conn;
		conn.OnPacket -= HandlePacket;
		conn.OnClosed -= OnClosed;
		TcpClient tcp = conn.Detach(out leftover);
		if (tcp == null)
		{
			conn.OnClosed += OnClosed;
			OnClosed("connection lost");
			return null;
		}
		Possessed = true;
		StatusText = "you are playing as it";
		return tcp;
	}

	/// <summary>Takes a live socket back from the game and keeps it alive headless at the given position.</summary>
	public void AttachHeadless(TcpClient tcp, byte[] leftover, Player from)
	{
		lock (_sync)
		{
			Position = from.position;
			Velocity = Vector2.Zero;
			_gotoTarget = null;
			_followPlayer = -1;
		}
		StatLife = from.statLife;
		StatLifeMax = Math.Max(20, from.statLifeMax);
		Direction = (byte)((from.direction < 0) ? 0 : 1);
		MountType = from.mount.Active ? from.mount.Type : -1;
		HoldPosition = true;
		CopySlotsFrom(from);
		ClientConnection conn = new ClientConnection();
		conn.OnPacket += HandlePacket;
		conn.OnClosed += OnClosed;
		_conn = conn;
		Possessed = false;
		State = ClientSessionState.InGame;
		StatusText = IsMainSession ? "in game (you, idle)" : "in game";
		conn.Adopt(tcp, leftover);
		StartKeepAlive();
		Send(PktPlayerControls());
	}

	/// <summary>Remembers what a Player carries (hotbar, equipment, mount slot) for fighting and flying decisions.</summary>
	private void CopySlotsFrom(Player p)
	{
		if (p == null)
		{
			return;
		}
		for (int slot = 0; slot < _slotType.Length; slot++)
		{
			try
			{
				Item item = new PlayerItemSlotID.SlotReference(p, slot).Item;
				bool has = item != null && item.stack > 0 && item.type > 0;
				_slotType[slot] = has ? item.type : 0;
				_slotStack[slot] = has ? item.stack : 0;
				_slotPrefix[slot] = has ? item.prefix : (byte)0;
			}
			catch
			{
				_slotType[slot] = 0;
				_slotStack[slot] = 0;
				_slotPrefix[slot] = 0;
			}
		}
	}

	/// <summary>Called when the game dropped this possessed connection itself.</summary>
	public void MarkGone(string reason)
	{
		Possessed = false;
		State = ClientSessionState.Disconnected;
		StatusText = reason;
		Spawned = false;
		StopKeepAlive();
		Notify();
	}

	#endregion

	#region Modes: kill aura, spam chat, god (.killaura, .spamchat, .god on a bot)

	/// <summary>Kill aura size in tiles (0 = off): enemies that close take 1500 damage, as the client's kill aura.</summary>
	public int KillAuraSize;

	/// <summary>What it says over and over (.spamchat), or null.</summary>
	public string SpamText;

	/// <summary>Ticks between two spam lines (.spamdelay).</summary>
	public int SpamDelay = 30;

	private int _spamTimer;

	/// <summary>Takes no damage (the default: a bot has no body of its own). .god switches it: off, it gets hurt, dies and respawns.</summary>
	public bool God = true;

	private int _hurtImmune;

	private int _respawnLeft = -1;

	private int _auraTick;

	/// <summary>Its PvP switch (pixies follow yours).</summary>
	public bool Hostile { get; private set; }

	private int _pvpCooldown;

	/// <summary>Turns its PvP on or off (the server and everyone see it).</summary>
	internal void SetHostile(bool on)
	{
		Hostile = on;
		Send(Frame(30, delegate(BinaryWriter w)
		{
			w.Write((byte)MyIndex);
			w.Write(on);
		}));
	}

	public bool Dead => _respawnLeft >= 0;

	/// <summary>Kill aura: on with a size, or toggled (size 1 when it had none), like the client's .killaura.</summary>
	public void SetKillAura(int? size)
	{
		if (size.HasValue)
		{
			KillAuraSize = Math.Max(0, size.Value);
		}
		else
		{
			KillAuraSize = (KillAuraSize > 0) ? 0 : 1;
		}
	}

	/// <summary>Spam chat: the text over and over, or off (null/empty), like the client's .spamchat.</summary>
	public void SetSpam(string text)
	{
		SpamText = string.IsNullOrWhiteSpace(text) ? null : text;
		_spamTimer = 0;
	}

	/// <summary>Every tick it is in game: its spam line, its kill aura and, without god, the hits it takes.</summary>
	private void UpdateModes(bool worldHere)
	{
		if (SpamText != null && ++_spamTimer >= Math.Max(1, SpamDelay))
		{
			_spamTimer = 0;
			string line = SpamText;
			if (line.StartsWith(".", StringComparison.Ordinal) && !line.StartsWith(".spamchat", StringComparison.OrdinalIgnoreCase))
			{
				BotScript.RunLine(this, line);
			}
			else
			{
				Say(line);
			}
		}
		if (!worldHere || Dead)
		{
			return;
		}
		int aura = Math.Max(KillAuraSize, IsPixie ? 1 : 0);
		if (aura > 0 && ++_auraTick % 2 == 0)
		{
			KillAura(aura);
		}
		if (IsPixie && Hostile)
		{
			HitPlayerTarget();
		}
		if (!God)
		{
			TakeHits();
		}
	}

	/// <summary>The client's kill aura (Combat.KillBubble) from the bot: 1500 damage to every enemy in the square around it.</summary>
	private void KillAura(int radius)
	{
		Point me = (Position + new Vector2(PlayerWidth / 2f, PlayerHeight / 2f)).ToTileCoordinates();
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			NPC npc = Main.npc[i];
			if (!npc.active || npc.friendly || npc.type == 488 || npc.dontTakeDamage)
			{
				continue;
			}
			Point at = npc.position.ToTileCoordinates();
			if (Math.Abs((long)at.X - me.X) <= radius && Math.Abs((long)at.Y - me.Y) <= radius)
			{
				Send(PktStrikeNpc(i, npc.generation, 1500, 0.5f, -npc.direction, false));
			}
		}
	}

	/// <summary>A pixie with PvP on hits the player you are fighting when it touches them, as a player's PvP hit (packet 117).</summary>
	private void HitPlayerTarget()
	{
		if (_pvpCooldown > 0)
		{
			_pvpCooldown--;
			return;
		}
		Player target = BotSets.YourPlayerTarget;
		if (target == null || (target.team != 0 && target.team == _team))
		{
			return;
		}
		Rectangle reach = new Rectangle((int)Position.X - 16, (int)Position.Y - 16, PlayerWidth + 32, PlayerHeight + 32);
		if (!reach.Intersects(target.Hitbox))
		{
			return;
		}
		int direction = (target.Center.X < Position.X + PlayerWidth / 2f) ? -1 : 1;
		int damage = Math.Max(1, BotSets.PixiePvpDamage);
		PlayerDeathReason reason = PlayerDeathReason.ByPlayer(MyIndex);
		Send(Frame(117, delegate(BinaryWriter w)
		{
			w.Write((byte)target.whoAmI);
			reason.WriteSelfTo(w);
			w.Write((short)damage);
			w.Write((byte)(direction + 1));
			w.Write((byte)2); // pvp
			w.Write((sbyte)-1);
		}));
		_pvpCooldown = 20;
	}

	/// <summary>Without god: enemies touching it and hostile projectiles hurt it (minus its defense), with the usual moment of immunity.</summary>
	private void TakeHits()
	{
		if (_hurtImmune > 0)
		{
			_hurtImmune--;
			return;
		}
		Rectangle body = new Rectangle((int)Position.X, (int)Position.Y, PlayerWidth, PlayerHeight);
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			NPC npc = Main.npc[i];
			if (npc.active && !npc.friendly && npc.damage > 0 && npc.Hitbox.Intersects(body))
			{
				Hurt(Main.DamageVar(npc.damage), PlayerDeathReason.ByNPC(i), (npc.Center.X < body.Center.X) ? 1 : -1);
				return;
			}
		}
		for (int i = 0; i < Main.maxProjectiles; i++)
		{
			Projectile p = Main.projectile[i];
			if (p.active && p.hostile && p.damage > 0 && p.Hitbox.Intersects(body))
			{
				Hurt(Main.DamageVar(p.damage * 2), PlayerDeathReason.ByProjectile(255, i), (p.Center.X < body.Center.X) ? 1 : -1);
				return;
			}
		}
	}

	/// <summary>Its defense: armor and accessories it wears.</summary>
	private int Defense()
	{
		int defense = 0;
		for (int i = 0; i < 10; i++)
		{
			Item item = Sample(_slotType[PlayerItemSlotID.Armor0 + i]);
			if (item != null)
			{
				defense += item.defense;
			}
		}
		return defense;
	}

	private void Hurt(int damage, PlayerDeathReason reason, int direction)
	{
		damage = Math.Max(1, damage - Defense() / 2);
		StatLife -= damage;
		_hurtImmune = 40;
		Send(PktHurt(damage, reason, direction));
		if (StatLife <= 0)
		{
			StatLife = 0;
			Send(PktDeath(damage, reason, direction));
			_respawnLeft = 60 * 10;
			StopPixie();
			StopMoving();
			Velocity = Vector2.Zero;
			return;
		}
		Send(PktLife());
	}

	/// <summary>Dead: waits, then comes back at its spawn point with full life.</summary>
	private void UpdateDead()
	{
		if (--_respawnLeft > 0)
		{
			return;
		}
		_respawnLeft = -1;
		StatLife = Math.Max(20, StatLifeMax);
		lock (_sync)
		{
			Position = SpawnPosition;
			Velocity = Vector2.Zero;
		}
		Send(PktSpawn(0)); // ReviveFromDeath
		Send(PktLife());
		Send(PktPlayerControls());
	}

	private byte[] PktHurt(int damage, PlayerDeathReason reason, int direction)
	{
		return Frame(117, delegate(BinaryWriter w)
		{
			w.Write((byte)MyIndex);
			reason.WriteSelfTo(w);
			w.Write((short)damage);
			w.Write((byte)(direction + 1));
			w.Write((byte)0);
			w.Write((sbyte)-1);
		});
	}

	private byte[] PktDeath(int damage, PlayerDeathReason reason, int direction)
	{
		return Frame(118, delegate(BinaryWriter w)
		{
			w.Write((byte)MyIndex);
			reason.WriteSelfTo(w);
			w.Write((short)damage);
			w.Write((byte)(direction + 1));
			w.Write((byte)0);
		});
	}

	#endregion

	#region Connect / disconnect

	public void Connect(string host, int port, string password)
	{
		Host = host;
		Port = port;
		Password = password;
		WasKicked = false;
		_leftOnPurpose = false;
		ReconnectAt = DateTime.MaxValue;
		State = ClientSessionState.Connecting;
		StatusText = "connecting to " + host + ":" + port;
		Notify();
		_conn = new ClientConnection();
		_conn.OnPacket += HandlePacket;
		_conn.OnClosed += OnClosed;
		ClientConnection conn = _conn;
		System.Threading.Tasks.Task.Run(delegate
		{
			try
			{
				conn.Adopt(BotRoutes.Open(this, host, port), null);
				if (conn != _conn)
				{
					conn.Close("replaced");
					return;
				}
				Send(PktHello());
				State = ClientSessionState.Handshaking;
				StatusText = "handshaking";
				Notify();
				StartKeepAlive();
			}
			catch (Exception ex)
			{
				State = ClientSessionState.Disconnected;
				StatusText = "connect failed: " + ex.Message;
				Notify();
			}
		});
	}

	/// <summary>Connects again with the same name, look, UUID and server.</summary>
	public void Reconnect()
	{
		if (Possessed)
		{
			return;
		}
		try
		{
			_conn.Close("reconnecting");
		}
		catch
		{
		}
		Spawned = false;
		_worldRequested = false;
		MyIndex = 255;
		Journal = new WorldJournal(24L * 1024 * 1024);
		lock (_sync)
		{
			_gotoTarget = null;
		}
		ReconnectTries++;
		Connect(Host, Port, Password);
	}

	public void Disconnect(string reason = "left")
	{
		if (Possessed)
		{
			return; // the game owns this connection right now
		}
		StatusText = reason;
		_leftOnPurpose = true;
		State = ClientSessionState.Disconnected;
		ReconnectAt = DateTime.MaxValue;
		StopKeepAlive();
		_conn.Close(reason);
	}

	private void StartKeepAlive()
	{
		if (_keepAlive == null)
		{
			_keepAlive = new Timer(KeepAliveTick, null, 1000, 1000);
		}
	}

	private void StopKeepAlive()
	{
		try
		{
			_keepAlive?.Dispose();
		}
		catch
		{
		}
		_keepAlive = null;
	}

	private void OnClosed(string reason)
	{
		if (Possessed)
		{
			return;
		}
		State = ClientSessionState.Disconnected;
		StatusText = reason ?? "disconnected";
		Spawned = false;
		StopKeepAlive();
		Notify();
	}

	private void Notify()
	{
		OnStateChanged?.Invoke(this, StatusText);
	}

	private void Send(byte[] packet)
	{
		_conn.Send(packet);
	}

	#endregion

	#region Commands

	public void Say(string text)
	{
		if (!Possessed && State == ClientSessionState.InGame && !string.IsNullOrEmpty(text))
		{
			Send(PktChat(text));
		}
	}

	/// <summary>Sends a whole packet as it is (a command run as this bot, <see cref="BotCommands"/>).</summary>
	internal void SendRaw(byte[] packet)
	{
		_conn.Send(packet);
	}

	/// <summary>
	/// Puts on <paramref name="head"/>, <paramref name="body"/>, <paramref name="legs"/> as vanity with <paramref name="dye"/>
	/// on each (everyone sees it); what it wore there comes back with <see cref="TakeOffOutfit"/>.
	/// </summary>
	internal void WearOutfit(int head, int body, int legs, int dye)
	{
		if (State != ClientSessionState.InGame || Possessed)
		{
			return;
		}
		if (_outfitSaved == null)
		{
			int[] saved = new int[OutfitSlots.Length * 3];
			for (int i = 0; i < OutfitSlots.Length; i++)
			{
				int slot = OutfitSlots[i];
				int type = 0;
				int stack = 0;
				int prefix = 0;
				if (_slotKnown[slot])
				{
					type = _slotType[slot];
					stack = _slotStack[slot];
					prefix = _slotPrefix[slot];
				}
				else if (Template != null)
				{
					try
					{
						Item item = new PlayerItemSlotID.SlotReference(Template, slot).Item;
						if (item != null && item.type > 0 && item.stack > 0)
						{
							type = item.type;
							stack = item.stack;
							prefix = item.prefix;
						}
					}
					catch
					{
					}
				}
				saved[i * 3] = type;
				saved[i * 3 + 1] = stack;
				saved[i * 3 + 2] = prefix;
			}
			_outfitSaved = saved;
		}
		int[] wear = { head, body, legs, dye, dye, dye };
		for (int i = 0; i < OutfitSlots.Length; i++)
		{
			Send(PktSlotItem(OutfitSlots[i], wear[i], 1, 0));
			// What it wears now as far as we know: the server (SSC) changing it again shows up here.
			_slotType[OutfitSlots[i]] = wear[i];
			_slotStack[OutfitSlots[i]] = 1;
			_slotPrefix[OutfitSlots[i]] = 0;
		}
		_outfitWear = wear;
	}

	/// <summary>The buff it shows while Multi Paste is on (it builds with you), or 0.</summary>
	public int WorkBuff { get; private set; }

	/// <summary>The mount that buff puts it on (the Witch's Broom for 230), or -1: everyone sees it riding it.</summary>
	public int WorkMount { get; private set; } = -1;

	/// <summary>It follows someone through the air: it hovers beside them (going through walls).</summary>
	private bool _airFollow;

	/// <summary>A bot of Multi Paste while it is on: it follows through the air and through walls, whatever it rides.</summary>
	public bool InMultiPaste;

	private DateTime _buffsAgainAt = DateTime.MaxValue;

	/// <summary>Main thread: sends its buffs again when the server replaced them (see packet 50).</summary>
	internal void KeepOwnBuffs()
	{
		if (DateTime.UtcNow >= _buffsAgainAt)
		{
			_buffsAgainAt = DateTime.MaxValue;
			ResendBuffs();
		}
	}

	/// <summary>Shows <paramref name="buff"/> on it (everyone sees it), or takes it away (0).</summary>
	internal void SetWorkBuff(int buff)
	{
		buff = (buff > 0 && buff < BuffID.Count) ? buff : 0;
		if (buff == WorkBuff)
		{
			return;
		}
		WorkBuff = buff;
		WorkMount = Mount.IsaMountOfBuff(buff);
		if (State == ClientSessionState.InGame && !Possessed)
		{
			Send(PktBuffs());
			_ticksSinceSend = 60; // on or off the mount at once
		}
	}

	/// <summary>What it wore before the outfit, back on.</summary>
	internal void TakeOffOutfit()
	{
		int[] saved = _outfitSaved;
		_outfitSaved = null;
		_outfitWear = null;
		if (saved == null || State != ClientSessionState.InGame || Possessed)
		{
			return;
		}
		for (int i = 0; i < OutfitSlots.Length; i++)
		{
			Send(PktSlotItem(OutfitSlots[i], saved[i * 3], saved[i * 3 + 1], saved[i * 3 + 2]));
			_slotType[OutfitSlots[i]] = (saved[i * 3 + 1] > 0) ? saved[i * 3] : 0;
			_slotStack[OutfitSlots[i]] = saved[i * 3 + 1];
			_slotPrefix[OutfitSlots[i]] = (byte)saved[i * 3 + 2];
		}
	}

	private byte[] PktSlotItem(int slot, int type, int stack, int prefix)
	{
		bool air = type <= 0 || stack <= 0;
		return Frame(5, delegate(BinaryWriter w)
		{
			w.Write((byte)MyIndex);
			w.Write((short)slot);
			w.Write((short)(air ? 0 : stack));
			w.Write(air ? (byte)0 : (byte)prefix);
			w.Write((short)(air ? 0 : type));
			w.Write((byte)0);
		});
	}

	/// <summary>It is here now (a command moved it): goes on from there, standing still.</summary>
	internal void PlaceAt(Vector2 position)
	{
		lock (_sync)
		{
			_gotoTarget = null;
			_followMoving = false;
		}
		Position = position;
		Velocity = Vector2.Zero;
		_stuckTicks = 0;
		_ticksSinceSend = 60;
	}

	public void GoTo(Vector2 worldPos)
	{
		lock (_sync)
		{
			_followPlayer = -1;
			_gotoTarget = worldPos;
			_stuckTicks = 0;
			HoldPosition = false;
		}
	}

	public void Follow(int playerIndex)
	{
		lock (_sync)
		{
			_gotoTarget = null;
			_followPlayer = playerIndex;
			_stuckTicks = 0;
			_followSide = 0;
			_followMoving = true;
			_followHover = false;
			HoldPosition = false;
		}
	}

	/// <summary>Flies around <paramref name="playerIndex"/> on the Pixie mount, like a pixie does (in your world).</summary>
	public void StartPixie(int playerIndex)
	{
		lock (_sync)
		{
			_gotoTarget = null;
			_followPlayer = -1;
			_followMoving = false;
			_followHover = false;
			HoldPosition = false;
		}
		bool was = _pixieTarget >= 0;
		_pixieTarget = playerIndex;
		_pixieClock = (float)(Id * 1.7);
		_stuckTicks = 0;
		if (!was)
		{
			Send(PktBuffs());
		}
	}

	/// <summary>Sends its buffs again (the pixie buff changed).</summary>
	internal void ResendBuffs()
	{
		Send(PktBuffs());
	}

	public void StopPixie()
	{
		if (_pixieTarget < 0)
		{
			return;
		}
		_pixieTarget = -1;
		Send(PktBuffs());
		if (MountType == MountID.Pixie)
		{
			MountType = -1;
		}
		Velocity = Vector2.Zero;
		_ticksSinceSend = 60; // tell the server it got off
	}

	public void StopMoving()
	{
		lock (_sync)
		{
			_gotoTarget = null;
			_followPlayer = -1;
			_followMoving = false;
			_followHover = false;
		}
	}

	#endregion

	#region Movement (main thread)

	/// <summary>
	/// Runs once per game tick on the main thread. When <paramref name="worldHere"/> is true the game has this session's world
	/// loaded, so the session walks with gravity, tile collision, steps and jumps (and can follow players); otherwise it can
	/// only glide straight toward a goto target.
	/// </summary>
	public void UpdateMovement(bool worldHere)
	{
		if (Possessed || State != ClientSessionState.InGame || !Spawned || Pasting)
		{
			return;
		}
		UpdateModes(worldHere);
		if (Dead)
		{
			UpdateDead();
			return;
		}
		Vector2? target;
		int follow;
		lock (_sync)
		{
			target = _gotoTarget;
			follow = _followPlayer;
		}
		_followHover = false;
		_airFollow = false;
		if (follow >= 0)
		{
			Player p = (worldHere && follow < 255) ? Main.player[follow] : null;
			target = null;
			if (p != null && p.active && p.whoAmI != MyIndex && !p.dead)
			{
				target = FollowSpot(p);
			}
		}
		bool changed;
		Player pixieOf = (_pixieTarget >= 0 && worldHere && _pixieTarget < 255) ? Main.player[_pixieTarget] : null;
		if (_pixieTarget >= 0 && (pixieOf == null || !pixieOf.active))
		{
			StopPixie();
			pixieOf = null;
			changed = true;
		}
		if (pixieOf != null)
		{
			changed = Pixie(pixieOf);
		}
		else if (_airFollow && follow >= 0)
		{
			// On the broom: flies to its place beside you, then stays still in the air (no gravity, no hopping).
			if (target.HasValue)
			{
				changed = Fly(target.Value, WorkMount, wings: false);
			}
			else
			{
				// Others see it still on the broom: its place goes out a bit more often than standing on the ground.
				// Without a mount that hovers, other games pull it down between updates: those go out more often then.
				int every = (WorkMount >= 0 && Mount.IsaCanFly(WorkMount)) ? 20 : 6;
				changed = Velocity != Vector2.Zero || _controlLeft || _controlRight || _controlJump || _ticksSinceSend >= every;
				Velocity = Vector2.Zero;
				_controlLeft = _controlRight = _controlJump = false;
				_onGround = false;
			}
		}
		else if (HoldPosition && !target.HasValue)
		{
			changed = false; // left by the game where it was (maybe mounted or flying): stay there
			Velocity = Vector2.Zero;
			_controlLeft = _controlRight = _controlJump = false;
		}
		else if (worldHere && TileAreaLoaded())
		{
			changed = WalkWithPhysics(target);
		}
		else
		{
			changed = Glide(target);
		}
		if (_fightShownTicks > 0)
		{
			_fightShownTicks--;
		}
		if (_useAnimLeft > 0)
		{
			_useAnimLeft--;
			if (_useAnimLeft == 0)
			{
				changed = true; // tell others the swing ended
			}
		}
		if (worldHere)
		{
			ProcessPendingHits();
			if (++_pickupTick % 4 == 0)
			{
				PickUpItems();
			}
		}
		if (worldHere && (FightOverride ?? MultiClientManager.SessionsFight) && TryAttack())
		{
			changed = true;
		}
		_ticksSinceSend++;
		if (changed || _ticksSinceSend >= 60)
		{
			Send(PktPlayerControls());
			_ticksSinceSend = 0;
		}
	}

	/// <summary>
	/// Where to stand next to the followed player, with hysteresis so it settles instead of fidgeting: it keeps one side,
	/// starts moving only when it fell well behind, stops once it arrived, and hovers beside a player who is flying
	/// (only if it can fly itself). Returns null when it should just stay put, facing the player.
	/// </summary>
	private Vector2? FollowSpot(Player p)
	{
		_airFollow = InMultiPaste || (WorkMount >= 0 && Mount.IsaCanFly(WorkMount));
		if (_airFollow)
		{
			return AirSpot(p);
		}
		Vector2 me = Position + new Vector2(PlayerWidth / 2f, PlayerHeight / 2f);
		bool canFly = FlyingMount() >= 0 || HasWings();
		if (_followSide == 0 || Vector2.Distance(me, p.Center) > 16f * 20f)
		{
			_followSide = (me.X < p.Center.X) ? -1 : 1;
		}
		Vector2 spot = new Vector2(p.Center.X + _followSide * 46f - PlayerWidth / 2f, p.position.Y + p.height - PlayerHeight);
		float dist = Vector2.Distance(spot, Position);
		bool playerFlying = p.velocity.Y != 0f || p.mount.Active && Mount.IsaCanFly(p.mount.Type) && p.velocity.Y == 0f && !Collision.SolidCollision(p.position + new Vector2(0f, p.height), p.width, 16);
		_followHover = canFly && playerFlying;
		if (_followMoving)
		{
			if (dist < 14f && (_onGround || _followHover))
			{
				_followMoving = false;
			}
		}
		else if (dist > 110f || (_followHover && dist > 48f) || Math.Abs(spot.Y - Position.Y) > 96f)
		{
			_followMoving = true;
		}
		if (_followMoving || _followHover)
		{
			return spot;
		}
		byte face = (byte)((p.Center.X < me.X) ? 0 : 1);
		if (face != Direction)
		{
			Direction = face;
			_ticksSinceSend = 60; // send the turn
		}
		return null;
	}

	/// <summary>
	/// On a flying mount: a place in the air beside and a little above the player, its own in a small formation (by its
	/// player slot, so bots don't sit on each other). It moves only once it is well away from it and stops when it gets
	/// there, then waits still in the air, facing the player. Null: stay where it is.
	/// </summary>
	private Vector2? AirSpot(Player p)
	{
		Vector2 spot = AirPlace(p);
		float dist = Vector2.Distance(spot, Position);
		if (_followMoving)
		{
			if (dist < 6f)
			{
				_followMoving = false;
			}
		}
		else if (dist > 40f)
		{
			_followMoving = true;
		}
		if (_followMoving)
		{
			return spot;
		}
		Vector2 me = Position + new Vector2(PlayerWidth / 2f, PlayerHeight / 2f);
		byte face = (byte)((p.Center.X < me.X) ? 0 : 1);
		if (face != Direction)
		{
			Direction = face;
			_ticksSinceSend = 60; // send the turn
		}
		return null;
	}

	/// <summary>Its place in the formation beside the player.</summary>
	private Vector2 AirPlace(Player p)
	{
		int slot = MyIndex % 8;
		int side = (slot % 2 == 0) ? -1 : 1;
		int row = slot / 2;
		return p.Center + new Vector2(side * (54f + row * 34f), -40f - row * 22f) - new Vector2(PlayerWidth / 2f, PlayerHeight / 2f);
	}

	/// <summary>Straight to its place beside the player, wherever it was.</summary>
	internal void TeleportTo(Player p)
	{
		PlaceAt(AirPlace(p));
	}

	private bool TileAreaLoaded()
	{
		try
		{
			int tx = (int)(Position.X / 16f);
			int ty = (int)(Position.Y / 16f);
			return WorldGen.InWorld(tx, ty, 10) && Main.sectionManager != null && Main.sectionManager.TileLoaded(tx, ty) && Main.sectionManager.TileLoaded(tx, ty + 3);
		}
		catch
		{
			return false;
		}
	}

	private bool Glide(Vector2? target)
	{
		_controlLeft = _controlRight = _controlJump = false;
		Velocity = Vector2.Zero;
		if (!target.HasValue)
		{
			return false;
		}
		Vector2 delta = target.Value - Position;
		float dist = delta.Length();
		if (dist < 8f)
		{
			if (_followPlayer < 0)
			{
				lock (_sync)
				{
					_gotoTarget = null;
				}
			}
			return false;
		}
		float step = Math.Min(dist, 6f);
		Velocity = delta / dist * step;
		Position += Velocity;
		Direction = (byte)((delta.X < 0f) ? 0 : 1);
		_controlLeft = delta.X < 0f;
		_controlRight = delta.X > 0f;
		return true;
	}

	private bool WalkWithPhysics(Vector2? target)
	{
		Vector2 oldPos = Position;
		Vector2 oldVel = Velocity;
		float wantX = 0f;
		if (target.HasValue)
		{
			float dx = target.Value.X - Position.X;
			float dy = target.Value.Y - Position.Y;
			// Far away or hopelessly stuck: glide there instead of walking (keeps up with fast players).
			if (Math.Abs(dx) > 16f * 70f || Math.Abs(dy) > 16f * 40f || _stuckTicks > 180)
			{
				bool moved = Glide(target);
				if (Vector2.Distance(Position, target.Value) < 64f)
				{
					_stuckTicks = 0;
				}
				return moved;
			}
			int flyMount = FlyingMount();
			bool wings = HasWings();
			if ((flyMount >= 0 || wings) && (_followHover || dy < -56f || Math.Abs(dx) > 16f * 18f || (!_onGround && dy < -8f)))
			{
				return Fly(target.Value, flyMount, wings);
			}
			if (Math.Abs(dx) > 10f)
			{
				wantX = Math.Sign(dx);
			}
			else if (_followPlayer < 0 && Math.Abs(dy) < 48f)
			{
				lock (_sync)
				{
					_gotoTarget = null;
				}
			}
		}
		_controlLeft = wantX < 0f;
		_controlRight = wantX > 0f;
		_controlJump = false;
		if (wantX != 0f)
		{
			Direction = (byte)((wantX < 0f) ? 0 : 1);
			Velocity.X = MathHelper.Lerp(Velocity.X, wantX * WalkSpeed, 0.25f);
		}
		else
		{
			Velocity.X *= 0.8f;
			if (Math.Abs(Velocity.X) < 0.1f)
			{
				Velocity.X = 0f;
			}
		}
		Velocity.Y = Math.Min(Velocity.Y + Gravity, MaxFall);
		Collision.StepUp(ref Position, ref Velocity, PlayerWidth, PlayerHeight, ref _stepSpeed, ref _gfxOffY);
		Vector2 resolved = Collision.TileCollision(Position, Velocity, PlayerWidth, PlayerHeight, fallThrough: target.HasValue && target.Value.Y > Position.Y + 32f);
		bool blockedX = wantX != 0f && Math.Abs(resolved.X) < 0.01f;
		_onGround = Velocity.Y > 0f && resolved.Y == 0f;
		Position += resolved;
		Velocity = resolved;
		if (_onGround && (blockedX || (target.HasValue && target.Value.Y < Position.Y - 40f && Math.Abs(target.Value.X - Position.X) < 120f)))
		{
			Velocity.Y = -JumpSpeed;
			_controlJump = true;
		}
		if (wantX != 0f && Vector2.Distance(oldPos, Position) < 0.5f)
		{
			_stuckTicks++;
		}
		else
		{
			_stuckTicks = 0;
		}
		return Position != oldPos || Velocity != oldVel || _controlLeft || _controlRight;
	}

	/// <summary>A pixie's flight: on the Pixie mount, around the player in loose loops above them, never quite still.</summary>
	private bool Pixie(Player p)
	{
		_pixieClock += 1f / 60f;
		float t = _pixieClock;
		Vector2 around = new Vector2((float)Math.Cos(t * 1.3) * 70f + (float)Math.Sin(t * 3.1) * 18f, -64f + (float)Math.Sin(t * 1.9) * 34f);
		Player foe = Hostile ? BotSets.YourPlayerTarget : null;
		NPC prey = BotSets.YourTarget;
		Vector2? huntAt = null;
		if (foe != null && Vector2.Distance(foe.Center, p.Center) < 16f * 60f)
		{
			huntAt = foe.Center;
		}
		else if (prey != null && Vector2.Distance(prey.Center, p.Center) < 16f * 60f)
		{
			huntAt = prey.Center;
		}
		Vector2 spot = (huntAt ?? (p.Center + new Vector2(around.X * 0.25f, around.Y * 0.25f))) - new Vector2(PlayerWidth / 2f, PlayerHeight / 2f);
		MountType = MountID.Pixie;
		// It chases you like a pixie: speeds up straight at you, flies past and swings back (through anything).
		Vector2 delta = spot - Position;
		float dist = delta.Length();
		if (dist > 16f * 40f)
		{
			Velocity = Vector2.Lerp(Velocity, Vector2.Normalize(delta) * 24f, 0.5f);
		}
		else if (dist > 1f)
		{
			Velocity += Vector2.Normalize(delta) * 0.45f;
			float max = 9f;
			if (Velocity.Length() > max)
			{
				Velocity = Vector2.Normalize(Velocity) * max;
			}
		}
		Position += Velocity;
		_onGround = false;
		_controlLeft = Velocity.X < -0.5f;
		_controlRight = Velocity.X > 0.5f;
		_controlJump = false;
		if (Math.Abs(Velocity.X) > 0.6f)
		{
			Direction = (byte)((Velocity.X < 0f) ? 0 : 1);
		}
		return true;
	}

	/// <summary>Flies straight at the target (tile collision still applies): on a flying mount if it has one, else with wings.</summary>
	private bool Fly(Vector2 target, int flyMount, bool wings)
	{
		Vector2 oldPos = Position;
		if (flyMount >= 0 && flyMount != WorkMount)
		{
			MountType = flyMount;
		}
		Vector2 delta = target - Position;
		float dist = delta.Length();
		float speed = (flyMount >= 0) ? 9f : 6.5f;
		// Arrive: slow down as it gets close so it stops on the spot instead of overshooting back and forth.
		Vector2 want = delta * 0.12f;
		if (want.Length() > speed)
		{
			want = Vector2.Normalize(want) * speed;
		}
		if (dist < 6f)
		{
			want = Vector2.Zero;
		}
		Velocity = Vector2.Lerp(Velocity, want, 0.18f);
		if (Velocity.LengthSquared() < 0.01f)
		{
			Velocity = Vector2.Zero;
		}
		// On Multi Paste's broom it goes through walls to reach you (like a pixie); otherwise blocks stop it.
		Vector2 resolved = _airFollow ? Velocity : Collision.TileCollision(Position, Velocity, PlayerWidth, PlayerHeight, fallThrough: true);
		Position += resolved;
		Velocity = resolved;
		_onGround = false;
		_controlLeft = Velocity.X < -0.5f;
		_controlRight = Velocity.X > 0.5f;
		_controlJump = flyMount < 0 && wings && (Velocity.Y < -0.3f || _followHover); // wings flap while jump is held
		if (Math.Abs(Velocity.X) > 0.8f)
		{
			Direction = (byte)((Velocity.X < 0f) ? 0 : 1);
		}
		_stuckTicks = (Vector2.Distance(oldPos, Position) < 0.5f && dist > 32f) ? (_stuckTicks + 1) : 0;
		return true;
	}

	private static Item Sample(int type)
	{
		if (type <= 0)
		{
			return null;
		}
		return ContentSamples.ItemsByType.TryGetValue(type, out var item) ? item : null;
	}

	private bool HasWings()
	{
		for (int i = 3; i < 10; i++)
		{
			Item item = Sample(_slotType[PlayerItemSlotID.Armor0 + i]);
			if (item != null && item.wingSlot > 0)
			{
				return true;
			}
		}
		return false;
	}

	private int FlyingMount()
	{
		if (WorkMount >= 0 && Mount.IsaCanFly(WorkMount))
		{
			return WorkMount;
		}
		if (MountType >= 0 && Mount.IsaCanFly(MountType))
		{
			return MountType;
		}
		Item item = Sample(_slotType[PlayerItemSlotID.Misc0 + 3]);
		if (item != null && item.mountType >= 0 && Mount.IsaCanFly(item.mountType))
		{
			return item.mountType;
		}
		return -1;
	}

	/// <summary>A real weapon: deals damage and is not a tool, summon staff, sentry, ammo, block or fishing pole.</summary>
	private static bool IsWeapon(Item item)
	{
		if (item == null || item.damage <= 0 || item.useStyle <= 0)
		{
			return false;
		}
		if (item.pick > 0 || item.axe > 0 || item.hammer > 0 || item.accessory || item.ammo > 0 || item.sentry || item.createTile >= 0 || item.fishingPole > 0)
		{
			return false;
		}
		bool whip = item.shoot > 0 && item.shoot < ProjectileID.Sets.IsAWhip.Length && ProjectileID.Sets.IsAWhip[item.shoot];
		return !item.summon || whip;
	}

	/// <summary>Hotbar slot 1 if it holds a weapon, else the first weapon on the hotbar, else -1 (it does not fight).</summary>
	private int PickWeaponSlot()
	{
		if (IsWeapon(Sample(_slotType[PlayerItemSlotID.Inventory0])))
		{
			return 0;
		}
		for (int i = 1; i < 10; i++)
		{
			if (IsWeapon(Sample(_slotType[PlayerItemSlotID.Inventory0 + i])))
			{
				return i;
			}
		}
		return -1;
	}

	public string WeaponName
	{
		get
		{
			int slot = PickWeaponSlot();
			return (slot < 0) ? null : Sample(_slotType[PlayerItemSlotID.Inventory0 + slot])?.Name;
		}
	}

	/// <summary>
	/// Hits the nearest enemy in reach with its weapon (same damage roll as the item, one hit per use time), showing the swing to
	/// everyone. Melee reaches a few tiles, whips about 12, ranged and magic about 26 with a clear line of sight.
	/// </summary>
	private bool TryAttack()
	{
		if (_attackCooldown > 0)
		{
			_attackCooldown--;
			return false;
		}
		int slot = PickWeaponSlot();
		if (slot < 0)
		{
			return false;
		}
		Item weapon = Sample(_slotType[PlayerItemSlotID.Inventory0 + slot]);
		bool whip = weapon.shoot > 0 && weapon.shoot < ProjectileID.Sets.IsAWhip.Length && ProjectileID.Sets.IsAWhip[weapon.shoot];
		float range = whip ? (16f * 12f) : ((weapon.melee && !weapon.noMelee && weapon.shoot <= 0) ? (16f * 3f + weapon.width * weapon.scale) : (16f * 26f));
		Vector2 center = Position + new Vector2(PlayerWidth / 2f, PlayerHeight / 2f);
		NPC best = null;
		float bestDist = range;
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			NPC npc = Main.npc[i];
			if (npc == null || !npc.active || !npc.CanBeChasedBy())
			{
				continue;
			}
			float d = Vector2.Distance(center, npc.Center);
			if (d < bestDist && Collision.CanHitLine(Position, PlayerWidth, PlayerHeight, npc.position, npc.width, npc.height))
			{
				best = npc;
				bestDist = d;
			}
		}
		if (best == null)
		{
			return false;
		}
		int dir = (best.Center.X < center.X) ? -1 : 1;
		Direction = (byte)((dir < 0) ? 0 : 1);
		_selectedItem = slot;
		_useAnimMax = Math.Max(2, weapon.useAnimation);
		_useAnimLeft = _useAnimMax;
		Vector2 aim = best.Center - center;
		_itemRotation = (float)Math.Atan2(aim.Y * dir, aim.X * dir);
		int damage = Main.DamageVar(weapon.damage);
		bool crit = Main.rand.Next(100) < weapon.crit + 4;
		Send(PktPlayerControls());
		Send(PktItemAnimation());
		PendingHit hit = new PendingHit
		{
			Npc = best.whoAmI,
			Generation = best.generation,
			Damage = damage,
			KnockBack = weapon.knockBack,
			Direction = dir,
			Crit = crit,
			ProjectileKey = -1
		};
		if (weapon.shoot > 0)
		{
			// Show the weapon's projectile to everyone; the hit lands when it arrives (whips: halfway through the swing).
			float speed = Math.Max(4f, weapon.shootSpeed);
			Vector2 velocity = (aim.LengthSquared() > 0f) ? (Vector2.Normalize(aim) * speed) : new Vector2(dir * speed, 0f);
			_projIndex = (_projIndex >= 999) ? 900 : (_projIndex + 1);
			_projGeneration = (_projGeneration >= 0x3FFF) ? 1 : (_projGeneration + 1);
			int key = new ProjectileKey(MyIndex, _projIndex, _projGeneration);
			Send(PktProjectile(key, center - new Vector2(4f, 4f), velocity, weapon.shoot));
			hit.ProjectileKey = key;
			hit.KillProjectile = !whip;
			hit.Ticks = whip ? Math.Max(1, _useAnimMax / 2) : Utils.Clamp((int)(bestDist / speed), 1, 60);
			_pendingHits.Add(hit);
		}
		else
		{
			hit.Ticks = 0;
			_pendingHits.Add(hit);
			ProcessPendingHits();
		}
		_attackCooldown = Math.Max(10, Math.Max(weapon.useAnimation, weapon.useTime));
		_fightTarget = best.whoAmI;
		_fightShownTicks = 120;
		return true;
	}

	/// <summary>
	/// Picks up the items the server reserved for it once they are within reach, like a player would: they go into its
	/// inventory (stacking first) and the server is told, so they disappear for everyone. Items it has no room for are let go.
	/// </summary>
	private void PickUpItems()
	{
		Rectangle reach = new Rectangle((int)Position.X - 48, (int)Position.Y - 48, PlayerWidth + 96, PlayerHeight + 96);
		for (int i = 0; i < Main.maxItems; i++)
		{
			WorldItem it = Main.item[i];
			if (it == null || !it.active || it.IsAir || it.playerIndexTheItemIsReservedFor != MyIndex || it.shimmerTime > 0f)
			{
				continue;
			}
			if (_skipItems.TryGetValue(i, out var until) && Main.GameUpdateCount < until)
			{
				continue;
			}
			if (!reach.Intersects(new Rectangle((int)it.position.X, (int)it.position.Y, it.width, it.height)))
			{
				continue;
			}
			int left = Store(it.type, it.stack, it.prefix);
			if (left == it.stack)
			{
				_skipItems[i] = Main.GameUpdateCount + 600;
				Send(PktReleaseItem((short)i));
				continue;
			}
			Send(PktWorldItem(i, it.position, (left > 0) ? it.type : 0, left, (left > 0) ? it.prefix : (byte)0));
		}
	}

	/// <summary>Puts an item into the inventory like the game does (stack, then free slot) and syncs the slots. Returns what did not fit.</summary>
	private int Store(int type, int stack, byte prefix)
	{
		// Hearts, mana stars and nebula boosters are used up on touch.
		if (type == 58 || type == 1734 || type == 1867 || type == 184 || type == 1735 || type == 1868 || type == 4143 || (type < ItemID.Sets.NebulaPickup.Length && ItemID.Sets.NebulaPickup[type]))
		{
			return 0;
		}
		Item sample = Sample(type);
		int max = Math.Max(1, sample?.maxStack ?? 9999);
		for (int slot = 0; slot < 58 && stack > 0; slot++)
		{
			if (_slotType[slot] == type && _slotPrefix[slot] == prefix && _slotStack[slot] < max)
			{
				int add = Math.Min(max - _slotStack[slot], stack);
				_slotStack[slot] += add;
				stack -= add;
				Send(PktSlotRaw(slot));
			}
		}
		if (stack > 0)
		{
			bool coin = type >= 71 && type <= 74;
			int[] order = coin ? new int[] { 50, 51, 52, 53 }.Concat(Enumerable.Range(0, 50)).ToArray() : Enumerable.Range(0, 50).ToArray();
			foreach (int slot in order)
			{
				if (_slotType[slot] == 0 || _slotStack[slot] <= 0)
				{
					int add = Math.Min(max, stack);
					_slotType[slot] = type;
					_slotStack[slot] = add;
					_slotPrefix[slot] = prefix;
					stack -= add;
					Send(PktSlotRaw(slot));
					if (stack <= 0)
					{
						break;
					}
				}
			}
		}
		return stack;
	}

	private byte[] PktSlotRaw(int slot)
	{
		int type = _slotType[slot];
		int stack = _slotStack[slot];
		bool air = type <= 0 || stack <= 0;
		return Frame(5, delegate(BinaryWriter w)
		{
			w.Write((byte)MyIndex);
			w.Write((short)slot);
			w.Write((short)(air ? 0 : stack));
			w.Write(air ? (byte)0 : _slotPrefix[slot]);
			w.Write((short)(air ? 0 : type));
			w.Write((byte)0);
		});
	}

	/// <summary>Item drop update (what a client sends after picking up: the rest of the stack, or air).</summary>
	private byte[] PktWorldItem(int index, Vector2 position, int type, int stack, byte prefix)
	{
		return Frame(21, delegate(BinaryWriter w)
		{
			w.Write((short)index);
			w.Write(position.X);
			w.Write(position.Y);
			w.Write(0f);
			w.Write(0f);
			w.Write((short)stack);
			w.Write(prefix);
			w.Write((byte)0);
			w.Write((short)type);
		});
	}

	/// <summary>Gives up an item reserved for it so the server can hand it to someone else.</summary>
	private byte[] PktReleaseItem(short index)
	{
		return Frame(39, delegate(BinaryWriter w)
		{
			w.Write(index);
			w.Write(true);
		});
	}

	private void ProcessPendingHits()
	{
		for (int i = _pendingHits.Count - 1; i >= 0; i--)
		{
			PendingHit hit = _pendingHits[i];
			if (hit.Ticks > 0)
			{
				hit.Ticks--;
				_pendingHits[i] = hit;
				continue;
			}
			_pendingHits.RemoveAt(i);
			NPC npc = (hit.Npc >= 0 && hit.Npc < Main.maxNPCs) ? Main.npc[hit.Npc] : null;
			bool alive = npc != null && npc.active && npc.generation == hit.Generation;
			if (alive)
			{
				Send(PktStrikeNpc(hit.Npc, hit.Generation, hit.Damage, hit.KnockBack, hit.Direction, hit.Crit));
			}
			if (hit.ProjectileKey != -1 && hit.KillProjectile)
			{
				Vector2 at = alive ? npc.Center : (Position + new Vector2(hit.Direction * 200f, 0f));
				Send(PktKillProjectile(hit.ProjectileKey, at));
			}
		}
	}

	/// <summary>A visual copy of the weapon's projectile (no damage: the hit itself is sent as a strike).</summary>
	private byte[] PktProjectile(int key, Vector2 position, Vector2 velocity, int type)
	{
		return Frame(27, delegate(BinaryWriter w)
		{
			ProjectileKey projectileKey = (ProjectileKey)key;
			if (_legacy)
			{
				w.Write((short)projectileKey.Index); // 1.4.5.6: identity ... owner
			}
			else
			{
				w.Write(key);
			}
			w.Write(position.X);
			w.Write(position.Y);
			w.Write(velocity.X);
			w.Write(velocity.Y);
			if (_legacy)
			{
				w.Write((byte)projectileKey.Spawner);
			}
			w.Write((short)type);
			w.Write((byte)0); // no ai / damage / knockback fields
		});
	}

	private byte[] PktKillProjectile(int key, Vector2 at)
	{
		return Frame(29, delegate(BinaryWriter w)
		{
			if (_legacy)
			{
				ProjectileKey projectileKey = (ProjectileKey)key;
				w.Write((short)projectileKey.Index);
				w.Write((byte)projectileKey.Spawner);
				return;
			}
			w.Write(key);
			w.Write(at.X);
			w.Write(at.Y);
		});
	}

	private byte[] PktItemAnimation()
	{
		return Frame(41, delegate(BinaryWriter w)
		{
			w.Write((byte)MyIndex);
			w.Write(_itemRotation);
			w.Write((short)_useAnimMax);
		});
	}

	private byte[] PktStrikeNpc(int npc, byte generation, int damage, float knockBack, int hitDirection, bool crit)
	{
		return Frame(28, delegate(BinaryWriter w)
		{
			if (_legacy)
			{
				w.Write((short)npc);
			}
			else
			{
				w.Write((byte)npc);
				w.Write(generation);
			}
			w.Write((short)damage);
			w.Write(knockBack);
			w.Write((byte)(hitDirection + 1));
			w.Write((byte)(crit ? 1 : 0));
		});
	}

	private void KeepAliveTick(object _)
	{
		// The main thread normally sends movement; this only keeps a quiet session visibly alive if the game is not ticking it.
		if (Possessed || State != ClientSessionState.InGame)
		{
			return;
		}
		if (Interlocked.Increment(ref _ticksSinceSend) > 180)
		{
			_ticksSinceSend = 0;
			Send(PktPlayerControls());
		}
	}

	#endregion

	#region Packet building

	private static byte[] Frame(byte type, Action<BinaryWriter> body)
	{
		using MemoryStream ms = new MemoryStream(64);
		using BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8);
		w.Write((ushort)0);
		w.Write(type);
		body?.Invoke(w);
		w.Flush();
		long len = ms.Length;
		byte[] buffer = ms.ToArray();
		buffer[0] = (byte)(len & 0xFF);
		buffer[1] = (byte)(len >> 8);
		return buffer;
	}

	private static byte Bits(bool b0, bool b1 = false, bool b2 = false, bool b3 = false, bool b4 = false, bool b5 = false, bool b6 = false, bool b7 = false)
	{
		int v = 0;
		if (b0) v |= 1;
		if (b1) v |= 2;
		if (b2) v |= 4;
		if (b3) v |= 8;
		if (b4) v |= 16;
		if (b5) v |= 32;
		if (b6) v |= 64;
		if (b7) v |= 128;
		return (byte)v;
	}

	private byte[] PktHello()
	{
		return Frame(1, w => w.Write(VersionString));
	}

	private byte[] PktUuid()
	{
		return Frame(68, w => w.Write(Uuid));
	}

	private byte[] PktPassword(string pw)
	{
		return Frame(38, w => w.Write(pw));
	}

	private byte[] PktRequestWorld()
	{
		return Frame(6, null);
	}

	private byte[] PktSpawnTileData()
	{
		return Frame(8, delegate(BinaryWriter w)
		{
			w.Write(-1);
			w.Write(-1);
			w.Write(_team);
		});
	}

	private byte[] PktLife()
	{
		return Frame(16, delegate(BinaryWriter w)
		{
			w.Write((byte)MyIndex);
			w.Write((short)StatLife);
			w.Write((short)StatLifeMax);
		});
	}

	private byte[] PktMana()
	{
		return Frame(42, delegate(BinaryWriter w)
		{
			w.Write((byte)MyIndex);
			w.Write((short)StatMana);
			w.Write((short)StatManaMax);
		});
	}

	private byte[] PktBuffs()
	{
		return Frame(50, delegate(BinaryWriter w)
		{
			w.Write((byte)MyIndex);
			int buff = BotSets.PixieBuff;
			if (IsPixie && buff > 0 && buff < BuffID.Count)
			{
				w.Write((ushort)buff); // the pixie buff (384 by default), as long as it is a pixie
			}
			int work = WorkBuff;
			// Never two mounts' buffs at once: each one puts it on its mount every frame in everyone's game, so it would
			// get on and off without end (the smoke). As a pixie, the pixie's wins.
			bool twoMounts = IsPixie && WorkMount >= 0;
			if (work > 0 && work < BuffID.Count && !(IsPixie && work == buff) && !twoMounts)
			{
				w.Write((ushort)work); // Multi Paste's buff while it builds
			}
			w.Write((ushort)0);
		});
	}

	private byte[] PktLoadout()
	{
		return Frame(147, delegate(BinaryWriter w)
		{
			w.Write((byte)MyIndex);
			w.Write((byte)((Template != null) ? Template.CurrentLoadoutIndex : 0));
			w.Write((ushort)0);
		});
	}

	private byte[] PktAppearance()
	{
		Player t = Template;
		return Frame(4, delegate(BinaryWriter w)
		{
			w.Write((byte)MyIndex);
			if (t != null)
			{
				w.Write((byte)t.skinVariant);
				w.Write((byte)t.voiceVariant);
				w.Write(t.voicePitchOffset);
				w.Write((byte)t.hair);
				w.Write(Name);
				w.Write(t.hairDye);
				NetMessage.WriteAccessoryVisibility(w, t.hideVisibleAccessory);
				w.Write(t.hideMisc);
				WriteRgb(w, t.hairColor);
				WriteRgb(w, t.skinColor);
				WriteRgb(w, t.eyeColor);
				WriteRgb(w, t.shirtColor);
				WriteRgb(w, t.underShirtColor);
				WriteRgb(w, t.pantsColor);
				WriteRgb(w, t.shoeColor);
				// Same bits as the game (bit0 mediumcore, bit1 hardcore, bit3 journey, bit2 extra accessory slot).
				w.Write(Bits(t.difficulty == 1, t.difficulty == 2, t.extraAccessory, t.difficulty == 3));
				w.Write(Bits(t.UsingBiomeTorches, t.happyFunTorchTime, t.unlockedBiomeTorches, t.unlockedSuperCart, t.enabledSuperCart));
				w.Write(Bits(t.usedAegisCrystal, t.usedAegisFruit, t.usedArcaneCrystal, t.usedGalaxyPearl, t.usedGummyWorm, t.usedAmbrosia, t.ateArtisanBread));
				return;
			}
			w.Write((byte)0); // skin variant
			w.Write((byte)1); // voice variant
			w.Write(0f); // voice pitch offset
			w.Write((byte)0); // hair
			w.Write(Name);
			w.Write((byte)0); // hair dye
			w.Write((ushort)0); // hide visible accessory
			w.Write((byte)0); // hide misc
			foreach (Color c in _generatedColors)
			{
				WriteRgb(w, c);
			}
			w.Write((byte)0); // difficulty flags: 0 = classic
			w.Write((byte)0); // torch flags
			w.Write((byte)0); // consumable flags
		});
	}

	/// <summary>Hair, skin, eyes, shirt, undershirt, pants, shoes of a generated (non-.plr) session.</summary>
	private static readonly Color[] _generatedColors = new Color[7]
	{
		new Color(215, 90, 55),
		new Color(255, 125, 90),
		new Color(105, 90, 75),
		new Color(175, 165, 140),
		new Color(160, 180, 215),
		new Color(255, 230, 175),
		new Color(160, 105, 60)
	};

	/// <summary>A local Player that looks exactly like what this session told the server (used when the game takes it over).</summary>
	public Player CreateLocalPlayer()
	{
		Player p;
		if (Template != null)
		{
			try
			{
				p = Template.SerializedClone();
			}
			catch
			{
				p = new Player();
			}
		}
		else
		{
			p = new Player
			{
				skinVariant = 0,
				voiceVariant = 1,
				hair = 0,
				hairDye = 0,
				hairColor = _generatedColors[0],
				skinColor = _generatedColors[1],
				eyeColor = _generatedColors[2],
				shirtColor = _generatedColors[3],
				underShirtColor = _generatedColors[4],
				pantsColor = _generatedColors[5],
				shoeColor = _generatedColors[6],
				difficulty = 0,
				statManaMax = StatManaMax
			};
		}
		p.name = Name;
		p.statLifeMax = Math.Max(20, StatLifeMax);
		p.statLife = Math.Max(1, Math.Min(StatLife, p.statLifeMax));
		p.statMana = p.statManaMax;
		return p;
	}

	private static void WriteRgb(BinaryWriter w, Color c)
	{
		w.Write(c.R);
		w.Write(c.G);
		w.Write(c.B);
	}

	private void SendSlots(int start, int count)
	{
		for (int i = 0; i < count; i++)
		{
			Send(PktSlot(start + i));
		}
	}

	private byte[] PktSlot(int slotId)
	{
		Item item = null;
		if (Template != null)
		{
			try
			{
				item = new PlayerItemSlotID.SlotReference(Template, slotId).Item;
			}
			catch
			{
				item = null;
			}
		}
		bool air = item == null || item.type <= 0 || item.stack <= 0;
		return Frame(5, delegate(BinaryWriter w)
		{
			w.Write((byte)MyIndex);
			w.Write((short)slotId);
			w.Write((short)(air ? 0 : item.stack));
			w.Write(air ? (byte)0 : item.prefix);
			w.Write((short)(air ? 0 : item.type));
			w.Write((byte)((!air && item.favorited) ? 1 : 0));
		});
	}

	private byte[] PktSpawn(byte context)
	{
		return Frame(12, delegate(BinaryWriter w)
		{
			w.Write((byte)MyIndex);
			w.Write(_spawnTileX);
			w.Write(_spawnTileY);
			w.Write(0); // respawn timer
			w.Write((short)0); // deaths pve
			w.Write((short)0); // deaths pvp
			w.Write(_team);
			w.Write(context);
		});
	}

	private byte[] PktPlayerControls()
	{
		Vector2 pos;
		Vector2 vel;
		lock (_sync)
		{
			pos = Position;
			vel = Velocity;
		}
		bool hasVel = vel != Vector2.Zero;
		// Multi Paste's mount (unless it flies as a pixie).
		int mount = (WorkMount >= 0 && !IsPixie) ? WorkMount : MountType;
		bool using_ = _useAnimLeft > 0;
		return Frame(13, delegate(BinaryWriter w)
		{
			w.Write((byte)MyIndex);
			w.Write(Bits(false, false, _controlLeft, _controlRight, _controlJump, using_, Direction == 1, false));
			w.Write(Bits(false, false, hasVel, false, true /*gravDir down*/, false, false, mount >= 0));
			w.Write((byte)0);
			w.Write((byte)0);
			w.Write((byte)_selectedItem);
			w.Write(pos.X);
			w.Write(pos.Y);
			if (hasVel)
			{
				w.Write(vel.X);
				w.Write(vel.Y);
			}
			if (mount >= 0)
			{
				w.Write((ushort)mount);
			}
		});
	}

	/// <summary>Acknowledges a server teleport of this player. Until acknowledged the server ignores our position updates.</summary>
	private byte[] PktTeleportAck()
	{
		return Frame(65, delegate(BinaryWriter w)
		{
			w.Write((byte)3);
			w.Write((short)MyIndex);
			w.Write(0f);
			w.Write(0f);
			w.Write((byte)0);
		});
	}

	private byte[] PktChat(string text)
	{
		return Frame(82, delegate(BinaryWriter w)
		{
			w.Write((ushort)1); // NetModule Text
			w.Write("Say");
			w.Write(text);
		});
	}

	#endregion

	#region Packet handling

	private void HandlePacket(byte type, byte[] payload)
	{
		if (State == ClientSessionState.InGame || State == ClientSessionState.Handshaking)
		{
			Journal.Record(type, payload, 0, payload.Length);
		}
		using MemoryStream ms = new MemoryStream(payload, writable: false);
		using BinaryReader r = new BinaryReader(ms, Encoding.UTF8);
		switch (type)
		{
		case 2: // Kick
		{
			string reason = ReadNetworkText(r);
			WasKicked = true;
			Disconnect("kicked: " + reason);
			break;
		}
		case 3: // PlayerInfo (slot assignment)
			HandleSlot(r);
			break;
		case 7: // WorldData
			HandleWorldData(r);
			break;
		case 37: // RequestPassword
			if (string.IsNullOrEmpty(Password))
			{
				WasKicked = true;
				Disconnect("server requires a password");
			}
			else
			{
				Send(PktPassword(Password));
			}
			break;
		case 49: // InitialSpawn
			DoInitialSpawn();
			break;
		case 16: // PlayerLifeMana (server may correct SSC)
		{
			int idx = r.ReadByte();
			if (idx == MyIndex)
			{
				StatLife = r.ReadInt16();
				StatLifeMax = Math.Max(20, (int)r.ReadInt16());
			}
			break;
		}
		case 65: // Teleport
			HandleTeleport(r);
			break;
		case 5: // SyncEquipment: the server (SSC) telling us what we carry
		{
			int idx = r.ReadByte();
			int slot = r.ReadInt16();
			int stack = r.ReadInt16();
			byte prefix = r.ReadByte();
			int itemType = r.ReadInt16();
			if (idx == MyIndex && slot >= 0 && slot < _slotType.Length)
			{
				_slotKnown[slot] = true;
				_slotType[slot] = (stack > 0) ? itemType : 0;
				_slotStack[slot] = (stack > 0) ? stack : 0;
				_slotPrefix[slot] = (stack > 0) ? prefix : (byte)0;
			}
			break;
		}
		case 55: // AddPlayerBuff: TShock disables a player with Webbed (149) for that long
		{
			int idx = r.ReadByte();
			int buff = r.ReadUInt16();
			int time = r.ReadInt32();
			if (idx == MyIndex && buff == 149)
			{
				_disabledUntil = DateTime.UtcNow.AddSeconds(Math.Max(0, time) / 60.0);
			}
			break;
		}
		case 50: // PlayerBuffs: the server set its buffs (its saved character on login): ours go again
		{
			int idx = r.ReadByte();
			if (idx == MyIndex && (WorkBuff > 0 || IsPixie))
			{
				_buffsAgainAt = DateTime.UtcNow.AddSeconds(0.5);
			}
			break;
		}
		case 39: // The server wants this item for a closer player: let it go (a real client does the same).
		{
			short item = r.ReadInt16();
			Send(PktReleaseItem(item));
			break;
		}
		case 82: // NetModules
			HandleNetModule(r);
			break;
		case 107: // SmartTextMessage
		{
			Color color = r.ReadRGB();
			string text = ReadChatText(r);
			RaiseChat(text, null, color);
			ForwardToAi(text, 255, null);
			break;
		}
		}
	}

	private void HandleTeleport(BinaryReader r)
	{
		byte flags = r.ReadByte();
		int kind = flags & 3;
		int who = r.ReadInt16();
		Vector2 to = new Vector2(r.ReadSingle(), r.ReadSingle());
		bool keepPosition = (flags & 4) != 0;
		if (kind != 0 || who != MyIndex)
		{
			return;
		}
		if (!keepPosition)
		{
			lock (_sync)
			{
				Position = to;
				Velocity = Vector2.Zero;
			}
		}
		Send(PktTeleportAck());
		Send(PktPlayerControls());
	}

	private void HandleSlot(BinaryReader r)
	{
		MyIndex = r.ReadByte();
		r.ReadBoolean(); // server cheat checks
		StatusText = "sending player data (slot " + MyIndex + ")";
		Notify();
		if (Template != null)
		{
			CopySlotsFrom(Template);
			StatLife = Math.Max(1, Template.statLifeMax);
			StatLifeMax = Math.Max(20, Template.statLifeMax);
			StatMana = Template.statManaMax;
			StatManaMax = Template.statManaMax;
		}
		Send(PktAppearance());
		Send(PktUuid());
		Send(PktLife());
		Send(PktMana());
		Send(PktBuffs());
		Send(PktLoadout());
		// Every equipment bank at its PlayerItemSlotID start (banks hold 40 items each), matching the verified handshake so
		// TShock fully registers the join. With a character file these carry its real items.
		SendSlots(PlayerItemSlotID.Inventory0, 59);
		SendSlots(PlayerItemSlotID.Armor0, 20);
		SendSlots(PlayerItemSlotID.Dye0, 10);
		SendSlots(PlayerItemSlotID.Misc0, 5);
		SendSlots(PlayerItemSlotID.MiscDye0, 5);
		SendSlots(PlayerItemSlotID.Bank1_0, 40);
		SendSlots(PlayerItemSlotID.Bank2_0, 40);
		SendSlots(PlayerItemSlotID.TrashItem, 1);
		SendSlots(PlayerItemSlotID.Bank3_0, 40);
		SendSlots(PlayerItemSlotID.Bank4_0, 40);
		SendSlots(PlayerItemSlotID.Loadout1_Armor_0, 20);
		SendSlots(PlayerItemSlotID.Loadout1_Dye_0, 10);
		SendSlots(PlayerItemSlotID.Loadout2_Armor_0, 20);
		SendSlots(PlayerItemSlotID.Loadout2_Dye_0, 10);
		SendSlots(PlayerItemSlotID.Loadout3_Armor_0, 20);
		SendSlots(PlayerItemSlotID.Loadout3_Dye_0, 10);
		if (!_worldRequested)
		{
			_worldRequested = true;
			Send(PktRequestWorld());
			StatusText = "requesting world";
			Notify();
		}
	}

	private void HandleWorldData(BinaryReader r)
	{
		try
		{
			r.ReadInt32(); // time
			r.ReadByte(); // day/moon flags
			r.ReadByte(); // moon phase
			r.ReadInt16(); // maxTilesX
			r.ReadInt16(); // maxTilesY
			_spawnTileX = r.ReadInt16();
			_spawnTileY = r.ReadInt16();
		}
		catch
		{
		}
		if (State == ClientSessionState.Handshaking)
		{
			StatusText = "requesting tiles";
			Notify();
			Send(PktSpawnTileData());
		}
	}

	private void DoInitialSpawn()
	{
		if (Spawned)
		{
			return;
		}
		SpawnPosition = new Vector2(_spawnTileX * 16 + 8 - PlayerWidth / 2f, _spawnTileY * 16 - PlayerHeight);
		lock (_sync)
		{
			Position = SpawnPosition;
			Velocity = Vector2.Zero;
		}
		Send(PktSpawn(1)); // SpawningIntoWorld
		Send(PktPlayerControls());
		Spawned = true;
		ReconnectTries = 0;
		State = ClientSessionState.InGame;
		StatusText = "in game";
		Notify();
	}

	private void HandleNetModule(BinaryReader r)
	{
		ushort module = r.ReadUInt16();
		if (module != 1) // Text
		{
			return;
		}
		byte author = r.ReadByte();
		string text = ReadChatText(r);
		Color color = Color.White;
		try
		{
			color = r.ReadRGB();
		}
		catch
		{
		}
		string name = (author < 255) ? PlayerName(author) : null;
		RaiseChat(text, (author < 255) ? (name ?? ("#" + author)) : null, color);
		ForwardToAi(text, author, name);
	}

	/// <summary>Name of a player on this session's server, from the player info packets it received.</summary>
	public string PlayerName(int index)
	{
		if (index == MyIndex)
		{
			return Name;
		}
		foreach (byte[] frame in Journal.PlayerState(index))
		{
			if (frame[2] != 4 || frame.Length < 12)
			{
				continue;
			}
			try
			{
				using MemoryStream ms = new MemoryStream(frame, 3, frame.Length - 3, writable: false);
				using BinaryReader r = new BinaryReader(ms, Encoding.UTF8);
				r.ReadByte(); // player
				r.ReadByte(); // skin variant
				r.ReadByte(); // voice variant
				r.ReadSingle(); // voice pitch
				r.ReadByte(); // hair
				return r.ReadString();
			}
			catch
			{
				return null;
			}
		}
		return null;
	}

	private void ForwardToAi(string text, int author, string authorName)
	{
		if (!AiEnabled || Possessed || State != ClientSessionState.InGame || !AiAssistant.HasKey)
		{
			return;
		}
		AiAssistant.OnSessionChat(Ai, Name, author == MyIndex, (author < 255) ? authorName : null, text, MultiClientManager.OwnNames(), MultiClientManager.GameInfoFor(this), () => AiEnabled && !Possessed && State == ClientSessionState.InGame, Say);
	}

	private void RaiseChat(string text, string sender, Color color)
	{
		OnChat?.Invoke(this, text, sender, color);
	}

	/// <summary>The chat text as the game would show it (localized keys and substitutions resolved, tags untouched).</summary>
	private static string ReadChatText(BinaryReader r)
	{
		try
		{
			return Terraria.Localization.NetworkText.Deserialize(r).ToString();
		}
		catch
		{
			return "";
		}
	}

	private static string ReadNetworkText(BinaryReader r)
	{
		try
		{
			byte mode = r.ReadByte();
			string text = r.ReadString();
			if (mode != 0)
			{
				int count = r.ReadByte();
				for (int i = 0; i < count; i++)
				{
					ReadNetworkText(r);
				}
			}
			return text;
		}
		catch
		{
			return "";
		}
	}

	private static string StripTags(string text)
	{
		if (string.IsNullOrEmpty(text) || text.IndexOf('[') < 0)
		{
			return text;
		}
		return System.Text.RegularExpressions.Regex.Replace(text, "\\[[a-zA-Z]{1,10}(/[^:\\]]+)?:([^\\]]*)\\]", "$2");
	}

	#endregion
}
