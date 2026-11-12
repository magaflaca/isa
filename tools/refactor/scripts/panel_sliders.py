"""Phase 4: World Edit and Misc keep their own slider references private and sync them themselves."""
import os
import re

MENU = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src", "IsaClient.Menu")


def load(name):
    return open(os.path.join(MENU, name), encoding="utf-8-sig").read().replace("\r\n", "\n")


def save(name, text):
    open(os.path.join(MENU, name), "wb").write(b"\xef\xbb\xbf" + text.replace("\n", "\r\n").encode("utf-8"))


def add_method(text, method):
    index = text.rstrip().rfind("\n}")
    return text[:index] + "\n\n" + method.rstrip("\n") + text[index:]


panel = load("CheatPanel.cs")
m = re.search(r"\tprivate void UpdateSliders\(\)\n\t\{\n(.*?)\n\t\}\n", panel, re.S)
body = m.group(1)
misc_part = re.search(r"\t\tif \(MiscMenu\._killAuraSize != null\)\n\t\t\{\n.*?\n\t\t\}", body, re.S).group(0)
world_part = body.replace(misc_part, "").rstrip()
world_part = world_part.replace("\t\t\treturn;", "\t\t\treturn false;").replace("WorldEditMenu.", "")
misc_part = misc_part.replace("MiscMenu.", "")
save("CheatPanel.cs", panel.replace(m.group(0), """	private void UpdateSliders()
	{
		if (WorldEditMenu.SyncSliders())
		{
			MiscMenu.SyncSliders();
		}
	}
"""))

world = load("WorldEditMenu.cs")
world = add_method(world, "\t/// <summary>Points the sliders at the current values; false while the category has not been built yet.</summary>\n\tinternal static bool SyncSliders()\n\t{\n" + world_part + "\n\t\treturn true;\n\t}\n")
world = re.sub(r"\tinternal static (MenuSlider|MenuSimpleItem|MenuListItem) _", r"\tprivate static \1 _", world)
save("WorldEditMenu.cs", world)

misc = load("MiscMenu.cs")
misc = add_method(misc, "\t/// <summary>Points the Kill Aura slider at the current value.</summary>\n\tinternal static void SyncSliders()\n\t{\n" + misc_part + "\n\t}\n")
misc = misc.replace("\tinternal static MenuSlider _killAuraSize", "\tprivate static MenuSlider _killAuraSize")
save("MiscMenu.cs", misc)
print("sliders synced by their categories")
