"""Finds the drawings that look most like a sketch.

  python search.py --index C:\\SketchIndex sketch.png
  python search.py --index C:\\SketchIndex sketch.jpg --photo --size 10x4.5 --open

Writes an HTML page of the best matches beside the index (or --out) and prints them.
"""
import argparse
import html
import json
import sys
import webbrowser
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import numpy as np
from PIL import Image

import sketch_common as sc

MM_PER_INCH = 25.4
ASPECT_WEIGHT = 0.1


class SketchIndex:
    def __init__(self, folder, model=None):
        """model overrides the index's model: its renders are re-embedded in memory."""
        self.folder = Path(folder)
        config = json.loads(Path(folder, "config.json").read_text())
        self.items = [json.loads(l) for l in Path(folder, "items.jsonl").read_text(encoding="utf-8").splitlines()]
        self.embedder = sc.Embedder(model or config["model"])
        if model and model != config["model"]:
            load = lambda it: Image.open(Path(folder, "thumbs", it["thumb"])).convert("RGB")
            with ThreadPoolExecutor(8) as pool:
                self.embeddings = np.concatenate([self.embedder.embed(list(pool.map(load, self.items[i:i + 2048])))
                                                  for i in range(0, len(self.items), 2048)])
        else:
            self.embeddings = np.load(Path(folder, "embeddings.npy")).astype(np.float32)
        self._aspects = None

    def size_mask(self, size, tolerance):
        """Drawings whose extents match size (either orientation, inches or millimeters)."""
        want = np.sort(np.array(size, dtype=np.float64))
        have = np.sort(np.array([[it["width"], it["height"]] for it in self.items]), axis=1)
        ok = np.zeros(len(self.items), bool)
        for factor in (1.0, MM_PER_INCH, 1 / MM_PER_INCH):
            ok |= np.all(np.abs(have * factor - want) <= tolerance * want, axis=1)
        return ok

    def scores(self, query, aspect=None, aspect_weight=ASPECT_WEIGHT):
        """Similarity of every drawing to a normalized query image; aspect is the
        sketch's short/long side ratio, which penalizes drawings of other proportions."""
        variants = self.embedder.embed(sc.dihedral(query))
        scores = (self.embeddings @ variants.T).max(axis=1)
        if aspect and aspect_weight:
            if self._aspects is None:
                wh = np.array([[it["width"], it["height"]] for it in self.items])
                self._aspects = np.maximum(wh.min(axis=1), 1e-9) / np.maximum(wh.max(axis=1), 1e-9)
            scores = scores - aspect_weight * np.abs(np.log(self._aspects / aspect))
        return scores

    def search(self, query, k=30, size=None, tolerance=0.15, aspect=None, aspect_weight=ASPECT_WEIGHT):
        """query is a normalized image; returns [(score, item)] best first."""
        scores = self.scores(query, aspect, aspect_weight)
        if size:
            scores = np.where(self.size_mask(size, tolerance), scores, -np.inf)
        order = np.argsort(-scores)[:k]
        return [(float(scores[i]), self.items[i]) for i in order if np.isfinite(scores[i])]


def parse_size(text):
    parts = text.lower().replace(" ", "").split("x")
    if len(parts) != 2:
        raise argparse.ArgumentTypeError("size looks like 10x4.5")
    return float(parts[0]), float(parts[1])


def write_html(path, index_folder, query_image, results, title):
    path = Path(path)
    query_file = path.with_suffix(".query.png")
    query_image.save(query_file)
    thumbs = Path(index_folder, "thumbs").resolve().as_uri()
    cards = []
    for rank, (score, it) in enumerate(results, 1):
        details = " · ".join(str(x) for x in (
            it.get("customer"), it.get("material"),
            f"{it['thickness']:g} thk" if it.get("thickness") else None,
            f"{it['width']:.3g} × {it['height']:.3g} {it.get('units', '')}".strip()) if x)
        where = it.get("path") or f"server drawing {it.get('id')}"
        cards.append(f"""<figure><img src="{thumbs}/{it['thumb']}"><figcaption>
<b>{rank}. {html.escape(it['name'])}</b><span>{html.escape(details)}</span>
<small title="{html.escape(where)}">score {score:.3f} · {html.escape(where)}</small></figcaption></figure>""")
    path.write_text(f"""<!doctype html><meta charset="utf-8"><title>{html.escape(title)}</title>
<style>
:root {{ color-scheme: light dark; --bg:#fafafa; --card:#fff; --fg:#222; --muted:#666; --line:#ddd; }}
@media (prefers-color-scheme: dark) {{ :root {{ --bg:#1b1b1b; --card:#262626; --fg:#eee; --muted:#aaa; --line:#3a3a3a; }} }}
body {{ font: 14px system-ui, sans-serif; background: var(--bg); color: var(--fg); margin: 16px; }}
header {{ display: flex; gap: 16px; align-items: center; margin-bottom: 16px; }}
header img {{ width: 160px; border: 1px solid var(--line); background: #fff; }}
main {{ display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 12px; }}
figure {{ margin: 0; background: var(--card); border: 1px solid var(--line); border-radius: 6px; padding: 8px; }}
figure img {{ width: 100%; background: #fff; border-radius: 4px; }}
figcaption {{ display: flex; flex-direction: column; gap: 2px; overflow-wrap: anywhere; }}
span, small {{ color: var(--muted); }}
</style>
<header><img src="{query_file.name}"><div><h2>{html.escape(title)}</h2>
<p>The sketch as the model sees it (left), then the {len(results)} closest drawings.</p></div></header>
<main>{''.join(cards)}</main>""", encoding="utf-8")
    return path


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("sketch", help="image of the sketch (PNG/JPG; crop off any window chrome)")
    ap.add_argument("--index", required=True)
    ap.add_argument("-k", type=int, default=30, help="number of matches")
    ap.add_argument("--photo", action="store_true", help="camera photo or scan: adaptive thresholding")
    ap.add_argument("--size", type=parse_size, help="overall size, e.g. 10x4.5 (either orientation, in or mm)")
    ap.add_argument("--tolerance", type=float, default=0.15, help="relative size tolerance (default 0.15)")
    ap.add_argument("--out", help="HTML results file (default: <index>/last-search.html)")
    ap.add_argument("--open", action="store_true", help="open the results in a browser")
    args = ap.parse_args()

    mask = sc.sketch_mask(Image.open(args.sketch), photo=args.photo)
    index = SketchIndex(args.index)
    query = sc.normalize(mask)
    results = index.search(query, args.k, args.size, args.tolerance, sc.aspect(mask))
    for rank, (score, it) in enumerate(results, 1):
        print(f"{rank:3}. {score:.3f}  {it['name']}  [{it.get('customer', '')}]  "
              f"{it['width']:.3g}x{it['height']:.3g}")
    out = write_html(args.out or Path(args.index, "last-search.html"), args.index, query, results,
                     f"Matches for {Path(args.sketch).name}")
    print(f"results: {out}")
    if args.open:
        webbrowser.open(out.resolve().as_uri())


if __name__ == "__main__":
    sys.exit(main())
