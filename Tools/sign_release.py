"""Sign the release APK with Cyclopea's own key, so the Meta Horizon Store accepts it.

    python -I Tools/sign_release.py                       # Builds/Cyclopea-judge.apk -> Builds/Cyclopea-release.apk
    python -I Tools/sign_release.py --in X.apk --out Y.apk

Unity signs every build with its temporary debug key, which the store rejects. This re-signs the finished APK
(zipalign, then apksigner: v2 and v3, as minSdk 32 needs no v1) with Keys/cyclopea-release.keystore, whose password is in Keys/keystore-password.txt.
Keys/ is git-ignored. Back it up: every future update must be signed with the same key.
Fails loudly if the result is still signed by "Android Debug" or has no v2 signature. (Cyclopea uses the
network for Meta achievements and its streak leaderboard, so INTERNET is expected here.)
"""
import argparse
import os
import pathlib
import subprocess
import sys
import tempfile

REPO = pathlib.Path(__file__).resolve().parent.parent
ANDROID = pathlib.Path(r"C:\Unity\Editors\6000.0.85f1\Editor\Data\PlaybackEngines\AndroidPlayer")
JDK = ANDROID / "OpenJDK"
KEYS = REPO / "Keys"


def tools():
    bt = sorted((ANDROID / "SDK" / "build-tools").iterdir())[-1]
    return bt / "zipalign.exe", bt / "apksigner.bat", bt / "aapt.exe"


def run(args, **kw):
    env = dict(os.environ, JAVA_HOME=str(JDK))
    r = subprocess.run([str(a) for a in args], capture_output=True, text=True, env=env, **kw)
    if r.returncode != 0:
        sys.exit(f"failed: {pathlib.Path(str(args[0])).name}\n{r.stdout}\n{r.stderr}")
    return r.stdout + r.stderr


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="src", default=str(REPO / "Builds" / "Cyclopea-judge.apk"))
    ap.add_argument("--out", default=str(REPO / "Builds" / "Cyclopea-release.apk"))
    a = ap.parse_args()
    ks, pw = KEYS / "cyclopea-release.keystore", KEYS / "keystore-password.txt"
    for f in (pathlib.Path(a.src), ks, pw):
        if not f.exists():
            sys.exit(f"missing: {f}")
    zipalign, apksigner, aapt = tools()
    with tempfile.TemporaryDirectory() as tmp:
        aligned = pathlib.Path(tmp) / "aligned.apk"
        run([zipalign, "-f", "-p", "4", a.src, aligned])
        # PKCS12: the key's password is the keystore's, which apksigner uses when --key-pass is left out.
        run([apksigner, "sign", "--ks", ks, "--ks-key-alias", "cyclopea", "--ks-pass", f"file:{pw}",
             "--v1-signing-enabled", "true", "--v2-signing-enabled", "true",
             "--out", a.out, aligned])
    # minSdk 32: Android needs no v1 (JAR) signature, and Meta asks for v1 *or* v2. Verify as for API 24 so the
    # v2 signature is reported, not only the newest scheme.
    v = run([apksigner, "verify", "--verbose", "--print-certs", "--min-sdk-version", "24", a.out])
    perms = run([aapt, "dump", "permissions", a.out])
    signer = next((l for l in v.splitlines() if "certificate DN" in l), "?")
    schemes = [l.strip() for l in v.splitlines() if l.startswith("Verified using v")]
    print(signer)
    print("\n".join(schemes))
    bad = []
    if "Android Debug" in signer:
        bad.append("still signed with the debug key")
    if not any("v2 scheme" in s and s.endswith("true") for s in schemes):
        bad.append("no v2 signature (Meta Quest needs v1 or v2)")
    if bad:
        sys.exit("FAILED: " + "; ".join(bad))
    print(f"ok: {a.out} ({pathlib.Path(a.out).stat().st_size / 1e6:.0f} MB)")


if __name__ == "__main__":
    main()
