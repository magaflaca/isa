using System;
using System.Collections.Generic;

namespace IsaClient.MultiClient;

/// <summary>
/// Everything one server connection received that the game needs to rebuild that server's world without asking again:
/// tile data and tile edits in arrival order, plus the latest copy of "state" packets (world info, NPCs, items, players).
/// Each connection (headless or driven by the game) has one; it is what makes switching sessions possible without
/// reconnecting, both inside the same world (replay only what is new) and across servers (rebuild the whole world).
/// </summary>
public sealed class WorldJournal
{
	/// <summary>Tile packets, replayed in order: section, frame section, tile edits, doors, tile squares, chests, signs, liquid, objects, tile entities.</summary>
	private static readonly bool[] _orderedTypes = Types(10, 11, 17, 19, 20, 34, 47, 48, 79, 86);

	/// <summary>Latest-wins packets about the world itself.</summary>
	private static readonly bool[] _worldStateTypes = Types(7, 21, 22, 23, 56, 57, 74, 78, 90, 101, 103, 106);

	/// <summary>Latest-wins packets about players (keyed by player, and slot for equipment).</summary>
	private static readonly bool[] _playerStateTypes = Types(4, 5, 13, 14, 16, 30, 42, 45, 50, 147);

	/// <summary>Replay order of player state: activate, look, equipment, then the rest.</summary>
	private static readonly byte[] _playerReplayOrder = new byte[10] { 14, 4, 5, 147, 16, 42, 50, 30, 45, 13 };

	private readonly object _lock = new object();

	private readonly List<byte[]> _ordered = new List<byte[]>();

	private readonly Dictionary<long, byte[]> _worldState = new Dictionary<long, byte[]>();

	private readonly Dictionary<long, byte[]> _playerState = new Dictionary<long, byte[]>();

	private long _bytes;

	private readonly long _cap;

	/// <summary>Tile data was dropped because the cap was reached; a rebuilt world may miss areas.</summary>
	public bool Overflowed { get; private set; }

	/// <summary>How many ordered frames the game world already contains (for same-world incremental replay).</summary>
	public int Cursor;

	/// <summary><see cref="IsaClient.MultiClient.WorldEpoch"/> the cursor belongs to; a different epoch means the game world was rebuilt since.</summary>
	public int CursorEpoch = -1;

	public WorldJournal(long capBytes)
	{
		_cap = capBytes;
	}

	private static bool[] Types(params int[] types)
	{
		bool[] result = new bool[256];
		foreach (int t in types)
		{
			result[t] = true;
		}
		return result;
	}

	public long Bytes
	{
		get
		{
			lock (_lock)
			{
				return _bytes;
			}
		}
	}

	public int OrderedCount
	{
		get
		{
			lock (_lock)
			{
				return _ordered.Count;
			}
		}
	}

	public static bool IsInteresting(byte type)
	{
		return _orderedTypes[type] || _worldStateTypes[type] || _playerStateTypes[type];
	}

	/// <summary>Records a packet. <paramref name="payload"/> starts right after the type byte.</summary>
	public void Record(byte type, byte[] payload, int offset, int count)
	{
		if (!IsInteresting(type) || count < 0)
		{
			return;
		}
		byte[] frame = new byte[count + 3];
		frame[0] = (byte)(frame.Length & 0xFF);
		frame[1] = (byte)(frame.Length >> 8);
		frame[2] = type;
		Buffer.BlockCopy(payload, offset, frame, 3, count);
		lock (_lock)
		{
			if (_orderedTypes[type])
			{
				if (Overflowed)
				{
					return;
				}
				if (_bytes + frame.Length > _cap)
				{
					Overflowed = true;
					return;
				}
				_bytes += frame.Length;
				_ordered.Add(frame);
				return;
			}
			if (_worldStateTypes[type])
			{
				long key = ((long)((type == 90) ? 21 : type) << 32) | (uint)WorldStateIndex(type, frame);
				_worldState[key] = frame;
				return;
			}
			int player = (count > 0) ? payload[offset] : 0;
			if (type == 14 && count >= 2 && payload[offset + 1] == 0)
			{
				// Player left: forget everything about that slot so a rebuilt world does not show a ghost.
				List<long> stale = new List<long>();
				foreach (long k in _playerState.Keys)
				{
					if ((int)((k >> 16) & 0xFF) == player)
					{
						stale.Add(k);
					}
				}
				foreach (long k in stale)
				{
					_playerState.Remove(k);
				}
			}
			int slot = (type == 5 && count >= 3) ? (payload[offset + 1] | (payload[offset + 2] << 8)) : 0;
			_playerState[((long)type << 32) | ((long)player << 16) | (uint)(slot & 0xFFFF)] = frame;
		}
	}

	/// <summary>Index inside a latest-wins world packet (NPC / item slot etc.), 0 for singletons.</summary>
	private static int WorldStateIndex(byte type, byte[] frame)
	{
		switch (type)
		{
		case 21:
		case 90:
		case 22:
		case 23:
		case 56:
			return (frame.Length >= 5) ? (frame[3] | (frame[4] << 8)) : 0;
		default:
			return 0;
		}
	}

	/// <summary>Ordered frames from <paramref name="from"/> to the end.</summary>
	public List<byte[]> OrderedFrom(int from)
	{
		lock (_lock)
		{
			from = Math.Max(0, Math.Min(from, _ordered.Count));
			return _ordered.GetRange(from, _ordered.Count - from);
		}
	}

	/// <summary>World info (packet 7) first, then the other world state.</summary>
	public List<byte[]> WorldState()
	{
		lock (_lock)
		{
			List<byte[]> result = new List<byte[]>();
			if (_worldState.TryGetValue(7L << 32, out var info))
			{
				result.Add(info);
			}
			foreach (KeyValuePair<long, byte[]> pair in _worldState)
			{
				if (pair.Key != 7L << 32)
				{
					result.Add(pair.Value);
				}
			}
			return result;
		}
	}

	public byte[] WorldInfo()
	{
		lock (_lock)
		{
			_worldState.TryGetValue(7L << 32, out var info);
			return info;
		}
	}

	/// <summary>Player state in a safe replay order; <paramref name="onlyPlayer"/> &gt;= 0 limits it to one slot.</summary>
	public List<byte[]> PlayerState(int onlyPlayer = -1)
	{
		lock (_lock)
		{
			List<byte[]> result = new List<byte[]>();
			foreach (byte type in _playerReplayOrder)
			{
				foreach (KeyValuePair<long, byte[]> pair in _playerState)
				{
					if ((byte)(pair.Key >> 32) != type)
					{
						continue;
					}
					int player = (int)((pair.Key >> 16) & 0xFF);
					if (onlyPlayer < 0 || player == onlyPlayer)
					{
						result.Add(pair.Value);
					}
				}
			}
			return result;
		}
	}

	/// <summary>Player slots this server reported as active (from the latest packet 14 of each).</summary>
	public List<int> ActivePlayers()
	{
		lock (_lock)
		{
			List<int> result = new List<int>();
			foreach (KeyValuePair<long, byte[]> pair in _playerState)
			{
				if ((byte)(pair.Key >> 32) == 14 && pair.Value.Length >= 5 && pair.Value[4] == 1)
				{
					result.Add(pair.Value[3]);
				}
			}
			return result;
		}
	}

	public void Clear()
	{
		lock (_lock)
		{
			_ordered.Clear();
			_worldState.Clear();
			_playerState.Clear();
			_bytes = 0;
			Overflowed = false;
			Cursor = 0;
			CursorEpoch = -1;
		}
	}
}
