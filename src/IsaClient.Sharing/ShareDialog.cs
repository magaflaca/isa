using System;
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
/// What to do with a style or a schematic from the chat (characters open the character file dialog). Drawn by
/// <see cref="PlayerFileDropDialog"/>, which keeps the game from seeing the clicks while it's open, with the same look.
/// </summary>
public static class ShareDialog
{
	private static ShareKind _kind;

	private static ShareInbox.Item _item;

	public static bool IsOpen => _item != null;

	public static void Open(ShareKind kind, ShareInbox.Item item)
	{
		if (item == null)
		{
			return;
		}
		if (kind == ShareKind.Character)
		{
			if (item.Path != null && System.IO.File.Exists(item.Path))
			{
				PlayerFileDropDialog.Open(item.Path);
			}
			else
			{
				ShareTransfer.Want(item.Code, ShareKind.Character);
				ClientChat.Info(IsaClient.Languages.L10n.F("{0} is still on the way: it opens when it arrives.", string.IsNullOrEmpty(item.Name) ? IsaClient.Languages.L10n.T("The character") : item.Name));
			}
			return;
		}
		PlayerFileDropDialog.Close();
		FileShareDialog.Close();
		_kind = kind;
		_item = item;
		SoundEngine.PlaySound(10);
	}

	public static void Close()
	{
		_item = null;
	}

