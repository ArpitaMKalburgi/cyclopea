"""
End-to-end check of Cyclopea on a connected Quest, driven from the PC (development build only).

    python -I Tools/device_e2e.py            # fresh install, autoplay the whole game, report
    python -I Tools/device_e2e.py --keep     # keep the installed app and its saved progress

What it does:
  1. Finds the headset over adb, keeps it awake (as if worn), and stops if the system says it has lost
     tracking ("Finding position in room"): the headset must sit upright, cameras facing the room.
  2. Installs Builds/Cyclopea-dev.apk fresh (first launch: notice, then the tutorial creature) and starts it.
  3. Per-eye check through Meta XR Operator (Tools/operator_eyes.py): with the debug tint on, the left eye's
     image must be red and the right eye's blue. Needs the one-time capture prompt accepted in the headset.
  4. Sends autoplay=60 (a simulated player who sees depth down to 60″): notice -> tutorial -> round 1 ->
     Sort -> Catch -> score card -> home card. Takes a screenshot at every new screen.
  5. Reads the game log: every autoplay step, exceptions, errors, warning floods, frame rate (VrApi FPS).
  6. Pulls the session logs and runs Tools/stats.py on them.

Writes Tools/device-e2e/<time>/report.md plus screenshots and the raw log. Exit code 0 = everything passed.
Only talks to the headset over USB and to files in this repo; no network.
"""
import argparse, os, re, shutil, subprocess, sys, time
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PKG = "com.ujwal240.cyclopea"
ADB = os.environ.get("ADB", r"C:\Unity\Editors\6000.0.85f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe")
METAVR = shutil.which("metavr") or "metavr"
# Git Bash, not the WSL bash that Windows may find first (it cannot read a Windows path).
GITBASH = next((b for b in ("C:/Program Files/Git/bin/bash.exe", "C:/Program Files (x86)/Git/bin/bash.exe") if os.path.exists(b)), "bash")
REMOTE = f"/sdcard/Android/data/{PKG}/files/remote.txt"


def run(args, timeout=60, check=False):
    p = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)
    if check and p.returncode != 0:
        raise RuntimeError(f"{' '.join(map(str, args))}: {p.stderr.strip()[:300]}")
    return p


def adb(*a, timeout=60, check=False):
    return run([ADB, *a], timeout=timeout, check=check)


def remote(line):
    adb("shell", f"mkdir -p /sdcard/Android/data/{PKG}/files && echo '{line}' >> {REMOTE}")


def screenshot(out, name):
    f = out / f"{name}.png"
    p = run([METAVR, "capture", "screenshot", "-o", str(f), "--method", "metacam"], timeout=60)
    return f if f.exists() else None


