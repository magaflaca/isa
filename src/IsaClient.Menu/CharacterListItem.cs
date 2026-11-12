using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.UI;
using IsaClient.Community;
using Terraria;

namespace IsaClient.Menu;

/// <summary>
/// One row of the Player List (one per player index, 0-255; 255 is the server). Portrait (left click teleports to
/// them), name with its state, life/mana, difficulty and index, the spectate tick (left: spectate, right: spectate and show
/// their inventory in your HUD) and the client badge (zMod or IsaClient user). Clicking the row opens the inventory viewer.
/// Inactive slots that still hold data are listed greyed out as leftover data.
/// </summary>
public class CharacterListItem : UIPanel
{
	private const float Hidden = 100000000f;

	public Player OurPlayer;

	private readonly Asset<Texture2D> _dividerTexture;

	private readonly Asset<Texture2D> _innerPanelTexture;

	private readonly UICharacter _playerPanel;

	private readonly UIImageButton _spectateButton;

	private readonly UIImage _clientIcon;

	public int PlayerIndex;

	private readonly UIElement _goldenOutline;

	private readonly UIElement _specOn;

	private int _shownKind = 2;

	private static Asset<Texture2D> StatueIcon => Main.Assets.Request<Texture2D>("Images/Item_" + Terraria.ID.ItemID.AlphabetStatueZ, AssetRequestMode.ImmediateLoad);

	private static Asset<Texture2D> IsaStatue => Main.Assets.Request<Texture2D>("Images/Item_" + Terraria.ID.ItemID.AlphabetStatueI, AssetRequestMode.ImmediateLoad);

	private static Asset<Texture2D> TerraAngelStatue => Main.Assets.Request<Texture2D>("Images/Item_" + Terraria.ID.ItemID.AlphabetStatueT, AssetRequestMode.ImmediateLoad);

	private bool IsServerSlot
	{
		get
		{
			if (PlayerIndex == 255)
			{
				return Main.netMode == 1;
			}
			return false;
		}
	}

	public bool IsActive
	{
		get
		{
			if (!Main.player[PlayerIndex].active)
			{
				return IsServerSlot;
			}
			return true;
		}
	}

	public CharacterListItem(int index)
	{
		BackgroundColor = new Color(63, 82, 151) * 0.7f;
		BorderColor = new Color(89, 116, 213) * 0.7f;
		_dividerTexture = Main.Assets.Request<Texture2D>("Images/UI/Divider", AssetRequestMode.ImmediateLoad);
		_innerPanelTexture = Main.Assets.Request<Texture2D>("Images/UI/InnerPanelBackground", AssetRequestMode.ImmediateLoad);
		Height.Set(70f, 0f);
		Width.Set(0f, 1f);
		SetPadding(6f);
		if (index < 0 || index > 255)
		{
			index = 0;
		}
		OurPlayer = new Player();
		PlayerIndex = index;
		CloneFromIndex();
		UpdateBackgroundColor();
		UIImageButton spectate = new UIImageButton(Main.Assets.Request<Texture2D>("Images/Inventory_Tick_Off", AssetRequestMode.ImmediateLoad))
		{
			VAlign = 0.075f,
			HAlign = 0.995f,
			MarginTop = Hidden
		};
		UIImage specOn = new UIImage(Main.Assets.Request<Texture2D>("Images/Inventory_Tick_On", AssetRequestMode.ImmediateLoad))
		{
			IgnoresMouseInteraction = true
		};
		_specOn = specOn;
		spectate.Append(_specOn);
		spectate.OnLeftClick += delegate
		{
			SpectatePlayer(withInventory: false);
		};
		spectate.OnRightClick += delegate
		{
			SpectatePlayer(withInventory: true);
		};
		_spectateButton = spectate;
		Append(spectate);
		UIImage icon = new UIImage(StatueIcon)
		{
			ImageScale = 0.75f,
			VAlign = -0.3f,
			HAlign = 0.965f,
			MarginTop = Hidden
		};
		icon.Color = Color.Lerp(Color.White, Color.LightCyan, 0.5f);
		_clientIcon = icon;
		Append(icon);
		_playerPanel = new UICharacter(OurPlayer);
		_playerPanel.Left.Set(4f, 0f);
		_goldenOutline = new UIImage(Main.Assets.Request<Texture2D>("Images/UI/IconCompletion", AssetRequestMode.ImmediateLoad))
		{
			HAlign = 0.5f,
			VAlign = 0.5f,
			Top = new StyleDimension(-10f, 0f),
			Left = new StyleDimension(-3f, 0f),
			IgnoresMouseInteraction = true
		};
		_playerPanel.Append(_goldenOutline);
		_playerPanel.OnLeftClick += delegate
		{
			if (IsActive && PlayerIndex != Main.myPlayer && PlayerInput.Triggers.JustReleased.MouseLeft && PlayerIndex != 255)
			{
				Spectate.TeleportToPlayer(PlayerIndex);
			}
		};
		_goldenOutline.MarginTop = (PlayerIndex == Main.myPlayer) ? 0f : Hidden;
		Append(_playerPanel);
		base.OnLeftClick += OpenInvView;
	}

