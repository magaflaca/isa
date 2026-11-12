"""Pixel-art sprites for IsaClient's games (chess pieces, board tiles, tic tac toe marks).

Writes src/IsaClient.Assets/Games.png, an atlas of 16x16 cells:
  row 0: white  king queen rook bishop knight pawn
  row 1: black  king queen rook bishop knight pawn
  row 2: light tile, dark tile, X, O, felt tile
Run: python tools/games/make_sprites.py [preview.png]
"""
import os
import random
import sys

from PIL import Image

CELL = 16

# '#' body, 'o' accent (gold / gem), 's' a groove in the body (outline colour). Light comes from the top left.
PIECES = {
    "king": [
        "................",
        ".......##.......",
        ".....######.....",
        ".......##.......",
        "...###.##.###...",
        "..############..",
        "..#####oo#####..",
        "...##########...",
        "....########....",
        "....oooooooo....",
        ".....######.....",
        ".....######.....",
        "....########....",
        "...##########...",
        "..############..",
        "................",
    ],
    "queen": [
        "................",
        "..o..o.oo.o..o..",
        "..#..#.##.#..#..",
        "..##.######.##..",
        "...##########...",
        "....###oo###....",
        ".....######.....",
        "......####......",
        ".....oooooo.....",
        "......####......",
        ".....######.....",
        ".....######.....",
        "....########....",
        "...##########...",
        "..############..",
        "................",
    ],
    "rook": [
        "................",
        "................",
        "...##..##..##...",
        "...##########...",
        "....########....",
        ".....######.....",
        ".....##ss##.....",
        ".....######.....",
        ".....######.....",
        ".....######.....",
        ".....######.....",
        "....oooooooo....",
        "....########....",
        "...##########...",
        "..############..",
        "................",
    ],
    "bishop": [
        "................",
        ".......oo.......",
        ".......##.......",
        "......####......",
        ".....###s##.....",
        ".....##s###.....",
        ".....######.....",
        "......####......",
        ".....oooooo.....",
        "......####......",
        "......####......",
        ".....######.....",
        "....########....",
        "...##########...",
        "..############..",
        "................",
    ],
    "knight": [
        "................",
        "........#.#.....",
        ".......######...",
        "......########..",
        ".....##o#######.",
        "....###########.",
        "...######.#####.",
        "...#####..#####.",
        "....###..######.",
        ".........######.",
        "........######..",
        ".....oooooooo...",
        "....##########..",
        "...############.",
        "..############..",
        "................",
    ],
    "pawn": [
        "................",
        "................",
        "................",
        "................",
        "......####......",
        ".....######.....",
        ".....######.....",
        "......####......",
        ".....oooooo.....",
        "......####......",
        "......####......",
        ".....######.....",
        "....########....",
        "...##########...",
        "..############..",
        "................",
    ],
}

ORDER = ["king", "queen", "rook", "bishop", "knight", "pawn"]

# Hallowed (pearl and gold) and Shadow (ebon and crimson gems).
WHITE = {
    "hi": (255, 253, 244), "base": (234, 228, 214), "shade": (190, 180, 166), "deep": (146, 134, 128),
    "outline": (48, 36, 46), "acc": (247, 196, 64), "acc_hi": (255, 238, 140), "acc_lo": (196, 132, 34),
}
BLACK = {
    "hi": (146, 126, 182), "base": (98, 82, 130), "shade": (68, 56, 94), "deep": (46, 36, 66),
    "outline": (12, 8, 18), "acc": (228, 62, 112), "acc_hi": (255, 140, 176), "acc_lo": (160, 30, 80),
}


def blur(grid):
    out = [[0.0] * CELL for _ in range(CELL)]
    for y in range(CELL):
        for x in range(CELL):
            total = 0.0
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    yy, xx = y + dy, x + dx
                    if 0 <= yy < CELL and 0 <= xx < CELL:
                        total += grid[yy][xx]
            out[y][x] = total / 9.0
    return out


