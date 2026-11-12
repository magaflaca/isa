using System;
using System.Collections.Generic;

namespace IsaClient.Gba.Trainer;

public enum BodyRole : byte
{
	None,
	Skin,
	Shirt,
	Undershirt,
	Pants,
	Shoes
}

public enum HeadDir
{
	Down,
	Up,
	Left,
	Right
}

/// <summary>One unique overworld frame of Red or Leaf, analysed (t2p/templates.py <c>Frame</c>).</summary>
public sealed class BodyFrame
{
	public string Kind;

	public int Off;

	public byte[,] Idx;

	public HeadDir Dir;

	public double AnchorX;

	public double AnchorY;

	/// <summary>The frame without its head (0 = transparent).</summary>
	public byte[,] Body;

	public BodyRole[,] Roles;

	public byte[,] Tones;

	public int Neck;
}

/// <summary>
/// Red / Leaf body templates: finds the head in every frame by template matching, strips it and gives every remaining
/// pixel a clothing role so it can be recolored with the Terraria colors.
/// </summary>
public static class BodyTemplates
{
	private const int OutlineIdx = 15;

	private struct Template
	{
		public int[] Ys;

		public int[] Xs;

		public byte[] V;
	}

	public static List<BodyFrame> Load(FireRedRom rom, bool red)
	{
		List<BodyFrame> list = new List<BodyFrame>();
		foreach (var look in rom.Game.Looks)
		{
			foreach (var (off, w, h) in rom.Frames(red ? look.Male : look.Female, look.Count))
			{
				list.Add(new BodyFrame { Kind = look.Kind, Off = off, Idx = rom.ReadFrame(off, w, h) });
			}
		}
		return list;
	}

	private static (HeadDir, Template)[] HeadTemplates(List<BodyFrame> frames, int neckY)
	{
		List<BodyFrame> normal = frames.FindAll(f => f.Kind == "normal");
		Template Of(byte[,] a)
		{
			List<int> ys = new List<int>();
			List<int> xs = new List<int>();
			List<byte> v = new List<byte>();
			int w = a.GetLength(1);
			for (int y = 0; y < neckY; y++)
			{
				for (int x = 0; x < w; x++)
				{
					if (a[y, x] > 0)
					{
						ys.Add(y);
						xs.Add(x);
						v.Add(a[y, x]);
					}
				}
			}
			return new Template { Ys = ys.ToArray(), Xs = xs.ToArray(), V = v.ToArray() };
		}
		Template left = Of(normal[2].Idx);
		int[] mirrored = Array.ConvertAll(left.Xs, x => 15 - x);
		return new[]
		{
			(HeadDir.Down, Of(normal[0].Idx)),
			(HeadDir.Up, Of(normal[1].Idx)),
			(HeadDir.Left, left),
			(HeadDir.Right, new Template { Ys = left.Ys, Xs = mirrored, V = left.V })
		};
	}

	private static (double Score, int D, int Dx, int Dy) LocateHead(byte[,] idx, (HeadDir, Template)[] temps)
	{
		int h = idx.GetLength(0);
		int w = idx.GetLength(1);
		double best = -1e9;
		int bd = 0;
		int bdx = 0;
		int bdy = 0;
		for (int d = 0; d < temps.Length; d++)
		{
			Template t = temps[d].Item2;
			int minY = int.MaxValue;
			int maxY = int.MinValue;
			int minX = int.MaxValue;
			int maxX = int.MinValue;
			for (int k = 0; k < t.Ys.Length; k++)
			{
				minY = Math.Min(minY, t.Ys[k]);
				maxY = Math.Max(maxY, t.Ys[k]);
				minX = Math.Min(minX, t.Xs[k]);
				maxX = Math.Max(maxX, t.Xs[k]);
			}
			for (int dy = -12; dy < h - 16; dy++)
			{
				if (minY + dy < 0 || maxY + dy >= h)
				{
					continue;
				}
				for (int dx = -4; dx < w - 12; dx++)
				{
					if (minX + dx < 0 || maxX + dx >= w)
					{
						continue;
					}
					int same = 0;
					int empty = 0;
					for (int k = 0; k < t.Ys.Length; k++)
					{
						byte got = idx[t.Ys[k] + dy, t.Xs[k] + dx];
						if (got == t.V[k])
						{
							same++;
						}
						if (got == 0)
						{
							empty++;
						}
					}
					double score = same - 0.6 * empty;
					if (score > best)
					{
						best = score;
						bd = d;
						bdx = dx;
						bdy = dy;
					}
				}
			}
		}
		return (best, bd, bdx, bdy);
	}

