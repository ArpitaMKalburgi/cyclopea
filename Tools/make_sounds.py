#!/usr/bin/env python3
"""
make_sounds.py - procedural sound-effects pack for Cyclopea (Meta Quest 3, seated mixed reality).

Every clip is synthesised from scratch with numpy, written as 48 kHz / 16-bit / mono PCM WAV into
Assets/Cyclopea/Audio/, read back, self-checked (table on stdout) and drawn as a log-frequency
spectrogram contact sheet at Tools/sound-sheet.png (numpy FFT + Pillow, no matplotlib).

    python -I Tools/make_sounds.py

Design rules
  * Palette: kalimba, marimba, glockenspiel, glass, bubbles, small formant voices, breathy noise.
    Only sines are summed (no square/pulse waves), so nothing is buzzy or 8-bit.
  * Key: D major pentatonic (D E F# A B). hz() refuses any other pitch class, so every struck note is
    in key and every glide starts and ends on a scale note (a few glides scoop in from a nearby pitch).
  * Deterministic: each clip seeds its own RNG from a CRC32 of its name; the room IR has a fixed seed.
  * Mastering, per clip: small-room reverb at a low mix -> zero-phase 35 Hz high-pass and gentle
    low-pass (8.5 kHz, raised only for the sparkle clips) -> DC removal -> raised-cosine fades
    (>= 5 ms) -> peak to -3 dBFS -> if the clip is quieter than the shared loudness window, gentle
    look-ahead gain riding (crest-factor reduction, never waveshaping) and re-peak to -3 dBFS.
  * static_bed is synthesised in the frequency domain over exactly 8 s and its "breathing" LFOs use
    whole cycles per 8 s, so it is periodic by construction: sample N-1 runs into sample 0 exactly as
    any two neighbouring samples do. That is a perfect crossfade with no level dip at the seam.

Tuning by ear: each clip is one c_<name>() function below. The numbers in it are notes, onset times
(seconds), gains (linear) and decay constants (tau, seconds). The CLIPS table at the bottom holds the
reverb mix, low-pass and fade lengths.
"""

import math
import os
import wave
import zlib

import numpy as np

SR = 48000
HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
OUT_DIR = os.path.join(PROJECT, "Assets", "Cyclopea", "Audio")
SHEET_PATH = os.path.join(HERE, "sound-sheet.png")

PEAK_DBFS = -3.0
PEAK = 10.0 ** (PEAK_DBFS / 20.0)
BED_RMS_DBFS = -30.0
LOUD_LO_DBFS = -15.0   # matched clips are lifted (by gain riding) to at least this full-clip RMS
LOUD_SPAN_DB = 4.0     # ...and must all sit within this span
MAX_RIDE_DB = 7.0      # most crest-factor reduction gain riding may apply
FMAX_TONAL = 9000.0    # partials at or above this are not generated
FMAX_SPARKLE = 13000.0  # ...except in deliberate sparkles

# ----------------------------------------------------------------------------------------------
# Pitch: D major pentatonic only
# ----------------------------------------------------------------------------------------------
_PC = {"C": 0, "C#": 1, "D": 2, "D#": 3, "E": 4, "F": 5, "F#": 6, "G": 7, "G#": 8, "A": 9,
       "A#": 10, "B": 11}
_SCALE = {2, 4, 6, 9, 11}  # D E F# A B


def hz(note):
    """'F#5' -> 739.99. Raises for any pitch outside D major pentatonic."""
    name, octave = note[:-1], int(note[-1])
    pc = _PC[name]
    if pc not in _SCALE:
        raise ValueError(f"{note} is outside D major pentatonic")
    return 440.0 * 2.0 ** ((12 * (octave + 1) + pc - 69) / 12.0)


# ----------------------------------------------------------------------------------------------
# Small helpers
# ----------------------------------------------------------------------------------------------
def n_of(sec):
    return int(round(sec * SR))


def tline(n):
    return np.arange(n) / SR


def smoothstep(u):
    u = np.clip(u, 0.0, 1.0)
    return u * u * (3.0 - 2.0 * u)


def phase_of(freq, n=None):
    """Running phase (radians) of a frequency track; starts at exactly 0 so tones start at a zero."""
    freq = np.broadcast_to(np.asarray(freq, float), (n,) if n else np.shape(freq))
    c = np.cumsum(freq)
    return 2.0 * np.pi * (c - c[0]) / SR


def add(buf, sig, at, gain=1.0):
    i = n_of(at)
    if i >= len(buf):
        return
    m = min(len(sig), len(buf) - i)
    buf[i:i + m] += gain * sig[:m]


def perc_env(n, attack, tau, hold=0.0):
    """Smooth attack (smoothstep) then exponential decay with time constant tau."""
    t = tline(n)
    a = smoothstep(t / attack) if attack > 0 else np.ones(n)
    return a * np.exp(-np.maximum(t - attack - hold, 0.0) / tau)


def env_pts(n, pts):
    """Breakpoint envelope [(time, value), ...] with smoothstep easing between points."""
    t = tline(n)
    out = np.full(n, float(pts[-1][1]))
    out[t < pts[0][0]] = pts[0][1]
    for (t0, a0), (t1, a1) in zip(pts[:-1], pts[1:]):
        m = (t >= t0) & (t < t1)
        out[m] = a0 + (a1 - a0) * smoothstep((t[m] - t0) / (t1 - t0))
    return out


def glide(n, pts, ease=True):
    """Pitch track through [(time, Hz), ...], interpolated in log-frequency.
    ease: True = smoothstep, False = linear, or a callable u -> u."""
    t = tline(n)
    lf = np.full(n, math.log(pts[-1][1]))
    lf[t < pts[0][0]] = math.log(pts[0][1])
    for (t0, f0), (t1, f1) in zip(pts[:-1], pts[1:]):
        m = (t >= t0) & (t < t1)
        u = (t[m] - t0) / (t1 - t0)
        if ease is True:
            u = smoothstep(u)
        elif callable(ease):
            u = ease(u)
        lf[m] = math.log(f0) + (math.log(f1) - math.log(f0)) * u
    return np.exp(lf)


def vibrato(n, rate, depth, delay):
    """Multiplicative pitch wobble that fades in after `delay` seconds. depth is a ratio (0.01 = 1%)."""
    t = tline(n)
    return 1.0 + depth * smoothstep((t - delay) / 0.08) * np.sin(2.0 * np.pi * rate * t)


def note_tail(n, frac=0.25):
    """Raised-cosine fade over the last `frac` of a note buffer, so a note that is still ringing when
    its buffer ends dies away instead of being cut (a cut is a small click)."""
    w = np.ones(n)
    b = max(1, int(n * frac))
    w[n - b:] = 0.5 + 0.5 * np.cos(np.pi * (np.arange(b) + 1) / b)
    return w


def norm_peak(x, to=1.0):
    p = np.max(np.abs(x))
    return x * (to / p) if p > 0 else x


# ----------------------------------------------------------------------------------------------
# Filters (zero-phase, frequency domain) and noise
# ----------------------------------------------------------------------------------------------
def lp_mag(f, fc, order=2):
    return 1.0 / np.sqrt(1.0 + (np.asarray(f, float) / fc) ** (2 * order))


def hp_mag(f, fc, order=2):
    r = (np.asarray(f, float) / fc) ** (2 * order)
    return np.sqrt(r / (1.0 + r))


def bp_mag(f, fc, q):
    f = np.maximum(np.asarray(f, float), 1e-3)
    x = q * (f / fc - fc / f)
    return 1.0 / np.sqrt(1.0 + x * x)


def fft_filter(x, magfn):
    """Zero-phase filter by a magnitude response magfn(freqs). Padded so nothing wraps around."""
    n = len(x)
    N = 1 << int(math.ceil(math.log2(n + 16384)))
    f = np.fft.rfftfreq(N, 1.0 / SR)
    return np.fft.irfft(np.fft.rfft(x, N) * magfn(f), N)[:n]


