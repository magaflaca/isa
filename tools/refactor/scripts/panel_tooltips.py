"""Parses the tooltip switch of CheatPanel.UpdateButtonMousedOver: simple cases (one MouseText + break) vs others."""
import json
import os
import re
import sys

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src", "IsaClient.Menu")
text = open(os.path.join(SRC, "CheatPanel.cs"), encoding="utf-8-sig").read().replace("\r\n", "\n")
start = text.index("\tprivate static void UpdateButtonMousedOver(UIElement element, string buttonName)")
switch_start = text.index("\t\tswitch (buttonName)\n\t\t{\n", start) + len("\t\tswitch (buttonName)\n\t\t{\n")
end = text.index("\n\t\t}\n\t}\n", switch_start)
body = text[switch_start:end]
sections = re.split(r"\n(?=\t\tcase |\t\tdefault:)", "\n" + body)
simple, complex_ = {}, []
for section in sections:
    section = section.strip("\n")
    if not section:
        continue
    labels = re.findall(r'^\t\tcase "((?:[^"\\]|\\.)*)":', section, re.M)
    rest = re.sub(r'^\t\tcase "(?:[^"\\]|\\.)*":\n', "", section, flags=re.M).strip()
    m = re.fullmatch(r'Main\.instance\.MouseText\(("(?:[^"\\]|\\.)*")(?:, 0, 0)?\);\s*break;', rest, re.S)
    if labels and m:
        for label in labels:
            simple[label] = m.group(1)
    else:
        complex_.append((labels, rest[:160]))
print(f"simple: {len(simple)}  complex: {len(complex_)}")
for labels, rest in complex_:
    print("  ", labels, "|", rest.replace("\n", " ")[:150])
json.dump(simple, open(os.path.join(SRC, "..", "..", "tools", "refactor", ".work", "tooltips.json"), "w", encoding="utf-8"), indent=0, ensure_ascii=False)
