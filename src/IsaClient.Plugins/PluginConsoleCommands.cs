using System;
using System.IO;
using System.Linq;
using IsaClient.Core;
using TerraAngel;

namespace IsaClient.Plugins;

/// <summary>.plugins and .pc in the chat, and the plugin commands of the plugin console.</summary>
public static class PluginConsoleCommands
{
	internal static void Register()
	{
		ClientLoader.Console.AddCommand("plugins", delegate
		{
			foreach (PluginEntry entry in PluginLoader.Entries)
			{
				ClientLoader.Console.WriteLine(entry.Key + ": " + State(entry));
			}
		}, "Lists the plugins and their state");
		ClientLoader.Console.AddCommand("reload", delegate(TerraAngel.UI.ClientWindows.Console.ConsoleWindow.CmdStr cmd)
		{
			foreach (PluginEntry entry in Targets(cmd.FullArgs))
			{
				PluginLoader.Reload(entry);
			}
		}, "Reloads one plugin (reload name) or every plugin that is on (reload)");
	}

	private static string State(PluginEntry entry)
	{
		if (entry.Error != null)
		{
			return "error: " + entry.Error;
		}
		if (entry.Loaded)
		{
			return entry.ChangedOnDisk ? "on (a new build is on disk: reload)" : "on";
		}
		return PluginLoader.IsEnabled(entry) ? "on (not loaded)" : "off";
	}

	private static PluginEntry[] Targets(string name)
	{
		PluginLoader.Scan();
		if (string.IsNullOrWhiteSpace(name))
		{
			return PluginLoader.Entries.Where(PluginLoader.IsEnabled).ToArray();
		}
		PluginEntry found = PluginLoader.Find(name.Trim());
		return (found == null) ? new PluginEntry[0] : new[] { found };
	}

	#region Chat commands

	/// <summary>.plugins [list | on/off/reload &lt;name&gt; | folder | console]</summary>
	internal static void PluginsCommand(CommandContext command)
	{
		if (!PluginHost.Started || PluginHost.SystemOff)
		{
			ClientChat.Error(PluginHost.SystemOff ? "Plugins are turned off (Settings > Client > Plugins & TerraAngel, or .plugins system on)." : (PluginHost.StartError ?? "The plugin system is still starting."));
			if (PluginHost.SystemOff && command.ArgumentCount >= 2 && command.Parts[1].Equals("system", StringComparison.OrdinalIgnoreCase) && command.Parts[2].Equals("on", StringComparison.OrdinalIgnoreCase))
			{
				PluginHost.SetSystemOn(true);
				ClientChat.Info("Plugins turned on: they start in a moment.");
			}
			return;
		}
		string action = (command.ArgumentCount >= 1) ? command.Parts[1].ToLowerInvariant() : "";
		string name = (command.ArgumentCount >= 2) ? string.Join(" ", command.Parts.Skip(2)) : "";
		switch (action)
		{
		case "":
			PluginHost.ToggleOverlay();
			break;
		case "list":
			PluginLoader.Scan();
			if (PluginLoader.Entries.Count == 0)
			{
				ClientChat.Info("No plugins in " + PluginLoader.PluginsFolder);
			}
			foreach (PluginEntry entry in PluginLoader.Entries)
			{
				ClientChat.Info(entry.Key + ": " + State(entry));
			}
			break;
		case "on":
		case "off":
		{
			PluginLoader.Scan();
			PluginEntry entry = PluginLoader.Find(name);
			if (entry == null)
			{
				ClientChat.Error("No plugin called " + name + " (.plugins list)");
				return;
			}
			PluginLoader.SetEnabled(entry, action == "on");
			ClientChat.Info(entry.Name + ": " + State(entry));
			break;
		}
		case "reload":
		{
			PluginEntry[] targets = Targets(name);
			if (targets.Length == 0)
			{
				ClientChat.Error("Nothing to reload.");
				return;
			}
			foreach (PluginEntry entry in targets)
			{
				PluginLoader.Reload(entry);
				ClientChat.Info(entry.Name + ": " + State(entry));
			}
			break;
		}
		case "folder":
			PluginOverlay.OpenFolder();
			break;
		case "forks":
		case "fork":
			PluginOverlay.OpenForksFolder();
			ClientChat.Info("Put your TerraAngel fork in a folder here (as you edit it), then turn it on in the plugin overlay. README.txt explains it.");
			break;
		case "devtools":
			DevTools.DevToolsInstaller.Ensure("plugin sources, .NET 10 builds and the C# console", () => ClientChat.Info("Developer tools ready: Plugins/Sources folders compile, TerraAngel .NET 10 builds convert, and the plugin console runs C#."));
			break;
		case "console":
			PluginHost.OverlayVisible = true;
			PluginHost.SetWindowShown(ClientLoader.ConsoleWindow, true);
			break;
		case "system":
			if (name.Equals("off", StringComparison.OrdinalIgnoreCase))
			{
				PluginHost.SetSystemOn(false);
				ClientChat.Info("Plugins, TerraAngel and ImGui are off (.plugins system on brings them back).");
			}
			break;
		default:
			ClientChat.Error("Usage: .plugins [list | on <name> | off <name> | reload [name] | folder | forks | console | devtools]");
			break;
		}
	}

