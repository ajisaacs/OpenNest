# Remainder Strip Re-Fill Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** After the main fill, detect oddball last column/row, remove it, and re-fill the remainder strip independently to maximize part count (30 -> 32 for the test case).

**Architecture:** Extract the strategy selection logic from `Fill(NestItem, Box)` into a reusable `FindBestFill` method. Add `TryRemainderImprovement` that clusters placed parts, detects oddball last cluster, computes the remainder strip box, and calls `FindBestFill` on it. Only used when it improves the count.

**Tech Stack:** C# / .NET 8, OpenNest.Engine

---

### Task 1: Extract FindBestFill from Fill(NestItem, Box)

**Files:**
- Modify: `OpenNest.Engine/NestEngine.cs:32-105`

**Step 1: Create `FindBestFill` by extracting the strategy logic**

Move lines 34-95 (everything except the quantity check and `Plate.Parts.AddRange`) into a new private method. `Fill` delegates to it.

```csharp
private List<Part> FindBestFill(NestItem item, Box workArea)
{
    var bestRotation = RotationAnalysis.FindBestRotation(item);

    var engine = new FillLinear(workArea, Plate.PartSpacing);

    // Build candidate rotation angles — always try the best rotation and +90°.
    var angles = new List<double> { bestRotation, bestRotation + Angle.HalfPI };

    // When the work area is narrow relative to the part, sweep rotation
    // angles so we can find one that fits the part into the tight strip.
    var testPart = new Part(item.Drawing);

    if (!bestRotation.IsEqualTo(0))
        testPart.Rotate(bestRotation);

    testPart.UpdateBounds();

    var partLongestSide = System.Math.Max(testPart.BoundingBox.Width, testPart.BoundingBox.Height);
    var workAreaShortSide = System.Math.Min(workArea.Width, workArea.Height);

    if (workAreaShortSide < partLongestSide)
    {
        // Try every 5° from 0 to 175° to find rotations that fit.
        var step = Angle.ToRadians(5);

        for (var a = 0.0; a < System.Math.PI; a += step)
        {
            if (!angles.Any(existing => existing.IsEqualTo(a)))
                angles.Add(a);
        }
    }

    List<Part> best = null;

    foreach (var angle in angles)
    {
        var h = engine.Fill(item.Drawing, angle, NestDirection.Horizontal);
        var v = engine.Fill(item.Drawing, angle, NestDirection.Vertical);

        if (IsBetterFill(h, best))
            best = h;

        if (IsBetterFill(v, best))
            best = v;
    }

    Debug.WriteLine($"[FindBestFill] Linear: {best?.Count ?? 0} parts | WorkArea: {workArea.Width:F1}x{workArea.Height:F1} | Angles: {angles.Count}");

    // Try rectangle best-fit (mixes orientations to fill remnant strips).
    var rectResult = FillRectangleBestFit(item, workArea);

    Debug.WriteLine($"[FindBestFill] RectBestFit: {rectResult?.Count ?? 0} parts");

    if (IsBetterFill(rectResult, best))
        best = rectResult;

    // Try pair-based approach.
    var pairResult = FillWithPairs(item, workArea);

    Debug.WriteLine($"[FindBestFill] Pair: {pairResult.Count} parts");

    if (IsBetterFill(pairResult, best))
        best = pairResult;

    return best;
}
```

**Step 2: Simplify `Fill(NestItem, Box)` to delegate**

```csharp
public bool Fill(NestItem item, Box workArea)
{
    var best = FindBestFill(item, workArea);

    if (best == null || best.Count == 0)
        return false;

    if (item.Quantity > 0 && best.Count > item.Quantity)
        best = best.Take(item.Quantity).ToList();

    Plate.Parts.AddRange(best);
    return true;
}
```

**Step 3: Build and verify no regressions**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds, no errors.

**Step 4: Commit**

```bash
git add OpenNest.Engine/NestEngine.cs
git commit -m "refactor: extract FindBestFill from Fill(NestItem, Box)"
```

---

### Task 2: Add ClusterParts helper

**Files:**
- Modify: `OpenNest.Engine/NestEngine.cs`

**Step 1: Add the `ClusterParts` method**

Place after `IsBetterValidFill` (around line 287). Groups parts into positional clusters (columns or rows) based on center position gaps.

```csharp
/// <summary>
/// Groups parts into positional clusters along the given axis.
/// Parts whose center positions are separated by more than half
/// the part dimension start a new cluster.
/// </summary>
private static List<List<Part>> ClusterParts(List<Part> parts, bool horizontal)
{
    var sorted = horizontal
        ? parts.OrderBy(p => p.BoundingBox.Center.X).ToList()
        : parts.OrderBy(p => p.BoundingBox.Center.Y).ToList();

    var refDim = horizontal
        ? sorted.Max(p => p.BoundingBox.Width)
        : sorted.Max(p => p.BoundingBox.Height);
    var gapThreshold = refDim * 0.5;

    var clusters = new List<List<Part>>();
    var current = new List<Part> { sorted[0] };

    for (var i = 1; i < sorted.Count; i++)
    {
        var prevCenter = horizontal
            ? sorted[i - 1].BoundingBox.Center.X
            : sorted[i - 1].BoundingBox.Center.Y;
        var currCenter = horizontal
            ? sorted[i].BoundingBox.Center.X
            : sorted[i].BoundingBox.Center.Y;

        if (currCenter - prevCenter > gapThreshold)
        {
            clusters.Add(current);
            current = new List<Part>();
        }

        current.Add(sorted[i]);
    }

    clusters.Add(current);
    return clusters;
}
```

