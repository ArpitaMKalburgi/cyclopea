# Device day (Nov 4): first Cyclopea run on Quest 3

Goal by tonight:
- Answer the only hard question: does each eye get its own sub-pixel image on the real headset?
- Film the first pop-out.

Everything else is built and checked offline:
- `Tools/captures-report.md`: per-eye shift and the one-eye control.
- `Tools/room-sweep.md`: placement across 58 rooms.
- Tests: `Tools/editmode-results.xml`, `Tools/playmode-results.xml`.

## 0. Headset and PC setup (25 min, once)
1. Charge and power on the headset. Finish Meta's first-run setup with your Meta account.
2. On your phone, open the Meta Horizon app. Go to Devices, pick your Quest 3, then Headset settings, and turn **Developer Mode on**. Restart the headset.
3. In the headset, go to Settings → Movement tracking and turn **Hand tracking on**.
4. Sit at your desk. Go to Settings → Physical space → **Space setup** and scan the room so the desk is labelled *Table*.
   - Skip this once, deliberately, on a later run. Without a scan, the game finds the desk with depth sensing instead, which is what a judge without a scan gets.
5. Connect a USB-C cable to the PC. In the headset, accept **Allow USB debugging** and tick *Always allow from this computer*.
6. On the PC (optional, but it unlocks the agent checks below):
   - In PowerShell: `iwr -useb https://developers.meta.com/horizon/install-cli/windows/ | iex` (installs the Meta VR CLI).
   - In Claude Code: `/plugin marketplace add meta-quest/agentic-tools`, then `/plugin install meta-vr@meta-vr`.
   - In the headset's Store: install **OVR Metrics Tool**.

## 1. Install and launch (2 min)
From `cyclopea/` in Git Bash:

```
Tools/quest.sh devices      # should list the Quest
Tools/quest.sh install      # installs Builds/Cyclopea-dev.apk
Tools/quest.sh run
```

Optional, in a second terminal: `Tools/quest.sh log`.

## 2. Checks, in order

Checks 1 to 5 matter most.

| # | Check | How | Pass | If it fails |
|---|---|---|---|---|
| 1 | Passthrough + static | Look at your desk | Your room, with four squares of white-on-black static on the desk | Black background: tell Claude (passthrough config) |
| 2 | **Each eye gets its own image** | Pinch left thumb + **middle** finger. Close one eye, then the other | Left eye sees the squares tinted **red**, right eye **blue** | Same colour in both eyes means per-eye rendering is broken. Tell Claude: the shader fallbacks are ready |
| 3 | Pop direction | Tint off (middle pinch again). Look at the four squares with both eyes | One square has a blob that floats **toward** you | If it sinks *behind* the surface, pinch left **pinky** to flip it. Note which way was right |
| 4 | Play | Point at a square with either hand and pinch thumb + **index**. Or look at a square and pinch with your hand in your lap | Green frame when right. It gets harder, then ends on a Depth Score card | No ray: hand tracking is off (step 0.3). Right thumb + middle switches to the raw-ray backup |
| 5 | One-eye control | Pinch left **ring** until it says *Eyes: LeftOnly*. Play 40 trials honestly | About 1 in 4 right (chance) | Clearly better than chance means a one-eye clue exists. Tell Claude |
| 6 | Your hand in front | Reach over a square | Your real hand covers the static. The static never draws on top of your hand | Static over your hand: tell Claude (hand mask) |
| 7 | Room quiets | Watch the room during a trial | The room dims slightly and loses colour while you search, then comes back | — |
| 8 | Thumb-only play | Rest your hand. Swipe your thumb along your index finger, then tap | A highlight appears, moves with swipes, and the tap picks | Nothing happens: say so (microgestures need recent OS) |
| 9 | Round 2 camouflage | Finish round 1, allow the camera prompt, then pinch to play again | The static takes your desk's colours | Denied or unchanged: fine, note it |
| 10 | Score stays on the desk | After a score, quit the app (`Tools/quest.sh run` again, or the menu) | "Last time … Beat it." card appears where you played | No card: note it (anchors) |

Afterwards, pinch left ring again until *Eyes: Both*. Then run `Tools/quest.sh stats` to pull the logs and print the numbers.

## 3. Agent checks (needs step 0.6)

```
Tools/quest.sh operator     # prepares Meta XR Operator (redo after every headset reboot)
Tools/quest.sh eyes         # look at the squares: tint on, capture each eye, compare -> Tools/device-eyes/report.md
python -I Tools/reveal.py Tools/device-eyes/left.png Tools/device-eyes/right.png --out Tools/reveal --stem first
Tools/quest.sh metrics on   # play one full session
Tools/quest.sh metrics off && Tools/quest.sh metrics pull
Tools/quest.sh perf         # 10 s Perfetto capture + analysis
Tools/quest.sh fov on       # narrow-FoV check: do the squares and the title still fit? then: Tools/quest.sh fov off
```

`quest.sh eyes` proves check 2 without anyone closing an eye. `reveal.py` turns the same two captures into the trailer's "what your two eyes compare" frame and a red/cyan anaglyph.

## 4. Film these (phone + headset recording)
- **Your first time**, from launch to the first creature you find. Real reaction, no retakes. This is the "first person it measured was me" line.
- **Headset recording of the static** (Quest menu → Camera → Record video). A recording shows one eye only, so no creature will be in it. That clip is the "game you can't screenshot" proof. The reveal frame from section 3 goes right after it in the trailer.
- The Depth Score card, and the next-day card on the desk.

## 5. Controls
| Gesture / key | Does |
|---|---|
| Point + pinch (either hand) | Pick a square (Interaction SDK ray) |
| Look + pinch (hand anywhere) | Pick the square you're looking at (Interaction SDK gaze) |
| Thumb swipe / thumb tap | Move the highlight / pick (microgestures) |
| Left thumb + middle | Eye tint on/off |
| Left thumb + ring | Eyes: Both → Left only → Right only |
| Left thumb + pinky | Flip pop direction |
| Right thumb + middle | Input: Interaction SDK → raw ray (backup) → eyes-only dwell (look and hold 1.4 s) |
| Pinch any square on the score card | Play again |
| `Tools/quest.sh remote tint=1 eyes=LeftOnly mode=Dwell choose=2` | Same switches from the PC (dev builds) |
| Immersive Debugger panel (dev builds) | Sliders for dot size, contrast, room dimming. Live level, surface source, anchor and camera status |
| Editor: T / E / O / H / N / R, keys 1-4 | Same switches, plus next tester id, restart, pick a square |

Finger switches and the remote exist only in development builds. The judge build (`Builds/Cyclopea-judge.apk`) has none of them.

## 6. Send Claude
1. Pass or fail for checks 1-10 (one word each).
2. Which pop direction was right.
3. The output of `Tools/quest.sh stats`, and `Tools/device-eyes/report.md` if you ran section 3.
4. Your Depth Score line.
5. Anything that felt bad in the first minute.
