using System;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.IO;
using IsaClient.Community;
using IsaClient.Gameplay;
using IsaClient.Movement;
using IsaClient.Rendering;

namespace Terraria;

public partial class Player
{
	/// <summary>IsaClient: UI portraits (Player List) are drawn at full brightness; unlike socialIgnoreLight, PlayerFrame never clears it.</summary>
	public bool isaAlwaysLit;

	// Fields some TerraAngel forks added to Player: their editors bind ImGui controls to them by ref, so they have to be
	// real fields. The TerraAngel Community plugin gives them their effect (Harmony); without it they do nothing.
	public int ModStatDefense;

	public int ModDashType;

	public float ModThorns;

	public int ModLifeRegen;

	public int ModAggro;

	public int ModArmorPenetration;

	public bool ModNoKnockback;

	public bool ModShimmerImmune;

	public bool ModLavaImmune;

	public int NaturalStatDefense;

	public int NaturalDashType;

	public float NaturalThorns;

	public int NaturalLifeRegen;

	public int NaturalAggro;

	public int NaturalArmorPenetration;

	public bool NaturalNoKnockback;

	public bool NaturalShimmerImmune;

	public bool NaturalLavaImmune;

	public bool UseModStatDefense;

	public bool UseModDashType;

	public bool UseModThorns;

	public bool UseModAggro;

	public bool UseModLifeRegen;

	public bool UseModArmorPenetration;

	public bool UseModShimmerImmune;

	public bool UseModLavaImmune;

	public bool functionalGhost;

	public bool isProjectileSpammer;

	public int spammedProjectileCount;

	public int blockProjectileRecieveTime;

	public bool isSoundSpammer;

	public int spammedSoundCount;

	public int blockSoundRecieveTime;

	public bool isParticleSpammer;

	public int spammedParticleCount;

	public int blockParticleRecieveTime;

	public bool isNebulaSpammer;

	public int spammedNebulaCount;

	public int blockNebulaRecieveTime;

	public int playerSyncsWhileNotActive;

	public bool spawned = true;

	public bool wasDetectedInVanish;

	public bool joining;

	/// <summary>Cheats → Player → Ignore Crowd Control (local player only).</summary>
	public bool IsIgnoringCC
	{
		get
		{
			if (whoAmI == Main.myPlayer)
			{
				return PlayerCheats.IgnoreCC;
			}
			return false;
		}
	}

	public bool UsingExtraAccessorySlots
	{
		get
		{
			if (whoAmI == Main.myPlayer)
			{
				return PlayerCheats.ExtraAccSlots;
			}
			return false;
		}
	}

	public bool IsIgnoringDamageBlocks
	{
		get
		{
			if (whoAmI == Main.myPlayer)
			{
				return PlayerCheats.IgnoreDamageBlocks;
			}
			return false;
		}
	}

	public bool IsIgnoringLiquids
	{
		get
		{
			if (whoAmI == Main.myPlayer)
			{
				return PlayerCheats.IgnoreLiquids;
			}
			return false;
		}
	}

	/// <summary>
	/// Set the first time this player's zone packet (36) arrives with zone5 bit 1: the zMod signature. zMod sets that bit in
	/// every zone packet it sends (when joining, when a zone changes and every 15 s), and the server forwards it to everyone.
	/// Kept until the player leaves, so the badge never flickers.
	/// </summary>
	public bool zModDetected;

	/// <summary>
	/// Set when this player shows TerraAngel's presence mark (TerraAngel's "Broadcast Presence", on by default there): a
	/// controls packet (13) whose camera target is (-114514, -1919810), or the older 1-damage hurt packet (117) from NPC
	/// 203. Read the same way TerraAngel reads it; kept until the player leaves.
	/// </summary>
	public bool terraAngelDetected;

	/// <summary>IsaClient Player List: 0 = vanilla/unknown client, 1 = IsaClient user, 2 = zMod user, 3 = TerraAngel user.</summary>
	public int IsaClientKind
	{
		get
		{
			if (whoAmI == Main.myPlayer)
			{
				return PlayerListPanel.ShareMyInfo ? 1 : 0;
			}
			BitsByte b = zone5;
			if (b[1] || zModDetected)
			{
				return 2;
			}
			if (b[5] && b[6] && b[7])
			{
				return 1;
			}
			if (terraAngelDetected)
			{
				return 3;
			}
			return 0;
		}
	}

