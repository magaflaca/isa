using System;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;
using IsaClient.Community;
using IsaClient.Core;
using IsaClient.Networking;

namespace IsaClient.Movement;

/// <summary>Teleports: to the mouse, from the map, to coordinates and to spawn, plus teleport request bypass.</summary>
public static class TeleportTools
{
	/// <summary>Teleports the local player so its feet are on the given tile.</summary>
	public static void TeleportToTile(int tileX, int tileY)
	{
		Player player = Main.LocalPlayer;
		player.position = new Vector2((float)tileX * 16f - (float)(player.width / 2) + 8f, (float)tileY * 16f - (float)player.height);
		player.velocity = Vector2.Zero;
		player.fallStart = (int)player.position.Y / 16;
		player.oldPosition = player.position;
		LastCenter = player.Center;
		player.noFallDmg = true;
		if (Main.netMode != 0)
		{
			NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
		}
	}

	[Setting("ClientSideTeleport", Remembered = true)]
	public static bool BypassTeleportRequests;

	private static bool _teleportBypassInfoShown;

	/// <summary>After a teleport done locally because of "Bypass Teleport Requests": tell the server where we are.</summary>
	public static void AfterBypassedTeleport()
	{
		if (Main.netMode == 1 && BypassTeleportRequests)
		{
			NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
			if (!_teleportBypassInfoShown)
			{
				_teleportBypassInfoShown = true;
				ClientChat.Notice("Teleported without asking the server. Potions and conches work best with the map revealed (.revealmap).");
			}
		}
	}

	public const string MapTpKeyName = "Map TP";

	[Setting("MouseTeleport", Remembered = true)]
	public static bool GotoMouse;

	[Setting("MapTeleport", Remembered = true)]
	public static bool MapTeleportation;

	public static void FakeNetMessage13(Vector2 plrPos, int heldItem)
	{
		if (Main.netMode == 0)
		{
			return;
		}
		int num = 256;
		int num2 = 13;
		int whoAmI = Main.LocalPlayer.whoAmI;
		lock (NetMessage.buffer[num])
		{
			BinaryWriter writer = NetMessage.buffer[num].writer;
			if (writer == null)
			{
				NetMessage.buffer[num].ResetWriter();
				writer = NetMessage.buffer[num].writer;
			}
			writer.BaseStream.Position = 0L;
			long position = writer.BaseStream.Position;
			writer.BaseStream.Position += 2L;
			writer.Write((byte)num2);
			Player player = Main.player[whoAmI];
			writer.Write((byte)whoAmI);
			BitsByte bitsByte = (byte)0;
			bitsByte[0] = player.controlUp;
			bitsByte[1] = player.controlDown;
			bitsByte[2] = player.controlLeft;
			bitsByte[3] = player.controlRight;
			bitsByte[4] = player.controlJump;
			bitsByte[5] = player.controlUseItem;
			bitsByte[6] = player.direction == 1;
			bitsByte[7] = player.controlDash;
			writer.Write(bitsByte);
			BitsByte bitsByte2 = (byte)0;
			bitsByte2[0] = player.pulley;
			bitsByte2[1] = player.pulley && player.pulleyDir == 2;
			bitsByte2[2] = false;
			bitsByte2[3] = player.vortexStealthActive;
			bitsByte2[4] = player.gravDir == 1f;
			bitsByte2[5] = player.shieldRaised;
			bitsByte2[6] = player.ghost;
			bitsByte2[7] = player.mount.Active;
			writer.Write(bitsByte2);
			BitsByte bitsByte3 = (byte)0;
			bitsByte3[0] = player.tryKeepingHoveringUp;
			bitsByte3[1] = player.IsVoidVaultEnabled;
			bitsByte3[2] = player.sitting.isSitting;
			bitsByte3[3] = player.downedDD2EventAnyDifficulty;
			bitsByte3[4] = player.petting.isPetting;
			bitsByte3[5] = player.petting.isPetSmall;
			bitsByte3[6] = player.PotionOfReturnOriginalUsePosition.HasValue;
			bitsByte3[7] = player.tryKeepingHoveringDown;
			writer.Write(bitsByte3);
			BitsByte bitsByte4 = (byte)0;
			bitsByte4[0] = player.sleeping.isSleeping;
			bitsByte4[1] = player.autoReuseAllWeapons;
			bitsByte4[2] = player.controlDownHold;
			bitsByte4[3] = player.isOperatingAnotherEntity;
			bitsByte4[4] = player.controlUseTile;
			bitsByte4[5] = player.netCameraTarget.HasValue;
			bitsByte4[6] = player.lastItemUseAttemptSuccess;
			bitsByte4[7] = player.accSnappingStoneLightUp;
			writer.Write(bitsByte4);
			writer.Write((byte)heldItem);
			writer.WriteVector2(plrPos);
			if (bitsByte2[7])
			{
				writer.Write((ushort)player.mount.Type);
			}
			if (bitsByte3[6])
			{
				writer.WriteVector2(player.PotionOfReturnOriginalUsePosition.Value);
				writer.WriteVector2(player.PotionOfReturnHomePosition.Value);
			}
			if (bitsByte4[5])
			{
				writer.WriteVector2(player.netCameraTarget.Value);
			}
			int num3 = (int)writer.BaseStream.Position;
			if (num3 > 65535)
			{
				throw new Exception("Maximum packet length exceeded. id: " + num2 + " length: " + num3);
			}
			writer.BaseStream.Position = position;
			writer.Write((ushort)num3);
			writer.BaseStream.Position = num3;
			if (Main.netMode == 1 && IsaClient.Building.PasteJob.Route(NetMessage.buffer[num].writeBuffer, 0, num3))
			{
			}
			else if (Main.netMode == 1 && Netplay.Connection.Socket.IsConnected())
			{
				try
				{
					NetMessage.buffer[num].spamCount++;
					PacketPacing.QueuedPackets++;
					Main.ActiveNetDiagnosticsUI.CountSentMessage(num2, num3);
					Netplay.Connection.Socket.AsyncSend(NetMessage.buffer[num].writeBuffer, 0, num3, Netplay.Connection.ClientWriteCallBack);
				}
				catch
				{
				}
			}
			NetMessage.buffer[num].writeLocked = false;
		}
	}

