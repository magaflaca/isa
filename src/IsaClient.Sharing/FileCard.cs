using System;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.UI.Chat;

namespace IsaClient.Sharing;

/// <summary>
/// A shared file in a chat line: framed like the character and schematic cards, with an icon for what it is, its name,
/// and its size or how far it got ("Uploading 40%", "Receiving 75%") with a thin bar. Small files come down as soon as
/// the card shows; bigger ones on a click. Click opens what can be done with it (<see cref="FileShareDialog"/>).
/// </summary>
internal sealed class FileCard : TextSnippet
{
	private const float Height = 26f;

	private readonly FileShare.Entry _entry;

	/// <summary>In the chat line still (not sent yet).</summary>
	private readonly bool _pending;

	public FileCard(FileShare.Entry entry, bool pending = false)
		: base("[" + (entry?.Name ?? "") + "]")
	{
		_entry = entry;
		_pending = pending;
		CheckForHover = true;
		DeleteWhole = true;
	}

	private static Color Back(FileType type) => type switch
	{
		FileType.RainbowLetters => new Color(120, 60, 130),
		FileType.World => new Color(40, 90, 140),
		FileType.TileMap => new Color(40, 110, 100),
		_ => new Color(70, 76, 120)
	};

	/// <summary>The name to show: rainbow letter files without their double extension.</summary>
	internal static string Title(FileShare.Entry e)
	{
		string name = e.Name ?? "file";
		if (name.EndsWith(".rainbow.xml", StringComparison.OrdinalIgnoreCase))
		{
			name = name.Substring(0, name.Length - 12);
		}
		return name;
	}

	internal static string Status(FileShare.Entry e, out float bar, bool pending = false)
	{
		bar = -1f;
		string size = FileShare.SizeText(e.Size);
		if (e.Mine)
		{
			if (e.Busy)
			{
				bar = e.Progress;
				return IsaClient.Languages.L10n.T("Uploading") + " " + (int)(e.Progress * 100f) + "%";
			}
			return (e.Error != null) ? IsaClient.Languages.L10n.T("Upload failed") : (size + "  " + IsaClient.Languages.L10n.T(pending ? "Ready to send" : "Sent"));
		}
		if (e.Here)
		{
			return size + "  " + IsaClient.Languages.L10n.T("Received");
		}
		if (e.Busy)
		{
			bar = e.Progress;
			return IsaClient.Languages.L10n.T("Receiving") + " " + (int)(e.Progress * 100f) + "%";
		}
		if (e.Error != null)
		{
			return IsaClient.Languages.L10n.T("Couldn't get it");
		}
		if (e.Size <= FileShare.AutoDownload)
		{
			FileShare.Download(e);
			bar = 0f;
			return IsaClient.Languages.L10n.T("Receiving") + " 0%";
		}
		return size + "  " + IsaClient.Languages.L10n.T("Click to download");
	}

