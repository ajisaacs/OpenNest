# Sketch search (prototype)

Finds drawings that look like a hand sketch, a phone photo of one, or an MS Paint
outline. A DINOv2 image model compares the sketch with a rendering of every
drawing. The model is pretrained, optionally fine-tuned on synthetic sketches of
the library itself (see [Fine-tune](#fine-tune)).

## Setup

```sh
pip install torch --index-url https://download.pytorch.org/whl/cu124
pip install -r requirements.txt
```

A CPU-only torch works, but embedding is much slower than on a GPU.

## Build the index

```sh
python build_index.py --index C:\SketchIndex --server http://barge.lan:8091
python build_index.py --index C:\SketchIndex --dxf X:\Archive
```

`--server` reads the server's drawing library with GET requests only. `--dxf`
walks a folder of DXF files and skips text, dimensions, hatches and
`ETCH`/`SCRIBE` layers. Both options can be repeated and combined. Programs are
cached under `<index>/cache`. Re-running the build only processes drawings whose
revision or file changed.

## Search

```sh
python search.py --index C:\SketchIndex sketch.png --open
python search.py --index C:\SketchIndex photo.jpg --photo --size 10x4.5
```

- **MS Paint:** draw the outline in a dark color and save the file, or crop a
  screenshot to the canvas. Window chrome in the image counts as part of the
  sketch. Either outlines or a filled silhouette work.
- **Photos and scans:** pass `--photo` to use local thresholding, which handles
  uneven lighting. Crop to the sketch and take the photo square-on.
- **`--size WxL`:** keeps only drawings of that overall size (either
  orientation, inches or millimeters, default ±15%). The size is a hard filter,
  so use it only when the sketch's dimensions are reliable.
- **Leave out text and small details:** draw the outline and the holes.
  Rotation and mirroring do not matter.

Results go to `<index>/last-search.html`. The page shows the sketch as the model
sees it, followed by the closest drawings with name, customer, material and
size.

## Measure accuracy

```sh
python evaluate.py --index C:\SketchIndex --synthetic 500
python evaluate.py --index C:\SketchIndex --real C:\Sketches
```

`--synthetic` turns random held-out drawings into fake sketches and reports how
often the source drawing ranks in the top N. Fake sketches have wobbly lines,
±15% proportions, dropped small holes, random rotation, mirroring, line weight
and fill. "Same shape" also counts a drawing that renders identically to the
answer, such as duplicates and unchanged revisions.

`--real` takes a folder of real sketches, each named after its drawing
(`BRKT-12.png`, or `BRKT-12__2.jpg` for extra sketches of the same part). Real
sketches are the result that decides whether the approach works.

## How it works

1. Each drawing's cut geometry is rasterized: G-code from the server, or DXF
   entities. Scribe, etch and break motions are skipped.
2. Drawings and sketches share one `normalize()` step. It reduces the ink to
   1px center lines (a filled silhouette becomes its boundary), crops and scales
   the result into a 224×224 canvas, and redraws it at a fixed stroke width.
3. DINOv2 embeds the image: the CLS token joined with the mean patch token.
4. The query is embedded in all 8 rotations and mirrors, and each drawing scores
   its best match. A small penalty applies when the drawing's proportions
   differ from the sketch's (`ASPECT_WEIGHT` in `search.py`).

## Fine-tune

The pretrained model alone is weak on a 40k-drawing library. Training it on
synthetic sketches of the library itself needs no labeling:

```sh
python train.py --index C:\SketchIndex --out C:\SketchIndex\model
python build_index.py --index C:\SketchIndex --server http://barge.lan:8091 --model C:\SketchIndex\model
python evaluate.py --index C:\SketchIndex --synthetic 400
```

Each epoch pairs every training drawing's rendering with a freshly generated
fake sketch (`synthetic.py`, kept in the drawing's orientation because search
tries all 8). The model learns to pick each sketch's own drawing out of the
batch. Two groups of drawings are left out of training:

- Near-duplicate renderings, which would count as wrong answers within a batch.
- About 10% of drawings chosen by key (`is_holdout`). `evaluate.py` samples only
  these, so its scores are not inflated by training.

Rebuilding the index with a new `--model` reuses the existing renders and only
re-embeds them.
