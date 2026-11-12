using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using IsaClient.Community;
using IsaClient.Core;

namespace IsaClient.Gameplay;

/// <summary>Item and buff commands (.item, .buff, .heal, .wear) and item tweaks (Zenith item, rope snakes, magical ice).</summary>
public static class ItemTools
{
	/// <summary>Item search by id, current-language name, English internal name or partial match.</summary>
	public static List<int> SearchItems(string query)
	{
		List<int> result = new List<int>();
		if (int.TryParse(query, out var id))
		{
			if (id > 0 && id < ItemID.Count)
			{
				result.Add(id);
			}
			return result;
		}
		string wanted = RainbowText.Normalize(query);
		string compact = wanted.Replace(" ", "");
		List<int> partial = new List<int>();
		for (int i = 1; i < ItemID.Count; i++)
		{
			string name = RainbowText.Normalize(Lang.GetItemNameValue(i));
			string internalName = ItemID.Search.TryGetName(i, out var n) ? n.ToLowerInvariant() : "";
			if (name == wanted || internalName == compact)
			{
				result.Add(i);
			}
			else if (name.Contains(wanted) || (internalName.Length > 0 && internalName.Contains(compact)))
			{
				partial.Add(i);
			}
		}
		return (result.Count > 0) ? result : partial;
	}

	/// <summary>.buff / .b</summary>
	internal static void BuffCommand(CommandContext command)
	{
		string[] array = command.Parts;
		if (array.Length < 2)
		{
			ClientChat.Error("Usage: .buff <id or name> [seconds, -1 = permanent]");
			return;
		}
		List<string> args = ChatCommands.SplitArguments(string.Join(" ", array.Skip(1)));
		int seconds = 30;
		if (args.Count > 1 && int.TryParse(args[args.Count - 1], out var parsed))
		{
			seconds = parsed;
			args.RemoveAt(args.Count - 1);
		}
		string query = string.Join(" ", args);
		List<int> matches = new List<int>();
		if (int.TryParse(query, out var id))
		{
			if (id > 0 && id < BuffID.Count)
			{
				matches.Add(id);
			}
		}
		else
		{
			string wanted = RainbowText.Normalize(query);
			for (int i = 1; i < BuffID.Count; i++)
			{
				string name = RainbowText.Normalize(Lang.GetBuffName(i));
				if (name == wanted)
				{
					matches.Clear();
					matches.Add(i);
					break;
				}
				if (name.Contains(wanted) || (BuffID.Search.TryGetName(i, out var internalName) && internalName.ToLowerInvariant() == wanted.Replace(" ", "")))
				{
					matches.Add(i);
				}
			}
		}
		if (matches.Count == 0)
		{
			ClientChat.Error("No buff matches '" + query + "'.");
			return;
		}
		if (matches.Count > 1)
		{
			ClientChat.Notice("Buffs matching '" + query + "':");
			ClientChat.Notice(string.Join("  ", matches.Take(20).Select((int b) => Lang.GetBuffName(b) + " (" + b + ")")) + ((matches.Count > 20) ? ("  ... +" + (matches.Count - 20)) : ""));
			return;
		}
		int buff = matches[0];
		int time = ((seconds < 0) ? 108000000 : (Math.Max(1, seconds) * 60));
		// Through IsaBuffs: infinite buffs come back after death and every buff given this way is saved with the .plr.
		if (BuffTools.Give(buff, time, seconds < 0))
		{
			ClientChat.Info("Added buff " + Lang.GetBuffName(buff) + ((seconds < 0) ? " permanently." : (" for " + seconds + " second" + ((seconds == 1) ? "" : "s") + ".")));
		}
		else
		{
			ClientChat.Error("Failed to add buff " + Lang.GetBuffName(buff) + ".");
		}
	}

