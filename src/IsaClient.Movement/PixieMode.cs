using System;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace IsaClient.Movement;

/// <summary>
/// .pixie [player]: your character moves as a Pixie (NPC 75) does, its AI (aiStyle 22) ported line by line, and
/// stays on the Pixie mount (buff 384), killing what touches it (.killaura 1). Its target is the closest other player, or the one named; alone, the cursor.
/// The AI sets the velocity right before the player's own collision (<see cref="BeforeCollision"/>), and reads what
/// the collision did after it (<see cref="AfterCollision"/>), just as an NPC's collideX / collideY.
/// </summary>
public static class PixieMode
{
	public static bool Active;

	/// <summary>The player chased, or -1: the closest one (alone, the cursor).</summary>
	private static int _target = -1;

	/// <summary>.pixie attack: after the closest hostile NPC instead of players (none: the cursor).</summary>
	private static bool _npcs;

	private static readonly float[] ai = new float[4];

	private static int direction = 1;

	private static int directionY = 1;

	private static Vector2 velocity;

	private static Vector2 oldVelocity;

	private static bool collideX;

	private static bool collideY;

	private static int _lastLife;

	/// <summary>.pixie [player | attack | off]</summary>
	internal static void Command(CommandContext command)
	{
		string arg = command.Rest.Trim();
		if (arg.Equals("off", StringComparison.OrdinalIgnoreCase) || arg.Equals("stop", StringComparison.OrdinalIgnoreCase) || (arg.Length == 0 && Active))
		{
			Stop();
			return;
		}
		_npcs = arg.Equals("attack", StringComparison.OrdinalIgnoreCase) || arg.Equals("npc", StringComparison.OrdinalIgnoreCase) || arg.Equals("npcs", StringComparison.OrdinalIgnoreCase);
		int target = -1;
		if (arg.Length > 0 && !_npcs)
		{
			Player p = IsaClient.Community.ChatTools.FindPlayer(arg, out string error);
			if (p == null)
			{
				ClientChat.Error(error ?? ("No player " + arg + "."));
				return;
			}
			target = p.whoAmI;
		}
		Start(target);
	}

	public static void Start(int target)
	{
		Player me = Main.LocalPlayer;
		if (NpcBrain.Active)
		{
			NpcBrain.Stop();
		}
		bool was = Active;
		_target = target;
		Active = true;
		if (!was)
		{
			Array.Clear(ai, 0, ai.Length);
			velocity = me.velocity;
			oldVelocity = velocity;
			direction = me.direction;
			directionY = 1;
			collideX = false;
			collideY = false;
			_lastLife = me.statLife;
		}
		ClientChat.Info(_npcs ? IsaClient.Languages.L10n.T("Pixie: you're a pixie after the closest enemy (none: the cursor). .pixie again stops.") : (target >= 0) ? IsaClient.Languages.L10n.F("Pixie: you're a pixie after {0}. .pixie again stops.", Main.player[target].name) : IsaClient.Languages.L10n.T("Pixie: you're a pixie after the closest player (alone: the cursor). .pixie again stops."));
	}

	public static void Stop()
	{
		if (!Active)
		{
			return;
		}
		Active = false;
		Player me = Main.LocalPlayer;
		if (me.mount.Active && me.mount.Type == MountID.Pixie)
		{
			me.mount.Dismount(me);
		}
		int buff = me.FindBuffIndex(BuffID.PixieMount);
		if (buff >= 0)
		{
			me.DelBuff(buff);
		}
		ClientChat.Info("Pixie: you're you again.");
	}

	/// <summary>Every tick in a world: keeps the Pixie mount on.</summary>
	public static void Update()
	{
		if (!Active)
		{
			return;
		}
		if (Main.gameMenu)
		{
			Active = false;
			return;
		}
		Player me = Main.LocalPlayer;
		if (_target >= 0 && (!Main.player[_target].active || _target == Main.myPlayer))
		{
			_target = -1;
		}
		if (!me.dead)
		{
			me.AddBuff(BuffID.PixieMount, 2);
			// A kill aura of 1 block, as .killaura 1 (unless that's already on).
			if (!IsaClient.Gameplay.Combat.KillAura || IsaClient.Gameplay.Combat.KillAuraSize <= 0)
			{
				IsaClient.Gameplay.Combat.KillBubble(1);
			}
		}
	}

