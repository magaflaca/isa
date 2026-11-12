namespace Terraria.IO;

public partial class PlayerFileData
{
	/// <summary>
	/// IsaClient ("Save .plr"): a local character file for a copy of another player. Never a cloud save, always a new
	/// file name (GetPlayerPathFromName appends a number when the name is taken), nothing written yet.
	/// </summary>
	public static PlayerFileData IsaCreateCopyFile(Player player, string path = null)
	{
		PlayerFileData playerFileData = new PlayerFileData();
		playerFileData.Metadata = FileMetadata.FromCurrentSettings(FileType.Player);
		playerFileData.Player = player;
		playerFileData._isCloudSave = false;
		playerFileData._path = path ?? Main.GetPlayerPathFromName(player.name, cloudSave: false);
		return playerFileData;
	}

	/// <summary>IsaClient (Keep SSC Character): lets the save write a server-side character for a moment.</summary>
	public void IsaSetServerSide(bool value)
	{
		ServerSideCharacter = value;
	}
}
