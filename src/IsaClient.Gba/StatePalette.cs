using System;
using System.Collections.Generic;

namespace IsaClient.Gba;

/// <summary>
/// Recolors the player inside an mGBA state before it's loaded. The game copies the player's palette to memory only on
/// a new map, so a state keeps the palette of whatever trainer was used when that map loaded (maybe several trainers
/// ago). mGBA's state (version 0x0100000x) holds the hardware palette at 0x800 (1 KB) and work RAM at 0x21000 (256 KB);
/// FireRed keeps two palette buffers in work RAM (unfaded, then faded = what goes to the screen every frame).
/// Each sprite palette slot with IsaClient's player signature (0x0000 then the 0x0842 outline: no palette of the game
/// starts so), the reflection's start, or one of the original Red/Leaf palettes gets the current trainer's.
/// </summary>
public static class StatePalette
{
	private const int PaletteAt = 0x800;

	private const int WorkRamAt = 0x21000;

	private const int WorkRamSize = 0x40000;

	/// <summary>Returns how many palette slots changed (0 when nothing matched or the state isn't mGBA's GBA format).</summary>
	public static int Patch(byte[] state, byte[] player, byte[] reflection, IList<byte[]> oldPlayers, IList<byte[]> oldReflections, out string where)
	{
		where = "not an mGBA GBA state";
		if (state == null || state.Length < WorkRamAt + WorkRamSize || player == null || player.Length < 32 || reflection == null || reflection.Length < 32)
		{
			return 0;
		}
		if ((BitConverter.ToUInt32(state, 0) & 0xFFFFFF00) != 0x01000000)
		{
			return 0;
		}
		List<int> buffers = new List<int>();
		// the faded buffer equals the hardware palette; the unfaded one sits right before it
		int found = Find(state, WorkRamAt, WorkRamSize, PaletteAt, 0x400);
		if (found >= 0)
		{
			// both buffers equal the screen when no fade is going on: the first match is then the unfaded one
			buffers.Add(found);
			if (found + 0x800 <= WorkRamAt + WorkRamSize && Equal(state, found, found + 0x400, 0x400))
			{
				buffers.Add(found + 0x400);
			}
			else if (found - 0x400 >= WorkRamAt)
			{
				buffers.Add(found - 0x400);
			}
		}
		else
		{
			// in the middle of a fade: look for the two equal blocks instead
			for (int o = WorkRamAt; o + 0x800 <= WorkRamAt + WorkRamSize; o += 4)
			{
				if (Equal(state, o, o + 0x400, 0x400) && Varied(state, o))
				{
					buffers.Add(o);
					buffers.Add(o + 0x400);
					break;
				}
			}
		}
		if (buffers.Count == 0)
		{
			where = "palette buffers not found";
			return 0;
		}
		buffers.Add(PaletteAt);
		where = "buffers at 0x" + (0x02000000 + buffers[0] - WorkRamAt).ToString("X8");
		int changed = 0;
		int r0 = reflection[0] | (reflection[1] << 8);
		int r1 = reflection[2] | (reflection[3] << 8);
		foreach (int buffer in buffers)
		{
			for (int slot = 0; slot < 16; slot++)
			{
				int at = buffer + 0x200 + slot * 32;
				int c0 = state[at] | (state[at + 1] << 8);
				int c1 = state[at + 2] | (state[at + 3] << 8);
				byte[] put = null;
				if ((c0 == 0x0000 && c1 == 0x0842) || Any(state, at, oldPlayers))
				{
					put = player;
				}
				else if ((c0 == r0 && c1 == r1) || Any(state, at, oldReflections))
				{
					put = reflection;
				}
				if (put != null && !Equal(state, at, put))
				{
					Buffer.BlockCopy(put, 0, state, at, 32);
					changed++;
				}
			}
		}
		return changed;
	}

	private static int Find(byte[] d, int start, int len, int patternAt, int patternLen)
	{
		for (int o = start; o + patternLen <= start + len; o += 2)
		{
			if (Equal(d, o, patternAt, patternLen))
			{
				return o;
			}
		}
		return -1;
	}

	private static bool Equal(byte[] d, int a, int b, int len)
	{
		for (int i = 0; i < len; i++)
		{
			if (d[a + i] != d[b + i])
			{
				return false;
			}
		}
		return true;
	}

	private static bool Equal(byte[] d, int at, byte[] b)
	{
		for (int i = 0; i < 32; i++)
		{
			if (d[at + i] != b[i])
			{
				return false;
			}
		}
		return true;
	}

	private static bool Any(byte[] d, int at, IList<byte[]> list)
	{
		if (list == null)
		{
			return false;
		}
		foreach (byte[] b in list)
		{
			if (b != null && b.Length >= 32 && Equal(d, at, b))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>A real palette buffer has many different colors (not an empty or filled area).</summary>
	private static bool Varied(byte[] d, int o)
	{
		HashSet<int> colors = new HashSet<int>();
		for (int c = 0; c < 0x400 && colors.Count < 24; c += 2)
		{
			colors.Add(d[o + c] | (d[o + c + 1] << 8));
		}
		return colors.Count >= 24;
	}
}
