#!/usr/bin/env python3
"""
Builds the chunk map of the starter village from the NRO map 5 picture and the NRO scenery images (owner request 2026-10-08).

Input (the owner's files, paths can be changed with the options):
  map_5.png / map_5.json   the NRO map 5 rendered at zoom x4 (6912 x 2400: 72 x 25 tiles of 96 px) with its tile matrix
  Teamobi2026/SRC/data/item_bg_temp/x1..x4/<id>.png   scenery images (trees, houses, rocks, grass), ids 0 to 16
Output, into BattleBall/SourceData/data/map/village_start (the client reads it like the other downloaded assets, build_assets.py publishes it):
  chunk.json              size, ground heights, scenery placement and NPCs of the village
  ground_a_x1..x4.png, ground_b_x1..x4.png   the two halves of one segment of the map (a texture stays within 4096 px on weak phones)
  bg/x1..x4/<id>.png      the scenery images that are used
The village is THREE segments of this picture joined end to end (owner request 2026-10-11: the last two of the five were cut), every second one mirrored (both ends of the picture are the same high platform, so the
joints are seamless and the walk does not repeat itself). The black border tile columns of the picture are cut away, and the black bottom row of tiles (the owner saw it as a black band under the map), and so is the empty sky above.
Usage:  python tools/build_village_map.py [--map-png ...] [--map-json ...] [--bg ...] [--out ...]
"""
import argparse
import json
import os
import random
import shutil

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DEFAULT_PNG = r"C:\Users\cuonghayho\Documents\HocTap\AdrGame\phan_tich_game_2d\Ball\Report\output_maps\map_5.png"
DEFAULT_JSON = DEFAULT_PNG.replace(".png", ".json")
DEFAULT_BG = os.path.join(ROOT, "Teamobi2026", "SRC", "data", "item_bg_temp")
DEFAULT_OUT = os.path.join(ROOT, "BattleBall", "SourceData", "data", "map", "village_start")
DEFAULT_BACKGROUND = "C:/Users/cuonghayho/Desktop/data/Gemini_Generated_Image_3bhh3b3bhh3b3bhh.png"   # far background (owner file, 2026-10-08)
BG_HEIGHT_CANVAS = 640      # the background is shown this tall (canvas pixels); it scrolls slowly behind the map
BG_PARALLAX = 0.25
BG_BASE_Y = 90              # the bottom of the background sits at this height of the world (the ground covers it; a flat colour fills below)

SCALES = (1, 2, 3, 4)
SEGMENTS = 3
TILE4 = 96                  # a tile at zoom x4
CROP_TOP4 = 960             # the picture is empty sky above this row (x4); the first solid row of the map is 1061
SOLID_RUN4 = 160            # a column counts as ground where it is solid for this many x4 pixels (grass tufts are thinner)
STEP1 = 4                   # one ground height every 4 pixels of zoom x1
SINK1 = 11                  # the walkable ground is this many x1 pixels below the detected top (the bushes and tufts on it): the feet stand in the grass, not above it. The scenery keeps the detected top (owner request: trees and houses stay up where they were).
SCENERY_IDS = (0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 13, 14, 15, 16, 97, 129, 237)    # 9 is a floating island: not used; 129 the hover capsule behind Bunma, 97 and 237 the ships behind Cui


