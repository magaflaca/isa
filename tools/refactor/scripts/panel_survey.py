"""Survey of CheatPanel: per method, which instance members of the panel it uses."""
import glob
import os
import re

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src", "IsaClient.Menu")
texts = {os.path.basename(p): open(p, encoding="utf-8-sig").read() for p in glob.glob(os.path.join(SRC, "CheatPanel*.cs"))}
all_text = "\n".join(texts.values())
instance_fields = set(re.findall(r"^\t(?:private|public|internal|protected) (?!static|const)(?:readonly )?[\w<>\[\],. ]+ (_?\w+)(?: = [^;]*)?;", all_text, re.M))
instance_methods = set(re.findall(r"^\t(?:private|public|internal|protected) (?!static)(?:override )?[\w<>\[\],. ]+ (\w+)\(", all_text, re.M))
print("instance fields:", sorted(instance_fields))
print("instance methods:", sorted(instance_methods))
for name, text in texts.items():
    for m in re.finditer(r"^\t(?:private|public|internal|protected) (static )?[\w<>\[\],. ]+ (\w+)\(([^)]*)\)\r?\n\t\{(.*?)^\t\}", text, re.M | re.S):
        static, method, body = m.group(1), m.group(2), m.group(4)
        used_f = sorted(f for f in instance_fields if re.search(r"(?<![\w.])" + re.escape(f) + r"\b", body))
        used_m = sorted(x for x in instance_methods if x != method and re.search(r"(?<![\w.])" + re.escape(x) + r"\(", body))
        lines = body.count("\n")
        if lines > 40 or used_f:
            print(f"{name:26} {'static ' if static else ''}{method:34} {lines:5} lines  fields={used_f}  methods={used_m}")