	public static Vector2 LastCenter
	{
		get
		{
			return new Vector2(Main.LocalPlayer.oldPosition.X + (float)(Main.LocalPlayer.width / 2), Main.LocalPlayer.oldPosition.Y + (float)(Main.LocalPlayer.height / 2));
		}
		set
		{
			Main.LocalPlayer.oldPosition = new Vector2(value.X - (float)(Main.LocalPlayer.width / 2), value.Y - (float)(Main.LocalPlayer.height / 2));
		}
	}

	internal static bool TryGetMapCoordinates(int PinX, int PinY, out Point result)
	{
		if (!Main.mapFullscreen)
		{
			result = new Point(-1, -1);
			return false;
		}
		_ = Main.maxTilesX / Main.textureMaxWidth;
		_ = Main.maxTilesY / Main.textureMaxHeight;
		float num = 10f;
		float num2 = 10f;
		float num3 = Main.maxTilesX - 10;
		float num4 = Main.maxTilesY - 10;
		float mapFullscreenScale = Main.mapFullscreenScale;
		float num5 = (float)Main.screenWidth / (float)Main.maxTilesX * 0.8f;
		if (Main.mapFullscreenScale < num5)
		{
			Main.mapFullscreenScale = num5;
		}
		if (Main.mapFullscreenScale > 16f)
		{
			Main.mapFullscreenScale = 16f;
		}
		mapFullscreenScale = Main.mapFullscreenScale;
		if (Main.mapFullscreenPos.X < num)
		{
			Main.mapFullscreenPos.X = num;
		}
		if (Main.mapFullscreenPos.X > num3)
		{
			Main.mapFullscreenPos.X = num3;
		}
		if (Main.mapFullscreenPos.Y < num2)
		{
			Main.mapFullscreenPos.Y = num2;
		}
		if (Main.mapFullscreenPos.Y > num4)
		{
			Main.mapFullscreenPos.Y = num4;
		}
		float x = Main.mapFullscreenPos.X;
		float y = Main.mapFullscreenPos.Y;
		float num6 = x * mapFullscreenScale;
		y *= mapFullscreenScale;
		float num7 = 0f - num6 + (float)(Main.screenWidth / 2);
		float num8 = 0f - y + (float)(Main.screenHeight / 2);
		num7 += num * mapFullscreenScale;
		num8 += num2 * mapFullscreenScale;
		float num9 = Main.maxTilesX / 840;
		num9 *= Main.mapFullscreenScale;
		float num10 = num7;
		float num11 = num8;
		float num12 = TextureAssets.Map.Width();
		float num13 = TextureAssets.Map.Height();
		if (Main.maxTilesX == 8400)
		{
			num9 *= 0.999f;
			num10 -= 40.6f * num9;
			num11 = num8 - 5f * num9;
			num12 -= 8.045f;
			num12 *= num9;
			num13 += 0.12f;
			num13 *= num9;
			if ((double)num9 < 1.2)
			{
				num13 += 1f;
			}
		}
		else if (Main.maxTilesX == 6400)
		{
			num9 *= 1.09f;
			num10 -= 38.8f * num9;
			num11 = num8 - 3.85f * num9;
			num12 -= 13.6f;
			num12 *= num9;
			num13 -= 6.92f;
			num13 *= num9;
			if ((double)num9 < 1.2)
			{
				num13 += 2f;
			}
		}
		else if (Main.maxTilesX == 6300)
		{
			num9 *= 1.09f;
			num10 -= 39.8f * num9;
			num11 = num8 - 4.08f * num9;
			num12 -= 26.69f;
			num12 *= num9;
			num13 -= 6.92f;
			num13 *= num9;
			if ((double)num9 < 1.2)
			{
				num13 += 2f;
			}
		}
		else if (Main.maxTilesX == 4200)
		{
			num9 *= 0.998f;
			num10 -= 37.3f * num9;
			num11 -= 1.7f * num9;
			num12 -= 16f;
			num12 *= num9;
			num13 -= 8.31f;
			num13 *= num9;
		}
		int num14 = (int)((0f - num7 + (float)PinX) / mapFullscreenScale + num);
		int num15 = (int)((0f - num8 + (float)PinY) / mapFullscreenScale + num2);
		bool flag = false;
		if ((float)num14 < num)
		{
			flag = true;
		}
		if ((float)num14 >= num3)
		{
			flag = true;
		}
		if ((float)num15 < num2)
		{
			flag = true;
		}
		if ((float)num15 >= num4)
		{
			flag = true;
		}
		if (!flag)
		{
			result = new Point(num14, num15);
			return true;
		}
		result = new Point(-1, -1);
		return false;
	}

