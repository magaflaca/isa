namespace Terraria.IO;

public partial class WorldFile
{
	public static void SaveWorldToLocalFile(bool resetTime = false, bool useTemps = false)
	{
		_SaveWorld(useCloudSaving: false, resetTime, useTemps, canBeSkipped: false);
	}
}
