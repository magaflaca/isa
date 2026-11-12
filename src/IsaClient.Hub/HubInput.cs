using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;

using IsaClient.Languages;

namespace IsaClient.Hub;

/// <summary>
/// Mouse, keyboard focus, clipping and animation state of the new panel for one frame. The panel is immediate mode:
/// every control asks here whether the mouse is on it and whether it was clicked. Coordinates are "hub units": UI units
/// divided by the panel's own size setting (<see cref="Scale"/>).
/// </summary>
internal static class HubInput
{
	internal static readonly object TextMarker = new object();

	public static float Scale = 1f;

	public static Matrix Matrix = Matrix.Identity;

	public static Point Mouse;

	public static bool Clicked;

	public static bool RightClicked;

	public static bool Down;

	/// <summary>Wheel turned over the panel since the last frame (taken from the game in <see cref="ClientHub.Update"/>).</summary>
	public static int Wheel;

	public static string Hovered;

	private static string _lastHovered;

	public static string Tooltip;

	public static string Focus;

	private static bool _focusSetNow;

	public static bool Clipping;

	/// <summary>
	/// A box inside the page with its own scroll (set by the page each frame): the mouse wheel over it scrolls the box,
	/// not the page.
	/// </summary>
	public static Rectangle InnerScroll;

	public static Rectangle Clip;

	/// <summary>An open popup (menu, picker): nothing under it sees the mouse.</summary>
	public static Rectangle Popup = Rectangle.Empty;

	public static bool PopupOpen;

	private static readonly Dictionary<string, float> _anim = new Dictionary<string, float>();

	private static bool _consumed;

	/// <summary>
	/// The click that opened the panel from somewhere else (an item linked in chat) is not a click on the panel: it's
	/// ignored until the button is let go.
	/// </summary>
	public static bool IgnoreUntilRelease;

	public static void Begin(bool mouseAllowed)
	{
		Scale = HubPrefs.Scale;
		Matrix = Matrix.CreateScale(Scale) * Main.UIScaleMatrix;
		if (mouseAllowed)
		{
			Mouse = new Point((int)(Main.mouseX / Scale), (int)(Main.mouseY / Scale));
			Clicked = Main.mouseLeft && Main.mouseLeftRelease;
			RightClicked = Main.mouseRight && Main.mouseRightRelease;
			Down = Main.mouseLeft;
			if (IgnoreUntilRelease)
			{
				if (Main.mouseLeft)
				{
					Clicked = Down = false;
				}
				else
				{
					IgnoreUntilRelease = false;
				}
			}
		}
		else
		{
			Mouse = new Point(-100000, -100000);
			Clicked = RightClicked = Down = false;
			Wheel = 0;
		}
		Hovered = null;
		Tooltip = null;
		_focusSetNow = false;
		_consumed = false;
	}

	/// <summary>After the frame: hover sound, click clean-up and the hover text.</summary>
	public static void End(SpriteBatch sb)
	{
		if (Clicked && Focus != null && !_focusSetNow && (Hovered == null || !Hovered.StartsWith("field:")))
		{
			Focus = null;
		}
		if (Hovered != null && Hovered != _lastHovered && !Hovered.StartsWith("field:") && !Hovered.StartsWith("card:"))
		{
			Sound(12);
		}
		_lastHovered = Hovered;
		if (_consumed)
		{
			Main.mouseLeftRelease = false;
			Main.mouseRightRelease = false;
		}
		Wheel = 0;
	}

	/// <summary>The click was used: nothing else this frame (game, other UI) gets it.</summary>
	public static void Consume()
	{
		_consumed = true;
	}

	public static void SetFocus(string id)
	{
		Focus = id;
		_focusSetNow = true;
		Main.clrInput();
	}

	public static bool In(Rectangle r)
	{
		if (!r.Contains(Mouse))
		{
			return false;
		}
		if (Clipping && !Clip.Contains(Mouse))
		{
			return false;
		}
		if (PopupOpen && !_drawingPopup && Popup.Contains(Mouse))
		{
			return false;
		}
		return true;
	}

