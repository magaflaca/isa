using IsaClient.Building;

namespace Terraria;

public partial class NetMessage
{
	private static bool SendBlocked(int msgType, int number, float number2, float number3, float number4)
	{
		if (IsaClient.Items.ShotPreview.Busy)
		{
			return true;
		}
		if (msgType == 28 && Main.netMode == 1)
		{
			IsaClient.MultiClient.BotSets.NoteStrike(number);
		}
		else if (msgType == 117 && Main.netMode == 1 && number != Main.myPlayer && (((byte)number4) & 2) != 0)
		{
			IsaClient.MultiClient.BotSets.NotePlayerStrike(number);
		}
		// Your position stays put on the server while you paste (the paste moves you); bots' Multi Paste parts are not yours.
		if (Main.netMode == 0 || (msgType == 13 && Main.netMode == 1 && (Selection.ConcurrentPastes - IsaClient.Building.PasteJob.BotParts > 0 || Selection.ConcurrentFillOrDels > 0 || IsaClient.Building.PasteJob.InBotPart)))
		{
			return true;
		}
		if (!IsaClient.Networking.ProtocolCompat.AllowSend(msgType, number, number2, number3, number4))
		{
			return true;
		}
		if (Main.netMode == 1)
		{
			if (msgType == 31)
			{
				IsaClient.WorldTools.ProtectedChests.OnOpenRequest(number, (int)number2);
			}
			else if (IsaClient.WorldTools.ProtectedChests.SuppressPacket(msgType))
			{
				return true;
			}
		}
		return false;
	}
}
