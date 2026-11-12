"""Phase 4: the row lists built inside CheatPanel.AssembleBindPanels become each category's Entries."""
import os
import re

MENU = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src", "IsaClient.Menu")


def load(name):
    return open(os.path.join(MENU, name), encoding="utf-8-sig").read().replace("\r\n", "\n")


def save(name, text):
    open(os.path.join(MENU, name), "wb").write(b"\xef\xbb\xbf" + text.replace("\n", "\r\n").encode("utf-8"))


panel = load("CheatPanel.cs")
assemble = re.search(r"\tprivate void AssembleBindPanels\(\)\n\t\{\n(.*?)\n\t\}\n", panel, re.S)
body = assemble.group(1)


def strings(text):
    return re.findall(r'"((?:[^"\\]|\\.)*)"', text)


def local_list(name):
    m = re.search(r"List<string> " + name + r" = new List<string>\s*\{(.*?)\};", body, re.S)
    return strings(m.group(1))


render_extra = load("RenderMenu.cs")
render_base = strings(re.search(r"ExtraRenderCheats = new List<string>\s*\{(.*?)\};", render_extra, re.S).group(1))
render_more = strings(re.search(r"renderCheats\.AddRange\(new string\[\] \{(.*?)\}\);", body, re.S).group(1))
music = strings(re.search(r"CreateCheatGroup\(5, new List<string>\s*\{(.*?)\}\)\);", body, re.S).group(1))

entries = {
    "PlayerMenu.cs": local_list("cheatsListPlayer"),
    "AiMenu.cs": local_list("cheatsListAI"),
    "InfoMenu.cs": local_list("cheatsList3"),
    "MiscMenu.cs": local_list("cheatsList6"),
    "WorldEditMenu.cs": local_list("cheatsList7"),
    "MusicMenu.cs": music,
    "RenderMenu.cs": render_base + render_more,
}


def literal(items):
    rows = []
    line = "\t\t"
    for item in items:
        piece = '"' + item + '", '
        if len(line) + len(piece) > 118:
            rows.append(line.rstrip())
            line = "\t\t"
        line += piece
    rows.append(line.rstrip().rstrip(","))
    return "new List<string>\n\t{\n" + "\n".join(rows) + "\n\t}"


for file, items in entries.items():
    text = load(file)
    if file == "RenderMenu.cs":
        text = re.sub(r"(\t/// <summary>[^\n]*\n)?\t(?:private|internal) static readonly List<string> ExtraRenderCheats = new List<string>\s*\{.*?\};\n\n?", "", text, count=1, flags=re.S)
    declaration = "\t/// <summary>The rows of this category, in display order.</summary>\n\tinternal static readonly List<string> Entries = " + literal(items) + ";\n\n"
    brace = text.index("\n{\n", text.index(" class ")) + 3
    text = text[:brace] + declaration + text[brace:]
    if "using System.Collections.Generic;" not in text:
        text = "using System.Collections.Generic;\n" + text
    save(file, text)
    print(f"{file}: {len(items)} rows")
