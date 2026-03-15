# Strip Nester Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a strip-based multi-drawing nesting strategy that dedicates a tight strip to the largest-area drawing and fills the remnant with remaining drawings.

**Architecture:** New `StripNester` class in `OpenNest.Engine` that orchestrates strip optimization using `NestEngine.Fill` as a building block. Tries bottom and left strip orientations, finds the tightest strip via a shrink loop, fills remnants with remaining items, and picks the denser result. Integrated into `NestingTools` MCP as an additional strategy in `autonest_plate`.

**Tech Stack:** C# / .NET 8, OpenNest.Engine, OpenNest.Mcp

**Spec:** `docs/superpowers/specs/2026-03-15-strip-nester-design.md`

---

## Chunk 1: Core StripNester

### Task 1: Create StripDirection enum

**Files:**
- Create: `OpenNest.Engine/StripDirection.cs`

- [ ] **Step 1: Create the enum file**

```csharp
namespace OpenNest
{
    public enum StripDirection
    {
        Bottom,
        Left
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/StripDirection.cs
git commit -m "feat: add StripDirection enum"
```

---

### Task 2: Create StripNestResult internal class

**Files:**
- Create: `OpenNest.Engine/StripNestResult.cs`

- [ ] **Step 1: Create the result class**

```csharp
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest
{
    internal class StripNestResult
    {
        public List<Part> Parts { get; set; } = new();
        public Box StripBox { get; set; }
        public Box RemnantBox { get; set; }
        public FillScore Score { get; set; }
        public StripDirection Direction { get; set; }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/StripNestResult.cs
git commit -m "feat: add StripNestResult internal class"
```

---

### Task 3: Create StripNester class — strip item selection and initial strip height estimation

**Files:**
- Create: `OpenNest.Engine/StripNester.cs`

This task creates the class with the constructor and the helper methods for selecting the strip item and estimating the initial strip dimensions. The main `Nest` method is added in the next task.

