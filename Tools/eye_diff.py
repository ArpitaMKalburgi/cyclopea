"""Verify per-eye rendering of a random-dot stereogram from two eye captures.

Usage:  python eye_diff.py left.png right.png [--expect-shift PX] [--mask x0 y0 x1 y1]

Checks:
  1. Outside the creature mask the two eyes must be (near) identical.
  2. Inside the mask the right eye must be the left eye shifted horizontally by
     the expected number of pixels (sub-pixel, via phase correlation).

Captures must be taken on the same frame with the camera rig locked (set the
rig pose and the shader _Seed from the harness so both buffers share a frame),
otherwise the stereo baseline itself adds a shift everywhere. Run with:
  python -I eye_diff.py ...
"""
import argparse
import sys

import numpy as np
from PIL import Image


def load_gray(path):
    return np.asarray(Image.open(path).convert("L"), dtype=np.float64)


def phase_shift(a, b):
    """Sub-pixel horizontal shift of b relative to a (positive = b is to the right)."""
    a = a - a.mean()
    b = b - b.mean()
    fa, fb = np.fft.fft2(a), np.fft.fft2(b)
    cross = fa * np.conj(fb)
    cross /= np.abs(cross) + 1e-9
    corr = np.fft.ifft2(cross).real
    peak = np.unravel_index(np.argmax(corr), corr.shape)
    y, x = peak
    # Parabolic interpolation around the peak column for sub-pixel precision.
    w = corr.shape[1]
    c0, c1, c2 = corr[y, (x - 1) % w], corr[y, x], corr[y, (x + 1) % w]
    denom = c0 - 2 * c1 + c2
    frac = 0.5 * (c0 - c2) / denom if abs(denom) > 1e-12 else 0.0
    shift = x + frac
    if shift > w / 2:
        shift -= w
    return -shift


def main():
    p = argparse.ArgumentParser()
    p.add_argument("left")
    p.add_argument("right")
    p.add_argument("--expect-shift", type=float, default=None, help="expected px shift inside the mask")
    p.add_argument("--mask", type=int, nargs=4, metavar=("X0", "Y0", "X1", "Y1"),
                   help="bounding box of the creature mask in pixels")
    p.add_argument("--tol", type=float, default=0.15, help="allowed error in px")
    args = p.parse_args()

    L, R = load_gray(args.left), load_gray(args.right)
    if L.shape != R.shape:
        sys.exit(f"shape mismatch {L.shape} vs {R.shape}")

    ok = True
    if args.mask:
        x0, y0, x1, y1 = args.mask
        outside = np.ones(L.shape, bool)
        outside[y0:y1, x0:x1] = False
        diff_out = np.abs(L - R)[outside].mean()
        print(f"mean |L-R| outside mask: {diff_out:.3f} (expect ~0)")
        ok &= diff_out < 1.0

        shift = phase_shift(L[y0:y1, x0:x1], R[y0:y1, x0:x1])
        print(f"shift inside mask: {shift:+.3f} px")
        if args.expect_shift is not None:
            err = abs(abs(shift) - abs(args.expect_shift))
            print(f"expected |shift| {abs(args.expect_shift):.3f} px, error {err:.3f}")
            ok &= err <= args.tol
    else:
        shift = phase_shift(L, R)
        print(f"global shift: {shift:+.3f} px, mean |L-R| {np.abs(L - R).mean():.3f}")

    print("PASS" if ok else "FAIL")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
