using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using ReLogic.Graphics;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;
using Terraria.GameContent.UI.Chat;
using IsaClient.Core;
using IsaClient.Gameplay;
using IsaClient.Rendering;
using Terraria;

namespace IsaClient.Menu;

/// <summary>
/// The inventory viewer, opened by clicking a row of the Player List: the player's inventory, coins, ammo, held
/// mouse item, trash, armor/accessories/vanity/dyes (with their hide toggles), equipment slots, buffs and loadouts (click a
/// loadout to preview it), plus a side column with their portrait, info and actions. Previous/Next walk through every
/// player index. Everything is read-only: Shift+click copies an item into your inventory, the favorite key with chat open
/// shares it.
/// </summary>
public class InventoryViewer : UIState
{
	private static InventoryViewer _instance;

	private InventoryViewerButtons _infoButtons;

	public static int TargetPlayer = 0;

	/// <summary>What the big grid shows: 0 inventory, 1 Piggy Bank, 2 Safe, 3 Defender's Forge, 4 Void Vault.</summary>
	public static int Storage;

	internal static readonly int[] _storageIcons = { ItemID.Chest, ItemID.PiggyBank, ItemID.Safe, ItemID.DefendersForge, ItemID.VoidVault };

	internal static readonly string[] _storageNames = { "Inventory", "Piggy Bank", "Safe", "Defender's Forge", "Void Vault" };

	/// <summary>Loadout being previewed (-1 = the one they wear).</summary>
	public static int TemporaryLoadoutIndex = -1;

	private UIPanel _backPanel;

	public static bool MousedOver;

	/// <summary>What is drawn: the target's live arrays, or a copy wearing the previewed loadout.</summary>
	public static Player OurPlayer = new Player();

	public static InventoryViewer Instance => _instance ?? (_instance = new InventoryViewer());

	public bool IsActive => Main.player[TargetPlayer].active;

	/// <summary>Opens the viewer on a player (from the Player List).</summary>
	public static void Open(int index)
	{
		if (IsaClient.Networking.ServerModes.BlocksTool())
		{
			return;
		}
		TargetPlayer = Utils.Clamp(index, 0, 254);
		TemporaryLoadoutIndex = -1;
		Storage = 0;
		CloneFromIndex();
		MenuState.CustomUI2.SetState(Instance);
		Instance._infoButtons?.UpdateNameplateAndFlavorText();
	}

	/// <summary>Points the shared state at a player without opening this window (the panel's player view uses it).</summary>
	internal static void Select(int index)
	{
		TargetPlayer = Utils.Clamp(index, 0, 254);
		TemporaryLoadoutIndex = -1;
		Storage = 0;
		CloneFromIndex();
	}

	/// <summary>Drops the instance so the next open builds it again (called when the cheat panel is rebuilt).</summary>
	public static void Reset()
	{
		_instance = null;
	}

	public override void OnInitialize()
	{
		BuildPage();
	}

	private void BuildPage()
	{
		RemoveAllChildren();
		UIElement outer = new UIElement();
		outer.Width.Set(0f, 0.875f);
		outer.MaxWidth.Set(900f, 0f);
		outer.MinWidth.Set(700f, 0f);
		outer.Top.Set(120f, 0f);
		outer.Height.Set(-120f, 1f);
		outer.HAlign = 0.5f;
		Append(outer);
		UIPanel panel = new UIPanel();
		panel.Width.Set(0f, 1f);
		panel.Height.Set(-90f, 1f);
		panel.BackgroundColor = new Color(33, 43, 79) * 0.8f;
		outer.Append(panel);
		panel.PaddingTop -= 4f;
		panel.PaddingBottom -= 4f;
		int topHeight = 24;
		UIElement top = new UIElement
		{
			Width = new StyleDimension(0f, 1f),
			Height = new StyleDimension(topHeight, 0f),
			VAlign = 0f
		};
		top.SetPadding(0f);
		panel.Append(top);
		_backPanel = panel;
		InventoryViewerButtons buttons = new InventoryViewerButtons
		{
			Height = new StyleDimension(12f, 1f),
			HAlign = 1f
		};
		int gap = 4;
		UIElement bottom = new UIElement
		{
			Width = new StyleDimension(0f, 1f),
			Height = new StyleDimension(-topHeight - 6 - gap, 1f),
			VAlign = 1f,
			Top = new StyleDimension(-gap, 0f)
		};
		bottom.SetPadding(0f);
		panel.Append(bottom);
		bottom.Append(buttons);
		_infoButtons = buttons;
		AddBackAndForwardButtons(top);
		MakeExitButton(top);
	}

