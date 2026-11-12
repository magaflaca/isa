using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using IsaClient.Core;
using IsaClient.Networking;
using IsaClient.Building;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.IO;

namespace IsaClient.MultiClient;

/// <summary>
/// Multi Paste (Experimental): every paste you make is shared out between you and your bots on a TShock server. The
/// schematic is cut in strips of columns (never through an object) and each one pastes its strip with the client's own
/// paste engine at the same time, the bots through their own connections at their own packet rate (.pl, 3 in Packet
/// 100/ms by default), so no one goes over the server's limits and no one pastes over another. Blocks first for all,
/// then objects. The default set's bots join when it is turned on, do their steps and come to you; or it uses the bots
/// already there. While they paste they wear the plumber's clothes with a simple dye, and their own 10 s after.
/// </summary>
public static class MultiPaste
{
	public static bool Enabled;

	/// <summary>The set whose bots join to paste (none: any bot here).</summary>
	public static string SetId = "";

	/// <summary>No bots are called: the ones on this server paste.</summary>
	public static bool UseBotsHere;

	/// <summary>You take a strip too.</summary>
	public static bool YouPasteToo = true;

	public static bool Plumbers = true;

	/// <summary>The buff bots show while they build (230: the Witch's Broom), 0 for none.</summary>
	public static int PasteBuff = Terraria.ID.BuffID.WitchBroom;

	/// <summary>Another buff for building bots; the ones building (or about to change back) show it at once.</summary>
	public static void SetPasteBuff(int buff)
	{
		PasteBuff = (buff > 0 && buff < Terraria.ID.BuffID.Count) ? buff : 0;
		foreach (ClientSession s in MultiClientManager.All.Where(s => s.WorkBuff > 0))
		{
			s.SetWorkBuff(PasteBuff);
			if (_parts.TryGetValue(s, out PasteJob job))
			{
				job.MountType = s.WorkMount;
			}
		}
		MultiClientManager.Save();
	}

	/// <summary>A bot's .pl when it has none of its own.</summary>
	public static int DefaultRate = 3;

	/// <summary>Bots send in Packet Maximum (pause 500 ms) instead of Packet 100/ms.</summary>
	public static bool BotMaxMode;

	/// <summary>Each bot's own .pl, by name.</summary>
	public static Dictionary<string, int> Rates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	public const float OutfitSeconds = 10f;

	/// <summary>Plumber's Hat, Shirt and Pants.</summary>
	private static readonly int[] Outfit = { 244, 245, 246 };

	/// <summary>The one-color dyes: red, orange, yellow, lime, green, teal, cyan, sky blue, blue, purple, violet, pink.</summary>
	private static readonly int[] SimpleDyes = { 1007, 1008, 1009, 1010, 1011, 1012, 1013, 1014, 1015, 1016, 1017, 1018 };

	private static PasteGroup _group;

	private static readonly Dictionary<ClientSession, PasteJob> _parts = new Dictionary<ClientSession, PasteJob>();

	private static DateTime _undressAt = DateTime.MaxValue;

	private static readonly HashSet<ClientSession> _called = new HashSet<ClientSession>();

	private static readonly Random _random = new Random();

	/// <summary>The paste going on now, or null.</summary>
	public static PasteGroup Running => _group;

	/// <summary>The last paste shared out (going on or done).</summary>
	public static PasteGroup Last { get; private set; }

	public static BotSet Set => string.IsNullOrEmpty(SetId) ? null : BotSets.Sets.FirstOrDefault(s => s.Id == SetId);

	public static int RateOf(ClientSession s)
	{
		return (s?.Name != null && Rates.TryGetValue(s.Name, out int rate)) ? rate : DefaultRate;
	}

	/// <summary>One .pl for every bot (their own ones go).</summary>
	public static void SetAllRates(int rate)
	{
		DefaultRate = Math.Max(1, Math.Min(1000, rate));
		Rates.Clear();
		MultiClientManager.Save();
	}

