using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ImGuiNET;
using IsaClient.Core;
using TerraAngel;
using TerraAngel.Tools;
using TerraAngel.UI;
using NVector2 = System.Numerics.Vector2;

namespace IsaClient.Plugins;

/// <summary>
/// The overlay's main window: the plugins in the folder (turn on/off, reload, errors), the tools plugins added (in
/// TerraAngel's tabs), each IsaClient plugin's own tab, and a menu to show the plugins' windows.
/// </summary>
internal static class PluginOverlay
{
	private static readonly (ToolTabs Tab, string Title)[] _toolTabs =
	{
		(ToolTabs.MainTools, "Main"),
		(ToolTabs.VisualTools, "Visuals"),
		(ToolTabs.ESPTools, "ESP"),
		(ToolTabs.LightingTools, "Lighting"),
		(ToolTabs.AutomationTools, "Automation"),
		(ToolTabs.Inspector, "Inspector"),
		(ToolTabs.MiscTools, "Misc"),
		(ToolTabs.BrokenTools, "Broken")
	};

	private static readonly Microsoft.Xna.Framework.Color Muted = new Microsoft.Xna.Framework.Color(154, 167, 190);

	private static readonly Microsoft.Xna.Framework.Color Good = new Microsoft.Xna.Framework.Color(110, 220, 120);

	private static readonly Microsoft.Xna.Framework.Color Bad = new Microsoft.Xna.Framework.Color(240, 110, 110);

	private static readonly Microsoft.Xna.Framework.Color Busy = new Microsoft.Xna.Framework.Color(255, 210, 90);

	public static void Draw(ImGuiIOPtr io)
	{
		NVector2 size = new NVector2(Math.Min(620f, io.DisplaySize.X - 40f), Math.Min(520f, io.DisplaySize.Y - 80f));
		ImGui.SetNextWindowPos(new NVector2(30f, io.DisplaySize.Y - size.Y - 40f), ImGuiCond.FirstUseEver);
		ImGui.SetNextWindowSize(size, ImGuiCond.FirstUseEver);
		bool open = true;
		if (!ImGui.Begin(ClientInfo.Name + " Plugins###IsaPlugins", ref open, ImGuiWindowFlags.MenuBar))
		{
			ImGui.End();
			PluginHost.OverlayVisible = open;
			return;
		}
		PluginHost.OverlayVisible = open;
		DrawMenu();
		if (ImGui.BeginTabBar("##PluginTabs"))
		{
			if (ImGui.BeginTabItem("Plugins"))
			{
				DrawPlugins();
				ImGui.EndTabItem();
			}
			DrawToolTabs(io);
			foreach (ClientPlugin plugin in PluginLoader.LoadedClientPlugins().ToList())
			{
				if (!HasTab(plugin))
				{
					continue;
				}
				if (ImGui.BeginTabItem(plugin.Name + "###plugin:" + plugin.GetType().FullName))
				{
					try
					{
						plugin.DrawTab(io);
					}
					catch (Exception ex)
					{
						ImGuiExt.TextColored("Error: " + ex.Message, Bad);
						PluginLog.ErrorOnce(plugin.Name + " (tab): " + ex.Message, ex);
					}
					ImGui.EndTabItem();
				}
			}
			ImGui.EndTabBar();
		}
		ImGui.End();
	}