	public static bool TryGetMapCoordinatesV2(int PinX, int PinY, int Goal, out Point result)
	{
		if (!Main.mapFullscreen)
		{
			result = new Point(-1, -1);
			return false;
		}
		_ = Main.maxTilesX / Main.textureMaxWidth;
		_ = Main.maxTilesY / Main.textureMaxHeight;
		float num = 10f;
		float num2 = 10f;
		float num3 = Main.maxTilesX - 10;
		float num4 = Main.maxTilesY - 10;
		float mapFullscreenScale = Main.mapFullscreenScale;
		float num5 = (float)Main.screenWidth / (float)Main.maxTilesX * 0.8f;
		if (Main.mapFullscreenScale < num5)
		{
			Main.mapFullscreenScale = num5;
		}
		if (Main.mapFullscreenScale > 16f)
		{
			Main.mapFullscreenScale = 16f;
		}
		mapFullscreenScale = Main.mapFullscreenScale;
		if (Main.mapFullscreenPos.X < num)
		{
			Main.mapFullscreenPos.X = num;
		}
		if (Main.mapFullscreenPos.X > num3)
		{
			Main.mapFullscreenPos.X = num3;
		}
		if (Main.mapFullscreenPos.Y < num2)
		{
			Main.mapFullscreenPos.Y = num2;
		}
		if (Main.mapFullscreenPos.Y > num4)
		{
			Main.mapFullscreenPos.Y = num4;
		}
		float x = Main.mapFullscreenPos.X;
		float y = Main.mapFullscreenPos.Y;
		float num6 = x * mapFullscreenScale;
		y *= mapFullscreenScale;
		float num7 = 0f - num6 + (float)(Main.screenWidth / 2);
		float num8 = 0f - y + (float)(Main.screenHeight / 2);
		num7 += num * mapFullscreenScale;
		num8 += num2 * mapFullscreenScale;
		float num9 = Main.maxTilesX / 840;
		num9 *= Main.mapFullscreenScale;
		float num10 = num7;
		float num11 = num8;
		float num12 = TextureAssets.Map.Width();
		float num13 = TextureAssets.Map.Height();
		if (Main.maxTilesX == 8400)
		{
			num9 *= 0.999f;
			num10 -= 40.6f * num9;
			num11 = num8 - 5f * num9;
			num12 -= 8.045f;
			num12 *= num9;
			num13 += 0.12f;
			num13 *= num9;
			if ((double)num9 < 1.2)
			{
				num13 += 1f;
			}
		}
		else if (Main.maxTilesX == 6400)
		{
			num9 *= 1.09f;
			num10 -= 38.8f * num9;
			num11 = num8 - 3.85f * num9;
			num12 -= 13.6f;
			num12 *= num9;
			num13 -= 6.92f;
			num13 *= num9;
			if ((double)num9 < 1.2)
			{
				num13 += 2f;
			}
		}
		else if (Main.maxTilesX == 6300)
		{
			num9 *= 1.09f;
			num10 -= 39.8f * num9;
			num11 = num8 - 4.08f * num9;
			num12 -= 26.69f;
			num12 *= num9;
			num13 -= 6.92f;
			num13 *= num9;
			if ((double)num9 < 1.2)
			{
				num13 += 2f;
			}
		}
		else if (Main.maxTilesX == 4200)
		{
			num9 *= 0.998f;
			num10 -= 37.3f * num9;
			num11 -= 1.7f * num9;
			num12 -= 16f;
			num12 *= num9;
			num13 -= 8.31f;
			num13 *= num9;
		}
		switch (Goal)
		{
		case 0:
		{
			int num14 = (int)((0f - num7 + (float)PinX) / mapFullscreenScale + num);
			int num15 = (int)((0f - num8 + (float)PinY) / mapFullscreenScale + num2);
			bool flag = false;
			if ((float)num14 < num)
			{
				flag = true;
			}
			if ((float)num14 >= num3)
			{
				flag = true;
			}
			if ((float)num15 < num2)
			{
				flag = true;
			}
			if ((float)num15 >= num4)
			{
				flag = true;
			}
			if (!flag)
			{
				result = new Point(num14, num15);
				return true;
			}
			result = new Point(-1, -1);
			return false;
		}
		case 1:
		{
			Vector2 val = default(Vector2);
			val = new Vector2(num7, num8);
			Vector2 val2 = new Vector2((float)PinX, (float)PinY) * mapFullscreenScale - new Vector2(10f * mapFullscreenScale);
			result = (val + val2).ToPoint();
			return true;
		}
		default:
			result = new Point(-1, -1);
			return false;
		}
	}

