# Cyclopea

Critters hide in a static of dots on your real desk, and only two eyes working together can see them rise. Slap the one you see. The main game is Critter Whack, a 60-second arcade round; the Depth Check on the home card is the measured side mode.

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
    Progress (daily bests across days, personal best, graph scale, next goal, streak line),
    FrameStats (frame-time percentiles in a fixed histogram), ErrorTally (errors counted, the first of each kind kept, thread-safe)
  Scripts/Runtime/                     one MonoBehaviour per file (Unity only serialises a class whose name matches its file)
    HomeCard                           the daily Depth Check's home: streak, best, next creature; the four squares are its
                                       buttons (Play, Depth Check, Field guide, Options); on menus, every input reaches every option
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
    MicrogestureSelector               thumb swipes and taps on menus and in the Depth Check (not in Critter Whack)
    HandRaySelector, HeadDwellSelector, KeyboardSelector   backup / accessibility / editor inputs
     VoiceSelector                      a word parser only (tested); no speech reaches it: this build has no Voice SDK and no microphone permission
    PassthroughMood                    room dims during a Depth Check trial
    RoomAcoustics                      Meta XR Audio reverb room sized from the MRUK scan; the creature's chirp plays from where it floated
    DeskCamouflage                     Depth Check in the desk's colours (Passthrough Camera API, asked for after the first Depth Check; not yet verified on a headset)
    ScoreAnchor                        today's score left on the desk (persistent spatial anchor) with a two-week graph + streak
    PlatformServices                   entitlement, achievements, days-in-a-row leaderboard (Platform SDK)
    WhackRound                         Critter Whack: 3×3 tiles, critter kinds, adaptive rise, combos, points that pop up, a streak that
                                       climbs the scale, the last 10 s a frenzy (×2), a golden preview before goldens unlock
    PlayLog                            one log per launch: every Critter Whack round (score, catches, inputs, frame-time percentiles,
                                       health), the first of each error or exception
    TuningPanel                        Immersive Debugger panel (dev builds)
    DevControls                        finger switches + adb file remote (dev builds only)
  Editor/CyclopeaSetup.cs              headless setup (incl. ISDK rig via its wizard, XR Operator, Immersive Debugger), scene, APKs, captures
  Tests/Editor/                        81 NUnit tests (staircase, ladder, codes, layout, camouflage, streak, microgestures, voice,
                                       creatures, Catch geometry checked by ray tracing, hold-still detector, progress, goals,
                                       frame-time percentiles, error tally under threads and floods, the round record)
  Tests/PlayMode/                      55 in the real scene: wiring (no missing scripts), simulated sessions through Spot, Sort
                                       and Catch, motion mode, notice, tutorial, creatures, pause/resume, rest break, home card,
                                       options, decoys, personal best and graph, text inside the comfortable view,
                                       placement sweep over every MRUK sample room, Critter Whack, health checks,
                                       the menu from every activity, labelled score cards, one hand = one slap, points and
                                       streak pitch, frenzy, golden preview, the play log, 20 rounds in a row (soak)
Tools/
  quest.sh                             install / run / log / sessions / stats / remote / Meta VR CLI perf + fov / OVR Metrics /
                                       XR Operator per-eye check / upload to the Competition channel
  verify_captures.py                   checks rendered per-eye images: commanded shift, and no one-eye cue
  operator_eyes.py                     same question on the headset, through Meta XR Operator captures
  verify_motion.py                     motion mode: the creature slides by the commanded amount with head sway; both eyes identical
  reveal.py                            per-eye pair -> anaglyph + "what your two eyes compare" frame (trailer, gallery)
  room-sweep.md, captures-report.md    latest results
  stats.py                             Critter Whack rounds (catch rate, learning, inputs, frame times, errors) from the play logs;
                                       test-retest ICC, one-eye control vs chance, decoy trials by eyes, norms comparison (Spot
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
| Unit tests in Unity | 81 / 81 (staircase, ladder, codes, layout, desk palette on 4 desk types, streak, microgesture grid, voice parsing, motion parallax, creatures, Catch geometry, hold-still, progress across days, goals, frame times, error tally, round record) |
| Tests in the real scene | 55 / 55 |
| Photosensitivity | critters appearing, hopping and ducking redraw the whole grid at most 3 times a second (shortest gap measured over a round ≥ 0.34 s); a player's own catch still answers at once |
| Menu inside the narrow field of view | across all 87 rooms and 1,372 seats, the Menu button's widest corner is 25.2° from the gaze centre (design area 27°) |
| A way back from anywhere | the Menu square beside the grid (also ☰, the left-hand menu pinch, Esc) pauses a round, the Depth Check or the tutorial: the round's clock and critters wait, nothing floats behind the labels, and Resume puts back the same trial or the same time left; Home and start over keep the round's catches but set no best; Options' Back returns to the menu; every score card labels its squares (again, Home, the other game, Options). Taking the headset off or opening the system menu mid-game opens the game's menu, so the player comes back to "Paused" and resumes by choice, with nothing chosen while the headset is off |
| One hand, one slap | fingertips on a critter with the palm landing first on the empty tile beside it: 1 caught, 0 empty taps (first playtest: 41 empty taps in a round from palms counted as second picks) |
| Every catch answers, every round builds | the points pop up from the caught tile and fade within a second (text only, so the static still changes at most 3 times a second); four in a row sound three semitones higher and show ×2, an empty slap starts the scale again; the last seconds are a frenzy worth double, with the HUD saying so; a golden preview rises before goldens unlock and says "Golden! +500"; none of it in rounds too short to have it |
| Every round on record | a round to the clock and a round left from the menu are both in the launch's play log with their inputs, frame-time percentiles and mean fps; an error logged mid-round is counted in that round and kept once with where it happened; the file on disk holds every line |
| Long play, bounded | frame times in a fixed histogram (10 simulated hours, the same size); 5,000 different errors keep 20 kinds and count all; 20 Critter Whack rounds back to back in the real scene leave no objects behind, keep the managed heap flat, never overflow the nest, and log all 20 rounds |
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
| Home card and options | a returning player opens on the home card (streak, "Best 180″ (Nov 2)", "Next: a Rare creature. They hide below 120″."); the squares are Play / Depth Check / Field guide / Options; high contrast makes the static black and white, frames bolder, text bold, room darker; look and hold becomes the input and the instructions say "Look at it and hold"; less movement skips Catch; all three survive a relaunch |
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
