"""Pixel art for IsaClient's casino (Poker, Blackjack): cards, chips and the table's textures.

The Moon Lord dealer is drawn in the game from Terraria's own Moon Lord textures (see CasinoArt.MoonLord).
Writes src/IsaClient.Assets/Casino.png, an atlas whose layout CasinoArt.cs reads (LAYOUT below), and optionally a big
preview: python tools/casino/make_art.py [preview.png]
"""
import math
import os
import random
import sys

from PIL import Image, ImageDraw

LAYOUT = """
Atlas (pixels):
  Every piece has at least one clear pixel around it (scaled drawing never picks up a neighbour's edge).
  y=0:   card face 40x56 at x=0, card back 40x56 at x=41, dealer button 18x18 at x=82
  y=57:  big suits 11x11 (spade heart diamond club) at x=i*12; small suits 7x7 at x=48+i*8
  y=69:  rank glyphs 2..10 J Q K A, 9 tall, each in a 10-wide slot at x=i*10 (white; 10 is 9 wide, the rest 7)
  y=79:  chips seen from the top 18x18 (1 5 25 100 500 1000) at x=i*19; chip edges 18x5 at y=98
  y=0:   felt grain 48x48 at x=100, carpet tile 32x32 at x=150
  y=104: pill 24x14 (white fill, darker rim; 3-slice: 7 | 10 | 7) at x=0, chip shadow 20x6 at x=25,
         bet ring 44x44 (white, two rings) at x=46, card shadow 40x56 at x=91 (the card's shape, black)
"""

CLEAR = (0, 0, 0, 0)
CARD_W, CARD_H = 40, 56


def bitmap(rows, color):
    h, w = len(rows), max(len(r) for r in rows)
    img = Image.new("RGBA", (w, h), CLEAR)
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch == "#":
                img.putpixel((x, y), color)
    return img


# ---------------------------------------------------------------- cards

def rounded_card(fill, edge, shade):
    img = Image.new("RGBA", (CARD_W, CARD_H), CLEAR)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, CARD_W - 1, CARD_H - 1), radius=4, fill=edge)
    d.rounded_rectangle((1, 1, CARD_W - 2, CARD_H - 2), radius=3, fill=fill)
    # a soft shade along the bottom and the right
    d.line([(3, CARD_H - 2), (CARD_W - 4, CARD_H - 2)], fill=shade)
    d.line([(CARD_W - 2, 3), (CARD_W - 2, CARD_H - 4)], fill=shade)
    return img


def card_face():
    return rounded_card((252, 250, 244, 255), (70, 66, 74, 255), (218, 212, 200, 255))


def card_back():
    img = rounded_card((246, 240, 226, 255), (70, 40, 44, 255), (210, 200, 186, 255))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((3, 3, CARD_W - 4, CARD_H - 4), radius=2, fill=(128, 26, 40, 255))
    # a diamond lattice in a darker red
    for y in range(4, CARD_H - 4):
        for x in range(4, CARD_W - 4):
            if (x + y) % 6 == 0 or (x - y) % 6 == 0:
                img.putpixel((x, y), (96, 16, 30, 255))
    # a gold frame inside and a gold eye emblem in the middle (the Moon Lord's)
    d.rounded_rectangle((5, 5, CARD_W - 6, CARD_H - 6), radius=2, outline=(214, 170, 72, 255))
    cx, cy = CARD_W // 2, CARD_H // 2
    d.ellipse((cx - 9, cy - 6, cx + 8, cy + 6), fill=(96, 16, 30, 255))
    d.ellipse((cx - 8, cy - 5, cx + 7, cy + 5), fill=(240, 226, 190, 255))
    d.ellipse((cx - 4, cy - 4, cx + 3, cy + 4), fill=(52, 200, 190, 255))
    d.ellipse((cx - 2, cy - 2, cx + 1, cy + 2), fill=(16, 40, 48, 255))
    img.putpixel((cx - 3, cy - 3), (255, 255, 255, 255))
    d.arc((cx - 9, cy - 9, cx + 8, cy + 3), 200, 340, fill=(214, 170, 72, 255))
    d.arc((cx - 9, cy - 3, cx + 8, cy + 9), 20, 160, fill=(214, 170, 72, 255))
    return img


def dealer_button():
    img = Image.new("RGBA", (18, 18), CLEAR)
    d = ImageDraw.Draw(img)
    d.ellipse((0, 1, 17, 17), fill=(60, 52, 40, 255))
    d.ellipse((0, 0, 17, 16), fill=(70, 62, 50, 255))
    d.ellipse((1, 1, 16, 15), fill=(250, 246, 236, 255))
    d.ellipse((3, 3, 14, 13), outline=(210, 196, 170, 255))
    g = bitmap(["####.", "#...#", "#...#", "#...#", "#...#", "####."], (40, 34, 30, 255))
    img.alpha_composite(g, (6, 5))
    return img


