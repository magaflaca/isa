"""Renames a module created by a move plan: python rename_module.py <Ns.Old> <New> <plan.move>
References are found by member name (Old.member for the members the plan moved into Old)."""
import glob, os, re, sys
old_full, new, plan = sys.argv[1], sys.argv[2], sys.argv[3]
ns, old = old_full.rsplit(".", 1)
members = [l.split("->")[0].strip().rsplit(".", 1)[1] for l in open(plan, encoding="utf-8")
           if "->" in l and l.split("->")[1].strip() == "IsaClient." + old_full]
src = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "src")
pat = re.compile(r'(?<![\w.])' + old + r'\.(' + "|".join(map(re.escape, members)) + r')\b')
folder = os.path.join(src, "IsaClient." + ns)
os.rename(os.path.join(folder, old + ".cs"), os.path.join(folder, new + ".cs"))
count = 0
for p in glob.glob(os.path.join(src, "**", "*.cs"), recursive=True):
    rel = os.path.relpath(p, src)
    if rel.startswith(("bin", "obj")):
        continue
    b = open(p, "rb").read(); bom = b.startswith(b"\xef\xbb\xbf"); s = b.decode("utf-8-sig")
    s2 = pat.sub(new + r".\1", s)
    if os.path.basename(p) == new + ".cs":
        s2 = s2.replace("static partial class " + old + "\r\n", "static partial class " + new + "\r\n").replace("static partial class " + old + "\n", "static partial class " + new + "\n")
    if s2 != s:
        count += 1
        open(p, "wb").write((b"\xef\xbb\xbf" if bom else b"") + s2.encode("utf-8"))
print(f"{old} -> {new}: {len(members)} members, {count} files")
