"""Measures how often search finds the right drawing.

Synthetic: turns random held-out drawings (never trained on) into fake hand
sketches and records where the source drawing ranks among the whole index.

  python evaluate.py --index C:\\SketchIndex --synthetic 300

Real: a folder of sketch images, each named after the drawing it should find
(the drawing name, or name + anything after "__", e.g. "BRKT-12__photo.jpg").

  python evaluate.py --index C:\\SketchIndex --real C:\\Sketches [--photo]

A "same shape" hit also counts a drawing whose rendering is effectively identical
to the answer (duplicates and unchanged revisions are common in a part library).
"""
import argparse
import json
import random
import sys
from pathlib import Path

import numpy as np
from PIL import Image

import build_index
from synthetic import sketchify
import sketch_common as sc
from search import ASPECT_WEIGHT, SketchIndex

SAME_SHAPE = 0.985
CUTOFFS = (1, 5, 10, 20, 50)


def rank_of(index, results_keys, truth_idx):
    """(strict rank, same-shape rank), 1-based; None when beyond the list."""
    truth_emb = index.embeddings[truth_idx]
    strict = same = None
    for r, i in enumerate(results_keys, 1):
        if strict is None and i == truth_idx:
            strict = r
        if same is None and (i == truth_idx or float(index.embeddings[i] @ truth_emb) >= SAME_SHAPE):
            same = r
    return strict, same


def full_ranking(index, mask, k, aspect_weight):
    scores = index.scores(sc.normalize(mask), sc.aspect(mask), aspect_weight)
    return list(np.argsort(-scores)[:k])


def report(name, ranks, k):
    print(f"\n{name}: {len(ranks)} queries")
    for label, col in (("exact drawing", 0), ("same shape", 1)):
        found = [r[col] for r in ranks]
        cells = "  ".join(f"top{c}: {100 * sum(1 for f in found if f and f <= c) / len(found):5.1f}%" for c in CUTOFFS)
        print(f"  {label:13} {cells}  (median rank {np.median([f or k + 1 for f in found]):.0f})")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--index", required=True)
    ap.add_argument("--synthetic", type=int, help="number of synthetic sketches")
    ap.add_argument("--real", help="folder of real sketches named after their drawing")
    ap.add_argument("--photo", action="store_true")
    ap.add_argument("--model", help="evaluate this model instead of the index's (re-embeds renders in memory)")
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--save", help="folder to save synthetic sketches and their normalized form")
    ap.add_argument("--aspect-weight", type=float, nargs="+", default=[ASPECT_WEIGHT],
                    help="one or more proportion-penalty weights to compare")
    args = ap.parse_args()
    k = 200
    index = SketchIndex(args.index, args.model)

    if args.synthetic:
        rng = random.Random(args.seed)
        holdout = [i for i, it in enumerate(index.items) if sc.is_holdout(it["key"])]
        picks = rng.sample(holdout, min(args.synthetic, len(holdout)))
        ranks = {w: [] for w in args.aspect_weight}
        if args.save:
            Path(args.save).mkdir(parents=True, exist_ok=True)
        for n, i in enumerate(picks):
            it = index.items[i]
            sketch = sketchify(build_index.load_polys(args.index, it), rng)
            mask = sc.sketch_mask(sketch)
            for w in args.aspect_weight:
                ranks[w].append(rank_of(index, full_ranking(index, mask, k, w), i))
            if args.save and n < 40:
                sketch.save(Path(args.save, f"{n:03}-sketch.png"))
                sc.normalize(mask).save(Path(args.save, f"{n:03}-query.png"))
                Path(args.save, f"{n:03}-answer.txt").write_text(
                    json.dumps({"name": it["name"], "thumb": it["thumb"], "rank": ranks[args.aspect_weight[0]][-1]}))
        for w in args.aspect_weight:
            report(f"synthetic sketches vs {len(index.items)} drawings, aspect weight {w:g}", ranks[w], k)

    if args.real:
        by_name = {}
        for i, it in enumerate(index.items):
            by_name.setdefault(it["name"].lower(), []).append(i)
        ranks = []
        for f in sorted(Path(args.real).iterdir()):
            if f.suffix.lower() not in (".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"):
                continue
            name = f.stem.split("__")[0].lower()
            if name not in by_name:
                print(f"  {f.name}: no indexed drawing named '{name}'")
                continue
            ranking = full_ranking(index, sc.sketch_mask(Image.open(f), photo=args.photo), k, args.aspect_weight[0])
            best = min((rank_of(index, ranking, i) for i in by_name[name]),
                       key=lambda r: (r[0] or k + 1, r[1] or k + 1))
            print(f"  {f.name}: rank {best[0] or f'>{k}'} (same shape {best[1] or f'>{k}'})")
            ranks.append(best)
        if ranks:
            report("real sketches", ranks, k)


if __name__ == "__main__":
    sys.exit(main())
