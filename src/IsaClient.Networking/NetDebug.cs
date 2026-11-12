using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using IsaClient.Building;
using IsaClient.Community;
using IsaClient.Core;
using IsaClient.Movement;
using IsaClient.WorldTools;

namespace IsaClient.Networking;

/// <summary>Netplay information on screen and debug names of packets.</summary>
public static class NetDebug
{
	[Setting("DisplayNetText", Remembered = true)]
	public static bool ShowNetplayText;

	[Setting("RandomUuid", Remembered = true)]
	public static bool RandomUUID;

	private static readonly Color _netplayRed = new Color(255, 10, 10);

	/// <summary>Netplay information at the top of the screen.</summary>
	public static void DrawNetplayText(SpriteBatch spriteBatch)
	{
		if (Main.gameMenu)
		{
			return;
		}
		float y = 40f;
		float centerX = (float)Main.screenWidth / 2f;
		if (ShowNetplayText && Main.netMode == 1 && Netplay.Connection.IsActive)
		{
			string text = "TimeOutTimer: " + Netplay.Connection.TimeOutTimer;
			if (Netplay.Connection.TimeOutTimer > 600)
			{
				text += " - Server Unresponsive";
			}
			Utils.DrawBorderString(spriteBatch, IsaClient.Languages.L10n.T(text), new Vector2(centerX, y), (Netplay.Connection.TimeOutTimer > 600) ? _netplayRed : Color.White, 1f, 0.5f, 0.5f);
			y += 20f;
		}
		if (ShowNetplayText && Main.netMode != 0)
		{
			string text2 = "Server Type: " + (ServerDetection.OnTshockServer ? "TShock" : ((Main.netMode == 2) ? "Local Server (Hosting)" : "Vanilla"));
			if (ServerDetection.OnTshockServer && Main.ServerSideCharacter)
			{
				text2 += " | Using Server Side Characters (SSC)";
			}
			Utils.DrawBorderString(spriteBatch, IsaClient.Languages.L10n.T(text2), new Vector2(centerX, y), Color.White, 1f, 0.5f, 0.5f);
			y += 20f;
		}
		if (MapReveal.IsRevealing && Main.netMode == 1)
		{
			Utils.DrawBorderString(spriteBatch, IsaClient.Languages.L10n.T("Sections Remaining: " + MapReveal.CountUnloadedSections()), new Vector2(centerX, y), Color.White, 1f, 0.5f, 0.5f);
			y += 20f;
		}
		int count = BrushOperations.TilesToBrush.Count;
		if (count > 0 && Main.netMode != 0)
		{
			Utils.DrawBorderString(spriteBatch, IsaClient.Languages.L10n.T("Brush Packets in Queue: " + count), new Vector2(centerX, y), (PacketPacing.QueuedPackets >= 1500) ? Color.Red : Color.White, 1f, 0.5f, 0.5f);
		}
		if (FreeCamera.FreeCam)
		{
			Utils.DrawBorderStringBig(spriteBatch, IsaClient.Languages.L10n.T("Free Camera"), new Vector2(centerX, 120f), Color.White, 1f, 0.5f, 0.5f);
		}
		else if (Spectate.IsSpectating)
		{
			string name = Main.player[Spectate.SpectatePlayerTarget].name;
			Utils.DrawBorderStringBig(spriteBatch, IsaClient.Languages.L10n.T("Spectating " + ((name.Length < 60) ? name : ("Player Index " + Spectate.SpectatePlayerTarget))), new Vector2(centerX, 120f), Color.White, 1f, 0.5f, 0.5f);
		}
		if (MapTools.WorldDownload && Main.netMode == 1 && !string.IsNullOrEmpty(Main.statusText))
		{
			Utils.DrawBorderStringBig(spriteBatch, IsaClient.Languages.L10n.T(Main.statusText), new Vector2(centerX, 180f), Color.White, 1f, 0.5f, 0.5f);
		}
	}

	internal static readonly Dictionary<int, string> PlayerPacketNames = new Dictionary<int, string>
	{
		{ 5, "SyncEquipment" },
		{ 12, "PlayerSpawn" },
		{ 13, "PlayerControls" },
		{ 16, "PlayerLifeMana" },
		{ 35, "PlayerHeal" },
		{ 36, "SyncPlayerZone" },
		{ 40, "SyncTalkNPC" },
		{ 41, "ShotAnimationAndSound" },
		{ 42, "PlayerMana" },
		{ 43, "PlayerHealMana" },
		{ 45, "PlayerTeamUpdate" },
		{ 50, "PlayerBuffs" },
		{ 55, "AddPlayerBuff" },
		{ 58, "InstrumentSound" },
		{ 62, "PlayerDodge" },
		{ 66, "PlayerHealOther" },
		{ 117, "PlayerHurtV2" },
		{ 118, "PlayerDeathV2" }
	};

	#region Chat commands

	/// <summary>.netplay / .nettext</summary>
	internal static void NetplayCommand(CommandContext command)
	{
		ShowNetplayText = !ShowNetplayText;
		ClientChat.Info("Netplay text " + (ShowNetplayText ? "en" : "dis") + "abled.");
	}

	#endregion
}
