using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.UI;
using IsaClient.Community;
using IsaClient.Core;
using IsaClient.Gameplay;
using Terraria;

namespace IsaClient.Menu;

/// <summary>
/// Right column of the inventory viewer: name, portrait over the biome they are in, info block and the actions
/// (Teleport, Spectate, Copy Appearance, Copy Inventory, Save .plr). Buttons only show when they apply.
/// </summary>
public class InventoryViewerButtons : UIPanel
{
	private readonly UIList _list;

	private NamePlate _nameplate;

	private PlayerPortraitElement _portrait;

	private FlavorText _flavorText;

	private UIElement _nameplateElement;

	private UIElement _portraitElement;

	private UIElement _flavorTextElement;

	private UIElement _teleportButton;

	private UIElement _spectateButton;

	private UIElement _saveAsFileButton;

	private UIElement _copyAppearenceButton;

	private UIElement _copyInventoryButton;

	private UIElement _shareButton;

	private UIElement _restoreButton;

	private UIElement _pixelArtButton;

	private readonly List<UIElement> _shown = new List<UIElement>();

	public static Player OurPlayer => InventoryViewer.OurPlayer;

	public static int PlayerIndex => InventoryViewer.TargetPlayer;

	public bool IsActive => Main.player[PlayerIndex].active;

	public InventoryViewerButtons()
	{
		Width.Set(230f, 0f);
		Height.Set(0f, 1f);
		SetPadding(0f);
		BorderColor = new Color(89, 116, 213, 255);
		BackgroundColor = new Color(73, 94, 171);
		UIList list = new UIList
		{
			Width = StyleDimension.FromPixelsAndPercent(0f, 1f),
			Height = StyleDimension.FromPixelsAndPercent(0f, 1f)
		};
		list.SetPadding(2f);
		list.PaddingBottom = 4f;
		list.PaddingTop = 4f;
		list.ListPadding = 4f;
		list.ManualSortMethod = delegate
		{
		};
		Append(list);
		_list = list;
		AppendBorderOverEverything();
		FillInfoForEntry();
	}

	public void UpdateNameplateAndFlavorText()
	{
		if (_nameplate?.UIText != null)
		{
			_nameplate.UIText.SetText(OurPlayer.name);
		}
		if (_flavorText?.UIText != null)
		{
			_flavorText.UIText.SetText(IsaClient.Languages.L10n.T(_flavorText.GenerateStuff()));
		}
	}

	private void AppendBorderOverEverything()
	{
		UIPanel border = new UIPanel
		{
			Width = new StyleDimension(0f, 1f),
			Height = new StyleDimension(0f, 1f),
			IgnoresMouseInteraction = true
		};
		border.BorderColor = new Color(89, 116, 213, 255);
		border.BackgroundColor = Color.Transparent;
		Append(border);
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		UpdateNameplateAndFlavorText();
		UpdateUIList();
		UpdateBackgroundImage();
	}

