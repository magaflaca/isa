#!/usr/bin/env python3
"""IsaClient source tree tool.

  python isa.py setup      build the full source tree in src/ from your own Terraria.exe
  python isa.py patches    write your changes to game files back into patches/
  python isa.py status     list the game files that differ from patches/
"""
import argparse
import difflib
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(ROOT, "src")
PATCHES = os.path.join(ROOT, "patches")
WORK = os.path.join(ROOT, ".vanilla")
VANILLA = os.path.join(WORK, "src")
TOOLS = os.path.join(ROOT, ".tools")
STATE = os.path.join(WORK, "state.json")
MANIFEST = os.path.join(PATCHES, "manifest.json")
HASHES = os.path.join(PATCHES, "vanilla.sha1")
OWN_PREFIX = "IsaClient."
SKIP_DIRS = ("bin", "obj")
BS = chr(92)
NO_NEWLINE = BS.encode() + b" No newline at end of file\n"

IGNORE_HEAD = [
    "/.vanilla/",
    "/.tools/",
    "bin/",
    "obj/",
    "__pycache__/",
    ".vs/",
    ".idea/",
    "*.user",
    "/src/*",
    "!/src/IsaClient.*/",
]


def fail(message):
    sys.exit("error: " + message)


def read(path):
    with open(path, "rb") as f:
        return f.read()


