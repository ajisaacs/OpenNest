# Strip Nester Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a strip-based multi-drawing nesting strategy as a `NestEngineBase` subclass that dedicates a tight strip to the largest-area drawing and fills the remnant with remaining drawings.

**Architecture:** `StripNestEngine` extends `NestEngineBase`, uses `DefaultNestEngine` internally (composition) for individual fills. Registered in `NestEngineRegistry`. For single-item fills, delegates to `DefaultNestEngine`. For multi-drawing nesting, orchestrates the strip+remnant strategy. The MCP `autonest_plate` tool always runs `StripNestEngine` as a competitor alongside the current sequential approach, picking the denser result.

**Tech Stack:** C# / .NET 8, OpenNest.Engine, OpenNest.Mcp

**Spec:** `docs/superpowers/specs/2026-03-15-strip-nester-design.md`

**Depends on:** `docs/superpowers/plans/2026-03-15-abstract-nest-engine.md` (must be implemented first — provides `NestEngineBase`, `DefaultNestEngine`, `NestEngineRegistry`)

---

## Chunk 1: Core StripNestEngine

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

### Task 3: Create StripNestEngine — class skeleton with selection and estimation helpers

**Files:**
- Create: `OpenNest.Engine/StripNestEngine.cs`

This task creates the class extending `NestEngineBase`, with `Name`/`Description` overrides, the single-item `Fill` override that delegates to `DefaultNestEngine`, and the helper methods for strip item selection and dimension estimation. The main `Nest` method is added in the next task.

- [ ] **Step 1: Create StripNestEngine with skeleton and helpers**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    public class StripNestEngine : NestEngineBase
    {
        private const int MaxShrinkIterations = 20;

        public StripNestEngine(Plate plate) : base(plate)
        {
        }

        public override string Name => "Strip";

        public override string Description => "Strip-based nesting for mixed-drawing layouts";

        /// <summary>
        /// Single-item fill delegates to DefaultNestEngine.
        /// The strip strategy adds value for multi-drawing nesting, not single-item fills.
        /// </summary>
        public override List<Part> Fill(NestItem item, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            var inner = new DefaultNestEngine(Plate);
            return inner.Fill(item, workArea, progress, token);
        }

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
        /// uses DefaultNestEngine.Fill which tries many rotation angles internally.
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
git add OpenNest.Engine/StripNestEngine.cs
git commit -m "feat: add StripNestEngine skeleton with Fill delegate and estimation helpers"
```

---

### Task 4: Add the Nest method and TryOrientation

**Files:**
- Modify: `OpenNest.Engine/StripNestEngine.cs`

This is the main multi-drawing algorithm: tries both orientations, fills strip + remnant, compares results. Uses `DefaultNestEngine` internally for all fill operations (composition pattern per the abstract engine spec).

Key detail: The remnant fill shrinks the remnant box after each item fill using `ComputeRemainderWithin` to prevent overlapping placements.

- [ ] **Step 1: Add Nest, TryOrientation, and ComputeRemainderWithin methods**

Add these methods to the `StripNestEngine` class, after the `EstimateStripDimension` method:

```csharp
        /// <summary>
        /// Multi-drawing strip nesting strategy.
        /// Picks the largest-area drawing for strip treatment, finds the tightest strip
        /// in both bottom and left orientations, fills remnants with remaining drawings,
        /// and returns the denser result.
        /// </summary>
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

            // Initial fill using DefaultNestEngine (composition, not inheritance).
            var inner = new DefaultNestEngine(Plate);
            var stripParts = inner.Fill(
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

                var trialInner = new DefaultNestEngine(Plate);
                var trialParts = trialInner.Fill(
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

                    var remnantInner = new DefaultNestEngine(Plate);
                    var remnantParts = remnantInner.Fill(
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
git add OpenNest.Engine/StripNestEngine.cs
git commit -m "feat: add StripNestEngine.Nest with strip fill, shrink loop, and remnant fill"
```

---

### Task 5: Register StripNestEngine in NestEngineRegistry

**Files:**
- Modify: `OpenNest.Engine/NestEngineRegistry.cs`

- [ ] **Step 1: Add Strip registration**

In `NestEngineRegistry.cs`, add the strip engine registration in the static constructor, after the Default registration:

```csharp
            Register("Strip",
                "Strip-based nesting for mixed-drawing layouts",
                plate => new StripNestEngine(plate));
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/NestEngineRegistry.cs
git commit -m "feat: register StripNestEngine in NestEngineRegistry"
```

---

## Chunk 2: MCP Integration

### Task 6: Integrate StripNestEngine into autonest_plate MCP tool

**Files:**
- Modify: `OpenNest.Mcp/Tools/NestingTools.cs`

Run the strip nester alongside the existing sequential approach. Both use side-effect-free fills (4-arg `Fill` returning `List<Part>`), then the winner's parts are added to the plate.

Note: After the abstract engine migration, callsites already use `NestEngineRegistry.Create(plate)`. The `autonest_plate` tool creates a `StripNestEngine` directly for the strip strategy competition (it's always tried, regardless of active engine selection).

- [ ] **Step 1: Refactor AutoNestPlate to run both strategies**

In `NestingTools.cs`, replace the fill/pack logic in `AutoNestPlate` (the section after the items list is built) with a strategy competition.

Replace the fill/pack logic with:

```csharp
            // Strategy 1: Strip nesting
            var stripEngine = new StripNestEngine(plate);
            var stripResult = stripEngine.Nest(items, null, CancellationToken.None);
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

Update the output section:

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

Add this private method to `NestingTools`. It mirrors the existing sequential fill phase using side-effect-free fills.

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

                var engine = new DefaultNestEngine(plate);
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
git commit -m "feat: integrate StripNestEngine into autonest_plate MCP tool"
```

---

## Chunk 3: Publish and Test

### Task 7: Publish MCP server and test with real parts

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
