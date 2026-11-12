using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.Drawing;
using IsaClient.Core;
using IsaClient.Movement;

namespace IsaClient.Rendering;

/// <summary>Cosmetics: walking and death particles, death effects and sounds, Lucy messages, RGB map icons.</summary>
public static class Cosmetics
{
	/// <summary>0 none, 1 Aetherium blocks, 2 flames, 3 dyed flames.</summary>
	[Setting("WalkParticleType", Remembered = true, Min = 0, Max = 3)]
	public static int WalkParticleType;

	public static bool ShimmerWalkParticles;

	[Setting("UseDeathParticleCosmetic", Remembered = true)]
	public static bool DeathParticleCosmeticEnabled;

	[Setting("DeathParticleType", Remembered = true)]
	public static int DeathParticleCosmeticId = (int)ParticleOrchestraType.Keybrand;

	[Setting("UseDeathEffectCosmetic", Remembered = true)]
	public static bool DeathGoreCosmeticEnabled;

	[Setting("DeathEffectId", Remembered = true)]
	public static int DeathGoreCosmeticId = 11;

	[Setting("UseDeathSoundCosmetic", Remembered = true)]
	public static bool DeathSoundCosmeticEnabled;

	[Setting("DeathSoundType", Remembered = true)]
	public static int DeathSoundCosmeticId = (int)ParticleOrchestraType.SlapHand;

	[Setting("UseDeathComseticsOnAllPlayers", Remembered = true)]
	public static bool DeathCosmeticsForAll;

	/// <summary>0 no override, 1 forced off, 2 forced on, 3 forced on for everyone.</summary>
	[Setting("LucyMessageType", Remembered = true, Min = 0, Max = 3)]
	public static int LucyMessagesMode;

	/// <summary>0 disabled, 1 self, 2 others, 3 everyone.</summary>
	[Setting("LucyDeathMessageType", Remembered = true, Min = 0, Max = 3)]
	public static int LucyDeathMessageMode;

	[Setting("AfterImageCosmetic", Remembered = true, LegacyKey = "ZazaOnlyAfterImageCosmetic")]
	public static bool ArmorShadowCosmetic;

	[Setting("PulsateCosmetic", Remembered = true, LegacyKey = "ZazaOnlyPulsateCosmetic")]
	public static bool ArmorOutlineCosmetic;

	[Setting("RgbPlayerOutline", Remembered = true, LegacyKey = "ZazaOnlyRgbPlayerOutline")]
	public static bool RgbPlayerOutline;

	[Setting("ClientOnlyRgbNpcIcons", Remembered = true)]
	public static bool RgbNpcIcons;

	[Setting("ClientOnlyBossIcons", Remembered = true)]
	public static bool RgbBossIcons;

	/// <summary>Particles a death can emit (1.4.5.8 enum values).</summary>
	public static readonly ParticleOrchestraType[] DeathParticles = new ParticleOrchestraType[17]
	{
		ParticleOrchestraType.Keybrand,
		ParticleOrchestraType.StellarTune,
		ParticleOrchestraType.BlackLightningHit,
		ParticleOrchestraType.RainbowRodHit,
		ParticleOrchestraType.NightsEdge,
		ParticleOrchestraType.TrueNightsEdge,
		ParticleOrchestraType.Excalibur,
		ParticleOrchestraType.TrueExcalibur,
		ParticleOrchestraType.TerraBlade,
		ParticleOrchestraType.ChlorophyteLeafCrystalPassive,
		ParticleOrchestraType.ChlorophyteLeafCrystalShot,
		ParticleOrchestraType.PetExchange,
		ParticleOrchestraType.GasTrap,
		ParticleOrchestraType.ShimmerArrow,
		ParticleOrchestraType.TownSlimeTransform,
		ParticleOrchestraType.ShimmerTownNPC,
		ParticleOrchestraType.ShimmerTownNPCSend
	};

	public static readonly ParticleOrchestraType[] DeathSounds = new ParticleOrchestraType[3]
	{
		ParticleOrchestraType.SlapHand,
		ParticleOrchestraType.FlyMeal,
		ParticleOrchestraType.WaffleIron
	};

	/// <summary>Gore effects (tree-shake gores) a death can emit; -2 all balloons, -3 yellow and blue stars.</summary>
	public static readonly int[] DeathEffects = new int[22]
	{
		11, 16, 17, -3, 61, 99, 276, 375, 411, 412,
		413, 414, 415, 570, 825, 861, 907, 908, 909, -2,
		1201, 1218
	};

