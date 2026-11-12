using System;
using System.Collections.Generic;
using ImGuiNET;

namespace TerraAngel.Tools;

/// <summary>Tab of the overlay's Tools page a tool is listed in (TerraAngel's tabs).</summary>
public enum ToolTabs
{
	None,
	NewTab,
	MainTools,
	LightingTools,
	VisualTools,
	ESPTools,
	AutomationTools,
	Inspector,
	MiscTools,
	BrokenTools
}

/// <summary>A tool (TerraAngel's API): drawn in its tab of the overlay's Tools page, updated every frame.</summary>
public abstract class Tool
{
	/// <summary>Name used for sorting (and the tab title of a NewTab tool).</summary>
	public virtual string Name => GetType().Name;

	public virtual ToolTabs Tab => ToolTabs.None;

	public virtual void DrawUI(ImGuiIOPtr io)
	{
	}

	public virtual void Update()
	{
	}
}

/// <summary>The tools plugins added (TerraAngel's ToolManager): <c>ToolManager.AddTool&lt;MyTool&gt;()</c> in Load.</summary>
public static class ToolManager
{
	private static readonly Dictionary<Type, Tool> _tools = new Dictionary<Type, Tool>();

	private static readonly List<Tool> _all = new List<Tool>();

	public static T GetTool<T>() where T : Tool => (T)GetTool(typeof(T));

	public static void AddTool<T>() where T : Tool => AddTool(typeof(T));

	public static void RemoveTool<T>() where T : Tool => RemoveTool(typeof(T));

	public static Tool GetTool(Type type)
	{
		if (_tools.TryGetValue(type, out Tool tool))
		{
			return tool;
		}
		throw new KeyNotFoundException("Tool of type " + type.FullName + " is not registered.");
	}

	public static bool TryGetTool<T>(out T tool) where T : Tool
	{
		bool found = _tools.TryGetValue(typeof(T), out Tool value);
		tool = value as T;
		return found;
	}

	public static void AddTool(Type type)
	{
		if (_tools.ContainsKey(type))
		{
			return;
		}
		Tool tool = (Tool)Activator.CreateInstance(type);
		_tools.Add(type, tool);
		_all.Add(tool);
	}

	/// <summary>Adds a tool that was already created (with arguments of its own).</summary>
	public static void AddTool(Tool tool)
	{
		if (tool == null || _tools.ContainsKey(tool.GetType()))
		{
			return;
		}
		_tools.Add(tool.GetType(), tool);
		_all.Add(tool);
	}

	public static void RemoveTool(Type type)
	{
		if (_tools.TryGetValue(type, out Tool tool))
		{
			_tools.Remove(type);
			_all.Remove(tool);
		}
	}

	public static List<Tool> GetToolsOfTab(ToolTabs tab)
	{
		List<Tool> list = new List<Tool>();
		foreach (Tool tool in _all)
		{
			if (tool.Tab == tab)
			{
				list.Add(tool);
			}
		}
		list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
		return list;
	}

	public static IReadOnlyList<Tool> AllTools => _all;

	public static void SortTabs()
	{
	}

	/// <summary>Every tool's Update; a tool that throws is reported once per message and keeps running.</summary>
	public static void Update() => Update(null);

	/// <summary>The Update of the tools <paramref name="which"/> accepts (all when null).</summary>
	public static void Update(Func<Tool, bool> which)
	{
		for (int i = 0; i < _all.Count; i++)
		{
			Tool tool = _all[i];
			if (which != null && !which(tool))
			{
				continue;
			}
			try
			{
				tool.Update();
			}
			catch (Exception ex)
			{
				IsaClient.Plugins.PluginLog.ErrorOnce(tool.Name + ": " + ex.Message, ex);
			}
		}
	}

	/// <summary>Removes every tool of <paramref name="assembly"/> (its plugin was turned off).</summary>
	internal static void RemoveToolsOf(System.Reflection.Assembly assembly)
	{
		for (int i = _all.Count - 1; i >= 0; i--)
		{
			if (_all[i].GetType().Assembly == assembly)
			{
				_tools.Remove(_all[i].GetType());
				_all.RemoveAt(i);
			}
		}
	}
}
