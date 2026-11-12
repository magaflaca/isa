using System;
using IsaClient.Core;
using Terraria;
using Terraria.Audio;

namespace IsaClient.Movement;

/// <summary>
/// Movement > Fly the Carpet Like a Broom: the real Flying Carpet (the accessory, so everyone sees the carpet, with or
/// without IsaClient), flown the way the Witch's Broom flies. R (Quick Mount) gets on and off it; while on, it goes in
/// any direction with the broom's own numbers (9 sideways, 8 up and down, 0.16 to speed up and slow down, hovering in
/// place when nothing is held), never runs out, and takes no fall damage.
/// <list type="bullet">
/// <item>Others see the carpet because the game draws it for any player holding jump in the air with a Flying Carpet
/// on: the updates sent to them say jump is held while you ride (your own keys are untouched). Their copy of the carpet runs out after 5 seconds, so every few seconds one
/// update says "touching the ground" for an instant, which gives them a fresh carpet (you don't move).</item>
/// <item>Wings, rocket boots and double jumps fly first for them (the game does those before the carpet): take them off
/// to look right to others.</item>
/// </list>
/// </summary>
public static class CarpetBroom
{
	[Setting("CarpetBroom")]
	public static bool Enabled;

	public static bool Riding { get; private set; }

	private const float SideSpeed = 9f;

	private const float UpDownSpeed = 8f;

	private const float Acceleration = 0.16f;

	/// <summary>Ticks between the carpet refreshes for the others (theirs lasts 300).</summary>
	private const int Refresh = 240;

	private static int _ticks;

	/// <summary>R (Quick Mount): on and off the carpet. False when it isn't ours to handle (a real mount, no carpet on).</summary>
	public static bool QuickMount(Player p)
	{
		if (!Enabled || p.whoAmI != Main.myPlayer)
		{
			return false;
		}
		if (Riding)
		{
			Stop(p);
			return true;
		}
		if (!p.carpet || p.mount.Active || p.dead || p.CCed || p.grappling[0] >= 0)
		{
			return false;
		}
		Riding = true;
		_ticks = 0;
		_vx = p.velocity.X;
		_vy = p.velocity.Y * p.gravDir;
		SoundEngine.PlaySound(Terraria.ID.SoundID.Item24, p.position);
		if (p.velocity.Y == 0f)
		{
			// Off the ground at once, like getting on the broom.
			_vy = -2f;
		}
		return true;
	}

	private static void Stop(Player p)
	{
		Riding = false;
		p.carpetFrame = -1;
		p.canCarpet = false;
		p.carpetTime = 0;
	}

	/// <summary>The carpet's own animation (the game's carpet code puts it away every tick when jump isn't held).</summary>
	private static int _frame;

	private static float _frameCounter;

	/// <summary>
	/// Player.Update, just before collision (everything else already moved you this tick): the broom's flight, and the
	/// carpet shown flapping. Your keys are left alone; only the update sent to the others says jump is held.
	/// </summary>
	public static void BeforeCollision(Player p)
	{
		if (!Enabled || !p.carpet || p.dead || p.mount.Active || p.grappling[0] >= 0 || p.CCed || p.frozen || p.webbed || p.stoned || Main.gameMenu)
		{
			Stop(p);
			return;
		}
		// Sideways: up to 9, 0.16 a tick (to a stop as well). Your own speed of the last tick, not what running added.
		float wantX = (p.controlLeft == p.controlRight) ? 0f : (p.controlRight ? SideSpeed : -SideSpeed);
		_vx = Approach(_vx, wantX, Acceleration);
		// Up and down: up (or jump) to 8 up, down to 8 down, else slowing to hover.
		bool up = p.controlUp || p.controlJump;
		float wantY = up ? (-UpDownSpeed) : (p.controlDown ? UpDownSpeed : 0f);
		_vy = Approach(_vy, wantY, Acceleration);
		float vy = (Math.Abs(_vy) < 0.001f) ? -0.001f : _vy;
		p.velocity = new Microsoft.Xna.Framework.Vector2(_vx, vy * p.gravDir);
		p.fallStart = (int)(p.position.Y / 16f);
		p.fallStart2 = p.fallStart;
		p.jump = 0;
		// Wings and rocket boots stay still while riding (holding up would fly them); they refill on the ground.
		p.wingTime = 0f;
		p.rocketTime = 0;
		// The carpet out, flapping faster the faster you go (the game's own pace).
		_frameCounter += 1f + Math.Abs(_vx * 0.5f);
		if (_frameCounter > 8f)
		{
			_frameCounter = 0f;
			_frame = (_frame + 1) % 6;
		}
		p.carpetFrame = _frame;
		p.carpetFrameCounter = _frameCounter;
		p.canCarpet = false;
		p.carpetTime = 300;
		if (Main.netMode == 1)
		{
			_ticks++;
			if (_ticks % Refresh == 0)
			{
				// One update "on the ground": their carpet starts again (theirs runs out after 5 seconds).
				Microsoft.Xna.Framework.Vector2 keep = p.velocity;
				p.velocity.Y = 0f;
				NetMessage.SendData(13, -1, -1, null, p.whoAmI);
				p.velocity = keep;
			}
			else if (_ticks % 3 == 0)
			{
				// Where you are, often: their copy of you moves with the carpet's slow sideways pace otherwise.
				NetMessage.SendData(13, -1, -1, null, p.whoAmI);
			}
		}
	}

	private static float _vx;

	private static float _vy;

	private static float Approach(float value, float target, float step)
	{
		if (value < target)
		{
			return Math.Min(target, value + step);
		}
		return Math.Max(target, value - step);
	}
}