	/// <summary>Copies the live player into <see cref="OurPlayer"/> (the new panel draws that copy).</summary>
	internal void Refresh()
	{
		CloneFromIndex();
	}

	/// <summary>The "(Inactive)", "(Connecting)", "(Vanished)" prefix of the name.</summary>
	internal string NamePrefix => GetNamePrefix();

	private void CloneFromIndex()
	{
		Player player = Main.player[PlayerIndex];
		if (!IsServerSlot)
		{
			OurPlayer.name = player.name;
			OurPlayer.joining = player.joining;
			OurPlayer.spawned = player.spawned;
			OurPlayer.team = player.team;
			OurPlayer.hostile = player.hostile;
			OurPlayer.difficulty = player.difficulty;
			OurPlayer.statLife = player.statLife;
			OurPlayer.statMana = player.statMana;
			OurPlayer.statLifeMax = player.statLifeMax;
			OurPlayer.statLifeMax2 = player.statLifeMax2;
			OurPlayer.statManaMax = player.statManaMax;
			OurPlayer.statManaMax2 = player.statManaMax2;
			OurPlayer.skinVariant = player.skinVariant;
			OurPlayer.eyeColor = player.eyeColor;
			OurPlayer.hairColor = player.hairColor;
			OurPlayer.hair = player.hair;
			OurPlayer.skinColor = player.skinColor;
			OurPlayer.shirtColor = player.shirtColor;
			OurPlayer.underShirtColor = player.underShirtColor;
			OurPlayer.shoeColor = player.shoeColor;
			OurPlayer.pantsColor = player.pantsColor;
			OurPlayer.hairDye = player.hairDye;
			OurPlayer.hairDyeColor = player.hairDyeColor;
			OurPlayer.hairDyeVar = player.hairDyeVar;
			OurPlayer.armor = player.armor;
			OurPlayer.dye = player.dye;
			OurPlayer.miscEquips = player.miscEquips;
			OurPlayer.miscDyes = player.miscDyes;
			OurPlayer.hideMisc = player.hideMisc;
			OurPlayer.hideVisibleAccessory = player.hideVisibleAccessory;
			OurPlayer.wereWolf = player.wereWolf;
			OurPlayer.hideWolf = player.hideWolf;
			OurPlayer.forceWerewolf = player.forceWerewolf;
			OurPlayer.merman = player.merman;
			OurPlayer.hideMerman = player.hideMerman;
			OurPlayer.forceMerman = player.forceMerman;
			OurPlayer.zone1 = player.zone1;
			OurPlayer.zone2 = player.zone2;
			OurPlayer.zone3 = player.zone3;
			OurPlayer.zone4 = player.zone4;
			OurPlayer.zone5 = player.zone5;
			OurPlayer.wasDetectedInVanish = player.wasDetectedInVanish;
			if (!IsActive)
			{
				OurPlayer.wereWolf = false;
				OurPlayer.hideWolf = false;
				OurPlayer.forceWerewolf = false;
				OurPlayer.merman = false;
				OurPlayer.hideMerman = false;
				OurPlayer.forceMerman = false;
				OurPlayer.zone5 = (byte)0;
			}
			else
			{
				if ((OurPlayer.wereWolf || OurPlayer.forceWerewolf) && !OurPlayer.hideWolf)
				{
					OurPlayer.legs = 20;
					OurPlayer.body = 21;
					OurPlayer.head = 38;
				}
				if ((OurPlayer.merman || OurPlayer.forceMerman) && !OurPlayer.hideMerman)
				{
					OurPlayer.head = 39;
					OurPlayer.legs = 21;
					OurPlayer.body = 22;
				}
			}
		}
		else
		{
			OurPlayer.name = "Server";
			OurPlayer.statLifeMax = 1000000;
			OurPlayer.statLifeMax2 = OurPlayer.statLifeMax;
			OurPlayer.statLife = OurPlayer.statLifeMax;
			OurPlayer.statManaMax = 1000000;
			OurPlayer.statManaMax2 = OurPlayer.statManaMax;
			OurPlayer.statMana = OurPlayer.statManaMax;
		}
		OurPlayer.direction = 1;
		OurPlayer.gravDir = 1f;
		OurPlayer.socialIgnoreLight = true;
		OurPlayer.isaAlwaysLit = true;
	}

