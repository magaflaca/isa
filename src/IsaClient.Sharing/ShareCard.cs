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
/// A shared thing in a chat line: a small framed card with the character's head (or the style on a bare head, or a
/// map of the schematic), its name, and while it travels, how far it got ("Uploading 40%", "Receiving 75%") with a
/// thin bar. Hover for details, click to open it.
/// </summary>
internal sealed class ShareCard : TextSnippet
{
	private const float Height = 26f;

	private readonly ShareKind _kind;

	private readonly ShareInbox.Item _item;

	public ShareCard(ShareKind kind, ShareInbox.Item item)
		: base("[" + (item?.Name ?? "") + "]")
	{
		_kind = kind;
		_item = item;
		CheckForHover = true;
		DeleteWhole = true;
	}

	private static Color Back(ShareKind kind) => kind switch
	{
		ShareKind.Character => new Color(52, 72, 140),
		ShareKind.Style => new Color(98, 70, 150),
		_ => new Color(40, 110, 100)
	};

	private string Title
	{
		get
		{
			string name = string.IsNullOrWhiteSpace(_item.Name) ? null : _item.Name;
			return _kind switch
			{
				ShareKind.Character => name ?? IsaClient.Languages.L10n.T("Character"),
				ShareKind.Style => (name != null && name != "Style") ? IsaClient.Languages.L10n.F("{0}'s style", name) : IsaClient.Languages.L10n.T("Character style"),
				_ => name ?? IsaClient.Languages.L10n.T("Schematic")
			};
		}
	}

	/// <summary>"Uploading 40%", "Receiving 75%", a size... and the bar's fill (negative: no bar).</summary>
	private string Status(out float bar)
	{
		bar = -1f;
		if (_kind == ShareKind.Style)
		{
			return IsaClient.Languages.L10n.T("Style");
		}
		if (_kind == ShareKind.Schematic)
		{
			SchematicShare.Entry e = _item.Schematic;
			if (e == null)
			{
				return "";
			}
			string size = e.Width + "x" + e.Height;
			// With the pipe off: its Imgur picture going up or coming down.
			if (e.Image != null && e.Image.Busy && !e.Ready)
			{
				bar = e.Image.Progress;
				return IsaClient.Languages.L10n.T(e.Mine ? "Uploading" : "Receiving") + " " + (int)(e.Image.Progress * 100f) + "%";
			}
			if (e.Uploading)
			{
				bar = e.Progress;
				return IsaClient.Languages.L10n.T("Uploading") + " " + (int)(e.Progress * 100f) + "%";
			}
			if (e.Mine || e.Ready)
			{
				return size;
			}
			if (e.Failed)
			{
				return IsaClient.Languages.L10n.T("Couldn't get it");
			}
			ShareTransfer.Want(e.Code, ShareKind.Schematic);
			ShareTransfer.Transfer pipe = ShareTransfer.Get(e.Code);
			float progress = Math.Max(e.Progress, (pipe != null && pipe.Pieces > 0) ? pipe.Fraction : 0f);
			bar = progress;
			return (e.Status == "Waiting for archive.org" && progress <= 0f) ? IsaClient.Languages.L10n.T("Waiting for archive.org") : (IsaClient.Languages.L10n.T("Receiving") + " " + (int)(progress * 100f) + "%");
		}
		ShareTransfer.Transfer t = ShareTransfer.Get(_item.Code);
		if (t == null)
		{
			ShareTransfer.Want(_item.Code, _kind);
			bar = 0f;
			return IsaClient.Languages.L10n.T("Waiting");
		}
		// With the pipe off it goes inside an Imgur picture (archive.org keeps a spare copy).
		ArchiveStore.Job archive = t.Archive;
		ArchiveStore.Job image = t.Image;
		if (t.Sending)
		{
			if (t.Pushing)
			{
				bar = t.Fraction;
				return IsaClient.Languages.L10n.T("Uploading") + " " + (int)(t.Fraction * 100f) + "%";
			}
			ArchiveStore.Job going = (image != null && image.Busy) ? image : ((image == null || image.Failed) && archive != null && archive.Busy) ? archive : null;
			if (going != null)
			{
				bar = going.Progress;
				return IsaClient.Languages.L10n.T("Uploading") + " " + (int)(going.Progress * 100f) + "%";
			}
			if ((image == null || image.Failed) && archive != null && archive.Failed)
			{
				return IsaClient.Languages.L10n.T("Upload failed");
			}
			return (t.Done > 0 || (image != null && image.Done) || (archive != null && archive.Done)) ? IsaClient.Languages.L10n.T("Sent") : IsaClient.Languages.L10n.T("Ready to send");
		}
		if (!t.Complete && image != null && image.Busy)
		{
			bar = image.Progress;
			return IsaClient.Languages.L10n.T("Receiving") + " " + (int)(image.Progress * 100f) + "%";
		}
		if (!t.Complete && archive != null && t.Done == 0)
		{
			if (archive.Failed)
			{
				return IsaClient.Languages.L10n.T("Couldn't get it");
			}
			bar = archive.Progress;
			return (archive.Waiting && archive.Progress <= 0f) ? IsaClient.Languages.L10n.T("Waiting for archive.org") : (IsaClient.Languages.L10n.T("Receiving") + " " + (int)(archive.Progress * 100f) + "%");
		}
		if (!t.Complete)
		{
			if (t.Failed)
			{
				return IsaClient.Languages.L10n.T("The sender left");
			}
			bar = t.Fraction;
			return IsaClient.Languages.L10n.T("Receiving") + " " + (int)(t.Fraction * 100f) + "%";
		}
		Player loaded = ShareInbox.Character(_item);
		return (loaded != null) ? (loaded.statLifeMax + " HP  " + loaded.statManaMax + " MP") : IsaClient.Languages.L10n.T("Received");
	}