	public void FillInfoForEntry()
	{
		_list.Clear();
		_nameplate = new NamePlate();
		_portrait = new PlayerPortraitElement();
		_flavorText = new FlavorText();
		_nameplateElement = _nameplate.ProvideUIElement();
		_portraitElement = _portrait.ProvideUIElement();
		_flavorTextElement = _flavorText.ProvideUIElement();
		InventoryViewerButton teleport = new InventoryViewerButton("Teleport");
		teleport.OnLeftClick += TeleportButton_OnLeftClick;
		InventoryViewerButton spectate = new InventoryViewerButton("Spectate", "Left click: spectate.\nRight click: spectate and see their inventory in your HUD.", "Stop Spectating", () => Spectate.SpectatePlayerTarget == PlayerIndex || (PlayerIndex == Main.myPlayer && Spectate.SpectatePlayerTarget != -1) || (Spectate.SpectatePlayerTarget != -1 && !IsActive));
		spectate.OnLeftClick += delegate
		{
			SpectatePlayer(withInventory: false);
		};
		spectate.OnRightClick += delegate
		{
			SpectatePlayer(withInventory: true);
		};
		InventoryViewerButton copyLook = new InventoryViewerButton("Copy Appearance", "Wear their hair, skin and colors (your name stays).\nRestore My Character puts yours back.");
		copyLook.OnLeftClick += delegate
		{
			CharacterPresets.Wear(OurPlayer, WearParts.Look, OurPlayer.name);
		};
		InventoryViewerButton copyInventory = new InventoryViewerButton("Copy Inventory", "Wear their inventory, equipment, loadouts, life and mana.\nRestore My Character puts yours back.");
		copyInventory.OnLeftClick += delegate
		{
			CharacterPresets.Wear(OurPlayer, WearParts.Inventory | WearParts.Equipment | WearParts.Stats, OurPlayer.name);
		};
		InventoryViewerButton restore = new InventoryViewerButton("Restore My Character", "Puts your own look, inventory, equipment, life and mana back.");
		restore.OnLeftClick += delegate
		{
			CharacterPresets.Restore();
		};
		_restoreButton = restore;
		InventoryViewerButton pixelArt = new InventoryViewerButton("Paste as Pixel Art", "");
		pixelArt.OnUpdate += delegate(UIElement el)
		{
			if (el.IsMouseHovering)
			{
				Main.instance.MouseText(IsaClient.Languages.L10n.T("Builds this character out of real blocks and starts a paste.\nOptions: " + CharacterPresets.ArtOptionsText() + "\nRight click: change size. Options are in your own row's window and .pixelart"), 0, 0);
			}
		};
		pixelArt.OnLeftClick += delegate
		{
			if (!string.IsNullOrEmpty(OurPlayer.name))
			{
				CharacterPresets.RequestPixelArt(OurPlayer, OurPlayer.name);
				SoundEngine.PlaySound(12);
			}
		};
		pixelArt.OnRightClick += delegate
		{
			CharacterPresets.ArtScale = (CharacterPresets.ArtScale % 4) + 1;
			SoundEngine.PlaySound(12);
		};
		_pixelArtButton = pixelArt;
		InventoryViewerButton share = new InventoryViewerButton("Show Me As IsaClient: On", "Lets other IsaClient users see that you use IsaClient (the I statue in their Player List).\nzMod and vanilla clients never see it. Saved in your settings.", "Show Me As IsaClient: Off", () => !PlayerListPanel.ShareMyInfo);
		share.OnLeftClick += delegate
		{
			PlayerListPanel.ShareMyInfo = !PlayerListPanel.ShareMyInfo;
			ClientSettings.Save();
			SoundEngine.PlaySound(12);
		};
		_shareButton = share;
		InventoryViewerButton save = new InventoryViewerButton("Save .plr (Double Click)", "Double click: saves a copy of this character to your Players folder.\nNew file name, none of your characters is overwritten.");
		save.OnLeftDoubleClick += delegate
		{
			if (!string.IsNullOrEmpty(Main.player[PlayerIndex].name))
			{
				SoundEngine.PlaySound(12);
				CharacterPresets.SaveCopy(Main.player[PlayerIndex]);
			}
		};
		_teleportButton = teleport;
		_spectateButton = spectate;
		_copyAppearenceButton = copyLook;
		_copyInventoryButton = copyInventory;
		_saveAsFileButton = save;
		UpdateUIList();
	}

	private static UIElement Separator()
	{
		return new UIHorizontalSeparator
		{
			Width = StyleDimension.FromPixelsAndPercent(0f, 1f),
			Color = new Color(89, 116, 213, 255) * 0.9f
		};
	}

	/// <summary>Rebuilds the column only when the set of shown buttons changes (not every frame).</summary>
	public void UpdateUIList()
	{
		List<UIElement> want = new List<UIElement>();
		if (IsActive && PlayerIndex != Main.myPlayer)
		{
			want.Add(_teleportButton);
		}
		if ((IsActive && PlayerIndex != Main.myPlayer) || Spectate.SpectatePlayerTarget != -1)
		{
			want.Add(_spectateButton);
		}
		bool hasCharacter = !string.IsNullOrEmpty(OurPlayer.name);
		if (hasCharacter && PlayerIndex != Main.myPlayer)
		{
			want.Add(_copyAppearenceButton);
			want.Add(_copyInventoryButton);
		}
		if (CharacterPresets.Wearing)
		{
			want.Add(_restoreButton);
		}
		if (hasCharacter)
		{
			want.Add(_pixelArtButton);
			want.Add(_saveAsFileButton);
		}
		if (PlayerIndex == Main.myPlayer)
		{
			want.Add(_shareButton);
		}
		bool same = _list.Count > 0 && want.Count == _shown.Count;
		for (int i = 0; same && i < want.Count; i++)
		{
			same = want[i] == _shown[i];
		}
		if (same)
		{
			return;
		}
		_shown.Clear();
		_shown.AddRange(want);
		_list.Clear();
		_list.Add(_nameplateElement);
		_list.Add(Separator());
		_list.Add(_portraitElement);
		_list.Add(Separator());
		_list.Add(_flavorTextElement);
		_list.Add(Separator());
		// Short actions share a row (Teleport | Spectate, Copy Appearance | Copy Inventory) so every action fits
		// the column, even on small screens or a large UI scale.
		for (int i = 0; i < want.Count; i++)
		{
			UIElement button = want[i];
			UIElement next = ((i + 1 < want.Count) ? want[i + 1] : null);
			bool pair = (button == _teleportButton && next == _spectateButton) || (button == _copyAppearenceButton && next == _copyInventoryButton);
			button.Remove();
			if (pair)
			{
				next.Remove();
				UIElement row = new UIElement
				{
					Width = StyleDimension.FromPixelsAndPercent(0f, 1f),
					Height = StyleDimension.FromPixels(32f)
				};
				((InventoryViewerButton)button).SetLayout(half: true);
				((InventoryViewerButton)next).SetLayout(half: true, right: true);
				row.Append(button);
				row.Append(next);
				_list.Add(row);
				i++;
			}
			else
			{
				((InventoryViewerButton)button).SetLayout(half: false);
				_list.Add(button);
			}
		}
		_list.Recalculate();
	}

