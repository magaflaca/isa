using System;
using System.Collections.Generic;
using System.IO;
using IsaClient.Core;
using IsaClient.Gba;
using Microsoft.Xna.Framework.Graphics;

namespace IsaClient.Psp;

/// <summary>
/// Save slots like the other consoles': each one keeps the exact moment and a picture of the screen, in
/// IsaClient/PSP/States (&lt;game&gt;.slotN.state + .png). The games' own saves are on the memory stick and stay there.
/// </summary>
public static class PspSaves
{
	public const int Count = 8;

	private static readonly Dictionary<int, SaveSlot> Slots = new Dictionary<int, SaveSlot>();

	/// <summary>A slot is being written (its buttons wait).</summary>
	public static int Busy = -1;

	private static string Base(int i)
	{
		PspEntry game = PspGame.Current;
		return (game == null || game.Reading) ? null : PspGame.StatePath(game.Key, "slot" + i);
	}

	public static void Forget() => Slots.Clear();

	public static SaveSlot Info(int i)
	{
		if (Slots.TryGetValue(i, out SaveSlot s))
		{
			return s;
		}
		s = new SaveSlot { Index = i };
		string path = Base(i);
		if (path == null)
		{
			return s;
		}
		if (File.Exists(path))
		{
			s.Used = true;
			s.When = File.GetLastWriteTime(path);
			s.Trainer = PspGame.Title;
		}
		Slots[i] = s;
		return s;
	}

	/// <summary>Saves the moment in slot <paramref name="i"/> (over what was there).</summary>
	public static void Save(int i)
	{
		PspRemote m = PspGame.Machine;
		string path = Base(i);
		if (m == null || path == null || Busy >= 0)
		{
			return;
		}
		Busy = i;
		m.SaveTo(path, ok =>
		{
			Busy = -1;
			Slots.Remove(i);
			PspGame.ShowToast(ok ? IsaClient.Languages.L10n.F("Saved in slot {0}", i) : IsaClient.Languages.L10n.T("Couldn't save"));
		});
	}

	/// <summary>Back to slot <paramref name="i"/>: jumps to it while playing, or turns the console on with it.</summary>
	public static void Load(int i)
	{
		string path = Base(i);
		if (path == null || !File.Exists(path))
		{
			return;
		}
		PspRemote m = PspGame.Machine;
		if (m == null)
		{
			PspGame.StartFrom(path);
			return;
		}
		m.LoadFrom(path, ok => PspGame.ShowToast(ok ? IsaClient.Languages.L10n.F("Slot {0} loaded", i) : IsaClient.Languages.L10n.T("That save doesn't fit this game")));
	}

	public static void Delete(int i)
	{
		string path = Base(i);
		if (path == null)
		{
			return;
		}
		foreach (string ext in new[] { "", ".png" })
		{
			try
			{
				if (File.Exists(path + ext))
				{
					File.Delete(path + ext);
				}
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		}
		Slots.Remove(i);
	}

	/// <summary>The slot's picture (null when it has none).</summary>
	public static Texture2D Thumb(int i)
	{
		string path = Base(i);
		return (path == null) ? null : GbaSaves.Picture(path + ".png");
	}
}
