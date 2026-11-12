using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.UI.Chat;

namespace IsaClient.Community;

/// <summary>
/// Chat pictures that came through the pipe are put on the chat message they were sent with, so they show exactly as
/// pictures sent in the chat did: under that message, with the server's own format (prefixes, colors). The text goes
/// through the server and the pictures through the pipe, in either order: pictures look back at the messages of the last
/// seconds and, if theirs hasn't come yet, wait for it. Pictures sent without text become a message of their own, in
/// the sender's format when one of their messages showed it before.
/// </summary>
public static class PipePictures
{
	private sealed class Waiting
	{
		public string Name;

		public string Text;

		public List<string> Links;

		public double Until;
	}

	private sealed class Seen
	{
		public ChatMessageContainer Message;

		public double At;
	}

	/// <summary>How far back (and how long ahead) a message is matched with its pictures, in ticks.</summary>
	private const double Window = 15.0 * 60.0;

	/// <summary>Pictures sent without text wait this long for their blank line.</summary>
	private const double BlankWindow = 4.0 * 60.0;

	private static readonly List<Seen> _recent = new List<Seen>();

	private static readonly List<Waiting> _waiting = new List<Waiting>();

	/// <summary>Each player's message format: the text before what they wrote, and its color.</summary>
	private static readonly Dictionary<string, KeyValuePair<string, Color>> _formats = new Dictionary<string, KeyValuePair<string, Color>>();

	private static bool _addingOwn;

	private static double Now => Main.timeForVisualEffects;

	/// <summary>A new chat message (RemadeChatMonitor): waiting pictures may be its.</summary>
	public static void OnChatMessage(ChatMessageContainer message)
	{
		if (_addingOwn || message == null)
		{
			return;
		}
		_recent.Add(new Seen { Message = message, At = Now });
		_recent.RemoveAll(s => Now - s.At > Window * 2);
		if (_recent.Count > 60)
		{
			_recent.RemoveRange(0, _recent.Count - 60);
		}
		for (int i = 0; i < _waiting.Count; i++)
		{
			if (Matches(message, _waiting[i].Name, _waiting[i].Text, exact: false))
			{
				Attach(message, _waiting[i]);
				_waiting.RemoveAt(i);
				return;
			}
		}
	}

	/// <summary>Pictures from a player (you too): onto their message, or a message of their own.</summary>
	public static void Received(int player, string text, List<string> links)
	{
		if (links == null || links.Count == 0)
		{
			return;
		}
		string name = (player >= 0 && player < 255) ? (Main.player[player]?.name ?? "") : "";
		Waiting pictures = new Waiting
		{
			Name = name,
			Text = IsaClient.Core.ChatText.Plain((text ?? "").Trim()),
			Links = links,
			Until = Now + Window
		};
		if (name.Length == 0)
		{
			ShowAlone(pictures);
			return;
		}
		if (pictures.Text.Length == 0)
		{
			// Sent alone: they go on the blank line sent with them (older IsaClients send none: don't wait long).
			pictures.Until = Now + BlankWindow;
		}
		// Their message may be here already: the newest that has their text, else the newest with their name.
		foreach (bool exact in new[] { true, false })
		{
			for (int i = _recent.Count - 1; i >= 0; i--)
			{
				Seen seen = _recent[i];
				if (Now - seen.At <= Window && Matches(seen.Message, name, pictures.Text, exact))
				{
					Attach(seen.Message, pictures);
					return;
				}
			}
		}
		_waiting.Add(pictures);
	}

	/// <summary>Every frame: pictures whose message never came show on their own.</summary>
	public static void Update()
	{
		for (int i = _waiting.Count - 1; i >= 0; i--)
		{
			if (Now > _waiting[i].Until)
			{
				Waiting pictures = _waiting[i];
				_waiting.RemoveAt(i);
				if (pictures.Text.Length == 0 || !AttachToNewestOf(pictures))
				{
					ShowAlone(pictures);
				}
			}
		}
		if (Main.gameMenu && (_recent.Count > 0 || _waiting.Count > 0))
		{
			_recent.Clear();
			_waiting.Clear();
		}
	}

	private static bool Matches(ChatMessageContainer message, string name, string text, bool exact)
	{
		string original = message.OriginalText ?? "";
		if (message.HasPipePictures || name.Length == 0)
		{
			return false;
		}
		int at = original.IndexOf(name, System.StringComparison.Ordinal);
		// The name is in the message's head (prefixes and the name, before what they wrote).
		if (at < 0 || at > 120)
		{
			return false;
		}
		if (text == null)
		{
			return true;
		}
		if (text.Length == 0)
		{
			return Blank(original);
		}
		if (exact)
		{
			return original.EndsWith(text, System.StringComparison.Ordinal);
		}
		// Loose: the end of what they wrote is in it (colored chat wraps it in tags; ']' gets escaped).
		string tail = (text.Length > 12) ? text.Substring(text.Length - 12) : text;
		return tail.Length == 0 || tail.IndexOf(']') >= 0 || original.Contains(tail);
	}

	/// <summary>Last chance (their text may have been translated): the newest message of theirs.</summary>
	private static bool AttachToNewestOf(Waiting pictures)
	{
		for (int i = _recent.Count - 1; i >= 0; i--)
		{
			Seen seen = _recent[i];
			if (Now - seen.At <= Window * 2 && Matches(seen.Message, pictures.Name, null, exact: false))
			{
				Attach(seen.Message, pictures);
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// The blank line sent with pictures: the server's format and nothing after it. Formats end in what goes before the
	/// text ("<Name> ", "Name Lv. 118: "), so the line ends in a space, a ':' or a '>'.
	/// </summary>
	private static bool Blank(string original)
	{
		string trimmed = original.TrimEnd();
		return original.EndsWith(" ", System.StringComparison.Ordinal) || trimmed.EndsWith(":", System.StringComparison.Ordinal) || trimmed.EndsWith(">", System.StringComparison.Ordinal);
	}

	private static void Attach(ChatMessageContainer message, Waiting pictures)
	{
		string original = message.OriginalText ?? "";
		if (pictures.Text.Length > 0 && original.EndsWith(pictures.Text, System.StringComparison.Ordinal) && original.Length > pictures.Text.Length)
		{
			_formats[pictures.Name] = new KeyValuePair<string, Color>(original.Substring(0, original.Length - pictures.Text.Length), message.Color);
		}
		message.AddPipePictures(string.Join(" ", pictures.Links));
	}

	private static void ShowAlone(Waiting pictures)
	{
		string links = string.Join(" ", pictures.Links);
		_addingOwn = true;
		try
		{
			if (_formats.TryGetValue(pictures.Name, out KeyValuePair<string, Color> format))
			{
				Main.NewText(format.Key + links, format.Value.R, format.Value.G, format.Value.B);
			}
			else
			{
				Main.NewText("<" + pictures.Name + "> " + links);
			}
		}
		finally
		{
			_addingOwn = false;
		}
	}
}
