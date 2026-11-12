"""Checks that the [Setting] fields save the same keys as the old Load/Save code, with the same "remembered" gating.

    python scripts/verify_settings.py <folder with the old src> <src>
"""
import glob
import os
import re
import sys

old_root, new_root = sys.argv[1], sys.argv[2]


def read(path):
    return open(path, encoding="utf-8-sig").read()


old_settings = read(os.path.join(old_root, "IsaClient.Core", "ClientSettings.cs"))
old_files = [old_settings] + [read(os.path.join(old_root, p)) for p in [
    "IsaClient.Gameplay/PlayerCheats.cs", "IsaClient.Rendering/Zoom.cs", "IsaClient.Rendering/FullbrightLighting.cs",
    "IsaClient.Networking/ProtocolCompat.cs", "IsaClient.Gameplay/BuffTools.cs"]]
old_put = set()
for text in old_files:
    old_put |= set(re.findall(r'\.Put\("(\w+)"', text))

load = re.search(r"public static void Load\(bool NoHacks = false\)(.*?)\n\t\}", old_settings, re.S).group(1)
gated_block = load[load.index("if (Selection.RememberEnabled && !NoHacks)"):]
gated = set(re.findall(r'\.Get\("(\w+)"', gated_block))
for name in ["LoadInfoSettings", "LoadFeatureSettings"]:
    body = re.search(name + r"\(Preferences cfg\)(.*?)\n\t\}", old_settings, re.S).group(1)
    gated |= set(re.findall(r'\.Get\("(\w+)"', body))
cheats = read(os.path.join(old_root, "IsaClient.Gameplay/PlayerCheats.cs"))
gated |= set(re.findall(r'\.Get\("(\w+)"', re.search(r"LoadPlayerCheats\(Preferences cfg\)(.*?)\n\t\}", cheats, re.S).group(1)))

new_keys, new_gated, legacy = set(), set(), {}
for path in glob.glob(os.path.join(new_root, "**", "*.cs"), recursive=True):
    if os.sep + "bin" + os.sep in path or os.sep + "obj" + os.sep in path:
        continue
    for m in re.finditer(r'\[Setting\("(\w+)"([^\]]*)\)\]', read(path)):
        new_keys.add(m.group(1))
        if "Remembered = true" in m.group(2):
            new_gated.add(m.group(1))
        lk = re.search(r'LegacyKey = "(\w+)"', m.group(2))
        if lk:
            legacy[lk.group(1)] = m.group(1)
new_keys |= {"NetworkVersion", "CommandAbbreviations"}
mapped_old = {legacy.get(k, k) for k in old_put}
mapped_gated = {legacy.get(k, k) for k in gated}
print(f"old keys: {len(old_put)}  new keys: {len(new_keys)}")
print("missing:", sorted(mapped_old - new_keys))
print("extra:", sorted(new_keys - mapped_old))
print("gating differs:", sorted((mapped_gated ^ new_gated) & new_keys))
