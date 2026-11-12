using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Content;
using ReLogic.Graphics;
using ReLogic.OS;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.UI.Chat;
using IsaClient.Networking;
using IsaClient.Core;
using Terraria.ID;
using Terraria;

namespace IsaClient.Menu;

/// <summary>
/// Main menu overlay in the top-left corner: UUID tweaks ([F1] change, [F2] hide, [F3] random UUID), [F4] logo,
/// [F5] network version (1.4.5.8 / 1.4.5.6), the proxy line, then the client version, the credits with clickable links and
/// a row of icons (TerraAngel and original client credits, thanks, bots, music note).
/// </summary>
public static class MainMenuOverlay
{
	private const string AuthorUrl = "https://github.com/magaflaca";

	private const string OriginModUrl = "https://youtu.be/UItObM05crs";

	private const string TerraAngelUrl = "https://github.com/UnrealMultiple/TerraAngel";

	private const float Scale = 0.32f;

	private const float LineHeight = 18f;

	private static readonly Color _textColor = new Color(255, 255, 255) * 0.9f;

	private static readonly Color _linkColor = new Color(120, 190, 255);

	private static readonly Color _linkHoverColor = new Color(255, 231, 69);

	private struct Segment
	{
		public string Text;

		public string Url;
	}

	private static readonly List<Segment[]> _creditLines = new List<Segment[]>
	{
		new Segment[2]
		{
			new Segment { Text = "By " },
			new Segment { Text = "@isawicca", Url = AuthorUrl }
		},
		new Segment[5]
		{
			new Segment { Text = "Inspired by " },
			new Segment { Text = "TerraAngel", Url = TerraAngelUrl },
			new Segment { Text = ", " },
			new Segment { Text = "ZazaMod", Url = OriginModUrl },
			new Segment { Text = " and other clients." }
		}
	};

	public static void Draw(SpriteBatch spriteBatch)
	{
		if (!Main.gameMenu || WorldGen.drunkWorldGen)
		{
			return;
		}
		HandleKeys();
		if (MenuState.HideGameMenuUi)
		{
			return;
		}
		DynamicSpriteFont font = FontAssets.DeathText.Value;
		Vector2 position = new Vector2(4f, 16f);
		Point mouse = new Point(Main.mouseX, Main.mouseY);
		DrawString(spriteBatch, font, "Current UUID: " + Main.clientUUID, position, _textColor, 0.33f);
		position.Y += LineHeight;
		string text = "[F1] Change UUID, or click: ";
		DrawString(spriteBatch, font, text, position, _textColor, Scale);
		Vector2 iconPos = new Vector2(position.X + font.MeasureString(IsaClient.Languages.L10n.T(text)).X * Scale, position.Y - 2f);
		Asset<Texture2D>[] reforge = TextureAssets.Reforge;
		Rectangle iconBox = new Rectangle((int)iconPos.X, (int)iconPos.Y, (int)((double)reforge[0].Width() * 0.75), (int)((double)reforge[0].Height() * 0.75));
		bool iconHover = iconBox.Contains(mouse);
		spriteBatch.Draw(reforge[iconHover ? 1 : 0].Value, iconPos, null, Color.White, 0f, Vector2.Zero, 0.75f, SpriteEffects.None, 0f);
		if (iconHover)
		{
			Main.LocalPlayer.mouseInterface = true;
			if (Main.mouseLeft && Main.mouseLeftRelease)
			{
				Main.mouseLeftRelease = false;
				ChangeUUID();
			}
		}
		position.Y += LineHeight;
		DrawString(spriteBatch, font, "[F2] Hide UI", position, _textColor, Scale);
		position.Y += LineHeight;
		DrawString(spriteBatch, font, "[F3] Random UUID: " + (NetDebug.RandomUUID ? "enabled" : "disabled"), position, _textColor, Scale);
		position.Y += LineHeight;
		DrawString(spriteBatch, font, "[F4] Logo, icon & music: " + (ClientLogo.Enabled ? "IsaClient" : "Terraria"), position, _textColor, Scale);
		position.Y += LineHeight;
		DrawNetVersionLine(spriteBatch, font, position, mouse);
		position.Y += LineHeight;
		DrawMobileLine(spriteBatch, font, position, mouse);
		position.Y += LineHeight + 4f;
		DrawProxyBlock(spriteBatch, font, ref position, mouse);
		if (Main.menuMode != 0)
		{
			return;
		}
		position.Y += LineHeight + 8f;
		DrawString(spriteBatch, font, ClientInfo.DisplayName, position, new Color(255, 231, 69), 0.36f);
		position.Y += LineHeight + 2f;
		foreach (Segment[] line in _creditLines)
		{
			float x = position.X;
			foreach (Segment segment in line)
			{
				Vector2 size = font.MeasureString(IsaClient.Languages.L10n.T(segment.Text)) * Scale;
				Color color = _textColor;
				if (segment.Url != null)
				{
					Rectangle hitBox = new Rectangle((int)x, (int)position.Y, (int)size.X, (int)LineHeight);
					bool hovering = hitBox.Contains(mouse);
					color = (hovering ? _linkHoverColor : _linkColor);
					if (hovering)
					{
						Main.LocalPlayer.mouseInterface = true;
						Main.instance.MouseText(IsaClient.Languages.L10n.T(segment.Url), 0, 0);
						if (!PlayerInput.IgnoreMouseInterface && Main.mouseLeft && Main.mouseLeftRelease)
						{
							Main.mouseLeftRelease = false;
							SoundEngine.PlaySound(10);
							OpenLink(segment.Url);
						}
					}
				}
				DrawString(spriteBatch, font, segment.Text, new Vector2(x, position.Y), color, Scale);
				x += size.X;
			}
			position.Y += LineHeight;
		}
		DrawIconRow(spriteBatch, new Vector2(position.X + MenuIcon.Size / 2f + 2f, position.Y + MenuIcon.Size / 2f + 6f), mouse);
	}

