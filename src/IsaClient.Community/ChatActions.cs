using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IsaClient.ChatImages;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Community;

/// <summary>
/// Anime reaction commands (.hug, .pat, .kiss, .dance...): a line in the chat ("Isa hugs Victoria!", in IsaClient's
/// language) with a random GIF of that reaction from nekos.best, sent like a message with a picture (through the pipe
/// or the chat, as Chat Pictures say).
/// </summary>
public static class ChatActions
{
	public sealed class Reaction
	{
		public string Command;

		/// <summary>nekos.best category.</summary>
		public string Category;

		/// <summary>"Isa hugs Victoria!" (you, them), in IsaClient's language.</summary>
		public Func<string, string, string> ToSomeone;

		/// <summary>"Isa wants a hug!" (alone).</summary>
		public Func<string, string> Alone;
	}

	private static Reaction A(string command, string category, Func<string, string, string> toSomeone, Func<string, string> alone)
	{
		return new Reaction { Command = command, Category = category, ToSomeone = toSomeone, Alone = alone };
	}

	public static readonly List<Reaction> All = new List<Reaction>
	{
		A("hug", "hug", (me, them) => IsaClient.Languages.L10n.F("{0} hugs {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} wants a hug!", me)),
		A("pat", "pat", (me, them) => IsaClient.Languages.L10n.F("{0} pats {1}.", me, them), me => IsaClient.Languages.L10n.F("{0} wants headpats.", me)),
		A("kiss", "kiss", (me, them) => IsaClient.Languages.L10n.F("{0} kisses {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} blows kisses to everyone!", me)),
		A("cuddle", "cuddle", (me, them) => IsaClient.Languages.L10n.F("{0} cuddles {1}.", me, them), me => IsaClient.Languages.L10n.F("{0} wants to cuddle.", me)),
		A("poke", "poke", (me, them) => IsaClient.Languages.L10n.F("{0} pokes {1}.", me, them), me => IsaClient.Languages.L10n.F("{0} pokes everyone.", me)),
		A("slap", "slap", (me, them) => IsaClient.Languages.L10n.F("{0} slaps {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} slaps the air.", me)),
		A("bite", "bite", (me, them) => IsaClient.Languages.L10n.F("{0} bites {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} bites!", me)),
		A("highfive", "highfive", (me, them) => IsaClient.Languages.L10n.F("{0} high-fives {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} wants a high five!", me)),
		A("handhold", "handhold", (me, them) => IsaClient.Languages.L10n.F("{0} holds {1}'s hand.", me, them), me => IsaClient.Languages.L10n.F("{0} wants to hold hands.", me)),
		A("handshake", "handshake", (me, them) => IsaClient.Languages.L10n.F("{0} shakes {1}'s hand.", me, them), me => IsaClient.Languages.L10n.F("{0} offers a handshake.", me)),
		A("tickle", "tickle", (me, them) => IsaClient.Languages.L10n.F("{0} tickles {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} tickles everyone!", me)),
		A("feed", "feed", (me, them) => IsaClient.Languages.L10n.F("{0} feeds {1}.", me, them), me => IsaClient.Languages.L10n.F("{0} is hungry.", me)),
		A("bonk", "bonk", (me, them) => IsaClient.Languages.L10n.F("{0} bonks {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} bonks!", me)),
		A("punch", "punch", (me, them) => IsaClient.Languages.L10n.F("{0} punches {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} punches the air!", me)),
		A("kick", "kick", (me, them) => IsaClient.Languages.L10n.F("{0} kicks {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} kicks the air!", me)),
		A("peck", "peck", (me, them) => IsaClient.Languages.L10n.F("{0} gives {1} a little kiss.", me, them), me => IsaClient.Languages.L10n.F("{0} gives a little kiss.", me)),
		A("wave", "wave", (me, them) => IsaClient.Languages.L10n.F("{0} waves at {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} waves at everyone!", me)),
		A("stare", "stare", (me, them) => IsaClient.Languages.L10n.F("{0} stares at {1}...", me, them), me => IsaClient.Languages.L10n.F("{0} stares...", me)),
		A("baka", "baka", (me, them) => IsaClient.Languages.L10n.F("{0} calls {1} baka!", me, them), me => IsaClient.Languages.L10n.F("{0}: baka!", me)),
		A("yeet", "yeet", (me, them) => IsaClient.Languages.L10n.F("{0} yeets {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} yeets!", me)),
		A("lappillow", "lappillow", (me, them) => IsaClient.Languages.L10n.F("{0} lets {1} rest on their lap.", me, them), me => IsaClient.Languages.L10n.F("{0} offers a lap pillow.", me)),
		A("carry", "carry", (me, them) => IsaClient.Languages.L10n.F("{0} carries {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} wants to be carried.", me)),
		A("kabedon", "kabedon", (me, them) => IsaClient.Languages.L10n.F("{0} corners {1} against the wall!", me, them), me => IsaClient.Languages.L10n.F("{0} leans on the wall.", me)),
		A("blowkiss", "blowkiss", (me, them) => IsaClient.Languages.L10n.F("{0} blows a kiss to {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} blows a kiss!", me)),
		A("thumbsup", "thumbsup", (me, them) => IsaClient.Languages.L10n.F("{0} gives {1} a thumbs up!", me, them), me => IsaClient.Languages.L10n.F("{0} gives a thumbs up!", me)),
		A("salute", "salute", (me, them) => IsaClient.Languages.L10n.F("{0} salutes {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} salutes!", me)),
		A("shoot", "shoot", (me, them) => IsaClient.Languages.L10n.F("{0} shoots {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} shoots!", me)),
		A("smile", "smile", (me, them) => IsaClient.Languages.L10n.F("{0} smiles at {1}.", me, them), me => IsaClient.Languages.L10n.F("{0} smiles.", me)),
		A("happy", "happy", (me, them) => IsaClient.Languages.L10n.F("{0} is happy with {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} is happy!", me)),
		A("laugh", "laugh", (me, them) => IsaClient.Languages.L10n.F("{0} laughs at {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} laughs!", me)),
		A("cry", "cry", (me, them) => IsaClient.Languages.L10n.F("{0} cries because of {1}...", me, them), me => IsaClient.Languages.L10n.F("{0} cries...", me)),
		A("dance", "dance", (me, them) => IsaClient.Languages.L10n.F("{0} dances with {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} dances!", me)),
		A("blush", "blush", (me, them) => IsaClient.Languages.L10n.F("{0} blushes at {1}.", me, them), me => IsaClient.Languages.L10n.F("{0} blushes.", me)),
		A("pout", "pout", (me, them) => IsaClient.Languages.L10n.F("{0} pouts at {1}.", me, them), me => IsaClient.Languages.L10n.F("{0} pouts.", me)),
		A("shrug", "shrug", (me, them) => IsaClient.Languages.L10n.F("{0} shrugs at {1}.", me, them), me => IsaClient.Languages.L10n.F("{0} shrugs.", me)),
		A("sleep", "sleep", (me, them) => IsaClient.Languages.L10n.F("{0} falls asleep on {1}.", me, them), me => IsaClient.Languages.L10n.F("{0} falls asleep.", me)),
		A("think", "think", (me, them) => IsaClient.Languages.L10n.F("{0} thinks about {1}...", me, them), me => IsaClient.Languages.L10n.F("{0} is thinking...", me)),
		A("facepalm", "facepalm", (me, them) => IsaClient.Languages.L10n.F("{0} facepalms at {1}.", me, them), me => IsaClient.Languages.L10n.F("{0} facepalms.", me)),
		A("nod", "nod", (me, them) => IsaClient.Languages.L10n.F("{0} nods at {1}.", me, them), me => IsaClient.Languages.L10n.F("{0} nods.", me)),
		A("nope", "nope", (me, them) => IsaClient.Languages.L10n.F("{0} says nope to {1}.", me, them), me => IsaClient.Languages.L10n.F("{0}: nope.", me)),
		A("clap", "clap", (me, them) => IsaClient.Languages.L10n.F("{0} claps for {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} claps!", me)),
		A("angry", "angry", (me, them) => IsaClient.Languages.L10n.F("{0} is angry with {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} is angry!", me)),
		A("bored", "bored", (me, them) => IsaClient.Languages.L10n.F("{0} is bored of {1}.", me, them), me => IsaClient.Languages.L10n.F("{0} is bored.", me)),
		A("yawn", "yawn", (me, them) => IsaClient.Languages.L10n.F("{0} yawns at {1}.", me, them), me => IsaClient.Languages.L10n.F("{0} yawns.", me)),
		A("wink", "wink", (me, them) => IsaClient.Languages.L10n.F("{0} winks at {1}.", me, them), me => IsaClient.Languages.L10n.F("{0} winks.", me)),
		A("smug", "smug", (me, them) => IsaClient.Languages.L10n.F("{0} looks smugly at {1}.", me, them), me => IsaClient.Languages.L10n.F("{0} looks smug.", me)),
		A("confused", "confused", (me, them) => IsaClient.Languages.L10n.F("{0} is confused by {1}.", me, them), me => IsaClient.Languages.L10n.F("{0} is confused.", me)),
		A("shocked", "shocked", (me, them) => IsaClient.Languages.L10n.F("{0} is shocked by {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} is shocked!", me)),
		A("run", "run", (me, them) => IsaClient.Languages.L10n.F("{0} runs from {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} runs away!", me)),
		A("sip", "sip", (me, them) => IsaClient.Languages.L10n.F("{0} sips tea looking at {1}.", me, them), me => IsaClient.Languages.L10n.F("{0} sips tea.", me)),
		A("nom", "nom", (me, them) => IsaClient.Languages.L10n.F("{0} noms {1}!", me, them), me => IsaClient.Languages.L10n.F("{0}: nom nom.", me)),
		A("teehee", "teehee", (me, them) => IsaClient.Languages.L10n.F("{0} giggles at {1}.", me, them), me => IsaClient.Languages.L10n.F("{0}: teehee.", me)),
		A("wag", "wag", (me, them) => IsaClient.Languages.L10n.F("{0} wags at {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} wags happily!", me)),
		A("tableflip", "tableflip", (me, them) => IsaClient.Languages.L10n.F("{0} flips a table at {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} flips a table!", me)),
		A("spin", "spin", (me, them) => IsaClient.Languages.L10n.F("{0} spins around {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} spins!", me)),
		A("shake", "shake", (me, them) => IsaClient.Languages.L10n.F("{0} shakes {1}!", me, them), me => IsaClient.Languages.L10n.F("{0} shakes!", me)),
		A("bleh", "bleh", (me, them) => IsaClient.Languages.L10n.F("{0} sticks their tongue out at {1}.", me, them), me => IsaClient.Languages.L10n.F("{0}: bleh!", me)),
		A("nya", "nya", (me, them) => IsaClient.Languages.L10n.F("{0} meows at {1}: nya!", me, them), me => IsaClient.Languages.L10n.F("{0}: nya!", me)),
		A("lurk", "lurk", (me, them) => IsaClient.Languages.L10n.F("{0} lurks behind {1}...", me, them), me => IsaClient.Languages.L10n.F("{0} lurks...", me))
	};