	public static void SetRate(string name, int rate)
	{
		if (string.IsNullOrEmpty(name))
		{
			return;
		}
		Rates[name] = Math.Max(1, Math.Min(1000, rate));
		MultiClientManager.Save();
	}

	#region Who pastes

	/// <summary>A bot that would paste with you (from the default set, or any with "bots already here").</summary>
	public static bool IsCandidate(ClientSession s)
	{
		if (s == null || s.UserRemoved || s.IsMainSession)
		{
			return false;
		}
		BotSet set = Set;
		return UseBotsHere || set == null || s.SetId == set.Id;
	}

	/// <summary>In your world, alive, done with its steps: it pastes with you.</summary>
	public static bool IsReady(ClientSession s)
	{
		return IsCandidate(s) && s.State == ClientSessionState.InGame && s.Spawned && !s.Possessed && !s.Dead && s.IsConnected && s.MyIndex >= 0 && s.MyIndex < 255 && Possession.IsOnMyServer(s) && (s.Script == null || s.Script.Done);
	}

	public static List<ClientSession> Helpers()
	{
		return MultiClientManager.All.Where(IsReady).ToList();
	}

	/// <summary>"Ready", "Pasting", "Doing its steps"...</summary>
	public static string StatusOf(ClientSession s)
	{
		if (s.Pasting)
		{
			return "Pasting";
		}
		if (s.State != ClientSessionState.InGame || !s.Spawned)
		{
			return "Joining";
		}
		if (!Possession.IsOnMyServer(s))
		{
			return "On another server";
		}
		if (s.Possessed)
		{
			return "You play as it";
		}
		if (s.Dead)
		{
			return "Dead";
		}
		if (s.Script != null && !s.Script.Done)
		{
			return "Doing its steps";
		}
		return s.TShockDisabled ? "Disabled by TShock" : "Ready";
	}

	#endregion

	#region On and off

	public static void SetEnabled(bool on)
	{
		if (!on)
		{
			Enabled = false;
			_called.Clear();
			MultiClientManager.Announce("Multi Paste off: you paste alone.");
			return;
		}
		if (Main.netMode != 1 || Main.gameMenu || !MultiClientManager.TryGetCurrentServer(out string host, out int port))
		{
			MultiClientManager.AnnounceError("Multi Paste works on a server: join one first.");
			return;
		}
		Enabled = true;
		_called.Clear();
		BotSet set = Set;
		if (!UseBotsHere && set != null)
		{
			if (string.IsNullOrWhiteSpace(set.Host) || set.Port != port || !Possession.SameHost(host, set.Host))
			{
				MultiClientManager.AnnounceError("Multi Paste: the set \"" + set.Name + "\" goes to " + (string.IsNullOrWhiteSpace(set.Host) ? "no server" : (set.Host + ":" + set.Port)) + ", not here. Its bots weren't called; the bots here paste with you.");
			}
			else
			{
				BotSets.Start(set);
			}
		}
		int ready = Helpers().Count;
		MultiClientManager.Announce("Multi Paste on: your building is shared out (" + ready + ((ready == 1) ? " bot" : " bots") + " ready now" + (YouPasteToo ? ", and you" : "") + ").");
	}

	#endregion

	#region Sharing out

	/// <summary>
	/// Shares out a paste at <paramref name="origin"/> when Multi Paste is on and some bot is ready; false: paste it as
	/// usual (alone).
	/// </summary>
	public static bool TryStart(Point origin, List<TileCopy> tiles, int sizeX, int sizeY, bool blendAir)
	{
		return TryStartTiles("Paste", origin, tiles, sizeX, sizeY, blendAir);
	}

	/// <summary>Ctrl+Z / .undo of a paste, shared out like a paste.</summary>
	public static bool TryStartUndo(PasteUndo.PasteUndoEntry entry)
	{
		return entry != null && TryStartTiles("Undo", entry.Position, entry.Tiles, entry.SizeX, entry.SizeY, false);
	}

