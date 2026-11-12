"""Removes `partial` from IsaClient classes declared in a single file (left over from the moves)."""
import collections
import glob
import os
import re

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src")
declarations = collections.defaultdict(list)
files = glob.glob(os.path.join(SRC, "**", "*.cs"), recursive=True)
files = [f for f in files if os.sep + "bin" + os.sep not in f and os.sep + "obj" + os.sep not in f]
for path in files:
    text = open(path, encoding="utf-8-sig").read()
    namespace = re.search(r"^namespace ([\w.]+)", text, re.M)
    for m in re.finditer(r"\bpartial (?:class|struct) (\w+)", text):
        declarations[(namespace.group(1) if namespace else "", m.group(1))].append(path)
changed = 0
for (namespace, name), paths in declarations.items():
    if not namespace.startswith("IsaClient") or len(paths) != 1:
        continue
    path = paths[0]
    raw = open(path, "rb").read()
    text = raw.decode("utf-8-sig")
    new = re.sub(r"\b(static )?partial (class|struct) " + name + r"\b", lambda m: (m.group(1) or "") + m.group(2) + " " + name, text, count=1)
    if new != text:
        open(path, "wb").write((b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b"") + new.encode("utf-8"))
        changed += 1
print(f"partial removed from {changed} class(es)")
