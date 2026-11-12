using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace IsaClient.Hub;

/// <summary>A section of the new panel (Player, Movement, Building...): an icon in the sidebar and one or more pages.</summary>
internal sealed class HubSection
{
	public HubSection(string id, string title, string subtitle, HubIcon icon, Color accent)
	{
		Id = id;
		Title = title;
		Subtitle = subtitle;
		Icon = icon;
		Accent = accent;
	}

	public string Id { get; }

	public string Title { get; }

	public string Subtitle { get; }

	public HubIcon Icon { get; }

	/// <summary>The section's own color (sidebar marker, group titles).</summary>
	public Color Accent { get; }

	public List<HubPage> Pages { get; } = new List<HubPage>();

	/// <summary>Drawn by the window itself (Home, search...), not from pages.</summary>
	public bool Special;

	public IEnumerable<HubFeature> Features => Pages.SelectMany(page => page.Groups).SelectMany(group => group.Features);

	public int ActiveCount => Features.Count(feature => feature.Cheat && feature.Kind == FeatureKind.Toggle && feature.IsOn);
}

/// <summary>A tab inside a section: groups of features, or a page that draws something else (player list, bots).</summary>
internal sealed class HubPage
{
	public HubPage(string title, HubIcon icon)
	{
		Title = title;
		Icon = icon;
	}

	public string Title { get; }

	public HubIcon Icon { get; }

	public List<HubGroup> Groups { get; } = new List<HubGroup>();

	/// <summary>Set for pages that host a classic UI element instead of feature cards.</summary>
	public HubEmbed Embed;

	/// <summary>Set for pages the window draws with its own code.</summary>
	public HubPageDrawer Custom;

	/// <summary>Drawn with its own code under the page's feature cards (more of the same page).</summary>
	public HubPageDrawer Below;
}

/// <summary>Draws a custom page at (x, y) with the given width and returns the height it used.</summary>
internal delegate int HubPageDrawer(Microsoft.Xna.Framework.Graphics.SpriteBatch sb, int x, int y, int width);

/// <summary>A titled block of feature cards.</summary>
internal sealed class HubGroup
{
	public HubGroup(string title, HubIcon icon, string hint = null)
	{
		Title = title;
		Icon = icon;
		Hint = hint;
	}

	public string Title { get; }

	public HubIcon Icon { get; }

	/// <summary>A muted line under the title.</summary>
	public string Hint { get; }

	public List<HubFeature> Features { get; } = new List<HubFeature>();

	/// <summary>The features here are preferences, not cheats: they don't count as "on" and "Turn all off" leaves them.</summary>
	public bool Preferences;

	/// <summary>Extra buttons on the group's title line.</summary>
	public List<HubFeature> HeaderActions { get; } = new List<HubFeature>();
}
