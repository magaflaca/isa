using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;

namespace IsaClient.ChatImages;

/// <summary>
/// The emoticons button on the chat bar (the inventory's emote bubble): a small panel above it with text emoticons
/// (:) :( ^_^ uwu...) that the game's font draws well; a click writes one at the caret. It stays open to pick several.
/// </summary>
public static class EmoticonPicker
{
	[Setting("ChatEmoticonsButton")]
	public static bool Button = true;

	public static bool Visible { get; private set; }

	private const int Columns = 8;

	private const int CellWidth = 62;

	private const int CellHeight = 28;

	private static readonly string[] All =
	{
		":)", ":(", ":D", ";)", ":P", ":O", ":3", "xD",
		"XD", ":/", ":|", ">:(", ":'(", "<3", "</3", ":*",
		"^_^", "^^", "^-^", "^w^", "-_-", "-.-", "._.", ">_<",
		"o_O", "O_o", "T_T", ";_;", "uwu", "UwU", "owo", "OwO",
		">w<", ":v", "c:", ":c", "D:", "8)", "B)", "xP",
		"=)", "=(", "=D", "(:", "):", ":S", "o/", "\\o/",
		":-)", ":-(", ":-D", ";-)", "(y)", "(n)", "(^_^)/", "\\(^o^)/",
		"(>_<)", "(T_T)", "(o_O)", "(-_-)zzz", "(*^_^*)", "(=^.^=)", "<(^_^)>", "( ._.)",
		"(>'-')>", "<('-'<)", "^(;,;)^", "(._.)", "(^o^)", "(-.-)", "(*_*)", "(+_+)",
		"¯\\_(ツ)_/¯", "(¬_¬)", "(•_•)", "ʕ•ᴥ•ʔ", "(╯°□°)╯︵ ┻━┻"
	};

	private static string[] _shown;

	/// <summary>The emoticons whose every letter the chat's font has (the rest would come out as boxes).</summary>
	private static string[] Shown
	{
		get
		{
			if (_shown == null)
			{
				DynamicSpriteFont font = FontAssets.MouseText.Value;
				_shown = All.Where(e => e.All(c => font.IsCharacterSupported(c))).ToArray();
			}
			return _shown;
		}
	}

	public static void Toggle()
	{
		Visible = !Visible;
	}

	private static Rectangle Panel(Rectangle button)
	{
		int rows = (Shown.Length + Columns - 1) / Columns;
		int width = Columns * CellWidth + 16;
		int height = rows * CellHeight + 38;
		int right = Math.Min(Main.screenWidth - 8, button.Right + 8);
		int bottom = Main.screenHeight - 36 - IsaClient.Community.ChatBox.InputExtraHeight - 6;
		return new Rectangle(right - width, bottom - height, width, height);
	}

	private static Rectangle Cell(Rectangle panel, int i)
	{
		return new Rectangle(panel.X + 8 + i % Columns * CellWidth, panel.Y + 30 + i / Columns * CellHeight, CellWidth - 4, CellHeight - 4);
	}

	private static bool MouseFree => !PlayerInput.IgnoreMouseInterface;

	/// <summary>Before the chat bar takes the click: the button and the panel's clicks are ours.</summary>
	public static bool BeforeChatBar(Rectangle button)
	{
		if (!Main.drawingPlayerChat)
		{
			Visible = false;
			return false;
		}
		if (!Button || !MouseFree || !Main.mouseLeft || !Main.mouseLeftRelease)
		{
			return false;
		}
		if (button.Contains(Main.mouseX, Main.mouseY))
		{
			Main.mouseLeftRelease = false;
			Main.LocalPlayer.mouseInterface = true;
			SoundEngine.PlaySound(12);
			Toggle();
			return true;
		}
		if (!Visible)
		{
			return false;
		}
		Rectangle panel = Panel(button);
		if (!panel.Contains(Main.mouseX, Main.mouseY))
		{
			// A click anywhere else closes it (and does what it does).
			Visible = false;
			return false;
		}
		Main.mouseLeftRelease = false;
		Main.LocalPlayer.mouseInterface = true;
		string[] shown = Shown;
		for (int i = 0; i < shown.Length; i++)
		{
			if (Cell(panel, i).Contains(Main.mouseX, Main.mouseY))
			{
				Write(shown[i]);
				SoundEngine.PlaySound(12);
				break;
			}
		}
		return true;
	}

	/// <summary>Writes the emoticon at the caret, with a space after it (and before, if it would stick to a word).</summary>
	private static void Write(string emoticon)
	{
		string text = Main.chatText ?? "";
		string insert = emoticon + " ";
		if (IsaClient.Community.ChatBox.Enabled)
		{
			IsaClient.Community.ChatBox.InsertExternal(insert);
			return;
		}
		if (text.Length > 0 && !char.IsWhiteSpace(text[text.Length - 1]))
		{
			insert = " " + insert;
		}
		Main.chatText = text + insert;
	}

	public static void DrawButton(SpriteBatch sb, Rectangle r)
	{
		bool hover = r.Contains(Main.mouseX, Main.mouseY) && MouseFree;
		if (hover)
		{
			Main.LocalPlayer.mouseInterface = true;
			Main.instance.MouseText(IsaClient.Languages.L10n.T("Emoticons  :)  ^_^  uwu"), 0, 0);
		}
		Texture2D texture = TextureAssets.EmoteMenuButton.Value;
		Rectangle frame = texture.Frame(2, 1, (hover || Visible) ? 1 : 0);
		frame.Width -= 2;
		frame.Height -= 2;
		float scale = (r.Height - 2f) / frame.Height * (hover ? 1.08f : 1f);
		sb.Draw(texture, r.Center.ToVector2(), frame, Color.White, 0f, frame.Size() / 2f, scale, SpriteEffects.None, 0f);
	}

	public static void DrawPanel(SpriteBatch sb, Rectangle button)
	{
		if (!Visible || !Main.drawingPlayerChat)
		{
			return;
		}
		Rectangle panel = Panel(button);
		Utils.DrawInvBG(sb, panel, new Color(33, 43, 79) * 0.95f);
		Utils.DrawBorderString(sb, IsaClient.Languages.L10n.T("Emoticons"), new Vector2(panel.X + 10, panel.Y + 7), Color.White, 0.8f);
		if (panel.Contains(Main.mouseX, Main.mouseY) && MouseFree)
		{
			Main.LocalPlayer.mouseInterface = true;
		}
		DynamicSpriteFont font = FontAssets.MouseText.Value;
		string[] shown = Shown;
		for (int i = 0; i < shown.Length; i++)
		{
			Rectangle cell = Cell(panel, i);
			bool hover = cell.Contains(Main.mouseX, Main.mouseY) && MouseFree;
			Utils.DrawInvBG(sb, cell, hover ? new Color(90, 110, 190) * 0.95f : new Color(50, 62, 120) * 0.85f);
			Vector2 size = font.MeasureString(shown[i]);
			float scale = Math.Min(0.82f, (cell.Width - 8f) / Math.Max(1f, size.X));
			Utils.DrawBorderString(sb, shown[i], new Vector2(cell.Center.X, cell.Center.Y + 2), hover ? new Color(255, 235, 140) : Color.White, scale, 0.5f, 0.5f);
		}
	}
}
