namespace Terraria;

public partial class Mount
{
	/// <summary>IsaClient: true if that mount type can fly or hover (used by MultiClient sessions to follow through the air).</summary>
	public static bool IsaCanFly(int type)
	{
		return mounts != null && type >= 0 && type < mounts.Length && mounts[type] != null && (mounts[type].flightTimeMax > 0 || mounts[type].usesHover);
	}

	/// <summary>IsaClient: the mount a buff puts you on (230 -> the Witch's Broom), or -1.</summary>
	public static int IsaMountOfBuff(int buff)
	{
		if (mounts == null || buff <= 0)
		{
			return -1;
		}
		for (int i = 0; i < mounts.Length; i++)
		{
			if (mounts[i] != null && mounts[i].buff == buff)
			{
				return i;
			}
		}
		return -1;
	}
}
