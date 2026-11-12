using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IsaClient.Core;
using IsaClient.Net.Pipe;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;

namespace IsaClient.Community;

/// <summary>
/// Chat between IsaClient players over the pipe (channel <see cref="Channel"/>), never through the server: .whisper sends
/// one player a private message; .inchat switches what you type to the IsaClient chat (everyone with IsaClient here),
/// or sends one line there. Message: [kind, UTF-8 text]. The pipe is a trickle: lines are kept short.
/// </summary>
public static class PipeChat
{
	/// <summary>The pipe channel of chat ('H').</summary>
	public const byte Channel = 72;

	private const byte Whisper = 1;

	private const byte Room = 2;

	/// <summary>
	/// [count, then per picture: n + its n code bytes, or 0 + length + the link in UTF-8, then the text they were sent
	/// with (UTF-8, short) so they find their message]: chat pictures.
	/// </summary>
	private const byte Pictures = 3;

	/// <summary>[command length, command, target length, target, the GIF link]: a reaction (.hug...), shown in each client's language.</summary>
	private const byte Reaction = 4;

	/// <summary>Longest line sent (in characters): room for a sentence and a couple of picture codes.</summary>
	public const int MaxLength = 240;

	/// <summary>While on, what you type goes to the IsaClient chat instead of the server.</summary>
	public static bool InChat;

	private static readonly Color WhisperColor = new Color(255, 160, 230);

	private static readonly Color RoomColor = new Color(130, 230, 255);

	private static bool _hooked;

	/// <summary>Who .reply answers: the last player who whispered you (or you whispered), by slot and name.</summary>
	private static int _replyTo = -1;

	private static string _replyName;

	public static void Update()
	{
		if (!_hooked)
		{
			_hooked = true;
			PipeNet.OnMessage += OnMessage;
		}
		if (InChat && (Main.gameMenu || Main.netMode != 1))
		{
			InChat = false;
		}
		PipePictures.Update();
	}

	/// <summary>Called with every typed chat line: true when it went to the IsaClient chat.</summary>
	public static bool TryIntercept(string line)
	{
		if (!InChat || string.IsNullOrWhiteSpace(line) || line[0] == '/')
		{
			return false;
		}
		SendRoom(line);
		return true;
	}

	private static bool CanSend()
	{
		if (!PipeNet.Enabled || !PipeNet.Ready)
		{
			ClientChat.Error("The IsaClient chat needs a server and the pipe on (.pipe on).");
			return false;
		}
		return true;
	}

	private static byte[] Pack(byte kind, string text)
	{
		if (text.Length > MaxLength)
		{
			text = text.Substring(0, MaxLength);
		}
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		byte[] data = new byte[bytes.Length + 1];
		data[0] = kind;
		Array.Copy(bytes, 0, data, 1, bytes.Length);
		return data;
	}

	public static void SendRoom(string text)
	{
		text = (text ?? "").Trim();
		if (text.Length == 0 || !CanSend())
		{
			return;
		}
		PipeNet.Broadcast(Channel, Pack(Room, text));
		Show(RoomColor, "[IsaChat] " + Main.LocalPlayer.name + ": ", Trim(text));
	}

	public static void SendWhisper(Player to, string text)
	{
		text = (text ?? "").Trim();
		if (text.Length == 0 || !CanSend())
		{
			return;
		}
		if (to.IsaClientKind != 1)
		{
			ClientChat.Notice(IsaClient.Languages.L10n.F("{0} doesn't seem to use IsaClient: they may not get it.", to.name));
		}
		PipeNet.SendTo(to.whoAmI, Channel, Pack(Whisper, text));
		if (_replyTo < 0 || !SameAsReply())
		{
			_replyTo = to.whoAmI;
			_replyName = to.name;
		}
		Show(WhisperColor, IsaClient.Languages.L10n.F("[To {0}] ", to.name), Trim(text));
	}

	private static string Trim(string text)
	{
		return (text.Length > MaxLength) ? text.Substring(0, MaxLength) : text;
	}

