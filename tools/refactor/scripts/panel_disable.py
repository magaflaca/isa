"""Phase 4: every cheat category gets its own DisableAll(); the central switch in CheatPanel goes away."""
import os
import re

MENU = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src", "IsaClient.Menu")


def load(name):
    return open(os.path.join(MENU, name), encoding="utf-8-sig").read().replace("\r\n", "\n")


def save(name, text):
    open(os.path.join(MENU, name), "wb").write(b"\xef\xbb\xbf" + text.replace("\n", "\r\n").encode("utf-8"))


def add_method(text, method):
    """Before the closing brace of the (only) top-level class."""
    index = text.rstrip().rfind("\n}")
    return text[:index] + "\n\n" + method.rstrip("\n") + text[index:]


renamed = {
    "TravelMenu.cs": ("DisableTravelHacks", 1),
    "InfoMenu.cs": ("DisableInfoHacks", 2),
    "MiscMenu.cs": ("DisableMiscHacks", 5),
    "WorldEditMenu.cs": ("DisableWorldEditHacks", 6),
}
for file, (old, number) in renamed.items():
    text = load(file)
    text = re.sub(r"\b(private|internal) static void " + old + r"\(\)", "/// <summary>The category's \"Disable All\" button.</summary>\n\tinternal static void DisableAll()", text)
    text = text.replace(f"CheatPanel.UpdateClickedButton_DisableAll({number});", "DisableAll();")
    text = re.sub(r"\b" + old + r"\(\)", "DisableAll()", text)
    save(file, text)

added = {
    "RenderMenu.cs": (3, """\tFullbrightLighting.Set(0f);
\tInputState.NativeCursor = false;
\tChatTools.KeepChat = false;
\tWorldView.DisableAll();"""),
    "PlayerMenu.cs": (8, """\tPlayerCheats.DisablePlayerCheats();
\tPlayerCheats.GodMode = false;
\tPlayerCheats.JourneyMenu = false;
\tPlayerTools.AnyItemAnySlot = false;
\tAutoClicker.Enabled = false;
\tif (Main.LocalPlayer != null && Main.LocalPlayer.ghost)
\t{
\t\tPlayerTools.ToggleGhost();
\t}"""),
    "AiMenu.cs": (9, """\tAiAssistant.Enabled = false;
\tAiAssistant.ReplyToEveryone = false;
\tAiAssistant.Save();"""),
    "MusicMenu.cs": (7, """\tif (MusicPlayer.Enabled)
\t{
\t\tMusicPlayer.SetEnabled(enabled: false);
\t}
\telse
\t{
\t\tMusicPlayer.Stop(clearQueue: true);
\t}
\tMusicPlayer.AutoplayEnabled = false;
\tMusicPlayer.Save();"""),
}
for file, (number, body) in added.items():
    text = load(file)
    text = text.replace(f"CheatPanel.UpdateClickedButton_DisableAll({number});", "DisableAll();")
    indented = "\n".join("\t" + line for line in body.split("\n"))
    text = add_method(text, "\t/// <summary>The category's \"Disable All\" button.</summary>\n\tinternal static void DisableAll()\n\t{\n" + indented + "\n\t}\n")
    save(file, text)

panel = load("CheatPanel.cs")
match = re.search(r"\tprivate static void UpdateClickedButton_DisableAll\(int category\)\n\t\{\n.*?\n\t\}\n\n", panel, re.S)
panel = panel[:match.start()] + panel[match.end():]
save("CheatPanel.cs", panel)
print("DisableAll per category done")
