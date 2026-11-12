using System;
using System.Collections.Generic;
using System.IO;
using IsaClient.Core;
using IsaClient.Gba;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IsaClient.Psp;

/// <summary>What the player presses: the PSP's buttons, its stick and fast forward.</summary>
public enum PspButton
{
	Up,
	Down,
	Left,
	Right,
	StickUp,
	StickDown,
	StickLeft,
	StickRight,
	Cross,
	Circle,
	Square,
	Triangle,
	L,
	R,
	Start,
	Select,
	Fast
}

/// <summary>The PSP's own settings file (IsaClient/PSP/psp.json): the games, the window and the keys.</summary>
public static class PspConfig
{
	private static bool _loaded;

	public static string FilePath => Path.Combine(PspGame.Folder, "psp.json");

	/// <summary>The games in the library (paths of their files).</summary>
	public static readonly List<string> Games = new List<string>();

	/// <summary>Games the finder brought: its key for each one, and the game's file.</summary>
	public static readonly Dictionary<string, string> Found = new Dictionary<string, string>();

	/// <summary>Two keys per button (Keys.None = empty).</summary>
	public static readonly Keys[,] Bindings = new Keys[17, 2];

	public static readonly string[] Names = { "D-pad up", "D-pad down", "D-pad left", "D-pad right", "Stick up", "Stick down", "Stick left", "Stick right", "Cross", "Circle", "Square", "Triangle", "L", "R", "Start", "Select", "Fast forward" };

	/// <summary>The stick on WASD (most PSP games move with it), the D-pad on the arrows and the four face buttons on I J K L, laid out like the real ones.</summary>
	public static void ResetKeys()
	{
		Set(PspButton.Up, Keys.Up);
		Set(PspButton.Down, Keys.Down);
		Set(PspButton.Left, Keys.Left);
		Set(PspButton.Right, Keys.Right);
		Set(PspButton.StickUp, Keys.W);
		Set(PspButton.StickDown, Keys.S);
		Set(PspButton.StickLeft, Keys.A);
		Set(PspButton.StickRight, Keys.D);
		Set(PspButton.Cross, Keys.K);
		Set(PspButton.Circle, Keys.L);
		Set(PspButton.Square, Keys.J);
		Set(PspButton.Triangle, Keys.I);
		Set(PspButton.L, Keys.Q);
		Set(PspButton.R, Keys.E);
		Set(PspButton.Start, Keys.Enter);
		Set(PspButton.Select, Keys.Back);
		Set(PspButton.Fast, Keys.Space);
	}

	private static void Set(PspButton b, Keys key)
	{
		Bindings[(int)b, 0] = key;
		Bindings[(int)b, 1] = Keys.None;
	}

	/// <summary>Binds a key to one slot; the key leaves any other button that had it.</summary>
	public static void Bind(PspButton button, int slot, Keys key)
	{
		if (key != Keys.None)
		{
			for (int b = 0; b < Bindings.GetLength(0); b++)
			{
				for (int s = 0; s < 2; s++)
				{
					if (Bindings[b, s] == key)
					{
						Bindings[b, s] = Keys.None;
					}
				}
			}
		}
		Bindings[(int)button, slot] = key;
		Save();
	}

	public static bool Down(KeyboardState k, PspButton b)
	{
		Keys first = Bindings[(int)b, 0];
		Keys second = Bindings[(int)b, 1];
		return (first != Keys.None && k.IsKeyDown(first)) || (second != Keys.None && k.IsKeyDown(second));
	}

	public static string KeysOf(PspButton b)
	{
		string first = GbaConfig.KeyName(Bindings[(int)b, 0]);
		Keys second = Bindings[(int)b, 1];
		return (second == Keys.None) ? first : (first + " / " + GbaConfig.KeyName(second));
	}

	/// <summary>Reads the file once.</summary>
	public static void EnsureLoaded()
	{
		if (_loaded)
		{
			return;
		}
		_loaded = true;
		ResetKeys();
		try
		{
			if (!File.Exists(FilePath))
			{
				return;
			}
			JObject o = JObject.Parse(File.ReadAllText(FilePath));
			PspGame.RomPath = (string)o["RomPath"] ?? PspGame.RomPath;
			PspGame.Scale = Math.Max(2, Math.Min(4, (int?)o["Scale"] ?? PspGame.Scale));
			PspGame.Volume = Math.Max(0, Math.Min(100, (int?)o["Volume"] ?? PspGame.Volume));
			PspGame.Resume = (bool?)o["Resume"] ?? PspGame.Resume;
			PspGame.Resolution = Math.Max(1, Math.Min(3, (int?)o["Resolution"] ?? PspGame.Resolution));
			if (o["Games"] is JArray games)
			{
				foreach (JToken g in games)
				{
					string path = (string)g;
					if (!string.IsNullOrEmpty(path) && !Games.Contains(path))
					{
						Games.Add(path);
					}
				}
			}
			if (o["Found"] is JObject found)
			{
				foreach (JProperty f in found.Properties())
				{
					Found[f.Name] = (string)f.Value;
				}
			}
			if (o["Keys"] is JObject keys)
			{
				foreach (PspButton b in Enum.GetValues(typeof(PspButton)))
				{
					if (keys[b.ToString()] is JArray pair)
					{
						for (int s = 0; s < 2 && s < pair.Count; s++)
						{
							Bindings[(int)b, s] = Enum.TryParse((string)pair[s], out Keys k) ? k : Keys.None;
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	public static void Save()
	{
		try
		{
			JObject keys = new JObject();
			foreach (PspButton b in Enum.GetValues(typeof(PspButton)))
			{
				keys[b.ToString()] = new JArray(Bindings[(int)b, 0].ToString(), Bindings[(int)b, 1].ToString());
			}
			JObject o = new JObject
			{
				["RomPath"] = PspGame.RomPath ?? "",
				["Scale"] = PspGame.Scale,
				["Volume"] = PspGame.Volume,
				["Resume"] = PspGame.Resume,
				["Resolution"] = PspGame.Resolution,
				["Keys"] = keys,
				["Games"] = new JArray(Games),
				["Found"] = JObject.FromObject(Found)
			};
			Directory.CreateDirectory(PspGame.Folder);
			string tmp = FilePath + ".tmp";
			File.WriteAllText(tmp, o.ToString(Formatting.Indented));
			if (File.Exists(FilePath))
			{
				File.Delete(FilePath);
			}
			File.Move(tmp, FilePath);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}
}