	private void AddBackAndForwardButtons(UIElement innerTopContainer)
	{
		UIImageButton back = new UIImageButton(Main.Assets.Request<Texture2D>("Images/UI/Bestiary/Button_Back", AssetRequestMode.ImmediateLoad));
		back.SetHoverImage(Main.Assets.Request<Texture2D>("Images/UI/Bestiary/Button_Border", AssetRequestMode.ImmediateLoad));
		back.SetVisibility(1f, 1f);
		back.OnLeftClick += delegate
		{
			Step(-1);
		};
		innerTopContainer.Append(back);
		UIImageButton forward = new UIImageButton(Main.Assets.Request<Texture2D>("Images/UI/Bestiary/Button_Forward", AssetRequestMode.ImmediateLoad))
		{
			Left = new StyleDimension(back.Width.Pixels + 1f, 0f)
		};
		forward.SetHoverImage(Main.Assets.Request<Texture2D>("Images/UI/Bestiary/Button_Border", AssetRequestMode.ImmediateLoad));
		forward.SetVisibility(1f, 1f);
		forward.OnLeftClick += delegate
		{
			Step(1);
		};
		innerTopContainer.Append(forward);
		UIPanel label = new UIPanel
		{
			Left = new StyleDimension(back.Width.Pixels + 1f + forward.Width.Pixels + 3f, 0f),
			Width = new StyleDimension(135f, 0f),
			Height = new StyleDimension(0f, 1f),
			VAlign = 0.5f
		};
		label.BackgroundColor = new Color(35, 40, 83);
		label.BorderColor = new Color(35, 40, 83);
		label.SetPadding(0f);
		innerTopContainer.Append(label);
		label.Append(new UIText(IsaClient.Languages.L10n.T("< Previous   Next >"), 0.8f)
		{
			HAlign = 0.5f,
			VAlign = 0.5f
		});
	}

	private void Step(int direction)
	{
		TemporaryLoadoutIndex = -1;
		SoundEngine.PlaySound(12);
		TargetPlayer += direction;
		if (TargetPlayer > 254)
		{
			TargetPlayer = 0;
		}
		if (TargetPlayer < 0)
		{
			TargetPlayer = 254;
		}
		CloneFromIndex();
		_infoButtons.UpdateNameplateAndFlavorText();
	}

	private void MakeExitButton(UIElement outerContainer)
	{
		InventoryViewerButton exit = new InventoryViewerButton("")
		{
			Width = new StyleDimension(80f, 0f),
			Height = new StyleDimension(0f, 1f),
			VAlign = 0.5f,
			HAlign = 0.994f
		};
		exit.OnMouseOver += delegate(UIMouseEvent evt, UIElement el)
		{
			((UIPanel)el).BackgroundColor = new Color(73, 94, 171);
			((UIPanel)el).BorderColor = Colors.FancyUIFatButtonMouseOver;
		};
		exit.OnMouseOut += delegate(UIMouseEvent evt, UIElement el)
		{
			((UIPanel)el).BackgroundColor = new Color(63, 82, 151) * 0.8f;
			((UIPanel)el).BorderColor = new Color(89, 116, 213, 255);
		};
		exit.Append(new UIText(IsaClient.Languages.L10n.T(Language.GetTextValue("UI.Back")))
		{
			VAlign = 0.5f,
			HAlign = 0.5f,
			IgnoresMouseInteraction = true
		});
		exit.OnLeftMouseDown += delegate
		{
			SoundEngine.PlaySound(11);
			MenuState.CustomUI2.SetState(CheatPanel.UiStateNewCheatPanel);
			TemporaryLoadoutIndex = -1;
		};
		outerContainer.Append(exit);
	}

	public override void Update(GameTime gameTime)
	{
		MousedOver = false;
		base.Update(gameTime);
		CloneFromIndex();
		if (_backPanel.IsMouseHovering)
		{
			Main.LocalPlayer.mouseInterface = true;
			MousedOver = true;
		}
	}

