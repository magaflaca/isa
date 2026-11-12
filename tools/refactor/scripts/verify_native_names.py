"""Checks that every [DllImport] of the client still points at the same native function as in an old copy of src.

Windows finds a native function by its exact name (the method name, or EntryPoint). Renaming waveOutOpen to
WaveOutOpen compiles fine and fails only when called ("Unable to find an entry point named 'WaveOutOpen'").
usage: python verify_native_names.py "<old src>" <new src>
"""
import glob
import re
import sys

IMPORT = re.compile(r'\[DllImport\(([^\]]*)\)\]\s*(?:\[[^\]]*\]\s*)*[\w\s]*extern\s+[\w<>\[\]]+\s+(\w+)\s*\(')


def native_names(root):
    found = set()
    for path in glob.glob(root + "/**/*.cs", recursive=True):
        if "\\Terraria\\" in path or "/Terraria/" in path or "\\obj\\" in path or "/obj/" in path:
            continue
        text = open(path, encoding="utf-8-sig", errors="replace").read()
        for m in IMPORT.finditer(text):
            entry = re.search(r'EntryPoint\s*=\s*"(\w+)"', m.group(1))
            dll = re.search(r'"([^"]+)"', m.group(1)).group(1).lower()
            found.add((dll, entry.group(1) if entry else m.group(2)))
    return found


old, new = native_names(sys.argv[1]), native_names(sys.argv[2])
print(f"native functions: {len(old)} -> {len(new)}; missing {sorted(old - new)}; new {sorted(new - old)}")
sys.exit(1 if old - new else 0)