	/// <summary>The closest hostile NPC that can be hit, or null.</summary>
	private static NPC ClosestEnemy(Player me)
	{
		NPC found = null;
		float best = float.MaxValue;
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			NPC n = Main.npc[i];
			if (!n.active || n.friendly || n.townNPC || n.dontTakeDamage || n.lifeMax <= 5 || n.type == NPCID.TargetDummy)
			{
				continue;
			}
			float d = Math.Abs(n.Center.X - me.Center.X) + Math.Abs(n.Center.Y - me.Center.Y);
			if (d < best)
			{
				best = d;
				found = n;
			}
		}
		return found;
	}

	/// <summary>The target's box: the closest enemy (.pixie attack), else the chosen player, the closest other one, or the cursor.</summary>
	private static Rectangle Target(Player me)
	{
		if (_npcs)
		{
			NPC enemy = ClosestEnemy(me);
			if (enemy != null)
			{
				return enemy.Hitbox;
			}
			Vector2 cursor = Main.MouseWorld;
			return new Rectangle((int)cursor.X - 10, (int)cursor.Y - 21, 20, 42);
		}
		Player who = (_target >= 0) ? Main.player[_target] : null;
		if (who == null)
		{
			float best = float.MaxValue;
			for (int i = 0; i < 255; i++)
			{
				Player p = Main.player[i];
				if (i == Main.myPlayer || !p.active || p.dead || p.ghost)
				{
					continue;
				}
				float d = Math.Abs(p.Center.X - me.Center.X) + Math.Abs(p.Center.Y - me.Center.Y);
				if (d < best)
				{
					best = d;
					who = p;
				}
			}
		}
		if (who != null)
		{
			return who.Hitbox;
		}
		Vector2 mouse = Main.MouseWorld;
		return new Rectangle((int)mouse.X - 10, (int)mouse.Y - 21, 20, 42);
	}

	private static bool NoAggro(Player me)
	{
		Player who = (_target >= 0 && !_npcs) ? Main.player[_target] : null;
		return who != null && who.npcTypeNoAggro[NPCID.Pixie];
	}

	/// <summary>NPC.TargetClosest: face the target, up or down toward it.</summary>
	private static void TargetClosest(Player me, Rectangle target)
	{
		direction = (target.Center.X < me.position.X + me.width / 2) ? -1 : 1;
		directionY = (target.Center.Y < me.position.Y + me.height / 2) ? -1 : 1;
	}

	private static bool SolidOrWet(int x, int y)
	{
		Tile tile = Framing.GetTileSafely(x, y);
		return (tile.nactive() && Main.tileSolid[tile.type]) || tile.liquid > 0;
	}

	/// <summary>Player.Update, right before the collision: the Pixie's AI sets the velocity.</summary>
	public static void BeforeCollision(Player me)
	{
		if (!Active || me.whoAmI != Main.myPlayer || me.dead)
		{
			return;
		}
		Rectangle target = Target(me);
		bool justHit = me.statLife < _lastLife;
		_lastLife = me.statLife;
		Vector2 position = me.position;
		Vector2 center = me.Center;
		bool flag30 = false;
		if (justHit)
		{
			ai[2] = 0f;
		}
		if (ai[2] >= 0f)
		{
			int num309 = 16;
			bool flag32 = false;
			bool flag33 = false;
			if (position.X > ai[0] - num309 && position.X < ai[0] + num309)
			{
				flag32 = true;
			}
			else if ((velocity.X < 0f && direction > 0) || (velocity.X > 0f && direction < 0))
			{
				flag32 = true;
			}
			num309 += 24;
			if (position.Y > ai[1] - num309 && position.Y < ai[1] + num309)
			{
				flag33 = true;
			}
			if (flag32 && flag33)
			{
				ai[2]++;
				if (ai[2] >= 30f && num309 == 16)
				{
					flag30 = true;
				}
				if (ai[2] >= 60f)
				{
					ai[2] = -200f;
					direction *= -1;
					velocity.X *= -1f;
					collideX = false;
				}
			}
			else
			{
				ai[0] = position.X;
				ai[1] = position.Y;
				ai[2] = 0f;
			}
			TargetClosest(me, target);
		}
		else
		{
			ai[2]++;
			direction = (target.Center.X > center.X) ? -1 : 1;
		}
		int num310 = (int)(center.X / 16f) + direction * 2;
		int num311 = (int)(me.Bottom.Y / 16f);
		bool flag34 = true;
		bool flag35 = false;
		int num312 = 4;
		if (Main.rand.Next(6) == 0)
		{
			int dust = Dust.NewDust(position, me.width, me.height, 55, 0f, 0f, 200);
			Main.dust[dust].velocity *= 0.3f;
		}
		if (Main.rand.Next(40) == 0)
		{
			SoundEngine.PlaySound(27, (int)position.X, (int)position.Y);
		}
		if (me.Bottom.Y > target.Top)
		{
			for (int num342 = num311; num342 < num311 + num312; num342++)
			{
				if (SolidOrWet(num310, num342))
				{
					if (num342 <= num311 + 1)
					{
						flag35 = true;
					}
					flag34 = false;
					break;
				}
			}
		}
		if (NoAggro(me))
		{
			bool flag37 = false;
			for (int num343 = num311; num343 < num311 + num312 - 2; num343++)
			{
				if (SolidOrWet(num310, num343))
				{
					flag37 = true;
					break;
				}
			}
			directionY = (!flag37).ToDirectionInt();
		}
		if (flag30)
		{
			flag35 = false;
			flag34 = true;
		}
		if (flag34)
		{
			velocity.Y += 0.2f;
			if (velocity.Y > 2f)
			{
				velocity.Y = 2f;
			}
		}
		else
		{
			if ((directionY < 0 && velocity.Y > 0f) || flag35)
			{
				velocity.Y -= 0.2f;
			}
			if (velocity.Y < -4f)
			{
				velocity.Y = -4f;
			}
		}
		if (me.wet)
		{
			velocity.Y -= 0.2f;
			if (velocity.Y < -2f)
			{
				velocity.Y = -2f;
			}
		}
		if (collideX)
		{
			velocity.X = oldVelocity.X * -0.4f;
			if (direction == -1 && velocity.X > 0f && velocity.X < 1f)
			{
				velocity.X = 1f;
			}
			if (direction == 1 && velocity.X < 0f && velocity.X > -1f)
			{
				velocity.X = -1f;
			}
		}
		if (collideY)
		{
			velocity.Y = oldVelocity.Y * -0.25f;
			if (velocity.Y > 0f && velocity.Y < 1f)
			{
				velocity.Y = 1f;
			}
			if (velocity.Y < 0f && velocity.Y > -1f)
			{
				velocity.Y = -1f;
			}
		}
		float num345 = 3f;
		if (direction == -1 && velocity.X > 0f - num345)
		{
			velocity.X -= 0.1f;
			if (velocity.X > num345)
			{
				velocity.X -= 0.1f;
			}
			else if (velocity.X > 0f)
			{
				velocity.X += 0.05f;
			}
			if (velocity.X < 0f - num345)
			{
				velocity.X = 0f - num345;
			}
		}
		else if (direction == 1 && velocity.X < num345)
		{
			velocity.X += 0.1f;
			if (velocity.X < 0f - num345)
			{
				velocity.X += 0.1f;
			}
			else if (velocity.X < 0f)
			{
				velocity.X -= 0.05f;
			}
			if (velocity.X > num345)
			{
				velocity.X = num345;
			}
		}
		num345 = 1.5f;
		if (directionY == -1 && velocity.Y > 0f - num345)
		{
			velocity.Y -= 0.04f;
			if (velocity.Y > num345)
			{
				velocity.Y -= 0.05f;
			}
			else if (velocity.Y > 0f)
			{
				velocity.Y += 0.03f;
			}
			if (velocity.Y < 0f - num345)
			{
				velocity.Y = 0f - num345;
			}
		}
		else if (directionY == 1 && velocity.Y < num345)
		{
			velocity.Y += 0.04f;
			if (velocity.Y < 0f - num345)
			{
				velocity.Y += 0.05f;
			}
			else if (velocity.Y < 0f)
			{
				velocity.Y -= 0.03f;
			}
			if (velocity.Y > num345)
			{
				velocity.Y = num345;
			}
		}
		// As an NPC: the velocity before the collision is its oldVelocity.
		oldVelocity = velocity;
		me.velocity = velocity;
		me.direction = direction;
		me.fallStart = (int)(me.position.Y / 16f);
	}

	/// <summary>Player.Update, right after the collision: what it changed is the NPC's collideX / collideY.</summary>
	public static void AfterCollision(Player me)
	{
		if (!Active || me.whoAmI != Main.myPlayer || me.dead)
		{
			return;
		}
		collideX = me.velocity.X != oldVelocity.X;
		collideY = me.velocity.Y != oldVelocity.Y;
		velocity = me.velocity;
	}
}
