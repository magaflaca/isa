"""Makes private members internal when the build says another class now needs them (CS0122 after a move).

    dotnet build ... 2>&1 | python scripts/fix_access.py
"""
import glob
import os
import re
import sys

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src")
wanted = set()
for line in sys.stdin:
    m = re.search(r"error CS0122: '([\w.]+?)\.(\w+)(?:\([^']*\))?'", line)
    if m:
        wanted.add((m.group(1).split(".")[-1], m.group(2)))
files = [f for f in glob.glob(os.path.join(SRC, "IsaClient.*", "*.cs"))]
changed = 0
for type_name, member in sorted(wanted):
    done = False
    for path in files:
        raw = open(path, "rb").read()
        text = raw.decode("utf-8-sig")
        if not re.search(r"\b(class|struct) " + type_name + r"\b", text):
            continue
        pattern = re.compile(r"^(\t)private (?=(?:static |readonly |const |async |unsafe |volatile )*[\w<>\[\],.?() ]*?\b" + re.escape(member) + r"\b\s*(?:\(|=|;|\{|$))", re.M)
        new, n = pattern.subn(r"\1internal ", text)
        if n:
            open(path, "wb").write((b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b"") + new.encode("utf-8"))
            changed += n
            done = True
    if not done:
        print("not found:", type_name, member)
print(f"{changed} declaration(s) made internal for {len(wanted)} member(s)")
