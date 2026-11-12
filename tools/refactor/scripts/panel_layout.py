"""Phase 4: row width rules move to PanelLayout; the leftovers of the Extras partial and ExtraCheatsMenu go away."""
import os
import re

MENU = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src", "IsaClient.Menu")


def load(name):
    return open(os.path.join(MENU, name), encoding="utf-8-sig").read().replace("\r\n", "\n")


def save(name, text):
    open(os.path.join(MENU, name), "wb").write(b"\xef\xbb\xbf" + text.replace("\n", "\r\n").encode("utf-8"))


panel = load("CheatPanel.cs")
# old rows, to check PanelLayout has exactly the same ones
init = re.search(r"\tpublic override void OnInitialize\(\)\n\t\{\n(.*?)\t\tAddExtraFullLines\(\);\n", panel, re.S)
old_full = set(re.findall(r'"((?:[^"\\]|\\.)*)"', re.search(r"_CheatsFullLine = new List<string>\s*\{(.*?)\};", init.group(1), re.S).group(1)))
for m in re.finditer(r"_CheatsFullLine\.AddRange\(new string\[\] \{(.*?)\}\);", init.group(1), re.S):
    old_full |= set(re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(1)))
extra = load("ExtraCheatsMenu.cs")
old_full |= set(re.findall(r'"((?:[^"\\]|\\.)*)"', re.search(r"ExtraFullLineCheats = new string\[\]\s*\{(.*?)\};", extra, re.S).group(1)))
old_half = set(re.findall(r'"((?:[^"\\]|\\.)*)"', re.search(r"_CheatsHalfSingleLine = new List<string> \{(.*?)\};", init.group(1), re.S).group(1)))
layout = load("PanelLayout.cs")
new_full = set(re.findall(r'"((?:[^"\\]|\\.)*)"', re.search(r"FullWidth = new HashSet<string>\s*\{(.*?)\};", layout, re.S).group(1)))
new_half = set(re.findall(r'"((?:[^"\\]|\\.)*)"', re.search(r"HalfWidthCentered = new HashSet<string> \{(.*?)\};", layout, re.S).group(1)))
if old_full != new_full or old_half != new_half:
    raise SystemExit(f"layout differs: full -{old_full - new_full} +{new_full - old_full}; half -{old_half - new_half} +{new_half - old_half}")

panel = panel[:init.start(1)] + panel[init.end():].replace("", "", 0) if False else panel
panel = panel.replace(init.group(0), "\tpublic override void OnInitialize()\n\t{\n")
panel = re.sub(r"\tprivate static List<string> _Cheats(?:FullLine|ThickFullLine|HalfSingleLine|ThickHalfSingleLine) = new List<string>\(\);\n\n", "", panel)
rows_old = re.search(r"\t\t\tif \(_CheatsThickHalfSingleLine\.Contains\(name\)\)\n.*?\t\t\tif \(_CheatsFullLine\.Contains\(name\)\)\n", panel, re.S)
panel = panel.replace(rows_old.group(0), """			if (PanelLayout.IsHalfWidthCentered(name))
			{
				element.Width.Set(0f, 0.5f);
				element.HAlign = 0.5f;
				element.Height.Set(0f, 1f);
				line.Append(element);
				continue;
			}
			if (PanelLayout.IsFullWidth(name))
""")
panel = re.sub(r"\tprivate static bool HasOwnLine\(string cheat\)\n\t\{\n.*?\n\t\}\n\n", "", panel, flags=re.S)
panel = panel.replace("!HasOwnLine(entries[i + 1])", "!PanelLayout.HasOwnLine(entries[i + 1])")
panel = panel.replace("\tprivate UIElement _outerContainer;", "\tprivate readonly List<UIElement> _worldInfoCategories = new List<UIElement>();\n\n\tprivate UIElement _outerContainer;", 1)
panel = panel.replace("public partial class CheatPanel : UIState", "public class CheatPanel : UIState")
save("CheatPanel.cs", panel)
os.remove(os.path.join(MENU, "CheatPanel.Extras.cs"))
os.remove(os.path.join(MENU, "ExtraCheatsMenu.cs"))
print("layout moved; CheatPanel.Extras.cs and ExtraCheatsMenu.cs removed")