	private static Color Dim(Color c, Color by, float alpha)
	{
		c = c.MultiplyRGB(by);
		c.A = (byte)((float)(int)c.A * alpha);
		return c;
	}

	private void UpdateBackgroundColor()
	{
		bool server = IsServerSlot;
		if (!base.IsMouseHovering)
		{
			BackgroundColor = new Color(server ? 138 : 63, 82, 151) * 0.7f;
			BorderColor = new Color(server ? 165 : 89, 116, 213) * 0.7f;
			if (!IsActive)
			{
				BackgroundColor = Dim(BackgroundColor, new Color(150, 150, 150), 0.7f);
				BorderColor = Dim(BorderColor, new Color(150, 150, 150), 0.7f);
			}
		}
		else
		{
			BackgroundColor = new Color(server ? 156 : 73, 94, 171) * 0.8f;
			BorderColor = new Color(server ? 190 : 89, 116, 213) * 0.8f;
			if (!IsActive)
			{
				BackgroundColor = Dim(BackgroundColor, new Color(175, 175, 175), 0.8f);
				BorderColor = Dim(BorderColor, new Color(175, 175, 175), 0.8f);
			}
		}
	}

	public override void MouseOver(UIMouseEvent evt)
	{
		base.MouseOver(evt);
		_playerPanel.SetAnimated(animated: true);
	}

	public override void MouseOut(UIMouseEvent evt)
	{
		base.MouseOut(evt);
		_playerPanel.SetAnimated(animated: false);
	}

	private void DrawPanel(SpriteBatch spriteBatch, Vector2 position, float width)
	{
		Color color = (!IsActive) ? Color.Gray : (IsServerSlot ? Color.Purple : Color.White);
		Texture2D tex = _innerPanelTexture.Value;
		spriteBatch.Draw(tex, position, new Rectangle(0, 0, 8, tex.Height), color);
		spriteBatch.Draw(tex, new Vector2(position.X + 8f, position.Y), new Rectangle(8, 0, 8, tex.Height), color, 0f, Vector2.Zero, new Vector2((width - 16f) / 8f, 1f), SpriteEffects.None, 0f);
		spriteBatch.Draw(tex, new Vector2(position.X + width - 8f, position.Y), new Rectangle(16, 0, 8, tex.Height), color);
	}

