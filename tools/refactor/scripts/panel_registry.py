"""Phase 4: CheatPanel builds its category boxes from CheatCategories instead of parallel switches."""
import os
import re

MENU = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src", "IsaClient.Menu")


def load(name):
    return open(os.path.join(MENU, name), encoding="utf-8-sig").read().replace("\r\n", "\n")


def save(name, text):
    open(os.path.join(MENU, name), "wb").write(b"\xef\xbb\xbf" + text.replace("\n", "\r\n").encode("utf-8"))


panel = load("CheatPanel.cs")
start = panel.index("\t// Category order of the Cheats tab:")
end = panel.index("\tpublic override void OnActivate()")
new_block = '''	/// <summary>Builds one box per category of both tabs (see <see cref="CheatCategories"/>).</summary>
	private void AssembleCategories()
	{
		for (int i = 0; i < CheatCategories.CheatsTab.Length; i++)
		{
			_cheatCategories.Add(CreateCategoryBox(CheatCategories.CheatsTab[i], i));
		}
		for (int i = 0; i < CheatCategories.WorldInfoTab.Length; i++)
		{
			_worldInfoCategories.Add(CreateCategoryBox(CheatCategories.WorldInfoTab[i], i));
		}
	}

	private UISortableElement CreateCategoryBox(CheatCategory category, int order)
	{
		UISortableElement box = new UISortableElement(order);
		box.HAlign = 0.5f;
		box.Width.Set(0f, 1f);
		box.Height.Set(2000f, 0f);
		UIPanel panel = new UIPanel();
		panel.Width.Set(0f, 1f);
		panel.Height.Set(-16f, 1f);
		panel.VAlign = 1f;
		panel.BackgroundColor = Color.Lerp(new Color(33, 43, 79) * 0.8f, category.Tint, 0.18f);
		box.Append(panel);
		UIList rows = new UIList();
		rows.OverflowHidden = false;
		rows.Width.Set(0f, 1f);
		rows.Height.Set(-8f, 1f);
		rows.VAlign = 1f;
		rows.ListPadding = 5f;
		panel.Append(rows);
		AddRows(rows, category, panel.BackgroundColor);
		panel.BackgroundColor = panel.BackgroundColor.MultiplyRGBA(new Color(111, 111, 111));
		UITextPanel<LocalizedText> header = new UITextPanel<LocalizedText>(Language.GetText(category.Title), 0.7f)
		{
			VAlign = 0f,
			HAlign = 0.5f
		};
		category.DecorateHeader?.Invoke(header);
		box.Append(header);
		box.Recalculate();
		box.Width.Set(0f, 1f);
		box.Height.Set(rows.GetTotalHeight() + 30f + 16f, 0f);
		return box;
	}

	/// <summary>Rows go two per line unless a row is listed as full-width or centered half-width.</summary>
	private static void AddRows(UIList parent, CheatCategory category, Color color)
	{
		IReadOnlyList<string> entries = category.Entries;
		for (int i = 0; i < entries.Count; i++)
		{
			string name = entries[i];
			UISortableElement line = new UISortableElement(i);
			line.Width.Set(0f, 1f);
			line.Height.Set(30f, 0f);
			line.HAlign = 0.5f;
			parent.Add(line);
			UIElement element = category.Create(name, color);
			if (_CheatsThickHalfSingleLine.Contains(name))
			{
				element.Width.Set(0f, 0.5f);
				element.HAlign = 0.5f;
				element.Height.Set(45f, 0f);
				line.Height.Set(45f, 0f);
				line.Append(element);
				continue;
			}
			if (_CheatsHalfSingleLine.Contains(name))
			{
				element.Width.Set(0f, 0.5f);
				element.HAlign = 0.5f;
				element.Height.Set(0f, 1f);
				line.Append(element);
				continue;
			}
			if (_CheatsThickFullLine.Contains(name))
			{
				element.Width.Set(0f, 1f);
				element.Height.Set(45f, 0f);
				line.Height.Set(45f, 0f);
				line.Append(element);
				continue;
			}
			if (_CheatsFullLine.Contains(name))
			{
				element.Width.Set(0f, 1f);
				element.Height.Set(0f, 1f);
				line.Append(element);
				continue;
			}
			element.Width.Set(-5f, 0.5f);
			element.Height.Set(0f, 1f);
			line.Append(element);
			if (i + 1 < entries.Count && !HasOwnLine(entries[i + 1]))
			{
				i++;
				UIElement second = category.Create(entries[i], color);
				second.Width.Set(-5f, 0.5f);
				second.Height.Set(0f, 1f);
				second.HAlign = 1f;
				line.Append(second);
			}
			else
			{
				// Alone in its row (next one has its own line, or it is the last): use the whole row, no empty half.
				element.Width.Set(0f, 1f);
			}
		}
	}

	private static bool HasOwnLine(string cheat)
	{
		return _CheatsFullLine.Contains(cheat) || _CheatsThickFullLine.Contains(cheat) || _CheatsHalfSingleLine.Contains(cheat) || _CheatsThickHalfSingleLine.Contains(cheat);
	}

'''
panel = panel[:start] + new_block + panel[end:]
panel = panel.replace("\t\tAssembleBindPanels();\n", "\t\tAssembleCategories();\n")
if "using System.Collections.Generic;" not in panel:
    panel = "using System.Collections.Generic;\n" + panel
save("CheatPanel.cs", panel)

# The Extras partial: only the World Information header decoration and the extra full-line rows were left.
extras_part = load("CheatPanel.Extras.cs")
decorate = re.search(r"\tprivate void DecorateCategoryHeader\(UITextPanel<LocalizedText> header, int elementIndex\)\n\t\{\n.*?\n\t\}\n", extras_part, re.S).group(0)
extras_part = re.sub(r"\tprivate void AssembleExtraPanels\(\)\n\t\{\n.*?\n\t\}\n\n?", "", extras_part, flags=re.S)
extras_part = extras_part.replace(decorate, "")
save("CheatPanel.Extras.cs", extras_part)

# World Information: the header decoration belongs to its sections.
world = load("WorldInfoMenu.cs")
method = decorate.replace("private void DecorateCategoryHeader(UITextPanel<LocalizedText> header, int elementIndex)", "internal static void DecorateHeader(UITextPanel<LocalizedText> header, int section)")
method = method.replace("if (elementIndex != WorldInfoMenu.WorldInfoFirstCategory + 1 && elementIndex != WorldInfoMenu.WorldInfoFirstCategory + 2)", "if (section != 1 && section != 2)")
method = method.replace("bool hardmode = elementIndex == WorldInfoMenu.WorldInfoFirstCategory + 2;", "bool hardmode = section == 2;")
method = "\t/// <summary>Boss section titles: Shift+click spawns every boss of the section.</summary>\n" + method
index = world.rstrip().rfind("\n}")
world = world[:index] + "\n\n" + method.rstrip("\n") + world[index:]
save("WorldInfoMenu.cs", world)

# Leftovers in ExtraCheatsMenu: category ids, titles and colors are now in CheatCategories.
extra = load("ExtraCheatsMenu.cs")
for pattern in [r"\tinternal const int (CosmeticCategory|BiomeCategory|AudioCategory) = \d+;\n\n?",
                r"\tinternal static string ExtraCategoryTitle\(int elementIndex\)\n\t\{\n.*?\n\t\}\n\n?",
                r"\tinternal static Color ExtraCategoryColor\(int elementIndex, Color background\)\n\t\{\n.*?\n\t\}\n\n?"]:
    extra = re.sub(pattern, "", extra, flags=re.S)
save("ExtraCheatsMenu.cs", extra)
print("registry wired")
