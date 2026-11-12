using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria.GameContent.UI.Elements;
using Terraria.Localization;
using Terraria.UI;

namespace IsaClient.Menu;

/// <summary>
/// One category box of the panel: its title, tint, rows, and how each row is built. The rows and the builder live in
/// the category's own class (TravelMenu, PlayerMenu, ...); <see cref="CheatCategories"/> lists them all.
/// </summary>
internal sealed class CheatCategory
{
	public CheatCategory(string title, Color tint, IReadOnlyList<string> entries, Func<string, Color, UIElement> create, Action<UITextPanel<LocalizedText>> decorateHeader = null)
	{
		Title = title;
		Tint = tint;
		Entries = entries;
		Create = create;
		DecorateHeader = decorateHeader;
	}

	public string Title { get; }

	/// <summary>Mixed into the panel background (18%).</summary>
	public Color Tint { get; }

	/// <summary>Row names, in display order.</summary>
	public IReadOnlyList<string> Entries { get; }

	/// <summary>Builds the control of one row (row name, category color).</summary>
	public Func<string, Color, UIElement> Create { get; }

	/// <summary>Optional: extra behavior for the category's title (hover text, click).</summary>
	public Action<UITextPanel<LocalizedText>> DecorateHeader { get; }
}
