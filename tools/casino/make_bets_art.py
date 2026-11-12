"""Art for the casino's Bets page (roulette, slot machine, horse track): writes src/IsaClient.Assets/CasinoBets.png.

Fruit, bells, gems and horses in the game are Terraria's own items and mounts; this makes what Terraria doesn't have.
python tools/casino/make_bets_art.py [preview.png]

Atlas (pixels; BetsArt.cs reads this layout):
  wheel rotor 320x320 at 0,0 (numbers and pockets, turns);  wheel bowl 320x320 at 322,0 (wood rim, stays)
  cabinet 190x236 at 0,322 (drawn at 2x; reel window 26,84 138x102)
  seven 32x32 at 192,322;  ball 10x10 at 226,322;  turret 34x34 at 238,322
  stands tile 256x96 at 280,322 (loops);  hills tile 256x64 at 280,420 (loops)
  rail tile 32x20 at 192,358;  finish post 16x72 at 226,358;  flag 24x16 at 244,358;  gate 20x72 at 192,380
"""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "src", "IsaClient.Assets", "CasinoBets.png")
ORDER = [0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26]
RED = {1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36}
GOLD = (255, 208, 96, 255)
GOLD_DARK = (150, 100, 34, 255)
INK = (16, 14, 26, 255)
SS = 4


