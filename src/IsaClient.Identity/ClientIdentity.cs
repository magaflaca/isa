using System;
using System.IO;
using System.Security.Cryptography;
using IsaClient.Core;
using IsaClient.Net.Pipe;
using Terraria;

namespace IsaClient.Identity;

/// <summary>
/// A permanent id for this IsaClient (one per computer user, the same for every character), made once and kept in
/// IsaClient/identity.json. It is told to the other IsaClient players over the pipe when you join (channel
/// <see cref="Channel"/>), so features can tell people apart even when they switch characters or share a name:
/// <see cref="Of"/> gives a player's id, <see cref="PlayerWith"/> the player who has one, <see cref="PeerSeen"/> fires
/// when someone's id becomes known. Nothing else about you is sent.
/// <list type="bullet">
/// <item>Hello [id]: sent to everyone on joining; everyone answers with Reply [id].</item>
/// <item>Announce [id]: every couple of minutes, for those who joined late; who didn't know you answers with Reply.</item>
/// </list>
/// </summary>
public static class ClientIdentity
{
	/// <summary>The pipe channel of ids ('I').</summary>
	public const byte Channel = 73;

	private const byte Hello = 1;

	private const byte Reply = 2;

	private const byte Announce = 3;

	private const double AnnounceTicks = 150.0 * 60.0;

	private static ulong _id;

	private static readonly ulong[] _peers = new ulong[256];

	private static bool _hooked;

	private static bool _said;

	private static double _nextAnnounce;

	/// <summary>Someone's id became known (player slot, id). On the game thread.</summary>
	public static event Action<int, ulong> PeerSeen;

	/// <summary>This IsaClient's id (never 0).</summary>
	public static ulong Id
	{
		get
		{
			if (_id == 0)
			{
				_id = LoadOrCreate();
			}
			return _id;
		}
	}

	/// <summary>Your scores and the like move to the new id (set by the games).</summary>
	public static event Action<ulong, ulong> Rotated;

	/// <summary>A new id from now on (the old one is forgotten); everyone here hears the new one.</summary>
	public static ulong Rotate()
	{
		ulong old = Id;
		ulong id = NewId();
		Write(id);
		_id = id;
		_said = false;
		Rotated?.Invoke(old, id);
		return id;
	}

	/// <summary>.isaid [new]: shows your IsaClient id, or makes a new one.</summary>
	public static void IdCommand(CommandContext command)
	{
		string arg = command.Rest.Trim().ToLowerInvariant();
		if (arg == "new" || arg == "rotate" || arg == "reset")
		{
			string old = Text;
			Rotate();
			ClientChat.Info("Your new IsaClient ID is:");
			ClientChat.Info(Text);
			ClientChat.Info(IsaClient.Languages.L10n.F("The old one ({0}) is gone; your scores moved to the new one.", old));
			return;
		}
		ClientChat.Info("Your IsaClient ID is:");
		ClientChat.Info(Text);
		ClientChat.Info("The same for all your characters: players with IsaClient see it, the scores use it and reports include it. .isaid new makes another one.");
	}

	/// <summary>The id as 16 hex digits.</summary>
	public static string Text => Hex(Id);

	public static string Hex(ulong id)
	{
		return id.ToString("X16");
	}

	/// <summary>A player's id: yours, or what they told us (0 = not known: no IsaClient, or not yet).</summary>
	public static ulong Of(int player)
	{
		if (player == Main.myPlayer)
		{
			return Id;
		}
		return (player >= 0 && player < 255) ? _peers[player] : 0;
	}

	/// <summary>The player here with that id, or -1.</summary>
	public static int PlayerWith(ulong id)
	{
		if (id == 0)
		{
			return -1;
		}
		if (id == Id && !Main.gameMenu)
		{
			return Main.myPlayer;
		}
		for (int i = 0; i < 255; i++)
		{
			if (_peers[i] == id && Main.player[i] != null && Main.player[i].active)
			{
				return i;
			}
		}
		return -1;
	}

	/// <summary>Every tick (game loop): says hello when the pipe is up, forgets who left.</summary>
	public static void Update()
	{
		if (!_hooked)
		{
			_hooked = true;
			PipeNet.OnMessage += OnMessage;
		}
		if (Main.gameMenu || Main.netMode != 1 || !PipeNet.Enabled || !PipeNet.Ready)
		{
			if (_said)
			{
				_said = false;
				Array.Clear(_peers, 0, _peers.Length);
			}
			return;
		}
		for (int i = 0; i < 255; i++)
		{
			if (_peers[i] != 0 && (Main.player[i] == null || !Main.player[i].active))
			{
				_peers[i] = 0;
			}
		}
		double now = Main.timeForVisualEffects;
		if (!_said)
		{
			_said = true;
			_nextAnnounce = now + AnnounceTicks;
			PipeNet.Broadcast(Channel, Message(Hello));
		}
		else if (now > _nextAnnounce)
		{
			_nextAnnounce = now + AnnounceTicks;
			PipeNet.Broadcast(Channel, Message(Announce));
		}
	}

	private static void OnMessage(PipeInbound m)
	{
		if (m.Channel != Channel || m.Data == null || m.Data.Length < 9 || m.FromPlayer < 0 || m.FromPlayer >= 255 || m.FromPlayer == Main.myPlayer)
		{
			return;
		}
		try
		{
			ulong id = BitConverter.ToUInt64(m.Data, 1);
			if (id == 0)
			{
				return;
			}
			bool known = _peers[m.FromPlayer] == id;
			_peers[m.FromPlayer] = id;
			byte kind = m.Data[0];
			if (kind == Hello || (kind == Announce && !known))
			{
				PipeNet.SendTo(m.FromPlayer, Channel, Message(Reply));
			}
			if (!known)
			{
				PeerSeen?.Invoke(m.FromPlayer, id);
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static byte[] Message(byte kind)
	{
		byte[] data = new byte[9];
		data[0] = kind;
		Array.Copy(BitConverter.GetBytes(Id), 0, data, 1, 8);
		return data;
	}

	/// <summary>IsaClient/identity.json: {"Id": "16 hex digits"}.</summary>
	public static string FilePath => Path.Combine(ClientSettings.IsaClientFolder, "identity.json");

	private static ulong LoadOrCreate()
	{
		try
		{
			if (File.Exists(FilePath) && Parse((string)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(FilePath))["Id"], out ulong saved))
			{
				return saved;
			}
			// The first builds kept it in identity.txt.
			string old = Path.Combine(ClientSettings.IsaClientFolder, "identity.txt");
			if (File.Exists(old) && Parse(File.ReadAllText(old), out ulong before))
			{
				Write(before);
				File.Delete(old);
				return before;
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		ulong id = NewId();
		Write(id);
		return id;
	}

	private static ulong NewId()
	{
		byte[] bytes = new byte[8];
		ulong id = 0;
		using RandomNumberGenerator random = RandomNumberGenerator.Create();
		while (id == 0)
		{
			random.GetBytes(bytes);
			id = BitConverter.ToUInt64(bytes, 0);
		}
		return id;
	}

	private static bool Parse(string text, out ulong id)
	{
		id = 0;
		return text != null && ulong.TryParse(text.Trim(), System.Globalization.NumberStyles.HexNumber, null, out id) && id != 0;
	}

	private static void Write(ulong id)
	{
		try
		{
			Directory.CreateDirectory(ClientSettings.IsaClientFolder);
			File.WriteAllText(FilePath, new Newtonsoft.Json.Linq.JObject { ["Id"] = Hex(id) }.ToString());
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}
}