	private static void OnMessage(PipeInbound m)
	{
		if (m.Channel != Channel || m.Data == null || m.Data.Length < 2 || m.FromPlayer == Main.myPlayer || m.FromPlayer < 0 || m.FromPlayer >= 255)
		{
			return;
		}
		if (m.Data[0] == Reaction)
		{
			ShowReaction(m.FromPlayer, m.Data);
			return;
		}
		if (m.Data[0] == Pictures)
		{
			List<string> received = ReadPictures(m.Data, out string withText);
			PipePictures.Received(m.FromPlayer, withText, received);
			return;
		}
		string text;
		try
		{
			text = Encoding.UTF8.GetString(m.Data, 1, m.Data.Length - 1);
		}
		catch
		{
			return;
		}
		text = Trim(text.Replace('\n', ' ').Replace('\r', ' '));
		string name = Main.player[m.FromPlayer]?.name ?? ("#" + m.FromPlayer);
		if (m.Data[0] == Whisper && !m.Broadcast)
		{
			Show(WhisperColor, IsaClient.Languages.L10n.F("[From {0}] ", name), text);
			SoundEngine.PlaySound(12);
			_replyTo = m.FromPlayer;
			_replyName = name;
		}
		else if (m.Data[0] == Room)
		{
			Show(RoomColor, "[IsaChat] " + name + ": ", text);
		}
	}

	/// <summary>A chat line in the IsaClient chat's color (item tags in it show, like in the normal chat).</summary>
	private static void Show(Color color, string label, string text)
	{
		Main.NewText(label + text, color.R, color.G, color.B);
	}

	#region Reactions

	/// <summary>A reaction (.hug Isa) to every IsaClient player here, and to you: a colored line with its GIF.</summary>
	public static void SendReaction(string command, string target, string gif)
	{
		ChatActions.Reaction reaction = ChatActions.Find(command);
		if (reaction == null)
		{
			return;
		}
		byte[] name = Encoding.UTF8.GetBytes(command);
		byte[] to = Encoding.UTF8.GetBytes((target ?? "").Length > 40 ? target.Substring(0, 40) : (target ?? ""));
		byte[] link = Encoding.UTF8.GetBytes(gif ?? "");
		List<byte> data = new List<byte> { Reaction, (byte)name.Length };
		data.AddRange(name);
		data.Add((byte)to.Length);
		data.AddRange(to);
		data.AddRange(link);
		PipeNet.Broadcast(Channel, data.ToArray());
		ChatActions.Show(reaction, Main.LocalPlayer.name, target, gif);
	}

	private static void ShowReaction(int from, byte[] data)
	{
		try
		{
			int at = 1;
			int n = data[at++];
			string command = Encoding.UTF8.GetString(data, at, n);
			at += n;
			int t = data[at++];
			string target = Encoding.UTF8.GetString(data, at, t);
			at += t;
			string gif = (at < data.Length) ? Encoding.UTF8.GetString(data, at, data.Length - at) : "";
			ChatActions.Reaction reaction = ChatActions.Find(command);
			if (reaction == null || from < 0 || from >= 255)
			{
				return;
			}
			ChatActions.Show(reaction, Main.player[from]?.name ?? ("#" + from), target.Replace('\n', ' '), gif);
		}
		catch (System.ArgumentException)
		{
		}
		catch (System.IndexOutOfRangeException)
		{
		}
	}

	#endregion

	#region Pictures