	private static bool _drawingPopup;

	/// <summary>While a popup draws itself, its own area takes the mouse.</summary>
	public static void DrawingPopup(bool value)
	{
		_drawingPopup = value;
	}

	public static bool Visible(Rectangle r)
	{
		return !Clipping || (r.Bottom > Clip.Top && r.Top < Clip.Bottom);
	}

	/// <summary>Marks a control as hovered; returns true when it was left clicked this frame.</summary>
	public static bool Hover(string id, string tooltip = null)
	{
		Hovered = id;
		if (tooltip != null)
		{
			Tooltip = L10n.T(tooltip);
		}
		if (Main.LocalPlayer != null)
		{
			Main.LocalPlayer.mouseInterface = true;
		}
		if (Clicked)
		{
			Consume();
			return true;
		}
		return false;
	}

	/// <summary>A value easing towards 1 while <paramref name="on"/>, towards 0 otherwise (instant when animations are off).</summary>
	public static float Anim(string id, bool on, float speed = 0.2f)
	{
		float target = on ? 1f : 0f;
		if (!HubPrefs.Animations)
		{
			_anim[id] = target;
			return target;
		}
		if (!_anim.TryGetValue(id, out float value))
		{
			value = target;
		}
		value += (target - value) * speed;
		if (Math.Abs(target - value) < 0.01f)
		{
			value = target;
		}
		_anim[id] = value;
		return value;
	}

	public static void Sound(int type)
	{
		if (HubPrefs.Sounds)
		{
			SoundEngine.PlaySound(type);
		}
	}

	public static Rectangle ToScreen(Rectangle hub)
	{
		Vector2 a = Vector2.Transform(new Vector2(hub.X, hub.Y), Matrix);
		Vector2 b = Vector2.Transform(new Vector2(hub.Right, hub.Bottom), Matrix);
		return new Rectangle((int)a.X, (int)a.Y, (int)(b.X - a.X), (int)(b.Y - a.Y));
	}

	/// <summary>The panel's batch: hub units, the game's UI sampler.</summary>
	public static void Restart(SpriteBatch sb, bool scissor = false)
	{
		sb.End();
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.SamplerStateForCursor, DepthStencilState.None, scissor ? new RasterizerState
		{
			CullMode = CullMode.None,
			ScissorTestEnable = true
		} : RasterizerState.CullCounterClockwise, null, Matrix);
	}

	public static void BeginClip(SpriteBatch sb, Rectangle area)
	{
		Rectangle screen = ToScreen(area);
		Rectangle scissor = Rectangle.Intersect(screen, sb.GraphicsDevice.Viewport.Bounds);
		if (scissor.Width <= 0 || scissor.Height <= 0)
		{
			scissor = new Rectangle(0, 0, 1, 1);
		}
		sb.End();
		sb.GraphicsDevice.ScissorRectangle = scissor;
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.SamplerStateForCursor, DepthStencilState.None, new RasterizerState
		{
			CullMode = CullMode.None,
			ScissorTestEnable = true
		}, null, Matrix);
		Clip = area;
		Clipping = true;
	}

	public static void EndClip(SpriteBatch sb)
	{
		sb.End();
		// Terraria's UI clips its lists against the current scissor: leave the whole screen.
		sb.GraphicsDevice.ScissorRectangle = sb.GraphicsDevice.Viewport.Bounds;
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.SamplerStateForCursor, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Matrix);
		Clipping = false;
	}

	/// <summary>Typing into a field this frame (search, names...).</summary>
	public static string TypeInto(string value, int maxLength)
	{
		PlayerInput.WritingText = true;
		Main.CurrentInputTextTakerOverride = TextMarker;
		Main.chatRelease = false;
		Main.instance.HandleIME();
		string typed = Main.GetInputText(value ?? "");
		if (typed.Length > maxLength)
		{
			typed = typed.Substring(0, maxLength);
		}
		if (Main.inputTextEscape)
		{
			Main.inputTextEscape = false;
			Focus = null;
		}
		return typed;
	}
}