def ground_heights(img4):
    """Height of the ground above the bottom of the cropped picture, in x1 pixels, one value per x1 column."""
    w4, h4 = img4.size
    alpha = img4.getchannel("A").load()
    out = []
    for c in range(w4 // 4):
        x = 4 * c + 2
        top = None
        run = 0
        for y in range(h4):
            if alpha[x, y] > 128:
                run += 1
                if run >= SOLID_RUN4:
                    top = y - SOLID_RUN4 + 1
                    break
            else:
                run = 0
        out.append((h4 - top) / 4.0 if top is not None else 0.0)
    return out


def median(v, k):
    r = k // 2
    return [sorted(v[max(0, i - r):i + r + 1])[len(v[max(0, i - r):i + r + 1]) // 2] for i in range(len(v))]


def boxavg(v, k):
    r = k // 2
    return [sum(v[max(0, i - r):i + r + 1]) / len(v[max(0, i - r):i + r + 1]) for i in range(len(v))]


def platforms(h, min_width=120, spread=26.0):
    """Stretches of nearly flat ground wider than min_width (x1 pixels) whose height stays within `spread`: [(x0, x1, height)]."""
    out = []
    i = 0
    while i < len(h):
        lo = hi = h[i]
        j = i
        while j + 1 < len(h) and max(hi, h[j + 1]) - min(lo, h[j + 1]) <= spread:
            j += 1
            lo, hi = min(lo, h[j]), max(hi, h[j])
        if j - i + 1 >= min_width and lo > 150:          # above the rubble and the water
            seg = sorted(h[i:j + 1])
            out.append((i, j, seg[len(seg) // 2]))
        i = j + 1 if j > i else i + 1
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--map-png", default=DEFAULT_PNG)
    ap.add_argument("--map-json", default=DEFAULT_JSON)
    ap.add_argument("--bg", default=DEFAULT_BG)
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--background", default=DEFAULT_BACKGROUND)
    a = ap.parse_args()

    meta = json.load(open(a.map_json, encoding="utf-8"))
    assert meta["zoom_level"] == 4 and meta["tile_pixel_size"] == TILE4, "the picture must be the zoom x4 render"
    im = Image.open(a.map_png).convert("RGBA")
    crop = im.crop((TILE4, CROP_TOP4, im.width - TILE4, im.height - TILE4))       # no border columns, no black bottom row, no empty sky
    w4, h4 = crop.size
    seg_w1, img_h1 = w4 // 4, h4 // 4
    print("segment %d x %d (x1 pixels)" % (seg_w1, img_h1))

    if os.path.isdir(a.out):
        for name in os.listdir(a.out):                    # the folder itself may be open in another program: only its content is replaced
            path = os.path.join(a.out, name)
            if os.path.isdir(path):
                shutil.rmtree(path, ignore_errors=True)
            else:
                os.remove(path)
    os.makedirs(os.path.join(a.out, "bg"), exist_ok=True)

    # ground pictures: two halves per scale
    half4 = w4 // 2
    for n in SCALES:
        scaled = crop if n == 4 else crop.resize((w4 * n // 4, h4 * n // 4), Image.BOX if 4 % n == 0 else Image.LANCZOS)
        hw = scaled.width // 2
        scaled.crop((0, 0, hw, scaled.height)).save(os.path.join(a.out, "ground_a_x%d.png" % n), optimize=True)
        scaled.crop((hw, 0, scaled.width, scaled.height)).save(os.path.join(a.out, "ground_b_x%d.png" % n), optimize=True)
    slice_x = [0.0, (w4 // 2) / 4.0]
    slice_w = [(w4 // 2) / 4.0, (w4 - w4 // 2) / 4.0]

    # far background: two halves per scale (JPEG, no alpha), mirrored copies are made by the client
    background = None
    if a.background and os.path.exists(a.background):
        bgim = Image.open(a.background).convert("RGB")
        for n in SCALES:
            w = bgim.width * n // 4
            sc = bgim if n == 4 else bgim.resize((w, bgim.height * n // 4), Image.LANCZOS)
            hw = sc.width // 2
            sc.crop((0, 0, hw, sc.height)).save(os.path.join(a.out, "background_a_x%d.jpg" % n), quality=88, optimize=True)
            sc.crop((hw, 0, sc.width, sc.height)).save(os.path.join(a.out, "background_b_x%d.jpg" % n), quality=88, optimize=True)
        small = bgim.resize((64, 8), Image.BOX)
        top = [sum(small.getpixel((x, 0))[c] for x in range(64)) / 64 / 255 for c in range(3)]
        bottom = [sum(small.getpixel((x, 7))[c] for x in range(64)) / 64 / 255 for c in range(3)]
        background = {"width": bgim.width, "height": bgim.height, "heightCanvas": BG_HEIGHT_CANVAS, "parallax": BG_PARALLAX, "baseY": BG_BASE_Y,
                      "top": [round(v, 3) for v in top], "bottom": [round(v, 3) for v in bottom]}
        print("background %dx%d, top colour %s, bottom colour %s" % (bgim.width, bgim.height, background["top"], background["bottom"]))

    # terrain of one segment
    raw = ground_heights(crop)
    smooth = boxavg(median(raw, 5), 9)
    flat = platforms(smooth)
    for (x0, x1_, hgt) in flat:                           # the bumps of grass tufts are not walked on: the platform is flat
        for i in range(x0, x1_ + 1):
            smooth[i] = hgt
    smooth = boxavg(smooth, 5)                            # a little slope at the edges of the platforms
    sampled = [round(smooth[min(len(smooth) - 1, i)] - SINK1, 1) for i in range(0, len(smooth), STEP1)] + [round(smooth[-1] - SINK1, 1)]
    print("platforms (x0, x1, height):", [(p[0], p[1], round(p[2])) for p in flat])
    print("seam: start height %.1f, end height %.1f" % (smooth[0], smooth[-1]))

    # scenery: placed on the flat platforms of every segment (odd segments are mirrored), different seed per segment
    decor = []
    cui_at = [None]
    npcs_spots = []
    trees = (13, 12, 16, 0, 7, 2)
    houses = (10, 15)
    for s in range(SEGMENTS):
        rnd = random.Random(1000 + s)
        mirrored = s % 2 == 1
        placed = []                                       # (x, width) already used on this segment

        def free(x, wdt):
            return all(abs(x - px) >= (wdt + pw) / 2 + 8 for px, pw in placed) and 40 <= x <= seg_w1 - 40

        def add(img_id, x, ground_h, width):
            placed.append((x, width))
            wx = s * seg_w1 + ((seg_w1 - x) if mirrored else x)
            decor.append({"id": img_id, "x": round(wx, 1), "ground": round(ground_h, 1), "back": 1})

        sizes = {}
        for i in SCENERY_IDS:
            p = os.path.join(a.bg, "x1", "%d.png" % i)
            sizes[i] = Image.open(p).size
        if s == SEGMENTS - 1:
            # the two ships behind Cui (owner request 2026-10-11): on the widest platform of the last segment, where the dungeon NPC stands in front of them
            big = max(flat, key=lambda p: p[1] - p[0])
            mid = (big[0] + big[1]) // 2 + 60
            add(97, mid - 50, big[2], sizes[97][0])
            add(237, mid + 55, big[2], sizes[237][0])
            cui_at[0] = (s, mid + 5)
        for (x0, x1_, hgt) in flat:
            width = x1_ - x0
            # a house on the wider platforms (not every segment: the first and last always have one), then trees, rocks and grass between
            if width >= 280 and (s in (0, 4) or rnd.random() < 0.7):
                hid = rnd.choice(houses)
                hx = rnd.randint(x0 + sizes[hid][0] // 2 + 10, max(x0 + sizes[hid][0] // 2 + 11, x1_ - sizes[hid][0] // 2 - 10))
                if free(hx, sizes[hid][0]):
                    add(hid, hx, hgt, sizes[hid][0])
                    if len(npcs_spots) < 8:
                        npcs_spots.append((s, hx, hgt))
            for _ in range(max(1, width // 140)):
                tid = rnd.choice(trees)
                tx = rnd.randint(x0 + 20, x1_ - 20)
                if free(tx, sizes[tid][0] * 0.6):
                    add(tid, tx, hgt, sizes[tid][0] * 0.6)
            for _ in range(width // 90):
                gid = rnd.choice((4, 8, 14, 3, 11))
                gx = rnd.randint(x0 + 10, x1_ - 10)
                if free(gx, sizes[gid][0] * 0.5):
                    add(gid, gx, hgt, sizes[gid][0] * 0.5)

    # the hover capsule behind Bunma (owner request 2026-10-11): she stands at x 330 of the first platform
    bunma_x = 330.0
    for (x0, x1_, hgt) in flat:
        if x0 <= bunma_x <= x1_:
            decor.append({"id": 129, "x": bunma_x + 5.0, "ground": round(hgt, 1), "back": 1})
            break

    # NPCs stand on a platform near a house of the first and middle segment
    def spot(seg):
        c = [t for t in npcs_spots if t[0] == seg]
        return c[0] if c else None
    elder = spot(0) or (0, 300, 200)

    def world_x(seg, x, offset):
        # x is a house position in the segment; stand beside it, towards the middle of the platform
        mirrored = seg % 2 == 1
        lx = x + offset
        return seg * seg_w1 + ((seg_w1 - lx) if mirrored else lx)

    def ground_at_world(wx):
        seg = int(wx // seg_w1)
        lx = wx - seg * seg_w1
        if seg % 2 == 1:
            lx = seg_w1 - lx
        return smooth[max(0, min(len(smooth) - 1, int(lx)))]

    npcs = []
    for npc_id, name, costume, (seg, hx, hgt), talk, action in (
            ("elder", "Ông Trưởng làng", 430, elder, "Chào cháu! Dùng cần điều khiển để đi lại, kéo lên để nhảy, giữ lên để bay. Mở menu bên trái để xem nhân vật, hành trang và hòm thư.", "Talk"),):
        wx = world_x(seg, hx, 150)
        npcs.append({"id": npc_id, "name": name, "costume": costume, "x": round(wx, 1), "talk": talk, "action": action})
    # Cui, the NPC of the dungeons (owner request 2026-10-11: it takes the place of the placeholder gate of the cut end of the map): in front of the two ships, costume 9023 is
    # his Nro look (parts 48, 49, 50)
    cs, cx = cui_at[0]
    npcs.append({"id": "cui", "name": "Cui", "costume": 9023, "x": round(world_x(cs, cx, 0), 1), "talk": "Cháu muốn đi phó bản nào? Ta đưa cháu tới đó bằng tàu của ta.", "action": "DungeonGate"})
    # the training dummies (owner decision 2026-10-08, docs/architecture/BattleBallServer.md 15.12) on the flat platform right of the elder;
    # the id is the key of balance_config training.dummies, "mob" an Nro mob template drawn instead of the costume (-1 = the costume)
    for npc_id, name, mob, costume, wx in (("moc_nhan", "Mộc nhân", 0, 0, 880.0), ("saibamen", "Saibamen", -1, 647, 1060.0)):
        npcs.append({"id": npc_id, "name": name, "costume": costume, "mob": mob, "x": wx, "talk": "", "action": "TrainingDummy"})

    # Urôn sells every skin (owner request 2026-10-09, docs/architecture/BattleBallServer.md 15.15); costume 9020 is his Nro NPC look (parts 61, 62, 63)
    npcs.append({"id": "uron", "name": "Urôn", "costume": 9020, "x": 1250.0, "talk": "", "action": "Shop"})

    # Bunma sells clothes and stones (owner request 2026-10-10, docs 15.17): left of the elder, on the higher ground; costume 9021 is her Nro NPC look (parts 42, 43, 44)
    npcs.append({"id": "bunma", "name": "Bunma", "costume": 9021, "x": 330.0, "talk": "", "action": "Shop"})

    # Bà Hạt Mít upgrades gear (owner request 2026-10-10, docs 15.22): on the small platform right of Urôn; costume 9022 is her Nro look (parts 117, 118, 119)
    npcs.append({"id": "shop", "name": "Bà Hạt Mít", "costume": 9022, "x": 1530.0, "talk": "", "action": "Smith"})

    spawn = 400.0
    # scenery images for every scale
    for n in SCALES:
        os.makedirs(os.path.join(a.out, "bg", "x%d" % n), exist_ok=True)
        for i in SCENERY_IDS:
            shutil.copyfile(os.path.join(a.bg, "x%d" % n, "%d.png" % i), os.path.join(a.out, "bg", "x%d" % n, "%d.png" % i))

    chunk = {
        "id": "village_start", "name": "Làng tân thủ", "source": "NRO map 5 (map_5.png), 5 segments, odd ones mirrored",
        "segmentWidth": seg_w1, "segments": SEGMENTS, "imageHeight": img_h1, "flipOdd": True,
        "sliceX": slice_x, "sliceW": slice_w, "groundStep": STEP1, "ground": sampled,
        "spawnX": spawn, "decor": decor, "npcs": npcs,
    }
    if background:
        chunk["background"] = background
    with open(os.path.join(a.out, "chunk.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(chunk, f, ensure_ascii=False, indent=1)
    print("decor pieces:", len(decor), "| npcs:", [(n["id"], n["x"]) for n in npcs], "| files:", sum(len(fs) for _, _, fs in os.walk(a.out)))


if __name__ == "__main__":
    main()
