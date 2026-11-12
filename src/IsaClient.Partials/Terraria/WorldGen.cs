using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria.ID;
using Terraria.Map;

namespace Terraria;

public partial class WorldGen
{
	public static void SquareTileFrame(int i, int j, bool resetFrame, bool noBreak)
	{
		TileFrame(i - 1, j - 1, resetFrame: false, noBreak);
		TileFrame(i - 1, j, resetFrame: false, noBreak);
		TileFrame(i - 1, j + 1, resetFrame: false, noBreak);
		TileFrame(i, j - 1, resetFrame: false, noBreak);
		TileFrame(i, j, resetFrame, noBreak);
		TileFrame(i, j + 1, resetFrame: false, noBreak);
		TileFrame(i + 1, j - 1, resetFrame: false, noBreak);
		TileFrame(i + 1, j, resetFrame: false, noBreak);
		TileFrame(i + 1, j + 1, resetFrame: false, noBreak);
	}

	public static void RangeFrame(int startX, int startY, int endX, int endY, bool resetFrame, bool noBreak = false)
	{
		MapUpdateQueue.Add(new Rectangle(startX - 2, startY - 2, startX - endX + 4, startY - endY + 4));
		int num3 = endX + 1;
		int num4 = endY + 1;
		for (int k = startX - 1; k < num3 + 1; k++)
		{
			for (int l = startY - 1; l < num4 + 1; l++)
			{
				TileFrame(k, l, resetFrame, noBreak);
				Framing.WallFrame(k, l);
			}
		}
	}

	private static readonly HashSet<long> tileFrameImportantRunning = new HashSet<long>();

	private static void TileFrameImportantGuarded(int i, int j, int type, Tile tileCache, bool resetFrame)
	{
		if (tileCache != null && tileCache.active() && !TileID.Sets.Torches[type] && !CanKillTile(i, j))
		{
			return;
		}
		long key = ((long)i << 32) | (uint)j;
		if (!tileFrameImportantRunning.Add(key))
		{
			return;
		}
		try
		{
			TileFrameImportantChecked(i, j, type, tileCache, resetFrame);
		}
		finally
		{
			tileFrameImportantRunning.Remove(key);
		}
	}
}
