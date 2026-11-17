#!/usr/bin/env python3
"""
make_icon.py - Cyclopea's app icon, drawn from the game's own look (Pillow only).

    python -I Tools/make_icon.py

Writes:
  Assets/Cyclopea/Art/icon.png     1024 x 1024, the app icon (Player Settings uses it)
  Tools/art/icon-512.png           512 x 512, for the dashboard
  Tools/art/cover-landscape.png    2560 x 1440, cover art for the invite page

The mark is the game itself: a field of cream dots on deep blue (the static), and a one-eyed critter that is
made of nothing but brighter dots of the same field. No text in the icon; the covers add the name in
Bahnschrift from Windows (static images only, no font shipped).
"""
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
BG_TOP, BG_BOTTOM = (18, 28, 64), (8, 12, 34)
CREAM, DIM = (255, 244, 214), (120, 132, 176)
GOLD = (255, 205, 96)


def background(w, h):
    img = Image.new("RGB", (w, h))
    px = img.load()
    for y in range(h):
        t = y / max(1, h - 1)
        c = tuple(int(BG_TOP[i] + (BG_BOTTOM[i] - BG_TOP[i]) * t) for i in range(3))
        for x in range(w):
            px[x, y] = c
    return img


def in_critter(x, y, cx, cy, r):
    """A round body with two small ears: the critter's outline, in units of r."""
    dx, dy = (x - cx) / r, (y - cy) / r
    body = dx * dx + (dy * 1.08) ** 2 <= 1.0
    ear_l = (dx + 0.62) ** 2 + (dy + 0.86) ** 2 <= 0.11
    ear_r = (dx - 0.62) ** 2 + (dy + 0.86) ** 2 <= 0.11
    return body or ear_l or ear_r


def mark(img, cx, cy, r, seed=7, step=None):
    """Dots everywhere; inside the critter they are brighter and larger. One eye, looking back."""
    w, h = img.size
    step = step or max(10, int(r / 11))
    rng = random.Random(seed)
    d = ImageDraw.Draw(img, "RGBA")
    for gy in range(0, h + step, step):
        for gx in range(0, w + step, step):
            x = gx + rng.uniform(-0.38, 0.38) * step
            y = gy + rng.uniform(-0.38, 0.38) * step
            inside = in_critter(x, y, cx, cy, r)
            if not inside and rng.random() < 0.42:
                continue
            rad = step * (0.30 if inside else 0.17) * rng.uniform(0.8, 1.15)
            col = CREAM if inside else DIM
            a = 255 if inside else int(150 * rng.uniform(0.5, 1.0))
            d.ellipse((x - rad, y - rad, x + rad, y + rad), fill=col + (a,))
    # the eye: one disc, dark with a bright ring and a glint
    er = r * 0.34
    ex, ey = cx + r * 0.06, cy - r * 0.10
    d.ellipse((ex - er, ey - er, ex + er, ey + er), fill=(10, 14, 36, 255), outline=CREAM + (255,), width=max(3, int(r * 0.035)))
    pr = er * 0.52
    d.ellipse((ex - pr + er * 0.12, ey - pr, ex + pr + er * 0.12, ey + pr), fill=GOLD + (255,))
    gr = er * 0.16
    d.ellipse((ex + er * 0.30 - gr, ey - er * 0.34 - gr, ex + er * 0.30 + gr, ey - er * 0.34 + gr), fill=(255, 255, 255, 255))


def font(size):
    for name in ("bahnschrift.ttf", "segoeuib.ttf", "arialbd.ttf"):
        p = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts", name)
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def icon():
    img = background(1024, 1024)
    mark(img, 512, 540, 330)
    return img


def cover(w, h):
    img = background(w, h)
    mark(img, int(w * 0.30), int(h * 0.52), int(h * 0.30), step=int(h / 34))
    d = ImageDraw.Draw(img)
    d.text((int(w * 0.54), int(h * 0.36)), "Cyclopea", font=font(int(h * 0.15)), fill=CREAM)
    d.text((int(w * 0.545), int(h * 0.56)), "Critters only two eyes can see.", font=font(int(h * 0.045)), fill=DIM)
    return img


def main():
    os.makedirs(os.path.join(ROOT, "Assets", "Cyclopea", "Art"), exist_ok=True)
    os.makedirs(os.path.join(HERE, "art"), exist_ok=True)
    i = icon()
    i.save(os.path.join(ROOT, "Assets", "Cyclopea", "Art", "icon.png"))
    i.resize((512, 512), Image.LANCZOS).save(os.path.join(HERE, "art", "icon-512.png"))
    cover(2560, 1440).save(os.path.join(HERE, "art", "cover-landscape.png"))
    print("wrote icon.png, icon-512.png, cover-landscape.png")


if __name__ == "__main__":
    main()
