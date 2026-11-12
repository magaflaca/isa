using System.Globalization;
using IsaClient.Core;
using Terraria;

namespace IsaClient.Rendering;

/// <summary>
/// Fullbright / Global Brightness (Cheats → Render): every tile gets at least this much light. At 100% lighting
/// is skipped entirely and everything is drawn at full brightness. Commands: .fb / .fullbright [full|off|percent].
/// </summary>
public static class FullbrightLighting
{
	/// <summary>0 = off, 1 = 100% (no lighting at all).</summary>
	[Setting("GlobalBrightness", Min = 0f, Max = 1f)]
	public static float Percent;

	private static float _lastOn = 0.5f;

	public static bool Full => Percent >= 1f;

	/// <summary>After loading: the brightness used when Fullbright is turned back on.</summary>
	internal static void OnSettingsLoaded()
	{
		if (Percent > 0f)
		{
			_lastOn = Percent;
		}
	}

	public static void Set(float percent, bool save = true)
	{
		Percent = Utils.Clamp(percent, 0f, 1f);
		if (Percent > 0f)
		{
			_lastOn = Percent;
		}
		if (save)
		{
			ClientSettings.Save();
		}
	}

	public static string Label => (Percent > 0f) ? ((Percent * 100f).ToString("0") + "%") : "Off";

	/// <summary>.fb / .fullbright: toggles it (back to the last brightness used), or .fb full | off | 75.</summary>
	public static bool HandleCommand(string text, string[] array)
	{
		if (text != "fb" && text != "fullbright")
		{
			return false;
		}
		string arg = (array.Length > 1) ? array[1].ToLowerInvariant().TrimEnd('%') : "";
		if (arg == "full" || arg == "max")
		{
			Set(1f);
		}
		else if (arg == "off" || arg == "0")
		{
			Set(0f);
		}
		else if (arg == "half")
		{
			Set(0.5f);
		}
		else if (float.TryParse(arg.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
		{
			Set((value > 1f) ? (value / 100f) : value);
		}
		else
		{
			Set((Percent > 0f) ? 0f : _lastOn);
		}
		ClientChat.Info("Fullbright: " + Label + ((Percent > 0f) ? (Full ? " (full brightness, no lighting)" : " minimum brightness") : "") + ".  (.fb full | off | <percent>)");
		return true;
	}
}
