using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria.Audio;
using Terraria.IO;
using IsaClient.Building;
using IsaClient.Core;
using IsaClient.Networking;
using Terraria;

namespace IsaClient.Gameplay;

/// <summary>Which parts of a character are worn (copied onto you).</summary>
[Flags]
public enum WearParts
{
	None = 0,
	/// <summary>Hair, hair dye, skin variant and every body color.</summary>
	Look = 1,
	/// <summary>Inventory, coins and ammo (not the item on your cursor).</summary>
	Inventory = 2,
	/// <summary>Armor, accessories, vanity, dyes, equipment slots, visibility toggles and the three loadouts.</summary>
	Equipment = 4,
	/// <summary>Max life and mana and the permanent upgrades (extra accessory slot, Aegis fruit, Artisan loaf...).</summary>
	Stats = 8,
	All = 15
}

/// <summary>
/// Everything the Player List, the character window and .plr drag &amp; drop do with a whole character:
/// wear it (any parts, your name never changes, no reconnect) and restore your own one, save a copy as .plr,
/// load a .plr, and paste a character into the world as pixel art made of real blocks.
/// While you wear someone else, your character file keeps being saved with YOUR original character.
/// </summary>
public static class CharacterPresets
{
	#region Wearing

	/// <summary>Your character as it was before the first wear (deep copy). Null when you wear nothing.</summary>
	private static Player _original;

	/// <summary>The player object that is wearing (so saves of other objects, e.g. bots, are never touched).</summary>
	private static Player _wearer;

	public static bool Wearing => _original != null && _wearer == Main.LocalPlayer;

	public static string WornName { get; private set; } = "";

	/// <summary>Puts <paramref name="parts"/> of <paramref name="source"/> on you. The first wear keeps a copy of your character for Restore.</summary>
	public static void Wear(Player source, WearParts parts, string label)
	{
		if (source == null || parts == WearParts.None || Main.gameMenu)
		{
			return;
		}
		Player local = Main.LocalPlayer;
		Player from;
		try
		{
			from = (source == local) ? null : source.SafeSerializedClone();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			ClientChat.Error("Could not read that character.");
			return;
		}
		if (from == null)
		{
			return;
		}
		if (!Wearing)
		{
			_original = local.SafeSerializedClone();
			_wearer = local;
		}
		Apply(from, local, parts);
		WornName = string.IsNullOrEmpty(label) ? source.name : label;
		Sync(parts);
		SoundEngine.PlaySound(12);
		ClientChat.Info("Now wearing " + Describe(parts) + " of " + WornName + ". Restore your character any time (Player List > your row, or .wear restore).");
	}

	/// <summary>Puts your own character back on (everything, as it was before the first wear).</summary>
	public static void Restore(bool silent = false)
	{
		if (_original == null)
		{
			return;
		}
		if (_wearer == Main.LocalPlayer && !Main.gameMenu)
		{
			Apply(_original.SafeSerializedClone(), Main.LocalPlayer, WearParts.All);
			Sync(WearParts.All);
		}
		else if (_wearer != null)
		{
			Apply(_original.SafeSerializedClone(), _wearer, WearParts.All);
		}
		_original = null;
		_wearer = null;
		WornName = "";
		if (!silent)
		{
			SoundEngine.PlaySound(12);
			ClientChat.Info("Your own character is back.");
		}
	}

	/// <summary>Called when you leave a world: you always come back as yourself.</summary>
	public static void OnLeftWorld()
	{
		if (_original != null)
		{
			Restore(silent: true);
		}
	}

	/// <summary>
	/// Hooked in Player.InternalSavePlayerFile: while you wear someone else, the file gets your original character.
	/// Builds a separate object (a shallow copy with new arrays), so it is safe even from the autosave thread.
	/// </summary>
	public static Player PlayerToSave(Player player)
	{
		Player original = _original;
		if (original == null || player != _wearer)
		{
			return player;
		}
		try
		{
			Player copy = player.IsaShallowCopy();
			Apply(original, copy, WearParts.All);
			return copy;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			return player;
		}
	}

