using System;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;

namespace IsaClient.Core;

/// <summary>
/// "Sharp Display": the game tells Windows it handles screen scaling itself (DPI aware), so with Windows at 125% or 150%
/// it draws at the screen's real resolution: sharper, and the interface looks smaller. Off (the default), Windows
/// stretches the game like always. It only works before the window exists, so it is read from IsaConfig.json at
/// launch and a change takes effect on the next start.
/// </summary>
public static class DisplayScaling
{
	[Setting("DpiAware")]
	public static bool DpiAware;

	/// <summary>What this session started with (the setting may change while playing).</summary>
	public static bool AppliedAtStart { get; private set; }

	[DllImport("user32.dll")]
	private static extern bool SetProcessDPIAware();

	/// <summary>Program.LaunchGame, before the game window is made.</summary>
	public static void ApplyAtStart(string savePath)
	{
		try
		{
			string config = Path.Combine(savePath, "IsaClient", "IsaConfig.json");
			if (File.Exists(config) && (bool?)JObject.Parse(File.ReadAllText(config))["DpiAware"] == true)
			{
				AppliedAtStart = SetProcessDPIAware();
			}
		}
		catch
		{
			// A broken config never stops the game from starting.
		}
	}

	/// <summary>The panel switch: saved, and the chat says it needs a restart.</summary>
	public static void Set(bool on)
	{
		DpiAware = on;
		ClientSettings.Save();
		ClientChat.Info("Sharp Display " + (on ? "on" : "off") + ": restart the game to see it" + ((on == AppliedAtStart) ? " (this session already looks like that)." : "."));
	}
}
