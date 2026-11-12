using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.Map;
using Terraria.Net.Sockets;
using Terraria.UI.Chat;
using IsaClient.Core;
using IsaClient.Assistant;
using Terraria;

namespace IsaClient.MultiClient;

/// <summary>
/// Lets the player "become" one of their MultiClient sessions without reconnecting anything. Every connection stays open;
/// the game simply swaps which socket it drives:
///  - the target session hands its live socket to the game (its headless threads stop),
///  - the game's previous socket goes to a headless session that keeps that character online,
///  - same world: only what the target received while headless is replayed, instantly, no transition;
///  - another server: the game world is rebuilt from the target's <see cref="WorldJournal"/> under a full-screen fade,
///  - Main.myPlayer moves to the target's slot. The real character is kept aside untouched (it is what gets saved).
/// </summary>
public static class Possession
{
	/// <summary>Taken by Netplay's client loop around starting a read, so a swap never races a read being started.</summary>
	public static readonly object ReadGate = new object();

	public static volatile bool HoldReads;

	/// <summary>The player's own connection, kept alive headless while they play as another session. Null when not possessing.</summary>
	public static ClientSession MainSession { get; private set; }

	/// <summary>The session the game is currently driving, or null when the player is themselves.</summary>
	public static ClientSession Current { get; private set; }

	public static bool Active => Current != null;

	public static string OriginalName => _originalPlayer?.name ?? Main.LocalPlayer.name;

	/// <summary>Bumped whenever the game world is rebuilt for another server; journal cursors from older epochs are void.</summary>
	public static int WorldEpoch;

	/// <summary>Journal of the player's own connection (recorded from the join on), handed to <see cref="MainSession"/> when switching.</summary>
	private static WorldJournal _ownJournal;

	private static bool _replaying;

	private static Player _originalPlayer;

	private static int _originalIndex = -1;

	private enum Phase
	{
		None,
		FadeOut,
		Hold,
		FadeIn
	}

	private const int FadeOutTicks = 18;

	private const int HoldTicks = 16;

	private const int FadeInTicks = 30;

	private static Phase _phase;

	private static int _timer;

	private static ClientSession _target;

	private static string _title = "";

	private static string _subtitle = "";

	private static volatile string _lostReason;

	private static volatile bool _connectionLost;

	private static float _alpha;

	public static bool InTransition => _phase != Phase.None;

	/// <summary>Record the player's own world (needed to come back after visiting another server).</summary>
	public static bool RecordOwnWorld = true;

	private static readonly Color _sessionColor = new Color(120, 220, 160);

	/// <summary>The journal the game's live connection writes into right now.</summary>
	private static WorldJournal GameJournal => (Current != null) ? Current.Journal : _ownJournal;

	#region Hooks from the game

	/// <summary>Netplay is starting a fresh connection of the player's own: start a fresh journal for it.</summary>
	public static void OnConnectionStarting()
	{
		_ownJournal = RecordOwnWorld ? new WorldJournal(64L * 1024 * 1024) : null;
		WorldEpoch++;
	}

	/// <summary>Every packet the game receives (client side), before it is applied.</summary>
	public static void OnGamePacket(byte type, byte[] buffer, int start, int length)
	{
		if (_replaying || length < 1 || !WorldJournal.IsInteresting(type))
		{
			return;
		}
		GameJournal?.Record(type, buffer, start + 1, length - 1);
	}

	#endregion

	#region Starting a switch

