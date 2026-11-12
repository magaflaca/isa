using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IsaClient.Core;
using Newtonsoft.Json;
using Terraria;
using Terraria.ID;

namespace IsaClient.Items;

/// <summary>
/// Keeps changed items changed. A character file only stores an item's type, stack and prefix, so anything made in the
/// Item Editor came back plain after leaving. Now:
/// <list type="bullet">
/// <item>Your own characters: every save also writes "&lt;character&gt;.plr.isa" next to the file with the changed items
/// by slot; loading the character puts the changes back on the items still in those slots.</item>
/// <item>Servers with server-side characters (the server keeps your inventory): what you carry is remembered per server
/// and character in IsaClient/SSC Items. When the server hands your character back, the changes go back on the items it
/// sends, items shown to it as another id turn back into what they are, and items the server dropped because it
/// doesn't know them (old deprecated items, IsaClient's own) come back into the slots it left empty.</item>
/// </list>
/// The server is never sent anything for this: it all happens on your side.
/// </summary>
public static class ItemKeeper
{
	public sealed class Entry
	{
		public int Slot;

		/// <summary>The id the server knows the item by (what it sends back).</summary>
		public int SentType;

		/// <summary>Brought back when the server leaves its slot empty (it doesn't know the item).</summary>
		public bool Keep;

		public ItemDesign Design;
	}

	/// <summary>Turn off to leave changed items plain (as the game does).</summary>
	[Setting("KeepChangedItems")]
	public static bool Enabled = true;

	/// <summary>Items the server can't know: deprecated ones and IsaClient's own.</summary>
	public static bool Unknown(int type)
	{
		return type > 0 && type < ItemID.Count && (ItemID.Sets.Deprecated[type] || IsaClient.Gameplay.LegacyItems.IsCustom(type));
	}

	private static bool Changed(Item item)
	{
		return item != null && !item.IsAir && (item.isaMark != null || item.modifiedByItemMods);
	}

	/// <summary>Every slot of the character worth remembering.</summary>
	public static List<Entry> Snapshot(Player player, bool withUnknown)
	{
		List<Entry> list = new List<Entry>();
		for (int slot = 0; slot < PlayerItemSlotID.Count; slot++)
		{
			Item item;
			try
			{
				item = new PlayerItemSlotID.SlotReference(player, slot).Item;
			}
			catch
			{
				continue;
			}
			if (item == null || item.IsAir)
			{
				continue;
			}
			bool changed = Changed(item);
			bool keep = withUnknown && Unknown(item.type);
			if (!changed && !keep)
			{
				continue;
			}
			ItemDesign d = ItemDesign.Capture(item);
			if (!changed)
			{
				// Only the item itself, no changes to put back.
				d = new ItemDesign { Type = item.type, Prefix = (byte)item.prefix, Stack = item.stack, Name = null };
			}
			list.Add(new Entry { Slot = slot, SentType = ItemNet.Id(item), Keep = keep, Design = d });
		}
		return list;
	}

	/// <summary>Puts a remembered entry back on the item the slot has now (same item), or into it when it's empty and kept.</summary>
	private static bool Restore(Entry e, PlayerItemSlotID.SlotReference slot, bool fillEmpty)
	{
		Item item = slot.Item;
		ItemDesign d = e.Design;
		if (d == null || d.Type <= 0 || d.Type >= ItemID.Count)
		{
			return false;
		}
		if (item != null && !item.IsAir && (item.type == d.Type || item.type == e.SentType))
		{
			int stack = item.stack;
			bool favorited = item.favorited;
			if (item.type != d.Type || item.prefix != d.Prefix)
			{
				item.SetDefaults(d.Type);
				if (d.Prefix > 0)
				{
					item.Prefix(d.Prefix);
				}
			}
			if (d.Name != null)
			{
				d.Apply(item);
			}
			item.stack = Math.Min(stack, Math.Max(1, item.maxStack));
			item.favorited = favorited;
			return true;
		}
		if (fillEmpty && e.Keep && (item == null || item.IsAir))
		{
			Item made = (d.Name != null) ? d.Make(d.Stack) : Plain(d);
			slot.Item = made;
			return true;
		}
		return false;
	}

	private static Item Plain(ItemDesign d)
	{
		Item item = new Item();
		item.SetDefaults(d.Type);
		if (d.Prefix > 0)
		{
			item.Prefix(d.Prefix);
		}
		item.stack = Math.Max(1, d.Stack);
		return item;
	}

	#region Your own characters

	private static string SidecarPath(string plrPath)
	{
		return plrPath + ".isa";
	}