	/// <summary>True while the local player has noclip enabled.</summary>
	public bool IsNoclipping => whoAmI == Main.myPlayer && Noclip.Enabled;

	public bool ShouldRapidPosUpdate => whoAmI == Main.myPlayer && (Noclip.RapidPosUpdate || IsNoclipping || Combat.ShimmerGodMode || (Travel.SurfaceFrictionMode > 1 && velocity.Y == 0f)) && !Noclip.JitteringMyPosRn;

	public bool IsShimmerGodMode => whoAmI == Main.myPlayer && Combat.ShimmerGodMode;

	/// <summary>Cheat ghost: number keys 1-0, hotbar +/- and the mouse wheel, like the normal update path.</summary>
	private void GhostCheatHotbar()
	{
		if (Main.drawingPlayerChat || Main.editSign || Main.editChest || PlayerInput.WritingText)
		{
			return;
		}
		TriggersSet t = PlayerInput.Triggers.Current;
		int sel = -1;
		if (t.Hotbar1) sel = 0;
		if (t.Hotbar2) sel = 1;
		if (t.Hotbar3) sel = 2;
		if (t.Hotbar4) sel = 3;
		if (t.Hotbar5) sel = 4;
		if (t.Hotbar6) sel = 5;
		if (t.Hotbar7) sel = 6;
		if (t.Hotbar8) sel = 7;
		if (t.Hotbar9) sel = 8;
		if (t.Hotbar10) sel = 9;
		if (sel >= 0 && itemAnimation == 0 && selectedItem != sel)
		{
			selectedItemState.Select(sel);
			SoundEngine.PlaySound(12);
		}
		// Clicking a hotbar slot sets changeItem (Main.GUIHotbarDrawInner); the normal update path applies it.
		if (changeItem >= 0)
		{
			if (itemAnimation == 0)
			{
				selectedItemState.Select(changeItem);
			}
			changeItem = -1;
		}
		if (Main.playerInventory)
		{
			Main.DoScrollingInInventory();
		}
		else if (itemAnimation == 0)
		{
			HandleHotbarControls();
		}
		selectedItemState.Update();
	}

	/// <summary>IsaClient: one use of an item shot while ShotPreview captures (nothing is really spawned).</summary>
	public void IsaPreviewShoot(Item sItem)
	{
		ItemCheck_Shoot(whoAmI, sItem, sItem.damage, withAudioVisualFeedback: false);
	}