	private static bool TryStartTiles(string kind, Point origin, List<TileCopy> tiles, int sizeX, int sizeY, bool blendAir)
	{
		if (!Usable() || tiles == null || sizeX <= 0 || sizeY <= 0 || tiles.Count < sizeX * sizeY)
		{
			return false;
		}
		if (_group != null)
		{
			MultiClientManager.AnnounceError("Multi Paste: the last " + _group.Kind.ToLowerInvariant() + " is still going. Wait for it (or cancel it).");
			return true;
		}
		List<ClientSession> bots = Helpers();
		int parts = Math.Min(bots.Count + (YouPasteToo ? 1 : 0), sizeX);
		if (bots.Count == 0 || parts < 1 || (YouPasteToo && parts < 2))
		{
			return false;
		}
		List<int> cuts = Split(tiles, sizeX, sizeY, parts);
		Selection.CancelPaste = false;
		List<PasteJob> jobs = new List<PasteJob>();
		for (int i = 0; i + 1 < cuts.Count; i++)
		{
			int a = cuts[i];
			int e = cuts[i + 1];
			jobs.Add(new PasteJob
			{
				Tiles = tiles.GetRange(a * sizeY, (e - a) * sizeY),
				SizeX = e - a,
				SizeY = sizeY,
				Origin = new Point(origin.X + a, origin.Y),
				BlendAir = blendAir,
				LastPosition = new Vector2((origin.X + (a + e) / 2f) * 16f, origin.Y * 16f)
			});
		}
		Launch(kind, jobs, bots, PasteEngine.PastePart, quiet: false, sizeX + "x" + sizeY);
		return true;
	}

	/// <summary>Fill, erase or convert the selection (<paramref name="area"/>), in strips of columns, one each.</summary>
	public static bool TryStartRegion(string kind, Rectangle area, Action<PasteJob> run)
	{
		if (!Usable() || _group != null || area.Width <= 0 || area.Height <= 0)
		{
			return false;
		}
		List<ClientSession> bots = Helpers();
		int parts = Math.Min(bots.Count + (YouPasteToo ? 1 : 0), area.Width);
		if (bots.Count == 0 || parts < 1 || (YouPasteToo && parts < 2))
		{
			return false;
		}
		Selection.CancelAction = false;
		List<PasteJob> jobs = new List<PasteJob>();
		for (int i = 0; i < parts; i++)
		{
			int a = area.Width * i / parts;
			int e = area.Width * (i + 1) / parts;
			if (e <= a)
			{
				continue;
			}
			jobs.Add(new PasteJob
			{
				SizeX = e - a,
				SizeY = area.Height,
				Origin = new Point(area.X + a, area.Y),
				LastPosition = new Vector2((area.X + (a + e) / 2f) * 16f, area.Y * 16f)
			});
		}
		Launch(kind, jobs, bots, run, quiet: false, area.Width + "x" + area.Height);
		return true;
	}

