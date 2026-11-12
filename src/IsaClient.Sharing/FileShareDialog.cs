using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using IsaClient.Core;
using IsaClient.Gameplay;
using IsaClient.Menu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;

namespace IsaClient.Sharing;

/// <summary>
/// What to do with a file from the chat, drawn like the style and schematic windows (by
/// <see cref="PlayerFileDropDialog"/>, which keeps the game from seeing the clicks). Every file can be shown in its
/// folder or sent on; some get their own action: rainbow letters can be added (all, or the ones picked in the grid),
/// IsaClient items kept, a tilemap pasted, a world or a character opened.
/// </summary>
public static class FileShareDialog
{
	private static FileShare.Entry _entry;

	/// <summary>The letters of a rainbow file, read once it's here.</summary>
	private static List<RainbowGlyph> _letters;

	private static readonly HashSet<char> _picked = new HashSet<char>();

	private static float _scroll;

	public static bool IsOpen => _entry != null;

	public static void Open(FileShare.Entry entry)
	{
		if (entry == null)
		{
			return;
		}
		PlayerFileDropDialog.Close();
		_entry = entry;
		_letters = null;
		_picked.Clear();
		_scroll = 0f;
		SoundEngine.PlaySound(10);
	}

	public static void Close()
	{
		_entry = null;
		_letters = null;
	}

