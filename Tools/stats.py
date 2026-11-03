"""Turn Cyclopea session logs into the three published numbers.

Usage:  python -I stats.py logs/*.jsonl [--norms norms.csv]

Outputs a markdown table with:
  1. Test-retest reliability of the Depth Score across two sessions per subject —
     ICC(2,1), two-way random, absolute agreement, computed on log10(arcsec) —
     next to the published baseline (Transl Vis Sci Technol 2023, 12(3):1: r = 0.35–0.74).
  2. One-eye control: proportion correct in LeftOnly/RightOnly trials with a 95 % CI,
     tested against 4-AFC chance (0.25). Must NOT be above chance.
  3. Optional population comparison: percentile of each subject's threshold against a
     norms CSV (columns: percentile, arcsec) transcribed from i-Perception 2015 (CC BY).

Only reads the files you pass it; no network.
"""
import argparse
import csv
import glob
import json
import math
import sys
from collections import defaultdict


def load(paths):
    trials, summaries = [], []
    for pattern in paths:
        for p in glob.glob(pattern):
            with open(p, encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if not line:
                        continue
                    o = json.loads(line)
                    (trials if o.get("type") == "trial" else summaries).append(o)
    return trials, summaries


def icc21(matrix):
    """ICC(2,1) for an n_subjects x k_sessions matrix (Shrout & Fleiss 1979)."""
    n = len(matrix)
    k = len(matrix[0])
    grand = sum(sum(r) for r in matrix) / (n * k)
    row_means = [sum(r) / k for r in matrix]
    col_means = [sum(matrix[i][j] for i in range(n)) / n for j in range(k)]
    ss_rows = k * sum((m - grand) ** 2 for m in row_means)
    ss_cols = n * sum((m - grand) ** 2 for m in col_means)
    ss_err = sum((matrix[i][j] - row_means[i] - col_means[j] + grand) ** 2
                 for i in range(n) for j in range(k))
    ms_r = ss_rows / (n - 1)
    ms_c = ss_cols / (k - 1)
    ms_e = ss_err / ((n - 1) * (k - 1))
    return (ms_r - ms_e) / (ms_r + (k - 1) * ms_e + k * (ms_c - ms_e) / n)


def wilson(p_hat, n, z=1.96):
    if n == 0:
        return (float("nan"), float("nan"))
    centre = (p_hat + z * z / (2 * n)) / (1 + z * z / n)
    half = z * math.sqrt(p_hat * (1 - p_hat) / n + z * z / (4 * n * n)) / (1 + z * z / n)
    return centre - half, centre + half


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("logs", nargs="+")
    ap.add_argument("--norms", help="CSV percentile,arcsec from i-Perception 2015")
    a = ap.parse_args()
    trials, summaries = load(a.logs)
    # Motion-parallax sessions measure something else (depth from head movement, one eye is enough);
    # every stereo number below uses stereo sessions only. Logs from before the cue field are stereo.
    motion_summaries = [s for s in summaries if s.get("cue") == "Motion"]
    trials = [t for t in trials if t.get("cue", "Stereo") == "Stereo"]
    summaries = [s for s in summaries if s.get("cue", "Stereo") == "Stereo"]

    print("## Cyclopea — measured numbers\n")
    if motion_summaries:
        done = [s for s in motion_summaries if s.get("threshold_arcsec") is not None]
        print(f"Motion-parallax sessions (reported separately, not in any stereo number): {len(motion_summaries)}, "
              f"{len(done)} with a measured step.\n")

    # 1. Test-retest: first two Both-eyes sessions per subject with a finite threshold.
    per_subject = defaultdict(list)
    for s in sorted(summaries, key=lambda s: s["utc"]):
        if s.get("eyes") != "Both" or s.get("threshold_arcsec") is None:
            continue
        per_subject[s["subject"]].append(math.log10(s["threshold_arcsec"]))
    pairs = [v[:2] for v in per_subject.values() if len(v) >= 2]
    print("### 1. Test-retest reliability of the Depth Score")
    if len(pairs) >= 3:
        icc = icc21(pairs)
        diffs = [b - a_ for a_, b in pairs]
        mean_abs = sum(abs(d) for d in diffs) / len(diffs)
        print(f"| Test | N | Reliability |\n|---|---|---|")
        print(f"| Cyclopea (this build) | {len(pairs)} | ICC(2,1) = {icc:.2f}; mean |Δlog| = {mean_abs:.3f} (×{10**mean_abs:.2f}) |")
        print(f"| VR headset stereo test, TVST 2023 | 25 | r = 0.35–0.74 |\n")
    else:
        print(f"Need ≥ 3 subjects with two sessions; have {len(pairs)}.\n")

    # 2. One-eye control.
    mono = [t for t in trials if t.get("eyes") in ("LeftOnly", "RightOnly") and t.get("mode") == "FourAFC"
            and not t.get("decoy")]
    print("### 2. One-eye control (must be at chance)")
    if mono:
        n = len(mono)
        p = sum(1 for t in mono if t["correct"]) / n
        lo, hi = wilson(p, n)
        verdict = "PASS — no monocular leak" if lo <= 0.25 else "FAIL — shape visible to one eye"
        print(f"{n} trials with one eye covered: {100*p:.0f} % correct (95 % CI {100*lo:.0f}–{100*hi:.0f} %), chance 25 %. **{verdict}**\n")
    else:
        print("No one-eye trials logged yet.\n")

    # 3. Population comparison.
    if a.norms:
        norms = []
        with open(a.norms, encoding="utf-8") as f:
            for row in csv.DictReader(f):
                norms.append((float(row["percentile"]), float(row["arcsec"])))
        norms.sort()
        print("### 3. Against the i-Perception 2015 population distribution")
        print("| Subject | Depth Score (″) | Population percentile |\n|---|---|---|")
        for subj, logs in per_subject.items():
            t = 10 ** logs[-1]
            pct = next((p for p, arc in norms if arc >= t), 100.0)
            print(f"| {subj[:8]} | {t:.0f} | ~{pct:.0f}th |")
        print()


if __name__ == "__main__":
    main()
