using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.Graphics;
using Terraria.UI.Chat;
using IsaClient.Core;

namespace IsaClient.Community;

/// <summary>Player List: the overlay and page of connected players, client badges (IsaClient / zMod), vitals and tracker.</summary>
public static class PlayerListPanel
{
	private static readonly Color ClientBadge = new Color(120, 200, 255);

	private static readonly Color ZModBadge = new Color(230, 150, 255);

	private static readonly Color TerraAngelBadge = new Color(255, 128, 77);

	/// <summary>When each player's last zone packet arrived (Main.GameUpdateCount), for .clients.</summary>
	private static readonly uint[] _lastZonePacket = new uint[256];

	/// <summary>MessageBuffer case 36 on the client: remembers zMod users (zone5 bit 1) until they leave.</summary>
	public static void OnZonePacket(int index)
	{
		if (index < 0 || index >= 255)
		{
			return;
		}
		Player player = Main.player[index];
		_lastZonePacket[index] = Main.GameUpdateCount;
		if (index != Main.myPlayer && player.zone5[1])
		{
			player.zModDetected = true;
		}
	}

	/// <summary>MessageBuffer case 13 on the client: TerraAngel users mark their controls packets (see Player.terraAngelDetected).</summary>
	public static void OnControlsPacket(int index)
	{
		if (index < 0 || index >= 255 || index == Main.myPlayer)
		{
			return;
		}
		Player player = Main.player[index];
		if (player.netCameraTarget.HasValue && player.netCameraTarget.Value == new Vector2(-114514f, -1919810f))
		{
			player.terraAngelDetected = true;
		}
	}

	/// <summary>MessageBuffer case 117 on the client: TerraAngel's older presence mark, a 1-damage hurt "by" NPC 203.</summary>
	public static void OnHurtPacket(int index, PlayerDeathReason reason, int damage)
	{
		if (index >= 0 && index < 255 && index != Main.myPlayer && damage == 1 && reason != null && reason.SourceNPCIndex == 203)
		{
			Main.player[index].terraAngelDetected = true;
		}
	}

	internal static void PrintClients()
	{
		ClientChat.Info("Clients detected (zMod announces itself when it joins, then every 15 s):");
		for (int i = 0; i < 255; i++)
		{
			Player player = Main.player[i];
			if (!player.active)
			{
				continue;
			}
			int kind = player.IsaClientKind;
			string client = (i == Main.myPlayer) ? "you" : (kind == 1) ? "IsaClient" : (kind == 2) ? "zMod" : (kind == 3) ? "TerraAngel" : "vanilla / unknown";
			string sync = (i == Main.myPlayer) ? "" : (_lastZonePacket[i] == 0) ? "  (no zone packet yet)" : ("  (last zone packet " + (Main.GameUpdateCount - _lastZonePacket[i]) / 60 + " s ago)");
			Main.NewText(IsaClient.Languages.L10n.T("  " + player.name + ": " + client + sync), (kind == 2) ? ZModBadge : (kind == 1) ? ClientBadge : (kind == 3) ? TerraAngelBadge : Color.White);
		}
	}

	public static void TogglePlayerList()
	{
		ShowPlayerList = !ShowPlayerList;
		ClientChat.Info("Player List " + (ShowPlayerList ? "shown" : "hidden") + " (.playerlist).");
	}

	private const int ListRowHeight = 24;

	private const int ListWidth = 250;

	private static bool PlayerListShown => ShowPlayerList && !Main.gameMenu && !Main.mapFullscreen && !Main.playerInventory;

	private static int PlayerListCount()
	{
		int count = 0;
		for (int i = 0; i < 255; i++)
		{
			if (Main.player[i].active)
			{
				count++;
			}
		}
		return count;
	}

	/// <summary>Top of the list panel: right where the info accessories (shellphone) would start, under the minimap.</summary>
	private static int PlayerListTop => ((Main.mapStyle == 1 && Main.mapEnabled) ? 229 : -32) + 74 + 52 - 6;

	/// <summary>How far the info accessories move down so they sit under the Player List (0 when it is hidden).</summary>
	public static int PlayerListPushDown
	{
		get
		{
			if (!PlayerListShown)
			{
				return 0;
			}
			int count = PlayerListCount();
			return (count == 0) ? 0 : (ListRowHeight * count + 34 + 8);
		}
	}

