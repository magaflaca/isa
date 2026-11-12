using System;
using System.Collections.Generic;
using System.IO;
using IsaClient.Core;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IsaClient.Gba;

/// <summary>What the player presses: the console's buttons plus fast forward (X, Y and the screens swap are the DS's).</summary>
public enum GbaButton
{
	Up,
	Down,
	Left,
	Right,
	A,
	B,
	L,
	R,
	Start,
	Select,
	Fast,
	X,
	Y,
	Swap
}

/// <summary>
/// The GBA player's own settings file (IsaClient/GBA/gba.json): the ROM, the trainer, the window and the keys. Kept
/// apart from IsaConfig.json so another client (or an older one) saving its settings never forgets the ROM.
/// </summary>
public static class GbaConfig
{
	private static bool _loaded;

	public static string FilePath => Path.Combine(PokemonGame.Folder, "gba.json");

	/// <summary>The games in the library (ROM paths).</summary>
	public static readonly List<string> Games = new List<string>();

	/// <summary>Two keys per button (Keys.None = empty).</summary>
	public static readonly Keys[,] Bindings = new Keys[14, 2];

	public static readonly string[] Names = { "Up", "Down", "Left", "Right", "A", "B", "L", "R", "gba::Start", "gba::Select", "Fast forward", "X (DS)", "Y (DS)", "Swap screens (DS)" };

	/// <summary>PokeMMO's keys (WASD or arrows, Z = A, X = B), with Enter/Backspace for Start/Select and Q/E for L/R.</summary>
	public static void ResetKeys()
	{
		Set(GbaButton.Up, Keys.W, Keys.Up);
		Set(GbaButton.Down, Keys.S, Keys.Down);
		Set(GbaButton.Left, Keys.A, Keys.Left);
		Set(GbaButton.Right, Keys.D, Keys.Right);
		Set(GbaButton.A, Keys.Z, Keys.None);
		Set(GbaButton.B, Keys.X, Keys.None);
		Set(GbaButton.L, Keys.Q, Keys.None);
		Set(GbaButton.R, Keys.E, Keys.None);
		Set(GbaButton.Start, Keys.Enter, Keys.None);
		Set(GbaButton.Select, Keys.Back, Keys.None);
		Set(GbaButton.Fast, Keys.Space, Keys.None);
		Set(GbaButton.X, Keys.C, Keys.None);
		Set(GbaButton.Y, Keys.V, Keys.None);
		Set(GbaButton.Swap, Keys.Tab, Keys.None);
	}

	private static void Set(GbaButton b, Keys first, Keys second)
	{
		Bindings[(int)b, 0] = first;
		Bindings[(int)b, 1] = second;
	}

	/// <summary>Binds a key to one slot; the key leaves any other button that had it.</summary>
	public static void Bind(GbaButton button, int slot, Keys key)
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

	public static bool Down(KeyboardState k, GbaButton b)
	{
		Keys a = Bindings[(int)b, 0];
		Keys c = Bindings[(int)b, 1];
		return (a != Keys.None && k.IsKeyDown(a)) || (c != Keys.None && k.IsKeyDown(c));
	}

	/// <summary>A key's short name for the controls list.</summary>
	public static string KeyName(Keys k)
	{
		switch (k)
		{
		case Keys.None:
			return "-";
		case Keys.Back:
			return "Backspace";
		case Keys.Enter:
			return "Enter";
		case Keys.Up:
			return "↑";
		case Keys.Down:
			return "↓";
		case Keys.Left:
			return "←";
		case Keys.Right:
			return "→";
		case Keys.LeftShift:
			return "L Shift";
		case Keys.RightShift:
			return "R Shift";
		case Keys.LeftControl:
			return "L Ctrl";
		case Keys.RightControl:
			return "R Ctrl";
		case Keys.LeftAlt:
			return "L Alt";
		case Keys.RightAlt:
			return "R Alt";
		default:
		{
			string s = k.ToString();
			if (s.Length == 2 && s[0] == 'D' && char.IsDigit(s[1]))
			{
				return s.Substring(1);
			}
			return s.Replace("NumPad", "Num ").Replace("Oem", "");
		}
		}
	}

	public static string KeysOf(GbaButton b)
	{
		string a = KeyName(Bindings[(int)b, 0]);
		Keys second = Bindings[(int)b, 1];
		return (second == Keys.None) ? a : (a + " / " + KeyName(second));
	}

	/// <summary>Reads the file once (missing values keep what IsaConfig.json had, then the defaults).</summary>
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
				Save();
				return;
			}
			JObject o = JObject.Parse(File.ReadAllText(FilePath));
			PokemonGame.RomPath = (string)o["RomPath"] ?? PokemonGame.RomPath;
			PokemonGame.Character = (string)o["Character"] ?? PokemonGame.Character;
			PokemonGame.Body = Math.Max(0, Math.Min(2, (int?)o["Body"] ?? PokemonGame.Body));
			PokemonGame.Scale = Math.Max(2, Math.Min(4, (int?)o["Scale"] ?? PokemonGame.Scale));
			PokemonGame.Volume = Math.Max(0, Math.Min(100, (int?)o["Volume"] ?? PokemonGame.Volume));
			PokemonGame.Resume = (bool?)o["Resume"] ?? PokemonGame.Resume;
			PokemonGame.Smooth = (bool?)o["Smooth"] ?? PokemonGame.Smooth;
			PokemonGame.DsLayout = (DsLayout)Math.Max(0, Math.Min(2, (int?)o["DsLayout"] ?? (int)PokemonGame.DsLayout));
			PokemonGame.DsSwap = (bool?)o["DsSwap"] ?? PokemonGame.DsSwap;
			PokemonGame.DsEngineId = (string)o["DsEngine"] ?? PokemonGame.DsEngineId;
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
			if (o["Keys"] is JObject keys)
			{
				foreach (GbaButton b in Enum.GetValues(typeof(GbaButton)))
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
		// the ROM chosen before the library existed is its first game
		if (!string.IsNullOrEmpty(PokemonGame.RomPath) && !Games.Exists(g => string.Equals(g, PokemonGame.RomPath, StringComparison.OrdinalIgnoreCase)))
		{
			Games.Insert(0, PokemonGame.RomPath);
		}
	}

	public static void Save()
	{
		try
		{
			JObject keys = new JObject();
			foreach (GbaButton b in Enum.GetValues(typeof(GbaButton)))
			{
				keys[b.ToString()] = new JArray(Bindings[(int)b, 0].ToString(), Bindings[(int)b, 1].ToString());
			}
			JObject o = new JObject
			{
				["RomPath"] = PokemonGame.RomPath ?? "",
				["Character"] = PokemonGame.Character ?? "auto",
				["Body"] = PokemonGame.Body,
				["Scale"] = PokemonGame.Scale,
				["Volume"] = PokemonGame.Volume,
				["Resume"] = PokemonGame.Resume,
				["Smooth"] = PokemonGame.Smooth,
				["DsLayout"] = (int)PokemonGame.DsLayout,
				["DsSwap"] = PokemonGame.DsSwap,
				["DsEngine"] = PokemonGame.DsEngineId,
				["Keys"] = keys,
				["Games"] = new JArray(Games)
			};
			Directory.CreateDirectory(PokemonGame.Folder);
			string tmp = FilePath + ".tmp";
			File.WriteAllText(tmp, o.ToString(Formatting.Indented));
			if (File.Exists(FilePath))
			{
				File.Delete(FilePath);
			}
			File.Move(tmp, FilePath);
			ClientSettings.Save();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}
}