	/// <summary>Starts the swap to <paramref name="target"/> (null = back to your own character).</summary>
	public static bool Begin(ClientSession target, out string error)
	{
		error = null;
		if (Main.netMode != 1 || Main.gameMenu)
		{
			error = "Join a server first.";
			return false;
		}
		if (_phase != Phase.None || HoldReads)
		{
			error = "Already switching.";
			return false;
		}
		if (target == null || target == MainSession)
		{
			if (!Active)
			{
				error = "You are already playing as yourself.";
				return false;
			}
			if (MainSession == null || !MainSession.IsConnected || MainSession.State != ClientSessionState.InGame)
			{
				error = "Your own connection was lost. Leave the world to go back to the menu.";
				return false;
			}
			target = MainSession;
		}
		else
		{
			if (target == Current)
			{
				error = "You are already playing as " + target.Name + ".";
				return false;
			}
			if (target.State != ClientSessionState.InGame || !target.Spawned || !target.IsConnected)
			{
				error = target.Name + " is not in game (" + target.StatusText + ").";
				return false;
			}
		}
		if (!(Netplay.Connection.Socket is TcpSocket))
		{
			error = "Only direct IP connections can switch sessions (not Steam friend joins).";
			return false;
		}
		if (Main.mouseItem != null && !Main.mouseItem.IsAir)
		{
			error = "Put down the item on your cursor first.";
			return false;
		}
		bool sameWorld = IsOnMyServer(target);
		if (!sameWorld)
		{
			WorldJournal journal = target.Journal;
			if (journal == null || journal.WorldInfo() == null)
			{
				error = (target == MainSession) ? "Your own world was not recorded (turn on \"Remember my world\" in Bots > Options and rejoin)." : (target.Name + "'s world is not known yet; wait a moment.");
				return false;
			}
		}
		_target = target;
		_title = (target == MainSession) ? OriginalName : target.Name;
		if (sameWorld)
		{
			// Same world: swap right away, no transition.
			_target = null;
			if (Swap(target, currentIsDead: false))
			{
				SoundEngine.PlaySound(12);
			}
			return true;
		}
		_subtitle = (target == MainSession) ? ("back to yourself  -  " + target.Host + ":" + target.Port) : ("now playing as  -  " + target.Host + ":" + target.Port);
		_phase = Phase.FadeOut;
		_timer = 0;
		SoundEngine.PlaySound(10);
		return true;
	}

	/// <summary>True if the session is on the server whose world the game has loaded right now.</summary>
	public static bool IsOnMyServer(ClientSession session)
	{
		if (session == null || !MultiClientManager.TryGetCurrentServer(out var host, out var port))
		{
			return false;
		}
		return port == session.Port && SameHost(host, session.Host);
	}

	private static readonly Dictionary<string, IPAddress[]> _dnsCache = new Dictionary<string, IPAddress[]>(StringComparer.OrdinalIgnoreCase);

	public static bool SameHost(string a, string b)
	{
		if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		IPAddress[] aa = Resolve(a);
		IPAddress[] bb = Resolve(b);
		return aa.Any((IPAddress x) => bb.Contains(x));
	}

	private static IPAddress[] Resolve(string host)
	{
		if (host == null)
		{
			return new IPAddress[0];
		}
		lock (_dnsCache)
		{
			if (_dnsCache.TryGetValue(host, out var cached))
			{
				return cached;
			}
		}
		IPAddress[] result;
		try
		{
			result = IPAddress.TryParse(host, out var ip) ? new IPAddress[1] { ip } : Dns.GetHostAddresses(host);
		}
		catch
		{
			result = new IPAddress[0];
		}
		lock (_dnsCache)
		{
			_dnsCache[host] = result;
		}
		return result;
	}

	#endregion

	#region Per-tick update (main thread, right before the game parses incoming packets)

	/// <summary>Kick packet for the session we are playing as: come back to the real character instead of leaving the world.</summary>
	public static bool InterceptKick(string reason)
	{
		if (!Active || MainSession == null || !MainSession.IsConnected)
		{
			return false;
		}
		_lostReason = "was kicked: " + reason;
		_connectionLost = true;
		return true;
	}

	/// <summary>The game noticed its socket closed. While playing as a session, return to the real character instead.</summary>
	public static bool CatchLostConnection()
	{
		if (!Active || MainSession == null || !MainSession.IsConnected)
		{
			return false;
		}
		if (_lostReason == null)
		{
			_lostReason = "lost its connection";
		}
		_connectionLost = true;
		return true;
	}

	private static int _landingGuard;

	private static int _groundedTicks;

	/// <summary>After a switch: no fall damage until the new local player has stood on the ground for a moment.</summary>
	private static void UpdateLandingGuard()
	{
		if (_landingGuard <= 0 || Main.gameMenu || HoldReads)
		{
			return;
		}
		Player me = Main.LocalPlayer;
		me.fallStart = me.fallStart2 = (int)(me.position.Y / 16f);
		_groundedTicks = (me.velocity.Y == 0f) ? (_groundedTicks + 1) : 0;
		_landingGuard--;
		if (_groundedTicks >= 20)
		{
			_landingGuard = 0;
		}
	}

