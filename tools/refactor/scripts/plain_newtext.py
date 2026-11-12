"""Turns client messages written with Main.NewText(text) (no color, so white) into ClientChat.Info(text).

Only single-argument calls in IsaClient.* files are changed; calls with a color keep it.
usage: python plain_newtext.py <src>
"""
import glob
import os
import re
import sys

CALL = re.compile(r"Main\.NewText\(")


def single_argument_end(text, start):
    """Index of the closing parenthesis when the call has exactly one argument, else -1."""
    depth, i, quote = 1, start, None
    while i < len(text):
        c = text[i]
        if quote:
            if c == "\\":
                i += 2
                continue
            if c == quote:
                quote = None
        elif c in "\"'":
            quote = c
        elif c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
            if depth == 0:
                return i
        elif c == "," and depth == 1:
            return -1
        i += 1
    return -1


total = 0
for path in glob.glob(os.path.join(sys.argv[1], "IsaClient.*", "*.cs")):
    if path.endswith("ClientChat.cs"):
        continue
    raw = open(path, "rb").read()
    text = raw.decode("utf-8-sig")
    out, pos, changed = [], 0, 0
    for m in CALL.finditer(text):
        if m.start() < pos:
            continue
        end = single_argument_end(text, m.end())
        if end < 0:
            continue
        out.append(text[pos:m.start()] + "ClientChat.Info(" + text[m.end():end + 1])
        pos = end + 1
        changed += 1
    if changed:
        out.append(text[pos:])
        new = "".join(out)
        if "using IsaClient.Core;" not in new and "namespace IsaClient.Core;" not in new:
            new = new.replace("using Terraria;", "using IsaClient.Core;\r\nusing Terraria;", 1)
        open(path, "wb").write(("﻿" if raw.startswith(b"\xef\xbb\xbf") else "").encode("utf-8") + new.encode("utf-8"))
        print(f"{changed:3} {os.path.relpath(path, sys.argv[1])}")
        total += changed
print(f"{total} calls")
