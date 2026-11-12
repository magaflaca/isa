using System;
using System.Collections.Generic;

namespace IsaClient.Plugins;

/// <summary>
/// Entries plugins add to the title screen's menu, above Settings (TerraAngel's "TerraAngel Settings" and
/// "TerraAngel Plugins"). A plugin adds them on load and removes them on unload.
/// </summary>
public static class MainMenuEntries
{
	public sealed class Entry
	{
		public Func<string> Label;

		public Action Open;

		/// <summary>Null: always shown.</summary>
		public Func<bool> Visible;
	}

	private static readonly List<Entry> _entries = new List<Entry>();

	public static Entry Add(Func<string> label, Action open, Func<bool> visible = null)
	{
		Entry entry = new Entry { Label = label, Open = open, Visible = visible };
		_entries.Add(entry);
		return entry;
	}

	public static void Remove(Entry entry)
	{
		_entries.Remove(entry);
	}

	/// <summary>The entries to show now (plugins off: none).</summary>
	public static List<Entry> Shown()
	{
		List<Entry> shown = new List<Entry>();
		if (PluginHost.SystemOff)
		{
			return shown;
		}
		foreach (Entry entry in _entries)
		{
			try
			{
				if (entry.Visible == null || entry.Visible())
				{
					shown.Add(entry);
				}
			}
			catch
			{
			}
		}
		return shown;
	}
}
