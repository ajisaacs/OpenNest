# FillExact — Exact-Quantity Fill with Binary Search

## Problem

The current `NestEngine.Fill` fills an entire work area and truncates to `item.Quantity` with `.Take(n)`. This wastes plate space — parts are spread across the full area, leaving no usable remainder strip for subsequent drawings in AutoNest.

## Solution

Add a `FillExact` method that binary-searches for the smallest sub-area of the work area that fits exactly the requested quantity. This packs parts tightly against one edge, maximizing the remainder strip available for the next drawing.

## Coordinate Conventions

`Box.Width` is the X-axis extent. `Box.Length` is the Y-axis extent. The box is anchored at `(Box.X, Box.Y)` (bottom-left corner).

- **Shrink width** means reducing `Box.Width` (X-axis), producing a narrower box anchored at the left edge. The remainder strip extends to the right.
- **Shrink length** means reducing `Box.Length` (Y-axis), producing a shorter box anchored at the bottom edge. The remainder strip extends upward.

## Algorithm

1. **Early exits:**
   - Quantity is 0 (unlimited): delegate to `Fill` directly.
   - Quantity is 1: delegate to `Fill` directly (a single part placement doesn't benefit from area search).
2. **Full fill** — Call `Fill(item, workArea, progress, token)` to establish the upper bound (max parts that fit). This call gets progress reporting so the user sees the phases running.
3. **Already exact or under** — If `fullCount <= quantity`, return the full fill result. The plate can't fit more than requested anyway.
4. **Estimate starting point** — Calculate an initial dimension estimate assuming 50% utilization: `estimatedDim = (partArea * quantity) / (0.5 * fixedDim)`, clamped to at least the part's bounding box dimension in that axis.
5. **Binary search** (max 8 iterations, or until `high - low < partSpacing`) — Keep one dimension of the work area fixed and binary-search on the other:
   - `low = estimatedDim`, `high = workArea dimension`
   - Each iteration: create a test box, call `Fill(item, testBox, null, token)` (no progress — search iterations are silent), check count.
   - `count >= quantity` → record result, shrink: `high = mid`
   - `count < quantity` → expand: `low = mid`
   - Check cancellation token between iterations; if cancelled, return best found so far.
6. **Try both orientations** — Run the binary search twice: once shrinking length (fixed width) and once shrinking width (fixed length).
7. **Pick winner** — Compare by test box area (`testBox.Width * testBox.Length`). Return whichever orientation's result has a smaller test box area, leaving more remainder for subsequent drawings. Tie-break: prefer shrink-length (leaves horizontal remainder strip, generally more useful on wide plates).

## Method Signature

```csharp
// NestEngine.cs
public List<Part> FillExact(NestItem item, Box workArea,
    IProgress<NestProgress> progress, CancellationToken token)
```

Returns exactly `item.Quantity` parts packed into the smallest sub-area of `workArea`, or fewer if they don't all fit.

## Internal Helper

```csharp
private (List<Part> parts, double usedDim) BinarySearchFill(
    NestItem item, Box workArea, bool shrinkWidth,
    CancellationToken token)
```

Performs the binary search for one orientation. Returns the parts and the dimension value at which the exact quantity was achieved. Progress is not passed to inner Fill calls — the search iterations run silently.

## Engine State

Each inner `Fill` call clears `PhaseResults`, `AngleResults`, and overwrites `WinnerPhase`. After the winning Fill call is identified, `FillExact` runs the winner one final time with `progress` so:
- `PhaseResults` / `AngleResults` / `WinnerPhase` reflect the winning fill.
- The progress form shows the final result.

## Integration

### AutoNest (MainForm.RunAutoNest_Click)

Replace `engine.Fill(item, workArea, progress, token)` with `engine.FillExact(item, workArea, progress, token)` for multi-quantity items. The tighter packing means `ComputeRemainderStrip` returns a larger box for subsequent drawings.

### Single-drawing Fill

`FillExact` works for single-drawing fills too. When `item.Quantity` is set, the caller gets a tight layout instead of parts scattered across the full plate.

### Fallback

When `item.Quantity` is 0 (unlimited), `FillExact` falls through to the standard `Fill` behavior — fill the entire work area.

## Performance Notes

The binary search converges in at most 8 iterations per orientation. Each iteration calls `Fill` internally, which runs the pairs/linear/best-fit phases. For a typical auto-nest scenario:

- Full fill: 1 call (with progress)
- Shrink-length search: ~6-8 calls (silent)
- Shrink-width search: ~6-8 calls (silent)
- Final re-fill of winner: 1 call (with progress)
- Total: ~15-19 Fill calls per drawing

The inner `Fill` calls for reduced work areas are faster than full-plate fills since the search space is smaller. The `BestFitCache` (used by the pairs phase) is keyed on the full plate size, so it stays warm across iterations — only the linear/rect phases re-run.

Early termination (`high - low < partSpacing`) typically cuts 1-3 iterations, bringing the total closer to 12-15 calls.

## Edge Cases

- **Quantity 0 (unlimited):** Skip binary search, delegate to `Fill` directly.
- **Quantity 1:** Skip binary search, delegate to `Fill` directly.
- **Full fill already exact:** Return immediately without searching.
- **Part doesn't fit at all:** Return empty list.
- **Binary search can't hit exact count** (e.g., jumps from N-1 to N+2): Take the smallest test box where `count >= quantity` and truncate with `.Take(quantity)`.
- **Cancellation:** Check token between iterations. Return best result found so far.
