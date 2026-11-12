using System;
using IsaClient.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameInput;
using Terraria.UI;

namespace IsaClient.Hub;

/// <summary>
/// Hosts a classic UI element (the player list rows, the bots panel) inside a page of the new panel: the element lives
/// in its own <see cref="UserInterface"/>, is laid out on the page's area every frame and gets the mouse only when the
/// panel has it.
/// </summary>
internal sealed class HubEmbed
{
	private readonly Func<UIElement> _build;

	private readonly UserInterface _ui = new UserInterface();

	private UIState _state;

	private UIElement _root;

	private Rectangle _laidOut;

	/// <summary>The area in UI units, last frame (the mouse wheel goes to the element there).</summary>
	public Rectangle ScreenArea { get; private set; }

	public HubEmbed(Func<UIElement> build)
	{
		_build = build;
	}

	public void Draw(SpriteBatch sb, Rectangle hubArea, bool mouseAllowed, GameTime time)
	{
		try
		{
			if (_state == null)
			{
				_state = new UIState();
				_root = _build();
				// The root is placed by pixels on the page: classic alignment (bots panel: bottom) would move it away.
				_root.HAlign = 0f;
				_root.VAlign = 0f;
				_root.MarginLeft = _root.MarginRight = _root.MarginTop = _root.MarginBottom = 0f;
				_state.Append(_root);
				_ui.SetState(_state);
				_laidOut = Rectangle.Empty;
			}
			float scale = HubInput.Scale;
			Rectangle area = new Rectangle((int)(hubArea.X * scale), (int)(hubArea.Y * scale), (int)(hubArea.Width * scale), (int)(hubArea.Height * scale));
			ScreenArea = area;
			if (area != _laidOut)
			{
				_laidOut = area;
				_root.Left.Set(area.X, 0f);
				_root.Top.Set(area.Y, 0f);
				_root.Width.Set(area.Width, 0f);
				_root.Height.Set(area.Height, 0f);
				_state.Recalculate();
			}
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.SamplerStateForCursor, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
			int realX = Main.mouseX;
			int realY = Main.mouseY;
			bool inside = mouseAllowed && area.Contains(realX, realY);
			if (!inside)
			{
				Main.mouseX = -100000;
				Main.mouseY = -100000;
			}
			_ui.Update(time);
			_ui.Draw(sb, time);
			Main.mouseX = realX;
			Main.mouseY = realY;
			if (inside && Main.LocalPlayer != null)
			{
				Main.LocalPlayer.mouseInterface = true;
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
			_state = null;
		}
		HubInput.Restart(sb);
	}

	/// <summary>Builds the element again next frame (after the session changed, for example).</summary>
	public void Reset()
	{
		_state = null;
		_ui.SetState(null);
	}
}
