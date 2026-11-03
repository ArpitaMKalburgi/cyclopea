# Offline motion-parallax check

Rendered by `CyclopeaSetup.CaptureMotion` with the production shader: 12 cm patch at 60 cm, head 3 cm left (A) vs 3 cm right (B), 1024 px.

| level (arcsec-equivalent) | depth (mm) | expected slide A to B (px) | measured (px) | error (px) | outside moves (mean abs) | left vs right eye (mean abs) |
|---|---|---|---|---|---|---|
| 1920 | 53.2 | -49.805 | -49.813 ± 0.001 | rms 0.008 | 0.000000 | 0.000000 |
| 480 | 13.3 | -11.605 | -11.608 ± 0.003 | rms 0.005 | 0.000000 | 0.000000 |
| 0 | 0.0 | +0.000 | +0.000 ± 0.000 | rms 0.000 | 0.000000 | 0.000000 |

Verdict: PASS: the creature slides by the commanded amount, nothing else moves, and both eyes see the same image.
