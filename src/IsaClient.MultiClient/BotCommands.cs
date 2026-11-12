using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Net;
using Terraria.Net.Sockets;

namespace IsaClient.MultiClient;

/// <summary>
/// Any IsaClient command, run as a bot. For the length of the command the game takes the bot's place: Main.myPlayer is
/// the bot's player (in your world) and everything the game sends goes out through the bot's own connection, so the
/// command acts on the bot and the server sees the bot do it. Then it's all yours again, at once. Nothing runs for a
/// bot that isn't given a command, so idle bots stay light.
/// Modes that keep running every tick for "the player" (kill aura, noclip, god mode...) belong to whoever plays: when a
/// command switches one on, it is put back as it was for you; the bot's own versions are its steps (.pixie, .follow,
/// .fight, .goto).
/// </summary>
public static class BotCommands
{
	/// <summary>Runs <paramref name="line"/> (an IsaClient command) as <paramref name="bot"/>; false with the reason when it can't.</summary>
	public static bool Run(ClientSession bot, string line, out string error)
	{
		error = null;
		if (Main.netMode != 1 || Main.gameMenu || !Possession.IsOnMyServer(bot))
		{
			error = bot.Name + " is not in your world: commands run as a bot only there.";
			return false;
		}
		if (Possession.Active || Possession.InTransition)
		{
			error = "Not while you play as a bot (.bot return first).";
			return false;
		}
		int index = bot.MyIndex;
		if (index < 0 || index >= 255 || !Main.player[index].active || bot.State != ClientSessionState.InGame || !bot.IsConnected)
		{
			error = bot.Name + " is not in game yet.";
			return false;
		}
		RemoteServer connection = Netplay.Connection;
		ISocket socket = connection.Socket;
		int me = Main.myPlayer;
		Player body = Main.player[index];
		Vector2 position = body.position;
		StaticState before = StaticState.Take();
		// "At the cursor" means at the bot for a bot (items, projectiles, tiles...).
		int mouseX = Main.mouseX;
		int mouseY = Main.mouseY;
		try
		{
			connection.Socket = new BotSocket(socket, bot);
			Main.myPlayer = index;
			Vector2 screen = body.Center - Main.screenPosition;
			Main.mouseX = (int)screen.X;
			Main.mouseY = (int)screen.Y;
			ChatCommands.RunLocalCommand(line);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			error = "That command failed as " + bot.Name + ": " + ex.Message;
		}
		finally
		{
			Main.myPlayer = me;
			connection.Socket = socket;
			Main.mouseX = mouseX;
			Main.mouseY = mouseY;
		}
		// Whatever the command left switched on for "the player" stays as it was for you.
		List<string> modes = before.RestoreChanged();
		if (modes.Count > 0)
		{
			MultiClientManager.Announce(bot.Name + ": " + Head(line) + " keeps running for whoever plays, so it was left as it was for you (" + string.Join(", ", modes.Take(4)) + "). Bots have their own: .pixie, .follow, .fight, .goto.");
		}
		// It moved (a teleport): the bot carries on from there.
		if (body.position != position)
		{
			bot.PlaceAt(body.position);
		}
		return error == null;
	}

	private static string Head(string line)
	{
		string trimmed = line.Trim();
		int space = trimmed.IndexOf(' ');
		return (space < 0) ? trimmed : trimmed.Substring(0, space);
	}

	/// <summary>The game's socket while a command runs as a bot: what is sent goes through the bot's connection; reading stays the game's.</summary>
	private sealed class BotSocket : ISocket
	{
		private readonly ISocket _game;

		private readonly ClientSession _bot;

		public BotSocket(ISocket game, ClientSession bot)
		{
			_game = game;
			_bot = bot;
		}

		public void AsyncSend(byte[] data, int offset, int size, SocketSendCallback callback, object state = null)
		{
			byte[] copy = new byte[size];
			Buffer.BlockCopy(data, offset, copy, 0, size);
			_bot.SendRaw(copy);
			try
			{
				callback?.Invoke(state);
			}
			catch
			{
			}
		}

		public void Close()
		{
			_game.Close();
		}

		public bool IsConnected()
		{
			return _game.IsConnected();
		}

		public void Connect(RemoteAddress address)
		{
			_game.Connect(address);
		}

		public void AsyncReceive(byte[] data, int offset, int size, SocketReceiveCallback callback, object state = null)
		{
			_game.AsyncReceive(data, offset, size, callback, state);
		}

		public bool IsDataAvailable()
		{
			return _game.IsDataAvailable();
		}

		public bool StartListening(SocketConnectionAccepted callback)
		{
			return _game.StartListening(callback);
		}

		public void StopListening()
		{
			_game.StopListening();
		}

		public RemoteAddress GetRemoteAddress()
		{
			return _game.GetRemoteAddress();
		}
	}

	/// <summary>
	/// The client's simple settings and switches (static bool/number/text/enum fields of IsaClient's own classes, not
	/// the bots' or the panel's): taken before a command runs as a bot and put back where it changed them.
	/// </summary>
	private sealed class StaticState
	{
		private static FieldInfo[] _fields;

		private readonly object[] _values;

		private StaticState(object[] values)
		{
			_values = values;
		}

		private static FieldInfo[] Fields
		{
			get
			{
				if (_fields != null)
				{
					return _fields;
				}
				List<FieldInfo> fields = new List<FieldInfo>();
				Type[] types;
				try
				{
					types = typeof(BotCommands).Assembly.GetTypes();
				}
				catch (ReflectionTypeLoadException ex)
				{
					types = ex.Types.Where(t => t != null).ToArray();
				}
				foreach (Type type in types)
				{
					string ns = type.Namespace ?? "";
					if (!ns.StartsWith("IsaClient.", StringComparison.Ordinal) || ns.StartsWith("IsaClient.MultiClient", StringComparison.Ordinal) || ns.StartsWith("IsaClient.Hub", StringComparison.Ordinal) || ns.StartsWith("IsaClient.Menu", StringComparison.Ordinal) || ns.StartsWith("IsaClient.Languages", StringComparison.Ordinal) || type.ContainsGenericParameters)
					{
						continue;
					}
					foreach (FieldInfo f in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
					{
						if (f.IsLiteral || f.IsInitOnly || f.Name.Contains("<"))
						{
							continue;
						}
						Type t = f.FieldType;
						if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(Vector2))
						{
							fields.Add(f);
						}
					}
				}
				_fields = fields.ToArray();
				return _fields;
			}
		}

		public static StaticState Take()
		{
			FieldInfo[] fields = Fields;
			object[] values = new object[fields.Length];
			for (int i = 0; i < fields.Length; i++)
			{
				try
				{
					values[i] = fields[i].GetValue(null);
				}
				catch
				{
					values[i] = null;
				}
			}
			return new StaticState(values);
		}

		/// <summary>Puts back every field that changed; returns their names ("Combat.KillAura").</summary>
		public List<string> RestoreChanged()
		{
			List<string> changed = new List<string>();
			FieldInfo[] fields = Fields;
			for (int i = 0; i < fields.Length; i++)
			{
				try
				{
					object now = fields[i].GetValue(null);
					if (!Equals(now, _values[i]))
					{
						fields[i].SetValue(null, _values[i]);
						if (fields[i].FieldType == typeof(bool) && Equals(now, true))
						{
							changed.Add(fields[i].DeclaringType.Name + "." + fields[i].Name.TrimStart('_'));
						}
					}
				}
				catch
				{
				}
			}
			return changed;
		}
	}
}
