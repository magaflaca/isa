using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;

namespace IsaClient.Core;

/// <summary>
/// A copy of IsaClient's own key bindings (Open Menu, Noclip, Wiki Lookup...) in IsaClient/IsaKeys.json. Every Terraria
/// shares "input profiles.json", and vanilla rewrites it without the bindings it doesn't know, so after playing vanilla
/// they came back as the defaults. When a profile is loaded without them, they are taken from this copy.
/// </summary>
public static class ClientKeys
{
	private static readonly InputMode[] Modes = { InputMode.Keyboard, InputMode.KeyboardUI };

	private static string FilePath => Path.Combine(ClientSettings.IsaClientFolder, "IsaKeys.json");

	private static Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> _saved;

	public static void Save()
	{
		try
		{
			Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> all = new Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>();
			foreach (KeyValuePair<string, PlayerInputProfile> profile in PlayerInput.Profiles)
			{
				Dictionary<string, Dictionary<string, List<string>>> modes = new Dictionary<string, Dictionary<string, List<string>>>();
				foreach (InputMode mode in Modes)
				{
					Dictionary<string, List<string>> keys = new Dictionary<string, List<string>>();
					foreach (string trigger in UIManageControls.customKeybindList)
					{
						if (profile.Value.InputModes[mode].KeyStatus.TryGetValue(trigger, out List<string> list))
						{
							keys[trigger] = new List<string>(list);
						}
					}
					modes[mode.ToString()] = keys;
				}
				all[profile.Value.Name] = modes;
			}
			Directory.CreateDirectory(ClientSettings.IsaClientFolder);
			File.WriteAllText(FilePath, JsonConvert.SerializeObject(all, Formatting.Indented));
			_saved = all;
		}
		catch (Exception ex)
		{
			ClientLog.LogOnce(ex);
		}
	}

	public static void Restore(PlayerInputProfile profile, Dictionary<string, object> stored)
	{
		Dictionary<string, Dictionary<string, List<string>>> modes = SavedFor(profile.Name);
		if (modes == null)
		{
			return;
		}
		foreach (InputMode mode in Modes)
		{
			if (!modes.TryGetValue(mode.ToString(), out Dictionary<string, List<string>> keys))
			{
				continue;
			}
			JObject inFile = (stored.TryGetValue((mode == InputMode.Keyboard) ? "Mouse And Keyboard" : "Mouse And Keyboard UI", out object value) ? (value as JObject) : null);
			foreach (KeyValuePair<string, List<string>> binding in keys)
			{
				if ((inFile == null || inFile[binding.Key] == null) && profile.InputModes[mode].KeyStatus.TryGetValue(binding.Key, out List<string> list))
				{
					list.Clear();
					list.AddRange(binding.Value);
				}
			}
		}
	}

	private static Dictionary<string, Dictionary<string, List<string>>> SavedFor(string profile)
	{
		try
		{
			if (_saved == null)
			{
				_saved = File.Exists(FilePath) ? JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>>(File.ReadAllText(FilePath)) : new Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>();
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogOnce(ex);
			_saved = new Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>();
		}
		if (_saved == null || !_saved.TryGetValue(profile ?? "", out Dictionary<string, Dictionary<string, List<string>>> modes))
		{
			return null;
		}
		return modes;
	}
}
