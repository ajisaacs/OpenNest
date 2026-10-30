"""Shared geometry reading, image normalization and embedding for sketch search.

Drawings and sketches go through the same normalize() so the model compares like
with like: ink is reduced to 1px center lines, cropped, scaled to fit a square
canvas and re-stroked at a fixed width.
"""
import math
import re

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from skimage.filters import threshold_local
from skimage.morphology import binary_erosion, remove_small_objects, skeletonize

CANVAS = 224
MARGIN = 14
STROKE = 3
RENDER = 1024
FILLED_RATIO = 0.35

# Motions with these tags are marks or open break cuts, not part outline.
SKIPPED_TAGS = {"SCRIBE", "ETCH", "BREAK", "LEADIN", "LEADOUT"}
_WORD = re.compile(r"([A-Z])\s*(-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)")


def is_holdout(key):
    """About 10% of drawings, chosen by key, are never trained on and are what evaluation samples."""
    import hashlib
    return hashlib.md5(key.encode()).digest()[0] < 26


# ---------------------------------------------------------------- geometry

def _arc_points(sx, sy, ex, ey, cx, cy, clockwise):
    r = math.hypot(sx - cx, sy - cy)
    a0 = math.atan2(sy - cy, sx - cx)
    a1 = math.atan2(ey - cy, ex - cx)
    sweep = a1 - a0
    if clockwise:
        if sweep >= -1e-9:
            sweep -= 2 * math.pi
    elif sweep <= 1e-9:
        sweep += 2 * math.pi
    n = max(4, math.ceil(abs(sweep) / (math.pi / 36)))
    return [(cx + r * math.cos(a0 + sweep * i / n), cy + r * math.sin(a0 + sweep * i / n))
            for i in range(1, n + 1)]


def parse_program(text):
    """Returns the cut polylines of an OpenNest drawing program (G90/G91, G00-G03)."""
    absolute = True
    motion = 0
    x = y = 0.0
    polys = []
    current = None
    for raw in text.splitlines():
        code, _, tag = raw.partition(":")
        words = _WORD.findall(code.upper())
        if not words:
            continue
        values = {}
        for letter, number in words:
            if letter == "G":
                g = int(float(number))
                if g == 90:
                    absolute = True
                elif g == 91:
                    absolute = False
                elif g in (0, 1, 2, 3):
                    motion = g
                else:
                    values = None  # G65 calls and other codes are not geometry
                    break
            else:
                values[letter] = float(number)
        if not values or ("X" not in values and "Y" not in values):
            continue
        if absolute:
            nx, ny = values.get("X", x), values.get("Y", y)
        else:
            nx, ny = x + values.get("X", 0.0), y + values.get("Y", 0.0)
        skipped = tag.strip().upper().split(" ")[0] in SKIPPED_TAGS
        if motion == 0 or skipped:
            current = None
        else:
            if current is None:
                current = [(x, y)]
                polys.append(current)
            if motion == 1:
                current.append((nx, ny))
            else:
                cx, cy = x + values.get("I", 0.0), y + values.get("J", 0.0)
                current.extend(_arc_points(x, y, nx, ny, cx, cy, motion == 2))
        x, y = nx, ny
    return [p for p in polys if len(p) > 1]


_DXF_SKIP_TYPES = {"TEXT", "MTEXT", "DIMENSION", "HATCH", "POINT", "ATTDEF", "ATTRIB",
                   "LEADER", "MULTILEADER", "IMAGE", "WIPEOUT", "VIEWPORT", "SOLID", "3DFACE"}


def read_dxf(path):
    """Returns the polylines of a DXF's model space, skipping text, dimensions and marks."""
    import ezdxf
    from ezdxf import path as ezpath
    from ezdxf import recover

    doc, _ = recover.readfile(str(path))
    polys = []

    def visit(entities, depth=0):
        for e in entities:
            kind = e.dxftype()
            layer = (e.dxf.get("layer", "") or "").upper()
            if kind in _DXF_SKIP_TYPES or "ETCH" in layer or "SCRIBE" in layer:
                continue
            if kind == "INSERT":
                if depth < 8:
                    try:
                        visit(e.virtual_entities(), depth + 1)
                    except Exception:
                        pass
                continue
            try:
                p = ezpath.make_path(e)
            except (TypeError, ValueError, ezdxf.DXFError):
                continue
            pts = [(v.x, v.y) for v in p.flattening(0.002, segments=8)]
            if len(pts) > 1:
                polys.append(pts)

    visit(doc.modelspace())
    return polys


def bounds(polys):
    pts = np.array([pt for p in polys for pt in p])
    lo, hi = pts.min(axis=0), pts.max(axis=0)
    return lo, hi


# ---------------------------------------------------------------- images

def render_mask(polys, size=RENDER, width=2):
    """Rasterizes polylines to a boolean ink mask, aspect preserved."""
    lo, hi = bounds(polys)
    span = max(hi[0] - lo[0], hi[1] - lo[1], 1e-9)
    scale = (size - 8) / span
    w = max(8, int((hi[0] - lo[0]) * scale) + 8)
    h = max(8, int((hi[1] - lo[1]) * scale) + 8)
    img = Image.new("L", (w, h), 0)
    draw = ImageDraw.Draw(img)
    for p in polys:
        draw.line([((px - lo[0]) * scale + 4, h - 1 - ((py - lo[1]) * scale + 4)) for px, py in p],
                  fill=255, width=width)
    return np.asarray(img) > 0


