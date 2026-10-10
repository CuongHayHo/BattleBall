#!/usr/bin/env python3
"""
Builds the downloadable game assets published by the web server (docs/architecture/BattleBallServer.md, section 15.6).

Reads, from the BattleBall project:
  BattleBall/Assets/NroMannequin/Data/PartDatabase.json, CostumeCatalog.json   (which icons the costumes need)
  BattleBall/Assets/NroMannequin/Data/image.bytes                              (atlas slices: icon id -> Big0..Big4 or a loose PNG)
  BattleBall/SourceData/data/res/x{N}/Big{k}, data/icon/x{N}/{id}.png          (the sprites, N = 2, 3, 4)
and writes into web/assets:
  manifest.json   {"version", "packs":[{"id","scale","url","size","sha256"}]}
  packs/*.zip     "common" (scale 0: update_data/image) and, per scale, "x{N}_atlas" (Big0..Big4) and "x{N}_icons_###" (loose PNGs, 400 per pack)
Each zip keeps the folder layout of SourceData (data/res/xN/BigK, data/icon/xN/ID.png, data/update_data/image), so the client only unzips it
into its cache folder and NroSpriteResolver reads it as it reads SourceData in the Editor.
Scale x1 (owner request 2026-10-08, "x1 x2 x3 x4" in the settings): SourceData has no x1 atlas and misses some loose x1 icons, so the x1 set is
"x1_atlas_from_x2" (the x2 atlases, scale 1: NroSpriteResolver shrinks them to x1), "x1_icons_###" (the loose x1 PNGs that exist) and "x1_icons_fallback"
(the x2 version of every loose icon that has no x1 file: the resolver falls back to another scale for them).

Auras: "aura_x{N}" holds data/aura/x{N}/aura_<id>_<layer>.png of every aura in tools/aura_planner/aura_levels.json.

Usage:  python tools/build_assets.py [--out web/assets]
Re-running replaces the packs; packs whose content did not change keep the same sha256 (zips are written with fixed timestamps), so a
client only downloads what changed.
"""
import argparse
import ast
import hashlib
import io
import json
import os
import shutil
import struct
import zipfile

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CLIENT = os.path.join(ROOT, "BattleBall")
DATA = os.path.join(CLIENT, "Assets", "NroMannequin", "Data")
SOURCE = os.path.join(CLIENT, "SourceData", "data")
SCALES = (1, 2, 3, 4)
CHUNK = 400
FIXED_TIME = (2026, 1, 1, 0, 0, 0)


# icons the menus need besides the costumes: the Potara earring (Nro item 1819, icon 15614) and its shard (item 1820, icon 15613); see V025__potara_items_nro_ids.sql.
# 11569, 11572, 15412: the book materials #301 to #303 (the mailbox shows the picture of a gift).
# 728: the portrait of Urôn (the NPC shop shows it at the top, like the avatar of an NPC in Nro).
EXTRA_ICON_IDS = (15614, 15613, 11569, 11572, 15412, 728, 11166, 11167, 11168, 11169, 11170, 11171, 11172)      # the last seven: the jar of Ma phong ba (parts 1218 to 1220)
# the skill buttons of the village training (skill_template.icon_id of the 25 skills, docs/architecture/BattleBallServer.md 15.12)
SKILL_ICON_IDS = (539, 540, 717, 724, 720, 716, 711, 723, 722, 718, 2248, 3778, 3780, 3784, 3783, 3781, 3782, 3779, 11162, 11193, 11194)

# Nro mobs drawn by the client (the training dummy Mộc nhân is mob template 0): SourceData/data/mob/x{N}/{id} is the Nro file (a byte for the reader kind,
# the frame data, the PNG atlas, see NroUnity Controller case 11 / EffectData.readData); it is cut here into one strip of equal frames per scale
# (data/mob/strip/x{N}/{id}.png) and one JSON in Nro pixels (data/mob/strip/{id}.json: frames, w, h of a frame, the feet at ax from the left and ay from the bottom)
MOB_IDS = (0, 8, 11, 25, 32, 43, 49, 50)       # Mộc nhân and the pets hatched by Đẻ trứng (SkillUtil.getTempMobMe)