	public static string Describe(WearParts parts)
	{
		if (parts == WearParts.All)
		{
			return "everything";
		}
		List<string> names = new List<string>();
		if ((parts & WearParts.Look) != 0)
		{
			names.Add("the look");
		}
		if ((parts & WearParts.Inventory) != 0)
		{
			names.Add("the inventory");
		}
		if ((parts & WearParts.Equipment) != 0)
		{
			names.Add("the equipment");
		}
		if ((parts & WearParts.Stats) != 0)
		{
			names.Add("life & mana");
		}
		return string.Join(", ", names);
	}

	/// <summary>Copies parts from one player to another with new arrays and cloned items (never shares anything with <paramref name="from"/>).</summary>
	internal static void Apply(Player from, Player to, WearParts parts)
	{
		if ((parts & WearParts.Look) != 0)
		{
			to.hair = from.hair;
			to.hairDye = from.hairDye;
			to.hairDyeColor = from.hairDyeColor;
			to.hairDyeVar = from.hairDyeVar;
			to.skinVariant = from.skinVariant;
			to.hairColor = from.hairColor;
			to.skinColor = from.skinColor;
			to.eyeColor = from.eyeColor;
			to.shirtColor = from.shirtColor;
			to.underShirtColor = from.underShirtColor;
			to.pantsColor = from.pantsColor;
			to.shoeColor = from.shoeColor;
		}
		if ((parts & WearParts.Inventory) != 0)
		{
			Item[] inventory = CloneItems(from.inventory);
			// The item on your cursor stays yours.
			inventory[58] = to.inventory[58];
			to.inventory = inventory;
		}
		if ((parts & WearParts.Equipment) != 0)
		{
			to.armor = CloneItems(from.armor);
			to.dye = CloneItems(from.dye);
			to.miscEquips = CloneItems(from.miscEquips);
			to.miscDyes = CloneItems(from.miscDyes);
			to.hideVisibleAccessory = (bool[])from.hideVisibleAccessory.Clone();
			to.hideMisc = from.hideMisc;
			EquipmentLoadout[] loadouts = new EquipmentLoadout[from.Loadouts.Length];
			for (int i = 0; i < loadouts.Length; i++)
			{
				loadouts[i] = new EquipmentLoadout
				{
					Armor = CloneItems(from.Loadouts[i].Armor),
					Dye = CloneItems(from.Loadouts[i].Dye),
					Hide = (bool[])from.Loadouts[i].Hide.Clone()
				};
			}
			to.Loadouts = loadouts;
			to.CurrentLoadoutIndex = from.CurrentLoadoutIndex;
			to.unlockedSuperCart = from.unlockedSuperCart;
			to.enabledSuperCart = from.enabledSuperCart;
		}
		if ((parts & WearParts.Stats) != 0)
		{
			to.statLifeMax = from.statLifeMax;
			to.statManaMax = from.statManaMax;
			to.extraAccessory = from.extraAccessory;
			to.usedAegisCrystal = from.usedAegisCrystal;
			to.usedAegisFruit = from.usedAegisFruit;
			to.usedArcaneCrystal = from.usedArcaneCrystal;
			to.usedAmbrosia = from.usedAmbrosia;
			to.usedGalaxyPearl = from.usedGalaxyPearl;
			to.usedGummyWorm = from.usedGummyWorm;
			to.ateArtisanBread = from.ateArtisanBread;
			to.unlockedBiomeTorches = from.unlockedBiomeTorches;
			to.UsingBiomeTorches = from.UsingBiomeTorches;
			to.statLife = Math.Max(1, Math.Min(Math.Max(to.statLife, from.statLife), to.statLifeMax));
			to.statMana = Math.Min(Math.Max(to.statMana, from.statMana), to.statManaMax);
		}
	}

	internal static Item[] CloneItems(Item[] items)
	{
		Item[] copy = new Item[items.Length];
		for (int i = 0; i < items.Length; i++)
		{
			copy[i] = (items[i] ?? new Item()).Clone();
		}
		return copy;
	}

