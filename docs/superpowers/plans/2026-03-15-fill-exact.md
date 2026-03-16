# FillExact Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `FillExact` method to `NestEngine` that binary-searches for the smallest work area sub-region that fits an exact quantity of parts, then integrate it into AutoNest.

**Architecture:** `FillExact` wraps the existing `Fill(NestItem, Box, IProgress, CancellationToken)` method. It calls Fill repeatedly with progressively smaller test boxes (binary search on one dimension, both orientations), picks the tightest fit, then re-runs the winner with progress reporting. Callers swap `Fill` for `FillExact` — no other engine changes needed.

**Tech Stack:** C# / .NET 8, OpenNest.Engine, OpenNest (WinForms), OpenNest.Console, OpenNest.Mcp

**Spec:** `docs/superpowers/specs/2026-03-15-fill-exact-design.md`

---

## Chunk 1: Core Implementation

### Task 1: Add `BinarySearchFill` helper to NestEngine

**Files:**
- Modify: `OpenNest.Engine/NestEngine.cs` (add private method after the existing `Fill` overloads, around line 85)

- [ ] **Step 1: Add the BinarySearchFill method**

Add after the `Fill(NestItem, Box, IProgress, CancellationToken)` method (line 85):

```csharp
/// <summary>
/// Binary-searches for the smallest sub-area (one dimension fixed) that fits
/// exactly item.Quantity parts. Returns the best parts list and the dimension
/// value that achieved it.
/// </summary>
private (List<Part> parts, double usedDim) BinarySearchFill(
    NestItem item, Box workArea, bool shrinkWidth,
    CancellationToken token)
{
    var quantity = item.Quantity;
    var partBox = item.Drawing.Program.BoundingBox();
    var partArea = item.Drawing.Area;

    // Fixed and variable dimensions.
    var fixedDim = shrinkWidth ? workArea.Length : workArea.Width;
    var highDim = shrinkWidth ? workArea.Width : workArea.Length;

    // Estimate starting point: target area at 50% utilization.
    var targetArea = partArea * quantity / 0.5;
    var minPartDim = shrinkWidth
        ? partBox.Width + Plate.PartSpacing
        : partBox.Length + Plate.PartSpacing;
    var estimatedDim = System.Math.Max(minPartDim, targetArea / fixedDim);

    var low = estimatedDim;
    var high = highDim;

    List<Part> bestParts = null;
    var bestDim = high;

    for (var iter = 0; iter < 8; iter++)
    {
        if (token.IsCancellationRequested)
            break;

        if (high - low < Plate.PartSpacing)
            break;

        var mid = (low + high) / 2.0;

        var testBox = shrinkWidth
            ? new Box(workArea.X, workArea.Y, mid, workArea.Length)
            : new Box(workArea.X, workArea.Y, workArea.Width, mid);

        var result = Fill(item, testBox, null, token);

        if (result.Count >= quantity)
        {
            bestParts = result.Count > quantity
                ? result.Take(quantity).ToList()
                : result;
            bestDim = mid;
            high = mid;
        }
        else
        {
            low = mid;
        }
    }

    return (bestParts, bestDim);
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj --nologo -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/NestEngine.cs
git commit -m "feat(engine): add BinarySearchFill helper for exact-quantity search"
```

---

### Task 2: Add `FillExact` public method to NestEngine

**Files:**
- Modify: `OpenNest.Engine/NestEngine.cs` (add public method after the existing `Fill` overloads, before `BinarySearchFill`)

- [ ] **Step 1: Add the FillExact method**

Add between the `Fill(NestItem, Box, IProgress, CancellationToken)` method and `BinarySearchFill`:

