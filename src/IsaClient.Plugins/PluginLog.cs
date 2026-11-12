using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TerraAngel;

namespace IsaClient.Plugins;

/// <summary>Plugin messages: the plugin console plus Plugins/plugins.log (errors with their stack trace).</summary>
public static class PluginLog
{
	private static readonly HashSet<string> _reported = new HashSet<string>();

	public static void Info(string text)
	{
		ClientLoader.Console.WriteLine(text);
		Write(text);
	}

	public static void Error(string text, Exception ex = null)
	{
		ClientLoader.Console.WriteError(text);
		Write("ERROR " + text + ((ex != null) ? (Environment.NewLine + ex) : ""));
	}

	/// <summary>An error that may repeat every frame: reported the first time only.</summary>
	public static void ErrorOnce(string text, Exception ex = null)
	{
		lock (_reported)
		{
			if (!_reported.Add(text))
			{
				return;
			}
		}
		Error(text, ex);
	}

	private static readonly List<string> _recent = new List<string>();

	/// <summary>How many lines were logged since the game started (to read only the new ones, see <see cref="Since"/>).</summary>
	public static int Count { get; private set; }

	/// <summary>The lines logged after the first <paramref name="count"/> (the last 300 are kept).</summary>
	public static List<string> Since(int count)
	{
		lock (_recent)
		{
			int skip = Math.Max(0, count - (Count - _recent.Count));
			return _recent.Skip(Math.Min(skip, _recent.Count)).ToList();
		}
	}

	private static void Write(string text)
	{
		lock (_recent)
		{
			_recent.Add(DateTime.Now.ToString("HH:mm:ss") + " " + text);
			Count++;
			if (_recent.Count > 300)
			{
				_recent.RemoveAt(0);
			}
		}
		try
		{
			Directory.CreateDirectory(PluginLoader.PluginsFolder);
			File.AppendAllText(Path.Combine(PluginLoader.PluginsFolder, "plugins.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine);
		}
		catch
		{
		}
	}
}
