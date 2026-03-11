# FillScore Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace raw part-count comparisons with a structured FillScore (count → largest usable remnant → density) and expand remainder strip rotation coverage so denser pair patterns can win.

**Architecture:** New `FillScore` readonly struct with lexicographic comparison. Thread `workArea` parameter through `NestEngine` comparison methods. Expand `FillLinear.FillRemainingStrip` to try 0° and 90° in addition to seed rotations.

**Tech Stack:** .NET 8, C#, OpenNest.Engine

---

## Chunk 1: FillScore and NestEngine Integration

### Task 1: Create FillScore struct

**Files:**
- Create: `OpenNest.Engine/FillScore.cs`

- [ ] **Step 1: Create FillScore.cs**

```csharp
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    public readonly struct FillScore : System.IComparable<FillScore>
    {
        /// <summary>
        /// Minimum short-side dimension for a remnant to be considered usable.
        /// </summary>
        public const double MinRemnantDimension = 12.0;

        public int Count { get; }

        /// <summary>
        /// Area of the largest remnant whose short side >= MinRemnantDimension.
        /// Zero if no usable remnant exists.
        /// </summary>
        public double UsableRemnantArea { get; }

        /// <summary>
        /// Total part area / bounding box area of all placed parts.
        /// </summary>
        public double Density { get; }

        public FillScore(int count, double usableRemnantArea, double density)
        {
            Count = count;
            UsableRemnantArea = usableRemnantArea;
            Density = density;
        }

        /// <summary>
        /// Computes a fill score from placed parts and the work area they were placed in.
        /// </summary>
        public static FillScore Compute(List<Part> parts, Box workArea)
        {
            if (parts == null || parts.Count == 0)
                return default;

            var totalPartArea = 0.0;

            foreach (var part in parts)
                totalPartArea += part.BaseDrawing.Area;

            var bbox = ((IEnumerable<IBoundable>)parts).GetBoundingBox();
            var bboxArea = bbox.Area();
            var density = bboxArea > 0 ? totalPartArea / bboxArea : 0;

            var usableRemnantArea = ComputeUsableRemnantArea(parts, workArea);

            return new FillScore(parts.Count, usableRemnantArea, density);
        }

        /// <summary>
        /// Finds the largest usable remnant (short side >= MinRemnantDimension)
        /// by checking right and top edge strips between placed parts and the work area boundary.
        /// </summary>
        private static double ComputeUsableRemnantArea(List<Part> parts, Box workArea)
        {
            var maxRight = double.MinValue;
            var maxTop = double.MinValue;

            foreach (var part in parts)
            {
                var bb = part.BoundingBox;

                if (bb.Right > maxRight)
                    maxRight = bb.Right;

                if (bb.Top > maxTop)
                    maxTop = bb.Top;
            }

            var largest = 0.0;

            // Right strip
            if (maxRight < workArea.Right)
            {
                var width = workArea.Right - maxRight;
                var height = workArea.Height;

                if (System.Math.Min(width, height) >= MinRemnantDimension)
                    largest = System.Math.Max(largest, width * height);
            }

            // Top strip
            if (maxTop < workArea.Top)
            {
                var width = workArea.Width;
                var height = workArea.Top - maxTop;

                if (System.Math.Min(width, height) >= MinRemnantDimension)
                    largest = System.Math.Max(largest, width * height);
            }

            return largest;
        }

        /// <summary>
        /// Lexicographic comparison: count, then usable remnant area, then density.
        /// </summary>
        public int CompareTo(FillScore other)
        {
            var c = Count.CompareTo(other.Count);

            if (c != 0)
                return c;

            c = UsableRemnantArea.CompareTo(other.UsableRemnantArea);

            if (c != 0)
                return c;

            return Density.CompareTo(other.Density);
        }

        public static bool operator >(FillScore a, FillScore b) => a.CompareTo(b) > 0;
        public static bool operator <(FillScore a, FillScore b) => a.CompareTo(b) < 0;
        public static bool operator >=(FillScore a, FillScore b) => a.CompareTo(b) >= 0;
        public static bool operator <=(FillScore a, FillScore b) => a.CompareTo(b) <= 0;
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build OpenNest.Engine`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/FillScore.cs
git commit -m "feat: add FillScore struct with lexicographic comparison"
```

---

### Task 2: Update NestEngine to use FillScore

**Files:**
- Modify: `OpenNest.Engine/NestEngine.cs`

This task threads `workArea` through the comparison methods and replaces the inline logic with `FillScore`.

- [ ] **Step 1: Replace IsBetterFill**

Replace the existing `IsBetterFill` method (lines 299-315) with:

```csharp
private bool IsBetterFill(List<Part> candidate, List<Part> current, Box workArea)
{
    if (candidate == null || candidate.Count == 0)
        return false;

    if (current == null || current.Count == 0)
        return true;

    return FillScore.Compute(candidate, workArea) > FillScore.Compute(current, workArea);
}
```

- [ ] **Step 2: Replace IsBetterValidFill**

Replace the existing `IsBetterValidFill` method (lines 317-323) with:

```csharp
private bool IsBetterValidFill(List<Part> candidate, List<Part> current, Box workArea)
{
    if (candidate != null && candidate.Count > 0 && HasOverlaps(candidate, Plate.PartSpacing))
        return false;

    return IsBetterFill(candidate, current, workArea);
}
```

- [ ] **Step 3: Update all IsBetterFill call sites in FindBestFill**

In `FindBestFill` (lines 55-121), the `workArea` parameter is already available. Update each call:

```csharp
// Line 95 — was: if (IsBetterFill(h, best))
if (IsBetterFill(h, best, workArea))

