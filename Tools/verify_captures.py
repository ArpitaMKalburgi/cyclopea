"""Verify the offline per-eye captures written by CyclopeaSetup.CaptureEyes.

Usage:  python -I Tools/verify_captures.py [Tools/captures] [--out Tools/captures-report.md]

Two questions, answered from the rendered pixels rather than from the shader source:

1. Does each eye get the commanded disparity?
   Inside the creature (a square inscribed in its outline) the right-eye image must be the
   left-eye image shifted by the commanded amount; outside the creature the two eyes must be
   identical. Shift is measured by phase correlation refined with an iterative sub-pixel
   (Lucas-Kanade) step.

2. Can one eye alone find the creature?
   A monocular observer with privileged knowledge (it is told each square's outline) measures
   dot density and edge energy along the outline and inside it, and in a 4-choice trial picks the
   square that looks most different from plain static. Run on single-eye images: if its accuracy
   sits at chance (25 %), the creature carries no first-order monocular cue.
"""
import argparse
import json
import math
import os
import sys

import numpy as np
from PIL import Image

sys.stdout.reconfigure(encoding="utf-8")


def load_gray(path):
    return np.asarray(Image.open(path).convert("L"), dtype=np.float64) / 255.0


def uv_grid(n):
    c = (np.arange(n) + 0.5) / n
    u, v = np.meshgrid(c, 1.0 - c)  # PNG row 0 is the top of the quad (v = 1)
    return u, v


def mask_sdf(entry, n):
    u, v = uv_grid(n)
    cx, cy = entry["mask_center"]
    dx, dy = u - cx, v - cy
    ang = np.arctan2(dy, dx)
    r = entry["mask_radius"] * (1 + entry["mask_wobble"] * np.sin(entry["mask_lobes"] * ang + entry["mask_phase"]))
    return np.hypot(dx, dy) - r


def fourier_shift_rows(img, s):
    """Shift every row right by s pixels (exact sub-pixel shift for band-limited rows, periodic)."""
    f = np.fft.fft(img, axis=1)
    kx = np.fft.fftfreq(img.shape[1])
    return np.fft.ifft(f * np.exp(-2j * np.pi * kx * s)[None, :], axis=1).real


def measure_shift(L, R, rows, cols):
    """Shift s (px, positive = right) such that R ~= L shifted by s, scored on the crop only.

    Integer search over +/-80 px, then a 0.005 px grid search with exact Fourier shifts of whole
    rows (the crop sits far from the image edges, so periodic wrap never reaches it), then a
    parabola through the three best points.
    """
    a = L[rows, :]
    b = R[rows, :][:, cols]
    ssd = lambda sh: float(np.mean((fourier_shift_rows(a, sh)[:, cols] - b) ** 2))
    k = min(range(-80, 81), key=lambda kk: float(np.mean((np.roll(a, kk, axis=1)[:, cols] - b) ** 2)))
    grid = k + np.arange(-1.0, 1.0001, 0.005)
    vals = np.array([ssd(g) for g in grid])
    i = int(np.argmin(vals))
    if 0 < i < len(grid) - 1:
        y0, y1, y2 = vals[i - 1], vals[i], vals[i + 1]
        den = y0 - 2 * y1 + y2
        return float(grid[i] + (0.5 * (y0 - y2) / den * 0.005 if den > 0 else 0.0))
    return float(grid[i])


def shift_check(entries, root):
    rows = []
    for e in entries:
        n = e["size"]
        L = load_gray(os.path.join(root, e["stem"] + "_L.png"))
        R = load_gray(os.path.join(root, e["stem"] + "_R.png"))
        sdf = mask_sdf(e, n)
        cell = 0.05  # UV, matches StereoPatch.CellSizeM / 0.12
        outside = sdf > cell * 2
        diff_out = float(np.abs(L - R)[outside].mean())
        # Square inscribed in the smallest possible outline radius.
        cx, cy = e["mask_center"]
        half = 0.10
        x0, x1 = int((cx - half) * n), int((cx + half) * n)
        y0, y1 = int((1 - cy - half) * n), int((1 - cy + half) * n)
        shift = measure_shift(L, R, slice(y0, y1), slice(x0, x1))
        expect = -e["expect_px"]  # right eye shows the content shifted left by the full offset
        rows.append((n, e["arcsec"], expect, shift, shift - expect, diff_out))
    return rows


def features(img, sdf, n):
    cell = 0.05
    band = np.abs(sdf) < cell * 0.75
    inside = sdf < -cell
    ring = (sdf > cell) & (sdf < cell * 4)
    gy, gx = np.gradient(img)
    energy = gx * gx + gy * gy
    return np.array([
        img[band].mean() - img[ring].mean(),
        img[inside].mean() - img[ring].mean(),
        energy[band].mean() - energy[ring].mean(),
        img[band].std() - img[ring].std(),
    ])