def shaped_noise(n, rng, magfn, win=1024):
    """Time-varying coloured noise: STFT overlap-add of random spectra shaped by magfn(T, F), where T
    is a column of frame times (s) and F a row of frequencies (Hz). Variance ~ mean of magfn**2."""
    hop = win // 4
    nfr = (n + win) // hop + 2
    freqs = np.fft.rfftfreq(win, 1.0 / SR)
    times = (np.arange(nfr) * hop - win + win / 2) / SR
    mag = np.broadcast_to(magfn(times[:, None], freqs[None, :]), (nfr, len(freqs)))
    spec = (rng.standard_normal(mag.shape) + 1j * rng.standard_normal(mag.shape)) * mag
    w = 0.5 - 0.5 * np.cos(2.0 * np.pi * np.arange(win) / win)
    frames = np.fft.irfft(spec, win, axis=1) * w * math.sqrt(win / 2.0) / math.sqrt(1.5)
    out = np.zeros(nfr * hop + 2 * win)
    for i in range(nfr):
        out[i * hop:i * hop + win] += frames[i]
    return out[win:win + n]


# ----------------------------------------------------------------------------------------------
# Room: one shared small-room impulse response (darker, longer lows; shorter highs)
# ----------------------------------------------------------------------------------------------
_IR = None


ROOM_RT60 = 0.45  # seconds


def room_ir():
    """Exponentially decaying noise burst (RT60 0.45 s, 10 ms pre-delay, 20 ms soft build-up, dark
    3.5 kHz roll-off). A plain noise burst has a peaky random frequency response (+-10 dB from one
    note to the next), and these sounds are nearly pure tones, so its magnitude is flattened by
    alternating projection: impose the smooth target spectrum, then re-impose the decay envelope,
    30 times. Tone-to-tone wet level then varies only about +-2.5 dB. Normalised so the in-band
    (200 Hz-4 kHz) gain is 1, so `mix` is the wet level of a sustained tone (0.25 = -12 dB)."""
    global _IR
    if _IR is None:
        rng = np.random.default_rng(20261118)
        n = n_of(0.9)
        t = tline(n)
        env = np.exp(-6.91 * t / ROOM_RT60) * smoothstep(t / 0.02)
        f = np.fft.rfftfreq(n, 1.0 / SR)
        target = hp_mag(f, 150.0, 1) * lp_mag(f, 3500.0, 2)
        k = np.ones(n_of(0.004)) / n_of(0.004)
        h = rng.standard_normal(n) * env
        for _ in range(30):
            h = np.fft.irfft(target * np.exp(1j * np.angle(np.fft.rfft(h))), n)
            h = h / (np.sqrt(np.convolve(h * h, k, mode="same")) + 1e-12) * env
        h = np.concatenate([np.zeros(n_of(0.010)), h])
        N = 1 << 18
        H = np.abs(np.fft.rfft(h, N))
        fb = np.fft.rfftfreq(N, 1.0 / SR)
        _IR = h / np.sqrt(np.mean(H[(fb > 200) & (fb < 4000)] ** 2))
    return _IR


def reverb(x, mix):
    ir = room_ir()
    N = 1 << int(math.ceil(math.log2(len(x) + len(ir))))
    wet = np.fft.irfft(np.fft.rfft(x, N) * np.fft.rfft(ir, N), N)[:len(x)]
    return x + mix * wet


# ----------------------------------------------------------------------------------------------
# Instruments (all return a mono float array; amplitude ~1 at the strike)
# ----------------------------------------------------------------------------------------------
def modal(f, n, modes, attack, tau, fmax=FMAX_TONAL, bend=0.0, bend_tau=0.01):
    """Struck object: sum of decaying sine modes [(ratio, amp, tau_multiplier), ...].
    bend = brief upward pitch offset at the strike (a ratio) that relaxes with bend_tau."""
    t = tline(n)
    bendc = 1.0 + bend * np.exp(-t / bend_tau) if bend else np.ones(n)
    out = np.zeros(n)
    for ratio, amp, tmul in modes:
        fr = f * ratio
        if fr >= fmax:
            continue
        taper = 1.0 if fr < 0.75 * fmax else float(smoothstep((fmax - fr) / (0.25 * fmax)))
        out += amp * taper * np.sin(phase_of(fr * bendc)) * perc_env(n, attack, tau * tmul)
    return out * note_tail(n)


def sine_ping(f, dur, attack=0.002, tau=0.1):
    return modal(f, n_of(dur), [(1.0, 1.0, 1.0)], attack, tau)


def kalimba(f, dur=0.5, tau=0.2, fmax=FMAX_TONAL):
    # tine: strong fundamental, faint octave, the short inharmonic "ping" mode at 6.27x
    return modal(f, n_of(dur), [(1.0, 1.0, 1.0), (2.0, 0.05, 0.4), (6.27, 0.16, 0.10)],
                 attack=0.0015, tau=tau, fmax=fmax, bend=0.003, bend_tau=0.015)


def marimba(f, dur=0.6, tau=0.25, fmax=FMAX_TONAL):
    # tuned bar: fundamental, 4th partial tuned to two octaves, faint 10x; soft yarn-mallet attack
    return modal(f, n_of(dur), [(1.0, 1.0, 1.0), (3.99, 0.25, 0.25), (9.95, 0.05, 0.08)],
                 attack=0.003, tau=tau, fmax=fmax)


def glock(f, dur=1.0, tau=0.5, fmax=9500.0):
    # free-free bar modes 1 : 2.76 : 5.40 : 8.93, upper ones short and quiet
    return modal(f, n_of(dur), [(1.0, 1.0, 1.0), (2.76, 0.16, 0.33), (5.40, 0.05, 0.17),
                                (8.93, 0.015, 0.09)], attack=0.0012, tau=tau, fmax=fmax)


def glass(f, dur=0.35, tau=0.1, fmax=FMAX_SPARKLE):
    # wine-glass-like inharmonic ping for sparkles
    return modal(f, n_of(dur), [(1.0, 1.0, 1.0), (2.61, 0.20, 0.45), (4.90, 0.06, 0.25)],
                 attack=0.0008, tau=tau, fmax=fmax)


def chime(f, dur=0.8, tau=0.45):
    # warm chime: near-harmonic, very little of the glassy 2.76 mode, slower attack
    return modal(f, n_of(dur), [(1.0, 1.0, 1.0), (2.0, 0.10, 0.5), (2.76, 0.07, 0.3),
                                (4.0, 0.03, 0.25), (5.40, 0.015, 0.15)], attack=0.004, tau=tau)


def wood(f, rng, dur=0.2, tau=0.04):
    """Muted low wooden 'tok': short marimba-like body with a pitch drop + a tiny filtered knock."""
    n = n_of(dur)
    body = modal(f, n, [(1.0, 1.0, 1.0), (3.98, 0.18, 0.3)], attack=0.0012, tau=tau,
                 bend=0.05, bend_tau=0.006)
    knock = fft_filter(rng.standard_normal(n), lambda F: bp_mag(F, 1100.0, 1.5))
    knock = norm_peak(knock * perc_env(n, 0.0006, 0.004))
    return body + 0.22 * knock


def bubble(dur, f_from, f_to, glide_tau, attack, tau, wobble=0.0, wob_rate=20.0, wob_tau=0.05):
    """Bubble / pop: sine whose pitch flies from f_from to f_to (exponential approach)."""
    n = n_of(dur)
    t = tline(n)
    f = f_to + (f_from - f_to) * np.exp(-t / glide_tau)
    if wobble:
        f = f * (1.0 + wobble * np.exp(-t / wob_tau) * np.sin(2.0 * np.pi * wob_rate * t))
    return np.sin(phase_of(f)) * perc_env(n, attack, tau) * note_tail(n)


