"""
Checks the offline motion-parallax renders (CyclopeaSetup.CaptureMotion), production shader:

  1. Between the head 3 cm left (A) and 3 cm right (B), the creature's dots slide by the commanded
     amount, and the static outside the creature does not move at all.
  2. Both eyes see the same image (motion mode carries no disparity), so it works for one eye, for
     players without stereo vision, and on a headset whose per-eye rendering failed.
  3. Plain static (level 0) does not move.

    python -I Tools/verify_motion.py [Tools/captures-motion] [--out Tools/motion-report.md]
"""
import argparse, importlib.util, json, os
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("verify_captures", os.path.join(HERE, "verify_captures.py"))
vc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(vc)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("root", nargs="?", default="Tools/captures-motion")
    ap.add_argument("--out", default="Tools/motion-report.md")
    a = ap.parse_args()
    entries = json.load(open(os.path.join(a.root, "manifest.json"), encoding="utf-8"))
    rows, ok = [], True
    for e in entries:
        n = e["size"]
        A = vc.load_gray(os.path.join(a.root, e["stem"] + "_A.png"))
        B = vc.load_gray(os.path.join(a.root, e["stem"] + "_B.png"))
        AR = vc.load_gray(os.path.join(a.root, e["stem"] + "_AR.png"))
        sdf = vc.mask_sdf(e, n)
        outside = sdf > 0.05 * 2
        still_out = float(np.abs(A - B)[outside].mean())
        eyes_diff = float(np.abs(A - AR).mean())
        cx, cy = e["mask_center"]
        half = 0.10
        x0, x1 = int((cx - half) * n), int((cx + half) * n)
        y0, y1 = int((1 - cy - half) * n), int((1 - cy + half) * n)
        got = vc.measure_shift(A, B, slice(y0, y1), slice(x0, x1))
        exp = e["expect_px"]
        err = got - exp
        rows.append((e["arcsec"], e["depth_m"] * 1000, exp, got, err, still_out, eyes_diff))
        ok &= abs(err) < max(0.05, 0.02 * abs(exp)) and still_out < 1e-6 and eyes_diff < 1e-6

    lines = ["# Offline motion-parallax check", "",
             "Rendered by `CyclopeaSetup.CaptureMotion` with the production shader: 12 cm patch at 60 cm, "
             "head 3 cm left (A) vs 3 cm right (B), 1024 px.", "",
             "| level (arcsec-equivalent) | depth (mm) | expected slide A to B (px) | measured (px) | error (px) | outside moves (mean abs) | left vs right eye (mean abs) |",
             "|---|---|---|---|---|---|---|"]
    by = {}
    for r in rows:
        by.setdefault(r[0], []).append(r)
    for lvl in sorted(by, reverse=True):
        v = np.array(by[lvl], dtype=float)
        lines.append(f"| {lvl:.0f} | {v[0,1]:.1f} | {v[0,2]:+.3f} | {v[:,3].mean():+.3f} ± {v[:,3].std():.3f} | "
                     f"rms {np.sqrt(np.mean(v[:,4]**2)):.3f} | {v[:,5].max():.6f} | {v[:,6].max():.6f} |")
    lines += ["", f"Verdict: {'PASS' if ok else 'FAIL'}: the creature slides by the commanded amount, nothing else moves, "
                  "and both eyes see the same image." if ok else "Verdict: FAIL, see rows above.", ""]
    report = "\n".join(lines)
    with open(a.out, "w", encoding="utf-8") as f:
        f.write(report)
    print(report)
    raise SystemExit(0 if ok else 1)


if __name__ == "__main__":
    main()