	/// <summary>
	/// The character editor's "Apply to me": puts the parts (and the four storages) of an edited copy on you for real
	/// (not a wear: nothing to restore) and tells the server.
	/// </summary>
	public static void ApplyToMe(Player from, WearParts parts, bool storages)
	{
		if (from == null || Main.gameMenu)
		{
			return;
		}
		Player local = Main.LocalPlayer;
		Apply(from, local, parts);
		if (storages)
		{
			local.bank.item = CloneItems(from.bank.item);
			local.bank2.item = CloneItems(from.bank2.item);
			local.bank3.item = CloneItems(from.bank3.item);
			local.bank4.item = CloneItems(from.bank4.item);
		}
		Sync(parts);
	}

	/// <summary>Tells the server (and so every player) what changed.</summary>
	private static void Sync(WearParts parts)
	{
		if (Main.netMode != 1)
		{
			return;
		}
		int me = Main.myPlayer;
		if ((parts & WearParts.Look) != 0)
		{
			NetMessage.SendData(4, -1, -1, null, me);
		}
		if ((parts & WearParts.Stats) != 0)
		{
			NetMessage.SendData(16, -1, -1, null, me);
			NetMessage.SendData(42, -1, -1, null, me);
		}
		if ((parts & (WearParts.Inventory | WearParts.Equipment)) != 0)
		{
			ServerDetection.SendInventoryManually(instant: true);
		}
		NetMessage.SendData(13, -1, -1, null, me);
	}

	#endregion

	#region Files

	/// <summary>Loads a .plr (any location). Returns null and an English reason when it cannot be used.</summary>
	public static Player LoadFile(string path, out string error)
	{
		error = null;
		try
		{
			PlayerFileData data = Player.LoadPlayer(path, cloudSave: false);
			Player player = data?.Player;
			if (player == null || player.loadStatus != 0)
			{
				error = "That .plr could not be read (corrupt, or made by a newer Terraria).";
				return null;
			}
			return player;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			error = "That .plr could not be read: " + ex.Message;
			return null;
		}
	}

	/// <summary>Copies a .plr file into your Players folder under a free name (your existing characters are never overwritten).</summary>
	public static void ImportFile(string path, Player loaded)
	{
		try
		{
			Directory.CreateDirectory(Main.PlayerPath);
			string target = Main.GetPlayerPathFromName(loaded?.name ?? Path.GetFileNameWithoutExtension(path), cloudSave: false);
			File.Copy(path, target, overwrite: false);
			// Its IsaClient items go with it.
			if (File.Exists(path + ".isa") && !File.Exists(target + ".isa"))
			{
				File.Copy(path + ".isa", target + ".isa", overwrite: false);
			}
			ClientChat.Info("Added " + Path.GetFileName(target) + " to your characters.");
			SoundEngine.PlaySound(12);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			ClientChat.Error("Could not copy the character: " + ex.Message);
		}
	}

	/// <summary>Saves a copy of any player object (online player, loaded file, yourself) to your Players folder.</summary>
	public static void SaveCopy(Player source)
	{
		if (source == null || string.IsNullOrEmpty(source.name))
		{
			return;
		}
		Player copy;
		try
		{
			copy = source.SafeSerializedClone();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			ClientChat.Error("Could not copy " + source.name + ".");
			return;
		}
		// A free file name (the game adds 2, 3... when the name is taken): existing characters are never overwritten.
		string path = Main.GetPlayerPathFromName(copy.name, cloudSave: false);
		if (WriteFile(copy, path))
		{
			ClientChat.Info("Saved " + Path.GetFileName(path) + ".");
		}
	}

