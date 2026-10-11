"""
Makes the backdrops of the dungeon arenas (docs/architecture/BattleBallServer.md 15.34, owner request 2026-10-12: "thay doi background cho map pho ban, quet qua
...\\Desktop\\data\\map\\map2 de lay data").

Reads the folder given as the first argument (default: the owner's Desktop\\data\\map\\map2):
  * the pictures straight in the folder are full space backdrops (1916 x 821): written as arena_bg0.jpg, arena_bg1.jpg, ... (JPEG, they have no alpha)
  * the pictures in its subfolder (the moving objects) are sheets of objects on a transparent background (asteroids, planets, black holes):
    every separate object is cut out (connected pieces of the alpha channel) and written as arena_obj0.png, arena_obj1.png, ... (at most 480 px on the long side)
Output goes to BattleBall/Assets/BattleNet/UI/Sprites (the builder BbLoginSceneBuilder imports them as sprites and gives them to BbVillageView).
Needs only Pillow. The names are numbered in the order of the sorted file names, so the same folder always gives the same result.
"""
import os
import sys
from collections import deque

from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, '..', '..', 'Assets', 'BattleNet', 'UI', 'Sprites'))
DEFAULT_SRC = r'C:\Users\cuonghayho\Desktop\data\map\map2'
MAX_OBJECT = 480
MIN_AREA = 0.004          # a piece smaller than this share of the sheet is a speck, not an object


def cut_objects(sheet):
    """The bounding boxes (in sheet pixels) of the separate objects of a sheet with a transparent background, biggest first."""
    alpha = sheet.getchannel('A')
    step = 6
    small = alpha.resize((sheet.width // step, sheet.height // step), Image.BILINEAR).point(lambda v: 255 if v > 24 else 0)
    small = small.filter(ImageFilter.MaxFilter(5))                       # joins the broken parts of one object (rocks around an asteroid, the rim of a black hole)
    w, h = small.size
    px = small.load()
    seen = [[False] * w for _ in range(h)]
    boxes = []                                                            # (pieces, (x0, y0, x1, y1), mask of the piece at full size)
    for y in range(h):
        for x in range(w):
            if px[x, y] == 0 or seen[y][x]:
                continue
            queue = deque([(x, y)])
            seen[y][x] = True
            x0 = x1 = x
            y0 = y1 = y
            count = 0
            members = []
            while queue:
                cx, cy = queue.popleft()
                members.append((cx, cy))
                count += 1
                x0, x1, y0, y1 = min(x0, cx), max(x1, cx), min(y0, cy), max(y1, cy)
                for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                    if 0 <= nx < w and 0 <= ny < h and px[nx, ny] and not seen[ny][nx]:
                        seen[ny][nx] = True
                        queue.append((nx, ny))
            if count >= MIN_AREA * w * h:
                mask = Image.new('L', (w, h), 0)
                mpx = mask.load()
                for mx, my in members:
                    mpx[mx, my] = 255
                mask = mask.resize((w * step, h * step), Image.NEAREST).filter(ImageFilter.GaussianBlur(step / 2))
                boxes.append((count, (x0 * step, y0 * step, (x1 + 1) * step, (y1 + 1) * step), mask))
    boxes.sort(key=lambda b: -b[0])
    return [(b[1], b[2]) for b in boxes]


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_SRC
    os.makedirs(OUT, exist_ok=True)
    backdrops = sorted(f for f in os.listdir(src) if os.path.isfile(os.path.join(src, f)) and f.lower().endswith(('.png', '.jpg', '.jpeg')))
    for i, name in enumerate(backdrops):
        im = Image.open(os.path.join(src, name)).convert('RGB')
        target = os.path.join(OUT, 'arena_bg%d.jpg' % i)
        im.save(target, 'JPEG', quality=86, optimize=True)
        print('backdrop %d: %s %s -> %s (%d KB)' % (i, im.size, name.encode('ascii', 'replace').decode(), os.path.basename(target), os.path.getsize(target) // 1024))
    objects_dir = None
    for entry in sorted(os.listdir(src)):
        if os.path.isdir(os.path.join(src, entry)):
            objects_dir = os.path.join(src, entry)
    n = 0
    if objects_dir:
        for name in sorted(os.listdir(objects_dir)):
            if not name.lower().endswith('.png'):
                continue
            sheet = Image.open(os.path.join(objects_dir, name)).convert('RGBA')
            found = cut_objects(sheet)
            pieces = []
            for box, mask in found:
                if box[0] < 150 and box[2] - box[0] > 0.4 * sheet.width and sheet.size == (1916, 821):
                    # the two planets of the sheet touch (the ring of one lies over the other): they are cut by hand into the two planets
                    for part in ((40, 20, 530, 470), (330, 340, 1050, 790)):
                        pieces.append((part, 'pair'))
                else:
                    pieces.append((box, mask))
            # top to bottom, then left to right inside a sheet, so that the numbering does not depend on the size of the pieces
            pieces.sort(key=lambda b: (round((b[0][1] + b[0][3]) / 2 / (sheet.height / 2)), b[0][0]))
            for (x0, y0, x1, y1), mask in pieces:
                pad = 8
                box = (max(0, x0 - pad), max(0, y0 - pad), min(sheet.width, x1 + pad), min(sheet.height, y1 + pad))
                piece = sheet.crop(box)
                if mask == 'pair':
                    # the upper left planet is a circle, the lower right one (with its ring) is what lies below y 350 and outside the first circle
                    keep = Image.new('L', sheet.size, 0)
                    draw = ImageDraw.Draw(keep)
                    if x0 < 100:
                        draw.ellipse((285 - 222, 245 - 222, 285 + 222, 245 + 222), fill=255)
                    else:
                        draw.rectangle((330, 350, 1050, 790), fill=255)
                        draw.ellipse((285 - 240, 245 - 240, 285 + 240, 245 + 240), fill=0)
                        draw.rectangle((860, 340, 1050, 470), fill=0)               # the corner of a black hole of the same sheet
                    piece.putalpha(Image.composite(piece.getchannel('A'), Image.new('L', piece.size, 0), keep.crop(box)))
                elif mask is not None:                                     # nothing of the neighbours that reaches into the box
                    alpha = Image.composite(piece.getchannel('A'), Image.new('L', piece.size, 0), mask.crop(box))
                    piece.putalpha(alpha)
                scale = min(1.0, MAX_OBJECT / max(piece.size))
                if scale < 1.0:
                    piece = piece.resize((max(1, round(piece.width * scale)), max(1, round(piece.height * scale))), Image.LANCZOS)
                target = os.path.join(OUT, 'arena_obj%d.png' % n)
                piece.save(target, optimize=True)
                print('object %d: %s from a sheet %s -> %s (%d KB)' % (n, piece.size, sheet.size, os.path.basename(target), os.path.getsize(target) // 1024))
                n += 1
    print('%d backdrops, %d objects' % (len(backdrops), n))


if __name__ == '__main__':
    main()
