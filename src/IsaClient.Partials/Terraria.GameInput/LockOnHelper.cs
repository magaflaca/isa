using IsaClient.Core;

namespace Terraria.GameInput;

public partial class LockOnHelper
{
	/// <summary>IsaClient: lets Lock-On pick hostile players in PvP. Player targets are stored as Main.maxNPCs + whoAmI.</summary>
	[Setting("LockOnTargetsPlayers", Remembered = true)]
	public static bool targetPlayers;

	private static Entity TargetEntity(int id)
	{
		if (id < Main.maxNPCs)
		{
			return Main.npc[id];
		}
		return Main.player[id - Main.maxNPCs];
	}

	public static void ChangeEntityTargetType()
	{
		targetPlayers = !targetPlayers;
	}

	/// <summary>
	/// IsaClient Auto Aim: no key, the target nearest the cursor. Once it has one it holds on while it's alive and on
	/// screen, wherever the cursor goes (fast players get away from a cursor, not from the lock); it only changes when
	/// you point clearly at another one.
	/// </summary>
	private static void AutoTarget()
	{
		int keep = -1;
		if (_enabled && _pickedTarget >= 0 && _pickedTarget < _targets.Count)
		{
			keep = _targets[_pickedTarget];
		}
		// Everything on screen; the cursor only decides between them.
		RefreshTargets(Main.player[Main.myPlayer].Center, 3000f);
		int nearest = -1;
		float best = float.MaxValue;
		for (int i = 0; i < _targets.Count; i++)
		{
			float d = TargetEntity(_targets[i]).Distance(Main.MouseWorld);
			if (d < best)
			{
				best = d;
				nearest = i;
			}
		}
		_pickedTarget = (keep >= 0) ? _targets.IndexOf(keep) : -1;
		if (_pickedTarget >= 0 && nearest >= 0 && nearest != _pickedTarget)
		{
			float held = TargetEntity(_targets[_pickedTarget]).Distance(Main.MouseWorld);
			if (best < IsaClient.Gameplay.SmartAim.AutoRadius * 0.5f && best < held * 0.4f)
			{
				_pickedTarget = nearest;
			}
		}
		if (_pickedTarget < 0 && best <= IsaClient.Gameplay.SmartAim.AutoRadius)
		{
			_pickedTarget = nearest;
		}
		_enabled = _pickedTarget >= 0 && CanEnable();
		_threeDSTarget = -1;
		_targetClosestTarget = -1;
	}

	private static bool ValidTarget(Entity e)
	{
		if (e is Player p)
		{
			Player localPlayer = Main.LocalPlayer;
			if (!targetPlayers || p.whoAmI == Main.myPlayer || !p.active || p.dead || p.ghost || !p.hostile || !localPlayer.hostile)
			{
				return false;
			}
			return p.team == 0 || p.team != localPlayer.team;
		}
		NPC n = e as NPC;
		if (n == null || !n.active || n.dontTakeDamage || n.friendly || n.isLikeATownNPC || n.life < 1 || n.immortal)
		{
			return false;
		}
		if (n.aiStyle == 25 && n.ai[0] == 0f)
		{
			return false;
		}
		return true;
	}
}
