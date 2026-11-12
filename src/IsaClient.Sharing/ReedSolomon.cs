using System;
using System.Collections.Generic;

namespace IsaClient.Sharing;

/// <summary>
/// Reed-Solomon error correction over GF(256) (the code QR codes and CDs use): <see cref="Encode"/> adds
/// <c>nsym</c> check bytes to a block of up to 255 - nsym bytes, and <see cref="Decode"/> repairs up to nsym / 2
/// damaged bytes anywhere in it. Plain arrays only (no Span), so it runs anywhere.
/// </summary>
public static class ReedSolomon
{
	private static readonly int[] Exp = new int[512];

	private static readonly int[] Log = new int[256];

	private static readonly Dictionary<int, int[]> _generators = new Dictionary<int, int[]>();

	static ReedSolomon()
	{
		int x = 1;
		for (int i = 0; i < 255; i++)
		{
			Exp[i] = x;
			Log[x] = i;
			x <<= 1;
			if ((x & 0x100) != 0)
			{
				x ^= 0x11D;
			}
		}
		for (int i = 255; i < 512; i++)
		{
			Exp[i] = Exp[i - 255];
		}
	}

	private static int Mul(int a, int b) => (a == 0 || b == 0) ? 0 : Exp[Log[a] + Log[b]];

	private static int Div(int a, int b) => (a == 0) ? 0 : Exp[(Log[a] + 255 - Log[b]) % 255];

	private static int Pow(int x, int power) => Exp[((Log[x] * power) % 255 + 255) % 255];

	private static int Inverse(int x) => Exp[255 - Log[x]];

	private static int[] Scale(int[] p, int x)
	{
		int[] r = new int[p.Length];
		for (int i = 0; i < p.Length; i++)
		{
			r[i] = Mul(p[i], x);
		}
		return r;
	}

	private static int[] Add(int[] p, int[] q)
	{
		int[] r = new int[Math.Max(p.Length, q.Length)];
		for (int i = 0; i < p.Length; i++)
		{
			r[i + r.Length - p.Length] = p[i];
		}
		for (int i = 0; i < q.Length; i++)
		{
			r[i + r.Length - q.Length] ^= q[i];
		}
		return r;
	}

	private static int[] Multiply(int[] p, int[] q)
	{
		int[] r = new int[p.Length + q.Length - 1];
		for (int j = 0; j < q.Length; j++)
		{
			for (int i = 0; i < p.Length; i++)
			{
				r[i + j] ^= Mul(p[i], q[j]);
			}
		}
		return r;
	}

	private static int Eval(int[] p, int x)
	{
		int y = p[0];
		for (int i = 1; i < p.Length; i++)
		{
			y = Mul(y, x) ^ p[i];
		}
		return y;
	}

	private static int[] Reverse(int[] p)
	{
		int[] r = (int[])p.Clone();
		Array.Reverse(r);
		return r;
	}

	private static int[] Generator(int nsym)
	{
		lock (_generators)
		{
			if (!_generators.TryGetValue(nsym, out int[] g))
			{
				g = new[] { 1 };
				for (int i = 0; i < nsym; i++)
				{
					g = Multiply(g, new[] { 1, Pow(2, i) });
				}
				_generators[nsym] = g;
			}
			return g;
		}
	}

	/// <summary>The block with its <paramref name="nsym"/> check bytes after it.</summary>
	public static byte[] Encode(byte[] data, int nsym)
	{
		if (data.Length + nsym > 255)
		{
			throw new ArgumentException("block too long");
		}
		int[] gen = Generator(nsym);
		int[] work = new int[data.Length + nsym];
		for (int i = 0; i < data.Length; i++)
		{
			work[i] = data[i];
		}
		for (int i = 0; i < data.Length; i++)
		{
			int coef = work[i];
			if (coef != 0)
			{
				for (int j = 1; j < gen.Length; j++)
				{
					work[i + j] ^= Mul(gen[j], coef);
				}
			}
		}
		byte[] result = new byte[data.Length + nsym];
		Array.Copy(data, result, data.Length);
		for (int i = data.Length; i < result.Length; i++)
		{
			result[i] = (byte)work[i];
		}
		return result;
	}