	/// <summary>
	/// Mirrors the target into <see cref="OurPlayer"/>: look, stats, zones and buffs by value, item arrays by reference (so the
	/// view is live). Previewing another loadout works on copies: the worn one is swapped out and the previewed one in,
	/// exactly as the game switches loadouts, without touching the real player.
	/// </summary>
	internal static void CloneFromIndex()
	{
		Player player = Main.player[TargetPlayer];
		Player view = OurPlayer;
		view.name = player.name;
		view.joining = player.joining;
		view.spawned = player.spawned;
		view.team = player.team;
		view.hostile = player.hostile;
		view.difficulty = player.difficulty;
		view.statLife = player.statLife;
		view.statMana = player.statMana;
		view.statLifeMax = player.statLifeMax;
		view.statLifeMax2 = player.statLifeMax2;
		view.statManaMax = player.statManaMax;
		view.statManaMax2 = player.statManaMax2;
		view.skinVariant = player.skinVariant;
		view.eyeColor = player.eyeColor;
		view.hairColor = player.hairColor;
		view.hair = player.hair;
		view.skinColor = player.skinColor;
		view.shirtColor = player.shirtColor;
		view.underShirtColor = player.underShirtColor;
		view.shoeColor = player.shoeColor;
		view.pantsColor = player.pantsColor;
		view.hairDye = player.hairDye;
		view.hairDyeColor = player.hairDyeColor;
		view.hairDyeVar = player.hairDyeVar;
		view.inventory = player.inventory;
		view.buffType = player.buffType;
		view.buffTime = player.buffTime;
		view.trashItem = player.trashItem;
		view.miscEquips = player.miscEquips;
		view.miscDyes = player.miscDyes;
		view.hideMisc = player.hideMisc;
		view.unlockedSuperCart = player.unlockedSuperCart;
		view.enabledSuperCart = player.enabledSuperCart;
		if (TemporaryLoadoutIndex >= 0 && TemporaryLoadoutIndex < player.Loadouts.Length && TemporaryLoadoutIndex != player.CurrentLoadoutIndex)
		{
			view.armor = CloneItems(player.armor);
			view.dye = CloneItems(player.dye);
			view.hideVisibleAccessory = (bool[])player.hideVisibleAccessory.Clone();
			EquipmentLoadout[] loadouts = new EquipmentLoadout[player.Loadouts.Length];
			for (int i = 0; i < loadouts.Length; i++)
			{
				loadouts[i] = new EquipmentLoadout
				{
					Armor = CloneItems(player.Loadouts[i].Armor),
					Dye = CloneItems(player.Loadouts[i].Dye),
					Hide = (bool[])player.Loadouts[i].Hide.Clone()
				};
			}
			view.Loadouts = loadouts;
			loadouts[player.CurrentLoadoutIndex].Swap(view);
			loadouts[TemporaryLoadoutIndex].Swap(view);
			view.CurrentLoadoutIndex = TemporaryLoadoutIndex;
		}
		else
		{
			view.armor = player.armor;
			view.dye = player.dye;
			view.hideVisibleAccessory = player.hideVisibleAccessory;
			view.Loadouts = player.Loadouts;
			view.CurrentLoadoutIndex = player.CurrentLoadoutIndex;
		}
		view.wereWolf = player.wereWolf;
		view.hideWolf = player.hideWolf;
		view.forceWerewolf = player.forceWerewolf;
		view.merman = player.merman;
		view.hideMerman = player.hideMerman;
		view.forceMerman = player.forceMerman;
		view.zone1 = player.zone1;
		view.zone2 = player.zone2;
		view.zone3 = player.zone3;
		view.zone4 = player.zone4;
		view.zone5 = player.zone5;
		view.extraAccessory = player.extraAccessory;
		view.usedAegisCrystal = player.usedAegisCrystal;
		view.usedAegisFruit = player.usedAegisFruit;
		view.usedArcaneCrystal = player.usedArcaneCrystal;
		view.usedAmbrosia = player.usedAmbrosia;
		view.usedGalaxyPearl = player.usedGalaxyPearl;
		view.usedGummyWorm = player.usedGummyWorm;
		view.unlockedBiomeTorches = player.unlockedBiomeTorches;
		view.UsingBiomeTorches = player.UsingBiomeTorches;
		view.UsingSuperCart = player.UsingSuperCart;
		view.ateArtisanBread = player.ateArtisanBread;
		view.head = player.head;
		view.body = player.body;
		view.legs = player.legs;
		bool previewing = TemporaryLoadoutIndex != -1;
		if (!player.active || previewing)
		{
			view.wereWolf = false;
			view.hideWolf = false;
			view.forceWerewolf = false;
			view.merman = false;
			view.hideMerman = false;
			view.forceMerman = false;
			if (!player.active)
			{
				view.zone5 = (byte)0;
			}
		}
		else
		{
			if ((view.wereWolf || view.forceWerewolf) && !view.hideWolf)
			{
				view.legs = 20;
				view.body = 21;
				view.head = 38;
			}
			if ((view.merman || view.forceMerman) && !view.hideMerman)
			{
				view.head = 39;
				view.legs = 21;
				view.body = 22;
			}
		}
		view.direction = 1;
		view.gravDir = 1f;
		view.socialIgnoreLight = true;
		view.isaAlwaysLit = true;
	}

