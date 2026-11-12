"""Phase 4: CheatPanel stops being partial: the Player List page joins CheatPanel.cs, unused code goes."""
import os
import re

MENU = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src", "IsaClient.Menu")


def load(name):
    return open(os.path.join(MENU, name), encoding="utf-8-sig").read().replace("\r\n", "\n")


def save(name, text):
    open(os.path.join(MENU, name), "wb").write(b"\xef\xbb\xbf" + text.replace("\n", "\r\n").encode("utf-8"))


pages = load("CheatPanel.Pages.cs")
body = pages[pages.index("\n{\n", pages.index("public partial class CheatPanel")) + 3:pages.rstrip().rfind("\n}")]
body = re.sub(r"\t/// <summary>A framed row \(Terraria settings panel look\).*?\n\t\}\n\n", "", body, count=1, flags=re.S)
body = re.sub(r"\tprivate void AddRow\(UIElement row\)\n\t\{\n.*?\n\t\}\n\n", "", body, count=1, flags=re.S)
if "IsaRow" in body or "AddRow" in body:
    raise SystemExit("unused members still there")
panel = load("CheatPanel.cs")
index = panel.rstrip().rfind("\n}")
panel = panel[:index] + "\n\n" + body.strip("\n") + panel[index:]
panel = panel.replace("public partial class CheatPanel : UIState", "public class CheatPanel : UIState")
pages_usings = [l for l in pages.split("\n") if l.startswith("using ")]
panel_usings = set(l for l in panel.split("\n") if l.startswith("using "))
missing = [u for u in pages_usings if u not in panel_usings]
if missing:
    first = panel.index("using ")
    panel = panel[:first] + "\n".join(missing) + "\n" + panel[first:]
save("CheatPanel.cs", panel)
os.remove(os.path.join(MENU, "CheatPanel.Pages.cs"))
os.remove(os.path.join(MENU, "CheatPanel.ItemMods.cs"))
print("CheatPanel is one file now")