	/// <summary>.item "name or id" [stack] [prefix] ["player"] [-c count] — drops the item on a player (also online).</summary>
	internal static void ItemCommand(CommandContext command)
	{
		string chatText = command.Text;
		List<string> parts = ChatCommands.SplitArguments(chatText.Substring(1));
		if (parts.Count < 2)
		{
			ClientChat.Error("Usage: .i \"name or id\" [stack] [prefix] [\"player\"] [-c count]");
			return;
		}
		string query = parts[1];
		List<string> args = parts.Skip(2).ToList();
		int count = 1;
		int dashC = args.IndexOf("-c");
		if (dashC >= 0)
		{
			if (dashC + 1 < args.Count && int.TryParse(args[dashC + 1], out var parsedCount))
			{
				count = parsedCount;
				args.RemoveAt(dashC + 1);
			}
			args.RemoveAt(dashC);
		}
		string target = null;
		if (args.Count > 0 && (!int.TryParse(args[args.Count - 1], out _) || args.Count == 3))
		{
			target = args[args.Count - 1];
			args.RemoveAt(args.Count - 1);
		}
		int stack = 1;
		int prefix = 0;
		if (args.Count > 0)
		{
			int.TryParse(args[0], out stack);
		}
		if (args.Count > 1)
		{
			int.TryParse(args[1], out prefix);
		}
		List<int> matches = SearchItems(query);
		if (matches.Count == 0)
		{
			ClientChat.Error("No item matches '" + query + "'.");
			return;
		}
		if (matches.Count > 1)
		{
			ClientChat.Notice("Items matching '" + query + "':");
			foreach (IEnumerable<int> row in matches.Take(20).Select((int v, int i) => new { v, i }).GroupBy(x => x.i / 4, x => x.v))
			{
				ClientChat.Notice(string.Join("  ", row.Select((int m) => "[i:" + m + "] " + Lang.GetItemNameValue(m) + " (" + m + ")")));
			}
			if (matches.Count > 20)
			{
				ClientChat.Error("... and " + (matches.Count - 20) + " more.");
			}
			return;
		}
		int type = matches[0];
		stack = Utils.Clamp(stack, 1, 999999);
		prefix = Utils.Clamp(prefix, 0, PrefixID.Count - 1);
		count = Utils.Clamp(count, 1, 400);
		Player player = Main.LocalPlayer;
		if (target != null)
		{
			player = ChatTools.FindPlayer(target, out var error);
			if (player == null)
			{
				ClientChat.Error(error);
				return;
			}
		}
		// Without a target the item spawns at the cursor (also in free camera / spectating, the cursor follows the view).
		Vector2 position = ((target != null) ? (player.Center - new Vector2(0f, 32f)) : Main.MouseWorld);
		if (target == null && Main.netMode == 1 && Vector2.Distance(position, Main.LocalPlayer.Center) > 125f * 16f)
		{
			ClientChat.Error("Warning: the cursor is more than ~125 tiles away from you; TShock may reject the spawn.");
		}
		int maxStack = Math.Max(1, ContentSamples.ItemsByType.TryGetValue(type, out var sample) ? sample.maxStack : 9999);
		int spawned = 0;
		for (int c = 0; c < count; c++)
		{
			Vector2 velocity = ((count > 1) ? new Vector2(Main.rand.NextFloat() * 6f - 3f, Main.rand.NextFloat() * 4f - 6f) : Vector2.Zero);
			for (int remaining = stack; remaining > 0; remaining -= Math.Min(remaining, maxStack))
			{
				int batch = Math.Min(remaining, maxStack);
				SpawnItem(type, batch, prefix, position, velocity);
				spawned += batch;
			}
		}
		string tag = ((spawned > 1) ? ("[i/s" + spawned + ":" + type + "]") : ("[i:" + type + "]"));
		ClientChat.Info("Spawned " + tag + " " + Lang.GetItemNameValue(type) + ((prefix > 0) ? (" [" + Lang.prefix[prefix].Value + "]") : "") + ((count > 1) ? (" (x" + count + " times)") : "") + ((target != null) ? (" on " + player.name) : " at the cursor") + ".");
	}

