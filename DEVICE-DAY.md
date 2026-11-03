# Device day (Nov 4) — first Cyclopea run on Quest 3

Goal by tonight: answer the only hard question (does each eye get its own sub-pixel image on the real headset?) and film the first pop-out. Everything else is already built and checked offline (`Tools/captures-report.md`).

## 0. Headset setup (20 min, once)
1. Charge, power on, finish Meta's first-run setup with your Meta account.
2. Phone: Meta Horizon app → Devices → your Quest 3 → Headset settings → **Developer Mode ON**. Restart the headset.
3. In the headset: Settings → Movement tracking → **Hand tracking ON**.
4. Sit at your desk. Settings → Physical space → **Space setup** → scan the room so the desk is labelled *Table*. (Without a scan the game uses a floating easel in front of you; that works too.)
5. USB-C cable to the PC. In the headset, accept **Allow USB debugging** (tick *Always allow from this computer*).

## 1. Install and launch (2 min)
From `cyclopea/` in Git Bash:
```
Tools/quest.sh devices      # should list the Quest
Tools/quest.sh install      # installs Builds/Cyclopea-dev.apk
Tools/quest.sh run
```
Optional second terminal: `Tools/quest.sh log`.

## 2. The five checks, in order

| # | Check | How | Pass | If it fails |
|---|---|---|---|---|
| 1 | Passthrough + static | Look at the desk / in front of you | Your room, four squares of white-on-black static | Black background: tell Claude, passthrough config |
| 2 | **Each eye gets its own image** | Left hand: pinch thumb + **middle** finger. Close one eye, then the other | Left eye sees the squares tinted **red**, right eye **blue** | Same colour in both eyes = per-eye rendering broken. Tell Claude; shader fallbacks are ready |
| 3 | Pop direction | Tint off (middle pinch again). Look at the four squares with both eyes | One square has a blob that floats **toward** you | It sinks *behind* the surface: left **pinky** pinch flips it. Note which way was right |
| 4 | Play | Point at a square with either hand, pinch thumb + **index** | Green frame = right; gets harder; ends with a Depth Score card | Ray not showing: hand tracking off, see step 0.3 |
| 5 | One-eye control | Left **ring** pinch until it says *Eyes: LeftOnly*. Play 40 trials honestly | About 1 in 4 right (chance) | Clearly better than chance = a one-eye clue exists. Tell Claude |

Then: left ring pinch again until *Eyes: Both*. `Tools/quest.sh stats` pulls the logs and prints the numbers.

## 3. Film these (phone + headset recording)
- **Your first time**, from launch to the first creature you find. Real reaction, no retakes. This is the "first person it measured was me" line.
- **Headset recording of the static** (Quest menu → Camera → Record video). The recording shows one eye only, so the creatures will not be in it. That clip *is* the "game you can't screenshot" proof.
- The Depth Score card.

## 4. Other controls
| Gesture / key | Does |
|---|---|
| Left thumb + middle | Eye tint on/off |
| Left thumb + ring | Eyes: Both → Left only → Right only |
| Left thumb + pinky | Flip pop direction |
| Right thumb + middle | Eyes-only mode (look at a square and hold 1.4 s) |
| Pinch any square on the score card | Play again |
| Editor: T / E / O / H / N / R, keys 1-4 | Same, plus next tester id, restart, pick square |

## 5. Send Claude
1. Pass/fail for checks 1-5 (one word each).
2. Which pop direction was right.
3. The output of `Tools/quest.sh stats`.
4. Your Depth Score line.
5. Anything that felt bad in the first minute.
