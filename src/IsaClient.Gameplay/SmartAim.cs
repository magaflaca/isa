using System;
using System.Collections.Generic;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;

namespace IsaClient.Gameplay;

/// <summary>
/// Smarter aim for Lock-On, both optional:
/// <list type="bullet">
/// <item>Smart Aim leads the target: it follows where it really moves (a fit over its last steps, so the jumps of a
/// laggy server don't throw it off), plays its motion forward with gravity and walls, and aims where your shot and
/// the target meet. How fast and how much your shots fall is learned from the ones you fire with each weapon (until
/// then it reckons from the weapon and ammo).</item>
/// <item>Auto Aim needs no key: while you use a weapon it locks on whatever is nearest the cursor.</item>
/// </list>
/// Hits are judged by the shooter's game against what it shows, so leading what we see is what lands.
/// </summary>
public static class SmartAim
{
	[Setting("SmartAim", Remembered = true)]
	public static bool Enabled;

	[Setting("AutoAim", Remembered = true)]
	public static bool AutoAim;

	/// <summary>How close to the cursor (world pixels) Auto Aim picks a target.</summary>
	public const float AutoRadius = 320f;

	private const int History = 10;

	private const int MaxFrames = 120;

	#region Commands

	/// <summary>.smartaim</summary>
	internal static void SmartAimCommand(CommandContext command)
	{
		Enabled = !Enabled;
		ClientSettings.Save();
		ClientChat.Info(Enabled ? "Smart Aim on: Lock-On leads moving targets." : "Smart Aim off.");
	}

	/// <summary>.autoaim</summary>
	internal static void AutoAimCommand(CommandContext command)
	{
		AutoAim = !AutoAim;
		ClientSettings.Save();
		ClientChat.Info(AutoAim ? "Auto Aim on: weapons lock on the target nearest the cursor." : "Auto Aim off.");
	}

	#endregion

	#region Target motion

	private static Entity _tracked;

	private static readonly List<Vector2> _positions = new List<Vector2>();

	/// <summary>Once a tick (from Lock-On), with what it aims at: keeps that target's last positions.</summary>
	public static void Track(Entity target)
	{
		if (target == null)
		{
			_tracked = null;
			_positions.Clear();
			return;
		}
		Vector2 at = target.Center;
		// Another target, or it teleported: start over.
		if (target != _tracked || (_positions.Count > 0 && Vector2.Distance(_positions[_positions.Count - 1], at) > 160f))
		{
			_tracked = target;
			_positions.Clear();
		}
		_positions.Add(at);
		if (_positions.Count > History)
		{
			_positions.RemoveAt(0);
		}
	}

	/// <summary>
	/// Velocity and acceleration (per tick) from a least-squares fit over the last positions: steady through the
	/// stutter of server updates. Falls back on the game's own velocity while there are few. Used for how things turn
	/// and for flying players; the speed itself comes from the game.
	/// </summary>
	private static void Motion(Entity target, out Vector2 velocity, out Vector2 accel)
	{
		velocity = target.velocity;
		accel = Vector2.Zero;
		int n = (target == _tracked) ? _positions.Count : 0;
		if (n < 4)
		{
			return;
		}
		// Fit p(t) = a + b t + c t^2 / 2 with t = 0 at the newest position.
		double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0;
		double x0 = 0, x1 = 0, x2 = 0, y0 = 0, y1 = 0, y2 = 0;
		for (int i = 0; i < n; i++)
		{
			double t = i - (n - 1);
			double t2 = t * t / 2;
			Vector2 p = _positions[i];
			s0 += 1; s1 += t; s2 += t * t; s3 += t * t2; s4 += t2 * t2;
			x0 += p.X; x1 += p.X * t; x2 += p.X * t2;
			y0 += p.Y; y1 += p.Y * t; y2 += p.Y * t2;
		}
		// Normal equations of [1 t t^2/2]; s2' = sum(t * t^2/2) = s3, etc.
		double a11 = s0, a12 = s1, a13 = s2 / 2, a22 = s2, a23 = s3, a33 = s4;
		double det = a11 * (a22 * a33 - a23 * a23) - a12 * (a12 * a33 - a23 * a13) + a13 * (a12 * a23 - a22 * a13);
		if (Math.Abs(det) < 1e-9)
		{
			return;
		}
		double Solve(double r1, double r2, double r3, int which)
		{
			// Cramer's rule for b (which = 1) or c (which = 2).
			if (which == 1)
			{
				return (a11 * (r2 * a33 - a23 * r3) - r1 * (a12 * a33 - a23 * a13) + a13 * (a12 * r3 - r2 * a13)) / det;
			}
			return (a11 * (a22 * r3 - r2 * a23) - a12 * (a12 * r3 - r2 * a13) + r1 * (a12 * a23 - a22 * a13)) / det;
		}
		velocity = new Vector2((float)Solve(x0, x1, x2, 1), (float)Solve(y0, y1, y2, 1));
		accel = new Vector2((float)Solve(x0, x1, x2, 2), (float)Solve(y0, y1, y2, 2));
		// Trust the fit for speed, but keep the turn-rate believable.
		if (accel.Length() > 0.8f)
		{
			accel *= 0.8f / accel.Length();
		}
	}

