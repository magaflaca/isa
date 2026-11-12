using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Sharing;

/// <summary>What was linked in chat this session, yours and the others' (newest first), for the panel.</summary>
public static class ShareInbox
{
	public sealed class Item
	{
		public string Code;

		public ShareKind Kind;

		public string Name = "";

		public string From = "";

		public bool Mine;

		public DateTime When = DateTime.Now;

		public StyleLook Look;

		/// <summary>A received character's file (once it arrived).</summary>
		public string Path;

		/// <summary>Loaded from <see cref="Path"/> when first needed (summary, portrait).</summary>
		public Player Loaded;

		/// <summary>The load was tried (a file that can't be read isn't read again every frame).</summary>
		public bool LoadTried;

		public SchematicShare.Entry Schematic;

		private Player _mannequin;

		/// <summary>A bare character with its look (heads, portraits).</summary>
		public Player Mannequin => _mannequin ??= Look?.Mannequin();
	}

	private static readonly List<Item> _items = new List<Item>();

	public static int Version;

	public static List<Item> Items
	{
		get
		{
			lock (_items)
			{
				return _items.ToList();
			}
		}
	}

	public static Item Find(string code, ShareKind kind)
	{
		lock (_items)
		{
			return _items.FirstOrDefault(i => i.Code == code && i.Kind == kind);
		}
	}

	public static Item Remember(string code, ShareKind kind, string name, bool mine, StyleLook look = null, SchematicShare.Entry schematic = null, string from = null)
	{
		lock (_items)
		{
			Item item = _items.FirstOrDefault(i => i.Code == code && i.Kind == kind);
			if (item == null)
			{
				item = new Item { Code = code, Kind = kind };
				_items.Insert(0, item);
				if (_items.Count > 60)
				{
					_items.RemoveAt(_items.Count - 1);
				}
			}
			if (!string.IsNullOrEmpty(name))
			{
				item.Name = name;
			}
			item.Mine |= mine;
			item.Look ??= look;
			item.Schematic ??= schematic;
			if (!string.IsNullOrEmpty(from))
			{
				item.From = from;
			}
			else if (mine && string.IsNullOrEmpty(item.From))
			{
				item.From = Main.LocalPlayer?.name ?? "";
			}
			Version++;
			return item;
		}
	}

	/// <summary>A received character as a player object (loaded once from its file).</summary>
	public static Player Character(Item item)
	{
		if (item?.Path == null)
		{
			return null;
		}
		if (item.Loaded == null && !item.LoadTried)
		{
			item.LoadTried = true;
			item.Loaded = IsaClient.Gameplay.CharacterPresets.LoadFile(item.Path, out _);
		}
		return item.Loaded;
	}

	public static void Forget(Item item)
	{
		lock (_items)
		{
			_items.Remove(item);
		}
		Version++;
	}

	/// <summary>A payload that came whole through the pipe: written where it belongs.</summary>
	internal static void OnCompleted(string code, ShareKind kind, byte[] bytes)
	{
		try
		{
			ShareTransfer.Transfer t = ShareTransfer.Get(code);
			string from = (t != null && t.From >= 0 && t.From < Main.maxPlayers) ? Main.player[t.From].name : "";
			if (kind == ShareKind.Character)
			{
				Item item = Remember(code, kind, null, mine: false, from: from);
				item.Path = CharacterShare.Unpack(bytes, string.IsNullOrEmpty(item.Name) ? CharacterShare.NameIn(bytes) : item.Name, code);
				item.Loaded = null;
				item.LoadTried = false;
				Version++;
			}
			else if (kind == ShareKind.Schematic)
			{
				SchematicShare.FromPipe(code, bytes);
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}
}