def bell(f, n, tau=0.65, attack=0.003):
    """Calm 'major' bell: hum, prime, fifth, octave, major tenth, twelfth, plus two faint
    inharmonic shimmer partials. Some partials get a quiet twin a fraction of a Hz away (slow beat)."""
    t = tline(n)
    parts = [(0.5, 0.35, 1.7, 0.0), (1.0, 1.0, 1.0, 0.55), (1.5, 0.16, 0.7, 0.8),
             (2.0, 0.38, 0.55, 1.1), (2.5, 0.09, 0.45, 0.0), (3.0, 0.09, 0.35, 1.4),
             (4.2, 0.04, 0.22, 0.0), (5.4, 0.02, 0.13, 0.0)]
    out = np.zeros(n)
    for ratio, amp, tmul, beat in parts:
        fr = f * ratio
        s = np.sin(2.0 * np.pi * fr * t)
        if beat:
            s = (s + 0.4 * np.sin(2.0 * np.pi * (fr + beat) * t)) / 1.4
        out += amp * s * perc_env(n, attack, tau * tmul)
    return out


def pad_note(f, n, rng, env, harmonics=8, slope=1.7, cutoff=2200.0, detune_cents=5.0):
    """Warm held note: two slightly detuned harmonic stacks with soft roll-off and slow vibrato."""
    t = tline(n)
    out = np.zeros(n)
    for d in (-detune_cents, detune_cents):
        fd = f * 2.0 ** (d / 1200.0) * (1.0 + 0.0015 * np.sin(2 * np.pi * 4.8 * t + rng.uniform(0, 6.28)))
        ph = phase_of(fd)
        for k in range(1, harmonics + 1):
            if k * f > 8000.0:
                break
            out += k ** -slope * lp_mag(k * f, cutoff, 2) * np.sin(k * ph + rng.uniform(0, 6.28))
    return 0.5 * out * env


# Creature formants, scaled up for a tiny vocal tract: [(centre Hz, bandwidth Hz, gain), ...]
VOWELS = {
    "oo": [(650, 260, 1.0), (1400, 380, 0.55), (2800, 500, 0.12)],
    "ee": [(480, 200, 0.55), (2900, 500, 1.0), (3800, 600, 0.35)],
    "ah": [(1150, 320, 1.0), (1900, 420, 0.8), (3200, 600, 0.25)],
    "eh": [(800, 260, 0.9), (2500, 450, 0.75), (3500, 600, 0.25)],
    "mm": [(380, 220, 1.0), (1100, 400, 0.12), (2600, 600, 0.04)],
}


def formant_gain(f, va, vb, m):
    """Spectral envelope morphing from vowel va (m=0) to vb (m=1); a 0.22 floor keeps it a 'hint'."""
    g = 0.22
    for (fa, ba, ga), (fb, bb, gb) in zip(VOWELS[va], VOWELS[vb]):
        F, B, G = fa + (fb - fa) * m, ba + (bb - ba) * m, ga + (gb - ga) * m
        g = g + G / (1.0 + ((f - F) / B) ** 2)
    return g


def _taper(fk, hmax):
    return smoothstep((hmax - fk) / (0.3 * hmax))


def voice(f0, va, vb, morph, amp, slope=1.45, vib=(7.0, 0.012, 0.05), hmax=6500.0):
    """Creature voice: additive harmonics of a gliding f0 (with light vibrato), each harmonic weighted
    by a soft source slope and the morphing vowel formant at its own frequency."""
    n = len(f0)
    f = f0 * vibrato(n, *vib) if vib else f0
    ph = phase_of(f)
    out = np.zeros(n)
    for k in range(1, 20):
        fk = k * f
        if fk.min() >= hmax:
            break
        out += k ** -slope * formant_gain(fk, va, vb, morph) * _taper(fk, hmax) * np.sin(k * ph)
    return out * amp


def breath(n, rng, va, vb, morph, amp, lo=900.0):
    """Breathy noise shaped by the same vowel formants, following the voice's amplitude."""
    t = tline(n)

    def mag(T, F):
        return (np.interp(T, t, amp) * formant_gain(F, va, vb, np.interp(T, t, morph))
                * hp_mag(F, lo, 2) * lp_mag(F, 6000.0, 2))
    return shaped_noise(n, rng, mag, win=512)


def soft_tone(f0, cutoff, slope=1.4, vib=None, hmax=7000.0, nh=10):
    """Soft harmonic tone with a (time-varying) low-pass brightness `cutoff`."""
    n = len(f0)
    f = f0 * vibrato(n, *vib) if vib else f0
    ph = phase_of(f)
    out = np.zeros(n)
    for k in range(1, nh + 1):
        fk = k * f
        if fk.min() >= hmax:
            break
        out += k ** -slope * lp_mag(fk, cutoff, 2) * _taper(fk, hmax) * np.sin(k * ph)
    return out


def band_noise(n, rng, fc, q, amp, win=1024):
    """Noise through a band-pass whose centre fc(T) and level amp(T) are functions of time (s)."""
    return shaped_noise(n, rng, lambda T, F: amp(T) * bp_mag(F, fc(T), q), win)


# ----------------------------------------------------------------------------------------------
# The clips. Each takes (rng, n) and returns n samples.
# ----------------------------------------------------------------------------------------------
def c_pick_right(rng, n):
    b = np.zeros(n)
    add(b, kalimba(hz("D5"), 0.35, tau=0.15), 0.000, 0.80)
    add(b, kalimba(hz("A5"), 0.30, tau=0.19), 0.085, 1.00)
    add(b, sine_ping(hz("A6"), 0.25, 0.002, 0.06), 0.085, 0.10)  # a little shine on the top note
    return b


def c_pick_wrong(rng, n):
    b = np.zeros(n)
    add(b, wood(hz("A3"), rng), 0.000, 1.00)
    add(b, wood(hz("F#3"), rng), 0.115, 0.85)
    return b


def c_creature_condense(rng, n):
    b = np.zeros(n)
    notes = ["D6", "E6", "F#6", "A6", "B6", "D7", "E7", "F#7", "A7"]
    t0, gap = 0.0, 0.10
    for i, nm in enumerate(notes):  # tinkles: each gap 18% shorter, each note a step higher
        add(b, glass(hz(nm), 0.3, tau=0.10 + 0.015 * (i % 2)), t0, 0.45 + 0.30 * i / (len(notes) - 1))
        t0, gap = t0 + gap, gap * 0.82
    whoosh = band_noise(n, rng, lambda T: 1400.0 * (4500.0 / 1400.0) ** np.clip(T / 0.48, 0, 1), 1.6,
                        lambda T: smoothstep(T / 0.42) * (1.0 - smoothstep((T - 0.44) / 0.08)))
    add(b, norm_peak(whoosh), 0.0, 0.08)  # dots gathering
    t_bloop = 0.50
    blp = bubble(0.4, hz("A4"), hz("D5"), 0.03, 0.004, 0.13)
    blp += 0.18 * bubble(0.4, hz("A5"), hz("D6"), 0.03, 0.004, 0.07)
    add(b, blp, t_bloop, 0.95)
    return b


def c_creature_hop(rng, n):
    b = np.zeros(n)
    add(b, bubble(0.15, 560.0, hz("A5"), 0.020, 0.003, 0.038, wobble=0.035, wob_rate=24.0, wob_tau=0.04),
        0.0, 1.0)
    add(b, bubble(0.10, 820.0, hz("D6"), 0.012, 0.002, 0.022), 0.05, 0.35)
    return b


def _chirp(rng, n, f0, va, vb, morph, amp, vib, slope=1.45):
    v = voice(f0, va, vb, morph, amp, slope=slope, vib=vib)
    return v + 0.025 * norm_peak(breath(n, rng, va, vb, morph, amp)) * np.max(np.abs(v))