	/// <summary>After the game writes a character file: the changed items go next to it.</summary>
	public static void SaveSidecar(string plrPath, Player player, bool cloud)
	{
		if (!Enabled || cloud || string.IsNullOrEmpty(plrPath) || player == null)
		{
			return;
		}
		try
		{
			List<Entry> entries = Snapshot(player, withUnknown: false);
			string path = SidecarPath(plrPath);
			if (entries.Count == 0)
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
				return;
			}
			File.WriteAllText(path, JsonConvert.SerializeObject(entries));
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	/// <summary>After the game reads a character file: the changes go back on the items still in their slots.</summary>
	public static void LoadSidecar(string plrPath, Player player, bool cloud)
	{
		if (!Enabled || cloud || string.IsNullOrEmpty(plrPath) || player == null)
		{
			return;
		}
		try
		{
			string path = SidecarPath(plrPath);
			if (!File.Exists(path))
			{
				return;
			}
			List<Entry> entries = JsonConvert.DeserializeObject<List<Entry>>(File.ReadAllText(path));
			if (entries == null)
			{
				return;
			}
			foreach (Entry e in entries)
			{
				if (e.Slot >= 0 && e.Slot < PlayerItemSlotID.Count)
				{
					Restore(e, new PlayerItemSlotID.SlotReference(player, e.Slot), fillEmpty: false);
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	#endregion

	#region Server-side characters

	private static string _key;

	private static Dictionary<int, Entry> _remembered;

	private static int _lastSlotTick;

	private static int _connectedTick;

	private static string _lastWritten;

	private static int _restored;

	private static string SscFolder => Path.Combine(ClientSettings.IsaClientFolder, "SSC Items");

	private static string KeyFile(string key)
	{
		StringBuilder name = new StringBuilder();
		foreach (char c in key)
		{
			name.Append((char.IsLetterOrDigit(c) || c == '.' || c == '-') ? c : '_');
		}
		return Path.Combine(SscFolder, name + ".json");
	}

	private static string CurrentKey()
	{
		string file = Main.ActivePlayerFileData?.Path;
		string character = !string.IsNullOrEmpty(file) ? Path.GetFileName(file) : (Main.LocalPlayer?.name ?? "player");
		return (Netplay.ServerIPText + "_" + Netplay.ListenPort + "_" + character).ToLowerInvariant();
	}

	/// <summary>A new connection: what we remember for this server is read when the server starts sending slots.</summary>
	public static void OnConnectionStarting()
	{
		_key = null;
		_remembered = null;
		_lastWritten = null;
		_restored = 0;
		_connectedTick = (int)Main.GameUpdateCount;
		_lastSlotTick = _connectedTick;
	}

	/// <summary>The server set one of your slots (server-side character): put back what we remember for it.</summary>
	public static void OnServerSlot(int slotId, PlayerItemSlotID.SlotReference slot)
	{
		if (!Enabled)
		{
			return;
		}
		_lastSlotTick = (int)Main.GameUpdateCount;
		try
		{
			if (_remembered == null)
			{
				_key = CurrentKey();
				_remembered = new Dictionary<int, Entry>();
				string file = KeyFile(_key);
				if (File.Exists(file))
				{
					List<Entry> entries = JsonConvert.DeserializeObject<List<Entry>>(File.ReadAllText(file));
					if (entries != null)
					{
						foreach (Entry e in entries)
						{
							_remembered[e.Slot] = e;
						}
					}
				}
			}
			if (_remembered.TryGetValue(slotId, out Entry entry))
			{
				// Empty slots only filled while the server is handing the character over (the first minute).
				bool handingOver = Main.GameUpdateCount - (uint)_connectedTick < 3600;
				if (Restore(entry, slot, handingOver))
				{
					_restored++;
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	public static void Update()
	{
		if (!Enabled || Main.netMode != 1 || !Main.ServerSideCharacter || Main.gameMenu || Main.LocalPlayer == null)
		{
			return;
		}
		uint now = Main.GameUpdateCount;
		// Wait until the server is done sending the character (5 s without slots), then remember every few seconds.
		if (_remembered == null || now - (uint)_lastSlotTick < 300 || now % 180 != 0)
		{
			return;
		}
		try
		{
			if (_restored > 0)
			{
				ClientChat.Info(IsaClient.Languages.L10n.F("Kept {0} of your changed or server-unknown items as they were.", _restored));
				_restored = 0;
			}
			List<Entry> entries = Snapshot(Main.LocalPlayer, withUnknown: true);
			string json = JsonConvert.SerializeObject(entries);
			if (json == _lastWritten)
			{
				return;
			}
			_lastWritten = json;
			_remembered = entries.ToDictionary(e => e.Slot);
			Directory.CreateDirectory(SscFolder);
			File.WriteAllText(KeyFile(_key ?? CurrentKey()), json);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	#endregion
}