	/// <summary>
	/// Writes a player object to a .plr: <paramref name="path"/> (overwritten, the old file kept as .plr.isabak) or, when
	/// null, a new file named after the character. Returns false (and says why) when it fails.
	/// </summary>
	public static bool WriteFile(Player player, string path)
	{
		if (path != null && File.Exists(path))
		{
			try
			{
				File.Copy(path, path + ".isabak", overwrite: true);
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		}
		PlayerFileData file = PlayerFileData.IsaCreateCopyFile(player, path);
		Player copy = player;
		// The game also writes your cursor / guide / reforge / research items into a character file; keep them out of the copy.
		Item mouse = Main.mouseItem;
		Item guide = Main.guideItem;
		Item reforge = Main.reforgeItem;
		Item research = Main.CreativeMenu.GetItemByIndex(0);
		try
		{
			Main.mouseItem = new Item();
			Main.guideItem = new Item();
			Main.reforgeItem = new Item();
			Main.CreativeMenu.SetItembyIndex(new Item(), 0);
			Player.IsaWritePlayerFile(file);
			return true;
		}
		catch (Exception ex2)
		{
			ClientLog.LogException(ex2);
			ClientChat.Error("Could not save " + copy.name + ".plr: " + ex2.Message);
			return false;
		}
		finally
		{
			Main.mouseItem = mouse;
			Main.guideItem = guide;
			Main.reforgeItem = reforge;
			Main.CreativeMenu.SetItembyIndex(research, 0);
		}
	}

	#endregion

	#region Pixel art

	/// <summary>Each art pixel of the sprite becomes Scale x Scale tiles (1 = the character's real pixel size).</summary>
	[Setting("PixelArtSize", Min = 1, Max = 4)]
	public static int ArtScale = 1;

	/// <summary>Blocks (the outline, and any block used inside) are pasted actuated: actuator placed, toggled and removed, so you can walk through the art. The game draws actuated blocks at 40% brightness; colors are matched with that in mind.</summary>
	[Setting("PixelArtWalkThrough")]
	public static bool ArtWalkThrough = true;

	/// <summary>Illuminant Coating on every block: the art ignores darkness.</summary>
	[Setting("PixelArtGlow")]
	public static bool ArtGlow = true;

	private static Player _pendingArt;

	private static string _pendingArtName;

	/// <summary>Queues a pixel-art paste of a character; it is drawn and converted on the next game update (not mid-frame).</summary>
	public static void RequestPixelArt(Player source, string label)
	{
		if (source == null || Main.gameMenu)
		{
			return;
		}
		try
		{
			_pendingArt = source.SafeSerializedClone();
			_pendingArtName = string.IsNullOrEmpty(label) ? source.name : label;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	/// <summary>Per game update (main thread, outside drawing).</summary>
	public static void Update()
	{
		if (_pendingArt == null)
		{
			return;
		}
		Player source = _pendingArt;
		string name = _pendingArtName;
		_pendingArt = null;
		if (Main.gameMenu)
		{
			return;
		}
		try
		{
			Color[] pixels = CharacterArt.Capture(source, out int width, out int height);
			if (pixels == null)
			{
				ClientChat.Error("Nothing to draw for " + name + ".");
				return;
			}
			CharacterArt.LoadIntoPaste(pixels, width, height, ArtScale, ArtWalkThrough, ArtGlow);
			ClientChat.Info("Pixel art of " + name + " ready to paste (" + Selection.SchematicSizeX + "x" + Selection.SchematicSizeY + " blocks" + (ArtWalkThrough ? ", walk-through" : "") + (ArtGlow ? ", glowing" : "") + "). Left click in the world to place it; Ctrl+Z undoes it.");
			SoundEngine.PlaySound(12);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			ClientChat.Error("Could not make the pixel art: " + ex.Message);
		}
	}

	public static string ArtOptionsText()
	{
		return "x" + ArtScale + (ArtWalkThrough ? ", walk-through blocks" : ", solid blocks") + (ArtGlow ? ", glowing" : "");
	}

	#endregion

	#region Finding players

	/// <summary>An online player by index or (part of) the name, or yourself for "me".</summary>
	public static Player FindOnline(string query)
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			return null;
		}
		query = query.Trim();
		if (query.Equals("me", StringComparison.OrdinalIgnoreCase))
		{
			return Main.LocalPlayer;
		}
		if (int.TryParse(query, out int index) && index >= 0 && index < 255 && Main.player[index].active)
		{
			return Main.player[index];
		}
		Player partial = null;
		for (int i = 0; i < 255; i++)
		{
			Player p = Main.player[i];
			if (!p.active || string.IsNullOrEmpty(p.name))
			{
				continue;
			}
			if (p.name.Equals(query, StringComparison.OrdinalIgnoreCase))
			{
				return p;
			}
			if (partial == null && p.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				partial = p;
			}
		}
		return partial;
	}

	#endregion
}