- [ ] **Step 1: Create StripNester with selection and estimation logic**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    public class StripNester
    {
        private const int MaxShrinkIterations = 20;

        public StripNester(Plate plate)
        {
            Plate = plate;
        }

        public Plate Plate { get; }

        /// <summary>
        /// Selects the item that consumes the most plate area (bounding box area x quantity).
        /// Returns the index into the items list.
        /// </summary>
        private static int SelectStripItemIndex(List<NestItem> items, Box workArea)
        {
            var bestIndex = 0;
            var bestArea = 0.0;

            for (var i = 0; i < items.Count; i++)
            {
                var bbox = items[i].Drawing.Program.BoundingBox();
                var qty = items[i].Quantity > 0
                    ? items[i].Quantity
                    : (int)(workArea.Area() / bbox.Area());
                var totalArea = bbox.Area() * qty;

                if (totalArea > bestArea)
                {
                    bestArea = totalArea;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        /// <summary>
        /// Estimates the strip dimension (height for bottom, width for left) needed
        /// to fit the target quantity. Tries 0 deg and 90 deg rotations and picks the shorter.
        /// This is only an estimate for the shrink loop starting point — the actual fill
        /// uses NestEngine.Fill which tries many rotation angles internally.
        /// </summary>
        private static double EstimateStripDimension(NestItem item, double stripLength, double maxDimension)
        {
            var bbox = item.Drawing.Program.BoundingBox();
            var qty = item.Quantity > 0
                ? item.Quantity
                : System.Math.Max(1, (int)(stripLength * maxDimension / bbox.Area()));

            // At 0 deg: parts per row along strip length, strip dimension is bbox.Length
            var perRow0 = (int)(stripLength / bbox.Width);
            var rows0 = perRow0 > 0 ? (int)System.Math.Ceiling((double)qty / perRow0) : int.MaxValue;
            var dim0 = rows0 * bbox.Length;

            // At 90 deg: rotated bounding box (Width and Length swap)
            var perRow90 = (int)(stripLength / bbox.Length);
            var rows90 = perRow90 > 0 ? (int)System.Math.Ceiling((double)qty / perRow90) : int.MaxValue;
            var dim90 = rows90 * bbox.Width;

            var estimate = System.Math.Min(dim0, dim90);

            // Clamp to available dimension
            return System.Math.Min(estimate, maxDimension);
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/StripNester.cs
git commit -m "feat: add StripNester with strip selection and estimation"
```

---

### Task 4: Add the core Nest method and TryOrientation

**Files:**
- Modify: `OpenNest.Engine/StripNester.cs`

This is the main algorithm: tries both orientations, fills strip + remnant, compares results.

Key detail: The remnant fill must shrink the remnant box after each item fill using `ComputeRemainderWithin` (same pattern as `AutoNestPlate` in `NestingTools.cs:293-306`) to prevent overlapping placements.

- [ ] **Step 1: Add Nest, TryOrientation, and ComputeRemainderWithin methods**

Add these methods to the `StripNester` class, after the `EstimateStripDimension` method:

```csharp
        public List<Part> Nest(List<NestItem> items,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            if (items == null || items.Count == 0)
                return new List<Part>();

            var workArea = Plate.WorkArea();

            // Select which item gets the strip treatment.
            var stripIndex = SelectStripItemIndex(items, workArea);
            var stripItem = items[stripIndex];
            var remainderItems = items.Where((_, i) => i != stripIndex).ToList();

            // Try both orientations.
            var bottomResult = TryOrientation(StripDirection.Bottom, stripItem, remainderItems, workArea, token);
            var leftResult = TryOrientation(StripDirection.Left, stripItem, remainderItems, workArea, token);

            // Pick the better result.
            if (bottomResult.Score >= leftResult.Score)
                return bottomResult.Parts;

            return leftResult.Parts;
        }

        private StripNestResult TryOrientation(StripDirection direction, NestItem stripItem,
            List<NestItem> remainderItems, Box workArea, CancellationToken token)
        {
            var result = new StripNestResult { Direction = direction };

            if (token.IsCancellationRequested)
                return result;

            // Estimate initial strip dimension.
            var stripLength = direction == StripDirection.Bottom ? workArea.Width : workArea.Length;
            var maxDimension = direction == StripDirection.Bottom ? workArea.Length : workArea.Width;
            var estimatedDim = EstimateStripDimension(stripItem, stripLength, maxDimension);

            // Create the initial strip box.
            var stripBox = direction == StripDirection.Bottom
                ? new Box(workArea.X, workArea.Y, workArea.Width, estimatedDim)
                : new Box(workArea.X, workArea.Y, estimatedDim, workArea.Length);

            // Initial fill (does NOT add to plate — uses the 4-arg overload).
            var engine = new NestEngine(Plate);
            var stripParts = engine.Fill(
                new NestItem { Drawing = stripItem.Drawing, Quantity = stripItem.Quantity },
                stripBox, null, token);

            if (stripParts == null || stripParts.Count == 0)
                return result;

            // Measure actual strip dimension from placed parts.
            var placedBox = stripParts.Cast<IBoundable>().GetBoundingBox();
            var actualDim = direction == StripDirection.Bottom
                ? placedBox.Top - workArea.Y
                : placedBox.Right - workArea.X;

            var bestParts = stripParts;
            var bestDim = actualDim;
            var targetCount = stripParts.Count;

            // Shrink loop: reduce strip dimension by PartSpacing until count drops.
            for (var i = 0; i < MaxShrinkIterations; i++)
            {
                if (token.IsCancellationRequested)
                    break;

                var trialDim = bestDim - Plate.PartSpacing;
                if (trialDim <= 0)
                    break;

                var trialBox = direction == StripDirection.Bottom
                    ? new Box(workArea.X, workArea.Y, workArea.Width, trialDim)
                    : new Box(workArea.X, workArea.Y, trialDim, workArea.Length);

                var trialEngine = new NestEngine(Plate);
                var trialParts = trialEngine.Fill(
                    new NestItem { Drawing = stripItem.Drawing, Quantity = stripItem.Quantity },
                    trialBox, null, token);

                if (trialParts == null || trialParts.Count < targetCount)
                    break;

                // Same count in a tighter strip — keep going.
                bestParts = trialParts;
                var trialPlacedBox = trialParts.Cast<IBoundable>().GetBoundingBox();
                bestDim = direction == StripDirection.Bottom
                    ? trialPlacedBox.Top - workArea.Y
                    : trialPlacedBox.Right - workArea.X;
            }

            // Build remnant box with spacing gap.
            var spacing = Plate.PartSpacing;
            var remnantBox = direction == StripDirection.Bottom
                ? new Box(workArea.X, workArea.Y + bestDim + spacing,
                    workArea.Width, workArea.Length - bestDim - spacing)
                : new Box(workArea.X + bestDim + spacing, workArea.Y,
                    workArea.Width - bestDim - spacing, workArea.Length);

            // Collect all parts.
            var allParts = new List<Part>(bestParts);

            // If strip item was only partially placed, add leftovers to remainder.
            var placed = bestParts.Count;
            var leftover = stripItem.Quantity > 0 ? stripItem.Quantity - placed : 0;
            var effectiveRemainder = new List<NestItem>(remainderItems);

            if (leftover > 0)
            {
                effectiveRemainder.Add(new NestItem
                {
                    Drawing = stripItem.Drawing,
                    Quantity = leftover
                });
            }

            // Sort remainder by descending bounding box area x quantity.
            effectiveRemainder = effectiveRemainder
                .OrderByDescending(i =>
                {
                    var bb = i.Drawing.Program.BoundingBox();
                    return bb.Area() * (i.Quantity > 0 ? i.Quantity : 1);
                })
                .ToList();

            // Fill remnant with remainder items, shrinking the available area after each.
            if (remnantBox.Width > 0 && remnantBox.Length > 0)
            {
                var currentRemnant = remnantBox;

                foreach (var item in effectiveRemainder)
                {
                    if (token.IsCancellationRequested)
                        break;

                    if (currentRemnant.Width <= 0 || currentRemnant.Length <= 0)
                        break;

                    var remnantEngine = new NestEngine(Plate);
                    var remnantParts = remnantEngine.Fill(
                        new NestItem { Drawing = item.Drawing, Quantity = item.Quantity },
                        currentRemnant, null, token);

                    if (remnantParts != null && remnantParts.Count > 0)
                    {
                        allParts.AddRange(remnantParts);

                        // Shrink remnant to avoid overlap with next item.
                        var usedBox = remnantParts.Cast<IBoundable>().GetBoundingBox();
                        currentRemnant = ComputeRemainderWithin(currentRemnant, usedBox, spacing);
                    }
                }
            }

            result.Parts = allParts;
            result.StripBox = direction == StripDirection.Bottom
                ? new Box(workArea.X, workArea.Y, workArea.Width, bestDim)
                : new Box(workArea.X, workArea.Y, bestDim, workArea.Length);
            result.RemnantBox = remnantBox;
            result.Score = FillScore.Compute(allParts, workArea);

            return result;
        }

        /// <summary>
        /// Computes the largest usable remainder within a work area after a portion has been used.
        /// Picks whichever is larger: the horizontal strip to the right, or the vertical strip above.
        /// </summary>
        private static Box ComputeRemainderWithin(Box workArea, Box usedBox, double spacing)
        {
            var hWidth = workArea.Right - usedBox.Right - spacing;
            var hStrip = hWidth > 0
                ? new Box(usedBox.Right + spacing, workArea.Y, hWidth, workArea.Length)
                : Box.Empty;

            var vHeight = workArea.Top - usedBox.Top - spacing;
            var vStrip = vHeight > 0
                ? new Box(workArea.X, usedBox.Top + spacing, workArea.Width, vHeight)
                : Box.Empty;

            return hStrip.Area() >= vStrip.Area() ? hStrip : vStrip;
        }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/StripNester.cs
git commit -m "feat: add StripNester.Nest with strip fill, shrink loop, and remnant fill"
```

---

## Chunk 2: MCP Integration

### Task 5: Integrate StripNester into autonest_plate MCP tool

**Files:**
- Modify: `OpenNest.Mcp/Tools/NestingTools.cs`

Run the strip nester alongside the existing sequential approach. Both use the 4-arg `Fill` overload (no side effects), then the winner's parts are added to the plate.

- [ ] **Step 1: Refactor AutoNestPlate to run both strategies**

In `NestingTools.cs`, replace the fill/pack logic in `AutoNestPlate` (lines 236-278) with a strategy competition. The existing sequential logic is extracted to a `SequentialFill` helper.

Replace lines 236-278 with:

```csharp
            // Strategy 1: Strip nesting
            var stripNester = new StripNester(plate);
            var stripResult = stripNester.Nest(items, null, CancellationToken.None);
            var stripScore = FillScore.Compute(stripResult, plate.WorkArea());

            // Strategy 2: Current sequential fill
            var seqResult = SequentialFill(plate, items);
            var seqScore = FillScore.Compute(seqResult, plate.WorkArea());

            // Pick winner and apply to plate.
            var winner = stripScore >= seqScore ? stripResult : seqResult;
            var winnerName = stripScore >= seqScore ? "strip" : "sequential";
            plate.Parts.AddRange(winner);
            var totalPlaced = winner.Count;
```

Update the output section (around line 280):

```csharp
            var sb = new StringBuilder();
            sb.AppendLine($"AutoNest plate {plateIndex} ({winnerName} strategy): {(totalPlaced > 0 ? "success" : "no parts placed")}");
            sb.AppendLine($"  Parts placed: {totalPlaced}");
            sb.AppendLine($"  Total parts: {plate.Parts.Count}");
            sb.AppendLine($"  Utilization: {plate.Utilization():P1}");
            sb.AppendLine($"  Strip score: {stripScore.Count} parts, density {stripScore.Density:P1}");
            sb.AppendLine($"  Sequential score: {seqScore.Count} parts, density {seqScore.Density:P1}");

            var groups = plate.Parts.GroupBy(p => p.BaseDrawing.Name);
            foreach (var group in groups)
                sb.AppendLine($"  {group.Key}: {group.Count()}");

            return sb.ToString();
```

- [ ] **Step 2: Add the SequentialFill helper method**

Add this private method to `NestingTools`. It mirrors the existing `AutoNestPlate` fill phase using the 4-arg `Fill` overload for side-effect-free comparison.

```csharp
        private static List<Part> SequentialFill(Plate plate, List<NestItem> items)
        {
            var fillItems = items
                .Where(i => i.Quantity != 1)
                .OrderBy(i => i.Priority)
                .ThenByDescending(i => i.Drawing.Area)
                .ToList();

            var workArea = plate.WorkArea();
            var allParts = new List<Part>();

            foreach (var item in fillItems)
            {
                if (item.Quantity == 0 || workArea.Width <= 0 || workArea.Length <= 0)
                    continue;

                var engine = new NestEngine(plate);
                var parts = engine.Fill(
                    new NestItem { Drawing = item.Drawing, Quantity = item.Quantity },
                    workArea, null, CancellationToken.None);

                if (parts.Count > 0)
                {
                    allParts.AddRange(parts);
                    var placedBox = parts.Cast<IBoundable>().GetBoundingBox();
                    workArea = ComputeRemainderWithin(workArea, placedBox, plate.PartSpacing);
                }
            }

            return allParts;
        }
```

- [ ] **Step 3: Add required using statement**

Add `using System.Threading;` to the top of `NestingTools.cs` if not already present.

- [ ] **Step 4: Build the full solution**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Mcp/Tools/NestingTools.cs
git commit -m "feat: integrate StripNester into autonest_plate MCP tool"
```

---

## Chunk 3: Publish and Test

### Task 6: Publish MCP server and test with real parts

**Files:**
- No code changes — publish and manual testing

- [ ] **Step 1: Publish OpenNest.Mcp**

Run: `dotnet publish OpenNest.Mcp/OpenNest.Mcp.csproj -c Release -o "$USERPROFILE/.claude/mcp/OpenNest.Mcp"`
Expected: Build and publish succeeded

- [ ] **Step 2: Test with SULLYS parts**

Using the MCP tools, test the strip nester with the SULLYS-001 and SULLYS-002 parts:

1. Load the test nest file or import the DXF files
2. Create a 60x120 plate
3. Run `autonest_plate` with both drawings at qty 10
4. Verify the output reports which strategy won (strip vs sequential)
5. Verify the output shows scores for both strategies
6. Check plate info for part placement and utilization

- [ ] **Step 3: Compare with current results**

Verify the strip nester produces a result matching or improving on the target layout from screenshot 190519 (all 20 parts on one 60x120 plate with organized strip arrangement).

- [ ] **Step 4: Commit any fixes**

If issues are found during testing, fix and commit with descriptive messages.