// Line 98 — was: if (IsBetterFill(v, best))
if (IsBetterFill(v, best, workArea))

// Line 109 — was: if (IsBetterFill(rectResult, best))
if (IsBetterFill(rectResult, best, workArea))

// Line 117 — was: if (IsBetterFill(pairResult, best))
if (IsBetterFill(pairResult, best, workArea))
```

- [ ] **Step 4: Update IsBetterFill call sites in Fill(NestItem, Box)**

In `Fill(NestItem item, Box workArea)` (lines 32-53):

```csharp
// Line 39 — was: if (IsBetterFill(improved, best))
if (IsBetterFill(improved, best, workArea))
```

- [ ] **Step 5: Update call sites in Fill(List\<Part\>, Box)**

In `Fill(List<Part> groupParts, Box workArea)` (lines 123-166):

```csharp
// Line 141 — was: if (IsBetterFill(rectResult, best))
if (IsBetterFill(rectResult, best, workArea))

// Line 148 — was: if (IsBetterFill(pairResult, best))
if (IsBetterFill(pairResult, best, workArea))

// Line 154 — was: if (IsBetterFill(improved, best))
if (IsBetterFill(improved, best, workArea))
```

- [ ] **Step 6: Update FillPattern to accept and pass workArea**

Change the signature and update calls inside:

```csharp
private List<Part> FillPattern(FillLinear engine, List<Part> groupParts, List<double> angles, Box workArea)
{
    List<Part> best = null;

    foreach (var angle in angles)
    {
        var pattern = BuildRotatedPattern(groupParts, angle);

        if (pattern.Parts.Count == 0)
            continue;

        var h = engine.Fill(pattern, NestDirection.Horizontal);
        var v = engine.Fill(pattern, NestDirection.Vertical);

        if (IsBetterValidFill(h, best, workArea))
            best = h;

        if (IsBetterValidFill(v, best, workArea))
            best = v;
    }

    return best;
}
```

- [ ] **Step 7: Update FillPattern call sites**

Two call sites — both have `workArea` available:

In `Fill(List<Part> groupParts, Box workArea)` (line 130):
```csharp
// was: var best = FillPattern(engine, groupParts, angles);
var best = FillPattern(engine, groupParts, angles, workArea);
```

In `FillWithPairs` (line 216):
```csharp
// was: var filled = FillPattern(engine, pairParts, angles);
var filled = FillPattern(engine, pairParts, angles, workArea);
```

- [ ] **Step 8: Update FillWithPairs to use FillScore**

Replace the `ConcurrentBag` and comparison logic (lines 208-228):

```csharp
var resultBag = new System.Collections.Concurrent.ConcurrentBag<(FillScore score, List<Part> parts)>();

System.Threading.Tasks.Parallel.For(0, candidates.Count, i =>
{
    var result = candidates[i];
    var pairParts = result.BuildParts(item.Drawing);
    var angles = RotationAnalysis.FindHullEdgeAngles(pairParts);
    var engine = new FillLinear(workArea, Plate.PartSpacing);
    var filled = FillPattern(engine, pairParts, angles, workArea);

    if (filled != null && filled.Count > 0)
        resultBag.Add((FillScore.Compute(filled, workArea), filled));
});

List<Part> best = null;
var bestScore = default(FillScore);

foreach (var (score, parts) in resultBag)
{
    if (best == null || score > bestScore)
    {
        best = parts;
        bestScore = score;
    }
}
```

- [ ] **Step 9: Update TryRemainderImprovement call sites**

In `TryRemainderImprovement` (lines 438-456), the method already receives `workArea` — just update the internal `IsBetterFill` calls:

```csharp
// Line 447 — was: if (IsBetterFill(hResult, best))
if (IsBetterFill(hResult, best, workArea))