	/// <summary>
	/// Where the target will be in <paramref name="frames"/> ticks, as this game will show it (that's where hits are
	/// judged). A player is played forward the way the game moves them: with the keys they're holding (run left or
	/// right, the rest of a jump), their run speed, gravity and walls. Anything else keeps its course and the turn it
	/// was taking.
	/// </summary>
	private static Vector2 Future(Entity target, Vector2 fitVelocity, Vector2 fitAccel, int frames)
	{
		Vector2 position = target.position;
		if (target is Player p)
		{
			return FuturePlayer(p, fitAccel, frames);
		}
		Vector2 velocity = target.velocity;
		Vector2 accel = fitAccel;
		bool walls = !(target is NPC npc) || !npc.noTileCollide;
		for (int i = 0; i < frames; i++)
		{
			velocity += accel;
			// Turns fade: nothing keeps speeding up forever.
			accel *= 0.94f;
			if (walls)
			{
				Vector2 moved = Collision.TileCollision(position, velocity, target.width, target.height, fallThrough: true, fall2: true);
				if (moved.X != velocity.X)
				{
					accel.X = 0f;
				}
				if (moved.Y != velocity.Y)
				{
					accel.Y = Math.Min(accel.Y, 0f);
				}
				velocity = moved;
			}
			position += velocity;
		}
		return position + target.Size / 2f;
	}

	private static Vector2 FuturePlayer(Player p, Vector2 fitAccel, int frames)
	{
		Vector2 position = p.position;
		Vector2 velocity = p.velocity;
		int run = (p.controlRight ? 1 : 0) - (p.controlLeft ? 1 : 0);
		float top = Math.Max(p.maxRunSpeed, p.accRunSpeed);
		int jumpLeft = p.controlJump ? p.jump : 0;
		// In the air without a jump going: falling, or flying (wings, rockets) when the fit says it rises.
		bool flying = p.velocity.Y != 0f && jumpLeft == 0 && p.controlJump && fitAccel.Y < 0f;
		for (int i = 0; i < frames; i++)
		{
			if (run != 0)
			{
				if (velocity.X * run < 0f)
				{
					velocity.X += p.runSlowdown * run;
				}
				if (velocity.X * run < top)
				{
					velocity.X = Math.Min(top, velocity.X * run + p.runAcceleration) * run;
				}
			}
			else if (Math.Abs(velocity.X) <= p.runSlowdown)
			{
				velocity.X = 0f;
			}
			else
			{
				velocity.X -= p.runSlowdown * Math.Sign(velocity.X);
			}
			if (jumpLeft > 0)
			{
				velocity.Y = -Player.jumpSpeed * p.gravDir;
				jumpLeft--;
			}
			else if (flying)
			{
				velocity.Y = Math.Max(velocity.Y + fitAccel.Y, -p.maxFallSpeed);
			}
			else
			{
				velocity.Y += p.gravity * p.gravDir;
				if (velocity.Y * p.gravDir > p.maxFallSpeed)
				{
					velocity.Y = p.maxFallSpeed * p.gravDir;
				}
			}
			velocity = Collision.TileCollision(position, velocity, p.width, p.height, fallThrough: p.controlDown, fall2: p.controlDown, (int)p.gravDir);
			position += velocity;
		}
		return position + p.Size / 2f;
	}

