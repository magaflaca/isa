using System;
using System.Collections.Generic;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameInput;

namespace IsaClient.Items;

/// <summary>
/// One use of an item, played on a little stage exactly as the game plays it. The game's own shooting code runs
/// (Star Wrath's falling stars, Zenith's swords, a shotgun's spread...), the projectiles it makes are taken out of the
/// game before anyone sees them, and then each tick every one runs its own AI (Projectile.Update) and is drawn by the
/// game's own projectile drawing. While that happens nothing reaches the world: no damage, no cut grass, no other
/// projectile killed, no dust, gore, light, sound, packets or ammo used; the player is put back as it was after every
/// step, standing where it stood and aiming at the same point.
/// </summary>
public static class ShotPreview
{
	/// <summary>The shooting code is running for the preview.</summary>
	public static bool Capturing;

	/// <summary>A preview projectile is running its AI.</summary>
	public static bool Simulating;

	/// <summary>Preview projectiles are being drawn (lighting is full).</summary>
	public static bool Drawing;

	/// <summary>Capturing or simulating: the game's effects on the world stand down.</summary>
	public static bool Busy => Capturing || Simulating;

	/// <summary>The preview projectile running its AI now.</summary>
	public static Projectile Current;

	/// <summary>Where the preview aims, from the player's center (world pixels). Right click on the stage moves it.</summary>
	public static Vector2 Aim = new Vector2(360f, -40f);

	/// <summary>
	/// The preview's own dust (the game's dust array is swapped for this one while the preview runs or draws): many
	/// projectiles are only their dust (powders, bolts, beams), and none of it ever shows up in the world.
	/// </summary>
	private static Dust[] _dust;

	private static Dust[] PreviewDust
	{
		get
		{
			if (_dust == null)
			{
				_dust = new Dust[Main.dust.Length];
				for (int i = 0; i < _dust.Length; i++)
				{
					_dust[i] = new Dust { dustIndex = i };
				}
			}
			return _dust;
		}
	}

	/// <summary>Runs <paramref name="action"/> with the preview's dust in place of the game's.</summary>
	private static void WithDust(Action action)
	{
		Dust[] game = Main.dust;
		float dCount = Dust.dCount;
		int lava = Dust.lavaBubbles;
		int sand = Dust.SandStormCount;
		int snow = Main.snowDust;
		Main.dust = PreviewDust;
		try
		{
			action();
		}
		finally
		{
			Main.dust = game;
			Dust.dCount = dCount;
			Dust.lavaBubbles = lava;
			Dust.SandStormCount = sand;
			Main.snowDust = snow;
		}
	}

	private static readonly List<int> _made = new List<int>();

	private static readonly List<Projectile> _flying = new List<Projectile>();

	private static Vector2 _center;

	private static Vector2 _position;

	private static int _ticks;

	private static uint _lastStep;

	public static int Count => _flying.Count;

	/// <summary>Live dust of the preview (projectiles that are only dust count as something to show).</summary>
	public static bool HasDust
	{
		get
		{
			if (_dust == null)
			{
				return false;
			}
			foreach (Dust d in _dust)
			{
				if (d.active)
				{
					return true;
				}
			}
			return false;
		}
	}

	public static int Alive
	{
		get
		{
			int n = 0;
			foreach (Projectile p in _flying)
			{
				if (p.active)
				{
					n++;
				}
			}
			return n;
		}
	}

	/// <summary>Ticks since the use.</summary>
	public static int Ticks => _ticks;

	/// <summary>Called by Projectile.NewProjectile while busy: the projectile it made is taken for the preview.</summary>
	public static void Made(int index)
	{
		if (index >= 0 && index < Main.maxProjectiles)
		{
			_made.Add(index);
		}
	}

	/// <summary>The projectiles made since the last call, out of the game (their slots get an empty projectile).</summary>
	private static void TakeMade()
	{
		foreach (int index in _made)
		{
			Projectile p = Main.projectile[index];
			Main.projectile[index] = new Projectile();
			if (p != null && p.active && _flying.Count < 300 && !_flying.Contains(p))
			{
				_flying.Add(p);
			}
		}
		_made.Clear();
	}

	private struct PlayerState
	{
		public Vector2 Position, Velocity;