	/// <summary>
	/// Chat pictures to every IsaClient player here, through the pipe: shown to them (and you) as a message of yours
	/// with the thumbnails, like pictures in the chat. A few bytes a picture.
	/// </summary>
	public static void SendPictures(System.Collections.Generic.List<string> links, string text)
	{
		if (links == null || links.Count == 0)
		{
			return;
		}
		System.Collections.Generic.List<byte> data = new System.Collections.Generic.List<byte> { Pictures, (byte)Math.Min(links.Count, 255) };
		foreach (string link in links.Take(255))
		{
			System.Collections.Generic.List<int> code = IsaClient.ChatImages.PictureCodes.ToBytes(link);
			if (code != null)
			{
				data.Add((byte)code.Count);
				data.AddRange(code.Select(b => (byte)b));
			}
			else
			{
				byte[] bytes = Encoding.UTF8.GetBytes(link.Length > 200 ? link.Substring(0, 200) : link);
				data.Add(0);
				data.Add((byte)bytes.Length);
				data.AddRange(bytes);
			}
		}
		// The text they go with (its end is enough to find the message).
		text = (text ?? "").Trim();
		if (text.Length > 60)
		{
			text = text.Substring(text.Length - 60);
		}
		data.AddRange(Encoding.UTF8.GetBytes(text));
		PipeNet.Broadcast(Channel, data.ToArray());
		// Yours too: they go on your message when the server sends it back.
		PipePictures.Received(Main.myPlayer, text, links);
	}

	private static System.Collections.Generic.List<string> ReadPictures(byte[] data, out string text)
	{
		text = "";
		System.Collections.Generic.List<string> links = new System.Collections.Generic.List<string>();
		int count = (data.Length > 1) ? data[1] : 0;
		int at = 2;
		for (int i = 0; i < count && at < data.Length; i++)
		{
			int n = data[at++];
			if (n > 0)
			{
				if (at + n > data.Length)
				{
					break;
				}
				string link = IsaClient.ChatImages.PictureCodes.FromBytes(data.Skip(at).Take(n).Select(b => (int)b).ToList());
				at += n;
				if (link != null)
				{
					links.Add(link);
				}
			}
			else
			{
				if (at >= data.Length)
				{
					break;
				}
				int length = data[at++];
				if (at + length > data.Length)
				{
					break;
				}
				string link = Encoding.UTF8.GetString(data, at, length);
				at += length;
				// Only real picture links and shared characters, styles and schematics (whole tags): nothing else.
				if (IsaClient.ChatImages.ChatImages.IsImageLink(link) || IsaClient.Sharing.ShareChat.IsWholeShareTag(link))
				{
					links.Add(link);
				}
			}
		}
		if (at < data.Length)
		{
			try
			{
				text = Encoding.UTF8.GetString(data, at, data.Length - at);
			}
			catch
			{
				text = "";
			}
		}
		return links;
	}

	#endregion

	#region Commands

	/// <summary>.whisper &lt;player&gt; &lt;message&gt;</summary>
	internal static void WhisperCommand(CommandContext command)
	{
		string rest = command.Rest.Trim();
		if (rest.Length == 0)
		{
			ClientChat.Info("Usage: .whisper <player> <message> (only IsaClient players get it, never the server).");
			return;
		}
		Player target = ParseWhisper(rest, out string message, out string error);
		if (target == null)
		{
			ClientChat.Error(error);
			return;
		}
		if (string.IsNullOrWhiteSpace(message))
		{
			ClientChat.Error("Write the message after the name: .whisper <player> <message>");
			return;
		}
		SendWhisper(target, message);
	}

	/// <summary>"Name with spaces the message": the longest name of a player here the line starts with, else the first word.</summary>
	private static Player ParseWhisper(string rest, out string message, out string error)
	{
		error = null;
		foreach (Player p in Main.player.Where(p => p != null && p.active && p.whoAmI != Main.myPlayer && !string.IsNullOrEmpty(p.name)).OrderByDescending(p => p.name.Length))
		{
			if (rest.Equals(p.name, StringComparison.OrdinalIgnoreCase) || rest.StartsWith(p.name + " ", StringComparison.OrdinalIgnoreCase))
			{
				message = (rest.Length > p.name.Length) ? rest.Substring(p.name.Length + 1) : "";
				return p;
			}
		}
		int space = rest.IndexOf(' ');
		string who = (space < 0) ? rest : rest.Substring(0, space);
		message = (space < 0) ? "" : rest.Substring(space + 1);
		return ChatTools.FindPlayer(who.Trim('"'), out error);
	}

