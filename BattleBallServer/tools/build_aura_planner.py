"""
Builds the aura planner (tools/aura_planner/): the page the owner uses to give every aura a level milestone.

  python tools/build_aura_planner.py [--src <img_by_name folder>] [--out tools/aura_planner]

For every aura_<id>_<layer>.png of the Nro folder img_by_name/x4 (layer 0 = behind the character, layer 1 = in front of it; a 1x1 picture is an empty layer)
the script finds how many frames the strip holds (frames are stacked on top of each other and cut into equal parts, 4 or more),
copies the x2 picture into aura_planner/img/ (small enough for a web page) and writes aura_planner/data.js.

How the frame count is found (INFERRED from the pictures, the owner can correct it in the page): from 24 down to 4, the largest n for which every cut line
(at k * height / n) falls on transparent rows and a frame is not flatter than 0.8 x its width; when none fits, the n whose cut lines are the emptiest.
"""
import argparse
import json
import os
import re
import shutil

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_SRC = os.path.join(os.path.dirname(HERE), "..", "Teamobi2026", "SRC", "data", "img_by_name")
DEFAULT_OUT = os.path.join(HERE, "aura_planner")
EXCLUDE_IDS = {0}          # aura_0_* is not in the owner's list (2026-10-08)
MIN_FRAMES, MAX_FRAMES = 4, 24
MIN_FRAME_RATIO = 0.7      # frame height / width
MIN_BAND_RATIO = 0.4       # the same, when the frames are counted from their bands
EMPTY_ALPHA = 12           # a row whose strongest alpha is below this is empty


def row_alpha(alpha, w, y0, y1):
    """Strongest alpha of the rows y0..y1-1."""
    y0, y1 = max(0, int(y0)), min(alpha.height, int(y1))
    if y1 <= y0:
        return 0
    return alpha.crop((0, y0, w, y1)).getextrema()[1]


def content_runs(alpha, w, h, gap=2, thick=60):
    """Number of real bands of non-empty rows: bands closer than gap rows are one, bands thinner than thick rows are stray sparks."""
    bands, y = [], 0
    while y < h:
        if row_alpha(alpha, w, y, y + 1) >= EMPTY_ALPHA:
            start = y
            while y < h and row_alpha(alpha, w, y, y + 1) >= EMPTY_ALPHA:
                y += 1
            if bands and start - bands[-1][1] < gap:
                bands[-1][1] = y
            else:
                bands.append([start, y])
        else:
            y += 1
    return sum(1 for b in bands if b[1] - b[0] >= thick)


def detect_frames(path):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    alpha = im.split()[3]
    best = None
    for n in range(MAX_FRAMES, MIN_FRAMES - 1, -1):
        if h / n / w < MIN_FRAME_RATIO:
            continue
        cut = max(row_alpha(alpha, w, round(k * h / n) - 2, round(k * h / n) + 3) for k in range(1, n))
        if cut < EMPTY_ALPHA:
            return n, True
        if best is None or cut < best[0]:
            best = (cut, n)
    # no n has empty cut lines (the pictures touch the cells): the number of separate bands is the frame count when it makes sense
    runs = content_runs(alpha, w, h)
    if MIN_FRAMES <= runs <= MAX_FRAMES and h / runs / w >= MIN_BAND_RATIO:        # wide frames exist (aura 28 and 63: 600 x 300)
        return runs, False
    return (best[1] if best else MIN_FRAMES), False


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default=DEFAULT_SRC)
    ap.add_argument("--out", default=DEFAULT_OUT)
    a = ap.parse_args()
    src4 = os.path.join(a.src, "x4")
    src2 = os.path.join(a.src, "x2")
    img_out = os.path.join(a.out, "img")
    os.makedirs(img_out, exist_ok=True)
    for f in os.listdir(img_out):
        if f.endswith(".png"):
            os.remove(os.path.join(img_out, f))

    auras = {}
    for name in sorted(os.listdir(src4)):
        m = re.fullmatch(r"aura_(\d+)_(\d+)\.png", name)
        if not m:
            continue
        aid, layer = int(m.group(1)), int(m.group(2))
        if aid in EXCLUDE_IDS:
            continue
        w4, h4 = Image.open(os.path.join(src4, name)).size
        if w4 <= 4 and h4 <= 4:
            continue                                                # an empty layer
        n, sure = detect_frames(os.path.join(src4, name))
        p2 = os.path.join(src2, name)
        if not os.path.exists(p2):
            p2 = os.path.join(src4, name)
            scale = 4
        else:
            scale = 2
        w, h = Image.open(p2).size
        shutil.copyfile(p2, os.path.join(img_out, name))
        entry = auras.setdefault(aid, {"id": aid, "layers": []})
        entry["layers"].append({"layer": layer, "file": "img/" + name, "w": w, "h": h, "frames": n, "sure": sure, "scale": scale})

    for entry in auras.values():                                    # both layers of an aura animate together: the same cut when they are equally tall
        ls = sorted(entry["layers"], key=lambda l: l["layer"])
        for l in ls[1:]:
            if l["h"] == ls[0]["h"] and l["frames"] != ls[0]["frames"]:
                l["frames"], l["sure"] = ls[0]["frames"], ls[0]["sure"]
    data = [auras[k] for k in sorted(auras)]
    with open(os.path.join(a.out, "data.js"), "w", encoding="utf-8") as f:
        f.write("// made by tools/build_aura_planner.py: the auras of Nro (img_by_name) and the frame count found for each layer\n")
        f.write("const AURA_DATA = " + json.dumps(data, ensure_ascii=False) + ";\n")
    unsure = [(x["id"], l["layer"], l["frames"]) for x in data for l in x["layers"] if not l["sure"]]
    print("%d auras, %d layers, frame counts not certain for %s" % (len(data), sum(len(x["layers"]) for x in data), unsure))


if __name__ == "__main__":
    main()