		public int Direction, ItemAnimation, ItemAnimationMax, ItemTime, ItemTimeMax, StatMana, HeldProj, MouseX, MouseY, InputX, InputY;

		public float ItemRotation;

		public bool Channel;
	}

	private static PlayerState Save(Player p)
	{
		return new PlayerState
		{
			Position = p.position,
			Velocity = p.velocity,
			Direction = p.direction,
			ItemAnimation = p.itemAnimation,
			ItemAnimationMax = p.itemAnimationMax,
			ItemTime = p.itemTime,
			ItemTimeMax = p.itemTimeMax,
			StatMana = p.statMana,
			HeldProj = p.heldProj,
			ItemRotation = p.itemRotation,
			Channel = p.channel,
			MouseX = Main.mouseX,
			MouseY = Main.mouseY,
			InputX = PlayerInput.MouseX,
			InputY = PlayerInput.MouseY
		};
	}

	private static void Restore(Player p, PlayerState s)
	{
		p.position = s.Position;
		p.velocity = s.Velocity;
		p.direction = s.Direction;
		p.itemAnimation = s.ItemAnimation;
		p.itemAnimationMax = s.ItemAnimationMax;
		p.itemTime = s.ItemTime;
		p.itemTimeMax = s.ItemTimeMax;
		p.statMana = s.StatMana;
		p.heldProj = s.HeldProj;
		p.itemRotation = s.ItemRotation;
		p.channel = s.Channel;
		Main.mouseX = s.MouseX;
		Main.mouseY = s.MouseY;
		PlayerInput.MouseX = s.InputX;
		PlayerInput.MouseY = s.InputY;
	}

	/// <summary>The player as the preview has it: where it stood at the use, facing right, aiming at the mark.</summary>
	private static void Pose(Player p)
	{
		p.position = _position;
		p.velocity = Vector2.Zero;
		p.direction = 1;
		Vector2 aim = _center + Aim - Main.screenPosition;
		Main.mouseX = PlayerInput.MouseX = (int)aim.X;
		Main.mouseY = PlayerInput.MouseY = (int)aim.Y;
	}

