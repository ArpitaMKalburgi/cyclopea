#!/usr/bin/env bash
# Quest helper for Cyclopea. Run from the repo root in Git Bash:
#   Tools/quest.sh devices     list connected headsets (accept the USB prompt inside the headset first)
#   Tools/quest.sh install     install Builds/Cyclopea-dev.apk (replaces any previous build)
#   Tools/quest.sh run         launch the app
#   Tools/quest.sh log         follow the app's log (Unity + Cyclopea lines), Ctrl+C to stop
#   Tools/quest.sh pull        copy session logs to Tools/sessions/
#   Tools/quest.sh stats       pull, then run Tools/stats.py on everything pulled
#   Tools/quest.sh shot        save a headset screenshot to Tools/shots/
#   Tools/quest.sh perf        show frame rate for 10 s (OVR Metrics via logcat VrApi lines)
set -euo pipefail

ADB="${ADB:-/c/Unity/Editors/6000.0.85f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe}"
PKG="com.ujwal240.cyclopea"
APK="Builds/Cyclopea-dev.apk"
REMOTE="/sdcard/Android/data/$PKG/files/sessions"

case "${1:-}" in
  devices) "$ADB" devices -l ;;
  install) "$ADB" install -r -g "$APK" ;;
  run)     "$ADB" shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null && echo "launched $PKG" ;;
  log)     "$ADB" logcat -c; "$ADB" logcat -v time Unity:V CyclopeaLog:V '*:S' | grep --line-buffered -E "Cyclopea|Exception|Error|OpenXR" ;;
  pull)    mkdir -p Tools/sessions; "$ADB" pull "$REMOTE/." Tools/sessions/ && ls -l Tools/sessions | tail -n +2 ;;
  stats)   "$0" pull; python -I Tools/stats.py "Tools/sessions/*.jsonl" ;;
  shot)    mkdir -p Tools/shots; f="Tools/shots/$(date +%Y%m%d-%H%M%S).png"; "$ADB" exec-out screencap -p > "$f" && echo "$f" ;;
  perf)    "$ADB" logcat -c; timeout 10 "$ADB" logcat -v brief VrApi:I '*:S' | grep --line-buffered "FPS=" || true ;;
  *) sed -n 2,11p "$0"; exit 1 ;;
esac
