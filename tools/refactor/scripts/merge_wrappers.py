"""Merges "XCommand(CommandContext command) => Helper(command.Parts)" wrappers into the helper itself.

The helper takes the CommandContext and gets the wrapper's name; its old parameters become locals read from the
command, so its body does not change. The wrapper is deleted.
"""
import os
import re

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src")
PAIRS = [
    ("IsaClient.Building/PixelArt.cs", "PixelPlayerCommand", "CommandPlayerArt"),
    ("IsaClient.Building/PixelArt.cs", "PixelArtCommand", "CommandPixelArtOptions"),
    ("IsaClient.Community/ChatTools.cs", "SpamChatCommand", "CommandSpamChat"),
    ("IsaClient.Gameplay/AutoClicker.cs", "AutoClickerCommand", "HandleCommand"),
    ("IsaClient.Gameplay/Combat.cs", "SummonMobCommand", "SpawnMobCommand"),
    ("IsaClient.Gameplay/ItemTools.cs", "BuffCommand", "CommandBuff"),
    ("IsaClient.Gameplay/ItemTools.cs", "ItemCommand", "CommandItem"),
    ("IsaClient.Gameplay/ItemTools.cs", "HealCommand", "CommandHeal"),
    ("IsaClient.Gameplay/ItemTools.cs", "WearCommand", "CommandWear"),
    ("IsaClient.Gameplay/NpcControl.cs", "ControlNpcChatCommand", "ControlNpcCommand"),
    ("IsaClient.Gameplay/RainbowText.cs", "RainbowCommand", "CommandRainbow"),
    ("IsaClient.Gameplay/SelfCommands.cs", "KillSelfCommand", "KillMyself"),
    ("IsaClient.Gameplay/SelfCommands.cs", "CharacterDifficultyCommand", "PlayerDifficultyCommand"),
    ("IsaClient.Rendering/Zoom.cs", "ZoomCommand", "Command"),
    ("IsaClient.WorldTools/Chests.cs", "FillChestChatCommand", "FillChestCommand"),
    ("IsaClient.WorldTools/WorldInfo.cs", "WorldDifficultyChatCommand", "WorldDifficultyCommand"),
]
FINAL_NAMES = {"ControlNpcChatCommand": "ControlNpcCommand", "FillChestChatCommand": "FillChestCommand", "WorldDifficultyChatCommand": "WorldDifficultyCommand"}
SOURCE = {"string[]": "command.Parts", "string": "command.Text"}


def process(rel, wrapper, helper, catalog):
    path = os.path.join(SRC, rel)
    text = open(path, encoding="utf-8-sig").read().replace("\r\n", "\n")
    w = re.search(r"((?:\t///[^\n]*\n)*)\tinternal static void " + wrapper + r"\(CommandContext command\)\n\t\{\n\t\t(?:\w+\.)?" + helper + r"\(([^;]*)\);\n\t\}\n\n?", text)
    if not w:
        raise SystemExit(f"wrapper {wrapper} not found in {rel}")
    call_args = [a.strip() for a in w.group(2).split(",")] if w.group(2).strip() else []
    text = text[:w.start()] + text[w.end():]
    h = re.search(r"((?:\t///[^\n]*\n)*)\t(public|private|internal) static (?:async )?void " + helper + r"\(([^)]*)\)\n\t\{\n", text)
    if not h:
        raise SystemExit(f"helper {helper} not found in {rel}")
    params = [p.strip() for p in h.group(3).split(",")] if h.group(3).strip() else []
    if len(params) != len(call_args):
        raise SystemExit(f"{helper}: {len(params)} params, call passes {len(call_args)}")
    locals_ = []
    for param, arg in zip(params, call_args):
        ptype, pname = param.rsplit(" ", 1)
        locals_.append(f"\t\t{ptype} {pname} = {arg};\n")
    doc = h.group(1) or w.group(1)
    name = FINAL_NAMES.get(wrapper, wrapper)
    async_kw = "async " if "async void " + helper in h.group(0) else ""
    header = f"{doc}\tinternal static {async_kw}void {name}(CommandContext command)\n\t{{\n" + "".join(locals_)
    text = text[:h.start()] + header + text[h.end():]
    open(path, "wb").write(b"\xef\xbb\xbf" + text.replace("\n", "\r\n").encode("utf-8"))
    if name != wrapper:
        catalog = catalog.replace("." + wrapper + ",", "." + name + ",")
    return catalog


catalog_path = os.path.join(SRC, "IsaClient.Core", "CommandCatalog.cs")
catalog = open(catalog_path, encoding="utf-8-sig").read()
for rel, wrapper, helper in PAIRS:
    catalog = process(rel, wrapper, helper, catalog)
open(catalog_path, "wb").write(b"\xef\xbb\xbf" + catalog.replace("\r\n", "\n").replace("\n", "\r\n").encode("utf-8"))
print(f"merged {len(PAIRS)} wrappers")
