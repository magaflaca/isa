using System;
using System.Collections.Generic;
using System.Threading;
using IsaClient.Core;
using IsaClient.Networking;
using Microsoft.Xna.Framework;
using Terraria;

namespace IsaClient.Building;

/// <summary>Pastes sharing one schematic (Multi Paste): they wait for each other before objects go in, and stop together.</summary>
public sealed class PasteGroup
{
	public readonly List<PasteJob> Jobs = new List<PasteJob>();

	public readonly DateTime Started = DateTime.UtcNow;

	public volatile bool Cancelled;

	internal readonly HashSet<string> Errors = new HashSet<string>();

	public int Done
	{
		get
		{
			lock (Jobs)
			{
				int done = 0;
				foreach (PasteJob job in Jobs)
				{
					if (job.Finished)
					{
						done++;
					}
				}
				return done;
			}
		}
	}

	public bool Finished => Done >= Jobs.Count;

	/// <summary>"Paste", "Undo" or "Brush".</summary>
	public string Kind = "Paste";

	/// <summary>No "done" line at the end (brushes: every stroke is one).</summary>
	public bool Quiet;

	/// <summary>Main thread, once all parts are done.</summary>
	public Action OnFinished;
}

/// <summary>
/// One part of a paste, run by the client's own paste engine on its own thread: its tiles, and whose connection sends
/// them. A bot's part goes out through the bot's connection as the bot (its player in the packets that carry one), at
/// its own packet rate; your part goes out as yours. Nothing changes for a normal paste (no part).
/// </summary>
public sealed class PasteJob
{
	[ThreadStatic]
	private static PasteJob _current;

	private static int _botParts;

	public List<TileCopy> Tiles;

	public int SizeX;

	public int SizeY;

	public Point Origin;

	public bool BlendAir;

	/// <summary>Who pastes it ("you" or the bot's name), for its errors.</summary>
	public string Name = "you";

	/// <summary>The bot's player; -1 for you.</summary>
	public int PlayerIndex = -1;

	/// <summary>The bot's connection; null: yours.</summary>
	public Action<byte[]> Send;

	/// <summary>TShock disabled the bot (it waits, as you do).</summary>
	public Func<bool> Disabled;

	/// <summary>Packets before each pause (the .pl of this part).</summary>
	public int TileRate = 3;

	/// <summary>Packet Maximum (pause 500 ms) instead of Packet 100/ms.</summary>
	public bool MaxMode;

	public PasteGroup Group;

	/// <summary>The mount the bot rides while it builds (-1: none): kept in the positions the paste sends for it.</summary>
	public int MountType = -1;

	/// <summary>Where it was last put (a bot goes on from there).</summary>
	public Vector2 LastPosition;

	public volatile bool Finished;

	private bool _arrived;

	/// <summary>The part the paste engine runs on this thread, or null (a normal paste).</summary>
	public static PasteJob Current => _current;

	/// <summary>A bot's part runs on this thread.</summary>
	public static bool InBotPart => _current != null && _current.Send != null;

	/// <summary>Bots' parts running now (they are not your pastes: your position still goes out).</summary>
	public static int BotParts => _botParts;

	internal static void Enter(PasteJob job)
	{
		_current = job;
		if (job != null && job.Send != null)
		{
			Interlocked.Increment(ref _botParts);
		}
	}

	internal static void Leave()
	{
		PasteJob job = _current;
		_current = null;
		if (job == null)
		{
			return;
		}
		if (job.Send != null)
		{
			Interlocked.Decrement(ref _botParts);
		}
		job.Finished = true;
	}

	#region What the paste engine asks

	public static bool MaxModeNow => (_current != null && _current.Send != null) ? _current.MaxMode : PacketPacing.TilePacketMaxMode;

	public static int TileRateNow => (_current != null && _current.Send != null) ? Math.Max(1, _current.TileRate) : PacketPacing.TilePacketRate;

	public static int PaintRateNow => (_current != null && _current.Send != null) ? Math.Max(1, _current.TileRate) : PacketPacing.PaintPacketRate;

	public static int LiquidRateNow => (_current != null && _current.Send != null) ? Math.Max(1, _current.TileRate) : PacketPacing.LiquidPacketRate;

	public static int EraseRateNow => (_current != null && _current.Send != null) ? Math.Max(1, _current.TileRate) : PacketPacing.ErasePacketRate;

	/// <summary>
	/// A bot's pause after its <paramref name="sent"/>-th liquid: its .pl, then 100 ms (500 in Packet Maximum), never
	/// over 150 a second (TShock disables at 200).
	/// </summary>
	public static void PaceBotLiquid(int sent)
	{
		PasteJob job = _current;
		int rate = Math.Max(1, job?.TileRate ?? 1);
		if (sent % rate != 0)
		{
			return;
		}
		int wait = (job != null && job.MaxMode) ? 500 : 100;
		Thread.Sleep(Math.Max(wait, rate * 1000 / 150 + 20));
	}

	/// <summary>TShock disabled whoever pastes this part.</summary>
	public static bool DisabledNow
	{
		get
		{
			PasteJob job = _current;
			if (job != null && job.Disabled != null)
			{
				try
				{
					return job.Disabled();
				}
				catch
				{
					return false;
				}
			}
			return ServerDetection.DidTShockDisableYou;
		}
	}