	public static void DisableAll()
	{
		WalkParticleType = 0;
		ShimmerWalkParticles = false;
		WorldView.ServerSideCursor = false;
		DeathParticleCosmeticEnabled = false;
		DeathGoreCosmeticEnabled = false;
		DeathSoundCosmeticEnabled = false;
		DeathCosmeticsForAll = false;
		DeathParticleCosmeticId = (int)DeathParticles[0];
		DeathGoreCosmeticId = DeathEffects[0];
		DeathSoundCosmeticId = (int)DeathSounds[0];
		LucyMessagesMode = 0;
		LucyDeathMessageMode = 0;
		ArmorShadowCosmetic = false;
		ArmorOutlineCosmetic = false;
		RgbPlayerOutline = false;
		RgbNpcIcons = false;
		RgbBossIcons = false;
	}

	internal static string ParticleName(ParticleOrchestraType type)
	{
		switch (type)
		{
		case ParticleOrchestraType.SlapHand:
			return "Slap Hand SFX";
		case ParticleOrchestraType.FlyMeal:
			return "Flymeal SFX";
		case ParticleOrchestraType.WaffleIron:
			return "Waffle Iron SFX";
		case ParticleOrchestraType.BlackLightningHit:
			return "Black Lightning";
		case ParticleOrchestraType.ChlorophyteLeafCrystalPassive:
			return "Leaf Crystal Passive";
		case ParticleOrchestraType.ChlorophyteLeafCrystalShot:
			return "Leaf Crystal Shot";
		default:
		{
			// "TrueNightsEdge" -> "True Nights Edge"
			string raw = type.ToString();
			System.Text.StringBuilder text = new System.Text.StringBuilder(raw.Length + 8);
			for (int i = 0; i < raw.Length; i++)
			{
				if (i > 0 && char.IsUpper(raw[i]) && !char.IsUpper(raw[i - 1]))
				{
					text.Append(' ');
				}
				text.Append(raw[i]);
			}
			return text.ToString();
		}
		}
	}

	internal static string DeathEffectName(int id)
	{
		switch (id)
		{
		case 11:
		case 12:
		case 13:
			return "Cloud Smoke";
		case 16:
			return "Yellow Stars";
		case 17:
			return "Blue Stars";
		case -3:
			return "Yellow & Blue Stars";
		case 61:
		case 62:
		case 63:
			return "Smoke";
		case 99:
			return "Dark Smoke";
		case 276:
			return "Confetti";
		case 375:
			return "Blue Smoke";
		case 411:
			return "Tiny Bubbles";
		case 412:
			return "Small Bubbles";
		case 413:
			return "Medium Bubbles";
		case 414:
			return "Large Bubbles";
		case 415:
			return "Huge Bubbles";
		case 570:
			return "Music Notes";
		case 825:
			return "Lingering Smoke";
		case 861:
			return "Large Smoke";
		case 907:
			return "Red Balloons";
		case 908:
			return "Blue Balloons";
		case 909:
			return "Green Balloons";
		case -2:
			return "All Balloons";
		case 1201:
			return "Chum";
		case 1218:
			return "Coins";
		default:
			return id.ToString();
		}
	}

	public static int CycleValue(int[] values, int current, bool goBack)
	{
		int index = Array.IndexOf(values, current);
		if (index < 0)
		{
			return values[0];
		}
		index = goBack ? ((index - 1 + values.Length) % values.Length) : ((index + 1) % values.Length);
		return values[index];
	}

	public static int CycleParticle(ParticleOrchestraType[] values, int current, bool goBack)
	{
		int[] ids = new int[values.Length];
		for (int i = 0; i < values.Length; i++)
		{
			ids[i] = (int)values[i];
		}
		return CycleValue(ids, current, goBack);
	}

	/// <summary>Player.Update after the equipment, local player: Max Wing Stats, forced sights, invisible blocks.</summary>
	public static void ApplyAfterEquips(Player player)
	{
		if (Travel.MaxWings && player.wingsLogic != 0)
		{
			player.wingsLogic = 45;
		}
		if (WorldView.RenderInvisibleTiles)
		{
			player.CanSeeInvisibleBlocks = true;
		}
		if (WorldView.ForceSpelunker)
		{
			player.findTreasure = true;
		}
		if (WorldView.ForceBiomeSight)
		{
			player.biomeSight = true;
		}
		if (WorldView.ForceHunter)
		{
			player.detectCreature = true;
		}
		if (WorldView.ForceDangersense)
		{
			player.dangerSense = true;
		}
	}

