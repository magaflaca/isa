using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using IsaClient.Core;

namespace IsaClient.Rendering;

/// <summary>Draws player, NPC, projectile and item hitboxes.</summary>
public static class Hitboxes
{
	[Setting("RenderPlayerHitboxes", Remembered = true)]
	public static bool PlayerHitboxes;

	[Setting("RenderNpcHitboxes", Remembered = true)]
	public static bool NpcHitboxes;

	[Setting("RenderProjectileHitboxes", Remembered = true)]
	public static bool ProjectileHitboxes;

	[Setting("RenderDroppedItemHitboxes", Remembered = true)]
	public static bool ItemHitboxes;

	/// <summary>0 border, 1 border with overlay, 2 flat overlay.</summary>
	[Setting("HitboxRenderType", Remembered = true, Min = 0, Max = 2)]
	public static int HitBoxesTexture;

	public static bool UseExtraProjHitboxes;

	public static readonly List<Rectangle> ExtraProjHitboxes = new List<Rectangle>();

	public static bool AnyHitboxesShown
	{
		get
		{
			if (!PlayerHitboxes && !NpcHitboxes && !ProjectileHitboxes)
			{
				return ItemHitboxes;
			}
			return true;
		}
	}

	/// <summary>Projectile.Colliding: with .extraproj, the rectangles projectiles really hit with are drawn too.</summary>
	public static void RecordExtraProjectileHitbox(Rectangle rect)
	{
		if (!UseExtraProjHitboxes || !ProjectileHitboxes || Main.dedServ)
		{
			return;
		}
		lock (ExtraProjHitboxes)
		{
			if (ExtraProjHitboxes.Count < 2000 && !ExtraProjHitboxes.Contains(rect))
			{
				ExtraProjHitboxes.Add(rect);
			}
		}
	}

	/// <summary>Main.DoDraw, world space: the hitboxes of players, NPCs, projectiles and items.</summary>
	public static void DrawHitboxes(SpriteBatch spriteBatch)
	{
		try
		{
			if (ItemHitboxes)
			{
				for (int i = 0; i < Main.maxItems; i++)
				{
					WorldItem item = Main.item[i];
					if (item != null && item.active)
					{
						DrawEntityHitbox(spriteBatch, item.Hitbox, new Color(130, 0, 255) * ((item.width > 4 && item.height > 4) ? 0.75f : 0.95f));
					}
				}
			}
			if (NpcHitboxes)
			{
				for (int j = 0; j < Main.maxNPCs; j++)
				{
					NPC npc = Main.npc[j];
					if (!npc.active)
					{
						continue;
					}
					bool big = npc.width > 4 && npc.height > 4;
					Color color = Color.Red * (big ? 0.85f : 0.95f);
					if (npc.CountsAsACritter || npc.townNPC || npc.friendly)
					{
						color = new Color(0, 255, 0) * (big ? 0.75f : 0.85f);
					}
					else if (npc.damage <= 0)
					{
						color = Color.Orange * (big ? 0.75f : 0.85f);
					}
					else if (npc.dontTakeDamage)
					{
						color = new Color(70, 0, 0) * (big ? 0.85f : 0.95f);
					}
					DrawEntityHitbox(spriteBatch, npc.Hitbox, color);
				}
			}
			if (ProjectileHitboxes)
			{
				for (int k = 0; k < Main.maxProjectiles; k++)
				{
					Projectile projectile = Main.projectile[k];
					if (!projectile.active)
					{
						continue;
					}
					bool big = projectile.width > 4 && projectile.height > 4;
					Color color = Color.Gold * (big ? 1.25f : 1f);
					if (projectile.hostile)
					{
						color = new Color(255, 0, 100) * (big ? 0.85f : 1.25f);
					}
					else if (projectile.owner == Main.myPlayer)
					{
						color = Color.LimeGreen * (big ? 0.65f : 1.25f);
					}
					DrawEntityHitbox(spriteBatch, projectile.Hitbox, color);
				}
				lock (ExtraProjHitboxes)
				{
					foreach (Rectangle rect in ExtraProjHitboxes)
					{
						DrawEntityHitbox(spriteBatch, rect, new Color(220, 80, 0) * ((rect.Width > 4 && rect.Height > 4) ? 0.6f : 0.85f));
					}
					ExtraProjHitboxes.Clear();
				}
			}
			if (PlayerHitboxes)
			{
				for (int m = 0; m < Main.maxPlayers; m++)
				{
					Player other = Main.player[m];
					if (other.active && m != Main.myPlayer)
					{
						DrawEntityHitbox(spriteBatch, other.Hitbox, (other.dead || other.ghost) ? (Color.DarkGray * 0.65f) : (Color.Turquoise * 0.75f));
					}
				}
				Player me = Main.LocalPlayer;
				Color mine = (me.dead || me.ghost) ? (Color.DarkGray * 0.65f) : (Main.hslToRgb(Main.GlobalTimeWrappedHourly * 0.25f % 1f, 1f, 0.5f) * 0.65f);
				DrawEntityHitbox(spriteBatch, me.Hitbox, mine);
			}
		}
		catch
		{
		}
	}

	private static void DrawEntityHitbox(SpriteBatch spriteBatch, Rectangle rect, Color color)
	{
		bool flat = HitBoxesTexture == 2 || rect.Width < 3 || rect.Height < 3;
		if (!flat)
		{
			rect.Inflate(-2, -2);
		}
		Rectangle box = Main.ReverseGravitySupport(rect);
		Rectangle screen = Main.ReverseGravitySupport(new Rectangle((int)Main.screenPosition.X, (int)Main.screenPosition.Y, Main.screenWidth + 1, Main.screenHeight + 1));
		Rectangle visible = Rectangle.Intersect(screen, box);
		if (visible.Width == 0 || visible.Height == 0)
		{
			return;
		}
		visible.Offset(-screen.X, -screen.Y);
		if (Main.LocalPlayer.gravDir == -1f)
		{
			visible.Y = Main.screenHeight - visible.Y;
			visible.Y += 1 - visible.Height;
		}
		Texture2D pixel = TextureAssets.MagicPixel.Value;
		if (HitBoxesTexture == 1 || flat)
		{
			spriteBatch.Draw(pixel, visible, color * (flat ? 0.4f : 0.15f));
		}
		if (!flat)
		{
			spriteBatch.Draw(pixel, new Rectangle(visible.X, visible.Y - 2, visible.Width, 2), color);
			spriteBatch.Draw(pixel, new Rectangle(visible.X, visible.Y + visible.Height, visible.Width, 2), color);
			spriteBatch.Draw(pixel, new Rectangle(visible.X - 2, visible.Y, 2, visible.Height), color);
			spriteBatch.Draw(pixel, new Rectangle(visible.X + visible.Width, visible.Y, 2, visible.Height), color);
		}
	}

	#region Chat commands

	/// <summary>.extraproj</summary>
	internal static void ExtraProjectileHitboxesCommand(CommandContext command)
	{
		UseExtraProjHitboxes = !UseExtraProjHitboxes;
		ClientChat.Info("Rendering extra projectile hitboxes " + (UseExtraProjHitboxes ? "en" : "dis") + "abled" + ((UseExtraProjHitboxes && !ProjectileHitboxes) ? " (turn on Projectile Hitboxes in Render)" : ""));
	}

	#endregion
}
