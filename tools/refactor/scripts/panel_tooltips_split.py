"""Phase 4: tooltips go to the category that owns the row.

Sources today (first non-null wins): HeldItemMenu.HeldItemTooltip, ExtraCheatsMenu.ExtraTooltips (+ server-only rule),
the switch in MenuControls.ShowTooltip. Each category gets `internal static string Tooltip(string name)`.

    python scripts/panel_tooltips_split.py           (report)
    python scripts/panel_tooltips_split.py --write
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
MENU = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src", "IsaClient.Menu")
WRITE = "--write" in sys.argv
STRING = r'"(?:[^"\\]|\\.)*"'


def load(name):
    return open(os.path.join(MENU, name), encoding="utf-8-sig").read().replace("\r\n", "\n")


def save(name, text):
    open(os.path.join(MENU, name), "wb").write(b"\xef\xbb\xbf" + text.replace("\n", "\r\n").encode("utf-8"))


# --- base switch (MenuControls.ShowTooltip) ---
controls = load("MenuControls.cs")
show_start = controls.index("\tinternal static void ShowTooltip(UIElement element, string buttonName)")
switch_start = controls.index("\t\tswitch (buttonName)\n\t\t{\n", show_start) + len("\t\tswitch (buttonName)\n\t\t{\n")
switch_end = controls.index("\n\t\t}\n\t}\n", switch_start)
base = {}
pending = []
for line in controls[switch_start:switch_end].split("\n"):
    label = re.fullmatch(r"\t\tcase (" + STRING + "):", line)
    if label:
        pending.append(label.group(1))
        continue
    text = re.fullmatch(r"\t\t\tMain\.instance\.MouseText\((.*), 0, 0\);", line)
    if text:
        for name in pending:
            base[name] = text.group(1)
        continue
    if line.strip() == "break;":
        pending = []
        continue
    if line.strip():
        raise SystemExit("unexpected tooltip line: " + line)

# --- extras dictionary ---
extras_file = load("ExtraCheatsMenu.cs")
dict_body = re.search(r"ExtraTooltips = new Dictionary<string, string>\n\t\{\n(.*?)\n\t\};", extras_file, re.S).group(1)
extras = {m.group(1): m.group(2) for m in re.finditer(r"\{ (" + STRING + r"), (" + STRING + r"(?:\s*\+\s*" + STRING + r")*) \}", dict_body)}
declared = len(re.findall(r"^\t\t\{ ", dict_body, re.M))
if declared != len(extras):
    raise SystemExit(f"extras: parsed {len(extras)} of {declared}")

# --- held item ---
held = load("HeldItemMenu.cs")
held_names = re.findall(r"\t\tcase (" + STRING + "):", re.search(r"static string HeldItemTooltip\(string name\)\n\t\{\n(.*?)\n\t\}\n", held, re.S).group(1))

# --- which category owns each row ---
categories = {
    "PlayerMenu": "PlayerMenu.cs", "TravelMenu": "TravelMenu.cs", "InfoMenu": "InfoMenu.cs", "RenderMenu": "RenderMenu.cs",
    "CosmeticMenu": "CosmeticMenu.cs", "MiscMenu": "MiscMenu.cs", "WorldEditMenu": "WorldEditMenu.cs",
    "BiomeSpooferMenu": "BiomeSpooferMenu.cs", "AudioMenu": "AudioMenu.cs", "MusicMenu": "MusicMenu.cs", "AiMenu": "AiMenu.cs",
    "WorldInfoMenu": "WorldInfoMenu.cs", "HeldItemMenu": "HeldItemMenu.cs",
}
owner = {}
for cls, file in categories.items():
    text = load(file)
    for m in re.finditer(r"(?:Entries|SectionEntries|PresetEntries)\b[^=]*= new List<string>\[?\d*\]?\s*\{(.*?)\n\t\};", text, re.S):
        for name in re.findall(STRING, m.group(1)):
            owner.setdefault(name, []).append(cls)

common = {"\"Disable All\"", "\"Disable & Reset All\""}
overlap = (set(base) & set(extras)) | (set(base) & set(held_names)) | (set(extras) & set(held_names))
print(f"base {len(base)}, extras {len(extras)}, held {len(held_names)}; overlap: {sorted(overlap)}")
assignment = {}
unowned = []
for source, table in (("base", base), ("extras", extras)):
    for name, text in table.items():
        if name in common:
            continue
        owners = owner.get(name, [])
        if not owners:
            unowned.append((source, name))
            continue
        for cls in owners:
            if cls == "HeldItemMenu":
                continue
            assignment.setdefault(cls, {}).setdefault(name, text)
print("unowned (kept in MenuControls):", unowned)
for cls, table in sorted(assignment.items()):
    print(f"  {cls}: {len(table)}")
if not WRITE:
    sys.exit(0)
if overlap - common:
    sys.exit("overlap: resolve first")


def tooltip_method(table, server_only=False):
    rows = "\n".join(f"\t\t\t{name} => {text}," for name, text in table.items())
    doc = "\t/// <summary>Hover text of a row of this category (null: none).</summary>\n"
    if server_only:
        return doc + f"\tinternal static string Tooltip(string name)\n\t{{\n\t\tstring tooltip = name switch\n\t\t{{\n{rows}\n\t\t\t_ => null,\n\t\t}};\n\t\treturn (tooltip != null && _serverOnlyTooltips.Contains(name) && !ServerOnlyActionUnavailable(name)) ? null : tooltip;\n\t}}\n"
    return doc + f"\tinternal static string Tooltip(string name)\n\t{{\n\t\treturn name switch\n\t\t{{\n{rows}\n\t\t\t_ => null,\n\t\t}};\n\t}}\n"


def add_method(text, method):
    index = text.rstrip().rfind("\n}")
    return text[:index] + "\n\n" + method.rstrip("\n") + text[index:]


for cls, table in assignment.items():
    file = categories[cls]
    text = load(file)
    text = add_method(text, tooltip_method(table, server_only=(cls == "WorldInfoMenu")))
    if cls == "WorldInfoMenu":
        text = add_method(text, """\t/// <summary>Server-only actions only explain themselves when they cannot run.</summary>