	/// <summary>
	/// The brush's queued tiles (brushes, tile/wall/liquid/paint/erase), shared out: sorted left to right and cut in
	/// pieces, one each. False: the brush sends them itself.
	/// </summary>
	public static bool TryStartBrush()
	{
		if (!Usable() || _group != null)
		{
			return false;
		}
		List<BrushOperations.BrushData> queued;
		lock (BrushOperations.TilesToBrush)
		{
			queued = new List<BrushOperations.BrushData>(BrushOperations.TilesToBrush);
		}
		if (queued.Count < 8)
		{
			return false;
		}
		List<ClientSession> bots = Helpers();
		int parts = Math.Min(bots.Count + (YouPasteToo ? 1 : 0), queued.Count / 4);
		if (bots.Count == 0 || parts < 1 || (YouPasteToo && parts < 2))
		{
			return false;
		}
		PacketPacing.HandlingPackets = true;
		int taken = queued.Count;
		List<BrushOperations.BrushData> sorted = queued.OrderBy(d => d.X).ThenBy(d => d.Y).ToList();
		List<PasteJob> jobs = new List<PasteJob>();
		Dictionary<PasteJob, List<BrushOperations.BrushData>> pieces = new Dictionary<PasteJob, List<BrushOperations.BrushData>>();
		for (int i = 0; i < parts; i++)
		{
			int a = sorted.Count * i / parts;
			int e = sorted.Count * (i + 1) / parts;
			if (e <= a)
			{
				continue;
			}
			List<BrushOperations.BrushData> piece = sorted.GetRange(a, e - a);
			BrushOperations.BrushData mid = piece[piece.Count / 2];
			PasteJob job = new PasteJob { LastPosition = new Vector2(mid.X * 16f, mid.Y * 16f) };
			pieces[job] = piece;
			jobs.Add(job);
		}
		PasteGroup group = Launch("Brush", jobs, bots, delegate(PasteJob job)
		{
			List<BrushOperations.BrushData> piece = pieces[job];
			Thread thread = new Thread(() => BrushPart(job, piece)) { IsBackground = true, Name = "Multi Paste brush" };
			thread.Start();
		}, quiet: true, null);
		group.OnFinished = delegate
		{
			lock (BrushOperations.TilesToBrush)
			{
				int n = Math.Min(taken, BrushOperations.TilesToBrush.Count);
				BrushOperations.TilesToBrush.RemoveRange(0, n);
			}
			PacketPacing.HandlingPackets = false;
		};
		return true;
	}

	/// <summary>One piece of a brush, on its own thread: the brush's own packets for each tile, at this part's pace.</summary>
	private static void BrushPart(PasteJob job, List<BrushOperations.BrushData> piece)
	{
		PasteJob.Enter(job);
		try
		{
			int[] counts = new int[4];
			int[] sent = new int[4];
			int liquidsThisSecond = 0;
			foreach (BrushOperations.BrushData data in piece)
			{
				if (Main.gameMenu || job.Group.Cancelled)
				{
					break;
				}
				while (PasteJob.DisabledNow && !Main.gameMenu && !job.Group.Cancelled)
				{
					Thread.Sleep(50);
				}
				BrushOperations.SendBrushPackets(data, counts);
				for (int k = 0; k < 4; k++)
				{
					sent[k] += counts[k];
					counts[k] = 0;
				}
				if (data.BrushType == 3 && ServerDetection.OnTshockServer && ++liquidsThisSecond >= 150)
				{
					liquidsThisSecond = 0;
					Thread.Sleep(1050);
				}
				int kind;
				int rate;
				switch (data.BrushType)
				{
				case 1:
					kind = 1;
					rate = PasteJob.EraseRateNow;
					break;
				case 3:
					kind = 2;
					rate = PasteJob.LiquidRateNow;
					break;
				case 4:
					kind = 3;
					rate = PasteJob.PaintRateNow;
					break;
				default:
					kind = 0;
					rate = PasteJob.TileRateNow;
					break;
				}
				rate = Math.Max(1, rate);
				bool guard = PacketPacing.ServerSilentGuard || Performance.LowFpsGuard;
				if (PasteJob.MaxModeNow)
				{
					if (sent[kind] > rate || guard)
					{
						sent[kind] = 0;
						Thread.Sleep(500);
					}
				}
				else if (sent[kind] % rate == 0 || guard)
				{
					Thread.Sleep(100);
				}
			}
		}
		catch (Exception ex)
		{
			PasteJob.Error("Brush error: " + ex.Message);
			ClientLog.LogException(ex);
		}
		finally
		{
			if (job.Send == null)
			{
				NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
				NetMessage.SendData(5, -1, -1, null, Main.myPlayer, PlayerItemSlotID.InventoryMouseItem, (int)Main.LocalPlayer.inventory[PlayerItemSlotID.InventoryMouseItem].prefix);
			}
			PasteJob.Leave();
		}
	}

	private static bool Usable()
	{
		if (!Enabled || Main.netMode != 1 || Main.gameMenu)
		{
			return false;
		}
		// A finished one not let go yet (the next tick would): let it go now.
		PasteGroup group = _group;
		if (group != null && group.Finished)
		{
			Finish(group);
		}
		return true;
	}

