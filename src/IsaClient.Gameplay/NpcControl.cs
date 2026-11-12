using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using IsaClient.Core;

namespace IsaClient.Gameplay;

/// <summary>Controlling an NPC as if it were your player (.cnpc, single player).</summary>
public static class NpcControl
{
	public static short ControlledNpcType;

	public static NPC ControlledNpc;

	private static int _controlledOldAiStyle;

	private static int _controlledOldWidth;

	private static int _controlledOldHeight;

	/// <summary>NPC types that must be moved before the NPC update instead of after it.</summary>
	private static readonly HashSet<int> _possessEarlyTypes = new HashSet<int>
	{
		7, 10, 13, 39, 87, 95, 98, 113, 114, 117, 134, 245, 391, 392, 395, 398, 402, 412, 416, 454, 491, 510, 513, 594, 621
	};

	/// <summary>.controlnpc / .cnpc / .cn / .cnf</summary>
	internal static void ControlNpcCommand(CommandContext command)
	{
		string[] array = command.Parts;
		if (Main.netMode != 0)
		{
			ClientChat.Error("This only works offline.");
			return;
		}
		if (array.Length <= 1)
		{
			ClientChat.Info("Current controlled NPC type: " + ((ControlledNpcType == 0) ? "none" : NPCID.Search.GetName(ControlledNpcType)) + " (ID: " + ControlledNpcType + "). Use .cnpc 0 to stop.");
			return;
		}
		if (!Combat.TryParseNpc(array[1], out var type, out _) || type <= -66 || type >= NPCID.Count)
		{
			ClientChat.Error("Invalid NPC type.");
			return;
		}
		ReleaseControlledNpc();
		ControlledNpcType = (short)type;
		ClientChat.Info((type == 0) ? "No longer controlling an NPC." : ("Controlled NPC type set to " + NPCID.Search.GetName(type) + " (ID: " + type + "). Spawn one (.spawnmob) and it will follow you."));
	}

	private static void ReleaseControlledNpc()
	{
		NPC npc = ControlledNpc;
		ControlledNpc = null;
		if (npc == null || !npc.active)
		{
			return;
		}
		npc.aiStyle = _controlledOldAiStyle;
		if (!npc.noTileCollide)
		{
			npc.position.X += npc.width / 2 - _controlledOldWidth / 2;
			npc.position.Y += npc.height - _controlledOldHeight;
			npc.width = _controlledOldWidth;
			npc.height = _controlledOldHeight;
		}
		npc.immune[Main.myPlayer] = 0;
		npc.shimmerTransparency = 0f;
	}

	/// <summary>Called before (some types) and after the NPC update: keeps the controlled NPC on your player.</summary>
	public static void PossessNPC(bool early)
	{
		try
		{
			if (Main.netMode != 0 || Main.gameMenu)
			{
				if (ControlledNpcType != 0 || ControlledNpc != null)
				{
					ControlledNpc = null;
					ControlledNpcType = 0;
				}
				return;
			}
			if (ControlledNpcType == 0)
			{
				if (ControlledNpc != null)
				{
					ReleaseControlledNpc();
				}
				return;
			}
			if (_possessEarlyTypes.Contains(ControlledNpcType) != early)
			{
				return;
			}
			if (ControlledNpc != null && (!ControlledNpc.active || ControlledNpc.type != ControlledNpcType))
			{
				ControlledNpc = null;
			}
			Player player = Main.LocalPlayer;
			NPC npc = ControlledNpc;
			if (npc == null)
			{
				for (int i = 0; i < Main.maxNPCs; i++)
				{
					if (Main.npc[i].active && Main.npc[i].type == ControlledNpcType)
					{
						npc = Main.npc[i];
						_controlledOldAiStyle = npc.aiStyle;
						_controlledOldWidth = npc.width;
						_controlledOldHeight = npc.height;
						break;
					}
				}
			}
			if (npc != null)
			{
				npc.despawnEncouraged = false;
				npc.CanBeReplacedByOtherNPCs = false;
				npc.timeLeft = 750;
				npc.shimmerTransparency = player.dead ? 1f : 0f;
				if (!npc.noTileCollide)
				{
					npc.width = player.width;
					npc.height = player.height;
				}
				npc.immune[player.whoAmI] = 2;
				npc.Bottom = player.Bottom;
				npc.position.X = (int)npc.position.X;
				npc.position.Y = (int)npc.position.Y;
				npc.gfxOffY = player.gfxOffY;
				npc.velocity = player.velocity / 2f;
				if (!npc.noTileCollide && (npc.friendly || npc.townNPC || npc.isLikeATownNPC || (npc.aiStyle != 7 && npc.aiStyle != 37)))
				{
					npc.aiStyle = 0;
					npc.direction = player.direction;
					npc.spriteDirection = player.direction;
				}
				npc.netOffset *= 0f;
			}
			ControlledNpc = npc;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			ControlledNpc = null;
			ControlledNpcType = 0;
		}
	}

	/// <summary>Player.KillMe: the controlled NPC plays its death effect with you.</summary>
	public static void OnLocalPlayerDied(int hitDirection)
	{
		NPC npc = ControlledNpc;
		if (npc == null || Main.netMode != 0)
		{
			return;
		}
		try
		{
			int life = npc.life;
			npc.life = 0;
			npc.HitEffect(hitDirection);
			npc.life = life;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	#region Chat commands

	#endregion
}
