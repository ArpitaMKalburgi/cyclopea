# Cyclopea

Creatures hide in your real room, camouflaged so only two eyes working together can reveal them. Pinch the one you see; the camouflage tightens until the game finds the finest depth your eyes can resolve.

Entry for the Meta VR Start Developer Competition 2026, Gaming / New Experience. Concept and research: `../IDEA.md`. Build plan: `../PLAN.md`. First device run: [`DEVICE-DAY.md`](DEVICE-DAY.md).

## Build

Unity 6000.0.85f1 (6.0 LTS) with Android Build Support. Packages resolve from Unity's registry and Meta's (`npm.developer.oculus.com`): Meta XR Core, Interaction SDK, MR Utility Kit and Platform 207, URP 17, OpenXR 1.16, XR Hands.

Everything is scriptable headless:

```
UNITY=/c/Unity/Editors/6000.0.85f1/Editor/Unity.exe
P="C:\\Hackathons\\Meta VR Start Developer Competition 2026\\cyclopea"
$UNITY -batchmode -quit -buildTarget Android -projectPath "$P" -executeMethod Cyclopea.EditorTools.CyclopeaSetup.Setup        # configure project + build scene
$UNITY -batchmode -quit -buildTarget Android -projectPath "$P" -executeMethod Cyclopea.EditorTools.CyclopeaSetup.SceneAndApk  # rebuild scene + Builds/Cyclopea-dev.apk
$UNITY -batchmode -quit -buildTarget Android -projectPath "$P" -executeMethod Cyclopea.EditorTools.CyclopeaSetup.CaptureEyes  # offline per-eye renders
$UNITY -batchmode -buildTarget Android -projectPath "$P" -runTests -testPlatform EditMode -testResults Tools/editmode-results.xml
$UNITY -batchmode -buildTarget Android -projectPath "$P" -runTests -testPlatform PlayMode -testResults Tools/playmode-results.xml
python -I Tools/verify_captures.py
Tools/quest.sh install && Tools/quest.sh run
```

The same steps are on the **Cyclopea** menu inside the editor.

## Layout

```
Assets/Cyclopea/
  Shaders/RandomDotStereogram.shader   per-eye procedural random-dot field (Poisson), sub-pixel disparity via unity_StereoEyeIndex,
                                       force-eye and eye-tint debug modes, decoys, per-eye contrast (one-eye control)
  Scripts/Core/                        engine-independent psychophysics (no UnityEngine references)
    DisparityMath, Staircase, SkillLadder, SimulatedObserver, SessionLog, SecretCode, PatchLayout
  Scripts/Runtime/
    StereoPatch.cs                     one square of static; keeps disparity constant as the head moves
    SurfaceFinder.cs                   scanned table, else wall, else a floating easel
    Selectors.cs                       pinch ray (either hand), head-gaze dwell (eyes-only), keyboard/mouse
    SpotSession.cs                     4-choice Spot rung: staircase, feedback, JSONL log, Depth Score card
    DevControls.cs                     finger-pinch switches for eye tint, one-eye control, pop direction, dwell mode
  Editor/CyclopeaSetup.cs              headless setup, scene build, APK build, per-eye captures
  Tests/Editor/                        15 NUnit tests (staircase convergence, ladder, secret codes, layout)
  Tests/PlayMode/                      loads the real scene and plays whole sessions with a simulated player
Tools/
  verify_captures.py                   checks rendered per-eye images: commanded shift, and no one-eye cue
  captures-report.md                   latest result of the above
  stats.py                             test-retest ICC, one-eye control vs chance, norms comparison
  quest.sh                             install / run / log / pull sessions / stats / screenshot / fps
  StaircaseHarness/                    .NET harness for the core without Unity
```

## Verified before the headset

| Check | Result |
|---|---|
| Per-eye shift, production shader through URP | within 0.008 px rms of commanded, from 47.7 px down to 0.1 px |
| Eyes identical outside the creature | mean abs difference 0.0000 |
| One eye alone, privileged observer told the outline | 19.5–29.2 % in 4-choice trials (chance 25 %), 40 images per condition |
| Unit tests in Unity | 15 / 15 |
| Whole session in the real scene, simulated 60″ observer | completes in 55 trials, Depth Score 83″, log written |
| Random-guessing player | gets the honest "no stereo measured" card, no number |
| Dev APK | builds clean, 71 MB, arm64, API 32+, Vulkan, OpenXR multiview |

What only the headset can answer: that multiview delivers each eye its own image (eye-tint check), the pop direction, frame rate, and the human one-eye control. See `DEVICE-DAY.md`.

## Policy note

Cyclopea is a depth-perception game with a score. It is not a medical device and does not diagnose or treat anything. Consult an eye-care professional if you have a binocular vision condition. No treatment language appears in-app or in store copy.