	/// <summary>Drawn as an interface layer (see Main draw hooks): map, then players, then the shellphone info.</summary>
	public static void DrawPlayerList(SpriteBatch spriteBatch)
	{
		if (!PlayerListShown)
		{
			return;
		}
		DynamicSpriteFont font = FontAssets.MouseText.Value;
		Texture2D pixel = TextureAssets.MagicPixel.Value;
		int rowHeight = ListRowHeight;
		int width = ListWidth;
		int count = PlayerListCount();
		if (count == 0)
		{
			return;
		}
		int x = Main.screenWidth - 272;
		int y = PlayerListTop + 26;
		int height = rowHeight * count + 34;
		spriteBatch.Draw(pixel, new Rectangle(x - 8, y - 26, width + 16, height), new Color(15, 20, 45) * 0.82f);
		spriteBatch.Draw(pixel, new Rectangle(x - 8, y - 26, width + 16, 2), new Color(120, 160, 255) * 0.6f);
		ChatManager.DrawColorCodedStringWithShadow(spriteBatch, font, IsaClient.Languages.L10n.T("Players (" + count + ")"), new Vector2(x, y - 22), new Color(200, 220, 255), 0f, Vector2.Zero, Vector2.One);
		int row = 0;
		for (int i = 0; i < 255; i++)
		{
			Player player = Main.player[i];
			if (!player.active)
			{
				continue;
			}
			float ry = y + 6 + row * rowHeight;
			int kind = player.IsaClientKind;
			if (kind != 0)
			{
				Color badge = kind == 1 ? ClientBadge : (kind == 2 ? ZModBadge : TerraAngelBadge);
				string letter = kind == 1 ? "I" : (kind == 2 ? "Z" : "T");
				spriteBatch.Draw(pixel, new Rectangle(x, (int)ry, 18, 18), badge * 0.30f);
				spriteBatch.Draw(pixel, new Rectangle(x, (int)ry, 18, 2), badge * 0.8f);
				spriteBatch.Draw(pixel, new Rectangle(x, (int)ry + 16, 18, 2), badge * 0.8f);
				ChatManager.DrawColorCodedStringWithShadow(spriteBatch, font, IsaClient.Languages.L10n.T(letter), new Vector2(x + 5, ry - 3), badge, 0f, Vector2.Zero, new Vector2(0.9f));
			}
			Color nameColor = (i == Main.myPlayer) ? new Color(255, 240, 150) : Color.White;
			if (player.hostile)
			{
				nameColor = Color.Lerp(nameColor, new Color(255, 120, 120), 0.5f);
			}
			string name = player.name ?? "?";
			if (name.Length > 18)
			{
				name = name.Substring(0, 17) + "…";
			}
			ChatManager.DrawColorCodedStringWithShadow(spriteBatch, font, IsaClient.Languages.L10n.T(name), new Vector2(x + 24, ry - 3), nameColor, 0f, Vector2.Zero, new Vector2(0.85f));
			// HP bar on the right.
			int barW = 60;
			int barX = x + width - barW;
			float hpFrac = (player.statLifeMax2 > 0) ? MathHelper.Clamp((float)player.statLife / player.statLifeMax2, 0f, 1f) : 0f;
			spriteBatch.Draw(pixel, new Rectangle(barX, (int)ry + 4, barW, 10), new Color(60, 20, 20) * 0.9f);
			spriteBatch.Draw(pixel, new Rectangle(barX, (int)ry + 4, (int)(barW * hpFrac), 10), Color.Lerp(new Color(220, 60, 60), new Color(80, 220, 90), hpFrac));
			if (i != Main.myPlayer)
			{
				int dist = (int)(Vector2.Distance(player.Center, Main.LocalPlayer.Center) / 16f);
				ChatManager.DrawColorCodedStringWithShadow(spriteBatch, font, IsaClient.Languages.L10n.T(dist + "m"), new Vector2(barX - 44, ry - 3), new Color(160, 175, 200), 0f, Vector2.Zero, new Vector2(0.7f));
			}
			row++;
		}
	}

	public static int PrevPlrCount;

	/// <summary>Broadcast to others that you are an IsaClient user (Player List "I" statue). Off = invisible to their list.</summary>
	[Setting("InformOtherClients")]
	public static bool ShareMyInfo = true;

	public static bool ShowPlayerList;

	[Setting("ShowPlayerVitals", Remembered = true)]
	public static bool PlayerVitalStatsUI;

	[Setting("TrackPlayers", Remembered = true)]
	public static bool PlayerTracker;

	/// <summary>Player vitals: HP, mana and defense under every player.</summary>
	public static void DrawPlayerVitals()
	{
		if (Main.gameMenu || Main.mapFullscreen || !PlayerVitalStatsUI)
		{
			return;
		}
		try
		{
			Main.spriteBatch.End();
			Main.spriteBatch.Begin(SpriteSortMode.Deferred, null, null, null, null, null, Main.UIScaleMatrix);
			PlayerInput.SetZoom_World();
			int screenWidth = Main.screenWidth;
			int screenHeight = Main.screenHeight;
			Vector2 screenPosition = Main.screenPosition;
			PlayerInput.SetZoom_UI();
			for (int i = 0; i < 255; i++)
			{
				Player player = Main.player[i];
				if (player.active && i != Main.myPlayer)
				{
					DrawPlayerVitals_Inner(player, screenWidth, screenHeight, screenPosition);
				}
			}
			DrawPlayerVitals_Inner(Main.LocalPlayer, screenWidth, screenHeight, screenPosition);
		}
		catch
		{
		}
	}

