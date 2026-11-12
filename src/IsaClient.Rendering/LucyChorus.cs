using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IsaClient.Community;
using IsaClient.Core;
using IsaClient.Net.Pipe;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI;
using Terraria.ID;

namespace IsaClient.Rendering;

/// <summary>
/// Lucy the Axe talking over someone: one line, or many a second until stopped, like a chorus. Lucy's own lines go as
/// the game's Lucy message (packet 141): the server passes it to everyone and each player reads it in their own
/// language. That packet only carries which line, never text, so your own lines go to IsaClient players through the
/// pipe (one message a second with that second's lines: the pipe is slow) and show the same way, Lucy's color and
/// voice. Players without IsaClient get one of Lucy's lines in their place: sent with a line number past the real ones
/// (the game wraps it round to a real line), which IsaClient recognizes and doesn't show, since it shows yours.
/// <list type="bullet">
/// <item>.lucy: one line over you. .lucy &lt;player|me|mouse|all&gt; [per second]: one line, or that many a second.</item>
/// <item>.lucy stop (or .lucy while it runs) stops it. .lucy say &lt;text&gt;: your text over you, once.</item>
/// <item>.lucy add &lt;text&gt; / .lucy lines / .lucy remove &lt;number&gt; / .lucy clear: your own lines.</item>
/// </list>
/// </summary>
public static class LucyChorus
{
	public const byte Channel = 77;

	/// <summary>Line numbers from here are stand-ins for lines of your own (see the class summary).</summary>
	public const int StandIn = 200;

	/// <summary>Lucy's color (the game's).</summary>
	public static readonly Color LucyColor = new Color(184, 96, 98) * 1.15f;

	public static bool On;

	[Setting("LucyRate")]
	public static int Rate = 4;

	/// <summary>0 Lucy's lines, 1 your lines, 2 both mixed.</summary>
	[Setting("LucyLinesMode", Min = 0, Max = 2)]
	public static int Lines;

	/// <summary>Which of Lucy's lines: 0 any, else the game's kind + 1 (idle, storage, thrown away...).</summary>
	[Setting("LucyKind", Min = 0, Max = 7)]
	public static int Kind;

	/// <summary>With your own lines: players without IsaClient see one of Lucy's lines in their place.</summary>
	[Setting("LucyOthersSeeLucy")]
	public static bool OthersSeeLucy = true;

	/// <summary>Your own lines, one per line.</summary>
	[Setting("LucyCustomLines")]
	public static string Custom = "Hey! Chop some trees!\nWe make a good team!\nChop chop chop. Heh.";

	/// <summary>-1 you, -2 the mouse, -3 everyone, else a player slot.</summary>
	public static int Target = -1;

	public static readonly string[] LineModes = { "Lucy's lines", "Your lines", "Both, mixed" };

	public static readonly string[] Kinds = { "Any", "Idle", "Storage", "Thrown away", "Picked up", "Chopped a tree", "Gem tree", "Cactus" };

	public static readonly string[] Targets = { "You", "The mouse", "Everyone" };

	private static float _due;

	private static int _tick;

	private static readonly List<string> _batch = new List<string>();

	private static bool _hooked;

	private sealed class Waiting
	{
		public int At;

		public int Target;

		public Vector2? Point;

		public string Text;
	}

	/// <summary>Lines of others waiting to show (they come a second's worth at a time and are spread over it).</summary>
	private static readonly List<Waiting> _waiting = new List<Waiting>();