	/// <summary>
	/// The icons under the credits, left to right: T statue (TerraAngel's credits), Z statue (the original client's credits), heart
	/// (thanks), robot head (bots), the music note (music window) and the flag of IsaClient's language (language
	/// window). The note can be dragged away: the flag then takes its place next to the robot.
	/// </summary>
	private static void DrawIconRow(SpriteBatch spriteBatch, Vector2 firstCenter, Point mouse)
	{
		const float step = MenuIcon.Size + 8f;
		Vector2 center = firstCenter;
		CreditsScreen.DrawTerraAngelCredits(spriteBatch, center, mouse);
		center.X += step;
		CreditsScreen.DrawOriginCredits(spriteBatch, center, mouse);
		center.X += step;
		CreditsScreen.DrawThanks(spriteBatch, center, mouse);
		center.X += step;
		DrawBotsIcon(spriteBatch, center, mouse);
		center.X += step;
		bool noteHere = HudButtons.MusicNote && !HudButtons.MovedInMenu("note");
		HudButtons.DrawMusicNoteInMenu(spriteBatch, center);
		if (noteHere)
		{
			center.X += step;
		}
		DrawLanguageFlag(spriteBatch, center, mouse);
	}

	/// <summary>The flag of the language IsaClient is in: opens the language window.</summary>
	private static void DrawLanguageFlag(SpriteBatch spriteBatch, Vector2 center, Point mouse)
	{
		IsaClient.Languages.L10n.Pack pack = IsaClient.Languages.L10n.CurrentPack ?? IsaClient.Languages.L10n.Find(IsaClient.Languages.L10n.English);
		Rectangle box = new Rectangle((int)(center.X - MenuIcon.Size / 2f), (int)(center.Y - MenuIcon.Size / 2f), (int)MenuIcon.Size, (int)MenuIcon.Size);
		bool hover = MenuIcon.Contains(box, new Vector2(mouse.X, mouse.Y)) && !IsaClient.Music.MusicPlayerWindow.CoversUiPoint(mouse);
		float grow = hover ? 1.15f : 1f;
		int w = (int)(26 * grow);
		int h = (int)(17 * grow);
		IsaClient.Hub.CountryFlags.Draw(spriteBatch, pack?.Flag ?? "us", new Rectangle((int)center.X - w / 2, (int)center.Y - h / 2, w, h), hover ? 1f : 0.9f);
		if (!hover)
		{
			return;
		}
		Main.LocalPlayer.mouseInterface = true;
		Main.instance.MouseText(IsaClient.Languages.L10n.F("IsaClient's language: {0}", pack?.Name ?? "English") + "\n" + IsaClient.Languages.L10n.T("Click to change it, or to import or export language files"), 0, 0);
		if (Main.mouseLeft && Main.mouseLeftRelease)
		{
			Main.mouseLeftRelease = false;
			IsaClient.Languages.LanguageWindow.Toggle();
		}
	}

