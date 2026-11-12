#nullable enable
namespace IsaClient.Net.Pipe;

/// <summary>
/// Little-endian reads and writes and the two checksums the protocol uses. Plain <see cref="byte"/> arrays only — no
/// Span, so the module needs nothing beyond the framework the game already ships with.
/// </summary>
internal static class Bits
{
	public static void WriteU16(byte[] d, int o, ushort v)
	{
		d[o] = (byte)v;
		d[o + 1] = (byte)(v >> 8);
	}

	public static void WriteU32(byte[] d, int o, uint v)
	{
		d[o] = (byte)v;
		d[o + 1] = (byte)(v >> 8);
		d[o + 2] = (byte)(v >> 16);
		d[o + 3] = (byte)(v >> 24);
	}

	public static ushort ReadU16(byte[] d, int o)
	{
		return (ushort)(d[o] | (d[o + 1] << 8));
	}

	public static uint ReadU32(byte[] d, int o)
	{
		return (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));
	}

	/// <summary>CRC-16/CCITT-FALSE over the whole blob.</summary>
	public static ushort Crc16(byte[] data)
	{
		ushort crc = 0xFFFF;
		for (int j = 0; j < data.Length; j++)
		{
			crc ^= (ushort)(data[j] << 8);
			for (int i = 0; i < 8; i++)
			{
				crc = (ushort)(((crc & 0x8000) != 0) ? ((crc << 1) ^ 0x1021) : (crc << 1));
			}
		}
		return crc;
	}

	/// <summary>CRC-8 the ping carrier puts on each reassembled frame.</summary>
	public static byte Crc8(byte[] data, int offset, int length)
	{
		byte crc = 0;
		for (int j = 0; j < length; j++)
		{
			crc ^= data[offset + j];
			for (int i = 0; i < 8; i++)
			{
				crc = (byte)(((crc & 0x80) != 0) ? ((crc << 1) ^ 0x07) : (crc << 1));
			}
		}
		return crc;
	}
}