def tracking_lost():
    p = run([METAVR, "ui", "dump", "--format", "json"], timeout=60)
    return "Finding position in room" in p.stdout


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--keep", action="store_true", help="do not reinstall; keep saved progress")
    ap.add_argument("--arcsec", type=float, default=60.0, help="the simulated player's depth threshold")
    ap.add_argument("--minutes", type=float, default=8.0, help="give up on autoplay after this long")
    ap.add_argument("--no-eyes", action="store_true", help="skip the XR Operator per-eye check")
    a = ap.parse_args()

    out = ROOT / "Tools" / "device-e2e" / datetime.now().strftime("%Y%m%d-%H%M%S")
    out.mkdir(parents=True, exist_ok=True)
    report, fails = [], []

    def note(ok, what):
        report.append(f"| {'PASS' if ok else 'FAIL'} | {what} |")
        if not ok:
            fails.append(what)
        print(("PASS " if ok else "FAIL ") + what, flush=True)

    devs = [l for l in adb("devices").stdout.splitlines()[1:] if l.strip()]
    if not any(l.endswith("\tdevice") for l in devs):
        print("No authorised headset over USB. Plug it in, accept 'Allow USB debugging', then run again.")
        print("adb says:", devs or "nothing")
        return 2
    model = adb("shell", "getprop", "ro.product.model").stdout.strip()
    build = adb("shell", "getprop", "ro.build.version.incremental").stdout.strip()
    adb("shell", "am", "broadcast", "-a", "com.oculus.vrpowermanager.prox_close")   # behave as if worn
    if tracking_lost():
        print("The headset has lost tracking ('Finding position in room'). Stand it upright where a seated "
              "player's head would be, cameras facing the desk and the room, then run again.")
        return 3

    if not a.keep:
        adb("uninstall", PKG, timeout=120)
        r = adb("install", "-r", "-g", str(ROOT / "Builds" / "Cyclopea-dev.apk"), timeout=300)
        note("Success" in r.stdout, "fresh install of Builds/Cyclopea-dev.apk")
    adb("logcat", "-c")
    adb("shell", "am", "force-stop", PKG)
    adb("shell", "monkey", "-p", PKG, "-c", "android.intent.category.LAUNCHER", "1")
    pid = ""
    for _ in range(40):
        time.sleep(1)
        pid = adb("shell", "pidof", PKG).stdout.strip()
        if pid:
            break
    note(bool(pid), f"the game starts (pid {pid or 'none'})")
    if not pid:
        blocked = adb("logcat", "-d", "-v", "brief").stdout
        m = re.search(r"Launch is blocked because: ([^\n]+)", blocked)
        if m:
            print("The system blocked the launch:", m.group(1))
        return finish(out, report, fails, model, build)
    time.sleep(12)   # first frames, surface placement
    screenshot(out, "01-first-screen")

    if not a.no_eyes:
        print("Per-eye check: if the headset shows a screen-capture prompt, accept it (once per boot).", flush=True)
        subprocess.run([GITBASH, "Tools/quest.sh", "operator"], cwd=ROOT, timeout=120)
        remote("tint=1")
        time.sleep(2)
        p = run([sys.executable, "-I", str(ROOT / "Tools" / "operator_eyes.py"), "--metavr", METAVR,
                 "--out", str(out / "eyes")], timeout=240)
        remote("tint=0")
        print(p.stdout[-800:])
        note(p.returncode == 0, "each eye gets its own image (left red, right blue under the debug tint)"
             + ("" if p.returncode == 0 else f": {(p.stdout + p.stderr).strip().splitlines()[-1][:200] if (p.stdout + p.stderr).strip() else 'no output'}"))
        time.sleep(1)

    remote(f"autoplay={a.arcsec:g}")
    t0, seen, shots, state = time.time(), set(), 2, ""
    done = False
    while time.time() - t0 < a.minutes * 60:
        time.sleep(2)
        log = adb("logcat", "-d", "-v", "brief", "-s", "Unity:I").stdout
        for m in re.finditer(r"\[Cyclopea autoplay\] state ([^|\r\n]+)", log):
            st = m.group(1).strip()
            key = st.split(" ")[0] + (" " + st.split(" ")[1] if st.startswith("trial") else "")
            if key not in seen:
                seen.add(key)
                shots += 1
                screenshot(out, f"{shots:02d}-{re.sub(r'[^a-z0-9]+', '-', key.lower())}")
                print("screen:", key, flush=True)
        if "[Cyclopea autoplay] stop" in log:
            done = True
            break
        if not adb("shell", "pidof", PKG).stdout.strip():
            note(False, "the game is still running during autoplay (it exited)")
            break

    full = adb("logcat", "-d", "-v", "time", timeout=120).stdout
    (out / "logcat.txt").write_text(full, encoding="utf-8")
    steps = re.findall(r"\[Cyclopea autoplay\] ([^\r\n]+)", full)
    (out / "autoplay.txt").write_text("\n".join(steps), encoding="utf-8")
    stop = next((s for s in reversed(steps) if s.startswith("stop")), "no stop line")
    note(done and "complete" in stop, f"autoplay reaches the home card after a scored session ({stop})")
    for need in ("notice", "tutorial", "tutorial-wink-1", "tutorial-wink-2", "arcade", "menu", "home", "check", "trial Spot", "done"):
        note(any(k.startswith(need) for k in seen) or any(f"state {need}" in s for s in steps), f"screen reached: {need}")
    for opt in ("trial Sort", "trial Catch"):
        if any(k.startswith(opt) for k in seen):
            note(True, f"screen reached: {opt}")

    mine = [l for l in full.splitlines() if f"({pid})" in l or f"( {pid})" in l]
    exc = sorted({l.split("):", 1)[-1].strip()[:160] for l in mine if re.search(r"Exception|E/Unity", l)})
    note(not exc, f"no exceptions or Unity errors in the game log ({len(exc)} distinct)")
    for e in exc[:10]:
        report.append(f"|  | `{e}` |")
    warns = [l for l in mine if l.startswith(("W/Unity", "11-")) and "W/Unity" in l]
    per = {}
    for l in warns:
        msg = l.split("):", 1)[-1].strip()
        if msg and not msg.startswith(("UnityEngine.", "Oculus.", "[./", "(Filename")):
            per[msg[:120]] = per.get(msg[:120], 0) + 1
    flood = {k: v for k, v in per.items() if v > 50}
    note(not flood, "no warning repeated more than 50 times" + (f": {flood}" if flood else ""))
    fps = [int(m.group(1)) for m in re.finditer(r"VrApi.*?FPS=(\d+)/\d+", "\n".join(mine))]
    if fps:
        low = sorted(fps)[max(0, len(fps) // 20)]
        note(low >= 60, f"frame rate: median {sorted(fps)[len(fps) // 2]} fps, 5th percentile {low} fps over {len(fps)} s (need >= 60)")
    else:
        note(False, "frame rate: no VrApi FPS lines found")

    # Silent failures made visible: the game's own health check, read from its log.
    game_log = "\n".join(mine)
    health = {}
    for m in re.finditer(r"\[Cyclopea\] health ([^:]+): (Ok|Fallback|Fault)(?: \(([^)]*)\))?", game_log):
        health[m.group(1)] = (m.group(2), m.group(3) or "")
    view = health.get("Two-eye view", ("missing", ""))
    note(view[0] == "Ok", f"health: two-eye view {view[0]} ({view[1]})")
    head = health.get("Head tracking", ("missing", ""))
    note(head[0] == "Ok", f"health: head tracking {head[0]}")
    faults = re.findall(r"\[Cyclopea\] health ([^:]+): Fault \(([^)]*)\)", game_log)
    note(not faults, "health: no critical faults during the run" + (f": {faults[:5]}" if faults else ""))
    for part, (lvl, det) in sorted(health.items()):
        if lvl != "Ok":
            report.append(f"|  | {part}: {lvl} ({det}) |")
    verdicts = re.findall(r"\[Cyclopea\] verdict ([^\r\n]+)", game_log)
    note(any(v.startswith(("Measured", "AtFloor")) for v in verdicts), f"verdict: {verdicts[-1] if verdicts else 'none logged'}")

    # The play log: every Critter Whack round with the frame times the game measured itself.
    plays = re.findall(r"\[Cyclopea\] play log: round (\d+) \((\w+), (\d+) points, (.*?), errors (\d+)\)", game_log)
    note(bool(plays), f"play log: {len(plays)} Critter Whack round(s) recorded on the headset")
    for n, ended, pts, frames, errs in plays:
        m = re.search(r"p95 ([\d.]+) ms, p99 ([\d.]+) ms, max ([\d.]+) ms, ([\d.]+) % late at (\d+) Hz \((\d+) frames\)", frames)
        if m:
            p95, p99, mx, late, hz, count = float(m.group(1)), float(m.group(2)), float(m.group(3)), float(m.group(4)), int(m.group(5)), int(m.group(6))
            note(p95 <= 1000 / 60 + 0.25, f"round {n} ({ended}, {pts} points): 95 % of {count} frames at 60 fps or better "
                 f"(p95 {p95} ms, p99 {p99} ms, max {mx} ms, {late} % late at {hz} Hz)")
        note(errs == "0", f"round {n}: no errors logged during the round ({errs})")
    frenzy = re.findall(r"\[Cyclopea\] frenzy: ", game_log)
    full_rounds = [p for p in plays if p[1] == "clock"]
    note(len(frenzy) >= len(full_rounds), f"frenzy reached in {len(frenzy)} of {len(full_rounds)} round(s) played to the clock")

    sessions = out / "sessions"
    sessions.mkdir(exist_ok=True)
    adb("pull", f"/sdcard/Android/data/{PKG}/files/sessions/.", str(sessions), timeout=120)
    jsonl = list(sessions.glob("*.jsonl"))
    note(bool(jsonl), f"session logs written on the headset ({len(jsonl)} files)")
    if jsonl:
        p = run([sys.executable, "-I", str(ROOT / "Tools" / "stats.py"), str(sessions / "*.jsonl")], timeout=120)
        (out / "stats.md").write_text(p.stdout, encoding="utf-8")
    return finish(out, report, fails, model, build)


def finish(out, report, fails, model, build):
    lines = [f"# Cyclopea on the headset: end-to-end run {out.name}", "",
             f"Device: {model}, build {build}. Screenshots, raw log and session logs are in this folder.", "",
             "| Result | Check |", "|---|---|", *report, "",
             "**ALL PASSED**" if not fails else f"**{len(fails)} FAILED**"]
    (out / "report.md").write_text("\n".join(lines), encoding="utf-8")
    print("\nReport:", out / "report.md")
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(main())
