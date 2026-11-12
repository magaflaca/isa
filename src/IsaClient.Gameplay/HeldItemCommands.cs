using System.Globalization;
using Terraria.ID;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Gameplay;

/// <summary>Held item chat commands (.dmg, .ut, .at, .kb, .scale, .proj, .shootspeed, .shootstyle, .sha, .crtl, .crwl, .plst, .hlf, .hlm, .rngamount).</summary>
public static class HeldItemCommands
{
	public static void Run(string command, string[] args)
	{
		Item item = Main.LocalPlayer.HeldItem;
		string value = ((args.Length > 1) ? args[1] : null);
		switch (command)
		{
		case "rngam":
		case "rngamount":
			if (value == null)
			{
				ClientChat.Info("Current random projectile amount: " + ShootBehaviors.RandomCount);
			}
			else if (ushort.TryParse(value, out var amount) && amount <= 1000)
			{
				ShootBehaviors.RandomCount = amount;
				ClientChat.Info("Random projectile amount for RNG shoot styles set to " + amount);
			}
			else
			{
				ClientChat.Error("Invalid amount.");
			}
			return;
		case "shtst":
		case "shootstyle":
			if (value == null)
			{
				ClientChat.Info("Current Shoot Style: " + ShootBehaviors.Describe(item) + " (ID: " + item.itemShootBehavior + ")");
			}
			else if (int.TryParse(value, out var style) && style >= 0 && style < ShootBehaviors.Count)
			{
				item.itemShootBehavior = style;
				ClientChat.Info("Shoot Style set to " + ShootBehaviors.Describe(item) + " (ID: " + style + ")");
				Sync();
			}
			else
			{
				ClientChat.Error("Invalid Shoot Style (0-" + (ShootBehaviors.Count - 1) + ").");
			}
			return;
		}
		if (value == null)
		{
			ClientChat.Error("Value Required.");
			return;
		}
		switch (command)
		{
		case "ut":
		case "usetime":
			if (int.TryParse(value, out var useTime) && useTime >= 0)
			{
				item.useTime = useTime;
				Done("Usetime set to " + useTime);
			}
			else
			{
				Invalid("Invalid Amount.");
			}
			break;
		case "at":
		case "animationtime":
			if (int.TryParse(value, out var animation) && animation > 0)
			{
				item.useAnimation = animation;
				Done("Animation Time set to " + animation);
			}
			else
			{
				Invalid("Invalid Amount.");
			}
			break;
		case "crtl":
		case "createtile":
			if (int.TryParse(value, out var tile) && tile >= -1 && tile < TileID.Count)
			{
				item.createTile = tile;
				Done("CreateTile set to " + ((tile > -1) ? TileID.Search.GetName(tile) : "None") + " (ID: " + tile + ")");
			}
			else
			{
				Invalid("Invalid Tile ID.");
			}
			break;
		case "crwl":
		case "createwall":
			if (int.TryParse(value, out var wall) && wall >= -1 && wall < WallID.Count)
			{
				item.createWall = wall;
				Done("CreateWall set to " + ((wall > 0) ? WallID.Search.GetName(wall) : "None") + " (ID: " + wall + ")");
			}
			else
			{
				Invalid("Invalid Wall ID.");
			}
			break;
		case "plst":
		case "placestyle":
			if (int.TryParse(value, out var placeStyle) && placeStyle >= 0)
			{
				item.placeStyle = placeStyle;
				Done("PlaceStyle set to " + placeStyle);
			}
			else
			{
				Invalid("Invalid Amount.");
			}
			break;
		case "shootperuse":
		case "shootamount":
		case "spu":
		case "sha":
			if (int.TryParse(value, out var times) && times >= 1 && times <= ushort.MaxValue)
			{
				item.timesToShoot = (ushort)times;
				Done("Held item will now shoot " + times + " times.");
			}
			else
			{
				Invalid("Invalid Amount.");
			}
			break;
		case "scale":
		case "scl":
		case "itemsize":
			if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) && scale > 0f)
			{
				item.scale = scale;
				Done("Scale of held item set to " + scale.ToString(CultureInfo.InvariantCulture));
			}
			else
			{
				Invalid("Invalid Amount.");
			}
			break;
		case "proj":
		case "projectile":
		case "shoot":
			if (int.TryParse(value, out var projectile) && projectile >= 0 && projectile < ProjectileID.Count)
			{
				item.shoot = projectile;
				if (projectile > 0 && item.shootSpeed <= 0f)
				{
					item.shootSpeed = 10f;
				}
				Done("Projectile set to " + ((projectile > 0) ? Lang.GetProjectileName(projectile).Value : "None") + " (ID: " + projectile + ")");
			}
			else
			{
				Invalid("Invalid Projectile ID.");
			}
			break;
		case "projspeed":
		case "projectilespeed":
		case "shootspeed":
			if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
			{
				item.shootSpeed = speed;
				Done("Projectile speed set to " + speed.ToString(CultureInfo.InvariantCulture));
			}
			else
			{
				Invalid("Invalid Projectile Speed.");
			}
			break;
		case "damage":
		case "dmg":
			if (int.TryParse(value, out var damage) && damage >= 0)
			{
				item.damage = damage;
				Done("Damage set to " + damage);
			}
			else
			{
				Invalid("Invalid Damage.");
			}
			break;
		case "hlf":
		case "heallife":
			if (int.TryParse(value, out var healLife) && healLife >= 0)
			{
				item.healLife = healLife;
				Done("HealLife set to " + healLife);
			}
			else
			{
				Invalid("Invalid Amount.");
			}
			break;
		case "hlm":
		case "healmana":
			if (int.TryParse(value, out var healMana) && healMana >= 0)
			{
				item.healMana = healMana;
				Done("HealMana set to " + healMana);
			}
			else
			{
				Invalid("Invalid Amount.");
			}
			break;
		case "kb":
		case "knockback":
			if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var knockBack) && knockBack >= 0f)
			{
				item.knockBack = knockBack;
				Done("Knockback set to " + knockBack.ToString(CultureInfo.InvariantCulture));
			}
			else
			{
				Invalid("Invalid Knockback.");
			}
			break;
		}
	}

	private static void Done(string message)
	{
		ClientChat.Info(message);
		Sync();
	}

	private static void Invalid(string message)
	{
		ClientChat.Error(message);
	}

	private static void Sync()
	{
		if (Main.LocalPlayer.selectedItem == 58)
		{
			Main.mouseItem = Main.LocalPlayer.HeldItem.Clone();
		}
	}

	#region Chat commands

	/// <summary>.ut / .usetime / .at / .animationtime / .shtst / .shootstyle / .rngam / .rngamount / .crtl / .createtile / .crwl / .createwall / .plst / .placestyle / .shootperuse / .shootamount / .spu / .sha / .scale / .scl / .itemsize / .proj / .projectile / .shoot / .projspeed / .projectilespeed / .shootspeed / .damage / .dmg / .hlf / .heallife / .hlm / .healmana / .kb / .knockback</summary>
	internal static void ProjectileSpeedCommand(CommandContext command)
	{
		Run(command.Name, command.Parts);
	}

	#endregion
}
