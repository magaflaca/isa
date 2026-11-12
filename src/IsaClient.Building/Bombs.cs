using System;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Utilities;
using IsaClient.Core;
using IsaClient.Networking;

namespace IsaClient.Building;

/// <summary>World Edit bomb filler: fills a selection with bombs (.bombs, .btype, .bamount).</summary>
public static class Bombs
{
	public static bool BombFiller;

	public static int BombFillerType = 0;

	public static int BombAmount = 25;

	public static async void BombFillSelection()
	{
		await Task.Run(delegate
		{
			try
			{
				if (Main.rand == null)
				{
					Main.rand = new UnifiedRandom();
				}
				int num = 911;
				num = BombFillerType switch
				{
					1 => 903, 
					2 => 904, 
					3 => 905, 
					4 => 906, 
					5 => Main.rand.Next(903, 907), 
					_ => 911, 
				};
				if (!ServerDetection.DidTShockDisableYou)
				{
					if (Selection.SelectionA == Point.Zero || Selection.SelectionB == Point.Zero || BombAmount < 0)
					{
						ClientChat.Info("Invalid selection points.");
						BombFiller = false;
					}
					else
					{
						int num2 = Math.Min(Selection.SelectionA.X, Selection.SelectionB.X);
						int num3 = Math.Min(Selection.SelectionA.Y, Selection.SelectionB.Y);
						int num4 = Math.Abs(Selection.SelectionA.X - Selection.SelectionB.X);
						int num5 = Math.Abs(Selection.SelectionA.Y - Selection.SelectionB.Y);
						UnifiedRandom unifiedRandom = new UnifiedRandom();
						Vector2 val = default(Vector2);
						for (int i = 0; i < BombAmount; i++)
						{
							float num6 = unifiedRandom.Next(num2 * 16, (num2 + num4) * 16);
							float num7 = unifiedRandom.Next(num3 * 16, (num3 + num5) * 16);
							val = new Vector2(num6, num7);
							Point val2 = val.ToTileCoordinates();
							if (WorldGen.InWorld(val2.X, val2.Y, 1) && Framing.GetTileSafely(val2.X, val2.Y) != null && TileItemLookup.IsTile(Framing.GetTileSafely(val2.X, val2.Y)))
							{
								int num8 = Projectile.NewProjectile(Main.LocalPlayer.GetProjectileSource_Misc((BombFillerType != 5) ? num : Main.rand.Next(903, 907)), val.X, val.Y, 0f, 0f, (BombFillerType != 5) ? num : Main.rand.Next(903, 907), 0, 0f, Main.myPlayer);
								Main.projectile[num8].Kill();
								WorldGen.SquareTileFrame(val2.X, val2.Y);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				ClientChat.Info($"Bomb Filler error!\n{ex}");
				ClientLog.LogException(ex);
				BombFiller = false;
			}
		});
	}

	#region Chat commands

	/// <summary>.bamount</summary>
	internal static void BombAmountCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out var result35))
			{
				BombAmount = result35;
				ClientChat.Info($"Bomb amount set to {BombAmount}.");
			}
		}
		else
		{
			ClientChat.Info($"Current bomb amount: {BombAmount}");
		}
	}

	/// <summary>.btype</summary>
	internal static void BombTypeCommand(CommandContext command)
	{
		string[] names = new string[6] { "Dirt Bombs.", "Water Bombs.", "Lava Bombs.", "Honey Bombs.", "Dry Bombs.", "Random Liquid Bombs." };
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out var result19) && result19 >= 0 && result19 <= 5)
			{
				BombFillerType = result19;
				ClientChat.Info("Bomb type set to " + names[BombFillerType]);
			}
		}
		else
		{
			ClientChat.Info("Current bomb type: " + names[BombFillerType]);
		}
	}

	/// <summary>.bombs / .bombfiller</summary>
	internal static void BombFillerCommand(CommandContext command)
	{
		BombFiller = !BombFiller;
		ClientChat.Info("Auto bomb filler " + (BombFiller ? "en" : "dis") + "abled.");
	}

	#endregion
}