	private static void DrawPlayerVitals_Inner(Player player, int screenWidth, int screenHeight, Vector2 screenPosition)
	{
		if (player.dead || player.ghost)
		{
			return;
		}
		float uIScale = Main.UIScale;
		SpriteViewMatrix gameViewMatrix = Main.GameViewMatrix;
		Color color = new Color(255f, 255f, 255f) * 0.65f;
		string text = "   " + player.statLife + "/" + player.statLifeMax2 + "    " + player.statMana + "/" + player.statManaMax2 + "    " + player.statDefense;
		string text2 = player.statLife + "/" + player.statLifeMax2 + " ";
		string text3 = player.statMana + "/" + player.statManaMax2 + " ";
		Vector2 size = FontAssets.DeathText.Value.MeasureString(IsaClient.Languages.L10n.T(text)) / 2f;
		size.X -= FontAssets.DeathText.Value.MeasureString(IsaClient.Languages.L10n.T(text)).X / (5f * gameViewMatrix.Zoom.X) * (gameViewMatrix.Zoom.X / 2f);
		float num = 42f;
		if (Main.LocalPlayer.gravDir == -1f)
		{
			num += 24f;
		}
		if (player.statLife < player.statLifeMax2 && player.whoAmI != Main.myPlayer)
		{
			num += ((Main.LocalPlayer.gravDir == -1f) ? 8f : 16f);
		}
		Vector2 screenCenter = new Vector2((float)(screenWidth / 2) + screenPosition.X, (float)(screenHeight / 2) + screenPosition.Y);
		Vector2 bottomLeft = player.BottomLeft;
		bottomLeft += (bottomLeft - screenCenter) * (gameViewMatrix.Zoom - Vector2.One);
		Vector2 pos;
		pos.X = bottomLeft.X + (float)(player.width / 2) - size.X / 2f - screenPosition.X;
		pos.Y = bottomLeft.Y - size.Y - 2f + num - screenPosition.Y;
		Vector2 half = FontAssets.DeathText.Value.MeasureString(IsaClient.Languages.L10n.T(text)) / 2f;
		pos += half / 2f;
		pos *= 1f / uIScale;
		pos -= half / 2f;
		if (Main.LocalPlayer.gravDir == -1f)
		{
			pos.Y = (float)screenHeight - pos.Y;
		}
		pos = pos.Floor();
		ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value, IsaClient.Languages.L10n.T(text), pos, color, 0f, Vector2.Zero, Vector2.One);
		Main.spriteBatch.Draw((player.statLifeMax > 400) ? TextureAssets.Heart2.Value : TextureAssets.Heart.Value, pos, null, color, 0f, Vector2.Zero, 0.9f, SpriteEffects.None, 0f);
		Vector2 manaPos = new Vector2(pos.X + 21f + FontAssets.DeathText.Value.MeasureString(IsaClient.Languages.L10n.T(text2)).X * 0.4f, pos.Y - 1.5f);
		Main.spriteBatch.Draw(TextureAssets.Mana.Value, manaPos, null, color, 0f, Vector2.Zero, 0.9f, SpriteEffects.None, 0f);
		Vector2 defensePos = new Vector2(manaPos.X + 21f + FontAssets.DeathText.Value.MeasureString(IsaClient.Languages.L10n.T(text3)).X * 0.4f, pos.Y);
		Main.spriteBatch.Draw(TextureAssets.Extra[58].Value, defensePos, null, color, 0f, Vector2.Zero, 0.5f, SpriteEffects.None, 0f);
	}

	#region Chat commands

	/// <summary>.playerlist / .players / .plist</summary>
	internal static void PlayerListCommand(CommandContext command)
	{
		TogglePlayerList();
	}

	/// <summary>.clients / .zmod</summary>
	internal static void ClientsCommand(CommandContext command)
	{
		PrintClients();
	}

	/// <summary>.shareinfo / .informother / .informothers</summary>
	internal static void InformOthersCommand(CommandContext command)
	{
		ShareMyInfo = !ShareMyInfo;
		ClientSettings.Save();
		ClientChat.Info("Inform other clients you use IsaClient: " + (ShareMyInfo ? "yes (you show up in their Player List)" : "no (hidden)"));
	}

	/// <summary>.vitals / .playervitals</summary>
	internal static void PlayerVitalsCommand(CommandContext command)
	{
		PlayerVitalStatsUI = !PlayerVitalStatsUI;
		ClientChat.Info("Player vitals " + (PlayerVitalStatsUI ? "en" : "dis") + "abled.");
	}

	/// <summary>.track / .trackplayers / .plrtracker / .playertracker</summary>
	internal static void PlayerTrackerCommand(CommandContext command)
	{
		PlayerTracker = !PlayerTracker;
		ClientChat.Info("Player Tracker " + (PlayerTracker ? "En" : "Dis") + "abled");
	}

	#endregion
}