	#endregion

	#region Shots

	/// <summary>How a weapon's shots fly, per tick: speed, how much they fall each tick (after how many), learned.</summary>
	private sealed class Ballistics
	{
		public float Speed;

		public float Gravity;

		public float Delay;

		public int Samples;
	}

	private sealed class Shot
	{
		public int Index;

		public int Type;

		public int Weapon;

		public Vector2 Last;

		public readonly List<Vector2> Steps = new List<Vector2>();
	}

	private static readonly Dictionary<int, Ballistics> _learned = new Dictionary<int, Ballistics>();

	private static readonly List<Shot> _shots = new List<Shot>();

	/// <summary>Per projectile slot: the type of your shot there last tick, plus one (0 = none), to spot new ones.</summary>
	private static readonly int[] _slots = new int[Main.maxProjectiles];

	/// <summary>Once a tick: watches the shots you fire and learns how each weapon's fly.</summary>
	public static void Learn()
	{
		Player me = Main.LocalPlayer;
		if (me == null || !me.active)
		{
			return;
		}
		for (int i = _shots.Count - 1; i >= 0; i--)
		{
			Shot shot = _shots[i];
			Projectile p = Main.projectile[shot.Index];
			bool alive = p.active && p.owner == Main.myPlayer && p.type == shot.Type;
			if (alive)
			{
				Vector2 step = p.position - shot.Last;
				shot.Last = p.position;
				// It hit something or turned: what came before is the free flight.
				bool broke = shot.Steps.Count > 0 && (Vector2.Distance(step, shot.Steps[shot.Steps.Count - 1]) > 3f || step.Length() < 0.5f);
				if (!broke)
				{
					shot.Steps.Add(step);
				}
				if (!broke && shot.Steps.Count < 50)
				{
					continue;
				}
			}
			Finish(shot);
			_shots.RemoveAt(i);
		}
		Item held = me.HeldItem;
		bool shooting = me.itemAnimation > 0 && held != null && held.shoot > 0 && held.shootSpeed > 0f;
		for (int i = 0; i < Main.maxProjectiles; i++)
		{
			Projectile p = Main.projectile[i];
			int was = _slots[i];
			_slots[i] = (p.active && p.owner == Main.myPlayer) ? (p.type + 1) : 0;
			if (_slots[i] == 0 || was == _slots[i])
			{
				continue;
			}
			if (shooting && p.friendly && !p.minion && !p.sentry && _shots.Count < 6 && p.velocity.Length() > 1f)
			{
				_shots.Add(new Shot { Index = i, Type = p.type, Weapon = held.type, Last = p.position });
			}
		}
	}

	private static void Finish(Shot shot)
	{
		List<Vector2> steps = shot.Steps;
		if (steps.Count < 3)
		{
			return;
		}
		float speed = steps[0].Length();
		// Falling: how much the downward step grows each tick, and from when.
		float gravity = 0f;
		int delay = -1;
		int count = 0;
		for (int k = 1; k < steps.Count; k++)
		{
			float grow = steps[k].Y - steps[k - 1].Y;
			if (grow > 0.02f)
			{
				if (delay < 0)
				{
					delay = k;
				}
				gravity += grow;
				count++;
			}
		}
		gravity = (count > 0) ? (gravity / count) : 0f;
		if (count < 2)
		{
			gravity = 0f;
			delay = 0;
		}
		if (!_learned.TryGetValue(shot.Weapon, out Ballistics b))
		{
			_learned[shot.Weapon] = b = new Ballistics();
		}
		// Average in, so one odd shot doesn't throw it off.
		float w = (b.Samples == 0) ? 1f : 0.35f;
		b.Speed = MathHelper.Lerp(b.Speed, speed, w);
		b.Gravity = MathHelper.Lerp(b.Gravity, gravity, w);
		b.Delay = MathHelper.Lerp(b.Delay, Math.Max(0, delay), w);
		b.Samples++;
	}