# the auras (owner decision 2026-10-08): the pictures of tools/aura_planner/aura_levels.json, taken from the Nro folder img_by_name/x{N} (read only) into
# SourceData/data/aura/x{N}/ and packed per scale as "aura_x{N}"
AURA_TABLE = os.path.join(os.path.dirname(__file__), "aura_planner", "aura_levels.json")
NRO_IMAGES = os.path.join(ROOT, "Teamobi2026", "SRC", "data", "img_by_name")


def aura_files(z):
    """(name inside the zip, file on disk) for every aura picture at scale z; copies it from the Nro folder into SourceData when it is not there yet."""
    if not os.path.exists(AURA_TABLE):
        return []
    table = json.load(open(AURA_TABLE, encoding="utf-8"))
    names = sorted({"aura_%d_%d.png" % (a["id"], l["layer"]) for a in table["auras"] for l in a["layers"]})
    out_dir = os.path.join(SOURCE, "aura", "x%d" % z)
    os.makedirs(out_dir, exist_ok=True)
    entries = []
    for name in names:
        dst = os.path.join(out_dir, name)
        if not os.path.exists(dst) or Image.open(dst).mode != "P":          # not there yet, or a full-colour copy of an older run
            src = os.path.join(NRO_IMAGES, "x%d" % z, name)
            if not os.path.exists(src):
                print("  aura picture missing at x%d: %s" % (z, name))
                continue
            # 256 colours with transparency: the auras are pixel-art-like and the files become 5 times smaller (the download of the x3 set: 34 MB -> 6 MB)
            Image.open(src).convert("RGBA").quantize(colors=256, method=Image.FASTOCTREE, dither=Image.NONE).save(dst, "PNG", optimize=True)
        entries.append(("data/aura/x%d/%s" % (z, name), dst))
    return entries


THUMB_SIZE = 128


