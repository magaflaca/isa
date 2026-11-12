using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using IsaClient.Core;
using IsaClient.Games;
using IsaClient.Gba.Trainer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace IsaClient.Gba;

/// <summary>
/// Pictures for the trainer picker: the FireRed sprites a character becomes (walking, trainer card, battle back), made
/// in the background and kept as textures, and the Terraria character itself without armor.
/// </summary>
public static class TrainerArt
{
	private static readonly Dictionary<string, TrainerResult> _previews = new Dictionary<string, TrainerResult>();

	private static readonly List<string> _order = new List<string>();

	private static Task _working;

	private static string _workingKey;

	public static string PreviewError;

	private static readonly ConditionalWeakTable<TrainerResult, Texture2D> _walkTex = new ConditionalWeakTable<TrainerResult, Texture2D>();

	private static readonly Dictionary<string, Texture2D> _pictures = new Dictionary<string, Texture2D>();

	private static readonly ConditionalWeakTable<Player, Player> _bare = new ConditionalWeakTable<Player, Player>();

	/// <summary>The sprites of <paramref name="look"/> with a body choice (0 auto, 1 Red, 2 Leaf); null while they're made.</summary>
	public static TrainerResult Preview(TrainerLook look, int body)
	{
		string key = PokemonGame.RomCode + "|" + look.Key + "|" + body;
		if (_previews.TryGetValue(key, out TrainerResult r))
		{
			return r;
		}
		if (_working != null && !_working.IsCompleted)
		{
			return null;
		}
		if (_workingKey == key && PreviewError != null)
		{
			return null;
		}
		if (!PokemonGame.LoadRom())
		{
			PreviewError = "Choose the ROM first.";
			return null;
		}
		PreviewError = null;
		_workingKey = key;
		byte[] rom = PokemonGame.OriginalRom;
		bool? red = body switch
		{
			1 => true,
			2 => false,
			_ => null
		};
		_working = Task.Run(() =>
		{
			try
			{
				TrainerResult made = TrainerBuilder.Build(rom, look, red);
				made.Rom = null;
				Main.QueueMainThreadAction(() =>
				{
					_previews[key] = made;
					_order.Add(key);
					if (_order.Count > 12)
					{
						_previews.Remove(_order[0]);
						_order.RemoveAt(0);
					}
				});
			}
			catch (Exception ex)
			{
				PreviewError = ex.Message;
				ClientLog.LogException(ex);
			}
		});
		return null;
	}

	private static Texture2D Texture(int w, int h, Color[] px)
	{
		Texture2D t = new Texture2D(Main.instance.GraphicsDevice, w, h);
		t.SetData(px);
		return t;
	}

	private static Color ToColor(Rgb c) => new Color(c.R, c.G, c.B);

	/// <summary>The 20 standing/walking/running frames in a row (16x32 each).</summary>
	private static Texture2D WalkTexture(TrainerResult t)
	{
		if (_walkTex.TryGetValue(t, out Texture2D tex) && !tex.IsDisposed)
		{
			return tex;
		}
		int n = Math.Min(20, t.Frames.Count);
		Color[] px = new Color[16 * n * 32];
		for (int f = 0; f < n; f++)
		{
			byte[,] idx = t.Frames[f];
			for (int y = 0; y < 32 && y < idx.GetLength(0); y++)
			{
				for (int x = 0; x < 16 && x < idx.GetLength(1); x++)
				{
					byte i = idx[y, x];
					if (i != 0)
					{
						px[y * 16 * n + f * 16 + x] = ToColor(t.Palette[i]);
					}
				}
			}
		}
		tex = Texture(16 * n, 32, px);
		_walkTex.Remove(t);
		_walkTex.Add(t, tex);
		return tex;
	}

	// FRLG frame order: 0 down, 1 up, 2 left; walk pairs 3-4 down, 5-6 up, 7-8 left (right = left mirrored).
	private static readonly int[][] Walks =
	{
		new[] { 3, 0, 4, 0 },
		new[] { 7, 2, 8, 2 },
		new[] { 5, 1, 6, 1 },
		new[] { 7, 2, 8, 2 }
	};

