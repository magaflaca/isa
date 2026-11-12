using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using IsaClient.Core;
using IsaClient.Music;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace IsaClient.Games;

/// <summary>
/// The games' pixel art (IsaClient.Assets/Games.png, made by tools/games/make_sprites.py: chess pieces, wood tiles, X and
/// O) and the drawing helpers the boards share: crisp pixel drawing, soft discs and rings, easing and a real clock.
/// </summary>
internal static class GameArt
{
	public const int Cell = 16;

	private static Texture2D _atlas;

	private static bool _failed;

	private static readonly Stopwatch _clock = Stopwatch.StartNew();

	/// <summary>Seconds since start, for animations (keeps running while the game is paused).</summary>
	public static double Now => _clock.Elapsed.TotalSeconds;

	public static Texture2D Atlas
	{
		get
		{
			if ((_atlas == null || _atlas.IsDisposed) && !_failed)
			{
				try
				{
					using Stream stream = typeof(GameArt).Assembly.GetManifestResourceStream("IsaClient.Assets.Games.png");
					using Bitmap bitmap = new Bitmap(stream);
					BitmapData bits = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
					byte[] bgra = new byte[bitmap.Width * bitmap.Height * 4];
					System.Runtime.InteropServices.Marshal.Copy(bits.Scan0, bgra, 0, bgra.Length);
					bitmap.UnlockBits(bits);
					Color[] data = new Color[bitmap.Width * bitmap.Height];
					for (int i = 0; i < data.Length; i++)
					{
						int a = bgra[i * 4 + 3];
						data[i] = new Color(bgra[i * 4 + 2] * a / 255, bgra[i * 4 + 1] * a / 255, bgra[i * 4] * a / 255, a);
					}
					_atlas = new Texture2D(Main.instance.GraphicsDevice, bitmap.Width, bitmap.Height);
					_atlas.SetData(data);
				}
				catch (Exception ex)
				{
					_failed = true;
					ClientLog.LogException(ex);
				}
			}
			return _atlas;
		}
	}

	/// <summary>A chess piece: kind 1-6 (pawn..king), white or black.</summary>
	public static Rectangle Piece(int kind, bool white)
	{
		// Atlas columns: king, queen, rook, bishop, knight, pawn.
		int column = 6 - kind;
		return new Rectangle(column * Cell, white ? 0 : Cell, Cell, Cell);
	}

	public static readonly Rectangle LightTile = new Rectangle(0, 2 * Cell, Cell, Cell);

	public static readonly Rectangle DarkTile = new Rectangle(Cell, 2 * Cell, Cell, Cell);

	public static readonly Rectangle MarkX = new Rectangle(2 * Cell, 2 * Cell, Cell, Cell);

	public static readonly Rectangle MarkO = new Rectangle(3 * Cell, 2 * Cell, Cell, Cell);

	public static readonly Rectangle Felt = new Rectangle(4 * Cell, 2 * Cell, Cell, Cell);

	#region Batches

	/// <summary>
	/// The rasterizer Sharp and Smooth restart the batch with: a clipped list sets its scissor one while it draws (and
	/// puts this back after), so pictures drawn sharp inside it stay clipped.
	/// </summary>
	public static RasterizerState Raster = RasterizerState.CullCounterClockwise;

