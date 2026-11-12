using System;
using System.Collections.Generic;
using IsaClient.Core;
using IsaClient.Net.Pipe;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace IsaClient.Movement;

/// <summary>
/// Experimental > NPC Brain, seen by the other IsaClient players: while your brain shows its NPC, they see that NPC
/// where you are (and not you, when you hide yourself), walking, flying and turning as you move. Your client says which
/// NPC through the pipe when it changes and every few seconds (players who come later see it too); theirs draws it
/// with the game's own NPC drawing and animates it from your movement. Players without IsaClient keep seeing you.
/// </summary>
public static class NpcLook
{
	public const byte Channel = 78;

	private const byte Version = 1;

	/// <summary>Said again this often while on (ticks), so players who come later see it.</summary>
	private const int Repeat = 60 * 8;

	/// <summary>Forgotten when not heard for this long (the sender left or their pipe stopped).</summary>
	private static readonly TimeSpan Forget = TimeSpan.FromSeconds(30);

	private sealed class Look
	{
		public int NetId;

		public bool Hide;

		public NPC Npc;

		public DateTime Heard;

		/// <summary>Its AI threw here once: only its frames from then on.</summary>
		public bool AiBroken;
	}

	private static readonly Dictionary<int, Look> _looks = new Dictionary<int, Look>();

	private static bool _hooked;

	/// <summary>What was last said about you (netId, hide; 0 = nothing).</summary>
	private static int _saidId;

	private static bool _saidHide;

	private static int _sinceSaid;

	/// <summary>True when this player is drawn as an NPC here and hides (RefreshPlayerDrawOrder leaves them out).</summary>
	public static bool Hides(int player)
	{
		return player != Main.myPlayer && _looks.TryGetValue(player, out Look look) && look.Hide && look.Npc != null && Main.player[player].active && !Main.player[player].dead;
	}

	/// <summary>Every tick (main thread): say what you look like; animate what the others look like.</summary>
	internal static void Update()
	{
		if (!_hooked)
		{
			_hooked = true;
			PipeNet.OnMessage += OnMessage;
		}
		if (Main.gameMenu || Main.netMode != 1)
		{
			_looks.Clear();
			_saidId = 0;
			return;
		}
		Say();
		List<int> gone = null;
		foreach (KeyValuePair<int, Look> pair in _looks)
		{
			Player p = Main.player[pair.Key];
			if (!p.active || DateTime.Now - pair.Value.Heard > Forget)
			{
				(gone ??= new List<int>()).Add(pair.Key);
				continue;
			}
			Animate(pair.Value, p);
		}
		if (gone != null)
		{
			foreach (int i in gone)
			{
				_looks.Remove(i);
			}
		}
	}

	/// <summary>Your NPC (or none) to the others, when it changes and now and then.</summary>
	private static void Say()
	{
		bool showing = NpcBrain.Active && NpcBrain.ShowSkin && NpcBrain.Puppet != null;
		int id = showing ? NpcBrain.Type : 0;
		bool hide = showing && NpcBrain.HidePlayer;
		_sinceSaid++;
		bool changed = id != _saidId || hide != _saidHide;
		if (!changed && (id == 0 || _sinceSaid < Repeat))
		{
			return;
		}
		if (!PipeNet.CanSend)
		{
			return;
		}
		_saidId = id;
		_saidHide = hide;
		_sinceSaid = 0;
		short net = (short)id;
		PipeNet.Broadcast(Channel, new byte[] { Version, (byte)(id != 0 ? 1 : 0), (byte)net, (byte)(net >> 8), (byte)(hide ? 1 : 0) });
	}

