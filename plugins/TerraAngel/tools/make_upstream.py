"""Builds plugins/TerraAngel/ForkSupport (what the client needs to build a TerraAngel fork, see IsaClient's ForkBuilder) from a TerraAngel checkout whose src/ was generated (TerraAngelSetup):
  index.txt  "relative/path.cs<TAB>sha1" for every .cs of src/TerraAngel/Terraria (the game files and the TerraAngel
             folder), hashed after Normalize(); the client compares a fork's files with it to find what was changed.
  originals/ the original TerraAngel files the pack (Source/) changed while porting: the base of the 3-way merge of a
             fork's changes into the pack's version.
Usage: make_upstream.py <TerraAngel checkout>"""
import hashlib, os, shutil, subprocess, sys

def normalize(raw):
    text = raw.decode('utf-8', errors='replace')
    if text.startswith('﻿'):
        text = text[1:]
    lines = text.replace('\r\n', '\n').replace('\r', '\n').split('\n')
    return '\n'.join(l.rstrip() for l in lines).rstrip('\n')

def sha(raw):
    return hashlib.sha1(normalize(raw).encode('utf-8')).hexdigest()

checkout = sys.argv[1]
root = os.path.join(checkout, 'src', 'TerraAngel', 'Terraria')
here = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
pack = os.path.join(here, 'Source')
out = os.path.join(here, 'ForkSupport')
for old in ('index.txt', 'originals'):
    target = os.path.join(out, old)
    if os.path.isdir(target):
        shutil.rmtree(target)
    elif os.path.exists(target):
        os.remove(target)
os.makedirs(os.path.join(out, 'originals'))
commit = subprocess.run(['git', '-C', checkout, 'rev-parse', 'HEAD'], capture_output=True, text=True).stdout.strip()
rows, copied = [], 0
for dirpath, dirs, files in os.walk(root):
    dirs[:] = [d for d in dirs if d not in ('obj', 'bin')]
    for name in files:
        if not name.endswith('.cs'):
            continue
        full = os.path.join(dirpath, name)
        rel = os.path.relpath(full, root).replace(os.sep, '/')
        raw = open(full, 'rb').read()
        rows.append(rel + '\t' + sha(raw))
        if rel.startswith('TerraAngel/'):
            ported = os.path.join(pack, rel[len('TerraAngel/'):])
            if os.path.exists(ported) and sha(open(ported, 'rb').read()) != sha(raw):
                target = os.path.join(out, 'originals', rel)
                os.makedirs(os.path.dirname(target), exist_ok=True)
                shutil.copyfile(full, target)
                copied += 1
rows.sort()
with open(os.path.join(out, 'index.txt'), 'w', encoding='utf-8', newline='\n') as f:
    f.write('# TerraAngel ' + commit + '\n')
    f.write('\n'.join(rows) + '\n')
print(len(rows), 'files indexed,', copied, 'originals kept, commit', commit)