	public override bool UniqueDraw(bool justCheckingString, out Vector2 size, SpriteBatch spriteBatch, Vector2 position = default(Vector2), Color color = default(Color), float scale = 1f)
	{
		DynamicSpriteFont font = FontAssets.MouseText.Value;
		string title = Title(_entry);
		string status = Status(_entry, out float bar, _pending);
		float nameScale = 0.72f * scale;
		float statusScale = 0.6f * scale;
		float art = 24f;
		float textWidth = Math.Max(font.MeasureString(title).X * nameScale, font.MeasureString(status).X * statusScale);
		float width = (art + 12f) * scale + Math.Min(170f * scale, textWidth) + 8f * scale;
		size = new Vector2(width + 4f * scale, Height * scale * 0.85f);
		if (justCheckingString || spriteBatch == null || color.R < 20 && color.G < 20 && color.B < 20 && color != Color.White)
		{
			return true;
		}
		float a = color.A / 255f;
		try
		{
			FileType type = _entry.Type;
			Rectangle card = new Rectangle((int)position.X, (int)(position.Y - 3f * scale), (int)width, (int)(Height * scale));
			Utils.DrawInvBG(spriteBatch, card, Back(type) * (0.92f * a));
			Rectangle artBox = new Rectangle(card.X + (int)(4f * scale), card.Y + (int)(3f * scale), (int)(art * scale), card.Height - (int)(6f * scale));
			spriteBatch.Draw(TextureAssets.MagicPixel.Value, artBox, new Rectangle(0, 0, 1, 1), Color.Black * (0.25f * a));
			DrawIcon(spriteBatch, type, artBox, scale, a);
			float textX = artBox.Right + 6f * scale;
			float maxText = card.Right - 4f * scale - textX;
			spriteBatch.DrawString(font, Fit(font, title, maxText, nameScale), new Vector2(textX, card.Y + 1f * scale), Color.White * a, 0f, Vector2.Zero, nameScale, SpriteEffects.None, 0f);
			spriteBatch.DrawString(font, Fit(font, status, maxText, statusScale), new Vector2(textX, card.Y + 13f * scale), new Color(200, 210, 230) * a, 0f, Vector2.Zero, statusScale, SpriteEffects.None, 0f);
			if (bar >= 0f)
			{
				Rectangle track = new Rectangle(card.X + 4, card.Bottom - (int)(4f * scale), card.Width - 8, Math.Max(2, (int)(2f * scale)));
				spriteBatch.Draw(TextureAssets.MagicPixel.Value, track, new Rectangle(0, 0, 1, 1), Color.Black * (0.45f * a));
				float fill = MathHelper.Clamp(bar, 0f, 1f);
				spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(track.X, track.Y, Math.Max(2, (int)(track.Width * fill)), track.Height), new Rectangle(0, 0, 1, 1), new Color(120, 230, 140) * a);
				if (fill < 1f)
				{
					float glow = (float)(Main.timeForVisualEffects % 90.0 / 90.0);
					spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(track.X + (int)(track.Width * glow), track.Y, Math.Max(2, track.Width / 8), track.Height), new Rectangle(0, 0, 1, 1), Color.White * (0.35f * a));
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogOnce(ex);
		}
		return true;
	}

	private static string Fit(DynamicSpriteFont font, string text, float max, float scale)
	{
		if (font.MeasureString(text).X * scale <= max)
		{
			return text;
		}
		while (text.Length > 1 && font.MeasureString(text + "...").X * scale > max)
		{
			text = text.Substring(0, text.Length - 1);
		}
		return text + "...";
	}

	/// <summary>The item that stands for a kind of file, fitted in a box (chips, cards, the window).</summary>
	internal static void DrawIcon(SpriteBatch sb, FileType type, Rectangle box, float scale, float a)
	{
		int item = FileShare.Icon(type);
		Main.instance.LoadItem(item);
		Texture2D texture = TextureAssets.Item[item].Value;
		Rectangle frame = (Main.itemAnimations[item] != null) ? Main.itemAnimations[item].GetFrame(texture) : texture.Frame();
		float fit = Math.Min(1.5f * Math.Max(1f, scale), Math.Min((float)box.Width / frame.Width, (float)box.Height / frame.Height));
		sb.Draw(texture, new Vector2(box.Center.X, box.Center.Y), frame, Color.White * a, 0f, frame.Size() / 2f, fit, SpriteEffects.None, 0f);
	}

	public override void OnHover()
	{
		string text = Title(_entry) + "\n" + IsaClient.Languages.L10n.T(FileShare.TypeName(_entry.Type)) + ", " + FileShare.SizeText(_entry.Size) + "\n" + IsaClient.Languages.L10n.T(_entry.Mine ? "Your file, linked in chat." : (_entry.Here ? "Click: open, add or show it." : "Click to get it."));
		Main.instance.MouseText(text, 0, 0);
	}

	public override void OnClick()
	{
		Main.mouseLeftRelease = false;
		SoundEngine.PlaySound(12);
		if (!_entry.Here && !_entry.Mine)
		{
			FileShare.Download(_entry);
		}
		FileShareDialog.Open(_entry);
	}
}
