using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.ID;
using Terraria.Map;
using Terraria;

namespace IsaClient.Building;

/// <summary>
/// Turns a character into pixel art made of real tiles (like TerraWorld's "Real Art"): the character is drawn into an
/// off-screen texture exactly as the game draws it (look, armor, vanity, dyes, wings), brought back to the sprite's real
/// resolution (Terraria art is drawn at 2x, so every art pixel is 2x2 screen pixels), and every art pixel becomes one tile.
/// The outline is made of blocks only (left actuated or solid, as chosen); inside the character any block or wall with
/// any paint may be used, whichever looks closest. The result goes to the paste clipboard; the paste already knows how to
/// leave blocks actuated (place actuator, toggle, remove it) and how to paint and coat them.
/// </summary>
public static class CharacterArt
{
	private const int CanvasWidth = 96;

	private const int CanvasHeight = 96;

	/// <summary>The game draws actuated blocks at 40% brightness (Tile.actColor).</summary>
	private const float ActuatedBrightness = 0.4f;

	private struct PaletteEntry
	{
		public bool IsWall;

		public ushort Type;

		public byte Paint;

		public Vector3 Color;
	}

	private static List<PaletteEntry> _blocks;

	private static List<PaletteEntry> _walls;

	private static TileCopy _artMarker;

	/// <summary>True while the paste clipboard still holds character art: its empty pixels never erase what is behind them.</summary>
	public static bool ClipboardIsArt
	{
		get
		{
			if (_artMarker != null && Selection.CopiedTiles.Count > 0)
			{
				return Selection.CopiedTiles[0] == _artMarker;
			}
			return false;
		}
	}

	/// <summary>Draws the character (standing, facing right, no held item, full brightness) and returns its art pixels, cropped.</summary>
	public static Color[] Capture(Player player, out int width, out int height)
	{
		GraphicsDevice device = Main.instance.GraphicsDevice;
		Player p = player;
		p.isaAlwaysLit = true;
		p.socialIgnoreLight = true;
		p.direction = 1;
		p.gravDir = 1f;
		p.dead = false;
		p.ghost = false;
		p.velocity = Vector2.Zero;
		p.ResetEffects();
		p.ResetVisibleAccessories();
		p.UpdateMiscCounter();
		p.UpdateDyes();
		p.PlayerFrame();
		p.bodyFrame.Y = (p.legFrame.Y = (p.headFrame.Y = 0));
		p.isaAlwaysLit = true;
		int held = p.selectedItem;
		Item heldItem = p.inventory[held];
		p.inventory[held] = new Item();
		Color[] data = new Color[CanvasWidth * CanvasHeight];
		RenderTargetBinding[] previous = device.GetRenderTargets();
		using (RenderTarget2D target = new RenderTarget2D(device, CanvasWidth, CanvasHeight, mipMap: false, SurfaceFormat.Color, DepthFormat.None))
		{
			device.SetRenderTarget(target);
			device.Clear(Color.Transparent);
			Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
			try
			{
				Vector2 at = new Vector2((float)((CanvasWidth - p.width) / 2), (float)((CanvasHeight - p.height) / 2));
				Main.PlayerRenderer.DrawPlayer(Main.Camera, p, at + Main.screenPosition, 0f, Vector2.Zero, 0f, 1f);
			}
			finally
			{
				Main.spriteBatch.End();
				device.SetRenderTargets(previous);
			}
			target.GetData(data);
		}
		p.inventory[held] = heldItem;
		for (int i = 0; i < data.Length; i++)
		{
			Color c = data[i];
			if (c.A < 110)
			{
				data[i] = Color.Transparent;
			}
			else if (c.A < 255)
			{
				// Render targets hold premultiplied colors.
				float a = (float)(int)c.A / 255f;
				data[i] = new Color((int)Math.Min(255f, (float)(int)c.R / a), (int)Math.Min(255f, (float)(int)c.G / a), (int)Math.Min(255f, (float)(int)c.B / a), 255);
			}
		}
		Color[] art = Downsample(data, CanvasWidth, CanvasHeight, out int artWidth, out int artHeight);
		return Crop(art, artWidth, artHeight, out width, out height);
	}