	public static void Draw(SpriteBatch sb, int screenW, int screenH)
	{
		if (_item == null)
		{
			return;
		}
		if (_kind == ShareKind.Style)
		{
			DrawStyle(sb, screenW, screenH);
		}
		else
		{
			DrawSchematic(sb, screenW, screenH);
		}
		UiKit.EndFrame();
		if (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape))
		{
			Close();
		}
	}

	private static Rectangle Frame(SpriteBatch sb, int screenW, int screenH, string title, int height)
	{
		int width = Math.Min(520, screenW - 32);
		Rectangle panel = new Rectangle((screenW - width) / 2, (screenH - height) / 2, width, height);
		UiKit.Panel(sb, panel, new Color(33, 43, 79) * 0.97f);
		Utils.DrawBorderStringBig(sb, IsaClient.Languages.L10n.T(title), new Vector2(panel.Center.X, panel.Y + 28), new Color(255, 231, 69), 0.5f, 0.5f, 0.5f);
		return panel;
	}

	private static void DrawStyle(SpriteBatch sb, int screenW, int screenH)
	{
		StyleLook look = _item.Look;
		if (look == null)
		{
			Close();
			return;
		}
		Rectangle panel = Frame(sb, screenW, screenH, "Character style", 290);
		Rectangle portrait = new Rectangle(panel.X + 24, panel.Y + 56, 96, 110);
		UiKit.Portrait(sb, _item.Mannequin, portrait, 1.4f, new Color(20, 26, 50) * 0.9f);
		float textX = portrait.Right + 16;
		float textW = panel.Right - 20 - textX;
		string name = (string.IsNullOrWhiteSpace(look.Name) || look.Name == "Style") ? IsaClient.Languages.L10n.T("Character style") : IsaClient.Languages.L10n.F("{0}'s style", look.Name);
		UiKit.Text(sb, name, textX, portrait.Y + 14, Color.White, 1f, textW);
		UiKit.Text(sb, "[c/9AA7BE:" + IsaClient.Languages.L10n.F("Hairstyle {0}, clothing style {1}", look.Hair + 1, look.SkinVariant + 1) + "]", textX, portrait.Y + 40, Color.White, 0.75f, textW);
		// The seven colors, as swatches.
		Color[] colors = { look.HairColor, look.EyeColor, look.SkinColor, look.ShirtColor, look.UnderShirtColor, look.PantsColor, look.ShoeColor };
		string[] names = { "Hair", "Eyes", "Skin", "Shirt", "Undershirt", "Pants", "Shoes" };
		int swatch = 22;
		for (int i = 0; i < colors.Length; i++)
		{
			Rectangle r = new Rectangle((int)textX + i * (swatch + 6), portrait.Y + 60, swatch, swatch);
			Utils.DrawInvBG(sb, r, new Color(20, 26, 50) * 0.9f);
			sb.Draw(TextureAssets.MagicPixel.Value, new Rectangle(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8), new Rectangle(0, 0, 1, 1), colors[i]);
			if (r.Contains(UiKit.Mouse))
			{
				Main.instance.MouseText(IsaClient.Languages.L10n.T(names[i]) + "  #" + colors[i].R.ToString("X2") + colors[i].G.ToString("X2") + colors[i].B.ToString("X2"), 0, 0);
			}
		}
		UiKit.Text(sb, "[c/9AA7BE:" + IsaClient.Languages.L10n.T("Only the look: your items, stats and name stay yours.") + "]", textX, portrait.Y + 98, Color.White, 0.7f, textW);
		int bw = (panel.Width - 48 - 10) / 2;
		int by = portrait.Bottom + 18;
		bool kept = StyleLibrary.Has(look);
		if (UiKit.Button(sb, new Rectangle(panel.X + 24, by, bw, 38), "Try it on", UiKit.Green, enabled: !Main.gameMenu, "Puts this hair, clothes and colors on you.\nRestore My Character (your Player List row, or .wear restore) brings yours back."))
		{
			CharacterPresets.Wear(_item.Mannequin, WearParts.Look, name);
			Close();
			return;
		}
		if (UiKit.Button(sb, new Rectangle(panel.X + 34 + bw, by, bw, 38), "Copy for creation", UiKit.Blue, enabled: true, "Copies it as a player template: in character creation, the paste template button puts it on a new character."))
		{
			try
			{
				ReLogic.OS.Platform.Get<ReLogic.OS.IClipboard>().Value = look.ToTemplate();
				ClientChat.Info("Style copied: paste it in character creation.");
			}
			catch (Exception ex)
			{
				ClientLog.LogException(ex);
			}
		}
		by += 46;
		if (UiKit.Button(sb, new Rectangle(panel.X + 24, by, bw, 38), kept ? "In my styles" : "Keep in my styles", UiKit.Purple, enabled: !kept, "Saves it in IsaClient > Sharing > Styles, to try on or send later."))
		{
			StyleLibrary.Add(look);
			SoundEngine.PlaySound(12);
		}
		if (UiKit.Button(sb, new Rectangle(panel.X + 34 + bw, by, bw, 38), "Close", UiKit.Red))
		{
			Close();
		}
	}

	private static void DrawSchematic(SpriteBatch sb, int screenW, int screenH)
	{
		SchematicShare.Entry e = _item.Schematic;
		if (e == null)
		{
			Close();
			return;
		}
		Rectangle panel = Frame(sb, screenW, screenH, "Schematic", 312);
		Rectangle map = new Rectangle(panel.X + 24, panel.Y + 56, 200, 132);
		Utils.DrawInvBG(sb, map, new Color(20, 26, 50) * 0.9f);
		Rectangle inner = new Rectangle(map.X + 6, map.Y + 6, map.Width - 12, map.Height - 12);
		sb.Draw(TextureAssets.MagicPixel.Value, inner, new Rectangle(0, 0, 1, 1), new Color(10, 14, 28));
		Texture2D preview = SchematicShare.PreviewOf(e);
		if (preview != null)
		{
			float fit = Math.Min((float)inner.Width / preview.Width, (float)inner.Height / preview.Height);
			int w = Math.Max(1, (int)(preview.Width * fit));
			int h = Math.Max(1, (int)(preview.Height * fit));
			sb.Draw(preview, new Rectangle(inner.Center.X - w / 2, inner.Center.Y - h / 2, w, h), Color.White);
		}
		else
		{
			UiKit.Text(sb, "[c/9AA7BE:" + IsaClient.Languages.L10n.T(e.Ready ? "Drawing..." : "Not here yet") + "]", inner.Center.X, inner.Center.Y, Color.White, 0.75f, inner.Width, centered: true);
		}
		float textX = map.Right + 16;
		float textW = panel.Right - 20 - textX;
		UiKit.Text(sb, string.IsNullOrWhiteSpace(e.Name) ? IsaClient.Languages.L10n.T("Schematic") : e.Name, textX, map.Y + 14, Color.White, 1f, textW);
		UiKit.Text(sb, IsaClient.Languages.L10n.F("{0} x {1} tiles", e.Width, e.Height), textX, map.Y + 40, Color.White, 0.85f, textW);
		if (!string.IsNullOrEmpty(e.From) || !string.IsNullOrEmpty(_item.From))
		{
			UiKit.Text(sb, "[c/9AA7BE:" + IsaClient.Languages.L10n.F("From {0}", string.IsNullOrEmpty(_item.From) ? e.From : _item.From) + "]", textX, map.Y + 64, Color.White, 0.75f, textW);
		}
		string state;
		if (e.Uploading)
		{
			state = IsaClient.Languages.L10n.T("Uploading") + " " + (int)(e.Progress * 100f) + "%";
		}
		else if (e.Ready)
		{
			state = "[c/8CF08C:" + IsaClient.Languages.L10n.T(e.Mine ? (e.Uploaded ? "Shared on archive.org" : "Ready") : "Received") + "]";
		}
		else if (e.Failed)
		{
			state = "[c/FF8C8C:" + IsaClient.Languages.L10n.T("Couldn't get it from archive.org.") + "]";
		}
		else
		{
			ShareTransfer.Transfer pipe = ShareTransfer.Get(e.Code);
			float progress = Math.Max(e.Progress, (pipe != null) ? pipe.Fraction : 0f);
			state = IsaClient.Languages.L10n.T(e.Status == "Waiting for archive.org" && progress <= 0f ? "Waiting for archive.org" : "Receiving") + " " + (int)(progress * 100f) + "%";
		}
		UiKit.Text(sb, state, textX, map.Y + 90, Color.White, 0.75f, textW);
		UiKit.Text(sb, "[c/9AA7BE:" + IsaClient.Languages.L10n.T("Pasting works like dropping the file on the game.") + "]", textX, map.Y + 114, Color.White, 0.7f, textW);
		int bw = (panel.Width - 48 - 10) / 2;
		int by = map.Bottom + 18;
		if (UiKit.Button(sb, new Rectangle(panel.X + 24, by, bw, 38), "Paste it", UiKit.Green, enabled: e.Ready && !Main.gameMenu, "Loads it to paste, like dropping the file on the game."))
		{
			SchematicShare.Paste(e);
			Close();
			return;
		}
		if (UiKit.Button(sb, new Rectangle(panel.X + 34 + bw, by, bw, 38), "Save to my schematics", UiKit.Blue, enabled: e.Ready, "Copies it into your Schematics folder (never over another file)."))
		{
			SchematicShare.SaveCopy(e);
		}
		by += 46;
		if (UiKit.Button(sb, new Rectangle(panel.X + 24, by, bw, 38), "Send in chat", UiKit.Purple, enabled: e.Ready && !Main.gameMenu, "Links it in the chat line again."))
		{
			Close();
			ShareChat.LinkSchematic(e.Path);
			return;
		}
		if (UiKit.Button(sb, new Rectangle(panel.X + 34 + bw, by, bw, 38), "Close", UiKit.Red))
		{
			Close();
		}
	}
}
