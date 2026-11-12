using System;

namespace IsaClient.Core;

/// <summary>
/// Marks a static field as a saved setting: <see cref="ClientSettings"/> loads it from and saves it to IsaConfig.json
/// under <see cref="Key"/>. Numbers are clamped to <see cref="Min"/>..<see cref="Max"/> when loaded; enums are stored by name.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class SettingAttribute : Attribute
{
	public SettingAttribute(string key)
	{
		Key = key;
	}

	/// <summary>The name in IsaConfig.json. Never change it: users would lose the saved value.</summary>
	public string Key { get; }

	/// <summary>A cheat toggle: only restored when "Remember Enabled Cheats" is on (always saved).</summary>
	public bool Remembered { get; set; }

	public double Min { get; set; } = double.NaN;

	public double Max { get; set; } = double.NaN;

	/// <summary>An older name of the key, read when the current one is not in the file yet.</summary>
	public string LegacyKey { get; set; }
}
