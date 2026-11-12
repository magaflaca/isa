"""Phase 4: the mixed CreateExtraCheatElement switch is split into the categories it belongs to.

Travel and Render rows join their category's own switch (before its own cases, as they used to win); Cosmetic and
Audio get their own classes (CosmeticMenu, AudioMenu); the Biome Spoofers "Disable All" joins BiomeSpooferMenu.

    python scripts/panel_extras.py          (dry run: report duplicates)
    python scripts/panel_extras.py --write
"""
import os
import re
import sys

MENU = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src", "IsaClient.Menu")
WRITE = "--write" in sys.argv
LABEL = re.compile(r'^\t\tcase "((?:[^"\\]|\\.)*)":', re.M)


def load(name):
    return open(os.path.join(MENU, name), encoding="utf-8-sig").read().replace("\r\n", "\n")


def save(name, text):
    open(os.path.join(MENU, name), "wb").write(b"\xef\xbb\xbf" + text.replace("\n", "\r\n").encode("utf-8"))


extras = load("ExtraCheatsMenu.cs")
lines = extras.split("\n")
i_travel = lines.index("\t\t// Travel")
i_render = lines.index("\t\t// Render")
i_cosmetic = lines.index("\t\t// Cosmetic")
i_audio = lines.index("\t\t// Audio")
i_disable = lines.index('\t\tcase "Disable & Reset All":')
parts = {
    "travel": "\n".join(lines[i_travel + 1:i_render]),
    "render": "\n".join(lines[i_render + 1:i_cosmetic]),
    "cosmetic": "\n".join(lines[i_cosmetic + 1:i_audio]),
    "audio": "\n".join(lines[i_audio + 1:i_disable]),
}
for name, text in parts.items():
    print(name, len(LABEL.findall(text)), "cases")

travel = load("TravelMenu.cs")
render = load("RenderMenu.cs")
dup_travel = set(LABEL.findall(parts["travel"])) & set(LABEL.findall(travel))
dup_render = set(LABEL.findall(parts["render"])) & set(LABEL.findall(render))
print("duplicates travel:", dup_travel, " render:", dup_render)
if not WRITE:
    sys.exit(0)
if dup_travel or dup_render:
    sys.exit("duplicates: handle them first")


def insert_cases(text, method, cases):
    start = text.index(method)
    switch = text.index("\t\tswitch (cheat)\n\t\t{\n", start) + len("\t\tswitch (cheat)\n\t\t{\n")
    return text[:switch] + cases.rstrip("\n") + "\n" + text[switch:]


save("TravelMenu.cs", insert_cases(travel, "static UIElement CreatePanel_TravelHacks(", parts["travel"]))
save("RenderMenu.cs", insert_cases(render, "static UIElement CreatePanel_RenderHacks(", parts["render"]))

usings = "\n".join(l for l in extras.split("\n") if l.startswith("using "))


def new_category(class_name, summary, cases, disable_call):
    return f"""{usings}

namespace IsaClient.Menu;

/// <summary>{summary}</summary>
internal static class {class_name}
{{
	internal static UIElement Create(string cheat, Color color)
	{{
		switch (cheat)
		{{
{cases.rstrip()}
		case "Disable & Reset All":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{{
				SoundEngine.PlaySound(11);
				{disable_call};
			}});
		default:
			return new UIElement();
		}}
	}}
}}
"""


save("CosmeticMenu.cs", new_category("CosmeticMenu", "Cheats → Cosmetic: walking/death particles, death effects and sounds, Lucy, after-images, RGB map icons.", parts["cosmetic"], "Cosmetics.DisableAll()"))
save("AudioMenu.cs", new_category("AudioMenu", "Cheats → Audio Tweaks: swap music, forced soundtrack and the classic/disabled themes.", parts["audio"], "AudioTweaks.DisableAudioTweaks()"))

# Biome Spoofers: its "Disable All" lived in the extras switch.
biome = load("BiomeSpooferMenu.cs")
biome = insert_cases(biome, "static UIElement CreatePanel_BiomeSpoofer(", """		case "Disable All":
			return MenuControls.MakeButton(cheat, color, () => cheat, delegate
			{
				SoundEngine.PlaySound(11);
				BiomeSpoofers.DisableBiomeSpoofers();
			});
""")
save("BiomeSpooferMenu.cs", biome)

# What is left of CreateExtraCheatElement: only the World Information dispatch.
start = extras.index("\t/// <summary>The ported rows; null when the row isn't one of them")
end = extras.index("\n\t}\n", extras.index("internal static UIElement CreateExtraCheatElement(")) + len("\n\t}\n")
extras = extras[:start] + extras[end:]
save("ExtraCheatsMenu.cs", extras)
print("extras split")
