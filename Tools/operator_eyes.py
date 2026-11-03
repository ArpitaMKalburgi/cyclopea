"""
On-device per-eye check through Meta XR Operator (MCP layer inside development builds).

With the eye tint on (left eye red, right eye blue), asks XR Operator for the composited image of
each eye and checks that the left image of the static is redder and the right one bluer. That is
the one thing the offline renders cannot prove: that multiview really gives each eye its own image
on the headset.

    Tools/quest.sh operator   # once per boot
    Tools/quest.sh eyes       # pushes tint=1, runs this, pushes tint=0

Writes Tools/device-eyes/{left,right}.png and Tools/device-eyes/report.md. Exit code 0 = pass.
"""
import argparse, base64, io, json, re, subprocess, sys
from pathlib import Path

def call(metavr, eye):
    cmd = [metavr, "xroperator", "call", "openxr_capture_composited_image", "--args", json.dumps({"eye": eye})]
    out = subprocess.run(cmd, capture_output=True, text=True, timeout=60)
    if out.returncode != 0:
        raise RuntimeError(f"{' '.join(cmd)} failed: {out.stderr.strip()[:400]}")
    return out.stdout

def extract_png(text):
    """The tool returns MCP content; find the image either as base64 data or as a saved file path."""
    try:
        obj = json.loads(text)
        stack = [obj]
        while stack:
            o = stack.pop()
            if isinstance(o, dict):
                for k, v in o.items():
                    if isinstance(v, str) and k in ("data", "image", "base64", "png") and len(v) > 1000:
                        return base64.b64decode(v)
                    if isinstance(v, str) and k in ("path", "file", "filePath") and v.lower().endswith((".png", ".jpg")):
                        return Path(v).read_bytes()
                    stack.append(v)
            elif isinstance(o, list):
                stack.extend(o)
    except (json.JSONDecodeError, ValueError):
        pass
    blobs = re.findall(r"[A-Za-z0-9+/=]{2000,}", text)
    if blobs:
        return base64.b64decode(max(blobs, key=len))
    m = re.search(r"([A-Za-z]:\\[^\s\"']+\.png|/[^\s\"']+\.png)", text)
    if m:
        return Path(m.group(1)).read_bytes()
    raise RuntimeError("no image found in XR Operator output:\n" + text[:500])

def centre_rgb(png):
    from PIL import Image
    im = Image.open(io.BytesIO(png)).convert("RGB")
    w, h = im.size
    crop = im.crop((int(w * 0.35), int(h * 0.35), int(w * 0.65), int(h * 0.65)))
    px = list(crop.getdata())
    n = len(px)
    return tuple(sum(p[i] for p in px) / n for i in range(3)), im.size

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--metavr", default="metavr")
    ap.add_argument("--out", default="Tools/device-eyes")
    a = ap.parse_args()
    out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
    res = {}
    for eye in ("left", "right"):
        png = extract_png(call(a.metavr, eye))
        (out / f"{eye}.png").write_bytes(png)
        res[eye] = centre_rgb(png)
    (lr, lg, lb), size = res["left"]
    (rr, rg, rb), _ = res["right"]
    left_red = lr / max(1.0, lb)
    right_blue = rb / max(1.0, rr)
    ok = left_red > 1.15 and right_blue > 1.15
    lines = [
        "# Per-eye check on the headset (Meta XR Operator)", "",
        f"Image size {size[0]}x{size[1]}; centre 30% crop, mean RGB.", "",
        "| Eye | R | G | B | Ratio |", "|---|---|---|---|---|",
        f"| Left | {lr:.0f} | {lg:.0f} | {lb:.0f} | R/B {left_red:.2f} |",
        f"| Right | {rr:.0f} | {rg:.0f} | {rb:.0f} | B/R {right_blue:.2f} |", "",
        "**PASS: each eye receives its own image.**" if ok else
        "**FAIL: the eyes do not receive different images. Try the shader fallbacks in DEVICE-DAY.md.**",
        "", "Look at the static in the centre of view when running this; the crop assumes it is there.",
    ]
    (out / "report.md").write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))
    sys.exit(0 if ok else 1)

if __name__ == "__main__":
    main()