def font(size):
    for name in ("arialbd.ttf", "DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default()


def rotor():
    n = 320 * SS
    c = n / 2
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r_out, r_pocket, r_in = 158 * SS, 112 * SS, 92 * SS
    step = 360 / 37
    for i, num in enumerate(ORDER):
        a0 = -90 - step / 2 + i * step
        color = (34, 150, 84, 255) if num == 0 else ((178, 30, 44, 255) if num in RED else (24, 24, 34, 255))
        dark = (20, 96, 54, 255) if num == 0 else ((112, 16, 28, 255) if num in RED else (12, 12, 20, 255))
        d.pieslice((c - r_out, c - r_out, c + r_out, c + r_out), a0, a0 + step, fill=color)
        d.pieslice((c - r_pocket, c - r_pocket, c + r_pocket, c + r_pocket), a0, a0 + step, fill=dark)
    for i in range(37):
        a = math.radians(-90 - step / 2 + i * step)
        d.line((c + math.cos(a) * r_in, c + math.sin(a) * r_in, c + math.cos(a) * r_out, c + math.sin(a) * r_out), fill=GOLD, width=SS)
    d.ellipse((c - r_out, c - r_out, c + r_out, c + r_out), outline=GOLD, width=2 * SS)
    d.ellipse((c - r_pocket, c - r_pocket, c + r_pocket, c + r_pocket), outline=GOLD, width=SS)
    # the cone
    for k in range(40):
        t = k / 39
        r = r_in * (1 - t)
        col = tuple(int(a + (b - a) * t) for a, b in zip((76, 42, 24), (168, 104, 52))) + (255,)
        d.ellipse((c - r, c - r, c + r, c + r), fill=col)
    d.ellipse((c - r_in, c - r_in, c + r_in, c + r_in), outline=GOLD, width=2 * SS)
    # four gold spokes
    for k in range(4):
        a = math.radians(k * 90 + 45)
        p = (math.cos(a), math.sin(a))
        q = (-p[1], p[0])
        w = 7 * SS
        pts = [(c + q[0] * w, c + q[1] * w), (c + p[0] * r_in * 0.92, c + p[1] * r_in * 0.92), (c - q[0] * w, c - q[1] * w)]
        d.polygon(pts, fill=(214, 164, 70, 255))
    img = img.resize((320, 320), Image.LANCZOS)
    # numbers, each turned to face out
    for i, num in enumerate(ORDER):
        text = str(num)
        tile = Image.new("RGBA", (40 * SS, 30 * SS), (0, 0, 0, 0))
        td = ImageDraw.Draw(tile)
        f = font(15 * SS)
        box = td.textbbox((0, 0), text, font=f)
        td.text(((40 * SS - box[2] + box[0]) / 2 - box[0], (30 * SS - box[3] + box[1]) / 2 - box[1]), text, font=f, fill=(255, 250, 236, 255))
        angle = i * step
        tile = tile.rotate(-angle, resample=Image.BICUBIC, expand=True)
        tile = tile.resize((tile.width // SS, tile.height // SS), Image.LANCZOS)
        a = math.radians(-90 + angle)
        rr = 136
        img.alpha_composite(tile, (int(160 + math.cos(a) * rr - tile.width / 2), int(160 + math.sin(a) * rr - tile.height / 2)))
    return img


def bowl():
    n = 320 * SS
    c = n / 2
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse((0, 0, n - 1, n - 1), fill=(46, 24, 14, 255))
    d.ellipse((4 * SS, 4 * SS, n - 4 * SS, n - 4 * SS), fill=(126, 74, 38, 255))
    d.ellipse((10 * SS, 10 * SS, n - 10 * SS, n - 10 * SS), fill=(88, 48, 26, 255))
    d.ellipse((16 * SS, 16 * SS, n - 16 * SS, n - 16 * SS), fill=(26, 16, 14, 255))
    d.ellipse((6 * SS, 6 * SS, n - 6 * SS, n - 6 * SS), outline=GOLD_DARK, width=SS)
    for k in range(8):
        a = math.radians(k * 45 + 22.5)
        r = 152 * SS
        x, y = c + math.cos(a) * r, c + math.sin(a) * r
        s = 3 * SS
        d.polygon([(x - s, y), (x, y - s), (x + s, y), (x, y + s)], fill=GOLD)
    return img.resize((320, 320), Image.LANCZOS)


def turret():
    n = 34 * SS
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = n / 2
    for k in range(4):
        a = math.radians(k * 90)
        p = (math.cos(a), math.sin(a))
        q = (-p[1], p[0])
        pts = [(c + q[0] * 3 * SS, c + q[1] * 3 * SS), (c + p[0] * 16 * SS, c + p[1] * 16 * SS), (c - q[0] * 3 * SS, c - q[1] * 3 * SS)]
        d.polygon(pts, fill=(232, 186, 88, 255))
    d.ellipse((c - 8 * SS, c - 8 * SS, c + 8 * SS, c + 8 * SS), fill=(150, 100, 34, 255))
    d.ellipse((c - 6 * SS, c - 6 * SS, c + 6 * SS, c + 6 * SS), fill=(255, 224, 140, 255))
    d.ellipse((c - 2 * SS, c - 3 * SS, c + 1 * SS, c), fill=(255, 250, 230, 255))
    return img.resize((34, 34), Image.LANCZOS)


def ball():
    img = Image.new("RGBA", (10, 10), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse((0, 0, 9, 9), fill=(60, 60, 72, 255))
    d.ellipse((1, 1, 8, 8), fill=(236, 236, 244, 255))
    d.point([(3, 3), (3, 2), (2, 3)], fill=(255, 255, 255, 255))
    d.point([(7, 7), (6, 7), (7, 6)], fill=(176, 176, 196, 255))
    return img


FONT = {
    "L": "10000 10000 10000 10000 10000 10000 11111", "M": "10001 11011 10101 10101 10001 10001 10001",
    "N": "10001 11001 10101 10011 10001 10001 10001", "O": "01110 10001 10001 10001 10001 10001 01110",
    "S": "01111 10000 10000 01110 00001 00001 11110", "T": "11111 00100 00100 00100 00100 00100 00100",
    " ": "000 000 000 000 000 000 000",
}


def pixel_text(img, text, x, y, color, k=2):
    for ch in text:
        rows = FONT[ch].split()
        for j, row in enumerate(rows):
            for i, bit in enumerate(row):
                if bit == "1":
                    for yy in range(k):
                        for xx in range(k):
                            img.putpixel((x + i * k + xx, y + j * k + yy), color)
        x += (len(rows[0]) + 1) * k
    return x


def text_width(text, k=2):
    return sum((len(FONT[c].split()[0]) + 1) * k for c in text) - k


def cabinet():
    w, h = 190, 236
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    body, body_hi, body_lo = (70, 26, 104, 255), (116, 54, 160, 255), (40, 14, 66, 255)
    # crown with a moon
    d.rounded_rectangle((6, 6, w - 7, 74), radius=14, fill=INK)
    d.rounded_rectangle((8, 8, w - 9, 72), radius=12, fill=body)
    d.rounded_rectangle((8, 8, w - 9, 16), radius=6, fill=body_hi)
    d.rounded_rectangle((14, 14, w - 15, 64), radius=6, fill=(20, 12, 40, 255), outline=GOLD)
    # body
    d.rectangle((10, 70, w - 11, 212), fill=INK)
    d.rectangle((12, 70, w - 13, 210), fill=body)
    d.rectangle((12, 70, 18, 210), fill=body_hi)
    d.rectangle((w - 19, 70, w - 13, 210), fill=body_lo)
    # reel frame
    d.rounded_rectangle((20, 78, w - 21, 192), radius=4, fill=(120, 84, 26, 255))
    d.rounded_rectangle((22, 80, w - 23, 190), radius=3, fill=GOLD)
    d.rounded_rectangle((24, 82, w - 25, 188), radius=2, fill=(92, 56, 18, 255))
    d.rectangle((26, 84, w - 27, 186), fill=(250, 246, 234, 255))
    # coin tray and base
    d.rectangle((6, 208, w - 7, 233), fill=INK)
    d.rectangle((8, 210, w - 9, 231), fill=(96, 40, 134, 255))
    d.rectangle((8, 210, w - 9, 213), fill=body_hi)
    d.rectangle((30, 216, w - 31, 228), fill=(26, 16, 40, 255))
    d.rectangle((30, 216, w - 31, 218), fill=(10, 6, 18, 255))
    d.rectangle((32, 224, w - 33, 226), fill=GOLD_DARK)
    # sign text
    t = "MOON SLOTS"
    x = (w - text_width(t, 2)) // 2
    pixel_text(img, t, x + 1, 42, (120, 60, 20, 255), 2)
    pixel_text(img, t, x, 41, GOLD, 2)
    # a little moon and stars over the words
    d.ellipse((w // 2 - 9, 19, w // 2 + 9, 37), fill=(255, 236, 180, 255))
    d.ellipse((w // 2 - 4, 18, w // 2 + 12, 34), fill=(20, 12, 40, 255))
    for sx, sy in ((36, 26), (58, 22), (130, 23), (152, 28)):
        d.line((sx - 2, sy, sx + 2, sy), fill=(255, 230, 170, 255))
        d.line((sx, sy - 2, sx, sy + 2), fill=(255, 230, 170, 255))
    return img


SEVEN = [
    "######################",
    "######################",
    "######################",
    "######################",
    "####............######",
    "####...........######.",
    "###...........######..",
    ".............######...",
    "............######....",
    "...........######.....",
    "..........######......",
    ".........#######......",
    "........#######.......",
    "........#######.......",
    ".......#######........",
    ".......#######........",
    ".......#######........",
    "......#######.........",
    "......#######.........",
    "......#######.........",
    "......#######.........",
    "......#######.........",
]


def seven():
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    outline = (70, 8, 16, 255)
    ox, oy = 5, 5
    pts = {(x, y) for y, r in enumerate(SEVEN) for x, ch in enumerate(r) if ch == "#"}
    for (x, y) in pts:
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                img.putpixel((ox + x + dx, oy + y + dy), GOLD)
    for (x, y) in pts:
        t = y / 22
        img.putpixel((ox + x, oy + y), (int(255 - 40 * t), int(70 - 50 * t), int(70 - 40 * t), 255))
    for (x, y) in pts:
        if (x, y - 1) not in pts:
            img.putpixel((ox + x, oy + y), (255, 170, 150, 255))
    base = img.copy()
    for y in range(32):
        for x in range(32):
            if base.getpixel((x, y))[3] == 0:
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    if 0 <= x + dx < 32 and 0 <= y + dy < 32 and base.getpixel((x + dx, y + dy))[3]:
                        img.putpixel((x, y), outline)
                        break
    return img


def stands():
    w, h = 256, 96
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # the grandstand: a striped roof, rows of people
    d.rectangle((0, 0, w, 12), fill=(150, 34, 46, 255))
    for x in range(0, w, 32):
        d.rectangle((x, 0, x + 15, 12), fill=(240, 232, 214, 255))
    d.rectangle((0, 12, w, 14), fill=(60, 30, 26, 255))
    d.rectangle((0, 14, w, h), fill=(54, 40, 52, 255))
    for x in range(0, w, 64):
        d.rectangle((x + 30, 14, x + 33, h), fill=(34, 24, 30, 255))
    colors = [(226, 90, 80), (90, 150, 226), (240, 200, 90), (120, 200, 120), (200, 120, 210), (240, 240, 240), (230, 150, 70)]
    skins = [(238, 190, 150), (196, 140, 100), (140, 96, 70), (250, 214, 180)]
    s = 7
    for row in range(5):
        y = 20 + row * 15
        d.rectangle((0, y + 11, w, y + 13), fill=(34, 24, 30, 255))
        for i in range(w // 8):
            s = (s * 1103515245 + 12345) & 0x7FFFFFFF
            if (s >> 8) % 7 == 0:
                continue
            x = (i * 8 + 1 + (row % 2) * 4) % (w - 5)
            c = colors[(s >> 12) % len(colors)]
            k = skins[(s >> 16) % len(skins)]
            d.rectangle((x, y + 5, x + 4, y + 11), fill=c + (255,))
            d.rectangle((x + 1, y + 1, x + 3, y + 4), fill=k + (255,))
    return img


def hills():
    w, h = 256, 64
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    far = []
    near = []
    for x in range(w):
        a = 2 * math.pi * x / w
        far.append(int(26 + 8 * math.sin(a * 2 + 1) + 5 * math.sin(a * 5)))
        near.append(int(40 + 6 * math.sin(a * 3 + 2) + 3 * math.sin(a * 7 + 1)))
    for x in range(w):
        for y in range(h):
            if y >= near[x]:
                img.putpixel((x, y), (42, 112, 74, 255) if (y - near[x]) > 1 else (66, 150, 92, 255))
            elif y >= far[x]:
                img.putpixel((x, y), (72, 122, 142, 255) if (y - far[x]) > 1 else (96, 150, 166, 255))
    return img


def rail():
    img = Image.new("RGBA", (32, 20), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle((0, 2, 31, 5), fill=(250, 248, 240, 255))
    d.rectangle((0, 5, 31, 6), fill=(170, 164, 150, 255))
    d.rectangle((0, 11, 31, 13), fill=(250, 248, 240, 255))
    d.rectangle((0, 13, 31, 14), fill=(170, 164, 150, 255))
    d.rectangle((14, 0, 17, 19), fill=(250, 248, 240, 255))
    d.rectangle((17, 0, 18, 19), fill=(170, 164, 150, 255))
    return img


def post():
    img = Image.new("RGBA", (16, 72), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle((6, 8, 9, 71), fill=(240, 236, 226, 255))
    d.rectangle((9, 8, 10, 71), fill=(160, 150, 140, 255))
    for y in range(8, 72, 12):
        d.rectangle((6, y, 10, y + 5), fill=(190, 40, 50, 255))
    d.ellipse((2, 0, 13, 11), fill=INK)
    d.ellipse((3, 1, 12, 10), fill=GOLD)
    return img


def flag():
    img = Image.new("RGBA", (24, 16), (0, 0, 0, 0))
    for y in range(16):
        for x in range(24):
            img.putpixel((x, y), (20, 20, 26, 255) if ((x // 4) + (y // 4)) % 2 == 0 else (245, 245, 245, 255))
    return img


def gate():
    img = Image.new("RGBA", (20, 72), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, 19, 7), fill=(150, 34, 46, 255))
    d.rectangle((0, 7, 19, 8), fill=(70, 20, 26, 255))
    d.rectangle((2, 8, 4, 71), fill=(226, 218, 200, 255))
    d.rectangle((15, 8, 17, 71), fill=(226, 218, 200, 255))
    return img


def main():
    atlas = Image.new("RGBA", (644, 600), (0, 0, 0, 0))
    atlas.paste(rotor(), (0, 0))
    atlas.paste(bowl(), (322, 0))
    atlas.paste(cabinet(), (0, 322))
    atlas.paste(seven(), (192, 322))
    atlas.paste(ball(), (226, 322))
    atlas.paste(turret(), (238, 322))
    atlas.paste(stands(), (280, 322))
    atlas.paste(hills(), (280, 420))
    atlas.paste(rail(), (192, 358))
    atlas.paste(post(), (226, 358))
    atlas.paste(flag(), (244, 358))
    atlas.paste(gate(), (192, 380))
    atlas.save(OUT, optimize=True)
    print(OUT, os.path.getsize(OUT), "bytes")
    if len(sys.argv) > 1:
        back = Image.new("RGBA", atlas.size, (44, 40, 70, 255))
        back.alpha_composite(atlas)
        back.save(sys.argv[1])


if __name__ == "__main__":
    main()