	/// <summary>Registers every action as a chat command, plus .actions (the list).</summary>
	public static void Register()
	{
		foreach (Reaction action in All)
		{
			Reaction a = action;
			CommandRegistry.Register(command => Run(a, command), a.Command)
				.Help(CommandSection.Chat, "[player]", "Says it in the chat with an anime GIF (nekos.best).");
		}
		CommandRegistry.Register(ListCommand, "actions", "reactions")
			.Help(CommandSection.Chat, "", "Lists the anime reaction commands (.hug, .pat, .kiss...): each says it in the chat with a GIF.");
	}

	private static bool _busy;

	private static void Run(Reaction action, CommandContext command)
	{
		if (Main.netMode == 0 && Main.gameMenu)
		{
			return;
		}
		if (_busy)
		{
			ClientChat.Notice("Still getting the last GIF...");
			return;
		}
		string me = Main.LocalPlayer?.name ?? "";
		string rest = command.Rest.Trim();
		string target = null;
		if (rest.Length > 0)
		{
			Player player = ChatTools.FindPlayer(rest, out _);
			target = (player != null) ? player.name : rest;
		}
		_busy = true;
		Task.Run(delegate
		{
			string gif = null;
			string error = null;
			try
			{
				gif = GifSources.RandomAnime(action.Category);
			}
			catch (Exception ex)
			{
				error = ex.Message;
			}
			Main.QueueMainThreadAction(delegate
			{
				_busy = false;
				if (gif == null)
				{
					// The reaction goes anyway.
					ClientChat.Notice("Couldn't get a GIF" + ((error != null) ? (": " + error) : "") + ".");
				}
				// Only for IsaClient players (the pipe): each sees it in their own language, like a line from the server.
				if (Net.Pipe.PipeNet.CanSend)
				{
					PipeChat.SendReaction(action.Command, target ?? "", gif);
					return;
				}
				// The pipe off: in the server's chat, as words only IsaClient reads; it shows them the same line.
				List<byte[]> records = new List<byte[]> { ChatData.ReactionRecord(action.Command, me, target ?? "") };
				if (gif != null)
				{
					records.Add(ChatData.LinkRecord(gif));
				}
				List<string> parts = ChatData.Encode(records);
				if (parts != null)
				{
					ChatData.SendAlone(parts);
					return;
				}
				string text = (target != null) ? action.ToSomeone(me, target) : action.Alone(me);
				ChatAttachments.SendNow(text, (gif != null) ? new List<string> { gif } : new List<string>());
			});
		});
	}