	/// <summary>Pixel art from here on drawn sharp (nearest pixel) instead of blurred.</summary>
	public static void Sharp(SpriteBatch sb)
	{
		sb.End();
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, Raster, null, Main.UIScaleMatrix);
	}

	/// <summary>Back to the floating windows' usual batch.</summary>
	public static void Smooth(SpriteBatch sb)
	{
		sb.End();
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.gameMenu ? SamplerState.PointClamp : SamplerState.LinearClamp, DepthStencilState.None, Raster, null, Main.UIScaleMatrix);
	}

	#endregion

	#region Shapes

	public static void Sprite(SpriteBatch sb, Rectangle source, Vector2 center, float size, Color color, float rotation = 0f, SpriteEffects effects = SpriteEffects.None)
	{
		Texture2D atlas = Atlas;
		if (atlas == null)
		{
			return;
		}
		sb.Draw(atlas, center, source, color, rotation, new Vector2(source.Width, source.Height) / 2f, size / source.Width, effects, 0f);
	}

	public static void Tile(SpriteBatch sb, Rectangle source, Rectangle area, Color color, SpriteEffects effects = SpriteEffects.None)
	{
		Texture2D atlas = Atlas;
		if (atlas != null)
		{
			sb.Draw(atlas, area, source, color, 0f, Vector2.Zero, effects, 0f);
		}
	}

	/// <summary>A soft round spot (tint it), centered.</summary>
	public static void Disc(SpriteBatch sb, Vector2 center, float width, float height, Color color)
	{
		Texture2D disc = IsaClient.Hub.CountryFlags.Disc;
		sb.Draw(disc, center, null, color, 0f, new Vector2(disc.Width, disc.Height) / 2f, new Vector2(width / disc.Width, height / disc.Height), SpriteEffects.None, 0f);
	}

	/// <summary>A thin ring, centered.</summary>
	public static void Ring(SpriteBatch sb, Vector2 center, float size, Color color)
	{
		Texture2D ring = IsaClient.Hub.CountryFlags.Ring;
		sb.Draw(ring, center, null, color, 0f, new Vector2(ring.Width, ring.Height) / 2f, size / ring.Width, SpriteEffects.None, 0f);
	}

	/// <summary>A straight line <paramref name="thickness"/> wide from a to b.</summary>
	public static void Line(SpriteBatch sb, Vector2 a, Vector2 b, float thickness, Color color)
	{
		Vector2 d = b - a;
		float length = d.Length();
		if (length < 0.5f)
		{
			return;
		}
		sb.Draw(Terraria.GameContent.TextureAssets.MagicPixel.Value, a, new Rectangle(0, 0, 1, 1), color, (float)Math.Atan2(d.Y, d.X), new Vector2(0f, 0.5f), new Vector2(length, thickness), SpriteEffects.None, 0f);
	}

	/// <summary>A vertical gradient, top to bottom colour.</summary>
	public static void Gradient(SpriteBatch sb, Rectangle r, Color top, Color bottom)
	{
		int bands = Math.Max(1, Math.Min(24, r.Height / 2));
		for (int i = 0; i < bands; i++)
		{
			int y0 = r.Y + r.Height * i / bands;
			int y1 = r.Y + r.Height * (i + 1) / bands;
			MusicDrawing.Fill(sb, new Rectangle(r.X, y0, r.Width, y1 - y0), Color.Lerp(top, bottom, (i + 0.5f) / bands));
		}
	}

	/// <summary>A 1-pixel frame.</summary>
	public static void Frame(SpriteBatch sb, Rectangle r, Color color, int thickness = 1)
	{
		MusicDrawing.Fill(sb, new Rectangle(r.X, r.Y, r.Width, thickness), color);
		MusicDrawing.Fill(sb, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), color);
		MusicDrawing.Fill(sb, new Rectangle(r.X, r.Y + thickness, thickness, r.Height - thickness * 2), color);
		MusicDrawing.Fill(sb, new Rectangle(r.Right - thickness, r.Y + thickness, thickness, r.Height - thickness * 2), color);
	}

	#endregion

	#region Easing

	public static float Clamp01(double t)
	{
		return (float)Math.Max(0.0, Math.Min(1.0, t));
	}

	public static float EaseOut(float t)
	{
		t = 1f - t;
		return 1f - t * t * t;
	}

	public static float EaseInOut(float t)
	{
		return (t < 0.5f) ? (4f * t * t * t) : (1f - (float)Math.Pow(-2f * t + 2f, 3) / 2f);
	}

	/// <summary>Overshoots a little and settles (pop-in).</summary>
	public static float EaseBack(float t)
	{
		const float c1 = 1.70158f;
		const float c3 = c1 + 1f;
		return 1f + c3 * (float)Math.Pow(t - 1f, 3) + c1 * (float)Math.Pow(t - 1f, 2);
	}

	/// <summary>0..1..0 every <paramref name="period"/> seconds.</summary>
	public static float Pulse(double period)
	{
		return (float)(0.5 - 0.5 * Math.Cos(Now * Math.PI * 2.0 / period));
	}

	#endregion
}