	/// <summary>Repairs a block (data and check bytes) in place. False when it's too damaged to repair.</summary>
	public static bool Decode(byte[] block, int nsym)
	{
		try
		{
			int[] msg = new int[block.Length];
			for (int i = 0; i < block.Length; i++)
			{
				msg[i] = block[i];
			}
			int[] synd = Syndromes(msg, nsym);
			bool clean = true;
			foreach (int s in synd)
			{
				clean &= s == 0;
			}
			if (clean)
			{
				return true;
			}
			int[] errLoc = ErrorLocator(synd, nsym);
			if (errLoc == null)
			{
				return false;
			}
			List<int> errPos = FindErrors(Reverse(errLoc), msg.Length);
			if (errPos == null)
			{
				return false;
			}
			msg = Correct(msg, synd, errPos);
			if (msg == null)
			{
				return false;
			}
			foreach (int s in Syndromes(msg, nsym))
			{
				if (s != 0)
				{
					return false;
				}
			}
			for (int i = 0; i < block.Length; i++)
			{
				block[i] = (byte)msg[i];
			}
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static int[] Syndromes(int[] msg, int nsym)
	{
		// One leading zero, as the locator search expects.
		int[] synd = new int[nsym + 1];
		for (int i = 0; i < nsym; i++)
		{
			synd[i + 1] = Eval(msg, Pow(2, i));
		}
		return synd;
	}

	private static int[] ErrorLocator(int[] synd, int nsym)
	{
		int[] errLoc = { 1 };
		int[] oldLoc = { 1 };
		int shift = synd.Length - nsym;
		for (int i = 0; i < nsym; i++)
		{
			int k = i + shift;
			int delta = synd[k];
			for (int j = 1; j < errLoc.Length; j++)
			{
				delta ^= Mul(errLoc[errLoc.Length - 1 - j], synd[k - j]);
			}
			int[] grown = new int[oldLoc.Length + 1];
			Array.Copy(oldLoc, grown, oldLoc.Length);
			oldLoc = grown;
			if (delta != 0)
			{
				if (oldLoc.Length > errLoc.Length)
				{
					int[] newLoc = Scale(oldLoc, delta);
					oldLoc = Scale(errLoc, Inverse(delta));
					errLoc = newLoc;
				}
				errLoc = Add(errLoc, Scale(oldLoc, delta));
			}
		}
		int lead = 0;
		while (lead < errLoc.Length && errLoc[lead] == 0)
		{
			lead++;
		}
		int[] trimmed = new int[errLoc.Length - lead];
		Array.Copy(errLoc, lead, trimmed, 0, trimmed.Length);
		return ((trimmed.Length - 1) * 2 > nsym) ? null : trimmed;
	}

	private static List<int> FindErrors(int[] errLoc, int length)
	{
		int errs = errLoc.Length - 1;
		List<int> pos = new List<int>();
		for (int i = 0; i < length; i++)
		{
			if (Eval(errLoc, Pow(2, i)) == 0)
			{
				pos.Add(length - 1 - i);
			}
		}
		return (pos.Count == errs) ? pos : null;
	}

	private static int[] Correct(int[] msg, int[] synd, List<int> errPos)
	{
		List<int> coefPos = new List<int>();
		foreach (int p in errPos)
		{
			coefPos.Add(msg.Length - 1 - p);
		}
		int[] errLoc = { 1 };
		foreach (int i in coefPos)
		{
			errLoc = Multiply(errLoc, Add(new[] { 1 }, new[] { Pow(2, i), 0 }));
		}
		// The error evaluator: (reversed syndromes * locator) mod x^(n+1).
		int[] product = Multiply(Reverse(synd), errLoc);
		int nsym = errLoc.Length - 1;
		int keep = nsym + 1;
		int[] remainder = new int[Math.Min(keep, product.Length)];
		Array.Copy(product, product.Length - remainder.Length, remainder, 0, remainder.Length);
		int[] errEval = Reverse(remainder);
		List<int> x = new List<int>();
		foreach (int c in coefPos)
		{
			x.Add(Pow(2, -(255 - c)));
		}
		int[] e = new int[msg.Length];
		for (int i = 0; i < x.Count; i++)
		{
			int xiInv = Inverse(x[i]);
			int prime = 1;
			for (int j = 0; j < x.Count; j++)
			{
				if (j != i)
				{
					prime = Mul(prime, 1 ^ Mul(xiInv, x[j]));
				}
			}
			if (prime == 0)
			{
				return null;
			}
			int y = Mul(x[i], Eval(Reverse(errEval), xiInv));
			e[errPos[i]] = Div(y, prime);
		}
		int[] result = new int[msg.Length];
		for (int i = 0; i < msg.Length; i++)
		{
			result[i] = msg[i] ^ e[i];
		}
		return result;
	}
}
