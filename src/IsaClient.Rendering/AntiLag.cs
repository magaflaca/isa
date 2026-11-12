using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Drawing;
using Terraria.ID;
using IsaClient.Core;
using IsaClient.Networking;

namespace IsaClient.Rendering;

/// <summary>Anti-lag: blocks projectile/sound/particle spam, limits particles and dust, low FPS measures.</summary>
public static class AntiLag
{
	/// <summary>Percent of dust that is created (100 = vanilla).</summary>
	[Setting("DustDensity", Remembered = true, Min = 0, Max = 100)]
	public static int DustLevel = 100;

	public static int NoKillSoundTime = 0;

	[Setting("AntiLagProjectiles", Remembered = true)]
	public static bool AntiLagProjectiles;

	[Setting("AntiLagSounds", Remembered = true)]
	public static bool AntiLagSounds;

	[Setting("AntiLagParticles", Remembered = true)]
	public static bool AntiLagParticles;

	[Setting("AntiLagFps", Remembered = true)]
	public static bool AntiLagFps;

	[Setting("ParticleLimiter", Remembered = true)]
	public static bool ParticleLimiter;

	public static int ParticleLimit = 1500;

	public static int GlobalBlockedProjTime;

	public static int NoSoundsTime;

	public static int DisableProjLightTime;

	public static int ActiveDustCount;

	/// <summary>Hooked in Dust.NewDust: true when a new dust must not be created.</summary>
	public static bool BlockNewDust()
	{
		if (AntiLagFps && Performance.IsLowFPS)
		{
			return true;
		}
		if (ParticleLimiter)
		{
			if (ActiveDustCount >= ParticleLimit)
			{
				return true;
			}
			ActiveDustCount++;
		}
		return false;
	}

	/// <summary>Hooked in MessageBuffer case 27 (client): anti projectile spam.</summary>
	public static bool ShouldBlockIncomingProjectile(ProjectileKey key, int type)
	{
		int owner = key.Spawner;
		if (owner < 0 || owner > 255 || owner == Main.myPlayer || type <= 0 || type >= ProjectileID.Count || Main.projHostile[type])
		{
			return false;
		}
		Player player = Main.player[owner];
		Projectile existing;
		bool isNew = !key.TryGet(out existing) || existing.type != type;
		if (AntiLagProjectiles && isNew && player.blockProjectileRecieveTime <= 0 && (player.active || owner == 255))
		{
			if (owner != 0 || ServerDetection.TShockDisableTime <= 0)
			{
				player.spammedProjectileCount++;
			}
			if (type == 706)
			{
				player.spammedProjectileCount += 14;
			}
			if (player.spammedProjectileCount >= 180)
			{
				if (!player.isProjectileSpammer)
				{
					if (owner != 255)
					{
						player.isProjectileSpammer = true;
					}
					player.blockProjectileRecieveTime = 300;
					ClientChat.Notice("[Anti Lag] Detected projectile spam from " + ((owner == 255) ? "the Server!" : (player.name + ". They have been flagged as a projectile spammer & their projectiles have been blocked for 5 seconds.")));
				}
				else
				{
					player.blockProjectileRecieveTime = 900;
					ClientChat.Notice("[Anti Lag] Detected projectile spam from known spammer " + player.name + ". Their projectiles have been blocked for 15 seconds.");
				}
			}
		}
		if (GlobalBlockedProjTime > 0 || player.blockProjectileRecieveTime > 0)
		{
			if (existing != null && existing.active)
			{
				existing.active = false;
			}
			return true;
		}
		return false;
	}

	/// <summary>Hooked in MessageBuffer case 58 (instrument sounds): anti sound spam.</summary>
	public static bool ShouldBlockIncomingSound(int playerIndex)
	{
		if (playerIndex < 0 || playerIndex > 255 || playerIndex == Main.myPlayer)
		{
			return false;
		}
		Player player = Main.player[playerIndex];
		if (player.blockSoundRecieveTime > 0 || NoSoundsTime > 0)
		{
			return true;
		}
		if (!AntiLagSounds)
		{
			return false;
		}
		player.spammedSoundCount++;
		if (player.spammedSoundCount >= 100)
		{
			if (!player.isSoundSpammer)
			{
				if (playerIndex != 255)
				{
					player.isSoundSpammer = true;
				}
				player.blockSoundRecieveTime = 300;
				ClientChat.Notice("[Anti Lag] Detected sound spam from " + ((playerIndex == 255) ? "the Server!" : (player.name + ". They have been flagged as a sound spammer & their sounds have been blocked for 5 seconds.")));
			}
			else
			{
				player.blockSoundRecieveTime = 900;
				ClientChat.Notice("[Anti Lag] Detected sound spam from known spammer " + player.name + ". Their sounds have been blocked for 15 seconds.");
			}
			return true;
		}
		return false;
	}

