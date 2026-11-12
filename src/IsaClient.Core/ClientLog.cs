using System;
using System.IO;

namespace IsaClient.Core;

/// <summary>Error log of the client (Terraria/IsaClient/Errors.txt).</summary>
public static class ClientLog
{
	public static string ErrorLogPath = Path.Combine(ClientSettings.IsaClientFolder, "Errors.txt");

	private static readonly System.Collections.Generic.HashSet<string> _logged = new System.Collections.Generic.HashSet<string>();

	/// <summary>Logs an error that may repeat every frame: only the first time each message and place is seen.</summary>
	public static void LogOnce(Exception ex)
	{
		string key = ex.GetType().Name + ex.Message + ex.StackTrace?.Split('\n')[0];
		lock (_logged)
		{
			if (_logged.Count > 200 || !_logged.Add(key))
			{
				return;
			}
		}
		try
		{
			LogException(ex);
		}
		catch
		{
		}
	}

	private static readonly System.Collections.Generic.List<(DateTime Time, string Text)> _recent = new System.Collections.Generic.List<(DateTime, string)>();

	/// <summary>The last errors logged since the game started (bug and crash reports include them).</summary>
	public static System.Collections.Generic.List<(DateTime Time, string Text)> Recent
	{
		get
		{
			lock (_recent)
			{
				return new System.Collections.Generic.List<(DateTime, string)>(_recent);
			}
		}
	}

	public static void LogException(Exception ex)
	{
		string value = ex.ToString();
		lock (_recent)
		{
			_recent.Add((DateTime.Now, value));
			if (_recent.Count > 10)
			{
				_recent.RemoveAt(0);
			}
		}
		using StreamWriter streamWriter = new StreamWriter(ErrorLogPath, append: true);
		streamWriter.WriteLine(DateTime.Now);
		streamWriter.WriteLine(value);
		streamWriter.WriteLine("");
	}
}