def aura_thumbs():
    """
    One small still picture per aura for the Aura tab (owner request 2026-10-08: static pictures, loaded fast): the frame with the most substance of the x2 picture
    (both layers together, the back one first, the way the game draws them, bottom centre on the feet), fitted into a 128 x 128 square. Not scale dependent,
    so they go into the "common" pack. Returns (name inside the zip, file on disk).
    """
    if not os.path.exists(AURA_TABLE):
        return []
    table = json.load(open(AURA_TABLE, encoding="utf-8"))
    out_dir = os.path.join(SOURCE, "aura", "thumb")
    os.makedirs(out_dir, exist_ok=True)
    entries = []
    for a in sorted(table["auras"], key=lambda x: x["id"]):
        if not a.get("enabled", True):
            continue
        dst = os.path.join(out_dir, "aura_%d.png" % a["id"])
        if not os.path.exists(dst):
            layers = []
            for l in sorted(a["layers"], key=lambda l: l["layer"]):
                im = Image.open(os.path.join(NRO_IMAGES, "x2", "aura_%d_%d.png" % (a["id"], l["layer"]))).convert("RGBA")
                n, h, w = l["frames"], im.height, im.width
                layers.append([im.crop((0, round(f * h / n), w, round((f + 1) * h / n))) for f in range(n)])
            count = min(len(fr) for fr in layers)

            def substance(f):
                return sum(fr[f].getchannel("A").point(lambda v: 255 if v > 64 else 0).histogram()[255] for fr in layers)

            best = max(range(count), key=substance)
            max_w = max(fr[best].width for fr in layers)
            max_h = max(fr[best].height for fr in layers)
            scale = min((THUMB_SIZE - 4) / max_w, (THUMB_SIZE - 4) / max_h, 1.0)
            canvas = Image.new("RGBA", (THUMB_SIZE, THUMB_SIZE), (0, 0, 0, 0))
            for fr in layers:
                pic = fr[best]
                small = pic.resize((max(1, round(pic.width * scale)), max(1, round(pic.height * scale))), Image.LANCZOS)
                canvas.alpha_composite(small, ((THUMB_SIZE - small.width) // 2, THUMB_SIZE - 2 - small.height))
            canvas.save(dst, "PNG", optimize=True)
        entries.append(("data/aura/thumb/aura_%d.png" % a["id"], dst))
    return entries


def read_fx_tables():
    """
    The skill effects of the Nro client (GameScr.readEfect / readDart / readSkill) from SourceData/data/update_data/{effect,dart,skill}:
    effects: [(id, [(image, dx, dy)])]; darts: [dict]; paints: {paint id: {"happen", "stand": [frame], "fly": [frame]}} with frame = 12 ints
    (effect 0 id, dx, dy, effect 1 id, dx, dy, effect 2 id, dx, dy, arrow id, dx, dy; the effect ids count from 1).
    """
    base = os.path.join(SOURCE, "update_data")

    class R:
        def __init__(self, name):
            self.b = open(os.path.join(base, name), "rb").read()
            self.p = 0

        def rd(self, fmt):
            v = struct.unpack_from(">" + fmt, self.b, self.p)[0]
            self.p += struct.calcsize(">" + fmt)
            return v
    r = R("effect")
    effects = []
    for _ in range(r.rd("h")):
        eid = r.rd("h")
        effects.append((eid, [(r.rd("h"), r.rd("b"), r.rd("b")) for _ in range(r.rd("b"))]))
    r = R("dart")
    darts = []
    for _ in range(r.rd("h")):
        d = {"id": r.rd("h"), "nUpdate": r.rd("h"), "va": r.rd("h") * 256, "xdPercent": r.rd("h")}

        def arr():
            return [r.rd("h") for _ in range(r.rd("h"))]

        def arr2():
            return [[r.rd("h") for _ in range(r.rd("h"))] for _ in range(r.rd("h"))]
        d["tail"] = arr()
        d["tailBorder"] = arr()
        d["xd1"] = arr()
        d["xd2"] = arr()
        d["head"] = arr2()
        d["headBorder"] = arr2()
        darts.append(d)
    r = R("skill")
    n = r.rd("h")
    paints = {}
    for _ in range(n):
        pid = r.rd("h")
        if pid == 1111:
            pid = n - 1
        happen = r.rd("h")
        r.rd("b")

        def frames():
            out = []
            for _ in range(r.rd("b")):
                r.rd("b")
                out.extend(r.rd("h") for _ in range(12))
            return out
        paints[pid] = {"id": pid, "happen": happen, "stand": frames(), "fly": frames()}
    return effects, darts, paints


def fx_image_ids():
    """Every small image id the skill effects and darts draw (they must be in the asset packs)."""
    if not os.path.exists(os.path.join(SOURCE, "update_data", "effect")):
        return set()
    effects, darts, _ = read_fx_tables()
    ids = set()
    for _, frames in effects:
        ids.update(i for i, _, _ in frames)
    for d in darts:
        for k in ("tail", "tailBorder", "xd1", "xd2"):
            ids.update(d[k])
        for k in ("head", "headBorder"):
            for row in d[k]:
                ids.update(row)
    ids.discard(0)
    return ids


def skill_fx():
    """
    The pictures of the skills (owner request 2026-10-09: the effects of each skill from the Nro data) as data/skill/skill_fx.json, flat arrays for Unity's JsonUtility:
    {"effects": [{"f": [image, dx, dy, ...]}] (the effect id counts from 1), "darts": [{"nUpdate", "va", "xd", "tail", "tailB", "xd1", "xd2", "head": [{"v": []}], "headB": [...]}],
    "paints": [{"id", "happen", "stand": [12 ints per painted frame], "fly": [...]}]}. The frames are aligned with the poses of skill_poses.json.
    """
    if not os.path.exists(os.path.join(SOURCE, "update_data", "effect")):
        return []
    effects, darts, paints = read_fx_tables()
    data = {
        "effects": [{"f": [v for t in frames for v in t]} for _, frames in effects],
        "darts": [{"nUpdate": d["nUpdate"], "va": d["va"], "xd": d["xdPercent"], "tail": d["tail"], "tailB": d["tailBorder"], "xd1": d["xd1"], "xd2": d["xd2"],
                   "head": [{"v": row} for row in d["head"]], "headB": [{"v": row} for row in d["headBorder"]]} for d in darts],
        "paints": [paints[k] for k in sorted(paints)],
    }
    out_dir = os.path.join(SOURCE, "skill")
    os.makedirs(out_dir, exist_ok=True)
    dst = os.path.join(out_dir, "skill_fx.json")
    with open(dst, "w", encoding="utf-8", newline=chr(10)) as f:
        json.dump(data, f, separators=(",", ":"))
    return [("data/skill/skill_fx.json", dst)]


def used_icon_ids():
    parts = json.load(open(os.path.join(DATA, "PartDatabase.json"), encoding="utf-8"))["parts"]
    costumes = json.load(open(os.path.join(DATA, "CostumeCatalog.json"), encoding="utf-8"))["costumes"]
    ids = set()
    for p in parts:
        for im in p["images"]:
            ids.add(im["iconId"])
    for c in costumes:
        ids.add(c["iconId"])
    ids.update(EXTRA_ICON_IDS)
    ids.update(SKILL_ICON_IDS)
    ids.update(fx_image_ids())
    return sorted(i for i in ids if i > 0)


def atlas_ids(ids):
    """Icon ids cut from Big0..Big4 (same test as NroSpriteResolver.ImageRecord.IsAtlasCandidate)."""
    b = open(os.path.join(DATA, "image.bytes"), "rb").read()
    n = (b[0] << 8) | b[1]
    inside = set()
    for i in range(min(n, 1 << 20)):
        o = 2 + i * 9
        big = b[o]
        x, y, w, h = struct.unpack(">hhhh", b[o + 1:o + 9])
        if big <= 4 and x >= 0 and y >= 0 and w > 0 and h > 0 and x + w <= 255 and y + h <= 255:
            inside.add(i)
    return [i for i in ids if i in inside]


def read_nro_mob(path):
    """(image infos {id: (x0, y0, w, h)} in Nro px, frames [[(dx, dy, image id)]], atlas PIL image) of an Nro mob file."""
    b = open(path, "rb").read()
    p = 0

    def rd(fmt):
        nonlocal p
        v = struct.unpack_from(">" + fmt, b, p)[0]
        p += struct.calcsize(">" + fmt)
        return v
    kind = rd("b")
    n = rd("i")
    data = b[p:p + n]
    p += n
    m = rd("i")
    png = b[p:p + m]
    q = 0

    def r2(fmt):
        nonlocal q
        v = struct.unpack_from(">" + fmt, data, q)[0]
        q += struct.calcsize(">" + fmt)
        return v
    infos = {}
    for _ in range(r2("b")):
        iid = r2("b")
        x0, y0 = (r2("h"), r2("h")) if kind not in (0, 1) else (r2("B"), r2("B"))
        infos[iid] = (x0, y0, r2("B"), r2("B"))
    frames = []
    for _ in range(r2("h")):
        frames.append([(r2("h"), r2("h"), r2("b")) for _ in range(r2("b"))])
    return infos, frames, Image.open(io.BytesIO(png)).convert("RGBA")


def mob_strips():
    """(name inside the zip, file on disk, scale) for the mob strips of every scale and their JSON (scale 0)."""
    out = []
    for mob in MOB_IDS:
        meta = None
        for z in SCALES:
            src = os.path.join(SOURCE, "mob", "x%d" % z, str(mob))
            if not os.path.exists(src):
                continue
            infos, frames, atlas = read_nro_mob(src)
            x0 = min(dx for f in frames for dx, dy, i in f)
            y0 = min(dy for f in frames for dx, dy, i in f)
            x1 = max(dx + infos[i][2] for f in frames for dx, dy, i in f)
            y1 = max(dy + infos[i][3] for f in frames for dx, dy, i in f)
            w, h = x1 - x0, y1 - y0
            strip = Image.new("RGBA", (w * z * len(frames), h * z), (0, 0, 0, 0))
            for k, f in enumerate(frames):
                for dx, dy, i in f:
                    ix, iy, iw, ih = infos[i]
                    piece = atlas.crop((ix * z, iy * z, (ix + iw) * z, (iy + ih) * z))
                    strip.alpha_composite(piece, ((k * w + dx - x0) * z, (dy - y0) * z))
            dst_dir = os.path.join(SOURCE, "mob", "strip", "x%d" % z)
            os.makedirs(dst_dir, exist_ok=True)
            dst = os.path.join(dst_dir, "%d.png" % mob)
            strip.save(dst, "PNG", optimize=True)
            out.append(("data/mob/strip/x%d/%d.png" % (z, mob), dst, z))
            meta = {"id": mob, "frames": len(frames), "w": w, "h": h, "ax": -x0, "ay": y1}      # Nro draws the mob with its origin (0, 0) on the ground
        if meta:
            dst = os.path.join(SOURCE, "mob", "strip", "%d.json" % mob)
            with open(dst, "w", encoding="utf-8", newline="\n") as f:
                json.dump(meta, f)
            out.append(("data/mob/strip/%d.json" % mob, dst, 0))
    return out


def skill_poses():
    """
    The poses of every skill as the Nro client plays them (owner request 2026-10-08: the punches need their kicks, the charge skills their own frames).
    Source: SourceData/data/update_data/skill (copied from Teamobi2026 data/update_data/skill, read like GameScr.readSkill of NroUnity: per paint id the
    "stand" and "fly" lists of SkillInfoPaint, whose status is the pose of the Nro table shown for one painted frame) and SourceData/tables/json/skill_template.json
    (skills[level-1].id = the paint id of that level). Writes data/skill/skill_poses.json (lists, for Unity's JsonUtility):
    {"skills": [{"id": template id, "paints": [paint id of level 1, 2, ...]}], "paints": [{"id": paint id, "stand": [poses], "fly": [poses]}]}.
    """
    src = os.path.join(SOURCE, "update_data", "skill")
    tpl = os.path.join(CLIENT, "SourceData", "tables", "json", "skill_template.json")
    if not (os.path.exists(src) and os.path.exists(tpl)):
        return []
    b = open(src, "rb").read()
    p = 0

    def rd(fmt):
        nonlocal p
        v = struct.unpack_from(">" + fmt, b, p)[0]
        p += struct.calcsize(">" + fmt)
        return v
    n = rd("h")
    paints = {}
    for _ in range(n):
        pid = rd("h")
        if pid == 1111:
            pid = n - 1
        rd("h")                                            # effectHappenOnMob
        rd("b")                                            # numEff

        def infos():
            out = []
            for _ in range(rd("b")):
                out.append(rd("b"))
                for _ in range(12):
                    rd("h")                                # effects and arrows (stage 2)
            return out
        paints[pid] = {"id": pid, "stand": infos(), "fly": infos()}
    skills = []
    for row in json.load(open(tpl, encoding="utf-8"))["rows"]:
        levels = row["skills"] if isinstance(row["skills"], list) else ast.literal_eval(row["skills"])
        skills.append({"id": int(row["id"]), "paints": [lv["id"] for lv in levels]})
    out_dir = os.path.join(SOURCE, "skill")
    os.makedirs(out_dir, exist_ok=True)
    dst = os.path.join(out_dir, "skill_poses.json")
    with open(dst, "w", encoding="utf-8", newline="\n") as f:
        json.dump({"skills": skills, "paints": [paints[k] for k in sorted(paints)]}, f, separators=(",", ":"))
    return [("data/skill/skill_poses.json", dst)]


def write_zip(path, entries):
    """entries: list of (name inside the zip, file on disk). Deterministic: sorted, fixed timestamps."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for name, src in sorted(entries):
            info = zipfile.ZipInfo(name, FIXED_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            with open(src, "rb") as f:
                z.writestr(info, f.read())


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(ROOT, "BattleBallServer", "web", "assets"))
    out = ap.parse_args().out
    packs_dir = os.path.join(out, "packs")
    if os.path.isdir(packs_dir):
        shutil.rmtree(packs_dir)

    ids = used_icon_ids()
    atlas = set(atlas_ids(ids))
    loose = [i for i in ids if i not in atlas]
    print("icons used:", len(ids), "| from atlases:", len(atlas), "| loose files:", len(loose))

    packs = []

    def add(pack_id, scale, entries):
        rel = "packs/%s.zip" % pack_id
        path = os.path.join(out, rel)
        write_zip(path, entries)
        packs.append({"id": pack_id, "scale": scale, "url": rel, "size": os.path.getsize(path), "sha256": sha256(path)})
        print("  %-18s %5d files %8.2f MB" % (pack_id, len(entries), os.path.getsize(path) / 1e6))

    # the map chunk of the starter village (tools/build_village_map.py): chunk.json for every scale, the pictures per scale
    map_dir = os.path.join(SOURCE, "map", "village_start")
    common = [("data/update_data/image", os.path.join(DATA, "image.bytes"))]
    if os.path.isdir(map_dir):
        common.append(("data/map/village_start/chunk.json", os.path.join(map_dir, "chunk.json")))
    common.extend(aura_thumbs())
    common.extend(skill_poses())
    common.extend(skill_fx())
    # the sounds of the skills (Nro res/sound, numbered as SoundMn of Nro): punches, beams, gong, blasts, the three ultimates
    snd_dir = os.path.join(SOURCE, "sound")
    if os.path.isdir(snd_dir):
        common.extend(("data/sound/" + n, os.path.join(snd_dir, n)) for n in sorted(os.listdir(snd_dir)) if n.endswith(".wav"))
    # the weather of the village (Nro bg/light.png: the sunbeam, bg/fog0.png: the clouds)
    weather_dir = os.path.join(SOURCE, "weather")
    if os.path.isdir(weather_dir):
        common.extend(("data/weather/" + n, os.path.join(weather_dir, n)) for n in sorted(os.listdir(weather_dir)) if n.endswith(".png"))
    # the pictures of the three ultimates (Nro Effect_End, res/x4/e/e_9 to e_28: auras, gong, beam, energy balls, jar), one scale, the client shrinks them
    fx_dir = os.path.join(SOURCE, "effect_end")
    if os.path.isdir(fx_dir):
        common.extend(("data/effect_end/" + n, os.path.join(fx_dir, n)) for n in sorted(os.listdir(fx_dir)) if n.endswith(".png"))
    mobs = mob_strips()
    common.extend((n, f) for n, f, z in mobs if z == 0)
    add("common", 0, common)
    for z in SCALES:
        entries = []
        src_scale = 2 if z == 1 else z          # x1 has no atlas files: its atlases are the x2 ones (shrunk by the client)
        for k in range(5):
            f = os.path.join(SOURCE, "res", "x%d" % src_scale, "Big%d" % k)
            if os.path.exists(f):
                entries.append(("data/res/x%d/Big%d" % (src_scale, k), f))
        add("x1_atlas_from_x2" if z == 1 else "x%d_atlas" % z, z, entries)
        have = [i for i in loose if os.path.exists(os.path.join(SOURCE, "icon", "x%d" % z, "%d.png" % i))]
        if len(have) != len(loose):
            print("  x%d misses %d loose icons (the client falls back to another scale)" % (z, len(loose) - len(have)))
        if z == 1 and len(have) != len(loose):
            missing = [i for i in loose if i not in set(have) and os.path.exists(os.path.join(SOURCE, "icon", "x2", "%d.png" % i))]
            if missing:
                add("x1_icons_fallback", 1, [("data/icon/x2/%d.png" % i, os.path.join(SOURCE, "icon", "x2", "%d.png" % i)) for i in missing])
        if os.path.isdir(map_dir):
            pics = []
            for part, ext in (("ground_a", "png"), ("ground_b", "png"), ("background_a", "jpg"), ("background_b", "jpg")):
                f = os.path.join(map_dir, "%s_x%d.%s" % (part, z, ext))
                if os.path.exists(f):
                    pics.append(("data/map/village_start/%s_x%d.%s" % (part, z, ext), f))
            bg = os.path.join(map_dir, "bg", "x%d" % z)
            if os.path.isdir(bg):
                for name in sorted(os.listdir(bg)):
                    pics.append(("data/map/village_start/bg/x%d/%s" % (z, name), os.path.join(bg, name)))
            add("map_village_x%d" % z, z, pics)
        mob_pics = [(n, f) for n, f, sz in mobs if sz == z]
        if mob_pics:
            add("mob_x%d" % z, z, mob_pics)
        auras = aura_files(z)
        if auras:
            add("aura_x%d" % z, z, auras)
        for n in range(0, len(have), CHUNK):
            chunk = have[n:n + CHUNK]
            add("x%d_icons_%03d" % (z, n // CHUNK),
                z, [("data/icon/x%d/%d.png" % (z, i), os.path.join(SOURCE, "icon", "x%d" % z, "%d.png" % i)) for i in chunk])

    packs.sort(key=lambda p: (p["scale"], p["id"]))
    digest = hashlib.sha256("".join(p["id"] + p["sha256"] for p in packs).encode()).hexdigest()[:16]
    manifest = {"version": digest, "packs": packs}
    with open(os.path.join(out, "manifest.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=1)
    total = {z: sum(p["size"] for p in packs if p["scale"] in (0, z)) for z in SCALES}
    print("version", digest, "| download size per scale (MB):", {z: round(v / 1e6, 1) for z, v in total.items()})


if __name__ == "__main__":
    main()
