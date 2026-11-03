# Offline per-eye capture check

Rendered by `CyclopeaSetup.CaptureEyes` with the production shader, forcing each eye. Patch 12 cm at 60 cm. 1024 px = math check; 288 px = roughly the pixels a 12 cm patch covers on Quest 3 at 60 cm.

## 1. Each eye gets the commanded disparity

| px | arcsec | expected shift (px) | measured (px) | error (px) | mean abs L-R outside creature |
|---|---|---|---|---|---|
| 288 | 1920 | -13.404 | -13.398 ± 0.005 | rms 0.008 | 0.0000 |
| 288 | 480 | -3.351 | -3.343 ± 0.004 | rms 0.008 | 0.0000 |
| 288 | 120 | -0.838 | -0.846 ± 0.002 | rms 0.008 | 0.0000 |
| 288 | 60 | -0.419 | -0.416 ± 0.001 | rms 0.003 | 0.0000 |
| 288 | 15 | -0.105 | -0.099 ± 0.001 | rms 0.006 | 0.0000 |
| 288 | 0 | +0.000 | +0.000 ± 0.000 | rms 0.000 | 0.0000 |
| 1024 | 1920 | -47.659 | -47.666 ± 0.002 | rms 0.007 | 0.0000 |
| 1024 | 480 | -11.915 | -11.919 ± 0.000 | rms 0.004 | 0.0000 |
| 1024 | 120 | -2.979 | -2.980 ± 0.000 | rms 0.001 | 0.0000 |
| 1024 | 60 | -1.489 | -1.489 ± 0.001 | rms 0.001 | 0.0000 |
| 1024 | 15 | -0.372 | -0.367 ± 0.002 | rms 0.006 | 0.0000 |
| 1024 | 0 | +0.000 | +0.000 ± 0.000 | rms 0.000 | 0.0000 |

## 2. One eye alone cannot find the creature

Observer told each square's outline; picks, in 4-choice trials, the square whose outline region looks least like plain static. Chance = 25 %. Feature effect sizes (Cohen's d, target vs plain static): band density, interior density, band edge energy, band texture SD.

| px | arcsec | eye | observer accuracy | effect sizes d | n target / n static |
|---|---|---|---|---|---|
| 288 | 1920 | L | 22.2 % | -0.06, +0.08, -0.41, -0.01 | 40 / 40 |
| 288 | 1920 | R | 19.5 % | -0.02, +0.11, -0.43, +0.05 | 40 / 40 |
| 288 | 15 | L | 24.6 % | +0.23, +0.28, +0.21, +0.26 | 40 / 40 |
| 288 | 15 | R | 24.4 % | +0.23, +0.28, +0.21, +0.25 | 40 / 40 |
| 1024 | 1920 | L | 29.2 % | -0.00, +0.27, -0.28, +0.00 | 40 / 40 |
| 1024 | 1920 | R | 23.2 % | -0.08, +0.31, -0.34, -0.07 | 40 / 40 |
| 1024 | 15 | L | 20.9 % | -0.33, +0.31, -0.32, -0.33 | 40 / 40 |
| 1024 | 15 | R | 22.9 % | -0.33, +0.31, -0.32, -0.32 | 40 / 40 |

Verdict: no monocular cue detected by this observer.
