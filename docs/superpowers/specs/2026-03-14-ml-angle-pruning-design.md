# ML Angle Pruning Design

**Date:** 2026-03-14
**Status:** Draft

## Problem

The nesting engine's biggest performance bottleneck is `FillLinear.FillRecursive`, which consumes ~66% of total CPU time. The linear phase builds a list of rotation angles to try — normally just 2 (`bestRotation` and `bestRotation + 90`), but expanding to a full 36-angle sweep (0-175 in 5-degree increments) when the work area's short side is smaller than the part's longest side. This narrow-work-area condition triggers frequently during remainder-strip fills and for large/elongated parts. Each angle x 2 directions requires expensive ray/edge distance calculations for every tile placement.

## Goal

Train an ML model that predicts which rotation angles are competitive for a given part geometry and sheet size. At runtime, replace the full angle sweep with only the predicted angles, reducing linear phase compute time in the narrow-work-area case. The model only applies when the engine would otherwise sweep all 36 angles — for the normal 2-angle case, no change is needed.

## Design

### Training Data Collection

#### Forced Full Sweep for Training

In production, `FindBestFill` only sweeps all 36 angles when `workAreaShortSide < partLongestSide`. For training, the sweep must be forced for every part x sheet combination regardless of this condition — otherwise the model has no data to learn from for the majority of runs that only evaluate 2 angles.

`NestEngine` gains a `ForceFullAngleSweep` property (default `false`). When `true`, `FindBestFill` always builds the full 0-175 angle list. The training runner sets this to `true`; production code leaves it `false`.

#### Per-Angle Results from NestEngine

Instrument `NestEngine.FindBestFill` to collect per-angle results from the linear phase. Each call to `FillLinear.Fill(drawing, angle, direction)` produces a result that is currently only compared against the running best. With this change, each result is also accumulated into a collection on the engine instance.

New types in `NestProgress.cs`:

```csharp
public class AngleResult
{
    public double AngleDeg { get; set; }
    public NestDirection Direction { get; set; }
    public int PartCount { get; set; }
}
```

New properties on `NestEngine`:

```csharp
public bool ForceFullAngleSweep { get; set; }
public List<AngleResult> AngleResults { get; } = new();
```

`AngleResults` is cleared at the start of `Fill` (alongside `PhaseResults.Clear()`). Populated inside the `Parallel.ForEach` over angles in `FindBestFill` — uses a `ConcurrentBag<AngleResult>` during the parallel loop, then transferred to `AngleResults` via `AddRange` after the loop completes (same pattern as the existing `linearBag`).

#### Progress Window Enhancement

`NestProgress` gains a `Description` field — a freeform status string that the progress window displays directly:

```csharp
public class NestProgress
{
    // ... existing fields ...
    public string Description { get; set; }
}
```

Progress is reported per-angle during the linear phase (e.g. `"Linear: 35 V - 48 parts"`) and per-candidate during the pairs phase (e.g. `"Pairs: candidate 12/50"`). This gives real-time visibility into what the engine is doing, beyond the current phase-level updates.

#### BruteForceRunner Changes

`BruteForceRunner.Run` reads `engine.AngleResults` after `Fill` completes and passes them through `BruteForceResult`:

```csharp
public class BruteForceResult
{
    // ... existing fields ...
    public List<AngleResult> AngleResults { get; set; }
}
```

The training runner sets `engine.ForceFullAngleSweep = true` before calling `Fill`.

#### Database Schema

New `AngleResults` table:

| Column    | Type    | Description                          |
|-----------|---------|--------------------------------------|
| Id        | long    | PK, auto-increment                   |
| RunId     | long    | FK to Runs table                     |
| AngleDeg  | double  | Rotation angle in degrees (0-175)    |
| Direction | string  | "Horizontal" or "Vertical"           |
| PartCount | int     | Parts placed at this angle/direction |

