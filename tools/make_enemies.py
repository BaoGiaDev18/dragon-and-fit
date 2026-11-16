"""Generates the original enemy sprites for battle workouts (Phase 3).

Each enemy sheet is 192 x 64 px: 3 frames of 64 x 64 (idle, idle squash, hurt),
drawn on a 32 x 32 grid and scaled 2x with nearest-neighbour.
Run: python3 tools/make_enemies.py frontend/public/enemies
"""
import math, sys
from PIL import Image

OUTLINE = (14, 12, 18)
W = 32

def blank():
    return [[None] * W for _ in range(W)]

def rect(px, x0, y0, x1, y1, c):
    for y in range(max(0, y0), min(W, y1 + 1)):
        for x in range(max(0, x0), min(W, x1 + 1)):
            px[y][x] = c

def ellipse(px, cx, cy, rx, ry, c):
    for y in range(W):
        for x in range(W):
            if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1:
                px[y][x] = c

def outline(px):
    filled = [[px[y][x] is not None for x in range(W)] for y in range(W)]
    for y in range(W):
        for x in range(W):
            if not filled[y][x] and any(0 <= y + dy < W and 0 <= x + dx < W and filled[y + dy][x + dx]
                                        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                px[y][x] = OUTLINE

def golem(f):  # push: rock golem
    px = blank(); s = 1 if f == 1 else 0
    body, dark, glow = (132, 118, 104), (92, 80, 70), (255, 150, 60)
    rect(px, 9, 10 + s, 22, 25, body)
    rect(px, 5, 12 + s, 8, 22, dark); rect(px, 23, 12 + s, 26, 22, dark)      # arms
    rect(px, 4, 22, 9, 25, body); rect(px, 22, 22, 27, 25, body)              # fists
    rect(px, 11, 5 + s, 20, 11 + s, body)                                       # head
    rect(px, 12, 8 + s, 13, 9 + s, glow); rect(px, 18, 8 + s, 19, 9 + s, glow)  # eyes
    rect(px, 10, 26, 13, 28, dark); rect(px, 18, 26, 21, 28, dark)            # legs
    for x, y in ((12, 15), (16, 18), (19, 14), (14, 21)): px[y + s][x] = dark  # cracks
    rect(px, 15, 16 + s, 16, 17 + s, glow)                                      # core
    return px

def wolf(f):  # legs: shadow wolf
    px = blank(); s = 1 if f == 1 else 0
    fur, dark, eye = (82, 66, 120), (52, 40, 82), (255, 214, 92)
    ellipse(px, 16, 19 + s, 10, 6, fur)                                         # body
    rect(px, 8, 23, 9, 28, dark); rect(px, 12, 24, 13, 28, dark)
    rect(px, 19, 24, 20, 28, dark); rect(px, 23, 23, 24, 28, dark)              # legs
    ellipse(px, 23, 12 + s, 6, 5, fur)                                          # head
    rect(px, 27, 13 + s, 30, 15 + s, fur)                                       # snout
    rect(px, 19, 5 + s, 20, 8 + s, dark); rect(px, 24, 5 + s, 25, 8 + s, dark)  # ears
    rect(px, 24, 11 + s, 25, 11 + s, eye)
    rect(px, 2, 14 + s, 6, 16 + s, dark); rect(px, 1, 12 + s, 2, 14 + s, dark)  # tail
    return px

def kraken(f):  # pull: swamp kraken
    px = blank(); s = 1 if f == 1 else 0
    skin, dark, eye = (70, 150, 96), (40, 96, 60), (240, 240, 210)
    ellipse(px, 16, 11 + s, 9, 8, skin)                                         # head
    for i, x in enumerate((7, 11, 15, 19, 23)):                                 # tentacles
        for y in range(17, 29):
            dx = int(round(math.sin((y + i * 2 + f * 3) / 2.5)))
            px[y][min(W - 1, max(0, x + dx))] = skin if y < 26 else dark
            px[y][min(W - 1, max(0, x + dx + 1))] = skin if y < 26 else dark
    rect(px, 11, 9 + s, 13, 11 + s, eye); rect(px, 19, 9 + s, 21, 11 + s, eye)
    px[10 + s][12] = OUTLINE; px[10 + s][20] = OUTLINE
    rect(px, 14, 4 + s, 18, 5 + s, dark)
    return px

def ghost(f):  # core: storm wraith
    px = blank(); s = 1 if f == 1 else 0
    mist, dark, eye = (150, 210, 226), (96, 150, 176), (40, 52, 80)
    ellipse(px, 16, 13 + s, 9, 10, mist)
    rect(px, 7, 13 + s, 25, 24, mist)
    for x in range(7, 26):                                                      # wavy hem
        h = 25 + int(round(1.5 * math.sin((x + f * 2) / 1.6)))
        rect(px, x, 24, x, h, mist if x % 4 else dark)
    rect(px, 11, 11 + s, 13, 14 + s, eye); rect(px, 19, 11 + s, 21, 14 + s, eye)
    rect(px, 14, 18 + s, 18, 19 + s, eye)
    rect(px, 4, 15 + s, 6, 16 + s, dark); rect(px, 26, 15 + s, 28, 16 + s, dark)  # arms of wind
    return px

def dragon(f):  # monthly boss: ember dragon
    px = blank(); s = 1 if f == 1 else 0
    scale, belly, dark, fire = (176, 52, 40), (236, 170, 90), (112, 30, 26), (255, 200, 80)
    # wings
    for i in range(8):
        rect(px, 3 + i, 6 + i // 2 + s, 3 + i, 15 - i // 3, dark)
        rect(px, 28 - i, 6 + i // 2 + s, 28 - i, 15 - i // 3, dark)
    ellipse(px, 16, 19 + s, 8, 8, scale)          # body
    ellipse(px, 16, 21 + s, 4, 5, belly)          # belly
    ellipse(px, 16, 9 + s, 6, 5, scale)           # head
    rect(px, 11, 2 + s, 12, 5 + s, belly); rect(px, 20, 2 + s, 21, 5 + s, belly)  # horns
    rect(px, 13, 8 + s, 14, 9 + s, fire); rect(px, 18, 8 + s, 19, 9 + s, fire)     # eyes
    rect(px, 14, 12 + s, 18, 13 + s, dark)                                          # mouth
    rect(px, 10, 26, 13, 28, dark); rect(px, 19, 26, 22, 28, dark)                # legs
    rect(px, 24, 22 + s, 29, 24 + s, scale); rect(px, 29, 20 + s, 30, 22 + s, dark)  # tail
    if f == 1:
        rect(px, 15, 14, 17, 16, fire)                                              # ember breath
    return px

def crowned(draw):
    def wrapped(f):
        px = draw(f)
        top = next(y for y in range(W) if any(px[y]))
        cols = [x for x in range(W) if px[top][x] is not None]
        cx = (min(cols) + max(cols)) // 2
        gold = (236, 184, 72)
        y = max(0, top - 3)
        rect(px, cx - 3, y + 1, cx + 3, y + 2, gold)
        for dx in (-3, 0, 3):
            px[y][min(W - 1, max(0, cx + dx))] = gold
        return px
    return wrapped

ENEMIES = {"golem": golem, "wolf": wolf, "kraken": kraken, "ghost": ghost, "dragon": dragon,
           "golem_boss": crowned(golem), "wolf_boss": crowned(wolf), "kraken_boss": crowned(kraken), "ghost_boss": crowned(ghost)}

def render(px, hurt=False):
    outline(px)
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    for y in range(W):
        for x in range(W):
            c = px[y][x]
            if c is None:
                continue
            if hurt and c != OUTLINE:
                c = tuple(min(255, int(v * 0.5 + 255 * 0.5)) for v in c)  # white flash
            img.putpixel((x, y), (*c, 255))
    return img.resize((64, 64), Image.NEAREST)

def main(out):
    for name, draw in ENEMIES.items():
        sheet = Image.new("RGBA", (192, 64), (0, 0, 0, 0))
        sheet.paste(render(draw(0)), (0, 0))
        sheet.paste(render(draw(1)), (64, 0))
        sheet.paste(render(draw(0), hurt=True), (128, 0))
        sheet.save(f"{out}/{name}.png")
        print("wrote", name)

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else ".")