	/// <summary>.heal ["player"] [hearts]: drops hearts (50 by default) on a player, like .i 58 -c 50 "player". Without a player, on you.</summary>
	internal static void HealCommand(CommandContext command)
	{
		string chatText = command.Text;
		List<string> args = ChatCommands.SplitArguments(chatText.Substring(1)).Skip(1).ToList();
		int hearts = 50;
		if (args.Count > 0 && int.TryParse(args[args.Count - 1], out var parsed) && (args.Count > 1 || ChatTools.FindPlayer(args[0], out _) == null))
		{
			hearts = parsed;
			args.RemoveAt(args.Count - 1);
		}
		hearts = Utils.Clamp(hearts, 1, 400);
		Player player = Main.LocalPlayer;
		if (args.Count > 0)
		{
			player = ChatTools.FindPlayer(string.Join(" ", args), out var error);
			if (player == null)
			{
				ClientChat.Error(error);
				return;
			}
		}
		Heal(player, hearts);
	}

	/// <summary>Drops hearts on a player (.heal, and Heal in the panel's player view).</summary>
	internal static void Heal(Player player, int hearts)
	{
		Vector2 position = player.Center - new Vector2(0f, 32f);
		for (int i = 0; i < hearts; i++)
		{
			SpawnItem(58, 1, 0, position, new Vector2(Main.rand.NextFloat() * 6f - 3f, Main.rand.NextFloat() * 4f - 6f));
		}
		ClientChat.Info("Healing " + ((player.whoAmI == Main.myPlayer) ? "you" : player.name) + " with [i/s" + hearts + ":58] " + hearts + " hearts.");
	}

	/// <summary>Singleplayer: a normal item drop. Online: packet 21 with slot 400, which makes the server create the item.</summary>
	internal static void SpawnItem(int type, int stack, int prefix, Vector2 position, Vector2 velocity)
	{
		if (Main.netMode == 0)
		{
			int index = Item.NewItem(Main.LocalPlayer.GetItemSource_Misc(0), position, type, stack, prefix, NewItemOwnership.None, velocity);
			if (index >= 0 && index < Main.maxItems)
			{
				Main.item[index].velocity = velocity;
			}
			return;
		}
		if (!Netplay.Connection.IsActive || Netplay.Connection.Socket == null)
		{
			return;
		}
		using MemoryStream stream = new MemoryStream(32);
		using BinaryWriter writer = new BinaryWriter(stream);
		writer.Write((ushort)0);
		writer.Write((byte)21);
		writer.Write((short)400);
		writer.Write(position.X - 8f);
		writer.Write(position.Y - 8f);
		writer.Write(velocity.X);
		writer.Write(velocity.Y);
		writer.Write((short)stack);
		writer.Write((byte)prefix);
		writer.Write((byte)0);
		writer.Write((short)type);
		byte[] packet = stream.ToArray();
		byte[] length = BitConverter.GetBytes((ushort)packet.Length);
		packet[0] = length[0];
		packet[1] = length[1];
		Netplay.Connection.Socket.AsyncSend(packet, 0, packet.Length, delegate
		{
		});
	}

	/// <summary>.wear &lt;player|index|me&gt; [look|inv|gear|stats|all]   .wear restore</summary>
	internal static void WearCommand(CommandContext command)
	{
		string[] array = command.Parts;
		if (array.Length < 2)
		{
			ClientChat.Error("Usage: .wear <player name|index> [look|inv|gear|stats|all]   .wear restore");
			return;
		}
		if (array[1].Equals("restore", StringComparison.OrdinalIgnoreCase) || array[1].Equals("off", StringComparison.OrdinalIgnoreCase))
		{
			if (!CharacterPresets.Wearing)
			{
				ClientChat.Notice("You are already wearing your own character.");
				return;
			}
			CharacterPresets.Restore();
			return;
		}
		WearParts parts = WearParts.All;
		string query = array[1];
		if (array.Length > 2)
		{
			string last = array[array.Length - 1].ToLowerInvariant();
			WearParts? named = last switch
			{
				"look" => WearParts.Look,
				"appearance" => WearParts.Look,
				"inv" => WearParts.Inventory,
				"inventory" => WearParts.Inventory,
				"gear" => WearParts.Equipment,
				"equipment" => WearParts.Equipment,
				"stats" => WearParts.Stats,
				"all" => WearParts.All,
				_ => null
			};
			if (named.HasValue)
			{
				parts = named.Value;
				query = string.Join(" ", array, 1, array.Length - 2);
			}
			else
			{
				query = string.Join(" ", array, 1, array.Length - 1);
			}
		}
		Player target = CharacterPresets.FindOnline(query);
		if (target == null || target == Main.LocalPlayer)
		{
			ClientChat.Error("No other player matches '" + query + "'.");
			return;
		}
		CharacterPresets.Wear(target, parts, target.name);
	}