def c_creature_chirp_1(rng, n):  # up: "oo-ee" lift
    t = tline(n)
    f0 = glide(n, [(0.02, hz("E5")), (0.19, hz("B5"))])
    amp = env_pts(n, [(0.0, 0.0), (0.025, 1.0), (0.17, 0.85), (n / SR, 0.0)])
    return _chirp(rng, n, f0, "oo", "ee", smoothstep(t / 0.2), amp, (7.2, 0.012, 0.06))


def c_creature_chirp_2(rng, n):  # down: "ee-oo" droop
    t = tline(n)
    f0 = glide(n, [(0.03, hz("B5")), (0.24, hz("E5"))])
    amp = env_pts(n, [(0.0, 0.0), (0.02, 1.0), (0.20, 0.8), (n / SR, 0.0)])
    return _chirp(rng, n, f0, "ee", "oo", smoothstep((t - 0.03) / 0.21), amp, (6.5, 0.015, 0.08))


def c_creature_chirp_3(rng, n):  # up-down: "ah-oo" arc
    t = tline(n)
    f0 = glide(n, [(0.02, hz("F#5")), (0.13, hz("D6")), (0.30, hz("A5"))])
    amp = env_pts(n, [(0.0, 0.0), (0.03, 1.0), (0.13, 0.85), (0.28, 0.5), (n / SR, 0.0)])
    return _chirp(rng, n, f0, "ah", "oo", smoothstep((t - 0.15) / 0.17), amp, (7.0, 0.010, 0.06))


def c_creature_chirp_4(rng, n):  # trill: B5 <-> D6 warble, speeding up 11 -> 15 Hz
    t = tline(n)
    rate = 11.0 + 4.0 * t / (n / SR)
    s = np.tanh(2.5 * np.sin(phase_of(rate))) / np.tanh(2.5)  # dwells on the two notes
    lo, hi = math.log(hz("B5")), math.log(hz("D6"))
    f0 = np.exp(lo + (hi - lo) * (0.5 + 0.5 * s))
    amp = env_pts(n, [(0.0, 0.0), (0.03, 1.0), (0.30, 0.9), (n / SR, 0.0)]) * (0.85 + 0.15 * (0.5 + 0.5 * s))
    return _chirp(rng, n, f0, "ee", "eh", smoothstep(t / 0.4), amp, (6.0, 0.004, 0.1))


def c_creature_dive(rng, n):
    t = tline(n)
    f = glide(n, [(0.03, hz("B5")), (0.62, hz("D4"))], ease=lambda u: u ** 1.5)  # sinks faster and faster
    swirl_ph = phase_of(7.0 + 9.0 * np.clip(t / 0.7, 0, 1))  # swirl speeds up 7 -> 16 Hz
    depth = 0.03 * smoothstep(t / 0.1)
    p1 = phase_of(f * (1.0 + depth * np.sin(swirl_ph)))
    p2 = phase_of(1.5 * f * (1.0 + depth * np.cos(swirl_ph)))  # a fifth above, quarter-cycle apart
    tone = np.sin(p1) + 0.25 * np.sin(2 * p1) + 0.08 * np.sin(3 * p1) + 0.3 * np.sin(p2)
    tone *= env_pts(n, [(0.0, 0.0), (0.02, 1.0), (0.30, 0.85), (0.66, 0.0)])
    bed_like = lambda F: hp_mag(F, 300.0, 2) * lp_mag(F, 4000.0, 3) / np.sqrt(np.maximum(F, 50.0))
    noise = shaped_noise(n, rng, lambda T, F: (
        smoothstep((T - 0.05) / 0.40) * (1.0 - smoothstep((T - 0.45) / 0.35))
        * bp_mag(F, 2500.0 * (600.0 / 2500.0) ** np.clip(T / 0.75, 0, 1), 0.8) * bed_like(F)))
    noise = norm_peak(noise) * (1.0 + 0.4 * np.sin(swirl_ph))  # the noise swirls too
    return tone + 0.30 * noise


def c_creature_peek(rng, n):  # "hm?"
    t = tline(n)
    f0 = glide(n, [(0.03, 620.0), (0.07, hz("E5")), (0.17, hz("E5")), (0.33, hz("B5"))])
    morph = 0.6 * smoothstep((t - 0.16) / 0.16)  # closed "m" opening a little as it lifts
    amp = env_pts(n, [(0.0, 0.0), (0.035, 0.6), (0.15, 0.55), (0.24, 0.95), (0.32, 0.8), (n / SR, 0.0)])
    v = voice(f0, "mm", "eh", morph, amp, slope=1.6, vib=(6.0, 0.008, 0.2))
    h = band_noise(n, rng, lambda T: 2200.0 + 0 * T, 1.2,
                   lambda T: smoothstep(T / 0.012) * (1.0 - smoothstep((T - 0.03) / 0.04)))
    return v + 0.06 * norm_peak(h) * np.max(np.abs(v))


def c_new_kind(rng, n):
    b = np.zeros(n)
    notes = ["D6", "E6", "F#6", "A6", "B6", "D7"]
    times = [0.0, 0.065, 0.13, 0.195, 0.26, 0.34]
    amps = [0.55, 0.58, 0.62, 0.68, 0.74, 0.95]
    for i, (nm, t0, a) in enumerate(zip(notes, times, amps)):
        add(b, glock(hz(nm), 0.9, tau=0.5 if i < 5 else 0.75), t0, a)
    t = tline(n)
    pad = np.sin(2 * np.pi * hz("D5") * t) + 0.7 * np.sin(2 * np.pi * hz("A5") * t) + 0.3 * np.sin(
        2 * np.pi * hz("D6") * t)
    b += 0.14 * pad * env_pts(n, [(0.30, 0.0), (0.45, 1.0), (0.80, 0.6), (1.2, 0.0)])  # soft tail
    return b


def c_personal_best(rng, n):
    b = np.zeros(n)
    for i, (nm, t0) in enumerate([("D5", 0.00), ("F#5", 0.07), ("A5", 0.14), ("D6", 0.21)]):
        add(b, marimba(hz(nm), 0.7, tau=0.24), t0, 0.50 + 0.05 * i)
        add(b, glock(2 * hz(nm), 0.7, tau=0.3), t0, 0.10)
    tc = 0.30  # held chord: D major, warm pad + bass
    env = env_pts(n, [(tc, 0.0), (tc + 0.06, 1.0), (1.20, 0.78), (1.75, 0.0)])
    for nm, g in [("D4", 0.42), ("A4", 0.36), ("D5", 0.40), ("F#5", 0.30), ("A5", 0.26)]:
        b += g * pad_note(hz(nm), n, rng, env)
    t = tline(n)
    b += 0.30 * env * (np.sin(2 * np.pi * hz("D3") * t) + 0.3 * np.sin(4 * np.pi * hz("D3") * t))
    for nm, g in [("D6", 0.22), ("F#6", 0.18), ("A6", 0.16)]:  # chord strike
        add(b, glock(hz(nm), 1.4, tau=0.5), tc, g)
    add(b, kalimba(hz("D5"), 1.0, tau=0.3), tc, 0.30)
    pool = ["D7", "E7", "F#7", "A7", "B7"]  # shimmer: random high glass pings over the chord
    for tt in np.sort(rng.uniform(0.36, 1.45, 18)):
        g = rng.uniform(0.035, 0.08) * (1.0 - 0.4 * (tt - 0.36) / 1.1)
        add(b, glass(hz(pool[rng.integers(len(pool))]), 0.4, tau=0.08), tt, g)
    air = band_noise(n, rng, lambda T: 7000.0 + 0 * T, 1.0,
                     lambda T: smoothstep((T - 0.3) / 0.3) * (1.0 - smoothstep((T - 1.0) / 0.6)))
    return b + 0.012 * norm_peak(air)


