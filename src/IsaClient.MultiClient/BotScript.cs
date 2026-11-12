using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;

namespace IsaClient.MultiClient;

/// <summary>
/// The steps a bot runs when it arrives on a server, one by one: a chat line, a server command ("/login {pass}"), a
/// bot step (.wait, .say, .follow, .goto, .stop, .pixie, .fight, .ai, .leave) or any other IsaClient command, which
/// the bot runs itself (<see cref="BotCommands"/>). {name}, {pass}, {n}, {id}, {set}, {me} and {server} are filled
/// in. The same steps can be given to bots at any moment (.bot run).
/// </summary>
public sealed class BotScript
{
	/// <summary>Every step a bot understands, for the editor and help: (step, what it does).</summary>
	public static readonly (string Step, string Help)[] Known =
	{
		(".wait <seconds>", "Waits before the next step"),
		(".say <text>", "Says it in the chat (plain text does too)"),
		(".follow [me|player]", "Follows you or a player (in your world)"),
		(".goto <me|x y>", "Goes to you or to a tile"),
		(".stop", "Stops moving (and stops being a pixie)"),
		(".pixie [me|player|off]", "Flies around you or a player on the Pixie mount"),
		(".fight [on|off]", "Fights enemies near it"),
		(".killaura [size]", "Enemies that close take 1500 damage (again: off)"),
		(".spamchat <text>", "Says it over and over (alone: off); .spamdelay <ticks>"),
		(".god [on|off]", "Bots take no damage; .god off and they get hurt, die and respawn"),
		(".ai [on|off]", "Answers with the AI when someone says its name"),
		(".leave", "Leaves the server")
	};

	private readonly ClientSession _bot;

	private List<string> _lines = new List<string>();

	private int _next;

	private DateTime _due;

	private bool _armed = true;

	private int _joins;

	private bool _afterDone;

	private BotScript(ClientSession bot)
	{
		_bot = bot;
	}

	/// <summary>A step it is running now ("2/5 /login"), or null.</summary>
	public string Progress => (_next < _lines.Count) ? ((_next + 1) + "/" + _lines.Count) : null;

	/// <summary>It arrived and did all its steps.</summary>
	public bool Done => !_armed && _next >= _lines.Count && DateTime.UtcNow >= _due;

	/// <summary>Main thread, every tick: starts its steps when it arrives, runs the next one when due.</summary>
	internal static void Tick(ClientSession bot, DateTime now)
	{
		BotScript script = bot.Script ??= new BotScript(bot);
		script.Step(now);
	}

	private void Step(DateTime now)
	{
		if (_bot.State != ClientSessionState.InGame || !_bot.Spawned)
		{
			if (_bot.State != ClientSessionState.InGame)
			{
				_armed = true;
				_lines.Clear();
				_next = 0;
			}
			return;
		}
		if (_bot.Possessed)
		{
			return;
		}
		if (_armed)
		{
			_armed = false;
			_joins++;
			BotSet set = BotSets.SetOf(_bot);
			bool again = (set != null) ? set.StepsOnReconnect : BotSets.AnyStepsOnReconnect;
			_lines = (_joins == 1 || again) ? StepsFor(_bot, set) : new List<string>();
			_next = 0;
			_afterDone = false;
			_due = now.AddSeconds(1.0); // a moment to settle in first
		}
		if (_next < _lines.Count)
		{
			if (now < _due)
			{
				return;
			}
			string line = _lines[_next++];
			BotSet set = BotSets.SetOf(_bot);
			float gap = (set != null) ? set.StepGap : BotSets.AnyStepGap;
			_due = now.AddSeconds(Math.Max(0.1f, gap));
			double wait = Run(_bot, line);
			if (wait > 0)
			{
				_due = now.AddSeconds(wait);
			}
			return;
		}
		if (!_afterDone)
		{
			_afterDone = true;
			BotSet set = BotSets.SetOf(_bot);
			if (set != null && set.FollowMe && Possession.IsOnMyServer(_bot) && !Main.gameMenu)
			{
				_bot.Follow(Main.myPlayer);
			}
		}
	}

	private static List<string> StepsFor(ClientSession bot, BotSet set)
	{
		List<string> lines = new List<string>();
		if (bot.Member != null)
		{
			lines.AddRange(bot.Member.Steps);
		}
		lines.AddRange((set != null) ? set.Steps : BotSets.AnySteps);
		return lines.Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#", StringComparison.Ordinal)).ToList();
	}

	/// <summary>Runs one step on <paramref name="bot"/> now (.bot run, the batch bar).</summary>
	public static void RunLine(ClientSession bot, string line)
	{
		double wait = Run(bot, line);
		if (wait > 0)
		{
			MultiClientManager.Announce(".wait only means something inside the steps.");
		}
	}

