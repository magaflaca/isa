using System;
using System.Collections.Generic;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using ReLogic.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.UI.Chat;

namespace IsaClient.Community;

/// <summary>
/// Right click on a chat message (with the chat open) copies it to the clipboard exactly as it came, tags included
/// ([c/..], [i:..]); Ctrl + right click translates it (<see cref="IncomingTranslation"/>). The chat monitor reports
/// every line it draws; the message under the mouse gets a soft highlight, and a translated one shows its original.
/// </summary>
public static class ChatCopy
{
	[Setting("RightClickCopiesChat")]
	public static bool Enabled = true;

	private static readonly List<(ChatMessageContainer Message, Rectangle Area)> _lines = new List<(ChatMessageContainer, Rectangle)>();

	private static double _noteUntil;

	private static Vector2 _noteAt;

	private static string _note = "";

	/// <summary>Before the chat draws its lines.</summary>
	public static void BeginFrame()
	{
		_lines.Clear();
	}

	/// <summary>One drawn line of <paramref name="message"/> at <paramref name="position"/>.</summary>
	public static void TrackLine(ChatMessageContainer message, TextSnippet[] line, Vector2 position, bool drawingPlayerChat)
	{
		if (!(Enabled || IncomingTranslation.CtrlRightClick || IncomingTranslation.OriginalOnHover) || !drawingPlayerChat || message == null || line == null)
		{
			return;
		}
		Vector2 size = ChatManager.GetStringSize(FontAssets.MouseText.Value, line, Vector2.One);
		_lines.Add((message, new Rectangle((int)position.X - 4, (int)position.Y - 1, (int)Math.Max(size.X, 12f) + 8, 22)));
	}

	/// <summary>After the lines: highlight the hovered message and copy it on right click.</summary>
	public static void EndFrame()
	{
		DrawNote();
		if (_lines.Count == 0 || PlayerInput.IgnoreMouseInterface)
		{
			return;
		}
		Point mouse = new Point(Main.mouseX, Main.mouseY);
		ChatMessageContainer hovered = null;
		foreach ((ChatMessageContainer message, Rectangle area) in _lines)
		{
			if (area.Contains(mouse))
			{
				hovered = message;
				break;
			}
		}
		if (hovered == null)
		{
			return;
		}
		foreach ((ChatMessageContainer message, Rectangle area) in _lines)
		{
			if (message == hovered)
			{
				Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, area, Color.White * 0.07f);
			}
		}
		Main.LocalPlayer.mouseInterface = true;
		if (hovered.ShowsTranslation && IncomingTranslation.OriginalOnHover)
		{
			DrawOriginal(hovered.OriginalText);
		}
		if (Main.mouseRight && Main.mouseRightRelease)
		{
			bool ctrl = Main.keyState.IsKeyDown(Keys.LeftControl) || Main.keyState.IsKeyDown(Keys.RightControl);
			if (ctrl && IncomingTranslation.CtrlRightClick)
			{
				Main.mouseRightRelease = false;
				SoundEngine.PlaySound(12);
				Note(IncomingTranslation.Toggle(hovered));
			}
			else if (Enabled)
			{
				Main.mouseRightRelease = false;
				Copy(hovered.OriginalText);
			}
		}
	}

	/// <summary>A short word by the mouse ("Copied!", "Translated").</summary>
	public static void Note(string text)
	{
		_note = text ?? "";
		_noteUntil = Main.timeForVisualEffects + 60.0;
		_noteAt = new Vector2(Main.mouseX + 14, Main.mouseY - 18);
	}

	/// <summary>"Original:" and the message as it came, in a box above the mouse.</summary>
	private static void DrawOriginal(string text)
	{
		DynamicSpriteFont font = FontAssets.MouseText.Value;
		List<List<TextSnippet>> lines = Utils.WordwrapStringSmart(text ?? "", Color.White, font, 460, 6);
		float scale = 0.85f;
		float width = HeaderWidth(font, scale);
		foreach (List<TextSnippet> line in lines)
		{
			width = Math.Max(width, ChatManager.GetStringSize(font, line.ToArray(), Vector2.One).X * scale);
		}
		float height = (lines.Count + 1) * 20f * scale + 12f;
		Vector2 position = new Vector2(Main.mouseX + 18, Main.mouseY - height - 10);
		position.X = Math.Min(position.X, Main.screenWidth - width - 24);
		position.Y = Math.Max(4f, position.Y);
		Utils.DrawInvBG(Main.spriteBatch, new Rectangle((int)position.X - 8, (int)position.Y - 6, (int)width + 16, (int)height), new Color(23, 25, 81, 255) * 0.925f);
		Utils.DrawBorderString(Main.spriteBatch, "Original:", position, ClientChat.AccentColor, scale);
		for (int i = 0; i < lines.Count; i++)
		{
			ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, font, lines[i].ToArray(), position + new Vector2(0f, (i + 1) * 20f * scale), 0f, Vector2.Zero, new Vector2(scale), out _);
		}
	}

	private static float HeaderWidth(DynamicSpriteFont font, float scale)
	{
		return font.MeasureString("Original:").X * scale;
	}

	private static void Copy(string text)
	{
		try
		{
			ReLogic.OS.Platform.Get<ReLogic.OS.IClipboard>().Value = text ?? "";
			SoundEngine.PlaySound(12);
			Note("Copied!");
		}
		catch (Exception ex)
		{
			ClientChat.Error("Could not copy the message: " + ex.Message);
		}
	}

	private static void DrawNote()
	{
		if (Main.timeForVisualEffects >= _noteUntil)
		{
			return;
		}
		float fade = (float)Math.Min(1.0, (_noteUntil - Main.timeForVisualEffects) / 20.0);
		Utils.DrawBorderString(Main.spriteBatch, _note, _noteAt, ClientChat.AccentColor * fade, 0.8f);
	}
}
