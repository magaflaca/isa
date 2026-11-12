using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Core;
using IsaClient.Menu;
using Microsoft.Xna.Framework;

namespace IsaClient.Hub;

/// <summary>
/// Every section, page, group and feature of the new panel. The content lives in <see cref="GameplayContent"/>,
/// <see cref="WorldContent"/> and <see cref="ClientContent"/>; this class builds it once and finds features by id.
/// A row of the classic panel that no content places still shows up (section "More"), so nothing is ever lost.
/// </summary>
internal static class HubCatalog
{
	private static List<HubSection> _sections;

	private static readonly Dictionary<string, HubFeature> _byId = new Dictionary<string, HubFeature>();

	private static HubSection _home;

	public static HubSection Home
	{
		get
		{
			if (_sections == null)
			{
				Build();
			}
			return _home;
		}
	}

	/// <summary>What the last build found wrong (shown by .menu check).</summary>
	public static List<string> Problems { get; private set; } = new List<string>();

	public static List<HubSection> Sections
	{
		get
		{
			if (_sections == null)
			{
				Build();
			}
			return _sections;
		}
	}

	public static IEnumerable<HubFeature> AllFeatures => Sections.SelectMany(section => section.Features);

	public static HubFeature Find(string id)
	{
		if (_sections == null)
		{
			Build();
		}
		return (id != null && _byId.TryGetValue(id, out HubFeature feature)) ? feature : null;
	}

	public static HubSection FindSection(string id)
	{
		return Sections.FirstOrDefault(section => section.Id == id);
	}

	/// <summary>Builds everything again (classic rows are created again too).</summary>
	public static void Rebuild()
	{
		_sections = null;
		_byId.Clear();
	}

	private static void Build()
	{
		_byId.Clear();
		CatalogBuilder builder = new CatalogBuilder();
		_home = builder.Section("home", "Home", "Favorites, what is on, and your own buttons", HubIcon.Item(Terraria.ID.ItemID.FallenStar), new Color(255, 231, 69));
		_home.Special = true;
		try
		{
			GameplayContent.Build(builder);
			WorldContent.Build(builder);
			ClientContent.Build(builder);
		}
		catch (Exception ex)
		{
			ClientLog.LogException(ex);
		}
		builder.AddLeftovers();
		AddPageEntries(builder.Sections);
		Problems = builder.Problems;
		_sections = builder.Sections;
		foreach (HubFeature feature in _sections.SelectMany(section => section.Features))
		{
			_byId[feature.Id] = feature;
			if (feature.Companion != null)
			{
				_byId[feature.Companion.Id] = feature.Companion;
			}
		}
	}

	/// <summary>
	/// Pages drawn by their own code (Games, Bots, Proxy...) have no feature cards, so the search never found them: each
	/// gets one card, kept in a group of its page (the page draws itself, so it never shows there), that opens it.
	/// </summary>
	private static void AddPageEntries(List<HubSection> sections)
	{
		foreach (HubSection section in sections)
		{
			for (int i = 0; i < section.Pages.Count; i++)
			{
				HubPage page = section.Pages[i];
				if (page.Custom == null)
				{
					continue;
				}
				string sectionId = section.Id;
				int index = i;
				bool single = section.Pages.Count == 1;
				HubGroup group = new HubGroup(section.Title, section.Icon);
				HubFeature feature = new HubFeature
				{
					Id = "page:" + sectionId + ":" + index,
					Title = single ? section.Title : (section.Title + ": " + page.Title),
					Description = section.Subtitle,
					Icon = page.Icon,
					NativeKind = FeatureKind.Action,
					Cheat = false,
					Verb = "Open",
					Keywords = section.Title + " " + page.Title,
					Section = section,
					Group = group,
					Run = () => ClientHub.OpenSection(sectionId, index)
				};
				group.Features.Add(feature);
				page.Groups.Add(group);
			}
		}
	}
}

/// <summary>Fluent helper the content classes use: Section → Page → Group → features.</summary>
internal sealed class CatalogBuilder
{
	public readonly List<HubSection> Sections = new List<HubSection>();

	/// <summary>Content that names a row that does not exist, and rows no content placed.</summary>
	public readonly List<string> Problems = new List<string>();

	private readonly Dictionary<string, LegacyRow> _rows = new Dictionary<string, LegacyRow>();

	private readonly HashSet<string> _placed = new HashSet<string>();

	private HubSection _section;

	private HubPage _page;

	private HubGroup _group;

	public CatalogBuilder()
	{
		foreach (CheatCategory category in CheatCategories.CheatsTab.Concat(CheatCategories.WorldInfoTab))
		{
			foreach (string name in category.Entries)
			{
				if (name == "New Interface")
				{
					// The classic panel's way back to this one.
					continue;
				}
				// "Disable All" exists in several categories: those are keyed by category.
				string key = IsResetRow(name) ? (name + "@" + category.Title) : name;
				if (!_rows.ContainsKey(key))
				{
					_rows[key] = new LegacyRow(category, name);
				}
			}
		}
	}

	private static bool IsResetRow(string name)
	{
		return name == "Disable All" || name == "Disable & Reset All";
	}

	public HubSection Section(string id, string title, string subtitle, HubIcon icon, Color accent)
	{
		_section = new HubSection(id, title, subtitle, icon, accent);
		Sections.Add(_section);
		_page = null;
		_group = null;
		return _section;
	}

	public HubPage Page(string title, HubIcon icon)
	{
		_page = new HubPage(title, icon);
		_section.Pages.Add(_page);
		_group = null;
		return _page;
	}

