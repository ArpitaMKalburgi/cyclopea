# Cyclopea

Creatures hide in your real room, camouflaged so only two eyes working together can reveal them. Pinch the one you see; the camouflage tightens until the game finds the finest depth your eyes can resolve.

Entry for the Meta VR Start Developer Competition 2026, Gaming / New Experience. Concept and research: `../IDEA.md`. Build plan: `../PLAN.md`. First device run: [`DEVICE-DAY.md`](DEVICE-DAY.md).

## Build

Unity 6000.0.85f1 (6.0 LTS) with Android Build Support. Packages resolve from Unity's registry and Meta's (`npm.developer.oculus.com`): Meta XR Core, Interaction SDK, MR Utility Kit and Platform 207, URP 17, OpenXR 1.18 (1.17+ is needed for Meta XR Operator), XR Hands, Meta XR Audio 85 (spatializer). How every Meta tool named in the competition rules is used: [`../TOOLS.md`](../TOOLS.md).

Everything is scriptable headless:

```
UNITY=/c/Unity/Editors/6000.0.85f1/Editor/Unity.exe
P="C:\\Hackathons\\Meta VR Start Developer Competition 2026\\cyclopea"
$UNITY -batchmode -quit -buildTarget Android -projectPath "$P" -executeMethod Cyclopea.EditorTools.CyclopeaSetup.Setup        # configure project + build scene
$UNITY -batchmode -quit -buildTarget Android -projectPath "$P" -executeMethod Cyclopea.EditorTools.CyclopeaSetup.SceneAndApk  # rebuild scene + Builds/Cyclopea-dev.apk
$UNITY -batchmode -quit -buildTarget Android -projectPath "$P" -executeMethod Cyclopea.EditorTools.CyclopeaSetup.BuildJudgeApk # release build for the Competition channel
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
                                       force-eye and eye-tint debug modes, decoys, per-eye contrast (one-eye control), palette
  Shaders/HandOccluder.shader          depth-only hand mask: the real (passthrough) hand is always in front of the static
  Scripts/Core/                        engine-independent logic (no UnityEngine references)
    DisparityMath, Staircase, SkillLadder, SimulatedObserver, SessionLog, SecretCode, PatchLayout,
    Camouflage (desk palette with guaranteed contrast), Streak (days in a row), VoiceCommands (spoken square choice),
    Creatures (species from lobes, rarity from disparity, field-guide collection)
  Scripts/Runtime/                     one MonoBehaviour per file (Unity only serialises a class whose name matches its file)
    StereoPatch                        one square of static; keeps disparity constant as the head moves
    SurfaceFinder                      MRUK table/wall checked against the room -> Depth API PlaceBox -> easel that finds open space
    CreatureDirector / CreatureBody    found creatures: body built from the hidden outline, ride the fingertip (FingerTips), nest, tutorial
    SpotSession                        4-choice Spot rung: staircase, feedback, JSONL log, Depth Score card, rounds,
                                       stereo/motion cue, first-launch notice, pause/resume, 10-minute rest break, tutorial creature
    IsdkSelector, IsdkPatchEvents      Interaction SDK hand rays + v207 GazeInteractor (look and pinch)
    HeadGaze, EitherHandPinch          head-pose gaze source and pinch selector for the GazeInteractor
    MicrogestureSelector               thumb-only play (swipe to move, tap to pick)
    HandRaySelector, HeadDwellSelector, VoiceSelector, KeyboardSelector   backup / accessibility / editor inputs
    PassthroughMood                    room dims while a creature hides
    RoomAcoustics                      Meta XR Audio reverb room sized from the MRUK scan; the creature's chirp plays from where it floated
    DeskCamouflage                     round 2 in the desk's colours (Passthrough Camera API, asked for after round 1)
    ScoreAnchor                        today's score left on the desk (persistent spatial anchor) + streak
    PlatformServices                   entitlement, achievements, days-in-a-row leaderboard (Platform SDK)
    TuningPanel                        Immersive Debugger panel (dev builds)
    DevControls                        finger switches + adb file remote (dev builds only)
  Editor/CyclopeaSetup.cs              headless setup (incl. ISDK rig via its wizard, XR Operator, Immersive Debugger), scene, APKs, captures
  Tests/Editor/                        40 NUnit tests (staircase, ladder, codes, layout, camouflage, streak, microgestures, voice)
  Tests/PlayMode/                      real scene: wiring (no missing scripts), simulated sessions, motion mode, notice,
                                       pause/resume, rest break, placement sweep over every MRUK sample room
Tools/
  quest.sh                             install / run / log / sessions / stats / remote / Meta VR CLI perf + fov / OVR Metrics /
                                       XR Operator per-eye check / upload to the Competition channel
  verify_captures.py                   checks rendered per-eye images: commanded shift, and no one-eye cue
  operator_eyes.py                     same question on the headset, through Meta XR Operator captures
  verify_motion.py                     motion mode: the creature slides by the commanded amount with head sway; both eyes identical
  reveal.py                            per-eye pair -> anaglyph + "what your two eyes compare" frame (trailer, gallery)
  room-sweep.md, captures-report.md    latest results
  stats.py                             test-retest ICC, one-eye control vs chance, norms comparison
  StaircaseHarness/                    .NET harness for the core without Unity
web/index.html                         Stereo Secrets in the Quest Browser (WebXR, three.js eye layers): share link + backup stereo path
```