	private static Item[] CloneItems(Item[] items)
	{
		Item[] copy = new Item[items.Length];
		for (int i = 0; i < items.Length; i++)
		{
			copy[i] = items[i].Clone();
		}
		return copy;
	}

	public override void Draw(SpriteBatch spriteBatch)
	{
		base.Draw(spriteBatch);
		float inventoryScale = Main.inventoryScale;
		Color inventoryBack = Main.inventoryBack;
		Player previousDrawPlayer = ItemSlot.IsaDrawPlayer;
		try
		{
			DrawInventoryContents(spriteBatch);
		}
		finally
		{
			ItemSlot.IsaDrawPlayer = previousDrawPlayer;
			Main.inventoryScale = inventoryScale;
			Main.inventoryBack = inventoryBack;
		}
	}

	private static Item[] StorageItems(Player owner)
	{
		switch (Storage)
		{
		case 1:
			return owner.bank.item;
		case 2:
			return owner.bank2.item;
		case 3:
			return owner.bank3.item;
		case 4:
			return owner.bank4.item;
		default:
			return null;
		}
	}

	/// <summary>Buttons above the grid: inventory or one of the four storages.</summary>
	private static void DrawStorageTabs(SpriteBatch spriteBatch, Player owner, float x, float y)
	{
		for (int i = 0; i < _storageIcons.Length; i++)
		{
			Rectangle r = new Rectangle((int)(x + i * 34f), (int)y, 30, 30);
			bool selected = Storage == i;
			bool hover = r.Contains(Main.mouseX, Main.mouseY) && !PlayerInput.IgnoreMouseInterface;
			Utils.DrawInvBG(spriteBatch, r, (selected ? new Color(90, 115, 190) : new Color(44, 57, 105)) * (hover ? 1f : 0.85f));
			int type = _storageIcons[i];
			Main.instance.LoadItem(type);
			Texture2D texture = TextureAssets.Item[type].Value;
			Rectangle frame = (Main.itemAnimations[type] != null) ? Main.itemAnimations[type].GetFrame(texture) : texture.Frame();
			float scale = Math.Min(20f / frame.Width, 20f / frame.Height);
			spriteBatch.Draw(texture, r.Center.ToVector2(), frame, Color.White, 0f, frame.Size() / 2f, scale, SpriteEffects.None, 0f);
			if (hover)
			{
				Main.LocalPlayer.mouseInterface = true;
				Main.instance.MouseText(IsaClient.Languages.L10n.T(_storageNames[i] + ((i >= 1 && TargetPlayer != Main.myPlayer && !StorageSync.Received(TargetPlayer, i)) ? "\nNot received from the server: may be empty or old." : "")), 0, 0);
				if (Main.mouseLeft && Main.mouseLeftRelease)
				{
					Main.mouseLeftRelease = false;
					Storage = i;
					SoundEngine.PlaySound(12);
				}
			}
		}
		Utils.DrawBorderString(spriteBatch, IsaClient.Languages.L10n.T(_storageNames[Storage]), new Vector2(x + _storageIcons.Length * 34f + 8f, y + 6f), Color.White, 0.85f);
	}

	private static bool Hovering(int x, int y)
	{
		return Main.mouseX >= x && (float)Main.mouseX <= (float)x + (float)TextureAssets.InventoryBack.Width() * Main.inventoryScale && Main.mouseY >= y && (float)Main.mouseY <= (float)y + (float)TextureAssets.InventoryBack.Height() * Main.inventoryScale && !PlayerInput.IgnoreMouseInterface;
	}

