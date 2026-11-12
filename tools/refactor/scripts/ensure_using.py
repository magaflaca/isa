"""Adds `using <namespace>;` to every .cs file that contains <marker> and does not see that namespace yet.

    python scripts/ensure_using.py "[Setting(" IsaClient.Core
"""
import glob
import os
import re
import sys

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src")
marker, namespace = sys.argv[1], sys.argv[2]
changed = 0
for path in glob.glob(os.path.join(SRC, "**", "*.cs"), recursive=True):
    if os.sep + "bin" + os.sep in path or os.sep + "obj" + os.sep in path:
        continue
    raw = open(path, "rb").read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    text = raw.decode("utf-8-sig")
    if marker not in text:
        continue
    file_ns = re.search(r"^namespace ([\w.]+)", text, re.M)
    if f"using {namespace};" in text or (file_ns and (file_ns.group(1) == namespace or file_ns.group(1).startswith(namespace + "."))):
        continue
    nl = "\r\n" if "\r\n" in text else "\n"
    usings = list(re.finditer(r"^using [^\n]*\n", text, re.M))
    pos = usings[-1].end() if usings else 0
    text = text[:pos] + f"using {namespace};" + nl + text[pos:]
    open(path, "wb").write((b"\xef\xbb\xbf" if bom else b"") + text.encode("utf-8"))
    changed += 1
print(f"added using {namespace} to {changed} file(s)")