**Step 2: Build**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add OpenNest.Engine/NestEngine.cs
git commit -m "feat: add ClusterParts helper for positional grouping"
```

---

### Task 3: Add TryStripRefill and TryRemainderImprovement

**Files:**
- Modify: `OpenNest.Engine/NestEngine.cs`

**Step 1: Add `TryStripRefill`**

This method analyzes one axis: clusters parts, checks if last cluster is an oddball, computes the strip, and fills it.

```csharp
/// <summary>
/// Checks whether the last column (horizontal) or row (vertical) is an
/// oddball with fewer parts than the main grid. If so, removes those parts,
/// computes the remainder strip, and fills it independently.
/// Returns null if no improvement is possible.
/// </summary>
private List<Part> TryStripRefill(NestItem item, Box workArea, List<Part> parts, bool horizontal)
{
    var clusters = ClusterParts(parts, horizontal);

    if (clusters.Count < 2)
        return null;

    var lastCluster = clusters[clusters.Count - 1];
    var otherClusters = clusters.Take(clusters.Count - 1).ToList();

    // Find the most common cluster size (mode).
    var modeCount = otherClusters
        .Select(c => c.Count)
        .GroupBy(x => x)
        .OrderByDescending(g => g.Count())
        .First().Key;

    // Only proceed if last cluster is smaller (it's the oddball).
    if (lastCluster.Count >= modeCount)
        return null;

    var mainParts = otherClusters.SelectMany(c => c).ToList();
    var mainBbox = ((IEnumerable<IBoundable>)mainParts).GetBoundingBox();

    Box strip;

    if (horizontal)
    {
        var stripLeft = mainBbox.Right + Plate.PartSpacing;
        var stripWidth = workArea.Right - stripLeft;

        if (stripWidth < 1)
            return null;

        strip = new Box(stripLeft, workArea.Y, stripWidth, workArea.Height);
    }
    else
    {
        var stripBottom = mainBbox.Top + Plate.PartSpacing;
        var stripHeight = workArea.Top - stripBottom;

        if (stripHeight < 1)
            return null;

        strip = new Box(workArea.X, stripBottom, workArea.Width, stripHeight);
    }

    Debug.WriteLine($"[TryStripRefill] {(horizontal ? "H" : "V")} strip: {strip.Width:F1}x{strip.Height:F1} | Main: {mainParts.Count} | Oddball: {lastCluster.Count}");

    var stripParts = FindBestFill(item, strip);

    if (stripParts == null || stripParts.Count <= lastCluster.Count)
        return null;

    Debug.WriteLine($"[TryStripRefill] Strip fill: {stripParts.Count} parts (was {lastCluster.Count} oddball)");

    var combined = new List<Part>(mainParts);
    combined.AddRange(stripParts);
    return combined;
}
```

**Step 2: Add `TryRemainderImprovement`**

Tries both horizontal and vertical strip analysis.

```csharp
/// <summary>
/// Attempts to improve a fill result by detecting an oddball last
/// column or row and re-filling the remainder strip independently.
/// Returns null if no improvement is found.
/// </summary>
private List<Part> TryRemainderImprovement(NestItem item, Box workArea, List<Part> currentBest)
{
    if (currentBest == null || currentBest.Count < 3)
        return null;

    List<Part> bestImproved = null;

    var hImproved = TryStripRefill(item, workArea, currentBest, horizontal: true);

    if (IsBetterFill(hImproved, bestImproved))
        bestImproved = hImproved;

    var vImproved = TryStripRefill(item, workArea, currentBest, horizontal: false);

    if (IsBetterFill(vImproved, bestImproved))
        bestImproved = vImproved;

    return bestImproved;
}
```

**Step 3: Build**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds.

**Step 4: Commit**

```bash
git add OpenNest.Engine/NestEngine.cs
git commit -m "feat: add TryStripRefill and TryRemainderImprovement"
```

---

### Task 4: Wire remainder improvement into Fill

**Files:**
- Modify: `OpenNest.Engine/NestEngine.cs` — the `Fill(NestItem, Box)` method

**Step 1: Add remainder improvement call**

Update `Fill(NestItem, Box)` to try improving the result after the initial fill:

```csharp
public bool Fill(NestItem item, Box workArea)
{
    var best = FindBestFill(item, workArea);

    // Try improving by filling the remainder strip separately.
    var improved = TryRemainderImprovement(item, workArea, best);

    if (IsBetterFill(improved, best))
    {
        Debug.WriteLine($"[Fill] Remainder improvement: {improved.Count} parts (was {best?.Count ?? 0})");
        best = improved;
    }

    if (best == null || best.Count == 0)
        return false;

    if (item.Quantity > 0 && best.Count > item.Quantity)
        best = best.Take(item.Quantity).ToList();

    Plate.Parts.AddRange(best);
    return true;
}
```

**Step 2: Build**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add OpenNest.Engine/NestEngine.cs
git commit -m "feat: wire remainder strip re-fill into Fill(NestItem, Box)"
```

---

### Task 5: Verify with MCP tools

**Step 1: Publish MCP server**

```bash
dotnet publish OpenNest.Mcp/OpenNest.Mcp.csproj -c Release -o "$USERPROFILE/.claude/mcp/OpenNest.Mcp"
```

**Step 2: Test fill**

Use MCP tools to:
1. Import the DXF drawing from `30pcs Fill.zip` (or create equivalent plate + drawing)
2. Create a 96x48 plate with the same spacing (part=0.25, edges L=0.25 B=0.75 R=0.25 T=0.25)
3. Fill the plate
4. Verify part count is 32 (up from 30)
5. Check for overlaps

**Step 3: Compare against 32pcs reference**

Verify the layout matches the 32pcs.zip reference — 24 parts in the main grid + 8 in the remainder strip.

**Step 4: Final commit if any fixups needed**