	public static void UpdateMainThread()
	{
		try
		{
			UpdateLandingGuard();
			if (_connectionLost && Active)
			{
				_connectionLost = false;
				string name = Current.Name;
				string reason = _lostReason ?? "lost its connection";
				_lostReason = null;
				_phase = Phase.None;
				bool sameWorld = IsOnMyServer(MainSession);
				if (MainSession != null && Swap(MainSession, currentIsDead: true))
				{
					if (!sameWorld)
					{
						_title = OriginalName;
						_subtitle = name + " " + reason;
						_phase = Phase.Hold;
						_timer = 0;
						_alpha = 1f;
					}
					MultiClientManager.Announce(name + " " + reason + ". You are yourself again.");
				}
				return;
			}
			if (!Active && _connectionLost)
			{
				_connectionLost = false;
				_lostReason = null;
			}
			switch (_phase)
			{
			case Phase.FadeOut:
				_timer++;
				_alpha = Ease(_timer / (float)FadeOutTicks);
				if (_timer >= FadeOutTicks)
				{
					_alpha = 1f;
					ClientSession target = _target;
					_target = null;
					if (!Swap(target, currentIsDead: false))
					{
						_subtitle = "could not switch";
					}
					else
					{
						SoundEngine.PlaySound(2, -1, -1, 6, 0.6f);
					}
					_phase = Phase.Hold;
					_timer = 0;
				}
				break;
			case Phase.Hold:
				_alpha = 1f;
				if (++_timer >= HoldTicks)
				{
					_phase = Phase.FadeIn;
					_timer = 0;
				}
				break;
			case Phase.FadeIn:
				_timer++;
				_alpha = 1f - Ease(_timer / (float)FadeInTicks);
				if (_timer >= FadeInTicks)
				{
					_alpha = 0f;
					_phase = Phase.None;
				}
				break;
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			HoldReads = false;
			_replaying = false;
			_phase = Phase.None;
			_alpha = 0f;
		}
	}

	private static float Ease(float t)
	{
		t = MathHelper.Clamp(t, 0f, 1f);
		return t * t * (3f - 2f * t);
	}

	#endregion

	#region The swap

	/// <summary>Stops the game reading and waits for a read in flight. Returns false if it would not finish.</summary>
	private static bool PauseGameReads()
	{
		lock (ReadGate)
		{
			HoldReads = true;
		}
		Stopwatch wait = Stopwatch.StartNew();
		while (Netplay.Connection.IsReading && wait.ElapsedMilliseconds < 2000)
		{
			System.Threading.Thread.Sleep(1);
		}
		return !Netplay.Connection.IsReading;
	}

	/// <summary>Processes every complete packet the game already has and returns the unfinished tail (bytes of the next packet).</summary>
	private static byte[] FlushGameBuffer()
	{
		MessageBuffer mb = NetMessage.buffer[256];
		if (mb.checkBytes)
		{
			NetMessage.CheckBytes();
		}
		lock (mb)
		{
			byte[] tail = new byte[mb.totalData];
			Buffer.BlockCopy(mb.readBuffer, 0, tail, 0, mb.totalData);
			mb.totalData = 0;
			mb.checkBytes = false;
			return tail;
		}
	}

	private static bool Swap(ClientSession target, bool currentIsDead)
	{
		RemoteServer conn = Netplay.Connection;
		TcpSocket oldSocket = conn.Socket as TcpSocket;
		if (oldSocket == null || target == null)
		{
			return false;
		}
		bool sameWorld = IsOnMyServer(target);
		int oldIndex = Main.myPlayer;
		Player oldPlayer = Main.player[oldIndex];
		if (!currentIsDead)
		{
			CloseLocalUi(oldPlayer);
		}
		try
		{
			if (!PauseGameReads())
			{
				MultiClientManager.AnnounceError("The game was busy reading from the server; try again.");
				return false;
			}
			// 1. Take the target's socket first: if that fails nothing has changed yet.
			TcpClient newTcp = target.DetachForGame(out var newLeftover);
			if (newTcp == null)
			{
				MultiClientManager.AnnounceError(target.Name + " lost its connection.");
				return false;
			}
			// 2. Let the game finish everything already received on its socket and keep the unfinished tail.
			byte[] oldLeftover = FlushGameBuffer();
			// 3. The socket the game was using goes to a headless session that keeps that character online.
			bool firstSwitch = Current == null;
			ClientSession leaving = Current;
			if (firstSwitch)
			{
				MultiClientManager.TryGetCurrentServer(out var host, out var port);
				WorldJournal own = _ownJournal ?? new WorldJournal(64L * 1024 * 1024);
				_ownJournal = null;
				MainSession = ClientSession.AdoptMain(oldPlayer.name, Main.clientUUID, host, port, oldIndex, own);
				MainSession.OnChat += MultiClientManager.SessionChatReceived;
				MainSession.OnStateChanged += MultiClientManager.SessionStateChanged;
				leaving = MainSession;
			}
			// The AI switch belongs to the character: the one you leave keeps answering, the one you take brings its own.
			leaving.AiEnabled = AiAssistant.Enabled;
			// Everything that connection received so far is in the game world right now.
			leaving.Journal.Cursor = leaving.Journal.OrderedCount;
			leaving.Journal.CursorEpoch = WorldEpoch;
			TcpClient oldTcp = oldSocket.IsaGetClient();
			if (currentIsDead || oldTcp == null || !oldTcp.Connected)
			{
				leaving.MarkGone("disconnected");
				try
				{
					oldTcp?.Close();
				}
				catch
				{
				}
				MultiClientManager.Forget(leaving);
			}
			else
			{
				leaving.AttachHeadless(oldTcp, oldLeftover, oldPlayer);
			}
			// 4. The game now talks through the target's socket.
			conn.Socket = new TcpSocket(newTcp);
			conn.PendingTermination = false;
			conn.IsReading = false;
			conn.TimeOutTimer = 0;
			if (firstSwitch)
			{
				_originalPlayer = oldPlayer;
				_originalIndex = oldIndex;
			}
			// 5. Bring the game world and players in line with the target.
			_replaying = true;
			try
			{
				if (sameWorld)
				{
					ApplySameWorld(target, firstSwitch, oldPlayer, oldIndex);
				}
				else
				{
					RebuildWorld(target);
				}
			}
			finally
			{
				_replaying = false;
			}
			if (newLeftover != null && newLeftover.Length > 0)
			{
				NetMessage.ReceiveBytes(newLeftover, newLeftover.Length);
			}
			Player me = Main.player[Main.myPlayer];
			me.clientClone(Main.clientPlayer);
			Main.screenPosition = me.Center - new Vector2(Main.screenWidth, Main.screenHeight) / 2f;
			Netplay.ServerIPText = target.Host;
			Netplay.ListenPort = target.Port;
			AiAssistant.Enabled = target.AiEnabled;
			// 6. Bookkeeping.
			if (target == MainSession)
			{
				_ownJournal = MainSession.Journal;
				MainSession = null;
				Current = null;
				_originalPlayer = null;
				_originalIndex = -1;
			}
			else
			{
				Current = target;
			}
			return true;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			MultiClientManager.AnnounceError("Switch failed: " + ex.Message);
			return false;
		}
		finally
		{
			HoldReads = false;
		}
	}

	private static void ApplySameWorld(ClientSession target, bool firstSwitch, Player oldPlayer, int oldIndex)
	{
		if (firstSwitch)
		{
			Main.player[oldIndex] = MakeRemoteCopy(oldPlayer, oldIndex);
		}
		WorldJournal journal = target.Journal;
		int from = (journal.CursorEpoch == WorldEpoch) ? journal.Cursor : 0;
		foreach (byte[] frame in journal.OrderedFrom(from))
		{
			Feed(frame);
		}
		int newIndex = target.MyIndex;
		if (target == MainSession)
		{
			Main.player[newIndex] = _originalPlayer;
		}
		else if ((target.Template != null && !Main.ServerSideCharacter) || Main.player[newIndex] == null || !Main.player[newIndex].active)
		{
			Main.player[newIndex] = target.CreateLocalPlayer();
		}
		Player me = Main.player[newIndex];
		int mount = MountFor(me, target);
		PlaceLocalPlayer(me, target, newIndex);
		ReplayOwnState(journal, newIndex);
		ApplyServerLook(target, newIndex);
		KeepMount(me, mount);
	}

	/// <summary>Replaces the loaded world with the target's server: clear, then replay its whole journal.</summary>
	private static void RebuildWorld(ClientSession target)
	{
		WorldJournal journal = target.Journal;
		byte[] info = journal.WorldInfo();
		try
		{
			MapHelper.SaveMap();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		for (int i = 0; i < 255; i++)
		{
			Main.player[i] = new Player
			{
				whoAmI = i
			};
		}
		// World size comes from packet 7: time(int) flags(byte) moon(byte) maxTilesX(short) maxTilesY(short).
		Main.maxTilesX = BitConverter.ToInt16(info, 3 + 6);
		Main.maxTilesY = BitConverter.ToInt16(info, 3 + 8);
		WorldGen.setWorldSize();
		WorldGen.clearWorld();
		WorldEpoch++;
		Feed(info);
		foreach (byte[] frame in journal.WorldState())
		{
			if (frame != info)
			{
				Feed(frame);
			}
		}
		foreach (byte[] frame in journal.OrderedFrom(0))
		{
			Feed(frame);
		}
		int newIndex = target.MyIndex;
		foreach (byte[] frame in journal.PlayerState())
		{
			if (frame.Length > 3 && frame[3] != newIndex)
			{
				Feed(frame);
			}
		}
		Player me;
		if (target == MainSession)
		{
			me = _originalPlayer;
		}
		else
		{
			me = target.CreateLocalPlayer();
		}
		Main.player[newIndex] = me;
		int mount = MountFor(me, target);
		PlaceLocalPlayer(me, target, newIndex);
		ReplayOwnState(journal, newIndex);
		ApplyServerLook(target, newIndex);
		KeepMount(me, mount);
		try
		{
			Main.Map.Load();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		if (journal.Overflowed)
		{
			MultiClientManager.Announce("Part of " + target.Host + "'s world was too big to keep; far areas may look empty until you rejoin.");
		}
	}

	private static void PlaceLocalPlayer(Player me, ClientSession target, int index)
	{
		me.whoAmI = index;
		me.active = true;
		if (target != MainSession)
		{
			me.name = target.Name;
			me.position = target.Position;
			me.velocity = Vector2.Zero;
			me.dead = false;
			me.ghost = false;
			me.respawnTimer = 0;
			me.statLife = Math.Max(1, (me.statLife > 0) ? me.statLife : target.StatLife);
		}
		// A fresh Player counts its fall from fallStart = 0 (the top of the world), so landing would kill it. Start the fall
		// here and keep doing so until it has landed (a headless session on another server may have been hovering).
		me.fallStart = me.fallStart2 = (int)(me.position.Y / 16f);
		me.immune = true;
		me.immuneTime = Math.Max(me.immuneTime, 90);
		_landingGuard = 600;
		_groundedTicks = 0;
		Main.myPlayer = index;
	}

	/// <summary>Mount to keep after the switch: your own character's current one, or the one the session was sitting on.</summary>
	private static int MountFor(Player me, ClientSession target)
	{
		if (target == MainSession)
		{
			return me.mount.Active ? me.mount.Type : -1;
		}
		return target.MountType;
	}

	/// <summary>
	/// Puts the player back on the mount. A mount needs its buff (Mount.CheckMountBuff dismounts without it), and replaying the
	/// server's buff list for our slot can drop that buff, so it is added again here.
	/// </summary>
	private static void KeepMount(Player me, int mountType)
	{
		if (mountType < 0)
		{
			return;
		}
		try
		{
			if (!me.mount.Active || me.mount.Type != mountType)
			{
				me.mount.SetMount(mountType, me);
			}
			if (me.mount.Active && me.FindBuffIndex(me.mount.BuffType) == -1)
			{
				me.AddBuff(me.mount.BuffType, 3600);
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	/// <summary>
	/// Makes the local player look the way the server has it (on SSC servers that is the account's character, not the .plr the
	/// session was created from). The server usually tells everyone but the player itself, so the look is searched in every
	/// journal of that server: the session's own, your character's and the other sessions'.
	/// </summary>
	private static void ApplyServerLook(ClientSession target, int index)
	{
		if (!Main.ServerSideCharacter || target == MainSession)
		{
			return;
		}
		byte[] look = FindLook(target.Journal, index);
		if (look == null)
		{
			List<ClientSession> others = MultiClientManager.All;
			if (MainSession != null)
			{
				others.Add(MainSession);
			}
			foreach (ClientSession other in others)
			{
				if (other != target && other.Port == target.Port && SameHost(other.Host, target.Host))
				{
					look = FindLook(other.Journal, index);
					if (look != null)
					{
						break;
					}
				}
			}
		}
		if (look == null && _ownJournal != null)
		{
			look = FindLook(_ownJournal, index);
		}
		if (look != null)
		{
			Feed(look); // packet 4 for our own slot is applied by the game on SSC servers
		}
	}

	private static byte[] FindLook(WorldJournal journal, int index)
	{
		if (journal == null)
		{
			return null;
		}
		foreach (byte[] frame in journal.PlayerState(index))
		{
			if (frame[2] == 4)
			{
				return frame;
			}
		}
		return null;
	}

	/// <summary>The server's view of our own inventory/life/mana/buffs (SSC servers send these).</summary>
	private static void ReplayOwnState(WorldJournal journal, int index)
	{
		foreach (byte[] frame in journal.PlayerState(index))
		{
			byte type = frame[2];
			if (type == 5 || type == 16 || type == 42 || type == 50 || type == 147)
			{
				Feed(frame);
			}
		}
	}

	/// <summary>Runs a raw frame through the game's normal packet handling.</summary>
	private static void Feed(byte[] frame)
	{
		try
		{
			NetMessage.ReceiveBytes(frame, frame.Length);
			NetMessage.CheckBytes();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static Player MakeRemoteCopy(Player p, int index)
	{
		Player copy;
		try
		{
			copy = p.SerializedClone();
		}
		catch
		{
			copy = new Player
			{
				name = p.name
			};
			copy.CopyVisuals(p);
		}
		copy.whoAmI = index;
		copy.active = true;
		copy.position = p.position;
		copy.velocity = Vector2.Zero;
		copy.direction = p.direction;
		copy.statLife = p.statLife;
		copy.statLifeMax = p.statLifeMax;
		copy.statLifeMax2 = p.statLifeMax2;
		copy.team = p.team;
		copy.hostile = p.hostile;
		copy.dead = p.dead;
		copy.zone5 = p.zone5;
		if (p.mount.Active)
		{
			try
			{
				copy.mount.SetMount(p.mount.Type, copy);
			}
			catch
			{
			}
		}
		return copy;
	}

	private static void CloseLocalUi(Player p)
	{
		try
		{
			if (p.chest != -1)
			{
				p.chest = -1;
				NetMessage.SendData(33, -1, -1, null, -1);
				Recipe.UpdateRecipeList();
			}
			if (p.talkNPC != -1 || p.sign != -1)
			{
				Main.CloseNPCChatOrSign(quiet: true);
			}
			Main.playerInventory = false;
			Main.editSign = false;
			Main.editChest = false;
			Main.InGuideCraftMenu = false;
			Main.InReforgeMenu = false;
			p.controlUseItem = false;
			p.releaseUseItem = true;
		}
		catch
		{
		}
	}

	#endregion

	/// <summary>
	/// The game is leaving the world. If you were playing as a session, its socket goes back to it (so it stays online) and the
	/// game closes your own connection instead. Then the real character is put back in place.
	/// </summary>
	public static void OnGameDisconnected()
	{
		try
		{
			ClientSession current = Current;
			ClientSession main = MainSession;
			if (current != null && main != null && !main.Possessed && main.State == ClientSessionState.InGame && Netplay.Connection?.Socket is TcpSocket gameSocket && PauseGameReads())
			{
				TcpClient gameTcp = gameSocket.IsaGetClient();
				TcpClient mainTcp = (gameTcp != null && gameTcp.Connected) ? main.DetachForGame(out _) : null;
				if (mainTcp != null)
				{
					byte[] tail = FlushGameBuffer();
					Player p = Main.player[Main.myPlayer];
					current.Journal.Cursor = current.Journal.OrderedCount;
					current.Journal.CursorEpoch = -1;
					current.AiEnabled = AiAssistant.Enabled;
				current.AttachHeadless(gameTcp, tail, p);
					Netplay.Connection.Socket = new TcpSocket(mainTcp);
					current = null; // it stays online on its own
				}
			}
			if (_originalPlayer != null && _originalIndex >= 0)
			{
				Main.player[_originalIndex] = _originalPlayer;
				Main.myPlayer = _originalIndex;
			}
			if (main != null)
			{
				Netplay.ServerIPText = main.Host;
				Netplay.ListenPort = main.Port;
			}
			_originalPlayer = null;
			_originalIndex = -1;
			Current = null;
			if (current != null)
			{
				current.MarkGone("left with you");
				MultiClientManager.Forget(current);
			}
			MainSession = null;
			if (main != null && !main.Possessed)
			{
				main.Disconnect("left");
			}
			_ownJournal = null;
			_phase = Phase.None;
			_alpha = 0f;
			_connectionLost = false;
			_lostReason = null;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		finally
		{
			HoldReads = false;
		}
	}

	#region Drawing

	/// <summary>Full-screen fade (only when changing servers) and a small "playing as" tag while possessing.</summary>
	public static void Draw(SpriteBatch spriteBatch)
	{
		if (Main.gameMenu || (_alpha <= 0f && (!Active || Main.hideUI)))
		{
			return;
		}
		// Drawn in real screen pixels (identity matrix over the actual viewport) so it always covers the whole window,
		// whatever the UI scale or zoom; text is scaled by the UI scale by hand.
		spriteBatch.End();
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Matrix.Identity);
		try
		{
			Viewport viewport = spriteBatch.GraphicsDevice.Viewport;
			float w = viewport.Width;
			float h = viewport.Height;
			float ui = Math.Max(0.5f, Main.UIScale);
			DynamicSpriteFont big = FontAssets.DeathText.Value;
			DynamicSpriteFont small = FontAssets.MouseText.Value;
			Texture2D pixel = TextureAssets.MagicPixel.Value;
			if (_alpha > 0f)
			{
				spriteBatch.Draw(pixel, new Rectangle(-64, -64, (int)w + 128, (int)h + 128), new Color(8, 10, 20) * _alpha);
				float textAlpha = MathHelper.Clamp((_alpha - 0.35f) / 0.65f, 0f, 1f);
				if (textAlpha > 0f)
				{
					float drift = (_phase == Phase.FadeIn) ? (-14f * ui * (1f - _alpha)) : 0f;
					Vector2 textScale = new Vector2(0.9f * ui);
					Vector2 subSize = ChatManager.GetStringSize(small, IsaClient.Languages.L10n.T(_subtitle), textScale);
					Vector2 titleSize = ChatManager.GetStringSize(big, IsaClient.Languages.L10n.T(_title), textScale);
					Vector2 center = new Vector2(w / 2f, h / 2f + drift);
					ChatManager.DrawColorCodedStringWithShadow(spriteBatch, small, IsaClient.Languages.L10n.T(_subtitle), center + new Vector2(-subSize.X / 2f, -titleSize.Y / 2f - subSize.Y - 2f), new Color(170, 180, 200) * textAlpha, 0f, Vector2.Zero, textScale);
					ChatManager.DrawColorCodedStringWithShadow(spriteBatch, big, IsaClient.Languages.L10n.T(_title), center - titleSize / 2f, _sessionColor * textAlpha, 0f, Vector2.Zero, textScale);
				}
			}
			if (Active && _alpha < 1f && !Main.hideUI)
			{
				string tag = "Playing as " + Current.Name + "   [c/9AA7BE:.bot return  -  back to " + OriginalName + "]";
				Vector2 scale = new Vector2(0.75f * ui);
				Vector2 size = ChatManager.GetStringSize(small, IsaClient.Languages.L10n.T(tag), scale);
				Vector2 pos = new Vector2(w / 2f - size.X / 2f, h - size.Y - 14f * ui);
				float a = 1f - _alpha;
				Utils.DrawInvBG(spriteBatch, new Rectangle((int)(pos.X - 12f * ui), (int)(pos.Y - 5f * ui), (int)(size.X + 24f * ui), (int)(size.Y + 8f * ui)), new Color(20, 28, 60) * (0.8f * a));
				ChatManager.DrawColorCodedStringWithShadow(spriteBatch, small, IsaClient.Languages.L10n.T(tag), pos, _sessionColor * a, 0f, Vector2.Zero, scale);
			}
		}
		finally
		{
			spriteBatch.End();
			spriteBatch.Begin(SpriteSortMode.Deferred, null, null, null, null, null, Main.UIScaleMatrix);
		}
	}

	#endregion
}