def c_score_reveal(rng, n):
    b = np.zeros(n)
    for i, (nm, g) in enumerate([("D5", 0.8), ("F#5", 0.6), ("A5", 0.6), ("E6", 0.42)]):
        add(b, chime(hz(nm), 0.8, tau=0.55), 0.018 * i, g)  # soft strum, D add9
    t = tline(n)
    b += 0.30 * np.sin(2 * np.pi * hz("D4") * t) * perc_env(n, 0.05, 0.30)  # warm bloom underneath
    return b


def c_level_up(rng, n):
    b = np.zeros(n)
    for nm, t0, g, tau in [("A4", 0.0, 0.80, 0.20), ("D5", 0.085, 0.90, 0.22), ("F#5", 0.17, 1.0, 0.32)]:
        add(b, marimba(hz(nm), 0.6, tau=tau), t0, g)
    add(b, glock(hz("F#6"), 0.45, tau=0.3), 0.17, 0.15)
    return b


def c_ui_hover(rng, n):
    b = np.zeros(n)
    add(b, modal(hz("E6"), n, [(1.0, 1.0, 1.0), (2.76, 0.12, 0.4)], attack=0.001, tau=0.008), 0.002)
    return b


def c_ui_select(rng, n):
    b = np.zeros(n)
    add(b, bubble(0.12, 520.0, hz("A5"), 0.012, 0.003, 0.028), 0.003, 1.0)
    add(b, sine_ping(hz("A4"), 0.12, 0.004, 0.03), 0.003, 0.35)
    return b


def c_break_chime(rng, n):
    b = np.zeros(n)
    add(b, bell(hz("D5"), n, tau=0.85), 0.0)
    return b


def c_eye_close(rng, n):
    t = tline(n)
    f0 = glide(n, [(0.03, hz("A5")), (0.38, hz("D5"))])
    cutoff = 3500.0 * (900.0 / 3500.0) ** smoothstep((t - 0.03) / 0.35)  # closing brightness
    amp = env_pts(n, [(0.0, 0.0), (0.025, 1.0), (0.15, 0.8), (n / SR, 0.0)])
    v = soft_tone(f0, cutoff, vib=(6.0, 0.015 - 0.012 * t / (n / SR), 0.03)) * amp
    air = band_noise(n, rng, lambda T: 3000.0 * (800.0 / 3000.0) ** np.clip(T / 0.4, 0, 1), 1.2,
                     lambda T: np.interp(T, t, amp))
    return v + 0.05 * norm_peak(air) * np.max(np.abs(v))


def c_eye_open(rng, n):
    t = tline(n)
    f0 = glide(n, [(0.02, hz("D5")), (0.30, hz("A5"))])
    cutoff = 900.0 * (3500.0 / 900.0) ** smoothstep((t - 0.02) / 0.3)  # opening brightness
    amp = env_pts(n, [(0.0, 0.0), (0.06, 0.7), (0.25, 1.0), (n / SR, 0.0)])
    v = soft_tone(f0, cutoff, vib=(6.0, 0.004 + 0.008 * t / (n / SR), 0.05)) * amp
    for nm, t0, g in [("A6", 0.10, 0.18), ("B6", 0.16, 0.20), ("D7", 0.22, 0.24), ("F#7", 0.28, 0.26)]:
        add(v, glass(hz(nm), 0.3, tau=0.07), t0, g)
    return v


def c_catch_hold(rng, n):
    t = tline(n)
    dur = n / SR
    u = t / dur
    f0 = glide(n, [(0.0, hz("D5")), (0.48, hz("A5"))], ease=False)
    trem = 1.0 - 0.18 * (0.5 - 0.5 * np.cos(phase_of(5.0 + 8.0 * u)))  # pulse quickens 5 -> 13 Hz
    v = soft_tone(f0, 1500.0 * (3500.0 / 1500.0) ** u, vib=(6.0, 0.004, 0.05))
    v += 0.25 * u * np.sin(phase_of(1.5 * f0))  # the fifth above grows in
    amp = (0.3 + 0.7 * u ** 1.5) * trem  # crescendo
    air = band_noise(n, rng, lambda T: 1500.0 * (4500.0 / 1500.0) ** np.clip(T / dur, 0, 1), 1.4,
                     lambda T: 0.3 + 0.7 * np.clip(T / dur, 0, 1) ** 1.5)
    v = v * amp
    return v + 0.04 * norm_peak(air) * np.max(np.abs(v))


def c_catch_success(rng, n):
    b = np.zeros(n)
    add(b, kalimba(hz("D5"), 0.5, tau=0.16), 0.0, 0.70)
    add(b, marimba(hz("A4"), 0.5, tau=0.14), 0.0, 0.35)
    add(b, kalimba(hz("A5"), 0.5, tau=0.22), 0.09, 0.85)
    add(b, kalimba(hz("D6"), 0.5, tau=0.18), 0.09, 0.40)
    add(b, marimba(hz("D5"), 0.5, tau=0.16), 0.09, 0.30)
    for nm, t0, g in [("A6", 0.17, 0.16), ("D7", 0.20, 0.18), ("F#7", 0.23, 0.20), ("A7", 0.26, 0.22)]:
        add(b, glass(hz(nm), 0.3, tau=0.07), t0, g)
    air = band_noise(n, rng, lambda T: 7000.0 + 0 * T, 1.0,
                     lambda T: smoothstep((T - 0.16) / 0.06) * (1.0 - smoothstep((T - 0.25) / 0.2)))
    return b + 0.01 * norm_peak(air)


# ----------------------------------------------------------------------------------------------
# static_bed: periodic band-limited pink noise with slow breathing (seamless by construction)
# ----------------------------------------------------------------------------------------------
def make_bed(dur=8.0):
    rng = np.random.default_rng(zlib.crc32(b"static_bed"))
    N = n_of(dur)
    f = np.fft.rfftfreq(N, 1.0 / SR)
    band = hp_mag(f, 300.0, 3) * lp_mag(f, 4000.0, 3) / np.sqrt(np.maximum(f, 20.0))  # pink, 300 Hz-4 kHz
    split = lp_mag(f, 1200.0, 2)  # power-complementary low / high halves that breathe separately

    def periodic_noise(shape):
        X = shape * (rng.standard_normal(len(f)) + 1j * rng.standard_normal(len(f)))
        X[0] = 0.0
        X[-1] = 0.0
        return np.fft.irfft(X, N)  # an FFT of length N is circular: the result loops perfectly

    xl = periodic_noise(band * split)
    xh = periodic_noise(band * np.sqrt(1.0 - split ** 2))
    t = tline(N)

    def lfo(kmax=5):  # smooth random curve made only of whole cycles per loop -> periodic
        s = np.zeros(N)
        for k in range(1, kmax + 1):
            s += rng.uniform(0.4, 1.0) / k ** 0.7 * np.cos(2 * np.pi * k * t / dur + rng.uniform(0, 2 * np.pi))
        return s / np.max(np.abs(s))

    x = 10 ** (3.0 * lfo() / 20) * xl + 10 ** (4.0 * lfo() / 20) * xh  # +-3 dB low, +-4 dB high breathing
    x -= x.mean()
    return x * (10 ** (BED_RMS_DBFS / 20) / np.sqrt(np.mean(x ** 2)))


# ----------------------------------------------------------------------------------------------
# Mastering
# ----------------------------------------------------------------------------------------------
def fade_window(n, fade_in, fade_out):
    w = np.ones(n)
    a, b = max(1, n_of(fade_in)), max(1, n_of(fade_out))
    w[:a] = 0.5 - 0.5 * np.cos(np.pi * np.arange(a) / a)            # w[0] = 0
    w[n - b:] *= 0.5 + 0.5 * np.cos(np.pi * (np.arange(b) + 1) / b)  # w[-1] = 0
    return w