Each run produces up to ~72 rows (36 angles x 2 directions, minus angles where zero parts fit). With forced full sweep during training: 41k parts x 11 sheet sizes x ~72 angle results = ~32 million rows. SQLite handles this for batch writes; SQL Express on barge.lan is available as a fallback if needed.

New EF Core entity `TrainingAngleResult` in `OpenNest.Training/Data/`. `TrainingDatabase.AddRun` is extended to accept and batch-insert angle results alongside the run.

Migration: `MigrateSchema` creates the `AngleResults` table if it doesn't exist. Existing databases without the table continue to work — the table is created on first use.

### Model Architecture

**Type:** XGBoost multi-label classifier exported to ONNX.

**Input features (11 scalars):**
- Part geometry (7): Area, Convexity, AspectRatio, BBFill, Circularity, PerimeterToAreaRatio, VertexCount
- Sheet dimensions (2): Width, Height
- Derived (2): SheetAspectRatio (Width/Height), PartToSheetAreaRatio (PartArea / SheetArea)

The 32x32 bitmask is excluded from the initial model. The 7 scalar geometry features capture sufficient shape information for angle prediction. Bitmask can be added later if accuracy needs improvement.

**Output:** 36 probabilities, one per 5-degree angle bin (0, 5, 10, ..., 175). Each probability represents "this angle is competitive for this part/sheet combination."

**Label generation:** For each part x sheet run, an angle is labeled positive (1) if its best PartCount (max of H and V directions) is >= 95% of the overall best angle's PartCount for that run. This creates a multi-label target where typically 2-8 angles are labeled positive.

**Direction handling:** The model predicts angles only. Both H and V directions are always tried for each selected angle — direction computation is cheap relative to the angle setup.

### Training Pipeline

Python notebook at `OpenNest.Training/notebooks/train_angle_model.ipynb`:

1. **Extract** — Read SQLite database, join Parts + Runs + AngleResults into a flat dataframe.
2. **Filter** — Remove title block outliers using feature thresholds (e.g. BBFill < 0.01, abnormally large bounding boxes relative to actual geometry area). Log filtered parts for manual review.
3. **Label** — For each run, compute the best angle's PartCount. Mark angles within 95% as positive. Build a 36-column binary label matrix.
4. **Feature engineering** — Compute derived features (SheetAspectRatio, PartToSheetAreaRatio). Normalize if needed.
5. **Train** — XGBoost multi-label classifier. Use `sklearn.multioutput.MultiOutputClassifier` wrapping `xgboost.XGBClassifier`. Train/test split stratified by part (all sheet sizes for a part stay in the same split).
6. **Evaluate** — Primary metric: per-angle recall > 95% (must almost never skip the winning angle). Secondary: precision > 60% (acceptable to try a few extra angles). Report average angles predicted per part.
7. **Export** — Convert to ONNX via `skl2onnx` or `onnxmltools`. Save to `OpenNest.Engine/Models/angle_predictor.onnx`.

Python dependencies: `pandas`, `scikit-learn`, `xgboost`, `onnxmltools` (or `skl2onnx`), `matplotlib` (for evaluation plots).

### C# Inference Integration

New file `OpenNest.Engine/ML/AnglePredictor.cs`:

```csharp
public static class AnglePredictor
{
    public static List<double> PredictAngles(
        PartFeatures features, double sheetWidth, double sheetHeight);
}
```

- Loads `angle_predictor.onnx` from the `Models/` directory adjacent to the Engine DLL on first call. Caches the ONNX session for reuse.
- Runs inference with the 11 input features.
- Applies threshold (default 0.3) to the 36 output probabilities.
- Returns angles above threshold, converted to radians.
- Always includes 0 and 90 degrees as safety fallback.
- Minimum 3 angles returned (if fewer pass threshold, take top 3 by probability).
- If the model file is missing or inference fails, returns `null` — caller falls back to trying all angles (current behavior unchanged).