	private static void DrawMenu()
	{
		if (!ImGui.BeginMenuBar())
		{
			return;
		}
		if (ImGui.BeginMenu("Windows"))
		{
			List<ClientWindow> windows = ClientLoader.MainRenderer.ClientWindows.Where(w => w.IsToggleable).ToList();
			if (windows.Count == 0)
			{
				ImGui.TextDisabled("No windows");
			}
			foreach (ClientWindow window in windows)
			{
				if (ImGui.MenuItem(window.Title, null, window.IsEnabled))
				{
					PluginHost.SetWindowShown(window, !window.IsEnabled);
				}
			}
			ImGui.EndMenu();
		}
		if (ImGui.BeginMenu("Help"))
		{
			if (ImGui.MenuItem("Open the Plugins folder"))
			{
				OpenFolder();
			}
			if (ImGui.MenuItem("Open plugins.log"))
			{
				OpenFile(System.IO.Path.Combine(PluginLoader.PluginsFolder, "plugins.log"));
			}
			ImGui.Separator();
			ImGui.TextDisabled(PluginHost.OverlayKey + " shows and hides this overlay (.plugins too)");
			ImGui.EndMenu();
		}
		ImGui.EndMenuBar();
	}

	private static void DrawPlugins()
	{
		if (ImGui.Button("Open folder"))
		{
			OpenFolder();
		}
		ImGui.SameLine();
		if (ImGui.Button("Rescan"))
		{
			PluginLoader.Scan();
		}
		ImGui.SameLine();
		ImGuiExt.HelpMarker("Put *.IsaPlugin.dll or TerraAngel *.TAPlugin.dll files (also builds for .NET 10, they are converted) in the Plugins folder, or folders of .cs files in Plugins/Sources (they are compiled), press Rescan and turn them on. Plugins are programs that run inside the game: only use ones you trust.");
		ImGui.SameLine();
		if (DevTools.DevToolsInstaller.Installing)
		{
			ImGuiExt.TextColored("Developer tools: downloading...", Busy);
		}
		else if (DevTools.DevToolsInstaller.Installed)
		{
			ImGuiExt.TextColored("Developer tools: installed", Muted);
		}
		else if (ImGui.SmallButton("Install developer tools"))
		{
			DevTools.DevToolsInstaller.Ensure("plugin sources, .NET 10 builds and the C# console", null);
		}
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("The C# compiler and decompiler (~15 MB, downloaded once): Plugins/Sources, TerraAngel .NET 10 builds and the C# console need them.");
		}
		ImGui.Separator();
		IReadOnlyList<PluginEntry> entries = PluginLoader.Entries;
		if (entries.Count == 0)
		{
			ImGui.TextWrapped("No plugins yet.");
			ImGuiExt.TextColored(PluginLoader.PluginsFolder, Muted);
			return;
		}
		foreach (PluginEntry entry in entries.ToList())
		{
			ImGui.PushID(entry.File);
			bool enabled = PluginLoader.IsEnabled(entry);
			if (ImGui.Checkbox("##on", ref enabled))
			{
				PluginLoader.SetEnabled(entry, enabled);
			}
			ImGui.SameLine();
			ImGui.TextUnformatted(entry.Name);
			ImGui.SameLine();
			string kind = entry.Kind switch
			{
				PluginKind.Source => "source",
				PluginKind.Fork => "TerraAngel fork (in place of the TerraAngel pack)",
				PluginKind.Converted => "TerraAngel .NET 10 build, converted",
				_ => entry.IsTerraAngel ? "TerraAngel" : "IsaClient"
			};
			if (entry.Instance is ClientPlugin client)
			{
				kind = "v" + client.Version + (string.IsNullOrEmpty(client.Author) ? "" : (" by " + client.Author)) + (entry.Kind == PluginKind.Source ? ", source" : ((entry.Kind == PluginKind.Fork) ? ", fork" : ""));
			}
			ImGuiExt.TextColored(kind, Muted);
			if (entry.Building)
			{
				ImGui.SameLine();
				ImGuiExt.TextColored(entry.Kind == PluginKind.Converted ? "converting..." : ((entry.Kind == PluginKind.Fork) ? "building the fork..." : "compiling..."), Busy);
			}
			if (entry.Loaded)
			{
				ImGui.SameLine();
				ImGuiExt.TextColored(entry.ChangedOnDisk ? "new build on disk" : "running", entry.ChangedOnDisk ? new Microsoft.Xna.Framework.Color(255, 210, 90) : Good);
				ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - 70f);
				if (ImGui.SmallButton("Reload"))
				{
					PluginLoader.Reload(entry);
				}
			}
			if (entry.Instance is ClientPlugin described && !string.IsNullOrEmpty(described.Description))
			{
				ImGui.Indent(30f);
				ImGui.PushStyleColor(ImGuiCol.Text, Muted.PackedValue);
				ImGui.TextWrapped(described.Description);
				ImGui.PopStyleColor();
				ImGui.Unindent(30f);
			}
			if (entry.Error != null)
			{
				ImGui.Indent(30f);
				ImGui.PushStyleColor(ImGuiCol.Text, Bad.PackedValue);
				ImGui.TextWrapped(entry.Error);
				ImGui.PopStyleColor();
				if (entry.BuildErrors.Count > 1 && ImGui.TreeNode("All errors (" + entry.BuildErrors.Count + ")"))
				{
					foreach (string error in entry.BuildErrors)
					{
						ImGui.TextWrapped(error);
					}
					ImGui.TreePop();
				}
				ImGui.Unindent(30f);
			}
			ImGui.PopID();
		}
	}

	private static void DrawToolTabs(ImGuiIOPtr io)
	{
		foreach ((ToolTabs tab, string title) in _toolTabs)
		{
			List<Tool> tools = ToolManager.GetToolsOfTab(tab).Where(ListedHere).ToList();
			if (tools.Count == 0 || !ImGui.BeginTabItem(title))
			{
				continue;
			}
			foreach (Tool tool in tools)
			{
				DrawTool(tool, io);
			}
			ImGui.EndTabItem();
		}
		foreach (Tool tool in ToolManager.GetToolsOfTab(ToolTabs.NewTab).Where(ListedHere))
		{
			if (ImGui.BeginTabItem(tool.Name + "###tool:" + tool.GetType().FullName))
			{
				DrawTool(tool, io);
				ImGui.EndTabItem();
			}
		}
	}

	/// <summary>Tools of a plugin with its own tool window are shown there, not here.</summary>
	private static bool ListedHere(Tool tool)
	{
		System.Reflection.Assembly assembly = tool.GetType().Assembly;
		return !PluginLoader.LoadedClientPlugins().Any(p => p.HasOwnToolWindow && p.GetType().Assembly == assembly);
	}

	private static void DrawTool(Tool tool, ImGuiIOPtr io)
	{
		ImGui.PushID(tool.GetType().FullName);
		try
		{
			tool.DrawUI(io);
		}
		catch (Exception ex)
		{
			ImGuiExt.TextColored(tool.Name + ": " + ex.Message, Bad);
			PluginLog.ErrorOnce(tool.Name + " (draw): " + ex.Message, ex);
		}
		ImGui.PopID();
	}

	/// <summary>A ClientPlugin gets a tab only when it overrides DrawTab.</summary>
	private static bool HasTab(ClientPlugin plugin)
	{
		return plugin.GetType().GetMethod(nameof(ClientPlugin.DrawTab)).DeclaringType != typeof(ClientPlugin);
	}

	public static void OpenFolder()
	{
		System.IO.Directory.CreateDirectory(PluginLoader.PluginsFolder);
		OpenFile(PluginLoader.PluginsFolder);
	}

	/// <summary>Plugins/Forks (TerraAngel forks as people edit them, see ForkBuilder).</summary>
	public static void OpenForksFolder()
	{
		System.IO.Directory.CreateDirectory(PluginLoader.ForksFolder);
		DevTools.ForkBuilder.WriteReadme(PluginLoader.ForksFolder);
		OpenFile(PluginLoader.ForksFolder);
	}

	private static void OpenFile(string path)
	{
		try
		{
			Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
		}
		catch (Exception ex)
		{
			PluginLog.Error("Could not open " + path + ": " + ex.Message);
		}
	}
}