def sketch_mask(image, photo=False):
    """Converts a sketch image (scan, photo or MS Paint drawing) to a boolean ink mask."""
    if image.mode in ("RGBA", "LA") or "transparency" in image.info:
        bg = Image.new("RGBA", image.size, (255, 255, 255, 255))
        image = Image.alpha_composite(bg, image.convert("RGBA"))
    gray = np.asarray(image.convert("L"), dtype=np.float32)
    if gray.max() - gray.min() < 10:
        raise ValueError("The sketch image is blank.")
    if photo:
        gray = np.asarray(Image.fromarray(gray.astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.5)),
                          dtype=np.float32)
        block = max(15, (min(gray.shape) // 20) | 1)
        ink = gray < threshold_local(gray, block, offset=10)
    else:
        # Anything clearly darker than the paper is ink. Otsu would split faint
        # strokes from dark ones; the border is paper even when a fill dominates.
        border = np.concatenate([gray[0], gray[-1], gray[:, 0], gray[:, -1]])
        paper = float(np.median(border))
        if paper < 128:  # light lines on a dark background
            gray, paper = 255 - gray, 255 - paper
        darkest = float(np.percentile(gray, 0.1))
        ink = gray < paper - 0.35 * (paper - darkest)
    return remove_small_objects(ink, max(16, ink.size // 200000))


def aspect(mask):
    """Short/long side ratio of the ink's bounding box."""
    ys, xs = np.nonzero(mask)
    if len(xs) == 0:
        raise ValueError("No ink found.")
    h, w = ys.max() - ys.min() + 1, xs.max() - xs.min() + 1
    return min(h, w) / max(h, w)


def normalize(mask):
    """Crops, center-lines, scales and re-strokes an ink mask; returns a CANVAS x CANVAS RGB image."""
    ys, xs = np.nonzero(mask)
    if len(xs) == 0:
        raise ValueError("No ink found.")
    mask = mask[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    mask = np.pad(mask, 2)
    if mask.mean() > FILLED_RATIO:  # a filled silhouette: keep its boundary
        mask = mask & ~binary_erosion(mask)
    mask = skeletonize(mask)
    h, w = mask.shape
    s = (CANVAS - 2 * MARGIN) / max(h, w)
    tw, th = max(1, round(w * s)), max(1, round(h * s))
    small = Image.fromarray(mask.astype(np.uint8) * 255).resize((tw, th), Image.BOX)
    small = small.point(lambda v: 255 if v > 0 else 0).filter(ImageFilter.MaxFilter(STROKE))
    canvas = Image.new("L", (CANVAS, CANVAS), 0)
    canvas.paste(small, ((CANVAS - tw) // 2, (CANVAS - th) // 2))
    return Image.eval(canvas, lambda v: 255 - v).convert("RGB")


def dihedral(image):
    """The 8 rotations/mirrors of a square image."""
    out = []
    for flip in (False, True):
        base = image.transpose(Image.FLIP_LEFT_RIGHT) if flip else image
        for k in range(4):
            out.append(base.rotate(90 * k))
    return out


# ---------------------------------------------------------------- embedding

class Embedder:
    def __init__(self, model_name="facebook/dinov2-small", device=None):
        import torch
        from transformers import AutoModel

        self.torch = torch
        self.device = device or ("cuda" if torch.cuda.is_available() else "cpu")
        self.dtype = torch.float16 if self.device == "cuda" else torch.float32
        self.model = AutoModel.from_pretrained(model_name).to(self.device, self.dtype).eval()
        self.model_name = model_name

    def embed(self, images, batch=64):
        """L2-normalized embeddings (CLS token joined with mean patch token) as float32."""
        torch = self.torch
        out = []
        with torch.inference_mode():
            for i in range(0, len(images), batch):
                x = torch.from_numpy(np.stack([pixels(im) for im in images[i:i + batch]]))
                out.append(features(self.model, x.to(self.device, self.dtype)).cpu().numpy())
        return np.concatenate(out) if out else np.zeros((0, 0), np.float32)


_MEAN = np.array([0.485, 0.456, 0.406], np.float32).reshape(3, 1, 1)
_STD = np.array([0.229, 0.224, 0.225], np.float32).reshape(3, 1, 1)


def pixels(image):
    """A normalized RGB image as a CHW float32 array in the model's input scale."""
    return (np.asarray(image, dtype=np.float32).transpose(2, 0, 1) / 255 - _MEAN) / _STD


def features(model, x):
    """Embeddings of a batch of pixels() tensors; differentiable for training."""
    import torch.nn.functional as F
    import torch

    hidden = model(pixel_values=x).last_hidden_state
    cls = F.normalize(hidden[:, 0].float(), dim=1)
    patch = F.normalize(hidden[:, 1:].float().mean(dim=1), dim=1)
    return F.normalize(torch.cat([cls, patch], dim=1), dim=1)