	/// <summary>
	/// Halves the picture back to art pixels. The 2x2 grid can start on an odd screen pixel, so both offsets are tried on
	/// each axis and the one where the 2x2 cells are most uniform wins; each cell takes its most common color.
	/// </summary>
	internal static Color[] Downsample(Color[] data, int w, int h, out int outW, out int outH)
	{
		int bestX = 0;
		int bestY = 0;
		int bestScore = int.MaxValue;
		for (int px = 0; px < 2; px++)
		{
			for (int py = 0; py < 2; py++)
			{
				int score = 0;
				for (int y = -py; y < h; y += 2)
				{
					for (int x = -px; x < w; x += 2)
					{
						Color first = At(data, w, h, x, y);
						if (At(data, w, h, x + 1, y) != first)
						{
							score++;
						}
						if (At(data, w, h, x, y + 1) != first)
						{
							score++;
						}
						if (At(data, w, h, x + 1, y + 1) != first)
						{
							score++;
						}
					}
				}
				if (score < bestScore)
				{
					bestScore = score;
					bestX = px;
					bestY = py;
				}
			}
		}
		outW = (w + bestX + 1) / 2;
		outH = (h + bestY + 1) / 2;
		Color[] result = new Color[outW * outH];
		Color[] cell = new Color[4];
		for (int cy = 0; cy < outH; cy++)
		{
			for (int cx = 0; cx < outW; cx++)
			{
				int x = cx * 2 - bestX;
				int y = cy * 2 - bestY;
				cell[0] = At(data, w, h, x, y);
				cell[1] = At(data, w, h, x + 1, y);
				cell[2] = At(data, w, h, x, y + 1);
				cell[3] = At(data, w, h, x + 1, y + 1);
				int opaque = 0;
				foreach (Color c in cell)
				{
					if (c.A > 0)
					{
						opaque++;
					}
				}
				if (opaque < 2)
				{
					continue;
				}
				Color best = Color.Transparent;
				int bestCount = 0;
				for (int i = 0; i < 4; i++)
				{
					if (cell[i].A == 0)
					{
						continue;
					}
					int count = 0;
					for (int j = 0; j < 4; j++)
					{
						if (cell[j] == cell[i])
						{
							count++;
						}
					}
					if (count > bestCount)
					{
						bestCount = count;
						best = cell[i];
					}
				}
				result[cy * outW + cx] = best;
			}
		}
		return result;
	}

	private static Color At(Color[] data, int w, int h, int x, int y)
	{
		if (x < 0 || y < 0 || x >= w || y >= h)
		{
			return Color.Transparent;
		}
		return data[y * w + x];
	}