	public static void MouseTP(Vector2 pos)
	{
		if (Noclip.Enabled)
		{
			LastCenter = pos;
		}
		else
		{
			Main.LocalPlayer.Center = pos;
		}
		if (Main.netMode != 0)
		{
			NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
		}
		Main.LocalPlayer.fallStart = (int)(Main.LocalPlayer.position.Y / 16f);
	}

	#region Chat commands

	/// <summary>.spawn</summary>
	internal static void SpawnCommand(CommandContext command)
	{
		TeleportToTile(Main.spawnTileX, Main.spawnTileY);
		ClientChat.Info("Teleported to spawn.");
	}

	/// <summary>.tpbypass / .bypasstp</summary>
	internal static void TeleportBypassCommand(CommandContext command)
	{
		BypassTeleportRequests = !BypassTeleportRequests;
		ClientChat.Info("Teleportation Bypass " + (BypassTeleportRequests ? "en" : "dis") + "abled.");
	}

	/// <summary>.tp</summary>
	internal static void TeleportCommand(CommandContext command)
	{
		if (command.Parts.Length < 2 || command.Rest.Trim().Length == 0)
		{
			ClientChat.Error("Usage: .tp <x> <y>  or  .tp <player>");
			return;
		}
		string first = command.Parts[1];
		if (first != "*" && !int.TryParse(first, out _))
		{
			string name = command.Rest.Trim().Trim('"');
			Player target = ChatTools.FindPlayer(name, out string error);
			if (target == null)
			{
				ClientChat.Error(error);
				return;
			}
			MoveLocalPlayer(target.BottomLeft + new Vector2((target.width - command.Player.width) / 2f, 0f));
			ClientChat.Info("Teleported to " + target.name + ".");
			return;
		}
		int tileX = (int)command.Player.Bottom.X / 16;
		int tileY = (int)command.Player.Bottom.Y / 16;
		if (first != "*" && !int.TryParse(first, out tileX))
		{
			tileX = (int)command.Player.Bottom.X / 16;
		}
		if (command.Parts.Length > 2 && command.Parts[2] != "*" && !int.TryParse(command.Parts[2], out tileY))
		{
			ClientChat.Error("Invalid Y position.");
			tileY = (int)command.Player.Bottom.Y / 16;
		}
		MoveLocalPlayer(new Vector2((float)tileX * 16f - (float)(command.Player.width / 2) + 8f, (float)tileY * 16f));
		ClientChat.Info($"Teleported to {tileX}, {tileY}");
	}

	/// <summary>Moves you so your feet are at <paramref name="bottomLeft"/> (no fall damage) and tells the server.</summary>
	private static void MoveLocalPlayer(Vector2 bottomLeft)
	{
		Player player = Main.LocalPlayer;
		player.BottomLeft = bottomLeft;
		player.oldPosition = player.position;
		player.fallStart = (int)player.position.Y;
		LastCenter = player.Center;
		player.noFallDmg = true;
		NetMessage.SendData(13, -1, -1, null, Main.myPlayer);
	}

	/// <summary>.tpm / .mousetp / .gotomouse</summary>
	internal static void GoToMouseCommand(CommandContext command)
	{
		GotoMouse = !GotoMouse;
		ClientChat.Info("Mouse Teleportation " + (GotoMouse ? "En" : "Dis") + "abled");
	}

	/// <summary>.maptp / .mapteleport</summary>
	internal static void MapTeleportCommand(CommandContext command)
	{
		MapTeleportation = !MapTeleportation;
		ClientChat.Info("Map Teleportation " + (MapTeleportation ? "En" : "Dis") + "abled");
	}

	#endregion
}
