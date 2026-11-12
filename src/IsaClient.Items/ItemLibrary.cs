using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IsaClient.Core;
using IsaClient.Gameplay;
using IsaClient.Identity;
using Newtonsoft.Json;
using Terraria;

namespace IsaClient.Items;

/// <summary>
/// My Items: the IsaClient items you keep, yours and the ones other players shared with you, each one a JSON in
/// IsaClient/Items. They can be taken again from the Journey menu (its IsaClient tab) or the panel.
/// </summary>
public static class ItemLibrary
{
	private static List<ItemDesign> _items;

	/// <summary>Bumped on every change (pages and the Journey grid rebuild when it moves).</summary>
	public static int Version { get; private set; }

	public static string Folder => Path.Combine(ClientSettings.IsaClientFolder, "Items");

	public static List<ItemDesign> All
	{
		get
		{
			if (_items == null)
			{
				Load();
			}
			return _items;
		}
	}

	private static void Load()
	{
		_items = new List<ItemDesign>();
		try
		{
			if (!Directory.Exists(Folder))
			{
				return;
			}
			foreach (string file in Directory.GetFiles(Folder, "*.json"))
			{
				try
				{
					ItemDesign d = JsonConvert.DeserializeObject<ItemDesign>(File.ReadAllText(file));
					if (d != null && d.Mark != null && !string.IsNullOrEmpty(d.Mark.Id) && d.Type > 0 && d.Type < Terraria.ID.ItemID.Count)
					{
						_items.Add(d);
					}
				}
				catch (Exception ex)
				{
					ClientLog.LogException(ex);
				}
			}
			_items.Sort((a, b) => b.Updated.CompareTo(a.Updated));
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		Version++;
	}

	public static ItemDesign Get(string id)
	{
		return (id == null) ? null : All.FirstOrDefault(d => d.Mark.Id == id);
	}

	public static bool Has(string id)
	{
		return Get(id) != null;
	}

	/// <summary>Keeps a design (a new one, or the new version of one already kept).</summary>
	public static void Save(ItemDesign design)
	{
		if (design?.Mark == null)
		{
			return;
		}
		design.Updated = ItemDesign.Now;
		if (design.Created == 0)
		{
			design.Created = design.Updated;
		}
		All.RemoveAll(d => d.Mark.Id == design.Mark.Id);
		All.Insert(0, design);
		try
		{
			Directory.CreateDirectory(Folder);
			File.WriteAllText(Path.Combine(Folder, design.Mark.Id + ".json"), JsonConvert.SerializeObject(design, Formatting.Indented));
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			ClientChat.Error("Could not save the item: " + ex.Message);
		}
		Version++;
	}

	public static void Delete(string id)
	{
		All.RemoveAll(d => d.Mark.Id == id);
		try
		{
			string file = Path.Combine(Folder, id + ".json");
			if (File.Exists(file))
			{
				File.Delete(file);
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		Version++;
	}

	/// <summary>Items in the Journey menu: one per design.</summary>
	public static List<Item> JourneyItems()
	{
		if (_journeyVersion != Version || _journey == null)
		{
			_journeyVersion = Version;
			_journey = new List<Item>();
			foreach (ItemDesign d in All)
			{
				try
				{
					Item item = d.Make(1);
					if (!item.IsAir)
					{
						_journey.Add(item);
					}
				}
				catch
				{
				}
			}
		}
		return _journey;
	}

	private static List<Item> _journey;

	private static int _journeyVersion = -1;

	/// <summary>Every item of every older item preset becomes a design here (kept as they were). How many were added.</summary>
	public static int ImportOldPresets()
	{
		int added = 0;
		ItemModPresets.RefreshAllPresets();
		foreach (ItemModPreset preset in ItemModPresets.Presets)
		{
			foreach (ItemModPresets.ItemModData data in preset.Items)
			{
				try
				{
					Item item = new Item();
					item.SetDefaults(data.NetID);
					if (item.IsAir)
					{
						continue;
					}
					data.ApplyToItem(item, preset.Name);
					ItemDesign d = ItemDesign.Capture(item);
					d.LegacyPreset = null;
					d.Mark = ItemMark.New();
					d.Mark.Title = (preset.Name + " - " + d.ItemName);
					if (d.Mark.Title.Length > ItemMark.MaxTitle)
					{
						d.Mark.Title = d.Mark.Title.Substring(0, ItemMark.MaxTitle);
					}
					if (!string.IsNullOrWhiteSpace(preset.Author) && preset.Author != ItemModPreset.DefaultAuthor && preset.Author != "None")
					{
						d.Mark.Creator = preset.Author;
						d.Mark.CreatorId = 0;
					}
					bool same = All.Any(o => o.Type == d.Type && o.Mark.Title == d.Mark.Title);
					if (!same)
					{
						Save(d);
						added++;
					}
				}
				catch (Exception ex)
				{
					ClientLog.LogException(ex);
				}
			}
		}
		return added;
	}
}

/// <summary>
/// The latest name seen for each IsaClient ID (yours included), so an item shows its creator's current name even when
/// they changed it. Kept in IsaClient/names.json.
/// </summary>
public static class CreatorNames
{
	private static Dictionary<string, string> _names;

	private static bool _dirty;

	private static int _saveIn;

	private static string FilePath => Path.Combine(ClientSettings.IsaClientFolder, "names.json");

	private static Dictionary<string, string> Names
	{
		get
		{
			if (_names == null)
			{
				_names = new Dictionary<string, string>();
				try
				{
					if (File.Exists(FilePath))
					{
						_names = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? new Dictionary<string, string>();
					}
				}
				catch
				{
				}
			}
			return _names;
		}
	}

	public static string Of(ulong id, string fallback)
	{
		if (id == 0)
		{
			return string.IsNullOrEmpty(fallback) ? "?" : fallback;
		}
		int player = ClientIdentity.PlayerWith(id);
		if (player >= 0 && player < Main.maxPlayers && Main.player[player].active && !string.IsNullOrEmpty(Main.player[player].name))
		{
			return Main.player[player].name;
		}
		if (Names.TryGetValue(ClientIdentity.Hex(id), out string name) && !string.IsNullOrEmpty(name))
		{
			return name;
		}
		return string.IsNullOrEmpty(fallback) ? "?" : fallback;
	}

	public static void Seen(ulong id, string name)
	{
		if (id == 0 || string.IsNullOrWhiteSpace(name))
		{
			return;
		}
		string key = ClientIdentity.Hex(id);
		if (!Names.TryGetValue(key, out string old) || old != name)
		{
			Names[key] = name;
			_dirty = true;
			_saveIn = 120;
		}
	}

	private static bool _hooked;

	public static void Update()
	{
		if (!_hooked)
		{
			_hooked = true;
			ClientIdentity.PeerSeen += (player, id) =>
			{
				if (player >= 0 && player < Main.maxPlayers)
				{
					Seen(id, Main.player[player].name);
				}
			};
		}
		if (!Main.gameMenu && Main.LocalPlayer != null && Main.GameUpdateCount % 300 == 0)
		{
			Seen(ClientIdentity.Id, Main.LocalPlayer.name);
		}
		if (_dirty && --_saveIn <= 0)
		{
			_dirty = false;
			try
			{
				Directory.CreateDirectory(ClientSettings.IsaClientFolder);
				File.WriteAllText(FilePath, JsonConvert.SerializeObject(Names, Formatting.Indented));
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		}
	}
}
