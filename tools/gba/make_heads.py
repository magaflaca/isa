"""Bake the Pokemon heads of every Terraria hairstyle for IsaClient's GBA player (IsaClient.Gba.Trainer).

The 3D head of terraria-to-pokemon (t2p/head3d.py, numpy + scipy) only depends on the hairstyle when the character
wears no helmet, so it is rendered here once per hairstyle and painted with *symbolic* colours: the client swaps the
symbols for the character's skin/hair/eye ramps (t2p/paint.py Look) at run time.

    python tools/gba/make_heads.py <terraria-to-pokemon dir> src/IsaClient.Assets/PokemonHeads.bin [first last]

Output (deflate): "PKH1", u16 hair count, then per hairstyle: u8 beard level, then for each variant
(male, female; female only when beard > 0 differs) 5 images: down, up, left (32x44 map heads), front (96x96, trainer
card), back (96x96, battle back). Image: u8 x, y, w, h crop inside its canvas, then w*h bytes:
low 4 bits symbol (0 empty, 1-4 skin tone 0-3, 5-8 hair tone 0-3, 9 eye, 10 outline, 11 hair outline),
bit 0x80 = depth < 3 (hair behind the body in front views).
"""
import os
import struct
import sys
import time
import zlib

import numpy as np

T2P = os.path.abspath(sys.argv[1])
sys.path.insert(0, T2P)
from t2p import assets, compose, head3d, paint, pics  # noqa: E402

SKIN = [(1, i, 1) for i in range(4)]
HAIR = [(2, i, 1) for i in range(4)]
EYE = (3, 0, 1)
HAIR_OUTLINE = (4, 0, 1)
SYMBOL = {c: 1 + i for i, c in enumerate(SKIN)}
SYMBOL.update({c: 5 + i for i, c in enumerate(HAIR)})
SYMBOL[EYE] = 9
SYMBOL[tuple(paint.OUTLINE)] = 10
SYMBOL[HAIR_OUTLINE] = 11


class SymbolLook:
    def __init__(self, male):
        self.male = male
        self.skin = tuple(SKIN)
        self.hair = tuple(HAIR)
        self.eye = EYE
        self.hair_outline = HAIR_OUTLINE


def encode(img, dep):
    a = img[..., 3] > 0
    h, w = a.shape
    if not a.any():
        return bytes([0, 0, 0, 0])
    ys, xs = np.nonzero(a)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    out = bytearray([x0, y0, x1 - x0, y1 - y0])
    for y in range(y0, y1):
        for x in range(x0, x1):
            if not a[y, x]:
                out.append(0)
                continue
            s = SYMBOL[tuple(int(v) for v in img[y, x, :3])]
            if dep is not None and dep[y, x] < 3:
                s |= 0x80
            out.append(s)
    return bytes(out)


def bake(hair_index):
    rgba = assets.frame0(f"Player_Hair_{hair_index + 1}")
    model = head3d.HeadModel(rgba, None, rgba)
    beard = model.style.beard
    # renders are the same for both genders: only the painting (beards) differs
    p = compose.fit_protrusion(model)
    model.style.n = 9
    w, h, cx, cy = compose.CANVAS
    maps = {v: head3d.render(model, yaw, compose.SCALE, compose.CANVAS, p) for v, yaw in compose.VIEWS.items()}
    front_spec = pics.SPECS[("front", "red")]
    front = head3d.render(model, 0, front_spec["scale"], (96, 96, 48.0, 48.0), p)
    model.style.n = 16
    back_spec = pics.SPECS[("back", "red")]
    back = head3d.render(model, back_spec["yaw"], back_spec["scale"], (96, 96, 48.0, 48.0), 1.0, ss=3)
    variants = [True, False] if beard else [True]
    out = bytearray([beard])
    for male in variants:
        look = SymbolLook(male)
        b = beard if male else 0
        for view in ("down", "up", "left"):
            img, _ = paint.paint(maps[view], view, look, (cx, cy), beard=b)
            out += encode(img, maps[view].depth)
        img, _ = paint.paint(front, "down", look, (48.0, 48.0), beard=b)
        out += encode(img, front.depth)
        out += encode(pics.paint_big(back, look), None)
    return bytes(out)


def main():
    dst = sys.argv[2]
    first = int(sys.argv[3]) if len(sys.argv) > 3 else 0
    last = int(sys.argv[4]) if len(sys.argv) > 4 else 228
    parts = []
    t0 = time.time()
    for i in range(first, last):
        parts.append(bake(i))
        print(f"hair {i + 1}: {len(parts[-1])} bytes, {time.time() - t0:.0f}s", flush=True)
    raw = b"PKH1" + struct.pack("<H", len(parts)) + b"".join(parts)
    open(dst, "wb").write(zlib.compress(raw, 9))
    print("raw", len(raw), "->", os.path.getsize(dst))


if __name__ == "__main__":
    main()