	/// <summary>
	/// Gives the parts out (you the one nearest you, when you take part; each bot the nearest free one), dresses the
	/// bots and starts every part with <paramref name="run"/>.
	/// </summary>
	private static PasteGroup Launch(string kind, List<PasteJob> jobs, List<ClientSession> bots, Action<PasteJob> run, bool quiet, string size)
	{
		PasteGroup group = new PasteGroup { Kind = kind, Quiet = quiet };
		_parts.Clear();
		int mine = -1;
		if (YouPasteToo && jobs.Count > 0)
		{
			float me = Main.LocalPlayer.Center.X;
			mine = jobs.Select((j, i) => (Distance: Math.Abs(j.LastPosition.X - me), Index: i)).OrderBy(t => t.Distance).First().Index;
		}
		List<ClientSession> free = new List<ClientSession>(bots);
		for (int i = 0; i < jobs.Count; i++)
		{
			PasteJob job = jobs[i];
			job.Group = group;
			if (i != mine)
			{
				if (free.Count == 0)
				{
					continue;
				}
				ClientSession s = free.OrderBy(b => Math.Abs(b.Position.X - job.LastPosition.X)).First();
				free.Remove(s);
				job.Name = s.Name;
				job.PlayerIndex = s.MyIndex;
				job.Send = s.SendRaw;
				job.Disabled = () => s.TShockDisabled;
				job.TileRate = RateOf(s);
				job.MaxMode = BotMaxMode;
				_parts[s] = job;
				s.Pasting = true;
				if (Plumbers && !s.InOutfit)
				{
					s.WearOutfit(Outfit[0], Outfit[1], Outfit[2], DyeOf(s));
				}
				s.SetWorkBuff(PasteBuff);
				job.MountType = s.WorkMount;
			}
			group.Jobs.Add(job);
		}
		_group = group;
		Last = group;
		_undressAt = DateTime.MaxValue;
		foreach (PasteJob job in group.Jobs.ToList())
		{
			run(job);
		}
		if (!quiet)
		{
			int botCount = _parts.Count;
			MultiClientManager.Announce("Multi Paste: " + kind.ToLowerInvariant() + " " + size + " in " + group.Jobs.Count + " strips, " + ((mine >= 0) ? "you and " : "") + botCount + ((botCount == 1) ? " bot." : " bots."));
		}
		return group;
	}

	/// <summary>
	/// Column boundaries (0 ... width) for <paramref name="parts"/> strips with about the same work each, moved a little
	/// where needed so no object is cut in two.
	/// </summary>
	private static List<int> Split(List<TileCopy> tiles, int width, int height, int parts)
	{
		double[] cost = new double[width];
		double total = 0;
		for (int x = 0; x < width; x++)
		{
			double c = 1;
			for (int y = 0; y < height; y++)
			{
				TileCopy t = tiles[x * height + y];
				if (t == null)
				{
					continue;
				}
				if (t.active())
				{
					c += 1;
				}
				if (t.wall > 0)
				{
					c += 1;
				}
				if (t.liquid > 0)
				{
					c += 0.5;
				}
			}
			cost[x] = c;
			total += c;
		}
		List<int> cuts = new List<int> { 0 };
		int reach = Math.Max(2, width / Math.Max(1, parts) / 2);
		double acc = 0;
		int col = 0;
		for (int k = 1; k < parts; k++)
		{
			double target = total * k / parts;
			while (col < width && acc + cost[col] <= target)
			{
				acc += cost[col];
				col++;
			}
			int low = cuts[cuts.Count - 1] + 1;
			int high = width - (parts - k);
			if (low > high)
			{
				break;
			}
			int ideal = Math.Max(low, Math.Min(high, col));
			int best = ideal;
			for (int d = 0; d <= reach; d++)
			{
				if (ideal - d >= low && Cuttable(tiles, height, ideal - d))
				{
					best = ideal - d;
					break;
				}
				if (ideal + d <= high && Cuttable(tiles, height, ideal + d))
				{
					best = ideal + d;
					break;
				}
			}
			cuts.Add(best);
			acc = 0;
			for (int x = 0; x < best; x++)
			{
				acc += cost[x];
			}
			col = best;
		}
		cuts.Add(width);
		return cuts;
	}

