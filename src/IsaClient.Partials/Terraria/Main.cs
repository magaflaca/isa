using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Chat;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.Graphics.Capture;
using Terraria.UI.Chat;
using IsaClient.Building;
using IsaClient.Community;
using IsaClient.Core;
using IsaClient.Menu;
using IsaClient.Movement;
using IsaClient.Music;
using IsaClient.Rendering;
using IsaClient.WorldTools;

namespace Terraria;

public partial class Main
{
	/// <summary>Set while the Item Editor's preview draws projectiles (their batch keeps the stage's transform).</summary>
	public static Matrix? IsaTransform;

	public static bool hasFocus => instance != null && instance.IsActive;

	public static int textureMaxWidth = 2000;

	public static int textureMaxHeight = 1800;

	/// <summary>The item behind custom item-style tooltips (IsaClient: its name never gets the Show IDs suffix).</summary>
	internal static Item MouseTextFakeItem => _mouseTextFakeItem;

	public static event Action OnTickForRunningModUpdates;

	public static bool grabbingMap;

	/// <summary>
	/// A typed line with <paramref name="links"/> (chat pictures) added at its end: the text is translated when that is on,
	/// the links never are.
	/// </summary>
	public static void SubmitChatLine(string line, string links)
	{
		if (string.IsNullOrEmpty(links))
		{
			SubmitChatLine(line);
		}
		else if (string.IsNullOrWhiteSpace(line))
		{
			SendTypedChat(links);
		}
		else
		{
			IsaClient.Community.ChatTranslate.Send(line, translated => SendTypedChat(translated + " " + links));
		}
	}

	/// <summary>A line typed in a chat: client commands run, anything else is sent (plugin chats use it too).</summary>
	public static void SubmitChatLine(string line)
	{
		// A server with IsaClient off (IsaClientModes plugin): the line goes as the game would send it.
		if (IsaClient.Networking.ServerModes.ClientBlocked)
		{
			if (line != "" && !IsaClient.Networking.ServerModes.BlocksTypedCommand(line))
			{
				SendPlainChat(line);
			}
			return;
		}
		// Plugin chats (TerraAngel's) in Plugin Maker mode.
		if (IsaClient.Assistant.PluginMaker.PluginAgent.TryChatMode(line, withPictures: false))
		{
			return;
		}
		line = ChatCommands.ExpandAbbreviation(line);
		if (IsaClient.Networking.ServerModes.BlocksTypedCommand(line))
		{
			return;
		}
		if (line != "" && !MusicCommands.TryHandleTyped(line) && !MapReveal.TryHandle(line) && !ChatCommands.TryHandle(line) && !ChatManager.DebugCommands.Process((byte)myPlayer, line) && !IsaClient.MultiClient.MultiClientManager.InterceptChat(line) && !IsaClient.Community.PipeChat.TryIntercept(line))
		{
			IsaClient.Community.ChatTranslate.Send(line, SendTypedChat);
		}
	}

	/// <summary>A chat line exactly as the vanilla game sends it (no client colors, translation or secret chat).</summary>
	private static void SendPlainChat(string text)
	{
		ChatMessage message = ChatManager.Commands.CreateOutgoingMessage(text);
		if (netMode == 1)
		{
			ChatHelper.SendChatMessageFromClient(message);
		}
		else if (netMode == 0)
		{
			ChatManager.Commands.ProcessIncomingMessage(message, myPlayer);
		}
	}

	/// <summary>A typed chat line (already translated when that is on), colored and sent.</summary>
	private static void SendTypedChat(string text)
	{
		if (IsaClient.Community.SecretChat.TrySendTyped(text))
		{
			return;
		}
		ChatMessage message = ChatManager.Commands.CreateOutgoingMessage(IsaClient.Community.ChatColor.ApplyToOutgoing(text));
		if (netMode == 1)
		{
			ChatHelper.SendChatMessageFromClient(message);
		}
		else if (netMode == 0)
		{
			ChatManager.Commands.ProcessIncomingMessage(message, myPlayer);
		}
	}

