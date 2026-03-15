# Strip Nester Design Spec

## Problem

The current multi-drawing nesting strategies (AutoNester with NFP/simulated annealing, sequential FillExact) produce scattered, unstructured layouts. For jobs with multiple part types, a structured strip-based approach can pack more densely by dedicating a tight strip to the highest-area drawing and filling the remnant with the rest.

## Strategy Overview

1. Pick the drawing that consumes the most plate area (bounding box area x quantity) as the "strip item." All others are "remainder items."
2. Try two orientations — bottom strip and left strip.
3. For each orientation, find the tightest strip that fits the strip item's full quantity.
4. Fill the remnant area with remainder items using existing fill strategies.
5. Compare both orientations. The denser overall result wins.

## Algorithm Detail

### Step 1: Select Strip Item

Sort `NestItem`s by `Drawing.Program.BoundingBox().Area() * quantity` descending — bounding box area, not `Drawing.Area`, because the bounding box represents the actual plate space consumed by each part. The first item becomes the strip item. If quantity is 0 (unlimited), estimate max capacity from `workArea.Area() / bboxArea` as a stand-in for sorting.

### Step 2: Estimate Initial Strip Height

For the strip item, calculate at both 0 deg and 90 deg rotation. These two angles are sufficient since this is only an estimate for the shrink loop starting point — the actual fill in Step 3 uses `NestEngine.Fill` which tries many rotation angles internally.

- Parts per row: `floor(stripLength / bboxWidth)`
- Rows needed: `ceil(quantity / partsPerRow)`
- Strip height: `rows * bboxHeight`

Pick the rotation with the shorter strip height. The strip length is the work area dimension along the strip's long axis (work area width for bottom strip, work area length for left strip).

### Step 3: Initial Fill

Create a `Box` for the strip area:

- **Bottom strip**: `(workArea.X, workArea.Y, workArea.Width, estimatedStripHeight)`
- **Left strip**: `(workArea.X, workArea.Y, estimatedStripWidth, workArea.Length)`

Fill using `NestEngine.Fill(stripItem, stripBox)`. Measure the actual strip dimension from placed parts: for a bottom strip, `actualStripHeight = placedParts.GetBoundingBox().Top - workArea.Y`; for a left strip, `actualStripWidth = placedParts.GetBoundingBox().Right - workArea.X`. This may be shorter than the estimate since FillLinear packs more efficiently than pure bounding-box grid.

### Step 4: Shrink Loop

Starting from the actual placed dimension (not the estimate), capped at 20 iterations:

1. Reduce strip height by `plate.PartSpacing` (typically 0.25").
2. Create new strip box with reduced dimension.
3. Fill with `NestEngine.Fill(stripItem, newStripBox)`.
4. If part count equals the initial fill count, record this as the new best and repeat.
5. If part count drops, stop. Use the previous iteration's result (tightest strip that still fits).

For unlimited quantity (qty = 0), the initial fill count becomes the target.

### Step 5: Remnant Fill

Calculate the remnant box from the tightest strip's actual placed dimension, adding `plate.PartSpacing` between the strip and remnant to prevent spacing violations:

- **Bottom strip remnant**: `(workArea.X, workArea.Y + actualStripHeight + partSpacing, workArea.Width, workArea.Length - actualStripHeight - partSpacing)`
- **Left strip remnant**: `(workArea.X + actualStripWidth + partSpacing, workArea.Y, workArea.Width - actualStripWidth - partSpacing, workArea.Length)`

Fill remainder items in descending order by `bboxArea * quantity` (largest first, same as strip selection). If the strip item was only partially placed (fewer than target quantity), add the leftover quantity as a remainder item so it participates in the remnant fill.

For each remainder item, fill using `NestEngine.Fill(remainderItem, remnantBox)`.

### Step 6: Compare Orientations

Score each orientation using `FillScore.Compute` over all placed parts (strip + remnant) against `plate.WorkArea()`. The orientation with the better `FillScore` wins. Apply the winning parts to the plate.

## Classes

### `StripNester` (new, `OpenNest.Engine`)

```csharp
public class StripNester
{
    public StripNester(Plate plate) { }

    public List<Part> Nest(List<NestItem> items,
                           IProgress<NestProgress> progress,
                           CancellationToken token);
}
```

**Constructor**: Takes the target plate (for work area, part spacing, quadrant).

**`Nest` method**: Runs the full strategy. Returns the combined list of placed parts. The caller adds them to `plate.Parts`. Same instance-based pattern as `NestEngine`.

### `StripNestResult` (new, internal, `OpenNest.Engine`)

```csharp
internal class StripNestResult
{
    public List<Part> Parts { get; set; } = new();
    public Box StripBox { get; set; }
    public Box RemnantBox { get; set; }
    public FillScore Score { get; set; }
    public StripDirection Direction { get; set; }
}
```

Holds intermediate results for comparing bottom vs left orientations.

### `StripDirection` (new enum, `OpenNest.Engine`)

```csharp
public enum StripDirection { Bottom, Left }
```

## Integration

### MCP (`NestingTools`)

`StripNester` becomes an additional strategy in the autonest flow. When multiple items are provided, both `StripNester` and the current approach run, and the better result wins.

### UI (`AutoNestForm`)

Can be offered as a strategy option alongside existing NFP-based auto-nesting.

### No changes to `NestEngine`

`StripNester` is a consumer of `NestEngine.Fill`, not a modification of it.

## Edge Cases

- **Single item**: Strategy reduces to strip optimization only (shrink loop with no remnant fill). Still valuable for finding the tightest area.
- **Strip item can't fill target quantity**: Use the partial result. Leftover quantity is added to remainder items for the remnant fill.
- **Remnant too small**: `NestEngine.Fill` returns empty naturally. No special handling needed.
- **Quantity = 0 (unlimited)**: Initial fill count becomes the shrink loop target.
- **Strip already one part tall**: Skip the shrink loop.

## Dependencies

- `NestEngine.Fill(NestItem, Box)` — existing API, no changes needed.
- `FillScore.Compute` — existing scoring, no changes needed.
- `Part.GetBoundingBox()` / list extensions — existing geometry utilities.
