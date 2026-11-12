using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.UI.Chat;
using IsaClient.Core;
using Terraria;

using IsaClient.Languages;

namespace IsaClient.Menu;

/// <summary>
/// Small immediate-mode UI helpers (drawn and hit-tested in one call, in UI coordinates) shared by the character window
/// and the .plr drop dialog: framed buttons with hover sound, toggles, text that never overflows, and character portraits.
/// </summary>
public static class UiKit
{
	public static readonly Color Blue = new Color(63, 82, 151);

	public static readonly Color Green = new Color(56, 128, 84);

	public static readonly Color Purple = new Color(96, 82, 170);

	public static readonly Color Red = new Color(150, 58, 58);

	public static readonly Color Amber = new Color(150, 115, 50);

	public static readonly Color Muted = new Color(154, 167, 190);

	private static string _hovered;

	private static string _lastHovered;

	/// <summary>Call once per frame after drawing a screen of buttons: plays the hover tick when a new button is hovered.</summary>
	public static void EndFrame()
	{
		if (_hovered != null && _hovered != _lastHovered)
		{
			SoundEngine.PlaySound(12);
		}
		_lastHovered = _hovered;
		_hovered = null;
	}

	public static DynamicSpriteFont Font => FontAssets.MouseText.Value;

	public static Point Mouse => new Point(Main.mouseX, Main.mouseY);

	public static bool Clicked => Main.mouseLeft && Main.mouseLeftRelease;

	public static bool Button(SpriteBatch sb, Rectangle r, string label, Color color, bool enabled = true, string tooltip = null)
	{
		bool hover = enabled && r.Contains(Mouse);
		if (r.Contains(Mouse))
		{
			Main.LocalPlayer.mouseInterface = true;
			if (tooltip != null)
			{
				Main.instance.MouseText(L10n.T(tooltip), 0, 0);
			}
		}
		if (hover)
		{
			_hovered = label + r;
		}
		Color back = enabled ? (hover ? Color.Lerp(color, Color.White, 0.2f) : color) : (color * 0.45f);
		Utils.DrawInvBG(sb, r, back * 0.95f);
		if (hover)
		{
			Utils.DrawInvBG(sb, r, new Color(255, 231, 69) * 0.12f);
		}
		Text(sb, label, r.Center.X, r.Center.Y, enabled ? (hover ? Color.White : new Color(225, 230, 240)) : Muted, 0.85f, r.Width - 10, centered: true);
		if (hover && Clicked)
		{
			Main.mouseLeftRelease = false;
			SoundEngine.PlaySound(12);
			return true;
		}
		return false;
	}

	/// <summary>A button showing "label: on/off" in green/grey; returns true when clicked (the caller flips the value).</summary>
	public static bool Toggle(SpriteBatch sb, Rectangle r, string label, bool value, string tooltip = null)
	{
		return Button(sb, r, L10n.T(label) + L10n.T(value ? ": on" : ": off"), value ? Green : new Color(70, 76, 105), enabled: true, tooltip);
	}

	public static float Measure(string text, float scale)
	{
		return ChatManager.GetStringSize(Font, L10n.T(text) ?? "", new Vector2(scale)).X;
	}

	/// <summary>Draws text (chat tags allowed), shrinking it to <paramref name="maxWidth"/> if needed. Y is the vertical center.</summary>
	public static void Text(SpriteBatch sb, string text, float x, float centerY, Color color, float scale, float maxWidth = 0f, bool centered = false)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		// In the player's language (IsaClient.Languages.L10n).
		text = L10n.T(text);
		float width = ChatManager.GetStringSize(Font, text, new Vector2(scale)).X;
		if (maxWidth > 0f && width > maxWidth)
		{
			scale *= maxWidth / width;
			width = maxWidth;
		}
		Vector2 size = ChatManager.GetStringSize(Font, text, new Vector2(scale));
		Vector2 pos = new Vector2(centered ? (x - width / 2f) : x, centerY - size.Y / 2f + 4f * scale);
		ChatManager.DrawColorCodedStringWithShadow(sb, Font, text, pos, color, 0f, Vector2.Zero, new Vector2(scale), -1f, 1.5f);
	}

	public static void Panel(SpriteBatch sb, Rectangle r, Color color)
	{
		Utils.DrawInvBG(sb, r, color);
	}

	/// <summary>
	/// Draws a character standing in <paramref name="box"/> at full brightness, the way the character select screen does.
	/// <paramref name="player"/> is animated in place, so pass a copy, never a live player.
	/// </summary>
	public static void Portrait(SpriteBatch sb, Player player, Rectangle box, float scale, Color background)
	{
		if (background.A > 0)
		{
			Utils.DrawInvBG(sb, box, background);
		}
		if (player == null)
		{
			return;
		}
		try
		{
			player.isaAlwaysLit = true;
			player.socialIgnoreLight = true;
			player.direction = 1;
			player.gravDir = 1f;
			player.ResetEffects();
			player.ResetVisibleAccessories();
			player.UpdateMiscCounter();
			player.UpdateDyes();
			player.PlayerFrame();
			player.bodyFrame.Y = (player.legFrame.Y = (player.headFrame.Y = 0));
			player.isaAlwaysLit = true;
			int held = player.selectedItem;
			Item heldItem = player.inventory[held];
			player.inventory[held] = new Item();
			sb.End();
			sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
			// DrawPlayer scales around the bottom middle of the hitbox: place the hitbox so the scaled one is centered.
			Vector2 topLeft = new Vector2((float)box.Center.X - (float)player.width / 2f, (float)box.Center.Y + (float)player.height * scale / 2f - (float)player.height);
			Main.PlayerRenderer.DrawPlayer(Main.Camera, player, topLeft + Main.screenPosition, 0f, Vector2.Zero, 0f, scale);
			player.inventory[held] = heldItem;
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		finally
		{
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.SamplerStateForCursor, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
		}
	}

	/// <summary>One-line summary of a character for lists and dialogs.</summary>
	public static string Summary(Player p)
	{
		string difficulty = p.difficulty switch
		{
			1 => "Mediumcore",
			2 => "Hardcore",
			3 => "Journey",
			_ => "Classic"
		};
		return difficulty + "  [c/FF6E6E:" + p.statLifeMax + " life]  [c/6E96FF:" + p.statManaMax + " mana]";
	}
}
