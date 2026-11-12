using System;
using System.Collections.Generic;
using ImGuiNET;
using Microsoft.Xna.Framework.Input;
using ReLogic.Localization.IME;
using ReLogic.OS;
using Terraria;

namespace IsaClient.Plugins;

/// <summary>
/// Feeds ImGui the mouse, the keys and the typed characters (from the game's IME listener, so accents and dead keys
/// work like in the chat). Nothing reaches ImGui while the game window is in the background.
/// </summary>
internal static class ImGuiInput
{
	private static readonly Keys[] _allKeys = (Keys[])Enum.GetValues(typeof(Keys));

	private static readonly Queue<char> _typed = new Queue<char>();

	private static int _lastWheel;

	private static bool _listening;

	private static bool _hadFocus = true;

	public static void Listen()
	{
		if (_listening)
		{
			return;
		}
		_listening = true;
		Platform.Get<IImeService>().AddKeyListener(delegate(char c)
		{
			lock (_typed)
			{
				if (_typed.Count < 64)
				{
					_typed.Enqueue(c);
				}
			}
		});
		_lastWheel = Mouse.GetState().ScrollWheelValue;
	}

	public static void Update()
	{
		ImGuiIOPtr io = ImGui.GetIO();
		bool focus = Main.instance.IsActive;
		if (focus != _hadFocus)
		{
			io.AddFocusEvent(focus);
			_hadFocus = focus;
		}
		MouseState mouse = Mouse.GetState();
		if (!focus)
		{
			lock (_typed)
			{
				_typed.Clear();
			}
			_lastWheel = mouse.ScrollWheelValue;
			return;
		}
		KeyboardState keyboard = Keyboard.GetState();
		foreach (Keys key in _allKeys)
		{
			ImGuiKey imguiKey = Map(key);
			if (imguiKey != ImGuiKey.None)
			{
				io.AddKeyEvent(imguiKey, keyboard.IsKeyDown(key));
			}
		}
		io.AddKeyEvent(ImGuiKey.ModCtrl, keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl));
		io.AddKeyEvent(ImGuiKey.ModShift, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
		io.AddKeyEvent(ImGuiKey.ModAlt, keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt));
		io.AddKeyEvent(ImGuiKey.ModSuper, keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows));

		io.AddMousePosEvent(mouse.X, mouse.Y);
		io.AddMouseButtonEvent(0, mouse.LeftButton == ButtonState.Pressed);
		io.AddMouseButtonEvent(1, mouse.RightButton == ButtonState.Pressed);
		io.AddMouseButtonEvent(2, mouse.MiddleButton == ButtonState.Pressed);
		int wheel = mouse.ScrollWheelValue - _lastWheel;
		_lastWheel = mouse.ScrollWheelValue;
		if (wheel != 0)
		{
			io.AddMouseWheelEvent(0f, wheel / 120f);
		}

		lock (_typed)
		{
			// Characters only go to ImGui while one of its text boxes is focused (otherwise they are the game's).
			bool typing = io.WantTextInput;
			while (_typed.Count > 0)
			{
				char c = _typed.Dequeue();
				if (typing && c >= ' ' && c != 127)
				{
					io.AddInputCharacter(c);
				}
			}
		}
	}

	private static ImGuiKey Map(Keys key)
	{
		if (key >= Keys.D0 && key <= Keys.D9)
		{
			return ImGuiKey._0 + (key - Keys.D0);
		}
		if (key >= Keys.A && key <= Keys.Z)
		{
			return ImGuiKey.A + (key - Keys.A);
		}
		if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
		{
			return ImGuiKey.Keypad0 + (key - Keys.NumPad0);
		}
		if (key >= Keys.F1 && key <= Keys.F12)
		{
			return ImGuiKey.F1 + (key - Keys.F1);
		}
		return key switch
		{
			Keys.Back => ImGuiKey.Backspace,
			Keys.Tab => ImGuiKey.Tab,
			Keys.Enter => ImGuiKey.Enter,
			Keys.CapsLock => ImGuiKey.CapsLock,
			Keys.Escape => ImGuiKey.Escape,
			Keys.Space => ImGuiKey.Space,
			Keys.PageUp => ImGuiKey.PageUp,
			Keys.PageDown => ImGuiKey.PageDown,
			Keys.End => ImGuiKey.End,
			Keys.Home => ImGuiKey.Home,
			Keys.Left => ImGuiKey.LeftArrow,
			Keys.Right => ImGuiKey.RightArrow,
			Keys.Up => ImGuiKey.UpArrow,
			Keys.Down => ImGuiKey.DownArrow,
			Keys.Insert => ImGuiKey.Insert,
			Keys.Delete => ImGuiKey.Delete,
			Keys.Multiply => ImGuiKey.KeypadMultiply,
			Keys.Add => ImGuiKey.KeypadAdd,
			Keys.Subtract => ImGuiKey.KeypadSubtract,
			Keys.Decimal => ImGuiKey.KeypadDecimal,
			Keys.Divide => ImGuiKey.KeypadDivide,
			Keys.LeftShift => ImGuiKey.LeftShift,
			Keys.RightShift => ImGuiKey.RightShift,
			Keys.LeftControl => ImGuiKey.LeftCtrl,
			Keys.RightControl => ImGuiKey.RightCtrl,
			Keys.LeftAlt => ImGuiKey.LeftAlt,
			Keys.RightAlt => ImGuiKey.RightAlt,
			Keys.OemSemicolon => ImGuiKey.Semicolon,
			Keys.OemPlus => ImGuiKey.Equal,
			Keys.OemComma => ImGuiKey.Comma,
			Keys.OemMinus => ImGuiKey.Minus,
			Keys.OemPeriod => ImGuiKey.Period,
			Keys.OemQuestion => ImGuiKey.Slash,
			Keys.OemTilde => ImGuiKey.GraveAccent,
			Keys.OemOpenBrackets => ImGuiKey.LeftBracket,
			Keys.OemCloseBrackets => ImGuiKey.RightBracket,
			Keys.OemPipe => ImGuiKey.Backslash,
			Keys.OemQuotes => ImGuiKey.Apostrophe,
			_ => ImGuiKey.None
		};
	}
}