	/// <summary>.ta: the plugin overlay (like Insert), turning the TerraAngel pack on first if it is off.</summary>
	internal static void TerraAngelCommand(CommandContext command)
	{
		if (!PluginHost.Started || PluginHost.SystemOff)
		{
			ClientChat.Error(PluginHost.SystemOff ? "Plugins are turned off (Settings > Client > Plugins & TerraAngel, or .plugins system on)." : (PluginHost.StartError ?? "The plugin system is still starting."));
			return;
		}
		PluginLoader.Scan();
		PluginEntry pack = PluginLoader.Entries.FirstOrDefault(e => e.File != null && Path.GetFileName(e.File).Equals("TerraAngel.IsaPlugin.dll", StringComparison.OrdinalIgnoreCase));
		if (pack != null && !pack.Loaded)
		{
			PluginLoader.SetEnabled(pack, true);
			ClientChat.Info("TerraAngel: " + State(pack));
			PluginHost.OverlayVisible = true;
			return;
		}
		if (pack == null)
		{
			ClientChat.Notice("The TerraAngel pack (TerraAngel.IsaPlugin.dll) is not in the Plugins folder.");
		}
		PluginHost.ToggleOverlay();
	}

	/// <summary>.pc &lt;command&gt;: runs a plugin console command from the chat.</summary>
	internal static void ConsoleCommand(CommandContext command)
	{
		if (!PluginHost.Started || PluginHost.SystemOff)
		{
			ClientChat.Error(PluginHost.SystemOff ? "Plugins are turned off (Settings > Client > Plugins & TerraAngel, or .plugins system on)." : (PluginHost.StartError ?? "The plugin system is not running."));
			return;
		}
		if (command.ArgumentCount < 1)
		{
			ClientChat.Error("Usage: .pc <command> (.pc help lists them)");
			return;
		}
		TerraAngel.UI.ClientWindows.Console.ConsoleWindow console = ClientLoader.ConsoleWindow;
		int before = console.ConsoleItems.Count;
		// C# lines finish later: their result goes to the chat by itself.
		DevTools.ScriptConsole.EchoNextToChat = true;
		console.ExecuteCommand(command.Rest);
		DevTools.ScriptConsole.EchoNextToChat = false;
		// What the command printed also goes to the chat.
		foreach (var item in console.ConsoleItems.Skip(before).Take(12))
		{
			Terraria.Main.NewText(IsaClient.Languages.L10n.T(item.Text), item.TextColor.R, item.TextColor.G, item.TextColor.B);
		}
	}

	#endregion
}