	/// <summary>Custom shoot styles (random projectiles, raining random projectiles, random projectiles around the cursor, Flinxinator).</summary>
	private void IsaCustomShoot(int i, Item sItem, int weaponDamage)
	{
		int projToShoot = sItem.shoot;
		float speed = sItem.shootSpeed;
		if (sItem.melee && projToShoot >= 0 && projToShoot < ProjectileID.Count && !ProjectileID.Sets.NoMeleeSpeedVelocityScaling[projToShoot])
		{
			speed /= meleeSpeed;
		}
		bool canShoot = false;
		int Damage = weaponDamage;
		float KnockBack = sItem.knockBack;
		int usedAmmoItemId = 0;
		if (sItem.useAmmo > 0)
		{
			PickAmmo(sItem, ref projToShoot, ref speed, ref canShoot, ref Damage, ref KnockBack, out usedAmmoItemId, ItemID.Sets.gunProj[sItem.type]);
		}
		else
		{
			canShoot = true;
		}
		if (!canShoot)
		{
			return;
		}
		KnockBack = GetWeaponKnockback(sItem, KnockBack);
		IEntitySource source = GetProjectileSource_Item_WithPotentialAmmo(sItem, usedAmmoItemId);
		Vector2 origin = RotatedRelativePoint(MountedCenter);
		Vector2 aim = Main.MouseWorld - origin;
		if (aim == Vector2.Zero)
		{
			aim = new Vector2((float)direction, 0f);
		}
		aim.Normalize();
		aim *= Math.Max(speed, 1f);
		switch ((IsaClient.Gameplay.ItemShootBehavior)sItem.itemShootBehavior)
		{
		case IsaClient.Gameplay.ItemShootBehavior.Custom_RandomProj:
			IsaShootRandom(source, origin, aim, Damage, KnockBack, i);
			break;
		case IsaClient.Gameplay.ItemShootBehavior.Custom_RandomProjRain:
		{
			int count = Math.Max(1, (int)IsaClient.Gameplay.ShootBehaviors.RandomCount);
			if (Main.rand.Next(count) == 0)
			{
				count++;
			}
			for (int k = 0; k < count; k++)
			{
				Vector2 from = new Vector2(base.position.X + (float)width * 0.5f + (float)Main.rand.Next(201) * (0f - (float)direction) + (Main.MouseWorld.X - base.position.X), MountedCenter.Y - 600f);
				from.X = (from.X * 10f + base.Center.X) / 11f + (float)Main.rand.Next(-100, 101);
				from.Y -= 150 * k;
				Vector2 velocity = Main.MouseWorld - from;
				if (velocity.Y < 0f)
				{
					velocity.Y *= -1f;
				}
				if (velocity.Y < 20f)
				{
					velocity.Y = 20f;
				}
				velocity.Normalize();
				velocity *= Math.Max(speed, 1f);
				velocity.X += (float)Main.rand.Next(-40, 41) * 0.03f;
				velocity.Y += (float)Main.rand.Next(-40, 41) * 0.03f;
				velocity.X *= (float)Main.rand.Next(75, 150) * 0.01f;
				from.X += Main.rand.Next(-50, 51);
				IsaShootRandom(source, from, velocity, Damage, KnockBack, i);
			}
			break;
		}
		case IsaClient.Gameplay.ItemShootBehavior.Custom_RandomProjAroundCursor:
		{
			for (int k = 0; k < Math.Max(1, (int)IsaClient.Gameplay.ShootBehaviors.RandomCount); k++)
			{
				Vector2 target = Main.MouseWorld;
				if (GetSparkleGuitarTarget(out var validTargets))
				{
					NPC nPC = validTargets[Main.rand.Next(validTargets.Count)];
					target = nPC.Center + nPC.velocity * 20f;
				}
				else
				{
					target += Main.rand.NextVector2Circular(24f, 24f);
				}
				Vector2 heading = Main.rand.NextVector2CircularEdge(1f, 1f);
				float projectileSpeed = 12f + Main.rand.NextFloat() * 2f;
				IsaShootRandom(source, target - heading * projectileSpeed * 30f, heading * projectileSpeed, Damage, KnockBack, i);
			}
			break;
		}
		case IsaClient.Gameplay.ItemShootBehavior.Custom_SpawnAtCursor_Flinxinator:
		{
			int index = SpawnMinionOnCursor(source, i, projToShoot, sItem.damage, KnockBack);
			if (index >= 0 && index < Main.maxProjectiles)
			{
				Projectile projectile = Main.projectile[index];
				projectile.hitsInvincibleNpc = true;
				projectile.extraUpdates = 10;
				projectile.timeLeft *= 10;
				projectile.stopsDealingDamageAfterPenetrateHits = false;
				projectile.penetrate = 10000;
				projectile.localNPCHitCooldown = 0;
				projectile.restrikeDelay = 0;
			}
			break;
		}
		}
	}

	/// <summary>One random projectile (re-rolled while it is one that only works held by the player).</summary>
	private void IsaShootRandom(IEntitySource source, Vector2 from, Vector2 velocity, int damage, float knockBack, int owner)
	{
		for (int attempt = 0; attempt < 50; attempt++)
		{
			int type = IsaClient.Gameplay.ShootBehaviors.RandomProjectile();
			int index = Projectile.NewProjectile(source, from, velocity, type, damage, knockBack, owner);
			if (index < 0 || index >= Main.maxProjectiles)
			{
				return;
			}
			Projectile projectile = Main.projectile[index];
			if (!projectile.active || projectile.owner != Main.myPlayer || !IsaClient.Gameplay.ShootBehaviors.NeedsHolding(projectile))
			{
				return;
			}
			projectile.Kill();
		}
	}

	/// <summary>Writes a character file made by PlayerFileData.IsaCreateCopyFile (never marked server-side, so it is saved even on SSC servers).</summary>
	public static void IsaWritePlayerFile(PlayerFileData playerFile)
	{
		lock (IOLock)
		{
			Directory.CreateDirectory(Main.PlayerPath);
			InternalSavePlayerFile(playerFile);
		}
	}