def write(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(data)


def sha1(data):
    return hashlib.sha1(data).hexdigest()


def blob_sha(data):
    return hashlib.sha1(b"blob %d\0" % len(data) + data).hexdigest()


def is_text(data):
    return b"\0" not in data


def is_own(rel):
    return rel.startswith(OWN_PREFIX)


def split_lines(data):
    lines = data.split(b"\n")
    out = [line + b"\n" for line in lines[:-1]]
    if lines[-1]:
        out.append(lines[-1])
    return out


def make_patch(rel, old, new):
    a = split_lines(old)
    b = split_lines(new)
    name = rel.encode("utf-8")
    out = [b"--- a/" + name + b"\n", b"+++ b/" + name + b"\n"]

    def emit(prefix, line):
        out.append(prefix + line)
        if not line.endswith(b"\n"):
            out.append(b"\n" + NO_NEWLINE)

    for group in difflib.SequenceMatcher(None, a, b, autojunk=False).get_grouped_opcodes(3):
        a1, a2, b1, b2 = group[0][1], group[-1][2], group[0][3], group[-1][4]
        out.append(("@@ -%d,%d +%d,%d @@\n" % (a1 + 1 if a2 > a1 else a1, a2 - a1, b1 + 1 if b2 > b1 else b1, b2 - b1)).encode())
        for tag, i1, i2, j1, j2 in group:
            if tag == "equal":
                for line in a[i1:i2]:
                    emit(b" ", line)
                continue
            if tag in ("replace", "delete"):
                for line in a[i1:i2]:
                    emit(b"-", line)
            if tag in ("replace", "insert"):
                for line in b[j1:j2]:
                    emit(b"+", line)
    return b"".join(out)


HUNK = re.compile(rb"^@@ -(\d+),(\d+) \+(\d+),(\d+) @@")


def apply_patch(old, patch, rel="file"):
    a = split_lines(old)
    lines = split_lines(patch)
    out = []
    pos = 0
    i = 0
    while i < len(lines) and not lines[i].startswith(b"@@"):
        i += 1
    while i < len(lines):
        m = HUNK.match(lines[i])
        if not m:
            raise ValueError("%s: bad hunk header at patch line %d" % (rel, i + 1))
        start, old_count, new_count = int(m.group(1)), int(m.group(2)), int(m.group(4))
        start = start - 1 if old_count else start
        if start < pos:
            raise ValueError("%s: hunks out of order" % rel)
        out.extend(a[pos:start])
        pos = start
        i += 1
        seen_old = seen_new = 0
        while seen_old < old_count or seen_new < new_count:
            if i >= len(lines):
                raise ValueError("%s: patch ends inside a hunk" % rel)
            kind, body = lines[i][:1], lines[i][1:]
            i += 1
            if i < len(lines) and lines[i] == NO_NEWLINE:
                body = body[:-1]
                i += 1
            if kind not in (b" ", b"-", b"+"):
                raise ValueError("%s: bad patch line %d" % (rel, i))
            if kind in (b" ", b"-"):
                if pos >= len(a) or a[pos] != body:
                    raise ValueError("%s: line %d does not match the game file (decompile mismatch or stale patch)" % (rel, pos + 1))
                pos += 1
                seen_old += 1
            if kind in (b" ", b"+"):
                out.append(body)
                seen_new += 1
    out.extend(a[pos:])
    return b"".join(out)


def walk(root):
    found = {}
    for folder, dirs, names in os.walk(root):
        rel_folder = os.path.relpath(folder, root).replace(os.sep, "/")
        if rel_folder == ".":
            dirs[:] = [d for d in dirs if d not in SKIP_DIRS and not d.startswith(".")]
        for name in names:
            rel = name if rel_folder == "." else rel_folder + "/" + name
            found[rel] = os.path.join(folder, name)
    return found


def load_manifest():
    if not os.path.exists(MANIFEST):
        fail("patches/manifest.json is missing")
    with open(MANIFEST, encoding="utf-8") as f:
        return json.load(f)


def manifest_bytes(manifest):
    return (json.dumps(manifest, indent=2, ensure_ascii=False) + "\n").encode("utf-8")


def gitignore_text(manifest):
    lines = list(IGNORE_HEAD)
    for rel in sorted(set(manifest.get("replaced", [])) | set(manifest.get("added", []))):
        lines.append("!/src/" + rel)
    return "\n".join(lines) + "\n"


def load_state():
    if os.path.exists(STATE):
        with open(STATE, encoding="utf-8") as f:
            return json.load(f)
    return {}


def save_state(state):
    write(STATE, json.dumps(state, indent=0, sort_keys=True).encode("utf-8"))


def patch_path(rel):
    return os.path.join(PATCHES, rel.replace("/", os.sep) + ".patch")


def steam_roots():
    roots = []
    try:
        import winreg
        for hive, key, value in (
            (winreg.HKEY_CURRENT_USER, BS.join(("Software", "Valve", "Steam")), "SteamPath"),
            (winreg.HKEY_LOCAL_MACHINE, BS.join(("SOFTWARE", "WOW6432Node", "Valve", "Steam")), "InstallPath"),
            (winreg.HKEY_LOCAL_MACHINE, BS.join(("SOFTWARE", "Valve", "Steam")), "InstallPath"),
        ):
            try:
                with winreg.OpenKey(hive, key) as k:
                    roots.append(winreg.QueryValueEx(k, value)[0])
            except OSError:
                pass
    except ImportError:
        pass
    for drive in "CDEFGH":
        for tail in ("Program Files (x86)/Steam", "Program Files/Steam", "Steam", "SteamLibrary", "Games/Steam", "Games/SteamLibrary"):
            roots.append(drive + ":/" + tail)
    libraries = []
    for root in roots:
        libraries.append(root)
        vdf = os.path.join(root, "steamapps", "libraryfolders.vdf")
        if os.path.exists(vdf):
            text = open(vdf, encoding="utf-8", errors="replace").read()
            for m in re.finditer(r'"path"\s+"([^"]+)"', text):
                libraries.append(m.group(1).replace(BS + BS, BS))
    return libraries


def find_terraria(given):
    candidates = []
    if given:
        candidates.append(given)
    elif os.environ.get("TERRARIA_PATH"):
        candidates.append(os.environ["TERRARIA_PATH"])
    else:
        for library in steam_roots():
            candidates.append(os.path.join(library, "steamapps", "common", "Terraria"))
    for candidate in candidates:
        exe = candidate if candidate.lower().endswith(".exe") else os.path.join(candidate, "Terraria.exe")
        if os.path.isfile(exe):
            return os.path.abspath(exe)
    fail("Terraria.exe not found. Pass it: python isa.py setup --terraria C:/path/to/Terraria")


def run(args, **kw):
    try:
        return subprocess.run(args, **kw)
    except FileNotFoundError:
        fail("'%s' not found. Install the .NET 10 SDK: https://dotnet.microsoft.com/download" % args[0])


def decompile(exe, manifest, force):
    digest = hashlib.sha256(read(exe)).hexdigest()
    if digest != manifest["exe_sha256"] and not force:
        fail("%s is not %s.\n  expected sha256 %s\n  found           %s\n"
             "The patches only fit that build. Use --force to try anyway." % (exe, manifest["game"], manifest["exe_sha256"], digest))
    marker = os.path.join(WORK, "decompiled.json")
    stamp = {"exe_sha256": digest, "decompiler_version": manifest["decompiler_version"]}
    if os.path.exists(marker) and json.load(open(marker)) == stamp and os.path.isdir(VANILLA):
        return
    tool = os.path.join(TOOLS, "ilspycmd.exe" if os.name == "nt" else "ilspycmd")
    if not os.path.exists(tool):
        print("installing ilspycmd %s into .tools/" % manifest["decompiler_version"], flush=True)
        done = run(["dotnet", "tool", "install", "ilspycmd", "--version", manifest["decompiler_version"], "--tool-path", TOOLS])
        if done.returncode != 0:
            fail("could not install ilspycmd")
    if os.path.isdir(VANILLA):
        shutil.rmtree(VANILLA)
    os.makedirs(VANILLA)
    print("decompiling %s (a few minutes)" % exe, flush=True)
    done = run([tool, "-p", "-o", VANILLA, exe], stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if done.returncode != 0:
        sys.stdout.write(done.stdout.decode(errors="replace"))
        fail("ilspycmd failed")
    for name in SKIP_DIRS:
        shutil.rmtree(os.path.join(VANILLA, name), ignore_errors=True)
    write(marker, json.dumps(stamp).encode())


def check_decompile(vanilla, skip):
    if not os.path.exists(HASHES):
        return
    expected = {}
    for line in open(HASHES, encoding="utf-8"):
        if line.strip():
            digest, rel = line.rstrip("\n").split("  ", 1)
            expected[rel] = digest
    different = [rel for rel, digest in expected.items() if rel not in skip and (rel not in vanilla or blob_sha(read(vanilla[rel])) != digest)]
    extra = [rel for rel in vanilla if rel not in expected]
    if different or extra:
        print("warning: your decompile differs from the reference in %d file(s):" % (len(different) + len(extra)))
        for rel in (different + extra)[:15]:
            print("  " + rel)
        print("  Patches may not apply. Check the decompiler version and that XNA 4.0 is installed.")


def need_vanilla():
    if not os.path.isdir(VANILLA):
        fail("run 'python isa.py setup' first")
    return walk(VANILLA)


def cmd_setup(args):
    manifest = load_manifest()
    exe = find_terraria(args.terraria)
    decompile(exe, manifest, args.force)
    vanilla = walk(VANILLA)
    state = load_state()
    replaced = set(manifest.get("replaced", []))
    check_decompile(vanilla, replaced)
    removed = set(manifest.get("removed", []))
    written = kept = 0
    errors = []
    for rel in sorted(vanilla):
        dest = os.path.join(SRC, rel.replace("/", os.sep))
        if rel in replaced:
            continue
        if rel in removed:
            if os.path.exists(dest) and state.get(rel) == sha1(read(dest)):
                os.remove(dest)
            state.pop(rel, None)
            continue
        target = read(vanilla[rel])
        if os.path.exists(patch_path(rel)):
            try:
                target = apply_patch(target, read(patch_path(rel)), rel)
            except ValueError as e:
                errors.append(str(e))
                continue
        digest = sha1(target)
        if os.path.exists(dest):
            current = sha1(read(dest))
            if current == digest:
                state[rel] = digest
                continue
            if state.get(rel) != current and not args.reset:
                kept += 1
                print("kept (you changed it): src/" + rel)
                continue
        write(dest, target)
        state[rel] = digest
        written += 1
    save_state(state)
    for message in errors:
        print("patch failed: " + message)
    print("src/ ready: %d file(s) written, %d kept with your changes, %d patch error(s)" % (written, kept, len(errors)))
    if kept:
        print("  'python isa.py setup --reset' throws your changes to game files away.")
    if errors:
        sys.exit(1)
    print("build: dotnet build src/Terraria.csproj -c Release")


def changed_files(manifest):
    vanilla = need_vanilla()
    source = {rel: path for rel, path in walk(SRC).items() if not is_own(rel)}
    replaced = set(manifest.get("replaced", []))
    for rel in sorted(set(vanilla) | set(source)):
        if rel not in vanilla:
            yield rel, "added", None, read(source[rel])
        elif rel not in source:
            yield rel, "removed", read(vanilla[rel]), None
        else:
            old, new = read(vanilla[rel]), read(source[rel])
            if old == new:
                yield rel, "same", old, new
            elif rel in replaced or not (is_text(old) and is_text(new)):
                yield rel, "replaced", old, new
            else:
                yield rel, "patched", old, new


def patch_fits(path, old, new, rel):
    if not os.path.exists(path):
        return False
    try:
        return apply_patch(old, read(path), rel) == new
    except ValueError:
        return False


def cmd_patches(args):
    manifest = load_manifest()
    lists = {"added": [], "removed": [], "replaced": []}
    wanted = set()
    state = load_state()
    wrote = 0
    for rel, kind, old, new in changed_files(manifest):
        if kind in lists:
            lists[kind].append(rel)
        if new is not None and kind in ("same", "patched"):
            state[rel] = sha1(new)
        if kind != "patched":
            continue
        path = patch_path(rel)
        wanted.add(os.path.normcase(path))
        if patch_fits(path, old, new, rel):
            continue
        patch = make_patch(rel, old, new)
        if apply_patch(old, patch, rel) != new:
            fail("patch for %s does not reproduce the file" % rel)
        write(path, patch)
        wrote += 1
        print("patch written: patches/%s.patch" % rel)
    dropped = 0
    for folder, _, names in os.walk(PATCHES):
        for name in names:
            path = os.path.join(folder, name)
            if name.endswith(".patch") and os.path.normcase(path) not in wanted:
                os.remove(path)
                dropped += 1
                print("patch removed: " + os.path.relpath(path, ROOT).replace(os.sep, "/"))
    for key, values in lists.items():
        if sorted(manifest.get(key, [])) != values:
            print("%s game files: %s" % (key, ", ".join(values) or "none"))
        manifest[key] = values
    write(MANIFEST, manifest_bytes(manifest))
    write(os.path.join(ROOT, ".gitignore"), gitignore_text(manifest).encode())
    save_state(state)
    print("%d patch(es) written, %d removed" % (wrote, dropped))


def cmd_status(args):
    manifest = load_manifest()
    count = 0
    for rel, kind, old, new in changed_files(manifest):
        has_patch = os.path.exists(patch_path(rel))
        if kind == "same":
            if has_patch:
                print("back to the game's version: " + rel)
                count += 1
        elif kind == "patched":
            if not patch_fits(patch_path(rel), old, new, rel):
                print(("changed: " if has_patch else "new patch: ") + rel)
                count += 1
        elif rel not in manifest.get(kind, []):
            print("%s: %s" % (kind, rel))
            count += 1
    print("%d game file(s) differ from patches/" % count if count else "game files match patches/")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    setup = sub.add_parser("setup")
    setup.add_argument("--terraria", help="Terraria folder or Terraria.exe (found through Steam when omitted)")
    setup.add_argument("--force", action="store_true", help="accept a Terraria.exe with another hash")
    setup.add_argument("--reset", action="store_true", help="overwrite game files you changed")
    setup.set_defaults(run=cmd_setup)
    sub.add_parser("patches").set_defaults(run=cmd_patches)
    sub.add_parser("status").set_defaults(run=cmd_status)
    args = parser.parse_args()
    args.run(args)


if __name__ == "__main__":
    main()
