using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IsaClient.Core;
using Newtonsoft.Json;

namespace IsaClient.Sharing;

/// <summary>Your kept character styles (IsaClient/Styles.json): name and look, to try on or link in chat.</summary>
public static class StyleLibrary
{
	private sealed class Saved
	{
		public string Name = "";

		public string Look = "";
	}

	private static List<StyleLook> _styles;

	public static int Version;

	private static string FilePath => Path.Combine(ClientSettings.IsaClientFolder, "Styles.json");

	public static List<StyleLook> Styles
	{
		get
		{
			Load();
			return _styles.ToList();
		}
	}

	private static void Load()
	{
		if (_styles != null)
		{
			return;
		}
		_styles = new List<StyleLook>();
		try
		{
			if (File.Exists(FilePath))
			{
				foreach (Saved saved in JsonConvert.DeserializeObject<List<Saved>>(File.ReadAllText(FilePath)) ?? new List<Saved>())
				{
					StyleLook look = StyleLook.FromHex(saved.Look);
					if (look != null)
					{
						look.Name = saved.Name ?? "";
						_styles.Add(look);
					}
				}
			}
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	private static void Save()
	{
		try
		{
			Directory.CreateDirectory(ClientSettings.IsaClientFolder);
			File.WriteAllText(FilePath, JsonConvert.SerializeObject(_styles.Select(s => new Saved { Name = s.Name, Look = s.Hex() }).ToList(), Formatting.Indented));
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		Version++;
	}

	public static bool Has(StyleLook look)
	{
		Load();
		string hex = look.Hex();
		return _styles.Any(s => s.Hex() == hex);
	}

	public static void Add(StyleLook look)
	{
		Load();
		if (look == null || Has(look))
		{
			return;
		}
		_styles.Insert(0, look);
		Save();
	}

	public static void Remove(StyleLook look)
	{
		Load();
		string hex = look.Hex();
		_styles.RemoveAll(s => s.Hex() == hex && s.Name == look.Name);
		Save();
	}
}