	internal static Color[] Crop(Color[] data, int w, int h, out int width, out int height)
	{
		int minX = w;
		int minY = h;
		int maxX = -1;
		int maxY = -1;
		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				if (data[y * w + x].A > 0)
				{
					minX = Math.Min(minX, x);
					maxX = Math.Max(maxX, x);
					minY = Math.Min(minY, y);
					maxY = Math.Max(maxY, y);
				}
			}
		}
		if (maxX < 0)
		{
			width = 0;
			height = 0;
			return null;
		}
		width = maxX - minX + 1;
		height = maxY - minY + 1;
		Color[] cropped = new Color[width * height];
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				cropped[y * width + x] = data[(minY + y) * w + minX + x];
			}
		}
		return cropped;
	}

	/// <summary>Converts the art pixels to tiles and arms the paste (same clipboard as .loadsch / .drawpic).</summary>
	public static void LoadIntoPaste(Color[] pixels, int width, int height, int scale, bool walkThrough, bool glow)
	{
		scale = Utils.Clamp(scale, 1, 4);
		EnsurePalettes();
		PaletteEntry?[] chosen = new PaletteEntry?[width * height];
		Dictionary<ulong, PaletteEntry> cache = new Dictionary<ulong, PaletteEntry>();
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				Color c = pixels[y * width + x];
				if (c.A == 0)
				{
					continue;
				}
				bool outline = IsOutline(pixels, width, height, x, y);
				ulong key = (ulong)(c.PackedValue & 0xFFFFFFu) | (outline ? 0x100000000uL : 0uL);
				if (!cache.TryGetValue(key, out PaletteEntry entry))
				{
					entry = Closest(c, outline, walkThrough);
					cache[key] = entry;
				}
				chosen[y * width + x] = entry;
			}
		}
		BrushState.DisableBrushes();
		Selection.SelectionMode = false;
		Selection.CopyMode = false;
		PixelArt.DrawPhotoMode = false;
		Selection.CopiedTiles.Clear();
		int outWidth = width * scale;
		int outHeight = height * scale;
		// The clipboard is column-major (x outer, y inner), like every other paste source.
		for (int x = 0; x < outWidth; x++)
		{
			for (int y = 0; y < outHeight; y++)
			{
				TileCopy tile = new TileCopy();
				PaletteEntry? pick = chosen[y / scale * width + x / scale];
				if (pick.HasValue)
				{
					PaletteEntry e = pick.Value;
					if (e.IsWall)
					{
						tile.wall = e.Type;
						tile.wallColor(e.Paint);
						if (glow)
						{
							tile.fullbrightWall(fullbrightWall: true);
						}
					}
					else
					{
						tile.active(active: true);
						tile.type = e.Type;
						tile.color(e.Paint);
						if (walkThrough)
						{
							tile.inActive(inActive: true);
						}
						if (glow)
						{
							tile.fullbrightBlock(fullbrightBlock: true);
						}
					}
				}
				Selection.CopiedTiles.Add(tile);
			}
		}
		_artMarker = Selection.CopiedTiles[0];
		Selection.SchematicSizeX = outWidth;
		Selection.SchematicSizeY = outHeight;
		Selection.PasteMode = true;
		Selection.PasteDelayCounter = Selection.PasteDelay;
	}

	/// <summary>An art pixel touching transparency (or the picture's edge) on any side.</summary>
	private static bool IsOutline(Color[] pixels, int w, int h, int x, int y)
	{
		if (At(pixels, w, h, x - 1, y).A != 0 && At(pixels, w, h, x + 1, y).A != 0 && At(pixels, w, h, x, y - 1).A != 0)
		{
			return At(pixels, w, h, x, y + 1).A == 0;
		}
		return true;
	}

	private static PaletteEntry Closest(Color c, bool outline, bool walkThrough)
	{
		Vector3 target = new Vector3(c.R, c.G, c.B);
		float best = float.MaxValue;
		PaletteEntry bestEntry = _blocks[0];
		float blockFactor = walkThrough ? ActuatedBrightness : 1f;
		foreach (PaletteEntry entry in _blocks)
		{
			float d = Distance(target, entry.Color * blockFactor);
			if (d < best)
			{
				best = d;
				bestEntry = entry;
			}
		}
		if (!outline)
		{
			foreach (PaletteEntry entry2 in _walls)
			{
				float d2 = Distance(target, entry2.Color);
				if (d2 < best)
				{
					best = d2;
					bestEntry = entry2;
				}
			}
		}
		return bestEntry;
	}

	/// <summary>"Redmean" color distance (squared): cheap and much closer to how eyes compare colors than plain RGB.</summary>
	private static float Distance(Vector3 a, Vector3 b)
	{
		float rMean = (a.X + b.X) * 0.5f;
		float dr = a.X - b.X;
		float dg = a.Y - b.Y;
		float db = a.Z - b.Z;
		return (2f + rMean / 256f) * dr * dr + 4f * dg * dg + (2f + (255f - rMean) / 256f) * db * db;
	}

	/// <summary>
	/// Every usable block and every flat wall, each with every paint (none, the 24 colors, black, white, gray, brown, shadow
	/// and negative), described by the color the game shows for it (its map color, which the paint already tints).
	/// </summary>
	private static void EnsurePalettes()
	{
		if (_blocks != null)
		{
			return;
		}
		List<PaletteEntry> blocks = new List<PaletteEntry>();
		List<PaletteEntry> walls = new List<PaletteEntry>();
		for (int type = 0; type < TileID.Count; type++)
		{
			if (!Usable(type))
			{
				continue;
			}
			for (byte paint = 0; paint <= 30; paint++)
			{
				Color color = MapHelper.GetMapTileXnaColor(MapTile.Create(MapHelper.tileLookup[type], byte.MaxValue, paint), 0, 0);
				blocks.Add(new PaletteEntry
				{
					Type = (ushort)type,
					Paint = paint,
					Color = new Vector3(color.R, color.G, color.B)
				});
			}
		}
		for (int wall = 1; wall < WallID.Count; wall++)
		{
			if (MapHelper.wallLookup[wall] == 0 || !WallUsable(wall))
			{
				continue;
			}
			for (byte paint = 0; paint <= 30; paint++)
			{
				Color color2 = ColorMatching.MapTileWallColor((ushort)wall, paint);
				walls.Add(new PaletteEntry
				{
					IsWall = true,
					Type = (ushort)wall,
					Paint = paint,
					Color = new Vector3(color2.R, color2.G, color2.B)
				});
			}
		}
		if (blocks.Count == 0)
		{
			blocks.Add(new PaletteEntry
			{
				Type = 1,
				Color = new Vector3(128f, 128f, 128f)
			});
		}
		_blocks = blocks;
		_walls = walls;
	}

	internal static bool Usable(int type)
	{
		if (!Main.tileSolid[type] || Main.tileSolidTop[type] || Main.tileFrameImportant[type] || Main.tileNoAttach[type])
		{
			return false;
		}
		if (TileID.Sets.Falling[type] || Main.tileSand[type] || TileID.Sets.Conversion.Grass[type] || TileID.Sets.GrassSpecial[type] || TileID.Sets.Conversion.Moss[type] || Main.tileMoss[type] || TileID.Sets.CrackedBricks[type] || TileID.Sets.Platforms[type] || TileID.Sets.Leaves[type])
		{
			return false;
		}
		if (Main.tileCut[type] || Main.tileAlch[type] || TileID.Sets.BlocksStairs[type] || ColorMatching.IsNonSolidPixelTile(type))
		{
			return false;
		}
		switch (type)
		{
		case 48: // spikes
		case 54: // glass (see-through)
		case 137: // traps
		case 141: // explosives
		case 162: // thin ice
		case 225: // hive (honey drips)
		case 230:
		case 232: // wooden spikes
		case 484: // rolling cactus
		case 541: // echo block
		case 590:
			return false;
		}
		return true;
	}

	/// <summary>Walls that look like a flat surface (no see-through glass, fences or living walls).</summary>
	internal static bool WallUsable(int wall)
	{
		if (WallID.Sets.Transparent[wall])
		{
			return false;
		}
		switch (wall)
		{
		case 21: // glass
		case 106: // wooden fence
		case 107: // metal fence
		case 168:
		case 244:
			return false;
		}
		return true;
	}
}