def dc_fix(x, w):
    """Remove DC without moving the faded ends off zero: subtract a scaled copy of the fade window."""
    return x - (np.mean(x) / np.mean(w)) * w


def sliding_max(a, half):
    """max(a[i-half : i+half+1]) for every i, O(n) (van Herk / Gil-Werman). a must be >= 0."""
    n, w = len(a), 2 * half + 1
    p = np.concatenate([np.zeros(half), a, np.zeros(half + w)])
    p = np.concatenate([p, np.zeros((-len(p)) % w)])
    blocks = p.reshape(-1, w)
    pre = np.maximum.accumulate(blocks, axis=1).ravel()
    suf = np.maximum.accumulate(blocks[:, ::-1], axis=1)[:, ::-1].ravel()
    return np.maximum(suf[:n], pre[w - 1:w - 1 + n])


def gain_ride(x, thr, hold=0.003, att=0.004, rel=0.08, smooth=0.003):
    """Look-ahead gain riding on a +-3 ms held peak envelope (so the gain never wiggles within a
    waveform cycle): gain dips smoothly (exponential approach, `att` before the peak) so the envelope
    stays under thr, then recovers with `rel`. A slow gain change, not a waveshaper: no added buzz."""
    env = sliding_max(np.abs(x), n_of(hold))
    need = np.minimum(1.0, thr / np.maximum(env, 1e-12)).tolist()
    ka, kr = 1.0 - math.exp(-1.0 / (att * SR)), 1.0 - math.exp(-1.0 / (rel * SR))
    g = need
    for i in range(len(g) - 2, -1, -1):  # backward: start dipping before the peak
        v = g[i + 1] + (1.0 - g[i + 1]) * ka
        if v < g[i]:
            g[i] = v
    for i in range(1, len(g)):  # forward: release
        v = g[i - 1] + (1.0 - g[i - 1]) * kr
        if v < g[i]:
            g[i] = v
    g = np.array(g)
    L = max(1, n_of(smooth))
    g = np.convolve(np.pad(g, (L, L), mode="edge"), np.ones(L) / L, mode="same")[L:-L]
    return x * g


def rms_db(x):
    return 20.0 * math.log10(math.sqrt(float(np.mean(np.square(x)))) + 1e-12)


def master(x, opt):
    if opt["mix"] > 0:
        x = reverb(x, opt["mix"])
    x = fft_filter(x, lambda f: hp_mag(f, 35.0, 2) * lp_mag(f, opt["lp"], opt["lp_order"]))
    x = x - x.mean()
    w = fade_window(len(x), opt["fi"], opt["fo"])
    x = dc_fix(x * w, w)
    y = norm_peak(x, PEAK)
    ride = 0.0
    if opt["match"] and rms_db(y) < LOUD_LO_DBFS:
        def ridden(db):
            return norm_peak(dc_fix(gain_ride(y, PEAK * 10 ** (-db / 20)), w), PEAK)
        lo, hi = 0.0, MAX_RIDE_DB
        if rms_db(ridden(hi)) < LOUD_LO_DBFS:
            lo = hi
        else:
            for _ in range(9):
                mid = 0.5 * (lo + hi)
                if rms_db(ridden(mid)) < LOUD_LO_DBFS:
                    lo = mid
                else:
                    hi = mid
        ride = hi
        y = ridden(ride)
    return y, ride


# ----------------------------------------------------------------------------------------------
# Clip table: name, duration (s), function, mastering options, recipe (printed in the report)
# ----------------------------------------------------------------------------------------------
def C(name, dur, fn, desc, mix=0.25, lp=8500.0, lp_order=3, fi=0.005, fo=None, match=True):
    return dict(name=name, dur=dur, fn=fn, desc=desc, mix=mix, lp=lp, lp_order=lp_order, fi=fi,
                fo=fo if fo is not None else max(0.02, 0.18 * dur), match=match)


SPARKLE_LP = 14000.0
CLIPS = [
    C("pick_right", 0.35, c_pick_right,
      "kalimba D5 then A5 85 ms later (sine modes 1, 2, 6.27x; tau 0.15/0.19 s) + faint A6 sine shine"),
    C("pick_wrong", 0.30, c_pick_wrong,
      "two muted wooden toks A3 -> F#3 (115 ms apart): 2-mode bar, tau 40 ms, 5% pitch drop, bandpassed "
      "noise knock, low-passed at 3 kHz", mix=0.18, lp=3000.0, lp_order=2),
    C("creature_condense", 0.90, c_creature_condense,
      "9 glass pings D6..A7 up the scale, gaps shrinking 18% each, over a rising band-noise whoosh; "
      "bubble 'bloop' A4->D5 (+A5->D6 octave) at 0.50 s", lp=SPARKLE_LP, lp_order=2, fo=0.2),
    C("creature_hop", 0.15, c_creature_hop,
      "bubble sine flying 560 Hz -> A5 with a decaying 24 Hz pitch wobble (boing) + a tiny D6 bubble at 50 ms",
      fo=0.03),
    C("creature_chirp_1", 0.26, c_creature_chirp_1,
      "voice UP: harmonics of f0 gliding E5->B5, vowel morph oo->ee, 7.2 Hz vibrato, 2.5% breath", fo=0.03),
    C("creature_chirp_2", 0.30, c_creature_chirp_2,
      "voice DOWN: f0 B5->E5, vowel ee->oo, 6.5 Hz vibrato, 2.5% breath", fo=0.03),
    C("creature_chirp_3", 0.36, c_creature_chirp_3,
      "voice UP-DOWN: f0 F#5->D6->A5, vowel ah->oo, 7 Hz vibrato, 2.5% breath", fo=0.03),
    C("creature_chirp_4", 0.40, c_creature_chirp_4,
      "voice TRILL: f0 warbles B5<->D6 (tanh-shaped, 11->15 Hz), vowel ee->eh, synced amplitude flutter",
      fo=0.04),
    C("creature_dive", 0.80, c_creature_dive,
      "tone + fifth swirling (7->16 Hz pitch wobble) and sinking B5->D4, crossfading into bed-coloured "
      "noise whose band sweeps 2.5 kHz->600 Hz", fo=0.15),
    C("creature_peek", 0.40, c_creature_peek,
      "voice 'hm?': breathy 'h' noise, closed 'mm' hum on E5, then lift to B5 while opening toward 'eh'",
      fo=0.04),
    C("new_kind", 1.20, c_new_kind,
      "glockenspiel D6 E6 F#6 A6 B6 D7 every 65 ms (modes 1, 2.76, 5.40, 8.93) + soft D5/A5 sine pad tail",
      mix=0.32, lp=9500.0, fo=0.35),
    C("personal_best", 1.80, c_personal_best,
      "marimba+glock arpeggio D5 F#5 A5 D6, then held D-major pad (detuned soft harmonics, D3 bass) with "
      "glock/kalimba strike, 18 random high glass pings and a faint 7 kHz air shimmer",
      mix=0.30, lp=SPARKLE_LP, lp_order=2, fo=0.3),
    C("score_reveal", 0.80, c_score_reveal,
      "warm chime chord D5 F#5 A5 E6 strummed 18 ms apart (near-harmonic modes, 4 ms attack) + D4 sine bloom",
      mix=0.32, fo=0.25),
    C("level_up", 0.60, c_level_up,
      "marimba A4 D5 F#5 at 85 ms steps, last note rings longer, glock F#6 doubling", fo=0.15),
    C("ui_hover", 0.04, c_ui_hover,
      "E6 sine tick (tau 8 ms) with a faint 2.76x partial", mix=0.10, fo=0.012, match=False),
    C("ui_select", 0.12, c_ui_select,
      "bubble pop 520 Hz -> A5 (tau 28 ms) over a soft A4 body", mix=0.12, fo=0.03, match=False),
    C("break_chime", 1.50, c_break_chime,
      "single D5 'major' bell: hum D4, prime, fifth, octave, tenth, twelfth + 2 faint inharmonic partials, "
      "slow beating twins, tau 0.85 s", mix=0.32, fo=0.45),
    C("eye_close", 0.45, c_eye_close,
      "soft harmonic tone sliding A5->D5 as its low-pass closes 3.5 kHz->900 Hz, vibrato settling, "
      "falling breath", fo=0.08),
    C("eye_open", 0.45, c_eye_open,
      "mirror of eye_close (D5->A5, low-pass opening) + glass sparkle A6 B6 D7 F#7", lp=SPARKLE_LP,
      lp_order=2, fo=0.1),
    C("catch_hold", 0.50, c_catch_hold,
      "soft tone rising D5->A5 (linear in pitch) with growing fifth, crescendo, tremolo quickening "
      "5->13 Hz, rising breath", fo=0.04),
    C("catch_success", 0.60, c_catch_success,
      "kalimba D5 (+marimba A4), then kalimba A5+D6 (+marimba D5), then glass sparkle A6 D7 F#7 A7",
      lp=SPARKLE_LP, lp_order=2, fo=0.15),
]
BED_DESC = ("8 s periodic pink noise 300 Hz-4 kHz built in the FFT domain, split at 1.2 kHz into two "
            "bands breathing independently (+-3 / +-4 dB, 1-5 whole cycles per loop), -30 dBFS RMS")


