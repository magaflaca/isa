using System.Collections.Generic;
using Terraria.GameInput;

namespace Terraria.GameContent.UI.States;

public partial class UIManageControls
{
	public static List<string> customKeybindList = new List<string>
	{
		"Open Menu", "Load Config", "Use Brush", "Use Brush (Map)", "Mouse TP", "Map TP", "Hold for Activator", "Hold for Activator (Map)", "Hold for Rapidfire", "Noclip",
		"Noclip Speed Modifier", "Undo Paste", "Free Camera", "AFK AI", "Wiki Lookup", "Auto Clicker", "Ghost Mode"
	};

	// IsaClient's keys in the controls menu, by section (index, title, keys). Lock-On is the game's own controller
	// trigger, which vanilla only lists for gamepads: IsaClient makes it work on keyboard, so it's listed here too.
	private static readonly (int Index, string Title, List<string> Keys)[] _isaGroups =
	{
		(13, "IsaClient: General", new List<string> { "Open Menu", "Load Config", "Wiki Lookup", "Free Camera", "AFK AI", "Ghost Mode" }),
		(14, "IsaClient: Combat", new List<string> { "LockOn", "Hold for Rapidfire", "Auto Clicker" }),
		(15, "IsaClient: Movement", new List<string> { "Noclip", "Noclip Speed Modifier", "Mouse TP", "Map TP" }),
		(16, "IsaClient: Building", new List<string> { "Hold for Activator", "Hold for Activator (Map)", "Use Brush", "Use Brush (Map)", "Undo Paste" })
	};

	private void AddClientBindingGroups()
	{
		foreach (var group in _isaGroups)
		{
			// Gamepads already have Lock-On in their main section.
			List<string> pad = group.Keys.FindAll(k => k != "LockOn");
			_bindsKeyboard.Add(CreateBindingGroup(group.Index, group.Keys, InputMode.Keyboard));
			_bindsKeyboardUI.Add(CreateBindingGroup(group.Index, group.Keys, InputMode.KeyboardUI));
			_bindsGamepad.Add(CreateBindingGroup(group.Index, pad, InputMode.XBoxGamepad));
			_bindsGamepadUI.Add(CreateBindingGroup(group.Index, pad, InputMode.XBoxGamepadUI));
		}
		_bindsKeyboard.Add(CreateBindingGroup(17, IsaClient.Music.MusicPlayerWindow.HotkeyTriggers, InputMode.Keyboard));
		_bindsKeyboardUI.Add(CreateBindingGroup(17, IsaClient.Music.MusicPlayerWindow.HotkeyTriggers, InputMode.KeyboardUI));
		_bindsGamepad.Add(CreateBindingGroup(17, IsaClient.Music.MusicPlayerWindow.HotkeyTriggers, InputMode.XBoxGamepad));
		_bindsGamepadUI.Add(CreateBindingGroup(17, IsaClient.Music.MusicPlayerWindow.HotkeyTriggers, InputMode.XBoxGamepadUI));
	}
}