	/// <summary>The paste was cancelled (any part seeing your cancel stops them all).</summary>
	public static bool CancelNow
	{
		get
		{
			PasteJob job = _current;
			if (job?.Group != null)
			{
				if (Selection.CancelPaste)
				{
					job.Group.Cancelled = true;
				}
				return job.Group.Cancelled || Selection.CancelPaste;
			}
			return Selection.CancelPaste;
		}
	}

	/// <summary>A fill, erase or conversion was cancelled (any part seeing your cancel stops them all).</summary>
	public static bool CancelActionNow
	{
		get
		{
			PasteJob job = _current;
			if (job?.Group != null)
			{
				if (Selection.CancelAction)
				{
					job.Group.Cancelled = true;
				}
				return job.Group.Cancelled || Selection.CancelAction;
			}
			return Selection.CancelAction;
		}
	}

	/// <summary>The engine's progress lines: only for a normal paste (Multi Paste says how it went at the end).</summary>
	public static void Info(string text)
	{
		if (_current == null)
		{
			ClientChat.Info(text);
		}
	}

	/// <summary>The engine's errors: a part's say whose they are, once each.</summary>
	public static void Error(string text)
	{
		PasteJob job = _current;
		if (job == null)
		{
			ClientChat.Error(text);
			return;
		}
		if (job.Group != null)
		{
			lock (job.Group.Errors)
			{
				if (!job.Group.Errors.Add(job.Name + "|" + text))
				{
					return;
				}
			}
		}
		ClientChat.Error("[" + job.Name + "] " + text);
	}

	/// <summary>
	/// Every part puts its blocks and walls down first; objects, chests, paint and liquids wait until all of them have
	/// (an object can stand on a block of the next part).
	/// </summary>
	public static void Barrier()
	{
		PasteJob job = _current;
		if (job?.Group == null || job._arrived)
		{
			return;
		}
		job._arrived = true;
		DateTime giveUp = DateTime.UtcNow.AddMinutes(15);
		while (DateTime.UtcNow < giveUp && !job.Group.Cancelled && !Main.gameMenu)
		{
			bool all = true;
			lock (job.Group.Jobs)
			{
				foreach (PasteJob other in job.Group.Jobs)
				{
					if (!other._arrived && !other.Finished)
					{
						all = false;
						break;
					}
				}
			}
			if (all)
			{
				return;
			}
			Thread.Sleep(25);
		}
	}

	#endregion

	/// <summary>
	/// A position packet (13) with the bot's own mount: the paste builds them from your player, whose mount (or none)
	/// would otherwise show on the bot.
	/// </summary>
	private static byte[] WithMount(byte[] packet, int mount)
	{
		// [length 2][13][player][bits1][bits2][bits3][bits4][item][x y][velocity 8 if bits2 & 4][mount 2 if bits2 & 128]...
		int at = 17 + (((packet[5] & 4) != 0) ? 8 : 0);
		bool has = (packet[5] & 128) != 0;
		if (packet.Length < at || (has && packet.Length < at + 2))
		{
			return packet;
		}
		byte[] result;
		if (mount >= 0 && has)
		{
			result = packet;
			result[at] = (byte)(mount & 255);
			result[at + 1] = (byte)(mount >> 8);
			return result;
		}
		if (mount >= 0)
		{
			result = new byte[packet.Length + 2];
			Buffer.BlockCopy(packet, 0, result, 0, at);
			result[at] = (byte)(mount & 255);
			result[at + 1] = (byte)(mount >> 8);
			Buffer.BlockCopy(packet, at, result, at + 2, packet.Length - at);
			result[5] |= 128;
		}
		else if (has)
		{
			result = new byte[packet.Length - 2];
			Buffer.BlockCopy(packet, 0, result, 0, at);
			Buffer.BlockCopy(packet, at + 2, result, at, packet.Length - at - 2);
			result[5] &= 127;
		}
		else
		{
			return packet;
		}
		result[0] = (byte)(result.Length & 255);
		result[1] = (byte)(result.Length >> 8);
		return result;
	}

	/// <summary>
	/// Sends a whole packet the engine built through the bot's connection, when this thread runs a bot's part; false: it
	/// goes out as usual. The player byte of the packets that carry one is the bot's.
	/// </summary>
	public static bool Route(byte[] data, int offset, int length)
	{
		PasteJob job = _current;
		if (job == null || job.Send == null || length < 3)
		{
			return false;
		}
		byte[] copy = new byte[length];
		Buffer.BlockCopy(data, offset, copy, 0, length);
		byte type = copy[2];
		if ((type == 5 || type == 13 || type == 147) && length > 3)
		{
			copy[3] = (byte)job.PlayerIndex;
		}
		if (type == 13 && length >= 17)
		{
			job.LastPosition = new Vector2(BitConverter.ToSingle(copy, 9), BitConverter.ToSingle(copy, 13));
			copy = WithMount(copy, job.MountType);
		}
		try
		{
			job.Send(copy);
		}
		catch
		{
		}
		return true;
	}
}
