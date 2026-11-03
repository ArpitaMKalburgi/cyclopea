"""
Make the unscreenshottable visible, for the trailer and the gallery.

A Cyclopea creature exists only as a difference between the two eyes' images, so any single
recording (MQDH cast, phone, screenshot) shows plain static. Given a left and right image of the
same moment (XR Operator per-eye captures on the headset, or the offline renders in
Tools/captures), this writes:

  <stem>_anaglyph.png  red/cyan stereo: viewers with red/cyan glasses see the creature float
  <stem>_reveal.png    the static in grey with the creature outlined where the eyes disagree
                       (works with no glasses: "this is what your two eyes compare")
  <stem>_sbs.png       left | right side by side (cross-eyed / parallel viewing)

    python -I Tools/reveal.py Tools/device-eyes/left.png Tools/device-eyes/right.png --out Tools/reveal
    python -I Tools/reveal.py --demo        # from the offline 1920 arcsec captures
"""
import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

def load(p):
    return np.asarray(Image.open(p).convert("L"), dtype=np.float32) / 255.0

def best_shift(l, r, max_shift):
    """Whole-image horizontal shift that best aligns R to L (head-locked captures carry scene parallax)."""
    h, w = l.shape
    rows = slice(h // 4, 3 * h // 4)
    best, best_err = 0, float("inf")
    for s in range(-max_shift, max_shift + 1):
        a = l[rows, max_shift:w - max_shift]
        b = r[rows, max_shift + s:w - max_shift + s]
        err = float(np.mean((a - b) ** 2))
        if err < best_err:
            best, best_err = s, err
    return best

def reveal(left_path, right_path, out_dir, stem, align):
    l, r = load(left_path), load(right_path)
    if l.shape != r.shape:
        raise SystemExit(f"size mismatch {l.shape} vs {r.shape}")
    shift = best_shift(l, r, min(80, l.shape[1] // 8)) if align else 0
    r_al = np.roll(r, -shift, axis=1)

    anaglyph = np.stack([l, r_al, r_al], axis=-1)
    diff = np.abs(l - r_al)
    # Smooth the per-pixel disagreement into a region, then outline it.
    w = l.shape[1]
    d_img = Image.fromarray((np.clip(diff * 3, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(radius=max(3, w // 40)))
    d = np.asarray(d_img, dtype=np.float32) / 255.0
    mask = d > max(0.06, 0.35 * float(d.max()))
    k = max(3, (w // 64) | 1)   # odd kernel: close the gaps between dots into one silhouette
    m_img = Image.fromarray(mask.astype(np.uint8) * 255).filter(ImageFilter.MaxFilter(k)).filter(ImageFilter.MinFilter(k))
    mask = np.asarray(m_img) > 127
    edge = mask ^ (np.asarray(Image.fromarray(mask.astype(np.uint8) * 255).filter(ImageFilter.MinFilter(5))) > 127)
    base = 0.35 + 0.4 * l
    rv = np.stack([base, base, base], axis=-1)
    rv[mask] = rv[mask] * 0.55 + np.array([0.25, 0.55, 0.35]) * 0.45
    rv[edge] = np.array([0.45, 1.0, 0.6])

    out = Path(out_dir); out.mkdir(parents=True, exist_ok=True)
    save = lambda a, name: Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8)).save(out / f"{stem}_{name}.png")
    save(anaglyph, "anaglyph")
    save(rv, "reveal")
    save(np.concatenate([l, np.ones((l.shape[0], 8), np.float32), r], axis=1), "sbs")
    frac = float(mask.mean())
    print(f"{stem}: scene shift {shift}px, disagreement covers {frac:.1%} of the frame -> {out}")
    return frac

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("left", nargs="?")
    ap.add_argument("right", nargs="?")
    ap.add_argument("--out", default="Tools/reveal")
    ap.add_argument("--stem", default="frame")
    ap.add_argument("--no-align", action="store_true", help="skip whole-image alignment (offline renders)")
    ap.add_argument("--demo", action="store_true")
    a = ap.parse_args()
    if a.demo:
        caps = Path("Tools/captures")
        for seed in ("00", "01"):
            reveal(caps / f"1024_1920_{seed}_L.png", caps / f"1024_1920_{seed}_R.png", a.out, f"demo1920_{seed}", False)
        reveal(caps / "1024_0_00_L.png", caps / "1024_0_00_R.png", a.out, "demo0_control", False)
        return
    if not (a.left and a.right):
        ap.error("give LEFT and RIGHT images, or --demo")
    reveal(a.left, a.right, a.out, a.stem, not a.no_align)

if __name__ == "__main__":
    main()