	/// <summary>Hover, Shift+click copy and chat share for one slot of someone else's (or your own) items. Never moves anything.</summary>
	private static void HoverSlot(Item[] inv, int context, int slot)
	{
		Main.LocalPlayer.mouseInterface = true;
		Main.instance.MouseText("", 0, 0);
		ItemSlot.OverrideHoverFavoriteAndShift(inv, context, slot);
		ItemSlot.MouseHover(inv, context, slot);
		if (Main.cursorOverride == 2)
		{
			ItemSlot.LeftClickShareInChat(inv[slot]);
		}
		if (TargetPlayer != Main.myPlayer)
		{
			ItemSlot.LeftClickSteal(inv, slot);
		}
		CheckDoubleClickSpawn(inv, slot);
	}

	private static Item[] _lastClickArray;

	private static int _lastClickSlot = -1;

	private static readonly System.Diagnostics.Stopwatch _lastClickTime = new System.Diagnostics.Stopwatch();

	/// <summary>Double click on an item: it is spawned on you (same as .i: online the server creates it, so it also works on SSC servers).</summary>
	internal static void CheckDoubleClickSpawn(Item[] inv, int slot)
	{
		if (!Main.mouseLeft || !Main.mouseLeftRelease || Main.cursorOverride == 2 || Main.cursorOverride == 7)
		{
			return;
		}
		Item item = inv[slot];
		bool second = _lastClickArray == inv && _lastClickSlot == slot && _lastClickTime.IsRunning && _lastClickTime.ElapsedMilliseconds < 450;
		if (second && item != null && !item.IsAir)
		{
			Main.mouseLeftRelease = false;
			_lastClickArray = null;
			_lastClickSlot = -1;
			_lastClickTime.Reset();
			ItemTools.SpawnItem(item.type, Math.Max(1, item.stack), item.prefix, Main.LocalPlayer.Center, Vector2.Zero);
			SoundEngine.PlaySound(7);
			ClientChat.Info("Spawned " + ItemTagHandler.GenerateTag(item) + " on you.");
			return;
		}
		_lastClickArray = inv;
		_lastClickSlot = slot;
		_lastClickTime.Restart();
	}