	/// <summary>No object goes across the line before column <paramref name="column"/>.</summary>
	private static bool Cuttable(List<TileCopy> tiles, int height, int column)
	{
		for (int y = 0; y < height; y++)
		{
			TileCopy left = tiles[(column - 1) * height + y];
			TileCopy right = tiles[column * height + y];
			if (left != null && right != null && left.active() && right.active() && left.type == right.type && left.type < Main.tileFrameImportant.Length && Main.tileFrameImportant[left.type])
			{
				return false;
			}
		}
		return true;
	}

	#endregion

	#region Every tick

	/// <summary>Main thread, every tick: the ready bots come to you; a finished paste lets its bots go; outfits come off.</summary>
	internal static void Update()
	{
		PasteGroup group = _group;
		if (group != null && group.Finished)
		{
			Finish(group);
		}
		if (_group == null && DateTime.UtcNow >= _undressAt)
		{
			_undressAt = DateTime.MaxValue;
			foreach (ClientSession s in MultiClientManager.All.Where(s => s.InOutfit && !Wanted(s)))
			{
				s.TakeOffOutfit();
			}
		}
		if (Main.GameUpdateCount % 30 == 0)
		{
			KeepBuffs();
		}
		if (!Enabled)
		{
			return;
		}
		if (Main.netMode != 1 || Main.gameMenu)
		{
			Enabled = false;
			_called.Clear();
			return;
		}
		if (Main.GameUpdateCount % 30 != 0)
		{
			return;
		}
		// Ready (steps done): straight to you, once.
		foreach (ClientSession s in Helpers())
		{
			if (_called.Add(s) && !s.IsPixie)
			{
				s.Follow(Main.myPlayer);
			}
		}
	}

	/// <summary>
	/// While Multi Paste is on, its bots (in your world) wear its buff, and ride its mount when the buff is one; the
	/// others, and all of them once it is off, don't.
	/// </summary>
	private static void KeepBuffs()
	{
		foreach (ClientSession s in MultiClientManager.All)
		{
			if (s.Pasting)
			{
				continue;
			}
			bool wants = Wanted(s);
			s.InMultiPaste = wants;
			int buff = wants ? PasteBuff : 0;
			if (s.WorkBuff != buff)
			{
				s.SetWorkBuff(buff);
			}
			s.KeepOwnBuffs();
			if (wants && !s.IsPixie && !s.Dead && (s.Script == null || s.Script.Done) && !Main.LocalPlayer.dead && Vector2.Distance(s.Position, Main.LocalPlayer.Center) > TeleportTiles * 16f)
			{
				s.TeleportTo(Main.LocalPlayer);
			}
			// What everyone sees: if the bot doesn't have its buff in your world (the server replaced its buffs, e.g. its
			// saved character on /login), its mount would go on and off without end: the buffs go again (once a second).
			if (wants && buff > 0 && !s.IsPixie && s.MyIndex >= 0 && s.MyIndex < 255)
			{
				Player body = Main.player[s.MyIndex];
				if (body != null && body.active && Array.IndexOf(body.buffType, buff) < 0 && (!_buffsSentAt.TryGetValue(s, out DateTime sent) || DateTime.UtcNow - sent > TimeSpan.FromSeconds(1)))
				{
					_buffsSentAt[s] = DateTime.UtcNow;
					s.ResendBuffs();
				}
			}
			// The plumber's clothes too, all the time it is on (each bot keeps its dye); put on again whenever they are not
			// really on it any more (a bot just in, whose saved clothes the server loaded after ours), every 3 s at most.
			if (wants && Plumbers && !s.OutfitIntact)
			{
				if (!_dressedAt.TryGetValue(s, out DateTime last) || DateTime.UtcNow - last > TimeSpan.FromSeconds(3))
				{
					_dressedAt[s] = DateTime.UtcNow;
					s.WearOutfit(Outfit[0], Outfit[1], Outfit[2], DyeOf(s));
				}
			}
			else if ((!wants || !Plumbers) && s.InOutfit && _group == null && DateTime.UtcNow >= _undressAt)
			{
				s.TakeOffOutfit();
			}
		}
	}