SUITS = {
    "spade": [
        ".....#.....",
        "....###....",
        "...#####...",
        "..#######..",
        ".#########.",
        "###########",
        "###########",
        ".###.#.###.",
        ".....#.....",
        "....###....",
        "...#####...",
    ],
    "heart": [
        ".###...###.",
        "#####.#####",
        "###########",
        "###########",
        "###########",
        ".#########.",
        "..#######..",
        "...#####...",
        "....###....",
        ".....#.....",
        "...........",
    ],
    "diamond": [
        ".....#.....",
        "....###....",
        "...#####...",
        "..#######..",
        ".#########.",
        "###########",
        ".#########.",
        "..#######..",
        "...#####...",
        "....###....",
        ".....#.....",
    ],
    "club": [
        "....###....",
        "...#####...",
        "...#####...",
        "....###....",
        ".##.###.##.",
        "###########",
        "###########",
        ".##..#..##.",
        ".....#.....",
        "....###....",
        "...#####...",
    ],
}

SMALL = {
    "spade": ["...#...", "..###..", ".#####.", "#######", "#######", "..#.#..", ".#####."],
    "heart": [".##.##.", "#######", "#######", ".#####.", "..###..", "...#...", "......."],
    "diamond": ["...#...", "..###..", ".#####.", "#######", ".#####.", "..###..", "...#..."],
    "club": ["..###..", "..###..", "##.#.##", "#######", "##.#.##", "...#...", "..###.."],
}

# 7x9 bold ranks (10 is 9 wide)
GLYPHS = {
    "2": [".#####.", "##...##", ".....##", "....##.", "...##..", "..##...", ".##....", "##.....", "#######"],
    "3": ["######.", ".....##", ".....##", "..####.", ".....##", ".....##", ".....##", "##...##", ".#####."],
    "4": ["....##.", "...###.", "..####.", ".##.##.", "##..##.", "#######", "....##.", "....##.", "....##."],
    "5": ["#######", "##.....", "##.....", "######.", ".....##", ".....##", ".....##", "##...##", ".#####."],
    "6": ["..####.", ".##....", "##.....", "######.", "##...##", "##...##", "##...##", "##...##", ".#####."],
    "7": ["#######", ".....##", "....##.", "....##.", "...##..", "...##..", "..##...", "..##...", "..##..."],
    "8": [".#####.", "##...##", "##...##", ".#####.", "##...##", "##...##", "##...##", "##...##", ".#####."],
    "9": [".#####.", "##...##", "##...##", "##...##", ".######", ".....##", ".....##", "....##.", ".####.."],
    "10": ["##..####.", "##.##..##", "##.##..##", "##.##..##", "##.##..##", "##.##..##", "##.##..##", "##.##..##", "##..####."],
    "J": ["..#####", "....##.", "....##.", "....##.", "....##.", "....##.", "##..##.", "##..##.", ".####.."],
    "Q": [".#####.", "##...##", "##...##", "##...##", "##...##", "##.#.##", "##..##.", "##..##.", ".###.##"],
    "K": ["##...##", "##..##.", "##.##..", "####...", "###....", "####...", "##.##..", "##..##.", "##...##"],
    "A": ["..###..", ".##.##.", "##...##", "##...##", "##...##", "#######", "##...##", "##...##", "##...##"],
}


# ---------------------------------------------------------------- chips

CHIPS = {
    # value: (main, edge spots, inner)
    1: ((238, 236, 230), (44, 100, 200), (220, 218, 210)),
    5: ((204, 40, 48), (250, 248, 240), (184, 30, 40)),
    25: ((36, 150, 80), (250, 248, 240), (28, 128, 66)),
    100: ((38, 38, 46), (236, 236, 236), (30, 30, 36)),
    500: ((128, 52, 186), (252, 216, 90), (108, 40, 162)),
    1000: ((236, 186, 52), (130, 74, 18), (220, 166, 40)),
}


def chip_top(main, spot, inner):
    S = 18
    img = Image.new("RGBA", (S, S), CLEAR)
    d = ImageDraw.Draw(img)
    dark = tuple(max(0, v - 70) for v in main)
    d.ellipse((0, 0, S - 1, S - 1), fill=dark + (255,))
    d.ellipse((0, 0, S - 1, S - 2), fill=main + (255,))
    # six spots around the edge
    for k in range(6):
        a = math.radians(k * 60 + 30)
        x = S / 2 - 0.5 + math.cos(a) * 6.8
        y = S / 2 - 1 + math.sin(a) * 6.8
        d.ellipse((round(x) - 1.5, round(y) - 1.5, round(x) + 1, round(y) + 1), fill=spot + (255,))
    # the inner ring and centre
    d.ellipse((4, 4, S - 5, S - 6), fill=inner + (255,))
    d.ellipse((4, 4, S - 5, S - 6), outline=spot + (255,))
    # a highlight top left
    d.arc((1, 1, S - 2, S - 3), 190, 260, fill=tuple(min(255, v + 60) for v in main) + (255,))
    return img