	private static void OnMessage(PipeInbound m)
	{
		if (m.Channel != Channel || m.Data == null || m.Data.Length < 5 || m.Data[0] != Version || m.FromPlayer == Main.myPlayer || m.FromPlayer < 0 || m.FromPlayer >= Main.maxPlayers)
		{
			return;
		}
		try
		{
			if (m.Data[1] == 0)
			{
				_looks.Remove(m.FromPlayer);
				return;
			}
			int netId = (short)(m.Data[2] | (m.Data[3] << 8));
			if (!NpcBrain.Valid(netId))
			{
				return;
			}
			if (!_looks.TryGetValue(m.FromPlayer, out Look look) || look.NetId != netId)
			{
				NPC npc = new NPC();
				npc.SetDefaults(netId);
				npc.whoAmI = Main.maxNPCs;
				npc.active = true;
				npc.IsABestiaryIconDummy = false;
				look = new Look { NetId = netId, Npc = npc };
				_looks[m.FromPlayer] = look;
			}
			look.Hide = m.Data[4] != 0;
			look.Heard = DateTime.Now;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	/// <summary>
	/// One tick of the NPC, here, the way the brain runs it on its player: its own AI (turning, tilting, its states and
	/// moves) and its frames, standing where that player stands. Only its look is kept: where it is and how fast it goes
	/// are the player's (the game already moves them smoothly). The player counts as a ghost meanwhile, so it doesn't go
	/// after them; nothing it would make (NPCs, shots) is made.
	/// </summary>
	private static void Animate(Look look, Player p)
	{
		NPC npc = look.Npc;
		if (npc == null)
		{
			return;
		}
		Vector2 bottom = new Vector2(p.Center.X, p.position.Y + p.height);
		npc.Bottom = bottom;
		npc.velocity = p.velocity;
		npc.oldVelocity = p.velocity;
		npc.collideY = p.velocity.Y == 0f;
		npc.collideX = false;
		npc.wet = p.wet;
		npc.lavaWet = p.lavaWet;
		npc.honeyWet = p.honeyWet;
		npc.active = true;
		npc.timeLeft = 750;
		npc.life = Math.Max(1, (int)((long)npc.lifeMax * p.statLife / Math.Max(1, p.statLifeMax2)));
		if (!look.AiBroken)
		{
			bool ghost = p.ghost;
			bool dead = p.dead;
			int netMode = Main.netMode;
			try
			{
				p.ghost = true;
				p.dead = true;
				Main.netMode = 0;
				NpcBrain.Suppressing = true;
				NpcBrain.SuppressingShots = true;
				npc.AI();
			}
			catch
			{
				look.AiBroken = true;
			}
			finally
			{
				NpcBrain.Suppressing = false;
				NpcBrain.SuppressingShots = false;
				Main.netMode = netMode;
				p.ghost = ghost;
				p.dead = dead;
			}
			// Where it is and how it moves are the player's; the rest (rotation, states, direction) is the AI's.
			npc.Bottom = bottom;
			npc.velocity = p.velocity;
			if (float.IsNaN(npc.rotation))
			{
				npc.rotation = 0f;
			}
		}
		else
		{
			npc.direction = p.direction;
			npc.spriteDirection = p.direction;
		}
		try
		{
			Main.instance.LoadNPC(npc.type);
			npc.FindFrame();
		}
		catch
		{
		}
	}

	/// <summary>Main, with the NPCs drawn over players: each other player's NPC, standing where they stand.</summary>
	public static void Draw(SpriteBatch sb)
	{
		if (_looks.Count == 0 || Main.gameMenu)
		{
			return;
		}
		foreach (KeyValuePair<int, Look> pair in _looks)
		{
			Player p = Main.player[pair.Key];
			NPC npc = pair.Value.Npc;
			if (npc == null || !p.active || p.dead || p.ghost)
			{
				continue;
			}
			try
			{
				npc.Bottom = new Vector2(p.Center.X, p.position.Y + p.height + p.gfxOffY);
				int alpha = npc.alpha;
				if (npc.alpha >= 255)
				{
					npc.alpha = 0;
				}
				Main.instance.LoadNPC(npc.type);
				Main.instance.DrawNPCDirect(sb, npc, npc.behindTiles, Main.screenPosition);
				npc.alpha = alpha;
			}
			catch
			{
			}
		}
	}
}
