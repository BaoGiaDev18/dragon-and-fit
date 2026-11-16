"""Generates the original placeholder avatar sprite sheets (B4 spec).

Each sheet is 256 x 384 px: 4 idle frames (columns) x 6 evolution stages (rows),
64 x 64 per frame, drawn on a 32 x 32 grid and scaled 2x with nearest-neighbour.
Run: python3 tools/make_avatars.py frontend/public/avatars
"""
import sys
from PIL import Image

PALETTES = {
    "ember_knight":   {"cape": (178, 52, 38), "cape_dk": (110, 30, 26), "accent": (240, 163, 58)},
    "frost_blade":    {"cape": (52, 104, 178), "cape_dk": (30, 58, 110), "accent": (140, 214, 240)},
    "verdant_warden": {"cape": (58, 140, 72), "cape_dk": (32, 84, 44), "accent": (196, 226, 96)},
}
# Armour gets brighter with every evolution stage.
ARMOUR = [(112, 116, 124), (138, 142, 150), (168, 172, 180), (196, 198, 204), (214, 214, 220), (232, 230, 236)]
GOLD = (236, 184, 72)
OUTLINE = (18, 16, 20)
VISOR = (24, 22, 30)
SKIN = (222, 176, 140)
BOOT = (58, 46, 40)
BLADE = (214, 222, 232)
HILT = (120, 84, 52)

def frame(pal, stage, f):
    W = 32
    px = [[None] * W for _ in range(W)]
    bob = 1 if f in (1, 2) else 0
    sway = [0, 1, 1, 0][f]

    def rect(x0, y0, x1, y1, c, upper=True):
        dy = bob if upper else 0
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                yy = y + dy
                if 0 <= x < W and 0 <= yy < W:
                    px[yy][x] = c

    arm = ARMOUR[stage - 1]
    arm_dk = tuple(max(0, v - 40) for v in arm)

    # cape behind the body, swaying
    rect(11 - sway, 12, 20, 23, pal["cape_dk"])
    rect(12 - sway, 13, 19, 24, pal["cape"], upper=False)
    # legs and boots
    rect(13, 20, 14, 24, arm_dk, upper=False); rect(17, 20, 18, 24, arm_dk, upper=False)
    rect(12, 25, 14, 26, BOOT, upper=False); rect(17, 25, 19, 26, BOOT, upper=False)
    # torso
    rect(12, 12, 19, 19, arm)
    rect(13, 13, 18, 13, tuple(min(255, v + 25) for v in arm))
    rect(12, 19, 19, 19, HILT)                      # belt
    rect(15, 19, 16, 19, GOLD if stage >= 4 else (150, 150, 160))
    # arms
    rect(10, 13, 11, 18, arm_dk); rect(20, 13, 21, 17, arm_dk)
    rect(10, 19, 11, 19, SKIN); rect(20, 18, 21, 18, SKIN)
    # head / helmet
    rect(13, 5, 18, 11, arm)
    rect(14, 8, 17, 8, VISOR)
    rect(13, 11, 18, 11, arm_dk)
    # sword in the right hand
    rect(23, 3, 23, 16, BLADE); rect(24, 4, 24, 15, (170, 180, 196))
    rect(21, 17, 25, 17, GOLD if stage >= 4 else HILT)
    rect(23, 18, 23, 20, HILT)

    if stage >= 2:   # plume
        rect(15, 2, 16, 4, pal["cape"]); rect(16, 1, 17, 2, pal["cape"])
    if stage >= 3:   # pauldrons + shield
        rect(9, 11, 12, 13, arm); rect(19, 11, 22, 13, arm)
        rect(6, 13, 9, 21, pal["cape_dk"]); rect(7, 14, 8, 20, pal["cape"])
        rect(7, 16, 8, 17, GOLD if stage >= 4 else arm)
    if stage >= 4:   # gold trim
        rect(12, 12, 19, 12, GOLD); rect(13, 5, 18, 5, GOLD)
    if stage >= 6:   # crown spikes
        for x in (13, 15, 17):
            rect(x, 3, x, 4, GOLD)

    # 1-px outline around everything drawn so far
    filled = [[px[y][x] is not None for x in range(W)] for y in range(W)]
    for y in range(W):
        for x in range(W):
            if filled[y][x]:
                continue
            if any(0 <= y + dy < W and 0 <= x + dx < W and filled[y + dy][x + dx]
                   for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                px[y][x] = OUTLINE

    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    if stage >= 5:   # soft aura behind the outline, pulsing with the frame
        a = 70 + 30 * [0, 1, 2, 1][f]
        for y in range(W):
            for x in range(W):
                if px[y][x] is None and any(0 <= y + dy < W and 0 <= x + dx < W and px[y + dy][x + dx] == OUTLINE
                                            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                    img.putpixel((x, y), (*pal["accent"], a))
    for y in range(W):
        for x in range(W):
            if px[y][x] is not None:
                img.putpixel((x, y), (*px[y][x], 255))
    return img.resize((64, 64), Image.NEAREST)

def main(out):
    for name, pal in PALETTES.items():
        sheet = Image.new("RGBA", (256, 384), (0, 0, 0, 0))
        for stage in range(1, 7):
            for f in range(4):
                sheet.paste(frame(pal, stage, f), (f * 64, (stage - 1) * 64))
        sheet.save(f"{out}/{name}.png")
        print("wrote", f"{out}/{name}.png")

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else ".")
