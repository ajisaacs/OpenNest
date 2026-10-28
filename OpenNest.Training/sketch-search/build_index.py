"""Builds or refreshes a sketch-search index from an OpenNest server and/or DXF folders.

  python build_index.py --index C:\\SketchIndex --server http://barge.lan:8091
  python build_index.py --index C:\\SketchIndex --dxf X:\\Archive

Re-running only downloads, renders and embeds drawings whose revision changed.
"""
import argparse
import concurrent.futures as cf
import io
import json
import os
import sys
import time
import urllib.request
import zipfile
from pathlib import Path

import numpy as np
from PIL import Image

import sketch_common as sc


def list_server(server, limit=None):
    items, offset = [], 0
    while True:
        page = json.load(urllib.request.urlopen(f"{server}/api/drawings/query?limit=500&offset={offset}", timeout=60))
        for r in page["items"]:
            items.append({
                "key": f"server:{r['id']}", "source": "server", "id": r["id"], "revision": r["revision"],
                "sha": r["packageSha256"], "name": r["name"], "customer": r.get("customer", ""),
                "material": " ".join(x for x in (r.get("materialName"), r.get("materialGrade")) if x),
                "thickness": r.get("thickness"), "units": r.get("units", ""),
            })
        offset += len(page["items"])
        if not page["items"] or offset >= page["total"] or (limit and offset >= limit):
            return items[:limit] if limit else items


def list_dxf(folder, limit=None):
    items = []
    for root, _, files in os.walk(folder):
        for f in files:
            if f.lower().endswith(".dxf"):
                p = Path(root, f)
                st = p.stat()
                items.append({"key": f"dxf:{p}", "source": "dxf", "path": str(p), "name": p.stem,
                              "sha": f"{st.st_size}-{int(st.st_mtime)}", "units": ""})
                if limit and len(items) >= limit:
                    return items
    return items


def program_path(index, item):
    return Path(index, "cache", f"{item['id']}-r{item['revision']}.gcode")


def download(server, index, item):
    path = program_path(index, item)
    if path.exists():
        return None
    for attempt in range(3):
        try:
            data = urllib.request.urlopen(f"{server}/api/drawings/{item['id']}/file", timeout=60).read()
            z = zipfile.ZipFile(io.BytesIO(data))
            programs = sorted(n for n in z.namelist() if n.startswith("programs/program-") and not n.endswith("-subs"))
            path.write_bytes(z.read(programs[0]) if programs else b"")
            return None
        except Exception as e:  # noqa: BLE001 - reported per drawing
            err = e
            time.sleep(1 + attempt)
    return f"{item['name']}: {err}"


def load_polys(index, item):
    if item["source"] == "server":
        return sc.parse_program(program_path(index, item).read_text(errors="replace"))
    return sc.read_dxf(item["path"])


def render_one(args):
    index, item = args
    try:
        polys = load_polys(index, item)
        if not polys:
            return item["key"], None, "no cut geometry"
        lo, hi = sc.bounds(polys)
        sc.normalize(sc.render_mask(polys)).convert("L").save(Path(index, "thumbs", thumb_name(item)))
        return item["key"], (float(hi[0] - lo[0]), float(hi[1] - lo[1])), None
    except Exception as e:  # noqa: BLE001 - reported per drawing
        return item["key"], None, str(e)


def thumb_name(item):
    if item["source"] == "server":
        return f"{item['id']}.png"
    import hashlib
    return "dxf-" + hashlib.sha1(item["path"].encode()).hexdigest()[:16] + ".png"


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--index", required=True, help="index folder (created if missing)")
    ap.add_argument("--server", action="append", default=[], help="OpenNest server URL")
    ap.add_argument("--dxf", action="append", default=[], help="folder of DXF files (searched recursively)")
    ap.add_argument("--model", default="facebook/dinov2-small", help="Hugging Face model or fine-tuned model folder")
    ap.add_argument("--limit", type=int, help="index at most this many drawings per source (testing)")
    ap.add_argument("--workers", type=int, default=max(1, (os.cpu_count() or 4) - 2))
    args = ap.parse_args()
    if not args.server and not args.dxf:
        ap.error("give at least one --server or --dxf source")

    index = Path(args.index)
    for sub in ("cache", "thumbs"):
        Path(index, sub).mkdir(parents=True, exist_ok=True)

    items = []
    for server in args.server:
        server = server.rstrip("/")
        found = list_server(server, args.limit)
        print(f"{server}: {len(found)} drawings")
        with cf.ThreadPoolExecutor(12) as pool:
            errors = [e for e in pool.map(lambda it: download(server, index, it), found) if e]
        for e in errors[:10]:
            print("  download failed:", e)
        items += found
    for folder in args.dxf:
        found = list_dxf(folder, args.limit)
        print(f"{folder}: {len(found)} DXF files")
        items += found

    config = {"model": args.model}
    old_items, old_emb = {}, None
    if Path(index, "items.jsonl").exists() and Path(index, "config.json").exists():
        old_list = [json.loads(l) for l in Path(index, "items.jsonl").read_text(encoding="utf-8").splitlines()]
        old_items = {it["key"]: (it, i) for i, it in enumerate(old_list)}
        if json.loads(Path(index, "config.json").read_text()) == config:
            old_emb = np.load(Path(index, "embeddings.npy"))

    # Renders survive a model change; embeddings only survive with the same model.
    embedded, unembedded, todo = [], [], []
    for it in items:
        prev = old_items.get(it["key"])
        if prev and prev[0]["sha"] == it["sha"] and Path(index, "thumbs", thumb_name(it)).exists():
            it.update(width=prev[0]["width"], height=prev[0]["height"])
            if old_emb is not None:
                embedded.append((it, old_emb[prev[1]]))
            else:
                unembedded.append(it)
        else:
            todo.append(it)
    print(f"reusing {len(embedded) + len(unembedded)} renders, rendering {len(todo)}")

    by_key = {it["key"]: it for it in todo}
    failures = []
    started = time.time()
    with cf.ProcessPoolExecutor(args.workers) as pool:
        for n, (key, size, err) in enumerate(pool.map(render_one, [(str(index), it) for it in todo], chunksize=16), 1):
            if err:
                failures.append((key, err))
            else:
                by_key[key].update(width=size[0], height=size[1])
                unembedded.append(by_key[key])
            if n % 2000 == 0:
                print(f"  rendered {n}/{len(todo)} ({time.time() - started:.0f}s)")
    print(f"rendered {len(todo) - len(failures)} in {time.time() - started:.0f}s, {len(failures)} skipped")
    for key, err in failures[:15]:
        print("  skipped", key, "-", err)

    rows = [e for _, e in embedded]
    if unembedded:
        embedder = sc.Embedder(args.model)
        started = time.time()
        load = lambda it: Image.open(Path(index, "thumbs", thumb_name(it))).convert("RGB")
        with cf.ThreadPoolExecutor(8) as pool:
            for i in range(0, len(unembedded), 2048):
                rows += list(embedder.embed(list(pool.map(load, unembedded[i:i + 2048]))))
        print(f"embedded {len(unembedded)} in {time.time() - started:.0f}s")

    all_items = [it for it, _ in embedded] + unembedded
    for it in all_items:
        it["thumb"] = thumb_name(it)
    np.save(Path(index, "embeddings.npy"), np.stack(rows).astype(np.float16))
    Path(index, "items.jsonl").write_text("\n".join(json.dumps(it) for it in all_items), encoding="utf-8")
    Path(index, "config.json").write_text(json.dumps(config))
    print(f"index: {len(all_items)} drawings in {index}")


if __name__ == "__main__":
    sys.exit(main())
