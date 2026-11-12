using IsaClient.Building;

namespace Terraria;

public partial class Tile
{
	// ---- tModLoader-style accessors used by the world editor ----
	public bool HasTile
	{
		get { return active(); }
		set { active(value); }
	}

	public ushort TileType
	{
		get { return type; }
		set { type = value; }
	}

	public ushort WallType
	{
		get { return wall; }
		set { wall = value; }
	}

	public int LiquidType
	{
		get { return liquidType(); }
		set { liquidType(value); }
	}

	public byte LiquidAmount
	{
		get { return liquid; }
		set { liquid = value; }
	}

	public byte TileColor
	{
		get { return color(); }
		set { color(value); }
	}

	public byte WallColor
	{
		get { return wallColor(); }
		set { wallColor(value); }
	}

	public short TileFrameX
	{
		get { return frameX; }
		set { frameX = value; }
	}

	public short TileFrameY
	{
		get { return frameY; }
		set { frameY = value; }
	}

	public bool IsTileInvisible
	{
		get { return invisibleBlock(); }
		set { invisibleBlock(value); }
	}

	public bool IsWallInvisible
	{
		get { return invisibleWall(); }
		set { invisibleWall(value); }
	}

	public bool IsTileFullbright
	{
		get { return fullbrightBlock(); }
		set { fullbrightBlock(value); }
	}

	public bool IsWallFullbright
	{
		get { return fullbrightWall(); }
		set { fullbrightWall(value); }
	}

	public bool HasActuator
	{
		get { return actuator(); }
		set { actuator(value); }
	}

	public bool IsActuated
	{
		get { return inActive(); }
		set { inActive(value); }
	}

	public bool HasUnactuatedTile => nactive();

	public bool RedWire
	{
		get { return wire(); }
		set { wire(value); }
	}

	public bool BlueWire
	{
		get { return wire2(); }
		set { wire2(value); }
	}

	public bool GreenWire
	{
		get { return wire3(); }
		set { wire3(value); }
	}

	public bool YellowWire
	{
		get { return wire4(); }
		set { wire4(value); }
	}

	public BlockType BlockType
	{
		get { return (BlockType)blockType(); }
		set
		{
			halfBrick(value == BlockType.HalfBlock);
			slope((value >= BlockType.SlopeDownLeft) ? (byte)((int)value - 1) : (byte)0);
		}
	}
}
