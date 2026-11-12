using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace IsaClient.Networking;

/// <summary>Country codes and English names, in the order of the flag atlas (IsaClient.Assets.Flags.txt).</summary>
public static class CountryNames
{
	private static List<(string Code, string Name)> _list;

	private static Dictionary<string, int> _index;

	private static void EnsureLoaded()
	{
		if (_list != null)
		{
			return;
		}
		List<(string, string)> list = new List<(string, string)>();
		try
		{
			using Stream stream = typeof(CountryNames).Assembly.GetManifestResourceStream("IsaClient.Assets.Flags.txt");
			using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
			string line;
			while ((line = reader.ReadLine()) != null)
			{
				int bar = line.IndexOf('|');
				if (bar == 2)
				{
					list.Add((line.Substring(0, 2), line.Substring(3)));
				}
			}
		}
		catch
		{
		}
		_index = new Dictionary<string, int>();
		for (int i = 0; i < list.Count; i++)
		{
			_index[list[i].Item1] = i;
		}
		_list = list;
	}

	/// <summary>Cell of the flag atlas, or -1.</summary>
	public static int Index(string code)
	{
		EnsureLoaded();
		return (code != null && _index.TryGetValue(code, out int i)) ? i : -1;
	}

	public static string Name(string code)
	{
		EnsureLoaded();
		if (string.IsNullOrEmpty(code) || code == "xx")
		{
			return "Unknown";
		}
		int i = Index(code);
		return (i >= 0) ? _list[i].Name : code.ToUpperInvariant();
	}

	/// <summary>"de", "DE", "germany", "Germ"... the code, or null.</summary>
	public static string Find(string text)
	{
		EnsureLoaded();
		text = (text ?? "").Trim().ToLowerInvariant();
		if (text.Length == 0)
		{
			return null;
		}
		if (text.Length == 2 && _index.ContainsKey(text))
		{
			return text;
		}
		var match = _list.FirstOrDefault(c => c.Name.Equals(text, StringComparison.OrdinalIgnoreCase));
		if (match.Code == null)
		{
			match = _list.FirstOrDefault(c => c.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase));
		}
		return match.Code;
	}
}
