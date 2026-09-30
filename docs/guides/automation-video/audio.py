"""
صدای فیلمِ آموزشی: موسیقیِ پس‌زمینه‌ی آرام + افکت‌ها، هم‌زمان با رویدادهای guide.html.

    python audio.py sfx.json out.wav

sfx.json را render.cjs از window.SFX می‌سازد: [{t, k, until?}] که k یکی از
click / key / pop / chime / whoosh / rise / type است. همه‌چیز با numpy ساخته
می‌شود؛ هیچ فایلِ صوتیِ بیرونی لازم نیست.
"""
import json
import sys
import wave

import numpy as np

SR = 44100
rng = np.random.default_rng(7)  # قطعی: هر بار همان صدا


def env(n, a, r):
    """پوشِ ساده: حمله‌ی a و رهاییِ r (ثانیه) روی n نمونه."""
    e = np.ones(n)
    na, nr = int(a * SR), int(r * SR)
    if na: e[:na] = np.linspace(0, 1, na)
    if nr: e[-nr:] *= np.linspace(1, 0, nr)
    return e


def tone(f, dur, amp=1.0, decay=None):
    t = np.arange(int(dur * SR)) / SR
    s = np.sin(2 * np.pi * f * t)
    if decay: s *= np.exp(-t / decay)
    return amp * s


def noise(dur):
    return rng.standard_normal(int(dur * SR))


def add(*xs):
    """جمعِ صداهایی با طولِ متفاوت."""
    out = np.zeros(max(len(x) for x in xs))
    for x in xs: out[:len(x)] += x
    return out


def lowpass(x, k):
    return np.convolve(x, np.ones(k) / k, mode='same')


# ── افکت‌ها ──
def sfx_click():
    return add(0.55 * lowpass(noise(0.03), 3) * env(int(0.03 * SR), 0.001, 0.025), tone(1800, 0.04, 0.25, 0.01))


def sfx_key():
    return add(0.5 * lowpass(noise(0.05), 6) * env(int(0.05 * SR), 0.001, 0.04), tone(420, 0.08, 0.35, 0.03))


def sfx_pop():
    t = np.arange(int(0.12 * SR)) / SR
    f = 500 + 900 * np.exp(-t / 0.02)
    return 0.45 * np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.035)


def sfx_chime():
    out = np.zeros(int(0.9 * SR))
    for i, f in enumerate([1046.5, 1318.5, 1568.0]):
        s = tone(f, 0.7, 0.28, 0.25) + tone(f * 2, 0.7, 0.06, 0.12)
        o = int(i * 0.08 * SR)
        out[o:o + len(s)] += s
    return out


def sfx_whoosh(dur=0.45):
    n = int(dur * SR)
    x = noise(dur)
    # جاروب با پهنای فیلترِ متغیر
    k = np.linspace(40, 4, n).astype(int)
    y = np.array([x[max(0, i - k[i]):i + 1].mean() for i in range(n)])
    return 0.9 * y * np.sin(np.linspace(0, np.pi, n)) ** 2


def sfx_rise():
    return 1.6 * sfx_whoosh(1.6)


def sfx_type(dur):
    out = np.zeros(int(dur * SR) + SR // 10)
    t = 0.0
    while t < dur:
        c = 0.22 * lowpass(noise(0.015), 2) * env(int(0.015 * SR), 0.0005, 0.012)
        o = int(t * SR)
        out[o:o + len(c)] += c * rng.uniform(0.6, 1.0)
        t += rng.uniform(0.055, 0.11)
    return out


# ── موسیقی ──
def music(total):
    n = int(total * SR)
    out = np.zeros(n)
    bpm = 92
    beat = 60 / bpm
    # Cmaj9 - Am9 - Fmaj9 - G6 (آرام و «فناورانه»)
    chords = [
        [130.81, 196.00, 246.94, 293.66, 329.63],
        [110.00, 164.81, 196.00, 246.94, 261.63],
        [87.31, 174.61, 220.00, 261.63, 329.63],
        [98.00, 146.83, 196.00, 246.94, 329.63],
    ]
    bar = 4 * beat
    t0 = 0.0
    ci = 0
    while t0 < total:
        ch = chords[ci % 4]
        dur = min(bar + 0.6, total - t0)
        m = int(dur * SR)
        tt = np.arange(m) / SR
        pad = np.zeros(m)
        for f in ch:
            det = 1 + rng.uniform(-0.002, 0.002)
            pad += np.sin(2 * np.pi * f * det * tt) + 0.35 * np.sin(2 * np.pi * 2 * f * tt + 0.3)
        pad *= env(m, 0.5, 0.6) * 0.035
        o = int(t0 * SR)
        out[o:o + m] += pad[:n - o]
        # آرپژ: یک نتِ کوتاه روی هر نیم‌ضرب
        arp = ch[1:] + ch[1:][::-1][1:-1]
        for j in range(8):
            ts = t0 + j * beat / 2
            if ts >= total: break
            f = arp[j % len(arp)] * 2
            s = tone(f, 0.5, 0.05, 0.16) + tone(f * 2, 0.5, 0.012, 0.08)
            o = int(ts * SR)
            out[o:o + len(s)] += s[:max(0, n - o)]
        # ضربِ نرمِ باس روی ضربِ اول و سوم
        for j in (0, 2):
            ts = t0 + j * beat
            if ts >= total: break
            s = tone(ch[0] / 2, 0.6, 0.12, 0.2)
            o = int(ts * SR)
            out[o:o + len(s)] += s[:max(0, n - o)]
        t0 += bar
        ci += 1
    out *= env(n, 1.5, 2.5)
    return out


def main():
    spec = json.load(open(sys.argv[1], encoding='utf-8'))
    total = float(spec['duration'])
    n = int(total * SR)
    L = music(total)
    R = L.copy()
    fx = np.zeros(n)
    gen = {'click': sfx_click, 'key': sfx_key, 'pop': sfx_pop, 'chime': sfx_chime,
           'whoosh': sfx_whoosh, 'rise': sfx_rise}
    for e in spec['events']:
        k, t = e['k'], float(e['t'])
        s = sfx_type(float(e['until']) - t) if k == 'type' else gen[k]()
        o = int(t * SR)
        if o >= n: continue
        fx[o:o + len(s)] += s[:n - o]
    L += 0.7 * fx
    R += 0.7 * fx
    # جابه‌جاییِ اندکِ استریو برای موسیقی
    R = np.roll(R, 220)
    st = np.stack([L, R], axis=1)
    st /= max(1e-9, np.abs(st).max()) / 0.89
    pcm = (st * 32767).astype(np.int16)
    with wave.open(sys.argv[2], 'wb') as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print('audio:', sys.argv[2], f'{total:.1f}s')


if __name__ == '__main__':
    main()