	/// <summary>Hooked in NetParticlesModule.Deserialize (client): anti particle spam.</summary>
	public static bool ShouldBlockIncomingParticle(ParticleOrchestraType type, int playerIndex)
	{
		if (!AntiLagParticles || playerIndex < 0 || playerIndex > 255)
		{
			return false;
		}
		switch (type)
		{
		case ParticleOrchestraType.ChlorophyteLeafCrystalShot:
		case ParticleOrchestraType.TerraBlade:
		case ParticleOrchestraType.Excalibur:
		case ParticleOrchestraType.NightsEdge:
		case ParticleOrchestraType.TrueNightsEdge:
		case ParticleOrchestraType.TrueExcalibur:
		case ParticleOrchestraType.SilverBulletSparkle:
		case ParticleOrchestraType.SlapHand:
		case ParticleOrchestraType.FlyMeal:
		case ParticleOrchestraType.WaffleIron:
			return false;
		}
		Player player = Main.player[playerIndex];
		if (player.blockParticleRecieveTime > 0)
		{
			return true;
		}
		if (playerIndex < 255)
		{
			player.spammedParticleCount++;
		}
		if (player.spammedParticleCount >= 360)
		{
			if (!player.isParticleSpammer)
			{
				player.isParticleSpammer = true;
				player.blockParticleRecieveTime = 300;
				ClientChat.Notice("[Anti Lag] Detected particle spam from " + player.name + ". They have been flagged as a particle spammer & their particles have been blocked for 5 seconds.");
			}
			else
			{
				player.blockParticleRecieveTime = 600;
				ClientChat.Notice("[Anti Lag] Detected particle spam from known spammer " + player.name + ". Their particles have been blocked for 10 seconds.");
			}
			return true;
		}
		return false;
	}

	public static void CleanupGores()
	{
		for (int i = 0; i < Main.gore.Length; i++)
		{
			Main.gore[i].active = false;
		}
		ClientChat.Info("Cleaned up gores.");
	}

	/// <summary>Hooked in MessageBuffer case 102 (NebulaLevelUp): anti nebula-booster spam.</summary>
	public static bool ShouldBlockIncomingNebula(int playerIndex)
	{
		if (playerIndex < 0 || playerIndex > 255 || playerIndex == Main.myPlayer)
		{
			return false;
		}
		Player player = Main.player[playerIndex];
		if (player.blockNebulaRecieveTime > 0)
		{
			return true;
		}
		player.spammedNebulaCount++;
		if (player.spammedNebulaCount >= 50)
		{
			if (!player.isNebulaSpammer)
			{
				if (playerIndex != 255)
				{
					player.isNebulaSpammer = true;
				}
				player.blockNebulaRecieveTime = 300;
				ClientChat.Notice("[Anti Lag] Detected NebulaLevelUp spam from " + ((playerIndex == 255) ? "the Server!" : (player.name + ". They have been flagged as a Nebula spammer & their NebulaLevelUp packets have been blocked for 5 seconds.")));
			}
			else
			{
				player.blockNebulaRecieveTime = 900;
				ClientChat.Notice("[Anti Lag] Detected NebulaLevelUp spam from known spammer " + player.name + ". Their NebulaLevelUp packets have been blocked for 15 seconds.");
			}
			return true;
		}
		return false;
	}

	#region Chat commands

	/// <summary>.cleangores / .cgores / .cleangore / .cgore</summary>
	internal static void CleanGoresCommand(CommandContext command)
	{
		CleanupGores();
	}

	/// <summary>.antilag</summary>
	internal static void AntiLagCommand(CommandContext command)
	{
		bool enable = !(AntiLagProjectiles && AntiLagSounds && AntiLagParticles && AntiLagFps);
		AntiLagProjectiles = (AntiLagSounds = (AntiLagParticles = (AntiLagFps = enable)));
		ClientChat.Info("Anti-Lag measures " + (enable ? "en" : "dis") + "abled.");
	}

	/// <summary>.particlelimit / .particlelimiter</summary>
	internal static void ParticleLimiterCommand(CommandContext command)
	{
		if (command.Parts.Length > 1 && int.TryParse(command.Parts[1], out var limit))
		{
			ParticleLimit = Utils.Clamp(limit, 50, 6000);
			ParticleLimiter = true;
		}
		else
		{
			ParticleLimiter = !ParticleLimiter;
		}
		ClientChat.Info("Particle Limiter " + (ParticleLimiter ? ("enabled (" + ParticleLimit + " max)") : "disabled"));
	}

	/// <summary>.dust</summary>
	internal static void DustCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1].TrimEnd('%'), out var level) && level >= 0 && level <= 100)
			{
				DustLevel = level;
				ClientChat.Info("Dust level changed to " + DustLevel + "%");
			}
			else
			{
				ClientChat.Error("Invalid Dust level (0 - 100)");
			}
		}
		else
		{
			ClientChat.Info("Dust level is " + DustLevel + "%");
		}
	}

	#endregion
}
