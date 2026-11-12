using System;
using System.Diagnostics;
using Terraria;
using Terraria.Audio;
using IsaClient.Networking;
using IsaClient.Rendering;

namespace IsaClient.Core;

/// <summary>Frame time measurements and the low FPS guards used by brushes, pastes and anti-lag.</summary>
public static class Performance
{
	public static int UpdatesAtHighMsThreshold = 0;

	/// <summary>Packet pacing guard: the game has been running under ~50 FPS lately (unless bypassed).</summary>
	public static bool LowFpsGuard => !PacketPacing.BypassPacketProtections && IsLowFPS;

	public static bool IsLowFPS
	{
		get
		{
			if (Main.frameRate >= 15)
			{
				return UpdatesAtHighMsThreshold > 20;
			}
			return true;
		}
	}

	public static bool IsLowFPS_Alt
	{
		get
		{
			if (Main.frameRate >= 15)
			{
				return UpdatesAtHighMsThreshold > (Main.hasFocus ? 20 : 60);
			}
			return true;
		}
	}

	public static bool IsVeryLowFPS
	{
		get
		{
			if (Main.frameRate >= 10)
			{
				return UpdatesAtHighMsThreshold > 60;
			}
			return true;
		}
	}

	public static bool ShownLowFpsBlockedProjMsg;

	private static readonly Stopwatch _updateIntervalWatch = new Stopwatch();

	internal static void MeasureUpdateInterval()
	{
		if (!_updateIntervalWatch.IsRunning)
		{
			_updateIntervalWatch.Start();
			return;
		}
		float ms = (float)_updateIntervalWatch.Elapsed.TotalMilliseconds;
		_updateIntervalWatch.Restart();
		if (ms > 20f)
		{
			UpdatesAtHighMsThreshold += (int)Math.Min(ms / 4f - 4f, 200f);
		}
		else
		{
			UpdatesAtHighMsThreshold -= 2;
			if (UpdatesAtHighMsThreshold < 0)
			{
				UpdatesAtHighMsThreshold = 0;
			}
		}
	}

	internal static void LowFpsMeasures()
	{
		if (!AntiLag.AntiLagFps || !IsLowFPS_Alt || Main.netMode == 2)
		{
			return;
		}
		if (!ShownLowFpsBlockedProjMsg)
		{
			AntiLag.GlobalBlockedProjTime = (Main.hasFocus ? 150 : 300);
			AntiLag.DisableProjLightTime = (Main.hasFocus ? 150 : 300);
			if (Main.hasFocus)
			{
				ClientChat.Notice("[Anti Lag] Low frame rate. Removed all projectiles, dust, and gores. Projectile light has been temporarily disabled & no new projectiles can be recieved from network players for 5 seconds.");
			}
			ShownLowFpsBlockedProjMsg = true;
		}
		if (AntiLag.NoSoundsTime == 0)
		{
			SoundEngine.SoundPlayer?.StopAll();
			AntiLag.NoSoundsTime = 30;
		}
		for (int i = 0; i < Main.maxProjectiles; i++)
		{
			Projectile projectile = Main.projectile[i];
			if (projectile.active)
			{
				if (projectile.owner == Main.myPlayer && Main.netMode == 1)
				{
					NetMessage.SendData(29, -1, -1, null, projectile.key, float.NaN, float.NaN);
				}
				projectile.active = false;
			}
		}
		for (int j = 0; j < Main.dust.Length; j++)
		{
			Main.dust[j].active = false;
		}
		for (int k = 0; k < Main.gore.Length; k++)
		{
			Main.gore[k].active = false;
		}
		UpdatesAtHighMsThreshold /= 3;
	}
}