## Verified before the headset

| Check | Result |
|---|---|
| Per-eye shift, production shader through URP | within 0.008 px rms of commanded, from 47.7 px down to 0.1 px |
| Eyes identical outside the creature | mean abs difference 0.0000 |
| One eye alone, privileged observer told the outline | 19.5–29.2 % in 4-choice trials (chance 25 %), 40 images per condition |
| Unit tests in Unity | 44 / 44 (staircase, ladder, codes, layout, desk palette on 4 desk types, streak, microgesture grid, voice parsing, motion parallax) |
| Scene wiring | every square is an ISDK ray + gaze target; no missing scripts; all input modes wired |
| Whole session in the real scene, simulated 60″ observer | completes in 55 trials, Depth Score 83″, log written |
| Placement in rooms never seen (all MRUK sample rooms) | 87 rooms, 1,372 seats, 0 invalid; widest grid corner 26.1°, title 24.6° (Glasses design area 27°); Meta XR Audio room model fits every room |
| Random-guessing player | gets the honest "no stereo measured" card, no number; the next pinch starts motion mode |
| Found creatures | the creature that comes out of the static has the exact outline hidden in the square (checked in UV against the shader's mask), never blocks a pick (no colliders), rides a fingertip, settles in a nest of at most 5; rarity follows the disparity it was found at; field guide persists; tutorial runs end to end with and without a hand |
| Comfort and policy | first launch shows the notice (no medical-claim words, checked) and starts nothing until a pinch; pause freezes the trial and reaction time; 10 minutes of play leads to a rest break |
| Motion mode (head-motion parallax, one eye is enough) | see `Tools/motion-report.md`; in the real scene only the target square slides, and a simulated player finishes with a Motion Depth Score |
| Dev APK | builds clean, 100 MB, arm64, API 32+, Vulkan, OpenXR multiview, XR Operator + Immersive Debugger in |
| Judge APK (release) | builds clean, 73 MB; permissions: hand tracking, scene, anchors, headset camera, internet (Platform SDK). No screen capture (XR Operator stripped), no billing |

What only the headset can answer: that multiview delivers each eye its own image (eye-tint check), the pop direction, frame rate, and the human one-eye control. See `DEVICE-DAY.md`.

## Policy note

Cyclopea is a depth-perception game with a score. It is not a medical device and does not diagnose or treat anything. Consult an eye-care professional if you have a binocular vision condition. No treatment language appears in-app or in store copy.
