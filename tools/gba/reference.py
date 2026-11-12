"""Reference ROMs from terraria-to-pokemon without helmets (what IsaClient's GBA player does), to diff against the C#
port: python tools/gba/reference.py <terraria-to-pokemon dir> <rom.gba> <out dir> <player.plr>..."""
import os
import sys

T2P = os.path.abspath(sys.argv[1])
sys.path.insert(0, T2P)
from t2p import __main__ as cli, assets  # noqa: E402


def no_helmet(look):
    hair = assets.frame0(f"Player_Hair_{look.hair + 1}")
    return hair, None, 0, hair


assets.head_layers = no_helmet
cli.assets.head_layers = no_helmet
rom, out = sys.argv[2], sys.argv[3]
os.makedirs(out, exist_ok=True)
for plr in sys.argv[4:]:
    name = os.path.splitext(os.path.basename(plr))[0]
    print(cli.convert(plr, rom, os.path.join(out, name + ".gba"), preview=os.path.join(out, name + "_preview.png")))
