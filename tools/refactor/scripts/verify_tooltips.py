"""Checks that every (row name, tooltip text) pair of the old panel still exists after the split.

    python scripts/verify_tooltips.py <old IsaClient.Menu folder> <new IsaClient.Menu folder>
"""
import glob
import os
import re
import sys

STRING = r'"(?:[^"\\]|\\.)*"'


def pairs_old(folder):
    text = "\n".join(open(p, encoding="utf-8-sig").read().replace("\r\n", "\n") for p in glob.glob(os.path.join(folder, "CheatPanel*.cs")))
    found = {}
    switch = text[text.index("static void UpdateButtonMousedOver"):]
    switch = switch[:switch.index("\n\t}\n")]
    pending = []
    for line in switch.split("\n"):
        m = re.fullmatch(r"\t\tcase (" + STRING + "):", line)
        if m:
            pending.append(m.group(1))
            continue
        m = re.fullmatch(r"\t\t\tMain\.instance\.MouseText\((.*), 0, 0\);", line)
        if m:
            for n in pending:
                found[n] = m.group(1)
        if line.strip() == "break;":
            pending = []
    extras = re.search(r"ExtraTooltips = new Dictionary<string, string>\n\t\{\n(.*?)\n\t\};", text, re.S).group(1)
    for m in re.finditer(r"\{ (" + STRING + r"), (" + STRING + r"(?:\s*\+\s*" + STRING + r")*) \}", extras):
        found[m.group(1)] = m.group(2)
    return found


def pairs_new(folder):
    found = {}
    for path in glob.glob(os.path.join(folder, "*.cs")):
        text = open(path, encoding="utf-8-sig").read().replace("\r\n", "\n")
        for m in re.finditer(r"static string (?:Tooltip|SharedTooltip)\(string name\)\n\t\{\n(.*?)\n\t\}\n", text, re.S):
            for row in re.finditer(r"^\t\t\t(" + STRING + r") => (.*),$", m.group(1), re.M):
                found.setdefault(row.group(1), set()).add(row.group(2))
    return found


old, new = pairs_old(sys.argv[1]), pairs_new(sys.argv[2])
missing = [n for n in old if n not in new]
changed = [n for n in old if n in new and old[n] not in new[n]]
ambiguous = [n for n in new if len(new[n]) > 1]
print(f"old {len(old)} tooltips, new {len(new)}; missing {missing}; changed {changed}; different texts for one name {ambiguous}")
sys.exit(1 if missing or changed or ambiguous else 0)