	/// <summary>Robot head: opens the Bots window (<see cref="BotsMenu"/>).</summary>
	private static void DrawBotsIcon(SpriteBatch spriteBatch, Vector2 center, Point mouse)
	{
		bool usable = BotsMenu.CanOpen;
		if (!MenuIcon.DrawItem(spriteBatch, ItemID.RobotMask, center, mouse, usable ? Color.White : (Color.White * 0.5f)))
		{
			return;
		}
		Main.LocalPlayer.mouseInterface = true;
		Main.instance.MouseText(IsaClient.Languages.L10n.T(usable ? "Bots: extra clients on any server. Click to open." : "Bots can be opened from the main menu screens."), 0, 0);
		if (usable && Main.mouseLeft && Main.mouseLeftRelease)
		{
			Main.mouseLeftRelease = false;
			BotsMenu.Toggle();
		}
	}

	/// <summary>
	/// [F5] network version line with a paper airplane (B for 1.4.5.8, A for 1.4.5.6): switches between the two protocols,
	/// <see cref="ProtocolCompat"/>.
	/// </summary>
	private static void DrawNetVersionLine(SpriteBatch spriteBatch, DynamicSpriteFont font, Vector2 position, Point mouse)
	{
		string text = "[F5] Network version: " + ProtocolCompat.VersionName + "  ";
		DrawString(spriteBatch, font, text, position, ProtocolCompat.Legacy ? new Color(255, 200, 120) : _textColor, Scale);
		bool canToggle = ProtocolCompat.CanToggle;
		int icon = ProtocolCompat.Legacy ? ItemID.PaperAirplaneA : ItemID.PaperAirplaneB;
		bool iconHover = MenuIcon.DrawItem(spriteBatch, icon, LineIconCenter(font, text, position), mouse, canToggle ? Color.White : (Color.White * 0.5f));
		if (!iconHover)
		{
			return;
		}
		Main.LocalPlayer.mouseInterface = true;
		Main.instance.MouseText(IsaClient.Languages.L10n.T(canToggle ? ProtocolCompat.Tooltip : "The network version can only be changed while disconnected."), 0, 0);
		if (canToggle && Main.mouseLeft && Main.mouseLeftRelease)
		{
			Main.mouseLeftRelease = false;
			ProtocolCompat.Toggle();
		}
	}

	/// <summary>
	/// Platform line: a Shellphone while playing as a PC, a light yellow Cell Phone while simulating a phone
	/// (<see cref="MobileMode"/>). Clicking the icon switches between the two.
	/// </summary>
	private static void DrawMobileLine(SpriteBatch spriteBatch, DynamicSpriteFont font, Vector2 position, Point mouse)
	{
		string text = "Platform: " + (MobileMode.Enabled ? "Mobile (Android)" : "PC") + "  ";
		DrawString(spriteBatch, font, text, position, MobileMode.Enabled ? new Color(255, 200, 120) : _textColor, Scale);
		bool canToggle = MobileMode.CanToggle;
		int icon = MobileMode.Enabled ? ItemID.CellPhone : ItemID.Shellphone;
		bool iconHover = MenuIcon.DrawItem(spriteBatch, icon, LineIconCenter(font, text, position), mouse, canToggle ? Color.White : (Color.White * 0.5f));
		if (!iconHover)
		{
			return;
		}
		Main.LocalPlayer.mouseInterface = true;
		Main.instance.MouseText(IsaClient.Languages.L10n.T(canToggle ? MobileMode.Tooltip : "The platform can only be changed while disconnected."), 0, 0);
		if (canToggle && Main.mouseLeft && Main.mouseLeftRelease)
		{
			Main.mouseLeftRelease = false;
			MobileMode.Toggle();
		}
	}

