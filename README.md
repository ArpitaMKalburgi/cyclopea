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
    Creatures (species from lobes, rarity from disparity, field-guide collection),
    Reach (Catch: where a shift is seen in 3D, touch judgement, hold-still detector),
    Progress (daily bests across days, personal best, graph scale, next goal, streak line)
  Scripts/Runtime/                     one MonoBehaviour per file (Unity only serialises a class whose name matches its file)
    HomeCard                           the daily Depth Check's home: streak, best, next creature; the four squares are its
                                       buttons (Play, one-eye mode, Field guide, Options), so every input reaches every option
    Options                            high contrast, look and hold, less movement; kept between launches
    StereoPatch                        one square of static; keeps disparity constant as the head moves, or (Catch) holds
                                       the creature at a fixed physical height like a real object
    SurfaceFinder                      MRUK table/wall checked against the room -> Depth API PlaceBox -> easel that finds open space
    CreatureDirector / CreatureBody    found creatures: body built from the hidden outline, ride the fingertip (FingerTips), nest, tutorial
    SpotSession                        the session: Spot (4-choice) -> Sort (pinch the nearer of two) -> Catch (touch it with a
                                       fingertip at its height), staircase per rung, decoy trials (a flat fake in another square,
                                       never a staircase step), feedback, JSONL log, Depth Score card with personal best, rounds,
                                       stereo/motion cue, first-launch notice, pause/resume, 10-minute rest break, tutorial creature,
                                       look and hold on every menu and after 25 s without a choice
    IsdkSelector, IsdkPatchEvents      Interaction SDK hand rays + v207 GazeInteractor (look and pinch)
    HeadGaze, EitherHandPinch          head-pose gaze source and pinch selector for the GazeInteractor
    MicrogestureSelector               thumb-only play (swipe to move, tap to pick)
    HandRaySelector, HeadDwellSelector, VoiceSelector, KeyboardSelector   backup / accessibility / editor inputs
    PassthroughMood                    room dims while a creature hides
    RoomAcoustics                      Meta XR Audio reverb room sized from the MRUK scan; the creature's chirp plays from where it floated
    DeskCamouflage                     round 2 in the desk's colours (Passthrough Camera API, asked for after round 1)
    ScoreAnchor                        today's score left on the desk (persistent spatial anchor) with a two-week graph + streak
    PlatformServices                   entitlement, achievements, days-in-a-row leaderboard (Platform SDK)
    TuningPanel                        Immersive Debugger panel (dev builds)
    DevControls                        finger switches + adb file remote (dev builds only)
  Editor/CyclopeaSetup.cs              headless setup (incl. ISDK rig via its wizard, XR Operator, Immersive Debugger), scene, APKs, captures
  Tests/Editor/                        66 NUnit tests (staircase, ladder, codes, layout, camouflage, streak, microgestures, voice,
                                       creatures, Catch geometry checked by ray tracing, hold-still detector, progress, goals)
  Tests/PlayMode/                      24 in the real scene: wiring (no missing scripts), simulated sessions through Spot, Sort
                                       and Catch, motion mode, notice, tutorial, creatures, pause/resume, rest break, home card,
                                       options, decoys, personal best and graph, text inside the comfortable view,
                                       placement sweep over every MRUK sample room
Tools/
  quest.sh                             install / run / log / sessions / stats / remote / Meta VR CLI perf + fov / OVR Metrics /
                                       XR Operator per-eye check / upload to the Competition channel
  verify_captures.py                   checks rendered per-eye images: commanded shift, and no one-eye cue
  operator_eyes.py                     same question on the headset, through Meta XR Operator captures
  verify_motion.py                     motion mode: the creature slides by the commanded amount with head sway; both eyes identical
  reveal.py                            per-eye pair -> anaglyph + "what your two eyes compare" frame (trailer, gallery)
  room-sweep.md, captures-report.md    latest results
  stats.py                             test-retest ICC, one-eye control vs chance, decoy trials by eyes, norms comparison (Spot
                                       only); Sort and Catch apart
  StaircaseHarness/                    .NET harness for the core without Unity