	/// <summary>
	/// Lays the slots out inside the free space left of the side column, scaled to fit it (instead of fixed screen positions,
	/// which overflowed on most resolutions). Left block: inventory, coins, ammo, held item, trash, buffs. Right block, from
	/// right to left like the game's equipment: loadouts, armor/accessories, vanity, dyes, equipment, equipment dyes.
	/// </summary>
	private void DrawInventoryContents(SpriteBatch spriteBatch)
	{
		Player owner = Main.player[TargetPlayer];
		Player view = OurPlayer;
		CalculatedStyle inner = _backPanel.GetInnerDimensions();
		float areaX = inner.X + 6f;
		float areaY = inner.Y + 24f + 10f;
		DrawStorageTabs(spriteBatch, owner, areaX, areaY);
		areaY += 34f;
		float areaW = inner.Width - 230f - 12f - 16f;
		float areaH = inner.Height - 24f - 10f - 8f;
		bool demonHeart = view.CanDemonHeartAccessoryBeShown();
		bool masterSlot = view.CanMasterModeAccessoryBeShown();
		int accRows = 10 - ((!demonHeart) ? 1 : 0) - ((!masterSlot) ? 1 : 0);
		// In slot steps (56 * scale): 10 inventory + 2 coins/ammo + gaps + 5 equipment columns + loadout buttons.
		const float stepsWide = 10f + 0.4f + 2f + 0.8f + 5f + 0.8f;
		float stepsHigh = (float)accRows + 0.6f + 0.6f;
		float scale = Math.Min(areaW / (stepsWide * 56f), areaH / (stepsHigh * 56f));
		scale = MathHelper.Clamp(scale, 0.4f, 0.85f);
		Main.inventoryScale = scale;
		float step = 56f * scale;
		float slotSize = (float)TextureAssets.InventoryBack.Width() * scale;
		float labelHeight = 16f;
		float gridY = areaY + labelHeight;
		DynamicSpriteFont font = FontAssets.MouseText.Value;
		Color labelColor = new Color(Main.mouseTextColor, Main.mouseTextColor, Main.mouseTextColor, Main.mouseTextColor);

		// Inventory (the selected item is drawn like the hotbar's highlighted slot), or the chosen storage.
		ItemSlot.IsaDrawPlayer = owner;
		Item[] storage = StorageItems(owner);
		for (int i = 0; i < 10 && storage != null; i++)
		{
			for (int j = 0; j < 4; j++)
			{
				int sx = (int)(areaX + (float)i * step);
				int sy = (int)(gridY + (float)j * step);
				int slot = i + j * 10;
				if (slot >= storage.Length)
				{
					continue;
				}
				if (Hovering(sx, sy))
				{
					HoverSlot(storage, 3, slot);
				}
				ItemSlot.Draw(spriteBatch, storage, 3, slot, new Vector2(sx, sy));
			}
		}
		if (storage != null && TargetPlayer != Main.myPlayer && !StorageSync.Received(TargetPlayer, Storage))
		{
			Utils.DrawBorderString(spriteBatch, IsaClient.Languages.L10n.T("Not received from the server"), new Vector2(areaX, gridY + 4f * step + 6f), new Color(154, 167, 190), 0.7f);
		}
		for (int i = 0; i < 10 && storage == null; i++)
		{
			for (int j = 0; j < 5; j++)
			{
				int sx = (int)(areaX + (float)i * step);
				int sy = (int)(gridY + (float)j * step);
				int slot = i + j * 10;
				if (Hovering(sx, sy))
				{
					HoverSlot(view.inventory, 0, slot);
				}
				ItemSlot.Draw(spriteBatch, view.inventory, (owner.selectedItem != slot) ? 1 : 13, slot, new Vector2(sx, sy));
			}
		}

		// Coins, ammo, then the held item and the trash under them.
		float coinsX = areaX + 10.4f * step;
		float ammoX = coinsX + step;
		DrawLabel(spriteBatch, font, Lang.inter[26].Value, coinsX + slotSize / 2f, areaY, slotSize + 2f, labelColor);
		DrawLabel(spriteBatch, font, Lang.inter[27].Value, ammoX + slotSize / 2f, areaY, slotSize + 2f, labelColor);
		for (int k = 0; k < 4; k++)
		{
			int cy = (int)(gridY + (float)k * step);
			if (Hovering((int)coinsX, cy))
			{
				HoverSlot(view.inventory, 1, 50 + k);
			}
			ItemSlot.Draw(spriteBatch, view.inventory, 1, 50 + k, new Vector2((int)coinsX, cy));
			if (Hovering((int)ammoX, cy))
			{
				HoverSlot(view.inventory, 2, 54 + k);
			}
			ItemSlot.Draw(spriteBatch, view.inventory, 2, 54 + k, new Vector2((int)ammoX, cy));
		}
		int lastRowY = (int)(gridY + 4f * step);
		if (owner.selectedItem == 58 || !view.inventory[58].IsAir)
		{
			if (Hovering((int)coinsX, lastRowY))
			{
				HoverSlot(view.inventory, 0, 58);
			}
			ItemSlot.Draw(spriteBatch, view.inventory, 13, 58, new Vector2((int)coinsX, lastRowY));
		}
		Item[] trash = new Item[1] { view.trashItem };
		if (Hovering((int)ammoX, lastRowY))
		{
			HoverSlot(trash, 6, 0);
		}
		ItemSlot.Draw(spriteBatch, trash, 6, 0, new Vector2((int)ammoX, lastRowY));

		// Buffs under the inventory, wrapped to its width.
		int buffX = (int)areaX;
		int buffY = (int)(gridY + 5f * step + 8f);
		int perRow = Math.Max(1, (int)((ammoX + slotSize - areaX) / 38f));
		int hoveredBuff = -1;
		int shown = 0;
		for (int b = 0; b < Player.maxBuffs; b++)
		{
			if (owner.buffType[b] != 0)
			{
				hoveredBuff = DrawBuffIconFor(owner, hoveredBuff, b, buffX + shown % perRow * 38, buffY + shown / perRow * 50);
				shown++;
			}
		}
		if (hoveredBuff >= 0)
		{
			int type = owner.buffType[hoveredBuff];
			if (type > 0)
			{
				string name = Lang.GetBuffName(type) + (InfoOverlays.ShowIDs ? (" (ID: " + type + ")") : "");
				string tooltip = Main.GetBuffTooltip(owner, type);
				Main.instance.MouseText("", 0, 0);
				if (type == 147)
				{
					Main.bannerMouseOver = true;
				}
				if (Main.meleeBuff[type])
				{
					Main.instance.MouseTextHackZoom(IsaClient.Languages.L10n.T(name), -10, 0, tooltip);
				}
				else
				{
					Main.instance.MouseTextHackZoom(IsaClient.Languages.L10n.T(name), tooltip);
				}
			}
		}

		// Equipment block, drawn as the viewed player (loadout colors, shared loadout items).
		ItemSlot.IsaDrawPlayer = view;
		float loadoutX = areaX + areaW - 34f;
		float armorX = loadoutX - 4f - slotSize;
		float vanityX = armorX - step;
		float dyeX = vanityX - step;
		float miscX = dyeX - step - step * 0.2f;
		float miscDyeX = miscX - step;
		float equipY = gridY;
		float accGap = 4f * scale;
		Color normalBack = Main.inventoryBack;
		Color lockedBack = new Color(80, 80, 80, 80);
		Texture2D tickOn = TextureAssets.InventoryTickOn.Value;
		Texture2D tickOff = TextureAssets.InventoryTickOff.Value;
		Point mouse = new Point(Main.mouseX, Main.mouseY);
		int row = -1;
		for (int m = 0; m < 10; m++)
		{
			if ((m == 8 && !demonHeart) || (m == 9 && !masterSlot))
			{
				continue;
			}
			row++;
			float rowY = equipY + (float)row * step + ((m > 2) ? accGap : 0f);
			DrawEquipSlot(spriteBatch, view.armor, (m > 2) ? 10 : 8, m, (int)armorX, (int)rowY, !view.IsItemSlotUnlockedAndUsable(m), normalBack, lockedBack);
			DrawEquipSlot(spriteBatch, view.armor, (m > 2) ? 11 : 9, m + 10, (int)vanityX, (int)rowY, !view.IsItemSlotUnlockedAndUsable(m + 10), normalBack, lockedBack);
			DrawEquipSlot(spriteBatch, view.dye, 12, m, (int)dyeX, (int)rowY, !view.IsItemSlotUnlockedAndUsable(m), normalBack, lockedBack);
			if (m > 2)
			{
				// Accessory visibility (read-only, shown like the game's eye toggle).
				Texture2D tick = view.hideVisibleAccessory[m] ? tickOff : tickOn;
				Vector2 tickPos = new Vector2(armorX + slotSize - (float)tick.Width * 0.75f, rowY - 2f);
				spriteBatch.Draw(tick, tickPos, null, Color.White * 0.7f, 0f, Vector2.Zero, 0.85f, SpriteEffects.None, 0f);
				if (new Rectangle((int)tickPos.X, (int)tickPos.Y, tick.Width, tick.Height).Contains(mouse) && !PlayerInput.IgnoreMouseInterface)
				{
					Main.LocalPlayer.mouseInterface = true;
					Main.HoverItem = new Item();
					Main.hoverItemName = Lang.inter[view.hideVisibleAccessory[m] ? 60 : 59].Value;
					Main.instance.MouseText(IsaClient.Languages.L10n.T(Main.hoverItemName), 0, 0);
				}
			}
		}
		// Equipment page: pet, light pet, minecart, mount, hook, and their dyes.
		for (int slot = 0; slot < 5; slot++)
		{
			int context = slot switch
			{
				0 => 19,
				1 => 20,
				2 => 18,
				3 => 17,
				_ => 16,
			};
			float rowY = equipY + (float)slot * step;
			DrawEquipSlot(spriteBatch, view.miscEquips, context, slot, (int)miscX, (int)rowY, locked: false, normalBack, lockedBack);
			DrawEquipSlot(spriteBatch, view.miscDyes, 33, slot, (int)miscDyeX, (int)rowY, locked: false, normalBack, lockedBack);
			if (slot < 2)
			{
				Texture2D tick = view.hideMisc[slot] ? tickOff : tickOn;
				spriteBatch.Draw(tick, new Vector2(miscX + slotSize - (float)tick.Width * 0.75f, rowY - 2f), null, Color.White * 0.7f, 0f, Vector2.Zero, 0.85f, SpriteEffects.None, 0f);
			}
			else if (slot == 2 && view.unlockedSuperCart)
			{
				Texture2D cart = view.enabledSuperCart ? TextureAssets.Extra[255].Value : TextureAssets.Extra[256].Value;
				spriteBatch.Draw(cart, new Vector2(miscX + slotSize - (float)cart.Width * 0.6f, rowY - 2f), null, Color.White, 0f, Vector2.Zero, 0.85f, SpriteEffects.None, 0f);
			}
		}

		// Loadouts: click one to preview it (click the worn one to go back).
		Texture2D loadoutTex = TextureAssets.Extra[259].Value;
		for (int li = 0; li < owner.Loadouts.Length; li++)
		{
			Rectangle button = new Rectangle((int)loadoutX, (int)(equipY + (float)(li * 36)), 32, 32);
			int frameX = (li == view.CurrentLoadoutIndex) ? 1 : 0;
			bool hovered = false;
			if (button.Contains(mouse) && !PlayerInput.IgnoreMouseInterface)
			{
				hovered = true;
				Main.LocalPlayer.mouseInterface = true;
				Main.instance.MouseText(IsaClient.Languages.L10n.T(Language.GetTextValue("UI.Loadout" + (li + 1)) + ((li == owner.CurrentLoadoutIndex) ? " (worn)" : " (click to preview)")), 0, 0);
				if (Main.mouseLeft && Main.mouseLeftRelease)
				{
					Main.mouseLeftRelease = false;
					TemporaryLoadoutIndex = (li == owner.CurrentLoadoutIndex) ? (-1) : li;
					SoundEngine.PlaySound(12);
					CloneFromIndex();
				}
			}
			Rectangle frame = loadoutTex.Frame(3, 3, frameX, li);
			spriteBatch.Draw(loadoutTex, button.Center.ToVector2(), frame, Color.White, 0f, frame.Size() / 2f, 1f, SpriteEffects.None, 0f);
			if (hovered)
			{
				frame = loadoutTex.Frame(3, 3, 2, li);
				spriteBatch.Draw(loadoutTex, button.Center.ToVector2(), frame, Main.OurFavoriteColor, 0f, frame.Size() / 2f, 1f, SpriteEffects.None, 0f);
			}
		}
	}

