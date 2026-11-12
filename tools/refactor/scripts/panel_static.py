"""Phase 4 step 1: CheatPanel members that never use the panel instance become static (it is a single instance)."""
import glob
import os
import re

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src", "IsaClient.Menu")
METHODS = """MakeToggle MakeButton MakeSlider MakeCycle MakeFlag MakeAction MakeInfo MakeState UpdateClickedButton
UpdateClickedButton_Cycle HeldInfo HeldSlider HeldToggle Header SmallButton Offset ConvertWorldButton CreatePanel
CreatePanel_TravelHacks CreatePanel_RenderHacks CreatePanel_Player CreatePanel_AI CreatePanel_Music CreatePanel_MiscHacks
CreatePanel_InfoHacks CreatePanel_WorldEdit CreatePanel_HeldItem CreatePanel_ItemModPresets CreateExtraCheatElement
CreatePanel_BiomeSpoofer CreatePanel_WorldInfo WorldInfo_Bosses WorldInfo_Invasions WorldInfo_Utilities WorldInfo_Data
WorldInfo_General WorldInfo_Seeds""".split()
FIELDS = """_cancelFillOrDelActionButton _copySelectionButton _deleteSelectedLiquidButton _deleteSelectedTilesButton
_deleteSelectedWallsButton _deleteSelectionButton _eraseBrushSize _erasePacketRate _fillSelectionButton _liquidBrushSize
_liquidPacketRate _packetRate _paintPacketRate _remFromSelectionButton _superPaintBrushSize _tileBrushSize _wallBrushSize
_worldBorderSize _killAuraSize""".split()

done = set()
for path in glob.glob(os.path.join(SRC, "CheatPanel*.cs")):
    raw = open(path, "rb").read()
    text = raw.decode("utf-8-sig")
    for name in METHODS:
        pattern = re.compile(r"^(\t(?:private|public|internal|protected)) (?!static)((?:[\w<>\[\],.]+ )+)" + re.escape(name) + r"\(", re.M)
        text, n = pattern.subn(r"\1 static \2" + name + "(", text)
        if n:
            done.add(name)
    for name in FIELDS:
        pattern = re.compile(r"^(\t(?:private|public|internal|protected)) (?!static)([\w<>\[\],.]+) " + re.escape(name) + r"\b", re.M)
        text, n = pattern.subn(r"\1 static \2 " + name, text)
        if n:
            done.add(name)
    open(path, "wb").write(b"\xef\xbb\xbf" + text.encode("utf-8"))
missing = [n for n in METHODS + FIELDS if n not in done]
print(f"static: {len(done)}; not found (already static or absent): {missing}")
