using Terraria;
using IsaClient.Core;

namespace IsaClient.Movement;

/// <summary>Noclip: fly through blocks at the chosen speed.</summary>
public static class Noclip
{
	public const string KeyName = "Noclip";

	public const string SpeedKeyName = "Noclip Speed Modifier";

	[Setting("Noclip", Remembered = true)]
	public static bool Enabled;

	[Setting("NoclipSpeed")]
	public static int Speed = 25;

	[Setting("HighDataRate", Remembered = true)]
	public static bool RapidPosUpdate;

	public static bool JitteringMyPosRn;

	public static void CycleSpeed()
	{
		string text = "";
		switch (Speed)
		{
		case 9:
			Speed = 25;
			text = "default";
			break;
		case 64:
			Speed = 9;
			text = "slow";
			break;
		default:
			Speed = 64;
			text = "fast";
			break;
		}
		ClientChat.Info("Noclip speed set to " + text + ".");
	}

	#region Chat commands

	/// <summary>.rapidpos</summary>
	internal static void RapidPositionCommand(CommandContext command)
	{
		RapidPosUpdate = !RapidPosUpdate;
		ClientChat.Info("Rapid position updates " + (RapidPosUpdate ? "en" : "dis") + "abled.");
	}

	/// <summary>.noclip / .nc</summary>
	internal static void NoclipCommand(CommandContext command)
	{
		Enabled = !Enabled;
		ClientChat.Info("Noclip " + (Enabled ? "En" : "Dis") + "abled");
		if (Main.netMode != 0)
		{
			NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
			NetMessage.SendData(50, -1, -1, null, Main.myPlayer);
		}
	}

	/// <summary>.ncspeed / .noclipspeed</summary>
	internal static void NoclipSpeedCommand(CommandContext command)
	{
		CycleSpeed();
	}

	#endregion
}