```csharp
/// <summary>
/// Finds the smallest sub-area of workArea that fits exactly item.Quantity parts.
/// Uses binary search on both orientations and picks the tightest fit.
/// Falls through to standard Fill for unlimited (0) or single (1) quantities.
/// </summary>
public List<Part> FillExact(NestItem item, Box workArea,
    IProgress<NestProgress> progress, CancellationToken token)
{
    // Early exits: unlimited or single quantity — no benefit from area search.
    if (item.Quantity <= 1)
        return Fill(item, workArea, progress, token);

    // Full fill to establish upper bound.
    var fullResult = Fill(item, workArea, progress, token);

    if (fullResult.Count <= item.Quantity)
        return fullResult;

    // Binary search: try shrinking each dimension.
    var (lengthParts, lengthDim) = BinarySearchFill(item, workArea, shrinkWidth: false, token);
    var (widthParts, widthDim) = BinarySearchFill(item, workArea, shrinkWidth: true, token);

    // Pick winner by smallest test box area. Tie-break: prefer shrink-length.
    List<Part> winner;
    Box winnerBox;

    var lengthArea = lengthParts != null ? workArea.Width * lengthDim : double.MaxValue;
    var widthArea = widthParts != null ? widthDim * workArea.Length : double.MaxValue;

    if (lengthParts != null && lengthArea <= widthArea)
    {
        winner = lengthParts;
        winnerBox = new Box(workArea.X, workArea.Y, workArea.Width, lengthDim);
    }
    else if (widthParts != null)
    {
        winner = widthParts;
        winnerBox = new Box(workArea.X, workArea.Y, widthDim, workArea.Length);
    }
    else
    {
        // Neither search found the exact quantity — return full fill truncated.
        return fullResult.Take(item.Quantity).ToList();
    }

    // Re-run the winner with progress so PhaseResults/WinnerPhase are correct
    // and the progress form shows the final result.
    var finalResult = Fill(item, winnerBox, progress, token);

    if (finalResult.Count >= item.Quantity)
        return finalResult.Count > item.Quantity
            ? finalResult.Take(item.Quantity).ToList()
            : finalResult;

    // Fallback: return the binary search result if the re-run produced fewer.
    return winner;
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj --nologo -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/NestEngine.cs
git commit -m "feat(engine): add FillExact method for exact-quantity nesting"
```

---

### Task 3: Add Compactor class to Engine

**Files:**
- Create: `OpenNest.Engine/Compactor.cs`

- [ ] **Step 1: Create the Compactor class**

Create `OpenNest.Engine/Compactor.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest
{
    /// <summary>
    /// Pushes a group of parts left and down to close gaps after placement.
    /// Uses the same directional-distance logic as PlateView.PushSelected
    /// but operates on Part objects directly.
    /// </summary>
    public static class Compactor
    {
        private const double ChordTolerance = 0.001;

        /// <summary>
        /// Compacts movingParts toward the bottom-left of the plate work area.
        /// Everything already on the plate (excluding movingParts) is treated
        /// as stationary obstacles.
        /// </summary>
        public static void Compact(List<Part> movingParts, Plate plate)
        {
            if (movingParts == null || movingParts.Count == 0)
                return;

            Push(movingParts, plate, PushDirection.Left);
            Push(movingParts, plate, PushDirection.Down);
        }

        private static void Push(List<Part> movingParts, Plate plate, PushDirection direction)
        {
            var stationaryParts = plate.Parts
                .Where(p => !movingParts.Contains(p))
                .ToList();

            var stationaryBoxes = new Box[stationaryParts.Count];

            for (var i = 0; i < stationaryParts.Count; i++)
                stationaryBoxes[i] = stationaryParts[i].BoundingBox;

            var stationaryLines = new List<Line>[stationaryParts.Count];
            var opposite = Helper.OppositeDirection(direction);
            var halfSpacing = plate.PartSpacing / 2;
            var isHorizontal = Helper.IsHorizontalDirection(direction);
            var workArea = plate.WorkArea();

            foreach (var moving in movingParts)
            {
                var distance = double.MaxValue;
                var movingBox = moving.BoundingBox;

                // Plate edge distance.
                var edgeDist = Helper.EdgeDistance(movingBox, workArea, direction);
                if (edgeDist > 0 && edgeDist < distance)
                    distance = edgeDist;

                List<Line> movingLines = null;

                for (var i = 0; i < stationaryBoxes.Length; i++)
                {
                    var gap = Helper.DirectionalGap(movingBox, stationaryBoxes[i], direction);
                    if (gap < 0 || gap >= distance)
                        continue;

                    var perpOverlap = isHorizontal
                        ? movingBox.IsHorizontalTo(stationaryBoxes[i], out _)
                        : movingBox.IsVerticalTo(stationaryBoxes[i], out _);

                    if (!perpOverlap)
                        continue;

                    movingLines ??= halfSpacing > 0
                        ? Helper.GetOffsetPartLines(moving, halfSpacing, direction, ChordTolerance)
                        : Helper.GetPartLines(moving, direction, ChordTolerance);

                    stationaryLines[i] ??= halfSpacing > 0
                        ? Helper.GetOffsetPartLines(stationaryParts[i], halfSpacing, opposite, ChordTolerance)
                        : Helper.GetPartLines(stationaryParts[i], opposite, ChordTolerance);

                    var d = Helper.DirectionalDistance(movingLines, stationaryLines[i], direction);
                    if (d < distance)
                        distance = d;
                }

                if (distance < double.MaxValue && distance > 0)
                {
                    var offset = Helper.DirectionToOffset(direction, distance);
                    moving.Offset(offset);

                    // Update this part's bounding box in the stationary set for
                    // subsequent moving parts to collide against correctly.
                    // (Parts already pushed become obstacles for the next part.)
                }
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj --nologo -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/Compactor.cs
git commit -m "feat(engine): add Compactor for post-fill gravity compaction"
```