	public override bool UniqueDraw(bool justCheckingString, out Vector2 size, SpriteBatch spriteBatch, Vector2 position = default(Vector2), Color color = default(Color), float scale = 1f)
	{
		DynamicSpriteFont font = FontAssets.MouseText.Value;
		string title = Title;
		string status = Status(out float bar);
		float nameScale = 0.72f * scale;
		float statusScale = 0.6f * scale;
		float art = (_kind == ShareKind.Schematic) ? 40f : 24f;
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
			Rectangle card = new Rectangle((int)position.X, (int)(position.Y - 3f * scale), (int)width, (int)(Height * scale));
			Utils.DrawInvBG(spriteBatch, card, Back(_kind) * (0.92f * a));
			Rectangle artBox = new Rectangle(card.X + (int)(4f * scale), card.Y + (int)(3f * scale), (int)(art * scale), card.Height - (int)(6f * scale));
			spriteBatch.Draw(TextureAssets.MagicPixel.Value, artBox, new Rectangle(0, 0, 1, 1), Color.Black * (0.25f * a));
			DrawArt(spriteBatch, _kind, _item, artBox, scale, a);
			float textX = artBox.Right + 6f * scale;
			float maxText = card.Right - 4f * scale - textX;
			spriteBatch.DrawString(font, Fit(font, title, maxText, nameScale), new Vector2(textX, card.Y + 1f * scale), Color.White * a, 0f, Vector2.Zero, nameScale, SpriteEffects.None, 0f);
			spriteBatch.DrawString(font, Fit(font, status, maxText, statusScale), new Vector2(textX, card.Y + 13f * scale), new Color(200, 210, 230) * a, 0f, Vector2.Zero, statusScale, SpriteEffects.None, 0f);
			if (bar >= 0f)
			{
				Rectangle track = new Rectangle(card.X + 4, card.Bottom - (int)(4f * scale), card.Width - 8, Math.Max(2, (int)(2f * scale)));
				spriteBatch.Draw(TextureAssets.MagicPixel.Value, track, new Rectangle(0, 0, 1, 1), Color.Black * (0.45f * a));
				// Moves even before the first piece lands, so it never looks stuck.
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

	/// <summary>The card's picture: the head (a style's on a bare head, with a brush), or the schematic's map.</summary>
	internal static void DrawArt(SpriteBatch sb, ShareKind kind, ShareInbox.Item item, Rectangle box, float scale, float a)
	{
		if (kind == ShareKind.Schematic)
		{
			Texture2D preview = SchematicShare.PreviewOf(item.Schematic);
			if (preview != null)
			{
				float fit = Math.Min((float)box.Width / preview.Width, (float)box.Height / preview.Height);
				int w = Math.Max(1, (int)(preview.Width * fit));
				int h = Math.Max(1, (int)(preview.Height * fit));
				sb.Draw(preview, new Rectangle(box.Center.X - w / 2, box.Center.Y - h / 2, w, h), Color.White * a);
				return;
			}
			DrawItem(sb, ShareKinds.Item(ShareKind.Schematic), box, scale, a);
			return;
		}
		Player look = ShareInbox.Character(item) ?? item.Mannequin;
		if (look == null)
		{
			DrawItem(sb, ShareKinds.Item(kind), box, scale, a);
			return;
		}
		float headScale = 0.62f * scale;
		Vector2 center = new Vector2(box.Center.X, box.Center.Y + 1f * scale);
		Vector2 corner = new Vector2(center.X - 4f + 4f * headScale, center.Y - 14f + 9f * headScale);
		Main.PlayerRenderer.DrawPlayerHead(Main.Camera, look, corner, a, headScale, Color.Transparent);
		if (kind == ShareKind.Style)
		{
			DrawItem(sb, ShareKinds.Item(ShareKind.Style), new Rectangle(box.Right - (int)(11f * scale), box.Bottom - (int)(11f * scale), (int)(12f * scale), (int)(12f * scale)), scale, a);
		}
	}

	private static void DrawItem(SpriteBatch sb, int type, Rectangle box, float scale, float a)
	{
		Main.instance.LoadItem(type);
		Texture2D texture = TextureAssets.Item[type].Value;
		Rectangle frame = (Main.itemAnimations[type] != null) ? Main.itemAnimations[type].GetFrame(texture) : texture.Frame();
		float fit = Math.Min(1f, Math.Min((float)box.Width / frame.Width, (float)box.Height / frame.Height));
		sb.Draw(texture, new Vector2(box.Center.X, box.Center.Y), frame, Color.White * a, 0f, frame.Size() / 2f, fit, SpriteEffects.None, 0f);
	}

	public override void OnHover()
	{
		string text;
		switch (_kind)
		{
		case ShareKind.Character:
		{
			Player loaded = ShareInbox.Character(_item);
			text = Title + ((loaded != null) ? ("\n" + IsaClient.Menu.UiKit.Summary(loaded)) : "") + "\n" + IsaClient.Languages.L10n.T(_item.Mine ? "Your character, linked in chat." : "Click: wear it, save it, paste it as pixel art...");
			break;
		}
		case ShareKind.Style:
			text = Title + "\n" + IsaClient.Languages.L10n.T("Hair, clothes and colors. Click to try it on, keep it or copy it for character creation.");
			break;
		default:
		{
			SchematicShare.Entry e = _item.Schematic;
			ShareChat.HoverPreview = e;
			text = Title + ((e != null) ? ("  (" + e.Width + "x" + e.Height + ")") : "") + "\n" + IsaClient.Languages.L10n.T("Click: paste it, save it or send it on.");
			break;
		}
		}
		Main.instance.MouseText(text, 0, 0);
	}

	public override void OnClick()
	{
		Main.mouseLeftRelease = false;
		SoundEngine.PlaySound(12);
		ShareDialog.Open(_kind, _item);
	}
}