def piece(rows, pal):
    img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    px = img.load()
    solid = [[1.0 if rows[y][x] != "." else 0.0 for x in range(CELL)] for y in range(CELL)]
    soft = blur(blur(solid))
    for y in range(CELL):
        for x in range(CELL):
            ch = rows[y][x]
            if ch == ".":
                near = any(0 <= y + dy < CELL and 0 <= x + dx < CELL and rows[y + dy][x + dx] != "."
                           for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
                if near:
                    px[x, y] = pal["outline"] + (255,)
                continue
            if ch == "s":
                px[x, y] = pal["outline"] + (255,)
                continue
            # Light from the top left: the slope of the blurred silhouette tells which way the surface faces.
            gx = soft[y][min(x + 1, CELL - 1)] - soft[y][max(x - 1, 0)]
            gy = soft[min(y + 1, CELL - 1)][x] - soft[max(y - 1, 0)][x]
            light = gx * 0.75 + gy * 0.55 + (x - 7.5) * -0.012 + (y - 8) * -0.01
            if ch == "o":
                key = "acc_hi" if light > 0.12 else ("acc" if light > -0.12 else "acc_lo")
            else:
                key = "hi" if light > 0.16 else ("base" if light > -0.06 else ("shade" if light > -0.26 else "deep"))
            px[x, y] = pal[key] + (255,)
    return img


def tile(base, dark, light, seed):
    rnd = random.Random(seed)
    img = Image.new("RGBA", (CELL, CELL), base + (255,))
    px = img.load()
    # Two planks with a seam, grain streaks along them.
    for y in range(CELL):
        for x in range(CELL):
            px[x, y] = (light if rnd.random() < 0.05 else base) + (255,)
    for board in range(2):
        top = board * 8
        for _ in range(3):
            gy = top + rnd.randint(1, 6)
            gx = rnd.randint(0, CELL - 1)
            for i in range(rnd.randint(4, 9)):
                px[(gx + i) % CELL, gy] = dark + (255,)
    for x in range(CELL):
        px[x, 7] = dark + (255,)
        px[x, 8] = light + (255,)
    return img


def outlined(body, colors):
    """body: set of (x, y); colors(x, y) -> rgb. Adds a dark outline around it."""
    img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    px = img.load()
    out = colors(None, None)
    for y in range(CELL):
        for x in range(CELL):
            if (x, y) in body:
                px[x, y] = colors(x, y) + (255,)
            elif any((x + dx, y + dy) in body for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                px[x, y] = out + (255,)
    return img


def mark_x():
    hi, base, lo, out = (255, 176, 164), (238, 84, 84), (176, 44, 60), (54, 14, 24)
    body = set()
    for i in range(2, 14):
        for w in (-1, 0, 1):
            body.add((i + w - 1, i))
            body.add((16 - i - w, i))
    body = {(x, y) for x, y in body if 1 <= x <= 14 and 1 <= y <= 14}

    def color(x, y):
        if x is None:
            return out
        if (x, y - 1) not in body:
            return hi
        if (x, y + 1) not in body:
            return lo
        return base
    return outlined(body, color)


def mark_o():
    hi, base, lo, out = (176, 232, 255), (84, 170, 246), (40, 98, 190), (12, 26, 60)
    body = set()
    for y in range(CELL):
        for x in range(CELL):
            d = ((x - 7.5) ** 2 + (y - 7.5) ** 2) ** 0.5
            if 3.4 <= d <= 6.3:
                body.add((x, y))

    def color(x, y):
        if x is None:
            return out
        light = (7.5 - x) * 0.5 + (7.5 - y) * 0.8
        if ((x - 7.5) ** 2 + (y - 7.5) ** 2) ** 0.5 < 4.7:
            light = -light
        return hi if light > 2.5 else (lo if light < -2.5 else base)
    return outlined(body, color)


def main():
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    atlas = Image.new("RGBA", (CELL * 6, CELL * 3), (0, 0, 0, 0))
    for row, pal in enumerate((WHITE, BLACK)):
        for col, name in enumerate(ORDER):
            atlas.paste(piece(PIECES[name], pal), (col * CELL, row * CELL))
    atlas.paste(tile((236, 214, 176), (210, 180, 136), (246, 230, 200), 7), (0, 2 * CELL))
    atlas.paste(tile((150, 96, 66), (122, 74, 50), (168, 112, 80), 11), (CELL, 2 * CELL))
    atlas.paste(mark_x(), (2 * CELL, 2 * CELL))
    atlas.paste(mark_o(), (3 * CELL, 2 * CELL))
    atlas.paste(tile((58, 92, 74), (46, 74, 60), (70, 108, 88), 5), (4 * CELL, 2 * CELL))
    out = os.path.join(root, "src", "IsaClient.Assets", "Games.png")
    atlas.save(out)
    print("wrote", out)
    if len(sys.argv) > 1:
        # A preview: the atlas, and half a board with pieces on it.
        board = Image.new("RGBA", (CELL * 8, CELL * 4))
        for y in range(4):
            for x in range(8):
                t = (x + y) % 2
                board.paste(atlas.crop((t * CELL, 2 * CELL, t * CELL + CELL, 3 * CELL)), (x * CELL, y * CELL))
        line = ["rook", "knight", "bishop", "queen", "king", "bishop", "knight", "rook"]
        for x, name in enumerate(line):
            i = ORDER.index(name)
            for row, y in ((1, 0), (0, 3)):
                board.alpha_composite(atlas.crop((i * CELL, row * CELL, i * CELL + CELL, row * CELL + CELL)), (x * CELL, y * CELL))
            board.alpha_composite(atlas.crop((5 * CELL, CELL, 6 * CELL, 2 * CELL)), (x * CELL, CELL))
            board.alpha_composite(atlas.crop((5 * CELL, 0, 6 * CELL, CELL)), (x * CELL, 2 * CELL))
        scale = 6
        prev = Image.new("RGBA", (CELL * 8 * scale, CELL * 7 * scale + 8), (30, 30, 40, 255))
        prev.alpha_composite(atlas.resize((CELL * 6 * scale, CELL * 3 * scale), Image.NEAREST), (0, 0))
        prev.alpha_composite(board.resize((CELL * 8 * scale, CELL * 4 * scale), Image.NEAREST), (0, CELL * 3 * scale + 8))
        prev.save(sys.argv[1])


if __name__ == "__main__":
    main()
