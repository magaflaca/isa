using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using IsaClient.Core;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using Terraria;

namespace IsaClient.Gba;

/// <summary>One save slot as the slot list shows it.</summary>
public sealed class SaveSlot
{
	public int Index;

	public bool Used;

	public DateTime When;

	public string Trainer = "";
}

/// <summary>
/// Save slots like a console game's (GTA style): each one keeps the exact moment (a state), the cartridge's save at
/// that moment and a picture of the screen, so loading one brings everything back. Files in IsaClient/GBA/States:
/// &lt;game&gt;.slotN.state + .sav + .pal + .png + .json. Slot 1 is where the old quick save went.
/// </summary>
public static class GbaSaves
{
	public const int Count = 8;

	private static readonly Dictionary<int, SaveSlot> _info = new Dictionary<int, SaveSlot>();


	private static string Base(int i) => PokemonGame.StatePath("slot" + i);

	/// <summary>A slot is being written (its buttons wait).</summary>
	public static int Busy = -1;

	public static void Forget()
	{
		_info.Clear();
	}

	public static SaveSlot Info(int i)
	{
		if (_info.TryGetValue(i, out SaveSlot s))
		{
			return s;
		}
		s = new SaveSlot { Index = i };
		string state = Base(i);
		if (File.Exists(state))
		{
			s.Used = true;
			s.When = File.GetLastWriteTime(state);
			try
			{
				if (File.Exists(state + ".json"))
				{
					JObject o = JObject.Parse(File.ReadAllText(state + ".json"));
					s.Trainer = (string)o["Trainer"] ?? "";
					s.When = (DateTime?)o["When"] ?? s.When;
				}
			}
			catch
			{
			}
		}
		_info[i] = s;
		return s;
	}

	/// <summary>Saves the moment in slot <paramref name="i"/> (over what was there).</summary>
	public static void Save(int i)
	{
		GbaMachine m = PokemonGame.Machine;
		if (m == null || Busy >= 0)
		{
			return;
		}
		Busy = i;
		string path = Base(i);
		byte[] pal = PokemonGame.RunningPal;
		string trainer = PokemonGame.SupportsTrainer ? (PokemonGame.ChosenFile()?.Player?.name ?? (PokemonGame.TrainerFamily.MaleName + " / " + PokemonGame.TrainerFamily.FemaleName)) : PokemonGame.RomName;
		m.Snapshot((state, sram, frame, width) =>
		{
			bool ok = state != null && Write(path, state);
			if (ok)
			{
				if (sram != null)
				{
					Write(path + ".sav", sram);
				}
				if (pal != null)
				{
					Write(path + ".pal", pal);
				}
				SavePng(path + ".png", frame, width);
				JObject meta = new JObject { ["Trainer"] = trainer, ["When"] = DateTime.Now, ["Rom"] = PokemonGame.RomCode };
				Write(path + ".json", System.Text.Encoding.UTF8.GetBytes(meta.ToString()));
			}
			Main.QueueMainThreadAction(() =>
			{
				Busy = -1;
				_info.Remove(i);
				PokemonGame.ShowToast(ok ? IsaClient.Languages.L10n.F("Saved in slot {0}", i) : IsaClient.Languages.L10n.T("Couldn't save"));
			});
		});
	}

	/// <summary>Back to slot <paramref name="i"/>: turns the console on with it, or jumps to it while playing.</summary>
	public static void Load(int i)
	{
		string path = Base(i);
		if (!File.Exists(path))
		{
			return;
		}
		try
		{
			byte[] state = File.ReadAllBytes(path);
			byte[] sram = File.Exists(path + ".sav") ? File.ReadAllBytes(path + ".sav") : null;
			byte[] pal = File.Exists(path + ".pal") ? File.ReadAllBytes(path + ".pal") : null;
			GbaMachine m = PokemonGame.Machine;
			if (m != null)
			{
				PokemonGame.Recolor(state, pal, PokemonGame.RunningPal);
				m.Restore(state, sram, ok => Main.QueueMainThreadAction(() => PokemonGame.ShowToast(ok ? IsaClient.Languages.L10n.F("Slot {0} loaded", i) : IsaClient.Languages.L10n.T("That save doesn't fit this game"))));
			}
			else
			{
				PokemonGame.StartWith(state, sram, pal);
			}
		}
		catch (Exception ex)
		{
			PokemonGame.Problem = "Couldn't read that save: " + ex.Message;
			ClientLog.LogException(ex);
		}
	}

	public static void Delete(int i)
	{
		string path = Base(i);
		foreach (string ext in new[] { "", ".sav", ".pal", ".png", ".json" })
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
		_info.Remove(i);
	}

	/// <summary>The slot's picture (null when it has none).</summary>
	public static Texture2D Thumb(int i) => Picture(Base(i) + ".png");

	private static readonly Dictionary<string, (DateTime stamp, Texture2D tex)> _pictures = new Dictionary<string, (DateTime, Texture2D)>(StringComparer.OrdinalIgnoreCase);

	/// <summary>A PNG as a texture, kept until the file changes (null when it doesn't exist).</summary>
	public static Texture2D Picture(string png)
	{
		if (!File.Exists(png))
		{
			return null;
		}
		DateTime stamp = File.GetLastWriteTime(png);
		if (_pictures.TryGetValue(png, out var cached) && cached.stamp == stamp && cached.tex != null && !cached.tex.IsDisposed)
		{
			return cached.tex;
		}
		cached.tex?.Dispose();
		Texture2D tex = null;
		try
		{
			using Bitmap bmp = new Bitmap(png);
			int w = bmp.Width;
			int h = bmp.Height;
			BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			int[] argb = new int[w * h];
			for (int y = 0; y < h; y++)
			{
				Marshal.Copy(data.Scan0 + y * data.Stride, argb, y * w, w);
			}
			bmp.UnlockBits(data);
			Microsoft.Xna.Framework.Color[] px = new Microsoft.Xna.Framework.Color[w * h];
			for (int k = 0; k < px.Length; k++)
			{
				int c = argb[k];
				px[k] = new Microsoft.Xna.Framework.Color((c >> 16) & 255, (c >> 8) & 255, c & 255);
			}
			tex = new Texture2D(Main.instance.GraphicsDevice, w, h);
			tex.SetData(px);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		_pictures[png] = (stamp, tex);
		return tex;
	}

	/// <summary>The picture on screen as a PNG; of the DS's two screens, the top one (the bottom one when they're swapped).</summary>
	internal static void SavePng(string path, uint[] frame, int width)
	{
		try
		{
			if (frame == null || width <= 0 || frame.Length < width)
			{
				return;
			}
			int height = frame.Length / width;
			int top = 0;
			if (height == width * 3 / 2)
			{
				// both DS screens: keep the main one
				height /= 2;
				top = PokemonGame.DsSwap ? height : 0;
			}
			using Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
			BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
			int[] row = new int[bmp.Width];
			for (int y = 0; y < bmp.Height; y++)
			{
				for (int x = 0; x < bmp.Width; x++)
				{
					uint p = frame[(top + y) * bmp.Width + x];
					// XNA packs R in the low byte; GDI wants 0xAARRGGBB
					row[x] = unchecked((int)(0xFF000000u | ((p & 0xFF) << 16) | (p & 0xFF00) | ((p >> 16) & 0xFF)));
				}
				Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
			}
			bmp.UnlockBits(data);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			bmp.Save(path, ImageFormat.Png);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static bool Write(string path, byte[] data)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllBytes(path + ".tmp", data);
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			File.Move(path + ".tmp", path);
			return true;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			return false;
		}
	}
}