	/// <summary>Player.FloorVisuals, local player: Aetherium-block walking particles (seen by everyone).</summary>
	public static void WalkingShimmerParticles(Player player, bool falling, int floorType)
	{
		if ((!ShimmerWalkParticles && WalkParticleType != 1) || floorType == 659 || floorType == 667 || player.wet || player.mount.Cart)
		{
			return;
		}
		if (!falling)
		{
			float speed = Math.Abs(player.velocity.X) / 3f;
			if ((float)Main.rand.Next(100) > speed * 50f)
			{
				return;
			}
		}
		Vector2 positionInWorld = new Vector2(player.position.X, player.position.Y + (float)player.height - 2f) + new Vector2((float)player.width * Main.rand.NextFloat(), 6f * Main.rand.NextFloat());
		Vector2 movement = Main.rand.NextVector2Circular(0.8f, 0.8f);
		if (movement.Y > 0f)
		{
			movement.Y *= -1f;
		}
		ParticleOrchestrator.BroadcastOrRequestParticleSpawn(ParticleOrchestraType.ShimmerBlock, new ParticleOrchestraSettings
		{
			PositionInWorld = positionInWorld,
			MovementVector = movement
		});
	}

	/// <summary>Player.DoBootsEffect, local player: flames (or dyed goat-mount flames) where you walk, seen by everyone.</summary>
	public static bool WalkingFlameParticles(Player player, int x, int y)
	{
		if (!WorldGen.InWorld(x, y + 1))
		{
			return false;
		}
		Tile below = Main.tile[x, y + 1];
		Tile here = Main.tile[x, y];
		if (below == null || here == null)
		{
			return false;
		}
		bool onWater = below.liquid > 0 && (player.waterWalk || player.waterWalk2);
		if (!onWater && (!below.active() || !WorldGen.SolidTileAllowBottomSlope(x, y + 1)))
		{
			return false;
		}
		Vector2 positionInWorld = new Vector2(x * 16 + 8, y * 16 + 16);
		if (here.halfBrick() || here.slope() > 0)
		{
			positionInWorld.Y -= 8f;
		}
		if (!WorldGen.SolidTileAllowBottomSlope(x, y + 1) && !here.halfBrick())
		{
			positionInWorld.Y = player.Bottom.Y;
		}
		ParticleOrchestrator.BroadcastOrRequestParticleSpawn((WalkParticleType == 3) ? ParticleOrchestraType.WallOfFleshGoatMountFlames : ParticleOrchestraType.FlameWaders, new ParticleOrchestraSettings
		{
			PositionInWorld = positionInWorld
		});
		return true;
	}