**NuGet dependency:** `Microsoft.ML.OnnxRuntime` added to `OpenNest.Engine.csproj`.

### NestEngine Integration

In `FindBestFill` (the progress/token overload), the angle list construction changes:

```
Current:
  angles = [bestRotation, bestRotation + 90]
  + sweep 0-175 if narrow work area

With model (only when narrow work area condition is met):
  predicted = AnglePredictor.PredictAngles(features, sheetW, sheetH)
  if predicted != null:
    angles = predicted
    + bestRotation and bestRotation + 90 (if not already included)
  else:
    angles = current behavior (full sweep)

ForceFullAngleSweep = true (training only):
  angles = full 0-175 sweep regardless of work area condition
```

`FeatureExtractor.Extract(drawing)` is called once per drawing before the fill loop. This is cheap (~0ms) and already exists.

**Note:** The Pairs phase (`FillWithPairs`) uses hull-edge angles from each pair candidate's geometry, not the linear angle list. The ML model does not affect the Pairs phase angle selection. Pairs phase optimization (e.g. pruning pair candidates) is a separate future concern.

### Fallback and Safety

- **No model file:** Full angle sweep (current behavior). Zero regression risk.
- **Model loads but prediction fails:** Full angle sweep. Logged to Debug output.
- **Model predicts too few angles:** Minimum 3 angles enforced. 0, 90, bestRotation, and bestRotation + 90 always included.
- **Normal 2-angle case (no narrow work area):** Model is not consulted — the engine only tries bestRotation and bestRotation + 90 as it does today.
- **Model misses the optimal angle:** Recall target of 95% means ~5% of runs may not find the absolute best. The result will still be good (within 95% of optimal by definition of the training labels). Users can disable the model via a setting if needed.

## Files Changed

### OpenNest.Engine
- `NestProgress.cs` — Add `AngleResult` class, add `Description` to `NestProgress`
- `NestEngine.cs` — Add `ForceFullAngleSweep` and `AngleResults` properties, clear `AngleResults` alongside `PhaseResults`, populate per-angle results in `FindBestFill` via `ConcurrentBag` + `AddRange`, report per-angle progress with descriptions, use `AnglePredictor` for angle selection when narrow work area
- `ML/BruteForceRunner.cs` — Pass through `AngleResults` from engine
- `ML/AnglePredictor.cs` — New: ONNX model loading and inference
- `ML/FeatureExtractor.cs` — No changes (already exists)
- `Models/angle_predictor.onnx` — New: trained model file (added after training)
- `OpenNest.Engine.csproj` — Add `Microsoft.ML.OnnxRuntime` NuGet package

### OpenNest.Training
- `Data/TrainingAngleResult.cs` — New: EF Core entity for AngleResults table
- `Data/TrainingDbContext.cs` — Add `DbSet<TrainingAngleResult>`
- `Data/TrainingRun.cs` — No changes
- `TrainingDatabase.cs` — Add angle result storage, extend `MigrateSchema`
- `Program.cs` — Set `ForceFullAngleSweep = true` on engine, collect and store per-angle results from `BruteForceRunner`

### OpenNest.Training/notebooks (new directory)
- `train_angle_model.ipynb` — Training notebook
- `requirements.txt` — Python dependencies

### OpenNest (WinForms)
- Progress window UI — Display `NestProgress.Description` string (minimal change)

## Data Volume Estimates

- 41k parts x 11 sheet sizes = ~450k runs
- With forced full sweep: ~72 angle results per run = ~32 million angle result rows
- SQLite can handle this for batch writes. SQL Express on barge.lan available as fallback.
- Trained model file: ~1-5 MB ONNX

## Success Criteria

- Per-angle recall > 95% (almost never skips the winning angle)
- Average angles predicted: 4-8 per part (down from 36)
- Linear phase speedup in narrow-work-area case: 70-80% reduction
- Zero regression when model is absent — current behavior preserved exactly
- Progress window shows live angle/candidate details during nesting
