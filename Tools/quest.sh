#!/usr/bin/env bash
# Quest helper for Cyclopea. Run from the repo root in Git Bash.
#
#   Build and run
#     Tools/quest.sh devices            list connected headsets (accept the USB prompt inside the headset first)
#     Tools/quest.sh install            install Builds/Cyclopea-dev.apk (replaces any previous build)
#     Tools/quest.sh install-judge      install Builds/Cyclopea-judge.apk (release build judges get)
#     Tools/quest.sh run                launch the app
#     Tools/quest.sh log                follow the app's log (Unity + Cyclopea lines), Ctrl+C to stop
#
#   Data
#     Tools/quest.sh pull               copy session logs to Tools/sessions/
#     Tools/quest.sh stats              pull, then run Tools/stats.py on everything pulled
#     Tools/quest.sh shot               save a headset screenshot to Tools/shots/ (Meta VR CLI if present, else adb)
#
#   Remote control (development builds; see DevControls.PollRemote)
#     Tools/quest.sh remote tint=1 eyes=LeftOnly ...    keys: tint, eyes, sign, mode, restart, choose, tutorial, cue, sway, rung, rungs, home, option, decoys
#
#   Meta tools
#     Tools/quest.sh perf               10 s Perfetto capture with the Meta VR CLI, then its analysis
#     Tools/quest.sh metrics on|off     OVR Metrics Tool overlay + CSV (install OVR Metrics Tool from the Store first)
#     Tools/quest.sh metrics pull       copy OVR Metrics CSVs to Tools/perf/
#     Tools/quest.sh fov on|off         simulate the Meta VR Glasses field of view in the headset (design area)
#     Tools/quest.sh operator           prepare Meta XR Operator on the headset (port 8720, capture permission)
#     Tools/quest.sh eyes               per-eye check through XR Operator: tint on, capture left + right, compare
#     Tools/quest.sh upload [apk]       upload to the "Competition" release channel (needs OVR_APP_SECRET or OVR_TOKEN)
set -euo pipefail

ADB="${ADB:-/c/Unity/Editors/6000.0.85f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe}"
METAVR="${METAVR:-metavr}"
OPU="${OPU:-ovr-platform-util}"
PKG="com.ujwal240.cyclopea"
APP_ID="${OVR_APP_ID:-1408927408970808}"
APK="Builds/Cyclopea-dev.apk"
FILES="/sdcard/Android/data/$PKG/files"
REMOTE="$FILES/sessions"
have() { command -v "$1" >/dev/null 2>&1; }
mbroadcast() { "$ADB" shell am broadcast -n com.oculus.ovrmonitormetricsservice/.SettingsBroadcastReceiver \
               -a "com.oculus.ovrmonitormetricsservice.$1" "${@:2}" >/dev/null; }
push_remote() { printf '%s\n' "$@" > /tmp/cyc_remote.txt; "$ADB" push /tmp/cyc_remote.txt "$FILES/remote.txt" >/dev/null; }

case "${1:-}" in
  devices)       "$ADB" devices -l ;;
  install)       "$ADB" install -r -g "$APK" ;;
  install-judge) "$ADB" install -r -g Builds/Cyclopea-judge.apk ;;
  run)           "$ADB" shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null && echo "launched $PKG" ;;
  log)           "$ADB" logcat -c; "$ADB" logcat -v time Unity:V '*:S' | grep --line-buffered -E "Cyclopea|Exception|Error|OpenXR" ;;
  pull)          mkdir -p Tools/sessions; "$ADB" pull "$REMOTE/." Tools/sessions/ && ls -l Tools/sessions | tail -n +2 ;;
  stats)         "$0" pull; python -I Tools/stats.py "Tools/sessions/*.jsonl" ;;
  shot)
    mkdir -p Tools/shots; f="Tools/shots/$(date +%Y%m%d-%H%M%S).png"
    if have "$METAVR"; then "$METAVR" capture screenshot -o "$f" --method metacam; else "$ADB" exec-out screencap -p > "$f"; fi
    echo "$f" ;;
  remote)        shift; push_remote "$@"; echo "sent: $*" ;;
  perf)
    mkdir -p Tools/perf; out="Tools/perf/trace-$(date +%Y%m%d-%H%M%S)"
    if have "$METAVR"; then
      "$METAVR" perf capture --app "$PKG" --mode vr --duration 10000 -o "$out"
      "$METAVR" perf analyze-trace "$out"* || true
    else
      echo "Meta VR CLI not found; falling back to VrApi FPS lines (install: see TOOLS.md)"
      "$ADB" logcat -c; timeout 10 "$ADB" logcat -v brief VrApi:I '*:S' | grep --line-buffered "FPS=" || true
    fi ;;
  metrics)
    case "${2:-on}" in
      on)   mbroadcast ENABLE_STAT --es stat average_frame_rate; mbroadcast ENABLE_STATS
            mbroadcast ENABLE_OVERLAY --eb headlocked true; mbroadcast ENABLE_CSV; echo "OVR Metrics overlay + CSV on" ;;
      off)  mbroadcast DISABLE_CSV; mbroadcast DISABLE_OVERLAY; echo "OVR Metrics off" ;;
      pull) mkdir -p Tools/perf; "$ADB" pull /sdcard/Android/data/com.oculus.ovrmonitormetricsservice/files/CapturedMetrics/. Tools/perf/ ;;
    esac ;;
  fov)
    if [ "${2:-on}" = "on" ]; then "$METAVR" device fov-sim enable --design-area; else "$METAVR" device fov-sim disable; fi ;;
  operator)
    "$ADB" forward tcp:8720 tcp:8720
    "$ADB" shell setprop debug.oculus.experimentalEnabled 1
    "$ADB" shell setprop debug.meta_xr_operator.request_capture_permission 1
    "$ADB" shell am broadcast -a com.oculus.vrpowermanager.prox_close >/dev/null
    echo "XR Operator ready on 127.0.0.1:8720 (accept the one-time capture prompt in the headset; redo after a reboot)"
    have "$METAVR" && "$METAVR" xroperator status || true ;;
  eyes)
    mkdir -p Tools/device-eyes
    push_remote tint=1; sleep 2
    python -I Tools/operator_eyes.py --metavr "$METAVR" --out Tools/device-eyes
    push_remote tint=0 ;;
  upload)
    apk="${2:-Builds/Cyclopea-judge.apk}"
    if [ -n "${OVR_APP_SECRET:-}" ]; then cred=(--app-secret "$OVR_APP_SECRET");
    elif [ -n "${OVR_TOKEN:-}" ]; then cred=(--token "$OVR_TOKEN");
    else echo "Set OVR_APP_SECRET (Dashboard > app > Development > API) or OVR_TOKEN first."; exit 1; fi
    "$OPU" upload-quest-build --app-id "$APP_ID" "${cred[@]}" --apk "$apk" --channel Competition \
      --age-group TEENS_AND_ADULTS --notes "Cyclopea $(git rev-parse --short HEAD 2>/dev/null || echo build)" ;;
  *) sed -n 2,26p "$0"; exit 1 ;;
esac