	/// <summary>A deep copy (items, loadouts, look) that never touches the local player.</summary>
	/// <summary>IsaClient: a field-by-field copy (arrays shared) used to save a different character without touching this one.</summary>
	public Player IsaShallowCopy()
	{
		return (Player)MemberwiseClone();
	}

	public Player SafeSerializedClone()
	{
		return SerializedClone();
	}

	private void GhostCheatInventoryKey()
	{
		// Cheat ghost (flying while alive): keep the inventory and item usage working like a normal player.
		if (controlInv)
		{
			if (releaseInventory)
			{
				ToggleInv();
			}
			releaseInventory = false;
		}
		else
		{
			releaseInventory = true;
		}
	}

	private void GhostCheatUseItems()
	{
		if (whoAmI == Main.myPlayer && PlayerTools.GhostCheat && !Main.drawingPlayerChat && !Main.editSign && !Main.editChest && !Main.blockInput)
		{
			// Let items be picked up, selected and used while cheat-ghosting.
			GhostCheatHotbar();
			GrabItems(whoAmI);
			if (itemAnimation == 0 && ItemTimeIsZero && reuseDelay == 0)
			{
				dropItemCheck();
			}
			if (Main.netMode != 2 && mount.Type != 8)
			{
				ItemCheck_ManageRightClickFeatures();
			}
			ItemCheckWrapped(whoAmI);
			PlayerFrame();
		}
	}

	private void ApplyThinVanillaBorder(int num)
	{
		if (WorldView.KeepThinVanillaBorder && num < 640 && !IsNoclipping)
		{
			if (position.X < Main.leftWorld + 640f && position.X >= Main.leftWorld + 640f - 24f)
			{
				Main.cameraX = 0f;
				position.X = Main.leftWorld + 640f;
				velocity.X = 0f;
			}
			if (position.X + (float)width > Main.rightWorld - 640f && position.X + (float)width <= Main.rightWorld - 640f + 24f)
			{
				Main.cameraX = 0f;
				position.X = Main.rightWorld - 640f - (float)width;
				velocity.X = 0f;
			}
			if (position.Y < Main.topWorld + 640f && position.Y >= Main.topWorld + 640f - 24f)
			{
				Main.cameraY = 0f;
				position.Y = Main.topWorld + 640f;
				if (velocity.Y < 0.11f)
				{
					velocity.Y = 0.11f;
				}
			}
			if (position.Y > Main.bottomWorld - 640f - (float)height && position.Y <= Main.bottomWorld - 640f + 24f - (float)height)
			{
				Main.cameraY = 0f;
				position.Y = Main.bottomWorld - 640f - (float)height;
				velocity.Y = 0f;
			}
		}
	}

	private void MarkClientInZone5()
	{
		// IsaClient user signature broadcast in the free bits of zone5 (base game only uses zone5[0]=ZoneShimmer).
		// zMod marks its users with zone5[1], so IsaClient sets 5,6,7 WITHOUT bit 1: zMod clients see nothing,
		// IsaClient clients see the I statue.
		zone5[1] = false;
		if (PlayerListPanel.ShareMyInfo)
		{
			zone5[5] = true;
			zone5[6] = true;
			zone5[7] = true;
		}
		else
		{
			zone5[5] = false;
			zone5[6] = false;
			zone5[7] = false;
		}
	}

	private void ItemCheck_ShootWithStyle(int i, Item sItem, int weaponDamage, bool withAudioVisualFeedback)
	{
		// IsaClient Held Item: "Shoot Style" makes the item shoot like another weapon (see IsaShootBehaviors), and
		// everything it spawns inherits "Hits Invincible NPCs".
		bool hitsInvincible = Projectile.IsaSpawnHitsInvincible;
		Projectile.IsaSpawnHitsInvincible = sItem.hitsInvincibleNpc && i == Main.myPlayer;
		try
		{
			if (i == Main.myPlayer && IsaClient.Gameplay.ShootBehaviors.IsCustom(sItem.itemShootBehavior))
			{
				IsaCustomShoot(i, sItem, weaponDamage);
			}
			else
			{
				ItemCheck_Shoot_Inner(i, sItem, weaponDamage, withAudioVisualFeedback, (i == Main.myPlayer) ? IsaClient.Gameplay.ShootBehaviors.ShootAs(sItem) : sItem.type);
			}
		}
		finally
		{
			Projectile.IsaSpawnHitsInvincible = hitsInvincible;
		}
	}
}
