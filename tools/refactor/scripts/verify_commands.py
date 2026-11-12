"""Checks that the command catalog registers exactly the command names the old ChatCommands switches handled.

    python scripts/verify_commands.py <old ChatCommands.cs> <new CommandCatalog.cs>
"""
import re
import sys

old = open(sys.argv[1], encoding="utf-8-sig").read()
new = open(sys.argv[2], encoding="utf-8-sig").read()

old_names = set(re.findall(r'case "([^"]+)":', old))
old_names |= set(re.findall(r'text == "([^"]+)"', old))
old_names |= {"abbrev", "abbreviation", "alias"}
new_names = set()
for line in new.splitlines():
    if "CommandRegistry.Register(" in line:
        new_names |= set(re.findall(r'"([^"]+)"', line))

print(f"old: {len(old_names)} names, new: {len(new_names)} names")
missing = sorted(old_names - new_names)
extra = sorted(new_names - old_names)
print("missing in catalog:", missing)
print("new in catalog:", extra)
sys.exit(1 if missing else 0)