	/// <summary>The trainer walking on the spot facing <paramref name="dir"/> (0 down, 1 left, 2 up, 3 right), fitted in a box.</summary>
	public static void Walk(SpriteBatch sb, TrainerResult t, Rectangle box, int dir, Action<SpriteBatch> sharp = null, Action<SpriteBatch> restore = null)
	{
		if (t?.Frames == null || t.Frames.Count < 9)
		{
			return;
		}
		Texture2D tex = WalkTexture(t);
		int frame = Walks[dir][(int)(GameArt.Now * 6.0) % 4];
		// the frame is 16x32 with ~6 empty rows on top
		Rectangle src = new Rectangle(frame * 16, 4, 16, 28);
		float scale = Math.Max(1f, (float)Math.Floor(Math.Min(box.Width / 16f, box.Height / 28f)));
		int w = (int)(16 * scale);
		int h = (int)(28 * scale);
		Rectangle dst = new Rectangle(box.Center.X - w / 2, box.Bottom - h, w, h);
		GameArt.Disc(sb, new Vector2(dst.Center.X, dst.Bottom - scale * 2), w * 0.9f, scale * 5f, Color.Black * 0.3f);
		(sharp ?? GameArt.Sharp)(sb);
		sb.Draw(tex, dst, src, Color.White, 0f, Vector2.Zero, (dir == 3) ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
		(restore ?? GameArt.Smooth)(sb);
	}

	/// <summary>A 64x64 picture (trainer card or battle back), cached by <paramref name="key"/>.</summary>
	public static void Picture(SpriteBatch sb, byte[,] idx, Rgb[] pal, Rectangle box, string key, bool framed = true)
	{
		if (idx == null)
		{
			return;
		}
		if (!_pictures.TryGetValue(key, out Texture2D tex) || tex.IsDisposed)
		{
			int h = idx.GetLength(0);
			int w = idx.GetLength(1);
			Color[] px = new Color[w * h];
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					if (idx[y, x] != 0)
					{
						px[y * w + x] = ToColor(pal[idx[y, x]]);
					}
				}
			}
			tex = Texture(w, h, px);
			if (_pictures.Count > 40)
			{
				foreach (Texture2D old in _pictures.Values)
				{
					old.Dispose();
				}
				_pictures.Clear();
			}
			_pictures[key] = tex;
		}
		if (framed)
		{
			MusicBox(sb, box);
		}
		GameArt.Sharp(sb);
		sb.Draw(tex, box, Color.White);
		GameArt.Smooth(sb);
	}

	private static void MusicBox(SpriteBatch sb, Rectangle r)
	{
		IsaClient.Music.MusicDrawing.Fill(sb, r, new Color(232, 232, 216));
		GameArt.Frame(sb, r, Color.Black * 0.6f, 2);
	}

	/// <summary>The character without armor or accessories (what the sprite is made from).</summary>
	private static Player Bare(Player p)
	{
		if (_bare.TryGetValue(p, out Player bare))
		{
			return bare;
		}
		try
		{
			bare = p.SerializedClone();
			for (int i = 0; i < bare.armor.Length; i++)
			{
				bare.armor[i] = new Item();
			}
			for (int i = 0; i < bare.dye.Length; i++)
			{
				bare.dye[i] = new Item();
			}
			for (int i = 0; i < bare.miscEquips.Length; i++)
			{
				bare.miscEquips[i] = new Item();
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			bare = p;
		}
		_bare.Add(p, bare);
		return bare;
	}

	/// <summary>The Terraria character standing, without armor, fitted in a box.</summary>
	public static void Portrait(SpriteBatch sb, Player source, Rectangle box)
	{
		Player player = Bare(source);
		float scale = Math.Min(box.Width / 40f, box.Height / 56f);
		try
		{
			player.isaAlwaysLit = true;
			player.socialIgnoreLight = true;
			player.direction = 1;
			player.gravDir = 1f;
			player.ResetEffects();
			player.ResetVisibleAccessories();
			player.UpdateMiscCounter();
			player.UpdateDyes();
			player.PlayerFrame();
			player.bodyFrame.Y = player.legFrame.Y = player.headFrame.Y = 0;
			int held = player.selectedItem;
			Item heldItem = player.inventory[held];
			player.inventory[held] = new Item();
			sb.End();
			sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
			Vector2 topLeft = new Vector2(box.Center.X - player.width / 2f, box.Center.Y + player.height * scale / 2f - player.height);
			Main.PlayerRenderer.DrawPlayer(Main.Camera, player, topLeft + Main.screenPosition, 0f, Vector2.Zero, 0f, scale);
			player.inventory[held] = heldItem;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		finally
		{
			GameArt.Smooth(sb);
		}
	}

	/// <summary>The character's head (no helmet), centered on a point, for lists.</summary>
	public static void Head(SpriteBatch sb, Player source, Vector2 center, Action<SpriteBatch> restore = null)
	{
		Player player = Bare(source);
		try
		{
			Vector2 corner = new Vector2(center.X - 4f + 4f, center.Y - 14f + 9f);
			Main.PlayerRenderer.DrawPlayerHead(Main.Camera, player, corner, 1f, 1f, Color.Transparent);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		(restore ?? GameArt.Smooth)(sb);
	}
}

/// <summary>Little pixel pictures drawn from strings (no textures).</summary>
public static class PixelArt
{
	private static readonly string[] Ball =
	{
		"....kkkkkk....",
		"..kkrrrrrrkk..",
		".krrrrrrwwrrk.",
		".krrrrrrrwwrk.",
		"krrrrrrrrrrrrk",
		"krrrrrkkkrrrrk",
		"kkkkkkwwwkkkkk",
		"kwwwwkwgwkwwwk",
		"kwwwwkkkkkwwwk",
		"kwwwwwwwwwwwwk",
		".kwwwwwwwwwwk.",
		".kgwwwwwwwwgk.",
		"..kkggggggkk..",
		"....kkkkkk...."
	};

	private static readonly string[] ArrowArt =
	{
		"...k....",
		"...kk...",
		"kkkkyk..",
		"kyyyyyk.",
		"kyyyyyyk",
		"kyyyyyk.",
		"kkkkyk..",
		"...kk...",
		"...k...."
	};

	private static void Draw(SpriteBatch sb, string[] art, Rectangle area)
	{
		int n = art.Length;
		int m = art[0].Length;
		float px = Math.Min(area.Width / (float)m, area.Height / (float)n);
		float ox = area.X + (area.Width - px * m) / 2f;
		float oy = area.Y + (area.Height - px * n) / 2f;
		for (int y = 0; y < n; y++)
		{
			for (int x = 0; x < m; x++)
			{
				Color c = art[y][x] switch
				{
					'k' => new Color(24, 24, 32),
					'r' => new Color(230, 64, 64),
					'w' => new Color(245, 245, 245),
					'g' => new Color(180, 184, 196),
					'y' => new Color(255, 222, 80),
					_ => Color.Transparent
				};
				if (c.A == 0)
				{
					continue;
				}
				int x0 = (int)Math.Round(ox + x * px);
				int y0 = (int)Math.Round(oy + y * px);
				int x1 = (int)Math.Round(ox + (x + 1) * px);
				int y1 = (int)Math.Round(oy + (y + 1) * px);
				IsaClient.Music.MusicDrawing.Fill(sb, new Rectangle(x0, y0, x1 - x0, y1 - y0), c);
			}
		}
	}

	public static void PokeBall(SpriteBatch sb, Rectangle area) => Draw(sb, Ball, area);

	public static void Arrow(SpriteBatch sb, Vector2 center)
	{
		float bob = (float)Math.Sin(GameArt.Now * 5.0) * 2f;
		Draw(sb, ArrowArt, new Rectangle((int)(center.X - 10 + bob), (int)center.Y - 11, 20, 22));
	}
}