	public static bool KeepDeprecatedItems = true;

	public static int ZenithItem = -1;

	public static bool ReflectAllMyProjectiles;

	public static bool UnlimitedRopeSnakes;

	public static bool UnlimitedIce;

	public static bool RopeSnakeHeightOverride;

	public static int CustomRopeSnakeHeight;

	public static int FishItem;

	#region Chat commands

	/// <summary>.fitem / .fishitem / .fishi</summary>
	internal static void FishItemCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out var itemId) && itemId > 0 && itemId < ItemID.Count)
			{
				FishItem = itemId;
				ClientChat.Info(Lang.GetItemNameValue(itemId) + " (ID " + itemId + ") will now be fished up every time you reel in.");
			}
			else
			{
				ClientChat.Error("Invalid Item ID.");
			}
		}
		else
		{
			FishItem = 0;
			ClientChat.Info("No longer overriding fished up items.");
		}
	}

	/// <summary>.zitem / .zenithitem</summary>
	internal static void ZenithItemCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out var zenith) && zenith >= -1)
			{
				ZenithItem = zenith;
				if (zenith == -1)
				{
					ClientChat.Info("Zenith projectile item modifier disabled.");
				}
				else if (zenith == 0 || zenith >= ItemID.Count)
				{
					ClientChat.Error("Zenith projectile's displayed item set to ID " + zenith + ". It is not a valid item, a fallback texture will be used.");
				}
				else
				{
					ClientChat.Info("Zenith projectile's displayed item set to " + Lang.GetItemNameValue(zenith) + " (ID: " + zenith + ").");
				}
			}
			else
			{
				ClientChat.Error("Invalid item ID.");
			}
		}
		else
		{
			ClientChat.Info((ZenithItem == -1) ? "No Zenith projectile item override (-1)." : ("Current Zenith projectile item override: " + ((ZenithItem > 0 && ZenithItem < ItemID.Count) ? Lang.GetItemNameValue(ZenithItem) : "?") + " (ID: " + ZenithItem + ")."));
		}
	}

	/// <summary>.reflectall</summary>
	internal static void ReflectAllCommand(CommandContext command)
	{
		ReflectAllMyProjectiles = !ReflectAllMyProjectiles;
		ClientChat.Info(ReflectAllMyProjectiles ? "All your projectiles will now be reflected." : "Your projectiles will no longer be reflected.");
	}

	/// <summary>.infsn</summary>
	internal static void UnlimitedSnakeRopesCommand(CommandContext command)
	{
		UnlimitedRopeSnakes = !UnlimitedRopeSnakes;
		ClientChat.Info("Unlimited Snake Ropes " + (UnlimitedRopeSnakes ? "en" : "dis") + "abled.");
	}

	/// <summary>.infice</summary>
	internal static void InFIceCommand(CommandContext command)
	{
		UnlimitedIce = !UnlimitedIce;
		ClientChat.Info("Unlimited Magical Ice " + (UnlimitedIce ? "en" : "dis") + "abled.");
	}

	/// <summary>.snh / .snakeh</summary>
	internal static void SnakeHCommand(CommandContext command)
	{
		if (command.Parts.Length > 1)
		{
			if (int.TryParse(command.Parts[1], out var height) && height >= 0 && height < Main.maxTilesY)
			{
				CustomRopeSnakeHeight = height;
				RopeSnakeHeightOverride = height > 0;
				ClientChat.Info((height > 0) ? ("Custom Snake Rope height set to " + height) : "Custom Snake Rope height disabled.");
			}
			else
			{
				ClientChat.Error("Invalid Snake Rope height.");
			}
		}
		else
		{
			ClientChat.Info("Current Snake Rope height: " + CustomRopeSnakeHeight);
		}
	}

	#endregion
}
