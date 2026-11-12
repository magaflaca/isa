#nullable enable
using System.Text;
using IsaClient.Community;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Net.Pipe;

/// <summary>
/// A tiny <c>.pipe</c> command to try the link out and confirm pings reach other IsaClient players on a given server,
/// before anything is built on top. It uses its own demo channel, so it never clashes with a real feature's channel.
/// </summary>
public static class PipeCommands
{
	/// <summary>Reserved for this demo. Real features should pick their own channel.</summary>
	public const byte DemoChannel = 250;

	private static bool _registered;

	/// <summary>What stops working with the pipe off (the panel and .pipe off show it before asking).</summary>
	public static readonly string[] OffStops =
	{
		"Games with other players (Tic Tac Toe, Chess, Tetris Versus) stop working, and your scores aren't shared or ranked.",
		"The IsaClient chat (.inchat, .w, .r) and the reactions only IsaClient players see stop.",
		"IsaClient players stop seeing you as one (player list, ids), and you stop seeing them.",
		"IsaClient items you link in chat show to others as the plain item.",
		"You can't answer or ask again: if a piece of something sent to you gets lost on the way, it doesn't arrive."
	};

	/// <summary>What still works with the pipe off, another way.</summary>
	public static readonly string[] OffChanges =
	{
		"Pictures and GIFs go in the server's chat as a few short words: everyone sees the words, IsaClient players see the picture.",
		"Characters and schematics go hidden inside an Imgur picture (a spare copy goes to archive.org). Styles go as a few short chat lines.",
		"Their message waits a few seconds, until the picture is up and checked.",
		"Bigger things take several short lines in the chat (IsaClient hides them).",
		"You still receive: it only listens. Pictures, messages and shared things others send through the pipe reach you; nothing goes out from you."
	};

	/// <summary>Turns the pipe on or off (saved) and says so.</summary>
	public static void SetEnabled(bool on)
	{
		PipeNet.Enabled = on;
		ClientSettings.Save();
		ClientChat.Info(on ? "Pipe on: data goes between IsaClient players again." : "Pipe off: nothing goes out through it (it still listens). Pictures, GIFs and shared things go in the chat as short codes.");
	}

	/// <summary>Subscribes the demo receiver. Called once at startup so both ends can receive.</summary>
	public static void Register()
	{
		if (_registered)
		{
			return;
		}
		_registered = true;
		PipeNet.OnMessage += OnMessage;
	}

	private static void OnMessage(PipeInbound m)
	{
		if (m.Channel != DemoChannel)
		{
			return;
		}
		string name = (m.FromPlayer >= 0 && m.FromPlayer < 255 && Main.player[m.FromPlayer] != null) ? Main.player[m.FromPlayer].name : ("#" + m.FromPlayer);
		string text = Encoding.UTF8.GetString(m.Data);
		ClientChat.Info("[pipe] " + name + (m.Broadcast ? " (all)" : "") + ": " + text);
	}

	/// <summary>.pipe [ on | off | trace | all &lt;text&gt; | &lt;player&gt; &lt;text&gt; ]</summary>
	public static void PipeCommand(CommandContext command)
	{
		string[] parts = command.Parts;
		string sub = (parts.Length > 1) ? parts[1].ToLowerInvariant() : "";
		if (sub == "")
		{
			ClientChat.Info("Pipe is " + (PipeNet.Enabled ? "on" : "off") + ", " + (PipeNet.Ready ? "connected" : "not connected") + ", trace " + (PipeNet.Trace ? "on" : "off") + ". Usage: .pipe <player> <text> | all <text> | on | off | trace");
			return;
		}
		if (sub == "on")
		{
			SetEnabled(true);
			return;
		}
		if (sub == "off")
		{
			// It turns a lot off: say what first, and ask.
			if (PipeNet.Enabled && (parts.Length < 3 || parts[2].ToLowerInvariant() != "sure"))
			{
				ClientChat.Notice("Turning the pipe off stops everything that goes between IsaClient players:");
				foreach (string line in OffStops)
				{
					ClientChat.Info("- " + line);
				}
				ClientChat.Notice("What still works, another way:");
				foreach (string line in OffChanges)
				{
					ClientChat.Info("- " + line);
				}
				ClientChat.Notice("Sure? Type .pipe off sure (or use the panel: Multiplayer, IsaClient Pipe).");
				return;
			}
			SetEnabled(false);
			return;
		}
		if (sub == "trace")
		{
			PipeNet.Trace = !PipeNet.Trace;
			ClientSettings.Save();
			ClientChat.Info("Pipe trace " + (PipeNet.Trace ? "on" : "off") + ".");
			return;
		}
		if (Main.netMode != 1)
		{
			ClientChat.Error("Join a server first: the pipe needs other players.");
			return;
		}
		if (sub == "all")
		{
			string text = command.Rest.Length > 4 ? command.Rest.Substring(4) : "";
			if (text.Length == 0)
			{
				ClientChat.Error("Usage: .pipe all <text>");
				return;
			}
			PipeNet.Broadcast(DemoChannel, Encoding.UTF8.GetBytes(text));
			ClientChat.Info("Sent to everyone: " + text);
			return;
		}
		Player target = ChatTools.FindPlayer(parts[1], out string error);
		if (target == null)
		{
			ClientChat.Error(error);
			return;
		}
		int at = command.Rest.IndexOf(' ');
		string message = (at >= 0) ? command.Rest.Substring(at + 1) : "";
		if (message.Length == 0)
		{
			ClientChat.Error("Usage: .pipe <player> <text>");
			return;
		}
		PipeNet.SendTo(target.whoAmI, DemoChannel, Encoding.UTF8.GetBytes(message));
		ClientChat.Info("Sent to " + target.name + ": " + message);
	}
}