	/// <summary>Sends text to the chat as it is (no client commands, colors or translation).</summary>
	public static void SendRawChat(string text)
	{
		ChatMessage message = ChatManager.Commands.CreateOutgoingMessage(text);
		if (netMode == 1)
		{
			ChatHelper.SendChatMessageFromClient(message);
		}
		else if (netMode == 0)
		{
			ChatManager.Commands.ProcessIncomingMessage(message, myPlayer);
		}
	}

	/// <summary>IsaClient (spectate with inventory): runs a HUD draw with the spectated player in your slot, read-only.</summary>
	private static void DrawAsSpectatedHud(Action draw)
	{
		if (!Spectate.BeginSpectatedHud(out var swap))
		{
			draw();
			return;
		}
		try
		{
			draw();
		}
		finally
		{
			Spectate.EndSpectatedHud(ref swap);
		}
	}

	private static void DrawCustomUI_35_1_NewCheatPanel()
	{
		CheatPanel.MousedOver = false;
		if (gameMenu || !InputState.TildeTerminal || !MenuState.UiRework)
		{
			CheatPanel.DraggingSlider = false;
			CheatPanel.DraggingScrollbar = false;
			return;
		}
		spriteBatch.End();
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerStateForCursor, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, UIScaleMatrix);
		// A music window on top of the panel has the mouse: the panel must not react to it (hover, clicks, scroll).
		bool hideMouse = IsaClient.Music.MusicPlayerWindow.MouseOwnedByMusic;
		int realMouseX = mouseX;
		int realMouseY = mouseY;
		if (hideMouse)
		{
			mouseX = -100000;
			mouseY = -100000;
		}
		if (IsaClient.Hub.ClientHub.TakesPanel)
		{
			// The new panel draws in place of the classic one (the inventory viewer and My Character still use CustomUI2).
			IsaClient.Hub.ClientHub.Draw(spriteBatch, _drawInterfaceGameTime, !hideMouse);
		}
		else
		{
			MenuState.CustomUI2.Update(_drawInterfaceGameTime);
			MenuState.CustomUI2.Draw(spriteBatch, _drawInterfaceGameTime);
		}
		if (hideMouse)
		{
			mouseX = realMouseX;
			mouseY = realMouseY;
		}
		if (!MenuState.CustomUIMousedOver && !PlayerInput.Triggers.Current.MouseLeft)
		{
			CheatPanel.DraggingSlider = false;
			CheatPanel.DraggingScrollbar = false;
		}
	}

	protected void DrawCustomUI_8_CopySelectionMode()
	{
		if (gameMenu || !Selection.CopyMode)
		{
			return;
		}
		int num = Math.Min(Selection.CopyStartA.X, Selection.CopyStartB.X);
		int num2 = Math.Min(Selection.CopyStartA.Y, Selection.CopyStartB.Y);
		int num3 = Math.Abs(Selection.CopyStartA.X - Selection.CopyStartB.X);
		int num4 = Math.Abs(Selection.CopyStartA.Y - Selection.CopyStartB.Y);
		if (!mapFullscreen)
		{
			if (!LocalPlayer.mouseInterface || mapFullscreen)
			{
				instance.MouseText("Copy Selection Mode", 0, 0);
			}
			Microsoft.Xna.Framework.Rectangle val = Main.ReverseGravitySupport(new Microsoft.Xna.Framework.Rectangle(num * 16, num2 * 16, (num3 + 1) * 16, (num4 + 1) * 16));
			Microsoft.Xna.Framework.Rectangle val2 = Main.ReverseGravitySupport(new Microsoft.Xna.Framework.Rectangle((int)screenPosition.X, (int)screenPosition.Y, screenWidth + 1, screenHeight + 1));
			Microsoft.Xna.Framework.Rectangle val3 = default(Microsoft.Xna.Framework.Rectangle);
			Microsoft.Xna.Framework.Rectangle.Intersect(ref val2, ref val, out val3);
			if (val3.Width != 0 && val3.Height != 0)
			{
				val3.Offset(-val2.X, -val2.Y);
				spriteBatch.Draw(TextureAssets.MagicPixel.Value, val3, CaptureInterface.Settings.MarkedAreaColor);
				for (int i = 0; i < 2; i++)
				{
					spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Microsoft.Xna.Framework.Rectangle(val3.X, val3.Y + ((i == 1) ? val3.Height : (-2)), val3.Width, 2), Microsoft.Xna.Framework.Color.White);
					spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Microsoft.Xna.Framework.Rectangle(val3.X + ((i == 1) ? val3.Width : (-2)), val3.Y, 2, val3.Height), Microsoft.Xna.Framework.Color.White);
				}
			}
			return;
		}
		instance.MouseText("Copy Selection Mode", 0, 0);
		TeleportTools.TryGetMapCoordinatesV2(num, num2, 1, out var result);
		TeleportTools.TryGetMapCoordinatesV2(num + num3 + 1, num2 + num4 + 1, 1, out var result2);
		Microsoft.Xna.Framework.Rectangle val4 = new Microsoft.Xna.Framework.Rectangle(result.X, result.Y, result2.X - result.X, result2.Y - result.Y);
		Microsoft.Xna.Framework.Rectangle val5 = new Microsoft.Xna.Framework.Rectangle(0, 0, screenWidth + 1, screenHeight + 1);
		Microsoft.Xna.Framework.Rectangle val6 = default(Microsoft.Xna.Framework.Rectangle);
		Microsoft.Xna.Framework.Rectangle.Intersect(ref val5, ref val4, out val6);
		if (val6.Width != 0 && val6.Height != 0)
		{
			val6.Offset(-val5.X, -val5.Y);
			spriteBatch.Draw(TextureAssets.MagicPixel.Value, val6, CaptureInterface.Settings.MarkedAreaColor);
			for (int j = 0; j < 2; j++)
			{
				spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Microsoft.Xna.Framework.Rectangle(val6.X, val6.Y + ((j == 1) ? val6.Height : (-2)), val6.Width, 2), Microsoft.Xna.Framework.Color.White);
				spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Microsoft.Xna.Framework.Rectangle(val6.X + ((j == 1) ? val6.Width : (-2)), val6.Y, 2, val6.Height), Microsoft.Xna.Framework.Color.White);
			}
		}
	}

	protected void DrawCustomUI_11_FillOrDelSelectionMode()
	{
		if (gameMenu || !Selection.SelectionMode)
		{
			return;
		}
		int num = Math.Min(Selection.SelectionA.X, Selection.SelectionB.X);
		int num2 = Math.Min(Selection.SelectionA.Y, Selection.SelectionB.Y);
		int num3 = Math.Abs(Selection.SelectionA.X - Selection.SelectionB.X);
		int num4 = Math.Abs(Selection.SelectionA.Y - Selection.SelectionB.Y);
		if (!mapFullscreen)
		{
			if (!LocalPlayer.mouseInterface)
			{
				instance.MouseText(Selection.SelectionModeString(), 0, 0);
			}
			Microsoft.Xna.Framework.Rectangle val = Main.ReverseGravitySupport(new Microsoft.Xna.Framework.Rectangle(num * 16, num2 * 16, (num3 + 1) * 16, (num4 + 1) * 16));
			Microsoft.Xna.Framework.Rectangle val2 = Main.ReverseGravitySupport(new Microsoft.Xna.Framework.Rectangle((int)screenPosition.X, (int)screenPosition.Y, screenWidth + 1, screenHeight + 1));
			Microsoft.Xna.Framework.Rectangle val3 = default(Microsoft.Xna.Framework.Rectangle);
			Microsoft.Xna.Framework.Rectangle.Intersect(ref val2, ref val, out val3);
			if (val3.Width != 0 && val3.Height != 0)
			{
				val3.Offset(-val2.X, -val2.Y);
				spriteBatch.Draw(TextureAssets.MagicPixel.Value, val3, CaptureInterface.Settings.MarkedAreaColor);
				for (int i = 0; i < 2; i++)
				{
					spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Microsoft.Xna.Framework.Rectangle(val3.X, val3.Y + ((i == 1) ? val3.Height : (-2)), val3.Width, 2), Microsoft.Xna.Framework.Color.White);
					spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Microsoft.Xna.Framework.Rectangle(val3.X + ((i == 1) ? val3.Width : (-2)), val3.Y, 2, val3.Height), Microsoft.Xna.Framework.Color.White);
				}
			}
			return;
		}
		instance.MouseText(Selection.SelectionModeString(), 0, 0);
		TeleportTools.TryGetMapCoordinatesV2(num, num2, 1, out var result);
		TeleportTools.TryGetMapCoordinatesV2(num + num3 + 1, num2 + num4 + 1, 1, out var result2);
		Microsoft.Xna.Framework.Rectangle val4 = new Microsoft.Xna.Framework.Rectangle(result.X, result.Y, result2.X - result.X, result2.Y - result.Y);
		Microsoft.Xna.Framework.Rectangle val5 = new Microsoft.Xna.Framework.Rectangle(0, 0, screenWidth + 1, screenHeight + 1);
		Microsoft.Xna.Framework.Rectangle val6 = default(Microsoft.Xna.Framework.Rectangle);
		Microsoft.Xna.Framework.Rectangle.Intersect(ref val5, ref val4, out val6);
		if (val6.Width != 0 && val6.Height != 0)
		{
			val6.Offset(-val5.X, -val5.Y);
			spriteBatch.Draw(TextureAssets.MagicPixel.Value, val6, CaptureInterface.Settings.MarkedAreaColor);
			for (int j = 0; j < 2; j++)
			{
				spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Microsoft.Xna.Framework.Rectangle(val6.X, val6.Y + ((j == 1) ? val6.Height : (-2)), val6.Width, 2), Microsoft.Xna.Framework.Color.White);
				spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Microsoft.Xna.Framework.Rectangle(val6.X + ((j == 1) ? val6.Width : (-2)), val6.Y, 2, val6.Height), Microsoft.Xna.Framework.Color.White);
			}
		}
	}

	private static void UpdateClientModules()
	{
		if (!dedServ && Main.OnTickForRunningModUpdates != null)
		{
			Main.OnTickForRunningModUpdates();
		}
		if (!dedServ)
		{
			IsaClient.Music.MusicPlayerWindow.Update();
			IsaClient.Video.VideoPlayer.Update();
			IsaClient.ChatImages.ChatImages.Update();
			IsaClient.Music.MediaFocus.Update();
			try
			{
				IsaClient.Net.Pipe.PipeNet.Update();
			}
			catch (Exception pipeEx)
			{
				IsaClient.Core.ClientLog.LogException(pipeEx);
			}
			try
			{
				IsaClient.Games.GameLobby.Update();
				IsaClient.Community.PipeChat.Update();
				IsaClient.Items.ItemShare.Update();
				IsaClient.Sharing.ShareChat.Update();
				IsaClient.Items.ItemKeeper.Update();
				IsaClient.Items.CreatorNames.Update();
			}
			catch (Exception gamesEx)
			{
				IsaClient.Core.ClientLog.LogException(gamesEx);
			}
			IsaClient.Hub.ClientHub.Update();
			IsaClient.Menu.ProxyMenu.Update();
			IsaClient.Menu.BotsMenu.Update();
		}
	}

	private static void AddClientTooltipLines(Item item, ref int numLines, string[] toolTipLine, Microsoft.Xna.Framework.Color[] lineColors)
	{
		if (item.modifiedByServer)
		{
			toolTipLine[numLines] = "Modified by Server";
			numLines++;
		}
		if (item.modifiedByItemMods && item.isaMark == null)
		{
			toolTipLine[numLines] = "Modified By Preset: " + item.itemModPresetName;
			numLines++;
		}
		IsaClient.Items.ItemTooltip.Add(item, toolTipLine, lineColors, ref numLines);
		InfoOverlays.AddExtraItemInfoLines(item, toolTipLine, ref numLines);
	}

	private static void ShowTileDataUnderMouse()
	{
		Microsoft.Xna.Framework.Point mouseTileForData = new Microsoft.Xna.Framework.Point(Player.tileTargetX, Player.tileTargetY);
		if (InfoOverlays.SeeTileData && !LocalPlayer.mouseInterface && WorldGen.InWorld(mouseTileForData.X, mouseTileForData.Y) && tile[mouseTileForData.X, mouseTileForData.Y] != null)
		{
			MenuState.CustomTextOverride = InfoOverlays.TileDataString(tile[mouseTileForData.X, mouseTileForData.Y]);
			if (MenuState.CustomTextOverride != "")
			{
				instance.MouseText("", 0, 0);
				LocalPlayer.cursorItemIconEnabled = false;
				LocalPlayer.cursorItemIconID = -1;
			}
		}
	}
}