	/// <summary>A bot of Multi Paste while it is on: in your world, ready to build with you.</summary>
	/// <summary>A bot farther from you than this (in tiles) teleports to you.</summary>
	private const int TeleportTiles = 50;

	private static bool Wanted(ClientSession s)
	{
		return Enabled && Main.netMode == 1 && !Main.gameMenu && IsCandidate(s) && s.State == ClientSessionState.InGame && s.Spawned && !s.Possessed && Possession.IsOnMyServer(s);
	}

	private static readonly Dictionary<ClientSession, int> _dyes = new Dictionary<ClientSession, int>();

	private static readonly Dictionary<ClientSession, DateTime> _dressedAt = new Dictionary<ClientSession, DateTime>();

	private static readonly Dictionary<ClientSession, DateTime> _buffsSentAt = new Dictionary<ClientSession, DateTime>();

	/// <summary>Each bot's simple dye, picked once.</summary>
	private static int DyeOf(ClientSession s)
	{
		if (!_dyes.TryGetValue(s, out int dye))
		{
			dye = SimpleDyes[_random.Next(SimpleDyes.Length)];
			_dyes[s] = dye;
		}
		return dye;
	}

	private static void Finish(PasteGroup group)
	{
		_group = null;
		foreach (KeyValuePair<ClientSession, PasteJob> part in _parts)
		{
			ClientSession s = part.Key;
			s.Pasting = false;
			if (s.State == ClientSessionState.InGame)
			{
				s.PlaceAt(part.Value.LastPosition);
			}
		}
		_parts.Clear();
		_undressAt = DateTime.UtcNow.AddSeconds(OutfitSeconds);
		try
		{
			group.OnFinished?.Invoke();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		if (!group.Quiet || group.Cancelled)
		{
			double seconds = (DateTime.UtcNow - group.Started).TotalSeconds;
			string what = group.Kind.ToLowerInvariant();
			MultiClientManager.Announce(group.Cancelled ? ("Multi Paste: " + what + " cancelled.") : ("Multi Paste: " + what + " done, " + group.Jobs.Count + " strips in " + seconds.ToString("0.#") + " s."));
		}
	}

	#endregion

	#region Saved

	internal static void Load(Preferences config)
	{
		SetId = config.Get("MultiPasteSet", "") ?? "";
		config.Get("MultiPasteBotsHere", ref UseBotsHere);
		config.Get("MultiPasteYouToo", ref YouPasteToo);
		config.Get("MultiPastePlumbers", ref Plumbers);
		PasteBuff = config.Get("MultiPasteBuff", (int)Terraria.ID.BuffID.WitchBroom);
		config.Get("MultiPasteMaxMode", ref BotMaxMode);
		DefaultRate = Math.Max(1, Math.Min(1000, config.Get("MultiPasteRate", 3)));
		Dictionary<string, int> rates = config.Get("MultiPasteRates", new Dictionary<string, int>()) ?? new Dictionary<string, int>();
		Rates = new Dictionary<string, int>(rates, StringComparer.OrdinalIgnoreCase);
	}

	internal static void Save(Preferences config)
	{
		config.Put("MultiPasteSet", SetId ?? "");
		config.Put("MultiPasteBotsHere", UseBotsHere);
		config.Put("MultiPasteYouToo", YouPasteToo);
		config.Put("MultiPastePlumbers", Plumbers);
		config.Put("MultiPasteBuff", PasteBuff);
		config.Put("MultiPasteMaxMode", BotMaxMode);
		config.Put("MultiPasteRate", DefaultRate);
		config.Put("MultiPasteRates", new Dictionary<string, int>(Rates));
	}

	#endregion
}