	private bool CanAct
	{
		get
		{
			if (PlayerIndex != Main.myPlayer && IsActive)
			{
				return PlayerIndex != 255;
			}
			return false;
		}
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		_specOn.MarginTop = (Spectate.SpectatePlayerTarget == PlayerIndex && PlayerIndex != 255) ? 0f : Hidden;
		if (PlayerIndex == Main.myPlayer && _clientIcon.IsMouseHovering && _clientIcon.MarginTop == 0f)
		{
			Main.instance.MouseText(IsaClient.Languages.L10n.T("Other IsaClient users see you as an IsaClient user (zMod and vanilla clients never do).\nClick your row to change it."), 0, 0);
		}
		if (!CanAct)
		{
			return;
		}
		if (_playerPanel.IsMouseHovering)
		{
			Main.instance.MouseText(IsaClient.Languages.L10n.T("Left click to teleport to this player."), 0, 0);
		}
		if (_clientIcon.IsMouseHovering && _clientIcon.MarginTop == 0f)
		{
			int hovered = Main.player[PlayerIndex].IsaClientKind;
			Main.instance.MouseText(IsaClient.Languages.L10n.T((hovered == 1) ? "IsaClient User" : ((hovered == 2) ? "zMod User" : "TerraAngel User")), 0, 0);
		}
		if (_spectateButton.IsMouseHovering)
		{
			_specOn.MarginTop = (Spectate.SpectatePlayerTarget == PlayerIndex) ? Hidden : 0f;
			Main.instance.MouseText(IsaClient.Languages.L10n.T((Spectate.SpectatePlayerTarget != PlayerIndex) ? "Left click to spectate this player.\nRight click to see their inventory contents whilst spectating." : "Left/right click to stop spectating this player."), 0, 0);
		}
	}

	private void SpectatePlayer(bool withInventory)
	{
		if (CanAct)
		{
			Spectate.ToggleSpectate(PlayerIndex, withInventory);
		}
	}