	/// <summary>How the held weapon's shots fly: learned, or reckoned from the weapon and its ammo. Null for melee.</summary>
	private static Ballistics Of(Player me)
	{
		Item item = me.HeldItem;
		if (item == null || item.shoot <= 0 || item.shootSpeed <= 0f)
		{
			return null;
		}
		if (_learned.TryGetValue(item.type, out Ballistics learned) && learned.Speed > 0.5f)
		{
			return learned;
		}
		float speed = item.shootSpeed;
		int projectile = item.shoot;
		if (item.useAmmo > 0)
		{
			Item ammo = FindAmmo(me, item.useAmmo);
			if (ammo != null)
			{
				speed += ammo.shootSpeed;
				if (ammo.shoot > 0 && item.useAmmo != AmmoID.Rocket && item.useAmmo != AmmoID.Solution)
				{
					projectile = ammo.shoot;
				}
			}
			if (me.magicQuiver && (item.useAmmo == AmmoID.Arrow || item.useAmmo == AmmoID.Stake))
			{
				speed *= 1.1f;
			}
		}
		if (ContentSamples.ProjectilesByType.TryGetValue(projectile, out Projectile sample))
		{
			speed *= sample.extraUpdates + 1;
		}
		return new Ballistics { Speed = speed };
	}

	private static Item FindAmmo(Player me, int ammoType)
	{
		// The ammo slots first, as the game does.
		for (int pass = 0; pass < 2; pass++)
		{
			int from = (pass == 0) ? 54 : 0;
			int to = (pass == 0) ? 58 : 54;
			for (int i = from; i < to; i++)
			{
				Item it = me.inventory[i];
				if (it.stack > 0 && it.ammo == ammoType)
				{
					return it;
				}
			}
		}
		return null;
	}

	#endregion

	/// <summary>
	/// Where to aim at <paramref name="target"/> so the held weapon's shot meets it, or false to aim the usual way
	/// (melee, or no answer). Solved by going back and forth between "when do we meet" and "where is it then".
	/// </summary>
	public static bool TryAim(Entity target, out Vector2 aim)
	{
		aim = Vector2.Zero;
		Player me = Main.LocalPlayer;
		if (!Enabled || target == null || me == null)
		{
			return false;
		}
		Ballistics shot = Of(me);
		Motion(target, out Vector2 velocity, out Vector2 accel);
		Vector2 from = me.Center;
		if (shot == null)
		{
			// Melee and the like: a touch ahead of where it's going.
			aim = Future(target, velocity, accel, 4);
			return true;
		}
		float t = Vector2.Distance(from, target.Center) / shot.Speed;
		Vector2 point = target.Center;
		for (int i = 0; i < 8; i++)
		{
			int frames = (int)Math.Round(MathHelper.Clamp(t, 0f, MaxFrames));
			point = Future(target, velocity, accel, frames);
			float falling = Math.Max(0f, t - shot.Delay);
			Vector2 lifted = point - new Vector2(0f, 0.5f * shot.Gravity * falling * falling);
			float next = Vector2.Distance(from, lifted) / shot.Speed;
			aim = lifted;
			if (Math.Abs(next - t) < 0.25f)
			{
				break;
			}
			t = next;
		}
		return t <= MaxFrames;
	}

	/// <summary>Weapons only: Auto Aim leaves tools, blocks and the like alone.</summary>
	public static bool IsWeapon(Item item)
	{
		return item != null && item.damage > 0 && item.pick == 0 && item.axe == 0 && item.hammer == 0 && item.createTile < 0 && item.createWall < 0 && !item.summon;
	}
}