	private static void DrawLabel(SpriteBatch spriteBatch, DynamicSpriteFont font, string text, float centerX, float y, float maxWidth, Color color)
	{
		float width = font.MeasureString(IsaClient.Languages.L10n.T(text)).X;
		float labelScale = Math.Min(0.75f, maxWidth / Math.Max(1f, width));
		spriteBatch.DrawString(font, IsaClient.Languages.L10n.T(text), new Vector2(centerX - width * labelScale / 2f, y), color, 0f, Vector2.Zero, labelScale, SpriteEffects.None, 0f);
	}

	private static void DrawEquipSlot(SpriteBatch spriteBatch, Item[] inv, int context, int slot, int x, int y, bool locked, Color normalBack, Color lockedBack)
	{
		if (Hovering(x, y))
		{
			Main.armorHide = true;
			HoverSlot(inv, context, slot);
		}
		Main.inventoryBack = locked ? lockedBack : normalBack;
		ItemSlot.Draw(spriteBatch, inv, context, slot, new Vector2(x, y));
		Main.inventoryBack = normalBack;
	}

	/// <summary>A buff icon (and its time) of another player. Buffs of others cannot be removed.</summary>
	private static int DrawBuffIconFor(Player p, int drawBuffText, int buffSlot, int x, int y)
	{
		int type = p.buffType[buffSlot];
		if (type == 0)
		{
			return drawBuffText;
		}
		Color color = Color.White * 0.75f;
		Texture2D texture = TextureAssets.Buff[type].Value;
		Main.spriteBatch.Draw(texture, new Vector2(x, y), new Rectangle(0, 0, texture.Width, texture.Height), color, 0f, default(Vector2), 1f, SpriteEffects.None, 0f);
		int time = p.buffTime[buffSlot];
		// Only your own buff timers are real: other players' times are not sent by the server.
		bool showsTime = p.whoAmI == Main.myPlayer && !Main.vanityPet[type] && !Main.lightPet[type] && !Main.buffNoTimeDisplay[type] && (!p.honeyWet || type != 48) && (!p.wet || !Main.expertMode || type != 46);
		if (showsTime && time > 2)
		{
			string text = Lang.LocalizedDuration(new TimeSpan(0, 0, time / 60), abbreviated: true, showAllAvailableUnits: false);
			Main.spriteBatch.DrawString(FontAssets.ItemStack.Value, IsaClient.Languages.L10n.T(text), new Vector2(x, y + texture.Height), color, 0f, default(Vector2), 0.8f, SpriteEffects.None, 0f);
		}
		if (Main.mouseX < x + texture.Width && Main.mouseY < y + texture.Height && Main.mouseX > x && Main.mouseY > y && !PlayerInput.IgnoreMouseInterface)
		{
			drawBuffText = buffSlot;
			Main.LocalPlayer.mouseInterface = true;
		}
		return drawBuffText;
	}
}
