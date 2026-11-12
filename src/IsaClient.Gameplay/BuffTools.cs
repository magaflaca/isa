using System;
using System.Collections.Generic;
using Terraria.ID;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Gameplay;

/// <summary>
/// Buffs you give yourself (buff picker in My Character, or .buff):
/// <list type="bullet">
/// <item>Infinite buffs keep a huge time, show no timer, come back after you die and stay until you remove them.</item>
/// <item>They are saved in the .plr like Terraria's own buffs, including the ones Terraria never saves (pets, minions...).</item>
/// <item>"Stack Buffs" (Cheats → Player) lets buffs that replace each other stay together: the Well Fed tiers and the flasks.</item>
/// </list>
/// </summary>
public static class BuffTools
{
	/// <summary>The time an infinite buff gets (about 500 hours); anything above <see cref="InfiniteThreshold"/> counts as infinite.</summary>
	public const int Infinite = 108000000;

	public const int InfiniteThreshold = Infinite / 2;

	[Setting("StackBuffs")]
	public static bool StackBuffs;

	/// <summary>Right click on a debuff in Terraria's buff bar removes it, like buffs.</summary>
	[Setting("RemoveDebuffsOnRightClick")]
	public static bool RemoveDebuffs;

	/// <summary>Infinite buffs of the local player (re-added when the game removes them, e.g. on death).</summary>
	private static readonly HashSet<int> _permanent = new HashSet<int>();

	/// <summary>Buffs given this session: saved in the .plr even when Terraria would not save that buff type.</summary>
	private static readonly HashSet<int> _given = new HashSet<int>();

	private static readonly Dictionary<int, uint> _lastReadded = new Dictionary<int, uint>();

	private static Player _owner;

	private static uint _lastSync;

	public static void SetRemoveDebuffs(bool on)
	{
		RemoveDebuffs = on;
		ClientSettings.Save();
	}

	public static void SetStack(bool on)
	{
		StackBuffs = on;
		ClientSettings.Save();
	}

	/// <summary>Player.AddBuff: buffs that normally replace each other (Well Fed tiers, flasks) may stay together.</summary>
	public static bool Stacking(Player player)
	{
		return StackBuffs && player.whoAmI == Main.myPlayer;
	}

	public static bool IsInfinite(int time)
	{
		return time >= InfiniteThreshold;
	}

	public static bool IsPermanent(int type)
	{
		return _permanent.Contains(type);
	}

	/// <summary>Gives the local player a buff for <paramref name="frames"/> (or forever). Returns false if the game refused it.</summary>
	public static bool Give(int type, int frames, bool infinite)
	{
		Player player = Main.LocalPlayer;
		if (player == null || type <= 0 || type >= BuffID.Count)
		{
			return false;
		}
		CheckOwner(player);
		int time = infinite ? Infinite : Math.Max(1, frames);
		int index = player.FindBuffIndex(type);
		if (index >= 0)
		{
			player.buffTime[index] = time;
		}
		else
		{
			player.AddBuff(type, time);
			index = player.FindBuffIndex(type);
			if (index >= 0)
			{
				player.buffTime[index] = time;
			}
		}
		if (index < 0)
		{
			return false;
		}
		_given.Add(type);
		if (infinite)
		{
			_permanent.Add(type);
		}
		else
		{
			_permanent.Remove(type);
		}
		Sync(player, force: true);
		return true;
	}

	public static void Remove(int type)
	{
		Player player = Main.LocalPlayer;
		Forget(type);
		int index = player.FindBuffIndex(type);
		if (index >= 0)
		{
			player.DelBuff(index);
			Sync(player, force: true);
		}
	}

	/// <summary>The player removed the buff (right click on the buff bar): it is no longer kept forever.</summary>
	public static void Forget(int type)
	{
		_permanent.Remove(type);
		_given.Remove(type);
	}

	/// <summary>.plr save: buffs Terraria does not save are kept when you gave them yourself or they are infinite.</summary>
	public static bool KeepOnSave(Player player, int slot)
	{
		int type = player.buffType[slot];
		if (type <= 0)
		{
			return false;
		}
		return IsInfinite(player.buffTime[slot]) || (player.whoAmI == Main.myPlayer && player == _owner && _given.Contains(type));
	}

	/// <summary>Every tick in game: infinite buffs stay topped up and come back if something removed them.</summary>
	public static void Update()
	{
		if (Main.gameMenu || Main.LocalPlayer == null || !Main.LocalPlayer.active)
		{
			return;
		}
		Player player = Main.LocalPlayer;
		CheckOwner(player);
		for (int i = 0; i < Player.maxBuffs; i++)
		{
			int type = player.buffType[i];
			if (type > 0 && IsInfinite(player.buffTime[i]))
			{
				_permanent.Add(type);
				if (player.buffTime[i] < Infinite - 60)
				{
					player.buffTime[i] = Infinite;
				}
			}
		}
		if (player.dead || _permanent.Count == 0)
		{
			return;
		}
		bool readded = false;
		foreach (int type in _permanent)
		{
			if (player.FindBuffIndex(type) >= 0)
			{
				continue;
			}
			// Something keeps removing it (a mount or pet buff without its mount / pet...): try at most twice a second.
			if (_lastReadded.TryGetValue(type, out var last) && Main.GameUpdateCount - last < 30)
			{
				continue;
			}
			_lastReadded[type] = Main.GameUpdateCount;
			player.AddBuff(type, Infinite);
			readded = true;
		}
		if (readded)
		{
			Sync(player, force: false);
		}
	}

	private static void CheckOwner(Player player)
	{
		if (_owner == player)
		{
			return;
		}
		_owner = player;
		_permanent.Clear();
		_given.Clear();
		_lastReadded.Clear();
		for (int i = 0; i < Player.maxBuffs; i++)
		{
			if (player.buffType[i] > 0 && IsInfinite(player.buffTime[i]))
			{
				_permanent.Add(player.buffType[i]);
			}
		}
	}

	private static void Sync(Player player, bool force)
	{
		if (Main.netMode != 1 || (!force && Main.GameUpdateCount - _lastSync < 60))
		{
			return;
		}
		_lastSync = Main.GameUpdateCount;
		NetMessage.SendData(50, -1, -1, null, player.whoAmI);
	}

	/// <summary>"5m", "1h 20m", "inf": the time shown on a buff.</summary>
	public static string ShortTime(int frames)
	{
		if (IsInfinite(frames))
		{
			return "inf";
		}
		int seconds = frames / 60;
		if (seconds < 60)
		{
			return seconds + "s";
		}
		if (seconds < 3600)
		{
			return seconds / 60 + "m";
		}
		return seconds / 3600 + "h" + ((seconds % 3600 >= 60) ? (" " + seconds % 3600 / 60 + "m") : "");
	}
}