	public static List<string> CustomLines => (Custom ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

	public static void SetCustomLines(IEnumerable<string> lines)
	{
		Custom = string.Join("\n", lines.Select(l => l.Replace("\n", " ").Trim()).Where(l => l.Length > 0));
		ClientSettings.Save();
	}

	public static string TargetName => Target switch
	{
		-1 => "you",
		-2 => "the mouse",
		-3 => "everyone",
		_ => (Target >= 0 && Target < 255 && Main.player[Target].active) ? Main.player[Target].name : "you"
	};

	/// <summary>Packet 141 from someone: false when it's a stand-in for their own line (shown from the pipe instead).</summary>
	public static bool ShowsNetLine(byte variation) => variation < StandIn;

	#region Command

	/// <summary>.lucy [player|me|mouse|all] [per second] | stop | say &lt;text&gt; | add &lt;text&gt; | lines | remove &lt;n&gt; | clear</summary>
	public static void LucyCommand(CommandContext command)
	{
		string[] parts = command.Parts;
		string first = (parts.Length > 1) ? parts[1].ToLowerInvariant() : "";
		string rest = (parts.Length > 2) ? string.Join(" ", parts.Skip(2)) : "";
		switch (first)
		{
		case "":
			if (On)
			{
				Stop();
			}
			else
			{
				One(-1);
			}
			return;
		case "stop":
		case "off":
			Stop();
			return;
		case "say":
			if (rest.Length == 0)
			{
				ClientChat.Error("Usage: .lucy say <text>");
				return;
			}
			Popup(-1, rest);
			Send(-1, new List<string> { rest });
			return;
		case "add":
			if (rest.Length == 0)
			{
				ClientChat.Error("Usage: .lucy add <text>");
				return;
			}
			SetCustomLines(CustomLines.Concat(new[] { rest }));
			ClientChat.Info("Lucy line added (" + CustomLines.Count + " now). .lucy lines lists them.");
			return;
		case "lines":
		{
			List<string> lines = CustomLines;
			ClientChat.Info((lines.Count == 0) ? "No lines of yours yet: .lucy add <text>." : ("Your Lucy lines: " + string.Join("  ", lines.Select((l, i) => "[" + (i + 1) + "] " + l))));
			return;
		}
		case "remove":
		{
			List<string> lines = CustomLines;
			if (!int.TryParse(rest, out int n) || n < 1 || n > lines.Count)
			{
				ClientChat.Error("Usage: .lucy remove <number> (see .lucy lines).");
				return;
			}
			lines.RemoveAt(n - 1);
			SetCustomLines(lines);
			ClientChat.Info("Removed. " + lines.Count + " left.");
			return;
		}
		case "clear":
			SetCustomLines(new string[0]);
			ClientChat.Info("Your Lucy lines are cleared.");
			return;
		}
		int target = ParseTarget(parts[1], out string error);
		if (error != null)
		{
			ClientChat.Error(error);
			return;
		}
		if (parts.Length > 2 && int.TryParse(parts[2], out int rate))
		{
			Rate = Math.Max(1, rate);
			ClientSettings.Save();
			Start(target);
			return;
		}
		One(target);
	}

	private static int ParseTarget(string word, out string error)
	{
		error = null;
		switch (word.ToLowerInvariant())
		{
		case "me":
			return -1;
		case "mouse":
		case "cursor":
			return -2;
		case "all":
		case "everyone":
			return -3;
		}
		Player p = ChatTools.FindPlayer(word, out error);
		return (p != null) ? p.whoAmI : -1;
	}

	public static void Start(int target)
	{
		Target = target;
		On = true;
		_due = 0f;
		ClientChat.Info("Lucy: " + Rate + " a second over " + TargetName + ". .lucy stop stops her.");
	}

	public static void Stop()
	{
		if (On)
		{
			ClientChat.Info("Lucy is quiet now.");
		}
		On = false;
	}

	#endregion

	#region Talking

	/// <summary>Every tick.</summary>
	public static void Update()
	{
		if (!_hooked)
		{
			_hooked = true;
			PipeNet.OnMessage += OnPipe;
		}
		_tick++;
		ShowWaiting();
		if (!On || Main.gameMenu || Main.netMode == 2)
		{
			_batch.Clear();
			return;
		}
		if (Target >= 0 && (Target >= 255 || !Main.player[Target].active))
		{
			ClientChat.Info("Lucy's target left: she stops.");
			On = false;
			return;
		}
		_due += Math.Max(1, Rate) / 60f;
		while (_due >= 1f)
		{
			_due -= 1f;
			Talk(Target, _batch);
		}
		// Your lines go to IsaClient players once a second, all together.
		if (_tick % 60 == 0 && _batch.Count > 0)
		{
			Send(Target, _batch.ToList());
			_batch.Clear();
		}
	}

	/// <summary>A line of yours over the current target, now (here and to IsaClient players).</summary>
	public static void SayNow(string text)
	{
		Popup(Target, text);
		Send(Target, new List<string> { text });
	}

	/// <summary>One line over someone now.</summary>
	public static void One(int target)
	{
		List<string> mine = new List<string>();
		Talk(target, mine);
		Send(target, mine);
	}

	/// <summary>
	/// One line: Lucy's (the game's message: everyone sees it), or yours (shown here now, kept for the next pipe
	/// message, with a stand-in Lucy line for players without IsaClient).
	/// </summary>
	private static void Talk(int target, List<string> batch)
	{
		Vector2 position = PositionOver(target);
		Vector2 velocity = Velocity();
		LucyAxeMessage.MessageSource source = (Kind == 0) ? (LucyAxeMessage.MessageSource)Main.rand.Next(7) : (LucyAxeMessage.MessageSource)(Kind - 1);
		List<string> custom = CustomLines;
		if (custom.Count > 0 && (Lines == 1 || (Lines == 2 && Main.rand.Next(2) == 0)))
		{
			string line = custom[Main.rand.Next(custom.Count)];
			PopupAt(position, line);
			batch.Add(line);
			if (OthersSeeLucy && Main.netMode == 1)
			{
				// Without the pipe IsaClient players can't get yours: then they see the stand-in too (a real line number).
				int number = PipeNet.CanSend ? (StandIn + Main.rand.Next(50)) : Main.rand.Next(99);
				NetMessage.SendData(141, -1, -1, null, (int)source, number, velocity.X, velocity.Y, (int)position.X, (int)position.Y);
			}
			return;
		}
		LucyAxeMessage.Create(source, position, velocity, forced: true, (byte)Main.rand.Next(99));
	}

	/// <summary>Your lines to IsaClient players: one pipe message (they spread them over a second).</summary>
	private static void Send(int target, List<string> lines)
	{
		if (!PipeNet.CanSend || lines.Count == 0)
		{
			return;
		}
		Vector2 mouse = Main.MouseWorld;
		List<byte> data = new List<byte> { 1, (byte)((target >= 0) ? target : ((target == -1) ? Main.myPlayer : 255)), (byte)((target == -3) ? 1 : 0) };
		data.AddRange(BitConverter.GetBytes((int)mouse.X));
		data.AddRange(BitConverter.GetBytes((int)mouse.Y));
		foreach (string line in lines)
		{
			byte[] text = Encoding.UTF8.GetBytes((line.Length > 80) ? line.Substring(0, 80) : line);
			if (data.Count + 1 + text.Length > 700)
			{
				break;
			}
			data.Add((byte)text.Length);
			data.AddRange(text);
		}
		PipeNet.Broadcast(Channel, data.ToArray());
	}

	private static void OnPipe(PipeInbound m)
	{
		if (m.Channel != Channel || m.Data == null || m.Data.Length < 11 || m.Data[0] != 1)
		{
			return;
		}
		try
		{
			int over = m.Data[1];
			bool everyone = m.Data[2] == 1;
			Vector2 mouse = new Vector2(BitConverter.ToInt32(m.Data, 3), BitConverter.ToInt32(m.Data, 7));
			List<string> lines = new List<string>();
			int at = 11;
			while (at < m.Data.Length)
			{
				int n = m.Data[at++];
				if (at + n > m.Data.Length)
				{
					break;
				}
				lines.Add(Encoding.UTF8.GetString(m.Data, at, n));
				at += n;
			}
			for (int i = 0; i < lines.Count && _waiting.Count < 600; i++)
			{
				_waiting.Add(new Waiting
				{
					At = _tick + i * 60 / lines.Count,
					Target = everyone ? -3 : over,
					Point = (!everyone && over == 255) ? mouse : (Vector2?)null,
					Text = lines[i]
				});
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static void ShowWaiting()
	{
		for (int i = _waiting.Count - 1; i >= 0; i--)
		{
			Waiting w = _waiting[i];
			if (w.At > _tick)
			{
				continue;
			}
			_waiting.RemoveAt(i);
			if (Main.gameMenu)
			{
				continue;
			}
			if (w.Point.HasValue)
			{
				PopupAt(w.Point.Value, w.Text);
			}
			else
			{
				Popup(w.Target, w.Text);
			}
		}
	}

	#endregion

	#region Showing

	private static Vector2 Velocity() => new Vector2(Main.rand.NextFloatDirection() * 7f, -2f + Main.rand.NextFloat() * -2f);

	/// <summary>Where a line shows: over a player's head, the mouse, or anyone's (everyone).</summary>
	private static Vector2 PositionOver(int target)
	{
		if (target == -2)
		{
			return Main.MouseWorld;
		}
		if (target == -3)
		{
			List<Player> players = Main.player.Take(255).Where(p => p != null && p.active && !p.dead).ToList();
			return (players.Count > 0) ? players[Main.rand.Next(players.Count)].Top : Main.LocalPlayer.Top;
		}
		Player who = (target >= 0 && target < 255 && Main.player[target].active) ? Main.player[target] : Main.LocalPlayer;
		return who.Top;
	}

	private static void Popup(int target, string text)
	{
		PopupAt(PositionOver(target), text);
	}

	/// <summary>A line the way Lucy's show: her color, rising, with her voice.</summary>
	private static void PopupAt(Vector2 position, string text)
	{
		if (Main.dedServ || string.IsNullOrEmpty(text))
		{
			return;
		}
		PopupText.NewText(new AdvancedPopupRequest
		{
			Text = text,
			DurationInFrames = 420,
			Velocity = Velocity(),
			Color = LucyColor
		}, position);
		SoundEngine.PlaySound(SoundID.LucyTheAxeTalk, position);
	}

	#endregion
}