# ----------------------------------------------------------------------------------------------
# WAV I/O
# ----------------------------------------------------------------------------------------------
def write_wav(path, x):
    data = np.clip(np.round(x * 32767.0), -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())


def read_wav(path):
    with wave.open(path, "rb") as w:
        assert w.getnchannels() == 1 and w.getsampwidth() == 2 and w.getframerate() == SR
        raw = w.readframes(w.getnframes())
    return np.frombuffer(raw, dtype="<i2").astype(np.float64) / 32768.0


# ----------------------------------------------------------------------------------------------
# Self-check
# ----------------------------------------------------------------------------------------------
def stats(x):
    n = len(x)
    X = np.abs(np.fft.rfft(x)) ** 2
    f = np.fft.rfftfreq(n, 1.0 / SR)
    hf = 10 * math.log10((X[f > 8000].sum() + 1e-30) / (X.sum() + 1e-30))
    L = n_of(0.05)
    st = max(rms_db(x[i:i + L]) for i in range(0, max(1, n - L + 1), max(1, L // 4)))
    d = np.abs(np.diff(x))
    return dict(dur=n / SR, peak=20 * math.log10(np.max(np.abs(x)) + 1e-12), rms=rms_db(x), st=st,
                dc=float(np.mean(x)), jump=float(d.max()), jump_at=int(d.argmax()) / SR,
                edge=int(round(max(abs(x[0]), abs(x[-1])) * 32768)), hf=hf)


# ----------------------------------------------------------------------------------------------
# Contact sheet
# ----------------------------------------------------------------------------------------------
_MAGMA = np.array([[0, 0, 4], [28, 16, 68], [79, 18, 123], [129, 37, 129], [181, 54, 122],
                   [229, 80, 100], [251, 135, 97], [254, 194, 135], [252, 253, 191]], float)
FMIN, FMAXS, DB_LO, DB_HI = 40.0, 20000.0, -105.0, -6.0


def _font(size, bold=False):
    from PIL import ImageFont
    names = ["arialbd.ttf", "segoeuib.ttf"] if bold else ["arial.ttf", "segoeui.ttf"]
    for nm in names:
        for p in (os.path.join(r"C:\Windows\Fonts", nm), nm):
            try:
                return ImageFont.truetype(p, size)
            except OSError:
                pass
    return ImageFont.load_default()


def _colorize(v):
    xs = np.linspace(0, 1, len(_MAGMA))
    return np.stack([np.interp(v, xs, _MAGMA[:, c]) for c in range(3)], axis=-1).astype(np.uint8)


def _f_to_y(f, H):
    return (H - 1) * math.log(FMAXS / f) / math.log(FMAXS / FMIN)


def spectro_rgb(x, W, H):
    n = len(x)
    dur = n / SR
    nfft = 2048 if dur >= 0.6 else (1024 if dur >= 0.12 else 512)
    win = 0.5 - 0.5 * np.cos(2 * np.pi * np.arange(nfft) / nfft)
    pad = np.concatenate([np.zeros(nfft // 2), x, np.zeros(nfft // 2 + 1)])
    centers = np.linspace(0, n - 1, W).astype(int)
    frames = pad[centers[:, None] + np.arange(nfft)[None, :]] * win
    db = 20 * np.log10(np.abs(np.fft.rfft(frames, axis=1)) * 2.0 / win.sum() + 1e-12)
    rowf = FMAXS * (FMIN / FMAXS) ** (np.arange(H) / (H - 1))
    fb = rowf / (SR / nfft)
    i0 = np.clip(np.floor(fb).astype(int), 0, db.shape[1] - 2)
    fr = np.clip(fb - i0, 0, 1)
    img = (db[:, i0] * (1 - fr) + db[:, i0 + 1] * fr).T  # (H, W)
    return _colorize(np.clip((img - DB_LO) / (DB_HI - DB_LO), 0, 1))


def draw_panel(img, draw, x0, y0, title, sub, x, W, H, WH, fonts, marks=()):
    from PIL import Image
    f_title, f_small = fonts
    draw.text((x0, y0), title, fill=(235, 235, 240), font=f_title)
    draw.text((x0, y0 + 17), sub, fill=(160, 165, 180), font=f_small)
    sy = y0 + 36
    img.paste(Image.fromarray(spectro_rgb(x, W, H)), (x0, sy))
    for fq, lab, col in [(100, "100", (120, 120, 130)), (1000, "1k", (120, 120, 130)),
                         (8000, "8k", (70, 200, 220))]:
        yy = sy + _f_to_y(fq, H)
        for xx in range(x0, x0 + W, 6 if fq == 8000 else 3):
            draw.point((xx, yy), fill=col)
        draw.text((x0 + 2, yy - 12), lab, fill=col, font=f_small)
    dur = len(x) / SR
    step = 0.01 if dur <= 0.05 else (0.05 if dur <= 0.5 else 0.1)
    k = 0
    while k * step <= dur + 1e-9:
        xx = x0 + int(round(k * step / dur * (W - 1)))
        draw.line([(xx, sy + H - (6 if k % 5 == 0 else 3)), (xx, sy + H - 1)], fill=(200, 200, 210))
        k += 1
    wy = sy + H + 2  # waveform strip, full scale = half height; dotted lines at -3 dBFS
    draw.rectangle([x0, wy, x0 + W - 1, wy + WH - 1], fill=(18, 18, 26))
    mid = wy + WH / 2
    for lvl in (PEAK, -PEAK):
        yy = mid - lvl * (WH / 2 - 1)
        for xx in range(x0, x0 + W, 4):
            draw.point((xx, yy), fill=(90, 90, 110))
    edges = np.linspace(0, len(x), W + 1).astype(int)
    halo = n_of(0.003)  # look at least +-3 ms around each column so low tones do not alias the drawing
    for i in range(W):
        seg = x[max(0, min(edges[i], (edges[i] + edges[i + 1]) // 2 - halo)):
                max(edges[i + 1], (edges[i] + edges[i + 1]) // 2 + halo, edges[i] + 1)]
        lo, hi = float(seg.min()), float(seg.max())
        col = (255, 60, 60) if max(abs(lo), abs(hi)) > 0.999 else (120, 200, 160)
        draw.line([(x0 + i, mid - hi * (WH / 2 - 1)), (x0 + i, mid - lo * (WH / 2 - 1))], fill=col)
    for m in marks:
        xx = x0 + int(m * (W - 1))
        draw.line([(xx, sy), (xx, wy + WH - 1)], fill=(255, 255, 255))


def draw_sheet(entries, seam, path):
    from PIL import Image, ImageDraw
    COLS, W, H, WH, GAP, HEAD = 4, 440, 150, 34, 18, 36
    cell_w, cell_h = W + GAP, HEAD + H + WH + 2 + GAP
    cells = len(entries) + 2
    rows = (cells + COLS - 1) // COLS
    TOP = 46
    img = Image.new("RGB", (GAP + COLS * cell_w, TOP + rows * cell_h + 8), (10, 10, 16))
    draw = ImageDraw.Draw(img)
    fonts = (_font(14, bold=True), _font(11))
    draw.text((GAP, 12), "Cyclopea sound pack - log-frequency spectrograms (40 Hz - 20 kHz, dBFS colour), "
              "waveform below (dotted = -3 dBFS, red = clipping). Ticks every 10/50/100 ms.",
              fill=(235, 235, 240), font=_font(15, bold=True))
    for i, (name, x, sub) in enumerate(entries):
        r, c = divmod(i, COLS)
        draw_panel(img, draw, GAP + c * cell_w, TOP + r * cell_h, name, sub, x, W, H, WH, fonts)
    i = len(entries)
    r, c = divmod(i, COLS)
    draw_panel(img, draw, GAP + c * cell_w, TOP + r * cell_h, "static_bed loop seam",
               "last 1.0 s -> first 1.0 s (white line = seam)", seam, W, H, WH, fonts, marks=(0.5,))
    r, c = divmod(i + 1, COLS)
    x0, y0 = GAP + c * cell_w, TOP + r * cell_h
    draw.text((x0, y0), "colour scale (dBFS per FFT bin)", fill=(235, 235, 240), font=fonts[0])
    bar = _colorize(np.tile(np.linspace(0, 1, W)[None, :], (24, 1)))
    img.paste(Image.fromarray(bar), (x0, y0 + 36))
    for v in range(int(DB_LO), int(DB_HI) + 1, 20):
        xx = x0 + int((v - DB_LO) / (DB_HI - DB_LO) * (W - 1))
        draw.text((xx, y0 + 64), str(v), fill=(200, 200, 210), font=fonts[1])
    draw.text((x0, y0 + 90), "cyan dotted line = 8 kHz (harshness limit; sparkles may cross)\n"
              "key: D major pentatonic (D E F# A B)", fill=(160, 165, 180), font=fonts[1])
    img.save(path)


# ----------------------------------------------------------------------------------------------
# Main
# ----------------------------------------------------------------------------------------------
def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    rides = {}
    # static bed: no fades (it loops), only RMS-normalised
    write_wav(os.path.join(OUT_DIR, "static_bed.wav"), make_bed())
    for c in CLIPS:
        rng = np.random.default_rng(zlib.crc32(c["name"].encode()))
        n = n_of(c["dur"])
        dry = c["fn"](rng, n)
        assert len(dry) == n, c["name"]
        y, rides[c["name"]] = master(dry, c)
        write_wav(os.path.join(OUT_DIR, c["name"] + ".wav"), y)

    # ---- read everything back and check it ----
    names = ["static_bed"] + [c["name"] for c in CLIPS]
    data = {nm: read_wav(os.path.join(OUT_DIR, nm + ".wav")) for nm in names}
    st = {nm: stats(data[nm]) for nm in names}
    print(f"\nWrote {len(names)} clips to {OUT_DIR}\n")
    hdr = (f"{'file':<22}{'dur s':>6}{'peak':>7}{'RMS':>7}{'RMS50':>7}{'DC':>10}{'jump':>7}{'at s':>6}"
           f"{'edge':>5}{'>8k':>6}{'ride':>5}  flags")
    print(hdr)
    print("-" * len(hdr))
    matched = [c["name"] for c in CLIPS if c["match"]]
    spark = {c["name"] for c in CLIPS if c["lp"] >= SPARKLE_LP}
    for nm in names:
        s = st[nm]
        flags = []
        if nm != "static_bed" and abs(s["peak"] - PEAK_DBFS) > 0.05:
            flags.append("PEAK")
        if abs(s["dc"]) > 1e-4:
            flags.append("DC")
        if nm != "static_bed" and s["edge"] > 2:
            flags.append("EDGE")
        if nm != "static_bed" and nm not in spark and s["hf"] > -30:
            flags.append("HF")
        if s["peak"] > -0.1:
            flags.append("CLIP")
        print(f"{nm + '.wav':<22}{s['dur']:>6.3f}{s['peak']:>7.2f}{s['rms']:>7.2f}{s['st']:>7.2f}"
              f"{s['dc']:>10.1e}{s['jump']:>7.3f}{s['jump_at']:>6.3f}{s['edge']:>5d}{s['hf']:>6.0f}"
              f"{rides.get(nm, 0.0):>5.1f}  {' '.join(flags) or 'ok'}")
    r = [st[nm]["rms"] for nm in matched]
    print(f"\npeak = sample peak dBFS; RMS = full-clip RMS dBFS; RMS50 = loudest 50 ms window; DC = mean "
          f"(full scale = 1); jump = largest |x[n+1]-x[n]| (full scale = 1); edge = |first/last sample| in "
          f"LSB; >8k = share of energy above 8 kHz (dB); ride = gain riding applied (dB).")
    print(f"Loudness-matched clips ({len(matched)}, excludes static_bed, ui_hover, ui_select): full-clip RMS "
          f"{min(r):.2f} .. {max(r):.2f} dBFS, spread {max(r) - min(r):.2f} dB "
          f"({'ok' if max(r) - min(r) <= LOUD_SPAN_DB + 1e-9 else 'OVER'} vs {LOUD_SPAN_DB:.0f} dB)")

    bed = data["static_bed"]
    d = np.abs(np.diff(bed))
    seam = abs(bed[0] - bed[-1])
    L = n_of(0.05)
    print(f"\nstatic_bed loop seam: |x[0] - x[-1]| = {seam:.5f} FS ({seam * 32768:.0f} LSB); interior jumps "
          f"median {np.median(d):.5f}, 99.9th pct {np.percentile(d, 99.9):.5f}, max {d.max():.5f}. "
          f"seam/median = {seam / np.median(d):.2f}. RMS last 50 ms {rms_db(bed[-L:]):.2f} dBFS vs first "
          f"50 ms {rms_db(bed[:L]):.2f} dBFS. Seam jump percentile among interior jumps: "
          f"{100.0 * np.mean(d < seam):.1f}%")

    N = 1 << 18
    H = np.abs(np.fft.rfft(room_ir(), N))
    g = [20 * math.log10(H[int(round(hz(f"{p}{o}") * N / SR))]) for o in range(3, 8)
         for p in ("D", "E", "F#", "A", "B") if hz(f"{p}{o}") < 4000]
    print(f"room IR: RT60 {ROOM_RT60} s; wet gain at every scale note D3..B6 spans {min(g):+.1f} .. "
          f"{max(g):+.1f} dB around the nominal mix (std {np.std(g):.1f} dB)")

    print("\nRecipes:")
    print(f"  {'static_bed':<18} {BED_DESC}")
    for c in CLIPS:
        print(f"  {c['name']:<18} {c['desc']}")

    entries = []
    for nm in names:
        s = st[nm]
        entries.append((nm, data[nm], f"{s['dur']:.2f} s   peak {s['peak']:.1f}   RMS {s['rms']:.1f} dBFS"
                                      f"   >8k {s['hf']:.0f} dB"))
    seam_sig = np.concatenate([bed[-n_of(1.0):], bed[:n_of(1.0)]])
    draw_sheet(entries, seam_sig, SHEET_PATH)
    print(f"\nContact sheet: {SHEET_PATH}")


if __name__ == "__main__":
    main()