def mono_check(entries, root, size, level, rng):
    """4-AFC monocular observer: targets at `level`, distractors at 0 arcsec (plain static with an outline)."""
    tgt = [e for e in entries if e["size"] == size and e["arcsec"] == level]
    nul = [e for e in entries if e["size"] == size and e["arcsec"] == 0]
    out = {}
    for eye in ("L", "R"):
        ft = np.array([features(load_gray(os.path.join(root, e["stem"] + f"_{eye}.png")), mask_sdf(e, size), size) for e in tgt])
        fn = np.array([features(load_gray(os.path.join(root, e["stem"] + f"_{eye}.png")), mask_sdf(e, size), size) for e in nul])
        mu, sd = fn.mean(0), fn.std(0) + 1e-12
        # Effect size of each feature, target vs plain static.
        d = (ft.mean(0) - mu) / np.sqrt((ft.var(0) + fn.var(0)) / 2 + 1e-24)
        # Observer: Mahalanobis-like distance from plain static; pick the most unusual of four.
        score = lambda f: float(np.sum(((f - mu) / sd) ** 2))
        trials, hits = 4000, 0
        for _ in range(trials):
            t = ft[rng.integers(len(ft))]
            ds = fn[rng.choice(len(fn), 3, replace=False)]
            s_t = score(t)
            hits += s_t > max(score(x) for x in ds)
        acc = hits / trials
        se = math.sqrt(0.25 * 0.75 / len(ft))  # sampling error is dominated by the 24 target images
        out[eye] = (acc, d, len(ft), len(fn), se)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("root", nargs="?", default="Tools/captures")
    ap.add_argument("--out", default="Tools/captures-report.md")
    args = ap.parse_args()
    entries = json.load(open(os.path.join(args.root, "manifest.json"), encoding="utf-8"))
    rng = np.random.default_rng(11)

    lines = ["# Offline per-eye capture check", "",
             "Rendered by `CyclopeaSetup.CaptureEyes` with the production shader, forcing each eye. "
             "Patch 12 cm at 60 cm. 1024 px = math check; 288 px = roughly the pixels a 12 cm patch covers on Quest 3 at 60 cm.", "",
             "## 1. Each eye gets the commanded disparity", "",
             "| px | arcsec | expected shift (px) | measured (px) | error (px) | mean abs L-R outside creature |",
             "|---|---|---|---|---|---|"]
    rows = shift_check(entries, args.root)
    summary = {}
    for n, a, exp, got, err, dout in rows:
        summary.setdefault((n, a), []).append((exp, got, err, dout))
    worst = 0.0
    for (n, a), v in sorted(summary.items(), key=lambda kv: (kv[0][0], -kv[0][1])):
        v = np.array(v)
        err_rms = float(np.sqrt(np.mean(v[:, 2] ** 2)))
        worst = max(worst, float(np.max(np.abs(v[:, 2]))) / max(1e-9, abs(v[0, 0])) if a > 0 else 0)
        lines.append(f"| {n} | {a:.0f} | {v[0,0]:+.3f} | {v[:,1].mean():+.3f} ± {v[:,1].std():.3f} | rms {err_rms:.3f} | {v[:,3].mean():.4f} |")
    lines += ["", "## 2. One eye alone cannot find the creature", "",
              "Observer told each square's outline; picks, in 4-choice trials, the square whose outline region looks least like plain static. "
              "Chance = 25 %. Feature effect sizes (Cohen's d, target vs plain static): band density, interior density, band edge energy, band texture SD.", "",
              "| px | arcsec | eye | observer accuracy | effect sizes d | n target / n static |", "|---|---|---|---|---|---|"]
    verdict = True
    for size in sorted({e["size"] for e in entries}):
        for level in (1920, 15):
            if not any(e["size"] == size and e["arcsec"] == level for e in entries):
                continue
            res = mono_check(entries, args.root, size, level, rng)
            for eye, (acc, d, nt, nn, se) in res.items():
                flag = "" if acc < 0.25 + 2.5 * se else "  **above chance**"
                verdict &= flag == ""
                lines.append(f"| {size} | {level} | {eye} | {acc*100:.1f} %{flag} | {', '.join(f'{x:+.2f}' for x in d)} | {nt} / {nn} |")
    lines += ["", f"Verdict: {'no monocular cue detected by this observer' if verdict else 'a monocular cue is detectable; see rows marked above chance'}.", ""]
    report = "\n".join(lines)
    with open(args.out, "w", encoding="utf-8") as f:
        f.write(report)
    print(report)


if __name__ == "__main__":
    main()
