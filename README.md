# Cyclopea

Creatures hide in your real room, camouflaged so only two eyes working together can reveal them. Reach out and catch them.

Entry for the Meta VR Start Developer Competition 2026 — Gaming / New Experience. Concept, research and rubric mapping: `../IDEA.md`. Build plan: `../PLAN.md`.

## Layout

```
Assets/Cyclopea/
  Shaders/RandomDotStereogram.shader   per-eye procedural RDS (unity_StereoEyeIndex), sub-pixel disparity, decoy mode, per-eye contrast
  Scripts/Core/                        engine-independent psychophysics (no UnityEngine references)
    DisparityMath.cs                   arcsec <-> surface shift <-> pop-out depth
    Staircase.cs                       two-phase adaptive staircase (descent, then 3-down/1-up)
    SimulatedObserver.cs               synthetic player with a known threshold (tests + XR Operator harness)
  Tests/Editor/StaircaseTests.cs       NUnit: convergence vs the observer's 79.4 % point, floor exit, geometry
Tools/eye_diff.py                      verifies left/right eye captures: identical outside the mask, exact shift inside
```

## Core verified without Unity

`Scripts/Core` compiles under plain .NET. The convergence harness (scratch, not committed) gives, for observer thresholds 20–800″, mean log error ≤ 0.04 against the 79.4 % point, ~45–50 trials per run, and a 26-trial "≤ display floor" exit for a player who beats the headset.

## Requirements

Unity 6 LTS (Android Build Support, OpenJDK, SDK/NDK), Meta XR All-in-One SDK v81 (Core / Interaction SDK / MR Utility Kit v207), Quest 3 (Quest 3S supported; floor set per device).

## Policy note

Cyclopea is a depth-perception game with a score. It is not a medical device and does not diagnose or treat anything. Consult an eye-care professional if you have a binocular vision condition. No treatment language appears in-app or in store copy.