	/// <summary>Player.KillMe (every player this client sees die): death cosmetics and Lucy's death lines.</summary>
	public static void OnPlayerKilled(Player player, PlayerDeathReason damageSource)
	{
		if (Main.netMode == 2 || Main.gameMenu || player.dead)
		{
			return;
		}
		try
		{
			bool mine = player.whoAmI == Main.myPlayer;
			if (mine || DeathCosmeticsForAll)
			{
				EmitDeathParticles(player);
				EmitDeathEffect(player);
				EmitDeathSound(player);
			}
			if ((mine && LucyDeathMessageMode == 1) || (!mine && LucyDeathMessageMode == 2) || LucyDeathMessageMode == 3)
			{
				Vector2 position = new Vector2(player.Top.X, player.Top.Y - 24f);
				Vector2 velocity = new Vector2(Main.rand.NextFloatDirection() * 7f, -2f + Main.rand.NextFloat() * -2f);
				if (!mine && damageSource != null && damageSource.SourcePlayerIndexForMods == Main.myPlayer)
				{
					byte line;
					do
					{
						line = (byte)Main.rand.Next(1, 10);
					}
					while (line == 4);
					LucyAxeMessage.Create(LucyAxeMessage.MessageSource.ChoppedTree, position, velocity, forced: true, line);
				}
				else
				{
					LucyAxeMessage.Create(LucyAxeMessage.MessageSource.Storage, position, velocity, forced: true);
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static void EmitDeathParticles(Player player)
	{
		if (!DeathParticleCosmeticEnabled)
		{
			return;
		}
		ParticleOrchestraType type = (ParticleOrchestraType)DeathParticleCosmeticId;
		if (Array.IndexOf(DeathParticles, type) < 0)
		{
			return;
		}
		// Short sword-like hits look better stacked; the others are big enough on their own.
		bool stacked = type == ParticleOrchestraType.Keybrand || type == ParticleOrchestraType.StellarTune || type == ParticleOrchestraType.RainbowRodHit || (type >= ParticleOrchestraType.NightsEdge && type <= ParticleOrchestraType.ChlorophyteLeafCrystalShot) || type == ParticleOrchestraType.ShimmerArrow;
		int count = stacked ? 6 : 1;
		for (int i = 0; i < count; i++)
		{
			ParticleOrchestrator.BroadcastOrRequestParticleSpawn(type, new ParticleOrchestraSettings
			{
				PositionInWorld = player.Center
			});
		}
	}

	private static void EmitDeathSound(Player player)
	{
		ParticleOrchestraType type = (ParticleOrchestraType)DeathSoundCosmeticId;
		if (DeathSoundCosmeticEnabled && Array.IndexOf(DeathSounds, type) >= 0)
		{
			ParticleOrchestrator.BroadcastOrRequestParticleSpawn(type, new ParticleOrchestraSettings
			{
				PositionInWorld = player.Center
			});
		}
	}

	private static void EmitDeathEffect(Player player)
	{
		int id = DeathGoreCosmeticId;
		if (!DeathGoreCosmeticEnabled || Array.IndexOf(DeathEffects, id) < 0)
		{
			return;
		}
		int x = (int)(player.position.X / 16f);
		int y = (int)(player.position.Y / 16f);
		switch (id)
		{
		case 11:
			GoreBurst(x, y, 11, 12);
			break;
		case 61:
			GoreBurst(x, y, 61, 62);
			break;
		case 276:
			GoreBurst(x, y, 276, 282);
			break;
		case 375:
			GoreBurst(x, y, 375, 376);
			break;
		case 570:
			GoreBurst(x, y, 570, 571);
			break;
		case 825:
			GoreBurst(x, y, 825, 826);
			break;
		case 861:
			GoreBurst(x, y, 861, 861);
			break;
		case -2:
			GoreBurst(x, y, 907, 909);
			break;
		case -3:
			GoreBurst(x, y, 16, 17);
			GoreBurst(x, y, 16, 17);
			break;
		case 16:
		case 17:
		case 99:
		case 907:
		case 908:
		case 909:
			for (int i = 0; i < 3; i++)
			{
				TreeGore(x, y, id);
			}
			break;
		case 411:
		case 412:
		case 413:
		case 414:
		case 415:
		case 1218:
			for (int i = 0; i < 5; i++)
			{
				TreeGore(x, y, id);
			}
			break;
		default:
			TreeGore(x, y, id);
			break;
		}
	}

	private static void GoreBurst(int x, int y, int first, int last)
	{
		for (int gore = first; gore <= last; gore++)
		{
			TreeGore(x, y, gore);
		}
	}

	/// <summary>The tree-shake gore effect, sent through the server (packet 112) so everyone sees it.</summary>
	private static void TreeGore(int x, int y, int gore)
	{
		if (Main.netMode == 0)
		{
			WorldGen.TreeGrowFX(x, y, 1, gore);
		}
		else
		{
			NetMessage.SendData(112, -1, -1, null, 1, x, y, 1f, gore);
		}
	}

	/// <summary>Lucy Messages "Forced On (Everyone)": Lucy's idle lines float over every player.</summary>
	public static void TryPlayingLucyIdleMessage_AllPlayers()
	{
		LucyAxeMessage.MessageSource source = LucyAxeMessage.MessageSource.Idle;
		if (LucyAxeMessage._messageCooldownsByType[(int)source] > 0)
		{
			return;
		}
		for (int i = 0; i < Main.maxPlayers; i++)
		{
			if (Main.player[i].active)
			{
				LucyAxeMessage.Create(source, Main.player[i].Top, new Vector2(Main.rand.NextFloatDirection() * 7f, -2f + Main.rand.NextFloat() * -2f), forced: true);
			}
		}
		LucyAxeMessage.PutMessageTypeOnCooldown(source, Main.rand.Next(3600, 7200));
	}

	#region Chat commands

	/// <summary>.shp / .shimmerparticles</summary>
	internal static void ShimmerParticlesCommand(CommandContext command)
	{
		ShimmerWalkParticles = !ShimmerWalkParticles;
		ClientChat.Info("Shimmer Walking Particles " + (ShimmerWalkParticles ? "En" : "Dis") + "abled.");
	}

	/// <summary>.rgboutline / .rgbout</summary>
	internal static void RgbOutlineCommand(CommandContext command)
	{
		RgbPlayerOutline = !RgbPlayerOutline;
		ClientChat.Info("Rainbow outline for self " + (RgbPlayerOutline ? "En" : "Dis") + "abled.");
	}

	/// <summary>.rgbnpcicon / .rgbnpc</summary>
	internal static void RgbNpcIconCommand(CommandContext command)
	{
		RgbNpcIcons = !RgbNpcIcons;
		ClientChat.Info("Rainbow NPC icons " + (RgbNpcIcons ? "En" : "Dis") + "abled.");
	}

	/// <summary>.rgbbossicon / .rgbboss</summary>
	internal static void RgbBossIconCommand(CommandContext command)
	{
		RgbBossIcons = !RgbBossIcons;
		ClientChat.Info("Rainbow boss icons " + (RgbBossIcons ? "En" : "Dis") + "abled.");
	}

	#endregion
}
