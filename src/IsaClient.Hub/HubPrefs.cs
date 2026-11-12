using System;
using System.Collections.Generic;
using System.IO;
using IsaClient.Core;
using Terraria;
using Terraria.IO;

namespace IsaClient.Hub;

/// <summary>A button the user made: runs client commands and/or chat lines (server commands).</summary>
public sealed class CustomAction
{
	public string Name = "";

	/// <summary>One or more lines separated by ';' or new lines.</summary>
	public string Commands = "";

	public int Icon = Terraria.ID.ItemID.FallenStar;
}

/// <summary>A saved set of features that are on.</summary>
public sealed class FeatureProfile
{
	public string Name = "";

	public List<string> Features = new List<string>();
}

/// <summary>
/// Settings of the new panel, in Terraria/IsaClient/IsaHub.json (separate from IsaConfig.json, which is rewritten from
/// [Setting] fields on every save).
/// </summary>
internal static class HubPrefs
{
	/// <summary>Use the classic panel instead of the new one.</summary>
	public static bool UseClassic;

	public static string Theme = "Ocean";

	/// <summary>0.5-1: how opaque the window is.</summary>
	public static float Opacity = 0.96f;

	/// <summary>0.8-1.3: size of the whole window.</summary>
	public static float Scale = 1f;

	public static bool Compact;

	public static bool ShowDescriptions = true;

	public static bool CollapsedSidebar;

	public static bool Animations = true;

	public static bool Sounds = true;

	/// <summary>Open on Home (true) or on the last section (false).</summary>
	public static bool OpenOnHome;

	public static int LastPage;

	public static string LastSection = "home";

	public static float WindowX = float.NaN;

	public static float WindowY = float.NaN;

	public static float WindowW = float.NaN;

	public static float WindowH = float.NaN;

	public static List<string> Favorites = new List<string>();

	public static List<string> Recent = new List<string>();

	public static List<string> HiddenSections = new List<string>();

	public static List<CustomAction> Actions = new List<CustomAction>();

	public static List<FeatureProfile> Profiles = new List<FeatureProfile>();

	private static Preferences _store;

	private static bool _loaded;

	private static bool _dirty;

	private static string FilePath => Path.Combine(ClientSettings.IsaClientFolder, "IsaHub.json");

	public static void EnsureLoaded()
	{
		if (_loaded)
		{
			return;
		}
		_loaded = true;
		try
		{
			Directory.CreateDirectory(ClientSettings.IsaClientFolder);
			_store = new Preferences(FilePath);
			if (!_store.Load())
			{
				return;
			}
			UseClassic = _store.Get("UseClassic", UseClassic);
			Theme = _store.Get("Theme", Theme);
			Opacity = Utils.Clamp(_store.Get("Opacity", Opacity), 0.5f, 1f);
			Scale = Utils.Clamp(_store.Get("Scale", Scale), 0.8f, 1.3f);
			Compact = _store.Get("Compact", Compact);
			ShowDescriptions = _store.Get("ShowDescriptions", ShowDescriptions);
			CollapsedSidebar = _store.Get("CollapsedSidebar", CollapsedSidebar);
			Animations = _store.Get("Animations", Animations);
			Sounds = _store.Get("Sounds", Sounds);
			// Saved under a new name: the panel now reopens where you left it unless you choose Home.
			OpenOnHome = _store.Get("AlwaysOpenOnHome", OpenOnHome);
			LastSection = _store.Get("LastSection", LastSection);
			LastPage = _store.Get("LastPage", LastPage);
			WindowX = _store.Get("WindowX", -1f);
			WindowY = _store.Get("WindowY", -1f);
			WindowW = _store.Get("WindowW", -1f);
			WindowH = _store.Get("WindowH", -1f);
			if (WindowX < 0f || WindowY < 0f || WindowW <= 0f || WindowH <= 0f)
			{
				WindowX = WindowY = WindowW = WindowH = float.NaN;
			}
			Favorites = _store.Get("Favorites", Favorites) ?? new List<string>();
			Recent = _store.Get("Recent", Recent) ?? new List<string>();
			HiddenSections = _store.Get("HiddenSections", HiddenSections) ?? new List<string>();
			Actions = _store.Get("CustomActions", Actions) ?? new List<CustomAction>();
			Profiles = _store.Get("Profiles", Profiles) ?? new List<FeatureProfile>();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	/// <summary>Marks the settings as changed; they are written by <see cref="SaveIfChanged"/>.</summary>
	public static void Changed()
	{
		_dirty = true;
	}

	public static void SaveIfChanged()
	{
		if (_dirty)
		{
			Save();
		}
	}

	public static void Save()
	{
		_dirty = false;
		try
		{
			EnsureLoaded();
			if (_store == null)
			{
				_store = new Preferences(FilePath);
			}
			_store.Clear();
			_store.Put("UseClassic", UseClassic);
			_store.Put("Theme", Theme);
			_store.Put("Opacity", Opacity);
			_store.Put("Scale", Scale);
			_store.Put("Compact", Compact);
			_store.Put("ShowDescriptions", ShowDescriptions);
			_store.Put("CollapsedSidebar", CollapsedSidebar);
			_store.Put("Animations", Animations);
			_store.Put("Sounds", Sounds);
			_store.Put("AlwaysOpenOnHome", OpenOnHome);
			_store.Put("LastSection", LastSection);
			_store.Put("LastPage", LastPage);
			_store.Put("WindowX", float.IsNaN(WindowX) ? -1f : WindowX);
			_store.Put("WindowY", float.IsNaN(WindowY) ? -1f : WindowY);
			_store.Put("WindowW", float.IsNaN(WindowW) ? -1f : WindowW);
			_store.Put("WindowH", float.IsNaN(WindowH) ? -1f : WindowH);
			_store.Put("Favorites", Favorites);
			_store.Put("Recent", Recent);
			_store.Put("HiddenSections", HiddenSections);
			_store.Put("CustomActions", Actions);
			_store.Put("Profiles", Profiles);
			_store.Save();
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
	}

	public static bool IsFavorite(HubFeature feature)
	{
		return Favorites.Contains(feature.Id);
	}

	public static void ToggleFavorite(HubFeature feature)
	{
		if (!Favorites.Remove(feature.Id))
		{
			Favorites.Add(feature.Id);
		}
		Changed();
	}

	/// <summary>Remembers the feature as recently used (newest first, 10 kept).</summary>
	public static void Used(HubFeature feature)
	{
		Recent.Remove(feature.Id);
		Recent.Insert(0, feature.Id);
		if (Recent.Count > 10)
		{
			Recent.RemoveRange(10, Recent.Count - 10);
		}
		Changed();
	}
}