\tprivate static bool ServerOnlyActionUnavailable(string name)
\t{
\t\treturn name switch
\t\t{
\t\t\t"Spawn Golem" => !WorldInfo.GolemSpawnEnabled,
\t\t\t"Spawn Mechdusa" => !WorldInfo.MechdusaEnabled,
\t\t\t_ => Main.netMode == 1,
\t\t};
\t}
""")
    save(file, text)

held = held.replace("internal static string HeldItemTooltip(string name)", "/// <summary>Hover text of a Held Item / Item Mod Presets row (null: none).</summary>\n\tinternal static string Tooltip(string name)")
save("HeldItemMenu.cs", held)

# ShowTooltip: ask every category, then the shared texts.
unowned_rows = "\n".join(f"\t\t\t{name} => {base.get(name) or extras.get(name)}," for _, name in unowned)
common_rows = "\n".join(f"\t\t\t{name} => {base[name]}," for name in sorted(common) if name in base)
new_show = f"""\tinternal static void ShowTooltip(UIElement element, string buttonName)
\t{{
\t\tif (!element.IsMouseHovering)
\t\t{{
\t\t\treturn;
\t\t}}
\t\tstring tooltip = HeldItemMenu.Tooltip(buttonName) ?? CategoryTooltip(buttonName) ?? SharedTooltip(buttonName);
\t\tif (tooltip != null)
\t\t{{
\t\t\tMain.instance.MouseText(tooltip, 0, 0);
\t\t}}
\t}}

\tprivate static readonly System.Func<string, string>[] CategoryTooltips =
\t{{
{chr(10).join(f"		{cls}.Tooltip," for cls in assignment)}
\t}};

\tprivate static string CategoryTooltip(string name)
\t{{
\t\tforeach (System.Func<string, string> tooltip in CategoryTooltips)
\t\t{{
\t\t\tstring text = tooltip(name);
\t\t\tif (text != null)
\t\t\t{{
\t\t\t\treturn text;
\t\t\t}}
\t\t}}
\t\treturn null;
\t}}

\t/// <summary>Texts shared by several categories, and rows no category lists.</summary>
\tprivate static string SharedTooltip(string name)
\t{{
\t\treturn name switch
\t\t{{
{common_rows}
{unowned_rows}
\t\t\t_ => null,
\t\t}};
\t}}
"""
show_end = controls.index("\n\t}\n", switch_end + 1) + len("\n\t}\n")
controls = controls[:show_start] + new_show + controls[show_end:]
save("MenuControls.cs", controls)

extras_file = re.sub(r"\t(?:private|internal) static readonly Dictionary<string, string> ExtraTooltips = new Dictionary<string, string>\n\t\{\n.*?\n\t\};\n\n?", "", extras_file, flags=re.S)
extras_file = re.sub(r"\tinternal static string ExtraTooltip\(string name\)\n\t\{\n.*?\n\t\}\n\n?", "", extras_file, flags=re.S)
save("ExtraCheatsMenu.cs", extras_file)
print("tooltips split")