	/// <summary>Does one step; returns the seconds to wait before the next one (only .wait), else 0.</summary>
	private static double Run(ClientSession bot, string raw)
	{
		string line = Fill(bot, raw.Trim());
		if (line.Length == 0)
		{
			return 0;
		}
		if (line[0] != '.')
		{
			bot.Say(line); // chat, or a server command like /login
			return 0;
		}
		int space = line.IndexOf(' ');
		string word = ((space < 0) ? line.Substring(1) : line.Substring(1, space - 1)).ToLowerInvariant();
		string rest = (space < 0) ? "" : line.Substring(space + 1).Trim();
		string arg = rest.ToLowerInvariant();
		switch (word)
		{
		case "wait":
		case "w":
		case "sleep":
			return double.TryParse(rest.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double s) ? Math.Max(0, Math.Min(600, s)) : 1.0;
		case "say":
			bot.Say(rest);
			return 0;
		case "follow":
		{
			int target = PlayerFor(bot, rest);
			if (target >= 0)
			{
				bot.StopPixie();
				bot.Follow(target);
			}
			return 0;
		}
		case "stop":
			bot.StopPixie();
			bot.StopMoving();
			return 0;
		case "goto":
		{
			if (arg.Length == 0 || arg == "me")
			{
				if (Possession.IsOnMyServer(bot) && !Main.gameMenu)
				{
					bot.StopPixie();
					bot.GoTo(Main.LocalPlayer.position);
				}
				return 0;
			}
			string[] parts = rest.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length >= 2 && int.TryParse(parts[0], out int tx) && int.TryParse(parts[1], out int ty))
			{
				bot.StopPixie();
				bot.GoTo(new Vector2(tx * 16f, ty * 16f));
			}
			return 0;
		}
		case "pixie":
		case "fairy":
			if (arg == "off" || arg == "stop")
			{
				bot.StopPixie();
			}
			else
			{
				int target = PlayerFor(bot, rest);
				if (target >= 0)
				{
					bot.StartPixie(target);
				}
			}
			return 0;
		case "fight":
			bot.FightOverride = arg != "off";
			return 0;
		case "killaura":
		case "killbubble":
		case "ka":
			bot.SetKillAura((arg == "off") ? 0 : (int.TryParse(arg, out int size) ? size : (int?)null));
			return 0;
		case "spamchat":
			bot.SetSpam(rest);
			return 0;
		case "spamdelay":
			if (int.TryParse(arg, out int delay) && delay > 0)
			{
				bot.SpamDelay = delay;
			}
			return 0;
		case "god":
		case "godmode":
			bot.God = (arg == "on") || (arg != "off" && !bot.God);
			return 0;
		case "ai":
			bot.AiEnabled = arg != "off";
			return 0;
		case "leave":
		case "quit":
			MultiClientManager.Remove(bot);
			return 0;
		default:
		{
			// Any other IsaClient command: the bot runs it itself (in your world).
			string command = IsaClient.Core.ChatCommands.CommandSpecifier + line.Substring(1);
			if (!BotCommands.Run(bot, command, out string error))
			{
				MultiClientManager.AnnounceError(error);
			}
			return 0;
		}
		}
	}

	/// <summary>"me" or nothing = you, else a player of your world by name; -1 when there is none (or not your world).</summary>
	private static int PlayerFor(ClientSession bot, string who)
	{
		if (Main.gameMenu || !Possession.IsOnMyServer(bot))
		{
			MultiClientManager.AnnounceError(bot.Name + " is not in your world: it can only follow or fly around players there.");
			return -1;
		}
		if (who.Length == 0 || who.Equals("me", StringComparison.OrdinalIgnoreCase))
		{
			return Main.myPlayer;
		}
		Player p = IsaClient.Community.ChatTools.FindPlayer(who, out string error);
		if (p == null)
		{
			MultiClientManager.AnnounceError(error ?? ("No player " + who + "."));
			return -1;
		}
		return p.whoAmI;
	}

	/// <summary>{name}, {pass}, {n}, {id}, {set}, {me}, {server} filled in.</summary>
	public static string Fill(ClientSession bot, string line)
	{
		if (line.IndexOf('{') < 0)
		{
			return line;
		}
		BotSet set = BotSets.SetOf(bot);
		int n = (set != null && bot.Member != null) ? (set.Members.IndexOf(bot.Member) + 1) : bot.Id;
		string me = "";
		try
		{
			me = Possession.OriginalName ?? Main.LocalPlayer?.name ?? "";
		}
		catch
		{
		}
		return line.Replace("{name}", bot.Name)
			.Replace("{pass}", bot.Member?.Login ?? "")
			.Replace("{password}", bot.Member?.Login ?? "")
			.Replace("{n}", n.ToString())
			.Replace("{id}", bot.Id.ToString())
			.Replace("{set}", set?.Name ?? "")
			.Replace("{me}", me)
			.Replace("{server}", bot.Host + ":" + bot.Port);
	}
}
