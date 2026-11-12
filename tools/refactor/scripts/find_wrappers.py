"""Lists command handlers whose whole body is one call to another method of the code base."""
import glob
import os
import re

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src")
pattern = re.compile(r"\tinternal static void (\w+Command)\(CommandContext command\)\r?\n\t\{\r?\n\t\t([\w.]+)\(([^;]*)\);\r?\n\t\}")
for path in sorted(glob.glob(os.path.join(SRC, "IsaClient.*", "*.cs"))):
    text = open(path, encoding="utf-8-sig").read()
    for m in pattern.finditer(text):
        print(f"{os.path.basename(path):28} {m.group(1):32} -> {m.group(2)}({m.group(3)})")
