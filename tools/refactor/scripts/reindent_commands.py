"""Re-indents the generated chat command handlers ("#region Chat commands" blocks) by brace depth.

Project style: braces on their own lines, one tab per level, `case`/`default` labels at the level of the switch braces.
Lines that continue an expression (inside parentheses or after an operator) keep their offset from the statement start.
"""
import glob
import os
import re

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src")


def reindent_method(lines):
    out = []
    depth = 1  # method braces are at one tab
    paren = 0
    for raw in lines:
        stripped = raw.strip()
        if not stripped:
            out.append("")
            continue
        if stripped.startswith("}"):
            depth -= 1
        level = depth
        if paren > 0:
            level = depth + 1
        elif re.match(r"(case\s.*|default):\s*$", stripped):
            level = depth - 1
        out.append("\t" * level + stripped)
        if stripped.startswith("{") or (stripped.endswith("{") and not stripped.startswith("}")):
            depth += 1
        # parentheses that stay open continue on the next line
        code = re.sub(r'"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])\'', "", stripped)
        paren += code.count("(") - code.count(")")
        paren = max(paren, 0)
    return out


def process(text):
    nl = "\r\n" if "\r\n" in text else "\n"
    lines = text.split(nl)
    result = []
    i = 0
    in_region = False
    while i < len(lines):
        line = lines[i]
        if line.strip() == "#region Chat commands":
            in_region = True
        elif line.strip() == "#endregion" and in_region:
            in_region = False
        if in_region and line.startswith("\tinternal static void ") and line.rstrip().endswith("(CommandContext command)"):
            result.append(line)
            # method body: from "\t{" to the matching "\t}"
            j = i + 1
            body = []
            while not (lines[j] == "\t}" ):
                body.append(lines[j])
                j += 1
            body.append(lines[j])
            fixed = reindent_method(body)
            fixed[0] = "\t{"
            fixed[-1] = "\t}"
            result.extend(fixed)
            i = j + 1
            continue
        result.append(line)
        i += 1
    return nl.join(result)


changed = 0
for path in glob.glob(os.path.join(SRC, "**", "*.cs"), recursive=True):
    if os.sep + "bin" + os.sep in path or os.sep + "obj" + os.sep in path:
        continue
    raw = open(path, "rb").read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    text = raw.decode("utf-8-sig")
    if "#region Chat commands" not in text:
        continue
    new = process(text)
    if new != text:
        open(path, "wb").write((b"\xef\xbb\xbf" if bom else b"") + new.encode("utf-8"))
        changed += 1
print("re-indented", changed, "files")