web/index.html                         Stereo Secrets in the Quest Browser (WebXR, three.js eye layers): share link + backup stereo path
```

## Verified before the headset

| Check | Result |
|---|---|
| Per-eye shift, production shader through URP | within 0.008 px rms of commanded, from 47.7 px down to 0.1 px |
| Eyes identical outside the creature | mean abs difference 0.0000 |
| One eye alone, privileged observer told the outline | 19.5–29.2 % in 4-choice trials (chance 25 %), 40 images per condition |
| Unit tests in Unity | 76 / 76 (staircase, ladder, codes, layout, desk palette on 4 desk types, streak, microgesture grid, voice parsing, motion parallax, creatures, Catch geometry, hold-still, progress across days, goals) |
| Tests in the real scene | 32 / 32 |
| Scene wiring | every square is an ISDK ray + gaze target; no missing scripts; all input modes wired |
| Whole session in the real scene, simulated 60″ observer | Spot completes with a Depth Score of 93″ (56 trials, 4 of them decoy checks; 83–93″ across runs, the session's random draws differ); goes on to Sort (81″) and Catch (10 of 11); fakes ignored 4 of 4; one log summary per rung |
| Placement in rooms never seen (all MRUK sample rooms) | 87 rooms, 1,372 seats, 0 invalid; widest grid corner 26.1°, title 24.6° (Glasses design area 27°); Meta XR Audio room model fits every room |
| Random-guessing player | gets the honest "no stereo measured" card, no number; the next pinch starts motion mode |
| Known-answer benchmark (`Tools/benchmark.md`) | 6,000 simulated players with true thresholds we set, through the game's own ladder and verdict code: 0 of 1,000 with no two-eye depth given a number; 6 of 4,000 with depth told they have none (0.15 %); steady players scored within ×1.58 of the truth 99.8 % of the time, and the ± range shown with each score held the truth 94 % of the time; distraction makes a score coarser, almost never finer (2 of 3,144) |
| Four honest outcomes | a score with its ± range, "≤ 15″" (finer than the display), "no depth from two eyes", or "not sure this time" with the reason (answers scattered, easy ones missed, view disturbed); "not sure" never counts as a score and never leads to the one-eye version |
| Silent failures made visible (`SystemCheck`) | every frame: two views drawn from two eye positions 40–85 mm apart (read from the XR display), head tracked, under a quarter of frames late; a fault stops the rounds it touches from counting, says "That one didn't count: …", and after 8 in a row ends with "Couldn't measure: …". Room scan, passthrough, sounds, Meta account, desk card and saving report as ok or a named fallback, on the home card when it matters, on the Options page as "Checks:", in logcat and in every session log |
| Broken two-eye view, in the real scene | both eyes given one picture: 0 rounds counted, "Not sure this time / Couldn't measure: your two eyes got the same picture", never "no depth" (the benchmark's counterfactual without the check: 500 of 500 told "no depth") |
| Found creatures | the creature comes out exactly where it was seen floating (on the sightline, at the height the disparity showed) and, seen from the eyes, covers the exact outline hidden in the square (checked in UV against the shader's mask), never blocks a pick (no colliders), rides a fingertip, settles in a nest of at most 5; rarity follows the disparity it was found at; field guide persists; tutorial runs end to end with and without a hand |
| Sort and Catch | Sort shows two creatures and only those two squares are answers; Catch holds a creature at a fixed height, a simulated fingertip held still on it catches it (and it climbs on), held 8 cm too high gets "It floated lower than that."; no hands: Catch is skipped, not failed. Closed forms for where a shift is seen checked against a two-ray intersection |
| Home card and options | a returning player opens on the home card (streak, "Best 180″ (Nov 2)", "Next: a Rare creature. They hide below 120″."); the squares are Play / one-eye mode / Field guide / Options; high contrast makes the static black and white, frames bolder, text bold, room darker; look and hold becomes the input and the instructions say "Look at it and hold"; less movement skips Catch; all three survive a relaunch |
| Nobody stuck behind a pinch | look and hold is live on the notice, home card, options and score card whatever the input; accepting the notice by looking makes it the input; 25 s without a choice in play turns it on with "Can't pinch? Look at a square and hold your gaze on it." |
| Decoys | from the second staircase turn, about one Spot trial in six puts a flat fake (an outline either eye sees, zero disparity) in another square while the real creature floats clearly; picking the fake says so and leaves the staircase untouched; logged as decoy trials; the card says "Fakes ignored: n of m" |
| Growth between sessions | each day's best is kept; a finer score than ever says "New personal best!"; the card left on the desk draws the last 14 days (finer is higher); the home card shows the new best |
| Text inside the comfortable view | measured on the rendered text, not its anchor: notice, home, options, field guide, round title, the longest score card and the status line all within 24° of the gaze centre (limit 27°; status line 19.8°); menu labels 1.8–3.8 cm wide on 12 cm squares |
| Comfort and policy | first launch shows the notice (no medical-claim words, checked) and starts nothing until a pinch; pause freezes the trial and reaction time; 10 minutes of play leads to a rest break |
| Motion mode (head-motion parallax, one eye is enough) | see `Tools/motion-report.md`; in the real scene only the target square slides, and a simulated player finishes with a Motion Depth Score |
| Dev APK | builds clean, 105 MB, arm64, API 32+, Vulkan, OpenXR multiview, XR Operator + Immersive Debugger in |
| Judge APK (release) | builds clean, 76 MB; permissions: hand tracking, scene, anchors, headset camera, internet (Platform SDK). No screen capture (XR Operator stripped), no billing |

What only the headset can answer: that multiview delivers each eye its own image (eye-tint check), the pop direction, frame rate, and the human one-eye control. See `DEVICE-DAY.md`. `Tools/device_e2e.py` also fails the run if the game's own health check reports anything but an ok two-eye view and head tracking.

## Policy note

Cyclopea is a depth-perception game with a score. It is not a medical device and gives no medical advice. Consult an eye-care professional if you have a condition that affects your vision. No medical-claim language appears in-app or in store copy.