	public HubGroup Group(string title, HubIcon icon, string hint = null)
	{
		if (_page == null)
		{
			Page(_section.Title, _section.Icon);
		}
		_group = new HubGroup(title, icon, hint);
		_page.Groups.Add(_group);
		return _group;
	}

	/// <summary>A row of the classic panel, with its new name, line and icon. Null when the row does not exist.</summary>
	public HubFeature Row(string legacy, string title = null, string description = null, int icon = 0, string keywords = null, string command = null, string verb = null, bool wide = false, bool cheat = true)
	{
		HubFeature feature = Make(legacy, title, description, icon, keywords, command, verb, wide, cheat);
		if (feature != null)
		{
			Add(feature);
		}
		return feature;
	}

	/// <summary>Like <see cref="Row"/> with a texture icon.</summary>
	public HubFeature Row(string legacy, string title, string description, HubIcon icon, string keywords = null, string command = null, string verb = null, bool wide = false, bool cheat = true)
	{
		HubFeature feature = Make(legacy, title, description, 0, keywords, command, verb, wide, cheat);
		if (feature != null)
		{
			feature.Icon = icon;
			Add(feature);
		}
		return feature;
	}

	/// <summary>A card for a classic row plus a second button (a boss: defeated switch + Summon).</summary>
	public HubFeature Pair(string legacy, string companion, string title, string description, int icon, string verb, string keywords = null, bool cheat = false)
	{
		HubFeature feature = Row(legacy, title, description, icon, keywords, cheat: cheat);
		HubFeature second = Make(companion, title + ": " + verb, description, icon, keywords, null, verb, false, false);
		if (feature != null && second != null)
		{
			second.Section = feature.Section;
			second.Group = feature.Group;
			feature.Companion = second;
		}
		else if (feature == null && second != null)
		{
			second.Title = title;
			Add(second);
			return second;
		}
		return feature;
	}

	private HubFeature Make(string legacy, string title, string description, int icon, string keywords, string command, string verb, bool wide, bool cheat)
	{
		if (!_rows.TryGetValue(legacy, out LegacyRow row))
		{
			Problems.Add("No classic row \"" + legacy + "\"");
			return null;
		}
		_placed.Add(legacy);
		return new HubFeature
		{
			Id = "row:" + legacy,
			Title = title ?? StripCategory(legacy),
			Description = description ?? FirstLine(MenuControls.TooltipFor(row.Name)),
			Icon = (icon > 0) ? HubIcon.Item(icon) : (_group?.Icon ?? _section.Icon),
			Keywords = keywords ?? "",
			Command = command,
			Verb = verb,
			Wide = wide,
			Cheat = cheat && !(_group?.Preferences ?? false),
			Row = row
		};
	}

	/// <summary>Classic rows a custom page does the job of now: they're not leftovers (no "More" for them).</summary>
	public void Replaced(IEnumerable<string> legacy)
	{
		foreach (string key in legacy)
		{
			_placed.Add(key);
		}
	}

	/// <summary>
	/// Something on the current custom page, for the panel's search: found by its name and words, it opens the page
	/// (custom pages draw themselves, so they have no cards of their own).
	/// </summary>
	public HubFeature Find(string title, string description, string keywords = null, int icon = 0)
	{
		HubSection section = _section;
		HubPage page = _page;
		if (_group == null || !page.Groups.Contains(_group))
		{
			_group = new HubGroup(page.Title, page.Icon);
			page.Groups.Add(_group);
		}
		HubFeature feature = Native("find:" + section.Id + ":" + page.Title + ":" + title, title, description, (icon > 0) ? HubIcon.Item(icon) : page.Icon, FeatureKind.Action);
		feature.Keywords = (keywords ?? "") + " " + page.Title;
		feature.Verb = "Open";
		feature.Run = () => ClientHub.OpenSection(section.Id, Math.Max(0, section.Pages.IndexOf(page)));
		return feature;
	}

	/// <summary>A feature of the new panel itself (settings, shortcuts), not a classic row.</summary>
	public HubFeature Native(string id, string title, string description, HubIcon icon, FeatureKind kind)
	{
		HubFeature feature = new HubFeature
		{
			Id = "hub:" + id,
			Title = title,
			Description = description,
			Icon = icon.IsEmpty ? (_group?.Icon ?? _section.Icon) : icon,
			NativeKind = kind,
			Cheat = false
		};
		Add(feature);
		return feature;
	}

	private void Add(HubFeature feature)
	{
		if (_group == null)
		{
			Group(_section.Title, _section.Icon);
		}
		feature.Section = _section;
		feature.Group = _group;
		_group.Features.Add(feature);
	}

	/// <summary>Classic rows nothing placed: section "More", so every feature stays reachable.</summary>
	public void AddLeftovers()
	{
		List<string> left = _rows.Keys.Where(key => !_placed.Contains(key)).ToList();
		if (left.Count == 0)
		{
			return;
		}
		Section("more", "More", "Everything else", HubIcon.Item(Terraria.ID.ItemID.Toolbox), new Color(180, 180, 200));
		Group("Other features", _section.Icon);
		foreach (string key in left)
		{
			Problems.Add("Classic row \"" + key + "\" is only in More");
			Row(key);
		}
	}

	private static string StripCategory(string key)
	{
		int at = key.IndexOf('@');
		return (at > 0) ? key.Substring(0, at) : key;
	}

	private static string FirstLine(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		int newLine = text.IndexOf('\n');
		return (newLine >= 0) ? text.Substring(0, newLine) : text;
	}
}