	public static void Draw(SpriteBatch sb, int screenW, int screenH)
	{
		FileShare.Entry e = _entry;
		if (e == null)
		{
			return;
		}
		FileType type = e.Type;
		bool rainbow = type == FileType.RainbowLetters && e.Here;
		if (rainbow && _letters == null)
		{
			try
			{
				_letters = RainbowFont.Parse(File.ReadAllText(e.Path));
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
				_letters = new List<RainbowGlyph>();
			}
		}
		// Room for the file row, the letters and their buttons, and the two rows of buttons.
		int height = rainbow ? 460 : 268;
		int width = Math.Min(rainbow ? 600 : 520, screenW - 32);
		Rectangle panel = new Rectangle((screenW - width) / 2, (screenH - height) / 2, width, height);
		UiKit.Panel(sb, panel, new Color(33, 43, 79) * 0.97f);
		Utils.DrawBorderStringBig(sb, IsaClient.Languages.L10n.T(FileShare.TypeName(type)), new Vector2(panel.Center.X, panel.Y + 28), new Color(255, 231, 69), 0.5f, 0.5f, 0.5f);
		// The file: icon, name, size, state.
		Rectangle iconBox = new Rectangle(panel.X + 24, panel.Y + 56, 64, 64);
		Utils.DrawInvBG(sb, iconBox, new Color(20, 26, 50) * 0.9f);
		FileCard.DrawIcon(sb, type, new Rectangle(iconBox.X + 8, iconBox.Y + 8, iconBox.Width - 16, iconBox.Height - 16), 2f, 1f);
		float textX = iconBox.Right + 16;
		float textW = panel.Right - 20 - textX;
		UiKit.Text(sb, FileCard.Title(e), textX, iconBox.Y + 12, Color.White, 1f, textW);
		string state = FileCard.Status(e, out float bar);
		if (e.Error != null)
		{
			state = "[c/FF8C8C:" + IsaClient.Languages.L10n.T(e.Mine ? "Upload failed" : "Couldn't get it") + ": " + e.Error + "]";
		}
		UiKit.Text(sb, IsaClient.Languages.L10n.T(FileShare.TypeName(type)) + "  ·  " + state, textX, iconBox.Y + 36, UiKit.Muted, 0.75f, textW);
		if (bar >= 0f)
		{
			Rectangle track = new Rectangle((int)textX, iconBox.Y + 52, (int)textW, 6);
			sb.Draw(TextureAssets.MagicPixel.Value, track, Color.Black * 0.5f);
			sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(track.X, track.Y, (int)(track.Width * MathHelper.Clamp(bar, 0f, 1f)), track.Height), new Color(120, 230, 140));
		}
		int y = iconBox.Bottom + 14;
		if (rainbow)
		{
			y = DrawLetters(sb, new Rectangle(panel.X + 24, y, panel.Width - 48, 160)) + 12;
		}
		else
		{
			UiKit.Text(sb, "[c/9AA7BE:" + IsaClient.Languages.L10n.T(Hint(e, type)) + "]", panel.X + 24, y + 8, Color.White, 0.72f, panel.Width - 48);
			y += 30;
		}
		int bw = (panel.Width - 48 - 10) / 2;
		// The action of this kind of file.
		(string Label, string Tip, Action Run)? main = ActionFor(e, type);
		if (main.HasValue)
		{
			if (UiKit.Button(sb, new Rectangle(panel.X + 24, y, bw, 38), main.Value.Label, UiKit.Green, e.Here && !Main.gameMenu, main.Value.Tip))
			{
				main.Value.Run();
				return;
			}
		}
		else if (UiKit.Button(sb, new Rectangle(panel.X + 24, y, bw, 38), e.Here ? "Open" : "Download", UiKit.Green, (e.Here || (!e.Busy && !e.Mine)), e.Here ? "Opens it with the program Windows uses for it." : "Gets the file into IsaClient/Received/Files."))
		{
			if (e.Here)
			{
				OpenFile(e.Path, type);
			}
			else
			{
				FileShare.Download(e);
			}
		}
		if (UiKit.Button(sb, new Rectangle(panel.X + 34 + bw, y, bw, 38), "Show in folder", UiKit.Blue, e.Here, "Opens its folder with the file selected."))
		{
			ShowInFolder(e.Path);
		}
		y += 46;
		if (UiKit.Button(sb, new Rectangle(panel.X + 24, y, bw, 38), "Send in chat", UiKit.Purple, e.Here && !Main.gameMenu, "Links it in the chat again."))
		{
			string path = e.Path;
			Close();
			FileShare.Attach(path);
			return;
		}
		if (UiKit.Button(sb, new Rectangle(panel.X + 34 + bw, y, bw, 38), "Close", UiKit.Red))
		{
			Close();
		}
		UiKit.EndFrame();
		if (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape))
		{
			Close();
		}
	}

	private static string Hint(FileShare.Entry e, FileType type)
	{
		if (!e.Here)
		{
			return e.Mine ? "Your file." : "It comes as pictures from Imgur, checked byte by byte.";
		}
		return type switch
		{
			FileType.Item => "IsaClient items: keep them in My Items.",
			FileType.TileMap => "A Pixel Art Generator tilemap: paste it like a schematic.",
			FileType.World => "A world: open it from here.",
			FileType.Character => "A character file.",
			FileType.Schematic => "A schematic: paste it like dropping the file on the game.",
			_ => "Saved in IsaClient/Received/Files."
		};
	}

	/// <summary>The action of a kind of file (null: just open it).</summary>
	private static (string, string, Action)? ActionFor(FileShare.Entry e, FileType type)
	{
		string path = e.Path;
		switch (type)
		{
		case FileType.Item:
			return ("Add to My Items", "Keeps the items of this file in My Items.", delegate
			{
				try
				{
					List<IsaClient.Items.ItemDesign> designs = IsaClient.Items.ItemFiles.Read(File.ReadAllText(path));
					foreach (IsaClient.Items.ItemDesign d in designs)
					{
						IsaClient.Items.ItemLibrary.Save(d);
					}
					ClientChat.Info(IsaClient.Languages.L10n.F("Imported {0} items into My Items.", designs.Count));
				}
				catch (Exception ex)
				{
					ClientChat.Error("Could not read the items: " + ex.Message);
				}
				Close();
			});
		case FileType.TileMap:
			return ("Paste it", "Loads it to paste (left click in the world).", delegate
			{
				Close();
				IsaClient.Building.TileMapFiles.Open(path);
			});
		case FileType.Schematic:
			return ("Paste it", "Loads it to paste, like dropping the file on the game.", delegate
			{
				Close();
				IsaClient.Building.SchematicFiles.ForcedDialogPath = path;
				IsaClient.Building.SchematicFiles.Load();
			});
		case FileType.World:
			return ("Open world", "Shows what you can do with this world.", delegate
			{
				Close();
				PlayerFileDropDialog.OpenWorld(path);
			});
		case FileType.Character:
			return ("Open character", "Wear it, save it, paste it as pixel art...", delegate
			{
				Close();
				PlayerFileDropDialog.Open(path);
			});
		case FileType.RainbowLetters:
			return null;
		default:
			return null;
		}
	}

	#region Rainbow letters

	/// <summary>The letters as a grid (click to pick), and the buttons that add them.</summary>
	private static int DrawLetters(SpriteBatch sb, Rectangle area)
	{
		List<RainbowGlyph> letters = _letters ?? new List<RainbowGlyph>();
		Utils.DrawInvBG(sb, area, new Color(20, 26, 50) * 0.9f);
		const int cell = 48;
		const int gap = 6;
		Rectangle inner = new Rectangle(area.X + 8, area.Y + 8, area.Width - 16, area.Height - 16);
		int columns = Math.Max(1, (inner.Width + gap) / (cell + gap));
		int rows = (letters.Count + columns - 1) / columns;
		float content = rows * (cell + gap);
		if (inner.Contains(UiKit.Mouse) && PlayerInputWheel(out int wheel))
		{
			_scroll -= wheel / 2f;
		}
		_scroll = MathHelper.Clamp(_scroll, 0f, Math.Max(0f, content - inner.Height));
		for (int i = 0; i < letters.Count; i++)
		{
			int row = i / columns;
			int col = i % columns;
			Rectangle r = new Rectangle(inner.X + col * (cell + gap), inner.Y + row * (cell + gap) - (int)_scroll, cell, cell);
			if (r.Y < inner.Y - 1 || r.Bottom > inner.Bottom + 1)
			{
				continue;
			}
			RainbowGlyph glyph = letters[i];
			bool picked = _picked.Contains(glyph.Char);
			bool hover = r.Contains(UiKit.Mouse);
			Utils.DrawInvBG(sb, r, (picked ? new Color(110, 80, 170) : (hover ? new Color(60, 70, 120) : new Color(40, 48, 90))) * 0.95f);
			DrawGlyph(sb, glyph, r);
			Utils.DrawBorderString(sb, glyph.Char.ToString(), new Vector2(r.X + 4, r.Y + 2), RainbowFont.IsMine(glyph.Char) ? new Color(255, 200, 120) : Color.White * 0.8f, 0.6f);
			if (hover)
			{
				Main.LocalPlayer.mouseInterface = true;
				Main.instance.MouseText(IsaClient.Languages.L10n.F("\"{0}\": click to pick it", glyph.Char) + (RainbowFont.IsMine(glyph.Char) ? ("\n" + IsaClient.Languages.L10n.T("You have your own version: adding it replaces yours.")) : ""), 0, 0);
				if (UiKit.Clicked)
				{
					Main.mouseLeftRelease = false;
					SoundEngine.PlaySound(12);
					if (!_picked.Remove(glyph.Char))
					{
						_picked.Add(glyph.Char);
					}
				}
			}
		}
		int y = area.Bottom + 8;
		int quarter = (area.Width - 18) / 4;
		bool any = _picked.Count > 0;
		List<RainbowGlyph> chosen = any ? letters.Where(g => _picked.Contains(g.Char)).ToList() : letters;
		if (UiKit.Button(sb, new Rectangle(area.X, y, quarter, 34), IsaClient.Languages.L10n.F("Add picked ({0})", _picked.Count), UiKit.Green, any, "Adds the letters you picked to your rainbow font (Experimental > Rainbow)."))
		{
			Add(chosen);
		}
		if (UiKit.Button(sb, new Rectangle(area.X + quarter + 6, y, quarter, 34), IsaClient.Languages.L10n.F("Add all ({0})", letters.Count), UiKit.Green, letters.Count > 0, "Adds every letter of this file to your rainbow font."))
		{
			Add(letters);
		}
		if (UiKit.Button(sb, new Rectangle(area.X + (quarter + 6) * 2, y, quarter, 34), any ? "Write picked" : "Write all", UiKit.Amber, letters.Count > 0 && !Main.gameMenu, "Writes them in the world with rainbows, as .rainbow does (where .rainbow writes), without adding them to your font."))
		{
			if (RainbowText.Draw(chosen))
			{
				Close();
				return y + 34;
			}
		}
		if (UiKit.Button(sb, new Rectangle(area.X + (quarter + 6) * 3, y, area.Width - (quarter + 6) * 3, 34), any ? "Clear picks" : "Pick all", UiKit.Blue, letters.Count > 0))
		{
			if (any)
			{
				_picked.Clear();
			}
			else
			{
				foreach (RainbowGlyph g in letters)
				{
					_picked.Add(g.Char);
				}
			}
		}
		return y + 34;
	}

	private static bool PlayerInputWheel(out int wheel)
	{
		wheel = Terraria.GameInput.PlayerInput.ScrollWheelDelta;
		if (wheel == 0)
		{
			return false;
		}
		Terraria.GameInput.PlayerInput.ScrollWheelDelta = 0;
		return true;
	}

	private static void Add(IEnumerable<RainbowGlyph> glyphs)
	{
		List<RainbowGlyph> list = glyphs.ToList();
		if (list.Count == 0)
		{
			return;
		}
		int added = RainbowFont.Import(RainbowFont.Write(list));
		ClientChat.Info(IsaClient.Languages.L10n.F("Added {0} rainbow letters: {1}", added, new string(list.Select(g => g.Char).ToArray())));
		_picked.Clear();
	}

	/// <summary>A letter as the rainbows that draw it, small and colored along the way.</summary>
	private static void DrawGlyph(SpriteBatch sb, RainbowGlyph glyph, Rectangle r)
	{
		List<Vector4> points;
		try
		{
			points = RainbowFont.Points(glyph);
		}
		catch
		{
			return;
		}
		float scale = (r.Height - 10) / 150f;
		Vector2 center = new Vector2(r.Center.X, r.Center.Y + 2);
		for (int i = 0; i < points.Count; i++)
		{
			Color c = Main.hslToRgb((float)i / Math.Max(1, points.Count) * 0.85f, 1f, 0.62f);
			Vector2 p = center + new Vector2(points[i].X, points[i].Y) * scale;
			sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle((int)p.X - 1, (int)p.Y - 1, 3, 3), new Rectangle(0, 0, 1, 1), c);
		}
	}

	#endregion

	private static void OpenFile(string path, FileType type)
	{
		// Only what can't run anything opens from a chat file (pictures, text, sound, archives, PDF); the rest is shown
		// in its folder.
		string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
		bool safe = type == FileType.Picture || type == FileType.Text || type == FileType.Audio || type == FileType.Archive || ext == ".pdf";
		if (!safe)
		{
			ClientChat.Info("Files like this one aren't opened from the chat: here it is in its folder.");
			ShowInFolder(path);
			return;
		}
		try
		{
			Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
		}
		catch (Exception ex)
		{
			ClientChat.Error("Windows couldn't open it: " + ex.Message);
		}
	}

	private static void ShowInFolder(string path)
	{
		try
		{
			Process.Start("explorer.exe", "/select,\"" + path + "\"");
		}
		catch (Exception ex)
		{
			ClientChat.Error("Couldn't open the folder: " + ex.Message);
		}
	}
}
