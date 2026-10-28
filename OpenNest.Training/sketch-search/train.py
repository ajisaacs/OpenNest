"""Fine-tunes the sketch-search embedding on fake sketches of indexed drawings.

Each step pairs fresh synthetic sketches with their drawings' renderings and trains
the model to pick each sketch's own drawing out of the batch (symmetric InfoNCE).
Held-out drawings (sketch_common.is_holdout) and near-duplicate renderings, which
would be false negatives within a batch, are left out. No hand labelling is needed.

  python train.py --index C:\\SketchIndex --out C:\\SketchIndex\\model
  python build_index.py --index C:\\SketchIndex --server http://barge.lan:8091 --model C:\\SketchIndex\\model
"""
import argparse
import json
import math
import random
import sys
import time
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as F
from PIL import Image

import build_index
import sketch_common as sc
from synthetic import sketchify

DUPLICATE = 0.985


class SketchPairs(torch.utils.data.Dataset):
    def __init__(self, index, items):
        self.index, self.items = str(index), items

    def __len__(self):
        return len(self.items)

    def __getitem__(self, i):
        it = self.items[i]
        clean = Image.open(Path(self.index, "thumbs", it["thumb"])).convert("RGB")
        try:
            polys = build_index.load_polys(self.index, it)
            sketch = sc.normalize(sc.sketch_mask(sketchify(polys, random.Random(), reorient=False)))
        except Exception:  # noqa: BLE001 - a degenerate sketch falls back to the clean image
            sketch = clean
        return torch.from_numpy(sc.pixels(sketch)), torch.from_numpy(sc.pixels(clean))


def distinct(items, embeddings, device):
    """Indices of items whose rendering is not a near-duplicate of an earlier item's."""
    e = torch.from_numpy(embeddings).to(device, torch.float16)
    keep = []
    for start in range(0, len(items), 4096):
        block = e[start:start + 4096] @ e[:start + 4096].T
        rows = torch.arange(block.shape[0], device=device) + start
        earlier = torch.arange(block.shape[1], device=device)[None, :] < rows[:, None]
        best = block.masked_fill(~earlier, -1).max(dim=1).values
        keep += [start + i for i, dup in enumerate((best >= DUPLICATE).cpu().numpy()) if not dup]
    return keep


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--index", required=True, help="index built with the base model")
    ap.add_argument("--out", required=True, help="folder for the fine-tuned model")
    ap.add_argument("--base", help="model to start from (default: the index's model)")
    ap.add_argument("--epochs", type=int, default=5)
    ap.add_argument("--batch", type=int, default=96)
    ap.add_argument("--lr", type=float, default=2e-5)
    ap.add_argument("--temperature", type=float, default=0.05)
    ap.add_argument("--workers", type=int, default=12)
    args = ap.parse_args()

    from transformers import AutoModel

    device = "cuda" if torch.cuda.is_available() else "cpu"
    config = json.loads(Path(args.index, "config.json").read_text())
    items = [json.loads(l) for l in Path(args.index, "items.jsonl").read_text(encoding="utf-8").splitlines()]
    embeddings = np.load(Path(args.index, "embeddings.npy"))
    keep = distinct(items, embeddings, device)
    train = [items[i] for i in keep if not sc.is_holdout(items[i]["key"])]
    print(f"{len(items)} drawings, {len(keep)} distinct, training on {len(train)}")

    loader = torch.utils.data.DataLoader(
        SketchPairs(args.index, train), batch_size=args.batch, shuffle=True, drop_last=True,
        num_workers=args.workers, persistent_workers=args.workers > 0, pin_memory=True)
    model = AutoModel.from_pretrained(args.base or config["model"]).to(device).train()
    opt = torch.optim.AdamW(model.parameters(), lr=args.lr, weight_decay=0.05)
    total = args.epochs * len(loader)
    warmup = max(1, total // 20)
    sched = torch.optim.lr_scheduler.LambdaLR(
        opt, lambda s: min(1, (s + 1) / warmup) * 0.5 * (1 + math.cos(math.pi * min(1, s / total))))

    step, started = 0, time.time()
    for epoch in range(args.epochs):
        losses, hits = [], []
        for sketches, cleans in loader:
            x = torch.cat([sketches, cleans]).to(device, non_blocking=True)
            with torch.autocast(device, dtype=torch.bfloat16):
                f = sc.features(model, x)
            fs, fc = f[:len(sketches)], f[len(sketches):]
            logits = fs @ fc.T / args.temperature
            target = torch.arange(len(fs), device=device)
            loss = (F.cross_entropy(logits, target) + F.cross_entropy(logits.T, target)) / 2
            opt.zero_grad(set_to_none=True)
            loss.backward()
            torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            opt.step()
            sched.step()
            step += 1
            losses.append(loss.item())
            hits.append((logits.argmax(dim=1) == target).float().mean().item())
            if step % 50 == 0:
                print(f"  step {step}/{total}  loss {np.mean(losses[-50:]):.3f}  "
                      f"in-batch top1 {100 * np.mean(hits[-50:]):.1f}%  ({time.time() - started:.0f}s)", flush=True)
        print(f"epoch {epoch + 1}: loss {np.mean(losses):.3f}, in-batch top1 {100 * np.mean(hits):.1f}%", flush=True)
        model.save_pretrained(args.out)
    print(f"saved {args.out}")


if __name__ == "__main__":
    sys.exit(main())
