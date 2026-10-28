"""Fake hand sketches generated from drawing geometry, for evaluation and training.

Sketches get wobbly lines, up to 15% distorted proportions, about half their small
holes left out, a few degrees of tilt, random line weight and pen darkness, and
are sometimes filled in as a silhouette.
"""
import numpy as np
from PIL import Image, ImageDraw

import sketch_common as sc


def sketchify(polys, rng, reorient=True):
    """A fake hand sketch of a drawing as a grayscale image. With reorient=False the
    sketch keeps the drawing's orientation apart from a few degrees of tilt."""
    lo, hi = sc.bounds(polys)
    span = max(hi - lo)
    kept = []
    for p in polys:
        a = np.array(p)
        closed = np.hypot(*(a[0] - a[-1])) < 1e-3 * span
        if closed and max(a.max(0) - a.min(0)) < 0.08 * span and rng.random() < 0.5:
            continue
        kept.append(a)
    if not kept:
        kept = [np.array(p) for p in polys]

    angle = (rng.choice([0, 90, 180, 270]) if reorient else 0) + rng.gauss(0, 3)
    t = np.radians(angle)
    rot = np.array([[np.cos(t), -np.sin(t)], [np.sin(t), np.cos(t)]])
    stretch = np.diag([rng.uniform(0.85, 1.15), 1.0])
    mirror = np.diag([-1.0, 1.0]) if reorient and rng.random() < 0.25 else np.eye(2)
    m = rot @ stretch @ mirror

    nprng = np.random.default_rng(rng.randrange(1 << 30))
    amp = span * rng.uniform(0.001, 0.005)
    shaky = []
    for a in kept:
        seg = np.hypot(*np.diff(a, axis=0).T)
        length = np.concatenate([[0], np.cumsum(seg)])
        n = max(8, int(length[-1] / (span * 0.004)))
        s = np.linspace(0, length[-1], n)
        pts = np.stack([np.interp(s, length, a[:, 0]), np.interp(s, length, a[:, 1])], axis=1)
        noise = nprng.normal(size=(n + 30, 2))
        kernel = np.hanning(31) / np.hanning(31).sum()
        smooth = np.stack([np.convolve(noise[:, i], kernel, "valid") for i in range(2)], axis=1)[:n]
        smooth *= amp / (smooth.std() + 1e-12)
        shaky.append((pts - lo) @ m.T + smooth)

    allpts = np.concatenate(shaky)
    lo2, hi2 = allpts.min(0), allpts.max(0)
    scale = 700 / max(hi2 - lo2)
    w, h = int((hi2[0] - lo2[0]) * scale) + 100, int((hi2[1] - lo2[1]) * scale) + 100
    to_px = lambda a: [((x - lo2[0]) * scale + 50, h - ((y - lo2[1]) * scale + 50)) for x, y in a]

    if rng.random() < 0.2:  # filled silhouette, holes by even-odd
        ink = np.zeros((h, w), bool)
        for a in shaky:
            layer = Image.new("1", (w, h), 0)
            ImageDraw.Draw(layer).polygon(to_px(a), fill=1)
            ink ^= np.asarray(layer)
        img = Image.fromarray(np.where(ink, 0, 255).astype(np.uint8))
    else:
        img = Image.new("L", (w, h), 255)
        draw = ImageDraw.Draw(img)
        width, pen = rng.randint(2, 9), rng.randint(0, 120)
        for a in shaky:
            draw.line(to_px(a), fill=pen, width=width, joint="curve")
    return img
