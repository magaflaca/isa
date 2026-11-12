using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Core;
using IsaClient.Movement;
using IsaClient.Networking;
using Microsoft.Xna.Framework;
using Terraria;

namespace IsaClient.WorldTools;

/// <summary>
/// .wipe / .wipeitems / .autowipe: every item on the ground removed for everyone, as fast as the server allows.
/// In 1.4.5.8 the server only lets a player remove an item reserved for them (it reserves each item for the closest
/// player who has room for it), and removing is packet 151. So:
/// <list type="bullet">
/// <item>An item reserved for you goes the moment the server says so (packet 22 arriving), every time, during a wipe
/// and with the auto wipe on.</item>
/// <item>The server is told you stand on each group of items in turn, a few ticks each (it hands out free items every 5
/// ticks): once one is yours it stays yours, so the tour doesn't wait for answers. The server sees you jump around.</item>
/// <item>Items another player holds are looked at again 300 ticks after they were given to them, and then their client
/// is asked to hand them over: the time each was given is noted (packet 22), and you stand on it from just before that
/// moment until the handover is done. When it isn't known, you wait on that group (at most 5 seconds).</item>
/// <item>The server checks you have room: during the wipe it's told your last inventory slot is empty, and gets the
/// real one back at the end, with your real position.</item>
/// <item>Your client keeps whatever it is given meanwhile (it would hand items back when you aren't the closest).</item>
/// </list>
/// In single player everything just goes.
/// </summary>
public static class GroundWipe
{
	/// <summary>Longest a wipe goes on, in ticks.</summary>
	private const int MaxTicks = 60 * 15;

	/// <summary>Ticks on a group of free items (the server hands them out every 5; one more for the packet).</summary>
	private const int FreeDwell = 6;

	/// <summary>Ticks on a group another player holds when its turn isn't known (theirs come every 300).</summary>
	private const int HeldDwell = 305;

	/// <summary>Around a known turn: from this much before (your position has to get there first)...</summary>
	private const int TurnBefore = 30;

	/// <summary>...to this much after (their client is asked to hand it over, then the server gives it to you).</summary>
	private const int TurnAfter = 60;

	/// <summary>Grouping distance (the server counts you up to 1920 away, corner to corner).</summary>
	private const float Reach = 1700f;

	/// <summary>The last inventory slot, told to the server as empty during the wipe.</summary>
	private const int RoomSlot = 49;

	private static bool _running;

	private static bool _quiet;

	private static int _ticks;

	private static int _removed;

	private static int _autoTicks;

	private static bool _roomFaked;

	private static Vector2? _spot;

	private static int _spotUntil;

	/// <summary>When each item was last given to another player (our tick), to be there on its next turn.</summary>
	private static readonly Dictionary<int, uint> _heldSince = new Dictionary<int, uint>();

	/// <summary>Groups of held items waited on with no known turn (each once per wipe).</summary>
	private static readonly HashSet<int> _waited = new HashSet<int>();

	/// <summary>When the world was entered (reservations told at login don't say when they were made).</summary>
	private static uint _joinedAt;

	private static bool _wasInWorld;

	/// <summary>While on, your client never hands back an item the server reserved for you (Main.UpdateClient).</summary>
	public static bool Busy => _running;

	private static bool Removing => _running || WorldCommands.AutoDeleteItems;

	/// <summary>Starts a wipe of every item on the ground (quiet: no chat, for the auto wipe).</summary>
	public static void Start(bool quiet)
	{
		if (Main.gameMenu)
		{
			return;
		}
		if (Main.netMode == 0)
		{
			int count = 0;
			for (int i = 0; i < Main.maxItems; i++)
			{
				if (Main.item[i].active && Main.item[i].type > 0)
				{
					Main.item[i].TurnToAir();
					count++;
				}
			}
			if (!quiet)
			{
				ClientChat.Info("Total items removed: " + count);
			}
			return;
		}
		if (_running)
		{
			if (!quiet)
			{
				ClientChat.Info("A wipe is already going.");
			}
			return;
		}
		_running = true;
		_quiet = quiet;
		_ticks = 0;
		_removed = 0;
		_spot = null;
		_waited.Clear();
		FakeRoom();
		RemoveMine();
		if (!quiet)
		{
			ClientChat.Info(IsaClient.Languages.L10n.F("Wiping {0} items on the ground...", Count()));
		}
	}

	/// <summary>MessageBuffer, packet 22 (who an item is reserved for): yours goes at once; others' turn is noted.</summary>
	public static void OnReserved(int index)
	{
		if (Main.netMode != 1 || index < 0 || index >= Main.maxItems)
		{
			return;
		}
		WorldItem item = Main.item[index];
		int owner = item.playerIndexTheItemIsReservedFor;
		if (owner == Main.myPlayer)
		{
			_heldSince.Remove(index);
			if (Removing)
			{
				Remove(index);
			}
		}
		else if (owner != 255 && Main.GameUpdateCount - _joinedAt > 180)
		{
			_heldSince[index] = Main.GameUpdateCount;
		}
		else
		{
			_heldSince.Remove(index);
		}
	}

