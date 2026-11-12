using System;
using System.Collections.Generic;
using System.Linq;

namespace IsaClient.Gba.Trainer;

/// <summary>
/// Overworld frames (template body + baked head) and the palette fitting of t2p/compose.py: an agglomerative merge of
/// the few colors used down to 15, with outline and face colors resisting.
/// </summary>
public static class TrainerCompose
{
	private const double CanvasCx = 16.0;

	private const double CanvasCy = 26.0;

	private static int PyRound(double v) => (int)Math.Round(v, MidpointRounding.ToEven);

	public static Rgba[,] Compose(BodyFrame f, Rgb[] palette, HeadSet heads, TrainerColors look, Rgba[][,] painted)
	{
		int h = f.Body.GetLength(0);
		int w = f.Body.GetLength(1);
		Rgba[,] img = new Rgba[h, w];
		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				byte c = f.Body[y, x];
				if (c == 0)
				{
					continue;
				}
				img[y, x] = new Rgba((f.Roles[y, x] != BodyRole.None) ? look.Role(f.Roles[y, x], f.Tones[y, x]) : palette[c]);
			}
		}
		HeadImage head = heads.Of(f.Dir);
		Rgba[,] hp = painted[(int)f.Dir];
		int ox = PyRound(f.AnchorX - CanvasCx);
		int oy = PyRound(f.AnchorY - CanvasCy);
		bool behindBody = f.Dir == HeadDir.Down;
		for (int j = 0; j < head.H; j++)
		{
			int y = j + oy;
			if (y < 0 || y >= h)
			{
				continue;
			}
			for (int i = 0; i < head.W; i++)
			{
				int x = i + ox;
				if (x < 0 || x >= w || !hp[j, i].A)
				{
					continue;
				}
				// hair hanging behind the back is hidden by the body when seen from the front
				if (behindBody && img[y, x].A && head.Back[j, i] && y >= f.Neck)
				{
					continue;
				}
				img[y, x] = hp[j, i];
			}
		}
		return img;
	}

	private static void Feat(double r, double g, double b, out double f0, out double f1, out double f2)
	{
		r /= 255.0;
		g /= 255.0;
		b /= 255.0;
		double y = 0.299 * r + 0.587 * g + 0.114 * b;
		f0 = y * 2.0;
		f1 = r - y;
		f2 = b - y;
	}

	private sealed class Cluster
	{
		public double R;

		public double G;

		public double B;

		public double W;

		public List<Rgb> Members;

		public double Guard;

		public bool Fixed;
	}

	/// <summary>Returns a 16-entry palette (0 transparent) and the indexed images.</summary>
	public static (Rgb[] Palette, List<byte[,]> Indexed) Quantize(List<Rgba[,]> images, IEnumerable<Rgb> protectedColors, int n = 15)
	{
		Dictionary<Rgb, int> counts = new Dictionary<Rgb, int>();
		foreach (Rgba[,] im in images)
		{
			foreach (Rgba p in im)
			{
				if (p.A)
				{
					Rgb s = p.C.Snap8;
					counts.TryGetValue(s, out int k);
					counts[s] = k + 1;
				}
			}
		}
		HashSet<Rgb> prot = new HashSet<Rgb>(protectedColors.Select(c => c.Snap8));
		Rgb outline = TrainerColors.Outline.Snap8;
		// np.unique(axis=0): rows sorted lexicographically
		List<Cluster> clusters = counts.Keys.OrderBy(c => c.R).ThenBy(c => c.G).ThenBy(c => c.B).Select(c => new Cluster
		{
			R = c.R,
			G = c.G,
			B = c.B,
			W = counts[c],
			Members = new List<Rgb> { c },
			Guard = prot.Contains(c) ? 3.0 : 1.0,
			Fixed = c.Equals(outline)
		}).ToList();
		while (clusters.Count > n)
		{
			int m = clusters.Count;
			double[] f0 = new double[m];
			double[] f1 = new double[m];
			double[] f2 = new double[m];
			for (int i = 0; i < m; i++)
			{
				Feat(clusters[i].R, clusters[i].G, clusters[i].B, out f0[i], out f1[i], out f2[i]);
			}
			double bestCost = 0.0;
			int bi = -1;
			int bj = -1;
			for (int i = 0; i < m; i++)
			{
				for (int j = i + 1; j < m; j++)
				{
					Cluster a = clusters[i];
					Cluster b = clusters[j];
					if (a.Fixed && b.Fixed)
					{
						continue;
					}
					double d0 = f0[i] - f0[j];
					double d1 = f1[i] - f1[j];
					double d2 = f2[i] - f2[j];
					double cost = (d0 * d0 + d1 * d1 + d2 * d2) * a.W * b.W / (a.W + b.W);
					cost *= a.Guard * b.Guard;
					if (bi < 0 || cost < bestCost)
					{
						bestCost = cost;
						bi = i;
						bj = j;
					}
				}
			}
			Cluster ca = clusters[bi];
			Cluster cb = clusters[bj];
			Cluster keep = (ca.Fixed || ca.Guard > cb.Guard) ? ca : ((cb.Fixed || cb.Guard > ca.Guard) ? cb : null);
			Cluster merged = new Cluster
			{
				W = ca.W + cb.W,
				Members = ca.Members.Concat(cb.Members).ToList(),
				Guard = Math.Max(ca.Guard, cb.Guard),
				Fixed = ca.Fixed || cb.Fixed
			};
			if (keep != null)
			{
				merged.R = keep.R;
				merged.G = keep.G;
				merged.B = keep.B;
			}
			else
			{
				merged.R = (ca.R * ca.W + cb.R * cb.W) / (ca.W + cb.W);
				merged.G = (ca.G * ca.W + cb.G * cb.W) / (ca.W + cb.W);
				merged.B = (ca.B * ca.W + cb.B * cb.W) / (ca.W + cb.W);
			}
			clusters.RemoveAt(bj);
			clusters.RemoveAt(bi);
			clusters.Add(merged);
		}
		// fixed first, then brighter first (a stable sort, like Python's)
		clusters = clusters.Select((c, i) => (c, i)).OrderBy(t => t.c.Fixed ? 0 : 1).ThenBy(t =>
		{
			Feat(t.c.R, t.c.G, t.c.B, out double l, out _, out _);
			return -l;
		}).ThenBy(t => t.i).Select(t => t.c).ToList();
		Rgb[] pal = new Rgb[16];
		Dictionary<Rgb, byte> lut = new Dictionary<Rgb, byte>();
		for (int t = 0; t < clusters.Count; t++)
		{
			Cluster k = clusters[t];
			pal[t + 1] = new Rgb(PyRound(k.R) / 8 * 8, PyRound(k.G) / 8 * 8, PyRound(k.B) / 8 * 8);
			foreach (Rgb mem in k.Members)
			{
				lut[mem] = (byte)(t + 1);
			}
		}
		List<byte[,]> outImgs = new List<byte[,]>();
		foreach (Rgba[,] im in images)
		{
			int h = im.GetLength(0);
			int w = im.GetLength(1);
			byte[,] idx = new byte[h, w];
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					if (im[y, x].A)
					{
						idx[y, x] = lut[im[y, x].C.Snap8];
					}
				}
			}
			outImgs.Add(idx);
		}
		return (pal, outImgs);
	}

	/// <summary>Fits the game's water-reflection tint (an affine map per channel) and applies it to the new palette.</summary>
	public static Rgb[] ReflectionPalette(Rgb[] pal, Rgb[] origPal, Rgb[] origRefl)
	{
		double[] a = new double[3];
		double[] b = new double[3];
		for (int ch = 0; ch < 3; ch++)
		{
			double sx = 0;
			double sy = 0;
			double sxx = 0;
			double sxy = 0;
			int n = 0;
			for (int i = 1; i < 16; i++)
			{
				double x = Channel(origPal[i], ch);
				double y = Channel(origRefl[i], ch);
				sx += x;
				sy += y;
				sxx += x * x;
				sxy += x * y;
				n++;
			}
			double den = n * sxx - sx * sx;
			if (Math.Abs(den) < 1e-9)
			{
				// one value only: least norm solution of [x 1]·[a b] = y
				double mean = sy / n;
				double x0 = sx / n;
				a[ch] = mean * x0 / (x0 * x0 + 1.0);
				b[ch] = mean / (x0 * x0 + 1.0);
			}
			else
			{
				a[ch] = (n * sxy - sx * sy) / den;
				b[ch] = (sy - a[ch] * sx) / n;
			}
		}
		Rgb[] outPal = new Rgb[16];
		outPal[0] = origRefl[0];
		for (int i = 1; i < 16; i++)
		{
			int[] v = new int[3];
			for (int ch = 0; ch < 3; ch++)
			{
				// the tiny epsilon keeps exact fits (an integer that lstsq lands a hair above) on the same side
				double r = Math.Max(0.0, Math.Min(248.0, a[ch] * Channel(pal[i], ch) + b[ch] + 1e-9));
				v[ch] = (int)r / 8 * 8;
			}
			outPal[i] = new Rgb(v[0], v[1], v[2]);
		}
		return outPal;
	}

	private static double Channel(Rgb c, int ch) => ch switch
	{
		0 => c.R,
		1 => c.G,
		_ => c.B
	};
}
