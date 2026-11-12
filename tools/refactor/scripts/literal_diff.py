"""Lists string literals of the old client code that no longer appear anywhere in the new code.

A renamed identifier is checked by the compiler; a changed string (JSON key, URL, file name, reflection name) is not.
usage: python literal_diff.py "<old src>" <new src>
"""
import collections
import os
import re
import sys

LITERAL = re.compile(r'@"(?:[^"]|"")*"|"(?:[^"\\\r\n]|\\.)*"')


def client_files(root, old):
    for dirpath, _, names in os.walk(root):
        rel = os.path.relpath(dirpath, root).replace("\\", "/")
        if "/bin" in "/" + rel or "/obj" in "/" + rel:
            continue
        for name in names:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(dirpath, name)
            if rel.startswith("IsaClient.") or rel.startswith("EasyUI"):
                yield path


def literals(root, old):
    found = collections.defaultdict(list)
    for path in client_files(root, old):
        text = open(path, encoding="utf-8-sig", errors="replace").read()
        for m in LITERAL.finditer(text):
            line = text.count("\n", 0, m.start()) + 1
            found[m.group(0)].append(f"{os.path.relpath(path, root)}:{line}")
    return found


def all_text(root):
    parts = []
    for dirpath, _, names in os.walk(root):
        if "\\bin" in dirpath or "\\obj" in dirpath or "/bin" in dirpath or "/obj" in dirpath:
            continue
        for name in names:
            if name.endswith((".cs", ".html", ".css")):
                parts.append(open(os.path.join(dirpath, name), encoding="utf-8-sig", errors="replace").read())
    return "\n".join(parts)


old = literals(sys.argv[1], True)
new_text = all_text(sys.argv[2])
missing = sorted((lit, where) for lit, where in old.items() if lit not in new_text)
for lit, where in missing:
    print(f"{lit}\t{where[0]}" + (f" (+{len(where) - 1})" if len(where) > 1 else ""))
print(f"# {len(missing)} of {len(old)} old literals missing", file=sys.stderr)
