using System.Collections.Generic;
using IsaClient.Core;
using IsaClient.Movement;
using IsaClient.Networking;
using Microsoft.Xna.Framework;
using Terraria;

namespace IsaClient.Games;

/// <summary>
/// Blocks to put in the world or take out, sent in order at World Edit's "Block &amp; Wall Speed"
/// (<see cref="PacketPacing"/>), the way its paste does on TShock: near the block (a fake position) with it in hand.
/// Each is done here at once and on the server. Alone, all go at once. Used by the games played in the world
/// (<see cref="SnakeArt"/>, <see cref="TetrisArt"/>). When TShock freezes you (as World Edit's paste, by
/// <see cref="ServerDetection.DidTShockDisableYou"/>) nothing goes out and <see cref="Frozen"/> tells the game to wait;
/// when it ends, what was sent just before and the server undid goes again.
/// </summary>
public sealed class WorldBlocks
{
	private struct Op
	{
		public int X;

		public int Y;

		/// <summary>The tile to place, or -1 to remove what's there.</summary>
		public int Tile;

		public int Item;
	}

	private readonly LinkedList<Op> _ops = new LinkedList<Op>();

	/// <summary>What was sent lately (and when): what TShock may undo when it freezes you.</summary>
	private readonly List<KeyValuePair<double, Op>> _recent = new List<KeyValuePair<double, Op>>();

	private bool _wasFrozen;

	/// <summary>After a freeze: when to look at what the server undid (its tiles arrive first).</summary>
	private double _resumeAt = -1.0;

	/// <summary>TShock froze you, or it just ended and what it undid is being looked at: the game waits.</summary>
	public bool Frozen => Main.netMode == 1 && (ServerDetection.DidTShockDisableYou || _resumeAt >= 0.0);

	/// <summary>Blocks sent in the current window (a tenth of a second, or half in the 500 ms mode).</summary>
	private int _sentInWindow;

	private double _windowEnd;

	public int Count => _ops.Count;

	public void Clear()
	{
		_ops.Clear();
		_recent.Clear();
	}

	public void Place(int x, int y, int tile, int item)
	{
		_ops.AddLast(new Op { X = x, Y = y, Tile = tile, Item = item });
	}

	public void Remove(int x, int y)
	{
		_ops.AddLast(new Op { X = x, Y = y, Tile = -1 });
	}

	/// <summary>Every tick: as many as the speed allows (a pause while the server is silent).</summary>
	public void Send()
	{
		if (Main.netMode != 1)
		{
			for (int i = 0; i < 400 && _ops.Count > 0; i++)
			{
				Apply(Next());
			}
			return;
		}
		double now = GameArt.Now;
		// nothing waiting and nothing sent lately: a freeze has nothing to do with these blocks
		if (_ops.Count == 0 && _recent.Count == 0 && _resumeAt < 0.0)
		{
			_wasFrozen = false;
			return;
		}
		if (ServerDetection.DidTShockDisableYou)
		{
			if (!_wasFrozen)
			{
				_wasFrozen = true;
				ClientChat.Error("TShock froze you: the blocks wait until it ends. Lower Block & Wall Speed to avoid this.");
			}
			_resumeAt = now + 0.75;
			return;
		}
		if (_resumeAt >= 0.0)
		{
			if (now < _resumeAt)
			{
				return;
			}
			_resumeAt = -1.0;
			_wasFrozen = false;
			Redo();
		}
		if (now >= _windowEnd)
		{
			_sentInWindow = 0;
			_windowEnd = now + (PacketPacing.TilePacketMaxMode ? 0.5 : 0.1);
		}
		if (PacketPacing.ServerSilentGuard)
		{
			return;
		}
		int rate = System.Math.Max(1, PacketPacing.TilePacketRate);
		while (_ops.Count > 0 && _sentInWindow < rate)
		{
			Op op = Next();
			Apply(op);
			_recent.Add(new KeyValuePair<double, Op>(now, op));
			_sentInWindow++;
		}
		// TShock undoes the last few seconds of edits when it freezes you.
		int old = 0;
		while (old < _recent.Count && now - _recent[old].Key > 6.0)
		{
			old++;
		}
		_recent.RemoveRange(0, old);
	}

	private Op Next()
	{
		Op op = _ops.First.Value;
		_ops.RemoveFirst();
		return op;
	}

	/// <summary>After a freeze: what was sent lately and isn't in the world now goes first again, in order.</summary>
	private void Redo()
	{
		int redone = 0;
		for (int i = _recent.Count - 1; i >= 0; i--)
		{
			Op op = _recent[i].Value;
			Tile tile = Main.tile[op.X, op.Y];
			bool done = (op.Tile < 0) ? (tile == null || !tile.active()) : (tile != null && tile.active() && tile.type == op.Tile);
			// Only the last thing done to each cell counts.
			bool later = false;
			for (int j = i + 1; j < _recent.Count; j++)
			{
				if (_recent[j].Value.X == op.X && _recent[j].Value.Y == op.Y)
				{
					later = true;
					break;
				}
			}
			if (!done && !later)
			{
				if (op.Tile >= 0 && tile != null && tile.active())
				{
					_ops.AddFirst(op);
					_ops.AddFirst(new Op { X = op.X, Y = op.Y, Tile = -1 });
				}
				else
				{
					_ops.AddFirst(op);
				}
				redone++;
			}
		}
		_recent.Clear();
		if (redone > 0)
		{
			ClientChat.Info(IsaClient.Languages.L10n.F("Unfrozen: sending again the {0} blocks TShock undid.", redone));
		}
	}

	private static void Apply(Op op)
	{
		if (!WorldGen.InWorld(op.X, op.Y, 1))
		{
			return;
		}
		if (op.Tile < 0)
		{
			if (Main.tile[op.X, op.Y] == null || !Main.tile[op.X, op.Y].active())
			{
				return;
			}
			WorldGen.KillTile(op.X, op.Y, fail: false, effectOnly: false, noItem: true);
			if (Main.netMode == 1)
			{
				TeleportTools.FakeNetMessage13(new Vector2((op.X + 2) * 16, op.Y * 16), 58);
				NetMessage.SendData(17, -1, -1, null, 4, op.X, op.Y);
			}
			return;
		}
		if (Main.tile[op.X, op.Y] != null && Main.tile[op.X, op.Y].active())
		{
			WorldGen.KillTile(op.X, op.Y, fail: false, effectOnly: false, noItem: true);
		}
		WorldGen.PlaceTile(op.X, op.Y, op.Tile, mute: true, forced: true);
		if (Main.netMode == 1)
		{
			TeleportTools.FakeNetMessage13(new Vector2((op.X + 2) * 16, op.Y * 16), 58);
			RawPackets.SendModifiedData(5, -1, -1, null, Main.myPlayer, 58f, 0f, op.Item);
			NetMessage.SendData(17, -1, -1, null, 1, op.X, op.Y, op.Tile);
		}
	}
}
