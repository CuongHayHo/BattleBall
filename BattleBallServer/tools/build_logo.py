"""Builds web/logo_run.png: the animated logo of the web pages (the costume "ct dep vcl" running), five frames side by side.

Input: the five transparent frames BattleBall\\Logs\\logo_run\\run_2.png .. run_6.png (poses Run_1..Run_5), made by
  Unity -batchmode -quit -executeMethod NroRig.EditorTools.NroPoseSheetCapture.CaptureRunLogoBatch   (do not add -nographics: nothing is drawn then)
Output: web/logo_run.png (all frames cropped to the same box so the feet stay in place) and the frame size on the console, which
web/style.css (.runner) must match: FRAMES frames of W x H pixels, shown at half size for sharp pixels on high-DPI screens.
  python tools/build_logo.py
"""
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, '..', '..', 'BattleBall', 'Logs', 'logo_run')
OUT = os.path.join(HERE, '..', 'web', 'logo_run.png')
POSES = [2, 3, 4, 5, 6]
MARGIN = 6

frames = [Image.open(os.path.join(SRC, 'run_%d.png' % p)).convert('RGBA') for p in POSES]
boxes = [f.getchannel('A').getbbox() for f in frames]
if any(b is None for b in boxes):
    sys.exit('an empty frame: was the capture run with -nographics?')
x0 = min(b[0] for b in boxes) - MARGIN
y0 = min(b[1] for b in boxes) - MARGIN
x1 = max(b[2] for b in boxes) + MARGIN
y1 = max(b[3] for b in boxes) + MARGIN
w, h = x1 - x0, y1 - y0
w += w % 2
h += h % 2           # even size: it is shown at half size
sheet = Image.new('RGBA', (w * len(frames), h), (0, 0, 0, 0))
for i, f in enumerate(frames):
    sheet.paste(f.crop((x0, y0, x0 + w, y0 + h)), (i * w, 0))
sheet.save(OUT, optimize=True)
print('frames=%d frame=%dx%d sheet=%s (%d bytes)' % (len(frames), w, h, os.path.normpath(OUT), os.path.getsize(OUT)))