	/// <summary>
	/// Where a typed line with pictures attached goes, when it is the IsaClient chat's: .w/.whisper/.pm/.isamsg, .r/.reply,
	/// .inchat with a message, or anything while .inchat is on. Returns the sender (null: the line isn't ours) and the
	/// text to send with the pictures.
	/// </summary>
	public static Action<string> RouteFor(string typed, out string text)
	{
		text = (typed ?? "").Trim();
		bool command = text.Length > 0 && (text[0] == '.' || text[0] == ChatCommands.CommandSpecifier);
		if (!command)
		{
			if (InChat && (text.Length == 0 || text[0] != '/'))
			{
				return SendRoom;
			}
			return null;
		}
		int space = text.IndexOf(' ');
		string head = ((space < 0) ? text.Substring(1) : text.Substring(1, space - 1)).ToLowerInvariant();
		string rest = (space < 0) ? "" : text.Substring(space + 1).Trim();
		switch (head)
		{
		case "w":
		case "whisper":
		case "pm":
		case "isamsg":
		{
			Player target = ParseWhisper(rest, out string message, out string error);
			if (target == null)
			{
				return null;
			}
			text = message.Trim();
			return line => SendWhisper(target, line);
		}
		case "r":
		case "reply":
		case "replay":
			if (_replyTo < 0 || !SameAsReply())
			{
				return null;
			}
			text = rest;
			Player replyTo = Main.player[_replyTo];
			return line => SendWhisper(replyTo, line);
		case "inchat":
		case "isachat":
			text = rest;
			return SendRoom;
		default:
			return null;
		}
	}

	/// <summary>A message with its pictures (already coded, in groups): text and pictures together while they fit.</summary>
	public static void SendWithPictures(Action<string> send, string text, List<string> groups)
	{
		text = (text ?? "").Trim();
		int first = 0;
		if (text.Length > 0)
		{
			if (groups.Count > 0 && text.Length + 1 + groups[0].Length <= MaxLength)
			{
				send(text + " " + groups[0]);
				first = 1;
			}
			else
			{
				send(text);
			}
		}
		for (int i = first; i < groups.Count; i++)
		{
			send(groups[i]);
		}
	}

	/// <summary>True while the player .reply would answer is still the same one, here.</summary>
	private static bool SameAsReply()
	{
		Player p = (_replyTo >= 0 && _replyTo < 255) ? Main.player[_replyTo] : null;
		return p != null && p.active && p.name == _replyName;
	}

	/// <summary>.reply &lt;message&gt;: to the last player who whispered you.</summary>
	internal static void ReplyCommand(CommandContext command)
	{
		string message = command.Rest.Trim();
		if (_replyTo < 0)
		{
			ClientChat.Error("Nobody whispered you yet. Use .w <player> <message>.");
			return;
		}
		if (!SameAsReply())
		{
			ClientChat.Error(IsaClient.Languages.L10n.F("{0} isn't here anymore.", _replyName));
			return;
		}
		if (message.Length == 0)
		{
			ClientChat.Info(IsaClient.Languages.L10n.F(".r <message> answers {0}.", _replyName));
			return;
		}
		SendWhisper(Main.player[_replyTo], message);
	}

	/// <summary>.inchat [message]: alone it switches what you type to the IsaClient chat; with text it sends just that.</summary>
	internal static void InChatCommand(CommandContext command)
	{
		string rest = command.Rest.Trim();
		if (rest.Length > 0)
		{
			SendRoom(rest);
			return;
		}
		if (!InChat && !CanSend())
		{
			return;
		}
		InChat = !InChat;
		ClientChat.Info(InChat ? "IsaClient chat on: what you type goes only to IsaClient players here (not the server). .inchat again to go back." : "IsaClient chat off: what you type goes to the server again.");
	}

	#endregion
}