	public static void Analyse(List<BodyFrame> frames, TrainerGame game, bool red)
	{
		var temps = HeadTemplates(frames, game.NeckY);
		Dictionary<int, ZoneRoles> table = red ? game.MaleRoles : game.FemaleRoles;
		int[] hat = red ? game.MaleHat : game.FemaleHat;
		foreach (BodyFrame f in frames)
		{
			var (_, d, dx, dy) = LocateHead(f.Idx, temps);
			HeadDir dir = temps[d].Item1;
			f.Dir = dir;
			double baseX = (dir == HeadDir.Left || dir == HeadDir.Right) ? 7.5 : 8.0;
			double cx = (dir != HeadDir.Right) ? baseX : (16.0 - baseX);
			f.AnchorX = cx + dx;
			f.AnchorY = game.AnchorY + dy;
			byte[,] body = (byte[,])f.Idx.Clone();
			int h = body.GetLength(0);
			int w = body.GetLength(1);
			Template t = temps[d].Item2;
			for (int k = 0; k < t.Ys.Length; k++)
			{
				body[t.Ys[k] + dy, t.Xs[k] + dx] = 0;
			}
			int neck = (int)Math.Round(f.AnchorY + game.NeckY - game.AnchorY, MidpointRounding.ToEven);
			for (int y = 0; y < Math.Min(h, Math.Max(0, neck - 1)); y++)
			{
				for (int x = 0; x < w; x++)
				{
					if (body[y, x] == OutlineIdx)
					{
						body[y, x] = 0;
					}
				}
			}
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					double ddx = x + 0.5 - f.AnchorX;
					double ddy = y + 0.5 - f.AnchorY;
					if (y < neck && Math.Sqrt(ddx * ddx + ddy * ddy) < 8.5 && Array.IndexOf(hat, body[y, x]) >= 0)
					{
						body[y, x] = 0;
					}
				}
			}
			if (!red && game.StripFemaleHair)
			{
				body = StripLeafHair(body, neck);
			}
			f.Body = body;
			f.Neck = neck;
			f.Roles = new BodyRole[h, w];
			f.Tones = new byte[h, w];
			for (int y = 0; y < h; y++)
			{
				int zone = (y < neck + 4) ? 0 : ((y < neck + 6) ? 1 : 2);
				for (int x = 0; x < w; x++)
				{
					if (!table.TryGetValue(body[y, x], out ZoneRoles r))
					{
						continue;
					}
					var (role, tone) = r.Zones[zone];
					// props far from the body (rods, the bike's front wheel...) keep their colors
					if (role == BodyRole.None || (role != BodyRole.Skin && Math.Abs(x + 0.5 - f.AnchorX) > 7.0))
					{
						continue;
					}
					f.Roles[y, x] = role;
					f.Tones[y, x] = tone;
				}
			}
		}
	}

	private static bool IsLeafHair(int c) => c == 1 || c == 4;

	private static byte[,] StripLeafHair(byte[,] body, int neck)
	{
		byte[,] o = (byte[,])body.Clone();
		int h = o.GetLength(0);
		int w = o.GetLength(1);
		List<(int, int)> hair = new List<(int, int)>();
		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				if (IsLeafHair(o[y, x]))
				{
					hair.Add((y, x));
				}
			}
		}
		foreach (var (y, x) in hair)
		{
			List<int> left = new List<int>();
			List<int> right = new List<int>();
			for (int c = 0; c < x; c++)
			{
				int v = o[y, c];
				if (v != 0 && !IsLeafHair(v))
				{
					left.Add(v);
				}
			}
			for (int c = x + 1; c < w; c++)
			{
				int v = o[y, c];
				if (v != 0 && !IsLeafHair(v))
				{
					right.Add(v);
				}
			}
			bool inside = false;
			if (left.Count > 0 && right.Count > 0)
			{
				for (int k = Math.Max(0, left.Count - 3); k < left.Count && !inside; k++)
				{
					inside = left[k] != OutlineIdx;
				}
				for (int k = 0; k < Math.Min(3, right.Count) && !inside; k++)
				{
					inside = right[k] != OutlineIdx;
				}
			}
			// hair over the back/shoulders: patch it with the shirt so the body has no hole
			o[y, x] = (byte)(inside ? ((y < neck + 4) ? 5 : 12) : 0);
		}
		// outline pixels that only bordered the removed hair go too
		for (int pass = 0; pass < 2; pass++)
		{
			List<(int, int)> lonely = new List<(int, int)>();
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					if (o[y, x] != OutlineIdx)
					{
						continue;
					}
					bool solid = false;
					foreach (var (ny, nx) in new[] { (y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1) })
					{
						int v = (ny >= 0 && ny < h && nx >= 0 && nx < w) ? o[ny, nx] : 0;
						if (v != 0 && v != OutlineIdx)
						{
							solid = true;
						}
					}
					if (!solid)
					{
						lonely.Add((y, x));
					}
				}
			}
			foreach (var (y, x) in lonely)
			{
				o[y, x] = 0;
			}
		}
		return o;
	}
}
