"""Which category lists contain each row name (CheatPanel.AssembleBindPanels + the category classes)."""
import glob
import os
import re
import sys

MENU = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src", "IsaClient.Menu")
LIST = re.compile(r"(?:List<string> (\w+) = new List<string>|List<string> (\w+) = new List<string>\(\w+\)|(\w+)\.AddRange\(new string\[\])\s*\{?\s*\n?\s*\{?(.*?)\}", re.S)


def lists():
    found = {}
    for path in glob.glob(os.path.join(MENU, "*.cs")):
        text = open(path, encoding="utf-8-sig").read()
        for m in re.finditer(r"(\w+) = new List<string>\s*\{(.*?)\};", text, re.S):
            found.setdefault(os.path.basename(path) + ":" + m.group(1), []).extend(re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(2)))
        for m in re.finditer(r"(\w+)\.AddRange\(new string\[\] \{(.*?)\}\);", text, re.S):
            found.setdefault(os.path.basename(path) + ":" + m.group(1), []).extend(re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(2)))
    return found


if __name__ == "__main__":
    all_lists = lists()
    for name in sys.argv[1:]:
        print(name, "->", [k for k, v in all_lists.items() if name in v])
