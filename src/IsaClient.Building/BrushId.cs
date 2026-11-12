using ReLogic.Reflection;

namespace IsaClient.Building;

/// <summary>Ids of the world edit brush kinds.</summary>
public class BrushId
{
	public const byte TileBrush = 0;

	public const byte EraseBrush = 1;

	public const byte WallBrush = 2;

	public const byte LiquidAndDryBrush = 3;

	public const byte TilePaintBrush = 4;

	public const byte TileExploitBrush = 5;

	public static readonly byte Count = 6;

	public static readonly IdDictionary Search = IdDictionary.Create<BrushId, byte>();
}
