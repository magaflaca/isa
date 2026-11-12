using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameInput;
using IsaClient.Core;

namespace IsaClient.Menu;

/// <summary>
/// A small icon on the screen (see <see cref="HudButtons"/>): click it to run its action, hold it and drag to move it
/// anywhere. The main menu and the game remember their own position (<see cref="HudButtons.Positions"/>).
/// </summary>
public sealed class HudButton
{
	/// <summary>How far (pixels) the mouse has to move while held before it drags instead of clicking.</summary>
	private const float DragThreshold = 4f;

	/// <summary>Only one button can be held at a time.</summary>
	private static HudButton _pressed;

	private static bool _dragging;

	private static Vector2 _pressPoint;

	private static Vector2 _grabOffset;

	private readonly Action _click;

	private readonly Func<string> _tooltip;

	private readonly Action<SpriteBatch, Vector2, float, bool> _drawIcon;

	/// <param name="key">Name used for its saved position and in .hud.</param>
	/// <param name="click">What a click does.</param>
	/// <param name="tooltip">Hover text.</param>
	/// <param name="drawIcon">Draws the icon: sprite batch, center, size, hovered.</param>
	public HudButton(string key, Action click, Func<string> tooltip, Action<SpriteBatch, Vector2, float, bool> drawIcon)
	{
		Key = key;
		_click = click;
		_tooltip = tooltip;
		_drawIcon = drawIcon;
	}

	public string Key { get; }

	private string PositionKey => Key + (Main.gameMenu ? ".menu" : ".game");

	/// <summary>Draws it at its saved place, or centered on <paramref name="defaultCenter"/>, at most <paramref name="size"/> pixels.</summary>
	public void Draw(SpriteBatch spriteBatch, Vector2 defaultCenter, float size)
	{
		Vector2 center = HudButtons.Positions.TryGetValue(PositionKey, out float[] saved) && saved != null && saved.Length == 2
			? ClampToScreen(new Vector2(saved[0], saved[1]), size)
			: defaultCenter;
		Vector2 mouse = new Vector2(Main.mouseX, Main.mouseY);
		bool covered = IsaClient.Music.MusicPlayerWindow.CoversUiPoint(new Point(Main.mouseX, Main.mouseY));
		bool hover = !covered && MenuIcon.Contains(new Rectangle((int)(center.X - size / 2f), (int)(center.Y - size / 2f), (int)size, (int)size), mouse);
		if (hover && _pressed == null && !PlayerInput.IgnoreMouseInterface && Main.mouseLeft && Main.mouseLeftRelease)
		{
			Main.mouseLeftRelease = false;
			_pressed = this;
			_dragging = false;
			_pressPoint = mouse;
			_grabOffset = center - mouse;
		}
		bool held = _pressed == this;
		if (held)
		{
			if (Main.mouseLeft)
			{
				if (!_dragging && Vector2.Distance(mouse, _pressPoint) > DragThreshold)
				{
					_dragging = true;
				}
				if (_dragging)
				{
					center = ClampToScreen(mouse + _grabOffset, size);
					HudButtons.Positions[PositionKey] = new[] { center.X, center.Y };
				}
			}
			else
			{
				if (_dragging)
				{
					ClientSettings.Save();
				}
				else
				{
					_click();
				}
				_pressed = null;
				_dragging = false;
			}
		}
		if (hover || held)
		{
			Main.LocalPlayer.mouseInterface = true;
			if (!_dragging)
			{
				Main.instance.MouseText(IsaClient.Languages.L10n.T(_tooltip() + " (hold to move)"), 0, 0);
			}
		}
		_drawIcon(spriteBatch, center, size, hover || held);
	}

	/// <summary>Lets go of a held button (when buttons are turned off).</summary>
	internal static void Release()
	{
		_pressed = null;
		_dragging = false;
	}

	/// <summary>Screen size in UI coordinates (the space the buttons are drawn and the mouse is read in).</summary>
	internal static Vector2 UiScreenSize => new Vector2(Main.instance.GraphicsDevice.Viewport.Width, Main.instance.GraphicsDevice.Viewport.Height) / Main.UIScale;

	private static Vector2 ClampToScreen(Vector2 center, float size)
	{
		float half = size / 2f;
		Vector2 screen = UiScreenSize;
		return new Vector2(MathHelper.Clamp(center.X, half, Math.Max(half, screen.X - half)), MathHelper.Clamp(center.Y, half, Math.Max(half, screen.Y - half)));
	}
}