	private void SpectatePlayer(bool withInventory)
	{
		if (Spectate.SpectatePlayerTarget == PlayerIndex || PlayerIndex == Main.myPlayer)
		{
			Spectate.SpectatePlayerTarget = -1;
			Spectate.ShowSpectatedInventory = false;
			SoundEngine.PlaySound(12);
		}
		else if (IsActive && Spectate.IsInsideWorld(Main.player[PlayerIndex]))
		{
			Spectate.ToggleSpectate(PlayerIndex, withInventory);
			SoundEngine.PlaySound(12);
		}
	}

	private void TeleportButton_OnLeftClick(UIMouseEvent evt, UIElement listeningElement)
	{
		if (IsActive && PlayerIndex != Main.myPlayer && PlayerIndex != 255 && PlayerInput.Triggers.JustReleased.MouseLeft && Spectate.IsInsideWorld(Main.player[PlayerIndex]))
		{
			SoundEngine.PlaySound(12);
			Spectate.TeleportToPlayer(PlayerIndex);
		}
	}

	private static void UpdateBackgroundImage()
	{
		PlayerPortrait.SetPortraitBG(BiomeBackground(OurPlayer));
	}

	/// <summary>The map background of the biome the player is in (what the portrait stands on).</summary>
	public static Asset<Texture2D> BiomeBackground(Player p)
	{
		Asset<Texture2D> portraitBG = TextureAssets.MapBGs[0];
		int num = -1;
		if (p.ZoneUnderworldHeight)
		{
			num = 2;
		}
		else if (p.ZoneDungeon)
		{
			num = 4;
		}
		else if (p.ZoneLihzhardTemple)
		{
			num = 13;
		}
		else if (p.ZoneDirtLayerHeight || p.ZoneRockLayerHeight)
		{
			if (p.ZoneHive)
			{
				num = 15;
			}
			if (p.ZoneGranite)
			{
				num = 16;
			}
			if (p.ZoneMarble)
			{
				num = 17;
			}
			else if (!p.ZoneHive && !p.ZoneGranite && !p.ZoneMarble)
			{
				num = (p.ZoneGlowshroom ? 20 : (p.ZoneCorrupt ? (p.ZoneDesert ? 39 : ((!p.ZoneSnow) ? 22 : 33)) : (p.ZoneCrimson ? (p.ZoneDesert ? 40 : ((!p.ZoneSnow) ? 23 : 34)) : (p.ZoneHallow ? (p.ZoneDesert ? 41 : ((!p.ZoneSnow) ? 21 : 35)) : (p.ZoneSnow ? 3 : (p.ZoneJungle ? 12 : (p.ZoneDesert ? 14 : ((!p.ZoneRockLayerHeight) ? 1 : 31))))))));
			}
		}
		else if (p.ZoneGlowshroom)
		{
			num = 19;
		}
		else if (p.ZoneSkyHeight)
		{
			num = 32;
		}
		else if (p.ZoneCorrupt)
		{
			num = ((!p.ZoneDesert) ? 5 : 36);
		}
		else if (p.ZoneCrimson)
		{
			num = ((!p.ZoneDesert) ? 6 : 37);
		}
		else if (p.ZoneHallow)
		{
			num = ((!p.ZoneDesert) ? 7 : 38);
		}
		else if (p.ZoneBeach)
		{
			num = 10;
		}
		else if (p.ZoneSnow)
		{
			num = 11;
		}
		else if (p.ZoneJungle)
		{
			num = 8;
		}
		else if (p.ZoneDesert)
		{
			num = 9;
		}
		else if (p.ZoneGraveyard)
		{
			num = 26;
		}
		if (num > -1 && num < TextureAssets.MapBGs.Length)
		{
			portraitBG = TextureAssets.MapBGs[num];
		}
		return portraitBG;
	}
}