	private string GetNamePrefix()
	{
		string text = "";
		if (!IsActive && PlayerIndex != Main.myPlayer)
		{
			return (OurPlayer.name.Length > 0) ? "(Inactive - Leftover Data) " : "(Inactive) ";
		}
		if (OurPlayer.joining && PlayerIndex != Main.myPlayer)
		{
			text += "(Connecting) ";
		}
		if (OurPlayer.wasDetectedInVanish && PlayerIndex != Main.myPlayer)
		{
			text += "(Vanished) ";
		}
		return text;
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		base.DrawSelf(spriteBatch);
		int kind = (IsActive && !IsServerSlot) ? Main.player[PlayerIndex].IsaClientKind : 0;
		if (PlayerIndex == Main.myPlayer || PlayerIndex == 255)
		{
			_goldenOutline.MarginTop = (PlayerIndex == Main.myPlayer) ? 0f : Hidden;
			_spectateButton.MarginTop = Hidden;
			// Your own row shows the I statue while you let other clients see you as an IsaClient user.
			_clientIcon.MarginTop = (PlayerIndex == Main.myPlayer && kind != 0) ? 0f : Hidden;
			if (PlayerIndex == Main.myPlayer && kind != 0 && kind != _shownKind)
			{
				_shownKind = kind;
				_clientIcon.SetImage(IsaStatue);
			}
		}
		else
		{
			_goldenOutline.MarginTop = Hidden;
			_spectateButton.MarginTop = 0f;
			_clientIcon.MarginTop = (kind != 0) ? 0f : Hidden;
			// zMod users: the "Z" alphabet statue. IsaClient users: the "I" statue. TerraAngel users: the "T" statue.
			if (kind != 0 && kind != _shownKind)
			{
				_shownKind = kind;
				_clientIcon.SetImage((kind == 1) ? IsaStatue : ((kind == 2) ? StatueIcon : TerraAngelStatue));
			}
		}
		if (!IsActive)
		{
			_spectateButton.MarginTop = Hidden;
			_clientIcon.MarginTop = Hidden;
		}
		CalculatedStyle inner = GetInnerDimensions();
		CalculatedStyle portrait = _playerPanel.GetDimensions();
		float left = portrait.X + portrait.Width;
		CloneFromIndex();
		UpdateBackgroundColor();
		Color nameColor = (!IsActive) ? Color.DarkGray : (IsServerSlot ? Color.DeepPink : Color.White);
		Color iconColor = IsActive ? Color.White : Color.DarkGray;
		string name = GetNamePrefix() + ((OurPlayer.name.Length > 0) ? OurPlayer.name : (IsActive ? "(No Name)" : ""));
		Utils.DrawBorderString(spriteBatch, IsaClient.Languages.L10n.T(name), new Vector2(left + 6f, portrait.Y - 2f), nameColor);
		spriteBatch.Draw(_dividerTexture.Value, new Vector2(left, inner.Y + 21f), null, nameColor, 0f, Vector2.Zero, new Vector2((GetDimensions().X + GetDimensions().Width - left) / 8f, 1f), SpriteEffects.None, 0f);
		Vector2 cursor = new Vector2(left + 6f, inner.Y + 29f);
		float statsWidth = 215f;
		if (GameCulture.FromCultureName(GameCulture.CultureName.Chinese).IsActive)
		{
			statsWidth = 270f;
		}
		Vector2 stats = cursor;
		DrawPanel(spriteBatch, stats, statsWidth);
		spriteBatch.Draw(TextureAssets.Heart.Value, stats + new Vector2(5f, 2f), iconColor);
		stats.X += 10f + (float)TextureAssets.Heart.Width();
		Utils.DrawBorderString(spriteBatch, IsaClient.Languages.L10n.T((IsServerSlot ? "∞" : OurPlayer.statLifeMax2.ToString()) + Language.GetTextValue("GameUI.PlayerLifeMax")), stats + new Vector2(0f, 3f), nameColor);
		stats.X += 85f;
		spriteBatch.Draw(TextureAssets.Mana.Value, stats + new Vector2(5f, 2f), iconColor);
		stats.X += 8f + (float)TextureAssets.Mana.Width();
		Utils.DrawBorderString(spriteBatch, IsaClient.Languages.L10n.T((IsServerSlot ? "∞" : OurPlayer.statManaMax2.ToString()) + Language.GetTextValue("GameUI.PlayerManaMax")), stats + new Vector2(0f, 3f), nameColor);
		cursor.X += statsWidth + 5f;
		Vector2 diffPos = cursor;
		float diffWidth = 140f;
		if (GameCulture.FromCultureName(GameCulture.CultureName.Chinese).IsActive)
		{
			diffWidth = 80f;
		}
		else if (GameCulture.FromCultureName(GameCulture.CultureName.Russian).IsActive)
		{
			diffWidth = 160f;
		}
		DrawPanel(spriteBatch, diffPos, diffWidth);
		string difficulty = "";
		Color diffColor = Color.White;
		switch (OurPlayer.difficulty)
		{
		case 0:
			difficulty = Language.GetTextValue("UI.Softcore");
			break;
		case 1:
			difficulty = Language.GetTextValue("UI.Mediumcore");
			diffColor = Main.mcColor;
			break;
		case 2:
			difficulty = Language.GetTextValue("UI.Hardcore");
			diffColor = Main.hcColor;
			break;
		case 3:
			difficulty = Language.GetTextValue("UI.Creative");
			diffColor = Main.creativeModeColor;
			break;
		}
		if (IsServerSlot)
		{
			difficulty = "Operator";
			diffColor = Color.Purple;
		}
		if (!IsActive)
		{
			diffColor = diffColor.MultiplyRGB(Color.DarkGray);
		}
		diffPos += new Vector2(diffWidth * 0.5f - FontAssets.MouseText.Value.MeasureString(IsaClient.Languages.L10n.T(difficulty)).X * 0.5f, 3f);
		Utils.DrawBorderString(spriteBatch, IsaClient.Languages.L10n.T(difficulty), diffPos, diffColor);
		cursor.X += diffWidth + 5f;
		Vector2 indexPos = cursor;
		float indexWidth = inner.X + inner.Width - indexPos.X;
		DrawPanel(spriteBatch, indexPos, indexWidth);
		string indexText = "Index " + PlayerIndex;
		indexPos += new Vector2(indexWidth * 0.5f - FontAssets.MouseText.Value.MeasureString(IsaClient.Languages.L10n.T(indexText)).X * 0.5f, 3f);
		Utils.DrawBorderString(spriteBatch, IsaClient.Languages.L10n.T(indexText), indexPos, nameColor);
	}

	private void OpenInvView(UIMouseEvent evt, UIElement element)
	{
		if (OurPlayer.name != "" && !_playerPanel.IsMouseHovering && !_spectateButton.IsMouseHovering && PlayerIndex < 255)
		{
			if (PlayerIndex == Main.myPlayer)
			{
				CharacterWindow.Open();
				return;
			}
			SoundEngine.PlaySound(10);
			InventoryViewer.Open(PlayerIndex);
		}
	}
}
