"""Compares command registrations and [Setting] declarations between a snapshot folder and the current src.

    python scripts/verify_against_snapshot.py <snapshot src folder> <src folder>
"""
import glob
import os
import re
import sys


def read_all(root):
    out = {}
    for path in glob.glob(os.path.join(root, "**", "*.cs"), recursive=True):
        if os.sep + "bin" + os.sep in path or os.sep + "obj" + os.sep in path:
            continue
        out[os.path.relpath(path, root)] = open(path, encoding="utf-8-sig").read()
    return out


def commands(files):
    catalog = next(t for p, t in files.items() if p.endswith("CommandCatalog.cs"))
    names = {}
    for line in catalog.splitlines():
        m = re.search(r'CommandRegistry\.Register\(([\w.]+), ((?:"[^"]*"(?:, )?)+)\)', line)
        if m:
            for name in re.findall(r'"([^"]+)"', m.group(2)):
                names[name] = m.group(1).split(".")[-1]
    parsers = re.findall(r"RegisterParser\(c => (.*)\);", catalog)
    return names, parsers


def settings(files):
    found = {}
    for text in files.values():
        for m in re.finditer(r'\[Setting\(("\w+")([^\]]*)\)\]\s*\n\s*(?:public|internal|private) static [^=;]*? (\w+)(?: =|;)', text):
            found[m.group(1)] = (m.group(2).strip(), m.group(3))
    return found


old, new = read_all(sys.argv[1]), read_all(sys.argv[2])
old_cmd, old_parsers = commands(old)
new_cmd, new_parsers = commands(new)
print(f"commands: {len(old_cmd)} -> {len(new_cmd)}; missing {sorted(set(old_cmd) - set(new_cmd))}; extra {sorted(set(new_cmd) - set(old_cmd))}")
order_ok = [n for n in old_cmd] == [n for n in new_cmd]
print(f"command lookup order unchanged: {order_ok}; parsers unchanged: {old_parsers == new_parsers}")
old_set, new_set = settings(old), settings(new)
print(f"settings: {len(old_set)} -> {len(new_set)}; missing {sorted(set(old_set) - set(new_set))}; extra {sorted(set(new_set) - set(old_set))}")
changed = [k for k in old_set if k in new_set and old_set[k][0] != new_set[k][0]]
print(f"settings with different options: {changed}")
bad = (set(old_cmd) != set(new_cmd)) or not order_ok or old_parsers != new_parsers or set(old_set) != set(new_set) or changed
sys.exit(1 if bad else 0)