def chip_side(main, spot):
    W, H = 18, 5
    img = Image.new("RGBA", (W, H), CLEAR)
    d = ImageDraw.Draw(img)
    dark = tuple(max(0, v - 70) for v in main)
    d.rectangle((1, 0, W - 2, H - 1), fill=dark + (255,))
    d.rectangle((0, 1, W - 1, H - 2), fill=main + (255,))
    for x in (3, 8, 13):
        d.rectangle((x, 1, x + 1, H - 2), fill=spot + (255,))
    d.line([(1, 1), (W - 2, 1)], fill=tuple(min(255, v + 50) for v in main) + (255,))
    return img


# ---------------------------------------------------------------- textures

def felt_grain():
    """Soft noise (white and black, low alpha) to lay over the felt."""
    rnd = random.Random(7)
    img = Image.new("RGBA", (48, 48), CLEAR)
    for y in range(48):
        for x in range(48):
            v = rnd.random()
            if v < 0.18:
                img.putpixel((x, y), (255, 255, 255, int(10 + v * 60)))
            elif v > 0.86:
                img.putpixel((x, y), (0, 0, 0, int((v - 0.86) * 300)))
    return img


def carpet_tile():
    """A casino carpet: deep red with a small gold-and-teal motif (tiles seamlessly)."""
    img = Image.new("RGBA", (32, 32), (92, 18, 30, 255))
    d = ImageDraw.Draw(img)
    for (cx, cy) in ((8, 8), (24, 24)):
        d.polygon([(cx, cy - 5), (cx + 5, cy), (cx, cy + 5), (cx - 5, cy)], fill=(150, 104, 40, 255))
        d.polygon([(cx, cy - 3), (cx + 3, cy), (cx, cy + 3), (cx - 3, cy)], fill=(36, 92, 96, 255))
        img.putpixel((cx, cy), (230, 190, 90, 255))
    for (cx, cy) in ((24, 8), (8, 24)):
        for dx, dy in ((0, -2), (2, 0), (0, 2), (-2, 0)):
            img.putpixel((cx + dx, cy + dy), (150, 104, 40, 255))
    for x in range(32):
        for y in range(32):
            if (x + y) % 16 == 0 and img.getpixel((x, y))[0] == 92:
                img.putpixel((x, y), (112, 26, 40, 255))
    return img


def pill():
    img = Image.new("RGBA", (24, 14), CLEAR)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, 23, 13), radius=7, fill=(150, 150, 150, 255))
    d.rounded_rectangle((1, 1, 22, 12), radius=6, fill=(255, 255, 255, 255))
    return img


def chip_shadow():
    img = Image.new("RGBA", (20, 6), CLEAR)
    ImageDraw.Draw(img).ellipse((0, 0, 19, 5), fill=(0, 0, 0, 255))
    return img


def bet_ring():
    img = Image.new("RGBA", (44, 44), CLEAR)
    d = ImageDraw.Draw(img)
    d.ellipse((0, 0, 43, 43), outline=(255, 255, 255, 255), width=2)
    d.ellipse((5, 5, 38, 38), outline=(255, 255, 255, 140), width=1)
    return img


def card_shadow():
    img = Image.new("RGBA", (CARD_W, CARD_H), CLEAR)
    ImageDraw.Draw(img).rounded_rectangle((0, 0, CARD_W - 1, CARD_H - 1), radius=4, fill=(0, 0, 0, 255))
    return img


def build():
    atlas = Image.new("RGBA", (190, 160), CLEAR)
    atlas.alpha_composite(pill(), (0, 104))
    atlas.alpha_composite(chip_shadow(), (25, 104))
    atlas.alpha_composite(bet_ring(), (46, 104))
    atlas.alpha_composite(card_shadow(), (91, 104))
    atlas.alpha_composite(card_face(), (0, 0))
    atlas.alpha_composite(card_back(), (41, 0))
    atlas.alpha_composite(dealer_button(), (82, 0))
    white = (255, 255, 255, 255)
    for i, s in enumerate(("spade", "heart", "diamond", "club")):
        atlas.alpha_composite(bitmap(SUITS[s], white), (i * 12, 57))
        atlas.alpha_composite(bitmap(SMALL[s], white), (48 + i * 8, 57))
    for i, g in enumerate(("2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A")):
        atlas.alpha_composite(bitmap(GLYPHS[g], white), (i * 10, 69))
    for i, v in enumerate((1, 5, 25, 100, 500, 1000)):
        main, spot, inner = CHIPS[v]
        atlas.alpha_composite(chip_top(main, spot, inner), (i * 19, 79))
        atlas.alpha_composite(chip_side(main, spot), (i * 19, 98))
    atlas.alpha_composite(felt_grain(), (100, 0))
    atlas.alpha_composite(carpet_tile(), (150, 0))
    return atlas


if __name__ == "__main__":
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    atlas = build()
    out = os.path.join(root, "src", "IsaClient.Assets", "Casino.png")
    atlas.save(out)
    print("wrote", out, atlas.size)
    if len(sys.argv) > 1:
        big = atlas.resize((atlas.width * 6, atlas.height * 6), Image.NEAREST)
        bg = Image.new("RGBA", big.size, (34, 92, 60, 255))
        bg.alpha_composite(big)
        bg.save(sys.argv[1])
        print("preview", sys.argv[1])