// Line 452 — was: if (IsBetterFill(vResult, best))
if (IsBetterFill(vResult, best, workArea))
```

- [ ] **Step 10: Update FillWithPairs debug logging**

Update the debug line after the `foreach` loop over `resultBag` (line 230):

```csharp
// was: Debug.WriteLine($"[FillWithPairs] Best pair result: {best?.Count ?? 0} parts");
Debug.WriteLine($"[FillWithPairs] Best pair result: {bestScore.Count} parts, remnant={bestScore.UsableRemnantArea:F1}, density={bestScore.Density:P1}");
```

Also update `FindBestFill` debug line (line 102):

```csharp
// was: Debug.WriteLine($"[FindBestFill] Linear: {best?.Count ?? 0} parts | WorkArea: {workArea.Width:F1}x{workArea.Height:F1} | Angles: {angles.Count}");
var bestLinearScore = best != null ? FillScore.Compute(best, workArea) : default;
Debug.WriteLine($"[FindBestFill] Linear: {bestLinearScore.Count} parts, density={bestLinearScore.Density:P1} | WorkArea: {workArea.Width:F1}x{workArea.Height:F1} | Angles: {angles.Count}");
```

- [ ] **Step 11: Build to verify compilation**

Run: `dotnet build OpenNest.Engine`
Expected: Build succeeded

- [ ] **Step 12: Commit**

```bash
git add OpenNest.Engine/NestEngine.cs
git commit -m "feat: use FillScore for fill result comparisons in NestEngine"
```

**Note — deliberately excluded comparisons:**

- `TryStripRefill` (line 424): `stripParts.Count <= lastCluster.Count` — this is a threshold check ("did the strip refill find more parts than the ragged cluster it replaced?"), not a quality comparison between two complete fills. FillScore is not meaningful here because we're comparing a fill result against a subset of existing parts.
- `FillLinear.FillRemainingStrip` (line 436): internal sub-fill within a strip where remnant quality doesn't apply. Count-only is correct at this level.

---

## Chunk 2: Expanded Remainder Rotations

### Task 3: Expand FillRemainingStrip rotation coverage

**Files:**
- Modify: `OpenNest.Engine/FillLinear.cs`

This is the change that fixes the 45→47 case. Currently `FillRemainingStrip` only tries rotations from the seed pattern. Adding 0° and 90° ensures the remainder strip can discover better orientations.

- [ ] **Step 1: Update FillRemainingStrip rotation loop**

Replace the rotation loop in `FillRemainingStrip` (lines 409-441) with:

```csharp
            // Build rotation set: always try cardinal orientations (0° and 90°),
            // plus any unique rotations from the seed pattern.
            var filler = new FillLinear(remainingStrip, PartSpacing);
            List<Part> best = null;
            var rotations = new List<(Drawing drawing, double rotation)>();

            // Cardinal rotations for each unique drawing.
            var drawings = new List<Drawing>();

            foreach (var seedPart in seedPattern.Parts)
            {
                var found = false;

                foreach (var d in drawings)
                {
                    if (d == seedPart.BaseDrawing)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                    drawings.Add(seedPart.BaseDrawing);
            }

            foreach (var drawing in drawings)
            {
                rotations.Add((drawing, 0));
                rotations.Add((drawing, Angle.HalfPI));
            }

            // Add seed pattern rotations that aren't already covered.
            foreach (var seedPart in seedPattern.Parts)
            {
                var skip = false;

                foreach (var (d, r) in rotations)
                {
                    if (d == seedPart.BaseDrawing && r.IsEqualTo(seedPart.Rotation))
                    {
                        skip = true;
                        break;
                    }
                }

                if (!skip)
                    rotations.Add((seedPart.BaseDrawing, seedPart.Rotation));
            }

            foreach (var (drawing, rotation) in rotations)
            {
                var h = filler.Fill(drawing, rotation, NestDirection.Horizontal);
                var v = filler.Fill(drawing, rotation, NestDirection.Vertical);

                if (h != null && h.Count > 0 && (best == null || h.Count > best.Count))
                    best = h;

                if (v != null && v.Count > 0 && (best == null || v.Count > best.Count))
                    best = v;
            }
```

Note: The comparison inside `FillRemainingStrip` stays as count-only. This is an internal sub-fill within a strip — remnant quality doesn't apply at this level.

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build OpenNest.Engine`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/FillLinear.cs
git commit -m "feat: try cardinal rotations in FillRemainingStrip for better strip fills"
```

---

### Task 4: Full build and manual verification

- [ ] **Step 1: Build entire solution**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded with no errors

- [ ] **Step 2: Manual test with 4980 A24 PT02 nest**

Open the application, load the 4980 A24 PT02 drawing on a 60×120" plate, run Ctrl+F fill. Check Debug output for:
1. Pattern #1 (89.7°) should now get 47 parts via expanded remainder rotations
2. FillScore comparison should pick 47 over 45
3. Verify no overlaps in the result