	/// <summary>Starts a new use of <paramref name="item"/> (the old one is dropped). False when the player can't (menu, dead).</summary>
	public static bool Start(Item item)
	{
		Clear();
		Player p = Main.LocalPlayer;
		if (item == null || item.IsAir || p == null || !p.active || p.dead || Main.gameMenu || Busy)
		{
			return false;
		}
		PlayerState saved = Save(p);
		_position = p.position;
		_center = p.Center;
		_made.Clear();
		Capturing = true;
		try
		{
			Pose(p);
			Item copy = item.Clone();
			// "Shots per use": the game shoots that many times in one use.
			int times = Math.Max(1, Math.Min(200, (int)copy.timesToShoot));
			WithDust(() =>
			{
				for (int shot = 0; shot < times; shot++)
				{
					p.IsaPreviewShoot(copy);
				}
			});
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		finally
		{
			Capturing = false;
			TakeMade();
			Restore(p, saved);
		}
		_ticks = 0;
		_lastStep = Main.GameUpdateCount;
		return true;
	}

	public static void Clear()
	{
		if (_dust != null)
		{
			foreach (Dust d in _dust)
			{
				d.active = false;
			}
		}
		_flying.Clear();
		_made.Clear();
		_ticks = 0;
	}

	/// <summary>One game tick of every preview projectile (at most once per game tick).</summary>
	public static void Step()
	{
		if (_lastStep == Main.GameUpdateCount || _flying.Count == 0 || Main.gameMenu || Busy)
		{
			return;
		}
		_lastStep = Main.GameUpdateCount;
		Player p = Main.LocalPlayer;
		if (p == null || !p.active)
		{
			return;
		}
		PlayerState saved = Save(p);
		Projectile spare = Main.projectile[Main.maxProjectiles];
		Simulating = true;
		try
		{
			Pose(p);
			WithDust(() =>
			{
				foreach (Projectile proj in _flying.ToArray())
				{
					if (!proj.active)
					{
						continue;
					}
					Current = proj;
					Main.projectile[Main.maxProjectiles] = proj;
					proj.whoAmI = Main.maxProjectiles;
					try
					{
						proj.Update(Main.maxProjectiles);
					}
					catch
					{
						proj.active = false;
					}
					// What it spawned flies too (splits, explosions...).
					TakeMade();
				}
				Current = null;
				Dust.UpdateDust();
			});
		}
		finally
		{
			Simulating = false;
			Current = null;
			Main.projectile[Main.maxProjectiles] = spare;
			Restore(p, saved);
		}
		_ticks++;
	}

	/// <summary>The preview's dust, in full light (the game's dust drawing, without its screen culling).</summary>
	private static void DrawDust(SpriteBatch batch)
	{
		Texture2D texture = Terraria.GameContent.TextureAssets.Dust.Value;
		Vector2 origin = new Vector2(4f, 4f);
		foreach (Dust dust in PreviewDust)
		{
			if (!dust.active)
			{
				continue;
			}
			Color color = dust.GetAlpha(Color.White);
			float scale = dust.GetVisualScale();
			batch.Draw(texture, dust.position - Main.screenPosition, dust.frame, color, dust.GetVisualRotation(), origin, scale, SpriteEffects.None, 0f);
			if (dust.color.PackedValue != 0)
			{
				Color tint = dust.GetColor(color);
				if (tint.PackedValue != 0)
				{
					batch.Draw(texture, dust.position - Main.screenPosition, dust.frame, tint, dust.GetVisualRotation(), origin, scale, SpriteEffects.None, 0f);
				}
			}
		}
	}

	/// <summary>
	/// Draws the use on a stage: world pixels around where the player stood, <paramref name="scale"/> screen pixels each,
	/// with that spot at <paramref name="player"/> (panel coordinates). The panel's batch is ended and begun again.
	/// </summary>
	public static void Draw(SpriteBatch sb, Rectangle stage, Vector2 player, float scale, Matrix panel, bool scissor)
	{
		if (_flying.Count == 0)
		{
			return;
		}
		Matrix world = Matrix.CreateTranslation(new Vector3(Main.screenPosition - _center, 0f)) * Matrix.CreateScale(scale) * Matrix.CreateTranslation(new Vector3(player, 0f)) * panel;
		GraphicsDevice device = sb.GraphicsDevice;
		Rectangle oldScissor = device.ScissorRectangle;
		Rectangle clip = Rectangle.Intersect(IsaClient.Hub.HubInput.ToScreen(stage), device.Viewport.Bounds);
		if (clip.Width <= 0 || clip.Height <= 0)
		{
			return;
		}
		if (scissor)
		{
			clip = Rectangle.Intersect(clip, oldScissor);
		}
		sb.End();
		device.ScissorRectangle = clip;
		SpriteBatch game = Main.spriteBatch;
		Projectile spare = Main.projectile[Main.maxProjectiles];
		Drawing = true;
		Main.IsaTransform = world;
		Dust[] gameDust = Main.dust;
		Main.dust = PreviewDust;
		try
		{
			game.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, new RasterizerState
			{
				CullMode = CullMode.None,
				ScissorTestEnable = true
			}, null, world);
			foreach (Projectile proj in _flying)
			{
				if (!proj.active)
				{
					continue;
				}
				Main.projectile[Main.maxProjectiles] = proj;
				int who = proj.whoAmI;
				proj.whoAmI = Main.maxProjectiles;
				try
				{
					Main.instance.LoadProjectile(proj.type);
					Main.instance.DrawProjDirect(proj);
				}
				catch
				{
				}
				proj.whoAmI = who;
			}
			DrawDust(game);
			try
			{
				game.End();
			}
			catch (InvalidOperationException)
			{
				// A projectile's drawing ended the batch itself.
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			try
			{
				game.End();
			}
			catch
			{
			}
		}
		finally
		{
			Drawing = false;
			Main.IsaTransform = null;
			Main.dust = gameDust;
			Main.projectile[Main.maxProjectiles] = spare;
			device.ScissorRectangle = oldScissor;
			// The panel's batch again, however the game's drawing left things (some projectiles end and begin it).
			RasterizerState raster = scissor ? new RasterizerState
			{
				CullMode = CullMode.None,
				ScissorTestEnable = true
			} : RasterizerState.CullCounterClockwise;
			try
			{
				sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.SamplerStateForCursor, DepthStencilState.None, raster, null, panel);
			}
			catch (InvalidOperationException)
			{
				sb.End();
				sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.SamplerStateForCursor, DepthStencilState.None, raster, null, panel);
			}
		}
	}
}