---

## Chunk 2: Integration

### Task 4: Integrate FillExact and Compactor into AutoNest (MainForm)

**Files:**
- Modify: `OpenNest/Forms/MainForm.cs` (RunAutoNest_Click, around lines 797-815)

- [ ] **Step 1: Replace Fill with FillExact and add Compactor call**

In `RunAutoNest_Click`, change the Fill call and the block after it (around lines 799-815). Replace:

```csharp
                        var parts = await Task.Run(() =>
                            engine.Fill(item, workArea, progress, token));
```

with:

```csharp
                        var parts = await Task.Run(() =>
                            engine.FillExact(item, workArea, progress, token));
```

Then after `plate.Parts.AddRange(parts);` and before `ComputeRemainderStrip`, add the compaction call:

```csharp
                            plate.Parts.AddRange(parts);
                            Compactor.Compact(parts, plate);
                            activeForm.PlateView.Invalidate();
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build OpenNest.sln --nologo -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add OpenNest/Forms/MainForm.cs
git commit -m "feat(ui): use FillExact + Compactor in AutoNest"
```

---

### Task 5: Integrate FillExact and Compactor into Console app

**Files:**
- Modify: `OpenNest.Console/Program.cs` (around lines 346-360)

- [ ] **Step 1: Replace Fill with FillExact and add Compactor call**

Change the Fill call (around line 352) from:

```csharp
                var parts = engine.Fill(item, workArea, null, CancellationToken.None);
```

to:

```csharp
                var parts = engine.FillExact(item, workArea, null, CancellationToken.None);
```

Then after `plate.Parts.AddRange(parts);` add the compaction call:

```csharp
                    plate.Parts.AddRange(parts);
                    Compactor.Compact(parts, plate);
                    item.Quantity = System.Math.Max(0, item.Quantity - parts.Count);
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build OpenNest.Console/OpenNest.Console.csproj --nologo -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Console/Program.cs
git commit -m "feat(console): use FillExact + Compactor in --autonest"
```

---

### Task 6: Integrate FillExact and Compactor into MCP server

**Files:**
- Modify: `OpenNest.Mcp/Tools/NestingTools.cs` (around lines 255-264)

- [ ] **Step 1: Replace Fill with FillExact and add Compactor call**

Change the Fill call (around line 256) from:

```csharp
                var parts = engine.Fill(item, workArea, null, CancellationToken.None);
```

to:

```csharp
                var parts = engine.FillExact(item, workArea, null, CancellationToken.None);
```

Then after `plate.Parts.AddRange(parts);` add the compaction call:

```csharp
                    plate.Parts.AddRange(parts);
                    Compactor.Compact(parts, plate);
                    item.Quantity = System.Math.Max(0, item.Quantity - parts.Count);
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build OpenNest.Mcp/OpenNest.Mcp.csproj --nologo -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Mcp/Tools/NestingTools.cs
git commit -m "feat(mcp): use FillExact in autonest_plate for tighter packing"
```

---

## Chunk 3: Verification

### Task 7: End-to-end test via Console

- [ ] **Step 1: Run AutoNest with qty > 1 and verify tighter packing**

Run: `dotnet run --project OpenNest.Console/OpenNest.Console.csproj -- --autonest --quantity 10 --no-save "C:\Users\AJ\Desktop\N0312-002.zip"`

Verify:
- Completes without error
- Parts placed count is reasonable (not 0, not wildly over-placed)
- Utilization is reported

- [ ] **Step 2: Run with qty=1 to verify fallback path**

Run: `dotnet run --project OpenNest.Console/OpenNest.Console.csproj -- --autonest --no-save "C:\Users\AJ\Desktop\N0312-002.zip"`

Verify:
- Completes quickly (qty=1 goes through Pack, no binary search)
- Parts placed > 0

- [ ] **Step 3: Build full solution one final time**

Run: `dotnet build OpenNest.sln --nologo -v q`
Expected: `Build succeeded. 0 Error(s)`