	/// <summary>Personal proxy line with a Möbius strip: clicking it opens (or closes) the proxy window, <see cref="ProxyMenu"/>.</summary>
	private static void DrawProxyBlock(SpriteBatch spriteBatch, DynamicSpriteFont font, ref Vector2 position, Point mouse)
	{
		Proxy.EnsureLoaded();
		string label = "Proxy: " + Proxy.StatusLine + "  ";
		DrawString(spriteBatch, font, label, position, Proxy.Enabled ? new Color(150, 230, 170) : _textColor, Scale);
		if (!ProxyMenu.CanOpen && !ProxyMenu.IsOpen)
		{
			return;
		}
		bool open = ProxyMenu.IsOpen;
		bool iconHover = MenuIcon.DrawItem(spriteBatch, ItemID.MobiusStrip, LineIconCenter(font, label, position), mouse, open ? new Color(255, 231, 69) : Color.White);
		if (iconHover)
		{
			Main.LocalPlayer.mouseInterface = true;
			Main.instance.MouseText(IsaClient.Languages.L10n.T(open ? "Close the proxy window" : ("Hide your IP like a VPN: pick a country. " + ProxyPool.WorkingCount + " working proxies right now. Click to open.")), 0, 0);
			if (Main.mouseLeft && Main.mouseLeftRelease)
			{
				Main.mouseLeftRelease = false;
				ProxyMenu.Toggle();
			}
		}
	}

	/// <summary>Center of a small icon right after the text of a line.</summary>
	private static Vector2 LineIconCenter(DynamicSpriteFont font, string text, Vector2 position)
	{
		return new Vector2(position.X + font.MeasureString(IsaClient.Languages.L10n.T(text)).X * Scale + MenuIcon.Size / 2f, position.Y + LineHeight / 2f - 1f);
	}

	private static void HandleKeys()
	{
		if (PlayerInput.WritingText || Main.drawingPlayerChat)
		{
			return;
		}
		if (JustPressed(Keys.F1))
		{
			ChangeUUID();
		}
		if (JustPressed(Keys.F2))
		{
			MenuState.HideGameMenuUi = !MenuState.HideGameMenuUi;
		}
		if (JustPressed(Keys.F3))
		{
			NetDebug.RandomUUID = !NetDebug.RandomUUID;
			SoundEngine.PlaySound(12);
		}
		if (JustPressed(Keys.F4))
		{
			ClientLogo.Toggle();
			SoundEngine.PlaySound(12);
		}
		if (JustPressed(Keys.F5))
		{
			ProtocolCompat.Toggle();
		}
	}

	private static bool JustPressed(Keys key)
	{
		if (Main.keyState.IsKeyDown(key))
		{
			return Main.oldKeyState.IsKeyUp(key);
		}
		return false;
	}

	private static void ChangeUUID()
	{
		Main.clientUUID = Guid.NewGuid().ToString();
		SoundEngine.PlaySound(2, -1, -1, 37);
		try
		{
			Main.SaveSettings();
		}
		catch
		{
		}
	}

	private static void DrawString(SpriteBatch spriteBatch, DynamicSpriteFont font, string text, Vector2 position, Color color, float scale)
	{
		ChatManager.DrawColorCodedStringWithShadow(spriteBatch, font, IsaClient.Languages.L10n.T(text), position, color, 0f, Vector2.Zero, new Vector2(scale), -1f, 1.5f);
	}

	private static void OpenLink(string url)
	{
		try
		{
			Platform.Get<IPathService>().OpenURL(url);
		}
		catch (Exception ex)
		{
			Console.WriteLine("Failed to open link: " + ex.Message);
		}
	}
}
