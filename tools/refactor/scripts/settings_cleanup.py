"""Phase 3: per-module Load/Save methods replaced by [Setting] fields."""
import os
import re

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src")


def read(rel):
    raw = open(os.path.join(SRC, rel), "rb").read()
    return raw.decode("utf-8-sig"), raw.startswith(b"\xef\xbb\xbf")


def write(rel, text, bom):
    open(os.path.join(SRC, rel), "wb").write((b"\xef\xbb\xbf" if bom else b"") + text.encode("utf-8"))


def remove_method(text, signature_regex):
    """Removes a method (with its doc comment) whose declaration line matches the regex."""
    nl = "\r\n" if "\r\n" in text else "\n"
    pattern = re.compile(r"((?:^\t///[^\n]*\n)*)^\t[^\n]*" + signature_regex + r"[^\n]*\n\t\{\n.*?^\t\}\n(?:\n)?", re.M | re.S)
    plain = text.replace("\r\n", "\n")
    new, count = pattern.subn("", plain, count=1)
    if count != 1:
        raise SystemExit("method not found: " + signature_regex)
    return new.replace("\n", nl)


def edit(rel, func):
    text, bom = read(rel)
    new = func(text)
    if new == text:
        raise SystemExit("nothing changed in " + rel)
    write(rel, new, bom)


edit("IsaClient.Rendering/Zoom.cs", lambda t: remove_method(remove_method(t, r"static void Load\(Preferences cfg\)"), r"static void Save\(Preferences cfg\)"))
edit("IsaClient.Networking/ProtocolCompat.cs", lambda t: remove_method(remove_method(t, r"static void Load\(Preferences cfg\)"), r"static void Save\(Preferences cfg\)"))
edit("IsaClient.Gameplay/BuffTools.cs", lambda t: remove_method(remove_method(t, r"static void Load\(Preferences cfg\)"), r"static void Save\(Preferences cfg\)"))
edit("IsaClient.Gameplay/PlayerCheats.cs", lambda t: remove_method(remove_method(t, r"static void LoadPlayerCheats\(Preferences cfg\)"), r"static void SavePlayerCheats\(Preferences cfg\)"))


def fullbright(text):
    text = remove_method(text, r"static void Save\(Preferences cfg\)")
    nl = "\r\n" if "\r\n" in text else "\n"
    old = re.search(r"\tpublic static void Load\(Preferences cfg\)\r?\n\t\{\r?\n\t\tcfg\.Get\(\"GlobalBrightness\", ref Percent\);\r?\n\t\tPercent = Utils\.Clamp\(Percent, 0f, 1f\);\r?\n", text)
    if not old:
        raise SystemExit("Fullbright Load not found")
    text = text[:old.start()] + "\t/// <summary>After loading: the brightness used when Fullbright is turned back on.</summary>" + nl + "\tinternal static void OnSettingsLoaded()" + nl + "\t{" + nl + text[old.end():]
    return text


edit("IsaClient.Rendering/FullbrightLighting.cs", fullbright)
edit("IsaClient.Gameplay/AutoClicker.cs", lambda t: t.replace("\tpublic static AutoClickMode Mode = AutoClickMode.Click;", "\t[Setting(\"AutoClickMode\")]\r\n\tpublic static AutoClickMode Mode = AutoClickMode.Click;" if "\r\n" in t else "\t[Setting(\"AutoClickMode\")]\n\tpublic static AutoClickMode Mode = AutoClickMode.Click;")
     .replace("\tpublic static AutoClickButton Button = AutoClickButton.Left;", "\t[Setting(\"AutoClickButton\")]\r\n\tpublic static AutoClickButton Button = AutoClickButton.Left;" if "\r\n" in t else "\t[Setting(\"AutoClickButton\")]\n\tpublic static AutoClickButton Button = AutoClickButton.Left;"))
for rel in ["IsaClient.Core/ClientLifecycle.cs", "IsaClient.Core/GeneralCommands.cs"]:
    edit(rel, lambda t: t.replace("ClientSettings.Load(NoHacks: true);", "ClientSettings.Load(noCheats: true);"))
print("cleanup done")