	public static Reaction Find(string command)
	{
		return All.FirstOrDefault(a => a.Command == command);
	}

	private static readonly Microsoft.Xna.Framework.Color LineColor = new Microsoft.Xna.Framework.Color(255, 196, 230);

	private const string ByColor = "FFD66E";

	private const string ToColor = "8ED2FF";

	/// <summary>
	/// A reaction as a chat line in this client's language, the names colored, nobody "saying" it: shown with its GIF
	/// under it (a link the chat turns into the picture).
	/// </summary>
	public static void Show(Reaction reaction, string by, string to, string gif)
	{
		string text = Line(reaction.Command, by, to);
		if (!string.IsNullOrEmpty(gif) && ChatImages.ChatImages.IsImageLink(gif))
		{
			text += " " + gif;
		}
		Main.NewText(text, LineColor.R, LineColor.G, LineColor.B);
	}

	/// <summary>A reaction's line, in your language and its colors ("Isa hugs Victoria!"); null for an unknown one.</summary>
	public static string Line(string command, string by, string to)
	{
		Reaction reaction = Find(command);
		if (reaction == null)
		{
			return null;
		}
		string me = "[c/" + ByColor + ":" + Tag(by) + "]";
		string them = string.IsNullOrEmpty(to) ? null : ("[c/" + ToColor + ":" + Tag(to) + "]");
		return ChatColor.Colorize((them != null) ? reaction.ToSomeone(me, them) : reaction.Alone(me), LineColor);
	}

	/// <summary>A name inside a color tag: ']' would end it.</summary>
	private static string Tag(string name)
	{
		return (name ?? "").Replace("]", "\\]");
	}

	private static void ListCommand(CommandContext command)
	{
		ClientChat.Info(IsaClient.Languages.L10n.T("Reactions (with an anime GIF): ") + string.Join(" ", All.Select(a => "." + a.Command)) + IsaClient.Languages.L10n.T("  ·  .gif opens the GIF search."));
	}
}
