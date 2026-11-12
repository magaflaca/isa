namespace IsaClient.Core;

/// <summary>Small math helpers.</summary>
public static class MathUtil
{
	public static bool IsEven(int value)
	{
		return (value & 1) == 0;
	}
}