	/// <summary>Every tick (main thread).</summary>
	internal static void Update()
	{
		bool inWorld = !Main.gameMenu && Main.netMode == 1;
		if (inWorld && !_wasInWorld)
		{
			_joinedAt = Main.GameUpdateCount;
			_heldSince.Clear();
		}
		_wasInWorld = inWorld;
		if (!inWorld)
		{
			_running = false;
			_roomFaked = false;
			return;
		}
		if (!Removing)
		{
			return;
		}
		RemoveMine();
		if (!_running)
		{
			// The auto wipe also tours the world now and then (what isn't near you never comes to you).
			if (++_autoTicks >= 60 * 10 && Count() > 0)
			{
				_autoTicks = 0;
				Start(quiet: true);
			}
			return;
		}
		_ticks++;
		List<WorldItem> left = Left();
		if (left.Count == 0 || _ticks > MaxTicks)
		{
			Finish(left.Count);
			return;
		}
		uint now = Main.GameUpdateCount;
		// Staying where we are: until its time is up, while something of it is still there.
		if (_spot.HasValue && _ticks < _spotUntil && left.Any(i => Near(i, _spot.Value)))
		{
			Stand(_spot.Value);
			return;
		}
		// 1. A held item whose turn comes now: be there from just before it to just after.
		WorldItem due = null;
		int dueIn = int.MaxValue;
		foreach (WorldItem i in left)
		{
			if (i.playerIndexTheItemIsReservedFor != 255 && _heldSince.TryGetValue(i.whoAmI, out uint since))
			{
				int age = (int)(now - since);
				int toTurn = 300 - age % 300;
				if (toTurn <= TurnBefore && toTurn < dueIn)
				{
					due = i;
					dueIn = toTurn;
				}
			}
		}
		if (due != null)
		{
			Go(due.Center, dueIn + TurnAfter);
			return;
		}
		// 2. Free items, the closest group next.
		Vector2 from = _spot ?? Main.LocalPlayer.Center;
		WorldItem free = left.Where(i => i.playerIndexTheItemIsReservedFor == 255).OrderBy(i => Vector2.DistanceSquared(i.Center, from)).FirstOrDefault();
		if (free != null)
		{
			Go(free.Center, FreeDwell);
			return;
		}
		// 3. Held items whose turn isn't known: wait on each group once (known ones come back on their turn).
		WorldItem unknown = left.Where(i => i.playerIndexTheItemIsReservedFor != 255 && !_heldSince.ContainsKey(i.whoAmI) && !_waited.Contains(i.whoAmI)).OrderBy(i => Vector2.DistanceSquared(i.Center, from)).FirstOrDefault();
		if (unknown != null)
		{
			foreach (WorldItem i in left.Where(i => Near(i, unknown.Center)))
			{
				_waited.Add(i.whoAmI);
			}
			Go(unknown.Center, HeldDwell);
			return;
		}
		// 4. Only held items with a known turn later on: wait where the next one is.
		if (_heldSince.Count == 0 || !left.Any(i => _heldSince.ContainsKey(i.whoAmI)))
		{
			Finish(left.Count);
		}
	}

	private static void Go(Vector2 spot, int ticks)
	{
		_spot = spot;
		_spotUntil = _ticks + ticks;
		Stand(spot);
	}

	private static List<WorldItem> Left()
	{
		List<WorldItem> list = new List<WorldItem>();
		for (int i = 0; i < Main.maxItems; i++)
		{
			WorldItem item = Main.item[i];
			if (item.active && item.type > 0 && item.playerIndexTheItemIsReservedFor != Main.myPlayer)
			{
				list.Add(item);
			}
		}
		return list;
	}

	private static int Count()
	{
		int n = 0;
		for (int i = 0; i < Main.maxItems; i++)
		{
			if (Main.item[i].active && Main.item[i].type > 0)
			{
				n++;
			}
		}
		return n;
	}

	private static bool Near(WorldItem item, Vector2 spot)
	{
		return Math.Abs(item.Center.X - spot.X) + Math.Abs(item.Center.Y - spot.Y) < Reach;
	}

	/// <summary>Every item reserved for you, removed for everyone.</summary>
	private static void RemoveMine()
	{
		for (int i = 0; i < Main.maxItems; i++)
		{
			WorldItem item = Main.item[i];
			if (item.active && item.type > 0 && item.playerIndexTheItemIsReservedFor == Main.myPlayer)
			{
				Remove(i);
			}
		}
	}

	private static void Remove(int i)
	{
		WorldItem item = Main.item[i];
		if (!item.active || item.type <= 0)
		{
			return;
		}
		NetMessage.SendData(151, -1, -1, null, i);
		item.TurnToAir();
		_heldSince.Remove(i);
		_removed++;
	}

	/// <summary>The server is told you stand on this spot (every tick while there, so nothing moves you back).</summary>
	private static void Stand(Vector2 spot)
	{
		Player me = Main.LocalPlayer;
		TeleportTools.FakeNetMessage13(spot - new Vector2(me.width / 2f, me.height / 2f), me.selectedItem);
	}

	/// <summary>The server only gives you items you'd have room for: your last slot is empty for it, for now.</summary>
	private static void FakeRoom()
	{
		Player me = Main.LocalPlayer;
		_roomFaked = !me.inventory[RoomSlot].IsAir;
		if (_roomFaked)
		{
			RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, RoomSlot, 0f, 0f);
		}
	}

	private static void Finish(int left)
	{
		_running = false;
		_spot = null;
		if (_roomFaked)
		{
			// The real slot again.
			_roomFaked = false;
			Player me = Main.LocalPlayer;
			NetMessage.SendData(5, -1, -1, null, Main.myPlayer, RoomSlot, me.inventory[RoomSlot].prefix);
		}
		// Your real position again.
		NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
		if (!_quiet)
		{
			ClientChat.Info("Total items removed: " + _removed + ((left > 0) ? ("  (" + left + " stayed: someone else stood closer to them)") : ""));
		}
	}
}
